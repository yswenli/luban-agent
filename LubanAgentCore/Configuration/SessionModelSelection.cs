/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Configuration
*文件名： SessionModelSelection
*描述：会话级模型选择：会话单独记录模型时用会话的，否则回落全局默认模型
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

/// <summary>
/// 会话级模型选择：会话行单独记录模型时用会话的，否则回落全局默认模型。
/// </summary>
/// <remarks>
/// <para>
/// 全局选择（设置页的「新会话默认模型」）仍存在用户配置文件里；会话行只在用户为该会话单独挑模型时写入，
/// 因此没被单独选择过的会话始终跟随当前全局默认。
/// </para>
/// <para>
/// 不做实例内缓存：宿主每轮运行前调用一次，直接按主键查会话行，避免长驻 scope 里读到过期的会话模型。
/// </para>
/// </remarks>
public sealed class SessionModelSelection : IAgentModelSelection
{
    private readonly IAppConfigReader _config;
    private readonly SessionRepository _sessions;
    private readonly IUserContext _userContext;

    /// <summary>
    /// 创建会话级模型选择。
    /// </summary>
    /// <param name="config">用户配置（提供全局默认模型与已配置供应商）。</param>
    /// <param name="sessions">会话仓储（读取会话单独选定的模型）。</param>
    /// <param name="userContext">当前用户（会话归属过滤）。</param>
    public SessionModelSelection(IAppConfigReader config, SessionRepository sessions, IUserContext userContext)
    {
        _config = config;
        _sessions = sessions;
        _userContext = userContext;
    }

    /// <inheritdoc />
    string? IAgentModelSelection.ProviderId => Split(GlobalModel).Provider;

    /// <inheritdoc />
    string? IAgentModelSelection.ModelId => Split(GlobalModel).Model;

    /// <inheritdoc />
    string IAgentModelSelection.QualifiedModelName => GlobalModel;

    /// <inheritdoc />
    async ValueTask<string> IAgentModelSelection.ResolveQualifiedModelNameAsync(string? sessionId, CancellationToken ct)
    {
        var global = GlobalModel;
        if (string.IsNullOrWhiteSpace(sessionId)) return global;

        var (provider, model) = await _sessions.GetModelAsync(sessionId, UserId).ConfigureAwait(false);
        return ResolveSessionModel(provider, model) ?? global;
    }

    /// <summary>全局默认模型（<c>provider:model</c>；未设置时为空串）。</summary>
    private string GlobalModel => (_config.SelectedModel ?? string.Empty).Trim();

    /// <summary>会话归属用户；未绑定身份时按本地默认用户处理，与 SessionManager 同口径。</summary>
    private string UserId => string.IsNullOrWhiteSpace(_userContext.UserId)
        ? LocalUserDefaults.DefaultUserId
        : _userContext.UserId.Trim();

    /// <summary>
    /// 把会话行记录拼成限定名；供应商已不在配置里（被删除或改名）时返回 null，由调用方回落全局。
    /// </summary>
    private string? ResolveSessionModel(string? provider, string? model)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model)) return null;

        return _config.Providers.Any(p => string.Equals(p.Name, provider, StringComparison.OrdinalIgnoreCase))
            ? $"{provider}:{model}"
            : null;
    }

    /// <summary>拆分 <c>provider:model</c>。</summary>
    private static (string? Provider, string? Model) Split(string? qualified)
    {
        if (string.IsNullOrWhiteSpace(qualified)) return (null, null);

        var idx = qualified.IndexOf(':');
        return idx > 0 && idx < qualified.Length - 1
            ? (qualified[..idx], qualified[(idx + 1)..])
            : (null, null);
    }
}