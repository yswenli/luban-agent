/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： ProviderModelManagementUnitTest
*创建时间：2026/10/27
*描述：Provider / 模型管理服务的删除语义单测。
*      锁死既有缺陷修复：删除的正是当前选中模型所属 Provider 时，改走 ClearSelectedModel，
*      不得再抛 ArgumentException("模型不能为空")（原实现会让命令层只打印红字、丢失成功文案）。
*      同时校验边界：删除别的 Provider 不动当前选择；删除不存在的 Provider 明确报错。
*
*****************************************************************************/

using LuBan.AIAgent.Configuration;
using LubanAgentCore.Configuration;
using LubanAgentCore.Services.Management;

namespace LubanAgent.Tests;

[TestClass]
public class ProviderModelManagementUnitTest
{
    private string _tempDir = null!;
    private string _configPath = null!;
    private ConfigManager _config = null!;
    private ProviderModelManagementService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "luban-provider-mgmt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        _config = new ConfigManager(_configPath);
        _service = new ProviderModelManagementService(_config, new ProviderService(new ProviderCache()));
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
    public async Task DeleteProvider_SelectedModelBelongsToIt_ClearsSelectionWithoutThrowing()
    {
        _config.AddProvider("p1", "sk-1");
        _config.SetSelectedModel("p1:m1");

        // 修复前此处会抛 ArgumentException("模型不能为空")
        await _service.DeleteProviderAsync("p1");

        Assert.IsNull(_config.GetProvider("p1"), "Provider 应已删除");
        Assert.IsNull(_config.SelectedModel, "当前选择应被清空而不是写入空串");
        Assert.IsNull(_service.GetSelection().ProviderId);

        // 落盘校验：磁盘配置里不再有该 Provider，也没有残留的空模型选择
        Assert.IsTrue(File.Exists(_configPath));
        var json = File.ReadAllText(_configPath);
        Assert.IsFalse(json.Contains("\"p1\""), "磁盘配置不应残留已删除的 Provider");
    }

    [TestMethod]
    public async Task DeleteProvider_SelectedModelBelongsToAnother_KeepsSelection()
    {
        _config.AddProvider("p1", "sk-1");
        _config.AddProvider("p2", "sk-2");
        _config.SetSelectedModel("p1:m1");

        await _service.DeleteProviderAsync("p2");

        Assert.IsNull(_config.GetProvider("p2"));
        Assert.AreEqual("p1:m1", _config.SelectedModel, "删除无关 Provider 不得改动当前选择");
    }

    [TestMethod]
    public async Task DeleteProvider_Unknown_ThrowsInvalidOperation()
    {
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _service.DeleteProviderAsync("missing"));

        StringAssert.Contains(ex.Message, "missing");
    }
}