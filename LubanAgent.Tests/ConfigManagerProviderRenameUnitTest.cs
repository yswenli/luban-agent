/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： ConfigManagerProviderRenameUnitTest
*创建时间：2026/11/11
*描述：Provider 重命名语义单测。
*      锁死「标识改动即重命名」的约束：密钥 / 基址 / 显示名 / 超时 / 自定义模型原样保留，
*      当前选中模型的供应商前缀与刷新模型缓存同步迁移；目标标识被占用或原标识不存在时明确报错且不改动配置。
*
*****************************************************************************/

using LubanAgentCore.Configuration;

namespace LubanAgent.Tests;

[TestClass]
public class ConfigManagerProviderRenameUnitTest
{
    private string _tempDir = null!;
    private string _configPath = null!;
    private ProviderCache _cache = null!;
    private ConfigManager _config = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "luban-provider-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        _cache = new ProviderCache();
        _config = new ConfigManager(_configPath, new ProviderService(_cache));
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

    [TestMethod]
    public void RenameProvider_KeepsSecretsAndMigratesSelectedModel()
    {
        _config.AddProvider("p1", "sk-1", "https://example.test/v1");
        var provider = _config.GetProvider("p1")!;
        provider.DisplayName = "显示名";
        provider.NetworkTimeoutSeconds = 120;
        _config.AddCustomModel("p1", "m-custom");
        _config.SetSelectedModel("p1:m-custom");
        _cache.SetFetched("p1", new List<string> { "m-fetched" });

        var renamed = _config.RenameProvider("p1", "  P2  ");

        Assert.AreEqual("p2", renamed.Name, "名称应统一为去空白后的小写");
        Assert.IsNull(_config.GetProvider("p1"), "原标识不应残留");
        Assert.AreSame(provider, renamed, "应是原地改名，而不是新增一条配置");

        Assert.AreEqual("sk-1", renamed.ApiKey);
        Assert.AreEqual("https://example.test/v1", renamed.BaseUrl);
        Assert.AreEqual("显示名", renamed.DisplayName);
        Assert.AreEqual(120, renamed.NetworkTimeoutSeconds);
        CollectionAssert.AreEqual(new List<string> { "m-custom" }, renamed.CustomModels);
        Assert.AreEqual("p2:m-custom", _config.SelectedModel, "选中模型应跟着换供应商前缀");
        CollectionAssert.AreEqual(
            new List<string> { "m-fetched" },
            _cache.GetFetched("p2").ToList(),
            "刷新得到的模型缓存应跟着改名迁移");

        // 落盘校验：磁盘上只有新标识，且选中模型已迁移
        var json = File.ReadAllText(_configPath);
        StringAssert.Contains(json, "\"p2\"");
        Assert.IsFalse(json.Contains("\"p1\""), "磁盘配置不应残留原标识");
        StringAssert.Contains(json, "p2:m-custom");
    }

    [TestMethod]
    public void RenameProvider_SelectedModelOfAnotherProvider_IsUntouched()
    {
        _config.AddProvider("p1", "sk-1");
        _config.AddProvider("p2", "sk-2");
        _config.SetSelectedModel("p2:m1");

        _config.RenameProvider("p1", "p3");

        Assert.IsNull(_config.GetProvider("p1"));
        Assert.IsNotNull(_config.GetProvider("p3"));
        Assert.AreEqual("p2:m1", _config.SelectedModel, "重命名无关 Provider 不得改动当前选择");
    }

    [TestMethod]
    public void RenameProvider_TargetExists_ThrowsAndKeepsBothProviders()
    {
        _config.AddProvider("p1", "sk-1");
        _config.AddProvider("p2", "sk-2");
        _config.SetSelectedModel("p1:m1");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => _config.RenameProvider("p1", "p2"));
        StringAssert.Contains(ex.Message, "p2");

        Assert.AreEqual("sk-1", _config.GetProvider("p1")!.ApiKey, "冲突时不得覆盖目标 Provider");
        Assert.AreEqual("sk-2", _config.GetProvider("p2")!.ApiKey);
        Assert.AreEqual("p1:m1", _config.SelectedModel);
    }

    [TestMethod]
    public void RenameProvider_UnknownSource_Throws()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => _config.RenameProvider("missing", "p2"));
        StringAssert.Contains(ex.Message, "missing");
    }

    [TestMethod]
    public void RenameProvider_SameName_IsNoopWithoutCreatingDuplicates()
    {
        _config.AddProvider("p1", "sk-1");

        var result = _config.RenameProvider("p1", "P1");

        Assert.AreEqual("p1", result.Name);
        Assert.AreEqual(1, _config.Providers.Count, "同名重命名不得新增一条配置");
    }
}