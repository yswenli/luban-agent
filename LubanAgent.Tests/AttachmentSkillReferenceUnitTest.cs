/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： AttachmentSkillReferenceUnitTest
*创建时间：2026/11/23
*描述：`@` 技能补全与附件校验单测——锁死口径：
*      CLI 输入框选中技能后会插入 `@技能标识`，它不是文件路径；
*      提交校验必须能按「已知技能标识」跳过，否则会被判为无效附件而阻止发送。
*      未知标识、真实文件引用与大小写口径在本用例中一并固定。
*
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class AttachmentSkillReferenceUnitTest
{
    private string _root = string.Empty;
    private string _png = string.Empty;
    private AttachmentService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "luban-skill-ref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _png = Path.Combine(_root, "shot.png");
        File.WriteAllBytes(_png, Array.Empty<byte>());
        _png = Path.GetFullPath(_png);

        _service = new AttachmentService(new FakeProcessor(), new DiContainer(new ThrowingServiceProvider()));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 临时目录清理失败不影响断言 */ }
    }

    [TestMethod]
    public void FindUnresolvedReferences_SkipsKnownSkillIds()
    {
        var input = $"@brainstorming 帮我分析下 @{_png}";

        var invalid = AttachmentService.FindUnresolvedReferences(input, _service, ["brainstorming"]);

        Assert.AreEqual(0, invalid.Count, "技能标识不是文件引用，不能阻断发送");
    }

    [TestMethod]
    public void FindUnresolvedReferences_WithoutIgnoredList_TreatsSkillIdAsInvalid()
    {
        var invalid = AttachmentService.FindUnresolvedReferences("@brainstorming 帮我分析", _service);

        CollectionAssert.Contains(invalid.ToList(), "brainstorming",
            "未告知技能清单时，仍按原有附件口径判为无效引用");
    }

    [TestMethod]
    public void FindUnresolvedReferences_IgnoresSkillIdCaseInsensitively()
    {
        var invalid = AttachmentService.FindUnresolvedReferences("@BrainStorming 帮我分析", _service, ["brainstorming"]);

        Assert.AreEqual(0, invalid.Count, "技能标识比较忽略大小写");
    }

    [TestMethod]
    public void FindUnresolvedReferences_StillReportsUnknownTokens()
    {
        var invalid = AttachmentService.FindUnresolvedReferences(
            "@brainstorming 与 @no-such-skill 一起", _service, ["brainstorming"]);

        CollectionAssert.AreEqual(new[] { "no-such-skill" }, invalid.ToArray(),
            "忽略清单之外的 @片段必须继续按附件口径校验");
    }

    [TestMethod]
    public void FindUnresolvedReferences_ValidFileReferenceStillPasses()
    {
        var input = $"@\"{_png}\" 看看";

        var invalid = AttachmentService.FindUnresolvedReferences(input, _service, ["brainstorming"]);

        Assert.AreEqual(0, invalid.Count, "带引号的真实文件引用不受技能忽略清单影响");
    }

    /// <summary>只认图片扩展名，其余按不支持处理（真实实现见 DefaultAttachmentProcessor）。</summary>
    private sealed class FakeProcessor : IAttachmentProcessor
    {
        public Task<ProcessedAttachment> ProcessAsync(
            string filePath, bool generateThumbnail = false, CancellationToken ct = default)
            => throw new NotSupportedException("本用例只覆盖引用校验，不处理文件内容");

        public bool IsSupported(string filePath)
            => SupportedExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);

        public string ResolveMediaType(string filePath) => "image/png";

        public IReadOnlyList<string> SupportedExtensions { get; } = [".png", ".jpg", ".jpeg"];
    }

    /// <summary>引用校验不依赖 DI；被误用时立即暴露。</summary>
    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => throw new NotSupportedException($"本用例不应解析服务：{serviceType.Name}");
    }
}