using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Base;

namespace HelloPlugin;

/// <summary>
/// 冒烟测试插件：验证 ALC 热卸载、UI 贡献注销、Skill/MCP 注册与 PostToUi 失效令牌。
/// </summary>
public sealed class HelloPluginMain : PluginBase
{
    /// <summary>插件程序集内的静态计数器，用于验证 UI 投递令牌是否失效。</summary>
    public static int UiCallbackCount;

    public override string Id => "com.demo.hello";

    public override string Name => "冒烟测试插件";

    public override string Description => "M1 插件体系冒烟测试";

    protected override Task OnInitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        host.Log("HelloPlugin 初始化");

        // ① 排一个 UI 回调，随后立即卸载：断言其副作用不再发生（PRD §3.12.5 v0.18）
        //    副作用写进程级环境变量，便于跨 ALC 观测。
        PostToUi(() =>
        {
            UiCallbackCount++;
            Environment.SetEnvironmentVariable("HELLO_PLUGIN_UI_CALLBACK", "1");
        });

        // ② 注册面板工厂（宿主侧持有委托 → 卸载第 2 步必须清理，否则 ALC 无法回收）
        host.RegisterPanel("hello", () => new HelloPanel());

        // ③ 注册运行时贡献的技能
        host.RegisterSkill(new SkillDefinition
        {
            Id = "hello-plugin-skill",
            Name = "冒烟技能",
            Description = "运行时贡献"
        });

        return Task.CompletedTask;
    }

    protected override Task OnShutdownAsync(CancellationToken cancellationToken)
    {
        // 此处故意不做任何退订，验证框架侧第 2/3 步的注销能力是否足够
        return Task.CompletedTask;
    }

    public override IEnumerable<SkillDefinition> ContributeSkills()
    {
        yield return new SkillDefinition
        {
            Id = "hello-plugin-extra",
            Name = "运行时第二技能",
            Description = "ContributeSkills 通道"
        };
    }
}

/// <summary>占位面板实现。</summary>
public sealed class HelloPanel : IPluginPanel
{
    public string Title => "冒烟面板";

    public void OnUnloading()
    {
    }
}