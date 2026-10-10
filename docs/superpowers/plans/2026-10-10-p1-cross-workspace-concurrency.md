# P1 跨工作区并发 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让同一用户的多个工作区会话在单进程内**同时**安全运行（P0 已实现同工作区并发；P0 期间跨工作区同时运行会因工作区级注册表被覆盖而退化为串行）。

**Architecture:** 在 P0 的 L3 会话作用域之上引入真正的 L2 工作区作用域：`SessionRuntimeManager` 为每个 `workspaceId` 缓存一个 `IServiceScope`，会话 scope 作为其子 scope 创建；把工作区级注册表/服务从单例改为 workspace-scoped。移除 agent 侧为规避全局覆盖而保留的“每轮重载”临时策略。

**Tech Stack:** .NET 10、Microsoft.Extensions.DependencyInjection、Microsoft Agent Framework、SqlSugar(SQLite)、MSTest。

**Repo 根:**
- framework: `D:\WorkBench\Walle\luban\luban-framework`
- agent: `D:\WorkBench\Walle\luban\luban-agent`

**前置:** P0 完成。开发期 agent 对框架走 `ProjectReference`（提交 `de822af`），本计划**不发布中间包**；Task F2 仅递增版本号占位。最终发包与还原 PackageReference 见 P2 计划 Phase N。

**依赖顺序:** F（框架，发新包）→ G（agent 工作区作用域）→ H（验证）。

---

## File Structure

### framework `luban-framework`
- Modify `LuBan.AIAgent/LuBanAgentExtensions.cs:66-86` — `IRule`/`RuleEngine`/`SkillRegistry`/`MCPRegistry` singleton→scoped
- Modify `LuBan.AIAgent/LuBanAgentExtensions.cs:125` — `ContextInjectBuilder` 视依赖决定作用域
- Test `LuBan.XTestProject/AIAgent/WorkspaceScopeRegistryTests.cs`

### agent `luban-agent`
- Modify `LubanAgentCore/Services/SessionRuntimeManager.cs` — 嵌套 workspace→session scope
- Modify `LubanAgentCore/Hosting/AgentHostBuilder.cs` — 工作区级服务注册为 scoped
- Modify `LubanAgentCore/Agents/WorkspaceWikiContext.cs:22` — 经 `IWorkspaceContext`（P0 已改注入，此处确保按工作区隔离）
- Modify `LubanAgentCore/Services/WorkspaceManager.cs` — workspace scope 管理、移除每轮重载
- Test/冒烟

---

## Phase F — framework：工作区级注册表 scoped

### Task F1: 注册表 singleton→scoped

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/LuBanAgentExtensions.cs:66-86,125`
- Test: `luban-framework/LuBan.XTestProject/AIAgent/WorkspaceScopeRegistryTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Rules;
using LuBan.AIAgent.Skills;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LuBan.UnitTestProject.AIAgent;

[TestClass]
public class WorkspaceScopeRegistryTests
{
    private static ServiceProvider Build()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLuBanAgent(config);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [TestMethod]
    public void RuleEngine_DiffersPerScope()
    {
        using var sp = Build();
        using var a = sp.CreateScope();
        using var b = sp.CreateScope();
        Assert.AreNotSame(a.ServiceProvider.GetRequiredService<RuleEngine>(),
                          b.ServiceProvider.GetRequiredService<RuleEngine>());
    }

    [TestMethod]
    public void SkillRegistry_DiffersPerScope()
    {
        using var sp = Build();
        using var a = sp.CreateScope();
        using var b = sp.CreateScope();
        Assert.AreNotSame(a.ServiceProvider.GetRequiredService<SkillRegistry>(),
                          b.ServiceProvider.GetRequiredService<SkillRegistry>());
    }

    [TestMethod]
    public void McpRegistry_DiffersPerScope()
    {
        using var sp = Build();
        using var a = sp.CreateScope();
        using var b = sp.CreateScope();
        Assert.AreNotSame(a.ServiceProvider.GetRequiredService<MCP.MCPRegistry>(),
                          b.ServiceProvider.GetRequiredService<MCP.MCPRegistry>());
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WorkspaceScopeRegistryTests"`
Expected: FAIL（当前为同一单例）。

- [ ] **Step 3: 改注册**

把 `LuBanAgentExtensions.cs:66-73`、`:76-86` 的 `IRule`/`RuleEngine`/`IMCPClient`/`MCPRegistry`/`ISkill`/`SkillRegistry` 改为 `AddScoped`。评估 `ContextInjectBuilder`（`:125`）依赖是否需随工作区变化，如是则改 scoped，否则保持单例并注释原因。

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WorkspaceScopeRegistryTests"`
Expected: PASS。

- [ ] **Step 5: 回归既有测试**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~Rule|FullyQualifiedName~Skill|FullyQualifiedName~MCP"`
Expected: PASS（环境依赖型失败需甄别）。

- [ ] **Step 6: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/LuBanAgentExtensions.cs LuBan.XTestProject/AIAgent/WorkspaceScopeRegistryTests.cs
git -C luban-framework commit -m "工作区级注册表改 scoped 支持跨工作区并发"
```

---

### Task F2: 递增框架版本号（不发布）

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj:12`

- [ ] **Step 1: 递增版本号**（日期制，例如 `2026.10.11.1`）。

- [ ] **Step 2: 构建确认可编译**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: BUILD 成功（经项目引用直接编译框架源码）。

- [ ] **Step 3: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/LuBan.AIAgent.csproj
git -C luban-framework commit -m "递增 LuBan.AIAgent 版本"
```

> 不发布。最终发包与还原 PackageReference 见 P2 计划 Phase N。

---

## Phase G — agent：工作区作用域嵌套

### Task G1: `SessionRuntimeManager` 嵌套 workspace→session scope

**Files:**
- Modify: `luban-agent/LubanAgentCore/Services/SessionRuntimeManager.cs`

- [ ] **Step 1: 增加工作区 scope 缓存**

```csharp
private readonly ConcurrentDictionary<string, IServiceScope> _workspaceScopes = new();

private IServiceScope GetWorkspaceScope(string workspaceId)
    => _workspaceScopes.GetOrAdd(workspaceId, _ => _root.CreateScope());

public SessionRuntime GetOrCreate(string sessionId, string workspaceId, string workspaceRoot)
{
    return _runtimes.GetOrAdd(sessionId, id =>
    {
        var wsScope = GetWorkspaceScope(workspaceId);
        // 从工作区 scope 派生会话 scope，使工作区级 scoped 服务在会话间共享、会话级服务各自独立
        var sessionScopeFactory = wsScope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
        var scope = sessionScopeFactory.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<SessionContextState>();
        state.SessionId = id; state.WorkspaceId = workspaceId; state.WorkspaceRoot = workspaceRoot;
        state.AllowedRoots.Add(workspaceRoot);
        var ctx = scope.ServiceProvider.GetRequiredService<ISessionContext>();
        var rt = new SessionRuntime(scope, id, ctx);
        rt.Start();
        return rt;
    });
}
```

- [ ] **Step 2: 释放工作区 scope**

`DisposeAsync` 中在释放所有会话 scope 后释放 `_workspaceScopes`。

- [ ] **Step 3: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentCore/Services/SessionRuntimeManager.cs
git -C luban-agent commit -m "feat: 会话作用域嵌套于工作区作用域"
```

---

### Task G2: agent 工作区级服务注册为 scoped

**Files:**
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`
- Modify: `luban-agent/LubanAgentCore/Agents/WorkspaceWikiContext.cs:22`
- Modify: `luban-agent/LubanAgentCore/Services/WorkspaceManager.cs`

- [ ] **Step 1: 按工作区隔离的服务改 scoped**

把工作区内容相关服务（wiki/vector、工作区感知的检索、`IWikiContext`/`WorkspaceWikiContext`）改为 `AddScoped`，确保每个工作区 scope 一份；用户级数据（工作区列表仓储）保持单例。

- [ ] **Step 2: 移除“每轮重载”临时策略**

删除 P0 为规避全局覆盖而保留的每轮重载工作区级注册表的代码路径。

- [ ] **Step 3: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功；`ValidateOnBuild` 无 captive 报错。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentCore
git -C luban-agent commit -m "refactor: 工作区级服务按工作区作用域隔离"
```

---

## Phase H — 验证

### Task H1: 自动化测试与构建

- [ ] **Step 1: 框架单测**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WorkspaceScopeRegistryTests"`
Expected: PASS。

- [ ] **Step 2: 两端构建**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: BUILD 成功。

### Task H2: 手动冒烟

- [ ] **Step 1: 跨工作区并发**：工作区 A 会话与工作区 B 会话同时运行，工具/规则/技能/MCP 互不覆盖。
- [ ] **Step 2: 切换不影响运行**：切换查看任意工作区任意会话，后台继续。
- [ ] **Step 3: 工作区隔离**：A 的 wiki/检索与 B 不串。

记录结果；失败项回到对应 Task 修复。

---

## Self-Review

- **Spec 覆盖**：§3.3 规则 5（注册表 per-workspace）→F1；§3.4 注册表行→F1；§7 `RuleEngine`/`SkillRegistry`/`MCPRegistry` P1 行→F1；§9 P1→F/G；§11 验收跨工作区→H2。
- **占位符**：无 TBD。
- **类型一致性**：`GetWorkspaceScope`/`IServiceScopeFactory` 嵌套与 P0 `SessionRuntimeManager` 一致。

## 已知风险

- 工作区 scope 缓存需与空闲卸载/应用关闭对齐释放，避免 scope 泄漏。
- 若 P0 有单例误注入工作区 scoped 服务，`ValidateOnBuild` 会在 G2 暴露，需回改注册。
- 框架改动经 ProjectReference 生效，无需发包（F2 仅递增版本占位）。