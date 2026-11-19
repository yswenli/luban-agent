/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentWebHost.Services.Init
*文件名： InitTaskStore
*描述：云效 S2 项目初始化任务态落盘（设计 §4.4，D6）——
*      JSON 状态文件 + 原子写（tmp + Move 替换），路径可注入；
*      同代号互斥（TryBegin）、部分重跑留痕（Upsert 按 Repo 合并 Libs）。
*口径：docs/实施设计-云效DevOps工作台Web插件-S2.md §4.4
*
*****************************************************************************/

using System.Text.Json;
using System.Text.Json.Serialization;

namespace LubanAgentWebHost.Services.Init;

/// <summary>单库复制状态（wire 值：pending / cloning / renaming / pushing / done / failed）。</summary>
public enum LibStatus
{
    /// <summary>待处理。</summary>
    Pending,

    /// <summary>克隆母版中。</summary>
    Cloning,

    /// <summary>子目录改名中。</summary>
    Renaming,

    /// <summary>推送新库中。</summary>
    Pushing,

    /// <summary>完成。</summary>
    Done,

    /// <summary>失败（Detail 脱敏记录原因）。</summary>
    Failed,
}

/// <summary>任务级状态（wire 值：pending / running / done / failed）。</summary>
public enum InitTaskStatus
{
    /// <summary>待处理。</summary>
    Pending,

    /// <summary>进行中（TryBegin 占位即此态）。</summary>
    Running,

    /// <summary>全部收口成功。</summary>
    Done,

    /// <summary>以失败收口。</summary>
    Failed,
}

/// <summary>单库状态条目。</summary>
public sealed class LibState
{
    /// <summary>仓库名（如 <c>NEWX-Mixed</c>；合并键）。</summary>
    public string Repo { get; set; } = "";

    /// <summary>新库 id（建库成功后回填；供流水线区 <c>newRepoId</c> 取用）。</summary>
    public string? RepoId { get; set; }

    /// <summary>状态。</summary>
    public LibStatus Status { get; set; } = LibStatus.Pending;

    /// <summary>细节（失败原因等；必须脱敏，禁止 PAT 明文）。</summary>
    public string? Detail { get; set; }
}

/// <summary>一次项目初始化任务。</summary>
public sealed class InitTask
{
    /// <summary>任务标识。</summary>
    public string Id { get; set; } = "";

    /// <summary>新项目代号（互斥键）。</summary>
    public string Codename { get; set; } = "";

    /// <summary>任务级状态。</summary>
    public InitTaskStatus Status { get; set; } = InitTaskStatus.Pending;

    /// <summary>逐库状态（按 <see cref="LibState.Repo"/> 合并留痕）。</summary>
    public List<LibState> Libs { get; set; } = new();

    /// <summary>绑定工作区。</summary>
    public string? WorkspaceId { get; set; }

    /// <summary>初始化会话（回执追溯用）。</summary>
    public string? SessionId { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>收口时间；null = 进行中。</summary>
    public DateTime? FinishedAt { get; set; }
}

/// <summary>
/// 任务态 JSON 文件存储（单宿主进程、原子写）。
/// </summary>
/// <remarks>
/// <para>
/// 默认文件：<c>%LocalAppData%/LuBan/AIAgent/webhost/yunxiao-init-tasks.json</c>
/// （与 <c>WebPluginPaths.DefaultStateFilePath</c> 同目录先例）；
/// <b>测试必须传临时路径</b>（红线：单测禁止写真实 LocalApplicationData）。
/// </para>
/// <para>
/// 终态判定（TryBegin 互斥用）：<c>FinishedAt != null</c> 且所有 Lib 均 <c>done/failed</c>。
/// 「FinishedAt 已置但 Lib 仍停中间态」按非终态处理——崩溃恢复语义：上一轮没跑完不许开新一轮。
/// </para>
/// </remarks>
public sealed class InitTaskStore
{
    /// <summary>默认状态文件名。</summary>
    private const string DefaultFileName = "yunxiao-init-tasks.json";

    /// <summary>读写互斥（单宿主进程内并发安全）。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly string _filePath;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>创建任务态存储。</summary>
    /// <param name="filePath">状态文件路径；null 用默认（<c>%LocalAppData%/LuBan/AIAgent/webhost/yunxiao-init-tasks.json</c>）。测试必须传临时路径。</param>
    public InitTaskStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath() : filePath!;
    }

    /// <summary>默认状态文件路径。</summary>
    public static string DefaultFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LuBan",
        "AIAgent",
        "webhost",
        DefaultFileName);

    /// <summary>按任务标识取任务。</summary>
    /// <param name="id">任务标识。</param>
    /// <returns>任务；不存在返回 null。</returns>
    public async Task<InitTask?> GetAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var tasks = await LoadAsync().ConfigureAwait(false);
        return tasks.FirstOrDefault(t => string.Equals(t.Id, id.Trim(), StringComparison.Ordinal));
    }

    /// <summary>按代号取任务列表（含历史）。</summary>
    /// <param name="codename">项目代号。</param>
    /// <returns>同代号任务（按创建时间升序）。</returns>
    public async Task<IReadOnlyList<InitTask>> ListByCodenameAsync(string codename)
    {
        if (string.IsNullOrWhiteSpace(codename))
        {
            return Array.Empty<InitTask>();
        }

        var tasks = await LoadAsync().ConfigureAwait(false);
        return tasks
            .Where(t => string.Equals(t.Codename, codename.Trim(), StringComparison.Ordinal))
            .OrderBy(t => t.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// 写入（新增或更新）任务；同 Id 更新时 <b>按 Repo 合并 Libs</b>：
    /// 传入条目覆盖同名库，未提及的历史库保留（部分重跑留痕）。
    /// </summary>
    /// <param name="task">任务。</param>
    public async Task UpsertAsync(InitTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrWhiteSpace(task.Id))
        {
            throw new ArgumentException("任务标识不能为空", nameof(task));
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var tasks = await LoadCoreAsync().ConfigureAwait(false);
            var index = tasks.FindIndex(t => string.Equals(t.Id, task.Id, StringComparison.Ordinal));
            if (index < 0)
            {
                tasks.Add(task);
            }
            else
            {
                var existing = tasks[index];
                foreach (var lib in task.Libs)
                {
                    var hit = existing.Libs.FirstOrDefault(l =>
                        string.Equals(l.Repo, lib.Repo, StringComparison.Ordinal));
                    if (hit is null)
                    {
                        existing.Libs.Add(lib);
                    }
                    else
                    {
                        hit.Status = lib.Status;
                        hit.Detail = lib.Detail;
                        // RepoId 只进不出：重跑未重建库时保留既有 id
                        if (!string.IsNullOrWhiteSpace(lib.RepoId))
                            hit.RepoId = lib.RepoId;
                    }
                }

                existing.Codename = task.Codename;
                existing.Status = task.Status;
                existing.WorkspaceId = task.WorkspaceId;
                existing.SessionId = task.SessionId;
                existing.FinishedAt = task.FinishedAt;
                tasks[index] = existing;
            }

            await SaveCoreAsync(tasks).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 同代号开新任务的互斥入口：存在非终态任务（无 FinishedAt 或任一 Lib 非 done/failed）→ false；
    /// 否则写入「进行中」占位（<see cref="InitTaskStatus.Running"/>）并返回 true，
    /// 由调用方后续 <see cref="UpsertAsync"/> 补全（占位 Id 取 <see cref="ListByCodenameAsync"/> 里无 FinishedAt 的条目）。
    /// </summary>
    /// <param name="codename">项目代号。</param>
    /// <returns>是否成功占位。</returns>
    public async Task<bool> TryBeginAsync(string codename)
    {
        if (string.IsNullOrWhiteSpace(codename))
        {
            return false;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var tasks = await LoadCoreAsync().ConfigureAwait(false);
            var trimmed = codename.Trim();
            if (tasks.Any(t => string.Equals(t.Codename, trimmed, StringComparison.Ordinal) && IsNonTerminal(t)))
            {
                return false;
            }

            tasks.Add(new InitTask
            {
                Id = Guid.NewGuid().ToString("N"),
                Codename = trimmed,
                Status = InitTaskStatus.Running,
                CreatedAt = DateTime.Now,
            });
            await SaveCoreAsync(tasks).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>是否非终态（无 FinishedAt 或任一 Lib 非 done/failed）。</summary>
    private static bool IsNonTerminal(InitTask task)
        => task.FinishedAt is null
            || task.Libs.Any(l => l.Status is not (LibStatus.Done or LibStatus.Failed));

    /// <summary>读全量（损坏 JSON 当空；调用方须持 <see cref="_gate"/> 时用 LoadCoreAsync）。</summary>
    private async Task<List<InitTask>> LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>读全量（不加锁）。</summary>
    private async Task<List<InitTask>> LoadCoreAsync()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new List<InitTask>();
            }

            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<InitTask>>(json, _json) ?? new List<InitTask>();
        }
        catch (JsonException)
        {
            // 损坏 JSON 当空（宿主重启后自动重建任务态，不因脏文件拒服务）
            return new List<InitTask>();
        }
        catch (IOException)
        {
            return new List<InitTask>();
        }
    }

    /// <summary>原子写（写 tmp 再 Move 替换；不加锁）。</summary>
    private async Task SaveCoreAsync(List<InitTask> tasks)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmp = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(tasks, _json)).ConfigureAwait(false);
        File.Move(tmp, _filePath, overwrite: true);
    }
}
