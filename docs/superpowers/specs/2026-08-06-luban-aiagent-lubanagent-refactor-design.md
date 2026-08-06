# LuBan.AIAgent 与 LubanAgent 职责边界重构设计

## 背景

当前 `LuBan.AIAgent`（框架库）和 `LubanAgent`（CLI 应用）之间存在职责边界模糊的问题：应用级配置管理、Provider 路由、SQLite 存储等具体实现散布在框架中，导致框架难以在其他宿主（Web API、类库嵌入）中复用。

同时，框架中存在一些纯算法/工具类（如文本处理、哈希计算、通配符匹配），这些应该作为框架级工具提供给所有宿主使用，不应移入 CLI。

## 目标

- 框架（LuBan.AIAgent）只保留纯抽象 + Agent 运行时 + 可插拔组件 + 通用算法工具
- CLI（LubanAgent）承载所有应用级具体实现（配置持久化、Provider 路由、SQLite 存储）
- 框架保留并增强通用算法工具集（文本处理、向量计算、模式匹配等）
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
| `SqliteLocalMemoryStore` | `LocalMemory/` | `Infrastructure/` | SQLite 记忆存储（`ComputeContentHash` 提取到框架） |

### 框架保留并增强

#### Agent 运行时与核心抽象
- `LuBanAgent`、`LuBanAgentFactory`、`ILuBanAgentFactory`
- `ILuBanToolPlugin`、`ToolResult`、`ToolAttribute`
- `ISessionManager` 接口 + 模型
- `ILocalMemoryStore`、`ILocalMemoryService`、`IWorkspaceContextProvider`
- `IRetrievalService`、`IVectorStore`、`ICodeChunker`
- `IRule`、`IContentRule`、`ISkill`、`IMCPClient`

#### 工具插件系统
- 9 个内置工具插件 + 编排插件
- `ToolPluginRegistry`
- `AIFunctionFactoryHelper`

#### 规则引擎
- `RuleEngine`、`RuleBase`、`RuleCheckedAIFunction`
- 3 个内置规则（`BaseBehaviorRule`、`PathAccessRule`、`MemoryRecallRule`）
- `CustomRule`（`WildcardMatch` 改为 public）

#### 技能系统
- `SkillRegistry`、`SkillLoader`（`ParseSkillMd` 保留框架）、`SkillBase`
- 9 个内置技能

#### MCP 协议
- `MCPRegistry`、`MCPClientBase`、`StdioMCPClient`、`HttpMCPClient`
- `FileSystemMCPClient`

#### 编排引擎
- `Orchestrator`、`DagScheduler`、`SubAgentFactory`、`ContextStore`
- 规划器（`ITaskPlanner`、`LlmTaskPlanner`、`TemplateTaskPlanner`、`CompositeTaskPlanner`）

#### 检索系统
- `RetrievalService`、`ChunkerFactory`
- 13 个 Chunker 实现

#### 基础设施
- `PathGuard`、`ProcessRunner`、`PlaywrightSession`
- `SanitizingChatClient`
- `ToolConfirmationService`

#### 通用算法工具（框架级，所有宿主可复用）

| 工具类 | 位置 | 功能 | 来源 |
|--------|------|------|------|
| `VectorMath` | `Retrieval/` | 余弦相似度、float[] 与 byte[] 转换 | 已有 |
| `NGramExtractor` | `Utils/Text/` | FNV-1a 哈希、n-gram 提取、文本规范化 | 从 `LocalMemory/` 移入 |
| `TextUtils` | `Utils/Text/` | `ComputeContentHash`（SHA256 + 规范化） | 从 `SqliteLocalMemoryStore` 提取 |
| `WildcardMatcher` | `Utils/Text/` 或 `Rules/` | 通配符匹配算法 | 从 `CustomRule` 改为 public |
| `SkillMdParser` | `Skills/` | SKILL.md 解析（YAML frontmatter + markdown） | 从 `SkillLoader` 提取 |

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

## 框架新增工具类

### TextUtils（文本处理工具）

```csharp
// LuBan.AIAgent/Utils/Text/TextUtils.cs
namespace LuBan.AIAgent.Utils.Text;

/// <summary>
/// 文本处理工具集，提供哈希、规范化、n-gram 提取等通用算法。
/// </summary>
public static class TextUtils
{
    /// <summary>
    /// 计算规范化内容的 SHA256 哈希（用于去重）。
    /// 从 SqliteLocalMemoryStore.ComputeContentHash 提取。
    /// </summary>
    public static string ComputeContentHash(string content)
    {
        var normalized = NGramExtractor.Normalize(content);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalized)));
    }
}
```

### NGramExtractor（移入 Utils/Text/）

从 `LocalMemory/NGramExtractor.cs` 移入 `Utils/Text/NGramExtractor.cs`，功能不变。

### WildcardMatcher（通配符匹配）

```csharp
// LuBan.AIAgent/Utils/Text/WildcardMatcher.cs
namespace LuBan.AIAgent.Utils.Text;

/// <summary>
/// 通配符匹配工具，支持 * 匹配任意字符序列。
/// 从 CustomRule.WildcardMatch 改为 public 提取。
/// </summary>
public static class WildcardMatcher
{
    public static bool Match(string pattern, string value)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*")
            return true;
        if (string.IsNullOrEmpty(value))
            return false;

        pattern = pattern.ToLowerInvariant();
        value = value.ToLowerInvariant();

        var parts = pattern.Split('*');
        if (parts.Length == 1)
            return value == pattern;

        var pos = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0) continue;

            var idx = value.IndexOf(part, pos, StringComparison.Ordinal);
            if (idx < 0) return false;
            if (i == 0 && idx != 0) return false;
            pos = idx + part.Length;
        }

        var lastPart = parts[^1];
        if (lastPart.Length > 0 && !value.EndsWith(lastPart, StringComparison.Ordinal))
            return false;

        return true;
    }
}
```

### SkillMdParser（SKILL.md 解析器）

从 `SkillLoader.ParseSkillMd` 提取为独立的 `SkillMdParser` 类，保留在 `Skills/` 目录。

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

`LocalMemoryService` 第 198 行调用 `SqliteLocalMemoryStore.ComputeContentHash()`，改为调用框架级 `TextUtils.ComputeContentHash()`，消除对具体实现的静态依赖。

### CustomRule 通配符匹配公开化

`CustomRule.WildcardMatch` 从 `internal static` 改为调用 `WildcardMatcher.Match()`，或直接将 `WildcardMatcher` 作为框架级工具类。

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

### SqliteLocalMemoryStore 调整

- 从框架移入 CLI `Infrastructure/`
- `ComputeContentHash` 改为调用框架级 `TextUtils.ComputeContentHash()`
- 保留 SQLite 存储逻辑

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
- 框架新增 `TextUtils`、`WildcardMatcher` 等公共 API，向后兼容

## 实施步骤

1. 框架新增 `IProviderRouter`、`IAppConfigReader` 接口
2. 框架新增 `Utils/Text/` 目录，创建 `TextUtils`、`NGramExtractor`（从 LocalMemory 移入）、`WildcardMatcher`
3. 框架 `LocalMemoryService` 改为调用 `TextUtils.ComputeContentHash()`，消除静态耦合
4. 框架 `CustomRule` 改为调用 `WildcardMatcher.Match()`，或公开 `WildcardMatch` 方法
5. 框架 `SkillLoader` 提取 `ParseSkillMd` 为 `SkillMdParser`
6. 框架组件改为依赖接口（`LuBanAgentFactory`、`SkillRegistry`、`RuleEngine`、`MCPRegistry`）
7. CLI 新增接口实现（`ConfigManager` 实现 `IAppConfigReader`，`LuBanChatClient` 实现 `IProviderRouter`）
8. 框架移除旧文件（`ConfigManager`、`LuBanChatClient`、`SqliteLocalMemoryStore`、配置模型）
9. CLI 移入上述文件并调整命名空间
10. 更新 `AddLuBanAgent()` 扩展方法，移除 `SqliteLocalMemoryStore` 注册
11. CLI `Program.cs` 注册 `IProviderRouter`、`IAppConfigReader`、`ILocalMemoryStore`
12. 编译验证 + 运行测试
