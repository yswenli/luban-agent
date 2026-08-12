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
*描述：输入区视图，使用 Editor 作为多行编辑内核，
*Enter 提交，Shift+Enter 换行，自动扩展高度（最多 5 行）
*
*****************************************************************************/
using Terminal.Gui.Drawing;
using Terminal.Gui.Editor;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Views;

/// <summary>
/// 输入区视图。使用 <see cref="Editor"/> 作为多行编辑内核，
/// Enter 提交，Shift+Enter 换行，自动扩展高度（最多 5 行）。
/// </summary>
internal sealed class InputBarView : View
{
    private readonly Editor _editor;
    private const int MaxHeight = 5;

    /// <summary>
    /// 用户提交输入时触发（Enter，非 Shift+Enter）。
    /// </summary>
    public event Action<string>? Submitted;

    /// <summary>
    /// 输入框高度变化时触发，参数为新的内容高度（不含边框）。
    /// </summary>
    public event Action<int>? ContentHeightChanged;

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
        // 调试阶段加边框，便于定位输入框位置
        BorderStyle = LineStyle.Single;

        _editor = new Editor
        {
            X = 2,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            Multiline = true,
            WordWrap = true,
            GutterOptions = GutterOptions.None,
        };

        // 设置输入框配色：白色文字，亮蓝色背景（调试用）
        var inputScheme = new Scheme(
            new Attribute(TuiTheme.AssistantText, InputBackground))
        {
            Normal = new Attribute(TuiTheme.AssistantText, InputBackground),
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
        _editor.SetScheme(inputScheme);

        // 使用 Command 机制处理 Enter 键：
        // - Enter → Command.Accept（提交输入），移除默认的 Command.NewLine
        // - Shift+Enter → Command.NewLine（插入换行）
        _editor.KeyBindings.Remove(Key.Enter);
        _editor.KeyBindings.Add(Key.Enter, Command.Accept);
        _editor.KeyBindings.Add(Key.Enter.WithShift, Command.NewLine);

        // 通过 Accepting 事件处理提交，而非 KeyDown
        _editor.Accepting += OnEditorAccepting;
        _editor.TextChanged += OnTextChanged;

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

        // 填充背景色（亮蓝色），确保整个输入区域可见
        var bgAttr = new Attribute(TuiTheme.AssistantText, InputBackground);
        SetAttribute(bgAttr);
        for (var row = 0; row < Viewport.Height; row++)
        {
            for (var col = 0; col < Viewport.Width; col++)
            {
                AddRune(col, row, (Rune)' ');
            }
        }

        SetAttribute(TuiTheme.Attr(TuiTheme.Prompt, TextStyle.Bold, InputBackground));
        AddStr(0, 0, ">");
        return true;
    }

    /// <summary>
    /// 内容变化时自动调整高度（1~MaxHeight 行）并通知父视图。
    /// </summary>
    private void OnTextChanged(object? sender, EventArgs e)
    {
        var lineCount = Math.Max(1, _editor.Document?.LineCount ?? 1);
        var newContentHeight = Math.Min(lineCount, MaxHeight);
        // 总高度 = 内容高度 + 边框(上下各1)
        var newHeight = newContentHeight + 2;
        if (Height != newHeight)
        {
            Height = newHeight;
            ContentHeightChanged?.Invoke(newHeight);
            SetNeedsLayout();
            SetNeedsDraw();
        }
    }

    /// <summary>
    /// 处理 Enter 提交：通过 Command.Accept 触发，设置 Handled 阻止冒泡。
    /// </summary>
    private void OnEditorAccepting(object? sender, CommandEventArgs e)
    {
        var text = (_editor.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            e.Handled = true;
            return;
        }

        _editor.Text = string.Empty;
        Height = 3; // 1行内容 + 边框2
        ContentHeightChanged?.Invoke(3);
        e.Handled = true;
        Submitted?.Invoke(text);
    }
}
