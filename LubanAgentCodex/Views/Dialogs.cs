/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： Dialogs
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/2
*描述：统一的确认 / 错误弹出层，复用 Dialogs.axaml 中的按钮与卡片样式
*
*****************************************************************************/

namespace LubanAgentCodex.Views;

/// <summary>
/// 统一的美观对话框（确认 / 错误）
/// </summary>
public static class Dialogs
{
    private static readonly SolidColorBrush WarningBrush = new(Color.Parse("#FFC107"));
    private static readonly SolidColorBrush ErrorBrush = new(Color.Parse("#F44336"));
    private static readonly SolidColorBrush TextPrimaryBrush = new(Colors.White);
    private static readonly SolidColorBrush TextSecondaryBrush = new(Color.Parse("#A0A0A0"));

    // 关闭非 ASCII 转义，避免参数中的中文显示为 \uXXXX
    private static readonly JsonSerializerOptions ArgumentsJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 取主题画刷；主题未提供时回退到给定画刷
    /// </summary>
    private static IBrush ResourceBrush(string key, IBrush fallback)
    {
        if (Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush)
            return brush;
        return fallback;
    }

    /// <summary>
    /// 工具执行确认对话框：工具名 + 参数(JSON) + 拒绝 / 本轮全部允许 / 本会话全部允许 / 允许
    /// </summary>
    /// <param name="owner">所属窗口；为空时退化为非模态展示（仍等待选择）</param>
    /// <param name="toolName">待确认的工具名</param>
    /// <param name="arguments">工具参数</param>
    /// <param name="origin">确认来源（编排子代理节点）；主代理为 null，不显示来源行</param>
    /// <param name="ct">取消令牌；取消时自动关闭对话框并返回 Deny</param>
    public static async Task<ConfirmResult> ShowToolConfirmAsync(
        Window? owner,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        ToolConfirmationOrigin? origin = null,
        CancellationToken ct = default)
    {
        var hasArgs = arguments.Count > 0;
        var originText = origin?.ToDisplayString();
        var dlg = new Window
        {
            Title = "工具确认",
            Width = 660,
            Height = (hasArgs ? 372 : 216) + (originText is null ? 0 : 30),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        dlg.Classes.Add("dlgWindow");

        var card = new Border { Classes = { "dlgCard" } };
        var stack = new StackPanel { Spacing = 12 };

        // 头部：图标 + 标题
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = "⚠",
            FontSize = 20,
            Foreground = WarningBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = "需要确认是否执行该工具",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimaryBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(header);

        // 工具名
        stack.Children.Add(new TextBlock
        {
            Text = toolName,
            FontFamily = new FontFamily("Consolas,Menlo,monospace"),
            FontSize = 13,
            Foreground = WarningBrush,
            TextWrapping = TextWrapping.Wrap,
        });

        // 来源行：编排子代理发起时单独一行标注节点/角色/描述，主代理发起时不占位
        if (originText is not null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"来源：{originText}",
                FontSize = 12,
                Foreground = TextSecondaryBrush,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // 参数(JSON)：仅在有参数时展示
        if (hasArgs)
        {
            var argsBox = new TextBox
            {
                Text = JsonSerializer.Serialize(arguments, ArgumentsJsonOptions),
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas,Menlo,monospace"),
                FontSize = 12,
                Foreground = TextSecondaryBrush,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 8),
                MaxHeight = 208,
            };
            stack.Children.Add(new Border
            {
                Background = ResourceBrush("SidebarBrush", new SolidColorBrush(Color.Parse("#1E1E1E"))),
                CornerRadius = new CornerRadius(6),
                Child = argsBox,
            });
        }

        // 按钮区（固定在右下角）
        var chosen = ConfirmResult.Deny;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var deny = new Button { Content = "✗ 拒绝" };
        deny.Classes.Add("dlgDanger");
        var allowAll = new Button { Content = "本轮全部允许", MinWidth = 124 };
        allowAll.Classes.Add("dlgGhost");
        var allowSession = new Button { Content = "本会话全部允许", MinWidth = 124 };
        allowSession.Classes.Add("dlgGhost");
        var allow = new Button { Content = "✓ 允许" };
        allow.Classes.Add("dlgPrimary");
        deny.Click += (s, e) => { chosen = ConfirmResult.Deny; dlg.Close(ConfirmResult.Deny); };
        allowAll.Click += (s, e) => { chosen = ConfirmResult.AllowAll; dlg.Close(ConfirmResult.AllowAll); };
        allowSession.Click += (s, e) => { chosen = ConfirmResult.AllowSession; dlg.Close(ConfirmResult.AllowSession); };
        allow.Click += (s, e) => { chosen = ConfirmResult.Allow; dlg.Close(ConfirmResult.Allow); };
        buttons.Children.Add(deny);
        buttons.Children.Add(allowAll);
        buttons.Children.Add(allowSession);
        buttons.Children.Add(allow);

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        Grid.SetRow(stack, 0);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(stack);
        grid.Children.Add(buttons);
        card.Child = grid;
        dlg.Content = card;

        // 取消令牌（Esc / 终止）：关闭对话框，按 Deny 收尾
        using var reg = ct.CanBeCanceled
            ? ct.Register(() => Dispatcher.UIThread.Post(() =>
            {
                if (dlg.IsVisible) dlg.Close(ConfirmResult.Deny);
            }))
            : default;

        if (owner != null)
            return await dlg.ShowDialog<ConfirmResult>(owner);

        // 无 owner：非模态展示，仍等待用户选择
        var tcs = new TaskCompletionSource<ConfirmResult>();
        dlg.Closed += (s, e) => tcs.TrySetResult(chosen);
        dlg.Show();
        return await tcs.Task;
    }

    /// <summary>
    /// 用户提问对话框：逐题展示选项（单选/多选）与「其它」自由输入，全部作答后统一提交。
    /// </summary>
    /// <param name="owner">所属窗口；为空时退化为非模态展示（仍等待作答）。</param>
    /// <param name="questions">问题列表（1～4 个）。</param>
    /// <param name="origin">提问来源（编排子代理节点）；主代理为 null，不显示来源行。</param>
    /// <param name="ct">取消令牌；取消时关闭对话框并返回「已取消」。</param>
    /// <returns>逐题回答或取消结果。</returns>
    public static async Task<UserQuestionResult> ShowQuestionsAsync(
        Window? owner,
        IReadOnlyList<UserQuestion> questions,
        ToolConfirmationOrigin? origin = null,
        CancellationToken ct = default)
    {
        var originText = origin?.ToDisplayString();
        var dlg = new Window
        {
            Title = questions.Count > 1 ? $"Agent 提问（{questions.Count} 个）" : "Agent 提问",
            Width = 680,
            Height = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
        };
        dlg.Classes.Add("dlgWindow");

        var card = new Border { Classes = { "dlgCard" } };
        var root = new StackPanel { Spacing = 12 };

        // 头部：图标 + 说明
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = "❓",
            FontSize = 20,
            Foreground = WarningBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = "Agent 需要你确认一些信息",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        root.Children.Add(header);

        if (originText is not null)
        {
            root.Children.Add(new TextBlock
            {
                Text = $"来源：{originText}",
                FontSize = 12,
                Foreground = TextSecondaryBrush,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var picks = questions.Select(_ => new List<string>()).ToList();
        var others = new List<TextBox>(questions.Count);
        var chosen = UserQuestionResult.Cancelled();
        var submit = new Button { Content = "提交", MinWidth = 96 };
        submit.Classes.Add("dlgPrimary");

        // 「每题至少选中一项或输入一段文字」才允许提交
        void RefreshSubmit() => submit.IsEnabled = questions
            .Select((_, i) => picks[i].Count > 0 || !string.IsNullOrWhiteSpace(others[i].Text))
            .All(ok => ok);

        var body = new StackPanel { Spacing = 16 };
        for (var i = 0; i < questions.Count; i += 1)
        {
            var question = questions[i];
            var section = new StackPanel { Spacing = 6 };
            var index = i;

            var title = string.IsNullOrWhiteSpace(question.Header)
                ? $"问题 {index + 1}"
                : $"{question.Header}";
            section.Children.Add(new TextBlock
            {
                Text = questions.Count > 1 ? $"{title}（{index + 1}/{questions.Count}）" : title,
                FontSize = 12.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = WarningBrush,
            });
            section.Children.Add(new TextBlock
            {
                Text = question.Question,
                FontSize = 13.5,
                Foreground = TextPrimaryBrush,
                TextWrapping = TextWrapping.Wrap,
            });

            if (question.MultiSelect)
            {
                foreach (var option in question.Options)
                {
                    var check = new CheckBox
                    {
                        Content = FormatOption(option),
                        Foreground = TextPrimaryBrush,
                    };
                    check.IsCheckedChanged += (s, e) =>
                    {
                        if (check.IsChecked == true)
                        {
                            if (!picks[index].Contains(option.Label)) picks[index].Add(option.Label);
                        }
                        else
                        {
                            picks[index].Remove(option.Label);
                        }

                        RefreshSubmit();
                    };
                    section.Children.Add(check);
                }
            }
            else
            {
                var group = $"q{index}";
                foreach (var option in question.Options)
                {
                    var radio = new RadioButton
                    {
                        GroupName = group,
                        Content = FormatOption(option),
                        Foreground = TextPrimaryBrush,
                    };
                    radio.IsCheckedChanged += (s, e) =>
                    {
                        if (radio.IsChecked != true) return;
                        picks[index].Clear();
                        picks[index].Add(option.Label);
                        RefreshSubmit();
                    };
                    section.Children.Add(radio);
                }
            }

            var other = new TextBox
            {
                PlaceholderText = "其它：直接输入你的答案（可留空）",
                AcceptsReturn = false,
            };
            other.TextChanged += (s, e) => RefreshSubmit();
            others.Add(other);
            section.Children.Add(other);

            body.Children.Add(section);
        }

        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 360,
        };
        root.Children.Add(scroll);

        // 按钮区
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var cancel = new Button { Content = "跳过作答" };
        cancel.Classes.Add("dlgGhost");
        cancel.Click += (s, e) => dlg.Close(UserQuestionResult.Cancelled());
        submit.Click += (s, e) =>
        {
            var answers = new List<UserQuestionAnswer>(questions.Count);
            for (var i = 0; i < questions.Count; i += 1)
            {
                var labels = new List<string>(picks[i]);
                var extra = others[i].Text?.Trim();
                if (!string.IsNullOrWhiteSpace(extra)) labels.Add(extra);
                if (labels.Count == 0) return;
                answers.Add(new UserQuestionAnswer(questions[i].Question, labels));
            }

            chosen = UserQuestionResult.Answered(answers);
            dlg.Close(chosen);
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(submit);

        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRow(root, 0);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(root);
        grid.Children.Add(buttons);
        card.Child = grid;
        dlg.Content = card;

        RefreshSubmit();

        // 取消令牌（Esc / 终止）：关闭对话框，按「已取消」收尾（不算拒绝、不失败本轮）
        using var reg = ct.CanBeCanceled
            ? ct.Register(() => Dispatcher.UIThread.Post(() =>
            {
                if (dlg.IsVisible) dlg.Close(UserQuestionResult.Cancelled());
            }))
            : default;

        if (owner != null)
        {
            return await dlg.ShowDialog<UserQuestionResult>(owner) ?? UserQuestionResult.Cancelled();
        }

        // 无 owner：非模态展示，仍等待作答
        var tcs = new TaskCompletionSource<UserQuestionResult>();
        dlg.Closed += (s, e) => tcs.TrySetResult(chosen);
        dlg.Show();
        return await tcs.Task;
    }

    private static string FormatOption(UserQuestionOption option)
        => string.IsNullOrWhiteSpace(option.Description)
            ? option.Label
            : $"{option.Label} — {option.Description}";

    /// <summary>
    /// 美观的确认对话框：图标 + 标题/说明 + 取消(幽灵)/确认(主或危险) 按钮
    /// </summary>
    public static async Task<bool> ShowConfirmAsync(
        Window? owner,
        string title,
        string message,
        string? detail = null,
        string okText = "确定",
        bool danger = false)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 384,
            Height = string.IsNullOrWhiteSpace(detail) ? 172 : 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        dlg.Classes.Add("dlgWindow");

        var card = new Border { Classes = { "dlgCard" } };
        var stack = new StackPanel { Spacing = 14 };

        // 头部：图标 + 标题
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = "⚠",
            FontSize = 20,
            Foreground = WarningBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimaryBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(header);

        if (!string.IsNullOrWhiteSpace(detail))
        {
            stack.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 12.5,
                Foreground = TextSecondaryBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(30, 0, 0, 0),
            });
        }

        // 按钮区（固定在右下角）
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var cancel = new Button { Content = "取消" };
        cancel.Classes.Add("dlgGhost");
        var ok = new Button { Content = okText };
        ok.Classes.Add(danger ? "dlgDanger" : "dlgPrimary");
        cancel.Click += (s, e) => dlg.Close(false);
        ok.Click += (s, e) => dlg.Close(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        Grid.SetRow(stack, 0);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(stack);
        grid.Children.Add(buttons);
        card.Child = grid;
        dlg.Content = card;

        if (owner == null)
        {
            dlg.Show();
            return false;
        }

        var result = await dlg.ShowDialog<bool?>(owner);
        return result == true;
    }

    /// <summary>
    /// 美观的错误提示对话框：错误图标 + 说明 + 确定(主) 按钮
    /// </summary>
    public static async Task ShowErrorAsync(Window? owner, string message)
    {
        var dlg = new Window
        {
            Title = "错误",
            Width = 384,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        dlg.Classes.Add("dlgWindow");

        var card = new Border { Classes = { "dlgCard" } };
        var stack = new StackPanel { Spacing = 16 };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = "⛔",
            FontSize = 20,
            Foreground = ErrorBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13.5,
            Foreground = TextPrimaryBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(header);

        var ok = new Button { Content = "确定", HorizontalAlignment = HorizontalAlignment.Right };
        ok.Classes.Add("dlgPrimary");
        ok.Click += (s, e) => dlg.Close();

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        Grid.SetRow(stack, 0);
        Grid.SetRow(ok, 1);
        grid.Children.Add(stack);
        grid.Children.Add(ok);
        card.Child = grid;
        dlg.Content = card;

        if (owner != null) await dlg.ShowDialog(owner);
        else dlg.Show();
    }

    /// <summary>
    /// 工作区下存在运行中的会话时给出错误提示并返回 true，调用方据此直接结束删除流程。
    /// </summary>
    /// <param name="owner">所属窗口。</param>
    /// <param name="isRunning">工作区运行态判定；为空表示无法判定，按「没有运行中会话」放行。</param>
    /// <param name="workspaceId">工作区标识。</param>
    /// <returns>true 表示已提示并应中止删除。</returns>
    /// <remarks>级联删掉会话之后本轮仍会继续写库，界面里的活动消息也会变成孤儿。</remarks>
    public static async Task<bool> RejectDeleteIfWorkspaceRunningAsync(
        Window? owner,
        Func<string, Task<bool>>? isRunning,
        string workspaceId)
    {
        if (isRunning is null || !await isRunning(workspaceId))
        {
            return false;
        }

        await ShowErrorAsync(owner, "该工作区下有正在进行中的会话，请先停止再删除");
        return true;
    }

    /// <summary>
    /// 美观的信息提示对话框：信息图标 + 说明 + 确定(主) 按钮
    /// </summary>
    public static async Task ShowInfoAsync(Window? owner, string message)
    {
        var dlg = new Window
        {
            Title = "提示",
            Width = 384,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        dlg.Classes.Add("dlgWindow");

        var card = new Border { Classes = { "dlgCard" } };
        var stack = new StackPanel { Spacing = 16 };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = "ℹ",
            FontSize = 20,
            Foreground = new SolidColorBrush(Color.Parse("#4FC3F7")),
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13.5,
            Foreground = TextPrimaryBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(header);

        var ok = new Button { Content = "确定", HorizontalAlignment = HorizontalAlignment.Right };
        ok.Classes.Add("dlgPrimary");
        ok.Click += (s, e) => dlg.Close();

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        Grid.SetRow(stack, 0);
        Grid.SetRow(ok, 1);
        grid.Children.Add(stack);
        grid.Children.Add(ok);
        card.Child = grid;
        dlg.Content = card;

        if (owner != null) await dlg.ShowDialog(owner);
        else dlg.Show();
    }
}
