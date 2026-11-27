/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Services
*文件名： GitErrorClassifier
*描述：把 git 的 stderr 归类成前端可处理的失败原因
*
*****************************************************************************/

namespace LubanAgentCore.Services;

/// <summary>
/// git 失败原因。
/// </summary>
public enum GitFailureKind
{
    /// <summary>未知失败。</summary>
    Unknown,

    /// <summary>认证失败（用户名/令牌错误或权限不足）。</summary>
    Auth,

    /// <summary>仓库不存在或当前账号无权限看到。</summary>
    NotFound,

    /// <summary>网络 / DNS / TLS / 连接失败。</summary>
    Network,

    /// <summary>超时。</summary>
    Timeout,
}

/// <summary>
/// git 错误归类：只做文本匹配，便于单测与前后端共用同一套 reason。
/// </summary>
public static class GitErrorClassifier
{
    /// <summary>认证失败特征。</summary>
    private static readonly string[] AuthMarkers =
    [
        "authentication failed",
        "could not read username",
        "could not read password",
        "terminal prompts disabled",
        "invalid username or password",
        "http basic: access denied",
        "permission denied",
        "access denied",
        "401 unauthorized",
        "returned error: 401",
        "returned error: 403",
        "the requested url returned error: 401",
        "the requested url returned error: 403",
        "remote: unauthorized",
    ];

    /// <summary>仓库不存在特征。</summary>
    private static readonly string[] NotFoundMarkers =
    [
        "repository not found",
        "does not appear to be a git repository",
        "not found",
        "returned error: 404",
        "the requested url returned error: 404",
        "no such file or directory",
        "remote: not found",
    ];

    /// <summary>网络失败特征。</summary>
    private static readonly string[] NetworkMarkers =
    [
        "could not resolve host",
        "could not resolve hostname",
        "failed to connect",
        "connection refused",
        "connection timed out",
        "operation timed out",
        "ssl certificate problem",
        "unable to access",
        "network is unreachable",
        "could not read from remote repository",
        "early eof",
        "rpc failed",
    ];

    /// <summary>
    /// 归类失败原因。
    /// </summary>
    /// <param name="standardError">git 错误输出。</param>
    /// <param name="standardOutput">git 标准输出（部分服务端把提示写在这里）。</param>
    /// <param name="timedOut">是否因超时被终止。</param>
    /// <returns>失败原因。</returns>
    public static GitFailureKind Classify(string? standardError, string? standardOutput, bool timedOut)
    {
        if (timedOut)
        {
            return GitFailureKind.Timeout;
        }

        var text = $"{standardError}\n{standardOutput}".ToLowerInvariant();
        if (text.Trim().Length == 0)
        {
            return GitFailureKind.Unknown;
        }

        // 认证先判：401/403 的文本里也可能出现 "unable to access"，不能落到网络类
        if (AuthMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            return GitFailureKind.Auth;
        }

        if (NotFoundMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            return GitFailureKind.NotFound;
        }

        if (NetworkMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            return GitFailureKind.Network;
        }

        return GitFailureKind.Unknown;
    }

    /// <summary>
    /// 失败原因对应的前端 reason。
    /// </summary>
    /// <param name="kind">失败原因。</param>
    /// <returns><c>git-auth</c> / <c>git-not-found</c> / <c>git-network</c> / <c>git-timeout</c> / <c>git-unknown</c>。</returns>
    public static string ToReason(GitFailureKind kind) => kind switch
    {
        GitFailureKind.Auth => "git-auth",
        GitFailureKind.NotFound => "git-not-found",
        GitFailureKind.Network => "git-network",
        GitFailureKind.Timeout => "git-timeout",
        _ => "git-unknown",
    };
}