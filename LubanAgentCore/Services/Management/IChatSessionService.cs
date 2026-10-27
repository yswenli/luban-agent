namespace LubanAgentCore.Services.Management;

/// <summary>
/// 会话管理。
/// 方法面 = 现有 <c>/session</c> 命令与 Codex 会话列表的能力，不新增能力。
/// </summary>
public interface IChatSessionService
{
    /// <summary>
    /// 当前用户的会话列表。
    /// </summary>
    /// <param name="workspaceId">工作区标识；为 null 时返回当前用户的全部会话。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<SessionInfo>> ListAsync(string? workspaceId = null, CancellationToken ct = default);

    /// <summary>新建会话（可绑定工作区）。</summary>
    Task<SessionInfo> CreateAsync(string? title, string? workspaceId, CancellationToken ct = default);

    /// <summary>删除会话及其消息。</summary>
    Task DeleteAsync(string sessionId, CancellationToken ct = default);

    /// <summary>重命名会话。</summary>
    Task RenameAsync(string sessionId, string title, CancellationToken ct = default);

    /// <summary>读取会话消息（含附件元数据）。</summary>
    Task<IReadOnlyList<SessionMessage>> GetMessagesAsync(string sessionId, CancellationToken ct = default);

    /// <summary>清空会话消息（保留会话本身）。</summary>
    Task ClearMessagesAsync(string sessionId, CancellationToken ct = default);

    /// <summary>切换当前会话；调用方随后需重建会话运行时。</summary>
    Task SwitchAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// 物理删除当前用户的全部会话及消息。
    /// </summary>
    /// <param name="workspaceId">清空后用于重建「新会话」的工作区标识；为 null 时不重建。</param>
    /// <param name="ct">取消令牌。</param>
    Task ClearAllAsync(string? workspaceId = null, CancellationToken ct = default);
}