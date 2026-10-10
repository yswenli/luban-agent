/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Utils
*文件名：  ToolDisplayNames
*版本号： V1.0.0.0
*唯一标识：工具名 → 中文动作名映射
*当前的用户域：WALLE
*创建人：yswenli
*创建时间：2026/9/14
*描述：框架工具方法名到中文动作名的映射，CLI/GUI 双端共用。
*精确匹配优先；未命中时去掉 Async 后缀回退，仍无结果则返回原名。
*
*****************************************************************************/
namespace LubanAgentCore.Utils;

/// <summary>
/// 工具方法名 → 中文动作名映射。
/// 运行中："正在{动作}…"；完成："{动作}完成"；失败："{动作}失败"。
/// </summary>
public static class ToolDisplayNames
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        // FileSystem
        ["ReadFileAsync"] = "读取文件",
        ["WriteFileAsync"] = "写入文件",
        ["ListDirectoryAsync"] = "浏览目录",
        ["GetWorkspaceOverviewAsync"] = "检索工作区",
        ["DeleteFileAsync"] = "删除文件",
        ["DeleteDirectoryAsync"] = "删除目录",
        ["SearchFilesAsync"] = "搜索文件",
        ["GrepAsync"] = "全仓搜索",
        ["CreateDirectoryAsync"] = "创建目录",
        ["CopyFileAsync"] = "复制文件",
        ["MoveFileAsync"] = "移动文件",
        ["GetFileInfoAsync"] = "获取文件信息",
        // FileSystem（Edit）
        ["EditFileAsync"] = "编辑文件",
        ["MultiEditFileAsync"] = "批量编辑",
        // Git
        ["GitStatusAsync"] = "Git 状态",
        ["GitDiffAsync"] = "Git 差异",
        ["GitLogAsync"] = "Git 日志",
        ["GitShowAsync"] = "Git 查看",
        ["GitBranchListAsync"] = "列出分支",
        ["GitRemoteListAsync"] = "列出远端",
        ["GitBlameAsync"] = "Git 追溯",
        ["GitStashListAsync"] = "列出暂存",
        ["GitAddAsync"] = "Git 暂存",
        ["GitCommitAsync"] = "Git 提交",
        ["GitCheckoutAsync"] = "Git 检出",
        ["GitSwitchAsync"] = "Git 切换",
        ["GitBranchCreateAsync"] = "创建分支",
        ["GitCherryPickAsync"] = "Git 拣选",
        ["GitRevertAsync"] = "Git 回滚",
        ["GitRestoreAsync"] = "Git 恢复",
        ["GitMergeAsync"] = "Git 合并",
        ["GitStashAsync"] = "Git 储藏",
        ["GitInitAsync"] = "Git 初始化",
        ["GitCloneAsync"] = "Git 克隆",
        ["GitFetchAsync"] = "Git 拉取远端",
        ["GitPullAsync"] = "Git 拉取",
        ["GitPushAsync"] = "Git 推送",
        ["GitResetAsync"] = "Git 重置",
        ["GitRebaseAsync"] = "Git 变基",
        ["GitCleanAsync"] = "Git 清理",
        // MCP 资源
        ["mcp_list_resources"] = "列出 MCP 资源",
        ["mcp_read_resource"] = "读取 MCP 资源",
        // LocalMemory
        ["SaveAsync"] = "保存记忆",
        ["SearchAsync"] = "搜索记忆",
        ["ListAsync"] = "列出记忆",
        ["DeleteAsync"] = "删除记忆",
        // Browser
        ["NavigateAsync"] = "打开网页",
        ["ClickAsync"] = "点击元素",
        ["TypeTextAsync"] = "输入文本",
        ["ScreenshotAsync"] = "页面截图",
        ["GetContentAsync"] = "获取页面内容",
        ["WaitForSelectorAsync"] = "等待元素",
        ["GetCurrentUrlAsync"] = "获取页面地址",
        // Web
        ["FetchUrlAsync"] = "抓取网页",
        // Script
        ["RunShellAsync"] = "执行命令",
        ["RunLuaAsync"] = "执行 Lua",
        ["RunPythonAsync"] = "执行 Python",
        // Retrieval
        ["IndexDirectoryAsync"] = "索引目录",
        ["IndexContentAsync"] = "索引内容",
        ["SearchCodeAsync"] = "代码检索",
        ["GetIndexStatsAsync"] = "获取索引统计",
        // Orchestration
        ["plan_task"] = "规划任务",
        ["run_orchestration"] = "执行编排",
        // Context
        ["CompactContextAsync"] = "压缩上下文",
    };

    /// <summary>
    /// 返回工具的中文动作名；未命中时回退到去掉 Async 后缀的方法名或原名。
    /// </summary>
    /// <param name="toolName">框架工具方法名（如 GrepAsync）。</param>
    /// <returns>中文动作名（如"全仓搜索"）。</returns>
    public static string GetAction(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return "执行工具";
        if (Map.TryGetValue(toolName, out var action)) return action;
        return toolName.EndsWith("Async", StringComparison.Ordinal)
            ? toolName[..^5]
            : toolName;
    }

    private static readonly Dictionary<string, string> IconMap = new(StringComparer.Ordinal)
    {
        // FileSystem
        ["ReadFileAsync"] = "📄",
        ["WriteFileAsync"] = "✏️",
        ["ListDirectoryAsync"] = "📂",
        ["GetWorkspaceOverviewAsync"] = "🗂️",
        ["DeleteFileAsync"] = "🗑️",
        ["DeleteDirectoryAsync"] = "🗑️",
        ["SearchFilesAsync"] = "🔎",
        ["GrepAsync"] = "🔎",
        ["CreateDirectoryAsync"] = "📁",
        ["CopyFileAsync"] = "📤",
        ["MoveFileAsync"] = "📦",
        ["GetFileInfoAsync"] = "ℹ️",
        // FileSystem（Edit）
        ["EditFileAsync"] = "✏️",
        ["MultiEditFileAsync"] = "🛠️",
        // Git
        ["GitStatusAsync"] = "🌿",
        ["GitDiffAsync"] = "🌿",
        ["GitLogAsync"] = "🌿",
        ["GitShowAsync"] = "🌿",
        ["GitBranchListAsync"] = "🌿",
        ["GitRemoteListAsync"] = "🌿",
        ["GitBlameAsync"] = "🌿",
        ["GitStashListAsync"] = "🌿",
        ["GitAddAsync"] = "🌿",
        ["GitCommitAsync"] = "🌿",
        ["GitCheckoutAsync"] = "🌿",
        ["GitSwitchAsync"] = "🌿",
        ["GitBranchCreateAsync"] = "🌿",
        ["GitCherryPickAsync"] = "🌿",
        ["GitRevertAsync"] = "🌿",
        ["GitRestoreAsync"] = "🌿",
        ["GitMergeAsync"] = "🌿",
        ["GitStashAsync"] = "🌿",
        ["GitInitAsync"] = "🌿",
        ["GitCloneAsync"] = "🌿",
        ["GitFetchAsync"] = "🌿",
        ["GitPullAsync"] = "🌿",
        ["GitPushAsync"] = "🌿",
        ["GitResetAsync"] = "🌿",
        ["GitRebaseAsync"] = "🌿",
        ["GitCleanAsync"] = "🌿",
        // MCP 资源
        ["mcp_list_resources"] = "📦",
        ["mcp_read_resource"] = "📦",
        // LocalMemory
        ["SaveAsync"] = "💾",
        ["SearchAsync"] = "🧠",
        ["ListAsync"] = "📋",
        ["DeleteAsync"] = "🗑️",
        // Browser
        ["NavigateAsync"] = "🌐",
        ["ClickAsync"] = "🖱️",
        ["TypeTextAsync"] = "⌨️",
        ["ScreenshotAsync"] = "📸",
        ["GetContentAsync"] = "📄",
        ["WaitForSelectorAsync"] = "⏱️",
        ["GetCurrentUrlAsync"] = "🔗",
        // Web
        ["FetchUrlAsync"] = "🕷️",
        // Script
        ["RunShellAsync"] = "⚙️",
        ["RunLuaAsync"] = "📜",
        ["RunPythonAsync"] = "🐍",
        // Retrieval
        ["IndexDirectoryAsync"] = "📚",
        ["IndexContentAsync"] = "📝",
        ["SearchCodeAsync"] = "🔍",
        ["GetIndexStatsAsync"] = "📊",
        // Orchestration
        ["plan_task"] = "🧩",
        ["run_orchestration"] = "🚀",
        // Context
        ["CompactContextAsync"] = "📦",
    };

    /// <summary>
    /// 返回工具对应的 emoji 图标；未命中时返回 🔧。
    /// </summary>
    /// <param name="toolName">框架工具方法名（如 GrepAsync）。</param>
    /// <returns>emoji 图标（如"🔎"）。</returns>
    public static string GetIcon(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return "🔧";
        return IconMap.TryGetValue(toolName, out var icon) ? icon : "🔧";
    }
}