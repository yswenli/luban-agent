/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Entities
*文件名： DbSession
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/27
*描述：Session 数据库实体
*
*****************************************************************************/
namespace LubanAgentCore.Entities;

/// <summary>
/// 会话实体
/// </summary>
[SugarTable("ai_session", "AI 会话")]
public class DbSession : EntityBase
{
    /// <summary>
    /// 会话ID（唯一标识）
    /// </summary>
    [SugarColumn(ColumnDescription = "会话ID", Length = 64, IsNullable = false, UniqueGroupNameList = new[] { "session_id_unique" })]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// 用户ID
    /// </summary>
    [SugarColumn(ColumnDescription = "用户ID", Length = 64, IsNullable = true)]
    public string? UserId { get; set; }

    /// <summary>
    /// 会话标题
    /// </summary>
    [SugarColumn(ColumnDescription = "会话标题", Length = 256, IsNullable = true)]
    public string? Title { get; set; }

    /// <summary>
    /// 模型名称
    /// </summary>
    [SugarColumn(ColumnDescription = "模型名称", Length = 128, IsNullable = true)]
    public string? ModelName { get; set; }

    /// <summary>
    /// Provider 名称
    /// </summary>
    [SugarColumn(ColumnDescription = "Provider", Length = 64, IsNullable = true)]
    public string? Provider { get; set; }

    /// <summary>
    /// 消息数量
    /// </summary>
    [SugarColumn(ColumnDescription = "消息数量", IsNullable = false)]
    public int MessageCount { get; set; } = 0;

    /// <summary>
    /// 总 Token 数
    /// </summary>
    [SugarColumn(ColumnDescription = "总Token数", IsNullable = false)]
    public int TotalTokens { get; set; } = 0;

    /// <summary>
    /// 工作区ID
    /// </summary>
    [SugarColumn(ColumnDescription = "工作区ID", Length = 64, IsNullable = true)]
    public string? WorkspaceId { get; set; }
}

/// <summary>
/// 会话消息实体
/// </summary>
[SugarTable("ai_session_message", "AI 会话消息")]
public class DbSessionMessage : EntityBase
{
    /// <summary>
    /// 会话ID
    /// </summary>
    [SugarColumn(ColumnDescription = "会话ID", Length = 64, IsNullable = false)]
    public string SessionId { get; set; } = "";

    /// <summary>
    /// 消息角色（user, assistant, system, tool）
    /// </summary>
    [SugarColumn(ColumnDescription = "消息角色", Length = 32, IsNullable = false)]
    public string Role { get; set; } = "";

    /// <summary>
    /// 消息内容
    /// </summary>
    [SugarColumn(ColumnDescription = "消息内容", ColumnDataType = "text", IsNullable = false)]
    public string Content { get; set; } = "";

    /// <summary>
    /// AI 思考内容（reasoning，仅 assistant 消息可能有）
    /// </summary>
    [SugarColumn(ColumnDescription = "思考内容", ColumnDataType = "text", IsNullable = true)]
    public string? Thinking { get; set; }

    /// <summary>
    /// Token 数量
    /// </summary>
    [SugarColumn(ColumnDescription = "Token数量", IsNullable = true)]
    public int? Tokens { get; set; }

    /// <summary>
    /// 工具调用信息（JSON）
    /// </summary>
    [SugarColumn(ColumnDescription = "工具调用", ColumnDataType = "text", IsNullable = true)]
    public string? ToolCalls { get; set; }

    /// <summary>
    /// 是否已被压缩并入摘要
    /// </summary>
    [SugarColumn(ColumnDescription = "已压缩", IsNullable = false)]
    public bool IsCompacted { get; set; } = false;
}