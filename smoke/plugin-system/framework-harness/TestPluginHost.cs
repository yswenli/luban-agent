using LuBan.AIAgent.Connectors;
using LuBan.AIAgent.Plugins;

namespace Harness;

/// <summary>
/// 冒烟测试用的最小 <see cref="IPluginHost"/> 实现：只记账，不做真实 UI/工作区落地。
/// </summary>
/// <remarks>
/// 真实宿主（luban-agent-codex）需把 <see cref="RegisterSkill"/> / <see cref="RegisterMcpServer"/>
/// 落到工作区作用域的 <c>SkillRegistry</c> / <c>MCPRegistry</c>（PRD §3.11 三件套），
/// 本实现只用于验证框架侧的调用顺序与记账。
/// </remarks>
internal sealed class TestPluginHost : IPluginHost
{
    private readonly Dictionary<string, PluginSidebarEntry> _sidebarEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<IPluginPanel>> _panels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action> _pendingUi = new();
    private readonly List<string> _logs = new();

    /// <summary>是否保留 <see cref="PostToUi"/> 回调（token 模式专用）。</summary>
    public bool RetainUiCallbacks { get; init; }

    public IServiceProvider Services { get; set; } = null!;

    public string PluginId => "com.demo.hello";

    /// <inheritdoc />
    /// <remarks>M3 新增成员：宿主分发 host 时回填当前插件声明的权限；harness 不做真实门禁，给空集合即可。</remarks>
    public PluginPermissions Permissions { get; set; } = new();

    public IReadOnlyDictionary<string, PluginSidebarEntry> SidebarEntries => _sidebarEntries;

    public IReadOnlyDictionary<string, Func<IPluginPanel>> Panels => _panels;

    public HashSet<string> Skills { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> McpServers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Connectors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Logs => _logs;

    public void Log(string message) => _logs.Add(message);

    public void LogError(string message, Exception? exception = null) => _logs.Add($"[ERROR] {message} {exception?.Message}");

    public string? GetConfig(string key) => null;

    public void RegisterPanel(string entryId, Func<IPluginPanel> factory) => _panels[entryId] = factory;

    public void UnregisterPanel(string entryId) => _panels.Remove(entryId);

    public void RegisterSidebarEntry(PluginSidebarEntry entry) => _sidebarEntries[entry.Id] = entry;

    public void UnregisterSidebarEntry(string entryId) => _sidebarEntries.Remove(entryId);

    public void RegisterCommand(PluginCommandDescriptor command)
    {
    }

    public void RegisterSkill(SkillDefinition definition) => Skills.Add(definition.Id);

    public void UnregisterSkill(string skillId) => Skills.Remove(skillId);

    public void RegisterMcpServer(string name) => McpServers.Add(name);

    public void UnregisterMcpServer(string name) => McpServers.Remove(name);

    public void RegisterConnector(ConnectorFactoryRegistration registration) => Connectors.Add(registration.ProviderKey);

    public void UnregisterConnector(string providerKey) => Connectors.Remove(providerKey);

    public void PostToUi(Action action)
    {
        // 卸载/GC 模式：不保留委托，避免测试自身钉住插件 ALC（真实的宿主会立刻投递到 UI 线程）。
        if (!RetainUiCallbacks)
            return;

        _pendingUi.Add(action);
    }

    public IReadOnlyList<PluginSidebarEntry> GetSidebarEntries() => _sidebarEntries.Values.ToArray();

    public IPluginPanel? GetPanel(string entryId) => _panels.TryGetValue(entryId, out var factory) ? factory() : null;

    /// <summary>
    /// 模拟 UI 线程追上队列：执行全部残留回调，返回执行个数。
    /// 卸载后残留数必须为 0，否则说明失效令牌未生效（PRD §3.12.5 v0.18）。
    /// </summary>
    public int FlushUi()
    {
        var pending = _pendingUi.ToArray();
        _pendingUi.Clear();

        foreach (var action in pending)
        {
            try
            {
                action();
            }
            catch
            {
                // 令牌失效时应被 PluginBase 内部吞掉，此处兜底
            }
        }

        return pending.Length;
    }
}