/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.Security
*文件名： CodexCredentialStore
*版本号： V1.0.0.0
*唯一标识：新建
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/16
*描述：Codex 宿主凭据存储 - 落在全局 config.json（与 Provider.ApiKey 同基线）
*
*****************************************************************************/
using LuBan.AIAgent.Plugins.Security;
using LubanAgentCore.Configuration;
using LubanAgentCore.Hosting;

namespace LubanAgentCodex.Security;

/// <summary>
/// Codex 宿主凭据存储：读写全局 <c>config.json</c> 的 <c>Integrations</c> 节（PRD §3.7 / §4.9.3）。
/// </summary>
/// <remarks>
/// <para>
/// <b>单例</b>设计：注册为 <see cref="ICredentialStore"/> 后会被云效连接器（单例）与凭据提供者（单例）消费，
/// 故不能直接注入 <c>ConfigManager</c>（它按用户 scope 注册）。改为经 <see cref="UserContextRegistry"/>
/// 按 <see cref="LocalUserDefaults.DefaultUserId"/> 取「该用户的配置管理器」——与设置页 scoped 门面
/// 解析到的是<b>同一个实例</b>，读写不分裂。
/// </para>
/// <para>
/// 存储基线沿用宿主既有策略（与 LLM <c>ApiKey</c> 同基线，不加额外加密层），环境变量兜底见
/// <see cref="CredentialKeys.GetEnvironmentVariableName"/>。
/// </para>
/// </remarks>
public sealed class CodexCredentialStore : ICredentialStore
{
    private readonly UserContextRegistry _users;

    /// <summary>
    /// 创建 Codex 凭据存储。
    /// </summary>
    /// <param name="users">用户状态注册表（单例）。</param>
    public CodexCredentialStore(UserContextRegistry users)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    /// <summary>当前用户的配置管理器（Codex 为单用户，固定默认用户）。</summary>
    private ConfigManager Config => _users.GetOrCreate(LocalUserDefaults.DefaultUserId).Config;

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalized = CredentialKeys.Normalize(key);

        var configured = Config.GetIntegration(normalized);
        if (!string.IsNullOrWhiteSpace(configured))
            return Task.FromResult<string?>(configured);

        return Task.FromResult(ReadEnvironment(normalized));
    }

    /// <inheritdoc />
    /// <remarks>值按归一化键写入全局 config.json；<c>null</c>/空表示删除该键。</remarks>
    public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        var normalized = CredentialKeys.Normalize(key);
        Config.SetIntegration(normalized, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool Contains(string key)
    {
        var normalized = CredentialKeys.Normalize(key);
        return !string.IsNullOrWhiteSpace(Config.GetIntegration(normalized)) ||
               !string.IsNullOrWhiteSpace(ReadEnvironment(normalized));
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Keys =>
        Config.Integrations.Keys.Select(CredentialKeys.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string? ReadEnvironment(string key)
    {
        var variable = CredentialKeys.GetEnvironmentVariableName(key);
        if (string.IsNullOrWhiteSpace(variable))
            return null;

        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}