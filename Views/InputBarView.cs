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
*描述：输入区视图，原生 TextView 做编辑内核，外层仅负责按键转发
*
*****************************************************************************/
using System.Diagnostics;
using LubanAgent.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LubanAgent.Views;

/// <summary>
/// 输入区视图。编辑语义（光标移动、选区、多行）由原生 <see cref="TextView"/> 承担，
/// 本视图只负责提示符绘制与按键转发，历史/搜索/补全等业务逻辑位于 InputBarViewModel。
/// </summary>
internal sealed class InputBarView : View
{
    // Terminal.Gui 2.4 起标记 TextView 为过时，建议迁移到 tui-cs/Editor 的 EditorView。
    // 当前 TextView 功能完备且无需额外依赖，暂沿用；是否引入 Editor 包待评估。
#pragma warning disable CS0618
    private readonly TextView _textView;

    /// <summary>
    /// 用户提交一行输入时触发（Enter，非 Shift+Enter）。
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
            Height = Dim.Fill(),
            Multiline = false,
            WordWrap = false,
            CanFocus = true
        };
        _textView.KeyDown += OnTextViewKeyDown;
#pragma warning restore CS0618

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
    /// 拦截编辑器按键：Enter 提交，其余透传给 TextView。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="key">按键事件。</param>
    private void OnTextViewKeyDown(object? sender, Key key)
    {
        // DIAGNOSTIC: 每次按键都记录，用 stderr 直接输出避免日志库开销
        Console.Error.WriteLine($"[Perf] {Stopwatch.GetTimestamp()} KeyDown:{key} text.len={(_textView.Text ?? "").Length}");
        Logger.Error($"[Perf] KeyDown:{key}");

        if (key != Key.Enter || key.IsShift)
        {
            return;
        }

        key.Handled = true;

        var text = (_textView.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        _textView.Text = string.Empty;
        Submitted?.Invoke(text);
    }
}
