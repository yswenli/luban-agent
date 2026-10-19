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
├─ newproject.settings.json              # 代码组名格式 / 占位符规则 / 提交身份兜底 / 模板对照 / 参考流水线
├─ NewProjectPlugin.cs                   # 插件入口（PluginBase）
├─ Abstractions/
│  ├─ ProjectSpec.cs                     # 项目档案与相关模型（PRD §4.5）
│  ├─ BasicInfoValidation.cs             # Step 1 校验结果（字段级错误 + 非阻断提示，面板与聊天入口共用）
│  ├─ RepositoryPrecheck.cs              # Step 3 预检读数（只读：代码组 / 模板库 / pin / 仓库名 / 落地位置）
│  └─ INewProjectService.cs              # 共享能力层契约（PRD §4.10.2）
├─ Configuration/NewProjectSettings.cs   # 包内配置读取（模板对照 / 占位符规则 / 参考流水线）
├─ Serialization/PluginJsonContext.cs    # 源生成 JSON 上下文（插件持久化的唯一入口，见下）
├─ Services/
│  ├─ NewProjectService.cs               # 共享能力层实现（P4~P5 分批补齐）
│  ├─ PlaceholderReplacer.cs             # 克隆后的占位符替换（内容 + 文件/目录名）
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
7. `host.RegisterMcpServer("yunxiao")` → 该会话首次对话时由宿主按 `ConnectorRegistry` 的暴露面解析并注入（MCP 是会话级）；前提是 `config.json` 的 `connectors` 节已声明 `yunxiao`（未声明则解析返回 `null` 并告警跳过）；
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

## Step 3 / Step 6 实现要点（P3 定案）

### 前置校验 / 预检（任一缺失即阻断，不做动作）

| 前置 | 缺失时的行为 |
|------|--------------|
| 宿主注册的 `CodeupClient` | 时间线红字「云效连接器不可用」，阻断 |
| 云效组织 ID | 引导去 codex「设置 → 云效连接」填写（读取顺序：`YunxiaoApiOptions.OrganizationId` → 凭据存储 `yunxiao_organization_id`） |
| 云效 PAT | 同上（读取 `IYunxiaoCredentialProvider.GetPatAsync`） |
| 模板仓（模板库中按名 / ID 命中） | 阻断；错误里**列出模板库现有条目**（`templates[].projectId` 可精确指定） |
| 模板 pin 分支 | 阻断；配置留空时按 `develop → master → main → 模板默认分支` **实时解析**，解析不出即报错（不猜） |

组织 ID / 提交身份（F5=A）/ 占位符规则（F3=A）全部走插件配置；pin 由运行时解析后写入档案的
`repositories[].templateBranch`（**不落固定值**，PRD §6 #5 修订）。

### Step 3 预检（只读，先于动作）

`PrecheckRepositoriesAsync` 把「即将使用的参数」摆给用户核对，**不创建任何云效资源**：

- 目标代码组：按 `groupNameFormat`（默认 `{CODE}`，如 `jnms` → `JNMS`）在组织内**只查不建**，
  报告「已存在将复用（含 id）」或「不存在将新建（含父级＝组织根）」；
- 逐端：模板库命中项（`templateProjectId` / 全路径）、模板仓现有分支、解析出的 pin 及**依据**、
  目标仓库名（已存在将复用 / 将新建）、可见性、落地目录；
- 任何一环出错**不抛异常**，收集进 `RepositoryPrecheck.Errors` 逐条展示；
- 本地目录已存在且非空 → 归入 `Warnings`（执行时才三选一）。

面板侧：**「创建并克隆」在预检通过前保持禁用**；任何输入变更（仓库名 / 可见性）立即**作废**预检
（比对「输入签名」），需重新预检；预检读数在**执行后被消耗**（避免误以为还能直接再跑）。

### Step 3 时序（逐端串行）

1. **准备项目代码组**：先查后建——`ListNamespacesAsync(search)` 命中同名组即复用，未命中才
   `CreateNamespaceAsync`（`parentId` 空＝组织根，`visibility` 取配置 `defaultVisibility`）；
   产物以 `RemoteArtifactKind.Group`（项目级，`End = null`）入册；
2. **模板库定位**：`ListTemplateRepositoriesAsync(templateType = 1)`，按配置 `projectId`（整数）或
   `repository` 名匹配（**走模板库而非按名搜普通仓库**——只有登记为模板的仓才能作 `templateProjectId`）；
3. **解析 pin**：配置 `templates[].branch` 优先（不存在即阻断）；留空则 `ListBranchesAsync` 按
   `develop → master → main → 模板默认分支` 取第一个（写回 `repositories[].templateBranch`）；
4. 目标仓库同名检索：**命中即复用**（重跑幂等①，不重复建库）；未命中才 `CreateRepositoryAsync` 带
   `namespaceId`（＝代码组 id）+ `TemplateProject{ templateProjectId, templateType = 1, syncAllBranches = true }`；
5. `GetRepositoryAsync(id)` 补查 **HTTPS 克隆地址**（创建 / 列表返回都不含该字段）；
6. 目标目录存在且非空 → `CloneConflictResolver` **内联三选一**（PRD §4.3 Step 3）：
   - 复用已克隆 → **先做健康校验**（`git rev-parse --verify --quiet HEAD` 与 `refs/heads/<pin>`）：
     校验不通过（典型是一次失败克隆留下的 `HEAD = ref: refs/heads/.invalid` 残骸）→ **拒绝复用并给出引导**
     （重跑选「清空后重克隆」，或手动删目录），不再把坏目录盖章成「已克隆」（RC4）；
   - 清空后重克隆 → 删目录后重克隆；
   - 中止本步 → **远端仓库保留**（登记为孤儿产物），本地目录不动；
   无人回答（聊天入口 / 面板未创建，resolver 为 `null`）→ 中止本步 + 引导文案；
7. **等待模板分支就绪**：建库返回 **不等于** 模板分支已同步（`syncAllBranches` 在服务端异步），
   先轮询 `ListBranchesAsync` 直到 pin 分支出现（上限 120s / 间隔 3s），超时才报错（RC1）；
8. `CloneToLocalAsync(url, dir, pin)`（`git clone -b`，PRD §6 #17）：
   失败时**清理本次新建的残留目录**（残留不清理会在重跑时被误判为「已克隆」，RC3），
   失败文案带上 git 原始报错摘要（`GitCommandResult.FailureSummary()`，URL 内嵌凭据已脱敏，RC2）；
9. `PlaceholderReplacer.Apply`：替换**内容 + 文件/目录名**（跳过 `.git` / `bin` / `obj` / `node_modules`，4MB 上限，UTF-8 BOM 保留）；
10. 逐端即时落库 `RemoteArtifacts`（按 `Kind + End + Name` 去重）+ `ExternalId` / `CloneUrl` / `WebUrl`，最后 `CurrentStep = Database`。

> 每条产物**即时持久化**：某端失败时已成功端的账目不会丢（时间线 + 档案双份），重跑本步只补缺失。
> `repositories[].cloned` 语义收紧为「**本地目录确已就绪**」：真克隆成功，或「复用已克隆」经健康校验通过；
> 本步开始处理某端时先把它的 `cloned` 置回未就绪（本轮失败**不留上一轮的旧绿灯**），
> 任一端未就绪即 `Step 3` 失败（不给假绿，Step 6 亦据此拒推）。

### Step 6 时序（逐端串行）

1. `git rev-parse --verify --quiet refs/heads/main` → 已存在则 `checkout main`，否则 `checkout -b main`（自模板 pin 分支）；
2. `git add -A` → `git diff --cached --quiet` 判空，无改动**跳过提交**（不产生空提交）；
3. 有改动才提交，作者取本机 `git config user.name/user.email`，缺失时用配置 `commitAuthorName/Email` 兜底；
4. `CodeupClient.PushAsync(cloneUrl, dir, "main")`（连接器内置鉴权 URL 与出口门禁，**不强制推送**）；
5. 登记 `RemoteArtifactKind.Branch` 产物。

面板自身拉起的 git 进程同样要过门禁：`PluginPermissionGuard.RequireGit` / `RequireProcess`
在每次 `RunGitAsync` 前显式调用（PRD §3.7「不得借连接器之名绕过检查」）。

### 面板结构（P3 起）

左列从「单表单」改为「常驻状态卡 + 按 `CurrentStep` 切换的操作区」：

- 常驻：校验错误 / 非阻断提示 / 忙碌文案 / 残留横幅 + 「档案与维护」（重跑 Step 2 / 中止并清理）；
- Step 1 表单（`BasicInfo` / `Scaffold`）；
- Step 3 确认区（`Repositories`）：逐端仓库名 / 可见性单选 / 归属组织只读 / 落地位置 / **预检卡片（「预检（只读，不创建）」按钮 + 读数）** / 「我已确认」勾选 + 建库按钮 + 内联冲突三选一；
- Step 4-5 占位（`Database` / `Build`，含临时「直接执行推送」入口，P4 落地后移除）；
- Step 6 推送区（`Push`）；Step 7 占位（`Pipeline`）。

> 冲突三选一即使处于「忙碌」态也**保持可点**——它回答的正是运行中提出的问题，锁掉会把能力层挂到超时。
> 「创建并克隆」只在**预检通过且未被输入变更作废**时可用；未预检就点会被拦下并提示先预检（预检读数在执行后被消耗，重跑需重新预检）。

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

### P3 增补（Step 3 / Step 6）

11. **预检门禁**：Step 3 未预检时「创建并克隆」不可用（灰）；设备输入后点它 → 红字要求先预检，
    **不会**产生任何远端动作；改动仓库名 / 可见性 → 预检读数变黄「已作废」，须重新预检；
12. **预检读数**：点「预检（只读，不创建）」→ 读数含：目标代码组（不存在将新建 / 已存在将复用 · id）、
    逐端模板库命中（`templateProjectId` · 全路径）、pin 及依据（如「按开发约定取模板 develop」）、
    仓库名（将新建 / 已存在将复用 · 可见性）；此时**云效侧未新增任何代码组 / 仓库**（只读，可对照后台）；
13. **预检阻断**：未填「云效连接」的组织 ID / PAT，或模板库中找不到配置的模板仓名时点「预检」→
    读数与时间线给出红字（引导去设置页 / 列出模板库现有条目）；
14. **Step 3 正常路径**：预检通过 → 勾选「我已确认」→ 「创建并克隆」→ 逐条进时间线
    （准备项目代码组 → 定位模板仓与 pin → 创建仓库 / 同名复用 → 查询仓库地址 → **等待模板分支就绪** → 克隆 → 替换占位符）；
    「等待模板分支就绪」在 pin 分支尚未同步到新仓时轮询等待（上限 120s / 间隔 3s），就绪后立刻写「pin develop 已就绪」；
    复核本地目录里有仓库内容、`spec.json` 里 `repositories[].cloneUrl/webUrl/externalId/templateBranch` 已落、
    `remoteArtifacts` 出现 `Group`（项目级，`end` 为 null）与各端 `Repository` 条目；远端仓库由模板同步了分支；
    代码组落在 `groupNameFormat` 指定的名字下（默认＝缩写全大写，如 JNMS）；
15. **模板 pin 实时解析**：`templates[].branch` 留空 → 读数为 `develop`（Service/Admin）与 `master`（Mobile，无 develop）；
    在配置里把某端 pin 改成不存在的分支 → 预检阻断，提示现有分支清单（不静默回退）；
16. **Step 3 重跑幂等**：重新预检后（此时代码组已存在）→ 页面显示「已存在，将复用」；
    执行 → 时间线出现「云效已存在同名仓库，跳过创建直接复用」，**不会**因重名建库失败；
17. **克隆冲突三选一（含复用健康校验）**：手工往目标目录塞一个多余文件后重跑 → 预检的非阻断提示列出该目录；
    执行时面板出现三选一；选「复用已克隆」→ 时间线先出现「校验本地目录（X端）」且通过后才跳过克隆；
    选「清空后重克隆」→ 目录被清空后重克隆；
    选「中止本步」→ 时间线说明远端仓库保留（孤儿），本地目录未被改动；
18. **Step 6**：从 Step 4 占位区点「直接执行推送」→ 时间线逐端「整理分支 → 提交改动 → 推送到自有仓库」，
    云效仓库出现 `main` 分支与首次提交；再点一次 → 无改动时显示「工作区无改动，跳过提交」，推送仍成功（幂等）；
19. 「中止并清理」在 Step 3/6 视图下同样可见可用（常驻维护卡），且不删除本地目录、远端仓库与代码组。
20. **坏目录不得假成功（回归）**：把某端本地目录的 `.git/HEAD` 手工改成 `ref: refs/heads/.invalid`
    （复现一次失败克隆的残骸）后重跑 Step 3 → 三选一里选「复用已克隆」→ 时间线红字
    「不能『复用已克隆』：HEAD 无法解析…」且 `spec.json` 的该端 `cloned` **不会**置真；
    改成选「清空后重克隆」→ 目录被清空后正常克隆成功（本项对应真机首轮踩到的 RC4 假成功）。

> **真机第一次执行建仓动作前**，先对照第 12 项读数与用户逐项核对（模板库命中 / pin / 代码组名 / 仓库名 / 落地位置）。

## 阶段进度

| 批次 | 内容 | 状态 |
|------|------|------|
| **P1** | 工程骨架、清单、档案模型、共享能力层契约与桩、向导面板骨架、铺包通路、源生成 JSON 通路 | **已完成**（构建 0 错误；框架侧生命周期探针全绿，含「停用后 ALC 真回收」；用户人工冒烟通过） |
| **P2** | Step 1 表单 + Step 2 初始化项目环境（工作区 / 会话 / 技能 / 云效 / 项目助手） | **已完成**（构建 0 错误；含唯一性①死路修复与「中止并清理」补齐，详见上文「Step 1 / Step 2 实现要点」） |
| P3 | Step 3 预检 + 按模板建库并克隆（运行期自动建组 + 实时解析 pin，框架 `CodeupClient` 直连）+ Step 6 推送 | **代码完成 + 真机首轮排障已修**（构建 0 错误；修复 RC1 建库/克隆竞态、RC2 git 报错被吞、RC3 失败残留、RC4「复用已克隆」假成功；待重启 codex 复跑真机 11–20 项） |
| P4 | Step 4 配置与建库 + Step 5 编译检查 | 待办 |
| P5 | Step 7 创建流水线 + 远端产物孤儿清单化展示（本地回收已在 P2 完成） | 待办 |
| P6 | 6 个技能正文细化 + 聊天入口与向导联调 | 待办 |