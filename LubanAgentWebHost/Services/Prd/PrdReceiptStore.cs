/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.Prd
*文件名： PrdReceiptStore
*描述：PRD 拆分提交的幂等回执账本（按项目 JSON 落盘）
*
*****************************************************************************/

using System.Text.Json;

namespace LubanAgentWebHost.Services.Prd;

/// <summary>
/// 幂等回执账本存储。
/// </summary>
/// <remarks>
/// 落盘位置与项目初始化登记同族：<c>~/.luban-agent/webhost/yunxiao-prd/&lt;projectId&gt;.json</c>。
/// 账本只保存「幂等键 → 云效对象 ID」与回执明细，用于失败项重跑时跳过已成功项；
/// 读取失败（首次提交 / 文件损坏）一律视为空账本，不阻断提交。
/// </remarks>
public sealed class PrdReceiptStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _root;

    /// <summary>
    /// 使用默认目录创建账本存储。
    /// </summary>
    public PrdReceiptStore()
        : this(DefaultRoot)
    {
    }

    /// <summary>
    /// 默认账本根目录：<c>~/.luban-agent/webhost/yunxiao-prd</c>。
    /// </summary>
    public static string DefaultRoot => AgentHome.PrdReceiptDirectory;

    /// <summary>
    /// 使用指定目录创建账本存储（单测注入临时目录）。
    /// </summary>
    /// <param name="root">账本根目录。</param>
    public PrdReceiptStore(string root)
    {
        _root = string.IsNullOrWhiteSpace(root)
            ? throw new ArgumentException("账本目录不能为空", nameof(root))
            : root;
    }

    /// <summary>
    /// 读取项目账本。
    /// </summary>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账本；不存在或读取失败时返回空账本。</returns>
    public async Task<PrdReceiptLedger> LoadAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(projectId);
        if (!File.Exists(path))
            return new PrdReceiptLedger { ProjectId = projectId };

        try
        {
            await using var stream = File.OpenRead(path);
            var ledger = await JsonSerializer.DeserializeAsync<PrdReceiptLedger>(stream, JsonOpts, cancellationToken)
                .ConfigureAwait(false);
            return ledger ?? new PrdReceiptLedger { ProjectId = projectId };
        }
        catch (Exception)
        {
            return new PrdReceiptLedger { ProjectId = projectId };
        }
    }

    /// <summary>
    /// 保存项目账本（同键覆盖，保留最新）。
    /// </summary>
    /// <param name="ledger">账本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SaveAsync(PrdReceiptLedger ledger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        Directory.CreateDirectory(_root);
        ledger.UpdatedAt = DateTimeOffset.Now;

        var path = GetPath(ledger.ProjectId);
        var temp = path + ".tmp";

        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, ledger, JsonOpts, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// 账本文件路径（项目 ID 做文件名净化，防目录穿越）。
    /// </summary>
    /// <param name="projectId">项目 ID。</param>
    /// <returns>绝对路径。</returns>
    internal string GetPath(string projectId)
        => Path.Combine(_root, $"{Sanitize(projectId)}.json");

    /// <summary>
    /// 文件名净化（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>仅保留字母、数字、下划线与连字符。</returns>
    internal static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var chars = value.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_')
            .ToArray();

        var result = new string(chars);
        return result.Length > 80 ? result[..80] : result;
    }
}
