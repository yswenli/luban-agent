# 工作区 AGENTS.md 初始化 · 实现计划

> **For agentic workers:** 按 Task 顺序实现，每个 Task 用 `- [ ]` 复选框跟踪。每个 Task 末尾必须运行构建验证（见「验证约定」）。

**Goal:** 在 LubanAgentCli 与 LubanAgentCodex 提供「初始化工作区」能力：在工作区根目录生成 `AGENTS.md`。CLI 用 `/init <描述文本>`，Codex 用工作区右键菜单「初始化 AGENTS.md」+ 结构化表单弹窗。生成由新增内置 skill `agents-md-generator` 驱动（LLM 结合工程结构产出），生成后自动注入系统提示词（仅普通工作区）。

**Architecture:**
- **framework（LuBan.AIAgent）**：新增第 10 个内置 skill `AgentsMdGeneratorSkill`，纯提示词模板，DI 注册。frontmatter 只用现有四键（name/description/category/triggers），不改 `SkillMdParser`；原 JSON 的 `version/parameters/output_mode/rules` 全部融入 Markdown 正文。
- **LubanAgentCore**：新增共用的 `IAgentsMdInitializer` 服务（提示词装配 / 旧文件读取 / 结果规范化 / 落盘 / 校验提示），CLI 与 Codex 共用。AGENTS.md 注入点放在 `AgentProfile`，由 `NormalAgentProfile` opt-in。
- **LubanAgentCli**：新增 `InitCommand`，复用 `CommandViewModel` 现有 switch 路由。
- **LubanAgentCodex**：`Sidebar` 工作区行新增 `MenuFlyout`（当前工作区行**没有**右键菜单），新增 `InitAgentsMdDialog` 结构化表单，结果注入聊天流。

**Tech Stack:** .NET 10 / Terminal.Gui（CLI）/ Avalonia（Codex）/ MSTest

**验证约定（AGENTS.md：luban-agent 无自动化测试）：** 每 Task 末尾构建；最终 Task 手动冒烟。提交步骤列出，但**未经用户明确许可不执行 git commit**。

**工作目录约定：** 仓库根不是 git 仓库。framework 改动在 `luban-framework/`（独立仓库），agent 改动在 `luban-agent/`（独立仓库）。

**关键前置结论（调研已确认）：**

| 事实 | 位置 |
|------|------|
| 内置 skill = 9 个硬编码 C# 类，DI 注册，非嵌入资源 | `LuBan.AIAgent/Skills/BuiltIn/*.cs`、`LuBanAgentExtensions.cs:70-80` |
| SKILL.md frontmatter 只识别 4 键，`promptTemplate` 取正文 | `SkillMdParser.cs:57-75` |
| 无 LLM 可调用的 "load skill" 工具；skill 只能由宿主执行 | 全仓无 skill 相关 `ILuBanToolPlugin` |
| CLI 无 `/init`，命令是硬编码 switch | `CommandViewModel.cs:72-102` |
| CLI `CommandBase` 有 `BuildServiceProvider()` 自建容器先例 | `CommandBase.cs:82-90`、`SkillCommand.cs:448-470` |
| Codex 工作区行无右键菜单（历史有，被 `7648d05` 移除） | `Sidebar.axaml.cs:211-326` |
| Codex 会话行有 `MenuFlyout` 可作模板 | `Sidebar.axaml.cs:364-409` |
| `AgentProfile.BuildFullPrompt` 是 `private`，目前只注入 ActiveSkill | `AgentProfile.cs:111-157` |
| 工作区 skills 目录为**空**时才写内置 skill 到盘 | `WorkspaceManager.cs:552-556` |

---

## File Structure

| 文件 | 责任 | 动作 |
|------|------|------|
| `luban-framework/LuBan.AIAgent/Skills/BuiltIn/AgentsMdGeneratorSkill.cs` | 新内置 skill（纯提示词） | 新增 |
| `luban-framework/LuBan.AIAgent/LuBanAgentExtensions.cs` | DI 注册新 skill | 改 |
| `luban-framework/LuBan.AIAgent/README.md` / `README.en.md` | 内置 Skill 表加一行 | 改 |
| `luban-agent/LubanAgentCore/LubanAgentCore.csproj` | 开发期临时 `ProjectReference`（发版后回退 NuGet） | 改（临时） |
| `luban-agent/LubanAgentCore/Services/AgentsMdInitializer.cs` | 共用流程：选项模型 + 提示词装配 + 读旧文件 + 规范化 + 落盘 + 校验提示 | 新增 |
| `luban-agent/LubanAgentCore/Agents/AgentProfile.cs` | 加 `LoadWorkspaceAgentsMd` 钩子 + 读取 `<root>/AGENTS.md` 注入 | 改 |
| `luban-agent/LubanAgentCore/Agents/NormalAgentProfile.cs` | opt-in `LoadWorkspaceAgentsMd => true` | 改 |
| `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs` | 注册 `IAgentsMdInitializer` | 改 |
| `luban-agent/LubanAgentCli/Commands/InitCommand.cs` | `/init` 命令实现 | 新增 |
| `luban-agent/LubanAgentCli/App/ViewModels/CommandViewModel.cs` | 路由 `/init` + `ResolveCommand` + `/help` 文案 | 改 |
| `luban-agent/LubanAgentCli/README.md` | 命令表加 `/init` | 改 |
| `luban-agent/LubanAgentCodex/Views/Controls/Sidebar.axaml.cs` | 工作区行加 `MenuFlyout`、加右键过滤、抛 `AgentsMdInitRequested` | 改 |
| `luban-agent/LubanAgentCodex/Views/InitAgentsMdDialog.axaml(.cs)` | 结构化表单弹窗 | 新增 |
| `luban-agent/LubanAgentCodex/Views/MainWindow.axaml.cs` | 订阅事件、切工作区、弹窗、调用生成 | 改 |
| `luban-agent/LubanAgentCodex/ViewModels/MainWindowViewModel.cs` | `RunInitAgentsMdAsync`：拼提示词 → 复用 ConsumeStreamAsync → 回写文件 | 改 |
| `luban-agent/LubanAgentCli/docs/TUI-SMOKE-TEST.md` | 追加 `/init` 冒烟项 | 改 |

---

## Task 1: framework — 新增 `AgentsMdGeneratorSkill`

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Skills/BuiltIn/AgentsMdGeneratorSkill.cs`
- Modify: `luban-framework/LuBan.AIAgent/LuBanAgentExtensions.cs`
- Modify: `luban-framework/LuBan.AIAgent/README.md`、`README.en.md`

模仿 `GitCommitSkill.cs` 的写法（同样的版权头、XML 注释风格、`override` 属性）。

- [x] **Step 1: 新建 Skill 类**

`Id => "agents-md-generator"`、`Name => "Agents.md 规约生成器"`、`Category => "productivity"`。

`Description`（一字不改照抄需求，仅把文件名改为 AGENTS.md）：`生成工程化Agent元描述文档 AGENTS.md，定义Agent角色、能力、工具、权限、边界与异常策略，供Agent调度框架、LLM读取`

`Examples`：`生成agents.md` / `写agent规约` / `agent能力描述文档`

`TriggerKeywords`：`生成agents.md`、`写agent规约`、`agent能力描述文档`、`agents.md`、`初始化工作区`

- [x] **Step 2: 编写 `PromptTemplate`**

模板内**必须包含一次 `{input}` 占位符**（`SkillBase.ExecuteAsync:72-74` 依赖它做替换，否则只能尾部拼接）。

模板内容要求（原 JSON 的 `parameters` / `output_mode` / `rules` 全部转为正文约束）：

1. 角色：Agent 元描述文档生成专家。
2. 参数说明：`agent_name`（必填）、`agent_scene`（必填）、`capabilities`（选填）、`tools`（选填）、`output_mode`（full/lite，默认 full）。参数由调用方在 `{input}` 中以 `key: value` 形式给出；**未提供的参数一律从工作区实际结构推断，不得编造**。
3. 生成前动作：用 ListDirectory/ReadFile/SearchFiles 浏览工作区，识别语言、框架、构建/测试命令、目录约定；若已存在 AGENTS.md 则**合并更新**而非整篇重写。
4. full 模式固定 10 章节：`Metadata` / `角色定位` / `业务场景` / `能力清单` / `工具与API` / `权限与授权` / `禁止行为` / `异常处理` / `输入输出规约` / `协作与调度接口`。
5. lite 模式 4 章节：`Metadata` / `能力清单` / `工具与API` / `边界`（禁止行为+异常处理合并）。
6. `能力清单`、`工具与API` 必须用 Markdown 表格。
7. `禁止行为`、`异常处理` 在 full 模式下必须**独立成章**，不得并入其它章节。
8. 不写抒情文字，保持技术文档风格。
9. **输出契约（关键）**：只输出 AGENTS.md 正文 Markdown 本身；**不得用 ``` 代码围栏包裹全文**；不得输出"以下是生成结果"这类前后缀。原因是宿主直接把 `result.Text` 落盘。
10. 末尾附一段校验提示（可复制的提示词），用于校验 AGENTS.md 完整性。

- [x] **Step 3: DI 注册**

在 `LuBanAgentExtensions.cs:79` `FindSkillsSkill` 之后追加：

```csharp
        services.AddSingleton<ISkill, AgentsMdGeneratorSkill>();
```

- [x] **Step 4: 同步中英文 README**

`README.md` 内置 Skill 表（约 133-145 行）加行：

```markdown
| `agents-md-generator` | Agents.md 规约生成器 | productivity | 生成工作区 AGENTS.md 元描述文档 |
```

`README.en.md` 同步一条对应英文行（**需两份同步**，见 AGENTS.md 约定）。

- [x] **Step 5: 构建验证**

```powershell
dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj -c Debug -p:GeneratePackageOnBuild=false
```

预期 0 error。**本阶段不递增 `<Version>`**（开发期走 ProjectReference）。

---

## Task 2: 开发期依赖切换（临时）

**Files:**
- Modify: `luban-agent/LubanAgentCore/LubanAgentCore.csproj`

- [x] **Step 1: 替换 PackageReference 为 ProjectReference**

把 `LubanAgentCore.csproj:9` 的

```xml
    <PackageReference Include="LuBan.AIAgent" Version="2026.9.16.1" />
```

替换为

```xml
    <!-- TODO(发布后回退): 开发期临时 ProjectReference，便于 framework 改动即时生效；
         人工发 NuGet 后改回 <PackageReference Include="LuBan.AIAgent" Version="<新版本>" /> -->
    <ProjectReference Include="..\..\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj" />
```

> 注意：`LuBan.AIAgent.csproj` 有 `GeneratePackageOnBuild=True`，构建 agent 时会顺带产出 nupkg。如果介意，构建时加 `-p:GeneratePackageOnBuild=false`，或确认 framework 项目已 ApplyDirectoryBuildProps 统一处理。

- [x] **Step 2: 构建验证（两个解决方案都要过）**

```powershell
dotnet build luban-agent/LubanAgent.slnx
```

预期 0 error。

---

## Task 3: Core — `AgentsMdInitializer` 共用服务

**Files:**
- Create: `luban-agent/LubanAgentCore/Services/AgentsMdInitializer.cs`
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`

**职责边界（重要）：** 本服务只做「宿主无关」的部分——选项模型、提示词装配（渲染 skill 模板）、读旧文件、规范化 LLM 输出、落盘、校验提示。**LLM 调用由各宿主自己做**（CLI 走自建容器 + `ILuBanAgentFactory`，Codex 走 `AgentHostService` 以复用聊天流），否则 Codex「结果注入聊天流」无法成立。

- [x] **Step 1: 新建服务**

命名空间 `LubanAgentCore.Services`，文件头版权注释风格与 `AgentHostService.cs` 一致。

```csharp
public sealed record AgentsMdOptions
{
    public string? AgentName { get; init; }
    public string? AgentScene { get; init; }
    public IReadOnlyList<string>? Capabilities { get; init; }
    public IReadOnlyList<string>? Tools { get; init; }
    /// <summary>full | lite，默认 full</summary>
    public string OutputMode { get; init; } = "full";
    /// <summary>自由描述（CLI 透传文本、Codex 表单附加说明）</summary>
    public string? Description { get; init; }
    /// <summary>已存在的 AGENTS.md 内容，交给 LLM 做合并更新</summary>
    public string? ExistingContent { get; init; }
}

public interface IAgentsMdInitializer
{
    string SkillId { get; }
    string FileName { get; }
    string GetTargetPath(WorkspaceInfo workspace);
    string? ReadExisting(WorkspaceInfo workspace);
    /// <summary>渲染 skill 提示词模板，返回可直接交给 Agent 的完整 prompt</summary>
    string BuildPrompt(AgentsMdOptions options);
    /// <summary>剥离外层代码围栏、统一换行，得到可落盘 Markdown</summary>
    string Normalize(string llmOutput);
    Task WriteAsync(WorkspaceInfo workspace, string markdown, CancellationToken ct = default);
    /// <summary>生成后追加到聊天区的校验提示词（可复制）</summary>
    string BuildVerificationPrompt(string targetPath);
}
```

实现要点：
- `SkillId = "agents-md-generator"`、`FileName = "AGENTS.md"`。
- `BuildPrompt`：从 DI 取 `SkillRegistry`，`Get(SkillId)`；skill 为 null 时抛 `InvalidOperationException("内置 Skill agents-md-generator 未注册")`。按 `SkillBase.ExecuteAsync:72-74` 同样的规则渲染：`PromptTemplate.Contains("{input}") ? Replace("{input}", input) : $"{PromptTemplate}\n\n{input}"`，其中 `input` 由 `AgentsMdOptions` 序列化为

  ```
  agent_name: xxx
  agent_scene: xxx
  capabilities: a, b, c
  tools: x, y
  output_mode: lite
  description: ...
  existing_agents_md: |
    <旧文件全文>
  ```

  空字段省略不写（让 LLM 从工作区推断）。
- `ReadExisting`：`GetTargetPath` 存在则 `File.ReadAllText`，否则 `null`。
- `Normalize`：若全文以 ` ```markdown ` 或 ` ``` ` 开头且以 ` ``` ` 结尾，剥离围栏；`Trim()` + 结尾补一个 `\n`。
- `WriteAsync`：`File.WriteAllTextAsync(path, markdown, new UTF8Encoding(false), ct)`，路径取 `GetTargetPath`。
- `BuildVerificationPrompt`：返回一段固定提示词，例如「请逐章校验 `<path>` 是否包含 Metadata / 角色定位 / 业务场景 / 能力清单 / 工具与API / 权限与授权 / 禁止行为 / 异常处理 / 输入输出规约 / 协作与调度接口，列出缺失或空泛的章节并补齐。」+ 章节清单。**同时列出 lite 模式的 4 章对照**。

- [x] **Step 2: DI 注册**

在 `AgentHostBuilder.BuildServiceProvider`（`services.AddLuBanAgent(configuration);` 之后）追加：

```csharp
        services.AddSingleton<IAgentsMdInitializer, AgentsMdInitializer>();
```

- [x] **Step 3: 构建验证**

```powershell
dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj
```

---

## Task 4: Core — AGENTS.md 注入系统提示词（仅普通工作区）

**Files:**
- Modify: `luban-agent/LubanAgentCore/Agents/AgentProfile.cs`
- Modify: `luban-agent/LubanAgentCore/Agents/NormalAgentProfile.cs`

- [x] **Step 1: `AgentProfile` 加 opt-in 钩子 + 注入逻辑**

`BuildFullPrompt` 保持 `private`（最小改动），在类中新增：

```csharp
    /// <summary>
    /// 是否把工作区根目录的 AGENTS.md 注入系统提示词。默认关闭，普通工作区打开。
    /// </summary>
    protected virtual bool LoadWorkspaceAgentsMd => false;
```

在 `BuildFullPrompt`（`AgentProfile.cs:111`）内、`return sb.ToString();`（`:156`）之前插入：

```csharp
        if (LoadWorkspaceAgentsMd)
        {
            try
            {
                var agentsMdPath = Path.Combine(workspace.RootPath, "AGENTS.md");
                if (File.Exists(agentsMdPath))
                {
                    var content = File.ReadAllText(agentsMdPath);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        sb.AppendLine();
                        sb.AppendLine();
                        sb.AppendLine("## 工作区规约 (AGENTS.md)");
                        sb.AppendLine(content);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"读取工作区 AGENTS.md 失败: {workspace.RootPath}", ex);
            }
        }
```

> 读取失败必须吞掉异常，不能因 AGENTS.md 编码/长度问题导致 Agent 创建失败。`Logger` 已在本类可用（`LuBan.AIAgent` 全局）。

- [x] **Step 2: `NormalAgentProfile` opt-in**

在 `NormalAgentProfile.cs` 加：

```csharp
    /// <inheritdoc/>
    protected override bool LoadWorkspaceAgentsMd => true;
```

`RagAgentProfile` **不改**（决策：RAG 工作区不注入）。

- [x] **Step 3: 构建验证**

```powershell
dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj
```

---

## Task 5: CLI — `/init` 命令

**Files:**
- Create: `luban-agent/LubanAgentCli/Commands/InitCommand.cs`
- Modify: `luban-agent/LubanAgentCli/App/ViewModels/CommandViewModel.cs`
- Modify: `luban-agent/LubanAgentCli/README.md`

- [x] **Step 1: 新建 `InitCommand`**

继承 `CommandBase`，构造参数：`(ConfigManager, IConfiguration, IAgentsMdInitializer, IWorkspaceManager, ILuBanAgentFactory, ITuiOutputWriter, ITuiUiService)`。

> **偏离记录（已获用户确认）**：原计划用 `CommandBase.BuildServiceProvider()` 自建容器 + `SkillRegistry` 参数。但自建容器与根容器 `IOptions<LuBanAgentOptions>` 相互独立，拿不到 `WorkspaceManager` 注入的 `WorkspaceRoot` 与 `Tools.FileSystem.AllowedRoots`，`PathGuard` 会拒绝一切文件访问，agent 无法浏览工作区。改为直接注入根容器的 `ILuBanAgentFactory`（根容器的 Options 已被 `WorkspaceManager.AddWorkspaceRootToPathGuard` 就地改写），因此不再需要 `SkillRegistry`。

```
Name => "init"
Description => "初始化工作区 AGENTS.md"
```

`ExecuteAsync()`（无参）→ 打印用法与示例，返回。

`ExecuteAsync(string[] args)`：
1. 无内容 → 打印用法，`return true`。
2. 不支持子命令 → 前缀非 `-` 的 token 全部 `string.Join(" ", ...)` 作为描述文本。
3. `workspace = _workspaceManager.CurrentWorkspace`，为 null 报错退出。
4. `existing = _initializer.ReadExisting(workspace)`。
5. `options = new AgentsMdOptions { Description = description, ExistingContent = existing, OutputMode = "full" }`（full/lite 由 LLM 从描述自然语言判断，代码不解析）。
6. `targetPath = _initializer.GetTargetPath(workspace)`；若 `existing != null` → 打印旧文件摘要，`Ui.Confirm("覆盖 AGENTS.md", ...)` 未确认则中止。
7. `await _agentFactory.CreateAsync(modelName: ConfigManager.SelectedModel)`（`_agentFactory` 为构造注入的根容器 `ILuBanAgentFactory`）；`ConfigManager.HasSelectedModel` 为 false 时先报错退出。`LuBanAgent` 非 `IDisposable`，不要 `using`。
8. `SpinnerService.Start("正在生成 AGENTS.md...")` → `await _initializer.BuildPrompt(options)` 交给 `Agent.RunAsync(prompt, ct)`。
   > 这里**不经过 `ISkill.ExecuteAsync`**：因为要把 LLM 输出落盘而不是打印，且要复用同一个 agent 实例。若坚持走 skill，则用 `SkillContext` + `skill.ExecuteAsync`，结果从 `SkillResult.Text` 取。
9. `Normalize` → `WriteAsync` → 打印成功信息与路径。
10. 打印 `BuildVerificationPrompt(targetPath)`。
11. 全部包 try/catch，异常 `Logger.Error` + `Writer.WriteError(GetFriendlyApiErrorMessage(ex))`（`CommandBase` 已提供）。

- [x] **Step 2: `CommandViewModel` 路由**

`TryExecute` 的 switch（`:92` `/rag` 之后）追加：

```csharp
            case "/init":                        ExecuteManagementCommand<InitCommand>(parts); return true;
```

`ResolveCommand<TCommand>`（`:243` `/rag` 之后、`_ => null` 之前）追加：

```csharp
            nameof(InitCommand) => (TCommand)(object)new InitCommand(configManager, configuration,
                _services.GetRequiredService<IAgentsMdInitializer>(),
                _services.GetRequiredService<IWorkspaceManager>(),
                _services.GetRequiredService<ILuBanAgentFactory>(), _writer, _ui),
```

> `ResolveCommand` 用的是根 DI 容器 `_services`，Task 3 Step 2 已把 `IAgentsMdInitializer` 注册在 `AgentHostBuilder`，可解析。

- [x] **Step 3: `/help` 文案**

`ExecuteHelp`（`CommandViewModel.cs:113-126`）在 `/rag` 行后加：

```csharp
        _writer.WriteLine("  /init <描述文本>     初始化工作区 AGENTS.md");
```

- [x] **Step 4: README 命令表**

`LubanAgentCli/README.md` 命令表（约 312-328 行）同步加 `/init`。若存在英文版 README 一并同步。

- [x] **Step 5: 构建验证**

```powershell
dotnet build luban-agent/LubanAgentCli/LubanAgentCli.csproj
```

- [ ] **Step 6: 手动冒烟（CLI）**

在任一工作区下启动 CLI，执行：

```
/init 这是一个日志分析 Agent，负责解析和排查线上日志
/help
```

预期：`/init` 有 spinner、生成后打印路径与校验提示；`AGENTS.md` 出现在工作区根目录、内容为纯 Markdown 无外层围栏；`/help` 含 `/init` 行；未知子命令参数不报 `未知命令`。

---

## Task 6: Codex — 侧边栏工作区右键菜单

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/Controls/Sidebar.axaml.cs`

- [x] **Step 1: 新增事件**

在 `Sidebar` 的事件区（`WorkspaceSelected` 附近，`:46`）加：

```csharp
    /// <summary>请求初始化工作区 AGENTS.md。</summary>
    public event EventHandler<WorkspaceInfo>? AgentsMdInitRequested;
```

- [x] **Step 2: 工作区行加 `MenuFlyout`**

在 `wsRow.Child = wsGrid;`（`Sidebar.axaml.cs:312`）之后、`PointerPressed`（`:315`）之前插入。结构照抄会话行的 `MenuFlyout`（`:364-409`）：`MenuItem` + 横向 `StackPanel`（`PathIcon` 文档图标 + `TextBlock "初始化 AGENTS.md"`，`Foreground = Brush.Parse("#CCCCCC")`）。

```csharp
            var wsCopy = ws;
            var initItem = new Avalonia.Controls.MenuItem { Header = ...面板... };
            initItem.Click += (s, e) => AgentsMdInitRequested?.Invoke(this, wsCopy);
            var wsFlyout = new MenuFlyout();
            wsFlyout.Items.Add(initItem);
            wsRow.ContextFlyout = wsFlyout;
```

- [x] **Step 3: 补右键过滤（修既有缺陷）**

当前 `wsRow.PointerPressed`（`:315-324`）**没有**右键判断，右键会误触发切换工作区。在方法体首行加（与会话行 `:413` 对齐）：

```csharp
                if (e.GetCurrentPoint(wsRow).Properties.IsRightButtonPressed)
                    return; // 右键只弹菜单，不切换工作区
```

- [x] **Step 4: 构建验证**

```powershell
dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj
```

---

## Task 7: Codex — `InitAgentsMdDialog` 结构化表单弹窗

**Files:**
- Create: `luban-agent/LubanAgentCodex/Views/InitAgentsMdDialog.axaml`
- Create: `luban-agent/LubanAgentCodex/Views/InitAgentsMdDialog.axaml.cs`

参考 `NewWorkspaceDialog.axaml(.cs)`（多字段 + 样式）与 `Styles/Dialogs.axaml`（`dlgWindow` / `dlgPrimary` / `dlgGhost`）。弹窗顶部加一句说明：`将根据当前工作区结构生成 AGENTS.md`。

- [x] **Step 1: 布局**

字段（与 skill 参数一一对应）：

| 控件 | 名称 | 说明 |
|------|------|------|
| `TextBox` | `AgentNameBox` | Agent 名称（必填） |
| `TextBox` | `AgentSceneBox` | 业务场景（必填） |
| `TextBox` `AcceptsReturn` | `CapabilitiesBox` | 能力列表，**每行一条** |
| `TextBox` `AcceptsReturn` | `ToolsBox` | 工具/API，**每行一条** |
| `RadioButton` ×2 | `ModeFullRadio`/`ModeLiteRadio` | 输出模式，默认选中 full |
| 底部 | `OkButton`/`CancelButton` | 确定/取消 |

- [x] **Step 2: 代码后置**

`public AgentsMdOptions? Result { get; private set; }`
`ShowDialog<T>(owner)` 返回 `Result`；`OkButton.Click` 时校验必填（空则提示不关闭），把多行框按 `\n` 拆分、`Trim`、去空行得到 `IReadOnlyList<string>`，组装 `AgentsMdOptions` 后 `Close(Result)`；`CancelButton` → `Close(null)`。

- [x] **Step 3: 构建验证**

```powershell
dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj
```

---

## Task 8: Codex — 接线与结果注入聊天流

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/MainWindow.axaml.cs`
- Modify: `luban-agent/LubanAgentCodex/ViewModels/MainWindowViewModel.cs`

- [x] **Step 1: 订阅事件**

`MainWindow.axaml.cs:140-143` 现有订阅区加：

```csharp
                _sidebar.AgentsMdInitRequested += OnAgentsMdInitRequested;
```

- [x] **Step 2: 实现 `OnAgentsMdInitRequested`**

放在 `OnWorkspaceSelected`（`:347`）附近，复用其前缀逻辑：`EnsureAuthorizedAsync(ws)` → `SetCurrentAsync(ws.WorkspaceId)`（决策：**先切换工作区**）→ 拿 `_viewModel.Services` 解析 `IAgentsMdInitializer` → 读 `ExistingContent` → `new InitAgentsMdDialog()` 预填 → `await dialog.ShowDialog<AgentsMdOptions?>(this)` → 用户确定后调 `_viewModel.RunInitAgentsMdAsync(options)`。

若 `existing != null`，在弹窗确定后用 `Dialogs.ShowConfirmAsync(... "已存在 AGENTS.md，将合并更新并覆盖" ..., danger: true)` 二次确认。

- [x] **Step 3: `MainWindowViewModel` 改造 `ConsumeStreamAsync` 返回最终文本**

`ConsumeStreamAsync`（`:577`）改为 `private async Task<string> ConsumeStreamAsync(...)`：加一个局部 `StringBuilder`，在 `await foreach` 里对 `TextDeltaEvent` 累加（`t.Delta`），循环结束后 `return sb.ToString()`。`SendAsync`（`:147`）改为 `await Task.Run(async () => { await ConsumeStreamAsync(...); })`（丢弃返回值，行为不变）。

> 这是本计划唯一的既有代码行为改动点：只增加返回值与累加，不改事件分发顺序。

- [x] **Step 4: 新增 `RunInitAgentsMdAsync`**

```csharp
    public async Task RunInitAgentsMdAsync(AgentsMdOptions options)
    {
        // 1) 解析服务、补 ExistingContent、BuildPrompt（含 skill 模板）
        // 2) Messages.Add(new UserMessageItem { Content = 描述文本 or options.ToString() })
        // 3) 与 SendAsync 相同的 IsRunning / _cts / runSeq / _throttle 装配
        // 4) var text = await Task.Run(() => ConsumeStreamAsync(prompt, ct, runSeq));
        // 5) var md = _initializer.Normalize(text);
        //      await _initializer.WriteAsync(workspace, md, ct);
        // 6) Messages.Add(new SystemMessageItem { Content = $"已生成 {targetPath}" });
        // 7) Messages.Add(new SystemMessageItem { Content = _initializer.BuildVerificationPrompt(targetPath) });
        // 8) _agentHost.Reset();  // 让新建的 Agent 下一回合读到新 AGENTS.md
    }
```

`SkillRegistry` / `IAgentsMdInitializer` / `IWorkspaceManager` 通过 `Services`（`MainWindowViewModel.cs:47`）解析。

> **实现偏差记录（Task 8 落地时）**：
> 1. 弹窗预填改为 `new InitAgentsMdDialog(new AgentsMdOptions { AgentName = ws.Name })`（Agent 名称默认取工作区名），走 `InitAgentsMdDialog(AgentsMdOptions?)` 重载；`ExistingContent` 由宿主在弹窗返回后用 `options with { ExistingContent = existing }` 补齐。
> 2. `OnAgentsMdInitRequested` 增加 `IsRunning` 守卫（Agent 运行中直接拒绝，避免与在途回合争抢 `_cts`/消息流）；`SetCurrentAsync` 后补 `Dispatcher.UIThread.Post(_sidebar.SetServiceProvider + UpdateFooter)`，与 `OnWorkspaceSelected` 对齐。
> 3. Step 3 只给 `ConsumeStreamAsync` 增加返回值与累加；`SendAsync:147` 的 `await Task.Run(() => ConsumeStreamAsync(...))` **未改写**——`Task.Run` 对 `Func<Task<string>>` 返回 `Task<string>`，`await` 丢弃返回值即可，行为完全一致，避免无意义改动。
> 4. `RunInitAgentsMdAsync` 开头补 `_agentHost.InitializeAsync()` 预热（失败即报错返回），避免生成过程与 Agent 初始化错误混在同一流里难以定位；用户消息文案用 `初始化工作区 AGENTS.md：{AgentName}（{AgentScene}，{OutputMode}）` 而非 `options.ToString()`（record 默认 ToString 可读性差）。

- [x] **Step 5: 构建验证**

```powershell
dotnet build luban-agent/LubanAgent.slnx
```

- [ ] **Step 6: 手动冒烟（Codex）**

1. 右键任一非当前工作区 → 出现「初始化 AGENTS.md」，且**不切换**工作区。
2. 点击 → 工作区切过去 → 弹出表单 → 填写 → 确定。
3. 聊天区出现用户消息 → 流式输出 → 落盘提示 → 校验提示。
4. 目视 `AGENTS.md`：纯 Markdown、无 ``` 围栏、表格存在、禁止行为与异常处理独立成章。
5. 再次对同一工作区执行 → 出现覆盖二次确认 → 确认后内容为合并版。
6. 在该工作区发一条普通对话 → Agent 系统提示词含「## 工作区规约 (AGENTS.md)」（可用 `调试输出/日志` 或临时断点核对）。

---

## Task 9: 收尾

- [x] **Step 1: 更新冒烟文档**

`luban-agent/LubanAgentCli/docs/TUI-SMOKE-TEST.md` 追加 `/init` 用例（对应 Task 5 Step 6）。

- [x] **Step 2: 全量构建**

```powershell
dotnet build luban-framework/LuBan.FrameWork.sln
dotnet build luban-agent/LubanAgent.slnx
```

- [x] **Step 3: 回归测试（framework）**

```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj
```

> 该测试集多为集成测试（Redis/微信/轻流/SignalR/RagFlow/Office），环境不通的失败**不算回归**，只看是否新增失败。

- [ ] **Step 4: 发版与回退（需用户明确许可后执行）**

1. 递增 `LuBan.AIAgent.csproj` `<Version>`（日期制，如 `2026.9.16.1` → `2026.9.17.1`）。
2. `dotnet pack` 产出 nupkg 并发布 NuGet。
3. 把 `LubanAgentCore.csproj` 的 `ProjectReference` 改回 `PackageReference Include="LuBan.AIAgent" Version="<新版本>"`。
4. 重新 `dotnet build luban-agent/LubanAgent.slnx` 验证。
5. git 提交（进各自子仓库，中文短句；**须用户许可**）。

---

## Task 10: AGENTS.md 生效时机修复（方案 A，已获用户批准）

**问题**：CLI 侧 `/init` 只落盘，不重建 `ConversationViewModel` 的 Agent → 新生成的 AGENTS.md 需切换工作区/切模型/重启才进系统提示词。Codex 侧已在 `RunInitAgentsMdAsync` 末尾 `Reset()`，无此问题。

**用户追加要求**：加载工作区、切换工作区两条路径都必须重新读取 AGENTS.md。

### 实现

- [x] **Step 1: 新增契约**

`LubanAgentCore\Services\IAgentContextReloadTrigger.cs`：**只含** `void RequestReload();`（**不含** `IsRunning`，已否决该成员）。

- [x] **Step 2: `ConversationViewModel` 实现契约**

- 类声明 → `IDisposable, IAgentContextReloadTrigger`。
- 新增 `int _pendingForceReload`（无条件重建）与 `int _pendingKeepView`（保留会话视图），均用 `Interlocked`。
- `public void RequestReload()`：置 `_pendingKeepView` + `RequestSync(forceReload: true)`。
- `RequestSync(bool forceReload = false)`：`forceReload` 时置 `_pendingForceReload`。
- `SyncContextAsync`：`IsRunning` 早返回**之前不消费**标志（保持置位，本轮结束后重读）；早返回之后消费 `forceReload`/`keepView`；`needRebuild = forceReload || _agent is null || 工作区变化 || 模型变化`；`resetDoc = clearDoc && !keepView`（**关键**：`/init` 后同步不清文档、不重载历史，避免抹掉生成结果与校验提示）。
- `OnCurrentWorkspaceChanged` → `RequestSync(forceReload: true)`（切换工作区强制重载）。
- `InitializeAsync` 进入 `_syncGate` 前置 `_pendingForceReload = 1`（加载工作区强制重载）。
- `ProcessInputAsync` finally 的 `_pendingSync` 分支保持 `RequestSync()`（默认 clearDoc）。

- [x] **Step 3: 接线**

- `InitCommand` ctor 末尾追加 `IAgentContextReloadTrigger? reloadTrigger = null`（保持既有调用兼容）；落盘成功提示之后调 `_reloadTrigger?.RequestReload()`。
- `CommandViewModel.ResolveCommand<InitCommand>` 传入 `_conversationVm`（`ConversationViewModel` 隐式实现该接口）。**不走 DI**——`ConversationViewModel` 是 `RootView.cs:77` 手动 `new`，未注册容器。

- [x] **Step 4: 构建验证**

`dotnet build LubanAgentCli\LubanAgentCli.csproj -c Debug` → 0 警告 0 错误。
`LubanAgent.slnx` 全量构建的 Codex 2 个错误为**文件锁定**（VS + LubanAgentCodex 进程占用 `LubanAgentCore.dll`），非编译错误。

### 判定记录

1. **不给 `InitCommand` 加 `IsRunning` 守卫**：`RootView.OnInputSubmitted` 在 `_runActive || _vm.IsRunning || _initializing` 时把输入排队，`/init` 不会与运行中的对话并发；`SyncContextAsync` 的 `_pendingSync` 延迟机制已正确处理运行中请求。
2. **`AgentHostService` 不实现该接口**：Codex 侧已由 `RunStreamingAsync:124-128`（`_agent == null` 或工作区变化即重建）+ `/init` 后 `Reset()` 满足，避免多余改动。
3. **契约不含 `IsRunning`**：方案 A 定稿时已明确否决，中途一度加入后已回退。

### 修复：队列中的命令被当作聊天输入（已修，用户批准）

**现象**：`RootView` 队列中的输入经 `DrainQueue` → `SubmitAsync(text)` → `_vm.ProcessInputAsync(text)`，**绕过** `_commandVm.TryExecute`，导致「Agent 运行中排队的 `/xxx` 命令」在本轮结束后被当普通对话发给 Agent。

**修复**（`RootView.cs`）：

1. 抽出 `TryDispatchCommand(string text)`：`text.StartsWith('/') && _commandVm.TryExecute(text)`；`OnInputSubmitted` 立即执行分支改用它。
2. `DrainQueue` 改为循环：出队项先试 `TryDispatchCommand`；命中命令则**继续排空**（命令自身异步执行、不占用对话运行态），未命中则 `SubmitAsync` 并 `return`；队列空时 `_runActive = false`。

**残留语义说明**：管理型命令是 fire-and-forget（`ExecuteManagementCommand` → `Task.Run`），队列中多条命令之间只保证**派发顺序**，不保证完成顺序；此为既有架构限制，本次不引入命令完成信号。

---

## 风险与备注

1. **LLM 输出不稳定**：可能无视"不要代码围栏"的约束。`Normalize` 已做剥离兜底；若仍偶发，需在 `BuildPrompt` 里追加更强的输出契约（例如"第一个字符必须是 `#`"）。
2. **上下文膨胀**：决策为**不做长度截断**，超大 AGENTS.md 会挤占上下文。若后续出现实际症状，再补截断。
3. **ProjectReference 期间的 nupkg 副作用**：`GeneratePackageOnBuild=True` 会在构建 agent 时产出 framework 包，注意不要误发布。
4. **`SkillBase` 的 `{input}` 替换**：`PromptTemplate` 必须恰好包含一次 `{input}`，否则走尾部拼接分支，JSON/YAML 里若出现字面 `{input}` 会被误替换。
5. **`EnsureConfigDirectory` 只在 skills 目录为空时写盘**：已存在的工作区不会把新 skill 的 SKILL.md 落到 `.luban-agent/skills/agents-md-generator/`。但硬编码 skill 在 `SkillRegistry` 中优先级最高（`RebuildMerged`），**功能不受影响**；用户在 Codex 设置窗看不到该 SKILL.md 属预期。
6. **`AgentsMdInitializer` 用根容器解析 `SkillRegistry`**：`SkillRegistry` 是单例，`CommandViewModel` 已从 `_services` 解析它，一致。
