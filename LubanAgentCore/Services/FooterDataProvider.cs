/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： FooterDataProvider
*版本号： V1.0.0.0
*唯一标识：页脚数据提供者
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：聚合 git 分支、token 用量、模式名称等页脚元数据
*
*****************************************************************************/
using System.Diagnostics;
using LuBan.AIAgent.Abstractions;

namespace LubanAgentCore.Services;

/// <summary>
/// 页脚数据提供者。聚合 git 分支、token 用量、当前权限模式等页脚元数据。
/// 步骤 7 简化版——直接读取系统状态，未使用 IFooterDataProvider 扩展架构。
/// </summary>
public sealed class FooterDataProvider
{
    private volatile string _gitBranch = "—";
    private DateTime _lastGitFetch = DateTime.MinValue;
    private volatile bool _gitFetchInProgress;
    private static readonly TimeSpan GitBranchCacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>当前权限模式可读名称。</summary>
    public string ModeDisplay { get; set; } = "default";

    /// <summary>累计 token 用量（调用方通过 RecordUsage 更新）。</summary>
    public int TotalTokens { get; set; }

    /// <summary>当前工作目录的 git 分支名。</summary>
    public string GitBranch
    {
        get
        {
            if (DateTime.Now - _lastGitFetch <= GitBranchCacheDuration)
            {
                return _gitBranch;
            }

            if (!_gitFetchInProgress)
            {
                _gitFetchInProgress = true;
                _lastGitFetch = DateTime.Now;

                try
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            var branch = TryGetGitBranch();
                            _gitBranch = branch;
                        }
                        finally
                        {
                            _gitFetchInProgress = false;
                        }
                    });
                }
                catch
                {
                    _gitFetchInProgress = false;
                    throw;
                }
            }

            return _gitBranch;
        }
    }

    /// <summary>后台任务数（暂返回 0，Agent View 三期接入）。</summary>
    public int BackgroundTasks => 0;

    /// <summary>
    /// 尝试通过 git 命令获取当前分支名，失败时返回"—"
    /// </summary>
    private static string TryGetGitBranch()
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --abbrev-ref HEAD",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(1000);

            return proc.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : "—";
        }
        catch
        {
            return "—";
        }
    }
}
