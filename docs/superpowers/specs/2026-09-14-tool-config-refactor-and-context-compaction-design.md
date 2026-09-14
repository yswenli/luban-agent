# 工具配置重构 & 上下文压缩设计

**日期**: 2026-09-14 | **状态**: 设计完成，待审批

---

## 1. 背景与目标

目前 luban-agent 的工具配置全部写死在 `appsettings.json` 的 `LuBanAgent:Tools` 节中（Browser、FileSystem、Script、Web、Retrieval、LocalMemory 六组）。每次新增工具或调整参数都需改配置文件，不符合工具按 Agent 类型/工作区模式注册的架构目标。

目标：
1. 工具注册由 `AgentProfile` 派生类硬编码控制（免配置文件），framework 支持运行期工具参数注入
2. 新增 `CompactContext` 工具（LLM 可直接调用压缩对话上下文）
3. 清理 commit `972c862` 移除 Database/Redis 工具后的残留包引用、global usings 和过时描述
4. 修复所有编译警告
5. 更新中英文 README 并推送

---

## 2. 决策记录

| # | 问题 | 选择 | 说明 |
|---|------|------|------|
| Q1 | 压缩工具如何注册 | **A: LLM 可见工具** | `CompactContextAsync` 作为 AIFunction，Agent 可自行调用 |
| Q2 | 工具配置来源 | **A: 移除 appsettings `Tools` 整节** | 彻底免配置，Profile 硬编码 |
| Q3 | 改造层级 | **A: 改 framework** | 框架层提供运行期参数注入通道，需重打包 |
| Q4 | Profile 配置存放 | **C: 硬编码在 Profile 派生类** | 不落盘 |
| Q5 | 按模式差异化 | **B: 权限模式机制不变，按要求新增** | ToolPermissionMode |
| Q6 | 工具注册策略 | **B: 仅新增压缩工具** | 其余维持全注册 + 调用拦截 |
| Q7 | 压缩工具组名 | **A: 独立组 `context`** | RagAgentProfile 白名单追加此组 |
| Q8 | 放行策略 | **A: 加入 ReadOnlyTools** | 所有模式自动放行 |
| Q10 | 清理范围 | **A: 各遗留项齐全** | |
| Q11 | MCP 友好名 | **D: 保持现状** | 不处理 |
| Q12/Q13 | 子参数差异化 + 注入 | **C; B; B-i** | 少数子参数需 Profile 级硬编码；改 framework 工厂参数注入；GetTools 签名加参数 |
| Q14 | GetTools 签名变更方式 | **A (B-i)** | 改 `ILuBanToolPlugin` 接口 |

---

## 3. 架构设计（Part 1: Framework 层）

> 变更后需**重新打包 NuGet** 供 agent 消费。

### 3.1 ILuBanToolPlugin 接口变更

```diff
// LuBan.AIAgent/Abstractions/ILuBanToolPlugin.cs
public interface ILuBanToolPlugin
{
    string GroupName { get; }
    string? Description { get; }
-   IReadOnlyList<AIFunction> GetTools(IServiceProvider sp);
+   IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null);
    bool IsEnabled(LuBanAgentOptions options);
}
```

波及 8 个 plugin 实现，其中 5 个需要实际接收 toolsOptions 覆盖子参数：

| Plugin | GetTools 内读 options | 需改逻辑 |
|--------|----------------------|----------|
| Browser | `BrowserToolOptions` 已存字段，但 GetTools 内未用（由 `PlaywrightSession` 读） | **仅签名兼容** |
| FileSystem | `FileSystemToolOptions` 已存字段，GetTools 内未用 | **仅签名兼容** |
| Script | `ScriptToolOptions`（Shell/PythonPath/LuaPath/Timeout） | **需覆盖** |
| Web | `WebToolOptions` 传入 `WebToolGroup` | **需覆盖** |
| Retrieval | `RetrievalToolOptions`（ModelId/TopK/MaxFileSize 等）传入 `RetrievalToolGroup` | **需覆盖** |
| LocalMemory | `LocalMemoryOptions` 传入 `LocalMemoryToolGroup` | **需覆盖** |
| Orchestration | 不读 Tools 子节点 | 仅签名兼容 |
| MCP | 不读 Tools 子节点 | 仅签名兼容 |

覆盖逻辑：若 `toolsOptions != null`，则取 `toolsOptions.XXGroupOptions` 覆盖缓存的默认值；若 null，保持现有行为。

### 3.2 LuBanAgentFactory 参数扩展

```diff
// LuBanAgentFactory.cs
public Task<LuBanAgent> CreateAsync(
    string? modelName = null,
    string? systemPrompt = null,
    IEnumerable<string>? toolGroups = null,
    string? retrievalMode = null,
    bool useSessionHistory = false,
+   ToolGroupOptions? toolsOptions = null,
    CancellationToken cancellationToken = default)

public Task<LuBanAgent> CreateSubAgentAsync(
    string? modelName,
    IEnumerable<string>? toolGroups,
    string systemPrompt,
+   ToolGroupOptions? toolsOptions = null,
    CancellationToken cancellationToken = default)
```

`BuildTools(toolGroups, toolsOptions)` 将 `toolsOptions` 逐 plugin 传入 `GetTools(sp, toolsOptions)`。

### 3.3 新增 CompactContextToolPlugin

**文件**: `LuBan.AIAgent/Tools/Context/CompactContextToolPlugin.cs`
**GroupName**: `context`
**注册**: `AddSingleton<ILuBanToolPlugin, CompactContextToolPlugin>()`（在 `LuBanAgentExtensions` 中）

**依赖**:
- `IServiceScopeFactory`（构造注入，用于调用时创建 scope 获取 scoped `IChatClient`）
- `IOptions<LuBanAgentOptions>`（取 `Session.CompactTargetMessages`、`Session.CompactThreshold`）
- `ISessionManager`（从 `GetTools(sp)` 的 `sp` 解析，Singleton）
- `IToolConfirmationService`（从 `sp` 解析）

**AIFunction**: `CompactContextAsync(int? targetCount = null)`
- `targetCount` 为 null 时使用 `Session.CompactTargetMessages`（默认 20）
- 执行流程：
  1. 获取 `ISessionManager.CurrentSession?.SessionId`，为空返回失败
  2. 获取 `ISessionManager.GetActiveMessagesAsync(sessionId)`
  3. 分离摘要/正文消息（复用 `SessionChatHistoryProvider` 逻辑）
  4. `using var scope = _scopeFactory.CreateScope()` → 获取 scoped `IChatClient`
  5. 调用 `SummarizingChatReducer.ReduceAsync` 压缩
  6. `MarkMessagesCompactedAsync` + `AddMessageAsync("summary", ...)` 持久化
  7. 返回新旧摘要 + 压缩统计 JSON

### 3.4 ReadOnlyTools 默认集追加

```diff
// ToolConfirmationService.cs
private static readonly string[] DefaultReadOnlyTools =
[
    "ReadFileAsync", "ListDirectoryAsync", "GetWorkspaceOverviewAsync",
+   "CompactContextAsync",
];
```

### 3.5 清理 Database/Redis 残留

| 文件 | 操作 |
|------|------|
| `LuBan.AIAgent.csproj` | 移除 `<PackageReference>`：`MySqlConnector 2.6.2`、`Npgsql 10.0.3`、`Microsoft.Data.SqlClient 7.0.2`、`Microsoft.Data.Sqlite.Core 10.0.12` |
| `LuBan.AIAgent.csproj` `<Description>` | `支持自然语言操作浏览器、文件系统、脚本执行、数据库、Redis 等。` → `支持自然语言操作浏览器、文件系统、脚本、Web、检索、长期记忆、MCP 等。` |
| `GlobalUsings.cs` | 移除 `global using Microsoft.Data.SqlClient;`、`Microsoft.Data.Sqlite;`、`MySqlConnector;`、`Npgsql;`、`System.Data.Common;`（5 行） |

### 3.6 DI 生命周期修复

```diff
// LuBanAgentExtensions.cs
- services.AddScoped<ILuBanToolPlugin, Tools.Browser.BrowserToolPlugin>();
+ services.AddSingleton<ILuBanToolPlugin, Tools.Browser.BrowserToolPlugin>();
```

原因：`BrowserToolPlugin` 构造函数仅依赖 `IOptions<LuBanAgentOptions>`（Singleton），无需 Scoped。`ToolPluginRegistry` 为 Singleton，所有 plugin 统一 Singleton 消除生命周期不一致。

### 3.7 Framework README 更新

**LuBan.AIAgent/README.md / README.en.md**: 
- 移除 Database/Redis 工具描述
- 新增 `CompactContext` 工具说明：压缩上下文，LLM 可见读取类工具

---

## 4. 架构设计（Part 2: Agent 层）

### 4.1 AgentProfile 扩展

```diff
+ protected virtual ToolGroupOptions BuildToolOptions() => new();

public virtual async Task<LuBanAgent> CreateAgentAsync(...)
{
    ...
-   return await factory.CreateAsync(modelName, fullPrompt, ToolGroups, RetrievalMode, true);
+   return await factory.CreateAsync(modelName, fullPrompt, ToolGroups, RetrievalMode, true, BuildToolOptions());
}
```

**NormalAgentProfile**:
```csharp
protected override ToolGroupOptions BuildToolOptions() => new()
{
    Browser = { Headless = false },
    Script = { Shell = "cmd", DefaultTimeout = 30000 }
};
```
（其余选项保持 framework C# 默认值）

**RagAgentProfile**:
```csharp
// 构造中：_toolGroups = new[] { "retrieval", "filesystem", "context" }
protected override ToolGroupOptions BuildToolOptions() => base.BuildToolOptions();
```
（与 Normal 一致的基础覆盖，追加 context 工具组，可选覆盖 Retrieval.DefaultTopK）

### 4.2 移除 appsettings.json Tools 节

**2 个文件**:
- `LubanAgentCli/appsettings.json`: 删除 `"Tools": { ... }` 整节（第 29-63 行）
- `LubanAgentCodex/appsettings.json`: 同上

其余配置（Providers、Orchestration、Session、DbConnectionOptions 等）不变。

### 4.3 ToolDisplayNames 扩展

```diff
// ToolDisplayNames.cs
+ { "CompactContextAsync", "压缩上下文" }
```

### 4.4 CLI csproj 注释修复

```diff
- <!-- CLI 的 Database 工具仅支持 SqlServer/MySql/PostgreSql/SQLite，
-     裁剪 SqlSugarCore 传递的其余数据库驱动（Oracle/达梦/人大金仓/神舟通用），约 7MB。
-     若未来通过 SqlSugar 直连这些数据库，移除对应排除项即可恢复 -->
+ <!-- SqlSugar 仅用于会话持久化（SQLite），裁剪其余数据库驱动约 7MB -->
```

### 4.5 Codex 编译警告修复

**文件**: `LubanAgentCodex/Views/SettingsWindow.axaml.cs:693`

`CS8602: Dereference of a possibly null reference` — `provider.Name` 警告。修复方式：`provider!.Name`（因 isNew 分支保证非空）或重组为本地变量确保流分析识别。

### 4.6 Agent 侧 README 更新（6 对中英文）

| 文件 | 更新内容 |
|------|----------|
| `LubanAgentCore/README.md(.en)` | 移除 `Tools` 配置段说明；新增工具注册机制（Profile 硬编码免配置）+ CompactContext 说明 |
| `LubanAgentCli/README.md(.en)` | 移除 `Tools` 配置段示例；移除 Database/Redis 工具提及（英文版）；新增 CompactContext 说明 |
| `LubanAgentCodex/README.md(.en)` | 同上 |

---

## 5. 不改动的部分

| 项 | 原因 |
|----|------|
| MCP 工具友好名 | Q11 选 D |
| Orchestration / Providers / Session 配置 | 仍在 appsettings 中，不受影响 |
| 不新增配置文件 | Q4 选 C，全硬编码 |
| 权限模式调度逻辑 | 已有 ReadOnlyTools 机制，无需改调度分发 |

---

## 6. 文件变更清单

### Framework 层 (`luban-framework/LuBan.AIAgent/`)

| 文件 | 操作 |
|------|------|
| `Abstractions/ILuBanToolPlugin.cs` | 改 GetTools 签名 |
| `Abstractions/ToolConfirmationService.cs` | DefaultReadOnlyTools 加 CompactContextAsync |
| `Configuration/ToolGroupOptions.cs` | 不变（已有所有组默认值） |
| `LuBanAgentExtensions.cs` | 加 CompactContext 注册；Browser AddScoped→AddSingleton；移除已删插件注释 |
| `LuBanAgentFactory.cs` | CreateAsync/CreateSubAgentAsync 加 toolsOptions 参数；BuildTools 传递参数 |
| `LuBan.AIAgent.csproj` | 移除 4 个包引用；更新 Description |
| `GlobalUsings.cs` | 移除 5 个过时 global using |
| `Tools/Context/CompactContextToolPlugin.cs` | **新增** |
| `Tools/Browser/BrowserToolPlugin.cs` | GetTools 签名兼容 |
| `Tools/FileSystem/FileSystemToolPlugin.cs` | GetTools 签名兼容 |
| `Tools/Script/ScriptToolPlugin.cs` | GetTools 覆盖子参数 |
| `Tools/Web/WebToolPlugin.cs` | GetTools 覆盖子参数 |
| `Tools/Retrieval/RetrievalToolPlugin.cs` | GetTools 覆盖子参数 |
| `Tools/LocalMemory/LocalMemoryToolPlugin.cs` | GetTools 覆盖子参数 |
| `Tools/Orchestration/OrchestrationToolPlugin.cs` | GetTools 签名兼容 |
| `MCP/MCPToolPlugin.cs` | GetTools 签名兼容 |
| `README.md` / `README.en.md` | 更新工具列表描述 |

### Agent 层 (`luban-agent/`)

| 文件 | 操作 |
|------|------|
| `LubanAgentCore/Agents/AgentProfile.cs` | 加 BuildToolOptions 虚方法；CreateAgentAsync 传参 |
| `LubanAgentCore/Agents/NormalAgentProfile.cs` | 重写 BuildToolOptions |
| `LubanAgentCore/Agents/RagAgentProfile.cs` | ToolGroups 加 context；重写 BuildToolOptions |
| `LubanAgentCore/Utils/ToolDisplayNames.cs` | 加 CompactContextAsync 映射 |
| `LubanAgentCore/README.md(.en)` | 更新工具注册机制说明 |
| `LubanAgentCli/appsettings.json` | 删除 Tools 节 |
| `LubanAgentCli/LubanAgentCli.csproj` | 注释修复 |
| `LubanAgentCli/README.md(.en)` | 更新工具配置说明 |
| `LubanAgentCodex/appsettings.json` | 删除 Tools 节 |
| `LubanAgentCodex/Views/SettingsWindow.axaml.cs` | 修复 CS8602 |
| `LubanAgentCodex/README.md(.en)` | 更新工具配置说明 |

---

## 7. 构建与验证

1. `dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj` → 零错误零警告
2. `dotnet build luban-agent/luban-agent.slnx` → 零错误零警告（含 CS8602 修复）
3. 功能冒烟：NormalAgent 全工具可用；RagAgent 仅 retrieval/filesystem/context 可见；CompactContextAsync 可调用并压缩当前会话
4. `git commit` + `push` 两个仓库