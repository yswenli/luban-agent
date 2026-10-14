/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Infrastructure
*文件名： LegacyUserDataMigrator
*版本号： V1.0.0.0
*创建时间：2026/10/13
*描述：历史单用户数据归属迁移（会话/工作区 UserId 归一化到默认用户）
*
*****************************************************************************/
using LubanAgentCore.Hosting;

namespace LubanAgentCore.Infrastructure;

/// <summary>
/// 历史单用户数据归属迁移：将 Session/Workspace 中 UserId 为 NULL/''/'default' 的记录
/// 归一化为当前默认本地用户，保证升级后原有会话/工作区可见。
/// </summary>
public static class LegacyUserDataMigrator
{
    private static int _done;

    /// <summary>执行一次迁移（幂等；进程内只跑一次）。</summary>
    public static void Migrate()
    {
        if (Interlocked.Exchange(ref _done, 1) == 1) return;
        var uid = LocalUserDefaults.DefaultUserId;
        try
        {
            // 表名/列名以 SqlSugar 实体为准：DbSession→ai_session、DbWorkspace→ai_workspace；
            // 列名受 DbSettings.EnableUnderLine 影响，实体属性 UserId 落库为下划线 user_id，
            // 故此处原始 SQL 必须用 user_id（用 PascalCase 会报 "no such column: UserId" 而静默失败）。
            var sessionRepo = new Repositories.SessionRepository();
            sessionRepo.Context.Ado.ExecuteCommand(
                "UPDATE ai_session SET user_id = @uid WHERE user_id IS NULL OR user_id = '' OR user_id = @legacy",
                new { uid, legacy = LocalUserDefaults.LegacyUserId });

            var workspaceRepo = new Repositories.WorkspaceRepository();
            workspaceRepo.Context.Ado.ExecuteCommand(
                "UPDATE ai_workspace SET user_id = @uid WHERE user_id IS NULL OR user_id = '' OR user_id = @legacy",
                new { uid, legacy = LocalUserDefaults.LegacyUserId });
        }
        catch (Exception ex)
        {
            Logger.Error("历史用户数据迁移失败", ex);
        }
    }
}