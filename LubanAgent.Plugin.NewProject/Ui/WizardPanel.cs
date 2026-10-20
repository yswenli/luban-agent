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
using LubanAgent.Plugin.NewProject.Services;

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
/// <b>本阶段（M4-P3）范围</b>：Step 1 真表单（名称 / 缩写 / 介绍 / 端选择 / 本地目录 + 浏览）、
/// Step 2 初始化触发、Step 3 建库确认区（逐端仓库名 / 可见性 / 归属组织 / 「我已确认」+ 克隆冲突三选一）、
/// Step 6 推送区与 Step 4-5 占位区已接入，校验与执行全部委托共享能力层（面板只做展示与投递，零业务规则）；
/// Step 4 / 5 / 7 的操作区在 P4~P5 接入。面板不直接触碰 <c>Dispatcher</c>，UI 投递一律经插件传入的
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
    /// <summary>右列时间线的滚动宿主：内容变化后用它滚到最新一条（PRD §4.6）。</summary>
    private ScrollViewer? _timelineScroll;
    /// <summary>待滚动标记：<see cref="Refresh"/> 置位，由 <see cref="OnTimelineLayoutUpdated"/> 消费。</summary>
    private bool _pendingScrollToEnd;

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

    // ── Step 3 / Step 6 操作区（P3 接入）─────────────────────────────────────────
    private Border _statusCard = null!;
    private Border _maintenanceCard = null!;
    private Border _step1Form = null!;
    private Border _step3Area = null!;
    private Border _step6Area = null!;
    private Border _otherStepArea = null!;
    private Border _conflictPanel = null!;
    private StackPanel _repoRowsPanel = null!;

    /// <summary>各端仓库名输入行（Step 3 确认区）。</summary>
    private readonly List<RepoRowView> _repoRows = new();

    private readonly RadioButton _visibilityInternal = new()
    {
        Content = "组织内公开（默认）",
        GroupName = "newproject-visibility",
        IsChecked = true
    };

    private readonly RadioButton _visibilityPrivate = new() { Content = "私有", GroupName = "newproject-visibility" };
    private readonly TextBlock _orgText = new() { FontSize = 12, Foreground = PrimaryText, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _landingText = new() { FontSize = 12, Foreground = PrimaryText, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _confirmedCheck = new() { Content = "我已确认仓库名称与可见性（本步会在云效组织内创建真实仓库）" };
    private readonly Button _createRepoButton = new() { Content = "创建并克隆" };

    /// <summary>Step 3 预检按钮（只读：查代码组 / 模板库 / 分支 / 同名仓库，不创建任何云效资源）。</summary>
    private readonly Button _precheckButton = new() { Content = "预检（只读，不创建）" };

    /// <summary>Step 3 预检读数展示（预检通过 / 未通过 / 已作废）。</summary>
    private readonly TextBlock _precheckText = new()
    {
        FontSize = 11,
        Foreground = MutedText,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };

    /// <summary>最近一次预检结果（<c>null</c>＝尚未预检）。</summary>
    private RepositoryPrecheck? _precheck;

    /// <summary>预检时的输入签名：与当前输入不一致即表示预检已作废（PRD §4.3 Step 3）。</summary>
    private string? _precheckSignature;

    /// <summary>回填档案期间抑制「输入变更即作废预检」的误判（回填不是用户输入）。</summary>
    private bool _populating;

    private readonly TextBlock _conflictText = new() { FontSize = 12, Foreground = WarningBrush, TextWrapping = TextWrapping.Wrap };
    private readonly Button _conflictReuseButton = new() { Content = "复用已克隆" };
    private readonly Button _conflictRecloneButton = new() { Content = "清空后重克隆" };
    private readonly Button _conflictAbortButton = new() { Content = "中止本步" };
    private readonly Button _pushButton = new() { Content = "提交并推送" };
    private readonly Button _skipToPushButton = new() { Content = "跳过 Step 4/5，直接执行推送（临时入口，P4 落地后自动衔接）" };
    private readonly TextBlock _otherStepText = new() { FontSize = 12, Foreground = MutedText, TextWrapping = TextWrapping.Wrap };

    /// <summary>【T0 SPIKE · 临时】宿主自跑一轮诊断入口（验证通过后连同 <c>RunHostBridgeSpikeAsync</c> 一并删除）。</summary>
    private readonly Button _spikeButton = new() { Content = "T0 诊断：让宿主 Agent 自跑一轮脚本（临时）" };

    /// <summary>Step 3 运行期待决的克隆冲突三选一（面板与能力层之间的挂起点）。</summary>
    private TaskCompletionSource<CloneConflictChoice>? _conflictChoice;

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

        // Step 3 / Step 6（P3）：建库克隆、冲突三选一、推送
        _createRepoButton.Click += OnCreateReposClicked;
        _precheckButton.Click += OnPrecheckClicked;

        // 可见性变更 → 预检作废（仓库名输入在 BuildStep3Area 里逐行挂钩）
        _visibilityInternal.IsCheckedChanged += OnPlanCheckedChanged;
        _visibilityPrivate.IsCheckedChanged += OnPlanCheckedChanged;

        _pushButton.Click += OnPushClicked;
        _skipToPushButton.Click += OnSkipToPushClicked;
        _spikeButton.Click += OnSpikeClicked;
        _conflictReuseButton.Click += OnConflictChoiceClicked;
        _conflictRecloneButton.Click += OnConflictChoiceClicked;
        _conflictAbortButton.Click += OnConflictChoiceClicked;

        // 克隆冲突决策入口：面板在场时提供内联三选一；面板未创建（聊天入口）时能力层自行中止本步
        _service.CloneConflictResolver = ShowCloneConflictAsync;

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

        // 清掉指向本面板的决策回调：能力层是长生命周期实例，不得在卸载后回调已卸载程序集（PRD §6 #20 / #22）
        _service.CloneConflictResolver = null;
        _conflictChoice?.TrySetResult(CloneConflictChoice.Abort);
        _conflictChoice = null;

        // 解绑时间线布局订阅并清掉待滚动标记（卸载后不再有 UI 可滚）
        _timeline.LayoutUpdated -= OnTimelineLayoutUpdated;
        _pendingScrollToEnd = false;

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

        _statusCard = Card(new StackPanel
        {
            Spacing = 8,
            Children = { _busyText, _validationText, _warningText, _resumeBanner }
        });

        _step1Form = BuildStep1Form();
        _maintenanceCard = BuildMaintenanceCard();
        _step3Area = BuildStep3Area();
        _step6Area = BuildStep6Area();
        _otherStepArea = BuildOtherStepArea();

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
                    _statusCard,
                    _step1Form,
                    _maintenanceCard,
                    _step3Area,
                    _step6Area,
                    _otherStepArea
                }
            }
        };
    }

    /// <summary>统一的卡片容器（背景 / 圆角 / 1px 描边）。</summary>
    /// <param name="child">内容。</param>
    /// <returns>卡片。</returns>
    private static Border Card(Control child) => new()
    {
        Margin = new Thickness(0, 8, 0, 0),
        Padding = new Thickness(14),
        CornerRadius = new CornerRadius(8),
        Background = CardBackground,
        BorderThickness = new Thickness(1),
        BorderBrush = DividerBrush,
        Child = child
    };

    /// <summary>
    /// Step 1 表单卡（状态提示与维护按钮已抽到常驻卡，切换步骤时不会一起消失）。
    /// </summary>
    /// <returns>表单卡。</returns>
    private Border BuildStep1Form() => Card(new StackPanel
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
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _nextButton, _adoptButton }
            }
        }
    });

    /// <summary>
    /// 常驻「档案与维护」卡：任何步骤下都能重跑 Step 2 或中止并清理（PRD §4.7）。
    /// </summary>
    /// <returns>维护卡。</returns>
    private Border BuildMaintenanceCard() => Card(new StackPanel
    {
        Spacing = 8,
        Children =
        {
            new TextBlock { Text = "档案与维护", FontSize = 13, Foreground = PrimaryText },
            new TextBlock
            {
                Text = "「重跑 Step 2」复用同一工作区并新建执行会话；「中止并清理」注销工作区、删除已写入的技能与 MCP、"
                     + "清空档案——本地目录与云效上的远端产物都不会被删除（PRD §4.7）。",
                FontSize = 11,
                Foreground = MutedText,
                TextWrapping = TextWrapping.Wrap
            },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _rerunButton, _resetButton }
            },
            // 【T0 SPIKE · 临时】验证通过后删除本行与 _spikeButton
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _spikeButton }
            }
        }
    });

    /// <summary>
    /// Step 3 操作区（PRD §4.3「用户确认界面」）：逐端仓库名 / 可见性 / 归属组织 / 「我已确认」+ 建库按钮，
    /// 以及运行期出现的克隆冲突三选一。
    /// </summary>
    /// <returns>操作区。</returns>
    private Border BuildStep3Area()
    {
        _repoRowsPanel = new StackPanel { Spacing = 10 };
        foreach (var end in new[] { ProjectEnd.Service, ProjectEnd.Admin, ProjectEnd.Mobile })
        {
            var nameBox = new TextBox { PlaceholderText = "仓库名（可改）" };
            nameBox.TextChanged += OnPlanTextChanged;
            var templateText = new TextBlock { FontSize = 11, Foreground = MutedText, TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel
            {
                Spacing = 4,
                Children = { LabeledField($"{EndLabel(end)}端目标仓库名", nameBox), templateText }
            };

            _repoRowsPanel.Children.Add(panel);
            _repoRows.Add(new RepoRowView(end, panel, nameBox, templateText));
        }

        _conflictPanel = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = PageBackground,
            BorderThickness = new Thickness(1),
            BorderBrush = WarningBrush,
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    _conflictText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { _conflictReuseButton, _conflictRecloneButton, _conflictAbortButton }
                    }
                }
            }
        };

        return Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Step 3 · 按模板建库并克隆", FontSize = 14, Foreground = PrimaryText },
                new TextBlock
                {
                    Text = "以各端模板仓为模板，在云效组织内创建本项目专属仓库，再按 HTTPS 地址克隆到本地并替换占位符。"
                         + "仓库名可在此修改（改名会重新克隆到新目录）；模板在云效模板库中定位，pin 分支留空时按 "
                         + "develop → master → main → 模板默认分支 实时解析。",
                    FontSize = 12,
                    Foreground = MutedText,
                    TextWrapping = TextWrapping.Wrap
                },
                new Border { Height = 1, Background = DividerBrush },
                _repoRowsPanel,
                LabeledField("可见性", new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Children = { _visibilityInternal, _visibilityPrivate }
                }),
                LabeledField("归属组织", _orgText),
                LabeledField("落地位置", _landingText),
                new Border
                {
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(6),
                    Background = PageBackground,
                    BorderThickness = new Thickness(1),
                    BorderBrush = DividerBrush,
                    Child = new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock { Text = "Step 3 预检（只读，不创建）", FontSize = 12, Foreground = PrimaryText },
                            new TextBlock
                            {
                                Text = "预检只查不建：把「将要使用什么参数」提前摊开——目标代码组（新建还是复用）、逐端模板库命中项、"
                                     + "解析出的 pin 分支与依据、目标仓库名（新建还是复用）、可见性与落地位置。"
                                     + "任何输入变更都会让预检作废，需重新预检后才能执行。",
                                FontSize = 11,
                                Foreground = MutedText,
                                TextWrapping = TextWrapping.Wrap
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 8,
                                Children = { _precheckButton }
                            },
                            _precheckText
                        }
                    }
                },
                _confirmedCheck,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { _createRepoButton }
                },
                _conflictPanel
            }
        });
    }

    /// <summary>
    /// Step 6 操作区：切到目标分支（配置 <c>pushBranch</c>，默认 main）→ 暂存 → 有改动则提交 → 推送到各端自有仓库（PRD §4.3 Step 6）。
    /// </summary>
    /// <returns>操作区。</returns>
    private Border BuildStep6Area() => Card(new StackPanel
    {
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = "Step 6 · 推送改动到自有仓库", FontSize = 14, Foreground = PrimaryText },
            new TextBlock
            {
                Text = "逐端执行：切到目标分支（配置 pushBranch，默认 main；不存在则从模板 pin 分支创建）"
                     + "→ git add -A → 有改动则提交 → 推送。"
                     + "不强制推送；提交身份优先取本机 git 配置，缺失时用插件配置兜底。",
                FontSize = 12,
                Foreground = MutedText,
                TextWrapping = TextWrapping.Wrap
            },
            new Border { Height = 1, Background = DividerBrush },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _pushButton }
            }
        }
    });

    /// <summary>
    /// Step 4 / 5 / 7 的占位区（P4 / P5 落地前给出明确说明，并提供临时推送入口）。
    /// </summary>
    /// <returns>占位区。</returns>
    private Border BuildOtherStepArea() => Card(new StackPanel
    {
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = "后续步骤", FontSize = 14, Foreground = PrimaryText },
            _otherStepText,
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _skipToPushButton }
            }
        }
    });

    /// <summary>Step 3 各端仓库名行的视觉载体。</summary>
    private sealed record RepoRowView(ProjectEnd End, Control Root, TextBox NameBox, TextBlock TemplateText);

    /// <summary>端的中文标签（与能力层文案一致）。</summary>
    /// <param name="end">端。</param>
    /// <returns>标签。</returns>
    private static string EndLabel(ProjectEnd end) => end switch
    {
        ProjectEnd.Service => "后端",
        ProjectEnd.Admin => "管理端",
        ProjectEnd.Mobile => "移动端",
        _ => end.ToString()
    };

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

        _timelineScroll = new ScrollViewer { Padding = new Thickness(20), Content = column };
        // 时间线高度变化（新增 / 重建记录）后滚到最新：布局完成时内容高度才稳定（见 OnTimelineLayoutUpdated）
        _timeline.LayoutUpdated += OnTimelineLayoutUpdated;
        return _timelineScroll;
    }

    /// <summary>
    /// 时间线内容变化后把日志滚到最新一条。
    /// </summary>
    /// <remarks>
    /// 面板不得直接触碰 <c>Dispatcher</c>（PRD §6 #22），故不按优先级另行投递，而是借
    /// <c>LayoutUpdated</c> 在布局完成后再滚动——此时内容高度（<c>Extent</c>）已按新记录更新，
    /// <c>ScrollToEnd</c> 才能准确定位到底部（<see cref="Refresh"/> 内同步调用会读到旧高度而滚不到底）。
    /// </remarks>
    private void OnTimelineLayoutUpdated(object? sender, EventArgs e)
    {
        if (_unloaded || !_pendingScrollToEnd || _timelineScroll is null)
            return;

        _pendingScrollToEnd = false;
        _timelineScroll.ScrollToEnd();
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
        SyncStepAreas(spec, currentStep);

        // 步骤状态一律取自本步操作记录的真实终态，不再用「序号小于当前步」推断：
        // 序号推断会把「从未执行过的后续步」显示成已完成，也会把「停在该步但当前没在跑」
        // 显示成进行中（P3 冒烟缺陷：Step 4/5 未实现却显示「进行中」）。
        var operations = _service.Operations;
        var runningSteps = new HashSet<WizardStep>();
        var succeededSteps = new HashSet<WizardStep>();
        var failedSteps = new HashSet<WizardStep>();
        foreach (var record in operations)
        {
            switch (record.Status)
            {
                case StepStatus.Running:
                    runningSteps.Add(record.Step);
                    break;
                case StepStatus.Succeeded:
                    succeededSteps.Add(record.Step);
                    break;
                case StepStatus.Failed:
                    failedSteps.Add(record.Step);
                    break;
            }
        }

        foreach (var view in _stepViews)
        {
            var isCurrent = spec is not null && view.Step == currentStep;
            // Step 1 是纯表单采集，能力层不为其记成功记录：只要档案已建立即视为已完成
            var isSucceeded = succeededSteps.Contains(view.Step)
                              || (view.Step == WizardStep.BasicInfo && spec is not null);
            var isRunning = runningSteps.Contains(view.Step);
            var isFailed = !isSucceeded && failedSteps.Contains(view.Step);

            view.Badge.Background = isSucceeded ? SuccessBrush : isRunning ? AccentBrush : Brushes.Transparent;
            view.Badge.BorderBrush = isSucceeded ? SuccessBrush
                : isRunning || isCurrent ? AccentBrush
                : isFailed ? DangerBrush
                : DividerBrush;
            view.BadgeText.Foreground = isSucceeded || isRunning ? PageBackground : MutedText;
            view.Status.Foreground = isSucceeded ? SuccessBrush
                : isRunning || isCurrent ? AccentBrush
                : isFailed ? DangerBrush
                : MutedText;
            view.Status.Text = isSucceeded ? "已完成"
                : isRunning ? "执行中"
                : isFailed ? "失败"
                : isCurrent ? "待执行"
                : "未开始";
        }

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

        // 有新记录：请布局完成后滚到最新（同步调用会读到重建前的旧高度，滚不到底）
        _pendingScrollToEnd = true;

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
    /// 按当前步骤切换左列的操作区：Step 1 表单 / Step 3 建库确认区 / Step 4-5-7 占位区 / Step 6 推送区。
    /// </summary>
    /// <remarks>
    /// 「状态提示（校验 / 忙碌 / 残留横幅）」与「档案与维护」抽为常驻卡，故切换步骤不会连失败信息与
    /// 中止入口一起消失（P2 的单表单结构在 P3 起不再适用）。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    /// <param name="currentStep">当前步骤。</param>
    private void SyncStepAreas(ProjectSpec? spec, WizardStep currentStep)
    {
        var hasArchive = spec is not null;

        _step1Form.IsVisible = !hasArchive || currentStep is WizardStep.BasicInfo or WizardStep.Scaffold;
        _maintenanceCard.IsVisible = hasArchive && _resumeResolved;

        _step3Area.IsVisible = hasArchive && currentStep == WizardStep.Repositories;
        _step6Area.IsVisible = hasArchive && currentStep == WizardStep.Push;
        _otherStepArea.IsVisible = hasArchive && currentStep is WizardStep.Database or WizardStep.Build or WizardStep.Pipeline;

        if (spec is not null && _step3Area.IsVisible)
            PopulateStep3Area(spec);

        if (spec is not null && _otherStepArea.IsVisible)
        {
            _otherStepText.Text = currentStep switch
            {
                WizardStep.Database => "Step 4 · 配置与数据库：功能尚未实现（计划 M4-P4，将由项目助手驱动生成配置与建库脚本）。"
                                       + "本步当前无法执行，只能点下方临时入口直接跳到推送。",
                WizardStep.Build => "Step 5 · 编译检查：功能尚未实现（计划 M4-P4，将由项目助手逐端编译并汇总结果）。"
                                    + "本步当前无法执行，只能点下方临时入口直接跳到推送。",
                _ => "Step 7 · 创建流水线：功能尚未实现（计划 M4-P5）。本步当前无法执行。"
            };

            // 临时入口：P4 未落地前允许直接执行 Step 6（推送），避免向导在 Step 4 处彻底卡死
            _skipToPushButton.IsVisible = currentStep is WizardStep.Database or WizardStep.Build;
        }
    }

    /// <summary>
    /// 把档案里的建库计划回填到 Step 3 确认区（逐端：仓库名输入 + 模板仓 / pin 信息）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    private void PopulateStep3Area(ProjectSpec spec)
    {
        _populating = true;
        try
        {
            foreach (var row in _repoRows)
            {
                var selected = spec.Ends.Count == 0 || spec.Ends.Contains(row.End);
                row.Root.IsVisible = selected;
                if (!selected)
                    continue;

                var repository = spec.Repositories.FirstOrDefault(item => item.End == row.End);
                var name = repository?.RepositoryName ?? string.Empty;
                if (!row.NameBox.IsFocused && !string.Equals(row.NameBox.Text, name, StringComparison.Ordinal))
                    row.NameBox.Text = name;

                if (repository is null || string.IsNullOrWhiteSpace(repository.TemplateRepository))
                {
                    row.TemplateText.Foreground = DangerBrush;
                    row.TemplateText.Text = "模板仓：未配置（插件配置 templates[] 缺少该端对照）→ 本步会被阻断，请在插件配置中补齐。";
                    continue;
                }

                row.TemplateText.Foreground = MutedText;
                var pin = string.IsNullOrWhiteSpace(repository.TemplateBranch)
                    ? "pin 待运行期解析（develop → master → main → 模板默认分支；以预检读数为准）"
                    : $"pin {repository.TemplateBranch}（上次解析结果；本次执行会按配置重新解析）";
                var cloned = repository.Cloned ? " · 已克隆（重跑本步将跳过克隆）" : string.Empty;
                row.TemplateText.Text = $"模板仓：{repository.TemplateRepository} · {pin}{cloned}";
            }

            // 可见性：档案里已有取值则回填，否则保持「组织内公开」默认
            var visibility = spec.Repositories
                .FirstOrDefault(item => spec.Ends.Count == 0 || spec.Ends.Contains(item.End))
                ?.Visibility;
            var isPrivate = string.Equals(visibility, "private", StringComparison.OrdinalIgnoreCase);
            _visibilityInternal.IsChecked = !isPrivate;
            _visibilityPrivate.IsChecked = isPrivate;

            var organization = _service.OrganizationHint;
            _orgText.Text = string.IsNullOrWhiteSpace(organization)
                ? "未配置（请到 codex「设置 → 云效连接」填写组织 ID 后重跑本步）"
                : organization!;
            _orgText.Foreground = string.IsNullOrWhiteSpace(organization) ? DangerBrush : PrimaryText;

            _landingText.Text = string.IsNullOrWhiteSpace(spec.LocalPath)
                ? "未设置（Step 1 的本地目录为空）"
                : string.Join("；", _repoRows.Where(view => view.Root.IsVisible).Select(view =>
                {
                    var repository = spec.Repositories.FirstOrDefault(item => item.End == view.End);
                    return string.IsNullOrWhiteSpace(repository?.LocalPath)
                        ? $"{EndLabel(view.End)}：—"
                        : $"{EndLabel(view.End)}：{repository!.LocalPath}";
                }));
        }
        finally
        {
            _populating = false;
        }

        // 回填完成后判定一次：预检是否已因输入变化而作废
        SyncPrecheckState();
    }

    /// <summary>Step 3 确认区仓库名输入变更 → 预检作废。</summary>
    private void OnPlanTextChanged(object? sender, TextChangedEventArgs e) => MarkPrecheckStale();

    /// <summary>Step 3 可见性变更 → 预检作废。</summary>
    private void OnPlanCheckedChanged(object? sender, RoutedEventArgs e) => MarkPrecheckStale();

    /// <summary>
    /// 「创建并克隆」的可用性＝「预检已通过」且「预检未被输入变更作废」（PRD §4.3 Step 3）。
    /// </summary>
    private void SyncCreateEnabled()
    {
        var ready = !_busy
            && _precheck is { IsReady: true }
            && string.Equals(_precheckSignature, CurrentPlanSignature(), StringComparison.Ordinal);
        _createRepoButton.IsEnabled = ready;
    }

    /// <summary>
    /// 输入变更即作废预检（PRD §4.3 Step 3）：预检读的是一组具体参数，参数变了读数就不再可信。
    /// </summary>
    private void MarkPrecheckStale()
    {
        if (_populating || _precheck is null)
            return;

        _precheckSignature = null;
        _precheckText.IsVisible = true;
        _precheckText.Foreground = WarningBrush;
        _precheckText.Text = "输入已变更：预检结果已作废，请点「预检」重新核对后再执行。";
        SyncCreateEnabled();
    }

    /// <summary>刷新预检读数的展示（通过 / 未通过 / 已作废）。</summary>
    private void SyncPrecheckState()
    {
        if (_precheck is null)
        {
            SyncCreateEnabled();
            return;
        }

        _precheckText.IsVisible = true;
        if (!string.Equals(_precheckSignature, CurrentPlanSignature(), StringComparison.Ordinal))
        {
            _precheckText.Foreground = WarningBrush;
            _precheckText.Text = "输入已变更：预检结果已作废，请点「预检」重新核对后再执行。";
        }
        else
        {
            _precheckText.Foreground = _precheck.IsReady ? SuccessBrush : DangerBrush;
            _precheckText.Text = FormatPrecheck(_precheck);
        }

        SyncCreateEnabled();
    }

    /// <summary>
    /// 预检输入签名：把「当前确认区实际会提交的参数」压成可比对字符串（端 + 仓库名 + 可见性）。
    /// </summary>
    /// <returns>签名；无档案时为空串。</returns>
    private string CurrentPlanSignature()
    {
        var spec = _service.Current;
        if (spec is null)
            return string.Empty;

        var visibility = _visibilityPrivate.IsChecked == true ? "private" : "internal";
        var entries = _repoRows
            .Where(row => row.Root.IsVisible)
            .OrderBy(row => row.End)
            .Select(row => $"{row.End}:{(row.NameBox.Text ?? string.Empty).Trim()}:{visibility}");

        return $"{spec.Code}|{spec.Name}|{string.Join("|", entries)}";
    }

    /// <summary>预检读数的多行展示文案。</summary>
    /// <param name="precheck">预检结果。</param>
    /// <returns>文案。</returns>
    private static string FormatPrecheck(RepositoryPrecheck precheck)
    {
        var lines = new List<string>
        {
            $"目标代码组：{precheck.GroupName}"
                + (precheck.GroupExists
                    ? $"（已存在，将复用 · id {precheck.GroupId}）"
                    : $"（不存在，将新建 · 父级 {(precheck.GroupParentId.HasValue ? precheck.GroupParentId.Value.ToString() : "组织根")}）")
        };

        foreach (var end in precheck.Ends)
        {
            var label = EndLabel(end.End);
            lines.Add(end.IsReady
                ? $"· {label}：模板 {end.TemplateRepository}（id {end.TemplateProjectId} · {end.TemplatePath}）"
                  + $" · pin {end.ResolvedBranch}（{end.ResolvedBranchSource}）"
                  + $" → 仓库 {end.RepositoryName}"
                  + $"（{(end.RepositoryExists ? "已存在，将复用" : "将新建")} · {VisibilityText(end.Visibility)}）"
                : $"· {label}：✕ {end.Error}");
        }

        foreach (var warning in precheck.Warnings)
            lines.Add($"⚠ {warning}");

        lines.Add(precheck.IsReady
            ? $"预检通过（{precheck.TakenAt:HH:mm:ss}）：可以执行「创建并克隆」。"
            : "预检未通过：请按上述原因修正后重新预检（预检不会创建任何云效资源）。");

        return string.Join("\n", lines);
    }

    /// <summary>可见性的中文标签（与能力层文案一致）。</summary>
    /// <param name="visibility">可见性。</param>
    /// <returns>标签。</returns>
    private static string VisibilityText(string? visibility)
        => string.Equals(visibility, "private", StringComparison.OrdinalIgnoreCase) ? "私有" : "组织内公开";

    /// <summary>
    /// 把确认区当前输入整理成建库计划（Step 3 校验 / 预检 / 执行共用同一口径）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <returns>按端建库计划。</returns>
    private List<EndRepository> CollectPlan(ProjectSpec spec)
    {
        var plan = new List<EndRepository>();
        foreach (var row in _repoRows.Where(view => view.Root.IsVisible))
        {
            var current = spec.Repositories.FirstOrDefault(repository => repository.End == row.End);
            plan.Add(new EndRepository
            {
                End = row.End,
                TemplateRepository = current?.TemplateRepository ?? string.Empty,
                TemplateBranch = current?.TemplateBranch ?? string.Empty,
                RepositoryName = row.NameBox.Text ?? string.Empty,
                Visibility = _visibilityPrivate.IsChecked == true ? "private" : "internal",
                DefaultBranch = string.IsNullOrWhiteSpace(current?.DefaultBranch) ? "main" : current!.DefaultBranch,
                LocalPath = current?.LocalPath ?? string.Empty
            });
        }

        return plan;
    }

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
        _spikeButton.IsEnabled = !_busy;

        // Step 3 / Step 6 / 占位区（P3）：忙碌时只锁动作按钮；冲突三选一必须保持可点
        // —— 它回答的正是「Step 3 正在运行」时的问题，锁掉会直接把能力层挂死到超时。
        _precheckButton.IsEnabled = !_busy;
        SyncCreateEnabled();
        _pushButton.IsEnabled = !_busy;
        _skipToPushButton.IsEnabled = !_busy;
        _confirmedCheck.IsEnabled = !_busy;
        _visibilityInternal.IsEnabled = !_busy;
        _visibilityPrivate.IsEnabled = !_busy;
        foreach (var row in _repoRows)
            row.NameBox.IsEnabled = !_busy;
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
    /// Step 3「预检」（只读）：先按当前输入校验并回写档案，再让能力层查代码组 / 模板库 / 分支 / 同名仓库。
    /// </summary>
    private async void OnPrecheckClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _unloaded)
            return;

        var spec = _service.Current;
        if (spec is null)
        {
            ShowErrors(new[] { "尚未建立项目档案，请先完成 Step 1。" });
            return;
        }

        ShowErrors(Array.Empty<string>());

        var plan = CollectPlan(spec);
        SetBusy(true, "正在按当前输入校验建库计划…");
        try
        {
            var validation = await _service.ValidateRepositoryPlanAsync(plan).ConfigureAwait(true);
            if (_unloaded)
                return;

            if (!validation.IsValid)
            {
                _precheck = null;
                _precheckSignature = null;
                _precheckText.IsVisible = true;
                _precheckText.Foreground = DangerBrush;
                _precheckText.Text = "预检前置校验未通过：" + string.Join("；", validation.Errors);
                ShowErrors(validation.Errors);
                SyncCreateEnabled();
                return;
            }

            _service.ApplyRepositoryPlan(plan);

            SetBusy(true, "正在预检 Step 3（只读：查代码组 / 模板库 / 分支 / 同名仓库）…");
            var precheck = await _service.PrecheckRepositoriesAsync().ConfigureAwait(true);
            if (_unloaded)
                return;

            _precheck = precheck;
            _precheckSignature = CurrentPlanSignature();
            SyncPrecheckState();

            if (!precheck.IsReady)
            {
                ShowErrors(precheck.Errors
                    .Concat(precheck.Ends
                        .Where(end => !end.IsReady)
                        .Select(end => $"{EndLabel(end.End)}端 {end.Error}")));
            }
        }
        catch (Exception ex)
        {
            if (!_unloaded)
            {
                _precheck = null;
                _precheckSignature = null;
                ShowErrors(new[] { $"Step 3 预检失败：{ex.Message}" });
                SyncCreateEnabled();
            }
        }
        finally
        {
            if (!_unloaded)
                SetBusy(false, null);
        }
    }

    /// <summary>
    /// Step 3「创建并克隆」（PRD §4.3 Step 3）：确认勾选 → 校验建库计划 → 回写档案 → 执行。
    /// </summary>
    /// <remarks>
    /// <b>必须先通过预检</b>：未预检、或预检后被输入变更作废，都会在动作前阻断——
    /// 本步会在云效组织内创建真实仓库，确认区读数与实际执行参数必须一致（PRD §4.3）。
    /// </remarks>
    private async void OnCreateReposClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _unloaded)
            return;

        var spec = _service.Current;
        if (spec is null)
        {
            ShowErrors(new[] { "尚未建立项目档案，请先完成 Step 1。" });
            return;
        }

        ShowErrors(Array.Empty<string>());

        // 预检门禁：未预检 / 已作废 / 预检未通过，一律不允许触碰云效写接口
        if (_precheck is null || !string.Equals(_precheckSignature, CurrentPlanSignature(), StringComparison.Ordinal))
        {
            ShowErrors(new[] { "请先点「预检」并确认读数无误：预检未通过或已因输入变更作废时，本步不会执行。" });
            SyncPrecheckState();
            return;
        }

        if (!_precheck.IsReady)
        {
            ShowErrors(new[] { "预检未通过，请按预检读数修正后重新预检。" });
            return;
        }

        // 显式确认：本步会在云效组织内创建真实仓库（PRD §4.3「用户确认界面」）
        if (_confirmedCheck.IsChecked != true)
        {
            ShowErrors(new[] { "请先勾选「我已确认仓库名称与可见性」：本步会在云效组织内创建真实仓库。" });
            return;
        }

        var plan = CollectPlan(spec);

        SetBusy(true, "正在校验建库计划…");
        try
        {
            var validation = await _service.ValidateRepositoryPlanAsync(plan).ConfigureAwait(true);
            if (_unloaded)
                return;

            if (!validation.IsValid)
            {
                ShowErrors(validation.Errors);
                return;
            }

            _service.ApplyRepositoryPlan(plan);

            SetBusy(true, "正在按模板建库并克隆（逐端：建组 → 模板库定位 / 解析 pin → 建库 / 复用 → 克隆 → 替换占位符）…");
            var result = await _service.CreateRepositoriesAsync().ConfigureAwait(true);
            if (_unloaded)
                return;

            if (result.Status != StepStatus.Succeeded)
                ShowErrors(new[] { result.Message ?? "Step 3 失败，详见右侧操作记录。" });

            // 本次执行已消耗预检读数（且档案被回写）：作废预检，避免误以为还能直接再执行
            _precheck = null;
            _precheckSignature = null;
            _precheckText.IsVisible = true;
            _precheckText.Foreground = result.Status == StepStatus.Succeeded ? SuccessBrush : DangerBrush;
            _precheckText.Text = result.Status == StepStatus.Succeeded
                ? "本步已执行完毕（预检读数已消耗）。如需重跑，请先重新预检。"
                : "本步执行失败（预检读数已消耗）。修正后请重新预检再执行。";
            SyncCreateEnabled();
        }
        catch (Exception ex)
        {
            if (!_unloaded)
                ShowErrors(new[] { $"Step 3 执行失败：{ex.Message}" });
        }
        finally
        {
            if (!_unloaded)
                SetBusy(false, null);
        }
    }

    /// <summary>Step 6「提交并推送」（PRD §4.3 Step 6）。</summary>
    private async void OnPushClicked(object? sender, RoutedEventArgs e) => await RunPushAsync();

    /// <summary>Step 4-5 占位区的临时入口：跳过未实现步骤直接执行 Step 6。</summary>
    private async void OnSkipToPushClicked(object? sender, RoutedEventArgs e) => await RunPushAsync();

    /// <summary>【T0 SPIKE · 临时】让宿主 Agent 自跑一轮脚本、插件回读结果文件（验证通过后删除）。</summary>
    private async void OnSpikeClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _unloaded || _service.Current is null)
            return;

        if (_service is not NewProjectService service)
        {
            ShowErrors(new[] { "T0 诊断需要共享能力层具体实现，当前宿主返回的实例类型不匹配。" });
            return;
        }

        ShowErrors(Array.Empty<string>());
        SetBusy(true, "T0 诊断：正在让宿主 Agent 自跑一轮脚本并回读结果文件（会向当前会话写入一轮对话）…");
        try
        {
            var result = await service.RunHostBridgeSpikeAsync().ConfigureAwait(true);
            if (_unloaded)
                return;

            if (result.Status != StepStatus.Succeeded)
                ShowErrors(new[] { result.Message ?? "T0 诊断未全部通过，详见右侧操作记录。" });
        }
        catch (Exception ex)
        {
            if (!_unloaded)
                ShowErrors(new[] { $"T0 诊断异常：{ex.Message}" });
        }
        finally
        {
            if (!_unloaded)
                SetBusy(false, null);
        }
    }

    /// <summary>Step 6 执行入口（正常流程与临时入口共用）。</summary>
    private async Task RunPushAsync()
    {
        if (_busy || _unloaded || _service.Current is null)
            return;

        ShowErrors(Array.Empty<string>());
        SetBusy(true, "正在推送改动（逐端：整理分支 → 暂存 → 提交 → 推送）…");
        try
        {
            var result = await _service.PushAsync().ConfigureAwait(true);
            if (_unloaded)
                return;

            if (result.Status != StepStatus.Succeeded)
                ShowErrors(new[] { result.Message ?? "Step 6 失败，详见右侧操作记录。" });
        }
        catch (Exception ex)
        {
            if (!_unloaded)
                ShowErrors(new[] { $"Step 6 执行失败：{ex.Message}" });
        }
        finally
        {
            if (!_unloaded)
                SetBusy(false, null);
        }
    }

    /// <summary>
    /// 克隆冲突三选一（PRD §4.3 Step 3）：能力层在运行中挂起等待本面板回答；
    /// 面板未创建（聊天入口）时能力层中止本步并给出引导文案（<see cref="INewProjectService.CloneConflictResolver"/> 为 <c>null</c>）。
    /// </summary>
    /// <param name="conflict">冲突上下文。</param>
    /// <returns>用户选择。</returns>
    private Task<CloneConflictChoice> ShowCloneConflictAsync(CloneConflict conflict)
    {
        var completion = new TaskCompletionSource<CloneConflictChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _postToUi(() =>
        {
            if (_unloaded)
            {
                completion.TrySetResult(CloneConflictChoice.Abort);
                return;
            }

            _conflictChoice = completion;
            _conflictText.Text = $"{EndLabel(conflict.End)}端目标目录已存在且非空：{conflict.LocalPath}（{conflict.EntryCount} 项）。"
                                 + $"远端仓库 {conflict.RepositoryName} 已就绪，请选择本地目录的处置方式：";
            _conflictPanel.IsVisible = true;
        });

        return completion.Task;
    }

    /// <summary>冲突三选一按钮（复用已克隆 / 清空后重克隆 / 中止本步）。</summary>
    private void OnConflictChoiceClicked(object? sender, RoutedEventArgs e)
    {
        var choice = ReferenceEquals(sender, _conflictRecloneButton)
            ? CloneConflictChoice.Reclone
            : ReferenceEquals(sender, _conflictAbortButton)
                ? CloneConflictChoice.Abort
                : CloneConflictChoice.Reuse;

        _conflictPanel.IsVisible = false;
        var completion = _conflictChoice;
        _conflictChoice = null;
        completion?.TrySetResult(choice);
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