/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： AssistantToolPolicy
*创建时间：2026/10/21
*描述：路线② 助手的工具审批策略（M4-P4 决策点 D2：白名单过滤，不用 BypassPermissions）
*****************************************************************************/

using LuBan.AIAgent.Abstractions;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 插件驱动助手跑一轮提示词时的<b>工具审批白名单</b>（M4-P4 决策点 D2）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：路线② 下 Step 4 / Step 5 的实际动作由项目会话的助手执行。插件声明里的
/// <c>process:dotnet</c> 只管插件自身发起的进程，<b>管不到助手执行的命令</b>；故由插件在
/// 宿主 <c>AgentHostService.RunStreamingAsync</c> 的确认回调里按「工具名 + 命令行首词」补上等效约束，
/// 且只用 <see cref="ToolPermissionMode.Default"/>（不使用 <c>BypassPermissions</c> 全放行）。
/// </para>
/// <para>
/// <b>何时被回调</b>：Default 模式下工作区内的路径读写由框架直接放行（不回调），回调只发生在
/// ①删除 / 技能写删 / MCP 写删类工具；②无路径语义的脚本工具（<c>RunShellAsync</c> / <c>RunPythonAsync</c> / <c>RunLuaAsync</c>）；
/// ③越界路径的文件工具。因此本策略是「补漏」而非「全量把关」。
/// </para>
/// <para>
/// <b>已知残余风险（如实记录）</b>：<c>runpython</c> 被放行（Step 4 探活依赖 pymysql，框架内没有 MySQL 工具），
/// 而 Python 脚本本身可执行任意操作——本策略对脚本内容不做沙箱约束，只保证「删除 / 技能 / MCP / 越界写入」被拒。
/// 该风险与用户定案一致：Step 4/5 的执行形态是「助手亲自执行」。
/// </para>
/// </remarks>
public static class AssistantToolPolicy
{
    /// <summary>脚本工具：Python（探活 / 写结论文件的唯一通道）。</summary>
    public const string RunPythonTool = "RunPythonAsync";

    /// <summary>脚本工具：Shell（编译命令走这里）。</summary>
    public const string RunShellTool = "RunShellAsync";

    /// <summary>脚本工具：Lua（本链路不用）。</summary>
    public const string RunLuaTool = "RunLuaAsync";

    /// <summary>
    /// 允许执行的程序（命令首词，忽略大小写、路径与 <c>.exe</c> / <c>.cmd</c> / <c>.bat</c> / <c>.ps1</c> 后缀）。
    /// </summary>
    /// <remarks>
    /// 覆盖三端编译所需：.NET（<c>dotnet</c>）、Vite/Vue 前端（<c>node</c> / <c>npm</c> / <c>npx</c> / <c>pnpm</c> / <c>yarn</c>）、
    /// 依赖安装（<c>pip</c> / <c>pip3</c>）、脚本兜底（<c>python</c> / <c>py</c> / <c>python3</c>）与只读检查（<c>git</c>）。
    /// </remarks>
    public static readonly IReadOnlySet<string> AllowedExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet", "git", "node", "npm", "npx", "pnpm", "yarn", "python", "py", "python3", "pip", "pip3"
    };

    /// <summary>允许的 Shell 内置（pwsh 切目录用，无副作用）。</summary>
    private static readonly HashSet<string> AllowedShellBuiltins = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd", "set-location", "sl", "push-location", "pop-location"
    };

    /// <summary>无路径语义的只读工具：直接放行（不产生副作用，且能减少助手被驳回后的空转）。</summary>
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.Ordinal)
    {
        "ReadFileAsync", "ListDirectoryAsync", "GetWorkspaceOverviewAsync",
        "SearchFilesAsync", "GrepAsync", "GetFileInfoAsync",
        "ListSkillsAsync", "UseSkillAsync"
    };

    /// <summary>写类文件工具：仅当目标路径落在本项目工作区内才放行（越界即拒）。</summary>
    private static readonly Dictionary<string, string[]> WorkspaceWriteTools = new(StringComparer.Ordinal)
    {
        ["WriteFileAsync"] = new[] { "path" },
        ["CreateDirectoryAsync"] = new[] { "path" },
        ["EditFileAsync"] = new[] { "path" },
        ["MultiEditFileAsync"] = new[] { "path" },
        ["CopyFileAsync"] = new[] { "destPath" },
        ["MoveFileAsync"] = new[] { "destPath" }
    };

    /// <summary>删除类工具名（本项目 Step 4/5 从不删除，一律拒绝）。</summary>
    private static readonly HashSet<string> DeleteTools = new(StringComparer.Ordinal)
    {
        "DeleteFileAsync", "DeleteDirectoryAsync", "DeletePageAsync",
        "WriteSkillAsync", "DeleteSkillAsync", "WriteMcpServerAsync", "DeleteMcpServerAsync"
    };

    /// <summary>
    /// 对一次工具调用给出审批结论。
    /// </summary>
    /// <param name="toolName">工具名。</param>
    /// <param name="arguments">工具参数（可为 <c>null</c>）。</param>
    /// <param name="workspaceRoot">本项目工作区根（用于判定写类工具是否越界）。</param>
    /// <param name="reason">审批依据（无论放行或拒绝都会写入操作记录，供用户复核）。</param>
    /// <returns>确认结论。</returns>
    public static ConfirmResult Decide(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments,
        string? workspaceRoot,
        out string reason)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            reason = "工具名为空，拒绝。";
            return ConfirmResult.Deny;
        }

        if (Equals(toolName, RunPythonTool))
        {
            reason = "放行：runpython（白名单可执行文件 python，Step 4 探活与写结论文件的唯一通道）。";
            return ConfirmResult.Allow;
        }

        if (Equals(toolName, RunShellTool))
        {
            var command = ReadArgument(arguments, "command");
            if (IsAllowedShellCommand(command, out var offender))
            {
                reason = "放行：命令每一段的首词都在白名单内（dotnet / git / node / npm / npx / pnpm / yarn / python / pip 等）。";
                return ConfirmResult.Allow;
            }

            reason = $"拒绝：命令包含白名单外的可执行文件「{offender}」。";
            return ConfirmResult.Deny;
        }

        if (Equals(toolName, RunLuaTool))
        {
            reason = "拒绝：脚本工具 runlua 不在 Step 4/5 白名单内。";
            return ConfirmResult.Deny;
        }

        if (DeleteTools.Contains(toolName))
        {
            reason = $"拒绝：{toolName} 属删除 / 技能 / MCP 写删类工具，Step 4/5 不需要也不会执行。";
            return ConfirmResult.Deny;
        }

        if (ReadOnlyTools.Contains(toolName))
        {
            reason = "放行：只读工具。";
            return ConfirmResult.Allow;
        }

        if (WorkspaceWriteTools.TryGetValue(toolName, out var pathKeys))
        {
            foreach (var key in pathKeys)
            {
                var path = ReadArgument(arguments, key);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (IsUnderWorkspace(path!, workspaceRoot))
                {
                    reason = "放行：写类工具的目标路径落在本项目工作区内。";
                    return ConfirmResult.Allow;
                }

                reason = $"拒绝：写类工具目标路径越出本项目工作区（{path}）。";
                return ConfirmResult.Deny;
            }

            reason = $"拒绝：{toolName} 未给出目标路径，无法确认是否落在工作区内。";
            return ConfirmResult.Deny;
        }

        reason = $"拒绝：{toolName} 不在 Step 4/5 白名单内（本链路只允许只读工具、白名单脚本与工作区内写入）。";
        return ConfirmResult.Deny;
    }

    /// <summary>
    /// 判定一条 Shell 命令是否<b>每一段</b>都由白名单可执行文件 / 内置命令打头。
    /// </summary>
    /// <remarks>
    /// 纯函数（供单测锁死口径）：按 <c>||</c> · <c>&&</c> · <c>;</c> · <c>|</c> · 换行 切段，
    /// 每段取首个词（支持引号路径，去引号、去目录、去可执行后缀）判定；环境变量赋值段（<c>$env:X = ...</c>）视为无副作用放行。
    /// 复合语句里只要有一段越界，整条命令即拒——避免「用无害命令做前缀、真实动作藏在后半段」。
    /// </remarks>
    /// <param name="command">命令原文。</param>
    /// <param name="offender">首个越界的可执行文件名（判定通过时为空串）。</param>
    /// <returns>全部在白名单内返回 <c>true</c>。</returns>
    public static bool IsAllowedShellCommand(string? command, out string offender)
    {
        offender = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            offender = "(空命令)";
            return false;
        }

        foreach (var rawSegment in System.Text.RegularExpressions.Regex.Split(command!, @"\|\||&&|[;|\r\n]"))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0)
                continue;

            // 环境变量赋值（$env:FOO = "bar" / $FOO = 1）：无副作用，放行
            if (segment.StartsWith('$') && segment.Contains('='))
                continue;

            var head = ReadHead(segment);

            if (head.Length == 0)
                continue;

            var name = StripExecutableArtifacts(head);
            if (AllowedExecutables.Contains(name) || AllowedShellBuiltins.Contains(name))
                continue;

            offender = head;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 取一段命令的首词（程序名）。
    /// </summary>
    /// <remarks>
    /// 支持三种常见写法：<c>dotnet build</c> · <c>&amp; "dotnet" build</c>（pwsh 调用运算符）·
    /// <c>"C:\Program Files\dotnet\dotnet.exe" build</c>（带空格的引号路径）。
    /// </remarks>
    /// <param name="segment">命令段。</param>
    /// <returns>首词；取不到返回空串。</returns>
    private static string ReadHead(string segment)
    {
        var text = segment.TrimStart();
        if (text.Length == 0)
            return string.Empty;

        // pwsh 调用运算符：& "prog" arg
        if (text.StartsWith('&'))
            text = text[1..].TrimStart();

        if (text.Length > 0 && (text[0] == '"' || text[0] == '\''))
        {
            var quote = text[0];
            var end = text.IndexOf(quote, 1);
            return (end > 1 ? text[1..end] : text[1..]).Trim();
        }

        var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[0].Trim('"', '\'');
    }

    /// <summary>去目录 + 去可执行后缀，得到命令首词的程序名。</summary>
    /// <param name="head">命令首词。</param>
    /// <returns>程序名。</returns>
    private static string StripExecutableArtifacts(string head)
    {
        var name = head;
        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0)
            name = name[(slash + 1)..];

        foreach (var extension in new[] { ".exe", ".cmd", ".bat", ".ps1", ".com" })
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^extension.Length];
                break;
            }
        }

        return name;
    }

    /// <summary>判定路径是否落在工作区内（绝对化后按前缀比较，避免盘符 / 分隔符写法差异误判）。</summary>
    /// <param name="path">待判定路径。</param>
    /// <param name="workspaceRoot">工作区根。</param>
    /// <returns>落在工作区内返回 <c>true</c>。</returns>
    private static bool IsUnderWorkspace(string path, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return false;

        try
        {
            var root = Path.GetFullPath(workspaceRoot!)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var target = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return target.Equals(root, StringComparison.OrdinalIgnoreCase)
                   || target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>从工具参数里取字符串（框架传入的实参可能是 <c>JsonElement</c>）。</summary>
    /// <param name="arguments">参数表。</param>
    /// <param name="key">键名。</param>
    /// <returns>字符串值；取不到为 <c>null</c>。</returns>
    private static string? ReadArgument(IReadOnlyDictionary<string, object?>? arguments, string key)
    {
        if (arguments is null || !arguments.TryGetValue(key, out var value) || value is null)
            return null;

        return value switch
        {
            string text => text,
            System.Text.Json.JsonElement element => element.ValueKind == System.Text.Json.JsonValueKind.String
                ? element.GetString()
                : element.ToString(),
            _ => value.ToString()
        };
    }
}