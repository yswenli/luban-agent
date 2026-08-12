/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： RuleCommand
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：Rule 命令 - 查看和管理规则 (list/add/update/delete/switch)
*
*****************************************************************************/
using LubanAgent.App;

namespace LubanAgent.Commands;

/// <summary>
/// Rule 命令 - 查看和管理规则 (list/add/update/delete/switch)
/// </summary>
public class RuleCommand : CommandBase
{
    private readonly RuleEngine _ruleEngine;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "rule";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "查看和管理规则（-list/-add/-update/-delete/-switch）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="ruleEngine">规则引擎</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public RuleCommand(ConfigManager configManager, IConfiguration configuration, RuleEngine ruleEngine,
        ITuiOutputWriter writer, ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
        _ruleEngine = ruleEngine;
    }

    /// <summary>
    /// 执行命令（无参数时显示帮助）
    /// </summary>
    public override Task ExecuteAsync()
    {
        Writer.WriteLine();
        Writer.WriteHeader("Rule 管理命令");
        Writer.WriteLine("  rule -list    - 列出所有规则");
        Writer.WriteLine("  rule -add     - 添加自定义规则");
        Writer.WriteLine("  rule -update  - 更新自定义规则");
        Writer.WriteLine("  rule -delete  - 删除自定义规则");
        Writer.WriteLine("  rule -switch  - 启用/禁用规则");
        Writer.WriteLine("  简写: /r -l, /r -a, /r -u, /r -d, /r -s");
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
                await ListRulesAsync(); return true;
            case "-add":
            case "add":
                await AddRuleAsync(); return true;
            case "-update":
            case "update":
                await UpdateRuleAsync(); return true;
            case "-delete":
            case "delete":
                await DeleteRuleAsync(); return true;
            case "-switch":
            case "switch":
                await SwitchRuleAsync(); return true;
            default:
                Writer.WriteLine($"未知子命令: {args[0]}");
                return true;
        }
    }

    /// <summary>
    /// 列出所有规则（内置 + 自定义）
    /// </summary>
    private Task ListRulesAsync()
    {
        var rules = _ruleEngine.GetAllRules();
        var customIds = new HashSet<string>(ConfigManager.CustomRules.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

        if (rules.Count == 0 && ConfigManager.CustomRules.Count == 0)
        {
            Writer.WriteInfo("暂无可用规则");
            return Task.CompletedTask;
        }

        var rows = new List<IReadOnlyList<string>>();

        foreach (var rule in rules)
        {
            var status = rule.IsEnabled ? "✅" : "❌";
            var isCustom = customIds.Contains(rule.Id);
            var tag = isCustom ? "自定义" : "";

            string detail;
            if (isCustom)
            {
                var cfg = ConfigManager.CustomRules.First(c => c.Id.Equals(rule.Id, StringComparison.OrdinalIgnoreCase));
                detail = $"ATP: {cfg.ActionTypePattern}  TP: {cfg.TargetPattern}  Action: {cfg.Action}";
            }
            else
            {
                detail = rule.Description;
            }

            rows.Add(new[]
            {
                $"{status} {rule.Id}",
                rule.Name,
                tag,
                $"优先级: {rule.Priority}",
                detail
            });
        }

        Ui.ShowTable("所有规则", new[] { "规则", "名称", "标记", "优先级", "详情" }, rows);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加自定义规则
    /// </summary>
    private Task AddRuleAsync()
    {
        var values = Ui.ShowForm("添加自定义规则", new[]
        {
            new FormField("规则 ID"),
            new FormField("规则名称"),
            new FormField("ActionTypePattern (默认 *)", Required: false, InitialValue: "*"),
            new FormField("TargetPattern (默认 *)", Required: false, InitialValue: "*"),
            new FormField("Action (allow/deny)", Required: false, InitialValue: "deny"),
            new FormField("优先级 (默认 100)", Required: false, InitialValue: "100")
        });
        if (values is null) return Task.CompletedTask;

        var id = values[0].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(id))
        {
            Writer.WriteError("ID 不能为空");
            return Task.CompletedTask;
        }

        if (_ruleEngine.GetRule(id) != null)
        {
            Writer.WriteError($"ID '{id}' 已存在");
            return Task.CompletedTask;
        }

        if (ConfigManager.CustomRules.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            Writer.WriteError($"ID '{id}' 已存在于自定义规则中");
            return Task.CompletedTask;
        }

        var name = values[1].Trim();
        if (string.IsNullOrEmpty(name))
        {
            Writer.WriteError("名称不能为空");
            return Task.CompletedTask;
        }

        var actionTypePattern = values[2].Trim();
        if (string.IsNullOrEmpty(actionTypePattern))
            actionTypePattern = "*";

        var targetPattern = values[3].Trim();
        if (string.IsNullOrEmpty(targetPattern))
            targetPattern = "*";

        var action = values[4].Trim().ToLower();
        if (string.IsNullOrEmpty(action))
            action = "deny";

        if (action != "allow" && action != "deny")
        {
            Writer.WriteError("Action 只能是 allow 或 deny");
            return Task.CompletedTask;
        }

        var priorityInput = values[5].Trim();
        var priority = 100;
        if (!string.IsNullOrEmpty(priorityInput) && !int.TryParse(priorityInput, out priority))
        {
            Writer.WriteError("优先级必须是整数");
            return Task.CompletedTask;
        }

        try
        {
            var cfg = new CustomRuleConfig
            {
                Id = id,
                Name = name,
                ActionTypePattern = actionTypePattern,
                TargetPattern = targetPattern,
                Action = action,
                Priority = priority,
                Enabled = true
            };

            ConfigManager.AddCustomRule(cfg);
            Writer.WriteSuccess($"自定义规则 '{name}' ({id}) 已添加");
        }
        catch (Exception ex)
        {
            Logger.Error("RuleCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 更新自定义规则
    /// </summary>
    private Task UpdateRuleAsync()
    {
        var customRules = ConfigManager.CustomRules;
        if (customRules.Count == 0)
        {
            Writer.WriteInfo("没有自定义规则可更新");
            return Task.CompletedTask;
        }

        var chosen = Ui.Choose("选择要更新的自定义规则",
            customRules.Select(r => $"{r.Name} ({r.Id}){(r.Enabled ? "" : " [已禁用]")}").ToList());
        if (chosen is null) return Task.CompletedTask;

        var selected = customRules[chosen.Value];

        var values = Ui.ShowForm($"更新 '{selected.Name}'（留空保持原值）", new[]
        {
            new FormField("名称", Required: false, InitialValue: selected.Name),
            new FormField("ActionTypePattern", Required: false, InitialValue: selected.ActionTypePattern),
            new FormField("TargetPattern", Required: false, InitialValue: selected.TargetPattern),
            new FormField("Action (allow/deny)", Required: false, InitialValue: selected.Action),
            new FormField("优先级", Required: false, InitialValue: selected.Priority.ToString())
        });
        if (values is null) return Task.CompletedTask;

        var newName = values[0].Trim();
        if (!string.IsNullOrEmpty(newName)) selected.Name = newName;

        var newActionType = values[1].Trim();
        if (!string.IsNullOrEmpty(newActionType)) selected.ActionTypePattern = newActionType;

        var newTarget = values[2].Trim();
        if (!string.IsNullOrEmpty(newTarget)) selected.TargetPattern = newTarget;

        var newAction = values[3].Trim().ToLower();
        if (!string.IsNullOrEmpty(newAction))
        {
            if (newAction != "allow" && newAction != "deny")
            {
                Writer.WriteError("Action 只能是 allow 或 deny");
                return Task.CompletedTask;
            }
            selected.Action = newAction;
        }

        var newPriorityInput = values[4].Trim();
        if (!string.IsNullOrEmpty(newPriorityInput))
        {
            if (!int.TryParse(newPriorityInput, out var newPriority))
            {
                Writer.WriteError("优先级必须是整数");
                return Task.CompletedTask;
            }
            selected.Priority = newPriority;
        }

        try
        {
            ConfigManager.UpdateCustomRule(selected);
            Writer.WriteSuccess($"规则 '{selected.Name}' 已更新");
        }
        catch (Exception ex)
        {
            Logger.Error("RuleCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 删除自定义规则
    /// </summary>
    private Task DeleteRuleAsync()
    {
        var customRules = ConfigManager.CustomRules;
        if (customRules.Count == 0)
        {
            Writer.WriteInfo("没有自定义规则可删除");
            return Task.CompletedTask;
        }

        var chosen = Ui.Choose("选择要删除的自定义规则",
            customRules.Select(r => $"{r.Name} ({r.Id})").ToList());
        if (chosen is null) return Task.CompletedTask;

        var targetId = customRules[chosen.Value].Id;

        if (!Ui.Confirm("删除规则", $"确定要删除规则 '{targetId}' 吗？", defaultValue: false))
        {
            Writer.WriteInfo("已取消");
            return Task.CompletedTask;
        }

        try
        {
            ConfigManager.RemoveCustomRule(targetId);
            Writer.WriteSuccess($"规则 '{targetId}' 已删除");
        }
        catch (Exception ex)
        {
            Logger.Error("RuleCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 启用/禁用规则
    /// </summary>
    private Task SwitchRuleAsync()
    {
        var allItems = new List<(string Id, string DisplayName, bool IsBuiltin, bool IsEnabled)>();

        foreach (var rule in _ruleEngine.GetAllRules())
        {
            var isCustom = ConfigManager.CustomRules.Any(c => c.Id == rule.Id);
            allItems.Add((rule.Id, rule.Name, !isCustom, rule.IsEnabled));
        }

        foreach (var id in ConfigManager.DisabledBuiltinRules)
        {
            if (!allItems.Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                allItems.Add((id, id, true, false));
            }
        }

        foreach (var cfg in ConfigManager.CustomRules.Where(c => !c.Enabled))
        {
            if (!allItems.Any(a => a.Id.Equals(cfg.Id, StringComparison.OrdinalIgnoreCase)))
            {
                allItems.Add((cfg.Id, cfg.Name, false, false));
            }
        }

        if (allItems.Count == 0)
        {
            Writer.WriteInfo("暂无规则可切换");
            return Task.CompletedTask;
        }

        var chosen = Ui.Choose("选择要启用/禁用的规则",
            allItems.Select(item =>
            {
                var status = item.IsEnabled ? "已启用" : "已禁用";
                var type = item.IsBuiltin ? "内置" : "自定义";
                return $"{item.DisplayName} ({item.Id}) [{type}] [{status}]";
            }).ToList());
        if (chosen is null) return Task.CompletedTask;

        var selected = allItems[chosen.Value];

        try
        {
            if (selected.IsBuiltin)
            {
                ConfigManager.SetBuiltinRuleEnabled(selected.Id, !selected.IsEnabled);
            }
            else
            {
                ConfigManager.SetCustomRuleEnabled(selected.Id, !selected.IsEnabled);
            }

            var newState = selected.IsEnabled ? "已禁用" : "已启用";
            Writer.WriteSuccess($"规则 '{selected.Id}' {newState}");
        }
        catch (Exception ex)
        {
            Logger.Error("RuleCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }
}
