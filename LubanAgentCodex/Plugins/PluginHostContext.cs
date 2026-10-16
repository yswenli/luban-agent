/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.Plugins
*文件名： PluginHostContext
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：插件宿主的作用域访问桥：从单例宿主安全触达按用户/会话隔离的 scoped 服务
*
*****************************************************************************/
using LubanAgentCore.Configuration;
using LubanAgentCore.Context;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Skills;

namespace LubanAgentCodex.Plugins;

/// <summary>
/// 插件宿主的「作用域访问桥」。
/// </summary>
/// <remarks>
/// <para>
/// 插件宿主必须注册为<b>单例</b>（框架 <c>PluginManager</c> 为单例，经根容器延迟解析 <c>IPluginHost</c>），
/// 而它需要触达的大多服务是按用户/会话隔离的 <b>scoped</b>（<c>SkillRegistry</c> / <c>MCPRegistry</c> /
/// <c>IAgentReloadTrigger</c> / <c>SessionContextState</c>），不可直接构造注入。
/// </para>
/// <para>
/// 本类提供唯一合法通路：根单例 <see cref="UserContextRegistry"/> 取该用户的
/// <c>SessionManager</c>/<c>WorkspaceManager</c>（其内存实例为进程级共享），
/// 再由根单例 <see cref="SessionRuntimeManager"/> 定位会话运行时，用
/// <c>SessionRuntime.Services</c>（会话 scope）解析 scoped 服务——全程不提升作用域，
/// <c>ValidateScopes=true</c> 下安全。
/// </para>
/// </remarks>
public sealed class PluginHostContext
{
    private readonly IServiceProvider _root;
    private readonly UserContextRegistry _users;
    private readonly SessionRuntimeManager _runtimes;

    /// <summary>
    /// 创建作用域访问桥。
    /// </summary>
    /// <param name="root">根级 DI 容器。</param>
    /// <param name="users">用户状态注册表（根单例）。</param>
    /// <param name="runtimes">会话运行时管理器（根单例）。</param>
    public PluginHostContext(IServiceProvider root, UserContextRegistry users, SessionRuntimeManager runtimes)
    {
        _root = root;
        _users = users;
        _runtimes = runtimes;
    }

    /// <summary>根级 DI 容器（解析身份无关的进程级单例）。</summary>
    public IServiceProvider Root => _root;

    /// <summary>当前用户标识（桌面端为本地默认用户）。</summary>
    public string UserId => LocalUserDefaults.DefaultUserId;

    /// <summary>当前用户的状态桶（含会籍/工作区管理器）。</summary>
    public UserBucket User => _users.GetOrCreate(UserId);

    /// <summary>当前用户的会话管理器（进程级共享实例）。</summary>
    public ISessionManager SessionManager => User.SessionManager;

    /// <summary>当前用户的工作区管理器（进程级共享实例）。</summary>
    public IWorkspaceManager WorkspaceManager => User.WorkspaceManager;

    /// <summary>当前活动会话标识；无活动会话时为 <c>null</c>。</summary>
    public string? CurrentSessionId => SessionManager.CurrentSession?.SessionId;

    /// <summary>当前活动工作区；未选择时为 <c>null</c>。</summary>
    public WorkspaceInfo? CurrentWorkspace => WorkspaceManager.CurrentWorkspace;

    /// <summary>当前活动工作区根路径；未选择时为空串。</summary>
    public string CurrentWorkspaceRoot => CurrentWorkspace?.RootPath ?? string.Empty;

    /// <summary>全部会话运行时快照（用于跨会话级联操作）。</summary>
    public IReadOnlyList<SessionRuntime> AllRuntimes() => _runtimes.GetAll();

    /// <summary>
    /// 取指定会话运行时；不存在（尚未对话过）返回 <c>null</c>。
    /// </summary>
    /// <param name="sessionId">会话标识；为空取当前活动会话。</param>
    public SessionRuntime? GetRuntime(string? sessionId = null)
    {
        var id = string.IsNullOrWhiteSpace(sessionId) ? CurrentSessionId : sessionId;
        return string.IsNullOrWhiteSpace(id) ? null : _runtimes.Get(id);
    }

    /// <summary>
    /// 取指定会话绑定的工作区根路径；会话未创建时回退到当前工作区。
    /// </summary>
    /// <param name="runtime">会话运行时。</param>
    public string WorkspaceRootOf(SessionRuntime runtime)
    {
        var state = runtime.Services.GetService<SessionContextState>();
        var root = state?.WorkspaceRoot;
        return string.IsNullOrWhiteSpace(root) ? CurrentWorkspaceRoot : root!;
    }

    /// <summary>
    /// 在指定会话 scope 内解析服务（如会话私有的 <c>MCPRegistry</c> / <c>SkillRegistry</c>）。
    /// </summary>
    /// <typeparam name="T">服务类型。</typeparam>
    /// <param name="runtime">会话运行时。</param>
    /// <returns>服务实例；会话 scope 未注册时返回 <c>null</c>。</returns>
    public T? ResolveInSession<T>(SessionRuntime runtime) where T : class
        => runtime.Services.GetService<T>();

    /// <summary>
    /// 重置指定会话的 Agent 缓存，使其在下一次对话时按最新配置（含插件贡献的 Skill / MCP）重建。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    public void ResetAgent(string sessionId)
    {
        var runtime = GetRuntime(sessionId);
        if (runtime is null) return;

        try
        {
            runtime.Services.GetService<AgentHostService>()?.Reset(sessionId);
        }
        catch (Exception ex)
        {
            Logger.Warn($"重置会话 {sessionId} 的 Agent 失败（插件贡献生效延迟到下次对话）", ex);
        }
    }

    /// <summary>重置全部已存在会话的 Agent 缓存。</summary>
    public void ResetAllAgents()
    {
        foreach (var runtime in AllRuntimes())
            ResetAgent(runtime.SessionId);
    }
}