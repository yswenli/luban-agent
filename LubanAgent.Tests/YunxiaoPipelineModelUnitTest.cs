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
/// JSON 结构按只读取证还原，其中的组织 ID / 流水线 id / 名称<b>已替换为测试假值</b>，
/// 避免把组织专有值固化进仓库。
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
                "createAccountId": "000000000000000000000002",
                "createTime": 1724925264000,
                "pipelineId": 1000001,
                "pipelineName": "DEMO_DEV_Deploy_Demo-Service(7000)"
              }
            ]
            """;

        // 真机列表接口 = 裸数组（无 { result: ... } 外壳）
        var items = YunxiaoResponseParser.DeserializeList<YunxiaoPipeline>(Json);

        var item = items!.Single();
        Assert.AreEqual(1000001L, item.PipelineId, "数字 pipelineId 必须能反序列化（用 string? 会抛 JsonException）");
        Assert.AreEqual("1000001", item.DisplayId, "DisplayId 应把数字 id 归一化为不变文化的十进制字符串");
        Assert.AreEqual("DEMO_DEV_Deploy_Demo-Service(7000)", item.DisplayName);
        Assert.AreEqual("000000000000000000000002", item.CreateAccountId);
    }

    [TestMethod]
    public void DetailShape_NumericIdAndName_DeserializeIntoDisplayId()
    {
        const string Json = """
            {
              "id": 1000001,
              "name": "DEMO_DEV_Deploy_Demo-Service(7000)",
              "groupId": 42
            }
            """;

        // 真机详情接口 = 裸对象
        var pipeline = YunxiaoResponseParser.DeserializeObject<YunxiaoPipeline>(Json, "/oapi/v1/flow/organizations/0123456789abcdef01234567/pipelines/1000001")!;

        Assert.AreEqual(1000001L, pipeline.Id);
        Assert.AreEqual("1000001", pipeline.DisplayId);
        Assert.AreEqual("DEMO_DEV_Deploy_Demo-Service(7000)", pipeline.DisplayName);
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