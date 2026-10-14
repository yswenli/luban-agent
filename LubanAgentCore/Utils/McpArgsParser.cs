/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Utils
*文件名： McpArgsParser
*版本号： V1.0.0.0
*唯一标识：MCP 参数解析
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/14
*描述：MCP 启动参数的文本解析与回填，CLI / GUI 共用同一规则
*
*****************************************************************************/
namespace LubanAgentCore.Utils;

/// <summary>
/// MCP 启动参数与文本之间的互转，CLI / GUI 共用，避免两端分隔符分叉。
/// 规则：以换行为唯一分隔符——每行一个参数，行内空格原样保留（适配含空格的路径/参数）。
/// </summary>
public static class McpArgsParser
{
    /// <summary>
    /// 解析用户输入为参数列表（每行一个参数，行内空格保留）。
    /// </summary>
    /// <param name="input">多行文本，每行一个参数。</param>
    /// <returns>去空、裁剪首尾空白后的参数列表。</returns>
    public static List<string> Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new List<string>();

        return input
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>
    /// 将参数列表格式化为可回填的文本（每行一个参数）。
    /// </summary>
    /// <param name="args">参数列表。</param>
    /// <returns>多行文本；为空时返回空串。</returns>
    public static string Format(IReadOnlyList<string>? args)
        => args is null || args.Count == 0 ? string.Empty : string.Join(Environment.NewLine, args);
}