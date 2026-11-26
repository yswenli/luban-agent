/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebFileKinds
*描述：工作区文件预览的类型判定与体积上限（纯逻辑，便于单测）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>文件预览的处置方式。</summary>
public enum WebFilePreviewKind
{
    /// <summary>不支持预览：未知格式或超出体积上限。</summary>
    Unsupported,

    /// <summary>按纯文本回传。</summary>
    Text,

    /// <summary>按图片回传。</summary>
    Image,

    /// <summary>按视频流回传。</summary>
    Video,

    /// <summary>按音频流回传。</summary>
    Audio
}

/// <summary>
/// 哪些工作区文件允许回传浏览器。
/// </summary>
/// <remarks>
/// <para>
/// 只认文本、常见位图与浏览器原生音视频：会话里点击一个路径不等于授权宿主把任意文件交给浏览器，
/// 未知格式（二进制、可执行文件、压缩包等）一律回「不支持预览」。
/// </para>
/// <para>
/// <c>.svg</c> 归入文本而不是图片：同源直出 SVG 会让其中的脚本以本页权限执行，
/// 而页面持有宿主访问凭据；按源码文本展示没有这个风险。
/// </para>
/// </remarks>
public static class WebFileKinds
{
    /// <summary>文本类单文件上限（字节）。</summary>
    public const int MaxTextBytes = 2 * 1024 * 1024;

    /// <summary>图片类单文件上限（字节）。</summary>
    public const int MaxImageBytes = 10 * 1024 * 1024;

    /// <summary>音频类单文件上限（字节）。</summary>
    public const int MaxAudioBytes = 50 * 1024 * 1024;

    /// <summary>视频类单文件上限（字节）。</summary>
    /// <remarks>音视频走流式回传，不读进内存；上限只兜住「误点一个十几 G 的录制文件」这类情况。</remarks>
    public const int MaxVideoBytes = 200 * 1024 * 1024;

    /// <summary>按文本回传的内容类型。</summary>
    public const string TextContentType = "text/plain; charset=utf-8";

    /// <summary>文本类扩展名。</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".markdown", ".rst", ".adoc",
        ".json", ".jsonc", ".xml", ".yaml", ".yml", ".toml", ".ini", ".conf", ".cfg", ".properties",
        ".csv", ".tsv", ".http", ".rest", ".graphql", ".proto", ".svg",
        ".cs", ".csproj", ".sln", ".slnx", ".props", ".targets", ".cshtml", ".razor", ".vb", ".fs", ".fsproj",
        ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".css", ".scss", ".less", ".html", ".htm",
        ".vue", ".svelte", ".axaml", ".xaml",
        ".ps1", ".psm1", ".sh", ".bash", ".bat", ".cmd", ".sql",
        ".py", ".java", ".kt", ".kts", ".go", ".rs", ".c", ".h", ".cpp", ".hpp", ".cc",
        ".m", ".swift", ".dart", ".rb", ".php", ".lua", ".r", ".pl", ".scala", ".gradle",
        ".gitignore", ".gitattributes", ".editorconfig", ".env",
    };

    /// <summary>无扩展名但确定是文本的文件名（点开头的文件已由扩展名分支覆盖）。</summary>
    private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dockerfile", "makefile", "license", "readme", "changelog", "notice", "authors",
        "gemfile", "rakefile", "procfile"
    };

    /// <summary>图片扩展名 → 内容类型。</summary>
    private static readonly Dictionary<string, string> ImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".ico"] = "image/x-icon",
    };

    /// <summary>视频扩展名 → 内容类型。</summary>
    /// <remarks>
    /// 只列浏览器原生能放的封装：<c>.mkv</c>、<c>.avi</c>、<c>.wmv</c> 各浏览器支持差异太大，
    /// 归入「不支持预览」比回一个放不出来的流更好排查。
    /// </remarks>
    private static readonly Dictionary<string, string> VideoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".webm"] = "video/webm",
        [".ogv"] = "video/ogg",
        [".mov"] = "video/quicktime",
    };

    /// <summary>音频扩展名 → 内容类型。</summary>
    private static readonly Dictionary<string, string> AudioContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/ogg",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".flac"] = "audio/flac",
    };

    /// <summary>
    /// 判定文件预览方式。
    /// </summary>
    /// <param name="path">文件名或相对路径。</param>
    /// <param name="contentType">命中时回传的内容类型；不支持时为空串。</param>
    /// <returns>处置方式。</returns>
    public static WebFilePreviewKind Classify(string? path, out string contentType)
    {
        contentType = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return WebFilePreviewKind.Unsupported;

        var name = Path.GetFileName(path.Trim());
        if (name.Length == 0) return WebFilePreviewKind.Unsupported;

        if (TextFileNames.Contains(name))
        {
            contentType = TextContentType;
            return WebFilePreviewKind.Text;
        }

        var extension = Path.GetExtension(name);
        if (extension.Length == 0) return WebFilePreviewKind.Unsupported;

        if (TextExtensions.Contains(extension))
        {
            contentType = TextContentType;
            return WebFilePreviewKind.Text;
        }

        if (ImageContentTypes.TryGetValue(extension, out var imageContentType))
        {
            contentType = imageContentType;
            return WebFilePreviewKind.Image;
        }

        if (VideoContentTypes.TryGetValue(extension, out var videoContentType))
        {
            contentType = videoContentType;
            return WebFilePreviewKind.Video;
        }

        if (AudioContentTypes.TryGetValue(extension, out var audioContentType))
        {
            contentType = audioContentType;
            return WebFilePreviewKind.Audio;
        }

        return WebFilePreviewKind.Unsupported;
    }

    /// <summary>
    /// 该类型允许的最大字节数。
    /// </summary>
    /// <param name="kind">处置方式。</param>
    /// <returns>上限；不支持的类型为 0。</returns>
    public static long LimitOf(WebFilePreviewKind kind) => kind switch
    {
        WebFilePreviewKind.Text => MaxTextBytes,
        WebFilePreviewKind.Image => MaxImageBytes,
        WebFilePreviewKind.Audio => MaxAudioBytes,
        WebFilePreviewKind.Video => MaxVideoBytes,
        _ => 0
    };
}