/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： InitTaskStoreUnitTest
*描述：云效 S2 任务态落盘（InitTaskStore）单测（设计 §4.4）——
*      原子写读回 / 重启模拟 / 部分重跑留痕 / TryBegin 互斥与终态放行 /
*      损坏 JSON 容错 / 路径注入（禁写真实 LocalApplicationData，只用临时目录）。
*
*****************************************************************************/

using LubanAgentWebHost.Services.Init;

namespace LubanAgent.Tests;

[TestClass]
public class InitTaskStoreUnitTest
{
    /// <summary>每用例独立的临时目录（红线：单测禁止写真实 LocalApplicationData）。</summary>
    private string _tempDir = null!;

    private string FilePath => Path.Combine(_tempDir, "yunxiao-init-tasks.json");

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "luban-initstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言结论
        }
    }

    private static InitTask NewTask(string codename = "NEWX", string id = "task-1") => new()
    {
        Id = id,
        Codename = codename,
        Status = InitTaskStatus.Running,
        WorkspaceId = "ws-1",
        SessionId = "sess-1",
        CreatedAt = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Local),
        Libs =
        [
            new LibState { Repo = "NEWX-Mixed", Status = LibStatus.Cloning, Detail = "克隆中" },
        ],
    };

    // ── 原子写读回 ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Upsert_原子写读回_字段与枚举不丢()
    {
        var store = new InitTaskStore(FilePath);
        var task = NewTask();
        task.Libs.Add(new LibState { Repo = "NEWX2-Mixed", Status = LibStatus.Failed, Detail = "推送被拒" });

        await store.UpsertAsync(task);
        var loaded = await store.GetAsync("task-1");

        Assert.IsNotNull(loaded);
        Assert.AreEqual("NEWX", loaded.Codename);
        Assert.AreEqual(InitTaskStatus.Running, loaded.Status);
        Assert.AreEqual("ws-1", loaded.WorkspaceId);
        Assert.AreEqual("sess-1", loaded.SessionId);
        Assert.IsNull(loaded.FinishedAt);
        Assert.AreEqual(2, loaded.Libs.Count);
        Assert.AreEqual(LibStatus.Cloning, loaded.Libs[0].Status);
        Assert.AreEqual("克隆中", loaded.Libs[0].Detail);
        Assert.AreEqual(LibStatus.Failed, loaded.Libs[1].Status);

        // 原子写：不留 .tmp 残骸
        Assert.IsFalse(File.Exists(FilePath + ".tmp"));
        Assert.IsTrue(File.Exists(FilePath));
    }

    [TestMethod]
    public async Task Upsert_终态任务_FinishedAt与枚举读回()
    {
        var store = new InitTaskStore(FilePath);
        var task = NewTask();
        task.Status = InitTaskStatus.Done;
        task.FinishedAt = new DateTime(2026, 11, 18, 11, 0, 0, DateTimeKind.Local);
        task.Libs[0].Status = LibStatus.Done;

        await store.UpsertAsync(task);
        var loaded = await store.GetAsync("task-1");

        Assert.IsNotNull(loaded);
        Assert.AreEqual(InitTaskStatus.Done, loaded.Status);
        Assert.AreEqual(task.FinishedAt, loaded.FinishedAt);
        Assert.AreEqual(LibStatus.Done, loaded.Libs[0].Status);
    }

    // ── 重启模拟 ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task 新实例读旧文件_重启后任务态仍在()
    {
        var store = new InitTaskStore(FilePath);
        await store.UpsertAsync(NewTask());

        var restarted = new InitTaskStore(FilePath);
        var loaded = await restarted.GetAsync("task-1");
        var byCodename = await restarted.ListByCodenameAsync("NEWX");

        Assert.IsNotNull(loaded);
        Assert.AreEqual("NEWX", loaded.Codename);
        Assert.AreEqual(1, byCodename.Count);
        Assert.AreEqual("task-1", byCodename[0].Id);
    }

    // ── 部分重跑留痕 ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Upsert_部分重跑_不丢历史Libs()
    {
        var store = new InitTaskStore(FilePath);
        var task = NewTask();
        task.Libs =
        [
            new LibState { Repo = "NEWX-Mixed", Status = LibStatus.Done, Detail = "已推送" },
            new LibState { Repo = "NEWX2-Mixed", Status = LibStatus.Failed, Detail = "推送被拒" },
        ];
        await store.UpsertAsync(task);

        // 重跑只带回失败库的新状态：历史库（NEWX-Mixed）必须留痕
        var retry = NewTask();
        retry.Libs = [new LibState { Repo = "NEWX2-Mixed", Status = LibStatus.Done, Detail = "重跑已推送" }];
        await store.UpsertAsync(retry);

        var loaded = await store.GetAsync("task-1");
        Assert.IsNotNull(loaded);
        Assert.AreEqual(2, loaded.Libs.Count);
        var history = loaded.Libs.First(l => l.Repo == "NEWX-Mixed");
        var rerun = loaded.Libs.First(l => l.Repo == "NEWX2-Mixed");
        Assert.AreEqual(LibStatus.Done, history.Status);
        Assert.AreEqual("已推送", history.Detail);
        Assert.AreEqual(LibStatus.Done, rerun.Status);
        Assert.AreEqual("重跑已推送", rerun.Detail);
    }

    // ── TryBegin 互斥与终态放行 ─────────────────────────────────────────────

    [TestMethod]
    public async Task TryBegin_进行中互斥_终态放行()
    {
        var store = new InitTaskStore(FilePath);

        Assert.IsTrue(await store.TryBeginAsync("NEWX"));
        // 占位已写入：无 FinishedAt、进行中
        var placeholder = (await store.ListByCodenameAsync("NEWX")).Single();
        Assert.IsNull(placeholder.FinishedAt);
        Assert.AreEqual(InitTaskStatus.Running, placeholder.Status);

        // 同代号有进行中任务 → 拒
        Assert.IsFalse(await store.TryBeginAsync("NEWX"));
        // 不同代号不受影响
        Assert.IsTrue(await store.TryBeginAsync("OTHER"));

        // 补全占位为终态后放行
        placeholder.Status = InitTaskStatus.Done;
        placeholder.FinishedAt = DateTime.Now;
        placeholder.Libs = [new LibState { Repo = "NEWX-Mixed", Status = LibStatus.Done }];
        await store.UpsertAsync(placeholder);

        Assert.IsTrue(await store.TryBeginAsync("NEWX"));
    }

    [TestMethod]
    public async Task TryBegin_Lib未收口_视为非终态()
    {
        var store = new InitTaskStore(FilePath);
        // FinishedAt 已置但仍有 Lib 停在中间态 → 仍算进行中（崩溃恢复语义）
        var task = NewTask();
        task.FinishedAt = DateTime.Now;
        task.Libs = [new LibState { Repo = "NEWX-Mixed", Status = LibStatus.Pushing }];
        await store.UpsertAsync(task);

        Assert.IsFalse(await store.TryBeginAsync("NEWX"));
    }

    // ── 损坏 JSON 容错 ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task 损坏JSON_当空处理_仍可继续使用()
    {
        File.WriteAllText(FilePath, "{ 这不是合法 JSON !!");

        var store = new InitTaskStore(FilePath);
        Assert.IsNull(await store.GetAsync("task-x"));
        Assert.AreEqual(0, (await store.ListByCodenameAsync("NEWX")).Count);

        // 容错后仍能正常写入
        Assert.IsTrue(await store.TryBeginAsync("NEWX"));
        Assert.AreEqual(1, (await store.ListByCodenameAsync("NEWX")).Count);
    }

    // ── 路径注入 ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task 路径注入_只写临时目录_不碰真实AppData()
    {
        var store = new InitTaskStore(FilePath);
        await store.UpsertAsync(NewTask());

        // 断言真实落点在 Path.GetTempPath() 下的随机目录（非 %LocalAppData%/LuBan）
        var fullPath = Path.GetFullPath(FilePath);
        StringAssert.StartsWith(fullPath, Path.GetFullPath(Path.GetTempPath()));
        Assert.IsTrue(File.Exists(fullPath));
        Assert.IsFalse(fullPath.Contains("LocalApplicationData", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ListByCodenameAsync_只回同代号任务()
    {
        var store = new InitTaskStore(FilePath);
        await store.UpsertAsync(NewTask("NEWX", "task-a"));
        await store.UpsertAsync(NewTask("NEWX", "task-b"));
        await store.UpsertAsync(NewTask("OTHER", "task-c"));

        var list = await store.ListByCodenameAsync("NEWX");

        Assert.AreEqual(2, list.Count);
        Assert.IsTrue(list.All(t => t.Codename == "NEWX"));
    }
}
