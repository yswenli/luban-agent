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
*描述：输入区视图，使用 TextView 作为多行编辑内核，
*Enter 提交，Shift+Enter 换行，自动扩展高度（最多 5 行）
*
*****************************************************************************/
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LubanAgentCli.App.Views;

/// <summary>
/// 输入区视图。使用 <see cref="TextView"/> 作为多行编辑内核，
/// Enter 提交，Shift+Enter 换行，自动扩展高度（最多 5 行）。
/// </summary>
internal sealed class InputBarView : View
{
    private readonly TextView _textView;
    private const int MaxHeight = 5;

    /// <summary>
    /// 用户提交输入时触发（Enter，非 Shift+Enter）。
    /// </summary>
    public event Action<string>? Submitted;

    /// <summary>
    /// 初始化输入区视图。
    /// </summary>
    public InputBarView()
    {
        CanFocus = true;

        _textView = new TextView
        {
            X = 2,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = true,
            WordWrap = true,
            TabKeyAddsTab = false,
            EnterKeyAddsLine = false,
        };
        _textView.KeyDown += OnTextViewKeyDown;
        _textView.ContentsChanged += OnContentsChanged;

        Add(_textView);
    }

    /// <summary>
    /// 当前输入框文本。
    /// </summary>
    public string InputText
    {
        get => _textView.Text ?? string.Empty;
        set => _textView.Text = value ?? string.Empty;
    }

    /// <summary>
    /// 将焦点交给内部编辑器。
    /// </summary>
    public void FocusInput() => _textView.SetFocus();

    /// <summary>
    /// 绘制提示符。输入内容由子视图 TextView 自行渲染。
    /// </summary>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (Viewport.Width <= 0 || Viewport.Height <= 0)
        {
            return true;
        }

        SetAttribute(TuiTheme.Attr(TuiTheme.Prompt, TextStyle.Bold));
        AddStr(0, 0, ">");
        return true;
    }

    /// <summary>
    /// 内容变化时自动调整高度（1~MaxHeight 行）。
    /// </summary>
    private void OnContentsChanged(object? sender, EventArgs e)
    {
        var lineCount = Math.Max(1, _textView.Lines);
        var newHeight = Math.Min(lineCount, MaxHeight);
        Height = newHeight;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>
    /// 拦截编辑器按键：Enter 提交，Shift+Enter 插入换行，其余透传给 TextView。
    /// </summary>
    private void OnTextViewKeyDown(object? sender, Key key)
    {
        if (key != Key.Enter)
        {
            return;
        }

        key.Handled = true;

        // Shift+Enter：插入换行
        if (key.IsShift)
        {
            _textView.InsertText("\n");
            return;
        }

        var text = (_textView.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        _textView.Text = string.Empty;
        Height = 1;
        Submitted?.Invoke(text);
    }
}
