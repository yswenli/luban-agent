# LubanAgent.Plugin.NewProject（新建项目插件）

> PRD：`docs/PRD-插件体系与新建项目插件.md`（v0.23）· 里程碑 **M4**
> 插件标识：`com.tjces.newproject` · 显示名：新建项目

以 TJCES 三端模板仓创建新项目：**七步向导**（左侧栏面板）+ **聊天式入口**（`newproject-*` 技能），
两条入口共用同一个 `INewProjectService`，云效逻辑零分叉。
**（v0.22）** Step 4 / Step 5 的动作**不由插件执行**：插件下发提示词与硬边界 → **项目会话的助手亲自探活 / 编译** → 插件回读**结论文件**判定（技术路线 ②）。
**（v0.23）** 新增**步骤条回退导航**（可回到任一「已到达」步骤重看 / 重跑，只动面板视图游标、不动档案）与**推送闸门** `StepGate.CanPush`（编译未全部通过即拒推，向导 / 聊天同受保护）——修「Step 5 无入口导致越级推送」的真机缺陷，详见「回退导航与推送闸门」。**（v0.23 收口）** 审查发现 1–5 的修正：同目录重提不弹破坏性确认（改指路「重跑 Step 2」）、闸门拒绝不写 `Push` 终态且前置到档案空判之后、Step 3 重跑作废旧编译结论（`StepGate.InvalidateBuild`）、未到达步骤行显式降不透明度、重推按钮补 hover 反馈。**（v0.25 复审收口）** 二次逐行审查再修 5 处，其中一处是**阻断级**：**落库顺序 P0** —— Step 4 / Step 5 原先「先 `SaveSpec` 后 `EndOperation`」，而终态快照只回放**终态**记录，于是本轮记录此刻仍是 `Running` 被跳过，`StepStates` 里**根本没有 `Build` 键** → 闸门 `CanPush` 恒判否，**编译通过了也推不动**；已改为「先记终态再落库」（与 Step 6 同型）；另：同目录判据改用与服务端唯一性①同源的 `WorkspaceIdGenerator.Normalize`（解析 junction / 符号链接）、步骤行灰化只按「是否已到达」（忙碌时不再把当前步与已完成步一起变淡）、同目录黄字先清上一轮红字并补「中止并清理」出口。

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

1. 目标分支取配置 `pushBranch`（**v0.28 起默认 `develop`**，PRD §6 #17 改口径；留空才退回该端仓库默认分支）→
   `git rev-parse --verify --quiet refs/heads/<branch>` 已存在则 `checkout <branch>`，否则 `checkout -b <branch>`（自模板 pin 分支）；
2. `git add -A` → `git diff --cached --quiet` 判空，无改动**跳过提交**（不产生空提交）；
3. 有改动才提交，作者取本机 `git config user.name/user.email`，缺失时用配置 `commitAuthorName/Email` 兜底；
4. `CodeupClient.PushAsync(cloneUrl, dir, <branch>)`（连接器内置鉴权 URL 与出口门禁，**不强制推送**）；
   **（v0.29）限流退避重试**：推送失败先判「临时 / 永久」——只有 `GitCommandResult.IsRateLimited`
   （云效 `请求过于频繁，请稍后重试!` / HTTP `429`）才经 `PushRetryPolicy` 退避 **10s → 20s → 30s、最多 3 次**重推；
   非快进 / 鉴权 / 权限类失败**一律不重试**。等待期间同一条「进行中」记录被**原地改写**为
   「…限流（原文）：20s 后重试（第 2/3 次）」（`UpdateRunningOperation`，不摊成多条记录），重试成功则记
   「已推送 <branch> → …（云效限流后，第 N 次重试成功）」；三次用尽仍失败才置 `Failed`，文案**只给
   「稍等约 1 分钟后点『重新推送』」且不再提 PAT / 分支保护**（真机证据：同一条 PAT、同一个 `develop` 分支
   在 11 秒前的重试里推成功了）。取值依据即真机读数：11s 重推已过、3s 仍被限流，故首个退避取 10s；
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
- **回退范围**：**只放开「推进指针以内」的步骤**（`step <= reached`）；未到达的步骤行灰化——顺序执行仍是硬约束，回退只是「回到已到达的步重看/重跑」。灰化是**显式降整行不透明度**（`Opacity = 0.45`）：行内 `TextBlock` 都显式设了前景色，主题的「禁用前景色」覆盖不到，只靠 `IsEnabled` 肉眼看不出区别（P4 冒烟缺陷）。
- **回退只切视图**：不改档案、不自动重跑任何动作；重跑由该步操作区的按钮显式触发。
- **何时丢弃游标**：`Refresh()` 内按两条规则——①本次刷新**观察到推进指往前移**且越过游标（刚执行的动作把流程推到了后一步）→ 丢游标、视图跟上新步骤；②游标 > 推进指针（档案被「中止并清理」重置）→ 丢游标。
  **关键**：回看**早已到达**的步骤不产生前移，游标保持——所以「已到 Step 7 后回 Step 5 重编译成功」不会把视图弹走，用户能就地看到逐端结论（这正是回退导航的价值）。
- **步骤条状态**仍取「档案终态快照 ⊕ 本次记录」（`StepStateResolver`），所以回看已完成步骤仍显示「已完成」，不会退回「未开始」；高亮的是**视图游标**所在的步。
- **重启语义**：游标不落档 → 重新打开面板回到档案的「已到达最大步」。
- **Step 1 重提（破坏性）**：已建档案后从步骤条回到 Step 1、**改填另一个本地目录**再点「下一步」＝ **清空档案并按新目录从 Step 2 重来**，故按钮文案为「清空档案并重走 Step 2」，且**先弹卡内确认横幅**（确认 / 取消），不静默覆盖档案（PRD §4.7）。
- **同目录不算「另建项目」**：表单本地目录与档案 `LocalPath` 相同（`SameLocalPath`：直接用 `WorkspaceIdGenerator.Normalize`，与服务端唯一性口径①**同源**——解析符号链接 / junction、去尾部分隔符、Windows 统一大写）时**不弹破坏性横幅**，改为黄字指路「沿用请点维护卡的『重跑 Step 2』；想要全新档案走『中止并清理』」。原因：该目录已被宿主登记为工作区，重提必被唯一性口径①拦下，而「复用该工作区继续」按钮在有档案时按设计不出现——确认了只会撞一条指不到出路的红字。

#### 推送闸门 `StepGate.CanPush(spec)`

纯函数（`Services/StepGate.cs`），判据 = **`StepStates["Build"] == Succeeded` ∧ `BuildResults.Count > 0` ∧ 逐端全 `Ok`**，缺一即拒。`NewProjectService.PushAsync` **紧跟「档案为空」判定之后**就复核（先于 PAT / 克隆 / 仓库就绪等前置检查——顺序约束比参数齐备更根本，报错先给最该修的那条），向导按钮 / 常驻重推 / 聊天入口**同受保护**。

- 最关键的一条：**从未跑过 Step 5** → 档案里根本没有 `Build` 键 → 拒绝（真机档案正是这个形态）。
- 判据取**档案事实**（终态快照 + 逐端结论，跨重启保留），不取界面状态或内存记录。
- 拒绝文案：「编译（Step 5）尚未全部通过，已拒绝推送：请到步骤条的「编译」步逐端编译，全部通过后本步才可执行（编译结论见 Step 5 的逐端状态行与右侧操作记录）。」
- **拒绝不记操作记录**：一次「未执行的拒绝」不是本步的终态——若按旧写法记一条 `Failed`，`SnapshotStepStates` 回放时会把它写进档案，把早已成功的 `Push` 状态改写成失败（步骤条显示随之中招）。提示由 `StepResult.Message` 承载，不落档案。
- **反向操作 `StepGate.InvalidateBuild(spec)`**：作废编译结论（清 `BuildResults` + 移除 `StepStates["Build"]`），与 `CanPush` 成对放在同一处，免得两处口径各自漂移。调用点是 **Step 3 重跑成功之后**——本地代码可能已随模板 / pin / 占位符替换而变，不作废就会出现「改完代码不重编也能推」（闸门只看档案事实，会误放行）。服务层 `InvalidateBuildConclusion` 还须清掉本进程内存里遗留的 `Build` 记录，否则 `SnapshotStepStates` 回放会把刚清掉的键「复活」。
- **判据的产出方（落库顺序，v0.25 P0）**：终态快照由 `SaveSpec` → `SnapshotStepStates` 按**内存操作记录**回放生成，且**只回放终态记录**；故产生结论的那一步必须「**先 `EndOperation` 记终态、再 `SaveSpec` 落库**」。Step 4 / Step 5 原先顺序相反（先保存后记终态），本轮记录此刻还是 `Running` 被跳过 → `StepStates` 里没有 `Build` 键 → 闸门**恒判否**，编译通过了也推不动。口径：`StepStates` 里的键要能被闸门读到，生产者就得在保存前把记录置为终态（Step 3 / Step 6 本来就是这么写的）。

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
| 通过判定 | **四环齐**：①文件存在 ②schema 可解析（`StepReportParser`，走源生成上下文） ③`ok` 为真 ④本轮事件流无错误事件（**守卫拒绝 / 用户取消不算错误**，见下「助手工具白名单」与 v0.27） |
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

### Step 5：逐端编译（编译方式交助手判断 + 按端已知构建姿势）

- **插件不预设命令**：助手先看仓库里有什么（`.sln` / `.csproj` / `package.json` / `pubspec.yaml`）再自行决定；
  提示词里另给一段【本组织已知构建姿势】（v0.28 新增），**与本组织仓库既有做法冲突时以提示词为准**。
- **管理端（Admin，Vite + Vue）已知姿势（v0.28 本机实测通过）**：分支 `develop`；Node 16 —— 所有命令用
  `fnm exec --using=16 -- npm.cmd <子命令>` 包一层（**Windows 下必须写 `npm.cmd`**，`fnm` 直接 spawn `npm` 报 `program not found`；
  管道 + `Invoke-Expression` 那类写法仍被守卫拒绝）；装依赖 `npm install --registry=https://registry.npmmirror.com`（或 `cnpm`）；
  构建 **`npm run build:dev`（不是 `npm run build`：该仓库的构建脚本叫 `build:dev`）**。
- `checks[].name` **必须用端标识**（`Service` / `Admin` / `Mobile`），**逐端齐全**——漏端即判「该端未给结论」＝**本步失败**（不用「其余端通过」兜底，否则漏跑一端会被静默放过）。
- **失败阻断 Step 6**；**只编译、不改业务**（v0.28 放宽）：**允许修依赖树与环境**——装依赖、`fnm` 切 Node、
  重跑依赖自身的构建脚本（`npm rebuild X`）、必要时重建 `node_modules`；**禁止改业务源码与工程配置**
  （业务代码、`package.json`、`vite.config`、`.csproj` / `.sln` 等一律不改；锁文件不要手工编辑，包管理器装依赖时自动更新属正常副作用）。不清理编译产物。
- **真机根因（v0.28 闭环）**：管理端 `@vue-office/{docx,excel,pdf}` 的 `package.json` 里 `main = lib/index.js`，
  而 `lib/index.js` **不随包发布**——由包自身 `postinstall`（`lib/script/postinstall.js` → `utils.js` 的 `switchVersion(3)`，
  即把 `lib/v3/index.js` 复制成 `lib/index.js`，`index.css` 同理）生成。pnpm v10 默认跳过依赖的构建脚本 → 入口缺失 →
  `vite build` 报 `Failed to resolve entry for package "X"`。v0.27 曾判定「正解是在仓库侧执行 `pnpm approve-builds`，
  属仓库配置变更、不该让助手动」；v0.28 复核后改为：**这条路根本不需要 pnpm** —— 该仓库用 **npm 系**
  （`npm install` 默认跑依赖构建脚本）× **Node 16** 就是本组织既有姿势，助手在白名单内即可把依赖树修好
  （本机实测：`npm install` 81s → `npm run build:dev` exit 0 / 89s，`dist` 正常产出）。
  若装完仍报入口缺失，用 `fnm exec --using=16 -- npm.cmd rebuild X` 补跑后重试一次（实测可重新生成入口）。
  （附带结论：不带参数的 `pnpm approve-builds` 是**交互式**命令，助手在无 TTY 的 Shell 里本就跑不动。）

### 助手工具白名单（替代 `BypassPermissions`）

`AssistantToolPolicy.Decide` 对每次工具调用给结论，落 `Allow` / `Deny` 并写入操作记录（含依据）：

- **放行**：只读工具（`ReadFileAsync` / `ListDirectoryAsync` / `GetWorkspaceOverviewAsync` / `UseSkillAsync` 等）、`RunPythonAsync`、
  命令在白名单内的 `RunShellAsync`、**工作区内**的写类工具（`WriteFileAsync` / `CopyFileAsync` 按 `destPath` / `MoveFileAsync` 按 `destPath`）。
- **拒绝**：`RunLuaAsync`、删除类（`DeleteFileAsync` / `DeleteDirectoryAsync`）、技能与 MCP 写删类、越出工作区的写入、未知工具、工作区根未知时的任何写入。
- **Shell 逐段判定**：按 **未加引号** 的 `&&` / `||` / `;` / `|` / 换行切段（引号内的分隔符属参数文本、不切段——`node -e "…;…"` / `git commit -m "a;b"` 必须放行；单引号内除 `''` 外不转义、双引号内反引号转义、未闭合引号按「到末尾都在引号内」处理；单个 `&` 不切段，只有 `&&` 切），每段取首词（支持 `& "prog"` 与 `"C:\Program Files\...\dotnet.exe"` 引号路径，去目录、去 `.exe/.cmd/.bat/.ps1` 后缀），
  首词不在 `AllowedExecutables`（`dotnet` / `git` / `node` / `npm` / `npx` / `pnpm` / `yarn` / `fnm` / `cnpm` / `python` / `py` / `python3` / `pip` / `pip3`）与 pwsh 无副作用内置（`cd` / `Set-Location` 等）内即**整条拒绝**；
  `$env:X = "..."` 赋值段视为无副作用放行。
- **v0.28 新增 `fnm` / `cnpm`**：本组织管理端要求 Node 16 + npm 系客户端，助手需用 `fnm exec --using=16 -- npm.cmd …`
  与 `cnpm`（内网镜像客户端）。二者都只是「版本管理器 / 包管理器」，不引入新的越界面；
  `fnm env --use-on-cd | Out-String | Invoke-Expression` 那类写法**仍被拒**（管道分段后 `Out-String` / `Invoke-Expression`
  不是白名单首词，且 `Invoke-Expression` 等同任意求值——提示词里改为 `fnm exec --using=16 -- …` 的免管道形态）。
- **拒绝 ≠ 本轮失败（v0.27）**：被拒的调用**不计进事件流错误**（`AssistantToolPolicy.IsGuardDenial` 认宿主文案「工具调用被拒绝或已取消」），只留在操作记录「被拒明细」里——否则助手**试错一次就被自己的守卫判成整步失败**（四环第④环会中招）。早先的实现正是把守卫拒绝塞进 `errors`，真机两轮都因此判失败。

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
`NewProjectAssistantToolPolicyUnitTest`（工具白名单 + Shell 逐段判定 + 越界拒绝 + **引号内分隔符不切段** + **守卫拒绝文案判定**）、`StepStateResolverUnitTest`、
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
    云效仓库出现 **`develop`** 分支与首次提交（**v0.28：`pushBranch` 默认已由 `main` 改为 `develop`**，与 Step 3 克隆 pin 一致）；再点一次 → 无改动时显示「工作区无改动，跳过提交」，推送仍成功（幂等）；
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
    管理端 `pnpm` 系列），各端结论与「实际执行的命令」一并展示；**全部通过后**结论文案给出「可在步骤条点『6 推送』继续」的导航提示
    （Step 5 操作区本身没有「下一步」按钮）；失败时阻断 Step 6（重推会被闸门拒绝）。
27. **Step 5 漏端防护**：手工把某端的结论从 `results/step5-*.json` 删掉再让它回读 → 该端应显示「未给出该端的编译结论」且本步判失败
    （不静默放过）；
28. **助手不得回调插件（死锁防护）**：Step 4 / Step 5 执行期间观察操作记录 —— 助手**不应**出现调用
    `newproject-*` 技能或插件工具的记录；若出现，说明提示词边界失效（会自等死锁），按缺陷处理；
29. **工具白名单**：Step 5 里若助手尝试白名单外命令（如 `powershell` / `curl`），应被**拒绝**并记入操作记录，工作流不中断（助手改用白名单内命令重试）。

### P4 增补 · 冒烟缺陷修复项（v0.23）

30. **回到 Step 5 能编译（核心回归）**：从 Step 7（或任意靠后的步）**点步骤条「5 编译」行**（或连点「← 上一步」）
    → 左列渲染出 Step 5 操作区 → 「开始编译（逐端）」可点并跑通。修复前这一步**没有任何入口**（真机缺陷现场）。
31. **回退不越界**：未到达的步骤行**整行明显变淡**（不透明度降到 0.45）**且点不动**（如首次走到 Step 3 时，Step 5/6/7 行点不动）；
    「← 上一步」在第一可用步动作区时灰化。
32. **回看已完成步骤不显示「未开始」**：回到 Step 3/4 时，步骤条上这两行仍显示「已完成」（状态取自档案终态快照），
    不会因为游标回退而变回「未开始」。
33. **编译通过后视图不被弹走**：此前的缺陷档案（已到 Step 7、Step 5 从未跑）回 Step 5 编译**成功**后，
    视图**停留 Step 5**（能看到逐端结论），指针不会因为「早就到过 Step 7」把游标顶掉；重启面板则回到 Step 7（游标不落档）。
34. **编译未过时「重新推送」被拒（能力层）**：在 Step 5 未跑 / 失败 / 有端失败时，点「重新推送（重跑 Step 6）」（或聊天入口）
    → 被拒并提示「编译（Step 5）尚未全部通过，已拒绝推送…」，**不产生任何推送**；编译全部通过后同一入口放行。
    被拒时**步骤条上 Step 6 的状态不受影响**（拒绝不算一次推送终态，不产生 `Push` 失败记录/快照）；
    另注意：此拒绝**先于** PAT / 克隆检查报出（顺序约束比参数齐备更根本）。
35. **常驻重推入口不得提前出现**：Step 3 刚完成（推进指针 = Step 4）时，「档案与维护」卡里**不应**出现「重新推送（重跑 Step 6）」；
    走到 Step 6 后才出现。
36. **Step 1 重提的同目录分支**：建好档案后从步骤条回到 Step 1，**本地目录保持不变**（就是档案工作区）→ 点「下一步」应出**黄字提示**
    「本地目录就是当前档案（…）的工作区：沿用请点『档案与维护』卡里的『重跑 Step 2』…」，**不弹破坏性确认横幅、不清档案**；
    按提示点「重跑 Step 2」→ 复用同一工作区重走（进度不丢）。
37. **Step 1 重提的确认门（改目录分支）**：回到 Step 1 后**把本地目录改填成另一个目录** → 点「下一步」→ 才出现卡内确认横幅（确认 / 取消）；
    点「取消」无任何副作用（档案、时间线、视图都不变）；点「确认」→ 清空档案并按新目录从 Step 2 重走。
38. **Step 3 重跑作废旧编译结论**：Step 5 全部通过后回到 Step 3 重跑（成功）→ 再回 Step 5，结论区应显示「未开始/无结论」形态，
    且此时点「重新推送」应被闸门**拒绝**（本地代码可能已变，必须重新编译）；重新编译通过后再推即放行。
39. **重推按钮有可点反馈**：「重新推送（重跑 Step 6）」鼠标悬停时应变底色（不再是「看不出能不能点」）。

#### P4 复审增补 · v0.25（落库顺序 P0 等）

40. **编译通过后立刻能推（P0 回归，必测）**：Step 5 全部通过后（**不重启面板、不夹做别的步**）→ 打开
    `{LocalPath}\.luban-agent\plugin-newproject\spec.json`，`stepStates` 里应出现 `"Build": "Succeeded"`；
    随手点「6 推送」/「重新推送」应**放行**（不再被闸门拒）。修复前必然被拒——`StepStates` 里没有 `Build` 键，
    而步骤条却显示 Step 5「已完成」（判据来自记录，闸门读档案快照）。
41. **各步终态在**自己那一步**就落账（回归）**：Step 4 探活结束后 `stepStates` 立刻有 `"Database"`；Step 5 结束后立刻有 `"Build"`。
    编译**失败**时同样落账（`"Build": "Failed"`）→ 重启面板后步骤条 Step 4 / Step 5 仍显示真实终态，而非「未开始」。
42. **junction 目录不弹破坏性确认（同源判据）**：把档案工作区目录做成 junction（`mklink /J <别名> <真实目录>`）后，
    从步骤条回 Step 1、本地目录填**真实目标路径** → 点「下一步」应出**黄字**（判为同目录），**不弹**破坏性确认横幅。
43. **忙碌期间灰化不误导**：任一步执行中（`_busy`）步骤条**已到达的行不变淡**（含当前高亮步与已完成步），
    只有**未到达的行**保持 0.45 不透明度。
44. **同目录黄字无残留红字**：先制造一条红字（如本地目录填成不存在的盘符后点「下一步」），再把目录改回**档案工作区**目录并点「下一步」
    → 只应看到黄字提示（含「中止并清理」出口那条），**不残留上一轮红字**。

#### P4 复审增补 · v0.27（助手守卫两处真缺陷）

45. **引号内的分号不误伤（回归，必测）**：Step 5 里助手执行含引号内分隔符的命令（`node -e "…;…"` / `git commit -m "a;b"` / `python -c "import sys; print(sys.version)"`）
    应被**放行并正常执行**（运行记录里该次调用是「放行」而不是「拒绝」）。修复前必被拒——分段器按字面量切 `;`，后半截被当成可执行文件。
46. **守卫拒绝不判本步失败（回归）**：让助手尝试一条白名单外命令（如 `curl` / `powershell`）→ 该调用被**拒绝**并出现在操作记录「被拒明细」里，
    但**只要结论文件 `ok` 为真且事件流无真错误，本步仍判通过**（编译通过时应能继续推送）。修复前必判失败——守卫拒绝被当成「本轮事件流有 1 条错误」。

#### P4 复审增补 · v0.28（管理端构建姿势 + 边界放宽）

47. **管理端用本组织姿势能编过（核心回归，必测）**：重启 codex 后跑 Step 5，管理端应走
    `fnm exec --using=16 -- npm.cmd install --registry=https://registry.npmmirror.com` → `fnm exec --using=16 -- npm.cmd run build:dev`，
    结论 `ok: true`；运行记录里能看到这两条命令**被放行**（`fnm` / `cnpm` 在白名单内）。修复前助手只会 `pnpm run build` → 报入口缺失。
48. **`fnm` / `cnpm` 放行、管道形态仍拒（回归）**：`fnm exec --using=16 -- npm.cmd run build:dev` 与 `cnpm install`
    应放行；`fnm env --use-on-cd | Out-String | Invoke-Expression` 应**被拒**（管道分段后首词不是白名单可执行文件），
    且该拒绝**不判本步失败**（见第 46 项）。
49. **依赖入口缺失可自修（回归）**：人为删掉 `node_modules\@vue-office\pdf\lib\index.js` 后跑 Step 5，
    助手应改用 `fnm exec --using=16 -- npm.cmd rebuild @vue-office/pdf` 补跑并最终编过（本轮边界放宽后允许）；
    **边界仍守住**：业务源码与工程配置（`package.json` / `vite.config` / `.csproj` / `.sln`）不得被改写。
50. **Step 6 推的是 develop 不是 main（v0.28 口径）**：Step 5 通过后点「重新推送（重跑 Step 6）」→
    云效各端仓库的 **`develop`** 分支上出现本次提交（`main` 分支不应被新建/更新）；若仓库里 `develop` 尚不存在，
    时间线应显示从模板 pin 分支创建 `develop` 后再推送（配置来源＝包内 `newproject.settings.json` 的 `pushBranch`）。

#### P4 复审增补 · v0.29（Step 6 限流退避重试 + 文案误归因）

51. **限流会自动重试（核心回归，必测）**：Step 5 通过后点「6 推送」/「重新推送（重跑 Step 6）」，若云效返回
    `remote: 请求过于频繁，请稍后重试!`，时间线里那条「推送到自有仓库（X端）」应**原地变成**
    「云效服务端限流（…）：10s 后重试（第 1/3 次）」并继续往下跑（**不新增第二条记录**，同一条记录只在等待与结束间改写）；
    重试成功后应记「已推送 develop → …（云效限流后，第 N 次重试成功）」且 Step 6 判成功。修复前是一次失败即整步失败。
52. **限流失败文案不得误归因（回归，必测）**：让三次重试全部用尽（例如断网、或在限流窗口内反复触发）后，失败文案应为
    「…云效服务端限流：本轮已自动退避重试 3 次仍未成功，建议等约 1 分钟后点「重新推送（重跑 Step 6）」…」，
    **文案里不得出现「PAT」「分支保护」字样**（修复前必现「请确认 PAT 具备该仓库写权限，且目标分支未被保护」）。
53. **非限流失败不进退避等待**：制造一条非快进失败（远端 `develop` 已有本地没有的提交）→ 推送应**立即失败**、
    不出现「Ns 后重试」，且文案仍给出排查方向（「若持续失败，请检查 PAT 是否具备该仓库写权限、目标分支是否被保护」）。
54. **提交信息不再重字（回归）**：Step 6 提交成功后的记录与仓库 `git log` 应为
    `chore: 初始化 <项目名>（管理端）——由「新建项目」向导按模板 TJCES-Admin 生成`，
    时间线上也应是「整理分支（管理端）」「提交改动（管理端）」——**不得出现「管理端端」「后端端」「移动端端」**
    （修复前真机提交即 `chore: 初始化 测试项目一（管理端端）——…`）；同一批修正也覆盖界面文案：Step 3 表单字段为
    「管理端目标仓库名」、端分组标题为「管理端」、目录冲突横幅为「管理端目标目录已存在且非空…」
    （服务层 5 处 + UI 4 处，共 9 处）。

## 阶段进度

| 批次 | 内容 | 状态 |
|------|------|------|
| **P1** | 工程骨架、清单、档案模型、共享能力层契约与桩、向导面板骨架、铺包通路、源生成 JSON 通路 | **已完成**（构建 0 错误；框架侧生命周期探针全绿，含「停用后 ALC 真回收」；用户人工冒烟通过） |
| **P2** | Step 1 表单 + Step 2 初始化项目环境（工作区 / 会话 / 技能 / 云效 / 项目助手） | **已完成**（构建 0 错误；含唯一性①死路修复与「中止并清理」补齐，详见上文「Step 1 / Step 2 实现要点」） |
| P3 | Step 3 预检 + 按模板建库并克隆（运行期自动建组 + 实时解析 pin，框架 `CodeupClient` 直连）+ Step 6 推送 | **代码完成 + 真机首轮排障已修**（构建 0 错误；修复 RC1 建库/克隆竞态、RC2 git 报错被吞、RC3 失败残留、RC4「复用已克隆」假成功、RC5 只读 pack 挡住「清空后重克隆」导致三选一死锁；新增 `LubanAgentCore.Utils.DirectoryCleaner` + 4 条单测，测试 37/37 通过；待重启 codex 复跑真机 11–21 项） |
| P4 | Step 4 数据库探活（只读）+ Step 5 逐端编译（技术路线 ②：助手亲自执行 + 结论文件回读判定） | **代码完成 + 冒烟缺陷已修（v0.23）**（构建 0 错误；`LubanAgent.Tests` **135/135** 通过；新增提示词 / 结论解析与归并 / 探活参数与默认值 / 工具白名单 / **推进指针与推送闸门** 5 组单测；修复「Step 5 无入口 → 越级推送」根因——步骤条回退导航 + `StepGate.CanPush` 能力层闸门 + 重推入口可见性收紧；并完成审查收口：同目录重提不给破坏性确认、闸门拒绝不写 Push 终态、闸门前置、Step 3 重跑作废旧编译结论、未到达行显式灰化与重推 hover 反馈；v0.25 复审再收口：**落库顺序 P0**（Step 4/5 改为「先记终态再落库」，否则闸门读不到 `StepStates["Build"]` 恒判否）、同目录判据补齐 junction 别名、灰化只绑已到达、黄字清残留红字并补「中止并清理」出口；**v0.27**：真机 Step 5 冒烟暴露的**助手守卫两处真缺陷**随修——①Shell 逐段判定改为识别成对引号（引号内的 `;` / `|` / `&&` / 换行不再被误切成命令段，`node -e "…;…"` 不再被误拒）；②守卫拒绝不再计入「本轮错误事件」（只留「被拒明细」），不再把助手的一次试错判成整步失败；`LubanAgent.Tests` **137/137**；真机管理端编译失败的根因是**仓库基线**（pnpm 跳过 `@vue-office/docx` 的构建脚本 → 包入口缺失）；**v0.28**：该根因闭环——Step 5 提示词新增【本组织已知构建姿势】（管理端：`develop` 分支；`fnm exec --using=16 -- npm.cmd …`；`npm install --registry=https://registry.npmmirror.com`；**`npm run build:dev`** 而非 `npm run build`；报 `Failed to resolve entry for package "X"` 时 `npm.cmd rebuild X` 补跑），白名单补 `fnm` / `cnpm`，边界由「只编译、不修复」放宽为「**只编译、不改业务**」（允许修依赖树与环境，禁改业务源码与工程配置）；本机实测管理端 `npm install` 81s → `build:dev` exit 0 / 89s，`dist` 正常产出；另按组织口径把包内 `pushBranch` 默认由 `main` 改为 **`develop`**（对齐 Step 3 克隆 pin，配置改值、无代码改动）；**v0.29**：Step 6 真机推送缺陷闭环——①云效**服务端限流**（`remote: 请求过于频繁，请稍后重试!`）原先被当成「PAT 权限 / 分支保护」误归因、且一次失败即整步失败（真机后端隔 11s 重推成功、管理端 3s 后又被限流即挂），现按 `GitCommandResult.IsRateLimited` 分流并经 `PushRetryPolicy` **退避 10s → 20s → 30s、最多 3 次**（只重试限流，非快进 / 鉴权 / 权限不重试），等待期间原地改写同一条记录，文案不再提 PAT（聊天出口 `mcp_yunxiao_codeup_push` 同步）；②顺带修正 `$"{EndLabel(end)}端"` 造成的「管理端端 / 后端端」重字（含 Step 6 提交信息）；`LubanAgent.Tests` **148/148**；**待真机冒烟**——见「P4 增补」22–29、「P4 增补 · 冒烟缺陷修复项」30–46 与「P4 复审增补 · v0.28」47–50、「P4 复审增补 · v0.29」51–54） |
| P5 | Step 7 创建流水线 + 远端产物孤儿清单化展示（本地回收已在 P2 完成） | 待办 |
| P6 | 6 个技能正文细化 + 聊天入口与向导联调 | 进行中（Step 4/5 两个技能正文已按 v0.22 改写为「助手亲自执行」，主 skill 编排顺序已同步；Step 7 与联调待办） |