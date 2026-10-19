/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgent.Connectors.AliyunDevOps
*文件名： IYunxiaoCredentialProvider
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：云效凭据提供者接口
*
*****************************************************************************/

namespace LubanAgent.Connectors.AliyunDevOps;

/// <summary>
/// 云效凭据提供者接口 - 用于获取个人访问令牌（PAT）
/// </summary>
public interface IYunxiaoCredentialProvider
{
    /// <summary>
    /// 获取云效个人访问令牌（PAT）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>PAT 字符串，未配置时返回 null</returns>
    Task<string?> GetPatAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按<b>连接器实例名</b>获取云效个人访问令牌（PAT）。
    /// </summary>
    /// <remarks>
    /// Q15 泛化：凭据键为 <c>connector:&lt;实例名&gt;:token</c>，同一提供方的多实例各用各的 PAT。
    /// 本成员带默认实现，退化为默认实例（<c>yunxiao</c>），既有第三方实现者无需改动即可编译。
    /// </remarks>
    /// <param name="connectorName">连接器实例名；<c>null</c>/空白 视为默认实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>PAT 字符串，未配置时返回 null。</returns>
    Task<string?> GetPatAsync(string? connectorName, CancellationToken cancellationToken = default)
        => GetPatAsync(cancellationToken);
}