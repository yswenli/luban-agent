/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Entities
*文件名： DbWorkspace
*版本号： V1.0.0.0
*唯一标识：工作区实体
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：工作区实体
*
*****************************************************************************/
using LuBan.Orm.Models;
using SqlSugar;

namespace LubanAgentCli.Entities;

/// <summary>
/// 工作区实体
/// </summary>
[SugarTable("ai_workspace", "AI 工作区")]
public class DbWorkspace : EntityBase
{
    /// <summary>
    /// 工作区ID（GUID）
    /// </summary>
    [SugarColumn(ColumnDescription = "工作区ID", Length = 64, IsNullable = false)]
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// 显示名
    /// </summary>
    [SugarColumn(ColumnDescription = "名称", Length = 256, IsNullable = false)]
    public string Name { get; set; } = "";

    /// <summary>
    /// 根目录绝对路径
    /// </summary>
    [SugarColumn(ColumnDescription = "根目录", Length = 2048, IsNullable = false)]
    public string RootPath { get; set; } = "";

    /// <summary>
    /// 工作区类型：Normal | Rag
    /// </summary>
    [SugarColumn(ColumnDescription = "类型", Length = 32, IsNullable = false)]
    public string Type { get; set; } = "Normal";

    /// <summary>
    /// 归属用户
    /// </summary>
    [SugarColumn(ColumnDescription = "用户ID", Length = 64, IsNullable = true)]
    public string? UserId { get; set; }

    /// <summary>
    /// 配置目录相对路径（.luban-agent）
    /// </summary>
    [SugarColumn(ColumnDescription = "配置路径", Length = 2048, IsNullable = true)]
    public string? ConfigPath { get; set; }

    /// <summary>
    /// 最后活跃时间
    /// </summary>
    [SugarColumn(ColumnDescription = "最后活跃时间", IsNullable = true)]
    public DateTime? LastActiveAt { get; set; }

    /// <summary>
    /// 是否已授权访问根目录
    /// </summary>
    [SugarColumn(ColumnDescription = "已授权", IsNullable = false)]
    public bool IsAuthorized { get; set; } = false;
}
