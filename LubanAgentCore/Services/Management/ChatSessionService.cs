namespace LubanAgentCore.Services.Management;

/// <summary>会话管理实现。</summary>
public sealed class ChatSessionService : IChatSessionService
{
    private const string Source = "SessionCommand / Codex 会话列表";

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionInfo>> ListAsync(CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task<SessionInfo> CreateAsync(string? title, string? workspaceId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task RenameAsync(string sessionId, string title, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionMessage>> GetMessagesAsync(string sessionId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task ClearMessagesAsync(string sessionId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);

    /// <inheritdoc />
    public Task SwitchAsync(string sessionId, CancellationToken ct = default)
        => throw NotYetImplemented.For("Task 17", Source);
}