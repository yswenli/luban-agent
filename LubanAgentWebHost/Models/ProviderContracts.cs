/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Models
*文件名： ProviderContracts
*描述：供应商与模型的请求 / 响应模型
*
*****************************************************************************/

namespace LubanAgentWebHost.Models;

/// <summary>切换全局模型选择。</summary>
public sealed record ModelSelectRequest
{
    /// <summary>供应商标识。</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>模型标识。</summary>
    public string ModelId { get; init; } = string.Empty;
}

/// <summary>供应商设置项。</summary>
/// <param name="Name">标识（小写）。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="BaseUrl">基址。</param>
/// <param name="ApiKeyMask">API Key 掩码（不回显明文）。</param>
/// <param name="NetworkTimeoutSeconds">网络超时（秒）。</param>
/// <param name="CustomModels">自定义模型。</param>
/// <param name="IsSelected">是否包含当前全局选择。</param>
public sealed record ProviderListItemDto(
    string Name,
    string DisplayName,
    string? BaseUrl,
    string ApiKeyMask,
    int? NetworkTimeoutSeconds,
    IReadOnlyList<string> CustomModels,
    bool IsSelected);

/// <summary>内置供应商预设项（标识与默认基址由框架预设表提供）。</summary>
/// <param name="Name">标识（小写）。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="BaseUrl">预设基址；「自定义」为空，Azure / Ollama 为占位地址，Claude / Gemini 无预设地址。</param>
/// <param name="EditableBaseUrl">基址是否仍需用户填写（自定义端点项，以及无预设地址的项）。</param>
/// <param name="IsCustom">是否为「自定义」项；true 时标识与基址均可编辑。</param>
/// <param name="Warning">添加时的风险提示（如 Claude 需经 OpenAI 兼容代理），无则为 null。</param>
public sealed record ProviderPresetDto(
    string Name,
    string DisplayName,
    string BaseUrl,
    bool EditableBaseUrl,
    bool IsCustom,
    string? Warning);

/// <summary>保存供应商。<see cref="ApiKey"/> 留空表示不修改已有密钥。</summary>
public sealed record ProviderSaveRequest
{
    /// <summary>标识（小写）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>原标识；与 <see cref="Name"/> 不同时表示重命名，为空时按标识是否已存在判定新建 / 更新。</summary>
    public string? OriginalName { get; init; }

    /// <summary>API Key 明文；留空表示不修改。</summary>
    public string? ApiKey { get; init; }

    /// <summary>基址。</summary>
    public string? BaseUrl { get; init; }

    /// <summary>显示名。</summary>
    public string? DisplayName { get; init; }

    /// <summary>网络超时（秒）；小于等于 0 表示使用默认。</summary>
    public int? NetworkTimeoutSeconds { get; init; }

    /// <summary>自定义模型清单（按表单当前值做增量更新）。</summary>
    public IReadOnlyList<string>? CustomModels { get; init; }
}

/// <summary>供应商设置项删除请求。</summary>
public sealed record ProviderDeleteRequest
{
    /// <summary>供应商标识。</summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>单个模型条目。</summary>
/// <param name="ModelId">模型标识。</param>
/// <param name="IsCustom">是否为用户自定义模型（可改名 / 删除）。</param>
/// <param name="IsSelected">是否为全局当前模型。</param>
public sealed record ModelItemDto(string ModelId, bool IsCustom, bool IsSelected);

/// <summary>按供应商分组的模型清单。</summary>
/// <param name="ProviderId">供应商标识。</param>
/// <param name="DisplayName">供应商显示名。</param>
/// <param name="Models">模型条目。</param>
public sealed record ProviderModelsDto(
    string ProviderId,
    string DisplayName,
    IReadOnlyList<ModelItemDto> Models);

/// <summary>按供应商定位模型。</summary>
public sealed record ModelTargetRequest
{
    /// <summary>供应商标识。</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>模型标识。</summary>
    public string ModelId { get; init; } = string.Empty;
}

/// <summary>新增模型。</summary>
public sealed record ModelAddRequest
{
    /// <summary>供应商标识。</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>模型标识。</summary>
    public string ModelId { get; init; } = string.Empty;
}

/// <summary>重命名模型。</summary>
public sealed record ModelRenameRequest
{
    /// <summary>供应商标识。</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>原模型标识。</summary>
    public string OldModelId { get; init; } = string.Empty;

    /// <summary>新模型标识。</summary>
    public string NewModelId { get; init; } = string.Empty;
}