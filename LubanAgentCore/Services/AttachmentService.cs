using LuBan.AIAgent.Attachments;
using LuBan.AIAgent.Sessions;
using LubanAgentCore.Configuration;
using LubanAgentCore.Models;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LubanAgentCore.Services;

/// <summary>
/// 附件编排服务：路径解析、处理、模型能力判定、ChatMessage 构建、元数据序列化。
/// </summary>
public sealed class AttachmentService
{
    /// <summary>每消息最大附件数。</summary>
    public const int MaxAttachmentsPerMessage = 5;

    private static readonly Regex FileRefPattern =
        new("@(?:\"([^\"]+)\"|(\\S+))", RegexOptions.Compiled);

    private readonly IAttachmentProcessor _processor;
    private readonly IWorkspaceManager? _workspace;

    /// <summary>构造。</summary>
    /// <param name="processor">单文件处理器。</param>
    /// <param name="workspace">工作区管理器，用于相对路径解析（可为 null）。</param>
    public AttachmentService(IAttachmentProcessor processor, IWorkspaceManager? workspace = null)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _workspace = workspace;
    }

    /// <summary>
    /// 解析输入文本中的 @引用并**从文本中移除**，返回已归一化的绝对路径列表。
    /// 支持：@path、@"path with spaces"、@./relative（基于工作区根）、@/absolute。
    /// 仅返回存在且类型受支持的引用；其余引用原样保留在文本中。
    /// </summary>
    /// <param name="input">原始输入。</param>
    /// <param name="remainingText">去除有效引用后的正文。</param>
    public IReadOnlyList<string> ParseFileReferences(string input, out string remainingText)
    {
        var paths = new List<string>();
        var text = FileRefPattern.Replace(input, m =>
        {
            var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var resolved = ResolvePath(raw);
            if (resolved != null && File.Exists(resolved) && _processor.IsSupported(resolved))
            {
                if (!paths.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                    paths.Add(resolved);
                return string.Empty; // 有效引用：从正文移除
            }
            return m.Value; // 无效引用：保留原文，交由 UI 提交时校验提示
        });
        remainingText = Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
        return paths;
    }

    /// <summary>把引用路径解析为绝对路径；相对路径基于当前工作区根目录。</summary>
    /// <param name="path">引用中的路径。</param>
    /// <returns>绝对路径；无法解析时返回 null。</returns>
    public string? ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var p = path.Trim();
            if (Path.IsPathRooted(p))
                return Path.GetFullPath(p);

            var root = _workspace?.CurrentWorkspace?.RootPath;
            return string.IsNullOrEmpty(root)
                ? Path.GetFullPath(p)
                : Path.GetFullPath(Path.Combine(root, p));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>返回文本中无法解析为受支持文件的 @引用片段（供提交前校验提示）。</summary>
    /// <param name="input">原始输入文本。</param>
    /// <param name="service">用于解析路径的服务实例。</param>
    public static IReadOnlyList<string> FindUnresolvedReferences(string input, AttachmentService service)
    {
        var invalid = new List<string>();
        foreach (Match m in FileRefPattern.Matches(input))
        {
            var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var resolved = service.ResolvePath(raw);
            if (resolved == null || !File.Exists(resolved) || !service._processor.IsSupported(resolved))
                invalid.Add(raw);
        }
        return invalid;
    }

    /// <summary>处理附件路径集合。</summary>
    public async Task<IReadOnlyList<ProcessedAttachment>> ProcessAsync(
        IReadOnlyList<string> filePaths, bool generateThumbnails = false, CancellationToken ct = default)
    {
        if (filePaths.Count > MaxAttachmentsPerMessage)
            throw new InvalidOperationException($"最多附加 {MaxAttachmentsPerMessage} 个文件");

        var result = new List<ProcessedAttachment>(filePaths.Count);
        foreach (var path in filePaths)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(await _processor.ProcessAsync(path, generateThumbnails, ct).ConfigureAwait(false));
        }
        return result;
    }

    /// <summary>模型是否可接受这些附件（仅有图片时校验 vision）。</summary>
    public bool CanAttach(string modelId, IReadOnlyList<ProcessedAttachment> attachments)
    {
        var hasImage = attachments.Any(a => a.Info.Kind == AttachmentKind.Image);
        return !hasImage || ModelCapability.SupportsVision(modelId);
    }

    /// <summary>不支持时的提示。</summary>
    public string GetUnsupportedMessage(string modelId, IReadOnlyList<ProcessedAttachment> attachments)
        => $"当前模型 {modelId} 不支持图片输入，请切换到 vision 模型或移除图片附件";

    /// <summary>
    /// 构建含附件内容的用户消息。
    /// 直接委托 framework 的 <see cref="AttachmentMessageBuilder"/>，确保与 CLI/Codex 通路格式一致。
    /// </summary>
    public ChatMessage BuildUserMessage(string text, IReadOnlyList<ProcessedAttachment> attachments)
        => AttachmentMessageBuilder.Build(text, attachments);

    /// <summary>将附件元数据序列化为 JSON（供持久化）。</summary>
    public static string? ToAttachmentsJson(IReadOnlyList<ProcessedAttachment> attachments)
    {
        if (attachments.Count == 0)
            return null;
        var records = attachments.Select(a => new AttachmentRecord
        {
            FileName = a.Info.FileName,
            MediaType = a.Info.MediaType,
            FileSize = a.Info.FileSize,
            SourcePath = a.Info.SourcePath,
            Kind = a.Info.Kind.ToString()
        }).ToList();
        return JsonSerializer.Serialize(records);
    }

    /// <summary>转 UI 展示模型。</summary>
    public static IReadOnlyList<AttachmentDisplayInfo> ToDisplayInfos(IReadOnlyList<ProcessedAttachment> attachments)
        => attachments.Select(a => new AttachmentDisplayInfo(
            a.Info.FileName, a.Info.MediaType, a.Info.FileSize,
            a.ThumbnailPath,
            a.Info.Kind == AttachmentKind.Image,
            a.PixelSize)).ToList();

    /// <summary>历史附件 JSON（<see cref="AttachmentRecord"/> 数组）→ 显示信息。</summary>
    public static IReadOnlyList<AttachmentDisplayInfo> DisplayInfosFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<AttachmentDisplayInfo>();
        try
        {
            var records = JsonSerializer.Deserialize<List<AttachmentRecord>>(json);
            if (records is null)
                return Array.Empty<AttachmentDisplayInfo>();
            return records.Select(r => new AttachmentDisplayInfo(
                r.FileName, r.MediaType, r.FileSize, null,
                string.Equals(r.Kind, "Image", StringComparison.OrdinalIgnoreCase),
                null)).ToList();
        }
        catch
        {
            return Array.Empty<AttachmentDisplayInfo>();
        }
    }
}