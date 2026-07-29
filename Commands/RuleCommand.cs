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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Rules;
using Microsoft.Extensions.Configuration;

namespace LubanAgent.Commands;

/// <summary>
/// Rule 命令 - 查看和管理规则 (list/add/update/delete/switch)
/// </summary>
public class RuleCommand : CommandBase
{
    private readonly RuleEngine _ruleEngine;

    public override string Name => "rule";

    public override string Description => "查看和管理规则（-list/-add/-update/-delete/-switch）";

    public RuleCommand(ConfigManager configManager, IConfiguration configuration, RuleEngine ruleEngine)
        : base(configManager, configuration)
    {
        _ruleEngine = ruleEngine;
    }

    public override Task ExecuteAsync()
    {
        Console.WriteLine();
        Console.WriteLine("Rule 管理命令:");
        Console.WriteLine("  rule -list    - 列出所有规则");
        Console.WriteLine("  rule -add     - 添加自定义规则");
        Console.WriteLine("  rule -update  - 更新自定义规则");
        Console.WriteLine("  rule -delete  - 删除自定义规则");
        Console.WriteLine("  rule -switch  - 启用/禁用规则");
        Console.WriteLine("  简写: /r -l, /r -a, /r -u, /r -d, /r -s");
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
                Console.WriteLine($"未知子命令: {args[0]}");
                return true;
        }
    }

    private Task ListRulesAsync()
    {
        Console.WriteLine();

        var rules = _ruleEngine.GetAllRules();
        var customIds = new HashSet<string>(ConfigManager.CustomRules.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

        if (rules.Count == 0 && ConfigManager.CustomRules.Count == 0)
        {
            WriteInfo("暂无可用规则");
            return Task.CompletedTask;
        }

        foreach (var rule in rules)
        {
            var status = rule.IsEnabled ? "✅" : "❌";
            var isCustom = customIds.Contains(rule.Id);
            var tag = isCustom ? " [自定义]" : "";

            Console.WriteLine($"  {status} {rule.Id,-20} - {rule.Name}{tag}");
            Console.WriteLine($"     优先级: {rule.Priority}");

            if (isCustom)
            {
                var cfg = ConfigManager.CustomRules.First(c => c.Id.Equals(rule.Id, StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"     ActionTypePattern: {cfg.ActionTypePattern}  TargetPattern: {cfg.TargetPattern}  Action: {cfg.Action}");
            }
            else
            {
                Console.WriteLine($"     {rule.Description}");
            }

            Console.WriteLine();
        }

        return Task.CompletedTask;
    }

    private Task AddRuleAsync()
    {
        Console.WriteLine();
        Console.WriteLine("添加自定义规则:");
        Console.WriteLine();

        Console.Write("请输入规则 ID: ");
        var id = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            WriteError("ID 不能为空");
            return Task.CompletedTask;
        }

        id = id.ToLowerInvariant();

        if (_ruleEngine.GetRule(id) != null)
        {
            WriteError($"ID '{id}' 已存在");
            return Task.CompletedTask;
        }

        if (ConfigManager.CustomRules.Any(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            WriteError($"ID '{id}' 已存在于自定义规则中");
            return Task.CompletedTask;
        }

        Console.Write("请输入规则名称: ");
        var name = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            WriteError("名称不能为空");
            return Task.CompletedTask;
        }

        Console.Write("请输入 ActionTypePattern (默认 *): ");
        var actionTypePattern = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(actionTypePattern))
            actionTypePattern = "*";

        Console.Write("请输入 TargetPattern (默认 *): ");
        var targetPattern = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(targetPattern))
            targetPattern = "*";

        Console.Write("请输入 Action (allow/deny): ");
        var action = Console.ReadLine()?.Trim().ToLower();
        if (string.IsNullOrEmpty(action))
            action = "deny";

        if (action != "allow" && action != "deny")
        {
            WriteError("Action 只能是 allow 或 deny");
            return Task.CompletedTask;
        }

        Console.Write("请输入优先级 (默认 100): ");
        var priorityInput = Console.ReadLine()?.Trim();
        var priority = 100;
        if (!string.IsNullOrEmpty(priorityInput) && !int.TryParse(priorityInput, out priority))
        {
            WriteError("优先级必须是整数");
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
            WriteSuccess($"自定义规则 '{name}' ({id}) 已添加");
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    private Task UpdateRuleAsync()
    {
        Console.WriteLine();

        var customRules = ConfigManager.CustomRules;
        if (customRules.Count == 0)
        {
            WriteInfo("没有自定义规则可更新");
            return Task.CompletedTask;
        }

        Console.WriteLine("选择要更新的自定义规则:");
        for (int i = 0; i < customRules.Count; i++)
        {
            var r = customRules[i];
            var enabledTag = r.Enabled ? "" : " [已禁用]";
            Console.WriteLine($"  {i + 1}. {r.Name} ({r.Id}){enabledTag}");
        }

        Console.Write("请选择 (1-{0}) 或输入规则 ID: ", customRules.Count);
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            WriteError("无效输入");
            return Task.CompletedTask;
        }

        CustomRuleConfig? selected = null;

        if (int.TryParse(input, out var index) && index >= 1 && index <= customRules.Count)
        {
            selected = customRules[index - 1];
        }
        else
        {
            var lowerInput = input.ToLowerInvariant();
            if (ConfigManager.CustomRules.Any(c => c.Id.Equals(lowerInput, StringComparison.OrdinalIgnoreCase)))
            {
                selected = ConfigManager.CustomRules.First(c => c.Id.Equals(lowerInput, StringComparison.OrdinalIgnoreCase));
            }
            else if (_ruleEngine.GetRule(lowerInput) != null)
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

        Console.Write($"  ActionTypePattern [{selected.ActionTypePattern}]: ");
        var newActionType = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(newActionType)) selected.ActionTypePattern = newActionType;

        Console.Write($"  TargetPattern [{selected.TargetPattern}]: ");
        var newTarget = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(newTarget)) selected.TargetPattern = newTarget;

        Console.Write($"  Action [{selected.Action}] (allow/deny): ");
        var newAction = Console.ReadLine()?.Trim().ToLower();
        if (!string.IsNullOrEmpty(newAction))
        {
            if (newAction != "allow" && newAction != "deny")
            {
                WriteError("Action 只能是 allow 或 deny");
                return Task.CompletedTask;
            }
            selected.Action = newAction;
        }

        Console.Write($"  优先级 [{selected.Priority}]: ");
        var newPriorityInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(newPriorityInput))
        {
            if (!int.TryParse(newPriorityInput, out var newPriority))
            {
                WriteError("优先级必须是整数");
                return Task.CompletedTask;
            }
            selected.Priority = newPriority;
        }

        try
        {
            ConfigManager.UpdateCustomRule(selected);
            WriteSuccess($"规则 '{selected.Name}' 已更新");
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    private Task DeleteRuleAsync()
    {
        Console.WriteLine();

        var customRules = ConfigManager.CustomRules;
        if (customRules.Count == 0)
        {
            WriteInfo("没有自定义规则可删除");
            return Task.CompletedTask;
        }

        Console.WriteLine("选择要删除的自定义规则:");
        for (int i = 0; i < customRules.Count; i++)
        {
            var r = customRules[i];
            Console.WriteLine($"  {i + 1}. {r.Name} ({r.Id})");
        }

        Console.Write("请选择 (1-{0}) 或输入 ID: ", customRules.Count);
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            WriteError("无效输入");
            return Task.CompletedTask;
        }

        string? targetId = null;

        if (int.TryParse(input, out var index) && index >= 1 && index <= customRules.Count)
        {
            targetId = customRules[index - 1].Id;
        }
        else
        {
            var lowerInput = input.ToLowerInvariant();
            if (ConfigManager.CustomRules.Any(c => c.Id.Equals(lowerInput, StringComparison.OrdinalIgnoreCase)))
            {
                targetId = lowerInput;
            }
            else if (_ruleEngine.GetRule(lowerInput) != null)
            {
                WriteInfo("内置规则不可删除");
                return Task.CompletedTask;
            }
            else
            {
                WriteError("无效选择");
                return Task.CompletedTask;
            }
        }

        Console.Write($"确定要删除规则 '{targetId}' 吗？(y/N): ");
        var confirm = Console.ReadLine()?.Trim().ToLower();
        if (confirm != "y" && confirm != "yes")
        {
            Console.WriteLine("已取消");
            return Task.CompletedTask;
        }

        try
        {
            ConfigManager.RemoveCustomRule(targetId);
            WriteSuccess($"规则 '{targetId}' 已删除");
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    private Task SwitchRuleAsync()
    {
        Console.WriteLine();

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
            WriteInfo("暂无规则可切换");
            return Task.CompletedTask;
        }

        Console.WriteLine("选择要启用/禁用的规则:");
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

        if (!int.TryParse(choice, out var idx) || idx < 1 || idx > allItems.Count)
        {
            WriteError("无效选择");
            return Task.CompletedTask;
        }

        var selected = allItems[idx - 1];

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
            WriteSuccess($"规则 '{selected.Id}' {newState}");
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }
}
