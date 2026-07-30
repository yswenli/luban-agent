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

    /// <summary>
    /// 获取友好的 API 错误信息
    /// </summary>
    /// <param name="ex">异常</param>
    /// <returns>友好的错误信息</returns>
    protected static string GetFriendlyApiErrorMessage(Exception ex)
    {
        var message = ex.Message;
        
        if (message.Contains("404") || message.Contains("Not Found"))
        {
            return "API 请求失败：模型不存在或 API 端点配置错误。请检查：\n" +
                   "  1. 选择的模型是否支持\n" +
                   "  2. API 端点配置是否正确\n" +
                   "  3. API Key 是否有效";
        }
        
        if (message.Contains("401") || message.Contains("Unauthorized"))
        {
            return "API 认证失败：API Key 无效或已过期。请检查 API Key 配置。";
        }
        
        if (message.Contains("403") || message.Contains("Forbidden"))
        {
            if (message.Contains("usage limit") || message.Contains("billing cycle") || 
                message.Contains("quota") || message.Contains("access_terminated"))
            {
                return "API 配额已用尽：当前计费周期的使用限额已用完。\n" +
                       "请等待配额刷新或升级计划后继续使用。";
            }
            if (message.Contains("invalid_api_key") || message.Contains("api_key"))
            {
                return "API Key 无效：请检查 API Key 配置是否正确。";
            }
            return "API 访问被拒绝：没有权限访问该模型或 API。请检查 API Key 权限。";
        }
        
        if (message.Contains("429") || message.Contains("Too Many Requests"))
        {
            return "API 请求过于频繁：已达到速率限制。请稍后再试。";
        }
        
        if (message.Contains("500") || message.Contains("Internal Server Error"))
        {
            return "API 服务器错误：服务端出现问题。请稍后再试或联系服务商。";
        }
        
        if (message.Contains("503") || message.Contains("Service Unavailable"))
        {
            return "API 服务不可用：服务暂时不可用。请稍后再试。";
        }
        
        if (ex is System.ClientModel.ClientResultException clientEx)
        {
            return $"API 调用失败：{clientEx.Message}\n请检查模型配置和 API Key。";
        }
        
        return ex.Message;
    }
}