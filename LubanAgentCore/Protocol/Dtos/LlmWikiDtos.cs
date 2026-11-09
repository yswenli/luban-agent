/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCore.Protocol.Dtos
*文件名： LlmWikiDtos
*描述：LLM Wiki 接口的入参模型（响应统一复用 LubanAgentCore 的业务模型）
*
*****************************************************************************/

namespace LubanAgentCore.Protocol.Dtos;

/// <summary>创建知识库工作区。</summary>
public sealed record LlmWikiCreateRequest
{
    /// <summary>知识库根目录绝对路径。</summary>
    public string RootPath { get; init; } = "";

    /// <summary>显示名；空白时取目录名。</summary>
    public string? Name { get; init; }
}

/// <summary>仅带工作区标识的请求。</summary>
public sealed record LlmWikiWorkspaceRequest
{
    /// <summary>知识库工作区标识。</summary>
    public string WorkspaceId { get; init; } = "";
}

/// <summary>索引原始资料。</summary>
public sealed record LlmWikiIndexRequest
{
    /// <summary>知识库工作区标识。</summary>
    public string WorkspaceId { get; init; } = "";

    /// <summary>文件匹配模式；null 或空表示全部。</summary>
    public string? Glob { get; init; }
}

/// <summary>检索知识库。</summary>
public sealed record LlmWikiSearchRequest
{
    /// <summary>知识库工作区标识。</summary>
    public string WorkspaceId { get; init; } = "";

    /// <summary>检索关键词。</summary>
    public string Query { get; init; } = "";

    /// <summary>检索范围：WikiThenRaw / WikiOnly / RawOnly；null 取 WikiThenRaw。</summary>
    public string? Scope { get; init; }
}

/// <summary>提取 ingest 源文件。</summary>
public sealed record LlmWikiIngestRequest
{
    /// <summary>知识库工作区标识。</summary>
    public string WorkspaceId { get; init; } = "";

    /// <summary>工作区内文件 / glob / 目录。</summary>
    public string Target { get; init; } = "";
}