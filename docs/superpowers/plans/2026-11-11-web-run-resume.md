# Web 对话轮次续流（一期：WebHost + Web 页）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: 用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans` 逐任务执行。任务用 `- [ ]` 复选框跟踪。

**Goal：** 关掉网页只是「离开桌面」——这一轮在服务端继续跑完，重开同一会话时自动接上（快照 + 后续帧），并让「删除 / 清空」这类操作显式处理进行中的轮次。

**Architecture：** 新增轮次中心 `LubanAgentCore.Hosting.AgentRunHub`，由它持有「DI 作用域 + 取消源 + 尾部环形缓冲 + 语义快照 + 订阅者通道」；`/agent/chat` 的 action 只做「起轮次 → 当第一个订阅者 → 用自己的 pump 写 socket」，写失败只摘订阅者、不取消轮次。新增 `/agent/attach`（续流，无轮次回 `204`）与 `/agent/active`（探活）。前端 `resume()` 先探活再 attach，收到 `run.snapshot` 就整段重建气泡。

**Tech Stack：** .NET 10 / C# 13 / MSTest / SAEA.MVC 26.10.8.1 / SolidJS（`LubanAgentWebHost/web`）

**设计文档：** `luban-agent/docs/superpowers/specs/2026-11-11-web-run-resume-design.md`（已按审查结论修订，§13 为修订索引）

**本计划位置：** `luban-agent/docs/superpowers/plans/2026-11-11-web-run-resume.md`

**范围：** 一期只做 `LubanAgentWebHost` + `LubanAgentWebHost/web`（含 `LubanAgentCore` 里新增的 hub 与协议常量）。`LubanAgentCli` / `LubanAgentCodex` 的远程模式续流属二期（`HttpAgentHost.RunStreamingAsync` + `AgentSseFrame.Seq` + SSE `id:` 行），本计划**不改**。

---

## 0. 执行前必读

1. **根目录 `luban/` 不是 git 仓库。** 所有 git / build / test 命令都必须进 `cd D:\WorkBench\Walle\luban\luban-agent`。
2. **工作区已有用户 WIP，不得纳入本次改动**：`LubanAgentCore/Configuration/ConfigManager.cs`、`LubanAgentCore/Configuration/ProviderService.cs`、`LubanAgentCore/Protocol/AgentApiJson.cs`、`LubanAgentWebHost/Controllers/ProviderController.cs`、`LubanAgentWebHost/Models/ProviderContracts.cs`、`LubanAgentWebHost/Services/WebProviderService.cs`、`web/src/pages/Settings.tsx`、`web/src/pages/settings/ProviderTab.tsx`、`LubanAgent.Tests/ConfigManagerProviderRenameUnitTest.cs`、`wwwroot/index.html`、`wwwroot/assets/index-*`。任务里凡是 `git add` 都用**显式文件路径**，禁止 `git add -A` / `git add .`。
3. **`AgentApiJson.cs` 属用户 WIP**：本计划不新增/不修改它的接口；新 DTO 放独立文件（Task 5）。
4. **不执行 `git push`、`reset --hard`、`clean`、改写已有提交。** 每个任务末尾是「检查点」，允许本地 `git commit`（显式路径），推送等用户点名。
5. **构建 / 测试一律在 `luban-agent/` 下执行**：`dotnet build LubanAgent.slnx`、`dotnet test LubanAgent.Tests/LubanAgent.Tests.csproj`。前端 `cd LubanAgentWebHost/web; npm run build`（产物落 `wwwroot/`，会带 `assets/index-*` 删增噪音，属正常）。
6. **顺序不可乱**：hub（Task 1–3）→ 单测（Task 4）→ 协议与 WebHost 胶水（Task 5–7）→ 会话生命周期（Task 8）→ 前端（Task 9–11）→ 文档与全量验收（Task 12–13）。hub 必须先编译 + 单测通过，再改 `ChatAsync`，否则问题会同时来自两侧。
7. **每完成一个任务，把该任务的 `- [ ]` 改成 `- [x]`，并在「验证记录」行写实际命令与结果**（通过 / 失败原因）。这是判断基线是否可信的唯一依据。
8. **本计划对设计稿的四条施工级落定（不改变设计结论）**：
   - 轮次结束**立即**从 hub 移除，不做终态保留期（设计 §6.6 已改为此口径）。
   - hub 的可配参数只留在 `AgentRunHubOptions`（Core 内默认值），**一期不新增 `appsettings.json` 项**；心跳继续复用既有 `LubanAgentWeb:HeartbeatSeconds`。
   - `Subscribe` 的实际签名是 `Subscribe(int? fromSeq, bool snapshotOnGap = true)`：`/agent/chat` 自己那条订阅传 `snapshotOnGap: false`（它本来就从 0 开始收，快照帧对它没有意义，前端 `send` 也不解析 `run.snapshot`）。
   - 空体 204 用 `SAEA.MVC.EmptyResult`，**不是** `SAEA.Http.Model.HttpEmptyResult`（后者不是 `SAEA.MVC.ActionResult`，控制器动作返回不了）。

---

## 1. 落定的契约与类型落点

### 1.1 新增路由

| 路由 | 入参 | 返回 |
|---|---|---|
| `POST /agent/active` | `{ sessionId }` | `{ active, runId?, startedAt? }`，`sessionId` 为空 → `400` |
| `POST /agent/attach` | `{ sessionId, runId?, fromSeq? }` | 有活跃轮次 → SSE；否则 `204` 空体 |

`/agent/attach` 的分支（与设计 §6.3 一致）：

| 请求形态 | 行为 |
|---|---|
| 该会话无活跃轮次 | `204`（`SAEA.MVC.EmptyResult { Status = NoContent }`） |
| `runId` 非空但与当前轮次不符 | `204`（避免续到下一代轮次上） |
| `fromSeq` 缺省（页面刷新） | 先 `run.snapshot`，再补 `seq > snapshot.seq` 的帧，转实时 |
| 带 `fromSeq`（二期客户端） | 只补 `seq > fromSeq`；缺口已滑出缓冲窗口时改为「快照 + 后续」 |

### 1.2 类型落点

| 文件 | 内容 |
|---|---|
| `LubanAgentCore/Hosting/AgentRunHub.cs` | `AgentRunHub`、`AgentRunHubOptions` |
| `LubanAgentCore/Hosting/AgentRunHandle.cs` | `AgentRunHandle`、`AgentRunSubscription`、`AgentRunAttachResult`、`AgentRunStage` |
| `LubanAgentCore/Hosting/AgentRunSnapshot.cs` | `AgentRunSnapshot` 及子记录 |
| `LubanAgentCore/Protocol/Dtos/AgentRunRequests.cs` | `AgentAttachRequest` |
| `LubanAgentCore/Protocol/Events/AgentSseEventNames.cs` | 新增 `RunSnapshot = "run.snapshot"` |
| `LubanAgentWebHost/Hosting/SsePump.cs` | 新增 `RunAsync(response, reader, heartbeat, ct)`（订阅者 pump） |

`AgentRunHub` 只依赖 `IAgentHost` / `DiContainer` / `AgentSseFrame`，不引用 SAEA——这样 `LubanAgent.Tests`（不引 WebHost）能直接单测。

### 1.3 hub 公开签名（施工按此实现，签名变动需回改本计划）

```csharp
public sealed class AgentRunHub
{
    public AgentRunHub(
        Func<string, DiContainer> scopeFactory,
        AgentRunHubOptions? options = null,
        Action<string>? log = null);

    // 同会话已有轮次时：先取消 + 等其收尾（上限 PreemptTimeout），再登记新轮次
    Task<AgentRunHandle> StartAsync(
        string userId, string sessionId, Func<AgentRunHandle, CancellationToken, Task> body);

    AgentRunHandle? Find(string userId, string sessionId);       // 只返回未终结的轮次
    bool IsRunning(string userId, string sessionId);
    bool IsAnyRunning(string userId, IEnumerable<string> sessionIds);
    bool Cancel(string userId, string sessionId, string? runId = null);
    Task<bool> CancelAndWaitAsync(string userId, string sessionId, string? runId = null);
    Task ShutdownAsync();                                        // 取消失全部 + 等待收尾
}

public sealed class AgentRunHandle
{
    string RunId { get; }
    string SessionId { get; }
    DateTimeOffset StartedAt { get; }
    AgentRunStage Stage { get; }        // Running / Done / Cancelled / Failed
    bool IsCompleted { get; }
    CancellationToken Token { get; }    // 轮次取消令牌（宿主关闭 / 抢占 / 显式取消）
    IAgentHost Host { get; }
    DiContainer Services { get; }       // 轮次自有 scope

    internal void Cancel();                        // 取消本轮（hub 抢占与 Runs.Cancel 共用）
    void Publish(AgentSseFrame? frame);            // null 直接丢弃（心跳不过 hub）
    /// <param name="fromSeq">已收到的最后序号；null 表示按快照重建。</param>
    /// <param name="snapshotOnGap">缺口滑出缓冲窗口时是否允许改发快照；`/agent/chat` 自己订阅时传 false。</param>
    AgentRunAttachResult? Subscribe(int? fromSeq, bool snapshotOnGap = true); // null 表示轮次已终结
    void Unsubscribe(AgentRunSubscription subscription);
}

public sealed record AgentRunAttachResult(
    AgentRunSubscription Subscription,
    IReadOnlyList<AgentSseFrame> Pending);   // [run.snapshot?] + backlog
```

`AgentRunSubscription` 实现 `IDisposable`，持有 `ChannelReader<AgentSseFrame> Reader`。

---

## 2. 任务总览

| # | 任务 | 主要文件 | 完成判据 |
|---|---|---|---|
| 1 | 快照 DTO 与帧名 | `AgentRunSnapshot.cs`、`AgentSseEventNames.cs` | `LubanAgentCore` 编译 |
| 2 | 轮次句柄：seq / 环形缓冲 / 快照累积 / 订阅者 | `AgentRunHandle.cs` | `LubanAgentCore` 编译 |
| 3 | 轮次中心：登记 / 抢占 / 取消 / 关闭 | `AgentRunHub.cs` | `LubanAgentCore` 编译 |
| 4 | hub 单测 | `LubanAgent.Tests/AgentRunHubUnitTest.cs` | 新测试全绿 |
| 5 | 路由常量与 attach 入参 | `AgentApiRoutes.cs`、`AgentRunRequests.cs` | 编译 |
| 6 | SSE 订阅者 pump | `Hosting/SsePump.cs` | 编译 |
| 7 | `ChatAsync` 改造 + `Attach` / `Active` 动作 | `WebAgentService.cs`、`AgentController.cs` | 编译 |
| 8 | 会话生命周期：删除 `409`、清空先取消、工作区删除判定 | `WebSessionService.cs`、`WebWorkspaceService.cs`、`AgentWebRuntime.cs` | 编译 |
| 9 | 前端 SSE `onEmpty` | `web/src/lib/sse.ts` | `npm run build` |
| 10 | 前端 `applyFrame` / `resume()` / 挂载顺序 | `web/src/stores/chat.ts`、`web/src/pages/Chat.tsx`、`components/TopBar.tsx` | `npm run build` |
| 11 | 前端由 409 驱动的失败路径复核 | `stores/app.ts`、`pages/SessionList.tsx` | 代码复核，无改动则记录 |
| 12 | README 中英接口表与断连口径 | `README.md`、`README.en.md` | 中英一致 |
| 13 | 全量构建 / 测试 / 手动冒烟 | — | 设计 §10 的 10 条 |

---

## Task 1：快照 DTO 与 `run.snapshot` 帧名

**Files：**
- Create: `luban-agent/LubanAgentCore/Hosting/AgentRunSnapshot.cs`
- Modify: `luban-agent/LubanAgentCore/Protocol/Events/AgentSseEventNames.cs`

**为什么：** 续流的「断点前状态」必须先有稳定的传输形状；这个形状同时是 hub 单测的断言对象，必须先落地。

- [x] **Step 1：新增 `AgentRunSnapshot.cs`**

```csharp
namespace LubanAgentCore.Hosting;

/// <summary>一轮对话到当前时刻的语义快照；仅在「页面刷新、前端内存为空」时下发。</summary>
public sealed record AgentRunSnapshot
{
    public string RunId { get; init; } = "";
    public string SessionId { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public int Seq { get; init; }
    public string Text { get; init; } = "";
    public string Thinking { get; init; } = "";
    public List<AgentRunToolSnapshot> Tools { get; init; } = [];
    public List<AgentRunPlanSnapshot> Plans { get; init; } = [];
    public List<string> Progress { get; init; } = [];
    public int AttachmentsAdded { get; init; }
    public int AttachmentsSkipped { get; init; }
    public List<AgentRunNoticeSnapshot> Notices { get; init; } = [];
    public AgentRunConfirmationSnapshot? PendingConfirmation { get; init; }
}

/// <summary>工具卡快照；<c>Arguments</c> 为原始 JSON 文本（缩进与展示由前端决定）。</summary>
public sealed record AgentRunToolSnapshot
{
    public string CallId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string Status { get; init; } = "running";   // running / done / failed
    public string? ResultSummary { get; init; }
    public string? Error { get; init; }
}

/// <summary>计划项快照；<c>Arguments</c> 为原始 JSON 文本。</summary>
public sealed record AgentRunPlanSnapshot
{
    public string ToolName { get; init; } = "";
    public string Arguments { get; init; } = "";
}

/// <summary>notice 快照：原样保存该帧的事件名与载荷 JSON，前端用同一套 <c>applyFrame</c> 回放。</summary>
public sealed record AgentRunNoticeSnapshot
{
    public string Event { get; init; } = "";
    public string Data { get; init; } = "";
}

/// <summary>等待中的工具确认；<c>ExpiresAt</c> 让前端按剩余时间倒数，而不是从满值重来。</summary>
public sealed record AgentRunConfirmationSnapshot
{
    public string ConfirmationId { get; init; } = "";
    public string ToolName { get; init; } = "";
    public string Arguments { get; init; } = "";
    public int TimeoutSeconds { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
```

- [x] **Step 2：`AgentSseEventNames` 追加常量**（放在 `OrchestrationProgress` 之后）

```csharp
/// <summary>续流快照；载荷即 <see cref="Hosting.AgentRunSnapshot"/>（仅 attach 时下发一次）。</summary>
public const string RunSnapshot = "run.snapshot";
```

- [x] **Step 3：编译**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgentCore\LubanAgentCore.csproj -v m
```

期望：`Build succeeded`。

**验证记录：** ✅ 2026-11-11 实跑 `dotnet build LubanAgentCore\LubanAgentCore.csproj -v m` → 「已成功生成」，0 错误 / 4 警告（4 条均为既有 `System.Security.Cryptography.Pkcs 8.0.1` 的 NU1902/NU1903 漏洞告警，与本次改动无关）。

---

## Task 2：`AgentRunHandle`——seq / 环形缓冲 / 快照累积 / 订阅者

**Files：**
- Create: `luban-agent/LubanAgentCore/Hosting/AgentRunHandle.cs`

**为什么：** 设计 §6.1 的 6 条发布路径全部落在这一个类型里；把它与「登记/抢占」拆开，单测才能只测从不阻塞的发布路径。

- [x] **Step 1：类型骨架**

```csharp
using System.Threading.Channels;

namespace LubanAgentCore.Hosting;

public enum AgentRunStage { Running, Done, Cancelled, Failed }

public sealed record AgentRunAttachResult(
    AgentRunSubscription Subscription,
    IReadOnlyList<AgentSseFrame> Pending);

public sealed class AgentRunSubscription : IDisposable
{
    public ChannelReader<AgentSseFrame> Reader { get; }
    public void Dispose() { /* 幂等：完成并注销 */ }
}
```

- [x] **Step 2：字段与构造**

`AgentRunHandle` 由 hub 构造（`internal` 构造），传入：`runId`、`sessionId`、`IAgentHost host`、`DiContainer services`、`CancellationToken runToken`、`AgentRunHubOptions options`、`Action<string> log`。

内部状态：

- `private readonly object _gate = new();`
- `private int _seq;`（自增，首帧为 1）
- `private readonly Queue<(int Seq, AgentSseFrame Frame)> _backlog = new();`
- `private readonly List<AgentRunSubscription> _subscribers = [];`
- 快照累积字段（与 `AgentRunSnapshot` 一一对应，可空确认项单独存）
- `private AgentRunStage _stage = AgentRunStage.Running;`
- `internal Task Completion`（由 hub 赋值；`Finish` 完成它）

公开成员（`Publish` / `Subscribe` / `Unsubscribe` 之外的配套，缺一个 hub 就编不过）：

- `public CancellationToken Token { get; }` —— 就是构造传入的 `runToken`（即 `CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token).Token`），`body` 用它。
- `public bool IsCompleted => _stage != AgentRunStage.Running;`（锁外读也安全：只从 Running 单向转出）
- `internal void Cancel()` —— 轮次级取消源 `Cancel()`（`OperationCanceledException` 吞掉「已释放」）。hub 的抢占与 `Runs.Cancel` 都走它。
- `internal Task Completion { get; set; }`

- [x] **Step 3：`Publish(AgentSseFrame? frame)`**

严格按设计 §6.1 的顺序，**同一把锁内**完成「分配 seq → 更新快照 → 入缓冲 → 广播」，锁外做摘除：

1. `frame is null` → 直接 return（心跳不过 hub，不分 seq、不进快照/缓冲）。
2. 锁内：`_stage != Running` → return（终态后到达的帧丢弃）。
3. `var seq = ++_seq;` → `Accumulate(frame)` → `_backlog.Enqueue((seq, frame))` → 超出 `options.BacklogCapacity` 时 `Dequeue`。
4. 遍历 `_subscribers`：`sub.Reader` 对应的 `ChannelWriter.TryWrite(frame)` 为 false → 记入待摘除列表。
5. 锁外：对待摘除订阅者 `TryComplete()` + 从 `_subscribers` 移除 + `_log($"订阅者过慢已摘除：run={RunId}")`。

- [x] **Step 4：`Accumulate(frame)`——快照增量**

| 帧名 | 累积动作 |
|---|---|
| `text.delta` | `Text += delta` |
| `think.delta` | `Thinking += delta` |
| `tool.started` | `Tools.Add({callId, name, arguments=原始 JSON 文本, status="running"})`；`PendingConfirmation = null` |
| `tool.completed` | 按 `callId` 定位 → `Status="done"`、`ResultSummary=resultSummary` |
| `tool.failed` | 按 `callId` 定位 → `Status="failed"`、`Error=error` |
| `plan.item` | `Plans.Add({toolName, arguments=原始 JSON 文本})` |
| `orchestration.progress` | `Progress.Add(message ?? text)`，空串不入列表 |
| `tool.denied` / `attachment.added` / `attachment.skipped` / `error` | `Notices.Add({Event=帧名, Data=frame.Data 原文})`；`tool.denied` 同时清 `PendingConfirmation` |
| `tool.confirm` | `PendingConfirmation = {confirmationId, toolName, arguments=原始 JSON 文本, timeoutSeconds, ExpiresAt=UtcNow+timeoutSeconds}` |
| `attachment.added` | 另加 `AttachmentsAdded++` |
| `attachment.skipped` | 另加 `AttachmentsSkipped++` |
| `done` / `session` | 忽略（`done` 不改快照，避免前端回放到已结束的一轮） |

载荷解析统一走 `JsonDocument.Parse(frame.Data)` + `TryGetProperty`，解析失败只记日志、不抛（帧内容由框架产出，不应让 hub 因一个坏帧崩掉轮次）。

- [x] **Step 5：`Subscribe(int? fromSeq, bool snapshotOnGap = true)`——订阅与补发原子**

锁内完成：

1. `_stage != Running` → 返回 `null`。
2. 建订阅者：`Channel.CreateBounded<AgentSseFrame>(new BoundedChannelOptions(options.SubscriberCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait })`。
3. `fromSeq is null`（页面刷新）：`var snapshot = BuildSnapshot();`（`snapshot.Seq = _seq`）→ `pending = [run.snapshot 帧] + _backlog.Where(x => x.Seq > snapshot.Seq)`。
4. `fromSeq is { } seq`：若 `_backlog.Count == 0 || seq >= _backlog.Peek().Seq - 1` → `pending = _backlog.Where(x => x.Seq > seq)`；否则（缺口已滑出窗口）：
   - `snapshotOnGap == true` → 退回第 3 步（快照 + 后续）；
   - `snapshotOnGap == false` → 只补现有缓冲（`_backlog.Where(x => x.Seq > seq)`），**不发快照**。`/agent/chat` 自己的首连接走这条：它本来就是「从零开始收」，多发一个 `run.snapshot` 只会让前端多一条它不认识的帧。
5. 把订阅者加入 `_subscribers`，返回 `AgentRunAttachResult(sub, pending)`。

`run.snapshot` 帧用 `AgentSseFrame.Create(AgentSseEventNames.RunSnapshot, snapshot)` 生成（camelCase 由 `AgentEventProtocol.SerializePayload` 保证）。

- [x] **Step 6：`Unsubscribe` / `Finish` / `DisposeAsync`**

- `Unsubscribe(sub)`：锁内移除 + 锁外 `TryComplete()`，幂等（重复调用不抛）。
- `internal void Finish(AgentRunStage stage)`：锁内置 `_stage = stage`、取走 `_subscribers` 并清空 → 锁外逐个 `TryComplete()`。**必须在释放 scope 之前调用**（设计 §6.1 收尾顺序）。

> 施工落定：`Completion` 直接就是 hub 里那个 `Task.Run` 的返回任务，不在 `Finish` 里 `TrySetResult`——`Finish` 之后还要释放作用域、摘登记，`Completion` 完成判定必须晚于这两步，否则抢占方会在旧作用域尚未释放时开新轮次。原文「`Finish` 完成 `Completion`」已按此实现修正。
- `DisposeAsync()`：`await Services.DisposeAsync()`（`DiContainer` 已实现 `IAsyncDisposable`）。

- [x] **Step 7：编译**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgentCore\LubanAgentCore.csproj -v m
```

**验证记录：** ✅ 与 Task 3 一并实跑 `dotnet build LubanAgentCore\LubanAgentCore.csproj -v m` → 「已成功生成」，0 错误（`AgentRunHandle` 引用的 `AgentRunHubOptions` 在 Task 3 才落地，故编译合并到 Task 3 Step 5 一次执行）。

---

## Task 3：`AgentRunHub`——登记 / 抢占 / 取消 / 关闭

**Files：**
- Create: `luban-agent/LubanAgentCore/Hosting/AgentRunHub.cs`

**为什么：** `AgentWebRuntime` 现有的 `BeginRun` / `EndRun` 只登记取消源，不带作用域、不等待收尾；把这些职责搬进 hub 后，`WebHost` 侧只保留一层薄委托和 SSE 胶水。

- [x] **Step 1：选项与字段**

```csharp
public sealed record AgentRunHubOptions
{
    public int BacklogCapacity { get; init; } = 2000;      // 尾帧环形缓冲
    public int SubscriberCapacity { get; init; } = 512;    // 每个订阅者的有界队列
    public TimeSpan PreemptTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
```

`AgentRunHub` 持有：`Func<string, DiContainer> _scopeFactory`、`AgentRunHubOptions _options`、`ConcurrentDictionary<string, AgentRunHandle> _runs`（键 `userId\u0001sessionId`，与 `AgentWebRuntime.RunKey` 同口径）、`CancellationTokenSource _shutdown`。日志走 `Action<string>`（默认空实现；WebHost 传 `AgentWebRuntime.Log`）。

- [x] **Step 2：`StartAsync`**

1. `previous = _runs` 中的同会话轮次 → `previous.Cancel()` → `await previous.Completion.WaitAsync(_options.PreemptTimeout)`；`TimeoutException` 只记日志（`"抢占等待旧轮次收尾超时"`），继续。
2. `var scope = _scopeFactory(userId);` → `scope.GetService(typeof(IAgentHost)) as IAgentHost`；为 null 时 `await scope.DisposeAsync()` 后抛 `InvalidOperationException("当前作用域解析不到 IAgentHost")`（`ChatAsync` 的 `503` 已在前置校验拦过，这里是兜底）。
3. 建 `AgentRunHandle`：`runToken = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token).Token`；`_runs[key] = handle`。
4. 赋值后若 `_runs[key]` 已不是本 handle（并发 `StartAsync` 互相覆盖）→ 取消本 handle、释放 scope、重试一次（最多 3 次）。
5. 启动后台任务（`Task.Run`，`CancellationToken.None`）：

```csharp
handle.Completion = Task.Run(async () =>
{
    var stage = AgentRunStage.Done;
    try
    {
        await body(handle, handle.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (handle.Token.IsCancellationRequested)
    {
        stage = AgentRunStage.Cancelled;
    }
    catch (Exception ex)
    {
        stage = AgentRunStage.Failed;
        _log($"轮次失败：session={sessionId} run={handle.RunId} {ex.Message}");
    }
    finally
    {
        handle.Finish(stage);                       // 1. 完成订阅者通道
        await handle.DisposeAsync().ConfigureAwait(false);  // 2. 释放 scope / 宿主实例
        _runs.TryRemove(new KeyValuePair<string, AgentRunHandle>(key, handle)); // 3. 移除登记（只删自己）
    }
}, CancellationToken.None);
```

6. 返回 handle。`body` 里已经发布过 `done`（沿用 `StreamFramesAsync` 的收尾帧），`Finish` 只负责关通道。

- [x] **Step 3：查询与取消**

- `Find`：`_runs.TryGetValue` 且 `!handle.IsCompleted` 才返回（终结即视为无轮次）。
- `IsRunning`：`Find != null`。
- `IsAnyRunning(userId, sessionIds)`：任一 `IsRunning`（工作区删除判定用）。
- `Cancel(userId, sessionId, runId?)`：`runId` 非空且与登记不符 → false（迟到取消不误伤新轮次，沿用 `AgentWebRuntime.CancelRun` 口径）；相符 → `handle.Cancel()` → true。
- `CancelAndWaitAsync(...)`：`Cancel` 后 `await handle.Completion.WaitAsync(_options.PreemptTimeout)`，超时返回 false（调用方据返回值决定是否继续删/清）。

- [x] **Step 4：`ShutdownAsync`**

`_shutdown.Cancel()` → 取 `_runs` 快照 → 逐个 `await Completion.WaitAsync(_options.ShutdownTimeout)`（超时只记日志）→ `_shutdown.Dispose()`。重复调用幂等（`CancellationTokenSource` 已释放时直接返回）。

- [x] **Step 5：编译**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgentCore\LubanAgentCore.csproj -v m
```

**验证记录：** ⬜ 未执行

---

## Task 4：hub 单测

**Files：**
- Create: `luban-agent/LubanAgent.Tests/AgentRunHubUnitTest.cs`

**为什么：** hub 放 Core 的唯一理由就是能被 `LubanAgent.Tests` 直接覆盖；快照 / 缓冲 / 摘除 / 抢占这些判定靠手动冒烟验不出来。

- [x] **Step 1：测试脚手架**

`FakeAgentHost` 实现 `IAgentHost` 六个成员（`IsInitialized => false`、`Services => null!`、`CurrentSessionId => null`、`InitializeAsync(string sessionId, CancellationToken ct = default, AgentBuildOverrides? overrides = null) => Task.CompletedTask`、`Reset` 空、`RunStreamingAsync` 抛 `NotSupportedException`）——hub 只把 host 当透传句柄，测试里不用它跑流。

`NewHub()`：

```csharp
var root = new DiContainer().AddSingleton<IAgentHost>(new FakeAgentHost())
    .Build(validateScopes: false, validateOnBuild: false);
return new AgentRunHub(_ => root.CreateScope(), new AgentRunHubOptions { BacklogCapacity = 16, SubscriberCapacity = 4 });
```

> 注意：`CreateScope()` 返回子容器，测试里不要 Assert 其释放，只断言行为。

- [x] **Step 2：用例清单（全部为 `[TestMethod]`，中文方法名，与既有测试风格一致）**

1. `帧按序分配seq并进入缓冲`：`Publish` 3 帧 → `Subscribe(null)` 的 `Pending` 首帧是 `run.snapshot` 且 `Seq == 3`，其后无 backlog；`Subscribe(0)` 的 `Pending` 依次是 3 帧。
2. `空帧不进序列`：`Publish(null)` 后 `Subscribe(null)` 的 `snapshot.Seq == 0`。
3. `快照累积正文思考与工具`：`session` → `text.delta("ab")` ×2 → `think.delta` → `tool.started` → `tool.completed` → `tool.failed` → 断言 `Text/Thinking/Tools[?].Status/ResultSummary/Error`。
4. `快照累积notice与确认`：`tool.confirm` → 断言 `PendingConfirmation.ExpiresAt` 随 `timeoutSeconds` 递推；随后 `tool.denied` 与 `attachment.added` → `Notices` 两条 + `AttachmentsAdded == 1`，且 `PendingConfirmation == null`。
5. `环形缓冲滑窗`：容量 16，发 20 帧 → `Subscribe(0)`（缺口滑出）改走快照路径，`Pending[0].Name == "run.snapshot"`。
6. `慢订阅者被摘除且通道完成`：`SubscriberCapacity = 1`，订阅后发 3 帧 → 该订阅者 `Reader.ReadAsync` 在读完已入队帧后 `WaitToReadAsync` 返回 false（不永久挂住）；`hub` 里该轮次仍可继续发布（后续不再抛）。
7. `终态完成所有订阅者通道并移除登记`：body 返回 → `handle.Completion` 完成、`hub.Find(...)` 为 null、两个订阅者的 `WaitToReadAsync` 均返回 false。
8. `同会话新轮次抢占旧轮次`：第一轮 body `await Task.Delay(Timeout.Infinite, ct)`；`StartAsync` 第二轮 → 第一轮 `Stage == Cancelled`，第二轮 `Stage == Running`。
9. `取消只命中指定runId`：两轮（用不同 session 或先结束第一轮）→ `Cancel(..., "过期runId")` 返回 false，轮次仍在跑；正确 runId 返回 true 且 `Stage` 变 `Cancelled`。
10. `工作区判定`：`IsAnyRunning` 在同一用户多会话下为 true；全部结束/取消后为 false。

- [x] **Step 3：跑测试**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj --filter "FullyQualifiedName~AgentRunHubUnitTest"
```

期望：10 条全绿。若某条因「时序不可控」不稳，**改测试的同步方式**（用 `TaskCompletionSource` 卡住 body），不要用 `Task.Delay` 赌时序。

**验证记录：** ✅ 2026-11-11 实跑 `dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj --filter "FullyQualifiedName~AgentRunHubUnitTest"` → 「已通过! - 失败: 0，通过: 10，已跳过: 0，总计: 10」。首轮两处断言有误（比对 `Data` 原文而非标签），改为 `Tag(frame)` 取值后复跑全绿。

---

## Task 5：路由常量与 attach 入参

**Files：**
- Modify: `luban-agent/LubanAgentCore/Protocol/AgentApiRoutes.cs`
- Create: `luban-agent/LubanAgentCore/Protocol/Dtos/AgentRunRequests.cs`

- [x] **Step 1：`AgentApiRoutes.Agent` 追加两个常量**（放在 `Reset` 之后）

```csharp
/// <summary>探活：该会话是否有正在进行的回合。</summary>
public const string Active = "/agent/active";

/// <summary>接上该会话正在进行的回合（SSE）；无活跃回合返回 204。</summary>
public const string Attach = "/agent/attach";
```

- [x] **Step 2：新增 `AgentRunRequests.cs`**

```csharp
namespace LubanAgentCore.Protocol.Dtos;

/// <summary>接上正在进行的回合。</summary>
public sealed record AgentAttachRequest
{
    /// <summary>所属会话。</summary>
    public string SessionId { get; init; } = "";

    /// <summary>期望接上的轮次标识；非空且与服务端当前轮次不符时不续流（回 204）。</summary>
    public string? RunId { get; init; }

    /// <summary>已收到的最后帧序号；缺省表示前端内存为空，按快照重建。</summary>
    public int? FromSeq { get; init; }
}
```

- [x] **Step 3：编译**（同 Task 3 Step 5 命令）

**验证记录：** ✅ 随 Task 6 Step 2 一并实跑 `dotnet build LubanAgentWebHost\LubanAgentWebHost.csproj -v m` → 「已成功生成」，0 错误。

---

## Task 6：SSE 订阅者 pump

**Files：**
- Modify: `luban-agent/LubanAgentWebHost/Hosting/SsePump.cs`

**为什么：** 心跳从 producer 挪到「每个订阅者自己的 pump」（设计 §6.1 第 3 条）；同一个循环串行写帧与 `: ping`，保持「同一 socket 单写者」。

- [x] **Step 1：新增 `RunAsync`**（`using System.Threading.Channels;` 写到文件头）

```csharp
/// <summary>订阅者 pump：从轮次通道取帧写 socket，空闲时按心跳间隔补 <c>: ping</c>。</summary>
/// <param name="response">当前响应。</param>
/// <param name="reader">该订阅者的帧通道。</param>
/// <param name="heartbeat">心跳间隔。</param>
/// <param name="ct">退出令牌（宿主关闭时用）。</param>
/// <returns>通道完成或写失败时返回。</returns>
public static async Task RunAsync(
    HttpResponse response,
    ChannelReader<AgentSseFrame> reader,
    TimeSpan heartbeat,
    CancellationToken ct = default)
{
    using var timer = new PeriodicTimer(heartbeat);
    var tick = timer.WaitForNextTickAsync(ct).AsTask();
    while (true)
    {
        var read = reader.WaitToReadAsync(ct).AsTask();
        var winner = await Task.WhenAny(read, tick).ConfigureAwait(false);

        if (ReferenceEquals(winner, tick))
        {
            if (ct.IsCancellationRequested) return;
            if (!TryWriteHeartbeat(response)) return;
            tick = timer.WaitForNextTickAsync(ct).AsTask();
            continue;
        }

        bool more;
        try { more = await read.ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (reader.TryRead(out var frame))
        {
            if (!TryWrite(response, frame)) return;
        }

        if (!more) return;               // 轮次收尾：通道完成
    }
}
```

> 注意：`WaitToReadAsync` 与 `WaitForNextTickAsync` 每次循环都重建任务，不要缓存已完成的 tick。

- [x] **Step 2：编译**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgentWebHost\LubanAgentWebHost.csproj -v m
```

期望：通过（此时 `WebAgentService` 未改，`ChatAsync` 仍走旧路径）。

**验证记录：** ✅ 2026-11-11 实跑 `dotnet build LubanAgentWebHost\LubanAgentWebHost.csproj -v m` → 「已成功生成」，0 错误（`ChatAsync` 此时仍是旧路径，符合本步预期）。

---

## Task 7：`ChatAsync` 改造 + `Attach` / `Active` 动作

**Files：**
- Modify: `luban-agent/LubanAgentWebHost/Services/WebAgentService.cs`（`ChatAsync`、`StreamFramesAsync`；新增 `AttachAsync`、`ActiveAsync`）
- Modify: `luban-agent/LubanAgentWebHost/Controllers/AgentController.cs`

**为什么：** 这是唯一「轮次不再随连接死」的落点。改造后 `finally` 只剩注销订阅者。

- [x] **Step 1：`ChatAsync` 结构**

```csharp
public static Task<ActionResult> ChatAsync(string payload) => WebApi.RunGuardedAsync(async () =>
{
    // 1) JSON / prompt 校验保持不变（400 必须在切 Content-Type 之前）
    var userId = WebApi.Runtime.UserId;

    // 2) 前置校验用短命 scope，校验完立即释放
    SessionResolution resolution;
    await using (var probe = WebApi.Runtime.CreateRequestScope(userId))
    {
        if (probe.GetService(typeof(IAgentHost)) is not IAgentHost)
        {
            return WebApi.FailJson(503, AgentDisabled);
        }

        resolution = await WebSessionResolver.ResolveAsync(probe, userId, input.SessionId).ConfigureAwait(false);
        if (!resolution.Ok)
        {
            return WebApi.FailJson(resolution.Code, resolution.Error ?? "会话解析失败");
        }
    }

    // 3) 起轮次：scope / host / 取消源都由 hub 持有
    AgentRunHandle run;
    try
    {
        run = await WebApi.Runtime.Runs.StartAsync(userId, resolution.SessionId, async (handle, ct) =>
        {
            var sideFrames = new ConcurrentQueue<AgentSseFrame>();
            var progressSink = handle.Services.GetService<HostOrchestrationProgressSink>();
            void OnProgress(OrchestrationProgressEvent progress) => handle.Publish(
                AgentSseFrame.Create(AgentSseEventNames.OrchestrationProgress, OrchestrationProgressPayload.From(progress)));
            if (progressSink is not null) progressSink.Progress += OnProgress;

            var request = new AgentRunRequest
            {
                Input = prompt,
                SessionId = resolution.SessionId,
                SystemPrompt = string.IsNullOrWhiteSpace(input.System) ? null : input.System,
                Attachments = NormalizeAttachments(input.Attachments),
                EnableTools = input.EnableTools,
                ReasoningEffort = ParseEffort(input.ReasoningEffort),
                PermissionMode = ParsePermissionMode(input.PermissionMode),
                OnPlannedAction = (toolName, arguments) => sideFrames.Enqueue(
                    AgentSseFrame.Create(AgentSseEventNames.PlanItem, new { toolName, arguments })),
                OnUserDenied = (toolName, arguments) => sideFrames.Enqueue(
                    AgentSseFrame.Create(AgentSseEventNames.ToolDenied, new { toolName, arguments }))
            };

            try
            {
                await foreach (var frame in StreamFramesAsync(handle.Host, request, resolution, sideFrames, handle.RunId, ct)
                    .ConfigureAwait(false))
                {
                    handle.Publish(frame);      // null（心跳）由 hub 丢弃
                }
            }
            finally
            {
                if (progressSink is not null) progressSink.Progress -= OnProgress;
            }
        }).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        return WebApi.FailJson(503, WebApi.SafeErrorMessage(ex));
    }

    // 4) 当前连接 = 第一个订阅者（fromSeq = 0，重放本次已发布的帧，含 session 首帧）
    var response = WebApi.Response;
    AgentRunAttachResult? attach = null;
    try
    {
        SsePump.Begin(response);
        attach = run.Subscribe(0, snapshotOnGap: false);
        if (attach is null)
        {
            // 极端情况：轮次在「StartAsync」与「订阅」之间就已收尾，帧随通道关闭一起消失。
            // 给客户端一个明确信号，否则界面会静默停在半截。
            SsePump.TryWrite(response, AgentSseFrame.ForError("本轮已结束，请重新加载该会话查看结果"));
            SsePump.TryWrite(response, AgentSseFrame.ForDone(resolution.SessionId));
            return new AgentEventStreamResult();
        }

        foreach (var pending in attach.Pending)
        {
            if (!SsePump.TryWrite(response, pending)) return new AgentEventStreamResult();
        }

        await SsePump.RunAsync(
            response,
            attach.Subscription.Reader,
            TimeSpan.FromSeconds(Math.Max(1, WebApi.Runtime.Settings.HeartbeatSeconds)),
            WebApi.Runtime.ShutdownToken).ConfigureAwait(false);
    }
    finally
    {
        attach?.Subscription.Dispose();   // 只摘订阅者：不取消轮次
        SsePump.Complete(response);
    }

    return new AgentEventStreamResult();
});
```

要点（漏一条就会回到原 bug）：

- `CancelQuietly(linked)` 与 `WebApi.Runtime.EndRun(...)` 必须从 `finally` 删除；`finally` 不再碰轮次。
- `SsePump.*` 只在 HTTP 线程（本 action + `RunAsync` 循环）调用，后台 producer 只 `Publish`。
- `attach == null` 时**先 `Begin` 再写两帧 `error` + `done`**，不要改状态码（此时响应头已写出）。
- `Subscribe(0, snapshotOnGap: false)`：这是「从零开始收」，不需要快照帧；前端 `send` 也不解析 `run.snapshot`。
- `StreamFramesAsync` 的 `yield return null`（心跳占位）保留：它仍用于「宿主静默时排空旁路帧」，只是 `null` 由 hub 丢弃。

- [x] **Step 2：`AttachAsync`**

```csharp
public static Task<ActionResult> AttachAsync(string payload) => WebApi.RunGuardedAsync(async () =>
{
    if (!WebApi.TryReadPayload<AgentAttachRequest>(payload, out var input) || input is null
        || string.IsNullOrWhiteSpace(input.SessionId))
    {
        return WebApi.FailJson(400, "sessionId 不能为空");
    }

    var sessionId = input.SessionId.Trim();
    var run = WebApi.Runtime.Runs.Find(WebApi.Runtime.UserId, sessionId);
    if (run is null) return NoContent();
    if (!string.IsNullOrWhiteSpace(input.RunId)
        && !string.Equals(input.RunId.Trim(), run.RunId, StringComparison.Ordinal))
    {
        return NoContent();
    }

    // 先订阅，失败才可能回 204（一旦写了响应头就改不了状态码）
    var attach = run.Subscribe(input.FromSeq);
    if (attach is null) return NoContent();

    var response = WebApi.Response;
    try
    {
        SsePump.Begin(response);
        foreach (var frame in attach.Pending)
        {
            if (!SsePump.TryWrite(response, frame)) return new AgentEventStreamResult();
        }

        await SsePump.RunAsync(
            response,
            attach.Subscription.Reader,
            TimeSpan.FromSeconds(Math.Max(1, WebApi.Runtime.Settings.HeartbeatSeconds)),
            WebApi.Runtime.ShutdownToken).ConfigureAwait(false);
    }
    finally
    {
        attach.Subscription.Dispose();
        SsePump.Complete(response);
    }

    return new AgentEventStreamResult();
});

private static ActionResult NoContent() => new EmptyResult { Status = HttpStatusCode.NoContent };
```

> **不要用 `SAEA.Http.Model.HttpEmptyResult`**：它继承 `HttpActionResult`（只实现 `IHttpResult`），而控制器动作的返回类型与 `WebApi.*Json` 都是 `SAEA.MVC.ActionResult`，两者不可互换。`SAEA.MVC.EmptyResult`（该包已导出、实现 `IEmptyResult`）才是「空体 + 指定状态码」的可用类型，已在 `SAEA.MVC 26.10.8.1` 的 `bin` 里核对过 `Status` 属性与无参构造。

- [x] **Step 3：`ActiveAsync`**

```csharp
public static Task<ActionResult> ActiveAsync(string payload) => WebApi.RunGuardedAsync(async () =>
{
    if (!WebApi.TryReadPayload<AgentActiveRequest>(payload, out var input) || input is null
        || string.IsNullOrWhiteSpace(input.SessionId))
    {
        return WebApi.FailJson(400, "sessionId 不能为空");
    }

    var run = WebApi.Runtime.Runs.Find(WebApi.Runtime.UserId, input.SessionId.Trim());
    return WebApi.OkJson(new { active = run is not null, runId = run?.RunId, startedAt = run?.StartedAt });
});
```

`AgentActiveRequest` 与 `AgentAttachRequest` 同文件（`AgentRunRequests.cs`），只有 `SessionId`。

> `async` 无 `await` 会产生 CS1998 警告：上面两个动作体里确实没有 `await` 的，改成**非 async** 返回 `Task.FromResult(...)`，或保留 `await` 结构（`AttachAsync` 里 `SsePump.RunAsync` 就有 `await`；`ActiveAsync` 请写成 `WebApi.RunGuardedAsync(() => Task.FromResult<ActionResult>(...))` 形态，避免警告）。

- [x] **Step 4：`AgentController` 追加两个动作**

```csharp
/// <summary>探活：该会话是否有正在进行的回合。</summary>
[HttpPost]
public Task<ActionResult> Active(string payload) => WebAgentService.ActiveAsync(payload);

/// <summary>接上正在进行中的回合（SSE）；无活跃回合返回 204。</summary>
[HttpPost]
public Task<ActionResult> Attach(string payload) => WebAgentService.AttachAsync(payload);
```

- [x] **Step 5：编译**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgentWebHost\LubanAgentWebHost.csproj -v m
```

期望：通过；`WebAgentService` 里不再出现 `WebApi.Runtime.BeginRun` / `EndRun`（Task 8 才删这两个方法，本步允许 hub 与旧登记并存一编译周期）。

**验证记录：** ✅ 2026-11-11 实跑 `dotnet build LubanAgentWebHost\LubanAgentWebHost.csproj -v m` → 「已成功生成」，0 错误。施工落定：① `Runs` 与旧登记的替换在本次一并完成（Task 8 Step 1 提前），否则 `Runtime.Runs` 不解析；② 计划里的 `handle.Services` 改为 `handle.Host.Services`（`AgentRunHandle` 只暴露 `Host`，作用域持有在内部）；③ `CancelQuietly` 已删除——`ChatAsync` 不再取消轮次，留着就是死代码。

---

## Task 8：会话生命周期——删除 409、清空先取消、工作区删除判定

**Files：**
- Modify: `luban-agent/LubanAgentWebHost/Hosting/AgentWebRuntime.cs`
- Modify: `luban-agent/LubanAgentWebHost/Services/WebSessionService.cs`
- Modify: `luban-agent/LubanAgentWebHost/Services/WebWorkspaceService.cs`

**为什么：** 「关页面不再顺带取消」之后，原先被这个副作用兜住的删除/清空必须显式判定（设计 §6.7）。

- [x] **Step 1：`AgentWebRuntime` 换掉登记表**

- 加 `public AgentRunHub Runs { get; }`，构造函数里 `Runs = new AgentRunHub(userId => _host.Root.CreateUserScope(userId), log: Log);`
- `CancelRun` 改为 `=> Runs.Cancel(userId, sessionId, runId);`
- **删除** `_runs`、`BeginRun`、`EndRun`、`RunRegistration`、`RunKey`、`TryCancel`（删除前用 `Select-String -Pattern "BeginRun|EndRun|RunRegistration" -Path LubanAgentWebHost\*.cs,LubanAgentWebHost\**\*.cs` 确认只剩本文件与 `WebAgentService`）。
- `DisposeAsync` 改为：

```csharp
_shutdown.Cancel();                 // 先让各订阅者 pump 退出
await Runs.DisposeAsync().ConfigureAwait(false);   // 内部取消全部轮次并按 ShutdownTimeout 等收尾
_shutdown.Dispose();
await _host.DisposeAsync().ConfigureAwait(false);
```

- [x] **Step 2：`WebSessionService.DeleteAsync` 加 409**

在 `CheckOwnerAsync` 之后、`DeleteSessionAsync` 之前插入：

```csharp
if (WebApi.Runtime.Runs.IsRunning(WebApi.Runtime.UserId, input.SessionId.Trim()))
{
    return WebApi.FailJson(409, "该会话正在进行中，请先停止再删除");
}
```

判定必须早于软删，否则轮次跑完还会往已删会话里写。

- [x] **Step 3：`WebSessionService.ClearMessagesAsync` 改为先取消再清空**

在 `CheckOwnerAsync` 通过之后、`ClearMessagesAsync` 之前插入：

```csharp
var stopped = await WebApi.Runtime.Runs
    .CancelAndWaitAsync(WebApi.Runtime.UserId, input.SessionId.Trim())
    .ConfigureAwait(false);
if (!stopped && WebApi.Runtime.Runs.IsRunning(WebApi.Runtime.UserId, input.SessionId.Trim()))
{
    return WebApi.FailJson(409, "本轮对话尚未停止，请稍后重试");
}
```

> 无轮次时 `CancelAndWaitAsync` 返回 `true`（无需取消即视为已停止），保证「没有轮次也照常清空」。

- [x] **Step 4：`WebWorkspaceService.DeleteAsync` 加工作区判定**

在拿到 `input.WorkspaceId` 之后、`DeleteWorkspaceAsync` 之前：

```csharp
var sessions = await scope.GetRequiredService<ISessionWorkspaceQuery>()
    .GetByWorkspaceAsync(input.WorkspaceId.Trim(), WebApi.Runtime.UserId)
    .ConfigureAwait(false);
if (sessions.Any(s => WebApi.Runtime.Runs.IsRunning(WebApi.Runtime.UserId, s.SessionId)))
{
    return WebApi.FailJson(409, "该工作区下有正在进行中的会话，请先停止");
}
```

- [x] **Step 5：编译 + 跑既有测试**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgent.slnx -v m
dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj --filter "FullyQualifiedName~AgentRunHubUnitTest"
```

**验证记录：** ✅ 2026-11-11 实跑 `dotnet build LubanAgent.slnx -v m` → 「已成功生成」，0 错误；实跑全量 `dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj` → 「已通过! - 失败: 0，通过: 452，已跳过: 0，总计: 452」（含本任务新增的 10 条 hub 用例）。施工偏差：`CancelAndWaitAsync` 实现在「本就无轮次」时返回 `false`（不是计划里写的那样返回 `true`），调用方用紧随其后的 `IsRunning` 区分「无需取消」与「取消超时」，两处判定都已按此实现。

---

## Task 9：前端 SSE——`204` 走 `onEmpty`

**Files：**
- Modify: `luban-agent/LubanAgentWebHost/web/src/lib/sse.ts`

**为什么：** 现状 `!response.ok` 之外还会因 `content-type` 不含 `text/event-stream` 走 `onError`，`toApiError` 把空体 204 变成「请求失败（HTTP 204）」（`lib/api.ts` `parseEnvelope` 与 `toApiError` 的兜底文案）。`resume()` 必然踩到「跑完瞬间」。

- [x] **Step 1：`SseHandlers` 增加可选回调**

```ts
export interface SseHandlers {
  onFrame: (frame: SseFrame) => void;
  /** 服务端以 204 空体表示「没有可续的轮次」；不设该回调时按错误处理。 */
  onEmpty?: () => void;
  onError?: (error: unknown) => void;
  onClose?: () => void;
}
```

- [x] **Step 2：`streamPost` 在判定 Content-Type 之前先拦 204**

```ts
if (response.status === 204) {
  handlers.onEmpty?.();
  return;
}

const contentType = response.headers.get('content-type') ?? '';
if (!response.ok || !contentType.includes('text/event-stream') || !response.body) {
  handlers.onError?.(await toApiError(response));
  return;
}
```

- [x] **Step 3：构建**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent\LubanAgentWebHost\web
npm run build
```

**验证记录：** ✅ 2026-11-11 实跑 `npx tsc --noEmit` → 退出码 0；`npm run build` → `✓ 37 modules transformed`、`✓ built in 2.46s`，产物 `wwwroot/assets/index-Dri-UNi1.js`（176.03 kB）。

---

## Task 10：前端 `applyFrame` / `resume()` / 挂载顺序

**Files：**
- Modify: `luban-agent/LubanAgentWebHost/web/src/stores/chat.ts`
- Modify: `luban-agent/LubanAgentWebHost/web/src/pages/Chat.tsx`
- Modify: `luban-agent/LubanAgentWebHost/web/src/components/TopBar.tsx`（仅注释口径）

**为什么：** 断线重开后要「无点击自动续上」，且不能把 `loadHistory` 的整表覆盖与快照重建撞在一起。

- [x] **Step 1：抽出 `applyFrame`**

把 `send` 里 `onFrame` 的 `switch` 原样搬进 `createChat` 内的函数（`updateAssistant` / `updateTool` 已在该作用域）：

```ts
/** 应用一帧到指定 assistant 气泡；返回 true 表示本轮已结束（done）。 */
function applyFrame(assistantId: string, event: string, data: unknown): boolean {
  const payload = (data ?? {}) as Record<string, unknown>;
  switch (event) {
    case 'session':
      if (typeof payload.runId === 'string') setRunId(payload.runId);
      return false;
    case 'think.delta': /* … 原逻辑 … */ return false;
    // … 其余分支原样搬入 …
    case 'done':
      return true;
    default:
      return false;
  }
}
```

`send` 的 `onFrame` 改为：

```ts
onFrame: ({ event, data }) => {
  if (applyFrame(assistantId, event, data)) done = true;
},
```

- [x] **Step 2：快照重建 + `resume()`**

```ts
/** 用 run.snapshot 重建整条 assistant 气泡（仅页面刷新、内存为空时）。 */
function restoreFromSnapshot(assistantId: string, payload: Record<string, unknown>): void {
  const tools = Array.isArray(payload.tools)
    ? (payload.tools as Record<string, unknown>[]).map((tool) => ({
        callId: String(tool.callId ?? nextId()),
        name: String(tool.name ?? 'tool'),
        argumentsText: pretty(tool.arguments),
        status: (tool.status as ToolStatus) ?? 'running',
        resultSummary: tool.resultSummary === undefined ? undefined : String(tool.resultSummary),
        error: tool.error === undefined ? undefined : String(tool.error),
      }))
    : [];

  const bubble: ChatItem = {
    kind: 'assistant',
    id: assistantId,
    text: String(payload.text ?? ''),
    thinking: String(payload.thinking ?? ''),
    tools,
    streaming: true,
    history: false,
    time: String(payload.startedAt ?? new Date().toISOString()),
  };

  // 同一页面内重连（上次流断在中途）时，尾部那条未落库的 assistant 气泡就是本轮的半成品：
  // 直接替换它，否则重试一次就多出一份重复正文。
  setItems((list) => {
    const last = list.at(-1);
    if (last?.kind === 'assistant' && !last.history) {
      return [...list.slice(0, -1), bubble];
    }
    return [...list, bubble];
  });
  setPlans(Array.isArray(payload.plans) ? (payload.plans as PlanEntry[]) : []);
  setProgress(Array.isArray(payload.progress) ? (payload.progress as string[]) : []);

  // notice 类条目由同一套 applyFrame 回放，文案与实时路径完全一致
  for (const notice of (payload.notices as { event?: string; data?: string }[] | undefined) ?? []) {
    if (typeof notice.event !== 'string' || typeof notice.data !== 'string') continue;
    try {
      applyFrame(assistantId, notice.event, JSON.parse(notice.data));
    } catch {
      /* 坏载荷忽略 */
    }
  }

  const pending = payload.pendingConfirmation as Record<string, unknown> | null | undefined;
  if (pending) {
    const expiresAt = Date.parse(String(pending.expiresAt ?? ''));
    const remaining = Number.isFinite(expiresAt) ? Math.max(1, Math.round((expiresAt - Date.now()) / 1000)) : Number(pending.timeoutSeconds ?? 60);
    setConfirmation({
      confirmationId: String(pending.confirmationId ?? ''),
      toolName: String(pending.toolName ?? '工具调用'),
      arguments: pending.arguments,
      timeoutSeconds: remaining,
    });
  }

  if (typeof payload.runId === 'string') setRunId(payload.runId);
}
```

```ts
/** 结束本轮本地流状态（停止 / 断线 / 正常收尾共用）。 */
function finishStream(assistantId: string | null, done: boolean): void {
  if (assistantId) updateAssistant(assistantId, (item) => { item.streaming = false; });
  setStreaming(false);
  setRunId(null);
  setConfirmation(null);
  controller = null;
  if (done) void refreshSessions();
}

/** 探活并接上服务端正在进行的轮次；本地流还活着时直接返回。 */
async function resume(): Promise<void> {
  if (streaming()) return;

  let active = false;
  try {
    const state = await post<{ active?: boolean }>('/agent/active', { sessionId });
    active = state?.active === true;
  } catch {
    active = false;      // 探活失败按「无活跃轮次」处理，不阻塞进入会话
  }

  if (!active) {
    if (!historyLoaded() || items().length === 0) await loadHistory(true);
    return;
  }

  const assistantId = nextId();
  controller = new AbortController();
  setStreaming(true);
  setRunId(null);

  for (let attempt = 0; ; attempt += 1) {
    let done = false;
    let empty = false;      // 探活通过、attach 时轮次已跑完（跑完与 attach 之间的竞态）
    let started = false;
    await streamPost(
      '/agent/attach',
      { sessionId },
      {
        onFrame: ({ event, data }) => {
          if (event === 'run.snapshot') {
            restoreFromSnapshot(assistantId, (data ?? {}) as Record<string, unknown>);
            started = true;
            return;
          }
          if (!started) {
            // 极端情况下快照帧之前先到实时帧：懒建气泡，后续帧照常累积
            started = true;
            updateAssistant(assistantId, () => {});
          }
          if (applyFrame(assistantId, event, data)) done = true;
        },
        onEmpty: () => {
          empty = true;
        },
        onError: () => { /* 由外层循环重试 */ },
        onClose: () => { /* 同上 */ },
      },
      controller.signal,
    );

    if (empty) {
      // 轮次已结束：内容在库里，直接补历史；不能重试（重试只会一直 204）
      await loadHistory(true);
      finishStream(started ? assistantId : null, true);
      return;
    }
    if (done) { finishStream(assistantId, true); return; }
    if (controller === null || controller.signal.aborted) { finishStream(assistantId, false); return; }
    if (attempt >= 3) {
      finishStream(assistantId, false);
      showToast('续流失败，请重新打开该会话', 'error');
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 1000 * 2 ** attempt));
  }
}
```

> `onError` / `onClose` 故意留空：`streamPost` 正常返回即表示「流结束或失败」，由 `for` 循环按 `done` / `empty` / `aborted` / 重试次数判定。
> `empty` 必须单独判：`onEmpty` 之后再重试只会一次次拿到 `204`，看起来像「续流失败」。
> 重试会重新收到 `run.snapshot`；因为 `restoreFromSnapshot` 会替换尾部未落库气泡，重复快照不会产生第二份正文。

- [x] **Step 3：导出 `resume`**

`return { …, resume }` 加进 `createChat` 的返回对象。

- [x] **Step 4：`Chat.tsx` 挂载顺序**

```ts
onMount(() => {
  void loadCommands();
  void (async () => {
    await chat.loadHistory();     // 整表覆盖必须先在快照重建之前完成
    await chat.resume();
  })();
  if (sessions().length === 0) void refreshSessions();
  // 不在这里 abort：会话实例常驻，切换页面时流式回合继续在后台跑
});
```

- [x] **Step 5：`TopBar.tsx` 注释口径**

`closeTab` 的注释改为：

```ts
// 关标签只是本地离开：销毁本地实例并中止这条本地流；服务端轮次由 hub 继续跑，重开时自动续上
```

- [x] **Step 6：构建**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent\LubanAgentWebHost\web
npm run build
```

期望：`vite build` 通过（`tsc` 未在 build 脚本里，类型错误靠 IDE / `npx tsc --noEmit` 自查，自查结果写进验证记录）。

**验证记录：** ✅ 2026-11-11 实跑 `npx tsc --noEmit` 退出码 0；`npm run build` → `✓ built in 2.46s`。施工落定：① `restoreFromSnapshot` 内的局部变量命名为 `pendingConfirm`（原名 `pending` 会遮蔽同名的待发附件信号）；② `send` 的 `onClose` 收敛为 `finishStream(assistantId, done)`，与原逻辑等价；③ 快照里 `resultSummary` / `error` 对 `null` 也按 `undefined` 处理，避免 `String(null) === 'null'`。

---

## Task 11：前端删除失败路径复核（预期无代码改动）

**Files：**
- Review: `web/src/stores/app.ts`（`deleteSession` / `deleteWorkspace`）
- Review: `web/src/pages/SessionList.tsx:347`、`:422`
- Review: `web/src/pages/Chat.tsx:648`

- [x] **Step 1：核对三点**

1. `deleteSession` / `deleteWorkspace` 失败时只 `showToast` 并 `return false`，不透传异常（已满足，409 文案会直接弹出）。
2. 两个调用点都是 `if (ok) dropChat(...)`（已满足，删除失败不会销毁本地实例、不会停掉还在跑的轮次）。
3. `Chat.tsx` 删除当前会话的路径同样以 `ok` 为门（已满足）。

- [x] **Step 2：把核对结论写进验证记录**

若发现某处不是 `if (ok)` 门控，改为门控后再继续；不要顺手改按钮置灰等交互（设计 §6.7：服务端判定是硬约束）。

**验证记录：** ✅ 2026-11-11 静态核对（未运行，纯代码走查）：`stores/app.ts` 的 `deleteWorkspace`（`:219`）与 `deleteSession`（`:274`）均在 `catch` 里 `showToast(...)` 后 `return false`，不外抛；`pages/SessionList.tsx:347-348`、`:422-423` 与 `pages/Chat.tsx:650-652` 三处调用点全部为 `if (ok)` 门控后才 `dropChat(...)`。`409` 文案会直接弹 toast，本地实例与后台轮次不受影响。**无代码改动。**

---

## Task 12：README 中英同步

**Files：**
- Modify: `luban-agent/README.md`（接口表 `:438-456`、约束段 `:463`）
- Modify: `luban-agent/README.en.md`（同位置）

- [x] **Step 1：接口表补两行**（紧跟 `POST /agent/cancel`）

```markdown
| `POST /agent/active` | 探活：该会话是否有正在进行的回合 |
| `POST /agent/attach` | 接上该会话正在进行的回合（SSE，先补快照）；无活跃回合返回 204 |
```

英文：

```markdown
| `POST /agent/active` | Probe whether the session has a round in progress |
| `POST /agent/attach` | Attach to the round in progress (SSE, snapshot first); `204` when there is none |
```

- [x] **Step 2：改写「断连取消需显式调用」这条约束**

中文新文案：

> - **断连不等于取消**：一轮对话挂在宿主进程内的轮次中心上，SSE 连接断开只移除订阅者，轮次继续跑完；浏览器重开同一会话时先 `POST /agent/active` 探活，有活跃轮次则 `POST /agent/attach` 续流（先补 `run.snapshot`，再转实时帧），无活跃轮次回 `204` 并由客户端拉历史。主动停止仍走 `POST /agent/cancel`；删除进行中的会话、删除含进行中会话的工作区会被 `409` 拒绝，清空消息会先取消并等轮次收尾。

英文对应同义改写（保留 `409` 与两个新路由的表达）。

- [x] **Step 3：核对中英结构一致**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
Select-String -Path README.md,README.en.md -Pattern '/agent/(active|attach)'
```

期望：两份文件各命中 2 行，行号相邻。

**验证记录：** ✅ 2026-11-11 实跑 `Select-String -Path README.md,README.en.md -Pattern '/agent/(active|attach)'` → 两份文件各命中 3 行：新增两行接口表相邻（`:444` / `:445`），第 3 行在改写后的约束段（`:465`，该段同时提到两个路由，故比计划预期的 2 行多 1 行）。

---

## Task 13：全量验收

- [x] **Step 1：后端构建 + 测试**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent
dotnet build LubanAgent.slnx -v m
dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj -v m
```

期望：构建 0 error；测试全绿（含 Task 4 的 10 条）。

- [x] **Step 2：前端构建**

```powershell
cd D:\WorkBench\Walle\luban\luban-agent\LubanAgentWebHost\web
npm run build
```

- [ ] **Step 3：手动冒烟（对应设计 §10 的 10 条，逐条记结果）**

| # | 操作 | 期望 |
|---|---|---|
| 1 | 发起长回答 → 中途关页面 → 30 秒后重开同一会话 | 断点前正文/思考/工具卡/notice 完整，后续继续长，跑完可看全文 |
| 2 | 同上，观察宿主控制台 | 不再刷 `httpServer_OnError ... Sending data timeout`；最多一条「订阅者已摘除」 |
| 3 | 跑完瞬间重开 | 直接看到完整回答，**不出现**「请求失败（HTTP 204）」 |
| 4 | 等待工具确认时关页面 → 重开 | 确认框仍在，倒计时按剩余时间；点允许后流程继续 |
| 5 | 删除正在跑的会话 | 拒绝并提示「该会话正在进行中，请先停止再删除」，轮次继续跑完；停止后删除成功 |
| 6 | 删除含进行中会话的工作区 | 拒绝并提示「该工作区下有正在进行中的会话，请先停止」 |
| 7 | 进行中「清空消息」 | 轮次先取消并收尾，随后清空；之后不再冒出半截回答 |
| 8 | 续流后点停止 | 只取消本轮（控制台确认带 runId 命中），其它轮次不受影响 |
| 9 | 多标签页同会话 | 两个标签都能续流并各自完整输出 |
| 10 | `attach` 无活跃轮次直连（curl / 控制台） | `204` 空体 |

- [x] **Step 4：变更清单复核**

`git -C D:\WorkBench\Walle\luban\luban-agent status --short`，确认**没有**把用户 WIP 文件（见 §0 第 2 条）带入本次改动；`wwwroot/assets/index-*` 的删增属构建产物，提交时按需处理。

**验证记录：** ⚠️ 部分执行（2026-11-11）。

- Step 1 ✅ 实跑 `dotnet build LubanAgent.slnx -v m` → 「已成功生成」，退出码 0；`dotnet test LubanAgent.Tests\LubanAgent.Tests.csproj -v m` → 「已通过! - 失败: 0，通过: 452，已跳过: 0，总计: 452」，退出码 0。终审修复后复跑：构建 0 错误、20 个警告（均为既有 NU1902/NU1903 与 Avalonia AVLN3001）；测试 456 通过 / 0 失败（含新增的「无轮次时取消返回 false」「取消等待超时返回 false 且轮次仍在跑」两条）。
- Step 2 ✅ 实跑 `npx tsc --noEmit`（退出码 0）+ `npm run build` → `✓ built in 2.46s`；终审修复后复跑 `npx tsc --noEmit` 退出码 0、`npm run build` → `✓ built in 1.86s`，产物 `wwwroot/assets/index-DK6w7RpJ.js`。
- Step 3 ❌ **未执行**：10 条均需启动宿主并接真实模型做交互式浏览器验证，本回合未启动服务、未连接模型，不臆断通过；须人工按表逐条走。
- Step 4 ✅ 实跑 `git status --short`：本次改动落在 `LubanAgentCore/{Hosting,Protocol}`、`LubanAgentWebHost/{Controllers,Hosting,Services,web/src}`、`LubanAgent.Tests`、`README*.md`、计划文档；用户 WIP 文件（`ConfigManager.cs`、`ProviderService.cs`、`AgentApiJson.cs`、`ProviderController.cs`、`ProviderContracts.cs`、`WebProviderService.cs`、`web/src/stores/app.ts`、`Settings.tsx`、`ProviderTab.tsx`、`wwwroot/index.html`）保持原状未被本次触碰。`wwwroot/assets` 的删增（`index-XzOqHh6n.js` / `index-CC20lCS5.js` → `index-Dri-UNi1.js`）是 `vite build` 的产物替换，非源码改动。
- 补充静态核对：`ActionResult.Status` 由 MVC 执行器统一落盘（`JsonResult` 自身只有构造器、无状态成员），故 `EmptyResult { Status = NoContent }` 可产出 204 空体；**此结论来自反射查看 `SAEA.MVC.dll`，未经运行验证**。

---

## 附录 A：施工中若与设计冲突，按此判定

| 冲突点 | 判定 |
|---|---|
| 想给 hub 加「终态保留期」 | 不加。`active` 只回两态，排查靠日志（设计 §6.6 / §12.2） |
| 想让 `attach` 在没有活跃轮次时回 `200` + 空流 | 不允许。`204` 是契约，前端靠 `onEmpty` 分支区分（设计 §6.3） |
| 想让心跳帧进 hub | 不允许。心跳只由订阅者 pump 发（设计 §6.1 第 3 条） |
| 想在前端「点一下才接上」 | 不允许。`resume()` 自动执行，不加提示按钮 |
| 想让删除进行中会话「先取消再删」 | 不允许。硬拒绝 `409`；只有 `/session/clearMessages` 是先取消再清空（设计 §12.6） |
| 想顺手把 `AgentSseFrame` 加 `Seq` / 让前端解析 `id:` | 二期。一期只保证服务端 seq 内部一致（设计 §8 第 2 条） |

## 附录 B：回滚点

- hub + 单测（Task 1–4）独立于 WebHost：回滚只需删除新增文件，`LubanAgent.slnx` 仍可编译。
- `ChatAsync` 改造（Task 7）是唯一「行为切换点」：回滚该文件即回到「断连即取消」；`Attach` / `Active` 两个动作留在控制器上不会产生副作用（没人调用）。
- 前端 `resume()` 回滚：`Chat.tsx` 的 `onMount` 去掉 `await chat.resume()` 即回到「只拉历史」。

---

## 附录 C：独立终审结论与处理（2026-11-11）

独立审查子代理（只读）结论：**有条件合并，无 Critical**；2 条 Important、8 条 Minor，已按下表处理。

| 编号 | 位置 | 问题 | 处理 |
|---|---|---|---|
| I1 | `web/src/stores/chat.ts` `restoreFromSnapshot` | 只替换「列表最后一项」，尾部是 notice 时半成品气泡不在末尾 → 旧正文与快照正文并存 | ✅ 改为回扫「最后一个非 history 的 assistant」原位替换 |
| I2 | `web/src/stores/chat.ts` `send` 的 `onError` / `resume()` | SSE 出错分支不触发 `onClose`，`streaming` 永久为真：输入框卡在「停止」、`resume()` 被短路 | ✅ `onError` 里补 `finishStream(assistantId, false)`；`resume()` 的短路条件改为「`streaming()` 且 `controller` 非空」，残留状态不再挡住续流 |
| I3 | `LubanAgentCli/App/ViewModels/CommandViewModel.cs` 的 `/clear` | CLI 运行中不拒绝清屏，与 Codex 同名命令相反 | ⏸ 按既定口径保留（只清显示文档、不删库），不改；差异记于客户端计划附录 A |
| M1 | `web/src/stores/chat.ts` 快照 notice 重放 | 同页多次重连把同一条提示叠加多份 | ✅ `applyFrame` 增加可选 `noticeKey`，notice 统一走 `pushNotice` 按 key 去重；快照回放用「runId#序号」作 key |
| M2 | `web/src/stores/chat.ts` `send` 的 `onClose` | 流中断无任何提示 | ✅ `done === false` 且未主动中止时补一条「连接已中断，本轮仍在后台运行」的警告 |
| M3 | `WebSessionService.ClearAllAsync`、`WebLlmWikiService.DeleteAsync` | 批量清空 / 知识库删除会级联删会话，却缺 `409` 判定 | ✅ 两处补运行态判定，文案与 `/workspace/delete` 一致（前者无 workspaceId 时按当前用户全部会话判定） |
| M4 | `AgentRunHandle.BuildSnapshotPending` | `AddRange(_backlog.Where(x => x.Seq > snapshot.Seq))` 恒为空 | ✅ 删除死代码，改为单帧返回 + 注释说明快照已含缓冲全部内容 |
| M5 | `web/src/stores/chat.ts` `resume()` 的「实时帧先于快照」分支 | 注释写懒建气泡，实际是空转，后续 delta 全丢 | ✅ 真的追加一个空气泡 |
| M6 | `AgentRunHandle.Completion` / `AgentRunHub.StartAsync` | `Completion` 默认 `Task.CompletedTask`，`TryAdd` 与赋值之间被抢占方/取消方读到会误判「已收尾」 | ✅ 改为构造即创建 `TaskCompletionSource`，收尾末尾 `MarkCompleted()` 置位 |
| M7 | `SsePump.RunAsync` | 每次循环新建 `WaitToReadAsync`，心跳分支 `continue` 时旧任务无人 await | ✅ `read` 提升到循环外，只在被消费后重建 |
| M8 | `LubanAgent.Tests/AgentRunHubUnitTest.cs` | 缺「无轮次取消」「取消超时」路径覆盖 | ✅ 补 2 条；`BuildPending` 缺口→快照已由既有「环形缓冲滑窗」用例覆盖 |

前端仍无自动化测试（仓库现状），I1 / I2 / M1 / M2 / M5 的运行时效果需靠 Task 13 Step 3 的第 1–4、9 条人工冒烟验证。

## 附录 D：提交后二轮审查与处理（2026-11-11）

`6258700..4a924de` 落库后用户要求复审，按 Standards / Spec 两轴并行审查（Standards 14 条、Spec 5 条，均无 Critical、无硬性规范违规）。裁决：**断线重试按设计实现，其余按建议修复**。逐条处理如下。

| 编号 | 位置 | 问题 | 处理 |
|---|---|---|---|
| S1 | `AgentRunHub.StartAsync` / `WebAgentService.ChatAsync` | 抢占超时导致登记失败被兜成 503，前端重试无门 | ✅ 新增 `AgentRunConflictException`；`ChatAsync` 在其专属 catch 回 `409「上一轮尚未结束，请先停止后再发送」`，通用异常仍 503 |
| S2 | `AgentRunHandle.IsCompleted` / `Stage` | 终态在锁内写、锁外读，无非 `volatile` 保证 | ✅ 读改 `Volatile.Read`，`Finish` 内改 `Volatile.Write` |
| S3 | `ProviderService.MoveFetchedModels` | 只写新键不删旧键，「迁移」名不副实，旧标识会残留模型缓存 | ✅ `ProviderCache` 增 `Remove`，迁移后清掉旧键 |
| S4 | `AgentRunHandle` 快照注释 | 续行掉到第 0 列 | ✅ 补缩进 |
| S5 | `AgentRunHandle` 尾帧缓冲 | `BacklogCapacity` 配负数会 `Dequeue` 空队列抛异常 | ✅ 构造时 `Math.Max(0, …)` 钳制，`AgentRunHubOptions` 注释同步 |
| S6 | Codex 四处 / CLI 三处运行态守卫 | 同一 4 行块重复 | ◑ Codex 三处抽到 `Dialogs.RejectDeleteIfWorkspaceRunningAsync`；CLI 三处保留（一处判全局 `IsAnyRunning`、两处判工作区，参数需四个，抽 helper 不减少可读成本） |
| S7 | `AgentRunHubUnitTest.NewHub` | 收整个 `AgentRunHubOptions` 却只取两个字段，其余静默忽略 | ✅ 改收 `TimeSpan? preemptTimeout` / `shutdownTimeout` |
| S8 | `AgentAttachRequest.RunId` / `FromSeq` | 服务端已实现但客户端不传，契约无文档 | ✅ README 中英各补一句 `attach` 的可选入参语义（不删：`fromSeq` 缺口判定是设计 §10 的验收项） |
| S9 | `SessionRunRegistry.RunningSessionIds` | 全仓零调用 | ✅ 删除（客户端计划对应代码块同步删该行） |
| S12 | `settings.ts` / `ProviderTab.tsx` | `originalName` 走 `Record<string, unknown>`，重命名入参无类型约束 | ✅ 新增 `ProviderSaveRequest` 供 `saveProvider` 使用 |
| S13 | `styles.css` | `Settings.tsx` 删页脚后 `.settings-panel > .app-footer` 成死规则 | ✅ 删除该规则（`.app-footer` 与 `.boot > .app-footer` 仍被 `App.tsx` 使用，保留） |
| S14 | `WebAgentService.ActiveAsync` | 同步 lambda + `Task.FromResult` 与同文件风格不一致 | ⏸ 保留：改 `async` 需假 `await` 或以 `CS1998` 告警换风格，无实质收益 |
| P1 | `chat.ts` `send` 的 `onError` | 断线后不重试，本页收不到后台轮次余下内容（设计 §6.5 明确要求退避重试） | ✅ 新增 `recoverAfterDrop`：1s / 2s / 4s 退避重试 `resume()`，最多 3 次，耗尽才提示；`resume()` 改返回 `true/false` 供判定。偏差一处：「本地流还在跑」短路返回 `true`（返回 `false` 会让重试循环反复 attach，耗尽后掐掉用户新发的轮次） |
| P2 | `WebAgentService.ActiveAsync` / `AttachAsync` | 设计 §10 要求单测覆盖 204 / 空 `sessionId` 400 / `runId` 不符分支 | ⏸ 未补：`LubanAgent.Tests` 未引用 `LubanAgentWebHost`，动作层依赖静态 `AgentWebRuntime.Current` 与用户初始化（真实库、工作区目录），属集成测试成本；核心分支（`Subscribe(fromSeq)` 的缺口→快照、无缺口→补缓冲）已由 hub 单测覆盖，HTTP 壳层三态改由 Task 13 Step 3 人工冒烟覆盖 |
| P3 | `chat.ts` 提示去重 | 实时提示用 `local-N`、回放用 `runId#序号`，同页重连会重复显示断线前的提示 | ✅ 会话内累计 `renderedNotices`，快照回放跳过已渲染前缀，按 `notices.length` 对齐计数 |
| P4 | `WebSessionService.ClearAllAsync` | 「有会话正在进行中，请先停止」与 `/workspace/delete` 文案不一致 | ✅ 按范围区分：带 `workspaceId` 用工作区文案，全局清空保留原文案 |
| P5 | `AgentsMdInitializerUnitTest` | 「协作约定」只抽查 4 个关键词，793 字正文与 12 条顺序无回归保护 | ✅ 改为逐行字面量拼接后整段 `Contains`，并断言条数为 12 |