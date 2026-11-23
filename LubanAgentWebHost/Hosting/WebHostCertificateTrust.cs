/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： WebHostCertificateTrust
*描述：把宿主自签证书装入当前用户「受信任的根证书颁发机构」
*
*****************************************************************************/

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// 一次信任安装的结果。
/// </summary>
/// <param name="Installed">本次是否真的写入了信任库（false 表示原本已受信任）。</param>
/// <param name="RemovedStale">顺带清掉的同主题旧证书张数（证书重签后残留的失效副本）。</param>
public sealed record WebHostTrustResult(bool Installed, int RemovedStale);

/// <summary>
/// 宿主证书的系统信任安装。
/// </summary>
/// <remarks>
/// <para>
/// 客户端的指纹绑定（<c>LubanAgentCore.Client.WebHostTlsTrust</c>）只管程序化客户端；
/// 浏览器只认系统信任链，因此要把那张红标彻底去掉，只能让证书进「受信任的根证书颁发机构」。
/// </para>
/// <para>
/// 只写 <see cref="StoreLocation.CurrentUser"/>：不需要管理员权限，也不影响本机其他用户。
/// 代价是证书每两年重签一次就会换指纹，需重新安装——因此宿主每次启动都检测一次，
/// 缺失即补装，并把同主题的旧证书清掉，避免信任库随重签不断累积。
/// </para>
/// <para>
/// 只写入证书的公钥部分：私钥留在 pfx 里，不复制进信任库。
/// </para>
/// </remarks>
public static class WebHostCertificateTrust
{
    /// <summary>
    /// 判断证书是否已在当前用户受信任根中。
    /// </summary>
    /// <param name="certificate">待检查的证书。</param>
    /// <returns>已受信任返回 true。</returns>
    public static bool IsTrusted(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var expected = ComputeSha256(certificate);
        foreach (var existing in store.Certificates)
        {
            if (string.Equals(ComputeSha256(existing), expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 确保证书已在当前用户受信任根中；缺失则安装，并清掉同主题的旧证书。
    /// </summary>
    /// <param name="certificate">宿主证书（取其中的公钥部分写入）。</param>
    /// <returns>安装结果。</returns>
    /// <exception cref="CryptographicException">信任库不可写或证书格式不可用时抛出。</exception>
    public static WebHostTrustResult EnsureTrusted(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var sha256 = ComputeSha256(certificate);
        var subject = certificate.Subject;

        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        var installed = false;
        var removedStale = 0;

        // 快照后逐张判断：Remove 会改动存储，不能边遍历边改
        var snapshot = store.Certificates;
        foreach (var existing in snapshot)
        {
            if (string.Equals(ComputeSha256(existing), sha256, StringComparison.Ordinal))
            {
                // 已受信任：仍要清掉同主题的旧副本（例如上次重签留下的）
                continue;
            }

            if (string.Equals(existing.Subject, subject, StringComparison.Ordinal))
            {
                store.Remove(existing);
                removedStale++;
            }
        }

        var present = false;
        foreach (var existing in store.Certificates)
        {
            if (string.Equals(ComputeSha256(existing), sha256, StringComparison.Ordinal))
            {
                present = true;
                break;
            }
        }

        if (!present)
        {
            // 只写公钥：把含私钥的证书实例加进信任库会连带处理私钥，没有必要
            using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
            store.Add(publicOnly);
            installed = true;
        }

        return new WebHostTrustResult(installed, removedStale);
    }

    /// <summary>计算证书 DER 的 SHA-256 指纹（64 位大写十六进制）。</summary>
    /// <param name="certificate">证书。</param>
    /// <returns>指纹文本。</returns>
    private static string ComputeSha256(X509Certificate2 certificate)
        => Convert.ToHexString(SHA256.HashData(certificate.RawData));
}