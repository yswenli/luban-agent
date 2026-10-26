using LuBan.AIAgent.Defaults;
using LuBan.AIAgent.Hosting;
using LubanAgentCore.Context;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCore.Hosting;

/// <summary>
/// 补齐产品特有的会话上下文字段：框架只认 <see cref="DefaultContextState"/>，
/// 产品侧按用户隔离的服务读 <see cref="UserContextState"/>，故需把身份与工作区同步过去。
/// </summary>
public sealed class CoreSessionRuntimeInitializer(
    DefaultContextState state,
    IServiceProvider services) : ISessionRuntimeInitializer
{
    /// <inheritdoc />
    public Task InitializeAsync(SessionRuntimeSeed seed, CancellationToken ct = default)
    {
        state.WorkspaceId = seed.WorkspaceId;

        var productState = services.GetService<UserContextState>();
        if (productState is not null && !string.IsNullOrWhiteSpace(state.UserId))
            productState.UserId = state.UserId;

        return Task.CompletedTask;
    }
}