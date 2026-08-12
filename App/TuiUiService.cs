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

        done.Wait();
        if (error is not null)
        {
            Logger.Error("TuiUiService 模态操作异常", error);
            throw error;
        }
        return result!;
    }

    /// <inheritdoc/>
    public bool Confirm(string title, string message, bool defaultValue = false)
    {
        return RunModal(() =>
        {
            // defaultValue=false 时"否"在前（默认按钮），危险操作防误触
            var buttons = defaultValue ? new[] { "是", "否" } : new[] { "否", "是" };
            var r = MessageBox.Query(_app, title, message, buttons);
            return defaultValue ? r == 0 : r == 1;
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

            var ok = new Button { Text = "确定", IsDefault = true };
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
            dialog.AddButton(ok);
            dialog.AddButton(cancel);

            _app.Run(dialog);
            return result >= 0 ? result : (int?)null;
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<string>? ShowForm(string title, IReadOnlyList<FormField> fields)
        => throw new NotImplementedException("ShowForm 将在 Task 3 实现");

    /// <inheritdoc/>
    public void ShowTable(string title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows)
        => throw new NotImplementedException("ShowTable 将在 Task 3 实现");
}
