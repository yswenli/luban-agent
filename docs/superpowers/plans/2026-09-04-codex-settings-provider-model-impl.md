# Codex 设置弹层整合：实现计划（工作区配置 + 供应商与模型）

- 日期：2026-09-04
- 关联 spec：`2026-09-04-codex-settings-provider-model-design.md`
- 关联基础实现：`2026-09-04-codex-workspace-settings-impl.md`（Task 1–9 已完成，本计划在其之上扩展）

## 0. 与 spec 的一处布局取舍（实现细化）

spec §5.3 写「供应商/模型类顶栏显示 [＋ 新建]」。为减少改动、保持与现行中栏底部 `NewItemBtn` 一致，**本计划保留中栏底部的 `NewItemBtn`（仅动态改文案为「＋ 新建供应商」/「＋ 新建模型」），顶栏对供应商/模型类只显示「全局 config.json（供应商配置不分工作区）」说明文本**，不再在顶栏另放新建按钮。其余完全遵循 spec。

## 1. 改动文件清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `LubanAgentCodex/Views/SettingsWindow.axaml` | 改 | 左栏标题「配置作用域」→「设置分类」；保留 `ScopePanel` 容器（改名 `CategoryPanel`），`TabBar` 容器复用为顶栏（作用域 Combo / 全局说明）；窗口 `Title` 改为「设置中心」 |
| `LubanAgentCodex/Views/SettingsWindow.axaml.cs` | 改（主） | 枚举扩展；分类导航；Provider/Model 列表与编辑器；保存/删除；`MaskApiKey` 迁入 |
| `LubanAgentCodex/ViewModels/MainWindowViewModel.cs` | 改 | `ShowProviderManager` 重定向为 `OpenSettings(SettingsTabKind.Provider)` |
| `LubanAgentCodex/Views/ProviderManageWindow.axaml[.cs]` | 删 | 供应商管理窗口 |
| `LubanAgentCodex/Views/ProviderEditDialog.axaml[.cs]` | 删 | 供应商编辑对话框（`Builtin` 预设表迁入 `SettingsWindow`） |
| `LubanAgentCodex/Views/ModelSelectDialog.axaml[.cs]` | 删 | 模型选择对话框（改为内联） |

保留：`App.axaml.cs` 的 `ProviderHelper.Initialize`（模型显示名/预设端点仍依赖）。

## 2. Task 拆解

### Task 1 — 枚举扩展 + 窗口标题
- `SettingsWindow.axaml.cs` 的 `SettingsTabKind` 增加 `Provider`、`Model`（spec §5.1）。
- `SettingsWindow.axaml` 的 `Title` 改为「设置中心」（去掉「工作区配置」限定）。
- 风险 R1：保持无参 `SettingsWindow()` 构造函数不破坏。

### Task 2 — XAML 左栏改「设置分类」+ 顶栏容器
- 左栏标题文本 `配置作用域` → `设置分类`。
- 将 `Name="ScopePanel"` 的 `StackPanel` 保留（代码侧改名引用为 `CategoryPanel`），`Name="TabBar"` 的 `StackPanel` 保留作为顶栏容器（后续注入作用域 Combo 或全局说明文本）。
- 不变：中栏 `ItemList` + 底部 `NewItemBtn`、右栏 `EditorHost`、底栏动作 `DeleteItemBtn`/`ApplyBtn`/`SaveBtn`。

### Task 3 — 引入 ConfigManager 引用
- `SettingsWindow` 增加字段 `private ConfigManager? _configManager;`，在 `LoadAsync()`（或带参构造函数）通过 `_services!.GetRequiredService<ConfigManager>()` 获取，供 Provider/Model 列表与编辑器使用。
- 复刻 `ProviderManageWindow.MaskApiKey` 为 `SettingsWindow` 的 `private static string MaskApiKey(string)`（spec §6 迁移要求）。

### Task 4 — 左栏分类导航（替换 BuildScopePanel）
- 新增 `BuildCategoryNav()` 渲染两组共 5 项（不可点的组标题 + 可点项）：`工作区配置`(技能/规则/MCP)、`供应商与模型`(供应商/模型)。
- 可点项点击 → 设置 `_tab`、清空 `_selectedItemKey`、调用 `BuildTopBar()` → `RefreshItems()` → `BuildEditor()`。
- 原 `BuildScopePanel()` 删除；`OnScopeChanged()` 逻辑改为仅由顶栏作用域 Combo 驱动（Task 5）。

### Task 5 — 顶栏（作用域 Combo / 全局说明）
- 新增 `BuildTopBar()` 替代原 `BuildTabBar()`：
  - `_tab ∈ {Skill,Rule,Mcp}`：`TabBar` 容器内放 `ComboBox Name="ScopeCombo"`，项为「★ 全局」+ 各工作区名；`SelectionChanged` → 设置 `_selectedWorkspace` → `OnScopeChanged()`（清空条目/刷新/重建）。
  - `_tab ∈ {Provider,Model}`：放 `TextBlock`「全局 config.json（供应商配置不分工作区）」。
- `PreselectTab(kind)` 改为调用 `BuildCategoryNav()` + `BuildTopBar()` + `RefreshItems()` + `BuildEditor()`。

### Task 6 — 中栏列表扩展（Provider/Model）
- `EnumerateItems()` 增加分支：
  - `Provider`：返回 `_configManager.Providers.Select(p => p.Name)`。
  - `Model`：跨 provider 展平 `GetAllModels(p.Name)`，每行 `p.Name:model`；默认模型在 `RefreshItems` 后用 `SelectedModel` 前缀标记「✓ 默认」。
- `RefreshItems()` 对 Provider 类需用 `MaskApiKey` 脱敏显示 ApiKey（可在列表项模板里加第二行）。为简单起见，Provider 类列表项直接显示 `Name` + 脱敏 ApiKey 文本（用 `string` 组合或自定义类）。

### Task 7 — 右栏：供应商编辑器
- `BuildEditor()` 的 `switch` 增加 `Provider` → `BuildProviderEditor(host, key)`、`Model` → `BuildModelEditor(host, key)`。
- `BuildProviderEditor` 字段（复用 `AddField/AddToggle/AddCombo`）：
  - `Name`：`AddField`，**新建态可编辑、编辑态只读**（用 `_isNewProvider` 标志；新建态 Name 占位）。
  - `Type`（仅新建态）：`AddCombo`，复用 `ProviderEditDialog.Builtin` 预设表（OpenAI/Azure/.../custom），选中预填 Name（custom 可改）+ BaseUrl 默认（spec §5.4 说明）。
  - `ApiKey`：`PasswordBox` 风格（`PasswordChar='*'`，用 `TextBox` + `PasswordChar`）。
  - `BaseUrl`、`DisplayName`、`NetworkTimeoutSeconds`（数字，空=60）。
  - `CustomModels`：可增删清单，每行 `TextBox` + 删除按钮；「＋ 添加模型」新增空行。
  - 按钮：`[设为默认模型]`（弹内联 Combo 选本 provider 模型 → `SetSelectedModel`）、`[删除此供应商]`（走 `DeleteItemBtn`/`DeleteItemAsync`）。
- `Builtin` 预设表与 `TryGetDefaultEndpoint`（调用 `ProviderHelper.GetEndpoints`）从 `ProviderEditDialog` 迁入 `SettingsWindow`。

### Task 8 — 右栏：模型编辑器（内联）
- `BuildModelEditor`：
  - 内置模型（`GetAllModels` 但不在 `provider.CustomModels`）：只读 `Provider` + `Model` + `[设为默认]`。
  - 自定义模型（在 `provider.CustomModels`）：`[改名]`（`UpdateCustomModel`）、`[删除]`（`RemoveCustomModel`）、`[设为默认]`。
  - 顶栏/中栏「＋ 新建模型」：选 provider（Combo）+ 模型名 → `AddCustomModel`。
- 无独立窗体（替代 `ModelSelectDialog`）。

### Task 9 — 保存 / 删除 / 应用
- `OnSaveClick` 的 `switch` 增加 `Provider` → `SaveProviderAsync()`、`Model` → `SaveModelAsync()`（Model 多为内联操作，Save 仅落盘提示）。
- `SaveProviderAsync`（映射见 spec §7）：
  - 新建：`AddProvider(name, apiKey, baseUrl)`；
  - DisplayName / NetworkTimeoutSeconds 直接改 `provider` 字段；
  - CustomModels 差异：`AddCustomModel` / `RemoveCustomModel`；
  - `Save()`。
- `DeleteItemAsync` 增加 Provider 分支：`Providers.Remove(p)` + `Save()`；若 `SelectedModel` 以 `p.Name:` 开头则 `ClearSelectedModel()`；Model 类删除走内联按钮而非 `DeleteItemBtn`。
- `ApplyConfig()` 已调 `AgentHostService.Reset()`，对 Provider/Model（全局）始终生效；`CanApply()` 增加：`_tab ∈ {Provider,Model}` 返回 `true`（spec §5.6）。
- `UpdateActionBar()`：Provider/Model 类显示 ApplyBtn、按需禁用 DeleteItemBtn（Model 类由内联控制，可隐藏 `DeleteItemBtn`）。

### Task 10 — 迁移开放点 + 删除旧文件
- `MainWindowViewModel.cs:324` `ShowProviderManager` 改为：
  ```csharp
  private void ShowProviderManager(string[] args) => OpenSettings(SettingsTabKind.Provider);
  ```
- 删除 `ProviderManageWindow` / `ProviderEditDialog` / `ModelSelectDialog` 共 6 个文件（`.axaml` + `.axaml.cs`）。
- 全局搜索确认无残留 `new ProviderManageWindow(...)` / `ProviderEditDialog` / `ModelSelectDialog` / `ProviderEditResult` / `ModelItem` 引用（如有其他引用一并改 `OpenSettings(SettingsTabKind.Provider)`）。
- 确认 `App.axaml.cs` 的 `ProviderHelper.Initialize` 保留。

### Task 11 — 编译验证
- 正常环境执行：`cd D:\WorkBench\Walle\luban\luban-agent && dotnet build LubanAgentCodex\LubanAgentCodex.csproj -c Debug`。
- 关注：AVLN3001（无参构造保留）、命名空间引用 `LubanAgentCore.Configuration`（ConfigManager/ProviderConfig 已 via 现有 using）、样式类 `dlgPrimary/dlgGhost/dlgDanger`。
- 沙箱/环境故障（见前轮 NETSDK1060）无法在本环境验证，需在用户本机执行。

## 3. 验收对照（spec §9）

1. 左栏 5 项分类导航，点「供应商」「模型」可切换 ✓（Task 4/5）
2. 供应商：新建/编辑/删除、改 ApiKey/BaseUrl/DisplayName/Timeout、增删自定义模型、设默认；落盘 config.json ✓（Task 7/9）
3. 模型：跨 provider 查看、设默认、新建/改名/删除自定义 ✓（Task 8）
4. 技能/规则/MCP 能力不变，作用域改顶栏下拉 ✓（Task 5）
5. 旧窗口 6 文件删除、无残留引用 ✓（Task 10）
6. 应用配置后下次对话生效（Reset）✓（Task 9）

## 4. 风险

- **R1**：无参 `SettingsWindow()` 保留（Task 1）。
- **R2**：按钮样式统一 `dlgPrimary/dlgGhost/dlgDanger`；窗口 `Classes="dlgWindow"` 不变。
- **R3**：保存后必须 `Reset()`，否则 `LuBanChatClient` 缓存不更新 ApiKey/BaseUrl（Task 9）。
- **R4**：供应商/模型不做工作区维度（D2），`CanApply()` 对这两类恒 `true`。
- **R5（可选增强，非强制）**：「测试连接」「刷新模型」(`ProviderHelper.RefreshModelsAsync`) 不纳入本期，保持聚焦合并入口 + 删旧窗口。
