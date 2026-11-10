/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： SkillController
*描述：技能设置接口（浏览器端，文件型条目）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 技能设置接口。
/// </summary>
/// <remarks>
/// 与桌面端设置页一致，只暴露文件型条目（<c>SKILL.md</c>）：不列内置与 config 自定义技能，也不提供启停。
/// </remarks>
public sealed class SkillController : Controller
{
    /// <summary>列出作用域内的技能名。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识；缺省按全局。</param>
    /// <returns>技能名数组。</returns>
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

        var ids = requestScope.GetRequiredService<ISkillManagementService>().ListFileIds(root);
        return WebApi.OkJson(new { items = ids });
    });

    /// <summary>读取技能内容。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识。</param>
    /// <param name="key">技能名（目录名）。</param>
    /// <returns>技能内容；不存在时 404。</returns>
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

        var content = requestScope.GetRequiredService<ISkillManagementService>().ReadFile(key.Trim(), root);
        return content is null
            ? WebApi.FailJson(404, "技能不存在")
            : WebApi.OkJson(new SkillFileDto(content.Name, content.Description, content.Category, content.Triggers, content.Body));
    });

    /// <summary>保存技能。</summary>
    /// <param name="payload">包装后的 <see cref="SkillSaveRequest"/> JSON。</param>
    /// <returns>保存后的条目键。</returns>
    [HttpPost]
    public Task<ActionResult> Save(string payload) => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        if (!WebApi.TryReadPayload<SkillSaveRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return WebApi.FailJson(400, "技能名不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), input.Scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var name = input.Name.Trim();
        requestScope.GetRequiredService<ISkillManagementService>().SaveFile(
            input.OriginalId,
            new SkillFileContent(name, input.Description, input.Category, input.Triggers, input.Body),
            root);
        return WebApi.OkJson(new SettingsKeyResult(name));
    });

    /// <summary>删除技能。</summary>
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

        await requestScope.GetRequiredService<ISkillManagementService>()
            .DeleteAsync(input.Key.Trim(), ManagedOrigin.File, root)
            .ConfigureAwait(false);
        return WebApi.OkJson();
    });
}