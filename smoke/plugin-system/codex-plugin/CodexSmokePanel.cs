using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LuBan.AIAgent.Plugins;

namespace CodexSmokePlugin;

/// <summary>
/// 冒烟面板：同时实现 Avalonia <see cref="UserControl"/> 与框架 <see cref="IPluginPanel"/>，
/// 由宿主内容区强转挂载（PRD §3.4）。
/// </summary>
public sealed class CodexSmokePanel : UserControl, IPluginPanel
{
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#A6ADC8"));

    private readonly TextBlock _tick;

    /// <inheritdoc />
    public string Title => "冒烟面板";

    /// <summary>
    /// 构造面板（纯代码构建，无 XAML）。
    /// </summary>
    public CodexSmokePanel()
    {
        _tick = new TextBlock
        {
            Text = "UI 回调计数：0",
            Foreground = Muted
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "插件面板已挂载（P4 内容区）",
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White
                    },
                    new TextBlock
                    {
                        Text = "插件 com.demo.codex　面板键 codex-hello",
                        Foreground = Muted
                    },
                    _tick
                }
            }
        };
    }

    /// <summary>由插件经 <c>PostToUi</c> 调用，刷新计数（验证跨线程投递）。</summary>
    /// <param name="value">当前计数。</param>
    public void SetTick(int value)
    {
        _tick.Text = $"UI 回调计数：{value}（PostToUi 每秒 +1）";
    }

    /// <inheritdoc />
    public void OnUnloading()
    {
        // 本面板无外部订阅与定时器，仅演示契约实现
    }
}