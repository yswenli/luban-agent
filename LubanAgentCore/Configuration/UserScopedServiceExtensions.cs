using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCore.Configuration;

/// <summary>
/// 按当前用户解析 keyed 注册的实例级服务的辅助扩展方法。
/// </summary>
public static class UserScopedServiceExtensions
{
    /// <summary>
    /// 解析当前用户标识，作为 keyed 服务的键；缺失时回退为 <c>default</c>。
    /// </summary>
    /// <param name="services">服务提供者</param>
    /// <returns>当前用户键</returns>
    public static string ResolveUserKey(this IServiceProvider services)
        => services.GetRequiredService<IUserContext>().UserId ?? "default";

    /// <summary>
    /// 解析当前用户的 <see cref="ProviderService"/> 实例。
    /// </summary>
    /// <param name="services">服务提供者</param>
    /// <returns>Provider 门面实例</returns>
    public static ProviderService GetProviderService(this IServiceProvider services)
        => services.GetRequiredKeyedService<ProviderService>(services.ResolveUserKey());

    /// <summary>
    /// 解析当前用户的模型能力门面 <see cref="ModelCapabilityService"/> 实例。
    /// </summary>
    /// <param name="services">服务提供者</param>
    /// <returns>模型能力门面实例</returns>
    public static ModelCapabilityService GetModelCapabilityService(this IServiceProvider services)
        => services.GetRequiredKeyedService<ModelCapabilityService>(services.ResolveUserKey());

    /// <summary>
    /// 解析当前用户的模型能力缓存 <see cref="ModelCapabilityCache"/> 实例。
    /// </summary>
    /// <param name="services">服务提供者</param>
    /// <returns>模型能力缓存实例</returns>
    public static ModelCapabilityCache GetModelCapabilityCache(this IServiceProvider services)
        => services.GetRequiredKeyedService<ModelCapabilityCache>(services.ResolveUserKey());
}