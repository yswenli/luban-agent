/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Services
*文件名： FooterDataProvider
*版本号： V1.0.0.0
*唯一标识：页脚数据提供者
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：页脚数据提供者，获取git分支、token用量、工作目录等信息
*
*****************************************************************************/
using LubanAgentCore.Services;
using LuBan.AIAgent.Sessions;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace LubanAgentCodex.Services;

/// <summary>
/// 页脚数据提供者
/// </summary>
public class FooterDataProvider
{
    private readonly IServiceProvider _services;
    
    public FooterDataProvider(IServiceProvider services)
    {
        _services = services;
    }
    
    /// <summary>
    /// 获取当前 Git 分支名称
    /// </summary>
    public string GetGitBranch()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --abbrev-ref HEAD",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var branch = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return string.IsNullOrEmpty(branch) ? "unknown" : branch;
        }
        catch
        {
            return "unknown";
        }
    }
    
    /// <summary>
    /// 获取当前会话的 Token 用量
    /// </summary>
    public long GetTokenUsage()
    {
        try
        {
            var sessionManager = _services.GetService<ISessionManager>();
            if (sessionManager?.CurrentSession == null) return 0;
            
            var stats = sessionManager.GetSessionStatsAsync(sessionManager.CurrentSession.SessionId)
                .GetAwaiter().GetResult();
            return stats?.TotalTokens ?? 0;
        }
        catch
        {
            return 0;
        }
    }
    
    /// <summary>
    /// 获取当前工作目录
    /// </summary>
    public string GetWorkingDirectory()
    {
        try
        {
            var workspaceManager = _services.GetService<IWorkspaceManager>();
            return workspaceManager?.CurrentWorkspace?.RootPath ?? Directory.GetCurrentDirectory();
        }
        catch
        {
            return Directory.GetCurrentDirectory();
        }
    }
}
