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
*Enter 提交，Shift+Enter 换行。输入 / 时弹出命令补全菜单。
*
*****************************************************************************/
using LubanAgentCore.Commands;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Clipboard = Terminal.Gui.App.Clipboard;
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
    /// 必须由这里转发，否则确认键到不了确认块、agent 会一直等待用户选择。
    /// </summary>
    public Func<Key, bool>? KeyPreRouter { get; set; }

    /// <summary>
    /// 预路由成功消费按键后通知上层重绘（确认块的选中/焦点高亮需要刷新）。
    /// </summary>
    public Action? OnPreRoutedKey { get; set; }

    protected override bool OnKeyDown(Key key)
    {
        Infrastructure.TuiDiag.KeyArrival();

        if (Infrastructure.TuiDiag.Enabled)
        {
            Logger.Warn($"[TuiDiag-Enter] MultilineEditor.OnKeyDown ENTER key={key}");
        }

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

        // 命令补全菜单可见时优先拦截导航键
        if (Owner is { } owner && owner.IsCompletionVisible)
        {
            if (key == Key.CursorUp || key == Key.CursorDown
                || key == Key.Tab || key == Key.Enter
                || key == Key.Esc)
            {
                var handled = owner.HandleCompletionKey(key);
                if (handled)
                {
                    return true;
                }
            }
        }

        // Ctrl+O：打开文件选择对话框
        if (key == Key.O.WithCtrl)
        {
            OpenFilePickerRequested?.Invoke();
            return true;
        }

        // Shift+Enter 或 Ctrl+Enter 换行。
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
            // 补全菜单可见时 Enter 选中项（不提交）
            if (Owner is { IsCompletionVisible: true } && Owner.HandleCompletionKey(Key.Enter))
            {
                return true;
            }

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
                Owner?.HideCompletion();
                SubmitRequested?.Invoke(text);
            }
            return true;
        }

        return base.OnKeyDown(key);
    }
}

/// <summary>
/// 命令补全菜单：浮在 Editor 上方的两列弹出视图。
/// 左列命令名（20 列宽），右列描述填充。整行高亮选中项（Accent 色，参考图1 salmon 色）。
/// 仅作为 InputBarView 子视图叠加在 Editor 之上；可见时由 MultilineEditor.OnKeyDown 拦截导航键。
/// </summary>
internal sealed class CommandCompletionPopup : View
{
    private IReadOnlyList<SlashCommandDefinition> _items = Array.Empty<SlashCommandDefinition>();
    private int _selected;
    private int _visibleRows;

    /// <summary>当前选中的命令；未选中或不可见返回 null。</summary>
    public SlashCommandDefinition? SelectedItem =>
        IsVisible && _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    /// <summary>补全菜单是否处于显示状态。</summary>
    public bool IsVisible { get; private set; }

    /// <summary>当前可见行数（用于定位计算）。</summary>
    public int VisibleRows => _visibleRows;

    /// <summary>显示补全菜单并设置匹配项。</summary>
    public void Show(IReadOnlyList<SlashCommandDefinition> items, int maxRows)
    {
        _items = items;
        _selected = 0;
        _visibleRows = Math.Min(items.Count, Math.Max(1, maxRows));
        IsVisible = items.Count > 0;
        Height = _visibleRows;
        Visible = IsVisible;
        if (IsVisible)
        {
            SetNeedsDraw();
        }
    }

    /// <summary>隐藏补全菜单。</summary>
    public void Hide()
    {
        IsVisible = false;
        Visible = false;
        _items = Array.Empty<SlashCommandDefinition>();
        SetNeedsDraw();
    }

    /// <summary>移动选中项（循环）。</summary>
    public void MoveSelection(int delta)
    {
        if (!IsVisible || _items.Count == 0) return;
        _selected = (_selected + delta + _items.Count) % _items.Count;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (!IsVisible || _items.Count == 0) return true;

        var width = Viewport.Width;
        if (width <= 0) return true;

        var nameCol = Math.Min(20, width / 2);

        for (var i = 0; i < _visibleRows && i < _items.Count; i++)
        {
            var cmd = _items[i];
            var name = cmd.Name;
            var desc = cmd.Description ?? string.Empty;

            var namePadded = name.Length >= nameCol
                ? name[..nameCol]
                : name.PadRight(nameCol);
            var descMax = Math.Max(0, width - nameCol);
            var descText = desc.Length > descMax ? desc[..descMax] : desc.PadRight(descMax);
            var line = namePadded + descText;

            var isSelected = i == _selected;
            var bg = isSelected ? TuiTheme.Accent : new Color(0x80, 0x80, 0x80);
            var fg = isSelected ? TuiTheme.Background : TuiTheme.AssistantText;
            SetAttribute(new Attribute(fg, bg));

            // 清除行背景（避免上一帧残留）
            for (var c = 0; c < width; c++)
            {
                AddRune(c, i, (Rune)' ');
            }
            AddStr(0, i, line);
        }
        return true;
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
    private readonly CommandCompletionPopup _completionPopup;
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private const int HistoryMaxSize = 50;
    private string _lastTextForCompletion = string.Empty;
    private Label? _attachmentHint;
    private IServiceProvider? _services;
    private ITuiUiService? _ui;
    private ICommandCatalog? _catalog;

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
    public void SetServiceProvider(IServiceProvider services)
    {
        _services = services;
        try
        {
            _catalog = services.GetService<ICommandCatalog>();
        }
        catch
        {
            // 容错：catalog 缺失时不影响输入框基础功能
        }
    }

    /// <summary>注入 TUI 模态交互服务（由 RootView 在构造后调用，非 DI 注册项）。</summary>
    /// <param name="ui">TUI 交互服务实例。</param>
    public void SetUiService(ITuiUiService ui) => _ui = ui;

    /// <summary>补全菜单是否可见。</summary>
    public bool IsCompletionVisible => _completionPopup.IsVisible;

    /// <summary>命令补全弹层视图（由 RootView 添加到顶层，实现向上展开）。</summary>
    public CommandCompletionPopup CompletionPopup => _completionPopup;

    /// <summary>补全菜单状态变化时触发（用于通知 RootView 重绘）。</summary>
    public event Action? CompletionStateChanged;

    /// <summary>是否有待发送附件。</summary>
    public bool HasAttachments => _drafts.Count > 0;

    /// <summary>
    /// 刷新补全菜单：根据当前文本过滤 catalog。首字符非 / 或无匹配时隐藏。
    /// </summary>
    internal void RefreshCompletion(string text)
    {
        if (_catalog is null)
        {
            HideCompletion();
            return;
        }

        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '/')
        {
            HideCompletion();
            return;
        }

        // 仅在首行触发（多行编辑时不弹）
        if (text.Contains('\n'))
        {
            HideCompletion();
            return;
        }

        var matches = _catalog.MatchByPrefix(trimmed, SlashCommandClient.Cli);
        if (matches.Count == 0)
        {
            HideCompletion();
            return;
        }

        _completionPopup.Show(matches, Math.Min(10, matches.Count));
        CompletionStateChanged?.Invoke();
    }

    /// <summary>
    /// 处理补全菜单导航键。返回 true 表示已消费（Enter/Tab 选中、Up/Down 移动、Esc 关闭）。
    /// </summary>
    internal bool HandleCompletionKey(Key key)
    {
        if (!_completionPopup.IsVisible) return false;

        if (key == Key.CursorUp)
        {
            _completionPopup.MoveSelection(-1);
            SetNeedsDraw();
            return true;
        }
        if (key == Key.CursorDown)
        {
            _completionPopup.MoveSelection(1);
            SetNeedsDraw();
            return true;
        }
        if (key == Key.Esc)
        {
            HideCompletion();
            return true;
        }
        if (key == Key.Enter || key == Key.Tab)
        {
            var selected = _completionPopup.SelectedItem;
            if (selected is null) return false;

            // 替换编辑器内容为命令名 + 空格，便于继续输入参数
            // Terminal.Gui v2 Editor 设置 Text 后光标自动跟随末尾
            _editor.Text = selected.Name + " ";
            HideCompletion();
            return true;
        }
        return false;
    }

    /// <summary>隐藏补全菜单。</summary>
    internal void HideCompletion()
    {
        if (_completionPopup.IsVisible)
        {
            _completionPopup.Hide();
            CompletionStateChanged?.Invoke();
        }
    }

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

        if (!string.IsNullOrWhiteSpace(body))
        {
            _history.Add(body);
            if (_history.Count > HistoryMaxSize)
                _history.RemoveAt(0);
            _historyIndex = _history.Count;
        }

        if (drafts.Count == 0)
            Submitted?.Invoke(body);
        else
            SubmittedWithAttachments?.Invoke(body, drafts);
    }

    internal bool NavigateHistory(int delta)
    {
        if (Infrastructure.TuiDiag.Enabled)
        {
            Logger.Warn($"[TuiDiag-Enter] NavigateHistory delta={delta} historyCount={_history.Count} historyIndex={_historyIndex}");
        }
        if (_history.Count == 0) return false;

        var document = _editor.Document;
        if (document is null) return false;

        var docLine = document.GetLineByOffset(_editor.CaretOffset);
        var line = docLine.LineNumber;
        var lineCount = document.LineCount;

        if (Infrastructure.TuiDiag.Enabled)
        {
            Logger.Warn($"[TuiDiag-Enter] NavigateHistory line={line} lineCount={lineCount}");
        }

        // DocumentLine.LineNumber 为 1-based（首行=1）：首行=line 1，末行=lineCount
        if (delta < 0 && line > 1) return false;
        if (delta > 0 && line < lineCount) return false;

        var newIndex = _historyIndex + delta;
        if (newIndex < 0 || newIndex > _history.Count) return false;

        _historyIndex = newIndex;
        if (newIndex == _history.Count)
        {
            _editor.Text = string.Empty;
        }
        else
        {
            _editor.Text = _history[newIndex];
        }
        if (Infrastructure.TuiDiag.Enabled)
        {
            Logger.Warn($"[TuiDiag-Enter] NavigateHistory success newIndex={newIndex}");
        }
        return true;
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

        _editor.ContentChanged += (sender, e) =>
        {
            var currentText = _editor.Text ?? string.Empty;
            if (currentText != _lastTextForCompletion)
            {
                _lastTextForCompletion = currentText;
                RefreshCompletion(currentText);
            }
        };

        _editor.KeyDown += (sender, e) =>
        {
            if (Infrastructure.TuiDiag.Enabled)
            {
                Logger.Warn($"[TuiDiag-Enter] Editor.KeyDown key={e}");
            }

            if (e == Key.CursorUp || e == Key.CursorDown)
            {
                if (IsCompletionVisible) return;
                if (NavigateHistory(e == Key.CursorUp ? -1 : 1))
                {
                    e.Handled = true;
                }
            }
            else if (e == Key.V.WithCtrl || e == Key.V.WithShift.WithCtrl)
            {
                try
                {
                    var clipboard = _editor.App?.Clipboard;
                    var clipText = clipboard is not null && clipboard.TryGetClipboardData(out var data) ? data : null;
                    if (Infrastructure.TuiDiag.Enabled)
                    {
                        Logger.Warn($"[TuiDiag-Enter] Paste read len={(clipText == null ? -1 : clipText.Length)} supported={(clipboard?.IsSupported.ToString() ?? "null")}");
                    }
                    if (!string.IsNullOrEmpty(clipText))
                    {
                        _editor.ReplaceSelection(clipText);
                        if (Infrastructure.TuiDiag.Enabled)
                        {
                            Logger.Warn($"[TuiDiag-Enter] Paste inserted len={clipText.Length}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (Infrastructure.TuiDiag.Enabled)
                    {
                        Logger.Warn($"[TuiDiag-Enter] Paste failed: {ex.Message}");
                    }
                }
                TryConsumeDroppedPath();
                RefreshCompletion(_editor.Text ?? string.Empty);
                e.Handled = true;
            }
            else if (e == Key.C.WithCtrl)
            {
                if (_editor.HasSelection)
                {
                    var selectedText = _editor.SelectedText;
                    if (!string.IsNullOrEmpty(selectedText))
                    {
                        try
                        {
                            _editor.App?.Clipboard?.TrySetClipboardData(selectedText);
                            if (Infrastructure.TuiDiag.Enabled)
                            {
                                Logger.Warn($"[TuiDiag-Enter] Copy len={selectedText.Length}");
                            }
                        }
                        catch (Exception ex)
                        {
                            if (Infrastructure.TuiDiag.Enabled)
                            {
                                Logger.Warn($"[TuiDiag-Enter] Copy failed: {ex.Message}");
                            }
                        }
                    }
                    e.Handled = true;
                }
            }
            else if (e == Key.X.WithCtrl)
            {
                if (_editor.HasSelection)
                {
                    var selectedText = _editor.SelectedText;
                    if (!string.IsNullOrEmpty(selectedText))
                    {
                        try
                        {
                            _editor.App?.Clipboard?.TrySetClipboardData(selectedText);
                            _editor.ReplaceSelection(string.Empty);
                            if (Infrastructure.TuiDiag.Enabled)
                            {
                                Logger.Warn($"[TuiDiag-Enter] Cut len={selectedText.Length}");
                            }
                        }
                        catch (Exception ex)
                        {
                            if (Infrastructure.TuiDiag.Enabled)
                            {
                                Logger.Warn($"[TuiDiag-Enter] Cut failed: {ex.Message}");
                            }
                        }
                        TryConsumeDroppedPath();
                        RefreshCompletion(_editor.Text ?? string.Empty);
                        e.Handled = true;
                    }
                }
            }
        };

        // 解除 Editor 默认的 Tab/Shift+Tab 缩进/反缩进绑定，
        // 让这两个键能冒泡到 RootView 的全局快捷键处理
        // （Tab 切换任务视图、Shift+Tab 切换权限模式）
        // 注意：补全菜单可见时 Tab 会被 OnKeyDown 顶部拦截选中项，不会触发任务切换
        _editor.KeyBindings.Remove(Key.Tab);
        _editor.KeyBindings.Remove(Key.Tab.WithShift);

        _completionPopup = new CommandCompletionPopup
        {
            Visible = false,
            CanFocus = false
        };

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
