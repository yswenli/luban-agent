/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： ContextCompactionUnitTest
*描述：上下文压缩守卫——估算口径、保留窗口、摘要输入上界、事件文案、
*      帧还原与「超长兜底重跑」的重跑条件。
*
*****************************************************************************/

using LuBan.AIAgent.Errors;

namespace LubanAgent.Tests;

[TestClass]
public class ContextCompactionUnitTest
{
    // ---------------- 估算口径 ----------------

    [TestMethod]
    public void 估算token_中日韩按字计其余按四字符计()
    {
        // 压缩判定全靠这个估算，口径一偏就会「该压不压」或「白压一次」
        Assert.AreEqual(4, ContextCompactor.EstimateTokens("一二三四"));
        Assert.AreEqual(2, ContextCompactor.EstimateTokens("abcdefgh"));
        Assert.AreEqual(3, ContextCompactor.EstimateTokens("一二abcd"));
        Assert.AreEqual(0, ContextCompactor.EstimateTokens(null));
    }

    [TestMethod]
    public void 估算消息token_只计正文与附件占位()
    {
        var message = new SessionMessage
        {
            Content = "一二三四",
            // 思考存库但不回放，不能计入上下文估算，否则会提前误压
            Thinking = "一二",
            Attachments = "[]"
        };

        Assert.AreEqual(4 + 32, ContextCompactor.EstimateMessageTokens(message));
    }

    // ---------------- 压缩行为 ----------------

    [TestMethod]
    public async Task 压缩_按预算保留尾部并先写摘要后归档()
    {
        var sessions = new RecordingSessionManager();
        for (var i = 0; i < 6; i++)
        {
            sessions.Active.Add(new SessionMessage
            {
                Id = i + 1,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = new string('一', 1000) // 约 1000 token
            });
        }

        var chat = new RecordingChatClient("历史摘要");
        var compactor = NewCompactor(sessions, chat);

        var result = await compactor.CompactAsync("s1", budget: 3000, incoming: "本轮问题");

        Assert.IsNotNull(result);
        Assert.AreEqual(5, result.ArchivedCount);
        Assert.AreEqual(1, result.KeptCount);
        Assert.IsTrue(result.TokensAfter < result.TokensBefore);

        // 顺序锁死：先写摘要、后归档。反序时归档成功而写摘要失败会永久丢历史
        CollectionAssert.AreEqual(new[] { "add:summary", "compact:5" }, sessions.Calls);
        Assert.AreEqual("历史摘要", sessions.Added[0].Content);

        // 归档范围是被移出上下文的那 5 条，不含新写入的摘要
        CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4, 5 }, sessions.CompactedIds.ToArray());
    }

    [TestMethod]
    public async Task 压缩_无消息时返回空()
    {
        var sessions = new RecordingSessionManager();
        var compactor = NewCompactor(sessions, new RecordingChatClient("摘要"));

        var result = await compactor.CompactAsync("s1", budget: 1000, incoming: "hi");

        Assert.IsNull(result);
        Assert.AreEqual(0, sessions.Calls.Count);
    }

    [TestMethod]
    public async Task 压缩_摘要输入不超过预算八成()
    {
        var sessions = new RecordingSessionManager();
        for (var i = 0; i < 20; i++)
        {
            sessions.Active.Add(new SessionMessage
            {
                Id = i + 1,
                Role = "user",
                Content = new string('一', 4000) // 单条即 4000 token，远超窗口
            });
        }

        var chat = new RecordingChatClient("摘要");
        var compactor = NewCompactor(sessions, chat);

        await compactor.CompactAsync("s1", budget: 4000, incoming: null);

        // 摘要调用本身也走同一个模型：喂进去的内容必须装得下，否则超长兜底永远救不回来
        var fed = chat.LastPrompt;
        Assert.IsNotNull(fed);
        Assert.IsTrue(
            ContextCompactor.EstimateTokens(string.Concat(fed)) <= 3200,
            $"摘要输入估算 {ContextCompactor.EstimateTokens(string.Concat(fed))} token，超过阈值八成");
        Assert.IsTrue(
            ContextCompactor.EstimateTokens(string.Concat(fed)) > 1600,
            "预算够时摘要输入应覆盖多条归档消息，而不是只喂最近一条");
    }

    [TestMethod]
    public async Task 压缩_摘要器未真正归约时不写摘要()
    {
        var sessions = new RecordingSessionManager();
        // 摘要器会把 system 消息单独前置保留：此时输出是「system + 摘要 + 尾部」三条，首条不是摘要。
        // 必须放弃压缩，不能拿一条正文冒充摘要写库（否则历史被归档却只留假摘要）。
        // 预算调到恰好只归档「system + 超长首条」，且摘要输入装得下两者，确保真正走到摘要器。
        sessions.Active.Add(new SessionMessage { Id = 1, Role = "system", Content = "系统说明" });
        sessions.Active.Add(new SessionMessage { Id = 2, Role = "user", Content = new string('一', 1000) });
        sessions.Active.Add(new SessionMessage { Id = 3, Role = "user", Content = new string('二', 50) });
        sessions.Active.Add(new SessionMessage { Id = 4, Role = "assistant", Content = new string('三', 50) });

        var chat = new RecordingChatClient("摘要");
        var compactor = NewCompactor(sessions, chat);

        var result = await compactor.CompactAsync("s1", budget: 2000, incoming: null);

        Assert.IsNull(result);
        Assert.AreEqual(0, sessions.Added.Count);
        Assert.AreEqual(0, sessions.CompactedIds.Count);
        Assert.IsTrue(chat.LastPrompt.Count > 0, "应已调用摘要器，才有可验证的「未真正归约」场景");
        Assert.AreEqual(0, sessions.CompactedIds.Count);
        Assert.IsTrue(chat.LastPrompt.Count > 0, "应已调用摘要器，才有可验证的「未真正归约」场景");
    }

    // ---------------- 阈值与保留窗口 ----------------

    [TestMethod]
    public void 阈值_按窗口留白收敛且档位只能更早()
    {
        var options = new CompactionOptions();

        // 默认取末档：实际触发点 = 窗口减 15% 留白，默认不提前压缩
        Assert.AreEqual(891_290, options.ResolveThreshold(1_048_576, 1_048_576));
        // 用户手选更小的档位就是「宁可早点压」，不被窗口抬高
        Assert.AreEqual(300_000, options.ResolveThreshold(1_048_576, 300_000));
        // 小窗口按比例算出的留白不足下限，用 16k 兜住
        Assert.AreEqual(108_800, options.ResolveThreshold(128_000, 128_000));
        // 窗口比留白下限还小时，至少留一半窗口可用
        Assert.AreEqual(4_096, options.ResolveThreshold(8_192, 8_192));
    }

    [TestMethod]
    public async Task 压缩_保留最近KeepTokens原文()
    {
        var sessions = new RecordingSessionManager();
        for (var i = 0; i < 20; i++)
        {
            sessions.Active.Add(new SessionMessage
            {
                Id = i + 1,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = new string('一', 1000) // 每条约 1000 token
            });
        }

        var chat = new RecordingChatClient("历史摘要");
        var compactor = NewCompactor(sessions, chat);

        var result = await compactor.CompactAsync("s1", budget: 100_000, incoming: null);

        Assert.IsNotNull(result);
        // 保留窗口按 token 算：默认 15k，即最近 15 条，与消息条数配置无关
        Assert.AreEqual(5, result.ArchivedCount);
        Assert.AreEqual(15, result.KeptCount);
    }

    [TestMethod]
    public async Task 压缩_最新一条超过保留上限时仍保留()
    {
        var sessions = new RecordingSessionManager();
        for (var i = 0; i < 3; i++)
        {
            sessions.Active.Add(new SessionMessage { Id = i + 1, Role = "user", Content = "一二三四五六七八九十" });
        }

        sessions.Active.Add(new SessionMessage
        {
            Id = 4,
            Role = "assistant",
            Content = new string('二', 8000) // 单条就超过保留上限
        });

        var chat = new RecordingChatClient("历史摘要");
        var compactor = NewCompactor(sessions, chat);

        var result = await compactor.CompactAsync("s1", budget: 10_000, incoming: null);

        Assert.IsNotNull(result);
        // 没有接续点，下一轮只能对着一堆摘要说「继续」
        Assert.AreEqual(3, result.ArchivedCount);
        Assert.AreEqual(1, result.KeptCount);
        Assert.IsTrue(result.TokensAfter > 5000);
    }

    [TestMethod]
    public async Task 压缩_唯一一条超长消息仍归档()
    {
        var sessions = new RecordingSessionManager();
        sessions.Active.Add(new SessionMessage { Id = 1, Role = "user", Content = new string('一', 8000) });

        var chat = new RecordingChatClient("历史摘要");
        var compactor = NewCompactor(sessions, chat);

        var result = await compactor.CompactAsync("s1", budget: 10_000, incoming: null);

        Assert.IsNotNull(result);
        // 只有这一条时硬留等于没压：归档后至少还能靠摘要接上
        Assert.AreEqual(1, result.ArchivedCount);
        Assert.AreEqual(0, result.KeptCount);
        CollectionAssert.AreEqual(new long[] { 1 }, sessions.CompactedIds.ToArray());
    }

    // ---------------- 事件文案 ----------------

    [TestMethod]
    public void 压缩文案_开始与完成可区分()
    {
        var started = ContextCompactedEvent.Started(ContextCompactTriggers.Preflight, 8000);
        StringAssert.Contains(started.Describe(), "正在压缩");

        var done = ContextCompactedEvent.Done(
            ContextCompactTriggers.Overflow,
            new ContextCompactionResult(3, 2, 9000, 1200, 8000));
        StringAssert.Contains(done.Describe(), "上下文已压缩");
        StringAssert.Contains(done.Describe(), "归档 3 条");
        StringAssert.Contains(done.Describe(), "保留 2 条");
    }

    [TestMethod]
    public void 压缩文案_失败时不冒充已完成()
    {
        // 压缩失败必须自报失败，否则 UI 会停在「正在压缩…」或误以为历史已裁掉
        var failed = ContextCompactedEvent.Failed(ContextCompactTriggers.Overflow, 8000);

        StringAssert.Contains(failed.Describe(), "未生效");
        Assert.IsFalse(failed.Describe().Contains("已压缩"));
    }

    [TestMethod]
    public void 压缩文案_预算与估算未知时不带括号用量()
    {
        var done = ContextCompactedEvent.Done(
            ContextCompactTriggers.Preflight,
            new ContextCompactionResult(1, 1, 0, 0, null));

        Assert.IsFalse(done.Describe().Contains("tokens"));
    }

    // ---------------- 帧还原（远端客户端） ----------------

    [TestMethod]
    public void 帧还原_压缩事件带全量字段()
    {
        var frame = AgentSseFrame.Create(AgentSseEventNames.ContextCompacted, new
        {
            phase = ContextCompactionPhases.Done,
            trigger = ContextCompactTriggers.Overflow,
            archivedCount = 7,
            keptCount = 3,
            estimatedTokens = 1500,
            budget = 8000,
            message = "📦 上下文已压缩"
        });

        Assert.IsTrue(AgentEventTranslator.TryTranslate(frame, out var evt));
        var compacted = evt as ContextCompactedEvent;
        Assert.IsNotNull(compacted);
        Assert.AreEqual(ContextCompactionPhases.Done, compacted.Phase);
        Assert.AreEqual(ContextCompactTriggers.Overflow, compacted.Trigger);
        Assert.AreEqual(7, compacted.ArchivedCount);
        Assert.AreEqual(3, compacted.KeptCount);
        Assert.AreEqual(1500, compacted.EstimatedTokens);
        Assert.AreEqual(8000, compacted.Budget);
    }

    [TestMethod]
    public void 帧还原_预算缺省解析为null()
    {
        var frame = AgentSseFrame.Create(AgentSseEventNames.ContextCompacted, new
        {
            phase = ContextCompactionPhases.Started,
            trigger = ContextCompactTriggers.Preflight
        });

        Assert.IsTrue(AgentEventTranslator.TryTranslate(frame, out var evt));
        Assert.IsNull(((ContextCompactedEvent)evt!).Budget);
    }

    // ---------------- 起跑前预检 ----------------

    [TestMethod]
    public async Task 守卫_估算超预算时先压缩再起跑()
    {
        var sessions = new RecordingSessionManager();
        for (var i = 0; i < 10; i++)
        {
            sessions.Active.Add(new SessionMessage
            {
                Id = i + 1,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = new string('一', 1000)
            });
        }

        var host = new ScriptedHost([new TextDeltaEvent("ok")]);
        var guard = NewGuard(host, sessions, budget: 2000);

        var events = await CollectAsync(guard.RunStreamingAsync(Request("继续")));

        Assert.AreEqual(1, host.Calls);
        Assert.AreEqual(ContextCompactionPhases.Started, ((ContextCompactedEvent)events[0]).Phase);
        Assert.AreEqual(ContextCompactionPhases.Done, ((ContextCompactedEvent)events[1]).Phase);
        Assert.IsInstanceOfType<TextDeltaEvent>(events[2]);
        Assert.AreEqual(ContextCompactTriggers.Preflight, ((ContextCompactedEvent)events[1]).Trigger);
    }

    [TestMethod]
    public async Task 守卫_预算未知时跳过预检()
    {
        var sessions = new RecordingSessionManager();
        sessions.Active.Add(new SessionMessage { Id = 1, Role = "user", Content = new string('一', 100000) });

        var host = new ScriptedHost([new TextDeltaEvent("ok")]);
        var guard = NewGuard(host, sessions, budget: null);

        var events = await CollectAsync(guard.RunStreamingAsync(Request("继续")));

        Assert.AreEqual(1, host.Calls);
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(0, sessions.Calls.Count);
    }

    [TestMethod]
    public async Task 守卫_预检压缩失败时发失败标记并照常起跑()
    {
        var sessions = new RecordingSessionManager();
        for (var i = 0; i < 10; i++)
        {
            sessions.Active.Add(new SessionMessage
            {
                Id = i + 1,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = new string('一', 1000)
            });
        }

        var host = new ScriptedHost([new TextDeltaEvent("ok")]);
        var guard = NewGuard(
            host, sessions, budget: 2000, chat: new RecordingChatClient("摘要") { ThrowOnCall = true });

        var events = await CollectAsync(guard.RunStreamingAsync(Request("继续")));

        // 摘要失败不阻断对话：历史原样保留，用户看到「未生效」而不是卡在「正在压缩…」
        Assert.AreEqual(1, host.Calls);
        CollectionAssert.AreEqual(
            new[] { ContextCompactionPhases.Started, ContextCompactionPhases.Failed },
            events.OfType<ContextCompactedEvent>().Select(e => e.Phase).ToArray());
        Assert.AreEqual("ok", events.OfType<TextDeltaEvent>().Single().Delta);
        Assert.IsTrue(sessions.Active.Count > 0);
        Assert.AreEqual(0, sessions.Calls.Count);
    }

    // ---------------- 超长兜底重跑 ----------------

    [TestMethod]
    public async Task 守卫_首调超长且无产出时压缩后重跑一次()
    {
        var sessions = new RecordingSessionManager();
        sessions.Active.Add(new SessionMessage { Id = 1, Role = "user", Content = new string('一', 5000) });

        var host = new ScriptedHost([
            new ErrorEvent("上下文超出模型长度上限，请压缩历史后重试")
        ], [
            new TextDeltaEvent("重跑结果")
        ]);
        // 预算未知 -> 预检跳过，靠超长兜底
        var guard = NewGuard(host, sessions, budget: null);

        var events = await CollectAsync(guard.RunStreamingAsync(Request("继续")));

        Assert.AreEqual(2, host.Calls);

        // 首调的报错被吞掉，用户只看到「正在压缩 → 已压缩 → 重跑正文」
        CollectionAssert.AreEqual(
            new[]
            {
                ContextCompactionPhases.Started,
                ContextCompactionPhases.Done,
                ContextCompactTriggers.Overflow
            },
            new[]
            {
                ((ContextCompactedEvent)events[0]).Phase,
                ((ContextCompactedEvent)events[1]).Phase,
                ((ContextCompactedEvent)events[1]).Trigger
            });

        var text = events.OfType<TextDeltaEvent>().ToList();
        Assert.AreEqual(1, text.Count);
        Assert.AreEqual("重跑结果", text[0].Delta);
        Assert.AreEqual(0, events.OfType<ErrorEvent>().Count());
    }

    [TestMethod]
    public async Task 守卫_已产出正文后超长不重跑()
    {
        var sessions = new RecordingSessionManager();
        sessions.Active.Add(new SessionMessage { Id = 1, Role = "user", Content = "历史" });

        var host = new ScriptedHost([[
            new TextDeltaEvent("半成品"),
            new ErrorEvent("上下文超出模型长度上限，请压缩历史后重试")
        ]]);
        var guard = NewGuard(host, sessions, budget: null);

        var events = await CollectAsync(guard.RunStreamingAsync(Request("继续")));

        Assert.AreEqual(1, host.Calls);
        Assert.AreEqual(1, events.OfType<ErrorEvent>().Count());
        Assert.AreEqual("半成品", events.OfType<TextDeltaEvent>().Single().Delta);
    }

    [TestMethod]
    public async Task 守卫_重跑仍超长时把错误交回调用方()
    {
        var sessions = new RecordingSessionManager();
        sessions.Active.Add(new SessionMessage { Id = 1, Role = "user", Content = new string('一', 5000) });

        var host = new ScriptedHost([
            new ErrorEvent("上下文超出模型长度上限，请压缩历史后重试"),
            new ErrorEvent("上下文超出模型长度上限，请压缩历史后重试")
        ]);
        var guard = NewGuard(host, sessions, budget: null);

        var events = await CollectAsync(guard.RunStreamingAsync(Request("继续")));

        Assert.AreEqual(2, host.Calls);
        Assert.AreEqual(1, events.OfType<ErrorEvent>().Count());
        StringAssert.Contains(events.OfType<ErrorEvent>().Single().Message, "上下文超出模型长度上限");
    }

    [TestMethod]
    public async Task 守卫_兜底压缩失败时先报失败再回传原错误()
    {
        var sessions = new RecordingSessionManager();
        sessions.Active.Add(new SessionMessage { Id = 1, Role = "user", Content = new string('一', 5000) });

        var host = new ScriptedHost([
            new ErrorEvent("上下文超出模型长度上限，请压缩历史后重试")
        ]);
        var guard = NewGuard(
            host, sessions, budget: null, chat: new RecordingChatClient("摘要") { ThrowOnCall = true });

        var events = await CollectAsync(guard.RunStreamingAsync(Request("继续")));

        // 不重跑（压缩没成功，重跑必然同样超长），但要明确告诉用户压缩失败
        Assert.AreEqual(1, host.Calls);
        CollectionAssert.AreEqual(
            new[] { ContextCompactionPhases.Started, ContextCompactionPhases.Failed },
            events.OfType<ContextCompactedEvent>().Select(e => e.Phase).ToArray());
        StringAssert.Contains(events.OfType<ErrorEvent>().Single().Message, "上下文超出模型长度上限");
    }

    [TestMethod]
    public async Task 守卫_已产出正文后抛超长异常不重跑()
    {
        var sessions = new RecordingSessionManager();
        sessions.Active.Add(new SessionMessage { Id = 1, Role = "user", Content = "历史" });

        var info = new AgentApiErrorInfo(
            AgentApiErrorCategory.ContextLengthExceeded,
            400,
            "context_length_exceeded",
            "上下文超出模型长度上限，请压缩历史后重试",
            IsRetryable: false);

        var host = new ScriptedHost([new TextDeltaEvent("半成品")])
        {
            // 先产出正文再抛超长：与流内 ErrorEvent 同一判据，不允许重跑
            ThrowAfterFirst = new AgentApiException(info, new InvalidOperationException("too long"))
        };

        var guard = NewGuard(host, sessions, budget: null);

        // 异常照常抛给调用方（CLI / Codex 各有 AgentApiException 提示分支），不吞成重跑
        await Assert.ThrowsExactlyAsync<AgentApiException>(
            () => CollectAsync(guard.RunStreamingAsync(Request("继续"))));
        Assert.AreEqual(1, host.Calls);
    }

    [TestMethod]
    public async Task 守卫_首调抛超长异常时同样重跑()
    {
        var sessions = new RecordingSessionManager();
        sessions.Active.Add(new SessionMessage { Id = 1, Role = "user", Content = new string('一', 5000) });

        var info = new AgentApiErrorInfo(
            AgentApiErrorCategory.ContextLengthExceeded,
            400,
            "context_length_exceeded",
            "上下文超出模型长度上限，请压缩历史后重试",
            IsRetryable: false);

        var host = new ScriptedHost([new TextDeltaEvent("重跑结果")])
        {
            ThrowFirst = new AgentApiException(info, new InvalidOperationException("too long"))
        };

        var guard = NewGuard(host, sessions, budget: null);

        var events = await CollectAsync(guard.RunStreamingAsync(Request("继续")));

        Assert.AreEqual(2, host.Calls);
        Assert.AreEqual("重跑结果", events.OfType<TextDeltaEvent>().Single().Delta);
    }

    [TestMethod]
    public async Task 守卫_压缩器不可用时原样透传()
    {
        var host = new ScriptedHost([[new TextDeltaEvent("正文")]]);
        // 模拟「未选择模型」：压缩器解析会创建聊天客户端并抛错。
        // 该错误不能把原有错误通道变成抛异常，应退回内层宿主照常起跑。
        var root = new DiContainer()
            .AddSingleton<ContextCompactor>(_ => throw new InvalidOperationException("请先选择模型"))
            .Build(validateScopes: false, validateOnBuild: false);
        var guard = new ContextGuardAgentHost(host, root);

        var events = await CollectAsync(guard.RunStreamingAsync(Request("继续")));

        Assert.AreEqual(1, host.Calls);
        Assert.AreEqual("正文", events.OfType<TextDeltaEvent>().Single().Delta);
        Assert.AreEqual(0, events.OfType<ContextCompactedEvent>().Count());
    }

    // ---------------- 摘要模型 ----------------

    [TestMethod]
    public void 摘要模型_留空时沿用会话模型()
    {
        var session = new RecordingChatClient("会话摘要");
        var router = new StubProviderRouter(_ => new RecordingChatClient("独立摘要"));
        var factory = new CompactionChatClientFactory(
            router, session, new CompactionOptions(), new StubWindowLookup(5000));

        Assert.AreSame(session, factory.Create().Client);
        Assert.IsNull(factory.Create().ContextWindow, "沿用会话模型时不额外收窄摘要输入");
        Assert.AreEqual(0, router.Models.Count);
    }

    [TestMethod]
    public void 摘要模型_配置后使用独立客户端()
    {
        var session = new RecordingChatClient("会话摘要");
        var summarizer = new RecordingChatClient("独立摘要");
        var router = new StubProviderRouter(_ => summarizer);
        var factory = new CompactionChatClientFactory(
            router, session, new CompactionOptions { SummarizerModel = " cheap:small " },
            new StubWindowLookup(12_000));

        var client = factory.Create();
        Assert.AreSame(summarizer, client.Client);
        Assert.AreEqual(12_000, client.ContextWindow, "摘要输入要按摘要模型自己的窗口收敛");
        // 配置值两侧空白在校验前去掉，否则限定名匹配不上供应商
        CollectionAssert.AreEqual(new[] { "cheap:small" }, router.Models);
    }

    [TestMethod]
    public void 摘要模型_不可用时回落到会话模型()
    {
        var session = new RecordingChatClient("会话摘要");
        var router = new StubProviderRouter(_ => throw new InvalidOperationException("模型不存在"));
        var factory = new CompactionChatClientFactory(
            router, session, new CompactionOptions { SummarizerModel = "gone:missing" },
            new StubWindowLookup(null));

        // 摘要模型配错只影响成本，不影响压缩本身
        Assert.AreSame(session, factory.Create().Client);
    }

    [TestMethod]
    public async Task 压缩_摘要由配置模型生成()
    {
        var sessions = new RecordingSessionManager();
        for (var i = 0; i < 2; i++)
        {
            sessions.Active.Add(new SessionMessage
            {
                Id = i + 1,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = new string('一', 1000)
            });
        }

        var sessionClient = new RecordingChatClient("会话摘要");
        var summarizer = new RecordingChatClient("独立摘要");
        var compactor = new ContextCompactor(
            sessions, new FixedChatClientFactory(summarizer), new CompactionOptions { KeepTokens = 1 });

        var result = await compactor.CompactAsync("s1", budget: null, incoming: null);

        Assert.IsNotNull(result);
        Assert.AreEqual("独立摘要", sessions.Added.Single().Content);
        Assert.IsTrue(summarizer.LastPrompt.Count > 0);
        Assert.AreEqual(0, sessionClient.LastPrompt.Count, "会话模型不应被摘要占用");
    }

    [TestMethod]
    public async Task 压缩_摘要模型窗口更小时按其窗口收敛输入()
    {
        var sessions = new RecordingSessionManager();
        for (var i = 0; i < 20; i++)
        {
            sessions.Active.Add(new SessionMessage
            {
                Id = i + 1,
                Role = "user",
                Content = new string('一', 4000)
            });
        }

        var summarizer = new RecordingChatClient("摘要");
        var compactor = new ContextCompactor(
            sessions,
            new FixedChatClientFactory(summarizer, window: 2000),
            new CompactionOptions());

        await compactor.CompactAsync("s1", budget: 4000, incoming: null);

        // 会话阈值八成 = 3200；摘要模型只有 2000 窗口，最后按它的八成收紧到 1600
        var fed = ContextCompactor.EstimateTokens(string.Concat(summarizer.LastPrompt));
        Assert.IsTrue(fed <= 1600, $"摘要输入 {fed} token，超过摘要模型窗口八成");
        Assert.IsTrue(fed > 200, "窗口够时仍应覆盖多条归档消息");
    }

    // ---------------- 组装 ----------------

    private static AgentRunRequest Request(string input) => new() { Input = input, SessionId = "s1" };

    private static ContextCompactor NewCompactor(
        ISessionManager sessions, IChatClient chat, CompactionOptions? compaction = null)
        => new(sessions, new FixedChatClientFactory(chat), compaction ?? new CompactionOptions());

    private static ContextGuardAgentHost NewGuard(
        IAgentHost host, ISessionManager sessions, int? budget, IChatClient? chat = null)
    {
        var root = new DiContainer()
            .AddSingleton(NewCompactor(sessions, chat ?? new RecordingChatClient("压缩摘要")))
            .AddSingleton<ISessionGenerationSettings>(new FixedBudgetSettings(budget))
            .Build(validateScopes: false, validateOnBuild: false);

        return new ContextGuardAgentHost(host, root);
    }

    private static async Task<List<AgentStreamEvent>> CollectAsync(IAsyncEnumerable<AgentStreamEvent> stream)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var evt in stream)
        {
            events.Add(evt);
        }

        return events;
    }

    /// <summary>会话桩：记录调用顺序与归档范围。</summary>
    private sealed class RecordingSessionManager : ISessionManager
    {
        public List<SessionMessage> Active { get; } = new();

        public List<string> Calls { get; } = new();

        public List<SessionMessage> Added { get; } = new();

        public List<long> CompactedIds { get; } = new();

        public SessionInfo? CurrentSession => null;

        public Task<SessionInfo> CreateSessionAsync(string? title = null, string? workspaceId = null)
            => Task.FromResult(new SessionInfo());

        public Task<SessionInfo?> GetSessionAsync(string sessionId)
            => Task.FromResult<SessionInfo?>(new SessionInfo { SessionId = sessionId });

        public Task<IEnumerable<SessionInfo>> GetUserSessionsAsync()
            => Task.FromResult<IEnumerable<SessionInfo>>([]);

        public Task UpdateSessionTitleAsync(string sessionId, string title) => Task.CompletedTask;

        public Task DeleteSessionAsync(string sessionId) => Task.CompletedTask;

        public Task<SessionMessage> AddMessageAsync(string sessionId, string role, string content, int? tokens = null,
            string? thinking = null, string? attachments = null, string? toolCalls = null, string? segments = null,
            int? durationMs = null)
        {
            Calls.Add($"add:{role}");
            var message = new SessionMessage
            {
                SessionId = sessionId,
                Role = role,
                Content = content,
                Tokens = tokens
            };
            Added.Add(message);
            return Task.FromResult(message);
        }

        public Task<IEnumerable<SessionMessage>> GetMessagesAsync(string sessionId, int? limit = null)
            => Task.FromResult<IEnumerable<SessionMessage>>(Active);

        public Task<IEnumerable<SessionMessage>> GetLatestMessagesAsync(string sessionId, int count)
            => Task.FromResult<IEnumerable<SessionMessage>>(Active);

        public Task ClearMessagesAsync(string sessionId) => Task.CompletedTask;

        public Task<SessionStats> GetSessionStatsAsync(string sessionId) => Task.FromResult(new SessionStats());

        public Task SetCurrentSessionAsync(string sessionId) => Task.CompletedTask;

        public void ClearCurrentSession()
        {
        }

        public Task ClearAllSessionsAsync(string? workspaceId = null) => Task.CompletedTask;

        public Task<GlobalSessionStats> GetGlobalStatsAsync(int? days = null)
            => Task.FromResult(new GlobalSessionStats());

        public Task<IEnumerable<SessionMessage>> GetActiveMessagesAsync(string sessionId)
            => Task.FromResult<IEnumerable<SessionMessage>>(Active);

        public Task MarkMessagesCompactedAsync(string sessionId, IEnumerable<long> messageIds)
        {
            var ids = messageIds.ToList();
            CompactedIds.AddRange(ids);
            Calls.Add($"compact:{ids.Count}");

            // 归档后从活跃集合移除，模拟真实的 GetActiveMessagesAsync 视图
            Active.RemoveAll(m => ids.Contains(m.Id));
            return Task.CompletedTask;
        }
    }

    /// <summary>聊天桩：只回摘要文本，并记录摘要调用收到的提示内容。</summary>
    private sealed class RecordingChatClient(string summary) : IChatClient
    {
        public List<string> LastPrompt { get; private set; } = new();

        /// <summary>置真时摘要调用直接抛错，用于验证压缩失败的提示路径。</summary>
        public bool ThrowOnCall { get; init; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnCall)
            {
                throw new InvalidOperationException("摘要模型调用失败");
            }

            LastPrompt = messages.Select(m => m.Text ?? string.Empty).ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, summary)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (ThrowOnCall)
            {
                throw new InvalidOperationException("摘要模型调用失败");
            }

            LastPrompt = messages.Select(m => m.Text ?? string.Empty).ToList();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, summary);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>摘要客户端工厂桩：固定返回给定客户端（可指定窗口）。</summary>
    private sealed class FixedChatClientFactory(IChatClient client, int? window = null) : ICompactionChatClientFactory
    {
        public CompactionChatClient Create() => new(client, window);
    }

    /// <summary>模型窗口桩：所有模型返回同一个窗口。</summary>
    private sealed class StubWindowLookup(int? window) : IModelContextWindowLookup
    {
        public int? GetContextWindow(string modelKey) => window;
    }

    /// <summary>供应商路由桩：记录被请求的模型名，客户端由回调决定。</summary>
    private sealed class StubProviderRouter(Func<string?, IChatClient> create) : IProviderRouter
    {
        public List<string?> Models { get; } = new();

        public IChatClient CreateChatClient(string? providerModel = null)
        {
            Models.Add(providerModel);
            return create(providerModel);
        }

        public IReadOnlyList<ProviderInfo> GetAvailableProviders() => [];
    }

    /// <summary>按脚本逐次产出事件的宿主桩；脚本用尽后重复最后一段。</summary>
    private sealed class ScriptedHost : IAgentHost
    {
        private readonly List<AgentStreamEvent[]> _attempts;

        public ScriptedHost(params AgentStreamEvent[][] attempts) => _attempts = attempts.ToList();

        /// <summary>首次调用改抛的异常；非 null 时替代脚本产出。</summary>
        public Exception? ThrowFirst { get; set; }

        /// <summary>首次调用在产出完脚本事件之后再抛的异常；用于验证「已产出不重跑」。</summary>
        public Exception? ThrowAfterFirst { get; set; }

        public int Calls { get; private set; }

        public bool IsInitialized => true;

        public DiContainer Services => null!;

        public string? CurrentSessionId => "s1";

        public Task InitializeAsync(string sessionId, CancellationToken ct = default, AgentBuildOverrides? overrides = null)
            => Task.CompletedTask;

        public void Reset(string? sessionId = null)
        {
        }

        public async IAsyncEnumerable<AgentStreamEvent> RunStreamingAsync(
            AgentRunRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var attempt = Calls++;
            if (attempt == 0 && ThrowFirst is not null)
            {
                throw ThrowFirst;
            }

            var script = attempt < _attempts.Count ? _attempts[attempt] : _attempts[^1];
            foreach (var evt in script)
            {
                await Task.Yield();
                yield return evt;
            }

            if (attempt == 0 && ThrowAfterFirst is not null)
            {
                throw ThrowAfterFirst;
            }
        }
    }

    /// <summary>固定预算的生成参数桩。</summary>
    private sealed class FixedBudgetSettings(int? budget) : ISessionGenerationSettings
    {
        public ValueTask<string?> ResolveReasoningEffortAsync(string? sessionId, CancellationToken ct = default)
            => ValueTask.FromResult<string?>(null);

        public ValueTask<ContextBudget?> ResolveContextBudgetAsync(string? sessionId, CancellationToken ct = default)
            => ValueTask.FromResult(budget is { } value ? new ContextBudget(value, value) : (ContextBudget?)null);

        public ValueTask<SessionGenerationState> ResolveAsync(
            string? sessionId, ModelProfile? profile = null, CancellationToken ct = default)
            => ValueTask.FromResult(new SessionGenerationState(
                null,
                budget is { } value ? new ContextBudget(value, value) : null));

        public IReadOnlyList<int> GetTiers(string modelKey) => [];
    }
}