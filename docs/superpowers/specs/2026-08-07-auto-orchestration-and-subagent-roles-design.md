# 自动编排与 SubAgent 角色化设计

**日期**: 2026-08-07  
**作者**: yswenli  
**状态**: 草案

## 概述

本设计将 `/agi` 普通工作区的编排能力从"模型可选调用的工具"改为"自动判定 + 自动执行"，同时引入 SubAgent 角色注册表，使编排出的子任务具备专业角色（分析师、研究员、编码者、写作者）。

## 目标

1. **自动判定复合任务**：每轮用户输入由 planner 判定是否为复合任务，是则自动走 Orchestrator，否则走普通对话。
2. **SubAgent 角色化**：通过角色注册表为 SubAgent 分配专业角色，提供角色化系统提示词和默认工具组。
3. **消除递归编排风险**：禁止 SubAgent 的 `toolGroups=null`，并从 SubAgent 可用工具组中排除 `orchestration`。
4. **失败回退**：编排失败时自动回退到普通对话，保证用户总能得到响应。

## 非目标

- 不改变 `/agi` RAG 工作区和 `/browse` 命令的行为（它们不使用编排）。
- 不引入模板规划器（`TemplateTaskPlanner` 当前无模板注册，保持现状）。
- 不实现多模型路由（`TaskNode.ModelName` 字段保留但暂不启用）。

## 架构

### 整体流程

```
用户输入
  ↓
LlmTaskPlanner.PlanAsync(input)
  ↓
├─ Nodes.Count == 1 → 丢弃图谱 → 主 Agent 对话
├─ Nodes.Count >= 2 → Orchestrator.RunAsync(graph) → DAG 并行执行 SubAgent
└─ 异常/null → 回退主 Agent 对话
```

### 关键决策

- **判定机制**：planner 决策（复用现有 `LlmTaskPlanner`，单节点=普通，多节点=复合）。
- **执行前确认**：全自动执行（不展示图谱让用户确认）。
- **失败回退**：编排失败（含重规划耗尽）→ 回退普通对话。
- **orchestrate 工具**：自动判定取代显式工具，普通 `/agi` 不再暴露 `orchestrate` 工具。

## 详细设计

### 一、Planner 决策机制

**改动点**：`LlmTaskPlanner.BuildPlannerPrompt`

- 当前提示词要求输出"2-8 个节点"，改为"1-8 个节点"。
- 单节点图谱语义：`ToolGroups` 为空列表 `[]`，`Prompt` 为用户原始输入。
- CLI 层检测到 `Nodes.Count == 1` 时丢弃图谱，直接用主 Agent 处理。

**性能考量**：每次输入多一次 planner LLM 调用（约 1-3 秒延迟）。planner 调用是短 prompt（无工具上下文），比主 Agent 带工具的调用便宜。

**优化空间**：未来可加启发式预过滤（如输入长度 < 20 字且无"和/同时/然后"等关键词直接跳过 planner），但本次不做。

### 二、SubAgent 角色注册表

**模型层改动**：

1. `TaskNode` 增加可选字段 `string? Role`（如 `"analyst"`、`"coder"`）。
2. 新增 `SubAgentRole` 类：
   ```csharp
   public class SubAgentRole
   {
       public string Name { get; set; }
       public string SystemPromptTemplate { get; set; }  // 支持 {prompt} 占位符
       public List<string> DefaultToolGroups { get; set; }
   }
   ```
3. 新增 `SubAgentRoleRegistry`：从 DI 加载内置角色 + 从工作区 `.luban-agent/roles/*.json` 加载自定义角色。

**内置角色（4 个）**：

| 角色 | 默认工具组 | 职责 |
|------|-----------|------|
| `analyst` | `["filesystem"]` | 问题分析专家，强调结构化分析 |
| `researcher` | `["web","filesystem"]` | 信息检索专家，强调多源验证 |
| `coder` | `["filesystem","script","database"]` | 代码实现专家，强调可运行代码 |
| `writer` | `["filesystem"]` | 文案撰写专家，强调清晰表达 |

**SubAgentFactory 改造**：

- 若 `node.Role` 非空，从 `SubAgentRoleRegistry` 查找角色 → 拼接角色提示词 + 节点 prompt → 工具组优先级：`node.ToolGroups`（若显式指定）> 角色的 `DefaultToolGroups`。
- 若 `node.Role` 为空，`node.ToolGroups` 必须非 null（见下一节），走现有通用逻辑。
- 工具组解析逻辑：`node.ToolGroups ?? role.DefaultToolGroups`（若两者均为 null 则抛异常）。

**Planner 提示词扩展**：

- `LlmTaskPlanner.BuildPlannerPrompt` 的 JSON schema 增加 `"role": "analyst|researcher|coder|writer|null"` 字段。
- 提示词列出可用角色及其职责，要求 planner 为每个节点选择合适的角色。

### 三、toolGroups 收紧

**TaskNode.ToolGroups 语义变化**：

- 当前：`null` = 全部工具组（含 orchestration），`[]` = 无工具。
- 改后：`null` 仅在 `node.Role` 非空时允许（表示使用角色的 `DefaultToolGroups`）；`node.Role` 为空时 `null` 不再允许（planner 必须显式指定）。`[]` = 无工具。`SubAgentFactory.CreateAsync` 若收到 `ToolGroups == null && Role == null`，抛出 `ArgumentException`。

**orchestration 组排除**：

- `SubAgentFactory.CreateAsync` 在构建 `SubAgentSpec` 时，对最终解析出的工具组（`node.ToolGroups ?? role.DefaultToolGroups`）统一做过滤：剔除 `"orchestration"`（防止递归编排）。
- 若过滤后为空列表，保留空列表（SubAgent 无工具，纯文本推理）。

**Planner 提示词更新**：

- `LlmTaskPlanner.BuildPlannerPrompt` 的 JSON schema 中，`toolGroups` 字段从 `["web" | "filesystem" | null]` 改为显式列出所有可用组（不含 orchestration）：`["web", "filesystem", "script", "database", "redis", "retrieval", "localmemory", "browser"]` 或空数组 `[]`。
- 当节点指定了 `role` 时，`toolGroups` 可省略（使用角色的 `DefaultToolGroups`）或显式指定（覆盖角色默认值）。
- 当节点未指定 `role` 时，`toolGroups` 必须显式指定（不允许 null 或省略）。
- 提示词说明：`toolGroups` 必须为非空数组或空数组，不允许 null（除非指定了 role）。

**OrchestrationToolPlugin 移除**：

- CLI 的 `appsettings.json` 中 `LuBanAgent:Orchestration:ExposeAsTool` 改为 `false`，普通 `/agi` 不再暴露 `orchestrate` 工具。
- 框架层 `ExposeAsTool` 配置项保留供其他宿主使用。

### 四、CLI 集成（AgiCommand 改造）

**AgiCommand.RunChatLoop 改造**：

- 在循环内注入 `IOrchestrator` 和 `ITaskPlanner`（从 `serviceProvider` 获取）。
- 每轮用户输入后，先调用 `planner.PlanAsync(input, ct)`：
  - `Nodes.Count >= 2`：走编排路径（`orchestrator.RunAsync`）。
  - `Nodes.Count == 1`：丢弃图谱，走现有主 Agent 对话。
  - 异常或 null：回退主 Agent 对话。

**编排路径**：

- 调用 `orchestrator.RunAsync(graph, ct)`（传入预计算的图谱，跳过内部规划，避免双重 LLM 调用）。
- 控制台输出编排进度：复用 `Orchestrator.RunStreamingAsync` 的进度事件（PlanningStarted → PlanningCompleted → LayerCompleted → OrchestratingCompleted）。
- 编排完成后，把 `OrchestrationResult.FinalOutput` 作为助手回复输出。
- 若编排失败（`OverallStatus == "failed"` 且重规划耗尽）：回退主 Agent 对话，输出失败摘要 + "回退到普通对话..."提示。

**IOrchestrator 接口扩展**：

- 新增重载 `Task<OrchestrationResult> RunAsync(TaskGraph graph, CancellationToken ct = default)`，接受预计算图谱。
- 现有 `RunAsync(string task, ...)` 保留，内部调 planner 后调用新重载。

**主 Agent 对话路径**：

- 现有 `agent.RunStreamingAsync(input, ct)` 不变，工具组由 `NormalAgentProfile.ToolGroups`（null = 全部）控制。

**进度显示**：

- 编排执行时，控制台显示"正在拆解任务..."→"已生成 N 个节点"→逐层显示节点执行状态。
- 失败回退时显示"编排失败，回退到普通对话..."。

**配置开关**：

- `appsettings.json` 增加 `LuBanAgent:Orchestration:AutoDetect`（默认 `true`），控制是否启用自动判定。
- `AutoDetect=false` 时编排功能完全禁用（既不自动判定，也不暴露 orchestrate 工具），`/agi` 退化为纯单 Agent 对话。

### 五、测试与错误处理

**单元测试**：

- `SubAgentRoleRegistry`：加载内置角色 + 工作区扩展角色，角色查找。
- `SubAgentFactory`：Role 字段映射、DefaultToolGroups 覆盖、orchestration 组过滤、null ToolGroups 抛异常。
- `LlmTaskPlanner`：JSON schema 解析新 role 字段、toolGroups 非 null 校验。
- `AgiCommand` 分流逻辑：单节点丢弃图谱、多节点走编排、planner 异常回退。

**集成测试**：

- 端到端：输入"分析这个项目的架构并生成报告" → planner 拆出 2+ 节点 → 编排执行 → 输出聚合结果。
- 失败回退：模拟 planner 返回无效 JSON → 回退主 Agent 对话。
- 角色化：planner 为节点指定 `role: "coder"` → SubAgent 使用 coder 提示词 + filesystem/script 工具组。

**错误处理**：

- planner 调用失败（网络/模型异常）：捕获异常，回退主 Agent 对话，输出"任务规划失败，使用普通模式处理"。
- 编排执行失败（SubAgent 超时/异常）：Orchestrator 已有重规划机制，重规划耗尽后回退主 Agent。
- 角色查找失败（planner 指定了不存在的 role）：SubAgentFactory 回退到通用 SubAgent，输出警告日志。

**日志与可观测性**：

- 编排决策日志：记录 planner 返回的节点数、是否走编排、角色分配。
- 失败回退日志：记录失败原因、回退到普通对话。

## 配置变更

`appsettings.json` 新增/修改：

```json
{
  "LuBanAgent": {
    "Orchestration": {
      "Enabled": true,
      "ExposeAsTool": false,
      "AutoDetect": true,
      "PlannerType": "Composite",
      "MaxParallelism": 3,
      "MaxNodes": 20,
      "DefaultNodeTimeoutSeconds": 120
    }
  }
}
```

## 文件变更清单

### 框架层（luban-framework/LuBan.AIAgent）

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `Orchestration/Models/TaskNode.cs` | 修改 | 增加 `Role` 字段 |
| `Orchestration/Models/SubAgentRole.cs` | 新增 | 角色定义类 |
| `Orchestration/SubAgentRoleRegistry.cs` | 新增 | 角色注册表 |
| `Orchestration/SubAgentFactory.cs` | 修改 | 支持 Role 映射、toolGroups 过滤、null 校验 |
| `Orchestration/Planner/LlmTaskPlanner.cs` | 修改 | 提示词增加 role 字段、toolGroups 显式列表 |
| `Orchestration/IOrchestrator.cs` | 修改 | 新增 `RunAsync(TaskGraph)` 重载 |
| `Orchestration/Orchestrator.cs` | 修改 | 实现 `RunAsync(TaskGraph)` 重载 |
| `LuBanAgentExtensions.cs` | 修改 | 注册 SubAgentRoleRegistry |
| `Configuration/OrchestrationOptions.cs` | 修改 | 增加 `AutoDetect` 配置项 |

### CLI 层（luban-agent）

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `Commands/AgiCommand.cs` | 修改 | RunChatLoop 增加 planner 决策分流 |
| `appsettings.json` | 修改 | `ExposeAsTool` 改 false，增加 `AutoDetect` |
| `Profiles/NormalAgentProfile.cs` | 修改 | SystemPrompt 移除"自动拆解"宣传语 |

### 测试（luban-framework/LuBan.AIAgent.Tests）

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `Orchestration/SubAgentRoleRegistryTests.cs` | 新增 | 角色注册表测试 |
| `Orchestration/SubAgentFactoryTests.cs` | 修改 | 增加 Role 映射、toolGroups 过滤测试 |
| `Orchestration/Planner/LlmTaskPlannerTests.cs` | 修改 | 增加 role 字段解析测试 |

## 风险与缓解

| 风险 | 缓解措施 |
|------|---------|
| 每次输入多一次 planner LLM 调用，增加延迟和成本 | `AutoDetect` 配置开关可关闭；未来可加启发式预过滤 |
| planner 误判（简单任务拆出多节点） | 失败回退到普通对话；用户可通过 `AutoDetect=false` 关闭 |
| 角色化增加 planner 提示词复杂度 | 内置角色仅 4 个，提示词保持简洁；角色查找失败回退通用 SubAgent |
| SubAgent 工具组过滤可能遗漏 | 显式过滤 orchestration（含角色默认工具组）；单元测试覆盖 |
| 双重规划浪费 LLM 调用 | AgiCommand 传预计算图谱给 Orchestrator，跳过内部规划 |

## 后续优化

1. **启发式预过滤**：对短输入（< 20 字）且无复合关键词的输入跳过 planner，直接走普通对话。
2. **模板规划器**：注册常用任务模板（如"代码审查"、"文档生成"），减少 LLM 调用。
3. **多模型路由**：启用 `TaskNode.ModelName`，允许不同节点使用不同模型（如 planner 用强模型，SubAgent 用快模型）。
4. **角色扩展**：通过工作区 `.luban-agent/roles/*.json` 支持用户自定义角色。

## 附录

### A. 相关代码位置

- 编排链路：`LuBan.AIAgent/Orchestration/`（Orchestrator、DagScheduler、SubAgentFactory、Planner）
- 工具插件：`LuBan.AIAgent/Tools/`（OrchestrationToolPlugin、ToolPluginRegistry）
- CLI 集成：`luban-agent/Commands/AgiCommand.cs`、`Program.cs`

### B. 术语表

- **Planner**：任务规划器，将自然语言任务拆解为 DAG 任务图谱。
- **Orchestrator**：编排器，串联规划、调度与结果聚合。
- **SubAgent**：子 Agent，执行 DAG 中的单个节点任务。
- **Role**：SubAgent 角色，定义系统提示词模板和默认工具组。
