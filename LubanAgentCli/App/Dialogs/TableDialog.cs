/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App.Dialogs
*文件名： TableDialog
*版本号： V1.0.0.0
*唯一标识：表格对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/22
*描述：表格对话框。显示标题、数据表格和操作按钮。
*      支持可选模式（selectable=true，显示"选择"按钮）和只读模式（selectable=false，显示"关闭"按钮）。
*      通过 <see cref="SelectedIndex"/> 属性获取用户选择的行索引。
*      Enter 确认当前选中行，Esc 关闭。
*
*****************************************************************************/
using System.Data;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace LubanAgentCli.App.Dialogs;

/// <summary>
/// 表格对话框。显示标题、数据表格和操作按钮。
/// </summary>
internal sealed class TableDialog : BaseDialog
{
    private readonly TableView _table;
    private readonly bool _selectable;

    /// <summary>
    /// 用户选择的行索引。只读模式或未选择时为 -1。
    /// </summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>
    /// 初始化表格对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="columns">列名数组。</param>
    /// <param name="rows">行数据列表。每行为一列值的集合。</param>
    /// <param name="selectable">是否可选模式。true 显示"选择"按钮，false 显示"关闭"按钮。</param>
    public TableDialog(string title, string[] columns, IReadOnlyList<IReadOnlyList<string>> rows, bool selectable)
        : base(title, width: 72)
    {
        _selectable = selectable;

        var dt = new DataTable();
        foreach (var c in columns) dt.Columns.Add(c);
        foreach (var r in rows)
        {
            var cells = columns.Select((_, i) => i < r.Count ? (object)(r[i] ?? string.Empty) : string.Empty).ToArray();
            dt.Rows.Add(cells);
        }

        _table = new TableView
        {
            X = 1,
            Y = ContentTop,
            Width = Dim.Fill(2),
            Height = Dim.Fill(3),
            Table = new DataTableSource(dt),
            FullRowSelect = true,
            MultiSelect = false
        };
        // 移除表格边框
        if (_table.Border is not null)
            _table.Border.LineStyle = Terminal.Gui.Drawing.LineStyle.None;
        Add(_table);

        Height = 26;

        var actionButton = new Button { Text = selectable ? "选择" : "关闭" };
        AddButton(actionButton);
        _table.SetFocus();
    }

    /// <inheritdoc/>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        // Enter 确认当前选中行
        if (_selectable)
        {
            try
            {
                SelectedIndex = _table.Value?.SelectedCell.Y ?? -1;
            }
            catch { SelectedIndex = -1; }
        }
        return base.OnAccepting(args);
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            RequestStop();
            return true;
        }
        return base.OnKeyDown(key);
    }
}
