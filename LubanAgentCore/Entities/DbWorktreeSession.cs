/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Entities
*文件名： DbWorktreeSession
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/10
*描述：worktree 会话数据库实体
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions.Models;

namespace LubanAgentCore.Entities;

/// <summary>
/// worktree 会话实体。用于崩溃/重启后的对账与恢复。
/// </summary>
[SugarTable("ai_worktree_session", "worktree 会话")]
public class DbWorktreeSession : EntityBase
{
    /// <summary>
    /// 会话ID（唯一标识）
    /// </summary>
    [SugarColumn(ColumnDescription = "会话ID", Length = 64, IsNullable = false, UniqueGroupNameList = new[] { "worktree_session_id_unique" })]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// 工作区ID
    /// </summary>
    [SugarColumn(ColumnDescription = "工作区ID", Length = 64, IsNullable = true)]
    public string? WorkspaceId { get; set; }

    /// <summary>
    /// worktree 目录绝对路径
    /// </summary>
    [SugarColumn(ColumnDescription = "worktree路径", Length = 512, IsNullable = false)]
    public string WorktreePath { get; set; } = "";

    /// <summary>
    /// 临时分支名
    /// </summary>
    [SugarColumn(ColumnDescription = "分支", Length = 256, IsNullable = false)]
    public string Branch { get; set; } = "";

    /// <summary>
    /// 基线分支名
    /// </summary>
    [SugarColumn(ColumnDescription = "基线分支", Length = 256, IsNullable = true)]
    public string? BaseBranch { get; set; }

    /// <summary>
    /// 基线提交
    /// </summary>
    [SugarColumn(ColumnDescription = "基线提交", Length = 64, IsNullable = true)]
    public string? BaseCommit { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    [SugarColumn(ColumnDescription = "创建时间", IsNullable = true)]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// 由领域模型构造实体。
    /// </summary>
    /// <param name="session">worktree 会话记录。</param>
    /// <returns>数据库实体。</returns>
    public static DbWorktreeSession From(WorktreeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new DbWorktreeSession
        {
            SessionId = session.SessionId,
            WorkspaceId = session.WorkspaceId,
            WorktreePath = session.WorktreePath,
            Branch = session.Branch,
            BaseBranch = session.BaseBranch,
BaseCommit = session.BaseCommit,
            CreatedAt = session.CreatedAt.UtcDateTime,
        };
    }

    /// <summary>
    /// 转换为领域模型。
    /// </summary>
    /// <returns>worktree 会话记录。</returns>
    public WorktreeSession ToModel() => new()
    {
        SessionId = SessionId,
        WorkspaceId = WorkspaceId ?? "",
        WorktreePath = WorktreePath,
        Branch = Branch,
        BaseBranch = BaseBranch ?? "",
        BaseCommit = BaseCommit ?? "",
        CreatedAt = CreatedAt.HasValue
            ? new DateTimeOffset(CreatedAt.Value, TimeSpan.Zero)
            : DateTimeOffset.MinValue,
    };
}