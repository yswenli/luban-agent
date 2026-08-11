/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models
*文件名： ChoiceOption
*版本号： V1.0.0.0
*唯一标识：内联选择块选项模型
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：InlineChoiceBlock 中每个选项的数据模型
*
*****************************************************************************/
namespace LubanAgent.Models;

/// <summary>
/// 内联选择块的单个选项。键盘快捷键与鼠标点击均可选中。
/// </summary>
/// <param name="Key">键盘快捷键字符（如 'Y'、'N'、'A'），不区分大小写。</param>
/// <param name="Label">选项标签（如 "允许"、"拒绝"）。</param>
/// <param name="Value">绑定到此选项的任意值（提供给回调）。</param>
/// <param name="Description">可选的补充说明，渲染在标签之后。</param>
public sealed record ChoiceOption(char Key, string Label, object Value, string? Description = null);
