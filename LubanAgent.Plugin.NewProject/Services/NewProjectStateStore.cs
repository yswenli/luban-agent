/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： NewProjectStateStore
*创建时间：2026/10/16
*描述：项目档案持久化（残留检测与孤儿清单展示的依据，PRD §4.7 / M4 决策点 4）
*****************************************************************************/

using System.Text.Json;
using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Serialization;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 项目档案持久化：默认落 <c>%LocalAppData%/LuBan/AIAgent/plugins/state/com.tjces.newproject.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// 与宿主插件状态仓储（<c>plugins/plugin-states.json</c>）同级目录，便于统一清理；
/// 本文件只承载「项目档案」，不参与插件启停状态。
/// </para>
/// <para>
/// 不做断点续跑（PRD §4.8 #15 / §6 #19）：档案的作用是**残留检测**与**远端孤儿清单**展示，
/// 流程推进仍由用户在向导中确认。
/// </para>
/// <para>
/// JSON 读写一律走源生成上下文 <see cref="PluginJsonContext"/>，**不得**使用反射式
/// <c>JsonSerializer</c>（会把插件类型钉进默认 ALC 的静态缓存，导致插件 ALC 无法回收，
/// 详见该上下文注释）。
/// </para>
/// </remarks>
public sealed class NewProjectStateStore
{
    private const string FileName = "com.tjces.newproject.json";

    private readonly string _stateDirectory;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="stateDirectory">
    /// 状态目录；为 <c>null</c> 时使用宿主默认目录（<see cref="DefaultStateDirectory"/>）。
    /// 可显式指定以做隔离验证（避免触达用户真实状态目录）。
    /// </param>
    public NewProjectStateStore(string? stateDirectory = null)
        => _stateDirectory = stateDirectory ?? DefaultStateDirectory;

    /// <summary>
    /// 宿主默认状态目录：<c>%LocalAppData%/LuBan/AIAgent/plugins/state</c>。
    /// </summary>
    public static string DefaultStateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LuBan",
        "AIAgent",
        "plugins",
        "state");

    /// <summary>
    /// 状态文件路径。
    /// </summary>
    public string FilePath => Path.Combine(_stateDirectory, FileName);

    /// <summary>
    /// 读取档案；不存在或损坏时返回 <c>null</c>（不抛异常）。
    /// </summary>
    /// <returns>项目档案。</returns>
    public ProjectSpec? Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize(File.ReadAllText(path), PluginJsonContext.Default.ProjectSpec);
        }
        catch (Exception ex)
        {
            Logger.Warn("[newproject] 读取项目档案失败，按「无在途项目」处理。", ex);
            return null;
        }
    }

    /// <summary>
    /// 写入档案（失败只记日志，不影响向导流程）。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    public void Save(ProjectSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        try
        {
            Directory.CreateDirectory(_stateDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(spec, PluginJsonContext.Default.ProjectSpec));
        }
        catch (Exception ex)
        {
            Logger.Warn("[newproject] 保存项目档案失败。", ex);
        }
    }

    /// <summary>
    /// 清除档案（「中止并清理」在完成本地回收后调用）。
    /// </summary>
    public void Clear()
    {
        try
        {
            var path = FilePath;
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Warn("[newproject] 清除项目档案失败。", ex);
        }
    }
}