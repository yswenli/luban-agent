/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*命名空间：LubanAgentCore.Configuration
*文件名： ConfigJsonContext
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/11
*描述：配置模型的 System.Text.Json 源生成上下文，供 AOT/裁剪场景序列化配置使用
*
*=================================================
*修改标记
*修改时间：2026/9/11
*修改人： yswenli
*版本号： V1.0.0.0
*描述：配置模型的 System.Text.Json 源生成上下文，供 AOT/裁剪场景序列化配置使用
*
*****************************************************************************/

namespace LubanAgentCore.Configuration;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(ProviderConfig))]
[JsonSerializable(typeof(CustomSkillConfig))]
[JsonSerializable(typeof(CustomRuleConfig))]
[JsonSerializable(typeof(McpServerConfig))]
[JsonSerializable(typeof(ConnectorWorkspaceEntry))]
internal partial class ConfigJsonContext : JsonSerializerContext
{
}

internal static class ConfigJsonOptions
{
    // 进程级惰性缓存：JsonSerializerOptions 不可变且与用户无关，保留 static 以避免重复构造。
    private static JsonSerializerOptions? _pretty;

    /// <summary>
    /// 配置序列化用的 JSON 选项：在源生成上下文基础上关闭中文转义，进程内惰性缓存。
    /// </summary>
    public static JsonSerializerOptions Pretty =>
        _pretty ??= new JsonSerializerOptions(ConfigJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
}
