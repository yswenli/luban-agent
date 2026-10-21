/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： StepResultFile
*创建时间：2026/10/21
*描述：Step 4 / Step 5 助手结论文件的路径约定（M4-P4 定案）
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 助手结论文件的路径约定（M4-P4 定案）。
/// </summary>
/// <remarks>
/// <para>
/// 路径固定为 <c>{项目工作区}\.luban-agent\plugin-newproject\results\step{4,5}-&lt;时间戳&gt;.json</c>。
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

    /// <summary>
    /// 生成一次结论文件的绝对路径（每次执行一个新文件名，便于对照历次尝试）。
    /// </summary>
    /// <param name="workspaceRoot">项目工作区根。</param>
    /// <param name="step">步骤号（4 或 5）。</param>
    /// <returns>结论文件绝对路径。</returns>
    public static string BuildPath(string workspaceRoot, int step)
    {
        var name = $"step{step}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
        return Path.Combine(workspaceRoot, RelativeDirectory, name);
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