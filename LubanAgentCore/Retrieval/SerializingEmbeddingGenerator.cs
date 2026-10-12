/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Retrieval
*文件名： SerializingEmbeddingGenerator
*版本号： V1.0.0.0
*唯一标识：嵌入生成串行装饰器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/12
*描述：把并发嵌入请求串行化，保护单例 ONNX 会话；供检索服务 scoped 化后共享底层单例嵌入器
*
*****************************************************************************/
namespace LubanAgentCore.Retrieval;

/// <summary>
/// 嵌入生成器串行装饰器：内部以信号量串行执行 <see cref="GenerateAsync"/>，
/// 使底层单例嵌入器（ONNX 会话）在多会话并发时不被同时调用。
/// 装饰器自身为单例，随底层嵌入器一同生存。
/// </summary>
public sealed class SerializingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>, IDisposable
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _inner;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 创建串行装饰器。
    /// </summary>
    /// <param name="inner">被装饰的底层嵌入生成器（单例）。</param>
    public SerializingEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>> inner)
        => _inner = inner;

    /// <inheritdoc />
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await _inner.GenerateAsync(values, options, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType.IsInstanceOfType(this)) return this;
        return _inner.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
        if (_inner is IDisposable disposable) disposable.Dispose();
    }
}