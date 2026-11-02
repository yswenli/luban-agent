/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： RuleManageWindow
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：工作区规则管理窗口，显示和管理工作区的 Rule 配置
*
*****************************************************************************/

namespace LubanAgentCodex.Views;

/// <summary>
/// 规则管理窗口
/// </summary>
public partial class RuleManageWindow : Window
{
    private DiContainer? _services;
    private WorkspaceInfo? _workspace;
    private ListBox? _ruleList;

    /// <summary>
    /// 无参构造函数（Avalonia XAML 加载需要）
    /// </summary>
    public RuleManageWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 带参构造函数
    /// </summary>
    public RuleManageWindow(DiContainer services, WorkspaceInfo workspace) : this()
    {
        _services = services;
        _workspace = workspace;
        LoadRules();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _ruleList = this.FindControl<ListBox>("RuleList");
    }

    /// <summary>
    /// 加载规则列表
    /// </summary>
    private void LoadRules()
    {
        if (_ruleList == null || _services == null) return;

        try
        {
            var ruleEngine = _services.GetRequiredService<RuleEngine>();
            var rules = ruleEngine.GetAllRules();

            _ruleList.ItemsSource = rules.Select(r => new RuleItem
            {
                Id = r.Id,
                Name = r.Name ?? r.Id,
                Description = r.Description ?? "",
                IsEnabled = true,
            }).ToList();
        }
        catch (Exception ex)
        {
            _ruleList.ItemsSource = new List<RuleItem>
            {
                new() { Name = "加载失败", Description = ex.Message }
            };
        }
    }
}

/// <summary>
/// 规则列表项
/// </summary>
public class RuleItem
{
    /// <summary>
    /// 规则唯一标识，同时用作规则文件名。
    /// </summary>
    public string Id { get; set; } = "";
    /// <summary>
    /// 规则显示名称。
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// 规则用途说明（取自规则元数据描述）。
    /// </summary>
    public string Description { get; set; } = "";
    /// <summary>
    /// 规则当前是否启用。
    /// </summary>
    public bool IsEnabled { get; set; }
}
