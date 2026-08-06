# LuBan.AIAgent 与 LubanAgent 职责边界重构实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 LuBan.AIAgent 框架中的应用级代码（配置管理、Provider 路由、SQLite 存储）移入 LubanAgent CLI，同时保留并增强框架级通用算法工具。

**Architecture:** 框架通过 `IAppConfigReader` 和 `IProviderRouter` 接口与宿主解耦。CLI 实现这些接口并提供具体存储。框架新增 `Utils/Text/` 目录存放通用文本处理工具。

**Tech Stack:** .NET 8.0, C#, Microsoft.Extensions.AI, SqlSugar, ONNX Runtime

**Spec:** `docs/superpowers/specs/2026-08-06-luban-aiagent-lubanagent-refactor-design.md`

---

## 设计修正

经过代码审查，发现 spec 中有一处需要修正：

**`CustomRuleConfig`、`CustomSkillConfig`、`McpServerConfig` 必须保留在框架中**，因为框架内部的 `CustomRule`、`CustomSkill`、`StdioMCPClient`、`HttpMCPClient` 直接依赖这些类型。`IAppConfigReader` 接口对这三类使用现有的框架类型，仅对 `ProviderConfig` 使用新的 `ProviderConfigData`（因为 `ProviderConfig` 随 `ConfigManager` 移入 CLI）。

---

## 文件结构

### 框架新增文件

| 文件 | 职责 |
|------|------|
| `LuBan.AIAgent/Configuration/IAppConfigReader.cs` | 应用配置只读接口 + `ProviderConfigData` |
| `LuBan.AIAgent/Providers/IProviderRouter.cs` | Provider 路由接口 + `ProviderInfo` |
| `LuBan.AIAgent/Utils/Text/TextUtils.cs` | `ComputeContentHash` 哈希工具 |
| `LuBan.AIAgent/Utils/Text/NGramExtractor.cs` | 从 `LocalMemory/` 移入，FNV-1a + n-gram |
| `LuBan.AIAgent/Utils/Text/WildcardMatcher.cs` | 通配符匹配工具 |
| `LuBan.AIAgent/Skills/SkillMdParser.cs` | SKILL.md 解析器 |

### 框架修改文件

| 文件 | 修改内容 |
|------|---------|
| `LuBan.AIAgent/LocalMemory/LocalMemoryService.cs:197-198` | `ComputeContentHash` 改为调用 `TextUtils` |
| `LuBan.AIAgent/Rules/CustomRule.cs:104-135` | `WildcardMatch` 改为调用 `WildcardMatcher` |
| `LuBan.AIAgent/Skills/SkillRegistry.cs:16,19,69-73,220` | `ConfigManager` -> `IAppConfigReader` |
| `LuBan.AIAgent/Rules/RuleEngine.cs:12,15,71-74,213` | `ConfigManager` -> `IAppConfigReader` |
| `LuBan.AIAgent/MCP/MCPRegistry.cs:12,15,88-98,253` | `ConfigManager` -> `IAppConfigReader` |
| `LuBan.AIAgent/LuBanAgentExtensions.cs:92-105` | 移除 `SqliteLocalMemoryStore` 注册 |

### 框架删除文件

| 文件 | 原因 |
|------|------|
| `LuBan.AIAgent/Configuration/Storage/ConfigManager.cs` | 移入 CLI |
| `LuBan.AIAgent/Configuration/Storage/AppConfig.cs` | 移入 CLI |
| `LuBan.AIAgent/Configuration/Storage/ProviderConfig.cs` | 移入 CLI |
| `LuBan.AIAgent/Configuration/Storage/ProviderModels.cs` | 合并到 CLI `ProviderHelper` |
| `LuBan.AIAgent/Providers/LuBanChatClient.cs` | 移入 CLI（死代码） |
| `LuBan.AIAgent/LocalMemory/SqliteLocalMemoryStore.cs` | 移入 CLI |
| `LuBan.AIAgent/LocalMemory/NGramExtractor.cs` | 移入 `Utils/Text/` |

### CLI 新增文件

| 文件 | 职责 |
|------|------|
| `LubanAgent/Configuration/AppConfig.cs` | 从框架移入 |
| `LubanAgent/Configuration/ProviderConfig.cs` | 从框架移入 |
| `LubanAgent/Services/ConfigManager.cs` | 从框架移入，实现 `IAppConfigReader` |
| `LubanAgent/Infrastructure/SqliteLocalMemoryStore.cs` | 从框架移入 |
| `LubanAgent/Services/LuBanChatClient.cs` | 从框架移入，实现 `IProviderRouter` |

### CLI 修改文件

| 文件 | 修改内容 |
|------|---------|
| `LubanAgent/Program.cs:140-155` | 注册 `IAppConfigReader`、`IProviderRouter`、`ILocalMemoryStore` |

---

## Phase 1: 框架新增工具类

### Task 1: 创建 TextUtils + 移动 NGramExtractor

**Files:**
- Create: `LuBan.AIAgent/Utils/Text/TextUtils.cs`
- Move: `LuBan.AIAgent/LocalMemory/NGramExtractor.cs` -> `LuBan.AIAgent/Utils/Text/NGramExtractor.cs`

- [ ] **Step 1: 创建 Utils/Text 目录**

```powershell
New-Item -ItemType Directory -Path "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Utils\Text" -Force
```

- [ ] **Step 2: 移动 NGramExtractor**

```powershell
Move-Item "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LocalMemory\NGramExtractor.cs" "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Utils\Text\NGramExtractor.cs"
```

- [ ] **Step 3: 更新 NGramExtractor 命名空间**

修改 `LuBan.AIAgent/Utils/Text/NGramExtractor.cs` 第 1 行：

```csharp
// 旧：
namespace LuBan.AIAgent.LocalMemory;
// 新：
namespace LuBan.AIAgent.Utils.Text;
```

- [ ] **Step 4: 创建 TextUtils.cs**

```csharp
// LuBan.AIAgent/Utils/Text/TextUtils.cs
namespace LuBan.AIAgent.Utils.Text;

/// <summary>
/// 文本处理工具集，提供哈希、规范化等通用算法。
/// </summary>
public static class TextUtils
{
    /// <summary>
    /// 计算规范化内容的 SHA256 哈希（用于去重）。
    /// </summary>
    public static string ComputeContentHash(string content)
    {
        var normalized = NGramExtractor.Normalize(content);
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(normalized)));
    }
}
```

- [ ] **Step 5: 更新 LocalMemoryService 的 using**

修改 `LuBan.AIAgent/LocalMemory/LocalMemoryService.cs`，在文件顶部添加：

```csharp
using LuBan.AIAgent.Utils.Text;
```

- [ ] **Step 6: 修复 LocalMemoryService 静态耦合**

修改 `LuBan.AIAgent/LocalMemory/LocalMemoryService.cs` 第 197-198 行：

```csharp
// 旧：
private static string ComputeContentHash(string content)
    => SqliteLocalMemoryStore.ComputeContentHash(content);

// 新：
private static string ComputeContentHash(string content)
    => TextUtils.ComputeContentHash(content);
```

- [ ] **Step 7: 更新 SqliteLocalMemoryStore 的 using**

修改 `LuBan.AIAgent/LocalMemory/SqliteLocalMemoryStore.cs`，在文件顶部添加：

```csharp
using LuBan.AIAgent.Utils.Text;
```

- [ ] **Step 8: 更新 SqliteLocalMemoryStore.ComputeContentHash**

修改 `LuBan.AIAgent/LocalMemory/SqliteLocalMemoryStore.cs` 第 107-111 行：

```csharp
// 旧：
internal static string ComputeContentHash(string content)
{
    var normalized = NGramExtractor.Normalize(content);
    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
}

// 新：
internal static string ComputeContentHash(string content)
    => TextUtils.ComputeContentHash(content);
```

- [ ] **Step 9: 更新 GloabUsing.cs 添加 using**

修改 `LuBan.AIAgent/GloabUsing.cs`，添加：

```csharp
global using LuBan.AIAgent.Utils.Text;
```

- [ ] **Step 10: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"
```

- [ ] **Step 11: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A && git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "feat: add TextUtils, move NGramExtractor to Utils/Text/"
```

---

### Task 2: 创建 WildcardMatcher

**Files:**
- Create: `LuBan.AIAgent/Utils/Text/WildcardMatcher.cs`

- [ ] **Step 1: 创建 WildcardMatcher.cs**

```csharp
// LuBan.AIAgent/Utils/Text/WildcardMatcher.cs
namespace LuBan.AIAgent.Utils.Text;

/// <summary>
/// 通配符匹配工具，支持 * 匹配任意字符序列。
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

- [ ] **Step 2: 更新 CustomRule 使用 WildcardMatcher**

修改 `LuBan.AIAgent/Rules/CustomRule.cs`，在文件顶部添加：

```csharp
using LuBan.AIAgent.Utils.Text;
```

修改第 104-135 行的 `WildcardMatch` 方法，改为委托调用：

```csharp
// 旧：internal static bool WildcardMatch(string pattern, string value) { ... 完整实现 ... }
// 新：
internal static bool WildcardMatch(string pattern, string value)
    => WildcardMatcher.Match(pattern, value);
```

- [ ] **Step 3: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"
```

- [ ] **Step 4: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A && git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "feat: add WildcardMatcher utility, update CustomRule to delegate"
```

---

### Task 3: 创建 SkillMdParser

**Files:**
- Create: `LuBan.AIAgent/Skills/SkillMdParser.cs`

- [ ] **Step 1: 创建 SkillMdParser.cs**

从 `SkillLoader.ParseSkillMd`（第 79-152 行）提取为独立类：

```csharp
// LuBan.AIAgent/Skills/SkillMdParser.cs
namespace LuBan.AIAgent.Skills;

/// <summary>
/// SKILL.md 文件解析器。
/// 格式：YAML frontmatter（---）+ Markdown 正文（PromptTemplate）。
/// </summary>
public static class SkillMdParser
{
    /// <summary>
    /// 解析 SKILL.md 文件内容。
    /// </summary>
    public static FileSkillConfig? Parse(string content, string fallbackId, string sourcePath)
    {
        var config = new FileSkillConfig
        {
            Id = fallbackId.ToLowerInvariant(),
            SourcePath = sourcePath
        };

        var trimmed = content.TrimStart();

        if (trimmed.StartsWith("---"))
        {
            var endIndex = trimmed.IndexOf("---", 3, StringComparison.Ordinal);
            if (endIndex > 0)
            {
                var frontmatter = trimmed.Substring(3, endIndex - 3).Trim();
                var body = trimmed.Substring(endIndex + 3).Trim();

                foreach (var line in frontmatter.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var colonIndex = line.IndexOf(':');
                    if (colonIndex <= 0) continue;

                    var key = line.Substring(0, colonIndex).Trim().ToLowerInvariant();
                    var value = line.Substring(colonIndex + 1).Trim().Trim('"', '\'');

                    switch (key)
                    {
                        case "name":
                            config.Name = value;
                            break;
                        case "description":
                            config.Description = value;
                            break;
                        case "category":
                            config.Category = value;
                            break;
                        case "triggers":
                            config.TriggerKeywords = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(t => t.Trim()).ToList();
                            break;
                    }
                }

                config.PromptTemplate = body;
            }
            else
            {
                config.PromptTemplate = trimmed;
            }
        }
        else
        {
            config.PromptTemplate = trimmed;
        }

        if (string.IsNullOrEmpty(config.Name))
            config.Name = fallbackId;

        if (string.IsNullOrEmpty(config.Description))
        {
            var firstLine = config.PromptTemplate
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?
                .TrimStart('#', ' ') ?? "";
            config.Description = firstLine.Length > 100 ? firstLine.Substring(0, 100) + "..." : firstLine;
        }

        return config;
    }
}
```

- [ ] **Step 2: 更新 SkillLoader 使用 SkillMdParser**

修改 `LuBan.AIAgent/Skills/SkillLoader.cs`，将第 79-152 行的 `ParseSkillMd` 方法替换为：

```csharp
internal static FileSkillConfig? ParseSkillMd(string content, string fallbackId, string sourcePath)
    => SkillMdParser.Parse(content, fallbackId, sourcePath);
```

- [ ] **Step 3: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"
```

- [ ] **Step 4: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A && git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "feat: add SkillMdParser, update SkillLoader to delegate"
```

---

## Phase 2: 框架新增接口

### Task 4: 创建 IAppConfigReader 接口

**Files:**
- Create: `LuBan.AIAgent/Configuration/IAppConfigReader.cs`

- [ ] **Step 1: 创建 IAppConfigReader.cs**

```csharp
// LuBan.AIAgent/Configuration/IAppConfigReader.cs
namespace LuBan.AIAgent.Configuration;

/// <summary>
/// 应用配置只读接口，供框架组件读取用户配置。
/// 框架组件不应直接依赖具体的 ConfigManager 实现。
/// </summary>
public interface IAppConfigReader
{
    List<ProviderConfigData> Providers { get; }
    string? SelectedModel { get; }
    List<CustomSkillConfig> CustomSkills { get; }
    List<CustomRuleConfig> CustomRules { get; }
    List<McpServerConfig> McpServers { get; }
    List<string> DisabledBuiltinSkills { get; }
    List<string> DisabledBuiltinRules { get; }
    List<string> DisabledBuiltinMcpClients { get; }
}

/// <summary>
/// Provider 配置数据（从 CLI 层提供）
/// </summary>
public class ProviderConfigData
{
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string? Endpoint { get; set; }
    public List<string> Models { get; set; } = new();
}
```

- [ ] **Step 2: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"
```

- [ ] **Step 3: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A && git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "feat: add IAppConfigReader interface and ProviderConfigData"
```

---

### Task 5: 创建 IProviderRouter 接口

**Files:**
- Create: `LuBan.AIAgent/Providers/IProviderRouter.cs`

- [ ] **Step 1: 创建 IProviderRouter.cs**

```csharp
// LuBan.AIAgent/Providers/IProviderRouter.cs
using Microsoft.Extensions.AI;

namespace LuBan.AIAgent.Providers;

/// <summary>
/// Provider 路由接口，由宿主实现。
/// </summary>
public interface IProviderRouter
{
    IChatClient CreateChatClient(string? providerModel = null);
    IReadOnlyList<ProviderInfo> GetAvailableProviders();
}

/// <summary>
/// Provider 信息
/// </summary>
public record ProviderInfo(string Name, string DisplayName, string[] Models);
```

- [ ] **Step 2: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"
```

- [ ] **Step 3: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A && git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "feat: add IProviderRouter interface and ProviderInfo"
```

---

## Phase 3: 框架依赖反转

### Task 6: SkillRegistry 改用 IAppConfigReader

**Files:**
- Modify: `LuBan.AIAgent/Skills/SkillRegistry.cs:16,19,69-73,220`

- [ ] **Step 1: 修改 SkillRegistry 构造函数和字段**

修改 `LuBan.AIAgent/Skills/SkillRegistry.cs`：

```csharp
// 第 16 行，旧：
private readonly Configuration.ConfigManager? _configManager;
// 新：
private readonly Configuration.IAppConfigReader? _configReader;

// 第 19 行，旧：
public SkillRegistry(IEnumerable<ISkill> skills, Configuration.ConfigManager? configManager = null)
{
    _configManager = configManager;
// 新：
public SkillRegistry(IEnumerable<ISkill> skills, Configuration.IAppConfigReader? configReader = null)
{
    _configReader = configReader;
```

- [ ] **Step 2: 修改 LoadFromConfig 方法**

修改第 69-73 行：

```csharp
// 旧：
if (_configManager != null)
{
    foreach (var cfg in _configManager.CustomSkills.Where(c => c.Enabled))
        temp[cfg.Id] = new CustomSkill(cfg);
}

// 新：
if (_configReader != null)
{
    foreach (var cfg in _configReader.CustomSkills.Where(c => c.Enabled))
        temp[cfg.Id] = new CustomSkill(cfg);
}
```

- [ ] **Step 3: 修改 RebuildMerged 方法**

修改第 220 行：

```csharp
// 旧：
var disabledBuiltin = _configManager?.DisabledBuiltinSkills;
// 新：
var disabledBuiltin = _configReader?.DisabledBuiltinSkills;
```

- [ ] **Step 4: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"
```

- [ ] **Step 5: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A && git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "refactor: SkillRegistry use IAppConfigReader instead of ConfigManager"
```

---

### Task 7: RuleEngine 改用 IAppConfigReader

**Files:**
- Modify: `LuBan.AIAgent/Rules/RuleEngine.cs:12,15,71-74,213`

- [ ] **Step 1: 修改 RuleEngine 构造函数和字段**

修改 `LuBan.AIAgent/Rules/RuleEngine.cs`：

```csharp
// 第 12 行，旧：
private readonly Configuration.ConfigManager? _configManager;
// 新：
private readonly Configuration.IAppConfigReader? _configReader;

// 第 15 行，旧：
public RuleEngine(IEnumerable<IRule> rules, Configuration.ConfigManager? configManager = null)
{
    _configManager = configManager;
// 新：
public RuleEngine(IEnumerable<IRule> rules, Configuration.IAppConfigReader? configReader = null)
{
    _configReader = configReader;
```

- [ ] **Step 2: 修改 LoadFromConfig 方法**

修改第 71-74 行：

```csharp
// 旧：
if (_configManager != null)
{
    foreach (var cfg in _configManager.CustomRules)
        temp[cfg.Id] = new CustomRule(cfg);
}

// 新：
if (_configReader != null)
{
    foreach (var cfg in _configReader.CustomRules)
        temp[cfg.Id] = new CustomRule(cfg);
}
```

- [ ] **Step 3: 修改 RebuildMerged 方法**

修改第 213 行：

```csharp
// 旧：
var disabledBuiltin = _configManager?.DisabledBuiltinRules;
// 新：
var disabledBuiltin = _configReader?.DisabledBuiltinRules;
```

- [ ] **Step 4: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"
```

- [ ] **Step 5: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A && git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "refactor: RuleEngine use IAppConfigReader instead of ConfigManager"
```

---

### Task 8: MCPRegistry 改用 IAppConfigReader

**Files:**
- Modify: `LuBan.AIAgent/MCP/MCPRegistry.cs:12,15,88-98,253`

- [ ] **Step 1: 修改 MCPRegistry 构造函数和字段**

修改 `LuBan.AIAgent/MCP/MCPRegistry.cs`：

```csharp
// 第 12 行，旧：
private readonly Configuration.ConfigManager? _configManager;
// 新：
private readonly Configuration.IAppConfigReader? _configReader;

// 第 15 行，旧：
public MCPRegistry(IEnumerable<IMCPClient> clients, Configuration.ConfigManager? configManager = null)
{
    _configManager = configManager;
// 新：
public MCPRegistry(IEnumerable<IMCPClient> clients, Configuration.IAppConfigReader? configReader = null)
{
    _configReader = configReader;
```

- [ ] **Step 2: 修改 LoadFromConfig 方法**

修改第 88-98 行：

```csharp
// 旧：
if (_configManager != null)
{
    foreach (var cfg in _configManager.McpServers.Where(s => s.Enabled))
    {
        IMCPClient client = cfg.Transport?.ToLowerInvariant() switch
        {
            "http" or "sse" => new HttpMCPClient(cfg),
            _ => new StdioMCPClient(cfg)
        };
        temp[cfg.Name] = (client, FingerprintOf(cfg));
    }
}

// 新：
if (_configReader != null)
{
    foreach (var cfg in _configReader.McpServers.Where(s => s.Enabled))
    {
        IMCPClient client = cfg.Transport?.ToLowerInvariant() switch
        {
            "http" or "sse" => new HttpMCPClient(cfg),
            _ => new StdioMCPClient(cfg)
        };
        temp[cfg.Name] = (client, FingerprintOf(cfg));
    }
}
```

- [ ] **Step 3: 修改 RebuildMerged 方法**

修改第 253 行：

```csharp
// 旧：
var disabledBuiltin = _configManager?.DisabledBuiltinMcpClients;
// 新：
var disabledBuiltin = _configReader?.DisabledBuiltinMcpClients;
```

- [ ] **Step 4: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"
```

- [ ] **Step 5: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A && git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "refactor: MCPRegistry use IAppConfigReader instead of ConfigManager"
```

---

## Phase 4: 框架移除 SqliteLocalMemoryStore 注册

### Task 9: 更新 AddLuBanAgent()

**Files:**
- Modify: `LuBan.AIAgent/LuBanAgentExtensions.cs:92-105`

- [ ] **Step 1: 移除 SqliteLocalMemoryStore 注册**

修改 `LuBan.AIAgent/LuBanAgentExtensions.cs`，将第 92-105 行：

```csharp
// 旧（第 92-105 行）：
// 注册本地长期记忆（SQLite + 本地 Embedding，可选依赖 IEmbeddingGenerator）
services.Configure<LocalMemoryOptions>(configuration.GetSection("LuBanAgent:Tools:LocalMemory"));
services.AddSingleton<ILocalMemoryStore>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<LocalMemoryOptions>>().Value;
    var dbPath = opts.DatabasePath;
    if (string.IsNullOrWhiteSpace(dbPath))
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        dbPath = Path.Combine(appData, "LuBan", "AIAgent", "localmemory.db");
    }
    return new SqliteLocalMemoryStore(dbPath);
});
services.AddSingleton<ILocalMemoryService, LocalMemoryService>();

// 新：
// 注册本地长期记忆（可选依赖 IEmbeddingGenerator）
// 注意：ILocalMemoryStore 由宿主注册，框架不再默认注册 SqliteLocalMemoryStore
services.Configure<LocalMemoryOptions>(configuration.GetSection("LuBanAgent:Tools:LocalMemory"));
services.AddSingleton<ILocalMemoryService, LocalMemoryService>();
```

- [ ] **Step 2: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"
```

- [ ] **Step 3: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A && git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "refactor: remove SqliteLocalMemoryStore registration from AddLuBanAgent"
```

---

## Phase 5: 框架移除旧文件

### Task 10: 移除框架中的应用级文件

**Files:**
- Delete: `LuBan.AIAgent/Configuration/Storage/ConfigManager.cs`
- Delete: `LuBan.AIAgent/Configuration/Storage/AppConfig.cs`
- Delete: `LuBan.AIAgent/Configuration/Storage/ProviderConfig.cs`
- Delete: `LuBan.AIAgent/Configuration/Storage/ProviderModels.cs`
- Delete: `LuBan.AIAgent/Providers/LuBanChatClient.cs`
- Delete: `LuBan.AIAgent/LocalMemory/SqliteLocalMemoryStore.cs`

- [ ] **Step 1: 删除文件**

```powershell
$base = "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent"
Remove-Item "$base/Configuration/Storage/ConfigManager.cs"
Remove-Item "$base/Configuration/Storage/AppConfig.cs"
Remove-Item "$base/Configuration/Storage/ProviderConfig.cs"
Remove-Item "$base/Configuration/Storage/ProviderModels.cs"
Remove-Item "$base/Providers/LuBanChatClient.cs"
Remove-Item "$base/LocalMemory/SqliteLocalMemoryStore.cs"
```

- [ ] **Step 2: 编译验证框架**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj"
```

Expected: 编译成功（CLI 暂时会报错，下一步修复）

- [ ] **Step 3: 提交框架**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A && git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "refactor: remove ConfigManager, LuBanChatClient, SqliteLocalMemoryStore, AppConfig, ProviderConfig, ProviderModels from framework"
```

---

## Phase 6: CLI 移入文件并创建实现

### Task 11: CLI 创建 Configuration 目录并移入配置模型

**Files:**
- Create: `LubanAgent/Configuration/AppConfig.cs`
- Create: `LubanAgent/Configuration/ProviderConfig.cs`

- [ ] **Step 1: 创建 Configuration 目录**

```powershell
New-Item -ItemType Directory -Path "D:\WorkBench\Walle\luban\luban-agent\Configuration" -Force
```

- [ ] **Step 2: 创建 AppConfig.cs**

从框架的 `Configuration/Storage/AppConfig.cs` 复制，修改命名空间：

```csharp
// LubanAgent/Configuration/AppConfig.cs
namespace LubanAgent.Configuration;

/// <summary>
/// 应用配置（config.json 的完整结构）
/// </summary>
public class AppConfig
{
    public List<ProviderConfig> Providers { get; set; } = new();
    public string? SelectedModel { get; set; }
    public List<LuBan.AIAgent.Configuration.CustomSkillConfig> CustomSkills { get; set; } = new();
    public List<LuBan.AIAgent.Configuration.CustomRuleConfig> CustomRules { get; set; } = new();
    public List<LuBan.AIAgent.Configuration.McpServerConfig> McpServers { get; set; } = new();
    public List<string> DisabledBuiltinSkills { get; set; } = new();
    public List<string> DisabledBuiltinRules { get; set; } = new();
    public List<string> DisabledBuiltinMcpClients { get; set; } = new();
}
```

- [ ] **Step 3: 创建 ProviderConfig.cs**

从框架的 `Configuration/Storage/ProviderConfig.cs` 复制，修改命名空间：

```csharp
// LubanAgent/Configuration/ProviderConfig.cs
namespace LubanAgent.Configuration;

/// <summary>
/// Provider 配置
/// </summary>
public class ProviderConfig
{
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string? Endpoint { get; set; }
    public List<string> Models { get; set; } = new();
}
```

- [ ] **Step 4: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-agent" add -A && git -C "D:\WorkBench\Walle\luban\luban-agent" commit -m "feat: add Configuration/AppConfig.cs and ProviderConfig.cs"
```

---

### Task 12: CLI 创建 ConfigManager 实现 IAppConfigReader

**Files:**
- Create: `LubanAgent/Services/ConfigManager.cs`

- [ ] **Step 1: 创建 ConfigManager.cs**

从框架的 `Configuration/Storage/ConfigManager.cs` 复制，修改命名空间，实现 `IAppConfigReader`：

```csharp
// LubanAgent/Services/ConfigManager.cs
using LuBan.AIAgent.Configuration;
using LubanAgent.Configuration;

namespace LubanAgent.Services;

/// <summary>
/// 应用配置管理器，实现 IAppConfigReader 接口。
/// 管理 config.json 的读写和 CRUD 操作。
/// </summary>
public class ConfigManager : IAppConfigReader
{
    private readonly string _configPath;
    private AppConfig _config = new();

    // IAppConfigReader 实现
    List<ProviderConfigData> IAppConfigReader.Providers =>
        _config.Providers.Select(p => new ProviderConfigData
        {
            Name = p.Name,
            ApiKey = p.ApiKey,
            Endpoint = p.Endpoint,
            Models = p.Models
        }).ToList();

    string? IAppConfigReader.SelectedModel => _config.SelectedModel;
    List<CustomSkillConfig> IAppConfigReader.CustomSkills => _config.CustomSkills;
    List<CustomRuleConfig> IAppConfigReader.CustomRules => _config.CustomRules;
    List<McpServerConfig> IAppConfigReader.McpServers => _config.McpServers;
    List<string> IAppConfigReader.DisabledBuiltinSkills => _config.DisabledBuiltinSkills;
    List<string> IAppConfigReader.DisabledBuiltinRules => _config.DisabledBuiltinRules;
    List<string> IAppConfigReader.DisabledBuiltinMcpClients => _config.DisabledBuiltinMcpClients;

    // 保留原有的公开属性供 CLI 命令使用
    public List<ProviderConfig> Providers => _config.Providers;
    public string? SelectedModel => _config.SelectedModel;
    public List<CustomSkillConfig> CustomSkills => _config.CustomSkills;
    public List<CustomRuleConfig> CustomRules => _config.CustomRules;
    public List<McpServerConfig> McpServers => _config.McpServers;
    public List<string> DisabledBuiltinSkills => _config.DisabledBuiltinSkills;
    public List<string> DisabledBuiltinRules => _config.DisabledBuiltinRules;
    public List<string> DisabledBuiltinMcpClients => _config.DisabledBuiltinMcpClients;
    public bool HasSelectedModel => !string.IsNullOrEmpty(_config.SelectedModel);

    public ConfigManager(string configPath)
    {
        _configPath = configPath;
    }

    public void Load()
    {
        if (!File.Exists(_configPath))
        {
            _config = new AppConfig();
            return;
        }
        try
        {
            var json = File.ReadAllText(_configPath);
            _config = json.ToObject<AppConfig>() ?? new AppConfig();
        }
        catch
        {
            _config = new AppConfig();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_configPath, _config.ToJson(true));
    }

    public static string GetDefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "LuBan", "AIAgent", "config.json");
    }

    // 保留原有的 CRUD 方法（AddProvider, SetSelectedModel, AddCustomSkill 等）
    // 从框架的 ConfigManager.cs 复制所有 CRUD 方法，将 AppConfig 替换为 LubanAgent.Configuration.AppConfig
    // ... (此处省略，实际实施时从框架 ConfigManager.cs 复制所有 CRUD 方法)

    public IChatClient CreateChatClient()
    {
        // 从框架的 ConfigManager.CreateChatClient() 复制
        // ... (此处省略，实际实施时从框架复制)
        throw new NotImplementedException("从框架 ConfigManager.CreateChatClient() 复制");
    }
}
```

**注意：** 实际实施时，需要从框架的 `ConfigManager.cs` 完整复制所有 CRUD 方法和 `CreateChatClient()` 方法。此处为简化展示。

- [ ] **Step 2: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-agent\LubanAgent.csproj"
```

- [ ] **Step 3: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-agent" add -A && git -C "D:\WorkBench\Walle\luban\luban-agent" commit -m "feat: add ConfigManager implementing IAppConfigReader"
```

---

### Task 13: CLI 创建 SqliteLocalMemoryStore

**Files:**
- Create: `LubanAgent/Infrastructure/SqliteLocalMemoryStore.cs`

- [ ] **Step 1: 创建 SqliteLocalMemoryStore.cs**

从框架的 `LocalMemory/SqliteLocalMemoryStore.cs` 完整复制，修改命名空间：

```csharp
// LubanAgent/Infrastructure/SqliteLocalMemoryStore.cs
using System.Data;
using System.Data.SQLite;
using LuBan.AIAgent.LocalMemory;
using LuBan.AIAgent.Utils.Text;

namespace LubanAgent.Infrastructure;

/// <summary>
/// 基于 SQLite 的本地记忆存储实现
/// </summary>
public class SqliteLocalMemoryStore : ILocalMemoryStore, IDisposable
{
    // 从框架的 SqliteLocalMemoryStore.cs 完整复制所有代码
    // 将 ComputeContentHash 改为调用 TextUtils.ComputeContentHash()
    // ... (此处省略，实际实施时从框架完整复制)
}
```

- [ ] **Step 2: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-agent\LubanAgent.csproj"
```

- [ ] **Step 3: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-agent" add -A && git -C "D:\WorkBench\Walle\luban\luban-agent" commit -m "feat: add SqliteLocalMemoryStore to CLI Infrastructure"
```

---

### Task 14: CLI 创建 LuBanChatClient 实现 IProviderRouter

**Files:**
- Create: `LubanAgent/Services/LuBanChatClient.cs`

- [ ] **Step 1: 创建 LuBanChatClient.cs**

从框架的 `Providers/LuBanChatClient.cs` 复制，修改命名空间，实现 `IProviderRouter`：

```csharp
// LubanAgent/Services/LuBanChatClient.cs
using LuBan.AIAgent.Providers;
using Microsoft.Extensions.AI;

namespace LubanAgent.Services;

/// <summary>
/// 多 Provider 路由客户端，实现 IProviderRouter 和 IChatClient。
/// </summary>
public class LuBanChatClient : IProviderRouter, IChatClient
{
    private readonly Dictionary<string, IChatClient> _providers;
    private readonly string _defaultProvider;

    public LuBanChatClient(
        IEnumerable<KeyValuePair<string, IChatClient>> providers,
        string defaultProvider = "openai")
    {
        _providers = new Dictionary<string, IChatClient>(providers, StringComparer.OrdinalIgnoreCase);
        _defaultProvider = defaultProvider;
    }

    // IProviderRouter 实现
    public IChatClient CreateChatClient(string? providerModel = null)
    {
        if (string.IsNullOrEmpty(providerModel))
            return _providers.TryGetValue(_defaultProvider, out var defaultClient)
                ? defaultClient
                : _providers.Values.First();

        var colonIndex = providerModel.IndexOf(':');
        var providerName = colonIndex > 0 ? providerModel[..colonIndex] : _defaultProvider;

        return _providers.TryGetValue(providerName, out var client)
            ? client
            : throw new ArgumentException($"未知的 Provider: {providerName}");
    }

    public IReadOnlyList<ProviderInfo> GetAvailableProviders()
    {
        return _providers.Keys.Select(k => new ProviderInfo(k, k, Array.Empty<string>())).ToList();
    }

    // IChatClient 实现（从框架的 LuBanChatClient.cs 复制）
    // ... (此处省略，实际实施时从框架复制 GetResponseAsync, GetStreamingResponseAsync 等)
}
```

- [ ] **Step 2: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-agent\LubanAgent.csproj"
```

- [ ] **Step 3: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-agent" add -A && git -C "D:\WorkBench\Walle\luban\luban-agent" commit -m "feat: add LuBanChatClient implementing IProviderRouter"
```

---

### Task 15: 更新 CLI Program.cs DI 注册

**Files:**
- Modify: `LubanAgent/Program.cs:140-155`

- [ ] **Step 1: 更新 Program.cs**

修改 `LubanAgent/Program.cs` 第 140-155 行：

```csharp
// 旧（第 140-153 行）：
var configPath = ConfigManager.GetDefaultConfigPath();
var configManager = new ConfigManager(configPath);
configManager.Load();
services.AddSingleton(configManager);

// 注册 LuBan 文件日志
services.AddLogging(builder => builder.AddLuBanFileLogger());

// 注册 IChatClient，使用 ConfigManager 动态创建
services.AddScoped<IChatClient>(sp =>
{
    var cm = sp.GetRequiredService<ConfigManager>();
    return cm.CreateChatClient();
});

services.AddLuBanAgent(configuration);

// 新：
var configPath = ConfigManager.GetDefaultConfigPath();
var configManager = new ConfigManager(configPath);
configManager.Load();
services.AddSingleton(configManager);

// 注册 IAppConfigReader（ConfigManager 已实现）
services.AddSingleton<LuBan.AIAgent.Configuration.IAppConfigReader>(configManager);

// 注册 LuBan 文件日志
services.AddLogging(builder => builder.AddLuBanFileLogger());

// 注册 IChatClient，使用 ConfigManager 动态创建
services.AddScoped<IChatClient>(sp =>
{
    var cm = sp.GetRequiredService<ConfigManager>();
    return cm.CreateChatClient();
});

// 注册 IProviderRouter
services.AddSingleton<LuBan.AIAgent.Providers.IProviderRouter>(sp =>
{
    var cm = sp.GetRequiredService<ConfigManager>();
    // 创建 providers 字典
    var providers = new Dictionary<string, IChatClient>(StringComparer.OrdinalIgnoreCase);
    foreach (var p in cm.Providers)
    {
        if (!string.IsNullOrEmpty(p.ApiKey))
        {
            var endpoint = p.Endpoint ?? "https://api.openai.com/v1";
            var openAiClient = new OpenAI.OpenAIClient(p.ApiKey, new OpenAI.OpenAIClientOptions { Endpoint = new Uri(endpoint) });
            providers[p.Name] = openAiClient.AsIChatClient();
        }
    }
    return new LuBanChatClient(providers);
});

// 注册 ILocalMemoryStore（从框架移除，由 CLI 注册）
services.AddSingleton<LuBan.AIAgent.LocalMemory.ILocalMemoryStore>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<LuBan.AIAgent.LocalMemoryOptions>>().Value;
    var dbPath = opts.DatabasePath;
    if (string.IsNullOrWhiteSpace(dbPath))
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        dbPath = Path.Combine(appData, "LuBan", "AIAgent", "localmemory.db");
    }
    return new LubanAgent.Infrastructure.SqliteLocalMemoryStore(dbPath);
});

services.AddLuBanAgent(configuration);
```

- [ ] **Step 2: 编译验证**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-agent\LubanAgent.csproj"
```

- [ ] **Step 3: 提交**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-agent" add -A && git -C "D:\WorkBench\Walle\luban\luban-agent" commit -m "refactor: update Program.cs to register IAppConfigReader, IProviderRouter, ILocalMemoryStore"
```

---

## Phase 7: 最终验证

### Task 16: 全量编译验证

- [ ] **Step 1: 编译框架**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj" --configuration Release
```

Expected: 编译成功，0 errors

- [ ] **Step 2: 编译 CLI**

```powershell
dotnet build "D:\WorkBench\Walle\luban\luban-agent\LubanAgent.csproj" --configuration Release
```

Expected: 编译成功，0 errors

- [ ] **Step 3: 运行 CLI 验证基本功能**

```powershell
dotnet run --project "D:\WorkBench\Walle\luban\luban-agent\LubanAgent.csproj" -- /provider -list
```

Expected: 显示 Provider 列表

- [ ] **Step 4: 提交最终状态**

```powershell
git -C "D:\WorkBench\Walle\luban\luban-framework" add -A && git -C "D:\WorkBench\Walle\luban\luban-framework" commit -m "chore: final verification after boundary refactoring"
git -C "D:\WorkBench\Walle\luban\luban-agent" add -A && git -C "D:\WorkBench\Walle\luban\luban-agent" commit -m "chore: final verification after boundary refactoring"
```

---

## 回滚计划

如果实施过程中遇到无法解决的问题，可以回滚：

```powershell
# 框架回滚
git -C "D:\WorkBench\Walle\luban\luban-framework" reset --hard HEAD~10

# CLI 回滚
git -C "D:\WorkBench\Walle\luban\luban-agent" reset --hard HEAD~6
```

---

## 检查清单

- [ ] 框架新增 `IAppConfigReader` 接口
- [ ] 框架新增 `IProviderRouter` 接口
- [ ] 框架新增 `Utils/Text/TextUtils.cs`
- [ ] 框架新增 `Utils/Text/NGramExtractor.cs`（从 LocalMemory 移入）
- [ ] 框架新增 `Utils/Text/WildcardMatcher.cs`
- [ ] 框架新增 `Skills/SkillMdParser.cs`
- [ ] 框架 `LocalMemoryService` 使用 `TextUtils.ComputeContentHash()`
- [ ] 框架 `CustomRule` 使用 `WildcardMatcher.Match()`
- [ ] 框架 `SkillRegistry` 使用 `IAppConfigReader`
- [ ] 框架 `RuleEngine` 使用 `IAppConfigReader`
- [ ] 框架 `MCPRegistry` 使用 `IAppConfigReader`
- [ ] 框架 `AddLuBanAgent()` 移除 `SqliteLocalMemoryStore` 注册
- [ ] 框架移除 `ConfigManager`、`LuBanChatClient`、`SqliteLocalMemoryStore`、`AppConfig`、`ProviderConfig`、`ProviderModels`
- [ ] CLI 新增 `Configuration/AppConfig.cs`、`Configuration/ProviderConfig.cs`
- [ ] CLI 新增 `Services/ConfigManager.cs` 实现 `IAppConfigReader`
- [ ] CLI 新增 `Infrastructure/SqliteLocalMemoryStore.cs`
- [ ] CLI 新增 `Services/LuBanChatClient.cs` 实现 `IProviderRouter`
- [ ] CLI `Program.cs` 注册 `IAppConfigReader`、`IProviderRouter`、`ILocalMemoryStore`
- [ ] 框架编译成功
- [ ] CLI 编译成功
- [ ] CLI 运行 `/provider -list` 正常
