/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Models
*文件名： AgentsMdContracts
*描述：工作区 AGENTS.md 初始化接口的入参与响应模型
*
*****************************************************************************/

namespace LubanAgentWebHost.Models;

/// <summary>AGENTS.md 现状查询请求。</summary>
public sealed record AgentsMdInfoRequest
{
    /// <summary>目标工作区标识。</summary>
    public string WorkspaceId { get; init; } = "";
}

/// <summary>AGENTS.md 现状。</summary>
/// <param name="WorkspaceId">目标工作区标识。</param>
/// <param name="WorkspaceName">工作区显示名。</param>
/// <param name="RootPath">工作区根目录绝对路径。</param>
/// <param name="FileName">目标文件名。</param>
/// <param name="TargetPath">目标文件绝对路径。</param>
/// <param name="Exists">目标文件是否已存在。</param>
/// <param name="ExistingLength">已存在时的字符数；不存在为 0。</param>
public sealed record AgentsMdInfoDto(
    string WorkspaceId,
    string WorkspaceName,
    string RootPath,
    string FileName,
    string TargetPath,
    bool Exists,
    int ExistingLength);

/// <summary>AGENTS.md 生成请求。</summary>
public sealed record AgentsMdGenerateRequest
{
    /// <summary>目标工作区标识。</summary>
    public string WorkspaceId { get; init; } = "";

    /// <summary>Agent 名称（必填）。</summary>
    public string? AgentName { get; init; }

    /// <summary>业务场景（必填）。</summary>
    public string? Scene { get; init; }

    /// <summary>能力清单（选填）。</summary>
    public IReadOnlyList<string>? Capabilities { get; init; }

    /// <summary>工具 / API 清单（选填）。</summary>
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>输出模式：full / lite；null 取 full。</summary>
    public string? OutputMode { get; init; }

    /// <summary>自由描述（选填）。</summary>
    public string? Description { get; init; }

    /// <summary>目标文件已存在时是否覆盖。</summary>
    public bool Overwrite { get; init; }
}

/// <summary>AGENTS.md 手动保存请求。</summary>
public sealed record AgentsMdSaveRequest
{
    /// <summary>目标工作区标识。</summary>
    public string WorkspaceId { get; init; } = "";

    /// <summary>待落盘的 Markdown 正文。</summary>
    public string Markdown { get; init; } = "";

    /// <summary>目标文件已存在时是否覆盖。</summary>
    public bool Overwrite { get; init; }
}

/// <summary>AGENTS.md 落盘结果。</summary>
/// <param name="TargetPath">目标文件绝对路径。</param>
/// <param name="Markdown">规范化后实际写入的 Markdown。</param>
/// <param name="VerificationPrompt">用于校验文件完整性的提示词。</param>
/// <param name="SessionId">本次生成使用的临时会话；手动保存时为 null。</param>
public sealed record AgentsMdResultDto(
    string TargetPath,
    string Markdown,
    string VerificationPrompt,
    string? SessionId);