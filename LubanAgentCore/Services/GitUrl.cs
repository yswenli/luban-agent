/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Services
*文件名： GitUrl
*描述：仓库地址解析：识别协议、域名与端口，供凭据按域名匹配
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// 仓库地址协议。
/// </summary>
public enum GitUrlKind
{
    /// <summary>HTTPS（当前网页端唯一支持的凭据协议）。</summary>
    Https,

    /// <summary>HTTP（明文，仅兼容解析，网页端不提供）。</summary>
    Http,

    /// <summary>SSH（暂不支持网页录入凭据，仅解析）。</summary>
    Ssh,

    /// <summary>git:// 协议。</summary>
    Git,

    /// <summary>本地 file:// 地址。</summary>
    File,
}

/// <summary>
/// 解析后的仓库地址。
/// </summary>
/// <param name="Url">原始地址（已去首尾空白）。</param>
/// <param name="Kind">协议。</param>
/// <param name="Scheme">协议名（小写）。</param>
/// <param name="Host">域名（小写）；file 地址为空串。</param>
/// <param name="Port">端口；0 表示该协议无端口。</param>
/// <param name="UserName">地址中携带的用户名；仅 SSH 形式允许。</param>
public sealed record GitUrlInfo(
    string Url,
    GitUrlKind Kind,
    string Scheme,
    string Host,
    int Port,
    string? UserName)
{
    /// <summary>是否 HTTPS。</summary>
    public bool IsHttps => Kind == GitUrlKind.Https;

    /// <summary>该协议是否支持用户名 + Token 凭据（当前只有 HTTPS 在网页端开放）。</summary>
    public bool SupportsCredential => Kind is GitUrlKind.Https or GitUrlKind.Http;
}

/// <summary>
/// 仓库地址解析。
/// </summary>
/// <remarks>
/// 只做协议识别与域名/端口归一化，不校验仓库是否可达。
/// HTTP(S) 地址中的 <c>user:pass@</c> 一律拒绝：凭据必须走独立字段，
/// 避免明文进入 URL、日志与进程参数。
/// </remarks>
public static class GitUrl
{
    /// <summary>scp 形式：<c>user@host:path</c>。</summary>
    private static readonly System.Text.RegularExpressions.Regex ScpPattern = new(
        "^[A-Za-z0-9._-]+@[A-Za-z0-9._-]+:.+$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// 解析仓库地址。
    /// </summary>
    /// <param name="url">用户输入的地址。</param>
    /// <param name="info">解析结果。</param>
    /// <returns>可识别时为 <c>true</c>。</returns>
    public static bool TryParse(string? url, out GitUrlInfo info)
    {
        info = null!;
        var text = (url ?? string.Empty).Trim();
        if (text.Length == 0 || text[0] == '-' || text.Contains('\r') || text.Contains('\n'))
        {
            return false;
        }

        if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseHttp(text, GitUrlKind.Https, 443, out info);
        }

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseHttp(text, GitUrlKind.Http, 80, out info);
        }

        if (text.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseSshUri(text, out info);
        }

        if (ScpPattern.IsMatch(text))
        {
            var at = text.IndexOf('@');
            var colon = text.IndexOf(':', at + 1);
            var host = text[(at + 1)..colon];
            info = new GitUrlInfo(text, GitUrlKind.Ssh, "ssh", NormalizeHost(host), 22, text[..at]);
            return info.Host.Length > 0;
        }

        if (text.StartsWith("git://", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseUri(text, GitUrlKind.Git, "git", 9418, out info);
        }

        if (text.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseFile(text, out info);
        }

        return false;
    }

    /// <summary>HTTP(S)：拒绝地址内携带用户名或密码。</summary>
    private static bool TryParseHttp(string text, GitUrlKind kind, int defaultPort, out GitUrlInfo info)
    {
        info = null!;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.UserInfo.Length > 0)
        {
            return false;
        }

        var host = NormalizeHost(uri.Host);
        if (host.Length == 0)
        {
            return false;
        }

        info = new GitUrlInfo(text, kind, kind == GitUrlKind.Https ? "https" : "http", host, uri.Port > 0 ? uri.Port : defaultPort, null);
        return true;
    }

    /// <summary>SSH URI：允许 <c>user@host</c>，不允许密码。</summary>
    private static bool TryParseSshUri(string text, out GitUrlInfo info)
    {
        info = null!;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // 密码写在 SSH 地址里同样不允许（用户名是正常信息，密码不是）
        if (uri.UserInfo.Contains(':'))
        {
            return false;
        }

        var host = NormalizeHost(uri.Host);
        if (host.Length == 0)
        {
            return false;
        }

        var userName = uri.UserInfo.Length > 0 ? Uri.UnescapeDataString(uri.UserInfo) : null;
        info = new GitUrlInfo(text, GitUrlKind.Ssh, "ssh", host, uri.Port > 0 ? uri.Port : 22, userName);
        return true;
    }

    /// <summary>git://。</summary>
    private static bool TryParseUri(string text, GitUrlKind kind, string scheme, int port, out GitUrlInfo info)
    {
        info = null!;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            return false;
        }

        info = new GitUrlInfo(text, kind, scheme, NormalizeHost(uri.Host), uri.Port > 0 ? uri.Port : port, null);
        return true;
    }

    /// <summary>file:// 本地仓库。</summary>
    private static bool TryParseFile(string text, out GitUrlInfo info)
    {
        info = null!;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return false;
        }

        info = new GitUrlInfo(text, GitUrlKind.File, "file", "", 0, null);
        return true;
    }

    /// <summary>域名统一小写，去掉尾部的点。</summary>
    private static string NormalizeHost(string host)
        => (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
}