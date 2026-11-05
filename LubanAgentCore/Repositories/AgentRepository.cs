/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Repositories
*文件名： AgentRepository
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/4
*描述：每次访问取独立数据库连接的仓储基类
*
*****************************************************************************/

using LuBan.Orm.Interfaces;
using LuBan.Orm.Models;

using SqlSugar;

namespace LubanAgentCore.Repositories;

/// <summary>
/// 每次访问 <c>Context</c> 都取一个独立数据库连接的仓储基类。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LuBan.Orm.BaseRepository{TEntity}"/> 在构造时把 <c>Context</c> 固定为当时解析到的
/// <see cref="SqlSugarScopeProvider"/>。仓储实例在宿主内按用户共享（单例），
/// 该固定上下文被多个请求/后台任务同时使用时，会共用同一个底层连接，
/// 表现为读取器被提前关闭（<c>Invalid attempt to call GetName when reader is closed</c>）、
/// 列绑定错位（<c>xxx绑定到DbYyy失败</c>）等并发错误。
/// </para>
/// <para>
/// SqlSugar 自带的按异步上下文分发依赖 <c>AsyncLocal</c>；宿主（SAEA.MVC）的请求处理共享同一条
/// 执行上下文链，该分发无法隔离并发请求，故此处改为每次取独立连接：
/// <see cref="LuBanOrm.GetIsolatedClient{TEntity}"/> 会复制连接配置并重放过滤器/默认值/日志配置，
/// 且强制 <c>IsAutoCloseConnection=true</c>，命令结束即归还连接，不与其它请求共享连接状态。
/// </para>
/// </remarks>
/// <typeparam name="TEntity">实体类型。</typeparam>
public abstract class AgentRepository<TEntity> : BaseRepository<TEntity>
    where TEntity : EntityBase, IDeletedFilter, new()
{
    private readonly string _tenantId;

    /// <summary>
    /// 创建仓储实例。
    /// </summary>
    /// <param name="tenantId">租户ID，默认使用系统默认租户。</param>
    protected AgentRepository(long tenantId = LuBanOrmConst.DefaultTenantId)
        : base(tenantId)
        => _tenantId = tenantId.ToString();

    /// <summary>
    /// 数据库上下文：每次访问都取一个独立连接（<see cref="LuBanOrm.GetIsolatedClient{TEntity}"/>）。
    /// </summary>
    public override ISqlSugarClient Context => LuBanOrm.GetIsolatedClient<TEntity>(_tenantId);
}