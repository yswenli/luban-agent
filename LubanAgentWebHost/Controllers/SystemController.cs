/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Controllers
*文件名： SystemController
*描述：系统接口（健康检查、浏览器端首屏引导与命令清单）
*
*****************************************************************************/

namespace LubanAgentWebHost.Controllers;

/// <summary>
/// 系统接口。
/// </summary>
public sealed class SystemController : Controller
{
    /// <summary>
    /// 健康检查。
    /// </summary>
    /// <remarks>
    /// 免令牌校验（客户端启动器在拿到令牌前就要探测端口），但仍受来源校验保护；
    /// 不触发用户初始化，避免探测被初始化耗时拖长、也不让初始化失败拖垮端口就绪判定。
    /// </remarks>
    /// <returns>宿主信息。</returns>
    [HttpGet]
    public Task<ActionResult> Health() => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync(requireToken: false, ensureUser: false) is { } denied)
        {
            return denied;
        }

        return WebApi.OkJson(new HealthDto(
            "ok",
            Version,
            WebApi.Runtime.UserId,
            DateTime.Now));
    });

    /// <summary>
    /// 浏览器端首屏数据：工作区、供应商与模型、权限模式、附件与确认策略。
    /// </summary>
    /// <returns>引导数据。</returns>
    [HttpGet]
    public Task<ActionResult> Bootstrap() => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);

        var workspaceManager = scope.GetRequiredService<IWorkspaceManager>();
        var workspaces = await workspaceManager.GetUserWorkspacesAsync().ConfigureAwait(false);

        var providerService = scope.GetRequiredService<IProviderModelManagementService>();
        var providers = new List<ProviderSummaryDto>();
        foreach (var provider in await providerService.GetProvidersAsync().ConfigureAwait(false))
        {
            var models = await providerService.GetModelsAsync(provider.Name).ConfigureAwait(false);
            providers.Add(new ProviderSummaryDto(
                provider.Name,
                string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Name : provider.DisplayName!,
                models));
        }

        var (providerId, modelId) = providerService.GetSelection();

        var attachments = scope.GetRequiredService<AttachmentService>();

        return WebApi.OkJson(new BootstrapDto(
            WebApi.Runtime.UserId,
            Version,
            workspaces.Select(ToWorkspaceDto).ToList(),
            workspaceManager.CurrentWorkspace?.WorkspaceId,
            providers,
            providerId is null && modelId is null ? null : new ModelSelectionDto(providerId, modelId),
            Enum.GetValues<ToolPermissionMode>()
                .Select(mode => new PermissionModeDto(
                    PermissionModeRules.ToKey(mode),
                    PermissionModeRules.ToDisplayName(mode)))
                .ToList(),
            Enum.GetNames<ReasoningEffort>(),
            new AttachmentPolicyDto(
                attachments.SupportedExtensions.ToList(),
                AttachmentService.MaxAttachmentsPerMessage,
                MediaTypeUtil.HeicLikeExtensions.ToList(),
                // 与框架 DefaultAttachmentProcessor 的阈值保持一致：文本 10MB 上限、图片 5MB / 2000px 触发缩放
                10L * 1024 * 1024,
                5L * 1024 * 1024,
                2000),
            new ConfirmationPolicyDto(
                scope.GetService<IToolConfirmationBroker>()?.TimeoutSeconds
                    // 与框架 LuBanAgentOptions.Confirmation.TimeoutSeconds 的默认值一致
                    ?? 60)));
    });

    /// <summary>
    /// 浏览器端斜杠命令清单（过滤掉 CLI 专属命令与「退出程序」）。
    /// </summary>
    /// <returns>命令清单。</returns>
    [HttpGet]
    public Task<ActionResult> Commands() => WebApi.RunAsync(async () =>
    {
        if (await WebApi.GuardAsync() is { } denied)
        {
            return denied;
        }

        var commands = new CommandCatalog().All
            .Where(command => command.Supports(SlashCommandClient.Codex))
            .Where(command => !string.Equals(command.Name, "/exit", StringComparison.OrdinalIgnoreCase))
            .Select(command => new WebCommandDto(
                command.Name,
                command.Aliases,
                command.Description,
                command.Category,
                command.Usage,
                WebCommandAction(command.Name)))
            .ToList();

        return WebApi.OkJson(commands);
    });

    private static string Version =>
        typeof(SystemController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(SystemController).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static WorkspaceDto ToWorkspaceDto(WorkspaceInfo workspace) => new(
        workspace.WorkspaceId,
        workspace.Name,
        workspace.RootPath,
        workspace.Type,
        workspace.UserId,
        workspace.ConfigPath,
        workspace.LastActiveAt,
        workspace.IsAuthorized);

    /// <summary>命令名到前端动作标识的映射；前端只认识这些动词。</summary>
    /// <param name="name">命令名（含 /）。</param>
    /// <returns>动作标识。</returns>
    private static string WebCommandAction(string name) => name switch
    {
        "/help" => "action:help",
        "/clear" => "action:clearMessages",
        "/mode" => "action:cyclePermissionMode",
        "/stats" => "action:showSessionStats",
        "/session" => "action:openSessionList",
        "/work" => "action:focusWorkspaces",
        "/llmwiki" => "action:openLlmWiki",
        "/provider" => "openSettings:provider",
        "/model" => "openSettings:model",
        "/skill" => "openSettings:skill",
        "/rule" => "openSettings:rule",
        "/mcp" => "openSettings:mcp",
        "/a2a" => "openSettings:a2a",
        _ => "action:none"
    };
}