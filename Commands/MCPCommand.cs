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
using LubanAgent.App;

namespace LubanAgent.Commands;

/// <summary>
/// MCP 命令 - 查看和管理 MCP 客户端 (list/add/update/delete/switch/connect/tools)
/// </summary>
public class MCPCommand : CommandBase
{
    private readonly MCPRegistry _mcpRegistry;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "mcp";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "查看 MCP 客户端（-list/-add/-update/-delete/-switch/-connect/-tools）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="mcpRegistry">MCP 注册表</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public MCPCommand(ConfigManager configManager, IConfiguration configuration, MCPRegistry mcpRegistry,
        ITuiOutputWriter writer, ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
        _mcpRegistry = mcpRegistry;
    }

    /// <summary>
    /// 执行命令（无参数时显示帮助）
    /// </summary>
    public override Task ExecuteAsync()
    {
        Writer.WriteLine();
        Writer.WriteHeader("MCP 管理命令");
        Writer.WriteLine("  mcp -list              - 列出所有 MCP 客户端");
        Writer.WriteLine("  mcp -add               - 添加外部 MCP 服务器");
        Writer.WriteLine("  mcp -update            - 更新外部 MCP 服务器");
        Writer.WriteLine("  mcp -delete            - 删除外部 MCP 服务器");
        Writer.WriteLine("  mcp -switch            - 启用/禁用 MCP 客户端");
        Writer.WriteLine("  mcp -connect <name>    - 连接 MCP 客户端");
        Writer.WriteLine("  mcp -tools <name>      - 查看客户端可用工具");
        Writer.WriteLine("  简写: /mp -l, /mp -a, /mp -u, /mp -d, /mp -s, /mp -c, /mp -t");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行带子命令的命令
    /// </summary>
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

        Writer.WriteLine($"未知子命令或缺少参数: {string.Join(' ', args)}");
        return true;
    }

    /// <summary>
    /// 列出所有 MCP 客户端（已连接、已禁用）
    /// </summary>
    private Task ListClientsAsync()
    {
        var clients = _mcpRegistry.GetAll();
        var disabledExternal = ConfigManager.McpServers
            .Where(s => !s.Enabled)
            .ToList();

        var disabledBuiltin = ConfigManager.DisabledBuiltinMcpClients;

        if (clients.Count == 0 && disabledExternal.Count == 0 && disabledBuiltin.Count == 0)
        {
            Writer.WriteInfo("暂无 MCP 客户端");
            return Task.CompletedTask;
        }

        var rows = new List<IReadOnlyList<string>>();

        foreach (var client in clients)
        {
            var status = client.IsConnected ? "已连接" : "未连接";
            var type = _mcpRegistry.IsBuiltin(client.Name) ? "内置" : "外部";
            rows.Add(new[] { $"[{status}]", $"[{type}]", client.Name, client.Description });
        }

        if (disabledExternal.Count > 0)
        {
            foreach (var cfg in disabledExternal)
            {
                rows.Add(new[] { "[已禁用]", "[外部]", cfg.Name, cfg.Description });
            }
        }

        if (disabledBuiltin.Count > 0)
        {
            foreach (var name in disabledBuiltin)
            {
                rows.Add(new[] { "[已禁用]", "[内置]", name, "" });
            }
        }

        Ui.ShowTable("MCP 客户端", new[] { "状态", "类型", "名称", "描述" }, rows);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加外部 MCP 服务器
    /// </summary>
    private Task AddServerAsync()
    {
        var values = Ui.ShowForm("添加外部 MCP 服务器", new[]
        {
            new FormField("服务器名称"),
            new FormField("描述", Required: false),
            new FormField("启动命令 (如 npx)"),
            new FormField("命令参数 (空格分隔，可选)", Required: false)
        });
        if (values is null) return Task.CompletedTask;

        var name = values[0].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name))
        {
            Writer.WriteError("名称不能为空");
            return Task.CompletedTask;
        }

        if (_mcpRegistry.IsBuiltin(name))
        {
            Writer.WriteError($"名称 '{name}' 与内置客户端冲突");
            return Task.CompletedTask;
        }

        if (ConfigManager.McpServers.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Writer.WriteError($"名称 '{name}' 已存在");
            return Task.CompletedTask;
        }

        var description = values[1].Trim() ?? "";

        var command = values[2].Trim();
        if (string.IsNullOrEmpty(command))
        {
            Writer.WriteError("启动命令不能为空");
            return Task.CompletedTask;
        }

        var argsInput = values[3].Trim() ?? "";
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
            Writer.WriteSuccess($"外部 MCP 服务器 '{name}' 已添加");
            Writer.WriteInfo($"使用 /mcp connect {name} 连接");
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 更新外部 MCP 服务器配置
    /// </summary>
    private Task UpdateServerAsync()
    {
        var externalServers = ConfigManager.McpServers;
        if (externalServers.Count == 0)
        {
            Writer.WriteInfo("没有外部 MCP 服务器可更新");
            return Task.CompletedTask;
        }

        var chosen = Ui.Choose("选择要更新的外部 MCP 服务器",
            externalServers.Select(s => $"{s.Name}{(s.Enabled ? "" : " [已禁用]")}").ToList());
        if (chosen is null) return Task.CompletedTask;

        var selected = externalServers[chosen.Value];

        var values = Ui.ShowForm($"更新 '{selected.Name}'（留空保持原值）", new[]
        {
            new FormField("描述", Required: false, InitialValue: selected.Description),
            new FormField("启动命令", Required: false, InitialValue: selected.Command),
            new FormField("命令参数", Required: false, InitialValue: string.Join(' ', selected.Args))
        });
        if (values is null) return Task.CompletedTask;

        var newDesc = values[0].Trim();
        if (!string.IsNullOrEmpty(newDesc)) selected.Description = newDesc;

        var newCommand = values[1].Trim();
        if (!string.IsNullOrEmpty(newCommand)) selected.Command = newCommand;

        var newArgsInput = values[2].Trim();
        if (!string.IsNullOrEmpty(newArgsInput))
        {
            selected.Args = newArgsInput.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        try
        {
            ConfigManager.UpdateMcpServer(selected);
            Writer.WriteSuccess($"MCP 服务器 '{selected.Name}' 已更新");
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 删除外部 MCP 服务器
    /// </summary>
    private async Task DeleteServerAsync()
    {
        var externalServers = ConfigManager.McpServers;
        if (externalServers.Count == 0)
        {
            Writer.WriteInfo("没有外部 MCP 服务器可删除");
            return;
        }

        var chosen = Ui.Choose("选择要删除的外部 MCP 服务器",
            externalServers.Select(s => s.Name).ToList());
        if (chosen is null) return;

        var targetName = externalServers[chosen.Value].Name;

        if (!Ui.Confirm("删除 MCP 服务器", $"确定要删除 MCP 服务器 '{targetName}' 吗？", defaultValue: false))
        {
            Writer.WriteInfo("已取消");
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
            Writer.WriteSuccess($"MCP 服务器 '{targetName}' 已删除");
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }
    }

    /// <summary>
    /// 启用/禁用 MCP 客户端
    /// </summary>
    private async Task SwitchServerAsync()
    {
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
            Writer.WriteInfo("暂无 MCP 客户端可切换");
            return;
        }

        var chosen = Ui.Choose("选择要启用/禁用的 MCP 客户端",
            allItems.Select(item =>
            {
                var status = item.IsEnabled ? "已启用" : "已禁用";
                var type = item.IsBuiltin ? "内置" : "外部";
                return $"{item.DisplayName} [{type}] [{status}]";
            }).ToList());
        if (chosen is null) return;

        var selected = allItems[chosen.Value];

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
            Writer.WriteSuccess($"MCP 客户端 '{selected.Name}' {newState}");
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }
    }

    /// <summary>
    /// 连接指定的 MCP 客户端
    /// </summary>
    /// <param name="clientName">客户端名称</param>
    private async Task ConnectAsync(string clientName)
    {
        if (string.IsNullOrEmpty(clientName))
        {
            Writer.WriteError("用法: mcp connect <name>");
            return;
        }

        var client = _mcpRegistry.Get(clientName);
        if (client == null)
        {
            Writer.WriteError($"未找到客户端: {clientName}");
            return;
        }

        Writer.WriteLine($"正在连接 {clientName}...");
        var success = await client.ConnectAsync();
        if (success)
            Writer.WriteSuccess("连接成功");
        else
            Writer.WriteError("连接失败");
    }

    /// <summary>
    /// 列出指定客户端的可用工具
    /// </summary>
    /// <param name="clientName">客户端名称</param>
    private async Task ListToolsAsync(string clientName)
    {
        if (string.IsNullOrEmpty(clientName))
        {
            Writer.WriteError("用法: mcp tools <name>");
            return;
        }

        var client = _mcpRegistry.Get(clientName);
        if (client == null)
        {
            Writer.WriteError($"未找到客户端: {clientName}");
            return;
        }

        if (!client.IsConnected)
        {
            Writer.WriteError($"客户端 {clientName} 未连接，请先连接");
            return;
        }

        Writer.WriteLine();
        Writer.WriteHeader($"{clientName} 可用的工具：");
        Writer.WriteLine();

        var tools = await client.ListToolsAsync();
        foreach (var tool in tools)
        {
            Writer.WriteLine($"  - {tool.Name}: {tool.Description}");
        }
    }
}
