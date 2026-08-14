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
*描述：输入区视图，使用 TextView 作为轻量级多行输入内核，
*Enter 提交，Shift+Enter 换行，自动扩展高度（最多 5 行）
*
*****************************************************************************/
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Views;

/// <summary>
/// 自定义 TextView，支持 Shift+Enter 插入换行。
/// </summary>
internal sealed class MultilineInputTextView : TextView
{
    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        // Shift+Enter：插入换行，不触发 Accepting
        if (key == Key.Enter.WithShift)
        {
            if (Infrastructure.TuiDiag.Enabled)
            {
                Logger.Warn($"[MultilineInputTextView] Shift+Enter detected, inserting newline");
            }
            InsertText("\n");
            return true;
        }

        return base.OnKeyDown(key);
    }
}

/// <summary>
/// 输入区视图。使用 <see cref="TextView"/> 作为轻量级多行输入内核，
/// Enter 提交，Shift+Enter 换行，自动扩展高度（最多 5 行）。
/// </summary>
internal sealed class InputBarView : View
{
    private readonly MultilineInputTextView _textView;
    private const int MaxHeight = 5;

    /// <summary>默认内容行数（终端行数为整数，取 2 行内容 + 上下边框 = 总高 4）。</summary>
    private const int MinContentLines = 2;

    /// <summary>
    /// 用户提交输入时触发（Enter）。
    /// </summary>
    public event Action<string>? Submitted;

    /// <summary>
    /// 输入框高度变化时触发，参数为新的总高度（含边框）。
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
        // 加边框，便于定位输入框位置
        BorderStyle = LineStyle.RoundedDashed;

        // 通过 Scheme 设置背景色
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

        _textView = new MultilineInputTextView
        {
            X = 2,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            Multiline = true,
            WordWrap = true,
            // Enter 触发 Accepting 事件（提交），而非插入换行
            EnterKeyAddsLine = false,
            // Tab 不插入制表符，让焦点切走
            TabKeyAddsTab = false,
        };
        _textView.SetScheme(bgScheme);
        _textView.Accepting += OnTextViewAccepting;
        _textView.ContentsChanged += OnTextViewContentsChanged;

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
    /// 将焦点交给内部文本视图。
    /// </summary>
    public void FocusInput() => _textView.SetFocus();

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        // 诊断：记录所有到达此视图的按键事件
        if (Infrastructure.TuiDiag.Enabled)
        {
            Logger.Warn($"[InputBarView] OnKeyDown: key={key}, keyCode={key.KeyCode}, isShift={key.IsShift}, isCtrl={key.IsCtrl}");
        }

        return base.OnKeyDown(key);
    }

    /// <summary>
    /// 绘制提示符。输入内容由子视图 TextView 自行渲染。
    /// </summary>
    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (Viewport.Width <= 0 || Viewport.Height <= 0)
        {
            return true;
        }

        // 只绘制提示符，背景色由 Scheme 控制
        SetAttribute(TuiTheme.Attr(TuiTheme.Prompt, TextStyle.Bold, InputBackground));
        AddStr(0, 0, ">");
        return true;
    }

    /// <summary>
    /// 内容变化时按视觉折行数自动调整高度（MinContentLines~MaxHeight 行）并通知父视图。
    /// WordWrap 的视觉换行不产生 \n，必须按可用宽度估算折行，否则长输入始终单行显示。
    /// </summary>
    private void OnTextViewContentsChanged(object? sender, EventArgs e)
    {
        var text = _textView.Text ?? string.Empty;
        // 可用内容宽度：视口宽 - 提示符缩进(X=2) - 右边距(1)
        var contentWidth = Math.Max(1, Viewport.Width - 3);

        var visualLines = 0;
        foreach (var logicalLine in text.Split('\n'))
        {
            var w = EstimateDisplayWidth(logicalLine);
            visualLines += Math.Max(1, (w + contentWidth - 1) / contentWidth);
        }

        var newContentHeight = Math.Clamp(visualLines, MinContentLines, MaxHeight);
        // 总高度 = 内容高度 + 边框(上下各1)
        var newHeight = newContentHeight + 2;
        if (Height != newHeight)
        {
            Height = newHeight;
            ContentHeightChanged?.Invoke(newHeight);
            SetNeedsLayout();
        }
    }

    /// <summary>估算显示宽度（CJK 等宽字符计 2 列）。</summary>
    private static int EstimateDisplayWidth(string s)
    {
        var w = 0;
        foreach (var c in s)
        {
            w += c > 0xFF ? 2 : 1;
        }
        return w;
    }

    /// <summary>
    /// 处理 Enter 提交：TextView 的 Accepting 事件触发。
    /// </summary>
    private void OnTextViewAccepting(object? sender, CommandEventArgs e)
    {
        var text = (_textView.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            e.Handled = true;
            return;
        }

        _textView.Text = string.Empty;
        Height = MinContentLines + 2;
        ContentHeightChanged?.Invoke(MinContentLines + 2);
        e.Handled = true;
        Submitted?.Invoke(text);
    }
}
