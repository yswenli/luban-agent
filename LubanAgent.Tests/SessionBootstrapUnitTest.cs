/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： SessionBootstrapUnitTest
*描述：宿主侧会话引导——切换工作区不再隐式建会话，由宿主按需补建：
*      已有活动会话时原样复用，没有再为指定工作区新建一条。
*
*****************************************************************************/

using LuBan.AIAgent.Sessions;
using LubanAgentCore.Services;

namespace LubanAgent.Tests;

[TestClass]
public class SessionBootstrapUnitTest
{
    [TestMethod]
    public async Task EnsureAsync_无活动会话_为指定工作区新建()
    {
        var sessions = new FakeSessionManager();

        var current = await SessionBootstrap.EnsureAsync(sessions, "ws-1");

        Assert.AreEqual("new-session", current.SessionId);
        CollectionAssert.AreEqual(new[] { "create:新会话|ws-1" }, sessions.Calls);
    }

    [TestMethod]
    public async Task EnsureAsync_已有活动会话_复用不新建()
    {
        var sessions = new FakeSessionManager();
        await sessions.SetCurrentSessionAsync("s-existing");

        var current = await SessionBootstrap.EnsureAsync(sessions, "ws-1");

        Assert.AreEqual("s-existing", current.SessionId);
        CollectionAssert.AreEqual(new[] { "switch:s-existing" }, sessions.Calls);
    }

    [TestMethod]
    public async Task EnsureAsync_显式标题_用于新建会话()
    {
        var sessions = new FakeSessionManager();

        await SessionBootstrap.EnsureAsync(sessions, "ws-2", "知识库对话");

        CollectionAssert.AreEqual(new[] { "create:知识库对话|ws-2" }, sessions.Calls);
    }

    [TestMethod]
    public async Task EnsureAsync_工作区标识为空_抛异常()
    {
        var sessions = new FakeSessionManager();

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => SessionBootstrap.EnsureAsync(sessions, "  "));
    }

    private sealed class FakeSessionManager : ISessionManager
    {
        public List<string> Calls { get; } = new();

        public SessionInfo? CurrentSession { get; private set; }

        public Task<SessionInfo> CreateSessionAsync(string? title = null, string? workspaceId = null)
        {
            Calls.Add($"create:{title}|{workspaceId}");
            CurrentSession = new SessionInfo { SessionId = "new-session", Title = title };
            return Task.FromResult(CurrentSession);
        }

        public Task<SessionInfo?> GetSessionAsync(string sessionId)
            => Task.FromResult<SessionInfo?>(new SessionInfo { SessionId = sessionId });

        public Task<IEnumerable<SessionInfo>> GetUserSessionsAsync()
            => Task.FromResult<IEnumerable<SessionInfo>>(Array.Empty<SessionInfo>());

        public Task UpdateSessionTitleAsync(string sessionId, string title)
        {
            Calls.Add($"rename:{sessionId}|{title}");
            return Task.CompletedTask;
        }

        public Task DeleteSessionAsync(string sessionId)
        {
            Calls.Add("delete:" + sessionId);
            return Task.CompletedTask;
        }

        public Task<SessionMessage> AddMessageAsync(string sessionId, string role, string content, int? tokens = null,
            string? thinking = null, string? attachments = null, string? toolCalls = null, string? segments = null, int? durationMs = null)
            => Task.FromResult(new SessionMessage { SessionId = sessionId, Role = role, Content = content });

        public Task<IEnumerable<SessionMessage>> GetMessagesAsync(string sessionId, int? limit = null)
            => Task.FromResult<IEnumerable<SessionMessage>>(Array.Empty<SessionMessage>());

        public Task<IEnumerable<SessionMessage>> GetLatestMessagesAsync(string sessionId, int count)
            => Task.FromResult<IEnumerable<SessionMessage>>(Array.Empty<SessionMessage>());

        public Task ClearMessagesAsync(string sessionId)
        {
            Calls.Add("clear:" + sessionId);
            return Task.CompletedTask;
        }

        public Task<SessionStats> GetSessionStatsAsync(string sessionId) => Task.FromResult(new SessionStats());

        public Task SetCurrentSessionAsync(string sessionId)
        {
            Calls.Add("switch:" + sessionId);
            CurrentSession = new SessionInfo { SessionId = sessionId };
            return Task.CompletedTask;
        }

        public void ClearCurrentSession() => CurrentSession = null;

        public Task ClearAllSessionsAsync(string? workspaceId = null)
        {
            Calls.Add("clearall:" + workspaceId);
            return Task.CompletedTask;
        }

        public Task<GlobalSessionStats> GetGlobalStatsAsync(int? days = null)
            => Task.FromResult(new GlobalSessionStats());

        public Task<IEnumerable<SessionMessage>> GetActiveMessagesAsync(string sessionId)
            => Task.FromResult<IEnumerable<SessionMessage>>(Array.Empty<SessionMessage>());

        public Task MarkMessagesCompactedAsync(string sessionId, IEnumerable<long> messageIds)
            => Task.CompletedTask;
    }
}
