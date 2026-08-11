/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
*文件名： TuiTheme
*版本号： V1.0.0.0
*唯一标识：TUI 主题配色
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：TUI 主题配色（24-bit TrueColor），参照 Claude Code 终端视觉风格
*
*****************************************************************************/
using Terminal.Gui.Drawing;

// 消歧：全局 using 引入了 Spectre.Console（迁移步骤 6 移除），此处显式指向 Terminal.Gui 类型
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgent.App;

/// <summary>
/// TUI 主题配色。所有颜色为 24-bit TrueColor，对应设计文档 3.3 节配色方案表。
/// </summary>
internal static class TuiTheme
{
    /// <summary>深灰近黑背景色 #1a1a1a。</summary>
    public static readonly Color Background = new(0x1A, 0x1A, 0x1A, 0xFF);

    /// <summary>用户消息 淡蓝白 #85B7EB。</summary>
    public static readonly Color UserMessage = new(0x85, 0xB7, 0xEB, 0xFF);

    /// <summary>AI 回复正文 灰白 #D3D1C7。</summary>
    public static readonly Color AssistantText = new(0xD3, 0xD1, 0xC7, 0xFF);

    /// <summary>思考过程 紫色 #AFA9EC。</summary>
    public static readonly Color Thinking = new(0xAF, 0xA9, 0xEC, 0xFF);

    /// <summary>Tool call 淡黄 #FAC775。</summary>
    public static readonly Color ToolCall = new(0xFA, 0xC7, 0x75, 0xFF);

    /// <summary>Tool result 淡青 #5DCAA5。</summary>
    public static readonly Color ToolResult = new(0x5D, 0xCA, 0xA5, 0xFF);

    /// <summary>确认块标题 珊瑚红 #F0997B。</summary>
    public static readonly Color ConfirmTitle = new(0xF0, 0x99, 0x7B, 0xFF);

    /// <summary>系统消息 灰色 #888780。</summary>
    public static readonly Color SystemMessage = new(0x88, 0x87, 0x80, 0xFF);

    /// <summary>输入提示符 绿色 #97C459。</summary>
    public static readonly Color Prompt = new(0x97, 0xC4, 0x59, 0xFF);

    /// <summary>页脚 default 模式 / 流式橙色 * 标记 #EF9F27。</summary>
    public static readonly Color Accent = new(0xEF, 0x9F, 0x27, 0xFF);

    /// <summary>成功 绿 #5DCAA5。</summary>
    public static readonly Color Success = new(0x5D, 0xCA, 0xA5, 0xFF);

    /// <summary>失败 红 #F09595。</summary>
    public static readonly Color Failure = new(0xF0, 0x95, 0x95, 0xFF);

    /// <summary>Plan 模式 紫色。</summary>
    public static readonly Color ModePlan = new(0xAF, 0xA9, 0xEC, 0xFF);

    /// <summary>AcceptEdits 模式 蓝色。</summary>
    public static readonly Color ModeAcceptEdits = new(0x85, 0xB7, 0xEB, 0xFF);

    /// <summary>BypassPermissions 模式 红色（警示）。</summary>
    public static readonly Color ModeBypass = new(0xF0, 0x95, 0x95, 0xFF);

    /// <summary>
    /// 构造前景色为 <paramref name="foreground"/>、背景为主题背景色的属性。
    /// </summary>
    /// <param name="foreground">前景色。</param>
    /// <param name="style">文本样式，默认无样式。</param>
    /// <returns>可直接用于 <c>View.SetAttribute</c> 的属性。</returns>
    public static Attribute Attr(Color foreground, TextStyle style = TextStyle.None)
        => new(foreground, Background, style);

    /// <summary>
    /// 构造指定前景色和背景色的属性。用于 Block 渲染中需要非默认背景色的行内片段。
    /// </summary>
    /// <param name="foreground">前景色。</param>
    /// <param name="style">文本样式。</param>
    /// <param name="background">背景色。</param>
    /// <returns>可直接用于 <c>View.SetAttribute</c> 的属性。</returns>
    public static Attribute Attr(Color foreground, TextStyle style, Color background)
        => new(foreground, background, style);

    /// <summary>
    /// 构建全局配色方案：所有 VisualRole 统一使用深灰背景，避免默认蓝底。
    /// </summary>
    /// <returns>应用于根视图的配色方案。</returns>
    public static Scheme BuildScheme()
    {
        var normal = new Attribute(AssistantText, Background);
        return new Scheme(normal)
        {
            Normal = normal,
            Focus = new Attribute(Background, UserMessage),
            HotNormal = new Attribute(Accent, Background, TextStyle.Bold),
            HotFocus = new Attribute(Background, Accent, TextStyle.Bold),
            Disabled = new Attribute(SystemMessage, Background),
            Active = new Attribute(UserMessage, Background),
            HotActive = new Attribute(Accent, Background, TextStyle.Bold),
            Highlight = new Attribute(Background, Accent),
            Editable = new Attribute(AssistantText, Background),
            ReadOnly = new Attribute(SystemMessage, Background)
        };
    }
}
