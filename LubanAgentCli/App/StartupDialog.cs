/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App
*文件名： StartupDialog
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/12
*描述：启动向导对话框，显示初始化进度，支持取消和错误处理
*
*****************************************************************************/

using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LubanAgentCli.App;

internal class StartupDialog : Dialogs.BaseDialog
{
    private readonly string[] _args;
    private readonly IUiDispatcher _dispatcher;
    private readonly ITuiUiService _ui;
    private readonly Label _bannerLabel;
    private readonly Label _statusLabel;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _retrievalCts;

    /// <summary>
    /// 初始化完成后的服务容器。
    /// </summary>
    public AgentHostServices? Services { get; private set; }

    /// <summary>
    /// 到统一 Agent 服务的连接（随应用退出释放；自动拉起模式下会一并结束服务子进程）。
    /// </summary>
    public AgentClientConnection? Connection { get; private set; }

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
        : base("正在初始化...", width: 64)
    {
        _args = args;
        _dispatcher = dispatcher;
        _ui = ui;

        Height = 18;

        _bannerLabel = new Label
        {
            X = 1,
            Y = ContentTop,
            Width = 20,
            Height = 1,
            Text = "luban-agent-cli"
        };
        _bannerLabel.SetScheme(new Scheme(
            new Attribute(TuiTheme.Accent, TuiTheme.PopupBackground, TextStyle.Bold))
        {
            Normal = new Attribute(TuiTheme.Accent, TuiTheme.PopupBackground, TextStyle.Bold),
            Focus = new Attribute(TuiTheme.Accent, TuiTheme.PopupBackground, TextStyle.Bold),
            HotNormal = new Attribute(TuiTheme.Accent, TuiTheme.PopupBackground, TextStyle.Bold),
            HotFocus = new Attribute(TuiTheme.Accent, TuiTheme.PopupBackground, TextStyle.Bold),
        });
        Add(_bannerLabel);

        _statusLabel = new Label
        {
            X = 1,
            Y = Pos.Bottom(_bannerLabel) + 1,
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

            Report("② 连接统一 Agent 服务...");
            Connection = await AgentClientHostBuilder.ConnectAsync(new AgentClientOptions
            {
                // 服务子进程与客户端同工作目录，保持相对路径与工作区语义一致
                WorkingDirectory = Directory.GetCurrentDirectory()
            });

            Report("③ 准备嵌入模型...");
            _retrievalCts = new CancellationTokenSource();
            _dispatcher.Invoke(() => _cancelButton.Visible = true);
            var (embedder, modelManager) = await RetrievalBootstrap.PrepareAsync(
                config, Report, _retrievalCts.Token);
            _dispatcher.Invoke(() => _cancelButton.Visible = false);

            Report("④ 构建服务容器...");
            Services = StartupRunner.BuildHost(config, embedder, modelManager, Connection);

            var promptAuthorization = new Func<WorkspaceInfo, Task<bool>>(async ws =>
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
            });

            // 授权弹窗必须挂到实际生效的实现上：远程模式下工作区管理器是 HTTP 实现，
            // 它自己不会弹窗，未挂回调时一律按拒绝处理。
            switch (Services.GetRequiredService<IWorkspaceManager>())
            {
                case WorkspaceManager local:
                    local.AuthorizationPrompt = promptAuthorization;
                    break;
                case HttpWorkspaceManager remote:
                    remote.AuthorizationPrompt = promptAuthorization;
                    break;
            }

            // ⑤ 准备工作区：选最近活跃非 LLM Wiki 工作区（无则弹创建表单）、授权、设为当前并可生成 AGENTS.md。
            // 必须在向导的运行中主循环内调用——解析器会弹出嵌套模态表单。
            Report("⑤ 准备工作区...");
            await StartupWorkspaceResolver.ResolveAsync(Services, _ui, Notices, remoteMode: Connection is not null);

            Success = true;
            _dispatcher.Invoke(RequestStop);
        }
        catch (OperationCanceledException)
        {
            Report("已取消");
            Success = false;
            await CloseConnectionAsync();
            _dispatcher.Invoke(RequestStop);
        }
        catch (Exception ex)
        {
            Logger.Error("Startup initialization failed", ex);
            Report($"错误: {ex.Message}");
            Success = false;
            await CloseConnectionAsync();
            _dispatcher.Invoke(RequestStop);
        }
    }

    /// <summary>
    /// 初始化失败时释放已建立的连接（自动拉起模式下同时结束服务子进程）。
    /// </summary>
    private async Task CloseConnectionAsync()
    {
        if (Connection is not null)
        {
            await Connection.DisposeAsync();
            Connection = null;
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
