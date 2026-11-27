/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Entities
*文件名： DbAgentUser
*描述：Web 宿主用户实体
*
*****************************************************************************/

namespace LubanAgentCore.Entities;

/// <summary>
/// Web 宿主用户。
/// </summary>
/// <remarks>
/// <see cref="UserId"/> 同时作为用户级数据目录名与工作区归属值，注册后不可变；
/// 密码只存 PBKDF2 派生值，不保存明文。
/// </remarks>
[SugarTable("ai_user", "Web 宿主用户")]
public class DbAgentUser : EntityBase
{
    /// <summary>用户标识（稳定不变，等同登录名的小写形式）。</summary>
    [SugarColumn(ColumnDescription = "用户标识", Length = 64, IsNullable = false,
        UniqueGroupNameList = ["agent_user_id_unique"])]
    public string UserId { get; set; } = "";

    /// <summary>登录名（保留用户输入的原始大小写）。</summary>
    [SugarColumn(ColumnDescription = "登录名", Length = 64, IsNullable = false)]
    public string UserName { get; set; } = "";

    /// <summary>显示名。</summary>
    [SugarColumn(ColumnDescription = "显示名", Length = 128, IsNullable = true)]
    public string? DisplayName { get; set; }

    /// <summary>PBKDF2 密码摘要（格式见 <c>PasswordHasher</c>）。</summary>
    [SugarColumn(ColumnDescription = "密码摘要", Length = 512, IsNullable = false)]
    public string PasswordHash { get; set; } = "";

    /// <summary>是否管理员。</summary>
    [SugarColumn(ColumnDescription = "是否管理员", IsNullable = false)]
    public bool IsAdmin { get; set; }

    /// <summary>是否内置账号：不可改名、不可删除（管理员 <c>yswenli</c>）。</summary>
    [SugarColumn(ColumnDescription = "是否内置", IsNullable = false)]
    public bool IsBuiltIn { get; set; }

    /// <summary>是否已禁用：禁用后不能再登录，已签发的令牌在下次动作校验时失效。</summary>
    [SugarColumn(ColumnDescription = "是否禁用", IsNullable = false)]
    public bool Disabled { get; set; }

    /// <summary>令牌版本：改密或禁用时递增，使旧令牌立即失效。</summary>
    [SugarColumn(ColumnDescription = "令牌版本", IsNullable = false)]
    public int TokenVersion { get; set; } = 1;

    /// <summary>最后登录时间（UTC）。</summary>
    [SugarColumn(ColumnDescription = "最后登录时间", IsNullable = true)]
    public DateTime? LastLoginAt { get; set; }
}