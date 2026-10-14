/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Utils
*文件名： PermissionModeRules
*版本号： V1.0.0.0
*唯一标识：权限模式规则
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/14
*描述：权限模式的解析、循环、展示规则，CLI / GUI 共用，避免两端语义分叉
*
*****************************************************************************/
using LuBan.AIAgent.Abstractions;

namespace LubanAgentCore.Utils;

/// <summary>
/// 权限模式规则：统一「字符串 → 模式」解析、循环顺序与展示文案，供 CLI / GUI 复用。
/// </summary>
public static class PermissionModeRules
{
    /// <summary>
    /// 解析权限模式字符串（大小写不敏感）。无法识别时返回 <c>null</c>。
    /// </summary>
    /// <param name="value">模式文本，如 default / plan / accept-edits / bypass。</param>
    /// <returns>对应的权限模式，或 null。</returns>
    public static ToolPermissionMode? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "default" => ToolPermissionMode.Default,
        "plan" => ToolPermissionMode.Plan,
        "accept-edits" or "acceptedits" => ToolPermissionMode.AcceptEdits,
        "bypass" or "bypasspermissions" => ToolPermissionMode.BypassPermissions,
        _ => null
    };

    /// <summary>
    /// 循环切换到下一权限模式（Default → Plan → AcceptEdits → BypassPermissions → Default）。
    /// </summary>
    /// <param name="mode">当前模式。</param>
    /// <returns>下一模式。</returns>
    public static ToolPermissionMode Next(ToolPermissionMode mode) => mode switch
    {
        ToolPermissionMode.Default => ToolPermissionMode.Plan,
        ToolPermissionMode.Plan => ToolPermissionMode.AcceptEdits,
        ToolPermissionMode.AcceptEdits => ToolPermissionMode.BypassPermissions,
        ToolPermissionMode.BypassPermissions => ToolPermissionMode.Default,
        _ => ToolPermissionMode.Default
    };

    /// <summary>
    /// 权限模式的稳定键名（default / plan / accept-edits / bypass），用于页脚与命令回显。
    /// </summary>
    /// <param name="mode">权限模式。</param>
    /// <returns>键名。</returns>
    public static string ToKey(ToolPermissionMode mode) => mode switch
    {
        ToolPermissionMode.Default => "default",
        ToolPermissionMode.Plan => "plan",
        ToolPermissionMode.AcceptEdits => "accept-edits",
        ToolPermissionMode.BypassPermissions => "bypass",
        _ => "default"
    };

    /// <summary>
    /// 权限模式的中文可读名称（如「默认 (Default)」），用于 GUI 提示文案。
    /// </summary>
    /// <param name="mode">权限模式。</param>
    /// <returns>可读名称。</returns>
    public static string ToDisplayName(ToolPermissionMode mode) => mode switch
    {
        ToolPermissionMode.Default => "默认 (Default)",
        ToolPermissionMode.Plan => "计划 (Plan)",
        ToolPermissionMode.AcceptEdits => "接受编辑 (AcceptEdits)",
        ToolPermissionMode.BypassPermissions => "跳过权限 (Bypass)",
        _ => mode.ToString()
    };
}