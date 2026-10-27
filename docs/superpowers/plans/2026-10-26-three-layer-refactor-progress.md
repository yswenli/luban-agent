# SDD ledger — plan: luban-agent/docs/superpowers/plans/2026-10-26-three-layer-refactor.md

## Environment baseline（执行前记录）

| 项 | 值 |
|---|---|
| 框架分支 | `refactor/three-layer-2026-10-26`（自 `main` @ `25ac101`） |
| 产品分支 | `refactor/three-layer-2026-10-26`（自 `main` @ `46fa6a7`，main 领先 origin 7 个提交） |
| 框架基线构建 | ✅ 通过，**必须带 `-p:RestoreIgnoreFailedSources=true`** |
| 产品基线构建 | ✅ 通过（18 warning，0 error） |
| 产品基线测试 | ✅ 276 passed / 0 failed |
| 框架基线测试 | 未跑（大量集成测试依赖外部服务），后续只跑新增单测 |

### 已知环境坑（每个 subagent 都必须知道）

1. **`WebApplication1/Nuget.Config` 里 `net6framework`（coding.net）源返回 404**，全解决方案构建时必须加 `-p:RestoreIgnoreFailedSources=true`，否则 restore 直接失败。**不要删除该配置文件**（属环境配置，不是本次任务）。
2. **框架仓库有 9 个 csproj 存在未提交改动**（`Microsoft.Extensions.*` 10.0.13 → 10.0.14，用户本人的改动）。因此：
   - 框架侧提交**禁止 `git add -A`**，必须显式列出本次新增/修改的文件。
   - 这些 10.0.14 改动**不属于本计划**，不得提交、不得回滚。

## Pre-flight 冲突扫描

| 任务 | 独占文件 | 与他组交集 | 裁决（Ruling） |
|---|---|---|---|
| Task 1 | 3 个 agent csproj | 无 | 计划里写「5 个工程」有误：只有 `LubanAgentCore` / `LubanAgentA2A` / `LubanAgent.Plugin.NewProject` 含 `LuBan.*` PackageReference；Cli/Codex/Tests 通过 ProjectReference 传递。**只改 3 个。** |
| Task 2–9 | 框架新增文件 | 互相顺序依赖 | 串行执行 |
| Task 10 | `WebApplication1/Program.cs`、`appsettings.json` | 无 | 需 `-p:RestoreIgnoreFailedSources=true` |
| Task 11–12 | Core + 两端 UI | 顺序依赖 | 串行 |
| Task 13 | `Core/Services/Management/*`、`Core/Security/*` | 是 14–19 的前置 | 接口骨架先落地并编译通过，才允许并行 |
| Task 14–19 | 各自独占（见计划 §10.1） | P6 消费 P1–P5 接口 | 骨架落地后并行 |

## 任务状态

| # | 任务 | 状态 | 备注 |
|---|---|---|---|
| 1 | ProjectReference 直连基线 | ✅ 完成 | 只改 3 个 csproj（R3）；`d44fa5a` |
| 2 | 框架契约一 | ✅ 完成 | 4 文件 203 行 + 4 测试；`33d1aa0` |
| 3 | 框架契约二 | ✅ 完成 | 11 文件 446 行 + 5 测试；`adb20c1` |
| 4 | 框架会话运行时 | ✅ 完成 | 5 文件 414 行 + 2 测试；`16fdd41` |
| 5 | 框架 AgentHost | ✅ 完成 | 6 文件 670 行 + 6 测试（R7–R11）；`82c2924` |
| 6 | 框架命令清单 | ✅ 完成 | 4 文件 256 行 + 3 测试；只有 3 个源文件，无 `SlashCommandClient.cs`（R15）；`b47e765` |
| 7 | 框架 Provider | ✅ 完成 | 4 文件 485 行 + 5 测试；两张表逐字校验一致；`9f8e035` |
| 8 | 框架工具展示 | ✅ 完成 | 3 文件 340 行 + 7 测试（R20：API 是 `GetAction`/`GetIcon`/`Summarize`）；`40984d1` |
| 9 | 框架 DI 扩展 | ✅ 完成 | 4 文件 251 行 + 4 测试；钉 OpenAI 2.14.0；`a7a6c49` |
| 10 | WebApplication1 最小端点 | ✅ 完成 | 3 文件 36 行；需用户填 API Key 后运行验证；`bd6bef4` |
| 11 | Core 切宿主 | ✅ 完成 | 删除 15 个重复类型 + 新增 2 个接缝文件；构建 ✅、`LubanAgent.Tests` 276 passed；`bec42e0` |
| 12 | UI 类型替换 + 冒烟 | 🟡 自动化部分完成 | 机械替换已在 `bec42e0`；补齐 XAML 命名空间（`1aa132e`）；容器 38 项解析全通过、CLI/Codex 实启动存活；7 项 TUI/GUI 交互冒烟待人工 |
| 13 | 9 服务接口骨架 | ✅ 完成 | 10 接口 + 4 record + 9 实现桩 + DPAPI 凭据保护；构建退出码 0；`06e9216`（R47–R49） |
| 14 | P1 模型 | ✅ 完成 | 契约补到 10 方法（R51）；CLI 两命令 + Codex 三处数据源切换；构建退出码 0、`LubanAgent.Tests` 289 passed、DI 冒烟 54/54；`ef0785d`（R51–R55） |
| 15 | P2 Skill/Rule | pending | |
| 16 | P3 MCP/Connector | pending | |
| 17 | P4 Workspace/Session/Wiki | pending | |
| 18 | P5 插件/凭据 | pending | |
| 19 | P6 Codex 设置页 | pending | |
| 20 | 收尾 | pending | 需人工发版，必停 |

## Rulings

- R1：分支而非 worktree。两个独立仓库、且 agent 侧要用 `..\..\luban-framework\...` 相对路径直连框架源码，worktree 会破坏该相对布局。已在两仓各建同名分支。
- R2：框架侧一律 `git add <显式路径>`，禁止 `git add -A`（保护用户未提交的 10.0.14 改动）。
- R3：Task 1 只改 3 个 csproj。
- R4：`StreamEvent` → `AgentStreamEvent` 一次性全量重命名，不留 `[Obsolete]` 别名。
- R5：`IAgentHost` 为 **Scoped**，`SessionRuntimeManager` 为 **Singleton**；`ValidateScopes`/`ValidateOnBuild` 下单例不得消费 Scoped。
- R6：`AgentRunRequest.ConfirmHandler` 保持同步 `Func<string, IReadOnlyDictionary<string, object?>, ConfirmResult>`；null 时沿用框架既有语义「未设置回调一律拒绝」。
- R7：`LuBanAgentOptions.Models` 实为 `Dictionary<string, ModelEndpointOptions>`（仅 `BaseUrl`/`ApiKey`），故 `DefaultAgentModelSelection` 改读 `LuBanAgentOptions.DefaultModel`（`string?`，`provider:model`），不新增 `ModelSelectionOptions`；未配置时 `QualifiedModelName` 为空串、`ProviderId`/`ModelId` 为 null。
- R8：`IModelCapabilityGate` 在原 2 方法外新增 `IModelCapabilityProbe? CreateProbe(string modelId, bool hasImage)`（同文件定义 `IModelCapabilityProbe`）；`AllowAllModelCapabilityGate.CreateProbe` 返回 null；`IAgentAttachmentOrchestrator` 同步新增 `CreateProbe`。视觉探测/回填必须归 Core。
- R9：`AgentHost.RunStreamingAsync` **不**校验 `request.Input` 非空，只 `ArgumentNullException.ThrowIfNull(request)` —— 仅附件、正文为空是合法消息（`@文件` 被剥离后正文可能为空）。
- R10：`DefaultAgentAttachmentOrchestrator.ProcessAsync` **逐文件**调 `IAttachmentProcessor.ProcessAsync`，保留「最多 5 个」上限与原文案 `最多附加 {MaxAttachmentsPerMessage} 个文件`；`ExtractReferences` 默认原样返回 `(input, empty)`。
- R11：`ToolConfirmationContextExtensions.ConfigureForTurn` 搬到框架 `Hosting/`，去掉可选 `IWorkspaceManager` 参数，只保留 `sessionContext` 路径检查；Task 11 需删除产品同名文件（唯一调用方 `AgentHostService` 将一并删除）。
- R12：`SessionRuntimeSeed` 不增 `UserId`；`CoreSessionRuntimeInitializer` 必须从注入的 `DefaultContextState.UserId` 复制到产品 `UserContextState.UserId`，否则会话 scope 内按用户隔离的 `ConfigManager` 等会落到默认用户桶。
- R13：`LubanAgent.Plugin.NewProject/Services/HostAssistantBridge.cs` 仍引用 `AgentHostService`；Task 11 删除后必须最小适配为 `IAgentHost`（`GetService<IAgentHost>()` + `InitializeAsync(sessionId)`）。
- R14：框架测试项目是 MSTest **4.5.1**，`Assert.ThrowsException*` 已移除，统一用 `Assert.ThrowsExactly` / `ThrowsExactlyAsync`；`CollectionAssert.IsNotEmpty` 也不存在（只有 `AreEqual`/`AreEquivalent`/`Contains`/`DoesNotContain`/`AllItemsAreNotNull`/`AllItemsAreUnique`/`IsSubsetOf`/`IsNotSubsetOf`/`AllItemsAreInstancesOfType`），非空断言统一用 `Assert.IsNotEmpty`。
- R15：Task 6 产品 `Commands/` 只有 3 个文件（`SlashCommandClient` 枚举定义在 `SlashCommandDefinition.cs` 内），框架侧**不新建第 4 个文件**。
- R16：`RequestDumpChatClient` **不搬**框架，留 `LubanAgentCore/Infrastructure/`。框架 `LuBanChatClient` 构造函数新增第三个可选参数 `Func<IChatClient, IChatClient>? decorateClient = null`，`CreateChatClient` 中对原始 `IChatClient` 应用；为 null 不包装。产品在 Task 11 传 `c => new RequestDumpChatClient(c)` 保留诊断行为，框架零产品类型。
- R17：`ProviderConfig` / `ProviderEndpointInfo` / `ExtendedProviderInfo` **不搬**框架 —— 它们是产品配置模型（`ConfigManager`、`ProviderHelper.Initialize` 的 `LuBanAgent:Providers` 绑定、Task 14 的 `UpsertProviderAsync` 都要用）；框架 `ProviderDefaults` 只承载 `_defaultBaseUrls`（17 项）与 `_models`（20 项）两张只读表，`ProviderIds` = 模型表键顺序。
- R18：`ProviderHelper` 的可变状态（`_providerConfigs`/`_initialized`）与远端刷新（`RefreshModelsAsync`/`FetchModelsFromEndpointAsync`）**不进框架**，留 Core。
- R19（Task 11 增补）：Task 11 除计划列出的文件外，还须删除产品 `LubanAgentCore/Configuration/LuBanChatClient.cs` 与 `ProviderCatalog.cs`，让 `UserContextRegistry` 改用框架 `LuBan.AIAgent.Configuration.Providers.LuBanChatClient`（传 `decorateClient`），CLI/Codex 的 `ProviderCatalog.All` 改用框架类型；`ProviderHelper` 的 `_defaultBaseUrls`/`_models` 改为委托框架 `ProviderDefaults`，避免两张表双份漂移。

- R20：Task 8 计划写的 API 名（`ToolDisplayNames.Get` / `ToolArgsFormatter.Format`）与源码不符，**以源码为准**：实际是 `GetAction(string)` / `GetIcon(string)` / `Summarize(IReadOnlyDictionary<string, object?>?, int maxLength = DefaultMaxLength)`；只按实际 API 写测试，不改实现名。
- R21：Subagent 配额于 Task 8 派发时耗尽（token-plan 1 个月额度，10-27 16:00 UTC 重置），Task 8 起由控制器本人按同一 TDD 流程直接执行，流程与验收标准不变。
- R22：`WebApplication1/appsettings.json` 尾部有一份重复 `LuBanAgent` 段导致 `DefaultModel` 被覆盖为空、启动即失败；JSON 的 `//` 注释解析正常，**不是**根因。删重复段 + 补 `DefaultModel` 后端点可用。
- R23：`WebApplication1` 运行时验证用真实 Key 走环境变量注入（`LuBanAgent__Models__ApiKey` 等），不落盘、不写仓库文件；验证后停进程并清 env。
- R24：产品 `Agents/AgentProfile.cs` 与框架 `AgentProfileBase` 等价（仅 `WorkspaceInfo` → `WorkspaceDescriptor` 差异），直接删产品文件；`LlmWikiAgentProfile.ConfigPath` 会丢，改用常量 `".luban-agent"`（与原 `ConfigManager` 约定一致）。
- R25：视觉门禁与附件编排必须留在产品：新增 `VisionModelCapabilityGate : IModelCapabilityGate`（内部复用 `AttachmentService.CanAttach/GetUnsupportedMessage`）、`CoreAttachmentOrchestrator : IAgentAttachmentOrchestrator`（复用 `AttachmentService.ParseFileReferences` 保留 `@文件` 行为），`VisionCapabilityProbe` 直接实现 `IModelCapabilityProbe`。框架侧 `AllowAllModelCapabilityGate` 不承担产品语义。
- R26：不新增 `global using LuBan.AIAgent.Hosting;`（Codex `GlobalUsings.cs` 只把 `LubanAgentCore.Commands` 换成 `LuBan.AIAgent.Hosting.Commands`），`IAgentHost`/`AgentStreamEvent`/`SessionRuntime*`/`DefaultContextState` 由各文件显式 `using`，避免与产品同命名空间类型歧义。
- R27：Task 11 的「删类型」必然连带 UI 编译失败，故 `AgentHostService → IAgentHost`、`StreamEvent → AgentStreamEvent`、`RunStreamingAsync(8 参数) → AgentRunRequest` 的**机械替换在 Task 11 内完成**（计划 Task 11 Step 7 已允许），Task 12 只负责真实冒烟与行为回归。
- R28：`IAgentHost.InitializeAsync` 只有一个带 `sessionId` 的重载，产品原 `InitializeAsync()` 无参调用点一律显式传当前会话 id（`MainWindowViewModel` 两处传 `session.SessionId`）。
- R29：产品 `SessionContextState` 的全部消费点（`StartupWorkspaceResolver`、`CodexPluginHost`、`PluginHostContext`、`WorktreeSessionManager`）改读框架 `DefaultContextState`；`HostAssistantBridge` 的最小适配为 `GetService<IAgentHost>()` + `RunStreamingAsync(new AgentRunRequest{...})`，不保留旧重载。
- R30：**构建验收必须看退出码**。Avalonia XAML 编译错误行格式是 `<file>: Avalonia error AVLN2000: ...`，用 `': error '` 过滤抓不到，导致 Task 11 误判「0 错误」。实际 `LubanAgentCodex/Views/Controls/InputBox.axaml` 仍写 `clr-namespace:LubanAgentCore.Commands;assembly=LubanAgentCore`（该命名空间已删），XAML 编译失败 → 程序集里没有预编译 XAML → 运行时 `XamlLoadException: No precompiled XAML found for LubanAgentCodex.App`（退出码 `0xE0434352`）。修正为 `clr-namespace:LuBan.AIAgent.Hosting.Commands;assembly=LuBan.AIAgent` 后恢复正常（`1aa132e`）。
- R31：Task 12 的自动化验证用**仓库外临时控制台工程**（`%TEMP%\opencode\di-smoke`，`ProjectReference` 直连 `LubanAgentCore.csproj`）复刻 CLI 启动序（`BuildConfiguration` → `InitConfigUtil` → `ProviderHelper.Initialize` → `DatabaseInitializer.Initialize` → `AgentHostBuilder.BuildServiceProvider` → `CreateSessionAsync`/`SetCurrentSessionAsync` → `InitializeAsync`），共 38 项断言全通过；不入库，避免为一次性冒烟在生产解决方案里加可执行夹具。
- R32：`IAgentHost.IsInitialized` 依赖 `ISessionManager.CurrentSession`，冒烟必须先真实建会话并设为当前，否则 `InitializeAsync` 成功但 `IsInitialized` 仍为 false（框架语义正确，非缺陷）。

## 任务 2–5 期间订正的计划事实错误

1. 测试命名空间是 `LuBan.XTestProject`（计划文档写的 `LuBan.UnitTestProject` 是笔误）。
2. `LuBanAgentOptions.Models` 是字典（见 R7）。
3. `IAttachmentProcessor` 是**单文件**接口（见 R10）。
4. `ToolPermissionMode` 默认值是 `Default`，不是 `Normal`。
5. `ConfigureForTurn` 以扩展方法形式搬到框架（见 R11）。
6. 框架会话运行时用 `_root.CreateScope()` + 显式回填 `DefaultContextState.UserId`，不使用产品 `CreateUserScope`。
## 插入项：附件缺陷修复（A+D 方案，2026-10-26）

真机现象：用户输入 `D:\Users\yswenli\图片\Marcus.png 看看这个图里面有什么`，模型拿到的是文本路径，改用 `ReadFile` 读 4.8MB PNG 二进制，最后报「无 Pillow / 无视觉工具」。DB 取证：会话 `d7a08127366e` 该条 user 消息 `attachments = NULL`。

- R33：根因是**图片从未进入 `AgentRunRequest.AttachmentPaths`**，不是宿主链路丢 `DataContent`（`AttachmentMessageBuilder`/`SanitizingChatClient`/`ResilientChatClient`/`RequestDumpChatClient`/`LuBanChatClient` 均保留 `DataContent`），**不是**本次三层重构回归。
- R34：CLI 附件只有 3 个入口：`@路径`（`InputBarView.cs` `OnEditorSubmit`）、`Ctrl+O`、`TryConsumeDroppedPath`（要求整段内容恰为存在文件）。「路径 + 问题」一个都不命中。
- R35：框架 `IAgentAttachmentOrchestrator.ExtractReferences` 此前全仓零调用点（死接缝）；Codex 端无任何 `@` 识别。
- R36：选定 **A+D**：A = 框架统一接线 `ExtractReferences` + 裸图片路径自动转附件；D = 附加失败明确提示 + `/help` 与启动横幅补 `@路径` / `Ctrl+O` 引导。不做阻塞式拦截（附件无法清除会造成死锁）。
- R37：`ModelCapability.SupportsVision` 未知默认 `true`（乐观放行）；用户配置的 `ModelCapabilities` 不含当前 `SelectedModel`（`内网-10.10.0.147:qwen3.8-flash`），故门禁不会误拦。
- R38：附件通道不做工作区路径规则校验（`D:\Users\yswenli\图片\` 在工作区外是合法的）。
- R39：实现落点——框架 `Hosting/AgentAttachmentResolver.cs`（合并 UI 路径 + `@引用`、`Path.GetFullPath` 归一、`OrdinalIgnoreCase` 去重、UI 优先），`AgentHost.RunStreamingCoreAsync` 在模型校验后 `request = request with { Input = resolved.Text, AttachmentPaths = resolved.Paths }`；产品 `AttachmentService.FindBareImageMentions/StripBareImageMentions` + `CoreAttachmentOrchestrator.ExtractReferences` 接入裸图片路径；CLI `OnEditorSubmit` 追加同一兜底（仅附加成功才剥离正文，失败 `Notify` 但不拦截发送）。
- R40：裸路径正则按 `MediaTypeUtil.ImageExtensions` 长度降序拼 alternation，**必须**整体包一层 `(?:...)` 且与引号分支一起被起始边界 `(?<![A-Za-z0-9_@\.\\/])` 约束（漏掉分组会让 `|` 把边界拆掉，实测会把 `@C:\...\a.png` 里的路径再命中一次，并把 `.png` 当作相对路径）。
- R41：验证——框架 `AgentAttachmentResolverUnitTest` 4/4 通过；产品 `AttachmentBareImageMentionUnitTest` 8/8 通过；`LubanAgent.slnx` 构建退出码 0；`LubanAgent.Tests` 284 passed / 0 failed。
- R42：待办——CLI 实机复测「图片路径 + 问题」（期望出现附件块且 DB `attachments` 非空）、Codex 同场景复测。
- R43（复核 P1-1 修复）：`ParseFileReferences` 原来**无条件** `Regex.Replace(text, "[ \t]{2,}", " ").Trim()`，而 Codex 每轮都走 `ExtractReferences`，等于静默改写用户正文。改为收集「有效引用区间」后由 `RemoveRanges` 定点移除：只吸收被移除片段两侧紧邻的空格/制表符，两侧都还有正文且不在换行边界时补一个空格；未命中引用时正文**一个字符都不动**。`StripBareImageMentions` 复用同一 `RemoveRanges`，不再全局压缩空白（否则会破坏正文别处的代码缩进）。
- R44（复核 P1-2 修复）：裸路径扫描先算「受保护区」——围栏代码块（` ``` ` / `~~~`，未闭合则其后全部视为代码）与行内反引号代码；命中区间与受保护区相交则跳过。另加同行前文代码特征判定（前文 trim 末尾为 `=`/`(`/`[`/`{`/`,`，或为 `"`/`'` 且其前一字符是 `@`/`$` 或同行含 `=`/`(`），拦掉 `var p = @"D:\imgs\logo.png";` 这类示例路径。修的是「代码里的路径被当附件附加、随后又被 `StripBareImageMentions` 从代码里删掉」。
- R45（复核 P1-3 修复）：`AgentAttachmentResolver.Merge` 删掉 `refCount == 0` 早退（原来只有 UI 路径时直接透传宿主原始列表，跳过 `Trim`/`GetFullPath`/去重）。现在两路都走 `Append`：归一 → 丢弃空项 → `OrdinalIgnoreCase` 去重，UI 优先。
- R46：复核后验证——框架 `AgentAttachmentResolverUnitTest` 5/5、产品 `AttachmentBareImageMentionUnitTest` 13/13，`LubanAgent.Tests` 289 passed / 0 failed；`LuBan.FrameWork.sln` 与 `LubanAgent.slnx` 构建退出码均为 0。
- R47（Task 13 类型策略，用户拍板 **B**）：计划原文的 7 个新 record 有 4 个是臆造——`SkillInfo`（框架 `Tools/Skill/SkillToolGroup.cs:91`）、`ProviderInfo`（`Configuration/IProviderRouter.cs:46`）、`McpTestResult`（`Tools/McpManage/McpManageToolGroup.cs:267`）、`PluginDescriptor`/`PluginPermissions`/`PluginChangedEventArgs`（`Plugins/{Lifecycle,Contracts}`）、`ConnectorStatus`（`Connectors/ConnectorRegistry.cs:44`）、`SessionInfo`/`SessionMessage`（`Sessions/ISessionManager.cs:153/194`）、`ConnectorConfig`/`McpServerConfig`（`Configuration/`）、`WorkspaceInfo`（产品 `Services/WorkspaceManager.cs:29`）全部**复用**；真正新建只有 `RuleInfo`/`McpInfo`/`ConnectorTestResult`/`WikiStatus`。计划里的 `McpConfig` 实际叫 `McpServerConfig`。
- R48（Task 13 骨架策略，用户拍板 **E1**）：不用计划原文的 `services.AddScoped<I>(_ => throw new NotImplementedException("P1"))`——那会让任何解析都炸、且 Task 14–19 必须回改 `AgentHostBuilder`（与并行规则冲突）。改为 9 个**真实实现类**（方法体 `throw NotYetImplemented.For("Task 14", "ModelCommand / …")` 保留任务号与业务来源）+ DI 注册真实类型；`ValidateOnBuild` 下容器依然可用，Task 14–19 只改方法体。
- R49（Task 13 凭据策略，用户拍板 **F1**）：`System.Security.Cryptography.ProtectedData` 9.0.5（本机 NuGet 缓存最高版，10.x 未缓存；net10.0 平台中立 TFM 必须显式引用）。接口签名由计划的 `byte[] Protect(byte[])` 改为 **string**：密文以 `dpapi:` 前缀 + Base64 直接可落 JSON 配置，非加密值原样返回便于渐进迁移；`Entropy = "LuBan.AIAgent.Credential.v1"`；非 Windows `IsAvailable=false` 且加解密抛 `InvalidOperationException`（不降级明文）。CA1416 通过在每个方法内直接 `if (!OperatingSystem.IsWindows()) throw` 消除。
- R50：Task 13 构建实测 `LubanAgent.slnx` 退出码 0、0 错误、16 警告；其中 `NU1902/NU1903 System.Security.Cryptography.Pkcs 8.0.1` 来自 `LuBan.Orm → SqlSugarCore → Oracle.ManagedDataAccess.Core`（`dotnet nuget why` 已确认），**非本次引入**，不在 Task 13 处理范围。
- R51（Task 14 契约补齐，用户拍板 **H2**）：`IProviderModelManagementService` 的方法数 7 → 10，`GetProvidersAsync` 返回 `IReadOnlyList<ProviderConfig>`（**不**保留 `ProviderInfo`，因为 CLI/Codex 需要 `ApiKey`/`BaseUrl`/`CustomModels`）；新增 `AddModelAsync`/`UpdateModelAsync`/`DeleteModelAsync`，把 `/model -add/-update/-delete` 的自定义模型增删改一并收口。
- R52：`ProviderModelManagementService` 只做编排，业务规则全部委托 `ConfigManager`（持久化）与 `ProviderService`（按用户隔离的模型缓存）；`GetAllModels` 走 `_providers.GetAllModels(id, _config.GetProvider(id)?.CustomModels)`，与原命令 `_providerService.GetAllModels(name, provider.CustomModels)` 等价。`GetProvidersAsync` 返回**深拷贝快照**（7 个属性逐个复制 + 两个 List 复制），调用方误改不会漏 `Save()`。
- R53：`SetSelectionAsync` **不做会话运行时重建**。计划原文写「含会话重置」，与现状不符：`ConfigManager.SetSelectedModel` 的 `SelectedModelChanged` 全仓无订阅者，切模型对已建会话本就不生效，本 Task 只搬不改。
- R54：`DeleteProviderAsync` **保留**既有缺陷：删除的正是当前选中 Provider 时，`SetSelectedModel("")` 会抛 `ArgumentException("模型不能为空")`，命令层 catch 后打印红色错误、不输出成功文案（Provider 实际已删除并保存）。是否改 `ClearSelectedModel` 待用户拍板；命令层的「注意: 已清除当前选择的模型」提示按删除前归属判断，行为与原实现一致。
- R55：唯一有意偏差——Codex `/model -switch` 此前把任意字符串直接写入 `SelectedModel`（无 `:` 也照写），现走 `SetSelectionAsync(providerId, modelId)` 必须先拆分，故对不含 `:` 的输入改为报错「模型格式错误，应为 provider:model，例如 openai:gpt-4o」（与 CLI 同文案）。合法输入行为不变。另 `InputBox` 下拉切换改为调 `SetSelectionAsync`（该方法同步完成持久化后返回已完成 Task，顺序与直接调 `SetSelectedModel` 一致）。
- R56（Task 15 契约，用户拍板 **A**）：`ISkillManagementService`/`IRuleManagementService` 全部 **DTO 化**，逐条改为 `ListAsync`（返回 `IReadOnlyList<SkillListItem>` / `IReadOnlyList<RuleInfo>`）、`AddAsync`/`UpdateAsync`（接收 `CustomSkillConfig`/`CustomRuleConfig`）、`DeleteAsync(id, origin, workspaceRoot?)`、`SetEnabledAsync`、`ListFileIds`、`ReadFile`、`SaveFile` —— 各 8 个方法。计划原文的 `ImportAsync` 不存在于现有代码，直接删除不建。返回 DTO 而非框架接口（`ISkill`/`IRule`）的原因：CLI/Codex 需要 `Enabled`/`Origin`/`Examples` 等可编辑字段，框架接口不外露 disabled 项与来源。
- R57（Task 15 来源标记，用户拍板 **A + C**）：新增 `ManagedOrigin { Builtin, File, Config }`，CLI 显示为 `[内置]`/`[文件]`/`[自定义]`；`RuleInfo` 增 `Origin`/`Priority`/`ActionTypePattern`/`TargetPattern`/`Action` 与派生属性 `IsBuiltin`/`IsCustom`。来源判定依据：Skill 用 `SkillRegistry` 的 `IsBuiltin`（`SkillLoader` 三级合并时已标记）；Rule 因框架 `RuleEngine` 不外露来源，只能反推——`rule is not CustomRule` → 内置，否则看文件命中还是 config 命中（`CustomRule` 同时被文件型与 config 型包装，故按 Id 在 `ScanRuleFiles` / `ConfigManager.CustomRules` 中出现的位置分类）。文件型在 `-switch` 中提示「请在该规则文件里修改 Enabled 字段」（`NotSupportedException`），因为 `RuleEngine` **加载时直接跳过** `Enabled == false` 的文件规则，服务侧无法用 config 开关控制它。
- R58：服务必须**单独补出**框架合并视图漏掉的项——① 被禁用的内置项（`DisabledBuiltinSkills`/`DisabledBuiltinRules`）；② 文件型禁用项（`LoadFromWorkspace` 只收 `Enabled == true`）；③ config 禁用项（`LoadFromConfig` 只加载 `Enabled == true`；RuleEngine 侧虽保留但 SkillRegistry 侧不保留）。列表顺序沿用旧实现：启用项按分类/优先级，禁用内置与禁用自定义各自成组。
- R59（Task 15 后端形态）：Codex 设置页 Skill/Rule 页整体切到服务（用户拍板 **C**），故服务必须双后端。**文件后端方法声明为同步**（`ListFileIds`/`ReadFile`/`SaveFile`）——Codex UI 线程原先在 `BuildEditor`/`SaveXxxAsync` 里直接 `File.ReadAllText`，若改成 `async` 后由 UI 线程 `GetAwaiter().GetResult()` 等待会死锁；三个方法内部只是一次同步磁盘 IO，标成 `Task` 纯属包装。config 后端（`AddAsync`/`UpdateAsync`/`SetEnabledAsync`）保持 `Task` 以对齐 CLI 调用风格。
- R60：**只搬不改**的保留项（有意偏差，均在旧代码里就存在）——① `/rule -list` 仍只显示 `IsEnabled || IsCustom` 的项，不新增「禁用的内置规则」行（旧实现遍历 `GetAllRules()`，禁用的内置本就不在合并视图，故不显示）；② `/skill -update`、`/rule -update`、`-delete` 仍只针对 `ConfigManager.CustomSkills/CustomRules` 里的 config 自定义项，不覆盖文件型；③ 改名撞名/文件占用时提示带「保存失败：」前缀（Codex 侧新增 `SetHint($"保存失败：{ex.Message}")`，旧代码是直接 `File.Move` 抛异常未捕获）。另 `RuleCommand` 不再注入 `RuleEngine`（全部读取走服务），`SkillRegistry` 仍保留注入，仅服务 `ExecuteSkillAsync` 执行 Skill。
- R61：Task 15 验证——`LubanAgent.slnx` 构建退出码 **0**（0 错误、18 警告，仍为既有 `NU1902/NU1903`）；`LubanAgent.Tests` **301 passed / 0 failed**（新增 `SkillRuleFileBackendUnitTest` 12 用例：Skill/Rule 保存读取往返、改名移动/删旧、重名抛错、路径穿越拒绝、排序、Action 归一化）；DI 冒烟 54/54 通过；Task 15 **未改框架源码**，`LuBan.*` 版本仍为 `2026.10.20.1`。

- R62（Task 16 计划事实订正）：计划原文三处与代码不符——① `ConnectorCommand` **从不存在**（CLI 无连接器命令）；② Codex **无连接器页**，只有「云效连接」组织 ID + PAT 表单；③ MCP 现状是**双后端**（CLI 只读 config.json + 内置；Codex 设置页只写 `mcps/*.json` 文件，二者互不影响）。故 Task 16 按「MCP 收口 + Connector 只做服务不接 UI」执行。
- R63（Task 16 MCP 契约，用户拍板 **A1**）：`IMcpManagementService` 补齐到 **11 个方法**——`ListAsync`、`AddAsync`、`UpdateAsync`、`DeleteAsync(name, origin, workspaceRoot?)`、`SetEnabledAsync`、`ConnectAsync`、`ListToolsAsync`、`TestAsync` + 文件后端同步方法 `ListFileIds`/`ReadFile`/`SaveFile`；来源复用 Task 15 的 `ManagedOrigin`，列表 DTO 复用/新建 `McpInfo`，探测结果复用框架 `McpTestResult`，工具项复用框架 `MCPTool`。
- R64（Task 16 只搬不改取舍）：CLI 的 `-update`/`-delete`/`-switch` 仍用 `ConfigManager` 读原始配置填表单（沿用 `/skill`、`/rule` 先例），**写操作全部走服务**；表单级校验文案（`名称不能为空`、`名称 '{name}' 与内置客户端冲突`、`名称 '{name}' 已存在`、`启动命令不能为空`）与 `-list`/`-switch`/`-connect`/`-tools` 的表格列、顺序、成功/失败文案保持原样。服务在写完 config.json 后调 `_registry.Reload()`，使同进程内后续 `-list` 立即反映变更（旧实现要重启 CLI 才可见）。
- R65（Task 16 Connector 范围，用户拍板 **C1**）：`ConnectorManagementService` 落地 5 个真实方法（`ListAsync`/`AddAsync`/`DeleteAsync`/`SetEnabledAsync`/`TestAsync`），**不改任何 UI**。连接器启用状态只落工作区（`config.json` 只表达可用性），未选工作区时 `Add/Delete/SetEnabled` 直接抛错不隐式回落；`TestAsync` 三条早退分支按序返回中文原因：未声明 → 提供方未注册 → 凭据引用缺失（`ICredentialStore` 在 Core/CLI 可能为 null，此时任何 `$credential:` 引用都判缺失）；写盘后 `LoadFromWorkspace` + `IAgentReloadTrigger.RequestReload()`，与框架连接器管理工具同口径。
- R66（Task 16 Codex 切服务）：Codex 设置页 MCP 页（`EnumerateItems`/`ItemExists`/`BuildMcpEditor`/`SaveMcpAsync`/`NewItem`/`DeleteItemAsync`）全部改走 `McpManagement`，`ScopeRoot` 传 `_selectedWorkspace?.RootPath`（null = 全局 `~/.luban-agent/mcps`）；删除页内 `JsonOpts` 与已无用的 `CurrentDir()`。`SaveMcpAsync` 不自行 catch（`OnSaveClick` 已统一输出「保存失败：」），并去掉 `async`（文件后端同步）。改名语义与原实现一致：`SaveFile(originalId, cfg)` 在 `originalId != Name` 时删旧文件。
- R67：Task 16 验证——`LubanAgent.slnx` 构建退出码 **0**；`LubanAgent.Tests` **310 passed / 0 failed**（新增 `McpConnectorManagementUnitTest` 9 用例：MCP 文件后端字段往返、改名删旧、排序、删除、路径穿越/空名拒绝 + 连接器探测未声明/提供方未注册/凭据缺失）；DI 冒烟 **54/54** 通过；Task 16 **未改框架源码**，`LuBan.*` 版本仍为 `2026.10.20.1`。
- R68（Task 17 契约与职责边界，用户拍板）：① 工作区服务 **A**——`IWorkspaceManagementService` 保持 6 个方法严格转发，关键字解析、模糊匹配、会话数聚合留命令层；② 会话列表 **A**——`ListAsync(string? workspaceId = null)`，非空走工作区仓储过滤、空走 `ISessionManager.GetUserSessionsAsync()`；③ `/session -clear` **A**——新增 `ClearAllAsync(string? workspaceId)`，契约共 8 个方法；④ 列表预览与「(当前)」标记 **A**——留在命令层（预览直接读 `SessionMessageRepository`）；⑤ LLM Wiki 范围 **B**——整条 `/llmwiki` 收口，9 个子命令全部进服务，契约共 13 个方法。Codex 会话删除切 `DeleteAsync`、`WikiStatus` 对齐 CLI 并保留索引计数（用户拍板 A）。
- R69（Task 17 单测方案 A，用户拍板）：3 个 ORM 仓储**不直接注入服务**，改为抽最小接口 `ISessionWorkspaceQuery`/`ILlmWikiFileStore`/`ILlmWikiChunkStore`（`Repositories/ManagementStores.cs`），由 `SessionRepository`/`LlmWikiFileRepository`/`LlmWikiChunkRepository` 原地实现，`AgentHostBuilder` 用 3 行 Singleton 映射到具体仓储。原因：具体仓储构造函数会触发 `LuBanOrm` 静态构造（读 `appsettings.json` 的 `SnowId`/数据库配置），无库测试进程一 new 即抛「未配置数据库」，不改就无法做纯单测。顺带避免把整张 ORM 诊断类型泄漏到服务契约。
- R70（Task 17 只搬不改保留项）：`/session -new` 仍**不传 `workspaceId`**（`_sessions.CreateAsync(title, workspaceId: null)`），会话落库 `WorkspaceId = null`，随后 `/session -list` 按当前工作区过滤看不到——既有缺陷，本次不修，保持命令层行为一致。
- R71（Task 17 顺带修复，均为搬迁过程中被暴露的既有显示/数据缺陷）：① CLI 会话列表更新时间列原用 `LuBan.Common.StringUtil.ToString(this object?, format)` 扩展，`DateTime?` 会走对象格式化输出 `"0.00"`；改为 `session.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "-"`。② Codex 侧栏删除会话原直接调 `SessionRepository.SoftDeleteAsync`，只软删会话行、消息成孤儿；改走服务 `DeleteAsync` → `ISessionManager.DeleteSessionAsync`（软删会话 + 物理清消息 + 清当前会话引用），并带越权防护。③ Codex 新建会话原手写 `DbSession` 插库（易漏 `UserId`），改走服务 `CreateAsync`。
- R72：Task 17 验证——`LubanAgent.slnx` 构建退出码 **0**；`LubanAgent.Tests` **335 passed / 0 failed**（新增 `ManagementServiceUnitTest` 25 用例：工作区 6 方法转发 + 授权门禁跳过不存在工作区、会话两条列表数据源与映射/转发、LLM Wiki 三种检索范围选路、状态取当前工作区、`ClearAsync` 先文件后切块、ingest 小文件提取与 `../` 越界拒绝、未配置嵌入模型的守卫、后台任务服务缺省返回 null）；DI 冒烟 **60/60** 通过（新增 3 个仓储缝解析 + 3 个服务实链路断言）；Task 17 **未改框架源码**，`LuBan.*` 版本仍为 `2026.10.20.1`。
- R73（Task 18 四项拍板）：① 拆分形态 **A** —— Core 新增 `IPluginUiDispatcher`（`IsUiThread`/`Post`/`Invoke`）+ `ImmediatePluginUiDispatcher` 默认实现，Codex 提供 `AvaloniaPluginUiDispatcher` 并 `Replace` 覆盖；宿主业务原样搬迁，仅把两处 `Dispatcher.UIThread` 换成接缝调用。② 插件管理服务契约**完整对齐**现有窗口能力：`ListAsync`/`GetAsync`（新增，供 `LastError` 文案）/`InstallAsync→Task<string?>`/`UpgradeAsync→Task<bool>`（新增）/`UninstallAsync→Task<bool>`/`SetEnabledAsync→Task<bool>` + `Changed` 事件；**删除**零消费者的 `GetPermissionsAsync`。③ `AgentPluginHost` 统一到 `AgentHostBuilder` 注册（`IPluginHost`/`IPluginSessionContributor` 映射同处），CLI/A2A 也可解析。④ `PluginBootstrap` 一并搬 Core 并改 `public static`，Codex 仅调用。
- R74（Task 18 搬移范围）：`git mv` 6 个文件——`CodexPluginHost`→`LubanAgentCore/Plugins/AgentPluginHost`（改名）、`PluginHostRegistry`/`PluginHostContext`/`PluginMcpCatalog`/`PluginBootstrap`→`LubanAgentCore/Plugins/`、`CodexCredentialStore`→`LubanAgentCore/Security/CredentialStore`（只搬类型，类名改为 `CredentialStore`；**注册仍留在 Codex**，CLI/A2A 行为零变化；凭据仍为明文基线，未接 DPAPI）。`PluginHostContext` 搬入 Core 后需显式 `using LubanAgentCore.Hosting`（`LocalUserDefaults` 来源），Codex 侧原靠全局 using。`UnregisterPanel` 仍走**同步** `Invoke`（ALC 卸载弱引用验证硬约束），`GetCommands` 保留。
- R75（Task 18 Codex 侧改动）：`App.axaml.cs` 删掉插件三件套注册、只留 `Replace<IPluginUiDispatcher, AvaloniaPluginUiDispatcher>`；`Sidebar`/`MainWindow` 字段类型换 `AgentPluginHost`；`PluginManageWindow` 整体切 `IPluginManagementService`——`Refresh()` → `RefreshAsync()`（`ListAsync` + 读失败提示）、`DescribeError` → `DescribeErrorAsync`（`GetAsync().LastError`）、`RunAsync` 的 bool 重载改 `Func<bool, Task<string>>`、6 个操作按服务返回值组装**原文案**、`Changed` 订阅 + 关闭退订。`CredentialStore` 相关行只换类型名，`IYunxiaoCredentialProvider`/`YunxiaoSettingsService` 注册不变。
- R76（Task 18 测试覆盖边界）：新增 `PluginHostUnitTest` **12 用例**（侧栏登记/覆盖/移除与变更通知、缺 id 忽略、面板工厂覆盖丢弃旧实例、注销顺序「同步解绑 → `OnUnloading` → 移除」、无实例时不解绑但仍走同步封送、封送接缝 `Post`/就地分支、命令登记、无活动会话时 MCP 注册不崩）。**`IPluginManagementService` 不做单测**：`PluginManager` 是 `sealed` 具体类且未抽缝，测试会连带触发 `LuBanOrm` 静态构造（无库即抛「SnowId 未配置」）；其覆盖依赖宿主单测 + 手动冒烟。技能路径（`RegisterSkill`/`UnregisterSkill`）会经工作区管理器落库，同属集成路径，不入无库单测。
- R77：Task 18 验证——`LubanAgent.slnx` 构建退出码 **0**（0 错误）；`LubanAgent.Tests` **347 passed / 0 failed**（335 基线 + 12 新用例）；DI 冒烟 **66/66** 通过（新增 `AgentPluginHost`/`IPluginHost`/`IPluginSessionContributor`/`IPluginUiDispatcher` 解析 + `IPluginManagementService.ListAsync`/`GetAsync` 只读断言；无 UI 宿主默认解析为 `ImmediatePluginUiDispatcher`）；Task 18 **未改框架源码**，`LuBan.*` 版本仍为 `2026.10.20.1`。

- R78（Task 19 拆分形态，用户拍板 **A**）：`Views/SettingsWindow.axaml.cs` 由 **1722 行**重写为 **192 行窗口壳**（`_vm`/`_services` 字段、`InitializeComponent`、`LoadAsync`、`PreselectTab`、`BuildEditor` 分发、`SetField`/`GetText`/`GetToggle`/`GetCombo` 控件解析、事件接线）。UI 按职责拆 4 个 partial（同目录 `Views/Settings/`）：`SettingsWindow.Navigation.cs`(240，路径解析 / 左栏分类导航 / 顶栏作用域 / 中栏列表)、`SettingsWindow.Actions.cs`(440，新建 / 保存 / 删除 / 应用配置 / 动作栏 / 提示)、`SettingsWindow.Editors.Workspace.cs`(224，编辑器分发 + Skill/Rule/MCP 编辑器 + 字段工厂)、`SettingsWindow.Editors.Global.cs`(422，Provider/Model/A2A/Yunxiao 编辑器 + 自定义模型行 + 供应商预设联动)；`SettingsTabKind` 从壳文件搬到 `Views/Settings/SettingsTabKind.cs`(42)。业务拆 7 个文件到 `ViewModels/Settings/`：`SettingsContracts.cs`(142)、`SettingsWindowViewModel.cs`(101)、`SkillRuleTabViewModel.cs`(187)、`McpTabViewModel.cs`(123)、`ProviderModelTabViewModel.cs`(256)、`A2aTabViewModel.cs`(188)、`YunxiaoTabViewModel.cs`(51)；**VM 不引用任何 Avalonia 类型**（无 `Control`/`Brush`/`Dispatcher`）。
- R79（Provider 保存路径，用户拍板 **P1**）：设置页 Provider / Model 两页仍在 VM 内**直连 `ConfigManager`**（`AddProvider`/`AddCustomModel`/`RemoveCustomModel`/`UpdateCustomModel`/`SetSelectedModel`/`ClearSelectedModel`/`Save`），不改走 `IProviderModelManagementService`。理由：管理服务是 CLI 命令语义（`GetProvidersAsync` 返回深拷贝快照、`SetSelectionAsync` 必须先拆 `provider:model`），设置页是「配置整体编辑」语义（一次保存同时改 ApiKey/BaseUrl/DisplayName/超时/自定义模型集合，且依赖 `GetProvider` 返回**引用**后直接改字段），套一层要多一次全字段映射且语义不对齐。`ProviderModelTabViewModel` 只依赖 `IServiceProvider`，单测可注入临时路径 `ConfigManager`，可测性不受影响；Task 20 若要求统一走服务，改动点仅此一个文件。
- R80（分层契约）：分页 VM 统一返回 `SettingsOutcome(Ok, Key, Message)`；窗口只做三件事——把控件值采集为 record（`SkillForm`/`RuleForm`/`McpForm`/`ProviderForm`/`A2aServerForm`/`A2aClientForm`）、按 `Ok` 决定 `SetHint`/`SetSavedHint`、成功后 `RefreshItems + BuildEditor`。壳 VM 持有 `Tab`/`IsGlobal`/`SelectedWorkspace`/`SelectedItemKey` + 5 个分页 VM 实例，`EnumerateFileItems()` 按页签路由（Skill/Rule/MCP 返回文件 Id 列表，其余返回空）。
- R81（文案零漂移）：全部提示文案由 VM 产出，逐字沿用旧实现——新建 4 条（A2A 服务端/客户端、供应商草稿、`已新建条目，请填写内容后点击「保存」。`）、模型页 6 条操作提示（`请先选择模型。`/`已设为默认模型：…`/`名称不能为空。`/`已改名。`/`已删除自定义模型。`/`已设为默认：…`/`请选择供应商并填写模型名。`/`添加失败：…`/`已添加自定义模型。`）、删除 3 条、`SetSavedHint` 两条分支、`应用配置` 1 条、云效保存 1 条；窗口 `SetHint` 只负责 `OrangeRed` / `#8A8A8A` 着色。
- R82（原样保留的既有行为，同 R70 口径）：① Provider「＋新建」建草稿后 `RefreshItems` 回落选中首个真实 Provider（真实项排序在前），既有缺陷本次不修；② Model 页操作即时生效且隐藏新建/删除/保存；③ Yunxiao 页隐藏新建/删除、保留保存；④ A2A 页 `CanApply()` 恒 false；⑤ 云效删除兜底直接 `return`；⑥ A2A / Provider 删除成功走 `SetHint(Message)` 而非 `SetSavedHint()`；⑦ Provider 删除后若默认模型属于该 Provider 则 `ClearSelectedModel()`——**设置页原本就是正确实现**，与 R54 的 CLI 管理服务 `SetSelectedModel("")` 缺陷是两条独立路径，不要混改。
- R83（VM 单测，用户拍板补测）：新增 `LubanAgent.Tests/SettingsViewModelUnitTest.cs` **15 用例**——技能必填 / 字段修剪、规则 Id 必填 / 优先级解析、MCP 空 transport 兜底 `stdio` 与多行参数解析、文件来源删除传 `ManagedOrigin.File` + 作用域根、新建重名避让（`new-skill` → `new-skill-2`）、供应商 ApiKey 必填、供应商 upsert 与自定义模型**差异增删**（含二次保存清空 BaseUrl/DisplayName/超时）、列表 ApiKey 脱敏 + `✓默认` 标记、删除供应商联动清默认模型、A2A 新建服务端→客户端、A2A 无工作区拒绝、客户端 Name/Endpoint 必填、壳 VM 按页签路由。假实现 3 个（`ISkillManagementService`/`IRuleManagementService`/`IMcpManagementService`），Provider 页用临时路径 `ConfigManager` 验真实落盘语义。
- R84：`LubanAgent.Tests.csproj` 新增 `ProjectReference ..\LubanAgentCodex\LubanAgentCodex.csproj`。理由：VM 虽无 Avalonia 类型，但类型定义在 Codex 程序集内，单测必须引用；该工程 `OutputType=WinExe` 不影响 MSTest 以库方式加载。另注：VM 文件用到框架 `GlobalLubanAgentPath`（`LuBan.AIAgent` 根命名空间）与 `ProviderCatalog`（`LuBan.AIAgent.Configuration.Providers`），partial 文件不共享 using，须各自显式声明。
- R85：Task 19 验证——`LubanAgent.slnx` 构建退出码 **0**（0 错误）；`LubanAgent.Tests` **362 passed / 0 failed**（347 基线 + 15 新用例）；Task 19 **未改框架源码**，`LuBan.*` 版本仍为 `2026.10.20.1`。待人工冒烟 7 个 Tab（Skill / Rule / MCP / A2A / Provider / Model / Yunxiao 的新建、保存、删除、应用配置、提示文案）。
