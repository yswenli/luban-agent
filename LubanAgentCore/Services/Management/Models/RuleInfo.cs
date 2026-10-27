namespace LubanAgentCore.Services.Management.Models;

/// <summary>规则展示信息（内置 + 文件型 + config 自定义的统一视图）。</summary>
/// <param name="Id">规则标识。</param>
/// <param name="Name">展示名。</param>
/// <param name="Description">描述。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Origin">来源（决定写回哪个后端）。</param>
/// <param name="Priority">优先级（数值越大越优先）。</param>
/// <param name="ActionTypePattern">动作类型匹配式（config 自定义规则使用）。</param>
/// <param name="TargetPattern">目标匹配式（config 自定义规则使用）。</param>
/// <param name="Action">allow / deny（config 自定义规则使用）。</param>
public sealed record RuleInfo(
    string Id,
    string Name,
    string Description,
    bool IsEnabled,
    ManagedOrigin Origin,
    int Priority = 100,
    string ActionTypePattern = "*",
    string TargetPattern = "*",
    string Action = "deny")
{
    /// <summary>是否硬编码内置项。</summary>
    public bool IsBuiltin => Origin == ManagedOrigin.Builtin;

    /// <summary>是否 config.json 自定义项。</summary>
    public bool IsCustom => Origin == ManagedOrigin.Config;
}