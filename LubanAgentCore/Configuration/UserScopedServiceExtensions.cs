using LuBan.AIAgent.Abstractions;
using LubanAgentCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCore.Configuration;

/// <summary>按当前用户解析 Scoped 门面服务的辅助扩展。</summary>
public static class UserScopedServiceExtensions
{
    /// <summary>解析当前用户标识；缺失时回退为本地默认用户。</summary>
    public static string ResolveUserKey(this IServiceProvider services)
        => services.GetRequiredService<IUserContext>().UserId is { Length: > 0 } id
            ? id
            : LocalUserDefaults.DefaultUserId;

    /// <summary>解析当前用户的 <see cref="ProviderService"/>。</summary>
    public static ProviderService GetProviderService(this IServiceProvider services)
        => services.GetRequiredService<ProviderService>();

    /// <summary>解析当前用户的 <see cref="ModelCapabilityService"/>。</summary>
    public static ModelCapabilityService GetModelCapabilityService(this IServiceProvider services)
        => services.GetRequiredService<ModelCapabilityService>();

    /// <summary>解析当前用户的 <see cref="ModelCapabilityCache"/>。</summary>
    public static ModelCapabilityCache GetModelCapabilityCache(this IServiceProvider services)
        => services.GetRequiredService<ModelCapabilityCache>();
}