/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentWebHost.Services.Init
*文件名： ISessionGateway
*描述：云效 S2 项目初始化会话通路收口契约（设计 §4.3，R-S2-1 的隔离层）——
*      建专用会话 / 下发装配工具的指令（内部 Chat SSE 通道）/ 工具确认 / 最终回执。
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §4.3
*      实测记录（2026-11-18，最小回路 R-S2-1）见 SessionGateway 帧解析注释。
*
*****************************************************************************/

namespace LubanAgentWebHost.Services.Init;

/// <summary>
/// 一次指令跑完后的最终回执。
/// </summary>
/// <remarks>
/// 字段口径钉自 R-S2-1 最小回路实测：
/// <c>done</c> 帧 <c>{ sessionId }</c> 为成功终态；<c>error</c> 帧 <c>{ message }</c> 为失败；
/// <c>tool.denied</c> / <c>tool.failed</c> 表示预批外工具调用被拒（按「失败该库」语义计 <see cref="Ok"/>=false）。
/// </remarks>
/// <param name="Ok">是否成功收尾（done 终态且无错误/无拒绝）。</param>
/// <param name="Summary">助手正文摘要（<c>text.delta</c> 拼接，可能为空）。</param>
/// <param name="Error">失败原因（<c>error</c> 帧 / <c>tool.failed</c> 汇总；成功时为 null）。</param>
/// <param name="SessionId">实际会话标识（取自 <c>session</c>/<c>done</c> 帧）。</param>
/// <param name="RunId">本轮标识（取自 <c>session</c> 帧 <c>runId</c>）。</param>
/// <param name="Denied">是否出现过 <c>tool.denied</c>（预批策略拒绝过工具调用）。</param>
public sealed record SessionReceipt(
    bool Ok,
    string? Summary,
    string? Error,
    string? SessionId = null,
    string? RunId = null,
    bool Denied = false);

/// <summary>
/// <c>tool.confirm</c> 帧的关键字段（预批策略入参）。
/// </summary>
/// <remarks>
/// 帧载荷实测形态（R-S2-1）：
/// <c>{"confirmationId":"…","toolName":"RunShellAsync","arguments":{"command":"…","workingDirectory":"…"},"timeoutSeconds":60}</c>；
/// 注意 <c>tool.started</c> 帧的 <c>name</c> 是 <c>RunShell</c>，与本帧 <c>toolName</c>（<c>RunShellAsync</c>）不一致，
/// 策略匹配以 <see cref="Command"/>（命令文本）为准，不依赖工具名。
/// </remarks>
/// <param name="ConfirmationId">确认标识（回传 /agent/confirm 用）。</param>
/// <param name="ToolName">工具名（如 <c>RunShellAsync</c>）。</param>
/// <param name="Command">工具参数里的命令文本（<c>arguments.command</c>；无则 null）。</param>
public sealed record ToolConfirmCall(string ConfirmationId, string ToolName, string? Command);

/// <summary>
/// 云效项目初始化会话通路收口（设计 §4.3）。
/// </summary>
/// <remarks>
/// <para>
/// 实现口径（<see cref="SessionGateway"/>）：经 <c>WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId)</c>
/// 解析底层服务（与 <c>WebAgentService</c> 壳同款取 DI），<b>in-process 调用、不走 HTTP 自环</b>。
/// </para>
/// <para>
/// 壳层（批次 D 的 WebYunxiaoService / Controller）怎么用：
/// ① <see cref="CreateInitSessionAsync"/> 建一次性初始化会话（留存回执）；
/// ② <see cref="SendAsync"/> 逐条下发 git 指令序列（<c>approvedCommands</c> 传预检产出的
/// <c>GitStep.Command</c> 清单；命中预批清单的 <c>tool.confirm</c> 逐条放行，偏离即 deny 并由调用方失败该库）；
/// ③ <see cref="WaitReceiptAsync"/> 取最终回执写入任务态。
/// 人工确认通道仍走 <see cref="ConfirmAsync"/>（与界面 /agent/confirm 同一确认机制，不用 bypass 绕过）。
/// </para>
/// </remarks>
public interface ISessionGateway
{
    /// <summary>
    /// 新建专用初始化会话（留存）。
    /// </summary>
    /// <param name="workspaceId">绑定的工作区标识。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新会话标识。</returns>
    Task<string> CreateInitSessionAsync(string workspaceId, CancellationToken ct);

    /// <summary>
    /// 会话通路轻量探测（不建会话、不发指令）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>通路可用返回 true（manifests 的 <c>sessionReady</c> 用）。</returns>
    Task<bool> ProbeAsync(CancellationToken ct);

    /// <summary>
    /// 下发一条指令并消费「装配工具的对话」事件流到终态。
    /// </summary>
    /// <remarks>
    /// 必须走 Chat/SSE 通道（<c>EnableTools=true</c> 的流式轮次）——非流式 Send 不装配工具（已核实）。
    /// 期间 <c>tool.confirm</c> 帧按预批策略处理：<paramref name="approvedCommands"/> 内命中 → 逐条放行（allow），
    /// 否则 deny。<b>用 allow 而非 allowAll</b>：框架的 allowAll 是「本轮同类工具免确认」，
    /// 会把后续偏离清单的调用一并放过（绕过预批红线），故逐帧逐条判定。
    /// </remarks>
    /// <param name="sessionId">目标会话。</param>
    /// <param name="instruction">指令文本。</param>
    /// <param name="approvedCommands">预批命令清单（预检产出的 <c>GitStep.Command</c>）；null/空 = 一律 deny。</param>
    /// <param name="ct">取消令牌。</param>
    Task SendAsync(string sessionId, string instruction, IReadOnlyList<string>? approvedCommands, CancellationToken ct);

    /// <summary>
    /// 回传工具确认结果（人工通道与预批策略共用同一机制）。
    /// </summary>
    /// <param name="sessionId">所属会话。</param>
    /// <param name="confirmationId">确认标识（取自 <c>tool.confirm</c> 帧）。</param>
    /// <param name="result">allow / deny / allowAll / allowSession。</param>
    /// <param name="ct">取消令牌。</param>
    Task ConfirmAsync(string sessionId, string confirmationId, string result, CancellationToken ct);

    /// <summary>
    /// 取最终回执：SSE 终态折叠结果优先，缺失时回读会话消息兜底。
    /// </summary>
    /// <param name="sessionId">目标会话。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>回执。</returns>
    Task<SessionReceipt> WaitReceiptAsync(string sessionId, CancellationToken ct);
}
