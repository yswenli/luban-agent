namespace LubanAgentCore.Models;

/// <summary>
/// 工具确认块中用户的选择结果。
/// </summary>
public enum ConfirmResult
{
    /// <summary>允许本次调用。</summary>
    Allow,

    /// <summary>拒绝本次调用。</summary>
    Deny,

    /// <summary>本轮（当前 agent 交互回合内）后续同类工具调用全部允许，免确认。</summary>
    AllowAll
}
