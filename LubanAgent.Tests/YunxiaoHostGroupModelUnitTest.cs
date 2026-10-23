/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： YunxiaoHostGroupModelUnitTest
*描述：云效主机组（部署目标）模型反序列化口径单测（v0.32）
*      口径：列表 = 裸数组且 hostInfos 恒为 null；详情 = 裸对象且带 hostInfos；
*            id 是 JSON 数字（路径键必须是它，不是 uuid）
*****************************************************************************/

using LubanAgentCore.Connectors.AliyunDevOps;

namespace LubanAgent.Tests;

/// <summary>
/// <see cref="YunxiaoHostGroup"/> / <see cref="YunxiaoHostInfo"/> 的反序列化契约单测。
/// </summary>
/// <remarks>
/// <para>
/// 这组用例是<b>真机口径</b>的回归护栏（v0.32「创建流水线之前选部署目标」的只读数据源）。
/// 2026-10-22 只读取证确认三件事：① <c>GET /flow/organizations/{orgId}/hostGroups</c> 是<b>裸数组</b>，
/// 元素 <c>id</c> 是 <b>JSON 数字</b>、<c>hostInfos</c> <b>恒为 <c>null</c></b>、<c>serviceConnectionId</c>
/// 与 <c>aliyunRegion</c> 都可为 <c>null</c>；② 机器明细只在<b>详情</b>接口
/// <c>GET …/hostGroups/{数字id}</c> 里返回（裸对象）；③ 详情接口的路径键必须是数字 id——用 <c>uuid</c> 当路径键
/// 真机会 400（<c>NumberFormatException</c>），故模型必须把 uuid 与 id 分开承载、且 id 用整型。
/// </para>
/// <para>
/// 解析一律走 <see cref="YunxiaoResponseParser"/>（客户端的真实解析入口），顺带覆盖「裸数组 / 裸对象」两种形态。
/// 真实 JSON 来自只读取证（组织 <c>648c2f9fb44b3d9a1d1684d9</c>，主机组 <c>135860</c> 开发服务器组
/// 与 <c>226948</c> 移动云DEV主机组），此处以字面量固化，避免用例依赖网络。
/// </para>
/// </remarks>
[TestClass]
public class YunxiaoHostGroupModelUnitTest
{
    /// <summary>
    /// 列表接口原文（裸数组；<c>hostInfos</c> 恒为 <c>null</c>，id 为 JSON 数字）。
    /// </summary>
    private const string ListJson = """
        [
          {
            "creatorAccountId": "648c2ea5b97ac8ef22f7db09",
            "updateTime": 1688613797000,
            "hostNum": 1,
            "modifierAccountId": "648c2ea5b97ac8ef22f7db09",
            "description": null,
            "type": "ECS",
            "createTime": 1688613781000,
            "ecsType": "ECS_ALIYUN",
            "aliyunRegion": "cn-shanghai",
            "ecsLabelKey": "",
            "ecsLabelValue": "",
            "name": "开发服务器组",
            "id": 135860,
            "uuid": "ynjxmv6Z74491AzA",
            "serviceConnectionId": 496036,
            "hostInfos": null
          },
          {
            "creatorAccountId": "64a6638755ddb11ea28b33c7",
            "updateTime": 1727232937000,
            "hostNum": 1,
            "modifierAccountId": "64a6638755ddb11ea28b33c7",
            "description": null,
            "type": "ECS",
            "createTime": 1724838869000,
            "ecsType": "ECS_ALIYUN",
            "aliyunRegion": null,
            "ecsLabelKey": null,
            "ecsLabelValue": null,
            "name": "移动云DEV主机组",
            "id": 226948,
            "uuid": "tIPKOJBbwwOVKVia",
            "serviceConnectionId": null,
            "hostInfos": null
          }
        ]
        """;

    /// <summary>
    /// 详情接口原文（裸对象；带机器明细 <c>hostInfos</c>）。
    /// </summary>
    private const string DetailJson = """
        {
          "creatorAccountId": "64a6638755ddb11ea28b33c7",
          "updateTime": 1727232937000,
          "hostNum": 1,
          "modifierAccountId": "64a6638755ddb11ea28b33c7",
          "description": null,
          "type": "ECS",
          "createTime": 1724838869000,
          "ecsType": "ECS_ALIYUN",
          "aliyunRegion": null,
          "ecsLabelKey": null,
          "ecsLabelValue": null,
          "name": "移动云DEV主机组",
          "id": 226948,
          "uuid": "tIPKOJBbwwOVKVia",
          "serviceConnectionId": null,
          "hostInfos": [
            {
              "updateTime": 1792650289310,
              "creatorAccountId": null,
              "instanceName": "tjc-sh-100-dev-all-in-one",
              "modifierAccountId": null,
              "ip": "10.10.0.214",
              "createTime": 1792650289310,
              "objectType": "MachineInfo",
              "aliyunRegionId": "PUBLIC",
              "machineSn": "s-a488bcdf54aa4745ae33",
              "status": "ok"
            }
          ]
        }
        """;

    [TestMethod]
    public void ListShape_BareArray_CarriesNumericIdUuidAndNullHostInfos()
    {
        // 真机列表接口 = 裸数组（无 { result: ... } 外壳）
        var items = YunxiaoResponseParser.DeserializeList<YunxiaoHostGroup>(ListJson);

        Assert.IsNotNull(items);
        Assert.AreEqual(2, items!.Count);

        var devGroup = items[0];
        Assert.AreEqual(135860L, devGroup.Id, "数字 id 必须能反序列化（用 string? 会抛 JsonException）");
        Assert.AreEqual("ynjxmv6Z74491AzA", devGroup.Uuid, "部署阶段 machineGroup 要填的就是 uuid");
        Assert.AreEqual("开发服务器组", devGroup.Name);
        Assert.AreEqual("ECS", devGroup.Type);
        Assert.AreEqual("ECS_ALIYUN", devGroup.EcsType);
        Assert.AreEqual(1, devGroup.HostNum);
        Assert.AreEqual("cn-shanghai", devGroup.AliyunRegion);
        Assert.AreEqual(496036L, devGroup.ServiceConnectionId);
        Assert.IsNull(devGroup.HostInfos, "列表接口的 hostInfos 恒为 null（机器明细要另查详情）");

        var mobileDev = items[1];
        Assert.AreEqual(226948L, mobileDev.Id);
        Assert.AreEqual("tIPKOJBbwwOVKVia", mobileDev.Uuid, "Service 端默认部署目标（settings.pipelineDefaults[Service].deployMachineGroup）");
        Assert.AreEqual("移动云DEV主机组", mobileDev.Name);
        Assert.AreEqual(1, mobileDev.HostNum);
        Assert.IsNull(mobileDev.AliyunRegion, "真机观测可为 null");
        Assert.IsNull(mobileDev.ServiceConnectionId, "真机观测可为 null");
    }

    [TestMethod]
    public void DetailShape_BareObject_CarriesHostInfosWithInstanceIpAndStatus()
    {
        // 真机详情接口 = 裸对象（路径键为数字 id）
        var group = YunxiaoResponseParser.DeserializeObject<YunxiaoHostGroup>(
            DetailJson,
            "/oapi/v1/flow/organizations/648c2f9fb44b3d9a1d1684d9/hostGroups/226948");

        Assert.IsNotNull(group);
        Assert.AreEqual(226948L, group!.Id);
        Assert.AreEqual("tIPKOJBbwwOVKVia", group.Uuid);

        Assert.IsNotNull(group.HostInfos, "详情接口必须带出机器明细");
        Assert.AreEqual(1, group.HostInfos!.Count);

        var machine = group.HostInfos[0];
        Assert.AreEqual("tjc-sh-100-dev-all-in-one", machine.InstanceName);
        Assert.AreEqual("10.10.0.214", machine.Ip);
        Assert.AreEqual("ok", machine.Status);
        Assert.AreEqual("s-a488bcdf54aa4745ae33", machine.MachineSn);
        Assert.AreEqual("MachineInfo", machine.ObjectType);
        Assert.AreEqual("PUBLIC", machine.AliyunRegionId);
    }

    [TestMethod]
    public void ShapeGuards_ListParseOfObjectIsNull_AndStringFormIdIsTolerated()
    {
        // 形态护栏：详情（裸对象）不能当列表解析——避免「拿详情体喂列表方法」得到空列表的假成功
        Assert.IsNull(YunxiaoResponseParser.DeserializeList<YunxiaoHostGroup>(DetailJson));

        // AllowReadingFromString 容忍「字符串形式的数字」（真机给数字，这里锁住容错下限）
        var tolerant = YunxiaoResponseParser.DeserializeList<YunxiaoHostGroup>(
            """[{ "id": "226948", "uuid": "tIPKOJBbwwOVKVia", "name": "移动云DEV主机组" }]""");

        Assert.IsNotNull(tolerant);
        Assert.AreEqual(226948L, tolerant!.Single().Id);
    }
}