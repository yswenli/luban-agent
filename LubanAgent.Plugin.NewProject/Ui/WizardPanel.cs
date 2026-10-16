/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Ui
*文件名： WizardPanel
*创建时间：2026/10/16
*描述：新建项目向导面板（PRD §4.6）：左侧步骤条 + 表单区，右侧 Agent 操作记录区
*****************************************************************************/

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LuBan.AIAgent.Plugins;
using LubanAgent.Plugin.NewProject.Abstractions;

namespace LubanAgent.Plugin.NewProject.Ui;

/// <summary>
/// 新建项目向导面板：同时实现 Avalonia <see cref="UserControl"/> 与框架 <see cref="IPluginPanel"/>，
/// 由宿主内容区强转挂载（PRD §3.4「面板约定」）。
/// </summary>
/// <remarks>
/// <para>
/// <b>布局（PRD §4.6）</b>：左侧七步步骤条 + 当前步表单；右侧「Agent 操作记录」实时时间线，
/// 让用户看到每一步做了什么、结果如何，并可就地在失败步骤重试。
/// </para>
/// <para>
/// <b>本阶段（M4-P2）范围</b>：Step 1 真表单（名称 / 缩写 / 介绍 / 端选择 / 本地目录 + 浏览）与
/// Step 2 初始化触发已接入，校验与执行全部委托共享能力层（面板只做展示与投递，零业务规则）；
/// Step 3~7 的操作区在 P3 起逐步接入。面板不直接触碰 <c>Dispatcher</c>，UI 投递一律经插件传入的
/// <c>PostToUi</c> 令牌（PRD §6 #22）。
/// </para>
/// <para>
/// 纯代码构建 UI（无 XAML）：插件以独立 ALC 加载，避免 XAML 编译产物带来的额外加载面。
/// </para>
/// </remarks>
public sealed class WizardPanel : UserControl, IPluginPanel
{
    private static readonly IBrush PageBackground = new SolidColorBrush(Color.Parse("#1E1E2E"));
    private static readonly IBrush CardBackground = new SolidColorBrush(Color.Parse("#181825"));
    private static readonly IBrush DividerBrush = new SolidColorBrush(Color.Parse("#313244"));
    private static readonly IBrush PrimaryText = new SolidColorBrush(Color.Parse("#CDD6F4"));
    private static readonly IBrush MutedText = new SolidColorBrush(Color.Parse("#A6ADC8"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#89B4FA"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#A6E3A1"));
    private static readonly IBrush DangerBrush = new SolidColorBrush(Color.Parse("#F38BA8"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F9E2AF"));

    /// <summary>七步定义（PRD §4.3）。标题不含序号：序号由左侧徽标承载，避免「③ 3 建库」重复。</summary>
    private static readonly (WizardStep Step, string Title, string Summary)[] Steps =
    {
        (WizardStep.BasicInfo, "基本信息", "名称 / 缩写 / 介绍 / 端 / 本地目录"),
        (WizardStep.Scaffold, "初始化", "工作区 · 会话 · 技能 · 云效 · 项目助手"),
        (WizardStep.Repositories, "建库", "以模板建各端仓库并克隆到本地"),
        (WizardStep.Database, "数据库", "生成配置 · 执行建库脚本"),
        (WizardStep.Build, "编译", "逐端编译并汇总结果"),
        (WizardStep.Push, "推送", "提交并推送到各端专属仓库"),
        (WizardStep.Pipeline, "流水线", "逐端克隆参考流水线并创建")
    };

    private readonly INewProjectService _service;
    private readonly Action<Action> _postToUi;

    private readonly List<StepView> _stepViews = new();
    private readonly TextBlock _archiveText;
    private readonly TextBlock _stepHint;
    private readonly StackPanel _timeline;

    // ── Step 1 表单 ──────────────────────────────────────────────────────────────
    private readonly TextBox _nameBox = new() { PlaceholderText = "例如：津南民生实事" };
    private readonly TextBox _codeBox = new() { PlaceholderText = "例如：jnms（小写字母开头，只能小写字母或数字）" };
    private readonly TextBox _introBox = new()
    {
        PlaceholderText = "一句话说明项目用途（将写入项目助手上下文）",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 64
    };

    private readonly TextBox _pathBox = new() { PlaceholderText = @"例如：D:\Work\jnms（作为 Step 2 的工作区根目录）" };
    private readonly CheckBox _serviceCheck = new() { Content = "后端 Service（必选）", IsChecked = true, IsEnabled = false };
    private readonly CheckBox _adminCheck = new() { Content = "管理端 Admin（必选）", IsChecked = true, IsEnabled = false };
    private readonly CheckBox _mobileCheck = new() { Content = "移动端 Mobile（可选）" };
    private readonly Button _browseButton = new() { Content = "浏览…" };
    private readonly Button _nextButton = new() { Content = "下一步：初始化项目" };
    private readonly Button _adoptButton = new() { Content = "复用该工作区继续", IsVisible = false };
    private readonly Button _rerunButton = new() { Content = "重跑 Step 2" };
    private readonly Button _resetButton = new() { Content = "中止并清理" };
    private readonly TextBlock _validationText;
    private readonly TextBlock _warningText;
    private readonly TextBlock _busyText;
    private readonly Border _resumeBanner;
    private readonly TextBlock _resumeText;

    /// <summary>
    /// 残留档案是否已由用户明确处置（PRD §4.7：禁止静默覆盖上一次未完成的档案）。
    /// </summary>
    private bool _resumeResolved = true;

    /// <summary>
    /// 最近一次校验命中唯一性口径①的已登记工作区（<c>null</c> 即未命中）。
    /// </summary>
    /// <remarks>
    /// 命中时面板给出「复用该工作区继续」出口——否则用户会卡在「错误文案让他点某个按钮、
    /// 而那个按钮只在另一种状态下存在」的死角（P2 冒烟缺陷）。
    /// </remarks>
    private string? _existingWorkspaceId;

    /// <summary>命中唯一性口径①的工作区名称（仅用于文案）。</summary>
    private string? _existingWorkspaceName;

    private bool _busy;
    private bool _unloaded;
    private bool _subscribed;

    /// <summary>
    /// 构造面板。
    /// </summary>
    /// <param name="service">共享能力层（与聊天入口同一实例）。</param>
    /// <param name="postToUi">
    /// UI 线程投递器（插件 <c>PostToUi</c>）：面板所有跨线程刷新都必须经它，得不到令牌即丢弃。
    /// </param>
    public WizardPanel(INewProjectService service, Action<Action> postToUi)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));

        _archiveText = new TextBlock
        {
            Text = "尚未开始新项目",
            FontSize = 13,
            Foreground = MutedText,
            TextWrapping = TextWrapping.Wrap
        };

        _stepHint = new TextBlock
        {
            Text = string.Empty,
            FontSize = 13,
            Foreground = AccentBrush,
            TextWrapping = TextWrapping.Wrap
        };

        _validationText = new TextBlock
        {
            FontSize = 12,
            Foreground = DangerBrush,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        _warningText = new TextBlock
        {
            FontSize = 12,
            Foreground = WarningBrush,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        _busyText = new TextBlock
        {
            FontSize = 12,
            Foreground = AccentBrush,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };

        _resumeText = new TextBlock
        {
            FontSize = 12,
            Foreground = PrimaryText,
            TextWrapping = TextWrapping.Wrap
        };

        _resumeBanner = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = CardBackground,
            BorderThickness = new Thickness(1),
            BorderBrush = AccentBrush,
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    _resumeText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { BuildResumeKeepButton(), BuildResumeDiscardButton() }
                    }
                }
            }
        };

        _timeline = new StackPanel { Spacing = 8 };

        // 残留档案未处置前禁止「下一步」，避免静默覆盖（PRD §4.7）
        _resumeResolved = _service.Current is null;

        _browseButton.Click += OnBrowseClicked;
        _nextButton.Click += OnNextClicked;
        _adoptButton.Click += OnAdoptClicked;
        _rerunButton.Click += OnRerunClicked;
        _resetButton.Click += OnAbortCleanupClicked;

        // 三列必须显式设列号：Grid 子控件不设列号会全部落在第 0 列相互重叠（P2 冒烟缺陷）
        var left = BuildLeftColumn();
        var divider = BuildDivider();
        var right = BuildRightColumn();
        Grid.SetColumn(left, 0);
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(right, 2);

        Content = new Border
        {
            Background = PageBackground,
            Child = new Grid
            {
                // 左：向导区（更宽）· 中：1px 分隔 · 右：Agent 操作记录（PRD §4.6.1 草图）
                ColumnDefinitions = new ColumnDefinitions("3*,1,2*"),
                Children = { left, divider, right }
            }
        };

        AttachStepViewGrids();
        Refresh();

        _service.Changed += OnServiceChanged;
        _subscribed = true;
    }

    /// <inheritdoc />
    public string Title => "新建项目";

    /// <inheritdoc />
    /// <remarks>
    /// 卸载前由宿主调用：解绑共享能力层事件，避免卸载后回调触碰已卸载程序集（PRD §6 #20 / #22）。
    /// </remarks>
    public void OnUnloading()
    {
        _unloaded = true;

        if (_subscribed)
        {
            _service.Changed -= OnServiceChanged;
            _subscribed = false;
        }
    }

    /// <summary>
    /// 左列：步骤条 + 当前步表单占位。
    /// </summary>
    private Control BuildLeftColumn()
    {
        var steps = new StackPanel { Spacing = 6 };
        foreach (var (step, title, summary) in Steps)
        {
            var badgeText = new TextBlock
            {
                Text = ((int)step).ToString(),
                FontSize = 12,
                Foreground = MutedText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var badge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush = DividerBrush,
                Background = Brushes.Transparent,
                Child = badgeText,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 14,
                Foreground = PrimaryText
            };

            var summaryBlock = new TextBlock
            {
                Text = summary,
                FontSize = 11,
                Foreground = MutedText,
                TextWrapping = TextWrapping.Wrap
            };

            var statusBlock = new TextBlock
            {
                Text = "未开始",
                FontSize = 11,
                Foreground = MutedText,
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    badge,
                    new StackPanel
                    {
                        Margin = new Thickness(10, 0, 10, 0),
                        Spacing = 2,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { titleBlock, summaryBlock }
                    },
                    statusBlock
                }
            };
            Grid.SetColumn(row.Children[1], 1);
            Grid.SetColumn(statusBlock, 2);

            steps.Children.Add(new Border
            {
                Padding = new Thickness(10, 6),
                CornerRadius = new CornerRadius(6),
                Child = row
            });

            _stepViews.Add(new StepView(step, badge, badgeText, statusBlock));
        }

        var form = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = CardBackground,
            BorderThickness = new Thickness(1),
            BorderBrush = DividerBrush,
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Step 1 · 项目基本信息", FontSize = 14, Foreground = PrimaryText },
                    new TextBlock
                    {
                        Text = "名称 / 缩写 / 介绍 / 端选择 / 本地目录；点「下一步」后立即自动执行 Step 2 初始化"
                             + "（建工作区 → 授权 → 会话 → 技能 → 云效），全过程实时记录在右侧。",
                        FontSize = 12,
                        Foreground = MutedText,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new Border { Height = 1, Background = DividerBrush },
                    LabeledField("项目名称", _nameBox),
                    LabeledField("英文缩写", _codeBox),
                    LabeledField("项目介绍", _introBox),
                    LabeledField("端选择", new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 16,
                        Children = { _serviceCheck, _adminCheck, _mobileCheck }
                    }),
                    LabeledField("本地目录", BuildPathRow()),
                    _validationText,
                    _warningText,
                    _busyText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { _nextButton, _adoptButton, _rerunButton, _resetButton }
                    },
                    _resumeBanner
                }
            }
        };

        return new ScrollViewer
        {
            Padding = new Thickness(20),
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "新建项目", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = PrimaryText },
                    new TextBlock
                    {
                        Text = "以 TJCES 三端模板仓创建新项目：后端与管理端必选，移动端可选。",
                        FontSize = 12,
                        Foreground = MutedText,
                        TextWrapping = TextWrapping.Wrap
                    },
                    _archiveText,
                    _stepHint,
                    new Border { Height = 1, Background = DividerBrush, Margin = new Thickness(0, 4) },
                    steps,
                    form
                }
            }
        };
    }

    /// <summary>
    /// 「标签 + 控件」竖向字段块。
    /// </summary>
    /// <param name="label">字段名。</param>
    /// <param name="input">输入控件。</param>
    /// <returns>字段块。</returns>
    private static Control LabeledField(string label, Control input) => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = label, FontSize = 12, Foreground = MutedText },
            input
        }
    };

    /// <summary>本地目录行：输入框 + 「浏览…」按钮。</summary>
    /// <returns>行控件。</returns>
    private Control BuildPathRow()
    {
        _browseButton.Margin = new Thickness(8, 0, 0, 0);
        _browseButton.VerticalAlignment = VerticalAlignment.Center;

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { _pathBox, _browseButton }
        };
        Grid.SetColumn(_browseButton, 1);
        return row;
    }

    /// <summary>残留档案处置按钮：保留并继续。</summary>
    private Button BuildResumeKeepButton()
    {
        var keep = new Button { Content = "保留并继续" };
        keep.Click += OnResumeKeepClicked;
        return keep;
    }

    /// <summary>残留档案处置按钮：清理后重来。</summary>
    private Button BuildResumeDiscardButton()
    {
        var discard = new Button { Content = "清理后重来" };
        discard.Click += OnResumeDiscardClicked;
        return discard;
    }

    /// <summary>
    /// 中列：1px 垂直分隔线。
    /// </summary>
    private static Control BuildDivider() => new Border { Background = DividerBrush };

    /// <summary>
    /// 右列：Agent 操作记录区（时间线）。
    /// </summary>
    private Control BuildRightColumn()
    {
        var column = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Agent 操作记录", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = PrimaryText },
                new TextBlock
                {
                    Text = "每一步执行（云效调用、克隆、编译、建流水线…）都会实时记录在此，"
                         + "失败时可就地查看原因并在左侧重试；凭据一律以 *** 呈现（PRD §4.6 / §4.8）。",
                    FontSize = 12,
                    Foreground = MutedText,
                    TextWrapping = TextWrapping.Wrap
                },
                new Border { Height = 1, Background = DividerBrush },
                _timeline
            }
        };

        return new ScrollViewer { Padding = new Thickness(20), Content = column };
    }

    /// <summary>
    /// 服务事件 → UI 线程刷新。
    /// </summary>
    private void OnServiceChanged(object? sender, EventArgs e) => _postToUi(Refresh);

    /// <summary>
    /// 按当前档案与操作记录刷新界面。
    /// </summary>
    private void Refresh()
    {
        var spec = _service.Current;
        var currentStep = _service.CurrentStep;

        _archiveText.Text = spec is null
            ? "尚未开始新项目：在 Step 1 填表后创建档案；"
              + "聊天入口说「新建一个项目，缩写 jnms」也会落到同一档案。"
            : $"项目档案：{spec.Name}（{spec.Code}）· 端 {string.Join("/", spec.Ends)} · 本地目录 "
              + $"{(string.IsNullOrWhiteSpace(spec.LocalPath) ? "未设置" : spec.LocalPath)}";

        _stepHint.Text = spec is null
            ? "当前步骤：1 基本信息（待填表）"
            : $"当前步骤：{(int)currentStep} {Steps[(int)currentStep - 1].Title}";

        if (spec is not null && !_resumeResolved)
        {
            _resumeText.Text = $"检测到上次未完成的项目档案：{spec.Name}（{spec.Code}）· 已推进到 {(int)spec.CurrentStep} 步"
                               + $"{(string.IsNullOrWhiteSpace(spec.WorkspaceId) ? string.Empty : " · 工作区已建立")}。"
                               + "请先选择处置方式，「下一步」在处置前不可用（不会静默覆盖）。";
        }

        SyncFormEnabled();

        foreach (var view in _stepViews)
        {
            var isCurrent = spec is not null && view.Step == currentStep;
            var isDone = spec is not null && (int)view.Step < (int)currentStep;

            view.Badge.Background = isDone ? SuccessBrush : isCurrent ? AccentBrush : Brushes.Transparent;
            view.Badge.BorderBrush = isDone ? SuccessBrush : isCurrent ? AccentBrush : DividerBrush;
            view.BadgeText.Foreground = isDone || isCurrent ? PageBackground : MutedText;
            view.Status.Foreground = isDone ? SuccessBrush : isCurrent ? AccentBrush : MutedText;
            view.Status.Text = isDone ? "已完成" : isCurrent ? "进行中" : "未开始";
        }

        var operations = _service.Operations;
        _timeline.Children.Clear();

        if (operations.Count == 0)
        {
            _timeline.Children.Add(new TextBlock
            {
                Text = "暂无操作记录。",
                FontSize = 12,
                Foreground = MutedText
            });
            return;
        }

        foreach (var record in operations)
        {
            var statusBrush = record.Status switch
            {
                StepStatus.Succeeded => SuccessBrush,
                StepStatus.Failed => DangerBrush,
                StepStatus.Running => AccentBrush,
                _ => MutedText
            };

            _timeline.Children.Add(new Border
            {
                Padding = new Thickness(12, 8),
                CornerRadius = new CornerRadius(6),
                Background = CardBackground,
                BorderThickness = new Thickness(1),
                BorderBrush = DividerBrush,
                Child = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{record.Timestamp:HH:mm:ss} · {(int)record.Step} 步 · {record.Action}",
                            FontSize = 12,
                            Foreground = statusBrush
                        },
                        new TextBlock
                        {
                            Text = record.Detail,
                            FontSize = 12,
                            Foreground = PrimaryText,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = $"状态 {StatusText(record.Status)} · 耗时 {FormatDuration(record.Duration)}",
                            FontSize = 11,
                            Foreground = MutedText
                        }
                    }
                }
            });
        }
    }

    /// <summary>
    /// 步骤条行的视觉载体（步骤号 + 序号徽标 + 状态文字）。
    /// </summary>
    private sealed record StepView(WizardStep Step, Border Badge, TextBlock BadgeText, TextBlock Status);

    /// <summary>步骤状态中文名（PRD §4.6.1 草图用语）。</summary>
    /// <param name="status">状态。</param>
    /// <returns>中文状态。</returns>
    private static string StatusText(StepStatus status) => status switch
    {
        StepStatus.Succeeded => "成功",
        StepStatus.Failed => "失败",
        StepStatus.Running => "进行中",
        StepStatus.Skipped => "已跳过",
        _ => "未开始"
    };

    /// <summary>耗时展示：未计时/延迟动作显示「—」，亚秒显示毫秒。</summary>
    /// <param name="duration">耗时。</param>
    /// <returns>可读耗时。</returns>
    private static string FormatDuration(TimeSpan duration)
        => duration <= TimeSpan.Zero
            ? "—"
            : duration.TotalSeconds < 1
                ? $"{duration.TotalMilliseconds:F0}ms"
                : $"{duration.TotalSeconds:F1}s";

    /// <summary>
    /// 按当前繁忙 / 残留状态同步按钮与横幅可用性。
    /// </summary>
    private void SyncFormEnabled()
    {
        var hasArchive = _service.Current is not null;
        var blockedByResidual = hasArchive && !_resumeResolved;

        _resumeBanner.IsVisible = blockedByResidual;

        _nameBox.IsEnabled = !_busy;
        _codeBox.IsEnabled = !_busy;
        _introBox.IsEnabled = !_busy;
        _pathBox.IsEnabled = !_busy;
        _mobileCheck.IsEnabled = !_busy;
        _browseButton.IsEnabled = !_busy;

        _nextButton.IsEnabled = !_busy && !blockedByResidual;

        // 「复用该工作区继续」只在「最近一次校验命中唯一性①」且「尚无档案」时出现：
        // 有档案时正确入口是「重跑 Step 2」（复用同一档案的工作区），两者不并存以免误导。
        _adoptButton.IsVisible = !_busy && _existingWorkspaceId is not null && !hasArchive;
        _adoptButton.IsEnabled = _adoptButton.IsVisible;

        _rerunButton.IsVisible = hasArchive && _resumeResolved;
        _rerunButton.IsEnabled = _rerunButton.IsVisible && !_busy;
        _resetButton.IsEnabled = !_busy;
    }

    /// <summary>切换忙碌态并给出阶段提示。</summary>
    /// <param name="busy">是否忙碌。</param>
    /// <param name="message">阶段提示（<c>null</c> 即隐藏）。</param>
    private void SetBusy(bool busy, string? message)
    {
        _busy = busy;
        _busyText.Text = message ?? string.Empty;
        _busyText.IsVisible = !string.IsNullOrWhiteSpace(message);
        SyncFormEnabled();
    }

    /// <summary>展示字段级校验错误（空集合即清除）。</summary>
    /// <param name="errors">错误文案。</param>
    private void ShowErrors(IEnumerable<string> errors)
    {
        var list = errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToList();
        _validationText.Text = string.Join("\n", list.Select(error => "• " + error));
        _validationText.IsVisible = list.Count > 0;
    }

    /// <summary>展示非阻断提示（空集合即清除）；与错误区分色，且不阻止「下一步」。</summary>
    /// <param name="warnings">提示文案。</param>
    private void ShowWarnings(IEnumerable<string> warnings)
    {
        var list = warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)).ToList();
        _warningText.Text = string.Join("\n", list.Select(warning => "• " + warning));
        _warningText.IsVisible = list.Count > 0;
    }

    /// <summary>记住最近一次校验命中的已登记工作区（供「复用该工作区继续」出口使用）。</summary>
    /// <param name="validation">校验结果。</param>
    private void RememberExistingWorkspace(BasicInfoValidation validation)
    {
        _existingWorkspaceId = validation.ExistingWorkspaceId;
        _existingWorkspaceName = validation.ExistingWorkspaceName;
    }

    /// <summary>读取端选择。</summary>
    /// <returns>所选端（后端 / 管理端恒在）。</returns>
    private List<ProjectEnd> SelectedEnds()
    {
        var ends = new List<ProjectEnd>();
        if (_serviceCheck.IsChecked == true) ends.Add(ProjectEnd.Service);
        if (_adminCheck.IsChecked == true) ends.Add(ProjectEnd.Admin);
        if (_mobileCheck.IsChecked == true) ends.Add(ProjectEnd.Mobile);
        return ends;
    }

    /// <summary>把表单清空（不动档案）。</summary>
    private void ClearForm()
    {
        _nameBox.Text = string.Empty;
        _codeBox.Text = string.Empty;
        _introBox.Text = string.Empty;
        _pathBox.Text = string.Empty;
        _mobileCheck.IsChecked = false;
        _existingWorkspaceId = null;
        _existingWorkspaceName = null;
        ShowErrors(Array.Empty<string>());
        ShowWarnings(Array.Empty<string>());
    }

    /// <summary>「下一步」：校验 → 建档案 → 立即执行 Step 2（PRD §4.3）。</summary>
    private async void OnNextClicked(object? sender, RoutedEventArgs e) => await ProceedAsync(false);

    /// <summary>
    /// 「复用该工作区继续」：唯一性①命中后的显式出口（用户确认复用已登记工作区，不静默覆盖）。
    /// </summary>
    private async void OnAdoptClicked(object? sender, RoutedEventArgs e) => await ProceedAsync(true);

    /// <summary>
    /// Step 1 主流程：校验 → 建档案 → 立即执行 Step 2（PRD §4.3）。
    /// </summary>
    /// <param name="allowExistingWorkspace">
    /// 是否放行唯一性口径①（该目录已被登记为工作区）；仅「复用该工作区继续」传 <c>true</c>。
    /// </param>
    private async Task ProceedAsync(bool allowExistingWorkspace)
    {
        if (_busy || _unloaded)
            return;

        ShowErrors(Array.Empty<string>());
        ShowWarnings(Array.Empty<string>());
        var name = _nameBox.Text ?? string.Empty;
        var code = _codeBox.Text ?? string.Empty;
        var introduction = _introBox.Text ?? string.Empty;
        var localPath = _pathBox.Text ?? string.Empty;
        var ends = SelectedEnds();

        SetBusy(true, allowExistingWorkspace
            ? "正在校验并复用已登记工作区…"
            : "正在校验项目基本信息…");
        try
        {
            var validation = await _service
                .ValidateBasicInfoAsync(name, code, introduction, ends, localPath, allowExistingWorkspace)
                .ConfigureAwait(true);
            if (_unloaded)
                return;

            RememberExistingWorkspace(validation);
            ShowWarnings(validation.Warnings);

            if (!validation.IsValid)
            {
                ShowErrors(validation.Errors);
                SetBusy(false, null);
                return;
            }

            _resumeResolved = true;
            _service.StartNewProject(name, code, introduction, ends, localPath);

            SetBusy(true, allowExistingWorkspace
                ? $"正在复用工作区{(_existingWorkspaceName is null ? string.Empty : $"「{_existingWorkspaceName}」")}"
                  + "并执行 Step 2（授权 → 会话 → 技能 → 云效）…"
                : "正在初始化项目环境（Step 2）：建工作区 → 授权 → 会话 → 技能 → 云效…");
            var result = await _service.InitializeAsync().ConfigureAwait(true);
            if (_unloaded)
                return;

            if (result.Status != StepStatus.Succeeded)
                ShowErrors(new[] { result.Message ?? "Step 2 初始化失败，详见右侧操作记录。" });
        }
        catch (Exception ex)
        {
            if (!_unloaded)
                ShowErrors(new[] { $"执行失败：{ex.Message}" });
        }
        finally
        {
            if (!_unloaded)
                SetBusy(false, null);
        }
    }

    /// <summary>「重跑 Step 2」：重跑幂等（复用已有工作区、显式新建会话，PRD §4.3 陷阱①③）。</summary>
    private async void OnRerunClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _unloaded || _service.Current is null)
            return;

        ShowErrors(Array.Empty<string>());
        SetBusy(true, "正在重跑 Step 2（复用已有工作区，新建执行会话）…");
        try
        {
            var result = await _service.InitializeAsync().ConfigureAwait(true);
            if (_unloaded)
                return;

            if (result.Status != StepStatus.Succeeded)
                ShowErrors(new[] { result.Message ?? "重跑失败，详见右侧操作记录。" });
        }
        catch (Exception ex)
        {
            if (!_unloaded)
                ShowErrors(new[] { $"重跑失败：{ex.Message}" });
        }
        finally
        {
            if (!_unloaded)
                SetBusy(false, null);
        }
    }

    /// <summary>
    /// 「中止并清理」（PRD §4.7）：注销本项目工作区（不删本地目录）、删除已写入的 <c>newproject-*</c> 技能与
    /// <c>yunxiao</c> MCP 文件、清空档案与操作记录；远端产物一律不自动删除。
    /// </summary>
    /// <remarks>
    /// 原「重置」只清档案、<b>不</b>注销工作区，留下「档案没了、工作区登记还在」的登记残留，
    /// 与 Step 1 唯一性口径①叠加会形成同目录再也无法前进的死角（P2 冒烟缺陷），故与「中止并清理」合并为同一语义。
    /// </remarks>
    private async void OnAbortCleanupClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _unloaded)
            return;

        SetBusy(true, "正在中止并清理（注销工作区 · 删除技能与 MCP）…");
        try
        {
            await _service.AbortAndCleanupAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (!_unloaded)
                ShowErrors(new[] { $"中止并清理失败：{ex.Message}" });
        }
        finally
        {
            if (!_unloaded)
            {
                _resumeResolved = true;
                ClearForm();
                SetBusy(false, null);
            }
        }
    }

    /// <summary>残留档案：保留并继续（回填表单，不覆盖档案）。</summary>
    private void OnResumeKeepClicked(object? sender, RoutedEventArgs e)
    {
        if (_unloaded)
            return;

        var spec = _service.Current;
        if (spec is not null)
        {
            _nameBox.Text = spec.Name;
            _codeBox.Text = spec.Code;
            _introBox.Text = spec.Introduction;
            _pathBox.Text = spec.LocalPath;
            _mobileCheck.IsChecked = spec.Ends.Contains(ProjectEnd.Mobile);
        }

        _resumeResolved = true;
        ShowErrors(Array.Empty<string>());
        SyncFormEnabled();
    }

    /// <summary>残留档案：清理后重来（本地完整回收 + 清空档案，不含远端产物，PRD §4.7）。</summary>
    private async void OnResumeDiscardClicked(object? sender, RoutedEventArgs e)
    {
        if (_unloaded)
            return;

        await _service.AbortAndCleanupAsync().ConfigureAwait(true);

        if (_unloaded)
            return;

        _resumeResolved = true;
        ClearForm();
        SyncFormEnabled();
    }

    /// <summary>「浏览…」：经 TopLevel 的 StorageProvider 选择目录（不引入 XAML）。</summary>
    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _unloaded)
            return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is null)
            {
                ShowErrors(new[] { "当前宿主不支持目录选择器，请手工填写绝对路径。" });
                return;
            }

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择本项目的工作区根目录",
                AllowMultiple = false
            }).ConfigureAwait(true);

            if (_unloaded)
                return;

            var folder = folders?.FirstOrDefault();
            if (folder is null)
                return;

            var localPath = folder.TryGetLocalPath();
            _pathBox.Text = string.IsNullOrWhiteSpace(localPath) ? folder.Path.ToString() : localPath;
        }
        catch (Exception ex)
        {
            if (!_unloaded)
                ShowErrors(new[] { $"打开目录选择器失败：{ex.Message}" });
        }
    }

    /// <summary>
    /// 占位：步骤视觉载体已在 <see cref="BuildLeftColumn"/> 内注册，无需额外补挂。
    /// </summary>
    private void AttachStepViewGrids()
    {
        // 步骤行的 Grid 列绑定在构建时已完成（见 BuildLeftColumn），此方法保留以明确
        // 「构建 → 绑定 → 首次刷新」的顺序，后续步骤表单接入时在此挂接控件引用。
    }
}