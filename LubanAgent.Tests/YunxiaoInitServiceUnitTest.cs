/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： YunxiaoInitServiceUnitTest
*描述：云效 S2 项目初始化服务 Part1（manifests + preflight）单测（设计 §3/§4 / S2b §11）——
*      模板源翻页聚合（ListTemplateRepositoriesAsync）/ 代号前缀替换派生 + 命令序列 + 脱敏透传 /
*      复用库标记（ReusedLibs）与结构待校验 / 目录·端口冲突 / sessionReady 门禁（仅新建库）/
*      非法参数 400 语义 / 预检零副作用 / 执行复用语义 / FR-3 流水线 Exists 与 SkipIfExisting。
*      全部用手写假件（ICodeupClient/IFlowClient/ISessionGateway/IYunxiaoSettingsService/
*      IWorkspaceDirectoryProvider），不打真实宿主、不写真实 LocalApplicationData。
*      夹具一律合成名（TPLX/NEWX/ACME_DEV_Deploy_DEMO），禁用值零引用。
*
*****************************************************************************/

using System.Net;
using System.Net.Sockets;
using LubanAgentWebHost.Services.Init;

namespace LubanAgent.Tests;

[TestClass]
public class YunxiaoInitServiceUnitTest
{
    /// <summary>模板库（合成名）。</summary>
    private const string TemplateRepoName = "TPLX-Mixed";

    /// <summary>新代号（合成名）。</summary>
    private const string NewCode = "NEWX";

    /// <summary>合成克隆地址（无凭据）。</summary>
    private const string TemplateCloneUrl = "https://codeup.example.com/org-demo/tplx-mixed.git";

    private static readonly IReadOnlyList<string> TemplatePipelines =
    [
        "ACME_DEV_Deploy_TPLX-Mobile",
        "ACME_DEV_Deploy_TPLX-Admin",
        "ACME_DEV_Deploy_TPLX-Service",
    ];

    // ── 手写假件 ────────────────────────────────────────────────────────────

    private sealed class FakeCodeupClient : ICodeupClient
    {
        /// <summary>写类调用留痕（零副作用断言用）。</summary>
        public List<string> WriteCalls { get; } = new();

        public List<(int Page, int PerPage)> SearchCalls { get; } = new();

        public List<string> GetRepoCalls { get; } = new();

        /// <summary>按页号出搜索结果（设置后绕过 Repos 合成；GetManifests 翻页聚合用）。</summary>
        public Func<int, YunxiaoPagedResult<YunxiaoRepository>?>? SearchHandler { get; set; }

        /// <summary>
        /// 组织仓库集（按名检索命中源；GetRepoHandler/CreatedRepos 语义迁移到此 + Search 合成）：
        /// 既有库由用例预置，建库后自动登记（模拟「建过即搜得到」，TOCTOU / 重跑幂等断言用）。
        /// </summary>
        public List<YunxiaoRepository> Repos { get; } = new();

        /// <summary>建库返回值钩子（默认 null；RepoId 回填断言用）。</summary>
        public Func<YunxiaoCreateRepositoryRequest, YunxiaoRepository?>? CreateRepoHandler { get; set; }

        /// <summary>建库请求留痕（参数断言用）。</summary>
        public List<YunxiaoCreateRepositoryRequest> CreateRepoRequests { get; } = new();

        /// <summary>建组请求留痕。</summary>
        public List<YunxiaoCreateNamespaceRequest> CreateNamespaceRequests { get; } = new();

        /// <summary>代码组数据集（默认按 page/perPage 分页返回）。</summary>
        public List<YunxiaoNamespace> Namespaces { get; } =
        [
            new YunxiaoNamespace { Id = 11, Name = "DEMOGRP" },
            new YunxiaoNamespace { Id = 12, Name = "ACMEGRP" },
        ];

        /// <summary>按页号覆写代码组返回（null = 按 Namespaces 分页；空页 = 翻页截止信号）。</summary>
        public Func<int, List<YunxiaoNamespace>?>? NamespacesHandler { get; set; }

        /// <summary>代码组列表调用留痕（页号；翻页聚合断言用）。</summary>
        public List<int> NamespaceCalls { get; } = new();

        /// <summary>模板库数据集（ListTemplateRepositoriesAsync 按 page/perPage 分页返回）。</summary>
        public List<YunxiaoTemplateRepository> Templates { get; } = new();

        /// <summary>模板库调用留痕（(page, perPage)；翻页聚合断言用）。</summary>
        public List<(int Page, int PerPage)> TemplateCalls { get; } = new();

        /// <summary>按页号覆写模板库返回（null = 按 Templates 分页；空页 = 翻页截止信号）。</summary>
        public Func<int, List<YunxiaoTemplateRepository>?>? TemplateHandler { get; set; }

        public Task<YunxiaoPagedResult<YunxiaoRepository>?> SearchRepositoriesAsync(
            string? search = null, int page = 1, int perPage = 20, string? orderBy = null,
            string? sort = null, bool? archived = null, string? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            SearchCalls.Add((page, perPage));
            if (SearchHandler is not null)
            {
                return Task.FromResult(SearchHandler.Invoke(page));
            }

            // 模糊关键字语义仿真（对齐「名称检索关键字」）：Name/Path 含关键字即返回（相似名一并返回），
            // 精确命中与否由被测服务 FindRepoByNameAsync 的 Name/Path 精确匹配判定
            var matches = Repos
                .Where(r => string.IsNullOrWhiteSpace(search)
                    || (r.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (r.Path?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            var items = matches.Skip((page - 1) * perPage).Take(perPage).ToList();
            return Task.FromResult<YunxiaoPagedResult<YunxiaoRepository>?>(new YunxiaoPagedResult<YunxiaoRepository>
            {
                Items = items,
                Total = matches.Count,
                Page = page,
                PerPage = perPage,
            });
        }

        public Task<YunxiaoRepository?> GetRepositoryAsync(
            string repoId, string? organizationId = null, CancellationToken cancellationToken = default)
        {
            GetRepoCalls.Add(repoId);
            return Task.FromResult(Repos.FirstOrDefault(r =>
                string.Equals(r.Name, repoId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Path, repoId, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<YunxiaoRepository?> CreateRepositoryAsync(
            YunxiaoCreateRepositoryRequest request, string? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            WriteCalls.Add("CreateRepository:" + request.Name);
            CreateRepoRequests.Add(request);
            var created = CreateRepoHandler?.Invoke(request);
            // 建过即搜得到（语义自 CreatedRepos/Get 回读迁移而来）
            Repos.Add(created ?? new YunxiaoRepository { Name = request.Name, Path = request.Name?.ToLowerInvariant() });
            return Task.FromResult(created);
        }

        public Task<YunxiaoRepository?> UpdateRepositoryAsync(
            string repoId, YunxiaoUpdateRepositoryRequest request, string? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            WriteCalls.Add("UpdateRepository:" + repoId);
            return Task.FromResult<YunxiaoRepository?>(null);
        }

        public Task<List<YunxiaoBranch>?> ListBranchesAsync(
            long repositoryId, int page = 1, int perPage = 100, string? sort = null,
            string? search = null, string? organizationId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<List<YunxiaoBranch>?>(null);

        public Task<List<YunxiaoTemplateRepository>?> ListTemplateRepositoriesAsync(
            int templateType = 1, int page = 1, int perPage = 100, string? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            TemplateCalls.Add((page, perPage));
            if (TemplateHandler is not null)
            {
                return Task.FromResult(TemplateHandler.Invoke(page));
            }

            return Task.FromResult<List<YunxiaoTemplateRepository>?>(
                Templates.Skip((page - 1) * perPage).Take(perPage).ToList());
        }

        public Task<List<YunxiaoNamespace>?> ListNamespacesAsync(
            string? search = null, long? parentId = null, int page = 1, int perPage = 100,
            string? orderBy = null, string? sort = null, string? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            NamespaceCalls.Add(page);
            if (NamespacesHandler is not null)
            {
                return Task.FromResult(NamespacesHandler.Invoke(page));
            }

            return Task.FromResult<List<YunxiaoNamespace>?>(
                Namespaces.Skip((page - 1) * perPage).Take(perPage).ToList());
        }

        public Task<YunxiaoNamespace?> CreateNamespaceAsync(
            YunxiaoCreateNamespaceRequest request, string? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            WriteCalls.Add("CreateNamespace:" + request.Name);
            CreateNamespaceRequests.Add(request);
            return Task.FromResult<YunxiaoNamespace?>(new YunxiaoNamespace { Id = 99, Name = request.Name });
        }
    }

    private sealed class FakeFlowClient : IFlowClient
    {
        public List<string> WriteCalls { get; } = new();

        /// <summary>ListPipelinesAsync 返回的流水线清单（空 = null）。</summary>
        public List<YunxiaoPipeline> Pipelines { get; } = new();

        /// <summary>按 id 取流水线详情（未命中返回 null）。</summary>
        public Func<string, YunxiaoPipeline?>? GetPipelineHandler { get; set; }

        /// <summary>建线请求留痕（参数断言用）。</summary>
        public List<YunxiaoCreatePipelineRequest> CreateCalls { get; } = new();

        /// <summary>改线请求留痕（参数断言用）。</summary>
        public List<(string PipelineId, YunxiaoUpdatePipelineRequest Request)> UpdateCalls { get; } = new();

        /// <summary>建线返回 id（可抛异常模拟单端失败）。</summary>
        public Func<YunxiaoCreatePipelineRequest, string?>? CreateHandler { get; set; }

        /// <summary>改线返回值（可抛异常模拟单端失败）。</summary>
        public Func<string, bool>? UpdateHandler { get; set; }

        /// <summary>ListPipelinesAsync 调用留痕（页号；反查频次断言用）。</summary>
        public List<int> ListPipelineCalls { get; } = new();

        public Task<List<YunxiaoPipeline>> ListPipelinesAsync(
            string? pipelineName = null, int maxResults = 100, string? statusList = null,
            string? organizationId = null, CancellationToken cancellationToken = default)
        {
            ListPipelineCalls.Add(maxResults);
            return Task.FromResult(Pipelines.ToList());
        }

        public Task<YunxiaoPipeline?> GetPipelineAsync(
            string pipelineId, string? organizationId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(GetPipelineHandler?.Invoke(pipelineId));

        public Task<string?> CreatePipelineAsync(
            YunxiaoCreatePipelineRequest request, string? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            WriteCalls.Add("CreatePipeline");
            CreateCalls.Add(request);
            return Task.FromResult(CreateHandler is null ? "pipe-fake" : CreateHandler(request));
        }

        public Task<bool> UpdatePipelineAsync(
            string pipelineId, YunxiaoUpdatePipelineRequest request, string? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            WriteCalls.Add("UpdatePipeline:" + pipelineId);
            UpdateCalls.Add((pipelineId, request));
            return Task.FromResult(UpdateHandler is null || UpdateHandler(pipelineId));
        }

        public Task<YunxiaoPipelineRun?> RunPipelineAsync(
            string pipelineId, YunxiaoRunPipelineRequest? request = null, string? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            WriteCalls.Add("RunPipeline:" + pipelineId);
            return Task.FromResult<YunxiaoPipelineRun?>(null);
        }

        public Task<bool> ExecutePipelineJobRunAsync(
            string pipelineId, string pipelineRunId, string jobId, string? organizationId = null,
            CancellationToken cancellationToken = default)
        {
            WriteCalls.Add("ExecutePipelineJobRun");
            return Task.FromResult(false);
        }

        public Task<List<YunxiaoHostGroup>?> ListHostGroupsAsync(
            string? organizationId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<List<YunxiaoHostGroup>?>(null);

        public Task<YunxiaoHostGroup?> GetHostGroupAsync(
            long hostGroupId, string? organizationId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<YunxiaoHostGroup?>(null);
    }

    private sealed class FakeSessionGateway : ISessionGateway
    {
        public bool ProbeResult { get; set; } = true;

        public List<string> WriteCalls { get; } = new();

        /// <summary>SendAsync 留痕（instruction / approvedCommands 断言用）。</summary>
        public List<(string SessionId, string Instruction, IReadOnlyList<string>? Approved)> Sends { get; } = new();

        /// <summary>SendAsync 行为覆写（可抛异常 / 可挂起模拟执行中）。</summary>
        public Func<string, string, IReadOnlyList<string>?, Task>? SendHandler { get; set; }

        /// <summary>回执（Ok=false 模拟会话收尾失败）。</summary>
        public SessionReceipt Receipt { get; set; } = new(true, null, null);

        /// <summary>建会话行为覆写（可同步抛异常）。</summary>
        public Func<Task<string>>? CreateSessionHandler { get; set; }

        public Task<string> CreateInitSessionAsync(string workspaceId, CancellationToken ct)
        {
            WriteCalls.Add("CreateInitSession:" + workspaceId);
            return CreateSessionHandler is null ? Task.FromResult("sess-fake") : CreateSessionHandler();
        }

        public Task<bool> ProbeAsync(CancellationToken ct) => Task.FromResult(ProbeResult);

        public Task SendAsync(
            string sessionId, string instruction, IReadOnlyList<string>? approvedCommands, CancellationToken ct)
        {
            WriteCalls.Add("Send:" + sessionId);
            Sends.Add((sessionId, instruction, approvedCommands));
            return SendHandler is null ? Task.CompletedTask : SendHandler(sessionId, instruction, approvedCommands);
        }

        public Task ConfirmAsync(string sessionId, string confirmationId, string result, CancellationToken ct)
        {
            WriteCalls.Add("Confirm:" + confirmationId);
            return Task.CompletedTask;
        }

        public Task<SessionReceipt> WaitReceiptAsync(string sessionId, CancellationToken ct)
            => Task.FromResult(new SessionReceipt(Receipt.Ok, Receipt.Summary, Receipt.Error, sessionId, Receipt.RunId, Receipt.Denied));
    }

    private sealed class FakeSettingsService : IYunxiaoSettingsService
    {
        public string GetOrganizationId() => "org-demo";

        public bool HasPat() => true;

        public Task SaveAsync(string? organizationId, string? pat = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void ApplyPersistedOrganizationId()
        {
        }
    }

    private sealed class FakeWorkspaceDirectoryProvider : IWorkspaceDirectoryProvider
    {
        public List<WorkspaceDir> Items { get; } = new();

        public IReadOnlyList<WorkspaceDir> List() => Items;
    }

    // ---------------- S2 收口 · 项目初始化完成登记 ----------------

    /// <summary>缺 项目 ID/标识 或代号 → 400，且不落盘。</summary>
    [TestMethod]
    public async Task SaveProjectInit_缺键400()
    {
        var h = Create(Path.GetTempPath());

        var noKey = await h.Service.SaveProjectInitAsync(
            new ProjectInitRequest(null, null, "BBKY"), CancellationToken.None);
        Assert.IsFalse(noKey.Ok);
        Assert.AreEqual(400, noKey.Code);

        var noCode = await h.Service.SaveProjectInitAsync(
            new ProjectInitRequest("pid-1", "BBKY", " "), CancellationToken.None);
        Assert.IsFalse(noCode.Ok);
        Assert.AreEqual(400, noCode.Code);
    }

    /// <summary>合法入参落盘：项目 ID / 标识 / 库名 / 三端流水线可反查。</summary>
    [TestMethod]
    public async Task SaveProjectInit_合法入参落盘可反查()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-init-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ProjectInitStore(Path.Combine(root, "init.json"));
            var service = new YunxiaoInitService(
                new FakeCodeupClient(), new FakeFlowClient(), new FakeSessionGateway(),
                new InitTaskStore(Path.Combine(root, "tasks.json")),
                new FakeSettingsService(), new FakeWorkspaceDirectoryProvider(), store);

            var result = await service.SaveProjectInitAsync(new ProjectInitRequest(
                "pid-1", "BBKY", "BBKY", "BBKY-Mixed",
                new[] { new ProjectInitEnd("mobile", "CME_DEV_Deploy_BBKY-Mobile", "123", Dir: "BBKY-Mobile") },
                WorkspaceId: "ws-1"),
                CancellationToken.None);

            Assert.IsTrue(result.Ok);
            var all = await store.GetAllAsync();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("pid-1", all[0].ProjectId);
            Assert.AreEqual("BBKY-Mixed", all[0].RepoName);
            Assert.AreEqual(1, all[0].Pipelines.Count);
            Assert.AreEqual("123", all[0].Pipelines[0].PipelineId);
            Assert.AreEqual("ws-1", all[0].WorkspaceId);
            Assert.AreEqual("BBKY-Mobile", all[0].Pipelines[0].Dir);

            // 反查：按标识命中，DTO 回带反显所需字段
            var got = await service.GetProjectInitAsync(null, "BBKY", CancellationToken.None);
            Assert.IsTrue(got.Ok);
            Assert.IsNotNull(got.Data);
            Assert.AreEqual("BBKY", got.Data!.Codename);
            Assert.AreEqual("BBKY-Mixed", got.Data.RepoName);
            Assert.AreEqual(1, got.Data.Ends.Count);
            Assert.AreEqual("CME_DEV_Deploy_BBKY-Mobile", got.Data.Ends[0].Name);
            Assert.AreEqual("BBKY-Mobile", got.Data.Ends[0].Dir);
            Assert.AreEqual("ws-1", got.Data.WorkspaceId);

            // 按未知项目反查 → null（不误报已初始化）
            var missing = await service.GetProjectInitAsync("pid-unknown", null, CancellationToken.None);
            Assert.IsTrue(missing.Ok);
            Assert.IsNull(missing.Data);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed record Harness(
        FakeCodeupClient Codeup,
        FakeFlowClient Flow,
        FakeSessionGateway Gateway,
        FakeWorkspaceDirectoryProvider Workspaces,
        YunxiaoInitService Service);

    private static Harness Create(string rootPath)
    {
        var codeup = new FakeCodeupClient();
        var flow = new FakeFlowClient();
        var gateway = new FakeSessionGateway();
        var workspaces = new FakeWorkspaceDirectoryProvider();
        workspaces.Items.Add(new WorkspaceDir("ws-1", rootPath));
        var service = new YunxiaoInitService(
            codeup, flow, gateway,
            new InitTaskStore(Path.Combine(Path.GetTempPath(), "luban-yunxiao-" + Guid.NewGuid().ToString("N"), "tasks.json")),
            new FakeSettingsService(),
            workspaces);
        return new Harness(codeup, flow, gateway, workspaces, service);
    }

    /// <summary>合成仓库条目。</summary>
    private static YunxiaoRepository Repo(string name, string? httpUrl = null, string? description = null)
        => new() { Name = name, Path = name.ToLowerInvariant(), HttpUrlToRepo = httpUrl, Description = description };

    /// <summary>合成模板库条目（ListTemplateRepositoriesAsync 真值形态）。</summary>
    private static YunxiaoTemplateRepository Template(string name, long projectId, string? description = null)
        => new() { Name = name, ProjectId = projectId, Description = description, Path = name.ToLowerInvariant() };

    // ── GetManifests：模板源（ListTemplateRepositoriesAsync）翻页聚合 ──────

    [TestMethod]
    public async Task GetManifests_模板源翻页聚合_3页29条_perPage100()
    {
        var h = Create(Path.GetTempPath());
        var all = Enumerable.Range(1, 29).Select(i => Template($"TPL{i:D2}-Mixed", i)).ToList();
        h.Codeup.TemplateHandler = page => page switch
        {
            1 => all.Take(10).ToList(),
            2 => all.Skip(10).Take(10).ToList(),
            3 => all.Skip(20).Take(9).ToList(),
            _ => [],
        };

        var manifests = await h.Service.GetManifestsAsync(CancellationToken.None);

        Assert.AreEqual(29, manifests.Repos.Count);
        CollectionAssert.AreEqual(
            new[] { (1, 100), (2, 100), (3, 100), (4, 100) },
            h.Codeup.TemplateCalls);
    }

    [TestMethod]
    public async Task GetManifests_模板源收录_单端与综合模板均返回()
    {
        var h = Create(Path.GetTempPath());
        h.Codeup.Templates.AddRange(
        [
            Template("TPLX-Mixed", 1, "综合模板"),
            Template("TPLX-Service", 2, "单端模板"),
            Template("NEWX-Admin", 3),
        ]);

        var manifests = await h.Service.GetManifestsAsync(CancellationToken.None);

        // S2b §11.1：模板源是登记真值，单端与综合模板均收录（不再按 -Mixed 后缀筛）
        Assert.AreEqual(3, manifests.Repos.Count);
        CollectionAssert.AreEquivalent(
            new[] { "TPLX-Mixed", "TPLX-Service", "NEWX-Admin" },
            manifests.Repos.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public async Task GetManifests_模板翻页_空页截断不死循环()
    {
        var h = Create(Path.GetTempPath());
        h.Codeup.TemplateHandler = page => page switch
        {
            1 => [Template("TPLX-Mixed", 1)],
            _ => [],
        };

        var manifests = await h.Service.GetManifestsAsync(CancellationToken.None);

        Assert.AreEqual(1, manifests.Repos.Count);
        Assert.AreEqual(2, h.Codeup.TemplateCalls.Count);
    }

    [TestMethod]
    public async Task GetManifests_聚合模板字段_描述与TemplateProjectId()
    {
        var h = Create(Path.GetTempPath());
        h.Codeup.Templates.Add(Template("TPLX-Mixed", 321, "母版"));
        h.Gateway.ProbeResult = true;

        var manifests = await h.Service.GetManifestsAsync(CancellationToken.None);

        Assert.AreEqual("TPLX-Mixed", manifests.Repos[0].Name);
        Assert.AreEqual("母版", manifests.Repos[0].Description);
        Assert.AreEqual(321L, manifests.Repos[0].TemplateProjectId);
        Assert.AreEqual(2, manifests.Groups.Count);
        Assert.AreEqual("DEMOGRP", manifests.Groups[0].Name);
        Assert.AreEqual(1, manifests.Workspaces.Count);
        Assert.AreEqual("ws-1", manifests.Workspaces[0].Id);
        Assert.IsTrue(manifests.SessionReady);
    }

    [TestMethod]
    public async Task GetManifests_代码组翻页合并_两页各60条_去重120()
    {
        var h = Create(Path.GetTempPath());
        h.Codeup.Templates.Add(Template("TPLX-Mixed", 1));
        h.Codeup.NamespacesHandler = page => page switch
        {
            1 => Enumerable.Range(1, 60).Select(i => new YunxiaoNamespace { Id = i, Name = $"GRP{i:D3}" }).ToList(),
            2 => Enumerable.Range(61, 60).Select(i => new YunxiaoNamespace { Id = i, Name = $"GRP{i:D3}" }).ToList(),
            _ => null,   // 空页 = 翻页截止
        };

        var manifests = await h.Service.GetManifestsAsync(CancellationToken.None);

        Assert.AreEqual(120, manifests.Groups.Count);
        Assert.AreEqual(120, manifests.Groups.Select(g => g.Id).Distinct().Count());   // 按 Id 去重无重复
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, h.Codeup.NamespaceCalls);
    }

    // ── Preflight：派生 + 序列 + 脱敏 ───────────────────────────────────────

    private static PreflightRequest Request(
        string? templateRepoName = TemplateRepoName,
        string? newCode = NewCode,
        string? workspaceId = "ws-1",
        string? branch = "master",
        string? groupNewName = null,
        IReadOnlyList<InitEndConfig>? ends = null)
        => new(
            templateRepoName ?? "",
            newCode ?? "",
            workspaceId ?? "",
            branch ?? "",
            TemplatePipelines,
            groupNewName,
            ends);

    [TestMethod]
    public async Task Preflight_派生命名与命令序列_脱敏透传()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var data = result.Data!;
        Assert.AreEqual("NEWX-Mixed", data.Plan.RepoName);
        Assert.AreEqual("TPLX-Admin", data.Plan.Dirs[0].From);
        Assert.AreEqual("NEWX-Admin", data.Plan.Dirs[0].To);
        Assert.AreEqual("ACME_DEV_Deploy_TPLX-Mobile", data.Plan.Pipelines[0].From);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Mobile", data.Plan.Pipelines[0].To);

        // 命令序列与 GitCommandPlanner 同源：clone → mv×3 → commit → remote → push → 切 develop（S2c §12.2 尾部 develop 步）
        Assert.IsTrue(data.GitCommands.Count >= 8);
        StringAssert.Contains(data.GitCommands[0].Command, "git clone --branch master");
        StringAssert.Contains(data.GitCommands[0].Command, TemplateCloneUrl);
        StringAssert.Contains(data.GitCommands[^2].Command, "push origin master");
        StringAssert.Contains(data.GitCommands[^1].Command, "checkout -b develop");
        Assert.AreEqual(0, data.Conflicts.Count);
        Assert.IsTrue(data.SessionReady);

        // 脱敏透传：序列里只有裸 URL，无 PAT/凭据痕迹
        foreach (var step in data.GitCommands)
        {
            Assert.IsFalse(step.Command.Contains("pt-", StringComparison.Ordinal), step.Command);
            Assert.IsFalse(step.Command.Contains('@'), step.Command);
        }
    }

    [TestMethod]
    public async Task Preflight_库名已存在_复用_缺源码_出检出步骤()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed", "https://codeup.example.com/org-demo/newx-mixed.git"));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        // S2c §12.1.2：复用库 + 本地无源码 → 出本地检出步骤（clone 复用库真 URL → 切 develop），不列冲突
        Assert.IsTrue(result.Ok);
        var data = result.Data!;
        Assert.AreEqual(0, data.Conflicts.Count);
        CollectionAssert.AreEqual(new[] { "NEWX-Mixed" }, data.ReusedLibs!.ToList());
        Assert.AreEqual(3, data.GitCommands.Count);
        StringAssert.Contains(data.GitCommands[0].Command, "git clone");
        StringAssert.Contains(data.GitCommands[0].Command, "https://codeup.example.com/org-demo/newx-mixed.git");
        StringAssert.Contains(data.GitCommands[^1].Command, "checkout -b develop");
        Assert.IsTrue(data.Warnings!.Any(w => w.Contains("结构待校验")));
        Assert.IsTrue(data.SessionReady);
    }

    [TestMethod]
    public async Task Preflight_库名已存在_复用_已有源码_零git步骤()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed", "NEWX-Admin"));
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed"));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        // S2c §12.1.2 幂等：目录已有源码 → 零 git 步骤（不重复 clone）
        Assert.IsTrue(result.Ok);
        var data = result.Data!;
        Assert.AreEqual(0, data.Conflicts.Count);
        CollectionAssert.AreEqual(new[] { "NEWX-Mixed" }, data.ReusedLibs!.ToList());
        Assert.AreEqual(0, data.GitCommands.Count);
        Assert.IsTrue(data.SessionReady);
    }

    [TestMethod]
    public async Task Preflight_复用库_本地三子目录齐全_无结构警告()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed", "NEWX-Admin"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed", "NEWX-Mobile"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed", "NEWX-Service"));
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed"));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        Assert.AreEqual(0, result.Data!.Warnings!.Count);
        Assert.AreEqual(0, result.Data.Conflicts.Count);
    }

    [TestMethod]
    public async Task Preflight_单端模板_前缀替换派生_无子目录段()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);
        h.Codeup.Repos.Add(Repo("TPLX-Service", "https://codeup.example.com/org-demo/tplx-service.git"));

        var result = await h.Service.PreflightAsync(Request(templateRepoName: "TPLX-Service"), CancellationToken.None);

        // S2b §11.1：单端模板库名 = 代号前缀替换（TPLX-Service → NEWX-Service），无三子目录改名
        Assert.IsTrue(result.Ok);
        var data = result.Data!;
        Assert.AreEqual("NEWX-Service", data.Plan.RepoName);
        Assert.AreEqual(0, data.Plan.Dirs.Count);
        Assert.IsFalse(data.GitCommands.Any(s => s.Command.Contains(" mv ")));
        Assert.IsFalse(data.GitCommands.Any(s => s.Command.Contains("commit")));
        Assert.AreEqual(0, data.Conflicts.Count);
        Assert.AreEqual(0, data.ReusedLibs!.Count);
    }

    [TestMethod]
    public async Task Preflight_按名查重_相似名不误命中()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed2"));   // 相似名：模糊检索会返回，精确匹配须拒（NEWX-Mixed2 ≠ NEWX-Mixed）

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        Assert.AreEqual(0, result.Data!.Conflicts.Count);
        Assert.AreEqual(0, result.Data.ReusedLibs!.Count);   // 不误判复用
        Assert.IsTrue(result.Data.GitCommands.Count > 0);    // 新建库仍出 git 步骤
    }

    [TestMethod]
    public async Task Preflight_按名查重_翻页后才命中_转复用()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        for (var i = 1; i <= 100; i++)
        {
            h.Codeup.Repos.Add(Repo($"NEWX-Mixed{i:D2}"));   // 相似名占满第 1 页
        }
        h.Codeup.Repos.Add(Repo("NEWX-Mixed"));              // 精确命中排第 101 → 第 2 页才出现

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        Assert.AreEqual(0, result.Data!.Conflicts.Count);
        CollectionAssert.AreEqual(new[] { "NEWX-Mixed" }, result.Data.ReusedLibs!.ToList());
        Assert.IsTrue(h.Codeup.SearchCalls.Contains((2, 100)));   // 确有翻页到第 2 页
    }

    [TestMethod]
    public async Task Preflight_工作区目录已存在_列冲突()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed"));
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        Assert.IsTrue(result.Data!.Conflicts.Any(c => c.Contains("目录")));
    }

    [TestMethod]
    public async Task Preflight_后端端口被占用_列冲突()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        // 真起一个监听端口模拟占用
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var busyPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var ends = new[] { new InitEndConfig("backend", On: true, Port: busyPort) };
            var result = await h.Service.PreflightAsync(Request(ends: ends), CancellationToken.None);

            Assert.IsTrue(result.Ok);
            Assert.IsTrue(result.Data!.Conflicts.Any(c => c.Contains("端口")));
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Preflight_SessionReady为false_只标记不报错()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Gateway.ProbeResult = false;

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        Assert.IsFalse(result.Data!.SessionReady);
        Assert.IsNotNull(result.Data.Plan);
    }

    // ── 非法参数 400 语义 ───────────────────────────────────────────────────

    [TestMethod]
    public async Task Preflight_非法参数_400语义()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);

        // 母版库名无 -Mixed 后缀
        var badTemplate = await h.Service.PreflightAsync(Request(templateRepoName: "NOTMIXED"), CancellationToken.None);
        Assert.IsFalse(badTemplate.Ok);
        Assert.AreEqual(400, badTemplate.Code);
        Assert.IsNotNull(badTemplate.Message);

        // 新代号非法（含连字符）
        var badCode = await h.Service.PreflightAsync(Request(newCode: "NEW-X"), CancellationToken.None);
        Assert.IsFalse(badCode.Ok);
        Assert.AreEqual(400, badCode.Code);

        // 未知工作区
        var badWorkspace = await h.Service.PreflightAsync(Request(workspaceId: "ws-404"), CancellationToken.None);
        Assert.IsFalse(badWorkspace.Ok);
        Assert.AreEqual(400, badWorkspace.Code);

        // 分支为空
        var badBranch = await h.Service.PreflightAsync(Request(branch: " "), CancellationToken.None);
        Assert.IsFalse(badBranch.Ok);
        Assert.AreEqual(400, badBranch.Code);
    }

    // ── 零副作用 ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Preflight_零副作用_写方法零调用()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        await h.Service.PreflightAsync(Request(groupNewName: "NEWGRP"), CancellationToken.None);

        // 不建组、不建库、不发会话、不动流水线
        Assert.AreEqual(0, h.Codeup.WriteCalls.Count);
        Assert.AreEqual(0, h.Flow.WriteCalls.Count);
        Assert.AreEqual(0, h.Gateway.WriteCalls.Count);
    }

    [TestMethod]
    public async Task GetManifests_零副作用_不建不发()
    {
        var h = Create(Path.GetTempPath());
        h.Codeup.Templates.Add(Template("TPLX-Mixed", 1));

        await h.Service.GetManifestsAsync(CancellationToken.None);

        Assert.AreEqual(0, h.Codeup.WriteCalls.Count);
        Assert.AreEqual(0, h.Flow.WriteCalls.Count);
        Assert.AreEqual(0, h.Gateway.WriteCalls.Count);
    }

    // ── 任务 7：执行 / 任务 / 重跑（Part2）────────────────────────────────

    /// <summary>执行入参装配（缺省单库 TPLX-Mixed → NEWX-Mixed）。</summary>
    private static ExecRequest Exec(
        IReadOnlyList<ExecLibSpec>? libs = null,
        string? newCode = NewCode,
        string? workspaceId = "ws-1",
        string? branch = "master",
        string? groupNewName = null,
        string? visibility = null,
        string? description = null)
        => new(
            libs ?? [new ExecLibSpec(TemplateRepoName, "NEWX-Mixed")],
            newCode ?? "",
            workspaceId ?? "",
            branch ?? "",
            groupNewName,
            visibility,
            description);

    /// <summary>新建独立临时根目录（不落真实 LocalApplicationData）。</summary>
    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-ex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>轮询任务态直到收口（后台 Task.Run 推进的同步栅栏）。</summary>
    private static async Task<InitTask> WaitTerminalAsync(YunxiaoInitService service, string taskId)
    {
        for (var i = 0; i < 500; i++)
        {
            var result = await service.GetTaskAsync(taskId, CancellationToken.None);
            if (result.Data?.FinishedAt is not null)
            {
                return result.Data!;
            }

            await Task.Delay(20);
        }

        Assert.Fail("后台任务未在限时内收口");
        return null!;
    }

    [TestMethod]
    public async Task Exec_秒回taskId_TryBegin互斥_进行中再Exec409()
    {
        var root = NewRoot();
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        // 挂起 SendAsync，保证后台停在执行中（秒回与互斥断言的前提）
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Gateway.SendHandler = (_, _, _) => gate.Task;

        var first = await h.Service.ExecAsync(Exec(), CancellationToken.None);

        Assert.IsTrue(first.Ok);
        Assert.IsNotNull(first.Data);

        // 秒回：返回时后台未收口
        var mid = await h.Service.GetTaskAsync(first.Data!, CancellationToken.None);
        Assert.IsNotNull(mid.Data);
        Assert.IsNull(mid.Data!.FinishedAt);

        // 互斥：同代号进行中再 Exec → 409
        var second = await h.Service.ExecAsync(Exec(), CancellationToken.None);
        Assert.IsFalse(second.Ok);
        Assert.AreEqual(409, second.Code);

        gate.SetResult();
        var done = await WaitTerminalAsync(h.Service, first.Data!);
        Assert.AreEqual(InitTaskStatus.Done, done.Status);
        Assert.AreEqual(NewCode, done.Codename);
    }

    [TestMethod]
    public async Task Exec_二次查重转复用_零建库_发本地检出_他库继续()
    {
        var root = NewRoot();
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("TPL2-Mixed", "https://codeup.example.com/org-demo/tpl2-mixed.git"));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed", "https://codeup.example.com/org-demo/newx-mixed.git"));   // 预检后被抢先创建 → 转复用

        var exec = await h.Service.ExecAsync(Exec(libs:
        [
            new ExecLibSpec(TemplateRepoName, "NEWX-Mixed"),
            new ExecLibSpec("TPL2-Mixed", "NEWX2-Mixed"),
        ]), CancellationToken.None);

        Assert.IsTrue(exec.Ok);
        var task = await WaitTerminalAsync(h.Service, exec.Data!);

        // 被抢先创建的库（复用）：零建库，但缺本地源码 → 发「本地检出」计划（S2c §12.1.2/§12.2）
        var lib1 = task.Libs.Single(l => l.Repo == "NEWX-Mixed");
        Assert.AreEqual(LibStatus.Done, lib1.Status);
        Assert.AreEqual("复用已有库", lib1.Detail);
        Assert.IsFalse(h.Codeup.CreateRepoRequests.Any(r => r.Name == "NEWX-Mixed"));
        var reuseSend = h.Gateway.Sends.FirstOrDefault(s => s.Instruction.Contains("完成 NEWX-Mixed 的本地检出"));
        Assert.IsNotNull(reuseSend, "复用库应发本地检出指令");
        Assert.IsFalse(h.Gateway.Sends.Any(s => s.Instruction.Contains("完成 NEWX-Mixed 的复制初始化")));
        // approvedCommands 同源 = PlanLocalCheckout（clone + 两步 develop）
        var expectedCheckout = GitCommandPlanner.PlanLocalCheckout(
            root, "https://codeup.example.com/org-demo/newx-mixed.git", "NEWX-Mixed", "master");
        CollectionAssert.AreEqual(expectedCheckout.Select(s => s.Command).ToList(), reuseSend!.Approved!.ToList());

        // 他库继续：建库 + 复制初始化会话推进 done
        var lib2 = task.Libs.Single(l => l.Repo == "NEWX2-Mixed");
        Assert.AreEqual(LibStatus.Done, lib2.Status);
        Assert.IsTrue(h.Codeup.CreateRepoRequests.Any(r => r.Name == "NEWX2-Mixed"));
        Assert.IsTrue(h.Gateway.Sends.Any(s => s.Instruction.Contains("完成 NEWX2-Mixed 的复制初始化")));
        Assert.AreEqual(2, h.Gateway.Sends.Count);

        // 复用不算失败 → 全 done 收口
        Assert.AreEqual(InitTaskStatus.Done, task.Status);
        Assert.IsNotNull(task.FinishedAt);
    }

    [TestMethod]
    public async Task Exec_复用已有源码_幂等_零git零Send()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed", "NEWX-Admin"));
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed"));

        var exec = await h.Service.ExecAsync(Exec(), CancellationToken.None);

        Assert.IsTrue(exec.Ok);
        var task = await WaitTerminalAsync(h.Service, exec.Data!);
        var lib = task.Libs.Single();
        Assert.AreEqual(LibStatus.Done, lib.Status);
        Assert.AreEqual("复用已有库", lib.Detail);
        Assert.AreEqual(0, h.Gateway.Sends.Count);   // 已有源码 → 幂等零 git
    }

    [TestMethod]
    public async Task Exec_全复用_Session不可用_不阻断_零建库零Send()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed", "NEWX-Admin"));   // 已有本地源码（幂等）
        var h = Create(root);
        h.Gateway.ProbeResult = false;   // 会话通路不可用
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed"));   // 全复用

        var exec = await h.Service.ExecAsync(Exec(), CancellationToken.None);

        // S2c §12.2 门禁：全部库已有本地源码检出 → 免会话（503 不触发），幂等零 git
        Assert.IsTrue(exec.Ok);
        var task = await WaitTerminalAsync(h.Service, exec.Data!);

        var lib = task.Libs.Single();
        Assert.AreEqual(LibStatus.Done, lib.Status);
        Assert.AreEqual("复用已有库", lib.Detail);
        Assert.AreEqual(InitTaskStatus.Done, task.Status);
        Assert.AreEqual(0, h.Codeup.CreateRepoRequests.Count);   // 零 CreateRepository
        Assert.AreEqual(0, h.Codeup.WriteCalls.Count);           // 零建组
        Assert.AreEqual(0, h.Gateway.Sends.Count);               // 零 SendAsync
        Assert.IsFalse(h.Gateway.WriteCalls.Any(c => c.StartsWith("CreateInitSession", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Exec_全复用_缺源码_Session不可用_仍要会话503()
    {
        var root = NewRoot();
        var h = Create(root);
        h.Gateway.ProbeResult = false;   // 会话通路不可用
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed"));   // 全复用但本地缺源码

        var exec = await h.Service.ExecAsync(Exec(), CancellationToken.None);

        // S2c §12.2：全复用但需 clone（缺本地源码）→ 仍要求会话 → 503 阻断
        Assert.IsFalse(exec.Ok);
        Assert.AreEqual(503, exec.Code);
        Assert.AreEqual(0, h.Codeup.WriteCalls.Count);
        Assert.AreEqual(0, h.Gateway.WriteCalls.Count);
    }

    [TestMethod]
    public async Task Exec_混合_一新建一复用_仍要会话()
    {
        var root = NewRoot();
        var h = Create(root);
        h.Gateway.ProbeResult = false;
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("TPL2-Mixed", "https://codeup.example.com/org-demo/tpl2-mixed.git"));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed"));   // 一库复用

        var exec = await h.Service.ExecAsync(Exec(libs:
        [
            new ExecLibSpec(TemplateRepoName, "NEWX-Mixed"),
            new ExecLibSpec("TPL2-Mixed", "NEWX2-Mixed"),   // 一库需新建
        ]), CancellationToken.None);

        // 存在需新建的库 → 会话仍阻断（503）
        Assert.IsFalse(exec.Ok);
        Assert.AreEqual(503, exec.Code);
        Assert.AreEqual(0, h.Codeup.WriteCalls.Count);
        Assert.AreEqual(0, h.Gateway.WriteCalls.Count);
    }

    [TestMethod]
    public async Task Exec_成功链_建库一次_预批序列_Lib_done_FinishedAt()
    {
        var root = NewRoot();
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.CreateRepoHandler = req => new YunxiaoRepository
        {
            Id = 999L,
            Name = req.Name
            // 不给 HttpUrlToRepo：保持既有「兜底拼装克隆地址」路径，指令文本断言不受影响
        };

        var exec = await h.Service.ExecAsync(
            Exec(groupNewName: "DEMOGRP", visibility: "private", description: "合成演示库"),
            CancellationToken.None);

        Assert.IsTrue(exec.Ok);
        var task = await WaitTerminalAsync(h.Service, exec.Data!);

        Assert.AreEqual(InitTaskStatus.Done, task.Status);
        Assert.IsNotNull(task.FinishedAt);
        Assert.AreEqual("sess-fake", task.SessionId);
        // 契约缺口修复钉子：建库成功回填 LibState.RepoId（供流水线区 newRepoId）
        Assert.AreEqual("999", task.Libs.Single().RepoId);

        // CreateRepository 一次且参数正确；同名组复用（DEMOGRP → Id 11，不建组）
        Assert.AreEqual(1, h.Codeup.CreateRepoRequests.Count);
        var created = h.Codeup.CreateRepoRequests[0];
        Assert.AreEqual("NEWX-Mixed", created.Name);
        Assert.AreEqual("private", created.Visibility);
        Assert.AreEqual("合成演示库", created.Description);
        Assert.AreEqual(11L, created.NamespaceId);
        Assert.IsFalse(h.Codeup.WriteCalls.Any(c => c.StartsWith("CreateNamespace:", StringComparison.Ordinal)));

        // SendAsync 收到的 approvedCommands = GitCommandPlanner 同源序列（含 S2c §12.2 尾部切 develop 步）
        Assert.AreEqual(1, h.Gateway.Sends.Count);
        var (sessionId, instruction, approved) = h.Gateway.Sends[0];
        Assert.AreEqual("sess-fake", sessionId);
        var expected = GitCommandPlanner.PlanCloneRenamePush(
            root,
            TemplateCloneUrl,
            "https://codeup.aliyun.com/DEMOGRP/NEWX-Mixed.git",
            "TPLX",
            "NEWX",
            new[] { "NEWX-Admin", "NEWX-Mobile", "NEWX-Service" },
            "master").ToList();
        expected.Add(GitCommandPlanner.PlanSwitchDevelopLocal($"{root}/NEWX-Mixed"));
        CollectionAssert.AreEqual(expected.Select(s => s.Command).ToList(), approved!.ToList());
        StringAssert.Contains(approved[^1], "checkout -b develop");

        // instruction 明示将执行的命令序列（与 GitCommands 同源）且无凭据形态
        foreach (var step in expected)
        {
            StringAssert.Contains(instruction, step.Command);
        }

        Assert.IsFalse(instruction.Contains("pt-", StringComparison.Ordinal), instruction);
        Assert.AreEqual(LibStatus.Done, task.Libs.Single().Status);
    }

    [TestMethod]
    public async Task Exec_Send抛异常_Lib失败_Detail脱敏无pt()
    {
        var root = NewRoot();
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Gateway.SendHandler = (_, _, _) => throw new InvalidOperationException("远端拒绝：凭据 pt-FAKE9988TOKEN 失效");

        var exec = await h.Service.ExecAsync(Exec(), CancellationToken.None);
        Assert.IsTrue(exec.Ok);
        var task = await WaitTerminalAsync(h.Service, exec.Data!);

        var lib = task.Libs.Single();
        Assert.AreEqual(LibStatus.Failed, lib.Status);
        Assert.IsNotNull(lib.Detail);
        Assert.IsFalse(lib.Detail!.Contains("pt-", StringComparison.Ordinal), lib.Detail);
        StringAssert.Contains(lib.Detail, "远端拒绝");
        Assert.IsTrue(lib.Detail.Length <= 200);
        Assert.AreEqual(InitTaskStatus.Failed, task.Status);
    }

    [TestMethod]
    public async Task Exec_Session不可用_503_零写调用()
    {
        var root = NewRoot();
        var h = Create(root);
        h.Gateway.ProbeResult = false;

        var exec = await h.Service.ExecAsync(Exec(), CancellationToken.None);

        Assert.IsFalse(exec.Ok);
        Assert.AreEqual(503, exec.Code);
        Assert.IsNotNull(exec.Message);
        Assert.AreEqual(0, h.Codeup.WriteCalls.Count);
        Assert.AreEqual(0, h.Flow.WriteCalls.Count);
        Assert.AreEqual(0, h.Gateway.WriteCalls.Count);
    }

    [TestMethod]
    public async Task Retry_只重跑failed库_done库不动_幂等不重复建库()
    {
        var root = NewRoot();
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("TPL2-Mixed", "https://codeup.example.com/org-demo/tpl2-mixed.git"));

        var calls = 0;
        h.Gateway.SendHandler = (_, _, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("会话中断");
            }

            return Task.CompletedTask;
        };

        var exec = await h.Service.ExecAsync(Exec(libs:
        [
            new ExecLibSpec(TemplateRepoName, "NEWX-Mixed"),
            new ExecLibSpec("TPL2-Mixed", "NEWX2-Mixed"),
        ]), CancellationToken.None);
        var task = await WaitTerminalAsync(h.Service, exec.Data!);
        Assert.AreEqual(LibStatus.Failed, task.Libs.Single(l => l.Repo == "NEWX-Mixed").Status);
        Assert.AreEqual(LibStatus.Done, task.Libs.Single(l => l.Repo == "NEWX2-Mixed").Status);
        Assert.AreEqual(2, h.Codeup.CreateRepoRequests.Count);

        // 重跑：只动 failed 库；已建库复用不重复建
        var retry = await h.Service.RetryAsync(exec.Data!, ["NEWX-Mixed"], CancellationToken.None);
        Assert.IsTrue(retry.Ok);
        Assert.AreEqual(exec.Data, retry.Data);
        var retried = await WaitTerminalAsync(h.Service, exec.Data!);
        Assert.AreEqual(LibStatus.Done, retried.Libs.Single(l => l.Repo == "NEWX-Mixed").Status);
        Assert.AreEqual(LibStatus.Done, retried.Libs.Single(l => l.Repo == "NEWX2-Mixed").Status);
        Assert.AreEqual(2, h.Codeup.CreateRepoRequests.Count);   // 不重复建库
        Assert.AreEqual(3, h.Gateway.Sends.Count);               // done 库未重发

        // 再次 Retry：非 failed 不动，零新增调用（幂等）
        var again = await h.Service.RetryAsync(exec.Data!, ["NEWX-Mixed"], CancellationToken.None);
        Assert.IsTrue(again.Ok);
        Assert.AreEqual(2, h.Codeup.CreateRepoRequests.Count);
        Assert.AreEqual(3, h.Gateway.Sends.Count);
    }

    [TestMethod]
    public async Task Retry_任务或库不存在_404语义()
    {
        var h = Create(Path.GetTempPath());
        var missingTask = await h.Service.RetryAsync("no-such-task", ["NEWX-Mixed"], CancellationToken.None);
        Assert.IsFalse(missingTask.Ok);
        Assert.AreEqual(404, missingTask.Code);

        var root = NewRoot();
        var h2 = Create(root);
        h2.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        var exec = await h2.Service.ExecAsync(Exec(), CancellationToken.None);
        await WaitTerminalAsync(h2.Service, exec.Data!);

        var missingLib = await h2.Service.RetryAsync(exec.Data!, ["NOT-THERE-Mixed"], CancellationToken.None);
        Assert.IsFalse(missingLib.Ok);
        Assert.AreEqual(404, missingLib.Code);
    }

    [TestMethod]
    public async Task GetTask_未知id_404_回读字段一致()
    {
        var root = NewRoot();
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        var missing = await h.Service.GetTaskAsync("no-such-task", CancellationToken.None);
        Assert.IsFalse(missing.Ok);
        Assert.AreEqual(404, missing.Code);

        var exec = await h.Service.ExecAsync(Exec(), CancellationToken.None);
        await WaitTerminalAsync(h.Service, exec.Data!);

        var result = await h.Service.GetTaskAsync(exec.Data!, CancellationToken.None);
        Assert.IsTrue(result.Ok);
        var task = result.Data!;
        Assert.AreEqual(exec.Data, task.Id);
        Assert.AreEqual(NewCode, task.Codename);
        Assert.AreEqual(InitTaskStatus.Done, task.Status);
        Assert.AreEqual("ws-1", task.WorkspaceId);
        Assert.AreEqual("sess-fake", task.SessionId);
        Assert.IsTrue(task.CreatedAt > DateTime.MinValue);
        Assert.IsNotNull(task.FinishedAt);
        Assert.AreEqual(LibStatus.Done, task.Libs.Single().Status);
    }

    [TestMethod]
    public async Task Exec_Retry_GetTask_非法参数_400语义()
    {
        var root = NewRoot();
        var h = Create(root);

        Assert.AreEqual(400, (await h.Service.ExecAsync(Exec(newCode: null), CancellationToken.None)).Code);
        Assert.AreEqual(400, (await h.Service.ExecAsync(Exec(workspaceId: null), CancellationToken.None)).Code);
        Assert.AreEqual(400, (await h.Service.ExecAsync(Exec(branch: null), CancellationToken.None)).Code);
        Assert.AreEqual(400, (await h.Service.ExecAsync(Exec(libs: []), CancellationToken.None)).Code);
        Assert.AreEqual(400, (await h.Service.ExecAsync(
            Exec(libs: [new ExecLibSpec("NOTMIXED")]), CancellationToken.None)).Code);

        Assert.AreEqual(400, (await h.Service.GetTaskAsync("", CancellationToken.None)).Code);
        Assert.AreEqual(400, (await h.Service.RetryAsync("", ["NEWX-Mixed"], CancellationToken.None)).Code);
        Assert.AreEqual(400, (await h.Service.RetryAsync("t", [], CancellationToken.None)).Code);
    }

    [TestMethod]
    public async Task Exec_后台同步异常_安全收口落failed不逃逸()
    {
        var root = NewRoot();
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Gateway.CreateSessionHandler = () => throw new InvalidOperationException("会话服务爆炸 pt-BOOM12345");

        var exec = await h.Service.ExecAsync(Exec(), CancellationToken.None);
        Assert.IsTrue(exec.Ok);   // 秒回不受后台异常影响

        var task = await WaitTerminalAsync(h.Service, exec.Data!);
        Assert.AreEqual(InitTaskStatus.Failed, task.Status);
        Assert.IsNotNull(task.FinishedAt);
        var lib = task.Libs.Single();
        Assert.AreEqual(LibStatus.Failed, lib.Status);
        Assert.IsNotNull(lib.Detail);
        Assert.IsFalse(lib.Detail!.Contains("pt-", StringComparison.Ordinal), lib.Detail);
    }

    [TestMethod]
    public async Task Exec_无同名代码组_建组并挂新建库()
    {
        var root = NewRoot();
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        var exec = await h.Service.ExecAsync(Exec(groupNewName: "NEWGRP"), CancellationToken.None);
        var task = await WaitTerminalAsync(h.Service, exec.Data!);

        Assert.AreEqual(InitTaskStatus.Done, task.Status);
        Assert.IsTrue(h.Codeup.WriteCalls.Contains("CreateNamespace:NEWGRP"));
        Assert.AreEqual("NEWGRP", h.Codeup.CreateNamespaceRequests.Single().Name);
        Assert.AreEqual(99L, h.Codeup.CreateRepoRequests.Single().NamespaceId);
    }

    // ── 任务 8b：FR-3 流水线预览 / 提交 ───────────────────────────────────

    /// <summary>合成母版 YAML（占位符形态，纯合成无真值）。</summary>
    private const string TemplateYaml =
        "repo: {{REPO_NAME}}\n" +
        "repoId: {{REPO_ID}}\n" +
        "workDir: {{WORK_DIR}}\n" +
        "branch: {{BRANCH}}\n" +
        "port: {{PORT}}\n" +
        "path: TPLX-Admin\n" +
        "stages:\n" +
        "  - stage: build\n";

    /// <summary>合成 tb 形态（无顶层 stages:，不可作 PIPELINEASCODE 源）。</summary>
    private const string TbFormFlow = "steps:\n  - name: build\n    type: console\n";

    /// <summary>合成流水线条目。</summary>
    private static YunxiaoPipeline Pipeline(string name, long id, string flow)
        => new()
        {
            PipelineId = id,
            PipelineName = name,
            PipelineConfig = new YunxiaoPipelineConfig { Flow = flow },
        };

    [TestMethod]
    public async Task Preview_反查命名匹配_逐端返回diff()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.AddRange(
        [
            Pipeline("ACME_DEV_Deploy_TPLX-Mobile", 101, TemplateYaml),
            Pipeline("ACME_DEV_Deploy_TPLX-Admin", 102, TemplateYaml),
            Pipeline("ACME_DEV_Deploy_TPLX-Service", 103, TemplateYaml),
            Pipeline("OTHER_PIPELINE", 104, TemplateYaml),
        ]);
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            "rid-1", "NEWX-Mixed", "TPLX", "NEWX",
            [
                new EndConfig("mobile", true, "/w/mob", "ACME_DEV_Deploy_NEWX-Mobile"),
                new EndConfig("admin", true, "/w/adm", "ACME_DEV_Deploy_NEWX-Admin"),
                new EndConfig("backend", true, "/w/svc", "ACME_DEV_Deploy_NEWX-Service", 9000),
                new EndConfig("extra", false, "/w/x", "X"),   // 未启用不参与
            ]), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var items = result.Data!.Items;
        Assert.AreEqual(3, items.Count);
        CollectionAssert.AreEquivalent(
            new[] { "ACME_DEV_Deploy_NEWX-Mobile", "ACME_DEV_Deploy_NEWX-Admin", "ACME_DEV_Deploy_NEWX-Service" },
            items.Select(i => i.Name).ToList());
        foreach (var item in items)
        {
            Assert.IsNull(item.Warning);
            StringAssert.Contains(item.Diff, "+path: NEWX-Admin");
        }
    }

    /// <summary>tb 形态（表单流水线，无顶层 stages:）不再被跳过——YamlSubstitutor 做纯文本替换，同样能生成 diff。</summary>
    [TestMethod]
    public async Task Preview_非PIPELINEASCODE_照样替换与diff()
    {
        var h = Create(Path.GetTempPath());
        // tb 形态 fixture，含代号段 "TPLX"，Substitute 应该替换成 NEWX
        const string TbFlowWithCode = "name: build_TPLX\nsteps:\n  - name: TPLX-Admin\n    type: console\n";
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_TPLX-Mobile", 101, TbFlowWithCode));
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            "rid-1", "NEWX-Mixed", "TPLX", "NEWX",
            [new EndConfig("mobile", true, "/w/mob", "ACME_DEV_Deploy_NEWX-Mobile")]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var item = result.Data!.Items.Single();
        Assert.IsNull(item.Warning, "tb 形态不再被标记跳过——无警告");
        Assert.IsTrue(item.Content!.Contains("NEWX"), "替换后的 content 应含新代号 NEWX");
        Assert.IsTrue(item.Diff.Length > 0, "tb 形态 YamlSubstitutor 做了替换 → 应有 diff");
    }

    [TestMethod]
    public async Task Preview_替换与diff_串测占位符与代号段()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_TPLX-Service", 103, TemplateYaml));
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            "rid-1", "NEWX-Mixed", "TPLX", "NEWX",
            [new EndConfig("backend", true, "/w/svc", "ACME_DEV_Deploy_NEWX-Service", 9000)]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var diff = result.Data!.Items.Single().Diff;

        // - 行是母版原样，+ 行是替换后
        StringAssert.Contains(diff, "-repo: {{REPO_NAME}}");
        StringAssert.Contains(diff, "+repo: NEWX-Mixed");
        StringAssert.Contains(diff, "+repoId: rid-1");
        StringAssert.Contains(diff, "+workDir: /w/svc");
        StringAssert.Contains(diff, "+branch: master");
        StringAssert.Contains(diff, "+port: 9000");
        StringAssert.Contains(diff, "+path: NEWX-Admin");

        // 未变更行不进 diff 块
        Assert.IsFalse(diff.Contains("stages:", StringComparison.Ordinal), diff);

        // 契约缺口修复钉子：预览带回替换后 content（提交用），前端无需由 diff 重建
        var content = result.Data!.Items.Single().Content;
        Assert.IsNotNull(content);
        StringAssert.Contains(content!, "repo: NEWX-Mixed");
        StringAssert.Contains(content!, "port: 9000");
        Assert.IsFalse(content!.Contains("{{REPO_NAME}}", StringComparison.Ordinal), content);
        StringAssert.Contains(content!, "stages:");
    }

    [TestMethod]
    public async Task Preview_手选TemplatePipelineName_兜底反查()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("CUSTOM-NAME-PIPE", 101, TemplateYaml));
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            "rid-1", "NEWX-Mixed", "TPLX", "NEWX",
            [new EndConfig("mobile", true, "/w/mob", "ACME_DEV_Deploy_NEWX-Mobile", TemplatePipelineName: "CUSTOM-NAME-PIPE")]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var item = result.Data!.Items.Single();
        Assert.IsNull(item.Warning);
        StringAssert.Contains(item.Diff, "+repo: NEWX-Mixed");
    }

    [TestMethod]
    public async Task Preview_Exists标注_派生名精确命中OrdinalIgnoreCase才true()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_TPLX-Mobile", 101, TemplateYaml));
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_NEWX-Mobile", 102, TemplateYaml));
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            "rid-1", "NEWX-Mixed", "TPLX", "NEWX",
            [
                // 派生名大小写变体命中（OrdinalIgnoreCase）→ Exists
                new EndConfig("mobile", true, "/w/mob", "ACME_DEV_Deploy_NEWX-MOBILE"),
                // 派生名缺失 → Exists=false（提交将新建）
                new EndConfig("backend", true, "/w/svc", "ACME_DEV_Deploy_NEWX-Service", 9000),
            ]), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var items = result.Data!.Items;
        Assert.IsTrue(items.Single(i => i.End == "mobile").Exists);
        Assert.IsFalse(items.Single(i => i.End == "backend").Exists);
    }

    [TestMethod]
    public async Task Preview_前缀容忍端口差异_互认Exists()
    {
        var h = Create(Path.GetTempPath());
        // 远端已有名带 (7001)，派生名带 (9000) → 剥离 (端口) 后前缀一致 → 互认 Exists（S2c §12.3）
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_NEWX-Service(7001)", 201, TemplateYaml));
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            "rid-1", "NEWX-Mixed", "TPLX", "NEWX",
            [new EndConfig("backend", true, "/w/svc", "ACME_DEV_Deploy_NEWX-Service(9000)", 9000)]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var item = result.Data!.Items.Single();
        Assert.IsTrue(item.Exists, "(7001) vs (9000) 前缀容忍应互认 Exists");
    }

    [TestMethod]
    public async Task Preview_PipelineInfo_回显真实名Id与解析端口()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_NEWX-Service(7001)", 201, TemplateYaml));
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            "rid-1", "NEWX-Mixed", "TPLX", "NEWX",
            [new EndConfig("backend", true, "/w/svc", "ACME_DEV_Deploy_NEWX-Service(9000)", 9000)]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var info = result.Data!.Items.Single().Pipeline;
        Assert.IsNotNull(info, "已存在端应回填 PipelineInfo");
        Assert.AreEqual("201", info!.Id);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Service(7001)", info.ActualName);
        Assert.AreEqual(7001, info.Port);   // 从 ActualName 的 (数字) 解析
    }

    [TestMethod]
    public async Task Preview_PipelineInfo_解析不出端口为null()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_NEWX-Service", 201, TemplateYaml));
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            "rid-1", "NEWX-Mixed", "TPLX", "NEWX",
            [new EndConfig("backend", true, "/w/svc", "ACME_DEV_Deploy_NEWX-Service", 9000)]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var info = result.Data!.Items.Single().Pipeline;
        Assert.IsNotNull(info);
        Assert.IsNull(info!.Port);   // 无 (端口) 后缀 → 解析不出为 null
    }

    [TestMethod]
    public async Task Preview_全复用NewRepoId可空_按库名回查真实id()
    {
        var h = Create(Path.GetTempPath());
        h.Codeup.Repos.Add(new YunxiaoRepository
        {
            Id = 555L,
            Name = "NEWX-Mixed",
            Path = "newx-mixed",
            HttpUrlToRepo = "https://codeup.example.com/org-demo/newx-mixed.git",
        });
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_TPLX-Service", 103, TemplateYaml));
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        // S2c §12.3.5：全复用 F6 不等 FR-2 任务 → NewRepoId 留空，按 NewRepoName 回查
        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            null, "NEWX-Mixed", "TPLX", "NEWX",
            [new EndConfig("backend", true, "/w/svc", "ACME_DEV_Deploy_NEWX-Service", 9000)]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        // {{REPO_ID}} 用回查的真实 id（555）
        StringAssert.Contains(result.Data!.Items.Single().Diff, "+repoId: 555");
    }

    [TestMethod]
    public async Task Preview_全复用NewRepoId可空_按库名查不到_404()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_TPLX-Service", 103, TemplateYaml));
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            null, "NOT-THERE-Mixed", "TPLX", "NEWX",
            [new EndConfig("backend", true, "/w/svc", "ACME_DEV_Deploy_NEWX-Service", 9000)]),
            CancellationToken.None);

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(404, result.Code);
    }

    [TestMethod]
    public async Task Commit_create_调用参数正确_返回pipelineId()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.CreateHandler = req => "id-" + req.PipelineName;
        const string content = "stages:\n  - stage: a\n";

        var result = await h.Service.CommitPipelinesAsync(new PipelineCommitRequest(
            [new CommitItem("mobile", "create", "ACME_DEV_Deploy_NEWX-Mobile", content)]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var item = result.Data!.Results.Single();
        Assert.AreEqual("mobile", item.End);
        Assert.IsTrue(item.Ok);
        Assert.AreEqual("id-ACME_DEV_Deploy_NEWX-Mobile", item.PipelineId);
        Assert.IsNull(item.Message);

        var call = h.Flow.CreateCalls.Single();
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Mobile", call.PipelineName);
        Assert.AreEqual(content, call.Content);
    }

    [TestMethod]
    public async Task Commit_SkipIfExisting_已存在端零写调用_回执带Id()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_NEWX-Mobile", 201, TemplateYaml));

        var result = await h.Service.CommitPipelinesAsync(new PipelineCommitRequest(
            [new CommitItem("mobile", "create", "ACME_DEV_Deploy_NEWX-Mobile", "stages:\n  - a\n")]),
            CancellationToken.None);

        // S2c §12.3.4：已存在端零操作，回执「已存在·已同步信息」（含 Id/ActualName，可追溯）
        Assert.IsTrue(result.Ok);
        var item = result.Data!.Results.Single();
        Assert.AreEqual("mobile", item.End);
        Assert.IsTrue(item.Ok);
        StringAssert.Contains(item.Message!, "已存在·已同步信息");
        StringAssert.Contains(item.Message!, "ACME_DEV_Deploy_NEWX-Mobile");
        Assert.AreEqual("201", item.PipelineId);   // 回执带 Id
        Assert.AreEqual(0, h.Flow.WriteCalls.Count);
        Assert.AreEqual(0, h.Flow.CreateCalls.Count);
    }

    [TestMethod]
    public async Task Commit_SkipIfExistingFalse_显式create仍建()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_NEWX-Mobile", 201, TemplateYaml));
        h.Flow.CreateHandler = _ => "id-new";

        var result = await h.Service.CommitPipelinesAsync(new PipelineCommitRequest(
            [new CommitItem("mobile", "create", "ACME_DEV_Deploy_NEWX-Mobile", "stages:\n  - a\n", SkipIfExisting: false)]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        Assert.IsTrue(result.Data!.Results.Single().Ok);
        Assert.AreEqual(1, h.Flow.CreateCalls.Count);
    }

    [TestMethod]
    public async Task Commit_补全与覆盖混提_缺失端create_已有端跳过()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_NEWX-Mobile", 201, TemplateYaml));
        h.Flow.CreateHandler = req => "id-" + req.PipelineName;

        var result = await h.Service.CommitPipelinesAsync(new PipelineCommitRequest(
        [
            new CommitItem("mobile", "create", "ACME_DEV_Deploy_NEWX-Mobile", "stages:\n"),
            new CommitItem("admin", "create", "ACME_DEV_Deploy_NEWX-Admin", "stages:\n"),
        ]), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var results = result.Data!.Results;
        StringAssert.Contains(results[0].Message!, "已存在·已同步信息");
        Assert.AreEqual("201", results[0].PipelineId);   // 已存在端回执带 Id
        Assert.IsTrue(results[1].Ok);
        Assert.AreEqual("id-ACME_DEV_Deploy_NEWX-Admin", results[1].PipelineId);
        Assert.AreEqual(1, h.Flow.CreateCalls.Count);   // 只建缺失端
    }

    [TestMethod]
    public async Task Commit_update无Confirm_409且零调用()
    {
        var h = Create(Path.GetTempPath());

        var result = await h.Service.CommitPipelinesAsync(new PipelineCommitRequest(
            [new CommitItem("mobile", "update", "ACME_DEV_Deploy_NEWX-Mobile", "stages:\n", PipelineId: "201")]),
            CancellationToken.None);

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(409, result.Code);
        StringAssert.Contains(result.Message!, "confirm");
        Assert.AreEqual(0, h.Flow.WriteCalls.Count);
        Assert.AreEqual(0, h.Flow.CreateCalls.Count);
        Assert.AreEqual(0, h.Flow.UpdateCalls.Count);
    }

    /// <summary>tb 形态（无顶层 stages:）不再被 400 拒绝——后端不再校验 stages，tb 形态也能提交。</summary>
    [TestMethod]
    public async Task Commit_content无stages_照样create成功()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.CreateHandler = req => "id-tb-pipeline";

        var result = await h.Service.CommitPipelinesAsync(new PipelineCommitRequest(
            [new CommitItem("mobile", "create", "N", TbFormFlow)]),
            CancellationToken.None);

        // 新口径：tb 形态不再 400，后端会直接调 create
        Assert.IsTrue(result.Ok, "tb 形态不再被拒绝 → create 成功");
        Assert.AreEqual(1, h.Flow.WriteCalls.Count, "应有 1 次 create 调用");
    }

    [TestMethod]
    public async Task Commit_逐端独立_一端抛异常他端照常()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.CreateHandler = req => req.PipelineName == "BAD"
            ? throw new InvalidOperationException("接口爆炸 pt-LEAK1234")
            : "id-" + req.PipelineName;

        var result = await h.Service.CommitPipelinesAsync(new PipelineCommitRequest(
        [
            new CommitItem("mobile", "create", "BAD", "stages:\n"),
            new CommitItem("admin", "create", "GOOD", "stages:\n"),
        ]), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var results = result.Data!.Results;
        Assert.AreEqual(2, results.Count);
        Assert.IsFalse(results[0].Ok);
        Assert.IsNotNull(results[0].Message);
        Assert.IsFalse(results[0].Message!.Contains("pt-", StringComparison.Ordinal), results[0].Message);
        Assert.IsTrue(results[1].Ok);
        Assert.AreEqual("id-GOOD", results[1].PipelineId);
    }

    [TestMethod]
    public async Task Commit_结果回读字段_create与update()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.CreateHandler = _ => "pid-1";
        h.Flow.UpdateHandler = _ => true;
        const string content = "stages:\n  - stage: z\n";

        var result = await h.Service.CommitPipelinesAsync(new PipelineCommitRequest(
        [
            new CommitItem("mobile", "create", "PIPE-A", content),
            new CommitItem("backend", "update", "PIPE-B", content, Confirm: true, PipelineId: "201"),
        ]), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var results = result.Data!.Results;
        Assert.IsTrue(results[0].Ok);
        Assert.AreEqual("pid-1", results[0].PipelineId);
        Assert.IsTrue(results[1].Ok);
        Assert.AreEqual("201", results[1].PipelineId);

        var update = h.Flow.UpdateCalls.Single();
        Assert.AreEqual("201", update.PipelineId);
        Assert.AreEqual("PIPE-B", update.Request.PipelineName);
        Assert.AreEqual(content, update.Request.Content);
    }

    [TestMethod]
    public async Task Commit_update返回false_该端Ok为false他端不受累()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.CreateHandler = _ => "pid-1";
        h.Flow.UpdateHandler = _ => false;

        var result = await h.Service.CommitPipelinesAsync(new PipelineCommitRequest(
        [
            new CommitItem("backend", "update", "PIPE-B", "stages:\n", Confirm: true, PipelineId: "201"),
            new CommitItem("mobile", "create", "PIPE-A", "stages:\n"),
        ]), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var results = result.Data!.Results;
        Assert.IsFalse(results[0].Ok);
        Assert.IsNotNull(results[0].Message);
        Assert.IsTrue(results[1].Ok);
    }

    // ── S2d §13.3：后缀段匹配兜底 + 预检缺省名反查（前缀不写死，夹具合成 ACME_） ──

    [TestMethod]
    public async Task Preview_空前缀表单名_后缀段命中ACME真名_互认Exists()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_NEWX-Mobile", 102, TemplateYaml));
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        // 表单名空前缀（_DEV_Deploy_{代号}-{端}）与真名 ACME_DEV_Deploy_* 互认（§13.3 匹配兜底）
        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            "rid-1", "NEWX-Mixed", "TPLX", "NEWX",
            [new EndConfig("mobile", true, "/w/mob", "_DEV_Deploy_NEWX-Mobile")]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var item = result.Data!.Items.Single();
        Assert.IsTrue(item.Exists, "空前缀表单名应与 ACME_DEV_Deploy_* 真名后缀段互认");
        Assert.IsNotNull(item.Pipeline);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Mobile", item.Pipeline!.ActualName);
    }

    [TestMethod]
    public async Task Preview_空前缀容忍端口差异组合_互认Exists()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_NEWX-Service(7001)", 201, TemplateYaml));
        h.Flow.GetPipelineHandler = id => h.Flow.Pipelines.FirstOrDefault(p => p.DisplayId == id);

        // 空前缀 + (9000) vs ACME 前缀 + (7001)：前缀容忍与端口容忍组合互认
        var result = await h.Service.PreviewPipelinesAsync(new PipelinePreviewRequest(
            "rid-1", "NEWX-Mixed", "TPLX", "NEWX",
            [new EndConfig("backend", true, "/w/svc", "_DEV_Deploy_NEWX-Service(9000)", 9000)]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        Assert.IsTrue(result.Data!.Items.Single().Exists, "(9000) 空前缀 vs (7001) ACME 前缀组合应互认");
    }

    [TestMethod]
    public async Task Commit_SkipIfExisting_空前缀_后缀段命中TOCTOU_跳过零写调用()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_NEWX-Mobile", 201, TemplateYaml));

        var result = await h.Service.CommitPipelinesAsync(new PipelineCommitRequest(
            [new CommitItem("mobile", "create", "_DEV_Deploy_NEWX-Mobile", "stages:\n  - a\n")]),
            CancellationToken.None);

        // TOCTOU 复查后缀段命中 → 已存在端零操作，回执「已存在·已同步信息」（含 Id/真实名）
        Assert.IsTrue(result.Ok);
        var item = result.Data!.Results.Single();
        Assert.IsTrue(item.Ok);
        StringAssert.Contains(item.Message!, "已存在·已同步信息");
        StringAssert.Contains(item.Message!, "ACME_DEV_Deploy_NEWX-Mobile");
        Assert.AreEqual("201", item.PipelineId);
        Assert.AreEqual(0, h.Flow.WriteCalls.Count);
        Assert.AreEqual(0, h.Flow.CreateCalls.Count);
    }

    [TestMethod]
    public async Task Preflight_母版流水线名缺省_反查真实名带前缀再派生()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.AddRange(
        [
            Pipeline("ACME_DEV_Deploy_TPLX-Mobile", 101, TemplateYaml),
            Pipeline("ACME_DEV_Deploy_TPLX-Service(7001)", 103, TemplateYaml),
        ]);

        // 母版流水线名缺省（[]）→ 反查 ListPipelinesAsync 母版后缀段真实名（带 ACME_ 前缀）再派生
        var result = await h.Service.PreflightAsync(new PreflightRequest(
            TemplateRepoName, NewCode, "ws-1", "master",
            [],
            Ends:
            [
                new InitEndConfig("mobile", true),
                new InitEndConfig("backend", true, Port: 9000),
            ]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var pipes = result.Data!.Plan.Pipelines;
        Assert.AreEqual(2, pipes.Count);
        Assert.AreEqual("ACME_DEV_Deploy_TPLX-Mobile", pipes[0].From);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Mobile", pipes[0].To);
        Assert.AreEqual("ACME_DEV_Deploy_TPLX-Service(7001)", pipes[1].From);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Service(9000)", pipes[1].To);
    }

    [TestMethod]
    public async Task Preflight_母版流水线名已给齐_不反查ListPipelines()
    {
        var h = Create(Path.GetTempPath());

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        // 既有口径：三名已覆盖三端 → 零 ListPipelines 反查（不为命名额外打扰流水线接口）
        Assert.IsTrue(result.Ok);
        Assert.AreEqual(3, result.Data!.Plan.Pipelines.Count);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Mobile", result.Data!.Plan.Pipelines[0].To);
        Assert.AreEqual(0, h.Flow.ListPipelineCalls.Count);
    }

    // ── S2e §14 修复一：DerivePipelinesAsync（反查真名 → 逐端派生，永不前导下划线）──

    [TestMethod]
    public async Task DerivePipelines_反查到真名_FromTo带真实前缀()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_TPLX-Mobile", 101, TemplateYaml));

        var result = await h.Service.DerivePipelinesAsync(new DeriveRequest(
            TemplateRepoName, NewCode,
            [new InitEndConfig("mobile", true)]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var item = result.Data!.Items.Single();
        Assert.AreEqual("mobile", item.End);
        Assert.AreEqual("ACME_DEV_Deploy_TPLX-Mobile", item.From);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Mobile", item.To);
        Assert.IsFalse(item.To.StartsWith("_", StringComparison.Ordinal), item.To);
        Assert.AreEqual(1, h.Flow.ListPipelineCalls.Count);
    }

    [TestMethod]
    public async Task DerivePipelines_反查不到_To无前导下划线兜底()
    {
        var h = Create(Path.GetTempPath());   // Flow.Pipelines 为空 → ListPipelinesAsync 返回 null

        var result = await h.Service.DerivePipelinesAsync(new DeriveRequest(
            TemplateRepoName, NewCode,
            [new InitEndConfig("mobile", true), new InitEndConfig("admin", true)]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        Assert.AreEqual(2, result.Data!.Items.Count);
        foreach (var item in result.Data.Items)
        {
            Assert.IsNull(item.From, $"{item.End} 反查不到应为 null");
            Assert.AreEqual($"DEV_Deploy_NEWX-{(item.End == "mobile" ? "Mobile" : "Admin")}", item.To);
            Assert.IsFalse(item.To.StartsWith("_", StringComparison.Ordinal), item.To);
        }
    }

    [TestMethod]
    public async Task DerivePipelines_后端端口后缀含port_仅On端出项()
    {
        var h = Create(Path.GetTempPath());
        h.Flow.Pipelines.Add(Pipeline("ACME_DEV_Deploy_TPLX-Service(7001)", 103, TemplateYaml));

        var result = await h.Service.DerivePipelinesAsync(new DeriveRequest(
            TemplateRepoName, NewCode,
            [
                new InitEndConfig("mobile", false),                              // 未勾选 → 不出项
                new InitEndConfig("backend", true, Port: 9000),
            ]),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var item = result.Data!.Items.Single();
        Assert.AreEqual("backend", item.End);
        Assert.AreEqual("ACME_DEV_Deploy_TPLX-Service(7001)", item.From);
        Assert.AreEqual("ACME_DEV_Deploy_NEWX-Service(9000)", item.To);   // 端口后缀含 (port)
    }

    [TestMethod]
    public async Task DerivePipelines_Ends缺省_三端全开_反查不到兜底无前导下划线()
    {
        var h = Create(Path.GetTempPath());

        var result = await h.Service.DerivePipelinesAsync(
            new DeriveRequest(TemplateRepoName, NewCode),
            CancellationToken.None);

        Assert.IsTrue(result.Ok);
        Assert.AreEqual(3, result.Data!.Items.Count);
        CollectionAssert.AreEquivalent(
            new[] { "mobile", "admin", "backend" },
            result.Data.Items.Select(i => i.End).ToList());
        Assert.IsTrue(result.Data.Items.All(i => i.To.StartsWith("DEV_Deploy_NEWX-", StringComparison.Ordinal)));
        Assert.IsTrue(result.Data.Items.All(i => !i.To.StartsWith("_", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task DerivePipelines_非法参数_400语义()
    {
        var h = Create(Path.GetTempPath());

        var blank = await h.Service.DerivePipelinesAsync(new DeriveRequest("", NewCode), CancellationToken.None);
        Assert.IsFalse(blank.Ok);
        Assert.AreEqual(400, blank.Code);

        var noDash = await h.Service.DerivePipelinesAsync(new DeriveRequest("NOTMIXED", NewCode), CancellationToken.None);
        Assert.IsFalse(noDash.Ok);
        Assert.AreEqual(400, noDash.Code);

        var badCode = await h.Service.DerivePipelinesAsync(new DeriveRequest(TemplateRepoName, "NEW-X"), CancellationToken.None);
        Assert.IsFalse(badCode.Ok);
        Assert.AreEqual(400, badCode.Code);

        var nullReq = await h.Service.DerivePipelinesAsync(null, CancellationToken.None);
        Assert.IsFalse(nullReq.Ok);
        Assert.AreEqual(400, nullReq.Code);
        Assert.AreEqual(0, h.Flow.ListPipelineCalls.Count);   // 非法参数零反查
    }

    // ── S2e §14 修复三：本地检出判定补形态（根即检出 / 根含 .git）────────────

    [TestMethod]
    public async Task Preflight_工作区根即检出_零git步骤_无未克隆警告()
    {
        // 形态 A：工作区根本身即检出（根目录名与库名规范化相等）——用户报障③核心场景
        // 叶子名恰为库名（父目录带 guid 防串扰）
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"), "NEWX-Mixed");
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Admin"));   // 根非空（检出内容）
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var data = result.Data!;
        Assert.AreEqual(0, data.GitCommands.Count);                       // 零 git 步骤
        Assert.IsFalse((data.Warnings ?? []).Any(w => w.Contains("本地未克隆")));   // 不再误报
        Assert.IsFalse((data.Warnings ?? []).Any(w => w.Contains("结构待校验")));
        Assert.AreEqual(0, data.Conflicts.Count);
    }

    [TestMethod]
    public async Task Preflight_根含git与综合子目录_零git步骤()
    {
        // 形态 B：根含 .git + 综合库特征子目录 ≥2 → 已有源码
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Admin"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mobile"));
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        Assert.AreEqual(0, result.Data!.GitCommands.Count);
        Assert.AreEqual(0, result.Data.Conflicts.Count);
    }

    [TestMethod]
    public async Task Preflight_子目录形态保持_已有源码_零git步骤()
    {
        // 原形态回归：{root}/{repoName} 子目录已有源码 → 零 git（与修复三前行为一致）
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed", "NEWX-Admin"));
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        Assert.AreEqual(0, result.Data!.GitCommands.Count);
    }

    [TestMethod]
    public async Task Preflight_均不命中_出检出序列_原行为()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        // 都不命中 → 新建库出 clone-rename-push 序列（原行为保持）
        Assert.IsTrue(result.Ok);
        Assert.IsTrue(result.Data!.GitCommands.Count > 0);
        StringAssert.Contains(result.Data.GitCommands[0].Command, "git clone");
    }

    [TestMethod]
    public async Task Exec_根即检出_免会话_零Send()
    {
        // 修复三同口径：Exec/RunInit 复用 LocalSourceLayout（根即检出 → 免会话、零 git 零 Send）
        var root = Path.Combine(Path.GetTempPath(), "luban-ex-" + Guid.NewGuid().ToString("N"), "NEWX-Mixed");
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Admin"));
        var h = Create(root);
        h.Gateway.ProbeResult = false;   // 会话不可用：已有源码 → 不阻断
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        var exec = await h.Service.ExecAsync(Exec(), CancellationToken.None);

        Assert.IsTrue(exec.Ok);
        var task = await WaitTerminalAsync(h.Service, exec.Data!);
        Assert.AreEqual(InitTaskStatus.Done, task.Status);
        Assert.AreEqual(0, h.Gateway.Sends.Count);
        Assert.AreEqual(0, h.Codeup.WriteCalls.Count);
    }

    // ── S2e §14 修复四：复用库预检输出结构校验（不输出改名对照）──────────────

    [TestMethod]
    public async Task Preflight_复用库_不出改名对照_出结构校验字段()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed", "NEWX-Admin"));   // 缺 Mobile/Service
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed"));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var data = result.Data!;
        CollectionAssert.AreEqual(new[] { "NEWX-Mixed" }, data.ReusedLibs!.ToList());
        Assert.AreEqual(0, data.Plan.Dirs.Count);   // 复用库无改名动作 → 不输出「将改名目录」对照
        Assert.IsNotNull(data.StructureChecks);
        Assert.IsTrue(data.StructureChecks!.Any(c => c.Contains("NEWX-Admin") && c.Contains("存在")));
        Assert.IsTrue(data.StructureChecks!.Any(c => c.Contains("NEWX-Mobile") && c.Contains("缺失")));
        Assert.IsTrue(data.Warnings!.Any(w => w.Contains("结构待校验")));   // 缺子目录仍出待校验
    }

    [TestMethod]
    public async Task Preflight_复用库_本地未克隆_结构校验待校验()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed"));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var data = result.Data!;
        Assert.AreEqual(0, data.Plan.Dirs.Count);
        Assert.IsTrue(data.StructureChecks!.Any(c => c.Contains("待校验")));
        Assert.IsTrue(data.Warnings!.Any(w => w.Contains("结构待校验")));
    }

    [TestMethod]
    public async Task Preflight_复用库_结构齐全_结构校验全存在_无警告()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed", "NEWX-Admin"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed", "NEWX-Mobile"));
        Directory.CreateDirectory(Path.Combine(root, "NEWX-Mixed", "NEWX-Service"));
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));
        h.Codeup.Repos.Add(Repo("NEWX-Mixed"));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        var data = result.Data!;
        Assert.AreEqual(0, data.Warnings!.Count);
        Assert.AreEqual(3, data.StructureChecks!.Count);
        Assert.IsTrue(data.StructureChecks!.All(c => c.Contains("存在")));
    }

    [TestMethod]
    public async Task Preflight_新建库_结构校验为空_改名对照保持()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var h = Create(root);
        h.Codeup.Repos.Add(Repo(TemplateRepoName, TemplateCloneUrl));

        var result = await h.Service.PreflightAsync(Request(), CancellationToken.None);

        // 新建库：Plan.Dirs 改名对照照旧（3 对），StructureChecks 为空
        Assert.IsTrue(result.Ok);
        var data = result.Data!;
        Assert.AreEqual(3, data.Plan.Dirs.Count);
        Assert.AreEqual(0, (data.StructureChecks ?? []).Count);
        Assert.AreEqual(0, data.ReusedLibs!.Count);
    }
}
