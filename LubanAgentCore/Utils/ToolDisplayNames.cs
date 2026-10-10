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
*      运行时工具名经 Microsoft.Extensions.AI 的 AIFunctionFactory 注册后
*      会被剥离 async 方法的 Async 后缀（如 ReadFileAsync → ReadFile），
*      故查表前统一先剥离尾部 Async；未命中则返回原名。
*
*****************************************************************************/
namespace LubanAgentCore.Utils;

/// <summary>
/// 工具方法名 → 中文动作名映射。
/// 运行中："正在{动作}…"；完成："{动作}完成"；失败："{动作}失败"。
/// </summary>
public static class ToolDisplayNames
{
    private const string AsyncSuffix = "Async";

    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        // FileSystem
        ["ReadFile"] = "读取文件",
        ["WriteFile"] = "写入文件",
        ["ListDirectory"] = "浏览目录",
        ["GetWorkspaceOverview"] = "检索工作区",
        ["DeleteFile"] = "删除文件",
        ["DeleteDirectory"] = "删除目录",
        ["SearchFiles"] = "搜索文件",
        ["Grep"] = "全仓搜索",
        ["CreateDirectory"] = "创建目录",
        ["CopyFile"] = "复制文件",
        ["MoveFile"] = "移动文件",
        ["GetFileInfo"] = "获取文件信息",
        // FileSystem（Edit）
        ["EditFile"] = "编辑文件",
        ["MultiEditFile"] = "批量编辑",
        // Git
        ["GitStatus"] = "Git 状态",
        ["GitDiff"] = "Git 差异",
        ["GitLog"] = "Git 日志",
        ["GitShow"] = "Git 查看",
        ["GitBranchList"] = "列出分支",
        ["GitRemoteList"] = "列出远端",
        ["GitBlame"] = "Git 追溯",
        ["GitStashList"] = "列出暂存",
        ["GitAdd"] = "Git 暂存",
        ["GitCommit"] = "Git 提交",
        ["GitCheckout"] = "Git 检出",
        ["GitSwitch"] = "Git 切换",
        ["GitBranchCreate"] = "创建分支",
        ["GitCherryPick"] = "Git 拣选",
        ["GitRevert"] = "Git 回滚",
        ["GitRestore"] = "Git 恢复",
        ["GitMerge"] = "Git 合并",
        ["GitStash"] = "Git 储藏",
        ["GitInit"] = "Git 初始化",
        ["GitClone"] = "Git 克隆",
        ["GitFetch"] = "Git 拉取远端",
        ["GitPull"] = "Git 拉取",
        ["GitPush"] = "Git 推送",
        ["GitReset"] = "Git 重置",
        ["GitRebase"] = "Git 变基",
        ["GitClean"] = "Git 清理",
        // Log
        ["LogStats"] = "日志概览",
        ["LogGrep"] = "日志检索",
        ["LogSlice"] = "日志切片",
        // MCP 资源
        ["mcp_list_resources"] = "列出 MCP 资源",
        ["mcp_read_resource"] = "读取 MCP 资源",
        // LocalMemory
        ["Save"] = "保存记忆",
        ["Search"] = "搜索记忆",
        ["List"] = "列出记忆",
        ["Delete"] = "删除记忆",
        // Browser
        ["Navigate"] = "打开网页",
        ["Click"] = "点击元素",
        ["TypeText"] = "输入文本",
        ["Screenshot"] = "页面截图",
        ["GetContent"] = "获取页面内容",
        ["WaitForSelector"] = "等待元素",
        ["GetCurrentUrl"] = "获取页面地址",
        // Web
        ["FetchUrl"] = "抓取网页",
        // Script
        ["RunShell"] = "执行命令",
        ["RunLua"] = "执行 Lua",
        ["RunPython"] = "执行 Python",
        // Retrieval
        ["IndexDirectory"] = "索引目录",
        ["IndexContent"] = "索引内容",
        ["SearchCode"] = "代码检索",
        ["GetIndexStats"] = "获取索引统计",
        // Orchestration
        ["plan_task"] = "规划任务",
        ["run_orchestration"] = "执行编排",
        // Context
        ["CompactContext"] = "压缩上下文",
    };

    /// <summary>
    /// 返回工具的中文动作名；未命中时回退到去掉 Async 后缀的方法名或原名。
    /// </summary>
    /// <param name="toolName">运行时工具名（如 Grep；兼容带 Async 后缀的方法名如 GrepAsync）。</param>
    /// <returns>中文动作名（如"全仓搜索"）。</returns>
    public static string GetAction(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return "执行工具";
        var normalized = Normalize(toolName);
        return Map.TryGetValue(normalized, out var action) ? action : normalized;
    }

    private static readonly Dictionary<string, string> IconMap = new(StringComparer.Ordinal)
    {
        // FileSystem
        ["ReadFile"] = "📄",
        ["WriteFile"] = "✏️",
        ["ListDirectory"] = "📂",
        ["GetWorkspaceOverview"] = "🗂️",
        ["DeleteFile"] = "🗑️",
        ["DeleteDirectory"] = "🗑️",
        ["SearchFiles"] = "🔎",
        ["Grep"] = "🔎",
        ["CreateDirectory"] = "📁",
        ["CopyFile"] = "📤",
        ["MoveFile"] = "📦",
        ["GetFileInfo"] = "ℹ️",
        // FileSystem（Edit）
        ["EditFile"] = "✏️",
        ["MultiEditFile"] = "🛠️",
        // Git
        ["GitStatus"] = "🌿",
        ["GitDiff"] = "🌿",
        ["GitLog"] = "🌿",
        ["GitShow"] = "🌿",
        ["GitBranchList"] = "🌿",
        ["GitRemoteList"] = "🌿",
        ["GitBlame"] = "🌿",
        ["GitStashList"] = "🌿",
        ["GitAdd"] = "🌿",
        ["GitCommit"] = "🌿",
        ["GitCheckout"] = "🌿",
        ["GitSwitch"] = "🌿",
        ["GitBranchCreate"] = "🌿",
        ["GitCherryPick"] = "🌿",
        ["GitRevert"] = "🌿",
        ["GitRestore"] = "🌿",
        ["GitMerge"] = "🌿",
        ["GitStash"] = "🌿",
        ["GitInit"] = "🌿",
        ["GitClone"] = "🌿",
        ["GitFetch"] = "🌿",
        ["GitPull"] = "🌿",
        ["GitPush"] = "🌿",
        ["GitReset"] = "🌿",
        ["GitRebase"] = "🌿",
        ["GitClean"] = "🌿",
        // Log
        ["LogStats"] = "📜",
        ["LogGrep"] = "📜",
        ["LogSlice"] = "📜",
        // MCP 资源
        ["mcp_list_resources"] = "📦",
        ["mcp_read_resource"] = "📦",
        // LocalMemory
        ["Save"] = "💾",
        ["Search"] = "🧠",
        ["List"] = "📋",
        ["Delete"] = "🗑️",
        // Browser
        ["Navigate"] = "🌐",
        ["Click"] = "🖱️",
        ["TypeText"] = "⌨️",
        ["Screenshot"] = "📸",
        ["GetContent"] = "📄",
        ["WaitForSelector"] = "⏱️",
        ["GetCurrentUrl"] = "🔗",
        // Web
        ["FetchUrl"] = "🕷️",
        // Script
        ["RunShell"] = "⚙️",
        ["RunLua"] = "📜",
        ["RunPython"] = "🐍",
        // Retrieval
        ["IndexDirectory"] = "📚",
        ["IndexContent"] = "📝",
        ["SearchCode"] = "🔍",
        ["GetIndexStats"] = "📊",
        // Orchestration
        ["plan_task"] = "🧩",
        ["run_orchestration"] = "🚀",
        // Context
        ["CompactContext"] = "📦",
    };

    /// <summary>
    /// 返回工具对应的 emoji 图标；未命中时返回 🔧。
    /// </summary>
    /// <param name="toolName">运行时工具名（如 Grep；兼容带 Async 后缀的方法名如 GrepAsync）。</param>
    /// <returns>emoji 图标（如"🔎"）。</returns>
    public static string GetIcon(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return "🔧";
        return IconMap.TryGetValue(Normalize(toolName), out var icon) ? icon : "🔧";
    }

    /// <summary>
    /// 剥离尾部 Async 后缀，使带/不带后缀的工具名都能命中映射表。
    /// </summary>
    private static string Normalize(string toolName)
        => toolName.EndsWith(AsyncSuffix, StringComparison.Ordinal)
            ? toolName[..^AsyncSuffix.Length]
            : toolName;
}
