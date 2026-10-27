/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： ProviderModelManagementUnitTest
*创建时间：2026/10/27
*描述：Provider / 模型管理服务的删除语义与快照克隆单测。
*      锁死既有缺陷修复：删除的正是当前选中模型所属 Provider 时，改走 ClearSelectedModel，
*      不得再抛 ArgumentException("模型不能为空")（原实现会让命令层只打印红字、丢失成功文案）。
*      同时校验边界：删除别的 Provider 不动当前选择；删除不存在的 Provider 明确报错。
*      克隆用例按反射枚举全部可写属性，ProviderConfig 新增字段而 Clone 漏搬时会直接失败。
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

    [TestMethod]
    public async Task GetProvidersAsync_CopiesEverySettableProperty()
    {
        _config.AddProvider("p1", "sk-1");
        var original = _config.GetProvider("p1")!;

        // 给每个可写属性填互不相同的非默认值：Clone 漏搬某字段时，克隆对象仍是默认值，断言即失败
        var properties = typeof(ProviderConfig).GetProperties().Where(p => p.CanWrite).ToList();
        foreach (var prop in properties)
        {
            prop.SetValue(original, MakeValue(prop.PropertyType, prop.Name));
        }

        var snapshot = await _service.GetProvidersAsync();
        Assert.AreEqual(1, snapshot.Count);
        var clone = snapshot[0];

        foreach (var prop in properties)
        {
            var expected = prop.GetValue(original);
            var actual = prop.GetValue(clone);
            if (expected is System.Collections.IEnumerable expectedItems and not string)
            {
                var actualItems = actual as System.Collections.IEnumerable;
                Assert.IsNotNull(actualItems, $"{prop.Name} 未被 Clone 复制");
                CollectionAssert.AreEqual(
                    expectedItems.Cast<object>().ToList(),
                    actualItems!.Cast<object>().ToList(),
                    $"{prop.Name} 未被 Clone 复制");
            }
            else
            {
                Assert.AreEqual(expected, actual, $"{prop.Name} 未被 Clone 复制");
            }
        }

        // 集合必须是独立副本，否则调用方改快照会污染配置
        Assert.AreNotSame(original.SupportedModels, clone.SupportedModels);
        Assert.AreNotSame(original.CustomModels, clone.CustomModels);
    }

    private static object? MakeValue(Type type, string name) => type switch
    {
        _ when type == typeof(string) => $"custom-{name}",
        _ when type == typeof(int) => 1234,
        _ when type == typeof(int?) => 4321,
        _ when type == typeof(bool) => true,
        _ when type == typeof(List<string>) => new List<string> { $"custom-{name}-1", $"custom-{name}-2" },
        _ => throw new InvalidOperationException(
            $"ProviderConfig 新增了未覆盖的属性类型 {type.Name}（{name}），请扩展本用例并确认 Clone 已搬运")
    };
}