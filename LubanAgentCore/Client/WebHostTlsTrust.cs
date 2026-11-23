/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Client
*文件名： WebHostTlsTrust
*描述：客户端对宿主自签证书的指纹信任（只放行登记过的那一张）
*
*****************************************************************************/

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace LubanAgentCore.Client;

/// <summary>
/// 客户端侧的宿主证书信任策略。
/// </summary>
/// <remarks>
/// <para>
/// 宿主用自签证书，系统信任库不认；这里改成指纹绑定：发现文件里登记的 SHA-256
/// 就是唯一可信证书，连到别的服务或证书被替换都会被拒。
/// </para>
/// <para>
/// 发现文件没有指纹（旧版宿主）时不做任何放行——旧版宿主本来也没启用 TLS，
/// 走 HTTP 时这个回调根本不会被调用，因此不影响向后兼容。
/// </para>
/// </remarks>
public static class WebHostTlsTrust
{
    /// <summary>
    /// 生成只信任指定指纹的证书校验回调。
    /// </summary>
    /// <param name="expectedSha256">可信证书的 SHA-256 指纹（十六进制，忽略大小写）；为空表示不启用绑定。</param>
    /// <returns>校验回调；<paramref name="expectedSha256"/> 为空时返回 null（交回系统默认校验）。</returns>
    public static RemoteCertificateValidationCallback? CreateCallback(string? expectedSha256)
        => string.IsNullOrWhiteSpace(expectedSha256)
            ? null
            : (_, certificate, _, _) => Matches(certificate, expectedSha256);

    /// <summary>
    /// 判断证书指纹是否与预期一致。
    /// </summary>
    /// <param name="certificate">服务端证书。</param>
    /// <param name="expectedSha256">预期指纹。</param>
    /// <returns>一致返回 true。</returns>
    public static bool Matches(X509Certificate? certificate, string? expectedSha256)
    {
        if (certificate is null || string.IsNullOrWhiteSpace(expectedSha256))
        {
            return false;
        }

        var expected = Normalize(expectedSha256);
        if (expected.Length == 0)
        {
            return false;
        }

        // 优先用 X509Certificate2.RawData 计算，避免 Thumbprint 的 SHA-1 口径混淆
        var raw = certificate is X509Certificate2 cert2
            ? cert2.RawData
            : X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()).RawData;

        var actual = Convert.ToHexString(SHA256.HashData(raw));
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(actual),
            System.Text.Encoding.ASCII.GetBytes(expected));
    }

    /// <summary>去掉分隔符并转成大写，容忍 <c>AA:BB:CC</c> 与 <c>aabbcc</c> 两种写法。</summary>
    /// <param name="value">指纹文本。</param>
    /// <returns>64 位大写十六进制；非法输入返回空串。</returns>
    private static string Normalize(string value)
    {
        var buffer = new StringBuilder(64);
        foreach (var ch in value)
        {
            if (ch is ':' or '-' or ' ')
            {
                continue;
            }

            buffer.Append(char.ToUpperInvariant(ch));
        }

        return buffer.Length == 64 ? buffer.ToString() : string.Empty;
    }
}