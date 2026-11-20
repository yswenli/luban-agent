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

    /// <summary>搜索工作项（服务端条件过滤）。</summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="criteria">搜索条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果；空体时为 <c>null</c>。</returns>
    Task<YunxiaoPagedResult<YunxiaoWorkitem>?> SearchWorkitemsAsync(
        string organizationId,
        YunxiaoWorkitemSearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>获取工作项详情（含描述与自定义字段值）。</summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="workitemId">工作项 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作项详情；空体时为 <c>null</c>。</returns>
    Task<YunxiaoWorkitem?> GetWorkitemAsync(
        string organizationId,
        string workitemId,
        CancellationToken cancellationToken = default);

    /// <summary>创建工作项。</summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建工作项 ID；响应缺 id 时为 <c>null</c>。</returns>
    Task<string?> CreateWorkitemAsync(
        string organizationId,
        YunxiaoCreateWorkitemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>获取项目下的工作项类型。</summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="category">分类：<c>Req</c> / <c>Bug</c> / <c>Task</c>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作项类型列表；空体时为空列表。</returns>
    Task<List<YunxiaoWorkitemType>> ListWorkitemTypesAsync(
        string organizationId,
        string projectId,
        string category,
        CancellationToken cancellationToken = default);

    /// <summary>获取工作项附件列表。</summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="workitemId">工作项 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>附件列表；空体时为空列表。</returns>
    Task<List<YunxiaoWorkitemAttachment>> ListWorkitemAttachmentsAsync(
        string organizationId,
        string workitemId,
        CancellationToken cancellationToken = default);

    /// <summary>创建迭代。</summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="name">迭代名称。</param>
    /// <param name="owners">负责人 userId 列表（至少一项）。</param>
    /// <param name="startDate">开始日期。</param>
    /// <param name="endDate">结束日期。</param>
    /// <param name="description">描述。</param>
    /// <param name="capacityHours">工时容量。</param>
    /// <param name="operatorId">操作者 userId。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建迭代 ID；响应缺 id 时为 <c>null</c>。</returns>
    Task<string?> CreateSprintAsync(
        string organizationId,
        string projectId,
        string name,
        IReadOnlyList<string> owners,
        string? startDate,
        string? endDate,
        string? description,
        int? capacityHours,
        string? operatorId,
        CancellationToken cancellationToken = default);

    /// <summary>创建当前用户（取 <c>assignedTo</c> 用）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前用户；空体时为 <c>null</c>。</returns>
    Task<YunxiaoCurrentUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>创建工作项关系。</summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="workitemId">源工作项 ID。</param>
    /// <param name="relationType">关系类型。</param>
    /// <param name="targetWorkitemId">目标工作项 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建关系 ID；响应缺 id 时为 <c>null</c>。</returns>
    Task<string?> CreateWorkitemRelationAsync(
        string organizationId,
        string workitemId,
        string relationType,
        string targetWorkitemId,
        CancellationToken cancellationToken = default);

    /// <summary>查询测试用例库列表。</summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="page">页码。</param>
    /// <param name="perPage">每页条数（上限 100）。</param>
    /// <param name="name">名称过滤。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用例库列表。</returns>
    Task<List<YunxiaoTestRepo>> ListTestReposAsync(
        string organizationId,
        int page,
        int perPage,
        string? name,
        CancellationToken cancellationToken = default);

    /// <summary>创建测试用例。</summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="request">用例请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建用例 ID。</returns>
    Task<string?> CreateTestcaseAsync(
        string organizationId,
        YunxiaoCreateTestcaseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>创建测试计划（未真机验证）。</summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="request">计划请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建计划标识。</returns>
    Task<string?> CreateTestPlanAsync(
        string organizationId,
        YunxiaoCreateTestPlanRequest request,
        CancellationToken cancellationToken = default);
}
