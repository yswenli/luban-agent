namespace LubanAgentCore.Services.Management.Models;

/// <summary>
/// <c>/llmwiki -ingest --wait</c> 的提取产物：源文件已合并为工作区内的临时 markdown。
/// </summary>
/// <param name="TempPath">合并后的临时文件绝对路径（位于工作区配置目录下的 <c>temp</c>）；<paramref name="MergedFiles"/> 为 0 时为空字符串。</param>
/// <param name="RelativePath">相对工作区根的展示路径（正斜杠）；<paramref name="MergedFiles"/> 为 0 时为空字符串。</param>
/// <param name="MergedFiles">成功提取的源文件数。</param>
/// <param name="TotalFiles">命中的源文件总数。</param>
/// <param name="Truncated">汇总内容是否因超过上限被截断。</param>
/// <param name="Skipped">提取失败被跳过的文件说明（已格式化为可直接展示的文案）。</param>
/// <remarks>
/// 交给 Agent 整理这一步属于会话交互（依赖 <c>ConversationViewModel</c>），由命令层拿着本产物
/// 调 <c>ProcessInputAsync</c>；服务只负责提取与落盘，不做任何 UI/会话调用。
/// 全部源文件提取失败时返回 <paramref name="MergedFiles"/> = 0 的产物（含跳过原因），由调用方决定提示文案。
/// </remarks>
public sealed record WikiIngestDraft(
    string TempPath,
    string RelativePath,
    int MergedFiles,
    int TotalFiles,
    bool Truncated = false,
    IReadOnlyList<string>? Skipped = null);