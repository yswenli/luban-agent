/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentWebHost.Services.Init
*文件名： ProjectInitStore
*描述：项目初始化完成登记（「已初始化」判定依据）——
*      代码库任务收口且三端流水线就绪后落盘一条记录，
*      项目列表按 项目 ID / 项目标识 反查并标注状态。
*      与 InitTaskStore 同目录、同为原子写 JSON。
*
*****************************************************************************/

using System.Text.Json;
using System.Text.Json.Serialization;

namespace LubanAgentWebHost.Services.Init;

/// <summary>单端流水线登记条目。</summary>
public sealed class ProjectInitPipeline
{
    /// <summary>端名（mobile / admin / backend）。</summary>
    public string End { get; set; } = "";

    /// <summary>该端是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>流水线名。</summary>
    public string? Name { get; set; }

    /// <summary>母版流水线名（再次进入初始化页反显用）。</summary>
    public string? TemplateName { get; set; }

    /// <summary>该端在代码库内的目录。</summary>
    public string? Dir { get; set; }

    /// <summary>后端监听端口（非后端端为 null）。</summary>
    public int? Port { get; set; }

    /// <summary>云效流水线 id（已存在跳过或新建成功后回填）。</summary>
    public string? PipelineId { get; set; }
}

/// <summary>一次项目初始化的完成登记。</summary>
public sealed class ProjectInitRecord
{
    /// <summary>云效项目 ID（优先匹配键；未知时为空）。</summary>
    public string ProjectId { get; set; } = "";

    /// <summary>项目标识（云效 customCode，如 BBKY；ProjectId 未知时按它匹配）。</summary>
    public string? Identifier { get; set; }

    /// <summary>新项目代号（初始化时的 newCode）。</summary>
    public string Codename { get; set; } = "";

    /// <summary>初始化使用的宿主工作区 ID。</summary>
    public string? WorkspaceId { get; set; }

    /// <summary>母版库代号（如 TJCES，再次进入初始化页反显用）。</summary>
    public string? TemplateCode { get; set; }

    /// <summary>选中的母版库（原库名列表）。</summary>
    public List<string> SelectedRepos { get; set; } = new();

    /// <summary>代码组名（共享设置）。</summary>
    public string? GroupName { get; set; }

    /// <summary>初始化分支。</summary>
    public string? Branch { get; set; }

    /// <summary>绑定的代码库名（如 BBKY-Mixed）。</summary>
    public string? RepoName { get; set; }

    /// <summary>三端流水线登记。</summary>
    public List<ProjectInitPipeline> Pipelines { get; set; } = new();

    /// <summary>完成时间。</summary>
    public DateTime CompletedAt { get; set; }
}

/// <summary>
/// 初始化完成登记的 JSON 文件存储（单宿主进程、原子写）。
/// </summary>
/// <remarks>
/// 默认文件：<c>%LocalAppData%/LuBan/AIAgent/webhost/yunxiao-project-init.json</c>
/// （与 <see cref="InitTaskStore"/> 同目录）；测试必须传临时路径。
/// </remarks>
public sealed class ProjectInitStore
{
    /// <summary>默认状态文件名。</summary>
    private const string DefaultFileName = "yunxiao-project-init.json";

    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly string _filePath;

    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>创建登记存储。</summary>
    /// <param name="filePath">状态文件路径；null 用默认（<c>%LocalAppData%/LuBan/AIAgent/webhost/yunxiao-project-init.json</c>）。测试必须传临时路径。</param>
    public ProjectInitStore(string? filePath = null)
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

    /// <summary>读全部登记（损坏 JSON 当空）。</summary>
    /// <returns>登记列表。</returns>
    public async Task<IReadOnlyList<ProjectInitRecord>> GetAllAsync()
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

    /// <summary>
    /// 写入（新增或更新）：先按 <see cref="ProjectInitRecord.ProjectId"/> 匹配，
    /// 项目 ID 未知时按 <see cref="ProjectInitRecord.Identifier"/> 匹配；命中即覆盖。
    /// </summary>
    /// <param name="record">完成登记。</param>
    public async Task UpsertAsync(ProjectInitRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.ProjectId) && string.IsNullOrWhiteSpace(record.Identifier))
        {
            throw new ArgumentException("项目 ID / 项目标识至少提供一个", nameof(record));
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = await LoadCoreAsync().ConfigureAwait(false);
            var index = records.FindIndex(r => SameProject(r, record));
            if (index < 0)
            {
                records.Add(record);
            }
            else
            {
                // 项目 ID / 标识只进不出：后续登记缺字段时保留既有值
                if (string.IsNullOrWhiteSpace(record.ProjectId))
                {
                    record.ProjectId = records[index].ProjectId;
                }

                if (string.IsNullOrWhiteSpace(record.Identifier))
                {
                    record.Identifier = records[index].Identifier;
                }

                records[index] = record;
            }

            await SaveCoreAsync(records).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 按项目 ID（优先）或项目标识反查完成登记。
    /// </summary>
    /// <param name="projectId">云效项目 ID。</param>
    /// <param name="identifier">项目标识（customCode）。</param>
    /// <returns>命中登记；未登记返回 null。</returns>
    public async Task<ProjectInitRecord?> FindAsync(string? projectId, string? identifier)
    {
        if (string.IsNullOrWhiteSpace(projectId) && string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = await LoadCoreAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(projectId))
            {
                var byId = records.FirstOrDefault(r =>
                    !string.IsNullOrWhiteSpace(r.ProjectId)
                    && string.Equals(r.ProjectId, projectId, StringComparison.Ordinal));
                if (byId is not null)
                {
                    return byId;
                }
            }

            return string.IsNullOrWhiteSpace(identifier)
                ? null
                : records.FirstOrDefault(r =>
                    !string.IsNullOrWhiteSpace(r.Identifier)
                    && string.Equals(r.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>是否同一项目（非空 ID 优先，其次非空标识）。</summary>
    private static bool SameProject(ProjectInitRecord left, ProjectInitRecord right)
    {
        if (!string.IsNullOrWhiteSpace(left.ProjectId) && !string.IsNullOrWhiteSpace(right.ProjectId))
        {
            return string.Equals(left.ProjectId, right.ProjectId, StringComparison.Ordinal);
        }

        return !string.IsNullOrWhiteSpace(left.Identifier)
            && !string.IsNullOrWhiteSpace(right.Identifier)
            && string.Equals(left.Identifier, right.Identifier, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<ProjectInitRecord>> LoadCoreAsync()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new List<ProjectInitRecord>();
            }

            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<ProjectInitRecord>>(json, _json) ?? new List<ProjectInitRecord>();
        }
        catch (JsonException)
        {
            return new List<ProjectInitRecord>();
        }
        catch (IOException)
        {
            return new List<ProjectInitRecord>();
        }
    }

    /// <summary>原子写（写 tmp 再 Move 替换；不加锁）。</summary>
    private async Task SaveCoreAsync(List<ProjectInitRecord> records)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmp = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(records, _json)).ConfigureAwait(false);
        File.Move(tmp, _filePath, overwrite: true);
    }
}
