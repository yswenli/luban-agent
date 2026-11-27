/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol
*文件名： SupportedLanguages
*描述：Web 界面语言白名单
*
*****************************************************************************/

namespace LubanAgentCore.Protocol;

/// <summary>
/// Web 界面语言白名单，服务端只接受这里的代码。
/// </summary>
/// <remarks>
/// 与前端 <c>LubanAgentWebHost/web/src/i18n/index.ts</c> 的 <c>LOCALES</c> 一一对应；
/// 增删语言时两处必须同步，否则用户选了也存不进去。
/// </remarks>
public static class SupportedLanguages
{
    /// <summary>全部受支持的界面语言代码（BCP 47）。</summary>
    public static readonly IReadOnlyList<string> All =
    [
        "en",
        "az",
        "es",
        "id",
        "it",
        "ja",
        "ko",
        "pt",
        "pl",
        "ru",
        "th",
        "tr",
        "zh-CN",
        "zh-TW"
    ];

    /// <summary>
    /// 校验并归一化语言代码；大小写不敏感（<c>zh-cn</c> → <c>zh-CN</c>）。
    /// </summary>
    /// <param name="value">待校验的语言代码。</param>
    /// <param name="normalized">归一化后的代码；校验失败时为空串。</param>
    /// <returns>是否受支持。</returns>
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        foreach (var item in All)
        {
            if (string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                normalized = item;
                return true;
            }
        }

        return false;
    }
}