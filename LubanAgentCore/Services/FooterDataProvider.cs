/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Services
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
using LuBan.AIAgent.Abstractions;

namespace LubanAgentCore.Services;

/// <summary>
/// 页脚数据提供者。聚合 git 分支、token 用量、当前权限模式等页脚元数据。
/// 步骤 7 简化版——直接读取系统状态，未使用 IFooterDataProvider 扩展架构。
/// </summary>
public sealed class FooterDataProvider
{
    private string _gitBranch = GitBranchReader.UnknownBranch;

    /// <summary>当前权限模式可读名称。</summary>
    public string ModeDisplay { get; set; } = "default";

    /// <summary>当前工作区名称（由会话视图在上下文切换时更新）。</summary>
    public string WorkspaceName { get; set; } = "";

    /// <summary>当前模型标识（由会话视图在上下文切换时更新）。</summary>
    public string ModelName { get; set; } = "";

    /// <summary>当前会话标题（由会话视图在上下文切换时更新）。</summary>
    public string SessionTitle { get; set; } = "";

    /// <summary>累计 token 用量（调用方通过 RecordUsage 更新）。</summary>
    public int TotalTokens { get; set; }

    /// <summary>当前工作目录的 git 分支名。</summary>
    public string GitBranch
    {
        get
        {
            var branch = GitBranchReader.Current;
            if (!string.IsNullOrEmpty(branch))
            {
                _gitBranch = branch;
            }

            return _gitBranch;
        }
    }

    /// <summary>后台任务数（暂返回 0，Agent View 三期接入）。</summary>
    public int BackgroundTasks => 0;
}
