/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： RuleController
*描述：规则设置接口（浏览器端，文件型条目）
*
*****************************************************************************/

using LuBan.AIAgent.Configuration;

using LubanAgentCore.Services.Management;
using LubanAgentCore.Services.Management.Models;

using LubanAgentWebHost.Hosting;

using SAEA.MVC;

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 规则设置接口。
/// </summary>
/// <remarks>
/// 与桌面端设置页一致，只暴露文件型条目（<c>rules/&lt;id&gt;.json</c>）：不列内置与 config 自定义规则，也不提供启停。
/// </remarks>
public sealed class RuleController : Controller
{
    /// <summary>列出作用域内的规则 Id。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识；缺省按全局。</param>
    /// <returns>规则 Id 数组。</returns>
    [HttpGet]
    public Task<ActionResult> List(string? scope) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var ids = requestScope.GetRequiredService<IRuleManagementService>().ListFileIds(root);
        return WebApi.OkJson(new { items = ids });
    });

    /// <summary>读取规则内容。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识。</param>
    /// <param name="key">规则 Id（文件名去扩展名）。</param>
    /// <returns>规则内容；不存在时 404。</returns>
    [HttpGet]
    public Task<ActionResult> Read(string? scope, string? key) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return WebApi.FailJson(400, "key 不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var rule = requestScope.GetRequiredService<IRuleManagementService>().ReadFile(key.Trim(), root);
        return rule is null
            ? WebApi.FailJson(404, "规则不存在")
            : WebApi.OkJson(ToDto(rule));
    });

    /// <summary>保存规则。</summary>
    /// <param name="payload">包装后的 <see cref="RuleSaveRequest"/> JSON。</param>
    /// <returns>保存后的条目键。</returns>
    [HttpPost]
    public Task<ActionResult> Save(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<RuleSaveRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.Id))
        {
            return WebApi.FailJson(400, "规则 Id 不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), input.Scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var id = input.Id.Trim();
        requestScope.GetRequiredService<IRuleManagementService>().SaveFile(
            input.OriginalId,
            new CustomRuleConfig
            {
                Id = id,
                Name = input.Name,
                Description = input.Description,
                ActionTypePattern = string.IsNullOrWhiteSpace(input.ActionTypePattern) ? "*" : input.ActionTypePattern.Trim(),
                TargetPattern = string.IsNullOrWhiteSpace(input.TargetPattern) ? "*" : input.TargetPattern.Trim(),
                Action = string.IsNullOrWhiteSpace(input.Action) ? "allow" : input.Action.Trim(),
                Priority = input.Priority,
                Enabled = input.Enabled,
                Content = input.Content,
            },
            root);
        return WebApi.OkJson(new SettingsKeyResult(id));
    });

    /// <summary>删除规则。</summary>
    /// <param name="payload">包装后的 <see cref="SettingsKeyRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    [HttpPost]
    public Task<ActionResult> Delete(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<SettingsKeyRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.Key))
        {
            return WebApi.FailJson(400, "key 不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), input.Scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        await requestScope.GetRequiredService<IRuleManagementService>()
            .DeleteAsync(input.Key.Trim(), ManagedOrigin.File, root)
            .ConfigureAwait(false);
        return WebApi.OkJson();
    });

    private static RuleFileDto ToDto(CustomRuleConfig rule) => new(
        rule.Id,
        rule.Name ?? "",
        rule.Description ?? "",
        rule.ActionTypePattern ?? "*",
        rule.TargetPattern ?? "*",
        rule.Action ?? "allow",
        rule.Priority,
        rule.Enabled,
        rule.Content ?? "");
}