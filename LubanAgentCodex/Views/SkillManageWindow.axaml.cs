/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： SkillManageWindow
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：工作区技能管理窗口，显示和管理工作区的 Skill 配置
*
*****************************************************************************/

namespace LubanAgentCodex.Views;

/// <summary>
/// 技能管理窗口
/// </summary>
public partial class SkillManageWindow : Window
{
    private DiContainer? _services;
    private WorkspaceInfo? _workspace;
    private ListBox? _skillList;

    /// <summary>
    /// 无参构造函数（Avalonia XAML 加载需要）
    /// </summary>
    public SkillManageWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 带参构造函数
    /// </summary>
    public SkillManageWindow(DiContainer services, WorkspaceInfo workspace) : this()
    {
        _services = services;
        _workspace = workspace;
        LoadSkills();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _skillList = this.FindControl<ListBox>("SkillList");
    }

    /// <summary>
    /// 加载技能列表
    /// </summary>
    private void LoadSkills()
    {
        if (_skillList == null || _services == null) return;

        try
        {
            var skillRegistry = _services.GetRequiredService<SkillRegistry>();
            var skills = skillRegistry.GetAll();

            _skillList.ItemsSource = skills.Select(s => new SkillItem
            {
                Id = s.Id,
                Name = s.Name ?? s.Id,
                Description = s.Description ?? "",
                IsEnabled = true,
            }).ToList();
        }
        catch (Exception ex)
        {
            _skillList.ItemsSource = new List<SkillItem>
            {
                new() { Name = "加载失败", Description = ex.Message }
            };
        }
    }
}

/// <summary>
/// 技能列表项
/// </summary>
public class SkillItem
{
    /// <summary>
    /// 技能唯一标识，同时用作技能目录名。
    /// </summary>
    public string Id { get; set; } = "";
    /// <summary>
    /// 技能显示名称。
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// 技能用途说明（取自技能元数据描述）。
    /// </summary>
    public string Description { get; set; } = "";
    /// <summary>
    /// 技能当前是否启用。
    /// </summary>
    public bool IsEnabled { get; set; }
}
