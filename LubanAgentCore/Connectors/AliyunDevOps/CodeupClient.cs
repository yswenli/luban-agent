/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： CodeupClient
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：云效代码管理客户端
*
*****************************************************************************/

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效代码管理客户端 - 提供仓库管理（查询 / 创建 / 模板库 / 代码组 / 分支）等能力。
/// </summary>
/// <remarks>
/// <b>不再承担 Git 操作</b>：克隆 / 推送一律改由助手经 git 工具组完成（v0.3 设计 §5.5）。
/// </remarks>
public class CodeupClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly YunxiaoApiOptions _options;
    private readonly IYunxiaoCredentialProvider _credentialProvider;
    private readonly PluginPermissionGuard _guard;
    private readonly string? _connectorName;
    private bool _disposed;

    // 忽略 null 字段，避免把未设置的可见性/模板等参数以 null 发给云效
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 创建 CodeupClient 实例
    /// </summary>
    /// <param name="options">API 配置</param>
    /// <param name="credentialProvider">凭据提供者</param>
    /// <param name="permissionGuard">插件权限门禁（可选；不传则用进程级兜底实例）。</param>
    /// <param name="connectorName">所属连接器实例名（可选，Q15）；决定 PAT 凭据键，<c>null</c> 视为默认实例。</param>
    public CodeupClient(
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

    #region 仓库管理 API

    /// <summary>
    /// 搜索代码仓库
    /// </summary>
    /// <param name="search">搜索关键字</param>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="perPage">每页条数</param>
    /// <param name="orderBy">排序字段（可选）</param>
    /// <param name="sort">排序方向：asc / desc（可选）</param>
    /// <param name="archived">是否包含已归档仓库（可选）</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>仓库列表</returns>
    /// <remarks>
    /// 云效列表接口返回的是<b>裸数组</b>，总数在响应头 <c>x-total</c>（不在响应体内），
    /// 故此处把两者合成为 <see cref="YunxiaoPagedResult{T}"/> 以保持调用方签名稳定。
    /// </remarks>
    public async Task<YunxiaoPagedResult<YunxiaoRepository>?> SearchRepositoriesAsync(
        string? search = null,
        int page = 1,
        int perPage = 20,
        string? orderBy = null,
        string? sort = null,
        bool? archived = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/codeup/organizations/{orgId}/repositories";
        var queryParams = new List<string>
        {
            $"page={page}",
            $"perPage={perPage}"
        };
        
        if (!string.IsNullOrWhiteSpace(search))
            queryParams.Add($"search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrWhiteSpace(orderBy))
            queryParams.Add($"orderBy={Uri.EscapeDataString(orderBy!)}");
        if (!string.IsNullOrWhiteSpace(sort))
            queryParams.Add($"sort={Uri.EscapeDataString(sort!)}");
        if (archived.HasValue)
            queryParams.Add($"archived={(archived.Value ? "true" : "false")}");

        var response = await SendRawAsync(
            HttpMethod.Get,
            $"{path}?{string.Join("&", queryParams)}",
            body: null,
            cancellationToken);

        var items = YunxiaoResponseParser.DeserializeList<YunxiaoRepository>(response.Body);

        return new YunxiaoPagedResult<YunxiaoRepository>
        {
            Items = items,
            Total = ReadHeaderInt(response.Headers, "x-total", items?.Count ?? 0),
            Page = page,
            PerPage = perPage
        };
    }

    /// <summary>
    /// 获取仓库详情
    /// </summary>
    /// <param name="repoId">仓库 ID</param>
    /// <param name="organizationId">组织 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>仓库详情</returns>
    public async Task<YunxiaoRepository?> GetRepositoryAsync(
        string repoId,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoId))
            throw new ArgumentException("仓库 ID 不能为空", nameof(repoId));

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/codeup/organizations/{orgId}/repositories/{repoId}";
        
        var result = await SendRequestAsync<YunxiaoRepository>(
            HttpMethod.Get,
            path,
            cancellationToken: cancellationToken);

        return result;
    }

    /// <summary>
    /// 创建仓库
    /// </summary>
    /// <param name="request">创建请求</param>
    /// <param name="organizationId">组织 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的仓库</returns>
    public async Task<YunxiaoRepository?> CreateRepositoryAsync(
        YunxiaoCreateRepositoryRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/codeup/organizations/{orgId}/repositories";

        // createParentPath 是查询串参数（不是请求体字段）：path 含多级分组时由云效自动补建父路径
        if (request.CreateParentPath == true)
            path += "?createParentPath=true";

        var result = await SendRequestAsync<YunxiaoRepository>(
            HttpMethod.Post,
            path,
            request,
            cancellationToken);

        return result;
    }

    /// <summary>
    /// 更新仓库
    /// </summary>
    /// <param name="repoId">仓库 ID</param>
    /// <param name="request">更新请求</param>
    /// <param name="organizationId">组织 ID（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新后的仓库</returns>
    public async Task<YunxiaoRepository?> UpdateRepositoryAsync(
        string repoId,
        YunxiaoUpdateRepositoryRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoId))
            throw new ArgumentException("仓库 ID 不能为空", nameof(repoId));

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/codeup/organizations/{orgId}/repositories/{repoId}";
        
        var result = await SendRequestAsync<YunxiaoRepository>(
            HttpMethod.Put,
            path,
            request,
            cancellationToken);

        return result;
    }

    #endregion

    #region 分支 / 模板库 / 代码组 API

    /// <summary>
    /// 查询仓库分支列表（<c>GET .../repositories/{id}/branches</c>）。
    /// </summary>
    /// <param name="repositoryId">仓库 ID。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="perPage">每页条数。</param>
    /// <param name="sort">排序方向：asc / desc（可选）。</param>
    /// <param name="search">分支名关键字（可选）。</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分支列表（含 <c>defaultBranch</c> 标记，用于解析模板 pin）。</returns>
    public async Task<List<YunxiaoBranch>?> ListBranchesAsync(
        long repositoryId,
        int page = 1,
        int perPage = 100,
        string? sort = null,
        string? search = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/codeup/organizations/{orgId}/repositories/{repositoryId}/branches";
        var queryParams = new List<string> { $"page={page}", $"perPage={perPage}" };
        if (!string.IsNullOrWhiteSpace(sort))
            queryParams.Add($"sort={Uri.EscapeDataString(sort!)}");
        if (!string.IsNullOrWhiteSpace(search))
            queryParams.Add($"search={Uri.EscapeDataString(search!)}");

        var response = await SendRawAsync(
            HttpMethod.Get,
            $"{path}?{string.Join("&", queryParams)}",
            body: null,
            cancellationToken);

        return YunxiaoResponseParser.DeserializeList<YunxiaoBranch>(response.Body);
    }

    /// <summary>
    /// 查询模板代码库列表（<c>GET .../repositories/templates</c>）。
    /// </summary>
    /// <param name="templateType">模板类型：1 自定义模板（组织登记）/ 2 系统预置模板。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="perPage">每页条数。</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模板库列表。</returns>
    /// <remarks>
    /// 云效的「模板库」是独立概念：只有登记在模板库里的仓库才能作为
    /// <see cref="YunxiaoTemplateProject.TemplateProjectId"/> 使用。定位模板仓应优先走本方法，
    /// 而不是按名搜索普通仓库（后者可能命中未登记为模板的仓，导致建库时才失败）。
    /// </remarks>
    public async Task<List<YunxiaoTemplateRepository>?> ListTemplateRepositoriesAsync(
        int templateType = 1,
        int page = 1,
        int perPage = 100,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/codeup/organizations/{orgId}/repositories/templates"
                   + $"?page={page}&perPage={perPage}&templateType={templateType}";

        var response = await SendRawAsync(HttpMethod.Get, path, body: null, cancellationToken);

        return YunxiaoResponseParser.DeserializeList<YunxiaoTemplateRepository>(response.Body);
    }

    /// <summary>
    /// 查询代码组 / 命名空间列表（<c>GET .../namespaces</c>）。
    /// </summary>
    /// <param name="search">代码组名关键字（可选）。</param>
    /// <param name="parentId">父级 ID 过滤（可选）。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="perPage">每页条数。</param>
    /// <param name="orderBy">排序字段（可选）。</param>
    /// <param name="sort">排序方向：asc / desc（可选）。</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>代码组列表。</returns>
    public async Task<List<YunxiaoNamespace>?> ListNamespacesAsync(
        string? search = null,
        long? parentId = null,
        int page = 1,
        int perPage = 100,
        string? orderBy = null,
        string? sort = null,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/codeup/organizations/{orgId}/namespaces";
        var queryParams = new List<string> { $"page={page}", $"perPage={perPage}" };
        if (!string.IsNullOrWhiteSpace(search))
            queryParams.Add($"search={Uri.EscapeDataString(search!)}");
        if (parentId.HasValue)
            queryParams.Add($"parentId={parentId.Value}");
        if (!string.IsNullOrWhiteSpace(orderBy))
            queryParams.Add($"orderBy={Uri.EscapeDataString(orderBy!)}");
        if (!string.IsNullOrWhiteSpace(sort))
            queryParams.Add($"sort={Uri.EscapeDataString(sort!)}");

        var response = await SendRawAsync(
            HttpMethod.Get,
            $"{path}?{string.Join("&", queryParams)}",
            body: null,
            cancellationToken);

        return YunxiaoResponseParser.DeserializeList<YunxiaoNamespace>(response.Body);
    }

    /// <summary>
    /// 创建代码组（<c>POST .../groups</c>）。
    /// </summary>
    /// <param name="request">创建请求（<c>name</c> / <c>path</c> 必填）。</param>
    /// <param name="organizationId">组织 ID（可选，优先于配置）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建的代码组。</returns>
    public async Task<YunxiaoNamespace?> CreateNamespaceAsync(
        YunxiaoCreateNamespaceRequest request,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var orgId = organizationId ?? _options.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
            throw new InvalidOperationException("未配置组织 ID");

        var path = $"/oapi/v1/codeup/organizations/{orgId}/groups";

        return await SendRequestAsync<YunxiaoNamespace>(
            HttpMethod.Post,
            path,
            request,
            cancellationToken);
    }

    #endregion

    #region HTTP 请求

    /// <summary>
    /// 原始响应：响应体 + 响应头（云效列表接口把分页信息放在 <c>x-total</c> 等响应头里）。
    /// </summary>
    /// <param name="Body">响应体文本。</param>
    /// <param name="Headers">响应头。</param>
    private sealed record RawResponse(string Body, HttpResponseHeaders Headers);

    /// <summary>
    /// 发起请求并返回原始响应体（含出口门禁、重试与错误映射）。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">相对路径（含查询串）。</param>
    /// <param name="body">请求体对象（<c>null</c> 表示无体）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>原始响应。</returns>
    /// <exception cref="YunxiaoApiException">HTTP 非 2xx，或 2xx 但响应体携带业务错误码。</exception>
    private async Task<RawResponse> SendRawAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        // 出口门禁（PRD §3.7）：目标主机 + PAT 凭据
        _guard.RequireNetwork(_options.Endpoint, "CodeupClient.SendRawAsync");
        _guard.RequireCredential(PatCredentialKey, "CodeupClient.SendRawAsync");

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

                return new RawResponse(responseBody, response.Headers);
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
    /// 请求并反序列化<b>单个对象</b>响应。
    /// </summary>
    /// <remarks>
    /// 云效成功响应是<b>裸对象</b>（直接把资源字段放在根上），<b>不套</b> <c>{ "result": ... }</c> 外壳
    /// ——这一点已对 <c>GET /repositories/{id}</c> 实测确认。
    /// </remarks>
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

    /// <summary>
    /// 从响应头读取整型值（云效列表接口的总数在 <c>x-total</c>）。
    /// </summary>
    /// <param name="headers">响应头。</param>
    /// <param name="name">头名称。</param>
    /// <param name="fallback">读不到时的兜底值。</param>
    /// <returns>整型值。</returns>
    private static int ReadHeaderInt(HttpResponseHeaders headers, string name, int fallback)
        => headers.TryGetValues(name, out var values)
           && int.TryParse(values.FirstOrDefault(), out var parsed)
            ? parsed
            : fallback;

    #endregion

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}

/// <summary>
/// 云效 API 异常
/// </summary>
public class YunxiaoApiException : Exception
{
    /// <summary>
    /// 错误码
    /// </summary>
    public int ErrorCode { get; }

    /// <summary>
    /// 创建异常
    /// </summary>
    public YunxiaoApiException(int errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}