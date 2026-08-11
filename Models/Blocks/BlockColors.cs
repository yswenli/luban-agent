/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： BlockColors
*版本号： V1.0.0.0
*唯一标识：Block 配色常量
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Block 子类共享的 24-bit TrueColor 配色常量，与 App/TuiTheme.cs 中的值一致
*
*****************************************************************************/
using Terminal.Gui.Drawing;

// 消歧：全局 using 引入了 Spectre.Console（迁移步骤 6 移除）
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgent.Models.Blocks;

/// <summary>
/// Block 子类共享的配色常量。值与 <c>App/TuiTheme.cs</c> 中的 24-bit TrueColor 定义一致。
/// Models 层不直接依赖 TuiTheme（App 层），通过本类内联常量保持纯度。
/// </summary>
internal static class BlockColors
{
    /// <summary>用户消息 淡蓝白 #85B7EB。</summary>
    public static readonly Color UserMessage = new(0x85, 0xB7, 0xEB, 0xFF);

    /// <summary>AI 回复正文 灰白 #D3D1C7。</summary>
    public static readonly Color AssistantText = new(0xD3, 0xD1, 0xC7, 0xFF);

    /// <summary>思考过程 紫色 #AFA9EC。</summary>
    public static readonly Color Thinking = new(0xAF, 0xA9, 0xEC, 0xFF);

    /// <summary>工具调用 淡黄 #FAC775。</summary>
    public static readonly Color ToolCall = new(0xFA, 0xC7, 0x75, 0xFF);

    /// <summary>工具结果 淡青 #5DCAA5。</summary>
    public static readonly Color ToolResult = new(0x5D, 0xCA, 0xA5, 0xFF);

    /// <summary>确认标题 珊瑚红 #F0997B。</summary>
    public static readonly Color ConfirmTitle = new(0xF0, 0x99, 0x7B, 0xFF);

    /// <summary>系统消息 灰色 #888780。</summary>
    public static readonly Color System = new(0x88, 0x87, 0x80, 0xFF);

    /// <summary>强调色 橙色 #EF9F27。</summary>
    public static readonly Color Accent = new(0xEF, 0x9F, 0x27, 0xFF);

    /// <summary>成功 绿色 #5DCAA5。</summary>
    public static readonly Color Success = new(0x5D, 0xCA, 0xA5, 0xFF);

    /// <summary>失败 红色 #F09595。</summary>
    public static readonly Color Failure = new(0xF0, 0x95, 0x95, 0xFF);
}
