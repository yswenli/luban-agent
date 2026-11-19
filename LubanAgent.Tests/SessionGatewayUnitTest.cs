/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Tests
*文件名： SessionGatewayUnitTest
*描述：会话网关 SSE 帧解析与预批策略纯函数单测（设计 §4.3）——
*      帧名/字段名钉自 R-S2-1 最小回路实测（2026-11-18 宿主 18081）：
*      tool.confirm={confirmationId,toolName,arguments:{command},timeoutSeconds}、
*      tool.denied={toolName,arguments}、tool.failed={callId,error}、done={sessionId}。
*      只打 SessionFrameParser 纯函数（mock 帧序列），不打真实宿主、不写真实 LocalApplicationData。
*
*****************************************************************************/

using LubanAgentWebHost.Services.Init;

namespace LubanAgent.Tests;

[TestClass]
public class SessionGatewayUnitTest
{
    /// <summary>预批命令（合成名夹具，形态同 GitCommandPlanner 产出）。</summary>
    private static readonly IReadOnlyList<string> ApprovedCommands = new[]
    {
        "git clone --branch master https://codeup.example.com/org-demo/tplx-mixed.git /tmp/ws/NEWX-Mixed",
        "git -C \"/tmp/ws/NEWX-Mixed\" push origin master",
    };

    private static AgentSseFrame Frame(string name, string json) => new(name, json);

    /// <summary>tool.confirm 帧（实测载荷形态：RunShellAsync + arguments.command）。</summary>
    private static AgentSseFrame ToolConfirm(string confirmationId, string toolName, string? command)
        => Frame(
            AgentSseEventNames.ToolConfirm,
            command is null
                ? $$"""{"confirmationId":"{{confirmationId}}","toolName":"{{toolName}}","arguments":{},"timeoutSeconds":60}"""
                : $$"""{"confirmationId":"{{confirmationId}}","toolName":"{{toolName}}","arguments":{"command":"{{command}}","workingDirectory":"/tmp/ws"},"timeoutSeconds":60}""");

    // ── tool.confirm 帧解析 ─────────────────────────────────────────────────

    [TestMethod]
    public void TryReadToolConfirm_实测帧形态_解析出确认字段()
    {
        var frame = ToolConfirm("cf-001", "RunShellAsync", "git status");

        var ok = SessionFrameParser.TryReadToolConfirm(frame, out var call);

        Assert.IsTrue(ok);
        Assert.AreEqual("cf-001", call.ConfirmationId);
        Assert.AreEqual("RunShellAsync", call.ToolName);
        Assert.AreEqual("git status", call.Command);
    }

    [TestMethod]
    public void TryReadToolConfirm_非确认帧_不解析()
    {
        var frame = Frame(AgentSseEventNames.ToolStarted,
            """{"callId":"call_1","name":"RunShell","arguments":{"command":"git status"},"llmRound":1}""");

        Assert.IsFalse(SessionFrameParser.TryReadToolConfirm(frame, out _));
    }

    [TestMethod]
    public void TryReadToolConfirm_缺confirmationId_不解析()
    {
        var frame = Frame(AgentSseEventNames.ToolConfirm,
            """{"toolName":"RunShellAsync","arguments":{"command":"git status"},"timeoutSeconds":60}""");

        Assert.IsFalse(SessionFrameParser.TryReadToolConfirm(frame, out _));
    }

    [TestMethod]
    public void TryReadToolConfirm_载荷非法JSON_不解析()
    {
        Assert.IsFalse(SessionFrameParser.TryReadToolConfirm(
            Frame(AgentSseEventNames.ToolConfirm, "{not-json"), out _));
    }

    // ── 预批放行 / deny 分支 ────────────────────────────────────────────────

    [TestMethod]
    public void DecideConfirmResult_命中预批清单_放行()
    {
        var call = new ToolConfirmCall(
            "cf-1", "RunShellAsync",
            "git clone --branch master https://codeup.example.com/org-demo/tplx-mixed.git /tmp/ws/NEWX-Mixed");

        Assert.AreEqual(
            SessionFrameParser.ConfirmAllow,
            SessionFrameParser.DecideConfirmResult(ApprovedCommands, call));
    }

    [TestMethod]
    public void DecideConfirmResult_偏离清单_拒绝()
    {
        var call = new ToolConfirmCall("cf-2", "RunShellAsync", "rm -rf /tmp/ws");

        Assert.AreEqual(
            SessionFrameParser.ConfirmDeny,
            SessionFrameParser.DecideConfirmResult(ApprovedCommands, call));
    }

    [TestMethod]
    public void DecideConfirmResult_清单为空_一律拒绝()
    {
        var call = new ToolConfirmCall(
            "cf-3", "RunShellAsync",
            "git clone --branch master https://codeup.example.com/org-demo/tplx-mixed.git /tmp/ws/NEWX-Mixed");

        Assert.AreEqual(SessionFrameParser.ConfirmDeny, SessionFrameParser.DecideConfirmResult(null, call));
        Assert.AreEqual(SessionFrameParser.ConfirmDeny, SessionFrameParser.DecideConfirmResult(Array.Empty<string>(), call));
    }

    [TestMethod]
    public void DecideConfirmResult_子串前缀不算命中_防绕过()
    {
        // 预批命令是恶意命令前缀时不得放行（只认精确匹配）
        var call = new ToolConfirmCall(
            "cf-4", "RunShellAsync",
            "git -C \"/tmp/ws/NEWX-Mixed\" push origin master && rm -rf /");

        Assert.AreEqual(
            SessionFrameParser.ConfirmDeny,
            SessionFrameParser.DecideConfirmResult(ApprovedCommands, call));
    }

    [TestMethod]
    public void DecideConfirmResult_无命令文本_拒绝()
    {
        var call = new ToolConfirmCall("cf-5", "RunShellAsync", null);

        Assert.AreEqual(SessionFrameParser.ConfirmDeny, SessionFrameParser.DecideConfirmResult(ApprovedCommands, call));
    }

    // ── 帧序列折叠 → SessionReceipt ─────────────────────────────────────────

    [TestMethod]
    public void Fold_纯文本收尾_成功回执()
    {
        var frames = new[]
        {
            Frame(AgentSseEventNames.Session,
                """{"sessionId":"sess-1","created":true,"runId":"run-1"}"""),
            Frame(AgentSseEventNames.TextDelta, """{"delta":"pong","llmRound":1}"""),
            Frame(AgentSseEventNames.Done, """{"sessionId":"sess-1"}"""),
        };

        var receipt = SessionFrameParser.Fold(frames);

        Assert.IsTrue(receipt.Ok);
        Assert.AreEqual("pong", receipt.Summary);
        Assert.IsNull(receipt.Error);
        Assert.AreEqual("sess-1", receipt.SessionId);
        Assert.AreEqual("run-1", receipt.RunId);
        Assert.IsFalse(receipt.Denied);
    }

    [TestMethod]
    public void Fold_error帧_失败回执()
    {
        var frames = new[]
        {
            Frame(AgentSseEventNames.Session, """{"sessionId":"sess-2","created":true,"runId":"run-2"}"""),
            Frame(AgentSseEventNames.Error, """{"message":"上游模型不可用"}"""),
            Frame(AgentSseEventNames.Done, """{"sessionId":"sess-2"}"""),
        };

        var receipt = SessionFrameParser.Fold(frames);

        Assert.IsFalse(receipt.Ok);
        Assert.AreEqual("上游模型不可用", receipt.Error);
        Assert.AreEqual("sess-2", receipt.SessionId);
    }

    [TestMethod]
    public void Fold_预批拒绝序列_失败且Denied()
    {
        // 实测 deny 后帧序：tool.denied → tool.failed（"工具调用被拒绝或已取消"）→ done
        var frames = new[]
        {
            Frame(AgentSseEventNames.Session, """{"sessionId":"sess-3","created":false,"runId":"run-3"}"""),
            ToolConfirm("cf-9", "RunShellAsync", "rm -rf /tmp/ws"),
            Frame(AgentSseEventNames.ToolDenied,
                """{"toolName":"RunShellAsync","arguments":{"command":"rm -rf /tmp/ws"}}"""),
            Frame(AgentSseEventNames.ToolFailed,
                """{"callId":"call_1","error":"工具调用被拒绝或已取消","llmRound":1}"""),
            Frame(AgentSseEventNames.Done, """{"sessionId":"sess-3"}"""),
        };

        var receipt = SessionFrameParser.Fold(frames);

        Assert.IsFalse(receipt.Ok);
        Assert.IsTrue(receipt.Denied);
        Assert.IsNotNull(receipt.Error);
        StringAssert.Contains(receipt.Error, "工具调用被拒绝或已取消");
        StringAssert.Contains(receipt.Error, "被预批策略拒绝");
    }

    [TestMethod]
    public void Fold_无done终态帧_未收尾失败()
    {
        var frames = new[]
        {
            Frame(AgentSseEventNames.Session, """{"sessionId":"sess-4","created":true,"runId":"run-4"}"""),
            Frame(AgentSseEventNames.TextDelta, """{"delta":"半截","llmRound":1}"""),
        };

        var receipt = SessionFrameParser.Fold(frames);

        Assert.IsFalse(receipt.Ok);
        Assert.AreEqual("半截", receipt.Summary);
        StringAssert.Contains(receipt.Error, "done");
    }

    [TestMethod]
    public void Fold_toolFailed_计为失败()
    {
        var frames = new[]
        {
            Frame(AgentSseEventNames.Session, """{"sessionId":"sess-5","created":true,"runId":"run-5"}"""),
            Frame(AgentSseEventNames.ToolFailed,
                """{"callId":"call_2","error":"git exitCode=128","llmRound":1}"""),
            Frame(AgentSseEventNames.Done, """{"sessionId":"sess-5"}"""),
        };

        var receipt = SessionFrameParser.Fold(frames);

        Assert.IsFalse(receipt.Ok);
        Assert.IsFalse(receipt.Denied);
        StringAssert.Contains(receipt.Error, "git exitCode=128");
    }

    [TestMethod]
    public void Fold_空序列_未收尾失败()
    {
        var receipt = SessionFrameParser.Fold(Array.Empty<AgentSseFrame>());

        Assert.IsFalse(receipt.Ok);
        Assert.IsNull(receipt.Summary);
        Assert.IsNotNull(receipt.Error);
    }
}
