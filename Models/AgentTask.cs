/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models
*文件名： AgentTask
*版本号： V1.0.0.0
*唯一标识：Agent 任务模型
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Agent View 三期多会话中单个任务的运行状态
*
*****************************************************************************/
namespace LubanAgent.Models;

/// <summary>
/// Agent View（三期）中单个任务的运行状态。
/// 由 TaskRegistry 统一管理生命周期，每个任务绑定一个 CancellationTokenSource。
/// </summary>
public sealed class AgentTask
{
    /// <summary>任务唯一标识。</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>任务描述/用户输入。</summary>
    public string Description { get; }

    /// <summary>关联工作区名称。</summary>
    public string? WorkspaceName { get; set; }

    /// <summary>当前状态。</summary>
    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Pending;

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; } = DateTime.Now;

    /// <summary>开始执行时间。</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>完成时间。</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>取消令牌源。调用 Cancel() 后 Agent 流式循环应检测并退出。</summary>
    public CancellationTokenSource Cts { get; } = new();

    /// <summary>
    /// 初始化任务。
    /// </summary>
    /// <param name="description">任务描述。</param>
    /// <param name="workspaceName">关联工作区名称，可为空。</param>
    public AgentTask(string description, string? workspaceName = null)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        WorkspaceName = workspaceName;
    }
}

/// <summary>
/// Agent 任务生命周期状态。
/// </summary>
public enum AgentTaskStatus
{
    /// <summary>等待调度。</summary>
    Pending,

    /// <summary>正在执行。</summary>
    Running,

    /// <summary>执行成功完成。</summary>
    Completed,

    /// <summary>执行失败。</summary>
    Failed,

    /// <summary>被用户取消。</summary>
    Cancelled
}
