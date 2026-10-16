using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Base;
using LuBan.AIAgent.Plugins.Lifecycle;
using LuBan.AIAgent.Plugins.Loading;
using Microsoft.Extensions.DependencyInjection;

namespace Harness;

/// <summary>
/// M1 插件体系冒烟测试宿主（PRD §3.4 / §3.12 / §3.12.5）。
/// </summary>
/// <remarks>
/// 两种模式：
/// <list type="bullet">
/// <item><c>unload</c> —— 正向：启用 → 停用 → 断言 ALC 弱引用死亡（RequiresRestart=false）+ UI/Skill/MCP 精确注销。</item>
/// <item><c>token</c> —— 反向：宿主故意保留插件的 UI 委托（等价「订阅后不退订」）→ 断言
/// 卸载被<b>检出失败</b>并降级（Disabled + RequiresRestart=true），而不是静默通过；
/// 同时断言 <c>PostToUi</c> 失效令牌拦截了卸载后追补的回调。</item>
/// <item><c>unload -- &lt;plugins-timer&gt;</c> —— 二分定位：把「周期 PostToUi 定时器」变体插件跑正向模式，
/// 用来排除「定时器钉住 ALC」这一嫌疑（见 README §2）。</item>
/// </list>
/// </remarks>
internal static class Program
{
    private const string PluginId = "com.demo.hello";

    private static async Task<int> Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "unload";
        var root = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "plugins"));

        Console.WriteLine($"=== 模式 {mode} / 插件根 {root} ===");

        Environment.SetEnvironmentVariable("HELLO_PLUGIN_UI_CALLBACK", null);

        var testHost = new TestPluginHost { RetainUiCallbacks = mode == "token" };
        var stateFile = Path.Combine(Path.GetTempPath(), $"luban-plugin-state-{Guid.NewGuid():N}.json");

        using var provider = new ServiceCollection()
            .AddSingleton<IPluginHost>(testHost)
            .BuildServiceProvider();

        var validator = new ManifestValidator(new ManifestValidationOptions
        {
            TargetApp = "Harness",
            HostVersion = "2026.10.15"
        });
        var factory = new PluginFactory();
        var loader = new PluginLoader(validator, factory);
        var resolver = new PluginDependencyResolver();
        var catalog = new BuiltinPluginCatalog(Array.Empty<IPlugin>());
        var repository = new PluginStateRepository(stateFile);
        var manager = new PluginManager(loader, resolver, catalog, repository, provider, new PluginManagerOptions
        {
            UnloadVerificationRounds = 8
        });

        var failures = new List<string>();

        // ── 扫描与登记 ──
        var found = await manager.ScanAsync(new[] { root });
        Check(failures, found == 1, $"扫描发现 1 个插件（实际 {found}）");
        Check(failures, manager.GetPlugin(PluginId)?.State == PluginState.Installed, "扫描后状态为 Installed");
        if (found != 1)
            return Report(failures);

        // ── 启用 ──
        Check(failures, await manager.EnableAsync(PluginId), "EnableAsync 返回 true");
        Check(failures, manager.GetPlugin(PluginId)!.State == PluginState.Enabled, "启用后状态为 Enabled");

        Check(failures, testHost.SidebarEntries.Count == 1, $"清单声明注册了 1 个侧栏条目（实际 {testHost.SidebarEntries.Count}）");
        Check(failures, testHost.Panels.Count == 1, $"插件自行注册了 1 个面板工厂（实际 {testHost.Panels.Count}）");
        Check(failures,
            testHost.Skills.SetEquals(new[] { "hello-manifest-skill", "hello-plugin-extra", "hello-plugin-skill" }),
            $"三条 Skill 通道全部生效：清单 + ContributeSkills + host.RegisterSkill（实际 [{string.Join(",", testHost.Skills)}]）");
        Check(failures, testHost.McpServers.SetEquals(new[] { "yunxiao" }),
            $"清单声明的 MCP 已注册（实际 [{string.Join(",", testHost.McpServers)}]）");

        // ── 停用：7 步卸载 + GC 弱引用验证 ──
        Check(failures, await manager.DisableAsync(PluginId), "DisableAsync 返回 true（卸载失败也不算硬失败）");

        var afterDisable = manager.GetPlugin(PluginId)!;
        Check(failures, afterDisable.State == PluginState.Disabled, "停用后状态为 Disabled");

        if (mode == "unload")
        {
            Check(failures, !afterDisable.RequiresRestart,
                $"【正向·GC 断言】ALC/实例/程序集/类型弱引用全部死亡，程序集已真回收（RequiresRestart={afterDisable.RequiresRestart}）");
        }
        else
        {
            Check(failures, afterDisable.RequiresRestart,
                $"【反向·降级断言】残留引用被检出，正确降级为 Disabled+需重启而非静默通过（RequiresRestart={afterDisable.RequiresRestart}）");
        }

        Check(failures, testHost.SidebarEntries.Count == 0, $"【第 2 步】侧栏条目已注销（实际 {testHost.SidebarEntries.Count}）");
        Check(failures, testHost.Panels.Count == 0, $"【第 2 步】面板工厂已注销（实际 {testHost.Panels.Count}）");
        Check(failures, testHost.Skills.Count == 0, $"【第 3 步】全部 Skill 已反注册（实际 [{string.Join(",", testHost.Skills)}]）");
        Check(failures, testHost.McpServers.Count == 0, $"【第 3 步】MCP 已反注册（实际 [{string.Join(",", testHost.McpServers)}]）");

        // ── UI 投递令牌（PRD §3.12.5 v0.18）──
        var flushed = testHost.FlushUi();
        var leaked = Environment.GetEnvironmentVariable("HELLO_PLUGIN_UI_CALLBACK");

        if (mode == "token")
        {
            Check(failures, flushed >= 1, $"UI 线程追补执行了 {flushed} 个卸载前排入的回调");
            Check(failures, leaked is null, "【令牌断言】追补回调被失效令牌丢弃，副作用未发生，且未抛「访问已卸载类型」异常");
        }
        else
        {
            Check(failures, flushed == 0, $"卸载未遗留 UI 回调（实际 {flushed}）");
        }

        // ── 可重复装载 ──
        Check(failures, await manager.EnableAsync(PluginId), "二次 EnableAsync 成功（上一轮已彻底清理）");
        Check(failures, testHost.Skills.Count == 3, $"二次启用后 Skill 重新注册为 3 条（实际 {testHost.Skills.Count}）");
        Check(failures, await manager.DisableAsync(PluginId), "二次 DisableAsync 成功");
        if (mode == "unload")
            Check(failures, !manager.GetPlugin(PluginId)!.RequiresRestart, "二次卸载 ALC 仍确认回收");

        try { File.Delete(stateFile); } catch { /* 忽略 */ }

        return Report(failures);
    }

    private static void Check(List<string> failures, bool condition, string message)
    {
        Console.WriteLine(condition ? $"  [OK] {message}" : $"  [FAIL] {message}");
        if (!condition)
            failures.Add(message);
    }

    private static int Report(List<string> failures)
    {
        if (failures.Count == 0)
        {
            Console.WriteLine("---- 全部断言通过 ----");
            return 0;
        }

        Console.WriteLine($"---- 失败 {failures.Count} 项 ----");
        return 1;
    }
}