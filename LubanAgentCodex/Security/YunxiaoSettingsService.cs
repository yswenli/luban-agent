/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.Security
*文件名： YunxiaoSettingsService
*版本号： V1.0.0.0
*唯一标识：新建
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/16
*描述：云效连接配置读写门面（组织 ID + PAT），供设置页使用
*
*****************************************************************************/
using LubanAgent.Connectors.AliyunDevOps;
using LubanAgentCore.Hosting;
using LuBan.AIAgent.Plugins.Security;

namespace LubanAgentCodex.Security;

/// <summary>
/// 云效连接配置读写门面：组织 ID 落 <see cref="YunxiaoApiOptions"/>（运行时即时生效）并持久化，
/// PAT 落 <see cref="ICredentialStore"/>（PRD §3.7 / §4.9）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="YunxiaoApiOptions"/> 由框架以工厂注册为<b>共享单例</b>，连接器每次请求都读它的
/// <c>OrganizationId</c>，故写入后无需重启即可生效。
/// </para>
/// <para>
/// PAT 只经 <see cref="ICredentialStore"/> 流转，<b>不回显</b>到 UI（设置页仅显示「已配置」）。
/// </para>
/// </remarks>
public sealed class YunxiaoSettingsService
{
    private readonly ICredentialStore _store;
    private readonly YunxiaoApiOptions _options;

    /// <summary>
    /// 创建云效设置服务。
    /// </summary>
    /// <param name="store">宿主凭据存储。</param>
    /// <param name="options">云效 API 选项（共享单例）。</param>
    public YunxiaoSettingsService(ICredentialStore store, YunxiaoApiOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 读取当前生效的组织 ID（优先运行时选项，其次凭据存储）。
    /// </summary>
    public string GetOrganizationId()
    {
        if (!string.IsNullOrWhiteSpace(_options.OrganizationId))
            return _options.OrganizationId!;

        return _store.GetAsync(AliyunCredentialKeyAliases.DefaultOrganizationIdKey).GetAwaiter().GetResult() ?? string.Empty;
    }

    /// <summary>
    /// 判断是否已配置 PAT（不回显明文）。
    /// </summary>
    public bool HasPat() => _store.Contains(AliyunCredentialKeyAliases.DefaultTokenKey);

    /// <summary>
    /// 保存云效连接配置。
    /// </summary>
    /// <param name="organizationId">组织 ID；空字符串表示清空。</param>
    /// <param name="pat">PAT；<c>null</c> 表示<b>保持原值不变</b>（设置页留空语义）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SaveAsync(
        string? organizationId,
        string? pat = null,
        CancellationToken cancellationToken = default)
    {
        var orgId = string.IsNullOrWhiteSpace(organizationId) ? null : organizationId.Trim();

        // 组织 ID：运行时共享选项 + 持久化（便于下次启动即用）
        _options.OrganizationId = orgId;
        await _store.SetAsync(AliyunCredentialKeyAliases.DefaultOrganizationIdKey, orgId, cancellationToken).ConfigureAwait(false);

        // PAT：null 视为「不修改」，避免设置页留空时误清空既有令牌
        if (pat is not null)
            await _store.SetAsync(AliyunCredentialKeyAliases.DefaultTokenKey, string.IsNullOrWhiteSpace(pat) ? null : pat.Trim(), cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// 把持久化的组织 ID 回填到运行时选项（应用启动、构建容器后调用一次）。
    /// </summary>
    public void ApplyPersistedOrganizationId()
    {
        if (!string.IsNullOrWhiteSpace(_options.OrganizationId))
            return;

        var persisted = _store.GetAsync(AliyunCredentialKeyAliases.DefaultOrganizationIdKey).GetAwaiter().GetResult();
        if (!string.IsNullOrWhiteSpace(persisted))
            _options.OrganizationId = persisted;
    }
}