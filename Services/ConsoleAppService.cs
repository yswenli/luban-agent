/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： ConsoleAppService
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：控制台应用服务，管理命令分发
*
*****************************************************************************/

namespace LubanAgent.Services;

/// <summary>
/// 控制台应用服务，管理命令分发
/// </summary>
public class ConsoleAppService
{
    private readonly ConfigManager _configManager;
    private readonly IConfiguration _configuration;
    private readonly SkillRegistry _skillRegistry;
    private readonly RuleEngine _ruleEngine;
    private readonly MCPRegistry _mcpRegistry;
    private readonly ISessionManager _sessionManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, ICommand> _commands;
    private readonly List<string> _commandHistory;

    /// <summary>
    /// 可用的命令名称列表（用于 Tab 自动完成）
    /// </summary>
    private static readonly string[] CommandNames = new[]
    {
        "/provider", "/p",
        "/model", "/m",
        "/skill", "/sk",
        "/rule", "/r",
        "/mcp", "/mp",
        "/session", "/se",
        "/agi", "/a",
        "/browse", "/b",
        "/stats", "/st",
        "/exit"
    };

    /// <summary>
    /// 命令别名映射（简写 → 完整命令名）
    /// </summary>
    private static readonly Dictionary<string, string> CommandAliases = new()
    {
        ["p"] = "provider",
        ["m"] = "model",
        ["sk"] = "skill",
        ["r"] = "rule",
        ["mp"] = "mcp",
        ["se"] = "session",
        ["a"] = "agi",
        ["b"] = "browse",
        ["st"] = "stats"
    };

    /// <summary>
    /// 子命令缩写映射（缩写 → 完整子命令名，带 - 前缀）
    /// </summary>
    private static readonly Dictionary<string, string> SubCommandAliases = new()
    {
        ["-l"] = "-list",
        ["-a"] = "-add",
        ["-u"] = "-update",
        ["-d"] = "-delete",
        ["-s"] = "-switch",
        ["-n"] = "-new",
        ["-c"] = "-clear",
        ["-t"] = "-tools"
    };

    /// <summary>
    /// 创建应用服务实例
    /// </summary>
    public ConsoleAppService(
        ConfigManager configManager,
        IConfiguration configuration,
        SkillRegistry skillRegistry,
        RuleEngine ruleEngine,
        MCPRegistry mcpRegistry,
        ISessionManager sessionManager,
        IServiceProvider serviceProvider)
    {
        _configManager = configManager;
        _configuration = configuration;
        _skillRegistry = skillRegistry;
        _ruleEngine = ruleEngine;
        _mcpRegistry = mcpRegistry;
        _sessionManager = sessionManager;
        _serviceProvider = serviceProvider;
        _commands = new Dictionary<string, ICommand>();
        _commandHistory = new List<string>();

        RegisterCommands();
    }

    /// <summary>
    /// 注册所有命令
    /// </summary>
    private void RegisterCommands()
    {
        RegisterCommand(new ProviderCommand(_configManager, _configuration));
        RegisterCommand(new ModelCommand(_configManager, _configuration));
        RegisterCommand(new SkillCommand(_configManager, _configuration, _skillRegistry));
        RegisterCommand(new RuleCommand(_configManager, _configuration, _ruleEngine));
        RegisterCommand(new MCPCommand(_configManager, _configuration, _mcpRegistry));
        RegisterCommand(new SessionCommand(_configManager, _configuration, _sessionManager));
        RegisterCommand(new AgiCommand(_configManager, _configuration, _sessionManager, _serviceProvider, TryExecuteCommandAsync));
        RegisterCommand(new BrowseCommand(_configManager, _configuration, TryExecuteCommandAsync));
        RegisterCommand(new StatsCommand(_configManager, _configuration, _sessionManager));
    }

    /// <summary>
    /// 注册单个命令
    /// </summary>
    private void RegisterCommand(ICommand command)
    {
        _commands[command.Name] = command;
    }

    /// <summary>
    /// 运行应用
    /// </summary>
    public async Task RunAsync()
    {
        ShowWelcome();
        ShowCurrentStatus();

        while (true)
        {
            ShowMenu();

            // 使用 Tab 自动完成功能读取命令
            var input = ConsoleUtil.ReadLineWithAutoComplete(
                "请输入命令: ",
                CommandNames,
                _commandHistory);

            if (string.IsNullOrEmpty(input))
                continue;

            // 支持 / 前缀
            input = NormalizeInput(input);

            if (input == "exit")
            {
                Console.WriteLine("再见！");
                return;
            }

            await ExecuteCommandAsync(input);
        }
    }

    /// <summary>
    /// 显示欢迎信息
    /// </summary>
    private static void ShowWelcome()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("========================================");
        Console.WriteLine("  LuBan AI Agent 命令行工具 v1.0");
        Console.WriteLine("========================================");
        Console.WriteLine();
        Console.WriteLine("提示: 所有命令以 / 开头，按 Tab 自动完成，按上/下箭头浏览历史");
        Console.WriteLine("      支持命令简写: /p=/provider, /m=/model, /sk=/skill 等");
        Console.WriteLine("      支持子命令简写: -l=list, -a=add, -s=switch 等（例: /p -l）");
    }

    /// <summary>
    /// 显示当前状态
    /// </summary>
    private void ShowCurrentStatus()
    {
        Console.WriteLine();
        Console.WriteLine("当前状态:");
        Console.WriteLine($"  Provider 数量: {_configManager.Providers.Count}");

        if (_configManager.Providers.Count > 0)
        {
            Console.WriteLine("  已配置的 Provider:");
            foreach (var p in _configManager.Providers)
            {
                Console.WriteLine($"    - {ProviderModels.GetDisplayName(p.Name)}");
            }
        }

        if (_configManager.HasSelectedModel)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  当前选择的模型: {_configManager.SelectedModel}");
            Console.ResetColor();
        }
        else if (_configManager.Providers.Count > 0)
        {
            Console.WriteLine("  当前选择的模型: 未选择 (请使用 model switch 命令选择)");
        }
    }

    /// <summary>
    /// 显示菜单
    /// </summary>
    private static void ShowMenu()
    {
        Console.WriteLine();
        Console.WriteLine("可用命令 (/ 前缀，Tab 自动完成，支持简写):");
        Console.WriteLine("  /provider /p   - 管理 AI Provider (-list/-add/-update/-delete/-switch)");
        Console.WriteLine("  /model /m      - 管理模型 (-list/-add/-update/-delete/-switch)");
        Console.WriteLine("  /skill /sk     - 查看和执行 Skill (-list/-add/-update/-delete/-switch)");
        Console.WriteLine("  /rule /r       - 查看和管理规则 (-list/-add/-update/-delete/-switch)");
        Console.WriteLine("  /mcp /mp       - 查看 MCP 客户端 (-list/-add/-update/-delete/-switch/-connect/-tools)");
        Console.WriteLine("  /session /se   - 管理对话会话 (-list/-new/-clear/-switch)");
        Console.WriteLine("  /agi /a        - 通用 Agent 对话");
        Console.WriteLine("  /browse /b     - 针对网站操作特异化 Agent");
        Console.WriteLine("  /stats /st     - 会话与 Token 统计 (-days N)");
        Console.WriteLine("  /exit          - 退出程序");
        Console.WriteLine();
        Console.WriteLine("子命令简写: -l=-list, -a=-add, -u=-update, -d=-delete, -s=-switch, -n=-new, -c=-clear, -t=-tools");
        Console.WriteLine("示例: /p -l (= /provider -list), /st -d 10 (= /stats -days 10)");
    }

    /// <summary>
    /// 标准化输入，移除 / 前缀
    /// </summary>
    private static string NormalizeInput(string input)
    {
        input = input.Trim();
        if (input.StartsWith('/'))
            input = input[1..];
        return input;
    }

    /// <summary>
    /// 执行命令（供外部调用，如 AgiCommand 中的 / 命令）
    /// </summary>
    /// <param name="input">原始输入（支持 / 前缀）</param>
    /// <returns>是否已处理</returns>
    public async Task<bool> TryExecuteCommandAsync(string input)
    {
        if (string.IsNullOrEmpty(input) || !input.StartsWith('/'))
            return false;

        input = NormalizeInput(input);
        if (input == "exit")
            return false;

        await ExecuteCommandAsync(input);
        return true;
    }

    /// <summary>
    /// 执行命令
    /// </summary>
    private async Task ExecuteCommandAsync(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var commandName = GetCommandName(parts[0]);
        if (commandName == null)
        {
            Console.WriteLine("未知命令，请重试");
            return;
        }

        if (_commands.TryGetValue(commandName, out var command))
        {
            // 如果有子命令参数，先尝试带参数执行
            if (parts.Length > 1)
            {
                var expandedArgs = ExpandSubCommandAliases(parts[1..]);
                var handled = await command.ExecuteAsync(expandedArgs);
                if (handled)
                    return;
            }
            // 没有参数或命令不支持子命令，走交互式菜单
            await command.ExecuteAsync();
        }
        else
        {
            Console.WriteLine("未知命令，请重试");
        }
    }

    /// <summary>
    /// 展开子命令缩写（-l → list, -a → add 等）
    /// </summary>
    private static string[] ExpandSubCommandAliases(string[] args)
    {
        if (args.Length == 0) return args;

        var result = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            result[i] = SubCommandAliases.TryGetValue(args[i].ToLower(), out var expanded)
                ? expanded
                : args[i];
        }
        return result;
    }

    /// <summary>
    /// 获取命令名称（支持别名）
    /// </summary>
    private static string? GetCommandName(string input)
    {
        var name = input.ToLower().TrimStart('/');
        
        // 数字快捷键
        if (int.TryParse(name, out var num))
        {
            return num switch
            {
                1 => "provider",
                2 => "model",
                3 => "skill",
                4 => "rule",
                5 => "mcp",
                6 => "session",
                7 => "agi",
                8 => "browse",
                9 => "stats",
                10 => "exit",
                _ => null
            };
        }

        // 检查是否为别名
        if (CommandAliases.TryGetValue(name, out var fullName))
            return fullName;

        // 返回原始名称（如果有效命令）或 null
        return name;
    }
}