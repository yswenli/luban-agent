/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Retrieval
*文件名： EmbeddingModelCatalog
*版本号： V1.0.0.0
*唯一标识：嵌入模型目录
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：嵌入模型目录
*
*****************************************************************************/
namespace LubanAgentCli.Retrieval;

/// <summary>
/// 模型文件规格，描述单个模型文件的远程路径、本地名称和最小文件大小。
/// </summary>
/// <param name="RemotePath">远程下载路径（相对于 RemoteBase/MirrorBase）</param>
/// <param name="LocalName">保存到本地后的相对文件名</param>
/// <param name="MinSizeBytes">文件最小字节数，用于校验下载完整性</param>
public record ModelFileSpec(string RemotePath, string LocalName, long MinSizeBytes);

/// <summary>
/// 嵌入模型规格，定义模型的标识、向量维度、下载地址及所需文件列表。
/// </summary>
/// <param name="ModelId">模型唯一标识</param>
/// <param name="Dimension">向量维度</param>
/// <param name="RemoteBase">HuggingFace 远程基础路径</param>
/// <param name="MirrorBase">国内镜像基础路径</param>
/// <param name="Files">模型所需的文件列表</param>
public record EmbeddingModelSpec(string ModelId, int Dimension, string RemoteBase, string MirrorBase, IReadOnlyList<ModelFileSpec> Files);

/// <summary>
/// 嵌入模型目录
/// </summary>
public static class EmbeddingModelCatalog
{
    /// <summary>
    /// all-MiniLM-L6-v2（384 维，英文模型，默认）
    /// </summary>
    public static readonly EmbeddingModelSpec AllMiniLmL6V2 = new(
        "all-MiniLM-L6-v2", 384,
        "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/",
        "https://hf-mirror.com/sentence-transformers/all-MiniLM-L6-v2/resolve/main/",
        new ModelFileSpec[]
        {
            new("onnx/model.onnx?download=true", "model.onnx", 1),
            new("tokenizer.json", "tokenizer.json", 1),
            new("tokenizer_config.json", "tokenizer_config.json", 1),
        });

    /// <summary>
    /// bge-small-zh-v1.5（384 维，中文+代码混合场景，默认）
    /// </summary>
    public static readonly EmbeddingModelSpec BgeSmallZhV15 = new(
        "bge-small-zh-v1.5", 384,
        "https://huggingface.co/onnx-community/bge-small-zh-v1.5-ONNX/resolve/main/",
        "https://hf-mirror.com/onnx-community/bge-small-zh-v1.5-ONNX/resolve/main/",
        new ModelFileSpec[]
        {
            new("onnx/model.onnx?download=true", "onnx/model.onnx", 1),
            new("onnx/model.onnx_data?download=true", "onnx/model.onnx_data", 1),
            new("tokenizer.json", "tokenizer.json", 1),
            new("tokenizer_config.json", "tokenizer_config.json", 1),
        });

    /// <summary>
    /// 默认模型
    /// </summary>
    public static readonly EmbeddingModelSpec Default = BgeSmallZhV15;

    /// <summary>
    /// 按模型标识查找
    /// </summary>
    public static EmbeddingModelSpec? Find(string modelId)
    {
        if (string.Equals(modelId, AllMiniLmL6V2.ModelId, StringComparison.OrdinalIgnoreCase))
            return AllMiniLmL6V2;
        if (string.Equals(modelId, BgeSmallZhV15.ModelId, StringComparison.OrdinalIgnoreCase))
            return BgeSmallZhV15;
        return null;
    }
}
