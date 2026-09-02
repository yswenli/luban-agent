/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
*文件名： StartupRunner
*版本号： V1.0.0.0
*唯一标识：启动运行器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/12
*描述：封装应用初始化逻辑，供启动向导调用
*
*****************************************************************************/
using LubanAgentCli.App.Services;
using LubanAgentCore.Hosting;
using LubanAgentCore.Infrastructure;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Retrieval;

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
    public static IServiceProvider BuildServiceProvider(
        IConfiguration configuration,
        OnnxEmbeddingGenerator? embedder,
        ModelManager? modelManager)
    {
        // TUI 特定：启用诊断埋点
        TuiDiag.Enabled = configuration.GetValue<bool>("LuBanAgent:DebugMode");

        // 调用 Core 的构建方法，注册 TUI 特定服务
        return AgentHostBuilder.BuildServiceProvider(
            configuration,
            embedder,
            modelManager,
            services =>
            {
                // TUI 特定：注册 TitleService
                services.AddSingleton<TitleService>();
            });
    }

    /// <summary>
    /// 初始化工作区。
    /// </summary>
    public static Task InitializeWorkspaceAsync(IServiceProvider sp, List<string> notices)
        => AgentHostBuilder.InitializeWorkspaceAsync(sp, notices);
}
