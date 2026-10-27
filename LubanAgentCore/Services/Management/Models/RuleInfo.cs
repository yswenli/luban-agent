namespace LubanAgentCore.Services.Management.Models;

/// <summary>规则展示信息（内置与自定义规则统一视图）。</summary>
/// <param name="Id">规则标识。</param>
/// <param name="Name">展示名。</param>
/// <param name="Description">描述。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="IsBuiltin">是否内置规则。</param>
public sealed record RuleInfo(
    string Id,
    string Name,
    string Description,
    bool IsEnabled,
    bool IsBuiltin);