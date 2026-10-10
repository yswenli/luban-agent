# Agent 工具缺口补齐 实现计划（Edit / Git / MCP 资源 / Web fetch）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `luban-framework` 的 `LuBan.AIAgent` / `LuBan.Common` 中补齐 Edit、Git、MCP 资源读取、Web fetch 增强四项工具能力，并在 `luban-agent` 侧同步展示名、配置与中英 README，最后删除两个历史空目录。

**Architecture:** 工具实现全部落在框架程序集；`luban-agent`（CLI + Codex 共用 `LubanAgentCore`）仅通过 NuGet 依赖，故本计划先在 `LuBan.Common` 增加通用能力（HTML→Markdown、带 ContentType 的响应读取），再在 `LuBan.AIAgent` 新增/扩展工具（`FileSystemToolGroup`、新 `GitToolGroup`、`MCPToolPlugin`、`WebToolGroup`），最后回 agent 侧补展示名/配置/文档。每个 Phase 独立可交付、可构建、可测试。

**Tech Stack:** .NET 10、C#、Microsoft.Extensions.AI（`AIFunctionFactory`）、MSTest、ReverseMarkdown、HtmlAgilityPack、`ProcessRunner`（外部进程执行）。

**关联设计文档:** `luban-agent/docs/superpowers/specs/2026-10-09-agent-tool-gaps-design.md`（状态：已批准）。

**范围与边界（务必先读）:**
- 所有 PowerShell 命令的 `workdir` 参数按需指定；**根目录 `luban/` 不是 git 仓库**，git 提交必须 `workdir` 进入 `luban-framework` 或 `luban-agent` 子仓库执行。
- 工具计数：Git 只读 8 + 写 18 = **26**。工具组总数由 10 变 **11**。
- 本计划**不含 worktree**（用户已裁决本轮不做）。
- CLI/Codex 双端共用 `LubanAgentCore`，`ToolDisplayNames` 位于 agent 仓 `LubanAgentCore\Utils\ToolDisplayNames.cs`（**不在框架**）。
- Git 工具默认不进编排子代理（`DefaultToolGroups=["filesystem"]`），本轮不改该默认值，仅在 README 标注。
- 确认语义铁律：**任何 Git 工具都不加入 `AlwaysConfirmTools`**；工作区内一律免确认，越界逐次确认；只读 Git 工具加入 `DefaultReadOnlyTools`（仅 Plan 模式生效）。
- 冲突处理为 **C-2 结果回传**：检测到冲突返回失败态 + 文件清单，交给 agent 收尾，工具**不自动** `add`/`--continue`。

---

## 文件结构（先锁定职责）

**框架 · LuBan.Common**
- Modify: `LuBan.Common/LuBan.Common.csproj` — 增 `ReverseMarkdown` 包引用。
- Create: `LuBan.Common/MarkdownUtil.cs` — HTML→Markdown 静态工具。
- Modify: `LuBan.Common/Http/HttpClientProxy.cs` — 增 `GetResponseAsync`。
- Modify: `LuBan.Common/README.md` + `README.en.md` — 说明新能力。

**框架 · LuBan.AIAgent**
- Create: `LuBan.AIAgent/Configuration/GitToolOptions.cs` — Git 组配置。
- Modify: `LuBan.AIAgent/Configuration/ToolGroupOptions.cs` — 增 `Git` 成员。
- Modify: `LuBan.AIAgent/Configuration/WebToolOptions.cs` — 增 `DefaultFormat`。
- Create: `LuBan.AIAgent/Tools/Git/GitToolPlugin.cs` — Git 插件 + 工具组（26 方法）。
- Modify: `LuBan.AIAgent/Tools/FileSystem/FileSystemToolPlugin.cs` — Edit/MultiEdit + `EditOperation`。
- Modify: `LuBan.AIAgent/MCP/MCPToolPlugin.cs` — 资源工具 ×2。
- Modify: `LuBan.AIAgent/Tools/Web/WebToolPlugin.cs` — format 参数与转换链。
- Modify: `LuBan.AIAgent/Abstractions/ToolConfirmationService.cs` — 只读默认集合补 Git/资源工具名。
- Modify: `LuBan.AIAgent/LuBanAgentExtensions.cs` — 注册 `GitToolPlugin`。
- Modify: `LuBan.AIAgent/README.md` + `README.en.md` — 工具组表/计数/目录树。

**框架 · 测试**
- Create: `LuBan.XTestProject/EditToolUnitTest.cs`
- Create: `LuBan.XTestProject/GitToolUnitTest.cs`
- Create: `LuBan.XTestProject/McpResourceToolUnitTest.cs`
- Create: `LuBan.XTestProject/WebFetchFormatUnitTest.cs`

**Agent · LubanAgentCore / 宿主**
- Modify: `LubanAgentCore/Utils/ToolDisplayNames.cs`
- Modify: `LubanAgentCli/appsettings.json`（可选补 `Tools:Git`）
- Modify: `LubanAgentCli/README.md` + `README.en.md`、`LubanAgentCodex/README.md` + `README.en.md`

**清理**
- Delete dirs: `LuBan.AIAgent/Tools/Database/`、`LuBan.AIAgent/Tools/Redis/`

---

## Phase A：LuBan.Common 基础能力

### Task A1：引入 ReverseMarkdown 并新增 `MarkdownUtil.HtmlToMarkdown`

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.Common\LuBan.Common.csproj`
- Create: `D:\WorkBench\Walle\luban\luban-framework\LuBan.Common\MarkdownUtil.cs`
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\MarkdownUtilUnitTest.cs`

- [ ] **Step 1：新增包引用**

在 `LuBan.Common.csproj` 的 `<ItemGroup>` 包引用列表中，紧随 `HtmlAgilityPack` 行后新增一行：

```xml
    <PackageReference Include="ReverseMarkdown" Version="4.2.0" />
```

若还原报“版本不存在”，改为运行 `dotnet add luban-framework/LuBan.Common/LuBan.Common.csproj package ReverseMarkdown` 取最新稳定版，再把生成的版本号写回 csproj。

- [ ] **Step 2：写失败测试**

创建 `MarkdownUtilUnitTest.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.XTestProject
*文件名： MarkdownUtilUnitTest
*创建时间：2026/10/9
*描述：验证 HTML→Markdown 基础转换
*****************************************************************************/
namespace LuBan.XTestProject;

[TestClass]
public class MarkdownUtilUnitTest
{
    [TestMethod]
    public void HtmlToMarkdown_ConvertsHeadingsAndLinks()
    {
        const string html = "<h1>标题</h1><p>正文 <a href=\"https://example.com\">链接</a></p>";

        var md = LuBan.Common.MarkdownUtil.HtmlToMarkdown(html);

        StringAssert.Contains(md, "# 标题");
        StringAssert.Contains(md, "正文");
        StringAssert.Contains(md, "[链接](https://example.com)");
    }

    [TestMethod]
    public void HtmlToMarkdown_EmptyInput_ReturnsEmpty()
    {
        var md = LuBan.Common.MarkdownUtil.HtmlToMarkdown("");
        Assert.AreEqual(string.Empty, md);
    }
}
```

- [ ] **Step 3：运行测试确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~MarkdownUtilUnitTest"`
Expected: 编译失败（`MarkdownUtil` 不存在）。这即“失败”。

- [ ] **Step 4：实现 `MarkdownUtil`**

创建 `MarkdownUtil.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.Common
*文件名： MarkdownUtil
*创建时间：2026/10/9
*描述：HTML 转 Markdown 工具（基于 ReverseMarkdown）
*****************************************************************************/
using ReverseMarkdown;

namespace LuBan.Common;

/// <summary>
/// HTML 与 Markdown 互转工具。
/// </summary>
public static class MarkdownUtil
{
    /// <summary>
    /// 把 HTML 文本转换为 Markdown。转换失败时抛出异常，交由调用方决定降级策略。
    /// </summary>
    /// <param name="html">待转换的 HTML 文本。</param>
    /// <param name="baseUrl">保留参数，供将来解析相对链接使用；当前实现忽略。</param>
    /// <returns>转换后的 Markdown 文本；输入为空时返回空字符串。</returns>
    public static string HtmlToMarkdown(this string html, string? baseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var converter = new Converter(new Config
        {
            UnknownTags = Config.UnknownTagsOption.Bypass,
            GithubFlavored = true,
            RemoveComments = true,
            SmartHrefHandling = true
        });
        return converter.Convert(html);
    }
}
```

- [ ] **Step 5：运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~MarkdownUtilUnitTest"`
Expected: PASS（2 passed）。

- [ ] **Step 6：提交（进入 framework 子仓库）**

```powershell
git add luban-framework/LuBan.Common/LuBan.Common.csproj luban-framework/LuBan.Common/MarkdownUtil.cs luban-framework/LuBan.XTestProject/MarkdownUtilUnitTest.cs
git commit -m "新增 HTML 转 Markdown 工具 MarkdownUtil"
```

（说明：`git add` 路径相对 workspace 根即可，但 `git commit` 需在 `luban-framework` 子仓库内执行；下文所有提交同此约定。执行时 `workdir=luban-framework`，路径改为仓库内相对路径。）

### Task A2：`HttpClientProxy.GetResponseAsync`（返回字节 + ContentType）

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.Common\Http\HttpClientProxy.cs`（在 `GetBytesAsync` 之后新增方法，约 line 430 附近）
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\HttpClientProxyResponseUnitTest.cs`

- [ ] **Step 1：写失败测试（本地文件 URL，无需外网）**

创建 `HttpClientProxyResponseUnitTest.cs`。为一个真实可访问的本地页面测 ContentType，使用 `data:`/`file:` 不便；改用公共方法签名存在性 + gzip 逻辑复用验证。核心断言签名与返回首元素：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.XTestProject
*文件名： HttpClientProxyResponseUnitTest
*创建时间：2026/10/9
*描述：验证 GetResponseAsync 返回字节与 ContentType
*****************************************************************************/
using LuBan.Common.Http;

namespace LuBan.XTestProject;

[TestClass]
public class HttpClientProxyResponseUnitTest
{
    [TestMethod]
    public async Task GetResponseAsync_ReturnsBytesAndContentType()
    {
        // 使用 example.com 作为稳定可访问的 text/html 目标；无网络时允许跳过
        var proxy = HttpClientProxy.Create(new Uri("https://example.com/"), timeout: 30, useLog: false);
        (byte[] Bytes, string? ContentType) result;
        try
        {
            result = await proxy.GetResponseAsync(string.Empty, timeout: 30);
        }
        catch (Exception ex)
        {
            Assert.Inconclusive($"网络不可用，跳过：{ex.Message}");
            return;
        }

        Assert.IsTrue(result.Bytes.Length > 0);
        StringAssert.Contains(result.ContentType ?? "", "html");
    }
}
```

- [ ] **Step 2：运行测试确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~HttpClientProxyResponseUnitTest"`
Expected: 编译失败（`GetResponseAsync` 不存在）。

- [ ] **Step 3：实现 `GetResponseAsync`**

在 `HttpClientProxy.cs` 的 `GetBytesAsync` 方法之后新增：

```csharp
    /// <summary>
    /// GET 请求，返回响应字节与响应 Content-Type（MediaType）。
    /// 复用 gzip 手动解压与 ApiCallLog 记录；不改动 <see cref="GetBytesAsync"/> 行为。
    /// </summary>
    /// <param name="resource">相对资源路径。</param>
    /// <param name="headers">请求头。</param>
    /// <param name="timeout">超时秒数。</param>
    /// <returns>响应字节与 MediaType（可能为 null）。</returns>
    public async Task<(byte[] Bytes, string? ContentType)> GetResponseAsync(
        string resource, Dictionary<string, string>? headers = null, int timeout = 180)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        Exception? exception = null;

        byte[] bytes = [];
        string? contentType = null;

        try
        {
            var reqMsg = new HttpRequestMessage(new HttpMethod("GET"), resource);

            FillHeaders(reqMsg, headers);

            using var resMsg = await SendAsync(reqMsg, timeout);

            bytes = await resMsg.Content.ReadAsByteArrayAsync();

            contentType = resMsg.Content.Headers.ContentType?.MediaType;

            if (resMsg.Content.Headers.ContentEncoding != null &&
                resMsg.Content.Headers.ContentEncoding.Count > 0 &&
                resMsg.Content.Headers.ContentEncoding.First() == "gzip")
            {
                bytes = ZipUtil.Decompress(bytes) ?? [];
            }
        }
        catch (Exception ex)
        {
            exception = ex;
        }
        stopwatch.Stop();

        var apiLog = new ApiLogInfo()
        {
            CallIp = "",
            Url = _httpClient.BaseAddress?.ToString().RemoveLast() ?? "" + resource,
            RequestMethod = "GET",
            Header = (headers == null ? "" : headers.ToJson()),
            Input = string.Empty,
            Cost = stopwatch.ElapsedMilliseconds,
            Created = DateTimeUtil.Now
        };

        if (exception != null)
        {
            apiLog.Exception = exception;
            apiLog.StatusCode = 500;
        }
        else
        {
            apiLog.StatusCode = 200;
        }

        apiLog.Output = bytes?.ToNoTitleString() ?? "";

        if (_useLog) Logger.ApiCallLog(apiLog);

        if (exception != null) throw exception;

        return (bytes ?? [], contentType);
    }
```

（`Stopwatch`、`ZipUtil`、`ApiLogInfo`、`DateTimeUtil`、`_httpClient`、`_useLog`、`FillHeaders`、`SendAsync`、`RemoveLast`、`ToJson`、`ToNoTitleString` 均为该文件已有依赖，无需新增 using。）

- [ ] **Step 4：构建验证**

Run: `dotnet build luban-framework/LuBan.Common/LuBan.Common.csproj`
Expected: Build succeeded。

- [ ] **Step 5：运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~HttpClientProxyResponseUnitTest"`
Expected: PASS 或 Inconclusive（无网络时）——**不得为 Failed**。

- [ ] **Step 6：提交**

```powershell
git add luban-framework/LuBan.Common/Http/HttpClientProxy.cs luban-framework/LuBan.XTestProject/HttpClientProxyResponseUnitTest.cs
git commit -m "HttpClientProxy 新增返回 ContentType 的 GetResponseAsync"
```

---

## Phase B：Edit / MultiEdit（filesystem 组）

### Task B0：验证 `AIFunctionFactory` 对象数组参数绑定（Go/No-Go 探针）

**Files:**
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\AIFunctionArrayBindingProbeUnitTest.cs`

- [ ] **Step 1：写探针测试**

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.XTestProject
*文件名： AIFunctionArrayBindingProbeUnitTest
*创建时间：2026/10/9
*描述：探针——验证 AIFunctionFactory 能绑定对象数组参数（MultiEdit 前置）
*****************************************************************************/
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace LuBan.XTestProject;

public class ProbeEditOperation
{
    public string OldString { get; set; } = "";
    public string NewString { get; set; } = "";
    public bool ReplaceAll { get; set; }
}

public class ProbeToolGroup
{
    public int Sum(int[] values) => values.Sum();
    public int SumOps(IReadOnlyList<ProbeEditOperation> ops) => ops.Count;
}

[TestClass]
public class AIFunctionArrayBindingProbeUnitTest
{
    private static string? Call(AIFunction fn, string jsonArgs)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonArgs)!;
        return fn.InvokeAsync(new AIFunctionArguments(args)).GetAwaiter().GetResult()?.ToString();
    }

    [TestMethod]
    public void ObjectArrayBinding_IsSupported()
    {
        var fn = AIFunctionFactory.Create(
            typeof(ProbeToolGroup).GetMethod(nameof(ProbeToolGroup.SumOps))!, new ProbeToolGroup());

        var result = Call(fn, "{\"ops\":[{\"oldString\":\"a\",\"newString\":\"b\",\"replaceAll\":true}]}");

        Assert.AreEqual("1", result);
    }
}
```

- [ ] **Step 2：运行探针**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AIFunctionArrayBindingProbeUnitTest"`
Expected: **PASS** → 继续 Task B1。
- 若 FAIL（无法绑定对象数组）→ **停止本 Phase 的 MultiEdit 部分**：只做 Task B1/B2（`EditOperation` 与 `EditFileAsync`），跳过 Task B4，并在 `FileSystemToolPlugin` 不注册 MultiEdit；把结论回写设计文档 §3.1。**执行者须先向用户报告此分叉再继续。**

- [ ] **Step 3：删除探针（保留结论）**

删除 `AIFunctionArrayBindingProbeUnitTest.cs`（探针使命完成；结论记录在提交信息/设计文档）。

- [ ] **Step 4：提交**

```powershell
git add luban-framework/  # 或在 luban-framework 内 git add -A
git commit -m "验证 AIFunctionFactory 支持对象数组参数绑定后移除探针"
```

### Task B1：新增 `EditOperation` 模型

**Files:**
- Create: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Tools\FileSystem\EditOperation.cs`

- [ ] **Step 1：实现模型**

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.AIAgent.Tools.FileSystem
*文件名： EditOperation
*创建时间：2026/10/9
*描述：批量编辑的单条替换操作
*****************************************************************************/
namespace LuBan.AIAgent.Tools.FileSystem;

/// <summary>
/// 批量编辑（MultiEdit）中的单条精确字符串替换操作。
/// 使用 class + 可写属性，供 <c>AIFunctionFactory</c> 从 JSON 反序列化数组元素。
/// </summary>
public class EditOperation
{
    /// <summary>
    /// 待替换的原文（必须唯一或配合 <see cref="ReplaceAll"/>）。
    /// </summary>
    public string OldString { get; set; } = "";

    /// <summary>
    /// 替换后的新文本。
    /// </summary>
    public string NewString { get; set; } = "";

    /// <summary>
    /// 是否允许替换全部匹配；false 时多匹配视为歧义并报错。
    /// </summary>
    public bool ReplaceAll { get; set; }
}
```

- [ ] **Step 2：构建验证**

Run: `dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj`
Expected: Build succeeded。

- [ ] **Step 3：提交**

```powershell
git add luban-framework/LuBan.AIAgent/Tools/FileSystem/EditOperation.cs
git commit -m "新增批量编辑操作模型 EditOperation"
```

### Task B2：`EditFileAsync` 精确替换

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Tools\FileSystem\FileSystemToolPlugin.cs`
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\EditToolUnitTest.cs`

- [ ] **Step 1：写失败测试**

创建 `EditToolUnitTest.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.XTestProject
*文件名： EditToolUnitTest
*创建时间：2026/10/9
*描述：验证 EditFileAsync / MultiEditFileAsync 精确替换语义
*****************************************************************************/
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Tools.FileSystem;
using Microsoft.Extensions.Options;

namespace LuBan.XTestProject;

[TestClass]
public class EditToolUnitTest
{
    private static string _root = null!;
    private static FileSystemToolGroup _group = null!;

    [ClassInitialize]
    public static void Setup(TestContext context)
    {
        _root = Path.Combine(Path.GetTempPath(), "luban-edit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var options = Options.Create(new LuBanAgentOptions
        {
            Tools = new ToolGroupOptions
            {
                FileSystem = new FileSystemToolOptions { Enabled = true, AllowedRoots = new List<string> { _root } }
            }
        });
        var ctx = new ToolConfirmationContext
        {
            Callback = (_, _) => Task.FromResult(ConfirmResult.Allow),
            WorkspacePathChecker = _ => true
        };
        _group = new FileSystemToolGroup(new ToolConfirmationService(ctx, options));
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    private string Write(string name, string content)
    {
        var p = Path.Combine(_root, name);
        File.WriteAllText(p, content);
        return p;
    }

    [TestMethod]
    public async Task EditFileAsync_SingleMatch_ReplacesAndWrites()
    {
        var p = Write("a.txt", "hello world");

        var result = await _group.EditFileAsync(p, "world", "luban");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("hello luban", File.ReadAllText(p));
    }

    [TestMethod]
    public async Task EditFileAsync_NoMatch_ReturnsFail()
    {
        var p = Write("b.txt", "abc");
        var result = await _group.EditFileAsync(p, "zzz", "y");
        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Message!, "未找到匹配");
        Assert.AreEqual("abc", File.ReadAllText(p));
    }

    [TestMethod]
    public async Task EditFileAsync_MultipleMatches_WithoutReplaceAll_ReturnsFail()
    {
        var p = Write("c.txt", "x x x");
        var result = await _group.EditFileAsync(p, "x", "y");
        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Message!, "replace_all");
        Assert.AreEqual("x x x", File.ReadAllText(p));
    }

    [TestMethod]
    public async Task EditFileAsync_MultipleMatches_WithReplaceAll_ReplacesAll()
    {
        var p = Write("d.txt", "x x x");
        var result = await _group.EditFileAsync(p, "x", "y", replace_all: true);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("y y y", File.ReadAllText(p));
    }

    [TestMethod]
    public async Task EditFileAsync_EmptyOldString_ReturnsFail()
    {
        var p = Write("e.txt", "abc");
        var result = await _group.EditFileAsync(p, "", "y");
        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Message!, "old_string 不能为空");
    }
}
```

- [ ] **Step 2：运行确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~EditToolUnitTest"`
Expected: 编译失败（`EditFileAsync` 不存在）。

- [ ] **Step 3：实现 `EditFileAsync`**

在 `FileSystemToolPlugin.cs` 的 `FileSystemToolGroup` 类内、`GetFileInfoAsync` 之前新增（复用已有 `ConfirmOrBlockAsync` 与 `DirectoryPathMessage`）：

```csharp
    /// <summary>
    /// 对文件执行精确字符串替换。old_string 必须唯一（或设置 replace_all）。
    /// </summary>
    /// <param name="path">目标文件路径。</param>
    /// <param name="old_string">待替换的原文。</param>
    /// <param name="new_string">替换后的新文本。</param>
    /// <param name="replace_all">是否替换全部匹配。</param>
    /// <returns>替换结果。</returns>
    [Description("对文件执行精确字符串替换（old_string → new_string）。默认要求 old_string 唯一，多点替换请用 MultiEdit 或 replace_all=true。创建/覆盖整个文件请用 WriteFile。")]
    public async Task<ToolResult<string>> EditFileAsync(string path, string old_string, string new_string, bool replace_all = false)
    {
        if (await ConfirmOrBlockAsync(nameof(EditFileAsync), path,
            new Dictionary<string, object?> { ["path"] = path, ["old_string"] = old_string, ["new_string"] = new_string, ["replace_all"] = replace_all }) is { } blocked)
        {
            return blocked;
        }

        if (string.IsNullOrEmpty(old_string))
            return ToolResult.Fail<string>("old_string 不能为空；如需创建/覆盖整个文件请用 WriteFile。");

        if (Directory.Exists(path))
            return ToolResult.Fail<string>(DirectoryPathMessage(path, "编辑文件需要包含文件名的文件路径"));
        if (!File.Exists(path))
            return ToolResult.Fail<string>($"未找到文件: {path}。请先用 ReadFile 核对路径。");

        try
        {
            var original = await File.ReadAllTextAsync(path);
            var count = CountOccurrences(original, old_string);

            if (count == 0)
                return ToolResult.Fail<string>("未找到匹配内容，请用 ReadFile 核对原文（注意空白/换行/缩进）。");
            if (count > 1 && !replace_all)
                return ToolResult.Fail<string>($"匹配到 {count} 处，请扩大 old_string 上下文以唯一定位，或设置 replace_all=true。");

            var updated = replace_all
                ? original.Replace(old_string, new_string)
                : ReplaceFirst(original, old_string, new_string);

            await File.WriteAllTextAsync(path, updated);
            return ToolResult.Ok<string>($"已替换 {count} 处。", $"path={path}; replaced={count}");
        }
        catch (Exception ex)
        {
            Logger.Error("编辑文件异常", ex, path);
            return ToolResult.Fail<string>($"操作失败: {ex.Message}");
        }
    }

    private static int CountOccurrences(string text, string token)
    {
        if (token.Length == 0) return 0;
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += token.Length;
        }
        return count;
    }

    private static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        var idx = text.IndexOf(oldValue, StringComparison.Ordinal);
        return idx < 0 ? text : text[..idx] + newValue + text[(idx + oldValue.Length)..];
    }
```

- [ ] **Step 4：运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~EditToolUnitTest"`
Expected: PASS（5 passed）。

- [ ] **Step 5：提交**

```powershell
git add luban-framework/LuBan.AIAgent/Tools/FileSystem/FileSystemToolPlugin.cs luban-framework/LuBan.XTestProject/EditToolUnitTest.cs
git commit -m "文件系统工具新增 EditFileAsync 精确替换"
```

### Task B3：注册 `EditFileAsync` 到 filesystem 工具组

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Tools\FileSystem\FileSystemToolPlugin.cs`（`FileSystemToolPlugin.GetTools` 的 `List<AIFunction>`）

- [ ] **Step 1：注册（无 workspaceRootProvider，读写类不兜底）**

在 `GetTools` 返回列表中，`WriteFileAsync` 行之后新增：

```csharp
            AIFunctionFactoryHelper.Create(toolGroup, nameof(FileSystemToolGroup.EditFileAsync)),
```

- [ ] **Step 2：构建验证**

Run: `dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj`
Expected: Build succeeded。

- [ ] **Step 3：提交**

```powershell
git add luban-framework/LuBan.AIAgent/Tools/FileSystem/FileSystemToolPlugin.cs
git commit -m "注册 EditFileAsync 到 filesystem 工具组"
```

### Task B4：`MultiEditFileAsync` 原子批量替换（B0 通过才执行）

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Tools\FileSystem\FileSystemToolPlugin.cs`
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\EditToolUnitTest.cs`（追加测试）

- [ ] **Step 1：追加失败测试**

在 `EditToolUnitTest.cs` 类内追加：

```csharp
    [TestMethod]
    public async Task MultiEditFileAsync_AllValid_AppliesAtomically()
    {
        var p = Write("m1.txt", "aaa bbb");
        var edits = new List<EditOperation>
        {
            new() { OldString = "aaa", NewString = "111" },
            new() { OldString = "bbb", NewString = "222" }
        };

        var result = await _group.MultiEditFileAsync(p, edits);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("111 222", File.ReadAllText(p));
    }

    [TestMethod]
    public async Task MultiEditFileAsync_AnyInvalid_WritesNothing()
    {
        var p = Write("m2.txt", "aaa bbb");
        var edits = new List<EditOperation>
        {
            new() { OldString = "aaa", NewString = "111" },
            new() { OldString = "zzz", NewString = "999" }
        };

        var result = await _group.MultiEditFileAsync(p, edits);

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.Message!, "第 2 条");
        Assert.AreEqual("aaa bbb", File.ReadAllText(p));
    }
```

- [ ] **Step 2：运行确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~MultiEditFileAsync"`
Expected: 编译失败（`MultiEditFileAsync` 不存在）。

- [ ] **Step 3：实现 `MultiEditFileAsync`**

在 `EditFileAsync` 之后新增：

```csharp
    /// <summary>
    /// 对同一文件按顺序批量应用多条精确替换（原子：任一条失败则整体不写入）。
    /// </summary>
    /// <param name="path">目标文件路径。</param>
    /// <param name="edits">替换操作列表。</param>
    /// <returns>批量替换结果。</returns>
    [Description("对同一文件原子批量执行多条精确字符串替换；任一条失败则整体不写入。")]
    public async Task<ToolResult<string>> MultiEditFileAsync(string path, IReadOnlyList<EditOperation> edits)
    {
        if (await ConfirmOrBlockAsync(nameof(MultiEditFileAsync), path,
            new Dictionary<string, object?> { ["path"] = path, ["edits"] = edits }) is { } blocked)
        {
            return blocked;
        }

        if (edits == null || edits.Count == 0)
            return ToolResult.Fail<string>("edits 不能为空。");

        if (Directory.Exists(path))
            return ToolResult.Fail<string>(DirectoryPathMessage(path, "编辑文件需要包含文件名的文件路径"));
        if (!File.Exists(path))
            return ToolResult.Fail<string>($"未找到文件: {path}。请先用 ReadFile 核对路径。");

        try
        {
            var current = await File.ReadAllTextAsync(path);

            for (var i = 0; i < edits.Count; i++)
            {
                var op = edits[i];
                var ordinal = i + 1;

                if (string.IsNullOrEmpty(op.OldString))
                    return ToolResult.Fail<string>($"第 {ordinal} 条失败：old_string 不能为空。");
                if (op.OldString == op.NewString)
                    return ToolResult.Fail<string>($"第 {ordinal} 条失败：old_string 与 new_string 相同。");

                var count = CountOccurrences(current, op.OldString);
                if (count == 0)
                    return ToolResult.Fail<string>($"第 {ordinal} 条失败：未找到匹配内容，请用 ReadFile 核对原文。");
                if (count > 1 && !op.ReplaceAll)
                    return ToolResult.Fail<string>($"第 {ordinal} 条失败：匹配到 {count} 处，请扩大上下文或设置 replaceAll=true。");

                current = op.ReplaceAll
                    ? current.Replace(op.OldString, op.NewString)
                    : ReplaceFirst(current, op.OldString, op.NewString);
            }

            await File.WriteAllTextAsync(path, current);
            return ToolResult.Ok<string>($"已应用 {edits.Count} 条替换。", $"path={path}; edits={edits.Count}");
        }
        catch (Exception ex)
        {
            Logger.Error("批量编辑文件异常", ex, path);
            return ToolResult.Fail<string>($"操作失败: {ex.Message}");
        }
    }
```

- [ ] **Step 4：注册 MultiEdit**

在 `GetTools` 列表中 `EditFileAsync` 行之后新增：

```csharp
            AIFunctionFactoryHelper.Create(toolGroup, nameof(FileSystemToolGroup.MultiEditFileAsync)),
```

- [ ] **Step 5：运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~EditToolUnitTest"`
Expected: PASS（7 passed）。

- [ ] **Step 6：提交**

```powershell
git add luban-framework/LuBan.AIAgent/Tools/FileSystem/FileSystemToolPlugin.cs luban-framework/LuBan.XTestProject/EditToolUnitTest.cs
git commit -m "文件系统工具新增 MultiEditFileAsync 原子批量替换"
```

---

## Phase C：Git 工具组

### Task C1：`GitToolOptions` 与 `ToolGroupOptions.Git`

**Files:**
- Create: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Configuration\GitToolOptions.cs`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Configuration\ToolGroupOptions.cs`

- [ ] **Step 1：创建选项类**

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.AIAgent.Configuration
*文件名： GitToolOptions
*创建时间：2026/10/9
*描述：Git 工具配置选项
*****************************************************************************/
namespace LuBan.AIAgent.Configuration;

/// <summary>
/// Git 工具配置。
/// </summary>
public class GitToolOptions
{
    /// <summary>
    /// 是否启用 Git 工具组（默认启用）。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 默认超时时间（毫秒），与脚本工具对齐。
    /// </summary>
    public int DefaultTimeout { get; set; } = 120000;
}
```

- [ ] **Step 2：在 `ToolGroupOptions` 增成员**

在 `Log` 属性之前新增：

```csharp
    /// <summary>
    /// Git 工具组配置（非 opt-in，默认启用）。
    /// </summary>
    public GitToolOptions Git { get; set; } = new();
```

- [ ] **Step 3：构建验证**

Run: `dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj`
Expected: Build succeeded。

- [ ] **Step 4：提交**

```powershell
git add luban-framework/LuBan.AIAgent/Configuration/GitToolOptions.cs luban-framework/LuBan.AIAgent/Configuration/ToolGroupOptions.cs
git commit -m "新增 Git 工具组配置选项"
```

### Task C2：`GitToolPlugin` + `GitToolGroup` 执行底座、冲突检测与工具骨架

**Files:**
- Create: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Tools\Git\GitToolPlugin.cs`
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\GitToolUnitTest.cs`

- [ ] **Step 1：写失败测试（本地临时 git 仓库）**

创建 `GitToolUnitTest.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.XTestProject
*文件名： GitToolUnitTest
*创建时间：2026/10/9
*描述：验证 Git 工具组执行底座、只读工具与冲突检测
*****************************************************************************/
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Infrastructure;
using LuBan.AIAgent.Tools.Git;
using Microsoft.Extensions.Options;

namespace LuBan.XTestProject;

[TestClass]
public class GitToolUnitTest
{
    private static string _repo = null!;
    private static GitToolGroup _group = null!;

    [ClassInitialize]
    public static void Setup(TestContext context)
    {
        _repo = Path.Combine(Path.GetTempPath(), "luban-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repo);

        var opts = Options.Create(new LuBanAgentOptions
        {
            WorkspaceRoot = _repo,
            Tools = new ToolGroupOptions { Git = new GitToolOptions { Enabled = true, DefaultTimeout = 60000 } }
        });
        var ctx = new ToolConfirmationContext
        {
            Callback = (_, _) => Task.FromResult(ConfirmResult.Allow),
            WorkspacePathChecker = _ => true
        };
        _group = new GitToolGroup(
            opts.Value.Tools.Git,
            new ProcessRunner(),
            new ToolConfirmationService(ctx, opts),
            () => _repo);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        try { if (Directory.Exists(_repo)) Directory.Delete(_repo, true); } catch { }
    }

    [TestMethod]
    public async Task GitInit_Then_Status_Works()
    {
        var init = await _group.GitInitAsync(workingDirectory: _repo);
        Assert.IsTrue(init.IsSuccess, init.Message);

        File.WriteAllText(Path.Combine(_repo, "f.txt"), "hi");
        var status = await _group.GitStatusAsync(workingDirectory: _repo);
        Assert.IsTrue(status.IsSuccess);
        StringAssert.Contains(status.Data!, "f.txt");
    }

    [TestMethod]
    public async Task GitCommit_RequiresAuthor_ReturnsActionableFailureOrSuccess()
    {
        await _group.GitInitAsync(workingDirectory: _repo);
        File.WriteAllText(Path.Combine(_repo, "g.txt"), "hi");
        await _group.GitAddAsync(paths: new[] { "g.txt" }, workingDirectory: _repo);

        var result = await _group.GitCommitAsync("test commit", workingDirectory: _repo);

        // 未配置 user.name/email 时 commit 会失败，工具应返回失败态（非异常）
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Message);
    }
}
```

- [ ] **Step 2：运行确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~GitToolUnitTest"`
Expected: 编译失败（`GitToolGroup` 不存在）。

- [ ] **Step 3：实现 `GitToolPlugin.cs`（含全部 26 个方法）**

创建 `Tools\Git\GitToolPlugin.cs`（完整内容）：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.AIAgent.Tools.Git
*文件名： GitToolPlugin
*创建时间：2026/10/9
*描述：Git 版本控制工具插件与工具组
*****************************************************************************/
using LuBan.AIAgent.Abstractions;

namespace LuBan.AIAgent.Tools.Git;

/// <summary>
/// Git 工具插件（GroupName="git"，默认启用）。
/// </summary>
public class GitToolPlugin : ILuBanToolPlugin
{
    private readonly GitToolOptions _options;

    /// <summary>
    /// 创建 GitToolPlugin 实例。
    /// </summary>
    /// <param name="options">Agent 配置。</param>
    public GitToolPlugin(IOptions<LuBanAgentOptions> options)
    {
        _options = options.Value.Tools.Git;
    }

    /// <inheritdoc />
    public string GroupName => "git";

    /// <inheritdoc />
    public string? Description => "Git 版本控制工具，支持状态、差异、提交、分支、合并、变基、暂存等操作";

    /// <inheritdoc />
    public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
    {
        var opts = toolsOptions?.Git ?? _options;
        var runner = sp.GetRequiredService<ProcessRunner>();
        var confirmationService = sp.GetRequiredService<IToolConfirmationService>();
        var agentOptions = sp.GetService<IOptions<LuBanAgentOptions>>();
        Func<string?> workspaceRootProvider = () => agentOptions?.Value.WorkspaceRoot;

        var group = new GitToolGroup(opts, runner, confirmationService, workspaceRootProvider);

        return new List<AIFunction>
        {
            // 只读
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitStatusAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitDiffAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitLogAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitShowAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitBranchListAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitRemoteListAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitBlameAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitStashListAsync)),
            // 写
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitAddAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitCommitAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitCheckoutAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitSwitchAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitBranchCreateAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitCherryPickAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitRevertAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitRestoreAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitMergeAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitStashAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitInitAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitCloneAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitFetchAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitPullAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitPushAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitResetAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitRebaseAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(GitToolGroup.GitCleanAsync)),
        };
    }

    /// <inheritdoc />
    public bool IsEnabled(LuBanAgentOptions options) => options.Tools.Git.Enabled;
}

/// <summary>
/// Git 工具组：以系统 git 为可执行文件，复用 <see cref="ProcessRunner"/> 执行。
/// 确认语义：以实际仓库工作目录作为确认路径（工作区内免确认、越界逐次确认）。
/// </summary>
public class GitToolGroup
{
    private readonly GitToolOptions _options;
    private readonly ProcessRunner _processRunner;
    private readonly IToolConfirmationService _confirmationService;
    private readonly Func<string?> _workspaceRootProvider;

    /// <summary>
    /// 创建 GitToolGroup 实例。
    /// </summary>
    /// <param name="options">Git 工具配置。</param>
    /// <param name="processRunner">进程执行器。</param>
    /// <param name="confirmationService">工具调用确认服务。</param>
    /// <param name="workspaceRootProvider">工作区根目录提供者（缺省工作目录）。</param>
    public GitToolGroup(
        GitToolOptions options,
        ProcessRunner processRunner,
        IToolConfirmationService confirmationService,
        Func<string?> workspaceRootProvider)
    {
        _options = options;
        _processRunner = processRunner;
        _confirmationService = confirmationService;
        _workspaceRootProvider = workspaceRootProvider;
    }

    // ─────────────── 执行底座 ───────────────

    private string? ResolveWorkingDirectory(string? workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory)) return workingDirectory;
        var root = _workspaceRootProvider();
        return string.IsNullOrWhiteSpace(root) ? null : root;
    }

    private async Task<ToolResult<string>?> ConfirmOrBlockAsync(
        string toolName, string? workingDirectory, Dictionary<string, object?> arguments)
    {
        var path = ResolveWorkingDirectory(workingDirectory);
        var outcome = await _confirmationService.EvaluateAsync(toolName, path, arguments).ConfigureAwait(false);
        return outcome switch
        {
            EnumConfirmationOutcome.Allowed => null,
            EnumConfirmationOutcome.Planned => ToolResult.Plan<string>(),
            _ => ToolResult.Denied<string>()
        };
    }

    private async Task<ToolResult<string>> RunGitAsync(
        string toolName,
        IReadOnlyList<string> args,
        string? workingDirectory,
        int? timeoutMs,
        bool detectConflict,
        Dictionary<string, object?> arguments)
    {
        if (await ConfirmOrBlockAsync(toolName, workingDirectory, arguments) is { } blocked)
            return blocked;

        var dir = ResolveWorkingDirectory(workingDirectory);
        var timeout = timeoutMs ?? _options.DefaultTimeout;
        var commandLine = "git " + string.Join(" ", args);

        var result = await _processRunner.RunAsync(
            "git", string.Empty, dir,
            stdin: null, timeoutMs: timeout,
            argumentList: args, outputEncoding: Encoding.UTF8);

        if (result.ExitCode == -1 &&
            (result.StandardError.Contains("可执行文件不存在") || result.StandardError.Contains("无法启动")))
        {
            return ToolResult.Fail<string>(
                "Git 工具不可用：未找到 git 可执行文件。请先安装 Git 并确保其在 PATH 中。",
                new { exitCode = -1, stdout = "", stderr = result.StandardError, commandLine, workingDirectory = dir }.ToJson());
        }

        if (detectConflict && result.ExitCode != 0)
        {
            var conflicts = await DetectConflictsAsync(dir, timeout);
            if (conflicts.Count > 0)
            {
                return ToolResult.Fail<string>(
                    "检测到合并冲突，请手动解决以下文件后告知我继续：\n" + string.Join("\n", conflicts),
                    new
                    {
                        exitCode = result.ExitCode,
                        stdout = result.StandardOutput,
                        stderr = result.StandardError,
                        commandLine,
                        workingDirectory = dir,
                        conflicts
                    }.ToJson());
            }
        }

        var payload = new
        {
            exitCode = result.ExitCode,
            stdout = result.StandardOutput,
            stderr = result.StandardError,
            durationMs = result.DurationMs,
            timedOut = result.TimedOut,
            commandLine,
            workingDirectory = dir
        }.ToJson();

        return result.ExitCode == 0
            ? ToolResult.Ok<string>(payload, "exitCode=0")
            : ToolResult.Fail<string>($"git 命令执行失败（exitCode={result.ExitCode}）：{result.StandardError}", payload);
    }

    private async Task<List<string>> DetectConflictsAsync(string? dir, int timeout)
    {
        var conflicts = new List<string>();

        var ls = await _processRunner.RunAsync("git", string.Empty, dir, null, timeout,
            argumentList: new[] { "ls-files", "-u" }, outputEncoding: Encoding.UTF8);
        if (!string.IsNullOrWhiteSpace(ls.StandardOutput))
            conflicts.AddRange(ls.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        var status = await _processRunner.RunAsync("git", string.Empty, dir, null, timeout,
            argumentList: new[] { "status", "--porcelain" }, outputEncoding: Encoding.UTF8);
        var markers = new[] { "UU", "AA", "DD", "AU", "UA", "DU", "UD" };
        foreach (var line in status.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length >= 2 && markers.Contains(line[..2]))
                conflicts.Add(line);
        }

        return conflicts.Distinct().ToList();
    }

    // ─────────────── 只读工具 ───────────────

    /// <summary>显示工作区 git 状态。</summary>
    /// <param name="workingDirectory">仓库工作目录（缺省工作区根）。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("显示 git 工作区状态（git status --porcelain -b）。")]
    public Task<ToolResult<string>> GitStatusAsync(string? workingDirectory = null, int? timeoutMs = null)
        => RunGitAsync(nameof(GitStatusAsync), new[] { "status", "--porcelain=v1", "-b" }, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["workingDirectory"] = workingDirectory });

    /// <summary>显示差异。</summary>
    /// <param name="path">限定路径。</param>
    /// <param name="staged">是否查看已暂存差异。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("显示 git 差异（git diff）。staged=true 查看已暂存差异。")]
    public Task<ToolResult<string>> GitDiffAsync(string? path = null, bool? staged = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "diff" };
        if (staged == true) args.Add("--staged");
        if (!string.IsNullOrWhiteSpace(path)) { args.Add("--"); args.Add(path!); }
        return RunGitAsync(nameof(GitDiffAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["path"] = path, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>显示提交日志。</summary>
    /// <param name="maxCount">最大条数。</param>
    /// <param name="path">限定路径。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("显示 git 提交日志（git log --oneline）。")]
    public Task<ToolResult<string>> GitLogAsync(int? maxCount = null, string? path = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "log", "--oneline", "-n", (maxCount ?? 20).ToString() };
        if (!string.IsNullOrWhiteSpace(path)) { args.Add("--"); args.Add(path!); }
        return RunGitAsync(nameof(GitLogAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["path"] = path, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>显示对象内容。</summary>
    /// <param name="reference">提交/标签/分支引用。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("显示 git 对象内容（git show <reference>）。")]
    public Task<ToolResult<string>> GitShowAsync(string reference, string? workingDirectory = null, int? timeoutMs = null)
        => RunGitAsync(nameof(GitShowAsync), new[] { "show", reference }, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["reference"] = reference, ["workingDirectory"] = workingDirectory });

    /// <summary>列出分支。</summary>
    /// <param name="includeRemote">是否包含远端分支。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("列出 git 分支（git branch）。includeRemote=true 时包含远端分支。")]
    public Task<ToolResult<string>> GitBranchListAsync(bool? includeRemote = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "branch" };
        if (includeRemote == true) args.Add("-a");
        return RunGitAsync(nameof(GitBranchListAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["workingDirectory"] = workingDirectory });
    }

    /// <summary>列出远端。</summary>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("列出 git 远端仓库（git remote -v）。")]
    public Task<ToolResult<string>> GitRemoteListAsync(string? workingDirectory = null, int? timeoutMs = null)
        => RunGitAsync(nameof(GitRemoteListAsync), new[] { "remote", "-v" }, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["workingDirectory"] = workingDirectory });

    /// <summary>追溯文件逐行提交。</summary>
    /// <param name="path">文件路径。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("追溯 git 文件逐行提交信息（git blame -- <path>）。")]
    public Task<ToolResult<string>> GitBlameAsync(string path, string? workingDirectory = null, int? timeoutMs = null)
        => RunGitAsync(nameof(GitBlameAsync), new[] { "blame", "--", path }, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["path"] = path, ["workingDirectory"] = workingDirectory });

    /// <summary>列出暂存。</summary>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("列出 git 暂存列表（git stash list）。")]
    public Task<ToolResult<string>> GitStashListAsync(string? workingDirectory = null, int? timeoutMs = null)
        => RunGitAsync(nameof(GitStashListAsync), new[] { "stash", "list" }, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["workingDirectory"] = workingDirectory });

    // ─────────────── 写工具 ───────────────

    /// <summary>暂存文件。</summary>
    /// <param name="paths">要暂存的路径列表。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git add 暂存指定路径。paths 为路径数组，传 [\".\"] 暂存全部。")]
    public Task<ToolResult<string>> GitAddAsync(string[] paths, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "add", "--" };
        args.AddRange(paths);
        return RunGitAsync(nameof(GitAddAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["paths"] = paths, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>提交。</summary>
    /// <param name="message">提交信息。</param>
    /// <param name="all">是否自动暂存已跟踪文件（-a）。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git commit 提交。all=true 时自动暂存已跟踪文件。")]
    public Task<ToolResult<string>> GitCommitAsync(string message, bool? all = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "commit", "-m", message };
        if (all == true) args.Add("--all");
        return RunGitAsync(nameof(GitCommitAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["message"] = message, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>检出。</summary>
    /// <param name="target">目标分支/提交。</param>
    /// <param name="createBranch">是否创建并切换（-b）。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git checkout 检出目标。createBranch=true 时创建并切换（-b）。")]
    public Task<ToolResult<string>> GitCheckoutAsync(string target, bool? createBranch = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "checkout" };
        if (createBranch == true) args.Add("-b");
        args.Add(target);
        return RunGitAsync(nameof(GitCheckoutAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["target"] = target, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>切换。</summary>
    /// <param name="target">目标分支。</param>
    /// <param name="createBranch">是否创建并切换（-c）。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git switch 切换分支。createBranch=true 时创建并切换（-c）。")]
    public Task<ToolResult<string>> GitSwitchAsync(string target, bool? createBranch = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "switch" };
        if (createBranch == true) args.Add("-c");
        args.Add(target);
        return RunGitAsync(nameof(GitSwitchAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["target"] = target, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>创建分支。</summary>
    /// <param name="name">分支名。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git branch 创建分支（不切换）。")]
    public Task<ToolResult<string>> GitBranchCreateAsync(string name, string? workingDirectory = null, int? timeoutMs = null)
        => RunGitAsync(nameof(GitBranchCreateAsync), new[] { "branch", name }, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["name"] = name, ["workingDirectory"] = workingDirectory });

    /// <summary>拣选提交。</summary>
    /// <param name="commits">提交引用列表。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git cherry-pick 拣选提交。commits 为提交引用数组。")]
    public Task<ToolResult<string>> GitCherryPickAsync(string[] commits, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "cherry-pick" };
        args.AddRange(commits);
        return RunGitAsync(nameof(GitCherryPickAsync), args, workingDirectory, timeoutMs, true,
            new Dictionary<string, object?> { ["commits"] = commits, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>回滚提交。</summary>
    /// <param name="commits">提交引用列表。</param>
    /// <param name="noCommit">是否不自动提交（--no-commit）。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git revert 回滚提交。commits 为提交引用数组。")]
    public Task<ToolResult<string>> GitRevertAsync(string[] commits, bool? noCommit = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "revert" };
        if (noCommit == true) args.Add("--no-commit");
        args.AddRange(commits);
        return RunGitAsync(nameof(GitRevertAsync), args, workingDirectory, timeoutMs, true,
            new Dictionary<string, object?> { ["commits"] = commits, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>恢复文件。</summary>
    /// <param name="paths">要恢复的路径列表。</param>
    /// <param name="staged">是否从暂存恢复（--staged）。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git restore 恢复文件。staged=true 时从暂存恢复（--staged）。")]
    public Task<ToolResult<string>> GitRestoreAsync(string[] paths, bool? staged = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "restore" };
        if (staged == true) args.Add("--staged");
        args.Add("--");
        args.AddRange(paths);
        return RunGitAsync(nameof(GitRestoreAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["paths"] = paths, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>合并。</summary>
    /// <param name="reference">要合并的引用。</param>
    /// <param name="noCommit">是否不自动提交（--no-commit）。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git merge 合并引用。冲突时返回需人工解决的文件清单。")]
    public Task<ToolResult<string>> GitMergeAsync(string reference, bool? noCommit = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "merge" };
        if (noCommit == true) args.Add("--no-commit");
        args.Add(reference);
        return RunGitAsync(nameof(GitMergeAsync), args, workingDirectory, timeoutMs, true,
            new Dictionary<string, object?> { ["reference"] = reference, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>暂存操作。</summary>
    /// <param name="action">动作：push/pop/apply/list/drop。</param>
    /// <param name="message">push 时的说明。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git stash 管理暂存。action 取 push/pop/apply/list/drop，缺省 push。")]
    public Task<ToolResult<string>> GitStashAsync(string? action = null, string? message = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var verb = string.IsNullOrWhiteSpace(action) ? "push" : action!.Trim().ToLowerInvariant();
        var args = new List<string> { "stash", verb };
        if (verb == "push" && !string.IsNullOrWhiteSpace(message)) { args.Add("-m"); args.Add(message!); }
        var detect = verb is "pop" or "apply";
        return RunGitAsync(nameof(GitStashAsync), args, workingDirectory, timeoutMs, detect,
            new Dictionary<string, object?> { ["action"] = action, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>初始化仓库。</summary>
    /// <param name="defaultBranch">初始分支名。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git init 初始化仓库。defaultBranch 指定初始分支名。")]
    public Task<ToolResult<string>> GitInitAsync(string? defaultBranch = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "init" };
        if (!string.IsNullOrWhiteSpace(defaultBranch)) { args.Add("-b"); args.Add(defaultBranch!); }
        return RunGitAsync(nameof(GitInitAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["workingDirectory"] = workingDirectory });
    }

    /// <summary>克隆仓库。</summary>
    /// <param name="url">远端地址。</param>
    /// <param name="directory">目标目录。</param>
    /// <param name="workingDirectory">执行目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git clone 克隆远端仓库。")]
    public Task<ToolResult<string>> GitCloneAsync(string url, string? directory = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "clone", url };
        if (!string.IsNullOrWhiteSpace(directory)) args.Add(directory!);
        return RunGitAsync(nameof(GitCloneAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["url"] = url, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>抓取远端。</summary>
    /// <param name="remote">远端名。</param>
    /// <param name="prune">是否清理已删除的远端分支（--prune）。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git fetch 抓取远端更新。")]
    public Task<ToolResult<string>> GitFetchAsync(string? remote = null, bool? prune = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "fetch" };
        if (!string.IsNullOrWhiteSpace(remote)) args.Add(remote!);
        if (prune == true) args.Add("--prune");
        return RunGitAsync(nameof(GitFetchAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["remote"] = remote, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>拉取。</summary>
    /// <param name="remote">远端名。</param>
    /// <param name="branch">分支名。</param>
    /// <param name="rebase">是否使用 rebase（--rebase）。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git pull 拉取远端更新。冲突时返回需人工解决的文件清单。")]
    public Task<ToolResult<string>> GitPullAsync(string? remote = null, string? branch = null, bool? rebase = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "pull" };
        if (rebase == true) args.Add("--rebase");
        if (!string.IsNullOrWhiteSpace(remote)) args.Add(remote!);
        if (!string.IsNullOrWhiteSpace(branch)) args.Add(branch!);
        return RunGitAsync(nameof(GitPullAsync), args, workingDirectory, timeoutMs, true,
            new Dictionary<string, object?> { ["remote"] = remote, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>推送。</summary>
    /// <param name="remote">远端名。</param>
    /// <param name="branch">分支名。</param>
    /// <param name="setUpstream">是否设置上游（-u）。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git push 推送。setUpstream=true 时设置上游（-u）。")]
    public Task<ToolResult<string>> GitPushAsync(string? remote = null, string? branch = null, bool? setUpstream = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "push" };
        if (setUpstream == true) args.Add("-u");
        if (!string.IsNullOrWhiteSpace(remote)) args.Add(remote!);
        if (!string.IsNullOrWhiteSpace(branch)) args.Add(branch!);
        return RunGitAsync(nameof(GitPushAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["remote"] = remote, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>重置。</summary>
    /// <param name="mode">模式：soft/mixed/hard。</param>
    /// <param name="target">目标引用。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git reset 重置。mode 取 soft/mixed/hard，缺省 mixed。")]
    public Task<ToolResult<string>> GitResetAsync(string? mode = null, string? target = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "reset" };
        if (!string.IsNullOrWhiteSpace(mode)) args.Add("--" + mode!.Trim().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(target)) args.Add(target!);
        return RunGitAsync(nameof(GitResetAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["mode"] = mode, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>变基。</summary>
    /// <param name="upstream">上游引用。</param>
    /// <param name="interactive">是否交互式（-i）。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git rebase 变基到 upstream。冲突时返回需人工解决的文件清单。注意 -i 交互式在非交互进程下不可用。")]
    public Task<ToolResult<string>> GitRebaseAsync(string upstream, bool? interactive = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "rebase" };
        if (interactive == true) args.Add("-i");
        args.Add(upstream);
        return RunGitAsync(nameof(GitRebaseAsync), args, workingDirectory, timeoutMs, true,
            new Dictionary<string, object?> { ["upstream"] = upstream, ["workingDirectory"] = workingDirectory });
    }

    /// <summary>清理未跟踪文件。</summary>
    /// <param name="dryRun">预演（-n），缺省 true 更安全。</param>
    /// <param name="directories">是否包含目录（-d）。</param>
    /// <param name="force">是否强制（-f）。</param>
    /// <param name="workingDirectory">仓库工作目录。</param>
    /// <param name="timeoutMs">超时毫秒。</param>
    [Description("git clean 清理未跟踪文件。dryRun 缺省 true（仅预演）；真正删除需 dryRun=false 且 force=true。")]
    public Task<ToolResult<string>> GitCleanAsync(bool? dryRun = null, bool? directories = null, bool? force = null, string? workingDirectory = null, int? timeoutMs = null)
    {
        var args = new List<string> { "clean" };
        var isDryRun = dryRun ?? true;
        args.Add(isDryRun ? "-n" : "-f");
        if (directories == true) args.Add("-d");
        if (force == true && isDryRun) args.Add("-f");
        return RunGitAsync(nameof(GitCleanAsync), args, workingDirectory, timeoutMs, false,
            new Dictionary<string, object?> { ["dryRun"] = dryRun, ["workingDirectory"] = workingDirectory });
    }
}
```

- [ ] **Step 4：运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~GitToolUnitTest"`
Expected: PASS（2 passed；若环境 `git` 缺失则 `GitInit` 返回失败态，测试会失败——此时需先安装 git）。

- [ ] **Step 5：提交**

```powershell
git add luban-framework/LuBan.AIAgent/Tools/Git/ luban-framework/LuBan.XTestProject/GitToolUnitTest.cs
git commit -m "新增 Git 工具组（26 个细粒度工具）与冲突结果回传"
```

### Task C3：注册插件 + 只读默认集合

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBanAgentExtensions.cs`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Abstractions\ToolConfirmationService.cs`

- [ ] **Step 1：注册插件**

在 `LuBanAgentExtensions.AddLuBanAgent` 的插件注册区（`Tools.Web.WebToolPlugin` 之后）新增：

```csharp
        services.AddSingleton<ILuBanToolPlugin, Tools.Git.GitToolPlugin>();
```

- [ ] **Step 2：只读默认集合补 Git 只读工具名**

把 `ToolConfirmationService.DefaultReadOnlyTools` 数组改为：

```csharp
    private static readonly string[] DefaultReadOnlyTools =
    [
        "ReadFileAsync", "ListDirectoryAsync", "GetWorkspaceOverviewAsync",
        "SearchFilesAsync", "GrepAsync", "GetFileInfoAsync",
        // Git 只读工具（Plan 模式放行）
        "GitStatusAsync", "GitDiffAsync", "GitLogAsync", "GitShowAsync",
        "GitBranchListAsync", "GitRemoteListAsync", "GitBlameAsync", "GitStashListAsync",
        // MCP 资源只读工具
        "mcp_list_resources", "mcp_read_resource",
    ];
```

- [ ] **Step 3：加断言测试（确认默认集合含新工具）**

在 `GitToolUnitTest.cs` 追加：

```csharp
    [TestMethod]
    public void DefaultReadOnlyTools_ContainsGitReadOnlyTools()
    {
        var ctx = new ToolConfirmationContext();
        var svc = new ToolConfirmationService(ctx);
        Assert.IsTrue(svc.ReadOnlyTools.Contains("GitStatusAsync"));
        Assert.IsTrue(svc.ReadOnlyTools.Contains("GitDiffAsync"));
        Assert.IsFalse(svc.AlwaysConfirmTools.Contains("GitCommitAsync"), "Git 工具不得进入 AlwaysConfirmTools");
    }
```

- [ ] **Step 4：运行测试**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~GitToolUnitTest"`
Expected: PASS（3 passed）。

- [ ] **Step 5：提交**

```powershell
git add luban-framework/LuBan.AIAgent/LuBanAgentExtensions.cs luban-framework/LuBan.AIAgent/Abstractions/ToolConfirmationService.cs luban-framework/LuBan.XTestProject/GitToolUnitTest.cs
git commit -m "注册 Git 工具插件并补充只读默认集合"
```

---

## Phase D：MCP 资源工具

### Task D1：`mcp_list_resources` / `mcp_read_resource`

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\MCP\MCPToolPlugin.cs`
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\McpResourceToolUnitTest.cs`

- [ ] **Step 1：写失败测试（用 FileSystemMCPClient 的资源接口）**

创建 `McpResourceToolUnitTest.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.XTestProject
*文件名： McpResourceToolUnitTest
*创建时间：2026/10/9
*描述：验证 MCP 资源列表/读取工具
*****************************************************************************/
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.MCP;
using LuBan.AIAgent.MCP.BuiltIn;

namespace LuBan.XTestProject;

[TestClass]
public class McpResourceToolUnitTest
{
    [TestMethod]
    public async Task ListResources_ForUnsupportedClient_ReturnsEnumerable()
    {
        // FileSystemMCPClient 若不支持 resources，应返回空集合而非异常
        var client = new FileSystemMCPClient();
        var resources = await client.ListResourcesAsync();
        Assert.IsNotNull(resources);
    }
}
```

- [ ] **Step 2：运行确认（可能直接 PASS，因为接口已存在）**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~McpResourceToolUnitTest"`
Expected: PASS。这确认底层能力就绪，接下来只需在插件暴露工具。

- [ ] **Step 3：在 `MCPToolPlugin.GetTools` 暴露两个资源工具**

在 `GetTools` 内、遍历 `registry.GetAll()` 之前，先加入两个通用资源工具（若 `registry` 非空）。把 `GetTools` 方法体改为：

```csharp
    public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
    {
        var registry = sp.GetService<MCPRegistry>();
        if (registry == null) return Array.Empty<AIFunction>();

        var confirmationService = sp.GetRequiredService<IToolConfirmationService>();
        var tools = new List<AIFunction>
        {
            new McpListResourcesAIFunction(registry, confirmationService),
            new McpReadResourceAIFunction(registry, confirmationService),
        };

        foreach (var client in registry.GetAll().Where(c => c.IsConnected))
        {
            IEnumerable<MCPTool> mcpTools;
            try
            {
                mcpTools = client.ListToolsAsync().GetAwaiter().GetResult();
            }
            catch
            {
                continue;
            }

            foreach (var tool in mcpTools)
            {
                tools.Add(new MCPToolAIFunction(client, tool, confirmationService));
            }
        }
        return tools;
    }
```

- [ ] **Step 4：新增两个资源函数类（同文件内）**

在 `MCPToolPlugin.cs` 中 `MCPToolAIFunction` 类之后新增：

```csharp
    /// <summary>
    /// 列出 MCP 资源（可选按 client 过滤）。
    /// </summary>
    private sealed class McpListResourcesAIFunction : AIFunction
    {
        private readonly MCPRegistry _registry;
        private readonly IToolConfirmationService _confirmationService;

        public McpListResourcesAIFunction(MCPRegistry registry, IToolConfirmationService confirmationService)
        {
            _registry = registry;
            _confirmationService = confirmationService;
        }

        public override string Name => "mcp_list_resources";

        public override string Description => "列出已连接 MCP 服务器可用的资源（client 可选，缺省遍历全部）。";

        public override JsonElement JsonSchema => JsonDocument.Parse(
            """{"type":"object","properties":{"client":{"type":"string","description":"MCP 服务器名称，缺省全部"}}}""").RootElement.Clone();

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var args = new Dictionary<string, object?>(arguments);
            var outcome = await _confirmationService.EvaluateAsync(Name, null, args);
            if (outcome == EnumConfirmationOutcome.Planned) return ToolResult.Plan<string>();
            if (outcome != EnumConfirmationOutcome.Allowed) return ToolResult.Denied<string>();

            var clientFilter = arguments.TryGetValue("client", out var c) ? c?.ToString() : null;
            var clients = _registry.GetAll().Where(x => x.IsConnected);
            if (!string.IsNullOrWhiteSpace(clientFilter))
                clients = clients.Where(x => x.Name.Equals(clientFilter, StringComparison.OrdinalIgnoreCase));

            var list = new List<object>();
            foreach (var client in clients)
            {
                IEnumerable<MCPResource> resources;
                try { resources = await client.ListResourcesAsync(cancellationToken); }
                catch { continue; }
                foreach (var r in resources)
                    list.Add(new { client = client.Name, uri = r.Uri, name = r.Name, description = r.Description, mimeType = r.MimeType });
            }

            return list.Count == 0
                ? "当前没有可用的 MCP 资源。"
                : JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// 读取指定 MCP 资源。
    /// </summary>
    private sealed class McpReadResourceAIFunction : AIFunction
    {
        private readonly MCPRegistry _registry;
        private readonly IToolConfirmationService _confirmationService;

        public McpReadResourceAIFunction(MCPRegistry registry, IToolConfirmationService confirmationService)
        {
            _registry = registry;
            _confirmationService = confirmationService;
        }

        public override string Name => "mcp_read_resource";

        public override string Description => "读取指定 MCP 资源的完整内容（需 client 与 uri）。";

        public override JsonElement JsonSchema => JsonDocument.Parse(
            """{"type":"object","required":["client","uri"],"properties":{"client":{"type":"string"},"uri":{"type":"string"}}}""").RootElement.Clone();

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var args = new Dictionary<string, object?>(arguments);
            var outcome = await _confirmationService.EvaluateAsync(Name, null, args);
            if (outcome == EnumConfirmationOutcome.Planned) return ToolResult.Plan<string>();
            if (outcome != EnumConfirmationOutcome.Allowed) return ToolResult.Denied<string>();

            var clientName = arguments.TryGetValue("client", out var c) ? c?.ToString() : null;
            var uri = arguments.TryGetValue("uri", out var u) ? u?.ToString() : null;
            if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(uri))
                return "参数缺失：需要 client 与 uri。";

            var client = _registry.Get(clientName!);
            if (client == null) return $"未找到 MCP 服务器: {clientName}。";
            if (!client.IsConnected) return $"MCP 服务器 {clientName} 未连接。";

            try
            {
                var content = await client.ReadResourceAsync(uri!, cancellationToken);
                return content?.Content ?? "（资源内容为空）";
            }
            catch (Exception ex)
            {
                return $"读取 MCP 资源失败: {ex.Message}";
            }
        }
    }
```

（`System.Text.Json` 已在文件顶部 `using`。）

- [ ] **Step 5：构建验证**

Run: `dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj`
Expected: Build succeeded。

- [ ] **Step 6：运行测试**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~McpResourceToolUnitTest"`
Expected: PASS。

- [ ] **Step 7：提交**

```powershell
git add luban-framework/LuBan.AIAgent/MCP/MCPToolPlugin.cs luban-framework/LuBan.XTestProject/McpResourceToolUnitTest.cs
git commit -m "MCP 插件暴露资源列表与读取工具"
```

---

## Phase E：Web fetch 增强

### Task E1：`WebToolOptions.DefaultFormat` + `FetchUrlAsync` format 参数与转换链

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Configuration\WebToolOptions.cs`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Tools\Web\WebToolPlugin.cs`
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\WebFetchFormatUnitTest.cs`

- [ ] **Step 1：写失败测试**

创建 `WebFetchFormatUnitTest.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.XTestProject
*文件名： WebFetchFormatUnitTest
*创建时间：2026/10/9
*描述：验证 Web 抓取格式参数与降级链
*****************************************************************************/
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Tools.Web;

namespace LuBan.XTestProject;

[TestClass]
public class WebFetchFormatUnitTest
{
    [TestMethod]
    public void WebToolOptions_DefaultFormat_IsMarkdown()
    {
        var opts = new WebToolOptions();
        Assert.AreEqual("markdown", opts.DefaultFormat);
    }

    [TestMethod]
    public async Task FetchUrlAsync_RawFormat_ReturnsHtmlUnchanged()
    {
        var group = new WebToolGroup(new WebToolOptions { MaxCharacters = 12000 });
        try
        {
            var result = await group.FetchUrlAsync("https://example.com/", format: "raw");
            if (!result.IsSuccess) { Assert.Inconclusive("网络不可用，跳过"); return; }
            StringAssert.Contains(result.Data!, "<");
        }
        catch (Exception ex)
        {
            Assert.Inconclusive($"网络不可用，跳过：{ex.Message}");
        }
    }

    [TestMethod]
    public async Task FetchUrlAsync_MarkdownFormat_DoesNotReturnRawHtmlTags()
    {
        var group = new WebToolGroup(new WebToolOptions { MaxCharacters = 12000 });
        try
        {
            var result = await group.FetchUrlAsync("https://example.com/", format: "markdown");
            if (!result.IsSuccess) { Assert.Inconclusive("网络不可用，跳过"); return; }
            Assert.IsFalse(result.Data!.Contains("<html"), "markdown 结果不应包含原始 html 标签");
        }
        catch (Exception ex)
        {
            Assert.Inconclusive($"网络不可用，跳过：{ex.Message}");
        }
    }
}
```

- [ ] **Step 2：运行确认失败**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WebFetchFormatUnitTest"`
Expected: 编译失败（`DefaultFormat` / `format` 参数不存在）。

- [ ] **Step 3：`WebToolOptions` 增 `DefaultFormat`**

在 `WebToolOptions` 内 `MaxCharacters` 之后新增：

```csharp
    /// <summary>
    /// 默认输出格式：raw / text / markdown。默认 markdown，失败时降级 markdown→text→raw。
    /// </summary>
    public string DefaultFormat { get; set; } = "markdown";
```

- [ ] **Step 4：改写 `FetchUrlAsync` 与其转换链**

把 `WebToolGroup.FetchUrlAsync` 替换为（并新增私有辅助方法）：

```csharp
    /// <summary>
    /// 获取 URL 内容。
    /// </summary>
    /// <param name="url">目标 URL。</param>
    /// <param name="format">输出格式：raw / text / markdown；缺省取配置 DefaultFormat。</param>
    /// <returns>页面内容</returns>
    [Description("获取 URL 内容。format 可选 raw/text/markdown，缺省 markdown；仅对 HTML 内容做转换。")]
    public async Task<ToolResult<string>> FetchUrlAsync(string url, string? format = null)
    {
        if (!IsValidHttpUrl(url))
            return ToolResult.Fail<string>($"无效的 URL: {url}。仅支持 http:// 和 https:// 协议，且不允许访问内网地址。");

        var target = NormalizeFormat(format ?? _options.DefaultFormat);

        try
        {
            var uri = new Uri(url);
            var baseUri = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
            var resource = uri.PathAndQuery.TrimStart('/');

            var proxy = HttpClientProxy.Create(baseUri, timeout: 30, useLog: true);
            var (bytes, contentType) = await proxy.GetResponseAsync(resource, timeout: 30);
            var raw = Encoding.UTF8.GetString(bytes);

            var isHtml = contentType is not null &&
                (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
                 contentType.Contains("application/xhtml+xml", StringComparison.OrdinalIgnoreCase));

            var content = isHtml
                ? ConvertHtml(raw, target, baseUri.ToString())
                : raw;

            if (content.Length > _options.MaxCharacters)
                content = content.Substring(0, _options.MaxCharacters) + "\n\n[内容已截断]";

            return ToolResult.Ok(content, $"statusCode=200; contentType={contentType}; format={target}; url={url}");
        }
        catch (OperationCanceledException ex)
        {
            Logger.Error("获取 URL 异常：请求超时", ex, url);
            return ToolResult.Fail<string>($"请求超时（30 秒）: {url}\n建议: 该站点响应过慢或不可达，请尝试更换 URL 或使用搜索引擎获取信息。");
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("获取 URL 异常：HTTP 请求失败", ex, url);
            return ToolResult.Fail<string>(BuildHttpRequestErrorMessage(ex, url), ((int?)ex.StatusCode ?? 0).ToString());
        }
        catch (IOException ex)
        {
            Logger.Error("获取 URL 异常：内容读取失败", ex, url);
            return ToolResult.Fail<string>($"读取 URL 内容失败: {url}\n错误: {ex.Message}\n建议: 请尝试更换 URL 或使用搜索引擎获取信息。");
        }
        catch (Exception ex)
        {
            Logger.Error("获取 URL 异常", ex, url);
            return ToolResult.Fail<string>($"获取 URL 失败: {url}\n错误: {ex.Message}");
        }
    }

    private static string NormalizeFormat(string? format)
    {
        var value = format?.Trim().ToLowerInvariant();
        return value is "raw" or "text" or "markdown" ? value : "markdown";
    }

    private static string ConvertHtml(string html, string format, string baseUrl)
    {
        switch (format)
        {
            case "raw":
                return html;
            case "text":
                try { return html.GetTextFromHtml(); }
                catch { return html; }
            default: // markdown（含降级链 markdown→text→raw）
                try
                {
                    var md = html.HtmlToMarkdown(baseUrl);
                    if (!string.IsNullOrWhiteSpace(md)) return md;
                }
                catch { }
                try { return html.GetTextFromHtml(); }
                catch { return html; }
        }
    }
```

（`GetTextFromHtml` / `HtmlToMarkdown` 为 `LuBan.Common` 扩展方法，`namespace LuBan.Common`；`WebToolPlugin.cs` 现有 `using` 未显式引入，请确认文件顶部/全局 using 覆盖 `LuBan.Common`——框架项目 `GlobalUsing` 或隐式全局已含；若无，则加 `using LuBan.Common;`。）

- [ ] **Step 5：运行测试确认通过**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WebFetchFormatUnitTest"`
Expected: PASS（或 Inconclusive，无网络时；不得 Failed）。

- [ ] **Step 6：提交**

```powershell
git add luban-framework/LuBan.AIAgent/Configuration/WebToolOptions.cs luban-framework/LuBan.AIAgent/Tools/Web/WebToolPlugin.cs luban-framework/LuBan.XTestProject/WebFetchFormatUnitTest.cs
git commit -m "Web 抓取支持 markdown/text/raw 格式与降级链"
```

---

## Phase F：Agent 侧展示名、配置、文档与清理

### Task F1：`ToolDisplayNames` 补条目

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore\Utils\ToolDisplayNames.cs`

- [ ] **Step 1：动作名映射增条目**

在 `Map` 字典的 `// FileSystem` 段与末尾 `// Context` 段之间追加：

```csharp
        // FileSystem（Edit）
        ["EditFileAsync"] = "编辑文件",
        ["MultiEditFileAsync"] = "批量编辑",
        // Git
        ["GitStatusAsync"] = "Git 状态",
        ["GitDiffAsync"] = "Git 差异",
        ["GitLogAsync"] = "Git 日志",
        ["GitShowAsync"] = "Git 查看",
        ["GitBranchListAsync"] = "列出分支",
        ["GitRemoteListAsync"] = "列出远端",
        ["GitBlameAsync"] = "Git 追溯",
        ["GitStashListAsync"] = "列出暂存",
        ["GitAddAsync"] = "Git 暂存",
        ["GitCommitAsync"] = "Git 提交",
        ["GitCheckoutAsync"] = "Git 检出",
        ["GitSwitchAsync"] = "Git 切换",
        ["GitBranchCreateAsync"] = "创建分支",
        ["GitCherryPickAsync"] = "Git 拣选",
        ["GitRevertAsync"] = "Git 回滚",
        ["GitRestoreAsync"] = "Git 恢复",
        ["GitMergeAsync"] = "Git 合并",
        ["GitStashAsync"] = "Git 暂存",
        ["GitInitAsync"] = "Git 初始化",
        ["GitCloneAsync"] = "Git 克隆",
        ["GitFetchAsync"] = "Git 拉取远端",
        ["GitPullAsync"] = "Git 拉取",
        ["GitPushAsync"] = "Git 推送",
        ["GitResetAsync"] = "Git 重置",
        ["GitRebaseAsync"] = "Git 变基",
        ["GitCleanAsync"] = "Git 清理",
        // MCP 资源
        ["mcp_list_resources"] = "列出 MCP 资源",
        ["mcp_read_resource"] = "读取 MCP 资源",
```

- [ ] **Step 2：图标映射增条目**

在 `IconMap` 字典的 `// FileSystem` 段与末尾 `// Context` 段之间追加：

```csharp
        // FileSystem（Edit）
        ["EditFileAsync"] = "✏️",
        ["MultiEditFileAsync"] = "🛠️",
        // Git
        ["GitStatusAsync"] = "🌿",
        ["GitDiffAsync"] = "🌿",
        ["GitLogAsync"] = "🌿",
        ["GitShowAsync"] = "🌿",
        ["GitBranchListAsync"] = "🌿",
        ["GitRemoteListAsync"] = "🌿",
        ["GitBlameAsync"] = "🌿",
        ["GitStashListAsync"] = "🌿",
        ["GitAddAsync"] = "🌿",
        ["GitCommitAsync"] = "🌿",
        ["GitCheckoutAsync"] = "🌿",
        ["GitSwitchAsync"] = "🌿",
        ["GitBranchCreateAsync"] = "🌿",
        ["GitCherryPickAsync"] = "🌿",
        ["GitRevertAsync"] = "🌿",
        ["GitRestoreAsync"] = "🌿",
        ["GitMergeAsync"] = "🌿",
        ["GitStashAsync"] = "🌿",
        ["GitInitAsync"] = "🌿",
        ["GitCloneAsync"] = "🌿",
        ["GitFetchAsync"] = "🌿",
        ["GitPullAsync"] = "🌿",
        ["GitPushAsync"] = "🌿",
        ["GitResetAsync"] = "🌿",
        ["GitRebaseAsync"] = "🌿",
        ["GitCleanAsync"] = "🌿",
        // MCP 资源
        ["mcp_list_resources"] = "📦",
        ["mcp_read_resource"] = "📦",
```

- [ ] **Step 2b：把 `EditFileAsync`/`MultiEditFileAsync` 加到 `Map` 后回退安全（无需改代码）**：`GetAction` 已对未知名去 `Async` 回退，无需额外处理。

- [ ] **Step 3：构建验证**

Run: `dotnet build luban-agent/luban-agent.slnx`
Expected: Build succeeded。这一步同时会把 framework 的最新 nupkg 拉取/或本地 project 引用一并编译（视 SDK 解析）。若 agent 仍引用旧 NuGet 包，本阶段构建只验证 agent 自身代码；工具是否生效需在 framework 打包发布后手动冒烟。

- [ ] **Step 4：提交（进入 agent 子仓库）**

```powershell
git add LubanAgentCore/Utils/ToolDisplayNames.cs
git commit -m "feat: 补全 Edit/Git/MCP 资源工具展示名与图标"
```

（说明：agent 仓库提交需 `workdir=luban-agent`，路径为仓库内相对路径。）

### Task F2：`appsettings.json` 补 `Tools:Git`（可选，保持默认启用）

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\appsettings.json`
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCodex\appsettings.json`

- [ ] **Step 1：在 `LuBanAgent.Tools` 下显式声明（便于用户发现）**

由于当前两份 `appsettings.json` 未显式写 `Tools` 节点，默认值即 `Enabled=true`。为避免“找不到开关”，在 `"Confirmation"` 之前新增：

```json
    "Tools": {
      "Git": {
        "Enabled": true,
        "DefaultTimeout": 120000
      }
    },
```

两份文件都加。注意 JSON 中 `"Tools"` 与已有键同级、逗号正确。

- [ ] **Step 2：验证 JSON 合法**

Run: `Get-Content -Raw luban-agent/LubanAgentCli/appsettings.json | ConvertFrom-Json | Out-Null; Write-Output OK`
Expected: OK（无异常）。对 Codex 同样执行。

- [ ] **Step 3：提交**

```powershell
git add LubanAgentCli/appsettings.json LubanAgentCodex/appsettings.json
git commit -m "feat: 显式声明 Git 工具组开关"
```

### Task F3：中英 README 同步

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\README.md` + `README.en.md`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.Common\README.md` + `README.en.md`
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\README.md` + `README.en.md`
- Modify: `D:\WorkBench\Walle\luban\luban-agent\LubanAgentCodex\README.md` + `README.en.md`

- [ ] **Step 1：`LuBan.AIAgent/README.md` 内置工具表加 `git` 行**

在“内置工具”表（约 line 116 脚本行之后）新增：

```markdown
| **Git 工具** | `git` | 状态/差异/日志/分支/暂存/提交/合并/变基/推送等 26 个细粒度工具；工作区内免确认，越界逐次确认；冲突时返回文件清单交回 agent 处理 |
```

并把“内置工具”表下方描述里的“10 大内置工具组”（约 line 823）改为：

```markdown
- **11 大内置工具组**覆盖浏览器自动化、文件操作、脚本执行、Git 版本控制、Web 请求、语义检索、上下文压缩、本地记忆、MCP、多 Agent 编排、LLM Wiki 知识库（opt-in）
```

- [ ] **Step 2：`LuBan.AIAgent/README.md` 目录树加 `Tools/Git/` 与 `EditOperation.cs`**

在目录树 `Tools/` 段（约 line 704 `Script/` 之后）新增：

```markdown
│   ├── Git/                           # Git 版本控制工具
│   │   └── GitToolPlugin.cs           # Git 工具插件与工具组
```

并在 `Configuration/` 段（约 line 687 `ScriptToolOptions.cs` 之后）新增：

```markdown
│   ├── GitToolOptions.cs              # Git 工具选项
```

在 `FileSystem/FileSystemToolPlugin.cs` 行下补 `EditOperation.cs` 说明（可合并为一行的注释）。

- [ ] **Step 3：`LuBan.AIAgent/README.en.md` 同步**

在英文“Built-in Tools”表加 Git 行；工具组总数文案同步为 “11 built-in tool groups”；目录树同步加 `Tools/Git/GitToolPlugin.cs` 与 `GitToolOptions.cs`。

- [ ] **Step 4：`LuBan.Common/README.md` 与 `README.en.md`**

在能力清单新增：`MarkdownUtil.HtmlToMarkdown`（HTML→Markdown）、`HttpClientProxy.GetResponseAsync`（返回字节 + ContentType）。中文与英文各一段。

- [ ] **Step 5：agent README ×4**

在 CLI/Codex 各自 README 的工具表加 Git 行与 Edit 能力说明，并标注“Git 工具默认不进编排子代理（`Orchestration.DefaultToolGroups=["filesystem"]`）”。中英各改一份（共 4 个文件）。参考 Codex `README.md:53-62` 的表格样式。

- [ ] **Step 6：提交**

```powershell
# framework 仓
git add LuBan.AIAgent/README.md LuBan.AIAgent/README.en.md LuBan.Common/README.md LuBan.Common/README.en.md
git commit -m "文档：补充 Git 工具组、Edit、Markdown/GetResponseAsync 能力说明"
```

```powershell
# agent 仓（workdir=luban-agent）
git add LubanAgentCli/README.md LubanAgentCli/README.en.md LubanAgentCodex/README.md LubanAgentCodex/README.en.md
git commit -m "docs: 补充 Git/Edit 工具说明"
```

### Task F4：删除历史空目录

**Files:**
- Delete: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Tools\Database\`
- Delete: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Tools\Redis\`

- [ ] **Step 1：确认仍为空**

Run: `Get-ChildItem -LiteralPath "luban-framework/LuBan.AIAgent/Tools/Database","luban-framework/LuBan.AIAgent/Tools/Redis" -Force -Recurse | Measure-Object | Select-Object -ExpandProperty Count`
Expected: `0`。若非 0，**停止**并向用户报告（可能存在未预期文件）。

- [ ] **Step 2：删除**

```powershell
Remove-Item -LiteralPath "luban-framework/LuBan.AIAgent/Tools/Database" -Recurse -Force
Remove-Item -LiteralPath "luban-framework/LuBan.AIAgent/Tools/Redis" -Recurse -Force
```

（空目录 git 不跟踪，通常无需提交；若其中曾有空 `.gitkeep` 之类已被跟踪文件，则一并 `git rm`。）

- [ ] **Step 3：确认目录消失**

Run: `Get-ChildItem -LiteralPath "luban-framework/LuBan.AIAgent/Tools" -Directory | Select-Object -ExpandProperty Name`
Expected: 列表不再含 `Database`、`Redis`。

### Task F5：全量构建与测试收口

- [ ] **Step 1：构建 framework 解决方案**

Run: `dotnet build luban-framework/LuBan.FrameWork.sln`
Expected: Build succeeded。

- [ ] **Step 2：构建 agent 解决方案**

Run: `dotnet build luban-agent/luban-agent.slnx`
Expected: Build succeeded。

- [ ] **Step 3：跑本计划新增的全部单测**

Run: `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~EditToolUnitTest|FullyQualifiedName~GitToolUnitTest|FullyQualifiedName~McpResourceToolUnitTest|FullyQualifiedName~WebFetchFormatUnitTest|FullyQualifiedName~MarkdownUtilUnitTest|FullyQualifiedName~HttpClientProxyResponseUnitTest|FullyQualifiedName~AIFunctionArrayBindingProbeUnitTest"`
Expected: 全部 PASS（网络相关用例在无网络时为 Inconclusive，可接受）。

- [ ] **Step 4：手动冒烟（CLI，参考 `LubanAgentCli/docs/TUI-SMOKE-TEST.md`）**

在 framework 重新打包/本地引用生效后，于 CLI 依次验证：
1. Edit：让 agent 对工作区内某文件做一处替换、多处替换与 `replace_all`。
2. Git：`git status` / `git diff` / 暂存 + 提交。
3. 冲突：构造一次 merge 冲突，确认返回“需人工解决”清单且未自动 `--continue`。
4. MCP：`mcp_list_resources` + `mcp_read_resource`。
5. Web：抓取 `https://example.com` 返回 markdown。

- [ ] **Step 5：最终提交（如有零散改动）**

```powershell
git status
# 按需补交
```

---

## Self-Review 记录

- **Spec 覆盖**：Edit/MultiEdit（Phase B）✓；Git 26 工具 + 冲突回传 + 只读集合 + 配置 + 注册（Phase C）✓；MCP 资源 ×2（Phase D）✓；Web markdown 降级链 + `GetResponseAsync`（Phase A2 + E1）✓；删空目录（F4）✓；注释与中英 README（F3）✓；ToolDisplayNames（F1）✓；appsettings（F2）✓。
- **占位符扫描**：无 TBD/TODO；所有代码步骤均给出完整代码；Git 26 工具均给出完整方法体。
- **类型一致性**：`EditOperation`（Phase B1）→ `MultiEditFileAsync(IReadOnlyList<EditOperation>)`（B4）一致；`GitToolOptions`（C1）→ `GitToolGroup` ctor（C2）一致；`GetResponseAsync` 返回 `(byte[] Bytes, string? ContentType)`（A2）→ `FetchUrlAsync` 解构一致；`MCPResource`/`MCPResourceContent` 字段与 `IMCPClient` 一致。
- **已知前置探针**：Task B0 为 MultiEdit 的 Go/No-Go；失败则跳过 B4 并按计划回退（单次 Edit）。
- **构建依赖**：Phase C/E 的 agent 冒烟需 framework 打包发布，计划已标注。