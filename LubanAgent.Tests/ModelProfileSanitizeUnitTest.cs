/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： ModelProfileSanitizeUnitTest
*描述：模型能力档案写入前收敛单测
*      锁死三条：标准链路发不出的档位（minimal / max）与非法名字一律丢弃；
*      非正数窗口按未知处理；全空的用户档案等同删除登记。
*
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class ModelProfileSanitizeUnitTest
{
    private string _tempDir = null!;
    private ConfigManager _config = null!;
    private ProviderModelManagementService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "luban-profile-sanitize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _config = new ConfigManager(Path.Combine(_tempDir, "config.json"));
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
    public async Task SetModelProfile_DropsUnsendableAndKeepsSendableInEnumOrder()
    {
        await _service.SetModelProfileAsync("p1", "m1", new ModelProfile
        {
            Reasoning = ["max", "high", "minimal", "ExtraHigh", "not-an-effort", "low"],
            ContextWindow = 300_000
        });

        var profile = await _service.GetModelProfileAsync("p1", "m1");
        Assert.IsNotNull(profile);
        CollectionAssert.AreEqual(
            new[] { "Low", "High", "ExtraHigh" },
            profile!.Reasoning!.ToArray(),
            "只保留标准链路可发的档位，统一为枚举名");
        Assert.AreEqual(300_000, profile.ContextWindow);
    }

    [TestMethod]
    public async Task SetModelProfile_NonPositiveWindowBecomesUnknown()
    {
        await _service.SetModelProfileAsync("p1", "m1", new ModelProfile
        {
            Reasoning = ["High"],
            ContextWindow = -1
        });

        var profile = await _service.GetModelProfileAsync("p1", "m1");
        Assert.IsNotNull(profile);
        Assert.IsNull(profile!.ContextWindow, "非正数窗口按未知处理");
    }

    [TestMethod]
    public async Task SetModelProfile_AllEmptyDeletesUserOverride()
    {
        await _service.SetModelProfileAsync("openai", "o1", new ModelProfile { Reasoning = null, ContextWindow = null });

        Assert.IsFalse(_config.HasModelProfileOverride("openai:o1"), "全空档案不应留下用户登记");
        Assert.AreEqual(200_000, (await _service.GetModelProfileAsync("openai", "o1"))!.ContextWindow, "应回落到内置值");
    }

    [TestMethod]
    public async Task SetModelProfile_EmptyEffortListStaysExplicitUnsupported()
    {
        await _service.SetModelProfileAsync("p1", "m1", new ModelProfile { Reasoning = [], ContextWindow = 64_000 });

        var profile = await _service.GetModelProfileAsync("p1", "m1");
        Assert.IsNotNull(profile!.Reasoning, "空集合表示明确不支持，不能变成未知");
        Assert.AreEqual(0, profile.Reasoning!.Count);
    }

    [TestMethod]
    public async Task SetModelProfile_NullDeletesOverride_AndRestoresBuiltin()
    {
        await _service.SetModelProfileAsync("glm", "glm-5.2", new ModelProfile { Reasoning = ["High"] });
        Assert.IsTrue(_config.HasModelProfileOverride("glm:glm-5.2"));

        await _service.SetModelProfileAsync("glm", "glm-5.2", null);

        Assert.IsFalse(_config.HasModelProfileOverride("glm:glm-5.2"));
        var builtin = await _service.GetModelProfileAsync("glm", "glm-5.2");
        CollectionAssert.AreEqual(
            new[] { "None", "High", "ExtraHigh" },
            builtin!.Reasoning!.ToArray(),
            "删除登记后应回到内置档位");
    }
}