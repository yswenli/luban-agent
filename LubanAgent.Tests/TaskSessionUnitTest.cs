/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： TaskSessionUnitTest
*描述：任务与宿主工作区会话的有效绑定、持久化及路由契约单测
*****************************************************************************/

using System.Text.Json;
using LubanAgentWebHost.Controllers;
using LubanAgentWebHost.Services;
using LubanAgentWebHost.Services.TaskDetail;

namespace LubanAgent.Tests;

[TestClass]
public class TaskSessionUnitTest
{
    [TestMethod]
    public async Task Store_损坏JSON时Upsert抛出JsonException且原文件不变()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "sessions.json");
            await File.WriteAllTextAsync(path, "{");
            var original = await File.ReadAllBytesAsync(path);
            var store = new TaskSessionStore(path);

            await Assert.ThrowsExactlyAsync<JsonException>(
                () => store.UpsertAsync(Binding("link-corrupt", "task-corrupt", "session-corrupt")));

            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task Store_文件占用时Upsert抛出IOException且原文件不变()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "sessions.json");
            await File.WriteAllTextAsync(path, "[]");
            var original = await File.ReadAllBytesAsync(path);
            var store = new TaskSessionStore(path);
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await Assert.ThrowsExactlyAsync<IOException>(
                    () => store.UpsertAsync(Binding("link-locked", "task-locked", "session-locked")));

                Assert.IsFalse(File.Exists(path + ".tmp"), "读取失败不应进入写入");
            }

            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task Store_同一任务只保留最新有效绑定()
    {
        var root = TempRoot();
        try
        {
            var path = Path.Combine(root, "sessions.json");
            var store = new TaskSessionStore(path);
            await store.UpsertAsync(Binding("old-link", "task-1", "session-old"));
            await store.UpsertAsync(Binding("other-link", "task-2", "session-other"));
            await store.UpsertAsync(Binding("new-link", "task-1", "session-new"));

            var current = await store.GetAsync("task-1");
            Assert.IsNotNull(current);
            Assert.AreEqual("new-link", current.LinkId);
            Assert.AreEqual("session-new", current.SessionId);
            Assert.AreEqual(2, (await store.ListAsync()).Count);

            var reloaded = new TaskSessionStore(path);
            Assert.AreEqual("session-new", (await reloaded.GetAsync("task-1"))?.SessionId);
            Assert.AreEqual("session-other", (await reloaded.GetAsync("task-2"))?.SessionId);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task Service_新绑定补齐标识与首轮时间()
    {
        var root = TempRoot();
        try
        {
            var service = new TaskSessionService(new TaskSessionStore(Path.Combine(root, "sessions.json")));
            var input = Binding("", "task-1", "session-1");
            input.SeedAt = default;
            input.LastActiveAt = null;

            var saved = await service.SaveAsync(input);
            Assert.IsTrue(saved.Ok, saved.Message);
            Assert.IsFalse(string.IsNullOrWhiteSpace(saved.Data!.LinkId));
            Assert.AreNotEqual(default, saved.Data.SeedAt);
            Assert.IsNotNull(saved.Data.LastActiveAt);

            var loaded = await service.GetAsync("task-1");
            Assert.IsTrue(loaded.Ok, loaded.Message);
            Assert.AreEqual(saved.Data.LinkId, loaded.Data!.LinkId);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public async Task Service_缺少绑定必填字段时拒绝()
    {
        var root = TempRoot();
        try
        {
            var store = new TaskSessionStore(Path.Combine(root, "sessions.json"));
            var service = new TaskSessionService(store);
            var missing = Binding("", "task-1", "");
            var saved = await service.SaveAsync(missing);
            Assert.IsFalse(saved.Ok);
            Assert.AreEqual(400, saved.Code);
            Assert.AreEqual(0, (await store.ListAsync()).Count);

            var queried = await service.GetAsync(" ");
            Assert.IsFalse(queried.Ok);
            Assert.AreEqual(400, queried.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Routes_任务会话读写端点已暴露()
    {
        var controller = typeof(YunxiaoController);
        Assert.IsNotNull(controller.GetMethod("TaskSession"));
        Assert.IsNotNull(controller.GetMethod("TaskSessionSave"));
        Assert.IsNotNull(typeof(WebYunxiaoService).GetMethod("TaskSessionAsync"));
        Assert.IsNotNull(typeof(WebYunxiaoService).GetMethod("TaskSessionSaveAsync"));
    }

    private static TaskSession Binding(string linkId, string workitemId, string sessionId) => new()
    {
        LinkId = linkId,
        WorkItemId = workitemId,
        WorkspaceId = "ws-1",
        SessionId = sessionId,
        SeedDigest = "替换流水线｜描述 20 字｜DEMO-Mixed@develop",
        SeedCommentCount = 3,
        SeedAt = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Local),
        LastActiveAt = new DateTime(2026, 11, 20, 10, 1, 0, DateTimeKind.Local),
        Status = TaskSessionStatus.Created
    };

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-task-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
