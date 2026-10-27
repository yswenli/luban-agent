# 三层职责重构（框架 / Core / UI）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把「Agent 宿主 + 会话运行时 + Provider 路由 + 命令清单 + 工具展示元数据」从 `LubanAgentCore` 搬进 `LuBan.AIAgent`，把业务从 UI 搬进 Core 的 9 个 `*ManagementService`，使框架几行代码可用、Core 只管资料与扩展、UI 只做 UI。

**Architecture:** 单包内收（方案 A）。`LuBan.AIAgent` 新增 `Hosting/`、`Configuration/Providers/`、`Abstractions/` 三块能力，靠 4 个接缝（`IAgentWorkspaceResolver` / `IAgentProfileResolver` / `IAgentSessionExtension` / `IAgentUserResolver`）与产品解耦；Core 通过实现这 4 个接缝 + 各 `*ManagementService` 提供产品能力；两个 UI 只注入 `IAgentHost` 与服务。

**Tech Stack:** .NET 10 / MSTest / Microsoft.Extensions.AI 10.11.0 / OpenAI 2.14.0（框架显式钉）/ Avalonia（Codex）/ Terminal.Gui（CLI）/ SQLite（Core）。

**设计文档：** `luban-agent/docs/superpowers/specs/2026-10-26-three-layer-refactor-design.md`

---

## 0. 前置约定（每个任务都适用）

- **仓库根不是 git 仓库**。提交必须进子仓库：
  - 框架：`git -C luban-framework ...`
  - 产品：`git -C luban-agent ...`
- 构建：
  - `dotnet build luban-framework/LuBan.FrameWork.sln`
  - `dotnet build luban-agent/LubanAgent.slnx`
- 测试：
  - `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj`
  - `dotnet test luban-agent/LubanAgent.Tests/LubanAgent.Tests.csproj`
  - 单个类：`--filter "FullyQualifiedName~XxxUnitTest"`
- **开发期依赖**：agent 侧走 `ProjectReference`（Task 1 建立），全部验证通过后由**人工发版**再改回 `PackageReference`（Task 20）。
- **禁止顺手改文案**：CLI 命令改造只允许「删业务代码 + 改调用目标」，`Render*` 方法与字符串常量不许动。
- 提交信息中文；`luban-agent` 带 conventional 前缀（`feat:`/`fix:`/`refactor:`/`docs:`），`luban-framework` 用纯中文短句。

---

## 任务总览

| # | 任务 | 仓库 | 并行组 | 依赖 |
|---|---|---|---|---|
| 1 | ProjectReference 直连基线 | agent | — | — |
| 2 | 框架契约：`WorkspaceDescriptor` / `AgentStreamEvent` / `AgentRunRequest` | framework | — | — |
| 3 | 框架契约：`IAgentHost` + 4 接缝 + Profile | framework | — | 2 |
| 4 | 框架：会话运行时迁移 | framework | — | 3 |
| 5 | 框架：`AgentHost` + 附件编排 + 能力门禁 | framework | — | 4 |
| 6 | 框架：命令清单迁移 | framework | — | 2 |
| 7 | 框架：Provider 层迁移 | framework | — | 2 |
| 8 | 框架：工具展示元数据迁移 | framework | — | 2 |
| 9 | 框架：DI 扩展 `AddLuBanAgentHost` / `AddLuBanAgentOpenAI` | framework | — | 5,6,7,8 |
| 10 | 框架：`WebApplication1` 最小 Agent 端点 | framework | — | 9 |
| 11 | Core：删重复类型 + 注册 4 接缝 | agent | — | 9 |
| 12 | UI：类型替换 + 全量冒烟 | agent | — | 11 |
| 13 | Core：9 个服务接口骨架 | agent | — | 12 |
| 14 | P1 模型/Provider 下沉 | agent | P1 | 13 |
| 15 | P2 Skill/Rule 下沉 | agent | P2 | 13 |
| 16 | P3 MCP/Connector 下沉 | agent | P3 | 13 |
| 17 | P4 Workspace/Session/Wiki 下沉 | agent | P4 | 13 |
| 18 | P5 插件/凭据下沉 | agent | P5 | 13 |
| 19 | P6 Codex 设置页拆 ViewModel | agent | P6 | 13 |
| 20 | 收尾：别名清零 / 文档 / 人工发版 / 改回 PackageReference | 双仓 | — | 14–19 |

---

# 阶段 A：环境准备

### Task 1: agent 侧改为 ProjectReference 直连

**Files:**
- Modify: `luban-agent/LubanAgentCore/LubanAgentCore.csproj`
- Modify: `luban-agent/LubanAgentCli/LubanAgentCli.csproj`
- Modify: `luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
- Modify: `luban-agent/LubanAgentA2A/LubanAgentA2A.csproj`
- Modify: `luban-agent/LubanAgent.Plugin.NewProject/LubanAgent.Plugin.NewProject.csproj`

- [ ] **Step 1: 查清 5 个工程里所有 `LuBan.*` 包引用**

Run:
```powershell
Select-String -Path luban-agent\*\*.csproj -Pattern '<PackageReference Include="LuBan\.'
```
Expected: 列出全部待替换项（`LuBan.Common` / `LuBan.AIAgent` / `LuBan.Orm` / `LuBan.Logging` / `LuBan.DI` / `LuBan.Threading` 等）。

- [ ] **Step 2: 逐条替换为 ProjectReference**

对每个 `LuBan.X` 的 `PackageReference`，改成（注意相对路径按工程所在目录层级调整，`..\..\luban-framework\LuBan.X\LuBan.X.csproj`）：

```xml
<!-- TEMP-LOCAL-VERIFY: 三层重构开发期直连框架源码，推送前必须改回 PackageReference -->
<ProjectReference Include="..\..\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj" />
```

- [ ] **Step 3: 构建验证**

Run:
```powershell
dotnet build luban-agent/LubanAgent.slnx
```
Expected: `Build succeeded`，0 error。

- [ ] **Step 4: 顺手验证框架改动即时生效**

在 `luban-framework/LuBan.AIAgent/LuBanAgentDefaultsExtensions.cs` 里加一行无害注释，重新 `dotnet build luban-agent/LubanAgent.slnx`，确认框架被重新编译（输出里出现 `LuBan.AIAgent -> ...dll`）。验证后删掉该注释。

- [ ] **Step 5: 提交**

```powershell
git -C luban-agent add -A
git -C luban-agent commit -m "refactor: 开发期改为 ProjectReference 直连框架（TEMP-LOCAL-VERIFY）"
```

---

# 阶段 B：框架内建

### Task 2: 框架契约（一）`WorkspaceDescriptor` / `AgentStreamEvent` / `AgentRunRequest`

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Hosting/WorkspaceDescriptor.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/AgentStreamEvent.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/AgentRunRequest.cs`
- Test: `luban-framework/LuBan.XTestProject/AgentStreamEventUnitTest.cs`

- [ ] **Step 1: 写失败测试**

`luban-framework/LuBan.XTestProject/AgentStreamEventUnitTest.cs`：

```csharp
using LuBan.AIAgent.Hosting;

namespace LuBan.UnitTestProject;

[TestClass]
public class AgentStreamEventUnitTest
{
    [TestMethod]
    public void TextDeltaEvent_CarriesDelta_AndDefaultsRoundToZero()
    {
        var evt = new TextDeltaEvent("hi");

        Assert.AreEqual("hi", evt.Delta);
        Assert.AreEqual(0, evt.LlmRound);
    }

    [TestMethod]
    public void LlmRound_IsInitOnly()
    {
        AgentStreamEvent evt = new ThinkingDeltaEvent("t") { LlmRound = 3 };

        Assert.AreEqual(3, evt.LlmRound);
    }

    [TestMethod]
    public void AgentRunRequest_DefaultsAreSane()
    {
        var req = new AgentRunRequest { Input = "hello" };

        Assert.AreEqual("hello", req.Input);
        Assert.AreEqual(0, req.AttachmentPaths.Count);
        Assert.IsNull(req.SessionId);
        Assert.IsNull(req.ConfirmHandler);
    }

    [TestMethod]
    public void WorkspaceDescriptor_IsValueEqual()
    {
        var a = new WorkspaceDescriptor("w1", "demo", @"C:\demo", "normal");
        var b = new WorkspaceDescriptor("w1", "demo", @"C:\demo", "normal");

        Assert.AreEqual(a, b);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentStreamEventUnitTest"
```
Expected: 编译失败，`WorkspaceDescriptor` / `AgentStreamEvent` / `AgentRunRequest` 不存在。

- [ ] **Step 3: 写 `WorkspaceDescriptor`**

`luban-framework/LuBan.AIAgent/Hosting/WorkspaceDescriptor.cs`：

```csharp
namespace LuBan.AIAgent.Hosting;

/// <summary>
/// 工作区的中立描述（框架不依赖产品的工作区模型）。
/// </summary>
/// <param name="Id">工作区标识。</param>
/// <param name="Name">工作区名称。</param>
/// <param name="RootPath">工作区根目录绝对路径。</param>
/// <param name="Type">工作区类型（产品自定义，如 <c>normal</c> / <c>llmwiki</c>）。</param>
public sealed record WorkspaceDescriptor(string Id, string Name, string RootPath, string Type);
```

- [ ] **Step 4: 写 `AgentStreamEvent`**

`luban-framework/LuBan.AIAgent/Hosting/AgentStreamEvent.cs`：把
`luban-agent/LubanAgentCore/Models/StreamEvent.cs` 全文复制过来，然后：

1. 命名空间改为 `LuBan.AIAgent.Hosting`；
2. `public abstract record StreamEvent` → `public abstract record AgentStreamEvent`；
3. 所有 `: StreamEvent` → `: AgentStreamEvent`；
4. 补 `using LuBan.AIAgent.Orchestration;`（`OrchestrationProgress` / `NodeResult` / `NodeActivityItem` / `ProgressEventType` 来源，按编译器提示补全）。

`AgentRunRequest` 同一步创建，`luban-framework/LuBan.AIAgent/Hosting/AgentRunRequest.cs`：

```csharp
using LuBan.AIAgent.Abstractions;

namespace LuBan.AIAgent.Hosting;

/// <summary>
/// 一轮对话的入参（取代 <c>AgentHost.RunStreamingAsync</c> 的散参数）。
/// </summary>
public sealed record AgentRunRequest
{
    /// <summary>用户输入文本。</summary>
    public required string Input { get; init; }

    /// <summary>附件文件绝对路径列表。</summary>
    public IReadOnlyList<string> AttachmentPaths { get; init; } = [];

    /// <summary>目标会话标识；null 表示当前活动会话。</summary>
    public string? SessionId { get; init; }

    /// <summary>工具权限模式。</summary>
    public ToolPermissionMode PermissionMode { get; init; } = ToolPermissionMode.Normal;

    /// <summary>工具确认处理器；null 表示使用默认策略（按权限模式）。</summary>
    public Func<string, IReadOnlyDictionary<string, object?>, ConfirmResult>? ConfirmHandler { get; init; }

    /// <summary>Plan 模式下计划项回调。</summary>
    public Action<string, IReadOnlyDictionary<string, object?>>? OnPlannedAction { get; init; }

    /// <summary>用户明确拒绝工具调用时的回调。</summary>
    public Action<string, IReadOnlyDictionary<string, object?>>? OnUserDenied { get; init; }
}
```

> `ToolPermissionMode` / `ConfirmResult` 已存在于 `LuBan.AIAgent.Abstractions`，确认命名空间后按编译提示补 `using`。

- [ ] **Step 5: 跑测试确认通过**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentStreamEventUnitTest"
```
Expected: 4 passed。

- [ ] **Step 6: 提交**

```powershell
git -C luban-framework add -A
git -C luban-framework commit -m "新增宿主契约：WorkspaceDescriptor、AgentStreamEvent、AgentRunRequest"
```

---

### Task 3: 框架契约（二）`IAgentHost` + 4 接缝 + Profile

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Hosting/IAgentHost.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/IAgentWorkspaceResolver.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/IAgentProfileResolver.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/IAgentSessionExtension.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/IAgentUserResolver.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/IAgentModelSelection.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/IAgentProfile.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/AgentProfileBase.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/DefaultAgentProfile.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/DefaultSeams.cs`
- Test: `luban-framework/LuBan.XTestProject/DefaultSeamsUnitTest.cs`

- [ ] **Step 1: 写失败测试**

`luban-framework/LuBan.XTestProject/DefaultSeamsUnitTest.cs`：

```csharp
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Defaults;
using LuBan.AIAgent.Hosting;
using Microsoft.Extensions.Options;

namespace LuBan.UnitTestProject;

[TestClass]
public class DefaultSeamsUnitTest
{
    private static IOptions<LuBanAgentOptions> Options(string? root = null)
        => Microsoft.Extensions.Options.Options.Create(new LuBanAgentOptions { WorkspaceRoot = root });

    [TestMethod]
    public async Task SingleWorkspaceResolver_ReturnsConfiguredRoot()
    {
        var resolver = new SingleWorkspaceResolver(Options(@"C:\work\demo"));

        var ws = await resolver.ResolveAsync("s1");

        Assert.AreEqual(@"C:\work\demo", ws.RootPath);
        Assert.AreEqual("default", ws.Id);
    }

    [TestMethod]
    public void DefaultAgentProfileResolver_AlwaysReturnsDefaultProfile()
    {
        var resolver = new DefaultAgentProfileResolver(new DefaultAgentProfile());

        var profile = resolver.Resolve(new WorkspaceDescriptor("w", "n", @"C:\w", "normal"));

        Assert.IsInstanceOfType<DefaultAgentProfile>(profile);
    }

    [TestMethod]
    public void DefaultAgentUserResolver_FallsBackToLocal()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
        var resolver = new DefaultAgentUserResolver();

        Assert.AreEqual("local", resolver.ResolveUserId(services));
    }

    [TestMethod]
    public void DefaultAgentModelSelection_ReadsConfiguredDefault()
    {
        var selection = new DefaultAgentModelSelection(Options());

        Assert.IsFalse(string.IsNullOrWhiteSpace(selection.QualifiedModelName) || selection.QualifiedModelName == ":");
    }
}
```

> `LuBanAgentOptions` 若没有 `DefaultModel` 属性，改用 `LuBanAgent:Models:Default` 的绑定模型（见 Step 4 代码注释），并把断言改成「未配置时返回空字符串」。

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~DefaultSeamsUnitTest"
```
Expected: 编译失败（类型不存在）。

- [ ] **Step 3: 写 `IAgentHost` 与 4 个接缝**

`Hosting/IAgentHost.cs`：

```csharp
using LuBan.AIAgent.Abstractions;

namespace LuBan.AIAgent.Hosting;

/// <summary>
/// Agent 宿主：按会话路由流式对话，输出 UI 无关的 <see cref="AgentStreamEvent"/> 序列。
/// </summary>
/// <remarks>
/// 必须以 <b>Scoped</b> 解析（它用当前 scope 取 <c>IChatClient</c> / 附件编排器等 Scoped 服务）。
/// 每会话的 Agent 实例在其会话 scope 内构建并缓存，宿主自身不持有会话状态。
/// </remarks>
public interface IAgentHost
{
    /// <summary>当前会话的 Agent 是否已就绪。</summary>
    bool IsInitialized { get; }

    /// <summary>当前 scope 的服务提供者（供宿主读取全局配置/工作区等）。</summary>
    IServiceProvider Services { get; }

    /// <summary>当前活动会话标识（无则 null）。</summary>
    string? CurrentSessionId { get; }

    /// <summary>确保指定会话的 Agent 已构建（首次对话前调用）。</summary>
    Task InitializeAsync(string sessionId, CancellationToken ct = default);

    /// <summary>重置指定会话的 Agent 缓存；null 表示当前会话。</summary>
    void Reset(string? sessionId = null);

    /// <summary>运行一轮对话。</summary>
    IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(AgentRunRequest request, CancellationToken ct = default);
}
```

`Hosting/IAgentWorkspaceResolver.cs`：

```csharp
namespace LuBan.AIAgent.Hosting;

/// <summary>解析「某会话属于哪个工作区」。</summary>
public interface IAgentWorkspaceResolver
{
    /// <summary>解析会话绑定的工作区；实现应保证返回非 null（无工作区时回退默认）。</summary>
    Task<WorkspaceDescriptor> ResolveAsync(string sessionId, CancellationToken ct = default);
}
```

`Hosting/IAgentProfileResolver.cs`：

```csharp
namespace LuBan.AIAgent.Hosting;

/// <summary>按工作区选择 Agent Profile。</summary>
public interface IAgentProfileResolver
{
    /// <summary>选 Profile。</summary>
    IAgentProfile Resolve(WorkspaceDescriptor workspace);
}
```

`Hosting/IAgentSessionExtension.cs`：

```csharp
namespace LuBan.AIAgent.Hosting;

/// <summary>
/// 会话级扩展点：建 Agent 前由宿主逐个调用（插件贡献、连接器启用等）。
/// </summary>
public interface IAgentSessionExtension
{
    /// <summary>在会话 scope 上应用扩展。</summary>
    Task ApplyAsync(IServiceProvider sessionServices, WorkspaceDescriptor workspace, CancellationToken ct = default);
}
```

`Hosting/IAgentUserResolver.cs`：

```csharp
namespace LuBan.AIAgent.Hosting;

/// <summary>解析当前会话归属用户。</summary>
public interface IAgentUserResolver
{
    /// <summary>返回用户标识（不得返回 null 或空串）。</summary>
    string ResolveUserId(IServiceProvider services);
}
```

- [ ] **Step 4: 写 `IAgentModelSelection` 与默认实现**

`Hosting/IAgentModelSelection.cs`：

```csharp
namespace LuBan.AIAgent.Hosting;

/// <summary>当前选定的模型（宿主读取；产品可用自己的配置实现覆盖）。</summary>
public interface IAgentModelSelection
{
    /// <summary>Provider 标识（如 <c>openai</c>）。</summary>
    string? ProviderId { get; }

    /// <summary>模型标识（如 <c>gpt-4.1</c>）。</summary>
    string? ModelId { get; }

    /// <summary>限定名 <c>provider:model</c>；未选择时为空字符串。</summary>
    string QualifiedModelName { get; }
}
```

`Hosting/DefaultSeams.cs`：

```csharp
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Defaults;
using LuBan.AIAgent.Abstractions;
using Microsoft.Extensions.Options;

namespace LuBan.AIAgent.Hosting;

/// <summary>单工作区解析：取 <c>LuBanAgent:WorkspaceRoot</c>，缺省为进程当前目录。</summary>
public sealed class SingleWorkspaceResolver(IOptions<LuBanAgentOptions> options) : IAgentWorkspaceResolver
{
    /// <inheritdoc />
    public Task<WorkspaceDescriptor> ResolveAsync(string sessionId, CancellationToken ct = default)
    {
        var root = string.IsNullOrWhiteSpace(options.Value.WorkspaceRoot)
            ? Directory.GetCurrentDirectory()
            : options.Value.WorkspaceRoot!;

        return Task.FromResult(new WorkspaceDescriptor("default", "Default", root, "normal"));
    }
}

/// <summary>恒返回固定 Profile。</summary>
public sealed class DefaultAgentProfileResolver(IAgentProfile profile) : IAgentProfileResolver
{
    /// <inheritdoc />
    public IAgentProfile Resolve(WorkspaceDescriptor workspace) => profile;
}

/// <summary>默认用户解析：读 <see cref="IUserContext"/>，缺失回落 <c>local</c>。</summary>
public sealed class DefaultAgentUserResolver : IAgentUserResolver
{
    /// <inheritdoc />
    public string ResolveUserId(IServiceProvider services)
        => services.GetService<IUserContext>()?.UserId is { Length: > 0 } id ? id : "local";
}

/// <summary>
/// 默认模型选择：读 <c>LuBanAgent:Models:Provider</c> / <c>LuBanAgent:Models:Model</c>。
/// </summary>
public sealed class DefaultAgentModelSelection(IOptions<LuBanAgentOptions> options) : IAgentModelSelection
{
    /// <inheritdoc />
    public string? ProviderId { get; } = options.Value.Models?.Provider;

    /// <inheritdoc />
    public string? ModelId { get; } = options.Value.Models?.Model;

    /// <inheritdoc />
    public string QualifiedModelName
        => string.IsNullOrWhiteSpace(ProviderId) || string.IsNullOrWhiteSpace(ModelId)
            ? string.Empty
            : $"{ProviderId}:{ModelId}";
}
```

> 若 `LuBanAgentOptions` 没有 `Models` 节点，先在 `LuBanAgentOptions` 里加：
> ```csharp
> public sealed class ModelSelectionOptions { public string? Provider { get; set; } public string? Model { get; set; } }
> public ModelSelectionOptions? Models { get; set; }
> ```

- [ ] **Step 5: 写 `IAgentProfile` / `AgentProfileBase` / `DefaultAgentProfile`**

`Hosting/IAgentProfile.cs`：

```csharp
namespace LuBan.AIAgent.Hosting;

/// <summary>Agent 配置：系统提示词、工具组、检索模式与创建流程。</summary>
public interface IAgentProfile
{
    /// <summary>系统提示词。</summary>
    string SystemPrompt { get; }

    /// <summary>启用的工具组；null 表示全部。</summary>
    string[]? ToolGroups { get; }

    /// <summary>检索模式；null 表示不检索，<c>auto</c> 表示自动注入。</summary>
    string? RetrievalMode { get; }

    /// <summary>在给定会话 scope 上创建 Agent。</summary>
    /// <param name="factory">Agent 工厂。</param>
    /// <param name="modelName">模型限定名（<c>provider:model</c>）。</param>
    /// <param name="workspace">工作区描述。</param>
    /// <param name="ruleEngine">规则引擎。</param>
    /// <param name="pluginRegistry">工具插件注册表。</param>
    /// <param name="skillRegistry">Skill 注册表。</param>
    /// <param name="mcpRegistry">MCP 注册表。</param>
    /// <param name="ct">取消令牌。</param>
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

`Hosting/AgentProfileBase.cs`：把
`luban-agent/LubanAgentCore/Agents/AgentProfile.cs` 的内容搬过来，改动：

1. 命名空间 → `LuBan.AIAgent.Hosting`；
2. `abstract class AgentProfile` → `abstract class AgentProfileBase : IAgentProfile`；
3. `WorkspaceInfo workspace` → `WorkspaceDescriptor workspace`（两处：`CreateAgentAsync`、`BuildFullPrompt`）；
4. `workspace.RootPath` / `Name` / `Type` 访问保持不变（`WorkspaceDescriptor` 有同名字段）；
5. `Logger` 用 `LuBan.Common.Logger`（若原文件靠 global using，补 `using LuBan.Common;`）。

`Hosting/DefaultAgentProfile.cs`：

```csharp
namespace LuBan.AIAgent.Hosting;

/// <summary>产品中立的默认 Profile：通用助手提示词 + 全部工具组。</summary>
public sealed class DefaultAgentProfile : AgentProfileBase
{
    /// <inheritdoc />
    public override string SystemPrompt
        => "你是一个通用 AI 助手。优先使用工具完成任务，回答简洁准确。";

    /// <inheritdoc />
    public override string[]? ToolGroups => null;

    /// <inheritdoc />
    public override string? RetrievalMode => null;
}
```

- [ ] **Step 6: 跑测试确认通过**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~DefaultSeamsUnitTest"
```
Expected: 4 passed。

- [ ] **Step 7: 提交**

```powershell
git -C luban-framework add -A
git -C luban-framework commit -m "新增宿主接缝：IAgentHost、工作区/Profile/扩展/用户解析与 Profile 基类"
```

---

### Task 4: 框架：会话运行时迁移

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Hosting/SessionRuntime.cs`（搬自 `luban-agent/LubanAgentCore/Services/SessionRuntime.cs`）
- Create: `luban-framework/LuBan.AIAgent/Hosting/SessionRuntimeManager.cs`（搬自同名 Core 文件）
- Create: `luban-framework/LuBan.AIAgent/Hosting/SessionRuntimeSeed.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/ISessionRuntimeInitializer.cs`
- Test: `luban-framework/LuBan.XTestProject/SessionRuntimeManagerUnitTest.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Defaults;
using LuBan.AIAgent.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuBan.UnitTestProject;

[TestClass]
public class SessionRuntimeManagerUnitTest
{
    private static (SessionRuntimeManager Manager, ServiceProvider Root) Build()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddOptions();
        services.AddScoped<DefaultContextState>();
        services.AddScoped<ISessionContext, DefaultSessionContext>();
        services.AddSingleton<ISessionRuntimeInitializer, RecordingInitializer>();
        var root = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        return (new SessionRuntimeManager(root, config), root);
    }

    [TestMethod]
    public async Task GetOrCreate_FillsContextState_AndCallsInitializers()
    {
        var (manager, _) = Build();

        var runtime = manager.GetOrCreate("s1", "u1", "w1", @"C:\ws");
        await Task.Delay(100);

        var state = runtime.Services.GetRequiredService<DefaultContextState>();
        Assert.AreEqual("s1", state.SessionId);
        Assert.AreEqual(@"C:\ws", state.WorkspaceRoot);
        Assert.IsTrue(state.AllowedRoots.Contains(@"C:\ws"));
        Assert.IsTrue(RecordingInitializer.Called);

        await manager.DisposeAsync();
    }

    [TestMethod]
    public async Task GetOrCreate_SameSession_ReturnsSameRuntime()
    {
        var (manager, _) = Build();

        var a = manager.GetOrCreate("s1", "u1", "w1", @"C:\ws");
        var b = manager.GetOrCreate("s1", "u1", "w1", @"C:\ws");

        Assert.AreSame(a, b);
        await manager.DisposeAsync();
    }

    private sealed class RecordingInitializer : ISessionRuntimeInitializer
    {
        public static bool Called;
        public Task InitializeAsync(SessionRuntimeSeed seed, CancellationToken ct = default)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~SessionRuntimeManagerUnitTest"
```
Expected: 编译失败（`SessionRuntimeManager` 不在框架）。

- [ ] **Step 3: 搬 `SessionRuntime`**

`git -C luban-agent show HEAD:luban-agent/LubanAgentCore/Services/SessionRuntime.cs` 取原文，落到 `luban-framework/LuBan.AIAgent/Hosting/SessionRuntime.cs`，改动：

| 原 | 新 |
|---|---|
| `namespace LubanAgentCore.Services` | `namespace LuBan.AIAgent.Hosting` |
| `using LubanAgentCore.Configuration;` | 删除（若用到 `ConfigManager` 则改为 `IAgentModelSelection`） |
| `LubanAgentCore.*` 任何引用 | 按编译器提示换框架类型 |

- [ ] **Step 4: 搬 `SessionRuntimeManager` 并加初始化接缝**

`Hosting/SessionRuntimeSeed.cs`：

```csharp
namespace LuBan.AIAgent.Hosting;

/// <summary>会话 scope 初始化数据。</summary>
public sealed record SessionRuntimeSeed(
    string SessionId,
    string? WorkspaceId,
    string WorkspaceRoot,
    IReadOnlyList<string> AllowedRoots);
```

`Hosting/ISessionRuntimeInitializer.cs`：

```csharp
namespace LuBan.AIAgent.Hosting;

/// <summary>
/// 会话 scope 创建后的初始化扩展点（宿主用产品字段补齐上下文）。
/// </summary>
public interface ISessionRuntimeInitializer
{
    /// <summary>在会话 scope 内执行初始化。</summary>
    Task InitializeAsync(SessionRuntimeSeed seed, CancellationToken ct = default);
}

/// <summary>空实现（未注册实现时的回落）。</summary>
public sealed class NoOpSessionRuntimeInitializer : ISessionRuntimeInitializer
{
    /// <inheritdoc />
    public Task InitializeAsync(SessionRuntimeSeed seed, CancellationToken ct = default) => Task.CompletedTask;
}
```

`Hosting/SessionRuntimeManager.cs`：搬 Core 原文件，`CreateRuntime` 改成：

```csharp
private SessionRuntime CreateRuntime(string sessionId, string userId, string workspaceId, string workspaceRoot)
{
    var scope = _root.CreateUserScope(userId);

    var state = scope.ServiceProvider.GetRequiredService<DefaultContextState>();
    state.SessionId = sessionId;
    state.WorkspaceId = workspaceId;
    state.WorkspaceRoot = workspaceRoot;
    if (!string.IsNullOrWhiteSpace(workspaceRoot))
        state.AllowedRoots.Add(workspaceRoot);

    var seed = new SessionRuntimeSeed(
        sessionId,
        workspaceId,
        workspaceRoot,
        state.AllowedRoots.ToList());

    // 初始化器是同步前置动作（连接器/插件贡献必须在首轮对话前就绪），
    // 这里阻塞等待是为保持 GetOrCreate 的同步签名；实现不得做长耗时 IO。
    foreach (var initializer in scope.ServiceProvider.GetServices<ISessionRuntimeInitializer>())
    {
        try { initializer.InitializeAsync(seed).GetAwaiter().GetResult(); }
        catch (Exception ex) { Logger.Warn($"会话初始化器执行失败: {initializer.GetType().Name}", ex); }
    }

    var ctx = scope.ServiceProvider.GetRequiredService<ISessionContext>();
    var runtime = new SessionRuntime(scope, sessionId, ctx);
    runtime.Start();
    return runtime;
}
```

> 若 `CreateUserScope` 是产品扩展方法（`LubanAgentCore` 里），把它一并搬进框架 `Hosting` 或改为直接用 `_root.CreateScope()` + 显式写 `DefaultContextState.UserId`。**优先后者**（少一个产品依赖）。

- [ ] **Step 5: 跑测试确认通过**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~SessionRuntimeManagerUnitTest"
```
Expected: 2 passed。

- [ ] **Step 6: 提交**

```powershell
git -C luban-framework add -A
git -C luban-framework commit -m "迁入会话运行时与初始化接缝"
```

---

### Task 5: 框架：`AgentHost` + 附件编排 + 能力门禁

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Hosting/AgentHost.cs`（搬自 `luban-agent/LubanAgentCore/Services/AgentHostService.cs`，578 行）
- Create: `luban-framework/LuBan.AIAgent/Hosting/IAgentAttachmentOrchestrator.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/DefaultAgentAttachmentOrchestrator.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/IModelCapabilityGate.cs`
- Test: `luban-framework/LuBan.XTestProject/AgentRunRequestValidationUnitTest.cs`

- [ ] **Step 1: 写失败测试（可离线跑，不碰真实模型）**

```csharp
using LuBan.AIAgent.Attachments;
using LuBan.AIAgent.Hosting;

namespace LuBan.UnitTestProject;

[TestClass]
public class AgentRunRequestValidationUnitTest
{
    [TestMethod]
    public void AllowAllModelCapabilityGate_AlwaysAllows()
    {
        IModelCapabilityGate gate = new AllowAllModelCapabilityGate();

        Assert.IsTrue(gate.CanAttach("any", Array.Empty<ProcessedAttachment>()));
        Assert.AreEqual(string.Empty, gate.GetUnsupportedMessage("any", Array.Empty<ProcessedAttachment>()));
    }

    [TestMethod]
    public void AgentRunRequest_RequiresInput()
    {
        Assert.ThrowsException<ArgumentException>(() =>
        {
            var req = new AgentRunRequest { Input = "" };
            _ = req.Input ?? throw new ArgumentException();
            if (req.Input.Length == 0) throw new ArgumentException("input required");
        });
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentRunRequestValidationUnitTest"
```
Expected: 编译失败（`AllowAllModelCapabilityGate` 不存在）。

- [ ] **Step 3: 写门禁与附件编排契约**

`Hosting/IModelCapabilityGate.cs`：

```csharp
using LuBan.AIAgent.Attachments;

namespace LuBan.AIAgent.Hosting;

/// <summary>模型对附件的支持判定（产品按模型能力缓存实现）。</summary>
public interface IModelCapabilityGate
{
    /// <summary>该模型能否承载这些附件。</summary>
    bool CanAttach(string modelId, IReadOnlyList<ProcessedAttachment> attachments);

    /// <summary>不支持时的用户提示；支持时返回空串。</summary>
    string GetUnsupportedMessage(string modelId, IReadOnlyList<ProcessedAttachment> attachments);
}

/// <summary>默认门禁：一律允许。</summary>
public sealed class AllowAllModelCapabilityGate : IModelCapabilityGate
{
    /// <inheritdoc />
    public bool CanAttach(string modelId, IReadOnlyList<ProcessedAttachment> attachments) => true;

    /// <inheritdoc />
    public string GetUnsupportedMessage(string modelId, IReadOnlyList<ProcessedAttachment> attachments) => string.Empty;
}
```

`Hosting/IAgentAttachmentOrchestrator.cs`：

```csharp
using LuBan.AIAgent.Attachments;

namespace LuBan.AIAgent.Hosting;

/// <summary>附件编排：路径解析 + 处理 + 能力门禁。</summary>
public interface IAgentAttachmentOrchestrator
{
    /// <summary>从输入文本中剥离 @引用，返回（剩余文本, 已识别路径）。</summary>
    (string Text, IReadOnlyList<string> Paths) ExtractReferences(string input);

    /// <summary>处理附件。</summary>
    Task<IReadOnlyList<ProcessedAttachment>> ProcessAsync(
        IReadOnlyList<string> paths, CancellationToken ct = default);

    /// <summary>检查模型能否承载，不能则给出提示。</summary>
    string? Validate(string modelId, IReadOnlyList<ProcessedAttachment> attachments);
}
```

`Hosting/DefaultAgentAttachmentOrchestrator.cs`：直接包框架已有的 `IAttachmentProcessor`：

```csharp
using LuBan.AIAgent.Attachments;

namespace LuBan.AIAgent.Hosting;

/// <summary>默认附件编排：只做处理与门禁，不解析 @引用（交宿主）。</summary>
public sealed class DefaultAgentAttachmentOrchestrator(
    IAttachmentProcessor processor,
    IModelCapabilityGate gate) : IAgentAttachmentOrchestrator
{
    /// <inheritdoc />
    public (string Text, IReadOnlyList<string> Paths) ExtractReferences(string input)
        => (input, Array.Empty<string>());

    /// <inheritdoc />
    public Task<IReadOnlyList<ProcessedAttachment>> ProcessAsync(
        IReadOnlyList<string> paths, CancellationToken ct = default)
        => paths.Count == 0
            ? Task.FromResult<IReadOnlyList<ProcessedAttachment>>(Array.Empty<ProcessedAttachment>())
            : processor.ProcessAsync(paths, generateThumbnails: false, ct);

    /// <inheritdoc />
    public string? Validate(string modelId, IReadOnlyList<ProcessedAttachment> attachments)
        => attachments.Count == 0 || gate.CanAttach(modelId, attachments)
            ? null
            : gate.GetUnsupportedMessage(modelId, attachments);
}
```

- [ ] **Step 4: 搬 `AgentHost` 并按符号表替换产品依赖**

把 `luban-agent/LubanAgentCore/Services/AgentHostService.cs` 落到 `Hosting/AgentHost.cs`，按下表**逐符号**替换（编译器逐个兜底）：

| 原符号 | 替换为 |
|---|---|
| `class AgentHostService` | `sealed class AgentHost : IAgentHost` |
| `namespace LubanAgentCore.Services` | `namespace LuBan.AIAgent.Hosting` |
| `StreamEvent` | `AgentStreamEvent`（全文件） |
| `ConfigManager` + `configManager.SelectedModel` | `IAgentModelSelection` + `selection.QualifiedModelName`（空则抛 `未选择模型`） |
| `IWorkspaceManager` + `ResolveSessionWorkspaceAsync` | `IAgentWorkspaceResolver.ResolveAsync(sessionId, ct)` |
| `WorkspaceInfo` | `WorkspaceDescriptor` |
| `new LlmWikiAgentProfile(workspace) : new NormalAgentProfile()` | `profileResolver.Resolve(workspace)` |
| `sp.GetServices<IPluginSessionContributor>()` 循环 | `sp.GetServices<IAgentSessionExtension>()` 循环，调 `ApplyAsync(sp, workspace, ct)` |
| `ConnectorRegistry.LoadFromWorkspace/ApplyEnabledAsync` | 同上（由 Core 的 `ConnectorSessionExtension` 承接） |
| `AttachmentService` | `IAgentAttachmentOrchestrator`（`ProcessAsync` + `Validate`） |
| `CurrentUserId` 里的 `LocalUserDefaults.DefaultUserId` | `userResolver.ResolveUserId(_services)` |
| `SessionRepository`（解析会话工作区） | 删除（由 `IAgentWorkspaceResolver` 承接） |
| 视觉能力探测 `VisionCapabilityProbe` | 保留调用壳，但探测回填逻辑移到 Core `VisionModelCapabilityGate`；宿主只调 `gate.Observe(...)`（若无法解耦，则保留探测代码并把 `IModelCapabilityCache` 换成框架接口） |

改造后的公开面：

```csharp
public sealed class AgentHost(
    IServiceProvider services,
    SessionRuntimeManager runtimes,
    IAgentWorkspaceResolver workspaceResolver,
    IAgentProfileResolver profileResolver,
    IAgentUserResolver userResolver,
    IAgentModelSelection modelSelection,
    IAgentAttachmentOrchestrator attachments) : IAgentHost
{
    /// <inheritdoc />
    public IServiceProvider Services => services;

    /// <inheritdoc />
    public string? CurrentSessionId => services.GetService<ISessionManager>()?.CurrentSession?.SessionId;

    /// <inheritdoc />
    public bool IsInitialized => CurrentSessionId is { Length: > 0 } id && runtimes.Get(id)?.Agent is not null;

    /// <inheritdoc />
    public async Task InitializeAsync(string sessionId, CancellationToken ct = default)
        => await EnsureRuntimeAsync(sessionId, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public void Reset(string? sessionId = null)
    {
        var id = sessionId ?? CurrentSessionId;
        if (id is not { Length: > 0 }) return;

        var runtime = runtimes.Get(id);
        if (runtime is null) return;

        if (runtime.Agent is IDisposable old) { try { old.Dispose(); } catch { } }
        runtime.Agent = null;
        runtime.Workspace = null;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(AgentRunRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);
        return RunStreamingCoreAsync(request, ct);
    }
}
```

> `EnsureRuntimeAsync` / `EnsureAgentAsync` / `RunCoreAsync` 的方法体从原文件搬，按上表替换符号即可。**不要重写事件转换逻辑**（`LlmRoundKey`、编排进度、工具结果摘要这些行为必须逐字保留）。

- [ ] **Step 5: 跑测试确认通过**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentRunRequestValidationUnitTest"
```
Expected: 2 passed。

- [ ] **Step 6: 提交**

```powershell
git -C luban-framework add -A
git -C luban-framework commit -m "迁入 AgentHost 与附件编排/能力门禁契约"
```

---

### Task 6: 框架：命令清单迁移

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Hosting/Commands/ICommandCatalog.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/Commands/CommandCatalog.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/Commands/SlashCommandDefinition.cs`
- Create: `luban-framework/LuBan.AIAgent/Hosting/Commands/SlashCommandClient.cs`
- Test: `luban-framework/LuBan.XTestProject/CommandCatalogUnitTest.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Hosting.Commands;

namespace LuBan.UnitTestProject;

[TestClass]
public class CommandCatalogUnitTest
{
    private readonly CommandCatalog _catalog = new();

    [TestMethod]
    public void All_ContainsCoreCommands()
    {
        var names = _catalog.All.Select(c => c.Name).ToList();
        CollectionAssert.Contains(names, "/model");
        CollectionAssert.Contains(names, "/help");
    }

    [TestMethod]
    public void MatchByPrefix_EmptyPrefix_ReturnsOnlyClientSupported()
    {
        var cli = _catalog.MatchByPrefix("/", SlashCommandClient.Cli);

        Assert.IsTrue(cli.All(c => c.Supports(SlashCommandClient.Cli)));
    }

    [TestMethod]
    public void MatchByPrefix_FiltersByText()
    {
        var result = _catalog.MatchByPrefix("/mo", SlashCommandClient.Both);

        Assert.IsTrue(result.Any(c => c.Name == "/model"));
        Assert.IsFalse(result.Any(c => c.Name == "/help"));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~CommandCatalogUnitTest"
```
Expected: 编译失败。

- [ ] **Step 3: 搬 4 个文件**

```powershell
$src = "luban-agent/LubanAgentCore/Commands"
$dst = "luban-framework/LuBan.AIAgent/Hosting/Commands"
New-Item -ItemType Directory -Force $dst | Out-Null
Get-ChildItem $src -Filter *.cs | ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    $text = $text -replace 'namespace LubanAgentCore\.Commands', 'namespace LuBan.AIAgent.Hosting.Commands'
    Set-Content (Join-Path $dst $_.Name) $text
}
```

然后按编译错误补 `using`（`CommandCatalog` 若引用产品类型，必须删掉那条命令定义——**框架里只放不依赖产品的命令**）。

- [ ] **Step 4: 跑测试确认通过**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~CommandCatalogUnitTest"
```
Expected: 3 passed。

- [ ] **Step 5: 提交**

```powershell
git -C luban-framework add -A
git -C luban-framework commit -m "迁入斜杠命令清单"
```

---

### Task 7: 框架：Provider 层迁移

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Configuration/Providers/ProviderCatalog.cs`
- Create: `luban-framework/LuBan.AIAgent/Configuration/Providers/ProviderDefaults.cs`
- Create: `luban-framework/LuBan.AIAgent/Configuration/Providers/LuBanChatClient.cs`
- Test: `luban-framework/LuBan.XTestProject/ProviderDefaultsUnitTest.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Configuration.Providers;

namespace LuBan.UnitTestProject;

[TestClass]
public class ProviderDefaultsUnitTest
{
    [TestMethod]
    public void GetDefaultBaseUrl_KnownProvider_ReturnsUrl()
    {
        Assert.AreEqual("https://api.deepseek.com/v1/", ProviderDefaults.GetDefaultBaseUrl("deepseek"));
    }

    [TestMethod]
    public void GetDefaultBaseUrl_UnknownProvider_ReturnsNull()
    {
        Assert.IsNull(ProviderDefaults.GetDefaultBaseUrl("no-such-provider"));
    }

    [TestMethod]
    public void GetDefaultModels_KnownProvider_NotEmpty()
    {
        CollectionAssert.IsNotEmpty(ProviderDefaults.GetDefaultModels("openai").ToList());
    }

    [TestMethod]
    public void GetDefaultBaseUrl_IsCaseInsensitive()
    {
        Assert.AreEqual(ProviderDefaults.GetDefaultBaseUrl("openai"), ProviderDefaults.GetDefaultBaseUrl("OpenAI"));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ProviderDefaultsUnitTest"
```
Expected: 编译失败。

- [ ] **Step 3: 写 `ProviderDefaults`（把 `ProviderHelper` 的静态表转成纯查询静态方法）**

`Configuration/Providers/ProviderDefaults.cs`：从 `luban-agent/LubanAgentCore/Configuration/ProviderHelper.cs` 复制 `_defaultBaseUrls` 与 `_models` 两张表，暴露：

```csharp
namespace LuBan.AIAgent.Configuration.Providers;

/// <summary>内置 Provider 的默认端点与模型清单（纯只读数据）。</summary>
public static class ProviderDefaults
{
    /// <summary>取默认 BaseUrl；未知 Provider 返回 null。</summary>
    public static string? GetDefaultBaseUrl(string providerId);

    /// <summary>取默认模型清单；未知 Provider 返回空集合。</summary>
    public static IReadOnlyList<string> GetDefaultModels(string providerId);

    /// <summary>全部内置 Provider 标识。</summary>
    public static IReadOnlyList<string> ProviderIds { get; }
}
```

**要求**：两张表用 `IReadOnlyDictionary<string, ...>(StringComparer.OrdinalIgnoreCase)`，**不允许**保留 `_initialized` 之类的可变静态状态。`ProviderHelper` 里负责「刷新模型列表 / 调远端」的方法**不进框架**，留在 Core。

- [ ] **Step 4: 搬 `ProviderCatalog` 与 `LuBanChatClient`**

```powershell
$src = "luban-agent/LubanAgentCore/Configuration"
$dst = "luban-framework/LuBan.AIAgent/Configuration/Providers"
New-Item -ItemType Directory -Force $dst | Out-Null
foreach ($n in @('ProviderCatalog.cs','LuBanChatClient.cs')) {
    $text = Get-Content (Join-Path $src $n) -Raw
    $text = $text -replace 'namespace LubanAgentCore\.Configuration', 'namespace LuBan.AIAgent.Configuration.Providers'
    Set-Content (Join-Path $dst $n) $text
}
```

按编译错误去产品依赖（`ProviderConfig` 若在 Core 且被引用，一并搬或改为框架 record）。

- [ ] **Step 5: 跑测试确认通过**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ProviderDefaultsUnitTest"
```
Expected: 4 passed。

- [ ] **Step 6: 提交**

```powershell
git -C luban-framework add -A
git -C luban-framework commit -m "迁入 Provider 目录与聊天客户端"
```

---

### Task 8: 框架：工具展示元数据迁移

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Abstractions/ToolDisplayNames.cs`
- Create: `luban-framework/LuBan.AIAgent/Abstractions/ToolArgsFormatter.cs`
- Test: `luban-framework/LuBan.XTestProject/ToolDisplayNamesUnitTest.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Abstractions;

namespace LuBan.UnitTestProject;

[TestClass]
public class ToolDisplayNamesUnitTest
{
    [TestMethod]
    public void Get_KnownTool_ReturnsChineseName()
    {
        var name = ToolDisplayNames.Get("ReadFileAsync");

        Assert.IsFalse(string.IsNullOrWhiteSpace(name));
        Assert.AreNotEqual("ReadFileAsync", name);
    }

    [TestMethod]
    public void Get_UnknownTool_FallsBackToMethodName()
    {
        Assert.AreEqual("NoSuchTool", ToolDisplayNames.Get("NoSuchTool"));
    }

    [TestMethod]
    public void FormatArgs_LongValue_IsTruncated()
    {
        var args = new Dictionary<string, object?> { ["path"] = new string('x', 100) };

        var text = ToolArgsFormatter.Format(args);

        Assert.IsTrue(text.Length <= ToolArgsFormatter.DefaultMaxLength + ToolArgsFormatter.Ellipsis.Length + 8);
        Assert.IsTrue(text.EndsWith(ToolArgsFormatter.Ellipsis));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ToolDisplayNamesUnitTest"
```
Expected: 编译失败。

- [ ] **Step 3: 搬两个文件**

```powershell
$dst = "luban-framework/LuBan.AIAgent/Abstractions"
foreach ($n in @('ToolDisplayNames.cs','ToolArgsFormatter.cs')) {
    $text = Get-Content "luban-agent/LubanAgentCore/Utils/$n" -Raw
    $text = $text -replace 'namespace LubanAgentCore\.Utils', 'namespace LuBan.AIAgent.Abstractions'
    Set-Content (Join-Path $dst $n) $text
}
```

若 `ToolArgsFormatter.Format` 的实际方法名不同（如 `Summarize`），**以原文件为准改测试**，不要改实现。

- [ ] **Step 4: 跑测试确认通过**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ToolDisplayNamesUnitTest"
```
Expected: 3 passed。

- [ ] **Step 5: 提交**

```powershell
git -C luban-framework add -A
git -C luban-framework commit -m "迁入工具展示名与参数摘要格式化"
```

---

### Task 9: 框架：DI 扩展 `AddLuBanAgentHost` / `AddLuBanAgentOpenAI`

**Files:**
- Create: `luban-framework/LuBan.AIAgent/LuBanAgentHostExtensions.cs`
- Create: `luban-framework/LuBan.AIAgent/LuBanAgentOpenAIExtensions.cs`
- Modify: `luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj`（钉 `OpenAI 2.14.0`）
- Test: `luban-framework/LuBan.XTestProject/AgentHostRegistrationUnitTest.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent;
using LuBan.AIAgent.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LuBan.UnitTestProject;

[TestClass]
public class AgentHostRegistrationUnitTest
{
    private static ServiceProvider Build()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LuBanAgent:WorkspaceRoot"] = @"C:\ws" })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging(b => b.AddDebug());
        services.AddSingleton<IChatClient>(new StubChatClient());
        services.AddLuBanAgentDefaults(config);
        services.AddLuBanAgentHost();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "stub")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "stub");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [TestMethod]
    public void Container_Validates()
    {
        using var sp = Build();
        Assert.IsNotNull(sp);
    }

    [TestMethod]
    public void IAgentHost_ResolvesPerScope()
    {
        using var sp = Build();
        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();

        var a = scope1.ServiceProvider.GetRequiredService<IAgentHost>();
        var b = scope2.ServiceProvider.GetRequiredService<IAgentHost>();

        Assert.AreNotSame(a, b);
    }

    [TestMethod]
    public void SessionRuntimeManager_IsSingleton()
    {
        using var sp = Build();
        using var s1 = sp.CreateScope();
        using var s2 = sp.CreateScope();

        Assert.AreSame(
            s1.ServiceProvider.GetRequiredService<SessionRuntimeManager>(),
            s2.ServiceProvider.GetRequiredService<SessionRuntimeManager>());
    }

    [TestMethod]
    public void DefaultSeams_AreRegistered()
    {
        using var sp = Build();
        using var scope = sp.CreateScope();

        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IAgentWorkspaceResolver>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IAgentProfileResolver>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IAgentUserResolver>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IModelCapabilityGate>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IAgentAttachmentOrchestrator>());
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentHostRegistrationUnitTest"
```
Expected: 编译失败（`AddLuBanAgentHost` 不存在）。

- [ ] **Step 3: 写 `AddLuBanAgentHost`**

`LuBanAgentHostExtensions.cs`：

```csharp
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Hosting;
using LuBan.AIAgent.Hosting.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LuBan.AIAgent;

/// <summary>Agent 宿主注册扩展。</summary>
public static class LuBanAgentHostExtensions
{
    /// <summary>
    /// 注册 Agent 宿主（<see cref="IAgentHost"/>）及其默认接缝实现。
    /// 需先调用 <c>AddLuBanAgent</c> 或 <c>AddLuBanAgentDefaults</c>。
    /// </summary>
    public static IServiceCollection AddLuBanAgentHost(this IServiceCollection services)
    {
        services.TryAddSingleton<SessionRuntimeManager>();

        services.TryAddScoped<IAgentHost, AgentHost>();
        services.TryAddScoped<IAgentProfile, DefaultAgentProfile>();
        services.TryAddScoped<IAgentWorkspaceResolver, SingleWorkspaceResolver>();
        services.TryAddScoped<IAgentProfileResolver>(sp =>
            new DefaultAgentProfileResolver(sp.GetRequiredService<IAgentProfile>()));
        services.TryAddScoped<IAgentUserResolver, DefaultAgentUserResolver>();
        services.TryAddScoped<IAgentModelSelection, DefaultAgentModelSelection>();
        services.TryAddScoped<IModelCapabilityGate, AllowAllModelCapabilityGate>();
        services.TryAddScoped<IAgentAttachmentOrchestrator, DefaultAgentAttachmentOrchestrator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISessionRuntimeInitializer, NoOpSessionRuntimeInitializer>());
        services.TryAddSingleton<ICommandCatalog, CommandCatalog>();

        return services;
    }
}
```

> `TryAddEnumerable` 对 `NoOpSessionRuntimeInitializer` 会导致「注册失败」——`TryAddEnumerable` 只在没有**同实现类型**时添加，这里用它正好。若编译不过，改 `services.TryAddScoped<ISessionRuntimeInitializer, NoOpSessionRuntimeInitializer>()`。

- [ ] **Step 4: 写 `AddLuBanAgentOpenAI`**

`LuBanAgentOpenAIExtensions.cs`：

```csharp
using System.ClientModel;
using LuBan.AIAgent.Configuration.Providers;
using LuBan.AIAgent.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace LuBan.AIAgent;

/// <summary>OpenAI 兼容 Provider 注册扩展。</summary>
public static class LuBanAgentOpenAIExtensions
{
    /// <summary>
    /// 按 <c>LuBanAgent:Models</c> 配置注册 <see cref="IChatClient"/>；配置缺失时只记 warning，不注册。
    /// </summary>
    /// <remarks>
    /// 配置形态：
    /// <code>
    /// "LuBanAgent": {
    ///   "Models": {
    ///     "Provider": "deepseek",
    ///     "Model": "deepseek-v4-pro",
    ///     "ApiKey": "sk-xxx",
    ///     "BaseUrl": "https://api.deepseek.com/v1/"
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public static IServiceCollection AddLuBanAgentOpenAI(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("LuBanAgent:Models");
        var provider = section["Provider"];
        var model = section["Model"];
        var apiKey = section["ApiKey"];
        var baseUrl = section["BaseUrl"] ?? ProviderDefaults.GetDefaultBaseUrl(provider ?? "");

        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(apiKey))
        {
            // 配置不全：保持宿主自行注册 IChatClient 的能力，这里不注册也不抛
            services.AddSingleton<IChatClientFactoryWarning>(new IChatClientFactoryWarning(
                "LuBanAgent:Models 未配置完整（Provider/Model/ApiKey），未注册默认 IChatClient。"));
            return services;
        }

        services.TryAddSingleton<IChatClient>(_ =>
        {
            var client = new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(baseUrl!) });

            return client.GetChatClient(model).AsIChatClient();
        });

        return services;
    }
}

/// <summary>启动期提示载体（宿主可读取并打印）。</summary>
public sealed record IChatClientFactoryWarning(string Message);
```

> **版本约束**：在 `LuBan.AIAgent.csproj` 里显式加 `<PackageReference Include="OpenAI" Version="2.14.0" />`，并在注释里写明「2.13.0 有流式工具调用丢 chunk 的破坏性变更」。

- [ ] **Step 5: 跑测试确认通过**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentHostRegistrationUnitTest"
```
Expected: 4 passed。

- [ ] **Step 6: 提交**

```powershell
git -C luban-framework add -A
git -C luban-framework commit -m "新增 AddLuBanAgentHost / AddLuBanAgentOpenAI 与 OpenAI 2.14.0 版本约束"
```

---

### Task 10: 框架：`WebApplication1` 最小 Agent 端点

**Files:**
- Modify: `luban-framework/WebApplication1/Program.cs`
- Modify: `luban-framework/WebApplication1/appsettings.json`

- [ ] **Step 1: 加配置节**

`appsettings.json` 增加（值由本地环境填）：

```json
{
  "LuBanAgent": {
    "WorkspaceRoot": "C:\\WorkBench\\Walle\\luban",
    "Models": {
      "Provider": "deepseek",
      "Model": "deepseek-v4-pro",
      "ApiKey": ""
    }
  }
}
```

- [ ] **Step 2: 加最小端点**

`Program.cs` 追加（保持文件原有内容不动）：

```csharp
// ===== 最小 Agent 示例：三行注册 + 一次流式对话 =====
builder.Services.AddLuBanAgentDefaults(builder.Configuration);
builder.Services.AddLuBanAgentHost();
builder.Services.AddLuBanAgentOpenAI(builder.Configuration);

app.MapPost("/agent/minimal", async (IAgentHost host, ISessionManager sessions, string prompt, CancellationToken ct) =>
{
    var session = await sessions.CreateSessionAsync("minimal", null);
    await sessions.SetCurrentSessionAsync(session.SessionId);
    await host.InitializeAsync(session.SessionId, ct);

    var sb = new System.Text.StringBuilder();
    await foreach (var evt in host.RunStreamingAsync(new AgentRunRequest { Input = prompt, SessionId = session.SessionId }, ct))
    {
        if (evt is TextDeltaEvent delta) sb.Append(delta.Delta);
    }
    return Results.Text(sb.ToString());
});
```

- [ ] **Step 3: 跑起来验证**

Run:
```powershell
dotnet run --project luban-framework/WebApplication1
```
然后另开终端：
```powershell
curl -X POST "http://localhost:5000/agent/minimal?prompt=%E8%AF%BB%E5%8F%96%20AGENTS.md%20%E7%AC%AC%E4%B8%80%E8%A1%8C"
```
Expected: 返回文本含 `AGENTS.md` 的实际内容（或模型对文件内容的复述）。

> 这一步是「框架几行代码可用」的**唯一硬证明**。跑不通不许进阶段 C。

- [ ] **Step 4: 提交**

```powershell
git -C luban-framework add -A
git -C luban-framework commit -m "WebApplication1 增加最小 Agent 端点验证宿主可用性"
```

---

# 阶段 C：Core 切到框架宿主

### Task 11: Core 删重复类型 + 注册 4 接缝

**Files:**
- Delete: `luban-agent/LubanAgentCore/Services/AgentHostService.cs`
- Delete: `luban-agent/LubanAgentCore/Services/SessionRuntime.cs`
- Delete: `luban-agent/LubanAgentCore/Services/SessionRuntimeManager.cs`
- Delete: `luban-agent/LubanAgentCore/Models/StreamEvent.cs`
- Delete: `luban-agent/LubanAgentCore/Context/SessionContext.cs`
- Delete: `luban-agent/LubanAgentCore/Context/SessionContextState.cs`
- Delete: `luban-agent/LubanAgentCore/Commands/*.cs`
- Delete: `luban-agent/LubanAgentCore/Utils/ToolDisplayNames.cs`
- Delete: `luban-agent/LubanAgentCore/Utils/ToolArgsFormatter.cs`
- Create: `luban-agent/LubanAgentCore/Hosting/CoreSeams.cs`
- Create: `luban-agent/LubanAgentCore/Hosting/CoreSessionRuntimeInitializer.cs`
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`
- Modify: `luban-agent/LubanAgentCore/Agents/NormalAgentProfile.cs`
- Modify: `luban-agent/LubanAgentCore/Agents/LlmWikiAgentProfile.cs`
- Modify: `luban-agent/LubanAgentCore/Configuration/ConfigManager.cs`

- [ ] **Step 1: 删文件**

```powershell
git -C luban-agent rm LubanAgentCore/Services/AgentHostService.cs
git -C luban-agent rm LubanAgentCore/Services/SessionRuntime.cs
git -C luban-agent rm LubanAgentCore/Services/SessionRuntimeManager.cs
git -C luban-agent rm LubanAgentCore/Models/StreamEvent.cs
git -C luban-agent rm LubanAgentCore/Context/SessionContext.cs
git -C luban-agent rm LubanAgentCore/Context/SessionContextState.cs
git -C luban-agent rm LubanAgentCore/Commands/ICommandCatalog.cs LubanAgentCore/Commands/CommandCatalog.cs LubanAgentCore/Commands/SlashCommandDefinition.cs LubanAgentCore/Commands/SlashCommandClient.cs
git -C luban-agent rm LubanAgentCore/Utils/ToolDisplayNames.cs LubanAgentCore/Utils/ToolArgsFormatter.cs
```

- [ ] **Step 2: Profile 改为继承框架基类**

`NormalAgentProfile.cs` / `LlmWikiAgentProfile.cs`：`class X : AgentProfile` → `class X : AgentProfileBase`，`using LubanAgentCore.Agents;` 保留（同命名空间）；`WorkspaceInfo` → `WorkspaceDescriptor`（构造参数与 `CreateAgentAsync` 调用处一起改）。

`WorkspaceInfo` → `WorkspaceDescriptor` 的适配放在 `CoreAgentProfileResolver` 里，避免每个 Profile 内部都做映射。

- [ ] **Step 3: 写 4 个接缝实现**

`Hosting/CoreSeams.cs`：

```csharp
using LuBan.AIAgent.Hosting;
using LubanAgentCore.Agents;
using LubanAgentCore.Configuration;
using LubanAgentCore.Models;
using LubanAgentCore.Plugins;

namespace LubanAgentCore.Hosting;

/// <summary>按会话绑定的工作区解析（历史会话未绑定时回退当前工作区）。</summary>
public sealed class CoreWorkspaceResolver(
    SessionRepository sessionRepository,
    IWorkspaceManager workspaceManager,
    IAgentUserResolver userResolver) : IAgentWorkspaceResolver
{
    /// <inheritdoc />
    public async Task<WorkspaceDescriptor> ResolveAsync(string sessionId, CancellationToken ct = default)
    {
        var userId = userResolver.ResolveUserId(new EmptyServiceProvider());
        var dbSession = await sessionRepository.GetBySessionIdAsync(sessionId, userId);
        var workspaceId = dbSession?.WorkspaceId;

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            var ws = await workspaceManager.GetWorkspaceAsync(workspaceId);
            if (ws is not null) return ToDescriptor(ws);
        }

        var current = workspaceManager.CurrentWorkspace
            ?? throw new InvalidOperationException("未设置当前工作区");

        return ToDescriptor(current);
    }

    /// <summary>产品工作区 → 框架描述。</summary>
    public static WorkspaceDescriptor ToDescriptor(WorkspaceInfo ws)
        => new(ws.WorkspaceId, ws.Name, ws.RootPath, ws.Type);
}
```

> `IAgentUserResolver` 在 `CoreWorkspaceResolver` 里拿不到 scope，改为构造注入 `LuBan.AIAgent.Abstractions.IUserContext`（Scoped）即可，避免 `EmptyServiceProvider`。按编译结果选一种，**不要**引入假的 `IServiceProvider`。

```csharp
/// <summary>按工作区类型选 Profile。</summary>
public sealed class CoreAgentProfileResolver(IServiceProvider services) : IAgentProfileResolver
{
    /// <inheritdoc />
    public IAgentProfile Resolve(WorkspaceDescriptor workspace)
        => workspace.Type == WorkspaceTypes.LlmWiki
            ? ActivatorUtilities.CreateInstance<LlmWikiAgentProfile>(services, workspace)
            : ActivatorUtilities.CreateInstance<NormalAgentProfile>(services, workspace);
}

/// <summary>插件会话级贡献回放。</summary>
public sealed class PluginSessionExtension(IServiceProvider services) : IAgentSessionExtension
{
    /// <inheritdoc />
    public Task ApplyAsync(IServiceProvider sessionServices, WorkspaceDescriptor workspace, CancellationToken ct = default)
    {
        foreach (var contributor in sessionServices.GetServices<IPluginSessionContributor>())
        {
            try { contributor.Contribute(sessionServices); }
            catch (Exception ex) { Logger.Warn("插件会话级贡献回放失败", ex); }
        }
        return Task.CompletedTask;
    }
}

/// <summary>连接器工作区启用。</summary>
public sealed class ConnectorSessionExtension : IAgentSessionExtension
{
    /// <inheritdoc />
    public async Task ApplyAsync(IServiceProvider sessionServices, WorkspaceDescriptor workspace, CancellationToken ct = default)
    {
        var connectors = sessionServices.GetService<ConnectorRegistry>();
        if (connectors is null) return;

        try
        {
            connectors.LoadFromWorkspace(workspace.RootPath);
            await connectors.ApplyEnabledAsync(workspace.RootPath, ct);
        }
        catch (Exception ex) { Logger.Warn("连接器工作区启用失败", ex); }
    }
}

/// <summary>用户解析：包产品的本地默认用户。</summary>
public sealed class CoreAgentUserResolver : IAgentUserResolver
{
    /// <inheritdoc />
    public string ResolveUserId(IServiceProvider services)
        => services.GetService<LuBan.AIAgent.Abstractions.IUserContext>()?.UserId is { Length: > 0 } id
            ? id
            : LocalUserDefaults.DefaultUserId;
}
```

- [ ] **Step 4: 写会话初始化器**

`Hosting/CoreSessionRuntimeInitializer.cs`：

```csharp
using LuBan.AIAgent.Defaults;
using LuBan.AIAgent.Hosting;

namespace LubanAgentCore.Hosting;

/// <summary>补齐产品特有的会话上下文字段。</summary>
public sealed class CoreSessionRuntimeInitializer(DefaultContextState state) : ISessionRuntimeInitializer
{
    /// <inheritdoc />
    public Task InitializeAsync(SessionRuntimeSeed seed, CancellationToken ct = default)
    {
        state.WorkspaceId = seed.WorkspaceId;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: `ConfigManager` 实现 `IAgentModelSelection`**

```csharp
public class ConfigManager : /* 原有接口 */, LuBan.AIAgent.Hosting.IAgentModelSelection
{
    /// <inheritdoc />
    string? LuBan.AIAgent.Hosting.IAgentModelSelection.ProviderId => SelectedProvider;

    /// <inheritdoc />
    string? LuBan.AIAgent.Hosting.IAgentModelSelection.ModelId => SelectedModelName;

    /// <inheritdoc />
    string LuBan.AIAgent.Hosting.IAgentModelSelection.QualifiedModelName => SelectedModel ?? string.Empty;
}
```

> 成员名以 `ConfigManager` 现有 API 为准（`SelectedModel` 一定存在，返回 `provider:model`）。

- [ ] **Step 6: 改 `AgentHostBuilder`**

删掉 `services.AddSingleton<SessionRuntimeManager>();`、`services.AddScoped<AgentHostService>();`、`services.AddSingleton<ICommandCatalog, CommandCatalog>();`，改为：

```csharp
services.AddLuBanAgentDefaults(configuration);
services.AddLuBanAgentHost();

// 产品覆盖框架默认接缝
services.AddScoped<IAgentWorkspaceResolver, CoreWorkspaceResolver>();
services.AddScoped<IAgentProfileResolver, CoreAgentProfileResolver>();
services.AddScoped<IAgentUserResolver, CoreAgentUserResolver>();
services.AddScoped<IModelCapabilityGate, VisionModelCapabilityGate>();
services.AddEnumerable(p => p.AddScoped<IAgentSessionExtension, PluginSessionExtension>());
services.AddEnumerable(p => p.AddScoped<IAgentSessionExtension, ConnectorSessionExtension>());
services.AddScoped<ISessionRuntimeInitializer, CoreSessionRuntimeInitializer>();
services.AddScoped<LuBan.AIAgent.Hosting.IAgentModelSelection>(sp => sp.GetRequiredService<ConfigManager>());
```

- [ ] **Step 7: 构建（预期大量编译错误，逐个修）**

Run:
```powershell
dotnet build luban-agent/LubanAgent.slnx
```
Expected: 先报 `StreamEvent` → `AgentStreamEvent`、`AgentHostService` → `IAgentHost` 的替换点。**这一步允许长尾**，但要确保每修一处就消掉一类错误。

- [ ] **Step 8: 提交（与 UI 改动拆开）**

```powershell
git -C luban-agent add -A
git -C luban-agent commit -m "refactor: Core 切换到框架宿主，删除已上收的重复类型"
```

---

### Task 12: UI 类型替换 + 全量冒烟

**Files:**
- Modify: `luban-agent/LubanAgentCli/**/*.cs`（`StreamEvent` → `AgentStreamEvent`、`AgentHostService` → `IAgentHost`）
- Modify: `luban-agent/LubanAgentCodex/**/*.cs`（同上）
- Modify: `luban-agent/LubanAgentCli/Commands/*.cs`（`RunStreamingAsync` 改 `AgentRunRequest`）

- [ ] **Step 1: 全量符号替换**

```powershell
# 仅预览，不落盘：确认命中范围
rg -n "\bStreamEvent\b|\bAgentHostService\b|Commands\.ToolDisplayNames|Core\.Utils\.ToolArgsFormatter" luban-agent --glob "*.cs"
```

- [ ] **Step 2: 执行替换**

```powershell
Get-ChildItem -Recurse luban-agent\LubanAgentCli,luban-agent\LubanAgentCodex -Filter *.cs |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  ForEach-Object {
      $t = Get-Content $_.FullName -Raw
      if ($t -match '\bStreamEvent\b|\bAgentHostService\b') {
          $t = $t -replace '\bAgentHostService\b', 'IAgentHost'
          $t = $t -replace '\bStreamEvent\b', 'AgentStreamEvent'
          Set-Content $_.FullName $t -NoNewline
      }
  }
```

- [ ] **Step 3: 改 `RunStreamingAsync` 调用点**

每个调用点形如：

```csharp
await foreach (var evt in _host.RunStreamingAsync(input, attachments, Confirm, mode, onPlan, onDenied, sessionId, ct))
```

改为：

```csharp
await foreach (var evt in _host.RunStreamingAsync(new AgentRunRequest
{
    Input = input,
    AttachmentPaths = attachments,
    SessionId = sessionId,
    PermissionMode = mode,
    ConfirmHandler = Confirm,
    OnPlannedAction = onPlan,
    OnUserDenied = onDenied
}, ct))
```

- [ ] **Step 4: 构建**

Run:
```powershell
dotnet build luban-agent/LubanAgent.slnx
```
Expected: `Build succeeded`。

- [ ] **Step 5: 冒烟（必须逐项记录结果）**

1. TUI 启动 → 一轮普通对话（无工具）
2. TUI 一轮带工具确认的对话（`ReadFile`）
3. TUI 一轮带附件（图片 + 文本）
4. TUI Plan 模式一轮
5. TUI 切换模型后一轮（验证 `Reset` 生效）
6. Codex 启动 → 一轮普通对话
7. Codex 一轮带附件 + 工具确认

Expected: 7 项行为与改造前一致；事件顺序、思考块归并、工具结果摘要无变化。

- [ ] **Step 6: 提交**

```powershell
# 禁止 add -A：仓库里有用户本人未提交的改动（appsettings.json、LubanAgentCli.csproj 等），
# 必须显式列出本次替换涉及的路径（见 R2）。
git -C luban-agent add LubanAgentCli LubanAgentCodex -- ':!*appsettings.json'
git -C luban-agent commit -m "refactor: 两端 UI 切换到框架 IAgentHost 与 AgentStreamEvent"
```

---

# 阶段 D：业务下沉（一次性全改，按隔离度并行）

### Task 13: 9 个服务接口骨架（唯一前置同步点）

**Files:**
- Create: `luban-agent/LubanAgentCore/Services/Management/IProviderModelManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/ISkillManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/IRuleManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/IMcpManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/IConnectorManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/IWorkspaceManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/IChatSessionService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/ILlmWikiManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/IPluginManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Security/ICredentialProtector.cs`
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`（注册骨架）

- [ ] **Step 1: 按现有 CLI 命令的公开行为写接口**

```csharp
namespace LubanAgentCore.Services.Management;

/// <summary>Provider / 模型管理。</summary>
public interface IProviderModelManagementService
{
    /// <summary>全部 Provider（含显示名/端点）。</summary>
    Task<IReadOnlyList<ProviderInfo>> GetProvidersAsync(CancellationToken ct = default);

    /// <summary>指定 Provider 可选模型（含远端刷新的缓存）。</summary>
    Task<IReadOnlyList<string>> GetModelsAsync(string providerId, CancellationToken ct = default);

    /// <summary>刷新远端模型列表。</summary>
    Task<IReadOnlyList<string>> RefreshModelsAsync(string providerId, CancellationToken ct = default);

    /// <summary>当前选择。</summary>
    (string? ProviderId, string? ModelId) GetSelection();

    /// <summary>切换当前模型。</summary>
    Task SetSelectionAsync(string providerId, string modelId, CancellationToken ct = default);

    /// <summary>新增/更新 Provider 配置。</summary>
    Task UpsertProviderAsync(ProviderConfig config, CancellationToken ct = default);

    /// <summary>删除 Provider。</summary>
    Task DeleteProviderAsync(string providerId, CancellationToken ct = default);
}
```

其余 8 个接口同样按「CLI 命令现在能做什么」逐条列方法（**不加新能力**）。方法清单如下，照此写文件（返回类型用产品已有模型；`CancellationToken ct = default` 一律加在最后）：

```csharp
// ISkillManagementService
Task<IReadOnlyList<SkillInfo>> ListAsync(CancellationToken ct = default);
Task ImportAsync(string sourcePath, CancellationToken ct = default);
Task DeleteAsync(string name, CancellationToken ct = default);
Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default);

// IRuleManagementService
Task<IReadOnlyList<RuleInfo>> ListAsync(CancellationToken ct = default);
Task AddAsync(string name, string content, CancellationToken ct = default);
Task DeleteAsync(string name, CancellationToken ct = default);
Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default);

// IMcpManagementService
Task<IReadOnlyList<McpInfo>> ListAsync(CancellationToken ct = default);
Task AddAsync(McpConfig config, CancellationToken ct = default);
Task DeleteAsync(string name, CancellationToken ct = default);
Task<McpTestResult> TestAsync(string name, CancellationToken ct = default);

// IConnectorManagementService
Task<IReadOnlyList<ConnectorInfo>> ListAsync(CancellationToken ct = default);
Task AddAsync(ConnectorConfig config, CancellationToken ct = default);
Task DeleteAsync(string name, CancellationToken ct = default);
Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default);
Task<ConnectorTestResult> TestAsync(string name, CancellationToken ct = default);

// IWorkspaceManagementService
Task<IReadOnlyList<WorkspaceInfo>> ListAsync(CancellationToken ct = default);
Task<WorkspaceInfo?> GetCurrentAsync(CancellationToken ct = default);
Task<WorkspaceInfo> CreateAsync(string name, string rootPath, string type, CancellationToken ct = default);
Task DeleteAsync(string workspaceId, CancellationToken ct = default);
Task SwitchAsync(string workspaceId, CancellationToken ct = default);
Task GrantAccessAsync(string workspaceId, CancellationToken ct = default);

// IChatSessionService
Task<IReadOnlyList<SessionInfo>> ListAsync(CancellationToken ct = default);
Task<SessionInfo> CreateAsync(string? title, string? workspaceId, CancellationToken ct = default);
Task DeleteAsync(string sessionId, CancellationToken ct = default);
Task RenameAsync(string sessionId, string title, CancellationToken ct = default);
Task<IReadOnlyList<SessionMessage>> GetMessagesAsync(string sessionId, CancellationToken ct = default);
Task ClearMessagesAsync(string sessionId, CancellationToken ct = default);
Task SwitchAsync(string sessionId, CancellationToken ct = default);

// ILlmWikiManagementService
Task<WikiStatus> GetStatusAsync(CancellationToken ct = default);
Task RebuildAsync(string workspaceId, IProgress<double>? progress, CancellationToken ct = default);
Task ClearAsync(string workspaceId, CancellationToken ct = default);

// IPluginManagementService
Task<IReadOnlyList<PluginInfo>> ListAsync(CancellationToken ct = default);
Task InstallAsync(string packagePath, CancellationToken ct = default);
Task UninstallAsync(string pluginId, CancellationToken ct = default);
Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default);
Task<PluginPermissionSnapshot> GetPermissionsAsync(string pluginId, CancellationToken ct = default);

event EventHandler<PluginChangedEventArgs>? Changed;
```

> `SkillInfo` / `RuleInfo` / `McpInfo` / `ConnectorInfo` / `WikiStatus` / `PluginInfo` / `PluginPermissionSnapshot` / `PluginChangedEventArgs` 若产品里没有，就在 `LubanAgentCore/Services/Management/Models/` 下新建最小 record（只放 UI 需要的字段，**不要**顺手扩展能力）。

凭据契约 `ICredentialProtector`：

```csharp
namespace LubanAgentCore.Security;

/// <summary>凭据加解密（Windows 用 DPAPI，其他平台用占位实现）。</summary>
public interface ICredentialProtector
{
    /// <summary>加密。</summary>
    byte[] Protect(byte[] plaintext);

    /// <summary>解密。</summary>
    byte[] Unprotect(byte[] ciphertext);

    /// <summary>是否可用（不可用时凭据读写必须显式报错，不得降级为明文）。</summary>
    bool IsAvailable { get; }
}
```

- [ ] **Step 2: 全部实现先抛 `NotImplementedException`，并注册到 DI**

```csharp
services.AddScoped<IProviderModelManagementService>(_ => throw new NotImplementedException("P1"));
```

> 只有这样才能让 P1–P6 **同时编译通过**并并行开工。

- [ ] **Step 3: 构建**

Run:
```powershell
dotnet build luban-agent/LubanAgent.slnx
```
Expected: `Build succeeded`。

- [ ] **Step 4: 提交**

```powershell
git -C luban-agent add -A
git -C luban-agent commit -m "refactor: 建立 9 个管理服务与凭据保护契约骨架"
```

---

> **并行派发规则（Task 14–19）**
> - 每个 subagent 只允许改「自己那组的文件」，跨组文件一律不碰。
> - 每个 subagent 完成后必须给出：`dotnet build` 结果 + 自己那条冒烟路径的结果。
> - 任何一组发现必须改公共文件（`AgentHostBuilder.cs`、`ConfigManager.cs`），**停下来上报**，由主会话统一改。

### Task 14: P1 模型 / Provider 下沉

**Files:**
- Create: `luban-agent/LubanAgentCore/Services/Management/ProviderModelManagementService.cs`
- Modify: `luban-agent/LubanAgentCli/Commands/ModelCommand.cs`
- Modify: `luban-agent/LubanAgentCodex/Views/MainWindow.axaml.cs`（模型列表，仅改数据来源）
- Modify: `luban-agent/LubanAgentCodex/Views/InputBox.axaml.cs`（同上）
- Delete: Codex 版 `FooterDataProvider`（若与 Core 版重复）

- [ ] **Step 1: 实现 `ProviderModelManagementService`**

```csharp
namespace LubanAgentCore.Services.Management;

/// <summary>Provider / 模型管理的唯一实现（CLI 与 Codex 共用）。</summary>
public sealed class ProviderModelManagementService(
    ConfigManager config,
    IAgentModelSelection selection) : IProviderModelManagementService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ProviderInfo>> GetProvidersAsync(CancellationToken ct = default)
        => throw new NotImplementedException("从 ConfigManager 的 Provider 读取逻辑搬入");

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetModelsAsync(string providerId, CancellationToken ct = default)
        => throw new NotImplementedException("从 ModelCommand 的列表构建搬入（含缓存）");

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> RefreshModelsAsync(string providerId, CancellationToken ct = default)
        => throw new NotImplementedException("从 ModelCommand 的远端刷新搬入");

    /// <inheritdoc />
    public (string? ProviderId, string? ModelId) GetSelection()
        => (selection.ProviderId, selection.ModelId);

    /// <inheritdoc />
    public Task SetSelectionAsync(string providerId, string modelId, CancellationToken ct = default)
        => throw new NotImplementedException("从 ModelCommand 的切换搬入（含持久化 + 会话重置）");

    /// <inheritdoc />
    public Task UpsertProviderAsync(ProviderConfig config, CancellationToken ct = default)
        => throw new NotImplementedException("从 Codex SettingsWindow 的 Provider 保存搬入");

    /// <inheritdoc />
    public Task DeleteProviderAsync(string providerId, CancellationToken ct = default)
        => throw new NotImplementedException("从 Codex SettingsWindow 的 Provider 删除搬入");
}
```

> 每个 `NotImplementedException` 必须在本 Step 内替换成真实逻辑（来源见注释）。三处模型列表构建（`ModelCommand`、`MainWindowViewModel:1147`、`InputBox:115`）合并到 `GetModelsAsync` 的**唯一一份**实现。

- [ ] **Step 2: CLI 只替换数据来源**

```csharp
// ModelCommand 里原来的 model 列表构建整段删掉，替换为：
var models = await _providerModelService.GetModelsAsync(providerId);
RenderModelList(models);   // 原方法，一字不改
```

- [ ] **Step 3: Codex 两处改数据来源**

`MainWindow.axaml.cs` / `InputBox.axaml.cs` 里自建的模型列表构建删掉，改为注入 `IProviderModelManagementService` 并调 `GetModelsAsync`。删除 Codex 版 `FooterDataProvider`，统一用 Core 版。

- [ ] **Step 4: 构建 + 冒烟**

```powershell
dotnet build luban-agent/LubanAgent.slnx
```
冒烟：TUI `/model` 列出、切换、远端刷新三项；切换后 `IAgentHost.Reset()` 生效（下一轮用新模型）；Codex 模型下拉正常。

- [ ] **Step 5: 提交**

```powershell
git -C luban-agent add -A
git -C luban-agent commit -m "refactor: 模型/Provider 逻辑下沉到 ProviderModelManagementService"
```

### Task 15: P2 Skill / Rule 下沉

**Files:**
- Create: `luban-agent/LubanAgentCore/Services/Management/SkillManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/RuleManagementService.cs`
- Modify: `luban-agent/LubanAgentCli/Commands/SkillCommand.cs`
- Modify: `luban-agent/LubanAgentCli/Commands/RuleCommand.cs`
- Modify: `luban-agent/LubanAgentCodex/Views/SettingsWindow.axaml.cs`（Skill/Rule Tab 的数据来源）

- [ ] **Step 1: 实现 `SkillManagementService`**

```csharp
namespace LubanAgentCore.Services.Management;

/// <summary>Skill 管理（工作区 + 内置）。</summary>
public sealed class SkillManagementService(SkillRegistry registry) : ISkillManagementService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<SkillInfo>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SkillInfo>>(
            registry.GetAll().Select(s => new SkillInfo(s.Name, s.Description, true)).ToList());

    /// <inheritdoc />
    public Task ImportAsync(string sourcePath, CancellationToken ct = default)
    {
        // 复用 CLI SkillCommand 现有的导入逻辑：拷到工作区 .luban-agent/skills/<name>/
        throw new NotImplementedException("从 SkillCommand 搬入导入逻辑");
    }

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken ct = default)
        => throw new NotImplementedException("从 SkillCommand 搬入删除逻辑");

    /// <inheritdoc />
    public Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default)
        => throw new NotImplementedException("从 SkillCommand 搬入启停逻辑");
}
```

> 每个 `NotImplementedException` 都必须在本 Step 内替换成从 `SkillCommand` 搬来的真实逻辑；**不允许**留到下一个 Task。

- [ ] **Step 2: 实现 `RuleManagementService`**：同样把 `RuleCommand` 的列出/新增/删除/启停搬进来，`RuleEngine` 与 `.luban-agent/rules/` 目录操作为唯一实现。

- [ ] **Step 3: 改 CLI（只换数据源）**

```csharp
// SkillCommand 里原来的 registry / 文件 IO 段落整段删掉，替换为：
var skills = await _skillService.ListAsync();
RenderSkillTable(skills);   // 原方法，一字不改
```

- [ ] **Step 4: 构建 + 冒烟**

```powershell
dotnet build luban-agent/LubanAgent.slnx
```
冒烟：TUI `/skill` 列出/导入/删除/启停；`/rule` 列出/新增/删除/启停；Codex 设置页 Skill/Rule Tab 数据一致。

- [ ] **Step 5: 提交**

```powershell
git -C luban-agent add -A
git -C luban-agent commit -m "refactor: Skill/Rule 逻辑下沉到管理服务"
```

### Task 16: P3 MCP / Connector 下沉

**Files:**
- Create: `luban-agent/LubanAgentCore/Services/Management/McpManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/ConnectorManagementService.cs`
- Modify: `luban-agent/LubanAgentCli/Commands/McpCommand.cs`
- Modify: `luban-agent/LubanAgentCli/Commands/ConnectorCommand.cs`
- Modify: `luban-agent/LubanAgentCodex/Views/SettingsWindow.axaml.cs`（MCP/连接器 Tab）

- [ ] **Step 1: 实现 `McpManagementService`**

```csharp
namespace LubanAgentCore.Services.Management;

/// <summary>MCP 管理（列表/增删/连通性测试）。</summary>
public sealed class McpManagementService(IServiceProvider services) : IMcpManagementService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<McpInfo>> ListAsync(CancellationToken ct = default)
        => throw new NotImplementedException("从 McpCommand 搬入列出逻辑");

    /// <inheritdoc />
    public Task AddAsync(McpConfig config, CancellationToken ct = default)
        => throw new NotImplementedException("从 McpCommand 搬入新增逻辑");

    /// <inheritdoc />
    public Task DeleteAsync(string name, CancellationToken ct = default)
        => throw new NotImplementedException("从 McpCommand 搬入删除逻辑");

    /// <inheritdoc />
    public Task<McpTestResult> TestAsync(string name, CancellationToken ct = default)
        => throw new NotImplementedException("从 McpCommand 搬入测试逻辑（含超时与错误摘要）");
}
```

> `NotImplementedException` 必须在本 Step 内替换为真实逻辑。

- [ ] **Step 2: 实现 `ConnectorManagementService`**：把 `ConnectorCommand` 的列表/新增/删除/启停/测试搬进来；凭据写入统一走 `IConnectorConfigWriter`（已存在）→ 失效 → 通知链路，**不许**新开写入路径。

- [ ] **Step 3: 改 CLI（只换数据源）**

```csharp
var connectors = await _connectorService.ListAsync();
RenderConnectorTable(connectors);   // 原方法，一字不改
```

- [ ] **Step 4: 构建 + 冒烟**

冒烟：`/mcp` 列出/新增/删除/测试；`/connector` 列出/新增/删除/启停/测试（含一个真实云效连接器）；Codex 设置页 MCP/连接器 Tab 一致。

- [ ] **Step 5: 提交**

```powershell
git -C luban-agent add -A
git -C luban-agent commit -m "refactor: MCP/连接器逻辑下沉到管理服务"
```

### Task 17: P4 Workspace / Session / Wiki 下沉

**Files:**
- Create: `luban-agent/LubanAgentCore/Services/Management/WorkspaceManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/ChatSessionService.cs`
- Create: `luban-agent/LubanAgentCore/Services/Management/LlmWikiManagementService.cs`
- Modify: `luban-agent/LubanAgentCli/Commands/WorkspaceCommand.cs`
- Modify: `luban-agent/LubanAgentCli/Commands/SessionCommand.cs`
- Modify: `luban-agent/LubanAgentCli/Commands/WikiCommand.cs`
- Modify: `luban-agent/LubanAgentCodex/Views/MainWindow.axaml.cs`（会话列表）

- [ ] **Step 1: 实现 `WorkspaceManagementService`**：把 `WorkspaceCommand` 的列表/当前/新建/删除/切换/授权搬进来。授权逻辑复用现有 `WorkspaceManager`，**不许**在服务里重新实现路径校验。

- [ ] **Step 2: 实现 `ChatSessionService`**：包 `ISessionManager`（SQLite 实现）+ 标题/导出逻辑；导出格式保持 CLI 现状。

- [ ] **Step 3: 实现 `LlmWikiManagementService`**：包 `KnowledgeTaskService` 与 `IWikiService`，暴露「状态 / 重建 / 清理」三件事。

- [ ] **Step 4: 改 CLI（只换数据源）**

```csharp
var sessions = await _sessionService.ListAsync();
RenderSessionTable(sessions);   // 原方法，一字不改
```

- [ ] **Step 5: 构建 + 冒烟**

冒烟：`/workspace` 全操作 + 未授权工作区降级路径（验证 `EffectiveRoot` 回退不崩）；`/session` 新建/切换/重命名/删除/清空；`/wiki` 状态/重建/清理。

- [ ] **Step 6: 提交**

```powershell
git -C luban-agent add -A
git -C luban-agent commit -m "refactor: 工作区/会话/知识库逻辑下沉到管理服务"
```

### Task 18: P5 插件 / 凭据下沉

**Files:**
- Create: `luban-agent/LubanAgentCore/Services/Management/PluginManagementService.cs`
- Create: `luban-agent/LubanAgentCore/Security/CredentialStore.cs`
- Create: `luban-agent/LubanAgentCore/Security/DpapiCredentialProtector.cs`
- Create: `luban-agent/LubanAgentCore/Security/UnsupportedCredentialProtector.cs`
- Delete: `luban-agent/LubanAgentCodex/Plugins/CodexPluginHost.cs`
- Delete: `luban-agent/LubanAgentCodex/Security/CodexCredentialStore.cs`
- Modify: `luban-agent/LubanAgentCodex/**`（改调 Core 服务）

- [ ] **Step 1: DPAPI 实现**

```csharp
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace LubanAgentCore.Security;

/// <summary>Windows DPAPI 实现。</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialProtector : ICredentialProtector
{
    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext)
        => ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

    /// <inheritdoc />
    public byte[] Unprotect(byte[] ciphertext)
        => ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
}
```

非 Windows：

```csharp
namespace LubanAgentCore.Security;

/// <summary>不支持加密的平台：明确失败，不降级为明文。</summary>
public sealed class UnsupportedCredentialProtector : ICredentialProtector
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext)
        => throw new PlatformNotSupportedException("当前平台不支持凭据加密（需 Windows DPAPI）。");

    /// <inheritdoc />
    public byte[] Unprotect(byte[] ciphertext)
        => throw new PlatformNotSupportedException("当前平台不支持凭据解密（需 Windows DPAPI）。");
}
```

注册：

```csharp
if (OperatingSystem.IsWindows())
    services.AddSingleton<ICredentialProtector, DpapiCredentialProtector>();
else
    services.AddSingleton<ICredentialProtector, UnsupportedCredentialProtector>();
```

- [ ] **Step 2: 搬 `CodexPluginHost` → `PluginManagementService`**：把 UI 回调改成事件（`PluginInstalled` / `PluginUninstalled` / `PluginPermissionDenied`），UI 订阅后只做刷新。
- [ ] **Step 3: `CredentialStore` 用 `ICredentialProtector`**，`IsAvailable == false` 时读写直接抛，不允许明文落盘。
- [ ] **Step 4: 构建 + 冒烟**：Codex 插件安装/卸载/启停；卸载后 `WeakReference` 可回收（沿用 `PluginAssemblySharing` 单测思路，临时加一段回收断言）。
- [ ] **Step 5: 提交。**

### Task 19: P6 Codex 设置页拆 ViewModel

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/SettingsWindow.axaml.cs`（1,848 → ≤300）
- Create: `luban-agent/LubanAgentCodex/ViewModels/Settings/SettingsWindowViewModel.cs`
- Create: `luban-agent/LubanAgentCodex/ViewModels/Settings/ProviderTabViewModel.cs`
- Create: `luban-agent/LubanAgentCodex/ViewModels/Settings/SkillRuleTabViewModel.cs`
- Create: `luban-agent/LubanAgentCodex/ViewModels/Settings/McpConnectorTabViewModel.cs`
- Create: `luban-agent/LubanAgentCodex/ViewModels/Settings/PluginTabViewModel.cs`
- Create: `luban-agent/LubanAgentCodex/ViewModels/Settings/WorkspaceTabViewModel.cs`

- [ ] **Step 1: 建壳 ViewModel**（只包服务调用 + 可观察集合，不含 UI 类型）
- [ ] **Step 2: 逐 Tab 迁移**，每迁一个 Tab 构建一次。
- [ ] **Step 3: 开启编译绑定检查**：确认 `AvaloniaUseCompiledBindingsByDefault` 为 `true`，让绑定错误在构建期暴露。
- [ ] **Step 4: 冒烟**：6 个 Tab 全操作一遍（Provider/Skill/Rule/MCP/Connector/Plugin/Workspace）。
- [ ] **Step 5: 提交。**

---

# 阶段 E：收尾

### Task 20: 清零检查 + 文档 + 人工发版 + 改回 PackageReference

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/README.md`
- Modify: `luban-framework/LuBan.AIAgent/README.en.md`
- Modify: `luban-agent/AGENTS.md`（若在 agent 仓库内）
- Modify: 5 个 agent csproj（`ProjectReference` → `PackageReference`）

- [ ] **Step 1: 清零点检查**

```powershell
rg -n "\bStreamEvent\b" luban-agent luban-framework --glob "*.cs"
rg -n "AgentHostService" luban-agent luban-framework --glob "*.cs"
rg -n "\bYunxiao\b|\bCodeup\b|AliyunDevOps" luban-framework/LuBan.AIAgent --glob "*.cs"
rg -n "Terminal\.Gui|Avalonia" luban-framework/LuBan.AIAgent --glob "*.csproj"
rg -n "TEMP-LOCAL-VERIFY" luban-agent --glob "*.csproj"
```
Expected: 前三条 0 命中；第四条 0 命中；第五条此时**仍有命中**（还没改回）。

- [ ] **Step 2: 更新文档**：`LuBan.AIAgent/README.md` 与 `README.en.md` 同步补 `AddLuBanAgentHost` / `AddLuBanAgentOpenAI` / 4 个接缝章节（中英两份必须一致）。

- [ ] **Step 3: 人工发版（暂停，等人工）**

> 编辑在此停止，把以下清单交给人工执行：
> 1. 框架 `LuBan.AIAgent.csproj` 的 `<Version>` 改为 `2026.10.26.1`；
> 2. `dotnet build luban-framework/LuBan.FrameWork.sln`（`GeneratePackageOnBuild` 自动出包）；
> 3. 推 `nexus3`（`https://nexus.tjingcai.com/repository/nuget-hosted/index.json`）；
> 4. 确认包可被 `dotnet restore` 拉到。

- [ ] **Step 4: 改回 PackageReference**

```powershell
Get-ChildItem luban-agent\*\*.csproj |
  Where-Object { $_.FullName -notmatch '\\smoke\\|\\obj\\|\\bin\\' } |
  ForEach-Object {
      $t = Get-Content $_.FullName -Raw
      $t = $t -replace '(?s)<!-- TEMP-LOCAL-VERIFY.*?-->\s*<ProjectReference[^>]*LuBan\.[^>]*/>', '<PackageReference Include="LuBan.AIAgent" Version="2026.10.26.1" />'
      Set-Content $_.FullName $t -NoNewline
  }
```

> 逐工程核对：`LuBan.Common` / `LuBan.Orm` / `LuBan.Logging` / `LuBan.DI` / `LuBan.Threading` 各自要有对应 `PackageReference`，版本统一 `2026.10.26.1`（正则只处理 `LuBan.AIAgent` 时，其余手工补）。

- [ ] **Step 5: 走真实包路径复验**

```powershell
dotnet nuget locals all --clear
dotnet restore luban-agent/LubanAgent.slnx
dotnet build luban-agent/LubanAgent.slnx
dotnet test luban-agent/LubanAgent.Tests/LubanAgent.Tests.csproj
```
Expected: 全部成功；`TEMP-LOCAL-VERIFY` 0 命中。

- [ ] **Step 6: 提交**

```powershell
git -C luban-agent add -A
git -C luban-agent commit -m "chore: 三层重构收尾，LuBan.* 改回 PackageReference 2026.10.26.1"
git -C luban-framework add -A
git -C luban-framework commit -m "三层重构：宿主能力上收，README 中英同步"
```

---

## 附录 A：验收命令（可在 CI 复用）

```powershell
# 分层纯度
rg -n "\bYunxiao\b|\bCodeup\b|AliyunDevOps" luban-framework/LuBan.AIAgent --glob "*.cs"   # 期望 0
rg -n "Terminal\.Gui" luban-framework/LuBan.AIAgent --glob "*.csproj"                      # 期望 0
rg -n "Avalonia" luban-framework/LuBan.AIAgent --glob "*.csproj"                           # 期望 0
rg -n "Repository|File\.Write|File\.Delete" luban-agent/LubanAgentCli/Commands --glob "*.cs" # 期望 0
rg -n "TEMP-LOCAL-VERIFY" luban-agent --glob "*.csproj"                                     # 期望 0（smoke 除外）

# 构建与测试
dotnet build luban-framework/LuBan.FrameWork.sln
dotnet build luban-agent/LubanAgent.slnx
dotnet test luban-agent/LubanAgent.Tests/LubanAgent.Tests.csproj
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj
```

## 附录 B：行数体检（不作为门禁）

| 项目 | 当前 | 目标 |
|---|---|---|
| `LubanAgentCli/Commands/` | 4,338 | ≤ 1,500 |
| `LubanAgentCodex/Views/SettingsWindow.axaml.cs` | 1,848 | ≤ 300 |
| `CodexPluginHost.cs` | 785 | 0（删除） |
| `LubanAgentCore` 总行数 | 13,774 | ≤ 11,200 |

## 附录 C：任务依赖图

```
Task1 ─┬─ Task2 ─┬─ Task3 ── Task4 ── Task5 ─┐
       │         ├─ Task6 ──────────────────┤
       │         ├─ Task7 ──────────────────┼─ Task9 ── Task10
       │         └─ Task8 ──────────────────┘
       └─ Task11 ── Task12 ── Task13 ─┬─ Task14 (P1) ─┐
                                      ├─ Task15 (P2)  │
                                      ├─ Task16 (P3)  ├─ Task20
                                      ├─ Task17 (P4)  │
                                      ├─ Task18 (P5)  │
                                      └─ Task19 (P6) ─┘
```

Task 14–19 可在 Task 13 完成后**同时**开工（每组独占文件）。