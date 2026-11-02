/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： AttachmentServiceLimitUnitTest
*创建时间：2026/10/29
*描述：附件数量上限单测——锁死「正文 @引用 + UI 附件合并后超限」的整轮失败缺陷：
*      AttachmentService.ProcessAsync 原实现在超限时直接抛 InvalidOperationException，
*      而框架层 AgentHost 是按上限截断并下发 attachment.skipped，两层口径不一致，
*      一旦框架上限被调大，产品侧就会先抛，整轮对话直接失败（用户输入已清空、无从补救）。
*      修复口径：产品侧同口径截断，不再抛异常。
*
*****************************************************************************/

using LuBan.DI;
using LuBan.AIAgent.Attachments;
using LubanAgentCore.Services;

using Microsoft.Extensions.AI;

namespace LubanAgent.Tests;

[TestClass]
public class AttachmentServiceLimitUnitTest
{
    /// <summary>记录处理次数并按路径产出最小可用的 <see cref="ProcessedAttachment"/>。</summary>
    private sealed class CountingProcessor : IAttachmentProcessor
    {
        public int Calls { get; private set; }

        public Task<ProcessedAttachment> ProcessAsync(
            string filePath, bool generateThumbnail = false, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ProcessedAttachment
            {
                Info = new AttachmentInfo(Path.GetFileName(filePath), "text/plain", 0, AttachmentKind.TextFile, filePath),
                Content = new TextContent(string.Empty)
            });
        }

        public bool IsSupported(string filePath) => true;

        public string ResolveMediaType(string filePath) => "text/plain";

        public IReadOnlyList<string> SupportedExtensions { get; } = [".txt"];
    }

    /// <summary>上限校验不依赖 DI；被误用时立即暴露。</summary>
    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => throw new NotSupportedException($"本用例不应解析服务：{serviceType.Name}");
    }

    private static string[] Paths(int count)
        => Enumerable.Range(0, count).Select(i => Path.Combine(Path.GetTempPath(), $"a{i}.txt")).ToArray();

    [TestMethod]
    public async Task ProcessAsync_超上限时截断且不抛异常()
    {
        var processor = new CountingProcessor();
        var service = new AttachmentService(processor, new DiContainer(new ThrowingServiceProvider()));

        var result = await service.ProcessAsync(Paths(7));

        Assert.AreEqual(AttachmentService.MaxAttachmentsPerMessage, result.Count, "超限应截断到上限，而不是抛异常");
        Assert.AreEqual(AttachmentService.MaxAttachmentsPerMessage, processor.Calls, "只处理上限内的前 N 个");
        Assert.AreEqual("a0.txt", result[0].Info.FileName, "保留的必须是最先出现的来源");
    }

    [TestMethod]
    public async Task ProcessAsync_恰好等于上限时全部处理()
    {
        var processor = new CountingProcessor();
        var service = new AttachmentService(processor, new DiContainer(new ThrowingServiceProvider()));

        var result = await service.ProcessAsync(Paths(AttachmentService.MaxAttachmentsPerMessage));

        Assert.AreEqual(AttachmentService.MaxAttachmentsPerMessage, result.Count);
        Assert.AreEqual(AttachmentService.MaxAttachmentsPerMessage, processor.Calls);
    }

    [TestMethod]
    public async Task ProcessAsync_未超上限时原样处理()
    {
        var processor = new CountingProcessor();
        var service = new AttachmentService(processor, new DiContainer(new ThrowingServiceProvider()));

        var result = await service.ProcessAsync(Paths(3));

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(3, processor.Calls);
        Assert.AreEqual("a0.txt", result[0].Info.FileName, "顺序必须保持");
    }
}