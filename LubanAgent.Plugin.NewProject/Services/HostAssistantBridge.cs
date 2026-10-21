/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： HostAssistantBridge
*创建时间：2026/10/21
*描述：路线② 桥接层：让插件经宿主既有服务，在项目会话上自跑一轮提示词（M4-P4 决策点 D1）
*****************************************************************************/

using System.Diagnostics;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Sessions;
using LubanAgentCore.Configuration;
using LubanAgentCore.Hosting;
using LubanAgentCore.Models;
using LubanAgentCore.Services;
using LubanAgent.Plugin.NewProject.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 插件 → 宿主会话助手的桥接层（M4-P4 技术路线 ②）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：Step 4 / Step 5 的判定要求「执行形态仍是路线 ②」——真正的动作（连库探活、逐端编译）
/// 由项目会话里的助手执行，插件只负责「下发提示词 → 收集事件 → 回读结论文件 → 给出阻断或放行」。
/// 插件不能直接解析宿主容器里的会话服务（scoped 转发 + <c>ValidateScopes=true</c> 会抛），
/// 故一律经 <see cref="NewProjectService"/> 的按需解析入口（<c>UserContextRegistry</c> 用户桶）拿服务。
/// </para>
/// <para>
/// <b>会话与运行时的自愈</b>：档案里的 <c>SessionId</c> 可能为空（聊天入口 / 档案被手工清过），
/// 或会话存在但运行时未构建（重启后未打开过该会话）——两种都由本层兜底：先自动建会话，
/// 再经用户 scope 的 <see cref="AgentHostService.InitializeAsync"/> 唤起运行时。
/// </para>
/// <para>
/// <b>工具审批</b>：一律 <see cref="ToolPermissionMode.Default"/> + <see cref="AssistantToolPolicy"/> 白名单回调，
/// <b>不</b>使用 <c>BypassPermissions</c>；被拒的调用不取消本轮（<c>onUserDenied</c> 传 <c>null</c>），
/// 只记进 <see cref="TurnOutcome.Denials"/>、<b>不计进</b> <see cref="TurnOutcome.Errors"/>（见该字段注释），
/// 让助手自己看到拒绝结果后继续收尾。
/// </para>
/// <para>
/// <b>并发</b>：同一会话的回合是串行的（运行时自带队列）。若下发时该会话正被 UI 聊天占用，
/// 本回合会排队等待——<see cref="TurnOutcome.WasBusy"/> 只作读数呈现，不打断、不取消。
/// </para>
/// </remarks>
internal sealed class HostAssistantBridge
{
    private readonly Func<Type, object?> _resolve;
    private readonly Func<ISessionManager?> _resolveSessions;
    private readonly IPluginHost? _host;

    /// <summary>创建桥接层。</summary>
    /// <param name="resolve">从宿主根容器按需解析服务的委托。</param>
    /// <param name="resolveSessions">解析当前用户会话管理器的委托。</param>
    /// <param name="host">插件宿主能力接口（可空：只用于日志）。</param>
    internal HostAssistantBridge(Func<Type, object?> resolve, Func<ISessionManager?> resolveSessions, IPluginHost? host)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _resolveSessions = resolveSessions ?? throw new ArgumentNullException(nameof(resolveSessions));
        _host = host;
    }

    /// <summary>
    /// 一轮提示词的执行读数（事件流 + 结论文件的回读结果）。
    /// </summary>
    internal sealed record TurnOutcome
    {
        /// <summary>事件流是否跑完（未因宿主能力缺失而提前返回）。</summary>
        public bool Ran { get; init; }

        /// <summary>实际使用的会话 id（可能由本层新建）。</summary>
        public string? SessionId { get; init; }

        /// <summary>本层是否新建了会话（调用方需要把 id 写回档案）。</summary>
        public bool SessionCreated { get; init; }

        /// <summary>本层是否唤起了运行时（重启后首次运行）。</summary>
        public bool RuntimeBootstrapped { get; init; }

        /// <summary>下发时该会话是否正被 UI 聊天占用（本回合排队执行）。</summary>
        public bool WasBusy { get; init; }

        /// <summary>助手调用的工具名（按调用顺序）。</summary>
        public IReadOnlyList<string> ToolCalls { get; init; } = Array.Empty<string>();

        /// <summary>审批放行的调用（工具名 + 依据）。</summary>
        public IReadOnlyList<string> Approvals { get; init; } = Array.Empty<string>();

        /// <summary>审批拒绝的调用（工具名 + 依据）。</summary>
        public IReadOnlyList<string> Denials { get; init; } = Array.Empty<string>();

        /// <summary>本轮错误（工具失败 / 错误事件）；<b>不含</b>守卫拒绝与用户取消（那两类只进 <see cref="Denials"/>）。</summary>
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        /// <summary>助手正文总字数（只作读数，不参与判定）。</summary>
        public int TextLength { get; init; }

        /// <summary>约定的结论文件路径。</summary>
        public string? ResultFile { get; init; }

        /// <summary>结论文件正文；不存在时为 <c>null</c>。</summary>
        public string? ResultFileContent { get; init; }

        /// <summary>本轮耗时。</summary>
        public TimeSpan Elapsed { get; init; }

        /// <summary>宿主能力缺失时的失败原因（非空表示<b>没能跑起来</b>，与「跑完了但结论是失败」区分开）。</summary>
        public string? Failure { get; init; }
    }

    /// <summary>
    /// 在项目会话上下发一轮提示词并回读结论文件。
    /// </summary>
    /// <param name="spec">项目档案（提供 <c>SessionId</c> / <c>WorkspaceId</c> / <c>LocalPath</c>）。</param>
    /// <param name="prompt">提示词。</param>
    /// <param name="resultFile">约定的结论文件绝对路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行读数。</returns>
    internal async Task<TurnOutcome> RunAsync(
        ProjectSpec spec,
        string prompt,
        string resultFile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var watch = Stopwatch.StartNew();
        var toolCalls = new List<string>();
        var approvals = new List<string>();
        var denials = new List<string>();
        var errors = new List<string>();
        var textLength = 0;
        var sessionCreated = false;
        var bootstrapped = false;
        var wasBusy = false;
        string? sessionId = spec.SessionId;

        TurnOutcome Fail(string reason, string? content = null)
            => new()
            {
                Ran = false,
                SessionId = sessionId,
                SessionCreated = sessionCreated,
                RuntimeBootstrapped = bootstrapped,
                WasBusy = wasBusy,
                ToolCalls = toolCalls,
                Approvals = approvals,
                Denials = denials,
                Errors = errors,
                TextLength = textLength,
                ResultFile = resultFile,
                ResultFileContent = content,
                Elapsed = watch.Elapsed,
                Failure = reason
            };

        try
        {
            // ── ① 会话运行时管理器（宿主单例）─────────────────────────────────
            var runtimes = _resolve(typeof(SessionRuntimeManager)) as SessionRuntimeManager;
            if (runtimes is null)
                return Fail("宿主未注册会话运行时管理器（SessionRuntimeManager），无法驱动项目助手执行本步。");

            var sessions = _resolveSessions();

            // ── ② 定位会话：档案 → 当前会话；都没有就新建 ─────────────────────
            if (string.IsNullOrWhiteSpace(sessionId))
                sessionId = sessions?.CurrentSession?.SessionId;

            var runtime = string.IsNullOrWhiteSpace(sessionId) ? null : runtimes.Get(sessionId!);

            // ── ③ 运行时缺失：先补会话，再经用户 scope 唤起运行时 ─────────────
            if (runtime is null)
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    if (sessions is null)
                        return Fail("当前用户的会话管理器不可用，无法自动创建执行会话。");

                    var title = string.IsNullOrWhiteSpace(spec.Name) ? "新建项目" : $"新建项目：{spec.Name}";
                    var created = await sessions.CreateSessionAsync(title, spec.WorkspaceId).ConfigureAwait(false);
                    sessionId = created.SessionId;
                    sessionCreated = true;
                }

                var factory = _resolve(typeof(IServiceScopeFactory)) as IServiceScopeFactory;
                using var scope = factory?.CreateUserScope(LocalUserDefaults.DefaultUserId);
                var bootstrap = scope?.ServiceProvider.GetService(typeof(AgentHostService)) as AgentHostService;
                if (bootstrap is null)
                    return Fail("无法解析 AgentHostService，项目会话运行时不可用。");

                await bootstrap.InitializeAsync(sessionId!, cancellationToken).ConfigureAwait(false);
                bootstrapped = true;
                runtime = runtimes.Get(sessionId!);
            }

            if (runtime is null)
                return Fail("项目会话运行时仍不可用（InitializeAsync 后未注册）。");

            wasBusy = runtime.IsRunning;

            var runner = runtime.Services.GetService(typeof(AgentHostService)) as AgentHostService;
            if (runner is null)
                return Fail("项目会话 scope 内无法解析 AgentHostService。");

            // ── ④ 下发提示词：Default 模式 + 白名单确认回调 ────────────────────
            var workspaceRoot = spec.LocalPath;

            await foreach (var evt in runner.RunStreamingAsync(
                prompt,
                Array.Empty<string>(),
                (toolName, arguments) =>
                {
                    var decision = AssistantToolPolicy.Decide(toolName, arguments, workspaceRoot, out var reason);
                    if (decision == ConfirmResult.Allow)
                        approvals.Add($"{toolName}｜{reason}");
                    else
                        denials.Add($"{toolName}｜{reason}");

                    return decision;
                },
                ToolPermissionMode.Default,
                onPlannedAction: null,
                onUserDenied: null,
                sessionId: sessionId,
                ct: cancellationToken).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case ToolCallStartedEvent started:
                        toolCalls.Add(started.Name);
                        break;
                    case TextDeltaEvent delta:
                        textLength += delta.Delta?.Length ?? 0;
                        break;
                    case ToolCallFailedEvent failed:
                        // 守卫拒绝 / 用户取消不是「执行失败」：拒绝依据已由审批回调写进 Denials（见类注释）。
                        // 若这里再计进 Errors，四环判据的「事件流无错误」就会把「助手换条命令再试一次」判成整步失败
                        // ——真机已复现：一次 RunShell 被白名单拒 → 即使逐端编译全过也报失败。
                        if (!AssistantToolPolicy.IsGuardDenial(failed.Error))
                            errors.Add($"工具失败：{failed.Error}");
                        break;
                    case ErrorEvent error:
                        errors.Add(error.Message);
                        break;
                }
            }

            var content = await TryReadAsync(resultFile, cancellationToken).ConfigureAwait(false);

            return new TurnOutcome
            {
                Ran = true,
                SessionId = sessionId,
                SessionCreated = sessionCreated,
                RuntimeBootstrapped = bootstrapped,
                WasBusy = wasBusy,
                ToolCalls = toolCalls,
                Approvals = approvals,
                Denials = denials,
                Errors = errors,
                TextLength = textLength,
                ResultFile = resultFile,
                ResultFileContent = content,
                Elapsed = watch.Elapsed,
                Failure = null
            };
        }
        catch (OperationCanceledException)
        {
            return Fail("已取消。");
        }
        catch (Exception ex)
        {
            _host?.LogError("[newproject] 驱动项目助手执行失败", ex);
            return Fail($"驱动项目助手执行失败：{ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>
    /// 回读结论文件（不存在或读取失败都只返回 <c>null</c>，由调用方按「未产出结论」处理）。
    /// </summary>
    /// <param name="path">结论文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>文件正文；不存在时为 <c>null</c>。</returns>
    private static async Task<string?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}