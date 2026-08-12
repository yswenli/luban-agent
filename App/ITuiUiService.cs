/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.App
*文件名： ITuiUiService
*版本号： V1.0.0.0
*唯一标识：TUI UI 服务抽象
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/12
*描述：TUI 模态交互原语抽象（确认/提示/选择/表单/表格），命令层仅依赖此接口
*
*****************************************************************************/
namespace LubanAgent.App;

/// <summary>
/// 表单字段定义。
/// </summary>
/// <param name="Label">字段标签。</param>
/// <param name="IsPassword">是否密码输入（掩码显示）。</param>
/// <param name="InitialValue">初始值。</param>
/// <param name="Required">是否必填（确定时校验非空）。</param>
/// <param name="Multiline">是否多行文本（使用多行编辑区）。</param>
public sealed record FormField(
    string Label,
    bool IsPassword = false,
    string? InitialValue = null,
    bool Required = true,
    bool Multiline = false);

/// <summary>
/// TUI 模态交互服务。所有方法可从任意线程调用：
/// UI 线程直接弹窗（嵌套 modal），后台线程编组到 UI 线程并同步等待结果。
/// </summary>
public interface ITuiUiService
{
    /// <summary>确认对话框。返回 true=用户确认。defaultValue 控制默认按钮（false 时默认"否"，用于删除等危险操作）。</summary>
    bool Confirm(string title, string message, bool defaultValue = false);

    /// <summary>信息提示框（仅"确定"按钮）。</summary>
    void Notify(string title, string message);

    /// <summary>列表选择框。返回选中项索引（0 起），取消返回 null。</summary>
    int? Choose(string title, IReadOnlyList<string> options);

    /// <summary>多字段表单框。返回按字段顺序的值列表，取消返回 null。取消/校验失败时不返回部分值。</summary>
    IReadOnlyList<string>? ShowForm(string title, IReadOnlyList<FormField> fields);

    /// <summary>表格弹窗（TableView，仅查看，"关闭"按钮）。</summary>
    void ShowTable(string title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows);
}
