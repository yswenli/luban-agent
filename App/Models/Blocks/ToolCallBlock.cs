/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： ToolCallBlock
*版本号： V1.0.0.0
*唯一标识：工具调用 Block
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Agent 工具调用，淡黄色，单行显示"正在使用工具[名称]"（参考 Claude Code）
*
*****************************************************************************/
namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 工具调用 Block。淡黄色着色，单行显示 <c>正在使用工具[工具名]</c>，
/// 完成后追加耗时后缀；不可折叠，不展示参数与返回内容。
/// </summary>
public sealed class ToolCallBlock : Block
{
    /// <summary>工具名称。</summary>
    public string ToolName { get; }

    /// <summary>工具调用 ID（MCP 协议中的 callId）。</summary>
    public string? CallId { get; set; }

    /// <summary>
    /// 初始化工具调用 Block。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="callId">调用 ID（可选）。</param>
    public ToolCallBlock(string toolName, string? callId = null)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        CallId = callId;
    }

    /// <inheritdoc/>
    public override bool IsFoldable => false;

    /// <inheritdoc/>
    public override void Layout(int width)
    {
        base.Layout(width);
        LineCount = 1;
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        var durationStr = Duration.HasValue ? $" · {Duration.Value.TotalSeconds:F1}s" : string.Empty;
        lines.Add(RenderLine.Single($"正在使用工具[{ToolName}]{durationStr}", BlockColors.ToolCall));
    }
}
