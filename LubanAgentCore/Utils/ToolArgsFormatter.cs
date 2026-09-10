/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Utils
*文件名： ToolArgsFormatter
*版本号： V1.0.0.0
*唯一标识：工具参数摘要格式化
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/10
*描述：工具调用参数的单行摘要格式化，CLI 与 GUI 共用，
*避免两端各自实现导致截断长度与省略号不一致。
*
*****************************************************************************/
namespace LubanAgentCore.Utils;

/// <summary>
/// 工具调用参数的单行摘要格式化器。
/// 框架的 <c>IToolConfirmationService.FormatArguments</c> 输出多行明细，
/// 适用于确认对话框；计划项等单行场景使用本格式化器。
/// </summary>
public static class ToolArgsFormatter
{
    /// <summary>
    /// 摘要中单个参数值的默认最大长度，超出部分截断。
    /// </summary>
    public const int DefaultMaxLength = 40;

    /// <summary>
    /// 截断时使用的省略标记。
    /// </summary>
    public const string Ellipsis = "...";

    /// <summary>
    /// 把工具参数压成单行摘要（取首个参数，值超长则截断）。
    /// </summary>
    /// <param name="args">工具参数。</param>
    /// <param name="maxLength">参数值最大长度，缺省 <see cref="DefaultMaxLength"/>。</param>
    /// <returns>形如 <c>path=D:\work\a.txt</c> 的单行摘要；无参数时返回空串。</returns>
    public static string Summarize(IReadOnlyDictionary<string, object?>? args, int maxLength = DefaultMaxLength)
    {
        if (args is null || args.Count == 0)
        {
            return string.Empty;
        }

        var first = args.First();
        var text = first.Value?.ToString() ?? "null";

        if (maxLength > Ellipsis.Length && text.Length > maxLength)
        {
            text = string.Concat(text.AsSpan(0, maxLength - Ellipsis.Length), Ellipsis);
        }

        return $"{first.Key}={text}";
    }
}
