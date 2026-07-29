/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Commands
*文件名： StatsCommand
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/29
*描述：统计命令 - 会话与 Token 统计
*
 *****************************************************************************/
using System;
using System.Threading.Tasks;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Sessions;
using Microsoft.Extensions.Configuration;

namespace LubanAgent.Commands;

/// <summary>
/// 统计命令 - 会话与 Token 统计
/// </summary>
public class StatsCommand : CommandBase
{
    private readonly ISessionManager _sessionManager;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "stats";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "会话与 Token 统计（-days N）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    public StatsCommand(ConfigManager configManager, IConfiguration configuration, ISessionManager sessionManager)
        : base(configManager, configuration)
    {
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// 执行命令（无参数统计全部）
    /// </summary>
    public override Task ExecuteAsync() => ShowStatsAsync(null);

    /// <summary>
    /// 执行带参数的命令，支持 -days N 或 -d N
    /// </summary>
    public override async Task<bool> ExecuteAsync(string[] args)
    {
        int? days = null;

        if (args.Length > 0)
        {
            if (args.Length == 2 && (args[0] == "-days" || args[0] == "-d" || args[0] == "days") && int.TryParse(args[1], out var d) && d > 0)
            {
                days = d;
            }
            else
            {
                WriteError("用法: /stats [-days N]（N 为正整数）\n      简写: /st -d N");
                return true;
            }
        }

        await ShowStatsAsync(days);
        return true;
    }

    private async Task ShowStatsAsync(int? days)
    {
        var stats = await _sessionManager.GetGlobalStatsAsync(days);

        Console.WriteLine();
        Console.WriteLine(days.HasValue ? $"最近 {days} 天统计：" : "全部统计：");
        Console.WriteLine($"  总会话数: {stats.TotalSessions}");
        Console.WriteLine($"  总消息数: {stats.TotalMessages}");
        Console.WriteLine($"  总 Token: {stats.TotalTokens:N0}");
        Console.WriteLine($"  统计天数: {stats.Days}");
        Console.WriteLine($"  日均 Token: {stats.AverageDailyTokens:F0}");
    }
}
