/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Connectors.AliyunDevOps
*文件名： FlowClient
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：云效流水线客户端
*
*****************************************************************************/

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LubanAgent.Connectors.AliyunDevOps;

/// <summary>
/// 云效流水线客户端 - 提供流水线管理、执行等能力
/// </summary>
public class FlowClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly YunxiaoApiOptions _options;
    private readonly IYunxiaoCredentialProvider _credentialProvider;
    private readonly PluginPermissionGuard _guard;
    private readonly string? _connectorName;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // 部署阶段相关的资源字面量：服务连接 / 主机组 / 构建集群 / 制品仓库。
    // 云效 OpenAPI 无「复制流水线」接口，克隆实为 YAML 文本复制，官方不做资源重绑定，
    // 故克隆后需产出「待人工绑定清单」（PRD §6 #14）。
    private static readonly Regex ResourceLiteralPattern = new(
        @"(?im)^\s*(serviceConnection|machineGroup|runsOn|hostGroup|artifactRepo|endpoint)\s*:\s*(?<v>.+?)\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// 创建 FlowClient 实例
    /// </summary>
    /// <param name="options">API 配置</param>
    /// <param name="credentialProvider">凭据提供者</param>
    /// <param name="permissionGuard">插件权限门禁（可选；不传则用进程级兜底实例）。</param>
    /// <param name="connectorName">所属连接器实例名（可选，Q15）；决定 PAT 凭据键，<c>null</c> 视为默认实例。</param>
    public FlowClient(
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

    #region 流水线管理 API

    /// <summary>
    /// 列出流水线
    /// </summary>
    /// <param name="pipelineName">流水线名称过滤（可选）</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="perPage">每页条数</param>
    /// <param name="statusList">状态过滤（可选，逗号分隔）</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>流水线列表</returns>
    public async Task<YunxiaoPagedResult<YunxiaoPipeline>?> ListPipelinesAsync(
        string? pipelineName = null,
        int page = 1,
        int perPage = 20,
        string? statusList = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/flow/organizations/{orgId}/pipelines";
        var queryParams = new List<string>
        {
            $"page={page}",
            $"perPage={perPage}"
        };

        if (!string.IsNullOrWhiteSpace(pipelineName))
            queryParams.Add($"pipelineName={Uri.EscapeDataString(pipelineName)}");
        if (!string.IsNullOrWhiteSpace(statusList))
            queryParams.Add($"statusList={Uri.EscapeDataString(statusList!)}");

        var result = await SendRequestAsync<YunxiaoPagedResult<YunxiaoPipeline>>(
            HttpMethod.Get,
            $"{path}?{string.Join("&", queryParams)}",
            cancellationToken: cancellationToken);

        return result;
    }

    /// <summary>
    /// 获取流水线详情
    /// </summary>
    /// <param name="pipelineId">流水线 ID</param>
    /// <param name="organizationId">组织 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>流水线详情</returns>
    public async Task<YunxiaoPipeline?> GetPipelineAsync(
        string pipelineId,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipelineId))
            throw new ArgumentException("流水线 ID 不能为空", nameof(pipelineId));

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/flow/organizations/{orgId}/pipelines/{pipelineId}";

        var result = await SendRequestAsync<YunxiaoPipeline>(
            HttpMethod.Get,
            path,
            cancellationToken: cancellationToken);

        return result;
    }

    /// <summary>
    /// 创建流水线
    /// </summary>
    /// <param name="request">创建请求</param>
    /// <param name="organizationId">组织 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的流水线</returns>
    public async Task<YunxiaoPipeline?> CreatePipelineAsync(
        YunxiaoCreatePipelineRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/flow/organizations/{orgId}/pipelines";

        var result = await SendRequestAsync<YunxiaoPipeline>(
            HttpMethod.Post,
            path,
            request,
            cancellationToken);

        return result;
    }

    /// <summary>
    /// 更新流水线
    /// </summary>
    /// <param name="pipelineId">流水线 ID</param>
    /// <param name="request">更新请求</param>
    /// <param name="organizationId">组织 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的流水线</returns>
    public async Task<YunxiaoPipeline?> UpdatePipelineAsync(
        string pipelineId,
        YunxiaoUpdatePipelineRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipelineId))
            throw new ArgumentException("流水线 ID 不能为空", nameof(pipelineId));

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/flow/organizations/{orgId}/pipelines/{pipelineId}";

        var result = await SendRequestAsync<YunxiaoPipeline>(
            HttpMethod.Put,
            path,
            request,
            cancellationToken);

        return result;
    }

    /// <summary>
    /// 从模板克隆流水线（读取源 YAML → 变量替换 → 创建）
    /// </summary>
    /// <param name="sourcePipelineId">源流水线 ID</param>
    /// <param name="newName">新流水线名称</param>
    /// <param name="substitutions">变量替换映射（YAML 中的占位符 → 实际值）</param>
    /// <param name="disableDeployStage">是否默认禁用部署阶段（PRD §6 #14，默认 true）</param>
    /// <param name="organizationId">组织 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>克隆结果（含待人工绑定清单）</returns>
    /// <remarks>
    /// 云效 OpenAPI <b>没有</b>「复制流水线」接口，此处为自实现：读取源流水线 YAML 文本、
    /// 做占位符替换后创建新流水线。因此流水线中引用的服务连接 / 主机组 / 构建集群等
    /// 资源不会被官方重绑定，需由用户人工确认（见 <see cref="YunxiaoPipelineCloneResult.PendingBindings"/>）。
    /// </remarks>
    public async Task<YunxiaoPipelineCloneResult?> ClonePipelineFromTemplateAsync(
        string sourcePipelineId,
        string newName,
        Dictionary<string, string>? substitutions = null,
        bool disableDeployStage = true,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePipelineId))
            throw new ArgumentException("源流水线 ID 不能为空", nameof(sourcePipelineId));

        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("新流水线名称不能为空", nameof(newName));

        // 1. 读取源流水线
        var sourcePipeline = await GetPipelineAsync(sourcePipelineId, organizationId, cancellationToken);
        if (sourcePipeline?.PipelineConfig?.Flow == null)
            throw new InvalidOperationException("源流水线不存在或未配置 YAML");

        // 2. 收集「待人工绑定清单」（部署相关资源字面量，去重）
        var pendingBindings = new List<string>();
        foreach (Match match in ResourceLiteralPattern.Matches(sourcePipeline.PipelineConfig.Flow))
        {
            var literal = $"{match.Groups[1].Value}: {match.Groups[2].Value}";
            if (!pendingBindings.Contains(literal, StringComparer.OrdinalIgnoreCase))
                pendingBindings.Add(literal);
        }

        // 3. 替换变量
        var yaml = sourcePipeline.PipelineConfig.Flow;
        if (substitutions != null && substitutions.Count > 0)
        {
            foreach (var kvp in substitutions)
            {
                yaml = yaml.Replace($"${{{kvp.Key}}}", kvp.Value);
            }
        }

        // 4. 默认禁用部署阶段（PRD §6 #14）。
        //    云效 Flow YAML 结构随版本变化，这里只做「显式 disabled: false → true」的保守替换，
        //    不做结构性插入，避免损坏 YAML；未能命中的部署阶段通过待人工绑定清单提示人工处理。
        var deployStageDisabled = false;
        if (disableDeployStage)
        {
            var replaced = Regex.Replace(yaml, @"(?im)^(\s*disabled\s*:\s*)false\b", "${1}true");
            deployStageDisabled = !ReferenceEquals(replaced, yaml) && !string.Equals(replaced, yaml, StringComparison.Ordinal);
            yaml = replaced;
        }

        // 5. 创建新流水线
        var createRequest = new YunxiaoCreatePipelineRequest
        {
            PipelineName = newName,
            Content = yaml
        };

        var created = await CreatePipelineAsync(createRequest, organizationId, cancellationToken);

        return new YunxiaoPipelineCloneResult
        {
            PipelineId = created?.PipelineId,
            PipelineName = created?.PipelineName ?? newName,
            DeployStageDisabled = deployStageDisabled,
            PendingBindings = pendingBindings
        };
    }

    /// <summary>
    /// 触发整条流水线运行（CreatePipelineRun）
    /// </summary>
    /// <param name="pipelineId">流水线 ID</param>
    /// <param name="request">运行请求</param>
    /// <param name="organizationId">组织 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>运行结果</returns>
    public async Task<YunxiaoPipelineRun?> RunPipelineAsync(
        string pipelineId,
        YunxiaoRunPipelineRequest? request = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipelineId))
            throw new ArgumentException("流水线 ID 不能为空", nameof(pipelineId));

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        // 云效「创建流水线运行」接口为 POST /pipelines/{id}/runs
        var path = $"/oapi/v1/flow/organizations/{orgId}/pipelines/{pipelineId}/runs";

        var result = await SendRequestAsync<YunxiaoPipelineRun>(
            HttpMethod.Post,
            path,
            request,
            cancellationToken);

        return result;
    }

    /// <summary>
    /// 运行某个流水线运行实例中的单个任务（ExecutePipelineJobRun）
    /// </summary>
    /// <param name="pipelineId">流水线 ID</param>
    /// <param name="pipelineRunId">运行实例 ID</param>
    /// <param name="jobId">任务 ID</param>
    /// <param name="organizationId">组织 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否执行成功</returns>
    public async Task<bool> ExecutePipelineJobRunAsync(
        string pipelineId,
        string pipelineRunId,
        string jobId,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipelineId))
            throw new ArgumentException("流水线 ID 不能为空", nameof(pipelineId));
        if (string.IsNullOrWhiteSpace(pipelineRunId))
            throw new ArgumentException("运行实例 ID 不能为空", nameof(pipelineRunId));
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("任务 ID 不能为空", nameof(jobId));

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/flow/organizations/{orgId}/pipelines/{pipelineId}/runs/{pipelineRunId}/jobs/{jobId}/run";

        await SendRequestAsync<object>(HttpMethod.Post, path, null, cancellationToken);

        return true;
    }

    #endregion

    #region HTTP 请求

    private async Task<T?> SendRequestAsync<T>(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        // 出口门禁（PRD §3.7）：目标主机 + PAT 凭据
        _guard.RequireNetwork(_options.Endpoint, "FlowClient.SendRequestAsync");
        _guard.RequireCredential(PatCredentialKey, "FlowClient.SendRequestAsync");

        var pat = await _credentialProvider.GetPatAsync(_connectorName, cancellationToken);
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
                    var errorMsg = YunxiaoErrorMapper.MapHttpStatus((int)response.StatusCode, responseBody);

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

                // 解析响应
                var apiResponse = JsonSerializer.Deserialize<YunxiaoApiResponse<T>>(responseBody, JsonOpts);

                if (apiResponse == null)
                    throw new YunxiaoApiException(0, "响应解析失败");

                if (apiResponse.ErrorCode != 0)
                {
                    var errorMsg = YunxiaoErrorMapper.MapBusinessError(apiResponse.ErrorCode, apiResponse.ErrorMessage);
                    throw new YunxiaoApiException(apiResponse.ErrorCode, errorMsg);
                }

                return apiResponse.Result;
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

        return default;
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