# Codex 设置弹层整合：工作区配置 + 供应商与模型

- 日期：2026-09-04
- 作者：yswenli / AI 协作
- 关联：`2026-09-04-codex-workspace-settings-design.md`（设置中心基础设计）

## 1. 背景与目标

当前 Codex 端存在**两套互不连通的设置入口**：

- `⚙ 设置` 按钮（Sidebar）→ `SettingsWindow`：管理 `.luban-agent` 目录里的 **技能 / 规则 / MCP**，按作用域（★全局 + 工作区）分层。
- 另一处入口（`MainWindowViewModel` → `ProviderManageWindow`）：管理 **供应商 / 模型**，数据写在全局 `config.json`，**没有工作区维度**。

目标：把「供应商与模型」也并入同一个设置弹层，形成统一的「设置中心」，让用户在一处完成所有配置，并删除旧的独立窗口。

## 2. 已确认的设计决策

| # | 议题 | 决策 |
|---|------|------|
| D1 | 导航形态 | **方案 A：左侧分类导航**（详见 §4），最贴近现有三栏结构 |
| D2 | 供应商/模型作用域 | **仅全局**（维持现状，写 `config.json`，不分工作区） |
| D3 | 旧窗口去留 | **合并进新弹层后删除** `ProviderManageWindow` / `ProviderEditDialog` / `ModelSelectDialog` |

## 3. 现状代码对照（实现依据，字段名逐字准确）

### 3.1 Provider 数据模型（`LubanAgentCore/Configuration/ProviderConfig.cs`）
- `string Name`（小写，唯一标识）
- `string ApiKey`
- `string? BaseUrl`（为空用默认地址）
- `string? DisplayName`
- `List<string> SupportedModels`
- `List<string> CustomModels`（用户自定义模型）
- `int? NetworkTimeoutSeconds`

### 3.2 ConfigManager 可用 API（`LubanAgentCore/Configuration/ConfigManager.cs`）
- `List<ProviderConfig> Providers`
- `void AddProvider(string name, string apiKey, string? baseUrl = null)`（upsert）
- `ProviderConfig? GetProvider(string name)` / `bool HasProvider(string name)`
- `List<string> GetAllModels(string providerName)`（内置预设 + 自定义合并）
- `void AddCustomModel(string providerName, string modelName)`
- `void UpdateCustomModel(string providerName, string oldModelName, string newModelName)`
- `void RemoveCustomModel(string providerName, string modelName)`
- `void SetSelectedModel(string model)`（`"provider:model"` 格式）/ `void ClearSelectedModel()`
- `string? SelectedModel` / `bool HasSelectedModel`
- `void Save()`

### 3.3 模型来源
- 内置预设模型：`ProviderHelper.GetAllModels(providerName, customModels)` / `GetModels`。
- 默认模型：全局唯一，存于 `ConfigManager.SelectedModel`（格式 `provider:model`）。

### 3.4 热生效机制
- `AgentHostBuilder` 将 `IChatClient` 注册为 Scoped，由 `ConfigManager.CreateChatClient()` 按 `SelectedModel` 构造；`LuBanChatClient` 对客户端有缓存。
- 因此**改完供应商/模型后必须调用 `AgentHostService.Reset()`**（与现有「应用配置」一致），才能清缓存、使新的 BaseUrl / ApiKey / 默认模型在下次对话生效。

### 3.5 旧窗口唯一开放点
- `LubanAgentCodex/ViewModels/MainWindowViewModel.cs:326`：`var window = new ProviderManageWindow(Services);`
- 另：`App.axaml.cs:103` 的 `ProviderHelper.Initialize(configuration)` 需保留（`ProviderHelper` 仍被模型列表/显示名使用）。

## 4. 目标 UI 结构（方案 A：左侧分类导航）

```
┌──────────────┬───────────────────────────────────────────────┐
│ 工作区配置    │  [作用域: ★全局 ▼]    ← 仅 workspace 类显示    │
│  ├ 技能       │ ┌─────────────┬─────────────────────────────┐  │
│  ├ 规则       │ │ 条目列表     │ 编辑器                      │  │
│  └ MCP 服务    │ │ (skills/    │ (技能/规则/MCP 编辑器，      │  │
│              │ │  rules/     │  见基础设计文档)              │  │
│ 供应商与模型  │ │  mcps 文件) │                             │  │
│  ├ 供应商      │ └─────────────┴─────────────────────────────┘  │
│  └ 模型       │  提示文本 …………   [删除此条目][应用配置][保存]  │
│              │  （供应商/模型类：顶栏显示「全局 config.json」   │
│              │   +「＋ 新建」，无作用域下拉）                    │
└──────────────┴───────────────────────────────────────────────┘
```

- **左栏 = 分类导航**（替换原 `ScopePanel`）：两组共 5 项——`工作区配置`(技能/规则/MCP)、`供应商与模型`(供应商/模型)。
- **中栏 = 条目列表**：随分类切换内容（skills/rules/mcps 文件；或 provider 列表；或 model 列表）。
- **右栏 = 编辑器**：随分类切换。
- **顶栏（原 `TabBar` 位置）**：
  - 选中「技能/规则/MCP」时 → 显示**作用域 ComboBox**（★全局 + 各工作区，原竖排 `ScopePanel` 改为下拉，避免工作区多时溢出）。
  - 选中「供应商/模型」时 → 显示「全局 config.json」说明 +「＋ 新建」按钮（无作用域）。

## 5. 详细设计

### 5.1 `SettingsTabKind` 枚举扩展
在 `SettingsWindow.axaml.cs` 现有枚举增加两项：
```csharp
public enum SettingsTabKind
{
    Skill,
    Rule,
    Mcp,
    Provider,   // 新增
    Model,      // 新增
}
```
`PreselectTab(SettingsTabKind)` 已存在，可直接用于「从命令/菜单定位到供应商页」。

### 5.2 左栏分类导航（替换 ScopePanel）
- 新增 `BuildCategoryNav()`：两组标题（不可点）+ 5 个可点项。
- 点击项 → 设置 `_tab`、清空 `_selectedItemKey`、切换顶栏（作用域 / 全局说明）、刷新中栏、重建右栏。
- 原 `BuildScopePanel()` / `OnScopeChanged()` 改为：仅当 `_tab ∈ {Skill,Rule,Mcp}` 时，由顶栏作用域 ComboBox 驱动（见 5.3）。

### 5.3 顶栏作用域（原 TabBar 区）
- 工作区配置类：放置 `ComboBox`（`Name="ScopeCombo"`），项为「★ 全局」+ 各工作区名；`SelectionChanged` → 同原 `OnScopeChanged` 逻辑（清空条目、刷新、重建）。
- 供应商/模型类：隐藏 ScopeCombo，显示 `TextBlock`「全局 config.json（供应商配置不分工作区）」+ `NewItemBtn`（文案「＋ 新建供应商」/「＋ 新建模型」）。

### 5.4 供应商编辑器（`_tab == Provider`）
- **中栏**：`ConfigManager.Providers` 列表，显示 `Name` + ApiKey 脱敏（`MaskApiKey` 复用自 `ProviderManageWindow`）+ BaseUrl + 默认标记（`SelectedModel` 以 `name:` 开头）。
- **右栏字段**（复用 `AddField` / `AddToggle` / `AddCombo` 辅助方法）：
  - `Name`：Text；**编辑态只读**（与 `ProviderEditDialog` 一致，Name 即唯一键不可改）。
  - `ApiKey`：密码框（`PasswordChar='*'`）。
  - `BaseUrl`：Text（空=默认）。
  - `DisplayName`：Text（可选）。
  - `NetworkTimeoutSeconds`：Text（数字，空=默认 60）。
  - `CustomModels`：可增删的模型清单（每行一个 `TextBox` + 删除按钮；「＋ 添加模型」新增空行；保存时调用 `AddCustomModel` / `RemoveCustomModel`）。
  - 操作：`[设为默认模型]`（从本供应商模型清单选一个 → `SetSelectedModel("name:model")`）、`[删除此供应商]`、`[保存]`、`[应用配置]`。
- **保存逻辑**：复用 `ConfigManager.AddProvider(name, apiKey, baseUrl)` 写 Name/ApiKey/BaseUrl；DisplayName / NetworkTimeoutSeconds / CustomModels 差异更新（见 §7 映射）；`Save()` 落盘。
- **删除**：`ConfigManager.Providers.Remove(provider)` + `Save()`；若 `SelectedModel` 属该 provider 则 `ClearSelectedModel()`。
- **新建**：创建空白 `ProviderConfig`（Name 占位，编辑后保存）。

> 说明：`ProviderEditDialog` 的「类型下拉 + 内置预设 BaseUrl」逻辑可保留为「新建」时的便捷预填（OpenAI/Azure/Ollama 等），但 Name 允许用户在 custom 下自定义。

### 5.5 模型编辑器（`_tab == Model`）
- **中栏**：跨所有 provider 展平模型列表，每行 `provider : model`，默认模型加「✓ 默认」徽标。来源：`ConfigManager.Providers` + `GetAllModels(name)`。
- **右栏**：
  - 内置模型（不在 `provider.CustomModels`）：只读显示 `Provider` + `Model`，`[设为默认]`（`SetSelectedModel("p:m")`）。
  - 自定义模型（在 `provider.CustomModels`）：可改名（`UpdateCustomModel`）、`[删除]`（`RemoveCustomModel`）、`[设为默认]`。
  - 顶栏「＋ 新建模型」：选 provider（ComboBox）+ 模型名 → `AddCustomModel`。
- 无独立编辑窗体，全部内联，避免保留 `ModelSelectDialog`。

### 5.6 保存与热生效
- 供应商/模型页的「应用配置」按钮调用 `AgentHostService.Reset()`（与 skills/rules/mcps 一致），使新 BaseUrl/ApiKey/默认模型在下次对话生效。
- 供应商/模型为全局配置，`Reset()` 影响全局，无需按工作区判断 `CanApply()`（该判断仅用于 workspace 类）。

## 6. 迁移（删除旧窗口）

| 删除文件 | 说明 |
|----------|------|
| `Views/ProviderManageWindow.axaml` / `.axaml.cs` | 供应商管理窗口 |
| `Views/ProviderEditDialog.axaml` / `.axaml.cs` | 供应商编辑对话框 |
| `Views/ModelSelectDialog.axaml` / `.axaml.cs` | 模型选择对话框 |

- 开放点改造：`MainWindowViewModel.cs:326` 改为：
  ```csharp
  var window = new SettingsWindow(Services, currentWorkspace);
  window.PreselectTab(SettingsTabKind.Provider);
  window.Show();
  ```
- `App.axaml.cs:103` 的 `ProviderHelper.Initialize` **保留**（模型显示名/预设端点仍依赖）。
- `ProviderManageWindow` 中的 `MaskApiKey` 辅助方法需**迁移到 `SettingsWindow`**（§5.4 中栏脱敏复用，源文件删除后不可再引用）。
- 检查是否有 `/provider` 命令或其他引用指向旧窗口，一并改为打开 `SettingsWindow` 并 `PreselectTab(SettingsTabKind.Provider)`。
- 若 `MainWindowViewModel` 中仍有 `ShowProviderManager` 之类方法，重定向到 `OpenSettings(SettingsTabKind.Provider)`。

## 7. 数据模型 ↔ ConfigManager 映射（实现核对表）

| UI 操作 | ConfigManager 调用 |
|---------|--------------------|
| 保存供应商 Name/ApiKey/BaseUrl | `AddProvider(name, apiKey, baseUrl)` |
| 保存 DisplayName / Timeout | 直接改 `provider.DisplayName` / `provider.NetworkTimeoutSeconds` 后 `Save()` |
| 添加自定义模型 | `AddCustomModel(provider, model)` |
| 改名自定义模型 | `UpdateCustomModel(provider, old, new)` |
| 删除自定义模型 | `RemoveCustomModel(provider, model)` |
| 设为默认模型 | `SetSelectedModel("provider:model")` |
| 删除供应商 | `Providers.Remove(p)` + `Save()`；若默认属该 provider 则 `ClearSelectedModel()` |
| 应用配置 | `AgentHostService.Reset()` |

## 8. 风险与注意

- **R1（AVLN3001）**：`SettingsWindow` 已有无参构造函数（`public SettingsWindow()`），新增分类导航不改此约束，保持。
- **R2（样式一致性）**：所有按钮沿用 `Styles/Dialogs.axaml` 的 `dlgPrimary/dlgGhost/dlgDanger`；弹窗根 `Classes="dlgWindow"`。
- **R3（缓存）**：务必在保存后 `Reset()`，否则 `LuBanChatClient` 缓存的旧客户端不会更新 ApiKey/BaseUrl。
- **R4（作用域）**：供应商/模型**不做工作区维度**（D2）。若未来要支持工作区级覆盖，需在 `AppConfig`/`ConfigManager` 增加工作区槽位，本设计不含。
- **R5（可选增强，非必须）**：「刷新模型列表」可复用 `ProviderHelper.RefreshModelsAsync(name, apiKey, baseUrl)`，作为供应商编辑器的「刷新」按钮；本期不强制实现。原 mockup 中的「测试连接」按钮同理为可选增强，现有代码无此能力。

## 9. 验收标准

1. `⚙ 设置` 打开的弹层左侧含 5 项分类导航，点击「供应商」「模型」可切换。
2. 供应商页：可新建/编辑/删除供应商，编辑 ApiKey/BaseUrl/DisplayName/Timeout、增删自定义模型、设为默认模型；保存后 `config.json` 更新。
3. 模型页：可跨 provider 查看全部模型、设为默认、新建/改名/删除自定义模型。
4. 工作区配置（技能/规则/MCP）能力不变，作用域改为顶栏下拉。
5. 旧 `ProviderManageWindow` / `ProviderEditDialog` / `ModelSelectDialog` 已从代码与项目移除，无残留引用。
6. 改完供应商/模型后点「应用配置」，下次对话使用新配置（Reset 生效）。
