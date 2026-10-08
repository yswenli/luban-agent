/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App
*文件名： TuiUiService
*版本号： V1.0.0.0
*唯一标识：TuiUiService 实现
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/12
*描述：基于 Terminal.Gui Dialog/MessageBox/TableView/ListView 的 ITuiUiService 实现，
*支持 UI 线程直跑与后台线程编组等待两种调用方式。
*作为外观层，内部使用 BaseDialog 子类的各个对话框组件。
*
*****************************************************************************/
using LubanAgentCli.App.Dialogs;

namespace LubanAgentCli.App;

/// <summary>
/// <see cref="ITuiUiService"/> 的 Terminal.Gui 实现。
/// 构造于 UI 线程（记录线程 ID）；模态方法在 UI 线程直接嵌套 modal Run，
/// 后台线程经 <see cref="IApplication.Invoke"/> 编组并用信号量同步等待。
/// 作为外观层，内部使用 <see cref="BaseDialog"/> 子类的各个对话框组件。
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
    /// 若当前已在 UI 线程则直接执行；否则通过 <see cref="IApplication.Invoke"/> 编组到 UI 线程。
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
            using var dialog = new ConfirmDialog(title, message, defaultValue);
            _app.Run(dialog);
            return dialog.Confirmed;
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
            using var dialog = new ChooseDialog(title, options);
            _app.Run(dialog);
            return dialog.SelectedIndex >= 0 ? dialog.SelectedIndex : (int?)null;
        });
    }

    /// <inheritdoc/>
#pragma warning disable CS0618
    public IReadOnlyList<string>? ShowForm(string title, IReadOnlyList<FormField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count == 0) return Array.Empty<string>();

        return RunModal(() =>
        {
            var inputs = new List<View>(fields.Count);

            static string GetValue(View v) => v switch
            {
                TextField tf => tf.Text ?? string.Empty,
                TextView tv => tv.Text ?? string.Empty,
                _ => string.Empty
            };

            bool Validate()
            {
                for (var i = 0; i < fields.Count; i++)
                {
                    if (fields[i].Required && string.IsNullOrWhiteSpace(GetValue(inputs[i])))
                    {
                        MessageBox.ErrorQuery(_app, title, $"{fields[i].Label} 不能为空", "确定");
                        return false;
                    }
                }
                return true;
            }

            using var dialog = new FormDialog(title, fields, Validate);
            foreach (var v in dialog.InputViews) inputs.Add(v);
            _app.Run(dialog);
            return dialog.Values;
        });
    }
#pragma warning restore CS0618

    /// <inheritdoc/>
    public void RunWithProgress(string title, string initialStatus, Func<Action<string>, Task> work, bool closable = true)
    {
        ArgumentNullException.ThrowIfNull(work);

        RunModal<object?>(() =>
        {
            using var dialog = new ProgressDialog(title, initialStatus, closable);

            var app = _app;
            Exception? error = null;
            using var completed = new ManualResetEventSlim(false);

            _ = Task.Run(async () =>
            {
                try
                {
                    await work(msg => app.Invoke(() => dialog.UpdateStatus(msg)));
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    app.Invoke(() => dialog.RequestStop());
                    completed.Set();
                }
            });

            _app.Run(dialog);

            // 等工作彻底结束再抛异常，避免未观察异常丢失；生成任务通常瞬时完成
            completed.Wait();
            if (error is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }
            return null;
        });
    }

    /// <inheritdoc/>
    public int ShowTable(string title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows, bool selectable = false)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        return RunModal(() =>
        {
            using var dialog = new TableDialog(title, columns.ToArray(), rows, selectable);
            _app.Run(dialog);
            return dialog.SelectedIndex;
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> PickFiles(string title, bool allowMultiple)
    {
        return RunModal<IReadOnlyList<string>>(() =>
        {
            using var dialog = new OpenDialog
            {
                Title = title,
                AllowsMultipleSelection = allowMultiple,
                OpenMode = OpenMode.File,
                MustExist = true,
                X = Pos.Center(),
                Y = Pos.Center(),
                Width = 80,
                Height = 24
            };
            _app.Run(dialog);
            if (dialog.Canceled || dialog.FilePaths is null)
                return Array.Empty<string>();
            return dialog.FilePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .ToList();
        });
    }
}
