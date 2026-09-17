/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.Commands
*文件名： CommandBase
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：命令基类
*
*****************************************************************************/
namespace LubanAgentCli.Commands;

/// <summary>
/// 命令基类，提供通用功能
/// </summary>
public abstract class CommandBase : ICommand
{
    /// <summary>
    /// 配置管理器
    /// </summary>
    protected readonly ConfigManager ConfigManager;

    /// <summary>
    /// 应用配置
    /// </summary>
    protected readonly IConfiguration Configuration;

    /// <summary>TUI 输出写入器（输出到会话文档）</summary>
    protected readonly ITuiOutputWriter Writer;
    /// <summary>TUI 模态交互服务</summary>
    protected readonly ITuiUiService Ui;

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="writer">TUI 输出写入器</param>
    /// <param name="ui">TUI 模态交互服务</param>
    protected CommandBase(ConfigManager configManager, IConfiguration configuration,
        ITuiOutputWriter writer, ITuiUiService ui)
    {
        ConfigManager = configManager;
        Configuration = configuration;
        Writer = writer ?? throw new ArgumentNullException(nameof(writer));
        Ui = ui ?? throw new ArgumentNullException(nameof(ui));
    }

    /// <summary>
    /// 命令名称
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// 命令描述
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// 执行命令
    /// </summary>
    public abstract Task ExecuteAsync();

    /// <summary>
    /// 执行命令（带子命令和参数），默认不支持子命令
    /// </summary>
    public virtual Task<bool> ExecuteAsync(string[] args)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// 构建服务提供者，注册核心服务并初始化 LubanAgent
    /// </summary>
    /// <returns>配置完成的服务提供者</returns>
    protected ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddSingleton(ConfigManager);
        services.AddSingleton<IChatClient>(sp => ChatClientResilience.Wrap(
            ConfigManager.CreateChatClient(),
            sp.GetRequiredService<IOptions<LuBanAgentOptions>>().Value));
        services.AddLuBanAgent(Configuration);
        return services.BuildServiceProvider();
    }
}