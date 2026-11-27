/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebAgentsMdService
*描述：工作区 AGENTS.md 初始化业务（现状查询 / 模型生成并落盘 / 手动保存）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 工作区 AGENTS.md 初始化业务。
/// </summary>
/// <remarks>
/// 生成走一次「不装配工具」的非流式模型调用：宿主侧临时建会话承接本轮消息，
/// 结束后删除，避免污染用户的会话列表；落盘与章节规范化全部委托
/// <see cref="IAgentsMdInitializer"/>，与桌面端同一套规则。
/// </remarks>
public static class WebAgentsMdService
{
    private const string AgentDisabled =
        "LuBan Agent 未启用：请检查 appsettings 的 LuBanAgent 节与数据库配置";

    /// <summary>读取目标工作区的 AGENTS.md 现状。</summary>
    /// <param name="payload">包装后的 <see cref="AgentsMdInfoRequest"/> JSON。</param>
    /// <returns>现状（路径、是否存在、已有字符数）。</returns>
    public static Task<ActionResult> InfoAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!TryReadWorkspaceId(payload, out var workspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var workspace = await scope.GetRequiredService<IWorkspaceManager>()
            .GetWorkspaceAsync(workspaceId)
            .ConfigureAwait(false);
        if (workspace is null)
        {
            return WebApi.FailJson(404, "工作区不存在");
        }

        var initializer = scope.GetRequiredService<IAgentsMdInitializer>();
        var targetPath = initializer.GetTargetPath(workspace);
        var existing = ReadExistingText(targetPath);
        return WebApi.OkJson(new AgentsMdInfoDto(
            workspace.WorkspaceId,
            workspace.Name,
            workspace.RootPath,
            initializer.FileName,
            targetPath,
            existing is not null,
            existing?.Length ?? 0));
    });

    /// <summary>调用模型生成 AGENTS.md 并落盘。</summary>
    /// <param name="payload">包装后的 <see cref="AgentsMdGenerateRequest"/> JSON。</param>
    /// <returns>落盘结果。</returns>
    public static Task<ActionResult> GenerateAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<AgentsMdGenerateRequest>(payload, out var input) || input is null
            || string.IsNullOrWhiteSpace(input.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input.AgentName))
        {
            return WebApi.FailJson(400, "agentName 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input.Scene))
        {
            return WebApi.FailJson(400, "scene 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        if (scope.GetService(typeof(IAgentHost)) is not IAgentHost host)
        {
            return WebApi.FailJson(503, AgentDisabled);
        }

        var workspace = await scope.GetRequiredService<IWorkspaceManager>()
            .GetWorkspaceAsync(input.WorkspaceId.Trim())
            .ConfigureAwait(false);
        if (workspace is null)
        {
            return WebApi.FailJson(404, "工作区不存在");
        }

        var initializer = scope.GetRequiredService<IAgentsMdInitializer>();
        var targetPath = initializer.GetTargetPath(workspace);
        var existing = ReadExistingText(targetPath);
        if (existing is not null && !input.Overwrite)
        {
            return WebApi.FailJson(409, $"{initializer.FileName} 已存在，确认覆盖后重试");
        }

        var options = new AgentsMdOptions
        {
            AgentName = input.AgentName.Trim(),
            AgentScene = input.Scene.Trim(),
            Capabilities = NormalizeList(input.Capabilities),
            Tools = NormalizeList(input.Tools),
            OutputMode = string.IsNullOrWhiteSpace(input.OutputMode) ? "full" : input.OutputMode.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            ExistingContent = existing
        };

        string prompt;
        try
        {
            prompt = initializer.BuildPrompt(options);
        }
        catch (InvalidOperationException ex)
        {
            return WebApi.FailJson(500, ex.Message);
        }

        var sessions = scope.GetRequiredService<ISessionManager>();
        var previousSessionId = sessions.CurrentSession?.SessionId;
        var session = await sessions.CreateSessionAsync("初始化 AGENTS.md", workspace.WorkspaceId)
            .ConfigureAwait(false);

        var content = new StringBuilder();
        string? failure = null;

        try
        {
            var request = new AgentRunRequest
            {
                Input = prompt,
                SessionId = session.SessionId,
                EnableTools = false,
                ReasoningEffort = ReasoningEffort.None,
                PermissionMode = ToolPermissionMode.Default
            };

            await foreach (var evt in host
                .RunStreamingAsync(request, WebApi.Runtime.ShutdownToken)
                .ConfigureAwait(false))
            {
                switch (evt)
                {
                    case TextDeltaEvent text:
                        content.Append(text.Delta);
                        break;
                    case ErrorEvent error:
                        failure ??= error.Message;
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            failure = "生成已取消";
        }
        catch (Exception ex)
        {
            failure = WebApi.SafeErrorMessage(ex);
        }
        finally
        {
            // 临时会话只用于承接本轮消息，无论成败都丢弃，避免出现在会话列表里
            host.Reset(session.SessionId);
            await DeleteQuietlyAsync(sessions, session.SessionId).ConfigureAwait(false);
            // 删除会把「活动会话」指针置空，恢复成生成前的会话，避免影响后续不带 sessionId 的调用
            if (!string.IsNullOrWhiteSpace(previousSessionId))
            {
                await RestoreCurrentQuietlyAsync(sessions, previousSessionId).ConfigureAwait(false);
            }
        }

        if (failure is not null)
        {
            return WebApi.FailJson(502, failure);
        }

        var markdown = initializer.Normalize(content.ToString());
        if (markdown.Length == 0)
        {
            return WebApi.FailJson(502, "生成结果为空，AGENTS.md 未写入");
        }

        await initializer.WriteAsync(workspace, markdown).ConfigureAwait(false);

        // 让下一次对话重建 Agent，从而读到新的 AGENTS.md
        host.Reset();
        return WebApi.OkJson(new AgentsMdResultDto(
            targetPath,
            markdown,
            initializer.BuildVerificationPrompt(targetPath),
            session.SessionId));
    });

    /// <summary>保存用户编辑后的 Markdown（与生成走同一套规范化）。</summary>
    /// <param name="payload">包装后的 <see cref="AgentsMdSaveRequest"/> JSON。</param>
    /// <returns>落盘结果。</returns>
    public static Task<ActionResult> SaveAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<AgentsMdSaveRequest>(payload, out var input) || input is null
            || string.IsNullOrWhiteSpace(input.WorkspaceId))
        {
            return WebApi.FailJson(400, "workspaceId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(input.Markdown))
        {
            return WebApi.FailJson(400, "markdown 不能为空");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.UserId);
        var workspace = await scope.GetRequiredService<IWorkspaceManager>()
            .GetWorkspaceAsync(input.WorkspaceId.Trim())
            .ConfigureAwait(false);
        if (workspace is null)
        {
            return WebApi.FailJson(404, "工作区不存在");
        }

        var initializer = scope.GetRequiredService<IAgentsMdInitializer>();
        var targetPath = initializer.GetTargetPath(workspace);
        var existing = ReadExistingText(targetPath);
        if (existing is not null && !input.Overwrite)
        {
            return WebApi.FailJson(409, $"{initializer.FileName} 已存在，确认覆盖后重试");
        }

        var markdown = initializer.Normalize(input.Markdown);
        if (markdown.Length == 0)
        {
            return WebApi.FailJson(400, "规范化后内容为空，未写入");
        }

        await initializer.WriteAsync(workspace, markdown).ConfigureAwait(false);

        if (scope.GetService(typeof(IAgentHost)) is IAgentHost host)
        {
            host.Reset();
        }

        return WebApi.OkJson(new AgentsMdResultDto(
            targetPath,
            markdown,
            initializer.BuildVerificationPrompt(targetPath),
            null));
    });

    /// <summary>解析只带工作区标识的入参。</summary>
    private static bool TryReadWorkspaceId(string? payload, out string workspaceId)
    {
        workspaceId = "";
        if (!WebApi.TryReadPayload<AgentsMdInfoRequest>(payload, out var input)
            || string.IsNullOrWhiteSpace(input?.WorkspaceId))
        {
            return false;
        }

        workspaceId = input!.WorkspaceId.Trim();
        return true;
    }

    /// <summary>读取目标文件正文；不存在或读取失败返回 null。</summary>
    private static string? ReadExistingText(string targetPath)
    {
        try
        {
            if (!File.Exists(targetPath))
            {
                return null;
            }

            var content = File.ReadAllText(targetPath);
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>过滤空白项；无有效项返回 null。</summary>
    private static IReadOnlyList<string>? NormalizeList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var items = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();
        return items.Count == 0 ? null : items;
    }

    /// <summary>删除临时会话；失败不影响已完成的落盘。</summary>
    private static async Task DeleteQuietlyAsync(ISessionManager sessions, string sessionId)
    {
        try
        {
            await sessions.DeleteSessionAsync(sessionId).ConfigureAwait(false);
        }
        catch
        {
            /* 临时会话残留不影响结果 */
        }
    }

    /// <summary>恢复活动会话指针；失败不影响已完成的落盘。</summary>
    private static async Task RestoreCurrentQuietlyAsync(ISessionManager sessions, string sessionId)
    {
        try
        {
            await sessions.SetCurrentSessionAsync(sessionId).ConfigureAwait(false);
        }
        catch
        {
            /* 指针恢复失败只影响「最近会话」的默认值 */
        }
    }
}