/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol.Dtos
*文件名： WorkspaceDtos
*描述：工作区接口的传输模型与入参
*
*****************************************************************************/

namespace LubanAgentCore.Protocol.Dtos;

/// <summary>工作区传输模型。</summary>
/// <param name="WorkspaceId">工作区标识。</param>
/// <param name="Name">显示名。</param>
/// <param name="RootPath">根目录绝对路径。</param>
/// <param name="Type">类型：Normal / LlmWiki。</param>
/// <param name="UserId">归属用户。</param>
/// <param name="ConfigPath">配置目录相对路径。</param>
/// <param name="LastActiveAt">最后活跃时间。</param>
/// <param name="IsAuthorized">是否已授权访问根目录。</param>
/// <param name="HasGit">工作区根目录下是否存在 <c>.git</c>（目录或 worktree/submodule 的文件形态）。</param>
public sealed record WorkspaceDto(
    string WorkspaceId,
    string Name,
    string RootPath,
    string Type,
    string? UserId,
    string? ConfigPath,
    DateTime? LastActiveAt,
    bool IsAuthorized,
    bool HasGit);

/// <summary>根目录占用查询结果（跨用户判重，只回布尔值，不暴露他人工作区细节）。</summary>
/// <param name="Used">是否已被任一用户登记为工作区。</param>
public sealed record WorkspaceRootUsedDto(bool Used);

/// <summary>创建工作区。</summary>
public sealed record WorkspaceCreateRequest
{
    /// <summary>根目录路径；来源为 <c>directory</c> 时必填。</summary>
    public string RootPath { get; init; } = "";

    /// <summary>显示名；空白时取目录名。</summary>
    public string? Name { get; init; }

    /// <summary>类型：Normal / LlmWiki；null 取 Normal。</summary>
    public string? Type { get; init; }

    /// <summary>
    /// 来源：<c>directory</c>（默认，使用 <see cref="RootPath"/>）或 <c>git</c>
    /// （按 <see cref="GitUrl"/> 克隆到 <c>~/.luban-agent/repositories/{Name}</c>）。
    /// </summary>
    public string? Source { get; init; }

    /// <summary>git 仓库地址；来源为 <c>git</c> 时必填。</summary>
    public string? GitUrl { get; init; }

    /// <summary>目标目录已存在时是否复用（缺省 false：返回冲突提示，不静默使用）。</summary>
    public bool ReuseExisting { get; init; }

    /// <summary>
    /// 目录已被其他用户登记为工作区时是否确认共用（缺省 false：返回 409 提示，不静默共用）。
    /// </summary>
    /// <remarks>共用只在物理目录层：每个用户仍持有自己的工作区记录，会话与配置互不影响。</remarks>
    public bool ShareExisting { get; init; }
}

/// <summary>仅带工作区标识的请求。</summary>
public sealed record WorkspaceIdRequest
{
    /// <summary>工作区标识。</summary>
    public string WorkspaceId { get; init; } = "";
}

/// <summary>工作区根目录白名单（机器级，所有用户共享）。</summary>
public sealed record WorkspaceRootsRequest
{
    /// <summary>允许创建工作的绝对路径列表；至少一项，空白项忽略。</summary>
    public List<string> Roots { get; init; } = [];
}

/// <summary>重命名工作区。</summary>
public sealed record WorkspaceRenameRequest
{
    /// <summary>工作区标识。</summary>
    public string WorkspaceId { get; init; } = "";

    /// <summary>新显示名。</summary>
    public string Name { get; init; } = "";
}