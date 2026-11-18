/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： YunxiaoSettingsServiceUnitTest
*描述：云效连接配置服务（YunxiaoSettingsService，Core 共享版）行为测试：
*      SaveAsync 的 PAT 三分支语义（null 保持 / 空串清除 / 有值覆盖）、
*      规范键落盘、幂等、HasPat 不泄露明文；
*      并以 DI 形状断言钉死 C28（WebHost 可写 CredentialStore 注册）。
*      注意：全程用手写内存假 ICredentialStore，禁止构造真实
*      LubanAgentCore.Security.CredentialStore / UserContextRegistry——
*      后者 GetOrCreate 内部走 ConfigManager.GetDefaultConfigPath 会写真实
*      LocalApplicationData 配置（UserContextRegistry.cs:87-93），造成测试污染。
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class YunxiaoSettingsServiceUnitTest
{
    /// <summary>
    /// 内存假凭据存储：按字面键存取（不做别名归一——那是 CredentialStore 层的事），
    /// null/空白值按键删除语义与 ICredentialStore 契约一致。
    /// </summary>
    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _data = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_data.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(value))
                _data.Remove(key);
            else
                _data[key] = value.Trim();
            return Task.CompletedTask;
        }

        public bool Contains(string key)
            => _data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

        public IReadOnlyCollection<string> Keys => _data.Keys.ToArray();
    }

    private static (FakeCredentialStore Store, YunxiaoApiOptions Options, YunxiaoSettingsService Service) Create()
    {
        var store = new FakeCredentialStore();
        var options = new YunxiaoApiOptions();
        return (store, options, new YunxiaoSettingsService(store, options));
    }

    [TestMethod]
    public async Task SaveAsync_PatNull_KeepsExisting()
    {
        var (store, _, service) = Create();

        await service.SaveAsync("org-1", "old");
        await service.SaveAsync("org-1", pat: null);

        Assert.AreEqual("old", await store.GetAsync(AliyunCredentialKeyAliases.DefaultTokenKey));
    }

    [TestMethod]
    public async Task SaveAsync_PatEmpty_Clears()
    {
        var (store, _, service) = Create();

        await service.SaveAsync("org-1", "old");
        await service.SaveAsync("org-1", pat: "");

        Assert.IsFalse(service.HasPat());
        Assert.IsNull(await store.GetAsync(AliyunCredentialKeyAliases.DefaultTokenKey));
    }

    [TestMethod]
    public async Task SaveAsync_PatValue_Overwrites()
    {
        var (store, _, service) = Create();

        await service.SaveAsync("org-1", "a");
        await service.SaveAsync("org-1", "b");

        Assert.AreEqual("b", await store.GetAsync(AliyunCredentialKeyAliases.DefaultTokenKey));
    }

    [TestMethod]
    public async Task SaveAsync_WritesCanonicalKeys()
    {
        var (store, _, service) = Create();

        await service.SaveAsync("org-1", "tok");

        // YunxiaoSettingsService 直写规范键，假存储按字面键断言
        Assert.IsTrue(store.Contains("connector:yunxiao:token"));
        Assert.IsTrue(store.Contains("connector:yunxiao:organizationid"));
        CollectionAssert.Contains(store.Keys.ToList(), "connector:yunxiao:token");
        CollectionAssert.Contains(store.Keys.ToList(), "connector:yunxiao:organizationid");
        Assert.AreEqual("tok", await store.GetAsync("connector:yunxiao:token"));
        Assert.AreEqual("org-1", await store.GetAsync("connector:yunxiao:organizationid"));
    }

    [TestMethod]
    public async Task HasPat_NeverLeaksPlaintext()
    {
        // 签名钉死：只返回 bool、无出参，明文不可能经返回值泄露
        var method = typeof(YunxiaoSettingsService).GetMethod(nameof(YunxiaoSettingsService.HasPat));
        Assert.IsNotNull(method);
        Assert.AreEqual(typeof(bool), method!.ReturnType);
        Assert.AreEqual(0, method.GetParameters().Length);

        var (_, _, service) = Create();
        await service.SaveAsync("org-1", "super-secret-pat");

        // 行为：只反映「是否已配置」，不返回任何明文
        Assert.IsTrue(service.HasPat());
    }

    [TestMethod]
    public async Task SaveAsync_SetAsyncDoesNotThrow()
    {
        var (_, _, service) = Create();

        // 全程不抛 NotSupportedException（框架默认 ConfigurationCredentialStore.SetAsync 会抛；
        // C28 用可写实现替换后不应再出现）
        await service.SaveAsync("org-1", "pat-1");
        await service.SaveAsync("org-1", "");
        await service.SaveAsync("org-1", "pat-2");
    }

    [TestMethod]
    public async Task SaveAsync_Idempotent()
    {
        var (store, _, service) = Create();

        await service.SaveAsync("org-1", "pat-1");
        await service.SaveAsync("org-1", "pat-1");

        Assert.AreEqual("pat-1", await store.GetAsync(AliyunCredentialKeyAliases.DefaultTokenKey));
        Assert.AreEqual("org-1", await store.GetAsync(AliyunCredentialKeyAliases.DefaultOrganizationIdKey));
        Assert.AreEqual("org-1", service.GetOrganizationId());
        Assert.IsTrue(service.HasPat());
    }

    [TestMethod]
    public void WebHostCredentialStoreRegistration_ResolvesWritableImplementation()
    {
        // 照 LubanAgentWebHost/Program.cs 的 C28 注册代码搭最小容器；
        // 仅断言 DI 形状（解析出的运行时类型），绝不调用 SetAsync/GetAsync、不落盘。
        // CredentialStore 构造需要 UserContextRegistry——这条例外允许直接构造，
        // 其构造函数本身不触盘（GetOrCreate 才会，此处不调用）。
        var services = new DiContainer();
        services.AddSingleton(new UserContextRegistry(new DiContainer().Build(validateScopes: false, validateOnBuild: false)));
        services.AddSingleton<LubanAgentCore.Security.CredentialStore>();
        services.ReplaceSingleton<ICredentialStore>(d => d.GetRequiredService<LubanAgentCore.Security.CredentialStore>());

        using var provider = services.Build(validateScopes: false, validateOnBuild: false);
        var resolved = provider.GetRequiredService<ICredentialStore>();

        Assert.IsInstanceOfType(resolved, typeof(LubanAgentCore.Security.CredentialStore));
        Assert.IsNotInstanceOfType(resolved, typeof(ConfigurationCredentialStore));
    }
}
