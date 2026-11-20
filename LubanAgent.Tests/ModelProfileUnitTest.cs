/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： ModelProfileUnitTest
*描述：模型能力档案单测
*      锁死三件事：内置表的键是纯模型 id（不带供应商前缀）、档位可发、窗口为正；
*      上下文档位派生规则（300K 步长、末档取窗口、窗口未知即无档位）；
*      用户登记优先于内置表，删除登记即恢复内置；内置档案对任意供应商的同名模型生效。
*
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class ModelProfileUnitTest
{
    private string _tempDir = null!;
    private string _configPath = null!;
    private ConfigManager _config = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "luban-model-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        _config = new ConfigManager(_configPath);
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
    public void BuiltinKeys_ArePlainModelIds()
    {
        foreach (var key in ModelProfileDefaults.Keys)
        {
            Assert.IsFalse(key.Contains(':'), $"{key} 不应带供应商前缀：能力属于模型本身");
            Assert.AreEqual(key, key.Trim(), $"{key} 首尾不能有空白");
            Assert.IsFalse(key.Any(char.IsWhiteSpace), $"{key} 中间不能有空白");
        }
    }

    [TestMethod]
    public void BuiltinProfiles_UseSendableEffortsAndPositiveWindow()
    {
        var sendable = new[] { "None", "Low", "Medium", "High", "ExtraHigh" };

        foreach (var key in ModelProfileDefaults.Keys)
        {
            var profile = ModelProfileDefaults.Get(key);
            Assert.IsNotNull(profile, $"{key} 取不到内置档案");

            if (profile!.ContextWindow is { } window)
            {
                Assert.IsTrue(window > 0, $"{key} 的上下文窗口必须为正数");
            }

            foreach (var effort in profile.Reasoning ?? [])
            {
                Assert.IsTrue(
                    Enum.TryParse<ReasoningEffort>(effort, true, out var parsed) && sendable.Contains(parsed.ToString()),
                    $"{key} 的档位 {effort} 不在标准链路可发集合内");
            }
        }
    }

    [TestMethod]
    public void BuiltinProfiles_KeyLookupIsCaseInsensitive()
    {
        var upper = ModelProfileDefaults.Get("O1");
        Assert.IsNotNull(upper, "内置表查找应大小写不敏感");
        Assert.AreEqual(200_000, upper!.ContextWindow);

        // 带供应商前缀也能命中：自定义网关（如 token-hub-internal）同样适用
        Assert.AreEqual(200_000, ModelProfileDefaults.Get("openai:o1")!.ContextWindow);

        Assert.IsNull(ModelProfileDefaults.Get("not-exist"), "未登记的模型应返回 null");
        Assert.IsNull(ModelProfileDefaults.Get(null));
    }

    [TestMethod]
    public void ContextTiers_DeriveByStepAndKeepActualWindowAtTail()
    {
        CollectionAssert.AreEqual(new[] { 128_000 }, ContextTiers.FromWindow(128_000).ToArray(), "低于 300K 只有一个档位");
        CollectionAssert.AreEqual(new[] { 300_000 }, ContextTiers.FromWindow(300_000).ToArray(), "正好 300K 只有一档");
        CollectionAssert.AreEqual(
            new[] { 300_000, 600_000, 900_000, 1_048_576 },
            ContextTiers.FromWindow(1_048_576).ToArray(),
            "末档取实际窗口而不是继续按步长补齐");

        Assert.AreEqual(0, ContextTiers.FromWindow(null).Count, "窗口未知时没有档位");
        Assert.AreEqual(0, ContextTiers.FromWindow(0).Count, "非正数窗口按未知处理");
    }

    [TestMethod]
    public void ContextTiers_DefaultIsFirstTier_AndNormalizeDropsOutOfRange()
    {
        Assert.AreEqual(300_000, ContextTiers.DefaultTier(1_048_576), "达到 300K 的模型默认档固定 300K");
        Assert.AreEqual(128_000, ContextTiers.DefaultTier(128_000), "窗口小于 300K 时默认档即窗口");
        Assert.IsNull(ContextTiers.DefaultTier(null));

        Assert.AreEqual(900_000, ContextTiers.Normalize(1_048_576, 900_000), "合法档位原样返回");
        Assert.IsNull(ContextTiers.Normalize(262_144, 300_000), "超窗档位必须被判非法");
        Assert.IsNull(ContextTiers.Normalize(262_144, 0));
    }

    [TestMethod]
    public void GetModelProfile_UserOverrideWins_AndResetRestoresBuiltin()
    {
        Assert.AreEqual(200_000, _config.GetModelProfile("openai:o1")!.ContextWindow, "未登记时应读到内置值");
        Assert.IsFalse(_config.HasModelProfileOverride("openai:o1"));

        _config.SetModelProfile("openai:o1", new ModelProfile
        {
            Reasoning = ["High"],
            ContextWindow = 400_000
        });

        var user = _config.GetModelProfile("openai:o1")!;
        Assert.AreEqual(400_000, user.ContextWindow, "用户登记应覆盖内置窗口");
        CollectionAssert.AreEqual(new[] { "High" }, user.Reasoning);
        Assert.IsTrue(_config.HasModelProfileOverride("OPENAI:O1"), "覆盖判定应大小写不敏感");

        // 键统一小写落盘
        var json = File.ReadAllText(_configPath);
        StringAssert.Contains(json, "openai:o1");

        _config.SetModelProfile("openai:o1", null);
        Assert.AreEqual(200_000, _config.GetModelProfile("openai:o1")!.ContextWindow, "删除登记后应恢复内置值");
        Assert.IsFalse(_config.HasModelProfileOverride("openai:o1"));
    }

    [TestMethod]
    public void GetModelProfile_UnknownModelReturnsNull_ButExplicitUnsupportedKeepsEmpty()
    {
        Assert.IsNull(_config.GetModelProfile("openai:no-such-model"), "未知模型应返回 null 而不是空档案");

        _config.SetModelProfile("custom:no-such-model", new ModelProfile { Reasoning = [], ContextWindow = 64_000 });
        var stored = _config.GetModelProfile("custom:no-such-model")!;
        Assert.IsNotNull(stored.Reasoning);
        Assert.AreEqual(0, stored.Reasoning!.Count, "明确登记「不支持」必须保留空集合，不能退化成未知");
    }

    [TestMethod]
    public void GetModelProfile_BuiltinAppliesToAnyProvider_ByModelId()
    {
        // 自定义网关上的同名模型：能力按模型 id 命中，与供应商无关
        var profile = _config.GetModelProfile("token-hub-internal:qwen4-max");
        Assert.IsNotNull(profile, "自定义供应商下的已知模型应命中内置档案");
        Assert.AreEqual(1_000_000, profile!.ContextWindow);
        CollectionAssert.AreEqual(new[] { "Low", "High", "ExtraHigh" }, profile.Reasoning!.ToArray());
        Assert.IsFalse(_config.HasModelProfileOverride("token-hub-internal:qwen4-max"), "内置命中不算用户登记");

        // 用户按 provider:model 登记后优先于内置
        _config.SetModelProfile("token-hub-internal:qwen4-max", new ModelProfile
        {
            Reasoning = ["Low"],
            ContextWindow = 300_000
        });

        var user = _config.GetModelProfile("token-hub-internal:qwen4-max")!;
        Assert.AreEqual(300_000, user.ContextWindow, "用户登记应覆盖内置窗口");
        CollectionAssert.AreEqual(new[] { "Low" }, user.Reasoning);

        _config.SetModelProfile("token-hub-internal:qwen4-max", null);
        Assert.AreEqual(1_000_000, _config.GetModelProfile("token-hub-internal:qwen4-max")!.ContextWindow, "删除登记后应恢复内置值");
    }

    [TestMethod]
    public void Defaults_RoundTripAndClear()
    {
        var empty = _config.GetDefaults();
        Assert.IsNull(empty.ReasoningEffort);
        Assert.IsNull(empty.ContextTier);

        _config.SetDefaults(new GenerationDefaults { ReasoningEffort = "High", ContextTier = 600_000 });
        var saved = _config.GetDefaults();
        Assert.AreEqual("High", saved.ReasoningEffort);
        Assert.AreEqual(600_000, saved.ContextTier);

        // 副本语义：外部改动不回写配置对象
        saved.ContextTier = 1;
        Assert.AreEqual(600_000, _config.GetDefaults().ContextTier);

        _config.SetDefaults(null);
        Assert.IsNull(_config.GetDefaults().ReasoningEffort, "传 null 等同全部跟随模型");
        Assert.IsNull(_config.GetDefaults().ContextTier);
    }
}