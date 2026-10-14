/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.Commands
*文件名： InitCommand
*版本号： V1.0.0.0
*唯一标识：初始化工作区 AGENTS.md
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/17
*描述：初始化工作区 AGENTS.md 命令，由内置 Skill agents-md-generator 生成规约文档
*
*****************************************************************************/
namespace LubanAgentCli.Commands;

using LuBan.AIAgent.Abstractions;

/// <summary>
/// 初始化工作区 AGENTS.md 命令。
/// 用法：/init &lt;描述文本&gt;
/// </summary>
public class InitCommand : CommandBase
{
    private readonly IAgentsMdInitializer _initializer;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILuBanAgentFactory _agentFactory;
    private readonly SpinnerService _spinner;
    private readonly IAgentReloadTrigger? _reloadTrigger;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "init";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "初始化工作区 AGENTS.md";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="initializer">AGENTS.md 初始化服务</param>
    /// <param name="workspaceManager">工作区管理器</param>
    /// <param name="agentFactory">Agent 工厂（取自根容器，携带工作区 AllowedRoots）</param>
    /// <param name="spinner">会话级加载指示器</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    /// <param name="reloadTrigger">宿主上下文重载触发器（可选），落盘后触发 Agent 重建以应用新 AGENTS.md</param>
    public InitCommand(
        ConfigManager configManager,
        IConfiguration configuration,
        IAgentsMdInitializer initializer,
        IWorkspaceManager workspaceManager,
        ILuBanAgentFactory agentFactory,
        SpinnerService spinner,
        ITuiOutputWriter writer,
        ITuiUiService ui,
        IAgentReloadTrigger? reloadTrigger = null)
        : base(configManager, configuration, writer, ui)
    {
        _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _spinner = spinner ?? throw new ArgumentNullException(nameof(spinner));
        _reloadTrigger = reloadTrigger;
    }

    /// <summary>
    /// 执行命令（无参数时显示用法）
    /// </summary>
    public override Task ExecuteAsync()
    {
        ShowUsage();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行带参数的命令。前缀为 '-' 的 token 视为不支持的子命令并忽略，其余 token 拼接为描述文本。
    /// </summary>
    public override async Task<bool> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return true;
        }

        var description = string.Join(" ", args.Where(a => !a.StartsWith('-'))).Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            ShowUsage();
            return true;
        }

        await RunInitAsync(description);
        return true;
    }

    /// <summary>
    /// 执行 AGENTS.md 初始化流程
    /// </summary>
    /// <param name="description">用户描述文本</param>
    private async Task RunInitAsync(string description)
    {
        try
        {
            var workspace = _workspaceManager.CurrentWorkspace;
            if (workspace is null)
            {
                Writer.WriteError("当前没有工作区，请先使用 work -new 或 work -switch 选择工作区");
                return;
            }

            if (!ConfigManager.HasSelectedModel)
            {
                Writer.WriteError("请先使用 model switch 命令选择模型");
                return;
            }

            var targetPath = _initializer.GetTargetPath(workspace);
            var existing = _initializer.ReadExisting(workspace);

            Writer.WriteLine();
            Writer.WriteHeader("初始化工作区 AGENTS.md");
            Writer.WriteLine($"工作区: {workspace.Name} ({workspace.RootPath})");
            Writer.WriteLine($"目标文件: {targetPath}");

            if (existing is not null)
            {
                Writer.WriteWarning($"已存在 AGENTS.md（{existing.Length} 字符），将由模型基于旧内容合并更新");
                if (!Ui.Confirm("覆盖 AGENTS.md",
                        $"将合并现有内容并覆盖以下文件:\n{targetPath}\n\n是否继续？", defaultValue: true))
                {
                    Writer.WriteInfo("已取消");
                    return;
                }
            }

            var options = new AgentsMdOptions
            {
                Description = description,
                ExistingContent = existing,
                OutputMode = "full"
            };

            var prompt = _initializer.BuildPrompt(options);

            Writer.WriteLine();
            _spinner.Start("正在生成 AGENTS.md...");
            string rawOutput;
            try
            {
                var agent = await _agentFactory.CreateAsync(modelName: ConfigManager.SelectedModel);
                var response = await agent.RunAsync(prompt);
                rawOutput = response.Text ?? string.Empty;
            }
            finally
            {
                _spinner.Stop();
            }

            var markdown = _initializer.Normalize(rawOutput);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                Writer.WriteError("生成结果为空，未写入文件，请重试或补充更详细的描述");
                return;
            }

            await _initializer.WriteAsync(workspace, markdown);

            Writer.WriteLine();
            Writer.WriteSuccess($"AGENTS.md 已生成: {targetPath}（{markdown.Length} 字符）");
            Writer.WriteLine();
            Writer.WriteInfo("可在聊天区粘贴以下提示词校验生成结果：");
            Writer.WriteLine(_initializer.BuildVerificationPrompt(targetPath));
            Writer.WriteLine();

            // 触发宿主重建 Agent，使新生成的 AGENTS.md 立即进入后续对话的系统提示词
            _reloadTrigger?.RequestReload();
        }
        catch (Exception ex)
        {
            Logger.Error("InitCommand 操作异常", ex);
            // 框架已包装 LLM/API 失败为 AgentApiException；其余（配置类）异常保留原文
            var message = ex is AgentApiException apiEx ? apiEx.Error.FriendlyMessage : ex.Message;
            Writer.WriteError(message);
        }
    }

    /// <summary>
    /// 打印用法说明
    /// </summary>
    private void ShowUsage()
    {
        Writer.WriteLine();
        Writer.WriteHeader("初始化工作区 AGENTS.md");
        Writer.WriteLine("  /init <描述文本>   - 根据描述生成工作区根目录的 AGENTS.md");
        Writer.WriteLine();
        Writer.WriteLine("示例:");
        Writer.WriteLine("  /init 这是一个日志分析 Agent，负责解析和排查线上日志");
        Writer.WriteLine("  /init 精简模式：代码评审 Agent，关注命名与安全");
    }
}
