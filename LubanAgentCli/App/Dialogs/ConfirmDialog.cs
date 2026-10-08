/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App.Dialogs
*文件名： ConfirmDialog
*版本号： V1.0.0.0
*唯一标识：确认对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/22
*描述：确认对话框。显示标题、消息和"是/否"按钮。
*      通过 <see cref="Result"/> 属性获取用户选择结果。
*      危险操作默认"否"防误触，确认操作默认"是"。
*      Enter 等同于默认按钮，Esc 等同于非默认按钮。
*
*****************************************************************************/
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace LubanAgentCli.App.Dialogs;

/// <summary>
/// 确认对话框。显示标题、消息和"是/否"按钮。
/// </summary>
internal sealed class ConfirmDialog : BaseDialog
{
    private readonly bool _defaultValue;
    private readonly int _yesButtonIndex;

    /// <summary>
    /// 用户是否选择"是"。默认按钮点击与 Esc 取反默认值时都会写入此属性。
    /// </summary>
    public bool Confirmed { get; private set; }

    /// <summary>
    /// 初始化确认对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="message">消息内容。</param>
    /// <param name="defaultValue">默认选中按钮。true 表示默认"是"（确认操作），false 表示默认"否"（危险操作）。</param>
    public ConfirmDialog(string title, string message, bool defaultValue)
        : base(title, width: 60)
    {
        _defaultValue = defaultValue;
        Confirmed = defaultValue;
        Height = 8;

        var msgLabel = new Label
        {
            X = 1,
            Y = ContentTop,
            Width = Dim.Fill(2),
            Height = 3,
            Text = message
        };
        Add(msgLabel);

        var yesButton = new Button { Text = "是" };
        var noButton = new Button { Text = "否" };

        // 视觉顺序：左边"是"，右边"否"
        // 默认按钮：确认操作默认"是"，危险操作默认"否"
        // Dialog.AddButton 规定"最后添加的按钮为默认按钮"（Button.IsDefault=true）。
        // 注意：非默认按钮被按下时框架只写入 Dialog.Result 并关闭对话框，不会再触发 OnAccepted，
        // 因此结果必须在 OnAccepting（默认/非默认按钮均会触发）中读取 Result 得出。
        if (defaultValue)
        {
            AddButton(noButton);
            AddButton(yesButton);
            _yesButtonIndex = 1;
            yesButton.SetFocus();
        }
        else
        {
            AddButton(yesButton);
            AddButton(noButton);
            _yesButtonIndex = 0;
            noButton.SetFocus();
        }
    }

    /// <inheritdoc/>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        var handled = base.OnAccepting(args);

        // Dialog.OnAccepting 已把 Result 设为被按按钮的 0 基索引（默认按钮与非默认按钮均会写入），
        // 而 OnAccepted 对非默认按钮不会触发，故此处据 Result 得出用户选择。
        if (base.Result is int index)
        {
            Confirmed = index == _yesButtonIndex;
        }

        return handled;
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            // Esc 取反默认值
            Confirmed = !_defaultValue;
            RequestStop();
            return true;
        }
        return base.OnKeyDown(key);
    }
}
