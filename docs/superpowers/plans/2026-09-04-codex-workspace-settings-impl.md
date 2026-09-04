# LubanAgentCodex 工作区「设置」中心与侧边栏改版 · 实现计划

> **For agentic workers:** 按 Task 顺序实现，每个 Task 用 `- [ ]` 复选框跟踪。每步末尾运行构建验证（本沙箱 NuGet restore 故障，统一用 `--no-restore` 跳过 restore）。

**Goal:** 把「技能 / 规则 / MCP」从只读全局 registry 的三窗重构为直接读写 `.luban-agent` 目录的设置窗，并改版侧边栏（去「⋯」菜单、加「−」删除、双击重命名、底部「⚙ 设置」按钮）；命令面板 `/skill` `/rule` `/mcp` 重定向到设置窗。

**Architecture:** 新增 `SettingsWindow`（三栏 IDE 风：左作用域｜中条目｜右编辑器 + 顶部 Tab）。复用框架层已完成的用户级加载（`GlobalLubanAgentPath` + `SkillLoader`/`RuleEngine`/`MCPRegistry` 双源扫描）。删除统一走已实现的 `WorkspaceManager.DeleteWorkspaceAsync`。旧三窗废弃由设置窗接管。

**Tech Stack:** .NET 10 / Avalonia / MSTest（无自动化测试，验证=构建 + 手动冒烟）

**Spec:** `docs/superpowers/specs/2026-09-04-codex-workspace-settings-design.md`

**验证约定（AGENTS.md：luban-agent 无自动化测试）：** 每个 Task 末尾 `dotnet build --no-restore`；最终 Task 做构建 + 手动冒烟清单。提交步骤列出，但**未经用户明确许可不执行 git commit**。

**工作目录约定：** 仓库根不是 git 仓库，git 命令须进入 `luban-agent/` 子仓库执行；框架改动属 `luban-framework/` 独立仓库。构建命令在 `luban-agent/` 下运行。

---

## 0. 已完成的框架层改动（本计划不再重复，仅记录）

上一阶段已实现并通过编译（`dotnet build LuBan.AIAgent.csproj --no-restore -c Debug -p:GeneratePackageOnBuild=false` → 0/0）：

| 文件 | 改动 |
|------|------|
| `luban-framework/LuBan.AIAgent/GlobalLubanAgentPath.cs` | 新增，暴露 `Root/SkillsDir/RulesDir/McpsDir`，统一解析 `~/.luban-agent/{skills,rules,mcps}` |
| `luban-framework/LuBan.AIAgent/Skills/SkillLoader.cs` | `LoadAll` 用户级改为扫 `~/.luban-agent/skills`，保留旧 `LocalAppData` 路径兼容读取 |
| `luban-framework/LuBan.AIAgent/Rules/RuleEngine.cs` | `LoadFromWorkspace` 先扫 `~/.luban-agent/rules` |
| `luban-framework/LuBan.AIAgent/MCP/MCPRegistry.cs` | `LoadFromWorkspace` 先扫 `~/.luban-agent/mcps` |

> 注意：这些改动在 `luban-framework` 子仓库，尚未提交（需用户许可）。Codex/Cli 通过 NuGet/ProjectReference 引用 `LuBan.AIAgent`——**若走 NuGet 包引用，需重新打包/提升包版本**；若走 ProjectReference 则自动生效。实现阶段需确认 Codex 引用方式，保证 `GlobalLubanAgentPath` 可被 `LubanAgentCodex` 引用到。

---

## 计划前发现（对设计文档的 2 处修正）

1. **`IWorkspaceManager` 未声明 `DeleteWorkspaceAsync`**：该方法只存在于具体类 `WorkspaceManager`（`WorkspaceManager.cs:445`），接口（`cs:75-121`）里没有。设计文档 4.5 写「只需接线，不新增 Core 代码」不准确——须先补一行接口声明（Task 1）。
2. **设计文档 4.1 列定义笔误**：「`Auto,*,Auto,Auto,Auto`」（5 列）与「图标｜名称｜➕｜−」4 个元素不符；实际应为 `Auto,*,Auto,Auto`（4 列，删除按钮替换「⋯」占第 3 列）。

---

## File Structure

| 文件 | 责任 | 动作 |
|------|------|------|
| `LubanAgentCore/Services/WorkspaceManager.cs` | `IWorkspaceManager` 补 `DeleteWorkspaceAsync` 声明 | 改 |
| `LubanAgentCodex/Views/Controls/Sidebar.axaml` | 底部加「⚙ 设置」按钮 | 改 |
| `LubanAgentCodex/Views/Controls/Sidebar.axaml.cs` | 删「⋯」菜单、加「−」删除、双击重命名、设置事件、RAG 删除修正 | 改 |
| `LubanAgentCodex/Views/MainWindow.axaml.cs` | 订阅 `SettingsRequested`，打开设置窗 | 改 |
| `LubanAgentCodex/Views/SettingsWindow.axaml` | 三栏设置窗布局 | 新增 |
| `LubanAgentCodex/Views/SettingsWindow.axaml.cs` | 作用域/Tab/条目/编辑器 + 文件读写 + 热加载 | 新增 |
| `LubanAgentCodex/ViewModels/MainWindowViewModel.cs` | 命令面板 `/skill` `/rule` `/mcp` 重定向 | 改 |
| （可选）`SkillManageWindow`/`RuleManageWindow`/`MCPManageWindow` | 废弃删除 | 删（待确认） |

---

## Task 1: Core — `IWorkspaceManager` 补 `DeleteWorkspaceAsync` 声明

**Files:**
- Modify: `luban-agent/LubanAgentCore/Services/WorkspaceManager.cs`

- [ ] **Step 1: 接口补声明**

在 `IWorkspaceManager` 接口的 `SetCurrentAsync`（`cs:120`）之后、接口闭合 `}`（`cs:121`）之前插入：

```csharp
    /// <summary>
    /// 删除工作区：逻辑删除工作区 + 软删会话 + 清理 RAG 向量索引，并处理当前工作区引用。
    /// </summary>
    /// <param name="workspaceId">工作区ID</param>
    Task<bool> DeleteWorkspaceAsync(string workspaceId);
```

> 实现 `WorkspaceManager.DeleteWorkspaceAsync` 已在 `cs:445` 存在，无需新增实现体，接口补声明即可。

- [ ] **Step 2: 构建验证**

```bash
cd luban-agent && dotnet build LubanAgentCore/LubanAgentCore.csproj --no-restore
```

Expected: 0 错误 0 警告。

- [ ] **Step 3: Commit（需用户许可）**

```bash
cd luban-agent && git add LubanAgentCore/Services/WorkspaceManager.cs && git commit -m "feat: IWorkspaceManager 暴露 DeleteWorkspaceAsync"
```

---

## Task 2: Sidebar.axaml — 底部加「⚙ 设置」按钮

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/Controls/Sidebar.axaml`

- [ ] **Step 1: 在版本号行后插入设置按钮**

`Sidebar.axaml` Grid.Row=4 的底部 `StackPanel`（`cs:64`）内、`LubanAgentCodex` + `v1.0.0` 的水平 `StackPanel`（`cs:65-74`）之后，插入：

```xml
<Button Name="SettingsBtn" Content="⚙ 设置"
        Classes="sidebarFooterBtn" Margin="0,8,0,0"
        HorizontalAlignment="Stretch"
        HorizontalContentAlignment="Center" />
```

> 若 `Classes="sidebarFooterBtn"` 样式不存在，先用内联样式（透明背景、`TextTertiaryBrush` 前景、hover 高亮），与底部其他 footer 文案视觉一致；不强行新增样式类。

- [ ] **Step 2: 构建验证**

```bash
cd luban-agent && dotnet build LubanAgentCodex/LubanAgentCodex.csproj --no-restore
```

Expected: XAML 编译通过（`x:Name`/`Name="SettingsBtn"` 可被 `FindControl<Button>` 解析）。

---

## Task 3: Sidebar.axaml.cs — 删「⋯」、加「−」删除、双击重命名、设置事件、RAG 删除修正

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/Controls/Sidebar.axaml.cs`

- [ ] **Step 1: 新增 `SettingsRequested` 事件 + `InitializeComponent` 挂接设置按钮**

在 `RagInitRequested` 事件（`cs:56`）附近新增：

```csharp
/// <summary>
/// 设置中心打开请求（由底部「⚙ 设置」按钮触发）
/// </summary>
public event EventHandler? SettingsRequested;
```

在 `InitializeComponent`（`cs:63-74`）末尾 `FindControl` 并挂接：

```csharp
if (this.FindControl<Button>("SettingsBtn") is Button settingsBtn)
    settingsBtn.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
```

- [ ] **Step 2: 删除「⋯」菜单按钮与 Flyout**

删除 `wsMenuBtn` 定义（`cs:203-213`）、Flyout 五项构建与 `Click` 处理器（`cs:233-337`）、`wsMenuBtn.Flyout = flyout`（`cs:337`）、`Grid.SetColumn(wsMenuBtn, 3)` 与 `wsGrid.Children.Add(wsMenuBtn)`（`cs:342/346`）、hover 显示/隐藏（`cs:350-351`）。

- [ ] **Step 3: 加「−」删除按钮（替换「⋯」位，第 3 列）**

在 `newSessionBtn`（`cs:216-231`）之后新增：

```csharp
var deleteBtn = new Button
{
    Content = "−",
    FontSize = 16,
    FontWeight = FontWeight.Bold,
    Padding = new Thickness(4, 2),
    Margin = new Thickness(0, 0, 2, 0),
    Background = Brushes.Transparent,
    BorderThickness = new Thickness(0),
    Foreground = Brush.Parse("#858585"),
    VerticalAlignment = VerticalAlignment.Center,
};
ToolTip.SetTip(deleteBtn, "删除工作区");
deleteBtn.Click += async (s, e) =>
{
    try
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null) return;
        var ok = await Dialogs.ShowConfirmAsync(owner, "确认删除",
            $"确定要删除工作区 \"{ws.Name}\" 吗？",
            "删除后将逻辑删除该工作区、其会话及关联的 RAG 向量索引。",
            "确定删除", danger: true);
        if (ok)
        {
            await _workspaceManager!.DeleteWorkspaceAsync(ws.WorkspaceId); // D6：统一逻辑删 + 级联清理
            LoadWorkspaces();
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"删除工作区失败: {ex.Message}");
    }
};
```

`Grid.SetColumn(deleteBtn, 3)` 并 `wsGrid.Children.Add(deleteBtn)`；`wsGrid` 列定义改为 `new ColumnDefinitions("Auto,*,Auto,Auto")`（保持 4 列）。

- [ ] **Step 4: 双击重命名（行内 TextBox）**

将 `wsName` 由局部 `TextBlock` 改为支持双击切换为 `TextBox` 的容器：在 `wsRow.PointerPressed`（`cs:354-358`）中，当 `e.ClickCount == 2` 且事件源非按钮时进入行内编辑——用 `TextBox`（初值 `ws.Name`）替换 `wsGrid` 第 1 列的 `wsName`，`KeyDown`：Enter → `WorkspaceRepository.UpdateAsync` 改名并 `LoadWorkspaces()`；Esc → 还原 `TextBlock`。切换工作区判断保留但排除 `TextBox` 源（现有 `if (e.Source is Button) return` 增加 `|| e.Source is TextBox`）。

> 参考既有 `RenameSessionAsync`（`cs:569`）的持久化写法，但交互是「行内替换」而非弹窗（D2）。

- [ ] **Step 5: 修正 RAG 知识库删除**

`DeleteRagAsync`（`cs:661-691`）中，将：

```csharp
var wsRepo = services.GetRequiredService<WorkspaceRepository>();
await wsRepo.DeleteAsync(w => w.WorkspaceId == model.WorkspaceId);
// 同时清理其下的会话 ... 逐个 SoftDeleteAsync
```

改为：

```csharp
await _workspaceManager!.DeleteWorkspaceAsync(model.WorkspaceId);
```

删除其中冗余的「逐个软删会话」循环（`DeleteWorkspaceAsync` 内部 `SoftDeleteByWorkspaceAsync` 已覆盖）。确认弹窗保留不变。

- [ ] **Step 6: 构建验证**

```bash
cd luban-agent && dotnet build LubanAgentCodex/LubanAgentCodex.csproj --no-restore
```

Expected: 0 错误（此时 `SettingsRequested` 尚未被订阅，MainWindow 侧未接线也不会报错，因事件未使用不产生编译错误）。

---

## Task 4: MainWindow.axaml.cs — 订阅 `SettingsRequested` 并打开设置窗

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/MainWindow.axaml.cs`

- [ ] **Step 1: 订阅事件**

在侧边栏订阅块（`cs:132-138`）加：

```csharp
_sidebar.SettingsRequested += OnSettingsRequested;
```

- [ ] **Step 2: 新增处理器**

在 `OnRagInitRequested`（`cs:373`）附近新增：

```csharp
private async void OnSettingsRequested(object? sender, EventArgs e)
{
    if (_viewModel?.Services == null) return;
    var current = _viewModel.Services.GetRequiredService<IWorkspaceManager>().CurrentWorkspace;
    var win = new SettingsWindow(_viewModel.Services, current);
    await win.ShowDialog(this);
    _sidebar?.SetServiceProvider(_viewModel.Services); // 刷新（可能重命名）
}
```

> `SettingsWindow` 于 Task 5 创建；本 Task 依赖 Task 5 先落地，或与 Task 5 合并提交。实现时按依赖顺序：先 Task 5 骨架，再回填本 Task 引用。

- [ ] **Step 3: 构建验证**

```bash
cd luban-agent && dotnet build LubanAgentCodex/LubanAgentCodex.csproj --no-restore
```

---

## Task 5: SettingsWindow.axaml — 三栏布局骨架

**Files:**
- Add: `luban-agent/LubanAgentCodex/Views/SettingsWindow.axaml`
- Add: `luban-agent/LubanAgentCodex/Views/SettingsWindow.axaml.cs`

- [ ] **Step 1: XAML 布局**

`Window`（约 900×620，`Classes="dlgWindow"`），三栏结构：

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:LubanAgentCodex.Views"
        x:Class="LubanAgentCodex.Views.SettingsWindow"
        Title="设置 · 工作区配置" Width="900" Height="620"
        WindowStartupLocation="CenterOwner">
  <Grid ColumnDefinitions="220,1,*,*" RowDefinitions="*,Auto">
    <!-- 左栏：作用域列表 -->
    <Border Grid.Column="0" Name="ScopePanel" .../>
    <!-- 分隔线 -->
    <Border Grid.Column="1" Width="1" Background="{DynamicResource DividerBrush}"/>
    <!-- 右区：顶部 Tab + 中栏条目 + 右栏编辑器 -->
    <Grid Grid.Column="2" RowDefinitions="Auto,*,Auto">
      <StackPanel Grid.Row="0" Orientation="Horizontal" Name="TabBar"/>
      <Grid Grid.Row="1" ColumnDefinitions="240,1,*">
        <ListBox Grid.Column="0" Name="ItemList"/>
        <Border Grid.Column="1" Width="1" .../>
        <ScrollViewer Grid.Column="2" Name="EditorHost"/>
      </Grid>
      <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right"
                  Name="ActionBar"/>  <!-- [保存] [删除此条目] [应用配置] -->
    </Grid>
  </Grid>
</Window>
```

> 左栏顶部固定「★ 全局」项 + 分隔线 + 工作区列表（`Type != "Rag"`）；Tab 栏放「技能/规则/MCP」三个 ToggleButton。全部控件用 `Name`/`x:Name`，代码-behind 用 `FindControl` 解析（运行时 XAML 约定）。

- [ ] **Step 2: 代码-behind 骨架**

`SettingsWindow.axaml.cs`：`InitializeComponent` 里 `FindControl` 各控件；`public SettingsWindow(IServiceProvider services, WorkspaceInfo? currentWorkspace)`；`public void PreselectTab(TabKind kind)`；枚举 `TabKind { Skill, Rule, Mcp }`。本 Task 先让窗口可打开、Tab 可切换、左栏列出「★ 全局 + 工作区」，编辑器留占位。

- [ ] **Step 3: 构建验证**

```bash
cd luban-agent && dotnet build LubanAgentCodex/LubanAgentCodex.csproj --no-restore
```

Expected: 窗口能编译、`SettingsWindow` 类型可被 MainWindow 引用（Task 4 闭合）。

---

## Task 6: SettingsWindow — 作用域/Tab/条目列表 + 文件读写（skills/rules/mcps）

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/SettingsWindow.axaml.cs`

- [ ] **Step 1: 作用域与路径解析**

私有方法按当前 `SelectedScope` 返回三类目录：

```csharp
private (string skills, string rules, string mcps) ResolveDirs()
{
    if (_isGlobal)
        return (GlobalLubanAgentPath.SkillsDir, GlobalLubanAgentPath.RulesDir, GlobalLubanAgentPath.McpsDir);
    var root = _selectedWorkspace!.RootPath;
    var baseDir = Path.Combine(root, ".luban-agent");
    return (Path.Combine(baseDir, "skills"), Path.Combine(baseDir, "rules"), Path.Combine(baseDir, "mcps"));
}
```

`SelectedScope` 变化时重读对应目录；目录缺失时显示空列表占位「暂无条目，点击 + 新建」。

- [ ] **Step 2: 条目列表（按 Tab）**

- skills：枚举 `skillsDir` 下子目录，每目录含 `SKILL.md`；条目显示 `目录名`。
- rules：枚举 `rulesDir/*.json`；条目显示文件名（去 `.json`）。
- mcps：枚举 `mcpsDir/*.json`；条目显示文件名（去 `.json`）。
- 列表底部 `+ 新建`：创建空条目进入编辑（Task 7 实现具体编辑）。

- [ ] **Step 3: 文件读写基元**

```csharp
private static string ReadText(string path) => File.Exists(path) ? File.ReadAllText(path) : "";
private static void WriteText(string path, string content) {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
}
private static void EnsureScopeDirs((string s, string r, string m) dirs) {
    Directory.CreateDirectory(dirs.s); Directory.CreateDirectory(dirs.r); Directory.CreateDirectory(dirs.m);
}
```

- [ ] **Step 4: 构建验证**

```bash
cd luban-agent && dotnet build LubanAgentCodex/LubanAgentCodex.csproj --no-restore
```

> 依赖：`GlobalLubanAgentPath` 属 `LuBan.AIAgent` 命名空间。若 Codex 通过 NuGet 引用旧版框架包，`GlobalLubanAgentPath` 不可见——需先确认引用方式（见第 0 节），必要时临时以 `Path.Combine(Environment.GetFolderPath(UserProfile), ".luban-agent", ...)` 本地解析，待框架包升级后切回。

---

## Task 7: SettingsWindow — 三类编辑器 + 新建/删除/保存

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/SettingsWindow.axaml.cs`

- [ ] **Step 1: 技能编辑器（frontmatter 表单 + Markdown 正文）**

右栏：`name`（必填）、`description`（必填）、`category`（默认 `custom`）、`triggers`（逗号分隔，可选）四个文本框 + Markdown 多行 `TextBox`（`AcceptsReturn=true`）。保存拼装：

```text
---
name: <name>
description: <description>
category: <category>
triggers: <triggers>
---
<正文>
```

写回 `skills/<name>/SKILL.md`。字段与 `SkillMdParser` 四键（`name/description/category/triggers`）对齐。

- [ ] **Step 2: 规则编辑器（9 字段表单 + 查看 JSON）**

字段：`Id`/`Name`/`Description`/`ActionTypePattern`(默认 `*`)/`TargetPattern`(默认 `*`)/`Action`(默认 `deny`)/`Priority`(默认 `100`)/`Enabled`(开关)/`Content`(可选)。保存 `JsonSerializer.Serialize(config, options)`（`WriteIndented=true`）→ `rules/<id>.json`。提供「查看 JSON」折叠（可编辑原始 JSON，保存前反序列化校验）。

- [ ] **Step 3: MCP 编辑器（6 字段表单 + 查看 JSON）**

字段：`Name`/`Description`/`Enabled`/`Transport`(下拉 `stdio`/`http`/`sse`，默认 `stdio`)/`Command`/`Args`(字符串列表，一行一个)。Transport 切 http/sse 时提示「URL 填在 `Command` 或 `Args[0]`」。保存 `mcps/<name>.json`。

- [ ] **Step 4: 统一校验 + 保存/删除/新建**

保存前校验（规则/MCP：`JsonSerializer.Deserialize<CustomRuleConfig/McpServerConfig>`；技能：frontmatter `name/description` 非空），失败在编辑器下方红字提示不落盘。删除：技能 `Directory.Delete(skills/<name>, true)`，规则/MCP `File.Delete`。新建：创建空条目进入编辑。

- [ ] **Step 5: 构建验证**

```bash
cd luban-agent && dotnet build LubanAgentCodex/LubanAgentCodex.csproj --no-restore
```

> JSON 库二选一并保持一致：项目已全局导入 `System.Text.Json`，优先用它；字段名保持 `CustomRuleConfig`/`McpServerConfig` 属性名（PascalCase）以保证框架加载器能反序列化回相同类。

---

## Task 8: 应用配置热加载 + `PreselectTab` + 命令面板重定向

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/SettingsWindow.axaml.cs`
- Modify: `luban-agent/LubanAgentCodex/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: 「应用配置」按钮**

底部动作栏加「应用配置」按钮，仅当 `SelectedScope` 为「★ 全局」或「当前正在对话的工作区」时可见/可用。点击：

```csharp
var host = _services.GetRequiredService<AgentHostService>();
host.Reset();   // 置空 _agent，下次对话按「先全局→后工作区」重新 LoadFromWorkspace（自愈式，不中断当前上下文）
```

非当前工作区作用域：隐藏该按钮，保存时提示「下次切换到该工作区时生效」。

- [ ] **Step 2: `PreselectTab` 落点**

`PreselectTab(TabKind)` 切到对应 Tab 并刷新条目列表（供命令面板与设置按钮预选）。

- [ ] **Step 3: 命令面板重定向**

`MainWindowViewModel.cs` 中 `ShowSkillManager`/`ShowRuleManager`/`ShowMcpManager`（`cs:333/349/365`）三处，将 `new XxxManageWindow(...).Show()` 改为：

```csharp
var window = new SettingsWindow(Services, workspace);
window.PreselectTab(TabKind.Skill);   // Rule / Mcp 对应
window.Show();
```

> 命令面板在 ViewModel 内、无 `Window` owner，沿用现有 `.Show()`（非模态）。模态打开仅侧边栏按钮路径（MainWindow 作 owner，见 Task 4）。

- [ ] **Step 4: 构建验证**

```bash
cd luban-agent && dotnet build LubanAgentCodex/LubanAgentCodex.csproj --no-restore
```

---

## Task 9: 整体构建 + 手动冒烟 + 旧三窗清理

**Files:**
- （可选删除）`luban-agent/LubanAgentCodex/Views/{SkillManageWindow,RuleManageWindow,MCPManageWindow}.axaml(.cs)`

- [ ] **Step 1: 全量构建**

```bash
cd luban-agent && dotnet build --no-restore
```

Expected: 0 错误。若旧三窗仍被别处引用（命令面板已重定向后应无），先确认引用清零再删除。

- [ ] **Step 2: 手动冒烟清单**

1. 侧边栏无「⋯」菜单，hover 也无。
2. 点「−」弹确认框，取消无变化；确认后工作区消失、会话与 RAG 索引被清理（`rag_file/rag_chunk` 无孤儿）。
3. 双击工作区名行内改名，回车生效、Esc 取消。
4. 点「⚙ 设置」弹设置窗；左栏「★ 全局 + 工作区」、Tab 切类型、编辑保存后 `.luban-agent` 落盘。
5. 全局项保存一个 skill → 点「应用配置」→ 下次对话能加载该技能（`SkillRegistry` 含该条目）。
6. 命令面板 `/skill` 打开设置窗并定位技能 Tab。

- [ ] **Step 3: Commit（需用户许可，可拆分）**

```bash
cd luban-agent && git add -A && git commit -m "feat: 工作区设置中心 + 侧边栏改版（去菜单/删除/重命名/设置入口）"
```

---

## 风险与决策点

- **R-A（框架引用方式）**：`GlobalLubanAgentPath` 在 `LuBan.AIAgent`（`luban-framework` 子仓库）。Codex 若走 NuGet 包引用，需先重新打包框架并提升版本，否则 `GlobalLubanAgentPath` 不可见（Task 6 依赖）。实现前先确认 `LubanAgentCodex.csproj` 是 ProjectReference 还是 PackageReference。
- **R-B（`AgentHostService.Reset` 语义）**：`Reset()` 置空 `_agent`，下次 `RunStreamingAsync` 因 `_agent == null` 触发 `InitializeAsync` 重载配置（自愈式，不打断当前对话），符合设计文档 R4。
- **R-C（命令面板无 owner）**：`MainWindowViewModel` 无 `Window` 引用，故命令面板路径用非模态 `.Show()`；仅侧边栏按钮走 MainWindow 模态 `ShowDialog`。
- **R-D（旧三窗删除）**：确认命令面板/侧边栏引用清零后再删，避免遗漏导致编译失败。
