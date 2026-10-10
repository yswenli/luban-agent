/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Runtime
*文件名： A2ARuntimeState
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：A2A 运行时状态，保存工作区解析后的有效配置与远端执行器映射供宿主/执行器共享
*
*****************************************************************************/
using System.Collections.Concurrent;

using LuBan.AIAgent.Orchestration;

using LubanAgentA2A.Configuration;

namespace LubanAgentA2A.Runtime;

/// <summary>
/// A2A 运行时状态。单例，保存 <see cref="A2ARuntimeLifecycle"/> 解析出的有效配置与
/// 运行时注册的「角色名 → 远端执行器」映射，供 <c>A2AServerHost</c> 与组合执行器
/// 在运行时读取（避免在 DI 注册期依赖尚未确定的工作区）。
/// </summary>
public sealed class A2ARuntimeState
{
    private readonly ConcurrentDictionary<string, ISubAgentExecutor> _remoteExecutors = new(StringComparer.Ordinal);

    /// <summary>当前有效配置；未启动时为空配置。</summary>
    public Agent2AgentOptions Options { get; private set; } = new();

    /// <summary>本地进程内 Agent 名称（注册表键，默认 "luban-agent"）。</summary>
    public string LocalAgentName { get; private set; } = "luban-agent";

    /// <summary>是否已启动。</summary>
    public bool IsStarted { get; private set; }

    /// <summary>启动时使用的工作区根（已规范化）；未启动为空。用于判定换工作区后是否需重建。</summary>
    public string? WorkspaceRoot { get; private set; }

    /// <summary>远端执行器映射的实时只读视图（键为角色名）。</summary>
    public IReadOnlyDictionary<string, ISubAgentExecutor> RemoteExecutors => _remoteExecutors;

    /// <summary>更新有效配置（由生命周期在启动时调用）。</summary>
    /// <param name="options">解析后的有效配置。</param>
    public void Update(Agent2AgentOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>设置本地进程内 Agent 名称。</summary>
    /// <param name="agentName">Agent 名称。</param>
    public void SetLocalAgentName(string agentName)
    {
        if (!string.IsNullOrWhiteSpace(agentName))
        {
            LocalAgentName = agentName;
        }
    }

    /// <summary>注册/覆盖远端执行器。</summary>
    /// <param name="roleName">角色名。</param>
    /// <param name="executor">远端执行器。</param>
    public void SetRemoteExecutor(string roleName, ISubAgentExecutor executor)
    {
        ArgumentException.ThrowIfNullOrEmpty(roleName);
        ArgumentNullException.ThrowIfNull(executor);
        _remoteExecutors[roleName] = executor;
    }

    /// <summary>清空全部远端执行器。</summary>
    public void ClearRemoteExecutors() => _remoteExecutors.Clear();

    /// <summary>标记启动/停止状态。</summary>
    /// <param name="started">是否已启动。</param>
    public void SetStarted(bool started)
    {
        IsStarted = started;
    }

    /// <summary>记录本次启动使用的工作区根（已规范化）。停止时清空。</summary>
    /// <param name="workspaceRoot">规范化的工作区根路径；null 表示无工作区。</param>
    public void SetWorkspaceRoot(string? workspaceRoot)
    {
        WorkspaceRoot = workspaceRoot;
    }
}