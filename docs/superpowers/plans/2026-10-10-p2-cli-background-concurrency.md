# P2 CLI 后台并发 + per-user keyed + 清理 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 CLI（Terminal.Gui）支持多会话后台并发（会话切换时后台继续运行、可切回查看流式输出），并把用户级共享缓存按 `userId` 做 keyed 隔离，清理剩余进程级静态，调优空闲卸载。

**Architecture:** CLI 每个会话绑定独立 `ConversationViewModel` + `ISessionContext`，`RootView` 用 per-session 输入队列（`Channel`）支撑后台消息泵；切换仅切换绑定视图，不停止会话。共享 LLM 侧缓存改 `KeyedService`（key=userId）。

**Tech Stack:** .NET 10、Terminal.Gui、Microsoft Agent Framework、MSTest。

**Repo 根:**
- framework: `D:\WorkBench\Walle\luban\luban-framework`
- agent: `D:\WorkBench\Walle\luban\luban-agent`

**前置:** P0、P1 完成。开发期 agent 对框架走 `ProjectReference`（提交 `de822af`），本计划 Phase I–M **不发布中间包**；Task I2 仅递增版本号占位。全部验收后由 **Phase N** 统一发包并还原 PackageReference。

**依赖顺序:** I（框架 keyed）→ J（agent per-user）→ K（CLI 并发）→ L（清理/调优）→ M（验证）。

---

## File Structure

### framework `luban-framework`
- Modify `LuBan.AIAgent/Providers/ProviderHelper.cs` — 缓存按 userId keyed
- Modify `LuBan.AIAgent/Models/ModelCapabilityRegistry.cs` — 缓存按 userId/租户 keyed
- Test `LuBan.XTestProject/AIAgent/PerUserCacheTests.cs`

### agent `luban-agent`
- Modify `LubanAgentCore/Hosting/AgentHostBuilder.cs` — keyed 注册、清理剩余单例
- Modify `LubanAgentCore/Services/SessionManager.cs` — 剩余静态/`CurrentSession` 清理
- Modify `LubanAgentCore/Agents/WorkspaceManager.cs` — 剩余静态清理（若 P1 未覆盖）
- Modify `LubanAgentCli/Views/RootView.cs`、`LubanAgentCli/ViewModels/ConversationViewModel.cs` — per-session 视图与后台消息泵
- Test/冒烟

---

## Phase I — framework：per-user keyed 缓存

### Task I1: `ProviderHelper` 缓存 keyed

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/Providers/ProviderHelper.cs`
- Test: `luban-framework/LuBan.XTestProject/AIAgent/PerUserCacheTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using LuBan.AIAgent.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LuBan.UnitTestProject.AIAgent;

[TestClass]
public class PerUserCacheTests
{
    [TestMethod]
    public void ProviderCache_IsolatedPerUser()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton<ProviderCache>("userA");
        services.AddKeyedSingleton<ProviderCache>("userB");
        using var sp = services.BuildServiceProvider();
        Assert.AreNotSame(sp.GetRequiredKeyedService<ProviderCache>("userA"),
                          sp.GetRequiredKeyedService<ProviderCache>("userB"));
        Assert.AreSame(sp.GetRequiredKeyedService<ProviderCache>("userA"),
                       sp.GetRequiredKeyedService<ProviderCache>("userA"));
    }
}
```

> 若 `ProviderHelper` 内缓存非独立类型，测试改为断言两 userId 解析出的 provider 实例不同。

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~PerUserCacheTests"`
Expected: FAIL（当前共享）。

- [ ] **Step 3: 改 keyed 注册与取用**

把当前共享缓存改为按 `userId` keyed（`AddKeyedSingleton` + `GetRequiredKeyedService`），`ProviderHelper` 取用时传入当前 `userId`。

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~PerUserCacheTests"`
Expected: PASS。

- [ ] **Step 5: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/Providers/ProviderHelper.cs LuBan.XTestProject/AIAgent/PerUserCacheTests.cs
git -C luban-framework commit -m "provider 缓存按 userId 隔离"
```

---

### Task I2: `ModelCapabilityRegistry` 缓存 keyed + 递增版本

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/Models/ModelCapabilityRegistry.cs`
- Modify: `luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj:12`

- [ ] **Step 1: 缓存按 userId/租户 keyed**（同 I1 模式）。

- [ ] **Step 2: 回归**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~Provider|FullyQualifiedName~ModelCapability"`
Expected: PASS（环境依赖失败需甄别）。

- [ ] **Step 3: 递增版本号（不发布）**

- [ ] **Step 4: 构建确认可编译**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: BUILD 成功（经项目引用直接编译框架源码）。

- [ ] **Step 5: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/Models/ModelCapabilityRegistry.cs LuBan.AIAgent/LuBan.AIAgent.csproj
git -C luban-framework commit -m "模型能力缓存按 userId 隔离并递增版本"
```

> 不发布。最终发包与还原见 Phase N。

---

## Phase J — agent：接入 per-user + 清理剩余单例

### Task J1: 接入 keyed 依赖与清理剩余单例

**Files:**
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`
- Modify: `luban-agent/LubanAgentCore/Services/SessionManager.cs`

- [ ] **Step 1: 注册 keyed 依赖并传入 userId**

在装配处注册 keyed provider/能力缓存；在解析点传入当前 `userId`。

- [ ] **Step 2: 清理剩余进程级静态/单例**

处理 P0/P1 遗留的按用户或会话变化却仍为单例的服务；无法安全降级的保留单例并注释理由。

- [ ] **Step 3: 构建**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: BUILD 成功；`ValidateOnBuild` 无 captive 报错。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentCore
git -C luban-agent commit -m "feat: 接入 per-user keyed 缓存并清理剩余单例"
```

---

## Phase K — CLI 后台并发

### Task K1: per-session `ConversationViewModel` 与输入队列

**Files:**
- Modify: `luban-agent/LubanAgentCli/ViewModels/ConversationViewModel.cs`
- Modify: `luban-agent/LubanAgentCli/Views/RootView.cs`

- [ ] **Step 1: 每个会话独立 VM + 队列**

`RootView` 为每个 `sessionId` 维护一个 `ConversationViewModel` 与其输入 `Channel`；会话运行写入自有 VM，不阻塞 UI。

- [ ] **Step 2: 切换仅换绑定视图**

切换会话时只切换绑定/重绘，不取消已运行会话；后台流式更新经队列泵入对应 VM。

- [ ] **Step 3: 构建**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: BUILD 成功。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentCli
git -C luban-agent commit -m "feat: CLI 会话后台并发与切换视图"
```

---

## Phase L — 清理与调优

### Task L1: 剩余静态清理

- [ ] **Step 1: 全局排查**

Grep agent 内 `static` 可变字段、`Lazy<...>` 进程级缓存、`static` 事件；逐项判定作用域，改实例/作用域，不能改的注释理由。

- [ ] **Step 2: 构建**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: BUILD 成功。

- [ ] **Step 3: 提交**

```bash
git -C luban-agent add LubanAgentCore
git -C luban-agent commit -m "refactor: 清理剩余进程级可变静态"
```

### Task L2: 空闲卸载调优

- [ ] **Step 1: 参数化空闲卸载**（默认 30 分钟，可配置），确保卸载时联动释放会话 scope 与工作区 scope。

- [ ] **Step 2: 构建**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: BUILD 成功。

- [ ] **Step 3: 提交**

```bash
git -C luban-agent add LubanAgentCore
git -C luban-agent commit -m "feat: 空闲卸载可配置并联动释放作用域"
```

---

## Phase M — 验证

### Task M1: 自动化测试与构建

- [ ] **Step 1: 框架单测**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~PerUserCacheTests"`
Expected: PASS。

- [ ] **Step 2: 两端构建**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: BUILD 成功。

### Task M2: 手动冒烟

- [ ] **Step 1: CLI 后台并发**：会话 A 运行中切到 B 发起，再切回 A 可见持续流式输出。
- [ ] **Step 2: per-user 隔离**：切换用户账号，缓存不串。
- [ ] **Step 3: 空闲卸载**：达到阈值后作用域释放、内存回落。

记录结果；失败项回对应 Task 修复。

---

## Phase N — 验收后统一发包并还原为 PackageReference

> 触发条件：P0/P1/P2 三阶段全部通过验收。此阶段是**人工操作节点**，发包前暂停。

### Task N1: 发布最终框架包

**Files:**
- 涉及版本：`luban-framework/LuBan.AIAgent`、`LuBan.Common`（P0 A4 改动）等本三阶段被修改的框架项目

- [ ] **Step 1: 汇总待发包**

本三阶段改动的框架项目：`LuBan.AIAgent`、`LuBan.Common`（其余框架项目经 ProjectReference 传递编译，若其源码亦被改动需一并纳入）。

- [ ] **Step 2: 递增并构建 Release**

对每个待发包项目递增 `<Version>`（日期制）后：

Run: `dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj -c Release`
Run: `dotnet build luban-framework/LuBan.Common/LuBan.Common.csproj -c Release`
Expected: 各生成 `bin/Release/*.nupkg`。

- [ ] **Step 3: 人工发布**

**（暂停，等用户确认）** 按 `AGENTS.md` 流程发布到 NuGet 源；记录最终版本号。

### Task N2: 还原 agent 框架引用为 PackageReference

**Files:**
- Modify: `luban-agent/LubanAgentCore/LubanAgentCore.csproj`
- Modify: `luban-agent/LubanAgentA2A/LubanAgentA2A.csproj`

- [ ] **Step 1: 用最终版本号替换 ProjectReference**

移除 `TEMP-LOCAL-VERIFY` 标记的 `ProjectReference`，恢复为 `PackageReference`（版本= N1 发布号）：

```xml
<PackageReference Include="LuBan.Common" Version="<final>" />
<PackageReference Include="LuBan.DI" Version="<final>" />
<PackageReference Include="LuBan.Threading" Version="<final>" />
<PackageReference Include="LuBan.Logging" Version="<final>" />
<PackageReference Include="LuBan.Orm" Version="<final>" />
<PackageReference Include="LuBan.AIAgent" Version="<final>" />
```
`LubanAgentA2A.csproj` 同步还原 `LuBan.AIAgent` 的 PackageReference。

- [ ] **Step 2: 还原后构建（不再依赖本地源码）**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: BUILD 成功，且不再编译 `luban-framework` 源码。

- [ ] **Step 3: 提交**

```bash
git -C luban-agent add LubanAgentCore/LubanAgentCore.csproj LubanAgentA2A/LubanAgentA2A.csproj
git -C luban-agent commit -m "chore: 验收后框架引用还原为 PackageReference"
```

---

## Self-Review

- **Spec 覆盖**：§9 P2（CLI 后台并发、per-user keyed、清理剩余静态、空闲卸载调优）→I/J/K/L；§11 验收→M2；开发期项目引用策略→N（发包+还原）。
- **占位符**：`<final>` 为 N1 人工发布的版本号，属预期占位，非 TBD。
- **类型一致性**：keyed 注册与取用 API 一致；CLI 队列/VM 与 P0 `SessionRuntimeManager` 命名一致。

## 已知风险

- CLI 并发须确认 Terminal.Gui 主循环线程安全（更新经 `Application.Invoke` 或队列泵）。
- keyed 依赖解析需在正确的用户上下文（会话/工作区）内进行，避免解析到错误 userId。
- **还原为 PackageReference 后必须依赖已发布的最终包**：N2 构建前确认 N1 已发布成功，否则会出现包版本解析失败。
- 若三阶段期间还改了 `LuBan.DI`/`LuBan.Threading`/`LuBan.Logging`/`LuBan.Orm` 源码，N1 需一并发包并统一版本号，避免混版。