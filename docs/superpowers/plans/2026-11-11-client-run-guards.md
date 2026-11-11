# CLI / Codex 会话运行态守卫实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: 用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans` 逐任务执行。任务用 `- [ ]` 复选框跟踪。

**Goal：** 两个本机客户端（CLI / Codex）在「会话正在跑一轮对话」时，不允许把这一轮的消息流或会话数据清掉——清空、删除一律拒绝并给出明确提示，退出确认按运行态说明会中断回合。

**Architecture：** 不改轮次架构（两个客户端本来就是进程内直连 `IAgentHost`，切会话不取消、运行中不被 LRU 驱逐，已具备「后台跑完、切回可见」）。只补三类守卫：Codex 的 `SessionViewModel.TryClear()`（运行中返回 false）、`MainWindowViewModel` 的清空/删除入口按运行态拒绝、CLI 新增进程级 `SessionRunRegistry` 供 `/session -clear` 判断。

**Tech Stack：** .NET 10 / C# 13 / MSTest / Terminal.Gui（CLI）/ Avalonia + CommunityToolkit.Mvvm（Codex）

**上游设计依据：** `luban-agent/docs/superpowers/specs/2026-11-11-web-run-resume-design.md` §6.7（`158`–`162` 行）：删除进行中的会话拒绝、清空不销毁会话时避免「已清空又长出一条回答」。本计划把同一条判定下沉到本机客户端；与设计唯一差异是**清空类也采用拒绝**（设计里 Web 的清空是删库，走「先取消再清空」；CLI/Codex 的运行是进程内直连，取消后仍需等收尾，跨层等待成本高，且拒绝不会丢数据）。

**本计划位置：** `luban-agent/docs/superpowers/plans/2026-11-11-client-run-guards.md`

**范围：** `LubanAgentCodex`、`LubanAgentCli`、`LubanAgent.Tests`（仅新增 Codex 单测）。不改 `LubanAgentCore`、`LubanAgentWebHost`、前端，也不改 Web 端已落地的轮次中心。

---

## 0. 执行前必读

1. **根目录 `luban/` 不是 git 仓库。** 所有 git / build / test 命令都必须进 `cd D:\WorkBench\Walle\luban\luban-agent`。
2. **工作区已有用户 WIP，不得纳入本次改动**：`LubanAgentCore/Configuration/ConfigManager.cs`、`LubanAgentCore/Configuration/ProviderService.cs`、`LubanAgentCore/Protocol/AgentApiJson.cs`、`LubanAgentWebHost/Controllers/ProviderController.cs`、`LubanAgentWebHost/Models/ProviderContracts.cs`、`LubanAgentWebHost/Services/WebProviderService.cs`、`LubanAgentWebHost/web/src/pages/Settings.tsx`、`LubanAgentWebHost/web/src/pages/settings/ProviderTab.tsx`、`LubanAgentWebHost/wwwroot/index.html`、`LubanAgentWebHost/wwwroot/assets/index-*`、`LubanAgent.Tests/ConfigManagerProviderRenameUnitTest.cs`。**不要 `git add -A` / `git add .`**。
3. **本计划不产生提交动作。** 每个任务末尾是「检查点」，只允许核对改动范围；提交时机与范围由用户统一决定。禁止 `git push`、`reset --hard`、`clean`、改写已有提交。
4. **构建 / 测试一律在 `luban-agent/` 下执行**：`dotnet build LubanAgent.slnx -v m`、`dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj -v m`。
5. **每完成一个任务**，把该任务的 `- [ ]` 改成 `- [x]`，并在「验证记录」行写实际命令与结果（通过 / 失败原因、退出码）。这是判断基线是否可信的唯一依据。
6. **代码注释不写溯源标记**：不写「见设计 §6.x」，规则直接写完整。

---

## 1. 现状审计（执行前先确认，勿重复施工）

| 入口 | 位置 | 现状 | 本次处置 |
|---|---|---|---|
| CLI 会话切换 | `LubanAgentCli/App/Views/RootView.cs:370` `OnCurrentSessionChanged` | 不取消后台会话，VM 常驻 | 不改 |
| CLI LRU 驱逐 | `LubanAgentCli/App/Views/RootView.cs:357` `CanEvict` | 已排除 `slot.Vm.IsRunning` | 不改 |
| CLI 退出确认 | `LubanAgentCli/App/Views/RootView.cs:689-691` | 已提示「运行中退出将中断当前任务」 | 不改 |
| CLI `/session -clear` | `LubanAgentCli/Commands/SessionCommand.cs:266` | 物理删除全部会话，**无运行态判断** | Task 4–5 补守卫 |
| CLI `/clear` | `LubanAgentCli/App/ViewModels/CommandViewModel.cs:159` | 只清显示文档，不删库 | 不改（清屏后输出继续出现属正常） |
| Codex 切会话重建历史 | `LubanAgentCodex/ViewModels/MainWindowViewModel.cs:1315-1316` | 已 `if (!isNew && session.IsRunning) return;` | 不改 |
| Codex LRU 驱逐 | `LubanAgentCodex/ViewModels/MainWindowViewModel.cs:513-516` | 已跳过当前与 `IsRunning` | 不改 |
| Codex `ClearMessages()` | `LubanAgentCodex/ViewModels/MainWindowViewModel.cs:1405` | 运行中直接 `Messages.Clear()`，`_currentAssistant` 变孤儿，**整轮丢失** | Task 1–2 补守卫 |
| Codex `/session -clear` | `LubanAgentCodex/ViewModels/MainWindowViewModel.cs:1254-1258` | 先删库再清内存，**无运行态判断** | Task 2 补守卫 |
| Codex 退出确认 | `LubanAgentCodex/Views/MainWindow.axaml.cs:276-285` | 未提示运行中的回合会被中断 | Task 3 补文案 |

---

## 2. 落定的口径与文案

| 场景 | 处置 | 文案 |
|---|---|---|
| Codex 清空当前会话（`/clear`、`ClearMessages()`、`/session -clear`） | 当前会话 `IsRunning` 时**拒绝**，不清内存、不删库 | `该会话正在进行中，请先停止再清空`（`/session -clear` 用 `…再删除`） |
| CLI `/session -clear`（物理删除全部会话） | **任一**会话运行中即拒绝，不执行删除 | `有会话正在进行中，请先停止再删除` |
| Codex 退出 | 任一会话运行中时，确认框写明会中断 | `正在运行的回合会被中断，未发送的内容不会保存。` |
| CLI 退出 | 已有提示 | 不改 |

---

## Task 1：Codex `SessionViewModel.TryClear()` + 单测

**Files：**
- Modify: `luban-agent/LubanAgentCodex/ViewModels/SessionViewModel.cs`
- Test: `luban-agent/LubanAgent.Tests/CodexSessionClearGuardUnitTest.cs`（新建）

**为什么：** `Clear()` 是唯一入口，但调用方 `MainWindowViewModel.ClearMessages()` 拿不到「是否清成功」。先把它改成带返回值的 `TryClear()`，再用单测锁死「运行中不清理」这条判定——这条判定靠 UI 手点很难复现（要先让会话处于运行中再去点清空）。

- [x] **Step 1：先写失败测试**

新建 `LubanAgent.Tests/CodexSessionClearGuardUnitTest.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： CodexSessionClearGuardUnitTest
*创建时间：2026/11/11
*描述：Codex 会话清空守卫单测——运行中清空会让正在增长的助手消息变成孤儿（界面看不到、内容丢失），
*      必须拒绝。
*
*****************************************************************************/

using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Hosting;
using LuBan.DI;

using LubanAgentCodex.ViewModels;
using LubanAgentCodex.ViewModels.Messages;

namespace LubanAgent.Tests;

[TestClass]
public class CodexSessionClearGuardUnitTest
{
    /// <summary>只用于构造会话 ViewModel 的宿主桩：清空不触达对话能力。</summary>
    private sealed class FakeAgentHost : IAgentHost
    {
        public bool IsInitialized => false;

        public DiContainer Services => null!;

        public string? CurrentSessionId => null;

        public Task InitializeAsync(string sessionId, CancellationToken ct = default, AgentBuildOverrides? overrides = null)
            => Task.CompletedTask;

        public void Reset(string? sessionId = null)
        {
        }

        public IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(AgentRunRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static SessionViewModel NewSession()
        => new(
            "session-clear-guard",
            new FakeAgentHost(),
            null,
            () => ToolPermissionMode.Default,
            _ => false);

    [TestMethod]
    public void 运行中清空被拒绝且消息保持原样()
    {
        var session = NewSession();
        session.Messages.Add(new UserMessageItem { Content = "hello" });
        session.IsRunning = true;

        Assert.IsFalse(session.TryClear());
        Assert.AreEqual(1, session.Messages.Count);
    }

    [TestMethod]
    public void 空闲时清空成功()
    {
        var session = NewSession();
        session.Messages.Add(new UserMessageItem { Content = "hello" });

        Assert.IsTrue(session.TryClear());
        Assert.AreEqual(0, session.Messages.Count);
    }
}
```

- [x] **Step 2：跑一次，确认失败**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj --filter "FullyQualifiedName~CodexSessionClearGuardUnitTest"
```

期望：编译失败，`CS1061: "SessionViewModel"未包含"TryClear"的定义`。

- [x] **Step 3：实现 `TryClear()`**

把 `LubanAgentCodex/ViewModels/SessionViewModel.cs` 里的 `Clear()` 整段替换为：

```csharp
    /// <summary>
    /// 清空消息流与运行中节点。
    /// </summary>
    /// <remarks>
    /// 会话正在运行时拒绝：本轮消息仍在增长，清空会让当前助手消息脱离集合（界面看不到、内容丢失），
    /// 收尾时也无处回填。需要清空请先停止本轮。
    /// </remarks>
    /// <returns>true 表示已清空；false 表示会话正在运行，未做任何清理。</returns>
    public bool TryClear()
    {
        if (IsRunning) return false;

        Messages.Clear();
        _runningNodes.Clear();
        return true;
    }
```

- [x] **Step 4：跑测试，确认通过**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj --filter "FullyQualifiedName~CodexSessionClearGuardUnitTest"
```

期望：`失败: 0，通过: 2`。

> 此时 `MainWindowViewModel.cs:1407` 的 `_currentSession?.Clear()` 会编译失败（方法已改名），由 Task 2 立即修复；不要单独提交成半截状态。

- [x] **Step 5：检查点**

确认只改了上面两个文件；不执行任何 git 命令。

**验证记录：** ✅ 2026-11-11 `dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj --filter "FullyQualifiedName~CodexSessionClearGuardUnitTest"`：先 RED（CS1061 "SessionViewModel"未包含"TryClear"的定义，退出码 1），实现后 GREEN（失败 0，通过 2，总计 2，退出码 0）。实施顺序偏差：Task 1 Step 4 的测试要等 MainWindowViewModel.ClearMessages() 改完才能编译，实际先落 Task 2 Step 1-4 再跑 Task 1 Step 4。

---

## Task 2：Codex 清空 / 删除入口按运行态拒绝

**Files：**
- Modify: `luban-agent/LubanAgentCodex/ViewModels/MainWindowViewModel.cs`

**为什么：** `ClearMessages()` 是 `/clear` 与 `/session -clear` 的共同入口；把返回值改成「是否真的清了」，两个命令才能给出正确提示，不会出现「已被拒绝还提示会话已清空」。

- [x] **Step 1：新增 `IsAnySessionRunning`**

在 `MainWindowViewModel.cs:232` 的 `public bool IsRunning => _currentSession?.IsRunning ?? false;` 之后插入：

```csharp
    /// <summary>
    /// 是否有任一会话正在运行；退出确认据此提示会中断回合。
    /// </summary>
    public bool IsAnySessionRunning => _sessions.Values.Any(session => session.IsRunning);
```

- [x] **Step 2：`ClearMessages()` 改为返回是否清成功**

把 `MainWindowViewModel.cs:1402-1409` 整段替换为：

```csharp
    /// <summary>
    /// 清空当前会话消息流。
    /// </summary>
    /// <returns>true 表示已清空（无当前会话时视为无需清理）；false 表示当前会话正在运行，已拒绝。</returns>
    public bool ClearMessages()
    {
        var session = _currentSession;
        if (session is null)
        {
            ClearPendingAttachments();
            return true;
        }

        if (!session.TryClear()) return false;

        ClearPendingAttachments();
        return true;
    }
```

- [x] **Step 3：`/clear` 分支按结果给提示**

把 `MainWindowViewModel.cs:625-628` 替换为：

```csharp
            case "/clear":
                if (ClearMessages())
                {
                    Messages.Add(new SystemMessageItem { Content = "会话已清空" });
                }
                else
                {
                    Messages.Add(new SystemMessageItem
                    {
                        Content = "该会话正在进行中，请先停止再清空",
                        IsError = true
                    });
                }
                break;
```

- [x] **Step 4：`/session -clear` 分支加运行态守卫**

把 `MainWindowViewModel.cs:1254-1259` 替换为：

```csharp
        else if (args[0] == "-clear" || args[0] == "clear")
        {
            // 运行中先拒绝：删库之后本轮仍会继续写，内存里的活动消息也会变成孤儿
            if (_currentSession?.IsRunning == true)
            {
                Messages.Add(new SystemMessageItem
                {
                    Content = "该会话正在进行中，请先停止再删除",
                    IsError = true
                });
                return;
            }

            sessionManager.ClearCurrentSession();
            ClearMessages();
            Messages.Add(new SystemMessageItem { Content = "会话已清空" });
        }
```

- [x] **Step 5：构建**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgent.slnx -v m
```

期望：0 error。若 `MainWindowViewModel.cs` 还有别处调用 `Clear()`，按编译错误逐个改为 `TryClear()`（当前唯一调用点是 `:1407`，已在上一步替换）。

- [x] **Step 6：检查点**

确认只改了 `MainWindowViewModel.cs`；不执行任何 git 命令。

**验证记录：** ✅ 2026-11-11 `dotnet build LubanAgent.slnx -v m` → 0 个错误、16 个警告（均为既有 NU1902/NU1903 与 Avalonia AVLN3001），退出码 0。

---

## Task 3：Codex 退出确认按运行态补提示

**Files：**
- Modify: `luban-agent/LubanAgentCodex/Views/MainWindow.axaml.cs`

**为什么：** 退出会中断正在跑的回合，但当前文案只说「未发送的内容不会保存」，用户不会意识到这一轮白跑了。

- [x] **Step 1：改写 `ConfirmExitAsync()`**

把 `MainWindow.axaml.cs:276-285` 替换为：

```csharp
    private async Task<bool> ConfirmExitAsync()
    {
        var running = _viewModel?.IsAnySessionRunning == true;
        return await Dialogs.ShowConfirmAsync(
            this,
            "退出确认",
            "确定要退出 Luban Agent 吗？",
            running
                ? "正在运行的回合会被中断，未发送的内容不会保存。"
                : "退出后当前会话将关闭，未发送的内容不会保存。",
            "退出",
            danger: true);
    }
```

- [x] **Step 2：构建**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgent.slnx -v m
```

期望：0 error。

- [x] **Step 3：检查点**

确认只改了 `MainWindow.axaml.cs`；不执行任何 git 命令。

**验证记录：** ✅ 2026-11-11 `dotnet build LubanAgent.slnx -v m` → 0 个错误、20 个警告，退出码 0。该构建在 Task 3/4/5 全部落盘后执行，未单列 Task 3 的独立构建。

---

## Task 4：CLI 新增 `SessionRunRegistry` 并在会话 VM 中登记

**Files：**
- Create: `luban-agent/LubanAgentCli/App/Models/SessionRunRegistry.cs`
- Modify: `luban-agent/LubanAgentCli/App/StartupRunner.cs`
- Modify: `luban-agent/LubanAgentCli/App/ViewModels/ConversationViewModel.cs`

**为什么：** `/session -clear` 在 `LubanAgentCli/Commands/SessionCommand.cs` 里执行，拿不到 `RootView` 的会话槽位（槽位是私有字段）。需要一个进程级只读登记表，让命令层能问「现在有会话在跑吗」。

- [x] **Step 1：新建 `SessionRunRegistry.cs`**

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCli.App.Models
*文件名： SessionRunRegistry
*描述：进程级会话运行态登记；供 /session -clear 等全局命令判断是否有会话正在进行中
*
*****************************************************************************/

namespace LubanAgentCli.App.Models;

/// <summary>
/// 进程级会话运行态登记：记录哪些会话正在跑一轮对话。
/// </summary>
/// <remarks>
/// 只登记运行态，不持有轮次本身；会话的 <c>ConversationViewModel</c> 在进入/离开运行态时同步这里。
/// 全局命令据此拒绝「删掉正在跑的会话」这类操作。
/// </remarks>
public sealed class SessionRunRegistry
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.Ordinal);

    /// <summary>标记会话进入运行态（重复登记无副作用）。</summary>
    /// <param name="sessionId">会话标识。</param>
    public void Enter(string sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId))
        {
            _running[sessionId] = 0;
        }
    }

    /// <summary>标记会话离开运行态（未登记时无副作用）。</summary>
    /// <param name="sessionId">会话标识。</param>
    public void Exit(string sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId))
        {
            _running.TryRemove(sessionId, out _);
        }
    }

    /// <summary>是否有任一会话正在运行。</summary>
    public bool IsAnyRunning => !_running.IsEmpty;
}
```

- [x] **Step 2：注册到宿主容器**

在 `StartupRunner.cs:65-66` 的 `TitleService` 注册之后插入：

```csharp
                // 会话运行态登记：供 /session -clear 判断是否有会话正在进行中
                services.AddSingleton<SessionRunRegistry>();
```

并在文件顶部补 using：

```csharp
using LubanAgentCli.App.Models;
```

> `LubanAgentCli.App.Models` 已在 `LubanAgentCli/GlobalUsings.cs:31` 全局引入，实际不需要新增任何 using；此处仅作说明。

- [x] **Step 3：`ConversationViewModel` 在运行态变化时登记**

先加字段（放在 `LubanAgentCli/App/ViewModels/ConversationViewModel.cs` 的其它只读字段旁）：

```csharp
    private readonly SessionRunRegistry? _runRegistry;
```

在构造函数末尾（`_configManager.SelectedModelChanged += OnSelectedModelChanged;` 之后）加：

```csharp
        _runRegistry = services.GetService<SessionRunRegistry>();
```

把 `ConversationViewModel.cs:131-132` 的：

```csharp
    /// <summary>当前是否正在运行 agent 对话。</summary>
    public bool IsRunning { get; private set; }
```

替换为：

```csharp
    private bool _isRunning;

    /// <summary>当前是否正在运行 agent 对话。</summary>
    /// <remarks>运行态同步登记到 <see cref="SessionRunRegistry"/>，供 /session -clear 等全局命令判断。</remarks>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            if (value)
            {
                _runRegistry?.Enter(_sessionId);
            }
            else
            {
                _runRegistry?.Exit(_sessionId);
            }
        }
    }
```

在 `Dispose()`（`:1123` 起）的开头补一行，避免槽位销毁后仍被登记为运行中：

```csharp
        _runRegistry?.Exit(_sessionId);
```

> `Dispose` 里已有 `_currentCts?.Cancel()`；本次只加登记清理，不改取消逻辑。

- [x] **Step 4：构建**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgent.slnx -v m
```

期望：0 error。

- [x] **Step 5：检查点**

确认只改了上述三个文件；不执行任何 git 命令。

**验证记录：** ✅ 2026-11-11 `dotnet build LubanAgent.slnx -v m` → 0 个错误、20 个警告，退出码 0（与 Task 3/5 合并验证）。

---

## Task 5：CLI `/session -clear` 加运行态守卫

**Files：**
- Modify: `luban-agent/LubanAgentCli/Commands/SessionCommand.cs`
- Modify: `luban-agent/LubanAgentCli/App/ViewModels/CommandViewModel.cs`

**为什么：** 这是 CLI 唯一「物理删除全部会话」的入口；运行中删除会让本轮继续往已删除的会话里写，事后无法回填。

- [x] **Step 1：`SessionCommand` 注入登记表**

在 `SessionCommand.cs` 的字段区加：

```csharp
    private readonly SessionRunRegistry _runRegistry;
```

构造函数签名改为（在 `IWorkspaceManagementService workspaces` 与 `ITuiOutputWriter writer` 之间插入）：

```csharp
    public SessionCommand(ConfigManager configManager, IConfiguration configuration, IChatSessionService sessions, ISessionManager sessionManager, ISessionMessageQuery messageQuery,
        IWorkspaceManagementService workspaces, SessionRunRegistry runRegistry, ITuiOutputWriter writer, ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
        _sessions = sessions;
        _sessionManager = sessionManager;
        _messageQuery = messageQuery;
        _workspaces = workspaces;
        _runRegistry = runRegistry;
    }
```

同步更新构造函数的 XML 注释，加一条：

```csharp
    /// <param name="runRegistry">会话运行态登记（判断是否有会话正在进行中）。</param>
```

> `LubanAgentCli.App.Models` 已在 `LubanAgentCli/GlobalUsings.cs:31` 全局引入，`SessionCommand.cs` 不需要新增 using。

- [x] **Step 2：`ClearAllSessionsAsync` 加守卫**

把 `SessionCommand.cs:266-276` 替换为：

```csharp
    private async Task ClearAllSessionsAsync()
    {
        // 运行中先拒绝：删库之后本轮仍会继续写；一旦删掉再恢复，这一轮内容无法回填
        if (_runRegistry.IsAnyRunning)
        {
            Writer.WriteError("有会话正在进行中，请先停止再删除");
            return;
        }

        if (!Ui.Confirm("删除全部会话", "确认物理删除全部会话及消息数据？此操作不可恢复", defaultValue: false))
        {
            Writer.WriteInfo("已取消");
            return;
        }

        await _sessions.ClearAllAsync((await _workspaces.GetCurrentAsync())?.WorkspaceId);
        Writer.WriteSuccess("已删除全部会话数据");
    }
```

- [x] **Step 3：`CommandViewModel` 解析时传入登记表**

把 `CommandViewModel.cs:249-253` 的 `SessionCommand` 分支替换为：

```csharp
            nameof(SessionCommand) => (TCommand)(object)new SessionCommand(configManager, configuration,
                _services.GetRequiredService<IChatSessionService>(),
                _services.GetRequiredService<ISessionManager>(),
                _services.GetRequiredService<ISessionMessageQuery>(),
                _services.GetRequiredService<IWorkspaceManagementService>(),
                _services.GetRequiredService<SessionRunRegistry>(), _writer, _ui),
```

> `LubanAgentCli.App.Models` 已在 `LubanAgentCli/GlobalUsings.cs:31` 全局引入，`CommandViewModel.cs` 不需要新增 using。

- [x] **Step 4：构建**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgent.slnx -v m
```

期望：0 error。

- [x] **Step 5：检查点**

确认只改了上述两个文件；不执行任何 git 命令。

**验证记录：** ✅ 2026-11-11 `dotnet build LubanAgent.slnx -v m` → 0 个错误、20 个警告，退出码 0。new SessionCommand( 全仓仅 CommandViewModel.cs:249 一处调用，已同步。

---

## Task 6：全量验收

- [x] **Step 1：后端构建 + 测试**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgent.slnx -v m
dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj -v m
```

期望：构建 0 error；测试全绿（含 Task 1 的 2 条）。

- [x] **Step 2：变更清单复核**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
git status --short
```

期望：本次只动以下文件——`LubanAgentCodex/ViewModels/SessionViewModel.cs`、`LubanAgentCodex/ViewModels/MainWindowViewModel.cs`、`LubanAgentCodex/Views/MainWindow.axaml.cs`、`LubanAgentCodex/Views/Controls/Sidebar.axaml.cs`、`LubanAgentCodex/Views/WorkManageWindow.axaml.cs`、`LubanAgentCodex/Views/LlmWikiManageWindow.axaml.cs`、`LubanAgentCli/App/Models/SessionRunRegistry.cs`（新）、`LubanAgentCli/App/StartupRunner.cs`、`LubanAgentCli/App/ViewModels/ConversationViewModel.cs`、`LubanAgentCli/App/ViewModels/CommandViewModel.cs`、`LubanAgentCli/Commands/SessionCommand.cs`、`LubanAgentCli/Commands/WorkCommand.cs`、`LubanAgentCli/Commands/LlmWikiCommand.cs`、`LubanAgent.Tests/CodexSessionClearGuardUnitTest.cs`（新）、本计划文档。用户 WIP 文件保持原状；Web 端既有改动（`LubanAgentCore/Hosting/*`、`LubanAgentWebHost/*`、`README*.md` 等）不属于本次新增改动，但会出现在同一份 `git status` 里，不得误认为本计划产物。

- [ ] **Step 3：手动冒烟（逐条记结果）**

| # | 客户端 | 操作 | 期望 |
|---|---|---|---|
| 1 | Codex | 发起长回答 → 运行中点「清空」/输入 `/clear` | 提示「该会话正在进行中，请先停止再清空」，消息流不变，回答继续 |
| 2 | Codex | 运行中输入 `/session -clear` | 提示「该会话正在进行中，请先停止再删除」，库与界面都不变 |
| 3 | Codex | 停止后再 `/clear`、`/session -clear` | 正常清空 |
| 4 | Codex | 一个会话在跑、切到另一个空闲会话点清空 | 空闲会话正常清空，运行中的不受影响 |
| 5 | Codex | 运行中关闭窗口 | 确认框写明「正在运行的回合会被中断」；取消后回合继续 |
| 6 | CLI | 发起长回答 → 运行中执行 `/session -clear` | 输出「有会话正在进行中，请先停止再删除」，会话未删，回答继续 |
| 7 | CLI | 停止后执行 `/session -clear` | 确认后删除成功 |
| 8 | CLI | 会话 A 在跑、切到会话 B 执行 `/session -clear` | 同样被拒绝（判定是「任一运行中」，不限于当前会话） |
| 9 | CLI | 运行中执行 `/clear` | 清屏后流式输出继续出现（既有行为，本次不改） |
| 10 | Codex | 某工作区下有会话在跑 → 侧栏点该工作区的删除按钮 | 提示「该工作区下有正在进行中的会话，请先停止再删除」，工作区与会话都不变、不弹确认框 |
| 11 | Codex | 同上，分别走「工作区管理窗口」删除与「LLM Wiki 管理窗口」删除该知识库 | 同样被拒，文案同上 |
| 12 | CLI | 某工作区下有会话在跑 → `/work -delete <工作区名>` | 输出「该工作区下有正在进行中的会话，请先停止再删除」，工作区未删、不弹确认 |
| 13 | CLI | 某个 LLM Wiki 知识库下有会话在跑 → `/llmwiki -delete <名称>` | 同上文案，知识库未删、不弹确认 |

- [ ] **Step 4：把每条冒烟结果写进本任务的验证记录**

**验证记录：** ✅ 2026-11-11 `dotnet build LubanAgent.slnx -v m` → 0 个错误；`dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj -v m` → 失败 0，通过 454，总计 454，退出码 0（含本次新增 2 条）。`git status --short` 复核：本次仅新增/改动 LubanAgentCli/App/StartupRunner.cs、LubanAgentCli/App/ViewModels/CommandViewModel.cs、LubanAgentCli/App/ViewModels/ConversationViewModel.cs、LubanAgentCli/Commands/SessionCommand.cs、LubanAgentCli/Commands/WorkCommand.cs、LubanAgentCli/Commands/LlmWikiCommand.cs、LubanAgentCli/App/Models/SessionRunRegistry.cs(新)、LubanAgentCodex/ViewModels/MainWindowViewModel.cs、LubanAgentCodex/ViewModels/SessionViewModel.cs、LubanAgentCodex/Views/MainWindow.axaml.cs、LubanAgentCodex/Views/Controls/Sidebar.axaml.cs、LubanAgentCodex/Views/WorkManageWindow.axaml.cs、LubanAgentCodex/Views/LlmWikiManageWindow.axaml.cs、LubanAgent.Tests/CodexSessionClearGuardUnitTest.cs(新) 与本计划文档。B2 补齐后重新构建：`dotnet build LubanAgent.slnx -v m` → 0 个错误、16 个警告（含两条既有的 Avalonia `AVLN3001`）；`dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj -v m` → 失败 0，通过 456，总计 456，退出码 0（终审后补了 2 条 hub 用例，故由 454 增至 456）。⛔ Step 3 的 13 条手动冒烟未执行：CLI / Codex 需要人工启动 TUI / 桌面端并接真实模型，本会话无法完成。

---

## 附录 A：与 Web 端口径的差异（执行时不要「顺手统一」）

| 点 | Web | CLI / Codex | 原因 |
|---|---|---|---|
| 清空消息 | 先取消再清空（删库） | 运行中直接拒绝 | 本机是进程内直连，取消后仍需等收尾；拒绝不会丢数据 |
| 删除会话 | `409` + 提示 | 提示后不执行 | 没有 HTTP 状态码，用输出/提示表达同一判定 |
| 轮次归属 | hub 按会话持有，可 attach | VM 常驻，切会话不取消 | 一期不动本机轮次架构 |

---

## 附录 B：施工补记（计划外发现与裁决）

**B1（已修）Codex 侧栏「删除会话」漏审。** 现状审计只列了 `ClearMessages()` 与 `/session -clear`，漏掉了 `LubanAgentCodex/Views/Controls/Sidebar.axaml.cs` 会话右键菜单的「删除会话」——它直接 `_sessionService.DeleteAsync(sessionId)`，运行中会被删库，与本计划「禁止删除进行中的会话」正面冲突。已补守卫：删除前查 `SessionRunningProvider`（`MainWindow.axaml.cs:188` 注入的 `_viewModel.IsSessionRunning`），运行中弹 `Dialogs.ShowErrorAsync(...,"该会话正在进行中，请先停止再删除")` 并直接返回，不弹确认框、不删库。裁决理由：属用户「删除会话要判断是否进行中」的直接落点，不是扩面。风险：若 `SessionRunningProvider` 为 null（侧栏未接线）则放行，与改造前行为一致。

**B2（已修，用户裁定「要补」）工作区级联删除补运行态守卫。** 覆盖原审计列出的 5 个入口，全部是「逻辑删工作区 + 级联清会话」，运行中会连带删掉进行中的会话：

- 判定源：`LubanAgentCodex/ViewModels/MainWindowViewModel.cs:244` 新增 `HasRunningSessionInWorkspaceAsync(workspaceId)` —— 取 `_sessions` 中 `IsRunning` 的会话 ID，逐个经 `ISessionWorkspaceQuery.GetSessionWorkspaceIdAsync` 比对目标工作区；`ISessionWorkspaceQuery` 取不到（或 `_sessions` 无运行中会话）时返回 `false`，与改造前行为一致。
- 注入：`LubanAgentCodex/Views/MainWindow.axaml.cs:190` 把该委托注入侧栏（`Sidebar.axaml.cs:47` `WorkspaceRunningProvider`）；`MainWindowViewModel.cs:819` / `:828` 在创建 `WorkManageWindow` / `LlmWikiManageWindow` 时作为构造参数传入。
- 守卫点：`Sidebar.axaml.cs:379`（工作区行删除按钮）、`Sidebar.axaml.cs:906`（删除知识库）、`WorkManageWindow.axaml.cs:121`（工作区管理窗口删除）、`LlmWikiManageWindow.axaml.cs:439`（知识库管理窗口删除）、`LubanAgentCli/Commands/WorkCommand.cs:356`（`/work -delete`）、`LubanAgentCli/Commands/LlmWikiCommand.cs:483`（`/llmwiki -delete`，后续补齐：与 `/work -delete` 同为「级联删会话 + 删工作区」，漏掉即留后门）。
- 判定复用：`LubanAgentCli/App/Models/SessionRunRegistry.cs:53` 新增 `IsWorkspaceRunningAsync(query, workspaceId, userId)`，内部用 `ISessionWorkspaceQuery.GetByWorkspaceAsync` 与登记表求交。
- 文案统一为「该工作区下有正在进行中的会话，请先停止再删除」；拒绝发生在确认框之前，不弹确认、不删库。CLI 走 `Writer.WriteError`，Codex 走 `Dialogs.ShowErrorAsync`。
- 风险：任一处 provider 为 `null`（未接线）时放行，与改造前一致。新增的构造参数均带默认值 `null`，`WorkManageWindow.axaml.cs` / `LlmWikiManageWindow.axaml.cs` 因此多出 Avalonia `AVLN3001`（XAML 运行期加载需无参构造）警告——这两个窗口原本就只有一个带参构造，警告为既有性质，非本次引入。

**B3（终审：已由独立子代理完成）** 首次尝试派独立审查子代理因会话额度不足（5 小时用量上限）失败，当时记为作者自审。后续额度恢复后已实跑独立只读终审，结论 **有条件合并、无 Critical**，2 条 Important 均落在 Web 端续流状态机（与本计划 B 部分无关），修复与逐条处理见 `2026-11-11-web-run-resume.md` 附录 C。

针对本计划（CLI / Codex）的终审意见只有一条口径争议：**CLI 的 `/clear` 运行中不拒绝**，与 Codex 同名命令行为相反。按既定口径保留不改（CLI 的 `/clear` 只清显示文档、不删库，内容已入库，重开会话可见）。终审同时确认：CLI / Codex 的删会话、清空、删工作区、退出各入口均已覆盖运行态判定，`SessionRunRegistry` 的进入/退出成对（`finally` + `Dispose` 兜底），LRU 驱逐跳过运行中会话。自审阶段记录的两条 Minor（`MainWindowViewModel.cs:1283` 忽略返回值但 `false` 不可达、`ClearMessages()` 层无单测）结论不变。

**B4（提交后二轮审查，已处理）** 用户复审 `6258700..4a924de` 后要求按建议修复，本计划范围内两处改动：

- Codex 三处「运行中先拒绝」块（侧栏删工作区、侧栏删知识库、`WorkManageWindow` / `LlmWikiManageWindow` 删除）抽成 `LubanAgentCodex/Views/Dialogs.cs` 的 `RejectDeleteIfWorkspaceRunningAsync(owner, isRunning, workspaceId)`；`null` 委托仍按「无运行中会话」放行，行为不变。
- `SessionRunRegistry.RunningSessionIds` 全仓零调用，删除（本文件 Task 1 Step 1 的代码块同步删该行）。
- 保留项：CLI 三处守卫不抽公共 helper——`SessionCommand` 判全局 `IsAnyRunning`、`WorkCommand` / `LlmWikiCommand` 判工作区，参数需四个，抽出来不减少可读成本。
