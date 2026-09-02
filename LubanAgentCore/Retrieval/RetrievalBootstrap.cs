/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Retrieval
*文件名： RetrievalBootstrap
*版本号： V1.0.0.0
*唯一标识：检索能力引导器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/2
*描述：统一准备检索所需的嵌入模型，供 CLI 与桌面端（Codex）复用
*
*****************************************************************************/
namespace LubanAgentCore.Retrieval;

/// <summary>
/// 检索能力引导器。封装「读取配置 → 定位模型规格 → 就绪检查/解压 → 构建 ONNX 嵌入生成器」这一
/// 宿主无关的流程，使 CLI 与桌面端共用同一份实现，避免两处逻辑漂移。
/// </summary>
public static class RetrievalBootstrap
{
    /// <summary>
    /// 准备嵌入模型（下载/解压进度通过 <paramref name="report"/> 回调报告）。
    /// </summary>
    /// <param name="configuration">应用配置，读取 <c>LuBanAgent:Tools:Retrieval</c> 节。</param>
    /// <param name="report">进度/提示回调。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>
    /// 成功时返回嵌入生成器与模型管理器；检索未启用、模型规格未知或本地模型包缺失时返回
    /// <c>(null, null)</c>，调用方应将检索功能降级关闭（RAG 知识库问答不可用，但不影响其余功能）。
    /// </returns>
    public static async Task<(OnnxEmbeddingGenerator? embedder, ModelManager? modelManager)> PrepareAsync(
        IConfiguration configuration,
        Action<string> report,
        CancellationToken ct = default)
    {
        var retrieval = configuration.GetSection("LuBanAgent:Tools:Retrieval").Get<RetrievalToolOptions>() ?? new RetrievalToolOptions();
        if (!retrieval.Enabled) return (null, null);

        var spec = EmbeddingModelCatalog.Find(retrieval.ModelId);
        if (spec == null)
        {
            report($"未知的嵌入模型：{retrieval.ModelId}，检索功能已禁用");
            return (null, null);
        }

        var mm = new ModelManager(spec);
        if (mm.IsModelReady()) return (new OnnxEmbeddingGenerator(mm.ModelDirectory, spec), mm);

        var ok = await mm.EnsureModelAsync(report, ct);
        if (!ok || !mm.IsModelReady())
        {
            report($"本地嵌入模型 {spec.ModelId} 未就绪，检索功能已禁用");
            report($"请将模型包放到: {mm.LocalZipPath}");
            return (null, null);
        }

        return (new OnnxEmbeddingGenerator(mm.ModelDirectory, spec), mm);
    }
}
