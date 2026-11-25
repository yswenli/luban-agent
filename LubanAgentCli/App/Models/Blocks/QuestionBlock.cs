/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCli.App.Models.Blocks
*文件名： QuestionBlock
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/24
*描述：Agent 提问内联块：逐题展示选项，支持多选与「其它」自由输入
*
*****************************************************************************/

// 消歧：全局 using 引入了 Spectre.Console，此处显式指向 Terminal.Gui 类型
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 提问内联块。一次展示一题，答完自动进入下一题；全部答完后一次性回传结果。
/// 键盘：↑↓ 移动、空格选中（多选切换）、Enter 确认本题、Tab 输入「其它」、Esc 跳过整组。
/// </summary>
public sealed class QuestionBlock : Block
{
    private readonly IReadOnlyList<UserQuestion> _questions;
    private readonly Action<UserQuestionResult> _onResolve;
    private readonly string _subtitle;
    private readonly List<List<string>> _answers;

    private int _index;
    private int _focus;
    private bool _editing;
    private string _free = "";
    private bool _resolved;

    /// <summary>创建提问块。</summary>
    /// <param name="questions">问题列表（1～4 个）。</param>
    /// <param name="onResolve">整组答完/取消后的回调。</param>
    /// <param name="origin">提问来源（编排子代理节点）；主代理为 null。</param>
    public QuestionBlock(
        IReadOnlyList<UserQuestion> questions,
        Action<UserQuestionResult> onResolve,
        ToolConfirmationOrigin? origin = null)
    {
        _questions = questions?.Count > 0
            ? questions
            : throw new ArgumentException("问题列表不能为空", nameof(questions));
        _onResolve = onResolve ?? throw new ArgumentNullException(nameof(onResolve));
        _subtitle = origin?.ToDisplayString() is { } text ? $"来源：{text}" : string.Empty;
        _answers = questions.Select(_ => new List<string>()).ToList();
    }

    /// <inheritdoc/>
    public override bool IsFoldable => false;

    /// <summary>块标题（用于诊断日志与「等待输入」提示）。</summary>
    public string Title => _questions.Count > 1
        ? $"❓ Agent 提问（共 {_questions.Count} 题）"
        : "❓ Agent 提问";

    /// <summary>当前题序号（0-based）。</summary>
    public int CurrentIndex => _index;

    /// <summary>是否已作答/取消。</summary>
    public bool IsResolved => _resolved;

    /// <summary>当前题目。</summary>
    public UserQuestion CurrentQuestion => _questions[Math.Clamp(_index, 0, _questions.Count - 1)];

    /// <inheritdoc/>
    public override void Layout(int width)
    {
        base.Layout(width);
        var w = Math.Max(1, width);
        var lines = 1; // 标题
        if (_subtitle.Length > 0) lines += WrapCount(_subtitle, w);
        lines += WrapCount(CurrentQuestion.Question, w);
        lines += CurrentQuestion.Options.Count;
        lines += 1; // 其它输入行
        lines += 1; // 操作提示
        LineCount = lines;
    }

    /// <inheritdoc/>
    public override void Render(List<RenderLine> lines, int width)
    {
        var w = Math.Max(1, width);

        lines.Add(RenderLine.Single(
            _resolved ? $"{Title} ✓" : Title, BlockColors.ConfirmTitle, TextStyle.Bold));

        if (_subtitle.Length > 0)
            AddWrapped(lines, _subtitle, w, BlockColors.System);

        var head = _questions.Count > 1
            ? $"[{_index + 1}/{_questions.Count}] {CurrentQuestion.Question}"
            : CurrentQuestion.Question;
        AddWrapped(lines, head, w, BlockColors.Accent);

        var options = CurrentQuestion.Options;
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var focused = !_resolved && !_editing && i == _focus;
            var picked = _answers[_index].Contains(option.Label);

            var marker = CurrentQuestion.MultiSelect
                ? (picked ? "[x]" : "[ ]")
                : (picked ? "(*)" : "( )");
            var prefix = focused ? "▶ " : "  ";
            var text = $"{prefix}{marker} {i + 1}. {option.Label}";
            if (!string.IsNullOrWhiteSpace(option.Description))
                text += $" — {option.Description}";
            if (text.Length > w) text = text[..w];

            var fg = picked
                ? BlockColors.Success
                : focused ? BlockColors.Accent : BlockColors.ConfirmTitle;
            lines.Add(RenderLine.Single(text, fg, focused ? TextStyle.Bold : TextStyle.None));
        }

        // 「其它」输入行：Tab 进入编辑，可选项与自由输入并存
        var freeText = _editing
            ? $"  ⌨ 其它：{_free}█"
            : string.IsNullOrWhiteSpace(_free)
                ? "  ⌨ 其它：按 Tab 输入自定义答案"
                : $"  ⌨ 其它：{_free}";
        if (freeText.Length > w) freeText = freeText[..w];
        lines.Add(RenderLine.Single(
            freeText,
            _editing ? BlockColors.Accent : BlockColors.System,
            _editing ? TextStyle.Bold : TextStyle.None));

        var hint = _editing
            ? "Enter 提交本题 · Backspace 删除 · Tab 退出输入"
            : "↑↓ 移动 · 空格选择 · Enter 确认 · Tab 输入其它 · Esc 跳过";
        AddWrapped(lines, hint, w, BlockColors.System);
    }

    /// <inheritdoc/>
    public override HitActionResult? HitTest(int localLine)
    {
        if (_resolved) return null;

        var subtitleLines = _subtitle.Length > 0 ? WrapCount(_subtitle, LastLayoutWidth) : 0;
        var questionLines = WrapCount(CurrentQuestion.Question, LastLayoutWidth);
        var optionStart = 1 + subtitleLines + questionLines;
        var index = localLine - optionStart;
        return index >= 0 && index < CurrentQuestion.Options.Count
            ? new HitActionResult(HitActionType.SelectOption, index)
            : null;
    }

    /// <summary>
    /// 处理按键。返回 true 表示已消费。
    /// </summary>
    /// <param name="key">按键。</param>
    /// <returns>已处理返回 true。</returns>
    public bool HandleKey(Key key)
    {
        if (_resolved) return false;

        if (key == Key.Esc)
        {
            Resolve(UserQuestionResult.Cancelled());
            return true;
        }

        if (key == Key.Tab)
        {
            _editing = !_editing;
            return true;
        }

        if (_editing)
        {
            if (key == Key.Enter)
            {
                CommitFreeInput();
                return true;
            }

            if (key == Key.Backspace)
            {
                if (_free.Length > 0) _free = _free[..^1];
                return true;
            }

            var c = (char)key;
            if (!char.IsControl(c))
            {
                _free += c;
                return true;
            }

            return false;
        }

        switch (key)
        {
            case var k when k == Key.CursorUp || k == Key.CursorLeft:
                _focus = _focus <= 0 ? CurrentQuestion.Options.Count - 1 : _focus - 1;
                return true;

            case var k when k == Key.CursorDown || k == Key.CursorRight:
                _focus = _focus >= CurrentQuestion.Options.Count - 1 ? 0 : _focus + 1;
                return true;

            case var k when k == Key.Space:
                Toggle(_focus);
                return true;

            case var k when k == Key.Enter:
                if (_answers[_index].Count == 0 && string.IsNullOrWhiteSpace(_free))
                    return true; // 未选任何项：不静默跳过，等用户选择
                Advance();
                return true;
        }

        // 数字键直接选择该选项；单选题选中即确认并进入下一题
        var digit = (char)key;
        if (digit is >= '1' and <= '9')
        {
            var optionIndex = digit - '1';
            if (optionIndex >= CurrentQuestion.Options.Count) return false;
            _focus = optionIndex;
            Toggle(optionIndex);
            if (!CurrentQuestion.MultiSelect) Advance();
            return true;
        }

        return false;
    }

    /// <summary>鼠标点击选项：单选立即确认，多选切换。</summary>
    /// <param name="index">选项索引。</param>
    public void SelectOption(int index)
    {
        if (_resolved || index < 0 || index >= CurrentQuestion.Options.Count) return;
        _focus = index;
        Toggle(index);
        if (!CurrentQuestion.MultiSelect) Advance();
    }

    /// <summary>取消整组提问。</summary>
    public void Cancel() => Resolve(UserQuestionResult.Cancelled());

    private void Toggle(int index)
    {
        var labels = _answers[_index];
        var label = CurrentQuestion.Options[index].Label;
        if (CurrentQuestion.MultiSelect)
        {
            if (!labels.Remove(label)) labels.Add(label);
        }
        else
        {
            labels.Clear();
            labels.Add(label);
        }
    }

    private void CommitFreeInput()
    {
        var text = _free.Trim();
        if (text.Length == 0)
        {
            _editing = false;
            return;
        }

        // 自由输入作为独立答案项（与选项并存时按「选项 + 其它」回传）
        if (!_answers[_index].Contains(text)) _answers[_index].Add(text);
        Advance();
    }

    private void Advance()
    {
        _index++;
        _focus = 0;
        _editing = false;
        _free = "";

        if (_index >= _questions.Count)
        {
            var answers = _questions
                .Select((q, i) => new UserQuestionAnswer(q.Question, _answers[i]))
                .ToList();
            Resolve(UserQuestionResult.Answered(answers));
        }
    }

    private void Resolve(UserQuestionResult result)
    {
        if (_resolved) return;
        _resolved = true;
        if (result.Outcome == UserQuestionOutcome.Cancelled && result.Message is null)
            result = UserQuestionResult.Cancelled();

        _onResolve(result);
    }

    private static int WrapCount(string text, int width)
    {
        var w = Math.Max(1, width);
        return string.IsNullOrEmpty(text) ? 0 : (text.Length + w - 1) / w;
    }

    private static void AddWrapped(List<RenderLine> lines, string text, int width, Color fg)
    {
        var span = text.AsSpan();
        while (span.Length > 0)
        {
            var take = Math.Min(width, span.Length);
            lines.Add(RenderLine.Single(span[..take].ToString(), fg));
            span = span[take..];
        }
    }
}