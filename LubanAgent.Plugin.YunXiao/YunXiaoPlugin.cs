/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgent.Plugin.YunXiao
*文件名： YunXiaoPlugin
*版本号： V0.1.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/17
*描述：「云效 DevOps 工作台」Web 插件入口（骨架）
*
*****************************************************************************/


namespace LubanAgent.Plugin.YunXiao;

/// <summary>
/// 「云效 DevOps 工作台」插件：在 Web 宿主左侧栏贡献一个页面入口，页面本身位于 <c>web/</c>。
/// </summary>
/// <remarks>
/// <para>
/// 本插件面向 Web 宿主，界面由静态页面承担；入口程序集只用于满足「插件必须带入口程序集」的加载约束，
/// 不注册桌面面板（Web 宿主不使用面板实现，只读取清单里的 <c>webPage</c>）。
/// </para>
/// <para>
/// 云效能力一律经宿主 REST 取数，插件程序集不引用宿主程序集、不引用云效 SDK、不自行拼云效 HTTP。
/// </para>
/// </remarks>
public sealed class YunXiaoPlugin : PluginBase
{
    /// <summary>插件标识（与 <c>plugin.json</c> 的 id 一致）。</summary>
    public const string PluginId = "com.tjces.yunxiao";

    /// <summary>工作台页面的侧栏条目 id（与清单 <c>uiContributions[].id</c> 一致）。</summary>
    public const string WorkbenchEntryId = "yunxiao-workbench";

    /// <inheritdoc />
    public override string Id => PluginId;

    /// <inheritdoc />
    public override string Name => "云效 DevOps 工作台";

    /// <inheritdoc />
    public override string Version => "0.1.0";

    /// <inheritdoc />
    public override string Description => "Web 端云效 DevOps 工作台：连接设置、项目列表、项目初始化、PRD 拆分、迭代看板与任务工作区";

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        // 页面取数走宿主 REST，这里不做任何注册：不注册面板、不注册技能、不建立连接。
        SafeLog("初始化：Web 页面入口由清单 webPage 提供，入口程序集无需额外注册");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnShutdownAsync(CancellationToken cancellationToken)
    {
        SafeLog("已关闭：无后台任务需要回收");
        return Task.CompletedTask;
    }
}