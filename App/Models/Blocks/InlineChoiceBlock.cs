/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： InlineChoiceBlock
*版本号： V1.0.0.0
*唯一标识：内联选择 Block
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：内联确认/选择公共组件。珊瑚红标题 + 选项行，双通道（键盘+鼠标）焦点同步
*
*****************************************************************************/
using LubanAgentCli.App.Models;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 内联选择 Block。公共确认/选择组件，用于工具确认、权限模式二次确认、
/// Plan 模式退出选项等场景。键盘（↑↓/快捷键/Enter）与鼠标点击双通道完全对等。
/// </summary>
public sealed class InlineChoiceBlock : Block
{
    private readonly List<ChoiceOption> _options;
    private int _focusedIndex;
    private ChoiceOption? _selected;
    private Action<ChoiceOption>? _onResolve;

    /// <summary>不可折叠（确认块必须始终可见）。</summary>
    public override bool IsFoldable => false;

    /// <summary>确认块标题（如 "⚠ WriteFileAsync"）。</summary>
    public string Title { get; }

    /// <summary>确认块描述/上下文说明。</summary>
    public string Description { get; }

    /// <summary>当前焦点选项索引（0-based）。键盘和鼠标共享，-1 表示无焦点。</summary>
    public int FocusedIndex
    {
        get => _focusedIndex;
        private set
        {
            if (_focusedIndex != value)
            {
                _focusedIndex = value;
                var handler = FocusedIndexChanged;
                handler?.Invoke();
            }
        }
    }

    /// <summary>已选中的选项，未选择时为 null。</summary>
    public ChoiceOption? Selected => _selected;

    /// <summary>选项列表。</summary>
    public IReadOnlyList<ChoiceOption> Options => _options;

    /// <summary>焦点索引变化事件（ViewModel 订阅后通知 View 刷新）。</summary>
    public event Action? FocusedIndexChanged;

    /// <summary>用户确认后触发（ViewModel 订阅后通知 View 变灰）。</summary>
    public event Action? Resolved;

    /// <summary>
    /// 初始化内联选择块。
    /// </summary>
    /// <param name="title">标题行（如 "⚠ WriteFileAsync"）。</param>
    /// <param name="description">描述行。</param>
    /// <param name="options">选项列表。</param>
    /// <param name="onResolve">选中回调（可选）。</param>
    public InlineChoiceBlock(
        string title,
        string description,
        IEnumerable<ChoiceOption> options,
        Action<ChoiceOption>? onResolve = null)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Description = description ?? string.Empty;
        _options = options?.ToList() ?? throw new ArgumentNullException(nameof(options));
        _onResolve = onResolve;

        if (_options.Count == 0)
        {
            throw new ArgumentException("选项列表不能为空", nameof(options));
        }
    }

    /// <inheritdoc/>
    public override void Layout(int width)
    {
        base.Layout(width);
        // 标题行 + 描述行（可能多行）+ N 个选项行
        var descLines = string.IsNullOrEmpty(Description) ? 0
            : Math.Max(1, (Description.Length + Math.Max(1, width) - 1) / Math.Max(1, width));
        LineCount = 1 + descLines + _options.Count;
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        var resolved = _selected is not null;

        // 标题行（珊瑚红）
        lines.Add(RenderLine.Single(
            resolved ? Title + " ✓" : Title,
            BlockColors.ConfirmTitle,
            TextStyle.Bold));

        // 描述行
        if (!string.IsNullOrEmpty(Description))
        {
            var remaining = Description.AsSpan();
            while (remaining.Length > 0)
            {
                var take = Math.Min(Math.Max(1, width), remaining.Length);
                lines.Add(RenderLine.Single(remaining[..take].ToString(), BlockColors.ConfirmTitle));
                remaining = remaining[take..];
            }
        }

        // 选项行
        var itemWidth = Math.Max(10, width - 2);

        for (var i = 0; i < _options.Count; i++)
        {
            var opt = _options[i];
            var isFocused = i == _focusedIndex && !resolved;

            var sb = new StringBuilder();
            if (resolved)
            {
                var marker = opt == _selected ? "●" : "○";
                sb.Append($"  {marker} [{opt.Key}] {opt.Label}");
            }
            else if (isFocused)
            {
                sb.Append($"▶ [{opt.Key}] {opt.Label}");
            }
            else
            {
                sb.Append($"  [{opt.Key}] {opt.Label}");
            }

            if (opt.Description is not null)
            {
                sb.Append($" — {opt.Description}");
            }

            var lineText = sb.ToString();
            if (lineText.Length > itemWidth)
            {
                lineText = lineText[..itemWidth];
            }

            var fg = resolved
                ? (opt == _selected ? BlockColors.Success : BlockColors.System)
                : (isFocused ? BlockColors.Accent : BlockColors.ConfirmTitle);

            var style = isFocused && !resolved ? TextStyle.Bold : TextStyle.None;

            lines.Add(RenderLine.Single(lineText, fg, style));
        }
    }

    /// <inheritdoc/>
    public override HitActionResult? HitTest(int localLine)
    {
        // 标题行为 0，跳过
        if (localLine == 0 || _selected is not null)
        {
            return null;
        }

        // 复用 Layout 已计算的 LineCount：LineCount = 1 + descLines + N
        // 选项起始行 = LineCount - _options.Count
        var optionStartLine = LineCount - _options.Count;
        var optionIndex = localLine - optionStartLine;

        if (optionIndex >= 0 && optionIndex < _options.Count)
        {
            return new HitActionResult(HitActionType.SelectOption, optionIndex);
        }

        return null;
    }

    /// <summary>
    /// 处理键盘输入。返回 true 表示已处理（调用方应停止传播）。
    /// </summary>
    /// <param name="key">按键事件。</param>
    /// <returns>已处理返回 true。</returns>
    public bool HandleKey(Key key)
    {
        if (_selected is not null)
        {
            return false; // 已确认，不再响应
        }

        // 快捷键：不区分大小写匹配。KeyCode 在 Terminal.Gui v2 中为 Key 枚举值，
        // 字母键 (Key.A..Key.Z) 的 KeyCode 可直接映射为字符。
        var c = (char)key;
        if (char.IsLetter(c))
        {
            foreach (var opt in _options)
            {
                if (char.ToUpperInvariant(opt.Key) == char.ToUpperInvariant(c))
                {
                    Select(_options.IndexOf(opt));
                    return true;
                }
            }
        }

        switch (key)
        {
            case var k when k == Key.CursorUp || k == Key.CursorLeft:
                FocusedIndex = _focusedIndex <= 0 ? _options.Count - 1 : _focusedIndex - 1;
                return true;

            case var k when k == Key.CursorDown || k == Key.CursorRight:
                FocusedIndex = _focusedIndex >= _options.Count - 1 ? 0 : _focusedIndex + 1;
                return true;

            case var k when k == Key.Enter || k == Key.Space:
                Select(_focusedIndex);
                return true;

            case var k when k == Key.Esc:
                SelectCancel();
                return true;
        }

        return false;
    }

    /// <summary>
    /// 根据命中测试结果或外部调用选中指定选项。
    /// </summary>
    /// <param name="index">选项索引。</param>
    public void Select(int index)
    {
        if (_selected is not null || index < 0 || index >= _options.Count)
        {
            return;
        }

        FocusedIndex = index;
        _selected = _options[index];
        _onResolve?.Invoke(_selected);
        _onResolve = null; // 释放闭包引用
        var handler = Resolved;
        handler?.Invoke();
    }

    /// <summary>
    /// 选中最小值等价于取消。如果选项列表中包含 Value 为 null 或 ConfirmResult.Deny 的项，
    /// 优先选中该项作为取消项；否则选中最后一个选项。
    /// </summary>
    private void SelectCancel()
    {
        var cancelIndex = _options.FindIndex(o => o.Value is ConfirmResult.Deny or null);
        if (cancelIndex < 0)
        {
            cancelIndex = _options.Count - 1;
        }

        Select(cancelIndex);
    }
}
