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

    /// <summary>
    /// 相对子目录（可多级，允许 <c>/</c> 与 <c>\</c>）；与 <see cref="RootPath"/> 组成最终目录。
    /// </summary>
    /// <remarks>
    /// 为空时保持旧语义：目录来源直接用 <see cref="RootPath"/> 作为工作区目录；
    /// git 来源在 <see cref="RootPath"/> 下再取项目名/仓库名作为子目录。
    /// </remarks>
    public string? SubPath { get; init; }

    /// <summary>本次请求携带的 HTTPS 凭据；为空时回落到当前用户已保存的同域名凭据。</summary>
    public GitCredentialInput? GitCredential { get; init; }

    /// <summary>创建成功后是否保存本次凭据（按用户 + 域名覆盖）。</summary>
    public bool SaveCredential { get; init; }
}

/// <summary>HTTPS 用户名与密码 / 令牌。</summary>
public sealed record GitCredentialInput
{
    /// <summary>用户名。</summary>
    public string? UserName { get; init; }

    /// <summary>密码 / 访问令牌。</summary>
    public string? Secret { get; init; }
}

/// <summary>仓库地址预检请求。</summary>
public sealed record GitVerifyRequest
{
    /// <summary>仓库地址。</summary>
    public string GitUrl { get; init; } = "";

    /// <summary>本次填写的凭据；为空时用当前用户已保存的同域名凭据。</summary>
    public GitCredentialInput? GitCredential { get; init; }
}

/// <summary>仓库地址预检结果（始终 200，用 <see cref="Ok"/> 表达成败）。</summary>
/// <param name="Ok">是否可访问。</param>
/// <param name="Kind">协议：https / http / ssh / git / file。</param>
/// <param name="Host">域名（小写）。</param>
/// <param name="Port">端口。</param>
/// <param name="Reason">失败原因：git-auth / git-not-found / git-network / git-timeout / git-unknown。</param>
/// <param name="Message">失败说明。</param>
public sealed record GitVerifyResultDto(
    bool Ok,
    string Kind,
    string Host,
    int Port,
    string? Reason,
    string? Message);

/// <summary>某个仓库地址对应的已保存凭据状态（不含密码）。</summary>
/// <param name="Kind">协议。</param>
/// <param name="Host">域名。</param>
/// <param name="Port">端口。</param>
/// <param name="Saved">是否已有当前用户的凭据。</param>
/// <param name="UserName">已保存凭据的用户名。</param>
public sealed record GitCredentialStatusDto(
    string Kind,
    string Host,
    int Port,
    bool Saved,
    string? UserName);

/// <summary>凭据列表项（永不包含密码）。</summary>
/// <param name="Id">凭据主键。</param>
/// <param name="UserId">归属用户标识。</param>
/// <param name="Owner">归属用户展示名（管理员视图用）。</param>
/// <param name="Scheme">协议。</param>
/// <param name="Host">域名。</param>
/// <param name="Port">端口。</param>
/// <param name="GitUserName">凭据用户名。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record GitCredentialDto(
    long Id,
    string UserId,
    string Owner,
    string Scheme,
    string Host,
    int Port,
    string GitUserName,
    DateTime? UpdatedAt);

/// <summary>删除凭据：按主键（管理员可删任意用户）或按仓库地址（只删自己的）。</summary>
public sealed record GitCredentialDeleteRequest
{
    /// <summary>凭据主键。</summary>
    public long? Id { get; init; }

    /// <summary>仓库地址；按地址删除当前用户该域名的凭据。</summary>
    public string? GitUrl { get; init; }
}

/// <summary>单独保存一条 HTTPS 凭据（设置页用，不依赖仓库地址）。</summary>
public sealed record GitCredentialSaveRequest
{
    /// <summary>域名，可选带端口：<c>git.example.com</c> 或 <c>git.example.com:8443</c>。</summary>
    public string Host { get; init; } = "";

    /// <summary>用户名。</summary>
    public string? UserName { get; init; }

    /// <summary>密码 / 访问令牌。</summary>
    public string? Secret { get; init; }
}

/// <summary>根目录内的目录条目。</summary>
/// <param name="Name">目录名。</param>
/// <param name="Path">相对根目录的路径（使用 <c>/</c> 分隔）。</param>
/// <param name="HasGit">目录下是否存在 <c>.git</c>。</param>
public sealed record WorkspaceBrowseEntryDto(string Name, string Path, bool HasGit);

/// <summary>根目录内的只读目录浏览结果。</summary>
/// <param name="Root">根目录。</param>
/// <param name="Path">当前目录相对根目录的路径；根目录本身为空串。</param>
/// <param name="Parent">上一级相对路径；已在根目录时为 <c>null</c>。</param>
/// <param name="Entries">子目录（单层，不含文件）。</param>
public sealed record WorkspaceBrowseDto(
    string Root,
    string Path,
    string? Parent,
    List<WorkspaceBrowseEntryDto> Entries);

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