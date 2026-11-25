/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Configuration
*文件名： SessionGenerationSettings
*描述：会话生效的思考强度与上下文预算解析
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

/// <summary>
/// 会话生效的生成参数解析：会话覆盖优先，其次全局默认，最后按模型窗口派生。
/// </summary>
/// <remarks>
/// 不做实例内缓存：宿主每轮按会话现查一次，避免长驻 scope 读到过期的覆盖值。
/// </remarks>
public sealed class SessionGenerationSettings : ISessionGenerationSettings
{
    /// <summary>标准链路能发出的思考档位；其余枚举值（minimal / max）一律不采用。</summary>
    private static readonly HashSet<ReasoningEffort> Sendable =
    [
        ReasoningEffort.None,
        ReasoningEffort.Low,
        ReasoningEffort.Medium,
        ReasoningEffort.High,
        ReasoningEffort.ExtraHigh
    ];

    private readonly ConfigManager _config;
    private readonly SessionRepository _sessions;
    private readonly IAgentModelSelection _selection;
    private readonly IUserContext _userContext;
    private readonly CompactionOptions _compaction;

    /// <summary>构造。</summary>
    /// <param name="config">用户配置（全局默认与模型档案）。</param>
    /// <param name="sessions">会话仓储（会话覆盖值）。</param>
    /// <param name="selection">会话实际使用的模型。</param>
    /// <param name="userContext">当前用户（会话归属过滤）。</param>
    /// <param name="compaction">压缩策略（窗口留白）。</param>
    public SessionGenerationSettings(
        ConfigManager config,
        SessionRepository sessions,
        IAgentModelSelection selection,
        IUserContext userContext,
        CompactionOptions compaction)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _compaction = compaction ?? throw new ArgumentNullException(nameof(compaction));
    }

    /// <inheritdoc />
    public async ValueTask<string?> ResolveReasoningEffortAsync(string? sessionId, CancellationToken ct = default)
    {
        // 档案未知（模型没登记，或只登记了窗口没登记档位）即「只跟随模型」：
        // 不知道对方收不收这个字段时宁可不发，避免把请求打成 400。
        var profile = await GetProfileAsync(sessionId, ct).ConfigureAwait(false);
        if (profile?.Reasoning is not { } allowed || allowed.Count == 0) return null;

        string? stored = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            (stored, _) = await _sessions.GetGenerationSettingsAsync(sessionId, UserId).ConfigureAwait(false);
        }

        return ResolveEffort(allowed, stored);
    }

    /// <inheritdoc />
    public async ValueTask<ContextBudget?> ResolveContextBudgetAsync(string? sessionId, CancellationToken ct = default)
    {
        var window = (await GetProfileAsync(sessionId, ct).ConfigureAwait(false))?.ContextWindow;
        // 窗口未知就没有档位可谈：交给框架的既有限制，不猜一个数
        if (window is not { } known) return null;

        int? stored = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            (_, stored) = await _sessions.GetGenerationSettingsAsync(sessionId, UserId).ConfigureAwait(false);
        }

        return ResolveBudget(known, stored);
    }

    /// <inheritdoc />
    public async ValueTask<SessionGenerationState> ResolveAsync(
        string? sessionId,
        ModelProfile? profile = null,
        CancellationToken ct = default)
    {
        // 调用方已经解析过模型时直接复用，避免同一次读取里重复「会话 → 模型」查询
        profile ??= await GetProfileAsync(sessionId, ct).ConfigureAwait(false);

        string? storedEffort = null;
        int? storedTier = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            (storedEffort, storedTier) = await _sessions.GetGenerationSettingsAsync(sessionId, UserId).ConfigureAwait(false);
        }

        return new SessionGenerationState(
            ResolveEffort(profile?.Reasoning, storedEffort),
            ResolveBudget(profile?.ContextWindow, storedTier));
    }

    /// <inheritdoc />
    public IReadOnlyList<int> GetTiers(string modelKey)
        => ContextTiers.FromWindow(_config.GetModelProfile(modelKey)?.ContextWindow);

    /// <summary>会话实际使用的模型档案；模型未知时返回 <c>null</c>。</summary>
    private async ValueTask<ModelProfile?> GetProfileAsync(string? sessionId, CancellationToken ct)
    {
        var modelKey = await _selection.ResolveQualifiedModelNameAsync(sessionId, ct).ConfigureAwait(false);
        return _config.GetModelProfile(modelKey);
    }

    /// <summary>在给定可用档位下解析思考强度：会话覆盖 → 全局默认 → 跟随模型。</summary>
    /// <param name="allowed">模型可发的思考档位；空表示模型未登记能力。</param>
    /// <param name="stored">会话存档值（未规范）。</param>
    private string? ResolveEffort(IReadOnlyList<string>? allowed, string? stored)
    {
        if (allowed is not { Count: > 0 }) return null;

        var canonical = Canonical(stored) ?? Canonical(_config.GetDefaults().ReasoningEffort);
        if (canonical is null) return null;

        // 会话切过模型或手改过配置都可能留下发不出去的值，集合外一律当跟随模型
        return allowed.Contains(canonical, StringComparer.OrdinalIgnoreCase) ? canonical : null;
    }

    /// <summary>在给定窗口下解析上下文预算：会话覆盖 → 全局默认 → 窗口末档。</summary>
    /// <param name="contextWindow">模型上下文窗口；未知时返回 <c>null</c>。</param>
    /// <param name="storedTier">会话存档位（未规范）。</param>
    private ContextBudget? ResolveBudget(int? contextWindow, int? storedTier)
    {
        // 窗口未知就没有档位可谈：交给框架的既有限制，不猜一个数
        if (contextWindow is not { } known) return null;

        var defaults = _config.GetDefaults().ContextTier;

        // 两个来源都收敛到当前窗口的档位集合，换模型后残留的大档位不会超窗
        var tier = ContextTiers.Normalize(known, storedTier)
            ?? ContextTiers.Normalize(known, defaults)
            ?? ContextTiers.DefaultTier(known);
        if (tier is not { } budget) return null;

        // 档位是「用户愿意在多大的上下文上花成本」，实际触发点还要给窗口留出余量：
        // 否则压完立刻又贴到窗口顶端，下一轮照样超长。
        return new ContextBudget(budget, _compaction.ResolveThreshold(known, budget));
    }

    /// <summary>把存档值规范成可发的枚举名；非法或不可发时返回 <c>null</c>。</summary>
    private static string? Canonical(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse<ReasoningEffort>(value.Trim(), true, out var effort) && Sendable.Contains(effort)
            ? effort.ToString()
            : null;
    }

    /// <summary>会话归属用户；未绑定身份时按本地默认用户处理，与 <see cref="SessionModelSelection"/> 同口径。</summary>
    private string UserId => string.IsNullOrWhiteSpace(_userContext.UserId)
        ? LocalUserDefaults.DefaultUserId
        : _userContext.UserId.Trim();
}