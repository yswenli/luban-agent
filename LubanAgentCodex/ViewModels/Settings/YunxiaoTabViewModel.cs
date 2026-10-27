/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.ViewModels.Settings
*文件名： YunxiaoTabViewModel
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗「云效连接」页业务（组织 ID 读取、PAT 现状、保存）
*
*****************************************************************************/

using LubanAgentCodex.Security;

namespace LubanAgentCodex.ViewModels.Settings;

/// <summary>
/// 云效连接页 ViewModel：读写经 <see cref="YunxiaoSettingsService"/>（组织 ID 落 config.json，PAT 落凭据存储）。
/// </summary>
/// <param name="services">宿主服务提供者（为 null 时视为容器不可用，操作返回失败文案）。</param>
public sealed class YunxiaoTabViewModel(IServiceProvider? services)
{
    private YunxiaoSettingsService? Settings => services?.GetService<YunxiaoSettingsService>();

    /// <summary>容器是否可用。</summary>
    public bool Available => Settings != null;

    /// <summary>读取已持久化的组织 ID（未配置返回空串）。</summary>
    public string GetOrganizationId() => Settings?.GetOrganizationId() ?? "";

    /// <summary>凭据存储中是否已有 PAT。</summary>
    public bool HasPat() => Settings?.HasPat() == true;

    /// <summary>
    /// 保存组织 ID 与 PAT；PAT 留空表示保持原值不变（避免误清空既有令牌）。
    /// </summary>
    /// <param name="organizationId">组织 ID。</param>
    /// <param name="pat">PAT 原文（空白=不修改）。</param>
    public async Task<SettingsOutcome> SaveAsync(string organizationId, string pat)
    {
        var settings = Settings;
        if (settings == null) return SettingsOutcome.Failure("云效设置服务不可用，无法保存。");

        var token = pat.Trim();
        await settings.SaveAsync(organizationId.Trim(), string.IsNullOrWhiteSpace(token) ? null : token);

        return SettingsOutcome.Success("已保存。组织 ID 与 PAT 已生效（无需重启）。");
    }
}