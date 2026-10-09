/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Commands
*文件名： CommandCatalog
*版本号： V1.0.0.0
*唯一标识：默认斜杠命令清单
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/20
*描述：默认命令清单实现。静态注册当前两端已实现的全部斜杠命令，
*以现有 Cli/Codex 代码为准，避免清单漂移。
*
*****************************************************************************/
using System.Collections.ObjectModel;

namespace LubanAgentCore.Commands;

/// <summary>
/// 默认命令清单实现。注册当前两端已实现的全部斜杠命令。
/// </summary>
public sealed class CommandCatalog : ICommandCatalog
{
    private static readonly IReadOnlyList<SlashCommandDefinition> DefaultCommands = new ReadOnlyCollection<SlashCommandDefinition>(BuildDefaultCommands());

    /// <inheritdoc/>
    public IReadOnlyList<SlashCommandDefinition> All => DefaultCommands;

    /// <inheritdoc/>
    public IReadOnlyList<SlashCommandDefinition> MatchByPrefix(string prefix, SlashCommandClient client)
    {
        if (string.IsNullOrEmpty(prefix) || prefix == "/")
        {
            return DefaultCommands.Where(c => c.Supports(client)).ToList();
        }

        var normalized = prefix.TrimStart('/');
        if (normalized.Length == 0)
        {
            return DefaultCommands.Where(c => c.Supports(client)).ToList();
        }

        var result = new List<SlashCommandDefinition>();
        foreach (var cmd in DefaultCommands)
        {
            if (!cmd.Supports(client)) continue;

            if (cmd.Name.TrimStart('/').StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(cmd);
                continue;
            }
            foreach (var alias in cmd.Aliases)
            {
                if (alias.TrimStart('/').StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(cmd);
                    break;
                }
            }
        }
        return result;
    }

    /// <inheritdoc/>
    public SlashCommandDefinition? Find(string name, SlashCommandClient client)
    {
        foreach (var cmd in DefaultCommands)
        {
            if (!cmd.Supports(client)) continue;
            if (cmd.Matches(name)) return cmd;
        }
        return null;
    }

    private static List<SlashCommandDefinition> BuildDefaultCommands()
    {
        var list = new List<SlashCommandDefinition>
        {
            // ── 基础 ──
            new("/help", Array.Empty<string>(), "显示此帮助", "基础", "/help", SlashCommandClient.Both),
            new("/clear", Array.Empty<string>(), "清空会话历史", "基础", "/clear", SlashCommandClient.Both),
            new("/mode", Array.Empty<string>(), "查看或切换权限模式", "基础", "/mode [name]", SlashCommandClient.Both),
            new("/exit", new[] { "/quit" }, "退出程序", "基础", "/exit", SlashCommandClient.Both),
            new("/agi", new[] { "/a", "/browse", "/b" }, "Agent 对话（默认激活，直接输入文本即可）", "基础", "/agi", SlashCommandClient.Cli),

            // ── 管理 ──
            new("/provider", new[] { "/p" }, "管理 AI Provider", "管理", "/provider [子命令]", SlashCommandClient.Both),
            new("/model", new[] { "/m" }, "管理模型", "管理", "/model [子命令]", SlashCommandClient.Both),
            new("/skill", new[] { "/sk" }, "管理/执行 Skill", "管理", "/skill [子命令]", SlashCommandClient.Both),
            new("/rule", new[] { "/r" }, "管理规则", "管理", "/rule [子命令]", SlashCommandClient.Both),
            new("/mcp", new[] { "/mp" }, "管理 MCP 客户端", "管理", "/mcp [子命令]", SlashCommandClient.Both),
            new("/session", new[] { "/se" }, "管理对话会话", "管理", "/session [子命令]", SlashCommandClient.Both),
            new("/stats", new[] { "/st" }, "会话与 Token 统计", "管理", "/stats", SlashCommandClient.Both),
            new("/work", new[] { "/w" }, "工作区管理", "管理", "/work [子命令]", SlashCommandClient.Both),
            new("/llmwiki", new[] { "/lw", "/rag", "/rg", "/wiki", "/wk" }, "LLM Wiki 知识库管理", "管理", "/llmwiki [子命令]", SlashCommandClient.Both),
            new("/a2a", Array.Empty<string>(), "A2A 协作管理", "管理", "/a2a [子命令]", SlashCommandClient.Both),
            new("/init", Array.Empty<string>(), "初始化工作区 AGENTS.md", "管理", "/init <描述文本>", SlashCommandClient.Cli),
        };
        return list;
    }
}
