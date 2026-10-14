/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Configuration
*文件名： ProviderCatalog
*版本号： V1.0.0.0
*唯一标识：内置 Provider 目录
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/14
*描述：内置 Provider 预设表，CLI / GUI 共用，避免两端逐字重复与字段分叉
*
*****************************************************************************/
namespace LubanAgentCore.Configuration;

/// <summary>
/// 内置 Provider 预设项。
/// </summary>
/// <param name="Name">唯一标识（小写）。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="NeedCustomEndpoint">是否需要用户填写自定义端点。</param>
/// <param name="NeedCustomApiKey">API Key 是否为本地免密（如 Ollama），此时可留空。</param>
/// <param name="DefaultUrl">需要自定义端点时的默认地址。</param>
/// <param name="Warning">添加时的风险提示（可空）。</param>
public sealed record ProviderPreset(
    string Name,
    string DisplayName,
    bool NeedCustomEndpoint,
    bool NeedCustomApiKey,
    string DefaultUrl,
    string? Warning);

/// <summary>
/// 内置 Provider 预设目录，供 CLI / GUI 的「添加 Provider」共用，避免两端各维护一份。
/// </summary>
public static class ProviderCatalog
{
    /// <summary>
    /// 内置 Provider 预设（顺序即 UI 展示顺序）。
    /// </summary>
    public static readonly IReadOnlyList<ProviderPreset> Builtins =
    [
        new("openai", "OpenAI", false, false, "", null),
        new("azure", "Azure OpenAI", true, false, "https://your-resource.openai.azure.com", null),
        new("deepseek", "DeepSeek", false, false, "", null),
        new("kimi", "Kimi (Moonshot)", false, false, "", null),
        new("glm", "智谱 GLM", false, false, "", null),
        new("qwen", "通义千问", false, false, "", null),
        new("doubao", "豆包", false, false, "", null),
        new("claude", "Claude", false, false, "", "注意: Claude 使用 Anthropic Messages API，与 OpenAI 格式不同。\n如需使用 Claude，请通过第三方代理（如 one-api）转换为 OpenAI 兼容格式。"),
        new("gemini", "Google Gemini", false, false, "", null),
        new("ollama", "Ollama (本地)", true, true, "http://localhost:11434/v1", null),
        new("minimax", "MiniMax", false, false, "", null),
        new("ark", "字节方舟 (火山引擎)", false, false, "", null),
        new("bailian", "阿里百炼", false, false, "", null),
        new("hunyuan", "腾讯混元", false, false, "", null),
        new("mimo", "小米 MiMo", false, false, "", null),
    ];

    /// <summary>
    /// 自定义 OpenAI 兼容 API 预设（UI 中列在内置项之后）。
    /// </summary>
    public static readonly ProviderPreset Custom =
        new("custom", "自定义 OpenAI 兼容 API", true, false, "", null);

    /// <summary>
    /// 内置项 + 自定义项（顺序即 UI 展示顺序）。
    /// </summary>
    public static readonly IReadOnlyList<ProviderPreset> All = [.. Builtins, Custom];
}