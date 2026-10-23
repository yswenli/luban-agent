# 新建项目插件「助手驱动」重构设计（Step 3/6/7 下沉 · 知识与模板移出代码）

- 日期：2026-10-22（**v0.2：三路对抗性审查后修订** → **v0.3：用户逐条定案，7 条否决项的替代方案已落地**）
- 状态：**待用户批准**（批准前不动任何产品代码）
- 范围：`luban-agent`（`LubanAgent.Plugin.NewProject` 重构 + `LubanAgentCore` 云效连接器 **删 2 个 / 补 3 个 MCP 工具** + `LubanAgent.Tests` 单测）
- 前置基线：`luban-agent/docs/PRD-插件体系与新建项目插件.md` v0.32、`luban-agent/LubanAgent.Plugin.NewProject/README.md`
- 不改动：`luban-framework`（`LuBan.*`，含 `GitToolPlugin`）——本设计只用现成能力，不新增框架契约

> **审查记录**：v0.1 经三路 fresh-context 对抗性审查（事实核查 / 可实施性反例 / 回归与基线一致性），21 条不成立断言
> 与契约缺口已并入 **§18**；**§14** 的 C 清单经用户逐条复核，7 条被否决、替代方案见 **§14.2** 与 **§18.2**。

---

## 1. 背景与动机

「新建项目」插件今天有**两条路径**，且执行方式不一致：

| 路径 | 入口 | Step 3 / 6 / 7 的执行方式 |
|------|------|--------------------------|
| 向导路径 | `Ui/WizardPanel` → `INewProjectService` | **插件直连框架连接器**：`CodeupClient` / `FlowClient`（REST） |
| 聊天路径 | `newproject-*` 技能 | 技能正文让助手编排 `mcp_yunxiao_*`，但实际仍推荐"回落能力层" |

问题（用户口径）：

1. **插件里重造了云效与 git 的活儿**：建组/建库/等分支就绪/克隆/占位符替换/提交推送/流水线渲染与创建，全在 `NewProjectService.cs`（**2892 行**）里用 C# 直连实现；`PipelineTemplates.cs` 内嵌三端 YAML 常量；`newproject.settings.json` 集中若干组织专有字面值。改一个端口、换一条服务连接、调一次模板，都要改配置甚至改代码并重发插件。
2. **两条路径执行方式分叉**：PRD §4.10.2 要求"同一个实例、零逻辑分叉"，现状只是"同一个 `INewProjectService`"，而它内部对向导路径是直连、对聊天路径的 `mcp_yunxiao_*` 只是"技能正文建议"。
3. **Step 4 / 5 已经证明了"助手执行 + 结论文件回读"这条路**（`HostAssistantBridge` + `AssistantToolPolicy` + `StepReport`/`StepReportParser`/`StepResultFile`），且真机跑通。所以本次不是发明新机制，而是**把已验证的机制推广到 Step 3 / 6 / 7**。

### 1.1 用户给定的目标口径（逐条）

| # | 口径 | 本设计如何落实 |
|---|------|----------------|
| G1 | 阿里云 **codeup / flow** 相关操作 → 由 lubanagent 对话调用 **MCP** 完成 | Step 3 / 6 / 7 的云效 REST 动作全部改为助手调 `mcp_yunxiao_*`（补齐 3 个缺的工具、删掉 2 个 git 类工具，见 §7） |
| G2 | **git** 操作 → 由 lubanagent 对话调用 **git 工具组**完成（含克隆与推送） | 助手调 `GitCloneAsync` / `GitStatusAsync` / `GitAddAsync` / `GitCommitAsync` / `GitCheckoutAsync` / `GitPushAsync` 等；克隆地址取仓库详情里的 `httpUrlToRepo` |
| G3 | **git 凭据由用户在外部环境自行配置**；插件/助手只负责把失败原因提示出来 | 插件**不再**拼 `oauth2:<PAT>@` 认证 URL、不做凭据注入；失败时把 git 原文摘要 + "请先在外部环境配好凭据"带进结论文件与操作记录 |
| G4 | 确定性闸门**降级**为"技能正文里的约定 + 结论文件回读判定"；插件只保留对输入值/格式的**薄校验** | 见 §6、§9；`PushRetryPolicy` / `PlaceholderReplacer` / `PipelineTemplates.cs` 全删；`StepGate` 保留但**判定源改为结论文件**（C7′）；只留 `ValidateBasicInfoAsync` / `ValidateRepositoryPlanAsync`（纯本地） |
| G5 | 组织专有值 + 三端流水线 YAML 模板 → 随包分发的 `*.md`（插件工程下 `skills/` 与 `docs/`） | 见 §4；`PipelineTemplates.cs` 整删；`newproject.settings.json` 瘦到只留 UI 预填用的短格式值 |
| G6 | 插件仍需**可视化参数录入界面**，且项目工作区里**能查到记录** | 见 §10；面板与档案 `spec.json` 保留，结论文件落在工作区 `results/`，UI 增加"打开本项目执行会话"入口 |

---

## 2. 目标 / 非目标 / 反目标

### 2.1 目标

- **T1** Step 3 / 6 / 7 的执行与判定下沉到项目会话助手；插件侧只做：薄校验、宿主水管、提示词构造、结论文件回读、**本地可核对项复核**（见 §6.1.4）、档案落账、UI 呈现。
- **T2** 组织专有知识与三端流水线 YAML 模板全部移出 C# / JSON 配置，改为随包分发的 `docs/*.md`；技能正文只写"流程与约定"，不写死组织参数值。
- **T3** 向导路径与聊天路径**执行方式一致**：同一套技能正文、同一套结论文件契约。
- **T4** 云效 MCP 工具面调整到能独立完成 Step 3 / 6 / 7 的程度：**删 2（`codeup_clone` / `codeup_push`）补 3（模板库检索 / 仓库详情 / 代码组检索），12 → 13**。
- **T5** 凭据零内联：会话历史与 `luban-ai-agent.db` 中不出现任何 PAT / 口令；git 凭据完全外置。

### 2.2 非目标

- 不改 Step 4（数据库探活）/ Step 5（编译检查）的机制与判据——它们已经是目标形态。
- 不改 Step 2 的宿主水管（建工作区 / 会话 / 注册技能与 `yunxiao` MCP）——它是宿主 API 操作，不是云效或 git 动作。
- 不改 `luban-framework`（`LuBan.*`）；不新增框架契约（`GitToolPlugin` 已可用）。
- 不做"无 UI 的纯聊天入口"或重做面板视觉设计。
- 不做"远端产物自动删除"。

### 2.3 反目标（明确不做，防止方案漂移）

- **不保留两套执行路径**：不允许"向导直连连接器 + 聊天走 MCP"并存。要么都走助手，要么不做。
- **不把 Step 2 交给助手**：工作区/会话/技能落盘属宿主管理能力，助手没有对应工具，也没有必要。
- **不引入"插件暴露工具给助手"的新机制**：插件不通过自定义 tool 反向调用 C# 逻辑（那会形成第二条隐式通路）。**本设计无例外**——目录清理（C5′）也走助手侧的表驱动白名单工具，不经插件。
- **不把 PAT 写进任何提示词、技能正文、docs 或结论文件**。
- **不在助手链路里加沙箱/新守卫层**（沿用 `AssistantToolPolicy`，只扩展白名单，见 §8）。

---

## 3. 关键约束与事实（读代码得到，作为设计依据）

> 本节所有事实**已逐条回代码核对**（含 v0.2 审查修正与 v0.3 定案修正，见 §18）。

### 3.1 现有机制（可直接复用）

- **路线 ② 脚手架**：`Services/HostAssistantBridge.cs`（`TurnOutcome` 含 `Ran` / `SessionId` / `ToolCalls` / `Approvals` / `Denials` / `Errors` / `ResultFile` / `ResultFileContent` / `Failure`）、`Services/StepReportParser.cs`、`Services/StepReportEvaluator.cs`（四环判定）、`Services/StepResultFile.cs`（路径约定）、`Abstractions/StepReport.cs`（`step` / `ok` / `checks[]` / `message`）。Step 4 / 5 已在用。
  - ⚠️ v0.2：`StepReportEvaluator` 只有 `EvaluateProbe`（**硬绑 `report.Step != 4` 即失败**）与 `EvaluateBuild`（**硬绑 `Step != 5`**）两个入口。**结论文件扩 schema 不等于判定可扩展**——Step 3/6/7 需要**新增判定入口**（见 §6.1）。
- **结论文件路径**：`{项目工作区}\.luban-agent\plugin-newproject\results\step{N}-{yyyyMMdd-HHmmss-fff}.json`；放工作区内的三个理由（免确认、不在任何 git 仓库内、用户可自查）写在 `StepResultFile` 的备注里，继续沿用。
  - ⚠️ v0.2：文件名**每次带毫秒时间戳**，准备与执行会生成两个文件。回读口径必须写清"只认本轮起始时刻之后生成的文件"（见 §6.1.3），否则会读到上一轮的旧结论。
- **技能落盘机制**：`LubanAgentCodex/Plugins/CodexPluginHost.cs` 的 `RegisterSkill` / `ResolveSourceDirectory` / `TryWriteSkillFile` → 从插件目录读 `entry` 指向的**单个 md**，写入 `{workspace}\.luban-agent\skills\<id>\SKILL.md`；上限 `MaxSkillFileBytes = 256 * 1024`（`SkillManageToolOptions`）。
- **工具审批回调**：`AssistantToolPolicy.Decide`（`ToolPermissionMode.Default` 下只对"删除/技能/MCP 写删类、无路径语义脚本工具、越界文件写"回调）。
  - ⚠️ **框架无"助手轮内提问"工具**（`AskUser` / `RequestInput` / `AskQuestion` 在 `LuBan.AIAgent/Tools/**` 全无）——任何需要用户确认的交互**必须跨下发**（这是 C6′ 的根因）。
- **框架既有免确认清单**：`ToolConfirmationService.AutoConfirmTools`（`ToolConfirmationService.cs:413/508`，来源配置 `LuBanAgent:Confirmation:AutoConfirmTools`，`EvaluateAsync` 早退于 `:701`）。**这是一条比"写插件策略"更轻的既有路线**，本次评估后**不采用**，理由见 §8.4。
- **git 工具组**：`luban-framework/LuBan.AIAgent/Tools/Git/GitToolPlugin.cs`（`GroupName="git"`，默认启用），共 5 组：
  - 只读：`GitStatusAsync` / `GitDiffAsync` / `GitLogAsync` / `GitShowAsync` / `GitBranchListAsync` / `GitRemoteListAsync` / `GitBlameAsync` / `GitStashListAsync`；
  - 写：`GitAddAsync` / `GitCommitAsync` / `GitCheckoutAsync` / `GitSwitchAsync` / `GitBranchCreateAsync`；
  - 远端：`GitCloneAsync`（`:612`）/ `GitFetchAsync` / `GitPushAsync`（`:671`）；
  - ⚠️ **破坏性：`GitResetAsync`（`:689`，接受 `mode="hard"`）/ `GitCleanAsync`（`:730`，支持 `force + directories`）**——初稿漏了这两个，见 §8.3；
  - worktree 类：`EnterWorktreeAsync` / `FinishWorktreeAsync` / `AbandonWorktreeAsync`。
- **框架文件工具（与 C5′ 相关，⚠️ v0.3 新增事实）**：`FileSystemToolPlugin.DeleteDirectoryAsync(path)`（`:807`）是**裸 `Directory.Delete(path, recursive: true)`**，`catch` 里对 `UnauthorizedAccessException` 只回失败文案——**不处理 Windows 只读位**（git pack 文件是只读的，正是 RC5 的根因）。确认回调传 `["path"]=path`（`:811-812`），故策略侧可据 `path` 判工作区边界。
- **云效连接器**（产品侧 `LubanAgentCore/Connectors/AliyunDevOps/`）：`CodeupClient` 有 `SearchRepositoriesAsync`(L95) / `GetRepositoryAsync`(L149) / `CreateRepositoryAsync`(L178) / `UpdateRepositoryAsync`(L213) / `ListBranchesAsync`(L255) / `ListTemplateRepositoriesAsync`(L298) / `ListNamespacesAsync`(L329) / `CreateNamespaceAsync`(L370) / `CloneToLocalAsync`(L411) / `PushAsync`(L468)；`FlowClient` 有 `ListPipelinesAsync`(L90) / `GetPipelineAsync`(L130) / `CreatePipelineAsync`(L164) / `UpdatePipelineAsync`(L204) / `RunPipelineAsync`(L244) / `ListHostGroupsAsync`(L320) / `GetHostGroupAsync`(L352)。
  - ⚠️ v0.2 修正的关键签名事实（决定了 §7 的工具入参）：
    - `ListTemplateRepositoriesAsync(int templateType = 1, int page = 1, int perPage = 100, string? organizationId = null, …)`——**没有 `search` 参数**。带 `search` 的是 `SearchRepositoriesAsync`（搜普通仓库，**不等于模板库**）。定位模板仓只能"全量拉（`perPage≤100`）+ 客户端按 `name` 匹配"；
    - `GetRepositoryAsync(string repoId, …)`——**只接受仓库 id**，没有"命名空间 + 路径"重载；
    - `YunxiaoRepository`（`YunxiaoModels.cs:104`）**只有 `HttpUrlToRepo`(L129) / `SSHUrlToRepo`(L135) / `DefaultBranch`(L153) / `Path` / `Visibility` 等，没有 `WebUrl`**。页面地址必须由 `NewProjectService.BuildWebUrl(httpUrlToRepo)`（`:3005`）推导；
    - `ListNamespacesAsync(string? search, long? parentId, int page, int perPage, …)`——**有 `search` 与 `parentId`**；
    - `GetHostGroupAsync(long hostGroupId)`——**路径键必须是数字 id**，传 uuid 会 400 `NumberFormatException`（PRD v0.32 实证）。
- **MCP 工具面**：`YunxiaoToolDefinitions.cs` + `YunxiaoMCPClient.cs` 现共 **12** 个工具（已核对）：`codeup_search_repos`、`codeup_clone`、`codeup_create_repo`、`codeup_push`、`codeup_list_branches`、`codeup_create_group`、`flow_list_pipelines`、`flow_list_host_groups`、`flow_get_host_group`、`flow_create_pipeline`、`flow_update_pipeline`、`flow_run_pipeline`。本次**删 2**（`codeup_clone` / `codeup_push`）/ **补 3** ⇒ **13**。
- **插件包铺装**：`LubanAgent.Plugin.NewProject.csproj` 的 `StagePluginPackage`（`:53`）只铺 `plugin.json` / `newproject.settings.json` / 自身 dll+deps.json / `skills/`（`:59`，源项 `PluginSkillFile Include="skills\**\*.md"`）——**确实不铺 docs**，本次要加（§4.2 已给出具体改法）。
- **插件持有点**：`plugin.json` 声明 6 个技能（`newproject-create` / `-clone-template` / `-setup-db` / `-build` / `-push-repo` / `-create-pipeline`）、`mcpServers: [yunxiao]`、permissions（含 `git:clone_push` / `git:commit` / `git:inspect` / `process:git` / `filesystem:workspace`）。
- **宿主侧工具**：`DirectoryCleaner` 位于 **`LubanAgentCore/Utils/DirectoryCleaner.cs`**（⚠️ v0.2 修正：不是插件自带文件；插件经编译期 `ProjectReference`→宿主默认 ALC 复用同一份类型，运行期不产生第二副本）。`SecretMasker` 同在 `LubanAgentCore/Utils/`。
  - ⚠️ v0.3：`DirectoryCleaner` **保留**（宿主公用；`NewProjectService` 的中止清理 `:2981` 仍在用，`DirectoryCleanerUnitTest` 继续锁底）；只有 Step 3"清空后重克隆"的删除改为助手执行（C5′）。

### 3.2 现状缺口（不补则设计不可行）

| 缺口 | 事实 | 影响 |
|------|------|------|
| **MCP 无"模板库检索"** | `CodeupClient.ListTemplateRepositoriesAsync`（`templateType=1`）未暴露；技能正文自认"模板库查询暂无对应 MCP 工具" | 助手无法定位 `templateProjectId`，Step 3 无法建库 |
| **MCP 无"仓库详情"** | `GetRepositoryAsync` 未暴露；`codeup_search_repos` 明写"返回体不含 HTTPS 克隆地址" | 助手拿不到 `httpUrlToRepo`，无法克隆、无法复用已有仓库 |
| **MCP 无"代码组检索"** | `ListNamespacesAsync` 未暴露；`codeup_create_group` 明写"不做重名查重、先查后建由向导后端负责" | 助手无法实现"同名代码组复用"，会重复建组或撞名 |
| **`AssistantToolPolicy` 会拒绝全部 MCP 工具调用（硬阻塞）** | MCP 工具的确认回调是 `MCPToolPlugin.MCPToolAIFunction.InvokeCoreAsync` → `EvaluateAsync(_name, path: null, args)`（`MCPToolPlugin.cs:135`）；`ToolConfirmationService.EvaluateAsync` 在 `Default` 模式下对 `path = null` 走 `IsAuthorizedPath(null) = false` → `AskUserAsync` → 插件回调 `AssistantToolPolicy.Decide`（`HostAssistantBridge.cs:218`）；工具名是 `mcp_<client>_<tool>`（`MCPToolPlugin.cs:113`，即 `mcp_yunxiao_codeup_create_repo`），不在白名单 → **末尾 `Deny`** | Step 3/6/7 的云效动作**全部被拒**，链路跑不通。详见 §8 |
| **git 工具组反而"不用管"（与直觉相反，但只限良性动作）** | `GitToolPlugin.RunGitAsync` 用 `confirmPath ?? ResolveWorkingDirectory(workingDirectory)` 作确认路径（`:250-251`）；`workingDirectory` 为空时回落 `_sessionContext.EffectiveRoot`（＝项目工作区根，`:52`/`:185-189`）；`GitCloneAsync` 显式传 `confirmPath: target`（`:626`）→ 落在工作区内 → `IsAuthorizedPath` 命中 → 框架**直接放行、不触发回调** | 白名单里加不加 git 工具名**没有区别**；但**破坏性动作同样免回调**（§8.3 如实记录） |
| **删除类工具被策略无条件拒绝（与 C5′ 相关）** | `AssistantToolPolicy.DeleteTools`（`:92-96`）含 `DeleteDirectoryAsync`，在 `:143-147` **无条件 `Deny`**；而写类走 `WorkspaceWriteTools` + `IsUnderWorkspace`（`:155-175`） | "清空后重克隆"若交给助手，必须先把 `DeleteDirectoryAsync` 挪到"工作区内按路径放行"的分支（C5′） |
| **`StepReport` 无处安放"产物账目"与"待用户输入"** | 现有 schema 只有 `step` / `ok` / `checks[]` / `message` | 档案落账（仓库 id / 地址 / 流水线 id）与交互项（克隆冲突三选一、部署目标选择）没有契约 |
| **结论文件扩不了类型（运行期崩）** | `Serialization/PluginJsonContext.cs` 只登记 3 个类型（`NewProjectSettings` / `ProjectSpec` / `StepReport`，`:43-45`）；插件 ALC 硬约束**不得反射序列化** | 新 schema 的每个类型**必须**登记，否则运行期 `NotSupportedException`——初稿把"扩 schema"写成了"填字段" |
| **四环第 4 环在云效/git 动作上收不到证据** | `AgentHostService` 只在 ①AI Function 抛异常 ②`ToolResult.IsUserCancelled` 时发 `ToolCallFailedEvent`；`MCPToolPlugin` 调用失败**返回普通字符串** `"MCP 工具调用失败: …"`（`:146`），git 业务失败返回 `ToolResult.Fail`——**都不是异常也不是取消** ⇒ 不进 `TurnOutcome.Errors` | Step 3/6/7 的第 4 环**恒空**，判定退化为"助手自报"；必须补独立证据（见 §6.1.4） |

### 3.3 环境约束（必须写进 docs 的"前置条件"）

- 云效 PAT：仍在 codex「设置 → 云效连接」里配置，由连接器注入 MCP；**不入工具入参**。
- git 凭据：由用户在**外部环境**配置（git credential helper / 系统凭据管理器 / 已配置的 remote），插件与技能不提供凭据通路；缺失时助手把 git 原文报错带出来，提示"请先在外部环境配好 codeup HTTPS 凭据"。
- 模板仓必须是**模板库**（`templateType=1`）条目，普通仓库搜索不保证命中。
- 建库后模板分支补齐是服务端异步（实证 10–17s），必须轮询等 pin 分支出现。
- 管理端仓库要求 Node 16（本组织姿势 `fnm exec --using=16 -- npm.cmd …`），该约定移入 docs。
- ⚠️ v0.3：**Windows 下含 `git` 仓库的目录有只读 pack 文件**，任何"整目录删除"都要处理只读位（RC5），见 §6.4。

---

## 4. 知识文件布局（G5 落实）

### 4.1 目录

```
LubanAgent.Plugin.NewProject/
├── plugin.json                     # 技能清单（改 description；新增 docs 无需登记）
├── newproject.settings.json        # 瘦身：只留 UI 预填用的短格式值
├── skills/                         # 编排入口（宿主 RegisterSkill 落进工作区）
│   ├── newproject-create.md              # 七步总入口（编排 + 闸门 + 交互约定）
│   ├── newproject-clone-template.md      # Step 3
│   ├── newproject-setup-db.md            # Step 4（仅同步措辞/路径）
│   ├── newproject-build.md               # Step 5（仅同步措辞/路径）
│   ├── newproject-push-repo.md           # Step 6
│   └── newproject-create-pipeline.md     # Step 7
└── docs/                           # 知识正文（随包分发，Step 2 复制进工作区）
    ├── README.md                         # docs 索引：哪份讲什么、何时读
    ├── org-reference.md                  # 组织专有值总表（§4.3）
    ├── step3-repositories.md             # 模板定位 / pin 择优 / 建组建库 / 克隆 / 占位符替换约定
    ├── step6-push.md                     # 分支口径 / 提交身份 / 推送姿势 / 凭据失败处置 / 限流退避
    ├── pipeline-service.yml.md           # 后端新版 YAML 模板（含 deploy 段占位说明）
    ├── pipeline-admin.yml.md             # 管理端（node_build + OSS 上传）
    ├── pipeline-mobile.yml.md            # 移动端（node_build + OSS 上传）
    └── pipeline-conventions.md           # 名称格式 / 变量与缓存 / 待人工绑定清单 / 覆盖更新口径
```

> **说明（助手替用户拍的决定，请复核）**：`docs/` 里的三端模板用 `*.yml.md` 后缀（Markdown 包裹的 YAML 代码块），既能被助手当文档读，也避免构建器把 `.yml` 当资源处理。若倾向纯 `.yml`，只需改扩展名，不影响其余设计（**但注意"技能只认单文件 md"的机制不适用于 docs——docs 不注册成技能**，见 C1）。

### 4.2 铺装与可读性（关键机制）

- **打包（必须三处一起改，缺一处就"设计跑不通"）**：
  1. `ItemGroup` 新增 `<None Include="docs\**\*" CopyToOutputDirectory="PreserveNewest" />`；
  2. 新增 `<PluginDocFile Include="docs\**\*.md" />`；
  3. `StagePluginPackage` 目标里：`MakeDir` 加 `$(PluginPackageDir)docs`，并加一条 `<Copy SourceFiles="@(PluginDocFile)" DestinationFolder="$(PluginPackageDir)docs" SkipUnchangedFiles="true" />`。
- **落进工作区**：**宿主不会自动铺 `docs`**（`TryWriteSkillFile` 只处理技能单文件；`StagePluginPackage` 白名单也只认 `skills/`）。因此 Step 2 由插件把 `docs/**` 复制到
  `{本地目录}\.luban-agent\plugin-newproject\docs\`（插件已有 `filesystem:workspace` 权限；复制是覆盖式，保证升级后为最新，并**不删除**用户手工补充的文件）。
- **助手如何找到它**：技能正文与每步提示词都写明该绝对路径（提示词由插件构造，可注入实际路径）；`docs/README.md` 作为索引。
- **"项目工作区里能查到"**：`docs/` 与 `results/` 都在项目目录内，用户可直接打开查看；这也是 G6 的落点之一。
- ⚠️ v0.2：已建项目的 `docs/` 是**创建时快照**（覆盖只发生在 Step 2 再跑）；升级插件后**不会**回改老项目。这与 §15 风险表一致，需要在插件 README 里说明"知识更新只对新建项目即时生效"。

### 4.3 `docs/org-reference.md` 承载的组织专有值（用户点名的清单）

逐条列出并注明取值来源与生效位置（本文件即"集中改这里、不必重发插件"的入口）：

| 类别 | 内容 |
|------|------|
| 模板仓 | 模板库定位约定（**必须走模板库列表接口全量拉取后按名匹配**）、三端模板仓名（`TJCES-Service` / `TJCES-Admin` / `TJCES-Mobile`）、可选 `projectId` 登记 |
| 项目命名 | 代码组名格式（`{CODE}`）、仓库名格式（`{Code}-{end}`，**注意是 `{Code}` 不是 `{code}`**）、推送分支（`develop`） |
| pin 择优 | `develop → master → main → 模板默认分支`（及"登记值必须真实存在，否则阻断"） |
| 占位符 | `TJCES→{CODE}` / `Tjces→{Code}` / `tjces→{code}`，令牌语义、跳过目录、大小写与替换顺序、幂等要求 |
| 后端流水线 | 端口 `7000`、镜像/容器名 `{code}-service`、tag `latest`、dockerRepo `{branch}`、Harbor `harbor.tjingcai.com`、codeup 服务连接 `r3x6es4esgbsn6ur`、主机组默认值、`buildCluster` 留空须人工绑定 |
| 前端流水线 | OSS 服务连接 `sm7mho5x9ex945rp`、bucket `tejingcai`、region `cn-shanghai`、目录 `DEV/{CODE}-Admin/`、`DEV/{CODE}-Mobile/`、上传源目录 `dist` / `dist/dev/h5`、构建集群留空 |
| 构建姿势 | 后端 `dotnet build`；管理端 `fnm exec --using=16 -- npm.cmd …` + `npm run build:dev`；移动端同理 |
| 提交身份 | 优先本机 `git config user.name/user.email`，缺失时兜底 `LuBan Agent <luban-agent@local>` |

> 三端 YAML 模板正文（`pipeline-*.yml.md`）为**翻译稿**，不是企服版 `flow` 原文照抄——PRD v0.30 已实证照抄必报 `1209300`。`newproject.settings.json` 里的 `pipelineReferences[].pipelineId`（3481236 / 3489337 / 3489317）作为**溯源标注**迁到 `pipeline-conventions.md`。

### 4.4 `newproject.settings.json` 瘦身后保留什么

只留 **UI 建单前就要用到的短格式值**（面板在跑任何助手之前要回显/预填）：

```jsonc
{
  "repositoryNameFormat": "{Code}-{end}",   // ⚠️ v0.2：实为 {Code}（大写 C），初稿写成 {code}
  "groupNameFormat": "{CODE}",
  "pushBranch": "develop",
  "defaultVisibility": "internal",
  "commitAuthorName": "LuBan Agent",
  "commitAuthorEmail": "luban-agent@local"
}
```

同步删除 `NewProjectSettings.cs` 的对应成员（`Templates[]` / `PipelineDefaults[]` / `PipelineReferences[]` / `PlaceholderRules[]` / `GroupParentId`）——**这些成员被面板与渲染器引用，删成员必然连带改调用点**（见 §17 同步清单），不是"改 JSON 就行"。

> **说明（替用户拍的决定，请复核）**：用户要求移出的是"模板仓名、端口、Harbor、OSS 桶/前缀、服务连接、主机组 uuid、构建姿势、pin 择优顺序 + YAML 模板"——这些**全部**移入 `docs/`。这里保留的 6 项是**格式与兜底**（不含任何组织敏感值），因为创新项目表单要在助手介入前就能渲染。若希望这些也进 docs，则建单表单需先跑一轮助手取格式值（多一轮交互、且离线不可用），不推荐。

---

## 5. 逐步映射：现状 → 目标

### 5.0 轮次口径（C6′ 定案，全文统一）

> **Step 3 / Step 7 不是"两轮"，而是"一次只读准备下发 + 用户确认后同会话执行续跑"。**
> 措辞上全文统一为「**准备**」与「**执行续跑**」，不再出现"两轮"。
> 实质未变：宿主 API 是"一轮一问一答"，`needsInput` 得到答复后，执行续跑在实现上仍是插件**再发一次提示词**——
> 这一点在 §6.1.3 如实写清，但**不把它叫成"两轮"**，以免与"每个步骤一次下发"的直觉打架。

### 5.1 总表

| 步 | 现状（C# 实现位置） | 目标 | 插件保留物 |
|----|---------------------|------|-----------|
| Step 1 | `ValidateBasicInfoAsync`（L172，本地） | **不变**（薄校验） | 全套 |
| Step 1' | `ValidateRepositoryPlanAsync`（L539，本地）+ `ApplyRepositoryPlan`（本地） | **不变**（薄校验：名字非空/合法/同批不重名/可见性取值） | 全套 |
| Step 2 | `InitializeAsync`（L344，宿主 API + 注册技能/MCP） | **基本不变**，新增 `docs` 铺装 | 全套 |
| Step 3 准备 | `PrecheckRepositoriesAsync`（L627，只读直连 Codeup） | **下沉**：助手一次只读下发 → 结论文件 → 面板渲染确认界面 | 面板渲染（吃 `artifacts[]`）+ 归档 |
| Step 3 执行 | `CreateRepositoriesAsync`（L766，建组/建库/**补查地址**/等分支/克隆/占位符） | **下沉**：助手执行续跑（可含交互项）→ 结论文件 → 落账 + **本地复核** | 提示词构造 + 结论回读 + **Step 3 重跑联动失效** |
| Step 4 | `SetupDatabaseAsync`（L1158）+ `SuggestDatabaseProbe`（本地） | **不变** | 全套 |
| Step 5 | `BuildAsync`（L1280） | **不变** | 全套 |
| Step 6 | `PushAsync`（L1440，`RunGitAsync` 本地 git + 连接器 push） | **下沉**：助手调 git 工具组 → 结论文件 → 落账 + 本地复核 | 提示词 + 回读 + `StepGate`（**判定源改读 Step 5 结论文件**，C7′）+ 限流退避口径（原 `PushRetryPolicy` 的约定迁 docs） |
| Step 7 准备 | `PreviewPipelines`（本地渲染）+ `ListDeployTargetsAsync`（L1722 直连 Flow） | **下沉**：助手一次下发（拉主机组 + 渲染 YAML）→ 结论文件 → 面板复核/微调/选目标 | 面板渲染（吃 `artifacts[]`）+ 归档 |
| Step 7 执行 | `CreatePipelinesAsync`（L1640）/ `UpdatePipelinesAsync`（L1647） | **下沉**：助手执行续跑 → 结论文件 → 落账 | 提示词 + 回读 |
| 中止清理 | `AbortAndCleanupAsync`（L2066） | **不变**（远端不自动删，只留孤儿清单；本地回收仍走 `DirectoryCleaner`） | 全套 |

### 5.2 Step 3 详细流程（准备 → 用户确认 → 执行续跑）

**准备 · 只读**（提示词 = `skills/newproject-clone-template.md` 的"预检"节 + `docs/step3-repositories.md` 路径）

助手动作（全部只读）：

1. 用 `mcp_yunxiao_codeup_list_template_repos`（`templateType=1`，`perPage=100`）**全量拉取模板库条目**，在客户端按各端名称约定（`TJCES-Service` 等）匹配出 `templateProjectId`——⚠️ v0.2：该接口**没有 `search` 入参**，不能指望服务端按名过滤；
2. 用 `mcp_yunxiao_codeup_list_branches`（`repositoryId` = `templateProjectId`）取模板仓分支，按 `develop → master → main → 模板默认分支` 解析 pin；
3. 用 `mcp_yunxiao_codeup_list_groups`（`search` = 代码组名）查目标代码组是否已存在（是否存在决定"新建/复用"）；
4. 用 `mcp_yunxiao_codeup_search_repos`（`search` = 仓库名）查目标仓库是否已存在；**若已存在，再调 `mcp_yunxiao_codeup_get_repo`（`repositoryId` = 命中项 id）取 `httpUrlToRepo`**；
5. 检查本地 `<localPath>\<仓库名>` 目录是否已存在且非空。

产出结论文件 `step3-*.json`：`status = "done"`，把上面的事实写进 **`artifacts[]`**（`kind=group` 一条 + `kind=repository` 每端一条，字段见 §6.1.1 的映射表），`checks[]` 逐项记录。面板据此渲染**确认界面**（沿用现有预检视图），用户可改仓库名/可见性（走 `ValidateRepositoryPlanAsync` + `ApplyRepositoryPlan`）。

**执行续跑 · 建组建库并克隆**

提示词带上用户确认后的建库计划 + 准备阶段结论文件路径 + 用户答复（`answers`）。助手动作：

1. 代码组不存在 → `mcp_yunxiao_codeup_create_group`（先查后建由助手守）；
2. 仓库不存在 → `mcp_yunxiao_codeup_create_repo`（`templateProjectId` / `templateType=1` / `syncAllBranches=true` / `namespaceId` / `path`）；
3. ⚠️ **v0.2 补**：新建仓库**必须再调一次 `mcp_yunxiao_codeup_get_repo`（`repositoryId` = 建库响应里的 id）取 `httpUrlToRepo`**——建库响应不保证带该字段，现状 `NewProjectService.cs:949-950` 也正是"建库后再查一次"。仅"复用已存在仓库"时才能直接用准备阶段拿到的地址；
4. 轮询 `mcp_yunxiao_codeup_list_branches` 等 pin 分支就绪（上限 120s / 间隔 3s，超时即失败）；
5. `GitCloneAsync(-b <pin> <httpUrlToRepo> <localPath>\<仓库名>)`；
6. 克隆失败 → 记录 git 原文摘要（**不内嵌凭据**），**不**自行删目录，写 `status="failed"`（残余目录清理见 §6.4）；
7. 目标目录非空（准备阶段已发现或 clone 时报错）→ 写 `status="needsInput"`，`inputs[]` 带三选一（见 §6.3）；
8. 占位符替换（见 §6.2）；
9. `GitStatusAsync` 复核工作树与 `refs/heads/<pin>` 可用后，把 `cloned=true` 与产物账目写进 `artifacts[]`。

插件回读：`artifacts[]` → 档案 `Repositories[]` / `RemoteArtifacts[]`（与今天字段一致：`ExternalId` / `WebUrl` / `CloneUrl` / `LocalPath` / `TemplateBranch` / `Cloned`）；**再执行 §6.1.4 的本地复核**；**通过后触发 §6.6 的重跑联动失效**。

### 5.3 Step 6 详细流程（一次下发）

提示词（`skills/newproject-push-repo.md` + `docs/step6-push.md`）：

1. 逐端：`GitStatusAsync` 看工作树；无改动则跳过（如实报告）；
2. 建/切分支：`pushBranch = develop`（`GitCheckoutAsync(-b)`，已存在则切过去）；
3. 提交身份兜底：读 `git config user.name/user.email`，缺失时按 docs 兜底值**在本仓库级**设置；
4. `GitAddAsync(-A)` → `GitCommitAsync(<消息>)`（提交信息格式写进 docs）；
5. `GitPushAsync(origin develop)`（`-u`）；**凭据由用户外部配置**；
6. 失败处置：`Authentication failed` / `could not read Username` 之类 → 结论文件 `status="failed"`、`message` 带 git 原文摘要 + 固定提示"请先在外部环境配好 codeup HTTPS 凭据（git credential helper / 系统凭据管理器）"；429 / 限流 → 按 `docs/step6-push.md` 的退避约定重试（⚠️ **线性 10s / 20s / 30s，最多 3 次**，与现已删除的 `PushRetryPolicy` 口径一致；v0.1 写的"指数间隔"是错的），仍失败即失败；
7. 成功 → `artifacts[]` 记录各端分支与提交 id。

**闸门（C7′）**：`StepGate.CanPush` 保留"编译未过不能推送"的阻断，但**判定源从 `StepStates["Build"]` 改为 Step 5 的结论文件**（详见 §6.7）：读**最近一份** `results/step5-*.json` 的逐端 `BuildResults`，**逐端全 Ok 且覆盖 `spec.Ends` 全集**才放行；`StepStates[step]` 仅留作界面进度显示，**不再作闸门依据**。`CanCreatePipelines` 同口径改读 Step 6 结论文件。

### 5.4 Step 7 详细流程（准备 → 用户确认 → 执行续跑）

**准备**（`mcp_yunxiao_flow_list_host_groups` + `flow_get_host_group` + 读 `docs/pipeline-*.yml.md`）

1. 拉主机组候选（**数字 id** + uuid + 名称 + 机器数；需要时 `flow_get_host_group(hostGroupId=数字id)` 取机器明细）；
2. 按 docs 模板逐端渲染新版 YAML（填项目令牌、端口/镜像/OSS/服务连接等；`{branch}` 取 `pushBranch`）；
3. 产出结论文件：`artifacts[]` 的 `kind=pipeline` 每端一条（`yaml` / `pendingBindings` / `yamlIncludesDeployStage` / 推荐部署目标见 §6.1.1）+ `inputs[]` 带**部署目标选择**（有独立部署阶段的端必选；选项**同时带数字 id 与 uuid**，见 §6.3）+ "待人工绑定清单"。

面板：展示 YAML（**可手工微调**）、部署目标下拉（只读回显机器明细）、待绑定清单。
⚠️ **C10′：面板不再做 YAML 即时校验**——`PipelineYamlEditorWindow` 只是一个文本框，`name:` 一致性 / Tab / 顶层 `stages:` 等一律**交给云效报回**（失败即 `status="failed"`，文案取云效原文）。微调后的文本随 `answers` 进执行续跑。

**执行续跑 · 创建或覆盖更新**

- 创建：`mcp_yunxiao_flow_create_pipeline`（逐端 1 条，最多 3 条）；**建完不自动运行**；
- 覆盖更新（v0.32 补充操作）：按档案里的 `PipelineId` 直改 → `mcp_yunxiao_flow_update_pipeline`；
- 部署段随创建写入；缺目标即失败、不发请求。

### 5.5 Step 3/6/7 之后仍留在插件里的 C#（"薄"的边界）

| 保留 | 理由 |
|------|------|
| `ValidateBasicInfoAsync` / `ValidateRepositoryPlanAsync` / `ApplyRepositoryPlan` | 纯本地输入校验与档案写回 |
| `StepGate`（**判定源改为读结论文件**，C7′）+ **Step 3 重跑联动失效（`InvalidateBuild` / `InvalidatePipelineConclusion`）** | 本地判定，不触网（§6.6 / §6.7） |
| `NewProjectStateStore` / `StepStateResolver` / `ProjectSpec` 等档案模型 | 进度与落账载体（`StepStates` 降级为"仅界面进度"） |
| `HostAssistantBridge` / `AssistantToolPolicy` / `StepPrompts` / `StepReportParser` / `StepReportEvaluator` / `StepResultFile` | 路线 ② 脚手架（本次扩展 schema 与**新增判定入口**，见 §6.1） |
| **本地复核器（新增，`StepLocalVerifier`）** | §6.1.4：对结论文件做插件侧可独立核对项（目录/`.git`/refs/占位符残余），补第 4 环的空洞 |
| `AssistantToolPolicy` 的**删除放行**（C5′） | `DeleteDirectoryAsync` 从"无条件拒绝"改为"工作区内按路径放行"（§8.6）；`DirectoryCleaner` 仍留在宿主供中止清理使用 |
| `DatabaseProbeDefaults` / `SuggestDatabaseProbe` | Step 4 表单本地预填（Step 4 不改） |
| `OrganizationHint`（读云效连接配置） | 只读展示 |

| 删除 | 理由 |
|------|------|
| **`Services/PipelineTemplates.cs` 整个文件**（`PipelineTemplateLibrary` + `PipelineRenderer` + `PipelineYamlOverride`）——C10′ | 三端 YAML 进 `docs/pipeline-*.yml.md`；渲染改由助手；`PipelineYamlOverride` 的"名一致"即时校验一并去掉（交给云效报回） |
| `PlaceholderReplacer.cs` | 改由助手现场脚本完成（§6.2），规则进 docs |
| `PushRetryPolicy.cs` | 退避约定进 `docs/step6-push.md` |
| **`CodeupClient.CloneToLocalAsync` / `PushAsync` + 私有 `ToGitCommandResult`**（C8′） | git 操作全走 git 工具组；这两个方法在 `LubanAgentCore` 内**已无其他消费者** |
| **`Connectors/AliyunDevOps/GitCommandResult.cs`**（C8′） | 只被上面两个方法（及其单测/`PushRetryPolicy`）使用；三者同批删除后成为死代码 |
| MCP 的 **`codeup_clone` / `codeup_push`**（定义 + 分派 + 参数解析 + 响应投影）（C8′） | 克隆/推送一律走 git 工具组（G2）；留着会形成"绕过 git 工具组"的第二条凭据通路 |
| `NewProjectService` 中 Step 3/6/7 的执行体（`CodeupClient`/`FlowClient` 直连、`RunGitAsync`、认证 URL 拼接） | 全部下沉；方法签名保留为"下发 + 回读" |
| `newproject.settings.json` 与 `NewProjectSettings.cs` 中的组织专有值（`templates[]` / `pipelineDefaults[]` / `pipelineReferences[]` / `placeholderRules` / `groupParentId`） | 迁入 `docs/`（§4.4 仅留 6 项格式值）；⚠️ 删成员**连带改**面板/渲染器调用点（§17） |

| 视图模型（保留，但数据来源改） | 说明 |
|------|------|
| `RepositoryPrecheck` / `EndPrecheck` / `PipelinePreview` | 保留为面板输入契约；**数据来源改为结论文件的 `artifacts[]`**（映射表见 §6.1.1，C11′）。**不再**新增 `precheck` / `preview` 专用段 |

| **删除后必须一并处理**（不做则构建直接红） | 处理方式 |
|------|------|
| `LubanAgent.Tests/NewProjectPipelineTemplateUnitTest.cs`（30+ 处引用 `PipelineTemplateLibrary` / `PipelineRenderer` / `PipelineYamlOverride`） | 整类**删除**；改为新增"`docs/` 模板文件存在性与结构"测试（见 §12-2） |
| `LubanAgent.Tests/NewProjectPushRetryUnitTest.cs`（整类为 `PushRetryPolicy` 而写） | 整类**删除**；退避口径改为 docs 约定（如要保留口径，改为断言 docs 文本片段） |
| `LubanAgent.Tests/NewProjectStepGateUnitTest.cs`（判据 = `StepStates["Build"]` + `BuildResults`） | **重写判据**为"读 Step 5 结论文件"（C7′） |
| `LubanAgent.Tests/YunxiaoToolDefinitionsUnitTest.cs`（断言工具数 = 12，且列了 `codeup_clone` / `codeup_push`） | 断言改 **13**；删两行；补 3 个新工具的 schema 断言 |
| `LubanAgent.Plugin.NewProject/skills/newproject-push-repo.md:38`（引用 `mcp_yunxiao_codeup_push`） | 改走 `git` 工具组的 `GitPushAsync` |
| `LubanAgent.Plugin.NewProject/README.md:220/225/227/243`（封 `DirectoryCleaner` 与 `CodeupClient` 直连） | 改写为"助手执行 + 表驱动策略"版 |
| `LubanAgentCore` 连接器 README / `luban-agent/docs/CONNECTOR-SMOKE-TEST.md`（列了 12 个工具） | 12 → 13，删 2 补 3 |

---

## 6. 关键机制的落地细节

### 6.1 结论文件契约（Step 3 / 6 / 7）

路径沿用 `StepResultFile`：`{workspace}\.luban-agent\plugin-newproject\results\step{N}-<ts>.json`。

在 `Abstractions/StepReport.cs` 上**扩展**（保持 Step 4/5 向后兼容：新字段可选、默认值不破坏旧判定）：

```jsonc
{
  "step": 3,
  "ok": true,
  "status": "done",            // done | needsInput | failed（建议用 string 而非 enum：
                               //   enum 遇未知值会在源生成反序列化阶段直接抛异常，
                               //   与"文件不存在"混在一起，不利于给出可操作文案）
  "checks":    [ { "name": "代码组已存在", "ok": true, "detail": "id=123, 将复用", "command": null } ],
  "inputs":    [ /* 见 §6.3，仅 needsInput 时非空 */ ],
  "artifacts": [ /* 见下：**唯一的面板数据源 + 落账来源**（C11′） */ ],
  "message":   ""              // 人话总结；failed 时必填可操作原因
}

// artifacts[] 元素（三种 kind，extra 按 kind 一一对应且必须强类型）
{
  "kind": "group | repository | pipeline",
  "end": "Service | Admin | Mobile | null",
  "name": "csxmy-Service",
  "externalId": "123456",                            // 组 id / 仓库 id / 流水线 id
  "webUrl": "https://codeup.aliyun.com/...",
  "cloneUrl": "https://codeup.aliyun.com/....git",   // repository 专用
  "localPath": "D:\\work\\csxmy\\csxmy-Service",     // repository 专用
  "templateBranch": "develop",                        // repository 专用（= 解析出的 pin）
  "yaml": "stages:\n ...",                            // pipeline 专用（准备阶段产出）
  "extra": { /* GroupExtra | RepositoryExtra | PipelineExtra，见下表 */ }
}
```

**⚠️ v0.2 必做项（初稿遗漏，不做就运行期崩）**：新增的每个持久化类型**必须**登记进 `Serialization/PluginJsonContext.cs` 的 `[JsonSerializable]` 列表（现仅 3 个）——插件 ALC 硬约束不得反射序列化。本设计需登记：`StepReportArtifact` + `GroupArtifactExtra` + `RepositoryArtifactExtra` + `PipelineArtifactExtra`（+ `StepReportInput` / `StepReportInputOption`）。

**⚠️ C11′ 硬约束**：`extra` **不得**是 `Dictionary<string, object?>`（那样面板映射与源生成都保不住）。每个 `kind` 对应一个**具名类型**，字段集合由本设计固定（见 §6.1.1 映射表），助手只能填值、不能变形。

**判定口径（四环，Step 3/6/7 各步一个判定入口）**：

1. 结论文件存在且非空；
2. 能以源生成上下文解析（`PluginJsonContext`）；
3. `status == "done"` 且 `ok == true`（Step 3/6/7 都是**阻断性**步骤——与 Step 4 的"不阻断"不同）；
4. 本轮事件流无**真错误**（`AssistantToolPolicy.IsGuardDenial` 判定为守卫拒绝/取消的不计，沿用既有口径）**且** §6.1.4 的本地复核通过。

⚠️ v0.2：`StepReportEvaluator.EvaluateProbe` / `EvaluateBuild` **硬绑 `report.Step != 4` / `!= 5` 即失败**，不能复用。需要**新增** `EvaluateRepositorySetup`(3) / `EvaluatePush`(6) / `EvaluatePipeline`(7) 三个纯函数入口（同样供单测锁死口径）。

`status == "needsInput"` → 插件渲染交互项，收到用户答复后**再下发执行续跑**（协议见 §6.1.3）；`status == "failed"` → 本步失败、不推进、文案取 `message`。

#### 6.1.1 `artifacts[]` → 面板视图模型映射（C11′，替代 v0.1 的 `precheck` / `preview` 段）

`artifacts[]` 既是**产物账目**（落进档案），也是**面板视图模型的唯一来源**。映射逐字段固定如下（面板不读 `extra` 的任意键，只读这些具名属性）：

| 目标视图模型 | 来源 | 字段映射 |
|--------------|------|----------|
| `RepositoryPrecheck.OrganizationId` | `artifacts[kind=group].extra.organizationId` | 直取 |
| `RepositoryPrecheck.GroupName` / `GroupId` / `GroupWebUrl` | `artifacts[kind=group]` | `name` / `externalId` / `webUrl` |
| `RepositoryPrecheck.GroupExists` / `GroupParentId` | `artifacts[kind=group].extra` | `exists` / `parentId` |
| `RepositoryPrecheck.Ends[]`（`EndPrecheck`） | `artifacts[kind=repository]`（**每端一条**） | 见下 |
| `EndPrecheck.End` / `RepositoryName` / `RepositoryWebUrl` / `LocalPath` / `ResolvedBranch` | 同上 | `end` / `name` / `webUrl` / `localPath` / `templateBranch` |
| `EndPrecheck.TemplateRepository` / `TemplateProjectId` / `TemplatePath` / `TemplateBranches` / `ResolvedBranchSource` / `RepositoryExists` / `LocalPathOccupied` / `Visibility` / `Error` | `artifacts[kind=repository].extra` | 同名字段（`Error` 为 `null` 表示该端可执行） |
| `RepositoryPrecheck.Errors` / `Warnings` | `checks[]`（`ok=false` → `Errors`；`ok=true` 且 `detail` 以"提示"开头 → `Warnings`） | 由插件归并 |
| `RepositoryPrecheck.TakenAt` | 结论文件 `mtime` | 直取（不再回显"预检早于输入"以外的时间语义） |
| `PipelinePreview[]` | `artifacts[kind=pipeline]`（**每端一条**） | `end` / `name`→`PipelineName` / `yaml`→`Yaml` / `externalId`→`PipelineId` / `webUrl`→`WebUrl` |
| `PipelinePreview.PendingBindings` / `YamlIncludesDeployStage` / `RecommendedDeployTargetUuid` | `artifacts[kind=pipeline].extra` | `pendingBindings` / `yamlIncludesDeployStage` / `recommendedDeployTargetUuid` |
| `PipelinePreview.Edited` | 面板本地状态（微调后由面板置位；**不再来自助手**） | — |

`extra` 的三个具名类型（字段即上表右列）：

```jsonc
// GroupArtifactExtra
{ "exists": true, "parentId": null, "organizationId": "..." }

// RepositoryArtifactExtra
{ "templateRepository": "TJCES-Service", "templateProjectId": 987,
  "templatePath": "tjc/TJCES-Service", "templateBranches": ["develop","master"],
  "resolvedBranchSource": "按 develop→master→main 择优",
  "repositoryExists": false, "localPathOccupied": false, "visibility": "internal",
  "error": null,
  "cloned": true, "defaultBranch": "develop", "commitId": "abc123" }   // 执行续跑阶段回填

// PipelineArtifactExtra
{ "yamlIncludesDeployStage": true, "pendingBindings": ["构建集群未绑定：请人工确认后回填"],
  "recommendedDeployTargetHostGroupId": 123456,      // ⚠️ 数字 id（拉机器明细用）
  "recommendedDeployTargetUuid": "tIPKOJBbwwOVKVia" } // YAML machineGroup 用
```

- `yamlIncludesDeployStage` 与 `recommendedDeployTarget*` 原由插件从 YAML 常量/配置算出；模板迁到 docs 后**改由助手在结论文件里给出**（依据是 `docs/pipeline-conventions.md` 的"哪些端有独立部署阶段"约定）。插件不再持有"哪端需要部署阶段"的常量。
- `pendingBindings` 同理由助手列出（依据 `docs/pipeline-conventions.md` 的"留空即需人工绑定"清单）。
- `StepReportArtifact.End` 用可空类型（`group` 无端归属）——⚠️ v0.2 同类问题：新增可空枚举字段在源生成下要验证（否则运行期解析异常）。

#### 6.1.2 多端混合结果与逐端完备性闸门（⚠️ v0.2 新增）

初稿只有单值 `status`，三端"一成一败"会被写成 `done`。定案：

- **逐端完备性**：`artifacts[]` 中 `kind=repository` 必须**每端一条**（Step 3 准备/执行）、`kind=pipeline` 必须覆盖"需创建的端"全集（Step 7）；`checks[]` 的名称沿用 `EndBuildResult` 的口径（取端标识），插件按端对齐。**缺端即整步 `failed`**（沿用 Step 5 的"不用其余端兜底"口径）。
- **混合结果表达**：`artifacts[kind=repository].extra.cloned` / `extra.commitId` 逐端如实记录；**任一端失败 → 整步 `failed`**，但已成功落账的条目**照常写进档案**，以便重跑"只补缺失"。
- **`checks[]` 逐端一条**：每端一条 `{name: "<端>", ok, detail, command}`，`detail` 在失败时给出原文摘要。

#### 6.1.3 `needsInput` 往返协议与结论文件时效（⚠️ v0.2 新增）

- **时效**：插件在**每次下发前**记录起始时刻，回读**只接受 `mtime >= 起始时刻` 的结论文件**（文件名带毫秒时间戳，准备与执行各生一份）；历史文件保留在 `results/` 但 UI 标注为"历史轮次"，不参与判定。
- **往返**：`status=needsInput` + `inputs[]` → 面板渲染 → 用户答复 → 插件**组装执行续跑提示词时显式携带**：①用户答复（`answers`，按 `inputs[].id` 对齐）；②准备阶段结论文件**绝对路径**；③"上一阶段已完成什么"的一句话摘要。助手据此只做增量，**不重跑准备阶段**。
- **实现口径（C6′）**：这一步在宿主 API 上确实是一次新的下发（无"轮内提问"工具），但**术语上不叫"第二/两轮"**——它是同一步骤的"执行续跑"。这一点写进技能正文与 README，避免读者误以为"每步只发一次提示词还能等到用户答复"。
- **三选一语义**（`clone-conflict`）：`abort` → 本步结束为 `failed`（文案取用户选择），不推进；`reclone` → **由助手删除目录**（C5′，见 §6.4），再进入执行续跑；`reuse` → 直接执行续跑，指令为"先做健康校验（工作树 + `.git/refs/heads/<pin>`）再继续"。
- **取消/超时**：用户在面板放弃 → 本步回到"未执行"（可重试），不写 `failed`；插件不自动重发。
- **并发**：若项目会话 `WasBusy`（助手正在跑），面板禁用推进按钮并提示"本项目执行会话正在运行"；不允许并发下发。
- **幂等**：执行续跑以"先查后建"为纲；重跑只补缺失（§11）。

#### 6.1.4 独立证据策略（⚠️ v0.2 新增：四环第 4 环的空洞）

事实（§3.2）：Step 3/6/7 的云效与 git **业务失败**都不进 `TurnOutcome.Errors`，第 4 环恒空 ⇒ 判定实际退化为"助手自报"。定案（**不完全解决，如实收窄**）：

- **插件侧新增本地复核器 `StepLocalVerifier`**（纯本地文件系统检查，不触网、不调 git 命令），对结论文件自报的产物做**可独立核对**的项，例如：
  - Step 3：`localPath` 目录存在且非空、`<localPath>/.git/HEAD` 存在且指向有效 ref、`<localPath>/.git/refs/heads/<pin>`（或 packed-refs）存在、**占位符残余扫描 = 0**（插件可读文件，这一项是真正的独立证据）；
  - Step 6：`<localPath>/.git/refs/remotes/origin/<pushBranch>` 存在且 commit id 与自报一致；
  - Step 7：**没有可独立核对的本地证据**（流水线在远端）⇒ 明确记为残余风险。
- **云效侧动作（建组/建库/创建流水线）的远端证据在本设计中不可得**（插件回读结论文件时不触网）⇒ 如实列为残余风险（C12），缓解手段仅两条：①助手把每个 MCP 调用的返回摘要写进 `checks[].detail`；②操作记录原样呈现工具调用与返回，供用户复核。
- ⚠️ 结论：**"Step 3/6/7 的通过判定 = 助手自报 + 本地可核对项"**，不等价于原 C# 直连的确定性闸门。这是 G4 降级的必然代价，本设计不粉饰。

### 6.2 占位符批量替换（原 `PlaceholderReplacer`）

- **规则进 docs**：`docs/org-reference.md`（规则表）+ `docs/step3-repositories.md`（替换语义）。
- **执行方式（替用户拍的决定，请复核）**：助手用 `RunPythonAsync` **现场写脚本**完成"内容 + 文件/目录名"批量替换：跳过 `.git` / `bin` / `obj` / `node_modules`；按 `From` 长度降序替换；区分大小写；**幂等**（替换前统计命中、替换后复查"残余命中 = 0"）；结论文件 `checks[]` 必须含 `placeholder-residual` 且 `ok=true`。
- **本地复核**：`StepLocalVerifier` **独立再扫一遍残余**（不自报即信）——这是 §6.1.4 里最有价值的一项独立证据。
- **备选（若不接受脚本方案）**：把 `PlaceholderReplacer` 留在插件里，作为 Step 3 执行续跑里唯一的"本地机械动作"，由插件在助手克隆成功后自动执行（不进助手链路）。两种方案对 UI 与档案无差异，仅影响"插件还剩多少 C# 逻辑"。

### 6.3 交互项（`inputs[]`）与"三选一 / 部署目标"

```jsonc
"inputs": [
  {
    "id": "clone-conflict",
    "kind": "choice",                       // choice | select
    "prompt": "目标目录非空，如何处理？",
    "ends": ["Service"],
    "options": [
      { "value": "reuse",   "label": "复用已克隆（健康校验通过才算就绪）" },
      { "value": "reclone", "label": "清空目录后重新克隆" },
      { "value": "abort",   "label": "中止本步" }
    ],
    "details": { "path": "D:\\work\\csxmy\\csxmy-Service", "entryCount": 42 }
  },
  {
    "id": "deploy-target",
    "kind": "select",
    "prompt": "选择部署目标（必须选，创建时写入 deploy 段）",
    "ends": ["Service"],
    "options": [ { "value": "123456",            // ⚠️ v0.2：value = 数字 hostGroupId（拉明细用）
                   "uuid": "tIPKOJBbwwOVKVia",   // YAML machineGroup 用
                   "label": "移动云DEV主机组（1 台）" } ],
    "details": { "defaultValue": "123456", "required": true }
  }
]
```

- ⚠️ v0.2：**必须同时带数字 id 与 uuid**——`flow_get_host_group` 只吃数字 id（传 uuid 会 400），而 YAML `machineGroup` 写的是 uuid。
- 复用现有 `CloneConflictResolver` 的形状（`Func<CloneConflict, Task<CloneConflictChoice>>`）：面板读到 `inputs[]` 后弹内联选择，答复拼进执行续跑提示词。
- **聊天路径（无面板）——C15′ 定案**：**在对话里选**。Step 7 准备阶段助手把主机组候选（名称 / 机器数 / 数字 id / uuid）**直接列在对话回复里**，用户回一句即可，助手在**同一会话继续**创建；`inputs[]` 仍照写（面板路径复用同一份契约），但聊天路径不依赖 `details.defaultValue` 兜底、也不降级为"手填 uuid 而跳过校验"。克隆冲突三选一在聊天路径同样由用户对话回复决定（`reclone` 时助手执行删除，C5′）。
  - 若用户既不选也不回复 → 本步停在 `needsInput`（可重试），**不发创建请求**。

### 6.4 本地目录清理（C5′ 定案：由助手执行）

"清空后重克隆"需要目录级删除动作。**定案：由助手执行**，插件不再介入。

- **策略改动**：`AssistantToolPolicy` 把 `DeleteDirectoryAsync` 从 `DeleteTools`（无条件拒绝，`AssistantToolPolicy.cs:92/143`）挪到"工作区内按路径放行"的口径（与 `WorkspaceWriteTools` 同判法，用 `IsUnderWorkspace`）；**越界 / 无 `path` 参数仍拒**。`DeleteFileAsync` / `DeletePageAsync` / 技能写删 / MCP 写删维持无条件拒绝。
- **⚠️ v0.3 新发现的执行风险（请你确认，见 §18.2 的 D1）**：框架 `FileSystemToolPlugin.DeleteDirectoryAsync` 是**裸 `Directory.Delete(recursive: true)`**（`:807/822`），**不处理 Windows 只读位**；而克隆出来的仓库里 `git` 的 pack 文件正是只读的（RC5 根因）。⇒ 直接 `DeleteDirectoryAsync` 有**大概率失败**（`UnauthorizedAccessException`）。
  - **备选 A（推荐）**：助手改用 `RunPythonAsync` 现场脚本 `shutil.rmtree(path, onerror=清只读后重试)`（`python` 已在 `AllowedExecutables` 白名单，纯助手侧、不改框架）。
  - **备选 B**：仍用 `DeleteDirectoryAsync`（策略放行），但要求助手在失败时按契约定向重试（概率性通过，不推荐）。
  - **备选 C**：改框架 `DeleteDirectoryAsync` 补只读处理——**超出"不改框架"口径**，需重新发包。
  - 本文档 §6.4 与 §8.6 按**备选 A** 写；若你选 B/C 我再改。
- 插件侧 `DirectoryCleaner` **保留**（中止清理仍在用），但**不再**用于本步。

### 6.5 组织专有值的唯一副本原则

同一事实**只允许在 `docs/` 里存在一份**。技能正文不得复述端口/Harbor/服务连接/uuid 等值（只写"读 `docs/org-reference.md` 的哪一节"）。这样"改一处生效"，也避免出现"技能与 docs 打架"。
配套单测：对 `skills/*.md` 做**禁用值扫描**（出现 `harbor.tjingcai.com` / `r3x6es4esgbsn6ur` / `tejingcai` 等字样即失败），把"唯一副本"从约定变成可执行检查。

### 6.6 Step 3 重跑联动与档案失效（⚠️ v0.2 新增；C7′ 调整失效对象）

PRD §6 #18 与 §4.8 已有两条既有裁决，本次下沉后**仍必须保留**（否则重跑会留下误导性旧结论）：

- Step 3 执行续跑成功后 → 作废 **Step 5 编译结论**与 **Step 7 流水线结论**：
  - `InvalidateBuild` → **删除（作废）最近一份 Step 5 结论文件** + 清档案 `BuildResults`（C7′：不再维护 `StepStates["Build"]` 快照键）；
  - `InvalidatePipelineConclusion` → **删除（作废）最近一份 Step 7 结论文件** + 清档案 `PipelineResults` + 清流水线类远端产物记录；
- 理由：流水线 YAML 写死代码源地址与分支，仓库改名/重建后旧结论是误导；**只清结论，不删云效上的流水线本体**（远端产物一律不自动删除）；
- 同时**清空 Step 3/5/6/7 的操作记录条目**（避免"上一阶段的工具调用"与"本阶段"混在一条时间线上）；
- v0.1 只写了"准备/执行流程"，未接这两条既有联动 ⇒ 本次补入实现清单（§17）。

### 6.7 闸门判定源改为结论文件（C7′ 定案）

- **保留**：`CanPush`（Step 5 → Step 6）与 `CanCreatePipelines`（Step 6 → Step 7）两条阻断——真机复盘过"Step 5 从未执行、Step 6 却推送成功"的自相矛盾状态，闸门不能丢。
- **改动**：判定源从"档案终态快照 `StepStates[step]` + 逐端结果"改为**读对应步骤的最近一份结论文件**：
  - `CanPush`：`results/step5-*.json`（取 `mtime` 最新一份，且晚于该步骤最近一次起始时刻）存在 ∧ 逐端 `BuildResults` 全 `Ok` ∧ 覆盖 `spec.Ends` 全集；
  - `CanCreatePipelines`：同口径读 `results/step6-*.json` 的逐端推送结果；
- `ProjectSpec.StepStates` **降级为"仅界面进度显示"**（仍由操作记录回放写入，`NewProjectService.cs:2468`），**不再作闸门依据**；这样也顺带消掉 README 记录的"落库顺序 P0（闸门读不到 `StepStates["Build"]` 恒判否）"那条脆弱性。
- `StepReportEvaluator` 的 step 4/5/6/7 判定入口可直接复用（`EvaluateProbe` / `EvaluateBuild` 不动，新增 `EvaluatePush`(6)），闸门读的即是判定结论，避免"两处口径各自漂移"。

---

## 7. MCP 工具面调整（`LubanAgentCore`，产品侧）

**净变化：删 2 补 3，12 → 13。**

### 7.1 新增 3 个（映射到**已存在**的 `CodeupClient` 方法，入参以**实际签名为准**）

| 新工具名 | 映射方法 | 关键入参（实际） | 关键返回 |
|----------|----------|------------------|----------|
| `codeup_list_template_repos` | `ListTemplateRepositoriesAsync` | `templateType`（默认 1）、`page`、`perPage`（默认 100）、`organizationId`。**没有 `search`**——工具说明必须写清"本接口不支持按名过滤，请全量拉取后按 `name` 匹配" | 模板库条目（id / name / path / 默认分支） |
| `codeup_get_repo` | `GetRepositoryAsync` | **`repositoryId`（必填）**、`organizationId`。**没有"命名空间 + 路径"重载** | `httpUrlToRepo` / `sshUrlToRepo` / `defaultBranch` / `id` / `name` / `path` / `visibility`（**无 `webUrl`**——工具说明须写明"页面地址由克隆地址推导"，或工具侧直接返回推导好的 `webUrl`） |
| `codeup_list_groups` | `ListNamespacesAsync` | `search`、`parentId`、`page`、`perPage` | 代码组（id / name / path / webUrl / parentId） |

### 7.2 删除 2 个（C8′）

| 工具 | 连带删除 |
|------|----------|
| `codeup_clone` / `codeup_push` | `YunxiaoToolDefinitions.cs` 的两条定义；`YunxiaoMCPClient.cs` 的分派（`:103/105`）、`CodeupCloneAsync` / `CodeupPushAsync` 实现与参数解析；`CodeupClient.CloneToLocalAsync` / `PushAsync` / 私有 `ToGitCommandResult`；`GitCommandResult.cs`（已无消费者）；引用它们的技能正文、README、PRD §4.11.3、单测 |

### 7.3 其它同步改动点

- `YunxiaoToolDefinitions.cs`：新增 3 个 `MCPTool` 定义（说明文字要写清"模板库 ≠ 普通仓库"、"`codeup_search_repos` 不含克隆地址"、"`codeup_get_repo` 只吃 id 且无 webUrl"这类坑，让助手不必试错）；现有 `codeup_create_group` 的说明"先查后建由向导后端负责"需改写为"先查后建由调用方（助手）负责"；
- ⚠️ v0.2：新增工具后 **`codeup_get_repo` 的返回必须带 `httpUrlToRepo`**（否则 §5.2 执行续跑第 3 步无解）；建议投影里**额外给出 `webUrl`**（由 `httpUrlToRepo` 推导），避免助手自己拼；
- `LubanAgentCore` 的云效连接器 README / `luban-agent/docs/CONNECTOR-SMOKE-TEST.md` 同步；
- `LubanAgent.Tests/YunxiaoToolDefinitionsUnitTest.cs`：工具数断言 12 → 13、删 `codeup_clone` / `codeup_push` 两行 + 3 个新工具的 schema 断言；
- 不新增权限项（沿用现有凭据与网络许可）。

> 不新增"复制流水线"工具（云效无此 API，PRD v0.30 已实证）。

---

## 8. 工具白名单扩展（`AssistantToolPolicy`）

**先纠正一个与直觉相反的机制事实**（读框架代码得到，决定了本节的做法）：

| 工具类别 | 确认回调是否触发 | 现状结果 | 需要做什么 |
|----------|------------------|----------|------------|
| **MCP 工具**（`mcp_yunxiao_*`） | **必触发**：`EvaluateAsync(name, path: null, args)`（`MCPToolPlugin.cs:135/178/250`），`Default` 模式下 `path = null` ⇒ `IsAuthorizedPath(null) = false` ⇒ `AskUserAsync` ⇒ 插件策略 | **全部 `Deny`**（不在白名单） | **必须新增"MCP 子策略"**，否则 Step 3/6/7 一步都走不动 |
| **git 工具**（`Git*Async`） | **不触发**（只要目标在工作区内）：确认路径 = `workingDirectory` 或会话 `EffectiveRoot`（=`spec.LocalPath`），落在工作区 ⇒ `IsAuthorizedPath` 命中 ⇒ 直接放行 | 已可正常使用 | 白名单**无需**改动；越界时才回调，被拒即是预期 |

### 8.1 必须新增：云效 MCP 子策略

**匹配方式（⚠️ v0.2 修正）**：`MCPToolPlugin` 把工具名拼成 `mcp_{client}_{tool}`（`MCPToolPlugin.cs:113`）。**不得**逐字锁死 `mcp_yunxiao_xxx`——连接器实例改名（`ConnectorRegistry` 里 `yunxiao` 可被改名/多实例）会让整张白名单**全线失效**。做法：先剥 `mcp_` 前缀得到 `{client}_{tool}`，再按**工具名段**（`{tool}`）匹配下表；`{client}` 只要求"是本插件声明的云效连接器实例"，不要求等于 `yunxiao`。

按**本项目实际需要**逐项列举（表驱动 + 单测锁死；未列出的 MCP 工具继续拒绝）：

| 放行 | 工具（按工具名段） | 放行依据（写进操作记录） |
|------|------|--------------------------|
| 只读 | `codeup_search_repos`、`codeup_list_template_repos`、`codeup_get_repo`、`codeup_list_groups`、`codeup_list_branches`、`flow_list_pipelines`、`flow_list_host_groups`、`flow_get_host_group` | 查证类，无副作用 |
| 写入 | `codeup_create_group`、`codeup_create_repo`、`flow_create_pipeline`、`flow_update_pipeline` | Step 3 / 7 的执行动作 |
| **拒绝** | `flow_run_pipeline` | 建完**不自动运行**（PRD 口径），本项目链路不需要 |
| **拒绝（双保险）** | `codeup_clone`、`codeup_push` | 两条工具本次已**删除**（§7.2）；策略表仍保留 `Deny`，万一连接器被改回也不放行 |

> 放行合计 **12** 个（8 只读 + 4 写入）；工具面总数 13，唯一被拒的是 `flow_run_pipeline`。
> 轮次约束（"只读准备阶段不得建资源"）**不**做进白名单——那等于把闸门又搬回能力层。按 G4 口径：轮次约束写在技能正文，由结论文件 `artifacts[]` 判定（准备阶段的结论文件若出现"新建"类产物即为不通过）。

### 8.2 git 工具组：不需要动白名单（如实记录）

- 工作区内的 git 动作**框架直接放行**，不经过插件策略。
- 越界 git（`workingDirectory` 指向工作区外）会回调并被拒——这正是需要的行为，保留即可。
- 策略里**不必**再列 git 工具名；若列出也只是"命中不会到达"的死代码，反而误导。

### 8.3 无法在策略层实现的事（如实记录为残余风险）——⚠️ v0.2 修正

v0.1 写"漏洞只在 shell 通道"，**这是错的**：

- **git 工具组自带破坏性动作，且在工作区内同样免回调**：
  - `GitResetAsync(mode="hard")`（`GitToolPlugin.cs:689`）、`GitCleanAsync(force=true, directories=true)`（`:730`）都走同一条 `RunGitAsync` → `pathForConfirm` 落在工作区 → `IsAuthorizedPath` 命中 → **直接放行、不触发回调**；
  - 因此"助手不做破坏性 git 操作"是**约定**，机制上拦不住。
- `AllowedExecutables` 含 `git`（`AssistantToolPolicy.cs:63`），而 shell 策略**只看命令首词、不看参数**（`IsAllowedShellCommand`，`:215`）⇒ `RunShellAsync("git push --force")` / `git reset --hard` / `git clean -fd` 同样**拦不住**。
- `GitPushAsync` 本身**没有** force 参数（`:671`，且 `RejectOptionLike` 拒绝 `-` 开头取值），所以"用工具推 force"做不到。
- 缓解（非强制）：技能正文写"不得使用 `--force` / `reset --hard` / `clean -fd`"；操作记录把工具调用原文**原样**呈现给用户复核。若将来要求硬拦，需要**框架侧**给 shell 与 git 工具加"参数黑名单"能力——**超出本次范围**（C14）。
- `mcp_list_resources` / `mcp_read_resource`：本项目不用，维持拒绝。

### 8.4 已评估但不采用：`AutoConfirmTools` 轻路线（⚠️ v0.2 新增）

框架既有 `ToolConfirmationService.AutoConfirmTools`（配置 `LuBanAgent:Confirmation:AutoConfirmTools`）是"按工具名免确认"清单，走 `EvaluateAsync` 早退（`:701`），比写插件策略更轻。**不采用**，理由：

1. 它是**全局宿主配置**，对**所有会话 / 所有插件**生效 —— 无法收窄到"本项目、本插件"；
2. 它只表达"免确认"，**不表达"拒绝"** —— 无法拒绝 `flow_run_pipeline`（免确认 ≠ 拒绝，拒绝仍需策略 `Deny`）；
3. 与 §10 的"操作记录里写明放行/拒绝依据"不匹配（无依据可写）。

### 8.5 单测要求

- 表驱动：上表每个工具名段 → 期望 `Allow` / `Deny`（含拒绝项与"未列出的 MCP 名"）；
- **实例改名不回归**：client 名换成别的（如 `mcp_yunxiao2_codeup_get_repo`）仍按工具名段判定；
- 工作区内 git 工具路径**不**经策略（通过 `IsAuthorizedPath` 行为验证，或用既有框架测试路径覆盖）；
- 未知工具、`RunLuaAsync` 仍拒绝（现状不回归）。

### 8.6 删除类工具的口径调整（C5′）

- `DeleteDirectoryAsync`：从 `DeleteTools` 的**无条件拒绝**改为**工作区内按路径放行**（与 `WorkspaceWriteTools` 同判法：`IsUnderWorkspace(path, workspaceRoot)`；越界 / 无 `path` → 拒）。建议在策略里**单列一个 `WorkspaceDeleteTools`**（而不是塞进 `WorkspaceWriteTools`），避免把"删除"混进"写入"的语义与文案里。
- `DeleteFileAsync` / `DeletePageAsync` / `WriteSkillAsync` / `DeleteSkillAsync` / `WriteMcpServerAsync` / `DeleteMcpServerAsync`：**维持无条件拒绝**。
- **⚠️ 实现前置（D1）**：框架 `DeleteDirectoryAsync` 不处理只读位（§3.1/§6.4）；按推荐备选 A，助手实际用 `RunPythonAsync` 的 `rmtree(onerror=清只读)` 删除，`DeleteDirectoryAsync` 的放行只是"契约兜底 + 面板文案一致"。

---

## 9. 凭据与安全

| 项 | 口径 |
|----|------|
| 云效 PAT | 用户在 codex「设置 → 云效连接」配置；连接器注入 MCP；**不进**工具入参、不进提示词、不落结论文件 |
| git 凭据 | **用户外部配置**（credential helper / 系统凭据管理器 / 预置 remote）；插件与技能不提供通路，也不在 URL 里拼 `oauth2:<PAT>@` |
| 失败呈现 | git 原文摘要 + 固定提示"请先在外部环境配好凭据"；`docs/step6-push.md` 给一张"报错 → 处置"对照表 |
| SQLite 落库 | 会话全文落 `luban-ai-agent.db`（按 `WorkspaceId` 归属）；因凭据外置，**不再有 PAT 进库**（相对现状是净改善——现状 Step 4 口令按方案 A 明文内联，属已知残余风险，本次不动） |
| 结论文件 | 不得含口令 / token；`artifacts[].cloneUrl` 只放**不带凭据**的 `httpUrlToRepo`；新增单测：结论文件文本**禁止出现 `oauth2:` 与 `@codeup` 之外的凭据形态** |
| ⚠️ C8′ 的净收益 | 删掉 `codeup_clone` / `codeup_push` 后，**"拼 `oauth2:<PAT>@` URL"的通路从代码里消失**（该拼法原在 `CodeupClient.CloneToLocalAsync` / `PushAsync` 内），凭据外置不再只是"约定" |

---

## 10. UI 与操作记录

- **面板结构不变**：左侧档案与步骤区、右侧操作记录时间线（`OperationRecord`）、Step 3 预检确认界面、Step 7 YAML 复核与部署目标下拉。
- **数据来源改变（C11′）**：预检确认界面与 Step 7 复核界面的视图模型（`RepositoryPrecheck` / `EndPrecheck` / `PipelinePreview`）**从结论文件 `artifacts[]` 映射**（§6.1.1）；面板渲染代码的改造量与"两个专用段"方案相当，但契约只有一个。
- **新增交互来源**：`inputs[]`（三选一、部署目标选择）由面板渲染；用户答复写进操作记录，并随执行续跑提示词下发。
- **操作记录内容变化**：现在是"C# 调了哪个连接器方法"，将来是"助手调了哪个工具、审批结论（放行/拒绝依据）、失败原文"——`TurnOutcome` 已有 `ToolCalls` / `Approvals` / `Denials` / `Errors`，直接可渲染（Step 4/5 已在用）。
- **新增入口**：面板提供"打开本项目执行会话"，把对话记录可达（回应"项目工作区里能查到每个对话记录"——对话全文在会话/SQLite 里，工作区里可查的是 `results/` 结论文件 + `docs/` + `spec.json` 档案；面板加跳转把两者接起来）。
- **进度推进**：`ProjectSpec` 字段不变（`Repositories[]` / `RemoteArtifacts[]` / `PipelineResults[]` / `BuildResults[]`），只是填充者从"C# 直连结果"改为"结论文件 `artifacts[]` + 本地复核"。
- **聊天路径（无面板）——C15′ 定案（不再有"不可保证完成"）**：
  - Step 7 准备阶段助手把**主机组候选清单直接列在对话里**（名称 / 机器数 / 数字 id / uuid），用户在**对话里回一句**；助手在**同一会话继续**创建。`inputs[]` 契约照写（面板路径复用同一份），但聊天路径**不依赖** `details.defaultValue` 兜底、也**不**降级为"手填 uuid 跳过校验"（PRD v0.32 裁决 ③ 仍成立）。
  - 克隆冲突三选一同样在对话里回复；`reclone` 时助手执行目录删除（C5′）。
  - 用户不回复 → 停在本步 `needsInput`（可重试），不发创建请求。
  - PRD 口径随之从"聊天路径 Step 7 不可保证完成"改写为"由用户在对话里选择部署目标，助手续跑"（§17.1）。

---

## 11. 失败与重试（全部降级为约定 + 结论文件）

| 场景 | 约定（技能/docs） | 结论文件 |
|------|-------------------|----------|
| 云效 401 / 令牌失效 | 不重试，提示重配 PAT | `failed` + 原文 |
| 云效 403 | 不重试（无权限，硬闯无用） | `failed` |
| 云效 429 / 限流 | 退避重试（⚠️ 线性 10/20/30s，最多 3 次），仍失败即失败 | 重试过程记 `checks[]` |
| pin 登记值不存在 / 模板仓无可用分支 | **阻断**，不猜默认分支 | `failed` |
| 建库后 pin 分支 120s 未出现 | **阻断**，不盲目 clone | `failed` |
| git 凭据缺失 / 认证失败 | 提示外部配置凭据 | `failed` + 固定提示 |
| git 克隆残骸（`.git/HEAD = ref: refs/heads/.invalid`） | 不自行删；写 `needsInput` 或 `failed`，由用户选清理（⚠️ 反转 PRD v0.19 的"必须清理"，见 §18） | `needsInput` / `failed` |
| 目标目录非空 | 三选一；选 `reclone` 后**由助手删除**（C5′，注意只读位 D1） | `needsInput` |
| 仓库名已存在 | 复用（幂等） | `done` + `checks` 注明"复用" |
| 流水线缺部署目标 | 不发请求即失败 | `failed` |
| 面板微调的 YAML 不合法 | 面板**不校验**（C10′），由云效报回 | `failed` + 云效原文 |

**幂等总原则**（技能正文）：所有"建"动作先查后建；所有产物即时写进结论文件（失败重跑只补缺失）；不做静默覆盖。

---

## 12. 验收方式

1. **构建**：`dotnet build luban-agent/LubanAgent.slnx`（含插件、`LubanAgentCore`、`LubanAgent.Tests`）——**0 错误 0 警告**。
2. **既有资产的处置**（⚠️ v0.2 新增；不处理则构建直接失败）：
   - 删除 `NewProjectPushRetryUnitTest.cs`（整类为已删类型而写）；
   - 删除 `NewProjectPipelineTemplateUnitTest.cs`（30+ 处引用已删类型）→ 新增"`docs/` 模板文件存在性与结构"测试（三端 `pipeline-*.yml.md` 齐备且含 `stages:`、`docs/README.md` 索引覆盖全部文件、`skills/*.md` 禁用值扫描）；
   - 改 `YunxiaoToolDefinitionsUnitTest.cs`：工具数 **12 → 13**、删 `codeup_clone`/`codeup_push` 两行、补 3 个新工具 schema 断言；
   - 重写 `NewProjectStepGateUnitTest.cs`：判据改为"读 Step 5 / Step 6 结论文件"（C7′）；
   - `NewProjectAssistantToolPolicyUnitTest.cs`：补 `DeleteDirectoryAsync` 工作区内放行 / 越界拒绝用例（C5′）；
   - 其余既有单测（`ValidateBasicInfoAsync` / `ValidateRepositoryPlanAsync` / `NewProjectStepReportUnitTest` 等）必须继续绿。
3. **单测（新增）**：
   - `StepReport` 扩展 schema 的解析（含 `status` / `inputs` / `artifacts` + 三个 `extra` 具名类型）与**三个新判定入口**（`EvaluateRepositorySetup` / `EvaluatePush` / `EvaluatePipeline`），覆盖 `done` / `needsInput` / `failed`；
   - **`artifacts[]` → 视图模型映射**（§6.1.1 逐字段；缺端 / 缺字段 → 明确报错而非静默空白）；
   - **逐端完备性**：缺端 → 判失败；混合结果 → `failed` 但成功项仍落账；
   - **`StepLocalVerifier`**：目录/`.git`/refs 缺失 → 判失败；占位符残余 → 判失败（用临时目录构造桩，不真连云效）；
   - `AssistantToolPolicy`：表驱动放行/拒绝（含 12 个放行 + `flow_run_pipeline` + 已删除两条的 `Deny`）+ **实例改名不回归** + 工作区内 git 绕过策略 + 工作区内删除放行；
   - 新 MCP 工具（3 个）的参数映射与响应投影（桩 HTTP/桩 client），含"`codeup_get_repo` 返回含 `httpUrlToRepo`"；**被删两条不再出现在工具面**（断言）；
   - `skills/*.md` 的**禁用值扫描**（§6.5）；`docs/` 三端模板结构；
   - 结论文件**凭据形态**禁令（§9）。
4. **手工冒烟（实现者可自证的部分）**：把 `LubanAgent.Plugin.NewProject/README.md` 现有清单改写成"助手执行"版，并新增可在本机自证的条目：
   - MCP 子策略表驱动行为（放行/拒绝项、未列出名继续拒绝）；
   - `needsInput` 三选一在面板上的往返（**用桩助手**注入结论文件，不必真连云效）；
   - `artifacts[]` 驱动面板渲染（桩）；
   - 部署目标缺选时的阻断（桩）；
   - 结论文件 `done` / `needsInput` / `failed` 三种状态的面板呈现与历史轮次标注；
   - 助手的"清空后重克隆"（含只读位处理，D1）在本地临时目录上可真跑。
5. **真机全流程（必须由用户环境执行，实现者跑不了）**：三端（Service / Admin / Mobile）从 Step 1 走到 Step 7。**前置**：真实云效组织、模板库条目可见（§16-4）、已配好的 git 凭据（§16-3）、主机组与流水线权限。产出对照：目录齐备、`results/step3|6|7-*.json` 齐备且 `done`、云效侧代码组/仓库/流水线与 v0.32 口径既有记录等价，并额外验证：未配 git 凭据时的克隆/推送失败文案（应出现"外部配置凭据"提示且**不含 PAT**）；401 / 429 的文案与重试行为；**Step 3 重跑后 Step 5/7 结论确已被作废**（§6.6）；**聊天路径**在对话里选部署目标后能走完 Step 7（C15′）。

   > 分工口径：**1–4 属"实现完成"的必备证据；5 属"交付验收"的用户侧动作**。设计文档与 PRD 的"完成"定义以前 4 项为准，第 5 项单独列为待办。
6. **回归**：Step 4 / 5 行为与判据不变（现有冒烟条目重跑；`EvaluateProbe` / `EvaluateBuild` 实现不改）。

---

## 13. 留给构建者的部分（本设计不指定）

- 每步提示词的**具体措辞与长度**（插件构造，可引用 docs 与技能正文；只要满足 §6 的契约）。
- 结论文件 schema 在 C# 中的**具体类型拆分**（`StepReport` 内嵌 vs 并列子类型）——只要源生成序列化可解析、Step 4/5 判定不被破坏、三个 `extra` 具名类型的字段集合与 §6.1.1 一致。
- `docs/*.md` 的**具体行文与排版**（内容范围由 §4 界定）。
- 三端 YAML 的**渲染策略**（助手直接写出完整 YAML，还是写"模板 + 填值说明"再拼）——只要最终提交给云效的是合法新版 YAML。
- 面板上 `inputs[]` 的**控件形态**（内联按钮组 / 下拉 / 弹窗）。
- 提交信息文案、目录清理的确认文案。
- 单测的具体组织方式与桩的构造。
- PRD（`docs/PRD-插件体系与新建项目插件.md`）升版到 v0.33 与 README（插件）同步更新的写法（回写清单见 §17）。

---

## 14. 替用户拍的决定清单（**已逐条复核**）

### 14.1 用户复核**保留**（11 条）

| # | 决定 | 若不接受会怎样 |
|---|------|----------------|
| C1 | `docs/` 不注册成技能，改为 **Step 2 由插件复制到 `{workspace}\.luban-agent\plugin-newproject\docs\`**，提示词/技能正文给绝对路径 | 若改成"每份知识 = 一个技能"，则技能列表会被参考资料塞满，且 YAML 长文受 256KB/单文件与 frontmatter 约束 |
| C2 | 三端 YAML 模板文件名用 **`pipeline-<end>.yml.md`**（Markdown 包裹） | 若坚持纯 `.yml`，需确认 csproj 打包与阅读方式，其余设计不变 |
| C3 | `newproject.settings.json` **保留 6 项格式/兜底值**（命名格式、推送分支、可见性、提交身份），其余组织专有值全进 docs | 若全进 docs，则建单表单需先跑一轮助手取格式值（多一轮、离线不可用） |
| C4 | 占位符替换改由助手 **`RunPythonAsync` 现场脚本**完成（要求残余命中 = 0） | 备选：`PlaceholderReplacer` 留在插件作为 Step 3 唯一的本地机械动作 |
| C9 | 新增"MCP 子策略"：放行 12 个本项目所需 `mcp_yunxiao_*`（8 只读 + 4 写入），**拒绝** `flow_run_pipeline`（并对已删除的 `codeup_clone` / `codeup_push` 保留 `Deny` 作双保险）；按**工具名段**匹配（连接器改名不失效）；"只读阶段不得建资源"不进策略层（按 G4 靠约定 + 结论文件） | 不加则 Step 3/6/7 **一步都跑不通**（§3.2 硬阻塞）；若要"按轮次参数化白名单"，多一层机制但要按轮下发 |
| C12 | 通过判定 = **结论文件四环 + 新增的本地复核器 `StepLocalVerifier`**；**明确承认云效侧动作的远端证据不可得**（不回读时不触网），列为残余风险 | 若要求"必须有独立远端证据"，则插件回读时必须再调云效（`flow_list_pipelines` 等）做二次核对——会引入"回读阶段触网 + 需要 PAT 通路"，且与"插件只做本地判定"口径冲突 |
| C13 | Step 3/6/7 的判定强度**弱于**原 C# 直连闸门（助手自报 + 本地可核对项）；PRD 中"确定性闸门"相关表述需同步改写 | 若不作此声明，等于把降级说成等价，后续复盘会误判 |
| C14 | 破坏性 git 动作（`--force` / `reset --hard` / `clean -fd`）**只在技能正文与操作记录层面约束**，不在机制层硬拦（需改框架，超出本次范围） | 若要求硬拦：需框架侧给 shell 策略与 git 工具加参数黑名单，构成"改 `LuBan.*`"的额外工作 |
| C16 | Step 3 执行成功后**必须**触发 `InvalidateBuild` / `InvalidatePipelineConclusion` 并清操作记录（PRD v0.30 既有裁决，本次下沉后仍保留；失效对象按 C7′ 改为"作废结论文件"） | 若不做，重跑后会留下误导性的旧 Step 5/7 结论 |
| C17 | **反转 PRD v0.19 的"克隆失败必须清理本次新建目录"**：改为不自动删、由用户在 UI 选择（`needsInput`） | 若保留自动删，则插件会在助手刚失败后立即删目录，用户看不到现场（与"失败原文可复核"口径冲突） |
| C18 | 新增 `StepReportArtifact` / `GroupArtifactExtra` / `RepositoryArtifactExtra` / `PipelineArtifactExtra` / `StepReportInput` 等类型**每个都必须登记 `PluginJsonContext`**；`PushRetryPolicy` / `PlaceholderReplacer` / `PipelineTemplates` / `GitCommandResult` 与 4 个单测类**连带删除或重写**，属实现清单硬项 | 若不处理，运行期 `NotSupportedException` 或构建直接失败 |

### 14.2 用户**否决**（7 条）及其替代方案（已定案）

| # | 原决定（被否决） | 替代方案（定案） |
|---|------------------|------------------|
| C5 | 目录删除由插件执行、助手侧拒绝删除类工具 | **C5′：由助手删除**——`AssistantToolPolicy` 把 `DeleteDirectoryAsync` 从无条件拒绝改为**工作区内按路径放行**（`IsUnderWorkspace`，越界即拒，建议单列 `WorkspaceDeleteTools`）。⚠️ 执行机制见 §6.4 的 D1（框架工具不处理只读位） |
| C6 | Step 3/7 各拆"两轮" | **C6′：做法不变、只改叫法**——术语统一为"**一次只读准备下发 + 用户确认后同会话执行续跑**"，全文不再称"两轮"；实现上仍是"答复后插件再发一次提示词"（框架无轮内提问工具），这一点写进技能正文与 README |
| C7 | `StepGate` 保留为本地档案**快照**判定 | **C7′：闸门判定源改吃结论文件**——"编译未过不能推送"的阻断保留，但判据从 `StepStates["Build"]` 改为**最近一份 Step 5 结论文件**的逐端 `BuildResults`（`CanCreatePipelines` 同口径读 Step 6 结论文件）；`StepStates` 降级为"仅界面进度"；`InvalidateBuild` 改为作废结论文件（§6.7） |
| C8 | `codeup_clone` / `codeup_push` 与 `CodeupClient.CloneToLocalAsync` / `PushAsync` 保留不删 | **C8′：删到位**——删 MCP 两条工具（定义 + 分派 + 实现）、删 `CloneToLocalAsync` / `PushAsync` / 私有 `ToGitCommandResult`、删 `GitCommandResult.cs`（`LubanAgentCore` 内已无消费者）；连带改 `skills/newproject-push-repo.md:38`、插件 README、`YunxiaoToolDefinitionsUnitTest.cs`、PRD §4.11.3 表与注①。**净收益**：代码里不再有"拼 `oauth2:<PAT>@`"的通路（§9） |
| C10 | `PipelineTemplates.cs` 不整删、保留 `PipelineYamlOverride` 作面板即时校验 | **C10′：整删 `PipelineTemplates.cs`**（含 `PipelineYamlOverride`），并**去掉面板即时校验**——`PipelineYamlEditorWindow.cs:174` 的 `Validate` 调用删除，"名一致 / 无 Tab / 顶层 `stages:`"一律交给云效报回；面板"手工微调"保留，微调文本经 `answers` 进执行续跑；`NewProjectPipelineTemplateUnitTest.cs` 整类删除 |
| C11 | 结论文件新增 `precheck` / `preview` 两个专用段 | **C11′：不新增段，面板改吃通用 `artifacts[]`**——`extra` 按 `kind` 用**具名强类型**（`GroupArtifactExtra` / `RepositoryArtifactExtra` / `PipelineArtifactExtra`），逐字段映射表见 §6.1.1 |
| C15 | 聊天路径（无面板）的 Step 7 定性为"可引导但不可保证完成" | **C15′：在对话里选**——助手把主机组候选（名称/机器数/数字 id/uuid）列在对话回复里，用户回一句，助手同会话继续创建；不用 `defaultValue` 兜底、不降级手填 uuid（PRD v0.32 裁决 ③ 仍成立）（§6.3 / §10） |

### 14.3 待你确认的执行细节（v0.3 新增）

| # | 事项 | 备选 |
|---|------|------|
| **D1** | "助手删除含 `.git` 的目录"用什么工具（框架 `DeleteDirectoryAsync` 不处理 Windows 只读位，直接删大概率失败） | **A（推荐）** 助手用 `RunPythonAsync` 的 `shutil.rmtree(onerror=清只读后重试)`；**B** 仍用 `DeleteDirectoryAsync` 并允许失败重试；**C** 改框架工具（超范围、要重发包）。本文档按 A 写 |

---

## 15. 风险与缓解（如实记录）

| 风险 | 缓解 |
|------|------|
| 助手执行的不确定性与原 C# 闸门的能力差（用户已接受降级） | 结论文件 `checks[]` 强制自证（残余命中、分支就绪、复用判定）+ **`StepLocalVerifier` 本地独立复核**；任一环不满足即 `failed`，不推进 |
| **云效侧动作无可独立核对的证据**（§6.1.4） | 如实记录（C12/C13）；用"每个 MCP 调用的返回摘要进 `checks[].detail`"+操作记录原样呈现兜底 |
| 占位符批量替换交给脚本，可能漏改 | 要求"替换前命中统计 / 替换后残余 = 0 / 幂等"三项证据进结论文件；**插件再独立扫一遍残余** |
| 交互轮次增加（Step 3、Step 7 各有一次准备与一次执行续跑）→ token 与耗时上升 | 准备阶段限定"只读、不建资源"；提示词尽量薄，正文引用 docs；步骤结果可重跑只补缺失 |
| git 凭据外置 → 首次使用必然遇到认证失败 | 把"配置凭据"写进 docs 前置条件与失败文案；冒烟清单专设一条 |
| 技能 / docs 改动需重发插件（无热更新） | 用户已接受；`docs/` 复制为覆盖式，升级即生效于**新建**项目（已建项目保持其工作区快照） |
| **助手删除目录在 Windows 上撞 git 只读位（RC5）** | 见 §6.4 / D1：按备选 A 用 `RunPythonAsync` 的 `rmtree(onerror=清只读)`；`DeleteDirectoryAsync` 的放行仅作契约兜底 |
| 助手可能"顺手"改了不该改的东西 | 工作区内写 + 白名单 shell 维持现状；删除类**仅放行工作区内目录删除**（其余仍拒）；**云效侧**用 §8.1 的表驱动子策略逐项把关；git 工具工作区内自动放行是框架既有行为，本次不改 |
| 助手可用 shell **或 git 工具组**绕过参数级约束（`git push --force` / `reset --hard` / `clean -fd`） | 策略层拦不住（§8.3），只能靠技能正文约定 + 操作记录原样呈现；若将来要硬拦，需框架侧能力，**不在本次范围**（C14） |
| 插件 ALC 约束（不得反射序列化） | schema 扩展继续走 `PluginJsonContext` 源生成；**新类型必须逐个登记**（C18）；`extra` 用具名类型而非自由字典（C11′） |
| 结论文件每次下发文件名不同 → 读到旧轮 | 回读按"本轮起始时刻"过滤（§6.1.3）；UI 标注历史轮次；闸门取 `mtime` 最新一份（C7′） |
| 删除 `NewProjectSettings` 成员 / 删除或重写 4 个单测类 → 构建红 | §17 同步清单逐项列出（C18） |

---

## 16. 前置依赖（开工前必须确认）

1. **`LubanAgentCore` 可改**（删 2 补 3 个 MCP 工具 + 删 `CloneToLocalAsync` / `PushAsync` / `GitCommandResult`）——属产品工程，不动框架；这是本设计的硬前置。
2. **`AssistantToolPolicy` 可改**（插件自身文件：新增 §8.1 的 MCP 子策略 + §8.6 的删除放行）——**不改这里，整条助手链路一步都走不动**，属硬前置。
3. **git 凭据在用户环境可用**（否则真机冒烟无法通过推送环节）。
4. **模板库条目在组织内可见**（`templateType=1`），否则 Step 3 无法定位模板。
5. **D1 的执行机制定案**（§14.3）。

---

## 17. 同步清单（改哪些文件、回写哪些既有裁决）

### 17.1 PRD 回写（`luban-agent/docs/PRD-插件体系与新建项目插件.md` v0.32 → v0.33）

| PRD 位置 | 现状口径 | 本次要改成 |
|----------|----------|-----------|
| 术语表 L66、§3.10 L364、验收 #11 L1007、§4.11.3 引言 L1145 / L1147、M6 L1199 | MCP 工具 **12 个** | **13 个**（删 `codeup_clone` / `codeup_push`、新增 `codeup_list_template_repos` / `codeup_get_repo` / `codeup_list_groups`） |
| §4.11.3 工具表 L1152 / L1154 与**注①** L1164（`codeup_clone` / `codeup_push` 为 Git 操作、用 `oauth2:<PAT>@`） | 两条 git 工具在 MCP 面上 | **删两行 + 改注①**：git 操作（克隆/推送）一律走**框架 git 工具组**，MCP 不再有 git 类工具；"拼 `oauth2:<PAT>@`"的描述删除 |
| §3.11 L1119 / L1121（技能 → MCP 映射含 `mcp_yunxiao_codeup_clone` / `codeup_push`） | 技能编排这两条 | 改为 `git` 工具组（`GitCloneAsync` / `GitPushAsync`） |
| L365（复用框架 `GitToolPlugin` 的说明）、L1194（M1 落地含 `CodeupClient.CloneToLocalAsync` / `PushAsync`）、L1231（§6 #17 分支策略引 `CloneToLocalAsync`） | 依赖连接器的克隆/推送实现 | 改为"克隆/推送由助手调 git 工具组；连接器不再提供 git 方法" |
| L33（v0.29 修订说明：`codeup.PushAsync` + `PushRetryPolicy`）、§6 #30 L1027、§4.11.3 注④ | 限流退避由能力层 `PushRetryPolicy` 实现 | 改为"退避口径写进 `docs/step6-push.md`，由助手执行；`PushRetryPolicy` 删除"（**口径不变**：线性 10/20/30、最多 3 次） |
| §4.3 / §4.8 / §4.9.1 / §4.11.3 / §6 各处"向导路径直连连接器、聊天路径走 MCP" | 两条路径执行方式**分叉** | 统一为**助手驱动**（Step 3/6/7 全部走助手） |
| L585（v0.19 #18-②）"克隆失败**必须清理**本次新建的目录" | 自动清理 | **反转**为不自动清理、由用户选择（C17） |
| L655 §4.8（v0.30）`PreviewPipelines`"**纯本地渲染、不调云效、不落档案**" | 纯本地 | 改为"助手准备阶段产出 `artifacts[kind=pipeline]` + 落结论文件"（不再是"纯本地 C# 渲染"） |
| L1014 验收 #18"确定性闸门"表述（等 pin/健康校验/幂等） | 由 C# 保证 | 改为"技能正文约定 + 结论文件判定 + 本地复核"（C13） |
| §4.7 / §6 #15 L1226（本地自动回收 / 远端不自动删）与闸门口径 | `StepGate` 判据 = `StepStates["Build"]` | **保持**"本地自动回收、远端不自动删"；闸门判据改为**读 Step 5 结论文件**（C7′），并删去"落库顺序 P0（否则闸门读不到 `StepStates['Build']`）"那条说明 |
| Step 7 部署目标（v0.32 裁决 ③）与聊天路径 | "不降级成手填 uuid"；聊天路径不可保证完成 | **保留**"不降级"；把"聊天路径不可保证完成"改写为"**由用户在对话里选择部署目标，助手续跑**"（C15′） |
| 新增条目 | — | 记入"知识文件布局（`skills/` + `docs/` 随包分发）""MCP 子策略（含删除类放行）""结论文件扩展 schema（`artifacts[]` + 具名 `extra`）""Step 3 重跑联动""闸门读结论文件"五项定案 |

### 17.2 其它文件

| 文件 | 改动 |
|------|------|
| `LubanAgent.Plugin.NewProject/LubanAgent.Plugin.NewProject.csproj` | §4.2 三处（`None` 项 / `PluginDocFile` 项 / `StagePluginPackage` 加 `docs` 铺装） |
| `LubanAgent.Plugin.NewProject/plugin.json` | 6 个技能 description 改写为"助手执行"版（技能清单本身不加 docs） |
| `LubanAgent.Plugin.NewProject/newproject.settings.json` | §4.4 瘦身（删组织专有值） |
| `LubanAgent.Plugin.NewProject/Configuration/NewProjectSettings.cs` | 删对应成员（`Templates[]` / `PipelineDefaults[]` / `PipelineReferences[]` / `PlaceholderRules[]` / `GroupParentId`）并改全部调用点（含注释里对 `PipelineRenderer` 的引用） |
| `LubanAgent.Plugin.NewProject/Serialization/PluginJsonContext.cs` | 登记全部新 schema 类型（C18：`StepReportArtifact` + 三个 `extra` + `StepReportInput` 等） |
| `LubanAgent.Plugin.NewProject/Abstractions/StepReport.cs` | 扩 schema（`status` / `inputs` / `artifacts[]`） |
| `LubanAgent.Plugin.NewProject/Services/StepReportEvaluator.cs` | 新增 3 个判定入口（Step 3/6/7）；`EvaluateProbe` / `EvaluateBuild` 不动 |
| `LubanAgent.Plugin.NewProject/Services/StepGate.cs` | 判据改读结论文件（C7′）：`CanPush` / `CanCreatePipelines` / `InvalidateBuild` |
| `LubanAgent.Plugin.NewProject/Services/`（新增）`StepLocalVerifier.cs` | §6.1.4 本地复核器 |
| `LubanAgent.Plugin.NewProject/Services/PipelineTemplates.cs` | **整个文件删除**（C10′） |
| `LubanAgent.Plugin.NewProject/Services/PlaceholderReplacer.cs`、`PushRetryPolicy.cs` | 删除 |
| `LubanAgent.Plugin.NewProject/Services/AssistantToolPolicy.cs` | 新增 MCP 子策略（§8.1）+ `DeleteDirectoryAsync` 工作区内放行（§8.6） |
| `LubanAgent.Plugin.NewProject/Services/NewProjectService.cs` | Step 3/6/7 执行体下沉；接 §6.6 失效联动；面板视图模型改从 `artifacts[]` 映射 |
| `LubanAgent.Plugin.NewProject/Ui/PipelineYamlEditorWindow.cs` | 删 `Validate` 调用（`:174`）与相关注释（`:31`）；只留文本框 + 微调回传 |
| `LubanAgent.Plugin.NewProject/Ui/WizardPanel.cs` | 注释里对 `PipelineRenderer` / `PipelineYamlOverride` 的引用改写（`:1149` / `:1163` / `:3075`）；预检/预览视图模型改从 `artifacts[]` 填 |
| `LubanAgent.Plugin.NewProject/skills/newproject-push-repo.md` | `:38` 的 `mcp_yunxiao_codeup_push` 改走 `GitPushAsync`；`:56` 的退避口径改为 docs 约定（去掉 `PushRetryPolicy` 字样） |
| `LubanAgent.Plugin.NewProject/README.md` | 1–71 条冒烟清单改写为"助手执行"版 + 新增 §12-4 条目 + 说明"docs 只对新建项目生效"；`:220/225/227/243` 的 `CodeupClient` 直连与 `DirectoryCleaner` 段落改写；`:303/309` 的 `StepGate` 判据改写（C7′） |
| `LubanAgentCore/Connectors/AliyunDevOps/CodeupClient.cs` | 删 `CloneToLocalAsync` / `PushAsync` / `ToGitCommandResult`（C8′） |
| `LubanAgentCore/Connectors/AliyunDevOps/GitCommandResult.cs` | 删除（C8′） |
| `LubanAgentCore/Connectors/AliyunDevOps/Mcp/YunxiaoToolDefinitions.cs` / `YunxiaoMCPClient.cs` | 删 2 个 git 类工具 + 补 3 个新工具 + `codeup_create_group` 说明改写 |
| `LubanAgentCore` 云效连接器 README、`luban-agent/docs/CONNECTOR-SMOKE-TEST.md` | 工具 12 → 13（删 2 补 3）冒烟 |
| `LubanAgent.Tests/` | 删 `NewProjectPushRetryUnitTest.cs` / `NewProjectPipelineTemplateUnitTest.cs`；改 `YunxiaoToolDefinitionsUnitTest.cs`（12→13、删两条、补三条）；重写 `NewProjectStepGateUnitTest.cs`（C7′）；补 `NewProjectAssistantToolPolicyUnitTest.cs`（删除放行）；新增 §12-3 各项 |
| `luban-framework` 中英文 README 对 | **不改**（不动框架） |

---

## 18. 审查与定案摘要

### 18.1 v0.1 → v0.2（三路对抗性审查，21 条被推翻的断言）

| # | v0.1 的断言 | 事实 | 处理 |
|---|------------|------|------|
| 1 | `codeup_list_template_repos` 有 `search` 入参 | `ListTemplateRepositoriesAsync` **无** `search`（带 search 的是搜普通仓库的 `SearchRepositoriesAsync`） | §7 表改入参；§5.2 准备第 1 步改"全量拉取 + 客户端按名匹配" |
| 2 | `codeup_get_repo` 入参可为"命名空间 + 路径" | 只有 `GetRepositoryAsync(string repoId)` | §7 表改入参 |
| 3 | 仓库详情返回 `webUrl` | `YunxiaoRepository` **无** `WebUrl`（须由 `BuildWebUrl(httpUrlToRepo)` 推导） | §7 表 + §5.2 准备第 4 步 |
| 4 | 仓库名格式 `{code}-{end}` | 实为 **`{Code}-{end}`** | §4.4 |
| 5 | `DirectoryCleaner` 是插件自带文件 | 在 **`LubanAgentCore/Utils/`**（宿主侧） | §3.1、§6.4 |
| 6 | 429 退避"指数间隔" | **线性** 10/20/30s（原 `PushRetryPolicy.BackoffSeconds`） | §5.3、§11 |
| 7 | `NewProjectService.cs` 约 3100 行 | **2892 行** | §1 |
| 8 | 执行阶段"`httpUrlToRepo` 不必再查" | **新建仓后必须再查一次**（现状 `NewProjectService.cs:949-950` 正是如此）；仅复用仓可免 | §5.2 执行第 3 步 |
| 9 | "扩 schema = 填字段" | `PluginJsonContext` 只登记 3 类型（不登记即 `NotSupportedException`）；`StepReportEvaluator` 硬绑 step 4/5 | §6.1、C18、§5.5 |
| 10 | `StagePluginPackage` 只需加一项 | 需**三处**同改（`None` / `PluginDocFile` / `MakeDir`+`Copy`） | §4.2 |
| 11 | 四环第 4 环可判定 Step 3/6/7 | **恒空**（MCP 失败返回普通字符串、git 业务失败返回 `Fail`，都不发 `ToolCallFailedEvent`） | §6.1.4、C12/C13 |
| 12 | 部署目标选项值用 uuid 即可 | `flow_get_host_group` **只吃数字 id**（uuid 会 400）；YAML 才要 uuid | §6.3、§6.1.1 |
| 13 | "绕过约束的漏洞只在 shell 通道" | **错**：`GitResetAsync(hard)` / `GitCleanAsync(-fd)` 在工作区内同样免回调直达 | §8.3、C14 |
| 14 | 白名单逐字匹配 `mcp_yunxiao_*` | 连接器实例改名会**全线失效** | §8.1（改按工具名段） |
| 15 | 验收只需"新增单测" | 3 个既有测试类会编译失败/断言红 | §12-2、§5.5、C18 |
| 16 | 未评估 `AutoConfirmTools` 轻路线 | 框架既有免确认清单，因"全局生效、不表达拒绝"而不采用 | §8.4 |
| 17 | 未提"混合结果"与"逐端完备性" | 单值 `status` 会把"一成一败"写成 `done` | §6.1.2 |
| 18 | 未提 `needsInput` 往返与结论文件时效 | 每次下发一份文件，可能读到旧轮 | §6.1.3 |
| 19 | 未接 Step 3 重跑失效联动 | PRD v0.30 既有裁决（`InvalidateBuild` / `InvalidatePipelineConclusion`） | §6.6、C16 |
| 20 | 未提聊天路径 Step 7 的结构性冲突 | 无面板即无"必选部署目标"载体 | §10、C15′ |
| 21 | 未提 PRD 回写与其它同步文件 | — | §17 |

### 18.2 v0.2 → v0.3（用户定案：7 条否决 + 2 条新增事实）

| # | 变更 | 落点 |
|---|------|------|
| 1 | **C5 否决** → 目录删除改为**助手执行**（`DeleteDirectoryAsync` 工作区内放行） | §2.3、§5.5、§6.1.3、§6.4、§8.6、§14.2、§17.2 |
| 2 | **C6 否决** → 全文改称"一次准备下发 + 执行续跑"，不再叫"两轮"（实质不变） | §5.0、§5.2、§5.4、§6.1.3、§11、§14.2、§15 |
| 3 | **C7 否决** → `StepGate` 判定源改**读结论文件**；`StepStates` 降级为"仅界面进度"；`InvalidateBuild` 改为作废结论文件 | §5.1、§5.3、§5.5、§6.6、§6.7、§12-2、§14.2、§17.1、§17.2 |
| 4 | **C8 否决** → 删 MCP `codeup_clone`/`codeup_push` + `CodeupClient.CloneToLocalAsync`/`PushAsync`/`ToGitCommandResult` + `GitCommandResult.cs`，并连带技能/README/单测/PRD | §5.5、§7.2、§8.1、§9、§12-2、§14.2、§17 |
| 5 | **C10 否决** → 整删 `PipelineTemplates.cs`（含 `PipelineYamlOverride`）+ **去掉面板即时校验** | §5.4、§5.5、§6.1、§11、§12-2、§14.2、§17.2 |
| 6 | **C11 否决** → **不新增 `precheck`/`preview` 段**，面板改吃通用 `artifacts[]`；`extra` 按 `kind` 用**具名强类型**（新写映射表） | §5.1、§5.2、§5.4、§5.5、§6.1、**§6.1.1**、§6.1.2、§6.6、§10、§12-2/3、§14.2、§17.2 |
| 7 | **C15 否决** → 聊天路径**在对话里选**部署目标，助手同会话续跑；删去"不可保证完成"定性 | §6.3、§10、§12-5、§14.2、§17.1 |
| 8 | **事实修正 A（工具数）**：删 2 补 3 ⇒ **12 → 13**（v0.2 写的"12 → 15"只算了补、没算删） | §1.1 T4、§3.1、§7、§8.1、§12-2、§14.1 C9、§17.1、§17.2 |
| 9 | **事实修正 B（新增）**：框架 `FileSystemToolPlugin.DeleteDirectoryAsync` 是裸 `Directory.Delete`，**不处理 Windows 只读位**（RC5）⇒ "助手删"需换机制（D1） | §3.1、§3.3、§5.5、§6.4、§8.6、§12-4、§14.3 **D1**、§15 |
| 10 | **事实修正 C（新增）**：框架**无"助手轮内提问"工具**（`AskUser`/`RequestInput` 全无）⇒ C6′ 只能是"改叫法"，不是"真单轮" | §3.1、§5.0、§6.1.3、§14.2 |