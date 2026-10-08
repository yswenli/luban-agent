/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App.Dialogs
*文件名： FormDialog
*版本号： V1.0.0.0
*唯一标识：表单对话框
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/22
*描述：表单对话框。显示标题、动态字段列表和"确定/取消"按钮。
*      支持单行输入（TextField）、多行输入（TextView）、密码输入。
*      通过 <see cref="Values"/> 属性获取用户输入值。取消时为 null。
*      Enter 触发校验并提交，Esc 取消。
*
*****************************************************************************/
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

#pragma warning disable CS0618

namespace LubanAgentCli.App.Dialogs;

/// <summary>
/// 表单对话框。显示标题、动态字段列表和"确定/取消"按钮。
/// </summary>
internal sealed class FormDialog : BaseDialog
{
    private readonly List<View> _inputs;
    private readonly Func<bool> _validate;

    /// <summary>
    /// 用户输入值列表。取消时为 null。
    /// </summary>
    public List<string>? Values { get; private set; }

    /// <summary>
    /// 输入视图列表。供外部访问输入控件。
    /// </summary>
    public IReadOnlyList<View> InputViews => _inputs;

    /// <summary>
    /// 初始化表单对话框。
    /// </summary>
    /// <param name="title">对话框标题。</param>
    /// <param name="fields">字段定义列表。</param>
    /// <param name="validate">校验函数。返回 true 表示通过，false 表示校验失败（阻止关闭）。</param>
    public FormDialog(
        string title,
        IReadOnlyList<FormField> fields,
        Func<bool> validate)
        : base(title)
    {
        _validate = validate;
        _inputs = new List<View>(fields.Count);

        var contentHeight = fields.Sum(f => f.Multiline ? 8 : 3);
        Height = Math.Min(contentHeight + 6, 32);

        View lastView = TitleLabel!;
        foreach (var f in fields)
        {
            var label = new Label { X = 1, Width = Dim.Fill(2), Text = f.Required ? $"{f.Label} *" : f.Label };
            label.Y = Pos.Bottom(lastView) + 1;
            Add(label);

            if (f.Multiline)
            {
                var tv = new TextView
                {
                    X = 1,
                    Width = Dim.Fill(2),
                    Height = 6,
                    Text = f.InitialValue ?? string.Empty
                };
                tv.Y = Pos.Bottom(label) + 1;
                Add(tv);
                _inputs.Add(tv);
                lastView = tv;
            }
            else
            {
                var tf = new TextField
                {
                    X = 1,
                    Width = Dim.Fill(2),
                    Text = f.InitialValue ?? string.Empty
                };
                if (f.IsPassword) tf.Secret = true;
                tf.Y = Pos.Bottom(label) + 1;
                Add(tf);
                _inputs.Add(tf);
                lastView = tf;
            }
        }

        var cancelButton = new Button { Text = "取消" };
        var okButton = new Button { Text = "确定" };
        AddButton(cancelButton);
        AddButton(okButton);

        // 初始焦点放到第一个输入框
        if (_inputs.Count > 0) _inputs[0].SetFocus();
    }

    /// <inheritdoc/>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        // Enter 触发 Accept：校验并提交
        if (!_validate())
        {
            return true;
        }
        Values = _inputs.Select(GetValue).ToList();
        return base.OnAccepting(args);
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            Values = null;
            RequestStop();
            return true;
        }
        return base.OnKeyDown(key);
    }

    /// <summary>
    /// 从视图获取输入值。
    /// </summary>
    private static string GetValue(View v) => v switch
    {
        TextField tf => tf.Text ?? string.Empty,
        TextView tv => tv.Text ?? string.Empty,
        _ => string.Empty
    };
}

#pragma warning restore CS0618
