/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：Skill 命令 - 查看和执行 Skill (list/add/update/delete/switch)
*
*****************************************************************************/
using LubanAgent.App;

namespace LubanAgent.Commands;

/// <summary>
/// Skill 命令 - 查看和执行 Skill (list/add/update/delete/switch)
/// </summary>
public class SkillCommand : CommandBase
{
    private readonly SkillRegistry _skillRegistry;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "skill";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "查看和执行 Skill（-list/-add/-update/-delete/-switch）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="skillRegistry">Skill 注册表</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public SkillCommand(ConfigManager configManager, IConfiguration configuration, SkillRegistry skillRegistry,
        ITuiOutputWriter writer, ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
        _skillRegistry = skillRegistry;
    }

    /// <summary>
    /// 执行命令（无参数时显示帮助）
    /// </summary>
    public override Task ExecuteAsync()
    {
        Writer.WriteLine();
        Writer.WriteHeader("Skill 管理命令");
        Writer.WriteLine("  skill -list    - 列出所有 Skill");
        Writer.WriteLine("  skill -add     - 添加自定义 Skill");
        Writer.WriteLine("  skill -update  - 更新自定义 Skill");
        Writer.WriteLine("  skill -delete  - 删除自定义 Skill");
        Writer.WriteLine("  skill -switch  - 启用/禁用 Skill");
        Writer.WriteLine("  skill <id>     - 执行 Skill");
        Writer.WriteLine("  简写: /sk -l, /sk -a, /sk -u, /sk -d, /sk -s");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行带子命令的命令
    /// </summary>
    public override async Task<bool> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
            return false;

        switch (args[0].ToLower())
        {
            case "-list":
            case "list":
                await ListSkillsAsync(); return true;
            case "-add":
            case "add":
                await AddSkillAsync(); return true;
            case "-update":
            case "update":
                await UpdateSkillAsync(); return true;
            case "-delete":
            case "delete":
                await DeleteSkillAsync(); return true;
            case "-switch":
            case "switch":
                await SwitchSkillAsync(); return true;
            default:
                await ExecuteSkillAsync(args[0], args.Length > 1 ? string.Join(' ', args[1..]) : null);
                return true;
        }
    }

    /// <summary>
    /// 列出所有 Skill（按分类分组）
    /// </summary>
    private Task ListSkillsAsync()
    {
        var skills = _skillRegistry.GetAll();
        var customIds = new HashSet<string>(ConfigManager.CustomSkills.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        var disabledBuiltin = new HashSet<string>(ConfigManager.DisabledBuiltinSkills, StringComparer.OrdinalIgnoreCase);

        if (skills.Count == 0 && ConfigManager.CustomSkills.Count == 0 && disabledBuiltin.Count == 0)
        {
            Writer.WriteInfo("暂无可用 Skill");
            return Task.CompletedTask;
        }

        var rows = new List<IReadOnlyList<string>>();

        foreach (var category in skills.Select(s => s.Category).Distinct())
        {
            foreach (var skill in skills.Where(s => s.Category == category))
            {
                var tags = new List<string>();
                if (customIds.Contains(skill.Id))
                {
                    tags.Add("自定义");
                    var cfg = ConfigManager.CustomSkills.First(c => c.Id == skill.Id);
                    if (!cfg.Enabled) tags.Add("已禁用");
                }

                rows.Add(new[]
                {
                    category,
                    skill.Id,
                    skill.Name,
                    skill.Description,
                    string.Join("/", tags),
                    skill.Examples.Any() ? skill.Examples.First() : ""
                });
            }
        }

        foreach (var id in disabledBuiltin)
        {
            rows.Add(new[] { "已禁用的内置 Skill", id, "", "", "已禁用", "" });
        }

        foreach (var cfg in ConfigManager.CustomSkills.Where(c => !c.Enabled))
        {
            rows.Add(new[] { "已禁用的自定义 Skill", cfg.Id, cfg.Name, "", "自定义/已禁用", "" });
        }

        Ui.ShowTable("所有 Skill", new[] { "分类", "ID", "名称", "描述", "标记", "示例" }, rows);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加自定义 Skill
    /// </summary>
    private Task AddSkillAsync()
    {
        // 一次完整表单；ID 重复校验在表单返回后进行（失败即终止，与原行为一致）
        var values = Ui.ShowForm("添加自定义 Skill", new[]
        {
            new FormField("Skill ID"),
            new FormField("Skill 名称"),
            new FormField("Skill 描述", Required: false),
            new FormField("分类", Required: false, InitialValue: "custom"),
            new FormField("提示词模板", Multiline: true),
            new FormField("示例（可选，逗号分隔）", Required: false)
        });
        if (values is null) return Task.CompletedTask; // 用户取消

        var id = values[0].Trim().ToLowerInvariant();

        if (_skillRegistry.Get(id) != null)
        {
            Writer.WriteError($"ID '{id}' 已存在");
            return Task.CompletedTask;
        }

        if (ConfigManager.CustomSkills.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            Writer.WriteError($"ID '{id}' 已存在于自定义 Skill 中");
            return Task.CompletedTask;
        }

        var name = values[1].Trim();
        var description = values[2].Trim();

        var category = values[3].Trim();
        if (string.IsNullOrEmpty(category))
            category = "custom";

        var promptTemplate = values[4];
        if (string.IsNullOrEmpty(promptTemplate))
        {
            Writer.WriteError("提示词模板不能为空");
            return Task.CompletedTask;
        }

        var examplesInput = values[5].Trim();
        var examples = string.IsNullOrEmpty(examplesInput)
            ? new List<string>()
            : examplesInput.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToList();

        try
        {
            var cfg = new CustomSkillConfig
            {
                Id = id,
                Name = name,
                Description = description,
                Category = category,
                PromptTemplate = promptTemplate,
                Examples = examples,
                Enabled = true
            };

            ConfigManager.AddCustomSkill(cfg);
            Writer.WriteSuccess($"自定义 Skill '{name}' ({id}) 已添加");
        }
        catch (Exception ex)
        {
            Logger.Error("SkillCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 更新自定义 Skill
    /// </summary>
    private Task UpdateSkillAsync()
    {
        var customSkills = ConfigManager.CustomSkills;
        if (customSkills.Count == 0)
        {
            Writer.WriteInfo("没有自定义 Skill 可更新");
            return Task.CompletedTask;
        }

        // 编号菜单 → Choose 对话框
        var chosen = Ui.Choose("选择要更新的自定义 Skill",
            customSkills.Select(s => $"{s.Name} ({s.Id}){(s.Enabled ? "" : " [已禁用]")}").ToList());
        if (chosen is null) return Task.CompletedTask; // 用户取消

        var selected = customSkills[chosen.Value];

        // 留空保持原值：字段非必填，初始值为现值；模板留空保持不变
        var values = Ui.ShowForm($"更新 '{selected.Name}'（留空保持原值）", new[]
        {
            new FormField("名称", Required: false, InitialValue: selected.Name),
            new FormField("描述", Required: false, InitialValue: selected.Description),
            new FormField("分类", Required: false, InitialValue: selected.Category),
            new FormField($"提示词模板（当前长度 {selected.PromptTemplate.Length} 字符，留空保持不变）",
                Required: false, InitialValue: selected.PromptTemplate, Multiline: true)
        });
        if (values is null) return Task.CompletedTask; // 用户取消

        var newName = values[0].Trim();
        if (!string.IsNullOrEmpty(newName)) selected.Name = newName;

        var newDesc = values[1].Trim();
        if (!string.IsNullOrEmpty(newDesc)) selected.Description = newDesc;

        var newCategory = values[2].Trim();
        if (!string.IsNullOrEmpty(newCategory)) selected.Category = newCategory;

        var newTemplate = values[3];
        if (!string.IsNullOrEmpty(newTemplate)) selected.PromptTemplate = newTemplate;

        try
        {
            ConfigManager.UpdateCustomSkill(selected);
            Writer.WriteSuccess($"Skill '{selected.Name}' 已更新");
        }
        catch (Exception ex)
        {
            Logger.Error("SkillCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 删除自定义 Skill
    /// </summary>
    private Task DeleteSkillAsync()
    {
        var customSkills = ConfigManager.CustomSkills;
        if (customSkills.Count == 0)
        {
            Writer.WriteInfo("没有自定义 Skill 可删除");
            return Task.CompletedTask;
        }

        // 编号菜单 → Choose 对话框
        var chosen = Ui.Choose("选择要删除的自定义 Skill",
            customSkills.Select(s => $"{s.Name} ({s.Id})").ToList());
        if (chosen is null) return Task.CompletedTask; // 用户取消

        var targetId = customSkills[chosen.Value].Id;

        // 危险操作：默认"否"
        if (!Ui.Confirm("删除 Skill", $"确定要删除 Skill '{targetId}' 吗？", defaultValue: false))
        {
            Writer.WriteInfo("已取消");
            return Task.CompletedTask;
        }

        try
        {
            ConfigManager.RemoveCustomSkill(targetId);
            Writer.WriteSuccess($"Skill '{targetId}' 已删除");
        }
        catch (Exception ex)
        {
            Logger.Error("SkillCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 启用/禁用 Skill
    /// </summary>
    private Task SwitchSkillAsync()
    {
        var allItems = new List<(string Id, string DisplayName, bool IsBuiltin, bool IsEnabled)>();

        foreach (var skill in _skillRegistry.GetAll())
        {
            var isCustom = ConfigManager.CustomSkills.Any(c => c.Id == skill.Id);
            allItems.Add((skill.Id, skill.Name, !isCustom, true));
        }

        foreach (var id in ConfigManager.DisabledBuiltinSkills)
        {
            if (!allItems.Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                allItems.Add((id, id, true, false));
            }
        }

        foreach (var cfg in ConfigManager.CustomSkills.Where(c => !c.Enabled))
        {
            if (!allItems.Any(a => a.Id.Equals(cfg.Id, StringComparison.OrdinalIgnoreCase)))
            {
                allItems.Add((cfg.Id, cfg.Name, false, false));
            }
        }

        if (allItems.Count == 0)
        {
            Writer.WriteInfo("暂无 Skill 可切换");
            return Task.CompletedTask;
        }

        // 编号菜单 → Choose 对话框
        var chosen = Ui.Choose("选择要启用/禁用的 Skill",
            allItems.Select(item =>
            {
                var status = item.IsEnabled ? "已启用" : "已禁用";
                var type = item.IsBuiltin ? "内置" : "自定义";
                return $"{item.DisplayName} ({item.Id}) [{type}] [{status}]";
            }).ToList());
        if (chosen is null) return Task.CompletedTask; // 用户取消

        var selected = allItems[chosen.Value];

        try
        {
            if (selected.IsBuiltin)
            {
                ConfigManager.SetBuiltinSkillEnabled(selected.Id, !selected.IsEnabled);
            }
            else
            {
                ConfigManager.SetCustomSkillEnabled(selected.Id, !selected.IsEnabled);
            }

            var newState = selected.IsEnabled ? "已禁用" : "已启用";
            Writer.WriteSuccess($"Skill '{selected.Id}' {newState}");
        }
        catch (Exception ex)
        {
            Logger.Error("SkillCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行指定的 Skill
    /// </summary>
    /// <param name="skillId">Skill ID</param>
    /// <param name="input">用户输入</param>
    private async Task ExecuteSkillAsync(string skillId, string? input)
    {
        var skill = _skillRegistry.Get(skillId);
        if (skill == null)
        {
            Writer.WriteError($"未找到 Skill: {skillId}");
            return;
        }

        if (!ConfigManager.HasSelectedModel)
        {
            Writer.WriteError("请先使用 model switch 命令选择模型");
            return;
        }

        if (string.IsNullOrEmpty(input))
        {
            Writer.WriteLine();
            Writer.WriteHeader($"执行 Skill: {skill.Name}");
            Writer.WriteLine(skill.Description);
            Writer.WriteLine();

            if (skill.Examples.Any())
            {
                Writer.WriteLine("示例:");
                foreach (var example in skill.Examples)
                {
                    Writer.WriteLine($"  - {example}");
                }
                Writer.WriteLine();
            }

            var values = Ui.ShowForm($"执行 Skill: {skill.Name}", new[]
            {
                new FormField("请输入内容")
            });
            if (values is null)
            {
                Writer.WriteInfo("已取消执行");
                return;
            }

            input = values[0].Trim();
            if (string.IsNullOrEmpty(input))
            {
                Writer.WriteInfo("已取消执行");
                return;
            }
        }

        using var serviceProvider = BuildServiceProvider();

        try
        {
            var agentFactory = serviceProvider.GetRequiredService<ILuBanAgentFactory>();
            var agent = await agentFactory.CreateAsync(modelName: ConfigManager.SelectedModel);

            var context = new SkillContext
            {
                Agent = agent,
                ServiceProvider = serviceProvider,
                Log = msg => Writer.WriteInfo($"  {msg}"),
                UpdateStatus = SpinnerService.UpdateStatus
            };

            Writer.WriteLine();
            Writer.WriteLine($"执行 Skill: {skill.Name}");
            Writer.WriteLine();

            SpinnerService.Start($"正在执行 {skill.Name}...");
            try
            {
                var result = await skill.ExecuteAsync(context, input);

                if (result.Success)
                {
                    Writer.WriteLine();
                    Writer.WriteLine(result.Text);
                }
                else
                {
                    Writer.WriteError(result.Error ?? "执行失败");
                }
            }
            finally
            {
                SpinnerService.Stop();
            }

            Writer.WriteLine();
        }
        catch (Exception ex)
        {
            Logger.Error("SkillCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }
    }
}
