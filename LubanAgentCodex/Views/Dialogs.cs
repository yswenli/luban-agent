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
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

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
}
