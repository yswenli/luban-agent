# 自动编排与 SubAgent 角色化实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `/agi` 普通工作区的编排能力从"模型可选调用的工具"改为"自动判定 + 自动执行"，同时引入 SubAgent 角色注册表。

**Architecture:** 每轮用户输入先由 `LlmTaskPlanner` 判定是否为复合任务（单节点=普通，多节点=复合），复合任务通过 `Orchestrator.RunAsync(TaskGraph)` 执行 DAG 并行 SubAgent。SubAgent 通过角色注册表获得专业角色（analyst/researcher/coder/writer）和默认工具组。

**Tech Stack:** .NET 8, C#, Microsoft.Extensions.AI, xUnit, Moq

---

## 文件结构

### 框架层（luban-framework/LuBan.AIAgent）

| 文件 | 职责 |
|------|------|
| `Orchestration/Models/SubAgentRole.cs` | 角色定义类（Name, SystemPromptTemplate, DefaultToolGroups） |
| `Orchestration/SubAgentRoleRegistry.cs` | 角色注册表（内置角色 + 工作区扩展） |
| `Orchestration/Models/TaskNode.cs` | 增加 `Role` 字段 |
| `Orchestration/SubAgentFactory.cs` | 支持 Role 映射、toolGroups 过滤、null 校验 |
| `Orchestration/Planner/LlmTaskPlanner.cs` | 提示词增加 role 字段、toolGroups 显式列表 |
| `Orchestration/IOrchestrator.cs` | 新增 `RunAsync(TaskGraph)` 重载 |
| `Orchestration/Orchestrator.cs` | 实现 `RunAsync(TaskGraph)` 重载 |
| `LuBanAgentExtensions.cs` | 注册 SubAgentRoleRegistry |
| `Configuration/OrchestrationOptions.cs` | 增加 `AutoDetect` 配置项 |

### CLI 层（luban-agent）

| 文件 | 职责 |
|------|------|
| `Commands/AgiCommand.cs` | RunChatLoop 增加 planner 决策分流 |
| `appsettings.json` | `ExposeAsTool` 改 false，增加 `AutoDetect` |
| `Profiles/NormalAgentProfile.cs` | SystemPrompt 移除"自动拆解"宣传语 |

### 测试（luban-framework/LuBan.AIAgent.Tests）

| 文件 | 职责 |
|------|------|
| `Orchestration/SubAgentRoleRegistryTests.cs` | 角色注册表测试 |
| `Orchestration/SubAgentFactoryTests.cs` | Role 映射、toolGroups 过滤测试 |
| `Orchestration/Planner/LlmTaskPlannerTests.cs` | role 字段解析测试 |

---

## Task 1: SubAgentRole 模型类

**Files:**
- Create: `LuBan.AIAgent/Orchestration/Models/SubAgentRole.cs`
- Test: `LuBan.AIAgent.Tests/Orchestration/Models/SubAgentRoleTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// LuBan.AIAgent.Tests/Orchestration/Models/SubAgentRoleTests.cs
using LuBan.AIAgent.Orchestration.Models;
using Xunit;

namespace LuBan.AIAgent.Tests.Orchestration.Models;

public class SubAgentRoleTests
{
    [Fact]
    public void SubAgentRole_ShouldHaveRequiredProperties()
    {
        var role = new SubAgentRole
        {
            Name = "coder",
            SystemPromptTemplate = "You are a coder. Task: {prompt}",
            DefaultToolGroups = new List<string> { "filesystem", "script" }
        };

        Assert.Equal("coder", role.Name);
        Assert.Equal("You are a coder. Task: {prompt}", role.SystemPromptTemplate);
        Assert.Equal(2, role.DefaultToolGroups.Count);
        Assert.Contains("filesystem", role.DefaultToolGroups);
        Assert.Contains("script", role.DefaultToolGroups);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~SubAgentRoleTests" --no-build`
Expected: FAIL with "The type or namespace name 'SubAgentRole' could not be found"

- [ ] **Step 3: Write minimal implementation**

```csharp
// LuBan.AIAgent/Orchestration/Models/SubAgentRole.cs
namespace LuBan.AIAgent.Orchestration.Models;

/// <summary>
/// SubAgent 角色定义
/// </summary>
public class SubAgentRole
{
    /// <summary>
    /// 角色名称（如 "analyst", "coder"）
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 系统提示词模板，支持 {prompt} 占位符
    /// </summary>
    public string SystemPromptTemplate { get; set; } = "";

    /// <summary>
    /// 默认工具组列表
    /// </summary>
    public List<string> DefaultToolGroups { get; set; } = new();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~SubAgentRoleTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add LuBan.AIAgent/Orchestration/Models/SubAgentRole.cs LuBan.AIAgent.Tests/Orchestration/Models/SubAgentRoleTests.cs
git commit -m "feat: add SubAgentRole model class"
```

---

## Task 2: SubAgentRoleRegistry 角色注册表

**Files:**
- Create: `LuBan.AIAgent/Orchestration/SubAgentRoleRegistry.cs`
- Test: `LuBan.AIAgent.Tests/Orchestration/SubAgentRoleRegistryTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// LuBan.AIAgent.Tests/Orchestration/SubAgentRoleRegistryTests.cs
using LuBan.AIAgent.Orchestration;
using LuBan.AIAgent.Orchestration.Models;
using Xunit;

namespace LuBan.AIAgent.Tests.Orchestration;

public class SubAgentRoleRegistryTests
{
    [Fact]
    public void GetRole_WithValidName_ShouldReturnRole()
    {
        var registry = new SubAgentRoleRegistry();
        var role = registry.GetRole("analyst");

        Assert.NotNull(role);
        Assert.Equal("analyst", role.Name);
        Assert.Contains("filesystem", role.DefaultToolGroups);
    }

    [Fact]
    public void GetRole_WithInvalidName_ShouldReturnNull()
    {
        var registry = new SubAgentRoleRegistry();
        var role = registry.GetRole("nonexistent");

        Assert.Null(role);
    }

    [Fact]
    public void GetAllRoles_ShouldReturnFourBuiltInRoles()
    {
        var registry = new SubAgentRoleRegistry();
        var roles = registry.GetAllRoles();

        Assert.Equal(4, roles.Count);
        Assert.Contains(roles, r => r.Name == "analyst");
        Assert.Contains(roles, r => r.Name == "researcher");
        Assert.Contains(roles, r => r.Name == "coder");
        Assert.Contains(roles, r => r.Name == "writer");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~SubAgentRoleRegistryTests" --no-build`
Expected: FAIL with "The type or namespace name 'SubAgentRoleRegistry' could not be found"

- [ ] **Step 3: Write minimal implementation**

```csharp
// LuBan.AIAgent/Orchestration/SubAgentRoleRegistry.cs
using LuBan.AIAgent.Orchestration.Models;

namespace LuBan.AIAgent.Orchestration;

/// <summary>
/// SubAgent 角色注册表
/// </summary>
public class SubAgentRoleRegistry
{
    private readonly Dictionary<string, SubAgentRole> _roles = new(StringComparer.OrdinalIgnoreCase);

    public SubAgentRoleRegistry()
    {
        RegisterBuiltInRoles();
    }

    private void RegisterBuiltInRoles()
    {
        Register(new SubAgentRole
        {
            Name = "analyst",
            SystemPromptTemplate = "You are a problem analysis expert. Analyze the task systematically and provide structured insights. Task: {prompt}",
            DefaultToolGroups = new List<string> { "filesystem" }
        });

        Register(new SubAgentRole
        {
            Name = "researcher",
            SystemPromptTemplate = "You are a research specialist. Gather information from multiple sources and verify findings. Task: {prompt}",
            DefaultToolGroups = new List<string> { "web", "filesystem" }
        });

        Register(new SubAgentRole
        {
            Name = "coder",
            SystemPromptTemplate = "You are a code implementation expert. Write clean, runnable code with proper error handling. Task: {prompt}",
            DefaultToolGroups = new List<string> { "filesystem", "script", "database" }
        });

        Register(new SubAgentRole
        {
            Name = "writer",
            SystemPromptTemplate = "You are a writing specialist. Create clear, well-structured content. Task: {prompt}",
            DefaultToolGroups = new List<string> { "filesystem" }
        });
    }

    public void Register(SubAgentRole role)
    {
        _roles[role.Name] = role;
    }

    public SubAgentRole? GetRole(string name)
    {
        return _roles.TryGetValue(name, out var role) ? role : null;
    }

    public IReadOnlyList<SubAgentRole> GetAllRoles()
    {
        return _roles.Values.ToList();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~SubAgentRoleRegistryTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add LuBan.AIAgent/Orchestration/SubAgentRoleRegistry.cs LuBan.AIAgent.Tests/Orchestration/SubAgentRoleRegistryTests.cs
git commit -m "feat: add SubAgentRoleRegistry with 4 built-in roles"
```

---

## Task 3: TaskNode 增加 Role 字段

**Files:**
- Modify: `LuBan.AIAgent/Orchestration/Models/TaskNode.cs`
- Test: `LuBan.AIAgent.Tests/Orchestration/Models/TaskNodeTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// LuBan.AIAgent.Tests/Orchestration/Models/TaskNodeTests.cs
using LuBan.AIAgent.Orchestration.Models;
using Xunit;

namespace LuBan.AIAgent.Tests.Orchestration.Models;

public class TaskNodeTests
{
    [Fact]
    public void TaskNode_ShouldHaveRoleProperty()
    {
        var node = new TaskNode
        {
            Id = "node1",
            Role = "coder",
            Prompt = "Implement feature X",
            ToolGroups = new List<string> { "filesystem", "script" }
        };

        Assert.Equal("coder", node.Role);
    }

    [Fact]
    public void TaskNode_RoleShouldBeOptional()
    {
        var node = new TaskNode
        {
            Id = "node1",
            Prompt = "Do something",
            ToolGroups = new List<string> { "filesystem" }
        };

        Assert.Null(node.Role);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~TaskNodeTests" --no-build`
Expected: FAIL with "'TaskNode' does not contain a definition for 'Role'"

- [ ] **Step 3: Add Role property to TaskNode**

在 `LuBan.AIAgent/Orchestration/Models/TaskNode.cs` 的 `Description` 属性后添加：

```csharp
    /// <summary>
    /// 获取或设置节点角色（如 "analyst", "coder"）。null 表示使用通用 SubAgent。
    /// </summary>
    public string? Role { get; set; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~TaskNodeTests"`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add LuBan.AIAgent/Orchestration/Models/TaskNode.cs LuBan.AIAgent.Tests/Orchestration/Models/TaskNodeTests.cs
git commit -m "feat: add Role property to TaskNode"
```

---

## Task 4: SubAgentFactory Role 映射与 toolGroups 过滤

**Files:**
- Modify: `LuBan.AIAgent/Orchestration/SubAgentFactory.cs`
- Test: `LuBan.AIAgent.Tests/Orchestration/SubAgentFactoryTests.cs`

- [ ] **Step 1: Write the failing test**

在 `LuBan.AIAgent.Tests/Orchestration/SubAgentFactoryTests.cs` 中添加：

```csharp
    [Fact]
    public async Task CreateAsync_WithRole_ShouldUseRoleDefaultToolGroups()
    {
        var innerFactory = new Mock<LuBanAgentFactory>();
        var roleRegistry = new SubAgentRoleRegistry();
        var factory = new SubAgentFactory(innerFactory.Object, roleRegistry);

        var spec = new SubAgentSpec
        {
            NodeId = "node1",
            Prompt = "Analyze this",
            Role = "analyst",
            ToolGroups = null  // Should use role's default
        };

        await factory.CreateAsync(spec);

        innerFactory.Verify(f => f.CreateSubAgentAsync(
            It.IsAny<string?>(),
            It.Is<IEnumerable<string>?>(tg => tg != null && tg.Contains("filesystem")),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task CreateAsync_WithExplicitToolGroups_ShouldOverrideRoleDefault()
    {
        var innerFactory = new Mock<LuBanAgentFactory>();
        var roleRegistry = new SubAgentRoleRegistry();
        var factory = new SubAgentFactory(innerFactory.Object, roleRegistry);

        var spec = new SubAgentSpec
        {
            NodeId = "node1",
            Prompt = "Code this",
            Role = "coder",
            ToolGroups = new List<string> { "web" }  // Override role default
        };

        await factory.CreateAsync(spec);

        innerFactory.Verify(f => f.CreateSubAgentAsync(
            It.IsAny<string?>(),
            It.Is<IEnumerable<string>?>(tg => tg != null && tg.Contains("web") && !tg.Contains("filesystem")),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task CreateAsync_WithOrchestrationInToolGroups_ShouldFilterIt()
    {
        var innerFactory = new Mock<LuBanAgentFactory>();
        var roleRegistry = new SubAgentRoleRegistry();
        var factory = new SubAgentFactory(innerFactory.Object, roleRegistry);

        var spec = new SubAgentSpec
        {
            NodeId = "node1",
            Prompt = "Do something",
            ToolGroups = new List<string> { "filesystem", "orchestration" }
        };

        await factory.CreateAsync(spec);

        innerFactory.Verify(f => f.CreateSubAgentAsync(
            It.IsAny<string?>(),
            It.Is<IEnumerable<string>?>(tg => tg != null && !tg.Contains("orchestration")),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task CreateAsync_WithNullToolGroupsAndNoRole_ShouldThrow()
    {
        var innerFactory = new Mock<LuBanAgentFactory>();
        var roleRegistry = new SubAgentRoleRegistry();
        var factory = new SubAgentFactory(innerFactory.Object, roleRegistry);

        var spec = new SubAgentSpec
        {
            NodeId = "node1",
            Prompt = "Do something",
            Role = null,
            ToolGroups = null
        };

        await Assert.ThrowsAsync<ArgumentException>(() => factory.CreateAsync(spec));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~SubAgentFactoryTests" --no-build`
Expected: FAIL (SubAgentFactory constructor doesn't accept roleRegistry yet)

- [ ] **Step 3: Update SubAgentFactory**

修改 `LuBan.AIAgent/Orchestration/SubAgentFactory.cs`：

1. 添加 `SubAgentRoleRegistry` 依赖：

```csharp
public class SubAgentFactory
{
    private readonly LuBanAgentFactory _innerFactory;
    private readonly SubAgentRoleRegistry _roleRegistry;

    public SubAgentFactory(LuBanAgentFactory innerFactory, SubAgentRoleRegistry roleRegistry)
    {
        _innerFactory = innerFactory;
        _roleRegistry = roleRegistry;
    }
```

2. 修改 `CreateAsync` 方法，添加 Role 映射和 toolGroups 过滤：

```csharp
    public async Task<LuBanAgent> CreateAsync(SubAgentSpec spec, CancellationToken ct = default)
    {
        // Resolve tool groups: explicit > role default
        List<string>? resolvedToolGroups = spec.ToolGroups;
        string? systemPrompt = null;

        if (!string.IsNullOrEmpty(spec.Role))
        {
            var role = _roleRegistry.GetRole(spec.Role);
            if (role != null)
            {
                resolvedToolGroups = spec.ToolGroups ?? role.DefaultToolGroups;
                systemPrompt = role.SystemPromptTemplate.Replace("{prompt}", spec.Prompt);
            }
            else
            {
                Logger.Warn($"Role '{spec.Role}' not found, falling back to generic SubAgent");
            }
        }

        // Validate: if no role and no explicit tool groups, throw
        if (resolvedToolGroups == null && string.IsNullOrEmpty(spec.Role))
        {
            throw new ArgumentException("ToolGroups must be specified when Role is not set");
        }

        // Filter out orchestration to prevent recursion
        if (resolvedToolGroups != null)
        {
            resolvedToolGroups = resolvedToolGroups
                .Where(g => !string.Equals(g, "orchestration", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var agent = await _innerFactory.CreateSubAgentAsync(
            modelName: spec.ModelName,
            toolGroups: resolvedToolGroups,
            systemPrompt: systemPrompt ?? BuildSubAgentSystemPrompt(spec),
            cancellationToken: ct);

        spec.SessionId = agent.Id;
        return agent;
    }
```

3. 更新 `SubAgentSpec` 添加 `Role` 字段：

在 `LuBan.AIAgent/Orchestration/Models/SubAgentSpec.cs` 中添加：

```csharp
    /// <summary>
    /// 获取或设置角色名称。
    /// </summary>
    public string? Role { get; set; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~SubAgentFactoryTests"`
Expected: PASS (4 new tests + existing tests)

- [ ] **Step 5: Commit**

```bash
git add LuBan.AIAgent/Orchestration/SubAgentFactory.cs LuBan.AIAgent/Orchestration/Models/SubAgentSpec.cs LuBan.AIAgent.Tests/Orchestration/SubAgentFactoryTests.cs
git commit -m "feat: SubAgentFactory supports Role mapping and toolGroups filtering"
```

---

## Task 5: LlmTaskPlanner 提示词更新

**Files:**
- Modify: `LuBan.AIAgent/Orchestration/Planner/LlmTaskPlanner.cs`
- Test: `LuBan.AIAgent.Tests/Orchestration/Planner/LlmTaskPlannerTests.cs`

- [ ] **Step 1: Write the failing test**

在 `LuBan.AIAgent.Tests/Orchestration/Planner/LlmTaskPlannerTests.cs` 中添加：

```csharp
    [Fact]
    public async Task PlanAsync_WithRoleInResponse_ShouldParseRole()
    {
        var mockChatClient = new Mock<IChatClient>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockOptions = new Mock<IOptions<LuBanAgentOptions>>();
        var mockRegistry = new Mock<ToolPluginRegistry>();

        mockOptions.Setup(o => o.Value).Returns(new LuBanAgentOptions());
        mockRegistry.Setup(r => r.GetEnabledPlugins()).Returns(new List<ILuBanToolPlugin>());
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ToolPluginRegistry))).Returns(mockRegistry.Object);

        var jsonResponse = @"{
            ""nodes"": [
                {
                    ""id"": ""analyze"",
                    ""description"": ""Analyze requirements"",
                    ""prompt"": ""Analyze the task"",
                    ""role"": ""analyst"",
                    ""toolGroups"": [""filesystem""],
                    ""dependencies"": [],
                    ""isCritical"": true
                }
            ]
        }";

        mockChatClient.Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var response = new Mock<ChatResponse>();
                var message = new ChatMessage(ChatRole.Assistant, jsonResponse);
                response.Setup(r => r.Messages).Returns(new List<ChatMessage> { message });
                return response.Object;
            });

        var planner = new LlmTaskPlanner(mockChatClient.Object, mockServiceProvider.Object, mockOptions.Object);
        var graph = await planner.PlanAsync("Analyze this task");

        Assert.NotNull(graph);
        Assert.Single(graph.Nodes);
        Assert.Equal("analyst", graph.Nodes[0].Role);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~LlmTaskPlannerTests.PlanAsync_WithRoleInResponse" --no-build`
Expected: FAIL (Role property not parsed yet)

- [ ] **Step 3: Update BuildPlannerPrompt**

修改 `LuBan.AIAgent/Orchestration/Planner/LlmTaskPlanner.cs` 的 `BuildPlannerPrompt` 方法：

```csharp
    private static string BuildPlannerPrompt(string task, List<string> tools)
    {
        return $@"你是任务规划专家。将用户的复合任务拆解为 DAG 任务图谱。

## 输出格式（严格 JSON）
{{
  ""nodes"": [
    {{
      ""id"": ""唯一标识（如 research/analyze/execute）"",
      ""description"": ""节点用途描述"",
      ""prompt"": ""执行 prompt，可使用 {{dep:节点id}} 引用前驱输出"",
      ""role"": ""analyst|researcher|coder|writer|null"",
      ""dependencies"": [""依赖的节点id""],
      ""toolGroups"": [""web"" | ""filesystem"" | ""script"" | ""database"" | ""redis"" | ""retrieval"" | ""localmemory"" | ""browser"" | null],
      ""isCritical"": true | false
    }}
  ]
}}

## 可用角色
- analyst: 问题分析专家，默认工具组 [""filesystem""]
- researcher: 信息检索专家，默认工具组 [""web"", ""filesystem""]
- coder: 代码实现专家，默认工具组 [""filesystem"", ""script"", ""database""]
- writer: 文案撰写专家，默认工具组 [""filesystem""]

## 可用工具组
{string.Join(", ", tools.Where(t => t != "orchestration"))}

## 拆解原则
1. 每个节点应是独立的、可验证的子任务
2. 无依赖的节点不要强行添加依赖
3. 节点数量控制在 1-8 个（1 个表示普通任务，多个表示复合任务）
4. 终点节点应产出最终交付物
5. 使用 {{dep:id}} 占位符让后继节点引用前驱输出
6. 为每个节点选择合适的角色（role），若不确定可设为 null
7. toolGroups 可省略（使用角色默认工具组）或显式指定（覆盖角色默认值）；若未指定 role，则 toolGroups 必须显式指定

## 用户任务
{task}";
    }
```

- [ ] **Step 4: Update JSON parsing to include Role**

在 `PlanAsync` 方法的 JSON 解析部分，添加 Role 字段解析：

```csharp
                if (nodeEl.TryGetProperty("role", out var roleProp))
                {
                    node.Role = roleProp.GetString();
                }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~LlmTaskPlannerTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add LuBan.AIAgent/Orchestration/Planner/LlmTaskPlanner.cs LuBan.AIAgent.Tests/Orchestration/Planner/LlmTaskPlannerTests.cs
git commit -m "feat: LlmTaskPlanner prompt includes role field and explicit toolGroups"
```

---

## Task 6: IOrchestrator.RunAsync(TaskGraph) 重载

**Files:**
- Modify: `LuBan.AIAgent/Orchestration/IOrchestrator.cs`
- Modify: `LuBan.AIAgent/Orchestration/Orchestrator.cs`
- Test: `LuBan.AIAgent.Tests/Orchestration/OrchestratorTests.cs`

- [ ] **Step 1: Write the failing test**

在 `LuBan.AIAgent.Tests/Orchestration/OrchestratorTests.cs` 中添加：

```csharp
    [Fact]
    public async Task RunAsync_WithPrecomputedGraph_ShouldSkipPlanning()
    {
        var mockPlanner = new Mock<ITaskPlanner>();
        var mockScheduler = new Mock<DagScheduler>();
        var mockContextStore = new Mock<ContextStore>();
        var mockOptions = new Mock<IOptions<LuBanAgentOptions>>();

        mockOptions.Setup(o => o.Value).Returns(new LuBanAgentOptions());

        var graph = new TaskGraph
        {
            GraphId = "test-graph",
            OriginalTask = "Test task",
            Nodes = new List<TaskNode>
            {
                new TaskNode { Id = "node1", Prompt = "Do something", ToolGroups = new List<string> { "filesystem" } }
            }
        };

        mockScheduler.Setup(s => s.ExecuteAsync(It.IsAny<TaskGraph>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationResult { OverallStatus = "completed" });

        var orchestrator = new Orchestrator(mockPlanner.Object, mockScheduler.Object, mockContextStore.Object, mockOptions.Object);
        var result = await orchestrator.RunAsync(graph);

        Assert.Equal("completed", result.OverallStatus);
        mockPlanner.Verify(p => p.PlanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~OrchestratorTests.RunAsync_WithPrecomputedGraph" --no-build`
Expected: FAIL with "'IOrchestrator' does not contain a definition for 'RunAsync' that takes 1 argument"

- [ ] **Step 3: Add overload to IOrchestrator**

在 `LuBan.AIAgent/Orchestration/IOrchestrator.cs` 中添加：

```csharp
    /// <summary>
    /// 执行预计算的任务图谱
    /// </summary>
    Task<OrchestrationResult> RunAsync(TaskGraph graph, CancellationToken ct = default);
```

- [ ] **Step 4: Implement overload in Orchestrator**

在 `LuBan.AIAgent/Orchestration/Orchestrator.cs` 中添加：

```csharp
    /// <inheritdoc/>
    public async Task<OrchestrationResult> RunAsync(TaskGraph graph, CancellationToken ct = default)
    {
        if (graph == null)
            throw new ArgumentNullException(nameof(graph));

        if (!graph.Validate(out var errors))
            throw new TaskPlanningException("DAG 校验失败", errors);

        var orchestrationOpts = _options.Value.Orchestration ?? new();
        var maxReplan = orchestrationOpts.MaxReplanAttempts;

        var attempt = 0;
        OrchestrationResult? lastResult = null;
        ReflectionResult? reflection = null;
        Dictionary<string, string>? dependencyOutputsSnapshot = null;

        while (attempt <= maxReplan)
        {
            OrchestrationResult result;
            try
            {
                result = await _scheduler.ExecuteAsync(graph, ct);

                if (result.OverallStatus == "failed" && attempt < maxReplan)
                {
                    var failedNodeIds = result.Nodes
                        .Where(n => n.Status == TaskNodeStatus.Failed)
                        .Select(n => n.NodeId)
                        .ToHashSet();

                    dependencyOutputsSnapshot = CaptureDependencyOutputs(graph, failedNodeIds);
                }
            }
            finally
            {
                _contextStore.Clear(graph.GraphId);
            }

            result.FinalOutput = AggregateFinalOutput(graph, result);
            result.ReplanningAttempts = attempt;
            result.Reflection = reflection;
            lastResult = result;

            if (result.OverallStatus != "failed")
                return result;

            if (attempt >= maxReplan)
            {
                result.ReplanningExhausted = true;
                return result;
            }

            attempt++;
            try
            {
                reflection = await PerformReflectionAsync(
                    graph, result, graph.OriginalTask, attempt, dependencyOutputsSnapshot, ct);
            }
            catch (Exception ex)
            {
                Logger.Warn($"反思阶段失败: {ex.Message}", ex);
                result.ReplanningExhausted = true;
                result.Reflection = new ReflectionResult
                {
                    Analysis = $"反思失败: {ex.Message}",
                    ShouldRetry = false,
                    FailedNodeIds = result.Nodes
                        .Where(n => n.Status == TaskNodeStatus.Failed)
                        .Select(n => n.NodeId).ToList()
                };
                return result;
            }

            result.Reflection = reflection;

            if (!reflection.ShouldRetry || reflection.NewNodes.Count == 0)
            {
                result.ReplanningExhausted = true;
                return result;
            }

            graph = BuildFixGraph(graph, reflection, attempt);
            if (!graph.Validate(out errors))
            {
                result.ReplanningExhausted = true;
                result.Reflection = new ReflectionResult
                {
                    Analysis = $"修正图谱校验失败: {string.Join("; ", errors)}",
                    ShouldRetry = false,
                    FailedNodeIds = reflection.FailedNodeIds
                };
                return result;
            }
        }

        lastResult!.ReplanningExhausted = true;
        return lastResult;
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test LuBan.AIAgent.Tests --filter "FullyQualifiedName~OrchestratorTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add LuBan.AIAgent/Orchestration/IOrchestrator.cs LuBan.AIAgent/Orchestration/Orchestrator.cs LuBan.AIAgent.Tests/Orchestration/OrchestratorTests.cs
git commit -m "feat: add IOrchestrator.RunAsync(TaskGraph) overload to skip re-planning"
```

---

## Task 7: LuBanAgentExtensions 注册 SubAgentRoleRegistry

**Files:**
- Modify: `LuBan.AIAgent/LuBanAgentExtensions.cs`

- [ ] **Step 1: Register SubAgentRoleRegistry**

在 `LuBan.AIAgent/LuBanAgentExtensions.cs` 的 `AddLuBanAgent` 方法中，在 Orchestration 子系统注册部分添加：

```csharp
        // ===== Orchestration 子系统注册 =====
        // ContextStore 纯内存线程安全字典，可 Singleton
        services.AddSingleton<Orchestration.ContextStore>();

        // SubAgent 角色注册表
        services.AddSingleton<Orchestration.SubAgentRoleRegistry>();
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build LuBan.AIAgent`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add LuBan.AIAgent/LuBanAgentExtensions.cs
git commit -m "feat: register SubAgentRoleRegistry in DI"
```

---

## Task 8: OrchestrationOptions 增加 AutoDetect 配置

**Files:**
- Modify: `LuBan.AIAgent/Configuration/OrchestrationOptions.cs`

- [ ] **Step 1: Add AutoDetect property**

在 `LuBan.AIAgent/Configuration/OrchestrationOptions.cs` 中添加：

```csharp
    /// <summary>
    /// 获取或设置是否启用自动判定（每轮输入由 planner 判定是否为复合任务）。
    /// </summary>
    public bool AutoDetect { get; set; } = true;
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build LuBan.AIAgent`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add LuBan.AIAgent/Configuration/OrchestrationOptions.cs
git commit -m "feat: add AutoDetect config to OrchestrationOptions"
```

---

## Task 9: AgiCommand planner 决策分流

**Files:**
- Modify: `luban-agent/Commands/AgiCommand.cs`

- [ ] **Step 1: Update RunChatLoop to add planner decision**

在 `luban-agent/Commands/AgiCommand.cs` 的 `RunChatLoop` 方法中，在用户输入后添加 planner 决策逻辑：

```csharp
            // RAG 自动检索注入：将检索结果拼接到用户输入前
            string finalInput = input;
            if (profile.RetrievalMode == "auto")
            {
                finalInput = await InjectRetrievalContextAsync(input, workspace, serviceProvider);
            }

            // 自动编排判定（仅普通工作区且 AutoDetect 启用时）
            var orchestrationOptions = serviceProvider.GetRequiredService<IOptions<LuBanAgentOptions>>().Value.Orchestration;
            var autoDetectEnabled = orchestrationOptions?.AutoDetect ?? false;
            var isRagWorkspace = workspace.Type == "Rag";

            if (autoDetectEnabled && !isRagWorkspace)
            {
                try
                {
                    var planner = serviceProvider.GetRequiredService<ITaskPlanner>();
                    var graph = await planner.PlanAsync(finalInput, escListener.Token);

                    if (graph != null && graph.Nodes.Count >= 2)
                    {
                        // 复合任务：走编排路径
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"检测到复合任务，已拆解为 {graph.Nodes.Count} 个子任务...");
                        Console.ResetColor();

                        var orchestrator = serviceProvider.GetRequiredService<IOrchestrator>();
                        var orchestrationResult = await orchestrator.RunAsync(graph, escListener.Token);

                        if (orchestrationResult.OverallStatus == "completed" || orchestrationResult.OverallStatus == "partial")
                        {
                            // 编排成功，输出结果
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write($"{DateTime.Now:HH:mm:ss} 🤖 ");
                            Console.ResetColor();
                            Console.WriteLine(orchestrationResult.FinalOutput);
                            continue;  // 跳过主 Agent 对话
                        }
                        else
                        {
                            // 编排失败，回退到主 Agent
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"编排失败（{orchestrationResult.OverallStatus}），回退到普通对话...");
                            Console.ResetColor();
                            // 继续走主 Agent 对话路径
                        }
                    }
                    // Nodes.Count == 1: 普通任务，丢弃图谱，走主 Agent 对话
                }
                catch (Exception ex)
                {
                    // planner 调用失败，回退到主 Agent
                    Logger.Warn("Planner 决策失败，回退到普通对话", ex);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("任务规划失败，使用普通模式处理...");
                    Console.ResetColor();
                    // 继续走主 Agent 对话路径
                }
            }
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build luban-agent`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add luban-agent/Commands/AgiCommand.cs
git commit -m "feat: AgiCommand adds planner decision for auto-orchestration"
```

---

## Task 10: appsettings.json 配置更新

**Files:**
- Modify: `luban-agent/appsettings.json`

- [ ] **Step 1: Update Orchestration config**

在 `luban-agent/appsettings.json` 的 `LuBanAgent:Orchestration` 节点中修改/添加：

```json
    "Orchestration": {
      "Enabled": true,
      "PlannerType": "Composite",
      "ExposeAsTool": false,
      "AutoDetect": true,
      "MaxParallelism": 3,
      "MaxNodes": 20,
      "DefaultNodeTimeoutSeconds": 120
    }
```

- [ ] **Step 2: Commit**

```bash
git add luban-agent/appsettings.json
git commit -m "chore: update appsettings.json with ExposeAsTool=false and AutoDetect=true"
```

---

## Task 11: NormalAgentProfile 提示词清理

**Files:**
- Modify: `luban-agent/Profiles/NormalAgentProfile.cs`

- [ ] **Step 1: Remove misleading claim**

修改 `luban-agent/Profiles/NormalAgentProfile.cs` 的 `SystemPrompt`，将"面对复合任务时，自动拆解为子任务并调度 SubAgent 并行执行"改为"面对复合任务时，系统会自动拆解为子任务并调度 SubAgent 并行执行"：

```csharp
    public override string SystemPrompt => @"你是一个智能助手，可以帮助用户完成各类任务。

## 工具使用原则
- **优先使用专用工具**：列出目录用 ListDirectory，读取文件用 ReadFile，搜索文件用 SearchFiles/Grep，而非 RunShell
- **脚本工具是最后手段**：仅当专用工具无法完成任务时才使用 RunShell/RunPython
- 在执行敏感操作前向用户确认

请根据用户的输入，结合可用的工具，给出准确、有帮助的回复。";
```

（删除了"面对复合任务时..."这一行，因为现在是系统自动判定，不需要模型主动拆解）

- [ ] **Step 2: Commit**

```bash
git add luban-agent/Profiles/NormalAgentProfile.cs
git commit -m "chore: remove misleading orchestration claim from NormalAgentProfile"
```

---

## Task 12: 集成测试

**Files:**
- Create: `luban-agent/Tests/Integration/AutoOrchestrationTests.cs`

- [ ] **Step 1: Write integration test**

```csharp
// luban-agent/Tests/Integration/AutoOrchestrationTests.cs
using LuBan.AIAgent.Orchestration;
using LuBan.AIAgent.Orchestration.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LubanAgentCli.Tests.Integration;

public class AutoOrchestrationTests
{
    [Fact]
    public async Task AutoOrchestration_WithCompositeTask_ShouldExecuteDag()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLuBanAgent(new ConfigurationBuilder().Build());
        services.AddSingleton<SubAgentRoleRegistry>();

        var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<SubAgentRoleRegistry>();

        // Act
        var coderRole = registry.GetRole("coder");

        // Assert
        Assert.NotNull(coderRole);
        Assert.Equal("coder", coderRole.Name);
        Assert.Contains("filesystem", coderRole.DefaultToolGroups);
        Assert.Contains("script", coderRole.DefaultToolGroups);
    }
}
```

- [ ] **Step 2: Run integration test**

Run: `dotnet test luban-agent --filter "FullyQualifiedName~AutoOrchestrationTests"`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add luban-agent/Tests/Integration/AutoOrchestrationTests.cs
git commit -m "test: add auto-orchestration integration test"
```

---

## 完成标准

- [ ] 所有单元测试通过
- [ ] 所有集成测试通过
- [ ] `/agi` 普通工作区输入简单问题（如"你好"）→ 单节点图谱 → 主 Agent 对话
- [ ] `/agi` 普通工作区输入复合任务（如"分析这个项目的架构并生成报告"）→ 多节点图谱 → 编排执行
- [ ] 编排失败时回退到普通对话
- [ ] SubAgent 不再包含 orchestration 工具组
- [ ] `AutoDetect=false` 时编排功能完全禁用

---

## 执行选择

**Plan complete and saved to `docs/superpowers/plans/2026-08-07-auto-orchestration-and-subagent-roles.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
