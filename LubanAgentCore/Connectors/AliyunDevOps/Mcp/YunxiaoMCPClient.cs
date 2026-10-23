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

using System.Globalization;

namespace LubanAgentCore.Connectors.AliyunDevOps.Mcp;

/// <summary>
/// 云效进程内 MCP 客户端 - 包装 <see cref="YunxiaoConnector"/> 暴露 13 个工具。
/// </summary>
/// <remarks>
/// 采用进程内形态（而非 stdio 子进程）是为了避免把 PAT 明文写进
/// <c>.luban-agent/mcps/*.json</c> 的 Args 字段（PRD §4.11.3 注②）。
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
    /// 本实例的权限环境（由宿主在创建时注入，PRD §3.7）。
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
        // 以插件身份执行：出口门禁据此判定越界（PRD §3.7）
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
                "flow_list_pipelines" => await FlowListPipelinesAsync(arguments, cancellationToken),
                "flow_list_host_groups" => await FlowListHostGroupsAsync(arguments, cancellationToken),
                "flow_get_host_group" => await FlowGetHostGroupAsync(arguments, cancellationToken),
                "flow_create_pipeline" => await FlowCreatePipelineAsync(arguments, cancellationToken),
                "flow_update_pipeline" => await FlowUpdatePipelineAsync(arguments, cancellationToken),
                "flow_run_pipeline" => await FlowRunPipelineAsync(arguments, cancellationToken),
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

        // 创建接口返回不含 HTTPS 克隆地址，补查仓库详情取 httpUrlToRepo（PRD §4.11.3 注③）
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
        var page = GetInt(arguments, "page", 1);
        var perPage = GetInt(arguments, "perPage", 20);
        var statusList = GetString(arguments, "statusList");
        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetFlowClient();
        var result = await client.ListPipelinesAsync(
            pipelineName, page, perPage, statusList, organizationId, cancellationToken);

        if (result == null)
            return Fail("列出流水线失败");

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
    /// 创建流水线：提交 <c>name</c> + 新版 YAML <c>content</c>（PRD §4.11.3 注⑤ v0.30）。
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