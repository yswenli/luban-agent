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
using Command = Terminal.Gui.Input.Command;
using Key = Terminal.Gui.Input.Key;

namespace LubanAgentCli.App.Views;

/// <summary>
/// 自定义 Editor，处理 Enter 提交和 Shift+Enter 换行。
/// </summary>
internal sealed class MultilineEditor : Terminal.Gui.Editor.Editor
{
    public event Action<string>? SubmitRequested;

    /// <summary>请求打开文件选择对话框（Ctrl+O）。</summary>
    public event Action? OpenFilePickerRequested;

    /// <summary>所属输入区（用于拖拽识别与空文本提交）。</summary>
    internal InputBarView? Owner { get; set; }

    /// <summary>
    /// 按键预路由：当存在挂起的确认块（工具确认 / 授权二次确认）时由 RootView 注入，
    /// 把按键优先转发给确认块。agent 阻塞等待期间焦点停留在输入编辑器，确认块本身收不到键，
    /// 必须由这里转发，否则确认键到不了确认块、agent 会一直等到超时。
    /// </summary>
    public Func<Key, bool>? KeyPreRouter { get; set; }

    /// <summary>
    /// 预路由成功消费按键后通知上层重绘（确认块的选中/焦点高亮需要刷新）。
    /// </summary>
    public Action? OnPreRoutedKey { get; set; }

    protected override bool OnKeyDown(Key key)
    {
        Infrastructure.TuiDiag.KeyArrival();

        // 优先路由给挂起的确认块
        if (KeyPreRouter is not null && KeyPreRouter(key))
        {
            if (Infrastructure.TuiDiag.Enabled)
            {
                Logger.Warn($"[TuiDiag-Enter] key={key} consumed-by=KeyPreRouter(PendingChoice)");
            }
            OnPreRoutedKey?.Invoke();
            return true;
        }

        // Ctrl+V / Ctrl+Shift+V：直接调用框架内置的 Paste 命令。
        // Editor 覆盖 OnKeyDown 后不会处理 KeyBindings 中的 Ctrl+V 粘贴；
        // InvokeCommand(Command.Paste) 会执行框架的粘贴处理（读取 Application.Clipboard 并插入光标处），最可靠。
        if (key == Key.V.WithCtrl || key == Key.V.WithShift.WithCtrl)
        {
            InvokeCommand(Command.Paste);
            if (Infrastructure.TuiDiag.Enabled)
            {
                Logger.Warn($"[TuiDiag-Enter] key={key} branch=Paste(InvokeCommand)");
            }
            // 粘贴内容恰为一个存在的文件路径 → 直接转为附件，不留在输入区
            Owner?.TryConsumeDroppedPath();
            return true;
        }

        // Ctrl+O：打开文件选择对话框
        if (key == Key.O.WithCtrl)
        {
            OpenFilePickerRequested?.Invoke();
            return true;
        }

        // Shift+Enter 或 Ctrl+Enter 换行。
        // 必须走 Command.NewLine：ReplaceSelection 在无选区时是 no-op（插不进任何字符），
        // 而 NewLine 是 Editor 官方的 Enter 绑定，可同时复用其自动缩进策略。
        if (key == Key.Enter.WithShift || key == Key.Enter.WithCtrl)
        {
            InvokeCommand(Command.NewLine);
            if (Infrastructure.TuiDiag.Enabled)
            {
                Logger.Warn($"[TuiDiag-Enter] key={key} branch=NewLine");
            }
            return true;
        }

        if (key == Key.Enter)
        {
            var text = (Text ?? string.Empty).Trim();
            if (Infrastructure.TuiDiag.Enabled)
            {
                Logger.Warn($"[TuiDiag-Enter] key=Enter prerouter=not-consumed Text.Length='{(Text?.Length ?? 0)}' trimmed.Length='{text.Length}'");
            }
            if (text.Length > 0 || Owner?.HasAttachments == true)
            {
                if (Infrastructure.TuiDiag.Enabled)
                {
                    Logger.Warn($"[TuiDiag-Enter] key=Enter SUBMIT textLen={text.Length}");
                }
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
    private readonly List<AttachmentDraft> _drafts = new();
    private readonly List<string> _draftPaths = new();
    private Label? _attachmentHint;
    private IServiceProvider? _services;
    private ITuiUiService? _ui;

    /// <summary>
    /// 用户提交输入时触发（Enter）。
    /// </summary>
    public event Action<string>? Submitted;

    /// <summary>
    /// 用户提交输入（带附件）时触发。
    /// </summary>
    public event Action<string, IReadOnlyList<AttachmentDraft>>? SubmittedWithAttachments;

    /// <summary>注入服务提供者（由 RootView 在构造后调用）。</summary>
    /// <param name="services">服务提供者。</param>
    public void SetServiceProvider(IServiceProvider services) => _services = services;

    /// <summary>注入 TUI 模态交互服务（由 RootView 在构造后调用，非 DI 注册项）。</summary>
    /// <param name="ui">TUI 交互服务实例。</param>
    public void SetUiService(ITuiUiService ui) => _ui = ui;

    /// <summary>是否有待发送附件。</summary>
    public bool HasAttachments => _drafts.Count > 0;

    /// <summary>添加附件（去重，超过 5 个拒绝）。</summary>
    public bool AddAttachment(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        if (_draftPaths.Contains(full, StringComparer.OrdinalIgnoreCase)) return true;
        if (_drafts.Count >= 5) return false;
        _draftPaths.Add(full);
        _drafts.Add(new AttachmentDraft(full, Path.GetFileName(full)));
        UpdateDraftHint();
        return true;
    }

    /// <summary>清空待发送附件。</summary>
    public void ClearAttachments()
    {
        _drafts.Clear();
        _draftPaths.Clear();
        UpdateDraftHint();
    }

    private void UpdateDraftHint()
    {
        if (_attachmentHint == null) return;
        _attachmentHint.Text = _drafts.Count == 0
            ? string.Empty
            : string.Join(" ", _drafts.Select(d => $"[📎 {d.FileName}]"));
    }

    /// <summary>若编辑器整段内容恰为一个存在的文件路径（拖拽/粘贴路径），转为附件并清空输入。</summary>
    internal bool TryConsumeDroppedPath()
    {
        var line = _editor.Text?.Trim() ?? string.Empty;
        if (line.Length == 0 || line.Contains('\n')) return false;
        var resolved = ResolveExistingFile(line);
        if (resolved == null) return false;
        if (!AddAttachment(resolved)) return false;
        _editor.Text = string.Empty;
        return true;
    }

    private static string? ResolveExistingFile(string candidate)
    {
        try
        {
            var full = Path.GetFullPath(candidate.Trim('"'));
            return File.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>编辑器提交：解析 @引用 → 合并附件 → 触发提交事件。</summary>
    private void OnEditorSubmit(string text)
    {
        var body = text;

        // 解析 @引用：先校验无效引用（有则整条不发送、保留编辑器与草稿），再移除有效引用并加入附件
        if (_services?.GetService(typeof(AttachmentService)) is AttachmentService svc)
        {
            var refs = svc.ParseFileReferences(text, out body);
            var invalid = AttachmentService.FindUnresolvedReferences(text, svc);
            if (invalid.Count > 0)
            {
                _ui?.Notify("附件错误", $"文件不存在或类型不支持: {string.Join(", ", invalid)}");
                return;
            }

            foreach (var p in refs)
            {
                if (!AddAttachment(p))
                    _ui?.Notify("附件过多", $"每条消息最多 5 个附件，已忽略: {Path.GetFileName(p)}");
            }
        }

        var drafts = _drafts.ToList();
        ClearAttachments();
        _editor.Text = string.Empty;

        if (drafts.Count == 0)
            Submitted?.Invoke(body);
        else
            SubmittedWithAttachments?.Invoke(body, drafts);
    }

    /// <summary>
    /// 按键预路由（由 RootView 注入）：把按键优先转发给挂起的确认块。
    /// </summary>
    public Func<Key, bool>? KeyPreRouter
    {
        get => _editor.KeyPreRouter;
        set => _editor.KeyPreRouter = value;
    }

    /// <summary>
    /// 预路由成功消费按键后的重绘通知（由 RootView 注入）。
    /// </summary>
    public Action? OnPreRoutedKey
    {
        get => _editor.OnPreRoutedKey;
        set => _editor.OnPreRoutedKey = value;
    }

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

        _attachmentHint = new Label
        {
            X = 2,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            Text = string.Empty
        };
        _attachmentHint.SetScheme(bgScheme);

        _editor = new MultilineEditor
        {
            X = 2,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            CanFocus = true,
            Multiline = true,
            WordWrap = false,
        };
        _editor.SetScheme(bgScheme);
        _editor.Owner = this;
        _editor.SubmitRequested += OnEditorSubmit;

        // Ctrl+V 粘贴已在 OnKeyDown 中通过 InvokeCommand(Command.Paste) 处理，无需在此绑定 KeyBindings。

        // 解除 Editor 默认的 Tab/Shift+Tab 缩进/反缩进绑定，
        // 让这两个键能冒泡到 RootView 的全局快捷键处理
        // （Tab 切换任务视图、Shift+Tab 切换权限模式）
        _editor.KeyBindings.Remove(Key.Tab);
        _editor.KeyBindings.Remove(Key.Tab.WithShift);

        Add(_editor);
        Add(_attachmentHint);

        _editor.OpenFilePickerRequested += () =>
        {
            if (_ui is not null)
            {
                foreach (var p in _ui.PickFiles("选择附件", allowMultiple: true))
                {
                    if (!AddAttachment(p))
                        _ui.Notify("附件过多", $"每条消息最多 5 个附件，已忽略: {Path.GetFileName(p)}");
                }
            }
        };
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
