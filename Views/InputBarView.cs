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
*描述：输入区视图，使用 TextField 作为单行编辑内核
*
*****************************************************************************/
using LubanAgent.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LubanAgent.Views;

/// <summary>
/// 输入区视图。使用 <see cref="TextField"/> 作为单行编辑内核，
/// 本视图负责提示符绘制与 Enter 键拦截提交。
/// </summary>
internal sealed class InputBarView : View
{
    private readonly TextField _textField;

    /// <summary>
    /// 用户提交一行输入时触发（Enter）。
    /// </summary>
    public event Action<string>? Submitted;

    /// <summary>
    /// 初始化输入区视图。
    /// </summary>
    public InputBarView()
    {
        CanFocus = true;

        _textField = new TextField
        {
            X = 2,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = true
        };
        _textField.KeyDown += OnTextFieldKeyDown;

        Add(_textField);
    }

    /// <summary>
    /// 当前输入框文本。
    /// </summary>
    public string InputText
    {
        get => _textField.Text ?? string.Empty;
        set => _textField.Text = value ?? string.Empty;
    }

    /// <summary>
    /// 将焦点交给内部编辑器。
    /// </summary>
    public void FocusInput() => _textField.SetFocus();

    /// <summary>
    /// 绘制提示符。输入内容由子视图 TextField 自行渲染。
    /// </summary>
    /// <param name="context">绘制上下文。</param>
    /// <returns>始终返回 true（提示符自绘）。</returns>
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
    /// 拦截编辑器按键：Enter 提交，其余透传给 TextField。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="key">按键事件。</param>
    private void OnTextFieldKeyDown(object? sender, Key key)
    {
        if (key != Key.Enter)
        {
            return;
        }

        key.Handled = true;

        var text = (_textField.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        _textField.Text = string.Empty;
        Submitted?.Invoke(text);
    }
}
