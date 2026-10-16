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
using Avalonia.Layout;
using Avalonia.Media;
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
/// <b>本阶段（M4-P1）范围</b>：只搭骨架与数据绑定（步骤状态与时间线均读共享能力层），
/// Step 1 表单控件与各步按钮在 P2 起逐步接入。面板不直接触碰 <c>Dispatcher</c>，
/// UI 投递一律经插件传入的 <c>PostToUi</c> 令牌（PRD §6 #22）。
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

    /// <summary>七步定义（PRD §4.3）。</summary>
    private static readonly (WizardStep Step, string Title, string Summary)[] Steps =
    {
        (WizardStep.BasicInfo, "1 基本信息", "名称 / 缩写 / 介绍 / 端 / 本地目录"),
        (WizardStep.Scaffold, "2 初始化", "工作区 · 会话 · 技能 · 云效 · 项目助手"),
        (WizardStep.Repositories, "3 建库", "以模板建各端仓库并克隆到本地"),
        (WizardStep.Database, "4 数据库", "生成配置 · 执行建库脚本"),
        (WizardStep.Build, "5 编译", "逐端编译并汇总结果"),
        (WizardStep.Push, "6 推送", "提交并推送到各端专属仓库"),
        (WizardStep.Pipeline, "7 流水线", "逐端克隆参考流水线并创建")
    };

    private readonly INewProjectService _service;
    private readonly Action<Action> _postToUi;

    private readonly List<StepView> _stepViews = new();
    private readonly TextBlock _archiveText;
    private readonly TextBlock _stepHint;
    private readonly StackPanel _timeline;

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

        _timeline = new StackPanel { Spacing = 8 };

        Content = new Border
        {
            Background = PageBackground,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("2*,1,3*"),
                Children =
                {
                    BuildLeftColumn(),
                    BuildDivider(),
                    BuildRightColumn()
                }
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

        var formPlaceholder = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Background = CardBackground,
            BorderThickness = new Thickness(1),
            BorderBrush = DividerBrush,
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Step 1 · 项目基本信息", FontSize = 14, Foreground = PrimaryText },
                    new TextBlock
                    {
                        Text = "表单控件（名称 / 缩写 / 介绍 / 端选择 / 本地目录）在 M4-P2 接入；"
                             + "当前骨架阶段仅呈现步骤状态与操作记录两条主链路。",
                        FontSize = 12,
                        Foreground = MutedText,
                        TextWrapping = TextWrapping.Wrap
                    }
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
                        Text = "以 TJCES 三端模板仓创建新项目：后端与管理端必选，移动端可选（PRD §4.3）。",
                        FontSize = 12,
                        Foreground = MutedText,
                        TextWrapping = TextWrapping.Wrap
                    },
                    _archiveText,
                    _stepHint,
                    new Border { Height = 1, Background = DividerBrush, Margin = new Thickness(0, 4) },
                    steps,
                    formPlaceholder
                }
            }
        };
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
                            Text = $"状态 {record.Status} · 耗时 {record.Duration.TotalSeconds:F1}s",
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

    /// <summary>
    /// 占位：步骤视觉载体已在 <see cref="BuildLeftColumn"/> 内注册，无需额外补挂。
    /// </summary>
    private void AttachStepViewGrids()
    {
        // 步骤行的 Grid 列绑定在构建时已完成（见 BuildLeftColumn），此方法保留以明确
        // 「构建 → 绑定 → 首次刷新」的顺序，后续步骤表单接入时在此挂接控件引用。
    }
}