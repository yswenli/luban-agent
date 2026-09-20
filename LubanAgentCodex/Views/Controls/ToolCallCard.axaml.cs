/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： ToolCallCard
*版本号： V1.0.0.0
*唯一标识：工具调用卡片
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/19
*描述：工具调用显示卡片：左侧色条 + 工具图标 + 复制参数 + 结果摘要 + 紧凑参数区。

*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCore.Utils;
using System.Text.Json;
using System.Threading.Tasks;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 工具调用卡片
/// </summary>
public partial class ToolCallCard : UserControl
{
    private static readonly string[] Frames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    private Border? _accentBar;
    private TextBlock? _statusIcon;
    private TextBlock? _toolIcon;
    private TextBlock? _toolNameText;
    private TextBlock? _stateText;
    private TextBox? _argumentsText;
    private Expander? _argumentsExpander;
    private Border? _resultSummaryBorder;
    private TextBlock? _resultSummaryText;
    private Button? _copyButton;
    private ToolCallItem? _boundItem;
    private DispatcherTimer? _timer;
    private int _frameIndex;

    public ToolCallCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _accentBar = this.FindControl<Border>("AccentBar");
        _statusIcon = this.FindControl<TextBlock>("StatusIcon");
        _toolIcon = this.FindControl<TextBlock>("ToolIcon");
        _toolNameText = this.FindControl<TextBlock>("ToolNameText");
        _stateText = this.FindControl<TextBlock>("StateText");
        _argumentsText = this.FindControl<TextBox>("ArgumentsText");
        _argumentsExpander = this.FindControl<Expander>("ArgumentsExpander");
        _resultSummaryBorder = this.FindControl<Border>("ResultSummaryBorder");
        _resultSummaryText = this.FindControl<TextBlock>("ResultSummaryText");
        _copyButton = this.FindControl<Button>("CopyButton");
        UpdateState();
    }

    /// <summary>
    /// 展示在其上的友好中文动作名。
    /// </summary>
    public string DisplayName => _boundItem is null ? "" : ToolDisplayNames.GetAction(_boundItem.ToolName);

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ToolCallItem item)
        {
            BindItem(item);
        }
    }

    private void BindItem(ToolCallItem item)
    {
        _boundItem = item;

        if (_toolIcon != null)
            _toolIcon.Text = ToolDisplayNames.GetIcon(item.ToolName);

        if (_toolNameText != null)
            _toolNameText.Text = DisplayName;

        var hasArgs = item.Arguments.Count > 0;
        if (hasArgs && _argumentsText != null)
        {
            var json = JsonSerializer.Serialize(item.Arguments, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            _argumentsText.Text = json;
        }

        // 无参数时隐藏折叠区，避免出现空 Expander
        if (_argumentsExpander != null)
            _argumentsExpander.IsVisible = hasArgs;

        if (_copyButton != null)
            _copyButton.IsVisible = hasArgs;

        UpdateState();

        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ToolCallItem.State))
                UpdateState();
        };
    }

    private void UpdateState()
    {
        if (_boundItem == null) return;

        switch (_boundItem.State)
        {
            case ToolCallState.Running:
                StartTimer();
                Tick();
                SetAccent((IBrush?)Application.Current?.FindResource("WarningBrush"));
                break;
            case ToolCallState.Done:
                StopTimer();
                RenderDone();
                SetAccent((IBrush?)Application.Current?.FindResource("SuccessBrush"));
                break;
            case ToolCallState.Failed:
                StopTimer();
                RenderFailed();
                SetAccent((IBrush?)Application.Current?.FindResource("ErrorBrush"));
                break;
        }
    }

    private void SetAccent(IBrush? brush)
    {
        if (_accentBar != null && brush != null)
            _accentBar.Background = brush;
    }

    private void OnTick(object? sender, EventArgs e) => Tick();

    private void Tick()
    {
        if (_boundItem == null || _statusIcon == null || _stateText == null) return;

        var elapsed = DateTime.Now - _boundItem.Timestamp;
        var frame = Frames[_frameIndex++ % Frames.Length];
        _statusIcon.Text = frame;
        _stateText.Text = $"⏳ {elapsed.TotalSeconds:F1}s";
        _stateText.Foreground = (IBrush?)Application.Current?.FindResource("WarningBrush")
            ?? Brushes.Yellow;
    }

    private void RenderDone()
    {
        if (_boundItem == null || _statusIcon == null || _stateText == null) return;

        var elapsed = DateTime.Now - _boundItem.Timestamp;
        _statusIcon.Text = "✓";
        _stateText.Text = $"{DisplayName}完成 · {elapsed.TotalSeconds:F1}s";
        _stateText.Foreground = (IBrush?)Application.Current?.FindResource("SuccessBrush")
            ?? Brushes.LimeGreen;

        ApplyResultSummary();
    }

    private void RenderFailed()
    {
        if (_boundItem == null || _statusIcon == null || _stateText == null) return;

        var elapsed = DateTime.Now - _boundItem.Timestamp;
        _statusIcon.Text = "✗";
        _stateText.Text = $"{_boundItem.ErrorMessage ?? "工具执行失败"} · {elapsed.TotalSeconds:F1}s";
        _stateText.Foreground = (IBrush?)Application.Current?.FindResource("ErrorBrush")
            ?? Brushes.Red;

        // 失败时不显示结果摘要（即使有也以错误信息为准）
        if (_resultSummaryBorder != null)
            _resultSummaryBorder.IsVisible = false;
    }

    /// <summary>
    /// 完成/失败后回填结果摘要（仅当 ResultSummary 非空时显示）。
    /// </summary>
    private void ApplyResultSummary()
    {
        if (_boundItem == null || _resultSummaryBorder == null || _resultSummaryText == null)
            return;

        var summary = _boundItem.ResultSummary;
        if (string.IsNullOrWhiteSpace(summary))
        {
            _resultSummaryBorder.IsVisible = false;
            return;
        }

        _resultSummaryText.Text = summary;
        _resultSummaryBorder.IsVisible = true;
    }

    /// <summary>
    /// 复制参数 JSON 到剪贴板。
    /// </summary>
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (_argumentsText?.Text is null || string.IsNullOrEmpty(_argumentsText.Text))
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;

        await clipboard.SetTextAsync(_argumentsText.Text);
    }

    private void StartTimer()
    {
        StopTimer();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void StopTimer()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopTimer();
    }
}
