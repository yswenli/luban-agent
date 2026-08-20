/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*Author：yswenli
*命名空间：LubanAgent.Configuration
*文件名： ProviderEndpointConfig
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/7
*描述：Provider 端点与扩展信息配置模型
*
*****************************************************************************/
namespace LubanAgentCore.Configuration;

/// <summary>
/// Provider 端点信息，包含 API 地址及其描述
/// </summary>
public class ProviderEndpointInfo
{
    /// <summary>
    /// API 端点地址
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 端点描述
    /// </summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 扩展的 Provider 信息，包含显示名称、可用端点列表及预设模型列表
/// </summary>
public class ExtendedProviderInfo
{
    /// <summary>
    /// Provider 显示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 可用的 API 端点列表
    /// </summary>
    public List<ProviderEndpointInfo> Endpoints { get; set; } = new();

    /// <summary>
    /// 预设的模型列表
    /// </summary>
    public List<string> Models { get; set; } = new();
}
