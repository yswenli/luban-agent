/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Commands
*文件名： SlashCommandDefinition
*版本号： V1.0.0.0
*唯一标识：斜杠命令元数据定义
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/20
*描述：CLI/Codex 共享的斜杠命令元数据。包含命令名、别名、描述、
*分类、用法、所属客户端标记。避免两端命令清单漂移。
*
*****************************************************************************/
namespace LubanAgentCore.Commands;

/// <summary>
/// 斜杠命令客户端标记：标识命令在哪些前端可用。
/// </summary>
[Flags]
public enum SlashCommandClient
{
    /// <summary>无（占位）。</summary>
    None = 0,
    /// <summary>LubanAgentCli（TUI）可用。</summary>
    Cli = 1,
    /// <summary>LubanAgentCodex（Avalonia）可用。</summary>
    Codex = 2,
    /// <summary>两端均可用。</summary>
    Both = Cli | Codex
}

/// <summary>
/// 斜杠命令元数据。两端共享同一份清单，避免漂移。
/// </summary>
/// <param name="Name">命令名（含 / 前缀，如 "/help"）。</param>
/// <param name="Aliases">别名列表（含 / 前缀，如 ["/h"]，可为空）。</param>
/// <param name="Description">中文描述（用于补全菜单第二列与 /help 文案）。</param>
/// <param name="Category">分类（基础 / 管理 / 会话 等，用于 /help 分组）。</param>
/// <param name="Usage">用法示例（如 "/help" 或 "/mode [name]"）。</param>
/// <param name="SupportedClients">客户端标记（Cli / Codex / Both）。</param>
public sealed record SlashCommandDefinition(
    string Name,
    IReadOnlyList<string> Aliases,
    string Description,
    string Category,
    string Usage,
    SlashCommandClient SupportedClients)
{
    /// <summary>
    /// 判断指定客户端是否支持本命令。
    /// </summary>
    public bool Supports(SlashCommandClient client)
        => (SupportedClients & client) != SlashCommandClient.None;

    /// <summary>
    /// 判断命令名或任一别名是否匹配指定输入（大小写不敏感，含 / 前缀）。
    /// </summary>
    public bool Matches(string input)
    {
        if (string.Equals(Name, input, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var alias in Aliases)
        {
            if (string.Equals(alias, input, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
