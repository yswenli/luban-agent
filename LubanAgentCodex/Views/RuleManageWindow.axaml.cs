/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： RuleManageWindow
*版本号： V1.0.0.0
*唯一标识：规则管理窗口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：工作区规则管理窗口，显示和管理工作区的 Rule 配置
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LuBan.AIAgent.Rules;
using LubanAgentCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.Views;

/// <summary>
/// 规则管理窗口
/// </summary>
public partial class RuleManageWindow : Window
{
    private IServiceProvider? _services;
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
    public RuleManageWindow(IServiceProvider services, WorkspaceInfo workspace) : this()
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
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsEnabled { get; set; }
}
