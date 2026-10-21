/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：LubanAgentCore.Connectors.AliyunDevOps
*文件名： YunxiaoApiOptions
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：云效 API 配置选项
*
*****************************************************************************/

namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// 云效 API 配置选项
/// </summary>
public class YunxiaoApiOptions
{
    /// <summary>
    /// 接入点（中心版固定为 openapi-rdc.aliyuncs.com）
    /// </summary>
    public string Endpoint { get; set; } = "openapi-rdc.aliyuncs.com";

    /// <summary>
    /// 组织 ID（云效企业标识）
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// HTTP 请求超时（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 失败重试次数
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// 重试延迟基数（毫秒，指数退避）
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// 是否启用详细日志（注意：不会记录 PAT 明文）
    /// </summary>
    public bool EnableVerboseLog { get; set; } = false;
}