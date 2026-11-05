/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： ManagementServiceUnitTest
*创建时间：2026/10/27
*描述：Task 17 搬迁到 Core 的三个管理服务单测。
*      ① 工作区管理：六个方法严格转发，授权门禁跳过不存在的工作区；
*      ② 会话管理：列表按是否给工作区走两条数据源，其余方法转发并映射；
*      ③ LLM Wiki 管理：三种检索范围选路、状态取当前工作区、清空顺序、
*         ingest 提取与 ../ 越界拒绝、未配置嵌入模型时的守卫。
*      服务一律依赖接口（仓储缝），故本测试不触碰 LuBanOrm 静态构造。
*
*****************************************************************************/

using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Retrieval;
using LuBan.AIAgent.Sessions;
using LuBan.AIAgent.Wiki;
using LubanAgentCore.Entities;
using LubanAgentCore.Hosting;
using LubanAgentCore.Models;
using LubanAgentCore.Repositories;
using LubanAgentCore.Services;
using LubanAgentCore.Services.Management;
using LubanAgentCore.Services.Management.Models;

namespace LubanAgent.Tests;

[TestClass]
public class ManagementServiceUnitTest
{
    // ---------------- 工作区管理 ----------------

    [TestMethod]
    public async Task Workspace_ListAsync_ForwardsAllWorkspaces()
    {
        var fake = new FakeWorkspaceManager();
        fake.Workspaces.Add(new WorkspaceInfo { WorkspaceId = "w1", Name = "一" });
        fake.Workspaces.Add(new WorkspaceInfo { WorkspaceId = "w2", Name = "二" });
        var svc = new WorkspaceManagementService(fake);

        var list = await svc.ListAsync();

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual("w2", list[1].WorkspaceId);
    }

    [TestMethod]
    public async Task Workspace_GetCurrentAsync_ReturnsManagerCurrent()
    {
        var fake = new FakeWorkspaceManager { CurrentWorkspace = new WorkspaceInfo { WorkspaceId = "cur" } };
        var svc = new WorkspaceManagementService(fake);

        var current = await svc.GetCurrentAsync();

        Assert.IsNotNull(current);
        Assert.AreEqual("cur", current.WorkspaceId);
    }

    [TestMethod]
    public async Task Workspace_CreateAsync_ForwardsNamePathAndType()
    {
        var fake = new FakeWorkspaceManager();
        var svc = new WorkspaceManagementService(fake);

        var created = await svc.CreateAsync("知识库", @"D:\kb", WorkspaceTypes.LlmWiki);

        Assert.AreEqual(@"D:\kb|知识库|LlmWiki", fake.LastCreate);
        Assert.AreEqual("created:" + @"D:\kb", created.WorkspaceId);
    }

    [TestMethod]
    public async Task Workspace_DeleteAndSwitch_ForwardIds()
    {
        var fake = new FakeWorkspaceManager();
        var svc = new WorkspaceManagementService(fake);

        await svc.DeleteAsync("del-1");
        await svc.SwitchAsync("sw-1");

        CollectionAssert.AreEqual(new[] { "delete:del-1", "switch:sw-1" }, fake.Calls);
    }

    [TestMethod]
    public async Task Workspace_GrantAccessAsync_MissingWorkspace_DoesNotPrompt()
    {
        var fake = new FakeWorkspaceManager();
        var svc = new WorkspaceManagementService(fake);

        await svc.GrantAccessAsync("missing");

        Assert.AreEqual(0, fake.EnsureAuthorizedCalls, "工作区不存在时不得进入授权弹窗");
    }

    [TestMethod]
    public async Task Workspace_GrantAccessAsync_ExistingWorkspace_EnsuresAuthorized()
    {
        var fake = new FakeWorkspaceManager();
        fake.ById["w1"] = new WorkspaceInfo { WorkspaceId = "w1", IsAuthorized = true };
        var svc = new WorkspaceManagementService(fake);

        await svc.GrantAccessAsync("w1");

        Assert.AreEqual(1, fake.EnsureAuthorizedCalls);
        Assert.AreEqual("w1", fake.LastEnsured!.WorkspaceId);
    }

    // ---------------- 会话管理 ----------------

    [TestMethod]
    public async Task Session_ListAsync_WithoutWorkspace_UsesSessionManager()
    {
        var manager = new FakeSessionManager();
        manager.UserSessions.Add(new SessionInfo { SessionId = "s1" });
        var query = new FakeSessionWorkspaceQuery();
        var svc = new ChatSessionService(manager, query, new FakeUserContext("u1"));

        var list = await svc.ListAsync();

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("s1", list[0].SessionId);
        Assert.AreEqual(0, query.Calls, "不传工作区时不应查询工作区仓储");
    }

    [TestMethod]
    public async Task Session_ListAsync_WithWorkspace_QueriesRepositoryAndMaps()
    {
        var manager = new FakeSessionManager();
        manager.UserSessions.Add(new SessionInfo { SessionId = "should-not-be-used" });
        var query = new FakeSessionWorkspaceQuery();
        query.Sessions.Add(new DbSession
        {
            SessionId = "s9",
            UserId = "u1",
            Title = "标题",
            MessageCount = 3,
            TotalTokens = 120,
            CreateTime = new DateTime(2026, 10, 1),
            UpdateTime = new DateTime(2026, 10, 2),
        });
        var svc = new ChatSessionService(manager, query, new FakeUserContext("u1"));

        var list = await svc.ListAsync("ws-1");

        Assert.AreEqual("ws-1", query.LastWorkspaceId);
        Assert.AreEqual("u1", query.LastUserId);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("s9", list[0].SessionId);
        Assert.AreEqual("标题", list[0].Title);
        Assert.AreEqual(3, list[0].MessageCount);
        Assert.AreEqual(120, list[0].TotalTokens);
        Assert.AreEqual(new DateTime(2026, 10, 2), list[0].UpdatedAt);
    }

    [TestMethod]
    public async Task Session_CreateDeleteRename_ForwardToManager()
    {
        var manager = new FakeSessionManager();
        var svc = new ChatSessionService(manager, new FakeSessionWorkspaceQuery(), new FakeUserContext("u1"));

        await svc.CreateAsync("新会话", "ws-1");
        await svc.RenameAsync("s1", "改名");
        await svc.DeleteAsync("s1");

        CollectionAssert.AreEqual(
            new[] { "create:新会话|ws-1", "rename:s1|改名", "delete:s1" },
            manager.Calls);
    }

    [TestMethod]
    public async Task Session_GetMessagesAsync_MapsAllFields()
    {
        var manager = new FakeSessionManager();
        manager.Messages.Add(new SessionMessage
        {
            Id = 7,
            SessionId = "s1",
            Role = "user",
            Content = "hi",
            Attachments = "[{\"FileName\":\"a.png\"}]",
            CreatedAt = new DateTime(2026, 10, 3),
        });
        var svc = new ChatSessionService(manager, new FakeSessionWorkspaceQuery(), new FakeUserContext("u1"));

        var messages = await svc.GetMessagesAsync("s1");

        Assert.AreEqual(1, messages.Count);
        Assert.AreEqual(7, messages[0].Id);
        StringAssert.Contains(messages[0].Attachments!, "a.png");
    }

    [TestMethod]
    public async Task Session_ClearMessagesSwitchClearAll_ForwardToManager()
    {
        var manager = new FakeSessionManager();
        var svc = new ChatSessionService(manager, new FakeSessionWorkspaceQuery(), new FakeUserContext("u1"));

        await svc.ClearMessagesAsync("s1");
        await svc.SwitchAsync("s1");
        await svc.ClearAllAsync("ws-1");

        CollectionAssert.AreEqual(
            new[] { "clear:s1", "switch:s1", "clearall:ws-1" },
            manager.Calls);
    }

    [TestMethod]
    public async Task Session_UserContextMissing_FallsBackToLocalDefaultUser()
    {
        var query = new FakeSessionWorkspaceQuery();
        var svc = new ChatSessionService(new FakeSessionManager(), query, new FakeUserContext(null));

        await svc.ListAsync("ws-1");

        Assert.AreEqual(LocalUserDefaults.DefaultUserId, query.LastUserId);
    }

    // ---------------- LLM Wiki 管理 ----------------

    [TestMethod]
    public async Task LlmWiki_SearchAsync_RawOnly_UsesRetrieval()
    {
        var retrieval = new FakeRetrievalService();
        var wiki = new FakeWikiService();
        var svc = NewWikiService(retrieval: retrieval, wiki: wiki);

        var results = await svc.SearchAsync("ws-1", "查询", WikiSearchScope.RawOnly);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("raw", results[0].FilePath);
        Assert.AreEqual("ws-1", retrieval.LastSearchWorkspaceId);
        Assert.AreEqual(5, retrieval.LastTopK);
        Assert.AreEqual(0, wiki.SearchCalls, "仅原始资料时不应走 wiki");
    }

    [TestMethod]
    public async Task LlmWiki_SearchAsync_WikiOnly_ExcludesRaw()
    {
        var retrieval = new FakeRetrievalService();
        var wiki = new FakeWikiService();
        var svc = NewWikiService(retrieval: retrieval, wiki: wiki);

        var results = await svc.SearchAsync("ws-1", "查询", WikiSearchScope.WikiOnly);

        Assert.AreEqual("wiki", results[0].FilePath);
        Assert.AreEqual(false, wiki.LastIncludeRaw);
        Assert.AreEqual(0, retrieval.SearchCalls);
    }

    [TestMethod]
    public async Task LlmWiki_SearchAsync_Default_IncludesRawFallback()
    {
        var wiki = new FakeWikiService();
        var svc = NewWikiService(wiki: wiki);

        await svc.SearchAsync("ws-1", "查询", WikiSearchScope.WikiThenRaw);

        Assert.AreEqual(true, wiki.LastIncludeRaw);
        Assert.AreEqual("ws-1", wiki.LastSearchWorkspaceId);
    }

    [TestMethod]
    public async Task LlmWiki_SearchAsync_WithoutEmbedder_ThrowsWithHint()
    {
        var svc = NewWikiService();

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => svc.SearchAsync("ws-1", "查询", WikiSearchScope.WikiThenRaw));

        Assert.IsTrue(ex.Message.Contains("未配置嵌入模型"), ex.Message);
    }

    [TestMethod]
    public async Task LlmWiki_ClearAsync_DeletesFilesBeforeChunks()
    {
        var calls = new List<string>();
        var files = new FakeLlmWikiFileStore(calls);
        var chunks = new FakeLlmWikiChunkStore(calls);
        var svc = new LlmWikiManagementService(new FakeWorkspaceManager(), files, chunks);

        await svc.ClearAsync("ws-1");

        CollectionAssert.AreEqual(new[] { "files:ws-1", "chunks:ws-1" }, calls);
    }

    [TestMethod]
    public async Task LlmWiki_ListFilesAsync_MapsIndexRows()
    {
        var files = new FakeLlmWikiFileStore(new List<string>());
        files.Files.Add(new DbLlmWikiFile
        {
            FilePath = "docs/a.md",
            Language = "markdown",
            ChunkCount = 4,
            IndexedTime = new DateTime(2026, 10, 4),
        });
        var svc = new LlmWikiManagementService(new FakeWorkspaceManager(), files, new FakeLlmWikiChunkStore(new List<string>()));

        var list = await svc.ListFilesAsync("ws-1");

        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("docs/a.md", list[0].FilePath);
        Assert.AreEqual("markdown", list[0].Language);
        Assert.AreEqual(4, list[0].ChunkCount);
    }

    [TestMethod]
    public async Task LlmWiki_GetStatusAsync_UsesCurrentWorkspaceAndCountsIndexRows()
    {
        var workspaces = new FakeWorkspaceManager
        {
            CurrentWorkspace = new WorkspaceInfo { WorkspaceId = "cur-ws" }
        };
        var wiki = new FakeWikiService
        {
            Stats = new WikiStats { PageCount = 7, LastRebuildSummary = "2026-10-05 重建" }
        };
        var files = new FakeLlmWikiFileStore(new List<string>());
        files.Files.Add(new DbLlmWikiFile());
        files.Files.Add(new DbLlmWikiFile());
        var chunks = new FakeLlmWikiChunkStore(new List<string>()) { Count = 5 };
        var svc = new LlmWikiManagementService(workspaces, files, chunks, wiki: wiki);

        var status = await svc.GetStatusAsync();

        Assert.AreEqual(7, status.PageCount);
        Assert.AreEqual("2026-10-05 重建", status.LastRebuildSummary);
        Assert.AreEqual(2, status.IndexedFiles);
        Assert.AreEqual(5, status.IndexedChunks);
        Assert.AreEqual("cur-ws", wiki.LastStatsWorkspaceId);
    }

    [TestMethod]
    public async Task LlmWiki_GetStatusAsync_NoCurrentWorkspace_Throws()
    {
        var svc = NewWikiService(wiki: new FakeWikiService());

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => svc.GetStatusAsync());

        Assert.IsTrue(ex.Message.Contains("/work -switch"), ex.Message);
    }

    [TestMethod]
    public async Task LlmWiki_StartIndexAsync_WithoutTaskService_ReturnsNull()
    {
        var svc = NewWikiService(retrieval: new FakeRetrievalService());

        Assert.IsNull(await svc.StartIndexAsync("ws-1"));
        Assert.IsNull(await svc.StartRebuildAsync("ws-1"));
        Assert.IsNull(await svc.StartIngestAsync("ws-1", "a.md"));
    }

    [TestMethod]
    public async Task LlmWiki_IndexFilesAsync_UsesWorkspaceRootAndGlob()
    {
        var retrieval = new FakeRetrievalService();
        var workspaces = new FakeWorkspaceManager();
        workspaces.ById["ws-1"] = new WorkspaceInfo { WorkspaceId = "ws-1", RootPath = @"D:\kb" };
        var svc = new LlmWikiManagementService(workspaces, new FakeLlmWikiFileStore(new List<string>()), new FakeLlmWikiChunkStore(new List<string>()), retrieval: retrieval);

        await svc.IndexFilesAsync("ws-1", "**/*.md");

        Assert.AreEqual(@"D:\kb", retrieval.LastIndexPath);
        Assert.AreEqual("**/*.md", retrieval.LastGlob);
        Assert.AreEqual("ws-1", retrieval.LastIndexWorkspaceId);
    }

    [TestMethod]
    public async Task LlmWiki_IndexFilesAsync_MissingWorkspace_Throws()
    {
        var svc = NewWikiService(retrieval: new FakeRetrievalService());

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => svc.IndexFilesAsync("nope"));

        Assert.IsTrue(ex.Message.Contains("找不到工作区"), ex.Message);
    }

    [TestMethod]
    public async Task LlmWiki_PrepareIngestAsync_SmallFile_ProducesDraftInsideWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-ingest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "note.md"), "# 标题\n\n正文内容。");

            var workspaces = new FakeWorkspaceManager();
            workspaces.ById["ws-1"] = new WorkspaceInfo
            {
                WorkspaceId = "ws-1",
                RootPath = root,
                ConfigPath = ".luban-agent",
                UserId = "u1",
            };
            var svc = new LlmWikiManagementService(workspaces, new FakeLlmWikiFileStore(new List<string>()), new FakeLlmWikiChunkStore(new List<string>()));
            var progress = new RecordingProgress();

            var draft = await svc.PrepareIngestAsync("ws-1", "note.md", progress);

            Assert.AreEqual(1, draft.MergedFiles);
            Assert.AreEqual(1, draft.TotalFiles);
            Assert.IsFalse(string.IsNullOrEmpty(draft.TempPath));
            Assert.IsTrue(File.Exists(draft.TempPath), "合并结果必须落盘到工作区 temp");
            Assert.IsTrue(draft.RelativePath.StartsWith(".luban-agent/temp/"), draft.RelativePath);
            Assert.IsTrue(progress.Reports.Count > 0, "应逐文件回报进度");

            var merged = await File.ReadAllTextAsync(draft.TempPath);
            Assert.IsTrue(merged.Contains("正文内容。"), merged);
            Assert.IsTrue(merged.Contains("note.md"), merged);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [TestMethod]
    public async Task LlmWiki_PrepareIngestAsync_PathOutsideRoot_IsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-ingest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var outside = Path.Combine(Path.GetDirectoryName(root)!, "outside-" + Guid.NewGuid().ToString("N") + ".md");
        await File.WriteAllTextAsync(outside, "越界文件");
        try
        {
            var workspaces = new FakeWorkspaceManager();
            workspaces.ById["ws-1"] = new WorkspaceInfo { WorkspaceId = "ws-1", RootPath = root };
            var svc = new LlmWikiManagementService(workspaces, new FakeLlmWikiFileStore(new List<string>()), new FakeLlmWikiChunkStore(new List<string>()));

            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => svc.PrepareIngestAsync("ws-1", "../" + Path.GetFileName(outside)));

            Assert.IsTrue(ex.Message.Contains("未找到匹配文件"), ex.Message);
        }
        finally
        {
            try { File.Delete(outside); } catch (IOException) { }
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    // ---------------- 装配辅助 ----------------

    private static LlmWikiManagementService NewWikiService(
        FakeRetrievalService? retrieval = null,
        FakeWikiService? wiki = null,
        FakeWorkspaceManager? workspaces = null)
    {
        workspaces ??= new FakeWorkspaceManager();
        workspaces.ById.TryAdd("ws-1", new WorkspaceInfo { WorkspaceId = "ws-1", RootPath = Path.GetTempPath() });
        return new LlmWikiManagementService(
            workspaces,
            new FakeLlmWikiFileStore(new List<string>()),
            new FakeLlmWikiChunkStore(new List<string>()),
            retrieval,
            wiki);
    }

    // ---------------- Stub：进度 ----------------

    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Reports { get; } = new();

        public void Report(string value) => Reports.Add(value);
    }

    // ---------------- Stub：用户上下文 ----------------

    private sealed class FakeUserContext(string? userId) : IUserContext
    {
        public string? UserId { get; } = userId;
    }

    // ---------------- Stub：工作区 ----------------

    private sealed class FakeWorkspaceManager : IWorkspaceManager
    {
        public WorkspaceInfo? CurrentWorkspace { get; set; }

        public event Action<WorkspaceInfo>? CurrentWorkspaceChanged
        {
            add { }
            remove { }
        }

        public List<WorkspaceInfo> Workspaces { get; } = new();
        public Dictionary<string, WorkspaceInfo> ById { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Calls { get; } = new();
        public string? LastCreate { get; private set; }
        public WorkspaceInfo? LastEnsured { get; private set; }
        public int EnsureAuthorizedCalls { get; private set; }

        public Task<WorkspaceInfo> CreateWorkspaceAsync(string rootPath, string? name = null, string type = WorkspaceTypes.Normal)
        {
            LastCreate = $"{rootPath}|{name}|{type}";
            return Task.FromResult(new WorkspaceInfo { WorkspaceId = "created:" + rootPath, Name = name ?? "", RootPath = rootPath, Type = type });
        }

        public Task SwitchWorkspaceAsync(string workspaceId)
        {
            Calls.Add("switch:" + workspaceId);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<WorkspaceInfo>> GetUserWorkspacesAsync()
            => Task.FromResult<IEnumerable<WorkspaceInfo>>(Workspaces);

        public Task<WorkspaceInfo?> GetWorkspaceAsync(string workspaceId)
            => Task.FromResult(ById.TryGetValue(workspaceId, out var ws) ? ws : null);

        public Task<WorkspaceInfo?> GetByRootPathAsync(string rootPath)
            => Task.FromResult(Workspaces.FirstOrDefault(w =>
                string.Equals(w.RootPath, rootPath, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<WorkspaceInfo>> SearchByNameAsync(string keyword)
            => Task.FromResult<IReadOnlyList<WorkspaceInfo>>(Workspaces
                .Where(w => w.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList());

        public Task<bool> EnsureAuthorizedAsync(WorkspaceInfo workspace)
        {
            EnsureAuthorizedCalls++;
            LastEnsured = workspace;
            return Task.FromResult(true);
        }

        public Task EnsureConfigDirectoryAsync(WorkspaceInfo workspace) => Task.CompletedTask;

        public Task SetCurrentAsync(string workspaceId)
        {
            Calls.Add("setcurrent:" + workspaceId);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteWorkspaceAsync(string workspaceId)
        {
            Calls.Add("delete:" + workspaceId);
            return Task.FromResult(true);
        }

        public Task<bool> RenameWorkspaceAsync(string workspaceId, string name)
        {
            Calls.Add("rename:" + workspaceId + "|" + name);
            return Task.FromResult(true);
        }

        public string? GetKnownRootPath(string? workspaceId)
            => workspaceId is not null && ById.TryGetValue(workspaceId, out var ws) ? ws.RootPath : null;

        public bool IsWithinWorkspace(string path) => true;
    }

    // ---------------- Stub：会话 ----------------

    private sealed class FakeSessionManager : ISessionManager
    {
        public List<string> Calls { get; } = new();
        public List<SessionInfo> UserSessions { get; } = new();
        public List<SessionMessage> Messages { get; } = new();

        public SessionInfo? CurrentSession { get; private set; }

        public Task<SessionInfo> CreateSessionAsync(string? title = null, string? workspaceId = null)
        {
            Calls.Add($"create:{title}|{workspaceId}");
            return Task.FromResult(new SessionInfo { SessionId = "new", Title = title });
        }

        public Task<SessionInfo?> GetSessionAsync(string sessionId)
            => Task.FromResult<SessionInfo?>(new SessionInfo { SessionId = sessionId });

        public Task<IEnumerable<SessionInfo>> GetUserSessionsAsync()
            => Task.FromResult<IEnumerable<SessionInfo>>(UserSessions);

        public Task UpdateSessionTitleAsync(string sessionId, string title)
        {
            Calls.Add($"rename:{sessionId}|{title}");
            return Task.CompletedTask;
        }

        public Task DeleteSessionAsync(string sessionId)
        {
            Calls.Add("delete:" + sessionId);
            return Task.CompletedTask;
        }

        public Task<SessionMessage> AddMessageAsync(string sessionId, string role, string content, int? tokens = null,
            string? thinking = null, string? attachments = null, string? toolCalls = null)
            => Task.FromResult(new SessionMessage { SessionId = sessionId, Role = role, Content = content });

        public Task<IEnumerable<SessionMessage>> GetMessagesAsync(string sessionId, int? limit = null)
            => Task.FromResult<IEnumerable<SessionMessage>>(Messages);

        public Task<IEnumerable<SessionMessage>> GetLatestMessagesAsync(string sessionId, int count)
            => Task.FromResult<IEnumerable<SessionMessage>>(Messages);

        public Task ClearMessagesAsync(string sessionId)
        {
            Calls.Add("clear:" + sessionId);
            return Task.CompletedTask;
        }

        public Task<SessionStats> GetSessionStatsAsync(string sessionId) => Task.FromResult(new SessionStats());

        public Task SetCurrentSessionAsync(string sessionId)
        {
            Calls.Add("switch:" + sessionId);
            CurrentSession = new SessionInfo { SessionId = sessionId };
            return Task.CompletedTask;
        }

        public void ClearCurrentSession() => CurrentSession = null;

        public Task ClearAllSessionsAsync(string? workspaceId = null)
        {
            Calls.Add("clearall:" + workspaceId);
            return Task.CompletedTask;
        }

        public Task<GlobalSessionStats> GetGlobalStatsAsync(int? days = null) => Task.FromResult(new GlobalSessionStats());

        public Task<IEnumerable<SessionMessage>> GetActiveMessagesAsync(string sessionId)
            => Task.FromResult<IEnumerable<SessionMessage>>(Messages);

        public Task MarkMessagesCompactedAsync(string sessionId, IEnumerable<long> messageIds) => Task.CompletedTask;
    }

    private sealed class FakeSessionWorkspaceQuery : ISessionWorkspaceQuery
    {
        public List<DbSession> Sessions { get; } = new();
        public int Calls { get; private set; }
        public string? LastWorkspaceId { get; private set; }
        public string? LastUserId { get; private set; }

        public Task<List<DbSession>> GetByWorkspaceAsync(string workspaceId, string userId)
        {
            Calls++;
            LastWorkspaceId = workspaceId;
            LastUserId = userId;
            return Task.FromResult(Sessions);
        }

        public Task<string?> GetSessionWorkspaceIdAsync(string sessionId, string userId)
            => Task.FromResult(Sessions
                .FirstOrDefault(s => s.SessionId == sessionId)?.WorkspaceId);
    }

    // ---------------- Stub：LLM Wiki 仓储缝 ----------------

    private sealed class FakeLlmWikiFileStore(List<string> calls) : ILlmWikiFileStore
    {
        public List<DbLlmWikiFile> Files { get; } = new();

        public Task<List<DbLlmWikiFile>> GetByWorkspaceAsync(string workspaceId) => Task.FromResult(Files);

        public Task DeleteByWorkspaceAsync(string workspaceId)
        {
            calls.Add("files:" + workspaceId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLlmWikiChunkStore(List<string> calls) : ILlmWikiChunkStore
    {
        public int Count { get; set; }

        public Task<int> CountByWorkspaceAsync(string workspaceId) => Task.FromResult(Count);

        public Task DeleteByWorkspaceAsync(string workspaceId)
        {
            calls.Add("chunks:" + workspaceId);
            return Task.CompletedTask;
        }
    }

    // ---------------- Stub：检索 / wiki ----------------

    private sealed class FakeRetrievalService : IRetrievalService
    {
        public int SearchCalls { get; private set; }
        public int LastTopK { get; private set; }
        public string? LastSearchWorkspaceId { get; private set; }
        public string? LastIndexPath { get; private set; }
        public string? LastGlob { get; private set; }
        public string? LastIndexWorkspaceId { get; private set; }

        public Task<IndexReport> IndexDirectoryAsync(string path, string? glob = null, bool force = false,
            IProgress<IndexProgress>? progress = null, CancellationToken cancellationToken = default, string? workspaceId = null)
        {
            LastIndexPath = path;
            LastGlob = glob;
            LastIndexWorkspaceId = workspaceId;
            return Task.FromResult(new IndexReport { ScannedFiles = 1, NewFiles = 1 });
        }

        public Task<IndexReport> IndexFileAsync(string path, bool force = false, CancellationToken cancellationToken = default, string? workspaceId = null)
            => Task.FromResult(new IndexReport());

        public Task RemoveAsync(string sourceName, CancellationToken cancellationToken = default, string? workspaceId = null)
            => Task.CompletedTask;

        public Task<IndexReport> IndexContentAsync(string content, string language, string sourceName,
            CancellationToken cancellationToken = default, string? workspaceId = null)
            => Task.FromResult(new IndexReport());

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(string query, int topK = 5, string? pathPrefix = null,
            string? language = null, CancellationToken cancellationToken = default, string? workspaceId = null)
        {
            SearchCalls++;
            LastTopK = topK;
            LastSearchWorkspaceId = workspaceId;
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(new List<RetrievalResult>
            {
                new() { FilePath = "raw", Content = "原始资料" }
            });
        }

        public Task<IndexStats> GetStatsAsync(string? workspaceId = null)
            => Task.FromResult(new IndexStats { TotalFiles = 1, TotalChunks = 1 });
    }

    private sealed class FakeWikiService : IWikiService
    {
        public int SearchCalls { get; private set; }
        public bool? LastIncludeRaw { get; private set; }
        public string? LastSearchWorkspaceId { get; private set; }
        public string? LastStatsWorkspaceId { get; private set; }
        public WikiStats Stats { get; set; } = new();

        public Task<WikiIndex> ReadIndexAsync(CancellationToken cancellationToken = default, string? workspaceId = null)
            => throw new NotSupportedException();

        public Task<WikiPage> ReadPageAsync(string relativePath, CancellationToken cancellationToken = default, string? workspaceId = null)
            => throw new NotSupportedException();

        public Task SavePageAsync(WikiPage page, CancellationToken cancellationToken = default, string? workspaceId = null)
            => throw new NotSupportedException();

        public Task DeletePageAsync(string relativePath, CancellationToken cancellationToken = default, string? workspaceId = null)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(string query, int? topK = null, bool? includeRaw = null,
            CancellationToken cancellationToken = default, string? workspaceId = null)
        {
            SearchCalls++;
            LastIncludeRaw = includeRaw;
            LastSearchWorkspaceId = workspaceId;
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(new List<RetrievalResult>
            {
                new() { FilePath = "wiki", Content = "wiki 页面" }
            });
        }

        public Task<IndexReport> RebuildIndexAsync(CancellationToken cancellationToken = default, string? workspaceId = null)
            => Task.FromResult(new IndexReport());

        public Task<WikiLintReport> LintAsync(CancellationToken cancellationToken = default, string? workspaceId = null)
            => Task.FromResult(new WikiLintReport());

        public Task<WikiStats> GetStatsAsync(CancellationToken cancellationToken = default, string? workspaceId = null)
        {
            LastStatsWorkspaceId = workspaceId;
            return Task.FromResult(Stats);
        }
    }
}