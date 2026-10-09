/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentA2A.InProcess
*文件名： InMemoryA2AAgentRegistry
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/8
*描述：内存 A2A Agent 注册表实现，进程内传输按名称路由的默认来源
*
*****************************************************************************/
using System.Collections.Concurrent;

using LuBan.AIAgent.A2A.Abstractions;
using LuBan.AIAgent.A2A.Models;

namespace LubanAgentA2A.InProcess;

/// <summary>
/// 内存 A2A Agent 注册表。线程安全，同名覆盖。
/// 由 <c>AddLubanAgentA2A()</c> 注册为单例，供 <see cref="InProcessA2ATransport"/> 路由。
/// </summary>
public sealed class InMemoryA2AAgentRegistry : IA2AAgentRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Register(A2AAgentDescriptor descriptor, IA2AHandler handler)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);

        _entries[descriptor.Name] = new Entry(descriptor, handler);
    }

    /// <inheritdoc />
    public bool TryResolve(string name, out A2AAgentDescriptor descriptor, out IA2AHandler handler)
    {
        if (!string.IsNullOrEmpty(name) && _entries.TryGetValue(name, out var entry))
        {
            descriptor = entry.Descriptor;
            handler = entry.Handler;
            return true;
        }

        descriptor = null!;
        handler = null!;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<A2AAgentDescriptor> GetAll()
        => _entries.Values.Select(e => e.Descriptor).ToList();

    private sealed record Entry(A2AAgentDescriptor Descriptor, IA2AHandler Handler);
}