using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Base;

namespace CodexSmokePlugin;

/// <summary>
/// M2 冒烟插件：验证 codex 宿主（<c>IPluginHost</c> 实现）的
/// 侧栏条目、内容区面板挂载、技能落盘、MCP 回放、UI 线程投递与卸载解绑。
/// </summary>
public sealed class CodexSmokeMain : PluginBase
{
    private Timer? _timer;
    private int _tick;
    private CodexSmokePanel? _panel;

    /// <inheritdoc />
    public override string Id => "com.demo.codex";

    /// <inheritdoc />
    public override string Name => "Codex 冒烟插件";

    /// <inheritdoc />
    public override string Description => "M2 宿主冒烟：侧栏/内容区/技能/MCP/卸载";

    /// <inheritdoc />
    public override IEnumerable<SkillDefinition> ContributeSkills() => new[]
    {
        new SkillDefinition
        {
            Id = "codex-runtime-skill",
            Name = "运行时贡献技能",
            Description = "由 ContributeSkills 声明",
            Entry = "skills/codex-runtime-skill.md"
        }
    };

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        host.Log("CodexSmokePlugin 初始化");

        // P4：注册面板工厂（延迟到宿主内容区挂载时才实例化，保证在 UI 线程上创建控件）
        host.RegisterPanel("codex-hello", () => _panel ??= new CodexSmokePanel());

        // P6/技能：运行时代码直接贡献一个技能（与 ContributeSkills 幂等重复，用于覆盖两条路径）
        host.RegisterSkill(new SkillDefinition
        {
            Id = "codex-runtime-skill",
            Name = "运行时贡献技能",
            Description = "由 host.RegisterSkill 直接注册",
            Entry = "skills/codex-runtime-skill.md"
        });

        // UI 投递：每秒 +1，验证 PostToUi 跨线程投递与卸载后令牌失效（不再有回调落到面板）
        _timer = new Timer(_ => PostToUi(() =>
        {
            _tick++;
            _panel?.SetTick(_tick);
        }), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnShutdownAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        SafeLog("定时器已释放");
        return Task.CompletedTask;
    }
}