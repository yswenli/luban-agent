/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Retrieval
*文件名： ModelManager
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：模型管理器
*
*****************************************************************************/
namespace LubanAgentCore.Retrieval;

/// <summary>
/// 嵌入模型管理器：优先用本地 zip 解压；本地缺包且配置了下载地址时先下载到数据目录再解压。
/// </summary>
public class ModelManager
{
    /// <summary>下载超时：50 MB 量级模型包，弱网下留足余量。</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

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
    public string ModelDirectory => Path.Combine(AgentHome.ModelsDirectory, _spec.ModelId);

    /// <summary>
    /// 程序目录下的 zip 路径（运行目录下 EmbeddingModels/{modelId}.zip），随发布体积提供
    /// </summary>
    public string LocalZipPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EmbeddingModels", $"{_spec.ModelId}.zip");

    /// <summary>
    /// 数据目录下的 zip 路径（{数据根}/models/{modelId}.zip）：下载的包落在这里，容器重建无需重下
    /// </summary>
    public string DataZipPath => Path.Combine(AgentHome.ModelsDirectory, $"{_spec.ModelId}.zip");

    /// <summary>
    /// zip 候选路径，按优先级排列：数据目录 → 程序目录
    /// </summary>
    public IReadOnlyList<string> ZipPathCandidates => [DataZipPath, LocalZipPath];

    /// <summary>
    /// 检查模型是否就绪
    /// </summary>
    public bool IsModelReady()
    {
        var dir = ModelDirectory;
        return _spec.Files.All(f => File.Exists(Path.Combine(dir, f.LocalName)));
    }

    /// <summary>
    /// 确保模型存在：已就绪直接返回；否则定位本地 zip，缺失且允许下载时先下载，再解压。
    /// </summary>
    /// <param name="reportStatus">进度/提示回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="downloadUrl">模型 zip 下载地址；为空表示不下载。</param>
    /// <param name="autoDownload">是否允许下载缺失的模型包。</param>
    public async Task<bool> EnsureModelAsync(
        Action<string>? reportStatus = null,
        CancellationToken cancellationToken = default,
        string? downloadUrl = null,
        bool autoDownload = true)
    {
        if (IsModelReady()) return true;

        var zipPath = ZipPathCandidates.FirstOrDefault(File.Exists);
        if (zipPath == null && autoDownload && !string.IsNullOrWhiteSpace(downloadUrl))
        {
            zipPath = await DownloadZipAsync(downloadUrl!.Trim(), reportStatus, cancellationToken).ConfigureAwait(false);
        }

        if (zipPath == null)
        {
            reportStatus?.Invoke($"本地模型包不存在: {string.Join(" 或 ", ZipPathCandidates)}");
            return false;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "LuBanModel_" + Guid.NewGuid().ToString("N"));
        try
        {
            reportStatus?.Invoke($"正在解压模型到 {ModelDirectory}…");
            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(zipPath, tempDir);
            Directory.CreateDirectory(ModelDirectory);
            foreach (var file in _spec.Files)
            {
                var fileName = Path.GetFileName(file.LocalName);
                var source = Directory.EnumerateFiles(tempDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (source == null)
                {
                    reportStatus?.Invoke($"模型包中缺少文件: {fileName}");
                    return false;
                }
                var target = Path.Combine(ModelDirectory, file.LocalName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
            reportStatus?.Invoke("解压完成");
            return IsModelReady();
        }
        catch (Exception ex)
        {
            Logger.Error("嵌入模型准备失败", ex);
            reportStatus?.Invoke($"解压失败: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 下载模型 zip 到数据目录。先写 <c>.download</c> 临时文件，成功后改名，
    /// 避免半截包在下次启动被当作可用包解压。
    /// </summary>
    /// <param name="url">模型 zip 下载地址。</param>
    /// <param name="reportStatus">进度/提示回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载完成的 zip 路径；失败返回 <c>null</c>（由调用方降级关闭检索）。</returns>
    private async Task<string?> DownloadZipAsync(string url, Action<string>? reportStatus, CancellationToken cancellationToken)
    {
        var target = DataZipPath;
        var temp = target + ".download";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            reportStatus?.Invoke($"正在下载嵌入模型包: {url}");
            using var http = new HttpClient { Timeout = DownloadTimeout };
            using var response = await http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var dest = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
            }

            var length = new FileInfo(temp).Length;
            if (length == 0)
            {
                reportStatus?.Invoke($"下载的模型包为空，已丢弃: {url}");
                return null;
            }

            File.Move(temp, target, overwrite: true);
            reportStatus?.Invoke($"模型包下载完成（{length / 1024d / 1024d:F1} MB）: {target}");
            return target;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("嵌入模型下载失败", ex, url);
            reportStatus?.Invoke($"模型下载失败: {ex.Message}");
            return null;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
