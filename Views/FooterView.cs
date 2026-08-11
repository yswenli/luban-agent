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

// 消歧：全局 using 引入了 Spectre.Console（迁移步骤 6 移除）
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgent.Views;

internal sealed class FooterView : View
{
    private FooterDataProvider? _provider;
    private string _modeText = "default";
    private bool _spinnerSubscribed;

    public FooterView()
    {
        CanFocus = false;
        // 订阅全局 SpinnerService，TUI 模式下显示状态
        SpinnerService.Changed += OnSpinnerChanged;
        _spinnerSubscribed = true;
    }

    private void OnSpinnerChanged() => SetNeedsDraw();

    public void SetProvider(FooterDataProvider provider)
    {
        _provider = provider;
    }

    public void SetMode(string mode)
    {
        _modeText = mode;
        if (_provider is not null) _provider.ModeDisplay = mode;
        SetNeedsDraw();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _spinnerSubscribed)
        {
            try { SpinnerService.Changed -= OnSpinnerChanged; } catch { }
        }
        base.Dispose(disposing);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var viewport = Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return true;
        }

        var col = 0;
        var p = _provider;

        // 若 SpinnerService 在运行，先渲染短状态
        if (SpinnerService.IsRunning)
        {
            var frame = SpinnerService.CurrentFrame;
            var status = SpinnerService.Status;
            var text = string.IsNullOrEmpty(status) ? frame : $"{frame} {status}";
            col += Write(col, text + "  ", TuiTheme.SystemMessage, viewport.Width);
        }

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
                col += Write(col, "  ", TuiTheme.SystemMessage, viewport.Width);
            }
        }

        Write(col, "tasks 待接入", TuiTheme.SystemMessage, viewport.Width);
        return true;
    }

    private int Write(int col, string text, Color color, int maxWidth, TextStyle style = TextStyle.None)
    {
        if (col >= maxWidth) return 0;
        var available = maxWidth - col;
        var output = text.Length <= available ? text : text[..available];
        SetAttribute(TuiTheme.Attr(color, style));
        AddStr(col, 0, output);
        return output.Length;
    }

    private static string ShortenPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var tail = parts.Where(p => !string.IsNullOrEmpty(p)).TakeLast(2).ToArray();
        return tail.Length == 0 ? path : "…/" + string.Join('/', tail);
    }

    private static string FormatTokens(int tokens)
    {
        if (tokens >= 1000) return $"{tokens / 1000.0:F1}k";
        return tokens.ToString();
    }
}
