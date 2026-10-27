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
| 13 | 9 服务接口骨架 | pending | |
| 14 | P1 模型 | pending | |
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
