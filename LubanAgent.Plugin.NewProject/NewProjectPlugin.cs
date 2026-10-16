/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject
*文件名： NewProjectPlugin
*创建时间：2026/10/16
*描述：「新建项目」插件入口（PRD §5 M4）
*****************************************************************************/

using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Base;
using LubanAgent.Plugin.NewProject.Services;
using LubanAgent.Plugin.NewProject.Ui;

namespace LubanAgent.Plugin.NewProject;

/// <summary>
/// 「新建项目」插件：以 TJCES 三端模板仓新建项目（七步向导 + 聊天式入口）。
/// </summary>
/// <remarks>
/// <para>
/// 插件只做三件事：注册侧栏面板（向导）、登记技能贡献（<c>newproject-*</c>，由清单声明 + 运行期贡献）、
/// 持有共享能力层实例（<see cref="INewProjectService"/>）供面板与聊天两条入口复用。
/// </para>
/// <para>
/// 云效能力全部经框架连接器 <c>LuBan.AIAgent.Connectors.AliyunDevOps</c>，插件内不自行拼 REST（PRD §4.11.1）。
/// </para>
/// </remarks>
public sealed class NewProjectPlugin : PluginBase
{
    /// <summary>插件标识（与 <c>plugin.json</c> 的 id 一致）。</summary>
    public const string PluginId = "com.tjces.newproject";

    /// <summary>向导面板的侧栏条目 id（与清单 <c>uiContributions[].id</c> 一致）。</summary>
    public const string WizardEntryId = "newproject-wizard";

    private readonly NewProjectService _service = new();

    private WizardPanel? _panel;

    /// <inheritdoc />
    public override string Id => PluginId;

    /// <inheritdoc />
    public override string Name => "新建项目";

    /// <inheritdoc />
    public override string Version => "1.0.0";

    /// <inheritdoc />
    public override string Description => "以 TJCES 三端模板仓新建项目：七步向导 + 聊天式入口（PRD §4）";

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        SafeLog("初始化：注册向导面板与共享能力层");

        _service.Attach(host);

        // 面板延迟创建：宿主内容区挂载时才实例化（保证在 UI 线程上创建控件）。
        // UI 投递经 PostToUi（带卸载即失效令牌），插件不直接触碰 Dispatcher（PRD §6 #22）。
        host.RegisterPanel(WizardEntryId, () => _panel ??= new WizardPanel(_service, PostToUi));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnShutdownAsync(CancellationToken cancellationToken)
    {
        _service.Detach();
        _panel = null;
        SafeLog("已关闭：共享能力层已解绑");
        return Task.CompletedTask;
    }
}