/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Mapping
*文件名： A2AMapping
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：框架 A2A 原生 DTO 与官方 A2A SDK 类型双向映射
*
*****************************************************************************/
using System.Text.Json;

using LuBan.AIAgent.A2A.Models;

using Sdk = A2A;

namespace LubanAgentA2A.Mapping;

/// <summary>
/// 框架 <c>LuBan.AIAgent.A2A.Models</c> 原生 DTO 与官方 <c>A2A</c> SDK 类型之间的双向映射。
/// <para>约定：</para>
/// <list type="bullet">
/// <item><c>Part</c> 采用字段存在式联合，框架侧按 <c>Text</c> &gt; <c>Raw</c> &gt; <c>Url</c> &gt; <c>Data</c> 优先级构造。</item>
/// <item>SDK <c>Part.Raw</c> 为 <c>byte[]</c>，框架侧以 base64 字符串表示。</item>
/// <item>SDK <c>Part.Data</c> 为 <c>JsonElement?</c>，框架侧以 JSON 原始文本表示。</item>
/// <item>SDK 元数据为 <c>Dictionary&lt;string, JsonElement&gt;</c>，框架侧为 <c>Dictionary&lt;string, string&gt;</c>。</item>
/// <item>SDK <c>Message.Extensions</c> 为 <c>List&lt;string&gt;</c>（URI 列表），框架侧为 <c>Dictionary&lt;string, string&gt;</c>，
/// 编码为 <c>"key=value"</c>（value 为空时仅 <c>"key"</c>）。</item>
/// <item><c>TaskState</c> 两枚举数值一致，直接按序转换；<c>Role</c> 数值不一致，显式映射。</item>
/// </list>
/// </summary>
public static class A2AMapping
{
    /// <summary>框架角色 → SDK 角色。</summary>
    public static Sdk.Role ToSdk(this A2ARole role) => role switch
    {
        A2ARole.Agent => Sdk.Role.Agent,
        _ => Sdk.Role.User,
    };

    /// <summary>SDK 角色 → 框架角色。</summary>
    public static A2ARole ToFramework(this Sdk.Role role) => role switch
    {
        Sdk.Role.Agent => A2ARole.Agent,
        _ => A2ARole.User,
    };

    /// <summary>框架任务状态 → SDK 任务状态（数值同序）。</summary>
    public static Sdk.TaskState ToSdk(this A2ATaskState state) => (Sdk.TaskState)(int)state;

    /// <summary>SDK 任务状态 → 框架任务状态（数值同序）。</summary>
    public static A2ATaskState ToFramework(this Sdk.TaskState state) => (A2ATaskState)(int)state;

    /// <summary>框架内容部件 → SDK 内容部件。</summary>
    public static Sdk.Part ToSdk(this A2APart part)
    {
        ArgumentNullException.ThrowIfNull(part);

        var sdk = new Sdk.Part
        {
            Filename = part.Filename,
            MediaType = part.MediaType,
            Metadata = ToSdkMeta(part.Metadata),
        };

        if (part.Text is not null)
        {
            sdk.Text = part.Text;
        }
        else if (part.Raw is not null)
        {
            sdk.Raw = Convert.FromBase64String(part.Raw);
        }
        else if (part.Url is not null)
        {
            sdk.Url = part.Url;
        }
        else if (part.Data is not null)
        {
            using var doc = JsonDocument.Parse(part.Data);
            sdk.Data = doc.RootElement.Clone();
        }

        return sdk;
    }

    /// <summary>SDK 内容部件 → 框架内容部件。</summary>
    public static A2APart ToFramework(this Sdk.Part part)
    {
        ArgumentNullException.ThrowIfNull(part);

        var fw = new A2APart
        {
            Filename = part.Filename,
            MediaType = part.MediaType,
            Metadata = ToFrameworkMeta(part.Metadata),
        };

        switch (part.ContentCase)
        {
            case Sdk.PartContentCase.Text:
                fw.Text = part.Text;
                break;
            case Sdk.PartContentCase.Raw:
                fw.Raw = part.Raw is not null ? Convert.ToBase64String(part.Raw) : null;
                break;
            case Sdk.PartContentCase.Url:
                fw.Url = part.Url;
                break;
            case Sdk.PartContentCase.Data:
                fw.Data = part.Data?.GetRawText();
                break;
        }

        return fw;
    }

    /// <summary>框架消息 → SDK 消息。</summary>
    public static Sdk.Message ToSdk(this A2AMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new Sdk.Message
        {
            Role = message.Role.ToSdk(),
            Parts = message.Parts?.Select(p => p.ToSdk()).ToList() ?? new List<Sdk.Part>(),
            MessageId = message.MessageId!,
            ContextId = message.ContextId,
            TaskId = message.TaskId,
            ReferenceTaskIds = message.ReferenceTaskIds?.ToList(),
            Extensions = ToSdkExtensions(message.Extensions),
            Metadata = ToSdkMeta(message.Metadata),
        };
    }

    /// <summary>SDK 消息 → 框架消息。</summary>
    public static A2AMessage ToFramework(this Sdk.Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new A2AMessage
        {
            Role = message.Role.ToFramework(),
            Parts = message.Parts?.Select(p => p.ToFramework()).ToList() ?? new List<A2APart>(),
            MessageId = message.MessageId,
            ContextId = message.ContextId,
            TaskId = message.TaskId,
            ReferenceTaskIds = message.ReferenceTaskIds?.ToList(),
            Extensions = ToFrameworkExtensions(message.Extensions),
            Metadata = ToFrameworkMeta(message.Metadata),
        };
    }

    /// <summary>框架任务状态 → SDK 任务状态。</summary>
    public static Sdk.TaskStatus ToSdk(this A2ATaskStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new Sdk.TaskStatus
        {
            State = status.State.ToSdk(),
            Message = status.Message?.ToSdk(),
            Timestamp = status.Timestamp,
        };
    }

    /// <summary>SDK 任务状态 → 框架任务状态。</summary>
    public static A2ATaskStatus ToFramework(this Sdk.TaskStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new A2ATaskStatus
        {
            State = status.State.ToFramework(),
            Message = status.Message?.ToFramework(),
            Timestamp = status.Timestamp,
        };
    }

    /// <summary>框架产出物 → SDK 产出物（不含 Append/LastChunk，其属流式事件）。</summary>
    public static Sdk.Artifact ToSdk(this A2AArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return new Sdk.Artifact
        {
            ArtifactId = artifact.ArtifactId,
            Name = artifact.Name,
            Description = artifact.Description,
            Parts = artifact.Parts?.Select(p => p.ToSdk()).ToList() ?? new List<Sdk.Part>(),
            Metadata = ToSdkMeta(artifact.Metadata),
        };
    }

    /// <summary>SDK 产出物 → 框架产出物。</summary>
    public static A2AArtifact ToFramework(this Sdk.Artifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        return new A2AArtifact
        {
            ArtifactId = artifact.ArtifactId ?? string.Empty,
            Name = artifact.Name,
            Description = artifact.Description,
            Parts = artifact.Parts?.Select(p => p.ToFramework()).ToList() ?? new List<A2APart>(),
            Metadata = ToFrameworkMeta(artifact.Metadata),
        };
    }

    /// <summary>框架任务 → SDK 任务。</summary>
    public static Sdk.AgentTask ToSdk(this A2ATask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new Sdk.AgentTask
        {
            Id = task.Id!,
            ContextId = task.ContextId!,
            Status = (task.Status ?? new A2ATaskStatus()).ToSdk(),
            History = task.History?.Select(m => m.ToSdk()).ToList(),
            Artifacts = task.Artifacts?.Select(a => a.ToSdk()).ToList(),
            Metadata = ToSdkMeta(task.Metadata),
        };
    }

    /// <summary>SDK 任务 → 框架任务。</summary>
    public static A2ATask ToFramework(this Sdk.AgentTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new A2ATask
        {
            Id = task.Id ?? string.Empty,
            ContextId = task.ContextId,
            Status = task.Status?.ToFramework() ?? new A2ATaskStatus(),
            History = task.History?.Select(m => m.ToFramework()).ToList(),
            Artifacts = task.Artifacts?.Select(a => a.ToFramework()).ToList(),
            Metadata = ToFrameworkMeta(task.Metadata),
        };
    }

    /// <summary>框架流式事件 → SDK 流式响应。</summary>
    public static Sdk.StreamResponse ToSdk(this A2AStreamEvent streamEvent)
    {
        ArgumentNullException.ThrowIfNull(streamEvent);

        switch (streamEvent.Kind)
        {
            case A2AStreamEventKind.Status:
                return new Sdk.StreamResponse
                {
                    StatusUpdate = new Sdk.TaskStatusUpdateEvent
                    {
                        TaskId = streamEvent.TaskId,
                        ContextId = streamEvent.ContextId!,
                        Status = (streamEvent.Status ?? new A2ATaskStatus()).ToSdk(),
                        Metadata = ToSdkMeta(streamEvent.Metadata),
                    },
                };

            case A2AStreamEventKind.Artifact:
                var artifact = streamEvent.Artifact ?? new A2AArtifact();
                return new Sdk.StreamResponse
                {
                    ArtifactUpdate = new Sdk.TaskArtifactUpdateEvent
                    {
                        TaskId = streamEvent.TaskId,
                        ContextId = streamEvent.ContextId!,
                        Artifact = artifact.ToSdk(),
                        Append = artifact.Append,
                        LastChunk = artifact.LastChunk,
                        Metadata = ToSdkMeta(streamEvent.Metadata),
                    },
                };

            default:
                throw new ArgumentOutOfRangeException(nameof(streamEvent), streamEvent.Kind, "未知的 A2A 流式事件种类。");
        }
    }

    /// <summary>SDK 流式响应 → 框架流式事件。<c>Task</c>/<c>Message</c> 载荷降级为状态事件。</summary>
    public static A2AStreamEvent ToFramework(this Sdk.StreamResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        switch (response.PayloadCase)
        {
            case Sdk.StreamResponseCase.StatusUpdate:
                var statusUpdate = response.StatusUpdate!;
                return new A2AStreamEvent
                {
                    Kind = A2AStreamEventKind.Status,
                    TaskId = statusUpdate.TaskId ?? string.Empty,
                    ContextId = statusUpdate.ContextId,
                    Status = statusUpdate.Status?.ToFramework() ?? new A2ATaskStatus(),
                    Metadata = ToFrameworkMeta(statusUpdate.Metadata),
                };

            case Sdk.StreamResponseCase.ArtifactUpdate:
                var artifactUpdate = response.ArtifactUpdate!;
                var artifact = artifactUpdate.Artifact?.ToFramework() ?? new A2AArtifact();
                artifact.Append = artifactUpdate.Append;
                artifact.LastChunk = artifactUpdate.LastChunk;
                return new A2AStreamEvent
                {
                    Kind = A2AStreamEventKind.Artifact,
                    TaskId = artifactUpdate.TaskId ?? string.Empty,
                    ContextId = artifactUpdate.ContextId,
                    Artifact = artifact,
                    Metadata = ToFrameworkMeta(artifactUpdate.Metadata),
                };

            case Sdk.StreamResponseCase.Task:
                var task = response.Task!;
                return new A2AStreamEvent
                {
                    Kind = A2AStreamEventKind.Status,
                    TaskId = task.Id ?? string.Empty,
                    ContextId = task.ContextId,
                    Status = task.Status?.ToFramework() ?? new A2ATaskStatus(),
                    Metadata = ToFrameworkMeta(task.Metadata),
                };

            case Sdk.StreamResponseCase.Message:
                var message = response.Message;
                return new A2AStreamEvent
                {
                    Kind = A2AStreamEventKind.Status,
                    TaskId = message?.TaskId ?? string.Empty,
                    ContextId = message?.ContextId,
                    Status = new A2ATaskStatus
                    {
                        State = A2ATaskState.Unspecified,
                        Message = message?.ToFramework(),
                    },
                };

            default:
                return new A2AStreamEvent
                {
                    Kind = A2AStreamEventKind.Status,
                    TaskId = string.Empty,
                    Status = new A2ATaskStatus(),
                };
        }
    }

    /// <summary>框架 Agent 描述符 → SDK AgentCard。能力标志按本地能力合成（流式=true、推送=false）。</summary>
    public static Sdk.AgentCard ToSdk(this A2AAgentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new Sdk.AgentCard
        {
            Name = descriptor.Name,
            Description = descriptor.Description ?? string.Empty,
            Version = descriptor.Version,
            SupportedInterfaces = descriptor.Interfaces?.Select(i => new Sdk.AgentInterface
            {
                Url = i.Url,
                ProtocolBinding = i.ProtocolBinding!,
                ProtocolVersion = i.ProtocolVersion!,
                Tenant = i.Tenant,
            }).ToList() ?? new List<Sdk.AgentInterface>(),
            Skills = descriptor.Skills?.Select(s => new Sdk.AgentSkill
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description ?? string.Empty,
                Tags = s.Tags?.ToList()!,
                Examples = s.Examples?.ToList(),
            }).ToList() ?? new List<Sdk.AgentSkill>(),
            DefaultInputModes = descriptor.DefaultInputModes?.ToList() ?? new List<string> { "text/plain" },
            DefaultOutputModes = descriptor.DefaultOutputModes?.ToList() ?? new List<string> { "text/plain" },
            Capabilities = new Sdk.AgentCapabilities
            {
                Streaming = true,
                PushNotifications = false,
                ExtendedAgentCard = descriptor.SupportsExtendedCard,
                Extensions = descriptor.Extensions?.Select(uri => new Sdk.AgentExtension { Uri = uri }).ToList(),
            },
            SecuritySchemes = ToSdkSecuritySchemes(descriptor.SecuritySchemes),
            SecurityRequirements = ToSdkSecurityRequirements(descriptor.SecurityRequirements),
        };
    }

    /// <summary>SDK AgentCard → 框架 Agent 描述符。</summary>
    public static A2AAgentDescriptor ToFramework(this Sdk.AgentCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new A2AAgentDescriptor
        {
            Name = card.Name ?? string.Empty,
            Description = card.Description,
            Version = card.Version ?? string.Empty,
            Interfaces = card.SupportedInterfaces?.Select(i => new A2AInterface
            {
                Url = i.Url ?? string.Empty,
                ProtocolBinding = i.ProtocolBinding,
                ProtocolVersion = i.ProtocolVersion,
                Tenant = i.Tenant,
            }).ToList(),
            Skills = card.Skills?.Select(s => new A2ASkillDescriptor
            {
                Id = s.Id ?? string.Empty,
                Name = s.Name ?? string.Empty,
                Description = s.Description,
                Tags = s.Tags?.ToList(),
                Examples = s.Examples?.ToList(),
            }).ToList(),
            DefaultInputModes = card.DefaultInputModes?.ToList(),
            DefaultOutputModes = card.DefaultOutputModes?.ToList(),
            SecuritySchemes = ToFrameworkSecuritySchemes(card.SecuritySchemes),
            SecurityRequirements = card.SecurityRequirements?.Select(r =>
                r.Schemes?.ToDictionary(kv => kv.Key, kv => kv.Value?.List?.ToList() ?? new List<string>())
                ?? new Dictionary<string, List<string>>()).ToList(),
            SupportsExtendedCard = card.Capabilities?.ExtendedAgentCard == true,
            Extensions = card.Capabilities?.Extensions?.Select(e => e.Uri ?? string.Empty).ToList(),
        };
    }

    private static Dictionary<string, JsonElement>? ToSdkMeta(Dictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var result = new Dictionary<string, JsonElement>(metadata.Count);
        foreach (var kv in metadata)
        {
            result[kv.Key] = JsonSerializer.SerializeToElement(kv.Value);
        }

        return result;
    }

    private static Dictionary<string, string>? ToFrameworkMeta(Dictionary<string, JsonElement>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var result = new Dictionary<string, string>(metadata.Count);
        foreach (var kv in metadata)
        {
            result[kv.Key] = kv.Value.ValueKind == JsonValueKind.String
                ? kv.Value.GetString() ?? string.Empty
                : kv.Value.GetRawText();
        }

        return result;
    }

    private static List<string>? ToSdkExtensions(Dictionary<string, string>? extensions)
    {
        if (extensions is null)
        {
            return null;
        }

        return extensions
            .Select(kv => string.IsNullOrEmpty(kv.Value) ? kv.Key : $"{kv.Key}={kv.Value}")
            .ToList();
    }

    private static Dictionary<string, string>? ToFrameworkExtensions(List<string>? extensions)
    {
        if (extensions is null)
        {
            return null;
        }

        var result = new Dictionary<string, string>(extensions.Count);
        foreach (var item in extensions)
        {
            if (string.IsNullOrEmpty(item))
            {
                continue;
            }

            var idx = item.IndexOf('=');
            if (idx < 0)
            {
                result[item] = string.Empty;
            }
            else
            {
                result[item[..idx]] = item[(idx + 1)..];
            }
        }

        return result;
    }

    private static Dictionary<string, Sdk.SecurityScheme>? ToSdkSecuritySchemes(Dictionary<string, A2ASecurityScheme>? schemes)
    {
        if (schemes is null)
        {
            return null;
        }

        var result = new Dictionary<string, Sdk.SecurityScheme>(schemes.Count);
        foreach (var kv in schemes)
        {
            result[kv.Key] = new Sdk.SecurityScheme
            {
                ApiKeySecurityScheme = new Sdk.ApiKeySecurityScheme
                {
                    Name = kv.Value.Name ?? string.Empty,
                    Location = kv.Value.In ?? "header",
                },
            };
        }

        return result;
    }

    private static List<Sdk.SecurityRequirement>? ToSdkSecurityRequirements(List<Dictionary<string, List<string>>>? requirements)
    {
        if (requirements is null)
        {
            return null;
        }

        return requirements.Select(req => new Sdk.SecurityRequirement
        {
            Schemes = req.ToDictionary(kv => kv.Key, kv => new Sdk.StringList { List = kv.Value.ToList() }),
        }).ToList();
    }

    private static Dictionary<string, A2ASecurityScheme>? ToFrameworkSecuritySchemes(Dictionary<string, Sdk.SecurityScheme>? schemes)
    {
        if (schemes is null)
        {
            return null;
        }

        var result = new Dictionary<string, A2ASecurityScheme>(schemes.Count);
        foreach (var kv in schemes)
        {
            if (kv.Value.SchemeCase != Sdk.SecuritySchemeCase.ApiKey)
            {
                continue;
            }

            result[kv.Key] = new A2ASecurityScheme
            {
                Type = "apiKey",
                Name = kv.Value.ApiKeySecurityScheme?.Name,
                In = kv.Value.ApiKeySecurityScheme?.Location,
            };
        }

        return result;
    }
}