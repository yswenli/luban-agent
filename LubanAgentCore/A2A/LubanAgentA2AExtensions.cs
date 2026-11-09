/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.A2A
*文件名： LubanAgentA2AExtensions
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：A2A agent 侧 DI 注册入口，绑定配置并注册 SDK 实现，替换框架默认抽象
*
*****************************************************************************/

using LubanAgentCore.Protocol;

namespace LubanAgentCore.A2A;

/// <summary>
/// A2A agent 侧注册扩展。
/// <para>
/// 内部先调用框架 <see cref="A2AExtensions.AddLubanA2A"/> 绑定 <see cref="A2AOptions"/> 与默认抽象，
/// 再以 SDK 实现替换 <see cref="IA2AAgentRegistry"/>/<see cref="IA2ATransport"/>/
/// <see cref="IA2AServerHost"/>，并把 <see cref="ISubAgentExecutor"/> 替换为「未启动时回退默认、
/// 启动后走进程内/远端组合」的工厂。
/// </para>
/// <para>
/// 本方法只做 DI 注册；工作区相关的角色解析与启动由 <see cref="IA2ALifecycle"/> 在宿主确定工作区后驱动。
/// </para>
/// </summary>
public static class LubanAgentA2AExtensions
{
    /// <summary>
    /// 注册 A2A SDK 实现并绑定配置。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合，便于链式调用。</returns>
    public static DiContainer AddLubanAgentA2A(
        this DiContainer services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 框架基线：绑定 A2AOptions + 注册默认抽象（默认抛 NotSupportedException，随后被替换）。
        services.AddLubanA2A(configuration);

        // 运行时状态：承载工作区解析后的有效配置与「角色名 → 远端执行器」实时映射。
        services.TryAddSingleton<A2ARuntimeState>();

        // SDK 实现替换框架默认抽象。
        services.ReplaceSingleton<IA2AAgentRegistry, InMemoryA2AAgentRegistry>();
        services.ReplaceSingleton<IA2ATransport>(d =>
            new InProcessA2ATransport(d.GetRequiredService<IA2AAgentRegistry>()));
        services.ReplaceSingleton<IA2AServerHost>(d =>
            new A2AServerHost(
                d.GetRequiredService<A2ARuntimeState>(),
                d.GetRequiredService<IA2AAgentRegistry>(),
                d,
                d.GetService<ILoggerFactory>(),
                d.GetService<ILogger<A2AServerHost>>()));

        // 运行时生命周期（由宿主在工作区确定后显式驱动）。
        services.TryAddSingleton<A2ARuntimeLifecycle>();
        services.TryAddSingleton<IA2ALifecycle>(d => d.GetRequiredService<A2ARuntimeLifecycle>());

        // 状态只读查询：本机实现直接读上面的运行时对象；远程客户端由 Client/HttpA2AStatusProvider 覆盖为 HTTP 实现。
        services.AddScoped<IA2AStatusProvider, A2AStatusService>();

        // 子代理执行接缝：未启动 A2A 时严格回退默认实现（行为不变）；
        // 启动后经组合执行器按角色分发（进程内 / 远端）。
        services.ReplaceScoped<ISubAgentExecutor>(d =>
        {
            var state = d.GetRequiredService<A2ARuntimeState>();
            if (!state.IsStarted)
            {
                return d.GetRequiredService<SubAgentFactory>();
            }

            var inProcess = new InProcessSubAgentExecutor(
                d.GetRequiredService<IA2ATransport>(),
                () => state.LocalAgentName);
            return new CompositeSubAgentExecutor(inProcess, state.RemoteExecutors);
        });

        return services;
    }
}