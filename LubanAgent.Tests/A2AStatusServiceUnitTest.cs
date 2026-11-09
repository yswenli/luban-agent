/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： A2AStatusServiceUnitTest
*描述：A2A 只读状态实现的状态映射单测（启用/运行/端点/工作区）
*
*****************************************************************************/

using LuBan.AIAgent.A2A;
using LuBan.AIAgent.A2A.Abstractions;
using LuBan.AIAgent.A2A.Models;

using LubanAgentCore.A2A.Runtime;

using Microsoft.Extensions.Options;

namespace LubanAgent.Tests;

[TestClass]
public class A2AStatusServiceUnitTest
{
    [TestMethod]
    public async Task 运行中状态按运行时对象映射()
    {
        var state = new A2ARuntimeState();
        state.SetLocalAgentName("agent-x");
        state.SetWorkspaceRoot(Path.Combine("C:", "ws"));
        var service = CreateService(
            new FakeLifecycle(isRunning: true),
            state,
            new FakeServerHost(endpoint: "http://127.0.0.1:9000"),
            enabled: true);

        var status = await service.GetStatusAsync();

        Assert.IsTrue(status.Enabled);
        Assert.IsTrue(status.IsRunning);
        Assert.AreEqual("agent-x", status.LocalAgentName);
        Assert.AreEqual("http://127.0.0.1:9000", status.Endpoint);
        Assert.AreEqual(Path.Combine("C:", "ws"), status.WorkspaceRoot);
    }

    [TestMethod]
    public async Task 未启动时端点与工作区为空()
    {
        var state = new A2ARuntimeState();
        var service = CreateService(
            new FakeLifecycle(isRunning: false),
            state,
            new FakeServerHost(endpoint: null),
            enabled: false);

        var status = await service.GetStatusAsync();

        Assert.IsFalse(status.Enabled);
        Assert.IsFalse(status.IsRunning);
        Assert.AreEqual("luban-agent", status.LocalAgentName);
        Assert.IsNull(status.Endpoint);
        Assert.IsNull(status.WorkspaceRoot);
    }

    [TestMethod]
    public void 状态模型可JSON往返()
    {
        var original = new LubanAgentCore.Protocol.Dtos.A2AStatusDto(
            true, true, "agent-x", "http://127.0.0.1:9000", @"C:\ws");

        var json = LubanAgentCore.Protocol.AgentApiJson.Serialize(original);
        var restored = LubanAgentCore.Protocol.AgentApiJson.Deserialize<LubanAgentCore.Protocol.Dtos.A2AStatusDto>(json);

        Assert.AreEqual(original, restored);
    }

    private static A2AStatusService CreateService(
        IA2ALifecycle lifecycle,
        A2ARuntimeState state,
        IA2AServerHost serverHost,
        bool enabled)
        => new(lifecycle, state, serverHost, Options.Create(new A2AOptions { Enabled = enabled }));

    private sealed class FakeLifecycle(bool isRunning) : IA2ALifecycle
    {
        public bool IsRunning { get; } = isRunning;

        public Task StartAsync(string? workspaceRoot, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeServerHost(string? endpoint) : IA2AServerHost
    {
        public string? Endpoint { get; } = endpoint;

        public bool IsRunning => Endpoint is not null;

        public IReadOnlyCollection<A2AAgentDescriptor> GetDescriptors() => Array.Empty<A2AAgentDescriptor>();

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}