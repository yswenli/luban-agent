/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App.Dialogs
*文件名： BaseDialog
*版本号： V1.0.0.0
*唯一标识：弹出层基类
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/22
*描述：弹出层基类，提供统一的样式、标题、按钮和行为。
*      所有自定义弹出层（ConfirmDialog、ChooseDialog、FormDialog、TableDialog、ProgressDialog）
*      均继承此基类，确保视觉风格一致。
*
*****************************************************************************/
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;

namespace LubanAgentCli.App.Dialogs;

/// <summary>
/// 弹出层基类。提供统一的无边框样式、灰色背景、白色文字、加粗左对齐标题。
/// 子类通过 <see cref="ContentTop"/> 定位内容起始位置，并自行调用 <c>AddButton</c> 添加按钮。
/// </summary>
internal class BaseDialog : Dialog
{
    /// <summary>标题标签。子类可通过此字段修改标题样式或隐藏标题。</summary>
    protected Label? TitleLabel;

    /// <summary>弹出层背景色。子类可通过构造函数参数覆盖默认值。</summary>
    protected readonly Color DialogBackground;

    /// <summary>
    /// 初始化弹出层基类。
    /// </summary>
    /// <param name="title">标题文本。为空时不显示标题栏。</param>
    /// <param name="width">弹出层宽度。默认 72。</param>
    /// <param name="background">背景色。默认使用 <see cref="TuiTheme.PopupBackground"/>。</param>
    public BaseDialog(
        string title,
        int width = 72,
        Color? background = null)
    {
        DialogBackground = background ?? TuiTheme.PopupBackground;

        X = Pos.Center();
        Y = Pos.Center();
        Width = width;

        ApplyStyle();
        CreateTitleLabel(title);
    }

    /// <summary>
    /// 应用统一样式：无边框、灰色背景、白色文字。
    /// </summary>
    protected void ApplyStyle()
    {
        BorderStyle = LineStyle.None;
        if (Border is not null)
            Border.LineStyle = LineStyle.None;
        SuperViewRendersLineCanvas = false;

        var normal = new Attribute(TuiTheme.PopupForeground, DialogBackground);
        SetScheme(new Scheme(normal)
        {
            Normal = normal,
            Focus = new Attribute(DialogBackground, TuiTheme.Accent),
            HotNormal = new Attribute(TuiTheme.Accent, DialogBackground, TextStyle.Bold),
            HotFocus = new Attribute(TuiTheme.Background, TuiTheme.Accent, TextStyle.Bold),
            Disabled = new Attribute(TuiTheme.SystemMessage, DialogBackground),
            Active = normal,
            HotActive = new Attribute(TuiTheme.Accent, DialogBackground, TextStyle.Bold),
            Highlight = new Attribute(DialogBackground, TuiTheme.Accent),
            Editable = normal,
            ReadOnly = new Attribute(TuiTheme.SystemMessage, DialogBackground)
        });
    }

    /// <summary>
    /// 创建标题标签。标题为加粗、左对齐、橙色，与内容间隔两行。
    /// </summary>
    /// <param name="title">标题文本。</param>
    private void CreateTitleLabel(string title)
    {
        if (string.IsNullOrEmpty(title)) return;

        TitleLabel = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(2),
            Height = 1,
            Text = title,
            TextAlignment = Alignment.Start
        };
        TitleLabel.SetScheme(new Scheme(
            new Attribute(TuiTheme.Accent, DialogBackground, TextStyle.Bold))
        {
            Normal = new Attribute(TuiTheme.Accent, DialogBackground, TextStyle.Bold),
            Focus = new Attribute(TuiTheme.Accent, DialogBackground, TextStyle.Bold),
            HotNormal = new Attribute(TuiTheme.Accent, DialogBackground, TextStyle.Bold),
            HotFocus = new Attribute(TuiTheme.Accent, DialogBackground, TextStyle.Bold),
        });
        Add(TitleLabel);
    }

    /// <summary>
    /// 内容起始位置。标题下方两行，或无标题时顶部一行。
    /// 子类使用此属性定位内容视图的 Y 坐标。
    /// </summary>
    protected Pos ContentTop => TitleLabel is not null
        ? Pos.Bottom(TitleLabel) + 2
        : Pos.Top(this) + 1;
}
