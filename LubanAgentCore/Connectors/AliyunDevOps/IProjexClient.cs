/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： IProjexClient
*描述：云效研发项目域（Projex）客户端接口（供工作台服务单测替换，ProjexClient 实现之）
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效研发项目域（Projex）客户端接口。
/// </summary>
/// <remarks>
/// <para>
/// 存在理由：<see cref="ProjexClient"/> 的方法非 virtual，工作台服务
/// （<c>YunxiaoWorkbenchService</c>）的单测无法替换它；提取本接口作测试缝合点。
/// 生产注册兼容：仍按 <see cref="ProjexClient"/> 具体类注册，
/// <c>TryAddSingleton&lt;IProjexClient&gt;</c> 桥接到同一实例。
/// </para>
/// <para>
/// <see cref="SearchProjectsWithPatAsync"/> 是接口上唯一超出既有公开面的成员：
/// 工作台的「测试连接 / 保存前探活」必须以<b>用户本次输入的 PAT</b> 发只读调用
/// （首次配置时并无已存 PAT，若走已存凭据通道探活必然失败，保存按钮永远解不开），
/// 而 <see cref="SearchProjectsAsync"/> 的鉴权头由客户端内部凭据提供者装配、不收外部 PAT，
/// 故补一个「显式 PAT 只读探活」入口。
/// </para>
/// </remarks>
public interface IProjexClient
{
    /// <summary>
    /// 搜索研发项目（按客户端内部已存凭据鉴权）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="page">页码（从 1 开始，下限 1）。</param>
    /// <param name="perPage">每页条数（上限 200，越界自动收敛）。</param>
    /// <param name="keyword">检索关键字（可选，非空时按名称模糊过滤）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>项目分页结果；空体时为 <c>null</c>。</returns>
    Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsAsync(
        string organizationId,
        int page,
        int perPage,
        string? keyword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取研发项目详情。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>项目详情；空体时为 <c>null</c>。</returns>
    Task<YunxiaoProject?> GetProjectAsync(
        string organizationId,
        string projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 以<b>显式 PAT</b> 发起只读探活搜索（不读凭据存储、不写任何配置）。
    /// </summary>
    /// <remarks>工作台「测试连接 / 保存前探活」专用：以用户本次输入的 PAT 建临时只读调用。</remarks>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="page">页码（从 1 开始，下限 1）。</param>
    /// <param name="perPage">每页条数（上限 200，越界自动收敛）。</param>
    /// <param name="keyword">检索关键字（可选，非空时按名称模糊过滤）。</param>
    /// <param name="pat">本次调用使用的 PAT（必填，空白即拒绝）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>项目分页结果；空体时为 <c>null</c>。</returns>
    Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsWithPatAsync(
        string organizationId,
        int page,
        int perPage,
        string? keyword,
        string pat,
        CancellationToken cancellationToken = default);
}
