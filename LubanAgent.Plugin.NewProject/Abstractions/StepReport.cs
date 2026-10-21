/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Abstractions
*文件名： StepReport
*创建时间：2026/10/21
*描述：Step 4 / Step 5「助手结论文件」契约与逐端编译结果（M4-P4）
*****************************************************************************/

namespace LubanAgent.Plugin.NewProject.Abstractions;

/// <summary>
/// 助手结论文件（<c>step4-*.json</c> / <c>step5-*.json</c>）的 schema（M4-P4 定案）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么用结论文件而不是解析对话</b>：路线② 下插件驱动助手跑一轮提示词，助手的正文与工具结果
/// 都不适合做机器判定（正文会被截断、工具结果只回前 200 字符）。故约定助手把结论写成结构化 JSON，
/// 插件回读后解析——判定口径 = 文件存在 + schema 可解析 + <see cref="Ok"/> + 事件流无错误事件，四者齐才算通过。
/// </para>
/// <para>路径约定：<c>{localPath}\.luban-agent\plugin-newproject\results\step{4,5}-&lt;时间戳&gt;.json</c>。</para>
/// </remarks>
public sealed class StepReport
{
    /// <summary>步骤号（<c>4</c> 或 <c>5</c>）。</summary>
    public int Step { get; set; }

    /// <summary>助手自评是否全部通过。</summary>
    public bool Ok { get; set; }

    /// <summary>逐项检查结论（Step 5 为逐端结论）。</summary>
    public List<StepReportCheck> Checks { get; set; } = new();

    /// <summary>一句话结论 / 失败原因。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 结论文件里的单条检查项。
/// </summary>
public sealed class StepReportCheck
{
    /// <summary>
    /// 检查项名称：Step 5 固定取端标识（<c>Service</c> / <c>Admin</c> / <c>Mobile</c>，即
    /// <see cref="ProjectEnd"/> 名称），插件据此把结论对回具体的端；Step 4 可自由命名。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>该项是否通过。</summary>
    public bool Ok { get; set; }

    /// <summary>该项详情（失败时的错误摘要，如「3 个错误：CS0246 …」）。</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>该项实际执行的命令（Step 5 记录编译方式，便于用户复核「助手判断」是否合理）。</summary>
    public string? Command { get; set; }
}

/// <summary>
/// Step 5 逐端编译结果（落档案：重启后仍能看到每端编译成没成、用的什么命令）。
/// </summary>
public sealed class EndBuildResult
{
    /// <summary>端。</summary>
    public ProjectEnd End { get; set; }

    /// <summary>该端是否编译通过。</summary>
    public bool Ok { get; set; }

    /// <summary>结果摘要（失败时为错误摘要）。</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>该端实际执行的命令（助手按工程类型自行判断，如 <c>dotnet build X.sln</c> / <c>pnpm build</c>）。</summary>
    public string? Command { get; set; }

    /// <summary>结论产生时间。</summary>
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.Now;
}