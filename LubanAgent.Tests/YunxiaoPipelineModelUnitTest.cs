/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： YunxiaoPipelineModelUnitTest
*描述：云效流水线模型反序列化口径单测（2026-10-22 真机取证校正）
*      口径：列表接口 pipelineId/pipelineName 与详情接口 id/name 的<b>拼写差异 + 数字型 id</b>都要吃得住
*****************************************************************************/

using LubanAgentCore.Connectors.AliyunDevOps;

namespace LubanAgent.Tests;

/// <summary>
/// <see cref="YunxiaoPipeline"/> 的反序列化契约单测。
/// </summary>
/// <remarks>
/// <para>
/// 这组用例是<b>真机口径</b>的回归护栏。2026-10-22 复审发现一个隐患：真机两个接口的字段名并不一致
/// （列表 = <c>pipelineId</c> / <c>pipelineName</c>，详情 = <c>id</c> / <c>name</c>），且 id <b>都是 JSON 数字</b>；
/// 模型若把 id 声明为 <c>string?</c>，<c>JsonOpts</c>（<c>JsonSerializerDefaults.Web</c>）在反序列化数字时必抛
/// <c>JsonException</c>——注意 Web 默认虽带了 <c>NumberHandling.AllowReadingFromString</c>，但那只管「字符串 → 数字」，
/// <b>不</b>管「数字 → 字符串」，所以 id 必须用整型承载，步骤否则会在「列出流水线」这一步直接炸。
/// </para>
/// <para>
/// 解析一律走 <see cref="YunxiaoResponseParser"/>（客户端的真实解析入口），顺带把「裸数组 / 裸对象」
/// 两种真机形态也覆盖到（细节用例见 <c>YunxiaoResponseParserUnitTest</c>）。
/// </para>
/// <para>
/// 真实 JSON 来自只读取证（组织 <c>648c2f9fb44b3d9a1d1684d9</c>，参考流水线 <c>CME_DEV_Deploy_TJCES-Service(7000)</c> ＝
/// <c>pipelineId 3481236</c>），此处以字面量固化，避免用例依赖网络。
/// </para>
/// </remarks>
[TestClass]
public class YunxiaoPipelineModelUnitTest
{
    [TestMethod]
    public void ListShape_NumericPipelineIdAndPipelineName_DeserializeIntoDisplayId()
    {
        const string Json = """
            [
              {
                "createAccountId": "64a6638755ddb11ea28b33c7",
                "createTime": 1724925264000,
                "pipelineId": 3481236,
                "pipelineName": "CME_DEV_Deploy_TJCES-Service(7000)"
              }
            ]
            """;

        // 真机列表接口 = 裸数组（无 { result: ... } 外壳）
        var items = YunxiaoResponseParser.DeserializeList<YunxiaoPipeline>(Json);

        var item = items!.Single();
        Assert.AreEqual(3481236L, item.PipelineId, "数字 pipelineId 必须能反序列化（用 string? 会抛 JsonException）");
        Assert.AreEqual("3481236", item.DisplayId, "DisplayId 应把数字 id 归一化为不变文化的十进制字符串");
        Assert.AreEqual("CME_DEV_Deploy_TJCES-Service(7000)", item.DisplayName);
        Assert.AreEqual("64a6638755ddb11ea28b33c7", item.CreateAccountId);
    }

    [TestMethod]
    public void DetailShape_NumericIdAndName_DeserializeIntoDisplayId()
    {
        const string Json = """
            {
              "id": 3481236,
              "name": "CME_DEV_Deploy_TJCES-Service(7000)",
              "groupId": 42
            }
            """;

        // 真机详情接口 = 裸对象
        var pipeline = YunxiaoResponseParser.DeserializeObject<YunxiaoPipeline>(Json, "/oapi/v1/flow/organizations/o/pipelines/3481236")!;

        Assert.AreEqual(3481236L, pipeline.Id);
        Assert.AreEqual("3481236", pipeline.DisplayId);
        Assert.AreEqual("CME_DEV_Deploy_TJCES-Service(7000)", pipeline.DisplayName);
        Assert.AreEqual(42L, pipeline.GroupId);
    }

    [TestMethod]
    public void DisplayAccessors_PreferDetailFields_AndFallBackToListFields()
    {
        // 详情口径优先
        var detail = new YunxiaoPipeline { Id = 1, Name = "detail-name", PipelineId = 2, PipelineName = "list-name" };
        Assert.AreEqual(1L, detail.Id);
        Assert.AreEqual("1", detail.DisplayId);
        Assert.AreEqual("detail-name", detail.DisplayName);

        // 只有列表字段时退化（真机列表接口就是这一形态）
        var listOnly = new YunxiaoPipeline { PipelineId = 2, PipelineName = "list-name" };
        Assert.AreEqual("2", listOnly.DisplayId);
        Assert.AreEqual("list-name", listOnly.DisplayName);

        // 都没有 → null（调用方按「未命中」处理）
        var empty = new YunxiaoPipeline();
        Assert.IsNull(empty.DisplayId);
        Assert.IsNull(empty.DisplayName);
    }
}