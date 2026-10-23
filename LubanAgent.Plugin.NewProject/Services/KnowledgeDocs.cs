/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： KnowledgeDocs
*创建时间：2026/10/23
*描述：插件包 docs/ 知识正文 → 项目工作区的同步器（方案 A：每步下发前自愈）
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 知识正文同步器：把插件包内的 <c>docs/</c> 覆盖式铺装到项目工作区。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么单独成类</b>：知识正文是各步提示词的<b>组织专有口径唯一来源</b>。此前只在 Step 2 复制一次，
/// 工作区被外部清理后后续各步仍照常下发，助手读不到文档只能凭记忆执行（真机 Step 3 因此「零写动作 + 回 DONE」）。
/// 现在改为每步下发前由 <see cref="Sync"/> 自愈，且<b>不可用即判失败、不下发</b>。
/// </para>
/// <para>
/// <b>覆盖式、不删除</b>：同名文件以插件包为准（升级即生效），用户手工补充的文件保留（§4.2）。
/// </para>
/// </remarks>
public static class KnowledgeDocs
{
    /// <summary>插件包内知识正文的目录名。</summary>
    public const string PackageFolder = "docs";

    /// <summary>知识正文相对项目工作区的目录（与 <c>StepPrompts.DocsDirectory</c> 同一约定）。</summary>
    public static string RelativeDirectory => Path.Combine(".luban-agent", "plugin-newproject", PackageFolder);

    /// <summary>知识正文所在目录的绝对路径。</summary>
    /// <param name="workspaceRoot">项目工作区根。</param>
    /// <returns>绝对目录。</returns>
    public static string DirectoryOf(string workspaceRoot) => Path.Combine(workspaceRoot, RelativeDirectory);

    /// <summary>
    /// 把插件包 <c>docs/</c> 覆盖式同步到项目工作区。
    /// </summary>
    /// <param name="packageRoot">插件包根目录（含 <c>docs/</c>）；为空视为不可用。</param>
    /// <param name="workspaceRoot">项目工作区根；为空视为不可用。</param>
    /// <returns>是否成功 + 复制份数 + 目标目录 + 失败原因。</returns>
    public static (bool Ok, int Count, string Target, string? Error) Sync(string? packageRoot, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return (false, 0, string.Empty, "项目工作区目录为空。");

        var target = DirectoryOf(workspaceRoot);

        if (string.IsNullOrWhiteSpace(packageRoot))
            return (false, 0, target, "未找到插件包目录（无法定位 docs 知识正文）。");

        var source = Path.Combine(packageRoot, PackageFolder);
        if (!Directory.Exists(source))
            return (false, 0, target, $"插件包内缺少 {PackageFolder} 知识正文目录：{source}");

        var copied = 0;
        try
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var destination = Path.Combine(target, relative);
                var folder = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                File.Copy(file, destination, overwrite: true);
                copied++;
            }
        }
        catch (Exception ex)
        {
            return (false, copied, target, ex.Message);
        }

        if (copied == 0)
            return (false, 0, target, $"插件包 {source} 下没有任何知识正文文件。");

        return (true, copied, target, null);
    }
}