/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： TaskDetailUnitTest
*描述：任务详情、评论降级与首轮上下文装配契约单测
*****************************************************************************/

using LubanAgentCore.Connectors.AliyunDevOps;
using LubanAgentWebHost.Controllers;
using LubanAgentWebHost.Services;
using LubanAgentWebHost.Services.Init;
using LubanAgentWebHost.Services.TaskDetail;

namespace LubanAgent.Tests;

[TestClass]
public class TaskDetailUnitTest
{
    [TestMethod]
    public void ToPlainText_富文本保留换行并解码实体()
    {
        var text = TaskDetailService.ToPlainText(
            "<p>第一行&nbsp;说明</p><p>第二行<br>第三行</p><ul><li>甲</li><li>乙</li></ul>");

        Assert.AreEqual("第一行 说明\n第二行\n第三行\n- 甲\n- 乙", text);
    }

    [TestMethod]
    public void ToPlainText_解包云效HtmlValue富文本()
    {
        var text = TaskDetailService.ToPlainText(
            "{\"htmlValue\":\"<article><p>第一行&nbsp;说明</p><p>第二行<br>第三行</p></article>\"}");

        Assert.AreEqual("第一行 说明\n第二行\n第三行", text);
    }

    [TestMethod]
    public void CanStart_仅允许待开始与进行中()
    {
        Assert.IsTrue(TaskDetailService.CanStart("TODO"));
        Assert.IsTrue(TaskDetailService.CanStart("doing"));
        Assert.IsFalse(TaskDetailService.CanStart("DONE"));
        Assert.IsFalse(TaskDetailService.CanStart(null));
    }

    [TestMethod]
    public async Task GetDetail_聚合父需求迭代登记与评论并生成首轮输入()
    {
        var root = TempRoot();
        try
        {
            var projex = new FakeProjexClient();
            projex.Workitems["task-1"] = SampleTask();
            projex.Workitems["req-1"] = new YunxiaoWorkitem
            {
                Id = "req-1",
                SerialNumber = "DEMO-1",
                Subject = "统一发布流水线"
            };
            projex.Sprints["sprint-1"] = new YunxiaoSprint { Id = "sprint-1", Name = "十一月迭代" };
            projex.Comments.AddRange(
            [
                Comment("old", "旧意见", 1000, "张三"),
                Comment("new", "<p>最新&nbsp;意见</p>", 3000, "李四", "RICHTEXT"),
                Comment("middle", "中间意见", 2000, "王五")
            ]);

            var initStore = new ProjectInitStore(Path.Combine(root, "init.json"));
            await initStore.UpsertAsync(new ProjectInitRecord
            {
                ProjectId = "project-1",
                Codename = "DEMO",
                WorkspaceId = "ws-new",
                RepoName = "DEMO-Mixed",
                Branch = "develop",
                Pipelines =
                [
                    new ProjectInitPipeline { End = "backend", Dir = "DEMO-Service", Name = "DEPLOY_DEMO-Service" }
                ]
            });
            var taskStore = new InitTaskStore(Path.Combine(root, "tasks.json"));
            var workspaces = new FakeWorkspaceDirectoryProvider();
            workspaces.Items.Add(new WorkspaceDir("ws-new", root));
            var service = new TaskDetailService(projex, new FakeSettingsService(), initStore, taskStore, workspaces);

            var result = await service.GetDetailAsync(
                new TaskDetailQuery { ProjectId = "project-1", WorkitemId = "task-1" }, CancellationToken.None);

            Assert.IsTrue(result.Ok, result.Message);
            var detail = result.Data!;
            Assert.AreEqual("第一行\n第二行", detail.Description);
            Assert.AreEqual("可演示", detail.Acceptance);
            Assert.AreEqual("2026-11-20", detail.DueDate);
            Assert.AreEqual("DEMO-1 统一发布流水线", detail.ParentRequirement?.DisplayName);
            Assert.AreEqual("十一月迭代", detail.SprintName);
            Assert.AreEqual("ws-new", detail.CodeContext?.WorkspaceId);
            Assert.AreEqual(root, detail.CodeContext?.WorkspaceRoot);
            Assert.AreEqual("DEMO-Service", detail.CodeContext?.Ends[0].Dir);
            Assert.AreEqual("new", detail.Comments[0].Id);
            Assert.IsTrue(detail.Comments[0].IsLatest);
            Assert.AreEqual("最新 意见", detail.Comments[0].Content);
            Assert.IsFalse(detail.Comments[1].IsLatest);
            StringAssert.Contains(detail.SeedPrompt, "任务 DEMO-2 替换流水线");
            StringAssert.Contains(detail.SeedPrompt, "可演示");
            StringAssert.Contains(detail.SeedPrompt, "旧意见");
            StringAssert.Contains(detail.SeedPrompt, "先使用 UseSkill 加载 brainstorming 技能");
            StringAssert.Contains(detail.SeedPrompt, "DEMO-Mixed/DEMO-Service");
            Assert.IsTrue(detail.SeedPrompt.IndexOf("旧意见", StringComparison.Ordinal)
                < detail.SeedPrompt.IndexOf("最新 意见", StringComparison.Ordinal), "首轮评论按时间正序");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task GetDetail_旧登记按代号回退工作区且评论失败不阻断()
    {
        var root = TempRoot();
        try
        {
            var projex = new FakeProjexClient();
            projex.Workitems["task-1"] = SampleTask();
            projex.CommentError = new InvalidOperationException("网络失败");
            var initStore = new ProjectInitStore(Path.Combine(root, "init.json"));
            await initStore.UpsertAsync(new ProjectInitRecord
            {
                ProjectId = "project-1",
                Codename = "DEMO",
                RepoName = "DEMO-Mixed"
            });
            var taskStore = new InitTaskStore(Path.Combine(root, "tasks.json"));
            await taskStore.UpsertAsync(new InitTask
            {
                Id = "old-task",
                Codename = "DEMO",
                WorkspaceId = "ws-old",
                CreatedAt = DateTime.Now.AddDays(-1),
                FinishedAt = DateTime.Now
            });
            var workspaces = new FakeWorkspaceDirectoryProvider();
            workspaces.Items.Add(new WorkspaceDir("ws-old", root));
            var service = new TaskDetailService(projex, new FakeSettingsService(), initStore, taskStore, workspaces);

            var result = await service.GetDetailAsync(
                new TaskDetailQuery { ProjectId = "project-1", WorkitemId = "task-1" }, CancellationToken.None);

            Assert.IsTrue(result.Ok, result.Message);
            Assert.AreEqual("ws-old", result.Data!.CodeContext?.WorkspaceId);
            StringAssert.Contains(result.Data.CommentError, "评论暂不可用");
            StringAssert.Contains(result.Data.SeedPrompt, "评论暂不可用");
            Assert.IsTrue(result.Data.CanStart);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void BuildSeedPrompt_评论超过三十条只带最近三十条并注明省略()
    {
        var detail = new TaskDetailResult
        {
            ProjectId = "project-1",
            WorkitemId = "task-1",
            SerialNumber = "DEMO-2",
            Title = "替换流水线",
            Description = "完整描述",
            Comments = Enumerable.Range(1, 32)
                .Select(i => new TaskCommentItem
                {
                    Id = i.ToString(),
                    Author = "测试",
                    Content = "评论-" + i,
                    CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(i),
                    IsLatest = i == 32
                })
                .Reverse()
                .ToList()
        };

        var prompt = TaskDetailService.BuildSeedPrompt(detail);

        StringAssert.Contains(prompt, "评论-3");
        StringAssert.Contains(prompt, "评论-32");
        Assert.IsFalse(prompt.Contains("评论-1\n", StringComparison.Ordinal));
        Assert.IsFalse(prompt.Contains("评论-2\n", StringComparison.Ordinal));
        StringAssert.Contains(prompt, "更早评论已省略 2 条");
        Assert.AreEqual(30, detail.SeedCommentCount);
    }

    [TestMethod]
    public async Task CreateComment_拒绝空白并透传Markdown正文()
    {
        var root = TempRoot();
        try
        {
            var projex = new FakeProjexClient { CreatedCommentId = "comment-1" };
            var service = new TaskDetailService(
                projex,
                new FakeSettingsService(),
                new ProjectInitStore(Path.Combine(root, "init.json")),
                new InitTaskStore(Path.Combine(root, "tasks.json")),
                new FakeWorkspaceDirectoryProvider());

            var blank = await service.CreateCommentAsync(new TaskCommentRequest
            {
                ProjectId = "project-1",
                WorkitemId = "task-1",
                Content = "  "
            }, CancellationToken.None);
            Assert.IsFalse(blank.Ok);
            Assert.AreEqual(400, blank.Code);
            Assert.AreEqual(0, projex.CreateCommentCalls);

            var created = await service.CreateCommentAsync(new TaskCommentRequest
            {
                ProjectId = "project-1",
                WorkitemId = "task-1",
                Content = "  **同意方案**  "
            }, CancellationToken.None);
            Assert.IsTrue(created.Ok, created.Message);
            Assert.AreEqual("comment-1", created.Data);
            Assert.AreEqual("**同意方案**", projex.LastComment);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Routes_任务详情与评论端点已暴露()
    {
        var controller = typeof(YunxiaoController);
        Assert.IsNotNull(controller.GetMethod("TaskDetail"));
        Assert.IsNotNull(controller.GetMethod("TaskComment"));
        Assert.IsNotNull(typeof(WebYunxiaoService).GetMethod("TaskDetailAsync"));
        Assert.IsNotNull(typeof(WebYunxiaoService).GetMethod("TaskCommentAsync"));
    }

    private static YunxiaoWorkitem SampleTask() => new()
    {
        Id = "task-1",
        SerialNumber = "DEMO-2",
        Subject = "替换流水线",
        Description = "<p>第一行</p><p>第二行</p>",
        FormatType = "RICHTEXT",
        ParentId = "req-1",
        Status = new YunxiaoWorkitemStatus { Id = "doing", Name = "进行中", NameEn = "DOING" },
        Sprint = new YunxiaoWorkitemRef { Id = "sprint-1" },
        Space = new YunxiaoWorkitemRef { Id = "project-1" },
        AssignedTo = new YunxiaoUserRef { Name = "开发者" },
        CustomFieldValues =
        [
            new YunxiaoCustomFieldValue
            {
                FieldName = "验收标准",
                Values = [new YunxiaoCustomFieldValueItem { DisplayValue = "可演示" }]
            },
            new YunxiaoCustomFieldValue
            {
                FieldName = "预计完成时间",
                Values = [new YunxiaoCustomFieldValueItem { DisplayValue = "2026-11-20" }]
            }
        ]
    };

    private static YunxiaoWorkitemComment Comment(
        string id, string content, long createdAt, string author, string format = "MARKDOWN") => new()
    {
        Id = id,
        Content = content,
        ContentFormat = format,
        GmtCreate = createdAt,
        User = new YunxiaoUserRef { Name = author }
    };

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-task-detail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class FakeSettingsService : IYunxiaoSettingsService
    {
        public string GetOrganizationId() => "org-1";
        public bool HasPat() => true;
        public Task SaveAsync(string? organizationId, string? pat = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public void ApplyPersistedOrganizationId() { }
    }

    private sealed class FakeWorkspaceDirectoryProvider : IWorkspaceDirectoryProvider
    {
        public List<WorkspaceDir> Items { get; } = [];
        public IReadOnlyList<WorkspaceDir> List() => Items;
    }

    private sealed class FakeProjexClient : IProjexClient
    {
        public Dictionary<string, YunxiaoWorkitem> Workitems { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, YunxiaoSprint> Sprints { get; } = new(StringComparer.Ordinal);
        public List<YunxiaoWorkitemComment> Comments { get; } = [];
        public Exception? CommentError { get; set; }
        public string? CreatedCommentId { get; set; }
        public string? LastComment { get; private set; }
        public int CreateCommentCalls { get; private set; }

        public Task<YunxiaoWorkitem?> GetWorkitemAsync(string organizationId, string workitemId, CancellationToken cancellationToken = default)
            => Task.FromResult(Workitems.GetValueOrDefault(workitemId));

        public Task<YunxiaoSprint?> GetSprintAsync(string? organizationId, string projectId, string sprintId, CancellationToken cancellationToken = default)
            => Task.FromResult(Sprints.GetValueOrDefault(sprintId));

        public Task<List<YunxiaoWorkitemComment>> ListWorkitemCommentsAsync(string organizationId, string workitemId, CancellationToken cancellationToken = default)
        {
            if (CommentError is not null) throw CommentError;
            return Task.FromResult(Comments.ToList());
        }

        public Task<string?> CreateWorkitemCommentAsync(string organizationId, string workitemId, string content, string? parentId, CancellationToken cancellationToken = default)
        {
            CreateCommentCalls++;
            LastComment = content;
            return Task.FromResult(CreatedCommentId);
        }

        public Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsAsync(string organizationId, int page, int perPage, string? keyword, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<YunxiaoProject?> GetProjectAsync(string organizationId, string projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsWithPatAsync(string organizationId, int page, int perPage, string? keyword, string pat, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<YunxiaoPagedResult<YunxiaoWorkitem>?> SearchWorkitemsAsync(string organizationId, YunxiaoWorkitemSearchCriteria criteria, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> CreateWorkitemAsync(string organizationId, YunxiaoCreateWorkitemRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<YunxiaoWorkitemType>> ListWorkitemTypesAsync(string organizationId, string projectId, string category, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<YunxiaoWorkitemAttachment>> ListWorkitemAttachmentsAsync(string organizationId, string workitemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> CreateSprintAsync(string organizationId, string projectId, string name, IReadOnlyList<string> owners, string? startDate, string? endDate, string? description, int? capacityHours, string? operatorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<YunxiaoSprint>> ListSprintsAsync(string? organizationId, string projectId, string? status, int page, int perPage, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<YunxiaoCurrentUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> CreateWorkitemRelationAsync(string organizationId, string workitemId, string relationType, string targetWorkitemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<YunxiaoTestRepo>> ListTestReposAsync(string organizationId, int page, int perPage, string? name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> CreateTestcaseAsync(string organizationId, YunxiaoCreateTestcaseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> CreateTestPlanAsync(string organizationId, YunxiaoCreateTestPlanRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<YunxiaoTestRepoDirectory>> ListTestRepoDirectoriesAsync(string organizationId, string testRepoId, int page = 1, int perPage = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
