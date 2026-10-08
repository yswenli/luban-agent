/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Infrastructure
*文件名： RequestDumpChatClient
*版本号： V1.0.0.0
*唯一标识：LLM 请求失败转储装饰器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/22
*描述：包装原始 IChatClient，在调用失败（含网络超时）时将完整请求（消息、选项、异常）转储为 JSON 文件，
*       用于诊断网关 500/超时等无法从日志回溯入参的问题。必须包裹在 ResilientChatClient 之内，
*       保证每次重试的原始异常都能被记录，且不改变上层容错包装的幂等判断。
*
*****************************************************************************/
using LuBan.Logging;

namespace LubanAgentCore.Infrastructure;

/// <summary>
/// LLM 请求失败转储装饰器：非流式与流式调用失败时，将本次请求的完整消息列表、
/// 关键选项与异常详情写入 <c>logs/llm-dump-*.json</c>，随后原样抛出异常。
/// 转储自身失败只记警告，绝不掩盖原始异常；仅宿主主动取消（Esc）不转储，网络超时仍会转储。
/// </summary>
public sealed class RequestDumpChatClient : IChatClient
{
    private readonly IChatClient _inner;

    /// <summary>
    /// 创建转储装饰器。
    /// </summary>
    /// <param name="inner">被包装的原始聊天客户端（不带容错层）。</param>
    public RequestDumpChatClient(IChatClient inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        try
        {
            return await _inner.GetResponseAsync(list, options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Dump(list, options, ex, "unary");
            throw;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        var completed = false;
        Exception? failure = null;

        await using (var enumerator = _inner
            .GetStreamingResponseAsync(list, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken))
        {
            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        completed = true;
                        break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    break;
                }

                yield return enumerator.Current;
            }
        }

        if (!completed && failure != null)
        {
            Dump(list, options, failure, "stream");
            throw failure;
        }
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? key = null)
        => _inner.GetService(serviceType, key);

    /// <inheritdoc/>
    public void Dispose()
    {
        _inner.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 将失败的请求转储为 JSON 文件；任何转储异常仅记警告，不影响原始异常传播。
    /// </summary>
    /// <param name="messages">本次请求的完整消息列表。</param>
    /// <param name="options">本次请求的聊天选项。</param>
    /// <param name="exception">导致失败的原始异常。</param>
    /// <param name="mode">调用模式（unary/stream）。</param>
    private static void Dump(IList<ChatMessage> messages, ChatOptions? options, Exception exception, string mode)
    {
        try
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"llm-dump-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");

            var payload = new
            {
                time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                mode,
                options = DescribeOptions(options),
                error = new
                {
                    type = exception.GetType().FullName,
                    message = exception.Message,
                    detail = exception.ToString()
                },
                messages = messages.Select(m => new
                {
                    role = m.Role.Value,
                    contents = m.Contents.Select(DescribeContent).ToArray()
                }).ToArray()
            };

            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            Logger.Warn($"LLM 请求失败，已转储至 {path}");
        }
        catch (Exception dumpEx)
        {
            Logger.Warn("LLM 请求转储写入失败", dumpEx);
        }
    }

    /// <summary>
    /// 提取转储所需的关键选项字段，避免序列化工具函数等复杂对象。
    /// </summary>
    /// <param name="options">聊天选项，可为 null。</param>
    private static object? DescribeOptions(ChatOptions? options)
    {
        if (options == null)
            return null;

        return new
        {
            options.ModelId,
            options.Temperature,
            options.MaxOutputTokens,
            options.TopP,
            options.ToolMode,
            options.AllowMultipleToolCalls,
            toolNames = options.Tools?.Select(t => t.Name).ToArray()
        };
    }

    /// <summary>
    /// 将消息内容转换为可 JSON 序列化的摘要对象；二进制附件只记录元数据不落原始字节。
    /// </summary>
    /// <param name="content">消息内容项。</param>
    private static object DescribeContent(AIContent content)
    {
        if (content is TextContent text)
            return new { type = "text", text = text.Text };

        if (content is FunctionCallContent call)
            return new { type = "function_call", name = call.Name, arguments = call.Arguments?.ToString() };

        if (content is FunctionResultContent result)
            return new { type = "function_result", callId = result.CallId, result = result.Result?.ToString() };

        if (content is DataContent data)
            return new { type = "data", mediaType = data.MediaType, bytes = data.Data.Length };

        if (content is UriContent uri)
            return new { type = "uri", uri = uri.Uri?.ToString() };

        return new { type = content.GetType().Name, value = content.ToString() };
    }
}
