/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： ChoiceBlocks
*版本号： V1.0.0.0
*唯一标识：内联选择块工厂
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：预设场景的内联选择块工厂方法（确认/二次确认/Plan退出）
*
*****************************************************************************/
namespace LubanAgent.Models.Blocks;

/// <summary>
/// 内联选择块工厂。为常见的确认、二次确认、Plan 模式退出等场景提供预设方法。
/// </summary>
public static class ChoiceBlocks
{
    /// <summary>
    /// 工具确认块：危险操作执行前要求用户确认。
    /// </summary>
    /// <param name="tool">工具名称。</param>
    /// <param name="args">工具参数。</param>
    /// <param name="onResolve">用户选择后的回调。</param>
    /// <returns>内联选择块实例。</returns>
    public static InlineChoiceBlock Confirm(
        string tool,
        IReadOnlyDictionary<string, object?>? args,
        Action<ConfirmResult> onResolve)
    {
        var desc = args is not null
            ? string.Join(", ", args.Select(kv => $"{kv.Key}={Str(kv.Value)}"))
            : string.Empty;

        return new InlineChoiceBlock(
            $"⚠ {tool}",
            desc,
            [
                new ChoiceOption('Y', "允许", ConfirmResult.Allow),
                new ChoiceOption('N', "拒绝", ConfirmResult.Deny),
                new ChoiceOption('A', "本轮全部允许", ConfirmResult.AllowAll, "后续同类工具免确认"),
            ],
            opt => onResolve((ConfirmResult)opt.Value));
    }

    /// <summary>
    /// BypassPermissions 模式二次确认块。
    /// </summary>
    /// <param name="onResolve">用户选择后的回调。</param>
    /// <returns>内联选择块实例。</returns>
    public static InlineChoiceBlock BypassConfirm(Action<bool> onResolve)
        => new(
            "⚠ Bypass Permissions",
            "将跳过所有工具确认，确定？",
            [
                new ChoiceOption('Y', "确定", true),
                new ChoiceOption('N', "取消", false),
            ],
            opt => onResolve((bool)opt.Value));

    /// <summary>
    /// Plan 模式退出确认块。
    /// </summary>
    /// <param name="pendingCount">待处理的计划项数量。</param>
    /// <param name="onResolve">用户选择后的回调。</param>
    /// <returns>内联选择块实例。</returns>
    public static InlineChoiceBlock PlanExit(int pendingCount, Action<PlanExitAction> onResolve)
        => new(
            "Plan 模式退出",
            $"有 {pendingCount} 个计划项待处理",
            [
                new ChoiceOption('E', "执行全部", PlanExitAction.ExecuteAll, "切换到 Default 并逐个确认"),
                new ChoiceOption('R', "逐个确认", PlanExitAction.ReviewEach, "逐项选择执行/跳过"),
                new ChoiceOption('D', "放弃", PlanExitAction.Discard, "丢弃所有计划项"),
            ],
            opt => onResolve((PlanExitAction)opt.Value));

    private static string Str(object? value)
    {
        if (value is null) return "null";
        var s = value.ToString();
        return s is not null && s.Length > 50 ? s[..47] + "..." : s ?? "null";
    }
}
