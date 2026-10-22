/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： YunxiaoResponseParserUnitTest
*描述：云效响应体解析器单测（2026-10-22 真机取证锁口径）
*      口径：成功＝裸数组 / 裸对象 / 裸标量（不套 result 外壳）；失败＝带 errorCode 的包裹体（errorCode 是字符串）
*****************************************************************************/

using System.Text.Json;
using System.Text.Json.Serialization;
using LubanAgentCore.Connectors.AliyunDevOps;

namespace LubanAgent.Tests;

/// <summary>
/// <see cref="YunxiaoResponseParser"/> 的契约单测：把「真机三形态 + 业务错误识别」钉死。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么单独测这个类</b>：<c>FlowClient</c> / <c>CodeupClient</c> 的解析入口都走它，而客户端方法依赖 HTTP、
/// 无法单测；把解析规则抽成纯函数后，这里就成了<b>唯一可测的缝合点</b>。
/// </para>
/// <para>
/// JSON 字面量全部来自 2026-10-22 的真机取证（组织 <c>648c2f9fb44b3d9a1d1684d9</c>）：
/// <c>GET /flow/.../pipelines</c> = 裸数组；<c>GET /pipelines/{id}</c> = 裸对象；<c>POST /pipelines</c> 成功 = 裸整数；
/// <c>PUT</c>/<c>DELETE</c> 成功 = 裸 <c>true</c>；失败才是
/// <c>{"errorCode":"InvalidPipeline.createOrUpdateError","errorMessage":"1209300 yaml校验失败…","requestId":"…"}</c>。
/// </para>
/// </remarks>
[TestClass]
public class YunxiaoResponseParserUnitTest
{
    [TestMethod]
    public void JsonOpts_UsesWebCamelCasePolicy_SoModelsMatchCloudFields()
    {
        // 与两个客户端同口径：web 命名策略（camelCase）+ 属性名忽略大小写
        Assert.AreSame(JsonNamingPolicy.CamelCase, YunxiaoResponseParser.JsonOpts.PropertyNamingPolicy);
        Assert.IsTrue(YunxiaoResponseParser.JsonOpts.PropertyNameCaseInsensitive);

        // JsonSerializerDefaults.Web 顺带把 NumberHandling 设成 AllowReadingFromString（.NET 的 Web 默认口径）：
        // 只允许「字符串 → 数字」，**不**允许「数字 → 字符串」，所以数字 id 仍必须用整型承载。
        Assert.AreEqual(JsonNumberHandling.AllowReadingFromString, YunxiaoResponseParser.JsonOpts.NumberHandling);
    }

    // ── 裸数组（列表接口）────────────────────────────────────────────────────

    [TestMethod]
    public void DeserializeList_BareArray_ReturnsItems()
    {
        const string Json = """
            [
              { "pipelineId": 3481236, "pipelineName": "A" },
              { "pipelineId": 3489337, "pipelineName": "B" }
            ]
            """;

        var items = YunxiaoResponseParser.DeserializeList<YunxiaoPipeline>(Json);

        Assert.IsNotNull(items);
        Assert.AreEqual(2, items!.Count);
        Assert.AreEqual("3481236", items[0].DisplayId);
        Assert.AreEqual("B", items[1].DisplayName);
    }

    [TestMethod]
    public void DeserializeList_NonArrayRoot_OrEmptyBody_ReturnsNull()
    {
        // 裸对象不是列表（例如把详情响应当列表传进来）
        Assert.IsNull(YunxiaoResponseParser.DeserializeList<YunxiaoPipeline>("""{ "id": 1 }"""));

        // 空体（204 / 空字符串）→ null（调用方按「没数据」处理）
        Assert.IsNull(YunxiaoResponseParser.DeserializeList<YunxiaoPipeline>(""));
        Assert.IsNull(YunxiaoResponseParser.DeserializeList<YunxiaoPipeline>("   "));
        Assert.IsNull(YunxiaoResponseParser.DeserializeList<YunxiaoPipeline>(null));

        // 空数组是「成功但无数据」，与 null 区分开
        var empty = YunxiaoResponseParser.DeserializeList<YunxiaoPipeline>("[]");
        Assert.IsNotNull(empty);
        Assert.AreEqual(0, empty!.Count);
    }

    // ── 裸对象（详情接口）────────────────────────────────────────────────────

    [TestMethod]
    public void DeserializeObject_BareObject_ReturnsInstance()
    {
        const string Json = """
            {
              "id": 3481236,
              "name": "CME_DEV_Deploy_TJCES-Service(7000)",
              "pipelineConfig": { "flow": "stages:\n  build:\n", "version": 1 }
            }
            """;

        var pipeline = YunxiaoResponseParser.DeserializeObject<YunxiaoPipeline>(Json, "/pipelines/3481236");

        Assert.IsNotNull(pipeline);
        Assert.AreEqual(3481236L, pipeline!.Id);
        Assert.IsNotNull(pipeline.PipelineConfig);
        StringAssert.Contains(pipeline.PipelineConfig!.Flow!, "stages:");
    }

    [TestMethod]
    public void DeserializeObject_EmptyBody_ReturnsDefault()
    {
        Assert.IsNull(YunxiaoResponseParser.DeserializeObject<YunxiaoPipeline>("", "/pipelines/1"));
        Assert.IsNull(YunxiaoResponseParser.DeserializeObject<YunxiaoPipeline>(null, "/pipelines/1"));
    }

    [TestMethod]
    public void DeserializeObject_MalformedJson_ThrowsApiException()
    {
        // 不静默返回 null：解析失败必须显式暴露，否则会被当成「数据不存在」
        var ex = Assert.ThrowsExactly<YunxiaoApiException>(
            () => YunxiaoResponseParser.DeserializeObject<YunxiaoPipeline>("{ not-json", "/pipelines/1"));

        Assert.AreEqual(0, ex.ErrorCode);
        StringAssert.Contains(ex.Message, "/pipelines/1");
    }

    // ── 裸标量 id（创建接口）──────────────────────────────────────────────────

    [TestMethod]
    public void ParseId_AcceptsBareNumberStringAndObject()
    {
        Assert.AreEqual("3481236", YunxiaoResponseParser.ParseId("3481236"));          // 真机：POST 成功 = 裸整数
        Assert.AreEqual("3481236", YunxiaoResponseParser.ParseId("\"3481236\""));      // 个别接口给字符串
        Assert.AreEqual("3481236", YunxiaoResponseParser.ParseId("""{ "id": 3481236 }""")); // 再包一层对象
        Assert.AreEqual("42", YunxiaoResponseParser.ParseId("""{ "id": " 42 " }"""));  // 字符串 id 去空白
    }

    [TestMethod]
    public void ParseId_ReturnsNull_ForNonIdShapes()
    {
        Assert.IsNull(YunxiaoResponseParser.ParseId("true"));      // PUT / DELETE 成功 = 裸 true
        Assert.IsNull(YunxiaoResponseParser.ParseId("[]"));
        Assert.IsNull(YunxiaoResponseParser.ParseId(""));
        Assert.IsNull(YunxiaoResponseParser.ParseId(null));
        Assert.IsNull(YunxiaoResponseParser.ParseId("""{ "name": "x" }"""));
    }

    // ── 业务错误识别 ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ParseBusinessError_StringErrorCode_FallsBackToHttpCodeInBody()
    {
        const string Json = """
            {
              "errorCode": "InvalidPipeline.createOrUpdateError",
              "errorMessage": "1209300 yaml校验失败：runsOn {} 不存在",
              "requestId": "abc",
              "code": 400
            }
            """;

        var error = YunxiaoResponseParser.ParseBusinessError(Json);

        Assert.IsNotNull(error);
        Assert.AreEqual(400, error!.Value.Code);                       // 字符串错误码转不出数字 → 用 code 兜底
        StringAssert.Contains(error.Value.Message, "yaml校验失败");
    }

    [TestMethod]
    public void ParseBusinessError_NumericErrorCode_IsUsedAsIs()
    {
        var error = YunxiaoResponseParser.ParseBusinessError(
            """{ "errorCode": 401, "errorMessage": "令牌无效" }""");

        Assert.IsNotNull(error);
        Assert.AreEqual(401, error!.Value.Code);
        Assert.AreEqual("令牌无效", error.Value.Message);
    }

    [TestMethod]
    public void ParseBusinessError_StringErrorCodeWithoutCode_DefaultsTo400()
    {
        var error = YunxiaoResponseParser.ParseBusinessError(
            """{ "errorCode": "SomeError", "errorDescription": "描述优先于兜底文案" }""");

        Assert.IsNotNull(error);
        Assert.AreEqual(400, error!.Value.Code);
        Assert.AreEqual("描述优先于兜底文案", error.Value.Message);
    }

    [TestMethod]
    public void ParseBusinessError_SuccessShapes_ReturnNull()
    {
        // 真机：成功响应根本没有 errorCode 字段
        Assert.IsNull(YunxiaoResponseParser.ParseBusinessError("""{ "result": [], "total": 0 }"""));

        // 云效把「0 / SUCCESS」也当成功
        Assert.IsNull(YunxiaoResponseParser.ParseBusinessError("""{ "errorCode": 0 }"""));
        Assert.IsNull(YunxiaoResponseParser.ParseBusinessError("""{ "errorCode": "0" }"""));
        Assert.IsNull(YunxiaoResponseParser.ParseBusinessError("""{ "errorCode": "SUCCESS" }"""));

        // 裸数组 / 裸标量 / 空体 / 非 JSON 文本都不算业务错误（交给调用方的 HTTP 分支处理）
        Assert.IsNull(YunxiaoResponseParser.ParseBusinessError("[1,2]"));
        Assert.IsNull(YunxiaoResponseParser.ParseBusinessError("true"));
        Assert.IsNull(YunxiaoResponseParser.ParseBusinessError(""));
        Assert.IsNull(YunxiaoResponseParser.ParseBusinessError(null));
        Assert.IsNull(YunxiaoResponseParser.ParseBusinessError("<html>502 Bad Gateway</html>"));
    }

    // ── 错误信息抽取 ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ExtractErrorMessage_PrefersKnownKeys_InOrder()
    {
        Assert.AreEqual("m", YunxiaoResponseParser.ExtractErrorMessage("""{ "errorMessage": "m", "message": "x" }"""));
        Assert.AreEqual("d", YunxiaoResponseParser.ExtractErrorMessage("""{ "errorDescription": "d", "message": "x" }"""));
        Assert.AreEqual("x", YunxiaoResponseParser.ExtractErrorMessage("""{ "message": "x" }"""));
    }

    [TestMethod]
    public void ExtractErrorMessage_ReturnsNull_WhenAbsentOrNotJson()
    {
        Assert.IsNull(YunxiaoResponseParser.ExtractErrorMessage("""{ "errorMessage": "" }"""));
        Assert.IsNull(YunxiaoResponseParser.ExtractErrorMessage("""{ "errorMessage": 42 }"""));   // 非字符串不取
        Assert.IsNull(YunxiaoResponseParser.ExtractErrorMessage("""{ "code": 500 }"""));
        Assert.IsNull(YunxiaoResponseParser.ExtractErrorMessage("[1,2]"));
        Assert.IsNull(YunxiaoResponseParser.ExtractErrorMessage("plain text error"));
        Assert.IsNull(YunxiaoResponseParser.ExtractErrorMessage(null));
    }
}