/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebFileLink
*描述：工作区文件 → 浏览器可直接打开的绝对地址（纯逻辑，便于单测）
*
*****************************************************************************/

namespace LubanAgentWebHost.Services;

/// <summary>
/// 工作区文件链接的拼装与路径校验。
/// </summary>
/// <remarks>
/// <para>
/// 模型只知道工作区根目录，不知道宿主监听地址，也猜不到 <c>/file/read</c> 路由，
/// 过去只能自造 <c>file:///D:/...</c> 或裸路径——网页里点不开。这里把「工作区内文件」
/// 统一转换成 <c>http://&lt;宿主地址&gt;/file/read?workspaceId=...&amp;path=...</c>。
/// </para>
/// <para>
/// 只回传工作区内真实存在的文件：工作区外的路径即便存在，文件接口也会拒绝，
/// 给出链接只会让用户点到 404，不如直接说明「无法预览」。
/// </para>
/// </remarks>
public static class WebFileLink
{
    /// <summary>
    /// 拼出宿主文件接口的绝对地址。
    /// </summary>
    /// <param name="baseAddress">宿主基地址，形如 <c>http://192.168.3.215:18080</c>。</param>
    /// <param name="workspaceId">工作区标识；为空时链接不带该参数（文件接口回落当前工作区）。</param>
    /// <param name="rootPath">工作区根目录。</param>
    /// <param name="path">模型给出的文件路径：相对工作区根，或工作区内绝对路径。</param>
    /// <param name="link">成功时的绝对地址。</param>
    /// <param name="relativePath">成功时文件相对工作区根的路径（正斜杠），用于回显给模型。</param>
    /// <param name="error">失败时的中文说明，可直接回给模型。</param>
    /// <returns>成功返回 true。</returns>
    public static bool TryBuild(
        string? baseAddress,
        string? workspaceId,
        string? rootPath,
        string? path,
        out string link,
        out string relativePath,
        out string error)
    {
        link = string.Empty;
        relativePath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(baseAddress))
        {
            error = "宿主地址不可用，无法生成文件链接。";
            return false;
        }

        var root = NormalizeRoot(rootPath);
        if (root is null)
        {
            error = "当前会话没有可用的工作区根目录，无法生成文件链接。";
            return false;
        }

        var input = path?.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "path 不能为空：请传入工作区内的文件路径。";
            return false;
        }

        if (!TryResolveFile(root, input, out var fullPath))
        {
            error = "工作区内找不到这个文件，或它不在当前工作区内；工作区外的文件无法生成可预览链接。";
            return false;
        }

        var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var query = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            query.Append("workspaceId=").Append(Uri.EscapeDataString(workspaceId.Trim())).Append('&');
        }
        query.Append("path=").Append(Uri.EscapeDataString(relative));

        relativePath = relative;
        link = $"{baseAddress.TrimEnd('/')}/file/read?{query}";
        return true;
    }

    /// <summary>把输入路径解析为工作区内的真实文件。</summary>
    /// <param name="root">已归一化的工作区根目录。</param>
    /// <param name="path">相对路径或工作区内绝对路径。</param>
    /// <param name="fullPath">命中时的绝对路径。</param>
    /// <returns>命中工作区内真实文件时返回 true。</returns>
    private static bool TryResolveFile(string root, string path, out string fullPath)
    {
        fullPath = string.Empty;

        if (!Path.IsPathFullyQualified(path))
        {
            // 相对路径复用文件接口的解析口径（./ 前缀、查询串、wiki/ 回落、百分号转义）
            return WebFilePath.TryResolve(root, path, out fullPath);
        }

        string absolute;
        try
        {
            absolute = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!WebFilePath.IsWithin(root, absolute))
        {
            return false;
        }

        // 绝对路径转成相对路径后再走同一套校验：符号链接/junction 逃逸同样被拦下
        return WebFilePath.TryResolve(root, Path.GetRelativePath(root, absolute), out fullPath);
    }

    /// <summary>归一化工作区根目录：去引号、取绝对路径、去结尾分隔符。</summary>
    /// <param name="rootPath">原始根路径。</param>
    /// <returns>归一化后的根路径；无效时返回 null。</returns>
    private static string? NormalizeRoot(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        try
        {
            var root = Path.GetFullPath(rootPath.Trim().Trim('"', '\''));
            var rootLength = Path.GetPathRoot(root)?.Length ?? 0;
            return root.Length > rootLength
                ? root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : root;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}