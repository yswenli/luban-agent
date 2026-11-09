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
public sealed record WorkspaceDto(
    string WorkspaceId,
    string Name,
    string RootPath,
    string Type,
    string? UserId,
    string? ConfigPath,
    DateTime? LastActiveAt,
    bool IsAuthorized);

/// <summary>创建工作区。</summary>
public sealed record WorkspaceCreateRequest
{
    /// <summary>根目录路径。</summary>
    public string RootPath { get; init; } = "";

    /// <summary>显示名；空白时取目录名。</summary>
    public string? Name { get; init; }

    /// <summary>类型：Normal / LlmWiki；null 取 Normal。</summary>
    public string? Type { get; init; }
}

/// <summary>仅带工作区标识的请求。</summary>
public sealed record WorkspaceIdRequest
{
    /// <summary>工作区标识。</summary>
    public string WorkspaceId { get; init; } = "";
}

/// <summary>重命名工作区。</summary>
public sealed record WorkspaceRenameRequest
{
    /// <summary>工作区标识。</summary>
    public string WorkspaceId { get; init; } = "";

    /// <summary>新显示名。</summary>
    public string Name { get; init; } = "";
}