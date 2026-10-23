/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
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

namespace LubanAgentCore.Connectors.AliyunDevOps;

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

    /// <summary>
    /// 与 <see cref="CodeupClient"/> 同口径的 JSON 选项（由 <see cref="YunxiaoResponseParser"/> 承载，
    /// 保证「请求体序列化」与「响应体解析」用的是一份配置）。
    /// </summary>
    private static JsonSerializerOptions JsonOpts => YunxiaoResponseParser.JsonOpts;

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
    /// <remarks>
    /// <b>真机口径（2026-10-22 取证）</b>：成功响应是<b>裸数组</b>（无 <c>{ result: ... }</c> 外壳），
    /// 元素字段为 <c>pipelineId</c>（数字）/ <c>pipelineName</c> / <c>createAccountId</c> / <c>createTime</c>；
    /// 分页信息<b>不在</b>响应体里（<c>page</c> / <c>perPage</c> 只是请求参数）。故返回 <see cref="List{T}"/>，
    /// 不再用带 <c>result</c> 外壳的 <see cref="YunxiaoPagedResult{T}"/>（那个类型对 <c>/flow</c> 侧不成立）。
    /// </remarks>
    /// <param name="pipelineName">流水线名称过滤（可选，<b>服务端精确匹配</b>，幂等检索就靠它）</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="perPage">每页条数（注意：服务端会把过大的值截到 10）</param>
    /// <param name="statusList">状态过滤（可选，逗号分隔）</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>流水线列表；响应体为空时为 <c>null</c>。</returns>
    public async Task<List<YunxiaoPipeline>?> ListPipelinesAsync(
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

        var responseBody = await SendRawAsync(
            HttpMethod.Get,
            $"{path}?{string.Join("&", queryParams)}",
            null,
            cancellationToken);

        return YunxiaoResponseParser.DeserializeList<YunxiaoPipeline>(responseBody);
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
    /// <remarks>
    /// 真机口径（2026-10-22 取证）：请求体为 <c>{ "name": ..., "content": "&lt;新版 YAML&gt;" }</c>，
    /// <c>content</c> 必须是顶层 <c>stages:</c> 的新版 YAML；<b>成功响应是裸整数 id</b>（无外壳），
    /// 由 <see cref="YunxiaoResponseParser.ParseId"/> 归一化为字符串。
    /// </remarks>
    /// <param name="request">创建请求（名称 + 新版 YAML）。</param>
    /// <param name="organizationId">组织 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新流水线 id；无法解析时为 <c>null</c>。</returns>
    public async Task<string?> CreatePipelineAsync(
        YunxiaoCreatePipelineRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.PipelineName))
            throw new ArgumentException("流水线名称不能为空", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("流水线 YAML 内容不能为空", nameof(request));

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/flow/organizations/{orgId}/pipelines";

        var responseBody = await SendRawAsync(
            HttpMethod.Post,
            path,
            request,
            cancellationToken);

        return YunxiaoResponseParser.ParseId(responseBody);
    }

    /// <summary>
    /// 更新流水线（新版 YAML 全量覆盖）
    /// </summary>
    /// <remarks>
    /// 更新与创建同口径：<c>name</c> + <c>content</c>（新版 YAML）；<b>名称会被一并覆盖</b>，
    /// 故调用方须回传现有名称。用于「一键补齐部署阶段」（在已创建的构建阶段之后追加部署阶段）。
    /// </remarks>
    /// <param name="pipelineId">流水线 ID</param>
    /// <param name="request">更新请求（名称 + 新版 YAML）。</param>
    /// <param name="organizationId">组织 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功（不抛异常即视为成功）。</returns>
    public async Task<bool> UpdatePipelineAsync(
        string pipelineId,
        YunxiaoUpdatePipelineRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipelineId))
            throw new ArgumentException("流水线 ID 不能为空", nameof(pipelineId));

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.PipelineName))
            throw new ArgumentException("流水线名称不能为空", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("流水线 YAML 内容不能为空", nameof(request));

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/flow/organizations/{orgId}/pipelines/{pipelineId}";

        await SendRawAsync(
            HttpMethod.Put,
            path,
            request,
            cancellationToken);

        return true;
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

        await SendRawAsync(HttpMethod.Post, path, null, cancellationToken);

        return true;
    }

    #endregion

    #region 主机组（部署目标）API

    /// <summary>
    /// 列出主机组（流水线部署阶段的目标机器组）
    /// </summary>
    /// <remarks>
    /// <b>真机口径（2026-10-22 取证）</b>：成功响应是<b>裸数组</b>（与 <c>/pipelines</c> 同口径）；
    /// 元素字段 <c>id</c>（<b>JSON 数字</b>）、<c>uuid</c>（部署阶段 <c>machineGroup</c> 用值）、
    /// <c>name</c> / <c>hostNum</c> / <c>aliyunRegion</c> / <c>serviceConnectionId</c>；
    /// <b>列表里 <c>hostInfos</c> 恒为 <c>null</c></b>，机器明细要另调 <see cref="GetHostGroupAsync"/>。
    /// <c>perPage</c> / 名称过滤等查询参数<b>真机不生效</b>（一律返回全量 10 条），故本方法不暴露过滤参数。
    /// </remarks>
    /// <param name="organizationId">组织 ID（可选，优先于配置）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>主机组列表；响应体为空时返回 <c>null</c>。</returns>
    public async Task<List<YunxiaoHostGroup>?> ListHostGroupsAsync(
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/flow/organizations/{orgId}/hostGroups";

        var responseBody = await SendRawAsync(
            HttpMethod.Get,
            path,
            null,
            cancellationToken);

        return YunxiaoResponseParser.DeserializeList<YunxiaoHostGroup>(responseBody);
    }

    /// <summary>
    /// 获取主机组详情（含机器明细 <c>hostInfos</c>）
    /// </summary>
    /// <remarks>
    /// <b>真机口径（2026-10-22 取证）</b>：成功响应是<b>裸对象</b>；<b>路径键必须是数字 <c>id</c></b>——
    /// 真机用 <c>uuid</c> 当路径键会 400（<c>NumberFormatException: For input string: "…"</c>）。
    /// 也不存在 <c>/hostGroups/{id}/hosts</c> / <c>/machines</c> / <c>/hostList</c> 之类子资源（一律 404），
    /// 机器明细只能从本接口的 <c>hostInfos</c> 拿。
    /// </remarks>
    /// <param name="hostGroupId">主机组<b>数字 id</b>（来自 <see cref="ListHostGroupsAsync"/>）。</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>主机组详情（含 <c>hostInfos</c>）。</returns>
    public async Task<YunxiaoHostGroup?> GetHostGroupAsync(
        long hostGroupId,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (hostGroupId <= 0)
            throw new ArgumentOutOfRangeException(nameof(hostGroupId), hostGroupId, "主机组 ID 必须是正数（真机路径键为数字 id）");

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/flow/organizations/{orgId}/hostGroups/{hostGroupId}";

        return await SendRequestAsync<YunxiaoHostGroup>(
            HttpMethod.Get,
            path,
            cancellationToken: cancellationToken);
    }

    #endregion

    #region HTTP 请求

    /// <summary>
    /// 发起请求并返回<b>原始响应体</b>（含出口门禁、重试与错误映射）。
    /// </summary>
    /// <remarks>
    /// <b>与 <see cref="CodeupClient"/> 同口径</b>：云效成功响应<b>不套</b> <c>{ "result": ... }</c> 外壳，
    /// 故先把裸响应体交出去，由 <see cref="YunxiaoResponseParser"/> 按「裸数组 / 裸对象 / 裸标量」分别解析，
    /// 避免历史上「拆壳后拿到 <c>null</c>」的假失败。
    /// </remarks>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">相对路径（含查询串）。</param>
    /// <param name="body">请求体对象（<c>null</c> 表示无体）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>原始响应体文本。</returns>
    /// <exception cref="YunxiaoApiException">HTTP 非 2xx，或 2xx 但响应体携带业务错误码。</exception>
    private async Task<string> SendRawAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        // 出口门禁（PRD §3.7）：目标主机 + PAT 凭据
        _guard.RequireNetwork(_options.Endpoint, "FlowClient.SendRawAsync");
        _guard.RequireCredential(PatCredentialKey, "FlowClient.SendRawAsync");

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
                    // 云效错误体是 {"errorCode":"InvalidPipeline.createOrUpdateError","errorMessage":"1209300 yaml校验失败…","requestId":"…"}
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

                return responseBody;
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
    /// 请求并反序列化<b>单个对象</b>响应（裸对象）。
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
        var responseBody = await SendRawAsync(method, path, body, cancellationToken);

        return YunxiaoResponseParser.DeserializeObject<T>(responseBody, path);
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