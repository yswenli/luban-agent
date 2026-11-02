/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.ViewModels.Settings
*文件名： SettingsWindowViewModel
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗壳 ViewModel（作用域 / 当前页签 / 选中项）与五个分页 ViewModel 的组合根
*
*****************************************************************************/

namespace LubanAgentCodex.ViewModels.Settings;

/// <summary>
/// 设置窗壳 ViewModel：只持有「作用域 + 当前页签 + 选中项」这三类状态，
/// 具体读写全部委派给分页 ViewModel，不含任何 Avalonia 类型。
/// </summary>
/// <param name="services">宿主服务提供者（为 null 时各分页按「容器不可用」降级）。</param>
public sealed class SettingsWindowViewModel(DiContainer? services)
{
    private readonly List<WorkspaceInfo> _workspaces = new();

    /// <summary>技能 / 规则页。</summary>
    public SkillRuleTabViewModel SkillRule { get; } = new(services);

    /// <summary>MCP 服务页。</summary>
    public McpTabViewModel Mcp { get; } = new(services);

    /// <summary>供应商 / 模型页。</summary>
    public ProviderModelTabViewModel ProviderModel { get; } = new(services);

    /// <summary>A2A 协作页。</summary>
    public A2aTabViewModel A2a { get; } = new();

    /// <summary>云效连接页。</summary>
    public YunxiaoTabViewModel Yunxiao { get; } = new(services);

    /// <summary>顶栏可选的工作区（已排除 LLM Wiki 知识库，按名称排序）。</summary>
    public IReadOnlyList<WorkspaceInfo> Workspaces => _workspaces;

    /// <summary>当前作用域工作区；null 表示「★ 全局」。</summary>
    public WorkspaceInfo? SelectedWorkspace { get; set; }

    /// <summary>当前页签。</summary>
    public SettingsTabKind Tab { get; set; } = SettingsTabKind.Skill;

    /// <summary>
    /// 当前选中条目键：技能=目录名，规则/MCP=文件名（去 .json），供应商=Name，
    /// 模型=<c>provider:model</c>，A2A=文件名，云效固定 <c>yunxiao</c>。
    /// </summary>
    public string? SelectedItemKey { get; set; }

    /// <summary>是否处于全局作用域。</summary>
    public bool IsGlobal => SelectedWorkspace == null;

    /// <summary>作用区根目录：null 表示用户级（全局）<c>~/.luban-agent</c>。</summary>
    public string? ScopeRoot => IsGlobal ? null : SelectedWorkspace?.RootPath;

    /// <summary>
    /// 加载工作区列表；预选工作区不在列表内（已删除或为 LLM Wiki 类型）时回落到「★ 全局」。
    /// </summary>
    /// <param name="preferred">预选工作区。</param>
    public async Task LoadWorkspacesAsync(WorkspaceInfo? preferred)
    {
        SelectedWorkspace = preferred;

        try
        {
            var manager = services!.GetRequiredService<IWorkspaceManager>();
            var all = await manager.GetUserWorkspacesAsync();

            _workspaces.Clear();
            // LLM Wiki 知识库工作区不纳入设置窗的工作区列表
            _workspaces.AddRange(all
                .Where(w => w.Type != WorkspaceTypes.LlmWiki)
                .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase));

            if (SelectedWorkspace != null &&
                !_workspaces.Any(w => w.WorkspaceId == SelectedWorkspace.WorkspaceId))
                SelectedWorkspace = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载工作区列表失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 列出「技能 / 规则 / MCP」页的条目键（其它页签返回空）。
    /// </summary>
    public IReadOnlyList<string> EnumerateFileItems() => Tab switch
    {
        SettingsTabKind.Skill => SkillRule.ListFileIds(SettingsTabKind.Skill, ScopeRoot),
        SettingsTabKind.Rule => SkillRule.ListFileIds(SettingsTabKind.Rule, ScopeRoot),
        SettingsTabKind.Mcp => Mcp.ListFileIds(ScopeRoot),
        _ => [],
    };
}