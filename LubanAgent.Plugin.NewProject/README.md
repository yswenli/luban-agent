# LubanAgent.Plugin.NewProject（新建项目插件）

> PRD：`docs/PRD-插件体系与新建项目插件.md`（v0.23）· 里程碑 **M4**
> 插件标识：`com.tjces.newproject` · 显示名：新建项目

以 TJCES 三端模板仓创建新项目：**七步向导**（左侧栏面板）+ **聊天式入口**（`newproject-*` 技能），
两条入口共用同一个 `INewProjectService`，云效逻辑零分叉。
**（v0.22）** Step 4 / Step 5 的动作**不由插件执行**：插件下发提示词与硬边界 → **项目会话的助手亲自探活 / 编译** → 插件回读**结论文件**判定（技术路线 ②）。
**（v0.23）** 新增**步骤条回退导航**（可回到任一「已到达」步骤重看 / 重跑，只动面板视图游标、不动档案）与**推送闸门** `StepGate.CanPush`（编译未全部通过即拒推，向导 / 聊天同受保护）——修「Step 5 无入口导致越级推送」的真机缺陷，详见「回退导航与推送闸门」。

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
│  ├─ DatabaseProbe.cs                   # Step 4 探活参数（含校验与口令脱敏的 Describe）
│  ├─ StepReport.cs                      # Step 4/5 结论文件模型（step / ok / checks[] / message）
│  └─ INewProjectService.cs              # 共享能力层契约（PRD §4.10.2）
├─ Configuration/NewProjectSettings.cs   # 包内配置读取（模板对照 / 占位符规则 / 参考流水线）
├─ Serialization/PluginJsonContext.cs    # 源生成 JSON 上下文（插件持久化的唯一入口，见下）
├─ Services/
│  ├─ NewProjectService.cs               # 共享能力层实现（P4~P5 分批补齐）
│  ├─ PlaceholderReplacer.cs             # 克隆后的占位符替换（内容 + 文件/目录名）
│  ├─ NewProjectStateStore.cs            # 档案持久化（残留检测 / 孤儿清单）
│  ├─ HostAssistantBridge.cs             # 路线② 桥接层：经宿主服务在项目会话上跑一轮提示词（P4）
│  ├─ StepPrompts.cs                     # Step 4/5 提示词构造（纯函数，锁死边界与结论契约）
│  ├─ StepReportParser.cs                # 结论文件解析（源生成上下文，不抛异常）
│  ├─ StepReportEvaluator.cs             # 结论归并与阻断判定（纯函数）
│  ├─ StepGate.cs                        # 推送闸门（纯函数：编译未全部通过即拒推，见「回退导航与推送闸门」）
│  ├─ StepResultFile.cs                   # 结论文件路径约定（工作区内，不在任何 git 仓库里）
│  ├─ AssistantToolPolicy.cs             # 助手工具白名单（替代 BypassPermissions）
│  └─ DatabaseProbeDefaults.cs           # Step 4 默认值：解析 appsettings.json 连接串装配参数
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
9. 落 `WorkspaceId` / `SessionId`、推进指针 `AdvanceTo(Repositories)`（**v0.23**：`CurrentStep` 语义＝「已到达的最大步」，只能单调前进）。

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

本插件 `plugin.json` 声明的 git 能力为**三个组**：`git:clone_push`（克隆/推送）、
`git:commit`（本地 `add` / `commit`）、`git:inspect`（`rev-parse` / `status` / `diff` / `branch` / `config` / `checkout`）。
Step 6 的本地提交由插件自己拉起 git，故必须声明后两组——只声明 `clone_push` 时
`RequireGit("add")` 会在运行期被拒（真机已复现：`权限越界:git未授权访问add`）。
门禁**只在唯一出口** `RunGitAsync` 内执行：操作名取子命令本身，同时校验 `process:git`，
调用点不再各处重复声明，避免漏点。

### 技能清单来源

技能 / MCP 声明**只**从插件清单读：`host.Services.GetService<PluginManager>()?.GetPlugin(host.PluginId)?.Manifest`，
不在代码里复刻一份 `plugin.json`（避免两处漂移）。

## Step 3 / Step 6 实现要点（P3 定案）

### 前置校验 / 预检（任一缺失即阻断，不做动作）

| 前置 | 缺失时的行为 |
|------|--------------|
| 宿主注册的 `CodeupClient` | 时间线红字「云效连接器不可用」，阻断 |
| 云效组织 ID | 引导去 codex「设置 → 云效连接」填写（读取顺序：`YunxiaoApiOptions.OrganizationId` → 凭据存储 `connector:yunxiao:organizationid`，历史键 `yunxiao_organization_id` 由别名表回退） |
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
   - 清空后重克隆 → 经 `DirectoryCleaner.DeleteForce` 清空（**先递归清只读再删**，RC5）后重克隆；
   - 中止本步 → **远端仓库保留**（登记为孤儿产物），本地目录不动；
   无人回答（聊天入口 / 面板未创建，resolver 为 `null`）→ 中止本步 + 引导文案；
7. **等待模板分支就绪**：建库返回 **不等于** 模板分支已同步（`syncAllBranches` 在服务端异步），
   先轮询 `ListBranchesAsync` 直到 pin 分支出现（上限 120s / 间隔 3s），超时才报错（RC1）；
8. `CloneToLocalAsync(url, dir, pin)`（`git clone -b`，PRD §6 #17）：
   失败时**清理本次新建的残留目录**（残留不清理会在重跑时被误判为「已克隆」，RC3；
   清理同样走 `DirectoryCleaner.DeleteForce`，见 RC5），
   失败文案带上 git 原始报错摘要（`GitCommandResult.FailureSummary()`，URL 内嵌凭据已脱敏，RC2）；
9. `PlaceholderReplacer.Apply`：替换**内容 + 文件/目录名**（跳过 `.git` / `bin` / `obj` / `node_modules`，4MB 上限，UTF-8 BOM 保留）；
10. 逐端即时落库 `RemoteArtifacts`（按 `Kind + End + Name` 去重）+ `ExternalId` / `CloneUrl` / `WebUrl`，最后 `AdvanceTo(Database)`（v0.23：单调推进）。

> 每条产物**即时持久化**：某端失败时已成功端的账目不会丢（时间线 + 档案双份），重跑本步只补缺失。
> `repositories[].cloned` 语义收紧为「**本地目录确已就绪**」：真克隆成功，或「复用已克隆」经健康校验通过；
> 本步开始处理某端时先把它的 `cloned` 置回未就绪（本轮失败**不留上一轮的旧绿灯**），
> 任一端未就绪即 `Step 3` 失败（不给假绿，Step 6 亦据此拒推）。

### Step 6 时序（逐端串行）

1. 目标分支取配置 `pushBranch`（默认 `main`，PRD §6 #17 定案；留空才退回该端仓库默认分支）→
   `git rev-parse --verify --quiet refs/heads/<branch>` 已存在则 `checkout <branch>`，否则 `checkout -b <branch>`（自模板 pin 分支）；
2. `git add -A` → `git diff --cached --quiet` 判空，无改动**跳过提交**（不产生空提交）；
3. 有改动才提交，作者取本机 `git config user.name/user.email`，缺失时用配置 `commitAuthorName/Email` 兜底；
4. `CodeupClient.PushAsync(cloneUrl, dir, <branch>)`（连接器内置鉴权 URL 与出口门禁，**不强制推送**）；
5. 登记 `RemoteArtifactKind.Branch` 产物。

面板自身拉起的 git 进程同样要过门禁（PRD §3.7「不得借连接器之名绕过检查」）：
`PluginPermissionGuard.RequireGit(子命令)` / `RequireProcess("git")` **内置于唯一出口 `RunGitAsync`**，
故 `add` / `commit` / `checkout` / `rev-parse` / `config` 一个都不漏；操作名按子命令名判定，
命中 `plugin.json` 声明的 `git:clone_push` / `git:commit` / `git:inspect` 三个能力组之一才放行。

### 面板结构（P3 起）

左列从「单表单」改为「常驻状态卡 + 按 `CurrentStep` 切换的操作区」：

- 常驻：校验错误 / 非阻断提示 / 忙碌文案 / 残留横幅 + 「档案与维护」（重跑 Step 2 / 重新推送（重跑 Step 6）/ 中止并清理）；
- **导航行（v0.23）**：步骤条下方一行 = 「← 上一步」按钮 + 提示文字（回看时显示「（回看；档案已到达 N …）」）；
- Step 1 表单（`BasicInfo` / `Scaffold`）；
- Step 3 确认区（`Repositories`）：逐端仓库名 / 可见性单选 / 归属组织只读 / 落地位置 / **预检卡片（「预检（只读，不创建）」按钮 + 读数）** / 「我已确认」勾选 + 建库按钮 + 内联冲突三选一；
- Step 4 探活表单（`Database`）：服务器 / 端口 / 库名 / 账号 / 口令（默认值取自克隆下来的 `WebHost/appsettings.json`，口令用 `PasswordChar` 遮蔽且不回填）+ 「开始探活（只读，不建库）」+ 结论区（读档案，跨重启可见）；
- Step 5 逐端卡（`Build`）：所选端逐条显示「通过 / 失败 / 未勾选」，附**实际执行的命令**与失败摘要 + 「开始编译（逐端）」；
- Step 6 推送区（`Push`）；Step 7 占位（`Pipeline`，只说明「P5 未实现」）；

> 冲突三选一即使处于「忙碌」态也**保持可点**——它回答的正是运行中提出的问题，锁掉会把能力层挂到超时。
> 「创建并克隆」只在**预检通过且未被输入变更作废**时可用；未预检就点会被拦下并提示先预检（预检读数在执行后被消耗，重跑需重新预检）。

### 回退导航与推送闸门（v0.23 · P4 真机冒烟缺陷修复）

真机冒烟出现「**5 编译 未开始 / 6 推送 已完成**」的自相矛盾状态。根因是三因叠加：

1. `PushAsync` 成功后无条件把 `spec.CurrentStep` 推到 `Pipeline`（Step 7）；
2. Step 5 的操作区**只在「当前步＝编译」时渲染**，而向导**没有任何回退入口**（步骤条不可点、无「上一步」）；
3. v0.22 新加的常驻「重新推送（重跑 Step 6）」按「至少一端已克隆」判定可见性 → Step 3 一完成即可点，
   于是 Step 6 在 Step 5 之前被执行。**「编译失败阻断 Step 6」实际只剩界面顺序，能力层没有任何校验。**

> 教训：**阻断必须落在能力层**。常驻入口、步骤条回退、聊天入口都能绕开界面顺序。

#### 视图游标 vs 推进指针（两个概念不要混）

| 概念 | 落点 | 语义 |
|------|------|------|
| **推进指针** `ProjectSpec.CurrentStep` | **档案**（落 `spec.json`） | **已到达的最大步**。由 `ProjectSpec.AdvanceTo(step)` 单调推进（只前进、不回退）；仅「新建档案」与「Step 2 失败回滚」两处直接赋值 |
| **视图游标** `WizardPanel._viewStep` | **内存**（不落档） | 用户当前在看哪一步。`null` ＝ 跟随推进指针 |

- **回退入口**：步骤条**每一行都是可点按钮**（透明底、手型光标）+ 导航行的**「← 上一步」**，双入口等价。
- **回退范围**：**只放开「推进指针以内」的步骤**（`step <= reached`）；未到达的步骤行灰化——顺序执行仍是硬约束，回退只是「回到已到达的步重看/重跑」。
- **回退只切视图**：不改档案、不自动重跑任何动作；重跑由该步操作区的按钮显式触发。
- **何时丢弃游标**：`Refresh()` 内按两条规则——①本次刷新**观察到推进指往前移**且越过游标（刚执行的动作把流程推到了后一步）→ 丢游标、视图跟上新步骤；②游标 > 推进指针（档案被「中止并清理」重置）→ 丢游标。
  **关键**：回看**早已到达**的步骤不产生前移，游标保持——所以「已到 Step 7 后回 Step 5 重编译成功」不会把视图弹走，用户能就地看到逐端结论（这正是回退导航的价值）。
- **步骤条状态**仍取「档案终态快照 ⊕ 本次记录」（`StepStateResolver`），所以回看已完成步骤仍显示「已完成」，不会退回「未开始」；高亮的是**视图游标**所在的步。
- **重启语义**：游标不落档 → 重新打开面板回到档案的「已到达最大步」。
- **Step 1 重提（破坏性）**：已建档案后从步骤条回到 Step 1 再点「下一步」＝ **清空档案并从 Step 2 重来**，故按钮文案变为「清空档案并重走 Step 2」，且**先弹卡内确认横幅**（确认 / 取消），不静默覆盖档案（PRD §4.7）。

#### 推送闸门 `StepGate.CanPush(spec)`

纯函数（`Services/StepGate.cs`），判据 = **`StepStates["Build"] == Succeeded` ∧ `BuildResults.Count > 0` ∧ 逐端全 `Ok`**，缺一即拒。`NewProjectService.PushAsync` 前置复核（紧跟原「无已克隆仓库」校验之后），向导按钮 / 常驻重推 / 聊天入口**同受保护**。

- 最关键的一条：**从未跑过 Step 5** → 档案里根本没有 `Build` 键 → 拒绝（真机档案正是这个形态）。
- 判据取**档案事实**（终态快照 + 逐端结论，跨重启保留），不取界面状态或内存记录。
- 拒绝文案：「编译（Step 5）尚未全部通过，已拒绝推送，请回到「编译」步逐端编译。」

#### 常驻「重新推送」可见性收紧

由「至少一端已克隆」改为 **「推进指针 ≥ Step 6」**（`_repushButton.IsVisible = spec is not null && (int)spec.CurrentStep >= (int)WizardStep.Push`）。原口径下 Step 3 一完成就出现常驻重推入口，等于给了一条编译前的越级通道（PRD §4.8 第 18 条⑥ / 第 22 条④）。

## Step 4 / Step 5 实现要点（P4 定案 · 技术路线 ②）

### 为什么「插件不代劳」

Step 4 / Step 5 的动作（连库探活、逐端编译）都在**项目工作区内、用宿主已有的能力**就能完成。
若由插件自己实现，等于把「脚本执行 / 文件写入 / 进程拉起」在插件侧再实现一遍，且与「聊天入口里助手执行」形成两套。
故定案：**插件只负责「下发提示词 + 固定边界 + 回读结论」**，动作交给**项目会话里的助手**（技术路线 ②）。

### 判定一律以「结论文件」为准（不解析对话正文）

| 项 | 约定 |
|----|------|
| 路径 | `{LocalPath}\.luban-agent\plugin-newproject\results\step{4,5}-<时间戳>.json` |
| schema | `{ "step": 4 或 5, "ok": bool, "checks": [ { "name", "ok", "detail", "command"? } ], "message" }` |
| 通过判定 | **四环齐**：①文件存在 ②schema 可解析（`StepReportParser`，走源生成上下文） ③`ok` 为真 ④本轮事件流无错误事件 |
| Step 3 之后注意 | 该目录与各端 git 仓库**同级、不在任何仓库内** → Step 6 推送不会把它带进业务仓库 |

放工作区内的理由：①命中「工作区内路径免确认」，不会被工具审批拒；②用户随时可自查（结论文件本身就是本步证据）。

### Step 4：只探活（v0.22 收窄）

- **只做一件事**：确认「能连上且库已存在」。**不建库、不执行任何 SQL、不生成/修改任何项目文件与配置**（唯一写入＝结论文件）。
- **不阻断**：探活未通过照常进 Step 5，只给可操作原因（`1049 Unknown database` → 提示手工建库后重跑）。
- **默认值**：从克隆下来的后端 `WebHost/appsettings.json` 的连接串预填服务器 / 端口 / 库名 / 账号
  （`DbConnectionOptions:ConnectionConfigs[0]:ConnectionString`；解析容错 `//` 注释与尾随逗号）；
  **口令不回填**（界面 `PasswordChar` 遮蔽，用户重填）。
- **落档**：服务器 / 端口 / 库名 / 账号回写 `spec.json`；**口令不落档、不写日志**，操作记录一律 `***`（`DatabaseProbeRequest.Describe()`）。
- **口令传递＝方案 A（用户裁决）**：口令**明文内联进本轮提示词**。**已知残余风险**：会随框架会话历史落进
  `luban-ai-agent.db`，并在该会话后续轮回灌模型上下文（已记入 PRD §4.8 第 19 条）。曾评估未采纳：系统临时口令文件 / 插件写含口令的探活脚本 / 插件直连探活。

### Step 5：逐端编译（编译方式交助手判断）

- **插件不预设命令**：助手先看仓库里有什么（`.sln` / `.csproj` / `package.json` / `pubspec.yaml`）再自行决定
  （后端 `dotnet build`、管理端 Vite/Vue `pnpm install && pnpm run build`）。真机管理端就是 Vite/Vue、无 `.sln`，统一 `dotnet build` 不成立。
- `checks[].name` **必须用端标识**（`Service` / `Admin` / `Mobile`），**逐端齐全**——漏端即判「该端未给结论」＝**本步失败**（不用「其余端通过」兜底，否则漏跑一端会被静默放过）。
- **失败阻断 Step 6**；只编译**不修复**、不清理编译产物。

### 助手工具白名单（替代 `BypassPermissions`）

`AssistantToolPolicy.Decide` 对每次工具调用给结论，落 `Allow` / `Deny` 并写入操作记录（含依据）：

- **放行**：只读工具（`ReadFileAsync` / `ListDirectoryAsync` / `GetWorkspaceOverviewAsync` / `UseSkillAsync` 等）、`RunPythonAsync`、
  命令在白名单内的 `RunShellAsync`、**工作区内**的写类工具（`WriteFileAsync` / `CopyFileAsync` 按 `destPath` / `MoveFileAsync` 按 `destPath`）。
- **拒绝**：`RunLuaAsync`、删除类（`DeleteFileAsync` / `DeleteDirectoryAsync`）、技能与 MCP 写删类、越出工作区的写入、未知工具、工作区根未知时的任何写入。
- **Shell 逐段判定**：按 `&&` / `||` / `;` / `|` / 换行切段，每段取首词（支持 `& "prog"` 与 `"C:\Program Files\...\dotnet.exe"` 引号路径，去目录、去 `.exe/.cmd/.bat/.ps1` 后缀），
  首词不在 `AllowedExecutables`（`dotnet` / `git` / `node` / `npm` / `npx` / `pnpm` / `yarn` / `python` / `py` / `python3` / `pip` / `pip3`）与 pwsh 无副作用内置（`cd` / `Set-Location` 等）内即**整条拒绝**；
  `$env:X = "..."` 赋值段视为无副作用放行。

### 防死锁：助手不得回调插件

本轮提示词由**插件在项目会话上发起**。若助手在同一个回合里回头调用 `newproject-*` 技能或「新建项目」插件的服务，
就会变成「同会话回合串行」的**自等死锁**。故两条提示词与三个技能正文都写死这条禁令。

### 已知遗留（建议单独立项）

- 框架 `FileSystemToolPlugin.DeleteDirectoryAsync`（`luban-framework/LuBan.AIAgent/Tools/FileSystem/FileSystemToolPlugin.cs:807`）
  用的是裸 `Directory.Delete`，删 git 目录（含只读 pack）会失败且只报「权限不足」。本插件自己的清理走
  `DirectoryCleaner.DeleteForce`（先清只读再删，RC5 已修）；框架那个要改需重新发包。
- **（v0.23 登记，待查）** Step 4 真机实测走「连库未带口令」分支（`1045 Access denied … (using password: NO)`）。
  口令按方案 A 内联进提示词，链路需复核「界面口令是否真的传进本轮提示词」，以及 `1045` 分支的文案是否明确引导用户回填口令后重跑
  （PRD §4.8 第 19 条已记残余风险，此处是交互提示的待确认项）。
- **（v0.23 说明）旧档案形态不再卡死**：修复前产生的档案可能出现「`CurrentStep` 已到最后一步、但 `StepStates` 里缺 `Build`」的
  自相矛盾状态（真机即此形态）。**不需要手工改档案**——新版面板可从步骤条点回 Step 5 补跑；闸门按 `StepStates` 判据拦截，
  不会因为指针靠前就放行推送。

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

## 验证（自动化 + 手工冒烟）

产品侧已有自动化测试项目 **`LubanAgent.Tests`**（MSTest）：

```powershell
dotnet test luban-agent/LubanAgent.Tests/LubanAgent.Tests.csproj
```

其中与本插件相关的用例：`NewProjectStepPromptsUnitTest`（提示词：口令内联 / 硬边界 / 结论契约 / 端清单）、
`NewProjectStepReportUnitTest`（结论解析容错 + Step 4 不阻断 + Step 5 逐端归并与漏端即失败 + 事件流有错即失败）、
`NewProjectDatabaseProbeUnitTest`（参数校验 / 口令脱敏 / 连接串解析不回填口令 / appsettings 容错 / 结论文件路径）、
`NewProjectAssistantToolPolicyUnitTest`（工具白名单 + Shell 逐段判定 + 越界拒绝）、`StepStateResolverUnitTest`、
`DirectoryCleanerUnitTest`、`NewProjectStepGateUnitTest`（**v0.23**：`AdvanceTo` 单调只前进 + `StepGate.CanPush` 判据，
含「冒烟档案形态」——`CurrentStep=Pipeline` 且无 `Build` 键——的回归锁定）。
**其余验证仍为手工冒烟**（本插件的 UI 与真机链路）。

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
18. **Step 6**：点常驻「档案与维护」卡里的「重新推送（重跑 Step 6）」（**v0.23：推进指针 ≥ Step 6 后可见**，Step 6 页自身的按钮等价）→ 时间线逐端「整理分支 → 提交改动 → 推送到自有仓库」，
    云效仓库出现 `main` 分支与首次提交；再点一次 → 无改动时显示「工作区无改动，跳过提交」，推送仍成功（幂等）；
19. 「中止并清理」在 Step 3/6 视图下同样可见可用（常驻维护卡），且不删除本地目录、远端仓库与代码组。
20. **坏目录不得假成功（回归）**：把某端本地目录的 `.git/HEAD` 手工改成 `ref: refs/heads/.invalid`
    （复现一次失败克隆的残骸）后重跑 Step 3 → 三选一里选「复用已克隆」→ 时间线红字
    「不能『复用已克隆』：HEAD 无法解析…」且 `spec.json` 的该端 `cloned` **不会**置真；
    改成选「清空后重克隆」→ 目录被清空后正常克隆成功（本项对应真机首轮踩到的 RC4 假成功）。
21. **只读 pack 残骸必须能清空（回归，RC5）**：造一个真机等价残骸——`<目标目录>/.git/objects/pack/`
    下放一个**只读** pack 文件（`attrib +r`），其余目录结构随意，然后重跑 Step 3 → 三选一里选「清空后重克隆」：
    目录必须被真正删空并重新克隆成功（**不得**再报 `Access to the path '…idx' is denied.`）。
    说明：git 在 Windows 上把 pack 标为只读，裸 `Directory.Delete` 必失败且会在抛错前删掉 HEAD / config / refs，
    把残骸越搞越烂——这正是 RC5 的死锁现场（真机 2026-10-20 17:27 踩到）。
    自动化侧由 `LubanAgent.Tests/DirectoryCleanerUnitTest.cs` 锁死（裸删必抛 + `DeleteForce` 必成）。

> **真机第一次执行建仓动作前**，先对照第 12 项读数与用户逐项核对（模板库命中 / pin / 代码组名 / 仓库名 / 落地位置）。

### P4 增补（Step 4 / Step 5）

**冒烟前置（重要，否则会测到旧技能正文）**：宿主把技能写入工作区是**只补缺失**策略，已存在的
`{LocalPath}\.luban-agent\skills\newproject-*` **不会被覆盖**。改了 `skills/*.md` 后必须先删掉这几个目录，
再从 Step 2 重跑，才能拿到新正文。另：P4 改动需先重新构建 `LubanAgent.slnx`（把新包铺到 codex 运行目录）。

22. **Step 4 界面**：进入 Step 4 → 服务器 / 端口 / 库名 / 账号已从克隆下来的 `WebHost/appsettings.json` **预填**，
    口令为空（遮蔽输入，不回填）；点「开始探活（只读，不建库）」→ 时间线出现「连接数据库」记录，右侧结论区出结果；
23. **Step 4 「库不存在」分支（必测）**：库名填一个**不存在**的库（Step 3 已把模板库名改成 `tjc_dev_{缩写}` 而该库通常尚未创建）
    → 结论应为**未通过**并点名 `1049 Unknown database`，且**流程照常推进到 Step 5**（第二步仍可点）；
24. **Step 4 「库存在」分支**：库名换成本机/服务器上**已存在**的库（如 `tjc_dev_tjces`）→ 结论通过；
    `spec.json` 里回写了服务器 / 端口 / 库名 / 账号，**没有任何口令痕迹**（含操作记录与日志）；
25. **Step 4 只读核验**：探活前后对比 —— 目标库**没有**多出任何表/库；克隆下来的项目目录里**没有**任何文件被改动
    （`git status` 干净，`appsettings*.json` 未被写入）；工作区里只多出 `results/step4-*.json`；
26. **Step 5 逐端**：进 Step 5 → 逐端卡片出现，点「开始编译（逐端）」→ 时间线出现助手的编译命令（后端 `dotnet build ...`、
    管理端 `pnpm` 系列），各端结论与「实际执行的命令」一并展示；**通过后**「下一步」才可用；失败时不可用（阻断 Step 6）；
27. **Step 5 漏端防护**：手工把某端的结论从 `results/step5-*.json` 删掉再让它回读 → 该端应显示「未给出该端的编译结论」且本步判失败
    （不静默放过）；
28. **助手不得回调插件（死锁防护）**：Step 4 / Step 5 执行期间观察操作记录 —— 助手**不应**出现调用
    `newproject-*` 技能或插件工具的记录；若出现，说明提示词边界失效（会自等死锁），按缺陷处理；
29. **工具白名单**：Step 5 里若助手尝试白名单外命令（如 `powershell` / `curl`），应被**拒绝**并记入操作记录，工作流不中断（助手改用白名单内命令重试）。

### P4 增补 · 冒烟缺陷修复项（v0.23）

30. **回到 Step 5 能编译（核心回归）**：从 Step 7（或任意靠后的步）**点步骤条「5 编译」行**（或连点「← 上一步」）
    → 左列渲染出 Step 5 操作区 → 「开始编译（逐端）」可点并跑通。修复前这一步**没有任何入口**（真机缺陷现场）。
31. **回退不越界**：未到达的步骤行**灰化不可点**（如首次走到 Step 3 时，Step 5/6/7 行点不动）；
    「← 上一步」在第一可用步动作区时灰化。
32. **回看已完成步骤不显示「未开始」**：回到 Step 3/4 时，步骤条上这两行仍显示「已完成」（状态取自档案终态快照），
    不会因为游标回退而变回「未开始」。
33. **编译通过后视图不被弹走**：此前的缺陷档案（已到 Step 7、Step 5 从未跑）回 Step 5 编译**成功**后，
    视图**停留 Step 5**（能看到逐端结论），指针不会因为「早就到过 Step 7」把游标顶掉；重启面板则回到 Step 7（游标不落档）。
34. **编译未过时「重新推送」被拒（能力层）**：在 Step 5 未跑 / 失败 / 有端失败时，点「重新推送（重跑 Step 6）」（或聊天入口）
    → 被拒并提示「编译（Step 5）尚未全部通过，已拒绝推送…」，**不产生任何推送**；编译全部通过后同一入口放行。
35. **常驻重推入口不得提前出现**：Step 3 刚完成（推进指针 = Step 4）时，「档案与维护」卡里**不应**出现「重新推送（重跑 Step 6）」；
    走到 Step 6 后才出现。
36. **Step 1 重提的确认门**：建好档案后从步骤条回到 Step 1 → 「下一步」文案已是「清空档案并重走 Step 2」；
    点它 → 出现卡内确认横幅（确认 / 取消）；点「取消」无任何副作用（档案、时间线、视图都不变）；
    点「确认」→ 清空档案并从 Step 2 重走（不再撞「已存在同名工作区」的死胡同）。

## 阶段进度

| 批次 | 内容 | 状态 |
|------|------|------|
| **P1** | 工程骨架、清单、档案模型、共享能力层契约与桩、向导面板骨架、铺包通路、源生成 JSON 通路 | **已完成**（构建 0 错误；框架侧生命周期探针全绿，含「停用后 ALC 真回收」；用户人工冒烟通过） |
| **P2** | Step 1 表单 + Step 2 初始化项目环境（工作区 / 会话 / 技能 / 云效 / 项目助手） | **已完成**（构建 0 错误；含唯一性①死路修复与「中止并清理」补齐，详见上文「Step 1 / Step 2 实现要点」） |
| P3 | Step 3 预检 + 按模板建库并克隆（运行期自动建组 + 实时解析 pin，框架 `CodeupClient` 直连）+ Step 6 推送 | **代码完成 + 真机首轮排障已修**（构建 0 错误；修复 RC1 建库/克隆竞态、RC2 git 报错被吞、RC3 失败残留、RC4「复用已克隆」假成功、RC5 只读 pack 挡住「清空后重克隆」导致三选一死锁；新增 `LubanAgentCore.Utils.DirectoryCleaner` + 4 条单测，测试 37/37 通过；待重启 codex 复跑真机 11–21 项） |
| P4 | Step 4 数据库探活（只读）+ Step 5 逐端编译（技术路线 ②：助手亲自执行 + 结论文件回读判定） | **代码完成 + 冒烟缺陷已修（v0.23）**（构建 0 错误；`LubanAgent.Tests` **132/132** 通过；新增提示词 / 结论解析与归并 / 探活参数与默认值 / 工具白名单 / **推进指针与推送闸门** 5 组单测；修复「Step 5 无入口 → 越级推送」根因——步骤条回退导航 + `StepGate.CanPush` 能力层闸门 + 重推入口可见性收紧；**待真机冒烟**——见「P4 增补」22–29 与「P4 增补 · 冒烟缺陷修复项」30–36） |
| P5 | Step 7 创建流水线 + 远端产物孤儿清单化展示（本地回收已在 P2 完成） | 待办 |
| P6 | 6 个技能正文细化 + 聊天入口与向导联调 | 进行中（Step 4/5 两个技能正文已按 v0.22 改写为「助手亲自执行」，主 skill 编排顺序已同步；Step 7 与联调待办） |