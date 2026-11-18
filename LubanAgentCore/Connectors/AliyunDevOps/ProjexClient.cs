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
    /// <param name="perPage">每页条数（上限 100，越界自动收敛）。</param>
    /// <param name="keyword">检索关键字（可选，非空时进请求体）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>项目分页结果；空体时为 <c>null</c>。</returns>
    /// <remarks>
    /// 与 codeup / flow 侧不同，Projex 搜索响应<b>自带分页外壳</b>
    /// （<c>{"result":[...],"total":N,"page":N,"perPage":N}</c>），
    /// 故直接按 <see cref="YunxiaoPagedResult{T}"/> 反序列化，无需响应头合成。
    /// </remarks>
    public async Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsAsync(
        string organizationId,
        int page,
        int perPage,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        var (url, body) = BuildSearchRequest(organizationId, page, perPage, keyword);

        var response = await SendRawAsync(
            HttpMethod.Post,
            url,
            body,
            cancellationToken);

        return ParseSearchResponse(response);
    }

    /// <summary>
    /// 以显式 PAT 搜索研发项目（只读探活用：不读凭据存储、不写任何配置）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="page">页码（从 1 开始，下限 1）。</param>
    /// <param name="perPage">每页条数（上限 100，越界自动收敛）。</param>
    /// <param name="keyword">检索关键字（可选，非空时进请求体）。</param>
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

        return ParseSearchResponse(response);
    }

    /// <summary>
    /// 获取研发项目详情（GetProjectInfo 形态，<b>URL 待实测校正</b>）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>项目详情；空体时为 <c>null</c>。</returns>
    /// <remarks>
    /// S1 预留占位：<c>GET /oapi/v1/projex/organizations/{organizationId}/projects/{projectId}</c>
    /// 先按 GetProjectInfo 常见形态写，URL 待 T11 实测校正。
    /// </remarks>
    public async Task<YunxiaoProject?> GetProjectAsync(
        string organizationId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
            throw new ArgumentException("组织 ID 不能为空", nameof(organizationId));
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("项目 ID 不能为空", nameof(projectId));

        // URL 待实测校正（先按 GetProjectInfo 常见形态写）
        var path = $"/oapi/v1/projex/organizations/{organizationId}/projects/{projectId}";

        return await SendRequestAsync<YunxiaoProject>(
            HttpMethod.Get,
            path,
            cancellationToken: cancellationToken);
    }

    #endregion

    #region 请求装配 / 响应解析（纯函数，供单测锁口径）

    /// <summary>
    /// 装配「搜索项目」请求（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="page">页码（下限 1）。</param>
    /// <param name="perPage">每页条数（区间 [1, 100]，越界自动收敛）。</param>
    /// <param name="keyword">检索关键字（非空时进请求体）。</param>
    /// <returns>请求路径与请求体。</returns>
    /// <remarks>
    /// <b>URL / 字段名待实测校正</b>：先按 Projex OpenAPI 常见形态写——
    /// <c>POST /oapi/v1/projex/organizations/{organizationId}/projects:search</c>；
    /// 检索字段名先按 <c>keyword</c> 写，T11 实测后校正。
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
        var safePerPage = Math.Clamp(perPage, 1, 100);

        // keyword 非空才进 body（检索字段名先按 keyword 写，待实测校正）
        object body = string.IsNullOrWhiteSpace(keyword)
            ? new { page = safePage, perPage = safePerPage }
            : new { page = safePage, perPage = safePerPage, keyword };

        var url = $"/oapi/v1/projex/organizations/{organizationId}/projects:search";
        return (url, body);
    }

    /// <summary>
    /// 解析「搜索项目」响应（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="json">响应体。</param>
    /// <returns>分页结果；空体时为 <c>null</c>。</returns>
    /// <remarks>
    /// Projex 搜索响应自带 <c>result/total/page/perPage</c> 外壳，与
    /// <see cref="YunxiaoPagedResult{T}"/> 的 JSON 字段一一对应，故按该模型直接反序列化
    /// （<see cref="YunxiaoResponseParser.JsonOpts"/> 同口径）；业务错误由
    /// <see cref="YunxiaoResponseParser.ParseBusinessError"/> 在发送链路里同口径拦截。
    /// </remarks>
    internal static YunxiaoPagedResult<YunxiaoProject>? ParseSearchResponse(string? json)
        => YunxiaoResponseParser.DeserializeObject<YunxiaoPagedResult<YunxiaoProject>>(
            json, "/oapi/v1/projex/projects:search");

    #endregion

    #region HTTP 请求

    /// <summary>
    /// 发起请求并返回原始响应体（含出口门禁、重试与错误映射）。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">相对路径（含查询串）。</param>
    /// <param name="body">请求体对象（<c>null</c> 表示无体）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="patOverride">显式 PAT（探活用）；<c>null</c> 时按连接器名从凭据提供者取已存 PAT。</param>
    /// <returns>响应体文本。</returns>
    /// <exception cref="YunxiaoApiException">HTTP 非 2xx，或 2xx 但响应体携带业务错误码。</exception>
    /// <remarks>
    /// 与 <see cref="CodeupClient"/> 同口径复制；差异仅在：Projex 分页信息在响应体里，
    /// 无需响应头通道，故直接返回响应体文本。
    /// </remarks>
    private async Task<string> SendRawAsync(
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

        if (string.IsNullOrWhiteSpace(response))
            return default;

        return YunxiaoResponseParser.DeserializeObject<T>(response, path);
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
