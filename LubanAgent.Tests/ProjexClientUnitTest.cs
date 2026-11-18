/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： ProjexClientUnitTest
*描述：云效研发项目域（Projex）客户端单测 —— 只测纯函数（请求装配 / 响应解析），
*      不引 HttpMessageHandler 桩（照 YunxiaoResponseParserUnitTest 先例）
*****************************************************************************/

namespace LubanAgent.Tests;

/// <summary>
/// <see cref="ProjexClient"/> 的纯函数契约单测：把「搜索请求装配 + 分页响应解析 + 业务错误同口径」钉死。
/// </summary>
/// <remarks>
/// <b>为什么只测纯函数</b>：公开方法依赖 HTTP 无法单测，故把请求装配（<c>BuildSearchRequest</c>）
/// 与响应解析（<c>ParseSearchResponse</c>）抽成 internal static 纯函数，这里就是唯一可测的缝合点。
/// JSON 夹具按 Projex 搜索常见响应形态写（<c>{"result":[...],"total":N,"page":N,"perPage":N}</c>），
/// 字段名待 T11 实测校正后同步修订。
/// </remarks>
[TestClass]
public class ProjexClientUnitTest
{
    // ── BuildSearchRequest：URL ─────────────────────────────────────────────

    [TestMethod]
    public void BuildSearchRequest_Url_ContainsSearchActionAndOrganizationId()
    {
        var (url, _) = ProjexClient.BuildSearchRequest("62c41b29f4demo", 1, 20, null);

        StringAssert.Contains(url, "projects:search");
        StringAssert.Contains(url, "62c41b29f4demo");
    }

    // ── BuildSearchRequest：分页参数收敛 ───────────────────────────────────

    [TestMethod]
    public void BuildSearchRequest_PerPage_IsClampedToUpperBound100()
    {
        var (_, body) = ProjexClient.BuildSearchRequest("org", 1, 200, null);

        Assert.AreEqual(100, ReadBodyInt(body, "perPage"));
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

    // ── BuildSearchRequest：keyword ─────────────────────────────────────────

    [TestMethod]
    public void BuildSearchRequest_Keyword_NonEmpty_IsIncludedInBody()
    {
        var (_, body) = ProjexClient.BuildSearchRequest("org", 1, 20, "demo");

        Assert.AreEqual("demo", ReadBodyString(body, "keyword"));
    }

    [TestMethod]
    public void BuildSearchRequest_Keyword_NullOrWhitespace_IsOmittedFromBody()
    {
        Assert.IsFalse(BodyHasProperty(ProjexClient.BuildSearchRequest("org", 1, 20, null).Body, "keyword"));
        Assert.IsFalse(BodyHasProperty(ProjexClient.BuildSearchRequest("org", 1, 20, "").Body, "keyword"));
        Assert.IsFalse(BodyHasProperty(ProjexClient.BuildSearchRequest("org", 1, 20, "   ").Body, "keyword"));
    }

    // ── ParseSearchResponse：分页响应（常见形态夹具）─────────────────────────

    [TestMethod]
    public void ParseSearchResponse_PagedShape_YieldsItemsAndTotal()
    {
        const string Json = """
            {
              "result": [
                {
                  "id": "62c41b29f4aaaa",
                  "name": "示例项目A",
                  "identifier": "demo-a",
                  "description": "示例描述A",
                  "updateTime": "2026-11-01T10:00:00+08:00"
                },
                { "id": "62c41b29f4bbbb", "name": "示例项目B", "identifier": "demo-b" }
              ],
              "total": 2,
              "page": 1,
              "perPage": 20
            }
            """;

        var paged = ProjexClient.ParseSearchResponse(Json);

        Assert.IsNotNull(paged);
        Assert.AreEqual(2, paged!.Total);
        Assert.AreEqual(1, paged.Page);
        Assert.AreEqual(20, paged.PerPage);

        Assert.IsNotNull(paged.Items);
        Assert.AreEqual(2, paged.Items!.Count);
        Assert.AreEqual("62c41b29f4aaaa", paged.Items[0].Id);
        Assert.AreEqual("示例项目A", paged.Items[0].Name);
        Assert.AreEqual("demo-a", paged.Items[0].Identifier);
        Assert.AreEqual("示例描述A", paged.Items[0].Description);
        Assert.AreEqual("2026-11-01T10:00:00+08:00", paged.Items[0].UpdateTime);
        Assert.AreEqual("demo-b", paged.Items[1].Identifier);
    }

    [TestMethod]
    public void ParseSearchResponse_EmptyBody_ReturnsDefault()
    {
        Assert.IsNull(ProjexClient.ParseSearchResponse(""));
        Assert.IsNull(ProjexClient.ParseSearchResponse(null));
    }

    // ── 错误形态：走 YunxiaoResponseParser.ParseBusinessError 同口径 ────────

    [TestMethod]
    public void ParseBusinessError_ProjexErrorShape_IsDetected()
    {
        const string Json = """
            {
              "code": 400,
              "errorCode": "InvalidParameter",
              "errorMessage": "分页参数不合法",
              "requestId": "req-demo"
            }
            """;

        var error = YunxiaoResponseParser.ParseBusinessError(Json);

        Assert.IsNotNull(error);
        Assert.AreEqual(400, error!.Value.Code);
        StringAssert.Contains(error.Value.Message, "分页参数不合法");
    }

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
