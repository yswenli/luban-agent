/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
*文件名： GitBranchReader
*版本号： V1.0.0.0
*唯一标识：git 分支读取器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/14
*描述：统一的 git 分支读取，供 CLI / GUI 页脚复用；后台异步刷新 + 30s 缓存，失败统一返回"—"
*
*****************************************************************************/
using LuBan.AIAgent.Infrastructure;

namespace LubanAgentCore.Services;

/// <summary>
/// git 分支读取器：CLI / GUI 页脚共用，统一取分支命令与失败语义。
/// 采用「读缓存 + 后台异步刷新」避免阻塞 UI；失败（非仓库/未安装 git/超时）统一返回 <see cref="UnknownBranch"/>。
/// </summary>
public static class GitBranchReader
{
    /// <summary>分支不可用时的占位符（两端统一）。</summary>
    public const string UnknownBranch = "—";

    private static readonly ProcessRunner Runner = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private static volatile string _branch = UnknownBranch;
    private static DateTime _lastFetch = DateTime.MinValue;
    private static int _fetching;

    /// <summary>
    /// 当前分支名（不阻塞）。首次或缓存过期时触发一次后台刷新，本次返回旧值。
    /// </summary>
    public static string Current
    {
        get
        {
            if (DateTime.Now - _lastFetch > CacheDuration && Interlocked.CompareExchange(ref _fetching, 1, 0) == 0)
            {
                _lastFetch = DateTime.Now;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _branch = await FetchAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _fetching, 0);
                    }
                });
            }

            return _branch;
        }
    }

    /// <summary>
    /// 立即读取当前工作目录的 git 分支名；失败返回 <see cref="UnknownBranch"/>。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分支名或占位符。</returns>
    public static async Task<string> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // workingDir 显式取进程当前目录：与原始实现一致（ProcessRunner 默认取 AppContext.BaseDirectory，会跑在 bin 下导致 git 找不到仓库）
            var result = await Runner.RunAsync(
                "git",
                arguments: "",
                workingDir: Directory.GetCurrentDirectory(),
                stdin: null,
                timeoutMs: 5000,
                cancellationToken: cancellationToken,
                argumentList: ["rev-parse", "--abbrev-ref", "HEAD"],
                outputEncoding: Encoding.UTF8).ConfigureAwait(false);

            var branch = result.StandardOutput.Trim();
            return result.ExitCode == 0 && !string.IsNullOrEmpty(branch) ? branch : UnknownBranch;
        }
        catch
        {
            return UnknownBranch;
        }
    }
}