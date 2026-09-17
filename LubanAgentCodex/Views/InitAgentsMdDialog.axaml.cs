/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： InitAgentsMdDialog
*版本号： V1.0.0.0
*唯一标识：初始化 AGENTS.md 对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/17
*描述：初始化 AGENTS.md 结构化表单，收集 Agent 名称/业务场景/能力清单/工具清单/输出模式
*
*****************************************************************************/
using Avalonia.Interactivity;

namespace LubanAgentCodex.Views;

/// <summary>
/// 初始化 AGENTS.md 对话框
/// </summary>
public partial class InitAgentsMdDialog : Window
{
    private TextBox? _agentNameBox;
    private TextBox? _agentSceneBox;
    private TextBox? _capabilitiesBox;
    private TextBox? _toolsBox;
    private RadioButton? _modeFullRadio;
    private RadioButton? _modeLiteRadio;
    private TextBlock? _errorText;

    /// <summary>
    /// 用户填写并确认后的生成选项；取消时为 null
    /// </summary>
    public AgentsMdOptions? Result { get; private set; }

    /// <summary>
    /// 无参构造函数（Avalonia XAML 加载需要）
    /// </summary>
    public InitAgentsMdDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 带预填值的构造函数
    /// </summary>
    /// <param name="initial">预填选项（可为 null）</param>
    public InitAgentsMdDialog(AgentsMdOptions? initial) : this()
    {
        if (initial == null) return;

        if (_agentNameBox != null) _agentNameBox.Text = initial.AgentName;
        if (_agentSceneBox != null) _agentSceneBox.Text = initial.AgentScene;
        if (_capabilitiesBox != null) _capabilitiesBox.Text = JoinLines(initial.Capabilities);
        if (_toolsBox != null) _toolsBox.Text = JoinLines(initial.Tools);

        if (string.Equals(initial.OutputMode, "lite", StringComparison.OrdinalIgnoreCase))
        {
            if (_modeLiteRadio != null) _modeLiteRadio.IsChecked = true;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        _agentNameBox = this.FindControl<TextBox>("AgentNameBox");
        _agentSceneBox = this.FindControl<TextBox>("AgentSceneBox");
        _capabilitiesBox = this.FindControl<TextBox>("CapabilitiesBox");
        _toolsBox = this.FindControl<TextBox>("ToolsBox");
        _modeFullRadio = this.FindControl<RadioButton>("ModeFullRadio");
        _modeLiteRadio = this.FindControl<RadioButton>("ModeLiteRadio");
        _errorText = this.FindControl<TextBlock>("ErrorText");

        if (this.FindControl<Button>("OkButton") is { } ok)
            ok.Click += OnOk;
        if (this.FindControl<Button>("CancelButton") is { } cancel)
            cancel.Click += (s, e) => Close(null);
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var agentName = _agentNameBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(agentName))
        {
            ShowError("请填写 Agent 名称");
            return;
        }

        var agentScene = _agentSceneBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(agentScene))
        {
            ShowError("请填写业务场景");
            return;
        }

        var outputMode = _modeLiteRadio?.IsChecked == true ? "lite" : "full";

        Result = new AgentsMdOptions
        {
            AgentName = agentName,
            AgentScene = agentScene,
            Capabilities = SplitLines(_capabilitiesBox?.Text),
            Tools = SplitLines(_toolsBox?.Text),
            OutputMode = outputMode,
        };

        Close(Result);
    }

    /// <summary>
    /// 按行拆分多行输入，去空白行
    /// </summary>
    /// <param name="text">多行文本</param>
    /// <returns>有效行列表；无有效行时返回 null</returns>
    private static IReadOnlyList<string>? SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var items = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        return items.Count == 0 ? null : items;
    }

    /// <summary>
    /// 列表转为多行文本，用于预填
    /// </summary>
    /// <param name="items">列表</param>
    /// <returns>多行文本；无有效项时返回 null</returns>
    private static string? JoinLines(IReadOnlyList<string>? items)
    {
        if (items == null || items.Count == 0) return null;

        var valid = items.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).ToList();
        return valid.Count == 0 ? null : string.Join(Environment.NewLine, valid);
    }

    private void ShowError(string msg)
    {
        if (_errorText != null)
        {
            _errorText.Text = msg;
            _errorText.IsVisible = true;
        }
    }
}