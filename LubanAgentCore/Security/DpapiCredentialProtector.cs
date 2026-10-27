using System.Security.Cryptography;
using System.Text;

namespace LubanAgentCore.Security;

/// <summary>
/// 基于 Windows DPAPI（<see cref="DataProtectionScope.CurrentUser"/>）的凭据保护实现。
/// 加密结果以 <see cref="Marker"/> 前缀 + Base64 存储，便于识别「是否已加密」；
/// 其他平台 <see cref="IsAvailable"/> 为 <c>false</c>，加解密显式报错而不是静默降级为明文。
/// </summary>
public sealed class DpapiCredentialProtector : ICredentialProtector
{
    /// <summary>加密值前缀，用于区分明文与密文。</summary>
    public const string Marker = "dpapi:";

    /// <summary>附加熵：避免其他进程用同一用户身份直接解开本产品的密文。</summary>
    private static readonly byte[] Entropy = "LuBan.AIAgent.Credential.v1"u8.ToArray();

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (!OperatingSystem.IsWindows())
        {
            throw Unsupported("加密");
        }

        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
        return Marker + Convert.ToBase64String(cipher);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);

        // 非加密值（历史配置里的明文）原样返回，便于渐进迁移。
        if (!protectedValue.StartsWith(Marker, StringComparison.Ordinal))
        {
            return protectedValue;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw Unsupported("解密");
        }

        var cipher = Convert.FromBase64String(protectedValue[Marker.Length..]);
        var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }

    private static InvalidOperationException Unsupported(string action)
        => new($"当前平台不支持凭据{action}（仅 Windows DPAPI 可用），请改用环境变量注入凭据。");
}