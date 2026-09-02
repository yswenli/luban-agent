/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： ProviderEditDialog
*版本号： V1.0.0.0
*唯一标识：Provider 编辑对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/2
*描述：Provider 添加/编辑对话框，选择类型并填写 name、apiKey、baseUrl
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LubanAgentCore.Configuration;

namespace LubanAgentCodex.Views;

/// <summary>
/// Provider 添加/编辑结果
/// </summary>
public class ProviderEditResult
{
    /// <summary>Provider 名称（小写）</summary>
    public string Name { get; set; } = "";

    /// <summary>API 密钥</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>API 基础地址，为空时使用默认</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Provider 添加/编辑对话框
/// </summary>
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

    /// <summary>
    /// 无参构造函数（Avalonia XAML 加载需要）
    /// </summary>
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

    /// <summary>
    /// 编辑模式构造函数
    /// </summary>
    /// <param name="existing">已存在的 Provider 配置</param>
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
