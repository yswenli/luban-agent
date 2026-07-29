/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Commands
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
using System.Threading.Tasks;
using LuBan.AIAgent;
using LuBan.AIAgent.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgent.Commands;

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

    /// <summary>
    /// 创建命令实例
    /// </summary>
    /// <param name="configManager">配置管理器</param>
    /// <param name="configuration">应用配置</param>
    protected CommandBase(ConfigManager configManager, IConfiguration configuration)
    {
        ConfigManager = configManager;
        Configuration = configuration;
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
    /// 读取密码输入（隐藏显示）
    /// </summary>
    /// <returns>输入的密码</returns>
    protected static string ReadPassword()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                Console.Write("\b \b");
            }
            else if (key.KeyChar != '\0')
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        }
        Console.WriteLine();
        return password.ToString();
    }

    /// <summary>
    /// 输出信息
    /// </summary>
    /// <param name="message">消息</param>
    protected static void WriteInfo(string message)
    {
        Console.WriteLine(message);
    }

    /// <summary>
    /// 输出错误
    /// </summary>
    /// <param name="message">错误消息</param>
    protected static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"错误: {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// 输出成功信息
    /// </summary>
    /// <param name="message">消息</param>
    protected static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    protected ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddSingleton(ConfigManager);
        services.AddSingleton<IChatClient>(sp => ConfigManager.CreateChatClient());
        services.AddLuBanAgent(Configuration);
        return services.BuildServiceProvider();
    }
}