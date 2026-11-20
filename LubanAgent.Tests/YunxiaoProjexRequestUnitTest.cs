/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： YunxiaoProjexRequestUnitTest
*描述：锁定云效 Projex 迭代 / 工作项请求装配与响应解析口径
*      （workitems:search 的 conditions 为 JSON 字符串、迭代条件用 sprint 字段、分页窗口 10000）
*****************************************************************************/

namespace LubanAgent.Tests;

/// <summary>
/// 云效 Projex 请求装配 / 响应解析单测。
/// </summary>
[TestClass]
public class YunxiaoProjexRequestUnitTest
{
    private const string Org = "org-1";

    [TestMethod]
    public void BuildSearchWorkitemsRequest_含迭代与状态条件_conditions为JSON字符串()
    {
        var criteria = new YunxiaoWorkitemSearchCriteria
        {
            ProjectId = "proj-1",
            Category = "Req",
            SprintId = "sprint-1",
            Status = "status-1,status-2",
            Subject = "登录"
        };

        var (url, body) = ProjexClient.BuildSearchWorkitemsRequest(Org, criteria);

        Assert.AreEqual($"/oapi/v1/projex/organizations/{Org}/workitems:search", url);

        var payload = (Dictionary<string, object?>)body;
        Assert.AreEqual("proj-1", payload["spaceId"]);
        Assert.AreEqual("Req", payload["category"]);
        Assert.AreEqual("Project", payload["spaceType"]);

        var conditions = payload["conditions"] as string;
        Assert.IsFalse(string.IsNullOrWhiteSpace(conditions), "conditions 必须是 JSON 字符串（不是对象）");

        using var doc = JsonDocument.Parse(conditions!);
        var group = doc.RootElement.GetProperty("conditionGroups")[0];

        var sprint = group.EnumerateArray().Single(c => c.GetProperty("fieldIdentifier").GetString() == "sprint");
        Assert.AreEqual("sprint", sprint.GetProperty("className").GetString());
        Assert.AreEqual("CONTAINS", sprint.GetProperty("operator").GetString());
        CollectionAssert.AreEqual(new[] { "sprint-1" }, sprint.GetProperty("value").EnumerateArray().Select(v => v.GetString()).ToArray());

        var status = group.EnumerateArray().Single(c => c.GetProperty("fieldIdentifier").GetString() == "status");
        CollectionAssert.AreEqual(new[] { "status-1", "status-2" }, status.GetProperty("value").EnumerateArray().Select(v => v.GetString()).ToArray());

        var subject = group.EnumerateArray().Single(c => c.GetProperty("fieldIdentifier").GetString() == "subject");
        Assert.AreEqual("input", subject.GetProperty("format").GetString());
        Assert.AreEqual("登录", subject.GetProperty("value")[0].GetString());
    }

    [TestMethod]
    public void BuildSearchWorkitemsRequest_无过滤条件时不带conditions()
    {
        var criteria = new YunxiaoWorkitemSearchCriteria { ProjectId = "proj-1", Category = "Task" };

        var (_, body) = ProjexClient.BuildSearchWorkitemsRequest(Org, criteria);

        var payload = (Dictionary<string, object?>)body;
        Assert.IsFalse(payload.ContainsKey("conditions"), "无过滤条件时不应发送 conditions");
        Assert.AreEqual(1, payload["page"]);
        Assert.AreEqual(20, payload["perPage"]);
    }

    [TestMethod]
    public void BuildSearchWorkitemsRequest_perPage收敛到200()
    {
        var criteria = new YunxiaoWorkitemSearchCriteria { ProjectId = "proj-1", Category = "Req", PerPage = 500 };

        var (_, body) = ProjexClient.BuildSearchWorkitemsRequest(Org, criteria);

        Assert.AreEqual(200, ((Dictionary<string, object?>)body)["perPage"]);
    }

    [TestMethod]
    public void BuildSearchWorkitemsRequest_超出分页窗口抛异常()
    {
        var criteria = new YunxiaoWorkitemSearchCriteria { ProjectId = "proj-1", Category = "Req", Page = 100, PerPage = 200 };

        Assert.ThrowsExactly<ArgumentException>(
            () => ProjexClient.BuildSearchWorkitemsRequest(Org, criteria));
    }

    [TestMethod]
    public void BuildSearchWorkitemsRequest_缺项目或分类抛异常()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProjexClient.BuildSearchWorkitemsRequest(Org, new YunxiaoWorkitemSearchCriteria { Category = "Req" }));

        Assert.ThrowsExactly<ArgumentException>(() =>
            ProjexClient.BuildSearchWorkitemsRequest(Org, new YunxiaoWorkitemSearchCriteria { ProjectId = "proj-1" }));
    }

    [TestMethod]
    public void ParseSearchWorkitemsResponse_读取分页头与条目()
    {
        const string json = """
        [
          { "id": "w-1", "subject": "需求一", "categoryId": "Req", "sprint": { "id": "s-1", "name": "迭代一" } }
        ]
        """;

        var result = ProjexClient.ParseSearchWorkitemsResponse(json, total: 42, page: 2, perPage: 20);

        Assert.IsNotNull(result);
        Assert.AreEqual(42, result!.Total);
        Assert.AreEqual(2, result.Page);
        Assert.AreEqual(20, result.PerPage);
        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual("w-1", result.Items[0].Id);
        Assert.AreEqual("s-1", result.Items[0].Sprint?.Id);
    }

    [TestMethod]
    public void ParseSearchWorkitemsResponse_空体返回null()
    {
        Assert.IsNull(ProjexClient.ParseSearchWorkitemsResponse(null, 0, 0, 0));
        Assert.IsNull(ProjexClient.ParseSearchWorkitemsResponse("", 0, 0, 0));
    }
}
