/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.ViewModels
*文件名： CommandViewModel
*版本号： V1.0.0.0
*唯一标识：命令 ViewModel
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：内联命令路由，将 `/` 输入解析为命令并执行，结果以 SystemBlock 形式追加到会话文档
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions;
using LubanAgent.App;
using LubanAgent.Models;
using LubanAgent.Models.Blocks;

namespace LubanAgent.ViewModels;

/// <summary>
/// 命令 ViewModel。解析 `/` 输入、匹配命令、执行并将结果以 SystemBlock 追加到文档。
/// 输出型命令（如 /help、/clear）结果即系统消息；操作型命令（如 /exit）直接执行。
/// </summary>
internal sealed class CommandViewModel
{
    private readonly ConversationDocument _doc;
    private readonly ConversationViewModel? _conversationVm;
    private readonly IServiceProvider _services;

    /// <summary>
    /// 请求退出应用时触发。
    /// </summary>
    public event Action? ExitRequested;

    public CommandViewModel(
        ConversationDocument doc,
        ConversationViewModel? conversationVm,
        IServiceProvider services)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _conversationVm = conversationVm;
        _services = services;
    }

    /// <summary>
    /// 处理以 `/` 开头的命令行输入。返回 true 表示已处理，false 表示非命令输入（应由 agent 处理）。
    /// </summary>
    /// <param name="input">用户输入。</param>
    /// <returns>已作为命令处理返回 true。</returns>
    public bool TryExecute(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('/'))
        {
            return false;
        }

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/exit":
            case "/quit":
                ExitRequested?.Invoke();
                return true;

            case "/help":
                ExecuteHelp();
                return true;

            case "/clear":
                ExecuteClear();
                return true;

            case "/mode":
                ExecuteMode(parts.Length > 1 ? parts[1] : null);
                return true;

            // ── Agent 对话命令（已自动启用，直接输入即可）──
            case "/agi":
            case "/a":
            case "/browse":
            case "/b":
                _doc.AppendBlock(new SystemBlock("Agent 已就绪，直接输入内容即可开始对话。（或输入 /help 查看帮助）",
                    foreground: BlockColors.Success));
                return true;

            // ── 以下命令尚未迁移（步骤 6 后续）──
            case "/model":
            case "/m":
            case "/provider":
            case "/p":
            case "/skill":
            case "/sk":
            case "/rule":
            case "/r":
            case "/mcp":
            case "/mp":
            case "/session":
            case "/se":
            case "/stats":
            case "/st":
            case "/work":
            case "/w":
            case "/rag":
            case "/rg":
                _doc.AppendBlock(new SystemBlock($"命令 {cmd} 正在迁移中（步骤 6 后续批次）", foreground: BlockColors.Accent));
                return true;

            default:
                _doc.AppendBlock(new SystemBlock($"未知命令: {cmd}，输入 /help 查看可用命令", foreground: BlockColors.Failure));
                return true;
        }
    }

    private void ExecuteHelp()
    {
        _doc.AppendBlock(new SystemBlock("╭─ LuBan Agent CLI 帮助", foreground: BlockColors.Accent, isBold: true));
        _doc.AppendBlock(new SystemBlock("│"));
        _doc.AppendBlock(new SystemBlock("│  可用命令:"));
        _doc.AppendBlock(new SystemBlock("│    /help          显示此帮助"));
        _doc.AppendBlock(new SystemBlock("│    /clear         清空会话历史"));
        _doc.AppendBlock(new SystemBlock("│    /mode [name]   查看或切换权限模式 (default/plan/accept-edits/bypass)"));
        _doc.AppendBlock(new SystemBlock("│    /exit, /quit   退出程序"));
        _doc.AppendBlock(new SystemBlock("│"));
        _doc.AppendBlock(new SystemBlock("│  快捷键:"));
        _doc.AppendBlock(new SystemBlock("│    Enter          提交输入"));
        _doc.AppendBlock(new SystemBlock("│    Ctrl+Q         强制退出"));
        _doc.AppendBlock(new SystemBlock("│    Esc            取消当前 Agent 任务"));
        _doc.AppendBlock(new SystemBlock("│    Ctrl+L         重绘屏幕"));
        _doc.AppendBlock(new SystemBlock("│    Shift+Tab      循环切换权限模式"));
        _doc.AppendBlock(new SystemBlock("│"));
        _doc.AppendBlock(new SystemBlock("│  更多命令（/model, /session, /stats, /work, ...）将在后续批次迁移。"));
        _doc.AppendBlock(new SystemBlock("│  直接输入文本即可与 Agent 对话，无需 /agi 前缀。"));
        _doc.AppendBlock(new SystemBlock("╰──────────────────────────────", foreground: BlockColors.Accent));
    }

    private void ExecuteClear()
    {
        // 清空文档中所有 Block，保留初始横幅由 RootView 重建
        _doc.AppendBlock(new SystemBlock("会话历史已清空", foreground: BlockColors.Success));
    }

    private void ExecuteMode(string? arg)
    {
        if (_conversationVm is null)
        {
            _doc.AppendBlock(new SystemBlock("Agent 尚未初始化，请先输入内容启动 Agent", foreground: BlockColors.System));
            return;
        }

        if (string.IsNullOrWhiteSpace(arg))
        {
            // 显示当前模式
            _doc.AppendBlock(new SystemBlock(
                $"当前权限模式: {_conversationVm.PermissionModeDisplay}",
                foreground: BlockColors.Accent));
            _doc.AppendBlock(new SystemBlock(
                "可用模式: default / plan / accept-edits / bypass  （使用 Shift+Tab 切换）",
                foreground: BlockColors.System));
            return;
        }

        var mode = arg.ToLowerInvariant() switch
        {
            "default" => ToolPermissionMode.Default,
            "plan" => ToolPermissionMode.Plan,
            "accept-edits" or "acceptedits" => ToolPermissionMode.AcceptEdits,
            "bypass" or "bypasspermissions" => ToolPermissionMode.BypassPermissions,
            _ => (ToolPermissionMode?)null
        };

        if (mode is null)
        {
            _doc.AppendBlock(new SystemBlock(
                $"无效模式: {arg}. 可用: default, plan, accept-edits, bypass",
                foreground: BlockColors.Failure));
            return;
        }

        _conversationVm.SetPermissionMode(mode.Value);
        _doc.AppendBlock(new SystemBlock(
            $"权限模式已切换: {_conversationVm.PermissionModeDisplay}",
            foreground: BlockColors.Accent));
    }
}
