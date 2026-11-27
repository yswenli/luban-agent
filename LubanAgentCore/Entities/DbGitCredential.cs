/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Entities
*文件名： DbGitCredential
*描述：Git 仓库凭据（账号级，按协议 + 域名 + 端口去重）
*
*****************************************************************************/

namespace LubanAgentCore.Entities;

/// <summary>
/// Git 仓库凭据：一个用户在同一域名（含端口）下只保留一条，供该域名下所有仓库复用。
/// </summary>
/// <remarks>
/// <para>
/// 唯一性由 <c>(UserId, Scheme, Host, Port)</c> 复合唯一索引保证，仓储层按这四列做 upsert。
/// 只保存域名，不保存完整仓库地址：同一 GitLab/GitHub 实例下的不同仓库共用一条凭据。
/// </para>
/// <para>
/// <see cref="Secret"/> 按产品决定明文存储（不加密）：它等价于该域名的访问令牌，
/// 读取接口只回用户名，永不回传该字段。
/// </para>
/// </remarks>
[SugarTable("ai_git_credential", "Git 仓库凭据")]
public class DbGitCredential : EntityBase
{
    /// <summary>密码 / 令牌的最大长度（与 <see cref="Secret"/> 列宽一致）。</summary>
    public const int SecretMaxLength = 1024;
    /// <summary>用户标识（Web 账号）。</summary>
    [SugarColumn(ColumnDescription = "用户标识", Length = 64, IsNullable = false,
        UniqueGroupNameList = ["git_credential_host_unique"])]
    public string UserId { get; set; } = "";

    /// <summary>协议：当前网页端只写 <c>https</c>。</summary>
    [SugarColumn(ColumnDescription = "协议", Length = 16, IsNullable = false,
        UniqueGroupNameList = ["git_credential_host_unique"])]
    public string Scheme { get; set; } = "https";

    /// <summary>域名（统一小写）。</summary>
    [SugarColumn(ColumnDescription = "域名", Length = 256, IsNullable = false,
        UniqueGroupNameList = ["git_credential_host_unique"])]
    public string Host { get; set; } = "";

    /// <summary>端口：始终写实际端口，HTTPS 默认 443。</summary>
    [SugarColumn(ColumnDescription = "端口", IsNullable = false,
        UniqueGroupNameList = ["git_credential_host_unique"])]
    public int Port { get; set; }

    /// <summary>用户名。</summary>
    [SugarColumn(ColumnDescription = "用户名", Length = 256, IsNullable = false)]
    public string UserName { get; set; } = "";

    /// <summary>密码 / 访问令牌（明文，按产品决定不加密）。</summary>
    [SugarColumn(ColumnDescription = "密码或令牌", Length = SecretMaxLength, IsNullable = false)]
    public string Secret { get; set; } = "";
}