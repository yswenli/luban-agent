/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.Security
*文件名： CodexYunxiaoCredentialProvider
*版本号： V1.0.0.0
*唯一标识：新建
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/16
*描述：Codex 宿主的云效 PAT 提供者 - 读取宿主凭据存储
*
*****************************************************************************/
using LuBan.AIAgent.Connectors.AliyunDevOps;
using LuBan.AIAgent.Plugins.Security;

namespace LubanAgentCodex.Security;

/// <summary>
/// Codex 宿主的云效 PAT 提供者：从 <see cref="ICredentialStore"/> 按<b>连接器实例名</b>读 PAT
/// （键 <c>connector:&lt;实例名&gt;:token</c>，默认实例即 <c>yunxiao_pat</c>；PRD §3.7 / §4.9）。
/// </summary>
/// <remarks>
/// <para>
/// 覆盖框架默认的 <c>ConfigurationYunxiaoCredentialProvider</c>（只读配置节点），使设置页录入的 PAT
/// <b>即时生效</b>（无需重启）：设置页写入 <see cref="ICredentialStore"/> → 本提供者在每次请求时读取。
/// </para>
/// <para>
/// <b>不缓存、不日志回显</b>明文 PAT（PRD §4.9.3）。插件能否读到 PAT 由连接器出口门禁另行判定
/// （须声明 <c>credential: ["yunxiao_pat"]</c>）。
/// </para>
/// </remarks>
public sealed class CodexYunxiaoCredentialProvider : IYunxiaoCredentialProvider
{
    private readonly ICredentialStore _store;

    /// <summary>
    /// 创建凭据提供者。
    /// </summary>
    /// <param name="store">宿主凭据存储。</param>
    public CodexYunxiaoCredentialProvider(ICredentialStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public Task<string?> GetPatAsync(CancellationToken cancellationToken = default)
        => GetPatAsync(null, cancellationToken);

    /// <inheritdoc />
    public async Task<string?> GetPatAsync(string? connectorName, CancellationToken cancellationToken = default)
    {
        // Q15：按实例名取键（默认实例的键与历史 yunxiao_pat 等价，旧键由存储层别名回退）
        var name = string.IsNullOrWhiteSpace(connectorName) ? YunxiaoConnector.DefaultName : connectorName.Trim();
        var key = CredentialKeys.ForConnectorToken(name);

        var pat = await _store.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(pat))
            return pat;

        // 存储实现（含配置节点）未命中时，再看环境变量兜底
        var variable = CredentialKeys.GetEnvironmentVariableName(key);
        var fallback = string.IsNullOrWhiteSpace(variable) ? null : Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }
}