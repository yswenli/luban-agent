/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： SkillCatalogServiceUnitTest
*创建时间：2026/11/23
*描述：三端 `@` 技能候选取数口径单测——锁死数据源语义：
*      1) 传 sessionId 时按“会话绑定工作区”加载，不能混入其他工作区的技能；
*      2) sessionId 缺失 / 查不到时回退当前工作区；
*      3) 结果带 IsBuiltin 标记，供三端区分内置与文件技能。
*
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class SkillCatalogServiceUnitTest
{
    private string _root = string.Empty;
    private string _workspaceA = string.Empty;
    private string _workspaceB = string.Empty;
    private string _current = string.Empty;
    private string _skillA = string.Empty;
    private string _skillB = string.Empty;
    private string _skillCurrent = string.Empty;
    private SkillCatalogService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "luban-skill-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _skillA = UniqueId("ws-a");
        _skillB = UniqueId("ws-b");
        _skillCurrent = UniqueId("ws-current");

        _workspaceA = NewWorkspace(_skillA);
        _workspaceB = NewWorkspace(_skillB);
        _current = NewWorkspace(_skillCurrent);

        var workspaces = new FakeWorkspaceManager(current: _current);
        workspaces.ById["ws-a"] = new WorkspaceInfo { WorkspaceId = "ws-a", RootPath = _workspaceA };
        workspaces.ById["ws-b"] = new WorkspaceInfo { WorkspaceId = "ws-b", RootPath = _workspaceB };

        var sessions = new FakeSessionWorkspaceQuery
        {
            WorkspaceBySession =
            {
                ["sess-a"] = "ws-a",
                ["sess-b"] = "ws-b",
            },
        };

        var registry = new SkillRegistry([new StubBuiltinSkill()], configReader: null);
        _service = new SkillCatalogService(registry, workspaces, sessions, new StubUserContext());
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 临时目录清理失败不影响断言 */ }
    }

    [TestMethod]
    public async Task ListAvailableAsync_UsesSessionWorkspace()
    {
        var items = await _service.ListAvailableAsync("sess-a");

        var ids = items.Select(s => s.Id).ToList();
        CollectionAssert.Contains(ids, _skillA, "必须包含会话工作区里的文件技能");
        Assert.IsFalse(ids.Contains(_skillB, StringComparer.OrdinalIgnoreCase),
            "不得混入其他工作区的技能");
        Assert.IsFalse(ids.Contains(_skillCurrent, StringComparer.OrdinalIgnoreCase),
            "会话已绑定工作区时不应回退当前工作区");
    }

    [TestMethod]
    public async Task ListAvailableAsync_SwitchesWithSessionWorkspace()
    {
        var items = await _service.ListAvailableAsync("sess-b");

        var ids = items.Select(s => s.Id).ToList();
        CollectionAssert.Contains(ids, _skillB);
        Assert.IsFalse(ids.Contains(_skillA, StringComparer.OrdinalIgnoreCase),
            "同一注册表按新会话重新加载后不得残留上一个工作区的技能");
    }

    [TestMethod]
    public async Task ListAvailableAsync_FallsBackToCurrentWorkspace()
    {
        foreach (var sessionId in new[] { null, "", "  ", "sess-unknown" })
        {
            var items = await _service.ListAvailableAsync(sessionId);

            CollectionAssert.Contains(items.Select(s => s.Id).ToList(), _skillCurrent,
                $"sessionId={sessionId ?? "<null>"} 时应回退当前工作区");
        }
    }

    [TestMethod]
    public async Task ListAvailableAsync_MarksBuiltinAndFileSkills()
    {
        var items = await _service.ListAvailableAsync("sess-a");

        Assert.IsTrue(items.Single(s => s.Id == "stub-builtin").IsBuiltin, "内置技能应标记 IsBuiltin");
        Assert.IsFalse(items.Single(s => s.Id == _skillA).IsBuiltin, "文件技能不应标记内置");
        Assert.AreEqual("Stub 内置技能", items.Single(s => s.Id == "stub-builtin").Name);
    }

    private string UniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>创建带一个工作区级 SKILL.md 的工作区根目录。</summary>
    private string NewWorkspace(string skillId)
    {
        var root = Path.Combine(_root, skillId);
        var dir = Path.Combine(root, ".luban-agent", "skills", skillId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "SKILL.md"),
            "---" + Environment.NewLine
            + $"name: {skillId}" + Environment.NewLine
            + "description: 工作区技能" + Environment.NewLine
            + "category: 测试" + Environment.NewLine
            + "---" + Environment.NewLine
            + "正文");
        return root;
    }

    private sealed class FakeSessionWorkspaceQuery : ISessionWorkspaceQuery
    {
        public Dictionary<string, string> WorkspaceBySession { get; } = new(StringComparer.Ordinal);

        public Task<List<DbSession>> GetByWorkspaceAsync(string workspaceId, string userId)
            => throw new NotSupportedException("候选查询只用到会话 → 工作区的单点映射");

        public Task<string?> GetSessionWorkspaceIdAsync(string sessionId, string userId)
            => Task.FromResult<string?>(
                WorkspaceBySession.TryGetValue(sessionId, out var id) ? id : null);
    }

    private sealed class FakeWorkspaceManager(string current) : IWorkspaceManager
    {
        public WorkspaceInfo? CurrentWorkspace { get; } =
            new() { WorkspaceId = "ws-current", RootPath = current };

        public Dictionary<string, WorkspaceInfo> ById { get; } = new(StringComparer.OrdinalIgnoreCase);

        public event Action<WorkspaceInfo>? CurrentWorkspaceChanged { add { } remove { } }

        public Task<WorkspaceInfo?> GetWorkspaceAsync(string workspaceId)
            => Task.FromResult(ById.TryGetValue(workspaceId, out var ws) ? ws : null);

        public Task<WorkspaceInfo> CreateWorkspaceAsync(string rootPath, string? name = null, string type = "Normal")
            => throw new NotSupportedException();

        public Task SwitchWorkspaceAsync(string workspaceId) => throw new NotSupportedException();

        public Task<IEnumerable<WorkspaceInfo>> GetUserWorkspacesAsync() => throw new NotSupportedException();

        public Task<WorkspaceInfo?> GetByRootPathAsync(string rootPath) => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkspaceInfo>> SearchByNameAsync(string keyword) => throw new NotSupportedException();

        public Task<bool> EnsureAuthorizedAsync(WorkspaceInfo workspace) => throw new NotSupportedException();

        public Task EnsureConfigDirectoryAsync(WorkspaceInfo workspace) => throw new NotSupportedException();

        public Task SetCurrentAsync(string workspaceId) => throw new NotSupportedException();

        public Task<bool> DeleteWorkspaceAsync(string workspaceId) => throw new NotSupportedException();

        public Task<bool> RenameWorkspaceAsync(string workspaceId, string name) => throw new NotSupportedException();

        public string? GetKnownRootPath(string? workspaceId) => null;

        public bool IsWithinWorkspace(string path) => false;
    }

    private sealed class StubUserContext : LuBan.AIAgent.Abstractions.IUserContext
    {
        public string? UserId => "unit-test-user";
    }

    private sealed class StubBuiltinSkill : ISkill
    {
        public string Id => "stub-builtin";

        public string Name => "Stub 内置技能";

        public string Description => "测试用内置技能";

        public string Category => "测试";

        public IEnumerable<string> Examples => Array.Empty<string>();

        public IEnumerable<string> TriggerKeywords => Array.Empty<string>();

        public Task<SkillResult> ExecuteAsync(SkillContext context, string input)
            => Task.FromResult(new SkillResult { Success = true });
    }
}