/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： AgentRunHubUnitTest
*创建时间：2026/11/11
*描述：轮次中心单测——锁死 seq 分配、快照累积、环形缓冲滑窗、慢订阅者摘除、
*      终态收尾、同会话抢占、按 runId 精确取消这几条判定。
*      这些判定靠手动冒烟看不出来：一旦 seq 或缓冲窗口错位，重连就会静默丢帧或重复正文。
*
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class AgentRunHubUnitTest
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>只当透传句柄用的宿主桩：hub 不调用其对话能力。</summary>
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

    private static AgentRunHub NewHub(
        int backlog = 16,
        int subscriber = 4,
        TimeSpan? preemptTimeout = null,
        TimeSpan? shutdownTimeout = null)
    {
        var root = new DiContainer()
            .AddSingleton<IAgentHost>(new FakeAgentHost())
            .Build(validateScopes: false, validateOnBuild: false);

        // 作用域由 hub 在轮次收尾时释放；测试里不另行断言释放动作，只断言行为
        return new AgentRunHub(
            _ => root.CreateScope(),
            new AgentRunHubOptions
            {
                BacklogCapacity = backlog,
                SubscriberCapacity = subscriber,
                PreemptTimeout = preemptTimeout ?? TimeSpan.FromSeconds(10),
                ShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(10)
            });
    }

    /// <summary>构造一帧与轮次语义无关的占位帧（快照累积时被忽略，只用于验序号与缓冲）。</summary>
    private static AgentSseFrame Frame(string tag) => new("test.frame", $"{{\"tag\":\"{tag}\"}}");

    /// <summary>取占位帧的标签。</summary>
    private static string Tag(AgentSseFrame frame)
        => JsonDocument.Parse(frame.Data).RootElement.GetProperty("tag").GetString()!;

    private static AgentSseFrame TextDelta(string delta)
        => AgentSseFrame.Create(AgentSseEventNames.TextDelta, new { delta, llmRound = 1 });

    private static AgentSseFrame ThinkDelta(string delta)
        => AgentSseFrame.Create(AgentSseEventNames.ThinkDelta, new { delta, llmRound = 1 });

    private static AgentRunSnapshot ReadSnapshot(AgentRunAttachResult result)
    {
        Assert.AreEqual(AgentSseEventNames.RunSnapshot, result.Pending[0].Name);
        return JsonSerializer.Deserialize<AgentRunSnapshot>(result.Pending[0].Data, JsonOpts)!;
    }

    /// <summary>挂住不返回的对话体，用于让轮次保持运行中。</summary>
    private static Task HangAsync(AgentRunHandle _, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct);

    [TestMethod]
    public async Task 续帧自带序号_可直接作为fromSeq()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            handle.Publish(Frame("t1"));
            handle.Publish(Frame("t2"));

            var replay = handle.Subscribe(1);
            Assert.IsNotNull(replay);
            Assert.AreEqual(1, replay.Pending.Count);
            Assert.AreEqual(2, replay.Pending[0].Seq);
            // id 行随帧下发：前端记录到的就是重连时要回传的 fromSeq
            StringAssert.StartsWith(replay.Pending[0].ToText(), "id: 2\n");

            // 快照帧也带同一序号，刷新后可直接用它续传
            var snapshot = handle.Subscribe(null);
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(2, snapshot.Pending[0].Seq);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 帧按序分配seq并进入缓冲()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            handle.Publish(Frame("t1"));
            handle.Publish(Frame("t2"));
            handle.Publish(Frame("t3"));

            var snapshotAttach = handle.Subscribe(null);
            Assert.IsNotNull(snapshotAttach);
            var snapshot = ReadSnapshot(snapshotAttach);
            Assert.AreEqual(3, snapshot.Seq);
            // 快照本身就是全量，其后不应再补发窗口内的旧帧
            Assert.AreEqual(1, snapshotAttach.Pending.Count);

            var replay = handle.Subscribe(0);
            Assert.IsNotNull(replay);
            CollectionAssert.AreEqual(
                new[] { "t1", "t2", "t3" },
                replay.Pending.Select(Tag).ToArray());
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 空帧不进序列()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            handle.Publish(null);

            var attach = handle.Subscribe(null);
            Assert.IsNotNull(attach);
            Assert.AreEqual(0, ReadSnapshot(attach).Seq);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 快照累积正文思考与工具()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            handle.Publish(AgentSseFrame.ForSession("s1", created: false, runId: handle.RunId));
            handle.Publish(TextDelta("ab"));
            handle.Publish(TextDelta("cd"));
            handle.Publish(ThinkDelta("想"));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolStarted, new
            {
                callId = "c1",
                name = "file_read",
                arguments = "{\"path\":\"a.txt\"}",
                llmRound = 1
            }));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolCompleted, new
            {
                callId = "c1",
                resultSummary = "ok",
                llmRound = 1
            }));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolStarted, new
            {
                callId = "c2",
                name = "shell",
                arguments = "{\"cmd\":\"dir\"}",
                llmRound = 1
            }));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolFailed, new
            {
                callId = "c2",
                error = "退出码 1",
                llmRound = 1
            }));

            var attach = handle.Subscribe(null);
            Assert.IsNotNull(attach);
            var snapshot = ReadSnapshot(attach);

            Assert.AreEqual("abcd", snapshot.Text);
            Assert.AreEqual("想", snapshot.Thinking);
            Assert.AreEqual(2, snapshot.Tools.Count);

            var done = snapshot.Tools.Single(x => x.CallId == "c1");
            Assert.AreEqual("file_read", done.Name);
            Assert.AreEqual("done", done.Status);
            Assert.AreEqual("ok", done.ResultSummary);
            Assert.IsNull(done.Error);
            Assert.AreEqual("{\"path\":\"a.txt\"}", done.Arguments);

            var failed = snapshot.Tools.Single(x => x.CallId == "c2");
            Assert.AreEqual("failed", failed.Status);
            Assert.AreEqual("退出码 1", failed.Error);
            Assert.IsNull(failed.ResultSummary);

            // 有序段落：正文 → 思考 → 工具 c1 → 工具 c2，顺序与发布一致
            Assert.AreEqual(4, snapshot.Segments.Count);
            CollectionAssert.AreEqual(
                new[] { "text", "thinking", "tool", "tool" },
                snapshot.Segments.Select(x => x.Kind).ToArray());
            Assert.AreEqual("abcd", snapshot.Segments[0].Text);
            Assert.AreEqual("想", snapshot.Segments[1].Text);
            Assert.AreEqual("c1", snapshot.Segments[2].Tool?.CallId);
            Assert.AreEqual("done", snapshot.Segments[2].Tool?.Status);
            Assert.AreEqual("c2", snapshot.Segments[3].Tool?.CallId);
            Assert.AreEqual("failed", snapshot.Segments[3].Tool?.Status);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 快照按帧时间戳记录思考段与工具耗时()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            const long t0 = 1_750_000_000_000L;
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ThinkDelta, new { delta = "想", llmRound = 1, at = t0 }));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ThinkDelta, new { delta = "再想", llmRound = 1, at = t0 + 4_000 }));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolStarted, new
            {
                callId = "c1",
                name = "shell",
                arguments = "{}",
                llmRound = 1,
                at = t0 + 10_000
            }));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolCompleted, new
            {
                callId = "c1",
                resultSummary = "ok",
                llmRound = 1,
                at = t0 + 12_500
            }));

            var attach = handle.Subscribe(null);
            Assert.IsNotNull(attach);
            var snapshot = ReadSnapshot(attach);

            // 思考段耗时 = 首帧到末帧；工具耗时 = started → completed
            Assert.AreEqual(2, snapshot.Segments.Count);
            Assert.AreEqual(4_000, snapshot.Segments[0].DurationMs);
            Assert.AreEqual(t0, snapshot.Segments[0].StartedAtMs);
            Assert.AreEqual(2_500, snapshot.Segments[1].Tool?.DurationMs);
            Assert.AreEqual(t0 + 10_000, snapshot.Segments[1].Tool?.StartedAtMs);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 快照保留未落定的思考与工具起点()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            const long t0 = 1_750_000_000_000L;

            // 思考还在进行：重连后前端要能接着算，所以起点必须随快照下发
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ThinkDelta, new { delta = "想", llmRound = 1, at = t0 }));
            var runningSnapshot = ReadSnapshot(handle.Subscribe(null)!);
            Assert.IsNull(runningSnapshot.Segments[0].DurationMs);
            Assert.AreEqual(t0, runningSnapshot.Segments[0].StartedAtMs);

            // 工具开始、尚未结束：思考区间到此落定（口径为首帧到末帧），工具只给起点不给耗时
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ThinkDelta, new { delta = "再想", llmRound = 1, at = t0 + 2_000 }));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolStarted, new
            {
                callId = "c1",
                name = "shell",
                arguments = "{}",
                llmRound = 1,
                at = t0 + 5_000
            }));
            var toolRunning = ReadSnapshot(handle.Subscribe(null)!);
            Assert.AreEqual(2_000, toolRunning.Segments[0].DurationMs);
            Assert.IsNull(toolRunning.Segments[1].Tool?.DurationMs);
            Assert.AreEqual(t0 + 5_000, toolRunning.Segments[1].Tool?.StartedAtMs);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 快照累积notice与确认()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolConfirm, new
            {
                confirmationId = "cf1",
                toolName = "shell",
                arguments = "{\"cmd\":\"del x\"}",
                timeoutSeconds = 60
            }));

            var confirmAttach = handle.Subscribe(null);
            Assert.IsNotNull(confirmAttach);
            var pending = ReadSnapshot(confirmAttach).PendingConfirmation;
            Assert.IsNotNull(pending);
            Assert.AreEqual("cf1", pending.ConfirmationId);
            Assert.AreEqual("shell", pending.ToolName);
            Assert.AreEqual(60, pending.TimeoutSeconds);
            // 过期时刻由服务端算好下发，前端按剩余时间倒数而不是从满值重来
            Assert.IsTrue(pending.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(50));

            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolDenied, new
            {
                toolName = "shell",
                arguments = "{\"cmd\":\"del x\"}"
            }));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.AttachmentAdded, new
            {
                fileName = "a.txt",
                mediaType = "text/plain",
                fileSize = 3L,
                source = "ui"
            }));

            var attach = handle.Subscribe(null);
            Assert.IsNotNull(attach);
            var snapshot = ReadSnapshot(attach);
            Assert.AreEqual(2, snapshot.Notices.Count);
            CollectionAssert.AreEqual(
                new[] { AgentSseEventNames.ToolDenied, AgentSseEventNames.AttachmentAdded },
                snapshot.Notices.Select(x => x.Event).ToArray());
            Assert.AreEqual(1, snapshot.AttachmentsAdded);
            Assert.IsNull(snapshot.PendingConfirmation);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 快照累积待回答提问()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolQuestion, new
            {
                questionId = "q-1",
                timeoutSeconds = 120,
                questions = new object[]
                {
                    new
                    {
                        question = "选哪个数据库？",
                        header = "数据库",
                        multiSelect = false,
                        options = new object[]
                        {
                            new { label = "PostgreSQL", description = "关系型" },
                            new { label = "SQLite" }
                        }
                    }
                }
            }));

            var attach = handle.Subscribe(null);
            Assert.IsNotNull(attach);
            var pending = ReadSnapshot(attach).PendingQuestion;
            Assert.IsNotNull(pending);
            Assert.AreEqual("q-1", pending.QuestionId);
            Assert.AreEqual(120, pending.TimeoutSeconds);
            StringAssert.Contains(pending.QuestionsJson, "选哪个数据库？");
            Assert.IsNull(pending.Origin, "主代理提问不带来源");
            // 过期时刻由服务端算好下发，前端按剩余时间倒数
            Assert.IsTrue(pending.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(100));

            // 工具被拒/取消：待回答提问随时间线作废，重连后不应再弹
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolDenied, new
            {
                toolName = "AskUserQuestion",
                arguments = "{}"
            }));

            var after = handle.Subscribe(null);
            Assert.IsNotNull(after);
            Assert.IsNull(ReadSnapshot(after).PendingQuestion);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 提问工具完成后快照不再带回旧提问()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolStarted, new
            {
                callId = "call-q",
                name = "AskUserQuestion",
                arguments = "{}"
            }));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolQuestion, new
            {
                questionId = "q-1",
                timeoutSeconds = 120,
                questions = new object[]
                {
                    new
                    {
                        question = "选哪个？",
                        options = new object[] { new { label = "A" }, new { label = "B" } }
                    }
                }
            }));

            var pendingAttach = handle.Subscribe(null);
            Assert.IsNotNull(pendingAttach);
            Assert.IsNotNull(ReadSnapshot(pendingAttach).PendingQuestion);

            // 用户已作答：提问工具随即完成，刷新后不应再弹已答过的旧问题
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolCompleted, new
            {
                callId = "call-q",
                resultSummary = "answered"
            }));

            var after = handle.Subscribe(null);
            Assert.IsNotNull(after);
            Assert.IsNull(ReadSnapshot(after).PendingQuestion);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 其它工具完成不清除待回答提问()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolStarted, new
            {
                callId = "call-q",
                name = "AskUserQuestion",
                arguments = "{}"
            }));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolQuestion, new
            {
                questionId = "q-1",
                timeoutSeconds = 120,
                questions = new object[]
                {
                    new
                    {
                        question = "选哪个？",
                        options = new object[] { new { label = "A" }, new { label = "B" } }
                    }
                }
            }));

            // 并行编排里另一个节点的工具收尾：不能误当作提问已结束
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolStarted, new
            {
                callId = "call-other",
                name = "RunShellAsync",
                arguments = "{}"
            }));
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolCompleted, new
            {
                callId = "call-other",
                resultSummary = "ok"
            }));

            var after = handle.Subscribe(null);
            Assert.IsNotNull(after);
            Assert.IsNotNull(ReadSnapshot(after).PendingQuestion, "提问仍在等用户作答，其它工具收尾不应清掉它");
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 子代理提问作答后清空快照待答提问()
    {
        // 编排子代理发起的提问不在父轮次工具卡里，tool.completed 永远匹配不到 callId；
        // 作答收尾必须由宿主显式清理，否则同一轮内刷新会把已答过的问题弹回来
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolQuestion, new
            {
                questionId = "q-1",
                timeoutSeconds = 120,
                questions = new object[]
                {
                    new
                    {
                        question = "选哪个？",
                        options = new object[] { new { label = "A" }, new { label = "B" } }
                    }
                }
            }));

            var before = handle.Subscribe(null);
            Assert.IsNotNull(before);
            Assert.IsNotNull(ReadSnapshot(before).PendingQuestion);

            // 标识不匹配：不能误清别的提问
            Assert.IsFalse(handle.ClearPendingQuestion("q-other"));
            var kept = handle.Subscribe(null);
            Assert.IsNotNull(kept);
            Assert.IsNotNull(ReadSnapshot(kept).PendingQuestion);

            Assert.IsTrue(handle.ClearPendingQuestion("q-1"));
            var after = handle.Subscribe(null);
            Assert.IsNotNull(after);
            Assert.IsNull(ReadSnapshot(after).PendingQuestion);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 过期提问不再进入快照()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            // 截止时刻已过：即使没人作答，重连也不该再弹出来陪跑到下一轮
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ToolQuestion, new
            {
                questionId = "q-1",
                timeoutSeconds = 0,
                questions = new object[]
                {
                    new
                    {
                        question = "选哪个？",
                        options = new object[] { new { label = "A" }, new { label = "B" } }
                    }
                }
            }));

            var attach = handle.Subscribe(null);
            Assert.IsNotNull(attach);
            Assert.IsNull(ReadSnapshot(attach).PendingQuestion);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 压缩提示进入快照通知()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            handle.Publish(AgentSseFrame.Create(AgentSseEventNames.ContextCompacted, new
            {
                phase = ContextCompactionPhases.Done,
                trigger = ContextCompactTriggers.Preflight,
                archivedCount = 3,
                keptCount = 2,
                estimatedTokens = 1200,
                budget = 8000,
                message = "📦 上下文已压缩"
            }));

            // 重连/刷新后压缩标记必须由快照回放，不能只活在实时流里
            var attach = handle.Subscribe(null);
            Assert.IsNotNull(attach);
            var snapshot = ReadSnapshot(attach);
            Assert.AreEqual(1, snapshot.Notices.Count);
            Assert.AreEqual(AgentSseEventNames.ContextCompacted, snapshot.Notices[0].Event);
            StringAssert.Contains(snapshot.Notices[0].Data, "上下文已压缩");
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 环形缓冲滑窗()
    {
        var hub = NewHub(backlog: 16);
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            for (var i = 1; i <= 20; i++)
            {
                handle.Publish(Frame($"t{i}"));
            }

            // 客户端只收到第 0 帧：缺口（1..4）已滑出窗口，只能退回快照
            var attach = handle.Subscribe(0);
            Assert.IsNotNull(attach);
            Assert.AreEqual(AgentSseEventNames.RunSnapshot, attach.Pending[0].Name);
            Assert.AreEqual(20, ReadSnapshot(attach).Seq);

            // 首连接（snapshotOnGap: false）不退回快照：它本来就从头收
            var first = handle.Subscribe(0, snapshotOnGap: false);
            Assert.IsNotNull(first);
            Assert.AreEqual(16, first.Pending.Count);
            Assert.IsFalse(first.Pending.Any(x => x.Name == AgentSseEventNames.RunSnapshot));
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 慢订阅者被摘除且通道完成()
    {
        var hub = NewHub(subscriber: 1);
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);
            var attach = handle.Subscribe(null);
            Assert.IsNotNull(attach);

            handle.Publish(Frame("t1"));
            // 队列只有 1 格且已满：第 2 帧写不进，订阅者应被摘除而不是把发布线程卡住
            handle.Publish(Frame("t2"));
            handle.Publish(Frame("t3"));

            var first = await attach.Subscription.Reader.ReadAsync();
            Assert.AreEqual("t1", Tag(first));
            Assert.IsFalse(await attach.Subscription.Reader.WaitToReadAsync());

            // 摘除后轮次仍能继续发布，新订阅者照常接入
            handle.Publish(Frame("t4"));
            var rejoin = handle.Subscribe(0);
            Assert.IsNotNull(rejoin);
            Assert.AreEqual(4, rejoin.Pending.Count);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 终态完成所有订阅者通道并移除登记()
    {
        var hub = NewHub();
        try
        {
            var gate = new TaskCompletionSource();
            var handle = await hub.StartAsync("u", "s1", async (_, ct) => await gate.Task.WaitAsync(ct));

            var s1 = handle.Subscribe(null);
            var s2 = handle.Subscribe(0);
            Assert.IsNotNull(s1);
            Assert.IsNotNull(s2);

            gate.SetResult();
            await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(AgentRunStage.Done, handle.Stage);
            Assert.IsNull(hub.Find("u", "s1"));
            Assert.IsFalse(await s1.Subscription.Reader.WaitToReadAsync());
            Assert.IsFalse(await s2.Subscription.Reader.WaitToReadAsync());
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 同会话新轮次抢占旧轮次()
    {
        var hub = NewHub();
        try
        {
            var first = await hub.StartAsync("u", "s1", HangAsync);
            var second = await hub.StartAsync("u", "s1", HangAsync);

            Assert.AreEqual(AgentRunStage.Cancelled, first.Stage);
            Assert.AreEqual(AgentRunStage.Running, second.Stage);
            Assert.AreSame(second, hub.Find("u", "s1"));
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 取消只命中指定runId()
    {
        var hub = NewHub();
        try
        {
            var handle = await hub.StartAsync("u", "s1", HangAsync);

            // 迟到取消（回合已换代）不得命中新回合
            Assert.IsFalse(hub.Cancel("u", "s1", "过期runId"));
            Assert.AreEqual(AgentRunStage.Running, handle.Stage);

            Assert.IsTrue(hub.Cancel("u", "s1", handle.RunId));
            await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(AgentRunStage.Cancelled, handle.Stage);
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 工作区判定()
    {
        var hub = NewHub();
        try
        {
            await hub.StartAsync("u", "s1", HangAsync);
            await hub.StartAsync("u", "s2", HangAsync);

            Assert.IsTrue(hub.IsAnyRunning("u", new[] { "s1", "s2" }));

            Assert.IsTrue(await hub.CancelAndWaitAsync("u", "s1"));
            Assert.IsTrue(await hub.CancelAndWaitAsync("u", "s2"));

            Assert.IsFalse(hub.IsAnyRunning("u", new[] { "s1", "s2" }));
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 无轮次时取消返回false()
    {
        var hub = NewHub();
        try
        {
            // 调用方据「返回 false + 再查 IsRunning」区分「本来没跑」与「取消超时」
            Assert.IsFalse(await hub.CancelAndWaitAsync("u", "不存在的会话"));
            Assert.IsFalse(hub.Cancel("u", "不存在的会话"));
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task 取消等待超时返回false且轮次仍在跑()
    {
        // 对话体故意不响应取消令牌，模拟「收尾迟迟不结束」：调用方必须拿到 false 而不是被无限挂住
        var hub = NewHub(
            preemptTimeout: TimeSpan.FromMilliseconds(150),
            shutdownTimeout: TimeSpan.FromMilliseconds(150));
        try
        {
            await hub.StartAsync("u", "s1", (_, _) => Task.Delay(Timeout.Infinite));

            Assert.IsFalse(await hub.CancelAndWaitAsync("u", "s1"));
            Assert.IsTrue(hub.IsRunning("u", "s1"));
        }
        finally
        {
            await hub.DisposeAsync();
        }
    }
}