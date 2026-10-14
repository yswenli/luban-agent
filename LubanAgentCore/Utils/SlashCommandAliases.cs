/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Utils
*文件名： SlashCommandAliases
*版本号： V1.0.0.0
*唯一标识：斜杠命令简写展开
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/14
*描述：斜杠命令子命令简写（-l/-a/-s 等）展开，CLI / GUI 共用
*
*****************************************************************************/
namespace LubanAgentCore.Utils;

/// <summary>
/// 斜杠命令子命令简写展开规则。歧义简写（如 <c>-s</c>）按命令区分：
/// <c>/a2a</c> 为 <c>-server</c>，LLM Wiki 为 <c>-search</c>，其余为 <c>-switch</c>。
/// </summary>
public static class SlashCommandAliases
{
    /// <summary>
    /// 展开子命令简写。首元素（命令名）保持不变。
    /// </summary>
    /// <param name="cmd">命令名（含 / 前缀，如 <c>/work</c>）。</param>
    /// <param name="parts">已按空白拆分的命令与参数。</param>
    /// <returns>展开后的参数数组（不修改入参）。</returns>
    public static string[] Expand(string cmd, string[] parts)
    {
        if (parts.Length == 0) return parts;

        var isLlmWiki = IsLlmWikiCommand(cmd);
        var isA2A = cmd == "/a2a";

        var result = new string[parts.Length];
        result[0] = parts[0];

        for (var i = 1; i < parts.Length; i++)
        {
            result[i] = parts[i] switch
            {
                "-l" => "-list",
                "-a" => "-add",
                "-u" => "-update",
                "-d" => "-delete",
                "-s" => isA2A ? "-server" : (isLlmWiki ? "-search" : "-switch"),
                "-i" when isLlmWiki => "-index",
                "-c" => isA2A ? "-client" : "-clear",
                "-n" => "-new",
                "-t" => "-tools",
                _ => parts[i]
            };
        }

        return result;
    }

    /// <summary>是否为 LLM Wiki 命令（含别名），用于区分 <c>-s</c>/<c>-i</c> 等歧义简写。</summary>
    /// <param name="cmd">命令名。</param>
    /// <returns>是则返回 true。</returns>
    public static bool IsLlmWikiCommand(string cmd)
        => cmd is "/llmwiki" or "/lw" or "/rag" or "/rg" or "/wiki" or "/wk";
}