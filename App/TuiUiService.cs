/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
*文件名： TuiUiService
*版本号： V1.0.0.0
*唯一标识：TuiUiService 实现
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/12
*描述：基于 Terminal.Gui Dialog/MessageBox/TableView/ListView 的 ITuiUiService 实现，
*支持 UI 线程直跑与后台线程编组等待两种调用方式
*
*****************************************************************************/
using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LubanAgent.App;

/// <summary>
/// <see cref="ITuiUiService"/> 的 Terminal.Gui 实现。
/// 构造于 UI 线程（记录线程 ID）；模态方法在 UI 线程直接嵌套 modal Run，
/// 后台线程经 <see cref="IApplication.Invoke"/> 编组并用信号量同步等待。
/// </summary>
internal sealed class TuiUiService : ITuiUiService
{
    private readonly IApplication _app;
    private readonly int _mainThreadId;

    /// <summary>
    /// 初始化 TUI UI 服务。必须在 UI 线程调用（Init 之后）。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例。</param>
    public TuiUiService(IApplication app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _mainThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>当前线程是否 UI 线程。</summary>
    private bool OnUiThread => Environment.CurrentManagedThreadId == _mainThreadId;

    /// <summary>
    /// 在 UI 线程同步执行模态操作并返回结果。
    /// </summary>
    private T RunModal<T>(Func<T> action)
    {
        if (OnUiThread)
        {
            return action();
        }

        using var done = new ManualResetEventSlim(false);
        T? result = default;
        Exception? error = null;

        _app.Invoke(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });

        // 等待不设超时——若 app 已 Dispose 且回调未执行，后台调用方会一直等待（进程退出场景无碍）。
        done.Wait();
        if (error is not null)
        {
            Logger.Error("TuiUiService 模态操作异常", error);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        return result!;
    }

    /// <inheritdoc/>
    public bool Confirm(string title, string message, bool defaultValue = false)
    {
        return RunModal(() =>
        {
            // AddButton 使最后一个按钮成为默认按钮（DefaultAcceptView）：
            // defaultValue=false 时"否"在末位（默认），危险操作防误触
            var buttons = defaultValue ? new[] { "否", "是" } : new[] { "是", "否" };
            var r = MessageBox.Query(_app, title, message, buttons);
            return defaultValue ? r == 1 : r == 0;
        });
    }

    /// <inheritdoc/>
    public void Notify(string title, string message)
    {
        RunModal<object?>(() =>
        {
            MessageBox.Query(_app, title, message, "确定");
            return null;
        });
    }

    /// <inheritdoc/>
    public int? Choose(string title, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count == 0) return null;

        return RunModal(() =>
        {
            using var dialog = new Dialog
            {
                Title = title,
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 64,
                Height = Math.Min(options.Count + 6, 24)
            };

            var list = new ListView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(2)
            };
            list.SetSource(new ObservableCollection<string>(options));
            list.SelectedItem = 0;
            dialog.Add(list);

            var result = -1;

            var ok = new Button { Text = "确定" };
            ok.Accepting += (_, _) =>
            {
                result = list.SelectedItem ?? -1;
                dialog.RequestStop();
            };
            var cancel = new Button { Text = "取消" };
            cancel.Accepting += (_, _) =>
            {
                result = -1;
                dialog.RequestStop();
            };
            // AddButton 使最后一个按钮成为默认按钮：先加"取消"再加"确定"，Enter 默认为确定
            dialog.AddButton(cancel);
            dialog.AddButton(ok);

            _app.Run(dialog);
            return result >= 0 ? result : (int?)null;
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<string>? ShowForm(string title, IReadOnlyList<FormField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count == 0) return Array.Empty<string>();

        return RunModal(() =>
        {
            // 每字段占：标签 1 行 + 输入 1 行（多行 6 行）+ 间隔 1 行；底部留 3 行给按钮
            var contentHeight = fields.Sum(f => f.Multiline ? 8 : 3);
            using var dialog = new Dialog
            {
                Title = title,
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 72,
                Height = Math.Min(contentHeight + 3, 32)
            };

            var inputs = new List<View>(fields.Count);
            var y = 0;
            foreach (var f in fields)
            {
                dialog.Add(new Label { X = 0, Y = y, Text = f.Required ? $"{f.Label} *" : f.Label });
                y++;

                if (f.Multiline)
                {
                    var tv = new TextView
                    {
                        X = 0,
                        Y = y,
                        Width = Dim.Fill(),
                        Height = 6,
                        Text = f.InitialValue ?? string.Empty
                    };
                    dialog.Add(tv);
                    inputs.Add(tv);
                    y += 6;
                }
                else
                {
                    var tf = new TextField
                    {
                        X = 0,
                        Y = y,
                        Width = Dim.Fill(),
                        Text = f.InitialValue ?? string.Empty
                    };
                    if (f.IsPassword) tf.Secret = true;
                    dialog.Add(tf);
                    inputs.Add(tf);
                    y++;
                }
                y++;
            }

            static string GetValue(View v) => v switch
            {
                TextField tf => tf.Text ?? string.Empty,
                TextView tv => tv.Text ?? string.Empty,
                _ => string.Empty
            };

            List<string>? values = null;

            var ok = new Button { Text = "确定" };
            ok.Accepting += (_, _) =>
            {
                // 必填校验：失败不关闭对话框
                for (var i = 0; i < fields.Count; i++)
                {
                    if (fields[i].Required && string.IsNullOrWhiteSpace(GetValue(inputs[i])))
                    {
                        MessageBox.ErrorQuery(_app, title, $"{fields[i].Label} 不能为空", "确定");
                        return;
                    }
                }
                values = inputs.Select(GetValue).ToList();
                dialog.RequestStop();
            };
            var cancel = new Button { Text = "取消" };
            cancel.Accepting += (_, _) =>
            {
                values = null;
                dialog.RequestStop();
            };
            // AddButton 使最后一个按钮成为默认按钮：先加"取消"再加"确定"，Enter 默认为确定
            dialog.AddButton(cancel);
            dialog.AddButton(ok);

            // 初始焦点放到第一个输入框
            if (inputs.Count > 0) inputs[0].SetFocus();

            _app.Run(dialog);
            return values;
        });
    }

    /// <inheritdoc/>
    public void ShowTable(string title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        RunModal<object?>(() =>
        {
            var dt = new System.Data.DataTable();
            foreach (var c in columns)
            {
                dt.Columns.Add(c);
            }
            foreach (var r in rows)
            {
                // 列数不足补空串，超出截断，保证 DataTable 不抛异常
                var cells = columns.Select((_, i) => i < r.Count ? (object)(r[i] ?? string.Empty) : string.Empty).ToArray();
                dt.Rows.Add(cells);
            }

            using var dialog = new Dialog
            {
                Title = title,
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 100,
                Height = 26
            };

            var table = new TableView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(2),
                Table = new DataTableSource(dt)
            };
            dialog.Add(table);

            var close = new Button { Text = "关闭" };
            close.Accepting += (_, _) => dialog.RequestStop();
            dialog.AddButton(close);

            _app.Run(dialog);
            return null;
        });
    }
}
