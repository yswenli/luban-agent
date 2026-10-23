/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： StepResultFile
*创建时间：2026/10/21
*描述：Step 3 / 4 / 5 / 6 / 7 助手结论文件的路径约定（M4-P4 定案，v0.33 扩到 3/6/7）
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 助手结论文件的路径约定（M4-P4 定案）。
/// </summary>
/// <remarks>
/// <para>
/// 路径固定为 <c>{项目工作区}\.luban-agent\plugin-newproject\results\step{3,4,5,6,7}-&lt;时间戳&gt;.json</c>。
/// </para>
/// <para>
/// <b>为什么放在工作区内</b>：①助手写它时命中「工作区内路径免确认」，不会因越界被工具审批拒绝；
/// ②它与各端 git 仓库同级、<b>不在任何 git 仓库里</b>，Step 6 推送不会把它带进业务仓库；
/// ③落盘在项目目录里，用户随时可自查（结论文件本身就是本步的证据）。
/// </para>
/// </remarks>
public static class StepResultFile
{
    /// <summary>结论文件相对项目工作区的目录。</summary>
    public static string RelativeDirectory => Path.Combine(".luban-agent", "plugin-newproject", "results");

    /// <summary>结论文件所在目录的绝对路径。</summary>
    /// <param name="workspaceRoot">项目工作区根。</param>
    /// <returns>绝对目录。</returns>
    public static string DirectoryOf(string workspaceRoot) => Path.Combine(workspaceRoot, RelativeDirectory);

    /// <summary>
    /// 生成一次结论文件的绝对路径（每次执行一个新文件名，便于对照历次尝试）。
    /// </summary>
    /// <param name="workspaceRoot">项目工作区根。</param>
    /// <param name="step">步骤号（3 / 4 / 5 / 6 / 7）。</param>
    /// <returns>结论文件绝对路径。</returns>
    public static string BuildPath(string workspaceRoot, int step)
    {
        var name = $"step{step}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
        return Path.Combine(workspaceRoot, RelativeDirectory, name);
    }

    /// <summary>
    /// 按修改时间倒序列出某步的历次结论文件（最新在前）。
    /// </summary>
    /// <param name="workspaceRoot">项目工作区根。</param>
    /// <param name="step">步骤号。</param>
    /// <returns>绝对路径列表；目录不存在时为空。</returns>
    public static IReadOnlyList<string> List(string workspaceRoot, int step)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return Array.Empty<string>();

        var dir = DirectoryOf(workspaceRoot);
        if (!System.IO.Directory.Exists(dir))
            return Array.Empty<string>();

        return System.IO.Directory
            .GetFiles(dir, $"step{step}-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(one => one, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>取某步最近一份结论文件的绝对路径（无则 <c>null</c>）。</summary>
    /// <param name="workspaceRoot">项目工作区根。</param>
    /// <param name="step">步骤号。</param>
    /// <returns>绝对路径；无则 <c>null</c>。</returns>
    public static string? FindLatest(string workspaceRoot, int step)
        => List(workspaceRoot, step).FirstOrDefault();

    /// <summary>
    /// 作废（删除）某步最近一份结论文件（§6.6 Step 3 重跑联动）。
    /// </summary>
    /// <param name="workspaceRoot">项目工作区根。</param>
    /// <param name="step">步骤号。</param>
    /// <returns>确实删除了一份返回 <c>true</c>。</returns>
    public static bool TryDeleteLatest(string workspaceRoot, int step)
    {
        var latest = FindLatest(workspaceRoot, step);
        if (latest is null)
            return false;

        try
        {
            File.Delete(latest);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 确保结论文件所在目录存在。
    /// </summary>
    /// <param name="resultFile">结论文件路径。</param>
    public static void EnsureDirectory(string resultFile)
    {
        var directory = Path.GetDirectoryName(resultFile);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory!);
    }
}