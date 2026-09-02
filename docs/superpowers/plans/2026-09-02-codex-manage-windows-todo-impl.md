# LubanAgentCodex 管理窗口 TODO 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 补全 LubanAgentCodex 三个管理窗口共 11 处 `// TODO` 空事件处理器，消除 TODO，业务能力向 CLI 看齐。

**Architecture:** 新增 `ProviderEditDialog`/`ModelSelectDialog` 两个对话框；改造 `RenameDialog` 支持 `DialogTitle`/`Watermark`；三个管理窗口实现增删改查/索引/搜索；配套改 `ConfigManager.ClearSelectedModel()` 与 `App.axaml.cs` 补 `ProviderHelper.Initialize`。参照 CLI `ProviderCommand`/`WorkCommand`/`RagCommand` 与 `MainWindow.axaml.cs` 的检索模式。

**Tech Stack:** .NET 10 / Avalonia / MSTest（无，验证=构建+手动冒烟）

**Spec:** `docs/superpowers/specs/2026-09-02-codex-manage-windows-todo-impl-design.md`

**验证约定（AGENTS.md：luban-agent 无自动化测试）：** 每个 Task 末尾运行 `dotnet build`；最终 Task 10 做构建+grep TODO+手动冒烟清单。提交步骤列出，但**未经用户明确许可不执行 git commit**。

**工作目录约定：** 根目录不是 git 仓库，git 命令须进入 `luban-agent/` 子仓库执行。构建命令在 `luban-agent/` 下运行。

---

## File Structure

| 文件 | 责任 | 动作 |
|------|------|------|
| `LubanAgentCore/Configuration/ConfigManager.cs` | 新增 `ClearSelectedModel()` | 改 |
| `LubanAgentCodex/App.axaml.cs` | 补 `ProviderHelper.Initialize` | 改 |
| `LubanAgentCodex/Views/RenameDialog.axaml(.cs)` | 加 `DialogTitle`/`Watermark` | 改 |
| `LubanAgentCodex/Views/ProviderEditDialog.axaml(.cs)` | Provider 添加/编辑表单 | 新增 |
| `LubanAgentCodex/Views/ModelSelectDialog.axaml(.cs)` | 模型列表选择 | 新增 |
| `LubanAgentCodex/Views/ProviderManageWindow.axaml(.cs)` | 4 事件实现 | 改 |
| `LubanAgentCodex/Views/WorkManageWindow.axaml(.cs)` | 3 事件实现 | 改 |
| `LubanAgentCodex/Views/RagManageWindow.axaml(.cs)` | 4 事件+视图切换 | 改 |

---

## Task 1: ConfigManager 新增 ClearSelectedModel

**Files:**
- Modify: `luban-agent/LubanAgentCore/Configuration/ConfigManager.cs`（在 `SetSelectedModel` 方法后插入）

- [ ] **Step 1: 新增 ClearSelectedModel 方法**

在 `SetSelectedModel` 方法（约第 188-194 行）之后插入：

```csharp
/// <summary>
/// 清空当前选中的模型（用于删除 Provider 后清理）
/// </summary>
public void ClearSelectedModel()
{
    _config.SelectedModel = null;
    Save();
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj`
Expected: 成功，无错误

- [ ] **Step 3: Commit（需用户许可）**

```bash
cd luban-agent && git add LubanAgentCore/Configuration/ConfigManager.cs && git commit -m "feat: ConfigManager 新增 ClearSelectedModel 方法"
```

---

## Task 2: App.axaml.cs 补 ProviderHelper.Initialize

**Files:**
- Modify: `luban-agent/LubanAgentCodex/App.axaml.cs:100`（`BuildServiceProvider` 之后）

- [ ] **Step 1: 加 using 与 Initialize 调用**

在 `App.axaml.cs` 顶部 using 区加（若未有）：

```csharp
using LubanAgentCore.Configuration;
```

在第 100 行 `_services = AgentHostBuilder.BuildServiceProvider(configuration, embedder, modelManager);` 之后插入：

```csharp
// 初始化 ProviderHelper（使 GetEndpoints 可用，ProviderEditDialog 预填 BaseUrl 依赖）
ProviderHelper.Initialize(configuration);
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
Expected: 成功

- [ ] **Step 3: Commit（需用户许可）**

```bash
cd luban-agent && git add LubanAgentCodex/App.axaml.cs && git commit -m "feat: App 启动补 ProviderHelper.Initialize"
```

---

## Task 3: RenameDialog 加 DialogTitle/Watermark

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/RenameDialog.axaml`
- Modify: `luban-agent/LubanAgentCodex/Views/RenameDialog.axaml.cs`

- [ ] **Step 1: axaml 标题与占位可变**

`RenameDialog.axaml`：将 `Window.Title="重命名"`（L4）改为 `Title="重命名" Name="Root"`（保留默认，代码可改）；内容区标题 `TextBlock Text="重命名"`（L19）加 `Name="TitleTextBlock"`；`TextBox NameTextBox`（L26）的 `PlaceholderText` 改为 `Name="PlaceholderText"` 绑定——简单做法：保持 `PlaceholderText="输入新名称"`，代码里设。

改后 axaml 关键片段：

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="LubanAgentCodex.Views.RenameDialog"
        Title="重命名"
        Name="Root"
        Width="400"
        Height="190"
        WindowStartupLocation="CenterOwner"
        CanResize="False"
        Background="{DynamicResource BackgroundBrush}">

    <Border Classes="dlgCard">
        <Grid RowDefinitions="*,Auto">

            <StackPanel Grid.Row="0" Spacing="16">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <TextBlock Text="✏️" FontSize="17" VerticalAlignment="Center" />
                    <TextBlock Name="TitleTextBlock"
                               Text="重命名"
                               FontSize="16"
                               FontWeight="Bold"
                               Foreground="{DynamicResource TextPrimaryBrush}"
                               VerticalAlignment="Center" />
                </StackPanel>

                <TextBox Name="NameTextBox"
                         PlaceholderText="输入新名称"
                         FontSize="14"
                         CornerRadius="8"
                         Background="{DynamicResource SidebarBrush}"
                         Foreground="{DynamicResource TextPrimaryBrush}"
                         BorderBrush="{DynamicResource BorderBrush}" />
            </StackPanel>
            <!-- 按钮区不变 -->
```

- [ ] **Step 2: cs 加 DialogTitle/Watermark 属性并应用**

`RenameDialog.axaml.cs` 在 `InitializeComponent` 末尾（绑定按钮之后）应用标题/占位。完整改后文件：

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LubanAgentCodex.Views;

public partial class RenameDialog : Window
{
    private TextBox? _nameTextBox;
    private TextBlock? _titleTextBlock;

    public string? Result { get; private set; }

    /// <summary>自定义窗口标题与内容标题，null 时默认"重命名"</summary>
    public string? DialogTitle { get; set; }

    /// <summary>输入框占位提示，null 时保持 axaml 默认</summary>
    public string? Watermark { get; set; }

    public RenameDialog()
    {
        InitializeComponent();
    }

    public RenameDialog(string currentName) : this()
    {
        ApplyCustomization();
        if (_nameTextBox != null)
        {
            _nameTextBox.Text = currentName;
            _nameTextBox.SelectAll();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _nameTextBox = this.FindControl<TextBox>("NameTextBox");
        _titleTextBlock = this.FindControl<TextBlock>("TitleTextBlock");

        if (this.FindControl<Button>("OkButton") is { } ok)
            ok.Click += OnOk;
        if (this.FindControl<Button>("CancelButton") is { } cancel)
            cancel.Click += OnCancel;
    }

    /// <summary>在构造后应用 DialogTitle/Watermark（仅在非默认构造里调用）</summary>
    private void ApplyCustomization()
    {
        var title = DialogTitle ?? "重命名";
        this.Title = title;
        if (_titleTextBlock != null) _titleTextBlock.Text = title;
        if (Watermark != null && _nameTextBox != null)
            _nameTextBox.Watermark = Watermark;
    }

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Result = _nameTextBox?.Text?.Trim();
        Close(Result);
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}
```

> 注：对象初始化器 `{ DialogTitle = "...", Watermark = "..." }` 在 `new RenameDialog("")` 的无参构造后设置属性，但 `ApplyCustomization` 在带参构造里调用——初始化器赋值发生在构造之后，所以 DialogTitle/Watermark 此时仍为 null。需改为：调用方用 `new RenameDialog("") { DialogTitle=..., Watermark=... }` 后，在 `Opened` 事件或手动调 `ApplyCustomization`。简化：把应用逻辑放到 `OnOpened` 覆盖或加 `public void Apply()` 由调用方在 Show 前调。最简：在 `OnOk` 之前不可控——改用属性 setter 即时应用。

**Step 2 修正（采用属性即时应用）：**

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LubanAgentCodex.Views;

public partial class RenameDialog : Window
{
    private TextBox? _nameTextBox;
    private TextBlock? _titleTextBlock;
    private string? _dialogTitle;
    private string? _watermark;

    public string? Result { get; private set; }

    public string? DialogTitle
    {
        get => _dialogTitle;
        set { _dialogTitle = value; ApplyTitle(); }
    }

    public string? Watermark
    {
        get => _watermark;
        set { _watermark = value; ApplyWatermark(); }
    }

    public RenameDialog()
    {
        InitializeComponent();
    }

    public RenameDialog(string currentName) : this()
    {
        if (_nameTextBox != null)
        {
            _nameTextBox.Text = currentName;
            _nameTextBox.SelectAll();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _nameTextBox = this.FindControl<TextBox>("NameTextBox");
        _titleTextBlock = this.FindControl<TextBlock>("TitleTextBlock");

        if (this.FindControl<Button>("OkButton") is { } ok)
            ok.Click += OnOk;
        if (this.FindControl<Button>("CancelButton") is { } cancel)
            cancel.Click += OnCancel;
    }

    private void ApplyTitle()
    {
        var title = _dialogTitle ?? "重命名";
        this.Title = title;
        if (_titleTextBlock != null) _titleTextBlock.Text = title;
    }

    private void ApplyWatermark()
    {
        if (_watermark != null && _nameTextBox != null)
            _nameTextBox.Watermark = _watermark;
    }

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Result = _nameTextBox?.Text?.Trim();
        Close(Result);
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}
```

> 说明：对象初始化器 `new RenameDialog("") { DialogTitle=..., Watermark=... }` 中，`RenameDialog("")` 先执行（此时控件已 `InitializeComponent` 加载），随后属性 setter 执行 `ApplyTitle/ApplyWatermark`，此时 `_nameTextBox`/`_titleTextBlock` 已就绪，可正常应用。

- [ ] **Step 3: 构建验证**

Run: `dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
Expected: 成功

- [ ] **Step 4: Commit（需用户许可）**

```bash
cd luban-agent && git add LubanAgentCodex/Views/RenameDialog.axaml LubanAgentCodex/Views/RenameDialog.axaml.cs && git commit -m "feat: RenameDialog 支持 DialogTitle 与 Watermark"
```

---

## Task 4: 新增 ProviderEditDialog

**Files:**
- Create: `luban-agent/LubanAgentCodex/Views/ProviderEditDialog.axaml`
- Create: `luban-agent/LubanAgentCodex/Views/ProviderEditDialog.axaml.cs`

- [ ] **Step 1: 创建 axaml**

`ProviderEditDialog.axaml`：

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="LubanAgentCodex.Views.ProviderEditDialog"
        Title="Provider"
        Width="440"
        Height="380"
        WindowStartupLocation="CenterOwner"
        CanResize="False"
        Background="{DynamicResource BackgroundBrush}">

    <Border Classes="dlgCard">
        <Grid RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto">

            <TextBlock Grid.Row="0" Name="HeaderTextBlock"
                       Text="添加 Provider"
                       FontSize="16" FontWeight="Bold"
                       Foreground="{DynamicResource TextPrimaryBrush}"
                       Margin="0,0,0,8" />

            <StackPanel Grid.Row="1" Spacing="6" Margin="0,0,0,8">
                <TextBlock Text="类型" FontSize="12" Foreground="{DynamicResource TextSecondaryBrush}" />
                <ComboBox Name="TypeCombo" FontSize="14" MinWidth="380" />
            </StackPanel>

            <StackPanel Grid.Row="2" Spacing="6" Margin="0,0,0,8">
                <TextBlock Text="名称" FontSize="12" Foreground="{DynamicResource TextSecondaryBrush}" />
                <TextBox Name="NameBox" FontSize="14" IsReadOnly="True"
                         Background="{DynamicResource SidebarBrush}"
                         Foreground="{DynamicResource TextPrimaryBrush}"
                         BorderBrush="{DynamicResource BorderBrush}" />
            </StackPanel>

            <StackPanel Grid.Row="3" Spacing="6" Margin="0,0,0,8">
                <TextBlock Text="API Key" FontSize="12" Foreground="{DynamicResource TextSecondaryBrush}" />
                <TextBox Name="ApiKeyBox" FontSize="14" PasswordChar="*"
                         Background="{DynamicResource SidebarBrush}"
                         Foreground="{DynamicResource TextPrimaryBrush}"
                         BorderBrush="{DynamicResource BorderBrush}" />
            </StackPanel>

            <StackPanel Grid.Row="4" Spacing="6" Margin="0,0,0,8">
                <TextBlock Text="Base URL（可选）" FontSize="12" Foreground="{DynamicResource TextSecondaryBrush}" />
                <TextBox Name="BaseUrlBox" FontSize="14"
                         Watermark="留空使用默认"
                         Background="{DynamicResource SidebarBrush}"
                         Foreground="{DynamicResource TextPrimaryBrush}"
                         BorderBrush="{DynamicResource BorderBrush}" />
            </StackPanel>

            <Grid Grid.Row="5" ColumnDefinitions="*,Auto,Auto" Margin="0,8,0,0">
                <TextBlock Name="ErrorText" Grid.Column="0" Text="" Foreground="#F44336"
                           FontSize="12" VerticalAlignment="Center" TextWrapping="Wrap" />
                <Button Grid.Column="1" Name="CancelButton" Content="取消"
                        Classes="dlgGhost" MinWidth="80" Margin="0,0,10,0" />
                <Button Grid.Column="2" Name="OkButton" Content="确定"
                        Classes="dlgPrimary" MinWidth="80" />
            </Grid>

        </Grid>
    </Border>

</Window>
```

- [ ] **Step 2: 创建 axaml.cs**

`ProviderEditDialog.axaml.cs`：

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LubanAgentCore.Configuration;

namespace LubanAgentCodex.Views;

public class ProviderEditResult
{
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string? BaseUrl { get; set; }
}

public partial class ProviderEditDialog : Window
{
    private readonly ComboBox? _typeCombo;
    private readonly TextBox? _nameBox;
    private readonly TextBox? _apiKeyBox;
    private readonly TextBox? _baseUrlBox;
    private readonly TextBlock? _errorText;
    private readonly TextBlock? _headerText;

    private bool _isEditMode;
    private string _selectedType = "";

    // 复刻 CLI BuiltinProviders（name, displayName, needCustomEndpoint, needCustomApiKey）
    private static readonly (string Name, string Display, bool NeedEndpoint, bool NeedKey, string DefaultUrl)[] Builtin =
    {
        ("openai", "OpenAI", false, false, ""),
        ("azure", "Azure OpenAI", true, false, "https://your-resource.openai.azure.com"),
        ("deepseek", "DeepSeek", false, false, ""),
        ("kimi", "Kimi (Moonshot)", false, false, ""),
        ("glm", "智谱 GLM", false, false, ""),
        ("qwen", "通义千问", false, false, ""),
        ("doubao", "豆包", false, false, ""),
        ("claude", "Claude", false, false, ""),
        ("gemini", "Google Gemini", false, false, ""),
        ("ollama", "Ollama (本地)", true, true, "http://localhost:11434/v1"),
        ("minimax", "MiniMax", false, false, ""),
        ("ark", "字节方舟 (火山引擎)", false, false, ""),
        ("bailian", "阿里百炼", false, false, ""),
        ("hunyuan", "腾讯混元", false, false, ""),
        ("mimo", "小米 MiMo", false, false, ""),
        ("custom", "自定义 OpenAI 兼容 API", true, false, ""),
    };

    public ProviderEditDialog()
    {
        InitializeComponent();
        _typeCombo = this.FindControl<ComboBox>("TypeCombo");
        _nameBox = this.FindControl<TextBox>("NameBox");
        _apiKeyBox = this.FindControl<TextBox>("ApiKeyBox");
        _baseUrlBox = this.FindControl<TextBox>("BaseUrlBox");
        _errorText = this.FindControl<TextBlock>("ErrorText");
        _headerText = this.FindControl<TextBlock>("HeaderTextBlock");

        if (_typeCombo != null)
        {
            _typeCombo.ItemsSource = Builtin.Select(b => b.Display).ToList();
            _typeCombo.SelectedIndex = 0;
            _typeCombo.SelectionChanged += OnTypeChanged;
        }
        OnTypeChanged(null, null!);

        this.FindControl<Button>("OkButton").Click += OnOk;
        this.FindControl<Button>("CancelButton").Click += (_, _) => Close(null);
    }

    public ProviderEditDialog(ProviderConfig existing) : this()
    {
        _isEditMode = true;
        if (_headerText != null) _headerText.Text = "编辑 Provider";
        if (_nameBox != null) { _nameBox.Text = existing.Name; _nameBox.IsReadOnly = true; }
        if (_apiKeyBox != null) _apiKeyBox.Text = existing.ApiKey;
        if (_baseUrlBox != null) _baseUrlBox.Text = existing.BaseUrl ?? "";
        if (_typeCombo != null) _typeCombo.IsEnabled = false;
        _selectedType = existing.Name;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_typeCombo == null) return;
        var idx = _typeCombo.SelectedIndex;
        if (idx < 0 || idx >= Builtin.Length) return;
        var (name, _, needEndpoint, _, defaultUrl) = Builtin[idx];
        _selectedType = name;
        if (_nameBox != null && !_isEditMode)
        {
            _nameBox.Text = name;
            _nameBox.IsReadOnly = name != "custom";
        }
        if (_baseUrlBox != null && !_isEditMode)
        {
            _baseUrlBox.Text = needEndpoint ? defaultUrl : TryGetDefaultEndpoint(name);
        }
    }

    private static string TryGetDefaultEndpoint(string name)
    {
        try
        {
            var eps = ProviderHelper.GetEndpoints(name);
            return eps.Count > 0 ? (eps[0].Url ?? "") : "";
        }
        catch
        {
            return "";
        }
    }

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var name = (_nameBox?.Text ?? "").Trim().ToLowerInvariant();
        var apiKey = (_apiKeyBox?.Text ?? "").Trim();
        var baseUrl = string.IsNullOrWhiteSpace(_baseUrlBox?.Text) ? null : _baseUrlBox!.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            ShowError("名称不能为空");
            return;
        }
        if (string.IsNullOrEmpty(apiKey))
        {
            ShowError("API Key 不能为空");
            return;
        }

        Close(new ProviderEditResult { Name = name, ApiKey = apiKey, BaseUrl = baseUrl });
    }

    private void ShowError(string msg)
    {
        if (_errorText != null) { _errorText.Text = msg; }
    }
}
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
Expected: 成功

- [ ] **Step 4: Commit（需用户许可）**

```bash
cd luban-agent && git add LubanAgentCodex/Views/ProviderEditDialog.* && git commit -m "feat: 新增 ProviderEditDialog"
```

---

## Task 5: 新增 ModelSelectDialog

**Files:**
- Create: `luban-agent/LubanAgentCodex/Views/ModelSelectDialog.axaml`
- Create: `luban-agent/LubanAgentCodex/Views/ModelSelectDialog.axaml.cs`

- [ ] **Step 1: 创建 axaml**

`ModelSelectDialog.axaml`：

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="LubanAgentCodex.Views.ModelSelectDialog"
        Title="选择模型"
        Width="400"
        Height="420"
        WindowStartupLocation="CenterOwner"
        CanResize="False"
        Background="{DynamicResource BackgroundBrush}">

    <Border Classes="dlgCard">
        <Grid RowDefinitions="*,Auto">
            <ListBox Grid.Row="0" Name="ModelList" Margin="0,0,0,8" />
            <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Spacing="10">
                <Button Name="CancelButton" Content="取消" Classes="dlgGhost" MinWidth="80" />
                <Button Name="OkButton" Content="确定" Classes="dlgPrimary" MinWidth="80" />
            </StackPanel>
        </Grid>
    </Border>

</Window>
```

- [ ] **Step 2: 创建 axaml.cs**

`ModelSelectDialog.axaml.cs`：

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LubanAgentCodex.Views;

public partial class ModelSelectDialog : Window
{
    private ListBox? _modelList;

    public string? SelectedModel { get; private set; }

    public ModelSelectDialog()
    {
        InitializeComponent();
    }

    public ModelSelectDialog(IList<string> models, string? currentModel = null) : this()
    {
        if (_modelList != null)
        {
            _modelList.ItemsSource = models.Select(m =>
            {
                var isCurrent = currentModel != null && m == currentModel;
                return new ModelItem { Name = m, Display = isCurrent ? $"{m} (已选)" : m };
            }).ToList();
            _modelList.SelectionChanged += (s, e) =>
            {
                if (_modelList.SelectedItem is ModelItem item)
                    SelectedModel = item.Name;
            };
        }

        this.FindControl<Button>("OkButton").Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(SelectedModel)) return;
            Close(SelectedModel);
        };
        this.FindControl<Button>("CancelButton").Click += (_, _) => Close(null);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _modelList = this.FindControl<ListBox>("ModelList");
    }
}

public class ModelItem
{
    public string Name { get; set; } = "";
    public string Display { get; set; } = "";
}
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
Expected: 成功

- [ ] **Step 4: Commit（需用户许可）**

```bash
cd luban-agent && git add LubanAgentCodex/Views/ModelSelectDialog.* && git commit -m "feat: 新增 ModelSelectDialog"
```

---

## Task 6: ProviderManageWindow 实现 4 事件

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/ProviderManageWindow.axaml.cs`（全文替换实现）

- [ ] **Step 1: 重写 axaml.cs 实现 4 事件**

`ProviderManageWindow.axaml.cs`（保留头部版权注释不变，从 `using` 起替换）：

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LubanAgentCore.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.Views;

public partial class ProviderManageWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly ConfigManager _configManager;
    private ListBox? _providerListBox;
    private Button? _addButton;
    private Button? _editButton;
    private Button? _deleteButton;
    private Button? _setDefaultButton;

    public ProviderManageWindow(IServiceProvider services)
    {
        _services = services;
        _configManager = services.GetRequiredService<ConfigManager>();
        InitializeComponent();
        LoadProviders();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _providerListBox = this.FindControl<ListBox>("ProviderListBox");
        _addButton = this.FindControl<Button>("AddButton");
        _editButton = this.FindControl<Button>("EditButton");
        _deleteButton = this.FindControl<Button>("DeleteButton");
        _setDefaultButton = this.FindControl<Button>("SetDefaultButton");

        if (_addButton != null) _addButton.Click += OnAdd;
        if (_editButton != null) _editButton.Click += OnEdit;
        if (_deleteButton != null) _deleteButton.Click += OnDelete;
        if (_setDefaultButton != null) _setDefaultButton.Click += OnSetDefault;
        if (_providerListBox != null)
            _providerListBox.SelectionChanged += OnSelectionChanged;
    }

    private void LoadProviders()
    {
        if (_providerListBox == null) return;

        var providers = _configManager.Providers;
        var selectedIndex = _providerListBox.SelectedIndex;

        _providerListBox.ItemsSource = providers.Select(p => new ProviderItem
        {
            Name = p.Name,
            ApiKeyMasked = MaskApiKey(p.ApiKey),
            BaseUrl = p.BaseUrl ?? "(默认)",
            Models = string.Join(", ", _configManager.GetAllModels(p.Name).Take(3)),
            Status = _configManager.SelectedModel?.StartsWith(p.Name + ":") == true ? "✓ 默认" : ""
        }).ToList();

        if (selectedIndex >= 0 && selectedIndex < providers.Count)
            _providerListBox.SelectedIndex = selectedIndex;
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8) return "****";
        return $"{apiKey[..4]}...{apiKey[^4..]}";
    }

    private async void OnAdd(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var dlg = new ProviderEditDialog();
            var result = await dlg.ShowDialog<ProviderEditResult?>(this);
            if (result == null) return;

            _configManager.AddProvider(result.Name, result.ApiKey, result.BaseUrl);
            LoadProviders();
            await Dialogs.ShowInfoAsync(this, "Provider 已添加");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderManageWindow.OnAdd 异常", ex, "add");
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnEdit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_providerListBox?.SelectedItem is not ProviderItem item) return;
        try
        {
            var provider = _configManager.GetProvider(item.Name);
            if (provider == null) { await Dialogs.ShowErrorAsync(this, "Provider 不存在"); return; }

            var dlg = new ProviderEditDialog(provider);
            var result = await dlg.ShowDialog<ProviderEditResult?>(this);
            if (result == null) return;

            _configManager.AddProvider(provider.Name, result.ApiKey, result.BaseUrl);
            LoadProviders();
            await Dialogs.ShowInfoAsync(this, "Provider 已更新");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderManageWindow.OnEdit 异常", ex, item.Name);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_providerListBox?.SelectedItem is not ProviderItem item) return;
        var idx = _providerListBox.SelectedIndex;
        try
        {
            var ok = await Dialogs.ShowConfirmAsync(this, "删除 Provider",
                $"确定删除 {item.Name} 吗？", okText: "删除", danger: true);
            if (!ok) return;

            var provider = _configManager.GetProvider(item.Name);
            _configManager.Providers.RemoveAt(idx);
            _configManager.Save();

            if (provider != null && _configManager.SelectedModel?.StartsWith($"{provider.Name}:") == true)
                _configManager.ClearSelectedModel();

            LoadProviders();
            await Dialogs.ShowInfoAsync(this, "Provider 已删除");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderManageWindow.OnDelete 异常", ex, item.Name);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnSetDefault(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_providerListBox?.SelectedItem is not ProviderItem item) return;
        try
        {
            var models = _configManager.GetAllModels(item.Name);
            if (models.Count == 0)
            {
                await Dialogs.ShowInfoAsync(this, "该 Provider 无可用模型，请先添加模型");
                return;
            }

            var currentModel = _configManager.SelectedModel?.StartsWith(item.Name + ":") == true
                ? _configManager.SelectedModel[(item.Name.Length + 1)..]
                : null;

            var dlg = new ModelSelectDialog(models, currentModel);
            var selected = await dlg.ShowDialog<string?>(this);
            if (string.IsNullOrEmpty(selected)) return;

            _configManager.SetSelectedModel($"{item.Name}:{selected}");
            LoadProviders();
            await Dialogs.ShowInfoAsync(this, $"已设为默认: {item.Name}:{selected}");
        }
        catch (Exception ex)
        {
            Logger.Error("ProviderManageWindow.OnSetDefault 异常", ex, item.Name);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = _providerListBox?.SelectedItem != null;
        if (_editButton != null) _editButton.IsEnabled = hasSelection;
        if (_deleteButton != null) _deleteButton.IsEnabled = hasSelection;
        if (_setDefaultButton != null) _setDefaultButton.IsEnabled = hasSelection;
    }

    private class ProviderItem
    {
        public string Name { get; set; } = "";
        public string ApiKeyMasked { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string Models { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
```

> 注：`Logger.Error` 三参重载不存在则用 `Logger.Error("msg", ex)`；实现时按 `LubanAgentCore` 的 `Logger` 实际签名调整。

- [ ] **Step 2: 构建验证**

Run: `dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
Expected: 成功

- [ ] **Step 3: Commit（需用户许可）**

```bash
cd luban-agent && git add LubanAgentCodex/Views/ProviderManageWindow.axaml.cs && git commit -m "feat: ProviderManageWindow 实现 4 个事件"
```

---

## Task 7: WorkManageWindow 实现 3 事件

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/WorkManageWindow.axaml.cs`

- [ ] **Step 1: 重写 axaml.cs 实现 3 事件**

`WorkManageWindow.axaml.cs`（保留头部版权注释，从 `using` 起替换）：

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LubanAgentCore.Repositories;
using LubanAgentCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.Views;

public partial class WorkManageWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IWorkspaceManager _workspaceManager;
    private ListBox? _workspaceListBox;
    private Button? _addButton;
    private Button? _switchButton;
    private Button? _deleteButton;
    private Button? _authorizeButton;

    public WorkManageWindow(IServiceProvider services)
    {
        _services = services;
        _workspaceManager = services.GetRequiredService<IWorkspaceManager>();
        InitializeComponent();
        LoadWorkspaces();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _workspaceListBox = this.FindControl<ListBox>("WorkspaceListBox");
        _addButton = this.FindControl<Button>("AddButton");
        _switchButton = this.FindControl<Button>("SwitchButton");
        _deleteButton = this.FindControl<Button>("DeleteButton");
        _authorizeButton = this.FindControl<Button>("AuthorizeButton");

        if (_addButton != null) _addButton.Click += OnAdd;
        if (_switchButton != null) _switchButton.Click += OnSwitch;
        if (_deleteButton != null) _deleteButton.Click += OnDelete;
        if (_authorizeButton != null) _authorizeButton.Click += OnAuthorize;
        if (_workspaceListBox != null)
            _workspaceListBox.SelectionChanged += OnSelectionChanged;
    }

    private async void LoadWorkspaces()
    {
        if (_workspaceListBox == null) return;

        var workspaces = await _workspaceManager.GetUserWorkspacesAsync();
        var currentId = _workspaceManager.CurrentWorkspace?.WorkspaceId;

        _workspaceListBox.ItemsSource = workspaces.Select(w => new WorkspaceItem
        {
            WorkspaceId = w.WorkspaceId,
            TypeIcon = w.Type == "Rag" ? "📚" : "📁",
            Name = w.Name,
            RootPath = w.RootPath,
            Status = w.WorkspaceId == currentId ? "✓ 当前" : "",
            IsAuthorized = w.IsAuthorized ? "✓" : "✗"
        }).ToList();
    }

    private async void OnAdd(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var dlg = new NewWorkspaceDialog();
            var ok = await dlg.ShowDialog<bool?>(this);
            if (ok != true) return;

            var ws = await _workspaceManager.CreateWorkspaceAsync(dlg.WorkspacePath!, dlg.WorkspaceName, "Normal");
            LoadWorkspaces();
            await Dialogs.ShowInfoAsync(this, $"已创建工作区: {ws.Name}，可点切换使用");
        }
        catch (Exception ex)
        {
            Logger.Error("WorkManageWindow.OnAdd 异常", ex, "add");
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnSwitch(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_workspaceListBox?.SelectedItem is not WorkspaceItem item) return;
        try
        {
            await _workspaceManager.SetCurrentAsync(item.WorkspaceId);
            LoadWorkspaces();
        }
        catch (Exception ex)
        {
            Logger.Error("WorkManageWindow.OnSwitch 异常", ex, item.WorkspaceId);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_workspaceListBox?.SelectedItem is not WorkspaceItem item) return;
        try
        {
            var ok = await Dialogs.ShowConfirmAsync(this, "删除工作区",
                $"删除 '{item.Name}' 将同时删除其下所有会话和索引，确认？",
                okText: "删除", danger: true);
            if (!ok) return;

            var sessionRepo = _services.GetRequiredService<SessionRepository>();
            var ragFileRepo = new RagFileRepository();
            var ragChunkRepo = new RagChunkRepository();
            var wsRepo = _services.GetRequiredService<WorkspaceRepository>();

            await sessionRepo.SoftDeleteByWorkspaceAsync(item.WorkspaceId);
            await ragFileRepo.DeleteByWorkspaceAsync(item.WorkspaceId);
            await ragChunkRepo.DeleteByWorkspaceAsync(item.WorkspaceId);
            await wsRepo.LogicDeleteAsync(w => w.WorkspaceId == item.WorkspaceId);

            if (_workspaceManager.CurrentWorkspace?.WorkspaceId == item.WorkspaceId)
                await Dialogs.ShowInfoAsync(this, "当前工作区已删除，请切换到其他工作区");

            LoadWorkspaces();
            await Dialogs.ShowInfoAsync(this, "已删除工作区");
        }
        catch (Exception ex)
        {
            Logger.Error("WorkManageWindow.OnDelete 异常", ex, item.WorkspaceId);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnAuthorize(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_workspaceListBox?.SelectedItem is not WorkspaceItem item) return;
        try
        {
            var ws = (await _workspaceManager.GetUserWorkspacesAsync())
                .FirstOrDefault(w => w.WorkspaceId == item.WorkspaceId);
            if (ws == null) return;

            if (ws.IsAuthorized)
            {
                await Dialogs.ShowInfoAsync(this, "工作区已授权");
                return;
            }

            var switched = false;
            if (_workspaceManager.CurrentWorkspace?.WorkspaceId != item.WorkspaceId)
            {
                await _workspaceManager.SetCurrentAsync(item.WorkspaceId);
                switched = true;
            }

            var ok = await _workspaceManager.EnsureAuthorizedAsync(ws);
            LoadWorkspaces();
            if (ok)
                await Dialogs.ShowInfoAsync(this, switched ? "已授权并切换为该工作区" : "工作区已授权");
            else
                await Dialogs.ShowErrorAsync(this, "授权失败");
        }
        catch (Exception ex)
        {
            Logger.Error("WorkManageWindow.OnAuthorize 异常", ex, item.WorkspaceId);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = _workspaceListBox?.SelectedItem != null;
        if (_switchButton != null) _switchButton.IsEnabled = hasSelection;
        if (_deleteButton != null) _deleteButton.IsEnabled = hasSelection;
        if (_authorizeButton != null) _authorizeButton.IsEnabled = hasSelection;
    }

    private class WorkspaceItem
    {
        public string WorkspaceId { get; set; } = "";
        public string TypeIcon { get; set; } = "";
        public string Name { get; set; } = "";
        public string RootPath { get; set; } = "";
        public string Status { get; set; } = "";
        public string IsAuthorized { get; set; } = "";
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
Expected: 成功

- [ ] **Step 3: Commit（需用户许可）**

```bash
cd luban-agent && git add LubanAgentCodex/Views/WorkManageWindow.axaml.cs && git commit -m "feat: WorkManageWindow 实现 3 个事件"
```

---

## Task 8: RagManageWindow axaml 加 BackButton/ResultListBox

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/RagManageWindow.axaml`

- [ ] **Step 1: axaml 加返回按钮与结果 ListBox**

`RagManageWindow.axaml` 全文替换：

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="LubanAgentCodex.Views.RagManageWindow"
        Title="RAG 知识库管理"
        Width="600"
        Height="400"
        WindowStartupLocation="CenterOwner">

    <Grid RowDefinitions="Auto,*">
        <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="8" Margin="16,16,16,8">
            <Button Name="CreateButton" Content="+ 创建知识库" />
            <Button Name="IndexButton" Content="索引" IsEnabled="False" />
            <Button Name="SearchButton" Content="搜索" IsEnabled="False" />
            <Button Name="DeleteButton" Content="删除" IsEnabled="False" />
            <Button Name="BackButton" Content="← 返回列表" IsVisible="False" />
        </StackPanel>

        <!-- 知识库列表（默认显示） -->
        <ListBox Grid.Row="1" Name="RagListBox" Margin="16,0,16,16" />

        <!-- 搜索结果列表（默认隐藏，与 RagListBox 同位置叠加） -->
        <ListBox Grid.Row="1" Name="ResultListBox" Margin="16,0,16,16" IsVisible="False" />
    </Grid>

</Window>
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
Expected: 成功（此时 .cs 还未引用新控件，可能未使用警告，无错）

- [ ] **Step 3: Commit（需用户许可）**

```bash
cd luban-agent && git add LubanAgentCodex/Views/RagManageWindow.axaml && git commit -m "feat: RagManageWindow axaml 加返回按钮与结果列表"
```

---

## Task 9: RagManageWindow 实现 4 事件 + 视图切换

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Views/RagManageWindow.axaml.cs`

- [ ] **Step 1: 重写 axaml.cs**

`RagManageWindow.axaml.cs`（保留头部版权注释，从 `using` 起替换）：

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LuBan.AIAgent.Retrieval;
using LubanAgentCore.Repositories;
using LubanAgentCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LubanAgentCodex.Views;

public partial class RagManageWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IWorkspaceManager _workspaceManager;
    private ListBox? _ragListBox;
    private ListBox? _resultListBox;
    private Button? _createButton;
    private Button? _indexButton;
    private Button? _searchButton;
    private Button? _deleteButton;
    private Button? _backButton;

    public RagManageWindow(IServiceProvider services)
    {
        _services = services;
        _workspaceManager = services.GetRequiredService<IWorkspaceManager>();
        InitializeComponent();
        LoadRagWorkspaces();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _ragListBox = this.FindControl<ListBox>("RagListBox");
        _resultListBox = this.FindControl<ListBox>("ResultListBox");
        _createButton = this.FindControl<Button>("CreateButton");
        _indexButton = this.FindControl<Button>("IndexButton");
        _searchButton = this.FindControl<Button>("SearchButton");
        _deleteButton = this.FindControl<Button>("DeleteButton");
        _backButton = this.FindControl<Button>("BackButton");

        if (_createButton != null) _createButton.Click += OnCreate;
        if (_indexButton != null) _indexButton.Click += OnIndex;
        if (_searchButton != null) _searchButton.Click += OnSearch;
        if (_deleteButton != null) _deleteButton.Click += OnDelete;
        if (_backButton != null) _backButton.Click += OnBack;
        if (_ragListBox != null)
            _ragListBox.SelectionChanged += OnSelectionChanged;
    }

    private async void LoadRagWorkspaces()
    {
        if (_ragListBox == null) return;

        var workspaces = await _workspaceManager.GetUserWorkspacesAsync();
        var ragWorkspaces = workspaces.Where(w => w.Type == "Rag").ToList();
        _ragListBox.ItemsSource = ragWorkspaces.Select(w => new RagItem
        {
            WorkspaceId = w.WorkspaceId,
            Name = w.Name,
            RootPath = w.RootPath,
            FileCount = "-",
            ChunkCount = "-",
            Status = "已创建"
        }).ToList();
    }

    private async void OnCreate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var dlg = new NewWorkspaceDialog();
            var ok = await dlg.ShowDialog<bool?>(this);
            if (ok != true) return;

            var ws = await _workspaceManager.CreateWorkspaceAsync(dlg.WorkspacePath!, dlg.WorkspaceName, "Rag");
            LoadRagWorkspaces();
            await Dialogs.ShowInfoAsync(this, $"已创建 RAG 知识库: {ws.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error("RagManageWindow.OnCreate 异常", ex, "create");
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private async void OnIndex(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_ragListBox?.SelectedItem is not RagItem item) return;
        var retrieval = _services.GetService<IRetrievalService>();
        if (retrieval == null)
        {
            await Dialogs.ShowInfoAsync(this, "嵌入模型未就绪，无法索引");
            return;
        }

        var previous = _workspaceManager.CurrentWorkspace;
        try
        {
            var ws = (await _workspaceManager.GetUserWorkspacesAsync())
                .FirstOrDefault(w => w.WorkspaceId == item.WorkspaceId);
            if (ws == null) return;

            await _workspaceManager.EnsureAuthorizedAsync(ws);
            await _workspaceManager.SetCurrentAsync(item.WorkspaceId);

            var dlg = new RenameDialog("") { DialogTitle = "索引文件匹配模式", Watermark = "留空索引全部文件" };
            var glob = await dlg.ShowDialog<string?>(this);
            if (glob == null) return;

            var report = await retrieval.IndexDirectoryAsync(ws.RootPath, glob == "" ? null : glob, force: false);
            await Dialogs.ShowInfoAsync(this,
                $"索引完成：扫描 {report.ScannedFiles}，新增 {report.NewFiles}，更新 {report.UpdatedFiles}，跳过 {report.SkippedFiles}，切块 {report.TotalChunks}");
        }
        catch (Exception ex)
        {
            Logger.Error("RagManageWindow.OnIndex 异常", ex, item.WorkspaceId);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
        finally
        {
            if (previous != null) await _workspaceManager.SetCurrentAsync(previous.WorkspaceId);
        }
    }

    private async void OnSearch(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_ragListBox?.SelectedItem is not RagItem item) return;
        var retrieval = _services.GetService<IRetrievalService>();
        if (retrieval == null)
        {
            await Dialogs.ShowInfoAsync(this, "嵌入模型未就绪，无法搜索");
            return;
        }

        var previous = _workspaceManager.CurrentWorkspace;
        try
        {
            var ws = (await _workspaceManager.GetUserWorkspacesAsync())
                .FirstOrDefault(w => w.WorkspaceId == item.WorkspaceId);
            if (ws == null) return;

            await _workspaceManager.SetCurrentAsync(item.WorkspaceId);

            var dlg = new RenameDialog("") { DialogTitle = "搜索查询", Watermark = "输入检索关键词" };
            var query = await dlg.ShowDialog<string?>(this);
            if (string.IsNullOrWhiteSpace(query)) return;

            var results = await retrieval.SearchAsync(query!, topK: 5);
            ShowSearchResults(results);
            if (results.Count == 0)
                await Dialogs.ShowInfoAsync(this, "未找到相关文档");
        }
        catch (Exception ex)
        {
            Logger.Error("RagManageWindow.OnSearch 异常", ex, item.WorkspaceId);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
        finally
        {
            if (previous != null) await _workspaceManager.SetCurrentAsync(previous.WorkspaceId);
        }
    }

    private void ShowSearchResults(IReadOnlyList<RetrievalResult> results)
    {
        if (_resultListBox == null || _ragListBox == null || _backButton == null) return;
        _ragListBox.IsVisible = false;
        _resultListBox.IsVisible = true;
        _backButton.IsVisible = true;
        _resultListBox.ItemsSource = results.Select(r => new SearchResultItem
        {
            FilePath = r.FilePath,
            SymbolName = string.IsNullOrEmpty(r.SymbolName) ? "-" : r.SymbolName!,
            LineRange = $"L{r.StartLine}-{r.EndLine}",
            Content = r.Content.Length > 200 ? r.Content[..200] + "…" : r.Content
        }).ToList();
    }

    private void OnBack(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_resultListBox == null || _ragListBox == null || _backButton == null) return;
        _resultListBox.IsVisible = false;
        _ragListBox.IsVisible = true;
        _backButton.IsVisible = false;
        LoadRagWorkspaces();
    }

    private async void OnDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_ragListBox?.SelectedItem is not RagItem item) return;
        try
        {
            var ok = await Dialogs.ShowConfirmAsync(this, "删除 RAG 知识库",
                $"删除 '{item.Name}' 将同时删除其下所有会话和索引，确认？",
                okText: "删除", danger: true);
            if (!ok) return;

            var sessionRepo = _services.GetRequiredService<SessionRepository>();
            var ragFileRepo = new RagFileRepository();
            var ragChunkRepo = new RagChunkRepository();
            var wsRepo = _services.GetRequiredService<WorkspaceRepository>();

            await sessionRepo.SoftDeleteByWorkspaceAsync(item.WorkspaceId);
            await ragFileRepo.DeleteByWorkspaceAsync(item.WorkspaceId);
            await ragChunkRepo.DeleteByWorkspaceAsync(item.WorkspaceId);
            await wsRepo.LogicDeleteAsync(w => w.WorkspaceId == item.WorkspaceId);

            LoadRagWorkspaces();
            await Dialogs.ShowInfoAsync(this, "已删除 RAG 知识库");
        }
        catch (Exception ex)
        {
            Logger.Error("RagManageWindow.OnDelete 异常", ex, item.WorkspaceId);
            await Dialogs.ShowErrorAsync(this, ex.Message);
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = _ragListBox?.SelectedItem != null;
        if (_indexButton != null) _indexButton.IsEnabled = hasSelection;
        if (_searchButton != null) _searchButton.IsEnabled = hasSelection;
        if (_deleteButton != null) _deleteButton.IsEnabled = hasSelection;
    }

    private class RagItem
    {
        public string WorkspaceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string RootPath { get; set; } = "";
        public string FileCount { get; set; } = "";
        public string ChunkCount { get; set; } = "";
        public string Status { get; set; } = "";
    }

    private class SearchResultItem
    {
        public string FilePath { get; set; } = "";
        public string SymbolName { get; set; } = "";
        public string LineRange { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
```

> 注：`Logger.Error` 签名按实际调整；`RagItem`/`SearchResultItem` 需 axaml `DataTemplate` 显示各字段，若 ListBox 无模板只显示 ToString——需在 axaml 加 `<ListBox.ItemTemplate>` 展示字段，或给类加 `ToString()`。建议在 Task 8 axaml 补 ItemTemplate（见 Task 8 补充）。为聚焦逻辑，此处依赖默认显示，UI 细节可后续打磨。

- [ ] **Step 2: 构建验证**

Run: `dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj`
Expected: 成功

- [ ] **Step 3: Commit（需用户许可）**

```bash
cd luban-agent && git add LubanAgentCodex/Views/RagManageWindow.axaml.cs && git commit -m "feat: RagManageWindow 实现 4 事件与搜索结果视图切换"
```

---

## Task 10: 最终构建 + grep TODO + 冒烟清单

**Files:**
- 验证全仓

- [ ] **Step 1: 整体构建**

Run: `dotnet build luban-agent/luban-agent.slnx`
Expected: 成功，0 错误

- [ ] **Step 2: grep 确认无 TODO**

Run: `grep -rn "//\s*TODO" luban-agent/LubanAgentCodex/Views/*.cs`（或 PowerShell `Select-String`）
Expected: 无命中

- [ ] **Step 3: 手动冒烟清单**

启动 Codex，逐项验证：
- Provider：添加（内置类型如 glm、自定义）、编辑（改 ApiKey）、删除（含选中模型属该 provider 时验证 `ClearSelectedModel`）、设为默认（选模型后列表显示 ✓ 默认）
- Work：新建（选目录）、删除（含当前工作区，提示切换）、授权（已授权提示；非当前工作区先切换再授权）
- Rag：创建知识库（选目录）、索引（空 glob 全量/指定 glob）、搜索（有结果切结果视图+返回；无结果提示）、删除
- **嵌入模型未就绪场景**：索引/搜索提示"嵌入模型未就绪"
- **索引/搜索后**：确认主窗口当前工作区未变（恢复原工作区）

- [ ] **Step 4: 最终 Commit（需用户许可）**

```bash
cd luban-agent && git add -A && git commit -m "feat: 完成 Codex 管理窗口 11 处 TODO 实现"
```

---

## 自审记录

- **Spec 覆盖**：spec §1-§8 各节均有对应 Task。配套改动（ConfigManager/App.axaml.cs）= Task 1/2；对话框 = Task 3/4/5；三窗口 = Task 6/7/8/9；验证 = Task 10。
- **类型一致性**：`ProviderEditResult`（Task4 定义，Task6 用）、`ModelItem`（Task5 定义）、`RagItem`/`SearchResultItem`（Task9 定义）、`WorkspaceItem`/`ProviderItem`（各自窗口内私有类）命名一致。`IRetrievalService.SearchAsync`/`IndexDirectoryAsync` 签名与 framework `IRetrievalService.cs` 一致。`RetrievalResult` 属性（FilePath/SymbolName/StartLine/EndLine/Content）与 `Models.cs` 一致。
- **已知待执行时确认项**：
  1. `Logger.Error` 重载签名——Task 6/7/9 用了三参 `Logger.Error(msg, ex, 标识)`，实际若仅两参则改 `Logger.Error(msg, ex)`。
  2. ListBox 项字段展示——`RagItem`/`SearchResultItem`/`WorkspaceItem`/`ProviderItem` 需 axaml `ItemTemplate` 或类加 `ToString()` 才能友好显示字段，Task 8 已为 Rag 加控件但未加 ItemTemplate，执行时按 UI 效果补 ItemTemplate（不影响逻辑）。
  3. `RenameDialog.axaml` 的 `TextBox` 需支持 `Watermark` 属性（Avalonia TextBox 有该属性），`PasswordChar` 同理。
