/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Services
*文件名： SessionAutoNameRunner
*描述：会话自动命名的触发判定与落库：仅首条消息、仅默认标题，成功后广播标题变更
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>会话标题变更通知。</summary>
/// <param name="SessionId">会话标识。</param>
/// <param name="Title">新标题。</param>
public sealed record SessionTitleChanged(string SessionId, string Title);

/// <summary>
/// 会话标题变更的进程内广播。
/// </summary>
/// <remarks>
/// 三端（Web / TUI / 桌面）都在本进程内运行，用一个单例事件即可让界面同步刷新；
/// 订阅方各自负责封送到自己的 UI 线程。
/// </remarks>
public sealed class SessionTitleNotifier
{
    /// <summary>标题变更时触发。</summary>
    public event Action<SessionTitleChanged>? Changed;

    /// <summary>广播一次标题变更；订阅方异常不影响其他订阅方与调用方。</summary>
    public void Publish(SessionTitleChanged change)
    {
        var handlers = Changed;
        if (handlers is null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<SessionTitleChanged>)handler)(change);
            }
            catch (Exception ex)
            {
                Logger.Warn($"会话标题变更通知失败：sessionId={change.SessionId}, error={ex.Message}");
            }
        }
    }
}

/// <summary>自动命名需要的会话数据访问缝（便于脱离数据库做单测）。</summary>
public interface IAutoTitleStore
{
    /// <summary>
    /// 取会话当前的标题与消息数。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="userId">归属用户。</param>
    /// <returns>会话不存在或已删除时返回 null。</returns>
    Task<(string? Title, int MessageCount)?> GetAutoTitleStateAsync(string sessionId, string userId);

    /// <summary>
    /// 仅当标题仍是默认值时覆盖标题。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="userId">归属用户。</param>
    /// <param name="defaultTitles">视为“未命名”的标题集合。</param>
    /// <param name="title">新标题。</param>
    /// <returns>是否真的更新了（标题已被改成别的值时为 false）。</returns>
    Task<bool> UpdateTitleIfDefaultAsync(
        string sessionId, string userId, IReadOnlyList<string> defaultTitles, string title);
}

/// <summary>
/// 自动命名的触发判定与落库。
/// </summary>
/// <remarks>
/// “仅首次”按“会话还没有任何消息”判定，而不是只看标题：用户手动改名后又改回默认文案时不会被二次覆盖。
/// 调用方负责放到后台执行，本类只做一次完整的判定 + 生成 + 写入。
/// </remarks>
public sealed class SessionAutoNameRunner(
    IAutoTitleStore store,
    ISessionAutoNamer namer,
    SessionTitleNotifier? notifier = null)
{
    /// <summary>被视为“未命名”的默认标题；三端历史上出现过这三种写法。</summary>
    public static IReadOnlyList<string> DefaultTitles { get; } = ["新对话", "新会话", "未命名"];

    /// <summary>标题是否仍是默认值。</summary>
    public static bool IsDefaultTitle(string? title)
        => !string.IsNullOrWhiteSpace(title)
           && DefaultTitles.Contains(title.Trim(), StringComparer.Ordinal);

    /// <summary>
    /// 首条消息触发一次自动命名。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="userId">归属用户。</param>
    /// <param name="text">首条用户消息原文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>写入的新标题；未触发或失败时返回 null。</returns>
    public async Task<string?> RunAsync(string sessionId, string userId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(text)) return null;

        var state = await store.GetAutoTitleStateAsync(sessionId, userId).ConfigureAwait(false);
        if (state is not { } current) return null;

        // 只要已经有过消息（含用户重试/多端并发导致的第二次进入），就不再改名
        if (current.MessageCount > 0 || !IsDefaultTitle(current.Title)) return null;

        var title = await namer.AutoSessionNameAsync(sessionId, text, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(title)) return null;

        // 条件更新兜底：命不中说明用户在这期间手动改了名，放弃覆盖
        if (!await store.UpdateTitleIfDefaultAsync(sessionId, userId, DefaultTitles, title).ConfigureAwait(false))
        {
            return null;
        }

        notifier?.Publish(new SessionTitleChanged(sessionId, title));
        return title;
    }
}