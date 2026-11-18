/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Models
*文件名： YunxiaoContracts
*描述：云效工作台（Web 插件）的请求 / 响应模型（入参经 payload 包装，出参进 ApiEnvelope.Data）
*
*****************************************************************************/

namespace LubanAgentWebHost.Models;

using LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>测试连接（只读探活）。</summary>
public sealed record YunxiaoTestConnectionRequest
{
    /// <summary>组织 ID。</summary>
    public string OrganizationId { get; init; } = string.Empty;

    /// <summary>PAT；省略表示用已存 PAT（设置页改地址不动令牌的场景）。</summary>
    public string? Pat { get; init; }
}

/// <summary>保存云效连接配置。</summary>
public sealed record YunxiaoSaveSettingsRequest
{
    /// <summary>组织地址（可选）；仅用于解析/校正组织 ID，<b>不落库</b>。</summary>
    public string? OrganizationAddress { get; init; }

    /// <summary>组织 ID。</summary>
    public string OrganizationId { get; init; } = string.Empty;

    /// <summary>PAT；省略/为 <c>null</c> 表示保持原值，空白串非法。</summary>
    public string? Pat { get; init; }
}

/// <summary>设置回显。</summary>
/// <param name="OrganizationId">组织 ID。</param>
/// <param name="HasPat">是否已配置 PAT（<b>绝不回明文</b>）。</param>
/// <remarks>组织地址由前端按 <c>https://devops.aliyun.com/orgs/{organizationId}</c> 回显，服务端不落地址。</remarks>
public sealed record YunxiaoSettingsDto(string OrganizationId, bool HasPat);

/// <summary>测试连接结论。</summary>
/// <param name="Ok">探活是否通过（失败走 ApiEnvelope.Fail，本载荷只在成功时出现）。</param>
/// <param name="ProjectCount">组织下可见项目数（探活顺带取 total）。</param>
/// <param name="Message">补充说明（成功为 null）。</param>
public sealed record YunxiaoTestConnectionDto(bool Ok, int ProjectCount, string? Message = null);

/// <summary>保存结果。</summary>
/// <param name="Saved">是否已保存（探活未通过不会走到这里）。</param>
public sealed record YunxiaoSaveSettingsDto(bool Saved);

/// <summary>研发项目分页列表。</summary>
/// <param name="Items">项目项。</param>
/// <param name="Total">总条数。</param>
/// <param name="Page">当前页码（默认 1，下限 1）。</param>
/// <param name="PerPage">每页条数（默认 20，上限 100）。</param>
public sealed record YunxiaoProjectListDto(
    IReadOnlyList<YunxiaoProject> Items,
    int Total,
    int Page,
    int PerPage);
