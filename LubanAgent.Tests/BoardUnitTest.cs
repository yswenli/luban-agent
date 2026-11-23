/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： BoardUnitTest
*描述：S4 迭代任务看板纯函数契约单测
*      覆盖：迭代排序与选择、列内排序（空值置末）、完成态判定、预计完成日期解析
*****************************************************************************/

using LubanAgentCore.Connectors.AliyunDevOps;
using LubanAgentWebHost.Services.Board;

namespace LubanAgent.Tests;

/// <summary>
/// S4 看板纯函数单测：排序、选迭代、完成态与日期解析。
/// </summary>
/// <remarks>
/// 与 <see cref="PrdS3UnitTest"/> 同口径：只测 internal static 纯函数，不引入 HTTP 桩。
/// 列构建依赖云效实例反查，属集成范畴，不在此测。
/// </remarks>
[TestClass]
public class BoardUnitTest
{
    // ── 迭代排序与选择 ────────────────────────────────────────────────────

    [TestMethod]
    public void OrderSprints_DoingFirst_ThenStartDateDesc()
    {
        var sprints = new List<YunxiaoSprint>
        {
            new() { Id = "s1", Name = "旧迭代", Status = "ARCHIVED", StartDate = "2026-09-01" },
            new() { Id = "s2", Name = "进行中", Status = "DOING", StartDate = "2026-11-01" },
            new() { Id = "s3", Name = "待开始", Status = "TODO", StartDate = "2026-12-01" },
            new() { Id = "s4", Name = "另一个进行中", Status = "DOING", StartDate = "2026-11-15" }
        };

        var ordered = BoardService.OrderSprints(sprints);

        Assert.AreEqual("s4", ordered[0].Id, "进行中内部按开始日期倒序");
        Assert.AreEqual("s2", ordered[1].Id);
        Assert.AreEqual("s3", ordered[2].Id, "非进行中按开始日期倒序");
        Assert.AreEqual("s1", ordered[3].Id);
    }

    [TestMethod]
    public void OrderSprints_SkipsBlankId_AndFallsBackToIdAsName()
    {
        var ordered = BoardService.OrderSprints(
        [
            new YunxiaoSprint { Id = " ", Name = "空 id" },
            new YunxiaoSprint { Id = "s1", Name = null }
        ]);

        Assert.AreEqual(1, ordered.Count);
        Assert.AreEqual("s1", ordered[0].Id);
        Assert.AreEqual("s1", ordered[0].Name, "名称为空时回落为 ID");
    }

    [TestMethod]
    public void PickSprint_PrefersRequested_ThenDoing_ThenFirst()
    {
        var sprints = new List<BoardSprintOption>
        {
            new() { Id = "s1", Name = "待开始", Status = "TODO" },
            new() { Id = "s2", Name = "进行中", Status = "DOING" }
        };

        Assert.AreEqual("s1", BoardService.PickSprint(sprints, "s1")!.Id, "请求指定的迭代优先");
        Assert.AreEqual("s2", BoardService.PickSprint(sprints, "不存在")!.Id, "未命中时取进行中");
        Assert.AreEqual("s2", BoardService.PickSprint(sprints, null)!.Id);
        Assert.IsNull(BoardService.PickSprint([], "s1"));
    }

    // ── 列内排序 ──────────────────────────────────────────────────────────

    [TestMethod]
    public void SortTasks_DueDateAscending_BlankLast()
    {
        var tasks = new List<BoardTaskItem>
        {
            new() { SerialNumber = "P-3", DueDate = null },
            new() { SerialNumber = "P-2", DueDate = "2026-11-25" },
            new() { SerialNumber = "P-1", DueDate = "2026-11-18" },
            new() { SerialNumber = "P-4", DueDate = "  " }
        };

        var sorted = BoardService.SortTasks(tasks);

        CollectionAssert.AreEqual(
            new[] { "P-1", "P-2", "P-3", "P-4" },
            sorted.Select(t => t.SerialNumber).ToArray(),
            "有日期的按升序在前，空值按编号稳定置末");
    }

    [TestMethod]
    public void SortTasks_SameDate_SortsBySerial()
    {
        var tasks = new List<BoardTaskItem>
        {
            new() { SerialNumber = "MGQH-10232", DueDate = "2026-11-18" },
            new() { SerialNumber = "MGQH-10231", DueDate = "2026-11-18" }
        };

        var sorted = BoardService.SortTasks(tasks);

        Assert.AreEqual("MGQH-10231", sorted[0].SerialNumber, "日期相同时按编号升序");
    }

    // ── 完成态 ────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsDoneStatus_MatchesEnAndChineseNames()
    {
        Assert.IsTrue(BoardService.IsDoneStatus("DONE", "已完成"));
        Assert.IsTrue(BoardService.IsDoneStatus("done", null), "英文名不区分大小写");
        Assert.IsTrue(BoardService.IsDoneStatus(null, "已关闭"));
        Assert.IsTrue(BoardService.IsDoneStatus(null, "已归档"));
        Assert.IsFalse(BoardService.IsDoneStatus("DOING", "进行中"));
        Assert.IsFalse(BoardService.IsDoneStatus(null, null));
    }

    // ── 迭代日期字段兼容 ──────────────────────────────────────────────────

    [TestMethod]
    public void SprintDates_AcceptEpochMillisecondsAndDateText()
    {
        var epoch = new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();
        var json = $$"""{"id":"s1","name":"迭代","startDate":{{epoch}},"endDate":"2026-11-26"}""";

        var sprint = System.Text.Json.JsonSerializer.Deserialize<YunxiaoSprint>(
            json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.IsNotNull(sprint);
        Assert.AreEqual("2026-11-19", sprint!.StartDate, "epoch 毫秒要转成 yyyy-MM-dd");
        Assert.AreEqual("2026-11-26", sprint.EndDate, "日期字符串原样归一");
    }

    // ── 预计完成日期解析 ──────────────────────────────────────────────────

    [TestMethod]
    public void ParseDueDate_SupportsDateTextWithTimeEpochAndInvalid()
    {
        Assert.AreEqual("2026-11-18", BoardService.ParseDueDate("2026-11-18"));
        Assert.AreEqual("2026-11-18", BoardService.ParseDueDate("2026-11-18 15:30:00"));

        var epoch = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();
        Assert.AreEqual("2026-11-18", BoardService.ParseDueDate(epoch.ToString()));

        Assert.IsNull(BoardService.ParseDueDate(null));
        Assert.IsNull(BoardService.ParseDueDate("  "));
        Assert.AreEqual("abc-def-gh", BoardService.ParseDueDate("abc-def-gh"),
            "无法解析为日期时按前 10 位降级展示");
    }
}
