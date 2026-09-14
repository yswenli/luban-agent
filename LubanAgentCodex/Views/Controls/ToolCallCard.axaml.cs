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
*描述：工具调用显示卡片，支持展开参数查看
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using LubanAgentCodex.ViewModels.Messages;
using LubanAgentCore.Utils;
using System.Text.Json;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 工具调用卡片
/// </summary>
public partial class ToolCallCard : UserControl
{
    private static readonly string[] Frames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    private TextBlock? _statusIcon;
    private TextBlock? _toolNameText;
    private TextBlock? _stateText;
    private TextBox? _argumentsText;
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
        _statusIcon = this.FindControl<TextBlock>("StatusIcon");
        _toolNameText = this.FindControl<TextBlock>("ToolNameText");
        _stateText = this.FindControl<TextBlock>("StateText");
        _argumentsText = this.FindControl<TextBox>("ArgumentsText");
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

        if (_toolNameText != null)
            _toolNameText.Text = DisplayName;

        if (_argumentsText != null && item.Arguments.Count > 0)
        {
            var json = JsonSerializer.Serialize(item.Arguments, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            _argumentsText.Text = json;
        }

        UpdateState();

        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ToolCallItem.State))
                UpdateState();
        };
    }

    private void UpdateState()
    {
        if (_boundItem == null || _statusIcon == null || _stateText == null) return;

        switch (_boundItem.State)
        {
            case ToolCallState.Running:
                StopTimer();
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _timer.Tick += OnTick;
                _timer.Start();
                Tick();
                break;
            case ToolCallState.Done:
                StopTimer();
                RenderDone();
                break;
            case ToolCallState.Failed:
                StopTimer();
                RenderFailed();
                break;
        }
    }

    private void OnTick(object? sender, EventArgs e) => Tick();

    private void Tick()
    {
        if (_boundItem == null || _statusIcon == null || _stateText == null) return;

        var elapsed = DateTime.Now - _boundItem.Timestamp;
        var frame = Frames[_frameIndex++ % Frames.Length];
        _statusIcon.Text = frame;
        _stateText.Text = $"⏳ {elapsed.TotalSeconds:F1}s";
        _stateText.Foreground = Brushes.Yellow;
    }

    private void RenderDone()
    {
        if (_boundItem == null || _statusIcon == null || _stateText == null) return;

        var elapsed = DateTime.Now - _boundItem.Timestamp;
        _statusIcon.Text = "✓";
        _stateText.Text = $"{DisplayName}完成 · {elapsed.TotalSeconds:F1}s";
        _stateText.Foreground = Brushes.LimeGreen;
    }

    private void RenderFailed()
    {
        if (_boundItem == null || _statusIcon == null || _stateText == null) return;

        var elapsed = DateTime.Now - _boundItem.Timestamp;
        _statusIcon.Text = "✗";
        _stateText.Text = $"{_boundItem.ErrorMessage ?? "工具执行失败"} · {elapsed.TotalSeconds:F1}s";
        _stateText.Foreground = Brushes.Red;
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
