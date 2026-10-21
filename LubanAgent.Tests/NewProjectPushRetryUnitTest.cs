/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectPushRetryUnitTest
*创建时间：2026/10/21
*描述：Step 6 推送限流退避策略单测（v0.29）——锁死真机缺陷：
*      云效服务端限流（remote: 请求过于频繁，请稍后重试!）被能力层当成「PAT 权限 / 分支保护」
*      硬编码文案，且一次失败即整步失败（同一步里后端隔 11s 重推成功、管理端 3s 后又被限流即挂）。
*      修法＝判定下沉到 GitCommandResult.IsRateLimited + 10s/20s/30s 退避重试（最多 3 次）+ 文案分流。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Services;
using LubanAgentCore.Connectors.AliyunDevOps;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectPushRetryUnitTest
{
    /// <summary>构造一个「成功」的 git 结果。</summary>
    private static GitCommandResult Ok(string output = "") => GitCommandResult.Ok(output);

    /// <summary>构造一个「失败」的 git 结果（首行摘要即 <paramref name="stderr"/> 的首行）。</summary>
    private static GitCommandResult Fail(string stderr) => new(false, 1, string.Empty, stderr, false);

    [TestMethod]
    public void IsRateLimited_CodeupRateLimitText_IsTrue()
    {
        // 真机原文（云效服务端拒绝 push 时写在 stderr）
        var result = Fail("remote: 请求过于频繁，请稍后重试!\nfatal: unable to access 'https://oauth2:***@codeup.aliyun.com/tjc/CSXMY/Csxmy-Admin.git/': "
                          + "The requested URL returned error: 429");

        Assert.IsTrue(result.IsRateLimited, "云效限流文案（含 429）必须判定为限流。");
        Assert.AreEqual("remote: 请求过于频繁，请稍后重试!", result.FailureSummary(), "失败摘要仍取 stderr 首行。");
    }

    [TestMethod]
    public void IsRateLimited_MarkerInStandardOutput_IsTrue()
    {
        // 不同 git / 代理层可能把 429 落在 stdout
        var result = new GitCommandResult(false, 128, "fatal: unable to access 'https://…': The requested URL returned error: 429", string.Empty, false);

        Assert.IsTrue(result.IsRateLimited, "429 落在 stdout 时同样要判限流。");
    }

    [TestMethod]
    public void IsRateLimited_NonRateLimitFailures_AreFalse()
    {
        var cases = new[]
        {
            "! [rejected]        develop -> develop (non-fast-forward)",           // 非快进：重试无用
            "remote: Invalid username or password (password authentication failed)", // 鉴权失败：重试无用
            "remote: Forbidden: you do not have permission to push to protected branch develop",
            "fatal: unable to access 'https://…': Could not resolve host: codeup.aliyun.com"
        };

        foreach (var stderr in cases)
        {
            Assert.IsFalse(Fail(stderr).IsRateLimited, $"「{stderr}」不应被判为限流。");
        }
    }

    [TestMethod]
    public void IsRateLimited_SuccessOrEmpty_IsFalse()
    {
        Assert.IsFalse(Ok("To https://codeup.aliyun.com/…\n   b80adaf..be71979  develop -> develop").IsRateLimited,
            "成功结果不可能是限流。");
        Assert.IsFalse(Fail(string.Empty).IsRateLimited, "空报错不臆测限流。");
    }

    [TestMethod]
    public void ShouldRetry_OnlyRateLimitedFailures()
    {
        Assert.IsTrue(PushRetryPolicy.ShouldRetry(Fail("remote: 请求过于频繁，请稍后重试!")));
        Assert.IsFalse(PushRetryPolicy.ShouldRetry(Ok()), "成功不重试。");
        Assert.IsFalse(PushRetryPolicy.ShouldRetry(Fail("! [rejected] develop -> develop (non-fast-forward)")),
            "非限流失败不得自动重试（重试解决不了，只会拖长等待并掩盖真因）。");
    }

    [TestMethod]
    public void Backoff_Is10_20_30_WithThreeRetries()
    {
        CollectionAssert.AreEqual(new[] { 10, 20, 30 }, PushRetryPolicy.BackoffSeconds.ToArray(),
            "退避序列应为 10s → 20s → 30s（真机读数：11s 重推已成功、3s 仍被限流）。");
        Assert.AreEqual(3, PushRetryPolicy.MaxRetries, "最多 3 次重试（首攻 + 3 = 单端最多 4 次尝试）。");
        Assert.AreEqual(TimeSpan.FromSeconds(10), PushRetryPolicy.DelayBefore(1));
        Assert.AreEqual(TimeSpan.FromSeconds(20), PushRetryPolicy.DelayBefore(2));
        Assert.AreEqual(TimeSpan.FromSeconds(30), PushRetryPolicy.DelayBefore(3));
    }

    [TestMethod]
    public void DelayBefore_OutOfRange_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PushRetryPolicy.DelayBefore(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PushRetryPolicy.DelayBefore(PushRetryPolicy.MaxRetries + 1));
    }

    [TestMethod]
    public void WaitingDetail_TellsWhatAndHowLong()
    {
        var detail = PushRetryPolicy.WaitingDetail("csxmy-Admin", "develop", 2, "remote: 请求过于频繁，请稍后重试!");

        StringAssert.Contains(detail, "csxmy-Admin · develop", "等待文案要标明是哪个仓库的哪个分支。");
        StringAssert.Contains(detail, "请求过于频繁", "要带 git 原始报错，便于用户自行判断。");
        StringAssert.Contains(detail, "20s 后重试", "要给出这一等是多久（第 2 次重试 = 20s）。");
        StringAssert.Contains(detail, "第 2/3 次", "要给出重试进度。");
    }

    [TestMethod]
    public void DescribeFailure_RateLimited_DoesNotBlamePatOrBranchProtection()
    {
        var reason = PushRetryPolicy.DescribeFailure(
            "csxmy-Admin", "develop", Fail("remote: 请求过于频繁，请稍后重试!"), retryCount: 3);

        StringAssert.Contains(reason, "请求过于频繁");
        StringAssert.Contains(reason, "云效服务端限流", "限流要按限流说。");
        StringAssert.Contains(reason, "自动退避重试 3 次");
        StringAssert.Contains(reason, "重新推送");
        Assert.IsFalse(reason.Contains("PAT", StringComparison.Ordinal),
            "限流分支**不得**再把用户引向 PAT / 权限（真机缺陷：同一条 PAT、同一分支 11 秒后重推成功）。");
        Assert.IsFalse(reason.Contains("分支", StringComparison.Ordinal) && reason.Contains("保护", StringComparison.Ordinal),
            "限流分支不得提分支保护。");
    }

    [TestMethod]
    public void DescribeFailure_OtherFailures_KeepPermissionCheckHint()
    {
        var reason = PushRetryPolicy.DescribeFailure(
            "csxmy-Admin", "develop", Fail("! [rejected] develop -> develop (non-fast-forward)"), retryCount: 0);

        StringAssert.Contains(reason, "non-fast-forward");
        StringAssert.Contains(reason, "PAT", "非限流失败仍要给出权限 / 分支保护的排查方向。");
        StringAssert.Contains(reason, "若持续失败", "措辞不把偶发失败一口咬定为权限问题。");
        Assert.IsFalse(reason.Contains("限流", StringComparison.Ordinal), "非限流失败不该说限流。");
    }
}