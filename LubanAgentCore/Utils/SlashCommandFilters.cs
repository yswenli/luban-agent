/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Utils
*文件名： SlashCommandFilters
*描述：斜杠命令清单过滤，隐藏框架目录中已下线的命令
*
*****************************************************************************/
namespace LubanAgentCore.Utils;

/// <summary>
/// 斜杠命令清单过滤。
/// </summary>
/// <remarks>
/// 框架内置的命令目录仍包含已随功能移除的 A2A 命令；宿主侧在帮助、补全与网页命令清单中统一隐藏，
/// 避免用户看到无法执行的条目。
/// </remarks>
public static class SlashCommandFilters
{
    /// <summary>框架目录中已下线、需要隐藏的命令名。</summary>
    private static readonly string[] RemovedNames = ["/a2a"];

    /// <summary>
    /// 判断命令是否已下线。
    /// </summary>
    /// <param name="name">命令名（含 / 前缀）。</param>
    /// <returns>已下线返回 true。</returns>
    public static bool IsRemoved(string? name)
        => name is not null && RemovedNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 过滤掉已下线的命令。
    /// </summary>
    /// <typeparam name="T">命令对象类型。</typeparam>
    /// <param name="commands">命令集合。</param>
    /// <param name="nameSelector">取命令名的委托。</param>
    /// <returns>仅包含可用命令的序列。</returns>
    public static IEnumerable<T> ExcludeRemoved<T>(this IEnumerable<T> commands, Func<T, string?> nameSelector)
        => commands.Where(command => !IsRemoved(nameSelector(command)));
}