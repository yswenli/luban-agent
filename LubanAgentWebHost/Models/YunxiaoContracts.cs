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

/// <summary>研发项目列表项（面向页面的扁平投影，时间已格式化）。</summary>
/// <param name="Id">项目 ID。</param>
/// <param name="Name">项目名称。</param>
/// <param name="Identifier">项目标识（云效 <c>customCode</c>）。</param>
/// <param name="Description">项目描述。</param>
/// <param name="UpdateTime">更新时间（<c>yyyy-MM-dd HH:mm:ss</c>；取 <c>gmtModified</c>，为空退 <c>gmtCreate</c>）。</param>
public sealed record YunxiaoProjectItemDto(
    string? Id,
    string? Name,
    string? Identifier,
    string? Description,
    string? UpdateTime);

/// <summary>研发项目分页列表。</summary>
/// <param name="Items">项目项。</param>
/// <param name="Total">总条数。</param>
/// <param name="Page">当前页码（默认 1，下限 1）。</param>
/// <param name="PerPage">每页条数（默认 20，上限 200）。</param>
public sealed record YunxiaoProjectListDto(
    IReadOnlyList<YunxiaoProjectItemDto> Items,
    int Total,
    int Page,
    int PerPage);

// ── S2 请求 DTO（批次 D · REST 面）────────────────────────────────────────
// 预检 / 执行 / 流水线预览 / 提交的请求体直接复用服务模型
// （LubanAgentWebHost.Services.Init 的 PreflightRequest / ExecRequest /
// PipelinePreviewRequest / PipelineCommitRequest）：均为 positional record，
// AgentApiJson Web 口径（camelCase、大小写不敏感）可直接反序列化，
// 按「能复用就复用」原则不重复定义，壳层 TryReadPayload<T> 以服务模型为 T。
// 仅「重跑」在服务侧无请求模型（RetryAsync 逐参入参），故在此补 wire DTO。

/// <summary>重跑指定失败库（POST /yunxiao/initretry）。</summary>
public sealed record YunxiaoRetryRequest
{
    /// <summary>任务标识。</summary>
    public string TaskId { get; init; } = string.Empty;

    /// <summary>要重跑的库名清单（服务端只收 failed 库，非 failed 不动）。</summary>
    public IReadOnlyList<string> RepoNames { get; init; } = [];
}
