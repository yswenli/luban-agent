/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： StepReportParser
*创建时间：2026/10/21
*描述：助手结论文件的解析（M4-P4）：判定口径的第一、二环（文件存在 + schema 可解析）
*****************************************************************************/

using System.Text.Json;
using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Serialization;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 结论文件（<c>step{4,5}-*.json</c>）的解析器。
/// </summary>
/// <remarks>
/// <para>
/// 一律走<b>源生成</b>上下文 <see cref="PluginJsonContext"/>（插件 ALC 硬约束：不得用反射序列化，见 csproj 注释）。
/// </para>
/// <para>
/// 解析失败<b>不抛异常</b>，只返回可操作原因——Step 4 的失败不阻断流程，Step 5 的失败要写成明确的阻断文案。
/// </para>
/// </remarks>
public static class StepReportParser
{
    /// <summary>
    /// 尝试把结论文件正文解析为 <see cref="StepReport"/>。
    /// </summary>
    /// <param name="json">结论文件正文（可为 <c>null</c> / 空）。</param>
    /// <param name="report">解析结果（失败时为 <c>null</c>）。</param>
    /// <param name="error">失败原因（成功时为空串）。</param>
    /// <returns>解析成功返回 <c>true</c>。</returns>
    public static bool TryParse(string? json, out StepReport? report, out string error)
    {
        report = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "结论文件为空：助手未写出任何内容。";
            return false;
        }

        try
        {
            report = JsonSerializer.Deserialize(json!, PluginJsonContext.Default.StepReport);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            error = $"结论文件不是合法 JSON（{ex.GetType().Name}）：{Truncate(ex.Message)}";
            return false;
        }

        if (report is null)
        {
            error = "结论文件解析结果为空（JSON 字面量为 null）。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>把异常信息压到一行、限长，避免污染操作记录。</summary>
    /// <param name="text">原文。</param>
    /// <returns>压缩后的文本。</returns>
    private static string Truncate(string? text)
    {
        var value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= 160 ? value : value[..160] + "…";
    }
}