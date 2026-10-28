/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.ViewModels.Settings
*文件名： SkillRuleTabViewModel
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗「技能 / 规则」页业务（列表、读取、保存、新建、删除），由管理服务承载文件语义
*
*****************************************************************************/

namespace LubanAgentCodex.ViewModels.Settings;

/// <summary>
/// 技能 / 规则页 ViewModel：只做校验与转发，所有提示文案与原设置窗保持一致。
/// </summary>
/// <param name="services">宿主服务提供者（为 null 时视为容器不可用，操作返回失败文案）。</param>
public sealed class SkillRuleTabViewModel(IServiceProvider? services)
{
    private ISkillManagementService? Skills => services?.GetService<ISkillManagementService>();

    private IRuleManagementService? Rules => services?.GetService<IRuleManagementService>();

    /// <summary>
    /// 列出当前作用域下的条目键。
    /// </summary>
    /// <param name="kind">技能或规则。</param>
    /// <param name="scopeRoot">作用区根目录（null=用户级全局）。</param>
    public IReadOnlyList<string> ListFileIds(SettingsTabKind kind, string? scopeRoot)
        => kind == SettingsTabKind.Skill
            ? Skills?.ListFileIds(scopeRoot).ToList() ?? []
            : Rules?.ListFileIds(scopeRoot).ToList() ?? [];

    /// <summary>条目是否已存在。</summary>
    /// <param name="kind">技能或规则。</param>
    /// <param name="name">条目键。</param>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public bool Exists(SettingsTabKind kind, string name, string? scopeRoot)
        => ListFileIds(kind, scopeRoot).Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>读取技能文件；不存在返回 null。</summary>
    /// <param name="key">技能目录名。</param>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public SkillFileContent? ReadSkill(string key, string? scopeRoot)
        => Skills?.ReadFile(key, scopeRoot);

    /// <summary>读取规则文件；不存在返回 null。</summary>
    /// <param name="key">规则 Id（文件名去后缀）。</param>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public CustomRuleConfig? ReadRule(string key, string? scopeRoot)
        => Rules?.ReadFile(key, scopeRoot);

    /// <summary>
    /// 保存技能（含必填校验）。
    /// </summary>
    /// <param name="oldKey">原条目键；新建时为 null。</param>
    /// <param name="form">表单内容。</param>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public SettingsOutcome SaveSkill(string? oldKey, SkillForm form, string? scopeRoot)
    {
        var name = form.Name.Trim();
        var description = form.Description.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
            return SettingsOutcome.Failure("技能的 name 与 description 不能为空。");

        try
        {
            (Skills ?? throw new InvalidOperationException("Skill 管理服务不可用"))
                .SaveFile(oldKey, new SkillFileContent(
                    name, description, form.Category.Trim(), form.Triggers.Trim(), form.Body), scopeRoot);
        }
        catch (Exception ex)
        {
            return SettingsOutcome.Failure($"保存失败：{ex.Message}");
        }

        return SettingsOutcome.Success(key: name);
    }

    /// <summary>
    /// 保存规则（含 Id 必填与优先级解析）。
    /// </summary>
    /// <param name="oldKey">原条目键；新建时为 null。</param>
    /// <param name="form">表单内容。</param>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public SettingsOutcome SaveRule(string? oldKey, RuleForm form, string? scopeRoot)
    {
        var id = form.Id.Trim();
        if (string.IsNullOrWhiteSpace(id))
            return SettingsOutcome.Failure("规则的 Id 不能为空。");

        _ = int.TryParse(form.PriorityText.Trim(), out var priority);

        var cfg = new CustomRuleConfig
        {
            Id = id,
            Name = form.Name.Trim(),
            Description = form.Description.Trim(),
            ActionTypePattern = form.ActionTypePattern.Trim(),
            TargetPattern = form.TargetPattern.Trim(),
            Action = form.Action.Trim(),
            Priority = priority,
            Enabled = form.Enabled,
            Content = form.Content,
        };

        try
        {
            (Rules ?? throw new InvalidOperationException("规则管理服务不可用"))
                .SaveFile(oldKey, cfg, scopeRoot);
        }
        catch (Exception ex)
        {
            return SettingsOutcome.Failure($"保存失败：{ex.Message}");
        }

        return SettingsOutcome.Success(key: id);
    }

    /// <summary>
    /// 新建一条默认技能 / 规则（自动避让重名）。
    /// </summary>
    /// <param name="kind">技能或规则。</param>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public SettingsOutcome CreateDefault(SettingsTabKind kind, string? scopeRoot)
    {
        var baseName = kind == SettingsTabKind.Skill ? "new-skill" : "new-rule";
        var name = baseName;
        var i = 1;
        while (Exists(kind, name, scopeRoot)) name = $"{baseName}-{i++}";

        try
        {
            if (kind == SettingsTabKind.Skill)
            {
                (Skills ?? throw new InvalidOperationException("Skill 管理服务不可用"))
                    .SaveFile(null, new SkillFileContent(name, "", "custom", "", ""), scopeRoot);
            }
            else
            {
                (Rules ?? throw new InvalidOperationException("规则管理服务不可用"))
                    .SaveFile(null, new CustomRuleConfig { Id = name, Name = name }, scopeRoot);
            }
        }
        catch (Exception ex)
        {
            return SettingsOutcome.Failure($"新建失败：{ex.Message}");
        }

        return SettingsOutcome.Success("已新建条目，请填写内容后点击「保存」。", name);
    }

    /// <summary>
    /// 删除技能 / 规则（文件来源）。
    /// </summary>
    /// <param name="kind">技能或规则。</param>
    /// <param name="key">条目键。</param>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public async Task<SettingsOutcome> DeleteAsync(SettingsTabKind kind, string key, string? scopeRoot)
    {
        try
        {
            if (kind == SettingsTabKind.Skill)
            {
                await (Skills ?? throw new InvalidOperationException("Skill 管理服务不可用"))
                    .DeleteAsync(key, ManagedOrigin.File, scopeRoot);
            }
            else
            {
                await (Rules ?? throw new InvalidOperationException("规则管理服务不可用"))
                    .DeleteAsync(key, ManagedOrigin.File, scopeRoot);
            }
        }
        catch (Exception ex)
        {
            return SettingsOutcome.Failure($"删除失败：{ex.Message}");
        }

        return SettingsOutcome.Success("已删除该条目。");
    }
}