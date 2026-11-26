/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Hosting
*文件名： CompactionChatClientFactory
*描述：摘要专用聊天客户端：按配置的模型另建客户端，未配置或不可用时回落到会话模型
*
*****************************************************************************/

namespace LubanAgentCore.Hosting;

/// <summary>
/// 摘要使用的聊天客户端及其上下文窗口。
/// </summary>
/// <param name="Client">聊天客户端。</param>
/// <param name="ContextWindow">该模型的上下文窗口（token）；未知或沿用会话模型时为 <c>null</c>，只按会话阈值收敛。</param>
public readonly record struct CompactionChatClient(IChatClient Client, int? ContextWindow);

/// <summary>
/// 为上下文摘要提供聊天客户端。
/// </summary>
public interface ICompactionChatClientFactory
{
    /// <summary>
    /// 取本次摘要使用的客户端。
    /// </summary>
    /// <returns>配置了独立摘要模型且可用时返回该模型，否则返回会话模型。</returns>
    CompactionChatClient Create();
}

/// <summary>
/// 按 <see cref="CompactionOptions.SummarizerModel"/> 解析摘要客户端。
/// </summary>
/// <remarks>
/// 与自动命名（<c>SessionAutoNamer</c>）同口径：按限定名经 <see cref="IProviderRouter"/> 另建客户端，
/// 这样摘要不会继承会话模型的思考强度与会话级参数。
/// 摘要属压缩链路里可放弃的一步：模型不可用时只告警并回落，不向调用方抛出。
/// </remarks>
public sealed class CompactionChatClientFactory(
    IProviderRouter router,
    IChatClient sessionClient,
    CompactionOptions options,
    IModelContextWindowLookup windows) : ICompactionChatClientFactory
{
    /// <inheritdoc />
    public CompactionChatClient Create()
    {
        var model = options.SummarizerModel?.Trim();
        if (string.IsNullOrEmpty(model))
        {
            return new CompactionChatClient(sessionClient, null);
        }

        try
        {
            // 摘要输入按会话阈值收敛后，还要按摘要模型自己的窗口再压一次
            return new CompactionChatClient(router.CreateChatClient(model), windows.GetContextWindow(model));
        }
        catch (Exception ex)
        {
            // 供应商被删 / 模型名失效属配置问题，不是服务故障：回落会话模型，压缩照常进行
            Logger.Warn($"上下文压缩：摘要模型不可用，改用会话模型。model={model}, error={ex.Message}");
            return new CompactionChatClient(sessionClient, null);
        }
    }
}