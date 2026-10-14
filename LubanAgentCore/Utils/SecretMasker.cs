/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCore.Utils
*文件名： SecretMasker
*版本号： V1.0.0.0
*唯一标识：密钥脱敏
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/14
*描述：API Key 等敏感文本的脱敏展示，CLI / GUI 共用
*
*****************************************************************************/
namespace LubanAgentCore.Utils;

/// <summary>
/// 敏感文本脱敏工具。
/// </summary>
public static class SecretMasker
{
    /// <summary>
    /// 将 API Key 脱敏显示，只保留前 4 位与后 4 位；长度不足或为空时返回 <c>****</c>。
    /// </summary>
    /// <param name="apiKey">原始 API Key。</param>
    /// <returns>脱敏后的字符串。</returns>
    public static string MaskApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
            return "****";
        return $"{apiKey[..4]}...{apiKey[^4..]}";
    }
}