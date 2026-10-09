/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Security
*文件名： ApiKeyAuthMiddleware
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：A2A 入站 API Key 鉴权中间件，校验 X-Api-Key 请求头
*
*****************************************************************************/
using Microsoft.AspNetCore.Http;

namespace LubanAgentA2A.Security;

/// <summary>
/// A2A 入站 API Key 鉴权中间件。
/// <para>仅对命中 A2A 端点路径前缀的请求生效；<c>/.well-known/agent-card.json</c> 等发现端点保持公开，
/// 以便客户端在鉴权前解析 AgentCard。</para>
/// <para><c>requireApiKey=true</c> 时未带或错误 <c>X-Api-Key</c> 一律返回 401；
/// 即使 <c>apiKeys</c> 为空也拒绝（无有效密钥即全部拒绝，失败安全）。</para>
/// </summary>
public sealed class ApiKeyAuthMiddleware
{
    /// <summary>入站鉴权请求头名称。</summary>
    public const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly bool _requireApiKey;
    private readonly HashSet<string> _apiKeys;
    private readonly string _pathPrefix;

    /// <summary>
    /// 创建鉴权中间件实例。
    /// </summary>
    /// <param name="next">管道中的下一个中间件。</param>
    /// <param name="requireApiKey">是否要求 API Key。</param>
    /// <param name="apiKeys">允许的 API Key 列表。</param>
    /// <param name="pathPrefix">需鉴权的路径前缀（如 "/a2a"）。</param>
    public ApiKeyAuthMiddleware(
        RequestDelegate next,
        bool requireApiKey,
        string[] apiKeys,
        string pathPrefix)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _requireApiKey = requireApiKey;
        _apiKeys = new HashSet<string>(apiKeys ?? Array.Empty<string>(), StringComparer.Ordinal);
        _pathPrefix = string.IsNullOrEmpty(pathPrefix) ? "/" : pathPrefix;
    }

    /// <summary>
    /// 执行鉴权：命中受保护路径且校验不通过时返回 401，否则放行。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_requireApiKey || !IsProtected(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (context.Request.Headers.TryGetValue(HeaderName, out var provided)
            && !string.IsNullOrEmpty(provided)
            && _apiKeys.Contains(provided.ToString()))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Unauthorized").ConfigureAwait(false);
    }

    private bool IsProtected(PathString path)
    {
        if (path.StartsWithSegments("/.well-known", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_pathPrefix == "/")
        {
            return true;
        }

        return path.StartsWithSegments(_pathPrefix, StringComparison.OrdinalIgnoreCase);
    }
}