/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebSkillService
*描述：技能设置业务（浏览器端，文件型条目）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 技能设置业务。
/// </summary>
/// <remarks>
/// 与桌面端设置页一致，只暴露文件型条目（<c>SKILL.md</c>）：不列内置与 config 自定义技能，也不提供启停。
/// </remarks>
public static class WebSkillService
{
    /// <summary>列出作用域内的技能（目录名 + 显示名 + 分类）。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识；缺省按全局。</param>
    /// <returns>技能摘要数组。</returns>
    public static Task<ActionResult> ListAsync(string? scope) => WebApi.RunGuardedAsync(async () =>
    {
        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var entries = requestScope.GetRequiredService<ISkillManagementService>().ListFileEntries(root);
        var items = entries
            .Select(e => new SkillFileSummaryDto(e.Id, e.Name, e.Category))
            .ToList();
        return WebApi.OkJson(new { items });
    });

    /// <summary>列出输入 <c>@</c> 时可选的技能（当前会话可用技能）。</summary>
    /// <param name="sessionId">会话标识；缺省按当前工作区。</param>
    /// <returns>已启用技能数组（含内置、自定义与工作区文件技能）。</returns>
    public static Task<ActionResult> LoadedAsync(string? sessionId) => WebApi.RunGuardedAsync(async () =>
    {
        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var skills = await requestScope.GetRequiredService<ISkillCatalogService>()
            .ListAvailableAsync(sessionId, default)
            .ConfigureAwait(false);

        var items = skills
            .Select(s => new AvailableSkillDto(s.Id, s.Name, s.Description, s.Category, s.IsBuiltin))
            .ToList();
        return WebApi.OkJson(new { items });
    });

    /// <summary>读取技能内容。</summary>
    /// <param name="scope"><c>global</c> 或工作区标识。</param>
    /// <param name="key">技能名（目录名）。</param>
    /// <returns>技能内容；不存在时 404。</returns>
    public static Task<ActionResult> ReadAsync(string? scope, string? key) => WebApi.RunGuardedAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return WebApi.FailJson(400, "key 不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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
            : WebApi.OkJson(new SkillFileDto(
                key.Trim(), content.Name, content.Description, content.Category, content.Triggers, content.Body));
    });

    /// <summary>保存技能。</summary>
    /// <param name="payload">包装后的 <see cref="SkillSaveRequest"/> JSON。</param>
    /// <returns>保存后的条目键（目录名）。</returns>
    public static Task<ActionResult> SaveAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<SkillSaveRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        // name / description 必填校验由 SkillManagementService.SaveFile 统一承担（抛 ArgumentException → 400）
        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), input.Scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        string key;
        try
        {
            key = requestScope.GetRequiredService<ISkillManagementService>().SaveFile(
                input.OriginalId,
                new SkillFileContent(
                    input.Name.Trim(), input.Description, input.Category, input.Triggers, input.Body),
                root);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // 名称为空/含非法字符、同名目录冲突都属用户输入问题：回 400 让前端直接提示
            return WebApi.FailJson(400, ex.Message);
        }

        return WebApi.OkJson(new SettingsKeyResult(key));
    });

    /// <summary>导入技能 ZIP 包。</summary>
    /// <param name="payload">包装后的 <see cref="SkillImportRequest"/> JSON。</param>
    /// <returns>已导入、被拒与未落盘（非 SKILL.md）的条目。</returns>
    public static Task<ActionResult> ImportAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<SkillImportRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.ContentBase64))
        {
            return WebApi.FailJson(400, "ZIP 内容不能为空");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(input.ContentBase64);
        }
        catch (FormatException)
        {
            return WebApi.FailJson(400, "ZIP 内容不是合法的 Base64");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var (root, error) = await SettingsScope
            .ResolveAsync(requestScope.GetRequiredService<IWorkspaceManager>(), input.Scope)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        using var stream = new MemoryStream(bytes, writable: false);
        SkillImportResult result;
        try
        {
            result = requestScope.GetRequiredService<ISkillManagementService>().ImportZip(stream, root);
        }
        catch (InvalidDataException)
        {
            return WebApi.FailJson(400, "不是有效的 ZIP 包");
        }
        catch (InvalidOperationException ex)
        {
            // 条目数 / 解压总大小超限：属用户输入问题
            return WebApi.FailJson(400, ex.Message);
        }

        return WebApi.OkJson(new { imported = result.Imported, skipped = result.Skipped, ignored = result.Ignored });
    });

    /// <summary>删除技能。</summary>
    /// <param name="payload">包装后的 <see cref="SettingsKeyRequest"/> JSON。</param>
    /// <returns>空结果。</returns>
    public static Task<ActionResult> DeleteAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<SettingsKeyRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        if (string.IsNullOrWhiteSpace(input.Key))
        {
            return WebApi.FailJson(400, "key 不能为空");
        }

        await using var requestScope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
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