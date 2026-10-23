/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Ui
*文件名： PipelineYamlEditorWindow
*创建时间：2026/10/22
*描述：Step 7 流水线 YAML 手工微调弹窗（v0.31）：把只读预览升级为可编辑，保存后由创建/补齐直接提交
*****************************************************************************/

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace LubanAgent.Plugin.NewProject.Ui;

/// <summary>
/// Step 7「编辑 YAML…」的独立编辑窗口：<b>只做编辑与微调回传</b>，不碰能力层、不调云效。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么会话内才生效</b>：保存结果回到 <c>WizardPanel</c> 的内存字典（按端），创建/补齐时随调用传入；
/// 提交成功后实际原文落档案 <c>PipelineResults[].Yaml</c>，重开面板不回填（避免旧微调悄悄覆盖新档案配置）。
/// </para>
/// <para>
/// <b>不做 YAML 校验（C10′）</b>：面板只负责传递文本，合法性交给云效在创建时<b>原样报回</b>（如 <c>1209300</c>），
/// 插件不写业务规则、不静默丢弃。
/// </para>
/// <para>纯代码构建 UI（无 XAML）：插件以独立 ALC 加载，避免 XAML 编译产物带来的额外加载面。</para>
/// </remarks>
public sealed class PipelineYamlEditorWindow : Window
{
    private static readonly IBrush PageBackground = new SolidColorBrush(Color.Parse("#1E1E2E"));
    private static readonly IBrush EditorBackground = new SolidColorBrush(Color.Parse("#11111B"));
    private static readonly IBrush PrimaryText = new SolidColorBrush(Color.Parse("#CDD6F4"));
    private static readonly IBrush MutedText = new SolidColorBrush(Color.Parse("#A6ADC8"));

    /// <summary>YAML 编辑框。</summary>
    private readonly TextBox _editor;

    /// <summary>该端的模板渲染原文（「恢复模板」用；窗口打开后不再变化）。</summary>
    private readonly string _templateYaml;

    /// <summary>该端流水线名（<c>name:</c> 必须与之一致，同名幂等检索依赖它）。</summary>
    private readonly string _pipelineName;

    /// <summary>
    /// 构造编辑窗口。
    /// </summary>
    /// <param name="endLabel">端标签（仅用于标题与提示文案）。</param>
    /// <param name="pipelineName">该端将使用的流水线名（<c>name:</c> 的期望值）。</param>
    /// <param name="yaml">当前生效的 YAML（模板原文，或上次保存的微调文本）。</param>
    public PipelineYamlEditorWindow(string endLabel, string pipelineName, string yaml)
    {
        _pipelineName = pipelineName ?? string.Empty;
        _templateYaml = yaml ?? string.Empty;

        Title = $"手工微调流水线 YAML · {endLabel} · {_pipelineName}";
        Width = 1000;
        Height = 720;
        MinWidth = 680;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = PageBackground;

        _editor = new TextBox
        {
            Text = _templateYaml,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12,
            Background = EditorBackground,
            Foreground = PrimaryText,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(8)
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_editor, ScrollBarVisibility.Auto);

        Content = BuildContent(endLabel);
    }

    /// <summary>
    /// 组装窗口内容：标题 / 提示 / 编辑区 / 动作按钮。
    /// </summary>
    /// <param name="endLabel">端标签。</param>
    /// <returns>根布局。</returns>
    private Control BuildContent(string endLabel)
    {
        var save = new Button { Content = "保存微调", FontSize = 12 };
        save.Click += OnSaveClicked;

        var restore = new Button { Content = "恢复模板", FontSize = 12 };
        restore.Click += OnRestoreClicked;

        var cancel = new Button { Content = "取消", FontSize = 12 };
        cancel.Click += (_, _) => Close(null);

        var root = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
        };

        var header = new TextBlock
        {
            Text = $"手工微调 · {endLabel} · {_pipelineName}",
            FontSize = 14,
            Foreground = PrimaryText,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(header, 0);

        var hint = new TextBlock
        {
            Text = "只改内容、别改结构：顶层 name / stages 必须保留，且 name 必须仍是 "
                   + $"「{_pipelineName}」（改名会让同名幂等检索与档案名称对不上）。"
                   + "缩进只能用空格、不能用 Tab。保存后点「创建流水线」提交的就是这份原文；"
                   + "「一键补齐部署阶段」会在末尾自动追加部署阶段（若你已自己写了 VMDockerDeploy 则不再追加）。"
                   + "语法错误由云效原样报回（1209300），不会静默丢弃。",
            FontSize = 11,
            Foreground = MutedText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 8)
        };
        Grid.SetRow(hint, 1);

        Grid.SetRow(_editor, 2);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { restore, cancel, save }
        };
        Grid.SetRow(actions, 3);

        root.Children.Add(header);
        root.Children.Add(hint);
        root.Children.Add(_editor);
        root.Children.Add(actions);
        return root;
    }

    /// <summary>「保存微调」：带编辑区原文关窗（不做校验，合法性交给云效创建时原样报回）。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        Close(_editor.Text ?? string.Empty);
    }

    /// <summary>「恢复模板」：把编辑区重置为本次打开时的模板原文（仍需再点保存才生效）。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnRestoreClicked(object? sender, RoutedEventArgs e)
    {
        _editor.Text = _templateYaml;
        _editor.Focus();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Esc 等同「取消」：长 YAML 里误按更符合直觉的出口是丢弃本次编辑，而不是悄悄保存。
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}