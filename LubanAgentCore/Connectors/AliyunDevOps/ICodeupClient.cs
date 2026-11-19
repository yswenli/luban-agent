/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： ICodeupClient
*描述：云效代码管理（Codeup）客户端接口（供工作台服务单测替换，CodeupClient 实现之）
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效代码管理（Codeup）客户端接口。
/// </summary>
/// <remarks>
/// 存在理由（同 <see cref="IProjexClient"/> 先例）：<see cref="CodeupClient"/> 的方法非 virtual，
/// S2 工作台服务（项目初始化）的单测无法替换它；提取本接口作测试缝合点。
/// 生产注册兼容：仍按 <see cref="CodeupClient"/> 具体类注册，
/// <c>TryAddSingleton&lt;ICodeupClient&gt;</c> 桥接到同一实例。公开面与 <see cref="CodeupClient"/> 逐签名一致。
/// </remarks>
public interface ICodeupClient
{
    /// <summary>搜索仓库（响应头 <c>x-total</c> 承载总数，合成为分页结果）。</summary>
    /// <param name="search">名称检索关键字（可选）。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="perPage">每页条数。</param>
    /// <param name="orderBy">排序字段（可选）。</param>
    /// <param name="sort">排序方向（可选）。</param>
    /// <param name="archived">是否包含已归档仓库（可选）。</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>仓库分页列表；响应体为空时为 <c>null</c>。</returns>
    Task<YunxiaoPagedResult<YunxiaoRepository>?> SearchRepositoriesAsync(
        string? search = null,
        int page = 1,
        int perPage = 20,
        string? orderBy = null,
        string? sort = null,
        bool? archived = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>获取仓库详情。</summary>
    /// <param name="repoId">仓库 ID。</param>
    /// <param name="organizationId">组织 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>仓库详情；响应体为空时为 <c>null</c>。</returns>
    Task<YunxiaoRepository?> GetRepositoryAsync(
        string repoId,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>创建仓库。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="organizationId">组织 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建的仓库；响应体为空时为 <c>null</c>。</returns>
    Task<YunxiaoRepository?> CreateRepositoryAsync(
        YunxiaoCreateRepositoryRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>更新仓库。</summary>
    /// <param name="repoId">仓库 ID。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="organizationId">组织 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的仓库；响应体为空时为 <c>null</c>。</returns>
    Task<YunxiaoRepository?> UpdateRepositoryAsync(
        string repoId,
        YunxiaoUpdateRepositoryRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>列出仓库分支（含 <c>defaultBranch</c> 标记）。</summary>
    /// <param name="repositoryId">仓库 ID（数字）。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="perPage">每页条数。</param>
    /// <param name="sort">排序字段（可选）。</param>
    /// <param name="search">检索关键字（可选）。</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分支列表。</returns>
    Task<List<YunxiaoBranch>?> ListBranchesAsync(
        long repositoryId,
        int page = 1,
        int perPage = 100,
        string? sort = null,
        string? search = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>列出模板仓库（定位母版库的正道入口）。</summary>
    /// <param name="templateType">模板类型（默认 1）。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="perPage">每页条数。</param>
    /// <param name="organizationId">组织 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模板仓库列表。</returns>
    Task<List<YunxiaoTemplateRepository>?> ListTemplateRepositoriesAsync(
        int templateType = 1,
        int page = 1,
        int perPage = 100,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>列出代码组。</summary>
    /// <param name="search">检索关键字（可选）。</param>
    /// <param name="parentId">父代码组 ID（可选）。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="perPage">每页条数。</param>
    /// <param name="orderBy">排序字段（可选）。</param>
    /// <param name="sort">排序方向（可选）。</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>代码组列表。</returns>
    Task<List<YunxiaoNamespace>?> ListNamespacesAsync(
        string? search = null,
        long? parentId = null,
        int page = 1,
        int perPage = 100,
        string? orderBy = null,
        string? sort = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>创建代码组。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="organizationId">组织 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建的代码组；响应体为空时为 <c>null</c>。</returns>
    Task<YunxiaoNamespace?> CreateNamespaceAsync(
        YunxiaoCreateNamespaceRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default);
}
