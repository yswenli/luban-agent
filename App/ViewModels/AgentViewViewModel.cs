/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.ViewModels
*文件名： AgentViewViewModel
*版本号： V1.0.0.0
*唯一标识：Agent View 视图模型
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Agent View 三期多会话管理，持有 TaskRegistry 并协调任务视图渲染
*
*****************************************************************************/
using LubanAgentCli.App.Models;
using LubanAgentCli.App.Models.Blocks;

namespace LubanAgentCli.App.ViewModels;

/// <summary>
/// Agent View 视图模型。持有 <see cref="TaskRegistry"/>，管理多会话任务，
/// 提供视图切换与任务状态查询。
/// </summary>
internal sealed class AgentViewViewModel
{
    private readonly TaskRegistry _registry;
    private readonly ConversationDocument _doc;

    /// <summary>是否显示任务视图（Tab 切换，默认显示对话视图）。</summary>
    public bool IsTaskViewActive { get; private set; }

    /// <summary>任务注册表。</summary>
    public TaskRegistry Registry => _registry;

    /// <summary>
    /// 初始化 Agent View ViewModel。
    /// </summary>
    /// <param name="registry">共享任务注册表。</param>
    /// <param name="doc">会话文档模型。</param>
    public AgentViewViewModel(TaskRegistry registry, ConversationDocument doc)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _registry.TasksChanged += RefreshTaskView;
    }

    /// <summary>
    /// 切换对话/任务视图。
    /// </summary>
    public void ToggleView()
    {
        IsTaskViewActive = !IsTaskViewActive;

        if (IsTaskViewActive)
        {
            RefreshTaskView();
        }
    }

    private void RefreshTaskView()
    {
        if (!IsTaskViewActive) return;

        var tasks = _registry.All;

        _doc.AppendBlock(new SystemBlock(string.Empty));
        _doc.AppendBlock(new SystemBlock("═══ Agent View · 多会话任务表 ═══",
            foreground: BlockColors.Accent, isBold: true));
        _doc.AppendBlock(new SystemBlock($"并发槽位: {_registry.RunningCount}/{_registry.MaxWorkers}",
            foreground: BlockColors.System));
        _doc.AppendBlock(new SystemBlock("按 Tab 回到对话视图", foreground: BlockColors.System));

        if (tasks.Count == 0)
        {
            _doc.AppendBlock(new SystemBlock("  暂无任务。在对话视图中输入内容即可创建 Agent 任务。",
                foreground: BlockColors.System));
        }
        else
        {
            foreach (var task in tasks)
            {
                var statusIcon = task.Status switch
                {
                    AgentTaskStatus.Running => "▶",
                    AgentTaskStatus.Completed => "✓",
                    AgentTaskStatus.Failed => "✗",
                    AgentTaskStatus.Cancelled => "⊗",
                    _ => "○"
                };

                var fg = task.Status switch
                {
                    AgentTaskStatus.Running => BlockColors.Accent,
                    AgentTaskStatus.Completed => BlockColors.Success,
                    AgentTaskStatus.Failed or AgentTaskStatus.Cancelled => BlockColors.Failure,
                    _ => BlockColors.System
                };

                var ws = task.WorkspaceName is not null ? $" @ {task.WorkspaceName}" : "";
                var desc = task.Description.Length > 60 ? task.Description[..57] + "..." : task.Description;
                _doc.AppendBlock(new SystemBlock(
                    $"  {statusIcon} [{task.Status}] {desc}{ws}",
                    foreground: fg));
            }
        }

        _doc.AppendBlock(new SystemBlock("═══", foreground: BlockColors.Accent));
        _doc.AppendBlock(new SystemBlock(string.Empty));
    }
}
