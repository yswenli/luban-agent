/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： AgentProtocolUnitTest
*描述：WebHost 与客户端之间的传输协议单测（帧编解码、事件还原、请求体包装、路由）
*
*****************************************************************************/

using System.Reflection;

using LuBan.AIAgent.Hosting;

using LubanAgentCore.Client;
using LubanAgentCore.Protocol;
using LubanAgentCore.Protocol.Dtos;
using LubanAgentCore.Protocol.Events;

namespace LubanAgent.Tests;

[TestClass]
public class AgentProtocolUnitTest
{
    [TestMethod]
    public void 帧文本往返_保留事件名与载荷()
    {
        var frame = AgentSseFrame.Create(AgentSseEventNames.TextDelta, new { delta = "你好", llmRound = 2 });
        var text = frame.ToText();

        Assert.IsTrue(text.StartsWith("event: text.delta\ndata: ", StringComparison.Ordinal));
        Assert.IsTrue(text.EndsWith("\n\n", StringComparison.Ordinal));

        Assert.IsTrue(AgentSseFrame.TryParse(text, out var parsed));
        Assert.AreEqual(AgentSseEventNames.TextDelta, parsed!.Name);
        Assert.AreEqual(frame.Data, parsed.Data);
    }

    [TestMethod]
    public void 心跳块不产生帧()
    {
        Assert.IsFalse(AgentSseFrame.TryParse(": ping\n", out var frame));
        Assert.IsNull(frame);
    }

    [TestMethod]
    public void 正文增量帧还原为宿主事件()
    {
        var frame = AgentSseFrame.Create(AgentSseEventNames.TextDelta, new { delta = "abc", llmRound = 3 });

        Assert.IsTrue(AgentEventTranslator.TryTranslate(frame, out var evt));
        var text = evt as TextDeltaEvent;
        Assert.IsNotNull(text);
        Assert.AreEqual("abc", text.Delta);
        Assert.AreEqual(3, text.LlmRound);
    }

    [TestMethod]
    public void 工具确认帧还原出确认标识与参数()
    {
        var frame = AgentSseFrame.Create(
            AgentSseEventNames.ToolConfirm,
            new
            {
                confirmationId = "cf-1",
                toolName = "RunShell",
                arguments = new { command = "dotnet build" },
                timeoutSeconds = 60
            });

        Assert.IsTrue(AgentEventTranslator.TryTranslate(frame, out var evt));
        var confirm = evt as ToolConfirmationRequestedEvent;
        Assert.IsNotNull(confirm);
        Assert.AreEqual("cf-1", confirm.ConfirmationId);
        Assert.AreEqual("RunShell", confirm.ToolName);
        Assert.AreEqual(60, confirm.TimeoutSeconds);
        Assert.AreEqual("dotnet build", confirm.Arguments["command"]);
    }

    [TestMethod]
    public void 未知事件名不产生帧且不抛异常()
    {
        var frame = new AgentSseFrame("orchestration.progress", "{\"x\":1}");

        Assert.IsFalse(AgentEventTranslator.TryTranslate(frame, out var evt));
        Assert.IsNull(evt);
    }

    [TestMethod]
    public void 计划项载荷按工具调用读取()
    {
        var frame = AgentSseFrame.Create(
            AgentSseEventNames.PlanItem,
            new { toolName = "WriteFile", arguments = new { path = "a.txt" } });

        Assert.IsTrue(AgentEventTranslator.TryReadToolInvocation(frame, out var tool, out var args));
        Assert.AreEqual("WriteFile", tool);
        Assert.AreEqual("a.txt", args["path"]);
    }

    [TestMethod]
    public void 会话与结束帧不还原为宿主事件()
    {
        var session = AgentSseFrame.ForSession("s1", created: true);
        var done = AgentSseFrame.ForDone("s1");

        Assert.IsFalse(AgentEventTranslator.TryTranslate(session, out _));
        Assert.IsFalse(AgentEventTranslator.TryTranslate(done, out _));
    }

    [TestMethod]
    public void 请求体包装成单字段payload()
    {
        var request = new AgentChatRequest { Prompt = "你好", EnableTools = true };
        var body = AgentApiJson.WrapPayload(request);

        using var document = System.Text.Json.JsonDocument.Parse(body);
        Assert.AreEqual(1, document.RootElement.EnumerateObject().Count());
        Assert.IsTrue(document.RootElement.TryGetProperty(AgentApiJson.PayloadField, out var payload));
        Assert.AreEqual(System.Text.Json.JsonValueKind.String, payload.ValueKind);

        var restored = AgentApiJson.UnwrapPayload<AgentChatRequest>(payload.GetString());
        Assert.IsNotNull(restored);
        Assert.AreEqual("你好", restored.Prompt);
        Assert.IsTrue(restored.EnableTools);
    }

    [TestMethod]
    public void 响应壳成功与失败载荷()
    {
        var ok = ApiEnvelope.Ok(new HealthDto("ok", "1.0", "yswenli", new DateTime(2026, 11, 4)));
        Assert.IsTrue(ok.Success);
        Assert.AreEqual(200, ok.Code);
        Assert.IsNull(ok.Message);
        Assert.AreEqual("yswenli", ok.GetData<HealthDto>()!.UserId);

        var fail = ApiEnvelope.Fail(404, "会话不存在");
        Assert.IsFalse(fail.Success);
        Assert.AreEqual(404, fail.Code);
        Assert.AreEqual("会话不存在", fail.Message);
        Assert.IsNull(fail.GetData<HealthDto>());
    }

    [TestMethod]
    public void 路由常量全部以斜杠开头且不含重复斜杠()
    {
        var routes = typeof(AgentApiRoutes)
            .GetNestedTypes()
            .SelectMany(t => t.GetFields())
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.IsTrue(routes.Count >= 20, $"路由数量异常：{routes.Count}");
        foreach (var route in routes)
        {
            Assert.IsTrue(route.StartsWith('/'), route);
            Assert.IsFalse(route.Contains("//"), route);
            Assert.IsFalse(route.EndsWith('/'), route);
            Assert.IsTrue(route.Count(c => c == '/') >= 2, route);
        }
    }

    [TestMethod]
    public void A2A路由包含只读状态与工作区配置读写()
    {
        var constants = typeof(AgentApiRoutes.A2A)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToList();

        Assert.AreEqual(7, constants.Count, "A2A 路由应为 1 条状态 + 6 条工作区配置读写（启停仍下沉到宿主进程）");
        foreach (var constant in constants)
        {
            Assert.IsTrue(
                ((string)constant.GetRawConstantValue()!).StartsWith("/a2a/", StringComparison.Ordinal),
                constant.Name);
        }

        Assert.AreEqual("/a2a/status", (string)typeof(AgentApiRoutes.A2A)
            .GetField(nameof(AgentApiRoutes.A2A.Status))!.GetRawConstantValue()!);
    }

    [TestMethod]
    public void 知识库路由全部落在llmwiki前缀下()
    {
        var routes = typeof(AgentApiRoutes.LlmWiki)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.AreEqual(13, routes.Count, "知识库路由应与 ILlmWikiManagementService 的 14 个方法面对齐（Clear/Delete 共用工作区入参）");
        foreach (var route in routes)
        {
            Assert.IsTrue(route.StartsWith("/llmwiki/", StringComparison.Ordinal), route);
        }
    }

    [TestMethod]
    public void 启动器优先使用显式路径()
    {
        var path = Path.Combine(Path.GetTempPath(), $"luban-webhost-{Guid.NewGuid():N}.exe");
        File.WriteAllText(path, "");
        try
        {
            var launcher = new ServerProcessLauncher(new AgentClientOptions { ServerPath = path });
            Assert.AreEqual(Path.GetFullPath(path), launcher.ResolveServerPath());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void 启动器在显式路径不存在时继续探测而不报错()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"luban-missing-{Guid.NewGuid():N}.exe");
        var launcher = new ServerProcessLauncher(new AgentClientOptions { ServerPath = missing });

        // 探测结果取决于本机是否已构建 WebHost；这里只锁定「不抛异常、不返回不存在的路径」
        var resolved = launcher.ResolveServerPath();
        Assert.IsTrue(resolved is null || File.Exists(resolved));
        Assert.AreNotEqual(Path.GetFullPath(missing), resolved);
    }

    [TestMethod]
    public void 拉起参数不再下发端口令牌超时与用户()
    {
        var args = ServerProcessLauncher.BuildArguments(
            new AgentClientOptions { Debug = true });

        foreach (var flag in new[] { "--port", "--token", "--timeout", "--user" })
        {
            Assert.IsFalse(args.Contains(flag), $"端口/令牌等以宿主配置为准，不应下发 {flag}");
        }
    }

    [TestMethod]
    public void 启动器插件目录存在时下发plugins_dir参数()
    {
        var root = Path.Combine(Path.GetTempPath(), $"luban-plugins-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var args = ServerProcessLauncher.BuildArguments(
                new AgentClientOptions { PluginsDirectory = root });

            var index = args.IndexOf("--plugins-dir");
            Assert.IsTrue(index >= 0, "插件目录存在时必须下发 --plugins-dir");
            Assert.AreEqual(Path.GetFullPath(root), args[index + 1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void 启动器插件目录不存在时不下发plugins_dir参数()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"luban-plugins-missing-{Guid.NewGuid():N}");

        var args = ServerProcessLauncher.BuildArguments(
            new AgentClientOptions { PluginsDirectory = missing });

        Assert.IsFalse(args.Contains("--plugins-dir"), "目录不存在时交给宿主回落默认目录");
    }

    [TestMethod]
    public void 会话帧携带本轮标识用于取消匹配()
    {
        var frame = AgentSseFrame.ForSession("s1", created: false, runId: "run-9");
        var payload = frame.GetPayload<Dictionary<string, object>>();

        Assert.AreEqual(AgentSseEventNames.Session, frame.Name);
        Assert.IsNotNull(payload);
        Assert.AreEqual("s1", payload["sessionId"]?.ToString());
        Assert.AreEqual("run-9", payload["runId"]?.ToString());

        // 缺省时字段存在但为 null（老客户端忽略即可）
        var legacy = AgentSseFrame.ForSession("s1", created: true);
        Assert.IsTrue(legacy.Data.Contains("runId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void 取消请求可携带本轮标识且允许缺省()
    {
        var body = AgentApiJson.WrapPayload(new AgentCancelRequest { SessionId = "s1", RunId = "run-9" });
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var payload = document.RootElement.GetProperty(AgentApiJson.PayloadField).GetString();

        var restored = AgentApiJson.UnwrapPayload<AgentCancelRequest>(payload);
        Assert.IsNotNull(restored);
        Assert.AreEqual("s1", restored.SessionId);
        Assert.AreEqual("run-9", restored.RunId);

        var withoutRun = System.Text.Json.JsonSerializer.Deserialize<AgentCancelRequest>("{\"sessionId\":\"s1\"}");
        Assert.IsNotNull(withoutRun);
        Assert.IsNull(withoutRun.RunId, "未携带 runId 时按旧语义取消当前回合");
    }

    [TestMethod]
    public void 启动器透传调试开关()
    {
        var debug = ServerProcessLauncher.BuildArguments(new AgentClientOptions { Debug = true });
        Assert.IsTrue(debug.Contains("--debug"));

        var normal = ServerProcessLauncher.BuildArguments(new AgentClientOptions());
        Assert.IsFalse(normal.Contains("--debug"));
    }

    [TestMethod]
    public void 服务端异常按状态码区分不存在()
    {
        Assert.IsTrue(new AgentServerException(404, "会话不存在").IsNotFound);
        Assert.IsFalse(new AgentServerException(403, "不属于本用户").IsNotFound);
        Assert.AreEqual(403, new AgentServerException(403, "x").Code);
    }

    [TestMethod]
    public void 编排进度帧往返_保留事件与节点活动明细()
    {
        var original = new OrchestrationProgressEvent(
            LuBan.AIAgent.Orchestration.Models.ProgressEventType.NodeActivity,
            "node-1",
            "子代理正在执行",
            1.5,
            null,
            new LuBan.AIAgent.Orchestration.Models.NodeActivityItem
            {
                Kind = LuBan.AIAgent.Orchestration.Models.NodeActivityKind.ToolCall,
                Content = "{\"path\":\"a.txt\"}",
                ToolName = "read_file",
                CallId = "call-9",
                Timestamp = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Local)
            });

        var frame = AgentSseFrame.Create(
            AgentSseEventNames.OrchestrationProgress, OrchestrationProgressPayload.From(original));

        Assert.AreEqual(AgentSseEventNames.OrchestrationProgress, frame.Name);
        var payload = frame.GetPayload<OrchestrationProgressPayload>();
        Assert.IsNotNull(payload);

        var restored = payload.ToEvent();
        Assert.AreEqual(original.EventType, restored.EventType);
        Assert.AreEqual("node-1", restored.NodeId);
        Assert.AreEqual("子代理正在执行", restored.Message);
        Assert.AreEqual(1.5, restored.ElapsedSeconds);
        Assert.IsNotNull(restored.Activity);
        Assert.AreEqual(original.Activity!.Kind, restored.Activity!.Kind);
        Assert.AreEqual("read_file", restored.Activity.ToolName);
        Assert.AreEqual("call-9", restored.Activity.CallId);
        Assert.AreEqual(original.Activity.Timestamp, restored.Activity.Timestamp);
    }

    [TestMethod]
    public void 编排进度帧缺活动明细时还原为空()
    {
        var original = new OrchestrationProgressEvent(
            LuBan.AIAgent.Orchestration.Models.ProgressEventType.NodeStarted, "node-2", "开始", null, null);
        var frame = AgentSseFrame.Create(
            AgentSseEventNames.OrchestrationProgress, OrchestrationProgressPayload.From(original));

        var restored = frame.GetPayload<OrchestrationProgressPayload>()!.ToEvent();
        Assert.AreEqual(LuBan.AIAgent.Orchestration.Models.ProgressEventType.NodeStarted, restored.EventType);
        Assert.IsNull(restored.Activity);
    }
}