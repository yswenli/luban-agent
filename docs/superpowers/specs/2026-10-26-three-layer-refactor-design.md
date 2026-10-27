# 三层职责重构设计（框架 / Core / UI）

- 日期：2026-10-26
- 状态：待评审（未批准前不动任何产品代码）
- 方案：**A —— 单包内收**（`LuBan.AIAgent` 一个包同时承载「能力」与「宿主」）
- 范围：一次性把三层搬到位，含框架发版与 Core / CLI / Codex 同步改造

---

## 1. 背景与问题

当前实际分层与设计意图相反：**框架薄、产品厚**。

| 层 | 代码量（近似） | 现状 |
|---|---|---|
| `luban-framework/LuBan.AIAgent` | — | 只提供工具、插件、规则、检索、编排等「零件」，没有「开箱即用的 Agent 宿主」 |
| `luban-agent/LubanAgentCore` | 13,774 行 | 承载了宿主生命周期、会话运行时、Provider/模型层、命令清单、UI 展示元数据 |
| `LubanAgentCli` | 13,343 行（其中 `Commands/` 4,338 行） | 11 个斜杠命令内含完整业务流程 |
| `LubanAgentCodex` | 11,331 行 | `SettingsWindow.axaml.cs` 1,848 行、`CodexPluginHost` 785 行、`CodexCredentialStore` 等业务逻辑直接写进 UI |

具体越界证据（本次体检确认）：

1. **宿主逻辑在 Core**：`Core/Services/AgentHostService.cs`（578 行）已经是一个完整的 Agent 宿主（初始化、会话运行时、流式对话、附件门禁、视觉能力探测回填），但它在产品程序集里，第三个 UI 只能复制。
2. **会话运行时在 Core**：`SessionRuntime.cs` / `SessionRuntimeManager.cs`（scope 创建、队列、空闲卸载）与框架的 `ISessionContext` / `ISessionManager` 契约脱节，只能靠 Core 的 `SessionContextState` 回填。
3. **展示元数据在 Core**：`Utils/ToolDisplayNames.cs`（225 行方法名→中文动作名映射）、`ToolArgsFormatter.cs`、`UiConstants.cs` 被 CLI 与 Codex 共用，但本质是 UI 语义。
4. **斜杠命令清单在 Core**：`Commands/CommandCatalog.cs` 里带 `SlashCommandClient { Cli, Codex, Both }` 标记，说明它本身就是跨 UI 共享元数据；而 11 个命令的**业务实现**却写在 CLI 里。
5. **UI 里写业务**：`LubanAgentCodex/Views/SettingsWindow.axaml.cs` 直接操作 Provider/插件/凭据；CLI 的每个命令自己拼 `ConfigManager` + Repository + 文件 IO。
6. **重复实现**：`FooterDataProvider` 两份（Core / Codex）；模型列表构建三处（`MainWindowViewModel:1147`、`InputBox:115`、`ModelCommand`）。

结果：新增第三个 UI（Web / A2A）必须复制一遍 Core 与 CLI 的一半代码；框架改一处，产品要跟着改三处。

---

## 2. 目标与非目标

### 2.1 目标

1. **框架几行代码可用**：`AddLuBanAgent(...)` + `AddLuBanAgentHost()` + `AddLuBanAgentOpenAI(configuration)` 之后，注入 `IAgentHost` 即可跑一轮带工具、带确认、带附件的流式对话。
2. **Core 只管「资料与扩展」**：模型文件（`EmbeddingModels/*.zip`）、ONNX 推理、用户目录与 `config.json`、SQLite schema 与 Repository、工作区管理、附件落盘、知识库/检索装配、云效连接器、自定义 Skill / Rule / MCP / Connector / 插件。
3. **UI 只做 UI**：渲染事件、收集输入、弹确认框、管理视图状态。UI 里不得出现 Repository、文件 IO、凭据读写、配置写入。
4. **框架厂商无关**：`LuBan.*` 源码不得出现 `Yunxiao` / `Codeup` / `AliyunDevOps` 字样。
5. **消除三处重复**：模型列表、Footer 数据、工具展示名，各只有一份实现。

### 2.2 非目标（本次不做）

- 不把 SQLite / ONNX / 向量库上收框架（框架只保留 `ISessionManager` 等契约 + 内存回落实现）。
- 不新增 `LuBan.AIAgent.Hosting` 独立包（方案 A 明确否决）。
- 不改 UI 视觉、不改交互流程、不做 UI 框架更换。
- 不重写 `NewProject` 插件业务；只把它依赖的宿主能力挪位。
- 不引入新的第三方依赖（`OpenAI` 版本钉死除外，见 §9）。

---

## 3. 目标架构

### 3.1 分层定义（唯一判定标准）

| 层 | 只允许出现 | 禁止出现 |
|---|---|---|
| `LuBan.AIAgent`（框架） | Agent 生命周期、会话运行时、Provider 路由、命令清单、工具展示元数据、契约接口、默认实现（内存版） | 厂商名、SQLite、ONNX、具体 UI 类型（Avalonia/Terminal.Gui）、用户目录布局 |
| `LubanAgentCore`（产品核心） | 持久化、配置、模型文件、工作区、知识库、厂商连接器、产品级 Profile、各 `*ManagementService` | 流式对话主循环、会话 scope 管理、UI 渲染 |
| `LubanAgentCli` / `LubanAgentCodex` | 视图、控件、输入、事件渲染、命令的「调服务 + 打印」 | Repository、文件 IO、凭据、配置写入、Provider 装配 |

判定口诀：**把这一行代码删掉，另一个 UI 会不会因此坏掉？会 → 不属于 UI。**

### 3.2 框架新增目录与类型

```
luban-framework/LuBan.AIAgent/
├── Hosting/
│   ├── IAgentHost.cs
│   ├── AgentHost.cs                     ← 由 Core/Services/AgentHostService.cs 迁入
│   ├── AgentRunRequest.cs               ← 新增：把 7 个散参数收成一个 record
│   ├── IAgentProfile.cs                 ← 由 Core/Agents/AgentProfile.cs 抽象
│   ├── AgentProfileBase.cs              ← 原 AgentProfile 的通用实现
│   ├── DefaultAgentProfile.cs           ← 产品中立的默认 Profile
│   ├── AgentStreamEvent.cs              ← 由 Core/Models/StreamEvent.cs 迁入（改名）
│   ├── IAgentModelSelection.cs          ← 新增：宿主读「当前选定模型」
│   ├── IAgentAttachmentOrchestrator.cs
│   ├── DefaultAgentAttachmentOrchestrator.cs
│   ├── IModelCapabilityGate.cs
│   ├── SessionRuntime.cs                ← 由 Core/Services/SessionRuntime.cs 迁入
│   ├── SessionRuntimeManager.cs         ← 由 Core/Services/SessionRuntimeManager.cs 迁入
│   ├── SessionRuntimeSeed.cs            ← 新增：会话 scope 初始化数据
│   ├── ISessionRuntimeInitializer.cs    ← 新增：宿主回填写侧状态
│   └── Commands/
│       ├── ICommandCatalog.cs           ← 由 Core/Commands 迁入
│       ├── CommandCatalog.cs
│       ├── SlashCommandDefinition.cs
│       └── SlashCommandClient.cs
├── Configuration/Providers/
│   ├── ProviderCatalog.cs               ← 由 Core/Configuration 迁入
│   ├── ProviderInfo.cs
│   ├── IProviderRouter.cs / DefaultProviderRouter.cs
│   ├── LuBanChatClient.cs               ← 由 Core/Configuration 迁入
│   └── ProviderDefaults.cs              ← 由 ProviderHelper 的静态表转实例服务
└── Abstractions/
    ├── ToolDisplayNames.cs              ← 由 Core/Utils 迁入
    └── ToolArgsFormatter.cs             ← 由 Core/Utils 迁入
```

### 3.3 关键新 API 形态

#### `IAgentHost`

```csharp
namespace LuBan.AIAgent.Hosting;

public interface IAgentHost
{
    bool IsInitialized { get; }
    IServiceProvider Services { get; }
    string? CurrentSessionId { get; }

    Task InitializeAsync(string sessionId, CancellationToken ct = default);
    void Reset(string? sessionId = null);

    IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(
        AgentRunRequest request,
        CancellationToken ct = default);
}
```

`AgentRunRequest` 收口现有 7 个散参数：

```csharp
public sealed record AgentRunRequest
{
    public required string Input { get; init; }
    public IReadOnlyList<string> AttachmentPaths { get; init; } = [];
    public string? SessionId { get; init; }
    public ToolPermissionMode PermissionMode { get; init; } = ToolPermissionMode.Normal;
    public Func<string, IReadOnlyDictionary<string, object?>, ConfirmResult>? ConfirmHandler { get; init; }
    public Action<string, IReadOnlyDictionary<string, object?>>? OnPlannedAction { get; init; }
    public Action<string, IReadOnlyDictionary<string, object?>>? OnUserDenied { get; init; }
}
```

> 不保留旧签名重载：`AgentHostService` 与 UI 在同一 commit 内一起改完（见 §10 阶段 2）。

#### `IAgentProfile` / `AgentProfileBase`

```csharp
public interface IAgentProfile
{
    string SystemPrompt { get; }
    string[]? ToolGroups { get; }       // null = 全部
    string? RetrievalMode { get; }      // null | "auto"
    Task<LuBanAgent> CreateAgentAsync(
        ILuBanAgentFactory factory,
        string modelName,
        WorkspaceDescriptor workspace,
        RuleEngine ruleEngine,
        ToolPluginRegistry pluginRegistry,
        SkillRegistry skillRegistry,
        MCPRegistry mcpRegistry,
        CancellationToken ct = default);
}
```

- `AgentProfileBase` 承接原 `AgentProfile` 的 `BuildToolOptions()`、`LoadWorkspaceAgentsMd`、`BuildFullPrompt(...)` 逻辑。
- `WorkspaceDescriptor` 是框架内的中立 record：`(string Id, string Name, string RootPath, string Type)`；Core 负责把 `WorkspaceInfo` 映射过来。

#### `IModelCapabilityGate`（附件模型门禁下沉为装饰器）

```csharp
public interface IModelCapabilityGate
{
    bool CanAttach(string modelId, IReadOnlyList<ProcessedAttachment> attachments);
    string GetUnsupportedMessage(string modelId, IReadOnlyList<ProcessedAttachment> attachments);
}
```

- 框架默认 `AllowAllModelCapabilityGate`。
- Core 注册 `VisionModelCapabilityGate`：读 `ConfigManager.SelectedModel` + `IModelCapabilityCache`，实现现有的视觉能力探测/回填策略。

#### `IAgentModelSelection`

```csharp
public interface IAgentModelSelection
{
    string? ProviderId { get; }
    string? ModelId { get; }
    string QualifiedModelName { get; }   // "provider:model"
}
```

- 框架 `DefaultAgentModelSelection` 从 `LuBanAgent:Models:Default` 读。
- Core 注册 `ConfigManager` 的适配器（`ConfigManager` 自身实现该接口，不额外包一层）。

#### 宿主 ↔ 产品的 4 个接缝（`AgentHost` 去产品依赖的关键）

`AgentHostService` 目前硬编码依赖 `IWorkspaceManager`、`SessionRepository`、`ConfigManager`、`LubanAgentCore.Plugins.IPluginSessionContributor`、`ConnectorRegistry`、`NormalAgentProfile`/`LlmWikiAgentProfile`。搬进框架必须换成这 4 个接口：

```csharp
/// 1) 解析「这个会话属于哪个工作区」（Core 读 SQLite + 工作区管理器）
public interface IAgentWorkspaceResolver
{
    Task<WorkspaceDescriptor> ResolveAsync(string sessionId, CancellationToken ct = default);
}

/// 2) 按工作区类型选 Profile（Core 决定 LlmWiki / Normal）
public interface IAgentProfileResolver
{
    IAgentProfile Resolve(WorkspaceDescriptor workspace);
}

/// 3) 会话级扩展点（Core 把插件贡献、连接器启用挂进来）
public interface IAgentSessionExtension
{
    Task ApplyAsync(IServiceProvider sessionServices, WorkspaceDescriptor workspace, CancellationToken ct = default);
}

/// 4) 会话身份（Core 的 UserContextState 适配；框架默认取 DefaultUserContext）
public interface IAgentUserResolver
{
    string ResolveUserId(IServiceProvider services);
}
```

- 框架提供默认实现：`SingleWorkspaceResolver`（取 `LuBanAgent:WorkspaceRoot`）、`DefaultAgentProfileResolver`（恒返回 `DefaultAgentProfile`）、空 `IAgentSessionExtension` 集合、`DefaultAgentUserResolver`（读 `IUserContext`，缺失回落 `"local"`）。
- Core 实现：`CoreWorkspaceResolver`（包 `SessionRepository` + `WorkspaceManager`）、`CoreAgentProfileResolver`（`LlmWiki` → `LlmWikiAgentProfile`，否则 `NormalAgentProfile`）、`PluginSessionExtension`/`ConnectorSessionExtension`（分别包 `IPluginSessionContributor` 与 `ConnectorRegistry`）、`CoreAgentUserResolver`（包 `LocalUserDefaults`）。

#### 会话作用域写侧接缝

```csharp
public sealed record SessionRuntimeSeed(
    string SessionId,
    string? WorkspaceId,
    string WorkspaceRoot,
    IReadOnlyList<string> AllowedRoots);

public interface ISessionRuntimeInitializer
{
    Task InitializeAsync(SessionRuntimeSeed seed, CancellationToken ct = default);
}
```

- `SessionRuntimeManager` 建好 scope 后，写入框架已有的 `Defaults/DefaultContextState`（`SessionId` / `WorkspaceRoot` / `AllowedRoots`），再 `GetServices<ISessionRuntimeInitializer>()` 逐个调用；Core 的实现只补产品字段（`WorkspaceId`、`UserId`、`TempDirectory`）。
- 默认 `NoOpSessionRuntimeInitializer` 已注册，不实现也能跑。

> **复用既有框架资产（重要）**：`LuBan.AIAgent.Defaults/` 下已经有 `DefaultContextState`、`DefaultSessionContext`、`DefaultWorkspaceContext`、`DefaultUserContext`、`InMemorySessionManager`、`InMemoryLocalMemoryStore`。
> 因此本次**不新建** `SessionContextState` / `SessionContext` / `InMemorySessionManager`；Core 的 `Context/SessionContext.cs` + `Context/SessionContextState.cs` 直接删除，改用框架 `DefaultContextState`（字段是 Core 版的超集，多一个 `UserId`）。
> 唯一行为差异：Core 版 `EffectiveRoot` 在工作区未设置时**抛异常**，框架 `DefaultSessionContext` 回退到 `LuBanAgent:WorkspaceRoot` → 进程当前目录。这条要写进冒烟清单（工作区未授权的场景不再崩）。

---

## 4. 依赖方向与生命周期约束

```
UI (Cli / Codex)
   ↓ 只依赖
LubanAgentCore (服务 + 实现)
   ↓ 只依赖
LuBan.AIAgent (契约 + 默认实现)
```

- Core **不得**被框架引用；框架**不得** `using LubanAgentCore.*`。
- 所有跨层交互只经 `LuBan.AIAgent` 内的接口。
- 生命周期（`ValidateScopes=true` + `ValidateOnBuild=true` 下必须成立）：
  - `IAgentHost`：**Scoped**。它注入的 `IServiceProvider` 是当前 scope，用来取 Scoped 的 `IAttachmentOrchestrator` / `IChatClient`。
  - `SessionRuntimeManager`：**Singleton**（内部用 `IServiceScopeFactory` 建会话 scope）。
  - `DefaultContextState` / `DefaultSessionContext`：**Scoped**（会话 scope 内唯一；框架 `Defaults/` 已有）。
  - `IAgentProfile`：**Scoped**（要读工作区）。
  - `IModelCapabilityGate` / `IAgentModelSelection`：**Scoped**。
  - 禁止 Singleton 消费 Scoped（现有 `PluginManager` / `BuiltinPluginCatalog` 的约束不变）。

---

## 5. 搬运清单（逐文件）

### 5.1 Core → 框架（移动 + 改名）

| 现位置 | 目标位置 | 动作 | 风险 |
|---|---|---|---|
| `Core/Services/AgentHostService.cs` | `Hosting/AgentHost.cs` | 去产品依赖：`ConfigManager`→`IAgentModelSelection`；`AttachmentService`→`IAgentAttachmentOrchestrator`；`SessionContextState` 用框架版 | 中（循环依赖风险） |
| `Core/Services/SessionRuntime.cs` | `Hosting/SessionRuntime.cs` | 仅替换命名空间 + 用 `SessionRuntimeSeed` | 低 |
| `Core/Services/SessionRuntimeManager.cs` | `Hosting/SessionRuntimeManager.cs` | 同上；新增 `ISessionRuntimeInitializer` 回填 | 中 |
| `Core/Models/StreamEvent.cs` | `Hosting/AgentStreamEvent.cs` | 类型改名 `StreamEvent`→`AgentStreamEvent`；两端全量替换（不留别名，record 无法别名） | 低（编译期全量替换，漏一处就编译失败） |
| `Core/Agents/AgentProfile.cs` | `Hosting/IAgentProfile.cs` + `AgentProfileBase.cs` | 抽象接口 + 通用基类；`WorkspaceInfo`→`WorkspaceDescriptor` | 中 |
| `Core/Commands/`（4 文件） | `Hosting/Commands/` | 原样搬 + 命名空间替换 | 低 |
| `Core/Configuration/ProviderCatalog.cs` | `Configuration/Providers/ProviderCatalog.cs` | 原样搬 | 低 |
| `Core/Configuration/LuBanChatClient.cs` | `Configuration/Providers/LuBanChatClient.cs` | 原样搬 | 低 |
| `Core/Configuration/ProviderHelper.cs` | `Configuration/Providers/ProviderDefaults.cs` | 静态类 → Singleton 服务（去静态可变状态） | 中 |
| `Core/Utils/ToolDisplayNames.cs` | `Abstractions/ToolDisplayNames.cs` | 原样搬 | 低 |
| `Core/Utils/ToolArgsFormatter.cs` | `Abstractions/ToolArgsFormatter.cs` | 原样搬 | 低 |
| `Core/Context/SessionContextState.cs`、`SessionContext.cs` | **删除**（不搬） | 用框架 `Defaults/DefaultContextState` + `DefaultSessionContext` 替代 | 低（字段是超集） |

### 5.2 留在 Core（明确不动）

- `EmbeddingModels/*.zip`、`ModelManager`、ONNX 推理、模型下载/校验。
- `config.json` 读写、用户目录布局（`.luban-agent/`）、`ConfigManager`。
- SQLite：全部 `*Repository`、schema、迁移、`SqliteVectorStore`。
- `WorkspaceManager`、`SessionManager`（实现框架 `ISessionManager`）、`AttachmentService` 的落盘/缩略图部分。
- 云效连接器（`Connectors/AliyunDevOps/**`）、`AliyunCredentialKeyAliases`。
- `CodexPluginHost`、`CodexCredentialStore`、`PluginBootstrap`、`PluginMcpCatalog` —— **从 Codex 工程搬进 Core**（见 §6）。
- `NormalAgentProfile` / `LlmWikiAgentProfile`（改为继承框架 `AgentProfileBase`）。
- 知识库、检索、`Vault`/`Wiki` 装配。
- `AgentHostBuilder`（缩为组装层：只做 DI 注册与初始化，不含流程逻辑）。

### 5.3 新增（框架侧）

- `IAgentHost` / `AgentHost` / `AgentRunRequest`
- `IAgentProfile` / `AgentProfileBase` / `DefaultAgentProfile`
- `IAgentModelSelection` / `DefaultAgentModelSelection`
- `IAgentAttachmentOrchestrator` / `DefaultAgentAttachmentOrchestrator`
- `IModelCapabilityGate` / `AllowAllModelCapabilityGate`
- `SessionRuntimeSeed` / `ISessionRuntimeInitializer` / `NoOpSessionRuntimeInitializer`
- `LuBanAgentHostExtensions.AddLuBanAgentHost(...)`
- `LuBanAgentOpenAIExtensions.AddLuBanAgentOpenAI(configuration)`

> 复用既有：`Defaults/DefaultContextState`、`Defaults/DefaultSessionContext`、`Defaults/InMemorySessionManager`（均已存在，`AddLuBanAgentDefaults` 已注册）。

### 5.4 新增（Core 侧共享服务）

UI 里现有的业务逻辑全部收进这些服务，两个 UI 只调它们：

| 服务 | 从哪来 | 职责 |
|---|---|---|
| `SkillManagementService` | CLI `SkillCommand` + Codex 设置页 | 列出/启用/禁用/导入/删除 Skill |
| `RuleManagementService` | CLI `RuleCommand` + Codex 设置页 | 列出/增删/启用 Rule |
| `McpManagementService` | CLI `McpCommand` + Codex 设置页 | 列出/增删/测试连接 MCP |
| `ConnectorManagementService` | CLI `ConnectorCommand` + Codex 设置页 | 连接器 CRUD、凭据写入、连通性测试 |
| `PluginManagementService` | `CodexPluginHost`（785 行） | 插件安装/卸载/启用/权限 |
| `WorkspaceManagementService` | CLI `WorkspaceCommand` + Codex 设置页 | 工作区 CRUD、授权、切换 |
| `LlmWikiManagementService` | CLI `WikiCommand` + Codex 设置页 | wiki 索引重建、状态、清理 |
| `ProviderModelManagementService` | CLI `ModelCommand` + Codex 设置页 | Provider/模型 CRUD、模型列表刷新（**唯一实现**） |
| `ChatSessionService` | CLI `SessionCommand` + Codex 会话列表 | 会话 CRUD、历史消息、标题、导出 |
| `CredentialStore` | Codex `Security/CodexCredentialStore.cs` | 凭据读写。抽 `ICredentialProtector`：Windows → DPAPI 实现；其他平台 → 占位实现（读写失败时明确报错，不静默降级为明文） |

---

## 6. UI 层收敛

### 6.1 CLI

- `Commands/*` 每个命令降到「解析参数 → 调 `*ManagementService` → 渲染」。目标：`Commands/` 从 4,338 行降到 ≤ 1,500 行。
- 渲染辅助（表格、颜色）保留在 CLI，抽到 `Cli/Rendering/`。
- 删除 CLI 内自建的 Provider/模型列表构建，改用 `ProviderModelManagementService`。

### 6.2 Codex

- `SettingsWindow.axaml.cs` 从 1,848 行拆成 `SettingsWindow.axaml.cs`（≤ 300 行，纯 VM 绑定）+ 各 Tab 的 ViewModel。
- `CodexPluginHost`（785 行）→ Core `PluginManagementService`；Codex 只保留一个薄适配器（把 UI 回调接到服务事件）。
- `CodexCredentialStore` → Core `CredentialStore`，Codex 通过接口调用。
- `FooterDataProvider`（Codex 版）删除，统一用框架/Core 版本。

### 6.3 展示策略下沉

- `UiConstants` 一分为二：CLI 的留在 `LubanAgentCli`，Codex 的留在 `LubanAgentCodex`。
- 通用的「工具中文展示名」「参数摘要格式」上收框架 `Abstractions/`（两端共用一份）。
- 若两端对同一工具想显示不同文案，框架 `ToolDisplayNames` 提供 `WithOverride(...)` 扩展点，而不是各自复制一张表。

---

## 7. DI 装配变更

### 7.1 框架侧

```csharp
// 能力
services.AddLuBanAgent(configuration);

// 宿主（新增）
services.AddLuBanAgentHost(options =>
{
    options.IdleUnloadMinutes = 30;      // 空闲会话卸载
    options.MaxConcurrentRuns = 1;       // 单会话串行
});

// 模型（新增，二选一）
services.AddLuBanAgentOpenAI(configuration);            // 读 LuBanAgent:Models
// 或
services.AddLuBanAgent(sp => myIChatClient);             // 现有方式，不变
```

`AddLuBanAgentOpenAI` 行为：

1. 读 `LuBanAgent:Models`（`Providers[]` + `Default`）。
2. 建 `OpenAIClient`（按 Provider 的 `BaseUrl` / `ApiKey`），包成 `IChatClient`。
3. 注册 `IProviderRouter` 默认实现 + `ProviderCatalog`。
4. 配置缺失时**不注册** `IChatClient`（让 `AddLuBanAgent(chatClientFactory)` 继续可用），并在日志给一条 warning。

`AddLuBanAgentHost` 行为：

- `IAgentHost` → `AgentHost`（Scoped）
- `SessionRuntimeManager`（Singleton）
- `IAgentProfile` → `DefaultAgentProfile`（`TryAdd`）
- 会话上下文与 `ISessionManager` 回落到 `AddLuBanAgentDefaults` 已有实现（`DefaultContextState` / `DefaultSessionContext` / `InMemorySessionManager`），Core 用自己的注册覆盖
- `IAgentAttachmentOrchestrator` → `DefaultAgentAttachmentOrchestrator`（`TryAdd`）
- `IModelCapabilityGate` → `AllowAllModelCapabilityGate`（`TryAdd`）
- `ISessionRuntimeInitializer` → `NoOpSessionRuntimeInitializer`（`TryAdd`）

### 7.2 Core 侧（`AgentHostBuilder` 缩为组装）

保留顺序：

1. 配置 + 日志 + SQLite Repository
2. `AddLuBanAgent` + `AddLuBanAgentHost` + `AddLuBanAgentOpenAI`
3. 产品注册：`SessionManager`（覆盖 `ISessionManager`）、`WorkspaceManager`、`ConfigManager`（兼 `IAgentModelSelection` / `IAppConfigReader` / `IConnectorConfigWriter`）、`VisionModelCapabilityGate`、`CoreSessionRuntimeInitializer`、各 `*ManagementService`
4. `AddAliyunDevOpsConnector(configuration)`
5. 可选：知识库/检索（`EnableKnowledgeBase`）

`AgentHostBuilder` 里**不再出现** `while` 循环、事件分支、附件判定等流程代码。

---

## 8. 插件 ALC 共享集更新

新增宿主类型全部在 `LuBan.AIAgent` 程序集内，`PluginAssemblySharing.ExactNames` 已含 `LuBan.AIAgent`，**无需新增**。

需要新增的是产品侧（若搬入 Core 后仍有独立程序集）：

- `LubanAgentCore`（若此前靠 `LubanAgent.` 前缀命中，则保持不变）

结论：**本次不改 `PluginAssemblySharing` 必备集**；仅在 Core 程序集名变化时才追加 `AdditionalExactNames`。这条写进验收：插件卸载后 `WeakReference` 必须可回收（沿用现有 `PluginAssemblySharing` 的单测）。

---

## 9. 依赖与版本

| 项 | 处理 |
|---|---|
| `OpenAI` | 框架 `LuBan.AIAgent.csproj` **显式钉 `2.14.0`**。原因：`Microsoft.Extensions.AI.OpenAI 10.11.0` 传递引入 `2.13.0`，而 `2.13.0` 有「流式工具调用丢 chunk」的破坏性变更，CLI 已因此钉 `2.14.0`。框架自建 `OpenAIClient` 若仍吃 `2.13.0`，两端行为不一致。 |
| `Microsoft.Extensions.AI` / `.OpenAI` | 保持 `10.11.0`，不升 `10.10.0` 以下（现有注释约束）。 |
| 框架包版本 | 开发期**不发版**；全部通过 `ProjectReference` 直连。推送前由人工发版，再改回 `PackageReference`（见 §9.1）。 |
| 三方包下放 | 框架若提升 `Microsoft.Extensions.*`，agent 侧**不得**再显式声明同名低版本（否则 `NU1605`）。本次不新增 agent 侧显式包。 |

### 9.1 开发验证期依赖方式（已定：ProjectReference 直连，人工发版收尾）

沿用仓库历史上的 `TEMP-LOCAL-VERIFY` 做法，但**方向明确、收尾有检查**：

1. **开发期**：`LubanAgentCore` / `LubanAgentA2A` / `LubanAgentCli` / `LubanAgentCodex` / `LubanAgent.Tests` 的 `LuBan.*` 引用临时改为 `<ProjectReference>`，注释统一标记：
   ```xml
   <!-- TEMP-LOCAL-VERIFY: 三层重构开发期直连框架源码，推送前必须改回 PackageReference -->
   <ProjectReference Include="..\..\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj" />
   ```
2. **原因**：改框架不即时生效的问题会严重拖慢本次搬运（一轮改动要发一次包）。直连后框架改动秒级生效，且能避免「框架编译通过、产品没验证」的错配。
3. **收尾**：全部验证通过、准备推送时
   - 由**人工**执行框架发版（`GeneratePackageOnBuild` 已开，人工确认版本号 `2026.10.26.1` 并推 `nexus3`）；
   - 把 5 个工程的 `ProjectReference` 全部改回 `PackageReference`，版本统一 `2026.10.26.1`；
   - `dotnet build luban-agent/LubanAgent.slnx` 重新验证（这次走真实包路径）；
   - 删除全部 `TEMP-LOCAL-VERIFY` 标记。
4. **门禁**：`grep -r "TEMP-LOCAL-VERIFY" luban-agent --include=*.csproj` 必须为 0 才能推送。

> `luban-agent/smoke/plugin-system/**` 的既有 `ProjectReference` 是**长期例外**，不参与本次收尾清理。

---

## 10. 迁移步骤（每步可编译、可回滚）

**阶段 0：环境准备（0.5 天）**

0. agent 侧 5 个工程的 `LuBan.*` 引用改为 `ProjectReference`，打 `TEMP-LOCAL-VERIFY` 标记；`dotnet build luban-agent/LubanAgent.slnx` 先验证「直连基线」可编译可运行。

**阶段 1：框架内建（不动产品行为）**

1. 框架新增 `Hosting/`、`Configuration/Providers/`、`Abstractions/` 类型；从 Core **复制**实现（此时 Core 保持原样，两边并存）。
2. 框架新增 `AddLuBanAgentHost` / `AddLuBanAgentOpenAI` / `ICredentialProtector`；补框架单测（内存 `ISessionManager`、`AgentHost` 一轮假模型对话、`CommandCatalog` 匹配）。
3. `WebApplication1` 加一个「最小 Agent」示例端点：只三行注册 + 注入 `IAgentHost` + 跑一轮「读文件 + 回答」。**这是「框架几行代码可用」的硬证明。**

**阶段 2：Core 切到框架宿主（产品行为不变）**

4. Core 删除已搬走的文件；`AgentHostService` → 别名转发到 `IAgentHost`（一版过渡）。
5. Core 注册：`SessionManager`、`VisionModelCapabilityGate`、`CoreSessionRuntimeInitializer`、`ConfigManager: IAgentModelSelection`。
6. 两端 UI 改为注入 `IAgentHost`（**类型名替换，逻辑不动**）。
7. 全量构建 + 冒烟：TUI 一轮对话、Codex 一轮对话、带附件一轮、带工具确认一轮。

**阶段 3：业务下沉到 Core 服务（一次性全改，按隔离度并行）**

8. 一次性建齐 9 个 `*ManagementService` + `ICredentialProtector` / `CredentialStore`，并按下面的**隔离度分组**派给多个 subagent 并行执行（见 §10.1）。
9. 同步把 CLI 11 个命令、Codex 设置页改为调用服务。**CLI 只搬不改：命令参数、输出文案、提示语、退出码全部保持现状。**
10. 删 `CodexPluginHost`、Codex 版 `FooterDataProvider`、三处模型列表构建。
11. `LubanAgent.Plugin.NewProject` **不动**：只确认它能用新宿主正常跑。

**阶段 4：收尾**

12. 全仓 `rg "\bStreamEvent\b"` 确认 0 命中；确认没有遗留的 `AgentHostService` 引用。
13. 更新 `LuBan.AIAgent/README.md` / `README.en.md`（中英同步）、`AGENTS.md` 分层表、CLI/Codex 冒烟文档。
14. 人工发版 → 改回 `PackageReference` → 走包路径复验 → 删 `TEMP-LOCAL-VERIFY`。

**顺序理由**：先让框架「能跑」，再让产品「用框架」，最后才「删旧代码」。任何一步失败都能停在可编译状态。

### 10.1 阶段 3 的并行分组（按隔离度切）

判定「隔离度高」的标准：**改的文件集合不重叠、依赖的服务不互为前置**。

| 组 | 内容 | 独占文件 | 依赖 |
|---|---|---|---|
| P1 | `ProviderModelManagementService` + `FooterDataProvider` 合并 + 三处模型列表统一 | `Core/Services/ProviderModelManagementService.cs`、`Cli/Commands/ModelCommand.cs`、`Codex/**` 模型列表相关 | 无 |
| P2 | `SkillManagementService` + `RuleManagementService` | `Core/Services/Skill*`、`Rule*`、`Cli/Commands/SkillCommand.cs`、`RuleCommand.cs` | 无 |
| P3 | `McpManagementService` + `ConnectorManagementService` | `Core/Services/Mcp*`、`Connector*`、`Cli/Commands/McpCommand.cs`、`ConnectorCommand.cs` | 无 |
| P4 | `WorkspaceManagementService` + `ChatSessionService` + `LlmWikiManagementService` | `Core/Services/Workspace*`、`ChatSession*`、`LlmWiki*`、对应 3 个 CLI 命令 | 无 |
| P5 | `PluginManagementService` + `ICredentialProtector` + `CredentialStore`（含 DPAPI 实现 + 非 Windows 占位实现） | `Core/Services/PluginManagement*`、`Core/Security/*`、`Codex/Plugins/**`、`Codex/Security/**` | 无 |
| P6 | Codex `SettingsWindow` 拆 ViewModel（消费 P1–P5 的服务接口，**先按接口编译，不等实现**） | `Codex/Views/SettingsWindow*`、新建 `Codex/ViewModels/Settings/*` | 依赖 P1–P5 **接口先落地** |

并行规则：

- P1–P5 **完全并行**，各自独占文件，互不修改对方文件。
- 先把 P1–P5 的**接口骨架**（只有 `interface` + DI 注册，方法体 `throw new NotImplementedException()`）一次性落地并编译通过，然后 P1–P5 + P6 才允许并行开工。这是唯一的前置同步点。
- P6 在接口骨架落地后即可开工，不阻塞于实现。
- 每个 subagent 的验收：自己那组 `dotnet build` 通过 + 自己那条冒烟路径跑通；**不得**顺手改别人的文件。

---

## 11. 回滚

| 阶段 | 回滚方式 | 代价 |
|---|---|---|
| 阶段 0 | 改回 `PackageReference` | 零 |
| 阶段 1 | 删除框架新文件（Core 未动，天然隔离） | 零 |
| 阶段 2 | `git revert` 单次 commit（Core 删文件 + 改注册在同一 commit） | 低 |
| 阶段 3 | 按 P1–P5 分组回滚：每组独占文件，revert 单组不影响其他组 | 低 |
| 阶段 4 | 不适用（只做检查与文档） | 零 |

关键约束：

1. **阶段 2 的「Core 删文件」必须与「改注册 + UI 类型替换」在同一个 commit**，否则中间态不可编译。
2. **阶段 3 的并行分组必须真的独占文件**，否则 revert 会互相拖累 —— 派发 subagent 前先按 §10.1 核对文件集合无交集。

---

## 12. 验收标准

### 12.1 硬指标

- [ ] `dotnet build luban-framework/LuBan.FrameWork.sln` 0 error。
- [ ] `dotnet build luban-agent/LubanAgent.slnx` 0 error。
- [ ] `dotnet test luban-agent/LubanAgent.Tests` 全绿。
- [ ] `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj` 全绿（环境类失败除外）。
- [ ] `LuBan.AIAgent/**` 全文搜索 `Yunxiao|Codeup|AliyunDevOps` 命中 0。
- [ ] `LuBan.AIAgent/**` 不引用 `Avalonia*`、`Terminal.Gui`（`csproj` 检查）。
- [ ] `LubanAgentCore/**` 不引用 `Avalonia*`、`Terminal.Gui`（现有依赖保持）。
- [ ] `LubanAgentCli/Commands/**` 无 `Repository`、无 `File.Write*`、无 `IConfiguration` 直接写入。
- [ ] `LubanAgentCodex/**` 除 `Security/` 内 `ICredentialProtector` 实现外无 `File.*`、无 `Repository` 直接调用。
- [ ] `grep -r "TEMP-LOCAL-VERIFY" luban-agent --include=*.csproj` 命中 0（除 `smoke/plugin-system/**`）。

### 12.2 行数目标（不作为门禁，作为体检）

| 文件/目录 | 现在 | 目标 |
|---|---|---|
| `LubanAgentCli/Commands/` | 4,338 | ≤ 1,500 |
| `LubanAgentCodex/Views/SettingsWindow.axaml.cs` | 1,848 | ≤ 300 |
| `CodexPluginHost.cs` | 785 | 删除（Core 承接） |
| `LubanAgentCore` 总量 | 13,774 | 减少 ≥ 2,500（搬走的部分） |

### 12.3 行为验收（手动冒烟）

1. **框架最小可用**：在现有 `luban-framework/WebApplication1` 里加一个「最小 Agent」端点 —— 只加 `AddLuBanAgent` + `AddLuBanAgentHost` + `AddLuBanAgentOpenAI`，注入 `IAgentHost`，能跑一轮「读文件 + 回答」。这是本次「框架几行代码可用」的**唯一硬证明**（不新建示例工程）。
2. TUI：`/model` 切换、`/skill`、`/mcp`、`/connector`、`/workspace`、`/session`、`/rule`、`/wiki` 各跑一遍。
3. Codex：设置页 6 个 Tab 全操作一遍；插件安装/卸载后，卸载的插件 `WeakReference` 可回收。
4. 两端同时带附件（图片 + 文本）+ 工具确认 + Plan 模式各一轮，事件序列与改造前一致。

---

## 13. 决定清单（D1–D8 设计者拍板，U1–U6 用户已确认）

| # | 决定 | 来源 | 理由 |
|---|---|---|---|
| D1 | `IAgentHost` 保持 **Scoped**，`SessionRuntimeManager` 为 Singleton | 设计者 | 现有 `AgentHostService` 就是 Scoped 且注入 Scope 内 `IServiceProvider`；改成 Singleton 会让 `GetRequiredService<IAttachmentOrchestrator>()` 抛 `ValidateScopes` 异常 |
| D2 | `StreamEvent` → `AgentStreamEvent` **一次性全量重命名，不留别名** | 设计者 | `StreamEvent` 是 `record` 继承体系，无法用「别名类型」兼容（派生 record 无法同时继承两个基类）；全量替换是机械操作，一次做完比留别名更干净 |
| D3 | 附件门禁做成 `IModelCapabilityGate` 装饰器，`IAttachmentProcessor` 留在框架 | 设计者 | 能力探测依赖产品的模型缓存与配置，不能进框架 |
| D4 | 框架新增 `WorkspaceDescriptor` record，替代 Core `WorkspaceInfo` | 设计者 | 框架不能依赖产品模型；`WorkspaceInfo` 保留在 Core 做映射 |
| D5 | `ProviderHelper` 静态类改为 Singleton 服务，去掉静态可变字段 | 设计者 | 静态 `Dictionary` + `_initialized` 在多用户/多工作区下是隐患 |
| D6 | 框架显式钉 `OpenAI 2.14.0` | 设计者 | 避免框架自建客户端吃 `2.13.0` 的流式工具调用 bug |
| D7 | 9 个 `*ManagementService` 一次性建齐，不做「先建两个再说」 | 设计者 | 只建一部分会让 UI 同时依赖新旧两套，反而更难删 |
| D8 | 不改 `PluginAssemblySharing` 必备集 | 设计者 | 新类型都在 `LuBan.AIAgent` 程序集内，已被覆盖 |
| U1 | 开发期用 `ProjectReference` 直连框架；推送前等**人工发版**再改回 `PackageReference` | 用户 | 避免每改一次框架就要发一次包（详见 §9.1） |
| U2 | 抽 `ICredentialProtector`：Windows 用 DPAPI，其他平台用占位实现 | 用户 | 为未来非 Windows 宿主留口子 |
| U3 | CLI 命令**只搬不改**：参数、文案、退出码一律不动 | 用户 | 把重构风险压到最低，文案统一另开需求 |
| U4 | `LubanAgent.Plugin.NewProject` **不动**，只保证能用新宿主 | 用户 | 控制本次范围 |
| U5 | 框架最小可用示例放现有 `WebApplication1` | 用户 | 不新增工程 |
| U6 | 阶段 3 一次性全改，拆成高隔离度 task，多 subagent 并行 | 用户 | 压缩周期；隔离度分组见 §10.1 |

---

## 15. 风险登记

| 风险 | 影响 | 缓解 |
|---|---|---|
| 阶段 1 删文件与改注册不同步 → 中间态编译失败 | 阻塞所有人 | 同一 commit；本地先跑通再提交 |
| `AgentHost` 去产品依赖时漏掉隐式依赖（如 `ConfigManager.SelectedModel`） | 运行期 `NullReference` | 用 `IAgentModelSelection` 接口 + 单测覆盖「无模型选中」分支 |
| `AgentStreamEvent` 重命名漏改导致编译失败 | 阻塞构建 | 全量 `rg` 替换 + 编译器兜底（`switch` 用 `is` 模式，漏改即报错） |
| 9 个 `*ManagementService` 一次性开发，行为回归面大 | 冒烟成本高 | 每个服务配套「改造前/后事件序列一致」的对比冒烟脚本 |
| Codex `SettingsWindow` 拆 ViewModel 引入绑定 bug | 设置页不可用 | 拆之前先补 Avalonia 绑定编译检查（`AvaloniaUseCompiledBindingsByDefault`） |
| 框架发版失败/私有源不可用 | 产品无法构建 | 开发期走 `ProjectReference` 不受影响；收尾发版由人工执行，本地先 `dotnet pack` 验证再推 `nexus3` |
| 阶段 3 并行 subagent 互相改到对方文件 | 冲突、回滚困难 | §10.1 分组前先核对文件集合无交集；接口骨架先落地并编译通过再并行 |
| CLI「只搬不改」时顺手改了文案 | 用户感知行为变化 | 命令类改动只允许「删业务代码 + 改调用目标」，`Render*`/字符串常量禁止修改 |

---

## 附录 A：本设计依赖的既有事实

- `ISessionContext` 现有成员：`SessionId`、`EffectiveRoot`、`ActiveWorktree`、`AllowedRoots`、`ResolveTempDirectory()`、`IsWithinEffectiveRoot(path)`。
- `ISessionManager`（框架）已定义 `SessionInfo` / `SessionMessage` / `SessionStats`。
- `LuBan.AIAgent.Defaults/` 已提供 `DefaultContextState`（含 `SessionId`/`WorkspaceId`/`WorkspaceRoot`/`UserId`/`ActiveWorktree`/`AllowedRoots`/`TempDirectory`）、`DefaultSessionContext`、`InMemorySessionManager`；`AddLuBanAgentDefaults` 三行即可注册最小宿主。
- `TempDirectory.Push(...)` 在 `LuBan.Common`，框架可直接用。
- `CommandCatalog` 现有 `SlashCommandClient { Cli, Codex, Both }`，命令清单本身是跨 UI 元数据。
- `ToolAttribute` 目前只有 `Name`，无展示名；`ToolDisplayNames` 是方法名 → 中文动作名映射。
- `PluginAssemblySharing.ExactNames` 已含 `LuBan.AIAgent`。
- 框架当前经 `Microsoft.Extensions.AI.OpenAI 10.11.0` 传递引入 `OpenAI 2.13.0`。