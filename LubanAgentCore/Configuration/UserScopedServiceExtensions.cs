/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Configuration
*文件名： UserScopedServiceExtensions
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/12
*描述：按当前用户解析 Scoped 门面服务的辅助扩展。
*
*=================================================
*修改标记
*修改时间：2026/10/12
*修改人： yswenli
*版本号： V1.0.0.0
*描述：按当前用户解析 Scoped 门面服务的辅助扩展。
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

/// <summary>按当前用户解析 Scoped 门面服务的辅助扩展。</summary>
public static class UserScopedServiceExtensions
{
    /// <summary>解析当前用户标识；缺失时回退为本地默认用户。</summary>
    public static string ResolveUserKey(this IServiceProvider services)
        => services.GetRequiredService<IUserContext>().ResolveUserKey();

    /// <summary>解析用户标识；缺失时回退为本地默认用户。</summary>
    public static string ResolveUserKey(this IUserContext userContext)
        => userContext.UserId is { Length: > 0 } id ? id : LocalUserDefaults.DefaultUserId;

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