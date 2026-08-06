# LuBan.AIAgent 与 LubanAgent 职责边界重构设计

## 背景

当前 `LuBan.AIAgent`（框架库）和 `LubanAgent`（CLI 应用）之间存在职责边界模糊的问题：应用级配置管理、Provider 路由、SQLite 存储等具体实现散布在框架中，导致框架难以在其他宿主（Web API、类库嵌入）中复用。

## 目标

- 框架（LuBan.AIAgent）只保留纯抽象 + Agent 运行时 + 可插拔组件
- CLI（LubanAgent）承载所有具体实现（配置持久化、Provider 路由、SQLite 存储）
- 框架移除应用级 SQLite 依赖（`SqliteLocalMemoryStore`），保留 `DatabaseToolPlugin` 的 SQLite 支持
- 消除重复代码（`ProviderModels` vs `ProviderHelper`）
- 清理死代码（`LuBanChatClient` 在框架中从未实例化）

## 移动清单

### 从框架移入 CLI

| 文件/类 | 框架位置 | CLI 目标位置 | 说明 |
|---------|---------|-------------|------|
| `ConfigManager` | `Configuration/Storage/` | `Services/` | 应用级配置管理（含 OpenAI SDK 客户端工厂） |
| `AppConfig` | `Configuration/Storage/` | `Configuration/` | 配置数据模型 |
| `ProviderConfig` | `Configuration/Storage/` | `Configuration/` | Provider 配置模型 |
| `CustomRuleConfig` | `Configuration/Storage/` | `Configuration/` | 自定义规则配置 |
| `CustomSkillConfig` | `Configuration/Storage/` | `Configuration/` | 自定义技能配置 |
| `McpServerConfig` | `Configuration/Storage/` | `Configuration/` | MCP 服务器配置 |
| `ProviderModels` | `Configuration/Storage/` | `Services/` | 合并到 `ProviderHelper` |
| `LuBanChatClient` | `Providers/` | `Services/` | 多 Provider 路由（框架中从未实例化，清理死代码） |
| `SqliteLocalMemoryStore` | `LocalMemory/` | `Infrastructure/` | SQLite 记忆存储 |
| `NGramExtractor` | `LocalMemory/` | `Infrastructure/` | 依赖 SQLite 记忆存储 |

### 框架保留

- Agent 运行时（`LuBanAgent`、`LuBanAgentFactory`、`ILuBanAgentFactory`）
- 工具插件系统 + 9 个内置插件 + 编排插件
- 规则引擎 + 3 个内置规则
- 技能系统 + 9 个内置技能
- MCP 协议（客户端、注册表、内置 MCP）
- 编排引擎（DAG 调度、规划器、子 Agent 工厂）
- 检索抽象（`IRetrievalService`、`IVectorStore`、`ICodeChunker`、13 个 Chunker）
- 基础设施（`PathGuard`、`ProcessRunner`、`PlaywrightSession`）
- `SanitizingChatClient`
- 本地记忆抽象（`ILocalMemoryStore`、`ILocalMemoryService`、`IWorkspaceContextProvider`）
- 会话抽象（`ISessionManager` 接口 + 模型）+ `SessionChatHistoryProvider`（依赖纯抽象，框架有单元测试）
- `ToolConfirmationService`
- `AIFunctionFactoryHelper`

## 接口设计

### IProviderRouter

```csharp
// 框架中定义：LuBan.AIAgent/Providers/IProviderRouter.cs
namespace LuBan.AIAgent.Providers;

public interface IProviderRouter
{
    IChatClient CreateChatClient(string? providerModel = null);
    IReadOnlyList<ProviderInfo> GetAvailableProviders();
}

public record ProviderInfo(string Name, string DisplayName, string[] Models);
```

### IAppConfigReader

```csharp
// 框架中定义：LuBan.AIAgent/Configuration/IAppConfigReader.cs
namespace LuBan.AIAgent.Configuration;

/// <summary>
/// 应用配置只读接口，供框架组件读取用户配置。
/// 框架组件不应直接依赖具体的 ConfigManager 实现。
/// </summary>
public interface IAppConfigReader
{
    List<ProviderConfigData> Providers { get; }
    string? SelectedModel { get; }
    List<CustomSkillConfigData> CustomSkills { get; }
    List<CustomRuleConfigData> CustomRules { get; }
    List<McpServerConfigData> McpServers { get; }
    List<string> DisabledBuiltinSkills { get; }
    List<string> DisabledBuiltinRules { get; }
    List<string> DisabledBuiltinMcpClients { get; }
}

public class ProviderConfigData
{
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string? Endpoint { get; set; }
    public List<string> Models { get; set; } = new();
}

public class CustomSkillConfigData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string PromptTemplate { get; set; } = "";
    public List<string> TriggerKeywords { get; set; } = new();
}

public class CustomRuleConfigData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ActionTypePattern { get; set; } = "*";
    public string TargetPattern { get; set; } = "*";
    public string Action { get; set; } = "allow";
    public int Priority { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class McpServerConfigData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "stdio";
    public string? Command { get; set; }
    public string[]? Args { get; set; }
    public string? Url { get; set; }
    public bool IsEnabled { get; set; } = true;
}
```

## 框架依赖变化

### NuGet 依赖

**保留：** `System.Data.SQLite.Core`（`DatabaseToolPlugin` 使用，与 MySqlConnector/Npgsql/SqlClient 并列）

**不移除：** 框架继续支持 SQLite 作为数据库工具的目标之一。

### 依赖反转

| 框架组件 | 原来依赖 | 改为依赖 |
|---------|---------|---------|
| `LuBanAgentFactory` | `ConfigManager` | `IProviderRouter` |
| `SkillRegistry` | `ConfigManager`（可选参数） | `IAppConfigReader`（可选参数） |
| `RuleEngine` | `ConfigManager`（可选参数） | `IAppConfigReader`（可选参数） |
| `MCPRegistry` | `ConfigManager`（可选参数） | `IAppConfigReader`（可选参数） |

### LocalMemoryService 静态耦合修复

`LocalMemoryService` 第 198 行调用 `SqliteLocalMemoryStore.ComputeContentHash()`，需要将哈希逻辑提取到 `MemoryEntry` 或独立工具类，消除对具体实现的静态依赖。

### AddLuBanAgent() 变化

- 移除 `ConfigManager` 注册（框架从未注册，由宿主在 `Program.cs` 注册）
- 移除 `SqliteLocalMemoryStore` 注册（第 100-103 行），要求宿主注册 `ILocalMemoryStore`
- 移除 `LuBanChatClient` 相关注册（框架从未注册，仅文档提及）
- 要求宿主注册 `IProviderRouter` 和 `IAppConfigReader`

## CLI 端实现

### ProviderHelper 合并

将框架的 `ProviderModels`（7 个 Provider：openai/deepseek/kimi/glm/qwen/doubao/ollama）与 CLI 的 `appsettings.json`（9 个 Provider：Kimi/MiniMax/Ark/Bailian/Hunyuan/MiMo/Azure/Claude/Gemini）合并为统一的 16 个 Provider 目录。

### ConfigManager 实现 IAppConfigReader

保留原有 JSON 文件读写逻辑（`%LocalAppData%\LuBan\AIAgent\config.json`），将 8 个只读属性适配为接口实现。

### LuBanChatClient 实现 IProviderRouter

保留原有路由逻辑，新增 `GetAvailableProviders()` 接口实现。

### CLI 新增目录结构

```
LubanAgent/
├── Configuration/          # 从框架移入的配置模型
│   ├── AppConfig.cs
│   ├── ProviderConfig.cs
│   ├── CustomRuleConfig.cs
│   ├── CustomSkillConfig.cs
│   └── McpServerConfig.cs
├── Infrastructure/         # 从框架移入的 SQLite 实现
│   ├── SqliteLocalMemoryStore.cs
│   ├── NGramExtractor.cs
│   └── DatabaseInitializer.cs (已有)
├── Services/               # 从框架移入 + 已有
│   ├── ConfigManager.cs    # 实现 IAppConfigReader
│   ├── ProviderHelper.cs   # 合并 ProviderModels
│   ├── LuBanChatClient.cs  # 实现 IProviderRouter
│   └── ...
```

## 兼容性

- `config.json` 格式不变，现有用户升级后无缝衔接
- SQLite 数据库位置、表结构不变
- `LuBan.AIAgent` 升主版本号（破坏性变更：移除 `ConfigManager`、`SqliteLocalMemoryStore`、`LuBanChatClient`）
- `LuBan.Agent.CLI` 同步升级

## 实施步骤

1. 框架新增 `IProviderRouter`、`IAppConfigReader` 接口
2. 框架 `LocalMemoryService` 提取 `ComputeContentHash` 到 `MemoryEntry`，消除静态耦合
3. 框架组件改为依赖接口（`LuBanAgentFactory`、`SkillRegistry`、`RuleEngine`、`MCPRegistry`）
4. CLI 新增接口实现（`ConfigManager` 实现 `IAppConfigReader`，`LuBanChatClient` 实现 `IProviderRouter`）
5. 框架移除旧文件（`ConfigManager`、`LuBanChatClient`、`SqliteLocalMemoryStore`、`NGramExtractor`、配置模型）
6. CLI 移入上述文件并调整命名空间
7. 更新 `AddLuBanAgent()` 扩展方法，移除 `SqliteLocalMemoryStore` 注册
8. CLI `Program.cs` 注册 `IProviderRouter`、`IAppConfigReader`、`ILocalMemoryStore`
9. 编译验证 + 运行测试
