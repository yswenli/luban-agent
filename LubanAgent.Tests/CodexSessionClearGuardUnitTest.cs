/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： CodexSessionClearGuardUnitTest
*创建时间：2026/11/11
*描述：Codex 会话清空守卫单测——运行中清空会让正在增长的助手消息变成孤儿（界面看不到、内容丢失），
*      必须拒绝。
*
*****************************************************************************/

using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Hosting;
using LuBan.DI;

using LubanAgentCodex.ViewModels;
using LubanAgentCodex.ViewModels.Messages;

namespace LubanAgent.Tests;

[TestClass]
public class CodexSessionClearGuardUnitTest
{
    /// <summary>只用于构造会话 ViewModel 的宿主桩：清空不触达对话能力。</summary>
    private sealed class FakeAgentHost : IAgentHost
    {
        public bool IsInitialized => false;

        public DiContainer Services => null!;

        public string? CurrentSessionId => null;

        public Task InitializeAsync(string sessionId, CancellationToken ct = default, AgentBuildOverrides? overrides = null)
            => Task.CompletedTask;

        public void Reset(string? sessionId = null)
        {
        }

        public IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(AgentRunRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static SessionViewModel NewSession()
        => new(
            "session-clear-guard",
            new FakeAgentHost(),
            null,
            () => ToolPermissionMode.Default,
            _ => false);

    [TestMethod]
    public void 运行中清空被拒绝且消息保持原样()
    {
        var session = NewSession();
        session.Messages.Add(new UserMessageItem { Content = "hello" });
        session.IsRunning = true;

        Assert.IsFalse(session.TryClear());
        Assert.AreEqual(1, session.Messages.Count);
    }

    [TestMethod]
    public void 空闲时清空成功()
    {
        var session = NewSession();
        session.Messages.Add(new UserMessageItem { Content = "hello" });

        Assert.IsTrue(session.TryClear());
        Assert.AreEqual(0, session.Messages.Count);
    }
}