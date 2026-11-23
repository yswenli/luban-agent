/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： ProjexClient
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/17
*描述：云效研发项目域（Projex）客户端
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效研发项目域（Projex）客户端 - 提供项目查询（搜索 / 详情）等能力。
/// </summary>
/// <remarks>
/// 形态对齐 <see cref="CodeupClient"/>：HttpClient 单例、<c>x-yunxiao-token</c> 头、
/// 超时 / 重试从 <see cref="YunxiaoApiOptions"/> 读；私有辅助（发请求、解响应、错误映射）
/// 自 <see cref="CodeupClient"/> 复制，不抽公共基类。
/// </remarks>
public class ProjexClient : IProjexClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly YunxiaoApiOptions _options;
    private readonly IYunxiaoCredentialProvider _credentialProvider;
    private readonly PluginPermissionGuard _guard;
    private readonly string? _connectorName;
    private bool _disposed;

    // 忽略 null 字段，避免把未设置的关键字等参数以 null 发给云效
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 创建 ProjexClient 实例
    /// </summary>
    /// <param name="options">API 配置</param>
    /// <param name="credentialProvider">凭据提供者</param>
    /// <param name="permissionGuard">插件权限门禁（可选；不传则用进程级兜底实例）。</param>
    /// <param name="connectorName">所属连接器实例名（可选，Q15）；决定 PAT 凭据键，<c>null</c> 视为默认实例。</param>
    public ProjexClient(
        YunxiaoApiOptions options,
        IYunxiaoCredentialProvider credentialProvider,
        PluginPermissionGuard? permissionGuard = null,
        string? connectorName = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
        _guard = permissionGuard ?? PluginPermissionGuard.Default;
        _connectorName = connectorName;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
    }

    /// <summary>
    /// 本实例的 PAT 凭据键（Q15 泛化：<c>connector:&lt;实例名&gt;:token</c>）。
    /// </summary>
    private string PatCredentialKey => CredentialKeys.ForConnectorToken(_connectorName ?? YunxiaoConnector.DefaultName);

    #region 研发项目 API

    /// <summary>
    /// 搜索研发项目（<c>POST .../projex/organizations/{organizationId}/projects:search</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="page">页码（从 1 开始，下限 1）。</param>
    /// <param name="perPage">每页条数（上限 200，越界自动收敛）。</param>
    /// <param name="keyword">检索关键字（可选，非空时按名称模糊过滤）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>项目分页结果；空体时为 <c>null</c>。</returns>
    /// <remarks>
    /// <b>真机口径（2026-11-18 取证）</b>：响应体是<b>裸 JSON 数组</b>，
    /// 分页信息在响应头（<c>X-Total</c> / <c>X-Page</c> / <c>X-Per-Page</c>），
    /// 故由客户端把数组与响应头合成 <see cref="YunxiaoPagedResult{T}"/>（与 Codeup 列表同口径）。
    /// </remarks>
    public async Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsAsync(
        string? organizationId,
        int page,
        int perPage,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        var orgId = YunxiaoOrganizationId.Resolve(organizationId, _options.OrganizationId);
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var (url, body) = BuildSearchRequest(orgId, page, perPage, keyword);

        var response = await SendRawAsync(
            HttpMethod.Post,
            url,
            body,
            cancellationToken);

        return ParseSearchResponse(response.Body, response.Total, response.Page, response.PerPage);
    }

    /// <summary>
    /// 以显式 PAT 搜索研发项目（只读探活用：不读凭据存储、不写任何配置）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="page">页码（从 1 开始，下限 1）。</param>
    /// <param name="perPage">每页条数（上限 200，越界自动收敛）。</param>
    /// <param name="keyword">检索关键字（可选，非空时按名称模糊过滤）。</param>
    /// <param name="pat">本次调用使用的 PAT（必填）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>项目分页结果；空体时为 <c>null</c>。</returns>
    /// <remarks>
    /// 工作台「测试连接 / 保存前探活」专用（<see cref="IProjexClient.SearchProjectsWithPatAsync"/>）：
    /// 以用户<b>本次输入</b>的 PAT 建临时只读调用，供保存前校验新令牌；请求装配 / 响应解析
    /// 与 <see cref="SearchProjectsAsync"/> 完全同口径。
    /// </remarks>
    public async Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsWithPatAsync(
        string organizationId,
        int page,
        int perPage,
        string? keyword,
        string pat,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pat))
            throw new ArgumentException("PAT 不能为空", nameof(pat));

        var (url, body) = BuildSearchRequest(organizationId, page, perPage, keyword);

        var response = await SendRawAsync(
            HttpMethod.Post,
            url,
            body,
            cancellationToken,
            pat.Trim());

        return ParseSearchResponse(response.Body, response.Total, response.Page, response.PerPage);
    }

    /// <summary>
    /// 获取研发项目详情（<c>GET .../projex/organizations/{organizationId}/projects/{projectId}</c>，URL 已实测通过）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>项目详情；空体时为 <c>null</c>。</returns>
    public async Task<YunxiaoProject?> GetProjectAsync(
        string? organizationId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var orgId = YunxiaoOrganizationId.Resolve(organizationId, _options.OrganizationId);
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("项目 ID 不能为空", nameof(projectId));

        var path = $"/oapi/v1/projex/organizations/{orgId}/projects/{projectId}";

        return await SendRequestAsync<YunxiaoProject>(
            HttpMethod.Get,
            path,
            cancellationToken: cancellationToken);
    }

    #endregion

    #region 迭代（Sprint）API

    /// <summary>
    /// 获取迭代列表（<c>GET .../projects/{id}/sprints</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="status">状态过滤（<c>TODO,DOING,ARCHIVED</c>，逗号分隔，可空）。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="perPage">每页条数（1–200）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>迭代列表（裸数组）；空体时为空列表。</returns>
    public async Task<List<YunxiaoSprint>> ListSprintsAsync(
        string? organizationId,
        string projectId,
        string? status,
        int page,
        int perPage,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        RequirePath(projectId, "项目 ID");

        var query = new List<string>
        {
            $"page={Math.Max(page, 1)}",
            $"perPage={ClampPerPage(perPage)}"
        };
        if (!string.IsNullOrWhiteSpace(status))
            query.Add($"status={Uri.EscapeDataString(status.Trim())}");

        var path = $"/oapi/v1/projex/organizations/{orgId}/projects/{Uri.EscapeDataString(projectId)}/sprints"
                   + $"?{string.Join("&", query)}";

        var response = await SendRawAsync(HttpMethod.Get, path, null, cancellationToken);
        return YunxiaoResponseParser.DeserializeList<YunxiaoSprint>(response.Body) ?? [];
    }

    /// <summary>
    /// 获取迭代详情（<c>GET .../projects/{projectId}/sprints/{id}</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="sprintId">迭代 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>迭代详情；空体时为 <c>null</c>。</returns>
    public async Task<YunxiaoSprint?> GetSprintAsync(
        string? organizationId,
        string projectId,
        string sprintId,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        RequirePath(projectId, "项目 ID");
        RequirePath(sprintId, "迭代 ID");

        var path = $"/oapi/v1/projex/organizations/{orgId}/projects/{Uri.EscapeDataString(projectId)}"
                   + $"/sprints/{Uri.EscapeDataString(sprintId)}";

        return await SendRequestAsync<YunxiaoSprint>(HttpMethod.Get, path, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 创建迭代（<c>POST .../projects/{id}/sprints</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="name">迭代名称（必填）。</param>
    /// <param name="owners">负责人 userId 列表（云效必填，至少一项）。</param>
    /// <param name="startDate">开始日期（可空）。</param>
    /// <param name="endDate">结束日期（可空）。</param>
    /// <param name="description">描述（可空）。</param>
    /// <param name="capacityHours">工时容量（可空）。</param>
    /// <param name="operatorId">操作者 userId（可空，个人 token 下无效）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建迭代 ID；响应缺 id 时为 <c>null</c>。</returns>
    public async Task<string?> CreateSprintAsync(
        string? organizationId,
        string projectId,
        string name,
        IReadOnlyList<string> owners,
        string? startDate,
        string? endDate,
        string? description,
        int? capacityHours,
        string? operatorId,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        RequirePath(projectId, "项目 ID");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("迭代名称不能为空", nameof(name));
        if (owners.Count == 0)
            throw new ArgumentException("负责人列表不能为空（云效要求至少一项）", nameof(owners));

        var path = $"/oapi/v1/projex/organizations/{orgId}/projects/{Uri.EscapeDataString(projectId)}/sprints";

        var body = new
        {
            name = name.Trim(),
            owners,
            startDate,
            endDate,
            description,
            capacityHours,
            operatorId
        };

        var created = await SendRequestAsync<YunxiaoIdResult>(HttpMethod.Post, path, body, cancellationToken);
        return created?.Id;
    }

    #endregion

    #region 工作项 API

    /// <summary>
    /// 获取项目下的工作项类型（<c>GET .../projects/{id}/workitemTypes?category=</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="category">分类：<c>Req</c> / <c>Bug</c> / <c>Task</c>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作项类型列表；空体时为空列表。</returns>
    public async Task<List<YunxiaoWorkitemType>> ListWorkitemTypesAsync(
        string? organizationId,
        string projectId,
        string category,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        RequirePath(projectId, "项目 ID");
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("工作项分类不能为空（Req / Bug / Task）", nameof(category));

        var path = $"/oapi/v1/projex/organizations/{orgId}/projects/{Uri.EscapeDataString(projectId)}"
                   + $"/workitemTypes?category={Uri.EscapeDataString(category.Trim())}";

        var response = await SendRawAsync(HttpMethod.Get, path, null, cancellationToken);
        return YunxiaoResponseParser.DeserializeList<YunxiaoWorkitemType>(response.Body) ?? [];
    }

    /// <summary>
    /// 获取工作项工作流（<c>GET .../projects/{projectId}/workitemTypes/{id}/workflows</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="workitemTypeId">工作项类型 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作流（含 <c>statuses</c> 列序与 <c>defaultStatusId</c>）；空体时为 <c>null</c>。</returns>
    public async Task<YunxiaoWorkitemWorkflow?> GetWorkitemWorkflowAsync(
        string? organizationId,
        string projectId,
        string workitemTypeId,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        RequirePath(projectId, "项目 ID");
        RequirePath(workitemTypeId, "工作项类型 ID");

        var path = $"/oapi/v1/projex/organizations/{orgId}/projects/{Uri.EscapeDataString(projectId)}"
                   + $"/workitemTypes/{Uri.EscapeDataString(workitemTypeId)}/workflows";

        return await SendRequestAsync<YunxiaoWorkitemWorkflow>(HttpMethod.Get, path, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 搜索工作项（<c>POST .../workitems:search</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="criteria">搜索条件（项目、分类、迭代 / 状态 / 负责人等过滤）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果；空体时为 <c>null</c>。</returns>
    /// <remarks>
    /// 分页信息在响应头（<c>X-Total</c> / <c>X-Page</c> / <c>X-Per-Page</c>），响应体是裸数组；
    /// 分页窗口上限 <c>page * perPage ≤ 10000</c>（超出由云效返回 400）。
    /// </remarks>
    public async Task<YunxiaoPagedResult<YunxiaoWorkitem>?> SearchWorkitemsAsync(
        string? organizationId,
        YunxiaoWorkitemSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        var (url, body) = BuildSearchWorkitemsRequest(orgId, criteria);

        var response = await SendRawAsync(HttpMethod.Post, url, body, cancellationToken);

        return ParseSearchWorkitemsResponse(response.Body, response.Total, response.Page, response.PerPage);
    }

    /// <summary>
    /// 创建工作项（<c>POST .../workitems</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="request">创建请求（项目、标题、类型、负责人必填）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建工作项 ID；响应缺 id 时为 <c>null</c>。</returns>
    public async Task<string?> CreateWorkitemAsync(
        string? organizationId,
        YunxiaoCreateWorkitemRequest request,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        RequirePath(request.ProjectId, "项目 ID");
        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new ArgumentException("工作项标题不能为空", nameof(request));
        RequirePath(request.WorkitemTypeId, "工作项类型 ID");
        RequirePath(request.AssignedTo, "负责人 userId");

        var path = $"/oapi/v1/projex/organizations/{orgId}/workitems";

        var body = new Dictionary<string, object?>
        {
            ["spaceId"] = request.ProjectId,
            ["subject"] = request.Subject,
            ["workitemTypeId"] = request.WorkitemTypeId,
            ["assignedTo"] = request.AssignedTo
        };
        if (!string.IsNullOrWhiteSpace(request.SprintId)) body["sprint"] = request.SprintId;
        if (request.Description is not null) body["description"] = request.Description;
        if (!string.IsNullOrWhiteSpace(request.FormatType)) body["formatType"] = request.FormatType;
        if (!string.IsNullOrWhiteSpace(request.ParentId)) body["parentId"] = request.ParentId;
        if (request.Labels is { Count: > 0 }) body["labels"] = request.Labels;
        if (request.CustomFieldValues is { Count: > 0 }) body["customFieldValues"] = request.CustomFieldValues;

        var created = await SendRequestAsync<YunxiaoIdResult>(HttpMethod.Post, path, body, cancellationToken);
        return created?.Id;
    }

    /// <summary>
    /// 更新工作项（<c>PUT .../workitems/{id}</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="workitemId">工作项 ID。</param>
    /// <param name="fields">待更新字段（键为云效字段名 / 自定义字段 ID，只传要改的）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task UpdateWorkitemAsync(
        string? organizationId,
        string workitemId,
        IReadOnlyDictionary<string, object?> fields,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        RequirePath(workitemId, "工作项 ID");
        if (fields.Count == 0)
            throw new ArgumentException("待更新字段不能为空", nameof(fields));

        var path = $"/oapi/v1/projex/organizations/{orgId}/workitems/{Uri.EscapeDataString(workitemId)}";

        await SendRawAsync(HttpMethod.Put, path, fields, cancellationToken);
    }

    /// <summary>
    /// 获取工作项详情（<c>GET .../workitems/{id}</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="workitemId">工作项 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作项详情；空体时为 <c>null</c>。</returns>
    public async Task<YunxiaoWorkitem?> GetWorkitemAsync(
        string? organizationId,
        string workitemId,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        RequirePath(workitemId, "工作项 ID");

        var path = $"/oapi/v1/projex/organizations/{orgId}/workitems/{Uri.EscapeDataString(workitemId)}";

        return await SendRequestAsync<YunxiaoWorkitem>(HttpMethod.Get, path, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 获取工作项评论列表（<c>GET .../workitems/{id}/comments</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="workitemId">工作项 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>评论列表；空体时为空列表。</returns>
    public async Task<List<YunxiaoWorkitemComment>> ListWorkitemCommentsAsync(
        string? organizationId,
        string workitemId,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        RequirePath(workitemId, "工作项 ID");

        var path = $"/oapi/v1/projex/organizations/{orgId}/workitems/{Uri.EscapeDataString(workitemId)}/comments";

        var response = await SendRawAsync(HttpMethod.Get, path, null, cancellationToken);
        return YunxiaoResponseParser.DeserializeList<YunxiaoWorkitemComment>(response.Body) ?? [];
    }

    /// <summary>
    /// 创建工作项评论（<c>POST .../workitems/{id}/comments</c>，Markdown 正文）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="workitemId">工作项 ID。</param>
    /// <param name="content">评论正文。</param>
    /// <param name="parentId">父评论 ID（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建评论 ID；响应缺 id 时为 <c>null</c>。</returns>
    public async Task<string?> CreateWorkitemCommentAsync(
        string? organizationId,
        string workitemId,
        string content,
        string? parentId,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        var (path, body) = BuildCreateCommentRequest(orgId, workitemId, content, parentId);
        var created = await SendRequestAsync<YunxiaoIdResult>(HttpMethod.Post, path, body, cancellationToken);
        return created?.Id;
    }

    /// <summary>装配创建评论请求（纯函数，供单测锁协议）。</summary>
    internal static (string Url, Dictionary<string, object?> Body) BuildCreateCommentRequest(
        string organizationId,
        string workitemId,
        string content,
        string? parentId)
    {
        RequirePath(organizationId, "组织 ID");
        RequirePath(workitemId, "工作项 ID");
        RequirePath(content, "评论正文");

        var body = new Dictionary<string, object?>
        {
            ["content"] = content.Trim(),
            ["formatType"] = "MARKDOWN"
        };
        if (!string.IsNullOrWhiteSpace(parentId))
            body["parentId"] = parentId.Trim();

        var url = $"/oapi/v1/projex/organizations/{Uri.EscapeDataString(organizationId)}"
                  + $"/workitems/{Uri.EscapeDataString(workitemId)}/comments";
        return (url, body);
    }

    /// <summary>
    /// 获取工作项附件列表（<c>GET .../workitems/{id}/attachments</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="workitemId">工作项 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>附件列表；空体时为空列表。</returns>
    public async Task<List<YunxiaoWorkitemAttachment>> ListWorkitemAttachmentsAsync(
        string? organizationId,
        string workitemId,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        RequirePath(workitemId, "工作项 ID");

        var path = $"/oapi/v1/projex/organizations/{orgId}/workitems/{Uri.EscapeDataString(workitemId)}/attachments";

        var response = await SendRawAsync(HttpMethod.Get, path, null, cancellationToken);
        return YunxiaoResponseParser.DeserializeList<YunxiaoWorkitemAttachment>(response.Body) ?? [];
    }

    /// <summary>
    /// 获取工作项文件下载信息（<c>GET .../workitems/{workitemId}/files/{id}</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="workitemId">工作项 ID。</param>
    /// <param name="fileId">文件 ID（附件列表的 <c>fileId</c>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>文件信息（含临时下载地址）；空体时为 <c>null</c>。</returns>
    public async Task<YunxiaoWorkitemFile?> GetWorkitemFileAsync(
        string? organizationId,
        string workitemId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        RequirePath(workitemId, "工作项 ID");
        RequirePath(fileId, "文件 ID");

        var path = $"/oapi/v1/projex/organizations/{orgId}/workitems/{Uri.EscapeDataString(workitemId)}"
                   + $"/files/{Uri.EscapeDataString(fileId)}";

        return await SendRequestAsync<YunxiaoWorkitemFile>(HttpMethod.Get, path, cancellationToken: cancellationToken);
    }

    #endregion

    #region 工作项关系 / 平台用户 / 测试（Testhub）API

    /// <summary>
    /// 创建当前用户（<c>GET /oapi/v1/platform/user</c>）。
    /// </summary>
    /// <remarks>个人 PAT 场景下用于取 <c>id</c> 作为工作项 <c>assignedTo</c>（云效要求必填）。</remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前用户；空体时为 <c>null</c>。</returns>
    public async Task<YunxiaoCurrentUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<YunxiaoCurrentUser>(
            HttpMethod.Get,
            "/oapi/v1/platform/user",
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 创建工作项关系（<c>POST .../workitems/{workItemId}/relationRecords</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="workitemId">源工作项 ID。</param>
    /// <param name="relationType">关系类型：<c>PARENT</c> / <c>SUB</c> / <c>ASSOCIATED</c> / <c>DEPEND_ON</c> / <c>DEPENDED_BY</c>。</param>
    /// <param name="targetWorkitemId">目标工作项 ID（请求体的 <c>workitemId</c>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建关系 ID；响应缺 id 时为 <c>null</c>。</returns>
    public async Task<string?> CreateWorkitemRelationAsync(
        string? organizationId,
        string workitemId,
        string relationType,
        string targetWorkitemId,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        var (path, body) = BuildCreateRelationRequest(orgId, workitemId, relationType, targetWorkitemId);

        var created = await SendRequestAsync<YunxiaoIdResult>(HttpMethod.Post, path, body, cancellationToken);
        return created?.Id;
    }

    /// <summary>
    /// 查询测试用例库列表（<c>GET .../testhub/organizations/{orgId}/testRepo/list</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="perPage">每页条数（接口上限 100，越界自动收敛）。</param>
    /// <param name="name">名称过滤（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用例库列表（裸数组）；空体时为空列表。</returns>
    public async Task<List<YunxiaoTestRepo>> ListTestReposAsync(
        string? organizationId,
        int page,
        int perPage,
        string? name,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        var path = BuildListTestReposPath(orgId, page, perPage, name);

        var response = await SendRawAsync(HttpMethod.Get, path, null, cancellationToken);
        return YunxiaoResponseParser.DeserializeList<YunxiaoTestRepo>(response.Body) ?? [];
    }

    /// <summary>列出用例库目录（裸数组；根目录 <c>parentId</c> 为 null，且通常有一条名为「全部用例」）。</summary>
    public async Task<List<YunxiaoTestRepoDirectory>> ListTestRepoDirectoriesAsync(
        string? organizationId,
        string? testRepoId,
        int page = 1,
        int perPage = 100,
        CancellationToken cancellationToken = default)
    {
        var orgId = RequireOrg(organizationId);
        var repoId = testRepoId?.Trim();
        if (string.IsNullOrWhiteSpace(repoId))
            throw new ArgumentException("testRepoId 不能为空", nameof(testRepoId));

        var path = BuildListTestRepoDirectoriesPath(orgId, repoId, page, perPage);
        var response = await SendRawAsync(HttpMethod.Get, path, null, cancellationToken);
        return YunxiaoResponseParser.DeserializeList<YunxiaoTestRepoDirectory>(response.Body) ?? [];
    }

    /// <summary>
    /// 构建目录列表路径（纯函数，供单测锁口径）。
    /// <c>perPage</c> 超出 1–100 范围时被 clamp。
    /// </summary>
    internal static string BuildListTestRepoDirectoriesPath(string org, string testRepoId, int page, int perPage)
    {
        var safePerPage = Math.Clamp(perPage, 1, 100);
        return $"/oapi/v1/testhub/organizations/{org}/testRepos/{testRepoId}/directories?page={Math.Max(1, page)}&perPage={safePerPage}";
    }

    /// <summary>
    /// 创建测试用例（<c>POST .../testRepos/{testRepoId}/testcases</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="request">用例请求（<c>TestRepoId</c> 必须存在）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建用例 ID；响应缺 id 时为 <c>null</c>。</returns>
    public async Task<string?> CreateTestcaseAsync(
        string? organizationId,
        YunxiaoCreateTestcaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var orgId = RequireOrg(organizationId);
        var (path, body) = BuildCreateTestcaseRequest(orgId, request);

        var created = await SendRequestAsync<YunxiaoTestcase>(HttpMethod.Post, path, body, cancellationToken);
        return created?.Id;
    }

    /// <summary>
    /// 创建测试计划（<c>POST .../testhub/organizations/{orgId}/testPlans</c>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="request">计划请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建计划标识（优先 <c>testPlanIdentifier</c>）；响应缺标识时为 <c>null</c>。</returns>
    /// <remarks>
    /// <b>未真机验证</b>：官方 MCP 包未提供「创建测试计划」工具，字段按云效文档最小集装配；
    /// 失败会由调用方记为单条回执，不影响已创建的需求 / 任务 / 用例。
    /// </remarks>
    public async Task<string?> CreateTestPlanAsync(
        string? organizationId,
        YunxiaoCreateTestPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var orgId = RequireOrg(organizationId);
        var (path, body) = BuildCreateTestPlanRequest(orgId, request);

        var created = await SendRequestAsync<YunxiaoTestPlan>(HttpMethod.Post, path, body, cancellationToken);
        return created?.TestPlanIdentifier ?? created?.Id;
    }

    #endregion

    #region 请求装配 / 响应解析（纯函数，供单测锁口径）

    /// <summary>
    /// 装配「创建测试用例库列表」请求路径（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="page">页码（下限 1）。</param>
    /// <param name="perPage">每页条数（区间 [1, 100]）。</param>
    /// <param name="name">名称过滤（非空才带）。</param>
    /// <returns>请求路径（含查询串）。</returns>
    internal static string BuildListTestReposPath(
        string organizationId,
        int page,
        int perPage,
        string? name)
    {
        RequirePath(organizationId, "组织 ID");

        var safePage = Math.Max(page, 1);
        var safePerPage = Math.Clamp(perPage, 1, 100);

        var query = new List<string> { $"page={safePage}", $"perPage={safePerPage}" };
        if (!string.IsNullOrWhiteSpace(name))
            query.Add($"name={Uri.EscapeDataString(name.Trim())}");

        return $"/oapi/v1/testhub/organizations/{organizationId}/testRepo/list?{string.Join("&", query)}";
    }

    /// <summary>
    /// 装配「创建测试用例」请求（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="request">用例请求。</param>
    /// <returns>请求路径与请求体。</returns>
    /// <exception cref="ArgumentException">用例库 ID 或标题为空。</exception>
    internal static (string Url, Dictionary<string, object?> Body) BuildCreateTestcaseRequest(
        string organizationId,
        YunxiaoCreateTestcaseRequest request)
    {
        RequirePath(organizationId, "组织 ID");
        RequirePath(request.TestRepoId, "用例库 ID");
        RequirePath(request.Subject, "测试用例标题");

        var url = $"/oapi/v1/testhub/organizations/{organizationId}/testRepos/{Uri.EscapeDataString(request.TestRepoId)}/testcases";

        var body = new Dictionary<string, object?>
        {
            ["subject"] = request.Subject.Trim()
        };
        if (!string.IsNullOrWhiteSpace(request.AssignedTo))
            body["assignedTo"] = request.AssignedTo.Trim();
        if (!string.IsNullOrWhiteSpace(request.DirectoryId))
            body["directoryId"] = request.DirectoryId.Trim();
        if (!string.IsNullOrWhiteSpace(request.PreCondition))
            body["preCondition"] = request.PreCondition;
        if (request.Labels is { Count: > 0 })
            body["labels"] = request.Labels;
        if (request.CustomFieldValues is { Count: > 0 })
            body["customFieldValues"] = request.CustomFieldValues;
        if (request.TestSteps is { Count: > 0 })
            body["testSteps"] = request.TestSteps;

        return (url, body);
    }

    /// <summary>
    /// 装配「创建测试计划」请求（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="request">计划请求。</param>
    /// <returns>请求路径与请求体。</returns>
    /// <exception cref="ArgumentException">计划名称为空。</exception>
    internal static (string Url, Dictionary<string, object?> Body) BuildCreateTestPlanRequest(
        string organizationId,
        YunxiaoCreateTestPlanRequest request)
    {
        RequirePath(organizationId, "组织 ID");
        RequirePath(request.Name, "测试计划名称");

        var url = $"/oapi/v1/testhub/organizations/{organizationId}/testPlans";

        var body = new Dictionary<string, object?>
        {
            ["name"] = request.Name.Trim()
        };
        if (!string.IsNullOrWhiteSpace(request.ProjectId))
            body["projectIdentifier"] = request.ProjectId.Trim();
        if (!string.IsNullOrWhiteSpace(request.SprintId))
            body["sprintIdentifier"] = request.SprintId.Trim();
        if (!string.IsNullOrWhiteSpace(request.StartDate))
            body["startDate"] = request.StartDate.Trim();
        if (!string.IsNullOrWhiteSpace(request.EndDate))
            body["endDate"] = request.EndDate.Trim();
        if (request.Managers is { Count: > 0 })
            body["managerIdentifiers"] = request.Managers;
        if (request.CaseIds is { Count: > 0 })
            body["testcases"] = request.CaseIds;

        return (url, body);
    }

    /// <summary>
    /// 装配「创建关系」请求（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="workitemId">源工作项 ID。</param>
    /// <param name="relationType">关系类型。</param>
    /// <param name="targetWorkitemId">目标工作项 ID。</param>
    /// <returns>请求路径与请求体。</returns>
    /// <exception cref="ArgumentException">任一必填项为空，或关系类型不受支持。</exception>
    internal static (string Url, Dictionary<string, object?> Body) BuildCreateRelationRequest(
        string organizationId,
        string workitemId,
        string relationType,
        string targetWorkitemId)
    {
        RequirePath(organizationId, "组织 ID");
        RequirePath(workitemId, "源工作项 ID");
        RequirePath(targetWorkitemId, "目标工作项 ID");
        if (!IsValidRelationType(relationType))
            throw new ArgumentException("关系类型不支持（PARENT / SUB / ASSOCIATED / DEPEND_ON / DEPENDED_BY）", nameof(relationType));

        var url = $"/oapi/v1/projex/organizations/{organizationId}/workitems/{Uri.EscapeDataString(workitemId)}/relationRecords";

        var body = new Dictionary<string, object?>
        {
            ["relationType"] = relationType.Trim().ToUpperInvariant(),
            ["workitemId"] = targetWorkitemId.Trim()
        };

        return (url, body);
    }

    /// <summary>
    /// 校验关系类型是否属于云效允许集合（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="relationType">关系类型。</param>
    /// <returns>是否合法。</returns>
    internal static bool IsValidRelationType(string? relationType)
    {
        return relationType?.Trim().ToUpperInvariant() is
            "PARENT" or "SUB" or "ASSOCIATED" or "DEPEND_ON" or "DEPENDED_BY";
    }

    /// <summary>
    /// 装配「搜索项目」请求（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="page">页码（下限 1）。</param>
    /// <param name="perPage">每页条数（区间 [1, 200]，越界自动收敛）。</param>
    /// <param name="keyword">检索关键字（非空时按名称模糊过滤）。</param>
    /// <returns>请求路径与请求体。</returns>
    /// <remarks>
    /// <b>真机口径（2026-11-18 取证）</b>：URL 为
    /// <c>POST /oapi/v1/projex/organizations/{organizationId}/projects:search</c>；
    /// 名称检索不是独立字段，而是放进 <c>conditions</c> 的 conditionGroups JSON 串
    /// （<c>name</c> 字段、<c>input</c> 格式、<c>BETWEEN</c> 操作符，值为关键字数组），
    /// 与官方文档示例同形；<c>perPage</c> 界值 [1, 200]（官方 0–200，0 视作无意义按下限收敛）。
    /// </remarks>
    internal static (string Url, object Body) BuildSearchRequest(
        string organizationId,
        int page,
        int perPage,
        string? keyword)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
            throw new ArgumentException("组织 ID 不能为空", nameof(organizationId));

        var safePage = Math.Max(page, 1);
        var safePerPage = Math.Clamp(perPage, 1, 200);

        var url = $"/oapi/v1/projex/organizations/{organizationId}/projects:search";

        // keyword 非空才带 conditions（名称模糊过滤，官方文档 conditionGroups 形态）
        if (string.IsNullOrWhiteSpace(keyword))
            return (url, new { page = safePage, perPage = safePerPage });

        var conditions = JsonSerializer.Serialize(new
        {
            conditionGroups = new[]
            {
                new[]
                {
                    new
                    {
                        className = "string",
                        fieldIdentifier = "name",
                        format = "input",
                        @operator = "BETWEEN",
                        toValue = (string?)null,
                        value = new[] { keyword.Trim() }
                    }
                }
            }
        });

        return (url, new { page = safePage, perPage = safePerPage, conditions });
    }

    /// <summary>
    /// 解析「搜索项目」响应（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="json">响应体（<b>裸 JSON 数组</b>）。</param>
    /// <param name="total">总条数（来自 <c>X-Total</c> 响应头，缺省 0）。</param>
    /// <param name="page">当前页码（来自 <c>X-Page</c> 响应头，缺省取 1）。</param>
    /// <param name="perPage">每页条数（来自 <c>X-Per-Page</c> 响应头，缺省取 items 数）。</param>
    /// <returns>分页结果；空体时为 <c>null</c>。</returns>
    /// <remarks>
    /// <b>真机口径（2026-11-18 取证）</b>：响应体是裸数组（无 <c>result/total</c> 外壳），
    /// 元素按 <see cref="YunxiaoProject"/> 反序列化（<see cref="YunxiaoResponseParser.JsonOpts"/> 同口径）；
    /// 分页三元组合成自响应头。业务错误由 <see cref="YunxiaoResponseParser.ParseBusinessError"/>
    /// 在发送链路里同口径拦截。
    /// </remarks>
    internal static YunxiaoPagedResult<YunxiaoProject>? ParseSearchResponse(
        string? json,
        int total,
        int page,
        int perPage)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var items = YunxiaoResponseParser.DeserializeList<YunxiaoProject>(json);
        if (items is null)
            return null;

        return new YunxiaoPagedResult<YunxiaoProject>
        {
            Items = items,
            Total = total > 0 ? total : items.Count,
            Page = page > 0 ? page : 1,
            PerPage = perPage > 0 ? perPage : items.Count
        };
    }

    /// <summary>
    /// 装配「搜索工作项」请求（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="criteria">搜索条件。</param>
    /// <returns>请求路径与请求体。</returns>
    /// <exception cref="ArgumentException">项目 / 分类为空，或 <c>page * perPage &gt; 10000</c>（云效分页窗口上限）。</exception>
    /// <remarks>
    /// <c>conditions</c> 是 <b>JSON 字符串</b>（不是对象），形态为
    /// <c>{"conditionGroups":[[{className,fieldIdentifier,format,operator,toValue,value}...]]}</c>；
    /// 迭代过滤用 <c>className=sprint / fieldIdentifier=sprint / format=list / operator=CONTAINS</c>。
    /// </remarks>
    internal static (string Url, object Body) BuildSearchWorkitemsRequest(
        string organizationId,
        YunxiaoWorkitemSearchCriteria criteria)
    {
        RequirePath(organizationId, "组织 ID");
        RequirePath(criteria.ProjectId, "项目 ID");
        RequirePath(criteria.Category, "工作项分类");

        var safePage = Math.Max(criteria.Page, 1);
        var safePerPage = ClampPerPage(criteria.PerPage);
        if ((long)safePage * safePerPage > 10_000)
            throw new ArgumentException("page * perPage 不能超过 10000（云效分页窗口上限）", nameof(criteria));

        var url = $"/oapi/v1/projex/organizations/{organizationId}/workitems:search";

        var body = new Dictionary<string, object?>
        {
            ["category"] = criteria.Category,
            ["spaceId"] = criteria.ProjectId,
            ["spaceType"] = "Project",
            ["orderBy"] = string.IsNullOrWhiteSpace(criteria.OrderBy) ? "gmtCreate" : criteria.OrderBy,
            ["sort"] = string.IsNullOrWhiteSpace(criteria.Sort) ? "desc" : criteria.Sort,
            ["page"] = safePage,
            ["perPage"] = safePerPage
        };

        var conditions = BuildWorkitemConditionsJson(criteria);
        if (conditions is not null)
            body["conditions"] = conditions;

        return (url, body);
    }

    /// <summary>
    /// 生成工作项搜索的 <c>conditions</c> JSON 串（无任何过滤时返回 <c>null</c>）。
    /// </summary>
    /// <param name="criteria">搜索条件。</param>
    /// <returns><c>{"conditionGroups":[...]}</c> 串；无过滤为 <c>null</c>。</returns>
    internal static string? BuildWorkitemConditionsJson(YunxiaoWorkitemSearchCriteria criteria)
    {
        var conditions = new List<object>();

        void AddList(string className, string fieldIdentifier, string? value, string format = "list")
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length == 0) return;
            conditions.Add(new
            {
                className,
                fieldIdentifier,
                format,
                @operator = "CONTAINS",
                toValue = (string?)null,
                value = values
            });
        }

        if (!string.IsNullOrWhiteSpace(criteria.Subject))
        {
            conditions.Add(new
            {
                className = "string",
                fieldIdentifier = "subject",
                format = "input",
                @operator = "CONTAINS",
                toValue = (string?)null,
                value = new[] { criteria.Subject.Trim() }
            });
        }

        AddList("status", "status", criteria.Status);
        AddList("user", "assignedTo", criteria.AssignedTo);
        AddList("sprint", "sprint", criteria.SprintId);
        AddList("workitemType", "workitemType", criteria.WorkitemTypeId);

        if (conditions.Count == 0)
            return null;

        return JsonSerializer.Serialize(new { conditionGroups = new[] { conditions } }, JsonOpts);
    }

    /// <summary>
    /// 解析「搜索工作项」响应（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="json">响应体（裸 JSON 数组）。</param>
    /// <param name="total">总条数（来自 <c>X-Total</c>）。</param>
    /// <param name="page">当前页（来自 <c>X-Page</c>）。</param>
    /// <param name="perPage">每页条数（来自 <c>X-Per-Page</c>）。</param>
    /// <returns>分页结果；空体时为 <c>null</c>。</returns>
    internal static YunxiaoPagedResult<YunxiaoWorkitem>? ParseSearchWorkitemsResponse(
        string? json,
        int total,
        int page,
        int perPage)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var items = YunxiaoResponseParser.DeserializeList<YunxiaoWorkitem>(json);
        if (items is null)
            return null;

        return new YunxiaoPagedResult<YunxiaoWorkitem>
        {
            Items = items,
            Total = total > 0 ? total : items.Count,
            Page = page > 0 ? page : 1,
            PerPage = perPage > 0 ? perPage : items.Count
        };
    }

    /// <summary>
    /// 解析组织 ID：入参优先，为空时回退配置值；仍为空则抛异常。
    /// </summary>
    /// <param name="organizationId">入参组织 ID。</param>
    /// <returns>有效组织 ID。</returns>
    private string RequireOrg(string? organizationId)
    {
        var orgId = YunxiaoOrganizationId.Resolve(organizationId, _options.OrganizationId);
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");
        return orgId;
    }

    /// <summary>
    /// 校验必填的路径片段。
    /// </summary>
    /// <param name="value">片段值。</param>
    /// <param name="name">用于报错的显示名。</param>
    private static void RequirePath(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name}不能为空", name);
    }

    /// <summary>每页条数收敛到云效允许区间 [1, 200]。</summary>
    /// <param name="perPage">原始值。</param>
    /// <returns>收敛后的值。</returns>
    private static int ClampPerPage(int perPage) => Math.Clamp(perPage, 1, 200);

    #endregion

    #region HTTP 请求

    /// <summary>
    /// 原始响应（响应体 + 分页响应头）。
    /// </summary>
    /// <param name="Body">响应体文本。</param>
    /// <param name="Total">总条数（<c>X-Total</c>，缺省 0）。</param>
    /// <param name="Page">当前页码（<c>X-Page</c>，缺省 0）。</param>
    /// <param name="PerPage">每页条数（<c>X-Per-Page</c>，缺省 0）。</param>
    private sealed record YunxiaoRawResponse(string Body, int Total, int Page, int PerPage);

    /// <summary>
    /// 发起请求并返回原始响应（含出口门禁、重试、错误映射与分页响应头提取）。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">相对路径（含查询串）。</param>
    /// <param name="body">请求体对象（<c>null</c> 表示无体）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="patOverride">显式 PAT（探活用）；<c>null</c> 时按连接器名从凭据提供者取已存 PAT。</param>
    /// <returns>响应体文本与分页响应头。</returns>
    /// <exception cref="YunxiaoApiException">HTTP 非 2xx，或 2xx 但响应体携带业务错误码。</exception>
    /// <remarks>
    /// 与 <see cref="CodeupClient"/> 同口径复制；差异仅在：Projex 列表接口把分页信息放
    /// <c>X-Total</c> / <c>X-Page</c> / <c>X-Per-Page</c> 响应头（与 Codeup 的 <c>x-total</c> 同型），
    /// 故此处顺带提取三元组随响应体返回。
    /// </remarks>
    private async Task<YunxiaoRawResponse> SendRawAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        string? patOverride = null)
    {
        // 出口门禁：目标主机 + PAT 凭据
        _guard.RequireNetwork(_options.Endpoint, "ProjexClient.SendRawAsync");
        _guard.RequireCredential(PatCredentialKey, "ProjexClient.SendRawAsync");

        var pat = patOverride ?? await _credentialProvider.GetPatAsync(_connectorName, cancellationToken);
        if (string.IsNullOrWhiteSpace(pat))
            throw new InvalidOperationException("未配置 PAT");

        var url = $"https://{_options.Endpoint}{path}";
        var retryCount = 0;

        while (retryCount <= _options.RetryCount)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);

                // 添加鉴权头
                request.Headers.Add("x-yunxiao-token", pat);

                // 添加请求体
                if (body != null)
                {
                    var json = JsonSerializer.Serialize(body, JsonOpts);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                if (_options.EnableVerboseLog)
                    Logger.Debug($"云效 API 请求: {method} {path}");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    // 云效错误体是 {"code":404,"errorCode":"SYSTEM_NOT_FOUND_ERROR","errorMessage":"...","traceId":"..."}
                    var detail = YunxiaoResponseParser.ExtractErrorMessage(responseBody) ?? responseBody;
                    var errorMsg = YunxiaoErrorMapper.MapHttpStatus((int)response.StatusCode, detail);

                    // 检查是否需要重试
                    if (YunxiaoErrorMapper.ShouldRetry((int)response.StatusCode) && retryCount < _options.RetryCount)
                    {
                        var delay = YunxiaoErrorMapper.GetRetryDelay((int)response.StatusCode, retryCount);
                        Logger.Warn($"云效 API 请求失败（{response.StatusCode}），{delay}ms 后重试（第 {retryCount + 1} 次）");
                        await Task.Delay(delay, cancellationToken);
                        retryCount++;
                        continue;
                    }

                    throw new YunxiaoApiException((int)response.StatusCode, errorMsg);
                }

                // 少数接口会用 2xx 携带业务错误码，这里统一拦截
                var businessError = YunxiaoResponseParser.ParseBusinessError(responseBody);
                if (businessError is not null)
                {
                    var (code, message) = businessError.Value;
                    throw new YunxiaoApiException(code, YunxiaoErrorMapper.MapBusinessError(code, message));
                }

                return new YunxiaoRawResponse(
                    responseBody,
                    ReadIntHeader(response.Headers, "X-Total"),
                    ReadIntHeader(response.Headers, "X-Page"),
                    ReadIntHeader(response.Headers, "X-Per-Page"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (YunxiaoApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (retryCount < _options.RetryCount)
                {
                    var delay = YunxiaoErrorMapper.GetRetryDelay(0, retryCount);
                    Logger.Warn($"云效 API 请求异常，{delay}ms 后重试（第 {retryCount + 1} 次）", ex);
                    await Task.Delay(delay, cancellationToken);
                    retryCount++;
                    continue;
                }

                throw new YunxiaoApiException(0, $"云效 API 请求失败: {ex.Message}", ex);
            }
        }

        throw new YunxiaoApiException(0, "云效 API 请求失败：重试次数已耗尽");
    }

    /// <summary>
    /// 读取整型响应头（缺失或非法返回 0）。
    /// </summary>
    /// <param name="headers">响应头集合。</param>
    /// <param name="name">响应头名（大小写不敏感）。</param>
    /// <returns>整型值；缺失 / 非法为 0。</returns>
    private static int ReadIntHeader(HttpResponseHeaders headers, string name)
    {
        if (headers.TryGetValues(name, out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return 0;
    }

    /// <summary>
    /// 请求并反序列化<b>单个对象</b>响应（云效成功响应是裸对象，不套 <c>{ "result": ... }</c> 外壳）。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">相对路径。</param>
    /// <param name="body">请求体对象（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>反序列化结果；响应体为空时为 <c>default</c>。</returns>
    private async Task<T?> SendRequestAsync<T>(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRawAsync(method, path, body, cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Body))
            return default;

        return YunxiaoResponseParser.DeserializeObject<T>(response.Body, path);
    }

    #endregion

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
