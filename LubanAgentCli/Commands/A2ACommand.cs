/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.Commands
*文件名： A2ACommand
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/9
*描述：A2A 协作管理命令（-list/-server/-client/-start/-stop），
*管理当前工作区 .luban-agent/a2a/*.json 配置文件与运行时启停。
*
*****************************************************************************/
using LuBan.AIAgent.A2A;
using LuBan.AIAgent.A2A.Abstractions;

using LubanAgentA2A.Configuration;
using LubanAgentA2A.Runtime;

namespace LubanAgentCli.Commands;

/// <summary>
/// A2A 协作管理命令。视图/管理视角：
/// <list type="bullet">
/// <item><c>/a2a</c> 或 <c>-list</c> 显示状态、服务端与客户端概览</item>
/// <item><c>-server</c> 查看或首次创建 <c>server.json</c></item>
/// <item><c>-client</c> 列出/新增/删除 <c>client.&lt;name&gt;.json</c></item>
/// <item><c>-start</c>/<c>-stop</c> 运行时启停</item>
/// </list>
/// </summary>
public class A2ACommand : CommandBase
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IA2ALifecycle _lifecycle;
    private readonly A2ARuntimeState _state;
    private readonly IA2AServerHost _serverHost;
    private readonly IOptions<A2AOptions> _baseline;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "a2a";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "A2A 协作管理（-list/-server/-client/-start/-stop）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="workspaceManager">工作区管理器</param>
    /// <param name="lifecycle">A2A 运行时生命周期</param>
    /// <param name="state">A2A 运行时状态</param>
    /// <param name="serverHost">A2A 服务端宿主</param>
    /// <param name="baseline">appsettings 的 A2A 基线（只读，用于显示总开关）</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    public A2ACommand(
        ConfigManager configManager,
        IConfiguration configuration,
        IWorkspaceManager workspaceManager,
        IA2ALifecycle lifecycle,
        A2ARuntimeState state,
        IA2AServerHost serverHost,
        IOptions<A2AOptions> baseline,
        ITuiOutputWriter writer,
        ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
        _workspaceManager = workspaceManager;
        _lifecycle = lifecycle;
        _state = state;
        _serverHost = serverHost;
        _baseline = baseline;
    }

    /// <summary>
    /// 执行命令（无参数时等同 -list）
    /// </summary>
    public override Task ExecuteAsync()
    {
        ListStatus();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行带子命令的命令
    /// </summary>
    public override async Task<bool> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            ListStatus();
            return true;
        }

        var sub = args[0].ToLowerInvariant();
        switch (sub)
        {
            case "-list":
            case "-l":
                ListStatus();
                break;
            case "-server":
            case "-s":
                ShowOrCreateServer();
                break;
            case "-client":
            case "-c":
                HandleClient(args);
                break;
            case "-start":
                await StartA2AAsync();
                break;
            case "-stop":
                await StopA2AAsync();
                break;
            default:
                Writer.WriteLine($"未知子命令: {sub}");
                ShowHelp();
                break;
        }
        return true;
    }

    /// <summary>
    /// 当前工作区根路径。
    /// </summary>
    private string? WorkspaceRoot => _workspaceManager.CurrentWorkspace?.RootPath;

    /// <summary>
    /// 显示 A2A 状态与服务端/客户端概览。
    /// </summary>
    private void ListStatus()
    {
        var root = WorkspaceRoot;
        var enabled = _baseline.Value.Enabled;

        Writer.WriteHeader("A2A 协作状态");
        Writer.WriteInfo($"  总开关(Enabled): {(enabled ? "已启用" : "已禁用")}");
        Writer.WriteInfo($"  运行状态: {(_lifecycle.IsRunning ? "运行中" : "未启动")}");
        Writer.WriteInfo($"  本地 Agent 名: {_state.LocalAgentName}");
        var endpoint = _serverHost.Endpoint;
        Writer.WriteInfo($"  服务端端点: {(string.IsNullOrWhiteSpace(endpoint) ? "-" : endpoint)}");
        Writer.WriteInfo($"  工作区: {(string.IsNullOrWhiteSpace(root) ? "(未选择)" : root)}");

        if (string.IsNullOrWhiteSpace(root))
        {
            Writer.WriteWarning("当前未选择工作区，无法读取 .luban-agent/a2a/*.json");
            return;
        }

        if (!enabled)
        {
            Writer.WriteWarning("A2A 未启用：需在 appsettings.json 的 LuBanAgent:Agent2Agent:Enabled 设为 true 后，工作区配置才会生效");
        }

        Writer.WriteLine();
        Writer.WriteHeader("服务端 (server.json)");
        var server = A2AWorkspaceConfigStore.LoadServer(root);
        if (server is null)
        {
            Writer.WriteInfo("  未配置（可执行 /a2a -server 创建）");
        }
        else
        {
            WriteServerFields(server);
        }

        Writer.WriteLine();
        ListClients(root);
    }

    /// <summary>
    /// 查看服务端配置；不存在则创建默认配置。
    /// </summary>
    private void ShowOrCreateServer()
    {
        var root = RequireWorkspace();
        if (root is null) return;

        if (A2AWorkspaceConfigStore.ServerExists(root))
        {
            var server = A2AWorkspaceConfigStore.LoadServer(root);
            Writer.WriteHeader("服务端配置 (server.json)");
            if (server is null)
            {
                Writer.WriteWarning("server.json 无法解析，请检查文件内容");
                return;
            }
            WriteServerFields(server);
            return;
        }

        var apiKey = Guid.NewGuid().ToString("N");
        var file = new A2AWorkspaceFile
        {
            Host = "127.0.0.1",
            Port = 0,
            Path = "/a2a",
            RequireApiKey = true,
            ApiKeys = new List<string> { apiKey },
            AgentName = "luban-agent",
            AgentVersion = "1.0.0",
        };
        A2AWorkspaceConfigStore.SaveServer(root, file);

        Writer.WriteSuccess($"已创建服务端配置: {Path.Combine(A2AWorkspaceConfigStore.ResolveDir(root), A2AWorkspaceConfigStore.ServerFileName)}");
        Writer.WriteInfo($"  API Key: {apiKey}");
        Writer.WriteWarning("API Key 为明文，请勿提交到版本库；建议将 .luban-agent/a2a/ 加入 .gitignore");
        Writer.WriteInfo("执行 /a2a -stop 再 /a2a -start（或重启）后服务端角色生效");
    }

    /// <summary>
    /// 处理客户端子命令。
    /// </summary>
    /// <param name="args">完整参数（args[0] = -client）。</param>
    private void HandleClient(string[] args)
    {
        var root = RequireWorkspace();
        if (root is null) return;

        if (args.Length < 2)
        {
            ListClients(root);
            return;
        }

        var action = args[1].ToLowerInvariant();
        switch (action)
        {
            case "-add":
            case "-a":
                AddClient(root, args);
                break;
            case "-delete":
            case "-del":
            case "-d":
                DeleteClient(root, args);
                break;
            case "-list":
            case "-l":
                ListClients(root);
                break;
            default:
                Writer.WriteLine($"未知客户端子命令: {action}");
                Writer.WriteInfo("用法: /a2a -client [-list | -add <name> <endpoint> [apiKey] | -delete <name>]");
                break;
        }
    }

    /// <summary>
    /// 列出客户端配置。
    /// </summary>
    /// <param name="root">工作区根路径。</param>
    private void ListClients(string root)
    {
        var clients = A2AWorkspaceConfigStore.LoadClients(root);
        Writer.WriteHeader($"客户端 ({clients.Count})");
        if (clients.Count == 0)
        {
            Writer.WriteInfo("  未配置（可执行 /a2a -client -add <name> <endpoint> [apiKey] 新增）");
            return;
        }

        foreach (var (file, cfg) in clients)
        {
            var name = string.IsNullOrWhiteSpace(cfg.Name) ? Path.GetFileNameWithoutExtension(file) : cfg.Name;
            var keyHint = string.IsNullOrEmpty(cfg.ApiKey) ? "" : " (apiKey=***)";
            var subAgentHint = cfg.RegisterAsSubAgent == true ? " [subagent]" : "";
            Writer.WriteInfo($"  - {name}: {cfg.Endpoint ?? "-"}{keyHint}{subAgentHint}");
        }
    }

    /// <summary>
    /// 新增客户端配置（缺参时表单收集）。
    /// </summary>
    /// <param name="root">工作区根路径。</param>
    /// <param name="args">完整参数（args[0]=-client, args[1]=-add）。</param>
    private void AddClient(string root, string[] args)
    {
        string name;
        string endpoint;
        string apiKey;
        bool register;

        if (args.Length >= 4)
        {
            name = args[2];
            endpoint = args[3];
            apiKey = args.Length >= 5 ? args[4] : "";
            register = false;
        }
        else
        {
            var values = Ui.ShowForm("新增 A2A 客户端", new[]
            {
                new FormField("名称"),
                new FormField("远端端点 (如 https://host/a2a)"),
                new FormField("API Key（可留空）", IsPassword: true, Required: false),
                new FormField("注册为子代理 (true/false)", InitialValue: "false", Required: false),
            });
            if (values is null) return;

            name = values[0].Trim();
            endpoint = values[1].Trim();
            apiKey = values[2].Trim();
            register = bool.TryParse(values[3].Trim(), out var parsed) && parsed;
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(endpoint))
        {
            Writer.WriteError("名称与端点不能为空");
            return;
        }

        var fileName = A2AWorkspaceConfigStore.BuildClientFileName(name);
        var filePath = Path.Combine(A2AWorkspaceConfigStore.ResolveDir(root), fileName);
        if (File.Exists(filePath))
        {
            if (!Ui.Confirm("覆盖客户端配置", $"客户端 '{name}' 已存在，确认覆盖？", defaultValue: false))
                return;
        }

        var file = new A2AWorkspaceFile
        {
            Name = name,
            Endpoint = endpoint,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            RegisterAsSubAgent = register,
        };
        A2AWorkspaceConfigStore.SaveClient(root, fileName, file);

        Writer.WriteSuccess($"已保存客户端配置: {fileName}");
        Writer.WriteInfo("执行 /a2a -stop 再 /a2a -start（或重启）后生效");
    }

    /// <summary>
    /// 删除客户端配置（缺参时选择列表）。
    /// </summary>
    /// <param name="root">工作区根路径。</param>
    /// <param name="args">完整参数（args[0]=-client, args[1]=-delete）。</param>
    private void DeleteClient(string root, string[] args)
    {
        var clients = A2AWorkspaceConfigStore.LoadClients(root);
        if (clients.Count == 0)
        {
            Writer.WriteError("暂无客户端配置");
            return;
        }

        string? fileName;
        if (args.Length >= 3)
        {
            var keyword = args[2];
            var match = clients.FirstOrDefault(c =>
                string.Equals(c.Config.Name, keyword, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.FileName, keyword, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.FileName, A2AWorkspaceConfigStore.BuildClientFileName(keyword), StringComparison.OrdinalIgnoreCase));
            fileName = match.FileName;
            if (fileName is null)
            {
                Writer.WriteError($"找不到客户端: {keyword}");
                return;
            }
        }
        else
        {
            var idx = Ui.Choose("选择要删除的客户端",
                clients.Select(c => string.IsNullOrWhiteSpace(c.Config.Name) ? c.FileName : c.Config.Name!).ToList());
            if (idx is null) return;
            fileName = clients[idx.Value].FileName;
        }

        if (!Ui.Confirm("删除客户端配置", $"确认删除 '{fileName}'？", defaultValue: false))
            return;

        A2AWorkspaceConfigStore.DeleteFile(root, fileName);
        Writer.WriteSuccess($"已删除客户端配置: {fileName}");
    }

    /// <summary>
    /// 启动 A2A 运行时。
    /// </summary>
    private async Task StartA2AAsync()
    {
        var root = RequireWorkspace();
        if (root is null) return;

        try
        {
            await _lifecycle.StartAsync(root);
            Writer.WriteSuccess(_lifecycle.IsRunning
                ? "A2A 已启动"
                : "A2A 启动完成（未启用或未配置角色，当前未运行）");
        }
        catch (Exception ex)
        {
            Logger.Error("启动 A2A 失败", ex);
            Writer.WriteError($"启动 A2A 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 停止 A2A 运行时。
    /// </summary>
    private async Task StopA2AAsync()
    {
        try
        {
            await _lifecycle.StopAsync();
            Writer.WriteSuccess("A2A 已停止");
        }
        catch (Exception ex)
        {
            Logger.Error("停止 A2A 失败", ex);
            Writer.WriteError($"停止 A2A 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 要求存在当前工作区。
    /// </summary>
    /// <returns>工作区根路径；无则报错并返回 null。</returns>
    private string? RequireWorkspace()
    {
        var root = WorkspaceRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            Writer.WriteError("当前未选择工作区");
            return null;
        }
        return root;
    }

    /// <summary>
    /// 输出服务端配置字段。
    /// </summary>
    /// <param name="server">服务端配置。</param>
    private void WriteServerFields(A2AWorkspaceFile server)
    {
        Writer.WriteInfo($"  Host: {server.Host ?? "-"}");
        Writer.WriteInfo($"  Port: {server.Port?.ToString() ?? "-"}");
        Writer.WriteInfo($"  Path: {server.Path ?? "-"}");
        Writer.WriteInfo($"  RequireApiKey: {server.RequireApiKey?.ToString() ?? "-"}");
        Writer.WriteInfo($"  AgentName: {server.AgentName ?? "-"}");
        Writer.WriteInfo($"  ApiKeys: {(server.ApiKeys is { Count: > 0 } ? $"已配置 {server.ApiKeys.Count} 个" : "未配置")}");
    }

    /// <summary>
    /// 显示帮助
    /// </summary>
    private void ShowHelp()
    {
        Writer.WriteLine();
        Writer.WriteHeader("A2A 协作管理用法：");
        Writer.WriteLine("  /a2a                                          - 显示状态（同 -list）");
        Writer.WriteLine("  /a2a -list                                    - 显示状态与服务端/客户端概览");
        Writer.WriteLine("  /a2a -server                                  - 查看或首次创建 server.json");
        Writer.WriteLine("  /a2a -client                                  - 列出客户端");
        Writer.WriteLine("  /a2a -client -add <name> <endpoint> [apiKey]  - 新增客户端");
        Writer.WriteLine("  /a2a -client -delete <name>                   - 删除客户端");
        Writer.WriteLine("  /a2a -start                                   - 启动 A2A");
        Writer.WriteLine("  /a2a -stop                                    - 停止 A2A");
        Writer.WriteLine("  简写: /a2a -l, /a2a -s, /a2a -c");
    }
}