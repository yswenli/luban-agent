/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： MCPManageWindow
*版本号： V1.0.0.0
*唯一标识：MCP 服务管理窗口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：工作区 MCP 服务管理窗口，显示和管理工作区的 MCP 配置
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LuBan.AIAgent.MCP;
using LubanAgentCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.Views;

/// <summary>
/// MCP 服务管理窗口
/// </summary>
public partial class MCPManageWindow : Window
{
    private IServiceProvider? _services;
    private WorkspaceInfo? _workspace;
    private ListBox? _mcpList;

    /// <summary>
    /// 无参构造函数（Avalonia XAML 加载需要）
    /// </summary>
    public MCPManageWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 带参构造函数
    /// </summary>
    public MCPManageWindow(IServiceProvider services, WorkspaceInfo workspace) : this()
    {
        _services = services;
        _workspace = workspace;
        LoadMCPClients();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _mcpList = this.FindControl<ListBox>("MCPList");
    }

    /// <summary>
    /// 加载 MCP 客户端列表
    /// </summary>
    private void LoadMCPClients()
    {
        if (_mcpList == null || _services == null) return;

        try
        {
            var mcpRegistry = _services.GetRequiredService<MCPRegistry>();
            var clients = mcpRegistry.GetAll();

            _mcpList.ItemsSource = clients.Select(c => new MCPItem
            {
                Name = c.Name ?? "",
                Description = c.Description ?? "",
                IsConnected = c.IsConnected,
            }).ToList();
        }
        catch (Exception ex)
        {
            _mcpList.ItemsSource = new List<MCPItem>
            {
                new() { Name = "加载失败", Description = ex.Message }
            };
        }
    }
}

/// <summary>
/// MCP 列表项
/// </summary>
public class MCPItem
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsConnected { get; set; }
}
