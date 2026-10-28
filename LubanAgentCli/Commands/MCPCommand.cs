/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.Commands
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
namespace LubanAgentCli.Commands;

/// <summary>
/// MCP 命令 - 查看和管理 MCP 客户端 (list/add/update/delete/switch/connect/tools)
/// </summary>
/// <remarks>
/// 所有读写都经 <see cref="IMcpManagementService"/>；命令层只保留交互编排与面向用户的文案，
/// 读取原始配置（<see cref="ConfigManager.McpServers"/>）仅用于填充表单初值（沿用 /skill、/rule 口径）。
/// </remarks>
public class MCPCommand : CommandBase
{
    private readonly IMcpManagementService _mcpManagement;

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
    /// <param name="mcpManagement">MCP 管理服务</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public MCPCommand(ConfigManager configManager, IConfiguration configuration,
        IMcpManagementService mcpManagement, ITuiOutputWriter writer, ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
        _mcpManagement = mcpManagement ?? throw new ArgumentNullException(nameof(mcpManagement));
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
                await ConnectAsync(args.Length > 1 ? args[1] : null); return true;
            case "-tools":
            case "tools":
                await ListToolsAsync(args.Length > 1 ? args[1] : null); return true;
        }

        Writer.WriteLine($"未知子命令: {string.Join(' ', args)}");
        return true;
    }

    /// <summary>
    /// 列出所有 MCP 客户端（已连接、已禁用）
    /// </summary>
    private async Task ListClientsAsync()
    {
        var items = await _mcpManagement.ListAsync();

        if (items.Count == 0)
        {
            Writer.WriteInfo("暂无 MCP 客户端");
            return;
        }

        var rows = items.Select(item =>
        {
            var status = item.IsEnabled ? (item.IsConnected ? "已连接" : "未连接") : "已禁用";
            var type = item.IsBuiltin ? "内置" : "外部";
            return (IReadOnlyList<string>)new[] { $"[{status}]", $"[{type}]", item.Name, item.Description };
        }).ToList();

        Ui.ShowTable("MCP 客户端", new[] { "状态", "类型", "名称", "描述" }, rows);
    }

    /// <summary>
    /// 添加外部 MCP 服务器
    /// </summary>
    private async Task AddServerAsync()
    {
        var values = Ui.ShowForm("添加外部 MCP 服务器", new[]
        {
            new FormField("服务器名称"),
            new FormField("描述", Required: false),
            new FormField("启动命令 (如 npx)"),
            new FormField("命令参数（一行一个，可含空格）", Required: false, Multiline: true)
        });
        if (values is null) return;

        var name = values[0].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name))
        {
            Writer.WriteError("名称不能为空");
            return;
        }

        var all = await _mcpManagement.ListAsync();
        if (all.Any(i => i.IsBuiltin && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Writer.WriteError($"名称 '{name}' 与内置客户端冲突");
            return;
        }

        if (ConfigManager.McpServers.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Writer.WriteError($"名称 '{name}' 已存在");
            return;
        }

        var description = values[1].Trim() ?? "";

        var command = values[2].Trim();
        if (string.IsNullOrEmpty(command))
        {
            Writer.WriteError("启动命令不能为空");
            return;
        }

        var argsInput = values[3].Trim() ?? "";
        var args = McpArgsParser.Parse(argsInput);

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

            await _mcpManagement.AddAsync(cfg);
            Writer.WriteSuccess($"外部 MCP 服务器 '{name}' 已添加");
            Writer.WriteInfo($"使用 /mcp connect {name} 连接");
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }
    }

    /// <summary>
    /// 更新外部 MCP 服务器配置
    /// </summary>
    private async Task UpdateServerAsync()
    {
        var externalServers = ConfigManager.McpServers;
        if (externalServers.Count == 0)
        {
            Writer.WriteInfo("没有外部 MCP 服务器可更新");
            return;
        }

        var chosen = Ui.Choose("选择要更新的外部 MCP 服务器",
            externalServers.Select(s => $"{s.Name}{(s.Enabled ? "" : " [已禁用]")}").ToList());
        if (chosen is null) return;

        var selected = externalServers[chosen.Value];

        var values = Ui.ShowForm($"更新 '{selected.Name}'（留空保持原值）", new[]
        {
            new FormField("描述", Required: false, InitialValue: selected.Description),
            new FormField("启动命令", Required: false, InitialValue: selected.Command),
            new FormField("命令参数（一行一个，可含空格）", Required: false, InitialValue: McpArgsParser.Format(selected.Args), Multiline: true)
        });
        if (values is null) return;

        var newDesc = values[0].Trim();
        if (!string.IsNullOrEmpty(newDesc)) selected.Description = newDesc;

        var newCommand = values[1].Trim();
        if (!string.IsNullOrEmpty(newCommand)) selected.Command = newCommand;

        var newArgsInput = values[2].Trim();
        if (!string.IsNullOrEmpty(newArgsInput))
        {
            selected.Args = McpArgsParser.Parse(newArgsInput);
        }

        try
        {
            await _mcpManagement.UpdateAsync(selected);
            Writer.WriteSuccess($"MCP 服务器 '{selected.Name}' 已更新");
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }
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

        try
        {
            await _mcpManagement.DeleteAsync(targetName, ManagedOrigin.Config);
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
        var allItems = await _mcpManagement.ListAsync();

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
                return $"{item.Name} [{type}] [{status}]";
            }).ToList());
        if (chosen is null) return;

        var selected = allItems[chosen.Value];

        try
        {
            await _mcpManagement.SetEnabledAsync(selected.Name, !selected.IsEnabled);

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
    private async Task ConnectAsync(string? clientName)
    {
        var items = await _mcpManagement.ListAsync();

        if (string.IsNullOrEmpty(clientName))
        {
            var candidates = items.Where(i => i.IsEnabled).ToList();
            if (candidates.Count == 0)
            {
                Writer.WriteError("暂无 MCP 客户端");
                return;
            }

            var chosen = Ui.Choose("选择要连接的 MCP 客户端",
                candidates.Select(c => $"{c.Name} - {c.Description}").ToList());
            if (chosen is null) return;

            clientName = candidates[chosen.Value].Name;
        }

        if (items.All(i => !i.IsEnabled || !i.Name.Equals(clientName, StringComparison.OrdinalIgnoreCase)))
        {
            Writer.WriteError($"未找到客户端: {clientName}");
            return;
        }

        try
        {
            Writer.WriteLine($"正在连接 {clientName}...");
            var success = await _mcpManagement.ConnectAsync(clientName);
            if (success)
                Writer.WriteSuccess("连接成功");
            else
                Writer.WriteError("连接失败");
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }
    }

    /// <summary>
    /// 列出指定客户端的可用工具
    /// </summary>
    /// <param name="clientName">客户端名称</param>
    private async Task ListToolsAsync(string? clientName)
    {
        var items = await _mcpManagement.ListAsync();

        if (string.IsNullOrEmpty(clientName))
        {
            var candidates = items.Where(i => i.IsEnabled).ToList();
            if (candidates.Count == 0)
            {
                Writer.WriteError("暂无 MCP 客户端");
                return;
            }

            var chosen = Ui.Choose("选择要查看工具的 MCP 客户端",
                candidates.Select(c => $"{c.Name} - {c.Description}").ToList());
            if (chosen is null) return;

            clientName = candidates[chosen.Value].Name;
        }

        var entry = items.FirstOrDefault(i => i.IsEnabled && i.Name.Equals(clientName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            Writer.WriteError($"未找到客户端: {clientName}");
            return;
        }

        if (!entry.IsConnected)
        {
            Writer.WriteError($"客户端 {clientName} 未连接，请先连接");
            return;
        }

        Writer.WriteLine();
        Writer.WriteHeader($"{clientName} 可用的工具：");
        Writer.WriteLine();

        try
        {
            var tools = await _mcpManagement.ListToolsAsync(clientName);
            foreach (var tool in tools)
            {
                Writer.WriteLine($"  - {tool.Name}: {tool.Description}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("MCPCommand 操作异常", ex);
            Writer.WriteError(ex.Message);
        }
    }
}