/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps.Mcp
*文件名： YunxiaoMCPClient
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：云效进程内 MCP 客户端 - 避免 PAT 落盘明文
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps.Mcp;

/// <summary>
/// 云效进程内 MCP 客户端 - 包装 <see cref="YunxiaoConnector"/> 暴露 37 个工具（Codeup 8 + Projex 19 + Testhub 3 + Flow 7）。
/// </summary>
/// <remarks>
/// 采用进程内形态（而非 stdio 子进程）是为了避免把 PAT 明文写进
/// <c>.luban-agent/mcps/*.json</c> 的 Args 字段。
/// 对 Agent 的工具名由 <c>MCPToolPlugin</c> 加前缀得到 <c>mcp_yunxiao_*</c>。
/// </remarks>
public class YunxiaoMCPClient : MCPClientBase, IDisposable
{
    private readonly YunxiaoConnector _connector;
    private bool _disposed;

    /// <summary>
    /// 服务器名称（<b>跟随连接器实例名</b>，默认实例为 <c>yunxiao</c>）。
    /// </summary>
    public override string Name => string.IsNullOrWhiteSpace(_connector.Name) ? YunxiaoConnector.DefaultName : _connector.Name;

    /// <summary>
    /// 服务器描述
    /// </summary>
    public override string Description => "云效代码管理与流水线工具";

    /// <summary>
    /// 创建云效 MCP 客户端
    /// </summary>
    /// <param name="connector">云效连接器</param>
    public YunxiaoMCPClient(YunxiaoConnector connector)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
    }

    /// <summary>
    /// 本实例的权限环境（由宿主在创建时注入）。
    /// </summary>
    /// <remarks>
    /// 进程内 MCP 客户端由宿主按插件创建（<c>PluginMcpCatalog</c> 每次调用新建），
    /// 故用可写属性而非构造参数注入：本类型亦被框架注册为单例，构造签名不宜改动。
    /// 保持为 <c>null</c> 时视为宿主自身调用（放行）。
    /// </remarks>
    public PluginPermissionScope? PermissionScope { get; set; }

    /// <inheritdoc />
    public override Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        // 进程内客户端无需连接，直接返回 true
        IsConnected = true;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public override Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task<IEnumerable<MCPTool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(YunxiaoToolDefinitions.GetAllTools());
    }

    /// <inheritdoc />
    public override async Task<MCPToolResult> CallToolAsync(
        string toolName,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        // 以插件身份执行：出口门禁据此判定越界
        using var _ = PluginScopeContext.Enter(PermissionScope);

        try
        {
            // 注意：MCPToolPlugin 传入的是 base 名（如 codeup_search_repos），
            // mcp_yunxiao_ 前缀由包装层添加，这里不得再带前缀。
            var baseName = StripPrefix(toolName);

            return baseName switch
            {
                "codeup_search_repos" => await CodeupSearchReposAsync(arguments, cancellationToken),
                "codeup_list_template_repos" => await CodeupListTemplateReposAsync(arguments, cancellationToken),
                "codeup_get_repo" => await CodeupGetRepoAsync(arguments, cancellationToken),
                "codeup_create_repo" => await CodeupCreateRepoAsync(arguments, cancellationToken),
                "codeup_list_branches" => await CodeupListBranchesAsync(arguments, cancellationToken),
                "codeup_create_group" => await CodeupCreateGroupAsync(arguments, cancellationToken),
                "codeup_list_groups" => await CodeupListGroupsAsync(arguments, cancellationToken),
                "codeup_update_repository" => await CodeupUpdateRepoAsync(arguments, cancellationToken),
                "projex_list_projects" => await ProjexListProjectsAsync(arguments, cancellationToken),
                "projex_get_project" => await ProjexGetProjectAsync(arguments, cancellationToken),
                "projex_list_sprints" => await ProjexListSprintsAsync(arguments, cancellationToken),
                "projex_get_sprint" => await ProjexGetSprintAsync(arguments, cancellationToken),
                "projex_create_sprint" => await ProjexCreateSprintAsync(arguments, cancellationToken),
                "workitem_list_types" => await WorkitemListTypesAsync(arguments, cancellationToken),
                "workitem_list_statuses" => await WorkitemListStatusesAsync(arguments, cancellationToken),
                "workitem_list_workflow" => await WorkitemListWorkflowAsync(arguments, cancellationToken),
                "workitem_list_requirements" => await WorkitemSearchAsync(arguments, "Req", cancellationToken),
                "workitem_create_requirement" => await WorkitemCreateAsync(arguments, cancellationToken),
                "workitem_list_tasks" => await WorkitemSearchAsync(arguments, "Task", cancellationToken),
                "workitem_create_task" => await WorkitemCreateAsync(arguments, cancellationToken),
                "workitem_update_task" => await WorkitemUpdateAsync(arguments, cancellationToken),
                "board_list_workitems_by_sprint" => await BoardListWorkitemsBySprintAsync(arguments, cancellationToken),
                "workitem_list_comments" => await WorkitemListCommentsAsync(arguments, cancellationToken),
                "workitem_get" => await WorkitemGetAsync(arguments, cancellationToken),
                "workitem_list_attachments" => await WorkitemListAttachmentsAsync(arguments, cancellationToken),
                "workitem_get_file" => await WorkitemGetFileAsync(arguments, cancellationToken),
                "workitem_create_relation" => await WorkitemCreateRelationAsync(arguments, cancellationToken),
                "testrepo_list" => await TestRepoListAsync(arguments, cancellationToken),
                "testcase_create" => await TestcaseCreateAsync(arguments, cancellationToken),
                "testplan_create" => await TestplanCreateAsync(arguments, cancellationToken),
                "flow_list_pipelines" => await FlowListPipelinesAsync(arguments, cancellationToken),
                "flow_list_host_groups" => await FlowListHostGroupsAsync(arguments, cancellationToken),
                "flow_get_host_group" => await FlowGetHostGroupAsync(arguments, cancellationToken),
                "flow_create_pipeline" => await FlowCreatePipelineAsync(arguments, cancellationToken),
                "flow_update_pipeline" => await FlowUpdatePipelineAsync(arguments, cancellationToken),
                "flow_run_pipeline" => await FlowRunPipelineAsync(arguments, cancellationToken),
                "flow_get_pipeline" => await FlowGetPipelineAsync(arguments, cancellationToken),
                _ => Fail($"未知工具: {toolName}")
            };
        }
        catch (YunxiaoApiException ex)
        {
            Logger.Error("云效 API 调用失败", ex, toolName);
            return Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error("云效 MCP 工具执行失败", ex, toolName);
            return Fail($"执行失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 兼容调用方传入带前缀或不带前缀的工具名。
    /// </summary>
    private static string StripPrefix(string toolName)
    {
        const string prefix = "mcp_yunxiao_";
        return toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? toolName[prefix.Length..]
            : toolName;
    }

    #region Codeup 工具实现

    private async Task<MCPToolResult> CodeupSearchReposAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var search = GetString(arguments, "search");
        var page = GetInt(arguments, "page", 1);
        var perPage = GetInt(arguments, "perPage", 20);
        var orderBy = GetString(arguments, "orderBy");
        var sort = GetString(arguments, "sort");
        var archived = GetBool(arguments, "archived");
        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetCodeupClient();
        var result = await client.SearchRepositoriesAsync(
            search, page, perPage, orderBy, sort, archived, organizationId, cancellationToken);

        if (result == null)
            return Fail("搜索仓库失败");

        return Ok(result.ToJson());
    }

    private async Task<MCPToolResult> CodeupListTemplateReposAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var templateType = GetInt(arguments, "templateType", 1);
        var page = GetInt(arguments, "page", 1);
        var perPage = GetInt(arguments, "perPage", 100);
        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetCodeupClient();
        var templates = await client.ListTemplateRepositoriesAsync(
            templateType, page, perPage, organizationId, cancellationToken);

        if (templates == null)
            return Fail("查询模板库失败");

        return Ok(templates.ToJson());
    }

    private async Task<MCPToolResult> CodeupGetRepoAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var repositoryId = GetString(arguments, "repositoryId");
        if (string.IsNullOrWhiteSpace(repositoryId))
            return Fail("缺少参数: repositoryId");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetCodeupClient();
        var repository = await client.GetRepositoryAsync(repositoryId!, organizationId, cancellationToken);

        if (repository == null)
            return Fail("查询仓库详情失败");

        // 云效仓库模型不带 webUrl，这里由 httpUrlToRepo 推导后一并返回，避免助手自行拼接。
        return Ok(new
        {
            id = repository.Id,
            name = repository.Name,
            path = repository.Path,
            httpUrlToRepo = repository.HttpUrlToRepo,
            sshUrlToRepo = repository.SSHUrlToRepo,
            webUrl = BuildWebUrl(repository.HttpUrlToRepo),
            defaultBranch = repository.DefaultBranch,
            visibility = repository.Visibility,
            description = repository.Description
        }.ToJson());
    }

    private async Task<MCPToolResult> CodeupCreateRepoAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var name = GetString(arguments, "name");
        var path = GetString(arguments, "path");
        if (string.IsNullOrWhiteSpace(name))
            return Fail("缺少参数: name");
        if (string.IsNullOrWhiteSpace(path))
            return Fail("缺少参数: path");

        var organizationId = GetString(arguments, "organizationId");
        var templateProjectId = GetLong(arguments, "templateProjectId");

        var request = new YunxiaoCreateRepositoryRequest
        {
            Name = name,
            Path = path,
            Visibility = GetString(arguments, "visibility") ?? "internal",
            NamespaceId = GetLong(arguments, "namespaceId"),
            CreateParentPath = GetBool(arguments, "createParentPath"),
            Description = GetString(arguments, "description")
        };

        if (templateProjectId.HasValue)
        {
            request.TemplateProject = new YunxiaoTemplateProject
            {
                TemplateProjectId = templateProjectId,
                TemplateType = GetInt(arguments, "templateType", 1),
                SyncAllBranches = GetBool(arguments, "syncAllBranches") ?? true
            };
        }

        var client = _connector.GetCodeupClient();
        var created = await client.CreateRepositoryAsync(request, organizationId, cancellationToken);
        if (created == null)
            return Fail("创建仓库失败");

        // 创建接口返回不含 HTTPS 克隆地址，补查仓库详情取 httpUrlToRepo
        if (string.IsNullOrWhiteSpace(created.HttpUrlToRepo) && created.Id.HasValue)
        {
            var detail = await client.GetRepositoryAsync(
                created.Id.Value.ToString(CultureInfo.InvariantCulture), organizationId, cancellationToken);
            if (detail != null)
            {
                created.HttpUrlToRepo ??= detail.HttpUrlToRepo;
                created.DefaultBranch ??= detail.DefaultBranch;
                created.SSHUrlToRepo ??= detail.SSHUrlToRepo;
            }
        }

        return Ok(created.ToJson());
    }

    private async Task<MCPToolResult> CodeupListGroupsAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var search = GetString(arguments, "search");
        var parentId = GetLong(arguments, "parentId");
        var page = GetInt(arguments, "page", 1);
        var perPage = GetInt(arguments, "perPage", 100);
        var orderBy = GetString(arguments, "orderBy");
        var sort = GetString(arguments, "sort");
        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetCodeupClient();
        var groups = await client.ListNamespacesAsync(
            search, parentId, page, perPage, orderBy, sort, organizationId, cancellationToken);

        if (groups == null)
            return Fail("查询代码组失败");

        return Ok(groups.ToJson());
    }

    private async Task<MCPToolResult> CodeupListBranchesAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var repositoryId = GetLong(arguments, "repositoryId");
        if (!repositoryId.HasValue)
            return Fail("缺少参数: repositoryId");

        var page = GetInt(arguments, "page", 1);
        var perPage = GetInt(arguments, "perPage", 100);
        var sort = GetString(arguments, "sort");
        var search = GetString(arguments, "search");
        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetCodeupClient();
        var branches = await client.ListBranchesAsync(
            repositoryId.Value, page, perPage, sort, search, organizationId, cancellationToken);

        if (branches == null)
            return Fail("查询分支失败");

        return Ok(branches.ToJson());
    }

    private async Task<MCPToolResult> CodeupCreateGroupAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var name = GetString(arguments, "name");
        var path = GetString(arguments, "path");
        if (string.IsNullOrWhiteSpace(name))
            return Fail("缺少参数: name");
        if (string.IsNullOrWhiteSpace(path))
            return Fail("缺少参数: path");

        var request = new YunxiaoCreateNamespaceRequest
        {
            Name = name,
            Path = path,
            ParentId = GetLong(arguments, "parentId"),
            Visibility = GetString(arguments, "visibility") ?? "internal",
            Description = GetString(arguments, "description")
        };

        var client = _connector.GetCodeupClient();
        var created = await client.CreateNamespaceAsync(
            request, GetString(arguments, "organizationId"), cancellationToken);

        return created == null
            ? Fail("创建代码组失败")
            : Ok(created.ToJson());
    }

    private async Task<MCPToolResult> CodeupUpdateRepoAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var repoId = GetString(arguments, "repositoryId");
        if (string.IsNullOrWhiteSpace(repoId))
            return Fail("缺少参数: repositoryId");

        var request = new YunxiaoUpdateRepositoryRequest
        {
            Name = GetString(arguments, "name"),
            VisibilityLevel = GetIntOrNull(arguments, "visibilityLevel"),
            Description = GetString(arguments, "description"),
            DefaultBranch = GetString(arguments, "defaultBranch")
        };

        var client = _connector.GetCodeupClient();
        var updated = await client.UpdateRepositoryAsync(
            repoId!, request, GetString(arguments, "organizationId"), cancellationToken);

        return updated == null
            ? Fail("更新代码仓库失败")
            : Ok(updated.ToJson());
    }

    #endregion

    #region Projex 工具实现

    private async Task<MCPToolResult> ProjexListProjectsAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var organizationId = GetString(arguments, "organizationId");
        var page = GetInt(arguments, "page", 1);
        var perPage = GetInt(arguments, "perPage", 20);
        var keyword = GetString(arguments, "keyword");

        var client = _connector.GetProjexClient();
        var result = await client.SearchProjectsAsync(
            organizationId, page, perPage, keyword, cancellationToken);

        if (result == null)
            return Fail("搜索研发项目失败");

        return Ok(result.ToJson());
    }

    private async Task<MCPToolResult> ProjexGetProjectAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var projectId = GetString(arguments, "projectId");
        if (string.IsNullOrWhiteSpace(projectId))
            return Fail("缺少参数: projectId");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var project = await client.GetProjectAsync(
            organizationId, projectId!, cancellationToken);

        if (project == null)
            return Fail("查询研发项目详情失败");

        return Ok(new
        {
            id = project.Id,
            name = project.Name,
            identifier = project.CustomCode,
            description = project.Description,
            scope = project.Scope,
            status = project.Status?.Name,
            statusId = project.Status?.Id,
            logicalStatus = project.LogicalStatus,
            created = project.GmtCreate,
            updated = project.DisplayUpdateTime
        }.ToJson());
    }

    private async Task<MCPToolResult> ProjexListSprintsAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var projectId = GetString(arguments, "projectId");
        if (string.IsNullOrWhiteSpace(projectId))
            return Fail("缺少参数: projectId");

        var organizationId = GetString(arguments, "organizationId");
        var status = GetString(arguments, "status");
        var page = GetInt(arguments, "page", 1);
        var perPage = GetInt(arguments, "perPage", 50);

        var client = _connector.GetProjexClient();
        var sprints = await client.ListSprintsAsync(
            organizationId, projectId!, status, page, perPage, cancellationToken);

        return Ok(sprints.ToJson());
    }

    private async Task<MCPToolResult> ProjexGetSprintAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var projectId = GetString(arguments, "projectId");
        var sprintId = GetString(arguments, "sprintId");
        if (string.IsNullOrWhiteSpace(projectId))
            return Fail("缺少参数: projectId");
        if (string.IsNullOrWhiteSpace(sprintId))
            return Fail("缺少参数: sprintId");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var sprint = await client.GetSprintAsync(
            organizationId, projectId!, sprintId!, cancellationToken);

        if (sprint == null)
            return Fail("查询迭代详情失败");

        return Ok(sprint.ToJson());
    }

    private async Task<MCPToolResult> ProjexCreateSprintAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var projectId = GetString(arguments, "projectId");
        var name = GetString(arguments, "name");
        if (string.IsNullOrWhiteSpace(projectId))
            return Fail("缺少参数: projectId");
        if (string.IsNullOrWhiteSpace(name))
            return Fail("缺少参数: name");

        var owners = GetStringList(arguments, "owners");
        if (owners is not { Count: > 0 })
            return Fail("缺少参数: owners（云效要求至少一项负责人 userId）");

        var organizationId = GetString(arguments, "organizationId");
        var startDate = GetString(arguments, "startDate");
        var endDate = GetString(arguments, "endDate");
        var description = GetString(arguments, "description");
        var capacityHours = GetIntOrNull(arguments, "capacityHours");
        var operatorId = GetString(arguments, "operatorId");

        var client = _connector.GetProjexClient();
        var id = await client.CreateSprintAsync(
            organizationId, projectId!, name!, owners, startDate, endDate, description,
            capacityHours, operatorId, cancellationToken);

        if (string.IsNullOrWhiteSpace(id))
            return Fail("创建迭代失败（响应缺 id）");

        return Ok(new { id }.ToJson());
    }

    private async Task<MCPToolResult> WorkitemListTypesAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var projectId = GetString(arguments, "projectId");
        var category = GetString(arguments, "category");
        if (string.IsNullOrWhiteSpace(projectId))
            return Fail("缺少参数: projectId");
        if (string.IsNullOrWhiteSpace(category))
            return Fail("缺少参数: category（Req / Bug / Task）");

        if (!IsValidCategory(category))
            return Fail("category 只能是 Req / Bug / Task");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var types = await client.ListWorkitemTypesAsync(
            organizationId, projectId!, category!, cancellationToken);

        return Ok(types.ToJson());
    }

    private async Task<MCPToolResult> WorkitemListStatusesAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var (workflow, error) = await ReadWorkflowAsync(arguments, cancellationToken);
        if (error != null)
            return error;

        return Ok(new
        {
            defaultStatusId = workflow!.DefaultStatusId,
            statuses = workflow.Statuses
        }.ToJson());
    }

    private async Task<MCPToolResult> WorkitemListWorkflowAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var (workflow, error) = await ReadWorkflowAsync(arguments, cancellationToken);
        if (error != null)
            return error;

        return Ok(workflow!.ToJson());
    }

    /// <summary>
    /// 拉取工作项工作流（状态 / 列序的共用实现）。
    /// </summary>
    /// <param name="arguments">工具参数（projectId / workitemTypeId / organizationId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作流与失败结果（成功时失败结果为 <c>null</c>）。</returns>
    private async Task<(YunxiaoWorkitemWorkflow? Workflow, MCPToolResult? Error)> ReadWorkflowAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var projectId = GetString(arguments, "projectId");
        var workitemTypeId = GetString(arguments, "workitemTypeId");
        if (string.IsNullOrWhiteSpace(projectId))
            return (null, Fail("缺少参数: projectId"));
        if (string.IsNullOrWhiteSpace(workitemTypeId))
            return (null, Fail("缺少参数: workitemTypeId（取自 workitem_list_types）"));

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var workflow = await client.GetWorkitemWorkflowAsync(
            organizationId, projectId!, workitemTypeId!, cancellationToken);

        return workflow == null
            ? (null, Fail("查询工作项工作流失败"))
            : (workflow, null);
    }

    /// <summary>
    /// 查询工作项（需求 / 任务共用实现，分类由工具名决定）。
    /// </summary>
    /// <param name="arguments">工具参数。</param>
    /// <param name="category">分类：<c>Req</c> / <c>Task</c>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具结果。</returns>
    private async Task<MCPToolResult> WorkitemSearchAsync(
        Dictionary<string, object?> arguments,
        string category,
        CancellationToken cancellationToken)
    {
        var projectId = GetString(arguments, "projectId");
        if (string.IsNullOrWhiteSpace(projectId))
            return Fail("缺少参数: projectId");

        var criteria = new YunxiaoWorkitemSearchCriteria
        {
            ProjectId = projectId!,
            Category = category,
            SprintId = GetString(arguments, "sprintId"),
            Status = GetString(arguments, "status"),
            Subject = GetString(arguments, "subject"),
            AssignedTo = GetString(arguments, "assignedTo"),
            WorkitemTypeId = GetString(arguments, "workitemTypeId"),
            OrderBy = GetString(arguments, "orderBy") ?? "gmtCreate",
            Sort = GetString(arguments, "sort") ?? "desc",
            Page = GetInt(arguments, "page", 1),
            PerPage = GetInt(arguments, "perPage", 50)
        };

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var result = await client.SearchWorkitemsAsync(organizationId, criteria, cancellationToken);

        if (result == null)
            return Fail("查询工作项失败");

        return Ok(new
        {
            items = result.Items,
            total = result.Total,
            page = result.Page,
            perPage = result.PerPage
        }.ToJson());
    }

    /// <summary>
    /// 创建工作项（需求 / 任务共用实现）。
    /// </summary>
    /// <param name="arguments">工具参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具结果（新建工作项 id）。</returns>
    private async Task<MCPToolResult> WorkitemCreateAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var projectId = GetString(arguments, "projectId");
        var subject = GetString(arguments, "subject");
        var workitemTypeId = GetString(arguments, "workitemTypeId");
        var assignedTo = GetString(arguments, "assignedTo");
        if (string.IsNullOrWhiteSpace(projectId))
            return Fail("缺少参数: projectId");
        if (string.IsNullOrWhiteSpace(subject))
            return Fail("缺少参数: subject");
        if (string.IsNullOrWhiteSpace(workitemTypeId))
            return Fail("缺少参数: workitemTypeId（取自 workitem_list_types）");
        if (string.IsNullOrWhiteSpace(assignedTo))
            return Fail("缺少参数: assignedTo（云效要求负责人 userId）");

        var customFieldValues = GetObjectDictionary(arguments, "customFieldValues");
        var priority = GetString(arguments, "priority");
        if (!string.IsNullOrWhiteSpace(priority))
        {
            customFieldValues ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            customFieldValues["priority"] = priority;
        }

        var request = new YunxiaoCreateWorkitemRequest
        {
            ProjectId = projectId!,
            Subject = subject!,
            WorkitemTypeId = workitemTypeId!,
            AssignedTo = assignedTo!,
            SprintId = GetString(arguments, "sprintId"),
            Description = GetString(arguments, "description"),
            FormatType = GetString(arguments, "formatType"),
            ParentId = GetString(arguments, "parentId"),
            Labels = GetStringList(arguments, "labels"),
            CustomFieldValues = customFieldValues
        };

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var id = await client.CreateWorkitemAsync(organizationId, request, cancellationToken);

        if (string.IsNullOrWhiteSpace(id))
            return Fail("创建工作项失败（响应缺 id）");

        return Ok(new { id }.ToJson());
    }

    private async Task<MCPToolResult> WorkitemUpdateAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var workitemId = GetString(arguments, "workitemId");
        if (string.IsNullOrWhiteSpace(workitemId))
            return Fail("缺少参数: workitemId");

        // 云效更新口径是扁平 {字段ID:值}，自定义字段与标准字段同级，不嵌 customFieldValues
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        AddIfPresent(fields, "subject", GetString(arguments, "subject"));
        AddIfPresent(fields, "description", GetString(arguments, "description"));
        AddIfPresent(fields, "formatType", GetString(arguments, "formatType"));
        AddIfPresent(fields, "status", GetString(arguments, "status"));
        AddIfPresent(fields, "assignedTo", GetString(arguments, "assignedTo"));
        AddIfPresent(fields, "sprint", GetString(arguments, "sprintId"));
        AddIfPresent(fields, "parentId", GetString(arguments, "parentId"));

        var labels = GetStringList(arguments, "labels");
        if (labels is { Count: > 0 })
            fields["labels"] = labels;

        var customFieldValues = GetObjectDictionary(arguments, "customFieldValues");
        if (customFieldValues != null)
        {
            foreach (var (key, value) in customFieldValues)
                fields[key] = value;
        }

        if (fields.Count == 0)
            return Fail("没有可更新字段");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        await client.UpdateWorkitemAsync(organizationId, workitemId!, fields, cancellationToken);

        return Ok(new { updated = true }.ToJson());
    }

    private async Task<MCPToolResult> BoardListWorkitemsBySprintAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var projectId = GetString(arguments, "projectId");
        var sprintId = GetString(arguments, "sprintId");
        if (string.IsNullOrWhiteSpace(projectId))
            return Fail("缺少参数: projectId");
        if (string.IsNullOrWhiteSpace(sprintId))
            return Fail("缺少参数: sprintId（迭代 ID）");

        var category = GetString(arguments, "category") ?? "Req";
        if (!IsValidCategory(category))
            return Fail("category 只能是 Req / Bug / Task");

        var criteria = new YunxiaoWorkitemSearchCriteria
        {
            ProjectId = projectId!,
            Category = category,
            SprintId = sprintId,
            Status = GetString(arguments, "status"),
            AssignedTo = GetString(arguments, "assignedTo"),
            OrderBy = GetString(arguments, "orderBy") ?? "gmtCreate",
            Sort = GetString(arguments, "sort") ?? "desc",
            Page = GetInt(arguments, "page", 1),
            PerPage = GetInt(arguments, "perPage", 100)
        };

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var result = await client.SearchWorkitemsAsync(organizationId, criteria, cancellationToken);

        if (result == null)
            return Fail("查询看板工作项失败");

        return Ok(new
        {
            items = result.Items,
            total = result.Total,
            page = result.Page,
            perPage = result.PerPage
        }.ToJson());
    }

    private async Task<MCPToolResult> WorkitemListCommentsAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var workitemId = GetString(arguments, "workitemId");
        if (string.IsNullOrWhiteSpace(workitemId))
            return Fail("缺少参数: workitemId");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var comments = await client.ListWorkitemCommentsAsync(organizationId, workitemId!, cancellationToken);

        return Ok(comments.ToJson());
    }

    private async Task<MCPToolResult> WorkitemGetAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var workitemId = GetString(arguments, "workitemId");
        if (string.IsNullOrWhiteSpace(workitemId))
            return Fail("缺少参数: workitemId");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var workitem = await client.GetWorkitemAsync(organizationId, workitemId!, cancellationToken);

        if (workitem == null)
            return Fail("查询工作项详情失败");

        return Ok(workitem.ToJson());
    }

    private async Task<MCPToolResult> WorkitemListAttachmentsAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var workitemId = GetString(arguments, "workitemId");
        if (string.IsNullOrWhiteSpace(workitemId))
            return Fail("缺少参数: workitemId");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var attachments = await client.ListWorkitemAttachmentsAsync(organizationId, workitemId!, cancellationToken);

        return Ok(attachments.ToJson());
    }

    private async Task<MCPToolResult> WorkitemGetFileAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var workitemId = GetString(arguments, "workitemId");
        var fileId = GetString(arguments, "fileId");
        if (string.IsNullOrWhiteSpace(workitemId))
            return Fail("缺少参数: workitemId");
        if (string.IsNullOrWhiteSpace(fileId))
            return Fail("缺少参数: fileId（取自附件列表）");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var file = await client.GetWorkitemFileAsync(organizationId, workitemId!, fileId!, cancellationToken);

        if (file == null)
            return Fail("查询工作项文件失败");

        return Ok(file.ToJson());
    }

    /// <summary>创建工作项关系（PARENT / SUB / ASSOCIATED / DEPEND_ON / DEPENDED_BY）。</summary>
    /// <param name="arguments">工具参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具结果（新建关系 id）。</returns>
    private async Task<MCPToolResult> WorkitemCreateRelationAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var workitemId = GetString(arguments, "workitemId");
        var targetWorkitemId = GetString(arguments, "targetWorkitemId");
        var relationType = GetString(arguments, "relationType");
        if (string.IsNullOrWhiteSpace(workitemId))
            return Fail("缺少参数: workitemId");
        if (string.IsNullOrWhiteSpace(targetWorkitemId))
            return Fail("缺少参数: targetWorkitemId");
        if (!ProjexClient.IsValidRelationType(relationType))
            return Fail("缺少或非法参数: relationType（PARENT / SUB / ASSOCIATED / DEPEND_ON / DEPENDED_BY）");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var id = await client.CreateWorkitemRelationAsync(
            organizationId, workitemId!, relationType!, targetWorkitemId!, cancellationToken);

        if (string.IsNullOrWhiteSpace(id))
            return Fail("创建工作项关系失败（响应缺 id）");

        return Ok(new { id }.ToJson());
    }

    /// <summary>查询测试用例库列表。</summary>
    /// <param name="arguments">工具参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具结果（用例库数组）。</returns>
    private async Task<MCPToolResult> TestRepoListAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var repos = await client.ListTestReposAsync(
            organizationId,
            GetInt(arguments, "page", 1),
            GetInt(arguments, "perPage", 100),
            GetString(arguments, "name"),
            cancellationToken);

        return Ok(repos.ToJson());
    }

    /// <summary>创建测试用例。</summary>
    /// <param name="arguments">工具参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具结果（新建用例 id）。</returns>
    private async Task<MCPToolResult> TestcaseCreateAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var testRepoId = GetString(arguments, "testRepoId");
        var subject = GetString(arguments, "subject");
        if (string.IsNullOrWhiteSpace(testRepoId))
            return Fail("缺少参数: testRepoId（取自 testrepo_list）");
        if (string.IsNullOrWhiteSpace(subject))
            return Fail("缺少参数: subject");

        var request = new YunxiaoCreateTestcaseRequest
        {
            TestRepoId = testRepoId!,
            Subject = subject!,
            AssignedTo = GetString(arguments, "assignedTo"),
            DirectoryId = GetString(arguments, "directoryId"),
            PreCondition = GetString(arguments, "preCondition"),
            Labels = GetStringList(arguments, "labels"),
            CustomFieldValues = GetObjectDictionary(arguments, "customFieldValues"),
            TestSteps = GetObjectList(arguments, "testSteps")
        };

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var id = await client.CreateTestcaseAsync(organizationId, request, cancellationToken);

        if (string.IsNullOrWhiteSpace(id))
            return Fail("创建测试用例失败（响应缺 id）");

        return Ok(new { id }.ToJson());
    }

    /// <summary>创建测试计划（字段未真机验证）。</summary>
    /// <param name="arguments">工具参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具结果（计划标识）。</returns>
    private async Task<MCPToolResult> TestplanCreateAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var name = GetString(arguments, "name");
        if (string.IsNullOrWhiteSpace(name))
            return Fail("缺少参数: name");

        var request = new YunxiaoCreateTestPlanRequest
        {
            Name = name!,
            ProjectId = GetString(arguments, "projectId"),
            SprintId = GetString(arguments, "sprintId"),
            StartDate = GetString(arguments, "startDate"),
            EndDate = GetString(arguments, "endDate"),
            Managers = GetStringList(arguments, "managers"),
            CaseIds = GetStringList(arguments, "caseIds")
        };

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetProjexClient();
        var id = await client.CreateTestPlanAsync(organizationId, request, cancellationToken);

        if (string.IsNullOrWhiteSpace(id))
            return Fail("创建测试计划失败（响应缺标识）");

        return Ok(new { id }.ToJson());
    }

    /// <summary>工作项分类只接受云效类型列表接口支持的三种。</summary>
    /// <param name="category">分类值。</param>
    /// <returns>合法返回 true。</returns>
    private static bool IsValidCategory(string? category)
        => category is "Req" or "Bug" or "Task";

    /// <summary>字段值非空时写入字典（云效更新接口不接受 null 值）。</summary>
    /// <param name="fields">目标字典。</param>
    /// <param name="key">字段名。</param>
    /// <param name="value">字段值。</param>
    private static void AddIfPresent(Dictionary<string, object?> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields[key] = value;
    }

    #endregion

    #region Flow 工具实现

    /// <summary>
    /// 列出流水线（<b>真机口径</b>：成功响应是裸数组，元素字段为 <c>pipelineId</c> / <c>pipelineName</c>）。
    /// </summary>
    /// <param name="arguments">参数（pipelineName / page / perPage / statusList / organizationId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具结果。</returns>
    private async Task<MCPToolResult> FlowListPipelinesAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var pipelineName = GetString(arguments, "pipelineName");
        var maxResults = GetInt(arguments, "maxResults", 100);
        var statusList = GetString(arguments, "statusList");
        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetFlowClient();
        var result = await client.ListPipelinesAsync(
            pipelineName, maxResults, statusList, organizationId, cancellationToken);

        return Ok(result.ToJson());
    }

    /// <summary>
    /// 列出主机组（<b>只读</b>：供「创建流水线之前选部署目标」；真机口径 = 裸数组，<c>hostInfos</c> 恒为 <c>null</c>）。
    /// </summary>
    /// <param name="arguments">参数（organizationId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具结果。</returns>
    private async Task<MCPToolResult> FlowListHostGroupsAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetFlowClient();
        var result = await client.ListHostGroupsAsync(organizationId, cancellationToken);

        if (result == null)
            return Fail("列出主机组失败");

        return Ok(result.ToJson());
    }

    /// <summary>
    /// 查询主机组详情（<b>只读</b>：含机器明细 <c>hostInfos</c>；<c>hostGroupId</c> 必须是数字 id）。
    /// </summary>
    /// <param name="arguments">参数（hostGroupId / organizationId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具结果。</returns>
    private async Task<MCPToolResult> FlowGetHostGroupAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var hostGroupId = GetLong(arguments, "hostGroupId");
        if (!hostGroupId.HasValue || hostGroupId.Value <= 0)
            return Fail("缺少参数: hostGroupId（必须是数字 id，不是 uuid）");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetFlowClient();
        var result = await client.GetHostGroupAsync(hostGroupId.Value, organizationId, cancellationToken);

        if (result == null)
            return Fail("查询主机组详情失败");

        return Ok(result.ToJson());
    }

    /// <summary>
    /// 创建流水线：提交 <c>name</c> + 新版 YAML <c>content</c>。
    /// </summary>
    /// <remarks>
    /// 云效 OpenAPI <b>没有</b>「复制流水线」接口，且 <c>content</c> 只收顶层 <c>stages:</c> 的新版 YAML
    /// （喂控制台 tb 形态 <c>flow</c> 原文必报 <c>1209300</c>「至少要包含一个 stage」）。
    /// 故本工具口径为「调用方自备新版 YAML → 提交创建」，不再做「读源 YAML → 替换 → 创建」。
    /// </remarks>
    /// <param name="arguments">参数（name / content / organizationId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具结果（成功时返回新流水线 id）。</returns>
    private async Task<MCPToolResult> FlowCreatePipelineAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var name = GetString(arguments, "name");
        var content = GetString(arguments, "content");

        if (string.IsNullOrWhiteSpace(name))
            return Fail("缺少参数: name");
        if (string.IsNullOrWhiteSpace(content))
            return Fail("缺少参数: content");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetFlowClient();
        var pipelineId = await client.CreatePipelineAsync(
            new YunxiaoCreatePipelineRequest { PipelineName = name, Content = content },
            organizationId,
            cancellationToken);

        return string.IsNullOrWhiteSpace(pipelineId)
            ? Fail("创建流水线失败")
            : Ok(new { success = true, pipelineId, name }.ToJson());
    }

    private async Task<MCPToolResult> FlowUpdatePipelineAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var pipelineId = GetString(arguments, "pipelineId");
        var name = GetString(arguments, "name");
        var content = GetString(arguments, "content");

        if (string.IsNullOrWhiteSpace(pipelineId))
            return Fail("缺少参数: pipelineId");
        if (string.IsNullOrWhiteSpace(name))
            return Fail("缺少参数: name");
        if (string.IsNullOrWhiteSpace(content))
            return Fail("缺少参数: content");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetFlowClient();
        var ok = await client.UpdatePipelineAsync(
            pipelineId!, new YunxiaoUpdatePipelineRequest { PipelineName = name, Content = content },
            organizationId, cancellationToken);

        return ok
            ? Ok(new { success = true, pipelineId }.ToJson())
            : Fail("更新流水线失败");
    }

    private async Task<MCPToolResult> FlowRunPipelineAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var pipelineId = GetString(arguments, "pipelineId");
        if (string.IsNullOrWhiteSpace(pipelineId))
            return Fail("缺少参数: pipelineId");

        var pipelineRunId = GetString(arguments, "pipelineRunId");
        var jobId = GetString(arguments, "jobId");
        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetFlowClient();

        // 传入 pipelineRunId + jobId → 运行单个任务；否则触发整条流水线运行
        if (!string.IsNullOrWhiteSpace(pipelineRunId) && !string.IsNullOrWhiteSpace(jobId))
        {
            var jobResult = await client.ExecutePipelineJobRunAsync(
                pipelineId!, pipelineRunId!, jobId!, organizationId, cancellationToken);
            return jobResult
                ? Ok(new { success = true, pipelineId, pipelineRunId, jobId }.ToJson())
                : Fail("运行流水线任务失败");
        }

        var request = new YunxiaoRunPipelineRequest
        {
            Branch = GetString(arguments, "branch"),
            Environment = GetDictionary(arguments, "env")
        };

        var result = await client.RunPipelineAsync(pipelineId!, request, organizationId, cancellationToken);

        return result == null
            ? Fail("运行流水线失败")
            : Ok(new { success = true, run = result }.ToJson());
    }

    private async Task<MCPToolResult> FlowGetPipelineAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var pipelineId = GetString(arguments, "pipelineId");
        if (string.IsNullOrWhiteSpace(pipelineId))
            return Fail("缺少参数: pipelineId");

        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetFlowClient();
        var pipeline = await client.GetPipelineAsync(
            pipelineId!, organizationId, cancellationToken);

        if (pipeline == null)
            return Fail("查询流水线详情失败");

        return Ok(new
        {
            id = pipeline.DisplayId,
            name = pipeline.DisplayName,
            status = pipeline.Status,
            yaml = pipeline.PipelineConfig?.Flow
        }.ToJson());
    }

    #endregion

    #region 辅助方法

    private static string? GetString(Dictionary<string, object?> arguments, string key)
    {
        return arguments.GetValueOrDefault(key)?.ToString();
    }

    /// <summary>
    /// 取整型参数（兼容数字与数字字符串：模型可能把 ID 当字符串传）。
    /// </summary>
    private static long? GetLong(Dictionary<string, object?> arguments, string key)
    {
        var value = arguments.GetValueOrDefault(key);
        if (value == null) return null;

        if (value is long longValue) return longValue;
        return long.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static int GetInt(Dictionary<string, object?> arguments, string key, int defaultValue)
    {
        var value = arguments.GetValueOrDefault(key);
        if (value == null) return defaultValue;

        if (value is int intValue) return intValue;
        if (int.TryParse(value.ToString(), out var parsed)) return parsed;

        return defaultValue;
    }

    /// <summary>
    /// 取可空整型参数（不存在或非法返回 null）。
    /// </summary>
    private static int? GetIntOrNull(Dictionary<string, object?> arguments, string key)
    {
        var value = arguments.GetValueOrDefault(key);
        if (value == null) return null;

        if (value is int intValue) return intValue;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool? GetBool(Dictionary<string, object?> arguments, string key)
    {
        var value = arguments.GetValueOrDefault(key);
        if (value == null) return null;

        if (value is bool boolValue) return boolValue;
        if (bool.TryParse(value.ToString(), out var parsed)) return parsed;

        return null;
    }

    private static Dictionary<string, string>? GetDictionary(Dictionary<string, object?> arguments, string key)
    {
        var value = arguments.GetValueOrDefault(key);
        if (value == null) return null;

        if (value is Dictionary<string, string> dict) return dict;

        // 尝试从 Dictionary<string, object?> 转换
        if (value is Dictionary<string, object?> objDict)
        {
            return objDict.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString() ?? string.Empty);
        }

        return null;
    }

    /// <summary>
    /// 取字符串数组参数（兼容 JSON 数组与逗号分隔字符串）。
    /// </summary>
    private static List<string>? GetStringList(Dictionary<string, object?> arguments, string key)
    {
        var value = arguments.GetValueOrDefault(key);
        if (value == null) return null;

        if (value is string text)
        {
            var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length > 0 ? [.. parts] : null;
        }

        if (value is not System.Collections.IEnumerable enumerable)
            return null;

        var list = new List<string>();
        foreach (var item in enumerable)
        {
            if (item == null) continue;

            if (item is System.Text.Json.JsonElement element)
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var child in element.EnumerateArray())
                    {
                        var childText = child.ValueKind == System.Text.Json.JsonValueKind.String
                            ? child.GetString()
                            : child.ToString();
                        if (!string.IsNullOrWhiteSpace(childText)) list.Add(childText!.Trim());
                    }
                    continue;
                }

                var text2 = element.ValueKind == System.Text.Json.JsonValueKind.String
                    ? element.GetString()
                    : element.ToString();
                if (!string.IsNullOrWhiteSpace(text2)) list.Add(text2!.Trim());
                continue;
            }

            var itemText = item.ToString();
            if (!string.IsNullOrWhiteSpace(itemText)) list.Add(itemText!.Trim());
        }

        return list.Count > 0 ? list : null;
    }

    /// <summary>
    /// 取对象字典参数（自定义字段值，值保持原始类型）。
    /// </summary>
    private static Dictionary<string, object?>? GetObjectDictionary(Dictionary<string, object?> arguments, string key)
    {
        var value = arguments.GetValueOrDefault(key);
        if (value == null) return null;

        if (value is System.Text.Json.JsonElement element &&
            element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                dict[property.Name] = property.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => property.Value.GetString(),
                    System.Text.Json.JsonValueKind.Number => property.Value.GetRawText(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };
            }
            return dict;
        }

        if (value is Dictionary<string, object?> objectDict)
            return new Dictionary<string, object?>(objectDict, StringComparer.Ordinal);

        return null;
    }

    /// <summary>
    /// 取对象数组参数（按原始 JSON 结构透传，如测试步骤）。
    /// </summary>
    private static IReadOnlyList<object>? GetObjectList(Dictionary<string, object?> arguments, string key)
    {
        var value = arguments.GetValueOrDefault(key);
        if (value == null) return null;

        if (value is System.Text.Json.JsonElement element)
        {
            if (element.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            return System.Text.Json.JsonSerializer.Deserialize<List<object>>(element.GetRawText());
        }

        return value as IReadOnlyList<object>;
    }

    /// <summary>
    /// 由 HTTPS 克隆地址推导仓库 Web 地址（去掉尾部 <c>.git</c>）。
    /// </summary>
    /// <param name="cloneUrl">克隆地址（可为空）。</param>
    /// <returns>Web 地址；入参为空时返回空串。</returns>
    private static string BuildWebUrl(string? cloneUrl)
        => string.IsNullOrWhiteSpace(cloneUrl)
            ? string.Empty
            : cloneUrl!.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? cloneUrl[..^4]
                : cloneUrl;

    #endregion

    /// <inheritdoc />
    /// <remarks>
    /// 只清理自身状态：<b>不</b>释放 <see cref="YunxiaoConnector"/>。连接器由 DI 容器以宿主单例持有，
    /// MCP 客户端对它是<b>借用</b>关系——若在此释放，某个会话反注册会连带把全局连接器 Dispose 掉，
    /// 导致其余会话在 <c>GetCodeupClient()</c> 抛 <see cref="ObjectDisposedException"/>。
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}