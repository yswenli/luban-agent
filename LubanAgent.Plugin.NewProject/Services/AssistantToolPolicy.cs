/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： AssistantToolPolicy
*创建时间：2026/10/21
*描述：路线② 助手的工具审批策略（M4-P4 决策点 D2：白名单过滤，不用 BypassPermissions）
*****************************************************************************/

using System.Text;
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
/// ③越界路径的文件工具；④<b>全部 MCP 工具</b>（<c>MCPToolPlugin</c> 以 <c>path=null</c> 调用确认服务，必然回调）。
/// 因此本策略是「补漏」而非「全量把关」。
/// </para>
/// <para>
/// <b>云效 MCP 子策略</b>：Step 3 / 6 / 7 的云效动作全走 <c>mcp_yunxiao_*</c>，若不加子策略会被末尾
/// <c>Deny</c> 全拒、链路一步走不动。故按<b>工具名段</b>表驱动放行本项目所需的 12 个云效工具
/// （8 只读 + 4 写入），并拒绝 <c>flow_run_pipeline</c>（<c>codeup_clone</c> / <c>codeup_push</c> 已从工具面删除，保留 <c>Deny</c> 双保险）。
/// 匹配不锁死 <c>client</c> 名，连接器实例改名不失效（详见 <see cref="TryMatchYunxiaoMcpTool"/>）。
/// </para>
/// <para>
/// <b>删除放行</b>：<c>DeleteDirectoryAsync</c> 原本无条件拒绝，现改为<b>工作区内按路径放行</b>
/// （Step 3 \"清空后重克隆\" 由助手执行，实际用 <c>RunPythonAsync</c> 的 <c>rmtree(清只读)</c> 兜底，见 <see cref="WorkspaceDeleteTools"/>）。
/// 其余删除 / 技能 / MCP 写删类工具维持无条件拒绝。
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
    /// <para>
    /// <b>v0.28 新增</b> <c>fnm</c> / <c>cnpm</c>：本组织管理端仓库要求 Node 16 + npm 系客户端，
    /// 助手需用 <c>fnm exec --using=16 -- npm.cmd …</c>（<c>fnm</c> 只做 Node 版本切换，本身不改仓库）
    /// 与 <c>cnpm</c>（内网 npm 镜像客户端）完成安装与构建。二者都只是「包管理器 / 版本管理器」，
    /// 不引入新的越界面；<c>fnm env | Out-String | Invoke-Expression</c> 那类管道 + 求值写法仍被拒
    /// （管道分段后 <c>Out-String</c> / <c>Invoke-Expression</c> 非白名单首词，且 <c>Invoke-Expression</c> 等同任意求值）。
    /// </para>
    /// </remarks>
    public static readonly IReadOnlySet<string> AllowedExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet", "git", "node", "npm", "npx", "pnpm", "yarn", "fnm", "cnpm", "python", "py", "python3", "pip", "pip3"
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

    /// <summary>删除类工具名（一律拒绝；<c>DeleteDirectoryAsync</c> 例外见 <see cref="WorkspaceDeleteTools"/>）。</summary>
    private static readonly HashSet<string> DeleteTools = new(StringComparer.Ordinal)
    {
        "DeleteFileAsync", "DeletePageAsync",
        "WriteSkillAsync", "DeleteSkillAsync", "WriteMcpServerAsync", "DeleteMcpServerAsync"
    };

    /// <summary>
    /// 删除类工具中<b>按工作区内路径放行</b>的（C5′：Step 3 \"清空后重克隆\" 由助手执行）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="WorkspaceWriteTools"/> 同判法（<see cref="IsUnderWorkspace"/>）：目标路径落在工作区内即放行，
    /// 越界或未给出路径一律拒绝。单列一张表（而不是并入写类）是为了不让「删除」混进「写入」的语义与依据文案。
    /// <para>
    /// <b>执行机制</b>：框架 <c>FileSystemToolPlugin.DeleteDirectoryAsync</c> 是裸 <c>Directory.Delete(recursive:true)</c>，
    /// 不处理 Windows 只读位（git pack 文件即只读，RC5）。故实际删除由助手改用
    /// <c>RunPythonAsync</c> 的 <c>shutil.rmtree(path, onerror=清只读后重试)</c> 完成；此处的放行是「契约兜底 + 文案一致」。
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string[]> WorkspaceDeleteTools = new(StringComparer.Ordinal)
    {
        ["DeleteDirectoryAsync"] = new[] { "path" }
    };

    /// <summary>MCP 工具名前缀（框架 <c>MCPToolPlugin</c> 的拼法：<c>mcp_{client}_{tool}</c>）。</summary>
    private const string McpToolPrefix = "mcp_";

    /// <summary>
    /// 云效 MCP 只读工具（按<b>工具名段</b>匹配，放行）。连接器实例改名（<c>mcp_yunxiao*_*</c>）不影响判定。
    /// </summary>
    private static readonly HashSet<string> YunxiaoReadOnlyMcpTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "codeup_search_repos", "codeup_list_template_repos", "codeup_get_repo", "codeup_list_groups",
        "codeup_list_branches", "flow_list_pipelines", "flow_list_host_groups", "flow_get_host_group"
    };

    /// <summary>云效 MCP 写入工具（Step 3 / Step 7 的执行动作，放行）。</summary>
    private static readonly HashSet<string> YunxiaoWriteMcpTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "codeup_create_group", "codeup_create_repo", "flow_create_pipeline", "flow_update_pipeline"
    };

    /// <summary>
    /// 云效 MCP 拒绝工具：<c>flow_run_pipeline</c>（建完不自动运行）；
    /// <c>codeup_clone</c> / <c>codeup_push</c> 已从工具面删除（git 操作一律走 git 工具组），此处保留 <c>Deny</c> 作双保险。
    /// </summary>
    private static readonly HashSet<string> YunxiaoDeniedMcpTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "flow_run_pipeline", "codeup_clone", "codeup_push"
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
                reason = "放行：命令每一段的首词都在白名单内（dotnet / git / node / npm / npx / pnpm / yarn / fnm / cnpm / python / pip 等）。";
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

        // ── 云效 MCP 子策略（Step 3/6/7 的云效动作全走 MCP，默认模式下必触发回调）────────
        if (toolName.StartsWith(McpToolPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (TryMatchYunxiaoMcpTool(toolName, out var mcpTool))
            {
                if (YunxiaoDeniedMcpTools.Contains(mcpTool))
                {
                    reason = Equals(mcpTool, "flow_run_pipeline")
                        ? "拒绝：云效工具 flow_run_pipeline 不在本项目白名单内（建完不自动运行）。"
                        : $"拒绝：云效工具 {mcpTool} 已从工具面删除（git 操作一律走 git 工具组）。";
                    return ConfirmResult.Deny;
                }

                if (YunxiaoReadOnlyMcpTools.Contains(mcpTool))
                {
                    reason = $"放行：云效只读工具 {mcpTool}（查证类，无副作用）。";
                    return ConfirmResult.Allow;
                }

                if (YunxiaoWriteMcpTools.Contains(mcpTool))
                {
                    reason = $"放行：云效写入工具 {mcpTool}（Step 3 / Step 7 的执行动作）。";
                    return ConfirmResult.Allow;
                }
            }

            reason = $"拒绝：MCP 工具 {toolName} 不在本项目白名单内（只放行云效 MCP 子策略列举的工具）。";
            return ConfirmResult.Deny;
        }

        // ── 工作区内目录删除（C5′：Step 3 \"清空后重克隆\" 由助手执行）────────────────
        if (WorkspaceDeleteTools.TryGetValue(toolName, out var deletePathKeys))
        {
            foreach (var key in deletePathKeys)
            {
                var path = ReadArgument(arguments, key);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (IsUnderWorkspace(path!, workspaceRoot))
                {
                    reason = "放行：删除类工具的目标路径落在本项目工作区内。";
                    return ConfirmResult.Allow;
                }

                reason = $"拒绝：删除类工具目标路径越出本项目工作区（{path}）。";
                return ConfirmResult.Deny;
            }

            reason = $"拒绝：{toolName} 未给出目标路径，无法确认是否落在工作区内。";
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
    /// 宿主对「工具调用被拒绝 / 被取消」的统一文案。
    /// </summary>
    /// <remarks>
    /// 字面量写在框架 <c>LuBan.AIAgent</c> 的 <c>AgentHostService</c> 里（拒绝与取消在结果层不可区分，故用同一条中性文案），
    /// 插件侧只能按字面量对齐；改框架时两处需同步（CLI 侧亦有一处同样的字面量比对）。
    /// </remarks>
    public const string DeniedOrCancelledMessage = "工具调用被拒绝或已取消";

    /// <summary>
    /// 判定一条工具失败读数是否<b>只</b>代表「守卫拒绝 / 用户取消」，而非真的执行失败。
    /// </summary>
    /// <remarks>
    /// 为什么必须区分：四环判据含「本轮事件流无错误事件」。守卫拒绝（白名单驳回）本身是**控制动作**，
    /// 若也计成错误，助手只要试错一次（换命令、换写法）就被自己的守卫判成整步失败——真机已复现
    /// （第二次尝试被拒 → 即便编译通过也报失败）。拒绝依据由审批回调写进 <c>Denials</c>，不会丢信息。
    /// </remarks>
    /// <param name="error">工具失败读数原文（<see cref="LuBan.AIAgent.Abstractions.ToolCallFailedEvent.Error"/>）。</param>
    /// <returns>是守卫拒绝 / 取消返回 <c>true</c>。</returns>
    public static bool IsGuardDenial(string? error) =>
        !string.IsNullOrWhiteSpace(error) && error.Contains(DeniedOrCancelledMessage, StringComparison.Ordinal);

    /// <summary>
    /// 判定一个 MCP 工具名是否属于本项目用到的云效工具集，并给出<b>工具名段</b>。
    /// </summary>
    /// <remarks>
    /// 框架把工具名拼成 <c>mcp_{client}_{tool}</c>（<c>MCPToolPlugin.cs:113</c>）。<b>不得</b>逐字锁死
    /// <c>mcp_yunxiao_xxx</c>——连接器实例可被改名 / 多实例，逐字匹配会让整张白名单全线失效。
    /// 故这里先剥 <c>mcp_</c> 前缀，再按「工具名段」后缀匹配（要求段前是 <c>_</c>），
    /// <c>{client}</c> 只要求非空，不要求等于 <c>yunxiao</c>。
    /// </remarks>
    /// <param name="toolName">完整工具名（如 <c>mcp_yunxiao_codeup_get_repo</c>）。</param>
    /// <param name="toolSegment">命中的工具名段（如 <c>codeup_get_repo</c>）；未命中为空串。</param>
    /// <returns>命中云效工具集返回 <c>true</c>。</returns>
    public static bool TryMatchYunxiaoMcpTool(string? toolName, out string toolSegment)
    {
        toolSegment = string.Empty;
        if (string.IsNullOrWhiteSpace(toolName)
            || !toolName!.StartsWith(McpToolPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = toolName[McpToolPrefix.Length..];
        if (remainder.Length == 0)
            return false;

        foreach (var candidate in EnumerateYunxiaoMcpTools())
        {
            if (remainder.EndsWith("_" + candidate, StringComparison.OrdinalIgnoreCase))
            {
                toolSegment = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>枚举本项目涉及的云效 MCP 工具名段（放行 + 拒绝全集）。</summary>
    /// <returns>工具名段。</returns>
    private static IEnumerable<string> EnumerateYunxiaoMcpTools()
    {
        foreach (var tool in YunxiaoReadOnlyMcpTools)
            yield return tool;
        foreach (var tool in YunxiaoWriteMcpTools)
            yield return tool;
        foreach (var tool in YunxiaoDeniedMcpTools)
            yield return tool;
    }

    /// <summary>
    /// 判定一条 Shell 命令是否<b>每一段</b>都由白名单可执行文件 / 内置命令打头。
    /// </summary>
    /// <remarks>
    /// 纯函数（供单测锁死口径）：按**未加引号**的 <c>||</c> · <c>&&</c> · <c>;</c> · <c>|</c> · 换行 切段
    /// （引号内的分隔符属参数文本，见 <see cref="SplitSegments"/>），
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

        foreach (var rawSegment in SplitSegments(command!))
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
    /// 按<b>未加引号</b>的分隔符（<c>||</c> · <c>&&</c> · <c>;</c> · <c>|</c> · 换行）切段。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>必须识别成对引号</b>：<c>node -e "const m=require('x'); console.log(m)"</c> 里的 <c>;</c> 是参数文本而不是命令分隔符。
    /// 早先的实现直接按字面量切，后半截 <c>console.log(m)"</c> 被当成可执行文件 → 白名单内的合法命令被整条拒绝
    /// （真机复现：助手想用 <c>node -e</c> 自查依赖入口，被守卫挡下，且该拒绝还被计成「本轮错误」）。
    /// </para>
    /// <para>
    /// 引号规则对齐 pwsh（命令由宿主的 Shell 工具执行）：单引号内除 <c>''</c>（一个字面单引号）外不转义；
    /// 双引号内用反引号转义。未闭合的引号按「到末尾都在引号内」处理——pwsh 对未闭合引号本身就报语法错，
    /// 不存在「引号内藏了半条命令还能被执行」的情形。
    /// </para>
    /// <para>单个 <c>&</c> 不切段（pwsh 的调用运算符，交给 <see cref="ReadHead"/> 处理），只有 <c>&amp;&amp;</c> 才切。</para>
    /// </remarks>
    /// <param name="command">命令原文。</param>
    /// <returns>命令段（含空段，由调用方跳过）。</returns>
    private static List<string> SplitSegments(string command)
    {
        var segments = new List<string>();
        var builder = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++)
        {
            var current = command[i];

            if (inSingleQuote)
            {
                builder.Append(current);
                if (current != '\'')
                    continue;

                // pwsh 单引号串内 '' 表示一个字面单引号，不结束引用
                if (i + 1 < command.Length && command[i + 1] == '\'')
                {
                    builder.Append('\'');
                    i++;
                    continue;
                }

                inSingleQuote = false;
                continue;
            }

            if (inDoubleQuote)
            {
                builder.Append(current);
                if (current == '`' && i + 1 < command.Length)
                {
                    builder.Append(command[i + 1]);
                    i++;
                    continue;
                }

                if (current == '"')
                    inDoubleQuote = false;

                continue;
            }

            switch (current)
            {
                case '\'':
                    inSingleQuote = true;
                    builder.Append(current);
                    continue;

                case '"':
                    inDoubleQuote = true;
                    builder.Append(current);
                    continue;

                case ';':
                case '|':
                case '\r':
                case '\n':
                    segments.Add(builder.ToString());
                    builder.Clear();

                    // || 是一个分隔符，不要把它切成两个空段
                    if (current == '|' && i + 1 < command.Length && command[i + 1] == '|')
                        i++;

                    continue;

                case '&':
                    if (i + 1 < command.Length && command[i + 1] == '&')
                    {
                        segments.Add(builder.ToString());
                        builder.Clear();
                        i++;
                        continue;
                    }

                    builder.Append(current);
                    continue;

                default:
                    builder.Append(current);
                    continue;
            }
        }

        segments.Add(builder.ToString());
        return segments;
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