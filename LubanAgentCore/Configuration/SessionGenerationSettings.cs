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

    /// <summary>构造。</summary>
    /// <param name="config">用户配置（全局默认与模型档案）。</param>
    /// <param name="sessions">会话仓储（会话覆盖值）。</param>
    /// <param name="selection">会话实际使用的模型。</param>
    /// <param name="userContext">当前用户（会话归属过滤）。</param>
    public SessionGenerationSettings(
        ConfigManager config,
        SessionRepository sessions,
        IAgentModelSelection selection,
        IUserContext userContext)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
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

        var canonical = Canonical(stored) ?? Canonical(_config.GetDefaults().ReasoningEffort);
        if (canonical is null) return null;

        // 会话切过模型或手改过配置都可能留下发不出去的值，集合外一律当跟随模型
        return allowed.Contains(canonical, StringComparer.OrdinalIgnoreCase) ? canonical : null;
    }

    /// <inheritdoc />
    public async ValueTask<int?> ResolveContextBudgetAsync(string? sessionId, CancellationToken ct = default)
    {
        var window = (await GetProfileAsync(sessionId, ct).ConfigureAwait(false))?.ContextWindow;
        // 窗口未知就没有档位可谈：交给框架的既有限制，不猜一个数
        if (window is not { } known) return null;

        int? stored = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            (_, stored) = await _sessions.GetGenerationSettingsAsync(sessionId, UserId).ConfigureAwait(false);
        }

        var defaults = _config.GetDefaults().ContextTier;

        // 两个来源都收敛到当前窗口的档位集合，换模型后残留的大档位不会超窗
        return ContextTiers.Normalize(known, stored)
            ?? ContextTiers.Normalize(known, defaults)
            ?? ContextTiers.DefaultTier(known);
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