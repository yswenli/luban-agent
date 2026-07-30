/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： MCPCommand
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：MCP 命令 - 查看 MCP 客户端 (list/add/update/delete/switch/connect/tools)
*
*****************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.MCP;
using Microsoft.Extensions.Configuration;

namespace LubanAgent.Commands;

/// <summary>
/// MCP 命令 - 查看和管理 MCP 客户端 (list/add/update/delete/switch/connect/tools)
/// </summary>
public class MCPCommand : CommandBase
{
    private readonly MCPRegistry _mcpRegistry;

    public override string Name => "mcp";

    public override string Description => "查看 MCP 客户端（-list/-add/-update/-delete/-switch/-connect/-tools）";

    public MCPCommand(ConfigManager configManager, IConfiguration configuration, MCPRegistry mcpRegistry)
        : base(configManager, configuration)
    {
        _mcpRegistry = mcpRegistry;
    }

    public override Task ExecuteAsync()
    {
        Console.WriteLine();
        Console.WriteLine("MCP 管理命令:");
        Console.WriteLine("  mcp -list              - 列出所有 MCP 客户端");
        Console.WriteLine("  mcp -add               - 添加外部 MCP 服务器");
        Console.WriteLine("  mcp -update            - 更新外部 MCP 服务器");
        Console.WriteLine("  mcp -delete            - 删除外部 MCP 服务器");
        Console.WriteLine("  mcp -switch            - 启用/禁用 MCP 客户端");
        Console.WriteLine("  mcp -connect <name>    - 连接 MCP 客户端");
        Console.WriteLine("  mcp -tools <name>      - 查看客户端可用工具");
        Console.WriteLine("  简写: /mp -l, /mp -a, /mp -u, /mp -d, /mp -s, /mp -c, /mp -t");
        return Task.CompletedTask;
    }

    public override async Task<bool> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
            return false;

        var subCmd = args[0].ToLower();
        switch (subCmd)
        {
            case "-list":
            case "list":
                await ListClientsAsync(); return true;
            case "-add":
            case "add":
                await AddServerAsync(); return true;
            case "-update":
            case "update":
                await UpdateServerAsync(); return true;
            case "-delete":
            case "delete":
                await DeleteServerAsync(); return true;
            case "-switch":
            case "switch":
                await SwitchServerAsync(); return true;
            case "-connect":
            case "connect":
                if (args.Length > 1) { await ConnectAsync(args[1]); return true; }
                break;
            case "-tools":
            case "tools":
                if (args.Length > 1) { await ListToolsAsync(args[1]); return true; }
                break;
        }

        Console.WriteLine($"未知子命令或缺少参数: {string.Join(' ', args)}");
        return true;
    }

    private Task ListClientsAsync()
    {
        Console.WriteLine();

        var clients = _mcpRegistry.GetAll();
        var disabledExternal = ConfigManager.McpServers
            .Where(s => !s.Enabled)
            .ToList();

        var disabledBuiltin = ConfigManager.DisabledBuiltinMcpClients;

        if (clients.Count == 0 && disabledExternal.Count == 0 && disabledBuiltin.Count == 0)
        {
            WriteInfo("暂无 MCP 客户端");
            return Task.CompletedTask;
        }

        foreach (var client in clients)
        {
            var status = client.IsConnected ? "已连接" : "未连接";
            var type = _mcpRegistry.IsBuiltin(client.Name) ? "内置" : "外部";
            Console.WriteLine($"  [{status}] [{type}] {client.Name}");
            Console.WriteLine($"     {client.Description}");
            Console.WriteLine();
        }

        if (disabledExternal.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[已禁用的外部 MCP 服务器]");
            Console.ResetColor();
            foreach (var cfg in disabledExternal)
            {
                Console.WriteLine($"  [已禁用] [外部] {cfg.Name}");
                Console.WriteLine($"     {cfg.Description}");
                Console.WriteLine();
            }
        }

        if (disabledBuiltin.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[已禁用的内置 MCP 客户端]");
            Console.ResetColor();
            foreach (var name in disabledBuiltin)
            {
                Console.WriteLine($"  [已禁用] [内置] {name}");
                Console.WriteLine();
            }
        }

        return Task.CompletedTask;
    }

    private Task AddServerAsync()
    {
        Console.WriteLine();
        Console.WriteLine("添加外部 MCP 服务器:");
        Console.WriteLine();

        Console.Write("请输入服务器名称: ");
        var name = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            WriteError("名称不能为空");
            return Task.CompletedTask;
        }

        name = name.ToLowerInvariant();

        if (_mcpRegistry.IsBuiltin(name))
        {
            WriteError($"名称 '{name}' 与内置客户端冲突");
            return Task.CompletedTask;
        }

        if (ConfigManager.McpServers.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            WriteError($"名称 '{name}' 已存在");
            return Task.CompletedTask;
        }

        Console.Write("请输入描述: ");
        var description = Console.ReadLine()?.Trim() ?? "";

        Console.Write("请输入启动命令 (如 npx): ");
        var command = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(command))
        {
            WriteError("启动命令不能为空");
            return Task.CompletedTask;
        }

        Console.Write("请输入命令参数 (空格分隔，可选): ");
        var argsInput = Console.ReadLine()?.Trim() ?? "";
        var args = string.IsNullOrEmpty(argsInput)
            ? new List<string>()
            : argsInput.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        try
        {
            var cfg = new McpServerConfig
            {
                Name = name,
                Description = description,
                Command = command,
                Args = args,
                Enabled = true
            };

            ConfigManager.AddMcpServer(cfg);
            WriteSuccess($"外部 MCP 服务器 '{name}' 已添加");
            WriteInfo($"使用 /mcp connect {name} 连接");
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    private Task UpdateServerAsync()
    {
        Console.WriteLine();

        var externalServers = ConfigManager.McpServers;
        if (externalServers.Count == 0)
        {
            WriteInfo("没有外部 MCP 服务器可更新");
            return Task.CompletedTask;
        }

        Console.WriteLine("选择要更新的外部 MCP 服务器:");
        for (int i = 0; i < externalServers.Count; i++)
        {
            var s = externalServers[i];
            var enabledTag = s.Enabled ? "" : " [已禁用]";
            Console.WriteLine($"  {i + 1}. {s.Name}{enabledTag}");
        }

        Console.Write("请选择 (1-{0}) 或输入名称: ", externalServers.Count);
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            WriteError("无效输入");
            return Task.CompletedTask;
        }

        McpServerConfig? selected = null;

        if (int.TryParse(input, out var index) && index >= 1 && index <= externalServers.Count)
        {
            selected = externalServers[index - 1];
        }
        else
        {
            var lowerInput = input.ToLowerInvariant();
            selected = externalServers.FirstOrDefault(s => s.Name.Equals(lowerInput, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                WriteError("无效选择");
                return Task.CompletedTask;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"更新 '{selected.Name}' (留空保持原值):");

        Console.Write($"  描述 [{selected.Description}]: ");
        var newDesc = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(newDesc)) selected.Description = newDesc;

        Console.Write($"  启动命令 [{selected.Command}]: ");
        var newCommand = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(newCommand)) selected.Command = newCommand;

        Console.Write($"  命令参数 [{string.Join(' ', selected.Args)}]: ");
        var newArgsInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(newArgsInput))
        {
            selected.Args = newArgsInput.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        try
        {
            ConfigManager.UpdateMcpServer(selected);
            WriteSuccess($"MCP 服务器 '{selected.Name}' 已更新");
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    private async Task DeleteServerAsync()
    {
        Console.WriteLine();

        var externalServers = ConfigManager.McpServers;
        if (externalServers.Count == 0)
        {
            WriteInfo("没有外部 MCP 服务器可删除");
            return;
        }

        Console.WriteLine("选择要删除的外部 MCP 服务器:");
        for (int i = 0; i < externalServers.Count; i++)
        {
            var s = externalServers[i];
            Console.WriteLine($"  {i + 1}. {s.Name}");
        }

        Console.Write("请选择 (1-{0}) 或输入名称: ", externalServers.Count);
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            WriteError("无效输入");
            return;
        }

        string? targetName = null;

        if (int.TryParse(input, out var index) && index >= 1 && index <= externalServers.Count)
        {
            targetName = externalServers[index - 1].Name;
        }
        else
        {
            var lowerInput = input.ToLowerInvariant();
            if (externalServers.Any(s => s.Name.Equals(lowerInput, StringComparison.OrdinalIgnoreCase)))
            {
                targetName = lowerInput;
            }
            else
            {
                WriteError("无效选择");
                return;
            }
        }

        Console.Write($"确定要删除 MCP 服务器 '{targetName}' 吗？(y/N): ");
        var confirm = Console.ReadLine()?.Trim().ToLower();
        if (confirm != "y" && confirm != "yes")
        {
            Console.WriteLine("已取消");
            return;
        }

        var client = _mcpRegistry.Get(targetName);
        if (client != null && client.IsConnected)
        {
            try
            {
                await client.DisconnectAsync();
            }
            catch { }
        }

        try
        {
            ConfigManager.RemoveMcpServer(targetName);
            WriteSuccess($"MCP 服务器 '{targetName}' 已删除");
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            WriteError(ex.Message);
        }
    }

    private async Task SwitchServerAsync()
    {
        Console.WriteLine();

        var allItems = new List<(string Name, string DisplayName, bool IsBuiltin, bool IsEnabled)>();

        foreach (var client in _mcpRegistry.GetAll())
        {
            var isBuiltin = _mcpRegistry.IsBuiltin(client.Name);
            allItems.Add((client.Name, client.Name, isBuiltin, true));
        }

        foreach (var name in ConfigManager.DisabledBuiltinMcpClients)
        {
            if (!allItems.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                allItems.Add((name, name, true, false));
            }
        }

        foreach (var cfg in ConfigManager.McpServers.Where(s => !s.Enabled))
        {
            if (!allItems.Any(a => a.Name.Equals(cfg.Name, StringComparison.OrdinalIgnoreCase)))
            {
                allItems.Add((cfg.Name, cfg.Name, false, false));
            }
        }

        if (allItems.Count == 0)
        {
            WriteInfo("暂无 MCP 客户端可切换");
            return;
        }

        Console.WriteLine("选择要启用/禁用的 MCP 客户端:");
        for (int i = 0; i < allItems.Count; i++)
        {
            var item = allItems[i];
            var status = item.IsEnabled ? "已启用" : "已禁用";
            var type = item.IsBuiltin ? "内置" : "外部";
            Console.ForegroundColor = item.IsEnabled ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"  {i + 1}. {item.DisplayName} [{type}] [{status}]");
            Console.ResetColor();
        }

        Console.Write("请选择 (1-{0}): ", allItems.Count);
        var choice = Console.ReadLine()?.Trim();

        if (!int.TryParse(choice, out var idx) || idx < 1 || idx > allItems.Count)
        {
            WriteError("无效选择");
            return;
        }

        var selected = allItems[idx - 1];

        try
        {
            if (selected.IsBuiltin)
            {
                ConfigManager.SetBuiltinMcpClientEnabled(selected.Name, !selected.IsEnabled);
            }
            else
            {
                if (selected.IsEnabled)
                {
                    var client = _mcpRegistry.Get(selected.Name);
                    if (client != null && client.IsConnected)
                    {
                        try { await client.DisconnectAsync(); } catch { }
                    }
                }
                ConfigManager.SetMcpServerEnabled(selected.Name, !selected.IsEnabled);
            }

            var newState = selected.IsEnabled ? "已禁用" : "已启用";
            WriteSuccess($"MCP 客户端 '{selected.Name}' {newState}");
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            WriteError(ex.Message);
        }
    }

    private async Task ConnectAsync(string clientName)
    {
        if (string.IsNullOrEmpty(clientName))
        {
            WriteError("用法: mcp connect <name>");
            return;
        }

        var client = _mcpRegistry.Get(clientName);
        if (client == null)
        {
            WriteError($"未找到客户端: {clientName}");
            return;
        }

        Console.WriteLine($"正在连接 {clientName}...");
        var success = await client.ConnectAsync();
        if (success)
            WriteSuccess("连接成功");
        else
            WriteError("连接失败");
    }

    private async Task ListToolsAsync(string clientName)
    {
        if (string.IsNullOrEmpty(clientName))
        {
            WriteError("用法: mcp tools <name>");
            return;
        }

        var client = _mcpRegistry.Get(clientName);
        if (client == null)
        {
            WriteError($"未找到客户端: {clientName}");
            return;
        }

        if (!client.IsConnected)
        {
            WriteError($"客户端 {clientName} 未连接，请先连接");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"{clientName} 可用的工具：");
        Console.WriteLine();

        var tools = await client.ListToolsAsync();
        foreach (var tool in tools)
        {
            Console.WriteLine($"  - {tool.Name}: {tool.Description}");
        }
    }
}
