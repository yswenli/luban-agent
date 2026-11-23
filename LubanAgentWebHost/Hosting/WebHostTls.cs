/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Hosting
*文件名： WebHostTls
*描述：宿主 TLS：自签证书的生成、持久化与启用
*
*****************************************************************************/

using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SAEA.Sockets.Ssl;

namespace LubanAgentWebHost.Hosting;

/// <summary>
/// 已启用的宿主证书。
/// </summary>
/// <param name="Certificate">含私钥的服务端证书。</param>
/// <param name="Sha256">证书 DER 的 SHA-256 指纹（大写十六进制），客户端据此放行自签证书。</param>
/// <param name="Reused">是否复用了磁盘上已有的证书（false 表示本次重新签发）。</param>
public sealed record WebHostTlsCertificate(X509Certificate2 Certificate, string Sha256, bool Reused);

/// <summary>
/// 宿主 TLS 的证书管理与启用。
/// </summary>
/// <remarks>
/// <para>
/// 证书是自签的：局域网 IP 拿不到公网信任的证书，走系统信任库又等于给整机装一个可信根，
/// 因此采用指纹绑定——客户端只放行发现文件里登记的那一张证书，等价于「只信任自己那台宿主」。
/// </para>
/// <para>
/// 证书落盘复用，浏览器只需在首次访问时确认一次；到期前自动重签并覆盖同一个 pfx。
/// 重签会换指纹，客户端需重启才能重新读到新指纹。
/// </para>
/// </remarks>
public static class WebHostTls
{
    /// <summary>pfx 文件的本地保护口令。</summary>
    /// <remarks>只用于保护落盘的私钥文件，不是任何外部服务的凭据，也不参与鉴权。</remarks>
    private const string PfxPassword = "luban-agent-webhost-local";

    /// <summary>证书主题。</summary>
    private const string SubjectName = "CN=LuBanAgent Local";

    /// <summary>证书有效期。</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(730);

    /// <summary>临期阈值：剩余寿命不足该值时提前重签，避免运行中突然过期。</summary>
    private static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    /// <summary>
    /// 默认证书文件路径（当前用户的本地应用数据目录）。
    /// </summary>
    public static string DefaultCertificatePath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.GetTempPath();
            }

            return Path.Combine(root, "luban-agent", "webhost-cert.pfx");
        }
    }

    /// <summary>
    /// 取得（必要时签发）证书并让 MVC 应用以 TLS 启动。
    /// </summary>
    /// <remarks>
    /// 必须在 <c>Start()</c> 之前调用。协议固定为 TLS 1.2：实测 SAEA 的服务端握手在本机
    /// <c>Tls12 | Tls13</c> 组合下会以「给函数提供的标志无效」失败，单独给 TLS 1.2 才成功。
    /// </remarks>
    /// <param name="application">尚未启动的 MVC 应用。</param>
    /// <param name="settings">启动参数（取证书路径与是否启用）。</param>
    /// <returns>已启用的证书信息。</returns>
    public static WebHostTlsCertificate Enable(SAEAMvcApplication application, WebHostSettings settings)
    {
        ArgumentNullException.ThrowIfNull(application);

        var tls = EnsureCertificate(settings);

        application.UseSsl(options =>
        {
            options.Enabled = true;
            options.ServerCertificate = tls.Certificate;
            options.Protocols = SslProtocols.Tls12;
            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
        });

        return tls;
    }

    /// <summary>
    /// 取得（必要时签发）宿主证书，但不启用 TLS。
    /// </summary>
    /// <remarks>供信任安装这类不启动服务的场景复用同一份证书与口令，避免另起一套 pfx 读写逻辑。</remarks>
    /// <param name="settings">启动参数（取证书路径）。</param>
    /// <returns>证书与指纹；是否复用磁盘已有证书。</returns>
    public static WebHostTlsCertificate EnsureCertificate(WebHostSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var path = string.IsNullOrWhiteSpace(settings.CertificatePath)
            ? DefaultCertificatePath
            : settings.CertificatePath!.Trim();

        var (certificate, reused) = LoadOrCreate(path);
        return new WebHostTlsCertificate(certificate, ComputeSha256(certificate), reused);
    }

    /// <summary>计算证书 DER 的 SHA-256 指纹（大写十六进制，与 <c>X509Certificate2.Thumbprint</c> 无关）。</summary>
    /// <param name="certificate">证书。</param>
    /// <returns>64 个大写十六进制字符。</returns>
    public static string ComputeSha256(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    /// <summary>
    /// 读取磁盘上的证书；缺失、损坏、无私钥或临近过期时重新签发并覆盖。
    /// </summary>
    /// <param name="path">pfx 路径。</param>
    /// <returns>证书与是否复用。</returns>
    private static (X509Certificate2 Certificate, bool Reused) LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                var existing = X509CertificateLoader.LoadPkcs12FromFile(path, PfxPassword);
                if (existing.HasPrivateKey && existing.NotAfter.ToUniversalTime() > DateTime.UtcNow + RenewBefore)
                {
                    return (existing, true);
                }

                existing.Dispose();
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            {
                // 文件损坏或口令不符：按不存在处理，重新签发覆盖
                AgentWebRuntime.Log($"宿主证书无法读取，将重新签发（{path}）：{ex.Message}");
            }
        }

        return (CreateAndSave(path), false);
    }

    /// <summary>签发自签证书并写入 pfx。</summary>
    /// <param name="path">pfx 路径。</param>
    /// <returns>含私钥的证书。</returns>
    private static X509Certificate2 CreateAndSave(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lanHost = WebHostAccess.GetPreferredLanHost();
        var ipAddresses = new List<string> { "127.0.0.1" };
        if (!string.Equals(lanHost, "127.0.0.1", StringComparison.Ordinal))
        {
            ipAddresses.Add(lanHost);
        }

        var bundle = SaeaCertificateFactory.CreateSelfSigned(new SaeaCertificateOptions
        {
            Purpose = SaeaCertificatePurpose.Server,
            Subject = SubjectName,
            DnsNames = ["localhost", Environment.MachineName],
            IpAddresses = ipAddresses.ToArray(),
            KeySize = 2048,
            NotBefore = DateTimeOffset.Now.AddDays(-1),
            NotAfter = DateTimeOffset.Now + Lifetime,
            PfxPassword = PfxPassword
        });

        bundle.SavePfx(path, PfxPassword);

        // 从磁盘读回：让后续每次启动都走同一条加载路径，避免内存证书与落盘内容不一致
        return X509CertificateLoader.LoadPkcs12FromFile(path, PfxPassword);
    }
}