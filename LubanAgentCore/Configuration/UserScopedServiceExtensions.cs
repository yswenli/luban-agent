using LuBan.AIAgent.Abstractions;
using LubanAgentCore.Context;
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

    /// <summary>
    /// 创建根派生作用域并回填指定用户身份。
    /// <para>MS.DI 的 <c>CreateScope()</c> 永远从根容器派生，不继承调用方作用域的身份状态；
    /// 未回填时按用户隔离的服务会回退到默认用户桶，故根派生 scope 必须显式回填。</para>
    /// </summary>
    /// <param name="services">服务提供者（通常为当前用户作用域）。</param>
    /// <param name="userId">目标用户标识；为空时回退本地默认用户。</param>
    public static IServiceScope CreateUserScope(this IServiceProvider services, string? userId)
        => services.GetRequiredService<IServiceScopeFactory>().CreateUserScope(userId);

    /// <summary>从作用域工厂创建根派生作用域并回填指定用户身份。</summary>
    /// <param name="scopeFactory">作用域工厂。</param>
    /// <param name="userId">目标用户标识；为空时回退本地默认用户。</param>
    public static IServiceScope CreateUserScope(this IServiceScopeFactory scopeFactory, string? userId)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserContextState>().UserId =
            string.IsNullOrWhiteSpace(userId) ? LocalUserDefaults.DefaultUserId : userId.Trim();
        return scope;
    }

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