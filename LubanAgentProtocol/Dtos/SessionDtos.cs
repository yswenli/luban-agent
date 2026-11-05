/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Protocol.Dtos
*文件名： SessionDtos
*描述：会话接口的传输入参（响应直接复用框架的会话模型）
*
*****************************************************************************/

namespace LubanAgent.Protocol.Dtos;

/// <summary>新建会话。</summary>
public sealed record SessionCreateRequest
{
    /// <summary>标题；null 用服务端默认标题。</summary>
    public string? Title { get; init; }

    /// <summary>绑定的工作区；null 表示不绑定。</summary>
    public string? WorkspaceId { get; init; }
}

/// <summary>重命名会话。</summary>
public sealed record SessionRenameRequest
{
    /// <summary>会话标识。</summary>
    public string SessionId { get; init; } = "";

    /// <summary>新标题。</summary>
    public string Title { get; init; } = "";
}

/// <summary>仅带会话标识的请求。</summary>
public sealed record SessionIdRequest
{
    /// <summary>会话标识。</summary>
    public string SessionId { get; init; } = "";
}

/// <summary>追加消息。</summary>
public sealed record SessionAddMessageRequest
{
    /// <summary>会话标识。</summary>
    public string SessionId { get; init; } = "";

    /// <summary>角色（user / assistant / system）。</summary>
    public string Role { get; init; } = "";

    /// <summary>正文。</summary>
    public string Content { get; init; } = "";

    /// <summary>Token 数。</summary>
    public int? Tokens { get; init; }

    /// <summary>思考内容。</summary>
    public string? Thinking { get; init; }

    /// <summary>附件元数据 JSON。</summary>
    public string? Attachments { get; init; }

    /// <summary>工具调用信息 JSON。</summary>
    public string? ToolCalls { get; init; }
}

/// <summary>标记消息为已压缩。</summary>
public sealed record SessionCompactionRequest
{
    /// <summary>会话标识。</summary>
    public string SessionId { get; init; } = "";

    /// <summary>消息标识集合。</summary>
    public IReadOnlyList<long> MessageIds { get; init; } = [];
}