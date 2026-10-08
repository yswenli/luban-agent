/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App.Dialogs
*文件名： ProgressDialog
*版本号： V1.0.0.0
*唯一标识：进度对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/22
*描述：进度对话框。显示标题和实时状态文本。无按钮。
*      通过 <see cref="UpdateStatus"/> 方法更新状态文本。
*      closable=false 时禁止 Esc/Ctrl+Q 关闭，用于长耗时任务。
*
*****************************************************************************/
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace LubanAgentCli.App.Dialogs;

/// <summary>
/// 进度对话框。显示标题和实时状态文本。无按钮。
/// </summary>
internal sealed class ProgressDialog : BaseDialog
{
    private readonly bool _closable;
    private readonly Label _statusLabel;

    /// <summary>
    /// 初始化进度对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="initialStatus">初始状态文本。</param>
    /// <param name="closable">是否允许用户按键关闭。false 时禁止 Esc/Ctrl+Q。</param>
    public ProgressDialog(string title, string initialStatus, bool closable)
        : base(title, width: 64)
    {
        _closable = closable;
        Height = 8;

        _statusLabel = new Label
        {
            X = 1,
            Y = ContentTop,
            Width = Dim.Fill(2),
            Height = Dim.Fill(2),
            Text = initialStatus
        };
        Add(_statusLabel);
    }

    /// <summary>
    /// 更新状态文本。
    /// </summary>
    /// <param name="message">新的状态文本。</param>
    public void UpdateStatus(string message)
    {
        _statusLabel.Text = message;
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        // 不可关闭时吞掉 Esc/Ctrl+Q，保证长耗时任务期间提示全程可见
        if (!_closable && (key == Key.Esc || key == Key.Q.WithCtrl))
        {
            return true;
        }
        return base.OnKeyDown(key);
    }
}
