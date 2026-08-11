/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Views
*文件名： FooterView
*版本号： V1.0.0.0
*唯一标识：页脚视图
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：页脚视图，显示权限模式、工作目录、git 分支、token 与后台任务
*
*****************************************************************************/
using LubanAgent.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

// 消歧：全局 using 引入了 Spectre.Console（迁移步骤 6 移除），此处显式指向 Terminal.Gui 类型
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgent.Views;

/// <summary>
/// 页脚视图（单行自绘）。数据源为 IFooterDataProvider，见迁移步骤 7。
/// 当前为骨架实现：绘制静态占位信息。
/// </summary>
internal sealed class FooterView : View
{
    private string _modeText = "default";

    public FooterView()
    {
        CanFocus = false;
    }

    public void SetMode(string mode)
    {
        _modeText = mode;
        SetNeedsDraw();
    }

    /// <summary>
    /// 自绘页脚内容。
    /// </summary>
    /// <param name="context">绘制上下文。</param>
    /// <returns>始终返回 true（完全自绘）。</returns>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        var viewport = Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return true;
        }

        var col = 0;

        col += Write(col, $"[{_modeText}]", TuiTheme.Accent, viewport.Width, TextStyle.Bold);
        col += Write(col, "  ", TuiTheme.SystemMessage, viewport.Width);
        col += Write(col, ShortenPath(Directory.GetCurrentDirectory()), TuiTheme.SystemMessage, viewport.Width);
        col += Write(col, "  —  ", TuiTheme.SystemMessage, viewport.Width);
        Write(col, "git/token/tasks 待接入", TuiTheme.SystemMessage, viewport.Width);

        return true;
    }

    /// <summary>
    /// 在指定列写入一段带色文本，自动裁剪超出视口的部分。
    /// </summary>
    /// <param name="col">起始列。</param>
    /// <param name="text">文本内容。</param>
    /// <param name="color">前景色。</param>
    /// <param name="maxWidth">视口宽度。</param>
    /// <param name="style">文本样式。</param>
    /// <returns>实际写入的字符数。</returns>
    private int Write(int col, string text, Color color, int maxWidth, TextStyle style = TextStyle.None)
    {
        if (col >= maxWidth)
        {
            return 0;
        }

        var available = maxWidth - col;
        var output = text.Length <= available ? text : text[..available];
        SetAttribute(TuiTheme.Attr(color, style));
        AddStr(col, 0, output);
        return output.Length;
    }

    /// <summary>
    /// 将绝对路径缩短为末两级目录，避免页脚被长路径占满。
    /// </summary>
    /// <param name="path">完整路径。</param>
    /// <returns>缩短后的显示路径。</returns>
    private static string ShortenPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var tail = parts.Where(p => !string.IsNullOrEmpty(p)).TakeLast(2).ToArray();
        return tail.Length == 0 ? path : "…/" + string.Join('/', tail);
    }
}
