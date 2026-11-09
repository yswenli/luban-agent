/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： WebApi
*描述：控制器公共守卫与响应壳（静态，避免在 Controller 与动作类之间插入基类）
*
*****************************************************************************/

using System.Net;

using LubanAgentCore.Protocol;

using SAEA.Http;
using SAEA.Http.Base;
using SAEA.MVC;

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// 控制器公共能力：来源校验、可选令牌校验、用户初始化与 JSON 响应壳。
/// </summary>
/// <remarks>
/// <para>
/// SAEA 用 <c>BaseType.FullName == "SAEA.MVC.Controller"</c> 收集控制器，
/// 控制器与 <see cref="Controller"/> 之间不能插入自定义基类，故公共逻辑只能做成静态方法。
/// </para>
/// <para>
/// SAEA 未设置 <c>m_OnException</c>，动作抛异常会因空引用直接掐断连接，故每个动作都必须经
/// <see cref="RunAsync"/> 包一层兜底。
/// </para>
/// </remarks>
public static class WebApi
{
    /// <summary>当前请求。</summary>
    public static HttpRequest Request => HttpContextBase.Current!.Request;

    /// <summary>当前响应。</summary>
    public static HttpResponse Response => HttpContextBase.Current!.Response;

    /// <summary>当前进程运行时。</summary>
    public static AgentWebRuntime Runtime => AgentWebRuntime.Current;

    /// <summary>
    /// 执行来源/令牌校验与用户初始化。
    /// </summary>
    /// <param name="requireToken">是否校验共享令牌（健康检查免校验，便于启动器探测）。</param>
    /// <param name="ensureUser">是否等待用户工作区/插件初始化（健康检查不需要，避免探测被初始化耗时拖住）。</param>
    /// <returns>拒绝响应；校验通过返回 null。</returns>
    public static async Task<ActionResult?> GuardAsync(bool requireToken = true, bool ensureUser = true)
    {
        if (!IsLoopback(Header("REMOTE_ADDR")))
        {
            return FailJson(403, "仅允许本机访问");
        }

        if (requireToken && RequiresToken(out var expected) && !HasValidToken(expected!))
        {
            return FailJson(401, "未授权：缺少或错误的访问令牌");
        }

        if (!ensureUser)
        {
            return null;
        }

        try
        {
            await Runtime.EnsureUserReadyAsync(Runtime.UserId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 原始消息可能含本机路径与库内细节：只进日志，不回传
            AgentWebRuntime.Log($"用户初始化失败：{ex}");
            return FailJson(503, "宿主初始化失败，请查看服务端日志");
        }

        return null;
    }

    /// <summary>
    /// 执行动作并兜底异常（SAEA 自身不处理动作异常）。
    /// </summary>
    /// <param name="action">动作体。</param>
    /// <returns>动作结果；异常时返回 500 壳。</returns>
    public static async Task<ActionResult> RunAsync(Func<Task<ActionResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return FailJson(499, "请求已取消");
        }
        catch (Exception ex)
        {
            AgentWebRuntime.Log($"动作执行失败：{ex}");
            return FailJson(500, SafeErrorMessage(ex));
        }
    }

    /// <summary>
    /// 把异常转成可安全回传客户端的文案。
    /// </summary>
    /// <remarks>
    /// 原始 <see cref="Exception.Message"/> 可能带本机绝对路径、SQL 片段或库内细节，
    /// 只写日志；回传端按异常类型给可操作提示。
    /// </remarks>
    /// <param name="ex">异常。</param>
    /// <returns>安全文案。</returns>
    public static string SafeErrorMessage(Exception ex) => ex switch
    {
        OperationCanceledException => "请求已取消",
        TimeoutException => "操作超时",
        FileNotFoundException => "所需文件不存在",
        DirectoryNotFoundException => "所需目录不存在",
        UnauthorizedAccessException => "无权访问所需资源",
        HttpRequestException => "外部服务调用失败",
        IOException => "本地文件读写失败",
        _ => "服务端处理失败，请查看服务端日志"
    };

    /// <summary>读入参包装里的 <c>payload</c> 并反序列化。</summary>
    /// <typeparam name="T">入参类型。</typeparam>
    /// <param name="payload">请求体中的 <c>payload</c> 字段。</param>
    /// <param name="request">反序列化结果。</param>
    /// <returns>是否解析成功。</returns>
    public static bool TryReadPayload<T>(string? payload, out T? request)
        where T : class
    {
        request = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            request = AgentApiJson.Deserialize<T>(payload);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }

        return request is not null;
    }

    /// <summary>成功响应。</summary>
    /// <param name="data">载荷。</param>
    /// <returns>JSON 结果。</returns>
    public static ActionResult OkJson(object? data = null)
        => new JsonResult(AgentApiJson.Serialize(ApiEnvelope.Ok(data)), HttpStatusCode.OK);

    /// <summary>失败响应。</summary>
    /// <param name="code">HTTP 状态码，同时写入响应壳。</param>
    /// <param name="message">失败原因。</param>
    /// <returns>JSON 结果。</returns>
    public static ActionResult FailJson(int code, string message)
        => new JsonResult(AgentApiJson.Serialize(ApiEnvelope.Fail(code, message)), (HttpStatusCode)code);

    /// <summary>取查询串参数；缺失时返回 null。</summary>
    /// <param name="name">参数名。</param>
    /// <returns>参数值。</returns>
    public static string? Query(string name)
        => Request.Query.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>取查询串整数参数；缺失或非法返回 null。</summary>
    /// <param name="name">参数名。</param>
    /// <returns>参数值。</returns>
    public static int? QueryInt(string name)
        => int.TryParse(Query(name), out var value) ? value : null;

    /// <summary>取请求头；缺失返回 null。</summary>
    /// <param name="name">头名（大小写不敏感）。</param>
    /// <returns>头值。</returns>
    public static string? Header(string name)
        => Request.Headers.TryGetValue(name, out var value) ? value : null;

    private static bool RequiresToken(out string? expected)
    {
        expected = Runtime.Settings.Token;
        return !string.IsNullOrEmpty(expected);
    }

    private static bool HasValidToken(string expected)
    {
        var header = Header("Authorization");
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        const string prefix = "Bearer ";
        var token = header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : header.Trim();

        return string.Equals(token, expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断来源是否为本机。
    /// </summary>
    /// <remarks>
    /// <c>Request.UserHostAddress</c> 取自 <c>HttpMessage.ID</c>，不是真实来源；
    /// 真实来源由 socket 层写入 <c>REMOTE_ADDR</c> 头，客户端无法伪造。
    /// </remarks>
    private static bool IsLoopback(string? remoteAddress)
        => IPAddress.TryParse(remoteAddress, out var address) && IPAddress.IsLoopback(address);
}