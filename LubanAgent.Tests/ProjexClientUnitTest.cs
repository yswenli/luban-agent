/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： ProjexClientUnitTest
*描述：云效研发项目域（Projex）客户端单测 —— 只测纯函数（请求装配 / 响应解析），
*      不引 HttpMessageHandler 桩（照 YunxiaoResponseParserUnitTest 先例）
*口径：2026-11-18 真机取证 —— 搜索响应为裸 JSON 数组（分页在 X-Total/X-Page/X-Per-Page
*      响应头），名称检索经 conditions 的 conditionGroups JSON 串，perPage 界值 [1,200]
*****************************************************************************/

namespace LubanAgent.Tests;

/// <summary>
/// <see cref="ProjexClient"/> 的纯函数契约单测：把「搜索请求装配 + 裸数组响应解析 + 业务错误同口径」钉死。
/// </summary>
/// <remarks>
/// <b>为什么只测纯函数</b>：公开方法依赖 HTTP 无法单测，故把请求装配（<c>BuildSearchRequest</c>）
/// 与响应解析（<c>ParseSearchResponse</c>）抽成 internal static 纯函数，这里就是唯一可测的缝合点。
/// JSON 夹具按 2026-11-18 真机响应形态写（裸数组 + <c>customCode</c> / <c>gmtCreate</c> / <c>gmtModified</c> 毫秒），
/// 值一律脱敏为占位符。
/// </remarks>
[TestClass]
public class ProjexClientUnitTest
{
    // ── BuildSearchRequest：URL ─────────────────────────────────────────────

    [TestMethod]
    public void BuildSearchRequest_Url_ContainsSearchActionAndOrganizationId()
    {
        var (url, _) = ProjexClient.BuildSearchRequest("org-demo-0001", 1, 20, null);

        StringAssert.Contains(url, "projects:search");
        StringAssert.Contains(url, "org-demo-0001");
    }

    // ── BuildSearchRequest：分页参数收敛 ───────────────────────────────────

    [TestMethod]
    public void BuildSearchRequest_PerPage_IsClampedToUpperBound200()
    {
        var (_, body) = ProjexClient.BuildSearchRequest("org", 1, 500, null);

        Assert.AreEqual(200, ReadBodyInt(body, "perPage"));
    }

    [TestMethod]
    public void BuildSearchRequest_PerPage_IsRaisedToFloor1()
    {
        var (_, body) = ProjexClient.BuildSearchRequest("org", 1, 0, null);

        Assert.AreEqual(1, ReadBodyInt(body, "perPage"));
    }

    [TestMethod]
    public void BuildSearchRequest_Page_FloorIs1_AndHigherValuesPassThrough()
    {
        Assert.AreEqual(1, ReadBodyInt(ProjexClient.BuildSearchRequest("org", 0, 20, null).Body, "page"));
        Assert.AreEqual(1, ReadBodyInt(ProjexClient.BuildSearchRequest("org", -3, 20, null).Body, "page"));
        Assert.AreEqual(5, ReadBodyInt(ProjexClient.BuildSearchRequest("org", 5, 20, null).Body, "page"));
    }

    // ── BuildSearchRequest：keyword → conditions 名称过滤 ────────────────────

    [TestMethod]
    public void BuildSearchRequest_Keyword_NonEmpty_ProducesNameFilterConditions()
    {
        var (_, body) = ProjexClient.BuildSearchRequest("org", 1, 20, "demo");

        // conditions 是 JSON 串（官方文档 conditionGroups 形态：name 字段 + BETWEEN + 值数组）
        var conditions = ReadBodyString(body, "conditions");
        Assert.IsNotNull(conditions);

        using var doc = JsonDocument.Parse(conditions!);
        var filter = doc.RootElement
            .GetProperty("conditionGroups")[0][0];

        Assert.AreEqual("name", filter.GetProperty("fieldIdentifier").GetString());
        Assert.AreEqual("input", filter.GetProperty("format").GetString());
        Assert.AreEqual("BETWEEN", filter.GetProperty("operator").GetString());
        Assert.AreEqual("demo", filter.GetProperty("value")[0].GetString());

        // 真机形态没有 keyword 字段
        Assert.IsFalse(BodyHasProperty(body, "keyword"));
    }

    [TestMethod]
    public void BuildSearchRequest_Keyword_NullOrWhitespace_OmitsConditions()
    {
        Assert.IsFalse(BodyHasProperty(ProjexClient.BuildSearchRequest("org", 1, 20, null).Body, "conditions"));
        Assert.IsFalse(BodyHasProperty(ProjexClient.BuildSearchRequest("org", 1, 20, "").Body, "conditions"));
        Assert.IsFalse(BodyHasProperty(ProjexClient.BuildSearchRequest("org", 1, 20, "   ").Body, "conditions"));
    }

    // ── ParseSearchResponse：裸数组 + 响应头分页（真机形态夹具）──────────────

    [TestMethod]
    public void ParseSearchResponse_BareArrayWithHeaderTotals_YieldsItemsAndTotal()
    {
        var paged = ProjexClient.ParseSearchResponse(BareArrayJson, total: 29, page: 1, perPage: 20);

        Assert.IsNotNull(paged);
        Assert.AreEqual(29, paged!.Total);
        Assert.AreEqual(1, paged.Page);
        Assert.AreEqual(20, paged.PerPage);

        Assert.IsNotNull(paged.Items);
        Assert.AreEqual(2, paged.Items!.Count);
        Assert.AreEqual("proj-demo-000000000001", paged.Items[0].Id);
        Assert.AreEqual("示例项目A", paged.Items[0].Name);
        Assert.AreEqual("DEMOA", paged.Items[0].CustomCode);
        Assert.AreEqual("示例描述A", paged.Items[0].Description);
        Assert.AreEqual("NORMAL", paged.Items[0].LogicalStatus);
        Assert.AreEqual("进行中", paged.Items[0].Status!.Name);
        Assert.AreEqual("用户甲", paged.Items[0].Creator!.Name);
        Assert.AreEqual("DEMOB", paged.Items[1].CustomCode);
    }

    [TestMethod]
    public void ParseSearchResponse_MissingHeaderTotals_FallsBackToItemsCount()
    {
        var paged = ProjexClient.ParseSearchResponse(BareArrayJson, total: 0, page: 0, perPage: 0);

        Assert.IsNotNull(paged);
        Assert.AreEqual(2, paged!.Total);
        Assert.AreEqual(1, paged.Page);
        Assert.AreEqual(2, paged.PerPage);
    }

    [TestMethod]
    public void ParseSearchResponse_EmptyBody_ReturnsDefault()
    {
        Assert.IsNull(ProjexClient.ParseSearchResponse("", 1, 1, 1));
        Assert.IsNull(ProjexClient.ParseSearchResponse(null, 1, 1, 1));
    }

    [TestMethod]
    public void ParseSearchResponse_RealShape_MapsCustomCodeAndEpochTimes()
    {
        var paged = ProjexClient.ParseSearchResponse(BareArrayJson, 2, 1, 20);

        // 项目标识统一取 customCode
        Assert.AreEqual("DEMOA", paged!.Items![0].DisplayIdentifier);

        // gmtModified 为空 → 更新时间退化 gmtCreate
        Assert.AreEqual(1761900000000, paged.Items[0].DisplayUpdateTime);
        Assert.IsNull(paged.Items[0].GmtModified);

        // gmtModified 有值 → 取它
        Assert.AreEqual(1762000000000, paged.Items[1].DisplayUpdateTime);
    }

    // ── BuildCreateCommentRequest：评论创建协议 ────────────────────────────

    [TestMethod]
    public void BuildCreateCommentRequest_使用同一路径Post与Markdown正文()
    {
        var (url, body) = ProjexClient.BuildCreateCommentRequest(
            "org demo", "work/item", "  首行\n第二行  ", "parent-1");

        Assert.AreEqual("/oapi/v1/projex/organizations/org%20demo/workitems/work%2Fitem/comments", url);
        Assert.AreEqual("首行\n第二行", ReadBodyString(body, "content"));
        Assert.AreEqual("MARKDOWN", ReadBodyString(body, "formatType"));
        Assert.AreEqual("parent-1", ReadBodyString(body, "parentId"));
    }

    [TestMethod]
    public void BuildCreateCommentRequest_无父评论时省略parentId()
    {
        var (_, body) = ProjexClient.BuildCreateCommentRequest("org", "work-1", "评论", null);

        Assert.IsFalse(BodyHasProperty(body, "parentId"));
    }

    [TestMethod]
    public void BuildCreateCommentRequest_空白正文抛异常()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProjexClient.BuildCreateCommentRequest("org", "work-1", "  ", null));
    }

    // ── 错误形态：走 YunxiaoResponseParser.ParseBusinessError 同口径 ────────

    [TestMethod]
    public void ParseBusinessError_ProjexErrorShape_IsDetected()
    {
        // 真机错误形态（2026-11-18 取证）：errorCode 为字符串、无 code 字段
        const string Json = """
            {
              "errorCode": "Forbidden.InvalidUser.UserNotInCurrentOrganization",
              "errorMessage": "The current user is not in the organization and has no right to operate.",
              "requestId": "req-demo"
            }
            """;

        var error = YunxiaoResponseParser.ParseBusinessError(Json);

        Assert.IsNotNull(error);
        StringAssert.Contains(error.Value.Message, "has no right to operate");
    }

    // ── 夹具：真机响应形态（值已脱敏为占位符）──────────────────────────────

    private const string BareArrayJson = """
        [
          {
            "creator": { "name": "用户甲", "id": "user-0001" },
            "modifier": null,
            "gmtCreate": 1761900000000,
            "gmtModified": null,
            "name": "示例项目A",
            "description": "示例描述A",
            "scope": "public",
            "customCode": "DEMOA",
            "icon": "https://example.invalid/icon-a.png",
            "logicalStatus": "NORMAL",
            "status": { "name": "进行中", "id": "status-0001" },
            "customFieldValues": null,
            "id": "proj-demo-000000000001"
          },
          {
            "creator": { "name": "用户乙", "id": "user-0002" },
            "modifier": { "name": "用户乙", "id": "user-0002" },
            "gmtCreate": 1761800000000,
            "gmtModified": 1762000000000,
            "name": "示例项目B",
            "description": "",
            "scope": "public",
            "customCode": "DEMOB",
            "icon": "https://example.invalid/icon-b.png",
            "logicalStatus": "NORMAL",
            "status": { "name": "进行中", "id": "status-0001" },
            "customFieldValues": null,
            "id": "proj-demo-000000000002"
          }
        ]
        """;

    // ── 夹具辅助：按与客户端同口径的 JsonOpts 序列化请求体后读字段 ─────────

    private static JsonElement ReadBody(object body)
        => JsonSerializer.SerializeToElement(body, YunxiaoResponseParser.JsonOpts);

    private static int ReadBodyInt(object body, string name)
        => ReadBody(body).GetProperty(name).GetInt32();

    private static string? ReadBodyString(object body, string name)
        => ReadBody(body).GetProperty(name).GetString();

    private static bool BodyHasProperty(object body, string name)
        => ReadBody(body).TryGetProperty(name, out _);
}
