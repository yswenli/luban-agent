# LubanAgentCodex 管理窗口 TODO 实现设计

- 日期：2026-09-02
- 范围：`LubanAgentCodex` 桌面端三个管理窗口共 11 处 `// TODO` 空实现事件处理器
- 目标：补全全部功能，消除 TODO，业务能力向 CLI（`ProviderCommand`/`WorkCommand`/`RagCommand`）看齐，交互桌面化

## 1. 背景与现状

`LubanAgentCodex/Views` 下三个管理窗口的事件处理器为空体 `// TODO`：

| 窗口 | 文件 | TODO 数 | 事件 |
|------|------|---------|------|
| ProviderManageWindow | `Views/ProviderManageWindow.axaml.cs` | 4 | OnAdd/OnEdit/OnDelete/OnSetDefault |
| WorkManageWindow | `Views/WorkManageWindow.axaml.cs` | 3 | OnAdd/OnDelete/OnAuthorize（OnSwitch 已实现） |
| RagManageWindow | `Views/RagManageWindow.axaml.cs` | 4 | OnCreate/OnIndex/OnSearch/OnDelete |

后端能力已齐备：
- `ConfigManager`：`AddProvider/GetProvider/Providers/Save/SetSelectedModel/GetAllModels/SelectedModel/HasProvider`
- `IWorkspaceManager`：`CreateWorkspaceAsync/SwitchWorkspaceAsync/SetCurrentAsync/GetUserWorkspacesAsync/EnsureAuthorizedAsync/CurrentWorkspace`
- `IRetrievalService`（framework 注入）：`IndexDirectoryAsync(rootPath,glob,force)` 返回 report；`SearchAsync(query,topK)` 返回命中列表
- `WorkspaceRepository`（`BaseRepository<DbWorkspace>`）：`LogicDeleteAsync/DeleteAsync/GetAllAsync`
- `RagFileRepository`/`RagChunkRepository`：`DeleteByWorkspaceAsync`
- `SessionRepository`：`SoftDeleteByWorkspaceAsync`
- `ProviderHelper`（静态）：`GetModels/GetDisplayName/GetEndpoints/RefreshModelsAsync`

依赖确认：
- `App.axaml.cs:106` 已设 `wm.AuthorizationPrompt = _ => Task.FromResult(true)`，授权自动通过
- `MainWindow.axaml.cs:416` 用 `Services.GetService<IRetrievalService>()` 并判空——`IRetrievalService` 仅在嵌入模型就绪（`embedder != null`）时注册（`AgentHostBuilder.cs:111`），未就绪时返回 null
- `Sidebar.axaml.cs` 已用 `GetRequiredService<WorkspaceRepository>()`，仓储已注册

参考实现：CLI `ProviderCommand.cs`/`WorkCommand.cs`/`RagCommand.cs`、`Sidebar.axaml.cs` 的工作区新建/删除/授权。

## 2. 设计决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| 保真度 | 能力向 CLI 看齐，交互桌面化 | 用户要求"实现全部功能" |
| RAG 搜索结果展示 | 管理窗口内切换视图（结果替换知识库列表 + 返回按钮） | 用户选定 |
| 对话框组织 | 方案B：新增 `ProviderEditDialog`/`ModelSelectDialog`，单字段复用 `RenameDialog`（加 Title/Watermark 参数） | 复用优先，少新增文件 |
| RAG 索引/搜索的工作区上下文 | 临时 `SetCurrentAsync` + `try/finally` 恢复原工作区 | 检索按 CurrentWorkspace 隔离（MainWindow 注释）；不永久切换避免改主窗口 |
| 授权非当前工作区 | 先 `SetCurrentAsync` 切换再 `EnsureAuthorizedAsync` | 避免 cwd 与 `_current` 不一致；与 CLI switch+authorize 等价 |
| `IRetrievalService` 获取 | `GetService` + 判空 | 注册有条件（embedder 未就绪时不注册，MainWindow 同款判空） |
| `RagFileRepository`/`RagChunkRepository` 获取 | 直接 `new` | 未注册 DI（CLI `WorkCommand.cs:314` 同款） |

## 3. 文件清单

### 新增
- `Views/ProviderEditDialog.axaml` + `Views/ProviderEditDialog.axaml.cs`
- `Views/ModelSelectDialog.axaml` + `Views/ModelSelectDialog.axaml.cs`

### 改动
- `Views/RenameDialog.axaml(.cs)`：加可选 `DialogTitle`（默认"重命名"）与 `Watermark`（占位提示）
- `Views/ProviderManageWindow.axaml(.cs)`：实现 4 事件；列表项补 ApiKey 脱敏/BaseUrl/状态列
- `Views/WorkManageWindow.axaml(.cs)`：实现 3 事件
- `Views/RagManageWindow.axaml(.cs)`：实现 4 事件；axaml 加"返回列表"按钮与 `ResultListBox`（叠加显隐），支持搜索结果视图切换

### 配套改动（深度审查发现的前置依赖）
- `LubanAgentCore/Configuration/ConfigManager.cs`：新增 `ClearSelectedModel()` 公开方法（置 `_config.SelectedModel = null; Save();`），供 Provider 删除后清空选中模型（规避 CLI `SetSelectedModel("")` 抛异常的瑕疵）
- `LubanAgentCodex/App.axaml.cs`：`BuildServiceProvider` 之后补 `ProviderHelper.Initialize(configuration)`，使 `ProviderHelper.GetEndpoints` 可用（ProviderEditDialog 预填 BaseUrl 依赖）

## 4. 组件设计

### 4.1 ProviderEditDialog

用途：Provider 添加与编辑共用表单。

字段控件：
- `ComboBox TypeCombo`：内置类型列表（复刻 CLI `BuiltinProviders` 的 15 项 + "自定义 OpenAI 兼容 API"）
- `TextBox NameBox`：选内置类型时锁定为该类型名（小写），选"自定义"时可自由输入
- `TextBox ApiKeyBox`：明文或 PasswordBox
- `TextBox BaseUrlBox`：选内置类型时预填默认端点（见下"BaseUrl 预填"），可改
- `TextBlock ErrorText`：校验错误提示
- OK/Cancel 按钮

**BaseUrl 预填（重要约束）**：`ProviderHelper.GetEndpoints(name)` **依赖 `ProviderHelper.Initialize()`，而 Codex 端未调用**（仅 CLI `StartupDialog.cs:136` 调用），直接调用抛 `InvalidOperationException`。因此：
- **配套改动**：在 `App.axaml.cs` `BuildServiceProvider` 之后补一行 `ProviderHelper.Initialize(configuration)`（对齐 CLI；`appsettings` 无 `LuBanAgent:Providers` 节时 `_providerConfigs` 为空、`GetEndpoints` 返回空列表，不抛异常）
- 预填逻辑：`var eps = ProviderHelper.GetEndpoints(name)`，取 `eps.FirstOrDefault()?.Url` 预填；为空则留空由用户填写（azure/ollama 等可用 CLI 同款默认值：`azure`→`https://your-resource.openai.azure.com`，`ollama`→`http://localhost:11434/v1`，内置于对话框代码）
- 可用性确认：`GetModels`/`GetDisplayName`/`GetAllModels`/`RefreshModelsAsync` 不依赖 `Initialize`（用静态字典），Codex 安全（`InputBox.axaml.cs:138` 已用 `RefreshModelsAsync`）

构造：
- `ProviderEditDialog()`：添加模式，类型下拉默认第一项
- `ProviderEditDialog(ProviderConfig existing)`：编辑模式，`NameBox` 只读预填 `existing.Name`、`ApiKeyBox` 预填 `existing.ApiKey`、`BaseUrlBox` 预填 `existing.BaseUrl`，类型下拉隐藏或锁定

返回：`ProviderEditResult { string Name, string ApiKey, string? BaseUrl }`，Cancel 返回 null。

校验：name 非空（自定义模式）、apiKey 非空；失败显示 ErrorText 不关闭。

### 4.2 ModelSelectDialog

用途：选择某 Provider 的模型作为默认。

字段控件：
- `ListBox ModelList`：模型列表（传入）
- OK/Cancel 按钮

构造：`ModelSelectDialog(IList<string> models, string? currentModel = null)`，`currentModel` 高亮标记"已选"。

返回：选中模型名（string），Cancel 返回 null。

### 4.3 RenameDialog 改动

加 `public string? DialogTitle { get; set; }`，默认 null。**注意 axaml 有两处"重命名"硬编码需都处理**：`Window.Title`（L4）与内容区标题 `TextBlock`（L19）。改动方式：给内容区标题 `TextBlock` 加 `Name="TitleTextBlock"`；在 `InitializeComponent` 或属性 setter 里 `this.Title = DialogTitle ?? "重命名"` 且 `TitleTextBlock.Text = DialogTitle ?? "重命名"`。同时为支持不同占位提示，可加 `public string? Watermark { get; set; }` 设置 `NameTextBox.PlaceholderText`（如 glob 场景"留空索引全部文件"）。

复用场景：
- RAG 索引 glob 输入：`new RenameDialog("") { DialogTitle = "索引文件匹配模式", Watermark = "留空索引全部文件" }`
- RAG 搜索查询输入：`new RenameDialog("") { DialogTitle = "搜索查询", Watermark = "输入检索关键词" }`

> 注：`RenameDialog` 当前 `Result` 为 string?，glob/查询复用语义可接受（单文本输入+确定/取消）。

## 5. 各窗口事件实现

### 5.1 ProviderManageWindow

依赖注入：构造已接收 `IServiceProvider`，取 `ConfigManager`（已注入）。

- **OnAdd**：
  1. `var dlg = new ProviderEditDialog()` → `await dlg.ShowDialog<ProviderEditResult?>(this)`
  2. result 为 null 则返回
  3. `try { ConfigManager.AddProvider(result.Name, result.ApiKey, result.BaseUrl); LoadProviders(); await Dialogs.ShowInfoAsync(this, "Provider 已添加"); }`
  4. `catch (Exception ex) { Logger.Error(...); await Dialogs.ShowErrorAsync(this, ex.Message); }`

- **OnEdit**：
  1. 取选中 `ProviderItem`，`ConfigManager.GetProvider(item.Name)` 得 `ProviderConfig`
  2. `new ProviderEditDialog(provider)` → ShowDialog
  3. result 为 null 返回
  4. `ConfigManager.AddProvider(provider.Name, result.ApiKey, result.BaseUrl)`（已存在即更新 ApiKey/BaseUrl）
  5. 刷新 + 提示

- **OnDelete**：
  1. 取选中 provider
  2. `var ok = await Dialogs.ShowConfirmAsync(this, "删除 Provider", $"确定删除 {displayName} 吗？", okText:"删除", danger:true)`
  3. !ok 返回
  4. `ConfigManager.Providers.RemoveAt(idx); ConfigManager.Save();`
  5. 若 `ConfigManager.SelectedModel?.StartsWith($"{provider.Name}:")` 则 `ConfigManager.ClearSelectedModel()`（**需在 `ConfigManager` 新增 `ClearSelectedModel()` 公开方法**，内部置 `_config.SelectedModel = null; Save();`。ConfigManager 属 LubanAgentCore 本仓库，直接改即生效，无需打包。注：CLI `ProviderCommand.cs:345` 调 `SetSelectedModel("")` 会因 `IsNullOrWhiteSpace` 校验抛异常——此为 CLI 瑕疵，桌面端用新方法规避）
  6. 刷新 + 提示

- **OnSetDefault**：
  1. 取选中 provider
  2. `var models = ConfigManager.GetAllModels(provider.Name)`
  3. 若空 → `Dialogs.ShowInfoAsync(this, "该 Provider 无可用模型，请先添加模型")` 返回
  4. `new ModelSelectDialog(models, ConfigManager.SelectedModel)` → ShowDialog
  5. result 为 null 返回
  6. `ConfigManager.SetSelectedModel($"{provider.Name}:{result}")`
  7. 刷新 + 提示

列表项 `ProviderItem` 补字段：`ApiKeyMasked`（前4…后4，≤8 显示 `****`）、`BaseUrl`（空显示"(默认)"）、`Status`（当前选中显示"✓ 默认"）。

### 5.2 WorkManageWindow

依赖：`IWorkspaceManager`、`WorkspaceRepository`、`SessionRepository` 用 `Services.GetRequiredService<T>()`（已注册 singleton）；`RagFileRepository`/`RagChunkRepository` **未注册 DI**，按 CLI 惯例直接 `new RagFileRepository()`/`new RagChunkRepository()`（参照 `WorkCommand.cs:314`）。

- **OnAdd**：
  1. `var dlg = new NewWorkspaceDialog()` → `await dlg.ShowDialog<bool?>(this)`
  2. ok != true 返回
  3. `var ws = await _workspaceManager.CreateWorkspaceAsync(dlg.WorkspacePath!, dialog.WorkspaceName, "Normal")`
  4. `LoadWorkspaces()` + 提示"已创建工作区，可点切换使用"

- **OnDelete**：
  1. 取选中 `WorkspaceItem`
  2. `ShowConfirmAsync(this, "删除工作区", $"删除 '{name}' 将同时删除其下所有会话和索引，确认？", okText:"删除", danger:true)`
  3. !ok 返回
  4. 级联：`await sessionRepo.SoftDeleteByWorkspaceAsync(id)` + `await ragFileRepo.DeleteByWorkspaceAsync(id)` + `await ragChunkRepo.DeleteByWorkspaceAsync(id)` + `await wsRepo.LogicDeleteAsync(w => w.WorkspaceId == id)`
  5. 若 `_workspaceManager.CurrentWorkspace?.WorkspaceId == id` → 提示"当前工作区已删除，请切换到其他工作区"
  6. `LoadWorkspaces()`

- **OnAuthorize**（先切换再授权，避免 cwd 与 `_current` 不一致）：
  1. 取选中 workspace；若已 `IsAuthorized` → `ShowInfoAsync(this, "工作区已授权")` 返回
  2. 若选中工作区不是当前工作区，`await _workspaceManager.SetCurrentAsync(id)` 先切换（正确同步 `_current`/cwd/PathGuard；授权后该工作区成为当前，符合"授权以便使用"直觉，与 CLI `/work -switch x` + `/work -authorize` 等价）
  3. `var ok = await _workspaceManager.EnsureAuthorizedAsync(ws)`（`AuthorizationPrompt` 自动 true）
  4. ok → 提示"已授权"（若非原当前工作区，附注"已切换为该工作区"）；否则提示"授权失败"
  5. `LoadWorkspaces()`

### 5.3 RagManageWindow

依赖：`IWorkspaceManager`、`SessionRepository`、`WorkspaceRepository` 用 `Services.GetRequiredService<T>()`；`IRetrievalService` **注册有条件（`AgentHostBuilder.cs:111` 仅 `embedder != null` 才注册），嵌入模型未就绪时不存在，必须 `Services.GetService<IRetrievalService>()` 判空**（参照 `MainWindow.axaml.cs:416`），为 null 时提示"嵌入模型未就绪"；`RagFileRepository`/`RagChunkRepository` 直接 `new`（未注册 DI）。

**工作区上下文约定**：`IRetrievalService` 按 `CurrentWorkspace` 隔离切块（`MainWindow.axaml.cs:415` 注释）。因此对选中 Rag 工作区的索引/搜索，须**临时 `SetCurrentAsync` 切换、操作完 `try/finally` 恢复原工作区**（参照 `MainWindow.axaml.cs:419-438`），不永久切换，避免意外改动主窗口当前工作区。

- **OnCreate**：
  1. `new NewWorkspaceDialog()` → ShowDialog<bool?>
  2. ok != true 返回
  3. `CreateWorkspaceAsync(path, name, "Rag")`
  4. `LoadRagWorkspaces()` + 提示

- **OnIndex**：
  1. `var retrieval = Services.GetService<IRetrievalService>()`；为 null → `ShowInfoAsync(this, "嵌入模型未就绪，无法索引")` 返回
  2. 取选中 RagItem 的 workspaceId 与 workspace
  3. `var previous = _workspaceManager.CurrentWorkspace`
  4. `try { await _workspaceManager.EnsureAuthorizedAsync(workspace); await _workspaceManager.SetCurrentAsync(id);` 临时切换
  5. `var dlg = new RenameDialog("") { DialogTitle = "索引文件匹配模式", Watermark = "留空索引全部文件" }` → ShowDialog；result 为 glob（可空=全部）
  6. `var report = await retrieval.IndexDirectoryAsync(workspace.RootPath, result, force:false)`
  7. `ShowInfoAsync(this, $"索引完成：扫描 {report.ScannedFiles}，新增 {report.NewFiles}，更新 {report.UpdatedFiles}，跳过 {report.SkippedFiles}，切块 {report.TotalChunks}")`
  8. `} catch (Exception ex) { Logger.Error; ShowErrorAsync }` 
  9. `finally { if (previous != null) await _workspaceManager.SetCurrentAsync(previous.WorkspaceId); }` 恢复原工作区

- **OnSearch**：
  1. `var retrieval = Services.GetService<IRetrievalService>()`；为 null → `ShowInfoAsync(this, "嵌入模型未就绪，无法搜索")` 返回
  2. 取选中 workspaceId 与 workspace；`var previous = _workspaceManager.CurrentWorkspace`
  3. `try { await _workspaceManager.SetCurrentAsync(id);` 临时切换（搜索读向量库，可不授权）
  4. `var dlg = new RenameDialog("") { DialogTitle = "搜索查询", Watermark = "输入检索关键词" }` → ShowDialog；result 为 query；空 → `finally` 恢复后返回
  5. `var results = await retrieval.SearchAsync(result, topK:5)`
  6. 切换为结果视图：隐藏 `_ragListBox`、显示 `_resultListBox`（`ItemsSource = results.Select(r => new SearchResultItem{...})`），显示"返回列表"按钮
  7. 空 results → `ShowInfoAsync(this, "未找到相关文档")` 但仍切结果视图显示空提示
  8. `} catch { ShowErrorAsync } finally { if (previous != null) await _workspaceManager.SetCurrentAsync(previous.WorkspaceId); }`

- **OnDelete**：与 WorkManageWindow.OnDelete 级联逻辑相同。

**搜索结果视图切换实现（两个 ListBox 叠加，按状态显隐）**：
- axaml 工具栏加 `Button Name="BackButton" Content="← 返回列表" IsVisible="False"`
- axaml 在 `RagListBox` 同位置叠加 `ListBox Name="ResultListBox" IsVisible="False"`
- 代码维护 `bool _isSearchResult` 状态
- `OnSearch` 后：`_isSearchResult=true`；`RagListBox.IsVisible=false`；`ResultListBox.IsVisible=true`；`BackButton.IsVisible=true`；`ResultListBox.ItemsSource=结果`
- `BackButton.Click` → `_isSearchResult=false`；`RagListBox.IsVisible=true`；`ResultListBox.IsVisible=false`；`BackButton.IsVisible=false`；`LoadRagWorkspaces()`
- 选用两个 ListBox 叠加而非 DataTemplate 切换，避免 ItemTemplate 复杂度

`SearchResultItem` 字段：`FilePath`、`SymbolName`（可空显示"-"）、`LineRange`（`StartLine-EndLine`，无则"-"）、`Content`（截断前 200 字）。

## 6. 错误处理与日志

- 所有事件处理器 `try/catch`
- catch：`Logger.Error("<窗口>.<事件> 异常", ex, 标识)` + `await Dialogs.ShowErrorAsync(this, ex.Message)`
- 参照 `Sidebar.axaml.cs` 的 `ShowErrorAsync` 模式

## 7. 验证

- 无自动化测试（AGENTS.md：luban-agent 无测试）
- 验证 = `dotnet build luban-agent/luban-agent.slnx` 通过 + 手动冒烟：
  - Provider：添加（内置/自定义）、编辑、删除（含选中模型属该 provider 场景，验证 ClearSelectedModel）、设为默认
  - Work：新建、删除（含当前工作区）、授权（含已授权、非当前工作区切换授权）
  - Rag：创建、索引（空 glob/指定 glob）、搜索（有结果/无结果/返回）、删除、**嵌入模型未就绪时索引/搜索的友好提示**
  - 索引/搜索后确认主窗口当前工作区未被改动（恢复原工作区）
- 确认 grep `//\s*TODO` 在 `LubanAgentCodex/Views/*.cs` 无命中

## 8. 不在范围内（YAGNI）

- 模型在线刷新（CLI `RefreshModelsAsync`）：桌面端 OnSetDefault 仅用本地 `GetAllModels`，不在线刷新（避免网络等待）。如需可后续加
- Provider 重命名：CLI 不支持（name 是唯一标识），桌面端编辑模式 name 只读，与此一致
- 多 RAG 知识库并行索引：OnIndex/OnSearch 仅对选中工作区，单工作区上下文
