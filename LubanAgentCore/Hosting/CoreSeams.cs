using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Attachments;
using LuBan.AIAgent.Connectors;
using LuBan.AIAgent.Hosting;
using LuBan.Logging;
using LubanAgentCore.Agents;
using LubanAgentCore.Configuration;
using LubanAgentCore.Models;
using LubanAgentCore.Plugins;
using LubanAgentCore.Repositories;
using LubanAgentCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCore.Hosting;

/// <summary>按会话绑定的工作区解析（历史会话未绑定时回退当前工作区）。</summary>
public sealed class CoreWorkspaceResolver(
    SessionRepository sessionRepository,
    IWorkspaceManager workspaceManager,
    IUserContext userContext) : IAgentWorkspaceResolver
{
    /// <inheritdoc />
    public async Task<WorkspaceDescriptor> ResolveAsync(string sessionId, CancellationToken ct = default)
    {
        var userId = userContext.ResolveUserKey();
        var dbSession = await sessionRepository.GetBySessionIdAsync(sessionId, userId).ConfigureAwait(false);
        var workspaceId = dbSession?.WorkspaceId;

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            var ws = await workspaceManager.GetWorkspaceAsync(workspaceId).ConfigureAwait(false);
            if (ws is not null) return ToDescriptor(ws);
        }

        var current = workspaceManager.CurrentWorkspace
            ?? throw new InvalidOperationException("未设置当前工作区");

        return ToDescriptor(current);
    }

    /// <summary>产品工作区 → 框架描述。</summary>
    public static WorkspaceDescriptor ToDescriptor(WorkspaceInfo ws)
        => new(ws.WorkspaceId, ws.Name, ws.RootPath, ws.Type);
}

/// <summary>按工作区类型选 Profile。</summary>
public sealed class CoreAgentProfileResolver(IServiceProvider services) : IAgentProfileResolver
{
    /// <inheritdoc />
    public IAgentProfile Resolve(WorkspaceDescriptor workspace)
        => workspace.Type == WorkspaceTypes.LlmWiki
            ? ActivatorUtilities.CreateInstance<LlmWikiAgentProfile>(services, workspace)
            : ActivatorUtilities.CreateInstance<NormalAgentProfile>(services);
}

/// <summary>插件会话级贡献回放。</summary>
public sealed class PluginSessionExtension : IAgentSessionExtension
{
    /// <inheritdoc />
    public Task ApplyAsync(IServiceProvider sessionServices, WorkspaceDescriptor workspace, CancellationToken ct = default)
    {
        foreach (var contributor in sessionServices.GetServices<IPluginSessionContributor>())
        {
            try { contributor.Contribute(sessionServices); }
            catch (Exception ex) { Logger.Warn("插件会话级贡献回放失败", ex); }
        }
        return Task.CompletedTask;
    }
}

/// <summary>连接器工作区启用。</summary>
public sealed class ConnectorSessionExtension : IAgentSessionExtension
{
    /// <inheritdoc />
    public async Task ApplyAsync(IServiceProvider sessionServices, WorkspaceDescriptor workspace, CancellationToken ct = default)
    {
        var connectors = sessionServices.GetService<ConnectorRegistry>();
        if (connectors is null) return;

        try
        {
            connectors.LoadFromWorkspace(workspace.RootPath);
            await connectors.ApplyEnabledAsync(workspace.RootPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) { Logger.Warn("连接器工作区启用失败", ex); }
    }
}

/// <summary>用户解析：包产品的本地默认用户。</summary>
public sealed class CoreAgentUserResolver : IAgentUserResolver
{
    /// <inheritdoc />
    public string ResolveUserId(IServiceProvider services)
        => services.GetService<IUserContext>()?.UserId is { Length: > 0 } id
            ? id
            : LocalUserDefaults.DefaultUserId;
}

/// <summary>视觉能力门禁：按用户能力缓存判定能否携带图片，并在首次带图调用时回填能力。</summary>
public sealed class VisionModelCapabilityGate : IModelCapabilityGate
{
    private readonly AttachmentService _attachments;
    private readonly IServiceProvider _services;

    /// <summary>创建门禁。</summary>
    /// <param name="attachments">产品附件服务（持有视觉判定逻辑）。</param>
    /// <param name="services">当前会话作用域，用于取用户能力缓存。</param>
    public VisionModelCapabilityGate(AttachmentService attachments, IServiceProvider services)
    {
        _attachments = attachments;
        _services = services;
    }

    /// <inheritdoc />
    public bool CanAttach(string modelId, IReadOnlyList<ProcessedAttachment> attachments)
        => _attachments.CanAttach(modelId, attachments);

    /// <inheritdoc />
    public string GetUnsupportedMessage(string modelId, IReadOnlyList<ProcessedAttachment> attachments)
        => _attachments.GetUnsupportedMessage(modelId, attachments);

    /// <inheritdoc />
    public IModelCapabilityProbe? CreateProbe(string modelId, bool hasImage)
        => VisionCapabilityProbe.Create(modelId, hasImage, _services.GetModelCapabilityCache());
}

/// <summary>附件编排：复用产品的 <see cref="AttachmentService"/>（含 @ 引用解析）。</summary>
public sealed class CoreAttachmentOrchestrator(
    AttachmentService attachments,
    IModelCapabilityGate gate) : IAgentAttachmentOrchestrator
{
    /// <inheritdoc />
    public (string Text, IReadOnlyList<string> Paths) ExtractReferences(string input)
    {
        var paths = new List<string>(attachments.ParseFileReferences(input, out var remainingText));

        // 裸图片路径兜底：真机上「路径 + 问题」不会命中 @引用（用户不知道要加 @），
        // 若不在这里识别，模型只会拿到文本路径并改用 ReadFile 读二进制。
        var mentions = attachments.FindBareImageMentions(remainingText);
        if (mentions.Count > 0)
        {
            remainingText = AttachmentService.StripBareImageMentions(remainingText, mentions);
            foreach (var mention in mentions)
            {
                if (!paths.Contains(mention.Path, StringComparer.OrdinalIgnoreCase))
                    paths.Add(mention.Path);
            }
        }

        return (remainingText, paths);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProcessedAttachment>> ProcessAsync(
        IReadOnlyList<string> paths, CancellationToken ct = default)
        => attachments.ProcessAsync(paths, generateThumbnails: false, ct);

    /// <inheritdoc />
    public string? Validate(string modelId, IReadOnlyList<ProcessedAttachment> attachments)
        => attachments.Count == 0 || gate.CanAttach(modelId, attachments)
            ? null
            : gate.GetUnsupportedMessage(modelId, attachments);

    /// <inheritdoc />
    public IModelCapabilityProbe? CreateProbe(string modelId, bool hasImage)
        => gate.CreateProbe(modelId, hasImage);
}