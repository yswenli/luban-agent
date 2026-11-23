/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebRequestScheme
*描述：判定当前请求实际使用的协议（纯逻辑，便于单测）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 当前请求实际使用的协议（<c>http</c> / <c>https</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 宿主只开一个监听端口，默认由 SAEA 直接终结 TLS；SAEA 的请求对象不暴露「本连接是否 TLS」
/// （<c>SAEA.Http.Base.HttpBase</c> 只有 <c>Protocal</c> 即协议版本），因此协议只能从两处推导：
/// 反向代理给出的 <c>X-Forwarded-Proto</c>，以及宿主自身的 <c>LubanAgentWeb:UseHttps</c> 配置。
/// </para>
/// <para>
/// 文件链接的基地址与 Cookie 的 <c>Secure</c> 必须走同一个判定：两处各判一次就会分叉，
/// 出现「链接是 https、Cookie 却不带 Secure」这类问题。本类是唯一入口。
/// </para>
/// </remarks>
public static class WebRequestScheme
{
    /// <summary>明文协议。</summary>
    public const string Http = "http";

    /// <summary>TLS 协议。</summary>
    public const string Https = "https";

    /// <summary>反向代理声明原始协议时用的请求头名。</summary>
    public const string ForwardedProtoHeader = "X-Forwarded-Proto";

    /// <summary>
    /// 判定请求实际协议。
    /// </summary>
    /// <param name="request">当前请求；为 null 时只能按宿主配置判断。</param>
    /// <param name="hostUsesHttps">宿主是否以 HTTPS 提供服务（<c>LubanAgentWeb:UseHttps</c>）。</param>
    /// <returns><see cref="Http"/> 或 <see cref="Https"/>。</returns>
    public static string Resolve(HttpRequest? request, bool hostUsesHttps)
        => Pick(WebRequestAddress.HeaderValue(request, ForwardedProtoHeader), hostUsesHttps);

    /// <summary>
    /// 按 <c>X-Forwarded-Proto</c> 的值判定协议；缺失或不可识别时按宿主配置。
    /// </summary>
    /// <remarks>
    /// 代理链会追加多段（如 <c>https, http</c>），只取第一段：它才是用户与最近一跳之间实际用的协议。
    /// 无法识别的值（拼错、非协议名）一律当缺失处理，不让它把链接与 Cookie 引到错误协议上。
    /// </remarks>
    /// <param name="forwardedProto"><c>X-Forwarded-Proto</c> 头值，可为 null。</param>
    /// <param name="hostUsesHttps">宿主是否以 HTTPS 提供服务。</param>
    /// <returns><see cref="Http"/> 或 <see cref="Https"/>。</returns>
    internal static string Pick(string? forwardedProto, bool hostUsesHttps)
    {
        if (!string.IsNullOrWhiteSpace(forwardedProto))
        {
            var first = forwardedProto.Split(',')[0].Trim();
            if (string.Equals(first, Https, StringComparison.OrdinalIgnoreCase))
            {
                return Https;
            }

            if (string.Equals(first, Http, StringComparison.OrdinalIgnoreCase))
            {
                return Http;
            }
        }

        return hostUsesHttps ? Https : Http;
    }
}