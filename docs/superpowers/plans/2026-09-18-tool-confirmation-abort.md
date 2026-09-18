# 工具确认：拒绝即终止本轮 + 等待无限期 + 子代理代确认 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用户在工具确认提示中拒绝时，两端立即终止当前对话回合并显示专用文案；主代理确认等待改为无限期；编排子代理不再向用户弹确认（由主代码按“本轮已允许集合”代确认）；主代码等待子代理/反思改为默认无限期。

**Architecture:** 框架在 `ToolConfirmationContext` 增加「用户拒绝钩子」与「子代理 AsyncLocal 作用域」；拒绝钩子仅在“用户回调返回 false 且本轮 token 未取消”时触发，宿主订阅后取消本轮 CTS；子代理作用域内确认服务跳过宿主回调，改查 `AllowedThisTurn`（命中放行、否则返回子代理专用拒绝结果）；编排节点/反思超时默认改 0（无限期）。

**Tech Stack:** .NET 10 / `Microsoft.Extensions.AI` 10.10.0 / MSTest 4.4.0 / Terminal.Gui CLI / Avalonia Codex

---

## 已定稿决策（访谈结论，勿改）

| 决策点 | 结论 |
|---|---|
| 拒绝范围 | 仅工具调用确认（CLI 内联选择块、Codex 确认卡）；工作区授权与规则拒绝不在此列 |
| 终止语义 | 复用 Esc 取消机制（作废 runSeq、停流、`IsRunning` 复位、可继续输入）+ 专用文案「已拒绝，本轮终止」；保留已输出内容 |
| 触发方式 | 框架通用钩子（`ToolConfirmationContext.OnUserDenied`），宿主每轮注入 |
| 触发条件 | 用户回调返回 `false` **且**本轮 token 未被取消；`Callback==null` 与 Esc 取消不触发 |
| Esc 语义 | 确认块上的 Esc（产生 Deny、token 未取消）→ 终止并显示「已拒绝，本轮终止」；全局 Esc（token 取消）→ 仍「任务已取消」（Codex 的 Esc 属后者） |
| 主代理等待 | 无限期：移除两端 2 分钟超时（工作区授权本就无限期，不变） |
| 子代理确认 | 不弹窗；由主代码代确认：`AllowedThisTurn` 命中 → 放行；否则返回**子代理专用拒绝文案**（不是“用户拒绝”） |
| 子代理识别 | `ToolConfirmationContext` 静态 `AsyncLocal` 作用域，`DagScheduler` 节点执行期进入（并行节点互不串扰） |
| 子代理等待 | 无限期：节点与反思超时默认改 0（0/负数=无限），节点级 `TimeoutSeconds>0` 仍可显式限时 |
| UI 标记 | CLI 被拒工具块显示「✗ 已被用户拒绝」；Codex 确认卡变「已拒绝」状态、工具卡标失败 |
| 验证 | 框架单测（确认三态/子代理代确认/钩子排除取消/子代理文案）+ TUI 冒烟 + README 中英文 |
| 发版 | 用户手工发版；NuGet 引用回退等发版后再定（开发期继续 `ProjectReference`） |

## 涉及文件

**framework（`D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent`）**

| 动作 | 文件 | 职责 |
|---|---|---|
| Modify | `Abstractions\ToolConfirmationService.cs` | 钩子、子代理作用域、代确认分支、AllowedThisTurn 线程安全 |
| Modify | `Abstractions\ToolResult.cs` | 新增 `SubAgentDenied<T>()` 与 `Denied<T>()`（按作用域选择） |
| Modify | `Orchestration\DagScheduler.cs` | 节点执行进入子代理作用域；节点超时 0=无限 |
| Modify | `Orchestration\Orchestrator.cs` | 反思超时 0=无限 |
| Modify | `Configuration\OrchestrationOptions.cs` | 两个超时默认改 0 + XML 文档 |
| Modify | 14 处确认拒绝调用点（8 文件） | `ToolResult.Cancelled<T>()` → `ToolResult.Denied<T>()` |
| Modify | `README.md` / `README.en.md` | 新增「工具确认与终止语义」章节 |
| Create | `LuBan.XTestProject\ToolConfirmationUnitTest.cs` | 确认引擎 + 钩子 + 子代理代确认单测 |

**agent（`D:\WorkBench\Walle\luban\luban-agent`）**

| 动作 | 文件 | 职责 |
|---|---|---|
| Modify | `LubanAgentCore\Services\ToolConfirmationContextExtensions.cs` | `ConfigureForTurn` 增加 `onUserDenied` |
| Modify | `LubanAgentCore\Services\AgentHostService.cs` | 透传钩子；被拒结果发 `ToolCallFailedEvent` |
| Modify | `LubanAgentCli\App\ViewModels\ConversationViewModel.cs` | 钩子→终止、无限等待、专用文案、工具块标记 |
| Modify | `LubanAgentCodex\ViewModels\MainWindowViewModel.cs` | 钩子→终止、无限等待、专用文案 |
| Modify | `LubanAgentCodex\Views\Controls\ConfirmCard.axaml.cs` | 拒绝/允许后卡片状态文案 |
| Modify | `LubanAgentCli\appsettings.json` / `LubanAgentCodex\appsettings.json` | 删除 `DefaultNodeTimeoutSeconds: 300` |
| Modify | `LubanAgentCli\docs\TUI-SMOKE-TEST.md` | 新增工具确认用例 50–56 |

---

## Task 1: 框架确认上下文：钩子 + 子代理作用域 + 代确认（TDD）

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/Abstractions/ToolConfirmationService.cs`
- Modify: `luban-framework/LuBan.AIAgent/Abstractions/ToolResult.cs`
- Create: `luban-framework/LuBan.XTestProject/ToolConfirmationUnitTest.cs`

- [x] **Step 1: 写失败测试**

`LuBan.XTestProject/ToolConfirmationUnitTest.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.XTestProject
*文件名： ToolConfirmationUnitTest
*唯一标识：工具确认引擎与子代理代确认单测
*创建时间：2026/9/18
*描述：验证确认三态、用户拒绝钩子（排除取消）、子代理代确认与专用文案
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Configuration;

namespace LuBan.XTestProject;

[TestClass]
public class ToolConfirmationUnitTest
{
    private const string WorkspaceRoot = @"C:\work";

    private static ToolConfirmationService CreateService(out ToolConfirmationContext context)
    {
        context = new ToolConfirmationContext();
        return new ToolConfirmationService(context, null);
    }

    private static void Configure(
        ToolConfirmationContext context,
        ToolPermissionMode mode = ToolPermissionMode.Default,
        Func<string, IReadOnlyDictionary<string, object?>, Task<bool>>? callback = null,
        Action<string, IReadOnlyDictionary<string, object?>>? onUserDenied = null,
        string? allowedTool = null)
    {
        context.Mode = mode;
        context.WorkspacePathChecker = path => path.StartsWith(WorkspaceRoot, StringComparison.OrdinalIgnoreCase);
        context.Callback = callback;
        context.OnUserDenied = onUserDenied;
        if (allowedTool is not null)
            context.AllowThisTurn(allowedTool);
    }

    [TestMethod]
    public async Task Default_WorkspaceInternalPath_IsAllowedWithoutCallback()
    {
        var service = CreateService(out var context);
        var callbackInvoked = false;
        Configure(context, callback: (_, _) => { callbackInvoked = true; return Task.FromResult(true); });

        var outcome = await service.EvaluateAsync("WriteFileAsync", @"C:\work\a.txt", new Dictionary<string, object?>());

        Assert.AreEqual(EnumConfirmationOutcome.Allowed, outcome);
        Assert.IsFalse(callbackInvoked);
    }

    [TestMethod]
    public async Task Default_WorkspaceExternalPath_CallbackTrue_IsAllowed()
    {
        var service = CreateService(out var context);
        Configure(context, callback: (_, _) => Task.FromResult(true));

        var outcome = await service.EvaluateAsync("WriteFileAsync", @"D:\other\a.txt", new Dictionary<string, object?>());

        Assert.AreEqual(EnumConfirmationOutcome.Allowed, outcome);
    }

    [TestMethod]
    public async Task Default_CallbackFalse_IsDenied_AndFiresUserDeniedHook()
    {
        var service = CreateService(out var context);
        string? deniedTool = null;
        Configure(context,
            callback: (_, _) => Task.FromResult(false),
            onUserDenied: (tool, _) => deniedTool = tool);

        var outcome = await service.EvaluateAsync("WriteFileAsync", @"D:\other\a.txt", new Dictionary<string, object?>());

        Assert.AreEqual(EnumConfirmationOutcome.Denied, outcome);
        Assert.AreEqual("WriteFileAsync", deniedTool);
    }

    [TestMethod]
    public async Task Default_CallbackNull_IsDenied_ButDoesNotFireHook()
    {
        var service = CreateService(out var context);
        var hookFired = false;
        Configure(context, callback: null, onUserDenied: (_, _) => hookFired = true);

        var outcome = await service.EvaluateAsync("WriteFileAsync", @"D:\other\a.txt", new Dictionary<string, object?>());

        Assert.AreEqual(EnumConfirmationOutcome.Denied, outcome);
        Assert.IsFalse(hookFired);
    }

    [TestMethod]
    public async Task TokenCanceled_IsDenied_WithoutCallbackOrHook()
    {
        var service = CreateService(out var context);
        var callbackInvoked = false;
        var hookFired = false;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Configure(context,
            callback: (_, _) => { callbackInvoked = true; return Task.FromResult(true); },
            onUserDenied: (_, _) => hookFired = true);
        context.CancellationToken = cts.Token;

        var outcome = await service.EvaluateAsync("WriteFileAsync", @"D:\other\a.txt", new Dictionary<string, object?>());

        Assert.AreEqual(EnumConfirmationOutcome.Denied, outcome);
        Assert.IsFalse(callbackInvoked, "取消后不应再弹确认");
        Assert.IsFalse(hookFired, "取消不应触发用户拒绝钩子");
    }

    [TestMethod]
    public async Task CanceledDuringWait_WakesWithDeny_AndDoesNotFireHook()
    {
        var service = CreateService(out var context);
        var hookFired = false;
        using var cts = new CancellationTokenSource();
        Configure(context,
            callback: async (_, _) => { await cts.CancelAsync(); return false; },
            onUserDenied: (_, _) => hookFired = true);
        context.CancellationToken = cts.Token;

        var outcome = await service.EvaluateAsync("WriteFileAsync", @"D:\other\a.txt", new Dictionary<string, object?>());

        Assert.AreEqual(EnumConfirmationOutcome.Denied, outcome);
        Assert.IsFalse(hookFired, "等待期被取消（token 已取消）不应触发用户拒绝钩子");
    }

    [TestMethod]
    public async Task Plan_NonReadOnly_IsPlanned_AndInvokesPlannedAction()
    {
        var service = CreateService(out var context);
        string? planned = null;
        Configure(context, mode: ToolPermissionMode.Plan);
        context.OnPlannedAction = (tool, _) => planned = tool;

        var outcome = await service.EvaluateAsync("WriteFileAsync", @"C:\work\a.txt", new Dictionary<string, object?>());

        Assert.AreEqual(EnumConfirmationOutcome.Planned, outcome);
        Assert.AreEqual("WriteFileAsync", planned);
    }

    [TestMethod]
    public async Task Bypass_IsAllowed()
    {
        var service = CreateService(out var context);
        Configure(context, mode: ToolPermissionMode.BypassPermissions);

        var outcome = await service.EvaluateAsync("RunShellAsync", null, new Dictionary<string, object?>());

        Assert.AreEqual(EnumConfirmationOutcome.Allowed, outcome);
    }

    [TestMethod]
    public async Task AlwaysConfirmTool_CallbackFalse_IsDenied_EvenInsideWorkspace()
    {
        var service = CreateService(out var context);
        Configure(context, callback: (_, _) => Task.FromResult(false));

        var outcome = await service.EvaluateAsync("DeleteFileAsync", @"C:\work\a.txt", new Dictionary<string, object?>());

        Assert.AreEqual(EnumConfirmationOutcome.Denied, outcome);
    }

    [TestMethod]
    public async Task ReadOnlyTool_IsAllowed()
    {
        var service = CreateService(out var context);
        Configure(context, callback: (_, _) => Task.FromResult(false));

        var outcome = await service.EvaluateAsync("ReadFileAsync", @"D:\other\a.txt", new Dictionary<string, object?>());

        Assert.AreEqual(EnumConfirmationOutcome.Allowed, outcome);
    }

    [TestMethod]
    public async Task SubAgentScope_WithoutAllowedThisTurn_IsDenied_WithoutCallbackOrHook()
    {
        var service = CreateService(out var context);
        var callbackInvoked = false;
        var hookFired = false;
        Configure(context,
            callback: (_, _) => { callbackInvoked = true; return Task.FromResult(true); },
            onUserDenied: (_, _) => hookFired = true);

        EnumConfirmationOutcome outcome;
        using (ToolConfirmationContext.EnterSubAgentScope())
        {
            outcome = await service.EvaluateAsync("WriteFileAsync", @"D:\other\a.txt", new Dictionary<string, object?>());
        }

        Assert.AreEqual(EnumConfirmationOutcome.Denied, outcome);
        Assert.IsFalse(callbackInvoked, "子代理不应弹用户确认");
        Assert.IsFalse(hookFired, "子代理代确认拒绝不是用户拒绝，不应触发终止钩子");
    }

    [TestMethod]
    public async Task SubAgentScope_WithAllowedThisTurn_IsAllowed_WithoutCallback()
    {
        var service = CreateService(out var context);
        var callbackInvoked = false;
        Configure(context,
            callback: (_, _) => { callbackInvoked = true; return Task.FromResult(true); },
            allowedTool: "WriteFileAsync");

        EnumConfirmationOutcome outcome;
        using (ToolConfirmationContext.EnterSubAgentScope())
        {
            outcome = await service.EvaluateAsync("WriteFileAsync", @"D:\other\a.txt", new Dictionary<string, object?>());
        }

        Assert.AreEqual(EnumConfirmationOutcome.Allowed, outcome);
        Assert.IsFalse(callbackInvoked);
    }

    [TestMethod]
    public void SubAgentScope_DeniedResult_UsesSubAgentMessage()
    {
        ToolResult<string> inside;
        using (ToolConfirmationContext.EnterSubAgentScope())
        {
            inside = ToolResult.Denied<string>();
        }
        var outside = ToolResult.Denied<string>();

        Assert.IsFalse(inside.UserCancelled);
        StringAssert.Contains(inside.Message, "子代理");
        Assert.IsTrue(outside.UserCancelled);
        StringAssert.Contains(outside.Message, "用户拒绝");
    }

    [TestMethod]
    public void SubAgentScope_IsScopedPerAsyncFlow()
    {
        Assert.IsFalse(ToolConfirmationContext.IsInSubAgentScope);
        using (ToolConfirmationContext.EnterSubAgentScope())
        {
            Assert.IsTrue(ToolConfirmationContext.IsInSubAgentScope);
        }
        Assert.IsFalse(ToolConfirmationContext.IsInSubAgentScope);
    }

    [TestMethod]
    public void IsUserCancelled_RawToolResult_And_SerializedJsonElement()
    {
        Assert.IsTrue(ToolResult.IsUserCancelled(ToolResult.Cancelled<string>()));
        Assert.IsFalse(ToolResult.IsUserCancelled(ToolResult.Ok("x")));

        // M.E.AI 的 AIFunctionFactory 默认把返回值序列化成 JsonElement（camelCase）
        var camel = JsonSerializer.Deserialize<JsonElement>("{\"isSuccess\":false,\"message\":\"x\",\"userCancelled\":true}");
        Assert.IsTrue(ToolResult.IsUserCancelled(camel));

        var pascal = JsonSerializer.Deserialize<JsonElement>("{\"IsSuccess\":false,\"UserCancelled\":true}");
        Assert.IsTrue(ToolResult.IsUserCancelled(pascal));

        var notCancelled = JsonSerializer.Deserialize<JsonElement>("{\"isSuccess\":false,\"userCancelled\":false}");
        Assert.IsFalse(ToolResult.IsUserCancelled(notCancelled));
    }
}
```

Run:

```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ToolConfirmation"
```

Expected: 编译失败（`OnUserDenied`/`AllowThisTurn`/`EnterSubAgentScope`/`ToolResult.Denied` 不存在）。

- [x] **Step 2: `ToolResult` 增加两种拒绝结果**

`Abstractions/ToolResult.cs`：在 `Cancelled<T>()` 之后插入：

```csharp
    /// <summary>
    /// 创建"子代理场景需用户授权、已自动阻止"的失败结果。
    /// 与 <see cref="Cancelled{T}"/> 区分：<b>不是用户拒绝</b>（UserCancelled=false），
    /// 而是子代理无权代确认、由主代码按本轮已允许集合代判后阻止。
    /// </summary>
    public static ToolResult<T> SubAgentDenied<T>()
        => new()
        {
            IsSuccess = false,
            Message = "该操作需要用户授权，子代理场景已自动阻止本次调用。"
                + "请改用无需人工确认的操作（如工作区内读写），或把该步骤交由主代理直接执行。"
        };

    /// <summary>
    /// 按当前执行作用域生成拒绝结果：子代理作用域返回 <see cref="SubAgentDenied{T}"/>，
    /// 否则返回 <see cref="Cancelled{T}"/>（用户拒绝）。
    /// </summary>
    public static ToolResult<T> Denied<T>()
        => ToolConfirmationContext.IsInSubAgentScope ? SubAgentDenied<T>() : Cancelled<T>();

    /// <summary>
    /// 判断工具调用结果是否为“用户拒绝”。兼容两种形态：
    /// ① 工厂直传的原始 <see cref="ToolResult"/>；
    /// ② Microsoft.Extensions.AI 的 <c>AIFunctionFactory</c> 默认序列化后的 <see cref="JsonElement"/>
    ///    （camelCase <c>userCancelled</c>，兼容 PascalCase）。
    /// </summary>
    /// <param name="result"><c>FunctionResultContent.Result</c>。</param>
    /// <returns>用户拒绝返回 true。</returns>
    public static bool IsUserCancelled(object? result)
    {
        if (result is ToolResult toolResult)
            return toolResult.UserCancelled;

        if (result is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            if (element.TryGetProperty("userCancelled", out var camel) && camel.ValueKind == JsonValueKind.True)
                return true;
            if (element.TryGetProperty("UserCancelled", out var pascal) && pascal.ValueKind == JsonValueKind.True)
                return true;
        }

        return false;
    }
```

- [x] **Step 3: `ToolConfirmationContext` 增加钩子、子代理作用域与 AllowThisTurn**

`Abstractions/ToolConfirmationService.cs`——`ToolConfirmationContext` 内：

1. 用私有锁 + 方法替换 `AllowedThisTurn` 公有 HashSet（防并行子代理读取与主代理写入竞态）：

```csharp
    private readonly object _allowedLock = new();

    /// <summary>
    /// 判断本轮是否已允许指定工具（用户选择过"本轮全部允许"）。
    /// 线程安全：主代理写入与并行子代理读取可能并发。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <returns>本轮已允许返回 true。</returns>
    public bool IsAllowedThisTurn(string toolName)
    {
        lock (_allowedLock)
        {
            return AllowedThisTurn.Contains(toolName);
        }
    }

    /// <summary>
    /// 将工具加入本轮已允许集合（用户选择"本轮全部允许"时由宿主调用）。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    public void AllowThisTurn(string toolName)
    {
        lock (_allowedLock)
        {
            AllowedThisTurn.Add(toolName);
        }
    }
```

并把原 `public HashSet<string> AllowedThisTurn { get; } = ...` 改为私有（`Reset()` 内 `AllowedThisTurn.Clear()` 需在锁内执行）：

```csharp
    private HashSet<string> AllowedThisTurn { get; } = new(StringComparer.OrdinalIgnoreCase);
```

`Reset()` 中改为：

```csharp
        lock (_allowedLock)
        {
            AllowedThisTurn.Clear();
        }
        OnPlannedAction = null;
        OnUserDenied = null;
```

2. 增加用户拒绝钩子属性（放在 `OnPlannedAction` 之后）：

```csharp
    /// <summary>
    /// 用户明确拒绝本轮工具调用时的通知回调。宿主订阅后应立即取消本轮令牌以终止对话：
    /// 仅当用户确认回调返回 false、且本轮取消令牌尚未取消时触发（Esc 取消不触发）。
    /// 子代理代确认拒绝不触发（那不是用户的选择）。
    /// </summary>
    public Action<string, IReadOnlyDictionary<string, object?>>? OnUserDenied { get; set; }
```

3. 增加子代理作用域（静态成员，放在 `Reset()` 之前或类尾）：

```csharp
    private static readonly AsyncLocal<int> SubAgentScopeDepth = new();

    /// <summary>
    /// 当前调用链是否处于编排子代理执行作用域。
    /// 子代理不向用户弹确认，由主代码按本轮已允许集合代确认。
    /// </summary>
    public static bool IsInSubAgentScope => SubAgentScopeDepth.Value > 0;

    /// <summary>
    /// 进入子代理执行作用域（DagScheduler 在节点执行期调用）；Dispose 退出。
    /// AsyncLocal 保证并行节点各自独立，互不串扰。
    /// </summary>
    /// <returns>作用域令牌。</returns>
    public static IDisposable EnterSubAgentScope()
    {
        var previous = SubAgentScopeDepth.Value;
        SubAgentScopeDepth.Value = previous + 1;
        return new SubAgentScopeToken(previous);
    }

    private sealed class SubAgentScopeToken(int previous) : IDisposable
    {
        public void Dispose() => SubAgentScopeDepth.Value = previous;
    }
```

> `AsyncLocal` 需要 `using System.Threading;`——项目 `ImplicitUsings` 已包含，无需新增。

- [x] **Step 4: `ToolConfirmationService` 调整判定与钩子触发**

1. `EvaluateAsync` 中 `AllowedThisTurn` 读取改为 `_context.IsAllowedThisTurn(toolName)`（原 `:272-275`）。
2. `AskUserAsync` 改为：

```csharp
    private async Task<EnumConfirmationOutcome> AskUserAsync(
        string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        // 免确认名单：命中则直接放行，不打断用户
        if (AutoConfirmTools.Contains(toolName))
        {
            return EnumConfirmationOutcome.Allowed;
        }

        // 子代理作用域：不向用户弹确认，改由主代码按本轮已允许集合代确认
        if (ToolConfirmationContext.IsInSubAgentScope)
        {
            return _context.IsAllowedThisTurn(toolName)
                ? EnumConfirmationOutcome.Allowed
                : EnumConfirmationOutcome.Denied;
        }

        var callback = _context.Callback;
        if (callback == null)
        {
            return EnumConfirmationOutcome.Denied;
        }

        var allowed = await callback(toolName, arguments).ConfigureAwait(false);
        if (allowed)
        {
            return EnumConfirmationOutcome.Allowed;
        }

        // 用户明确拒绝（token 未取消）：通知宿主终止本轮
        if (!_context.CancellationToken.IsCancellationRequested)
        {
            _context.OnUserDenied?.Invoke(toolName, arguments);
        }

        return EnumConfirmationOutcome.Denied;
    }
```

- [x] **Step 5: 替换 14 处确认拒绝调用点**

以下文件/行把确认分支的 `ToolResult.Cancelled<...>()` 改为 `ToolResult.Denied<...>()`（保持 `Planned` 分支不变）：

| 文件 | 行 |
|---|---|
| `Tools\Script\ScriptToolPlugin.cs` | 125, 198, 265 |
| `Tools\Retrieval\RetrievalToolPlugin.cs` | 108, 141 |
| `Tools\Orchestration\OrchestrationToolGroup.cs` | 54 |
| `Tools\LocalMemory\LocalMemoryToolPlugin.cs` | 105, 207 |
| `Tools\FileSystem\FileSystemToolPlugin.cs` | 131 |
| `Tools\Context\CompactContextToolPlugin.cs` | 125 |
| `Tools\Browser\BrowserToolPlugin.cs` | 117, 221, 258 |
| `MCP\MCPToolPlugin.cs` | 115 |

批量替换后 grep 校验：`ToolResult.Cancelled<` 在 framework 仅剩 `ToolResult.cs` 自身（定义/引用）。

- [x] **Step 6: 运行测试确认通过**

```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ToolConfirmation"
dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj -c Debug
```

Expected: 16 个测试通过（含执行时新增的 `SubAgentScope_NestedAndDoubleDispose_RestoresDepthCorrectly`）；构建 0 警告 0 错误。

---

## Task 2: 子代理作用域接入 + 超时默认无限（framework）

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/Orchestration/DagScheduler.cs`
- Modify: `luban-framework/LuBan.AIAgent/Orchestration/Orchestrator.cs`
- Modify: `luban-framework/LuBan.AIAgent/Configuration/OrchestrationOptions.cs`

- [x] **Step 1: `DagScheduler` 节点执行进入子代理作用域**

`Orchestration/DagScheduler.cs:172-205`：把 `await foreach` 整段包进作用域。将原 173-204 行的执行块改为：

```csharp
            var swRun = Stopwatch.StartNew();
            // 改为流式执行：把子 Agent 的思考/正文/工具调用/工具结果按时间轴上报，
            // 使上层 UI 能像主对话一样实时看到子代理在做什么。
            // 子代理执行期进入确认作用域：工具确认不弹窗，由主代码按本轮已允许集合代确认。
            var reporter = new NodeActivityReporter(node.Id, onProgress, reportGate);
            var output = new StringBuilder();
            using (ToolConfirmationContext.EnterSubAgentScope())
            {
                await foreach (var update in agent.RunStreamingAsync(resolvedPrompt, cts.Token))
                {
                    if (update.Contents is null) continue;

                    foreach (var content in update.Contents)
                    {
                        switch (content)
                        {
                            case TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
                                reporter.Thinking(reasoning.Text);
                                break;

                            case FunctionCallContent functionCall:
                                reporter.ToolCall(functionCall.Name, SummarizeArguments(functionCall.Arguments), functionCall.CallId);
                                break;

                            case FunctionResultContent functionResult:
                                reporter.ToolResult(functionResult, functionResult.CallId);
                                break;

                            case TextContent text when !string.IsNullOrEmpty(text.Text):
                                output.Append(text.Text);
                                reporter.Text(text.Text);
                                break;
                        }
                    }
                }
            }
            reporter.Flush();
            swRun.Stop();
```

- [x] **Step 2: `DagScheduler` 节点超时 0=无限**

`Orchestration/DagScheduler.cs:161-166` 原代码：

```csharp
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var orchestrationOpts = _options.Value.Orchestration ?? new();
            if (node.TimeoutSeconds.HasValue)
                cts.CancelAfter(TimeSpan.FromSeconds(node.TimeoutSeconds.Value));
            else if (orchestrationOpts.DefaultNodeTimeoutSeconds > 0)
                cts.CancelAfter(TimeSpan.FromSeconds(orchestrationOpts.DefaultNodeTimeoutSeconds));
```

改为（0/负数=无限期，默认走无限；节点显式给出 `TimeoutSeconds` 时不再回落全局默认）：

```csharp
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var orchestrationOpts = _options.Value.Orchestration ?? new();
            if (node.TimeoutSeconds.HasValue)
            {
                // 节点显式指定：>0 限时；0/负数表示无限期，不再回落到全局默认
                if (node.TimeoutSeconds.Value > 0)
                    cts.CancelAfter(TimeSpan.FromSeconds(node.TimeoutSeconds.Value));
            }
            else if (orchestrationOpts.DefaultNodeTimeoutSeconds > 0)
            {
                cts.CancelAfter(TimeSpan.FromSeconds(orchestrationOpts.DefaultNodeTimeoutSeconds));
            }
```

（其余不动：`catch (OperationCanceledException) when (!ct.IsCancellationRequested)` → “节点执行超时”仅在显式配置限时时可能命中。）

- [x] **Step 3: `Orchestrator` 反思超时 0=无限**

`Orchestration/Orchestrator.cs:256-259` 原代码：

```csharp
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(orchestrationOpts.ReflectionTimeoutSeconds));

        return await _planner.ReflectAsync(context, cts.Token);
```

改为：

```csharp
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (orchestrationOpts.ReflectionTimeoutSeconds > 0)
            cts.CancelAfter(TimeSpan.FromSeconds(orchestrationOpts.ReflectionTimeoutSeconds));

        return await _planner.ReflectAsync(context, cts.Token);
```

- [x] **Step 4: `OrchestrationOptions` 默认值改 0**

`Configuration/OrchestrationOptions.cs`：

- `DefaultNodeTimeoutSeconds`（`:39-45`）改为：

```csharp
    /// <summary>
    /// 获取或设置 SubAgent 默认超时时间（秒）。默认 0 表示<b>无限期</b>等待子代理完成；
    /// 显式配置大于 0 时作为节点执行的墙钟上限（覆盖「多轮 LLM 调用 + 全部工具调用」的累计耗时）。
    /// 节点可通过 <see cref="Orchestration.Models.TaskNode.TimeoutSeconds"/> 单独覆盖（0/负数=无限）。
    /// </summary>
    public int DefaultNodeTimeoutSeconds { get; set; } = 0;
```

- `ReflectionTimeoutSeconds`（`:77-81`）改为：

```csharp
    /// <summary>
    /// 获取或设置反思阶段 LLM 调用的超时时间（秒）。默认 0 表示<b>无限期</b>；
    /// 显式配置大于 0 时作为单次反思调用的上限。
    /// </summary>
    public int ReflectionTimeoutSeconds { get; set; } = 0;
```

- [x] **Step 5: 构建验证**

```powershell
dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj -c Debug
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ToolConfirmation"
```

Expected: 构建 0/0；16 个测试通过（含执行时新增的 `SubAgentScope_NestedAndDoubleDispose_RestoresDepthCorrectly`）。

---

## Task 3: Core 透传钩子 + 被拒结果事件（agent）

**Files:**
- Modify: `luban-agent/LubanAgentCore/Services/ToolConfirmationContextExtensions.cs`
- Modify: `luban-agent/LubanAgentCore/Services/AgentHostService.cs`

- [x] **Step 1: `ConfigureForTurn` 增加钩子参数**

`ToolConfirmationContextExtensions.cs:45-59`：签名增加参数并透传：

```csharp
    /// <param name="onUserDenied">用户明确拒绝工具调用时的回调；宿主应在此取消本轮令牌以终止对话。</param>
    public static void ConfigureForTurn(
        this ToolConfirmationContext context,
        ToolPermissionMode mode,
        CancellationToken cancellationToken,
        Func<string, IReadOnlyDictionary<string, object?>, bool>? confirmCallback,
        Action<string, IReadOnlyDictionary<string, object?>>? onPlannedAction = null,
        Action<string, IReadOnlyDictionary<string, object?>>? onUserDenied = null)
    {
        context.Mode = mode;
        context.CancellationToken = cancellationToken;
        context.WorkspacePathChecker = static path => WorkspaceManager.IsWithinWorkspace(path);
        context.Callback = confirmCallback is null
            ? null
            : (toolName, args) => Task.FromResult(confirmCallback(toolName, args));
        context.OnPlannedAction = onPlannedAction;
        context.OnUserDenied = onUserDenied;
    }
```

- [x] **Step 2: `AgentHostService` 透传钩子 + 被拒结果发失败事件**

`AgentHostService.cs`：

1. 两个 `RunStreamingAsync` 重载增加参数 `Action<string, IReadOnlyDictionary<string, object?>>? onUserDenied = null`（放在 `onPlannedAction` 之后、`ct` 之前），并向下传递到 `RunCoreAsync`。
2. `RunCoreAsync` 签名同样增加该参数；`context.ConfigureForTurn(...)` 调用改为：

```csharp
        context.ConfigureForTurn(
            permissionMode,
            ct,
            (toolName, args) =>
            {
                var result = confirmHandler(toolName, args);
                if (result == ConfirmResult.AllowAll)
                    context.AllowThisTurn(toolName);
                return result != ConfirmResult.Deny;
            },
            onPlannedAction,
            onUserDenied);
```

3. 工具结果映射（`:263-276`）改为识别“用户拒绝”并发出失败事件：

```csharp
                    // 工具结果
                    if (content is FunctionResultContent functionResult)
                    {
                        if (functionResult.Exception is not null)
                        {
                            yield return new ToolCallFailedEvent(
                                functionResult.CallId,
                                functionResult.Exception.Message);
                        }
                        else if (ToolResult.IsUserCancelled(functionResult.Result))
                        {
                            // 拒绝与取消在结果层不可区分：用中性文案，宿主聊天流已分别提示
                            yield return new ToolCallFailedEvent(functionResult.CallId, "工具调用被拒绝或已取消");
                        }
                        else
                        {
                            yield return new ToolCallCompletedEvent(functionResult.CallId);
                        }
                        continue;
                    }
```

（`ToolResult` 在 `LuBan.AIAgent.Abstractions`，`AgentHostService.cs` 已 `using`/全局可见；若缺少 using，补 `using LuBan.AIAgent.Abstractions;`。）

- [x] **Step 3: 构建验证**

```powershell
dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj -c Debug
```

Expected: 0 警告 0 错误。（此时 CLI/Codex 尚未传新参数，编译不受影响，因为参数可选。）

---

## Task 4: CLI 接入（无限等待 + 钩子终止 + 标记）

**Files:**
- Modify: `luban-agent/LubanAgentCli/App/ViewModels/ConversationViewModel.cs`
- Modify: `luban-agent/LubanAgentCli/appsettings.json`
- Modify: `luban-agent/LubanAgentCli/App/Views/RootView.cs`（仅清理残留超时注释）
- Modify: `luban-agent/LubanAgentCli/App/Views/ConversationView.cs`（仅清理残留超时注释）
- Modify: `luban-agent/LubanAgentCli/App/Views/InputBarView.cs`（仅清理残留超时注释）

- [x] **Step 1: 增加“用户拒绝”标志字段**

`ConversationViewModel.cs` 字段区（`_cancelRequested` 之后，`:80` 附近）插入：

```csharp
    // 本轮是否因用户拒绝工具确认而终止：用于 OCE 收尾时区分「已拒绝，本轮终止」与「任务已取消」
    private volatile bool _deniedByUser;

    // 终止文案是否已输出：拒绝钩子与 OCE 分支都可能尝试提示，用 Interlocked 保证只显示一次
    private int _denialMessageShown;
```

- [x] **Step 2: 装配钩子**

`SetupConfirmationContext`（`:699-713`）的 `ConfigureForTurn` 调用增加参数：

```csharp
        context.ConfigureForTurn(
            PermissionMode,
            _currentCts?.Token ?? default,
            confirmCallback: ConfirmTool,
            onPlannedAction: (tool, args) =>
            {
                // 运行已作废（Esc/新对话）：丢弃计划项，避免污染新对话
                if (_currentRunSeq != Volatile.Read(ref _activeRunSeq)) return;

                Interlocked.Increment(ref _plannedCount);
                _dispatcher.Invoke(() =>
                    _doc.AppendBlock(new SystemBlock(
                        $"  📋 计划项（Plan 模式，未执行）: {tool} {ToolArgsFormatter.Summarize(args)}",
                        foreground: BlockColors.Thinking)));
            },
            onUserDenied: (tool, args) =>
            {
                // 陈旧运行（Esc/新 turn）不提示、不终止（与 onPlannedAction 守卫一致）
                if (_currentRunSeq != Volatile.Read(ref _activeRunSeq)) return;

                // 用户明确拒绝：标记并立即终止本轮（框架保证 token 未取消，即非 Esc）
                _deniedByUser = true;
                Logger.Warn($"用户拒绝工具调用，终止本轮: {tool}");

                // 直接落终止文案：Cancel() 会使流循环判定 stale 并 break，OCE 分支不会触发；
                // 用 Interlocked 保证与 OCE 路径只显示一次
                if (Interlocked.Exchange(ref _denialMessageShown, 1) == 0)
                {
                    _dispatcher.Invoke(() => _doc.AppendBlock(
                        new SystemBlock("已拒绝，本轮终止", foreground: BlockColors.Failure)));
                }

                Cancel();
            });
```

- [x] **Step 3: 移除 2 分钟超时（同时修 stale-run 永久挂起前置）**

`ConfirmTool` 的 dispatcher 闭包中，stale-run 分支当前直接 `return` 且从不 `done.Set()`；改为无限等待后会造成真永久挂起，必须先唤醒：

```csharp
        _dispatcher.Invoke(() =>
        {
            // 运行已作废（Esc/新对话）：不弹确认块，避免旧运行干扰新对话；
            // 必须唤醒等待（结果保持 false），否则无限等待会永久挂起
            if (_currentRunSeq != Volatile.Read(ref _activeRunSeq))
            {
                done.Set();
                return;
            }
```

`ConfirmTool` 的等待改为（无限期；Esc/终止经 `ct.Register(() => done.Set())` 提前唤醒）：

```csharp
        // 等待用户选择（无限期；Esc/终止时经取消令牌注册提前唤醒）
        done.Wait();
```

- [x] **Step 4: OCE 收尾文案区分 + 标志复位 + 单次输出守卫**

拒绝钩子调用 `Cancel()` 后会递增 `_activeRunSeq`，CLI 流循环可能因 stale-run 直接 `break`，不一定进入 `OperationCanceledException` catch。因此终止文案由钩子直接输出，并用 `_denialMessageShown` 与 OCE 路径互斥，保证只显示一次。

在 `ProcessInputAsync` 开头复位标志（**不要放在 finally**：`Cancel()` 会递增 `_activeRunSeq`，导致 finally 的活动分支不执行、标志残留污染下一轮）。插入点在 `if (_cancelRequested) _cancelRequested = false;` 之后：

```csharp
        // 新一轮开始：清除上一轮的拒绝终止标志与文案去重标记
        _deniedByUser = false;
        Interlocked.Exchange(ref _denialMessageShown, 0);
```

OCE catch 改为：

```csharp
        catch (OperationCanceledException)
        {
            if (Interlocked.Exchange(ref _denialMessageShown, 1) == 0)
            {
                var message = _deniedByUser ? "已拒绝，本轮终止" : "任务已取消";
                _dispatcher.Invoke(() =>
                    _doc.AppendBlock(new SystemBlock(message, foreground: BlockColors.Failure)));
            }
        }
```

- [x] **Step 5: 被拒工具块标记「已被用户拒绝」（兼容 JsonElement 两形态）**

`ProcessInputAsync` 工具结果分支（`:916-946`）：

1. 先在 `ConfirmTool` 的 `AllowAll` 回调里把 `context.AllowedThisTurn.Add(toolName)` 改为 `context.AllowThisTurn(toolName)`（Task 1 已把属性私有化，否则编译失败）。
2. 工具结果识别改用框架辅助（兼容原始 `ToolResult` 与工厂序列化后的 `JsonElement`），并按本轮标志区分取消：

```csharp
                     if (content is FunctionResultContent functionResult)
                     {
                         // 用户拒绝/取消：不是异常，但也不应显示为"完成"
                         var userCancelled = functionResult.Exception is null
                             && ToolResult.IsUserCancelled(functionResult.Result);
 
                         if (functionResult.Exception is not null)
                         {
                             CloseThinkingBlock();
                             _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
                                 $"❌ 工具执行失败: {functionResult.Exception.Message}",
                                 foreground: BlockColors.Failure)));
                         }
```

3. `ToolCallBlock` 保持普通失败渲染不变，但新增定向状态方法，避免把任意异常文本挤入单行状态块：

```csharp
public void MarkDeniedByUser() => MarkFailedWithStatus("已被用户拒绝");
public void MarkCancelled() => MarkFailedWithStatus("已取消");
```

4. 下方 `MarkFailed/MarkComplete` 判断改为：

```csharp
                            if (target is not null)
                            {
                                if (functionResult.Exception is not null)
                                    target.MarkFailed(functionResult.Exception.Message);
                                else if (userCancelled)
                                {
                                    if (_deniedByUser)
                                        target.MarkDeniedByUser();
                                    else
                                        target.MarkCancelled();
                                }
                                else
                                    target.MarkComplete();
                                _runningToolBlocks.Remove(target);
                            }
```

> `ToolResult` 类型经 `LubanAgentCore.Services`/`LuBan.AIAgent.Abstractions` 是否可见：CLI `GlobalUsings.cs` 已有 `global using LuBan.AIAgent.Abstractions;`，直接用。

5. **同步修正 `Cancel()` 的收尾文案**：拒绝钩子会在工具结果产出前先调用 `Cancel()`，`_runningToolBlocks` 会被提前清空，因此被拒工具块的最终文案由 `Cancel()` 决定。把 `Cancel()` 中的

```csharp
            foreach (var pending in _runningToolBlocks)
                pending.MarkFailed("已取消");
```

改为：

```csharp
            foreach (var pending in _runningToolBlocks)
            {
                if (_deniedByUser)
                    pending.MarkDeniedByUser();
                else
                    pending.MarkCancelled();
            }
```

（编排节点块保持 `"已取消"` 不变——按访谈决策“其余在跑工具/节点按现有 Esc 收尾逻辑处理”。）

- [x] **Step 6: appsettings 去掉节点超时**

`LubanAgentCli/appsettings.json:50` 删除该行：

```jsonc
      "DefaultNodeTimeoutSeconds": 300,
```

（保留其余 Orchestration 配置；`ReflectionTimeoutSeconds` 未显式配置，无需处理。）

- [x] **Step 7: 清理“2 分钟超时”残留注释**

确认已改为无限等待后，以下注释不能再声称固定超时：

- `LubanAgentCli/App/Views/RootView.cs`：`否则会等到超时` → `否则会一直等待用户选择`；`导致 2 分钟超时的问题` → `导致长期挂起的问题`
- `LubanAgentCli/App/Views/ConversationView.cs`：`避免其干等 2 分钟超时` → `避免其一直等待用户选择`
- `LubanAgentCli/App/Views/InputBarView.cs`：`agent 会一直等到超时` → `agent 会一直等待用户选择`

仅改注释，不改逻辑。

- [x] **Step 8: 构建验证**

```powershell
dotnet build luban-agent/LubanAgentCli/LubanAgentCli.csproj -c Debug
```

Expected: 0 警告 0 错误。

---

## Task 5: Codex 接入（无限等待 + 钩子终止 + 卡片状态）

> 行号基准：2026-09-18 重审（含 Codex 附件提交 `49bf4e7` 后的版本）；Task 1–3 的 framework/Core 行号未受影响。若执行前再有改动，先重新定位。

**Files:**
- Modify: `luban-agent/LubanAgentCodex/ViewModels/MainWindowViewModel.cs`
- Modify: `luban-agent/LubanAgentCodex/Views/Controls/ConfirmCard.axaml.cs`
- Modify: `luban-agent/LubanAgentCodex/appsettings.json`

- [x] **Step 1: 标志字段**

`MainWindowViewModel.cs` 字段区（`_activeRunSeq` 之后，`:39`）插入：

```csharp
    // 本轮是否因用户拒绝工具确认而终止：用于取消收尾时区分「已拒绝，本轮终止」与「已取消」
    private volatile bool _deniedByUser;
```

- [x] **Step 2: 装配钩子 + 无限等待**

`ConsumeStreamAsync`（当前 `:892-904`，注意已新增 `attachmentPaths` 第二参）`RunStreamingAsync` 调用在 `onPlannedAction` 闭包之后、`ct: ct))` 之前增加参数：

```csharp
            onPlannedAction: (tool, args) =>
            {
                if (runSeq != _activeRunSeq) return; // 运行序号失效（取消/新对话）则丢弃
                Interlocked.Increment(ref plannedCount);
                var content = $"📋 计划项（Plan 模式，未执行）: {tool} {ToolArgsFormatter.Summarize(args)}";
                Dispatcher.UIThread.Post(() => Messages.Add(new SystemMessageItem { Content = content }));
            },
            onUserDenied: (tool, args) =>
            {
                // 用户明确拒绝：标记并立即终止本轮（框架保证 token 未取消，即非 Esc）
                if (runSeq != _activeRunSeq) return;
                _deniedByUser = true;
                _cts?.Cancel();
            },
```

`ConfirmCallback`（当前 `:1210-1236`）中 `done.Wait(TimeSpan.FromMinutes(2), ct)`（`:1233`）原：

```csharp
        done.Wait(TimeSpan.FromMinutes(2), ct);
```

改为：

```csharp
        // 无限期等待用户响应；Esc/终止时经 ct 注册提前唤醒（ct 可取消时抛 OCE，由 SendAsync 收尾）
        if (ct.CanBeCanceled)
            done.Wait(ct);
        else
            done.Wait();
```

- [x] **Step 3: 取消文案区分 + 标志复位**

`SendAsync` 的 OCE 分支（当前 `:345-348`）原：

```csharp
        catch (OperationCanceledException)
        {
            Messages.Add(new SystemMessageItem { Content = "已取消" });
        }
```

改为：

```csharp
        catch (OperationCanceledException)
        {
            Messages.Add(new SystemMessageItem { Content = _deniedByUser ? "已拒绝，本轮终止" : "已取消" });
        }
```

并在 `SendAsync` 开头复位标志（`_cts = new CancellationTokenSource();` 之前；**不要依赖 finally**：`Cancel()` 会递增 `_activeRunSeq`，虽然 Codex 的 finally 无条件执行，但统一放在开头更稳妥）：

```csharp
        // 新一轮开始：清除上一轮的拒绝终止标志
        _deniedByUser = false;
```
`RunInitAgentsMdAsync` 的 OCE 分支（当前 `:462-465`）保持「已取消」不变。

- [x] **Step 4: 确认卡状态文案**

`ConfirmCard.axaml.cs`：`Respond` 改为按结果更新卡片标题并禁用按钮：

```csharp
    private void Respond(ConfirmResult result)
    {
        _boundItem?.OnRespond(result);

        // 卡片状态落定：显示用户选择，并禁用按钮防止重复点击
        if (_toolNameText != null)
        {
            var status = result switch
            {
                ConfirmResult.Allow => "已允许",
                ConfirmResult.AllowAll => "已允许（本轮全部允许）",
                _ => "已拒绝"
            };
            _toolNameText.Text = $"工具: {_boundItem?.ToolName}（{status}）";
        }

        if (_allowButton != null) _allowButton.IsEnabled = false;
        if (_allowAllButton != null) _allowAllButton.IsEnabled = false;
        if (_denyButton != null) _denyButton.IsEnabled = false;
    }
```

- [x] **Step 5: appsettings 去掉节点超时**

`LubanAgentCodex/appsettings.json:50` 删除 `"DefaultNodeTimeoutSeconds": 300,`。

- [x] **Step 6: 构建验证**

```powershell
dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj -c Debug
```

Expected: 0 `error CS`（若应用/VS 在运行，仅允许 MSB3021/MSB3027 文件锁错误，需先关闭再验证）。

---

## Task 6: 文档与冒烟用例

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/README.md`
- Modify: `luban-framework/LuBan.AIAgent/README.en.md`
- Modify: `luban-agent/LubanAgentCli/docs/TUI-SMOKE-TEST.md`

- [x] **Step 1: 中文 README 增补章节并同步编排示例**

`README.md`：

1. 在 `### 10. 错误处理与自动重试` 之后、`## 支持的 AI Provider` 之前插入：

````markdown
### 11. 工具确认与终止语义

- **拒绝即终止**：用户在工具确认中选择「拒绝」时，宿主立即终止当前对话回合（保留已输出内容）。CLI 与 Codex 均显示「已拒绝，本轮终止」；CLI 被拒工具块显示「已被用户拒绝」，Codex 确认卡显示「已拒绝」、工具卡显示框架中性结果「工具调用被拒绝或已取消」。
- **等待无限期**：工具确认与工作区授权均无限期等待用户响应，无超时兜底；Esc 可随时取消本轮（CLI 显示「任务已取消」，Codex 显示「已取消」）。
- **子代理代确认**：编排子代理不会向用户弹确认；其工具调用在需要人工确认时，由主代码按「本轮已允许集合」代判——命中放行，否则返回子代理专用拒绝结果（不代表用户拒绝）。
- **本轮允许集合**：`ToolConfirmationContext.AllowedThisTurn` 不再公开；宿主使用 `AllowThisTurn(toolName)` 写入、框架使用 `IsAllowedThisTurn(toolName)` 读取。
- **子代理等待无限期**：`Orchestration.DefaultNodeTimeoutSeconds` 与 `ReflectionTimeoutSeconds` 默认 0（无限期）；显式配置大于 0 时才限时，节点仍可用 `TimeoutSeconds` 单独覆盖。
````

2. 将编排配置示例中的 `"DefaultNodeTimeoutSeconds": 300` 和 `"ReflectionTimeoutSeconds": 180` 改为 `0`，避免示例值被误认为默认限时。

- [x] **Step 2: 英文 README 同步**

`README.en.md`：

1. 在 `### 10. Error Handling and Automatic Retry` 之后插入：

````markdown
### 11. Tool Confirmation and Abort Semantics

- **Deny aborts the turn**: when the user chooses "Deny" in a tool confirmation, the host immediately aborts the current turn (partial output is kept). Both CLI and Codex show "已拒绝，本轮终止"; CLI marks the denied tool block "已被用户拒绝", while Codex marks the confirmation card "已拒绝" and the tool card with the framework-neutral result "工具调用被拒绝或已取消".
- **Unbounded waits**: tool confirmations and workspace authorization wait indefinitely for user input; there is no timeout. Esc cancels the turn at any time ("任务已取消" in CLI, "已取消" in Codex).
- **Sub-agent delegated confirmation**: orchestration sub-agents never prompt the user. Tool calls that would need manual confirmation are decided by the main code against the turn's allowed set — allowed when present, otherwise a sub-agent-specific denial (not a user denial).
- **Turn allow-list**: `ToolConfirmationContext.AllowedThisTurn` is no longer public; hosts call `AllowThisTurn(toolName)` and the framework checks `IsAllowedThisTurn(toolName)`.
- **Unbounded sub-agent waits**: `Orchestration.DefaultNodeTimeoutSeconds` and `ReflectionTimeoutSeconds` default to 0 (unbounded); set them above 0 to enforce limits, and `TimeoutSeconds` can still override per node.
````

2. Change the orchestration example's `"DefaultNodeTimeoutSeconds": 300` and `"ReflectionTimeoutSeconds": 180` to `0`.

- [x] **Step 3: 冒烟用例（避免与现有附件用例 28–49 重号）**

`TUI-SMOKE-TEST.md`：在 `## 预期结果` 之前追加，并从 **50** 开始编号：

```markdown
## 工具确认：拒绝与等待

### CLI

- [ ] 50. 让 Agent 执行需确认的操作（如工作区外写文件/RunShell），在确认块上选择「拒绝」— 聊天区显示「已拒绝，本轮终止」，模型不再继续输出，输入框可继续输入；被拒工具块显示「✗ 已被用户拒绝」
- [ ] 51. 同上，但在确认块上等待超过 2 分钟再点「允许」— 不再有 2 分钟超时，等待期间不会自动拒绝
- [ ] 52. 同上，在确认块焦点上按 Esc — 显示「已拒绝，本轮终止」（确认块上的 Esc 等价拒绝；非确认焦点的全局 Esc 仍为「任务已取消」）
- [ ] 53. 发起复合任务触发编排（子代理执行敏感操作）— 不再弹出子代理的确认；若该工具不在本轮已允许集合内，子代理收到「需用户授权、已自动阻止」的拒绝并自行调整

### Codex

- [ ] 54. 触发需确认的工具并点「拒绝」— 确认卡显示「工具: {工具名}（已拒绝）」状态，聊天区显示「已拒绝，本轮终止」，工具卡进入失败态
- [ ] 55. 触发需确认的工具，等待超过 2 分钟后点「允许」— 不再自动超时拒绝
- [ ] 56. 点「本轮全部允许」— 卡片显示「已允许（本轮全部允许）」，本轮同名工具不再重复确认
```

- [x] **Step 4: 构建 + 全部单测**

```powershell
dotnet build luban-agent/LubanAgent.slnx -c Debug
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ToolConfirmation"
```

Expected: 0 警告 0 错误；16 个测试通过。

---

## Task 7: 全量验证

- [x] **Step 1: 全量构建**

```powershell
dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj -c Debug
dotnet build luban-agent/LubanAgent.slnx -c Debug
```

Expected: 均 0 警告 0 错误（先关闭运行中的 Codex/VS 避免文件锁）。

- [x] **Step 2: 单测子集回归**

```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ToolConfirmation"
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentApiError"
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~FileSystemToolPath"
```

Expected: 16 / 39 / 12 全部通过。

- [x] **Step 3: 手动冒烟**

按 `TUI-SMOKE-TEST.md` 用例 50–56 执行；Codex 侧手测：拒绝后出现「已拒绝，本轮终止」、确认卡显示「已拒绝」状态、无 2 分钟超时。

- [ ] **Step 4: 发版与提交**

- 发版由用户手工执行（递增 `LuBan.AIAgent` 版本 → `dotnet pack` → 发布 → 之后再回退 `LubanAgentCore.csproj` 的 `ProjectReference`）。
- 提交仍需用户明确许可；建议 framework / agent 分仓提交，提交信息用中文（agent 带 conventional 前缀）。

---

## 回滚方式

- 代码回滚：还原上述文件；删除 `ToolConfirmationUnitTest.cs`；删除 README 第 11 节与冒烟用例 50–56。
- 行为回滚：把 `OrchestrationOptions` 两字段默认改回 300/180 并恢复 appsettings 行；`OnUserDenied` 置空即不终止；`DagScheduler` 去掉 `EnterSubAgentScope()` 即恢复弹窗。
- 兼容性：`AllowedThisTurn` 由公开 HashSet 改为 `IsAllowedThisTurn/AllowThisTurn` 方法，属 API 破坏性变更（随框架发版生效，README 已注明）。

## 已知限制

1. 子代理代确认只认“本轮已允许集合”；未允许的工具在子代理内一律被阻止（保守策略，按访谈决策）。
2. 节点/反思默认无限期后，异常挂起不再自动收口（依赖 LLM 层 60s 超时 + 重试兜底；纯逻辑死循环会永挂）。
3. CLI 确认块上的 Esc 与点「拒绝」语义相同（都会终止本轮）；仅全局 Esc（非确认焦点）保持「任务已取消」。
4. 用户拒绝后的本轮不入库（与既有“失败轮不入库”一致）。
