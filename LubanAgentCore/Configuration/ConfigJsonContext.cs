using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LuBan.AIAgent.Configuration;

namespace LubanAgentCore.Configuration;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(ProviderConfig))]
[JsonSerializable(typeof(CustomSkillConfig))]
[JsonSerializable(typeof(CustomRuleConfig))]
[JsonSerializable(typeof(McpServerConfig))]
internal partial class ConfigJsonContext : JsonSerializerContext
{
}

internal static class ConfigJsonOptions
{
    // 进程级惰性缓存：JsonSerializerOptions 不可变且与用户无关，保留 static 以避免重复构造。
    private static JsonSerializerOptions? _pretty;

    public static JsonSerializerOptions Pretty =>
        _pretty ??= new JsonSerializerOptions(ConfigJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
}
