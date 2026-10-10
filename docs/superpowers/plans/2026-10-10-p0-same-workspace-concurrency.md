# P0 端到端同工作区并发 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让单进程内同一工作区的多个会话并发运行互不串扰，并交付 git worktree 会话隔离与 Codex 自由切换。

**Architecture:** 单 DI 容器内为每个活动会话创建嵌套 `IServiceScope`（L3），把工具确认链、插件链、`PathGuard`、`TempDirectory`、会话历史从进程级单例改为会话作用域；新增 `IUserContext`/`IWorkspaceContext`/`ISessionContext`/`IWorktreeCoordinator` 抽象（框架定义、agent 实现）。会话运行态由 `SessionRuntime`（回合队列+运行循环+事件流）承载，`SessionRuntimeManager` 负责创建/激活/空闲卸载。worktree 生命周期由 `WorktreeSessionManager` 协调，主树 merge 冲突即停交人工。

**Tech Stack:** .NET 10、Microsoft.Extensions.DependencyInjection、Microsoft Agent Framework（`ChatClientAgent`）、SqlSugar(SQLite)、Avalonia(Codex)、Terminal.Gui(CLI)、MSTest。

**Repo 根:**
- framework: `D:\WorkBench\Walle\luban\luban-framework`（子仓库）
- agent: `D:\WorkBench\Walle\luban\luban-agent`（子仓库）

**开发期框架引用策略:** 为避免反复发框架 nupkg，agent 侧对**全部 `LuBan.*` 框架引用**已改为 `ProjectReference`（提交 `de822af`，带 `TEMP-LOCAL-VERIFY` 标记）。P0/P1/P2 全程走项目引用，构建即用框架最新源码；**三阶段全部验收后统一发最终版本包并还原为 `PackageReference`（见 P2 计划 Phase N）**。因此本计划**不发布中间包**，Task A8 仅递增版本号占位。

**依赖顺序:** Phase A（框架）→ Phase B（agent 底座）→ Phase C（worktree）→ Phase D（Codex UI）→ Phase E（验证）。A 与 C 的 git 工具部分可在框架侧先行；B/C/D 经项目引用直接使用 A 的源码。

---

## File Structure（本计划涉及的文件与职责）

### framework `luban-framework`
- Create `LuBan.AIAgent/Abstractions/IUserContext.cs` — 用户级上下文契约
- Create `LuBan.AIAgent/Abstractions/IWorkspaceContext.cs` — 工作区级上下文契约
- Create `LuBan.AIAgent/Abstractions/ISessionContext.cs` — 会话级上下文契约
- Create `LuBan.AIAgent/Abstractions/IWorktreeCoordinator.cs` — worktree 协调契约
- Create `LuBan.AIAgent/Abstractions/Models/WorktreeSession.cs` — worktree 会话记录
- Modify `LuBan.AIAgent/Abstractions/ToolConfirmationService.cs:56,378,436` — 确认上下文/服务语义保持，供 scoped 注册
- Modify `LuBan.AIAgent/LuBanAgentExtensions.cs:48-59,63,103-104` — 插件链/注册表/确认服务 singleton→scoped
- Modify `LuBan.AIAgent/Infrastructure/PathGuard.cs:29,37,47` — 改 session-scoped，读 `ISessionContext.AllowedRoots`
- Modify `LuBan.Common/IO/TempDirectory.cs:24-70` — `Resolver`→AsyncLocal provider `Push()`
- Modify `LuBan.AIAgent/Sessions/SessionChatHistoryProvider.cs:26-32,74,157` — 依赖 `ISessionContext` 取代 `ISessionManager.CurrentSession`
- Modify `LuBan.AIAgent/Tools/FileSystem/FileSystemToolPlugin.cs:37,60-62` — 根解析改 `ISessionContext.EffectiveRoot`
- Modify `LuBan.AIAgent/Tools/Git/GitToolPlugin.cs:16,42-49` — 同上 + 注册 worktree 工具
- Modify `LuBan.AIAgent/Tools/Git/GitToolPlugin.cs:87-131`（`GitToolGroup`）— 增 worktree 工具方法
- Modify `LuBan.AIAgent/LuBan.AIAgent.csproj:12` — 版本递增
- Test `LuBan.XTestProject/**` — 作用域隔离单测

### agent `luban-agent`
- Create `LubanAgentCore/Context/UserContext.cs` / `WorkspaceContext.cs` / `SessionContext.cs` / `SessionContextState.cs`
- Create `LubanAgentCore/Services/SessionRuntime.cs` / `SessionRuntimeManager.cs`
- Create `LubanAgentCore/Services/WorktreeSessionManager.cs`
- Create `LubanAgentCore/Models/WorktreeSessionEntity.cs` + Repository
- Modify `LubanAgentCore/Hosting/AgentHostBuilder.cs:57-118,140` — 注册上下文、Runtime、ValidateScopes、AgentHostService
- Modify `LubanAgentCore/Services/SessionManager.cs:32,50` — 去 `_currentSession` 静态
- Modify `LubanAgentCore/Services/WorkspaceManager.cs:146,165,174,203,217,360,396,404,622-647` — 去静态/CWD/options 改写
- Modify `LubanAgentCore/Services/AgentHostService.cs:23-55,241` — 改无状态路由器（按 sessionId 路由到 SessionRuntime）
- Create `LubanAgentCore/Agents/...` 或 `Context/AgentContext.cs` — 门面
- Modify `LubanAgentA2A/Executor/InProcessSubAgentExecutor.cs:44,58` — 去静态确认作用域
- Modify `LubanAgentCodex/App.axaml.cs:105-158` / `ViewModels/MainWindowViewModel.cs` — 多会话 UI
- Modify `LubanAgentCodex/Views/Controls/Sidebar.axaml.cs:364-460` — 运行徽标
- Modify `LubanAgentCli/appsettings.json:219`、`LubanAgentCodex/appsettings.json:215` — WAL

---

## Phase A — framework：作用域基础设施（项目引用，不发包）

### Task A1: 新增上下文与 worktree 契约（框架 Abstractions）

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Abstractions/IUserContext.cs`
- Create: `luban-framework/LuBan.AIAgent/Abstractions/IWorkspaceContext.cs`
- Create: `luban-framework/LuBan.AIAgent/Abstractions/ISessionContext.cs`
- Create: `luban-framework/LuBan.AIAgent/Abstractions/IWorktreeCoordinator.cs`
- Create: `luban-framework/LuBan.AIAgent/Abstractions/Models/WorktreeSession.cs`
- Test: `luban-framework/LuBan.XTestProject/AIAgent/WorktreeSessionTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Abstractions.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LuBan.UnitTestProject.AIAgent;

[TestClass]
public class WorktreeSessionTests
{
    [TestMethod]
    public void WorktreeSession_ShouldBe_Immutable_Record()
    {
        var s = new WorktreeSession
        {
            SessionId = "s1",
            WorkspaceId = "w1",
            WorktreePath = @"C:\repo\.worktrees\20261010-120000",
            Branch = "luban-worktree/20261010-120000",
            BaseBranch = "main",
            BaseCommit = "abc123",
            CreatedAt = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero)
        };
        var s2 = s with { Branch = "other" };
        Assert.AreEqual("luban-worktree/20261010-120000", s.Branch);
        Assert.AreEqual("other", s2.Branch);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WorktreeSessionTests"`
Expected: FAIL，`WorktreeSession` 不存在。

- [ ] **Step 3: 新增契约与模型**

`IUserContext.cs`:
```csharp
namespace LuBan.AIAgent.Abstractions;

/// <summary>用户级上下文（root/keyed）。</summary>
public interface IUserContext
{
    string? UserId { get; }
}
```

`IWorkspaceContext.cs`:
```csharp
namespace LuBan.AIAgent.Abstractions;

/// <summary>工作区级上下文（workspace-scoped；P0 由会话作用域承载）。</summary>
public interface IWorkspaceContext
{
    string? WorkspaceId { get; }
    string? RootPath { get; }
    string? RootPathFor(string? workspaceId);
}
```

`ISessionContext.cs`:
```csharp
using LuBan.AIAgent.Abstractions.Models;

namespace LuBan.AIAgent.Abstractions;

/// <summary>会话级上下文（session-scoped）。所有路径按 <see cref="EffectiveRoot"/> 解析。</summary>
public interface ISessionContext
{
    string? SessionId { get; }
    /// <summary>有活动 worktree 即 worktree 路径，否则工作区根。</summary>
    string EffectiveRoot { get; }
    WorktreeSession? ActiveWorktree { get; }
    IReadOnlyList<string> AllowedRoots { get; }
    string ResolveTempDirectory();
    bool IsWithinEffectiveRoot(string path);
}
```

`IWorktreeCoordinator.cs`:
```csharp
using LuBan.AIAgent.Abstractions.Models;

namespace LuBan.AIAgent.Abstractions;

/// <summary>worktree 会话生命周期协调（宿主实现）。</summary>
public interface IWorktreeCoordinator
{
    Task<WorktreeSession> BeginAsync(string reason, CancellationToken cancellationToken = default);
    Task<WorktreeMergeResult> EndAsync(CancellationToken cancellationToken = default);
    Task AbandonAsync(CancellationToken cancellationToken = default);
    WorktreeSession? GetActive(string sessionId);
}

/// <summary>merge 结果。</summary>
public sealed record WorktreeMergeResult(
    bool Merged,
    bool Conflict,
    string Message,
    IReadOnlyList<string>? ConflictFiles = null);
```

`Models/WorktreeSession.cs`:
```csharp
namespace LuBan.AIAgent.Abstractions.Models;

/// <summary>一次 worktree 会话的登记记录。</summary>
public sealed record WorktreeSession
{
    public required string SessionId { get; init; }
    public required string WorkspaceId { get; init; }
    public required string WorktreePath { get; init; }
    public required string Branch { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseCommit { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WorktreeSessionTests"`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/Abstractions/IUserContext.cs LuBan.AIAgent/Abstractions/IWorkspaceContext.cs LuBan.AIAgent/Abstractions/ISessionContext.cs LuBan.AIAgent/Abstractions/IWorktreeCoordinator.cs LuBan.AIAgent/Abstractions/Models/WorktreeSession.cs LuBan.XTestProject/AIAgent/WorktreeSessionTests.cs
git -C luban-framework commit -m "新增会话/工作区/worktree 上下文抽象与模型"
```

---

### Task A2: 工具确认链 + 插件链 singleton→scoped

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/LuBanAgentExtensions.cs:48-59,63,103-104`
- Test: `luban-framework/LuBan.XTestProject/AIAgent/ScopedPluginChainTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LuBan.UnitTestProject.AIAgent;

[TestClass]
public class ScopedPluginChainTests
{
    private static ServiceProvider Build()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLuBanAgent(config);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [TestMethod]
    public void ToolConfirmationContext_DiffersPerScope()
    {
        using var sp = Build();
        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();
        var a = scope1.ServiceProvider.GetRequiredService<ToolConfirmationContext>();
        var b = scope2.ServiceProvider.GetRequiredService<ToolConfirmationContext>();
        Assert.AreNotSame(a, b);
    }

    [TestMethod]
    public void ToolPluginRegistry_DiffersPerScope()
    {
        using var sp = Build();
        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();
        var a = scope1.ServiceProvider.GetRequiredService<ToolPluginRegistry>();
        var b = scope2.ServiceProvider.GetRequiredService<ToolPluginRegistry>();
        Assert.AreNotSame(a, b);
    }

    [TestMethod]
    public void RootResolve_Throws_ForScopedToolConfirmation()
    {
        using var sp = Build();
        Assert.ThrowsException<InvalidOperationException>(
            () => sp.GetRequiredService<ToolConfirmationContext>());
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ScopedPluginChainTests"`
Expected: `ToolConfirmationContext_DiffersPerScope` FAIL（当前为同一单例实例）。

- [ ] **Step 3: 改注册为 scoped**

在 `LuBan.AIAgent/LuBanAgentExtensions.cs` 把工具插件与相关注册改为 scoped：

```csharp
// 注册工具插件
services.AddScoped<ILuBanToolPlugin, Tools.Browser.BrowserToolPlugin>();
services.AddScoped<ILuBanToolPlugin, Tools.FileSystem.FileSystemToolPlugin>();
services.AddScoped<ILuBanToolPlugin, Tools.Script.ScriptToolPlugin>();
services.AddScoped<ILuBanToolPlugin, Tools.Web.WebToolPlugin>();
services.AddScoped<ILuBanToolPlugin, Tools.Git.GitToolPlugin>();
services.AddScoped<ILuBanToolPlugin, Tools.Retrieval.RetrievalToolPlugin>();
services.AddScoped<ILuBanToolPlugin, Tools.LocalMemory.LocalMemoryToolPlugin>();
services.AddScoped<ILuBanToolPlugin, MCP.MCPToolPlugin>();
services.AddScoped<ILuBanToolPlugin, Tools.Context.CompactContextToolPlugin>();
services.AddScoped<ILuBanToolPlugin, Tools.Wiki.WikiToolPlugin>();
services.AddScoped<ILuBanToolPlugin, Tools.Log.LogToolPlugin>();
// 条件注册（:142-145，Orchestration.Enabled=true 时）也必须同步改为 scoped，勿遗漏
// if (orchestrationEnabled) services.AddScoped<ILuBanToolPlugin, Tools.Orchestration.OrchestrationToolPlugin>();

LoadExternalPlugins(services, configuration);

services.AddScoped<ToolPluginRegistry>();
```
并把确认服务改为 scoped：
```csharp
services.AddScoped<ToolConfirmationContext>();
services.AddScoped<IToolConfirmationService, ToolConfirmationService>();
```
同时把 `LoadExternalPlugins`（`:191-193`）的 `services.AddSingleton(typeof(ILuBanToolPlugin), type)` 改为 `AddScoped`；把 `:144` 的 `services.AddSingleton<ILuBanToolPlugin, Tools.Orchestration.OrchestrationToolPlugin>();` 改为 `AddScoped`。

> 注意：`PathGuard`（Task A3）与 `IWorktreeCoordinator` 此时也应为 scoped；若本步后 `ValidateScopes` 因其它单例注入 scoped 抛错，先记录，由 A3–A6 逐步消除。

> **前置校验开关**：`ToolConfirmationContext`（`:103`）与 `ContextInjectBuilder`（`:125`）为单例，会把 scoped 依赖 captive。A2 起即在测试辅助 `Build()` 开启 `new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }`，让 captive 在最早阶段暴露；若暴露既有非本任务范围的 captive，记录并最小改动消除。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ScopedPluginChainTests"`
Expected: PASS。

- [ ] **Step 5: 回归插件相关既有测试**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ToolPlugin|FullyQualifiedName~Confirmation"`
Expected: PASS（或仅环境依赖型失败，需人工甄别）。

- [ ] **Step 6: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/LuBanAgentExtensions.cs LuBan.XTestProject/AIAgent/ScopedPluginChainTests.cs
git -C luban-framework commit -m "工具确认链与插件链改为 scoped 消除跨会话串扰"
```

---

### Task A3: `PathGuard` 改 scoped 并读 `ISessionContext.AllowedRoots`

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/Infrastructure/PathGuard.cs:29,37,47`
- Modify: `luban-framework/LuBan.AIAgent/LuBanAgentExtensions.cs:94`
- Test: `luban-framework/LuBan.XTestProject/AIAgent/PathGuardSessionScopeTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace LuBan.UnitTestProject.AIAgent;

internal sealed class FakeSessionContext : ISessionContext
{
    public string? SessionId { get; init; }
    public string EffectiveRoot { get; init; } = @"C:\ws";
    public LuBan.AIAgent.Abstractions.Models.WorktreeSession? ActiveWorktree => null;
    public IReadOnlyList<string> AllowedRoots { get; init; } = [];
    public string ResolveTempDirectory() => @"C:\ws\.tmp";
    public bool IsWithinEffectiveRoot(string path) => path.StartsWith(@"C:\ws", System.StringComparison.OrdinalIgnoreCase);
}

[TestClass]
public class PathGuardSessionScopeTests
{
    [TestMethod]
    public void IsAllowed_Uses_SessionAllowedRoots()
    {
        var ctx = new FakeSessionContext { AllowedRoots = [@"C:\ws\extra"] };
        var guard = new PathGuard(ctx, null);
        Assert.IsTrue(guard.IsAllowed(@"C:\ws\extra\a.txt"));
        Assert.IsFalse(guard.IsAllowed(@"C:\other\a.txt"));
    }

    [TestMethod]
    public void IsAllowed_Denies_WhenNoRoots()
    {
        var guard = new PathGuard(new FakeSessionContext(), null);
        Assert.IsFalse(guard.IsAllowed(@"C:\ws\a.txt"));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~PathGuardSessionScopeTests"`
Expected: FAIL（构造签名不匹配）。

- [ ] **Step 3: 修改 `PathGuard`**

`PathGuard.cs` 构造与 `IsAllowed`：
```csharp
private readonly ISessionContext _session;
private readonly LuBanAgentOptions? _options; // 兼容：无会话时回退全局

public PathGuard(ISessionContext session, IOptions<LuBanAgentOptions>? options = null)
{
    _session = session ?? throw new ArgumentNullException(nameof(session));
    _options = options?.Value;
}

public bool IsAllowed(string? path)
{
    if (string.IsNullOrWhiteSpace(path)) return false;
    var full = Path.GetFullPath(path);
    var roots = _session.AllowedRoots;
    if (roots is { Count: > 0 })
        return roots.Any(root => full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
    // 回退：无会话根时沿用旧配置（仅用于非会话场景）
    var fallback = _options?.Tools?.FileSystem?.AllowedRoots;
    if (fallback is { Count: > 0 })
        return fallback.Any(root => full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase));
    return false; // 默认拒绝
}
```
在 `LuBanAgentExtensions.cs:94` 改为 `services.AddScoped<PathGuard>();`。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~PathGuardSessionScopeTests"`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/Infrastructure/PathGuard.cs LuBan.AIAgent/LuBanAgentExtensions.cs LuBan.XTestProject/AIAgent/PathGuardSessionScopeTests.cs
git -C luban-framework commit -m "PathGuard 改 scoped 并按会话允许根校验"
```

---

### Task A4: `TempDirectory` 静态 Resolver→AsyncLocal provider

**Files:**
- Modify: `luban-framework/LuBan.Common/IO/TempDirectory.cs:24-70`
- Test: `luban-framework/LuBan.XTestProject/Common/TempDirectoryScopeTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System;
using System.IO;
using LuBan.Common.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LuBan.UnitTestProject.Common;

[TestClass]
public class TempDirectoryScopeTests
{
    [TestMethod]
    public void Push_Overrides_And_Restores()
    {
        var a = Path.Combine(Path.GetTempPath(), "luban-a");
        var b = Path.Combine(Path.GetTempPath(), "luban-b");
        using (TempDirectory.Push(new StaticTempDirectoryProvider(a)))
        {
            Assert.AreEqual(Path.GetFullPath(a), Path.GetFullPath(TempDirectory.GetTempDir()));
            using (TempDirectory.Push(new StaticTempDirectoryProvider(b)))
                Assert.AreEqual(Path.GetFullPath(b), Path.GetFullPath(TempDirectory.GetTempDir()));
            Assert.AreEqual(Path.GetFullPath(a), Path.GetFullPath(TempDirectory.GetTempDir()));
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~TempDirectoryScopeTests"`
Expected: FAIL（`Push`/`StaticTempDirectoryProvider` 不存在）。

- [ ] **Step 3: 改造 `TempDirectory`（`LuBan.Common/IO/TempDirectory.cs`）**

```csharp
public interface ITempDirectoryProvider
{
    string GetTempDir();
    string GetTempFilePath(string? prefix = null, string? extension = null);
}

public sealed class StaticTempDirectoryProvider(string? dir) : ITempDirectoryProvider
{
    public string GetTempDir()
    {
        if (string.IsNullOrEmpty(dir)) return Path.GetTempPath();
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir!;
        }
        catch { return Path.GetTempPath(); } // 自定义目录不可用时回退系统临时目录
    }

    public string GetTempFilePath(string? prefix = null, string? extension = null)
        => TempDirectory.BuildTempFilePath(GetTempDir(), prefix, extension);
}

internal sealed class DefaultTempDirectoryProvider : ITempDirectoryProvider
{
    public string GetTempDir() => Path.GetTempPath();
    public string GetTempFilePath(string? prefix = null, string? extension = null)
        => TempDirectory.BuildTempFilePath(GetTempDir(), prefix, extension);
}

public static class TempDirectory
{
    private static readonly AsyncLocal<ITempDirectoryProvider?> _current = new();
    private static readonly ITempDirectoryProvider _default = new DefaultTempDirectoryProvider();

    public static ITempDirectoryProvider Current => _current.Value ?? _default;

    public static IDisposable Push(ITempDirectoryProvider provider)
    {
        var prev = _current.Value;
        _current.Value = provider;
        return new Restore(() => _current.Value = prev);
    }

    public static string GetTempDir() => Current.GetTempDir();

    // 保留旧签名与默认值语义（prefix 空→temp，extension 空→.tmp，时间戳含毫秒）
    public static string GetTempFilePath(string? prefix = null, string? extension = null)
        => Current.GetTempFilePath(prefix, extension);

    /// <summary>仅清理自定义目录（非系统临时目录），保留原 IsFileInUse 并发保护。</summary>
    public static void Cleanup(TimeSpan maxAge)
    {
        var dir = Current.GetTempDir();
        if (string.Equals(
                Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            return; // 系统临时目录不清理
        if (!Directory.Exists(dir)) return;
        var cutoff = DateTime.Now - maxAge;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            try
            {
                if (File.GetLastWriteTime(file) >= cutoff) continue;
                if (IsFileInUse(file)) continue;
                File.Delete(file);
            }
            catch { }
        }
    }

    /// <summary>生成 {prefix}_{yyyyMMddHHmmssfff}_{8位guid}{ext} 路径（不创建文件）。</summary>
    internal static string BuildTempFilePath(string dir, string? prefix, string? extension)
    {
        var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "temp" : prefix;
        var ext = string.IsNullOrWhiteSpace(extension) ? ".tmp" : extension;
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 8);
        return Path.Combine(dir, $"{safePrefix}_{stamp}_{shortId}{ext}");
    }

    private static bool IsFileInUse(string filePath) { /* 原实现照搬 */ }

    private sealed class Restore(Action action) : IDisposable
    {
        private Action? _a = action;
        public void Dispose() { _a?.Invoke(); _a = null; }
    }
}
```
`DefaultTempDirectoryProvider`/`StaticTempDirectoryProvider` 复用原 `GetTempDir` 的“回退系统临时目录”逻辑；`Cleanup` 与 `IsFileInUse` 从原实现照搬（仅把目录来源由 `Resolver` 改为 `Current`）。**删除旧的 `public static Func<string?>? Resolver`**（不再提供进程级全局解析器）。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~TempDirectoryScopeTests"`
Expected: PASS。

- [ ] **Step 5: 修复 `WorkspaceManager.cs:174` 的 `TempDirectory.Resolver` 写入（编译断点，跨仓库）**

framework 删除 `Resolver` 后，agent `WorkspaceManager` 静态构造（`:172-183`）会编译失败（ProjectReference 直接编译框架源码）。删除该静态构造中对 `TempDirectory.Resolver` 的赋值；临时目录改由 agent 在 `SessionRuntime` 每轮 `TempDirectory.Push(...)`（见 Task B4）。本步含一笔 agent 提交：

```bash
git -C luban-agent add LubanAgentCore/Services/WorkspaceManager.cs
git -C luban-agent commit -m "refactor: 移除 TempDirectory 全局解析器写入（改按会话作用域 Push）"
```

- [ ] **Step 6: 提交**

```bash
git -C luban-framework add LuBan.Common/IO/TempDirectory.cs LuBan.XTestProject/Common/TempDirectoryScopeTests.cs
git -C luban-framework commit -m "TempDirectory 支持按作用域 Push 覆盖"
```

---

### Task A5: 框架内 `CurrentSession` 消费点改依赖会话上下文

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/Sessions/SessionChatHistoryProvider.cs:26-32,74,157`
- Modify: `luban-framework/LuBan.AIAgent/LuBanAgent.cs:176`（遗漏点：`_sessionManager?.CurrentSession`）
- Modify: `luban-framework/LuBan.AIAgent/Tools/Context/CompactContextToolPlugin.cs:127`（遗漏点：`_sessionManager.CurrentSession?.SessionId`）
- Test: `luban-framework/LuBan.XTestProject/AIAgent/SessionHistoryScopeTests.cs`

> 说明：A5 必须覆盖框架内**全部** `ISessionManager.CurrentSession` 消费点，避免 P0 去静态后仍有会话串写。`ISessionManager.CurrentSession`（`ISessionManager.cs:123`）可保留于接口，但不再被这三处读取。

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LuBan.UnitTestProject.AIAgent;

[TestClass]
public class SessionHistoryScopeTests
{
    [TestMethod]
    public void Ctor_Accepts_ISessionContext()
    {
        var ctor = typeof(SessionChatHistoryProvider).GetConstructors()[0];
        var hasCtx = System.Array.Exists(ctor.GetParameters(), p => p.ParameterType == typeof(ISessionContext));
        Assert.IsTrue(hasCtx, "构造函数应注入 ISessionContext");
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~SessionHistoryScopeTests"`
Expected: FAIL。

- [ ] **Step 3: 修改构造函数与读取点**

在 `SessionChatHistoryProvider` 构造注入 `ISessionContext sessionContext`，删除对 `_sessionManager.CurrentSession` 的依赖（保留 `ISessionManager` 仅用于其它需要的方法；若无其它用途则一并移除）。把 `:74`、`:157` 的 `_sessionManager.CurrentSession?.SessionId` 改为 `_sessionContext.SessionId`。

同样处理另两处：
- `LuBanAgent.cs:176`：`_sessionManager?.CurrentSession` 改为经注入/已有的 `ISessionContext`（`LuBanAgent` 为 scoped，可注入）。
- `CompactContextToolPlugin.cs:127`：`_sessionManager.CurrentSession?.SessionId` 改为 `ISessionContext.SessionId`（该插件 A2 已改 scoped，可注入 `ISessionContext`）。

把提交的 `git add` 一并包含这两个文件（Step 5 提交命令同步补全）。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~SessionHistoryScopeTests"`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/Sessions/SessionChatHistoryProvider.cs LuBan.AIAgent/LuBanAgent.cs LuBan.AIAgent/Tools/Context/CompactContextToolPlugin.cs LuBan.XTestProject/AIAgent/SessionHistoryScopeTests.cs
git -C luban-framework commit -m "框架内当前会话消费点改为依赖会话上下文"
```

---

### Task A6: 插件工作区根解析改 `ISessionContext.EffectiveRoot`

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/Tools/FileSystem/FileSystemToolPlugin.cs:37,60-62`
- Modify: `luban-framework/LuBan.AIAgent/Tools/Git/GitToolPlugin.cs:16,42-49`
- Test: `luban-framework/LuBan.XTestProject/AIAgent/WorkspaceRootProviderTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Tools.FileSystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace LuBan.UnitTestProject.AIAgent;

[TestClass]
public class WorkspaceRootProviderTests
{
    [TestMethod]
    public void FileSystemPlugin_Ctor_Takes_ISessionContext()
    {
        var ctor = typeof(FileSystemToolPlugin).GetConstructors()[0];
        Assert.IsTrue(ctor.GetParameters().Any(p => p.ParameterType.Name == "ISessionContext"));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WorkspaceRootProviderTests"`
Expected: FAIL。

- [ ] **Step 3: 改造插件构造**

`FileSystemToolPlugin`：注入 `ISessionContext sessionContext`，把 `Func<string?> workspaceRootProvider = () => options?.Value.WorkspaceRoot;` 改为 `() => sessionContext.EffectiveRoot`（`FileSystemToolPlugin.cs:62`）。
`GitToolPlugin`：同样注入 `ISessionContext`，把 `GitToolPlugin.cs:42` 的 `() => agentOptions?.Value.WorkspaceRoot` 改为 `() => sessionContext.EffectiveRoot`。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WorkspaceRootProviderTests"`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/Tools/FileSystem/FileSystemToolPlugin.cs LuBan.AIAgent/Tools/Git/GitToolPlugin.cs LuBan.XTestProject/AIAgent/WorkspaceRootProviderTests.cs
git -C luban-framework commit -m "工具插件工作区根改按会话有效根解析"
```

---

### Task A7: `GitToolGroup` 增 worktree 工具

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/Tools/Git/GitToolPlugin.cs:87-131`（`GitToolGroup`）、`:16-60`（插件注册工具）
- Test: `luban-framework/LuBan.XTestProject/AIAgent/GitWorktreeToolTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Abstractions.Models;
using LuBan.AIAgent.Tools.Git;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Threading.Tasks;

namespace LuBan.UnitTestProject.AIAgent;

internal sealed class FakeWorktreeCoordinator : IWorktreeCoordinator
{
    public WorktreeSession? Started;
    public bool MergeConflict;
    public Task<WorktreeSession> BeginAsync(string reason, CancellationToken ct = default)
    {
        Started = new WorktreeSession
        {
            SessionId = "s1", WorkspaceId = "w1",
            WorktreePath = @"C:\repo\.worktrees\x", Branch = "luban-worktree/x",
            BaseBranch = "main", BaseCommit = "abc", CreatedAt = default
        };
        return Task.FromResult(Started);
    }
    public Task<WorktreeMergeResult> EndAsync(CancellationToken ct = default)
        => Task.FromResult(MergeConflict
            ? new WorktreeMergeResult(false, true, "conflict", ["a.txt"])
            : new WorktreeMergeResult(true, false, "ok"));
    public Task AbandonAsync(CancellationToken ct = default) => Task.CompletedTask;
    public WorktreeSession? GetActive(string sessionId) => Started;
}

[TestClass]
public class GitWorktreeToolTests
{
    [TestMethod]
    public async Task EnterWorktree_Delegates_To_Coordinator()
    {
        var fake = new FakeWorktreeCoordinator();
        var group = new GitToolGroup(fake);
        var s = await group.EnterWorktreeAsync("build feature");
        Assert.IsNotNull(fake.Started);
        Assert.AreEqual("s1", s.SessionId);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~GitWorktreeToolTests"`
Expected: FAIL。

- [ ] **Step 3: 增构造重载与工具方法**

在 `GitToolGroup` 增第二构造（供测试/协调）：
```csharp
private readonly IWorktreeCoordinator? _worktrees;
public GitToolGroup(IWorktreeCoordinator worktrees) { _worktrees = worktrees; }
public async Task<WorktreeSession> EnterWorktreeAsync(string reason, CancellationToken ct = default)
    => await (_worktrees ?? throw new InvalidOperationException("未配置 worktree 协调器")).BeginAsync(reason, ct);
public async Task<WorktreeMergeResult> FinishWorktreeAsync(CancellationToken ct = default)
    => await (_worktrees ?? throw new InvalidOperationException("未配置 worktree 协调器")).EndAsync(ct);
public WorktreeSession? WorktreeStatus() => _worktrees?.GetActive(_workspaceRootProvider() ?? "");
```
并在 `GitToolPlugin` 中把 `EnterWorktreeAsync`/`FinishWorktreeAsync`/`WorktreeStatus` 注册为 AIFunction（沿用 `AIFunctionFactoryHelper.Create`，注册名 `EnterWorktree`/`FinishWorktree`/`WorktreeStatus`）。`GitToolPlugin` 需注入 `IWorktreeCoordinator`。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~GitWorktreeToolTests"`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/Tools/Git/GitToolPlugin.cs LuBan.XTestProject/AIAgent/GitWorktreeToolTests.cs
git -C luban-framework commit -m "Git 工具组新增 worktree 进入/完成/状态工具"
```

---

### Task A8: 递增框架版本号（不发布）

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj:12`
- Modify: `luban-framework/LuBan.Common/LuBan.Common.csproj`（`<Version>`）

- [ ] **Step 1: 递增版本号**

将 `<Version>` 递增一位（日期制 `2026.M.d.r`，例如 `2026.10.10.1`）。以 csproj 现状为准手动选择。`LuBan.Common` 因 A4 改动同步递增。

- [ ] **Step 2: 构建确认可编译**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: BUILD 成功（经项目引用直接编译框架源码，无需发包）。

- [ ] **Step 3: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/LuBan.AIAgent.csproj LuBan.Common/LuBan.Common.csproj
git -C luban-framework commit -m "递增 LuBan.AIAgent/LuBan.Common 版本"
```

> 不发布。最终发包与还原 PackageReference 见 P2 计划 Phase N。

---

## Phase B — agent：会话作用域底座

> 前置：agent 侧已对框架走 ProjectReference，Phase A 的框架改动直接生效，无需发包或改包引用版本。

### Task B1: 实现上下文契约并注册

**Files:**
- Create: `luban-agent/LubanAgentCore/Context/UserContext.cs`
- Create: `luban-agent/LubanAgentCore/Context/WorkspaceContext.cs`
- Create: `luban-agent/LubanAgentCore/Context/SessionContextState.cs`
- Create: `luban-agent/LubanAgentCore/Context/SessionContext.cs`
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`（注册）
- Test: 无（agent 无测试）→ 以构建 + 后续冒烟为准

- [ ] **Step 1: 新增 `SessionContextState`（scoped，可变）**

```csharp
using LuBan.AIAgent.Abstractions.Models;

namespace LubanAgentCore.Context;

/// <summary>会话作用域内可写的上下文状态，由 SessionRuntimeManager 在创建 scope 后填充。</summary>
public sealed class SessionContextState
{
    public string? SessionId { get; set; }
    public string? WorkspaceId { get; set; }
    public string? WorkspaceRoot { get; set; }
    public WorktreeSession? ActiveWorktree { get; set; }
    public List<string> AllowedRoots { get; } = new();
    public string? TempDirectory { get; set; }

    public string EffectiveRoot
        => ActiveWorktree?.WorktreePath
           ?? WorkspaceRoot
           ?? throw new InvalidOperationException("会话未设置工作区根或 worktree");
}
```

- [ ] **Step 2: 新增 `SessionContext`（scoped，读状态）**

```csharp
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Abstractions.Models;

namespace LubanAgentCore.Context;

public sealed class SessionContext(SessionContextState state) : ISessionContext
{
    public string? SessionId => state.SessionId;
    public string EffectiveRoot => state.EffectiveRoot;
    public WorktreeSession? ActiveWorktree => state.ActiveWorktree;
    public IReadOnlyList<string> AllowedRoots => state.AllowedRoots;

    public string ResolveTempDirectory()
        => state.TempDirectory
           ?? Path.Combine(state.WorkspaceRoot ?? Path.GetTempPath(), ".luban-agent", "temp");

    public bool IsWithinEffectiveRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(EffectiveRoot);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               || state.AllowedRoots.Any(r => full.StartsWith(Path.GetFullPath(r), StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 3: 新增 `WorkspaceContext`（scoped）/`UserContext`（root）**

```csharp
// WorkspaceContext.cs
using LuBan.AIAgent.Abstractions;
namespace LubanAgentCore.Context;

public sealed class WorkspaceContext(SessionContextState state) : IWorkspaceContext
{
    public string? WorkspaceId => state.WorkspaceId;
    public string? RootPath => state.WorkspaceRoot;
    public string? RootPathFor(string? workspaceId) => state.WorkspaceRoot;
}
```
```csharp
// UserContext.cs
using LuBan.AIAgent.Abstractions;
namespace LubanAgentCore.Context;

public sealed class UserContext(string? userId) : IUserContext
{
    public string? UserId { get; } = userId;
}
```

- [ ] **Step 4: 在 `AgentHostBuilder.cs` 注册**

在 `AddLuBanAgent(configuration)`（`:101`）之后加入：
```csharp
services.AddSingleton<IUserContext>(new UserContext(Environment.UserName));
services.AddScoped<SessionContextState>();
services.AddScoped<ISessionContext, SessionContext>();
services.AddScoped<IWorkspaceContext, WorkspaceContext>();
```
（`IWorktreeCoordinator` 在 Task C1 注册。）

- [ ] **Step 5: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。

- [ ] **Step 6: 提交**

```bash
git -C luban-agent add LubanAgentCore/Context LubanAgentCore/Hosting/AgentHostBuilder.cs
git -C luban-agent commit -m "feat: 实现用户/工作区/会话上下文契约并注册"
```

---

### Task B2: `SessionManager` 去 `_currentSession` 静态

**Files:**
- Modify: `luban-agent/LubanAgentCore/Services/SessionManager.cs:32,50`

- [ ] **Step 1: 现状确认**

Run: `git -C luban-agent grep -n "_currentSession\|CurrentSession" -- LubanAgentCore/Services/SessionManager.cs`
Expected: 命中 `:32` 字段与 `:50` 读写。

- [ ] **Step 2: 改造**

`SessionManager` 移除实例级 `_currentSession` 语义，改为无状态服务（或仅保留按 sessionId 查询的方法）。`CurrentSession` 属性与 `SetCurrentSessionAsync` 的“当前会话”语义迁移到 `SessionContextState.SessionId`（由 `SessionRuntime` 持有）。所有 `SessionManager` 消费者改为显式传 `sessionId`。

- [ ] **Step 3: 修复编译断点**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: 报出所有 `CurrentSession` 使用点；逐一改为注入 `ISessionContext`/显式 sessionId。

- [ ] **Step 4: 构建通过**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。

- [ ] **Step 5: 提交**

```bash
git -C luban-agent add LubanAgentCore/Services/SessionManager.cs
git -C luban-agent commit -m "refactor: SessionManager 去除当前会话静态语义"
```

---

### Task B3: `WorkspaceManager` 去静态、去 CWD、去 options 改写

**Files:**
- Modify: `luban-agent/LubanAgentCore/Services/WorkspaceManager.cs:146,165,174,203,217,360,396,404,622-647`

- [ ] **Step 1: 删除静态 `Current` 与 `_knownRoots`**

移除 `static _current`/`static Current`（`:165,203`）与 `_knownRoots` 静态缓存（`:189,196,667`）。工作区列表数据仍可由 `WorkspaceManager`（单例）持有；“当前工作区”指针改由各会话 `SessionContextState` 承载。

- [ ] **Step 2: 删除 `Directory.SetCurrentDirectory`**

删除 `:360`、`:396` 的调用；受影响的路径解析改为绝对路径（基于调用方传入的会话 `EffectiveRoot`）。

- [ ] **Step 3: 删除 `IOptions` 改写**

删除 `:404`（写 `WorkspaceRoot`）、`:622-647`（写 `AllowedRoots`）。二者改由 `SessionRuntime` 在进入会话/轮次时写入 `SessionContextState`；`:174` 的 `TempDirectory.Resolver` 写入已在 **A4 Step 5** 删除（此处勿重复）。

- [ ] **Step 4: `IsWithinWorkspace`(static) 改实例/上下文**

`:217` 的静态方法改为 `ISessionContext.IsWithinEffectiveRoot`（调用方注入会话上下文）。

- [ ] **Step 5: 修复消费者编译断点**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: 报出所有 `WorkspaceManager.Current` 使用点（如 `WorkspaceWikiContext.cs`、`FooterDataProvider.cs` 等），改为注入 `ISessionContext`/`IWorkspaceContext`。

- [ ] **Step 6: 构建通过**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。

- [ ] **Step 7: 提交**

```bash
git -C luban-agent add LubanAgentCore
git -C luban-agent commit -m "refactor: WorkspaceManager 去除静态/当前目录/全局配置改写"
```

---

### Task B4: `SessionRuntime` + `SessionRuntimeManager`

**Files:**
- Create: `luban-agent/LubanAgentCore/Services/SessionRuntime.cs`
- Create: `luban-agent/LubanAgentCore/Services/SessionRuntimeManager.cs`
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`（注册单例管理器）

- [ ] **Step 1: 新增 `SessionRuntime`**

职责：拥有一个会话 `IServiceScope`、一个 `IServiceScopeFactory` 派生的 scope、`LuBanAgent` 实例、回合 `Channel`/队列 + 运行循环、事件流、`SessionContextState` 引用。每轮在 `TempDirectory.Push(...)` 作用域内运行。

```csharp
using LubanAgentCore.Context;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCore.Services;

public sealed class SessionRuntime : IAsyncDisposable
{
    private readonly IServiceScope _scope;
    private readonly Channel<RuntimeTurn> _queue = Channel.CreateUnbounded<RuntimeTurn>();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public string SessionId { get; }
    public ISessionContext SessionContext { get; }
    public bool IsRunning { get; private set; }

    public SessionRuntime(IServiceScope scope, string sessionId, ISessionContext ctx)
    {
        _scope = scope; SessionId = sessionId; SessionContext = ctx;
    }

    public void Start()
        => _loop ??= Task.Run(ProcessLoopAsync);

    public ValueTask EnqueueAsync(RuntimeTurn turn) => _queue.Writer.WriteAsync(turn);

    private async Task ProcessLoopAsync()
    {
        await foreach (var turn in _queue.Reader.ReadAllAsync(_cts.Token))
        {
            IsRunning = true;
            try
            {
                using var tempScope = TempDirectory.Push(new StaticTempDirectoryProvider(SessionContext.ResolveTempDirectory()));
                await turn.RunAsync(SessionContext, turn.CancellationToken);
            }
            finally { IsRunning = false; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _cts.Cancel();
        if (_loop is not null) { try { await _loop; } catch { } }
        _scope.Dispose();
    }
}
```
`RuntimeTurn` 定义（委托须为 `Func<...Task>` 才能 `await`，且需携带本轮 `CancellationToken`）：

```csharp
public sealed class RuntimeTurn
{
    public required string Text { get; init; }
    public required Func<ISessionContext, CancellationToken, Task> RunAsync { get; init; }
    public CancellationToken CancellationToken { get; init; }
}
```
实际运行委托由 `AgentHostService` 提供（调用 `_agent.RunStreamingAsync` 并把事件推入该会话事件流）；`ProcessLoopAsync` 中 `await turn.RunAsync(SessionContext, turn.CancellationToken)` 与上述签名一致。

- [ ] **Step 2: 新增 `SessionRuntimeManager`**

```csharp
using System.Collections.Concurrent;
using LubanAgentCore.Context;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCore.Services;

public sealed class SessionRuntimeManager : IAsyncDisposable
{
    private readonly IServiceProvider _root;
    private readonly ConcurrentDictionary<string, SessionRuntime> _runtimes = new();
    private readonly TimeSpan _idle = TimeSpan.FromMinutes(30);

    public SessionRuntimeManager(IServiceProvider root) => _root = root;

    public SessionRuntime GetOrCreate(string sessionId, string workspaceId, string workspaceRoot)
    {
        return _runtimes.GetOrAdd(sessionId, id =>
        {
            var scope = _root.CreateScope();
            var state = scope.ServiceProvider.GetRequiredService<SessionContextState>();
            state.SessionId = id; state.WorkspaceId = workspaceId; state.WorkspaceRoot = workspaceRoot;
            state.AllowedRoots.Add(workspaceRoot);
            var ctx = scope.ServiceProvider.GetRequiredService<ISessionContext>();
            var rt = new SessionRuntime(scope, id, ctx);
            rt.Start();
            return rt;
        });
    }

    public SessionRuntime? Get(string sessionId) => _runtimes.GetValueOrDefault(sessionId);

    public async ValueTask DisposeAsync()
    {
        foreach (var rt in _runtimes.Values) await rt.DisposeAsync();
        _runtimes.Clear();
    }
}
```
（空闲卸载：在 `GetOrCreate` 记录 `LastActive`，由后台计时器超过 `_idle` 调 `DisposeAsync` 并移除；实现细节照 `AGENTS.md` 最小可用。）

- [ ] **Step 3: 注册**

在 `AgentHostBuilder.cs` 注册 `services.AddSingleton<SessionRuntimeManager>();`，并让 `BuildServiceProvider` 使用 `new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }`（`:140`）。

- [ ] **Step 4: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。若 `ValidateOnBuild` 报 captive dependency，回溯到对应注册修正。

- [ ] **Step 5: 提交**

```bash
git -C luban-agent add LubanAgentCore/Services/SessionRuntime.cs LubanAgentCore/Services/SessionRuntimeManager.cs LubanAgentCore/Hosting/AgentHostBuilder.cs
git -C luban-agent commit -m "feat: 新增会话运行时与运行时管理器（每会话 scope）"
```

---

### Task B5: `AgentHostService` 改为无状态路由器并纳入 DI

**Files:**
- Modify: `luban-agent/LubanAgentCore/Services/AgentHostService.cs:23-55,241`
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`（注册）
- Modify: `luban-agent/LubanAgentCodex/ViewModels/MainWindowViewModel.cs:286`（改从 DI 取用）

**生命周期决策（消除原自相矛盾）：** `AgentHostService` 保持 **Singleton**，但**不再持有任何 per-session 状态**——它是无状态路由器，内部持有 `SessionRuntimeManager`（singleton），把 `RunStreamingAsync(sessionId, text, ...)` 路由到对应 `SessionRuntime` 排队执行；每会话运行态全部在 `SessionRuntime` 的会话 scope 内。

- [ ] **Step 1: 改造为无状态路由器**

`AgentHostService` 删除单一 `_agent` 字段与 `Reset()` 重建语义；`RunStreamingAsync` 接收 `sessionId`，`SessionRuntime` 经 `SessionRuntimeManager.Get(sessionId)` 取用，经 `SessionRuntime.EnqueueAsync` 串行执行；每轮 `ConfigureForTurn` 作用于该会话 scope 解析出的 scoped `ToolConfirmationContext`（`AgentHostService.cs:241`）。

- [ ] **Step 2: 注册到 DI 并修取用路径**

`AgentHostBuilder.cs`：`services.AddSingleton<AgentHostService>();`。
`MainWindowViewModel.cs:286` 的 `new AgentHostService(services)` 改为构造注入或 `services.GetRequiredService<AgentHostService>()`；`SettingsWindow.axaml.cs:1673` 的 `GetRequiredService<AgentHostService>()` 随之可用。

- [ ] **Step 3: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentCore/Services/AgentHostService.cs LubanAgentCore/Hosting/AgentHostBuilder.cs
git -C luban-agent commit -m "refactor: AgentHostService 改为按会话并纳入 DI"
```

---

### Task B6: `IOrchestrationProgressSink` per-session

**Files:**
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`
- Modify: `luban-agent/LubanAgentCodex/App.axaml.cs:111-113`

- [ ] **Step 1: 改注册为 scoped**

把进度 sink 覆盖改为 `services.Replace(ServiceDescriptor.Scoped<IOrchestrationProgressSink>(sp => sp.GetRequiredService<HostOrchestrationProgressSink>()))`，并 `AddScoped<HostOrchestrationProgressSink>()`；确保 sink 事件按会话隔离（每个会话 scope 一份 sink，或 sink 内部按 sessionId 分派）。

- [ ] **Step 2: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。

- [ ] **Step 3: 提交**

```bash
git -C luban-agent add LubanAgentCore/Hosting/AgentHostBuilder.cs LubanAgentCodex/App.axaml.cs
git -C luban-agent commit -m "refactor: 编排进度出口按会话隔离"
```

---

### Task B7: A2A 会话作用域与工作区根

**Files:**
- Modify: `luban-agent/LubanAgentA2A/Executor/InProcessSubAgentExecutor.cs:44,58`
- Modify: `luban-agent/LubanAgentA2A/Runtime/A2ARuntimeLifecycle.cs:100-135`（`IsStarted` 幂等）
- Modify: `luban-agent/LubanAgentA2A/Runtime/A2ARuntimeState.cs:30-44`

- [ ] **Step 1: 去静态确认作用域**

`InProcessSubAgentExecutor.cs:58` 的静态 `ToolConfirmationContext.EnterSubAgentScope(allowAll: true)` 改为按会话/分派上下文设置（经 scoped 确认上下文），避免并发会话互相 allow-all。

- [ ] **Step 2: 工作区根按会话传入**

`A2ARuntimeLifecycle.StartAsync(workspaceRoot)` 的幂等短路（`:105-108`）改为按 `workspaceRoot`/会话区分（`A2ARuntimeState` 的本地 agent 名与角色注册按会话或工作区隔离），避免换工作区后沿用旧根。

- [ ] **Step 3: 构建**

Run: `dotnet build luban-agent/LubanAgentA2A/LubanAgentA2A.csproj`
Expected: BUILD 成功。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentA2A
git -C luban-agent commit -m "refactor: A2A 子代理确认作用域与工作区根按会话隔离"
```

---

### Task B8: SQLite WAL + busy_timeout

**Files:**
- Modify: `luban-agent/LubanAgentCli/appsettings.json:219`
- Modify: `luban-agent/LubanAgentCodex/appsettings.json:215`

- [ ] **Step 1: 修改连接串**

把 `"Data Source=./luban-ai-agent.db"` 改为 `"Data Source=./luban-ai-agent.db;Cache=Shared"`，并在 `LuBanOrm.Init()` 后执行 `PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;`（在 `LuBanOrm` 初始化处补一段 PRAGMA 执行；若非要害可在 DB 初始化器内）。

- [ ] **Step 2: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。

- [ ] **Step 3: 提交**

```bash
git -C luban-agent add LubanAgentCli/appsettings.json LubanAgentCodex/appsettings.json LubanAgentCore
git -C luban-agent commit -m "chore: SQLite 启用 WAL 与 busy_timeout"
```

---

## Phase C — worktree 协调

### Task C1: `WorktreeSessionManager`（实现 `IWorktreeCoordinator`）

**Files:**
- Create: `luban-agent/LubanAgentCore/Services/WorktreeSessionManager.cs`
- Create: `luban-agent/LubanAgentCore/Services/GitProcess.cs`（薄封装 `git` 调用，或复用框架 `ProcessRunner`）
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`（注册 `IWorktreeCoordinator` scoped）
- Modify: `luban-agent/LubanAgentCore/Context/SessionContextState.cs`（`ActiveWorktree`/`AllowedRoots` 已具备）

- [ ] **Step 1: 实现 `BeginAsync`**

```csharp
public async Task<WorktreeSession> BeginAsync(string reason, CancellationToken ct = default)
{
    var root = _sessionContext.EffectiveRoot; // 进入前=工作区根
    if (!await IsGitRepoAsync(root, ct)) throw new InvalidOperationException("当前目录不是 git 仓库");
    var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    var path = Path.Combine(root, ".worktrees", ts);
    var branch = $"luban-worktree/{ts}";
    var baseBranch = await RunAsync(root, "symbolic-ref", "--short HEAD", ct);
    var baseCommit = await RunAsync(root, "rev-parse", "HEAD", ct);
    await RunAsync(root, "worktree", "add", "-b", branch, path, "HEAD", ct);
    var session = new WorktreeSession { SessionId = _state.SessionId!, WorkspaceId = _state.WorkspaceId!, WorktreePath = path, Branch = branch, BaseBranch = baseBranch.Trim(), BaseCommit = baseCommit.Trim(), CreatedAt = DateTimeOffset.Now };
    _state.ActiveWorktree = session;
    if (!_state.AllowedRoots.Contains(path)) _state.AllowedRoots.Add(path);
    await _repo.InsertAsync(Entity.From(session));
    return session;
}
```
（进入前若主树脏，返回前提示需显式确认：由 UI/工具层在调用前检查 `git status --porcelain`。）

- [ ] **Step 2: 实现 `EndAsync`**

按 spec §5.4：主树 `status --porcelain` 非空→返回 `Conflict=false` 的“主树脏”停止结果；worktree 内 `add -A`+`commit`（无改动跳过）；主树 `merge <branch>`；冲突→`new WorktreeMergeResult(false,true,msg,conflictFiles)` 且不清理；成功→`worktree remove`+`branch -D`+删 DB+`TempDirectory.Cleanup`+还原 `ActiveWorktree`/`AllowedRoots`。

- [ ] **Step 3: `AbandonAsync`/`GetActive`**

`AbandonAsync`：`worktree remove --force` + `branch -D` + 删 DB + 还原。`GetActive(sessionId)` 从内存 registry 查。

- [ ] **Step 4: 注册并构建**

`services.AddScoped<IWorktreeCoordinator, WorktreeSessionManager>();`
Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。

- [ ] **Step 5: 提交**

```bash
git -C luban-agent add LubanAgentCore/Services/WorktreeSessionManager.cs LubanAgentCore/Hosting/AgentHostBuilder.cs
git -C luban-agent commit -m "feat: 实现 worktree 会话协调器"
```

---

### Task C2: `ai_worktree_session` 表实体与仓储

**Files:**
- Create: `luban-agent/LubanAgentCore/Models/WorktreeSessionEntity.cs`
- Create: `luban-agent/LubanAgentCore/Repositories/WorktreeSessionRepository.cs`
- 参照现有实体与 `EnableInitTable` 建表机制（`LuBanOrm`）

- [ ] **Step 1: 定义实体**

```csharp
[SugarTable("ai_worktree_session")]
public class WorktreeSessionEntity
{
    [SugarColumn(IsPrimaryKey = true)] public string SessionId { get; set; } = "";
    public string WorkspaceId { get; set; } = "";
    public string WorktreePath { get; set; } = "";
    public string Branch { get; set; } = "";
    public string BaseBranch { get; set; } = "";
    public string BaseCommit { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 2: 仓储 CRUD**（`InsertAsync`/`GetBySessionAsync`/`GetAllAsync`/`DeleteAsync`），沿用现有仓储模式。

- [ ] **Step 3: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentCore/Models/WorktreeSessionEntity.cs LubanAgentCore/Repositories/WorktreeSessionRepository.cs
git -C luban-agent commit -m "feat: 新增 worktree 会话持久化表与仓储"
```

---

### Task C3: 启动对账

**Files:**
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs` 或 Codex `App.axaml.cs` 启动流程
- Create: `luban-agent/LubanAgentCore/Services/WorktreeReconciler.cs`

- [ ] **Step 1: 实现对账**

启动时比对 `.worktrees/` 目录、`git worktree list`、`ai_worktree_session`：孤儿 worktree（无 DB 记录）→ 提示 prune；DB 有记录但目录缺失 → 提示恢复/放弃。

- [ ] **Step 2: 接入启动**

在 Codex 启动（`App.axaml.cs:146` 之前）与 CLI 启动调用 `WorktreeReconciler.RunAsync()`，结果以通知/日志呈现。

- [ ] **Step 3: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentCore/Services/WorktreeReconciler.cs LubanAgentCodex/App.axaml.cs
git -C luban-agent commit -m "feat: worktree 启动对账"
```

---

## Phase D — Codex 多会话 UI

### Task D1: 抽取 `SessionViewModel`（每会话一份运行态）

**Files:**
- Create: `luban-agent/LubanAgentCodex/ViewModels/SessionViewModel.cs`
- Modify: `luban-agent/LubanAgentCodex/ViewModels/MainWindowViewModel.cs`（把 `Messages`/`_cts`/`_activeRunSeq`/`_runningNodes` 等下沉）

- [ ] **Step 1: 下沉每会话状态**

把 `MainWindowViewModel` 中的 `Messages`（`:278`）、`_cts`、`_activeRunSeq`、`_runningNodes`（`:62`）、`_currentAssistant/_currentThinking/_throttle/_pendingText/_pendingThinking` 迁入 `SessionViewModel`，键为 `sessionId`。`MainWindowViewModel` 持有 `Dictionary<string, SessionViewModel>` 与当前选中项。

- [ ] **Step 2: 改 `SendAsync`/`LoadSessionHistoryAsync` 语义**

`SendAsync:305` 的 `if (IsRunning) return;` 改为针对**该会话**判断；`LoadSessionHistoryAsync:1591` 的运行中拒绝改为仅切换展示，不影响后台会话运行。

- [ ] **Step 3: 构建**

Run: `dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
Expected: BUILD 成功。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentCodex/ViewModels/SessionViewModel.cs LubanAgentCodex/ViewModels/MainWindowViewModel.cs
git -C luban-agent commit -m "feat: Codex 会话运行态按会话分片"
```

---

### Task D2: 侧栏运行徽标

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/Controls/Sidebar.axaml.cs:361-460`
- Modify: `luban-agent/LubanAgentCodex/ViewModels/MainWindowViewModel.cs`（暴露每会话状态）

- [ ] **Step 1: 扩展 `SessionItem`**

`SessionItem` 增 `bool IsRunning`；会话行（`:382-389`）增运行徽标控件（如小圆点），`LoadWorkspaces()` 重建列表（`:459`）时按当前运行集合恢复徽标。

- [ ] **Step 2: 订阅状态**

`MainWindowViewModel` 暴露 `SessionRunningChanged` 事件；侧栏订阅后更新对应 `SessionItem.IsRunning`。

- [ ] **Step 3: 构建**

Run: `dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
Expected: BUILD 成功。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentCodex/Views/Controls/Sidebar.axaml.cs LubanAgentCodex/ViewModels/MainWindowViewModel.cs
git -C luban-agent commit -m "feat: Codex 侧栏会话运行徽标"
```

---

### Task D3: 切换与后台确认归属

**Files:**
- Modify: `luban-agent/LubanAgentCodex/ViewModels/MainWindowViewModel.cs:1642-1677`（`ConfirmCallback`）、`:538-550`

- [ ] **Step 1: 确认弹窗按会话归属**

`ConfirmCallback` 的模态确认绑定到该会话视图（非当前选中会话时在侧栏显示待确认徽标），避免两会话确认互相阻塞。`_cts` 改为每会话一份（Task D1 已下沉）。

- [ ] **Step 2: 构建**

Run: `dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
Expected: BUILD 成功。

- [ ] **Step 3: 提交**

```bash
git -C luban-agent add LubanAgentCodex/ViewModels/MainWindowViewModel.cs
git -C luban-agent commit -m "feat: Codex 工具确认按会话归属"
```

---

## Phase E — 验证

### Task E1: 自动化测试与构建

- [ ] **Step 1: 框架单测**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WorktreeSession|FullyQualifiedName~ScopedPluginChain|FullyQualifiedName~PathGuardSessionScope|FullyQualifiedName~TempDirectoryScope|FullyQualifiedName~SessionHistoryScope|FullyQualifiedName~WorkspaceRootProvider|FullyQualifiedName~GitWorktreeTool"`
Expected: 全 PASS。

- [ ] **Step 2: 两端构建**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: BUILD 成功（全量 framework sln 受 `WebApplication1` 私源阻断，不要求）。

- [ ] **Step 3: 提交（如有测试补充）**

```bash
git -C luban-agent add -A && git -C luban-agent commit -m "test: P0 验证补充"
```

### Task E2: 手动冒烟（按 `LubanAgentCli/docs/TUI-SMOKE-TEST.md` 范式 + Codex）

- [ ] **Step 1: 同工作区两会话并发**：会话 2 进 worktree 实现功能乙、会话 1 主树实现功能甲，互不干扰。
- [ ] **Step 2: 后台继续**：切走会话 2，其后台继续推进（侧栏徽标为运行中）。
- [ ] **Step 3: 完成合并**：会话 2 说「完成」→ 合并回主分支，worktree/分支清理；构造 merge 冲突验证「停止交人工」。
- [ ] **Step 4: 主树脏**：Finish 前主树有未提交改动 → 正确阻断并提示。
- [ ] **Step 5: 崩溃对账**：带活动 worktree 强杀进程，重启 → 提示恢复/清理。

记录结果；失败项回到对应 Task 修复。

---

## Self-Review

- **Spec 覆盖**：§3.2 契约→A1；§3.3 规则→A2/A3/A4/A6/B2/B3；§3.4 映射→A2–A6/B2/B3/B6；§4 运行时→B4/B5；§5 worktree→A7/C1/C2/C3；§6 宿主→D1–D3（CLI 归 P2）；§7 阻断项→A2/A3/A4/A5/B2/B3/B6/B7；§8 边界→C1/C3/D3；§9 P0→全 Phase；§10/§11→E1/E2。
- **占位符**：无 TBD；机械改动以「精确行号 + 目标」表述，新类型给出完整代码。
- **类型一致性**：`WorktreeSession`/`IWorktreeCoordinator`/`ISessionContext`/`SessionContextState`/`SessionRuntime`/`SessionRuntimeManager` 命名前后一致；`TempDirectory.Push` 与 `StaticTempDirectoryProvider` 在 A4 定义、B4 使用一致。

## 已知风险

- **A2 的 scoped 化可能暴露更多 captive**：`ValidateScopes`/`ValidateOnBuild`（B4）会列出所有单例注入 scoped 的点，需逐个 Task 消除；若发现 `LuBanAgentFactory`（scoped）被单例消费，需一并调整。
- **项目引用链路**：B 阶段依赖 A 阶段源码，经 agent 的 ProjectReference 直接编译；若项目图出现重复/版本冲突，需在 `LubanAgentCore.csproj` 收敛引用。
- **Codex UI 重构面大**：D1 涉及迁移多处共享字段，需谨慎回归。
- **Agent 无自动化测试**：并发正确性主要靠 E2 冒烟，必要时为 `SessionRuntimeManager` 增加最小 harness。