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

// 消歧：全局 using 引入了 Spectre.Console（迁移步骤 6 移除）
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Views;

/// <summary>
/// 页脚视图。显示权限模式、工作目录、git 分支、token 统计。
/// </summary>
internal sealed class FooterView : View
{
    private FooterDataProvider? _provider;
    private string _modeText = "default";

    /// <summary>
    /// 初始化页脚视图。
    /// </summary>
    public FooterView()
    {
        CanFocus = false;
    }

    /// <summary>
    /// 设置页脚数据提供者。
    /// </summary>
    /// <param name="provider">页脚数据提供者。</param>
    public void SetProvider(FooterDataProvider provider)
    {
        _provider = provider;
    }

    /// <summary>
    /// 设置当前权限模式显示文本。
    /// </summary>
    /// <param name="mode">权限模式名称。</param>
    public void SetMode(string mode)
    {
        _modeText = mode;
        if (_provider is not null) _provider.ModeDisplay = mode;
        SetNeedsDraw();
    }

    /// <inheritdoc/>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        var viewport = Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return true;
        }

        var col = 0;
        var p = _provider;

        col += Write(col, $"[{_modeText}]", TuiTheme.Accent, viewport.Width, TextStyle.Bold);
        col += Write(col, "  ", TuiTheme.SystemMessage, viewport.Width);
        col += Write(col, ShortenPath(Directory.GetCurrentDirectory()), TuiTheme.SystemMessage, viewport.Width);
        col += Write(col, "  ", TuiTheme.SystemMessage, viewport.Width);

        if (p is not null)
        {
            var branch = p.GitBranch;
            if (branch != "—")
            {
                col += Write(col, $"git:{branch}", TuiTheme.SystemMessage, viewport.Width);
                col += Write(col, "  ", TuiTheme.SystemMessage, viewport.Width);
            }

            if (p.TotalTokens > 0)
            {
                col += Write(col, $"{FormatTokens(p.TotalTokens)} tok", TuiTheme.SystemMessage, viewport.Width);
            }
        }

        return true;
    }

    /// <summary>
    /// 在指定列位置写入带颜色与样式的文本，超出宽度时截断。
    /// </summary>
    /// <param name="col">起始列。</param>
    /// <param name="text">待写入文本。</param>
    /// <param name="color">前景色。</param>
    /// <param name="maxWidth">视口最大宽度。</param>
    /// <param name="style">文本样式。</param>
    /// <returns>实际写入的字符数。</returns>
    private int Write(int col, string text, Color color, int maxWidth, TextStyle style = TextStyle.None)
    {
        if (col >= maxWidth) return 0;
        var available = maxWidth - col;
        var output = text.Length <= available ? text : text[..available];
        SetAttribute(TuiTheme.Attr(color, style));
        AddStr(col, 0, output);
        return output.Length;
    }

    /// <summary>
    /// 截取路径末尾两段目录作为简短显示。
    /// </summary>
    /// <param name="path">完整路径。</param>
    /// <returns>简短路径字符串。</returns>
    private static string ShortenPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var tail = parts.Where(p => !string.IsNullOrEmpty(p)).TakeLast(2).ToArray();
        return tail.Length == 0 ? path : "…/" + string.Join('/', tail);
    }

    /// <summary>
    /// 格式化 token 数量（超过 1000 时显示为 x.xk）。
    /// </summary>
    /// <param name="tokens">token 数量。</param>
    /// <returns>格式化后的字符串。</returns>
    private static string FormatTokens(int tokens)
    {
        if (tokens >= 1000) return $"{tokens / 1000.0:F1}k";
        return tokens.ToString();
    }
}
