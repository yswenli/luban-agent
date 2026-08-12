# TUI 单一化重构设计

日期：2026-08-12
状态：已获用户批准
项目：luban-agent-cli（.NET 10.0，Terminal.Gui 2.4.17）

## 背景与目标

当前项目存在 Console / Terminal.Gui 双轨并存问题：

- `CommandViewModel.ExecuteManagementCommand` 通过 `Console.SetOut` 全局重定向捕获管理命令输出，再做 ANSI 剥离。
- 管理命令（MCP/Skill/Rule/Model/Provider 的 `-add` 流程）在 TUI 内直接 `Console.ReadLine()` 交互，与 Terminal.Gui 输入循环竞争同一控制台输入缓冲区。
- 启动初始化（嵌入模型下载经 `ConsoleUtil.RunWithStatusAsync`，含后台 `Console.ReadKey` 线程；工作区授权经 `AnsiConsole.Confirm`）在 TUI 启动前以控制台模式运行。
- 旧 Console 模式代码（ConsoleAppService、AgiCommand 控制台 Agent、BrowseCommand、EscKeyListener、ResponseSpinner）在 TUI 路由下已不可达。

目标：

1. 全部 Console 相关操作改为 Terminal.Gui 控件操作，消除双轨并存。
2. TUI 先启动，初始化准备工作通过 Terminal.Gui 弹层呈现。
3. 消除命令执行对 UI 线程的阻塞（当前 `ExecuteManagementCommand` 在 UI 线程 `.GetAwaiter().GetResult()`）。

## 已确认的决策

| 决策点 | 结论 |
|---|---|
| 旧 Console 模式代码 | 全部删除（程序仅支持 TUI，不可交互终端报错退出） |
| 管理命令逐步交互 | Terminal.Gui 模态对话框（多字段表单 + 确定/取消） |
| 表格输出 | Terminal.Gui TableView 弹窗 |
| 启动初始化呈现 | 启动向导模态弹窗，逐项显示进度 |
| 总体方案 | 方案 A：统一 ITuiUiService 抽象 |

## 1. 启动流程（TUI 先行 + 启动向导）

```
Main:
  CanRunInteractive() 失败 → Console.Error 一行报错退出（全程序唯一 Console 残留）
  TerminalGuiApp.Run(args):
    Application.Create().Init() → TUI 启动
    弹出模态 StartupDialog，后台线程逐步执行，状态行实时更新：
      ① 加载配置（BuildConfiguration / InitConfigUtil / ProviderHelper）
      ② 初始化数据库（DatabaseInitializer）
      ③ 嵌入模型（PrepareRetrieval：需下载时同对话框显示进度，可取消）
      ④ 构建服务容器（BuildServiceProvider）
      ⑤ 初始化工作区（InitializeWorkspace：需授权时弹授权确认框）
    任一步失败 → 对话框显示错误 + [重试] / [退出]
    全部成功 → 关闭对话框 → 用所得 ServiceProvider 创建 RootView → application.Run(root)
```

- 配置/DB/DI 本无控制台 I/O，按"TUI 先启动"原则全部收进向导后台线程执行。
- 启动提示（工作区名称等）作为 notices 传入 RootView，渲染在会话区顶部（沿用现有机制）。
- 嵌入模型下载进度替换 `ConsoleUtil.RunWithStatusAsync`（含其后台 `Console.ReadKey` 线程）为对话框进度显示 + 取消按钮（取消经 CancellationToken 传递）。

## 2. ITuiUiService 抽象（App 层，纯 BCL 类型）

```csharp
public interface ITuiUiService
{
    bool Confirm(string title, string message, bool defaultValue = false);
    IReadOnlyList<string>? ShowForm(string title, IReadOnlyList<FormField> fields); // null = 取消
    void ShowTable(string title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows);
    ITuiProgress ShowProgress(string title, string initialStatus);
    void Notify(string title, string message);
}

public sealed record FormField(string Label, bool IsPassword = false, string? InitialValue = null, bool Required = true);

public interface ITuiProgress : IDisposable
{
    void Update(string status);
    bool Cancelled { get; }
}
```

- 实现 `TuiUiService`：Terminal.Gui `Dialog` + `TextField`（密码用 `Secret = true`）+ `TableView` + `Button`。
- 命令在后台线程执行（见 §3），service 方法内部经 `IUiDispatcher.Invoke` 弹模态框 + `ManualResetEventSlim` 同步等待结果。
- 进度框为非模态 Dialog，后台线程经 dispatcher 更新状态文本；取消按钮设置 Cancelled。
- 命令层只依赖 `ITuiUiService` + `ITuiOutputWriter`，编译期保证无 Console/AnsiConsole 残留。

## 3. 命令层重写（去 SetOut 桥接 + 异步执行）

- **CommandViewModel**
  - 删除 `Console.SetOut` 捕获与 `StripAnsi`。
  - `TryExecute` 立即返回，命令在后台 Task 执行；异常经 dispatcher 回写错误 Block。
  - `ResolveCommand` 构造函数注入 `ITuiOutputWriter` + `ITuiUiService`。
- **TuiOutputWriter**：注入 `IUiDispatcher`，所有写入编组到 UI 线程，后台线程可安全调用。
- **CommandBase**：删除 `ReadPassword` / `WriteInfo` / `WriteError` / `WriteSuccess`（全部 Console 实现）；保留 `GetFriendlyApiErrorMessage`；构造函数接收 `ITuiOutputWriter` + `ITuiUiService`。
- **9 个管理命令逐个替换**（Provider / Model / Skill / Rule / MCP / Session / Stats / Work / Rag；Help/Clear/Mode 已迁移命令不动）：
  - 分步 `Console.ReadLine` → `ShowForm` 一次性表单
  - y/N 确认 → `Confirm`
  - `AnsiConsole.Write(table)` / `ConsoleUtil.WriteTable` → `ShowTable`
  - `AnsiConsole.MarkupLine` → `ITuiOutputWriter`
  - `ReadPassword` → `ShowForm`（`IsPassword = true`）
- **删除**：ConsoleAppService、AgiCommand、BrowseCommand、EscKeyListener、ResponseSpinner。
- **保留**：SpinnerService（TUI 页脚 spinner 使用）。

## 4. WorkspaceManager 授权解耦

- `EnsureAuthorizedAsync` 内的 AnsiConsole 提示改为注入委托 `Func<WorkspaceInfo, Task<bool>>? AuthorizationPrompt`；TUI 启动后由 UI 层设置为确认对话框实现（未设置时默认拒绝并记录日志，保证服务层无 UI 依赖）。
- `EnsureConfigDirectoryAsync` 的 AnsiConsole 错误提示 → Logger 记录 + 错误经返回值/事件上报。

## 5. 顺手修复与清理

- `FooterDataProvider.GitBranch`：git 进程获取改后台线程 + 缓存，UI 线程只读缓存（消除 UI 线程同步起进程 + `WaitForExit(1000)` 的周期性卡顿）。
- 移除 agent-cli 的 `global using Spectre.Console` 及全部直接使用；`ConsoleUtil` 调用点清零。
- `Console.Error` 仅保留在 `CanRunInteractive` 失败分支。
- LuBan.Common 框架内部的 ConsoleUtil/Spectre 不动（框架层不属本次范围）。

## 6. 验证

- `dotnet build` 零错误。
- 手动冒烟清单：
  - 启动向导逐步显示，模型下载进度与取消
  - 工作区授权确认框
  - `/help`、`/work -list` TableView 弹窗、`/mcp -add` 表单、Provider API Key 密码掩码
  - Agent 流式对话、Esc 取消、Tab 视图切换、Shift+Tab 模式切换、Ctrl+Q 退出
  - 命令后台执行期间界面可正常重绘/滚动
- 原输入延迟问题在重构完成后复测（Console 竞争源全部移除后验证是否消失）。

## 非目标（YAGNI）

- 不实现 TuiSnapshot 无头快照测试。
- 不做每个命令的完整 TUI 导航屏幕（方案 C）。
- 不改动 luban-framework 内部实现（WorkspaceManager 委托注入除外）。
