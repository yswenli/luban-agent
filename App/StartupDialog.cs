/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
*文件名： StartupDialog
*版本号： V1.0.0.0
*唯一标识：启动向导对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/12
*描述：启动向导对话框，显示初始化进度，支持取消和错误处理
*
*****************************************************************************/
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LubanAgent.App;

/// <summary>
/// 启动向导对话框。显示初始化进度，支持取消和错误处理。
/// </summary>
internal class StartupDialog : Dialog
{
    private readonly string[] _args;
    private readonly IUiDispatcher _dispatcher;
    private readonly ITuiUiService _ui;
    private readonly Label _statusLabel;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _retrievalCts;

    /// <summary>
    /// 初始化完成后的服务容器。
    /// </summary>
    public IServiceProvider? Services { get; private set; }

    /// <summary>
    /// 启动提示集合。
    /// </summary>
    public List<string> Notices { get; } = new();

    /// <summary>
    /// 初始化是否成功。
    /// </summary>
    public bool Success { get; private set; }

    /// <summary>
    /// 初始化启动向导对话框。
    /// </summary>
    public StartupDialog(string[] args, IUiDispatcher dispatcher, ITuiUiService ui)
    {
        _args = args;
        _dispatcher = dispatcher;
        _ui = ui;

        Title = "正在初始化...";
        X = Pos.Center();
        Y = Pos.Center();
        Width = 64;
        Height = 16;

        _statusLabel = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(2),
            Height = Dim.Fill(3),
            Text = ""
        };
        Add(_statusLabel);

        _cancelButton = new Button { Text = "取消" };
        _cancelButton.Accepting += (_, _) =>
        {
            _retrievalCts?.Cancel();
        };
        _cancelButton.Visible = false;
        AddButton(_cancelButton);

        _ = Task.Run(RunInitializationAsync);
    }

    private async Task RunInitializationAsync()
    {
        try
        {
            Report("① 加载配置...");
            var config = StartupRunner.BuildConfiguration(_args);
            config.InitConfigUtil();
            ProviderHelper.Initialize(config);

            Report("② 初始化数据库...");
            var messages = DatabaseInitializer.Initialize();
            foreach (var msg in messages) Report(msg);

            Report("③ 准备嵌入模型...");
            _retrievalCts = new CancellationTokenSource();
            _dispatcher.Invoke(() => _cancelButton.Visible = true);
            var (embedder, modelManager) = await StartupRunner.PrepareRetrievalAsync(
                config, Report, _retrievalCts.Token);
            _dispatcher.Invoke(() => _cancelButton.Visible = false);

            Report("④ 构建服务容器...");
            Services = StartupRunner.BuildServiceProvider(config, embedder, modelManager);

            var workspaceManager = Services.GetRequiredService<IWorkspaceManager>() as WorkspaceManager;
            if (workspaceManager != null)
            {
                workspaceManager.AuthorizationPrompt = async ws =>
                {
                    var tcs = new TaskCompletionSource<bool>();
                    _dispatcher.Invoke(() =>
                    {
                        try
                        {
                            tcs.TrySetResult(_ui.Confirm("工作区授权",
                                $"工作区 '{ws.Name}' 需要授权才能访问...\n是否授权？",
                                defaultValue: false));
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    });
                    return await tcs.Task;
                };
            }

            Report("⑤ 初始化工作区...");
            await StartupRunner.InitializeWorkspaceAsync(Services, Notices);

            Success = true;
            _dispatcher.Invoke(RequestStop);
        }
        catch (OperationCanceledException)
        {
            Report("已取消");
            Success = false;
            _dispatcher.Invoke(RequestStop);
        }
        catch (Exception ex)
        {
            Logger.Error("Startup initialization failed", ex);
            Report($"错误: {ex.Message}");
            Success = false;
            _dispatcher.Invoke(RequestStop);
        }
    }

    private void Report(string message)
    {
        _dispatcher.Invoke(() =>
        {
            var current = _statusLabel.Text ?? "";
            _statusLabel.Text = string.IsNullOrEmpty(current) ? message : $"{current}\n{message}";
        });
    }
}
