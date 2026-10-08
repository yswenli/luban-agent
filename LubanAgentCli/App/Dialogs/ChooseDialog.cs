/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App.Dialogs
*文件名： ChooseDialog
*版本号： V1.0.0.0
*唯一标识：选择列表对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/22
*描述：选择列表对话框。显示标题、选项列表和"确定/取消"按钮。
*      通过 <see cref="SelectedIndex"/> 属性获取用户选择的索引。
*      取消时返回 -1。Enter 确认当前选中项，Esc 取消。
*
*****************************************************************************/
using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace LubanAgentCli.App.Dialogs;

/// <summary>
/// 选择列表对话框。显示标题、选项列表和"确定/取消"按钮。
/// </summary>
internal sealed class ChooseDialog : BaseDialog
{
    private readonly ListView _list;

    /// <summary>
    /// 用户选择的索引。取消时为 -1。
    /// </summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>
    /// 初始化选择列表对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="options">选项列表。</param>
    public ChooseDialog(string title, IReadOnlyList<string> options)
        : base(title)
    {
        _list = new ListView
        {
            X = 1,
            Y = ContentTop,
            Width = Dim.Fill(2),
            Height = Math.Min(options.Count + 2, 20)
        };
        _list.SetSource(new ObservableCollection<string>(options));
        _list.SelectedItem = 0;
        Add(_list);

        Height = _list.Height + 5;

        var cancelButton = new Button { Text = "取消" };
        var okButton = new Button { Text = "确定" };
        AddButton(cancelButton);
        AddButton(okButton);
        _list.SetFocus();
    }

    /// <inheritdoc/>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        // Enter 确认当前选中项
        SelectedIndex = _list.SelectedItem ?? -1;
        return base.OnAccepting(args);
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            SelectedIndex = -1;
            RequestStop();
            return true;
        }
        return base.OnKeyDown(key);
    }
}
