# TUI 单一化重构实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 消除 Console/Terminal.Gui 双轨并存：全部 Console 操作迁移到 Terminal.Gui 控件，TUI 先启动、初始化走启动向导弹层，命令异步执行不阻塞 UI 线程。

**Architecture:** 新建 `ITuiUiService` 抽象（Confirm/Notify/Choose/ShowForm/ShowTable 五种模态原语），由 `TuiUiService`（Terminal.Gui Dialog/MessageBox/TableView/ListView）实现；9 个管理命令重写为仅依赖 `ITuiUiService` + `ITuiOutputWriter`；`Program.cs` 初始化逻辑移入 `StartupRunner`，由 `StartupDialog` 向导在 TUI 内执行；删除全部旧 Console 模式代码。

**Tech Stack:** .NET 10.0，Terminal.Gui 2.4.17（已验证 API：`MessageBox.Query(IApplication,...)`、`Dialog.AddButton(Button)`、`Button.Accepting` 事件、`TextField.Secret`、`TableView.Table = new DataTableSource(DataTable)`、`ListView.SetSource(ObservableCollection<T>)`、`Window : Runnable`）。

**验证策略（按已批准规格 §6）：** 本仓库无测试项目，验证 = `dotnet build` 零错误 + grep 审计 + 手动冒烟清单。每个任务必须编译通过再提交。

**构建命令：** `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
（全量构建有预存的 LuBan.AIAgent.dll/LuBan.Logging.dll/LuBan.Orm.dll 元数据缺失错误，与本次无关，始终用 `--no-dependencies`。）

**关键实现约定（所有任务遵守）：**
- `TuiUiService` 构造时记录 `Environment.CurrentManagedThreadId` 为 UI 线程 ID；模态方法在 UI 线程直接执行（Terminal.Gui 嵌套 modal Run），后台线程则 `_app.Invoke` + `ManualResetEventSlim` 等待。
- 命令重写期间（Task 5-14）命令仍在 UI 线程同步执行，`RunModal` 的 UI 线程直跑分支保证不死锁；Task 15 后命令在后台线程执行，走 Invoke 分支。
- 每个命令重写后必须 grep 该文件确认 `Console.`、`AnsiConsole`、`ConsoleUtil`、`ReadPassword`、`WriteInfo(`、`WriteError(`、`WriteSuccess(` 零残留（`GetFriendlyApiErrorMessage` 除外）。

---

### Task 1: ITuiUiService 接口

**Files:**
- Create: `App/ITuiUiService.cs`

- [ ] **Step 1: 创建接口文件**（注意：相对已批准规格有两处微调——移除 `ShowProgress`（YAGNI：启动向导内部自呈现进度，无其他消费方）；新增 `Choose`（对应命令中大量"编号菜单"交互）。规格文档已同步修订。）

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
*文件名： ITuiUiService
*版本号： V1.0.0.0
*唯一标识：TUI UI 服务抽象
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/12
*描述：TUI 模态交互原语抽象（确认/提示/选择/表单/表格），命令层仅依赖此接口
*
*****************************************************************************/
namespace LubanAgent.App;

/// <summary>
/// 表单字段定义。
/// </summary>
/// <param name="Label">字段标签。</param>
/// <param name="IsPassword">是否密码输入（掩码显示）。</param>
/// <param name="InitialValue">初始值。</param>
/// <param name="Required">是否必填（确定时校验非空）。</param>
/// <param name="Multiline">是否多行文本（使用多行编辑区）。</param>
public sealed record FormField(
    string Label,
    bool IsPassword = false,
    string? InitialValue = null,
    bool Required = true,
    bool Multiline = false);

/// <summary>
/// TUI 模态交互服务。所有方法可从任意线程调用：
/// UI 线程直接弹窗（嵌套 modal），后台线程编组到 UI 线程并同步等待结果。
/// </summary>
public interface ITuiUiService
{
    /// <summary>确认对话框。返回 true=用户确认。defaultValue 控制默认按钮（false 时默认"否"，用于删除等危险操作）。</summary>
    bool Confirm(string title, string message, bool defaultValue = false);

    /// <summary>信息提示框（仅"确定"按钮）。</summary>
    void Notify(string title, string message);

    /// <summary>列表选择框。返回选中项索引（0 起），取消返回 null。</summary>
    int? Choose(string title, IReadOnlyList<string> options);

    /// <summary>多字段表单框。返回按字段顺序的值列表，取消返回 null。取消/校验失败时不返回部分值。</summary>
    IReadOnlyList<string>? ShowForm(string title, IReadOnlyList<FormField> fields);

    /// <summary>表格弹窗（TableView，仅查看，"关闭"按钮）。</summary>
    void ShowTable(string title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows);
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
Expected: 0 错误

- [ ] **Step 3: Commit**

```bash
git add App/ITuiUiService.cs
git commit -m "feat: 新增 ITuiUiService 模态交互抽象（Confirm/Notify/Choose/ShowForm/ShowTable）"
```

---

### Task 2: TuiUiService 实现（第一部分：RunModal/Confirm/Notify/Choose）

**Files:**
- Create: `App/TuiUiService.cs`

- [ ] **Step 1: 创建实现文件**

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
*文件名： TuiUiService
*版本号： V1.0.0.0
*唯一标识：TuiUiService 实现
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/12
*描述：基于 Terminal.Gui Dialog/MessageBox/TableView/ListView 的 ITuiUiService 实现，
*支持 UI 线程直跑与后台线程编组等待两种调用方式
*
*****************************************************************************/
using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LubanAgent.App;

/// <summary>
/// <see cref="ITuiUiService"/> 的 Terminal.Gui 实现。
/// 构造于 UI 线程（记录线程 ID）；模态方法在 UI 线程直接嵌套 modal Run，
/// 后台线程经 <see cref="IApplication.Invoke"/> 编组并用信号量同步等待。
/// </summary>
internal sealed class TuiUiService : ITuiUiService
{
    private readonly IApplication _app;
    private readonly int _mainThreadId;

    /// <summary>
    /// 初始化 TUI UI 服务。必须在 UI 线程调用（Init 之后）。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例。</param>
    public TuiUiService(IApplication app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _mainThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>当前线程是否 UI 线程。</summary>
    private bool OnUiThread => Environment.CurrentManagedThreadId == _mainThreadId;

    /// <summary>
    /// 在 UI 线程同步执行模态操作并返回结果。
    /// </summary>
    private T RunModal<T>(Func<T> action)
    {
        if (OnUiThread)
        {
            return action();
        }

        using var done = new ManualResetEventSlim(false);
        T? result = default;
        Exception? error = null;

        _app.Invoke(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });

        done.Wait();
        if (error is not null)
        {
            Logger.Error("TuiUiService 模态操作异常", error);
            throw error;
        }
        return result!;
    }

    /// <inheritdoc/>
    public bool Confirm(string title, string message, bool defaultValue = false)
    {
        return RunModal(() =>
        {
            // defaultValue=false 时"否"在前（默认按钮），危险操作防误触
            var buttons = defaultValue ? new[] { "是", "否" } : new[] { "否", "是" };
            var r = MessageBox.Query(_app, title, message, buttons);
            return defaultValue ? r == 0 : r == 1;
        });
    }

    /// <inheritdoc/>
    public void Notify(string title, string message)
    {
        RunModal<object?>(() =>
        {
            MessageBox.Query(_app, title, message, "确定");
            return null;
        });
    }

    /// <inheritdoc/>
    public int? Choose(string title, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count == 0) return null;

        return RunModal(() =>
        {
            using var dialog = new Dialog
            {
                Title = title,
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 64,
                Height = Math.Min(options.Count + 6, 24)
            };

            var list = new ListView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(2)
            };
            list.SetSource(new ObservableCollection<string>(options));
            list.SelectedItem = 0;
            dialog.Add(list);

            var result = -1;

            var ok = new Button { Text = "确定", IsDefault = true };
            ok.Accepting += (_, _) =>
            {
                result = list.SelectedItem ?? -1;
                dialog.RequestStop();
            };
            var cancel = new Button { Text = "取消" };
            cancel.Accepting += (_, _) =>
            {
                result = -1;
                dialog.RequestStop();
            };
            dialog.AddButton(ok);
            dialog.AddButton(cancel);

            _app.Run(dialog);
            return result >= 0 ? result : (int?)null;
        });
    }
}
```

（ShowForm/ShowTable 在 Task 3 追加到同一文件。）

- [ ] **Step 2: 构建验证**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
Expected: 0 错误（CS0136 等命名冲突不应出现；如 `Dialog`/`Button` 与 Spectre 类型歧义，确认文件顶部未引入 Spectre 命名空间）

- [ ] **Step 3: Commit**

```bash
git add App/TuiUiService.cs
git commit -m "feat: TuiUiService 实现（Confirm/Notify/Choose + RunModal 跨线程编组）"
```

---

### Task 3: TuiUiService 实现（第二部分：ShowForm/ShowTable）

**Files:**
- Modify: `App/TuiUiService.cs`（在 `Choose` 方法后追加两个方法）

- [ ] **Step 1: 追加 ShowForm**

```csharp
    /// <inheritdoc/>
    public IReadOnlyList<string>? ShowForm(string title, IReadOnlyList<FormField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count == 0) return Array.Empty<string>();

        return RunModal(() =>
        {
            // 每字段占：标签 1 行 + 输入 1 行（多行 6 行）+ 间隔 1 行；底部留 3 行给按钮
            var contentHeight = fields.Sum(f => f.Multiline ? 8 : 3);
            using var dialog = new Dialog
            {
                Title = title,
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 72,
                Height = Math.Min(contentHeight + 3, 32)
            };

            var inputs = new List<View>(fields.Count);
            var y = 0;
            foreach (var f in fields)
            {
                dialog.Add(new Label { X = 0, Y = y, Text = f.Required ? $"{f.Label} *" : f.Label });
                y++;

                if (f.Multiline)
                {
                    var tv = new TextView
                    {
                        X = 0,
                        Y = y,
                        Width = Dim.Fill(),
                        Height = 6,
                        Text = f.InitialValue ?? string.Empty
                    };
                    dialog.Add(tv);
                    inputs.Add(tv);
                    y += 6;
                }
                else
                {
                    var tf = new TextField
                    {
                        X = 0,
                        Y = y,
                        Width = Dim.Fill(),
                        Text = f.InitialValue ?? string.Empty
                    };
                    if (f.IsPassword) tf.Secret = true;
                    dialog.Add(tf);
                    inputs.Add(tf);
                    y++;
                }
                y++;
            }

            static string GetValue(View v) => v switch
            {
                TextField tf => tf.Text ?? string.Empty,
                TextView tv => tv.Text ?? string.Empty,
                _ => string.Empty
            };

            List<string>? values = null;

            var ok = new Button { Text = "确定", IsDefault = true };
            ok.Accepting += (_, _) =>
            {
                // 必填校验：失败不关闭对话框
                for (var i = 0; i < fields.Count; i++)
                {
                    if (fields[i].Required && string.IsNullOrWhiteSpace(GetValue(inputs[i])))
                    {
                        MessageBox.ErrorQuery(_app, title, $"{fields[i].Label} 不能为空", "确定");
                        return;
                    }
                }
                values = inputs.Select(GetValue).ToList();
                dialog.RequestStop();
            };
            var cancel = new Button { Text = "取消" };
            cancel.Accepting += (_, _) =>
            {
                values = null;
                dialog.RequestStop();
            };
            dialog.AddButton(ok);
            dialog.AddButton(cancel);

            // 初始焦点放到第一个输入框
            if (inputs.Count > 0) inputs[0].SetFocus();

            _app.Run(dialog);
            return values;
        });
    }
```

- [ ] **Step 2: 追加 ShowTable**

```csharp
    /// <inheritdoc/>
    public void ShowTable(string title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        RunModal<object?>(() =>
        {
            var dt = new System.Data.DataTable();
            foreach (var c in columns)
            {
                dt.Columns.Add(c);
            }
            foreach (var r in rows)
            {
                // 列数不足补空串，超出截断，保证 DataTable 不抛异常
                var cells = columns.Select((_, i) => i < r.Count ? (object)(r[i] ?? string.Empty) : string.Empty).ToArray();
                dt.Rows.Add(cells);
            }

            using var dialog = new Dialog
            {
                Title = title,
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 100,
                Height = 26
            };

            var table = new TableView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(2),
                Table = new DataTableSource(dt)
            };
            dialog.Add(table);

            var close = new Button { Text = "关闭", IsDefault = true };
            close.Accepting += (_, _) => dialog.RequestStop();
            dialog.AddButton(close);

            _app.Run(dialog);
            return null;
        });
    }
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
Expected: 0 错误

- [ ] **Step 4: Commit**

```bash
git add App/TuiUiService.cs
git commit -m "feat: TuiUiService 增加 ShowForm（密码/多行/必填校验）与 ShowTable（TableView）"
```

---

### Task 4: TuiOutputWriter 注入 IUiDispatcher

**Files:**
- Modify: `App/TuiOutputWriter.cs`

- [ ] **Step 1: 改造构造函数与写入编组**（保留 `ITuiOutputWriter` 接口签名不变）

将 `TuiOutputWriter` 类改为：

```csharp
public sealed class TuiOutputWriter : ITuiOutputWriter
{
    private readonly ConversationDocument _doc;
    private readonly IUiDispatcher? _dispatcher;

    /// <summary>
    /// 初始化 TUI 输出写入器。
    /// </summary>
    /// <param name="doc">会话文档模型。</param>
    /// <param name="dispatcher">UI 线程调度器；提供时所有写入编组到 UI 线程（后台线程可安全调用）。</param>
    public TuiOutputWriter(ConversationDocument doc, IUiDispatcher? dispatcher = null)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// 追加 Block；有 dispatcher 时编组到 UI 线程。
    /// </summary>
    private void Append(SystemBlock block)
    {
        if (_dispatcher is null)
        {
            _doc.AppendBlock(block);
            return;
        }
        _dispatcher.Invoke(() => _doc.AppendBlock(block));
    }

    /// <inheritdoc/>
    public void WriteLine(string? text = null, TuiOutputStyle style = TuiOutputStyle.Default)
        => Append(new SystemBlock(text ?? string.Empty, foreground: ToColor(style)));

    /// <inheritdoc/>
    public void WriteHeader(string text)
        => Append(new SystemBlock(text, foreground: BlockColors.Accent, isBold: true));

    /// <inheritdoc/>
    public void WriteSuccess(string text)
        => Append(new SystemBlock(text, foreground: BlockColors.Success));

    /// <inheritdoc/>
    public void WriteError(string text)
        => Append(new SystemBlock(text, foreground: BlockColors.Failure));

    /// <inheritdoc/>
    public void WriteInfo(string text)
        => Append(new SystemBlock(text, foreground: BlockColors.System));

    /// <inheritdoc/>
    public void WriteWarning(string text)
        => Append(new SystemBlock(text, foreground: BlockColors.Accent));

    /// <inheritdoc/>
    public void WriteLine() => WriteLine(string.Empty);

    private static Color ToColor(TuiOutputStyle style) => style switch
    {
        TuiOutputStyle.Default => BlockColors.System,
        TuiOutputStyle.Accent => BlockColors.Accent,
        TuiOutputStyle.Success => BlockColors.Success,
        TuiOutputStyle.Failure => BlockColors.Failure,
        TuiOutputStyle.Warning => BlockColors.Accent,
        _ => BlockColors.System
    };
}
```

（`ITuiOutputWriter` 接口与 `TuiOutputStyle` 枚举保持不变；删除原文件中重复的定义时注意保留一份。）

- [ ] **Step 2: 构建验证**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
Expected: 0 错误

- [ ] **Step 3: Commit**

```bash
git add App/TuiOutputWriter.cs
git commit -m "feat: TuiOutputWriter 支持 IUiDispatcher 编组，后台线程可安全写入"
```

---

### Task 5: CommandBase 新构造函数 + 全命令构造链接线

**Files:**
- Modify: `Commands/CommandBase.cs`
- Modify: `Commands/ProviderCommand.cs`、`ModelCommand.cs`、`SkillCommand.cs`、`RuleCommand.cs`、`MCPCommand.cs`、`SessionCommand.cs`、`StatsCommand.cs`、`WorkCommand.cs`、`RagCommand.cs`（仅构造函数，机械调整）
- Modify: `ViewModels/CommandViewModel.cs`（构造函数 + ResolveCommand）
- Modify: `Views/RootView.cs`（构造函数接线）
- Modify: `App/TerminalGuiApp.cs`（创建 TuiUiService 并传给 RootView）

- [ ] **Step 1: CommandBase 增加 writer/ui，旧 Console helpers 标记 Obsolete 保留（过渡态，Task 14 删除）**

```csharp
public abstract class CommandBase : ICommand
{
    /// <summary>配置管理器</summary>
    protected readonly ConfigManager ConfigManager;
    /// <summary>应用配置</summary>
    protected readonly IConfiguration Configuration;
    /// <summary>TUI 输出写入器（输出到会话文档）</summary>
    protected readonly ITuiOutputWriter Writer;
    /// <summary>TUI 模态交互服务</summary>
    protected readonly ITuiUiService Ui;

    /// <summary>
    /// 创建命令实例
    /// </summary>
    protected CommandBase(ConfigManager configManager, IConfiguration configuration,
        ITuiOutputWriter writer, ITuiUiService ui)
    {
        ConfigManager = configManager;
        Configuration = configuration;
        Writer = writer ?? throw new ArgumentNullException(nameof(writer));
        Ui = ui ?? throw new ArgumentNullException(nameof(ui));
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract Task ExecuteAsync();
    public virtual Task<bool> ExecuteAsync(string[] args) => Task.FromResult(false);

    // ── 以下为旧 Console 实现，过渡态保留，Task 14 删除 ──
    [Obsolete("已迁移 TUI，使用 Ui.ShowForm(IsPassword) 替代")]
    protected static string ReadPassword() { /* 原实现保持不变 */ }
    [Obsolete("已迁移 TUI，使用 Writer.WriteInfo 替代")]
    protected static void WriteInfo(string message) { /* 原实现保持不变 */ }
    [Obsolete("已迁移 TUI，使用 Writer.WriteError 替代")]
    protected static void WriteError(string message) { /* 原实现保持不变 */ }
    [Obsolete("已迁移 TUI，使用 Writer.WriteSuccess 替代")]
    protected static void WriteSuccess(string message) { /* 原实现保持不变 */ }

    // BuildServiceProvider 与 GetFriendlyApiErrorMessage 保持不变
}
```

- [ ] **Step 2: 9 个命令构造函数机械调整**

以 ProviderCommand 为例，其余 8 个完全相同模式（各命令附加的依赖参数保持原位置不变）：

```csharp
// 原：
public ProviderCommand(ConfigManager configManager, IConfiguration configuration)
    : base(configManager, configuration) { }
// 改为：
public ProviderCommand(ConfigManager configManager, IConfiguration configuration,
    ITuiOutputWriter writer, ITuiUiService ui)
    : base(configManager, configuration, writer, ui) { }
```

如 SkillCommand 原构造为 `(ConfigManager, IConfiguration, SkillRegistry)` → 改为 `(ConfigManager, IConfiguration, SkillRegistry, ITuiOutputWriter, ITuiUiService)`，base 调用相应调整。逐文件确认编译通过。

- [ ] **Step 3: CommandViewModel 构造函数 + ResolveCommand 传参**

```csharp
// 字段追加
private readonly ITuiUiService _ui;

// 构造函数改为
public CommandViewModel(
    ConversationDocument doc,
    ConversationViewModel? conversationVm,
    IServiceProvider services,
    IUiDispatcher dispatcher,
    ITuiUiService ui)
{
    _doc = doc ?? throw new ArgumentNullException(nameof(doc));
    _conversationVm = conversationVm;
    _services = services;
    _ui = ui ?? throw new ArgumentNullException(nameof(ui));
    _writer = new TuiOutputWriter(_doc, dispatcher);
}

// ResolveCommand 中所有 new XxxCommand(...) 调用追加 _writer, _ui 两个实参，例如：
nameof(ProviderCommand) => new ProviderCommand(configManager, configuration, _writer, _ui) as TCommand,
nameof(SkillCommand) => (TCommand)(object)new SkillCommand(configManager, configuration,
    _services.GetRequiredService<SkillRegistry>(), _writer, _ui),
// …其余 7 个命令同样处理
```

- [ ] **Step 4: RootView 接线**

```csharp
// RootView 构造函数签名改为：
public RootView(
    IServiceProvider services,
    IUiDispatcher dispatcher,
    ITuiUiService ui,
    IReadOnlyList<string>? startupNotices = null)
// 构造体内：
_commandVm = new CommandViewModel(_doc, _vm, services, dispatcher, ui);
```

- [ ] **Step 5: TerminalGuiApp 创建 TuiUiService 并传入 RootView**

`TerminalGuiApp.Run` 中 `Dispatcher = new TerminalGuiDispatcher(application);` 之后追加：

```csharp
var ui = new TuiUiService(application);
```

`new RootView(_services, Dispatcher, startupNotices)` 改为 `new RootView(_services, Dispatcher, ui, startupNotices)`。

- [ ] **Step 6: 构建验证（应仅余 [Obsolete] 警告，无错误）**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
Expected: 0 错误（Obsolete 警告可接受）

- [ ] **Step 7: Commit**

```bash
git add Commands/ ViewModels/CommandViewModel.cs Views/RootView.cs App/TerminalGuiApp.cs
git commit -m "refactor: CommandBase 注入 ITuiOutputWriter/ITuiUiService，完成构造链接线"
```

---

### Task 6: ProviderCommand 重写（完整示例，其余命令参照此模式）

**Files:**
- Modify: `Commands/ProviderCommand.cs`

**交互点清单（grep 核实）：** :121-128 类型编号菜单 → `Ui.Choose`；:144-149 自定义名称/Key/URL → `Ui.ShowForm`（Key 用 `IsPassword: true`）；:162-163 API Key（`needCustomApiKey` 时明文）→ ShowForm 字段 `IsPassword: !needCustomApiKey`；:173-174 自定义 endpoint → ShowForm 字段带 `InitialValue: defaultUrl`；:217-239 `SelectEndpoint` 编号菜单 → `Ui.Choose`；:257-273 更新菜单编号选择 → `Ui.Choose`；更新流程的 Key/URL 提示 → `Ui.ShowForm`（`Required: false` + `InitialValue` 现值，留空保持原值逻辑保留）；全部 `Console.WriteLine` → `Writer.WriteLine/WriteInfo`；`WriteError/WriteSuccess` → `Writer.WriteError/WriteSuccess`。

- [ ] **Step 1: 重写 ExecuteAddAsync（完整代码，作为全部命令的参照范式）**

```csharp
private Task<bool> ExecuteAddAsync(string[] args)
{
    // 选择 Provider 类型（编号菜单 → Choose 对话框）
    var options = BuiltinProviders
        .Select(p => p.DisplayName)
        .Append("自定义 OpenAI 兼容 API")
        .ToList();
    var chosen = Ui.Choose("添加 Provider", options);
    if (chosen is null) return Task.FromResult(true); // 用户取消

    var choiceIndex = chosen.Value + 1; // 保持与原 1 起始编号一致的后续逻辑

    string providerName;
    string apiKey;
    string? baseUrl = null;

    if (choiceIndex == BuiltinProviders.Length + 1)
    {
        var values = Ui.ShowForm("自定义 Provider", new[]
        {
            new FormField("Provider 名称", InitialValue: "custom"),
            new FormField("API Key", IsPassword: true),
            new FormField("API Base URL", Required: false)
        });
        if (values is null) return Task.FromResult(true);

        providerName = string.IsNullOrWhiteSpace(values[0]) ? "custom" : values[0].Trim().ToLower();
        apiKey = values[1];
        baseUrl = string.IsNullOrWhiteSpace(values[2]) ? null : values[2].Trim();
    }
    else
    {
        var (name, displayName, needCustomEndpoint, needCustomApiKey, warning) = BuiltinProviders[choiceIndex - 1];
        providerName = name;

        if (!string.IsNullOrEmpty(warning))
        {
            Ui.Notify(displayName, warning);
        }

        var defaultUrl = name switch
        {
            "azure" => "https://your-resource.openai.azure.com",
            "ollama" => "http://localhost:11434/v1",
            _ => ""
        };

        var fields = new List<FormField>
        {
            new FormField($"{displayName} API Key", IsPassword: !needCustomApiKey)
        };
        if (needCustomEndpoint)
        {
            fields.Add(new FormField("API 地址", Required: false, InitialValue: defaultUrl));
        }

        var values = Ui.ShowForm($"添加 {displayName}", fields);
        if (values is null) return Task.FromResult(true);

        apiKey = needCustomApiKey ? values[0].Trim() : values[0];

        if (needCustomEndpoint)
        {
            baseUrl = string.IsNullOrWhiteSpace(values[1]) ? defaultUrl : values[1].Trim();
        }
        else
        {
            baseUrl = SelectEndpoint(providerName);
        }
    }

    if (string.IsNullOrEmpty(apiKey))
    {
        Writer.WriteError("API Key 不能为空");
        return Task.FromResult(false);
    }

    try
    {
        ConfigManager.AddProvider(providerName, apiKey, baseUrl);

        var displayName = GetProviderDisplayName(providerName);
        var models = ProviderHelper.GetModels(providerName);

        Writer.WriteSuccess($"Provider '{displayName}' 已添加并保存");

        if (models.Count > 0)
        {
            Writer.WriteInfo($"  支持的模型: {string.Join(", ", models.Take(5))}{(models.Count > 5 ? "..." : "")}");
        }
        else
        {
            Writer.WriteInfo("  提示: 该 Provider 没有预设模型，请使用 /model -add 添加自定义模型");
        }
    }
    catch (Exception ex)
    {
        Logger.Error("ProviderCommand 添加 Provider 异常", ex, providerName);
        Writer.WriteError(ex.Message);
    }

    return Task.FromResult(true);
}

private string? SelectEndpoint(string providerName)
{
    var endpoints = ProviderHelper.GetEndpoints(providerName);
    if (endpoints.Count == 0) return null;
    if (endpoints.Count == 1) return endpoints[0].Url;

    var chosen = Ui.Choose(
        $"{ProviderHelper.GetDisplayName(providerName)} API 地址选择",
        endpoints.Select(e => $"{e.Description} ({e.Url})").ToList());

    return chosen is { } i && i >= 0 && i < endpoints.Count
        ? endpoints[i].Url
        : endpoints[0].Url; // 取消/无效保持原行为：回落到第一个
}
```

注意：`SelectEndpoint` 由 `static` 改为实例方法（需要 `Ui`）。

- [ ] **Step 2: 重写 ExecuteUpdateAsync 及文件内其余交互点**（模式：编号菜单 → `Ui.Choose`；逐字段提示 → `Ui.ShowForm`，更新场景字段 `Required: false` 且 `InitialValue` 为现值，`留空保持原值` 判断逻辑保留；`Console.WriteLine/Write` → `Writer.WriteLine/WriteInfo`；`WriteError/WriteSuccess` → `Writer.WriteError/WriteSuccess`；列表输出若是表格形式（`ConsoleUtil.WriteTable` 或手工对齐循环）→ 收集行列后 `Ui.ShowTable`）

- [ ] **Step 3: 文件级审计**

Run: `rg -n "Console\.|AnsiConsole|ConsoleUtil|ReadPassword|WriteInfo\(|WriteError\(|WriteSuccess\(" Commands/ProviderCommand.cs`
Expected: 无输出（零残留）

- [ ] **Step 4: 构建 + Commit**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
Expected: 0 错误（Obsolete 警告应比 Task 5 减少）

```bash
git add Commands/ProviderCommand.cs
git commit -m "refactor: ProviderCommand 迁移至 ITuiUiService（Choose/ShowForm/密码字段）"
```

---

### Task 7: ModelCommand 重写

**Files:**
- Modify: `Commands/ModelCommand.cs`

**交互点清单（grep 核实）：** :195/:266/:346/:364/:451/:495 编号菜单选择 → `Ui.Choose`；:206/:295/:507/:535 模型名输入 → `Ui.ShowForm` 单字段；:375 确认 → `Ui.Confirm(defaultValue: false)`；更新场景（:284/:364/:495）→ `Ui.Choose` 选模型 + `Ui.ShowForm`（`Required: false` 留空保持原值）。

- [ ] **Step 1: 按 Task 6 模式重写全部交互点**（编号菜单 → Choose；逐字段提示 → ShowForm；y/N → Confirm(defaultValue: false)；Console.WriteLine/MarkupLine → Writer.WriteLine/WriteInfo；表格 → ShowTable）
- [ ] **Step 2: 文件级审计**

Run: `rg -n "Console\.|AnsiConsole|ConsoleUtil|ReadPassword|WriteInfo\(|WriteError\(|WriteSuccess\(" Commands/ModelCommand.cs`
Expected: 无输出

- [ ] **Step 3: 构建 + Commit**

```bash
git add Commands/ModelCommand.cs
git commit -m "refactor: ModelCommand 迁移至 ITuiUiService"
```

---

### Task 8: SkillCommand 重写（含多行字段）

**Files:**
- Modify: `Commands/SkillCommand.cs`

**交互点清单（grep 核实）：** :177-213 添加表单（ID/名称/描述/分类）→ `Ui.ShowForm`；:215-223 提示词模板多行输入（`.` 结束）→ `Ui.ShowForm` 的 `FormField("提示词模板", Multiline: true)`；:232 示例（逗号分隔）→ ShowForm 字段；:285/:384/:558 选择/输入 → `Ui.Choose` 或 `Ui.ShowForm`；:321-345 更新表单（留空保持原值）→ ShowForm（`Required: false`，模板用 `Multiline: true`）；:417 确认 → `Ui.Confirm(defaultValue: false)`；:487 编号菜单 → `Ui.Choose`。

注意：原添加流程是分两次询问（先 ID 校验再填其余），重写为一次表单后把 ID 重复校验移到表单返回后（校验失败 `Writer.WriteError` 并直接返回，不再重新弹出表单——与原"校验失败即终止"行为一致）。

- [ ] **Step 1: 按 Task 6 模式重写全部交互点**
- [ ] **Step 2: 文件级审计**（命令同上，路径换 SkillCommand.cs）
- [ ] **Step 3: 构建 + Commit**

```bash
git add Commands/SkillCommand.cs
git commit -m "refactor: SkillCommand 迁移至 ITuiUiService（含 Multiline 模板字段）"
```

---

### Task 9: RuleCommand 重写

**Files:**
- Modify: `Commands/RuleCommand.cs`

**交互点清单（grep 核实）：** :152-203 添加表单（ID/名称/actionTypePattern/targetPattern/action/priority）→ `Ui.ShowForm`（priority 字段原逻辑 TryParse 失败处理保留）；:259/:366 输入 → `Ui.Choose` 或 ShowForm；:295-313 更新表单（留空保持原值）→ ShowForm（`Required: false`）；:399 确认 → `Ui.Confirm(defaultValue: false)`；:469 编号菜单 → `Ui.Choose`。

- [ ] **Step 1: 按 Task 6 模式重写全部交互点**
- [ ] **Step 2: 文件级审计**（路径换 RuleCommand.cs）
- [ ] **Step 3: 构建 + Commit**

```bash
git add Commands/RuleCommand.cs
git commit -m "refactor: RuleCommand 迁移至 ITuiUiService"
```

---

### Task 10: MCPCommand 重写

**Files:**
- Modify: `Commands/MCPCommand.cs`

**交互点清单（grep 核实）：** :175-208 添加表单（名称/描述/命令/参数）→ `Ui.ShowForm`；:260/:338 输入 → `Ui.Choose` 或 ShowForm；:288-296 更新表单（留空保持原值）→ ShowForm（`Required: false`）；:366 确认 → `Ui.Confirm(defaultValue: false)`；:444 编号菜单 → `Ui.Choose`。

- [ ] **Step 1: 按 Task 6 模式重写全部交互点**
- [ ] **Step 2: 文件级审计**（路径换 MCPCommand.cs）
- [ ] **Step 3: 构建 + Commit**

```bash
git add Commands/MCPCommand.cs
git commit -m "refactor: MCPCommand 迁移至 ITuiUiService"
```

---

### Task 11: SessionCommand 重写

**Files:**
- Modify: `Commands/SessionCommand.cs`

**交互点清单（grep 核实）：** :249 删除确认 → `Ui.Confirm(defaultValue: false)`；其余为输出（会话列表/历史）——列表若是表格形式 → `Ui.ShowTable`（列：会话 ID/名称/消息数/最后活跃），否则 `Writer.WriteLine`。

- [ ] **Step 1: 按 Task 6 模式重写全部交互点**
- [ ] **Step 2: 文件级审计**（路径换 SessionCommand.cs）
- [ ] **Step 3: 构建 + Commit**

```bash
git add Commands/SessionCommand.cs
git commit -m "refactor: SessionCommand 迁移至 ITuiUiService"
```

---

### Task 12: StatsCommand 重写（纯输出）

**Files:**
- Modify: `Commands/StatsCommand.cs`

**交互点清单：** 无 ReadLine（grep 无匹配），全部为输出。统计数字段 → `Writer.WriteLine`；若有表格 → `Ui.ShowTable`。

- [ ] **Step 1: 按 Task 6 模式重写全部输出点**
- [ ] **Step 2: 文件级审计**（路径换 StatsCommand.cs）
- [ ] **Step 3: 构建 + Commit**

```bash
git add Commands/StatsCommand.cs
git commit -m "refactor: StatsCommand 迁移至 ITuiOutputWriter/ShowTable"
```

---

### Task 13: WorkCommand 重写

**Files:**
- Modify: `Commands/WorkCommand.cs`

**交互点清单（grep 核实）：** :123/:158 工作区列表表格（`AnsiConsole.Write(table)`）→ 行列收集后 `Ui.ShowTable`（列：名称/类型/根目录/授权/会话数/最后活跃）；:319 删除确认（`AnsiConsole.Confirm`）→ `Ui.Confirm(defaultValue: false)`；:193-194/:243/:256-259/:308/:337/:356-360/:378 MarkupLine → `Writer.WriteSuccess/WriteInfo/WriteError`。

注意：切换工作区时的授权确认由 `WorkspaceManager.EnsureAuthorizedAsync` 触发，本任务**不动**（Task 16 处理委托注入后自动走 TUI 对话框）。

- [ ] **Step 1: 按 Task 6 模式重写全部交互点**
- [ ] **Step 2: 文件级审计**（路径换 WorkCommand.cs）
- [ ] **Step 3: 构建 + Commit**

```bash
git add Commands/WorkCommand.cs
git commit -m "refactor: WorkCommand 迁移至 ITuiUiService（ShowTable 工作区列表）"
```

---

### Task 14: RagCommand 重写 + CommandBase 旧 helpers 删除 + Commands 层总审计

**Files:**
- Modify: `Commands/RagCommand.cs`
- Modify: `Commands/CommandBase.cs`（删除 [Obsolete] 方法）

**RagCommand 交互点清单（grep 核实）：** :158-160/:193-195/:200-211/:251-264/:298/:381 MarkupLine → `Writer.WriteSuccess/WriteInfo/WriteError/WriteWarning`；:325 表格 → `Ui.ShowTable`；:372 删除确认（`AnsiConsole.Confirm`）→ `Ui.Confirm(defaultValue: false)`。`:188 EnsureAuthorizedAsync` 调用保留（Task 16 处理）。索引（:199 `IndexDirectoryAsync`）无进度显示，保持开始/完成消息即可（Task 15 异步执行后 UI 不卡）。

- [ ] **Step 1: 按 Task 6 模式重写 RagCommand 全部交互点**
- [ ] **Step 2: RagCommand 文件级审计**（路径换 RagCommand.cs）
- [ ] **Step 3: 删除 CommandBase 中 4 个 [Obsolete] 方法**（ReadPassword/WriteInfo/WriteError/WriteSuccess）
- [ ] **Step 4: Commands 层总审计**

Run: `rg -n "Console\.|AnsiConsole|ConsoleUtil|ReadPassword|WriteInfo\(|WriteError\(|WriteSuccess\(" Commands/`
Expected: 无输出（ICommand.cs/CommandBase.cs 的注释提及除外，如有则清理注释）

- [ ] **Step 5: 构建 + Commit**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
Expected: 0 错误 0 Obsolete 警告

```bash
git add Commands/
git commit -m "refactor: RagCommand 迁移至 ITuiUiService；删除 CommandBase 旧 Console helpers"
```

---

### Task 15: CommandViewModel 去 SetOut 桥接 + 命令异步执行

**Files:**
- Modify: `ViewModels/CommandViewModel.cs`

- [ ] **Step 1: 替换 ExecuteManagementCommand 为异步 RunManagementCommand**

删除整个 `ExecuteManagementCommand<TCommand>`（含 `Console.SetOut` 捕获与 `StripAnsi` 方法），替换为：

```csharp
    /// <summary>
    /// 通用管理命令执行器。后台线程执行，UI 线程立即返回；
    /// 命令输出经 TuiOutputWriter（dispatcher 编组）、交互经 ITuiUiService（Invoke 编组）。
    /// </summary>
    private void RunManagementCommand<TCommand>(string[] parts) where TCommand : CommandBase
    {
        try
        {
            var command = ResolveCommand<TCommand>();
            if (command is null)
            {
                _writer.WriteError($"命令 {typeof(TCommand).Name} 初始化失败");
                return;
            }

            var expandedArgs = ExpandSubCommandAliases(parts);

            Task.Run(async () =>
            {
                try
                {
                    if (expandedArgs.Length > 1)
                    {
                        var handled = await command.ExecuteAsync(expandedArgs.Skip(1).ToArray());
                        if (!handled)
                        {
                            await command.ExecuteAsync();
                        }
                    }
                    else
                    {
                        await command.ExecuteAsync();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("管理命令执行异常", ex);
                    _writer.WriteError($"命令执行异常: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            _writer.WriteError($"命令执行异常: {ex.Message}");
        }
    }
```

`TryExecute` 中 9 处 `ExecuteManagementCommand<T>(parts)` 改为 `RunManagementCommand<T>(parts)`。

- [ ] **Step 2: 删除 StripAnsi 与不再使用的 using**（`System.Text.RegularExpressions` 若不再使用则删除；`Regex` 引用清零）

- [ ] **Step 3: 构建 + Commit**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
Expected: 0 错误

```bash
git add ViewModels/CommandViewModel.cs
git commit -m "refactor: 命令执行去 Console.SetOut 桥接，改为后台异步执行"
```

---

### Task 16: WorkspaceManager 授权委托注入

**Files:**
- Modify: `Services/WorkspaceManager.cs`（:344-381 EnsureAuthorizedAsync，:386-405 EnsureConfigDirectoryAsync）

- [ ] **Step 1: 添加 AuthorizationPrompt 属性并重写 EnsureAuthorizedAsync**

```csharp
    /// <summary>
    /// 授权确认提示委托。由 UI 层在启动后设置（TUI 确认对话框）；
    /// 未设置时默认拒绝并记录日志（服务层不依赖任何 UI）。
    /// </summary>
    public Func<WorkspaceInfo, Task<bool>>? AuthorizationPrompt { get; set; }

    /// <summary>
    /// 确保工作区已授权
    /// </summary>
    public async Task<bool> EnsureAuthorizedAsync(WorkspaceInfo workspace)
    {
        if (AuthorizationPrompt is null)
        {
            Logger.Warn($"工作区授权提示未配置，默认拒绝: {workspace.Name}");
            return false;
        }

        // 授权为敏感操作，UI 层默认"否"（需用户明确确认）
        var confirm = await AuthorizationPrompt(workspace);
        if (!confirm)
        {
            Logger.Warn($"工作区未授权，操作取消: {workspace.Name}");
            return false;
        }

        await _repo.UpdateAuthorizationAsync(workspace.WorkspaceId, true);
        workspace.IsAuthorized = true;

        // 同步更新 _current（避免传入对象为副本时状态不一致）
        lock (_currentLock)
        {
            if (_current != null && _current.WorkspaceId == workspace.WorkspaceId)
            {
                _current.IsAuthorized = true;
            }
        }

        AddWorkspaceRootToPathGuard(workspace.RootPath);
        SetCurrentDirectory(workspace.RootPath);
        return true;
    }
```

- [ ] **Step 2: EnsureConfigDirectoryAsync 的 AnsiConsole 错误提示改 Logger**

```csharp
            catch (Exception ex)
            {
                Logger.Error($"无法在工作区目录创建配置文件夹: {ex.Message}", ex);
            }
```

- [ ] **Step 3: 文件级审计**

Run: `rg -n "Console\.|AnsiConsole|ConsoleUtil" Services/WorkspaceManager.cs`
Expected: 无输出

- [ ] **Step 4: 构建 + Commit**

```bash
git add Services/WorkspaceManager.cs
git commit -m "refactor: WorkspaceManager 授权提示改为委托注入，去除 AnsiConsole 依赖"
```

---

### Task 17: DatabaseInitializer 消息返回化

**Files:**
- Modify: `Infrastructure/DatabaseInitializer.cs`（:78/:86/:224/:229 四处 Console.WriteLine）

- [ ] **Step 1: Initialize 签名改为返回消息列表**

```csharp
    /// <summary>
    /// 初始化数据库。返回初始化过程产生的提示消息（由调用方决定呈现方式）。
    /// </summary>
    public static IReadOnlyList<string> Initialize()
    {
        var messages = new List<string>();
        // :78  →  messages.Add($"检测到现有数据库 {Path.GetFileName(dbPath)}（{fi.Length / 1024.0:F1}KB），跳过初始化库、表、种子、视图。");
        // :86  →  messages.Add($"检测到现有数据库 {Path.GetFileName(dbPath)}（{fi.Length / 1024.0:F1}KB），小于阈值 100KB，将执行初始化。");
        // :224 →  messages.Add($"数据库已从 {Path.GetFileName(legacy)} 更名为 {Path.GetFileName(current)}");
        // :229 →  messages.Add($"数据库更名失败: {ex.Message}");
        // ... 其余逻辑不变，方法末尾：
        return messages;
    }
```

`Program.cs` 的 `DatabaseInitializer.Initialize();` 调用点在 Task 19 迁移到 StartupRunner 时消费返回值；本任务先同步修改该调用为 `_ = DatabaseInitializer.Initialize();` 保持编译。

- [ ] **Step 2: 文件级审计**

Run: `rg -n "Console\.|AnsiConsole|ConsoleUtil" Infrastructure/`
Expected: 无输出

- [ ] **Step 3: 构建 + Commit**

```bash
git add Infrastructure/DatabaseInitializer.cs Program.cs
git commit -m "refactor: DatabaseInitializer 控制台输出改为消息返回"
```

---

### Task 18: 删除旧 Console 模式文件

**Files:**
- Delete: `Services/ConsoleAppService.cs`
- Delete: `Commands/AgiCommand.cs`
- Delete: `Commands/BrowseCommand.cs`
- Delete: `UI/EscKeyListener.cs`
- Delete: `UI/ResponseSpinner.cs`
- Modify: `Program.cs`（删除 :233 `services.AddSingleton<ConsoleAppService>();`）
- Modify: `GlobalUsings.cs`（删除 `global using LubanAgent.UI;` 与 `global using Spectre.Console;`）

- [ ] **Step 1: 引用清零核实（必须确认仅 ConsoleAppService/AgiCommand/BrowseCommand 互相引用，无其他消费方）**

Run: `rg -n "ConsoleAppService|AgiCommand|BrowseCommand|EscKeyListener|ResponseSpinner" --type cs`
Expected: 仅出现于以上 5 个待删文件及 Program.cs:233；如有其他引用，先处理再删除

- [ ] **Step 2: 删除 5 个文件 + Program.cs 注册行 + GlobalUsings 两行**

- [ ] **Step 3: 构建 + Commit**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
Expected: 0 错误（如有缺 using 错误，按提示补 `using`）

```bash
git add -A
git commit -m "refactor: 删除旧 Console 模式代码（ConsoleAppService/AgiCommand/BrowseCommand/EscKeyListener/ResponseSpinner）"
```

---

### Task 19: 启动向导 + StartupRunner + Program/TerminalGuiApp 原子切换

**Files:**
- Create: `App/StartupResult.cs`
- Create: `App/StartupDialog.cs`
- Create: `App/StartupRunner.cs`
- Modify: `App/TerminalGuiApp.cs`（Run 重构）
- Modify: `Program.cs`（精简为入口）

- [ ] **Step 1: 创建 StartupResult**

```csharp
namespace LubanAgent.App;

/// <summary>
/// 启动向导结果。
/// </summary>
/// <param name="Success">是否全部初始化成功。</param>
/// <param name="Services">构建完成的 DI 容器（成功时非空）。</param>
/// <param name="Notices">启动提示（成功时非空，渲染到会话区顶部）。</param>
internal sealed record StartupResult(
    bool Success,
    IServiceProvider? Services,
    IReadOnlyList<string>? Notices);
```

- [ ] **Step 2: 创建 StartupRunner（从 Program.cs 移入 BuildConfiguration/PrepareRetrievalAsync/BuildServiceProvider/InitializeWorkspaceAsync/DelegateWorkspaceContextProvider，改造点如下）**

- `BuildConfiguration`：原样移入（无 Console）。
- `PrepareRetrievalAsync` 签名改为 `(IConfiguration configuration, Action<string> report, CancellationToken ct)`：
  - `ConsoleUtil.RunWithStatusAsync` 调用替换为直接 `await mm.EnsureModelAsync(report, ct)`（进度经 report 回调，取消经 ct）；
  - `Console.WriteLine` 系列提示（未知模型/未就绪/放置路径）改为 `report(...)`；
  - `OperationCanceledException` 捕获后 `report("已取消模型下载，检索功能禁用"); return (null, null);`
- `BuildServiceProvider`：原样移入（已无 ConsoleAppService 注册），`DelegateWorkspaceContextProvider` 一并移入。
- `InitializeWorkspaceAsync`：原样移入（无 Console，notices 收集不变）。

- [ ] **Step 3: 创建 StartupDialog**

```csharp
using LubanAgent.Configuration;
using LuBan.AIAgent.Abstractions;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LubanAgent.App;

/// <summary>
/// 启动向导对话框。作为首个 Runnable 运行：后台线程逐步执行初始化
/// （配置→数据库→嵌入模型→DI→工作区），状态行实时更新；
/// 模型下载期间显示取消按钮；失败显示错误并提供重试/退出。
/// </summary>
internal sealed class StartupDialog : Dialog
{
    private readonly string[] _args;
    private readonly IUiDispatcher _dispatcher;
    private readonly ITuiUiService _ui;
    private readonly List<string> _lines = new();
    private readonly Label _statusLabel;
    private readonly Button _cancelDownloadButton;
    private CancellationTokenSource? _downloadCts;
    private bool _downloadInProgress;

    /// <summary>初始化结果（Run 返回后读取）。</summary>
    public StartupResult? Result { get; private set; }

    public StartupDialog(string[] args, IUiDispatcher dispatcher, ITuiUiService ui)
    {
        _args = args ?? throw new ArgumentNullException(nameof(args));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));

        Title = "LuBan Agent CLI - 初始化";
        X = Pos.Center();
        Y = Pos.Center();
        Width = 76;
        Height = 16;

        _statusLabel = new Label { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(2), Text = string.Empty };
        Add(_statusLabel);

        _cancelDownloadButton = new Button { Text = "取消下载", Visible = false };
        _cancelDownloadButton.Accepting += (_, _) => _downloadCts?.Cancel();
        AddButton(_cancelDownloadButton);

        var quit = new Button { Text = "退出" };
        quit.Accepting += (_, _) =>
        {
            Result = new StartupResult(false, null, null);
            RequestStop();
        };
        AddButton(quit);
    }

    /// <inheritdoc/>
    public override void EndInit()
    {
        base.EndInit();
        Task.Run(RunStartupAsync);
    }

    /// <summary>后台线程执行启动流程，完成后关闭对话框。</summary>
    private async Task RunStartupAsync()
    {
        try
        {
            Report("① 加载配置...");
            var configuration = StartupRunner.BuildConfiguration(_args);
            configuration.InitConfigUtil();
            ProviderHelper.Initialize(configuration);

            Report("② 初始化数据库...");
            foreach (var m in DatabaseInitializer.Initialize()) Report(m);

            Report("③ 准备嵌入模型...");
            _downloadCts = new CancellationTokenSource();
            SetDownloadCancelVisible(true);
            var (embedder, modelManager) = await StartupRunner.PrepareRetrievalAsync(
                configuration, Report, _downloadCts.Token);
            SetDownloadCancelVisible(false);

            Report("④ 构建服务容器...");
            var sp = StartupRunner.BuildServiceProvider(configuration, embedder, modelManager);

            // 工作区授权提示 → TUI 确认对话框（全生命周期有效，含后续 /work -switch）
            if (sp.GetRequiredService<IWorkspaceManager>() is WorkspaceManager wm)
            {
                wm.AuthorizationPrompt = AskAuthorizationAsync;
            }

            Report("⑤ 初始化工作区...");
            var notices = new List<string>();
            await StartupRunner.InitializeWorkspaceAsync(sp, notices);

            Report("✓ 初始化完成");
            Result = new StartupResult(true, sp, notices);
            _dispatcher.Invoke(RequestStop);
        }
        catch (Exception ex)
        {
            Logger.Error("启动初始化失败", ex);
            Report($"✗ 初始化失败: {ex.Message}");
            // 失败：用户只能点"退出"（对话框保持打开）
            _dispatcher.Invoke(() => { });
        }
        finally
        {
            _downloadCts?.Dispose();
        }
    }

    /// <summary>追加一行状态并请求重绘（线程安全）。</summary>
    private void Report(string line)
    {
        _lines.Add(line);
        var text = string.Join('\n', _lines.TakeLast(11));
        _dispatcher.Invoke(() =>
        {
            _statusLabel.Text = text;
            _statusLabel.SetNeedsDraw();
        });
    }

    private void SetDownloadCancelVisible(bool visible)
    {
        _dispatcher.Invoke(() =>
        {
            _downloadInProgress = visible;
            _cancelDownloadButton.Visible = visible;
        });
    }

    /// <summary>工作区授权确认（TUI 对话框，默认"否"）。</summary>
    private Task<bool> AskAuthorizationAsync(WorkspaceInfo workspace)
    {
        var message =
            $"工作区: {workspace.Name}\n根目录: {workspace.RootPath}\n\n" +
            "AI Agent 将被授权访问此目录及其子目录：\n" +
            "  - 读取文件\n  - 写入/修改文件（需二次确认）\n  - 执行脚本（需二次确认）\n\n" +
            "是否授权？";
        return Task.FromResult(_ui.Confirm("工作区授权", message, defaultValue: false));
    }
}
```

- [ ] **Step 4: TerminalGuiApp.Run 重构**

```csharp
    /// <summary>
    /// 启动 TUI：先运行启动向导（初始化全部收进 TUI 弹层），成功后进入主界面。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    public void Run(string[] args)
    {
        IApplication? application = null;
        try
        {
            application = Application.Create();
            application.Init();

            ConfigureDriver(application);
            Dispatcher = new TerminalGuiDispatcher(application);
            var ui = new TuiUiService(application);

            // 启动向导：初始化（配置/数据库/嵌入模型/DI/工作区授权）
            var startup = new StartupDialog(args, Dispatcher, ui);
            application.Run(startup, OnUnhandledException);

            if (startup.Result is not { Success: true } result)
            {
                return; // 用户退出或初始化失败
            }

            Services = result.Services;
            using var root = new RootView(result.Services!, Dispatcher, ui, result.Notices);
            application.Run(root, OnUnhandledException);
        }
        finally
        {
            Dispatcher = null;
            application?.Dispose();
        }
    }
```

同步调整：删除 `_services` 字段与原构造函数；`Services` 改为 `public IServiceProvider? Services { get; private set; }`。

- [ ] **Step 5: Program.cs 精简**

```csharp
namespace LubanAgent;

/// <summary>
/// 程序入口
/// </summary>
class Program
{
    /// <summary>
    /// 程序主入口。仅做可交互终端检测（唯一 Console 使用点），随后进入 TUI。
    /// </summary>
    static Task<int> Main(string[] args)
    {
        if (!TerminalGuiApp.CanRunInteractive())
        {
            Console.Error.WriteLine("luban-agent-cli 需要可交互终端运行，检测到输入/输出被重定向或无终端窗口。");
            return Task.FromResult(1);
        }

        new TerminalGuiApp().Run(args);
        return Task.FromResult(0);
    }
}
```

删除 Program.cs 中已移走的全部辅助方法，保留文件头注释。

- [ ] **Step 6: 构建 + Commit**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
Expected: 0 错误（StartupDialog 缺 using 时按编译提示补充）

```bash
git add App/StartupResult.cs App/StartupDialog.cs App/StartupRunner.cs App/TerminalGuiApp.cs Program.cs
git commit -m "feat: TUI 先启动，初始化收进启动向导弹层（Program/TerminalGuiApp 原子切换）"
```

---

### Task 20: FooterDataProvider 后台获取 git 分支

**Files:**
- Modify: `Services/FooterDataProvider.cs`

- [ ] **Step 1: GitBranch 改为后台刷新 + 缓存读取**

```csharp
    private string? _cachedBranch;
    private DateTime _branchCacheTime;
    private int _refreshing;
    private static readonly TimeSpan BranchCacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>当前工作目录的 git 分支名。UI 线程只读缓存；过期时后台刷新（首次未就绪返回 "—"）。</summary>
    public string GitBranch
    {
        get
        {
            if ((_cachedBranch is null || DateTime.Now - _branchCacheTime >= BranchCacheTtl)
                && Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
            {
                Task.Run(() =>
                {
                    try
                    {
                        _cachedBranch = TryGetGitBranch();
                        _branchCacheTime = DateTime.Now;
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _refreshing, 0);
                    }
                });
            }

            return _cachedBranch ?? "—";
        }
    }
```

（消除 UI 线程同步 `Process.Start` + `WaitForExit(1000)` 的周期性卡顿。）

- [ ] **Step 2: 构建 + Commit**

```bash
git add Services/FooterDataProvider.cs
git commit -m "fix: git 分支获取改后台刷新，消除页脚重绘时 UI 线程起进程卡顿"
```

---

### Task 21: 终验（构建 + 全仓审计 + 手动冒烟）

- [ ] **Step 1: 全量构建**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent-cli\LubanAgentCli.csproj" --no-dependencies`
Expected: 0 错误 0 警告（除预存框架元数据问题外）

- [ ] **Step 2: Console 残留总审计**

Run: `rg -n "Console\.|AnsiConsole|ConsoleUtil|Spectre" --type cs`
Expected: 仅 `Program.cs` 的 `Console.Error.WriteLine`（CanRunInteractive 失败分支）与 `TerminalGuiApp.CanRunInteractive` 内 `Console.IsInputRedirected/IsOutputRedirected/WindowWidth/WindowHeight`（无 TUI 可用时的必要检测）；其余无输出

- [ ] **Step 3: 手动冒烟清单（用户执行，逐项确认）**

1. 启动：向导逐行显示 ①-⑤，完成后进入主界面，启动提示（工作区名）出现在会话区顶部
2. 首次运行（或删除嵌入模型目录后）：③ 显示下载进度，"取消下载"可中断且继续启动（检索禁用提示）
3. 新目录首次启动：弹"工作区授权"框，默认"否"；选"是"后继续
4. `/help`、`/clear`、`/mode` 正常
5. `/work -list` 弹 TableView 表格；`/work -switch xxx` 切换成功
6. `/provider -add` 弹类型选择，选 kimi 后弹表单，API Key 输入显示掩码；`/model -list` 表格
7. `/mcp -add` 表单四字段；`/skill -add` 含多行模板编辑
8. 危险确认（如 `/session` 删除）：确认框默认"否"
9. 输入文本与 Agent 流式对话正常，Esc 取消，Tab 视图切换，Shift+Tab 模式切换，Ctrl+L 重绘，Ctrl+Q 退出
10. **原输入延迟问题复测**：连续快速输入，字符应即时回显（Console 竞争源已全部移除）

- [ ] **Step 4: Commit**

```bash
git commit --allow-empty -m "chore: TUI 单一化重构终验（构建零错误 + Console 残留审计清零）"
```

---

## Self-Review 记录

- **规格覆盖：** §1 启动向导→Task 19；§2 ITuiUiService→Task 1-3；§3 命令重写+异步→Task 5-15；§4 WorkspaceManager→Task 16；§5 FooterDataProvider/清理→Task 17-18、20；§6 验证→Task 21。DatabaseInitializer/Program.cs 提示去向（审查发现）→Task 17/19。
- **规格微调：** 移除 `ShowProgress`（YAGNI，启动向导内部自呈现进度）；新增 `Choose`（编号菜单模式）；规格文档已同步修订。
- **类型一致性：** `ITuiUiService`/`FormField`/`ITuiOutputWriter`/`TuiOutputWriter(doc, dispatcher)`/`CommandBase(cm, cfg, writer, ui)`/`CommandViewModel(doc, vm, services, dispatcher, ui)`/`RootView(services, dispatcher, ui, notices)`/`StartupResult` 跨任务签名一致。
- **无占位符：** 全部新建设施含完整代码；命令任务含交互点清单（精确行号）+ 完整参照范式（Task 6）。
