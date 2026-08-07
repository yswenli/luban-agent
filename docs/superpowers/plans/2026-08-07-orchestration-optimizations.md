# 编排子系统后续优化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现编排子系统 4 项优化：启发式预过滤、模板规划器工作区加载、多模型路由、角色扩展。

**Architecture:** 框架层（LuBan.AIAgent）新增 `HeuristicFilterOptions` 配置类（含可测试的 `ShouldSkipPlanning` 判定方法）、`TaskGraphTemplate.FromJson` + `TemplateTaskPlanner.LoadFromWorkspace`、 `SubAgentRoleRegistry.LoadFromWorkspace`；多模型路由通过给 `LuBanAgentFactory` / `LlmTaskPlanner` 增加**可选** `IProviderRouter` 参数实现（additive，非替换）。CLI 层（luban-agent）在 AgiCommand 接入预过滤与工作区加载。

**Tech Stack:** .NET 8, Microsoft.Extensions.AI, MSTest（测试项目 ImplicitUsings + MSTest 全局 using 已启用，无 Moq，用 MockChatClient + 真 DI）。

**Spec:** `docs/superpowers/specs/2026-08-07-orchestration-optimizations-design.md`

**对 spec 的一处实现调整（已论证）：** spec 写"LuBanAgentFactory 注入 IProviderRouter **替代** IChatClient"。本计划改为**保留 IChatClient、追加可选 `IProviderRouter? providerRouter = null` 参数**。理由：(1) 框架 `AddLuBanAgent(configuration, chatClientFactory)` 重载只注册 IChatClient，替换会破坏所有现有消费方与约 15 处测试构造点；(2) CLI 的 `LuBanChatClient.CreateChatClient(null)` 硬编码默认 provider 为 "openai"，未配置 openai 时直接抛异常——可选参数方案在未指定 modelName 时继续走注入的 IChatClient，完全避开该地雷；(3) 内置 DI 容器对带默认值的可选构造函数参数支持良好（未注册时使用默认值），CLI 已注册 IProviderRouter 单例会自动注入。路由行为与 spec 目标一致：指定 modelName 时走路由，失败回退注入客户端并记警告。

**关键事实（执行者需知）：**
- 框架仓库：`D:\WorkBench\Walle\luban\luban-framework`；CLI 仓库：`D:\WorkBench\Walle\luban\luban-agent`（两个独立 git 仓库）。
- 框架 `GlobalUsings.cs` 已含 `System.Text.Json`、`Microsoft.Extensions.DependencyInjection` 等；`Logger` 类在 `namespace System`（LuBan.Common\LogCom\Logger.cs），全框架可直接用 `Logger.Info(string)` / `Logger.Warn(string, Exception?)`。
- `IProviderRouter` 接口在 `LuBan.AIAgent/Configuration/IProviderRouter.cs`：`IChatClient CreateChatClient(string? providerModel = null)` + `IReadOnlyList<ProviderInfo> GetAvailableProviders()`；`ProviderInfo` 是 record `(string Name, string DisplayName, string[] Models)`。
- `TaskGraphTemplate` 已有 `private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };`（当前未被使用，FromJson 直接用它）。
- `TemplateTaskPlanner._templates` 是 `private readonly List<TaskGraphTemplate>`，可 Add。
- AgiCommand 中 `Logger.Warn("...", ex)` 已有使用先例（AgiCommand.cs:346）。
- ModelName 数据流已通：`TaskNode.ModelName` → `DagScheduler` → `SubAgentSpec.ModelName` → `SubAgentFactory.CreateAsync` → `LuBanAgentFactory.CreateSubAgentAsync(modelName)`，仅工厂内部未使用，本次接通。
- 测试 filter 语法：`dotnet test --filter "FullyQualifiedName~类名"`。

---

## File Structure

### 框架层（luban-framework/LuBan.AIAgent）

| 文件 | 变更 | 职责 |
|------|------|------|
| `Configuration/HeuristicFilterOptions.cs` | 新增 | 预过滤配置 + `ShouldSkipPlanning` 判定 |
| `Configuration/OrchestrationOptions.cs` | 修改 | 增加 `HeuristicFilter` 属性 |
| `Orchestration/Planner/TaskGraphTemplate.cs` | 修改 | 增加 `FromJson` 静态方法（支持 spec 文件格式 name/keywords/graph） |
| `Orchestration/Planner/TemplateTaskPlanner.cs` | 修改 | 增加 `LoadFromWorkspace(workspaceRoot)` |
| `Orchestration/SubAgentRoleRegistry.cs` | 修改 | 增加 `LoadFromWorkspace(workspaceRoot)` |
| `Orchestration/Models/TaskNode.cs` | 修改 | 更新 ModelName 注释（删除"尚未实现"说明） |
| `LuBanAgentFactory.cs` | 修改 | 可选 IProviderRouter + `ResolveChatClient` |
| `Orchestration/Planner/LlmTaskPlanner.cs` | 修改 | 可选 IProviderRouter + PlannerModel 路由 |
| `LuBanAgentExtensions.cs` | 修改 | 更新 LlmTaskPlanner 注册注释 |

### CLI 层（luban-agent）

| 文件 | 变更 | 职责 |
|------|------|------|
| `Commands/AgiCommand.cs` | 修改 | 预过滤条件 + 工作区模板/角色加载 |
| `appsettings.json` | 修改 | Orchestration 下增加 HeuristicFilter |

### 测试（luban-framework/LuBan.AIAgent.Tests）

| 文件 | 变更 | 职责 |
|------|------|------|
| `Orchestration/HeuristicFilterTests.cs` | 新增 | ShouldSkipPlanning 逻辑 |
| `Orchestration/TemplateTaskPlannerTests.cs` | 修改 | LoadFromWorkspace 测试 |
| `Orchestration/SubAgentRoleRegistryTests.cs` | 修改 | LoadFromWorkspace 测试 |
| `Orchestration/ModelRoutingTests.cs` | 新增 | 工厂/LlmTaskPlanner 路由测试 |
| `AIAgentTests.cs` | 修改 | 追加 MockProviderRouter 辅助类 |

---

### Task 1: HeuristicFilterOptions 配置类（框架）

**Files:**
- Create: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Configuration\HeuristicFilterOptions.cs`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Configuration\OrchestrationOptions.cs`
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\Orchestration\HeuristicFilterTests.cs`（新增）

- [ ] **Step 1: Write the failing test**

新建 `LuBan.AIAgent.Tests\Orchestration\HeuristicFilterTests.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LuBan.AIAgent.Tests.Orchestration
*文件名： HeuristicFilterTests
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/7
*描述：HeuristicFilterOptions 单元测试
*
*****************************************************************************/
using LuBan.AIAgent.Configuration;

namespace LuBan.AIAgent.Tests.Orchestration;

[TestClass]
public class HeuristicFilterTests
{
    [TestMethod]
    public void TestShouldSkipPlanning_短输入无关键词_跳过()
    {
        var filter = new HeuristicFilterOptions();
        Assert.IsTrue(filter.ShouldSkipPlanning("你好"));
    }

    [TestMethod]
    public void TestShouldSkipPlanning_短输入含关键词_不跳过()
    {
        var filter = new HeuristicFilterOptions();
        Assert.IsFalse(filter.ShouldSkipPlanning("搜索并总结"));
    }

    [TestMethod]
    public void TestShouldSkipPlanning_长输入_不跳过()
    {
        var filter = new HeuristicFilterOptions { MaxLength = 20 };
        Assert.IsFalse(filter.ShouldSkipPlanning("这是一个长度明显超过二十个字符的用户输入内容"));
    }

    [TestMethod]
    public void TestShouldSkipPlanning_禁用时不跳过()
    {
        var filter = new HeuristicFilterOptions { Enabled = false };
        Assert.IsFalse(filter.ShouldSkipPlanning("你好"));
    }

    [TestMethod]
    public void TestShouldSkipPlanning_空输入不跳过()
    {
        var filter = new HeuristicFilterOptions();
        Assert.IsFalse(filter.ShouldSkipPlanning(""));
    }

    [TestMethod]
    public void TestOrchestrationOptions_包含HeuristicFilter默认值()
    {
        var opts = new OrchestrationOptions();
        Assert.IsNotNull(opts.HeuristicFilter);
        Assert.IsTrue(opts.HeuristicFilter.Enabled);
        Assert.AreEqual(20, opts.HeuristicFilter.MaxLength);
        Assert.IsTrue(opts.HeuristicFilter.Keywords.Count > 0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj" --filter "FullyQualifiedName~HeuristicFilterTests"`
Expected: 编译失败，`HeuristicFilterOptions` 类型不存在。

- [ ] **Step 3: Write minimal implementation**

新建 `LuBan.AIAgent\Configuration\HeuristicFilterOptions.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LuBan.AIAgent.Configuration
*文件名： HeuristicFilterOptions
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/7
*描述：启发式预过滤配置
*
*****************************************************************************/
namespace LuBan.AIAgent.Configuration;

/// <summary>
/// 启发式预过滤配置。对短输入且无复合任务关键词的输入跳过 planner，节省 LLM 调用。
/// </summary>
public class HeuristicFilterOptions
{
    /// <summary>
    /// 获取或设置是否启用启发式预过滤。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取或设置短输入阈值（字符数）。输入长度小于该值才可能被过滤。
    /// </summary>
    public int MaxLength { get; set; } = 20;

    /// <summary>
    /// 获取或设置复合任务关键词列表。短输入包含任一关键词时不跳过 planner。
    /// </summary>
    public List<string> Keywords { get; set; } = new() { "和", "同时", "然后", "并且", "另外", "还有", "分析并", "搜索并" };

    /// <summary>
    /// 判定是否应跳过 planner（直接走主 Agent 对话）。
    /// </summary>
    /// <param name="input">用户原始输入。</param>
    /// <returns>true 表示跳过 planner。</returns>
    public bool ShouldSkipPlanning(string input)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(input))
            return false;
        if (input.Length >= MaxLength)
            return false;
        return !Keywords.Any(kw => input.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }
}
```

修改 `OrchestrationOptions.cs`，在 `ReflectionTimeoutSeconds` 属性后追加：

```csharp
    /// <summary>
    /// 获取或设置启发式预过滤配置。
    /// </summary>
    public HeuristicFilterOptions HeuristicFilter { get; set; } = new();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj" --filter "FullyQualifiedName~HeuristicFilterTests"`
Expected: 6 个测试全部 PASS。

- [ ] **Step 5: Commit**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A
git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "feat: add HeuristicFilterOptions with ShouldSkipPlanning"
```

---

### Task 2: AgiCommand 启发式预过滤 + appsettings.json（CLI）

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\Commands\AgiCommand.cs`（约 283-294 行）
- Modify: `D:\WorkBench\Walle\luban\luban-agent\appsettings.json`（Orchestration 节）

- [ ] **Step 1: 修改 AgiCommand.cs**

在 `var isRagWorkspace = workspace.Type == "Rag";` 行之后插入一行：

```csharp
            // 启发式预过滤：短输入且无复合关键词时跳过 planner，节省一次 LLM 调用
            var skipByHeuristic = orchestrationOptions?.HeuristicFilter?.ShouldSkipPlanning(input) ?? false;
```

将 `if (autoDetectEnabled && !isRagWorkspace)` 改为：

```csharp
            if (autoDetectEnabled && !isRagWorkspace && !skipByHeuristic)
```

注意：判定使用原始 `input` 而非 RAG 注入后的 `finalInput`（注入内容会撑大长度导致过滤失效；普通工作区两者相同，此处显式用语义正确的变量）。

- [ ] **Step 2: 修改 appsettings.json**

在 `"Orchestration"` 节内 `"DefaultNodeTimeoutSeconds": 120` 之后追加：

```json
      "DefaultNodeTimeoutSeconds": 120,
      "HeuristicFilter": {
        "Enabled": true,
        "MaxLength": 20,
        "Keywords": [ "和", "同时", "然后", "并且", "另外", "还有", "分析并", "搜索并" ]
      }
```

- [ ] **Step 3: 验证构建**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli.csproj"`
Expected: 0 错误。（预过滤逻辑本身已由 Task 1 单测覆盖，CLI 交互循环无单测。）

- [ ] **Step 4: Commit**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-agent" add -A
git -C "D:\WorkBench\Walle\luban\luban-agent" commit -m "feat: add heuristic pre-filter to skip planner for short inputs"
```

---

### Task 3: TaskGraphTemplate.FromJson + TemplateTaskPlanner.LoadFromWorkspace（框架）

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Orchestration\Planner\TaskGraphTemplate.cs`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Orchestration\Planner\TemplateTaskPlanner.cs`
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\Orchestration\TemplateTaskPlannerTests.cs`

- [ ] **Step 1: Write the failing tests**

在 `TemplateTaskPlannerTests.cs` 类末尾追加（文件已有 using，无需新增；System.IO 由 ImplicitUsings 提供）：

```csharp
    private static string CreateTempWorkspace()
    {
        var dir = Path.Combine(Path.GetTempPath(), "luban-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public async Task TestLoadFromWorkspace_加载模板并命中()
    {
        var workspace = CreateTempWorkspace();
        try
        {
            var plansDir = Path.Combine(workspace, ".luban-agent", "plans");
            Directory.CreateDirectory(plansDir);
            File.WriteAllText(Path.Combine(plansDir, "code-review.json"), """
            {
              "name": "code-review",
              "keywords": ["代码审查", "code review"],
              "graph": {
                "nodes": [
                  { "id": "analyze", "description": "分析代码", "prompt": "分析代码结构", "role": "analyst", "toolGroups": ["filesystem"], "dependencies": [], "isCritical": true },
                  { "id": "review", "description": "审查意见", "prompt": "基于 {dep:analyze} 给出审查意见", "role": "coder", "toolGroups": ["filesystem"], "dependencies": ["analyze"], "isCritical": false }
                ]
              }
            }
            """);

            var planner = new TemplateTaskPlanner(Array.Empty<TaskGraphTemplate>());
            var loaded = planner.LoadFromWorkspace(workspace);

            Assert.AreEqual(1, loaded);
            var graph = await planner.PlanAsync("请做一次代码审查");
            Assert.IsNotNull(graph);
            Assert.AreEqual(2, graph!.Nodes.Count);
            Assert.AreEqual("analyst", graph.Nodes[0].Role);
        }
        finally { Directory.Delete(workspace, true); }
    }

    [TestMethod]
    public void TestLoadFromWorkspace_目录不存在返回0()
    {
        var planner = new TemplateTaskPlanner(Array.Empty<TaskGraphTemplate>());
        var loaded = planner.LoadFromWorkspace(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.AreEqual(0, loaded);
    }

    [TestMethod]
    public void TestLoadFromWorkspace_无效JSON被容忍()
    {
        var workspace = CreateTempWorkspace();
        try
        {
            var plansDir = Path.Combine(workspace, ".luban-agent", "plans");
            Directory.CreateDirectory(plansDir);
            File.WriteAllText(Path.Combine(plansDir, "bad.json"), "{ not valid json !!!");

            var planner = new TemplateTaskPlanner(Array.Empty<TaskGraphTemplate>());
            var loaded = planner.LoadFromWorkspace(workspace);
            Assert.AreEqual(0, loaded);
        }
        finally { Directory.Delete(workspace, true); }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj" --filter "FullyQualifiedName~TemplateTaskPlannerTests"`
Expected: 编译失败，`LoadFromWorkspace` 方法不存在。

- [ ] **Step 3: Write minimal implementation**

在 `TaskGraphTemplate.cs` 的 `Instantiate` 方法之后追加静态方法（复用类内已有的 `JsonOpts`）：

```csharp
    /// <summary>
    /// 从工作区模板 JSON 文本解析模板。
    /// 文件格式：{ "name": "...", "description": "...", "keywords": [...], "graph": { "nodes": [...] } }。
    /// </summary>
    /// <param name="json">模板 JSON 文本。</param>
    /// <returns>解析成功的模板；缺少 name 时返回 null。JSON 语法错误时抛出 JsonException。</returns>
    [RequiresUnreferencedCode("模板 JSON 反序列化依赖反射")]
    public static TaskGraphTemplate? FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var template = new TaskGraphTemplate
        {
            Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            Description = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
            Keywords = root.TryGetProperty("keywords", out var k) && k.ValueKind == JsonValueKind.Array
                ? k.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                : Array.Empty<string>()
        };
        template.Id = template.Name;

        if (string.IsNullOrWhiteSpace(template.Name))
            return null;

        if (root.TryGetProperty("graph", out var g) && g.ValueKind == JsonValueKind.Object)
        {
            var graph = JsonSerializer.Deserialize<TaskGraph>(g.GetRawText(), JsonOpts);
            if (graph != null)
                template.Prototype = graph;
        }

        return template;
    }
```

`JsonOpts` 已存在于该类（`PropertyNameCaseInsensitive = true`），TaskGraph/TaskNode 的 PascalCase 属性能匹配文件中的 camelCase 键。`RequiresUnreferencedCode` 需要的 `System.Diagnostics.CodeAnalysis` 已在 GlobalUsings。

在 `TemplateTaskPlanner.cs` 的 `PlanAsync` 方法之后追加：

```csharp
    /// <summary>
    /// 从工作区 `.luban-agent/plans/*.json` 加载任务模板。单个文件失败不影响其他文件。
    /// </summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <returns>成功加载的模板数量。</returns>
    [RequiresUnreferencedCode("模板 JSON 反序列化依赖反射")]
    public int LoadFromWorkspace(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return 0;

        var dir = Path.Combine(workspaceRoot, ".luban-agent", "plans");
        if (!Directory.Exists(dir))
            return 0;

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var template = TaskGraphTemplate.FromJson(File.ReadAllText(file));
                if (template == null)
                {
                    Logger.Warn($"任务模板文件缺少 name，已跳过: {file}");
                    continue;
                }
                _templates.Add(template);
                count++;
            }
            catch (Exception ex)
            {
                Logger.Warn($"加载任务模板失败: {file}", ex);
            }
        }

        if (count > 0)
            Logger.Info($"已从工作区加载 {count} 个任务模板 ({dir})");
        return count;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj" --filter "FullyQualifiedName~TemplateTaskPlannerTests"`
Expected: 全部 PASS（原有 5 个 + 新增 3 个）。

- [ ] **Step 5: Commit**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A
git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "feat: add TaskGraphTemplate.FromJson and TemplateTaskPlanner.LoadFromWorkspace"
```

---

### Task 4: SubAgentRoleRegistry.LoadFromWorkspace（框架）

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Orchestration\SubAgentRoleRegistry.cs`
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\Orchestration\SubAgentRoleRegistryTests.cs`

- [ ] **Step 1: Write the failing tests**

在 `SubAgentRoleRegistryTests.cs` 类末尾追加：

```csharp
    private static string CreateTempWorkspace()
    {
        var dir = Path.Combine(Path.GetTempPath(), "luban-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void TestLoadFromWorkspace_加载自定义角色()
    {
        var workspace = CreateTempWorkspace();
        try
        {
            var rolesDir = Path.Combine(workspace, ".luban-agent", "roles");
            Directory.CreateDirectory(rolesDir);
            File.WriteAllText(Path.Combine(rolesDir, "security-expert.json"), """
            {
              "name": "security-expert",
              "systemPromptTemplate": "You are a security expert. Task: {prompt}",
              "defaultToolGroups": ["filesystem", "script"]
            }
            """);

            var registry = new SubAgentRoleRegistry();
            var loaded = registry.LoadFromWorkspace(workspace);

            Assert.AreEqual(1, loaded);
            var role = registry.GetRole("security-expert");
            Assert.IsNotNull(role);
            Assert.AreEqual(2, role!.DefaultToolGroups.Count);
            Assert.AreEqual(5, registry.GetAllRoles().Count);
        }
        finally { Directory.Delete(workspace, true); }
    }

    [TestMethod]
    public void TestLoadFromWorkspace_自定义角色覆盖内置角色()
    {
        var workspace = CreateTempWorkspace();
        try
        {
            var rolesDir = Path.Combine(workspace, ".luban-agent", "roles");
            Directory.CreateDirectory(rolesDir);
            File.WriteAllText(Path.Combine(rolesDir, "coder.json"), """
            {
              "name": "coder",
              "systemPromptTemplate": "Custom coder. Task: {prompt}",
              "defaultToolGroups": ["filesystem"]
            }
            """);

            var registry = new SubAgentRoleRegistry();
            registry.LoadFromWorkspace(workspace);

            var role = registry.GetRole("coder");
            Assert.IsNotNull(role);
            Assert.IsTrue(role!.SystemPromptTemplate.StartsWith("Custom coder"));
            Assert.AreEqual(4, registry.GetAllRoles().Count);
        }
        finally { Directory.Delete(workspace, true); }
    }

    [TestMethod]
    public void TestLoadFromWorkspace_目录不存在返回0()
    {
        var registry = new SubAgentRoleRegistry();
        Assert.AreEqual(0, registry.LoadFromWorkspace(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [TestMethod]
    public void TestLoadFromWorkspace_无效文件被容忍()
    {
        var workspace = CreateTempWorkspace();
        try
        {
            var rolesDir = Path.Combine(workspace, ".luban-agent", "roles");
            Directory.CreateDirectory(rolesDir);
            File.WriteAllText(Path.Combine(rolesDir, "bad.json"), "not json");
            File.WriteAllText(Path.Combine(rolesDir, "noname.json"), """{ "systemPromptTemplate": "x {prompt}" }""");

            var registry = new SubAgentRoleRegistry();
            Assert.AreEqual(0, registry.LoadFromWorkspace(workspace));
        }
        finally { Directory.Delete(workspace, true); }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj" --filter "FullyQualifiedName~SubAgentRoleRegistryTests"`
Expected: 编译失败，`LoadFromWorkspace` 方法不存在。

- [ ] **Step 3: Write minimal implementation**

在 `SubAgentRoleRegistry.cs` 的 `GetAllRoles` 方法之后追加：

```csharp
    /// <summary>
    /// 从工作区 `.luban-agent/roles/*.json` 加载自定义角色。同名角色覆盖内置角色。单个文件失败不影响其他文件。
    /// </summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <returns>成功加载的角色数量。</returns>
    [RequiresUnreferencedCode("角色 JSON 反序列化依赖反射")]
    public int LoadFromWorkspace(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return 0;

        var dir = Path.Combine(workspaceRoot, ".luban-agent", "roles");
        if (!Directory.Exists(dir))
            return 0;

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var role = JsonSerializer.Deserialize<SubAgentRole>(File.ReadAllText(file), opts);
                if (role == null || string.IsNullOrWhiteSpace(role.Name))
                {
                    Logger.Warn($"角色文件无效（缺少 name），已跳过: {file}");
                    continue;
                }
                if (_roles.ContainsKey(role.Name))
                    Logger.Warn($"自定义角色 '{role.Name}' 覆盖同名内置角色");
                Register(role);
                count++;
            }
            catch (Exception ex)
            {
                Logger.Warn($"加载角色文件失败: {file}", ex);
            }
        }

        if (count > 0)
            Logger.Info($"已从工作区加载 {count} 个自定义角色 ({dir})");
        return count;
    }
```

文件顶部需追加 using（`System.Diagnostics.CodeAnalysis` 在 GlobalUsings 已有，但 `System.Text.Json` 也在 GlobalUsings 已有——实际上两者都无需手动添加；`RequiresUnreferencedCode` 属性可直接使用）。无需改 using。

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj" --filter "FullyQualifiedName~SubAgentRoleRegistryTests"`
Expected: 全部 PASS（原有 4 个 + 新增 4 个）。

- [ ] **Step 5: Commit**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A
git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "feat: add SubAgentRoleRegistry.LoadFromWorkspace for custom roles"
```

---

### Task 5: AgiCommand 工作区模板/角色加载（CLI）

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-agent\Commands\AgiCommand.cs`（约 106-109 行 skill 加载块之后）

- [ ] **Step 1: 修改 AgiCommand.cs**

在 skill 加载块（`if (workspaceSkillsDir != null) { _skillRegistry.LoadFromWorkspace(workspace.RootPath); }`）之后插入：

```csharp
        // 加载工作区编排配置：任务模板（.luban-agent/plans）与自定义角色（.luban-agent/roles）
        try
        {
            var templatePlanner = serviceProvider.GetService<LuBan.AIAgent.Orchestration.Planner.TemplateTaskPlanner>();
            var templatesLoaded = templatePlanner?.LoadFromWorkspace(workspace.RootPath) ?? 0;
            var roleRegistry = serviceProvider.GetService<LuBan.AIAgent.Orchestration.SubAgentRoleRegistry>();
            var rolesLoaded = roleRegistry?.LoadFromWorkspace(workspace.RootPath) ?? 0;
            if (templatesLoaded > 0 || rolesLoaded > 0)
                Console.WriteLine($"已加载工作区编排配置: {templatesLoaded} 个任务模板, {rolesLoaded} 个自定义角色");
        }
        catch (Exception ex)
        {
            Logger.Warn("加载工作区编排配置失败", ex);
        }
```

`serviceProvider` 在该位置上方（约 92 行）已定义。`GetService<T>` 需要 `Microsoft.Extensions.DependencyInjection`——AgiCommand.cs 已使用该命名空间（GetRequiredService 已出现）。

- [ ] **Step 2: 验证构建**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli.csproj"`
Expected: 0 错误。

- [ ] **Step 3: Commit**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-agent" add -A
git -C "D:\WorkBench\Walle\luban\luban-agent" commit -m "feat: load workspace plan templates and custom roles in /agi"
```

---

### Task 6: MockProviderRouter 测试辅助类

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\AIAgentTests.cs`（文件末尾 MockChatClient 类之后）

- [ ] **Step 1: 追加 MockProviderRouter**

在 `AIAgentTests.cs` 末尾 `MockChatClient` 类之后追加（与 MockChatClient 同级，namespace `LuBan.AIAgent.Tests`；文件顶部 using 已含 `LuBan.AIAgent.Configuration`——若没有则添加 `using LuBan.AIAgent.Configuration;`）：

```csharp
public class MockProviderRouter : IProviderRouter
{
    private readonly IChatClient _client;
    private readonly bool _throwOnRoute;

    public List<string?> RequestedModels { get; } = new();

    public MockProviderRouter(IChatClient client, bool throwOnRoute = false)
    {
        _client = client;
        _throwOnRoute = throwOnRoute;
    }

    public IChatClient CreateChatClient(string? providerModel = null)
    {
        RequestedModels.Add(providerModel);
        if (_throwOnRoute && providerModel != null)
            throw new InvalidOperationException($"Provider '{providerModel}' not found");
        return _client;
    }

    public IReadOnlyList<ProviderInfo> GetAvailableProviders()
        => new List<ProviderInfo> { new("mock", "Mock Provider", new[] { "test" }) };
}
```

- [ ] **Step 2: 验证编译**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj"`
Expected: 0 错误。

- [ ] **Step 3: Commit**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A
git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "test: add MockProviderRouter test helper"
```

---

### Task 7: LuBanAgentFactory 多模型路由（框架）

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBanAgentFactory.cs`
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\Orchestration\ModelRoutingTests.cs`（新增）

- [ ] **Step 1: Write the failing tests**

新建 `LuBan.AIAgent.Tests\Orchestration\ModelRoutingTests.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LuBan.AIAgent.Tests.Orchestration
*文件名： ModelRoutingTests
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/7
*描述：多模型路由单元测试
*
*****************************************************************************/
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Tests;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LuBan.AIAgent.Tests.Orchestration;

[TestClass]
public class ModelRoutingTests
{
    private static ServiceProvider BuildFactoryServices(MockProviderRouter router)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new LuBanAgentOptions()));
        services.AddSingleton<IChatClient>(new MockChatClient("default", _ => "结果"));
        services.AddSingleton<IProviderRouter>(router);
        services.AddSingleton<ToolPluginRegistry>();
        services.AddScoped<LuBanAgentFactory>();
        return services.BuildServiceProvider();
    }

    [TestMethod]
    public async Task TestCreateSubAgentAsync_指定模型时走路由()
    {
        var router = new MockProviderRouter(new MockChatClient("kimi", _ => "结果"));
        using var sp = BuildFactoryServices(router);
        var factory = sp.GetRequiredService<LuBanAgentFactory>();

        var agent = await factory.CreateSubAgentAsync("kimi:k2", null, "你是子代理");

        Assert.IsNotNull(agent);
        Assert.IsTrue(router.RequestedModels.Contains("kimi:k2"));
    }

    [TestMethod]
    public async Task TestCreateSubAgentAsync_未指定模型不走路由()
    {
        var router = new MockProviderRouter(new MockChatClient("kimi", _ => "结果"));
        using var sp = BuildFactoryServices(router);
        var factory = sp.GetRequiredService<LuBanAgentFactory>();

        var agent = await factory.CreateSubAgentAsync(null, null, "你是子代理");

        Assert.IsNotNull(agent);
        Assert.AreEqual(0, router.RequestedModels.Count);
    }

    [TestMethod]
    public async Task TestCreateSubAgentAsync_路由失败回退默认模型()
    {
        var router = new MockProviderRouter(new MockChatClient("default", _ => "结果"), throwOnRoute: true);
        using var sp = BuildFactoryServices(router);
        var factory = sp.GetRequiredService<LuBanAgentFactory>();

        var agent = await factory.CreateSubAgentAsync("missing:model", null, "你是子代理");

        Assert.IsNotNull(agent);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj" --filter "FullyQualifiedName~ModelRoutingTests"`
Expected: `TestCreateSubAgentAsync_指定模型时走路由` FAIL（RequestedModels 为空，因为工厂尚未路由）；另两个 PASS。

- [ ] **Step 3: Write minimal implementation**

修改 `LuBanAgentFactory.cs`：

1. 字段区追加：

```csharp
    private readonly IProviderRouter? _providerRouter;
```

2. 构造函数改为（追加可选参数并赋值）：

```csharp
    /// <summary>
    /// 创建 LuBanAgentFactory 实例
    /// </summary>
    /// <param name="chatClient">聊天客户端（默认模型）</param>
    /// <param name="pluginRegistry">工具插件注册表</param>
    /// <param name="options">配置选项</param>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="providerRouter">模型提供者路由（可选，注册后支持按 modelName 路由）</param>
    public LuBanAgentFactory(
        IChatClient chatClient,
        ToolPluginRegistry pluginRegistry,
        IOptions<LuBanAgentOptions> options,
        IServiceProvider serviceProvider,
        IProviderRouter? providerRouter = null)
    {
        _chatClient = chatClient;
        _pluginRegistry = pluginRegistry;
        _options = options;
        _serviceProvider = serviceProvider;
        _providerRouter = providerRouter;
    }
```

3. `CreateAsync` 中 `var functionClient = BuildFunctionClient(tools, opts);` 改为：

```csharp
        var functionClient = BuildFunctionClient(tools, opts, modelName);
```

4. `CreateSubAgentAsync` 的 XML 注释 `<param name="modelName">` 改为 `模型名称（格式 "provider:model"，经 IProviderRouter 路由；null 表示默认模型）。`，方法体中 `var functionClient = BuildFunctionClient(tools, opts);` 改为：

```csharp
        var functionClient = BuildFunctionClient(tools, opts, modelName);
```

5. `BuildFunctionClient` 签名与实现改为：

```csharp
    /// <summary>
    /// 构建 FunctionInvokingChatClient。
    /// </summary>
    /// <param name="tools">工具列表。</param>
    /// <param name="opts">配置选项。</param>
    /// <param name="modelName">模型名称（格式 "provider:model"），null 表示默认模型。</param>
    /// <returns>FunctionInvokingChatClient 实例。</returns>
    private FunctionInvokingChatClient BuildFunctionClient(List<AITool> tools, LuBanAgentOptions opts, string? modelName = null)
    {
        var sanitizedClient = new SanitizingChatClient(ResolveChatClient(modelName));
        var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();
        return new FunctionInvokingChatClient(sanitizedClient, loggerFactory, _serviceProvider)
        {
            MaximumIterationsPerRequest = Math.Max(1, opts.MaxToolLoopIterations)
        };
    }

    /// <summary>
    /// 按模型名称解析聊天客户端。未指定模型或未注册路由时使用注入的默认客户端；路由失败回退默认客户端。
    /// </summary>
    /// <param name="modelName">模型名称（格式 "provider:model"）。</param>
    /// <returns>聊天客户端实例。</returns>
    private IChatClient ResolveChatClient(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName) || _providerRouter == null)
            return _chatClient;
        try
        {
            return _providerRouter.CreateChatClient(modelName);
        }
        catch (Exception ex)
        {
            Logger.Warn($"模型 '{modelName}' 路由失败（{ex.Message}），回退默认模型");
            return _chatClient;
        }
    }
```

`BuildHistoryProvider` 保持使用 `_chatClient` 不变。

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj" --filter "FullyQualifiedName~ModelRoutingTests"`
Expected: 3 个测试全部 PASS。

- [ ] **Step 5: 回归验证（构造函数兼容性）**

现有测试均以 4 参构造/DI 解析 LuBanAgentFactory，可选参数不应破坏它们。

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj"`
Expected: 全部测试 PASS（重点确认 SubAgentFactoryTests、DagSchedulerTests、OrchestratorTests、ReplanningTests、AutoOrchestrationIntegrationTests 无回归）。

- [ ] **Step 6: Commit**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A
git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "feat: route modelName through IProviderRouter in LuBanAgentFactory"
```

---

### Task 8: LlmTaskPlanner 使用 PlannerModel 路由（框架）

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Orchestration\Planner\LlmTaskPlanner.cs`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBanAgentExtensions.cs`（注释）
- Test: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\Orchestration\ModelRoutingTests.cs`（追加）

- [ ] **Step 1: Write the failing tests**

在 `ModelRoutingTests.cs` 类末尾追加（需在文件顶部添加 `using LuBan.AIAgent.Orchestration.Planner;`）：

```csharp
    private const string PlannerGraphJson = """
        { "nodes": [ { "id": "a", "description": "a", "prompt": "p", "dependencies": [], "toolGroups": ["filesystem"] } ] }
        """;

    private static ServiceProvider BuildPlannerServices(MockProviderRouter router, string? plannerModel)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new LuBanAgentOptions
        {
            Orchestration = new OrchestrationOptions { PlannerModel = plannerModel, MaxNodes = 10 }
        }));
        services.AddSingleton<IChatClient>(new MockChatClient("default", _ => PlannerGraphJson));
        services.AddSingleton<IProviderRouter>(router);
        services.AddSingleton<ToolPluginRegistry>();
        return services.BuildServiceProvider();
    }

    [TestMethod]
    public async Task TestLlmTaskPlanner_配置PlannerModel时走路由()
    {
        var router = new MockProviderRouter(new MockChatClient("planner", _ => PlannerGraphJson));
        using var sp = BuildPlannerServices(router, "kimi:planner-strong");

        var planner = new LlmTaskPlanner(
            sp.GetRequiredService<IChatClient>(),
            sp,
            sp.GetRequiredService<IOptions<LuBanAgentOptions>>(),
            router);
        var graph = await planner.PlanAsync("任意任务");

        Assert.IsNotNull(graph);
        Assert.IsTrue(router.RequestedModels.Contains("kimi:planner-strong"));
    }

    [TestMethod]
    public async Task TestLlmTaskPlanner_未配置PlannerModel不走路由()
    {
        var router = new MockProviderRouter(new MockChatClient("planner", _ => PlannerGraphJson));
        using var sp = BuildPlannerServices(router, null);

        var planner = new LlmTaskPlanner(
            sp.GetRequiredService<IChatClient>(),
            sp,
            sp.GetRequiredService<IOptions<LuBanAgentOptions>>(),
            router);
        var graph = await planner.PlanAsync("任意任务");

        Assert.IsNotNull(graph);
        Assert.AreEqual(0, router.RequestedModels.Count);
    }

    [TestMethod]
    public async Task TestLlmTaskPlanner_路由失败回退注入客户端()
    {
        var router = new MockProviderRouter(new MockChatClient("x", _ => PlannerGraphJson), throwOnRoute: true);
        using var sp = BuildPlannerServices(router, "missing:model");

        var planner = new LlmTaskPlanner(
            sp.GetRequiredService<IChatClient>(),
            sp,
            sp.GetRequiredService<IOptions<LuBanAgentOptions>>(),
            router);
        var graph = await planner.PlanAsync("任意任务");

        Assert.IsNotNull(graph);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj" --filter "FullyQualifiedName~ModelRoutingTests"`
Expected: 编译失败，LlmTaskPlanner 无 4 参构造函数。

- [ ] **Step 3: Write minimal implementation**

修改 `LlmTaskPlanner.cs` 构造函数：

```csharp
    /// <summary>
    /// 创建 LlmTaskPlanner 实例。
    /// </summary>
    /// <param name="chatClient">聊天客户端（默认模型，作为未配置 PlannerModel 或路由失败时的回退）。</param>
    /// <param name="serviceProvider">服务提供者。</param>
    /// <param name="options">配置选项。</param>
    /// <param name="providerRouter">模型提供者路由（可选，配置 PlannerModel 后生效）。</param>
    public LlmTaskPlanner(
        IChatClient chatClient,
        IServiceProvider serviceProvider,
        IOptions<LuBanAgentOptions> options,
        IProviderRouter? providerRouter = null)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _chatClient = ResolvePlannerClient(chatClient, providerRouter, options.Value.Orchestration?.PlannerModel);
    }

    /// <summary>
    /// 按 PlannerModel 配置解析规划器使用的聊天客户端，路由失败回退注入客户端。
    /// </summary>
    private static IChatClient ResolvePlannerClient(IChatClient fallback, IProviderRouter? router, string? plannerModel)
    {
        if (string.IsNullOrEmpty(plannerModel) || router == null)
            return fallback;
        try
        {
            return router.CreateChatClient(plannerModel);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Planner 模型 '{plannerModel}' 路由失败（{ex.Message}），回退默认模型");
            return fallback;
        }
    }
```

修改 `LuBanAgentExtensions.cs` 第 108 行注释：

```csharp
        // 规划器：LlmTaskPlanner 依赖 IChatClient（通常 Scoped）+ 可选 IProviderRouter（PlannerModel 路由），必须 Scoped
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj" --filter "FullyQualifiedName~ModelRoutingTests"`
Expected: 6 个测试全部 PASS。

- [ ] **Step 5: 回归验证**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj"`
Expected: 全部 PASS（重点确认 LlmTaskPlannerTests、ReplanningTests 中以 3 参构造的 LlmTaskPlanner 仍编译并通过）。

- [ ] **Step 6: Commit**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A
git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "feat: route LlmTaskPlanner through PlannerModel via IProviderRouter"
```

---

### Task 9: 注释与 README 更新

**Files:**
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Orchestration\Models\TaskNode.cs`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\README.md`
- Modify: `D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\README.en.md`
- Modify: `D:\WorkBench\Walle\luban\luban-agent\README.md`
- Modify: `D:\WorkBench\Walle\luban\luban-agent\README.en.md`

- [ ] **Step 1: 更新 TaskNode.ModelName 注释**

将 `TaskNode.cs` 中 ModelName 属性的注释：

```csharp
    /// <summary>
    /// 获取或设置该节点使用的模型（格式 "provider:model"）。null 表示继承主 Agent 模型。
    /// 注意：当前 LuBanAgentFactory 的 modelName 参数尚未实现多模型路由，此字段仅作预留，
    /// 实际执行时统一使用主模型。
    /// </summary>
```

改为：

```csharp
    /// <summary>
    /// 获取或设置该节点使用的模型（格式 "provider:model"）。null 表示继承主 Agent 模型。
    /// 经 IProviderRouter 路由，Provider 不存在时回退默认模型。
    /// </summary>
```

- [ ] **Step 2: 更新框架 README（LuBan.AIAgent/README.md + README.en.md）**

在编排/Orchestration 相关章节（组件表格或配置示例附近）补充以下内容：

README.md（中文）追加小节：

```markdown
#### 工作区编排扩展

进入 `/agi` 工作区时自动加载以下目录：

- `.luban-agent/plans/*.json`：任务模板，命中关键词时由 TemplateTaskPlanner 直接生成图谱（不消耗 LLM 调用）。格式：`{ "name": "...", "keywords": [...], "graph": { "nodes": [...] } }`。
- `.luban-agent/roles/*.json`：自定义 SubAgent 角色，同名覆盖内置角色。格式：`{ "name": "...", "systemPromptTemplate": "... {prompt} ...", "defaultToolGroups": [...] }`。

#### 多模型路由

注册 `IProviderRouter` 后，`TaskNode.ModelName`（格式 `provider:model`）与 `OrchestrationOptions.PlannerModel` 会路由到对应 Provider；路由失败自动回退默认模型并记录警告。未注册路由时行为不变。

#### 启发式预过滤

`Orchestration:HeuristicFilter`（Enabled / MaxLength / Keywords）：短输入且无复合关键词时跳过 planner，节省一次 LLM 调用。
```

README.en.md（英文）追加对应小节：

```markdown
#### Workspace Orchestration Extensions

On entering an `/agi` workspace, the following directories are loaded automatically:

- `.luban-agent/plans/*.json`: task templates. When a keyword matches, TemplateTaskPlanner generates the graph directly (no LLM call). Format: `{ "name": "...", "keywords": [...], "graph": { "nodes": [...] } }`.
- `.luban-agent/roles/*.json`: custom SubAgent roles; same-name entries override built-in roles. Format: `{ "name": "...", "systemPromptTemplate": "... {prompt} ...", "defaultToolGroups": [...] }`.

#### Multi-Model Routing

When an `IProviderRouter` is registered, `TaskNode.ModelName` (format `provider:model`) and `OrchestrationOptions.PlannerModel` are routed to the corresponding provider. Routing failures fall back to the default model with a warning. Behavior is unchanged without a router.

#### Heuristic Pre-Filter

`Orchestration:HeuristicFilter` (Enabled / MaxLength / Keywords): short inputs without composite keywords skip the planner, saving one LLM call.
```

- [ ] **Step 3: 更新 CLI README（luban-agent/README.md + README.en.md）**

在两个文件的 Orchestration 配置示例中追加 HeuristicFilter 配置块：

```json
      "HeuristicFilter": {
        "Enabled": true,
        "MaxLength": 20,
        "Keywords": [ "和", "同时", "然后", "并且", "另外", "还有", "分析并", "搜索并" ]
      }
```

README.md 特性/说明区追加一行：

```markdown
- **编排扩展**: 工作区 `.luban-agent/plans/*.json` 定义任务模板、`.luban-agent/roles/*.json` 定义自定义 SubAgent 角色；`Orchestration:PlannerModel` 与节点 `ModelName` 支持 `provider:model` 多模型路由
```

README.en.md 对应位置追加：

```markdown
- **Orchestration Extensions**: workspace `.luban-agent/plans/*.json` for task templates and `.luban-agent/roles/*.json` for custom SubAgent roles; `Orchestration:PlannerModel` and node `ModelName` support `provider:model` multi-model routing
```

- [ ] **Step 4: 验证构建**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"`
Expected: 0 错误。

- [ ] **Step 5: Commit**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A
git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "docs: update README and TaskNode.ModelName comment for orchestration optimizations"
git -C "D:\WorkBench\Walle\luban\luban-agent" add -A
git -C "D:\WorkBench\Walle\luban\luban-agent" commit -m "docs: update README for heuristic filter and orchestration extensions"
```

---

### Task 10: 全量验证

**Files:** 无（仅验证）

- [ ] **Step 1: 框架构建**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"`
Expected: 0 错误 0 警告（新增代码不引入新警告）。

- [ ] **Step 2: CLI 构建**

Run: `dotnet build "D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli.csproj"`
Expected: 0 错误。

- [ ] **Step 3: 全部测试**

Run: `dotnet test "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent.Tests\LuBan.AIAgent.Tests.csproj"`
Expected: 全部 PASS。新增测试计数：HeuristicFilterTests 6 + TemplateTaskPlannerTests 3 + SubAgentRoleRegistryTests 4 + ModelRoutingTests 6 = 19 个新测试。

- [ ] **Step 4: 手工冒烟（可选，需真实 API Key）**

在任一工作区创建 `.luban-agent/plans/code-review.json`（用 Task 3 测试中的模板内容）与 `.luban-agent/roles/security-expert.json`（用 Task 4 测试中的角色内容），启动 CLI 进入 `/agi`，确认启动时打印"已加载工作区编排配置: 1 个任务模板, 1 个自定义角色"；输入"你好"确认不触发 planner（无"正在分析任务..."）；输入"帮我做一次代码审查"确认模板命中直接拆解执行。

---

## Self-Review 记录

- Spec 覆盖：启发式预过滤（Task 1/2）、模板规划器（Task 3/5）、多模型路由（Task 7/8）、角色扩展（Task 4/5）、配置项（Task 2）、测试（Task 1/3/4/6/7/8）——全覆盖。spec 中 `TaskGraphTemplate.cs 增加 JSON 反序列化支持` 由 `FromJson` 落实。
- 已知偏差：`LuBanAgentFactory`/`LlmTaskPlanner` 采用可选 IProviderRouter 参数而非替换 IChatClient（见文首论证）；`OrchestrationOptions.PlannerModel` 已在上一轮存在，本轮接通。
- 类型一致性：`MockProviderRouter(IChatClient, bool throwOnRoute = false)` / `RequestedModels` 在 Task 6 定义、Task 7/8 使用一致；`LoadFromWorkspace(string) -> int` 签名在两个注册表中一致；`ShouldSkipPlanning(string) -> bool` 在 Task 1 定义、Task 2 使用一致。
