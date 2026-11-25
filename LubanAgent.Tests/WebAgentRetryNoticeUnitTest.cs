/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebAgentRetryNoticeUnitTest
*描述：Web 宿主侧的 LLM 失败文案与自动重试提示帧
*
*****************************************************************************/

using LuBan.AIAgent.Errors;

using LubanAgentWebHost.Hosting;
using LubanAgentWebHost.Services;

namespace LubanAgent.Tests;

[TestClass]
public class WebAgentRetryNoticeUnitTest
{
    /// <summary>构造一个网络失败的分类结果（与真实链路一致：由分类器对传输层异常给出）。</summary>
    private static AgentApiException NetworkFailure()
        => new(ApiErrorClassifier.Classify(new HttpRequestException("connected host has failed to respond"), false));

    [TestMethod]
    public void 回传分类文案而不是兜底文案()
    {
        var api = NetworkFailure();

        Assert.AreEqual(api.Error.FriendlyMessage, WebApi.SafeErrorMessage(api));
        StringAssert.Contains(WebApi.SafeErrorMessage(api), "网络连接失败");
    }

    [TestMethod]
    public void 分类异常被外层包装时仍回传分类文案()
    {
        var api = NetworkFailure();

        Assert.AreEqual(api.Error.FriendlyMessage,
            WebApi.SafeErrorMessage(new InvalidOperationException("回合收尾失败", api)));
        Assert.AreEqual(api.Error.FriendlyMessage,
            WebApi.SafeErrorMessage(new AggregateException("Retry failed after 3 tries.", api)));
    }

    [TestMethod]
    public void 未分类异常保留兜底文案()
        => Assert.AreEqual("服务端处理失败，请查看服务端日志",
            WebApi.SafeErrorMessage(new InvalidOperationException("boom")));

    [TestMethod]
    public void 重试通知转成_api_retry_帧()
    {
        var frame = ApiRetryScope.CreateFrame(
            new AgentRetryNotice(AgentApiErrorCategory.NetworkError, 2, 4, TimeSpan.FromSeconds(2)));

        Assert.AreEqual(AgentSseEventNames.ApiRetry, frame.Name);

        using var doc = JsonDocument.Parse(frame.Data);
        var root = doc.RootElement;
        Assert.AreEqual("NetworkError", root.GetProperty("category").GetString());
        Assert.AreEqual("网络错误", root.GetProperty("label").GetString());
        Assert.AreEqual(2, root.GetProperty("attempt").GetInt32());
        Assert.AreEqual(4, root.GetProperty("maxAttempts").GetInt32());
        Assert.AreEqual(2000, root.GetProperty("delayMs").GetInt32());
    }

    [TestMethod]
    public void 重试通知只投递给当前作用域()
    {
        var received = new List<AgentRetryNotice>();
        var notice = new AgentRetryNotice(AgentApiErrorCategory.NetworkError, 1, 4, TimeSpan.FromSeconds(1));

        // 未绑定作用域（如非对话路径的调用）静默丢弃，不抛异常
        ApiRetryScope.Publish(notice);
        Assert.AreEqual(0, received.Count);

        using (ApiRetryScope.Begin(received.Add))
        {
            ApiRetryScope.Publish(notice);
        }

        Assert.AreEqual(1, received.Count, "作用域内应收到一次提示");

        ApiRetryScope.Publish(notice);
        Assert.AreEqual(1, received.Count, "作用域释放后不应再收到提示");
    }
}