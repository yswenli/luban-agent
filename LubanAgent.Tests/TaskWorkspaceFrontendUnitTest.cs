/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： TaskWorkspaceFrontendUnitTest
*描述：F2 任务详情与薄会话面板的静态前端契约测试
*****************************************************************************/

using System.ComponentModel;
using System.Diagnostics;

namespace LubanAgent.Tests;

[TestClass]
public class TaskWorkspaceFrontendUnitTest
{
    [TestMethod]
    public void 路由与Frame元信息_注册独立任务详情页()
    {
        var index = ReadWeb("index.html");
        var app = ReadWeb("assets/app.js");

        StringAssert.Contains(index, "id=\"screen-task\"");
        StringAssert.Contains(index, "id=\"actions-task\"");
        StringAssert.Contains(index, "src=\"assets/task.js\"");
        StringAssert.Contains(app, "path === '#/task'");
        StringAssert.Contains(app, "YunxiaoTask.init()");
        StringAssert.Contains(app, "YunxiaoTask.enter()");

        StringAssert.Contains(index, "data-frame=\"F0\"");
        StringAssert.Contains(index, "data-frame=\"F1\"");
        StringAssert.Contains(index, "data-frame=\"F2\"");
        StringAssert.Contains(index, "data-frame=\"F3\"");
        StringAssert.Contains(index, "data-frame=\"F4\"");
        StringAssert.Contains(index, "data-frame=\"F5 F6\"");
        StringAssert.Contains(index, "data-frame=\"F8\"");
        StringAssert.Contains(index, "data-frame=\"F9\"");
        StringAssert.Contains(index, "data-feature=\"功能 7\"");
        StringAssert.Contains(index, "data-prd-section=\"FR-7 / §2.7 / §4.10\"");
    }

    [TestMethod]
    public void 看板任务卡_携带项目与工作项进入详情()
    {
        var board = ReadWeb("assets/board.js");

        StringAssert.Contains(board, "#/task?projectId=");
        StringAssert.Contains(board, "workitemId=");
        StringAssert.Contains(board, "encodeURIComponent(task.id)");
        StringAssert.Contains(board, "<a class=\"board-card\"");
    }

    [TestMethod]
    public void 任务页_包含详情评论与首轮会话契约()
    {
        var index = ReadWeb("index.html");
        var taskPath = WebPath("assets/task.js");
        Assert.IsTrue(File.Exists(taskPath), "缺少 F2 前端实现 assets/task.js");
        var task = File.ReadAllText(taskPath);

        foreach (var id in new[]
                 {
                     "task-title", "task-description", "task-acceptance", "task-parent", "task-sprint-name",
                     "task-code-context", "task-comments", "task-comment-input", "btn-task-comment",
                     "task-workspace", "task-seed-summary", "task-session-title", "task-messages",
                     "task-followup-input", "btn-task-start", "btn-task-followup", "btn-task-cancel",
                     "btn-task-restart"
                 })
        {
            StringAssert.Contains(index, $"id=\"{id}\"");
        }

        StringAssert.Contains(task, "/yunxiao/taskdetail");
        StringAssert.Contains(task, "/yunxiao/taskcomment");
        StringAssert.Contains(task, "/yunxiao/tasksession");
        StringAssert.Contains(task, "/yunxiao/tasksessionsave");
        StringAssert.Contains(task, "/workspace/byrootpath");
        StringAssert.Contains(task, "/workspace/create");
        StringAssert.Contains(task, "/session/create");
        StringAssert.Contains(task, "/agent/chat");
        StringAssert.Contains(task, "permissionMode: 'plan'");
        StringAssert.Contains(task, "enableTools: true");
        StringAssert.Contains(task, "detail.canStart");
        StringAssert.Contains(task, "detail.seedPrompt");
        StringAssert.Contains(task, "detail.seedCommentCount");
    }

    [TestMethod]
    public void 任务页_支持恢复续流取消且不自造阶段控件()
    {
        var index = ReadWeb("index.html");
        var taskPath = WebPath("assets/task.js");
        Assert.IsTrue(File.Exists(taskPath), "缺少 F2 前端实现 assets/task.js");
        var task = File.ReadAllText(taskPath);

        StringAssert.Contains(task, "/session/latestmessages");
        StringAssert.Contains(task, "/agent/active");
        StringAssert.Contains(task, "/agent/attach");
        StringAssert.Contains(task, "/agent/cancel");
        StringAssert.Contains(task, "run.snapshot");
        StringAssert.Contains(task, "text.delta");
        StringAssert.Contains(task, "think.delta");
        StringAssert.Contains(task, "seedDigest");

        var combined = index + Environment.NewLine + task;
        Assert.IsFalse(combined.Contains("阶段一", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("阶段二", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("批准方案", StringComparison.Ordinal));
    }

    [TestMethod]
    public void 会话状态_匹配宿主数值枚举且取消优先于错误与完成()
    {
        var task = ReadWeb("assets/task.js");

        StringAssert.Contains(task, "const SessionStatus");
        StringAssert.Contains(task, "created: 1");
        StringAssert.Contains(task, "running: 2");
        StringAssert.Contains(task, "cancelled: 4");
        StringAssert.Contains(task, "status: SessionStatus.running");
        StringAssert.Contains(task, "saveBinding(SessionStatus.completed)");
        StringAssert.Contains(task, "saveBinding(SessionStatus.failed");
        StringAssert.Contains(task, "saveBinding(SessionStatus.cancelled");
        StringAssert.Contains(task, "saveBinding(SessionStatus.created)");
        var cancelled = task.IndexOf("if (state.cancelRequested && (state.streamFailed || state.streamDone))", StringComparison.Ordinal);
        var failed = task.IndexOf("if (state.streamFailed)", StringComparison.Ordinal);
        var done = task.IndexOf("if (state.streamDone)", StringComparison.Ordinal);
        Assert.IsTrue(cancelled >= 0 && failed >= 0 && done >= 0 && cancelled < failed && cancelled < done,
            "取消分支必须先于普通 SSE 错误与完成分支处理");
    }

    [TestMethod]
    public async Task 前端运行时_Node行为回归测试通过()
    {
        var script = TestPath("TaskWorkspaceFrontendRuntimeTest.cjs");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"--test \"{script}\"",
                WorkingDirectory = Path.GetDirectoryName(script)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        try
        {
            Assert.IsTrue(process.Start(), "无法启动 Node.js 前端运行时测试");
        }
        catch (Win32Exception error)
        {
            Assert.Inconclusive($"环境原因：未找到或无法启动 Node.js，前端运行时测试未执行。{error.Message}");
            return;
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await Task.WhenAll(stdout, stderr);
        Assert.AreEqual(0, process.ExitCode,
            $"node --test 退出码 {process.ExitCode}{Environment.NewLine}stdout:{Environment.NewLine}{output[0]}{Environment.NewLine}stderr:{Environment.NewLine}{output[1]}");
    }

    private static string ReadWeb(string relative) => File.ReadAllText(WebPath(relative));

    private static string TestPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "LubanAgent.Tests", fileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("未找到前端运行时测试脚本", fileName);
    }

    private static string WebPath(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var root = Path.Combine(directory.FullName, "LubanAgent.Plugin.YunXiao", "web");
            if (Directory.Exists(root))
                return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("未找到云效插件 web 目录");
    }
}
