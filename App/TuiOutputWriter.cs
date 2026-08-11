/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
*文件名： TuiOutputWriter
*版本号： V1.0.0.0
*唯一标识：TUI 输出写入器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：将命令文本输出转换为 ConversationDocument 中的 SystemBlock，
*统一 /help /clear /mode 等已迁移命令与后续命令的输出格式
*
*****************************************************************************/
using LubanAgent.Models;
using LubanAgent.Models.Blocks;
using Terminal.Gui.Drawing;

// 消歧：全局 using 引入了 Spectre.Console
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgent.App;

/// <summary>
/// TUI 输出写入器接口。命令（/help /mode /provider 等）通过此接口输出到会话文档，
/// 统一替换各类 Console.Write* 调用，确保所有命令输出格式一致。
/// </summary>
public interface ITuiOutputWriter
{
    void WriteLine(string? text = null, TuiOutputStyle style = TuiOutputStyle.Default);
    void WriteLine();
    void WriteHeader(string text);
    void WriteSuccess(string text);
    void WriteError(string text);
    void WriteInfo(string text);
    void WriteWarning(string text);
}

public enum TuiOutputStyle { Default = 0, Accent = 1, Success = 2, Failure = 3, Warning = 4 }

/// <summary>
/// TUI 输出写入器。统一命令文本输出 → ConversationDocument SystemBlock。
/// 用于 /help /mode /provider /model /session 等所有管理命令。
/// </summary>
public sealed class TuiOutputWriter : ITuiOutputWriter
{
    private readonly ConversationDocument _doc;

    /// <summary>
    /// 初始化 TUI 输出写入器。
    /// </summary>
    /// <param name="doc">会话文档模型。</param>
    public TuiOutputWriter(ConversationDocument doc)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
    }

    /// <inheritdoc/>
    public void WriteLine(string? text = null, TuiOutputStyle style = TuiOutputStyle.Default)
        => _doc.AppendBlock(new SystemBlock(text ?? string.Empty, foreground: ToColor(style)));

    /// <inheritdoc/>
    public void WriteHeader(string text)
        => _doc.AppendBlock(new SystemBlock(text, foreground: BlockColors.Accent, isBold: true));

    /// <inheritdoc/>
    public void WriteSuccess(string text)
        => _doc.AppendBlock(new SystemBlock(text, foreground: BlockColors.Success));

    /// <inheritdoc/>
    public void WriteError(string text)
        => _doc.AppendBlock(new SystemBlock(text, foreground: BlockColors.Failure));

    /// <inheritdoc/>
    public void WriteInfo(string text)
        => _doc.AppendBlock(new SystemBlock(text, foreground: BlockColors.System));

    /// <inheritdoc/>
    public void WriteWarning(string text)
        => _doc.AppendBlock(new SystemBlock(text, foreground: BlockColors.Accent));

    /// <inheritdoc/>
    public void WriteLine() => WriteLine(string.Empty);

    private static Color ToColor(TuiOutputStyle style) => style switch
    {
        TuiOutputStyle.Default => BlockColors.System,
        TuiOutputStyle.Accent => BlockColors.Accent,
        TuiOutputStyle.Success => BlockColors.Success,
        TuiOutputStyle.Failure => BlockColors.Failure,
        TuiOutputStyle.Warning => BlockColors.Accent,
        _ => BlockColors.System
    };
}
