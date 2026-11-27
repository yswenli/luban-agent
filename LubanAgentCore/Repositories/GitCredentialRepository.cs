/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Repositories
*文件名： GitCredentialRepository
*描述：Git 仓库凭据仓储（按用户 + 协议 + 域名 + 端口唯一）
*
*****************************************************************************/

namespace LubanAgentCore.Repositories;

/// <summary>
/// Git 仓库凭据仓储。
/// </summary>
public class GitCredentialRepository : AgentRepository<DbGitCredential>
{
    /// <summary>创建仓储。</summary>
    /// <param name="tenantId">租户ID，默认使用系统默认租户。</param>
    public GitCredentialRepository(long tenantId = LuBanOrmConst.DefaultTenantId)
        : base(tenantId)
    {
    }

    /// <summary>
    /// 按用户与域名（协议 + 主机 + 端口）查凭据。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="scheme">协议（小写）。</param>
    /// <param name="host">域名（小写）。</param>
    /// <param name="port">端口（HTTPS 默认 443）。</param>
    /// <returns>凭据；不存在时为 <c>null</c>。</returns>
    public Task<DbGitCredential?> GetAsync(string userId, string scheme, string host, int port)
        => GetFirstAsync(c => !c.IsDelete
                              && c.UserId == userId
                              && c.Scheme == scheme
                              && c.Host == host
                              && c.Port == port);

    /// <summary>按主键查凭据（管理员视图删除时校验归属用）。</summary>
    /// <param name="id">凭据主键。</param>
    /// <returns>凭据；不存在时为 <c>null</c>。</returns>
    public Task<DbGitCredential?> GetByIdAsync(long id)
        => GetFirstAsync(c => c.Id == id && !c.IsDelete);

    /// <summary>列出某个用户保存的凭据（按更新时间倒序）。</summary>
    /// <param name="userId">用户标识。</param>
    /// <returns>凭据列表。</returns>
    public Task<List<DbGitCredential>> ListByUserAsync(string userId)
        => AsQueryable()
            .Where(c => !c.IsDelete && c.UserId == userId)
            .OrderByDescending(c => c.UpdateTime ?? c.CreateTime)
            .ToListAsync();

    /// <summary>列出全部用户保存的凭据（管理员视图，按更新时间倒序）。</summary>
    /// <returns>凭据列表。</returns>
    public Task<List<DbGitCredential>> ListAllAsync()
        => AsQueryable()
            .Where(c => !c.IsDelete)
            .OrderByDescending(c => c.UpdateTime ?? c.CreateTime)
            .ToListAsync();

    /// <summary>
    /// 新增或覆盖同一域名下的凭据。
    /// </summary>
    /// <param name="scheme">协议（小写）。</param>
    /// <param name="host">域名（小写）。</param>
    /// <param name="port">端口。</param>
    /// <param name="userId">用户标识。</param>
    /// <param name="userName">用户名。</param>
    /// <param name="secret">密码 / 令牌。</param>
    /// <returns>保存后的凭据。</returns>
    public async Task<DbGitCredential> UpsertAsync(
        string userId,
        string scheme,
        string host,
        int port,
        string userName,
        string secret)
    {
        var existing = await GetAsync(userId, scheme, host, port).ConfigureAwait(false);
        if (existing is null)
        {
            var created = new DbGitCredential
            {
                UserId = userId,
                Scheme = scheme,
                Host = host,
                Port = port,
                UserName = userName,
                Secret = secret,
                CreateTime = DateTime.Now,
            };
            await InsertAsync(created).ConfigureAwait(false);
            return created;
        }

        existing.UserName = userName;
        existing.Secret = secret;
        existing.UpdateTime = DateTime.Now;
        await UpdateAsync(existing).ConfigureAwait(false);
        return existing;
    }

    /// <summary>
    /// 按主键硬删除（凭据被删后需要能用同一域名重新写入，故不做软删除）。
    /// </summary>
    /// <param name="id">凭据主键。</param>
    /// <returns>删除的行数。</returns>
    public Task<int> DeleteByIdAsync(long id)
        => Context.Deleteable<DbGitCredential>().Where(c => c.Id == id).ExecuteCommandAsync();

    /// <summary>按用户与域名删除。</summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="scheme">协议。</param>
    /// <param name="host">域名。</param>
    /// <param name="port">端口。</param>
    /// <returns>删除的行数。</returns>
    public Task<int> DeleteAsync(string userId, string scheme, string host, int port)
        => Context.Deleteable<DbGitCredential>()
            .Where(c => c.UserId == userId && c.Scheme == scheme && c.Host == host && c.Port == port)
            .ExecuteCommandAsync();
}