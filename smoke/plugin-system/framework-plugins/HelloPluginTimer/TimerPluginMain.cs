using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Base;

namespace HelloPluginTimer;

/// <summary>
/// 二分定位变体：与 codex 冒烟插件同构，额外持有「每秒 PostToUi」的定时器，
/// 用于判断 ALC 回收失败是否由周期性 UI 投递引起。
/// </summary>
public sealed class TimerPluginMain : PluginBase
{
    private Timer? _timer;

    public override string Id => "com.demo.hello";

    public override string Name => "定时器冒烟插件";

    public override string Description => "验证周期性 PostToUi 是否钉住 ALC";

    protected override Task OnInitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        host.Log("TimerPlugin 初始化");

        host.RegisterPanel("hello", () => new TimerPanel());

        host.RegisterSkill(new SkillDefinition
        {
            Id = "hello-plugin-skill",
            Name = "冒烟技能",
            Description = "运行时贡献"
        });

        _timer = new Timer(_ => PostToUi(() => { }), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        return Task.CompletedTask;
    }

    protected override Task OnShutdownAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
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
public sealed class TimerPanel : IPluginPanel
{
    public string Title => "定时器面板";

    public void OnUnloading()
    {
    }
}