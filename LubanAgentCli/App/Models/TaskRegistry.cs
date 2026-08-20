/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models
*文件名： TaskRegistry
*版本号： V1.0.0.0
*唯一标识：Agent 任务注册表
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：Agent View 三期多会话的任务注册表，管理并发任务生命周期
*
*****************************************************************************/
namespace LubanAgentCli.App.Models;

/// <summary>
/// Agent View（三期）任务注册表。管理多会话的 AgentTask 创建、状态跟踪与并发控制，
/// 提供线程安全的集合操作 API。
/// </summary>
public sealed class TaskRegistry
{
    private readonly object _lock = new();
    private readonly List<AgentTask> _tasks = new();

    /// <summary>默认最大并发任务数。</summary>
    public const int DefaultMaxWorkers = 3;

    /// <summary>
    /// 当前任务订阅数变化时触发。
    /// </summary>
    public event Action? TasksChanged;

    /// <summary>
    /// 当前最大并发任务数。
    /// </summary>
    public int MaxWorkers { get; }

    /// <summary>
    /// 当前运行中的任务数。
    /// </summary>
    public int RunningCount
    {
        get { lock (_lock) return _tasks.Count(t => t.Status == AgentTaskStatus.Running); }
    }

    /// <summary>
    /// 所有任务快照（线程安全，返回新列表）。
    /// </summary>
    public IReadOnlyList<AgentTask> All
    {
        get { lock (_lock) return _tasks.ToList().AsReadOnly(); }
    }

    /// <summary>
    /// 初始化任务注册表。
    /// </summary>
    /// <param name="maxWorkers">最大并发任务数，默认为 3。</param>
    public TaskRegistry(int maxWorkers = DefaultMaxWorkers)
    {
        MaxWorkers = Math.Max(1, maxWorkers);
    }

    /// <summary>
    /// 调度新任务。若未达并发上限则立即启动，否则加入等待队列。
    /// </summary>
    /// <param name="description">任务描述。</param>
    /// <param name="workspaceName">关联工作区名称。</param>
    /// <returns>创建的任务实例。</returns>
    public AgentTask Enqueue(string description, string? workspaceName = null)
    {
        var task = new AgentTask(description, workspaceName);

        lock (_lock)
        {
            _tasks.Add(task);

            if (RunningCount < MaxWorkers)
            {
                task.Status = AgentTaskStatus.Running;
                task.StartedAt = DateTime.Now;
            }
        }

        var handler = TasksChanged;
        handler?.Invoke();
        return task;
    }

    /// <summary>
    /// 标记任务完成。
    /// </summary>
    /// <param name="task">已完成的任务。</param>
    /// <param name="success">是否成功。</param>
    public void Complete(AgentTask task, bool success)
    {
        ArgumentNullException.ThrowIfNull(task);

        AgentTask? next = null;
        lock (_lock)
        {
            task.Status = success ? AgentTaskStatus.Completed : AgentTaskStatus.Failed;
            task.CompletedAt = DateTime.Now;
            task.Dispose();

            // 找到下一个候补任务并启动
            next = _tasks.FirstOrDefault(t => t.Status == AgentTaskStatus.Pending);
            if (next is not null && RunningCount < MaxWorkers)
            {
                next.Status = AgentTaskStatus.Running;
                next.StartedAt = DateTime.Now;
            }
        }

        var handler2 = TasksChanged;
        handler2?.Invoke();
    }

    public void Cancel(AgentTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_lock)
        {
            task.Cts.Cancel();

            if (task.Status is AgentTaskStatus.Running or AgentTaskStatus.Pending)
            {
                task.Status = AgentTaskStatus.Cancelled;
                task.CompletedAt = DateTime.Now;
            }

            task.Dispose();
        }

        var handler = TasksChanged;
        handler?.Invoke();
    }

    /// <summary>
    /// 取消所有任务。
    /// </summary>
    public void CancelAll()
    {
        IReadOnlyList<AgentTask> snapshot;
        lock (_lock)
        {
            snapshot = _tasks.ToList().AsReadOnly();
        }

        foreach (var task in snapshot)
        {
            Cancel(task);
        }
    }
}
