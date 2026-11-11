# LubanAgent 对话轮次续流设计（Web 页 → CLI/Codex）

- 日期：2026-11-11
- 范围：`luban-agent` / `LubanAgentWebHost`（一期）、`luban-agent` / `LubanAgentCore.Client` + `LubanAgentCli` + `LubanAgentCodex`（二期）
- 状态：待确认

## 1. 意图

现在关掉页面/切走网络，服务端会把这一轮对话直接取消，重开页面看不到任何后续内容，宿主的控制台还会持续打印 `Sending data timeout`。

用户要的语义是：**关页面只是离开桌面**。这一轮继续在服务端跑完，重开同一个会话时**自动接上**，断点前的思考、工具卡、正文都在，之后的内容继续往下长，直到这一轮自己结束。

## 2. 现状事实（均为代码核对结论）

| 事实 | 依据 |
|---|---|
| SSE 帧由 action 线程直接写 socket：写失败即 `break` | `LubanAgentWebHost/Services/WebAgentService.cs` `ChatAsync` 的帧循环 |
| `finally` 里无条件 `CancelQuietly(linked)` + `EndRun`，该令牌就是传给宿主的取消令牌，断连即取消整轮 | 同文件 `ChatAsync` 的 `finally`；`StreamFramesAsync` 用同一 `ct` 打开 `host.RunStreamingAsync` |
| 一轮的 DI 作用域就是 HTTP 请求作用域，请求结束即释放宿主实例与进度出口 | `ChatAsync` 内 `CreateRequestScope`，请求结束 `await using` 释放 |
| 帧没有缓冲、没有重放，`AgentEventStreamResult` 只是「不再补写响应体」的标记 | `Hosting/AgentEventStreamResult.cs`；`Hosting/SsePump.cs` |
| 对话接口只有 5 个，没有「接上正在跑的一轮」的入口 | `Controllers/AgentController.cs`；`LubanAgentCore/Protocol/AgentApiRoutes.cs` 的 `Agent` 段 |
| 消息只由 `StoreChatHistoryAsync`（回合正常结束后）一次写入 user + assistant；回合被取消则两条都不写 | `luban-framework/LuBan.AIAgent/Sessions/SessionChatHistoryProvider.cs:156`（方法）、`:220`、`:236`（两处写库） |
| 前端进入会话只拉历史，不探活 | `web/src/stores/chat.ts` 的 `loadHistory`；`web/src/pages/Chat.tsx` 的 `onMount` |
| 前端会话实例常驻池中，离开对话页不中止流 | `web/src/stores/chats.ts`；`Chat.tsx` `onMount` 的注释 |
| `Sending data timeout` 来自 SAEA 发送看门狗（默认 180s），不是本仓库代码；SSE 写出是同步阻塞读线程 | `SAEA.Http/Base/Net/HttpConnectionSession.cs` 的 `WriteBytes`（本地源码参照）；`SAEA.Sockets/Core/Tcp/SaeaServerSocket.cs` 的 `StartSendWatchdogAsync`。**仓库实际用的是 NuGet `SAEA.MVC 26.10.8.1`，本地源码不保证与包内一致** |
| 后台任务自建 `CreateUserScope` 长期持有 scope 在项目内已有先例 | `LubanAgentCore/Services/KnowledgeTaskService.cs:122`、`:153` |
| 远程模式下 CLI / Codex 共用 `HttpAgentHost` 上的 SSE 消费；`finally` 中「没收到 done 就调 `/agent/cancel`」，因此断线同样会杀掉服务端回合 | `LubanAgentCore/Client/HttpAgentHost.cs` 的 `RunStreamingAsync`；`AgentServerClient.cs` 的 `StreamAsync` 注释；`AgentClientConnection.cs` 替换 `IAgentHost` |
| CLI / Codex 都能接既有 WebHost（`AgentClientConnection` 字段在各宿主上） | `LubanAgentCli/App/StartupRunner.cs:42`、`LubanAgentCodex/App.axaml.cs:28` |
| 宿主身份单用户，取消登记已按 (userId, sessionId) 键控并带 runId 防误伤 | `Hosting/AgentWebRuntime.cs` 的 `BeginRun`/`CancelRun`/`EndRun` |

## 3. 目标

1. 一轮对话的生命周期不再与任何 HTTP 连接绑定；连接断开只移除订阅者。
2. 重开页面（或页面断线后自动重连）时**无需任何点击**：若该会话有活跃轮次，先补一份当前快照，再续实时帧。
3. 断点前的语义状态完整：正文、思考、工具卡（含状态）、计划项、编排进度、**等待中的工具确认**。
4. `Sending data timeout` 不再挂住整轮，只降级为「该连接被摘除」。
5. 不改变 `/agent/chat` 的既有契约与既有前端调用方式（老客户端仍可只发 chat、不 attach）。
6. 二期让 `HttpAgentHost` 在断线后自动续流，CLI / Codex 在远程模式下自动受益。

## 4. 非目标

- 不做多用户 / 远程访问，仍是回环单用户。
- 不做跨进程恢复：宿主进程退出后这一轮就没了（DB 里只会有已落库的部分）。
- 不改消息落库时机（仍按回合边界写 assistant 消息）。
- **显式取消或失败的轮次，user 与 assistant 两条消息都不落库**（`SessionChatHistoryProvider.StoreChatHistoryAsync` 只在正常结束时写）；重开后这一轮整段消失，与现状一致。
- 不做「历史消息里的工具结果回放」（现状就不落工具结果，不在本次范围）。

## 5. 反目标

- 为了续流把正文按秒写库（污染历史消息、与 `SessionChatHistoryProvider` 的回合边界语义打架）。
- 让后台轮次等在某个慢客户端的 socket 写完成上（这等于把原来的 bug 换个地方）。
- 新增第二套取消登记表（`AgentWebRuntime` 已有按会话的 `_runs`，必须复用）。

## 6. 一期：WebHost + Web 页面

### 6.1 轮次中心

轮次中心放 `LubanAgentCore/Hosting/AgentRunHub.cs`：它只需要 `IAgentHost`、`DiContainer`、`AgentSseFrame`，不碰 SAEA，符合「没有不可共享依赖的薄层并入 Core」的拆分原则。WebHost 侧只留 SSE 胶水（`Hosting/SsePump.cs`、`Hosting/AgentEventStreamResult.cs`）。

`LubanAgentWebHost/Hosting/AgentWebRuntime.cs` 以 `Runtime.Runs` 暴露它，现有的 `BeginRun` / `CancelRun` / `EndRun` 保留为薄委托，`/agent/cancel` 与 `WebAgentService.CancelAsync` 不需要改。

放在 Core 的直接收益：`LubanAgent.Tests`（只引 Core / Plugin.NewProject / Codex，不引 WebHost）能对快照累积、环形缓冲、订阅者摘除、`active/attach` 判定直接写单测；二期客户端复用同一套语义。

每个轮次持有：

- `RunId`、`SessionId`、`Stage`（`Running` / `Done` / `Cancelled` / `Failed`）、`StartedAt`、`FinishedAt`
- `CancellationTokenSource`（与既有 `RunRegistration` 合并，不新建第二张表）
- **自带 DI 作用域**（`Runtime.CreateRequestScope(userId)`）与宿主实例：轮次创建时一次性建好，`ChatAsync` 不再自己持有请求作用域给宿主用
- **语义快照**（见 6.2）
- **尾帧环形缓冲**：`(seq, frame)`，容量可配（默认 2000 帧）。只用于「快照之后到最新」的缝隙补帧，不承担全量重放
- **订阅者列表**：每个订阅者一个有界 `Channel<AgentSseFrame>`（容量 512）

发布路径：帧进入 hub 后，先更新快照与缓冲（同一把锁），再对每个订阅者 `TryWrite`。**发布永不阻塞**：写不进队列说明该订阅者已慢到失去意义，直接摘除该订阅者（并记一条日志），后台轮次继续。

### 6.2 快照

快照是「把这一轮到目前为止的语义状态压成一个对象」，字段与前端 `ChatItem` 的 assistant 分支对齐：

- `text`：累积的 `text.delta`
- `thinking`：累积的 `think.delta`
- `tools[]`：`{callId, name, argumentsText, status, resultText}`，由 `tool.started` / `tool.completed` / `tool.failed` 累积
- `plans[]`：`plan.item` 累积；`progress[]`：`orchestration.progress` 累积
- `attachmentsAdded` / `attachmentsSkipped`：计数
- `notices[]`：`{tone, text}`，覆盖 `tool.denied`、`attachment.added`、`attachment.skipped`、`error` 这些在前端是独立 notice 条目的帧——重开后它们不能凭空消失，只留计数是不够的
- `pendingConfirmation`：当前等待中的工具确认（`tool.confirm` 的载荷），用于重开后仍能弹确认框
- `seq`：生成快照时对应的帧序号

快照在每帧发布时增量更新，成本 O(1)。内存占用与这一轮的正文长度同阶，轮次结束后随 run 一起释放。

### 6.3 续流契约

沿用「路由只认 `/{controller}/{action}`」与既有 `payload` 单字段约定，新增两个动作到 `AgentController`：

**`POST /agent/active`**，入参 `{sessionId}`，返回 `{active, runId, stage, startedAt}`。轮次结束后的保留期内仍返回 `active=false` + 终态信息，便于前端区分「没有轮次」与「刚跑完还没落库」。

**`POST /agent/attach`**，入参 `{sessionId, runId?, fromSeq?}`，返回 SSE 流：

| 请求形态 | 服务端行为 |
|---|---|
| 该会话无轮次（或已过保留期） | 返回 `204`，前端改走 `loadHistory(true)` |
| 有轮次，`fromSeq` 缺省（页面刷新后内存为空） | 先发 1 帧 `run.snapshot`（6.2 的快照），再发缓冲中 `seq > snapshot.seq` 的帧，之后转实时 |
| 有轮次，带 `fromSeq`（二期客户端断线重连，UI 状态还在） | 只发 `seq > fromSeq` 的帧；若 `fromSeq` 已滑出缓冲窗口，改发 `run.snapshot` + `run.resync` 提示（二期处理） |
| 轮次在 attach 前已结束 | 一律回 `204`，由前端 `loadHistory(true)` 拿完整回答（保留期只用于让 `active` 能解释终态） |

新增事件名 `run.snapshot`（写入 `LubanAgentCore/Protocol/Events/AgentSseEventNames.cs`），载荷即 6.2 的快照。

**两处必须分清**（否则会重复收帧）：

- **页面刷新**（F5 / 关标签再开）= JS 内存清空 → `fromSeq` 缺省，走快照重建。
- **路由切回对话页**（点会话列表/设置页回来）= 会话实例还在 `web/src/stores/chats.ts` 的常驻池里、SSE 还活着、`streaming()` 仍为 true → **不 attach、不重复订阅**，由 6.5 的前置判断挡掉。

### 6.4 `ChatAsync` 改造

1. 解析与校验保持原样（`400` / `503` 仍在切换 `Content-Type` 之前）。
2. 校验通过后**创建轮次**（hub 内建 scope + 宿主），而不是在请求内建 scope 交给轮次用。
3. 后台 `Task.Run`（或 `Task.Factory.StartNew` + `LongRunning`）执行原来的 `StreamFramesAsync`，产出直接 `Publish` 给 hub，**不碰 `HttpResponse`**。
4. 当前 HTTP 连接注册为第一个订阅者，用自己的写循环从 channel 取帧写 socket；写失败只注销订阅者，**不取消轮次**。
5. `finally` 只剩「注销订阅者」；轮次的取消只来自 `/agent/cancel`、同会话新轮次抢占（`BeginRun` 已有语义）、宿主关闭（`ShutdownToken`）。
6. 订阅者 pump 结束（正常收到 `done` 或写失败）后 action 返回，SAEA 关连接，行为与现在一致。

`SsePump` 仍然只在订阅者 pump 线程调用，避免 `HttpResponse` 被后台线程触碰。

### 6.5 前端（`LubanAgentWebHost/web`）

- `stores/chat.ts`：把现有 `onFrame` 里的分支抽成 `applyFrame(item, event, payload)`，供 `send` 与续流共用；新增 `resume()`：
  0. `streaming()` 为 true（本地流还活着）→ 直接返回，绝不重复订阅
  1. `POST /agent/active`
  2. 不活跃 → 若本地尚无内容则 `loadHistory(true)`，结束
  3. 活跃 → `streamPost('/agent/attach', { sessionId })`；收到 `run.snapshot` 时新建 assistant 气泡并按快照填 `text` / `thinking` / `tools` / `plans` / `progress` / `notices`，`pendingConfirmation` 直接进确认弹窗状态
  4. 后续帧走 `applyFrame`
- `pages/Chat.tsx` 的 `onMount`：`loadHistory()` 之后调用 `chat.resume()`。
- 断线自动重连：`streamPost` 的 `onError` 若非用户主动停止（`AbortController` 未 abort），按 1s / 2s / 4s 退避重试 `resume()`，最多 3 次；耗尽后按现有逻辑显示错误提示。
- 顶部会话标签的「进行中」指示继续用 `streaming()`，不需要新状态。

### 6.6 生命周期与清理

- 轮次结束（`Done` / `Cancelled` / `Failed`）后，hub 中保留 60 秒（可配）再移除；保留期内 `active` 仍返回终态供排查，但 `attach` 一律回 `204`（内容已在 DB，前端走 `loadHistory`）。
- 宿主关闭：`ShutdownToken` 触发时取消全部轮次并等待其收尾（与现有 `AgentWebRuntime` 的关闭流程一致）。
- 轮次的 scope 与宿主实例在轮次移除时释放（`await using` / `DisposeAsync`）。

### 6.7 会话生命周期联动

改造后「关页面」不再顺带取消轮次，原先被这个副作用兜住的操作要显式处理：

- **`/session/delete` 拒绝删除进行中的会话**：`WebSessionService.DeleteAsync` 在归属校验（`CheckOwnerAsync`）之后、`DeleteSessionAsync` 之前加一道判定——该会话在 hub 里有 `Stage == Running` 的轮次 → `FailJson(409, "该会话正在进行中，请先停止再删除")`，**不执行任何删除**。判定必须早于软删，否则轮次跑完还会往已删会话里写。
- 前端不需要新交互：`web/src/stores/app.ts` 的 `deleteSession` 已有失败 toast 通路，409 的 message 直接弹出来；删除按钮是否提前置灰由施工定，服务端判定是硬约束。
- **`/session/clearMessages` 保持「先取消再清空」**：清空不销毁会话，先取消可避免「已清空却又长出一条回答」的幽灵数据。
- 前端 `chats.ts` 的 `dropChat` 仍只负责本地止流，服务端轮次由上面两条接口约定收尾。
- **删除工作区时若连带有进行中的会话，同样拒绝**：`/workspace/delete` 会连带删除该工作区下的会话（前端 `SessionList.tsx` 的「删除项目」把整组会话一起丢掉），判定口径与单会话删除一致——先查 hub 里该工作区下是否有 `Running` 轮次，有则 `409` + 「该工作区下有正在进行中的会话，请先停止」。

## 7. 边界与错误

| 场景 | 行为 |
|---|---|
| 客户端关页面 | 订阅者摘除，轮次继续 |
| 网络静默断开（无 FIN） | socket 写阻塞/超时 → 该订阅者被摘除；`Sending data timeout` 变成一条日志，不再挂住轮次。**前提是后台发布只入队、绝不写 socket**；即便包内的同步写永不返回，最坏结果也只是该订阅者的 pump 线程滞留到 SAEA 看门狗关连接，轮次与其快照不受影响 |
| CLI / Codex 远程模式正在跑的轮次，网页也能看到、attach、点停止 | 期望行为：单机单用户，轮次按 (userId, sessionId) 归属**会话**而非发起客户端（见 §12 决策 3） |
| 重开时轮次已跑完 | `attach` 回 `204` → 前端 `loadHistory(true)`，看到完整回答 |
| 重开时轮次刚失败或被取消 | `attach` 回 `204`；该轮内容从不落库（`SessionChatHistoryProvider.StoreChatHistoryAsync` 只在回合正常结束时写），重开看不到这半截——与现状一致，见 §4 非目标 |
| 删除进行中的会话 | 拒绝：`409` + 「该会话正在进行中，请先停止再删除」，会话与轮次都保持原样（见 6.7）；删除工作区若连带这样的会话同样拒绝；`/session/clearMessages` 则是先取消再清空 |
| 同一会话并发两轮 | 沿用 `BeginRun` 语义：新轮次取消旧轮次 |
| 宿主进程崩溃 | 该轮丢失，`active` 无记录 |
| 多标签页同时打开同一会话 | 广播：两个订阅者各自收到完整序列 |

## 8. 二期：Core 客户端（CLI / Codex 远程模式）

改动集中在 `LubanAgentCore/Client/HttpAgentHost.cs`，CLI / Codex 的 ViewModel 预计不需要改：

1. `RunStreamingAsync` 的 `finally` 区分两种收尾：
   - `ct` 被取消（用户按停止 / UI 关闭）→ 保持现状，调 `/agent/cancel`；
   - 读流出错或 EOF 但没收到 `done` → **不 cancel**，用 `fromSeq`（已 yield 到的帧序号）调 `/agent/attach` 续上，退避重连最多 3 次。
2. 服务端回 `run.snapshot` + `run.resync`（断线期间超出缓冲窗口）时，转换成一个新的 `AgentStreamEvent`（`RunResyncEvent`），由 CLI / Codex 的会话 ViewModel 用整段文本替换当前气泡。
3. 本机模式（不接 WebHost）没有 HTTP 边界，不存在该问题，不改。

一期只定契约、不改 Core 客户端；二期开工时再核对 `AgentStreamEvent` 的既有消费点。

## 9. 风险

| 风险 | 应对 |
|---|---|
| 后台轮次长期持有 DI scope 与宿主实例，可能出现作用域释放遗漏 | 已有先例（`KnowledgeTaskService`）；轮次移除时统一 `DisposeAsync`，并在宿主关闭路径上兜底 |
| 轮次中心与 `AgentWebRuntime` 现有 `_runs` 出现双份登记 | 明确复用：`BeginRun` / `CancelRun` / `EndRun` 委托给 hub，不允许第二张表 |
| 快照内存随正文线性增长 | 与正文同阶，轮次结束即释放；不额外保留历史轮次 |
| 前端接入 `run.snapshot` 时状态重建可能与 `loadHistory` 的结果重复 | `resume()` 在 `loadHistory()` 之后执行、`streaming()` 为 true 时直接跳过；快照到达时复用尾部 streaming 气泡，不新增重复条目 |
| SAEA 的同步写在包内到底是抛异常、阻塞 180s 还是永不返回，只能以实测为准 | 设计不依赖这个结论：后台发布只入队，轮次状态与快照不经过 socket；无论哪种表现，最坏是某个订阅者线程滞留 |
| hub 放 Core 后，WebHost 侧的 SSE 胶水（订阅者 pump、`AgentEventStreamResult`）仍无自动化覆盖 | hub 的快照/缓冲/订阅者判定用 `LubanAgent.Tests` 单测覆盖；SSE 胶水靠验收 2–5 的手动冒烟 |
| 一期上线后「关页面不再等于停止」可能被误认为卡死 | 会话标签维持「进行中」指示；停止按钮与 `/agent/cancel` 不变 |

## 10. 验收

1. `dotnet build LubanAgent.slnx` 与 `dotnet test LubanAgent.Tests/LubanAgent.Tests.csproj` 通过；新增单测覆盖：快照增量累积、环形缓冲滑窗、慢订阅者摘除、`active` 的进行中/终态/无记录三态、`attach` 的 `fromSeq` 分支判定。
2. 手动冒烟：发起一轮长回答 → 中途关掉页面 → 30 秒后重开同一会话 → 断点前内容完整（含工具卡与 notice 条目）、后续继续输出、跑完可滚动看全文。
3. 冒烟同时确认宿主控制台不再刷 `httpServer_OnError ... Sending data timeout`（断连最多留一条摘除日志）。
4. 冒烟「跑完后立刻重开」：直接看到完整回答（验证 `done` 之前的落库已完成）。
5. 冒烟「等待工具确认时关页面」：重开后确认框仍在，点允许后流程继续。
6. 冒烟「删除正在跑的会话」：被拒绝并提示「该会话正在进行中」，会话仍在、轮次继续跑完；停止后再删成功。删除工作区同理。
7. `README.md` / `README.en.md` 的接口表补 `/agent/active`、`/agent/attach` 两行（中英同步）。

## 11. 留给施工的决定

- 缓冲容量、保留期、订阅者队列容量的具体默认值与配置项落点（是 `WebHostSettings` 还是 `appsettings.json`）。
- hub 在 Core，创建用户 scope 的能力得由 WebHost 传入（`Func<DiContainer>` 之类的工厂委托），具体注入形态由施工定。
- 快照的序列化形态（直接复用 DTO，还是匿名对象）。
- 前端 `resume()` 的重试退避具体参数与「进行中」标签的文案。

## 12. 本设计中由我定下的判断（可推翻）

1. **快照是全量的，不是缝隙填充**：`run.snapshot` 到达即重建整条 assistant 气泡（正文/思考/工具/计划/进度/notice/待确认），因为它只在「页面刷新、内存为空」时下发。
2. **`attach` 不服务已结束的轮次**：终态一律 `204`，内容走 `/session/messages`；保留期只让 `active` 能解释终态。
3. **轮次归属会话，不归属发起客户端**：单机单用户下，CLI/Codex 远程模式与网页操作同一会话时互为可见、可停止。若要求隔离，则 hub 键与两个新端点入参都要加客户端标识，属于契约级改动。
4. **`AgentRunHub` 放 `LubanAgentCore`**：只依赖 `IAgentHost` / `DiContainer` / `AgentSseFrame`，进 Core 可被 `LubanAgent.Tests` 直接覆盖，二期客户端可复用；WebHost 只留 SAEA 胶水。
5. **显式取消/失败的部分内容不补写落库**：保持现有回合边界语义，不在本次引入「中断消息」。
6. **删除进行中的会话一律拒绝**（409 + 提示），不做「先取消再删」；`/session/clearMessages` 仍先取消再清空。这条把原先「关页面即取消」顺带兜住的清理责任显式化。