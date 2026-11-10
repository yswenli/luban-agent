/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： WebHostAccess
*描述：宿主访问门禁的令牌、入口地址与判定规则（纯逻辑，便于单测）
*
*****************************************************************************/

using System.Net.NetworkInformation;

namespace LubanAgentCore.Client;

/// <summary>访问门禁的判定结果。</summary>
public enum WebEntryDecision
{
    /// <summary>放行，交给 MVC 正常路由。</summary>
    Allow,

    /// <summary>令牌来自地址栏，换成 Cookie 后跳回干净地址。</summary>
    Redirect,

    /// <summary>拒绝。</summary>
    Unauthorized
}

/// <summary>
/// 宿主访问门禁的入口规则。
/// </summary>
/// <remarks>
/// <para>
/// 宿主监听全部网卡，直接放开 <c>GET /</c> 等于把整个 Agent（读写工作区、执行命令）交给同网段任何人。
/// 因此进程启动即生成一次性令牌，只接受三种携带方式：地址栏 <c>?t=</c>（首次进入）、
/// 由它换来的 Cookie（浏览器后续请求）、<c>Authorization: Bearer</c>（CLI / Codex / 插件）。
/// </para>
/// <para>
/// 令牌随进程生命周期存在，重启即失效；不做长期登录态。
/// </para>
/// </remarks>
public static class WebHostAccess
{
    /// <summary>地址栏携带令牌的参数名。</summary>
    public const string TokenQueryName = "t";

    /// <summary>换发凭据用的 Cookie 名。</summary>
    public const string AuthCookieName = "luban_auth";

    /// <summary>健康检查路径：客户端启动器在拿到令牌前就要探活，必须免令牌。</summary>
    public const string HealthPath = "/system/health";

    /// <summary>
    /// 生成一次性的访问令牌。
    /// </summary>
    /// <returns>32 字节随机数的 Base64Url 文本（43 个字符，可直接进 URL 与 Cookie）。</returns>
    public static string CreateToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// 校验令牌能否直接进地址栏与 <c>Set-Cookie</c> 头。
    /// </summary>
    /// <remarks>
    /// 令牌会原样写进这两处：含空格或分号会把 Cookie 截断，含换行则可能注入额外的响应头。
    /// 因此只接受 Base64Url 字符集（字母、数字、<c>-</c>、<c>_</c>），固定令牌时请照此填写。
    /// </remarks>
    /// <param name="token">待校验的令牌。</param>
    /// <returns>非空且只含 Base64Url 字符时返回 true。</returns>
    public static bool IsValidToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        foreach (var ch in token)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '-' && ch != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 拼出浏览器可直接打开的入口地址。
    /// </summary>
    /// <param name="baseAddress">监听地址，形如 <c>http://192.168.3.215:18080</c>。</param>
    /// <param name="token">本进程令牌。</param>
    /// <returns>形如 <c>http://192.168.3.215:18080/?t=xxx</c>。</returns>
    public static string BuildEntryUrl(string baseAddress, string token)
        => $"{baseAddress.TrimEnd('/')}/?{TokenQueryName}={Uri.EscapeDataString(token)}";

    /// <summary>
    /// 取一个适合在局域网内打开的 IPv4 地址。
    /// </summary>
    /// <remarks>
    /// 只看已启用、非环回的 IPv4，跳过虚拟网卡（VPN 的 TAP、Hyper-V/WSL、VMware、VirtualBox）
    /// 与 <c>169.254.*</c> 链路本地地址——它们的地址虽然「能连本机」，但同网段的其它设备连不上。
    /// 剩下的按私网段优先级排序（192.168 &gt; 10 &gt; 172.16-31 &gt; 其他）；
    /// 一个都取不到时退回 <c>127.0.0.1</c>。
    /// </remarks>
    /// <returns>IPv4 文本。</returns>
    public static string GetPreferredLanHost()
    {
        var candidates = new List<(int Rank, string Address)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    || IsVirtualAdapter(nic.Name, nic.Description))
                {
                    continue;
                }

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
                        || System.Net.IPAddress.IsLoopback(unicast.Address)
                        || IsLinkLocal(unicast.Address))
                    {
                        continue;
                    }

                    var address = unicast.Address.ToString();
                    candidates.Add((Rank(address), address));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // 取不到网卡信息时退回环回地址，不阻断启动
        }

        return candidates
            .OrderBy(static item => item.Rank)
            .ThenBy(static item => item.Address, StringComparer.Ordinal)
            .Select(static item => item.Address)
            .FirstOrDefault() ?? "127.0.0.1";
    }

    /// <summary>
    /// 是否为不该出现在入口地址里的虚拟网卡。
    /// </summary>
    /// <remarks>
    /// VPN 的 TAP 适配器与 Hyper-V/WSL 的虚拟交换机会给出 <c>192.168.*</c> / <c>172.16-31.*</c> 之类的地址，
    /// 排序上会被误当成局域网；同网段的其它设备并不在这个网段里，打开就是连不上。
    /// </remarks>
    /// <param name="name">网卡名（如 <c>本地连接</c>）。</param>
    /// <param name="description">网卡描述（如 <c>TAP-Windows Adapter V9</c>）。</param>
    /// <returns>是虚拟/隧道网卡则返回 true。</returns>
    public static bool IsVirtualAdapter(string? name, string? description)
    {
        var text = $"{name} {description}";
        string[] keywords =
        [
            "TAP-Windows",
            "OpenVPN",
            "WireGuard",
            "Hyper-V",
            "WSL",
            "vEthernet",
            "VMware",
            "VirtualBox",
            "Tailscale",
            "ZeroTier",
            "Pseudo-Interface",
            "Tunnel"
        ];

        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLinkLocal(System.Net.IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    /// <summary>
    /// 判定一个请求是否放行。
    /// </summary>
    /// <param name="url">请求路径（不含查询串，形如 <c>/session/list</c>）。</param>
    /// <param name="entryToken">地址栏 <c>?t=</c> 的值；未携带为 null。</param>
    /// <param name="cookieToken">Cookie 里的凭据值；未携带为 null。</param>
    /// <param name="authorization"><c>Authorization</c> 头原值；未携带为 null。</param>
    /// <param name="expectedToken">本进程令牌；为空表示配置异常。</param>
    /// <returns>判定结果。</returns>
    public static WebEntryDecision Decide(
        string? url,
        string? entryToken,
        string? cookieToken,
        string? authorization,
        string? expectedToken)
    {
        // 令牌缺失属于启动异常：宁可全部拒绝，也不能放开
        if (string.IsNullOrEmpty(expectedToken))
        {
            return WebEntryDecision.Unauthorized;
        }

        if (IsHealthPath(url))
        {
            return WebEntryDecision.Allow;
        }

        // 地址栏带了令牌就只认它：带错令牌不该靠旧 Cookie 蒙混过关
        if (!string.IsNullOrEmpty(entryToken))
        {
            return FixedEquals(entryToken, expectedToken)
                ? WebEntryDecision.Redirect
                : WebEntryDecision.Unauthorized;
        }

        return IsAuthorized(cookieToken, authorization, expectedToken)
            ? WebEntryDecision.Allow
            : WebEntryDecision.Unauthorized;
    }

    /// <summary>已持有凭据（Cookie 或 Bearer）时是否放行。</summary>
    /// <param name="cookieToken">Cookie 里的凭据值。</param>
    /// <param name="authorization"><c>Authorization</c> 头原值。</param>
    /// <param name="expectedToken">本进程令牌。</param>
    /// <returns>任一凭据与令牌一致则返回 true。</returns>
    public static bool IsAuthorized(string? cookieToken, string? authorization, string? expectedToken)
    {
        if (string.IsNullOrEmpty(expectedToken))
        {
            return false;
        }

        return FixedEquals(cookieToken, expectedToken)
            || FixedEquals(ReadBearerToken(authorization), expectedToken);
    }

    /// <summary>是否为免令牌的健康检查路径。</summary>
    /// <param name="url">请求路径。</param>
    /// <returns>是则返回 true。</returns>
    public static bool IsHealthPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var path = url.Trim();
        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        return string.Equals(path, HealthPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从 <c>Authorization</c> 头中取 Bearer 令牌。</summary>
    /// <param name="authorization">头原值。</param>
    /// <returns>令牌文本；头为空或不是 Bearer 时返回 null。</returns>
    public static string? ReadBearerToken(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return null;
        }

        const string prefix = "Bearer ";
        var value = authorization.Trim();
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? Blank(value[prefix.Length..])
            : null;
    }

    /// <summary>构造换发凭据的 <c>Set-Cookie</c> 头。</summary>
    /// <remarks>
    /// 走原始头而不是 SAEA 的 <c>HttpCookie</c>：后者会把 <c>Host</c>（含端口）塞进 <c>Domain</c>，
    /// 浏览器可能整条拒收；不带 Domain 即按主机名生效，IP 访问同样可用。
    /// </remarks>
    /// <param name="token">本进程令牌。</param>
    /// <returns>形如 <c>luban_auth=xxx; Path=/; HttpOnly; SameSite=Lax</c>。</returns>
    public static string BuildAuthCookie(string token)
        => $"{AuthCookieName}={token}; Path=/; HttpOnly; SameSite=Lax";

    /// <summary>去掉请求目标里的令牌参数，得到可写进 <c>Location</c> 的干净地址。</summary>
    /// <param name="relativeUrl">原始请求目标（可含查询串）。</param>
    /// <returns>形如 <c>/</c> 或 <c>/#/s/1</c> 之前的部分。</returns>
    public static string StripTokenQuery(string? relativeUrl)
    {
        if (string.IsNullOrEmpty(relativeUrl))
        {
            return "/";
        }

        var index = relativeUrl.IndexOf('?');
        var path = index < 0 ? relativeUrl : relativeUrl[..index];

        var kept = new List<string>();
        if (index >= 0)
        {
            foreach (var pair in relativeUrl[(index + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = pair.Split('=', 2)[0];
                if (string.Equals(Uri.UnescapeDataString(name), TokenQueryName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                kept.Add(pair);
            }
        }

        var target = path.Length == 0 ? "/" : path;
        return kept.Count == 0 ? target : $"{target}?{string.Join("&", kept)}";
    }

    /// <summary>
    /// 常数时间比对令牌：避免按字符逐位比较泄露前缀匹配长度。
    /// </summary>
    /// <param name="actual">请求带来的凭据。</param>
    /// <param name="expected">本进程令牌。</param>
    /// <returns>两者完全一致（区分大小写）时返回 true。</returns>
    private static bool FixedEquals(string? actual, string expected)
    {
        if (string.IsNullOrEmpty(actual))
        {
            return false;
        }

        var left = System.Text.Encoding.UTF8.GetBytes(actual);
        var right = System.Text.Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string? Blank(string value)
    {
        var text = value.Trim();
        return text.Length == 0 ? null : text;
    }

    private static int Rank(string address)
    {
        if (address.StartsWith("192.168.", StringComparison.Ordinal)) return 0;
        if (address.StartsWith("10.", StringComparison.Ordinal)) return 1;
        if (address.StartsWith("172.", StringComparison.Ordinal) && IsPrivate172(address)) return 2;
        return 3;
    }

    private static bool IsPrivate172(string address)
    {
        var parts = address.Split('.');
        return parts.Length > 1 && int.TryParse(parts[1], out var second) && second >= 16 && second <= 31;
    }
}