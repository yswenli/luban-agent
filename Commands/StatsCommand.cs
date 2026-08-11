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
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/29
*描述：统计命令 - 会话与 Token 统计（支持 --all 跨工作区统计）
*
*****************************************************************************/
namespace LubanAgent.Commands;

/// <summary>
/// 统计命令 - 会话与 Token 统计
/// </summary>
public class StatsCommand : CommandBase
{
    private readonly ISessionManager _sessionManager;
    private readonly SessionRepository _sessionRepo;

    /// <summary>
    /// 命令名称
    /// </summary>
    public override string Name => "stats";

    /// <summary>
    /// 命令描述
    /// </summary>
    public override string Description => "会话与 Token 统计（-days N, --all 跨工作区）";

    /// <summary>
    /// 创建命令实例
    /// </summary>
    public StatsCommand(ConfigManager configManager, IConfiguration configuration, ISessionManager sessionManager, SessionRepository sessionRepo)
        : base(configManager, configuration)
    {
        _sessionManager = sessionManager;
        _sessionRepo = sessionRepo;
    }

    /// <summary>
    /// 执行命令（无参数统计当前工作区）
    /// </summary>
    public override Task ExecuteAsync() => ShowStatsAsync(null, allWorkspaces: false);

    /// <summary>
    /// 执行带参数的命令，支持 -days N 或 --all
    /// </summary>
    public override async Task<bool> ExecuteAsync(string[] args)
    {
        int? days = null;
        var allWorkspaces = false;

        if (args.Length > 0)
        {
            // --all：跨工作区统计
            if (args.Contains("--all") || args.Contains("-all"))
            {
                allWorkspaces = true;
                args = args.Where(a => a != "--all" && a != "-all").ToArray();
            }

            if (args.Length == 2 && (args[0] == "-days" || args[0] == "-d" || args[0] == "days") && int.TryParse(args[1], out var d) && d > 0)
            {
                days = d;
            }
            else if (args.Length > 0)
            {
                WriteError("用法: /stats [-days N] [--all]\n      简写: /st -d N, /st --all");
                return true;
            }
        }

        await ShowStatsAsync(days, allWorkspaces);
        return true;
    }

    /// <summary>
    /// 显示统计信息
    /// </summary>
    /// <param name="days">统计天数，null 表示全部</param>
    /// <param name="allWorkspaces">是否跨工作区统计</param>
    private async Task ShowStatsAsync(int? days, bool allWorkspaces)
    {
        var wsId = WorkspaceManager.Current?.WorkspaceId;

        // --all 或无工作区上下文时统计全部；否则按当前工作区过滤
        if (allWorkspaces || string.IsNullOrEmpty(wsId))
        {
            var stats = await _sessionManager.GetGlobalStatsAsync(days);
            var scope = allWorkspaces ? "全部工作区" : "全部";
            Console.WriteLine();
            Console.WriteLine(days.HasValue ? $"{scope} 最近 {days} 天统计：" : $"{scope}统计：");
            Console.WriteLine($"  总会话数: {stats.TotalSessions}");
            Console.WriteLine($"  总消息数: {stats.TotalMessages}");
            Console.WriteLine($"  总 Token: {stats.TotalTokens:N0}");
            Console.WriteLine($"  统计天数: {stats.Days}");
            Console.WriteLine($"  日均 Token: {stats.AverageDailyTokens:F0}");
        }
        else
        {
            // 按当前工作区统计
            var sessions = await _sessionRepo.GetByWorkspaceAsync(wsId!);
            var since = days.HasValue ? DateTime.Now.AddDays(-days.Value) : (DateTime?)null;
            var filtered = since.HasValue
                ? sessions.Where(s => s.CreateTime >= since.Value).ToList()
                : sessions;

            var totalMessages = filtered.Sum(s => s.MessageCount);
            var totalTokens = filtered.Sum(s => s.TotalTokens);
            var spanDays = days ?? (filtered.Any()
                ? Math.Max(1, (int)(DateTime.Now - filtered.Min(s => s.CreateTime)).TotalDays + 1)
                : 1);

            Console.WriteLine();
            Console.WriteLine(days.HasValue
                ? $"当前工作区 最近 {days} 天统计："
                : $"当前工作区统计：");
            Console.WriteLine($"  总会话数: {filtered.Count}");
            Console.WriteLine($"  总消息数: {totalMessages}");
            Console.WriteLine($"  总 Token: {totalTokens:N0}");
            Console.WriteLine($"  统计天数: {spanDays}");
            Console.WriteLine($"  日均 Token: {(spanDays > 0 ? totalTokens / (double)spanDays : 0):F0}");
        }
    }
}
