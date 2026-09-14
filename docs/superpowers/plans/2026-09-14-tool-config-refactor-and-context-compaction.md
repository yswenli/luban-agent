# 工具配置重构 & 上下文压缩 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 移除 appsettings 的 `Tools` 节，转为 Profile 硬编码 + framework 运行期参数注入通道；新增 CompactContext 压缩工具；清理 Database/Redis 残留；修复编译警告；更新中英文 README。

**Architecture:** Framework 层 `ILuBanToolPlugin.GetTools` 加 `ToolGroupOptions? toolsOptions` 参数，由 `LuBanAgentFactory` 透传；Agent 层 `AgentProfile` 加 `BuildToolOptions` 虚方法返回 Profile 级子参数硬编码。CompactContext 作为独立工具组 `context` 注册，通过 `IServiceScopeFactory` 获取 scoped `IChatClient` 执行压缩。

**Tech Stack:** .NET 10, C#, Microsoft.Agents.AI, Microsoft.Extensions.AI

---

## File Structure

```
luban-framework/LuBan.AIAgent/
├── Abstractions/
│   ├── ILuBanToolPlugin.cs          [MODIFY] GetTools 签名加 toolsOptions 参数
│   ├── ToolPluginRegistry.cs        [MODIFY] GetAllFunctions 透传 toolsOptions（默认 null）
│   ├── ToolConfirmationService.cs   [MODIFY] DefaultReadOnlyTools 追加 CompactContextAsync
├── Configuration/
│   ├── ToolGroupOptions.cs          [NO CHANGE] 已有全部子组配置
│   └── LuBanAgentOptions.cs         [NO CHANGE] Tools 属性保留（IConfiguration 绑定仍需，值全是默认）
├── Tools/
│   ├── Context/
│   │   └── CompactContextToolPlugin.cs [CREATE] 新插件
│   ├── Browser/BrowserToolPlugin.cs [MODIFY] 签名兼容
│   ├── FileSystem/FileSystemToolPlugin.cs [MODIFY] 签名兼容
│   ├── Script/ScriptToolPlugin.cs   [MODIFY] 接收 toolsOptions 覆盖 Shell/Timeout
│   ├── Web/WebToolPlugin.cs         [MODIFY] 接收 toolsOptions 覆盖 MaxCharacters
│   ├── Retrieval/RetrievalToolPlugin.cs [MODIFY] 接收 toolsOptions 覆盖 Retrieval 子参数
│   ├── LocalMemory/LocalMemoryToolPlugin.cs [MODIFY] 接收 toolsOptions 覆盖 LocalMemory 子参数
│   ├── Orchestration/OrchestrationToolPlugin.cs [MODIFY] 仅签名兼容
├── MCP/
│   └── MCPToolPlugin.cs             [MODIFY] 仅签名兼容
├── LuBanAgentExtensions.cs          [MODIFY] Browser→Singleton, 注册 CompactContext
├── LuBanAgentFactory.cs             [MODIFY] CreateAsync/CreateSubAgentAsync 加 toolsOptions 参数
├── LuBan.AIAgent.csproj             [MODIFY] 移除 4 个包引用, 更新 Description
├── GlobalUsings.cs                  [MODIFY] 移除 5 行 stale using
├── README.md                        [MODIFY] 更新工具列表
└── README.en.md                     [MODIFY] 更新工具列表

luban-agent/
├── LubanAgentCore/
│   ├── Agents/
│   │   ├── AgentProfile.cs          [MODIFY] 加 BuildToolOptions 虚方法, CreateAgentAsync 传参
│   │   ├── NormalAgentProfile.cs    [MODIFY] 重写 BuildToolOptions
│   │   └── RagAgentProfile.cs       [MODIFY] ToolGroups 加 context, 重写 BuildToolOptions
│   └── Utils/
│       └── ToolDisplayNames.cs      [MODIFY] 加 CompactContextAsync 映射
├── LubanAgentCli/
│   ├── appsettings.json             [MODIFY] 删除 Tools 节
│   ├── LubanAgentCli.csproj         [MODIFY] 注释修复
│   ├── README.md                    [MODIFY] 更新工具配置说明
│   └── README.en.md                 [MODIFY] 更新工具配置说明
└── LubanAgentCodex/
    ├── appsettings.json             [MODIFY] 删除 Tools 节
    ├── Views/SettingsWindow.axaml.cs[MODIFY] 修复 CS8602
    ├── README.md                    [MODIFY] 更新工具配置说明
    └── README.en.md                 [MODIFY] 更新工具配置说明
```

---

### Task 1: Framework — ILuBanToolPlugin 接口签名变更

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Abstractions\ILuBanToolPlugin.cs`

- [ ] **Step 1: 改接口签名**

将 `GetTools` 方法签名从 `GetTools(IServiceProvider sp)` 改为 `GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)`：

```csharp
/// <summary>
/// 获取该分组下的所有工具函数
/// </summary>
/// <param name="sp">服务提供者，用于解析工具依赖</param>
/// <param name="toolsOptions">运行期工具参数覆盖，null 使用构造时默认值</param>
/// <returns>AIFunction 工具函数列表</returns>
IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null);
```

---

### Task 2: Framework — BrowserToolPlugin DI 修复 + 签名兼容

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBanAgentExtensions.cs` (line 46)
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Tools\Browser\BrowserToolPlugin.cs` (line 55-62)

- [ ] **Step 1: Browser 注册 AddScoped → AddSingleton**

```diff
- services.AddScoped<ILuBanToolPlugin, Tools.Browser.BrowserToolPlugin>();
+ services.AddSingleton<ILuBanToolPlugin, Tools.Browser.BrowserToolPlugin>();
```

- [ ] **Step 2: BrowserToolPlugin.GetTools 签名兼容**

Browser 在 `GetTools` 中 `new BrowserToolGroup(session, confirmationService)` 不使用 `_options`，仅需签名匹配：

```diff
- public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp)
+ public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
```

- [ ] **Step 3: 构建 framework 确认编译通过**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj
```

此时 8 个 plugin 的实现尚未更新，预期 10 个编译错误（不匹配接口）。确认报错符合预期。

---

### Task 3: Framework — 全部 plugin GetTools 签名适配 + 子参数覆盖

**Files:**
- Modify: 7 个 plugin 文件

- [ ] **Step 1: FileSystemToolPlugin.cs — 仅签名兼容**

`FileSystemToolGroup` 构造仅需 `PathGuard` + `IToolConfirmationService`，不接收 options：

```diff
- public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp)
+ public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
```

- [ ] **Step 2: ScriptToolPlugin.cs — 实际覆盖**

构造函数中 `_options = options.Value.Tools.Script`。GetTools 内构造 `new ScriptToolGroup(_options, ...)`。需用 `toolsOptions?.Script` 覆盖：

```diff
  public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
  {
+     var opts = toolsOptions?.Script ?? _options;
      var confirmationService = sp.GetRequiredService<IToolConfirmationService>();
-     var toolGroup = new ScriptToolGroup(_options, _processRunner, confirmationService);
+     var toolGroup = new ScriptToolGroup(opts, _processRunner, confirmationService);
      ...
  }
```

- [ ] **Step 3: WebToolPlugin.cs — 实际覆盖**

构造函数 `_options = options.Value.Tools.Web`，GetTools 内 `new WebToolGroup(_options)`：

```diff
  public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
  {
+     var opts = toolsOptions?.Web ?? _options;
-     var toolGroup = new WebToolGroup(_options);
+     var toolGroup = new WebToolGroup(opts);
      ...
  }
```

- [ ] **Step 4: RetrievalToolPlugin.cs — 实际覆盖**

`RetrievalToolPlugin` 存 `IOptions<LuBanAgentOptions>`，GetTools 内 `_options.Value.Tools.Retrieval`：

```diff
  public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
  {
+     var retrievalOptions = toolsOptions?.Retrieval ?? _options.Value.Tools.Retrieval;
      var svc = sp.GetService<IRetrievalService>();
      if (svc == null) return Array.Empty<AIFunction>();
      var confirmationService = sp.GetRequiredService<IToolConfirmationService>();
-     var group = new RetrievalToolGroup(svc, _options.Value.Tools.Retrieval, confirmationService);
+     var group = new RetrievalToolGroup(svc, retrievalOptions, confirmationService);
      ...
  }
```

- [ ] **Step 5: LocalMemoryToolPlugin.cs — 实际覆盖**

构造函数 `_options = options.Value.Tools.LocalMemory`：

```diff
  public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
  {
+     var memoryOptions = toolsOptions?.LocalMemory ?? _options;
      var confirmationService = sp.GetRequiredService<IToolConfirmationService>();
-     var group = new LocalMemoryToolGroup(sp.GetRequiredService<ILocalMemoryService>(), _options, confirmationService);
+     var group = new LocalMemoryToolGroup(sp.GetRequiredService<ILocalMemoryService>(), memoryOptions, confirmationService);
      ...
  }
```

- [ ] **Step 6: OrchestrationToolPlugin.cs — 仅签名兼容**

```diff
- public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp)
+ public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
```

- [ ] **Step 7: MCPToolPlugin.cs — 仅签名兼容**

```diff
- public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp)
+ public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
```

- [ ] **Step 8: 构建 framework 确认编译通过**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj
```

预期：0 errors, 0 warnings（CS8602 警告是 agent 侧，framework 无警告）。

---

### Task 4: Framework — ToolPluginRegistry 兼容

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Abstractions\ToolPluginRegistry.cs`

- [ ] **Step 1: GetAllFunctions 透传 toolsOptions**

```diff
  public IReadOnlyList<AIFunction> GetAllFunctions(IServiceProvider sp, IEnumerable<string>? groupNames = null)
      => GetPlugins(groupNames)
-         .SelectMany(p => p.GetTools(sp))
+         .SelectMany(p => p.GetTools(sp, null))
          .ToList();
```

---

### Task 5: Framework — ILuBanAgentFactory 接口 + LuBanAgentFactory 扩展 toolsOptions 参数

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\ILuBanAgentFactory.cs`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBanAgentFactory.cs`

- [ ] **Step 0: ILuBanAgentFactory 接口加 toolsOptions 参数**

```diff
-     Task<LuBanAgent> CreateAsync(
-         string? modelName = null,
-         string? systemPrompt = null,
-         IEnumerable<string>? toolGroups = null,
-         string? retrievalMode = null,
-         bool useSessionHistory = false,
-         CancellationToken cancellationToken = default);
+     Task<LuBanAgent> CreateAsync(
+         string? modelName = null,
+         string? systemPrompt = null,
+         IEnumerable<string>? toolGroups = null,
+         string? retrievalMode = null,
+         bool useSessionHistory = false,
+         ToolGroupOptions? toolsOptions = null,
+         CancellationToken cancellationToken = default);
```

需要加 `using LuBan.AIAgent.Configuration;`。

- [ ] **Step 1: BuildTools 接收并透传 toolsOptions**

```diff
- private List<AITool> BuildTools(IEnumerable<string>? toolGroups)
+ private List<AITool> BuildTools(IEnumerable<string>? toolGroups, ToolGroupOptions? toolsOptions = null)
  {
      var plugins = _pluginRegistry.GetPlugins(toolGroups);
      var tools = plugins
-         .SelectMany(p => p.GetTools(_serviceProvider))
+         .SelectMany(p => p.GetTools(_serviceProvider, toolsOptions))
          .Cast<AITool>()
          .ToList();
      ...
  }
```

- [ ] **Step 2: CreateAsync 加 toolsOptions 参数**

```diff
  public Task<LuBanAgent> CreateAsync(
      string? modelName = null,
      string? systemPrompt = null,
      IEnumerable<string>? toolGroups = null,
      string? retrievalMode = null,
      bool useSessionHistory = false,
+     ToolGroupOptions? toolsOptions = null,
      CancellationToken cancellationToken = default)
  {
      var opts = _options.Value;
      var instructions = systemPrompt ?? opts.SystemPrompt ?? "你是一个智能助手。";
-     var tools = BuildTools(toolGroups);
+     var tools = BuildTools(toolGroups, toolsOptions);
      ...
  }
```

- [ ] **Step 3: CreateSubAgentAsync 加 toolsOptions 参数**

```diff
  public Task<LuBanAgent> CreateSubAgentAsync(
      string? modelName,
      IEnumerable<string>? toolGroups,
      string systemPrompt,
+     ToolGroupOptions? toolsOptions = null,
      CancellationToken cancellationToken = default)
  {
      var opts = _options.Value;
-     var tools = BuildTools(toolGroups);
+     var tools = BuildTools(toolGroups, toolsOptions);
      ...
  }
```

- [ ] **Step 4: 构建确认编译通过**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj
```

---

### Task 6: Framework — 新增 CompactContextToolPlugin

**Files:**
- Create: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Tools\Context\CompactContextToolPlugin.cs`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBanAgentExtensions.cs` (在 line 57 上方)

- [ ] **Step 1: 创建 CompactContextToolPlugin**

Enumerate: `Context`
Type: `Dir`
New path: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Tools\Context`

```csharp
using System.ComponentModel;
using System.Text.Json;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LuBan.AIAgent.Tools.Context;

/// <summary>
/// 上下文压缩工具插件，允许 Agent 主动调用压缩对话上下文
/// </summary>
public class CompactContextToolPlugin : ILuBanToolPlugin
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<LuBanAgentOptions> _options;

    public CompactContextToolPlugin(IServiceScopeFactory scopeFactory, IOptions<LuBanAgentOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    public string GroupName => "context";

    public string? Description => "上下文压缩工具，压缩当前会话的对话历史，释放 token 预算";

    public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
    {
        var sessionManager = sp.GetRequiredService<ISessionManager>();
        var confirmationService = sp.GetRequiredService<IToolConfirmationService>();
        var group = new CompactContextToolGroup(_scopeFactory, sessionManager, confirmationService, _options.Value);
        return new List<AIFunction>
        {
            AIFunctionFactoryHelper.Create(group, nameof(CompactContextToolGroup.CompactContextAsync))
        };
    }

    public bool IsEnabled(LuBanAgentOptions options) => true;
}

/// <summary>
/// 上下文压缩工具组
/// </summary>
public class CompactContextToolGroup
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISessionManager _sessionManager;
    private readonly IToolConfirmationService _confirmationService;
    private readonly LuBanAgentOptions _options;

    public CompactContextToolGroup(
        IServiceScopeFactory scopeFactory,
        ISessionManager sessionManager,
        IToolConfirmationService confirmationService,
        LuBanAgentOptions options)
    {
        _scopeFactory = scopeFactory;
        _sessionManager = sessionManager;
        _confirmationService = confirmationService;
        _options = options;
    }

    [Description("压缩当前会话的对话上下文，将较旧的消息摘要化以释放 token 预算")]
    public async Task<ToolResult<string>> CompactContextAsync(
        [Description("压缩后保留的消息数，默认使用配置值")] int? targetCount = null)
    {
        var sessionId = _sessionManager.CurrentSession?.SessionId;
        if (string.IsNullOrEmpty(sessionId))
            return ToolResult.Fail<string>("无当前会话，无需压缩");

        var target = targetCount ?? _options.Session.CompactTargetMessages;
        var threshold = _options.Session.CompactThreshold;

        var active = (await _sessionManager.GetActiveMessagesAsync(sessionId)).ToList();

        var summaries = active.Where(m => m.Role == "summary").OrderByDescending(m => m.Id).ToList();
        var messages = active.Where(m => m.Role != "summary").ToList();

        if (messages.Count <= target + threshold)
            return ToolResult.Ok<string>("上下文未达到压缩阈值，无需压缩。当前活跃消息数: " + messages.Count);

        using var scope = _scopeFactory.CreateScope();
        var chatClient = scope.ServiceProvider.GetRequiredService<IChatClient>();

        var history = messages
            .Select(m => new ChatMessage(m.Role == "user" ? ChatRole.User : ChatRole.Assistant, m.Content))
            .ToList();

        var reducer = new Microsoft.Agents.AI.SummarizingChatReducer(chatClient, target, threshold);
        var reduced = (await reducer.ReduceAsync(history)).ToList();

        if (reduced.Count == 0 || reduced.Count >= history.Count)
            return ToolResult.Fail<string>("压缩后消息数未减少，摘要可能失败");

        var keptCount = Math.Min(reduced.Count - 1, history.Count);
        var keptTail = messages.Skip(messages.Count - keptCount).ToList();
        var compactedIds = messages.Take(messages.Count - keptCount).Select(m => m.Id)
            .Concat(summaries.Select(s => s.Id))
            .ToList();
        await _sessionManager.MarkMessagesCompactedAsync(sessionId, compactedIds);

        var summaryText = reduced[0].Text ?? "";
        await _sessionManager.AddMessageAsync(sessionId, "summary", summaryText,
            Math.Max(1, summaryText.Length / 4));

        var resultObj = new
        {
            oldSummary = summaries.FirstOrDefault()?.Content ?? "(无旧摘要)",
            newSummary = summaryText,
            compactedCount = compactedIds.Count,
            remainingCount = keptTail.Count
        };

        return ToolResult.Ok(resultObj.ToJson(), $"压缩完成：归档 {compactedIds.Count} 条消息，保留 {keptTail.Count} 条活跃消息");
    }
}
```

- [ ] **Step 2: 注册 CompactContextToolPlugin**

在 `LuBanAgentExtensions.cs` 的 `ToolPluginRegistry` 注册行（line 56）之前插入：

```diff
+         services.AddSingleton<ILuBanToolPlugin, Tools.Context.CompactContextToolPlugin>();

          services.AddSingleton<ToolPluginRegistry>();
```

- [ ] **Step 3: 构建确认编译通过**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj
```

---

### Task 7: Framework — ReadOnlyTools 追加 CompactContextAsync

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Abstractions\ToolConfirmationService.cs`

- [ ] **Step 1: DefaultReadOnlyTools 加条目**

```diff
  private static readonly string[] DefaultReadOnlyTools =
  [
      "ReadFileAsync", "ListDirectoryAsync", "GetWorkspaceOverviewAsync",
+     "CompactContextAsync",
  ];
```

---

### Task 8: Framework — 清理 Database 残留

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\GlobalUsings.cs`

- [ ] **Step 1: csproj 移除 4 个 Database NuGet 包引用**

移除以下 4 行：
```xml
<PackageReference Include="Microsoft.Data.Sqlite.Core" Version="10.0.12" />
```
(line 42)
```xml
<PackageReference Include="MySqlConnector" Version="2.6.2" />
```
(line 49)
```xml
<PackageReference Include="Npgsql" Version="10.0.3" />
```
(line 50)
```xml
<PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.2" />
```
(line 51)

- [ ] **Step 2: csproj 更新 Description**

```diff
-     <Description>LuBan.AIAgent 是基于 Microsoft Agent Framework 的 AI Agent 库，支持自然语言操作浏览器、文件系统、脚本执行、数据库、Redis 等。</Description>
+     <Description>LuBan.AIAgent 是基于 Microsoft Agent Framework 的 AI Agent 库，支持自然语言操作浏览器、文件系统、脚本、Web、检索、长期记忆、MCP、上下文压缩等。</Description>
```

- [ ] **Step 3: GlobalUsings.cs 移除 5 行 database global using**

移除以下行：
- Line 43: `global using Microsoft.Data.SqlClient;`
- Line 44: `global using Microsoft.Data.Sqlite;`
- Line 52: `global using MySqlConnector;`
- Line 54: `global using Npgsql;`
- Line 58: `global using System.Data.Common;`

- [ ] **Step 4: 构建确认**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj
```

预期：0 errors, 0 warnings。

---

### Task 9: Agent — AgentProfile 基类加 BuildToolOptions

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore\Agents\AgentProfile.cs`

- [ ] **Step 1: 加 using 和保护虚方法**

在文件顶部 using 区域加入 `using LuBan.AIAgent.Configuration;`；在类中加方法：

```diff
+ using LuBan.AIAgent.Configuration;

  public abstract class AgentProfile
  {
+     /// <summary>
+     /// 构建运行期工具参数覆盖。派生类可重写以定制子参数（如 Headless、Shell、Timeout），
+     /// null 表示使用框架内置默认值。
+     /// </summary>
+     protected virtual ToolGroupOptions BuildToolOptions() => new();
      ...
```

- [ ] **Step 2: CreateAgentAsync 传参**

```diff
      return await factory.CreateAsync(
          modelName: modelName,
          systemPrompt: fullPrompt,
          toolGroups: ToolGroups,
          retrievalMode: RetrievalMode,
-         useSessionHistory: true);
+         useSessionHistory: true,
+         toolsOptions: BuildToolOptions());
```

---

### Task 10: Agent — NormalAgentProfile 重写 BuildToolOptions

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore\Agents\NormalAgentProfile.cs`

- [ ] **Step 1: 加 using 并重写**

```diff
+ using LuBan.AIAgent.Configuration;

  public class NormalAgentProfile : AgentProfile
  {
+     /// <inheritdoc/>
+     protected override ToolGroupOptions BuildToolOptions() => new()
+     {
+         Browser = { Headless = false },
+         Script = { Shell = "cmd", DefaultTimeout = 30000 }
+     };
      ...
```

---

### Task 11: Agent — RagAgentProfile 加 context 工具组 + BuildToolOptions

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore\Agents\RagAgentProfile.cs`

- [ ] **Step 1: 追加 context 工具组**

```diff
+ using LuBan.AIAgent.Configuration;

  public class RagAgentProfile : AgentProfile
  {
      public RagAgentProfile(WorkspaceInfo workspace)
      {
          _workspace = workspace;
          _systemPrompt = "你是一个知识库问答专家...";
-         _toolGroups = new[] { "retrieval", "filesystem" };
+         _toolGroups = new[] { "retrieval", "filesystem", "context" };
          LoadRagConfig();
      }
```

- [ ] **Step 2: 加 BuildToolOptions 重写**

```diff
+     /// <inheritdoc/>
+     protected override ToolGroupOptions BuildToolOptions() => new()
+     {
+         Browser = { Headless = false },
+         Script = { Shell = "cmd", DefaultTimeout = 30000 }
+     };
  }
```

---

### Task 12: Agent — ToolDisplayNames 加 CompactContextAsync

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore\Utils\ToolDisplayNames.cs`

- [ ] **Step 1: 添加映射条目**

在 `// Orchestration` 区域（line 65 之后）插入：

```diff
          // Orchestration
          ["OrchestrateAsync"] = "编排任务",
+         // Context
+         ["CompactContextAsync"] = "压缩上下文",
      };
```

---

### Task 13: Agent — 删除 appsettings.json 的 Tools 节

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\appsettings.json`
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCodex\appsettings.json`

- [ ] **Step 1: CLI appsettings — 删除 Tools 节点（line 29-63）**

删除 `"Tools": { ... }` 整节（包含 Browser/FileSystem/Script/Web/Retrieval/LocalMemory 六个子节），保留外层逗号结构不变。

- [ ] **Step 2: Codex appsettings — 同上**

删除相同节点。

---

### Task 14: Agent — 修复 SettingsWindow CS8602 编译警告

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCodex\Views\SettingsWindow.axaml.cs` (line 682-693)

- [ ] **Step 1: 修复 provider.Name 空引用警告**

`GetProvider` 返回 `ProviderConfig?`，isNew 分支虽然赋新值但流分析仍视为可空。改为非空本地变量：

```diff
      var provider = _configManager.GetProvider(key);
      var isNew = provider == null;
-     if (isNew) provider = new ProviderConfig { Name = key };
+     var nonNullProvider = isNew ? new ProviderConfig { Name = key } : provider;
+     var nameBox = AddField(host, "Name（唯一标识，小写）", nonNullProvider!.Name);
-     var nameBox = AddField(host, "Name（唯一标识，小写）", provider.Name);
      if (!isNew) nameBox.IsReadOnly = true;
```

同时调整后续 `provider` 引用为 `nonNullProvider`：

```diff
-     SetField("ApiKey", AddPasswordField(host, "ApiKey", provider.ApiKey));
+     SetField("ApiKey", AddPasswordField(host, "ApiKey", nonNullProvider.ApiKey));
-     SetField("BaseUrl", AddField(host, "BaseUrl（空=默认）", provider.BaseUrl ?? ""));
+     SetField("BaseUrl", AddField(host, "BaseUrl（空=默认）", nonNullProvider.BaseUrl ?? ""));
-     SetField("DisplayName", AddField(host, "DisplayName（可选）", provider.DisplayName ?? ""));
+     SetField("DisplayName", AddField(host, "DisplayName（可选）", nonNullProvider.DisplayName ?? ""));
-     SetField("NetworkTimeoutSeconds", AddField(host, "NetworkTimeoutSeconds（空=默认 60）", provider.NetworkTimeoutSeconds?.ToString() ?? ""));
+     SetField("NetworkTimeoutSeconds", AddField(host, "NetworkTimeoutSeconds（空=默认 60）", nonNullProvider.NetworkTimeoutSeconds?.ToString() ?? ""));
```

---

### Task 15: Agent — LubanAgentCli.csproj 注释修复

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\LubanAgentCli.csproj`

- [ ] **Step 1: 替换 Database 驱动注释**

```diff
-         <!-- CLI 的 Database 工具仅支持 SqlServer/MySql/PostgreSql/SQLite，
-             裁剪 SqlSugarCore 传递的其余数据库驱动（Oracle/达梦/人大金仓/神舟通用），约 7MB。
-             若未来通过 SqlSugar 直连这些数据库，移除对应排除项即可恢复 -->
+         <!-- SqlSugar 仅用于会话持久化（SQLite），裁剪其余数据库驱动约 7MB -->
```

---

### Task 16: Framework — 更新 README

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\README.md`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\README.en.md`

- [ ] **Step 1: 先读取当前的 README 文件**

- [ ] **Step 2: README.md 更新**

将工具列表部分中的"数据库"和"Redis"提及替换为"上下文压缩"：

- 工具列表移除 Database、Redis 条目
- 新增 `context` 条目：上下文压缩（CompactContextAsync），LLM 可主动调用以释放 token 预算

- [ ] **Step 3: README.en.md 同步更新**

英文版做相同替换。

---

### Task 17: Agent — 更新 Cli + Codex README（4 个文件）

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\README.md`
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\README.en.md`
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCodex\README.md`
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCodex\README.en.md`

- [ ] **Step 1: 先读取各自当前的 README**

- [ ] **Step 2: 统一更新规则**

每个 README 做以下修改：
1. 若文档含 `LuBanAgent:Tools` 配置段说明，改为说明"工具配置现已由 AgentProfile 派生类在代码中硬编码，无需 appsettings 配置"；列出工具组：browser/filesystem/script/web/retrieval/localmemory/mcp/context/orchestration
2. 新增 CompactContext 工具说明：LLM 可见的压缩上下文工具
3. 英文版移除 Database/Redis 残留提及（若有）

---

### Task 18: 构建验证 — 全量无错误无警告

- [ ] **Step 1: 构建 framework**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj
```

预期：0 errors, 0 warnings。

- [ ] **Step 2: 构建 agent 全量**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\luban-agent.slnx
```

预期：0 errors, 0 warnings（CS8602 已修复）。

---

### Task 19: Commit — luban-framework 仓库

- [ ] **Step 1: 提交 framework 改动**

```bash
cd D:\WorkBench\Walle\luban\luban-framework
git add LuBan.AIAgent/
git commit -m "重构工具配置：移除 appsettings Tools 节依赖，Profile 硬编码 + framework 运行期参数注入；新增 CompactContext 工具；清理 Database 残留；修复 Browser DI 生命周期"
git push
```

---

### Task 20: Commit — luban-agent 仓库

- [ ] **Step 1: 提交 agent 改动**

```bash
cd D:\WorkBench\Walle\luban\luban-agent
git add LubanAgentCore/ LubanAgentCli/ LubanAgentCodex/
git commit -m "feat: 工具配置重构 — 移除 appsettings Tools 节，Profile 硬编码 + 上下文压缩工具"
git push
```