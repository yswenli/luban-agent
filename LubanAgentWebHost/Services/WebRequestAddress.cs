/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebRequestAddress
*描述：从当前请求头取出宿主对外基地址（纯逻辑，便于单测）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 请求头 → 宿主基地址（<c>scheme://host:port</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 文件链接必须能被同一台机器上的其它设备点开，所以地址只能取自用户此刻访问用的
/// <c>Host</c> 头：是否经代理、用 IP 还是主机名、端口是多少，全在请求里，宿主自己不写死。
/// </para>
/// <para>
/// 反向代理下 <c>X-Forwarded-Host</c> / <c>X-Forwarded-Proto</c> 才是用户实际看到的地址，
/// 有则优先；两者都没有时（非浏览器客户端、HTTP/1.0）回落到宿主自报的局域网地址。
/// 协议一律由 <see cref="WebRequestScheme"/> 判定，不在这里另设默认值。
/// </para>
/// </remarks>
public static class WebRequestAddress
{
    /// <summary>
    /// 从请求头解析基地址。
    /// </summary>
    /// <param name="request">当前请求。</param>
    /// <param name="hostUsesHttps">宿主是否以 HTTPS 提供服务（无转发头时据此判定协议）。</param>
    /// <returns>形如 <c>https://192.168.3.215:18080</c>；无法判定时返回 null。</returns>
    public static string? FromHeaders(HttpRequest? request, bool hostUsesHttps)
    {
        if (request is null)
        {
            return null;
        }

        var authority = HeaderValue(request, "X-Forwarded-Host") ?? HeaderValue(request, "Host");
        var scheme = WebRequestScheme.Resolve(request, hostUsesHttps);
        return Normalize(scheme, authority);
    }

    /// <summary>
    /// 取基地址：请求头优先，缺失时回落到宿主自报的局域网地址。
    /// </summary>
    /// <remarks>
    /// 兜底与宿主入口地址同源（<see cref="WebHostAccess.GetPreferredLanHost"/> + 监听端口），
    /// 不引入任何写死的地址；真实浏览器请求一定带 <c>Host</c>，兜底只用于非浏览器调用。
    /// </remarks>
    /// <param name="request">当前请求。</param>
    /// <param name="port">宿主监听端口。</param>
    /// <param name="hostUsesHttps">宿主是否以 HTTPS 提供服务。</param>
    /// <returns>形如 <c>https://192.168.3.215:18080</c>。</returns>
    public static string Resolve(HttpRequest? request, int port, bool hostUsesHttps)
        => FromHeaders(request, hostUsesHttps)
            ?? WebHostAccess.BuildBaseAddress(WebHostAccess.GetPreferredLanHost(), port, hostUsesHttps);

    /// <summary>
    /// 校验并归一化 <c>scheme://authority</c>：只接受 http/https 且 authority 不含路径与空白。
    /// </summary>
    /// <remarks>
    /// 协议由调用方按 <see cref="WebRequestScheme"/> 的判定结果传入；这里只做格式校验与归一，
    /// 不再自行决定默认协议，避免与链接、Cookie 的判定分叉。
    /// </remarks>
    /// <param name="scheme">协议（<c>http</c> / <c>https</c>，可为 null）。</param>
    /// <param name="authority"><c>Host</c> 头值，形如 <c>192.168.3.215:18080</c>。</param>
    /// <returns>归一化后的基地址；非法返回 null。</returns>
    internal static string? Normalize(string? scheme, string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return null;
        }

        var text = authority.Trim();
        // 头值被拼进过地址（如误带路径）时不做猜测，直接判定不可用
        if (text.Contains('/') || text.Contains('\\') || text.Any(char.IsWhiteSpace))
        {
            return null;
        }

        var protocol = string.Equals(scheme?.Trim(), "https", StringComparison.OrdinalIgnoreCase)
            ? "https"
            : "http";

        return Uri.TryCreate($"{protocol}://{text}", UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Authority)
                ? $"{uri.Scheme}://{uri.Authority}"
                : null;
    }

    /// <summary>
    /// 按名取头值（字典优先，回落到原始报文）。
    /// </summary>
    /// <remarks>
    /// SAEA 把头解析进 <see cref="HttpRequest.Headers"/> 时会清空 <c>Host</c> 的值
    /// （实测 <c>Host=</c>），原始报文仍完整保留在 <see cref="HttpRequest.HeaderStr"/>；
    /// 因此字典没取到时回落到原始头文本，否则「取用户实际访问地址」就永远只能走兜底。
    /// 协议判定（<see cref="WebRequestScheme"/>）也走这里，避免两套头解析。
    /// </remarks>
    /// <param name="request">当前请求；为 null 时返回 null。</param>
    /// <param name="name">头名。</param>
    /// <returns>头值；缺失或为空返回 null。</returns>
    internal static string? HeaderValue(HttpRequest? request, string name)
        => request is null
            ? null
            : DictionaryHeaderValue(request, name) ?? RawHeaderValue(request.HeaderStr, name);

    /// <summary>从头字典取头值（SAEA 的键大小写不定，这里忽略大小写）。</summary>
    /// <param name="request">当前请求。</param>
    /// <param name="name">头名。</param>
    /// <returns>头值；缺失或为空返回 null。</returns>
    private static string? DictionaryHeaderValue(HttpRequest request, string name)
    {
        if (request.Headers is null)
        {
            return null;
        }

        foreach (var pair in request.Headers)
        {
            if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
        }

        return null;
    }

    /// <summary>从原始请求头文本里取头值（形如 <c>Host: 192.168.3.215:18080</c>）。</summary>
    /// <param name="headerText">原始头文本（首行是请求行）。</param>
    /// <param name="name">头名。</param>
    /// <returns>头值；缺失或为空返回 null。</returns>
    internal static string? RawHeaderValue(string? headerText, string name)
    {
        if (string.IsNullOrWhiteSpace(headerText))
        {
            return null;
        }

        foreach (var raw in headerText.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            var colon = line.IndexOf(':');
            // 请求行（POST /agent/chat HTTP/1.1）不含冒号，直接跳过
            if (colon <= 0 || !line.AsSpan(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(colon + 1)..].Trim();
            return value.Length == 0 ? null : value;
        }

        return null;
    }
}