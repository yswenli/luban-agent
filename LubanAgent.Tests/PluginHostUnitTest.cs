/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： PluginHostUnitTest
*创建时间：2026/10/27
*描述：Task 18 插件宿主下沉到 Core 后的行为单测。
*      覆盖：侧栏条目的登记/覆盖/移除与变更通知、缺 id 忽略；
*            面板工厂覆盖时丢弃旧实例、注销时的「同步解绑 → OnUnloading → 移除」顺序、
*            未实例化面板不触发解绑；UI 线程封送接缝；命令登记；无活动会话时 MCP 注册不崩。
*      说明：IPluginManagementService 无法在本处单测（PluginManager 为 sealed 具体类，未抽缝），
*            其覆盖依赖本类 + 手动冒烟。
*
*****************************************************************************/

using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Hosting;
using LuBan.AIAgent.Plugins;
using LubanAgentCore.Plugins;
using LubanAgentCore.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LubanAgent.Tests;

[TestClass]
public class PluginHostUnitTest
{
    private ServiceProvider _root = null!;
    private SessionRuntimeManager _runtimes = null!;
    private RecordingDispatcher _ui = null!;
    private AgentPluginHost _host = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder().Build();
        _runtimes = new SessionRuntimeManager(_root, configuration);
        _ui = new RecordingDispatcher();
        _host = new AgentPluginHost(
            new PluginHostRegistry(),
            new PluginHostContext(_root, new UserContextRegistry(_root), _runtimes),
            configuration,
            Options.Create(new LuBanAgentOptions()),
            _ui);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _runtimes.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _root.Dispose();
    }

    // ---------------- 侧栏条目 ----------------

    [TestMethod]
    public void SidebarEntry_Register_RaisesChangedAndIsListed()
    {
        var changed = 0;
        _host.SidebarEntriesChanged += (_, _) => changed++;

        _host.RegisterSidebarEntry(new PluginSidebarEntry { Id = "todo", Label = "待办" });

        Assert.AreEqual(1, changed);
        Assert.AreEqual(1, _ui.PostCount, "通知应经封送接缝投递");

        var entries = _host.GetSidebarEntries();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("todo", entries[0].Id);
        Assert.AreEqual("待办", entries[0].Label);
    }

    [TestMethod]
    public void SidebarEntry_RegisterSameId_OverwritesWithoutDuplicating()
    {
        _host.RegisterSidebarEntry(new PluginSidebarEntry { Id = "todo", Label = "旧" });
        _host.RegisterSidebarEntry(new PluginSidebarEntry { Id = "todo", Label = "新" });

        var entries = _host.GetSidebarEntries();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("新", entries[0].Label);
    }

    [TestMethod]
    public void SidebarEntry_Unregister_OnlyNotifiesWhenActuallyRemoved()
    {
        _host.RegisterSidebarEntry(new PluginSidebarEntry { Id = "todo" });

        var changed = 0;
        _host.SidebarEntriesChanged += (_, _) => changed++;

        _host.UnregisterSidebarEntry("todo");
        Assert.AreEqual(1, changed);
        Assert.AreEqual(0, _host.GetSidebarEntries().Count);

        // 不存在 / 空 id：不再发通知
        _host.UnregisterSidebarEntry("todo");
        _host.UnregisterSidebarEntry("  ");
        Assert.AreEqual(1, changed);
    }

    [TestMethod]
    public void SidebarEntry_MissingId_IsIgnored()
    {
        var changed = 0;
        _host.SidebarEntriesChanged += (_, _) => changed++;

        _host.RegisterSidebarEntry(new PluginSidebarEntry { Id = "   " });

        Assert.AreEqual(0, changed);
        Assert.AreEqual(0, _ui.PostCount);
        Assert.AreEqual(0, _host.GetSidebarEntries().Count);
    }

    // ---------------- 面板 ----------------

    [TestMethod]
    public void Panel_RegisterOverwrite_DropsCachedInstance()
    {
        var first = new FakePanel();
        _host.RegisterPanel("p", () => first);
        Assert.AreSame(first, _host.GetPanel("p"));

        var second = new FakePanel();
        _host.RegisterPanel("p", () => second);

        Assert.AreSame(second, _host.GetPanel("p"), "工厂被替换后不得再返回旧实例（旧实例会钉住插件 ALC）");
    }

    [TestMethod]
    public void Panel_Unregister_DetachesThenUnloadsThenRemoves()
    {
        var order = new List<string>();
        var panel = new FakePanel { OnUnloadingCallback = () => order.Add("unload") };

        _host.RegisterPanel("p", () => panel);
        Assert.AreSame(panel, _host.GetPanel("p"));

        _host.PanelDetaching += (entryId, detaching) =>
            order.Add($"detach:{entryId}:{ReferenceEquals(detaching, panel)}");

        _host.UnregisterPanel("p");

        CollectionAssert.AreEqual(new[] { "detach:p:True", "unload" }, order);
        Assert.AreEqual(1, _ui.InvokeCount, "注销必须走同步封送，返回后框架立即进入 ALC 卸载");
        Assert.IsNull(_host.GetPanel("p"));
    }

    [TestMethod]
    public void Panel_UnregisterWithoutInstance_DoesNotDetach()
    {
        _host.RegisterPanel("p", () => new FakePanel());

        var detached = 0;
        _host.PanelDetaching += (_, _) => detached++;

        _host.UnregisterPanel("p");

        Assert.AreEqual(0, detached);
        Assert.AreEqual(1, _ui.InvokeCount, "无实例时仍走同步封送，但回调内不解绑");
        Assert.IsNull(_host.GetPanel("p"));
    }

    [TestMethod]
    public void Panel_RegisterMissingIdOrFactory_IsIgnored()
    {
        _host.RegisterPanel("  ", () => new FakePanel());
        _host.RegisterPanel("p", null!);

        Assert.IsNull(_host.GetPanel("  "));
        Assert.IsNull(_host.GetPanel("p"));
    }

    // ---------------- UI 线程封送 ----------------

    [TestMethod]
    public void PostToUi_UsesDispatcherWhenOffUiThread()
    {
        var ran = 0;
        _host.PostToUi(() => ran++);

        Assert.AreEqual(1, ran);
        Assert.AreEqual(1, _ui.PostCount);
    }

    [TestMethod]
    public void PostToUi_RunsInlineOnUiThread()
    {
        _ui.IsUiThread = true;

        var ran = 0;
        _host.PostToUi(() => ran++);

        Assert.AreEqual(1, ran);
        Assert.AreEqual(0, _ui.PostCount);
    }

    // ---------------- 命令 / MCP / 技能 ----------------

    [TestMethod]
    public void Command_Register_ListsAndIgnoresMissingName()
    {
        _host.RegisterCommand(new PluginCommandDescriptor { Name = "do", Description = "执行" });
        _host.RegisterCommand(new PluginCommandDescriptor { Name = " " });

        var commands = _host.GetCommands();
        Assert.AreEqual(1, commands.Count);
        Assert.AreEqual("do", commands[0].Name);
    }

    [TestMethod]
    public void Mcp_RegisterWithoutActiveSession_DoesNotThrow()
    {
        _host.RegisterMcpServer("yunxiao");

        Assert.AreEqual(0, _ui.PostCount);
    }

    // 说明：技能路径（RegisterSkill / UnregisterSkill）依赖工作区管理器（落库），
    // 属集成路径，不在此处的无 DB 单测范围内；其覆盖依赖手动冒烟。

    // ---------------- 测试替身 ----------------

    private sealed class RecordingDispatcher : IPluginUiDispatcher
    {
        public bool IsUiThread { get; set; }

        public int PostCount { get; private set; }

        public int InvokeCount { get; private set; }

        public void Post(Action action)
        {
            PostCount++;
            action();
        }

        public void Invoke(Action action)
        {
            InvokeCount++;
            action();
        }
    }

    private sealed class FakePanel : IPluginPanel
    {
        public string Title => "假面板";

        public Action? OnUnloadingCallback { get; set; }

        public void OnUnloading() => OnUnloadingCallback?.Invoke();
    }
}