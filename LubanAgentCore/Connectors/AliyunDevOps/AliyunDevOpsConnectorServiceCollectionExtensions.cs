/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： AliyunDevOpsConnectorServiceCollectionExtensions
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：云效连接器与进程内 MCP 客户端的 DI 注册扩展（PRD §3.10 / §4.11）
*
*****************************************************************************/
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效连接器与进程内 MCP 客户端的 DI 注册扩展（产品侧装配入口）。
/// </summary>
/// <remarks>
/// <para>
/// 注册项（均为单例，构造链不含 Scoped 服务，可在 <c>ValidateScopes=true</c> 下通过构建校验）：
/// <see cref="YunxiaoApiOptions"/>、<see cref="IYunxiaoCredentialProvider"/>（宿主可先注册覆盖）、
/// <see cref="YunxiaoConnector"/>、<see cref="CodeupClient"/>、<see cref="FlowClient"/>；
/// 并向框架 <c>ConnectorFactoryRegistry</c> 登记提供方工厂（<see cref="YunxiaoConnector.ProviderKey"/>）；
/// 同时以 <see cref="AliyunCredentialKeyAliases"/> 覆盖框架默认的空凭据键别名实现。
/// </para>
/// <para>
/// 注意：<b>不</b>把 <c>YunxiaoMCPClient</c> 注册进容器。按 PRD §4.11，MCP 生命周期跟随插件/工作区——
/// 由 <c>ConnectorRegistry</c> 从 <see cref="YunxiaoConnector.McpExposures"/> 按需派生并注册进会话的
/// <c>MCPRegistry</c>，停用/卸载时反注册。
/// </para>
/// </remarks>
public static class AliyunDevOpsConnectorServiceCollectionExtensions
{
    /// <summary>
    /// 云效配置节名。
    /// </summary>
    public const string ConfigurationSectionName = "LuBanAgent:Connectors:Yunxiao";

    /// <summary>
    /// 注册云效连接器（含提供方工厂与凭据键别名）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置。</param>
    /// <returns>服务集合（链式）。</returns>
    public static IServiceCollection AddAliyunDevOpsConnector(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(ConfigurationSectionName);

        services.TryAddSingleton(_ =>
        {
            var options = new YunxiaoApiOptions();
            section.Bind(options);
            return options;
        });

        // 默认凭据提供者：优先读 ICredentialStore（宿主可覆盖的凭据存储），未命中再回退配置节点。
        // 注意：必须用工厂显式传入 configuration，不能按实现类型注册——否则容器需要解析 IConfiguration，
        // 而宿主（含 AddLuBanAgentDefaults 的最小宿主与 AgentHostBuilder 的 ValidateOnBuild）未必把
        // IConfiguration 本身注册为服务，会直接导致 "Unable to resolve service for type IConfiguration"。
        services.TryAddSingleton<IYunxiaoCredentialProvider>(
            sp => new ConfigurationYunxiaoCredentialProvider(configuration, sp.GetService<ICredentialStore>()));

        services.TryAddSingleton<YunxiaoConnector>(sp => CreateConnector(sp));

        services.TryAddSingleton(sp => new CodeupClient(
            sp.GetRequiredService<YunxiaoApiOptions>(),
            sp.GetRequiredService<IYunxiaoCredentialProvider>(),
            sp.GetService<ProcessRunner>(),
            sp.GetService<PluginPermissionGuard>() ?? PluginPermissionGuard.Default));

        services.TryAddSingleton(sp => new FlowClient(
            sp.GetRequiredService<YunxiaoApiOptions>(),
            sp.GetRequiredService<IYunxiaoCredentialProvider>(),
            sp.GetService<PluginPermissionGuard>() ?? PluginPermissionGuard.Default));

        // 向框架登记提供方工厂：连接器实例由 ConnectorInstanceHost 按实例名各造一个。
        services.AddConnector<YunxiaoConnector>(YunxiaoConnector.ProviderKey, context =>
        {
            var connector = CreateConnector(context.Services);
            connector.Name = context.Name;
            return connector;
        });

        // 凭据键历史别名（框架已去厂商化，别名由产品承载）：覆盖框架默认的空实现。
        services.RemoveAll<ICredentialKeyAliases>();
        services.AddSingleton<ICredentialKeyAliases>(AliyunCredentialKeyAliases.Instance);

        return services;
    }

    /// <summary>
    /// 按容器造一个云效连接器实例（供容器直接注入，亦供连接器实例宿主按实例名各造一个）。
    /// </summary>
    /// <remarks>
    /// 连接器实例不是「共享单例」语义：<c>ConnectorInstanceHost</c> 按<b>实例名</b>各造一个并各自持有，
    /// 故此处必须是「每次调用新造」的工厂；<c>YunxiaoConnector.Name</c> 只能由调用方设置到<b>自己的</b>
    /// 实例上，绝不可改写容器内的单例（否则多实例互相覆盖，且暴露面名/凭据键全部串味）。
    /// </remarks>
    /// <param name="services">根容器。</param>
    /// <returns>新的连接器实例。</returns>
    internal static YunxiaoConnector CreateConnector(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new YunxiaoConnector(
            services.GetRequiredService<YunxiaoApiOptions>(),
            services.GetRequiredService<IYunxiaoCredentialProvider>(),
            services.GetService<ProcessRunner>(),
            services.GetService<PluginPermissionGuard>() ?? PluginPermissionGuard.Default);
    }
}

/// <summary>
/// 基于配置的云效凭据提供者（默认实现，便于本地联调）。
/// </summary>
/// <remarks>
/// <para>
/// 依次尝试：① <see cref="ICredentialStore"/>（宿主可写凭据存储，PRD §3.7），键为
/// <c>connector:&lt;实例名&gt;:token</c>（默认实例即 <c>connector:yunxiao:token</c>，
/// 由 <see cref="CredentialKeys.ForConnectorToken"/> 拼出，历史拼写键由
/// <see cref="AliyunCredentialKeyAliases"/> 回退读取）；
/// ② 配置节点 <c>LuBanAgent:Connectors:Yunxiao:AccessToken</c>（其它实例为
/// <c>LuBanAgent:Connectors:&lt;实例名&gt;:AccessToken</c>）。均未命中时返回 <c>null</c>，
/// 连接器会给出明确错误。<b>不缓存、不日志回显</b>明文 PAT（PRD §4.9.3）。
/// </para>
/// </remarks>
internal sealed class ConfigurationYunxiaoCredentialProvider : IYunxiaoCredentialProvider
{
    private readonly IConfiguration _configuration;
    private readonly ICredentialStore? _credentialStore;

    /// <summary>
    /// 创建配置凭据提供者。
    /// </summary>
    /// <param name="configuration">配置。</param>
    /// <param name="credentialStore">凭据存储；为空时仅读配置节点。</param>
    public ConfigurationYunxiaoCredentialProvider(IConfiguration configuration, ICredentialStore? credentialStore = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _credentialStore = credentialStore;
    }

    /// <inheritdoc />
    public Task<string?> GetPatAsync(CancellationToken cancellationToken = default)
        => GetPatAsync(null, cancellationToken);

    /// <inheritdoc />
    public async Task<string?> GetPatAsync(string? connectorName, CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(connectorName) ? YunxiaoConnector.DefaultName : connectorName.Trim();

        if (_credentialStore is not null)
        {
            // 凭据键按实例名泛化（connector:<实例名>:token）；历史拼写键由存储层的 ICredentialKeyAliases 回退读取。
            var stored = await _credentialStore.GetAsync(CredentialKeys.ForConnectorToken(name), cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stored))
                return stored;
        }

        // 配置节点兜底：默认实例沿用既有节名；其它实例按实例名取节
        var section = string.Equals(name, YunxiaoConnector.DefaultName, StringComparison.OrdinalIgnoreCase)
            ? AliyunDevOpsConnectorServiceCollectionExtensions.ConfigurationSectionName
            : $"LuBanAgent:Connectors:{name}";

        return _configuration[$"{section}:AccessToken"];
    }
}