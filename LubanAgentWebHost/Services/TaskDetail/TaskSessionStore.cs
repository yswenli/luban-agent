/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.TaskDetail
*文件名： TaskSessionStore
*描述：任务与宿主工作区会话的有效绑定存储；同一工作项仅保留最新绑定
*****************************************************************************/

using System.Text.Json;
using System.Text.Json.Serialization;

namespace LubanAgentWebHost.Services.TaskDetail;

/// <summary>任务会话状态。</summary>
public enum TaskSessionStatus
{
    /// <summary>未开始。</summary>
    NotStarted,
    /// <summary>会话已创建。</summary>
    Created,
    /// <summary>进行中。</summary>
    Running,
    /// <summary>已完成。</summary>
    Completed,
    /// <summary>已取消。</summary>
    Cancelled,
    /// <summary>失败。</summary>
    Failed
}

/// <summary>任务与宿主会话的有效绑定；不保存会话内的方案批准状态。</summary>
public sealed class TaskSession
{
    /// <summary>绑定 ID。</summary>
    public string LinkId { get; set; } = string.Empty;
    /// <summary>云效工作项 ID。</summary>
    public string WorkItemId { get; set; } = string.Empty;
    /// <summary>宿主工作区 ID。</summary>
    public string WorkspaceId { get; set; } = string.Empty;
    /// <summary>宿主会话 ID。</summary>
    public string SessionId { get; set; } = string.Empty;
    /// <summary>首轮输入摘要。</summary>
    public string SeedDigest { get; set; } = string.Empty;
    /// <summary>首轮带入评论数。</summary>
    public int SeedCommentCount { get; set; }
    /// <summary>首轮输入发送时间。</summary>
    public DateTime SeedAt { get; set; }
    /// <summary>最近活跃时间。</summary>
    public DateTime? LastActiveAt { get; set; }
    /// <summary>绑定状态。</summary>
    public TaskSessionStatus Status { get; set; }
    /// <summary>结果摘要与产物清单。</summary>
    public string? Result { get; set; }
}

/// <summary>任务会话绑定的 JSON 文件存储。</summary>
public sealed class TaskSessionStore
{
    private const string DefaultFileName = "yunxiao-task-sessions.json";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>创建绑定存储；测试必须传临时文件路径。</summary>
    public TaskSessionStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath() : filePath!;
    }

    /// <summary>默认绑定文件路径：<c>~/.luban-agent/webhost/yunxiao-task-sessions.json</c>。</summary>
    public static string DefaultFilePath() => Path.Combine(AgentHome.WebHostDirectory, DefaultFileName);

    /// <summary>读取任务当前有效绑定。</summary>
    public async Task<TaskSession?> GetAsync(string? workitemId)
    {
        if (string.IsNullOrWhiteSpace(workitemId)) return null;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var sessions = await LoadCoreAsync().ConfigureAwait(false);
            return sessions.FirstOrDefault(s => string.Equals(s.WorkItemId, workitemId.Trim(), StringComparison.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>读取全部有效绑定，按最近活跃时间倒序。</summary>
    public async Task<IReadOnlyList<TaskSession>> ListAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var sessions = await LoadCoreAsync().ConfigureAwait(false);
            return sessions.OrderByDescending(s => s.LastActiveAt ?? s.SeedAt).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>新增或替换任务的有效绑定；不操作宿主会话本身。</summary>
    public async Task UpsertAsync(TaskSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.WorkItemId))
            throw new ArgumentException("工作项 ID 不能为空", nameof(session));

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var sessions = await LoadCoreAsync().ConfigureAwait(false);
            var index = sessions.FindIndex(s => string.Equals(s.WorkItemId, session.WorkItemId.Trim(), StringComparison.Ordinal));
            if (index < 0) sessions.Add(session);
            else sessions[index] = session;
            await SaveCoreAsync(sessions).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<TaskSession>> LoadCoreAsync()
    {
        if (!File.Exists(_filePath)) return [];
        var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<TaskSession>>(json, _json) ?? [];
    }

    private async Task SaveCoreAsync(List<TaskSession> sessions)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var tmp = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(sessions, _json)).ConfigureAwait(false);
        File.Move(tmp, _filePath, overwrite: true);
    }
}
