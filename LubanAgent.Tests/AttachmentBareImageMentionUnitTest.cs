/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： AttachmentBareImageMentionUnitTest
*创建时间：2026/10/26
*描述：裸图片路径识别单测——锁死真机缺陷：
*      用户输入「D:\图片\a.png 看看这个图里面有什么」时，路径既没有 @ 前缀，
*      也不满足「整段内容恰为文件路径」的拖拽判定，于是附件通道一条都不命中，
*      模型只能拿到文本路径并改用 ReadFile 读二进制，最后报「无 Pillow/无视觉工具」。
*      修复口径：正文里的裸图片路径自动转附件，成功附加后才从正文剥离。
*
*****************************************************************************/

using LuBan.DI;
using LuBan.AIAgent.Attachments;
using LuBan.AIAgent.Hosting;
using LubanAgentCore.Hosting;
using LubanAgentCore.Models;
using LubanAgentCore.Services;

namespace LubanAgent.Tests;

[TestClass]
public class AttachmentBareImageMentionUnitTest
{
    private string _root = string.Empty;
    private string _png = string.Empty;
    private string _spacedPng = string.Empty;
    private string _note = string.Empty;
    private AttachmentService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "luban-bare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _png = Touch("Marcus.png");
        _spacedPng = Touch("有 空格.png");
        _note = Touch("note.txt");

        _service = new AttachmentService(new FakeProcessor(), new DiContainer(new ThrowingServiceProvider()));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 临时目录清理失败不影响断言 */ }
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return Path.GetFullPath(path);
    }

    [TestMethod]
    public void FindBareImageMentions_DetectsPathFollowedByQuestion()
    {
        var input = $@"{_png} 看看这个图里面有什么";

        var mentions = _service.FindBareImageMentions(input);

        Assert.AreEqual(1, mentions.Count, "「路径 + 问题」必须命中裸图片路径");
        Assert.AreEqual(_png, mentions[0].Path);
        Assert.AreEqual(_png, mentions[0].Text);
        Assert.AreEqual(0, mentions[0].Index);
        Assert.AreEqual(_png.Length, mentions[0].Length);
    }

    [TestMethod]
    public void FindBareImageMentions_QuotedPathWithSpaces()
    {
        var input = $@"""{_spacedPng}"" 这个是什么";

        var mentions = _service.FindBareImageMentions(input);

        Assert.AreEqual(1, mentions.Count, "引号包裹的含空格路径必须命中");
        Assert.AreEqual(_spacedPng, mentions[0].Path);
    }

    [TestMethod]
    public void FindBareImageMentions_IgnoresAtPrefixedReference()
    {
        var input = $"@{_png} 看看";

        var mentions = _service.FindBareImageMentions(input);

        Assert.AreEqual(0, mentions.Count, "@引用 由 ParseFileReferences 负责，裸路径扫描不得重复命中");
    }

    [TestMethod]
    public void FindBareImageMentions_IgnoresNonImageAndMissingFiles()
    {
        Assert.AreEqual(0, _service.FindBareImageMentions($"{_note} 帮我看看").Count,
            "非图片附件不属于裸图片兜底范围");
        Assert.AreEqual(0, _service.FindBareImageMentions(
                Path.Combine(_root, "不存在.png") + " 看看").Count,
            "不存在的路径不得当作附件");
    }

    [TestMethod]
    public void StripBareImageMentions_RemovesOnlyAttachedMentions()
    {
        var input = $"{_png} 和 {_spacedPng} 有什么区别";
        var mentions = _service.FindBareImageMentions(input);
        Assert.AreEqual(2, mentions.Count);

        // 只有第一个附加成功：正文应保留第二个路径，供后续提示用户
        var text = AttachmentService.StripBareImageMentions(input, [mentions[0]]);

        Assert.IsFalse(text.Contains(_png, StringComparison.Ordinal), "已附加的路径必须从正文剥离");
        StringAssert.Contains(text, _spacedPng);
        StringAssert.Contains(text, "有什么区别");
        Assert.IsFalse(text.Contains("  ", StringComparison.Ordinal), "剥离后多余空白应归一");
    }

    [TestMethod]
    public void StripBareImageMentions_LeavesPlainTextUntouched()
    {
        const string input = "  前面有空格  的普通文本  ";

        Assert.AreEqual(input, AttachmentService.StripBareImageMentions(input, Array.Empty<BareImageMention>()),
            "没有命中项时正文必须原样返回");
    }

    [TestMethod]
    public void CoreOrchestrator_ExtractReferences_ConvertsBareImageToAttachment()
    {
        var orchestrator = new CoreAttachmentOrchestrator(_service, new AllowAllModelCapabilityGate());
        var input = $"{_png} 看看这个图里面有什么";

        var (text, paths) = orchestrator.ExtractReferences(input);

        Assert.AreEqual("看看这个图里面有什么", text, "附加成功的裸路径必须从正文剥离，避免模型再去 ReadFile");
        CollectionAssert.AreEqual(new[] { _png }, paths.ToArray());
    }

    [TestMethod]
    public void CoreOrchestrator_ExtractReferences_MergesAtAndBareWithoutDuplicates()
    {
        var orchestrator = new CoreAttachmentOrchestrator(_service, new AllowAllModelCapabilityGate());
        var input = $"@{_png} 与 {_png} 对比";

        var (text, paths) = orchestrator.ExtractReferences(input);

        CollectionAssert.AreEqual(new[] { _png }, paths.ToArray(), "同一路径不得重复附加");
        Assert.IsFalse(text.Contains(_png, StringComparison.Ordinal));
        StringAssert.Contains(text, "对比");
    }

    [TestMethod]
    public void ParseFileReferences_WithoutReferences_KeepsTextVerbatim()
    {
        // Codex 每轮都会走 ExtractReferences：没有引用时正文/缩进/空白必须一个字符都不动
        const string input = "  第一行  有两个空格\n    indented();\n\n  null  ";

        var paths = _service.ParseFileReferences(input, out var remaining);

        Assert.AreEqual(0, paths.Count);
        Assert.AreEqual(input, remaining, "没有有效引用时正文必须原样返回");
    }

    [TestMethod]
    public void ParseFileReferences_RemovesReferenceAndOnlyTouchesNearbySpaces()
    {
        var input = $"看下 @{_png} 这个  图";

        var paths = _service.ParseFileReferences(input, out var remaining);

        CollectionAssert.AreEqual(new[] { _png }, paths.ToArray());
        Assert.AreEqual("看下 这个  图", remaining, "只归并被移除点两侧的空白，远处空格保持原样");
    }

    [TestMethod]
    public void FindBareImageMentions_IgnoresFencedCodeBlock()
    {
        var input = "```text\n" + _png + "\n```\n" + _png + " 看看";

        var mentions = _service.FindBareImageMentions(input);

        Assert.AreEqual(1, mentions.Count, "围栏代码块里的示例路径不得当作附件");
        Assert.AreEqual(input.LastIndexOf(_png, StringComparison.Ordinal), mentions[0].Index,
            "只有代码块之外的那一处才应命中");
    }

    [TestMethod]
    public void FindBareImageMentions_IgnoresInlineCodeAndAssignment()
    {
        Assert.AreEqual(0, _service.FindBareImageMentions($"看 `{_png}` 这个").Count,
            "行内反引号里的路径不得当作附件");
        Assert.AreEqual(0, _service.FindBareImageMentions("var p = @\"" + _png + "\";").Count,
            "赋值语句里的示例路径不得当作附件");
    }

    [TestMethod]
    public void StripBareImageMentions_PreservesCodeIndentation()
    {
        var input = "if (a)\n{\n    b();\n}\n" + _png + " 看看";

        var mentions = _service.FindBareImageMentions(input);
        Assert.AreEqual(1, mentions.Count);

        var text = AttachmentService.StripBareImageMentions(input, mentions);

        Assert.AreEqual("if (a)\n{\n    b();\n}\n看看", text,
            "剥离裸路径不得压缩正文里别处的缩进空白");
    }

    /// <summary>只认图片扩展名，其余按不支持处理（真实实现见 DefaultAttachmentProcessor）。</summary>
    private sealed class FakeProcessor : IAttachmentProcessor
    {
        public Task<ProcessedAttachment> ProcessAsync(
            string filePath, bool generateThumbnail = false, CancellationToken ct = default)
            => throw new NotSupportedException("本用例只覆盖路径识别，不处理文件内容");

        public bool IsSupported(string filePath)
            => SupportedExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);

        public string ResolveMediaType(string filePath) => "image/png";

        public IReadOnlyList<string> SupportedExtensions { get; } = [".png", ".jpg", ".jpeg"];
    }

    /// <summary>路径识别不依赖 DI；被误用时立即暴露。</summary>
    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => throw new NotSupportedException($"本用例不应解析服务：{serviceType.Name}");
    }
}