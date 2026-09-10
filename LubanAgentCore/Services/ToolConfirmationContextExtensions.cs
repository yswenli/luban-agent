/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： ToolConfirmationContextExtensions
*版本号： V1.0.0.0
*唯一标识：工具确认上下文装配扩展
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/10
*描述：每轮对话的工具确认上下文装配，CLI 与 GUI 共用，
*统一设置权限模式、取消令牌、工作区路径检查器与 Plan 计划项回调，
*消除两端各自装配导致的行为分歧。
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions;

namespace LubanAgentCore.Services;

/// <summary>
/// <see cref="ToolConfirmationContext"/> 的每轮装配扩展。
/// </summary>
public static class ToolConfirmationContextExtensions
{
    /// <summary>
    /// 为一轮对话装配确认上下文。
    /// 权限模式的实际策略由框架 ToolConfirmationService 统一分发：
    /// Bypass 全部放行、Plan 只记录计划项且不执行、AcceptEdits 放行编辑类、Default 逐项确认。
    /// 因此确认回调可无条件传入，无需宿主按模式提前省略。
    /// </summary>
    /// <param name="context">DI 单例确认上下文。</param>
    /// <param name="mode">本轮权限模式。</param>
    /// <param name="cancellationToken">取消令牌（Esc 中断时拒绝后续工具调用）。</param>
    /// <param name="confirmCallback">确认回调；仅在需要人工确认时被框架调用。</param>
    /// <param name="onPlannedAction">Plan 模式计划项回调，宿主用于收集或即时展示。</param>
    /// <remarks>
    /// 框架已把 <see cref="ToolConfirmationContext.Callback"/> 异步化为 <c>Func&lt;.., Task&lt;bool&gt;&gt;`，
    /// 但两端宿主目前仍以阻塞方式等待 UI 确认（CLI 用 ManualResetEventSlim、GUI 同理），
    /// 故此处集中把同步回调包装成 Task，行为与升级前一致；
    /// 后续若要真正不占用 agent 线程，应把宿主确认改为 TaskCompletionSource 再直接传异步回调。
    /// </remarks>
    public static void ConfigureForTurn(
        this ToolConfirmationContext context,
        ToolPermissionMode mode,
        CancellationToken cancellationToken,
        Func<string, IReadOnlyDictionary<string, object?>, bool>? confirmCallback,
        Action<string, IReadOnlyDictionary<string, object?>>? onPlannedAction = null)
    {
        context.Mode = mode;
        context.CancellationToken = cancellationToken;
        context.WorkspacePathChecker = static path => WorkspaceManager.IsWithinWorkspace(path);
        context.Callback = confirmCallback is null
            ? null
            : (toolName, args) => Task.FromResult(confirmCallback(toolName, args));
        context.OnPlannedAction = onPlannedAction;
    }
}
