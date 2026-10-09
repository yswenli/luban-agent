/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Repositories
*文件名： A2ATaskRepository
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：A2A 任务仓储
*
*****************************************************************************/
namespace LubanAgentCore.Repositories;

/// <summary>
/// A2A 任务仓储
/// </summary>
public class A2ATaskRepository : BaseRepository<DbA2ATask>
{
    /// <summary>
    /// 创建 A2A 任务仓储实例。
    /// </summary>
    /// <param name="tenantId">租户ID，默认使用系统默认租户</param>
    public A2ATaskRepository(long tenantId = LuBanOrmConst.DefaultTenantId)
        : base(tenantId)
    {
    }

    /// <summary>
    /// 根据任务ID获取任务
    /// </summary>
    public async Task<DbA2ATask?> GetByTaskIdAsync(string taskId)
    {
        return await GetFirstAsync(t => t.TaskId == taskId && !t.IsDelete);
    }

    /// <summary>
    /// 按任务ID新增或更新任务快照
    /// </summary>
    public async Task UpsertAsync(DbA2ATask entity)
    {
        var existing = await GetFirstAsync(t => t.TaskId == entity.TaskId);
        if (existing == null)
        {
            await InsertAsync(entity);
            return;
        }

        entity.Id = existing.Id;
        await UpdateAsync(entity);
    }

    /// <summary>
    /// 按任务ID物理删除任务
    /// </summary>
    public async Task<bool> DeleteByTaskIdAsync(string taskId)
    {
        return await DeleteAsync(t => t.TaskId == taskId);
    }

    /// <summary>
    /// 按条件分页查询任务（按状态时间戳倒序）
    /// </summary>
    /// <param name="contextId">上下文ID过滤，为空则不过滤</param>
    /// <param name="state">任务状态序数值过滤，为空则不过滤</param>
    /// <param name="statusTimestampAfter">仅返回状态时间戳晚于该时刻的任务</param>
    /// <param name="skip">跳过的记录数</param>
    /// <param name="take">返回的记录数</param>
    public async Task<(List<DbA2ATask> Items, int Total)> QueryAsync(
        string? contextId, int? state, DateTime? statusTimestampAfter, int skip, int take)
    {
        var query = AsQueryable().Where(t => !t.IsDelete);

        if (!string.IsNullOrEmpty(contextId))
            query = query.Where(t => t.ContextId == contextId);

        if (state.HasValue)
            query = query.Where(t => t.State == state.Value);

        if (statusTimestampAfter.HasValue)
            query = query.Where(t => t.StatusTimestamp != null && t.StatusTimestamp > statusTimestampAfter.Value);

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(t => t.StatusTimestamp, OrderByType.Desc)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return (items, total);
    }
}