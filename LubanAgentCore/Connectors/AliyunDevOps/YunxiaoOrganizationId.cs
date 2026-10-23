/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： YunxiaoOrganizationId
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/23
*描述：云效组织 ID 的解析口径（显式参数空白视为未提供，回落连接器配置）
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效组织 ID 解析口径：显式参数<b>空白视为未提供</b>，回落连接器配置。
/// </summary>
/// <remarks>
/// <para>
/// 不能用 <c>organizationId ?? options.OrganizationId</c>：助手把「不知道的值」写成空串时，
/// 空串会绕过兜底，客户端随后抛「未配置组织 ID」（2026-10-23 真机 Step 3 失败即此因）。
/// </para>
/// <para>
/// 另一个兜底在提示词侧：插件把运行时组织 ID 直接注入 Step 3 / Step 7 的提示词，
/// 助手每次调用云效 MCP 都显式传同一个值；本类负责「万一还是传了空串」的最后一层。
/// </para>
/// </remarks>
public static class YunxiaoOrganizationId
{
    /// <summary>
    /// 解析本次调用使用的组织 ID。
    /// </summary>
    /// <param name="organizationId">调用方显式传入的组织 ID（<c>null</c>／空白视为未提供）。</param>
    /// <param name="configuredOrganizationId">连接器配置里的组织 ID。</param>
    /// <returns>实际使用的组织 ID；两者都没有时返回配置值（可能为 <c>null</c>／空）。</returns>
    public static string? Resolve(string? organizationId, string? configuredOrganizationId)
        => string.IsNullOrWhiteSpace(organizationId) ? configuredOrganizationId : organizationId;
}