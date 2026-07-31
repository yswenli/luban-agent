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
namespace LubanAgent.Commands;

/// <summary>
/// Skill 命令 - 查看和执行 Skill (list/add/update/delete/switch)
/// </summary>
public class SkillCommand : CommandBase
{
    private readonly SkillRegistry _skillRegistry;

    public override string Name => "skill";

    public override string Description => "查看和执行 Skill（-list/-add/-update/-delete/-switch）";

    public SkillCommand(ConfigManager configManager, IConfiguration configuration, SkillRegistry skillRegistry)
        : base(configManager, configuration)
    {
        _skillRegistry = skillRegistry;
    }

    public override Task ExecuteAsync()
    {
        Console.WriteLine();
        Console.WriteLine("Skill 管理命令:");
        Console.WriteLine("  skill -list    - 列出所有 Skill");
        Console.WriteLine("  skill -add     - 添加自定义 Skill");
        Console.WriteLine("  skill -update  - 更新自定义 Skill");
        Console.WriteLine("  skill -delete  - 删除自定义 Skill");
        Console.WriteLine("  skill -switch  - 启用/禁用 Skill");
        Console.WriteLine("  skill <id>     - 执行 Skill");
        Console.WriteLine("  简写: /sk -l, /sk -a, /sk -u, /sk -d, /sk -s");
        return Task.CompletedTask;
    }

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

    private Task ListSkillsAsync()
    {
        var skills = _skillRegistry.GetAll();
        var customIds = new HashSet<string>(ConfigManager.CustomSkills.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
        var disabledBuiltin = new HashSet<string>(ConfigManager.DisabledBuiltinSkills, StringComparer.OrdinalIgnoreCase);

        if (skills.Count == 0 && ConfigManager.CustomSkills.Count == 0 && disabledBuiltin.Count == 0)
        {
            WriteInfo("暂无可用 Skill");
            return Task.CompletedTask;
        }

        var categories = skills.Select(s => s.Category).Distinct().ToList();

        try
        {
            Console.ForegroundColor = ConsoleColor.Green;

            Console.WriteLine();

            foreach (var category in categories)
            {
                Console.WriteLine($"[{category}]");

                foreach (var skill in skills.Where(s => s.Category == category))
                {
                    var tags = new List<string>();
                    if (customIds.Contains(skill.Id))
                    {
                        tags.Add("自定义");
                        var cfg = ConfigManager.CustomSkills.First(c => c.Id == skill.Id);
                        if (!cfg.Enabled) tags.Add("已禁用");
                    }
                    var tagStr = tags.Count > 0 ? $" [{string.Join("/", tags)}]" : "";

                    Console.WriteLine($"  {skill.Id,-20} - {skill.Name}{tagStr}");
                    Console.WriteLine($"  {"",-20}   {skill.Description}");

                    if (skill.Examples.Any())
                    {
                        Console.WriteLine($"  {"",-20}   示例: {skill.Examples.First()}");
                    }
                    Console.WriteLine();
                }
            }

            if (disabledBuiltin.Count > 0)
            {
                Console.WriteLine("[已禁用的内置 Skill]");
                foreach (var id in disabledBuiltin)
                {
                    Console.WriteLine($"  {id,-20} - [已禁用]");
                }
                Console.WriteLine();
            }

            var disabledCustom = ConfigManager.CustomSkills.Where(c => !c.Enabled).ToList();
            if (disabledCustom.Count > 0)
            {
                Console.WriteLine("[已禁用的自定义 Skill]");
                foreach (var cfg in disabledCustom)
                {
                    Console.WriteLine($"  {cfg.Id,-20} - {cfg.Name} [自定义/已禁用]");
                }
                Console.WriteLine();
            }
        }
        finally
        {
            Console.ResetColor();
        }

        return Task.CompletedTask;
    }

    private Task AddSkillAsync()
    {
        Console.WriteLine();
        Console.WriteLine("添加自定义 Skill:");
        Console.WriteLine();

        Console.Write("请输入 Skill ID: ");
        var id = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            WriteError("ID 不能为空");
            return Task.CompletedTask;
        }

        id = id.ToLowerInvariant();

        if (_skillRegistry.Get(id) != null)
        {
            WriteError($"ID '{id}' 已存在");
            return Task.CompletedTask;
        }

        if (ConfigManager.CustomSkills.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            WriteError($"ID '{id}' 已存在于自定义 Skill 中");
            return Task.CompletedTask;
        }

        Console.Write("请输入 Skill 名称: ");
        var name = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            WriteError("名称不能为空");
            return Task.CompletedTask;
        }

        Console.Write("请输入 Skill 描述: ");
        var description = Console.ReadLine()?.Trim() ?? "";

        Console.Write("请输入分类 (默认 custom): ");
        var category = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(category))
            category = "custom";

        Console.WriteLine("请输入提示词模板（多行输入，单独一行 '.' 结束）:");
        var templateBuilder = new StringBuilder();
        while (true)
        {
            var line = Console.ReadLine();
            if (line == ".") break;
            if (templateBuilder.Length > 0) templateBuilder.AppendLine();
            templateBuilder.Append(line);
        }

        var promptTemplate = templateBuilder.ToString();
        if (string.IsNullOrEmpty(promptTemplate))
        {
            WriteError("提示词模板不能为空");
            return Task.CompletedTask;
        }

        Console.Write("请输入示例（可选，逗号分隔）: ");
        var examplesInput = Console.ReadLine()?.Trim();
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
            WriteSuccess($"自定义 Skill '{name}' ({id}) 已添加");
        }
        catch (Exception ex)
        {
            Logger.Error("SkillCommand 操作异常", ex);
            WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    private Task UpdateSkillAsync()
    {
        Console.WriteLine();

        var customSkills = ConfigManager.CustomSkills;
        if (customSkills.Count == 0)
        {
            WriteInfo("没有自定义 Skill 可更新");
            return Task.CompletedTask;
        }

        Console.WriteLine("选择要更新的自定义 Skill:");
        for (int i = 0; i < customSkills.Count; i++)
        {
            var s = customSkills[i];
            var enabledTag = s.Enabled ? "" : " [已禁用]";
            Console.WriteLine($"  {i + 1}. {s.Name} ({s.Id}){enabledTag}");
        }

        Console.Write("请选择 (1-{0}) 或输入 Skill ID: ", customSkills.Count);
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            WriteError("无效输入");
            return Task.CompletedTask;
        }

        CustomSkillConfig? selected = null;

        if (int.TryParse(input, out var index) && index >= 1 && index <= customSkills.Count)
        {
            selected = customSkills[index - 1];
        }
        else
        {
            var lowerInput = input.ToLowerInvariant();
            if (ConfigManager.CustomSkills.Any(c => c.Id.Equals(lowerInput, StringComparison.OrdinalIgnoreCase)))
            {
                selected = ConfigManager.CustomSkills.First(c => c.Id.Equals(lowerInput, StringComparison.OrdinalIgnoreCase));
            }
            else if (_skillRegistry.Get(lowerInput) != null)
            {
                WriteInfo("内置组件不可修改，可用 switch 禁用");
                return Task.CompletedTask;
            }
            else
            {
                WriteError("无效选择");
                return Task.CompletedTask;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"更新 '{selected.Name}' (留空保持原值):");

        Console.Write($"  名称 [{selected.Name}]: ");
        var newName = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(newName)) selected.Name = newName;

        Console.Write($"  描述 [{selected.Description}]: ");
        var newDesc = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(newDesc)) selected.Description = newDesc;

        Console.Write($"  分类 [{selected.Category}]: ");
        var newCategory = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(newCategory)) selected.Category = newCategory;

        Console.WriteLine($"  提示词模板 (当前长度 {selected.PromptTemplate.Length} 字符，留空保持不变):");
        var templateInput = Console.ReadLine();
        if (!string.IsNullOrEmpty(templateInput))
        {
            var templateBuilder = new StringBuilder(templateInput);
            while (true)
            {
                var line = Console.ReadLine();
                if (line == ".") break;
                templateBuilder.AppendLine();
                templateBuilder.Append(line);
            }
            selected.PromptTemplate = templateBuilder.ToString();
        }

        try
        {
            ConfigManager.UpdateCustomSkill(selected);
            WriteSuccess($"Skill '{selected.Name}' 已更新");
        }
        catch (Exception ex)
        {
            Logger.Error("SkillCommand 操作异常", ex);
            WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    private Task DeleteSkillAsync()
    {
        Console.WriteLine();

        var customSkills = ConfigManager.CustomSkills;
        if (customSkills.Count == 0)
        {
            WriteInfo("没有自定义 Skill 可删除");
            return Task.CompletedTask;
        }

        Console.WriteLine("选择要删除的自定义 Skill:");
        for (int i = 0; i < customSkills.Count; i++)
        {
            var s = customSkills[i];
            Console.WriteLine($"  {i + 1}. {s.Name} ({s.Id})");
        }

        Console.Write("请选择 (1-{0}) 或输入 ID: ", customSkills.Count);
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            WriteError("无效输入");
            return Task.CompletedTask;
        }

        string? targetId = null;

        if (int.TryParse(input, out var index) && index >= 1 && index <= customSkills.Count)
        {
            targetId = customSkills[index - 1].Id;
        }
        else
        {
            var lowerInput = input.ToLowerInvariant();
            if (ConfigManager.CustomSkills.Any(c => c.Id.Equals(lowerInput, StringComparison.OrdinalIgnoreCase)))
            {
                targetId = lowerInput;
            }
            else if (_skillRegistry.Get(lowerInput) != null)
            {
                WriteInfo("内置 Skill 不可删除");
                return Task.CompletedTask;
            }
            else
            {
                WriteError("无效选择");
                return Task.CompletedTask;
            }
        }

        Console.Write($"确定要删除 Skill '{targetId}' 吗？(y/N): ");
        var confirm = Console.ReadLine()?.Trim().ToLower();
        if (confirm != "y" && confirm != "yes")
        {
            Console.WriteLine("已取消");
            return Task.CompletedTask;
        }

        try
        {
            ConfigManager.RemoveCustomSkill(targetId);
            WriteSuccess($"Skill '{targetId}' 已删除");
        }
        catch (Exception ex)
        {
            Logger.Error("SkillCommand 操作异常", ex);
            WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    private Task SwitchSkillAsync()
    {
        Console.WriteLine();

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
            WriteInfo("暂无 Skill 可切换");
            return Task.CompletedTask;
        }

        Console.WriteLine("选择要启用/禁用的 Skill:");
        for (int i = 0; i < allItems.Count; i++)
        {
            var item = allItems[i];
            var status = item.IsEnabled ? "已启用" : "已禁用";
            var type = item.IsBuiltin ? "内置" : "自定义";
            Console.ForegroundColor = item.IsEnabled ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"  {i + 1}. {item.DisplayName} ({item.Id}) [{type}] [{status}]");
            Console.ResetColor();
        }

        Console.Write("请选择 (1-{0}): ", allItems.Count);
        var choice = Console.ReadLine()?.Trim();

        if (!int.TryParse(choice, out var index) || index < 1 || index > allItems.Count)
        {
            WriteError("无效选择");
            return Task.CompletedTask;
        }

        var selected = allItems[index - 1];

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
            WriteSuccess($"Skill '{selected.Id}' {newState}");
        }
        catch (Exception ex)
        {
            Logger.Error("SkillCommand 操作异常", ex);
            WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    private async Task ExecuteSkillAsync(string skillId, string? input)
    {
        var skill = _skillRegistry.Get(skillId);
        if (skill == null)
        {
            WriteError($"未找到 Skill: {skillId}");
            return;
        }

        if (!ConfigManager.HasSelectedModel)
        {
            WriteError("请先使用 model switch 命令选择模型");
            return;
        }

        if (string.IsNullOrEmpty(input))
        {
            Console.WriteLine();
            Console.WriteLine($"执行 Skill: {skill.Name}");
            Console.WriteLine(skill.Description);
            Console.WriteLine();

            if (skill.Examples.Any())
            {
                Console.WriteLine("示例:");
                foreach (var example in skill.Examples)
                {
                    Console.WriteLine($"  - {example}");
                }
                Console.WriteLine();
            }

            Console.Write("请输入内容: ");
            input = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("已取消执行");
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
                Log = msg => Console.WriteLine($"  {msg}"),
                UpdateStatus = status => Console.Title = $"LuBan Agent - {status}"
            };

            Console.WriteLine();
            Console.WriteLine($"执行 Skill: {skill.Name}");
            Console.WriteLine();

            await ConsoleUtil.RunWithStatusAsync(async updateStatus =>
            {
                context.UpdateStatus = updateStatus;
                var result = await skill.ExecuteAsync(context, input);

                if (result.Success)
                {
                    Console.WriteLine();
                    Console.WriteLine(result.Text);
                }
                else
                {
                    WriteError(result.Error ?? "执行失败");
                }
            }, $"正在执行 {skill.Name}...", "cyan");

            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Logger.Error("SkillCommand 操作异常", ex);
            WriteError(ex.Message);
        }
    }
}
