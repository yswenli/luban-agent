/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： AgentServerClient
*描述：WebHost 的 HTTP/SSE 客户端（统一响应壳解析 + 帧逐行读取）
*
*****************************************************************************/

using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;

namespace LubanAgentCore.Client;

/// <summary>
/// WebHost 的传输层客户端。
/// </summary>
/// <remarks>
/// <para>
/// 所有请求体都用 <see cref="AgentApiJson.WrapPayload{T}"/> 压成单字段 <c>payload</c>：
/// 宿主只按 <c>Dictionary&lt;string,string&gt;</c> 反解 JSON 体，嵌套对象会在进入动作前被判 400。
/// </para>
/// <para>
/// 失败统一抛 <see cref="AgentServerException"/>（含状态码）；SSE 走
/// <see cref="StreamAsync"/>，读到 <c>event:</c>/<c>data:</c> 块即产出一帧，心跳注释行自动丢弃。
/// </para>
/// </remarks>
public sealed class AgentServerClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string? _token;
    private int _disposed;

    /// <summary>创建客户端。</summary>
    /// <param name="baseAddress">服务地址。</param>
    /// <param name="token">访问令牌；宿主总是要求令牌，直连时必须提供（自动接管时由发现文件给出）。</param>
    /// <param name="httpClient">外部提供的 HttpClient（为 null 时自建并负责释放）。</param>
    public AgentServerClient(Uri baseAddress, string? token = null, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        BaseAddress = baseAddress;
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            // 流式回合可能持续很久，超时一律由调用方的 CancellationToken 控制
            Timeout = Timeout.InfiniteTimeSpan
        };

        var root = baseAddress.ToString().TrimEnd('/');
        _http.BaseAddress = new Uri(root + "/");
    }

    /// <summary>服务地址。</summary>
    public Uri BaseAddress { get; }

    // ── 健康检查 ──────────────────────────────────────────────────────

    /// <summary>探测服务是否就绪（免令牌）。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>宿主信息。</returns>
    public async Task<HealthDto> GetHealthAsync(CancellationToken ct = default)
    {
        var envelope = await ExecuteAsync(HttpMethod.Get, AgentApiRoutes.System.Health, null, ct)
            .ConfigureAwait(false);
        return envelope.GetData<HealthDto>()
            ?? throw new AgentServerException(500, "健康检查响应缺少载荷");
    }

    // ── 查询 ──────────────────────────────────────────────────────────

    /// <summary>GET 并取载荷；载荷缺失视为失败。</summary>
    /// <typeparam name="T">载荷类型。</typeparam>
    /// <param name="route">路由。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>载荷。</returns>
    public Task<T> GetAsync<T>(string route, CancellationToken ct = default)
        => GetAsync<T>(route, null, ct);

    /// <summary>GET 并取载荷；载荷缺失视为失败。</summary>
    /// <typeparam name="T">载荷类型。</typeparam>
    /// <param name="route">路由。</param>
    /// <param name="query">查询串参数（null 值跳过）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>载荷。</returns>
    public async Task<T> GetAsync<T>(string route, IReadOnlyDictionary<string, string?>? query, CancellationToken ct = default)
    {
        var envelope = await ExecuteAsync(HttpMethod.Get, BuildQuery(route, query), null, ct)
            .ConfigureAwait(false);
        return envelope.GetData<T>() ?? throw new AgentServerException(500, $"响应缺少载荷：{route}");
    }

    /// <summary>GET 并取载荷；载荷缺失返回 default。</summary>
    /// <typeparam name="T">载荷类型。</typeparam>
    /// <param name="route">路由。</param>
    /// <param name="query">查询串参数（null 值跳过）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>载荷；无载荷时为 default。</returns>
    public async Task<T?> GetOrNullAsync<T>(string route, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default)
    {
        var envelope = await ExecuteAsync(HttpMethod.Get, BuildQuery(route, query), null, ct)
            .ConfigureAwait(false);
        return envelope.GetData<T>();
    }

    // ── 提交 ──────────────────────────────────────────────────────────

    /// <summary>POST 入参并忽略载荷。</summary>
    /// <param name="route">路由。</param>
    /// <param name="payload">入参对象（null 表示空对象）。</param>
    /// <param name="ct">取消令牌。</param>
    public Task PostAsync(string route, object? payload = null, CancellationToken ct = default)
        => ExecuteAsync(HttpMethod.Post, route, payload, ct);

    /// <summary>POST 入参并取载荷；载荷缺失视为失败。</summary>
    /// <typeparam name="T">载荷类型。</typeparam>
    /// <param name="route">路由。</param>
    /// <param name="payload">入参对象（null 表示空对象）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>载荷。</returns>
    public async Task<T> PostAsync<T>(string route, object? payload = null, CancellationToken ct = default)
    {
        var envelope = await ExecuteAsync(HttpMethod.Post, route, payload, ct).ConfigureAwait(false);
        return envelope.GetData<T>() ?? throw new AgentServerException(500, $"响应缺少载荷：{route}");
    }

    // ── 流式 ──────────────────────────────────────────────────────────

    /// <summary>
    /// POST 并逐帧读取 SSE。
    /// </summary>
    /// <remarks>
    /// 取消 <paramref name="ct"/> 或提前结束枚举都会关闭连接；服务端感知不到断连，
    /// 需要取消回合时必须显式调用 <c>/agent/cancel</c>（由 <see cref="HttpAgentHost"/> 负责）。
    /// </remarks>
    /// <param name="route">路由。</param>
    /// <param name="payload">入参对象（null 表示空对象）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>帧序列。</returns>
    public async IAsyncEnumerable<AgentSseFrame> StreamAsync(
        string route,
        object? payload = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, route, payload);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!response.IsSuccessStatusCode
            || !string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var body = await ReadBodyAsync(response, ct).ConfigureAwait(false);
            throw CreateFailure(response.StatusCode, body, TryParseEnvelope(body));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var block = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                if (AgentSseFrame.TryParse(block.ToString(), out var tail))
                {
                    yield return tail;
                }

                yield break;
            }

            if (line.Length == 0)
            {
                if (AgentSseFrame.TryParse(block.ToString(), out var frame))
                {
                    yield return frame;
                }

                block.Clear();
                continue;
            }

            block.Append(line).Append('\n');
        }
    }

    // ── 内部实现 ──────────────────────────────────────────────────────

    private async Task<ApiEnvelope> ExecuteAsync(
        HttpMethod method, string route, object? payload, CancellationToken ct)
    {
        using var request = CreateRequest(method, route, payload);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        var body = await ReadBodyAsync(response, ct).ConfigureAwait(false);

        var envelope = TryParseEnvelope(body);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            throw CreateFailure(response.StatusCode, body, envelope);
        }

        return envelope;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string route, object? payload)
    {
        var request = new HttpRequestMessage(method, route.TrimStart('/'));
        if (_token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        if (method == HttpMethod.Post)
        {
            var json = payload is null ? "{}" : AgentApiJson.WrapPayload(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static string BuildQuery(string route, IReadOnlyDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0)
        {
            return route;
        }

        var builder = new StringBuilder(route);
        var first = true;
        foreach (var pair in query)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            builder.Append(first ? '?' : '&');
            builder.Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value));
            first = false;
        }

        return first ? route : builder.ToString();
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "";
        }
    }

    private static ApiEnvelope? TryParseEnvelope(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return AgentApiJson.Deserialize<ApiEnvelope>(body);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static AgentServerException CreateFailure(
        HttpStatusCode status, string body, ApiEnvelope? envelope = null)
    {
        var code = (int)status;
        var message = envelope?.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = string.IsNullOrWhiteSpace(body)
                ? $"服务端返回 {code}（{status}）且无正文"
                : body.Trim();
        }

        return new AgentServerException(code, message);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}