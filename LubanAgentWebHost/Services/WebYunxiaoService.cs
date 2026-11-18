/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebYunxiaoService
*描述：云效工作台 REST 薄壳：守卫 + payload 解析 + ApiEnvelope 映射（业务全在 YunxiaoWorkbenchService）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 云效工作台 REST 薄壳（照抄 <see cref="WebPluginService"/> 惯例）。
/// </summary>
/// <remarks>
/// <b>壳层不写业务逻辑</b>：每方法 <see cref="WebApi.RunGuardedAsync"/> 兜底，
/// POST 走 <see cref="WebApi.TryReadPayload{T}"/>、GET 用 <see cref="WebApi.Query"/>/<see cref="WebApi.QueryInt"/>，
/// 经 <c>WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId)</c> 解析
/// <see cref="YunxiaoWorkbenchService"/> 后转调；
/// <see cref="YunxiaoWorkbenchResult{T}"/> → ApiEnvelope 的映射口径见该类型注释
/// （Ok → <c>WebApi.OkJson(Data)</c>，失败 → <c>WebApi.FailJson(Code, Message)</c>）。
/// </remarks>
public static class WebYunxiaoService
{
    /// <summary>读取连接设置（GET /yunxiao/settings）。</summary>
    /// <returns><c>{ organizationId, hasPat }</c>（绝不含 PAT 明文）。</returns>
    public static Task<ActionResult> GetSettingsAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoWorkbenchService>();

        var result = await service.GetSettingsAsync().ConfigureAwait(false);
        return ToEnvelope(result);
    });

    /// <summary>测试连接（POST /yunxiao/testconnection，只读探活）。</summary>
    /// <param name="payload">包装后的 <see cref="YunxiaoTestConnectionRequest"/> JSON。</param>
    /// <returns><c>{ ok, projectCount, message? }</c>。</returns>
    public static Task<ActionResult> TestConnectionAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<YunxiaoTestConnectionRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoWorkbenchService>();

        var result = await service
            .TestConnectionAsync(input.OrganizationId, input.Pat)
            .ConfigureAwait(false);
        return ToEnvelope(result);
    });

    /// <summary>保存连接设置（POST /yunxiao/savesettings，服务端先探活后落盘）。</summary>
    /// <param name="payload">包装后的 <see cref="YunxiaoSaveSettingsRequest"/> JSON。</param>
    /// <returns><c>{ saved: true }</c>。</returns>
    public static Task<ActionResult> SaveSettingsAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<YunxiaoSaveSettingsRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoWorkbenchService>();

        var result = await service
            .SaveSettingsAsync(input.OrganizationAddress, input.OrganizationId, input.Pat)
            .ConfigureAwait(false);
        return ToEnvelope(result);
    });

    /// <summary>项目列表（GET /yunxiao/projects）。</summary>
    /// <returns><c>{ items, total, page, perPage }</c>。</returns>
    public static Task<ActionResult> ListProjectsAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoWorkbenchService>();

        var result = await service
            .ListProjectsAsync(WebApi.QueryInt("page"), WebApi.QueryInt("perPage"), WebApi.Query("keyword"))
            .ConfigureAwait(false);
        return ToEnvelope(result);
    });

    /// <summary>
    /// 业务结果 → 响应壳（机械映射，见 <see cref="YunxiaoWorkbenchResult{T}"/> 注释）。
    /// </summary>
    private static ActionResult ToEnvelope<T>(YunxiaoWorkbenchResult<T> result)
        => result.Ok
            ? WebApi.OkJson(result.Data)
            : WebApi.FailJson(result.Code, result.Message ?? "服务端处理失败");
}
