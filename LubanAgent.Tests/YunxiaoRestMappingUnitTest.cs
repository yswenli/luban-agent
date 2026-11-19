/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： YunxiaoRestMappingUnitTest
*描述：云效 S2 REST 映射纯函数单测（批次 D 任务 9）——WebYunxiaoService.MapError：
*      YunxiaoWorkbenchResult → ApiEnvelope 错误码映射口径（400/404/409/503 + Ok 透传）。
*      说明：壳层其余部分依赖静态 WebApi.Runtime（起不来），不单测（S1 结论）；
*      本映射抽成纯函数后与壳层共用唯一口径。
*
*****************************************************************************/

namespace LubanAgent.Tests;

using LubanAgentWebHost.Services;

/// <summary>
/// <see cref="WebYunxiaoService.MapError{T}"/> 映射口径单测：
/// Ok → 200 数据透传；失败 → <c>Code &gt; 0 ? Code : 400</c>（400/404/409/503 全走此径）。
/// </summary>
[TestClass]
public class YunxiaoRestMappingUnitTest
{
    [TestMethod]
    public void 成功结果_Ok透传200与数据()
    {
        var envelope = WebYunxiaoService.MapError(YunxiaoWorkbenchResult<string>.Success("task-1"));

        Assert.IsTrue(envelope.Success);
        Assert.AreEqual(200, envelope.Code);
        Assert.IsNull(envelope.Message);
        Assert.AreEqual("task-1", envelope.GetData<string>());
    }

    [TestMethod]
    public void 失败400与404_按原语义码进响应壳()
    {
        var badRequest = WebYunxiaoService.MapError(YunxiaoWorkbenchResult<string>.Failure(400, "参数非法"));
        Assert.IsFalse(badRequest.Success);
        Assert.AreEqual(400, badRequest.Code);
        Assert.AreEqual("参数非法", badRequest.Message);

        var notFound = WebYunxiaoService.MapError(YunxiaoWorkbenchResult<string>.Failure(404, "任务不存在"));
        Assert.IsFalse(notFound.Success);
        Assert.AreEqual(404, notFound.Code);
        Assert.AreEqual("任务不存在", notFound.Message);
    }

    [TestMethod]
    public void 失败409_按原语义码进响应壳()
    {
        var conflict = WebYunxiaoService.MapError(
            YunxiaoWorkbenchResult<string>.Failure(409, "该项目代号已有进行中的初始化任务"));

        Assert.IsFalse(conflict.Success);
        Assert.AreEqual(409, conflict.Code);
        Assert.AreEqual("该项目代号已有进行中的初始化任务", conflict.Message);
    }

    [TestMethod]
    public void 失败503_按原语义码进响应壳且Code缺失兜底400()
    {
        var unavailable = WebYunxiaoService.MapError(
            YunxiaoWorkbenchResult<string>.Failure(503, "工作区会话通路不可用，无法执行 git 动作"));
        Assert.IsFalse(unavailable.Success);
        Assert.AreEqual(503, unavailable.Code);

        // Code 非正（未赋码）兜底 400，防壳层回 0/负状态码
        var fallback = WebYunxiaoService.MapError(YunxiaoWorkbenchResult<string>.Failure(0, "未分类失败"));
        Assert.IsFalse(fallback.Success);
        Assert.AreEqual(400, fallback.Code);
        Assert.AreEqual("未分类失败", fallback.Message);
    }
}
