/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*Author：yswenli
*命名空间：LubanAgent.Configuration
*文件名： ProviderConfig
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/7
*描述：Provider 配置模型
*
*****************************************************************************/
namespace LubanAgent.Configuration;

/// <summary>
/// AI Provider 配置模型，描述一个可用的模型服务提供商及其连接信息
/// </summary>
public class ProviderConfig
{
    /// <summary>
    /// Provider 名称（小写），作为唯一标识
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// API 密钥
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// API 基础地址，为空时使用默认地址
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 显示名称，为空时使用库中定义的默认显示名称
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 该 Provider 支持的模型列表
    /// </summary>
    public List<string> SupportedModels { get; set; } = new();

    /// <summary>
    /// 用户自定义的模型列表
    /// </summary>
    public List<string> CustomModels { get; set; } = new();

    /// <summary>
    /// 网络请求超时时间（秒），为空时使用默认值
    /// </summary>
    public int? NetworkTimeoutSeconds { get; set; }
}
