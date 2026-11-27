/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： WebHostAccess
*描述：宿主访问门禁的令牌、入口地址与判定规则（纯逻辑，便于单测）
*
*****************************************************************************/

namespace LubanAgentCore.Client;

/// <summary>访问门禁的判定结果。</summary>
public enum WebEntryDecision
{
    /// <summary>放行，交给 MVC 正常路由。</summary>
    Allow,

    /// <summary>令牌来自地址栏，换成 Cookie 后跳回干净地址。</summary>
    Redirect,

    /// <summary>拒绝。</summary>
    Unauthorized,

    /// <summary>浏览器侧会话已过绝对上限，需要重新获取入口地址。</summary>
    Expired
}

/// <summary>
/// 宿主访问门禁的入口规则。
/// </summary>
/// <remarks>
/// <para>
/// 宿主监听全部网卡，直接放开 <c>GET /</c> 等于把整个 Agent（读写工作区、执行命令）交给同网段任何人。
/// 因此浏览器必须先登录，凭据只接受两种携带方式：地址栏 <c>?t=</c>（首次进入，换成 Cookie 后跳回干净地址）、
/// 由登录或入口令牌换来的 <c>luban_auth</c> Cookie。
/// </para>
/// <para>
/// 两类凭据分工不同：地址栏与 Cookie 只认按用户签发的 JWT（短命、活动页面自动续期、有绝对上限）；
/// <c>Authorization: Bearer</c> 只认进程内固定的宿主令牌，客户端因此不会被浏览器会话的过期时间踢下线。
/// </para>
/// </remarks>
public static class WebHostAccess
{
    /// <summary>地址栏携带令牌的参数名。</summary>
    public const string TokenQueryName = "t";

    /// <summary>短码入口的路径前缀。</summary>
    public const string EntryCodePathPrefix = "/e/";

    /// <summary>换发凭据用的 Cookie 名。</summary>
    public const string AuthCookieName = "luban_auth";

    /// <summary>健康检查路径：客户端启动器在拿到令牌前就要探活，必须免令牌。</summary>
    public const string HealthPath = "/system/health";

    /// <summary>
    /// 生成访问令牌。
    /// </summary>
    /// <remarks>宿主固定令牌与浏览器会话令牌都用它生成，两者只是有效期不同。</remarks>
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
    /// <param name="baseAddress">监听地址，形如 <c>https://192.168.3.215:18080</c>。</param>
    /// <param name="token">浏览器会话令牌。</param>
    /// <returns>形如 <c>https://192.168.3.215:18080/?t=xxx</c>。</returns>
    public static string BuildEntryUrl(string baseAddress, string token)
        => $"{baseAddress.TrimEnd('/')}/?{TokenQueryName}={Uri.EscapeDataString(token)}";

    /// <summary>
    /// 拼出手机可直接打开的入口地址（本机监听的局域网地址）。
    /// </summary>
    /// <remarks>
    /// 手机上的 <c>127.0.0.1</c> 指向手机自己，所以对外入口一律取 <see cref="GetPreferredLanHost"/>。
    /// </remarks>
    /// <param name="port">本实例监听端口。</param>
    /// <param name="token">浏览器会话令牌。</param>
    /// <param name="https">是否以 HTTPS 提供服务。</param>
    /// <returns>形如 <c>https://192.168.3.215:18080/?t=xxx</c>。</returns>
    public static string BuildLanEntryUrl(int port, string token, bool https = true)
        => BuildEntryUrl(BuildBaseAddress(GetPreferredLanHost(), port, https), token);

    /// <summary>
    /// 拼出本机可直接打开的入口地址。
    /// </summary>
    /// <remarks>
    /// 主机固定取 <c>127.0.0.1</c>，与自述文件里的 <c>baseAddress</c> 保持同一个 origin：
    /// 终端打印的地址、<c>--token</c> 取回的地址与客户端探活的目标由此一致，浏览器也只需记住同一个站点。
    /// </remarks>
    /// <param name="port">本实例监听端口。</param>
    /// <param name="token">浏览器会话令牌。</param>
    /// <param name="https">是否以 HTTPS 提供服务。</param>
    /// <returns>形如 <c>https://127.0.0.1:18080/?t=xxx</c>。</returns>
    public static string BuildLocalEntryUrl(int port, string token, bool https)
        => BuildEntryUrl(BuildBaseAddress("127.0.0.1", port, https), token);

    /// <summary>
    /// 拼出浏览器可直接打开的短码入口地址。
    /// </summary>
    /// <remarks>
    /// 二维码里只放短码，生成矩阵的内容从约 280 字符降到 40 字符；打开后由宿主把短码换回带令牌的地址，
    /// 再走「地址栏令牌换 Cookie」的同一套门禁流程。
    /// </remarks>
    /// <param name="baseAddress">监听地址，形如 <c>https://192.168.3.215:18080</c>。</param>
    /// <param name="code">宿主进程内登记的入口短码。</param>
    /// <returns>形如 <c>https://192.168.3.215:18080/e/abc</c>。</returns>
    public static string BuildCodeEntryUrl(string baseAddress, string code)
        => $"{baseAddress.TrimEnd('/')}{EntryCodePathPrefix}{Uri.EscapeDataString(code)}";

    /// <summary>
    /// 拼出手机可直接打开的短码入口（本机监听的局域网地址）。
    /// </summary>
    /// <param name="port">本实例监听端口。</param>
    /// <param name="code">宿主进程内登记的入口短码。</param>
    /// <param name="https">是否以 HTTPS 提供服务。</param>
    /// <returns>形如 <c>https://192.168.3.215:18080/e/abc</c>。</returns>
    public static string BuildLanCodeEntryUrl(int port, string code, bool https = true)
        => BuildCodeEntryUrl(BuildBaseAddress(GetPreferredLanHost(), port, https), code);

    /// <summary>
    /// 拼出本机可直接打开的短码入口。
    /// </summary>
    /// <param name="port">本实例监听端口。</param>
    /// <param name="code">宿主进程内登记的入口短码。</param>
    /// <param name="https">是否以 HTTPS 提供服务。</param>
    /// <returns>形如 <c>https://127.0.0.1:18080/e/abc</c>。</returns>
    public static string BuildLocalCodeEntryUrl(int port, string code, bool https)
        => BuildCodeEntryUrl(BuildBaseAddress("127.0.0.1", port, https), code);

    /// <summary>
    /// 拼出 <c>scheme://host:port</c> 形式的基地址。
    /// </summary>
    /// <param name="host">主机名或 IP。</param>
    /// <param name="port">端口。</param>
    /// <param name="https">是否以 HTTPS 提供服务。</param>
    /// <returns>形如 <c>https://127.0.0.1:18080</c>。</returns>
    public static string BuildBaseAddress(string host, int port, bool https)
        => $"{(https ? "https" : "http")}://{host}:{port}";

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
    /// <param name="cookieToken">Cookie 里的浏览器会话令牌；未携带为 null。</param>
    /// <param name="authorization"><c>Authorization</c> 头原值；未携带为 null。</param>
    /// <param name="bearerToken">进程内固定的宿主令牌，只用于 <c>Bearer</c>；为空表示未启用。</param>
    /// <param name="sessions">浏览器会话令牌状态机；为空表示不接受浏览器凭据。</param>
    /// <param name="issueToken">需要回写 Cookie 的当前会话令牌；无需回写时为 null。</param>
    /// <returns>判定结果。</returns>
    public static WebEntryDecision Decide(
        string? url,
        string? entryToken,
        string? cookieToken,
        string? authorization,
        string? bearerToken,
        WebSessionTokens? sessions,
        out string? issueToken)
    {
        issueToken = null;

        // 两类凭据都没配属于启动异常：宁可全部拒绝，也不能放开
        if (string.IsNullOrEmpty(bearerToken) && sessions is null)
        {
            return WebEntryDecision.Unauthorized;
        }

        if (IsHealthPath(url))
        {
            return WebEntryDecision.Allow;
        }

        // 客户端（CLI / Codex / 插件）用固定令牌：不受浏览器会话的轮换与绝对上限影响
        if (IsBearerAuthorized(authorization, bearerToken))
        {
            return WebEntryDecision.Allow;
        }

        if (sessions is null)
        {
            return WebEntryDecision.Unauthorized;
        }

        // 地址栏带了令牌就只认它：带错令牌不该靠旧 Cookie 蒙混过关
        if (!string.IsNullOrEmpty(entryToken))
        {
            return FromSession(sessions.Check(entryToken), WebEntryDecision.Redirect, out issueToken);
        }

        return FromSession(sessions.Check(cookieToken), WebEntryDecision.Allow, out issueToken);
    }

    /// <summary>把会话令牌判定结果映射成门禁结论。</summary>
    /// <param name="check">会话令牌判定结果。</param>
    /// <param name="accepted">通过时给出的结论（地址栏入口是 <see cref="WebEntryDecision.Redirect"/>）。</param>
    /// <param name="issueToken">需要回写 Cookie 的令牌。</param>
    /// <returns>门禁结论。</returns>
    private static WebEntryDecision FromSession(
        WebSessionCheck check,
        WebEntryDecision accepted,
        out string? issueToken)
    {
        switch (check.State)
        {
            case WebSessionState.Accepted:
                issueToken = check.Token;
                return accepted;
            case WebSessionState.Expired:
                issueToken = null;
                return WebEntryDecision.Expired;
            default:
                issueToken = null;
                return WebEntryDecision.Unauthorized;
        }
    }

    /// <summary>是否携带了正确的宿主固定令牌（<c>Authorization: Bearer</c>）。</summary>
    /// <param name="authorization"><c>Authorization</c> 头原值。</param>
    /// <param name="bearerToken">进程内固定的宿主令牌。</param>
    /// <returns>头里是 Bearer 且与宿主令牌一致时返回 true。</returns>
    public static bool IsBearerAuthorized(string? authorization, string? bearerToken)
        => !string.IsNullOrEmpty(bearerToken)
           && FixedEquals(ReadBearerToken(authorization), bearerToken);

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
    /// HTTPS 下带 <c>Secure</c>，避免凭据在明文连接上传输。
    /// </remarks>
    /// <param name="token">浏览器会话令牌（轮换后的当前值）。</param>
    /// <param name="secure">是否为 HTTPS（HTTPS 时必须带 <c>Secure</c>）。</param>
    /// <returns>形如 <c>luban_auth=xxx; Path=/; HttpOnly; SameSite=Lax</c>。</returns>
    public static string BuildAuthCookie(string token, bool secure = false)
        => $"{AuthCookieName}={token}; Path=/; HttpOnly; SameSite=Lax" + (secure ? "; Secure" : string.Empty);

    /// <summary>构造清除登录 Cookie 的 <c>Set-Cookie</c> 头（登出用）。</summary>
    /// <param name="secure">是否为 HTTPS（HTTPS 时必须带 <c>Secure</c>）。</param>
    /// <returns>形如 <c>luban_auth=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0</c>。</returns>
    public static string BuildClearAuthCookie(bool secure = false)
        => $"{AuthCookieName}=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0" + (secure ? "; Secure" : string.Empty);

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
    internal static bool FixedEquals(string? actual, string expected)
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