/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Entities
*文件名： DbA2ATask
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：A2A 任务数据库实体
*
*****************************************************************************/
namespace LubanAgentCore.Entities;

/// <summary>
/// A2A 任务实体。
/// <para>仅存储协议无关的检索列（TaskId/ContextId/State/StatusTimestamp），
/// 完整 <c>AgentTask</c> 快照以 JSON 形式保存于 <see cref="Payload"/>，
/// 由 agent 侧 <c>SqlSugarA2ATaskStore</c> 负责编解码，Core 不引用 A2A SDK。</para>
/// </summary>
[SugarTable("ai_a2a_task", "A2A 任务")]
public class DbA2ATask : EntityBase
{
    /// <summary>
    /// A2A 任务ID（唯一标识）
    /// </summary>
    [SugarColumn(ColumnDescription = "任务ID", Length = 128, IsNullable = false, UniqueGroupNameList = new[] { "a2a_task_id_unique" })]
    public string TaskId { get; set; } = "";

    /// <summary>
    /// 上下文ID
    /// </summary>
    [SugarColumn(ColumnDescription = "上下文ID", Length = 128, IsNullable = true)]
    public string? ContextId { get; set; }

    /// <summary>
    /// 任务状态（TaskState 序数值）
    /// </summary>
    [SugarColumn(ColumnDescription = "任务状态", IsNullable = false)]
    public int State { get; set; }

    /// <summary>
    /// 状态时间戳（UTC）
    /// </summary>
    [SugarColumn(ColumnDescription = "状态时间戳", IsNullable = true)]
    public DateTime? StatusTimestamp { get; set; }

    /// <summary>
    /// 完整 AgentTask 快照（JSON）
    /// </summary>
    [SugarColumn(ColumnDescription = "任务快照", ColumnDataType = "text", IsNullable = false)]
    public string Payload { get; set; } = "";
}