/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views
*文件名： Dialogs
*版本号： V1.0.0.0
*唯一标识：统一美观对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/2
*描述：统一的确认 / 错误弹出层，复用 Dialogs.axaml 中的按钮与卡片样式
*
*****************************************************************************/
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LuBan.AIAgent.Abstractions;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LubanAgentCodex.Views;

/// <summary>
/// 统一的美观对话框（确认 / 错误）
/// </summary>
public static class Dialogs
{
    private static readonly SolidColorBrush WarningBrush = new(Color.Parse("#FFC107"));
    private static readonly SolidColorBrush ErrorBrush = new(Color.Parse("#F44336"));
    private static readonly SolidColorBrush TextPrimaryBrush = new(Colors.White);
    private static readonly SolidColorBrush TextSecondaryBrush = new(Color.Parse("#A0A0A0"));

    // 关闭非 ASCII 转义，避免参数中的中文显示为 \uXXXX
    private static readonly JsonSerializerOptions ArgumentsJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 取主题画刷；主题未提供时回退到给定画刷
    /// </summary>
    private static IBrush ResourceBrush(string key, IBrush fallback)
    {
        if (Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush)
            return brush;
        return fallback;
    }

    /// <summary>
    /// 工具执行确认对话框：工具名 + 参数(JSON) + 允许 / 本轮全部允许 / 拒绝
    /// </summary>
    /// <param name="owner">所属窗口；为空时退化为非模态展示（仍等待选择）</param>
    /// <param name="toolName">待确认的工具名</param>
    /// <param name="arguments">工具参数</param>
    /// <param name="ct">取消令牌；取消时自动关闭对话框并返回 Deny</param>
    public static async Task<ConfirmResult> ShowToolConfirmAsync(
        Window? owner,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        var hasArgs = arguments.Count > 0;
        var dlg = new Window
        {
            Title = "工具确认",
            Width = 520,
            Height = hasArgs ? 372 : 216,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        dlg.Classes.Add("dlgWindow");

        var card = new Border { Classes = { "dlgCard" } };
        var stack = new StackPanel { Spacing = 12 };

        // 头部：图标 + 标题
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = "⚠",
            FontSize = 20,
            Foreground = WarningBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = "需要确认是否执行该工具",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimaryBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(header);

        // 工具名
        stack.Children.Add(new TextBlock
        {
            Text = toolName,
            FontFamily = new FontFamily("Consolas,Menlo,monospace"),
            FontSize = 13,
            Foreground = WarningBrush,
            TextWrapping = TextWrapping.Wrap,
        });

        // 参数(JSON)：仅在有参数时展示
        if (hasArgs)
        {
            var argsBox = new TextBox
            {
                Text = JsonSerializer.Serialize(arguments, ArgumentsJsonOptions),
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas,Menlo,monospace"),
                FontSize = 12,
                Foreground = TextSecondaryBrush,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 8),
                MaxHeight = 208,
            };
            stack.Children.Add(new Border
            {
                Background = ResourceBrush("SidebarBrush", new SolidColorBrush(Color.Parse("#1E1E1E"))),
                CornerRadius = new CornerRadius(6),
                Child = argsBox,
            });
        }

        // 按钮区（固定在右下角）
        var chosen = ConfirmResult.Deny;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var deny = new Button { Content = "✗ 拒绝" };
        deny.Classes.Add("dlgDanger");
        var allowAll = new Button { Content = "本轮全部允许", MinWidth = 124 };
        allowAll.Classes.Add("dlgGhost");
        var allow = new Button { Content = "✓ 允许" };
        allow.Classes.Add("dlgPrimary");
        deny.Click += (s, e) => { chosen = ConfirmResult.Deny; dlg.Close(ConfirmResult.Deny); };
        allowAll.Click += (s, e) => { chosen = ConfirmResult.AllowAll; dlg.Close(ConfirmResult.AllowAll); };
        allow.Click += (s, e) => { chosen = ConfirmResult.Allow; dlg.Close(ConfirmResult.Allow); };
        buttons.Children.Add(deny);
        buttons.Children.Add(allowAll);
        buttons.Children.Add(allow);

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        Grid.SetRow(stack, 0);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(stack);
        grid.Children.Add(buttons);
        card.Child = grid;
        dlg.Content = card;

        // 取消令牌（Esc / 终止）：关闭对话框，按 Deny 收尾
        using var reg = ct.CanBeCanceled
            ? ct.Register(() => Dispatcher.UIThread.Post(() =>
            {
                if (dlg.IsVisible) dlg.Close(ConfirmResult.Deny);
            }))
            : default;

        if (owner != null)
            return await dlg.ShowDialog<ConfirmResult>(owner);

        // 无 owner：非模态展示，仍等待用户选择
        var tcs = new TaskCompletionSource<ConfirmResult>();
        dlg.Closed += (s, e) => tcs.TrySetResult(chosen);
        dlg.Show();
        return await tcs.Task;
    }

    /// <summary>
    /// 美观的确认对话框：图标 + 标题/说明 + 取消(幽灵)/确认(主或危险) 按钮
    /// </summary>
    public static async Task<bool> ShowConfirmAsync(
        Window? owner,
        string title,
        string message,
        string? detail = null,
        string okText = "确定",
        bool danger = false)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 384,
            Height = string.IsNullOrWhiteSpace(detail) ? 172 : 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        dlg.Classes.Add("dlgWindow");

        var card = new Border { Classes = { "dlgCard" } };
        var stack = new StackPanel { Spacing = 14 };

        // 头部：图标 + 标题
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = "⚠",
            FontSize = 20,
            Foreground = WarningBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimaryBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(header);

        if (!string.IsNullOrWhiteSpace(detail))
        {
            stack.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 12.5,
                Foreground = TextSecondaryBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(30, 0, 0, 0),
            });
        }

        // 按钮区（固定在右下角）
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var cancel = new Button { Content = "取消" };
        cancel.Classes.Add("dlgGhost");
        var ok = new Button { Content = okText };
        ok.Classes.Add(danger ? "dlgDanger" : "dlgPrimary");
        cancel.Click += (s, e) => dlg.Close(false);
        ok.Click += (s, e) => dlg.Close(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        Grid.SetRow(stack, 0);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(stack);
        grid.Children.Add(buttons);
        card.Child = grid;
        dlg.Content = card;

        if (owner == null)
        {
            dlg.Show();
            return false;
        }

        var result = await dlg.ShowDialog<bool?>(owner);
        return result == true;
    }

    /// <summary>
    /// 美观的错误提示对话框：错误图标 + 说明 + 确定(主) 按钮
    /// </summary>
    public static async Task ShowErrorAsync(Window? owner, string message)
    {
        var dlg = new Window
        {
            Title = "错误",
            Width = 384,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        dlg.Classes.Add("dlgWindow");

        var card = new Border { Classes = { "dlgCard" } };
        var stack = new StackPanel { Spacing = 16 };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = "⛔",
            FontSize = 20,
            Foreground = ErrorBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13.5,
            Foreground = TextPrimaryBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(header);

        var ok = new Button { Content = "确定", HorizontalAlignment = HorizontalAlignment.Right };
        ok.Classes.Add("dlgPrimary");
        ok.Click += (s, e) => dlg.Close();

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        Grid.SetRow(stack, 0);
        Grid.SetRow(ok, 1);
        grid.Children.Add(stack);
        grid.Children.Add(ok);
        card.Child = grid;
        dlg.Content = card;

        if (owner != null) await dlg.ShowDialog(owner);
        else dlg.Show();
    }

    /// <summary>
    /// 美观的信息提示对话框：信息图标 + 说明 + 确定(主) 按钮
    /// </summary>
    public static async Task ShowInfoAsync(Window? owner, string message)
    {
        var dlg = new Window
        {
            Title = "提示",
            Width = 384,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        dlg.Classes.Add("dlgWindow");

        var card = new Border { Classes = { "dlgCard" } };
        var stack = new StackPanel { Spacing = 16 };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.Children.Add(new TextBlock
        {
            Text = "ℹ",
            FontSize = 20,
            Foreground = new SolidColorBrush(Color.Parse("#4FC3F7")),
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13.5,
            Foreground = TextPrimaryBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(header);

        var ok = new Button { Content = "确定", HorizontalAlignment = HorizontalAlignment.Right };
        ok.Classes.Add("dlgPrimary");
        ok.Click += (s, e) => dlg.Close();

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        Grid.SetRow(stack, 0);
        Grid.SetRow(ok, 1);
        grid.Children.Add(stack);
        grid.Children.Add(ok);
        card.Child = grid;
        dlg.Content = card;

        if (owner != null) await dlg.ShowDialog(owner);
        else dlg.Show();
    }
}
