/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： AliyunCredentialKeyAliases
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/19
*描述：云效凭据键历史别名（框架已去厂商化，别名由产品承载）
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效凭据键别名：
/// 把历史拼写键（<c>yunxiao_pat</c> / <c>codeup:pat</c> / <c>yunxiao_organization_id</c> 等）
/// 解析为规范键（<c>connector:yunxiao:token</c> / <c>connector:yunxiao:organizationid</c>），
/// 并给出对应的历史环境变量名，供凭据存储回退读取（旧安装无需人工迁移）。
/// </summary>
/// <remarks>
/// 框架层只保留通用约定（<see cref="CredentialKeys"/>），厂商知识一律在产品侧承载：
/// 本类型即云效的 <see cref="ICredentialKeyAliases"/> 实现，由
/// <c>AddAliyunDevOpsConnector</c> 注册覆盖框架默认的空实现。
/// </remarks>
public sealed class AliyunCredentialKeyAliases : ICredentialKeyAliases
{
    /// <summary>单例（无状态，可安全共享）。</summary>
    public static AliyunCredentialKeyAliases Instance { get; } = new();

    /// <summary>默认实例（<c>yunxiao</c>）的令牌规范键：<c>connector:yunxiao:token</c>。</summary>
    public static string DefaultTokenKey { get; } = CredentialKeys.ForConnectorToken(YunxiaoConnector.DefaultName);

    /// <summary>默认实例（<c>yunxiao</c>）的组织 ID 规范键：<c>connector:yunxiao:organizationid</c>。</summary>
    public static string DefaultOrganizationIdKey { get; } =
        CredentialKeys.ForConnectorField(YunxiaoConnector.DefaultName, "organizationId");

    /// <summary>历史拼写 → 规范键。</summary>
    private static readonly Dictionary<string, string> Resolvers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yunxiao_pat"] = DefaultTokenKey,
        ["yunxiao:pat"] = DefaultTokenKey,
        ["yunxiao_token"] = DefaultTokenKey,
        ["codeup_pat"] = DefaultTokenKey,
        ["codeup:pat"] = DefaultTokenKey,
        ["codeup:token"] = DefaultTokenKey,

        ["yunxiao_organization_id"] = DefaultOrganizationIdKey,
        ["yunxiao_org_id"] = DefaultOrganizationIdKey,
        ["yunxiao:org"] = DefaultOrganizationIdKey,
        ["codeup_org_id"] = DefaultOrganizationIdKey,
        ["codeup:org"] = DefaultOrganizationIdKey,
    };

    /// <summary>规范键 → 历史拼写（回退读取顺序）。</summary>
    private static readonly Dictionary<string, string[]> LegacyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [DefaultTokenKey] =
        [
            "yunxiao_pat",
            "yunxiao:pat",
            "yunxiao_token",
            "codeup_pat",
            "codeup:pat",
            "codeup:token",
        ],
        [DefaultOrganizationIdKey] =
        [
            "yunxiao_organization_id",
            "yunxiao_org_id",
            "yunxiao:org",
            "codeup_org_id",
            "codeup:org",
        ],
    };

    /// <summary>规范键 → 历史环境变量名（回退兜底）。</summary>
    private static readonly Dictionary<string, string> LegacyEnvironmentVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        [DefaultTokenKey] = "LUBAN_YUNXIAO_PAT",
        [DefaultOrganizationIdKey] = "LUBAN_YUNXIAO_ORGANIZATION_ID",
    };

    /// <inheritdoc />
    public string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return CredentialKeys.Normalize(key);

        var trimmed = key.Trim();
        return Resolvers.TryGetValue(trimmed, out var canonical)
            ? canonical
            : CredentialKeys.Normalize(trimmed);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetLegacyAliases(string canonicalKey)
    {
        if (string.IsNullOrWhiteSpace(canonicalKey))
            return Array.Empty<string>();

        return LegacyAliases.TryGetValue(CredentialKeys.Normalize(canonicalKey), out var aliases)
            ? aliases
            : Array.Empty<string>();
    }

    /// <inheritdoc />
    public string? GetLegacyEnvironmentVariableName(string canonicalKey)
    {
        if (string.IsNullOrWhiteSpace(canonicalKey))
            return null;

        return LegacyEnvironmentVariables.TryGetValue(CredentialKeys.Normalize(canonicalKey), out var variable)
            ? variable
            : null;
    }
}