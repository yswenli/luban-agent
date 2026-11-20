/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebYunxiaoServiceUnitTest
*描述：云效工作台业务编排（YunxiaoWorkbenchService）单测：只读探活口径（临时 PAT、不落配置）、
*      先校验后保存（D7：探活失败零落盘）、格式/组织地址校验、分页默认与截断、不泄露 PAT 明文。
*      说明：壳层 WebYunxiaoService 依赖静态 WebApi.Runtime（起不来），只有转发，不单测。
*      全程用手写假 IProjexClient / IYunxiaoSettingsService / IYunxiaoCredentialProvider，
*      禁止碰真实 HttpClient 与凭据存储。
*
*****************************************************************************/

namespace LubanAgent.Tests;

using LubanAgentWebHost.Models;
using LubanAgentWebHost.Services;
using LubanAgentWebHost.Services.Init;

/// <summary>
/// <see cref="YunxiaoWorkbenchService"/> 行为单测：探活分类、保存时序、校验语义（400/409）、分页透传与脱敏。
/// </summary>
[TestClass]
public class WebYunxiaoServiceUnitTest
{
    // ---------------- 假实现 ----------------

    /// <summary>假 Projex 客户端：记录每次调用参数（含探活绑定的 PAT），可编排返回值 / 抛错。</summary>
    private sealed class FakeProjexClient : IProjexClient
    {
        /// <summary>一次搜索调用快照；<c>Pat</c> 为 null 表示走「已存凭据」通道（SearchProjectsAsync）。</summary>
        public sealed record SearchCall(string OrganizationId, int Page, int PerPage, string? Keyword, string? Pat);

        public List<SearchCall> Calls { get; } = new();

        public YunxiaoPagedResult<YunxiaoProject>? Result { get; set; } =
            new() { Items = new List<YunxiaoProject>(), Total = 0, Page = 1, PerPage = 1 };

        public Exception? Throws { get; set; }

        public Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsAsync(
            string organizationId, int page, int perPage, string? keyword,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new SearchCall(organizationId, page, perPage, keyword, null));
            var ex = Throws;
            if (ex is not null)
            {
                throw ex;
            }

            return Task.FromResult(Result);
        }

        public Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsWithPatAsync(
            string organizationId, int page, int perPage, string? keyword, string pat,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new SearchCall(organizationId, page, perPage, keyword, pat));
            var ex = Throws;
            if (ex is not null)
            {
                throw ex;
            }

            return Task.FromResult(Result);
        }

        public Task<YunxiaoProject?> GetProjectAsync(
            string organizationId, string projectId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<YunxiaoProject?>(null);

        // S3 新增能力不在本类测试范围内：一律显式抛出，避免误用为空实现
        public Task<YunxiaoPagedResult<YunxiaoWorkitem>?> SearchWorkitemsAsync(
            string organizationId, YunxiaoWorkitemSearchCriteria criteria,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<YunxiaoWorkitem?> GetWorkitemAsync(
            string organizationId, string workitemId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string?> CreateWorkitemAsync(
            string organizationId, YunxiaoCreateWorkitemRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<YunxiaoWorkitemType>> ListWorkitemTypesAsync(
            string organizationId, string projectId, string category,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<YunxiaoWorkitemAttachment>> ListWorkitemAttachmentsAsync(
            string organizationId, string workitemId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string?> CreateSprintAsync(
            string organizationId, string projectId, string name, IReadOnlyList<string> owners,
            string? startDate, string? endDate, string? description, int? capacityHours,
            string? operatorId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<YunxiaoCurrentUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string?> CreateWorkitemRelationAsync(
            string organizationId, string workitemId, string relationType, string targetWorkitemId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<YunxiaoTestRepo>> ListTestReposAsync(
            string organizationId, int page, int perPage, string? name,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string?> CreateTestcaseAsync(
            string organizationId, YunxiaoCreateTestcaseRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string?> CreateTestPlanAsync(
            string organizationId, YunxiaoCreateTestPlanRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>假设置服务：记录 SaveAsync 调用次数与参数（调用计数用于断言「探活失败零落盘」）。</summary>
    private sealed class FakeSettingsService : IYunxiaoSettingsService
    {
        public string OrganizationIdValue { get; set; } = string.Empty;

        public bool HasPatValue { get; set; }

        public int SaveCallCount { get; private set; }

        public (string? OrganizationId, string? Pat)? LastSave { get; private set; }

        public string GetOrganizationId() => OrganizationIdValue;

        public bool HasPat() => HasPatValue;

        public Task SaveAsync(
            string? organizationId, string? pat = null, CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            LastSave = (organizationId, pat);
            return Task.CompletedTask;
        }

        public void ApplyPersistedOrganizationId()
        {
        }
    }

    /// <summary>假凭据提供者：返回预置 PAT 并计数（断言「pat 缺省才取已存 PAT」）。</summary>
    private sealed class FakeCredentialProvider : IYunxiaoCredentialProvider
    {
        public string? StoredPat { get; set; }

        public int GetCallCount { get; private set; }

        public Task<string?> GetPatAsync(CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            return Task.FromResult(StoredPat);
        }
    }

    private static (FakeSettingsService Settings, FakeProjexClient Projects, FakeCredentialProvider Credentials, YunxiaoWorkbenchService Service) Create()
    {
        var settings = new FakeSettingsService();
        var projects = new FakeProjexClient();
        var credentials = new FakeCredentialProvider();
        return (settings, projects, credentials, new YunxiaoWorkbenchService(settings, projects, credentials));
    }

    // ---------------- 初始化完成登记 → 项目列表「已初始化」 ----------------

    /// <summary>登记存储：同项目二次登记覆盖而非追加，且缺键时保留既有 ID/标识。</summary>
    [TestMethod]
    public async Task 初始化登记_同项目覆盖且保留键()
    {
        var temp = Path.Combine(Path.GetTempPath(), "luban-init-store-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ProjectInitStore(temp);
            await store.UpsertAsync(new ProjectInitRecord { ProjectId = "pid-1", Identifier = "BBKY", Codename = "BBKY" });
            await store.UpsertAsync(new ProjectInitRecord { ProjectId = "pid-1", Codename = "BBKY2" });

            var all = await store.GetAllAsync();

            Assert.AreEqual(1, all.Count, "同项目未覆盖时不会新增第二条");
            Assert.AreEqual("BBKY2", all[0].Codename);
            Assert.AreEqual("BBKY", all[0].Identifier, "新登记缺标识时保留既有标识");
            Assert.AreEqual("pid-1", all[0].ProjectId);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>项目列表按登记标注：项目 ID 命中、标识命中均为已初始化，未命中为待初始化。</summary>
    [TestMethod]
    public async Task 项目列表_按初始化登记标注已初始化()
    {
        var (settings, projects, credentials, _) = Create();
        var temp = Path.Combine(Path.GetTempPath(), "luban-init-store-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ProjectInitStore(temp);
            await store.UpsertAsync(new ProjectInitRecord
            {
                ProjectId = "pid-1",
                Identifier = "BBKY",
                Codename = "BBKY",
                RepoName = "BBKY-Mixed",
                CompletedAt = DateTime.Now,
            });

            var service = new YunxiaoWorkbenchService(settings, projects, credentials, store);
            projects.Result = new YunxiaoPagedResult<YunxiaoProject>
            {
                Items = new List<YunxiaoProject>
                {
                    new() { Id = "pid-1", Name = "按 ID 命中", CustomCode = "OTHER" },
                    new() { Id = "pid-2", Name = "按标识命中", CustomCode = "BBKY" },
                    new() { Id = "pid-3", Name = "未登记", CustomCode = "ZZZ" },
                },
                Total = 3,
                Page = 1,
                PerPage = 20,
            };

            var result = await service.ListProjectsAsync(1, 20, null);

            Assert.IsTrue(result.Ok);
            var items = result.Data!.Items;
            Assert.IsTrue(items[0].Initialized, "项目 ID 命中登记 → 已初始化");
            Assert.IsTrue(items[1].Initialized, "标识命中登记 → 已初始化");
            Assert.IsFalse(items[2].Initialized, "未登记 → 待初始化");
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    // ---------------- 探活失败分类（TestConnection） ----------------

    [TestMethod]
    public async Task 测试连接_探活失败按分类给状态码与脱敏message()
    {
        var (_, projects, _, service) = Create();

        projects.Throws = new YunxiaoApiException(401, "原始细节不得外泄");
        var unauthorized = await service.TestConnectionAsync("org-1", "ptok");
        Assert.IsFalse(unauthorized.Ok);
        Assert.AreEqual(401, unauthorized.Code);
        Assert.AreEqual("令牌无效或权限不足", unauthorized.Message);

        projects.Throws = new YunxiaoApiException(403, "原始细节不得外泄");
        var forbidden = await service.TestConnectionAsync("org-1", "ptok");
        Assert.IsFalse(forbidden.Ok);
        Assert.AreEqual(401, forbidden.Code);
        Assert.AreEqual("令牌无效或权限不足", forbidden.Message);

        projects.Throws = new YunxiaoApiException(429, "原始细节不得外泄");
        var limited = await service.TestConnectionAsync("org-1", "ptok");
        Assert.IsFalse(limited.Ok);
        Assert.AreEqual(429, limited.Code);
        Assert.AreEqual("云效接口限流", limited.Message);

        projects.Throws = new HttpRequestException("boom");
        var unreachable = await service.TestConnectionAsync("org-1", "ptok");
        Assert.IsFalse(unreachable.Ok);
        Assert.AreEqual(502, unreachable.Code);
        Assert.AreEqual("云效服务暂不可用", unreachable.Message);
    }

    [TestMethod]
    public async Task 测试连接成功_一发探活取projectCount()
    {
        var (_, projects, _, service) = Create();
        projects.Result = new YunxiaoPagedResult<YunxiaoProject>
        {
            Items = new List<YunxiaoProject>(),
            Total = 7
        };

        var result = await service.TestConnectionAsync("org-1", "ptok");

        Assert.IsTrue(result.Ok);
        Assert.IsTrue(result.Data!.Ok);
        Assert.AreEqual(7, result.Data.ProjectCount);
        Assert.IsNull(result.Data.Message);

        // 只读探活口径：page=1、perPage=1、keyword=null，且绑定本次输入的 PAT
        var call = projects.Calls[^1];
        Assert.AreEqual("org-1", call.OrganizationId);
        Assert.AreEqual(1, call.Page);
        Assert.AreEqual(1, call.PerPage);
        Assert.IsNull(call.Keyword);
        Assert.AreEqual("ptok", call.Pat);
    }

    [TestMethod]
    public async Task 探活pat缺省_取已存PAT用于探活调用()
    {
        var (_, projects, credentials, service) = Create();
        credentials.StoredPat = "stored-pat";

        var result = await service.TestConnectionAsync("org-1", pat: null);

        Assert.IsTrue(result.Ok);
        Assert.AreEqual(1, credentials.GetCallCount);
        Assert.AreEqual("stored-pat", projects.Calls[^1].Pat);

        // 已存 PAT 也没有 → 无令牌可用，按凭据类失败
        credentials.StoredPat = null;
        projects.Calls.Clear();
        var missing = await service.TestConnectionAsync("org-1", pat: null);
        Assert.IsFalse(missing.Ok);
        Assert.AreEqual(401, missing.Code);
        Assert.AreEqual("令牌无效或权限不足", missing.Message);
        Assert.AreEqual(0, projects.Calls.Count);
    }

    // ---------------- 保存时序（先探活后落盘，D7） ----------------

    [TestMethod]
    public async Task 保存前探活失败_返回409语义且SaveAsync零调用()
    {
        var (settings, projects, _, service) = Create();
        projects.Throws = new YunxiaoApiException(401, "原始细节不得外泄");

        var result = await service.SaveSettingsAsync(null, "org-1", "new-pat");

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(409, result.Code);
        Assert.AreEqual("连接校验未通过，未保存", result.Message);
        Assert.AreEqual(0, settings.SaveCallCount);
    }

    [TestMethod]
    public async Task 保存前探活成功_SaveAsync收到正确参数()
    {
        var (settings, projects, credentials, service) = Create();

        var result = await service.SaveSettingsAsync(null, "org-1", "new-pat");

        Assert.IsTrue(result.Ok);
        Assert.IsTrue(result.Data!.Saved);
        Assert.AreEqual(1, settings.SaveCallCount);
        Assert.AreEqual("org-1", settings.LastSave!.Value.OrganizationId);
        Assert.AreEqual("new-pat", settings.LastSave!.Value.Pat);

        // 探活绑定的是本次输入的 PAT，且不查已存凭据
        Assert.AreEqual("new-pat", projects.Calls[^1].Pat);
        Assert.AreEqual(0, credentials.GetCallCount);
    }

    [TestMethod]
    public async Task 保存时pat缺省_透传null保持原值()
    {
        var (settings, projects, credentials, service) = Create();
        credentials.StoredPat = "stored-pat";

        var result = await service.SaveSettingsAsync(null, "org-1", pat: null);

        Assert.IsTrue(result.Ok);
        Assert.AreEqual(1, settings.SaveCallCount);
        Assert.AreEqual("org-1", settings.LastSave!.Value.OrganizationId);
        Assert.IsNull(settings.LastSave!.Value.Pat);
        Assert.AreEqual("stored-pat", projects.Calls[^1].Pat);
    }

    // ---------------- 格式 / 地址校验（400 语义） ----------------

    [TestMethod]
    public async Task 参数校验失败_返回400语义且不探活不落盘()
    {
        var (settings, projects, _, service) = Create();

        var emptyOrg = await service.TestConnectionAsync("", "ptok");
        Assert.IsFalse(emptyOrg.Ok);
        Assert.AreEqual(400, emptyOrg.Code);
        Assert.AreEqual("组织 ID 无效", emptyOrg.Message);

        var emptyPat = await service.SaveSettingsAsync(null, "org-1", "");
        Assert.IsFalse(emptyPat.Ok);
        Assert.AreEqual(400, emptyPat.Code);
        Assert.AreEqual("个人访问令牌格式非法", emptyPat.Message);

        var whitespacePat = await service.TestConnectionAsync("org-1", "   ");
        Assert.IsFalse(whitespacePat.Ok);
        Assert.AreEqual(400, whitespacePat.Code);
        Assert.AreEqual("个人访问令牌格式非法", whitespacePat.Message);

        Assert.AreEqual(0, projects.Calls.Count);
        Assert.AreEqual(0, settings.SaveCallCount);
    }

    [TestMethod]
    public async Task 组织地址末段_覆盖组织ID()
    {
        var (settings, projects, _, service) = Create();

        var result = await service.SaveSettingsAsync(
            "https://devops.aliyun.com/orgs/addr-org", "input-org", "ptok");

        Assert.IsTrue(result.Ok);
        Assert.AreEqual("addr-org", projects.Calls[^1].OrganizationId);
        Assert.AreEqual("addr-org", settings.LastSave!.Value.OrganizationId);
    }

    [TestMethod]
    public async Task 组织地址解析不出_返回400组织地址无效()
    {
        var (settings, projects, _, service) = Create();

        var result = await service.SaveSettingsAsync("不是地址", "input-org", "ptok");

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(400, result.Code);
        Assert.AreEqual("组织地址无效", result.Message);
        Assert.AreEqual(0, projects.Calls.Count);
        Assert.AreEqual(0, settings.SaveCallCount);
    }

    // ---------------- 项目列表（分页默认 / 截断透传） ----------------

    [TestMethod]
    public async Task 项目列表_默认分页与perPage截断后透传()
    {
        var (_, projects, _, service) = Create();
        projects.Result = new YunxiaoPagedResult<YunxiaoProject>
        {
            Items = new List<YunxiaoProject>
            {
                new()
                {
                    Id = "p1",
                    Name = "项目一",
                    CustomCode = "DEMO1",
                    Description = "描述一",
                    GmtCreate = 1761900000000,
                    GmtModified = 1762000000000
                }
            },
            Total = 42,
            Page = 1,
            PerPage = 20
        };

        var defaults = await service.ListProjectsAsync(null, null, null);
        Assert.IsTrue(defaults.Ok);
        Assert.AreEqual(1, defaults.Data!.Page);
        Assert.AreEqual(20, defaults.Data.PerPage);
        Assert.AreEqual(42, defaults.Data.Total);
        Assert.AreEqual(1, defaults.Data.Items.Count);
        // 扁平投影：标识取 customCode，更新时间取 gmtModified 格式化
        Assert.AreEqual("DEMO1", defaults.Data.Items[0].Identifier);
        Assert.IsFalse(string.IsNullOrWhiteSpace(defaults.Data.Items[0].UpdateTime));
        var first = projects.Calls[^1];
        Assert.AreEqual(1, first.Page);
        Assert.AreEqual(20, first.PerPage);
        Assert.IsNull(first.Keyword);

        var clamped = await service.ListProjectsAsync(0, 1000, "kw");
        Assert.IsTrue(clamped.Ok);
        var second = projects.Calls[^1];
        Assert.AreEqual(1, second.Page);
        Assert.AreEqual(200, second.PerPage);
        Assert.AreEqual("kw", second.Keyword);
    }

    // ---------------- 脱敏（绝不回 PAT 明文） ----------------

    [TestMethod]
    public async Task 读取设置_返回体只有组织ID与hasPat标志()
    {
        var (settings, _, _, service) = Create();
        settings.OrganizationIdValue = "org-9";
        settings.HasPatValue = true;

        var result = await service.GetSettingsAsync();

        Assert.IsTrue(result.Ok);
        Assert.AreEqual("org-9", result.Data!.OrganizationId);
        Assert.IsTrue(result.Data.HasPat);

        // 返回体断言：序列化后只允许 organizationId / hasPat 两个字段，不出现 pat 字段
        var json = AgentApiJson.Serialize(result.Data);
        StringAssert.Contains(json, "\"organizationId\"");
        StringAssert.Contains(json, "\"hasPat\"");
        Assert.IsFalse(
            json.Contains("\"pat\"", StringComparison.OrdinalIgnoreCase),
            "返回体不得出现 pat 字段：" + json);
    }
}
