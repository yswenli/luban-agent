/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： McpConnectorManagementUnitTest
*创建时间：2026/10/27
*描述：Task 16 搬迁到 Core 的两个管理服务单测。
*      ① MCP「文件型」后端：mcps/<name>.json 往返不丢字段、改名即删旧文件、
*        名称排序、路径穿越拒绝；
*      ② 连接器探测（TestAsync）的三条早退分支：未声明 / 提供方未注册 / 凭据缺失。
*      只测不触碰注册表与实例宿主的分支，故相关依赖传 null 或空实现。
*
*****************************************************************************/

using LuBan.DI;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Connectors;
using LubanAgentCore.Services;
using LubanAgentCore.Services.Management;
using LubanAgentCore.Services.Management.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace LubanAgent.Tests;

[TestClass]
public class McpConnectorManagementUnitTest
{
    private string _workspaceRoot = "";

    [TestInitialize]
    public void Setup()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), "luban-mcpconn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspaceRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_workspaceRoot))
                Directory.Delete(_workspaceRoot, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结论
        }
    }

    // ---------------- MCP 文件型后端 ----------------

    [TestMethod]
    public void Mcp_SaveFile_ReadFile_RoundTripsAllEditableFields()
    {
        var svc = NewMcpService();

        svc.SaveFile(null, new McpServerConfig
        {
            Name = "demo",
            Description = "示例服务",
            Transport = "http",
            Command = "npx",
            Args = new List<string> { "-y", "demo-server" },
            Url = "https://example.invalid/mcp",
            Enabled = false,
        }, _workspaceRoot);

        var cfg = svc.ReadFile("demo", _workspaceRoot);

        Assert.IsNotNull(cfg);
        Assert.AreEqual("demo", cfg.Name);
        Assert.AreEqual("示例服务", cfg.Description);
        Assert.AreEqual("http", cfg.Transport);
        Assert.AreEqual("npx", cfg.Command);
        CollectionAssert.AreEqual(new[] { "-y", "demo-server" }, cfg.Args);
        Assert.AreEqual("https://example.invalid/mcp", cfg.Url);
        Assert.IsFalse(cfg.Enabled);
    }

    [TestMethod]
    public void Mcp_SaveFile_Rename_DeletesOldFile()
    {
        var svc = NewMcpService();
        svc.SaveFile(null, new McpServerConfig { Name = "old-name" }, _workspaceRoot);

        svc.SaveFile("old-name", new McpServerConfig { Name = "new-name" }, _workspaceRoot);

        var mcpsDir = Path.Combine(_workspaceRoot, ".luban-agent", "mcps");
        Assert.IsFalse(File.Exists(Path.Combine(mcpsDir, "old-name.json")), "改名后旧文件必须消失");
        Assert.IsTrue(File.Exists(Path.Combine(mcpsDir, "new-name.json")));
        Assert.IsNull(svc.ReadFile("old-name", _workspaceRoot));
    }

    [TestMethod]
    public void Mcp_ListFileIds_ReturnsSortedIds()
    {
        var svc = NewMcpService();
        svc.SaveFile(null, new McpServerConfig { Name = "zeta" }, _workspaceRoot);
        svc.SaveFile(null, new McpServerConfig { Name = "Alpha" }, _workspaceRoot);

        var ids = svc.ListFileIds(_workspaceRoot);

        CollectionAssert.AreEqual(new[] { "Alpha", "zeta" }, ids.ToArray());
    }

    [TestMethod]
    public void Mcp_DeleteAsync_File_RemovesFile()
    {
        var svc = NewMcpService();
        svc.SaveFile(null, new McpServerConfig { Name = "gone" }, _workspaceRoot);

        svc.DeleteAsync("gone", ManagedOrigin.File, _workspaceRoot).GetAwaiter().GetResult();

        Assert.IsNull(svc.ReadFile("gone", _workspaceRoot));
        Assert.IsFalse(File.Exists(Path.Combine(_workspaceRoot, ".luban-agent", "mcps", "gone.json")));
    }

    [TestMethod]
    public void Mcp_SaveFile_RejectsPathTraversalName()
    {
        var svc = NewMcpService();

        Assert.ThrowsExactly<ArgumentException>(
            () => svc.SaveFile(null, new McpServerConfig { Name = "..\\evil" }, _workspaceRoot));
    }

    [TestMethod]
    public void Mcp_SaveFile_RejectsEmptyName()
    {
        var svc = NewMcpService();

        Assert.ThrowsExactly<ArgumentException>(
            () => svc.SaveFile(null, new McpServerConfig { Name = "   " }, _workspaceRoot));
    }

    // ---------------- 连接器探测（TestAsync 早退分支） ----------------

    [TestMethod]
    public void Connector_TestAsync_ReturnsFailure_WhenNotDeclared()
    {
        var svc = NewConnectorService(new FakeConfigReader(), new ConnectorFactoryRegistry(Array.Empty<ConnectorFactoryRegistration>()));

        var result = svc.TestAsync("missing").GetAwaiter().GetResult();

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.McpServerCount);
        StringAssert.Contains(result.Message, "未找到连接器");
    }

    [TestMethod]
    public void Connector_TestAsync_ReturnsFailure_WhenProviderNotRegistered()
    {
        var reader = new FakeConfigReader();
        reader.Connectors.Add(new ConnectorConfig { Name = "demo", Provider = "unknown", Enabled = true });

        var svc = NewConnectorService(reader, new ConnectorFactoryRegistry(Array.Empty<ConnectorFactoryRegistration>()));

        var result = svc.TestAsync("demo").GetAwaiter().GetResult();

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "未注册");
    }

    [TestMethod]
    public void Connector_TestAsync_ReturnsFailure_WhenCredentialMissing()
    {
        var reader = new FakeConfigReader();
        reader.Connectors.Add(new ConnectorConfig
        {
            Name = "demo",
            Provider = "demo",
            Enabled = true,
            Settings = new Dictionary<string, string?> { ["token"] = "$credential:missing-token" },
        });

        var factories = new ConnectorFactoryRegistry(new[]
        {
            new ConnectorFactoryRegistration("demo", _ => throw new NotSupportedException()),
        });

        // credentials 传 null：无法解析任何凭据引用 → 报缺少凭据
        var svc = NewConnectorService(reader, factories);

        var result = svc.TestAsync("demo").GetAwaiter().GetResult();

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "缺少凭据");
        StringAssert.Contains(result.Message, "missing-token");
    }

    // ---------------- 构造（只测不触碰注册表 / 实例宿主的分支） ----------------

    private static McpManagementService NewMcpService()
        => new(
            config: null!,
            registry: null!,
            workspaces: null!,
            frameworkClients: Array.Empty<LuBan.AIAgent.MCP.IMCPClient>(),
            resolver: null!,
            mcpOptions: Options.Create(new McpOptions()));

    private ConnectorManagementService NewConnectorService(IAppConfigReader reader, ConnectorFactoryRegistry factories)
    {
        var host = new ConnectorInstanceHost(new DiContainer(new EmptyServiceProvider()), factories);
        var registry = new ConnectorRegistry(host, factories, mcpRegistry: null, configReader: reader, configuration: null);

        return new ConnectorManagementService(
            registry: registry,
            factories: factories,
            host: host,
            writer: null!,
            workspaces: new FakeWorkspaceManager(),
            configReader: reader,
            configuration: new ConfigurationBuilder().Build(),
            reloadTrigger: null,
            credentials: null);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FakeConfigReader : IAppConfigReader
    {
        public List<ProviderConfigData> Providers { get; } = new();
        public string? SelectedModel => null;
        public List<CustomSkillConfig> CustomSkills { get; } = new();
        public List<CustomRuleConfig> CustomRules { get; } = new();
        public List<McpServerConfig> McpServers { get; } = new();
        public List<ConnectorConfig> Connectors { get; } = new();
        public List<string> DisabledBuiltinSkills { get; } = new();
        public List<string> DisabledBuiltinRules { get; } = new();
        public List<string> DisabledBuiltinMcpClients { get; } = new();
    }

    /// <summary>只有 <see cref="CurrentWorkspace"/> 被探测分支使用，其余成员一律不实现。</summary>
    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public WorkspaceInfo? CurrentWorkspace => null;

        public event Action<WorkspaceInfo>? CurrentWorkspaceChanged { add { } remove { } }

        public Task<WorkspaceInfo> CreateWorkspaceAsync(string rootPath, string? name = null, string type = "Normal")
            => throw new NotSupportedException();

        public Task SwitchWorkspaceAsync(string workspaceId) => throw new NotSupportedException();

        public Task<IEnumerable<WorkspaceInfo>> GetUserWorkspacesAsync() => throw new NotSupportedException();

        public Task<WorkspaceInfo?> GetWorkspaceAsync(string workspaceId) => throw new NotSupportedException();

        public Task<bool> EnsureAuthorizedAsync(WorkspaceInfo workspace) => throw new NotSupportedException();

        public Task EnsureConfigDirectoryAsync(WorkspaceInfo workspace) => throw new NotSupportedException();

        public Task SetCurrentAsync(string workspaceId) => throw new NotSupportedException();

        public Task<bool> DeleteWorkspaceAsync(string workspaceId) => throw new NotSupportedException();

        public Task<bool> RenameWorkspaceAsync(string workspaceId, string name) => throw new NotSupportedException();

        public string? GetKnownRootPath(string? workspaceId) => null;

        public bool IsWithinWorkspace(string path) => false;
    }
}