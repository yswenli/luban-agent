/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Retrieval
*文件名： ModelManager
*版本号： V1.0.0.0
*唯一标识：模型管理器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：模型管理器
*
*****************************************************************************/
namespace LubanAgentCli.Retrieval;

/// <summary>
/// 嵌入模型管理器（从本地 zip 解压）
/// </summary>
public class ModelManager
{
    private readonly EmbeddingModelSpec _spec;

    /// <summary>
    /// 创建模型管理器
    /// </summary>
    public ModelManager(EmbeddingModelSpec spec) => _spec = spec;

    /// <summary>
    /// 模型规格
    /// </summary>
    public EmbeddingModelSpec Spec => _spec;

    /// <summary>
    /// 模型目录
    /// </summary>
    public string ModelDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LuBan", "AIAgent", "models", _spec.ModelId);

    /// <summary>
    /// 本地 zip 路径（运行目录下 EmbeddingModels/{modelId}.zip）
    /// </summary>
    public string LocalZipPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EmbeddingModels", $"{_spec.ModelId}.zip");

    /// <summary>
    /// 检查模型是否就绪
    /// </summary>
    public bool IsModelReady()
    {
        var dir = ModelDirectory;
        return _spec.Files.All(f => File.Exists(Path.Combine(dir, f.LocalName)));
    }

    /// <summary>
    /// 确保模型存在（从本地 zip 解压）
    /// </summary>
    public Task<bool> EnsureModelAsync(Action<string>? reportStatus = null, CancellationToken cancellationToken = default)
    {
        if (IsModelReady()) return Task.FromResult(true);

        if (!File.Exists(LocalZipPath))
        {
            reportStatus?.Invoke($"本地模型包不存在: {LocalZipPath}");
            return Task.FromResult(false);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "LuBanModel_" + Guid.NewGuid().ToString("N"));
        try
        {
            reportStatus?.Invoke($"正在解压模型到 {ModelDirectory}…");
            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(LocalZipPath, tempDir);
            Directory.CreateDirectory(ModelDirectory);
            foreach (var file in _spec.Files)
            {
                var fileName = Path.GetFileName(file.LocalName);
                var source = Directory.EnumerateFiles(tempDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (source == null)
                {
                    reportStatus?.Invoke($"模型包中缺少文件: {fileName}");
                    return Task.FromResult(false);
                }
                var target = Path.Combine(ModelDirectory, file.LocalName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
            reportStatus?.Invoke("解压完成");
            return Task.FromResult(IsModelReady());
        }
        catch (Exception ex)
        {
            Logger.Error("嵌入模型准备失败", ex);
            reportStatus?.Invoke($"解压失败: {ex.Message}");
            return Task.FromResult(false);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
