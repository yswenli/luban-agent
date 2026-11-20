/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： PrdS3UnitTest
*描述：S3（PRD 同步 / 拆分 / 落库）纯函数契约单测
*      覆盖：Testhub 请求装配、关系类型校验、拆分输出解析与归一化、
*            验收标准提取、幂等键 / 类型选择、账本文件名净化
*****************************************************************************/

using LubanAgentCore.Connectors.AliyunDevOps;
using LubanAgentWebHost.Services.Prd;

namespace LubanAgent.Tests;

/// <summary>
/// S3 纯函数契约单测：请求装配与拆分归一化。
/// </summary>
/// <remarks>
/// 与 <see cref="ProjexClientUnitTest"/> 同口径：只测 internal static 纯函数，
/// 不引入 HttpMessageHandler 桩；模型调用（<see cref="PrdSplitService"/>）不在此测，
/// 仅用固定 JSON 输入验证「模型输出 → 计划」的容错与截断。
/// </remarks>
[TestClass]
public class PrdS3UnitTest
{
    // ── Testhub：创建测试用例请求装配 ──────────────────────────────────────

    [TestMethod]
    public void BuildCreateTestcaseRequest_UrlContainsTestRepoId_AndBodyMinimal()
    {
        var (url, body) = ProjexClient.BuildCreateTestcaseRequest(
            "org-1",
            new YunxiaoCreateTestcaseRequest
            {
                TestRepoId = "repo/1",
                Subject = "  登录成功  "
            });

        StringAssert.Contains(url, "/testhub/organizations/org-1/testRepos/repo%2F1/testcases");
        Assert.AreEqual("登录成功", body["subject"]);
        Assert.IsFalse(body.ContainsKey("assignedTo"), "未传入时不得写空字段");
        Assert.IsFalse(body.ContainsKey("preCondition"));
        Assert.IsFalse(body.ContainsKey("labels"));
    }

    [TestMethod]
    public void BuildCreateTestcaseRequest_IncludesOptionalFields()
    {
        var (_, body) = ProjexClient.BuildCreateTestcaseRequest(
            "org-1",
            new YunxiaoCreateTestcaseRequest
            {
                TestRepoId = "repo-1",
                Subject = "用例",
                AssignedTo = "user-1",
                DirectoryId = "dir-1",
                PreCondition = "已登录",
                Labels = ["冒烟"],
                TestSteps = [new { step = "打开首页" }]
            });

        Assert.AreEqual("user-1", body["assignedTo"]);
        Assert.AreEqual("dir-1", body["directoryId"]);
        Assert.AreEqual("已登录", body["preCondition"]);
        Assert.IsTrue(body.ContainsKey("labels"));
        Assert.IsTrue(body.ContainsKey("testSteps"));
    }

    [TestMethod]
    public void BuildCreateTestcaseRequest_Throws_WhenSubjectBlank()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ProjexClient.BuildCreateTestcaseRequest(
            "org-1",
            new YunxiaoCreateTestcaseRequest { TestRepoId = "repo-1", Subject = " " }));
    }

    // ── Testhub：测试计划请求装配（未真机验证，锁字段名） ─────────────────

    [TestMethod]
    public void BuildCreateTestPlanRequest_MapsCloudFieldNames()
    {
        var (url, body) = ProjexClient.BuildCreateTestPlanRequest(
            "org-1",
            new YunxiaoCreateTestPlanRequest
            {
                Name = "迭代 1 测试计划",
                ProjectId = "proj-1",
                SprintId = "sprint-1",
                StartDate = "2026-11-20",
                EndDate = "2026-11-30",
                Managers = ["user-1"],
                CaseIds = ["case-1"]
            });

        Assert.AreEqual("/oapi/v1/testhub/organizations/org-1/testPlans", url);
        Assert.AreEqual("迭代 1 测试计划", body["name"]);
        Assert.AreEqual("proj-1", body["spaceIdentifier"]);
        Assert.AreEqual("sprint-1", body["sprintIdentifier"]);
        Assert.AreEqual("2026-11-20", body["startDate"]);
        Assert.AreEqual("2026-11-30", body["endDate"]);
        Assert.IsTrue(body.ContainsKey("managers"));
        Assert.IsTrue(body.ContainsKey("testcases"));
    }

    [TestMethod]
    public void BuildCreateTestPlanRequest_OmitsEmptyOptionalFields()
    {
        var (_, body) = ProjexClient.BuildCreateTestPlanRequest(
            "org-1",
            new YunxiaoCreateTestPlanRequest { Name = "计划" });

        Assert.AreEqual(1, body.Count, "只传 name 时不应带任何可选字段");
    }

    // ── 工作项关系 ────────────────────────────────────────────────────────

    [TestMethod]
    public void BuildCreateRelationRequest_UrlAndBody()
    {
        var (url, body) = ProjexClient.BuildCreateRelationRequest("org-1", "wi-1", "sub", "wi-2");

        Assert.AreEqual("/oapi/v1/projex/organizations/org-1/workitems/wi-1/relationRecords", url);
        Assert.AreEqual("SUB", body["relationType"], "关系类型统一转大写");
        Assert.AreEqual("wi-2", body["workitemId"], "目标工作项字段名为 workitemId");
    }

    [TestMethod]
    public void IsValidRelationType_AcceptsCloudEnum_RejectsUnknown()
    {
        Assert.IsTrue(ProjexClient.IsValidRelationType("PARENT"));
        Assert.IsTrue(ProjexClient.IsValidRelationType("sub"));
        Assert.IsTrue(ProjexClient.IsValidRelationType("ASSOCIATED"));
        Assert.IsTrue(ProjexClient.IsValidRelationType("DEPEND_ON"));
        Assert.IsTrue(ProjexClient.IsValidRelationType("DEPENDED_BY"));
        Assert.IsFalse(ProjexClient.IsValidRelationType("BLOCK"));
        Assert.IsFalse(ProjexClient.IsValidRelationType(null));
    }

    [TestMethod]
    public void BuildListTestReposPath_ClampsPerPage_AndEncodesName()
    {
        var path = ProjexClient.BuildListTestReposPath("org-1", 0, 500, "回归 用例");

        StringAssert.Contains(path, "/testhub/organizations/org-1/testRepo/list?");
        StringAssert.Contains(path, "page=1");
        StringAssert.Contains(path, "perPage=100");
        StringAssert.Contains(path, "name=" + Uri.EscapeDataString("回归 用例"));
    }

    // ── 拆分输出解析 / 归一化 ─────────────────────────────────────────────

    [TestMethod]
    public void Parse_ToleratesCodeFenceAndProse()
    {
        const string output = """
            下面是拆分结果：
            ```json
            {
              "iteration": { "name": "迭代 A", "startDate": "2026-11-20", "endDate": "2026-11-30" },
              "requirements": [ { "key": "R1", "title": "登录", "priority": "p1" } ],
              "tasks": [ { "key": "T1", "requirementKey": "R1", "title": "接口开发" } ],
              "testcases": [ { "key": "C1", "requirementKey": "R1", "title": "登录成功" } ],
              "testplan": { "name": "" }
            }
            ```
            请确认。
            """;

        var (plan, error) = PrdSplitPlanner.Parse(output);

        Assert.IsNull(error);
        Assert.IsNotNull(plan);
        Assert.AreEqual(1, plan!.Requirements.Count);
        Assert.AreEqual("P1", plan.Requirements[0].Priority, "优先级统一大写");
        Assert.AreEqual(1, plan.Tasks.Count);
        Assert.AreEqual("迭代 A 测试计划", plan.TestPlan!.Name, "计划名为空时按迭代名补");
    }

    [TestMethod]
    public void Parse_DropsOrphanTask_AndClearsUnknownCaseRequirement()
    {
        const string output = """
            {
              "requirements": [ { "title": "需求 A" } ],
              "tasks": [ { "title": "孤立任务", "requirementKey": "R9" } ],
              "testcases": [ { "title": "用例", "requirementKey": "R9" } ]
            }
            """;

        var (plan, error) = PrdSplitPlanner.Parse(output);

        Assert.IsNull(error);
        Assert.IsNotNull(plan);
        Assert.AreEqual("R1", plan!.Requirements[0].Key, "缺省键按 R{序号} 补");
        Assert.AreEqual(0, plan.Tasks.Count, "未关联有效需求的任务应被丢弃");
        Assert.IsNull(plan.Testcases[0].RequirementKey, "不存在的需求关联应被清空");
        Assert.AreEqual(2, plan.Warnings.Count);
    }

    [TestMethod]
    public void Parse_ReturnsError_WhenRequirementsEmpty()
    {
        var (plan, error) = PrdSplitPlanner.Parse("""{ "tasks": [ { "title": "任务" } ] }""");

        Assert.IsNull(plan);
        StringAssert.Contains(error, "未解析出有效需求");
    }

    [TestMethod]
    public void Parse_TruncatesOverLimit()
    {
        var requirements = string.Join(",", Enumerable.Range(1, PrdSplitPlanner.MaxRequirements + 5)
            .Select(i => $$"""{ "title": "需求 {{i}}" }"""));

        var (plan, error) = PrdSplitPlanner.Parse($$"""{ "requirements": [ {{requirements}} ] }""");

        Assert.IsNull(error);
        Assert.AreEqual(PrdSplitPlanner.MaxRequirements, plan!.Requirements.Count);
        Assert.IsTrue(plan.Warnings.Any(w => w.Contains("截断")));
    }

    [TestMethod]
    public void ExtractJsonObject_ReturnsNull_WhenNoObject()
    {
        Assert.IsNull(PrdSplitPlanner.ExtractJsonObject("没有 JSON"));
        Assert.AreEqual("{\"a\":1}", PrdSplitPlanner.ExtractJsonObject("前缀 {\"a\":1} 后缀"));
    }

    [TestMethod]
    public void BuildUserPrompt_ContainsProjectAndRequirements()
    {
        var sync = new PrdSyncResult
        {
            ProjectId = "proj-1",
            ProjectName = "示例项目",
            Status = "ready",
            Requirements =
            [
                new PrdRequirementItem { Title = "登录", Description = "支持密码登录", Acceptance = "可登录" }
            ]
        };

        var prompt = PrdSplitPlanner.BuildUserPrompt(sync, [new PrdSupplementDocument { Name = "补充.md", Content = "补充说明" }]);

        StringAssert.Contains(prompt, "示例项目");
        StringAssert.Contains(prompt, "登录");
        StringAssert.Contains(prompt, "验收：可登录");
        StringAssert.Contains(prompt, "补充.md");
        Assert.IsTrue(prompt.Length <= PrdSplitPlanner.MaxPromptChars);
    }

    // ── 验收标准提取 ──────────────────────────────────────────────────────

    [TestMethod]
    public void ExtractAcceptance_FindsFieldByName_AndJoinsValues()
    {
        var fields = new List<YunxiaoCustomFieldValue>
        {
            new()
            {
                FieldName = "优先级",
                Values = [new YunxiaoCustomFieldValueItem { DisplayValue = "高" }]
            },
            new()
            {
                FieldName = "验收标准",
                Values =
                [
                    new YunxiaoCustomFieldValueItem { DisplayValue = "可登录" },
                    new YunxiaoCustomFieldValueItem { Identifier = "可退出" }
                ]
            }
        };

        var acceptance = PrdSyncService.ExtractAcceptance(fields);

        Assert.AreEqual("可登录\n可退出", acceptance);
    }

    [TestMethod]
    public void ExtractAcceptance_ReturnsNull_WhenFieldAbsent()
    {
        Assert.IsNull(PrdSyncService.ExtractAcceptance(null));
        Assert.IsNull(PrdSyncService.ExtractAcceptance([]));
        Assert.IsNull(PrdSyncService.ExtractAcceptance([new YunxiaoCustomFieldValue { FieldName = "优先级" }]));
    }

    // ── 提交：类型选择 / 幂等键 ────────────────────────────────────────────

    [TestMethod]
    public void PickTypeId_PrefersDefaultType()
    {
        var types = new List<YunxiaoWorkitemType>
        {
            new() { Id = "t1", Enable = true },
            new() { Id = "t2", Enable = true, DefaultType = true },
            new() { Id = "t3", Enable = false, DefaultType = true }
        };

        Assert.AreEqual("t2", PrdSubmitService.PickTypeId(types));
        Assert.IsNull(PrdSubmitService.PickTypeId([]));
        Assert.IsNull(PrdSubmitService.PickTypeId([new YunxiaoWorkitemType { Id = "x", Enable = false }]));
    }

    [TestMethod]
    public void MakeKey_StableForSameInput_ChangesWithFingerprint()
    {
        var first = PrdSubmitService.MakeKey("requirement", "R1", "登录");
        var second = PrdSubmitService.MakeKey("requirement", "R1", "登录");
        var changed = PrdSubmitService.MakeKey("requirement", "R1", "登录（改）");

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, changed);
        StringAssert.StartsWith(first, "requirement:R1:");
    }

    [TestMethod]
    public void ShortHash_ReturnsEightLowerHexChars()
    {
        var hash = PrdSubmitService.ShortHash("abc");

        Assert.AreEqual(8, hash.Length);
        Assert.AreEqual(hash.ToLowerInvariant(), hash);
    }

    // ── 账本：文件名净化 ──────────────────────────────────────────────────

    [TestMethod]
    public void Sanitize_RemovesPathSeparators()
    {
        Assert.AreEqual("______etc_passwd", PrdReceiptStore.Sanitize("../../etc/passwd"));
        Assert.AreEqual("unknown", PrdReceiptStore.Sanitize(" "));
    }

    [TestMethod]
    public void GetPath_StaysUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "prd-store-test");
        var store = new PrdReceiptStore(root);

        var path = store.GetPath("../../evil");

        Assert.AreEqual(root, Path.GetDirectoryName(path));
    }

    // ── Submit 编排（假 IProjexClient） ────────────────────────────────────

    [TestMethod]
    public async Task SubmitAsync_AllSuccess_WritesLedger_AndReturnsOk()
    {
        using var tempDir = new TempDir();
        var projex = new SubmitFakeProjexClient();
        projex.NextSprintId = "sp-1";
        projex.NextRepoId = "repo-1";
        projex.NextPlanId = "tp-1";
        var settings = new FakeYunxiaoSettingsService("org-1");
        var store = new PrdReceiptStore(tempDir.FullPath);
        var svc = new PrdSubmitService(projex, settings, store);

        var plan = new PrdSplitPlan
        {
            Iteration = new PrdIterationPlan { Name = "迭代 A", StartDate = "2026-11-20", EndDate = "2026-11-30" },
            Requirements = [new PrdRequirementPlan { Key = "R1", Title = "登录" }],
            Tasks = [new PrdTaskPlan { Key = "T1", RequirementKey = "R1", Title = "接口开发" }],
            Testcases = [new PrdTestcasePlan { Key = "C1", RequirementKey = "R1", Title = "登录成功" }],
            TestPlan = new PrdTestplanPlan { Name = "测试计划" }
        };

        var result = await svc.SubmitAsync(new PrdSubmitRequest { ProjectId = "proj-1", Plan = plan });

        Assert.IsTrue(result.Ok);
        var receipts = result.Data!.Receipts.ToList();
        Assert.AreEqual(6, receipts.Count, "迭代 + 1 需求 + 1 任务 + 关系 + 1 用例 + 1 测试计划");
        Assert.IsTrue(receipts.All(r => r.Ok));
        Assert.AreEqual("iteration", receipts[0].Kind);
        Assert.AreEqual("requirement", receipts[1].Kind);
        Assert.AreEqual("task", receipts[2].Kind);
        Assert.AreEqual("relation", receipts[3].Kind);
        Assert.AreEqual("testcase", receipts[4].Kind);
        Assert.AreEqual("testplan", receipts[5].Kind);

        var relationCall = projex.Calls.OfType<SubmitFakeProjexClient.CreateRelationCall>().Single();
        Assert.AreEqual("wi-R1", relationCall.WorkitemId, "需求作为父节点发起关系");
        Assert.AreEqual("SUB", relationCall.RelationType);
        Assert.AreEqual("wi-T2", relationCall.TargetId, "第二个工作项是任务（全局计数器=2）");

        var saved = await store.LoadAsync("proj-1");
        Assert.AreEqual(6, saved.Receipts.Count);
    }

    [TestMethod]
    public async Task SubmitAsync_ReSubmit_SkipsAlreadyReceipted()
    {
        using var tempDir = new TempDir();
        var projex = new SubmitFakeProjexClient();
        projex.NextSprintId = "sp-1";
        var settings = new FakeYunxiaoSettingsService("org-1");
        var store = new PrdReceiptStore(tempDir.FullPath);
        var svc = new PrdSubmitService(projex, settings, store);

        var plan = new PrdSplitPlan
        {
            Iteration = new PrdIterationPlan { Name = "迭代 A", StartDate = "2026-11-20", EndDate = "2026-11-30" },
            Requirements = [new PrdRequirementPlan { Key = "R1", Title = "登录" }]
        };

        var first = await svc.SubmitAsync(new PrdSubmitRequest { ProjectId = "proj-1", Plan = plan });
        Assert.IsTrue(first.Ok);

        var projex2 = new SubmitFakeProjexClient();
        projex2.NextSprintId = "sp-1";
        var svc2 = new PrdSubmitService(projex2, settings, store);
        var second = await svc2.SubmitAsync(new PrdSubmitRequest { ProjectId = "proj-1", Plan = plan });

        Assert.IsTrue(second.Ok);
        // 每次 submit 开头必然调两次 ListWorkitemTypesAsync（Req + Task），这是类型查询不走幂等账本
        var typeCalls = projex2.Calls.OfType<ValueTuple<string,string>>().Where(c => c.Item1 == "ListWorkitemTypes").ToList();
        Assert.IsTrue(typeCalls.Count >= 1, "类型查询每次都会调");
        // 迭代 / 需求（只有 1 条需求，无任务）都应被账本命中而跳过 → 无 CreateSprint / CreateWorkitem / CreateTestplan 等
        Assert.IsFalse(projex2.Calls.Any(c => (c as ValueTuple<string,string>?)?.Item1 == "CreateSprint"), "迭代已落账本，不应再创建");
        Assert.IsFalse(projex2.Calls.Any(c => (c as ValueTuple<string,string,string,string>?)?.Item1 == "CreateWorkitem"), "需求已落账本，不应再创建");
    }

    [TestMethod]
    public async Task SubmitAsync_MissingOrgId_FailsBeforeAnyRemoteCall()
    {
        var projex = new SubmitFakeProjexClient();
        var settings = new FakeYunxiaoSettingsService("");
        var store = new PrdReceiptStore(Path.GetTempPath());
        var svc = new PrdSubmitService(projex, settings, store);

        var plan = new PrdSplitPlan
        {
            Iteration = new PrdIterationPlan { Name = "迭代 A" },
            Requirements = [new PrdRequirementPlan { Key = "R1", Title = "x" }]
        };
        var result = await svc.SubmitAsync(new PrdSubmitRequest { ProjectId = "proj-1", Plan = plan });

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(400, result.Code);
        Assert.AreEqual(0, projex.Calls.Count);
    }

    // ── 测试辅助类 ──────────────────────────────────────────────────────────

    /// <summary>临时目录：Dispose 时自动清理。</summary>
    private sealed class TempDir : IDisposable
    {
        public static string Create() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "prd-s3-" + Guid.NewGuid().ToString("N"));
        public string FullPath { get; }
        public TempDir() => FullPath = Create();
        public void Dispose() { if (Directory.Exists(FullPath)) Directory.Delete(FullPath, true); }
    }

    private sealed class FakeYunxiaoSettingsService : IYunxiaoSettingsService
    {
        private readonly string _orgId;
        public FakeYunxiaoSettingsService(string orgId) => _orgId = orgId;
        public string GetOrganizationId() => _orgId;
        public bool HasPat() => true;
        public Task SaveAsync(string? organizationId, string? pat = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public void ApplyPersistedOrganizationId() { }
    }

    /// <summary>Submit 编排专用 Fake：记录每次调用的参数，供断言。</summary>
    private sealed class SubmitFakeProjexClient : IProjexClient
    {
        public record CreateRelationCall(string WorkitemId, string RelationType, string TargetId);

        public string? NextSprintId { get; set; }
        public string? NextRepoId { get; set; }
        public string? NextPlanId { get; set; }

        public List<object> Calls { get; } = new();
        private int _workitemCounter;
        private int _caseCounter;

        // ── Submit 实际用到的方法 ────────────────────────────────────────

        public Task<YunxiaoCurrentUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<YunxiaoCurrentUser?>(new() { Id = "user-1", Username = "tester" });

        public Task<string?> CreateSprintAsync(
            string organizationId, string projectId, string name, IReadOnlyList<string> owners,
            string? startDate, string? endDate, string? description, int? capacityHours,
            string? operatorId, CancellationToken cancellationToken = default)
        {
            Calls.Add(("CreateSprint", name));
            return Task.FromResult(NextSprintId);
        }

        public Task<List<YunxiaoWorkitemType>> ListWorkitemTypesAsync(
            string organizationId, string projectId, string category,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(("ListWorkitemTypes", category));
            var prefix = category == "Req" ? "REQ" : category == "Task" ? "TASK" : "OTHER";
            return Task.FromResult(new List<YunxiaoWorkitemType>
            {
                new() { Id = $"{prefix}-1", Enable = true, DefaultType = true, CategoryId = category },
                new() { Id = $"{prefix}-2", Enable = true, CategoryId = category }
            });
        }

        public Task<string?> CreateWorkitemAsync(
            string organizationId, YunxiaoCreateWorkitemRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(("CreateWorkitem", request.Subject, request.ParentId));
            _workitemCounter++;
            var kind = request.WorkitemTypeId.StartsWith("REQ") ? "R" : "T";
            return Task.FromResult<string?>($"wi-{kind}{_workitemCounter}");
        }

        public Task<string?> CreateWorkitemRelationAsync(
            string organizationId, string workitemId, string relationType, string targetWorkitemId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new CreateRelationCall(workitemId, relationType, targetWorkitemId));
            return Task.FromResult<string?>("rel-1");
        }

        public Task<List<YunxiaoTestRepo>> ListTestReposAsync(
            string organizationId, int page, int perPage, string? name,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(("ListTestRepos"));
            return Task.FromResult(new List<YunxiaoTestRepo>
            {
                new() { TestRepoIdentifier = NextRepoId ?? "repo-1", Name = "默认用例库" }
            });
        }

        public Task<string?> CreateTestcaseAsync(
            string organizationId, YunxiaoCreateTestcaseRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(("CreateTestcase", request.Subject));
            _caseCounter++;
            return Task.FromResult<string?>($"case-{_caseCounter}");
        }

        public Task<string?> CreateTestPlanAsync(
            string organizationId, YunxiaoCreateTestPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(("CreateTestPlan", request.Name));
            return Task.FromResult(NextPlanId);
        }

        // ── Submit 不使用，显式抛 NotSupportedException ───────────────────

        public Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsAsync(
            string organizationId, int page, int perPage, string? keyword,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsWithPatAsync(
            string organizationId, int page, int perPage, string? keyword, string pat,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<YunxiaoProject?> GetProjectAsync(
            string organizationId, string projectId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<YunxiaoPagedResult<YunxiaoWorkitem>?> SearchWorkitemsAsync(
            string organizationId, YunxiaoWorkitemSearchCriteria criteria,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<YunxiaoWorkitem?> GetWorkitemAsync(
            string organizationId, string workitemId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<List<YunxiaoWorkitemAttachment>> ListWorkitemAttachmentsAsync(
            string organizationId, string workitemId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
