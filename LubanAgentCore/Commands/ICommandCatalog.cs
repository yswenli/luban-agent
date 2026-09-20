/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Commands
*文件名： ICommandCatalog
*版本号： V1.0.0.0
*唯一标识：斜杠命令清单接口
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/20
*描述：斜杠命令清单接口。两端通过此接口查询命令元数据，
*用于补全菜单过滤、命令分发与 /help 文案生成。
*
*****************************************************************************/
namespace LubanAgentCore.Commands;

/// <summary>
/// 斜杠命令清单接口。两端通过此接口查询命令元数据。
/// </summary>
public interface ICommandCatalog
{
    /// <summary>
    /// 全部命令清单（按注册顺序）。
    /// </summary>
    IReadOnlyList<SlashCommandDefinition> All { get; }

    /// <summary>
    /// 按前缀过滤命令（补全菜单使用）。返回命令名或别名以 prefix 开头的项，
    /// 仅包含指定客户端支持的命令。prefix 为空或仅 "/" 时返回全部支持项。
    /// </summary>
    /// <param name="prefix">用户输入前缀（含 / 前缀，如 "/he"）。</param>
    /// <param name="client">查询的客户端（Cli / Codex）。</param>
    /// <returns>匹配的命令清单。</returns>
    IReadOnlyList<SlashCommandDefinition> MatchByPrefix(string prefix, SlashCommandClient client);

    /// <summary>
    /// 精确查找命令（命令分发使用）。按命令名或别名精确匹配。
    /// </summary>
    /// <param name="name">命令名或别名（含 / 前缀，如 "/h"）。</param>
    /// <param name="client">查询的客户端（Cli / Codex）。</param>
    /// <returns>命中的命令定义；未命中返回 null。</returns>
    SlashCommandDefinition? Find(string name, SlashCommandClient client);
}
