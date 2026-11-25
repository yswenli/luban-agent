using LuBan.AIAgent.Hosting;
using LuBan.DI;
using Microsoft.Extensions.AI;

namespace LubanAgent.Tests;

/// <summary>
/// 单回合用量与耗时采集器：验证累加口径、无用量兜底与耗时落定。
/// </summary>
[TestClass]
public class TurnMetricsUnitTest
{
    [TestMethod]
    public void RecordUsage_多轮往返应累加()
    {
        var metrics = new TurnMetrics();
        metrics.BeginTurn();

        // 工具调用会产生多次 LLM 往返，用量须累加而非覆盖
        metrics.RecordUsage(new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 });
        metrics.RecordUsage(new UsageDetails { InputTokenCount = 200, OutputTokenCount = 35 });

        Assert.IsTrue(metrics.HasUsage);
        Assert.AreEqual(300L, metrics.InputTokens);
        Assert.AreEqual(55L, metrics.OutputTokens);
        Assert.AreEqual(355L, metrics.TotalTokens);
    }

    [TestMethod]
    public void RecordUsage_缺Total时应回退输入加输出()
    {
        var metrics = new TurnMetrics();
        metrics.BeginTurn();

        metrics.RecordUsage(new UsageDetails { InputTokenCount = 12, OutputTokenCount = 8 });

        Assert.AreEqual(20L, metrics.TotalTokens);
    }

    [TestMethod]
    public void RecordUsage_优先采用TotalTokenCount()
    {
        var metrics = new TurnMetrics();
        metrics.BeginTurn();

        // 上游给出的 total 与 input+output 不等时应以 total 为准
        metrics.RecordUsage(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 999 });

        Assert.AreEqual(999L, metrics.TotalTokens);
    }

    [TestMethod]
    public void 未采集到用量时应报未采集()
    {
        var metrics = new TurnMetrics();
        metrics.BeginTurn();
        metrics.RecordUsage(null);
        metrics.EndTurn();

        Assert.IsFalse(metrics.HasUsage);
        Assert.AreEqual(0L, metrics.TotalTokens);
    }

    [TestMethod]
    public void 未调用BeginTurn时用量与耗时应为0()
    {
        var metrics = new TurnMetrics();

        Assert.IsFalse(metrics.HasUsage);
        Assert.AreEqual(0L, metrics.TotalTokens);
        Assert.AreEqual(0, metrics.ElapsedMs);
    }

    [TestMethod]
    public void BeginTurn应重置上一轮数据()
    {
        var metrics = new TurnMetrics();
        metrics.BeginTurn();
        metrics.RecordUsage(new UsageDetails { InputTokenCount = 50, OutputTokenCount = 50 });
        metrics.EndTurn();

        metrics.BeginTurn();

        Assert.IsFalse(metrics.HasUsage);
        Assert.AreEqual(0L, metrics.TotalTokens);
    }

    [TestMethod]
    public async Task EndTurn应落定正耗时()
    {
        var metrics = new TurnMetrics();
        metrics.BeginTurn();
        await Task.Delay(30);

        metrics.EndTurn();

        // 允许调度抖动，只要不低于实际等待量即可
        Assert.IsTrue(metrics.ElapsedMs >= 20, $"实际耗时 {metrics.ElapsedMs}ms 低于等待量");
    }

    [TestMethod]
    public async Task 未调用EndTurn时耗时也应有值()
    {
        var metrics = new TurnMetrics();
        metrics.BeginTurn();
        await Task.Delay(15);

        // 异常路径下不会被显式 EndTurn，读取时仍应给出耗时
        Assert.IsTrue(metrics.ElapsedMs >= 10, $"实际耗时 {metrics.ElapsedMs}ms 低于等待量");
    }

    [TestMethod]
    public void EndTurn应记录取消标记()
    {
        var metrics = new TurnMetrics();
        metrics.BeginTurn();
        metrics.EndTurn(cancelled: true);

        Assert.IsTrue(metrics.IsCancelled);
    }

    [TestMethod]
    public void 未落定的末段思考也要出现在耗时列表里()
    {
        var metrics = new TurnMetrics();
        metrics.BeginTurn();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        metrics.RecordThinking(t0);
        metrics.RecordThinking(t0.AddSeconds(7));

        // 历史提供者在 EndTurn 之前读取，末段思考不能丢
        CollectionAssert.AreEqual(new[] { 7000 }, metrics.ThinkingDurationsMs.ToArray());

        // 读取是只读投影：再 EndTurn 不会把同一段重复计入
        metrics.EndTurn();
        CollectionAssert.AreEqual(new[] { 7000 }, metrics.ThinkingDurationsMs.ToArray());
    }

    [TestMethod]
    public void 同一作用域内应解析出同一实例()
    {
        // AgentHost 与 SessionChatHistoryProvider 分处不同类，只有同 scope 共享才能把用量传下去
        var root = new DiContainer()
            .AddScoped<TurnMetrics>()
            .Build(validateScopes: false, validateOnBuild: false);

        using var scope = root.CreateScope();
        var first = scope.GetRequiredService<TurnMetrics>();
        var second = scope.GetRequiredService<TurnMetrics>();

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void 不同作用域应解析出不同实例()
    {
        var root = new DiContainer()
            .AddScoped<TurnMetrics>()
            .Build(validateScopes: false, validateOnBuild: false);

        using var scopeA = root.CreateScope();
        using var scopeB = root.CreateScope();

        // 跨会话串扰的直接防线：不同会话的 scope 不得共享用量
        Assert.AreNotSame(scopeA.GetRequiredService<TurnMetrics>(), scopeB.GetRequiredService<TurnMetrics>());
    }

    [TestMethod]
    public void 思考区间与工具耗时按发生顺序落定()
    {
        var metrics = new TurnMetrics();
        metrics.BeginTurn();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // 同一段思考的多帧增量只算一段：首帧到末帧
        metrics.RecordThinking(t0);
        metrics.RecordThinking(t0.AddSeconds(3));
        metrics.EndThinking();
        metrics.RecordToolStart("call-1", t0.AddSeconds(10));
        metrics.RecordToolEnd("call-1", t0.AddSeconds(12));

        CollectionAssert.AreEqual(new[] { 3000 }, metrics.ThinkingDurationsMs.ToArray());
        Assert.AreEqual(2000, metrics.GetToolDurationMs("call-1"));
        Assert.IsNull(metrics.GetToolDurationMs("call-unknown"));
    }

    [TestMethod]
    public void 以思考收尾时EndTurn也要落定耗时()
    {
        var metrics = new TurnMetrics();
        metrics.BeginTurn();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        metrics.RecordThinking(t0);
        metrics.RecordThinking(t0.AddSeconds(5));

        metrics.EndTurn();

        CollectionAssert.AreEqual(new[] { 5000 }, metrics.ThinkingDurationsMs.ToArray());
    }

    [TestMethod]
    public void 新一轮应清空上一轮的段落耗时()
    {
        var metrics = new TurnMetrics();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        metrics.BeginTurn();
        metrics.RecordThinking(t0);
        metrics.RecordThinking(t0.AddSeconds(2));
        metrics.EndThinking();
        metrics.RecordToolStart("call-1", t0);
        metrics.RecordToolEnd("call-1", t0.AddSeconds(1));

        metrics.BeginTurn();

        Assert.AreEqual(0, metrics.ThinkingDurationsMs.Count);
        Assert.IsNull(metrics.GetToolDurationMs("call-1"));
    }
}