/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Views
*文件名： InputBarView
*版本号： V1.0.0.0
*唯一标识：输入区视图
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：输入区视图，使用 Editor 作为高性能文本输入控件，
*Enter 提交，Shift+Enter 换行。
*
*****************************************************************************/
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Views;

/// <summary>
/// 自定义 Editor，处理 Enter 提交和 Shift+Enter 换行。
/// </summary>
internal sealed class MultilineEditor : Terminal.Gui.Editor.Editor
{
    public event Action<string>? SubmitRequested;

protected override bool OnKeyDown(Key key)
    {
        Infrastructure.TuiDiag.KeyArrival();

        if (Infrastructure.TuiDiag.Enabled)
        {
            Logger.Warn($"[TuiDiag] Editor.OnKeyDown: key={key}");
        }

        // Shift+Enter 或 Ctrl+Enter 换行
        if (key == Key.Enter.WithShift || key == Key.Enter.WithCtrl)
        {
            ReplaceSelection("\n");
            return true;
        }

        if (key == Key.Enter)
        {
            var text = (Text ?? string.Empty).Trim();
            if (text.Length > 0)
            {
                Text = string.Empty;
                SubmitRequested?.Invoke(text);
            }
            return true;
        }

        return base.OnKeyDown(key);
    }
}

/// <summary>
/// 输入区视图。使用 Editor 作为高性能文本输入控件，
/// Enter 提交，Shift+Enter 换行。
/// </summary>
internal sealed class InputBarView : View
{
    private readonly MultilineEditor _editor;

    /// <summary>
    /// 用户提交输入时触发（Enter）。
    /// </summary>
    public event Action<string>? Submitted;

    /// <summary>
    /// 输入框背景色（亮蓝色，调试阶段便于定位）。
    /// </summary>
    private static readonly Color InputBackground = new(0x1E, 0x3A, 0x5F, 0xFF);

    /// <summary>
    /// 初始化输入区视图。
    /// </summary>
    public InputBarView()
    {
        CanFocus = true;
        BorderStyle = LineStyle.RoundedDashed;

        var bgScheme = new Scheme(
            new Attribute(Color.White, InputBackground))
        {
            Normal = new Attribute(Color.White, InputBackground),
            Focus = new Attribute(Color.White, InputBackground),
            HotNormal = new Attribute(TuiTheme.AssistantText, InputBackground),
            HotFocus = new Attribute(Color.White, InputBackground),
            Disabled = new Attribute(TuiTheme.SystemMessage, InputBackground),
            Active = new Attribute(TuiTheme.AssistantText, InputBackground),
            HotActive = new Attribute(TuiTheme.AssistantText, InputBackground),
            Highlight = new Attribute(TuiTheme.Background, TuiTheme.Accent),
            Editable = new Attribute(Color.White, InputBackground),
            ReadOnly = new Attribute(TuiTheme.SystemMessage, InputBackground)
        };
        SetScheme(bgScheme);

_editor = new MultilineEditor
        {
            X = 2,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            Multiline = true,
            WordWrap = false,
        };
        _editor.SetScheme(bgScheme);
        _editor.SubmitRequested += text => Submitted?.Invoke(text);

        Add(_editor);
    }

    /// <summary>
    /// 当前输入框文本。
    /// </summary>
    public string InputText
    {
        get => _editor.Text ?? string.Empty;
        set => _editor.Text = value ?? string.Empty;
    }

    /// <summary>
    /// 将焦点交给内部编辑器。
    /// </summary>
    public void FocusInput() => _editor.SetFocus();

    /// <summary>
    /// 绘制提示符。输入内容由子视图 Editor 自行渲染。
    /// </summary>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (Viewport.Width <= 0 || Viewport.Height <= 0)
        {
            return true;
        }

        SetAttribute(TuiTheme.Attr(TuiTheme.Prompt, TextStyle.Bold, InputBackground));
        AddStr(0, 0, ">");
        return true;
    }
}
