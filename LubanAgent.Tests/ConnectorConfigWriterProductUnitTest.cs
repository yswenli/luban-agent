/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： ConnectorConfigWriterProductUnitTest
*创建时间：2026/10/20
*描述：产品侧 IConnectorConfigWriter 实现（ConfigManager）单测
*      ① 落点为工作区 .luban-agent/connectors/<name>.json；
*      ② 凭据门禁（疑似明文凭据必须走 $credential: 引用）；
*      ③ 文件名防穿越；④ 覆盖语义；⑤ 删除；
*      ⑥ 与用户 config.json 解耦（写入不改动 config.json）。
*
* 说明：框架已去厂商化，本用例一律用中性提供方键；厂商专属契约在
*      LubanAgent.Connectors.AliyunDevOps 下自测。
*
*****************************************************************************/

using System.Text.Json;
using LubanAgentCore.Configuration;
using LuBan.AIAgent.Connectors;

namespace LubanAgent.Tests;

[TestClass]
public class ConnectorConfigWriterProductUnitTest
{
    private const string DemoProvider = "demo";

    private sealed class Workspace : IDisposable
    {
        public string Root { get; }

        public Workspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "luban-product-connector-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string ConfigPath => Path.Combine(Root, "config.json");

        public string ConnectorFile(string name) =>
            Path.Combine(Root, ".luban-agent", "connectors", name + ".json");

        public ConfigManager NewManager() => new(ConfigPath);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    [TestMethod]
    public async Task WriteAsync_LandsInWorkspaceConnectorFile()
    {
        using var ws = new Workspace();

        var result = await ws.NewManager().WriteAsync(
            new ConnectorWorkspaceEntry
            {
                Name = "generated",
                Provider = DemoProvider,
                Settings = new Dictionary<string, string?> { ["endpoint"] = "https://example.test" },
                Enabled = true
            },
            overwrite: true,
            ws.Root);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.AreEqual(ws.ConnectorFile("generated"), result.Data);
        Assert.IsTrue(File.Exists(result.Data));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(result.Data));
        Assert.AreEqual("generated", doc.RootElement.GetProperty("name").GetString());
        Assert.AreEqual(DemoProvider, doc.RootElement.GetProperty("provider").GetString());
        Assert.AreEqual("https://example.test",
            doc.RootElement.GetProperty("settings").GetProperty("endpoint").GetString());
        Assert.IsTrue(doc.RootElement.GetProperty("enabled").GetBoolean());
    }

    [TestMethod]
    public async Task WriteAsync_DoesNotTouchUserConfigJson()
    {
        using var ws = new Workspace();
        const string original = """{ "providers": [], "connectors": [] }""";
        await File.WriteAllTextAsync(ws.ConfigPath, original);

        var result = await ws.NewManager().WriteAsync(
            new ConnectorWorkspaceEntry { Name = "generated", Provider = DemoProvider, Enabled = true },
            overwrite: true,
            ws.Root);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.AreEqual(original, await File.ReadAllTextAsync(ws.ConfigPath),
            "对话生成连接器只落工作区文件，不得改写用户 config.json");
    }

    [TestMethod]
    public async Task WriteAsync_RejectsPlaintextCredential()
    {
        using var ws = new Workspace();

        var result = await ws.NewManager().WriteAsync(
            new ConnectorWorkspaceEntry
            {
                Name = "generated",
                Provider = DemoProvider,
                Settings = new Dictionary<string, string?> { ["personalAccessToken"] = "pat-literal" }
            },
            overwrite: true,
            ws.Root);

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Message!, "凭据引用");
        Assert.IsFalse(File.Exists(ws.ConnectorFile("generated")), "被拒的写入不得留下文件");
    }

    [TestMethod]
    public async Task WriteAsync_AcceptsCredentialReference()
    {
        using var ws = new Workspace();

        var result = await ws.NewManager().WriteAsync(
            new ConnectorWorkspaceEntry
            {
                Name = "generated",
                Provider = DemoProvider,
                Settings = new Dictionary<string, string?>
                {
                    ["endpoint"] = "https://example.test",
                    ["personalAccessToken"] = "$credential:connector:yunxiao:token"
                }
            },
            overwrite: true,
            ws.Root);

        Assert.IsTrue(result.IsSuccess, result.Message);
        var json = await File.ReadAllTextAsync(result.Data!);
        StringAssert.Contains(json, "$credential:connector:yunxiao:token");
    }

    [TestMethod]
    public async Task WriteAsync_RejectsPathTraversalName()
    {
        using var ws = new Workspace();

        var result = await ws.NewManager().WriteAsync(
            new ConnectorWorkspaceEntry { Name = "../evil", Provider = DemoProvider },
            overwrite: true,
            ws.Root);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(File.Exists(Path.Combine(ws.Root, "evil.json")));
    }

    [TestMethod]
    public async Task WriteAsync_RespectsOverwriteFlag()
    {
        using var ws = new Workspace();
        var manager = ws.NewManager();
        var entry = new ConnectorWorkspaceEntry { Name = "generated", Provider = DemoProvider, Enabled = true };

        Assert.IsTrue((await manager.WriteAsync(entry, overwrite: true, ws.Root)).IsSuccess);

        var blocked = await manager.WriteAsync(entry, overwrite: false, ws.Root);
        Assert.IsFalse(blocked.IsSuccess);
        StringAssert.Contains(blocked.Message!, "overwrite");
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesConnectorFile()
    {
        using var ws = new Workspace();
        var manager = ws.NewManager();
        await manager.WriteAsync(
            new ConnectorWorkspaceEntry { Name = "generated", Provider = DemoProvider },
            overwrite: true,
            ws.Root);

        var result = await manager.DeleteAsync("generated", ws.Root);

        Assert.IsTrue(result.IsSuccess, result.Message);
        Assert.IsFalse(File.Exists(ws.ConnectorFile("generated")));

        var missing = await manager.DeleteAsync("generated", ws.Root);
        Assert.IsFalse(missing.IsSuccess);
    }

    [TestMethod]
    public async Task WriteAsync_ThenRegistrySeesEnabledConnector()
    {
        using var ws = new Workspace();
        await ws.NewManager().WriteAsync(
            new ConnectorWorkspaceEntry
            {
                Name = "generated",
                Provider = DemoProvider,
                Settings = new Dictionary<string, string?> { ["endpoint"] = "https://example.test" },
                Enabled = true
            },
            overwrite: true,
            ws.Root);

        // 产品写入的产物必须能被框架默认读取器（WorkspaceConnectorConfigWriter 的读取侧约定）解析回来
        var file = ws.ConnectorFile("generated");
        var entry = JsonSerializer.Deserialize<ConnectorWorkspaceEntry>(
            await File.ReadAllTextAsync(file),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.IsNotNull(entry);
        Assert.AreEqual("generated", entry.ResolveName(string.Empty));
        Assert.AreEqual(DemoProvider, entry.Provider);
        Assert.IsTrue(entry.Enabled);
        Assert.IsFalse(entry.IsEnableOnly);
    }
}