/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.Client
*文件名： A2AClientAdapter
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：A2A 远程客户端适配器，基于 SDK A2AClient 封装框架 IA2AClient
*
*****************************************************************************/
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

using LuBan.AIAgent.A2A.Abstractions;
using LuBan.AIAgent.A2A.Models;

using LubanAgentA2A.Mapping;

using Microsoft.Extensions.Logging;

using Sdk = A2A;

namespace LubanAgentA2A.Client;

/// <summary>
/// A2A 远程客户端适配器。基于 SDK <see cref="Sdk.A2AClient"/>，
/// 把框架 A2A 消息/描述符与 SDK 类型互转，供 <c>RemoteSubAgentExecutor</c> 调用远端 Agent。
/// </summary>
public sealed class A2AClientAdapter : IA2AClient
{
    private const string AgentCardPath = "/.well-known/agent-card.json";

    private readonly string _name;
    private readonly string _endpoint;
    private readonly string? _remoteAgentName;
    private readonly Sdk.A2AClient _client;
    private readonly Sdk.A2ACardResolver _cardResolver;

    /// <summary>
    /// 创建远程客户端适配器实例。
    /// </summary>
    /// <param name="name">客户端逻辑名称（注册为本地 SubAgent 角色）。</param>
    /// <param name="endpoint">远端端点基址。</param>
    /// <param name="httpClient">用于访问远端的 HttpClient（调用方负责其生命周期及鉴权头）。</param>
    /// <param name="remoteAgentName">远端 Agent 名称（可选，用于覆盖描述符名称）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public A2AClientAdapter(
        string name,
        string endpoint,
        HttpClient httpClient,
        string? remoteAgentName = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentNullException.ThrowIfNull(httpClient);

        _name = name;
        _endpoint = endpoint;
        _remoteAgentName = remoteAgentName;

        var baseUri = new Uri(endpoint, UriKind.Absolute);
        _client = new Sdk.A2AClient(baseUri, httpClient);
        _cardResolver = new Sdk.A2ACardResolver(baseUri, httpClient, AgentCardPath, logger);
    }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public string Endpoint => _endpoint;

    /// <inheritdoc />
    public async Task<A2AAgentDescriptor?> ResolveCardAsync(CancellationToken ct = default)
    {
        try
        {
            var card = await _cardResolver.GetAgentCardAsync(ct).ConfigureAwait(false);
            var descriptor = card.ToFramework();
            if (!string.IsNullOrEmpty(_remoteAgentName))
            {
                descriptor.Name = _remoteAgentName!;
            }
            return descriptor;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<A2ATask> SendAsync(A2AMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var request = new Sdk.SendMessageRequest { Message = message.ToSdk() };
        var response = await _client.SendMessageAsync(request, ct).ConfigureAwait(false);
        if (response.PayloadCase == Sdk.SendMessageResponseCase.Task && response.Task is not null)
        {
            return response.Task.ToFramework();
        }

        return new A2ATask
        {
            Status = new A2ATaskStatus
            {
                State = A2ATaskState.Unspecified,
                Message = response.Message?.ToFramework(),
            },
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<A2AStreamEvent> SendStreamingAsync(
        A2AMessage message, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var request = new Sdk.SendMessageRequest { Message = message.ToSdk() };

        await using var enumerator = _client.SendStreamingMessageAsync(request, ct).GetAsyncEnumerator(ct);
        while (true)
        {
            Sdk.StreamResponse? current = null;
            Exception? error = null;
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }
                current = enumerator.Current;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                error = ex;
            }

            if (error is not null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }

            yield return current!.ToFramework();
        }
    }

    /// <inheritdoc />
    public async Task<A2ATask?> GetTaskAsync(string taskId, CancellationToken ct = default)
    {
        try
        {
            var task = await _client.GetTaskAsync(new Sdk.GetTaskRequest { Id = taskId }, ct).ConfigureAwait(false);
            return task.ToFramework();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<A2ATask?> CancelAsync(string taskId, CancellationToken ct = default)
    {
        try
        {
            var task = await _client.CancelTaskAsync(new Sdk.CancelTaskRequest { Id = taskId }, ct).ConfigureAwait(false);
            return task.ToFramework();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}