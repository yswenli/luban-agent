# 编排子系统后续优化设计

**日期**: 2026-08-07  
**作者**: yswenli  
**状态**: 草案

## 概述

本设计实现编排子系统的 4 项后续优化：启发式预过滤、模板规划器、多模型路由、角色扩展。

## 目标

1. **启发式预过滤**：对短输入跳过 planner，减少 LLM 调用成本
2. **模板规划器**：注册常用任务模板，命中时快速生成图谱
3. **多模型路由**：允许不同节点使用不同模型（如 planner 用强模型，SubAgent 用快模型）
4. **角色扩展**：通过工作区文件支持用户自定义角色

## 详细设计

### 一、启发式预过滤

**配置项**（`appsettings.json`）：

```json
{
  "LuBanAgent": {
    "Orchestration": {
      "HeuristicFilter": {
        "Enabled": true,
        "MaxLength": 20,
        "Keywords": ["和", "同时", "然后", "并且", "另外", "还有", "分析并", "搜索并"]
      }
    }
  }
}
```

**新增配置类**：

```csharp
public class HeuristicFilterOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxLength { get; set; } = 20;
    public List<string> Keywords { get; set; } = new() { "和", "同时", "然后", "并且", "另外", "还有", "分析并", "搜索并" };
}
```

**逻辑**：

```csharp
// AgiCommand.cs
if (heuristicFilter.Enabled && input.Length < heuristicFilter.MaxLength 
    && !heuristicFilter.Keywords.Any(kw => input.Contains(kw)))
{
    // 跳过 planner，直接走主 Agent 对话
}
```

### 二、模板规划器

**模板文件位置**：`.luban-agent/plans/*.json`

**模板格式**：

```json
{
  "name": "code-review",
  "keywords": ["代码审查", "code review", "review code"],
  "graph": {
    "nodes": [
      {
        "id": "analyze",
        "description": "分析代码结构",
        "prompt": "分析 {param:target} 的代码结构和质量",
        "role": "analyst",
        "toolGroups": ["filesystem"],
        "dependencies": [],
        "isCritical": true
      },
      {
        "id": "review",
        "description": "给出审查意见",
        "prompt": "基于 {dep:analyze} 给出具体的代码审查意见",
        "role": "coder",
        "toolGroups": ["filesystem"],
        "dependencies": ["analyze"],
        "isCritical": false
      }
    ]
  }
}
```

**加载机制**：

- `TemplateTaskPlanner` 增加 `LoadFromWorkspace(string workspaceRoot)` 方法
- 从 `.luban-agent/plans/*.json` 加载模板
- 按关键词匹配，命中则实例化图谱

### 三、多模型路由

**核心改动**：`LuBanAgentFactory` 注入 `IProviderRouter` 替代 `IChatClient`

**改动点**：

1. `LuBanAgentFactory` 构造函数：
```csharp
public LuBanAgentFactory(
    IProviderRouter providerRouter,  // 替代 IChatClient
    ToolPluginRegistry pluginRegistry,
    IOptions<LuBanAgentOptions> options,
    IServiceProvider serviceProvider)
```

2. `BuildFunctionClient` 方法：
```csharp
private FunctionInvokingChatClient BuildFunctionClient(List<AITool> tools, LuBanAgentOptions opts, string? modelName = null)
{
    var chatClient = modelName != null 
        ? _providerRouter.CreateChatClient(modelName) 
        : _chatClient;  // 默认模型
    var sanitizedClient = new SanitizingChatClient(chatClient);
    // ...
}
```

3. `CreateAsync` / `CreateSubAgentAsync`：传递 `modelName` 到 `BuildFunctionClient`

4. `LlmTaskPlanner`：使用 `OrchestrationOptions.PlannerModel` 配置

### 四、角色扩展

**角色文件位置**：`.luban-agent/roles/*.json`

**角色格式**：

```json
{
  "name": "security-expert",
  "systemPromptTemplate": "You are a security expert. Analyze {prompt} for vulnerabilities and security issues.",
  "defaultToolGroups": ["filesystem", "script"]
}
```

**加载机制**：

- `SubAgentRoleRegistry` 增加 `LoadFromWorkspace(string workspaceRoot)` 方法
- 从 `.luban-agent/roles/*.json` 加载自定义角色
- 自定义角色覆盖同名内置角色

## 文件变更清单

### 框架层（luban-framework/LuBan.AIAgent）

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `Configuration/HeuristicFilterOptions.cs` | 新增 | 启发式预过滤配置 |
| `Configuration/OrchestrationOptions.cs` | 修改 | 增加 `HeuristicFilter` 属性 |
| `Orchestration/Planner/TemplateTaskPlanner.cs` | 修改 | 增加 `LoadFromWorkspace` 方法 |
| `Orchestration/Planner/TaskGraphTemplate.cs` | 修改 | 增加 JSON 反序列化支持 |
| `Orchestration/SubAgentRoleRegistry.cs` | 修改 | 增加 `LoadFromWorkspace` 方法 |
| `LuBanAgentFactory.cs` | 修改 | 注入 `IProviderRouter`，支持 modelName 路由 |
| `Orchestration/Planner/LlmTaskPlanner.cs` | 修改 | 使用 `PlannerModel` 配置 |

### CLI 层（luban-agent）

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `Commands/AgiCommand.cs` | 修改 | 增加启发式预过滤逻辑 |
| `Commands/AgiCommand.cs` | 修改 | 调用 `TemplateTaskPlanner.LoadFromWorkspace` |
| `Commands/AgiCommand.cs` | 修改 | 调用 `SubAgentRoleRegistry.LoadFromWorkspace` |
| `appsettings.json` | 修改 | 增加 `HeuristicFilter` 配置 |

### 测试（luban-framework/LuBan.AIAgent.Tests）

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `Orchestration/HeuristicFilterTests.cs` | 新增 | 预过滤逻辑测试 |
| `Orchestration/TemplateTaskPlannerTests.cs` | 修改 | 增加模板加载测试 |
| `Orchestration/SubAgentRoleRegistryTests.cs` | 修改 | 增加角色加载测试 |

## 风险与缓解

| 风险 | 缓解措施 |
|------|---------|
| 启发式预过滤误判（复合任务被跳过） | 配置开关可关闭；关键词列表可自定义 |
| 模板文件加载失败 | 捕获异常，记录日志，不影响其他功能 |
| 多模型路由失败（Provider 不存在） | 回退到默认模型，记录警告日志 |
| 角色文件冲突（同名覆盖） | 自定义角色覆盖内置角色，记录日志 |
