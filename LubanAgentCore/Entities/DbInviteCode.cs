/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Entities
*文件名： DbInviteCode
*描述：注册邀请码实体
*
*****************************************************************************/

namespace LubanAgentCore.Entities;

/// <summary>
/// 注册邀请码：一次有效，过期或已被使用即不可再用。
/// </summary>
[SugarTable("ai_invite_code", "注册邀请码")]
public class DbInviteCode : EntityBase
{
    /// <summary>邀请码（大写字母与数字，去掉易混字符）。</summary>
    [SugarColumn(ColumnDescription = "邀请码", Length = 32, IsNullable = false,
        UniqueGroupNameList = ["invite_code_unique"])]
    public string Code { get; set; } = "";

    /// <summary>创建者用户标识。</summary>
    [SugarColumn(ColumnDescription = "创建者", Length = 64, IsNullable = true)]
    public string? CreatedBy { get; set; }

    /// <summary>过期时间（UTC）。</summary>
    [SugarColumn(ColumnDescription = "过期时间", IsNullable = false)]
    public DateTime ExpiresAt { get; set; }

    /// <summary>使用者用户标识；为空表示尚未使用。</summary>
    [SugarColumn(ColumnDescription = "使用者", Length = 64, IsNullable = true)]
    public string? UsedBy { get; set; }

    /// <summary>使用时间（UTC）。</summary>
    [SugarColumn(ColumnDescription = "使用时间", IsNullable = true)]
    public DateTime? UsedAt { get; set; }
}