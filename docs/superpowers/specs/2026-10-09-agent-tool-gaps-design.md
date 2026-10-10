# Agent 工具缺口补齐设计（Edit / Git / MCP 资源 / Web fetch）

**日期**: 2026-10-09 | **状态**: 设计完成，待审批

---

## 1. 背景与目标

对照 Claude Code 的工具能力清单，当前 `luban-agent`（CLI 与桌面端共用 `LubanAgentCore` → 同一 `AgentHostBuilder`，工具集完全相同）存在四个能力缺口，另有历史遗留空目录待清理。工具实现全部落在框架 `LuBan.AIAgent`，agent 仅通过 NuGet 包（`LuBan.*` 2026.x.x）依赖，改框架源码需重新打包发布才对 agent 生效。

现状盘点（10 个工具组，两端一致）：`filesystem`、`script`、`browser`、`web`、`retrieval`、`localmemory`、`wiki`（opt-in）、`context`、`orchestration`、`mcp`。

目标：在**不破坏现有工具与确认/权限语义**的前提下，补齐 Edit、Git、MCP 资源读取、Web fetch 增强四项能力，并清理空目录。

---

## 2. 决策记录

| # | 问题 | 选择 | 说明 |
|---|------|------|------|
| Q1 | Edit 工具语义 | **精确字符串替换** | `old_string`→`new_string`，唯一匹配，提供 `replace_all`；对齐 Claude Code，对模型最直观、最安全 |
| Q2 | 是否提供 MultiEdit | **Edit + MultiEdit** | 同一文件多处批量替换，原子性：任一失败则整体不写入 |
| Q3 | `old_string` 空值 | **不支持创建文件** | 空值直接报错，创建文件仍走 `WriteFileAsync`，保持语义单一 |
| Q4 | Git 工具粒度 | **方案 A：细粒度多工具** | 每个 git 子命令一个 AIFunction，确认门控可按工具名精确分类 |
| Q5 | Git 工具范围 | **完整对齐 Claude Code（本轮不含 PR）** | 覆盖 status/diff/log/show/branch/add/commit/checkout/switch/cherry-pick/revert/restore/reset/merge/rebase/stash/init/clone/fetch/pull/push 等 |
| Q6 | PR / gh CLI | **本轮不做** | 本机未安装 gh，PR 相关工具不纳入本轮，避免硬依赖 |
| Q7 | Git 组启用策略 | **默认启用 + 确认门控** | `IsOptIn=false`，与 filesystem 一致；写操作由 `ToolConfirmationService` 按权限模式管控 |
| Q8 | MCP 资源暴露 | **新增两个通用工具** | `mcp_list_resources` + `mcp_read_resource`，先列后读；接口已存在，无需改客户端 |
| Q9 | Web fetch 增强落位 | **先在 LuBan.Common 做基础能力，再落工具** | 引入 ReverseMarkdown，新增 `MarkdownUtil.HtmlToMarkdown`，再在 WebToolGroup 消费 |
| Q10 | HTML→Markdown 库 | **ReverseMarkdown** | 轻量、无额外依赖，复用 LuBan.Common 已内置的 HtmlAgilityPack 解析 |
| Q11 | Web 默认格式 | **markdown** | `format` 参数支持 raw/text/markdown，默认 markdown，失败降级链 markdown→text→raw |
| Q12 | 空目录清理 | **删除 `Tools\Database`、`Tools\Redis`** | 两目录存在但为空，非项目源码 |
| Q13 | 文档与注释 | **文件头注释块 + 中文 XML 注释；中英文 README 成对更新** | 沿用框架既有规范 |
| Q14 | Git 确认语义 | **工作区内免确认；仅合并冲突时结果回传** | `path` 传实际仓库工作目录：落在工作区内即免确认（Default/Plan 一致）；仅当执行后检测到合并冲突，才返回"需人工介入"结果并交回 agent 收尾，见 3.2 |
| Q15 | Git 超时 | **暴露 `timeoutMs` 参数** | 每个 Git 工具增可选 `int? timeoutMs`，缺省取 `GitToolOptions.DefaultTimeout`（与 Script 对齐 120000） |
| Q16 | Web Content-Type 获取 | **新增 Common API `GetResponseAsync`** | 现 `GetBytesAsync` 丢弃响应头，无法判 Content-Type；在 `HttpClientProxy` 增返回「字节 + ContentType」的方法，复用其 gzip 解压与 ApiCallLog |

---

## 3. 架构设计

### 3.1 缺口1：Edit / MultiEdit（`filesystem` 组）

**落点**：`LuBan.AIAgent\Tools\FileSystem\FileSystemToolPlugin.cs`，在 `FileSystemToolGroup` 新增两个方法，复用现有 `ConfirmOrBlockAsync(toolName, path, args)` 确认门控（工作区内免确认、越界确认、Plan 记录计划、拒绝返回 Denied）。

| 方法 | 签名 | 行为 |
|------|------|------|
| `EditFileAsync` | `Task<ToolResult<string>> EditFileAsync(string path, string old_string, string new_string, bool replace_all = false)` | 精确字符串替换 |
| `MultiEditFileAsync` | `Task<ToolResult<string>> MultiEditFileAsync(string path, IReadOnlyList<EditOperation> edits)` | 同一文件多处批量替换（原子） |

**`EditFileAsync` 规则**：

1. `path` 目录 → 复用 `DirectoryPathMessage` 提示；文件不存在 → 报错提示先用 `ReadFileAsync` 核对。
2. `old_string` 为空 → 报错：`old_string 不能为空；如需创建/覆盖整个文件请用 WriteFile`。
3. 匹配次数为 0 → 报错：`未找到匹配内容，请用 ReadFile 核对原文（注意空白/换行/缩进）`。
4. 匹配次数 > 1 且 `replace_all=false` → 报错：`匹配到 N 处，请扩大 old_string 上下文或设置 replace_all=true`。
5. 成功：替换后一次性写回；返回替换处上下文（前后各若干行）+ 替换计数。

**`MultiEditFileAsync` 规则**：按顺序在内存中逐条应用；任一条 `old_string` 为空/未匹配/歧义则**整体不写**并返回失败（含第几条失败）；全部通过后一次性 `File.WriteAllTextAsync`。

**模型**：新增 `EditOperation` 为 **class**（非 record），带公开可写属性 `{ string OldString; string NewString; bool ReplaceAll; }`，供 `AIFunctionFactory` 的 JSON schema 反序列化（`MultiEditFileAsync(IReadOnlyList<EditOperation>)` 需可绑定数组元素）；放 `FileSystem` 命名空间下。**落地前必须先用一个最小 AIFunction 验证复杂参数绑定**（对象数组）能被模型正确填充，若 `AIFunctionFactory` 对该形态支持不佳，退回「单次 Edit 由模型重复调用」而不做 MultiEdit。

**确认归类**：两者均为「有路径、非删除」的编辑类，`AcceptEdits` 模式自动放行（走 `ConfirmOrBlockAsync` 现有分支，无需改确认服务默认集合）。

**注册**：`FileSystemToolPlugin.GetTools` 增加两个 `AIFunctionFactoryHelper.Create(...)`（不传 `workspaceRootProvider`，与读写类工具一致，不做路径兜底）。

**工具名映射**：`ToolDisplayNames` 增 `EditFileAsync`→"编辑文件"、`MultiEditFileAsync`→"批量编辑"，图标 `✏️`/`🛠️`。

### 3.2 缺口2：Git 工具组（`Tools\Git\GitToolPlugin.cs`）

**新增插件** `GitToolPlugin`：`GroupName="git"`，`IsOptIn=false`（默认启用），`IsEnabled(o) => o.Tools.Git.Enabled`。

**执行底座**：复用 `ProcessRunner`（参考 `ScriptToolGroup.RunShellAsync`，`ScriptToolPlugin.cs:135-178`），以 `git` 为可执行文件、参数用 `ArgumentList` 传递，工作目录取工具参数 `workingDirectory`，缺省取 `LuBanAgentOptions.WorkspaceRoot`。每个工具暴露可选 `int? timeoutMs`，缺省取 `GitToolOptions.DefaultTimeout`（120000，与 Script 对齐）。

**工具清单（细粒度，方案 A）**：

| 类别 | 工具 | 确认语义 |
|------|------|----------|
| 只读 | `GitStatusAsync`、`GitDiffAsync`、`GitLogAsync`、`GitShowAsync`、`GitBranchListAsync`、`GitRemoteListAsync`、`GitBlameAsync`、`GitStashListAsync` | Plan 模式放行（入 `ReadOnlyTools`） |
| 写 | `GitAddAsync`、`GitCommitAsync`、`GitCheckoutAsync`、`GitSwitchAsync`、`GitBranchCreateAsync`、`GitCherryPickAsync`、`GitRevertAsync`、`GitRestoreAsync`、`GitMergeAsync`、`GitStashAsync`、`GitInitAsync`、`GitCloneAsync`、`GitFetchAsync`、`GitPullAsync`、`GitPushAsync`、`GitResetAsync`、`GitRebaseAsync`、`GitCleanAsync` | 工作区内免确认；越界确认；Plan 记计划 |

**确认语义（Q14，与文件系统路径语义一致）**：
- 所有 Git 工具调用前 `IToolConfirmationService.EvaluateAsync(toolName, path: <实际仓库工作目录>, args)`。
- `path` 落在工作区内 → `IsAuthorizedPath` 命中 → Default/AcceptEdits 免确认；越界（`workingDirectory` 指向工作区外仓库）→ 逐次确认。
- Plan 模式：入 `ReadOnlyTools` 的只读工具放行，写工具记 `OnPlannedAction` 返回 `ToolResult.Plan`。
- **不把任何 Git 工具加入 `AlwaysConfirmTools`**（工作区内一律免确认，与 filesystem 写操作一致）。
- `DefaultReadOnlyTools` 增只读 Git 工具名（仅 Plan 分支生效）。

**返回结构**：统一 `ToolResult<string>`，输出含 `exitCode / stdout / stderr / commandLine`（参考 `RunShellAsync` 的匿名对象 JSON）。

**合并冲突处理（Q14，C-2 结果回传，交回 agent）**：
- 适用于可能产生冲突的操作：`GitMergeAsync`、`GitPullAsync`、`GitRebaseAsync`、`GitCherryPickAsync`、`GitRevertAsync`、`GitStashAsync`（pop/apply）。
- 命令执行后检测冲突：`git ls-files -u` 输出非空，或 `git status --porcelain` 含 `UU/AA/DD/AU/UA/DU/UD` 等未合并标记（以退出码 + 状态双判）。
- 命中冲突 → 返回**失败态** `ToolResult`，`Message` 明确指示："检测到合并冲突，请手动解决以下文件后告知我继续"，附冲突文件清单与当前 git 状态；**代理不自动解决、不自动 `add`/`--continue`**。
- 用户手动解决并在下一轮对话确认后，**agent 自行决定下一步**（重新调用 `GitStatusAsync` 复核、`GitAddAsync` 暂存、`GitCommitAsync`/`--continue` 收尾），工具本身不代劳。
- 无冲突的正常路径照常返回命令输出。

**配置**：`ToolGroupOptions` 增 `Git`；新增 `GitToolOptions { bool Enabled = true; int DefaultTimeout = 120000; }`（放 `Configuration`）；`LuBanAgentExtensions.AddLuBanAgent` 注册 `GitToolPlugin`。

**工具名映射**：`ToolDisplayNames` 增各 Git 工具中文名与图标（如 `GitStatusAsync`→"Git 状态"、`GitDiffAsync`→"Git 差异"、`GitCommitAsync`→"Git 提交" 等，图标 `🌿`）。

**子代理边界（G）**：CLI `appsettings.json` 的 `Orchestration.DefaultToolGroups=["filesystem"]` 不含 `git`，故编排子代理默认拿不到 Git 工具；如需在子代理使用 Git，须显式调整该配置。本轮不改默认值，仅在文档标注。

**技能协同**：现有 `GitCommitSkill`（纯提示词）保留，可与 `GitDiffAsync`/`GitCommitAsync` 配合；不改其逻辑。

### 3.3 缺口3：MCP 资源暴露（`MCPToolPlugin` 扩展）

接口 `IMCPClient.ListResourcesAsync`/`ReadResourceAsync` 已在 `HttpMCPClient`/`StdioMCPClient`/`MCPClientBase` 实现，`MCPToolPlugin` 未暴露。

**新增两个 AIFunction**（`GroupName="mcp"` 内，命名保持 `mcp_` 前缀）：

| 工具 | 参数 | 行为 |
|------|------|------|
| `mcp_list_resources` | `client?`（可选，缺省遍历全部已连接客户端） | 输出 `client / uri / name / mimeType` 列表（`IEnumerable<MCPResource>`，见 `IMCPClient.cs:131`） |
| `mcp_read_resource` | `client`、`uri` | 调 `ReadResourceAsync` 返回内容（`MCPResourceContent{Uri,Content,MimeType}`，见 `IMCPClient.cs:157`） |

**实现方式**：在 `MCPToolPlugin` 内新增轻量包装（可直接用 `AIFunctionFactory.Create` 包一个匿名委托，或仿 `MCPToolAIFunction` 建一个资源函数类）。两者均经 `IToolConfirmationService` 门控；归类为只读（Plan 放行）。未连接/找不到 client/uri → 返回可操作错误信息。

**工具名映射（H）**：`ToolDisplayNames` 增 `mcp_list_resources`→"列出 MCP 资源"、`mcp_read_resource`→"读取 MCP 资源"，图标 `📦`。命名保持 `mcp_` 前缀（与 `mcp_{client}_{tool}` 一致）。

**配置**：无需改客户端；`MCPToolPlugin.IsEnabled` 保持 `true`（由 `MCPRegistry` 是否为空决定有无工具）。

### 3.4 缺口4：Web fetch 增强

**第一步（LuBan.Common 基础能力）**：
- `LuBan.Common.csproj` 增 `ReverseMarkdown` NuGet。
- 新增 `MarkdownUtil`（或扩展 `HtmlUtil`）：`public static string HtmlToMarkdown(this string html, string? baseUrl = null)`，基于 ReverseMarkdown（内部用 HtmlAgilityPack 解析），失败抛出由调用方降级。
- **`HttpClientProxy` 新增响应方法（Q16，A1）**：现有 `GetBytesAsync` 内部已调用 `SendAsync` 拿到 `HttpResponseMessage`，但只 `ReadAsByteArrayAsync` 后丢弃响应头。新增
  `public async Task<(byte[] Bytes, string? ContentType)> GetResponseAsync(string resource, Dictionary<string, string>? headers = null, int timeout = 180)`，
  复用其 gzip 手动解压逻辑（该 handler 未开 `AutomaticDecompression`，见 `HttpClientProxy.cs:367-376`）与 `ApiCallLog` 记录，仅额外返回 `resMsg.Content.Headers.ContentType?.MediaType`。不改动现有 `GetBytesAsync` 行为。
- 同步 README（中英）说明新增能力。

**第二步（WebToolGroup 消费）**：
- `FetchUrlAsync` 增参数 `string? format = null`（`raw`/`text`/`markdown`）。
- `WebToolOptions` 增 `string DefaultFormat = "markdown"`。
- 处理链：改用 `GetResponseAsync` 取「字节 + ContentType」；仅当 `ContentType` 为 `text/html`（或 `application/xhtml+xml`）才做转换。`markdown`→调 `HtmlToMarkdown`，失败降级 `text`（`GetTextFromHtml`），再失败回 `raw`；截断仍按 `MaxCharacters`。
- 非 HTML（`Content-Type` 非 html）直接返回原文，不做转换。

### 3.5 缺口6：空目录清理

删除 `LuBan.AIAgent\Tools\Database`、`LuBan.AIAgent\Tools\Redis`（均无文件）。

### 3.6 分层改动汇总

| 层 | 新增/改动 |
|----|-----------|
| 框架 `LuBan.AIAgent` | `FileSystemToolGroup` +2 方法 + `EditOperation`；新增 `Tools\Git\GitToolPlugin.cs` + `GitToolOptions`；`MCPToolPlugin` +2 资源工具；`WebToolGroup.FetchUrlAsync` 增 format；`WebToolOptions` 增 DefaultFormat；`ToolGroupOptions` 增 Git；`ToolConfirmationService.DefaultReadOnlyTools` 增只读 Git 工具；`LuBanAgentExtensions` 注册 GitToolPlugin |
| 框架 `LuBan.Common` | 引 ReverseMarkdown；新增 `MarkdownUtil.HtmlToMarkdown`；`HttpClientProxy` 增 `GetResponseAsync` |
| 框架 README（中英成对，E） | `LuBan.AIAgent/README.md` 与 `README.en.md`：工具组表加 `git` 行、计数「10 大内置工具组」改 11、文件树加 `Tools/Git/`；`LuBan.Common/README.md` 与 `README.en.md` 说明 Markdown 能力 |
| Agent | `ToolDisplayNames` 增 Edit/Git/资源工具中文名与图标；`appsettings.json`（CLI/Codex）视需要补 `Tools:Git`；README 中英同步 |
| 清理 | 删 `Tools\Database`、`Tools\Redis` |

---

## 4. 验收要点

- 构建：`dotnet build luban-framework/LuBan.FrameWork.sln` 与 `dotnet build luban-agent/luban-agent.slnx` 通过。
- 单测（MSTest，`LuBan.UnitTestProject.csproj`）：Edit 精确替换/多匹配报错/`replace_all`/MultiEdit 原子性；Web `HtmlToMarkdown` 基础转换；`GetResponseAsync` 返回 ContentType；确认服务默认只读集合含新 Git 工具。
- 手动冒烟（CLI）：Edit 改文件、Git status/diff/commit、构造一次合并冲突验证「结果回传交回 agent」链路、MCP 列/读资源、Web 抓网页转 markdown。

## 5. 风险与边界

- ReverseMarkdown 转换质量对复杂页面不保证，已设降级链；非 HTML 内容不转换。
- Git 工具执行依赖系统 `git`；未安装时返回可操作错误，不崩溃。
- **确认语义边界（B 修正）**：Q14 下 Git 工具**不进** `AlwaysConfirmTools`，工作区内一律免确认。注意框架既有语义：`AlwaysConfirmTools` 并非"任何模式都确认"——`BypassPermissions` 会直接放行，子代理 allow-all 作用域则直接拒绝（`ToolConfirmationService.cs:516-529`）；`ReadOnlyTools` 也**仅在 Plan 模式生效**（`:453,534`），Default/AcceptEdits 不据此免确认。故 Git 只读免打扰依赖"传工作目录路径"而非只读名单。
- 合并冲突检测依赖退出码 + `git status --porcelain`/`ls-files -u` 双判；不同 git 版本状态码差异需冒烟覆盖。
- 工具数量增加（Git 组约 26 个：只读 8 + 写 18）可能增大工具选择空间；后续如需要可将 Git 组改 opt-in，本轮按决策默认启用。
- MultiEdit 的复杂参数（对象数组）绑定需先验证，见 3.1。
- **worktree 不在本轮范围**：经全工作区检索，代码库与本文档均无 worktree 支持。Claude Code 的 worktree 隔离属工作流层能力（Q5 的"完整对齐"本轮按工具层理解），如后续需要，另立设计新增 `GitWorktree*` 工具或隔离会话语义。
