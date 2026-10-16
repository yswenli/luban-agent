# LubanAgent.Plugin.NewProject（新建项目插件）

> PRD：`docs/PRD-插件体系与新建项目插件.md`（v0.18）· 里程碑 **M4**
> 插件标识：`com.tjces.newproject` · 显示名：新建项目

以 TJCES 三端模板仓创建新项目：**七步向导**（左侧栏面板）+ **聊天式入口**（`newproject-*` 技能），
两条入口共用同一个 `INewProjectService`，云效逻辑零分叉。

## 目录结构

```
LubanAgent.Plugin.NewProject/
├─ LubanAgent.Plugin.NewProject.csproj   # 含「铺包」目标（见下）
├─ plugin.json                           # 插件清单：侧栏条目 / 6 个技能 / yunxiao MCP / 权限
├─ newproject.settings.json              # 模板 pin、参考流水线、命名与推送分支约定
├─ NewProjectPlugin.cs                   # 插件入口（PluginBase）
├─ Abstractions/
│  ├─ ProjectSpec.cs                     # 项目档案与相关模型（PRD §4.5）
│  ├─ BasicInfoValidation.cs             # Step 1 校验结果（字段级错误 + 非阻断提示，面板与聊天入口共用）
│  └─ INewProjectService.cs              # 共享能力层契约（PRD §4.10.2）
├─ Configuration/NewProjectSettings.cs   # 包内配置读取（模板对照 / 参考流水线）
├─ Serialization/PluginJsonContext.cs    # 源生成 JSON 上下文（插件持久化的唯一入口，见下）
├─ Services/
│  ├─ NewProjectService.cs               # 共享能力层实现（P3~P5 分批补齐）
│  └─ NewProjectStateStore.cs            # 档案持久化（残留检测 / 孤儿清单）
├─ Ui/WizardPanel.cs                     # 向导面板（UserControl + IPluginPanel，纯代码 UI）
└─ skills/                               # 6 个技能正文（随插件包分发，落到工作区）
```

## 打包硬约束（重要，改动前先读）

插件包 `lib/` **只放插件自身程序集**（`LubanAgent.Plugin.NewProject.dll` + 同名 `deps.json`），
**不得**把宿主程序集（`LubanAgentCore.dll` 等）复制进去。

原因：框架 `PluginLoadContext` 只强制共享 `LuBan.*` / `Avalonia` / `Microsoft.*` / `System.*`；
宿主程序集若出现在 `lib/`，会被加载进插件 ALC，产生**第二份 `Type` 副本**，
与宿主传入的服务实例类型不一致（`IPlugin` / `IWorkspaceManager` 等转换会失败）。

依据（已实测）：`AssemblyDependencyResolver.ResolveAssemblyToPath` 对「文件缺失」的依赖返回 `null`
→ `PluginLoadContext.Load` 返回 `null` → 运行时回落**默认 ALC** → 与宿主共用同一份类型。

因此本工程：

- 以 `ProjectReference` 引用 `LubanAgentCore`（**编译期**用宿主/框架 API）；
- `StagePluginPackage` 目标只复制 `plugin.json` / `newproject.settings.json` / 自身 dll + deps.json / `skills/`，
  **不整目录复制**输出目录。

### 序列化硬约束（同样重要）

插件内的 JSON 读写**只能**经 `Serialization/PluginJsonContext`（`System.Text.Json` **源生成**），
**不得**使用反射式 `JsonSerializer.Deserialize<T>` / `Serialize<T>`（`T` 为插件自身类型），
也不得自建 `JsonSerializerOptions`。

原因（已实测，5 组对照）：反射路径会在**默认 ALC** 里按「插件类型」为键缓存反射元数据
（`JsonTypeInfo` / 成员访问委托），把插件 ALC **永久钉住** → 停用/卸载时第 ⑥ 步 GC 弱引用验证必然失败
（降级 `RequiresRestart`），M1 的「卸载即真回收」保证被破坏。实测结论：

| 做法 | 插件 ALC 能否回收 |
|------|------------------|
| 仅加载程序集 | ✅ |
| 反射式 `JsonSerializer` 解析**插件类型**（解析成功） | ❌ 钉住（其 `JsonSerializerOptions` 实例本身已被 GC，说明问题在默认 ALC 的类型缓存） |
| **源生成 `JsonSerializerContext`**（本插件采此方案） | ✅ |
| 纯 DOM（`JsonDocument` / `Utf8JsonWriter`） | ✅ |

新增需要持久化的模型时，**必须**在 `PluginJsonContext` 上登记 `[JsonSerializable]`。
配置里的「端」写字符串枚举（`"Service"`），由该上下文的 `UseStringEnumConverter` 承载。

## Step 1 / Step 2 实现要点（P2 定案）

### Step 1 校验在能力层，不在面板

`INewProjectService.ValidateBasicInfoAsync` 返回字段级错误文案（`BasicInfoValidation`），
面板只负责展示与投递——保证 PRD §4.10.2「面板与聊天入口零逻辑分叉」。

规则：名称/介绍必填（名称 ≤ 40 字）；缩写匹配 `^[a-z][a-z0-9]*$` 且 ≤ 20 字符；
后端与管理端必选；本地目录必须为绝对路径且可写（探针文件写完即删，只读语义），并做**唯一性**判定：

1. 该目录已被宿主登记为工作区（同路径必然派生同一 `WorkspaceId`）→ **拦截**，但必须给出出路：
   命中信息随 `BasicInfoValidation.ExistingWorkspaceId/Name` 返回，面板据此显示「复用该工作区继续」；
   用户点它即以 `allowExistingWorkspace: true` 重跑校验 → 建档案 → 复用该工作区执行 Step 2
   （复用分支本身幂等，见下）。没有这个出口就会形成死角：错误文案让用户去点一个只在另一种状态下存在的按钮
   （P2 冒烟缺陷）；
2. `{localPath}\{缩写}-Service|Admin|Mobile` 目录已存在且非空 → **只提示不拦截**（`Warnings`，警示色展示）：
   PRD §4.3 Step 3 明确要求「克隆前判断目标目录，非空时给复用已克隆 / 清空后重克隆 / 中止 三选一」，
   该决策属 Step 3；在 Step 1 硬拦截会把用户挡在本步并不负责的判断上。

云效侧「同名仓库唯一」不在此判定：留 Step 3 的重跑幂等（同名复用）。

> 原则：Step 1 的**任何**拦截都必须自带可执行的下一步。判不出出路的情形一律降级为提示。

### Step 2 时序与重跑幂等

1. 探测同路径工作区（`WorkspaceIdGenerator.Compute` + `IWorkspaceManager.GetWorkspaceAsync`）；
   存在即**复用**，不存在才 `CreateWorkspaceAsync`（同路径重复创建会抛「工作区已存在」）；
2. `EnsureAuthorizedAsync` 落库授权；
3. `SetCurrentAsync` 切为当前工作区（内部会恢复/新建会话）；
4. 会话**必须新建**：首次采用 `SetCurrentAsync` 自动建的「新会话」并改名，
   重跑分支**显式** `CreateSessionAsync("新建项目：{名称}")`，不复用它恢复的旧会话；
5. 档案快照写入工作区 `<工作区根>/.luban-agent/plugin-newproject/spec.json`（走源生成上下文）；
6. 重新发 `host.RegisterSkill(清单技能)` × 6 → 按 §3.11 落到**新**工作区；
7. `host.RegisterMcpServer("yunxiao")` → 该会话首次对话时由宿主注入（MCP 是会话级）；
8. 项目级 Agent 延迟到首次对话（`EnsureRuntimeAsync` 惰性创建）；
9. 落 `WorkspaceId` / `SessionId`、`CurrentStep = Repositories`。

失败**只回滚本步**：本步新建的工作区 `DeleteWorkspaceAsync` 注销（级联清理其会话），
**绝不删除** `LocalPath` 目录（PRD §4.3 陷阱②）；已存在的工作区、已完成步骤的产物一律不动。

### 「中止并清理」（PRD §4.7，P2 补齐）

面板的「中止并清理」与残留横幅的「清理后重来」走同一实现，顺序固定：

1. **先**删本插件写入的技能与 MCP：`host.UnregisterSkill(清单技能)` × 6 + `host.UnregisterMcpServer("yunxiao")`；
2. **再**注销本项目工作区：`DeleteWorkspaceAsync(WorkspaceId)`（**逻辑删，不删** `LocalPath` 目录）；
3. 远端产物**一律不自动删除**，只把孤儿条数写进时间线与日志（P3/P5 有真实远端产物后再做清单化展示）；
4. 清空档案与操作记录（等价于「标记未完成」：不留误导性进度），并补一条「中止并清理」时间线记录。

顺序不可颠倒：宿主删技能时优先按**本进程记录的技能根目录**定位，记录为空（重启后再清理）才退化到
「当前工作区」——Step 2 刚把项目工作区置为当前，此刻定位最准；先注销工作区会切走当前工作区，
反而让退化路径指向错误目录。

> 教训：早期实现只清档案、不注销工作区，留下「档案没了、工作区登记还在」的登记残留，
> 与上述唯一性①叠加即形成「同目录再也无法前进」的死角。清理语义必须成对（档案 ↔ 工作区登记）。

### 依赖解析：根单例 → 用户桶

`IWorkspaceManager` / `ISessionManager` 在宿主是 **scoped 转发**（按用户隔离），
从根容器直取在 `ValidateScopes=true` 下会抛。插件（单例生命周期）经根单例
`UserContextRegistry` → `GetOrCreate(LocalUserDefaults.DefaultUserId)` → `.WorkspaceManager` / `.SessionManager`
解析，**按需解析、用完即弃**，不长期持有（PRD §3.9 DI 硬约束）。

### 权限环境

Step 2 由面板事件回调触发，**不在**框架自动包裹范围内，故 `InitializeAsync` 内显式
`PluginScopeContext.Enter(new PluginPermissionScope(PluginId, Permissions))`，
否则后续连接器/进程/凭据出口会把插件调用误判为「宿主自身调用」而放行（PRD §3.7）。

### 技能清单来源

技能 / MCP 声明**只**从插件清单读：`host.Services.GetService<PluginManager>()?.GetPlugin(host.PluginId)?.Manifest`，
不在代码里复刻一份 `plugin.json`（避免两处漂移）。

## 构建与铺包

```powershell
dotnet build luban-agent/LubanAgent.slnx          # 整解决方案（含本插件）
dotnet build luban-agent/LubanAgent.Plugin.NewProject/LubanAgent.Plugin.NewProject.csproj
```

构建后插件包自动铺到 codex 运行目录：

```
luban-agent/LubanAgentCodex/bin/<Configuration>/net10.0/plugins/com.tjces.newproject/
├─ plugin.json
├─ newproject.settings.json
├─ lib/LubanAgent.Plugin.NewProject.dll
├─ lib/LubanAgent.Plugin.NewProject.deps.json
└─ skills/newproject-*.md
```

> 首次运行的 codex 可能**尚未启用**本插件：默认状态为禁用，需在「插件管理」窗口启用。
> 启用状态记录在 `%LocalAppData%/LuBan/AIAgent/plugins/plugin-states.json`。

## 验证（luban-agent 无自动化测试，手工冒烟）

1. 构建解决方案 → 确认上面的插件包目录已生成；
2. 启动 codex → 「插件管理」启用「新建项目」；
3. 左侧栏出现「新建项目」入口，点击后面板挂载成功（左侧步骤条 + Step 1 表单 + 右侧操作记录区）；
4. Step 1 填表 → 「下一步」：校验失败时字段级红字；有非阻断提示（如目标仓库目录已存在且非空）时黄字提示但不拦；
   通过后 Step 2 逐条写时间线（工作区 → 授权 → 当前工作区 → 会话 → 档案快照 → 技能 → 云效 MCP → 项目级 Agent）；
5. 复核落盘：`{LocalPath}/.luban-agent/skills/newproject-*.md`（6 个）、
   `{LocalPath}/.luban-agent/plugin-newproject/spec.json`；工作区列表与会话列表出现新记录；
6. 「重跑 Step 2」：时间线显示「复用已有工作区」并新建执行会话（不报「工作区已存在」）；
7. **唯一性①出口**：同一目录再点「下一步」→ 红字 + 出现「复用该工作区继续」；点它 →
   复用该工作区、新建执行会话、Step 2 全绿（不会再被卡死）；
8. 带残留档案重启面板：出现「清理后重来 / 保留并继续」横幅，未处置前「下一步」不可用；
9. 「中止并清理」：时间线出现 1 条「中止并清理」记录；`{LocalPath}/.luban-agent/skills/newproject-*` 全部消失，
   工作区列表不再有该项目，**`LocalPath` 目录本身仍在**；此后同目录再「下一步」可正常走通（登记残留已清）；
10. 停用插件 → 侧栏入口与面板消失，`.luban-agent/skills/newproject-*` 无残留。

## 阶段进度

| 批次 | 内容 | 状态 |
|------|------|------|
| **P1** | 工程骨架、清单、档案模型、共享能力层契约与桩、向导面板骨架、铺包通路、源生成 JSON 通路 | **已完成**（构建 0 错误；框架侧生命周期探针全绿，含「停用后 ALC 真回收」；用户人工冒烟通过） |
| **P2** | Step 1 表单 + Step 2 初始化项目环境（工作区 / 会话 / 技能 / 云效 / 项目助手） | **已完成**（构建 0 错误；含唯一性①死路修复与「中止并清理」补齐，详见上文「Step 1 / Step 2 实现要点」） |
| P3 | Step 3 按模板建库并克隆（框架 `YunxiaoConnector` 直连）+ Step 6 推送 | 待办 |
| P4 | Step 4 配置与建库 + Step 5 编译检查 | 待办 |
| P5 | Step 7 创建流水线 + 远端产物孤儿清单化展示（本地回收已在 P2 完成） | 待办 |
| P6 | 6 个技能正文细化 + 聊天入口与向导联调 | 待办 |