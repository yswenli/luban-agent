/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： SettingsViewModelUnitTest
*创建时间：2026/10/27
*描述：Task 19 设置窗业务下沉到 ViewModels/Settings 后的行为单测。
*      覆盖：技能/规则/MCP 的必填校验与提示文案、字段修剪、优先级解析、
*            默认 transport 与参数解析、文件来源删除、新建重名避让、
*            Provider upsert 与自定义模型差异同步、删除供应商清理默认模型、
*            A2A 新建服务端/客户端与必填校验、壳 VM 的按页签枚举路由。
*      说明：ViewModel 不持有任何 Avalonia 类型，故可直接构造；管理服务用假实现，
*            Provider 页经真实 ConfigManager（临时配置文件）验证落盘语义。
*
*****************************************************************************/

using LuBan.DI;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.MCP;
using LuBan.AIAgent.Tools.McpManage;
using LubanAgentCore.Configuration;
using LubanAgentCore.Services.Management;
using LubanAgentCore.Services.Management.Models;
using LubanAgentCodex.ViewModels.Settings;
using LubanAgentCodex.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgent.Tests;

[TestClass]
public class SettingsViewModelUnitTest
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "luban-settings-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch
        {
            // 临时目录清理失败不影响断言结果
        }
    }

    // ---------------- 技能 / 规则 ----------------

    [TestMethod]
    public void SaveSkill_EmptyNameOrDescription_ReturnsFailureText()
    {
        var skills = new FakeSkillManagementService();
        var vm = new SkillRuleTabViewModel(Provider(skills, null, null));

        var outcome = vm.SaveSkill(null, new SkillForm("  ", "说明", "", "", ""), null);

        Assert.IsFalse(outcome.Ok);
        Assert.AreEqual("技能的 name 与 description 不能为空。", outcome.Message);
        Assert.AreEqual(0, skills.Saved.Count);
    }

    [TestMethod]
    public void SaveSkill_TrimsFields_AndReturnsNewKey()
    {
        var skills = new FakeSkillManagementService();
        var vm = new SkillRuleTabViewModel(Provider(skills, null, null));

        var outcome = vm.SaveSkill("old", new SkillForm(" MySkill ", " 说明 ", " custom ", " a,b ", "正文"), "C:\\ws");

        Assert.IsTrue(outcome.Ok);
        Assert.AreEqual("MySkill", outcome.Key);

        var saved = skills.Saved.Single();
        Assert.AreEqual("old", saved.OriginalId);
        Assert.AreEqual("C:\\ws", saved.Root);
        Assert.AreEqual("MySkill", saved.Content.Name);
        Assert.AreEqual("说明", saved.Content.Description);
        Assert.AreEqual("custom", saved.Content.Category);
        Assert.AreEqual("a,b", saved.Content.Triggers);
        Assert.AreEqual("正文", saved.Content.Body);
    }

    [TestMethod]
    public void SaveRule_MissingId_ReturnsFailureText()
    {
        var rules = new FakeRuleManagementService();
        var vm = new SkillRuleTabViewModel(Provider(null, rules, null));

        var outcome = vm.SaveRule(null, new RuleForm(" ", "n", "d", "*", "*", "deny", "0", true, ""), null);

        Assert.IsFalse(outcome.Ok);
        Assert.AreEqual("规则的 Id 不能为空。", outcome.Message);
        Assert.AreEqual(0, rules.Saved.Count);
    }

    [TestMethod]
    public void SaveRule_ParsesPriority_AndKeepsBody()
    {
        var rules = new FakeRuleManagementService();
        var vm = new SkillRuleTabViewModel(Provider(null, rules, null));

        var outcome = vm.SaveRule("old", new RuleForm(" r1 ", "n", "d", " * ", " * ", "deny", " 42 ", true, "正文"), null);

        Assert.IsTrue(outcome.Ok);
        Assert.AreEqual("r1", outcome.Key);

        var saved = rules.Saved.Single();
        Assert.AreEqual("old", saved.OriginalId);
        Assert.AreEqual("r1", saved.Config.Id);
        Assert.AreEqual(42, saved.Config.Priority);
        Assert.IsTrue(saved.Config.Enabled);
        Assert.AreEqual("正文", saved.Config.Content);
    }

    // ---------------- MCP ----------------

    [TestMethod]
    public void SaveMcp_BlankTransport_DefaultsToStdio_AndParsesArgs()
    {
        var mcps = new FakeMcpManagementService();
        var vm = new McpTabViewModel(Provider(null, null, mcps));

        var outcome = vm.Save(null, new McpForm(" srv ", "d", "", "node", "a\r\nb", true), null);

        Assert.IsTrue(outcome.Ok);
        Assert.AreEqual("srv", outcome.Key);

        var saved = mcps.Saved.Single().Config;
        Assert.AreEqual("srv", saved.Name);
        Assert.AreEqual("stdio", saved.Transport);
        CollectionAssert.AreEqual(new[] { "a", "b" }, saved.Args);
        Assert.IsTrue(saved.Enabled);
    }

    [TestMethod]
    public async Task Delete_Skill_UsesFileOriginAndScopeRoot()
    {
        var skills = new FakeSkillManagementService();
        var vm = new SkillRuleTabViewModel(Provider(skills, null, null));

        var outcome = await vm.DeleteAsync(SettingsTabKind.Skill, "s1", "C:\\ws");

        Assert.IsTrue(outcome.Ok);
        Assert.AreEqual("已删除该条目。", outcome.Message);

        var deleted = skills.Deleted.Single();
        Assert.AreEqual("s1", deleted.Id);
        Assert.AreEqual(ManagedOrigin.File, deleted.Origin);
        Assert.AreEqual("C:\\ws", deleted.Root);
    }

    [TestMethod]
    public void CreateDefault_Skill_AvoidsExistingNames()
    {
        var skills = new FakeSkillManagementService();
        skills.FileIds.Add("new-skill");
        skills.FileIds.Add("new-skill-1");
        var vm = new SkillRuleTabViewModel(Provider(skills, null, null));

        var outcome = vm.CreateDefault(SettingsTabKind.Skill, null);

        Assert.IsTrue(outcome.Ok);
        Assert.AreEqual("new-skill-2", outcome.Key);
        Assert.AreEqual("已新建条目，请填写内容后点击「保存」。", outcome.Message);
    }

    // ---------------- 供应商 / 模型 ----------------

    [TestMethod]
    public void SaveProvider_MissingApiKey_ReturnsFailureText()
    {
        var vm = NewProviderViewModel(NewConfigManager());

        var outcome = vm.SaveProvider(new ProviderForm("p", " ", "", "", "", Array.Empty<string>()));

        Assert.IsFalse(outcome.Ok);
        Assert.AreEqual("ApiKey 不能为空。", outcome.Message);
    }

    [TestMethod]
    public void SaveProvider_UpsertsProvider_AndSyncsCustomModels()
    {
        var config = NewConfigManager();
        var vm = NewProviderViewModel(config);

        var first = vm.SaveProvider(new ProviderForm(
            " MyProv ", "sk-1", "https://api.example.com", "显示名", "90", new[] { "m1", "m2" }));

        Assert.IsTrue(first.Ok);
        Assert.AreEqual("myprov", first.Key);

        var provider = config.GetProvider("myprov");
        Assert.IsNotNull(provider);
        Assert.AreEqual("显示名", provider!.DisplayName);
        Assert.AreEqual(90, provider.NetworkTimeoutSeconds);
        CollectionAssert.AreEquivalent(new[] { "m1", "m2" }, provider.CustomModels);

        // 二次保存：清空 BaseUrl / DisplayName / 超时，自定义模型按差异增删
        var second = vm.SaveProvider(new ProviderForm(
            "myprov", "sk-2", "", "", "", new[] { "m1", "m3" }));

        Assert.IsTrue(second.Ok);
        provider = config.GetProvider("myprov");
        Assert.IsNotNull(provider);
        Assert.AreEqual("sk-2", provider!.ApiKey);
        Assert.IsNull(provider.BaseUrl);
        Assert.IsNull(provider.DisplayName);
        Assert.IsNull(provider.NetworkTimeoutSeconds);
        CollectionAssert.AreEquivalent(new[] { "m1", "m3" }, provider.CustomModels);
    }

    [TestMethod]
    public void BuildProviderList_MasksApiKey_AndMarksDefault()
    {
        var config = NewConfigManager();
        config.AddProvider("p1", "sk-1234567890abcdef");
        config.SetSelectedModel("p1:m1");
        var vm = NewProviderViewModel(config);

        var item = vm.BuildProviderList().Single();

        Assert.AreEqual("p1", item.Key);
        StringAssert.Contains(item.Display, "✓默认");
        Assert.IsFalse(item.Display.Contains("1234567890abcdef"), "列表不得回显完整 ApiKey");
    }

    [TestMethod]
    public void DeleteProvider_ClearsSelection_WhenDefaultBelongsToProvider()
    {
        var config = NewConfigManager();
        config.AddProvider("p1", "sk");
        config.SetSelectedModel("p1:m1");
        var vm = NewProviderViewModel(config);

        var outcome = vm.DeleteProvider("p1");

        Assert.IsTrue(outcome.Ok);
        Assert.AreEqual("已删除该供应商。", outcome.Message);
        Assert.IsNull(config.GetProvider("p1"));
        Assert.IsNull(config.SelectedModel);
    }

    // ---------------- A2A ----------------

    [TestMethod]
    public void CreateDefault_A2a_CreatesServerThenClientFile()
    {
        var workspace = Path.Combine(_tempDir, "ws");
        Directory.CreateDirectory(workspace);
        var vm = new A2aTabViewModel();

        var server = vm.CreateDefault(workspace);

        Assert.IsTrue(server.Ok);
        Assert.AreEqual("server", server.Key);
        Assert.AreEqual(1, vm.BuildList(workspace).Count);
        StringAssert.Contains(vm.BuildList(workspace)[0].Display, "服务端");

        var client = vm.CreateDefault(workspace);

        Assert.IsTrue(client.Ok);
        Assert.IsNotNull(client.Key);
        Assert.AreNotEqual("server", client.Key);
        Assert.IsTrue(File.Exists(Path.Combine(workspace, ".luban-agent", "a2a", client.Key!)));
        Assert.AreEqual(2, vm.BuildList(workspace).Count);
    }

    [TestMethod]
    public void CreateDefault_A2a_WithoutWorkspace_ReturnsFailureText()
    {
        var outcome = new A2aTabViewModel().CreateDefault(null);

        Assert.IsFalse(outcome.Ok);
        Assert.AreEqual("A2A 配置仅作用于工作区，请先在顶栏选择一个工作区。", outcome.Message);
    }

    [TestMethod]
    public void SaveClient_A2a_RequiresNameAndEndpoint()
    {
        var workspace = Path.Combine(_tempDir, "ws2");
        Directory.CreateDirectory(workspace);
        var vm = new A2aTabViewModel();

        var missingName = vm.SaveClient(workspace, null, new A2aClientForm(" ", "https://x", "", false, "", false));
        Assert.IsFalse(missingName.Ok);
        Assert.AreEqual("客户端的 Name 不能为空。", missingName.Message);

        var missingEndpoint = vm.SaveClient(workspace, null, new A2aClientForm("n", " ", "", false, "", false));
        Assert.IsFalse(missingEndpoint.Ok);
        Assert.AreEqual("客户端的 Endpoint 不能为空。", missingEndpoint.Message);
    }

    // ---------------- 壳 VM ----------------

    [TestMethod]
    public void EnumerateFileItems_RoutesByTab()
    {
        var skills = new FakeSkillManagementService();
        skills.FileIds.Add("a");
        skills.FileIds.Add("b");
        var vm = new SettingsWindowViewModel(Provider(skills, null, null));

        vm.Tab = SettingsTabKind.Skill;
        CollectionAssert.AreEqual(new[] { "a", "b" }, vm.EnumerateFileItems().ToArray());

        vm.Tab = SettingsTabKind.Provider;
        Assert.AreEqual(0, vm.EnumerateFileItems().Count);
    }

    // ---------------- 辅助 ----------------

    private ConfigManager NewConfigManager()
        => new(Path.Combine(_tempDir, "config.json"));

    private ProviderModelTabViewModel NewProviderViewModel(ConfigManager config)
    {
        var services = new DiContainer()
            .AddSingleton(config)
            .Build(validateScopes: false, validateOnBuild: false);
        return new ProviderModelTabViewModel(services);
    }

    private static DiContainer Provider(
        ISkillManagementService? skills,
        IRuleManagementService? rules,
        IMcpManagementService? mcps)
    {
        var services = new DiContainer();
        if (skills != null) services.AddSingleton<ISkillManagementService>(skills);
        if (rules != null) services.AddSingleton<IRuleManagementService>(rules);
        if (mcps != null) services.AddSingleton<IMcpManagementService>(mcps);
        return services.Build(validateScopes: false, validateOnBuild: false);
    }

    private sealed class FakeSkillManagementService : ISkillManagementService
    {
        public List<string> FileIds { get; } = new();

        public List<(string? OriginalId, SkillFileContent Content, string? Root)> Saved { get; } = new();

        public List<(string Id, ManagedOrigin Origin, string? Root)> Deleted { get; } = new();

        public Task<IReadOnlyList<SkillListItem>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SkillListItem>>(Array.Empty<SkillListItem>());

        public Task AddAsync(CustomSkillConfig skill, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(CustomSkillConfig skill, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(string id, ManagedOrigin origin, string? workspaceRoot = null, CancellationToken ct = default)
        {
            Deleted.Add((id, origin, workspaceRoot));
            return Task.CompletedTask;
        }

        public Task SetEnabledAsync(string id, bool enabled, CancellationToken ct = default) => Task.CompletedTask;

        public IReadOnlyList<string> ListFileIds(string? workspaceRoot) => FileIds;

        public SkillFileContent? ReadFile(string id, string? workspaceRoot) => null;

        public void SaveFile(string? originalId, SkillFileContent content, string? workspaceRoot)
            => Saved.Add((originalId, content, workspaceRoot));
    }

    private sealed class FakeRuleManagementService : IRuleManagementService
    {
        public List<(string? OriginalId, CustomRuleConfig Config, string? Root)> Saved { get; } = new();

        public Task<IReadOnlyList<RuleInfo>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RuleInfo>>(Array.Empty<RuleInfo>());

        public Task AddAsync(CustomRuleConfig rule, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(CustomRuleConfig rule, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(string id, ManagedOrigin origin, string? workspaceRoot = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetEnabledAsync(string id, bool enabled, CancellationToken ct = default) => Task.CompletedTask;

        public IReadOnlyList<string> ListFileIds(string? workspaceRoot) => Array.Empty<string>();

        public CustomRuleConfig? ReadFile(string id, string? workspaceRoot) => null;

        public void SaveFile(string? originalId, CustomRuleConfig rule, string? workspaceRoot)
            => Saved.Add((originalId, rule, workspaceRoot));
    }

    private sealed class FakeMcpManagementService : IMcpManagementService
    {
        public List<(string? OriginalId, McpServerConfig Config, string? Root)> Saved { get; } = new();

        public Task<IReadOnlyList<McpInfo>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpInfo>>(Array.Empty<McpInfo>());

        public Task AddAsync(McpServerConfig config, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(McpServerConfig config, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(string name, ManagedOrigin origin, string? workspaceRoot = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> ConnectAsync(string name, CancellationToken ct = default) => Task.FromResult(false);

        public Task<IReadOnlyList<MCPTool>> ListToolsAsync(string name, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MCPTool>>(Array.Empty<MCPTool>());

        public Task<McpTestResult> TestAsync(string name, CancellationToken ct = default)
            => Task.FromResult(new McpTestResult());

        public IReadOnlyList<string> ListFileIds(string? workspaceRoot) => Array.Empty<string>();

        public McpServerConfig? ReadFile(string id, string? workspaceRoot) => null;

        public void SaveFile(string? originalId, McpServerConfig config, string? workspaceRoot)
            => Saved.Add((originalId, config, workspaceRoot));
    }
}