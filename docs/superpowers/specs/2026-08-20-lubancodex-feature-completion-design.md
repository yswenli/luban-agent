# LubanAgentCodex 功能补全设计

## 概述

LubanAgentCodex 是基于 Avalonia UI 的 AI 编码代理桌面客户端，相比 LubanAgentCli（Terminal.Gui TUI）缺少多个功能。本设计文档描述如何补全这些缺失功能，使两个客户端功能对等。

## 目标

1. 补全命令系统（/provider, /skill, /rule, /mcp, /work, /rag）
2. 增强权限模式切换（Shift+Tab 快捷键、BypassPermissions 二次确认）
3. 实现 Markdown 渲染（支持鼠标选取）
4. 添加页脚状态栏（权限模式、git分支、工作目录、token用量）

## 非目标

- 不修改 LubanAgentCli
- 不修改 LubanAgentCore 共享层
- 不添加 CLI 独有的功能（如多会话任务视图）

---

## 1. 命令系统补全

### 1.1 功能清单

| 命令 | 简写 | 窗口类型 | 功能 |
|------|------|---------|------|
| `/provider` | `/p` | ProviderManageWindow (新建) | 列出/添加/更新/删除/切换 Provider |
| `/skill` | `/sk` | SkillManageWindow (已有) | 列出/添加/更新/删除/切换 Skill |
| `/rule` | `/r` | RuleManageWindow (已有) | 列出/添加/更新/删除/切换 规则 |
| `/mcp` | `/mp` | MCPManageWindow (已有) | 列出/添加/更新/删除/切换 MCP |
| `/work` | `/w` | WorkManageWindow (新建) | 列出/创建/切换/删除/信息 工作区 |
| `/rag` | `/rg` | RagManageWindow (新建) | 创建/索引/搜索/列出/删除 RAG |

### 1.2 命令路由

修改 `MainWindowViewModel.ExecuteCommandAsync` 方法：

```csharp
private async Task ExecuteCommandAsync(string input)
{
    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var cmd = parts[0].ToLowerInvariant();
    var args = ExpandSubCommandAliases(parts.Skip(1).ToArray());

    switch (cmd)
    {
        case "/help":
            ShowHelp();
            break;
        case "/clear":
            ClearMessages();
            break;
        case "/mode":
            await ExecuteModeCommandAsync(args);
            break;
        case "/model":
        case "/m":
            await ExecuteModelCommandAsync(args);
            break;
        case "/session":
        case "/se":
            await ExecuteSessionCommandAsync(args);
            break;
        case "/stats":
        case "/st":
            ShowStats();
            break;
        case "/provider":
        case "/p":
            await ShowProviderManagerAsync(args);
            break;
        case "/skill":
        case "/sk":
            await ShowSkillManagerAsync(args);
            break;
        case "/rule":
        case "/r":
            await ShowRuleManagerAsync(args);
            break;
        case "/mcp":
        case "/mp":
            await ShowMcpManagerAsync(args);
            break;
        case "/work":
        case "/w":
            await ShowWorkManagerAsync(args);
            break;
        case "/rag":
        case "/rg":
            await ShowRagManagerAsync(args);
            break;
        default:
            Messages.Add(new SystemMessageItem
            {
                Content = $"未知命令: {cmd}，输入 /help 查看可用命令",
                IsError = true
            });
            break;
    }
}
```

### 1.3 子命令简写展开

```csharp
private static string[] ExpandSubCommandAliases(string[] parts)
{
    var result = new string[parts.Length];
    for (var i = 0; i < parts.Length; i++)
    {
        result[i] = parts[i] switch
        {
            "-l" => "-list",
            "-a" => "-add",
            "-u" => "-update",
            "-d" => "-delete",
            "-s" => "-switch",
            "-n" => "-new",
            "-c" => "-clear",
            "-t" => "-tools",
            _ => parts[i]
        };
    }
    return result;
}
```

### 1.4 ProviderManageWindow

新建 `Views/ProviderManageWindow.axaml`：

```
┌─────────────────────────────────────────────┐
│  Provider 管理                           ✕  │
├─────────────────────────────────────────────┤
│  [+ 添加 Provider]                          │
│                                             │
│  ┌─────────────────────────────────────────┐│
│  │ Provider   | 模型           | 状态     ││
│  │─────────────────────────────────────────││
│  │ OpenAI     | gpt-4o         | ✓ 默认   ││
│  │ DeepSeek   | deepseek-chat  |          ││
│  │ Kimi       | moonshot-v1-8k |          ││
│  └─────────────────────────────────────────┘│
│                                             │
│  [编辑] [删除] [设为默认]                    │
└─────────────────────────────────────────────┘
```

功能：
- 列出所有 Provider（从 ConfigManager 获取）
- 添加 Provider（弹出对话框，输入 API Key、Base URL）
- 编辑 Provider（修改 API Key、Base URL）
- 删除 Provider（确认对话框）
- 设为默认（调用 ConfigManager.SetSelectedModel）

### 1.5 WorkManageWindow

新建 `Views/WorkManageWindow.axaml`：

```
┌─────────────────────────────────────────────┐
│  工作区管理                              ✕  │
├─────────────────────────────────────────────┤
│  [+ 新建工作区]                             │
│                                             │
│  ┌─────────────────────────────────────────┐│
│  │ 类型 | 名称      | 根目录        | 状态││
│  │─────────────────────────────────────────││
│  │ 📁  | luban     | D:\WorkBench  | ✓   ││
│  │ 📁  | my-project| D:\Projects   |     ││
│  │ 📚  | knowledge | D:\Knowledge  |     ││
│  └─────────────────────────────────────────┘│
│                                             │
│  [切换] [删除] [授权]                       │
└─────────────────────────────────────────────┘
```

功能：
- 列出所有工作区（从 WorkspaceManager 获取）
- 新建工作区（选择文件夹）
- 切换工作区（调用 WorkspaceManager.SetCurrentAsync）
- 删除工作区（确认对话框）
- 授权工作区（调用 WorkspaceManager.EnsureAuthorizedAsync）

### 1.6 RagManageWindow

新建 `Views/RagManageWindow.axaml`：

```
┌─────────────────────────────────────────────┐
│  RAG 知识库管理                          ✕  │
├─────────────────────────────────────────────┤
│  [+ 创建知识库]                             │
│                                             │
│  ┌─────────────────────────────────────────┐│
│  │ 名称      | 文件数 | 切块数 | 状态     ││
│  │─────────────────────────────────────────││
│  │ knowledge | 42     | 156   | ✓ 已索引  ││
│  │ docs      | 15     | 67    | ⏳ 索引中 ││
│  └─────────────────────────────────────────┘│
│                                             │
│  [索引] [搜索] [删除]                       │
└─────────────────────────────────────────────┘
```

功能：
- 列出所有 RAG 工作区
- 创建 RAG 知识库（选择文件夹，创建 Rag 类型工作区）
- 索引文件（调用 RetrievalService.IndexAsync）
- 搜索（弹出搜索对话框，显示结果）
- 删除（确认对话框）

---

## 2. 权限模式增强

### 2.1 Shift+Tab 快捷键

修改 `MainWindow.axaml.cs`，在 `OnKeyDown` 方法中添加：

```csharp
if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift)
{
    CyclePermissionMode();
    e.Handled = true;
}
```

### 2.2 权限模式循环

```csharp
private void CyclePermissionMode()
{
    _viewModel.PermissionMode = _viewModel.PermissionMode switch
    {
        ToolPermissionMode.Default => ToolPermissionMode.Plan,
        ToolPermissionMode.Plan => ToolPermissionMode.AcceptEdits,
        ToolPermissionMode.AcceptEdits => ToolPermissionMode.BypassPermissions,
        ToolPermissionMode.BypassPermissions => ToolPermissionMode.Default,
        _ => ToolPermissionMode.Default
    };
    
    // 更新页脚显示
    UpdateFooter();
    
    // 如果切换到 Bypass，显示二次确认
    if (_viewModel.PermissionMode == ToolPermissionMode.BypassPermissions)
    {
        _ = ConfirmBypassModeAsync();
    }
}
```

### 2.3 BypassPermissions 二次确认

```csharp
private async Task ConfirmBypassModeAsync()
{
    var dialog = new Window
    {
        Title = "⚠️ 安全确认",
        Width = 400,
        Height = 200,
        WindowStartupLocation = WindowStartupLocation.CenterOwner
    };
    
    var content = new StackPanel
    {
        Margin = new Thickness(20),
        Spacing = 16
    };
    
    content.Children.Add(new TextBlock
    {
        Text = "确定要切换到跳过权限模式吗？",
        FontSize = 14,
        TextWrapping = TextWrapping.Wrap
    });
    
    content.Children.Add(new TextBlock
    {
        Text = "此模式下所有工具调用将跳过确认，可能存在安全风险。",
        Foreground = Brushes.Red,
        TextWrapping = TextWrapping.Wrap
    });
    
    var buttonPanel = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = 8
    };
    
    var okButton = new Button { Content = "确定" };
    var cancelButton = new Button { Content = "取消" };
    
    okButton.Click += (s, e) => dialog.Close(true);
    cancelButton.Click += (s, e) => dialog.Close(false);
    
    buttonPanel.Children.Add(okButton);
    buttonPanel.Children.Add(cancelButton);
    content.Children.Add(buttonPanel);
    
    dialog.Content = content;
    
    var result = await dialog.ShowDialog<bool?>(_mainWindow);
    if (result != true)
    {
        // 用户取消，恢复到默认模式
        _viewModel.PermissionMode = ToolPermissionMode.Default;
        UpdateFooter();
    }
}
```

### 2.4 权限模式颜色

| 模式 | 显示名 | 颜色 | 色值 |
|------|--------|------|------|
| Default | default | 白色 | #FFFFFF |
| Plan | plan | 紫色 | #AFA9EC |
| AcceptEdits | accept-edits | 蓝色 | #85B7EB |
| BypassPermissions | bypass | 红色 | #F09595 |

---

## 3. Markdown 渲染

### 3.1 方案选择

使用 `Markdown.Avalonia` 库（NuGet 包），并确保支持鼠标选取。

### 3.2 实现步骤

1. **添加 NuGet 包引用**：
```xml
<PackageReference Include="Markdown.Avalonia" Version="0.11.0" />
```

2. **修改 AssistantMessageView.axaml**：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:LubanAgentCodex.ViewModels.Messages"
             xmlns:md="clr-namespace:Markdown.Avalonia;assembly=Markdown.Avalonia"
             x:Class="LubanAgentCodex.Views.Controls.AssistantMessageView"
             x:DataType="vm:AssistantMessageItem">

    <StackPanel Margin="0,8">
        <!-- 思考内容（如果有） -->
        <Border IsVisible="{Binding HasThinking}"
                Margin="0,0,0,12"
                Padding="12"
                Background="{DynamicResource BackgroundBrush}"
                CornerRadius="6"
                BorderBrush="{DynamicResource BorderBrush}"
                BorderThickness="1">
            <StackPanel>
                <TextBlock Text=" 思考中..."
                           Foreground="{DynamicResource TextTertiaryBrush}"
                           FontSize="12"
                           Margin="0,0,0,8" />
                <TextBlock Text="{Binding ThinkingContent}"
                           Foreground="{DynamicResource TextTertiaryBrush}"
                           TextWrapping="Wrap"
                           FontSize="13"
                           FontStyle="Italic" />
            </StackPanel>
        </Border>

        <!-- 主要内容（Markdown 渲染） -->
        <md:MarkdownScrollViewer 
            Markdown="{Binding Content}"
            Theme="Dark"
            FontFamily="Consolas, Menlo, Monaco, Courier New, monospace"
            FontSize="14"
            Background="Transparent" />

        <!-- 流式指示器 -->
        <TextBlock IsVisible="{Binding IsStreaming}"
                   Text="▌"
                   Foreground="{DynamicResource PrimaryBrush}"
                   FontSize="14"
                   Margin="0,4,0,0" />
    </StackPanel>
</UserControl>
```

### 3.3 鼠标选取支持

Markdown.Avalonia 的 MarkdownScrollViewer 默认支持：
- 鼠标拖拽选择文本
- Ctrl+A 全选
- Ctrl+C 复制
- 右键上下文菜单（如果支持）

如果 Markdown.Avalonia 不支持鼠标选取，使用降级方案：

**降级方案：自定义 SelectableMarkdownViewer**

```csharp
public class SelectableMarkdownViewer : UserControl
{
    private readonly TextBlock _textBlock;
    private string _selectedText = "";
    
    public static readonly StyledProperty<string> MarkdownProperty =
        AvaloniaProperty.Register<SelectableMarkdownViewer, string>(nameof(Markdown));
    
    public string Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }
    
    public SelectableMarkdownViewer()
    {
        var scrollViewer = new ScrollViewer();
        _textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Menlo, Monaco, Courier New, monospace"),
            FontSize = 14
        };
        
        scrollViewer.Content = _textBlock;
        Content = scrollViewer;
        
        // 监听鼠标事件实现选择
        _textBlock.PointerPressed += OnPointerPressed;
        _textBlock.PointerMoved += OnPointerMoved;
        _textBlock.PointerReleased += OnPointerReleased;
    }
    
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty)
        {
            UpdateContent();
        }
    }
    
    private void UpdateContent()
    {
        var markdown = Markdown ?? "";
        _textBlock.Inlines = MarkdownParser.Parse(markdown);
    }
    
    // ... 鼠标选择实现
}
```

---

## 4. 页脚状态栏

### 4.1 FooterBar 控件

新建 `Views/Controls/FooterBar.axaml`：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="LubanAgentCodex.Views.Controls.FooterBar">

    <Border Background="{DynamicResource CardBrush}"
            BorderBrush="{DynamicResource DividerBrush}"
            BorderThickness="0,1,0,0"
            Padding="12,6">
        <Grid ColumnDefinitions="Auto,Auto,Auto,Auto,*">
            <!-- 权限模式 -->
            <TextBlock Grid.Column="0" 
                       Name="PermissionModeText"
                       FontWeight="Bold"
                       FontSize="12"
                       Margin="0,0,16,0" />
            
            <!-- 分隔符 -->
            <TextBlock Grid.Column="1" 
                       Text="│"
                       Foreground="{DynamicResource DividerBrush}"
                       Margin="0,0,16,0" />
            
            <!-- 工作目录 -->
            <TextBlock Grid.Column="2" 
                       Name="WorkingDirectoryText"
                       Foreground="{DynamicResource TextSecondaryBrush}"
                       FontSize="12"
                       Margin="0,0,16,0" />
            
            <!-- Git 分支 -->
            <TextBlock Grid.Column="3" 
                       Name="GitBranchText"
                       Foreground="{DynamicResource TextSecondaryBrush}"
                       FontSize="12"
                       Margin="0,0,16,0" />
            
            <!-- Token 用量 -->
            <TextBlock Grid.Column="4" 
                       Name="TokenUsageText"
                       HorizontalAlignment="Right"
                       Foreground="{DynamicResource TextTertiaryBrush}"
                       FontSize="12" />
        </Grid>
    </Border>
</UserControl>
```

### 4.2 FooterBar.axaml.cs

```csharp
public partial class FooterBar : UserControl
{
    private TextBlock? _permissionModeText;
    private TextBlock? _workingDirectoryText;
    private TextBlock? _gitBranchText;
    private TextBlock? _tokenUsageText;
    
    public FooterBar()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _permissionModeText = this.FindControl<TextBlock>("PermissionModeText");
        _workingDirectoryText = this.FindControl<TextBlock>("WorkingDirectoryText");
        _gitBranchText = this.FindControl<TextBlock>("GitBranchText");
        _tokenUsageText = this.FindControl<TextBlock>("TokenUsageText");
    }
    
    public void UpdatePermissionMode(ToolPermissionMode mode)
    {
        if (_permissionModeText == null) return;
        
        _permissionModeText.Text = mode switch
        {
            ToolPermissionMode.Default => "[default]",
            ToolPermissionMode.Plan => "[plan]",
            ToolPermissionMode.AcceptEdits => "[accept-edits]",
            ToolPermissionMode.BypassPermissions => "[bypass]",
            _ => "[unknown]"
        };
        
        _permissionModeText.Foreground = mode switch
        {
            ToolPermissionMode.Default => Brush.Parse("#FFFFFF"),
            ToolPermissionMode.Plan => Brush.Parse("#AFA9EC"),
            ToolPermissionMode.AcceptEdits => Brush.Parse("#85B7EB"),
            ToolPermissionMode.BypassPermissions => Brush.Parse("#F09595"),
            _ => Brush.Parse("#FFFFFF")
        };
    }
    
    public void UpdateWorkingDirectory(string path)
    {
        if (_workingDirectoryText == null) return;
        
        // 简化路径，只显示最后两段
        var parts = path.Split(Path.DirectorySeparatorChar);
        var display = parts.Length > 2 
            ? $"{parts[^2]}{Path.DirectorySeparatorChar}{parts[^1]}"
            : path;
        
        _workingDirectoryText.Text = display;
    }
    
    public void UpdateGitBranch(string branch)
    {
        if (_gitBranchText == null) return;
        _gitBranchText.Text = $"git:{branch}";
    }
    
    public void UpdateTokenUsage(long tokens)
    {
        if (_tokenUsageText == null) return;
        
        var display = tokens > 1000 
            ? $"{tokens / 1000.0:F1}k tok"
            : $"{tokens} tok";
        
        _tokenUsageText.Text = display;
    }
}
```

### 4.3 数据源

使用 `FooterDataProvider`（从 CLI 移植）：

```csharp
public class FooterDataProvider
{
    private readonly IServiceProvider _services;
    
    public FooterDataProvider(IServiceProvider services)
    {
        _services = services;
    }
    
    public string GetGitBranch()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --abbrev-ref HEAD",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var branch = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return branch;
        }
        catch
        {
            return "unknown";
        }
    }
    
    public long GetTokenUsage()
    {
        var sessionManager = _services.GetService<ISessionManager>();
        if (sessionManager?.CurrentSession == null) return 0;
        
        // 从会话统计中获取 token 用量
        var stats = sessionManager.GetSessionStatsAsync(sessionManager.CurrentSession.SessionId)
            .GetAwaiter().GetResult();
        return stats?.TotalTokens ?? 0;
    }
    
    public string GetWorkingDirectory()
    {
        var workspaceManager = _services.GetService<IWorkspaceManager>();
        return workspaceManager?.CurrentWorkspace?.RootPath ?? Directory.GetCurrentDirectory();
    }
}
```

### 4.4 集成到 MainWindow

修改 `MainWindow.axaml`，在底部添加 FooterBar：

```xml
<Grid RowDefinitions="Auto,*,Auto,Auto">
    <!-- TitleBar -->
    <controls:TitleBar Grid.Row="0" Name="TitleBar" />
    
    <!-- MessageStream -->
    <controls:MessageStream Grid.Row="1" Name="MessageStream" />
    
    <!-- InputBox -->
    <controls:InputBox Grid.Row="2" Name="InputBox" />
    
    <!-- FooterBar -->
    <controls:FooterBar Grid.Row="3" Name="FooterBar" />
</Grid>
```

---

## 5. 实现顺序

1. **第1阶段：页脚状态栏**
   - 新建 FooterBar 控件
   - 移植 FooterDataProvider
   - 集成到 MainWindow

2. **第2阶段：权限模式增强**
   - 添加 Shift+Tab 快捷键
   - 实现 BypassPermissions 二次确认
   - 更新页脚显示

3. **第3阶段：Markdown 渲染**
   - 添加 Markdown.Avalonia NuGet 包
   - 修改 AssistantMessageView
   - 测试鼠标选取功能

4. **第4阶段：命令系统补全**
   - 新建 ProviderManageWindow
   - 新建 WorkManageWindow
   - 新建 RagManageWindow
   - 修改 MainWindowViewModel 命令路由
   - 测试所有命令

---

## 6. 测试计划

### 6.1 单元测试

- 命令路由测试（/provider, /skill, /rule, /mcp, /work, /rag）
- 权限模式切换测试
- Markdown 解析测试

### 6.2 集成测试

- 端到端命令执行测试
- 权限模式切换 + 二次确认测试
- Markdown 渲染 + 鼠标选取测试
- 页脚状态栏数据更新测试

### 6.3 UI 测试

- 所有管理窗口的打开/关闭
- 表格数据的正确显示
- 按钮的点击响应
- 键盘快捷键功能

---

## 7. 风险和缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| Markdown.Avalonia 不兼容当前 Avalonia 版本 | Markdown 渲染无法使用 | 使用降级方案（自定义解析器） |
| Markdown.Avalonia 不支持鼠标选取 | 无法选择和复制文本 | 使用自定义 SelectableMarkdownViewer |
| 管理窗口功能过于复杂 | 开发工作量大 | 分批实现，先实现核心功能 |
| git 命令执行失败 | 页脚无法显示分支 | 捕获异常，显示 "unknown" |

---

## 8. 文件清单

### 新建文件

| 文件 | 说明 |
|------|------|
| `Views/ProviderManageWindow.axaml` | Provider 管理窗口 |
| `Views/ProviderManageWindow.axaml.cs` | Provider 管理窗口代码 |
| `Views/WorkManageWindow.axaml` | 工作区管理窗口 |
| `Views/WorkManageWindow.axaml.cs` | 工作区管理窗口代码 |
| `Views/RagManageWindow.axaml` | RAG 知识库管理窗口 |
| `Views/RagManageWindow.axaml.cs` | RAG 知识库管理窗口代码 |
| `Views/Controls/FooterBar.axaml` | 页脚状态栏控件 |
| `Views/Controls/FooterBar.axaml.cs` | 页脚状态栏代码 |
| `Services/FooterDataProvider.cs` | 页脚数据提供者 |

### 修改文件

| 文件 | 修改内容 |
|------|---------|
| `LubanAgentCodex.csproj` | 添加 Markdown.Avalonia NuGet 包引用 |
| `ViewModels/MainWindowViewModel.cs` | 添加命令路由、权限模式切换 |
| `Views/MainWindow.axaml` | 添加 FooterBar 控件 |
| `Views/MainWindow.axaml.cs` | 添加键盘事件处理 |
| `Views/Controls/AssistantMessageView.axaml` | 替换为 Markdown 渲染控件 |
| `GlobalUsings.cs` | 添加 Markdown.Avalonia 命名空间 |
