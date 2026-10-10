# P1 跨工作区并发 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让同一用户的多个工作区会话在单进程内**同时**安全运行。P0 已实现同工作区并发并交付会话 scope；P0 期间跨工作区同时运行会因工作区级注册表被覆盖而退化为串行。

**Architecture（降级路线，已定）：** MS.DI **不存在父子/嵌套 scope**——`IServiceScopeFactory` 恒从 root 派生兄弟 scope，因此“会话 scope 嵌套于工作区 scope 以共享工作区级服务”无法用原生 DI 实现。本计划**放弃工作区级跨会话共享**，采用最简单可行的降级方案：
- 所有原本“工作区级”的注册表/规则/技能/MCP（`RuleEngine`/`SkillRegistry`/`MCPRegistry` 及其 `IRule`/`ISkill`/`IMCPClient`）直接改为 **scoped（会话 scope）**，每个会话一份，彻底消除跨会话/跨工作区覆盖；
- 复用 P0 的单层会话 scope（root → session），**不引入 workspace scope**；
- 移除 P0 为规避全局覆盖而保留的“每轮重载工作区级注册表”临时策略。

**代价（已知并接受）：** 同一工作区多个会话会各自加载一份规则/技能/MCP（重复加载）。后续若真需跨会话共享工作区状态，另起方案（keyed singleton / 子容器），不在本阶段。

**Tech Stack:** .NET 10、Microsoft.Extensions.DependencyInjection、Microsoft Agent Framework、SqlSugar(SQLite)、MSTest。

**Repo 根:**
- framework: `D:\WorkBench\Walle\luban\luban-framework`
- agent: `D:\WorkBench\Walle\luban\luban-agent`

**前置:** P0 完成。开发期 agent 对框架走 `ProjectReference`（提交 `de822af`），本计划**不发布中间包**；Task F2 仅递增版本号占位。最终发包与还原 PackageReference 见 P2 计划 Phase N。

**依赖顺序:** F（框架，注册表 scoped）→ G（agent 移除临时策略/工作区内容服务 scoped）→ H（验证）。

---

## File Structure

### framework `luban-framework`
- Modify `LuBan.AIAgent/LuBanAgentExtensions.cs:66-86` — `IRule`/`RuleEngine`/`IMCPClient`/`MCPRegistry`/`ISkill`/`SkillRegistry` singleton→scoped
- Modify `LuBan.AIAgent/LuBanAgentExtensions.cs:125` — `ContextInjectBuilder` singleton→scoped（其依赖 `RuleEngine` 变 scoped，否则 captive）
- Test `LuBan.XTestProject/AIAgent/SessionScopeRegistryTests.cs`

### agent `luban-agent`
- Modify `LubanAgentCore/Services/WorkspaceManager.cs` — 移除“每轮重载工作区级注册表”临时策略
- Modify `LubanAgentCore/Hosting/AgentHostBuilder.cs` — 工作区内容相关服务（wiki/检索）改 scoped；移除残留单例覆盖
- Modify `LubanAgentCore/Agents/WorkspaceWikiContext.cs:22` — 经 `IWorkspaceContext`/`ISessionContext` 按会话隔离
- Test/冒烟

> 无 `SessionRuntimeManager` 嵌套 workspace scope 改动：P0 的 `_root.CreateScope()`（单层会话 scope）即为最终形态。

---

## Phase F — framework：工作区级注册表改 session-scoped

### Task F1: 注册表 singleton→scoped

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/LuBanAgentExtensions.cs:66-86,125`
- Test: `luban-framework/LuBan.XTestProject/AIAgent/SessionScopeRegistryTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Rules;
using LuBan.AIAgent.Skills;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LuBan.UnitTestProject.AIAgent;

[TestClass]
public class SessionScopeRegistryTests
{
    private static ServiceProvider Build()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLuBanAgent(config);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [TestMethod]
    public void RuleEngine_DiffersPerSessionScope()
    {
        using var sp = Build();
        using var a = sp.CreateScope();
        using var b = sp.CreateScope();
        Assert.AreNotSame(a.ServiceProvider.GetRequiredService<RuleEngine>(),
                          b.ServiceProvider.GetRequiredService<RuleEngine>());
    }

    [TestMethod]
    public void SkillRegistry_DiffersPerSessionScope()
    {
        using var sp = Build();
        using var a = sp.CreateScope();
        using var b = sp.CreateScope();
        Assert.AreNotSame(a.ServiceProvider.GetRequiredService<SkillRegistry>(),
                          b.ServiceProvider.GetRequiredService<SkillRegistry>());
    }

    [TestMethod]
    public void McpRegistry_DiffersPerSessionScope()
    {
        using var sp = Build();
        using var a = sp.CreateScope();
        using var b = sp.CreateScope();
        Assert.AreNotSame(a.ServiceProvider.GetRequiredService<MCP.MCPRegistry>(),
                          b.ServiceProvider.GetRequiredService<MCP.MCPRegistry>());
    }
}
```

> 说明：因已放弃工作区级跨会话共享，`AreNotSame`（不同会话 scope 各自一份）即为期望语义。

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~SessionScopeRegistryTests"`
Expected: FAIL（当前为同一单例）。

- [ ] **Step 3: 改注册**

把 `LuBanAgentExtensions.cs:66-73`、`:76-86` 的 `IRule`/`RuleEngine`/`IMCPClient`/`MCPRegistry`/`ISkill`/`SkillRegistry` 改为 `AddScoped`。`ContextInjectBuilder`（`:125`，注入 `RuleEngine`）**必须同步改 `AddScoped`**，否则 `ValidateOnBuild` 报 captive。

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~SessionScopeRegistryTests"`
Expected: PASS。

- [ ] **Step 5: 回归既有测试**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~Rule|FullyQualifiedName~Skill|FullyQualifiedName~MCP"`
Expected: PASS（环境依赖型失败需甄别）。

- [ ] **Step 6: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/LuBanAgentExtensions.cs LuBan.XTestProject/AIAgent/SessionScopeRegistryTests.cs
git -C luban-framework commit -m "工作区级注册表改会话 scoped 支持跨工作区并发"
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

## Phase G — agent：移除临时策略 + 工作区内容服务按会话 scope

### Task G1: 移除“每轮重载工作区级注册表”临时策略

**Files:**
- Modify: `luban-agent/LubanAgentCore/Services/WorkspaceManager.cs`
- Modify: `luban-agent/LubanAgentCore/Services/AgentHostService.cs`

- [ ] **Step 1: 定位并删除重载路径**

搜索并删除 P0 为规避全局覆盖而保留的“进入会话/每轮时重新从工作区配置装载规则/技能/MCP 到进程级注册表”的代码路径（若存在于 `AgentHostService.ConfigureForTurn`/`Reset` 或 `WorkspaceManager` 的工作区切换流程）。这些注册表现已 scoped，由会话 scope 各自持有，无需重载。

- [ ] **Step 2: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功。

- [ ] **Step 3: 提交**

```bash
git -C luban-agent add LubanAgentCore
git -C luban-agent commit -m "refactor: 移除工作区级注册表每轮重载临时策略"
```

---

### Task G2: 工作区内容服务（wiki/检索）改 session-scoped

**Files:**
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`
- Modify: `luban-agent/LubanAgentCore/Agents/WorkspaceWikiContext.cs:22`

- [ ] **Step 1: 按会话隔离的服务改 scoped**

把工作区内容相关服务（wiki/vector、工作区感知的检索、`IWikiContext`/`WorkspaceWikiContext`）改为 `AddScoped`（或经 `ISessionContext`/`IWorkspaceContext` 取值），确保不同会话/工作区不串；用户级数据（工作区列表仓储）保持单例。

- [ ] **Step 2: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功；`ValidateOnBuild` 无 captive 报错。

- [ ] **Step 3: 提交**

```bash
git -C luban-agent add LubanAgentCore
git -C luban-agent commit -m "refactor: 工作区内容服务按会话作用域隔离"
```

---

## Phase H — 验证

### Task H1: 自动化测试与构建

- [ ] **Step 1: 框架单测**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~SessionScopeRegistryTests"`
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

- **Spec 覆盖**：§3.3 规则 5（注册表 per-workspace）→ 降级为 per-session，F1；§3.4 注册表行→F1；§7 `RuleEngine`/`SkillRegistry`/`MCPRegistry` P1 行→F1；§9 P1→F/G；§11 验收跨工作区→H2。
- **占位符**：无 TBD。
- **类型一致性**：默认复用 P0 `SessionRuntimeManager` 的单层 `_root.CreateScope()`；不涉及 workspace scope。
- **架构偏离说明**：原 §3.2/§3.3 的“L2 工作区 scope”在 MS.DI 下不可实现，本文档明确降级为“工作区服务 = session-scoped”，spec 中 L2 相关描述需同步标注为“已降级”。

## 已知风险

- **重复加载**：同工作区多会话各持一份规则/技能/MCP，内存与初始化开销上升；可接受。
- 若 P0 有单例误注入这些 scoped 服务，`ValidateOnBuild` 会在 F1/G2 暴露，需回改注册。
- 框架改动经 ProjectReference 生效，无需发包（F2 仅递增版本占位）。