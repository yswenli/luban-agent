/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Retrieval
*文件名： OnnxEmbeddingGenerator
*版本号： V1.0.0.0
*唯一标识：ONNX 嵌入生成器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：ONNX 嵌入生成器（本地推理）
*
*****************************************************************************/
namespace LubanAgentCli.Retrieval;

/// <summary>
/// ONNX 嵌入生成器（本地推理）
/// </summary>
public class OnnxEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>, IDisposable
{
    private const int MaxTokens = 512;
    private readonly string _modelDir;
    private readonly EmbeddingModelSpec _spec;
    private readonly object _initLock = new();
    private volatile InferenceSession? _session;
    private volatile Tokenizer? _tokenizer;

    /// <summary>
    /// 创建 ONNX 嵌入生成器
    /// </summary>
    public OnnxEmbeddingGenerator(string modelDir, EmbeddingModelSpec spec)
    {
        _modelDir = modelDir;
        _spec = spec;
    }

    // 延迟加载 ONNX 推理会话和分词器，使用双重检查锁定确保线程安全
    private (InferenceSession session, Tokenizer tokenizer) EnsureLoaded()
    {
        if (_session != null && _tokenizer != null) return (_session, _tokenizer);
        lock (_initLock)
        {
            if (_tokenizer == null)
            {
                var tokenizerPath = Path.Combine(_modelDir, "tokenizer.json");
                if (!File.Exists(tokenizerPath))
                    throw new FileNotFoundException($"tokenizer.json 不存在于 {tokenizerPath}");
                
                // Microsoft.ML.Tokenizers 2.0+ 使用 Create 方法替代 Load
                var bertTokenizerType = typeof(Tokenizer).Assembly.GetType("Microsoft.ML.Tokenizers.BertTokenizer");
                if (bertTokenizerType == null)
                    throw new NotSupportedException("Microsoft.ML.Tokenizers 版本不支持 BertTokenizer");
                
                var createMethod = bertTokenizerType.GetMethod("Create", new[] { typeof(string), typeof(BertOptions) });
                if (createMethod != null)
                {
                    var options = new BertOptions();
                    var configPath = Path.Combine(_modelDir, "tokenizer_config.json");
                    if (File.Exists(configPath))
                    {
                        try
                        {
                            var configText = File.ReadAllText(configPath);
                            var config = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(configText);
                            if (config.TryGetProperty("do_lower_case", out var doLowerProp))
                                options.LowerCaseBeforeTokenization = doLowerProp.GetBoolean();
                            if (config.TryGetProperty("unk_token", out var unkProp))
                                options.UnknownToken = unkProp.GetString() ?? "[UNK]";
                            if (config.TryGetProperty("strip_accents", out var stripProp) && stripProp.ValueKind != System.Text.Json.JsonValueKind.Null)
                                options.RemoveNonSpacingMarks = stripProp.GetBoolean();
                            if (config.TryGetProperty("tokenize_chinese_chars", out var cjkProp))
                                options.IndividuallyTokenizeCjk = cjkProp.GetBoolean();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"读取 tokenizer_config.json 失败: {ex.Message}");
                        }
                    }
                    _tokenizer = createMethod.Invoke(null, new object[] { tokenizerPath, options }) as Tokenizer
                        ?? throw new InvalidOperationException("BertTokenizer.Create 返回 null");
                }
                else
                {
                    // 回退到旧 API: BertTokenizer.Load(string)
                    var loadMethod = bertTokenizerType.GetMethod("Load", new[] { typeof(string) });
                    if (loadMethod == null)
                        throw new NotSupportedException("BertTokenizer 不支持 Create 或 Load 方法，请检查 Microsoft.ML.Tokenizers 版本");
                    _tokenizer = loadMethod.Invoke(null, new object[] { tokenizerPath }) as Tokenizer
                        ?? throw new InvalidOperationException("BertTokenizer.Load 返回 null");
                }
            }
            _session ??= new InferenceSession(Path.Combine(_modelDir, "model.onnx"));
            return (_session, _tokenizer);
        }
    }

    /// <inheritdoc />
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var (session, tokenizer) = EnsureLoaded();
        var texts = values.ToList();
        var result = new GeneratedEmbeddings<Embedding<float>>();

            // 按批次处理，每批最多 32 条文本
            foreach (var batch in texts.Chunk(32))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 分词并截断到最大 token 长度
                var encoded = batch.Select(t =>
                {
                    var ids = tokenizer.EncodeToIds(t);
                    if (ids.Count == 0)
                        return (IReadOnlyList<int>)new List<int> { 0 };
                    if (ids.Count > MaxTokens)
                        ids = ids.Take(MaxTokens - 1).Append(ids[^1]).ToList();
                    return (IReadOnlyList<int>)ids;
                }).ToList();

                // 构建 padded 输入张量
                int maxLen = encoded.Max(e => e.Count);
                var inputIds = new long[batch.Length * maxLen];
                var attention = new long[batch.Length * maxLen];
                var tokenTypes = new long[batch.Length * maxLen];
                for (int i = 0; i < batch.Length; i++)
                    for (int j = 0; j < encoded[i].Count; j++)
                    {
                        inputIds[i * maxLen + j] = encoded[i][j];
                        attention[i * maxLen + j] = 1;
                    }
                var dims = new[] { batch.Length, maxLen };
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, dims)),
                    NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attention, dims)),
                };
                if (session.InputMetadata.ContainsKey("token_type_ids"))
                    inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tokenTypes, dims)));

                // 执行 ONNX 推理
                using var outputs = session.Run(inputs);
                var outputName = session.OutputNames.Count > 0 ? session.OutputNames[0] : null;
                var hidden = outputs.FirstOrDefault(o => o.Name == outputName)?.AsTensor<float>();
                if (hidden == null)
                    throw new InvalidOperationException($"模型输出 '{outputName}' 不存在或类型不匹配");
                int hiddenDim = hidden.Dimensions[2];
                // 对每个 token 的 hidden states 做 mean pooling，然后 L2 归一化
                for (int i = 0; i < batch.Length; i++)
                {
                    int len = encoded[i].Count;
                    var vec = new float[hiddenDim];
                    for (int j = 0; j < len; j++)
                        for (int d = 0; d < hiddenDim; d++)
                            vec[d] += hidden[i, j, d];
                    for (int d = 0; d < hiddenDim; d++) vec[d] /= len;
                    Normalize(vec);
                    result.Add(new Embedding<float>(vec));
                }
            }
        return Task.FromResult(result);
    }

    // L2 归一化，使向量成为单位向量
    private static void Normalize(float[] v)
    {
        double norm = 0;
        foreach (var x in v) norm += x * x;
        norm = Math.Sqrt(norm);
        if (norm > 0) for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(EmbeddingGeneratorMetadata))
            return new EmbeddingGeneratorMetadata(_spec.ModelId, null, null, _spec.Dimension);
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _session?.Dispose();
        if (_tokenizer is IDisposable disposable)
            disposable.Dispose();
    }
}
