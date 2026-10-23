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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System.Globalization;
using LubanAgentCore.Infrastructure;
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
/// <b>本阶段（M4-P4）范围</b>：Step 1 真表单（名称 / 缩写 / 介绍 / 端选择 / 本地目录 + 浏览）、
/// Step 2 初始化触发、Step 3 建库确认区（逐端仓库名 / 可见性 / 归属组织 / 「我已确认」+ 克隆冲突三选一）、
/// Step 4 数据库探活表单（服务器 / 端口 / 库名 / 账号 / 口令 + 「开始探活」）、Step 5 逐端编译区与
/// Step 6 推送区已接入，校验与执行全部委托共享能力层（面板只做展示与投递，零业务规则）；
/// Step 4 / 5 的实际动作由项目助手执行（路线②），面板只负责采集参数与呈现结论。Step 7 的操作区在 P5 接入。
/// 面板不直接触碰 <c>Dispatcher</c>，UI 投递一律经插件传入的
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
    private static readonly IBrush HoverBrush = new SolidColorBrush(Color.Parse("#313244"));

    /// <summary>七步定义（PRD §4.3）。标题不含序号：序号由左侧徽标承载，避免「③ 3 建库」重复。</summary>
    private static readonly (WizardStep Step, string Title, string Summary)[] Steps =
    {
        (WizardStep.BasicInfo, "基本信息", "名称 / 缩写 / 介绍 / 端 / 本地目录"),
        (WizardStep.Scaffold, "初始化", "工作区 · 会话 · 技能 · 云效 · 项目助手"),
        (WizardStep.Repositories, "建库", "以模板建各端仓库并克隆到本地"),
        (WizardStep.Database, "数据库", "只读探活：能连上且库存在"),
        (WizardStep.Build, "编译", "逐端编译并汇总结果"),
        (WizardStep.Push, "推送", "提交并推送到各端专属仓库"),
        (WizardStep.Pipeline, "流水线", "逐端按模板渲染并创建（含变量与缓存）")
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

    // ── 步骤导航（P4 收尾：步骤条可点击回退 + 「上一步」）───────────────────────
    /// <summary>
    /// 视图游标：用户正在查看 / 操作的步骤（<c>null</c>＝跟随档案的「已到达最大步」）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 回退只动这个游标、不动档案的推进指针（<see cref="INewProjectService.CurrentStep"/> 单调只前进）：
    /// 「回到 Step 5 重跑编译」不会把已到达 Step 7 的档案拉回去，重启后视图自然回到已到达步。
    /// </para>
    /// <para>
    /// 游标超过推进指针（档案被「中止并清理」重置）时在 <see cref="Refresh"/> 内丢弃；本次刷新若观察到推进指针
    /// 前移越过游标（如回退到 Step 4 重跑探活），同样在 <see cref="Refresh"/> 内丢弃，让视图跟上新步骤。
    /// </para>
    /// </remarks>
    private WizardStep? _viewStep;

    /// <summary>
    /// 上一次刷新看到的推进指针：用于识别「本次刷新是否发生了前进」，从而决定回退游标是否该让位。
    /// </summary>
    /// <remarks>
    /// 只在指针真的前移且越过游标时才丢弃游标。「回退到早已到达的步骤重跑」（如已到 Step 7 后回 Step 5
    /// 重编译）不会让指针前移，视图就停在该步，用户能就地看到逐端编译结论——这正是回退导航想要的。
    /// </remarks>
    private WizardStep _lastKnownReached = WizardStep.BasicInfo;

    /// <summary>「上一步」按钮（PRD §4.6 草图项）：视图游标 −1。</summary>
    private readonly Button _backButton = new() { Content = "← 上一步" };

    /// <summary>回退导航说明（步骤条行同样可点击）。</summary>
    private readonly TextBlock _navHint = new()
    {
        Text = "步骤条可点击：回到「已到达」的步骤查看或重跑；只切视图，不改档案进度，也不会自动重跑动作。",
        FontSize = 11,
        Foreground = MutedText,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>
    /// Step 1 重提确认横幅：已建档案后从步骤条回到 Step 1、改填<b>另一个本地目录</b>再点「下一步」＝
    /// 清空档案并按新目录从 Step 2 重来，属破坏性动作，必须先显式确认（PRD §4.7「禁止静默覆盖档案」）。
    /// </summary>
    /// <remarks>
    /// 本地目录与档案相同的情形<b>不弹</b>此横幅：那不是「另建项目」而是「沿用同一工作区」，正确入口是
    /// 维护卡的「重跑 Step 2」——因为该目录已被宿主登记为工作区，重提必被唯一性口径①拦下，确认了也走不到
    /// Step 2（且「复用该工作区继续」按钮在有档案时按设计不出现）。
    /// </remarks>
    private readonly Border _restartConfirmBanner;

    private readonly TextBlock _restartConfirmText = new()
    {
        FontSize = 12,
        Foreground = WarningBrush,
        TextWrapping = TextWrapping.Wrap
    };

    private readonly Button _restartConfirmButton = new() { Content = "确认清空档案并重走 Step 2" };
    private readonly Button _restartCancelButton = new() { Content = "取消" };

    /// <summary>本次「下一步」是否已拿到「清空档案重来」的确认。</summary>
    private bool _restartConfirmed;

    // ── Step 1 表单 ──────────────────────────────────────────────────────────────
    private readonly TextBox _nameBox = new() { PlaceholderText = "例如：津南民生实事" };
    private readonly TextBox _codeBox = new() { PlaceholderText = "例如：jnms（只能是小写字母 a-z，不含数字）" };
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
    private Border _step7Area = null!;
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

    /// <summary>
    /// 常驻「档案与维护」卡里的正式入口：重跑 Step 6（提交并推送）。
    /// </summary>
    /// <remarks>
    /// 向导没有「返回上一步」的导航，Step 6 执行过后若要重推，得有个随时可达的入口。
    /// 原实现把它做成 Step 7 占位区的临时出口（文案「跳过 Step 7」），语义不实——P5 未实现前它并不是
    /// 「跳过」，而是「重跑」。故收口为常驻卡里的正式入口。
    /// Step 6 本身幂等（无改动则跳过提交、推送照常收尾，PRD §4.8 第 18 条⑥），重跑安全。
    /// </remarks>
    /// <remarks>
    /// 可见性＝<b>推进指针 ≥ Step 6</b>（v0.23 起；原口径「至少一端已克隆」在 Step 3 一完成就给出一条
    /// 编译前的越级通道）。可见只是「能点」，能不能真推由 <c>StepGate.CanPush</c> 在执行时复核——
    /// 编译未全部通过照样被拒。
    /// </remarks>
    private readonly Button _repushButton = new() { Content = "重新推送（重跑 Step 6）" };

    // ── Step 4 操作区（P4 接入）─────────────────────────────────────────────────
    private Border _step4Area = null!;
    private readonly TextBox _dbHostBox = new() { PlaceholderText = "例如：10.10.0.214" };
    private readonly TextBox _dbPortBox = new() { PlaceholderText = "3306" };
    private readonly TextBox _dbNameBox = new() { PlaceholderText = "例如：tjc_dev_jnms（需你先手工建库）" };
    private readonly TextBox _dbUserBox = new() { PlaceholderText = "例如：testuser" };
    private readonly TextBox _dbPasswordBox = new() { PasswordChar = '●', PlaceholderText = "数据库口令（只在内存，不写档案）" };
    private readonly Button _probeButton = new() { Content = "开始探活（只读，不建库）" };
    private readonly TextBlock _probeResultText = new()
    {
        FontSize = 12,
        Foreground = MutedText,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };

    /// <summary>Step 4 表单已回填过的档案缩写（同一档案只回填一次，不覆盖用户输入）。</summary>
    private string? _step4LoadedForCode;

    // ── Step 5 操作区（P4 接入）─────────────────────────────────────────────────
    private Border _step5Area = null!;
    private readonly Button _buildButton = new() { Content = "开始编译（逐端）" };
    private readonly StackPanel _buildRowsPanel = new() { Spacing = 6 };
    private readonly TextBlock _buildResultText = new()
    {
        FontSize = 12,
        Foreground = MutedText,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };

    /// <summary>Step 5 逐端状态行（端 → 最近一次编译结论）。</summary>
    private readonly List<EndBuildRowView> _buildRows = new();

    // ── Step 7 操作区（v0.30 接入）──────────────────────────────────────────────
    private readonly Button _pipelineButton = new() { Content = "创建流水线（逐端）" };
    /// <summary>Step 7「覆盖更新（逐端）」：按当前口径重推已有流水线（含部署段）。</summary>
    private readonly Button _updatePipelineButton = new() { Content = "覆盖更新（逐端）" };

    /// <summary>Step 7「准备预览（只读，不创建）」：下发一次只读准备，产出将要提交的 YAML 与绑定清单。</summary>
    private readonly Button _pipelinePrepareButton = new() { Content = "准备预览（只读，不创建）" };

    /// <summary>Step 7 准备读数展示（就绪 / 未就绪 / 失败）。</summary>
    private readonly TextBlock _pipelinePrepareText = new()
    {
        FontSize = 11,
        Foreground = MutedText,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };
    /// <summary>
    /// Step 7「部署目标」区的按端行容器（v0.32）：后端＝下拉 + 机器明细；前端＝固定文案（无独立部署阶段）。
    /// </summary>
    private readonly StackPanel _deployTargetRowsPanel = new() { Spacing = 6 };

    /// <summary>Step 7「刷新主机组」：重新拉取云效主机组列表（只读接口）。</summary>
    private readonly Button _deployRefreshButton = new() { Content = "刷新主机组", FontSize = 12 };

    /// <summary>Step 7 部署目标下拉（当前只有后端有独立部署阶段）。</summary>
    private readonly ComboBox _serviceDeployPicker = new()
    {
        FontSize = 12,
        MinWidth = 220,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    /// <summary>选中主机组的机器明细（创建之前先让用户看清「到底部署到哪台机器」）。</summary>
    private readonly TextBlock _serviceDeployMachinesText = new()
    {
        FontSize = 11,
        Foreground = MutedText,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };

    /// <summary>部署目标区的状态 / 失败提示（拉取失败时红字，并据此阻断创建）。</summary>
    private readonly TextBlock _deployTargetStatusText = new()
    {
        FontSize = 11,
        Foreground = MutedText,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };

    private readonly StackPanel _pipelineRowsPanel = new() { Spacing = 6 };
    private readonly TextBlock _pipelineResultText = new()
    {
        FontSize = 12,
        Foreground = MutedText,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };

    /// <summary>Step 7 待人工绑定清单（按端，只读）。</summary>
    private readonly TextBlock _pipelineBindingsText = new()
    {
        FontSize = 11,
        Foreground = WarningBrush,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };

    /// <summary>Step 7 YAML 预览的端选择（只读回显：预览到什么就提交什么）。</summary>
    private readonly ComboBox _pipelinePreviewPicker = new()
    {
        FontSize = 12,
        MinWidth = 120,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    /// <summary>Step 7 YAML 预览正文（只读；手工微调在独立窗口里做，见 <see cref="PipelineYamlEditorWindow"/>）。</summary>
    private readonly TextBox _pipelineYamlBox = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontSize = 11,
        Height = 220,
        IsVisible = false
    };

    /// <summary>Step 7「编辑 YAML…」：在独立窗口里手工微调选中端的提交原文。</summary>
    private readonly Button _pipelineEditButton = new() { Content = "编辑 YAML…", FontSize = 12 };

    /// <summary>Step 7「重置为模板」：丢弃选中端的手工微调（只在已微调时出现）。</summary>
    private readonly Button _pipelineResetButton = new() { Content = "重置为模板", FontSize = 12, IsVisible = false };

    /// <summary>Step 7「已手工微调」标记。</summary>
    private readonly TextBlock _pipelineEditedText = new()
    {
        Text = "已手工微调",
        FontSize = 11,
        Foreground = WarningBrush,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false
    };

    /// <summary>
    /// Step 7 手工微调的 YAML（按端；<b>只在会话内保留</b>）。
    /// </summary>
    /// <remarks>
    /// 提交成功后<b>实际提交的原文</b>落档案 <c>PipelineResults[].Yaml</c>；重开面板不自动回填，
    /// 避免旧微调在档案配置已变（Step 1 改名 / 仓库名调整）之后悄悄覆盖新渲染结果。
    /// </remarks>
    private readonly Dictionary<ProjectEnd, string> _pipelineYamlOverrides = new();

    /// <summary>Step 7 YAML 预览的说明行（点明这是哪一端的原文）。</summary>
    private readonly TextBlock _pipelineYamlCaption = new()
    {
        FontSize = 11,
        Foreground = MutedText,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false
    };

    /// <summary>Step 7 逐端状态行（端 → 流水线结论）。</summary>
    private readonly List<EndPipelineRowView> _pipelineRows = new();

    /// <summary>「覆盖更新」的破坏性确认（Step 3「我已确认」同一姿势；<c>UpdatePipeline</c> 会整体覆盖 YAML）。</summary>
    private readonly CheckBox _updatePipelineConfirm = new()
    {
        Content = "我已确认：覆盖更新会整体覆盖该端流水线 YAML（云效控制台上的人工改动会被抹掉）",
        FontSize = 12,
        Foreground = MutedText
    };

    /// <summary>Step 7 最近一次预览（<c>null</c>＝尚未预览）。</summary>
    private IReadOnlyList<PipelinePreview> _pipelinePreviews = Array.Empty<PipelinePreview>();

    /// <summary>
    /// Step 7 部署目标候选（云效主机组列表；<c>null</c>＝尚未拉到）。
    /// </summary>
    /// <remarks>
    /// 拉取失败时保持 <c>null</c> 并把原因记在 <see cref="_deployTargetsError"/>：口径是「读不到列表就不许创建流水线」，
    /// 绝不降级成让用户手填 uuid（填错云效只回「该机器组uuid[x]不存在」，白跑一次还污染远端）。
    /// </remarks>
    private IReadOnlyList<DeployHostGroup>? _deployTargets;

    /// <summary>部署目标下拉首次填充时抑制 <c>SelectionChanged</c> 回流（避免把默认选中当成用户改选）。</summary>
    private bool _deployPickerPopulating;

    /// <summary>部署目标列表拉取失败的原因（<c>null</c>＝没失败或还没拉）。</summary>
    private string? _deployTargetsError;

    /// <summary>部署目标列表是否正在拉取。</summary>
    private bool _deployTargetsLoading;

    /// <summary>Step 7 本次选定的部署目标（按端；只有「有独立部署阶段」的端会出现在这里）。</summary>
    private readonly Dictionary<ProjectEnd, DeployHostGroup> _deploySelections = new();

    /// <summary>部署目标下拉当前列出的候选（下标与 <see cref="_serviceDeployPicker"/> 的项一一对应）。</summary>
    private readonly List<DeployHostGroup> _deployPickerGroups = new();

    /// <summary>换过部署目标后的一次性附注（提醒「该端手工微调已丢弃」），随下一次区渲染显示后清空。</summary>
    private string? _deployTargetNote;

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

        _restartConfirmBanner = new Border
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
                    _restartConfirmText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { _restartConfirmButton, _restartCancelButton }
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
        _backButton.Click += OnBackClicked;
        _restartConfirmButton.Click += OnRestartConfirmClicked;
        _restartCancelButton.Click += OnRestartCancelClicked;

        // Step 3 / Step 6（P3）：建库克隆、冲突三选一、推送
        _createRepoButton.Click += OnCreateReposClicked;
        _precheckButton.Click += OnPrecheckClicked;

        // 可见性变更 → 预检作废（仓库名输入在 BuildStep3Area 里逐行挂钩）
        _visibilityInternal.IsCheckedChanged += OnPlanCheckedChanged;
        _visibilityPrivate.IsCheckedChanged += OnPlanCheckedChanged;

        _pushButton.Click += OnPushClicked;
        _repushButton.Click += OnPushClicked;

        // hover 反馈：行内按钮都显式设了背景，主题的 :pointerover 样式被本地值盖住，默认像是「点不动」
        // （P4 冒烟：重推入口没有任何可点提示）。进入时换底色、离开时清掉本地值交回主题默认。
        _repushButton.PointerEntered += (_, _) => _repushButton.Background = HoverBrush;
        _repushButton.PointerExited += (_, _) => _repushButton.ClearValue(Button.BackgroundProperty);
        _probeButton.Click += OnProbeClicked;
        _buildButton.Click += OnBuildClicked;
        _pipelinePrepareButton.Click += OnPreparePipelinesClicked;
        _pipelineButton.Click += OnPipelineClicked;
        _updatePipelineButton.Click += OnUpdatePipelinesClicked;
        _deployRefreshButton.Click += OnRefreshDeployTargetsClicked;
        _serviceDeployPicker.SelectionChanged += OnServiceDeployTargetChanged;
        _pipelineEditButton.Click += OnEditPipelineYamlClicked;
        _pipelineResetButton.Click += OnResetPipelineYamlClicked;
        _pipelinePreviewPicker.SelectionChanged += OnPipelinePreviewChanged;
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

            var rowButton = new Button
            {
                Padding = new Thickness(10, 6),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = row
            };
            // 回退导航（P4 收尾）：点击切到该步；「未到达」的步骤行由 SyncFormEnabled 统一灰化
            rowButton.Click += (_, _) => GoToStep(step);

            steps.Children.Add(rowButton);

            _stepViews.Add(new StepView(step, badge, badgeText, statusBlock, rowButton));
        }

        _statusCard = Card(new StackPanel
        {
            Spacing = 8,
            Children = { _busyText, _validationText, _warningText, _resumeBanner }
        });

        _step1Form = BuildStep1Form();
        _maintenanceCard = BuildMaintenanceCard();
        _step3Area = BuildStep3Area();
        _step4Area = BuildStep4Area();
        _step5Area = BuildStep5Area();
        _step6Area = BuildStep6Area();
        _step7Area = BuildStep7Area();

        // 回退导航行（P4 收尾）：显式「上一步」+ 说明；步骤条行本身也可点击
        _navHint.Margin = new Thickness(10, 0, 0, 0);
        var navRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { _backButton, _navHint }
        };
        Grid.SetColumn(_navHint, 1);

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
                    navRow,
                    _statusCard,
                    _step1Form,
                    _maintenanceCard,
                    _step3Area,
                    _step4Area,
                    _step5Area,
                    _step6Area,
                    _step7Area
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
                     + "（建工作区 → 授权 → 会话 → 技能 → 云效），全过程实时记录在右侧。"
                     + "已建档案后从步骤条回到本页，该按钮的语义变为「清空档案并重走 Step 2」，点它会先要求确认。",
                FontSize = 12,
                Foreground = MutedText,
                TextWrapping = TextWrapping.Wrap
            },
            _restartConfirmBanner,
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
    /// 常驻「档案与维护」卡：任何步骤下都能重跑 Step 2 / 重新推送 / 中止并清理（PRD §4.7）。
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
                Text = "「重跑 Step 2」复用同一工作区并新建执行会话；「重新推送」= 重跑 Step 6（已有改动才提交，"
                     + "推送照常收尾；推进到「推送」步后才出现，且编译未全部通过时会被拒绝）；「中止并清理」注销工作区、"
                     + "删除已写入的技能与 MCP、清空档案——本地目录与云效上的远端产物都不会被删除（PRD §4.7）。",
                FontSize = 11,
                Foreground = MutedText,
                TextWrapping = TextWrapping.Wrap
            },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _rerunButton, _repushButton, _resetButton }
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
                Children = { LabeledField($"{EndLabel(end)}目标仓库名", nameBox), templateText }
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
    /// Step 4 操作区（PRD §4.3 Step 4，v0.22 口径）：数据库参数表单 + 「开始探活」。
    /// </summary>
    /// <remarks>
    /// 本步<b>只探活</b>：真正的连接由项目助手执行（路线②），面板只采集参数并呈现结论；
    /// 不写任何项目文件 / <c>appsettings</c>、不建库，结论<b>不阻断</b>后续步骤。
    /// 口令只走内存（用 <see cref="TextBox.PasswordChar"/> 遮蔽），不回填、不落档案。
    /// </remarks>
    /// <returns>操作区。</returns>
    private Border BuildStep4Area() => Card(new StackPanel
    {
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = "Step 4 · 数据库探活（只读）", FontSize = 14, Foreground = PrimaryText },
            new TextBlock
            {
                Text = "填好参数点「开始探活」：由项目助手亲自连库确认「能连上且该库已存在」。"
                     + "本步不建库、不写任何文件或配置（appsettings 保持模板原样），库请先手工创建；"
                     + "探活结论不阻断后续步骤——失败也可以先继续编译、推送，稍后修好再重跑本步。",
                FontSize = 12,
                Foreground = MutedText,
                TextWrapping = TextWrapping.Wrap
            },
            new TextBlock
            {
                Text = "口令只在内存：不写入项目档案、不写日志，操作记录里以 *** 呈现。"
                     + "（已记录口径：本轮提示词会随框架会话历史落进本地会话库，详见 PRD §4.8）",
                FontSize = 11,
                Foreground = MutedText,
                TextWrapping = TextWrapping.Wrap
            },
            new Border { Height = 1, Background = DividerBrush },
            LabeledField("服务器（主机名或 IP）", _dbHostBox),
            LabeledField("端口", _dbPortBox),
            LabeledField("库名（需你先手工建库）", _dbNameBox),
            LabeledField("账号", _dbUserBox),
            LabeledField("口令", _dbPasswordBox),
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _probeButton }
            },
            _probeResultText
        }
    });

    /// <summary>
    /// Step 5 操作区（PRD §4.3 Step 5）：逐端编译状态列表 + 「开始编译」。
    /// </summary>
    /// <remarks>
    /// 编译方式<b>由助手按工程类型决定</b>（后端 .NET / 前端 Vite 的构建命令不同），面板不做判断，
    /// 只把每端的最近一次结论（落档案的 <see cref="ProjectSpec.BuildResults"/>）摊开给用户复核。
    /// </remarks>
    /// <returns>操作区。</returns>
    private Border BuildStep5Area()
    {
        foreach (var end in new[] { ProjectEnd.Service, ProjectEnd.Admin, ProjectEnd.Mobile })
        {
            var status = new TextBlock
            {
                Text = "未编译",
                FontSize = 12,
                Foreground = MutedText,
                TextWrapping = TextWrapping.Wrap
            };

            var name = new TextBlock
            {
                Text = EndLabel(end),
                FontSize = 12,
                Foreground = PrimaryText,
                Width = 64
            };

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children = { name, status }
            };
            Grid.SetColumn(status, 1);

            _buildRowsPanel.Children.Add(row);
            _buildRows.Add(new EndBuildRowView(end, row, status));
        }

        return Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Step 5 · 逐端编译检查", FontSize = 14, Foreground = PrimaryText },
                new TextBlock
                {
                    Text = "按所选端让项目助手逐端编译（编译方式由助手按工程类型判断：后端 dotnet build、"
                         + "Vite/Vue 前端按本组织既有姿势：fnm 切 Node 16 后 npm run build:dev）。"
                         + "本步只编译、不改业务：允许修依赖树与环境（装依赖 / 切 Node / 补跑依赖构建脚本），"
                         + "禁止改业务源码与工程配置；只要有一端未通过就会阻断 Step 6，把错误修好后重跑本步即可。",
                    FontSize = 12,
                    Foreground = MutedText,
                    TextWrapping = TextWrapping.Wrap
                },
                new Border { Height = 1, Background = DividerBrush },
                _buildRowsPanel,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { _buildButton }
                },
                _buildResultText
            }
        });
    }

    /// <summary>
    /// Step 6 操作区：切到目标分支（配置 <c>pushBranch</c>，v0.28 起默认 develop）→ 暂存 → 有改动则提交 → 推送到各端自有仓库（PRD §4.3 Step 6）。
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
                Text = "逐端执行：切到目标分支（配置 pushBranch，v0.28 起默认 develop；不存在则从模板 pin 分支创建）"
                     + "→ 暂存全部改动（git add .）→ 有改动则提交 → 推送。"
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
    /// Step 7 操作区（PRD §4.3 Step 7 <b>v0.32 口径</b>）：逐端流水线状态 + <b>部署目标选择</b> + YAML 预览（可手工微调）+ 创建 / 覆盖更新。
    /// </summary>
    /// <remarks>
    /// 面板零业务规则：模板、变量与缓存、待绑定清单、<b>「哪端需要部署阶段」与推荐目标</b>全部来自 Step 7 准备阶段的结论文件
    /// （助手产出 → 能力层映射为视图模型，见 <see cref="INewProjectService.PreparePipelinesAsync"/> /
    /// <see cref="INewProjectService.PreviewPipelines"/>），这里只呈现结果、收集选择与微调原文并投递动作。
    /// </remarks>
    /// <returns>操作区。</returns>
    private Border BuildStep7Area()
    {
        foreach (var end in new[] { ProjectEnd.Service, ProjectEnd.Admin, ProjectEnd.Mobile })
        {
            var status = new TextBlock
            {
                Text = "未创建",
                FontSize = 12,
                Foreground = MutedText,
                TextWrapping = TextWrapping.Wrap
            };

            var name = new TextBlock
            {
                Text = EndLabel(end),
                FontSize = 12,
                Foreground = PrimaryText,
                Width = 64
            };

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children = { name, status }
            };
            Grid.SetColumn(status, 1);

            _pipelineRowsPanel.Children.Add(row);
            _pipelineRows.Add(new EndPipelineRowView(end, row, status));
        }

        // 「部署目标」区（v0.32）：后端可选主机组；前端无独立部署阶段（真机取证：上传 OSS 即发布），给固定文案。
        _deployTargetRowsPanel.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "后端：",
                            FontSize = 12,
                            Foreground = PrimaryText,
                            Width = 64,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        _serviceDeployPicker,
                        _deployRefreshButton
                    }
                },
                _serviceDeployMachinesText
            }
        });

        foreach (var front in new[] { ProjectEnd.Admin, ProjectEnd.Mobile })
        {
            _deployTargetRowsPanel.Children.Add(new TextBlock
            {
                Text = $"{EndLabel(front)}：无独立部署阶段（构建产物上传 OSS 即发布）",
                FontSize = 12,
                Foreground = MutedText,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Step 7 · 创建流水线", FontSize = 14, Foreground = PrimaryText },
                new TextBlock
                {
                    Text = "先点「准备预览（只读，不创建）」：助手按 docs 的三端 YAML 模板拉主机组候选并渲染各端将要提交的"
                         + "新版 YAML（云效 OpenAPI 只收顶层 stages: 的新版 YAML，且没有「复制流水线」接口，故不再克隆参考流水线原文），"
                         + "写进结论文件后由面板复核。"
                         + "创建前按流水线名精确检索，命中同名即按当前口径**覆盖更新**该条（幂等，全端）；建完不自动运行。"
                         + "**部署阶段在创建时就写入**：先在下方「部署目标」里选定主机组（可展开机器明细核对），创建即把 deploy 段写进 YAML；"
                         + "读不到主机组列表时本步直接阻断（不降级成手填 uuid）。已有旧流水线可用「覆盖更新（逐端）」按当前口径整体重推。"
                         + "下方 YAML 可点「编辑 YAML…」手工微调（按端、会话内保留）：提交内容与预览原文一致；"
                         + "面板不做 YAML 校验，明显改坏的文本由云效创建时原样报回。",
                    FontSize = 12,
                    Foreground = MutedText,
                    TextWrapping = TextWrapping.Wrap
                },
                new Border { Height = 1, Background = DividerBrush },
                _pipelinePrepareText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { _pipelinePrepareButton }
                },
                _pipelineRowsPanel,
                _pipelineResultText,
                new TextBlock
                {
                    Text = "部署目标（创建前选定；写进 YAML 的 deploy 段）：",
                    FontSize = 12,
                    Foreground = PrimaryText,
                    TextWrapping = TextWrapping.Wrap
                },
                _deployTargetRowsPanel,
                _deployTargetStatusText,
                new TextBlock
                {
                    Text = "待人工绑定清单（云效不做资源重绑定，需你在控制台确认）：",
                    FontSize = 12,
                    Foreground = PrimaryText,
                    TextWrapping = TextWrapping.Wrap
                },
                _pipelineBindingsText,
                new Border { Height = 1, Background = DividerBrush },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { new TextBlock { Text = "YAML 预览：", FontSize = 12, Foreground = PrimaryText, VerticalAlignment = VerticalAlignment.Center }, _pipelinePreviewPicker }
                },
                _pipelineYamlCaption,
                _pipelineYamlBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { _pipelineEditButton, _pipelineResetButton, _pipelineEditedText }
                },
                _updatePipelineConfirm,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { _pipelineButton, _updatePipelineButton }
                }
            }
        });
    }

    /// <summary>Step 3 各端仓库名行的视觉载体。</summary>
    private sealed record RepoRowView(ProjectEnd End, Control Root, TextBox NameBox, TextBlock TemplateText);

    /// <summary>Step 5 逐端编译状态行的视觉载体。</summary>
    private sealed record EndBuildRowView(ProjectEnd End, Control Root, TextBlock Status);

    /// <summary>Step 7 逐端流水线状态行的视觉载体。</summary>
    private sealed record EndPipelineRowView(ProjectEnd End, Control Root, TextBlock Status);

    /// <summary>端的中文标签（**已自带「端」字**：调用方不得再拼一个「端」；与能力层口径一致）。</summary>
    /// <param name="end">端。</param>
    /// <returns>标签。</returns>
    /// <remarks>v0.29：与 <c>NewProjectService.EndLabel</c> 同步——此前多处拼「端」产出「管理端端」等重字。</remarks>
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
        var reached = _service.CurrentStep;

        // 若本次刷新观察到推进指针前移（刚执行的动作把流程推到了后一步），丢弃回退游标让视图跟上新步骤。
        // 反之：只是「回看早已到达的步骤」时指针不会前移，游标保持——用户能停在该步查看逐端结论。
        if ((int)reached > (int)_lastKnownReached
            && _viewStep is { } advancing
            && (int)reached > (int)advancing)
            _viewStep = null;
        _lastKnownReached = reached;

        // 视图游标只在「已到达」范围内有效：档案被重置（如「中止并清理」）后丢弃过期游标
        if (_viewStep is { } pinned && (int)pinned > (int)reached)
            _viewStep = null;
        var currentStep = _viewStep ?? reached;

        _archiveText.Text = spec is null
            ? "尚未开始新项目：在 Step 1 填表后创建档案；"
              + "聊天入口说「新建一个项目，缩写 jnms」也会落到同一档案。"
            : $"项目档案：{spec.Name}（{spec.Code}）· 端 {string.Join("/", spec.Ends)} · 本地目录 "
              + $"{(string.IsNullOrWhiteSpace(spec.LocalPath) ? "未设置" : spec.LocalPath)}";

        _stepHint.Text = spec is null
            ? "当前步骤：1 基本信息（待填表）"
            : $"当前步骤：{(int)currentStep} {Steps[(int)currentStep - 1].Title}"
              + (_viewStep is not null && _viewStep < reached
                  ? $"（回看；档案已到达 {(int)reached} {Steps[(int)reached - 1].Title}）"
                  : string.Empty);

        if (spec is not null && !_resumeResolved)
        {
            _resumeText.Text = $"检测到上次未完成的项目档案：{spec.Name}（{spec.Code}）· 已推进到 {(int)spec.CurrentStep} 步"
                               + $"{(string.IsNullOrWhiteSpace(spec.WorkspaceId) ? string.Empty : " · 工作区已建立")}。"
                               + "请先选择处置方式，「下一步」在处置前不可用（不会静默覆盖）。";
        }

        SyncFormEnabled();
        SyncStepAreas(spec, currentStep);

        // 步骤状态一律取自「真实终态」，不再用「序号小于当前步」推断：
        // 序号推断会把「从未执行过的后续步」显示成已完成，也会把「停在该步但当前没在跑」
        // 显示成进行中（P3 冒烟缺陷：Step 4/5 未实现却显示「进行中」）。
        // 记录只在内存、重启即空，故必须与档案里的终态快照归并（规则集中在 StepStateResolver）。
        var operations = _service.Operations;
        var stepStates = StepStateResolver.Resolve(spec?.StepStates, operations);

        foreach (var view in _stepViews)
        {
            var isCurrent = spec is not null && view.Step == currentStep;
            stepStates.TryGetValue(view.Step, out var state);

            // Step 1 是纯表单采集，能力层不为其记成功记录：只要档案已建立即视为已完成
            var isSucceeded = state == StepStatus.Succeeded
                              || (view.Step == WizardStep.BasicInfo && spec is not null);
            var isRunning = state == StepStatus.Running;
            var isFailed = !isSucceeded && !isRunning && state == StepStatus.Failed;
            var isSkipped = !isSucceeded && !isRunning && !isFailed && state == StepStatus.Skipped;

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
                : isSkipped ? "已跳过"
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
    /// 步骤条行的视觉载体（步骤号 + 序号徽标 + 状态文字 + 可点击的行按钮）。
    /// </summary>
    /// <param name="Step">步骤。</param>
    /// <param name="Badge">序号徽标。</param>
    /// <param name="BadgeText">徽标文字。</param>
    /// <param name="Status">状态文字。</param>
    /// <param name="Button">行按钮（回退导航入口；未到达的步骤由 <see cref="SyncFormEnabled"/> 灰化）。</param>
    private sealed record StepView(WizardStep Step, Border Badge, TextBlock BadgeText, TextBlock Status, Button Button);

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
    /// 按当前步骤切换左列的操作区：Step 1 表单 / Step 3 建库确认区 / Step 4-5-7 操作区 / Step 6 推送区。
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

        // Step 1 的「下一步」在已建档案后语义变为「清空档案并重走 Step 2」（破坏性，卡内先行确认）
        _nextButton.Content = hasArchive ? "清空档案并重走 Step 2" : "下一步：初始化项目";

        // 「重新推送（重跑 Step 6）」：流程已到达推送步（Step 6 及之后）才显示。
        // 早期按「至少一端已克隆」判定，Step 3 一完成就出现，等于给出「编译（Step 5）之前越级推送」的通道：
        // 推送成功把档案推到 Step 7，而当时的向导没有回退导航，Step 5 就此永久不可达（真机冒烟缺陷）。
        // 现在推进指针到 Step 6 即蕴含「Step 5 已通过」（能力层另有硬校验），且回退导航已补齐，常驻才安全。
        _repushButton.IsVisible = spec is not null && (int)spec.CurrentStep >= (int)WizardStep.Push;

        _step3Area.IsVisible = hasArchive && currentStep == WizardStep.Repositories;
        _step4Area.IsVisible = hasArchive && currentStep == WizardStep.Database;
        _step5Area.IsVisible = hasArchive && currentStep == WizardStep.Build;
        _step6Area.IsVisible = hasArchive && currentStep == WizardStep.Push;
        _step7Area.IsVisible = hasArchive && currentStep == WizardStep.Pipeline;

        if (spec is not null && _step3Area.IsVisible)
            PopulateStep3Area(spec);

        if (spec is not null && _step4Area.IsVisible)
            PopulateStep4Area(spec);

        if (spec is not null && _step5Area.IsVisible)
            PopulateStep5Area(spec);

        if (spec is not null && _step7Area.IsVisible)
            PopulateStep7Area(spec);
    }

    /// <summary>
    /// 解析当前视图步：显式游标优先，且不越过档案的「已到达最大步」。
    /// </summary>
    /// <param name="reached">档案推进指针（已到达最大步）。</param>
    /// <returns>视图步。</returns>
    private WizardStep CurrentViewStep(WizardStep reached)
        => _viewStep is { } pinned && (int)pinned <= (int)reached ? pinned : reached;

    /// <summary>
    /// 切到指定步骤（PRD §4.6「步骤条可点击 / 上一步」）。
    /// </summary>
    /// <remarks>
    /// 只移动面板的视图游标，不改档案推进指针，也不触发任何步骤动作——动作必须由用户在该步的操作区显式触发。
    /// 否则「点一下步骤条就重跑一遍建库」这类误触代价太大。超出「已到达最大步」的步骤必须按顺序执行，不可跳过去。
    /// </remarks>
    /// <param name="step">目标步骤。</param>
    private void GoToStep(WizardStep step)
    {
        if (_busy || _unloaded)
            return;

        if (_service.Current is null || (int)step > (int)_service.CurrentStep)
            return;

        // 导航即意味着放弃上一次「清空档案重来」的确认意图
        _restartConfirmed = false;
        _restartConfirmBanner.IsVisible = false;

        if (_viewStep == step)
            return;

        _viewStep = step;
        Refresh();
    }

    /// <summary>「上一步」（PRD §4.6 草图项）：视图游标 −1，只在「已到达」步之间移动。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        var step = CurrentViewStep(_service.CurrentStep);
        if (step > WizardStep.BasicInfo)
            GoToStep(step - 1);
    }

    /// <summary>
    /// 回填 Step 4 表单（同一档案只回填一次，避免覆盖用户输入；<b>口令永不回填</b>）。
    /// </summary>
    /// <remarks>
    /// 默认值来源（优先级由能力层 <c>SuggestDatabaseProbe</c> 决定）：档案里已填的非敏感参数 →
    /// 已克隆后端仓库的 <c>WebHost/appsettings.json</c> 连接串。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    private void PopulateStep4Area(ProjectSpec spec)
    {
        if (!string.Equals(_step4LoadedForCode, spec.Code, StringComparison.Ordinal))
        {
            var suggestion = _service.SuggestDatabaseProbe();

            _populating = true;
            try
            {
                _dbHostBox.Text = suggestion.Host;
                _dbPortBox.Text = suggestion.Port > 0
                    ? suggestion.Port.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                _dbNameBox.Text = suggestion.Database;
                _dbUserBox.Text = suggestion.User;
                _dbPasswordBox.Text = string.Empty;
            }
            finally
            {
                _populating = false;
            }

            _step4LoadedForCode = spec.Code;
        }

        // 结论区读档案里的最近一次探活（跨重启可见）；口令永不回显
        if (spec.Database.ProbedAt is not null && !string.IsNullOrWhiteSpace(spec.Database.ProbeMessage))
        {
            _probeResultText.IsVisible = true;
            _probeResultText.Foreground = spec.Database.ProbeOk ? SuccessBrush : WarningBrush;
            _probeResultText.Text = $"最近一次探活（{spec.Database.ProbedAt:yyyy-MM-dd HH:mm}）："
                                    + (spec.Database.ProbeOk ? "✔ " : "✖ ")
                                    + spec.Database.ProbeMessage;
        }
        else
        {
            _probeResultText.IsVisible = false;
        }
    }

    /// <summary>
    /// 刷新 Step 5 逐端状态行与汇总文案（数据来自档案里的 <see cref="ProjectSpec.BuildResults"/> 与步骤终态快照）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    private void PopulateStep5Area(ProjectSpec spec)
    {
        foreach (var row in _buildRows)
        {
            var inScope = spec.Ends.Count == 0 || spec.Ends.Contains(row.End);
            row.Root.IsVisible = inScope;
            if (!inScope)
                continue;

            var result = spec.BuildResults.FirstOrDefault(item => item.End == row.End);
            if (result is null)
            {
                row.Status.Text = "未编译";
                row.Status.Foreground = MutedText;
                continue;
            }

            row.Status.Foreground = result.Ok ? SuccessBrush : DangerBrush;
            row.Status.Text = (result.Ok ? "✔ 通过" : "✖ 失败")
                              + (string.IsNullOrWhiteSpace(result.Command) ? string.Empty : $"｜{result.Command}")
                              + $"｜{result.Summary}";
        }

        var lastFailed = spec.StepStates.TryGetValue(nameof(WizardStep.Build), out var raw)
                         && string.Equals(raw, nameof(StepStatus.Failed), StringComparison.Ordinal);

        if (spec.BuildResults.Count == 0)
        {
            _buildResultText.IsVisible = lastFailed;
            _buildResultText.Foreground = DangerBrush;
            _buildResultText.Text = "最近一次编译未通过且未产出逐端结论，详见右侧操作记录。";
            return;
        }

        var failed = spec.BuildResults.Count(item => !item.Ok);
        _buildResultText.IsVisible = true;
        _buildResultText.Foreground = failed == 0 && !lastFailed ? SuccessBrush : DangerBrush;
        _buildResultText.Text = failed == 0 && !lastFailed
            ? $"最近一次编译：{spec.BuildResults.Count} 端全部通过"
              + $"（{spec.BuildResults.Max(item => item.CheckedAt):yyyy-MM-dd HH:mm}）。"
              + "可在上方步骤条点「6 推送」继续推送（该步执行前仍会复核本结论）。"
            : $"最近一次编译未全部通过（下表为最近一次产出结论的执行结果）：{failed}/{spec.BuildResults.Count} 端失败。"
              + "本步失败会阻断 Step 6，修好后重跑本步。";
    }

    /// <summary>
    /// 刷新 Step 7 逐端状态行、待人工绑定清单与 YAML 预览（数据来自能力层 <see cref="INewProjectService.PreviewPipelines"/>，
    /// 面板零业务规则）。
    /// </summary>
    /// <remarks>
    /// 预览口径与创建口径同源：<b>预览到什么就提交什么</b>。已创建端从档案里带出流水线 id / 链接与部署阶段状态；
    /// 未创建端如实显示「未创建」，不假装已经建好。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    private void PopulateStep7Area(ProjectSpec spec)
    {
        // v0.32：预览必须带上本会话已选的部署目标，否则「预览的 YAML」与「创建的 YAML」会不一致。
        _pipelinePreviews = _service.PreviewPipelines(_pipelineYamlOverrides, _deploySelections);

        foreach (var row in _pipelineRows)
        {
            var inScope = spec.Ends.Count == 0 || spec.Ends.Contains(row.End);
            row.Root.IsVisible = inScope;
            if (!inScope)
                continue;

            var preview = _pipelinePreviews.FirstOrDefault(item => item.End == row.End);
            var result = spec.PipelineResults.FirstOrDefault(item => item.End == row.End);

            if (result is null || string.IsNullOrWhiteSpace(result.PipelineId))
            {
                var cloned = spec.Repositories.Any(item => item.End == row.End && item.Cloned);
                row.Status.Foreground = MutedText;
                row.Status.Text = preview is null
                    ? cloned
                        ? "未创建（尚未执行『准备（只读）』：先点该按钮生成预览，确认后再创建）"
                        : "未创建（该端尚未建库 / 克隆，本步会被跳过）"
                    : $"未创建｜将创建 {preview.PipelineName}";
                continue;
            }

            row.Status.Foreground = SuccessBrush;
            row.Status.Text = "✔ 已创建"
                              + (result.Created ? "（本次新建）" : "（命中同名，已覆盖更新）")
                              + $"｜{result.PipelineName}｜id {result.PipelineId}"
                              + (result.DeployStageIncluded
                                  ? $"｜含部署阶段（{result.DeployMachineGroupName} / {result.DeployMachineGroupUuid}）"
                                  : "｜未含部署阶段")
                              + (result.WebUrl.Length == 0 ? string.Empty : $"｜{result.WebUrl}");
        }

        // ── 待人工绑定清单：只列「已创建 / 将创建」端的条目，避免未建库时刷一屏空话 ──
        var pending = _pipelinePreviews
            .SelectMany(item => item.PendingBindings.Select(binding => $"{EndLabel(item.End)}{binding}"))
            .ToList();

        if (pending.Count == 0)
        {
            _pipelineBindingsText.IsVisible = false;
        }
        else
        {
            _pipelineBindingsText.IsVisible = true;
            _pipelineBindingsText.Text = string.Join("\n", pending.Select(item => $"· {item}"));
        }

        PopulatePipelinePreviewPicker();
        PopulateDeployTargetArea(spec);

        var lastFailed = spec.StepStates.TryGetValue(nameof(WizardStep.Pipeline), out var raw)
                         && string.Equals(raw, nameof(StepStatus.Failed), StringComparison.Ordinal);

        if (spec.PipelineResults.Count == 0)
        {
            _pipelineResultText.IsVisible = lastFailed;
            _pipelineResultText.Foreground = DangerBrush;
            _pipelineResultText.Text = lastFailed
                ? "最近一次创建流水线未通过且档案里没有成功结论，详见右侧操作记录。"
                : string.Empty;
            return;
        }

        var created = spec.PipelineResults.Count(item => !string.IsNullOrWhiteSpace(item.PipelineId));
        var withDeploy = spec.PipelineResults.Count(item => item.DeployStageIncluded);
        _pipelineResultText.IsVisible = true;
        _pipelineResultText.Foreground = lastFailed ? DangerBrush : SuccessBrush;
        _pipelineResultText.Text = $"档案里已有 {created} 端流水线（{withDeploy} 端含部署阶段）"
                                   + (lastFailed ? "；最近一次执行仍有失败项，详见右侧操作记录。" : "。");
    }

    /// <summary>
    /// 刷新「部署目标」区（v0.32）：拉取（首帧触发）/ 填充候选、回显默认与已选目标、摊出机器明细、给失败红字。
    /// </summary>
    /// <remarks>
    /// <b>阻断口径</b>：列表读不到（未配 PAT / 无权限 / 网络失败）或没有可用主机组时，只在此处与执行入口
    /// （<see cref="RunCreatePipelinesAsync"/>）给红字并拒绝创建，<b>不降级成手填 uuid</b>。
    /// 「哪端需要部署阶段」一律由能力层 <see cref="PipelinePreview.YamlIncludesDeployStage"/> 给出，面板不复制判断。
    /// </remarks>
    /// <param name="spec">项目档案。</param>
    private void PopulateDeployTargetArea(ProjectSpec spec)
    {
        var serviceInScope = spec.Ends.Count == 0 || spec.Ends.Contains(ProjectEnd.Service);
        var preview = _pipelinePreviews.FirstOrDefault(item => item.End == ProjectEnd.Service);

        // 只有后端有独立部署阶段（真机取证：前端两端上传 OSS 即发布）；后端未选中时本区无需交互。
        if (!serviceInScope || preview?.YamlIncludesDeployStage != true)
        {
            _serviceDeployPicker.SelectedIndex = -1;
            _serviceDeployPicker.ItemsSource = Array.Empty<string>();
            _serviceDeployPicker.IsEnabled = false;
            _deployPickerGroups.Clear();
            _deploySelections.Remove(ProjectEnd.Service);
            _deployRefreshButton.IsEnabled = false;
            _serviceDeployMachinesText.IsVisible = false;
            _deployTargetStatusText.IsVisible = true;
            _deployTargetStatusText.Foreground = MutedText;
            _deployTargetStatusText.Text = serviceInScope
                ? "该端没有独立部署阶段（构建产物上传 OSS 即发布），无需选择部署目标。"
                : "本端未选（Step 1 未勾选后端），无需部署目标。";
            return;
        }

        _deployRefreshButton.IsEnabled = !_deployTargetsLoading;

        if (_deployTargetsLoading)
        {
            _deployTargetStatusText.IsVisible = true;
            _deployTargetStatusText.Foreground = MutedText;
            _deployTargetStatusText.Text = "正在读取云效主机组列表…";
            return;
        }

        if (_deployTargetsError is not null)
        {
            _serviceDeployPicker.IsEnabled = false;
            _serviceDeployMachinesText.IsVisible = false;
            _deployTargetStatusText.IsVisible = true;
            _deployTargetStatusText.Foreground = DangerBrush;
            _deployTargetStatusText.Text = "✖ 读不到云效主机组列表，已阻断创建：" + _deployTargetsError
                                          + "（本步不做「手填 uuid」降级——uuid 填错云效只回「该机器组uuid[x]不存在」。"
                                          + "确认凭据 / 权限后点「刷新主机组」。）";
            return;
        }

        if (_deployTargets is null)
        {
            _deployTargetStatusText.IsVisible = true;
            _deployTargetStatusText.Foreground = MutedText;
            _deployTargetStatusText.Text = "正在读取云效主机组列表…";
            _ = LoadDeployTargetsAsync();
            return;
        }

        FillDeployPicker(preview.RecommendedDeployTargetUuid);
    }

    /// <summary>
    /// 用已拉到的候选填充部署目标下拉：保持用户已选（按 uuid 认），没选则默认选中配置推荐值。
    /// </summary>
    /// <remarks>
    /// <b>不默认选第一项</b>：组织里有多个主机组（含 UAT / PRD），默认选第一项极容易把发布打到别的主机组。
    /// </remarks>
    /// <param name="recommendedUuid">插件配置里的推荐 uuid（空＝没有默认，保持未选）。</param>
    private void FillDeployPicker(string recommendedUuid)
    {
        var groups = (_deployTargets ?? Array.Empty<DeployHostGroup>())
            .Where(group => !string.IsNullOrWhiteSpace(group.Uuid))
            .ToList();

        _deployPickerGroups.Clear();
        _deployPickerGroups.AddRange(groups);

        _deployPickerPopulating = true;
        try
        {
            _serviceDeployPicker.ItemsSource = groups.Select(group => group.DisplayLabel).ToList();

            var current = _deploySelections.TryGetValue(ProjectEnd.Service, out var selected) ? selected.Uuid : string.Empty;
            var index = groups.FindIndex(group => string.Equals(group.Uuid, current, StringComparison.Ordinal));
            if (index < 0 && !string.IsNullOrWhiteSpace(recommendedUuid))
                index = groups.FindIndex(group => string.Equals(group.Uuid, recommendedUuid.Trim(), StringComparison.Ordinal));

            _serviceDeployPicker.SelectedIndex = index;

            if (index >= 0)
                _deploySelections[ProjectEnd.Service] = groups[index];
            else
                _deploySelections.Remove(ProjectEnd.Service);
        }
        finally
        {
            _deployPickerPopulating = false;
        }

        _serviceDeployPicker.IsEnabled = groups.Count > 0;

        if (groups.Count == 0)
        {
            _serviceDeployMachinesText.IsVisible = false;
            _deployTargetStatusText.IsVisible = true;
            _deployTargetStatusText.Foreground = DangerBrush;
            _deployTargetStatusText.Text = "云效返回的主机组列表里没有带 uuid 的可用目标，已阻断创建："
                                           + "请到云效控制台确认主机组后点「刷新主机组」。";
            return;
        }

        var chosen = _serviceDeployPicker.SelectedIndex >= 0;
        _deployTargetStatusText.IsVisible = true;
        _deployTargetStatusText.Foreground = chosen ? MutedText : DangerBrush;
        _deployTargetStatusText.Text = chosen
            ? $"共 {groups.Count} 个可用主机组；写进 YAML 的 machineGroup 是主机组 uuid（不是列表里的数字 ID）。"
              + (string.IsNullOrWhiteSpace(recommendedUuid) ? string.Empty : "（已按插件配置默认选中）")
            : $"共 {groups.Count} 个可用主机组，但你尚未选择——创建前必须先选一个（可展开机器明细核对落点）。";

        // 刚换过目标的一次性提示（旧微调已丢弃）。
        if (_deployTargetNote is { } note)
        {
            _deployTargetStatusText.Text += note;
            _deployTargetNote = null;
        }

        SyncDeployMachinesText();
    }

    /// <summary>
    /// 显示当前选中主机组的机器明细（已缓存则直接显示，否则异步拉取；0 台机器给告警而不是报错）。
    /// </summary>
    private void SyncDeployMachinesText()
    {
        var index = _serviceDeployPicker.SelectedIndex;
        if (index < 0 || index >= _deployPickerGroups.Count)
        {
            _serviceDeployMachinesText.IsVisible = false;
            return;
        }

        var group = _deployPickerGroups[index];

        if (group.Machines.Count > 0)
        {
            _serviceDeployMachinesText.IsVisible = true;
            _serviceDeployMachinesText.Foreground = MutedText;
            _serviceDeployMachinesText.Text = "机器明细：\n"
                                              + string.Join("\n", group.Machines.Select(machine => "    · " + machine.DisplayLabel));
            return;
        }

        // 真机观测存在 hostNum=0 的空组（UAT / 公众版 / 专业版主机组均为 0）：可以选，但要让用户看清「没机器」。
        if (group.HostNum == 0)
        {
            _serviceDeployMachinesText.IsVisible = true;
            _serviceDeployMachinesText.Foreground = WarningBrush;
            _serviceDeployMachinesText.Text = "该主机组当前 0 台机器：部署会找不到落点，请确认是否选错主机组。";
            return;
        }

        _serviceDeployMachinesText.IsVisible = true;
        _serviceDeployMachinesText.Foreground = MutedText;
        _serviceDeployMachinesText.Text = $"正在读取「{group.Name}」的机器明细…";
        _ = LoadDeployMachinesAsync(group);
    }

    /// <summary>
    /// 拉取云效主机组列表（只读，v0.32）。失败只把原因记在 <see cref="_deployTargetsError"/> 上（界面据此阻断创建）。
    /// </summary>
    private async Task LoadDeployTargetsAsync()
    {
        if (_deployTargetsLoading)
            return;

        _deployTargetsLoading = true;
        try
        {
            _deployTargets = await _service.ListDeployTargetsAsync().ConfigureAwait(true);
            _deployTargetsError = null;
        }
        catch (Exception ex)
        {
            _deployTargets = null;
            _deployTargetsError = ex.Message;
        }
        finally
        {
            _deployTargetsLoading = false;
        }

        // 回填界面：期间用户可能已切走步骤（面板卸载）或档案被清空，两种情况都只更新状态、不碰控件。
        if (!_unloaded && _service.Current is { } spec)
            PopulateDeployTargetArea(spec);
    }

    /// <summary>
    /// 拉取某主机组的机器明细（只读）。失败<b>不阻断</b>创建（阻断判据只看列表能否读到），只在明细行给提示。
    /// </summary>
    /// <param name="group">主机组（取到明细后缓存在该实例上，避免重复请求）。</param>
    private async Task LoadDeployMachinesAsync(DeployHostGroup group)
    {
        try
        {
            group.Machines = await _service.ListDeployTargetMachinesAsync(group.HostGroupId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (!_unloaded)
            {
                _serviceDeployMachinesText.IsVisible = true;
                _serviceDeployMachinesText.Foreground = WarningBrush;
                _serviceDeployMachinesText.Text = $"机器明细读取失败：{ex.Message}";
            }

            return;
        }

        // 只有该组仍被选中时才回写界面（等待期间用户可能改选别的组）。
        var index = _serviceDeployPicker.SelectedIndex;
        if (!_unloaded
            && index >= 0
            && index < _deployPickerGroups.Count
            && ReferenceEquals(_deployPickerGroups[index], group))
        {
            SyncDeployMachinesText();
        }
    }

    /// <summary>Step 7「刷新主机组」：丢弃旧读数并重新拉取主机组列表（只读）。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnRefreshDeployTargetsClicked(object? sender, RoutedEventArgs e)
    {
        if (_unloaded || _service.Current is null || _deployTargetsLoading)
            return;

        _deployTargets = null;
        _deployTargetsError = null;
        _serviceDeployPicker.SelectedIndex = -1;
        _serviceDeployPicker.ItemsSource = Array.Empty<string>();
        _deployPickerGroups.Clear();
        _serviceDeployMachinesText.IsVisible = false;

        await LoadDeployTargetsAsync();
    }

    /// <summary>
    /// 部署目标改选（v0.32）：记住选择、刷新机器明细，并<b>丢弃该端的手工微调</b>。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// 为什么必须丢弃微调：手工微调原文里写着旧的 <c>machineGroup</c> uuid，留着会出现「下拉显示 A 主机组、
    /// 提交的 YAML 却打 B 主机组」的自相矛盾；提示语由 <see cref="_deployTargetNote"/> 随下一次渲染给出。
    /// </remarks>
    private void OnServiceDeployTargetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_deployPickerPopulating || _unloaded)
            return;

        var index = _serviceDeployPicker.SelectedIndex;
        if (index < 0 || index >= _deployPickerGroups.Count)
        {
            _deploySelections.Remove(ProjectEnd.Service);
            _serviceDeployMachinesText.IsVisible = false;
            return;
        }

        var group = _deployPickerGroups[index];
        if (_deploySelections.TryGetValue(ProjectEnd.Service, out var previous)
            && string.Equals(previous.Uuid, group.Uuid, StringComparison.Ordinal))
        {
            return;
        }

        _deploySelections[ProjectEnd.Service] = group;

        if (_pipelineYamlOverrides.Remove(ProjectEnd.Service))
            _deployTargetNote = $"。注意：部署目标已变更，已丢弃 {EndLabel(ProjectEnd.Service)} 的手工微调 YAML（旧文本里的 machineGroup 已过期）";

        if (_service.Current is { } spec)
            PopulateStep7Area(spec);
    }

    /// <summary>
    /// 刷新 YAML 预览的端选择器与正文（保持用户当前选择；选择项随所选端变化）。
    /// </summary>
    private void PopulatePipelinePreviewPicker()
    {
        var selected = _pipelinePreviewPicker.SelectedItem as string;

        _pipelinePreviewPicker.ItemsSource = _pipelinePreviews
            .Select(item => EndLabel(item.End))
            .ToList();

        var keep = _pipelinePreviews.Any(item => EndLabel(item.End) == selected) ? selected : null;
        _pipelinePreviewPicker.SelectedItem = keep
                                              ?? _pipelinePreviews.Select(item => EndLabel(item.End)).FirstOrDefault();

        ShowPipelineYaml(_pipelinePreviewPicker.SelectedItem as string);
    }

    /// <summary>把选中端的 YAML 原文摊到只读框里（已手工微调则显示微调后的原文）。</summary>
    /// <param name="endLabel">端标签；<c>null</c>／未命中时清空。</param>
    private void ShowPipelineYaml(string? endLabel)
    {
        var preview = _pipelinePreviews.FirstOrDefault(item => EndLabel(item.End) == endLabel);
        if (preview is null)
        {
            _pipelineYamlBox.IsVisible = false;
            _pipelineYamlCaption.IsVisible = false;
            _pipelineEditButton.IsEnabled = false;
            _pipelineResetButton.IsVisible = false;
            _pipelineEditedText.IsVisible = false;
            return;
        }

        _pipelineYamlCaption.IsVisible = true;
        _pipelineYamlCaption.Text = $"{EndLabel(preview.End)}将提交的流水线名 {preview.PipelineName}"
                                    + (preview.Created ? "（该端已存在，再次执行为覆盖更新）" : "（新建）")
                                    + (preview.Edited ? "。下方为**手工微调后的原文**，与提交内容完全一致：" : "。下方为原文预览，与提交内容完全一致：");
        _pipelineYamlBox.IsVisible = true;
        _pipelineYamlBox.Text = preview.Yaml;
        _pipelineEditButton.IsEnabled = !_busy;
        _pipelineResetButton.IsVisible = preview.Edited;
        _pipelineEditedText.IsVisible = preview.Edited;
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

        // Step 4 / 5：忙碌时只锁动作按钮与输入框（结论区保持可见，便于对照）
        _dbHostBox.IsEnabled = !_busy;
        _dbPortBox.IsEnabled = !_busy;
        _dbNameBox.IsEnabled = !_busy;
        _dbUserBox.IsEnabled = !_busy;
        _dbPasswordBox.IsEnabled = !_busy;
        _probeButton.IsEnabled = !_busy;
        _buildButton.IsEnabled = !_busy;

        // Step 3 / Step 6 / 占位区（P3）：忙碌时只锁动作按钮；冲突三选一必须保持可点
        // —— 它回答的正是「Step 3 正在运行」时的问题，锁掉会直接把能力层挂死到超时。
        _precheckButton.IsEnabled = !_busy;
        _pipelinePrepareButton.IsEnabled = !_busy;
        SyncCreateEnabled();
        _pushButton.IsEnabled = !_busy;
        _repushButton.IsEnabled = !_busy;
        _confirmedCheck.IsEnabled = !_busy;
        _visibilityInternal.IsEnabled = !_busy;
        _visibilityPrivate.IsEnabled = !_busy;
        foreach (var row in _repoRows)
            row.NameBox.IsEnabled = !_busy;

        // 回退导航（P4 收尾）：只放开「已到达」的步骤（回看 / 重跑）；超过推进指针的必须按顺序执行。
        // 步骤条行与「上一步」同受此约束；残留档案未处置时一并锁住（PRD §4.7）。
        var reached = _service.CurrentStep;
        var navigable = hasArchive && !_busy && !blockedByResidual;
        foreach (var view in _stepViews)
        {
            var reachable = navigable && (int)view.Step <= (int)reached;
            view.Button.IsEnabled = reachable;

            // 显式降不透明度：行内 TextBlock 全都显式设了前景色，主题的「禁用前景色」覆盖不到，
            // 只靠 IsEnabled 在视觉上几乎看不出区别（P4 冒烟：未到达的行与可点行肉眼一样）。
            // 灰化只按「进度」（是否已到达），与「能不能点」解耦——否则忙碌期间连当前步与已完成步
            // 一起变淡，看起来像进度丢了（P4 冒烟收口）。
            view.Button.Opacity = (int)view.Step <= (int)reached ? 1.0 : 0.45;
        }

        _backButton.IsEnabled = navigable && (int)CurrentViewStep(reached) > (int)WizardStep.BasicInfo;

        SyncStatusCardVisibility();
    }

    /// <summary>
    /// 常驻「状态提示」卡的空壳收敛：忙碌 / 校验错误 / 警告 / 残留横幅<b>全都没有</b>时整卡隐藏。
    /// </summary>
    /// <remarks>
    /// 卡内三个文本块各自按需隐藏，但卡自身（带底色与描边）不会自动收起来，留下一个空洞的圆角框
    /// （P3 真机截图缺陷）。故可见性由「卡内是否有可见内容」统一决定。
    /// </remarks>
    private void SyncStatusCardVisibility()
    {
        if (_statusCard is null)
            return;

        _statusCard.IsVisible = _busy
                                || _validationText.IsVisible
                                || _warningText.IsVisible
                                || _resumeBanner.IsVisible;
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
        SyncStatusCardVisibility();
    }

    /// <summary>展示非阻断提示（空集合即清除）；与错误区分色，且不阻止「下一步」。</summary>
    /// <param name="warnings">提示文案。</param>
    private void ShowWarnings(IEnumerable<string> warnings)
    {
        var list = warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)).ToList();
        _warningText.Text = string.Join("\n", list.Select(warning => "• " + warning));
        _warningText.IsVisible = list.Count > 0;
        SyncStatusCardVisibility();
    }

    /// <summary>记住最近一次校验命中的已登记工作区（供「复用该工作区继续」出口使用）。</summary>
    /// <param name="validation">校验结果。</param>
    private void RememberExistingWorkspace(BasicInfoValidation validation)
    {
        _existingWorkspaceId = validation.ExistingWorkspaceId;
        _existingWorkspaceName = validation.ExistingWorkspaceName;
    }

    /// <summary>两个本地目录是否指同一处（归一化后比较）。</summary>
    /// <remarks>
    /// 判据与服务端唯一性口径①<b>同源</b>：服务端按 <c>WorkspaceIdGenerator.Compute(localPath)</c> 派生
    /// <c>WorkspaceId</c> 再查宿主工作区（<c>NewProjectService.FindWorkspaceByPathAsync</c>），故此处直接用同一个
    /// <see cref="WorkspaceIdGenerator.Normalize" /> 归一化——解析符号链接 / junction、去尾部分隔符、
    /// Windows 下统一大写。若自行写一套（如只做 <c>GetFullPath</c> + 忽略大小写），「档案路径是 junction、
    /// 用户填真实目标」这类别名会被误判为「不同目录」，弹出破坏性确认后照样撞唯一性①的红字（死胡同）。
    /// 归一化抛异常（非法字符等）时退回原样比较，路径本身的合法性由后续校验负责报错。
    /// </remarks>
    /// <param name="left">左值（多为表单输入）。</param>
    /// <param name="right">右值（多为档案 <c>LocalPath</c>）。</param>
    /// <returns>指同一处返回 <c>true</c>。</returns>
    private static bool SameLocalPath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        static string Normalize(string path)
        {
            try
            {
                return WorkspaceIdGenerator.Normalize(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        // 归一化在 Windows 下已统一大写（与服务端判据一致），故按 Ordinal 比较；
        // 非 Windows 文件系统大小写敏感，用忽略大小写反而会与「WorkSpaceId 派生」口径漂移。
        return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
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

        // 已建档案后从步骤条回退到 Step 1 再点「下一步」＝清空档案、从 Step 2 重来（Step 3-6 的进度会重置）。
        // 属破坏性动作，必须先显式确认（PRD §4.7「禁止静默覆盖档案」）。
        if (!allowExistingWorkspace && _service.Current is { } existing && !_restartConfirmed)
        {
            // 同目录不是「另建项目」而是「沿用当前工作区」：该目录已被登记（Step 2 建过），重提必被唯一性
            // 口径①拦下，而「复用该工作区继续」按钮在已有档案时按设计不出现——确认了只会撞一条指不到出路的
            // 红字。故这里不弹破坏性横幅，改为指路「重跑 Step 2」（复用同一工作区、进度不丢）。
            if (SameLocalPath(_pathBox.Text, existing.LocalPath))
            {
                // 清上一轮残留红字：本分支不会走到后面的 ShowErrors(空)，不清会与黄字并排显示
                ShowErrors(Array.Empty<string>());
                ShowWarnings(new[]
                {
                    $"本地目录就是当前档案（{existing.Name}）的工作区：沿用请点「档案与维护」卡里的「重跑 Step 2」"
                    + "（复用工作区、新建执行会话，进度不丢）；想要一份全新档案请点「中止并清理」"
                    + "（注销工作区、删技能与 MCP、清空档案，本地目录与远端仓库不删），再回本步重新提交；"
                    + "要另建项目请改填其他目录后再点「下一步」。（若上部还有残留档案横幅，先点「保留并继续」解锁。）"
                });
                return;
            }

            _restartConfirmText.Text = $"已存在项目档案「{existing.Name}（{existing.Code}）」：重新提交会清空该档案，"
                                       + "并按当前表单的本地目录从 Step 2 重走——Step 3-6 的建库 / 探活 / 编译 / 推送"
                                       + "进度会重置（本地目录与云效上的远端仓库不会被删除）。确认要重来吗？";
            _restartConfirmBanner.IsVisible = true;
            return;
        }

        _restartConfirmed = false;
        _restartConfirmBanner.IsVisible = false;

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

            // 新档案从 Step 1 重新开始：丢弃回退游标，避免视图停在旧档案的步骤上
            _viewStep = null;

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

    /// <summary>Step 1 重提确认：确认后按「清空档案并重走 Step 2」继续。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnRestartConfirmClicked(object? sender, RoutedEventArgs e)
    {
        if (_busy || _unloaded)
            return;

        _restartConfirmed = true;
        await ProceedAsync(false).ConfigureAwait(true);
    }

    /// <summary>Step 1 重提确认的「取消」：收起横幅，不动档案。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnRestartCancelClicked(object? sender, RoutedEventArgs e)
    {
        _restartConfirmed = false;
        _restartConfirmBanner.IsVisible = false;
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
                // 档案已清空：丢弃回退游标与「清空档案重来」的确认态，视图回落到 Step 1
                _viewStep = null;
                _restartConfirmed = false;
                _restartConfirmBanner.IsVisible = false;
                SetBusy(false, null);
                Refresh();
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
        // 必须走 Refresh（而非只 SyncFormEnabled）：常驻「档案与维护」卡的可见性只在 SyncStepAreas 里算，
        // 处置残留时若只刷新表单开关，维护卡（重跑 Step 2 / 中止并清理）会永远不出现。
        Refresh();
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
        // 档案已清空：丢弃回退游标与「清空档案重来」的确认态
        _viewStep = null;
        _restartConfirmed = false;
        _restartConfirmBanner.IsVisible = false;
        Refresh();
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
                        .Select(end => $"{EndLabel(end.End)} {end.Error}")));
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

    /// <summary>Step 6「提交并推送」（PRD §4.3 Step 6）——与维护卡里的「重新推送（重跑 Step 6）」共用。</summary>
    private async void OnPushClicked(object? sender, RoutedEventArgs e) => await RunPushAsync();

    /// <summary>Step 4「开始探活」（PRD §4.3 Step 4，v0.22：只探活、不建库、不写文件）。</summary>
    private async void OnProbeClicked(object? sender, RoutedEventArgs e) => await RunProbeAsync();

    /// <summary>Step 5「开始编译」（PRD §4.3 Step 5）。</summary>
    private async void OnBuildClicked(object? sender, RoutedEventArgs e) => await RunBuildAsync();

    /// <summary>Step 7「创建流水线（逐端）」（PRD §4.3 Step 7 v0.30）。</summary>
    private async void OnPipelineClicked(object? sender, RoutedEventArgs e) => await RunCreatePipelinesAsync();

    /// <summary>Step 7「覆盖更新（逐端）」：按当前口径整体重推已有流水线（含部署段，破坏性、需先勾选确认）。</summary>
    private async void OnUpdatePipelinesClicked(object? sender, RoutedEventArgs e) => await RunUpdatePipelinesAsync();

    /// <summary>Step 7 YAML 预览切换端。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnPipelinePreviewChanged(object? sender, SelectionChangedEventArgs e)
        => ShowPipelineYaml(_pipelinePreviewPicker.SelectedItem as string);

    /// <summary>Step 7「编辑 YAML…」：弹窗手工微调选中端的提交原文。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnEditPipelineYamlClicked(object? sender, RoutedEventArgs e) => await EditPipelineYamlAsync();

    /// <summary>Step 7「重置为模板」：丢弃选中端的手工微调。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnResetPipelineYamlClicked(object? sender, RoutedEventArgs e)
    {
        if (_unloaded || _service.Current is null)
            return;

        var preview = CurrentPipelinePreview();
        if (preview is null || !_pipelineYamlOverrides.Remove(preview.End))
            return;

        ShowWarnings(Array.Empty<string>());
        PopulateStep7Area(_service.Current);
    }

    /// <summary>
    /// Step 7 手工微调入口：在独立窗口里编辑选中端 YAML，保存后回填内存字典并刷新预览。
    /// </summary>
    /// <remarks>
    /// 面板只负责传递文本：<b>不做任何 YAML 校验</b>（C10′），既不解析 YAML 也不猜云效规则；
    /// 明显改坏的文本由云效在创建时原样报回。
    /// </remarks>
    private async Task EditPipelineYamlAsync()
    {
        if (_busy || _unloaded || _service.Current is null)
            return;

        var preview = CurrentPipelinePreview();
        if (preview is null)
        {
            ShowWarnings(new[] { "当前没有可微调的流水线预览：请先在本步点『准备（只读）』生成预览后再微调（未建库 / 克隆的端需先完成 Step 3）。" });
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            ShowErrors(new[] { "当前宿主不是桌面窗口，无法打开 YAML 编辑器：请在 codex 桌面端里微调。" });
            return;
        }

        var dialog = new PipelineYamlEditorWindow(EndLabel(preview.End), preview.PipelineName, preview.Yaml);
        var edited = await dialog.ShowDialog<string?>(owner).ConfigureAwait(true);
        if (_unloaded || edited is null)
            return;

        // 与当前生效原文完全一致 ⇒ 视为未微调（避免「打开又保存」就把该端永久标成已微调）
        if (string.Equals(edited, preview.Yaml, StringComparison.Ordinal))
            _pipelineYamlOverrides.Remove(preview.End);
        else
            _pipelineYamlOverrides[preview.End] = edited;

        ShowErrors(Array.Empty<string>());
        ShowWarnings(Array.Empty<string>());

        if (_service.Current is { } spec)
            PopulateStep7Area(spec);
    }

    /// <summary>
    /// Step 7「准备预览（只读，不创建）」：下发一次只读准备，让助手拉主机组候选并按 docs 渲染各端 YAML，
    /// 产出结论文件后刷新复核界面（YAML 预览 / 待绑定清单 / 部署目标）。
    /// </summary>
    /// <remarks>
    /// <b>只读</b>：本阶段不创建 / 不更新云效流水线（写入动作在用户确认后的「创建流水线 / 覆盖更新」）。
    /// 界面据此渲染复核界面；部署目标缺选由执行入口自行阻断，准备阶段只负责把候选与预览摊开（C15′）。
    /// </remarks>
    private async void OnPreparePipelinesClicked(object? sender, RoutedEventArgs e)
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

        SetBusy(true, "正在准备 Step 7（只读：拉主机组候选 + 按 docs 渲染各端 YAML）…");
        try
        {
            var prepare = await _service.PreparePipelinesAsync().ConfigureAwait(true);
            if (_unloaded)
                return;

            _pipelinePrepareText.IsVisible = true;
            _pipelinePrepareText.Foreground = prepare.IsReady ? MutedText : DangerBrush;
            _pipelinePrepareText.Text = prepare.IsReady
                ? "准备就绪：" + prepare.Message
                : "准备未就绪：" + (prepare.Errors.Count > 0 ? string.Join("；", prepare.Errors) : prepare.Message);

            if (!prepare.IsReady)
                ShowErrors(prepare.Errors.Count > 0 ? prepare.Errors : new[] { prepare.Message });

            PopulateStep7Area(spec);
        }
        catch (Exception ex)
        {
            if (!_unloaded)
            {
                _pipelinePrepareText.IsVisible = true;
                _pipelinePrepareText.Foreground = DangerBrush;
                _pipelinePrepareText.Text = "Step 7 准备失败：" + ex.Message;
                ShowErrors(new[] { $"Step 7 准备失败：{ex.Message}" });
            }
        }
        finally
        {
            if (!_unloaded)
                SetBusy(false, null);
        }
    }

    /// <summary>取当前选中端对应的流水线预览（无预览时为 <c>null</c>）。</summary>
    /// <returns>预览；未命中返回 <c>null</c>。</returns>
    private PipelinePreview? CurrentPipelinePreview()
    {
        var label = _pipelinePreviewPicker.SelectedItem as string;
        return _pipelinePreviews.FirstOrDefault(item => EndLabel(item.End) == label);
    }

    /// <summary>
    /// Step 7 执行入口（创建流水线）：按准备结论的 YAML（<b>含部署段</b>）逐端创建。
    /// </summary>
    private async Task RunCreatePipelinesAsync()
    {
        if (_busy || _unloaded || _service.Current is null)
            return;

        if (BlockPipelineActionByDeployTarget())
            return;

        ShowErrors(Array.Empty<string>());
        SetBusy(true, "正在逐端创建云效流水线（准备结论原文（含部署段）→ 同名检索 → 创建）…");
        try
        {
            var result = await _service.CreatePipelinesAsync(_pipelineYamlOverrides, _deploySelections).ConfigureAwait(true);
            if (_unloaded)
                return;

            if (result.Status != StepStatus.Succeeded)
                ShowErrors(new[] { result.Message ?? "Step 7 未全部完成，详见右侧操作记录。" });
        }
        catch (Exception ex)
        {
            if (!_unloaded)
                ShowErrors(new[] { $"Step 7 执行失败：{ex.Message}" });
        }
        finally
        {
            if (!_unloaded)
                SetBusy(false, null);
        }
    }

    /// <summary>
    /// 创建 / 覆盖更新的<b>前置阻断</b>（v0.32）：需要部署阶段却没选到目标时给红字并拒绝执行。
    /// </summary>
    /// <returns>命中阻断返回 <c>true</c>（调用方直接返回，不发请求）。</returns>
    /// <remarks>
    /// 面板只是<b>早一步</b>拦下并把原因摊给用户；真正的守卫在能力层执行体里（那里同样 <c>fail</c> 且不发请求），
    /// 两条通路都会拦，避免「界面点得下去、结果把一个占位符 uuid 提交到云效」。
    /// 「没选但准备结论里有推荐目标」不算阻断——与 <c>PipelinePreview.RecommendedDeployTargetUuid</c> 的预选口径一致。
    /// </remarks>
    private bool BlockPipelineActionByDeployTarget()
    {
        var needsDeploy = _pipelinePreviews.Where(item => item.YamlIncludesDeployStage).ToList();
        if (needsDeploy.Count == 0)
            return false;

        if (_deployTargetsError is not null)
        {
            ShowErrors(new[]
            {
                "读不到云效主机组列表，已阻断执行：" + _deployTargetsError
                + " 请确认云效凭据 / 权限后点「刷新主机组」再执行。本步不做「手填 uuid」降级（uuid 填错云效只回「该机器组uuid[x]不存在」）。"
            });
            return true;
        }

        foreach (var preview in needsDeploy)
        {
            var chosen = _deploySelections.TryGetValue(preview.End, out var target) && !string.IsNullOrWhiteSpace(target.Uuid);
            if (chosen || !string.IsNullOrWhiteSpace(preview.RecommendedDeployTargetUuid))
                continue;

            ShowErrors(new[]
            {
                $"{EndLabel(preview.End)}尚未选定部署主机组：请在「部署目标」区点「刷新主机组」，"
                + "再从下拉里选一个（或到插件配置 pipelineDefaults[Service].deployMachineGroup 填默认值）后再执行本步。"
            });
            return true;
        }

        return false;
    }

    /// <summary>
    /// Step 7 补充入口（覆盖更新）：对该端已有流水线做整体覆盖更新，把<b>当前口径的完整 YAML（含部署段）</b>推上去。
    /// </summary>
    /// <remarks>
    /// 用途（v0.32）：把 v0.31 及更早建的「只含构建段、末尾带注释块」的流水线补成含部署段的完整 YAML，
    /// 或用户换了部署目标 / 改了配置后纠偏。破坏性，故卡内先勾选确认（同 Step 3 姿势，不弹模态框）。
    /// </remarks>
    private async Task RunUpdatePipelinesAsync()
    {
        if (_busy || _unloaded || _service.Current is null)
            return;

        if (BlockPipelineActionByDeployTarget())
            return;

        if (_updatePipelineConfirm.IsChecked != true)
        {
            ShowWarnings(new[]
            {
                "「覆盖更新」会对已有流水线做整体覆盖更新（UpdatePipeline）：若你已在云效控制台手工改过 YAML，"
                + "那些改动会被抹掉；v0.32 起还会把部署段一并写入（machineGroup 按当前「部署目标」的选择）。"
                + "确认后请先勾选按钮上方那句确认再执行。"
            });
            return;
        }

        ShowWarnings(Array.Empty<string>());
        SetBusy(true, "正在按当前口径整体覆盖更新已有流水线（UpdatePipeline，含部署段）…");
        try
        {
            var result = await _service.UpdatePipelinesAsync(_pipelineYamlOverrides, _deploySelections).ConfigureAwait(true);
            if (_unloaded)
                return;

            if (result.Status != StepStatus.Succeeded)
                ShowErrors(new[] { result.Message ?? "覆盖更新未完成，详见右侧操作记录。" });
        }
        catch (Exception ex)
        {
            if (!_unloaded)
                ShowErrors(new[] { $"覆盖更新失败：{ex.Message}" });
        }
        finally
        {
            if (!_unloaded)
                SetBusy(false, null);
        }
    }

    /// <summary>
    /// Step 4 执行入口：采集参数 → 交能力层驱动项目助手探活 → 呈现结论（<b>不阻断</b>流程）。
    /// </summary>
    private async Task RunProbeAsync()
    {
        if (_busy || _unloaded || _service.Current is null)
            return;

        var request = CollectProbeRequest();
        if (!request.TryValidate(out var invalid))
        {
            ShowErrors(new[] { invalid });
            return;
        }

        ShowErrors(Array.Empty<string>());
        ShowWarnings(Array.Empty<string>());
        SetBusy(true, "正在让项目助手执行数据库探活（只读：不建库、不写文件）…");
        try
        {
            var result = await _service.SetupDatabaseAsync(request).ConfigureAwait(true);
            if (_unloaded)
                return;

            // 口令用完即弃：既不留在内存，也不进任何记录
            _dbPasswordBox.Text = string.Empty;

            if (result.Status != StepStatus.Succeeded)
                ShowWarnings(new[] { result.Message ?? "探活未通过，详见右侧操作记录（本步不阻断流程）。" });
        }
        catch (Exception ex)
        {
            if (!_unloaded)
                ShowErrors(new[] { $"Step 4 执行失败：{ex.Message}" });
        }
        finally
        {
            if (!_unloaded)
                SetBusy(false, null);
        }
    }

    /// <summary>
    /// Step 5 执行入口：交能力层驱动项目助手逐端编译 → 呈现逐端结论（<b>失败阻断</b> Step 6）。
    /// </summary>
    private async Task RunBuildAsync()
    {
        if (_busy || _unloaded || _service.Current is null)
            return;

        ShowErrors(Array.Empty<string>());
        ShowWarnings(Array.Empty<string>());
        SetBusy(true, "正在让项目助手逐端编译（编译方式由助手按工程类型判断）…");
        try
        {
            var result = await _service.BuildAsync().ConfigureAwait(true);
            if (_unloaded)
                return;

            if (result.Status != StepStatus.Succeeded)
                ShowErrors(new[] { result.Message ?? "编译未全部通过，详见右侧操作记录。" });
        }
        catch (Exception ex)
        {
            if (!_unloaded)
                ShowErrors(new[] { $"Step 5 执行失败：{ex.Message}" });
        }
        finally
        {
            if (!_unloaded)
                SetBusy(false, null);
        }
    }

    /// <summary>
    /// 采集 Step 4 表单（口令只从内存取；端口留空按 3306）。
    /// </summary>
    /// <returns>探活参数。</returns>
    private DatabaseProbeRequest CollectProbeRequest()
    {
        var portText = _dbPortBox.Text?.Trim();
        var port = 3306;
        if (!string.IsNullOrEmpty(portText))
            port = int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;

        return new DatabaseProbeRequest
        {
            Host = _dbHostBox.Text?.Trim() ?? string.Empty,
            Port = port,
            Database = _dbNameBox.Text?.Trim() ?? string.Empty,
            User = _dbUserBox.Text?.Trim() ?? string.Empty,
            Password = _dbPasswordBox.Text ?? string.Empty
        };
    }

    /// <summary>Step 6 执行入口（Step 6 页与维护卡「重新推送」共用）。</summary>
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
            _conflictText.Text = $"{EndLabel(conflict.End)}目标目录已存在且非空：{conflict.LocalPath}（{conflict.EntryCount} 项）。"
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