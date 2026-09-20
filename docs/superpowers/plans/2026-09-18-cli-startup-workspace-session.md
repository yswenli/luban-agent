# LubanAgentCli 启动默认工作区/会话 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** CLI 启动时自动进入"最近活跃的非 RAG 工作区"的最近会话；无可用工作区时弹出创建表单（含可选 AGENTS.md 描述），退出时在控制台打印当前工作区/会话。

**Architecture:** 启动向导（`StartupDialog`）只负责配置/数据库/嵌入/DI 构建，不再碰工作区；向导关闭后、主界面（`RootView`）创建前，由新增的 CLI 侧 `StartupWorkspaceResolver` 完成"选/建工作区 → 授权 → SetCurrentAsync → 可选生成 AGENTS.md"。`AgentHostBuilder` 里 CWD 静默建工作区的旧逻辑删除。退出打印放在 `TerminalGuiApp.Run` 的 `finally`、`application.Dispose()` 之后。

**Tech Stack:** .NET 10、Terminal.Gui 2.6.0-develop.61、MSTest 无（luban-agent 无自动化测试，验证 = 构建 + 手动冒烟）。

**约定：** 全程中文提交信息，luban-agent 提交带 conventional 前缀（`feat:`/`fix:`/`refactor:`）。所有 git 命令在 `luban-agent/` 子仓库执行。

---

## 决策基线（来自 grill 访谈）

| 分支 | 决定 |
|---|---|
| 默认工作区 | `LastActiveAt` 最近活跃，排除 `Type == "Rag"`；`RootPath` 不存在则跳过；全部不可用视为无工作区 |
| 默认会话 | 该工作区 `UpdateTime` 最近活跃（沿用 `SetCurrentAsync`） |
| 无工作区 | 弹 `ShowForm` 三字段（路径必填预填 CWD、名称可空、AGENTS.md 描述多行可空）；路径不存在→`Notify` 报错后循环重弹；取消→进无工作区主界面并在启动提示里引导 |
| AGENTS.md | 名称可空（`CreateWorkspaceAsync` 自动取目录名）；描述仅映射 `AgentsMdOptions.Description`；仅当描述非空 **且** `ConfigManager.HasSelectedModel` 时生成；目录已有 `AGENTS.md` 则跳过保留；生成失败不阻断，仅记入启动提示 |
| 授权 | 保留现有授权弹窗（`AuthorizationPrompt`），顺序：授权 → `SetCurrentAsync` → 生成 |
| 结构 | CLI 侧主导；删除 `AgentHostBuilder.InitializeWorkspaceAsync` 与 `StartupRunner.InitializeWorkspaceAsync` |
| 退出打印 | `application.Dispose()` 后 `Console.WriteLine` 两行：`工作区: <名> (<RootPath>)` / `会话: <标题> (<SessionId>)`，缺省 `-` |
| 会话标题 | 自动创建的会话全局统一为"新会话"（`SwitchWorkspaceAsync` 与 `ClearAllSessionsAsync`） |

**访谈原文备注：** 曾先选"对话框内生成"，后澄清为"向导里生成"；曾选"向导之后单独弹"。最终解：创建表单在向导之后单独弹，生成递归属启动阶段、用独立轻量进度弹窗承载。

---

## File Structure

| 文件 | 动作 | 责任 |
|---|---|---|
| `LubanAgentCore/Services/WorkspaceManager.cs` | 修改 | 自动建会话标题"默认会话"→"新会话" |
| `LubanAgentCore/Services/SessionManager.cs` | 修改 | `ClearAllSessionsAsync` 重建会话标题同步改"新会话" |
| `LubanAgentCli/App/ITuiUiService.cs` | 修改 | 新增 `RunWithProgress` 原语 |
| `LubanAgentCli/App/TuiUiService.cs` | 修改 | 实现 `RunWithProgress` |
| `LubanAgentCli/App/StartupWorkspaceResolver.cs` | 新建 | 启动选择/创建/授权/生成编排 |
| `LubanAgentCli/App/StartupDialog.cs` | 修改 | 移除步骤⑤调用 |
| `LubanAgentCli/App/TerminalGuiApp.cs` | 修改 | 向导后调用解析器；退出打印 |
| `LubanAgentCli/App/StartupRunner.cs` | 修改 | 删除 `InitializeWorkspaceAsync` 转发 |
| `LubanAgentCore/Hosting/AgentHostBuilder.cs` | 修改 | 删除 `InitializeWorkspaceAsync` |
| `LubanAgentCli/docs/TUI-SMOKE-TEST.md` | 修改 | 追加冒烟用例 |

---

## Task 1: 统一自动创建会话标题为"新会话"

**Files:**
- Modify: `LubanAgentCore/Services/WorkspaceManager.cs:348`
- Modify: `LubanAgentCore/Services/SessionManager.cs:262`

- [ ] **Step 1: 改 WorkspaceManager**

`LubanAgentCore/Services/WorkspaceManager.cs` 第 348 行：

```csharp
                await _sessionManager.CreateSessionAsync(userId: "default", title: "新会话");
```

- [ ] **Step 2: 改 SessionManager**

`LubanAgentCore/Services/SessionManager.cs` 第 262 行：

```csharp
            await CreateSessionAsync(userId: "default", title: "新会话");
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: `Build succeeded` 0 error（预存在 warning 可忽略）

- [ ] **Step 4: 提交**

```bash
git add LubanAgentCore/Services/WorkspaceManager.cs LubanAgentCore/Services/SessionManager.cs
git commit -m "refactor: 自动创建会话标题统一为 新会话"
```

---

## Task 2: 新增 `RunWithProgress` 进度原语

**Files:**
- Modify: `LubanAgentCli/App/ITuiUiService.cs`
- Modify: `LubanAgentCli/App/TuiUiService.cs`

- [ ] **Step 1: 接口新增方法**

在 `ITuiUiService.cs` 的 `ShowForm` 声明之后插入：

```csharp
    /// <summary>
    /// 运行一个轻量模态进度框：后台执行 work，work 内通过 report 回调更新状态文本，执行完毕自动关闭。
    /// 仅用于启动阶段等无主循环承载的阻塞任务。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="initialStatus">初始状态文本。</param>
    /// <param name="work">实际工作，参数为状态更新回调。</param>
    void RunWithProgress(string title, string initialStatus, Func<Action<string>, Task> work);
```

- [ ] **Step 2: 实现**

在 `TuiUiService.cs` 的 `ShowForm` 方法之后、`ShowTable` 之前插入：

```csharp
    /// <inheritdoc/>
    public void RunWithProgress(string title, string initialStatus, Func<Action<string>, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        RunModal<object?>(() =>
        {
            using var dialog = new Dialog
            {
                Title = title,
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 64,
                Height = 8
            };

            var label = new Label
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill(2),
                Height = Dim.Fill(2),
                Text = initialStatus
            };
            dialog.Add(label);

            var app = _app;
            _ = Task.Run(async () =>
            {
                try
                {
                    await work(msg => app.Invoke(() => label.Text = msg));
                }
                finally
                {
                    app.Invoke(() => dialog.RequestStop());
                }
            });

            _app.Run(dialog);
            return null;
        });
    }
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: `Build succeeded`

- [ ] **Step 4: 提交**

```bash
git add LubanAgentCli/App/ITuiUiService.cs LubanAgentCli/App/TuiUiService.cs
git commit -m "feat: TuiUiService 新增 RunWithProgress 进度弹窗原语"
```

---

## Task 3: 新增启动工作区解析器 `StartupWorkspaceResolver`

**Files:**
- Create: `LubanAgentCli/App/StartupWorkspaceResolver.cs`

- [ ] **Step 1: 新建文件**

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*描述：启动阶段工作区解析器：选最近活跃非 RAG 工作区，无则弹创建表单，
*      随后授权、设为当前并可选生成 AGENTS.md（CLI 侧主导，向导之后、主界面之前调用）
*
*****************************************************************************/
using LubanAgentCore.Services;
using LuBan.AIAgent.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCli.App;

/// <summary>
/// 启动阶段工作区解析器。
/// </summary>
internal static class StartupWorkspaceResolver
{
    /// <summary>
    /// 解析并进入工作区。无可用工作区时弹出创建表单；用户取消则返回 false（进入无工作区主界面）。
    /// </summary>
    /// <param name="services">根级 DI 容器。</param>
    /// <param name="ui">TUI 模态交互服务。</param>
    /// <param name="notices">启动提示集合（追加，供主界面横幅展示）。</param>
    /// <returns>是否已成功进入某个工作区。</returns>
    public static async Task<bool> ResolveAsync(IServiceProvider services, ITuiUiService ui, List<string> notices)
    {
        var workspaceManager = services.GetRequiredService<IWorkspaceManager>();

        var target = await FindLatestUsableAsync(workspaceManager);

        if (target is null)
        {
            target = await PromptCreateAsync(services, ui);
            if (target is null)
            {
                notices.Add("未选择工作区。请先在磁盘上创建目录，再用 /w -n <路径> 创建工作区后开始对话。");
                return false;
            }
        }
        else
        {
            notices.Add($"当前工作区: {target.Name} ({target.RootPath})");
        }

        // 授权（保留现有授权弹窗）
        var authorized = await workspaceManager.EnsureAuthorizedAsync(target);
        notices.Add(authorized ? "工作区已授权" : "工作区未授权，文件操作将受限");

        // 设置当前工作区（内部恢复最近活跃会话，无则新建“新会话”）
        await workspaceManager.SetCurrentAsync(target.WorkspaceId);

        return true;
    }

    /// <summary>
    /// 取最近活跃、类型非 Rag 且根目录仍存在的工作区；按 LastActiveAt 倒序（仓储已排序）。
    /// </summary>
    private static async Task<WorkspaceInfo?> FindLatestUsableAsync(IWorkspaceManager workspaceManager)
    {
        var workspaces = await workspaceManager.GetUserWorkspacesAsync();
        foreach (var ws in workspaces)
        {
            if (string.Equals(ws.Type, "Rag", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Directory.Exists(ws.RootPath)) continue;
            return ws;
        }
        return null;
    }

    /// <summary>
    /// 循环弹出创建表单，直到成功创建或用户取消。
    /// </summary>
    private static async Task<WorkspaceInfo?> PromptCreateAsync(IServiceProvider services, ITuiUiService ui)
    {
        var workspaceManager = services.GetRequiredService<IWorkspaceManager>();
        var configManager = services.GetRequiredService<ConfigManager>();

        var cwd = Directory.GetCurrentDirectory();

        while (true)
        {
            var values = ui.ShowForm("创建工作区", new[]
            {
                new FormField("路径", InitialValue: cwd),
                new FormField("名称（可空，默认取目录名）", Required: false),
                new FormField("AGENTS.md 创建描述（可空）", Required: false, Multiline: true)
            });

            if (values is null) return null; // 用户取消

            var path = values[0].Trim();
            if (!Directory.Exists(path))
            {
                ui.Notify("创建工作区", $"目录不存在: {path}");
                continue;
            }

            var name = values[1].Trim();
            var description = values[2].Trim();

            WorkspaceInfo created;
            try
            {
                created = await workspaceManager.CreateWorkspaceAsync(
                    path, string.IsNullOrWhiteSpace(name) ? null : name, type: "Normal");
            }
            catch (InvalidOperationException ex)
            {
                ui.Notify("创建工作区", ex.Message);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                await TryGenerateAgentsMdAsync(services, ui, created, description);
            }

            return created;
        }
    }

    /// <summary>
    /// 尝试生成 AGENTS.md：未选模型或已存在则跳过，失败不阻断。
    /// </summary>
    private static async Task TryGenerateAgentsMdAsync(
        IServiceProvider services, ITuiUiService ui, WorkspaceInfo workspace, string description)
    {
        var initializer = services.GetRequiredService<IAgentsMdInitializer>();
        var configManager = services.GetRequiredService<ConfigManager>();

        if (!configManager.HasSelectedModel)
        {
            ui.Notify("AGENTS.md", "未选择模型，已跳过 AGENTS.md 生成。可稍后用 /init 生成。");
            return;
        }

        if (initializer.ReadExisting(workspace) is not null)
        {
            ui.Notify("AGENTS.md", "工作区已存在 AGENTS.md，已保留原文件，跳过生成。");
            return;
        }

        var agentFactory = services.GetRequiredService<ILuBanAgentFactory>();
        var model = configManager.SelectedModel!;

        try
        {
            ui.RunWithProgress("生成 AGENTS.md", "正在生成 AGENTS.md...", async report =>
            {
                var prompt = initializer.BuildPrompt(new AgentsMdOptions
                {
                    Description = description,
                    OutputMode = "full"
                });

                var agent = await agentFactory.CreateAsync(modelName: model);
                var response = await agent.RunAsync(prompt);
                var markdown = initializer.Normalize(response.Text ?? string.Empty);
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    throw new InvalidOperationException("生成结果为空");
                }

                await initializer.WriteAsync(workspace, markdown);
                report($"AGENTS.md 已生成（{markdown.Length} 字符）");
            });
        }
        catch (Exception ex)
        {
            Logger.Error("启动阶段生成 AGENTS.md 失败", ex);
            ui.Notify("AGENTS.md", $"生成失败，已跳过: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: `Build succeeded`。若报 `FormField` 命名参数不匹配，核对 `ITuiUiService.cs:27` 的 `FormField(string Label, bool IsPassword = false, string? InitialValue = null, bool Required = true, bool Multiline = false)`。

- [ ] **Step 3: 提交**

```bash
git add LubanAgentCli/App/StartupWorkspaceResolver.cs
git commit -m "feat: 新增启动工作区解析器（选/建/授权/生成 AGENTS.md）"
```

---

## Task 4: 接入启动流程并删除旧 CWD 逻辑

**Files:**
- Modify: `LubanAgentCli/App/TerminalGuiApp.cs:167-171`
- Modify: `LubanAgentCli/App/StartupDialog.cs:175-176`
- Modify: `LubanAgentCli/App/StartupRunner.cs:59-63`
- Modify: `LubanAgentCore/Hosting/AgentHostBuilder.cs:134-181`

- [ ] **Step 1: TerminalGuiApp 接入解析器**

`TerminalGuiApp.cs` 中 `_titleService.TitleChanged += _titleChangedHandler;` 之后、`using var root = ...` 之前插入：

```csharp
            // 阶段一点五：解析工作区（选最近活跃非 RAG；无则弹创建表单）。
            // 此时 Services 已就绪、StartupDialog 已关闭、主界面未创建。
            await StartupWorkspaceResolver.ResolveAsync(Services, ui, startup.Notices);
```

注意：`Run` 需支持 `await`。确认方法签名为 `public void Run(string[] args)` 时改为 `public async Task Run(string[] args)`？——不需要：解析器可同步阻塞等待。为避免改动 `Program.cs`，这里用同步等待：

```csharp
            StartupWorkspaceResolver.ResolveAsync(Services, ui, startup.Notices).GetAwaiter().GetResult();
```

> 说明：解析器内部对 `ShowForm`/`RunWithProgress` 的调用都在 UI 线程执行（嵌套模态），不能改成后台线程；`GetAwaiter().GetResult()` 仅等待 `CreateWorkspaceAsync`/`SetCurrentAsync` 等 DB/IO 异步，无死锁风险（无 SynchronizationContext）。

- [ ] **Step 2: StartupDialog 移除步骤⑤**

删除 `StartupDialog.cs` 第 175-176 行：

```csharp
            Report("⑤ 初始化工作区...");
            await StartupRunner.InitializeWorkspaceAsync(Services, Notices);
```

- [ ] **Step 3: 删除 StartupRunner 转发**

删除 `StartupRunner.cs` 第 59-63 行整个方法：

```csharp
    /// <summary>
    /// 初始化工作区。
    /// </summary>
    public static Task InitializeWorkspaceAsync(IServiceProvider sp, List<string> notices)
        => AgentHostBuilder.InitializeWorkspaceAsync(sp, notices);
```

- [ ] **Step 4: 删除 AgentHostBuilder.InitializeWorkspaceAsync**

删除 `AgentHostBuilder.cs` 第 134-181 行整个方法（含 xml 注释与实现）。同时确认文件顶部不再需要因该方法引入的 using（本文件其余代码仍使用 `LubanAgentCore.Services` 等，不要误删）。

- [ ] **Step 5: 构建验证**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: `Build succeeded`。若有 `InitializeWorkspaceAsync` 未定义/未使用报错，检查是否还有遗漏调用方：`git grep -n "InitializeWorkspaceAsync"` 应为空。

- [ ] **Step 6: 提交**

```bash
git add LubanAgentCli/App/TerminalGuiApp.cs LubanAgentCli/App/StartupDialog.cs LubanAgentCli/App/StartupRunner.cs LubanAgentCore/Hosting/AgentHostBuilder.cs
git commit -m "refactor: 启动工作区改由 CLI 侧解析，删除 CWD 静默新建逻辑"
```

---

## Task 5: 退出打印当前工作区/会话

**Files:**
- Modify: `LubanAgentCli/App/TerminalGuiApp.cs:173-178`

- [ ] **Step 1: 捕获并打印**

将 `Run` 的 `finally` 块改为：

```csharp
        finally
        {
            // 终端恢复后再打印，避免被备用屏幕吞掉；Services 此时仍存活（由 TerminalGuiApp.Dispose 释放）
            var workspaceLine = "-";
            var sessionLine = "-";
            if (Services is not null)
            {
                var ws = Services.GetService<IWorkspaceManager>()?.CurrentWorkspace;
                if (ws is not null)
                {
                    workspaceLine = $"{ws.Name} ({ws.RootPath})";
                }

                var session = Services.GetService<ISessionManager>()?.CurrentSession;
                if (session is not null)
                {
                    sessionLine = $"{session.Title} ({session.SessionId})";
                }
            }

            Dispatcher = null;
            application?.Dispose();

            Console.WriteLine($"工作区: {workspaceLine}");
            Console.WriteLine($"会话: {sessionLine}");
        }
```

- [ ] **Step 2: 补 using**

`TerminalGuiApp.cs` 顶部补充（若缺失）：

```csharp
using LubanAgentCore.Services;
using LuBan.AIAgent.Sessions;
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build luban-agent/LubanAgent.slnx`
Expected: `Build succeeded`。若 `IWorkspaceManager`/`ISessionManager` 解析不到，核对命名空间 `LubanAgentCore.Services` / `LuBan.AIAgent.Sessions`。

- [ ] **Step 4: 提交**

```bash
git add LubanAgentCli/App/TerminalGuiApp.cs
git commit -m "feat: 退出后在控制台打印当前工作区与会话"
```

---

## Task 6: 冒烟用例文档 + 手动验证

**Files:**
- Modify: `LubanAgentCli/docs/TUI-SMOKE-TEST.md`

- [ ] **Step 1: 追加用例**

在文档末尾追加（编号接现有最大值顺延）：

```markdown
### 用例 57：启动自动恢复最近活跃工作区

前置：库中存在多个 Normal 工作区，其中一个刚被切换/使用。
步骤：启动 `luban-agent-cli`，等待主界面出现。
预期：无创建弹窗；启动横幅显示 `工作区: <最近活跃>`；页脚显示该工作区最近会话标题；退出后控制台打印工作区与会话两行。

### 用例 58：无工作区时弹出创建表单

前置：库中无任何 Normal 工作区（或全部 Normal 工作区根目录已不存在），且仅有 Rag 工作区。
步骤：启动 `luban-agent-cli`。
预期：弹出"创建工作区"表单，路径预填当前目录；名称/描述可留空。

### 用例 59：路径不存在拒绝提交

步骤：在创建表单路径填一个不存在的目录并确定。
预期：弹出"目录不存在: X"提示，表单重新弹出（已填内容不保留），不创建工作区。

### 用例 60：创建并生成 AGENTS.md

前置：已配置并选择模型；目标目录内无 AGENTS.md。
步骤：创建表单填有效路径、名称留空、AGENTS.md 描述填一段中文并确定。
预期：先弹工作区授权确认（同意）；弹出"生成 AGENTS.md"进度框；完成后进入主界面，启动横幅出现"AGENTS.md 已生成"或"工作区已授权"等提示；目标目录生成 AGENTS.md。

### 用例 61：未选模型跳过生成

前置：未选择模型（`SelectedModel` 为空）。
步骤：创建表单填描述并确定。
预期：弹提示"未选择模型，已跳过 AGENTS.md 生成"；仍创建工作区并进入主界面。

### 用例 62：目录已有 AGENTS.md 跳过

步骤：对已存在 AGENTS.md 的目录执行创建工作区并填描述。
预期：弹提示"已保留原文件，跳过生成"；文件内容不变。

### 用例 63：取消创建进入无工作区主界面

步骤：在创建表单点取消。
预期：进入主界面；启动横幅出现"未选择工作区。请先在磁盘上创建目录，再用 /w -n <路径> 创建"；退出后控制台打印 `工作区: -`。

### 用例 64：退出打印

步骤：正常进入主界面后退出（/exit 或 Ctrl+Q 确认）。
预期：TUI 退出后控制台输出两行：
```
工作区: <名称> (<RootPath>)
会话: <标题> (<SessionId>)
```
```

- [ ] **Step 2: 手动冒烟**

Run: `dotnet run --project luban-agent/LubanAgentCli/LubanAgentCli.csproj`（需可交互终端）
逐条执行用例 57–64，记录实际结果。

- [ ] **Step 3: 提交**

```bash
git add LubanAgentCli/docs/TUI-SMOKE-TEST.md
git commit -m "docs: 补充启动工作区/会话与退出打印冒烟用例"
```

---

## 验证清单（全部完成后）

- [ ] `dotnet build luban-agent/LubanAgent.slnx` 0 error
- [ ] `git grep -n "InitializeWorkspaceAsync"` 无结果
- [ ] 手动冒烟用例 57–64 全部通过（用例 60 依赖真实模型，环境不具备时记为"未验证"并说明）
- [ ] 未提交任何无关文件（`git status` 仅含本计划涉及文件）

## 已知风险

1. **`GetAwaiter().GetResult()` 死锁**：解析器在 UI 线程同步等待 DB 异步。Terminal.Gui 主循环不安装 SynchronizationContext，`.NET ThreadPool` 回调不会回到 UI 线程，故无经典死锁；但若后续有人给 `ResolveAsync` 里的 `ShowForm` 改成后台线程调用会破坏嵌套模态假设。
2. **Codex 受影响**：Task 1 改的是 Core 共享逻辑，桌面端新建会话标题也会变"新会话"。
3. **授权弹窗嵌套**：`EnsureAuthorizedAsync` 的 `AuthorizationPrompt` 由 `StartupDialog` 设置在 `WorkspaceManager` 单例上，向导关闭后仍有效；若未来在向导外重建容器，需重新设置。
4. **无工作区主界面**：首条输入仍会因 `RebuildAgentCoreAsync` 抛"未设置当前工作区"，被 `RootView.SubmitAsync` 捕获显示为"Agent 初始化失败: 未设置当前工作区"——这是预期降级，未额外优化。

---

## 执行偏差记录（2026-09-18 实际落地）

1. **方案口径改为"在 ced5c40 基础上增量补齐"**：另有并发提交 `ced5c40 feat: 启动默认进入最近活跃工作区及其最近会话并回放历史` 与本计划重叠。经确认不再删除 `AgentHostBuilder.InitializeWorkspaceAsync` 与 `StartupRunner.InitializeWorkspaceAsync`，仅旁路增量接入（两者保留为未使用）。
2. **解析器调用点从"向导之后单独弹"改为"启动向导步骤⑤内调用"**：两个 `application.Run()` 之间主循环未运行，`ui.ShowForm`/`RunWithProgress` 依赖运行中的 main loop 泵送 `app.Invoke`，原计划的 `GetAwaiter().GetResult()` 会死锁。故 `StartupDialog` 步骤⑤由 `StartupRunner.InitializeWorkspaceAsync` 改为 `StartupWorkspaceResolver.ResolveAsync(Services, _ui, Notices)`。
3. **Task 4 的删除步骤取消**；`StartupRunner`/`AgentHostBuilder` 旧方法保留。
4. **会话标题统一为"新会话"**（Task 1）与并在冒烟用例 4b/65 中同步。
5. Task 2/3/5/6 按原计划执行；Task 4 仅保留 `StartupDialog` 与 `TerminalGuiApp` 的接入/退出打印（合并为提交 `cb89f89`）。