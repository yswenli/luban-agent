/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App
*文件名： StartupRunner
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/12
*描述：封装应用初始化逻辑，供启动向导调用
*
*****************************************************************************/

namespace LubanAgentCli.App;

/// <summary>
/// 启动运行器。封装应用初始化逻辑，供启动向导调用。
/// </summary>
internal static class StartupRunner
{
    /// <summary>
    /// 构建配置。
    /// </summary>
    public static IConfiguration BuildConfiguration(string[] args)
        => AgentHostBuilder.BuildConfiguration(args);

    /// <summary>
    /// 构建服务容器。
    /// </summary>
    /// <param name="configuration">应用配置。</param>
    /// <param name="embedder">嵌入模型；未配置时为 null。</param>
    /// <param name="modelManager">嵌入模型管理器。</param>
    /// <param name="connection">到统一 Agent 服务的连接；null 表示进程内本地运行。</param>
    public static AgentHostServices BuildHost(
        IConfiguration configuration,
        OnnxEmbeddingGenerator? embedder,
        ModelManager? modelManager,
        AgentClientConnection? connection = null)
    {
        // TUI 特定：启用诊断埋点
        TuiDiag.Enabled = configuration.GetValue<bool>("LuBanAgent:DebugMode");

        // 调用 Core 的构建方法，注册 TUI 特定服务
        return AgentHostBuilder.Build(
            configuration, embedder, modelManager,
            services =>
            {
                // 远程模式（connection 非空）：A2A 服务端都在 WebHost 侧，客户端再注册会重复起一套生命周期
                if (connection is null)
                {
                    // A2A：注册 SDK 实现与运行时生命周期（实际启动在进入工作区后由 StartupDialog 触发）
                    services.AddLubanAgentA2A(configuration);
                }

                // 编排进度出口：本机编排由框架直接 Publish；远程模式下 HttpAgentHost 收到
                // orchestration.progress 帧后经 Raise 投递到同一出口。两种模式都注册，界面无需分叉。
                services.AddSingleton<HostOrchestrationProgressSink>();
                services.ReplaceSingleton<IOrchestrationProgressSink>(
                    d => d.GetRequiredService<HostOrchestrationProgressSink>());

                // TUI 特定：注册 TitleService
                services.AddSingleton<TitleService>();

                // 会话 / 工作区 / 对话改走统一 Agent 服务；必须在容器构建前覆盖。
                connection?.RegisterInto(services);
            },
            // 远程模式下本地不再读写 SQLite，跳过历史数据归一化，避免凭空建库。
            migrateLegacyUserData: connection is null);
    }

    /// <summary>
    /// 初始化工作区。
    /// </summary>
    public static Task InitializeWorkspaceAsync(DiContainer sp, List<string> notices)
        => AgentHostBuilder.InitializeWorkspaceAsync(sp, notices);
}
