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
/// 云效进程内 MCP 客户端 - 包装 <see cref="YunxiaoConnector"/> 暴露 10 个工具。
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
                "codeup_clone" => await CodeupCloneAsync(arguments, cancellationToken),
                "codeup_create_repo" => await CodeupCreateRepoAsync(arguments, cancellationToken),
                "codeup_push" => await CodeupPushAsync(arguments, cancellationToken),
                "codeup_list_branches" => await CodeupListBranchesAsync(arguments, cancellationToken),
                "codeup_create_group" => await CodeupCreateGroupAsync(arguments, cancellationToken),
                "flow_list_pipelines" => await FlowListPipelinesAsync(arguments, cancellationToken),
                "flow_clone_pipeline" => await FlowClonePipelineAsync(arguments, cancellationToken),
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

    private async Task<MCPToolResult> CodeupCloneAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var httpsUrl = GetString(arguments, "httpsUrl");
        var localPath = GetString(arguments, "localPath");
        if (string.IsNullOrWhiteSpace(httpsUrl))
            return Fail("缺少参数: httpsUrl");
        if (string.IsNullOrWhiteSpace(localPath))
            return Fail("缺少参数: localPath");

        var branch = GetString(arguments, "branch");

        var client = _connector.GetCodeupClient();
        var result = await client.CloneToLocalAsync(httpsUrl!, localPath!, branch, cancellationToken);

        return result.Success
            ? Ok(new { localPath, commitId = (string?)null, branch }.ToJson())
            : Fail($"克隆失败：{result.FailureSummary()}");
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

    private async Task<MCPToolResult> CodeupPushAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var localPath = GetString(arguments, "localPath");
        var remoteUrl = GetString(arguments, "remoteUrl");

        if (string.IsNullOrWhiteSpace(localPath))
            return Fail("缺少参数: localPath");
        if (string.IsNullOrWhiteSpace(remoteUrl))
            return Fail("缺少参数: remoteUrl");

        var branch = GetString(arguments, "branch");
        var force = GetBool(arguments, "force") ?? false;

        var client = _connector.GetCodeupClient();
        var result = await client.PushAsync(remoteUrl!, localPath!, branch, force, cancellationToken);

        return result.Success
            ? Ok(new { localPath, remoteUrl = SecretMasker.MaskUrlCredentials(remoteUrl), branch }.ToJson())
            : Fail(result.IsRateLimited
                // v0.29：限流与权限 / 分支保护分开措辞——真机里两者曾被混为一谈（同一条 PAT 11 秒后重推即成功）
                ? $"推送失败：{result.FailureSummary()}"
                  + "（云效服务端限流：请等约 1 分钟后再推，已推送内容不受影响；不要据此判定 PAT 或分支保护有问题）"
                : $"推送失败：{result.FailureSummary()}");
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

    private async Task<MCPToolResult> FlowClonePipelineAsync(
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var sourcePipelineId = GetString(arguments, "sourcePipelineId");
        var newName = GetString(arguments, "newName");

        if (string.IsNullOrWhiteSpace(sourcePipelineId))
            return Fail("缺少参数: sourcePipelineId");
        if (string.IsNullOrWhiteSpace(newName))
            return Fail("缺少参数: newName");

        var substitutions = GetDictionary(arguments, "substitutions");
        var disableDeployStage = GetBool(arguments, "disableDeployStage") ?? true;
        var organizationId = GetString(arguments, "organizationId");

        var client = _connector.GetFlowClient();
        var result = await client.ClonePipelineFromTemplateAsync(
            sourcePipelineId!, newName!, substitutions, disableDeployStage, organizationId, cancellationToken);

        if (result == null)
            return Fail("克隆流水线失败");

        return Ok(result.ToJson());
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
        var result = await client.UpdatePipelineAsync(
            pipelineId!, new YunxiaoUpdatePipelineRequest { PipelineName = name, Content = content },
            organizationId, cancellationToken);

        return result == null
            ? Fail("更新流水线失败")
            : Ok(new { success = true, pipelineId }.ToJson());
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