/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Models.Blocks
*文件名： Block
*版本号： V1.0.0.0
*唯一标识：Block 抽象基类
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/11
*描述：会话区文档模型的最小渲染单元。子类提供 Layout/Render/HitTest 三项核心方法，
*构成自绘契约。
*
*****************************************************************************/
namespace LubanAgentCli.App.Models.Blocks;

/// <summary>
/// 会话区文档模型的最小渲染单元。子类通过覆写 <see cref="Layout(int)"/>、
/// <see cref="Render(List{RenderLine}, int)"/> 与 <see cref="HitTest(int)"/>
/// 构成自绘契约，由 ConversationDocument 管理生命周期、ConversationView 消费。
/// </summary>
public abstract class Block
{
    /// <summary>从 Block 创建时间到完成的耗时，仅在 IsComplete 为 true 时有效。</summary>
    public TimeSpan? Duration { get; protected set; }

    /// <summary>
    /// 当前 Block 渲染后的总行数。由 <see cref="Layout(int)"/> 计算并设置。
    /// </summary>
    public int LineCount { get; protected set; } = 1;

    /// <summary>是否允许折叠。不可折叠的 Block 忽略 IsCollapsed。</summary>
    public virtual bool IsFoldable => true;

    /// <summary>是否处于折叠状态。折叠时 LineCount 为 1（单行摘要）。</summary>
    public bool IsCollapsed { get; set; }

    /// <summary>Block 创建时间。</summary>
    public DateTime StartedAt { get; } = DateTime.Now;

    /// <summary>Block 内容是否已全部到达（不再有后续 token）。</summary>
    public bool IsComplete { get; protected set; }

    /// <summary>上次 Layout 时使用的宽度。子类可读取以判断是否需要重新 Layout。</summary>
    protected int LastLayoutWidth { get; private set; }

    /// <summary>
    /// 根据当前宽度计算本 Block 的行数，更新 <see cref="LineCount"/>。
    /// 子类覆写时须负责设置 LineCount 的值。
    /// </summary>
    /// <param name="width">可用宽度（列数）。</param>
    public virtual void Layout(int width)
    {
        LastLayoutWidth = width;
    }

    /// <summary>
    /// 生成当前 Block 的渲染行列表。子类覆写时向 <paramref name="lines"/> 追加 RenderLine。
    /// 调用方负责确保 width 与最近一次 <see cref="Layout"/> 的 width 一致。
    /// </summary>
    /// <param name="lines">渲染行输出列表，子类 append 到末尾。</param>
    /// <param name="width">可用宽度（列数）。</param>
    public abstract void Render(List<RenderLine> lines, int width);

    /// <summary>
    /// 鼠标命中测试。给定 Block 内部的行号（从 0 开始），返回命中的交互动作。
    /// 子类覆写以实现折叠切换、选项点击、链接打开等。
    /// </summary>
    /// <param name="localLine">Block 内部行号（0-based）。</param>
    /// <returns>命中的动作；若无交互项则返回 null。</returns>
    public virtual HitActionResult? HitTest(int localLine) => null;

    /// <summary>
    /// 标记 Block 内容已全部到达，记录耗时。
    /// </summary>
    public virtual void MarkComplete()
    {
        if (IsComplete) return;
        IsComplete = true;
        Duration = DateTime.Now - StartedAt;
    }
}

/// <summary>
/// 鼠标/键盘命中测试的交互动作结果。
/// </summary>
/// <param name="Type">动作类型。</param>
/// <param name="Data">附带的上下文数据（如选项值、URL 等）。</param>
public sealed record HitActionResult(HitActionType Type, object? Data = null);

/// <summary>
/// 命中动作类型。
/// </summary>
public enum HitActionType
{
    /// <summary>切换折叠/展开。</summary>
    ToggleCollapse,

    /// <summary>内联选择块选中选项。</summary>
    SelectOption,

    /// <summary>打开外部 URL。</summary>
    OpenUrl,

    /// <summary>自定义动作。</summary>
    Custom
}
