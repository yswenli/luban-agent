/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： SessionAutoNameUnitTest
*描述：会话自动命名——输出清洗与截断、触发判定（仅首条消息 + 仅默认标题）、
*      失败不覆盖、条件更新失败不广播。
*
*****************************************************************************/

using LubanAgentCore.Services;

namespace LubanAgent.Tests;

[TestClass]
public class SessionAutoNameUnitTest
{
    [TestMethod]
    public void Sanitize_剥离前缀引号与末尾标点()
    {
        var title = SessionAutoNamer.Sanitize("标题：“修复登录页的滑块验证。”");

        Assert.AreEqual("修复登录页的滑块验证", title);
    }

    [TestMethod]
    public void Sanitize_压空白并截断到十五字()
    {
        var title = SessionAutoNamer.Sanitize("帮我\n\n把  这个   需求拆成 可执行的开发任务清单并排优先级");

        Assert.AreEqual("帮我 把 这个 需求拆成 可执", title);
    }

    [TestMethod]
    public void Sanitize_超长按字符截断不切半个字符()
    {
        // 8 个 emoji（每个占两个 UTF-16 码元）+ 10 个汉字：按 Unicode 字符应截到 15 个
        var title = SessionAutoNamer.Sanitize("😀😀😀😀😀😀😀😀一二三四五六七八九十");

        Assert.AreEqual("😀😀😀😀😀😀😀😀一二三四五六七", title);
        Assert.AreEqual(23, title!.Length);
    }

    [TestMethod]
    public void Sanitize_空输出返回空()
    {
        Assert.IsNull(SessionAutoNamer.Sanitize("   "));
        Assert.IsNull(SessionAutoNamer.Sanitize("标题："));
    }

    [TestMethod]
    public void NormalizeInput_换行压成单空格并截断超长输入()
    {
        Assert.AreEqual("第一行 第二行", SessionAutoNamer.NormalizeInput("  第一行\r\n第二行  "));

        var longText = new string('字', 5000);
        Assert.AreEqual(4000, SessionAutoNamer.NormalizeInput(longText).Length);
    }

    [TestMethod]
    public void IsDefaultTitle_三端历史默认值都算未命名()
    {
        Assert.IsTrue(SessionAutoNameRunner.IsDefaultTitle("新对话"));
        Assert.IsTrue(SessionAutoNameRunner.IsDefaultTitle("新会话"));
        Assert.IsTrue(SessionAutoNameRunner.IsDefaultTitle("未命名"));
        Assert.IsFalse(SessionAutoNameRunner.IsDefaultTitle("修复登录页"));
        Assert.IsFalse(SessionAutoNameRunner.IsDefaultTitle(null));
    }

    [TestMethod]
    public async Task RunAsync_首条消息且默认标题_写入并广播()
    {
        var store = new FakeTitleStore("新对话", 0);
        var namer = new FakeNamer("修复登录页滑块验证");
        var notifier = new SessionTitleNotifier();
        var published = new List<SessionTitleChanged>();
        notifier.Changed += published.Add;

        var title = await new SessionAutoNameRunner(store, namer, notifier)
            .RunAsync("s-1", "u-1", "登录页的滑块验证坏了，帮我看看");

        Assert.AreEqual("修复登录页滑块验证", title);
        Assert.AreEqual("修复登录页滑块验证", store.Written);
        Assert.AreEqual(1, namer.Calls);
        Assert.AreEqual(1, published.Count);
        Assert.AreEqual("s-1", published[0].SessionId);
    }

    [TestMethod]
    public async Task RunAsync_已有消息_不再触发也不调用模型()
    {
        var store = new FakeTitleStore("新对话", 2);
        var namer = new FakeNamer("不该出现");

        var title = await new SessionAutoNameRunner(store, namer)
            .RunAsync("s-1", "u-1", "第二条消息");

        Assert.IsNull(title);
        Assert.AreEqual(0, namer.Calls);
        Assert.AreEqual(0, store.WriteAttempts);
    }

    [TestMethod]
    public async Task RunAsync_标题已被手动改过_不再触发()
    {
        var store = new FakeTitleStore("我自己起的名字", 0);
        var namer = new FakeNamer("不该出现");

        var title = await new SessionAutoNameRunner(store, namer)
            .RunAsync("s-1", "u-1", "首条消息");

        Assert.IsNull(title);
        Assert.AreEqual(0, namer.Calls);
        Assert.AreEqual(0, store.WriteAttempts);
    }

    [TestMethod]
    public async Task RunAsync_命名失败_保留默认标题且不广播()
    {
        var store = new FakeTitleStore("新对话", 0);
        var namer = new FakeNamer(null);
        var notifier = new SessionTitleNotifier();
        var published = 0;
        notifier.Changed += _ => published++;

        var title = await new SessionAutoNameRunner(store, namer, notifier)
            .RunAsync("s-1", "u-1", "首条消息");

        Assert.IsNull(title);
        Assert.IsNull(store.Written);
        Assert.AreEqual(0, published);
    }

    [TestMethod]
    public async Task RunAsync_生成期间用户抢先改名_条件更新失败不广播()
    {
        var store = new FakeTitleStore("新对话", 0) { AcceptWrite = false };
        var namer = new FakeNamer("自动主题");
        var notifier = new SessionTitleNotifier();
        var published = 0;
        notifier.Changed += _ => published++;

        var title = await new SessionAutoNameRunner(store, namer, notifier)
            .RunAsync("s-1", "u-1", "首条消息");

        Assert.IsNull(title);
        Assert.AreEqual(1, store.WriteAttempts);
        Assert.AreEqual(0, published);
    }

    private sealed class FakeTitleStore(string? title, int messageCount) : IAutoTitleStore
    {
        public string? Written { get; private set; }

        public int WriteAttempts { get; private set; }

        public bool AcceptWrite { get; init; } = true;

        public Task<(string? Title, int MessageCount)?> GetAutoTitleStateAsync(string sessionId, string userId)
            => Task.FromResult<(string? Title, int MessageCount)?>((title, messageCount));

        public Task<bool> UpdateTitleIfDefaultAsync(
            string sessionId, string userId, IReadOnlyList<string> defaultTitles, string newTitle)
        {
            WriteAttempts++;
            if (!AcceptWrite) return Task.FromResult(false);

            Written = newTitle;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeNamer(string? title) : ISessionAutoNamer
    {
        public int Calls { get; private set; }

        public Task<string?> AutoSessionNameAsync(string? sessionId, string text, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(title);
        }
    }
}