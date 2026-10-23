# LubanAgent.Plugin.NewProject（新建项目插件）

> PRD：`docs/PRD-插件体系与新建项目插件.md`（v0.32）· 里程碑 **M4**
> 插件标识：`com.tjces.newproject` · 显示名：新建项目

以三端模板仓创建新项目：**七步向导**（左侧栏面板）+ **聊天式入口**（`newproject-*` 技能），
两条入口共用同一个 `INewProjectService`，云效逻辑零分叉。
**（v0.22）** Step 4 / Step 5 的动作**不由插件执行**：插件下发提示词与硬边界 → **项目会话的助手亲自探活 / 编译** → 插件回读**结论文件**判定（技术路线 ②）。
**（v0.23）** 新增**步骤条回退导航**（可回到任一「已到达」步骤重看 / 重跑，只动面板视图游标、不动档案）与**推送闸门** `StepGate.CanPush`（编译未全部通过即拒推，向导 / 聊天同受保护）——修「Step 5 无入口导致越级推送」的真机缺陷，详见「回退导航与推送闸门」。**（v0.23 收口）** 审查发现 1–5 的修正：同目录重提不弹破坏性确认（改指路「重跑 Step 2」）、闸门拒绝不写 `Push` 终态且前置到档案空判之后、Step 3 重跑作废旧编译结论（`StepGate.InvalidateBuild`）、未到达步骤行显式降不透明度、重推按钮补 hover 反馈。**（v0.25 复审收口）** 二次逐行审查再修 5 处，其中一处是**阻断级**：**落库顺序 P0** —— Step 4 / Step 5 原先「先 `SaveSpec` 后 `EndOperation`」，而终态快照只回放**终态**记录，于是本轮记录此刻仍是 `Running` 被跳过，`StepStates` 里**根本没有 `Build` 键** → 闸门 `CanPush` 恒判否，**编译通过了也推不动**；已改为「先记终态再落库」（与 Step 6 同型）；另：同目录判据改用与服务端唯一性①同源的 `WorkspaceIdGenerator.Normalize`（解析 junction / 符号链接）、步骤行灰化只按「是否已到达」（忙碌时不再把当前步与已完成步一起变淡）、同目录黄字先清上一轮红字并补「中止并清理」出口。
**（v0.30 · Step 7 真机取证改写）** 云效控制台现存 70 条流水线**全是 tb 形态**，其 `flow` 原文提交必报 `1209300 校验失败`，且 OpenAPI 无「复制流水线」接口——故 Step 7 由「**克隆参考流水线**」改为「**内嵌新版 YAML 模板 + 占位符渲染 + 创建**」：三端模板（含**变量与缓存**：`variables` 的 `DOCKER_IMAGE_NAME` / `APP_PORT` / `DOCKER_CONTAINER_NAME` / `HARBOR_*` + 顶层 `cache`）内嵌于 `PipelineTemplates.cs`（`PipelineTemplateLibrary` / `PipelineRenderer`，随包分发、可单测），MCP 侧 `flow_clone_pipeline` 由 **`flow_create_pipeline`**（`name` + `content` + `organizationId`）取代；部署阶段默认禁用（只作注释附末尾）并可「一键补齐部署阶段」（仅后端，组件 `VMDockerDeploy`）。真机校正：变量**不带 `type`**（私密值用 `secret: true`）、`NodeBuild` 用 `versionType/customNodeVersion`、`OSSUpload` 用 `sourceFilePath/targetFilePath`、`OSSDelete` 用 `prefix`、`runsOn` **默认不写**（私有集群只能人工回填）、`VMDockerDeploy.machineGroup` **必须是主机组 uuid**。详见「Step 7 实现要点」与「P4 复审增补 · v0.30」。

**（v0.31 · Step 7 YAML 可手工微调）** 预览不再只是「列出来」：点「编辑 YAML…」在独立窗口里改选中端的提交原文（按端、**会话内**保留，「重置为模板」可回退），创建 / 一键补齐直接提交微调后的文本；提交前做最小校验（非空 / 无 Tab / 有顶层 `stages:` / `name:` 必须与模板名一致，其余语法交给云效原样报回），补齐时在微调文本末尾自动追加部署阶段（已自写 `VMDockerDeploy` 则不重复追加）。

**（v0.32 · 部署目标改为「创建前选择」）** 删掉「部署阶段默认禁用 + 注释块 + 事后一键补齐」那一套（它把「往哪台机器发布」这一件事推到了创建之后，且注释块在真机 YAML 里既不能执行、又容易被误删）：改为**创建流水线之前**给出「部署目标」——只读拉取云效主机组列表（连接器新增 `ListHostGroupsAsync` / `GetHostGroupAsync`，MCP 新增 `flow_list_host_groups` / `flow_get_host_group`）→ 面板下拉（**仅后端 Service 可选**，默认预选 `pipelineDefaults[Service].deployMachineGroup`，可展开机器明细核对落点）→ **创建即写**含 `  deploy:`（`VMDockerDeploy` + `machineGroup: '<uuid>'`）的完整 YAML，不再有注释块、不再需要补齐。**拉取失败 / 无权限一律阻断创建**（不降级成手填 uuid——uuid 填错云效只回「该机器组uuid[x]不存在」，是白跑一次且污染远端）。保留一个「**覆盖更新**」入口（原「一键补齐」位）给旧流水线补部署段 / 换目标后纠偏（`UpdatePipeline` 整体覆盖，仍走卡内勾选确认）。档案每端记「本次提交 YAML 是否含部署段」+「所选主机组 uuid / 名」。详见「Step 7 实现要点 · v0.32 部署目标」。

**（v0.32 收尾 · 命名与缩写校验调整）** ①**英文缩写收紧为只允许小写字母 a-z**（`^[a-z]+$`，不再允许数字；Step 1 校验文案与输入框占位提示同步）；②**代码库名默认首字母大写**——`repositoryNameFormat` 由 `{code}-{end}` 改为 `{Code}-{end}`（如 `csxmy` → `Csxmy-Service` / `Csxmy-Admin` / `Csxmy-Mobile`），与**全大写**的代码组名 `{CODE}`（如 `CSXMY`）刻意区分；`BuildRepositoryName` 由 `Replace("{code}", …, OrdinalIgnoreCase)`（会连带替换 `{Code}` / `{CODE}`）改为**区分大小写**展开 `{code}` / `{Code}` / `{CODE}` + `{end}`；**本地目录名与仓库名同源**，故本地目录亦变为 `Csxmy-Service`。

**（v0.32 收尾 · 命中同名改为「覆盖更新」，编排 B · 全端）** Step 7 创建入口按名命中同名流水线时，**不再「命中即复用」**，而是对该条 `UpdatePipeline` 提交本次渲染的 YAML（**全端** Service / Admin / Mobile；`Created = false`，档案 `DeployStageIncluded` 与部署目标随本次渲染变真实；覆盖更新失败即该端失败）——旧流水线可能缺部署段或残留上一版模板 / 占位符，复用会让「预览到的 YAML」与远端实际不一致（真机曾因此让后端流水线没有 `deploy` 段）。**副作用：普通重跑也会覆盖远端**（含控制台上的人工改动），即 **v0.30 §6 #15「命中即复用」被本口径取代**；「覆盖更新」按钮保留（对已有流水线纠偏 / 补部署段）。

**（v0.33 · Step 3/6/7 助手驱动重构 · 知识移出代码）** Step 3 / 6 / 7 的执行体从插件 C# 直连**下沉给项目会话的助手**：插件只负责「**下发提示词 + 固定边界 + 回读结论文件**」，云效动作走 `mcp_yunxiao_*`（`AssistantToolPolicy` 新增**云效 MCP 子策略**，按工具名段放行 12 个：8 只读 + 4 写入；`flow_run_pipeline` 拒绝，已删的 `codeup_clone` / `codeup_push` 双保险仍 Deny），git 动作走**框架 git 工具组**（工作区内**免确认、不经插件策略**）。组织专有值（模板仓名 / Harbor / 服务连接 / bucket / pipelineId / 主机组 uuid …）与三端 YAML 模板从代码迁到**随包分发的 `docs/` 知识文件**，技能正文只写「读 `docs/` 哪一节」，靠**禁用值扫描单测**把「唯一副本」变成可执行检查。Step 3 / 7 为「**一次只读准备下发 + 用户确认后同会话执行续跑**」（**不叫「两轮」**），结论文件扩展 `status`(done/needsInput/failed) / `artifacts[]`(判别属性 `kind` + 具名强类型 `extra`) / `inputs[]`（三选一 / 部署目标）；闸门 `StepGate.CanPush` / `CanCreatePipelines` 判据改读 **Step 5 / Step 6 结论文件**（`StepStates` 降级为仅界面进度）。删除 `Services/PipelineTemplates.cs`（`PipelineTemplateLibrary` / `PipelineRenderer` / `PipelineYamlOverride`）、`PlaceholderReplacer.cs`、`PushRetryPolicy.cs` 与 `CodeupClient.CloneToLocalAsync` / `PushAsync`；新增本地复核器 `StepLocalVerifier`。详见「Step 3 / Step 6 实现要点」「Step 7 实现要点」「验证」。

## 目录结构

```
LubanAgent.Plugin.NewProject/
├─ LubanAgent.Plugin.NewProject.csproj   # 含「铺包」目标（见下；新增 docs/ 铺装）
├─ plugin.json                           # 插件清单：侧栏条目 / 6 个技能 / yunxiao MCP / 权限
├─ newproject.settings.json              # 瘦身后仅留 6 项格式值（组织专有值已迁 docs/）
├─ docs/                                 # 随包分发的知识文件（组织专有值 + 三端 YAML 模板 + 编排约定）
├─ NewProjectPlugin.cs                   # 插件入口（PluginBase）
├─ Abstractions/
│  ├─ ProjectSpec.cs                     # 项目档案与相关模型（PRD §4.5）
│  ├─ BasicInfoValidation.cs             # Step 1 校验结果（字段级错误 + 非阻断提示，面板与聊天入口共用）
│  ├─ RepositoryPrecheck.cs              # Step 3 确认视图模型（数据来源＝结论文件 artifacts[]，C11′）
│  ├─ DatabaseProbe.cs                   # Step 4 探活参数（含校验与口令脱敏的 Describe）
│  ├─ StepReport.cs                      # 结论文件模型（step / ok / status / checks[] / inputs[] / artifacts[] / message）
│  ├─ PipelinePreview.cs                 # Step 7 复核视图模型（数据来源＝结论文件 artifacts[]）
│  └─ INewProjectService.cs              # 共享能力层契约（PRD §4.10.2）
├─ Configuration/NewProjectSettings.cs   # 包内配置读取（瘦身：仅 6 项格式值）
├─ Serialization/PluginJsonContext.cs    # 源生成 JSON 上下文（登记全部新 schema 类型）
├─ Services/
│  ├─ NewProjectService.cs               # 共享能力层实现（Step 3/6/7 执行体已下沉，改为「下发 + 回读」）
│  ├─ NewProjectStateStore.cs            # 档案持久化（残留检测 / 孤儿清单）
│  ├─ HostAssistantBridge.cs             # 路线② 桥接层：经宿主服务在项目会话上跑一轮提示词
│  ├─ StepPrompts.cs                     # Step 3/4/5/6/7 提示词构造（纯函数，锁死边界与结论契约）
│  ├─ StepReportParser.cs                # 结论文件解析（源生成上下文，不抛异常）
│  ├─ StepReportEvaluator.cs             # 结论归并与阻断判定（新增 Step 3/6/7 三个判定入口）
│  ├─ StepLocalVerifier.cs               # 本地复核器（目录 / .git / refs / 占位符残余，独立再核对）
│  ├─ StepGate.cs                        # 推送 / 建流水线闸门（纯函数：判据＝读 Step 5 / Step 6 结论文件，C7′）
│  ├─ StepResultFile.cs                  # 结论文件路径约定（工作区内，不在任何 git 仓库里）
│  ├─ AssistantToolPolicy.cs             # 助手工具白名单（含云效 MCP 子策略 + 删除类工作区内放行）
│  └─ DatabaseProbeDefaults.cs           # Step 4 默认值：解析 appsettings.json 连接串装配参数
├─ Ui/WizardPanel.cs                     # 向导面板（UserControl + IPluginPanel，纯代码 UI）
├─ Ui/PipelineYamlEditorWindow.cs         # Step 7 YAML 手工微调弹窗（仅文本框，不校验，交给云效报回）
└─ skills/                               # 6 个技能正文（随插件包分发，落到工作区）
```

> **已删除**（v0.33）：`Services/PipelineTemplates.cs`（模板迁 `docs/pipeline-*.yml.md`）、`Services/PlaceholderReplacer.cs`（改由助手现场脚本）、`Services/PushRetryPolicy.cs`（退避约定迁 `docs/step6-push.md`）、`Connectors/AliyunDevOps/GitCommandResult.cs`（随 `CodeupClient` 两个 git 方法同批删除）。

### 知识文件布局与生效范围（v0.33）

插件包新增 `docs/`，Step 2 随 `skills/` 一并**铺装**到项目工作区 `.luban-agent/plugin-newproject/docs/`，承载组织专有值与三端 YAML 模板：
`org-reference.md`（值总表）/ `step3-repositories.md` / `step6-push.md` / `pipeline-*.yml.md`（三端模板）/ `pipeline-conventions.md` / `README.md`（索引）。

> **生效范围**：`docs/` 与 `skills/` 都是**只补缺失**策略铺装——**知识文件的更新只对「新建项目」即时生效**；
> 已建项目保留其工作区里的**快照**（改 `docs/` 不会回溯改写已建项目工作区）。要让已建项目吃到新知识，需**重跑 Step 2** 重新铺装。

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
- `StagePluginPackage` 目标只复制 `plugin.json` / `newproject.settings.json` / 自身 dll + deps.json / `skills/` / `docs/`，
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

规则：名称/介绍必填（名称 ≤ 40 字）；缩写匹配 `^[a-z]+$`（**只允许小写字母 a-z，不含数字**）且 ≤ 20 字符；
后端与管理端必选；本地目录必须为绝对路径且可写（探针文件写完即删，只读语义），并做**唯一性**判定：

1. 该目录已被宿主登记为工作区（同路径必然派生同一 `WorkspaceId`）→ **拦截**，但必须给出出路：
   命中信息随 `BasicInfoValidation.ExistingWorkspaceId/Name` 返回，面板据此显示「复用该工作区继续」；
   用户点它即以 `allowExistingWorkspace: true` 重跑校验 → 建档案 → 复用该工作区执行 Step 2
   （复用分支本身幂等，见下）。没有这个出口就会形成死角：错误文案让用户去点一个只在另一种状态下存在的按钮
   （P2 冒烟缺陷）；
2. `{localPath}\{Code}-Service|Admin|Mobile`（如 `D:\ws\csxmy\Csxmy-Service`，**与云端仓库名同源**）目录已存在且非空 → **只提示不拦截**（`Warnings`，警示色展示）：
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

## Step 3 / Step 6 实现要点（v0.33 助手驱动重构）

### 执行方式总则（Step 3 / 6 / 7 共性）

Step 3 / 6 / 7 的云效与 git 动作**全部下沉给项目会话的助手**，插件只做「提示词下发 + 结论文件回读 + 闸门判定 + 落账」：

- **云效动作**走 `mcp_yunxiao_*`（`codeup_*` / `flow_*`）：`AssistantToolPolicy` 新增**云效 MCP 子策略**，按**工具名段**放行 12 个
  （8 只读：`codeup_search_repos` / `codeup_list_template_repos` / `codeup_get_repo` / `codeup_list_groups` /
  `codeup_list_branches` / `flow_list_pipelines` / `flow_list_host_groups` / `flow_get_host_group`；4 写入：
  `codeup_create_group` / `codeup_create_repo` / `flow_create_pipeline` / `flow_update_pipeline`），未列出的继续拒绝
  （`flow_run_pipeline` 本项目拒绝；`codeup_clone` / `codeup_push` 已删除、策略**双保险仍 Deny**）。
  匹配先剥 `mcp_` 前缀再按工具名段判定——连接器实例改名不会让白名单失效。
- **git 动作**走**框架 git 工具组**（`GitCloneAsync` / `GitStatusAsync` / `GitCheckoutAsync` / `GitAddAsync` /
  `GitCommitAsync` / `GitPushAsync` …）：工作区内**免确认、不经插件策略**（越界才回调并被拒），故**不必**在白名单里列 git 工具名。
- **组织专有值不在正文复述**：技能只写「读工作区 `.luban-agent/plugin-newproject/docs/` 的哪一节」（唯一副本原则）。

> **机制拦不住的破坏性动作（如实记录）**：工作区内的 `GitResetAsync(mode="hard")` / `GitCleanAsync(force)` 与 shell 里
> `git push --force` / `reset --hard` / `clean -fd` 都**免回调**，只能用**技能约定**约束（不得使用），并把工具调用原文
> 呈现到操作记录供复核。`GitPushAsync` 本身无 force 参数。

### 前置条件（不满足即阻断，不发请求）

| 前置 | 缺失时的行为 |
|------|--------------|
| 云效连接器（`yunxiao` MCP） | 时间线红字「云效连接器不可用」，阻断（向导路径由 MCP 子策略放行 `codeup_*` / `flow_*`） |
| 云效组织 ID | 引导去 codex「设置 → 云效连接」填写（读取顺序：`YunxiaoApiOptions.OrganizationId` → 凭据存储 `connector:yunxiao:organizationid`，历史键 `yunxiao_organization_id` 由别名表回退）；Step 3 / Step 7 的准备与执行提示词会**直接注入**该值，结论 `extra.organizationId` 必须同值 |
| 云效 PAT | 同上（读取 `IYunxiaoCredentialProvider.GetPatAsync`）；PAT 由连接器注入 MCP，**不进工具入参、不进提示词、不落结论文件** |
| **git 凭据** | 由用户在**外部环境**自行配好（credential helper / 系统凭据管理器）；插件与技能**不提供凭据通路**，也不在 URL 里拼 `oauth2:<PAT>@` |
| 模板仓（模板库中按名 / ID 命中） | 阻断（由助手按 `docs/step3-repositories.md` 的名称约定在客户端匹配） |
| 模板 pin 分支 | 阻断；按 `docs/org-reference.md` 的 pin 择优顺序**实时解析**，解析不出即报错（不猜默认分支） |

组织 ID / 提交身份 / 占位符规则与其它组织专有值全部走 `docs/`；pin 由助手解析后写进结论文件、再落档案的
`repositories[].templateBranch`（**不落固定值**，PRD §6 #5 修订）。

### 结论文件契约（Step 3 / 6 / 7）

路径沿用 `{LocalPath}\.luban-agent\plugin-newproject\results\step{N}-<ts>.json`。`StepReport` 扩展 schema：

```jsonc
{
  "step": 3, "ok": true,
  "status": "done",            // done | needsInput | failed（Step 3/6/7 均阻断性：必须 done ∧ ok）
  "checks":     [ { "name": "Service", "ok": true, "detail": "…", "command": null } ],
  "inputs":     [ /* 仅 needsInput 时非空：三选一 / 部署目标 */ ],
  "artifacts":  [ /* 面板视图模型 + 落账的唯一来源 */ ],
  "message":    ""
}
```

- `artifacts[]` 判别属性是 `kind`（`group` / `repository` / `pipeline`），`extra` 是**具名强类型**
  （`GroupArtifactExtra` / `RepositoryArtifactExtra` / `PipelineArtifactExtra`）——**不得**用自由字典。
- **逐端完备性**：`kind=repository`（Step 3）/ `kind=pipeline`（Step 7）**每端一条**，缺端即整步 `failed`；
  任一端失败 → 整步 `failed`，但已成功项**照常落账**，便于重跑只补缺失。
- 新 schema 类型必须登记进 `Serialization/PluginJsonContext`（插件 ALC 硬约束，不得反射序列化）。
- 判定新增三个纯函数入口 `EvaluateRepositorySetup`(3) / `EvaluatePush`(6) / `EvaluatePipeline`(7)；
  `EvaluateProbe`(4) / `EvaluateBuild`(5) 不动（Step 4/5 向后兼容）。
- **独立证据（如实收窄）**：Step 3/6 另有本地复核器 `StepLocalVerifier` 独立再核对
  （Step 3：目录 / `.git` / `refs/heads/<pin>` / 占位符残余 = 0；Step 6：`refs/remotes/origin/<pushBranch>` 与 commit id）；
  Step 7 流水线在远端**无本地可核对证据**，如实记为残余风险。云效侧建组 / 建库 / 建流水线的远端证据同样不可得，
  缓解手段＝助手把每个 MCP 返回摘要写进 `checks[].detail` + 操作记录原样呈现。

### 一次只读准备下发 + 同会话执行续跑（Step 3 / 7）

宿主 API 是「一轮一问一答」：`needsInput` 得到答复后，插件**再下发一次提示词**（执行续跑）。术语上**不叫「两轮」**；
执行续跑提示词**显式携带**：①用户答复（`answers`，按 `inputs[].id` 对齐）；②准备阶段结论文件**绝对路径**；
③「上一阶段已完成什么」的一句话摘要——助手据此**只做增量，不重跑准备阶段**。回读只接受 `mtime >= 本次起始时刻` 的结论文件。

### Step 3 准备（一次只读下发，不创建任何云效资源）

提示词＝`skills/newproject-clone-template.md` 的「准备」节 + `docs/step3-repositories.md` 路径。助手动作（全只读）：

1. `codeup_list_template_repos`（`templateType=1`、`perPage=100`）全量拉取模板库条目，**在客户端按名称约定**匹配出 `templateProjectId`；
2. `codeup_list_branches` 取模板分支，按 `docs/org-reference.md` 的 pin 择优顺序解析 pin；
3. `codeup_list_groups` 查目标代码组是否已存在（决定新建 / 复用）；
4. `codeup_search_repos` 查目标仓库是否已存在，**已存在再 `codeup_get_repo` 取 `httpUrlToRepo`**；
5. 检查该端**仓库目录**（`<工作区>\<仓库名>`，**已含仓库名**）是否已存在且非空。

产出 `results/step3-*.json` 的 `artifacts[]`（`kind=group` 一条 + `kind=repository` **每端一条**），面板据此渲染**确认界面**
（视图模型 `RepositoryPrecheck` / `EndPrecheck` 由 `artifacts[]` 映射，不再是 C# 预检读数）；用户可改仓库名 / 可见性
（走 `ValidateRepositoryPlanAsync` + `ApplyRepositoryPlan`）。**准备阶段不得出现任何「新建」类产物**。

> 面板侧：**「创建并克隆」在准备结论就绪前保持禁用**；任何输入变更（仓库名 / 可见性）立即**作废**该结论
> （比对「输入签名」），需重新准备；`mtime` 早于本次起始时刻的结论文件不参与判定。

### Step 3 执行续跑（建组 / 建库 / 克隆 / 占位符）

提示词带上用户确认后的建库计划 + 准备阶段结论文件**绝对路径** + 用户答复（`answers`）。助手动作：

1. 代码组不存在 → `codeup_create_group`（**先查后建由助手守**）；
2. 仓库不存在 → `codeup_create_repo`（`templateProjectId` / `templateType=1` / `syncAllBranches=true` / `namespaceId` / `path`）；
3. **新建仓库后必须再调一次 `codeup_get_repo`（`repositoryId` = 建库响应里的 id）取 `httpUrlToRepo`**——建库响应不保证带该字段；
4. 轮询 `codeup_list_branches` 等 pin 分支就绪（上限 120s / 间隔 3s，**超时即失败**）；
5. `GitCloneAsync(url=<httpUrlToRepo>, branch=<pin>, directory=<仓库目录>)`（框架 git 工具组；**仓库目录已含仓库名，勿再拼**；地址**不带凭据**）；
6. 克隆失败 → 记 git 原文摘要（**不内嵌凭据**）、**不自行删目录**、写 `status="failed"`；
7. 目标目录非空 → 写 `status="needsInput"`，`inputs[]` 带**克隆冲突三选一**；
8. **占位符替换**：助手用 `RunPythonAsync` 现场脚本完成（内容 + 文件 / 目录名，跳过 `.git` / `bin` / `obj` / `node_modules`，
   按 `From` 长度降序、区分大小写、**幂等**）；规则读 `docs/org-reference.md` 与 `docs/step3-repositories.md`；
   `checks[]` 必须含 `placeholder-residual` 且 `ok=true`；实际命中的令牌逐字写进 `extra.placeholderSources`（**至少一项**）；
9. `GitStatusAsync` / `GitBranchListAsync`（**都要带 `workingDirectory=<仓库目录>`**）复核工作树与 `refs/heads/<pin>` 后，把 `cloned` / `defaultBranch` / `commitId` 写进
   `artifacts[kind=repository].extra`。

- **克隆冲突三选一**：`reuse`（**先做健康校验**：工作树 + `refs/heads/<pin>`，不通过拒绝复用）/ `reclone`
  （**由助手删除目录**：`RunPythonAsync` 现场脚本 `shutil.rmtree(path, onerror=清只读后重试)`——克隆出的仓库 pack 文件是**只读**的，
  框架 `DeleteDirectoryAsync` 是裸 `Directory.Delete`、**不处理 Windows 只读位**，直接删大概率失败）/ `abort`（远端仓库保留，登记孤儿产物）。
- **逐端完备性与混合结果**：`checks[]` 逐端一条（`name` 取端标识）；任一端失败 → 整步 `failed`，但已成功项**照常落账**，重跑只补缺失。
- **插件回读**：`artifacts[]` → 档案 `Repositories[]` / `RemoteArtifacts[]`；再执行**本地复核** `StepLocalVerifier`
  （目录存在且非空 / `.git` 有效 / `refs/heads/<pin>` 存在 / 占位符残余 = 0，逐项独立再扫）；通过后触发**重跑联动失效**。
- **Step 3 重跑联动失效（保留）**：执行续跑成功后作废**最近一份 Step 5 结论文件 + 清 `BuildResults`**、
  **最近一份 Step 7 结论文件 + 清 `PipelineResults` 与流水线类远端产物记录**，并清 Step 3/5/6/7 的操作记录条目；
  **只清结论，不删云效上的仓库 / 流水线本体**（远端产物一律不自动删除）。

### Step 6（一次下发即可完成）

提示词＝`skills/newproject-push-repo.md` + `docs/step6-push.md`。逐端：

1. `GitStatusAsync(workingDirectory=<仓库目录>)` 看工作树；**无改动跳过提交**（如实报告，不产生空提交），仍继续推送；
2. 建 / 切目标分支：分支名按 `docs/step6-push.md` 解析（配置 `pushBranch` 有值用配置值，留空退回仓库默认分支，
   仓库为空以 docs 登记的默认分支创建）→ 已存在 `GitCheckoutAsync(target=<branch>, workingDirectory=<仓库目录>)`、
   否则 `GitCheckoutAsync(target=<branch>, createBranch=true, workingDirectory=<仓库目录>)`；
3. 提交身份：读本机 `git config user.name` / `user.email`，缺失按 `docs/org-reference.md` 兜底值在**本仓库级**设置；
4. `GitAddAsync(paths=["."], workingDirectory=<仓库目录>)`（**不能传 `-A`**）→ `GitCommitAsync(message=<消息>, workingDirectory=<仓库目录>)`（提交信息格式按 `docs/step6-push.md`）；
5. `GitPushAsync(remote="origin", branch=<branch>, setUpstream=true, workingDirectory=<仓库目录>)`；**凭据由用户外部配置**，URL 不带凭据；**不强制推送**；
6. 回写 `results/step6-*.json`：`artifacts[kind=repository]` 每端一条（`extra` 记推送目标分支与提交 id），`checks[]` 逐端一条。

- **限流退避（约定，读 `docs/step6-push.md`）**：429 / `请求过于频繁，请稍后重试!` 才退避 **线性 10s / 20s / 30s、
  最多 3 次**（原 `PushRetryPolicy` 的口径已迁 docs，代码删除；**不是指数间隔**）；非快进 / 鉴权 / 权限**一律不重试**。
  重试过程记入 `checks[]`，三次用尽仍失败才中断，已推送的端保留结果。**限流不是权限问题**。
- **认证失败 / 凭据缺失**：`failed` + 固定提示「请先在外部环境配好 codeup HTTPS 凭据（git credential helper / 系统凭据管理器）」。
- **本地复核**：`StepLocalVerifier` 核对 `<localPath>/.git/refs/remotes/origin/<pushBranch>` 存在且 commit id 与自报一致。

> 自 v0.33 起，Step 6 的本地提交 / 推送**不再由插件拉起 git**（原 `RunGitAsync` + `CodeupClient.PushAsync` 已删除），
> 改走框架 git 工具组；`plugin.json` 里的 `git:clone_push` / `git:commit` / `git:inspect` 能力组作为框架侧进程 / 权限口径保留。

### 面板结构（P3 起）

左列从「单表单」改为「常驻状态卡 + 按 `CurrentStep` 切换的操作区」：

- 常驻：校验错误 / 非阻断提示 / 忙碌文案 / 残留横幅 + 「档案与维护」（重跑 Step 2 / 重新推送（重跑 Step 6）/ 中止并清理）；
- **导航行（v0.23）**：步骤条下方一行 = 「← 上一步」按钮 + 提示文字（回看时显示「（回看；档案已到达 N …）」）；
- Step 1 表单（`BasicInfo` / `Scaffold`）；
- Step 3 确认区（`Repositories`）：逐端仓库名 / 可见性单选 / 归属组织只读 / 落地位置 / **准备卡片（「准备（只读，不创建）」按钮 + 结论读数，数据来源＝结论文件 `artifacts[]`）** / 「我已确认」勾选 + 建库按钮 + 内联冲突三选一；
- Step 4 探活表单（`Database`）：服务器 / 端口 / 库名 / 账号 / 口令（默认值取自克隆下来的 `WebHost/appsettings.json`，口令用 `PasswordChar` 遮蔽且不回填）+ 「开始探活（只读，不建库）」+ 结论区（读档案，跨重启可见）；
- Step 5 逐端卡（`Build`）：所选端逐条显示「通过 / 失败 / 未勾选」，附**实际执行的命令**与失败摘要 + 「开始编译（逐端）」；
- Step 6 推送区（`Push`）；Step 7 复核区（`Pipeline`：部署目标下拉 + YAML 预览 / 微调 + 覆盖更新 + 创建）。

> 冲突三选一即使处于「忙碌」态也**保持可点**——它回答的正是运行中提出的问题，锁掉会把能力层挂到超时。
> 「创建并克隆」只在**准备结论就绪且未被输入变更作废**时可用；未准备就点会被拦下并提示先准备（结论在执行后被消耗，重跑需重新准备）。

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

#### 闸门判定源改为结论文件 `StepGate.CanPush` / `CanCreatePipelines`（C7′）

纯函数（`Services/StepGate.cs`），保留「编译未全部通过即拒推」的阻断，**判据由「档案终态快照 `StepStates[step]`」改为「读对应步骤的最近一份结论文件」**：

- `CanPush`（Step 5 → 6）：取 `results/step5-*.json`（`mtime` 最新且晚于该步骤最近一次起始时刻）存在 ∧ 能解析 ∧
  逐端 `BuildResults` 全 `Ok` ∧ 覆盖 `spec.Ends` 全集——缺一即拒。
- `CanCreatePipelines`（Step 6 → 7）：同口径读 `results/step6-*.json` 的逐端推送结果。
- `ProjectSpec.StepStates` **降级为「仅界面进度显示」**（仍由操作记录回放写入），**不再作闸门依据**——顺带消掉
  「落库顺序 P0（闸门读不到 `StepStates["Build"]` 恒判否）」那条脆弱性。
- 判定入口复用 `StepReportEvaluator` 的新增纯函数（`EvaluateBuild` / `EvaluatePush`），**闸门读的即是判定结论**，避免两处口径各自漂移。
- 向导按钮 / 常驻重推 / 聊天入口**同受保护**；检查**紧跟「档案为空」判定之后**（先于凭据 / 克隆 / 仓库就绪等前置检查，报错先给最该修的那条）。
- 拒绝文案：「编译（Step 5）尚未全部通过，已拒绝推送：请到步骤条的「编译」步逐端编译，全部通过后本步才可执行（编译结论见 Step 5 的逐端状态行与右侧操作记录）。」
- **拒绝不记操作记录**：一次「未执行的拒绝」不是本步的终态——若按旧写法记一条 `Failed`，回放时会把早已成功的 `Push` 状态改写成失败（步骤条显示随之中招）。提示由 `StepResult.Message` 承载，不落档案。

#### 反向失效 `StepGate.InvalidateBuild` / `InvalidatePipelineConclusion`

作废编译 / 流水线结论，与闸门成对放在同一处，免得两处口径各自漂移。调用点是 **Step 3 重跑成功之后**——本地代码可能已随模板 / pin / 占位符替换而变，不作废就会出现「改完代码不重编也能推」（闸门只看结论文件，会误放行）。C7′ 调整失效对象：

- `InvalidateBuild` → **删除（作废）最近一份 Step 5 结论文件** + 清档案 `BuildResults`（不再维护 `StepStates["Build"]` 快照键）；
- `InvalidatePipelineConclusion` → **删除最近一份 Step 7 结论文件** + 清档案 `PipelineResults` + 清流水线类远端产物记录；
- 同时清空 Step 3/5/6/7 的操作记录条目；**只清结论，不删云效上的流水线本体**（远端产物一律不自动删除）。

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
| schema | `{ "step": 4 或 5, "ok": bool, "checks": [ { "name", "ok", "detail", "command"? } ], "message" }`（Step 3/6/7 扩展为 `status` / `inputs[]` / `artifacts[]`，见「结论文件契约」） |
| 通过判定 | **四环齐**：①文件存在 ②schema 可解析（`StepReportParser`，走源生成上下文） ③`ok` 为真 ④本轮事件流无错误事件（**守卫拒绝 / 用户取消不算错误**，见下「助手工具白名单」与 v0.27）；Step 3/6/7 另加第 ③′ 环 `status == "done"` 与 §6.1.4 的**本地复核**（`StepLocalVerifier`） |
| Step 3 之后注意 | 该目录与各端 git 仓库**同级、不在任何仓库内** → Step 6 推送不会把它带进业务仓库 |
| 时效（Step 3/6/7） | 插件**每次下发前**记起始时刻，回读**只接受 `mtime >= 起始时刻`** 的结论文件；历史文件保留在 `results/` 但 UI 标注「历史轮次」、不参与判定 |

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
- **拒绝**：`RunLuaAsync`、`DeleteFileAsync` / `DeletePageAsync` / 技能与 MCP 写删类、越出工作区的写入、未知工具、工作区根未知时的任何写入。
- **v0.33 · `DeleteDirectoryAsync` 工作区内放行（C5′）**：从「无条件拒绝」改为「**工作区内按路径放行**」（与 `WorkspaceWriteTools` 同判法，
  单列 `WorkspaceDeleteTools`）；越界 / 无 `path` 仍拒。⚠️ 框架 `DeleteDirectoryAsync` 是裸 `Directory.Delete`、**不处理 Windows 只读位**，
  故「清空后重克隆」实际由助手 `RunPythonAsync` 的 `shutil.rmtree(onerror=清只读)` 执行，放行只是**契约兜底 + 面板文案一致**。
- **v0.33 · 云效 MCP 子策略（§8.1）**：`mcp_yunxiao_*` 在 `Default` 模式下 `path = null` → 必然回调插件策略；按 §8.1 表**放行 12 个**
  （8 只读 + 4 写入），`flow_run_pipeline` 拒绝、已删的 `codeup_clone` / `codeup_push` 双保险仍 Deny，未列出的 MCP 工具继续拒绝。
  **匹配先剥 `mcp_` 前缀再按工具名段判定**——连接器实例改名（`mcp_yunxiao2_codeup_get_repo`）不回归。
- **Shell 逐段判定**：按 **未加引号** 的 `&&` / `||` / `;` / `|` / 换行切段（引号内的分隔符属参数文本、不切段——`node -e "…;…"` / `git commit -m "a;b"` 必须放行；单引号内除 `''` 外不转义、双引号内反引号转义、未闭合引号按「到末尾都在引号内」处理；单个 `&` 不切段，只有 `&&` 切），每段取首词（支持 `& "prog"` 与 `"C:\Program Files\...\dotnet.exe"` 引号路径，去目录、去 `.exe/.cmd/.bat/.ps1` 后缀），
  首词不在 `AllowedExecutables`（`dotnet` / `git` / `node` / `npm` / `npx` / `pnpm` / `yarn` / `fnm` / `cnpm` / `python` / `py` / `python3` / `pip` / `pip3`）与 pwsh 无副作用内置（`cd` / `Set-Location` 等）内即**整条拒绝**；
  `$env:X = "..."` 赋值段视为无副作用放行。
- **v0.28 新增 `fnm` / `cnpm`**：本组织管理端要求 Node 16 + npm 系客户端，助手需用 `fnm exec --using=16 -- npm.cmd …`
  与 `cnpm`（内网镜像客户端）。二者都只是「版本管理器 / 包管理器」，不引入新的越界面；
  `fnm env --use-on-cd | Out-String | Invoke-Expression` 那类写法**仍被拒**（管道分段后 `Out-String` / `Invoke-Expression`
  不是白名单首词，且 `Invoke-Expression` 等同任意求值——提示词里改为 `fnm exec --using=16 -- …` 的免管道形态）。
- **拒绝 ≠ 本轮失败（v0.27）**：被拒的调用**不计进事件流错误**（`AssistantToolPolicy.IsGuardDenial` 认宿主文案「工具调用被拒绝或已取消」），只留在操作记录「被拒明细」里——否则助手**试错一次就被自己的守卫判成整步失败**（四环第④环会中招）。早先的实现正是把守卫拒绝塞进 `errors`，真机两次都因此判失败。

### 防死锁：助手不得回调插件

本轮提示词由**插件在项目会话上发起**。若助手在同一个回合里回头调用 `newproject-*` 技能或「新建项目」插件的服务，
就会变成「同会话回合串行」的**自等死锁**。故两条提示词与三个技能正文都写死这条禁令。

### 已知遗留（建议单独立项）

- 框架 `FileSystemToolPlugin.DeleteDirectoryAsync`（`luban-framework/LuBan.AIAgent/Tools/FileSystem/FileSystemToolPlugin.cs:807`）
  用的是裸 `Directory.Delete`，删 git 目录（含只读 pack）会失败且只报「权限不足」。**v0.33 起**：Step 3「清空后重克隆」
  改由助手 `RunPythonAsync` 的 `shutil.rmtree(onerror=清只读)` 执行（策略层只把 `DeleteDirectoryAsync` 放开为「工作区内放行」作契约兜底）；
  插件自己的中止清理仍走 `DirectoryCleaner.DeleteForce`（先清只读再删，RC5 已修）；框架那个要改需重新发包。
- **（v0.23 登记，待查）** Step 4 真机实测走「连库未带口令」分支（`1045 Access denied … (using password: NO)`）。
  口令按方案 A 内联进提示词，链路需复核「界面口令是否真的传进本轮提示词」，以及 `1045` 分支的文案是否明确引导用户回填口令后重跑
  （PRD §4.8 第 19 条已记残余风险，此处是交互提示的待确认项）。
- **（v0.33 说明）旧档案形态不再卡死**：修复前产生的档案可能出现「`CurrentStep` 已到最后一步、但 `StepStates` 里缺 `Build`」的
  自相矛盾状态。**不需要手工改档案**——新版面板可从步骤条点回 Step 5 补跑；闸门**改读 Step 5 结论文件**，
  不会因为指针靠前就放行推送。

## Step 7 实现要点（v0.33 助手驱动）

### 为什么不再「克隆参考流水线」（真机取证结论，保留）

三条证据把老路堵死：①官方 OpenAPI **没有**「复制流水线」接口（`flow_clone_pipeline` 语义在服务端不成立）；
②`CreatePipeline` / `UpdatePipeline` **只收**顶层 `stages:` 的**新版 YAML**，喂控制台 tb 形态的 `flow` 原文必报
`400 1209300 yaml校验失败…至少要包含一个stage`；③全组织 **70 条流水线全部是 `schema: tb`**，找不到可提交的范本。

故改为「**按 `docs/pipeline-*.yml.md` 渲染新版 YAML → 提交创建**」。参考流水线配置**已删除**（Step 7 不再读它的 YAML）。

### 三端 YAML 模板迁 docs（v0.33）

- **模板不再内嵌代码**：三端模板进 `docs/pipeline-*.yml.md`（随包分发、Step 2 铺装到工作区）——后端含 `variables`
  （`DOCKER_IMAGE_NAME` / `APP_PORT` / `DOCKER_CONTAINER_NAME` / `HARBOR_*` 等）与顶层 `cache`；前端为 `NodeBuild` →
  `OSSDelete` → `OSSUpload`（均带 `region`）。**渲染改由助手完成**（原 `PipelineRenderer` / `PipelineTemplateLibrary` 已删除）。
- **占位符替换语义与值总表**读 `docs/org-reference.md`；「哪些端有独立部署阶段」「留空即需人工绑定」等编排约定读 `docs/pipeline-conventions.md`。
- **`yamlIncludesDeployStage` / `pendingBindings` / `recommendedDeployTarget*`** 原由插件从 YAML 常量 / 配置算出，
  模板迁 docs 后**改由助手在结论文件里给出**（依据 `docs/pipeline-conventions.md`）；插件**不再持有**「哪端需要部署阶段」的常量。
- 渲染结果统一 `Normalize` 成 **LF**（预览 / 编辑器 / 提交 / 档案留痕四处逐字一致，避免「预览好好的、提交却报错」）。

### Step 7 准备（一次只读下发）

提示词＝`skills/newproject-create-pipeline.md` 的「准备」节 + `docs/pipeline-*.yml.md` + `docs/pipeline-conventions.md`。助手动作：

1. `flow_list_host_groups` 拉主机组候选（**数字 id + uuid + 名称 + 机器数**）；需要时 `flow_get_host_group`
   （**路径键必须是数字 id**）摊出机器明细；
2. 按 docs 模板逐端渲染 YAML（填项目令牌 / 端口 / 镜像 / OSS / 服务连接等；`{branch}` 取 `pushBranch`）；
3. 产出 `results/step7-*.json`：`artifacts[kind=pipeline]` **每端一条**（`yaml` / `pendingBindings` / `yamlIncludesDeployStage` /
   `recommendedDeployTargetHostGroupId` / `recommendedDeployTargetUuid`）+ `inputs[]` 带**部署目标选择**（有独立部署阶段的端必选）+ 待人工绑定清单。

面板：展示 YAML（**可手工微调**）、部署目标下拉（只读回显机器明细）、待绑定清单。
⚠️ **C10′：面板不再做 YAML 即时校验**——`PipelineYamlEditorWindow` 只是文本框，`name:` 一致性 / Tab / 顶层 `stages:` 等
一律**交给云效报回**（失败即 `status="failed"`，文案取云效原文）。**准备阶段不得创建任何流水线**。

### Step 7 执行续跑（创建或覆盖更新）

- **创建**：`flow_create_pipeline`（逐端 1 条、最多 3 条）；**建完不自动运行**（`triggerEvents: []`）；需排障时按用户显式要求即席 `flow_run_pipeline`（策略默认拒绝）。
- **覆盖更新**（含创建按名命中同名自动走）：`flow_update_pipeline` 整体覆盖本次渲染的 YAML（**全端**，取代历史「命中即复用」）；
  更新会**一并覆盖流水线名称**，故必须回传现有名称；界面用**卡内勾选确认**后再提交。
- **部署段随创建写入**（仅后端）：`deploy` 段（缩进 2 空格、挂在 `stages:` 之下）随构建段一起写出，`machineGroup` 用本次选定的**主机组 uuid**；
  **缺目标即 `failed`、不发请求**；同时产出「待人工绑定清单」。
- 回写 `artifacts[kind=pipeline]`（流水线 id / webUrl / 是否含部署段 / 所选主机组 uuid + 名），`checks[]` 逐端一条；**缺端即整步 `failed`**。
- **步骤条前置**：`StepGate.CanCreatePipelines` 在界面拦未完成的 Step 7（判据＝读 Step 6 结论文件，与界面顺序无关）。

### 部署目标：创建前选择，创建即写（v0.32 口径保留）

- **只在创建之前问一次**：Step 7「部署目标」区**只读**拉取云效主机组列表（`flow_list_host_groups`），下拉列出候选
  （`DisplayLabel = 名称（N 台 · uuid …）`），选中即按需拉 `flow_get_host_group` 摊出**机器明细**（`instanceName` / `ip` / 状态）。
- **仅后端可交互**：前端两端没有独立部署阶段，面板固定文案「无独立部署阶段（上传 OSS 即发布）」；
  「哪端需要目标」由结论文件 `artifacts[kind=pipeline].extra.yamlIncludesDeployStage` 给出，**面板不复制判断**。
- **不选就阻断，且不降级**：列表读不到（未配 PAT / 无权限 / 网络失败）或无可用主机组 ⇒ 面板红字 + **拒绝创建**；
  需要部署段却「会话未选、docs 也没默认」⇒ 同样拒绝。**不做「手填 uuid」降级**——uuid 填错云效只回「该机器组 uuid[x] 不存在」，白跑一次且污染远端。
- **写进 YAML 的是 uuid，不是数字 id**：数字 id 的唯一用途是调 `GET …/hostGroups/{数字id}` 拿机器明细（路径键必须是数字，传 uuid 会 400 `NumberFormatException`）。
- **换目标 ⇒ 丢弃该端手工微调**：微调原文里写着旧 `machineGroup` uuid，留着会出现「下拉显示 A、提交却打 B」。
- **聊天路径（无面板，C15′）**：助手把主机组候选（名称 / 机器数 / 数字 id / uuid）**直接列在对话回复里**，用户回一句即可，
  助手在**同一会话继续**创建；`inputs[]` 仍照写（面板路径复用同一份契约），**不依赖 `defaultValue` 兜底、也不降级为手填 uuid**。
  用户不选不回复 → 停在 `needsInput`（可重试），**不发创建请求**。

### YAML 手工微调（v0.31；v0.33 起面板不校验）

- **入口**：Step 7 复核区下方「编辑 YAML…」→ 独立窗口（`Ui/PipelineYamlEditorWindow`）编辑当前选中端的提交原文；
  「重置为模板」丢弃该端微调；只读预览框回显微调后的文本并标「已手工微调」。微调按端存在面板内存字典，**会话内**保留，重开面板不回填。
- **纯覆盖**：有微调就用微调、否则用渲染结果（两分支都 `Normalize`）；v0.33 起**预览原文就含活动部署段**，用户改完原样提交，不再追加 / 拼接片段。
- **面板不校验**（C10′）：面板删掉 `Validate` 调用（`Ui/PipelineYamlEditorWindow`），**交给云效报回**（`1209300`）；
  「非空 / 无 Tab / 顶层 `stages:` / `name:` 一致」由云效侧约束兜底，失败即 `status="failed"` + 云效原文。
- **换部署目标丢弃该端微调**（旧 `machineGroup` 已过期），界面提示。

### 主机组与部署目标取值（值读 `docs/org-reference.md`）

- 主机组接口真机契约：`GET /oapi/v1/flow/organizations/{orgId}/hostGroups` → **200**（裸数组，字段 `name` / `id`（数字）/ `uuid` /
  `hostNum` / `aliyunRegion` / `serviceConnectionId`）；详情 `GET …/hostGroups/{数字id}` → 200 带 `hostInfos`。`/machineGroups` 等路径是 **404**。
- **落到连接器**：`FlowClient.ListHostGroupsAsync`（裸数组走 `DeserializeList`）+ `GetHostGroupAsync(long hostGroupId)`
  （`hostGroupId<=0` 抛 `ArgumentOutOfRangeException`）；MCP 暴露 `flow_list_host_groups` / `flow_get_host_group`。**路径键必须是数字 id**。
- **默认选中值、具体 uuid / 主机组 id、与参考流水线的对应关系**：**一律读 `docs/org-reference.md`**，本正文不复述。
- **空组要容忍**：`hostNum = 0` 的主机组可选但标「当前 0 台机器：部署会找不到落点」告警；部分字段（`aliyunRegion` / `serviceConnectionId`）为 `null`，UI 需容忍空。

### 真机校正清单（8 轮写探针 + 官方组件文档，2026-10-22）

| 项 | 真机口径 |
|----|----------|
| 变量 | 顶层 `variables:` 只写 `key` / `value`（可选 `secret: true`）；写 `type:` 报 `Not supported variable type` |
| 缓存 | 顶层 `cache: { cacheDirs: [...], cacheMode: remote\|local }`；`private*` ⇒ `local`，否则 `remote` |
| `runsOn` | 标量 `public/cn-beijing` / 容器 `{group, container}` / VM `{group, labels, vm}` 均 200；**私有集群任何写法全 400**——故 `runsOn` 默认不写，私有集群值只能人工回填（整串照写） |
| `Command` / `NodeBuild` | `with.run` 必填 |
| `NodeBuild` | `versionType`（`predefined` / `custom` / `nvmrc`）+ `customNodeVersion` |
| `OSSUpload` | 必填 `serviceConnection` / `region` / `bucket` / `sourceFilePath` / `targetFilePath`（**不是** `localPath` / `prefix`） |
| `OSSDelete` | 必填 `serviceConnection` / `region` / `bucket`，目录键 `prefix` |
| `VMDockerDeploy` | 必填 `machineGroup`（**uuid**）+ `run`；`executeUser` 默认 `root` |
| `with` 未知键 | **静默忽略**（历史写错的键不会报错，只会不生效——「明明提交成功却不生效」的坑源） |

### 响应解析：`YunxiaoResponseParser`（共享缝合点，保留）

`/flow/**` 与 **`/codeup/**` 的成功响应**都不套 `{"result":...}` 外壳：列表＝裸数组、详情＝裸对象、创建＝裸整数 id、更新/删除＝裸 `true`；
只在失败时出现 `{"errorCode":"…","errorMessage":"…"}`（`errorCode` 是**字符串**）。`YunxiaoResponseParser`（public static，
`JsonOpts` / `DeserializeObject<T>` / `DeserializeList<T>` / `ParseId` / `ParseBusinessError` / `ExtractErrorMessage`）把这条契约收在一处，
`FlowClient` 与 `CodeupClient` 共用，并由 `YunxiaoResponseParserUnitTest` 直接锁住（客户端方法依赖 HTTP 无法单测，这是唯一可测的缝合点）。

> 口径提醒：`JsonSerializerDefaults.Web` 会设 `NumberHandling.AllowReadingFromString`，但那**只**允许
> 「字符串 → 数字」，**不**允许「数字 → 字符串」——真机数字 id 必须用整型（`long?`）承载。

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
├─ docs/                       # v0.33：组织专有值 + 三端 YAML 模板 + 编排约定（铺装到工作区）
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
`NewProjectDatabaseProbeUnitTest`（参数校验 / 口令脱敏 / 连接串解析不回填口令 / appsettings 容错 / 结论文件路径）、
`NewProjectStepGateUnitTest`（**v0.23**：`AdvanceTo` 单调只前进 + `StepGate.CanPush` 判据；
**v0.33（C7′）：判据改为「读最近一份 `results/step5-*.json`」**——逐端全 `Ok` 且覆盖 `spec.Ends` 全集才放行，
含「冒烟档案形态」与「结论文件缺失 / 过期 / 缺端」的回归锁定）、
`NewProjectStepReportUnitTest`（结论解析容错〔缺字段 / 未知字段 / 坏 JSON / 空内容〕+ Step 4 不阻断 +
Step 5 逐端归并与漏端即失败 + 事件流有错即失败）、
`NewProjectStepReportDecisionUnitTest`（**v0.33**：扩展 schema 解析——`status` / `inputs[]` / `artifacts[]` + 三个具名 `extra` +
三个新判定入口 `EvaluateRepositorySetup`(3) / `EvaluatePush`(6) / `EvaluatePipeline`(7) 覆盖 `done` / `needsInput` / `failed`；
逐端完备性：缺端判失败、`ok=false` 优先于逐端详情、`ResolveStatus` 无 `status` 时按 `ok` 兜底）、
`NewProjectArtifactMappingUnitTest`（**v0.33**：`artifacts[]` → 面板视图模型逐字段映射——`kind=group` / `kind=repository` → `RepositoryPrecheck`、
`kind=pipeline` → `PipelinePreview`；缺产物 / 缺字段 / `extra` 类型不符一律明确报错）、
`NewProjectStepLocalVerifierUnitTest`（**v0.33**：本地复核——目录 / `.git` / `refs/heads` / `refs/remotes` 缺失判失败、占位符残余判失败，用临时目录造桩不连云效）、
`NewProjectKnowledgeAssetsUnitTest`（**v0.33**：三端 `docs/pipeline-*.yml.md` 齐备且含 `stages:`、`docs/README.md` 索引覆盖全部文件、
**`skills/*.md` 与 `plugin.json` 的组织专有值禁用扫描**；从测试输出目录向上定位含 `LubanAgent.slnx` 的仓库根）、
`NewProjectAssistantToolPolicyUnitTest`（工具白名单 + **云效 MCP 子策略**（按工具名段放行 / `flow_run_pipeline` 拒绝 / 连接器实例改名不回归）+
Shell 逐段判定 + 越界拒绝 + **引号内分隔符不切段** + **守卫拒绝文案判定** + **`DeleteDirectoryAsync` 工作区内放行 / 越界拒绝**）、
`StepStateResolverUnitTest`、`DirectoryCleanerUnitTest`、
`YunxiaoHostGroupModelUnitTest`（**v0.32**：主机组列表 / 详情字段拼写，样本=真机原文）、
`YunxiaoToolDefinitionsUnitTest`（**v0.33**：云效工具总数 **12 → 13**，删 `codeup_clone` / `codeup_push` 两行、
补 `codeup_list_template_repos` / `codeup_get_repo` / `codeup_list_groups` 三条 schema 断言，另含 `flow_list_host_groups` / `flow_get_host_group`）、
`YunxiaoResponseParserUnitTest`（**v0.30**：裸数组 / 裸对象 / 裸标量 id / 业务错误识别 / 错误信息抽取）、
`YunxiaoPipelineModelUnitTest`（**v0.30**：列表与详情两种字段拼写 + 数字 id 承载）。
> **v0.33 资产处置**：`NewProjectPipelineTemplateUnitTest.cs`（引用已删的 `PipelineTemplateLibrary` / `PipelineRenderer` / `PipelineYamlOverride`）与
> `NewProjectPushRetryUnitTest.cs`（为已删的 `PushRetryPolicy` 而写）**整类删除**；前者由「`docs/` 模板文件存在性与结构」测试（上表 `NewProjectKnowledgeAssetsUnitTest`）替代，
> 后者退避口径改由 `docs/step6-push.md` 承载（Prompt 单测锁提示词引用，不再断言已删类型）。
**其余验证仍为手工冒烟**（本插件的 UI 与真机链路）。

1. 构建解决方案 → 确认上面的插件包目录已生成（**含 `docs/`**）；
2. 启动 codex → 「插件管理」启用「新建项目」；
3. 左侧栏出现「新建项目」入口，点击后面板挂载成功（左侧步骤条 + Step 1 表单 + 右侧操作记录区）；
4. Step 1 填表 → 「下一步」：校验失败时字段级红字；有非阻断提示（如目标仓库目录已存在且非空）时黄字提示但不拦；
   通过后 Step 2 逐条写时间线（工作区 → 授权 → 当前工作区 → 会话 → 档案快照 → 技能 → **知识文件 `docs/`** → 云效 MCP → 项目级 Agent）；
5. 复核落盘：`{LocalPath}/.luban-agent/skills/newproject-*.md`（6 个）、
   `{LocalPath}/.luban-agent/plugin-newproject/spec.json`、**`{LocalPath}/.luban-agent/plugin-newproject/docs/`（组织专有值 / 三端 YAML 模板 / 编排约定）**；工作区列表与会话列表出现新记录；
6. 「重跑 Step 2」：时间线显示「复用已有工作区」并新建执行会话（不报「工作区已存在」）；
7. **唯一性①出口**：同一目录再点「下一步」→ 红字 + 出现「复用该工作区继续」；点它 →
   复用该工作区、新建执行会话、Step 2 全绿（不会再被卡死）；
8. 带残留档案重启面板：出现「清理后重来 / 保留并继续」横幅，未处置前「下一步」不可用；
9. 「中止并清理」：时间线出现 1 条「中止并清理」记录；`{LocalPath}/.luban-agent/skills/newproject-*` 全部消失，
   工作区列表不再有该项目，**`LocalPath` 目录本身仍在**；此后同目录再「下一步」可正常走通（登记残留已清）；
10. 停用插件 → 侧栏入口与面板消失，`.luban-agent/skills/newproject-*` 无残留。

### P3 增补（Step 3 / Step 6）

> **v0.33 口径**：Step 3 / Step 6 的云效与 git 动作**全部下沉给项目会话的助手**——云效走 `mcp_yunxiao_*`（`codeup_*` / `flow_*`）、
> git 走**框架 git 工具组**（`GitCloneAsync` / `GitPushAsync` …）；插件只做「提示词下发 + 结论文件回读 + 闸门判定 + 落账 + 本地复核」。
> 组织专有值 / 模板仓名约定 / pin 择优 / 占位符规则 / 提交信息格式 / 退避口径**一律读工作区 `docs/`**（技能正文不复述）。

11. **准备门禁**：Step 3 未准备时「创建并克隆」不可用（灰）；未准备就点它 → 红字要求先准备，**不会**产生任何远端动作；
    改动仓库名 / 可见性 → 准备读数变黄「已作废」（结论文件 `mtime` 早于本次起始时刻不参与判定），须重新准备；
12. **准备读数（只读，不创建）**：点「准备（只读，不创建）」→ 助手在**项目会话**里调只读 `mcp_yunxiao_*`
    （`codeup_list_template_repos` 全量拉模板库 / `codeup_list_branches` 解析 pin / `codeup_list_groups` 查代码组 /
    `codeup_search_repos` 查同名仓库〔命中再 `codeup_get_repo` 取克隆地址〕），产出 `results/step3-*.json`；读数含：
    目标代码组（不存在将新建 / 已存在将复用 · id）、逐端模板库命中（`templateProjectId` · 全路径）、
    pin 及依据（按 `docs/org-reference.md` 的 pin 择优顺序**实时解析**）、仓库名（将新建 / 已存在将复用 · 可见性）；
    此时**云效侧未新增任何代码组 / 仓库**（只读，可对照后台）；右侧操作记录呈现助手调用的 MCP 工具与返回摘要；
13. **准备阻断**：未填「云效连接」的组织 ID / PAT，或模板库中按名约定找不到模板仓时点「准备」→
    读数与时间线给出红字（引导去设置页 / 列出模板库现有条目）；**结论文件缺失 / 不可解析 / `status != done` 一律不发执行请求**；
14. **Step 3 正常路径**：准备通过 → 勾选「我已确认」→ 「创建并克隆」→ 助手在**同一会话执行续跑**（提示词显式携带用户答复 +
    准备阶段结论文件**绝对路径**），逐条进时间线（准备项目代码组〔`codeup_create_group`，**先查后建**〕→ 创建仓库 / 同名复用
    〔`codeup_create_repo`，`templateType=1` / `syncAllBranches=true`〕→ **新建后再 `codeup_get_repo` 取 `httpUrlToRepo`** →
    等待模板分支就绪〔`codeup_list_branches` 轮询〕→ `GitCloneAsync(url=<httpUrlToRepo>, branch=<pin>, directory=<仓库目录>)` → 占位符替换〔`RunPythonAsync` 现场脚本〕）；
    「等待模板分支就绪」在 pin 分支尚未同步到新仓时轮询等待（上限 120s / 间隔 3s），就绪后立刻写「pin develop 已就绪」；
    复核本地目录里有仓库内容、`spec.json` 里 `repositories[].cloneUrl/webUrl/externalId/templateBranch` 已落、
    `remoteArtifacts` 出现 `Group`（项目级，`end` 为 null）与各端 `Repository` 条目；远端仓库由模板同步了分支；
    代码组 / 仓库名落在 `docs/step3-repositories.md` 的命名约定下（各端仓库名**首字母大写**＋端后缀），本地目录名与之同源；
    **插件回读后再跑本地复核器 `StepLocalVerifier`**（目录存在且非空 / `.git` 有效 / `refs/heads/<pin>` 存在 / 占位符残余 = 0），通过才判成功；
15. **模板 pin 实时解析**：模板仓 pin 留空 → 读数为 `develop`（Service/Admin）与 `master`（Mobile，无 develop）；
    在 `docs/` 里把某端 pin 改成不存在的分支 → 准备阻断，提示现有分支清单（不静默回退）；
    ⚠️ pin / 模板仓名约定 / 占位符规则**都读工作区 `docs/`**，改 docs **只对新建项目即时生效**（已建项目沿用工作区快照，见上文「知识文件布局与生效范围」）；
16. **Step 3 重跑幂等**：重新准备后（此时代码组已存在）→ 页面显示「已存在，将复用」；
    执行 → 时间线出现「云效已存在同名仓库，跳过创建直接复用」，**不会**因重名建库失败；
    重跑成功后**作废 Step 5 / Step 7 结论**（删除最近一份 `step5-*.json` / `step7-*.json`、清 `BuildResults` / `PipelineResults`
    与流水线类远端产物记录，并清空 Step 3/5/6/7 的操作记录条目；**只清结论，不删云效上的仓库 / 流水线本体**）；
17. **克隆冲突三选一（含复用健康校验）**：手工往目标目录塞一个多余文件后重跑 → 准备的非阻断提示列出该目录；
    执行时面板渲染 `inputs[]` 里的三选一（`clone-conflict`）；选「复用已克隆」→ 时间线先出现「校验本地目录（X端）」
    〔工作树 + `refs/heads/<pin>`〕且通过后才跳过克隆；选「清空后重克隆」→ **由助手删除目录**
    （`RunPythonAsync` 的 `shutil.rmtree(path, onerror=清只读后重试)`；框架 `DeleteDirectoryAsync` 不处理 Windows 只读位）后重克隆；
    选「中止本步」→ 本步 `failed`（文案取用户选择），不推进，远端仓库保留（孤儿），本地目录未被改动；
18. **Step 6（助手调 git 工具组，一次下发完成）**：点常驻「档案与维护」卡里的「重新推送（重跑 Step 6）」
    （**推进指针 ≥ Step 6 后可见**，Step 6 页自身的按钮等价）→ 助手在项目会话里逐端走 git 工具组
    （`GitStatusAsync` → `GitCheckoutAsync`〔建 / 切分支，`createBranch=true`〕→ `GitAddAsync(paths=["."])` → `GitCommitAsync` → `GitPushAsync`，
    每个调用都带 `workingDirectory=<仓库目录>`），
    时间线逐端「整理分支 → 提交改动 → 推送到自有仓库」；云效仓库出现目标分支（`pushBranch`，默认 `develop`）与首次提交；
    再点一次 → 无改动时显示「工作区无改动，跳过提交」，推送仍成功（幂等）；凭据由用户**外部配置**，URL 不带凭据；
    插件回读 `results/step6-*.json` 的 `artifacts[kind=repository]` 落账，并由 `StepLocalVerifier` 核对
    `<localPath>/.git/refs/remotes/origin/<pushBranch>` 与 commit id；
19. 「中止并清理」在 Step 3/6 视图下同样可见可用（常驻维护卡），且不删除本地目录、远端仓库与代码组。
20. **坏目录不得假成功（回归）**：把某端本地目录的 `.git/HEAD` 手工改成 `ref: refs/heads/.invalid`
    （复现一次失败克隆的残骸）后重跑 Step 3 → 三选一里选「复用已克隆」→ 时间线红字
    「不能『复用已克隆』：HEAD 无法解析…」且 `spec.json` 的该端 `cloned` **不会**置真；
    改成选「清空后重克隆」→ 助手清空目录后正常克隆成功（本项对应真机首轮踩到的 RC4 假成功）。
21. **只读 pack 残骸必须能清空（回归，RC5）**：造一个真机等价残骸——`<目标目录>/.git/objects/pack/`
    下放一个**只读** pack 文件（`attrib +r`），其余目录结构随意，然后重跑 Step 3 → 三选一里选「清空后重克隆」：
    目录必须被真正删空并重新克隆成功（**不得**再报 `Access to the path '…idx' is denied.`）。
    说明：git 在 Windows 上把 pack 标为只读，裸 `Directory.Delete` 必失败且会在抛错前删掉 HEAD / config / refs，
    把残骸越搞越烂——这正是 RC5 的死锁现场（真机 2026-10-20 17:27 踩到）。v0.33 起该删除由助手用
    `RunPythonAsync` 的 `shutil.rmtree(onerror=清只读后重试)` 完成；插件侧 `DirectoryCleaner`（**保留**，仅供「中止并清理」用）
    仍由 `LubanAgent.Tests/DirectoryCleanerUnitTest.cs` 锁死（裸删必抛 + `DeleteForce` 必成）。

> **真机第一次执行建仓动作前**，先对照第 12 项读数与用户逐项核对（模板库命中 / pin / 代码组名 / 仓库名 / 落地位置）。

### P4 增补（Step 4 / Step 5）

**冒烟前置（重要，否则会测到旧技能正文）**：宿主把技能写入工作区是**只补缺失**策略，已存在的
`{LocalPath}\.luban-agent\skills\newproject-*` **不会被覆盖**（`docs/` 同样只补缺失）。改了 `skills/*.md` 或 `docs/` 后
必须先删掉 `{LocalPath}\.luban-agent\skills\newproject-*` 与 `{LocalPath}\.luban-agent\plugin-newproject\docs\`，
再从 Step 2 重跑，才能拿到新正文——即 **docs 知识更新只对新建项目即时生效，已建项目保留工作区快照**。
另：P4 改动需先重新构建 `LubanAgent.slnx`（把新包铺到 codex 运行目录）。

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
    **v0.33（C7′）：判据改为读 `results/step5-*.json`**——取 `mtime` 最新且晚于该步最近一次起始时刻的那份，
    逐端 `BuildResults` 全 `Ok` 且覆盖 `spec.Ends` 全集才放行（**不再读档案 `StepStates`**）；
    被拒时**步骤条上 Step 6 的状态不受影响**（拒绝不算一次推送终态，不产生 `Push` 失败记录/快照）；
    另注意：此拒绝**先于** PAT / 克隆检查报出（顺序约束比参数齐备更根本）。
35. **常驻重推入口不得提前出现**：Step 3 刚完成（推进指针 = Step 4）时，「档案与维护」卡里**不应**出现「重新推送（重跑 Step 6）」；
    走到 Step 6 后才出现。
36. **Step 1 重提的同目录分支**：建好档案后从步骤条回到 Step 1，**本地目录保持不变**（就是档案工作区）→ 点「下一步」应出**黄字提示**
    「本地目录就是当前档案（…）的工作区：沿用请点『档案与维护』卡里的『重跑 Step 2』…」，**不弹破坏性确认横幅、不清档案**；
    按提示点「重跑 Step 2」→ 复用同一工作区重走（进度不丢）。
37. **Step 1 重提的确认门（改目录分支）**：回到 Step 1 后**把本地目录改填成另一个目录** → 点「下一步」→ 才出现卡内确认横幅（确认 / 取消）；
    点「取消」无任何副作用（档案、时间线、视图都不变）；点「确认」→ 清空档案并按新目录从 Step 2 重走。
38. **Step 3 重跑作废旧编译结论**：Step 5 全部通过后回到 Step 3 重跑（成功）→ **删除最近一份 Step 5 结论文件 +
    清档案 `BuildResults` + 清空 Step 3/5/6/7 操作记录条目**（`StepGate.InvalidateBuild`，C7′：不再维护 `StepStates["Build"]` 键）；
    再回 Step 5，结论区应显示「未开始/无结论」形态，且此时点「重新推送」应被闸门**拒绝**（本地代码可能已变，必须重新编译）；
    重新编译通过后再推即放行。（Step 3 重跑同样作废 Step 7 结论：删最近一份 `step7-*.json` + 清 `PipelineResults`，
    但**不删云效上的流水线本体**。）
39. **重推按钮有可点反馈**：「重新推送（重跑 Step 6）」鼠标悬停时应变底色（不再是「看不出能不能点」）。

#### P4 复审增补 · v0.25（落库顺序 P0 等）

40. **编译通过后立刻能推（必测）**：Step 5 全部通过后（**不重启面板、不夹做别的步**）→ 打开
    `{LocalPath}\.luban-agent\plugin-newproject\results\` 应有一份 `step5-*.json`（逐端 `BuildResults` 全 `Ok`、覆盖 `spec.Ends` 全集）；
    随手点「6 推送」/「重新推送」应**放行**。**v0.33（C7′）**：闸门判据＝读该结论文件（不再读档案 `StepStates`）；
    修复前必然被拒——`StepStates` 里没有 `Build` 键，而步骤条却显示 Step 5「已完成」（判据来自记录，闸门读快照）。
    该「落库顺序 P0」脆弱性随判定源改读结论文件而消除。
41. **各步终态在**自己那一步**就落账（回归）**：Step 4 探活结束后 `stepStates` 立刻有 `"Database"`；Step 5 结束后立刻有 `"Build"`。
    编译**失败**时同样落账（`"Build": "Failed"`）→ 重启面板后步骤条 Step 4 / Step 5 仍显示真实终态，而非「未开始」。
    （v0.33：`StepStates` 已**降级为「仅界面进度显示」**，不再作闸门依据——闸门一律读结论文件。）
42. **junction 目录不弹破坏性确认（同源判据）**：把档案工作区目录做成 junction（`mklink /J <别名> <真实目录>`）后，
    从步骤条回 Step 1、本地目录填**真实目标路径** → 点「下一步」应出**黄字**（判为同目录），**不弹**破坏性确认横幅。
43. **忙碌期间灰化不误导**：任一步执行中（`_busy`）步骤条**已到达的行不变淡**（含当前高亮步与已完成步），
    只有**未到达的行**保持 0.45 不透明度。
44. **同目录黄字无残留红字**：先制造一条红字（如本地目录填成不存在的盘符后点「下一步」），再把目录改回**档案工作区**目录并点「下一步」
    → 只应看到黄字提示（含「中止并清理」出口那条），**不残留上一轮红字**。

#### P4 复审增补 · v0.27（助手守卫两处真缺陷）

45. **引号内的分号不误伤（回归，必测）**：Step 5 里助手执行含引号内分隔符的命令（`node -e "…;…"` / `git commit -m "a;b"` / `python -c "import sys; print(sys.version)"`）
    应被**放行并正常执行**（运行记录里该次调用是「放行」而不是「拒绝」）。修复前必被拒——分段器按字面量切 `;`，后半截被当成可执行文件。
46. **守卫拒绝不判本步失败（回归）**：让助手尝试一条白名单外命令（如 `curl` / `powershell`）或一个白名单外 MCP 工具
    （如 `flow_run_pipeline`）→ 该调用被**拒绝**并出现在操作记录「被拒明细」里，
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
    时间线应显示从模板 pin 分支创建 `develop` 后再推送。**v0.33**：目标分支名按 `docs/step6-push.md` 解析
    （配置 `pushBranch` 有值用配置值、留空退回仓库默认分支），助手据 `GitCheckoutAsync` 建 / 切分支。

#### P4 复审增补 · v0.29（Step 6 限流退避重试 + 文案误归因）

51. **限流会自动重试（核心回归，必测）**：Step 5 通过后点「6 推送」/「重新推送（重跑 Step 6）」，若云效返回
    `remote: 请求过于频繁，请稍后重试!`，时间线里那条「推送到自有仓库（X端）」应**原地变成**
    「云效服务端限流（…）：10s 后重试（第 1/3 次）」并继续往下跑（**不新增第二条记录**，同一条记录只在等待与结束间改写）；
    重试成功后应记「已推送 develop → …（云效限流后，第 N 次重试成功）」且 Step 6 判成功。修复前是一次失败即整步失败。
    **v0.33**：退避由助手按 `docs/step6-push.md` 的约定执行（**线性 10s / 20s / 30s、最多 3 次**，非指数；口径与原 `PushRetryPolicy` 一致，
    但实现已删除、只留 docs 约定），重试过程记入 `checks[]`；右侧操作记录原样呈现助手的 `GitPushAsync` 调用与返回。
52. **限流失败文案不得误归因（回归，必测）**：让三次重试全部用尽（例如断网、或在限流窗口内反复触发）后，失败文案应为
    「…云效服务端限流：本轮已自动退避重试 3 次仍未成功，建议等约 1 分钟后点「重新推送（重跑 Step 6）」…」，
    **文案里不得出现「PAT」「分支保护」字样**（修复前必现「请确认 PAT 具备该仓库写权限，且目标分支未被保护」）。
53. **非限流失败不进退避等待**：制造一条非快进失败（远端 `develop` 已有本地没有的提交）→ 推送应**立即失败**、
    不出现「Ns 后重试」，且文案仍给出排查方向（「若持续失败，请检查 PAT 是否具备该仓库写权限、目标分支是否被保护」）。
54. **提交信息不再重字（回归）**：Step 6 提交成功后的记录与仓库 `git log` 应为
    `chore: 初始化 <项目名>（管理端）——由「新建项目」向导按模板 <模板仓名> 生成`（**提交信息格式读 `docs/step6-push.md`**），
    时间线上也应是「整理分支（管理端）」「提交改动（管理端）」——**不得出现「管理端端」「后端端」「移动端端」**
    （修复前真机提交即 `chore: 初始化 测试项目一（管理端端）——…`）；同一批修正也覆盖界面文案：Step 3 表单字段为
    「管理端目标仓库名」、端分组标题为「管理端」、目录冲突横幅为「管理端目标目录已存在且非空…」
    （服务层 5 处 + UI 4 处，共 9 处）。

#### P4 复审增补 · v0.30（Step 7 流水线：模板渲染 + 变量与缓存 + 部署补齐）

> 前置：Step 6 真机复跑通过（`StepGate.CanCreatePipelines` 会在界面上拦住未完成的 Step 7，判据＝读 Step 6 结论文件）。改插件后需**重建铺包 + 重启 codex**。
> **v0.33 口径**：Step 7 同样「**一次只读准备下发 + 用户确认后同会话执行续跑**」——助手调 `mcp_yunxiao_flow_*` 拉主机组、按 `docs/pipeline-*.yml.md` 渲染 YAML，
> 产出 `results/step7-*.json`；插件据 `artifacts[kind=pipeline]` 渲染复核界面，`flow_create_pipeline` / `flow_update_pipeline` 由助手执行。

55. **YAML 预览可核对（必测）**：进 Step 7 → 面板从 `results/step7-*.json` 的 `artifacts[kind=pipeline]`（每端一条）渲染三端 YAML ——
    后端含 `variables:`（`DOCKER_IMAGE_NAME` / `APP_PORT` / `DOCKER_CONTAINER_NAME` / `HARBOR_*`）与 `cache:`；前端含 `NodeBuild`
    （`versionType: custom` + `customNodeVersion`）→ `OSSDelete`（`prefix`）→ `OSSUpload`（`sourceFilePath` / `targetFilePath`）且带 `region`；
    **不得出现** `type: String`（真机会被拒）、`localPath`（键名错）、`nodeVersion:`（旧写法）、`runsOn: private/`（硬拼前缀）；
    渲染原文统一归一化 **LF**（预览 / 编辑器 / 提交 / 档案留痕四处逐字一致）。
56. **默认不写 runsOn（回归）**：`docs/` 模板里对应集群值留空 → 渲染结果里**没有** `runsOn:` 行（＝云效公共北京集群），
    且 `cacheMode: remote`；把该值改成 `private/<集群ID>` 后重启 codex → 出现 `runsOn: private/<集群ID>`（**整串照写**）且 `cacheMode: local`。
57. **创建成功 + 形态可读回（必测）**：点「创建流水线（逐端）」→ 助手逐端 `flow_create_pipeline` → 时间线逐端成功 → 打开云效控制台：流水线「变量和缓存」页出现上述变量
    （`HARBOR_PASSWORD` 标为**私密**），步骤参数与预览一致，**建完不自动运行**（`triggerEvents: []`）；核对完成后**删除**测试流水线。
58. **幂等（重跑不重复建）**：同一步再点一次 → 时间线显示「命中同名流水线，已覆盖更新」（不新增第二条），名称与 YAML 被覆盖为当前渲染结果（**全端**；注意这会覆盖控制台上的人工改动）。
59. **部署段随创建写入（必测）**：进 Step 7 → 助手 `flow_list_host_groups` 拉到主机组列表，面板「部署目标」区**默认选中** `docs/org-reference.md` 登记的推荐主机组，
    机器明细行显示（`instanceName / ip / status`，值见 docs）；后端 YAML 预览里应**直接含活动部署段**（`deploy:` → `component: VMDockerDeploy` → `machineGroup: '<uuid>'`），
    **没有任何注释块**（`#   deploy:` / `# ⚠` 一类文案已彻底删除）；详细条目见「P5 增补 · v0.32」67–71。
60. **前端两端没有部署阶段**：管理端 / 移动端的「部署目标」区**不出现下拉**，固定文案
    「该端没有独立部署阶段（构建产物上传 OSS 即发布），无需选择部署目标。」；两端 YAML 里**不得**出现 `deploy:` / `VMDockerDeploy` / `machineGroup`
    （「哪端需要目标」由 `artifacts[kind=pipeline].extra.yamlIncludesDeployStage` 给出，面板不复制判断）。
61. **闸门拦截**：Step 6 未全部成功时进 Step 7 → 创建被拒并提示先完成推送（**判据＝读 Step 6 结论文件**，与界面顺序无关）。

#### P5 增补 · v0.31（Step 7 YAML 手工微调）

> 前置：Step 6 真机复跑通过。改插件后需**重建铺包 + 重启 codex**；本组可在 v0.30 的 55–61 之后接着测。

62. **编辑入口与回显**：Step 7 预览区出现「编辑 YAML…」→ 点开弹出独立窗口（标题含端与流水线名，编辑区等宽字体、可滚动）；
    改动后保存 → 主界面只读框显示**微调后的原文**，出现「已手工微调」标记与「重置为模板」；切换端再切回**改动仍在**（会话内按端保留）。
63. **重置为模板**：点「重置为模板」→ 该端回到模板渲染原文，标记与按钮消失；再点「编辑 YAML…」看到的是模板原文。
64. **提交即微调内容（必测）**：只改一处无害内容（如把 `APP_PORT` 的 `'7000'` 改成 `'7001'`）→ 保存 → 点「创建流水线（逐端）」→
    助手把微调正文随**执行续跑提示词**提交（`flow_create_pipeline`）→ 云效控制台该流水线「变量和缓存」应为 `7001`；档案 `PipelineResults[].Yaml` 与界面原文一致。核对后**删除**测试流水线。
65. **面板不校验（C10′，v0.33 起）**：把顶层 `stages:` 删掉 / 改成 Tab 缩进 / 把 `name:` 改掉 → 面板**不再即时拦下**，照常提交；
    由云效报回失败（如 `400 1209300 yaml校验失败…至少要包含一个stage`），时间线给可读失败（`status="failed"`，文案取云效原文）；
    失败时**不产生**半成品流水线（助手按契约把云效原文写进 `message` 与对应端 `checks[].detail`）。
66. **微调与部署段叠加（纯覆盖）**：对后端做微调（如把 `APP_PORT` 的 `'7000'` 改成 `'7001'`，保留模板里已含的 `deploy` 段）
    → 点「创建流水线」→ 控制台既见 `7001` 又见 `deploy` 阶段；**不再有**「末尾追加部署段 / 去重判据」这回事
    （助手「有微调用微调、否则用准备阶段渲染结果」两分支都归一化换行）。
    注意：**换部署目标会丢弃该端微调**（旧文本里的 `machineGroup` 已过期），界面会提示。

#### P5 增补 · v0.32（部署目标：创建前选择 + 创建即写 + 覆盖更新）

> 前置：Step 6 真机复跑通过。改插件后需**重建铺包 + 重启 codex**；本组接在 55–61、62–66 之后。
> 口径：部署段**随创建写入**，没有「注释块 / 一键补齐 / 事后补」；目标在创建前由用户选（`inputs[]` 的 `deploy-target`），**拉取失败一律阻断创建**。

67. **目标列表拉取与默认选中**：进 Step 7 → 助手在**项目会话**里 `flow_list_host_groups` 拉候选（`artifacts[kind=pipeline].extra` 带
    `recommendedDeployTargetHostGroupId` / `recommendedDeployTargetUuid`），面板「部署目标」区下拉填入候选（`名称（N 台 · uuid …）`）并**默认选中**推荐值；
    状态行为「共 N 个可用主机组…（已按插件配置默认选中）」；候选里**不含**没有 `uuid` 的项；具体 uuid / id 值读 `docs/org-reference.md`，正文不复述。
68. **机器明细可核对**：选中某项 → 助手按需 `flow_get_host_group`（**路径键必须是数字 id**，传 uuid 会 400）摊出机器明细，
    面板明细行列出该组机器（`instanceName / ip / status`）；换选 → 明细随之刷新（每个组只拉一次，缓存复用）；
    `hostNum = 0` 的组给**告警**「当前 0 台机器：部署会找不到落点」而非报错（部分字段 `aliyunRegion` / `serviceConnectionId` 可为 `null`，UI 需容忍空）。
69. **拉取失败阻断创建（必测）**：临时清空云效组织 ID 或 PAT（改用户配置后重启 codex）→ 进 Step 7 应红字
    「读不到云效主机组列表，已阻断创建：…」且**点创建即被拒、不产生任何云端请求**（云效里不出现新流水线）；
    恢复凭据后点「刷新主机组」→ 恢复正常。要复现「未选且无默认值也阻断」，需同时清空 `docs/org-reference.md` 登记的推荐主机组值
    （**不做「手填 uuid」降级**——uuid 填错云效只回「该机器组 uuid[x] 不存在」，白跑一次且污染远端）。
70. **创建即写 + 档案留痕（必测）**：保证 Step 6 通过 → 点「创建流水线（逐端）」→ 助手 `flow_create_pipeline`（部署段随创建写入）→
    云效控制台该流水线含 `deploy` 阶段（`VMDockerDeploy`，`machineGroup` = **本次所选 uuid**，无注释块）；
    档案 `PipelineResults[]` 该端 `DeployStageIncluded = true` 且记下 `DeployMachineGroupUuid` / `DeployMachineGroupName`。核对后**删除**测试流水线。
71. **覆盖更新补旧流水线（必测）**：对 live 后端流水线（早期版本建的、只含构建段 + 末尾注释块）→ 勾选「覆盖更新」确认 → 执行 →
    助手 `flow_update_pipeline`（**回传现有名称**）→ 云效控制台该流水线变为「构建段 + 活动部署段」（注释块消失），**名称未被改掉**；
    **未勾选确认**时点该按钮 → 黄字提示、不发请求。

## 阶段进度

| 批次 | 内容 | 状态 |
|------|------|------|
| **P1** | 工程骨架、清单、档案模型、共享能力层契约与桩、向导面板骨架、铺包通路、源生成 JSON 通路 | **已完成**（构建 0 错误；框架侧生命周期探针全绿，含「停用后 ALC 真回收」；用户人工冒烟通过） |
| **P2** | Step 1 表单 + Step 2 初始化项目环境（工作区 / 会话 / 技能 / 云效 / 项目助手） | **已完成**（构建 0 错误；含唯一性①死路修复与「中止并清理」补齐，详见上文「Step 1 / Step 2 实现要点」） |
| P3 | Step 3 准备（只读）+ 执行续跑（建组 / 建库 / 克隆 / 占位符）+ Step 6 助手推送 | **已完成 + v0.33 助手驱动重构**（历史：修复 RC1 建库/克隆竞态、RC2 git 报错被吞、RC3 失败残留、RC4「复用已克隆」假成功、RC5 只读 pack 挡住「清空后重克隆」导致三选一死锁；新增 `LubanAgentCore.Utils.DirectoryCleaner`（保留给「中止并清理」）。**v0.33**：Step 3/6 的云效与 git 动作**全部下沉给助手**——云效走 `mcp_yunxiao_*`（新增 `codeup_list_template_repos` / `codeup_get_repo` / `codeup_list_groups`，删 `codeup_clone` / `codeup_push`），git 走**框架 git 工具组**（`GitCloneAsync` / `GitPushAsync` …）；插件只做「提示词下发 + 结论文件回读（`artifacts[]`）+ 闸门判定（C7′ 读结论文件）+ 落账 + 本地复核 `StepLocalVerifier`」；`CodeupClient.CloneToLocalAsync` / `PushAsync`、`GitCommandResult.cs`、`Services/PlaceholderReplacer.cs`、`Services/PushRetryPolicy.cs` **删除**；组织专有值 / pin 择优 / 占位符规则 / 提交信息格式 / 退避口径迁入工作区 `docs/`（唯一副本原则）；Step 3 重跑作废 Step 5 / 7 结论（只清结论、不删远端本体）；**待真机冒烟**——见「P3 增补」11–21） |
| P4 | Step 4 数据库探活（只读）+ Step 5 逐端编译（技术路线 ②：助手亲自执行 + 结论文件回读判定） | **代码完成 + 冒烟缺陷已修（v0.23）**（构建 0 错误；`LubanAgent.Tests` **135/135** 通过；新增提示词 / 结论解析与归并 / 探活参数与默认值 / 工具白名单 / **推进指针与推送闸门** 5 组单测；修复「Step 5 无入口 → 越级推送」根因——步骤条回退导航 + `StepGate.CanPush` 能力层闸门 + 重推入口可见性收紧；并完成审查收口：同目录重提不给破坏性确认、闸门拒绝不写 Push 终态、闸门前置、Step 3 重跑作废旧编译结论、未到达行显式灰化与重推 hover 反馈；v0.25 复审再收口：**落库顺序 P0**（Step 4/5 改为「先记终态再落库」，否则闸门读不到 `StepStates["Build"]` 恒判否；**v0.33 起闸门改读结论文件，该脆弱性消除**）、同目录判据补齐 junction 别名、灰化只绑已到达、黄字清残留红字并补「中止并清理」出口；**v0.27**：真机 Step 5 冒烟暴露的**助手守卫两处真缺陷**随修——①Shell 逐段判定改为识别成对引号（引号内的 `;` / `|` / `&&` / 换行不再被误切成命令段，`node -e "…;…"` 不再被误拒）；②守卫拒绝不再计入「本轮错误事件」（只留「被拒明细」），不再把助手的一次试错判成整步失败；`LubanAgent.Tests` **137/137**；真机管理端编译失败的根因是**仓库基线**（pnpm 跳过 `@vue-office/docx` 的构建脚本 → 包入口缺失）；**v0.28**：该根因闭环——Step 5 提示词新增【本组织已知构建姿势】（管理端：`develop` 分支；`fnm exec --using=16 -- npm.cmd …`；`npm install --registry=https://registry.npmmirror.com`；**`npm run build:dev`** 而非 `npm run build`；报 `Failed to resolve entry for package "X"` 时 `npm.cmd rebuild X` 补跑），白名单补 `fnm` / `cnpm`，边界由「只编译、不修复」放宽为「**只编译、不改业务**」（允许修依赖树与环境，禁改业务源码与工程配置）；本机实测管理端 `npm install` 81s → `build:dev` exit 0 / 89s，`dist` 正常产出；另按组织口径把包内 `pushBranch` 默认由 `main` 改为 **`develop`**（对齐 Step 3 克隆 pin，配置改值、无代码改动）；**v0.29**：Step 6 真机推送缺陷闭环——①云效**服务端限流**（`remote: 请求过于频繁，请稍后重试!`）原先被当成「PAT 权限 / 分支保护」误归因、且一次失败即整步失败（真机后端隔 11s 重推成功、管理端 3s 后又被限流即挂），现按限流识别分流并经退避策略 **线性 10s → 20s → 30s、最多 3 次**（只重试限流，非快进 / 鉴权 / 权限不重试；**v0.33：该口径迁入 `docs/step6-push.md`、由助手执行，原 `PushRetryPolicy` 已删除**），等待期间原地改写同一条记录，文案不再提 PAT（聊天出口同步；**v0.33：推送一律走框架 git 工具组，`codeup_push` 已删除**）；②顺带修正 `$"{EndLabel(end)}端"` 造成的「管理端端 / 后端端」重字（含 Step 6 提交信息）；`LubanAgent.Tests` **148/148**；**待真机冒烟**——见「P4 增补」22–29、「P4 增补 · 冒烟缺陷修复项」30–46 与「P4 复审增补 · v0.28」47–50、「P4 复审增补 · v0.29」51–54） |
| P5 | Step 7 准备（主机组 + 渲染 YAML）+ 执行续跑（创建 / 覆盖更新）+ 远端产物孤儿清单化展示（本地回收已在 P2 完成） | **已完成 + v0.33 助手驱动重构**（历史 v0.30–v0.32：口径由「克隆参考流水线」改为「内嵌新版 YAML 模板 + 渲染 + 创建」（真机：tb 形态 `flow` 原文不可提交、OpenAPI 无复制接口、全组织 70 条全是 tb）；MCP `flow_clone_pipeline` → `flow_create_pipeline`；部署阶段改为「**创建前选目标 + 创建即写**」，注释块与「一键补齐」全链路删除，`AppendDeployStagesAsync` → `UpdatePipelinesAsync`，档案记 `DeployMachineGroupUuid` / `DeployMachineGroupName`；连接器新增只读主机组能力（`FlowClient.ListHostGroupsAsync` / `GetHostGroupAsync` + MCP `flow_list_host_groups` / `flow_get_host_group`，工具 10 → 12）；`PipelineYamlEditorWindow` 微调按端会话内保留。**v0.33**：三端 YAML 模板与编排约定**迁入 `docs/pipeline-*.yml.md` / `docs/pipeline-conventions.md`**（`Services/PipelineTemplates.cs` 整个文件删除——`PipelineTemplateLibrary` / `PipelineRenderer` / `PipelineYamlOverride` 全部移除），渲染与「哪些端有独立部署阶段 / 待绑定清单」**改由助手在结论文件 `artifacts[kind=pipeline]` 里给出**；Step 7 同样「一次只读准备下发 + 用户确认后同会话执行续跑」，`flow_create_pipeline` / `flow_update_pipeline` 由助手执行；面板**不再做 YAML 即时校验**（C10′，交云效报回）；`CanCreatePipelines` 判据改读 Step 6 结论文件（C7′）；**待真机验收**——按 4B 裁决排在 Step 6 复跑通过之后，见「P4 复审增补 · v0.30」55–61、「P5 增补 · v0.31」62–66 与「**P5 增补 · v0.32**」67–71） |
| P6 | 6 个技能正文「助手驱动」重写 + `docs/` 知识文件 + 聊天入口与向导联调 | 进行中（6 个技能正文已按 v0.33 改写为「助手执行」口径——Step 3/6/7 一律「助手调 `mcp_yunxiao_*`（云效侧）+ 框架 git 工具组」，组织专有值只写「读工作区 `docs/` 对应节」，不再出现 `codeup_clone` / `codeup_push` 与「两轮」措辞；`plugin.json` 6 个技能 description 同步改写；README 冒烟清单 1–71 条重写为「助手执行」版；聊天入口与向导联调待办） |