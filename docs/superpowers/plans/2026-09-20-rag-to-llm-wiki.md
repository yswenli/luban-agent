# RAG → LLM Wiki 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把现有"每次对话重新检索"的 RAG 模式升级为 LLM Wiki 模式：LLM 增量构建并维护持久化的 `wiki/` 知识库，查询时优先读 wiki、按需回落 raw 溯源。

**Architecture:** 三层数据（raw 工作区根 / `wiki/` 由 LLM 独占写 / schema 由内置 `RagAgentProfile` 系统提示词承载，`<root>/wiki/SCHEMA.md` 覆盖）。确定性工作（路径布局、index/log 维护、链接校验、增量重索引）由框架 `WikiService` 完成；创作由 LLM 通过 6 个 `wiki.*` 工具完成。wiki 工具组为 **opt-in**，普通工作区自动不含。

**Tech Stack:** .NET 10 / C#、Microsoft.Extensions.AI、MiniExcel 1.46.0、MSTest、SQLite 向量库（既有）。

**Spec:** `D:\WorkBench\Walle\luban\luban-agent\docs\superpowers\specs\2026-09-20-rag-to-llm-wiki-design.md`

---

## File Structure

### 框架 `luban-framework/LuBan.AIAgent/`（新目录 `Wiki/`、`Tools/Wiki/`）

| 文件 | 职责 |
|------|------|
| `Abstractions/ILuBanToolPlugin.cs` (改) | 加 `IsOptIn` 默认成员 |
| `Abstractions/ToolPluginRegistry.cs` (改) | `GetPlugins(null)` 过滤 opt-in |
| `Retrieval/IRetrievalService.cs` (改) | 加 `IndexFileAsync` / `RemoveAsync` |
| `Retrieval/RetrievalService.cs` (改) | 上述两个方法实现 |
| `Retrieval/Chunkers/ChunkerFactory.cs` (改) | 语言映射补 `.txt/.csv/.tsv/.jsonl/.ndjson` |
| `Configuration/WikiToolOptions.cs` (新) | wiki 工具组配置 |
| `Configuration/ToolGroupOptions.cs` (改) | 加 `Wiki` 成员 |
| `Wiki/Models.cs` (新) | `WikiPage` / `WikiIndex` / `WikiIndexEntry` / lint / stats |
| `Wiki/IWikiContext.cs` (新) | 宿主提供工作区根 |
| `Wiki/WikiFrontmatter.cs` (新) | frontmatter 解析/渲染 |
| `Wiki/WikiPageSerializer.cs` (新) | `WikiPage` ↔ markdown |
| `Wiki/WikiSlug.cs` (新) | 标题 → 文件名/唯一性 |
| `Wiki/WikiDefaults.cs` (新) | 内置 schema 文案 |
| `Wiki/IWikiService.cs` + `Wiki/WikiService.cs` (新) | wiki 核心服务 |
| `Wiki/Extractors/*.cs` (新) | `ISourceExtractor` + 注册表 + 8 个提取器 |
| `Tools/Wiki/WikiToolGroup.cs` + `Tools/Wiki/WikiToolPlugin.cs` (新) | LLM 工具 |
| `LuBanAgentExtensions.cs` (改) | 注册 `WikiToolPlugin` |
| `LuBan.AIAgent.csproj` (改) | 加 MiniExcel + 递增版本 |
| `LuBan.XTestProject/WikiUnitTest.cs` (新) | wiki/opt-in 测试 |
| `README.md` / `README.en.md` (改) | 成对更新 |

### Agent `luban-agent/`

| 文件 | 职责 |
|------|------|
| `LubanAgentCore/Agents/WorkspaceWikiContext.cs` (新) | `IWikiContext` 实现 |
| `LubanAgentCore/Hosting/AgentHostBuilder.cs` (改) | `embedder != null` 块内注册 wiki 服务 |
| `LubanAgentCore/Agents/RagAgentProfile.cs` (改) | 启用 wiki 组、`RetrievalMode=null`、schema、`rag-config.json.wiki` |
| `LubanAgentCore/LubanAgentCore.csproj` (改) | 提升 `LuBan.AIAgent` 版本号 |
| `LubanAgentCli/Commands/WikiCommand.cs` (新) | `/wiki` |
| `LubanAgentCli/App/ViewModels/CommandViewModel.cs` (改) | 路由 `/wiki` |
| `LubanAgentCore/Commands/CommandCatalog.cs` (改) | 注册 `/wiki` |
| `LubanAgentCodex/Views/RagManageWindow.axaml` + `.cs` (改) | 桌面端 wiki 入口 |
| `LubanAgentCli/docs/TUI-SMOKE-TEST.md` (改) | 冒烟步骤 |

---

## 阶段 A：框架基座（`luban-framework`）

所有命令默认在 `D:\WorkBench\Walle\luban\luban-framework` 下执行。

### Task 1: opt-in 工具组门控

**Files:**
- Modify: `LuBan.AIAgent/Abstractions/ILuBanToolPlugin.cs`
- Modify: `LuBan.AIAgent/Abstractions/ToolPluginRegistry.cs`
- Create: `LuBan.XTestProject/WikiUnitTest.cs`

- [ ] **Step 1: 写失败测试** — 新建 `LuBan.XTestProject/WikiUnitTest.cs`

```csharp
using System.Text.Json;
using LuBan.AIAgent;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace LuBan.XTestProject;

[TestClass]
public class WikiUnitTest
{
    private sealed class FakePlugin : ILuBanToolPlugin
    {
        public FakePlugin(string name, bool optIn) { GroupName = name; IsOptIn = optIn; }
        public string GroupName { get; }
        public string? Description => null;
        public bool IsOptIn { get; }
        public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
            => Array.Empty<AIFunction>();
        public bool IsEnabled(LuBanAgentOptions options) => true;
    }

    [TestMethod]
    public void GetPlugins_NullGroupNames_ExcludesOptIn()
    {
        var registry = new ToolPluginRegistry(
            new ILuBanToolPlugin[] { new FakePlugin("filesystem", false), new FakePlugin("wiki", true) },
            Options.Create(new LuBanAgentOptions()));

        var all = registry.GetPlugins(null);
        Assert.IsFalse(all.Any(p => p.GroupName == "wiki"), "ToolGroups=null 时不应包含 opt-in 工具组");
        Assert.IsTrue(all.Any(p => p.GroupName == "filesystem"));

        var explicitWiki = registry.GetPlugins(new[] { "wiki" });
        Assert.IsTrue(explicitWiki.Any(p => p.GroupName == "wiki"), "显式点名时应包含 opt-in 工具组");
    }
}
```

- [ ] **Step 2: 跑测试确认失败** — `dotnet test LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~GetPlugins_NullGroupNames_ExcludesOptIn"`，预期编译失败（`IsOptIn` 不存在）。

- [ ] **Step 3: 加接口默认成员** — 在 `ILuBanToolPlugin.cs` 的接口体内加（放在 `IsEnabled` 之前）：

```csharp
    /// <summary>
    /// 是否为 opt-in 工具组：为 true 时，仅当 profile 显式在 ToolGroups 中点名才注入；
    /// ToolGroups 为 null（表示"全部"）时不包含本组。默认 false。
    /// </summary>
    bool IsOptIn => false;
```

- [ ] **Step 4: 改注册表过滤** — 打开 `ToolPluginRegistry.cs`，找到 `GetPlugins(string[]? groupNames)`（约 L63-71），当 `groupNames == null` 时排除 opt-in：

```csharp
        if (groupNames == null)
            return GetEnabledPlugins().Where(p => !p.IsOptIn).ToList();
```

保留其原有按名筛选分支不变。

- [ ] **Step 5: 跑测试确认通过** — 同 Step 2 命令，预期 1 passed。

- [ ] **Step 6: 提交**

```
git add LuBan.AIAgent/Abstractions/ILuBanToolPlugin.cs LuBan.AIAgent/Abstractions/ToolPluginRegistry.cs LuBan.XTestProject/WikiUnitTest.cs
git commit -m "feat: 工具插件支持 opt-in 门控，ToolGroups=null 时排除"
```

---

### Task 2: `IRetrievalService` 单文件索引/删除

**Files:**
- Modify: `LuBan.AIAgent/Retrieval/IRetrievalService.cs`
- Modify: `LuBan.AIAgent/Retrieval/RetrievalService.cs`

> 不写自动化测试：构造 `RetrievalService` 需要真实 `IEmbeddingGenerator`；行为由 Task 13 的 `FakeRetrievalService` 与冒烟覆盖（spec §8.1）。

- [ ] **Step 1: 扩接口** — 在 `IRetrievalService` 加：

```csharp
    /// <summary>
    /// 索引/刷新单个文件（复用单文件提取 + 切块 + Upsert 逻辑）。
    /// </summary>
    /// <param name="path">文件绝对路径。</param>
    /// <param name="force">为 true 时忽略哈希未变化直接重建切块。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IndexReport> IndexFileAsync(string path, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按来源名（即索引时传入的 <c>FilePath</c>）软删除该文件的全部切块。
    /// </summary>
    /// <param name="sourceName">来源名，通常为该文件的绝对路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RemoveAsync(string sourceName, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: 实现** — 在 `RetrievalService.cs` 加（`SHA256`/`Encoding` 的 using 参照本文件既有实现；写锁字段名以文件内既有为准，下面沿用 `_rwLock`）：

```csharp
    /// <inheritdoc />
    public async Task<IndexReport> IndexFileAsync(string path, bool force = false, CancellationToken cancellationToken = default)
    {
        using var _ = await _rwLock.WriteLockAsync(cancellationToken);
        var fullPath = Path.GetFullPath(path);
        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        var existing = await _store.GetFilesAsync(fullPath);
        var old = existing.FirstOrDefault(f => string.Equals(f.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (!force && old != null && string.Equals(old.FileHash, hash, StringComparison.OrdinalIgnoreCase))
            return new IndexReport { ScannedFiles = 1, SkippedFiles = 1 };

        var language = _chunkers.GetLanguage(fullPath);
        return await IndexSingleContentAsync(content, language, fullPath, hash, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string sourceName, CancellationToken cancellationToken = default)
    {
        using var _ = await _rwLock.WriteLockAsync(cancellationToken);
        var existing = await _store.GetFilesAsync(null);
        foreach (var file in existing.Where(f => string.Equals(f.FilePath, sourceName, StringComparison.OrdinalIgnoreCase)))
            await _store.SoftDeleteFileAsync(file.Id);
    }
```

若 `IndexSingleContentAsync` 的实参/返回类型与上面不一致，以文件内既有签名为准对齐（它已存在于 `IndexDirectoryAsync` 内部调用链）。

- [ ] **Step 3: 构建验证** — `dotnet build LuBan.AIAgent/LuBan.AIAgent.csproj`，预期 0 error。

- [ ] **Step 4: 提交**

```
git add LuBan.AIAgent/Retrieval/IRetrievalService.cs LuBan.AIAgent/Retrieval/RetrievalService.cs
git commit -m "feat: 检索服务支持单文件索引与按来源软删除"
```

---

### Task 3: `ChunkerFactory` 语言映射补充

**Files:**
- Modify: `LuBan.AIAgent/Retrieval/Chunkers/ChunkerFactory.cs`

- [ ] **Step 1: 改映射** — 在 `ChunkerFactory.cs` 的语言映射字典（约 L120-131）补：

```csharp
            [".txt"] = "text",
            [".csv"] = "text",
            [".tsv"] = "text",
            [".jsonl"] = "json",
            [".ndjson"] = "json",
```

`.xlsx/.xls` 保持被 `LooksBinary`（L84）排除，不动。

- [ ] **Step 2: 构建验证** — `dotnet build LuBan.AIAgent/LuBan.AIAgent.csproj`，0 error。

- [ ] **Step 3: 提交**

```
git add LuBan.AIAgent/Retrieval/Chunkers/ChunkerFactory.cs
git commit -m "feat: 分块器补充文本/表格/jsonl 语言映射"
```

---

### Task 4: wiki 工具组配置

**Files:**
- Create: `LuBan.AIAgent/Configuration/WikiToolOptions.cs`
- Modify: `LuBan.AIAgent/Configuration/ToolGroupOptions.cs`

- [ ] **Step 1: 新建选项类**

```csharp
namespace LuBan.AIAgent.Configuration;

/// <summary>
/// Wiki 工具组配置（opt-in，默认关闭，由 Rag profile 显式启用）。
/// </summary>
public class WikiToolOptions
{
    /// <summary>
    /// 是否启用 wiki 工具组。默认 false，开启后 Rag 工作区才注入 wiki 工具。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 默认搜索返回页数。
    /// </summary>
    public int TopK { get; set; } = 8;

    /// <summary>
    /// 搜索默认是否回落到 raw（工作区根）溯源。
    /// </summary>
    public bool IncludeRawDefault { get; set; }

    /// <summary>
    /// 单次搜索结果最大字符数。
    /// </summary>
    public int MaxResultChars { get; set; } = 8000;
}
```

- [ ] **Step 2: 挂到 `ToolGroupOptions`** — 在 `ToolGroupOptions.cs` 加属性（放在 `LocalMemory` 旁，风格一致）：

```csharp
    /// <summary>
    /// Wiki 工具组选项（opt-in）。
    /// </summary>
    public WikiToolOptions Wiki { get; set; } = new();
```

- [ ] **Step 3: 构建验证** — `dotnet build LuBan.AIAgent/LuBan.AIAgent.csproj`，0 error。

- [ ] **Step 4: 提交**

```
git add LuBan.AIAgent/Configuration/WikiToolOptions.cs LuBan.AIAgent/Configuration/ToolGroupOptions.cs
git commit -m "feat: 新增 wiki 工具组配置"
```

---

### Task 5: wiki 模型与上下文接口

**Files:**
- Create: `LuBan.AIAgent/Wiki/Models.cs`
- Create: `LuBan.AIAgent/Wiki/IWikiContext.cs`

- [ ] **Step 1: 写模型** — `Models.cs`（命名空间 `LuBan.AIAgent.Wiki`）

```csharp
using System.Text.RegularExpressions;

namespace LuBan.AIAgent.Wiki;

/// <summary>wiki 页面（已解析 frontmatter）。</summary>
public class WikiPage
{
    /// <summary>相对 wiki 根的文件路径，统一用 '/' 分隔（如 sources/xxx.md）。</summary>
    public string RelativePath { get; set; } = "";
    /// <summary>标题。</summary>
    public string Title { get; set; } = "";
    /// <summary>页面类型：source|entity|concept|overview|query。</summary>
    public string Type { get; set; } = "source";
    /// <summary>标签。</summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>来源路径（相对工作区根，或 http 链接）。</summary>
    public List<string> Sources { get; set; } = new();
    /// <summary>正文（不含 frontmatter）。</summary>
    public string Body { get; set; } = "";
    /// <summary>供 index.md 使用的摘要（可空）。</summary>
    public string? IndexSummary { get; set; }
    /// <summary>创建日期。</summary>
    public DateTime? Created { get; set; }
    /// <summary>更新日期。</summary>
    public DateTime? Updated { get; set; }
}

/// <summary>index.md 的一个条目。</summary>
public class WikiIndexEntry
{
    /// <summary>分类（目录名或 "pages"）。</summary>
    public string Category { get; set; } = "pages";
    /// <summary>相对路径。</summary>
    public string RelativePath { get; set; } = "";
    /// <summary>标题。</summary>
    public string Title { get; set; } = "";
    /// <summary>摘要。</summary>
    public string? Summary { get; set; }
    /// <summary>更新时间。</summary>
    public DateTime? Updated { get; set; }
}

/// <summary>index.md 的内存模型。</summary>
public class WikiIndex
{
    /// <summary>条目集合。</summary>
    public List<WikiIndexEntry> Entries { get; } = new();

    /// <summary>按相对路径 upsert 条目。</summary>
    public void Upsert(string relativePath, WikiPage page)
    {
        Entries.RemoveAll(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        Entries.Add(new WikiIndexEntry
        {
            Category = CategoryOf(relativePath),
            RelativePath = relativePath,
            Title = page.Title,
            Summary = page.IndexSummary,
            Updated = page.Updated
        });
    }

    /// <summary>移除条目。</summary>
    public void Remove(string relativePath)
        => Entries.RemoveAll(e => string.Equals(e.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>取分类：一级目录名，根目录页面归 "pages"。</summary>
    public static string CategoryOf(string relativePath)
    {
        var idx = relativePath.IndexOf('/');
        return idx > 0 ? relativePath[..idx] : "pages";
    }

    /// <summary>解析 index.md。</summary>
    public static WikiIndex Parse(string markdown)
    {
        var index = new WikiIndex();
        var category = "pages";
        var regex = new Regex(@"^\s*-\s*\[(?<title>[^\]]+)\]\((?<path>[^)]+)\)");
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("## ")) { category = line[3..].Trim(); continue; }
            var m = regex.Match(line);
            if (!m.Success) continue;
            index.Entries.Add(new WikiIndexEntry
            {
                Category = category,
                RelativePath = m.Groups["path"].Value,
                Title = m.Groups["title"].Value
            });
        }
        return index;
    }

    /// <summary>渲染 index.md。</summary>
    public string Render()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("# Wiki Index\n\n");
        foreach (var group in Entries.GroupBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("## ").Append(group.Key).Append('\n');
            foreach (var e in group.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("- [").Append(e.Title).Append("](").Append(e.RelativePath).Append(')');
                if (!string.IsNullOrWhiteSpace(e.Summary)) sb.Append(" — ").Append(e.Summary);
                if (e.Updated.HasValue) sb.Append(" ｜ updated:").Append(e.Updated.Value.ToString("yyyy-MM-dd"));
                sb.Append('\n');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>lint 发现类型。</summary>
public enum LintFindingKind
{
    /// <summary>孤儿页：无任何入链。</summary>
    Orphan,
    /// <summary>死链：指向不存在的页面。</summary>
    DeadLink,
    /// <summary>未收录于 index.md。</summary>
    Unindexed,
    /// <summary>声明的来源文件不存在。</summary>
    MissingSource,
    /// <summary>来源文件比页面更新，建议重读。</summary>
    StaleCandidate
}

/// <summary>一条 lint 发现。</summary>
public class LintFinding
{
    /// <summary>类型。</summary>
    public LintFindingKind Kind { get; set; }
    /// <summary>相关页面相对路径。</summary>
    public string Path { get; set; } = "";
    /// <summary>说明。</summary>
    public string Detail { get; set; } = "";

    /// <summary>便于构造的便捷构造。</summary>
    public LintFinding() { }

    /// <summary>便于构造的便捷构造。</summary>
    public LintFinding(LintFindingKind kind, string path, string detail)
        => (Kind, Path, Detail) = (kind, path, detail);
}

/// <summary>lint 报告。</summary>
public class WikiLintReport
{
    /// <summary>发现集合。</summary>
    public List<LintFinding> Findings { get; set; } = new();
    /// <summary>扫描页面数（不含 log.md）。</summary>
    public int PageCount { get; set; }
}

/// <summary>wiki 统计。</summary>
public class WikiStats
{
    /// <summary>页面数（不含 log.md）。</summary>
    public int PageCount { get; set; }
    /// <summary>最近一次重建摘要。</summary>
    public string? LastRebuildSummary { get; set; }
}
```

- [ ] **Step 2: 写上下文接口**

```csharp
namespace LuBan.AIAgent.Wiki;

/// <summary>
/// 宿主提供的工作区上下文，供 wiki 定位工作区根。
/// 由 agent 侧实现（读取 WorkspaceManager.Current）。
/// </summary>
public interface IWikiContext
{
    /// <summary>当前工作区根目录绝对路径；无工作区时为 null。</summary>
    string? WorkspaceRoot { get; }
}
```

- [ ] **Step 3: 构建验证** — `dotnet build LuBan.AIAgent/LuBan.AIAgent.csproj`，0 error。

- [ ] **Step 4: 提交**

```
git add LuBan.AIAgent/Wiki/Models.cs LuBan.AIAgent/Wiki/IWikiContext.cs
git commit -m "feat: 新增 wiki 模型与工作区上下文接口"
```

---

### Task 6: frontmatter 解析/渲染

**Files:**
- Create: `LuBan.AIAgent/Wiki/WikiFrontmatter.cs`
- Create: `LuBan.AIAgent/Wiki/WikiPageSerializer.cs`
- Modify: `LuBan.XTestProject/WikiUnitTest.cs`

- [ ] **Step 1: 写失败测试** — 追加到 `WikiUnitTest`：

```csharp
    [TestMethod]
    public void WikiPageSerializer_RoundTrip_PreservesFieldsAndBody()
    {
        var page = new WikiPage
        {
            RelativePath = "entities/张三.md",
            Title = "张三",
            Type = "entity",
            Tags = { "人物", "客户" },
            Sources = { "raw/a.md" },
            Body = "# 张三\n\n正文",
            IndexSummary = "客户负责人",
            Created = new DateTime(2026, 9, 1),
            Updated = new DateTime(2026, 9, 20)
        };

        var markdown = WikiPageSerializer.Render(page);
        Assert.IsTrue(markdown.StartsWith("---\n"), "应以 frontmatter 开头");

        var parsed = WikiPageSerializer.Parse(page.RelativePath, markdown);
        Assert.AreEqual(page.Title, parsed.Title);
        Assert.AreEqual(page.Type, parsed.Type);
        CollectionAssert.AreEqual(page.Tags, parsed.Tags);
        CollectionAssert.AreEqual(page.Sources, parsed.Sources);
        StringAssert.Contains(parsed.Body, "正文");
    }

    [TestMethod]
    public void WikiFrontmatter_NoFrontmatter_ReturnsWholeAsBody()
    {
        var (fields, body) = WikiFrontmatter.Parse("纯文本内容");
        Assert.AreEqual(0, fields.Count);
        Assert.AreEqual("纯文本内容", body);
    }
```

- [ ] **Step 2: 跑测试确认失败** — 同 Task 1 filter（编译失败：类型不存在）。

- [ ] **Step 3: 写 `WikiFrontmatter.cs`**

```csharp
using System.Text;

namespace LuBan.AIAgent.Wiki;

/// <summary>YAML frontmatter 的最小解析/渲染（只支持字符串键值与行内数组）。</summary>
public static class WikiFrontmatter
{
    /// <summary>解析 markdown 首部 frontmatter，返回字段与正文。</summary>
    public static (Dictionary<string, string> Fields, string Body) Parse(string markdown)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(markdown)) return (fields, "");
        var normalized = markdown.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n")) return (fields, markdown);

        var end = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) return (fields, markdown);

        var header = normalized.Substring(4, end - 4);
        var body = normalized[(end + 4)..].TrimStart('\n');
        foreach (var line in header.Split('\n'))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            fields[line[..idx].Trim()] = Unquote(line[(idx + 1)..].Trim());
        }
        return (fields, body);
    }

    /// <summary>渲染 frontmatter + 正文。</summary>
    public static string Render(IReadOnlyDictionary<string, string> fields, string body)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        foreach (var kv in fields) sb.Append(kv.Key).Append(": ").Append(kv.Value).Append('\n');
        sb.Append("---\n\n").Append(body);
        return sb.ToString();
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }
}
```

- [ ] **Step 4: 写 `WikiPageSerializer.cs`**

```csharp
namespace LuBan.AIAgent.Wiki;

/// <summary>WikiPage 与 markdown 的互转。</summary>
public static class WikiPageSerializer
{
    /// <summary>解析 markdown 为页面。</summary>
    public static WikiPage Parse(string relativePath, string markdown)
    {
        var (fields, body) = WikiFrontmatter.Parse(markdown);
        return new WikiPage
        {
            RelativePath = relativePath,
            Title = fields.GetValueOrDefault("title", Path.GetFileNameWithoutExtension(relativePath)),
            Type = fields.GetValueOrDefault("type", "source"),
            Tags = ParseList(fields.GetValueOrDefault("tags")),
            Sources = ParseList(fields.GetValueOrDefault("sources")),
            Created = ParseDate(fields.GetValueOrDefault("created")),
            Updated = ParseDate(fields.GetValueOrDefault("updated")),
            Body = body
        };
    }

    /// <summary>渲染页面为 markdown。</summary>
    public static string Render(WikiPage page)
    {
        var fields = new Dictionary<string, string>
        {
            ["title"] = page.Title,
            ["type"] = page.Type,
            ["tags"] = "[" + string.Join(", ", page.Tags) + "]",
            ["sources"] = "[" + string.Join(", ", page.Sources) + "]",
            ["created"] = (page.Created ?? DateTime.Today).ToString("yyyy-MM-dd"),
            ["updated"] = (page.Updated ?? DateTime.Today).ToString("yyyy-MM-dd")
        };
        return WikiFrontmatter.Render(fields, page.Body);
    }

    private static List<string> ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        var v = value.Trim();
        if (v.StartsWith('[') && v.EndsWith(']')) v = v[1..^1];
        return v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, out var d) ? d.Date : null;
}
```

- [ ] **Step 5: 跑测试确认通过** — `dotnet test LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WikiUnitTest"`，4 passed。

- [ ] **Step 6: 提交**

```
git add LuBan.AIAgent/Wiki/WikiFrontmatter.cs LuBan.AIAgent/Wiki/WikiPageSerializer.cs LuBan.XTestProject/WikiUnitTest.cs
git commit -m "feat: wiki frontmatter 解析与页面序列化"
```

---

### Task 7: slug 生成

**Files:**
- Create: `LuBan.AIAgent/Wiki/WikiSlug.cs`
- Modify: `LuBan.XTestProject/WikiUnitTest.cs`

- [ ] **Step 1: 写失败测试**

```csharp
    [TestMethod]
    public void WikiSlug_FromTitle_FiltersIllegalCharsAndKeepsChinese()
    {
        Assert.AreEqual("张三", WikiSlug.FromTitle("张三"));
        Assert.AreEqual("a-b", WikiSlug.FromTitle("a / b"));
        Assert.AreEqual("untitled", WikiSlug.FromTitle("  ??  "));
    }

    [TestMethod]
    public void WikiSlug_EnsureUnique_AppendsSuffix()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "张三", "张三-2" };
        Assert.AreEqual("张三-3", WikiSlug.EnsureUnique("张三", existing));
    }
```

- [ ] **Step 2: 跑测试确认失败** — 编译失败（`WikiSlug` 不存在）。

- [ ] **Step 3: 写实现**

```csharp
using System.Text;

namespace LuBan.AIAgent.Wiki;

/// <summary>标题 → 文件名 slug。</summary>
public static class WikiSlug
{
    private static readonly char[] Illegal = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

    /// <summary>从标题生成 slug：保留中文与常规字符，过滤 Windows 非法字符，空白转 '-'。</summary>
    public static string FromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "untitled";
        var sb = new StringBuilder();
        foreach (var ch in title.Trim())
        {
            if (Array.IndexOf(Illegal, ch) >= 0 || char.IsControl(ch)) continue;
            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
                continue;
            }
            sb.Append(ch);
        }
        var slug = sb.ToString().Trim('-', '.');
        if (slug.Length == 0) slug = "untitled";
        return slug.Length > 80 ? slug[..80] : slug;
    }

    /// <summary>冲突时追加 -2、-3…</summary>
    public static string EnsureUnique(string slug, IReadOnlySet<string> existing)
    {
        if (!existing.Contains(slug)) return slug;
        for (var i = 2; ; i++)
        {
            var candidate = $"{slug}-{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过** — 6 passed。

- [ ] **Step 5: 提交**

```
git add LuBan.AIAgent/Wiki/WikiSlug.cs LuBan.XTestProject/WikiUnitTest.cs
git commit -m "feat: wiki 页面 slug 生成"
```

---

### Task 8: 提取层接口 + 注册表 + Text/Markdown 提取器

**Files:**
- Create: `LuBan.AIAgent/Wiki/Extractors/ISourceExtractor.cs`
- Create: `LuBan.AIAgent/Wiki/Extractors/SourceExtractorRegistry.cs`
- Create: `LuBan.AIAgent/Wiki/Extractors/TextExtractor.cs`
- Create: `LuBan.AIAgent/Wiki/Extractors/MarkdownExtractor.cs`
- Modify: `LuBan.XTestProject/WikiUnitTest.cs`

- [ ] **Step 1: 写失败测试**

```csharp
    [TestMethod]
    public async Task SourceExtractorRegistry_UnknownExtension_FallsBackToText()
    {
        var registry = SourceExtractorRegistry.CreateDefault();
        var dir = Path.Combine(Path.GetTempPath(), "luban-wiki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "a.unknown");
            await File.WriteAllTextAsync(file, "hello");
            var source = await registry.ExtractAsync(file);
            Assert.AreEqual("hello", source.Text);
            StringAssert.Contains(source.Markdown, "hello");
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: 跑测试确认失败** — 编译失败。

- [ ] **Step 3: 写接口与结果类型**

```csharp
namespace LuBan.AIAgent.Wiki.Extractors;

/// <summary>提取结果。</summary>
public class ExtractedSource
{
    /// <summary>源文件绝对路径。</summary>
    public string FilePath { get; set; } = "";
    /// <summary>纯文本（供 LLM 阅读）。</summary>
    public string Text { get; set; } = "";
    /// <summary>尽量保留结构的 markdown（无结构时等于 Text）。</summary>
    public string Markdown { get; set; } = "";
    /// <summary>是否被截断。</summary>
    public bool Truncated { get; set; }
}

/// <summary>源文件提取器。</summary>
public interface ISourceExtractor
{
    /// <summary>支持的扩展名（小写，含点，如 ".txt"）。</summary>
    IReadOnlyCollection<string> Extensions { get; }

    /// <summary>提取文件内容。</summary>
    Task<ExtractedSource> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: 写 `TextExtractor`（兜底）**

```csharp
namespace LuBan.AIAgent.Wiki.Extractors;

/// <summary>纯文本提取器，同时作为未命中扩展名的兜底。</summary>
public class TextExtractor : ISourceExtractor
{
    /// <summary>单文件最大读取字符数。</summary>
    public const int MaxChars = 200_000;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Extensions { get; } =
        new[] { ".txt", ".log", ".ini", ".cfg", ".toml", ".properties" };

    /// <inheritdoc />
    public async Task<ExtractedSource> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken);
        var truncated = text.Length > MaxChars;
        if (truncated) text = text[..MaxChars];
        return new ExtractedSource { FilePath = filePath, Text = text, Markdown = text, Truncated = truncated };
    }
}
```

- [ ] **Step 5: 写 `MarkdownExtractor`**

```csharp
namespace LuBan.AIAgent.Wiki.Extractors;

/// <summary>Markdown 提取器：原样返回。</summary>
public class MarkdownExtractor : ISourceExtractor
{
    /// <inheritdoc />
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".md", ".markdown" };

    /// <inheritdoc />
    public async Task<ExtractedSource> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken);
        var truncated = text.Length > TextExtractor.MaxChars;
        if (truncated) text = text[..TextExtractor.MaxChars];
        return new ExtractedSource { FilePath = filePath, Text = text, Markdown = text, Truncated = truncated };
    }
}
```

- [ ] **Step 6: 写注册表**

```csharp
namespace LuBan.AIAgent.Wiki.Extractors;

/// <summary>按扩展名路由到提取器；未命中回落 <see cref="TextExtractor"/>。</summary>
public class SourceExtractorRegistry
{
    private readonly Dictionary<string, ISourceExtractor> _byExtension =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TextExtractor _fallback = new();

    /// <summary>用给定提取器构建注册表。</summary>
    public SourceExtractorRegistry(IEnumerable<ISourceExtractor> extractors)
    {
        foreach (var extractor in extractors)
            foreach (var ext in extractor.Extensions)
                _byExtension[ext] = extractor;
    }

    /// <summary>构建默认提取器集合（`.xls` 也进 Excel 提取器尽力尝试，失败告警跳过）。</summary>
    public static SourceExtractorRegistry CreateDefault() => new(new ISourceExtractor[]
    {
        new TextExtractor(),
        new MarkdownExtractor(),
        new JsonExtractor(),
        new DelimitedTextExtractor(),
        new XmlExtractor(),
        new HtmlExtractor(),
        new ExcelExtractor()
    });

    /// <summary>解析扩展名对应的提取器（未命中返回兜底）。</summary>
    public ISourceExtractor Resolve(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return _byExtension.TryGetValue(ext, out var extractor) ? extractor : _fallback;
    }

    /// <summary>是否显式支持该扩展名。</summary>
    public bool Supports(string filePath) => _byExtension.ContainsKey(Path.GetExtension(filePath));

    /// <summary>提取文件。</summary>
    public Task<ExtractedSource> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
        => Resolve(filePath).ExtractAsync(filePath, cancellationToken);
}
```

> 说明：Step 6 引用了 Task 9/10/11 将创建的 `JsonExtractor`/`DelimitedTextExtractor`/`XmlExtractor`/`HtmlExtractor`/`ExcelExtractor`。为让本任务独立可编译测试，Step 6 之后立即按顺序完成 Task 9-11；或在 Task 8 内联先只放 Text+Markdown 并临时注释其余，完成 Task 11 再解开。**推荐：严格按 Task 8→9→10→11 顺序做，Task 8 提交前先把注册表 `CreateDefault` 改为只含已实现提取器，Task 11 最后一步再补全并测试兜底。**

- [ ] **Step 7: 跑测试确认通过** — `--filter "FullyQualifiedName~SourceExtractorRegistry_UnknownExtension_FallsBackToText"`，1 passed（在 Task 11 完成后）。

- [ ] **Step 8: 提交**

```
git add LuBan.AIAgent/Wiki/Extractors/
git commit -m "feat: 新增源文件提取层（文本/markdown 与注册表）"
```

---

### Task 9: Json / DelimitedText 提取器

**Files:**
- Create: `LuBan.AIAgent/Wiki/Extractors/JsonExtractor.cs`
- Create: `LuBan.AIAgent/Wiki/Extractors/DelimitedTextExtractor.cs`
- Modify: `LuBan.XTestProject/WikiUnitTest.cs`

- [ ] **Step 1: 写失败测试**

```csharp
    [TestMethod]
    public async Task JsonExtractor_Jsonl_ProducesOneBlockPerLine()
    {
        var dir = Path.Combine(Path.GetTempPath(), "luban-wiki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "a.jsonl");
            await File.WriteAllTextAsync(file, "{\"a\":1}\n{\"a\":2}\n");
            var source = await new JsonExtractor().ExtractAsync(file);
            StringAssert.Contains(source.Markdown, "1");
            StringAssert.Contains(source.Markdown, "2");
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public async Task DelimitedTextExtractor_Csv_ProducesMarkdownTable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "luban-wiki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "a.csv");
            await File.WriteAllTextAsync(file, "name,age\n张三,30\n");
            var source = await new DelimitedTextExtractor().ExtractAsync(file);
            StringAssert.Contains(source.Markdown, "| name | age |");
            StringAssert.Contains(source.Markdown, "张三");
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: 跑测试确认失败**

- [ ] **Step 3: 写 `JsonExtractor`**

```csharp
using System.Text;
using System.Text.Json;

namespace LuBan.AIAgent.Wiki.Extractors;

/// <summary>JSON / JSONC / JSONL 提取器。</summary>
public class JsonExtractor : ISourceExtractor
{
    /// <inheritdoc />
    public IReadOnlyCollection<string> Extensions { get; } =
        new[] { ".json", ".jsonc", ".jsonl", ".ndjson" };

    /// <inheritdoc />
    public async Task<ExtractedSource> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var raw = await File.ReadAllTextAsync(filePath, cancellationToken);
        var ext = Path.GetExtension(filePath);
        var isLines = ext.Equals(".jsonl", StringComparison.OrdinalIgnoreCase)
                      || ext.Equals(".ndjson", StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        if (isLines)
        {
            var i = 0;
            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                i++;
                sb.Append("### line ").Append(i).Append('\n').Append(Pretty(line.Trim())).Append("\n\n");
            }
        }
        else
        {
            sb.Append(Pretty(raw));
        }

        var text = sb.ToString();
        var truncated = text.Length > TextExtractor.MaxChars;
        if (truncated) text = text[..TextExtractor.MaxChars];
        return new ExtractedSource { FilePath = filePath, Text = raw, Markdown = text, Truncated = truncated };
    }

    private static string Pretty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }
}
```

- [ ] **Step 4: 写 `DelimitedTextExtractor`**

```csharp
using System.Text;

namespace LuBan.AIAgent.Wiki.Extractors;

/// <summary>CSV / TSV 提取器：转 markdown 表格。</summary>
public class DelimitedTextExtractor : ISourceExtractor
{
    /// <inheritdoc />
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".csv", ".tsv" };

    /// <inheritdoc />
    public async Task<ExtractedSource> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var raw = await File.ReadAllTextAsync(filePath, cancellationToken);
        var delimiter = Path.GetExtension(filePath).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
        var lines = raw.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var sb = new StringBuilder();
        var rows = 0;
        foreach (var line in lines)
        {
            var cells = line.Split(delimiter).Select(c => c.Trim()).ToArray();
            sb.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
            if (rows == 0) sb.Append("| ").Append(string.Join(" | ", cells.Select(_ => "---"))).Append(" |\n");
            rows++;
            if (rows >= 2000) break;
        }

        var markdown = sb.ToString();
        return new ExtractedSource { FilePath = filePath, Text = raw, Markdown = markdown };
    }
}
```

- [ ] **Step 5: 跑测试确认通过** — 2 passed。

- [ ] **Step 6: 提交**

```
git add LuBan.AIAgent/Wiki/Extractors/JsonExtractor.cs LuBan.AIAgent/Wiki/Extractors/DelimitedTextExtractor.cs LuBan.XTestProject/WikiUnitTest.cs
git commit -m "feat: 新增 JSON/CSV/TSV 提取器"
```

---

### Task 10: Xml / Html 提取器

**Files:**
- Create: `LuBan.AIAgent/Wiki/Extractors/XmlExtractor.cs`
- Create: `LuBan.AIAgent/Wiki/Extractors/HtmlExtractor.cs`
- Modify: `LuBan.XTestProject/WikiUnitTest.cs`

- [ ] **Step 1: 写失败测试**

```csharp
    [TestMethod]
    public async Task HtmlExtractor_DegradesToPlainText()
    {
        var dir = Path.Combine(Path.GetTempPath(), "luban-wiki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "a.html");
            await File.WriteAllTextAsync(file, "<html><body><h1>标题</h1><script>var x=1;</script><p>段落</p></body></html>");
            var source = await new HtmlExtractor().ExtractAsync(file);
            StringAssert.Contains(source.Text, "标题");
            StringAssert.Contains(source.Text, "段落");
            Assert.IsFalse(source.Text.Contains("var x=1"), "script 内容应被剥离");
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: 跑测试确认失败**

- [ ] **Step 3: 写 `XmlExtractor`**

```csharp
namespace LuBan.AIAgent.Wiki.Extractors;

/// <summary>XML / XAML / SVG 提取器：原样返回（保留结构供 LLM 阅读）。</summary>
public class XmlExtractor : ISourceExtractor
{
    /// <inheritdoc />
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".xml", ".xaml", ".svg" };

    /// <inheritdoc />
    public async Task<ExtractedSource> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken);
        var truncated = text.Length > TextExtractor.MaxChars;
        if (truncated) text = text[..TextExtractor.MaxChars];
        return new ExtractedSource { FilePath = filePath, Text = text, Markdown = text, Truncated = truncated };
    }
}
```

- [ ] **Step 4: 写 `HtmlExtractor`（正则去标签，降级纯文本，不承诺完整 markdown）**

```csharp
using System.Text.RegularExpressions;

namespace LuBan.AIAgent.Wiki.Extractors;

/// <summary>
/// HTML 提取器：移除 script/style/标签，降级为纯文本。
/// 不引入 HtmlAgilityPack，不承诺完整 markdown 结构（spec Q16）。
/// </summary>
public class HtmlExtractor : ISourceExtractor
{
    private static readonly Regex ScriptStyle = new(
        @"<(script|style)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Blank = new(@"\n{3,}", RegexOptions.Compiled);

    /// <inheritdoc />
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".html", ".htm" };

    /// <inheritdoc />
    public async Task<ExtractedSource> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var html = await File.ReadAllTextAsync(filePath, cancellationToken);
        var text = ScriptStyle.Replace(html, " ");
        text = Tag.Replace(text, "\n");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Blank.Replace(text, "\n\n").Trim();

        var truncated = text.Length > TextExtractor.MaxChars;
        if (truncated) text = text[..TextExtractor.MaxChars];
        return new ExtractedSource { FilePath = filePath, Text = text, Markdown = text, Truncated = truncated };
    }
}
```

- [ ] **Step 5: 跑测试确认通过** — 1 passed（`HtmlExtractor_DegradesToPlainText`）。

- [ ] **Step 6: 提交**

```
git add LuBan.AIAgent/Wiki/Extractors/XmlExtractor.cs LuBan.AIAgent/Wiki/Extractors/HtmlExtractor.cs LuBan.XTestProject/WikiUnitTest.cs
git commit -m "feat: 新增 XML/HTML 提取器（HTML 降级纯文本）"
```

---

### Task 11: Excel 提取器 + MiniExcel 依赖 + 版本递增

**Files:**
- Modify: `LuBan.AIAgent/LuBan.AIAgent.csproj`
- Create: `LuBan.AIAgent/Wiki/Extractors/ExcelExtractor.cs`
- Modify: `LuBan.AIAgent/Wiki/Extractors/SourceExtractorRegistry.cs`
- Modify: `LuBan.XTestProject/WikiUnitTest.cs`

- [ ] **Step 1: 加依赖 + 递增版本** — 在 `LuBan.AIAgent.csproj` 的 `<ItemGroup>` 加：

```xml
    <PackageReference Include="MiniExcel" Version="1.46.0" />
```

并把 `<Version>` 递增到当日版本（格式 `2026.M.d[.r]`，例：`2026.9.201`）。构建即生成 nupkg（`GeneratePackageOnBuild=True`）。

- [ ] **Step 2: 写失败测试**

```csharp
    [TestMethod]
    public async Task ExcelExtractor_Xlsx_ProducesMarkdownTable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "luban-wiki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "a.xlsx");
            MiniExcel.SaveAs(file, new[]
            {
                new Dictionary<string, object> { ["name"] = "张三", ["age"] = 30 }
            });
            var source = await new ExcelExtractor().ExtractAsync(file);
            StringAssert.Contains(source.Markdown, "张三");
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 3: 跑测试确认失败** — 编译失败（`ExcelExtractor` 不存在）。

- [ ] **Step 4: 写 `ExcelExtractor`（`.xlsx` 正式；`.xls` 尽力，失败抛异常由调用方告警跳过）**

```csharp
using System.Text;
using MiniExcelLibs;

namespace LuBan.AIAgent.Wiki.Extractors;

/// <summary>
/// Excel 提取器：每个 sheet 转 markdown 表格。
/// `.xlsx` 为正式支持；`.xls`（BIFF）MiniExcel 不支持，会抛异常由上层告警跳过（spec Q16）。
/// </summary>
public class ExcelExtractor : ISourceExtractor
{
    /// <inheritdoc />
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".xlsx", ".xls" };

    /// <inheritdoc />
    public Task<ExtractedSource> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        foreach (var sheet in MiniExcel.GetSheetNames(filePath))
        {
            sb.Append("## ").Append(sheet).Append('\n');
            var rows = MiniExcel.Query(filePath, useHeaderRow: true, sheetName: sheet).Cast<IDictionary<string, object>>();
            var headers = rows.FirstOrDefault()?.Keys.ToArray() ?? Array.Empty<string>();
            if (headers.Length > 0)
            {
                sb.Append("| ").Append(string.Join(" | ", headers)).Append(" |\n");
                sb.Append("| ").Append(string.Join(" | ", headers.Select(_ => "---"))).Append(" |\n");
            }
            foreach (var row in rows)
            {
                sb.Append("| ").Append(string.Join(" | ", headers.Select(h => row.TryGetValue(h, out var v) ? v?.ToString() ?? "" : ""))).Append(" |\n");
                cancellationToken.ThrowIfCancellationRequested();
            }
            sb.Append('\n');
        }

        var markdown = sb.ToString();
        var truncated = markdown.Length > TextExtractor.MaxChars;
        if (truncated) markdown = markdown[..TextExtractor.MaxChars];
        return Task.FromResult(new ExtractedSource { FilePath = filePath, Text = markdown, Markdown = markdown, Truncated = truncated });
    }
}
```

- [ ] **Step 5: 补全注册表** — 确认 `SourceExtractorRegistry.CreateDefault()` 含全部 7 个提取器（Text/Markdown/Json/DelimitedText/Xml/Html/Excel）。

- [ ] **Step 6: 跑测试确认通过** — `--filter "FullyQualifiedName~WikiUnitTest"`，全绿（含 Task 8 的兜底测试）。

- [ ] **Step 7: 提交**

```
git add LuBan.AIAgent/LuBan.AIAgent.csproj LuBan.AIAgent/Wiki/Extractors/ExcelExtractor.cs LuBan.AIAgent/Wiki/Extractors/SourceExtractorRegistry.cs LuBan.XTestProject/WikiUnitTest.cs
git commit -m "feat: 新增 Excel 提取器并引入 MiniExcel，递增版本"
```

---

### Task 12: `IWikiService` + `WikiService`（读/写/index/log）

**Files:**
- Create: `LuBan.AIAgent/Wiki/IWikiService.cs`
- Create: `LuBan.AIAgent/Wiki/WikiService.cs`
- Modify: `LuBan.XTestProject/WikiUnitTest.cs`

- [ ] **Step 1: 写接口**

```csharp
using LuBan.AIAgent.Retrieval;

namespace LuBan.AIAgent.Wiki;

/// <summary>
/// wiki 知识库服务：确定性维护 wiki/ 的路径布局、index.md、log.md、链接校验与增量重索引。
/// </summary>
public interface IWikiService
{
    /// <summary>读取 index.md。</summary>
    Task<WikiIndex> ReadIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>读取页面（相对 wiki 根的路径）。</summary>
    Task<WikiPage> ReadPageAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>写入/更新页面：落盘 + index upsert + log 追加 + 单页增量重索引。</summary>
    Task SavePageAsync(WikiPage page, CancellationToken cancellationToken = default);

    /// <summary>删除页面：删文件 + index 移除 + log 追加 + 向量软删除。</summary>
    Task DeletePageAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>搜索 wiki（默认限定 wiki 目录），includeRaw 时追加工作区根召回。</summary>
    Task<IReadOnlyList<RetrievalResult>> SearchAsync(string query, int topK = 8, bool includeRaw = false, CancellationToken cancellationToken = default);

    /// <summary>逐页重建 wiki 向量索引（排除 log.md），并清理消失页面。</summary>
    Task<IndexReport> RebuildIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>客观项 lint。</summary>
    Task<WikiLintReport> LintAsync(CancellationToken cancellationToken = default);

    /// <summary>文件系统口径统计。</summary>
    Task<WikiStats> GetStatsAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: 写失败测试（用假 `IRetrievalService`）** — 需在 `WikiUnitTest.cs` 顶部加 `using LuBan.AIAgent.Retrieval;`，并追加：

```csharp
    private sealed class FakeRetrievalService : IRetrievalService
    {
        public List<string> IndexedFiles { get; } = new();
        public List<string> RemovedSources { get; } = new();
        public List<string> SearchedPrefixes { get; } = new();

        public Task<IndexReport> IndexFileAsync(string path, bool force = false, CancellationToken ct = default)
        {
            IndexedFiles.Add(path);
            return Task.FromResult(new IndexReport { ScannedFiles = 1, TotalChunks = 1 });
        }

        public Task RemoveAsync(string sourceName, CancellationToken ct = default)
        {
            RemovedSources.Add(sourceName);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(string query, int topK = 5, string? pathPrefix = null, string? language = null, CancellationToken ct = default)
        {
            SearchedPrefixes.Add(pathPrefix ?? "<all>");
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }

        public Task<IndexReport> IndexDirectoryAsync(string path, string? glob = null, bool force = false, CancellationToken ct = default)
            => Task.FromResult(new IndexReport());
        public Task<IndexReport> IndexContentAsync(string content, string language, string sourceName, CancellationToken ct = default)
            => Task.FromResult(new IndexReport());
        public Task<IndexStats> GetStatsAsync() => Task.FromResult(new IndexStats());
    }

    private sealed class FakeWikiContext : IWikiContext
    {
        public string? WorkspaceRoot { get; set; }
    }

    [TestMethod]
    public async Task WikiService_SaveAndDeletePage_MaintainsIndexLogAndVector()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-wiki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var retrieval = new FakeRetrievalService();
        try
        {
            var service = new WikiService(retrieval, new FakeWikiContext { WorkspaceRoot = root }, new WikiToolOptions());
            var page = new WikiPage { RelativePath = "entities/张三.md", Title = "张三", Type = "entity", Body = "内容" };

            await service.SavePageAsync(page);
            Assert.IsTrue(File.Exists(Path.Combine(root, "wiki", "entities", "张三.md")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "wiki", "index.md")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "wiki", "log.md")));
            var index = await service.ReadIndexAsync();
            Assert.IsTrue(index.Entries.Any(e => e.RelativePath == "entities/张三.md"));
            Assert.IsTrue(retrieval.IndexedFiles.Any(p => p.EndsWith("张三.md")));

            await service.DeletePageAsync("entities/张三.md");
            Assert.IsFalse(File.Exists(Path.Combine(root, "wiki", "entities", "张三.md")));
            Assert.IsFalse((await service.ReadIndexAsync()).Entries.Any(e => e.RelativePath == "entities/张三.md"));
            Assert.IsTrue(retrieval.RemovedSources.Any(p => p.EndsWith("张三.md")));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task WikiService_Search_DefaultsToWikiPrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-wiki-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var retrieval = new FakeRetrievalService();
            var service = new WikiService(retrieval, new FakeWikiContext { WorkspaceRoot = root }, new WikiToolOptions());
            await service.SearchAsync("q");
            Assert.AreEqual(Path.Combine(root, "wiki"), retrieval.SearchedPrefixes[0]);
        }
        finally { Directory.Delete(root, true); }
    }
```

- [ ] **Step 3: 跑测试确认失败** — 编译失败（`WikiService` 不存在）。

- [ ] **Step 4: 写 `WikiService.cs`**

```csharp
using System.Text.RegularExpressions;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Retrieval;

namespace LuBan.AIAgent.Wiki;

/// <summary>wiki 服务默认实现。</summary>
public class WikiService : IWikiService
{
    private const string WikiFolder = "wiki";
    private readonly IRetrievalService _retrieval;
    private readonly IWikiContext _context;
    private readonly WikiToolOptions _options;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private IndexReport? _lastRebuild;

    /// <summary>构造。</summary>
    public WikiService(IRetrievalService retrieval, IWikiContext context, WikiToolOptions options)
    {
        _retrieval = retrieval;
        _context = context;
        _options = options;
    }

    private string RequireRoot()
    {
        var root = _context.WorkspaceRoot;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("当前没有工作区上下文，无法访问 wiki。");
        return Path.Combine(root, WikiFolder);
    }

    private static string NormalizeRel(string path)
        => path.Replace('\\', '/').Trim('/');

    private static string ToFull(string wikiRoot, string relative)
        => Path.Combine(wikiRoot, NormalizeRel(relative).Replace('/', Path.DirectorySeparatorChar));

    private static List<string> EnumeratePages(string wikiRoot)
    {
        if (!Directory.Exists(wikiRoot)) return new List<string>();
        return Directory.EnumerateFiles(wikiRoot, "*.md", SearchOption.AllDirectories)
            .Where(f => !string.Equals(Path.GetFileName(f), "log.md", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static async Task AppendLogAsync(string wikiRoot, string verb, string detail, CancellationToken ct)
    {
        var path = Path.Combine(wikiRoot, "log.md");
        var line = $"## [{DateTime.Today:yyyy-MM-dd}] {verb} | {detail}{Environment.NewLine}";
        await File.AppendAllTextAsync(path, line, ct);
    }

    /// <inheritdoc />
    public async Task<WikiIndex> ReadIndexAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(RequireRoot(), "index.md");
        if (!File.Exists(path)) return new WikiIndex();
        return WikiIndex.Parse(await File.ReadAllTextAsync(path, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<WikiPage> ReadPageAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var full = ToFull(RequireRoot(), relativePath);
        if (!File.Exists(full)) throw new FileNotFoundException($"wiki 页不存在: {relativePath}", full);
        return WikiPageSerializer.Parse(NormalizeRel(relativePath), await File.ReadAllTextAsync(full, cancellationToken));
    }

    /// <inheritdoc />
    public async Task SavePageAsync(WikiPage page, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var root = RequireRoot();
            var rel = NormalizeRel(page.RelativePath);
            if (string.IsNullOrWhiteSpace(rel) || rel.Equals("index.md", StringComparison.OrdinalIgnoreCase) || rel.Equals("log.md", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("页面路径非法（不得为空或为 index.md/log.md）。", nameof(page));

            var full = ToFull(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (File.Exists(full) && page.Created == null)
                page.Created = WikiPageSerializer.Parse(rel, await File.ReadAllTextAsync(full, cancellationToken)).Created;
            page.Updated = DateTime.Today;
            await File.WriteAllTextAsync(full, WikiPageSerializer.Render(page), cancellationToken);

            var index = await ReadIndexAsync(cancellationToken);
            index.Upsert(rel, page);
            await File.WriteAllTextAsync(Path.Combine(root, "index.md"), index.Render(), cancellationToken);

            await AppendLogAsync(root, "save", $"{rel}（{page.Title}）", cancellationToken);

            try { await _retrieval.IndexFileAsync(full, force: true, cancellationToken: cancellationToken); }
            catch (Exception ex) { Logger.Warn($"wiki 页重索引失败: {rel} - {ex.Message}"); }
        }
        finally { _writeLock.Release(); }
    }

    /// <inheritdoc />
    public async Task DeletePageAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var root = RequireRoot();
            var rel = NormalizeRel(relativePath);
            if (rel.Equals("index.md", StringComparison.OrdinalIgnoreCase) || rel.Equals("log.md", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("不得删除 index.md/log.md。", nameof(relativePath));

            var full = ToFull(root, rel);
            if (File.Exists(full)) File.Delete(full);

            var index = await ReadIndexAsync(cancellationToken);
            index.Remove(rel);
            await File.WriteAllTextAsync(Path.Combine(root, "index.md"), index.Render(), cancellationToken);
            await AppendLogAsync(root, "delete", rel, cancellationToken);

            try { await _retrieval.RemoveAsync(full, cancellationToken); }
            catch (Exception ex) { Logger.Warn($"wiki 页向量删除失败: {rel} - {ex.Message}"); }
        }
        finally { _writeLock.Release(); }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(string query, int topK = 8, bool includeRaw = false, CancellationToken cancellationToken = default)
    {
        var root = RequireRoot();
        var results = (await _retrieval.SearchAsync(query, topK, root, null, cancellationToken)).ToList();
        if (includeRaw)
        {
            var seen = new HashSet<string>(results.Select(r => r.FilePath), StringComparer.OrdinalIgnoreCase);
            foreach (var r in await _retrieval.SearchAsync(query, topK, null, null, cancellationToken))
                if (seen.Add(r.FilePath)) results.Add(r);
        }
        return results;
    }

    /// <inheritdoc />
    public async Task<IndexReport> RebuildIndexAsync(CancellationToken cancellationToken = default)
    {
        var root = RequireRoot();
        Directory.CreateDirectory(root);
        var report = new IndexReport();
        var pages = EnumeratePages(root);

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var r = await _retrieval.IndexFileAsync(page, force: true, cancellationToken: cancellationToken);
                report.ScannedFiles++;
                report.TotalChunks += r.TotalChunks;
            }
            catch (Exception ex) { report.Errors.Add($"{page}: {ex.Message}"); }
        }

        var index = await ReadIndexAsync(cancellationToken);
        var known = new HashSet<string>(pages.Select(p => NormalizeRel(Path.GetRelativePath(root, p))), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in index.Entries.ToList())
        {
            if (known.Contains(entry.RelativePath)) continue;
            try
            {
                await _retrieval.RemoveAsync(ToFull(root, entry.RelativePath), cancellationToken);
                report.DeletedFiles++;
            }
            catch (Exception ex) { report.Errors.Add($"{entry.RelativePath}: {ex.Message}"); }
        }

        _lastRebuild = report;
        return report;
    }

    /// <inheritdoc />
    public async Task<WikiLintReport> LintAsync(CancellationToken cancellationToken = default)
    {
        var root = RequireRoot();
        var pages = EnumeratePages(root);
        var rels = new HashSet<string>(pages.Select(p => NormalizeRel(Path.GetRelativePath(root, p))), StringComparer.OrdinalIgnoreCase);
        var index = await ReadIndexAsync(cancellationToken);
        var indexed = new HashSet<string>(index.Entries.Select(e => e.RelativePath), StringComparer.OrdinalIgnoreCase);
        var findings = new List<LintFinding>();
        var inbound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var linkRegex = new Regex(@"\]\(([^)]+\.md)\)", RegexOptions.Compiled);

        foreach (var page in pages)
        {
            var rel = NormalizeRel(Path.GetRelativePath(root, page));
            var parsed = WikiPageSerializer.Parse(rel, await File.ReadAllTextAsync(page, cancellationToken));

            if (!indexed.Contains(rel))
                findings.Add(new LintFinding(LintFindingKind.Unindexed, rel, "页面未收录于 index.md"));

            foreach (var src in parsed.Sources.Where(s => s.Contains('/') && !s.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
                if (!File.Exists(ToFull(_context.WorkspaceRoot!, src)))
                    findings.Add(new LintFinding(LintFindingKind.MissingSource, rel, $"来源缺失: {src}"));

            var dir = rel.Contains('/') ? rel[..rel.LastIndexOf('/')] : "";
            foreach (Match m in linkRegex.Matches(parsed.Body))
            {
                var target = m.Groups[1].Value;
                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                var targetRel = NormalizeRel(string.IsNullOrEmpty(dir) ? target : $"{dir}/{target}");
                if (!rels.Contains(targetRel))
                    findings.Add(new LintFinding(LintFindingKind.DeadLink, rel, $"死链: {target}"));
                else
                    inbound.Add(targetRel);
            }
        }

        foreach (var rel in rels)
            if (!rel.Equals("index.md", StringComparison.OrdinalIgnoreCase)
                && !rel.Equals("overview.md", StringComparison.OrdinalIgnoreCase)
                && !inbound.Contains(rel)
                && !index.Entries.Any(e => string.Equals(e.RelativePath, rel, StringComparison.OrdinalIgnoreCase) && e.Category == "sources"))
                findings.Add(new LintFinding(LintFindingKind.Orphan, rel, "无入链"));

        foreach (var page in pages)
        {
            var rel = NormalizeRel(Path.GetRelativePath(root, page));
            var parsed = WikiPageSerializer.Parse(rel, await File.ReadAllTextAsync(page, cancellationToken));
            if (parsed.Updated == null) continue;
            foreach (var src in parsed.Sources.Where(s => s.Contains('/') && !s.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
            {
                var full = ToFull(_context.WorkspaceRoot!, src);
                if (File.Exists(full) && File.GetLastWriteTime(full).Date > parsed.Updated.Value.Date)
                    findings.Add(new LintFinding(LintFindingKind.StaleCandidate, rel, $"来源较新: {src}"));
            }
        }

        return new WikiLintReport { Findings = findings, PageCount = rels.Count };
    }

    /// <inheritdoc />
    public Task<WikiStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var root = RequireRoot();
        var last = _lastRebuild == null
            ? null
            : $"扫描 {_lastRebuild.ScannedFiles}，删除 {_lastRebuild.DeletedFiles}，切块 {_lastRebuild.TotalChunks}";
        return Task.FromResult(new WikiStats { PageCount = EnumeratePages(root).Count, LastRebuildSummary = last });
    }
}
```

> `Logger.Warn` 若框架无此签名，用本仓库既有日志调用风格替换（参照 `RetrievalService.cs` 的 using 与调用）。测试注入的是 `WikiToolOptions`（Task 4）；`_options` 当前仅备用，保留字段以免后续改动接口。

- [ ] **Step 5: 跑测试确认通过** — `--filter "FullyQualifiedName~WikiUnitTest"`，全绿。

- [ ] **Step 6: 提交**

```
git add LuBan.AIAgent/Wiki/IWikiService.cs LuBan.AIAgent/Wiki/WikiService.cs LuBan.XTestProject/WikiUnitTest.cs
git commit -m "feat: 实现 wiki 服务（读写/index/log/lint/重建）"
```

---

### Task 13: 内置 schema 文案

**Files:**
- Create: `LuBan.AIAgent/Wiki/WikiDefaults.cs`

- [ ] **Step 1: 写默认 schema**（供 agent 侧 `RagAgentProfile` 追加进系统提示词）

```csharp
namespace LuBan.AIAgent.Wiki;

/// <summary>wiki 默认 schema 文案（承载 LLM 创作规范）。</summary>
public static class WikiDefaults
{
    /// <summary>默认 schema：目录布局、frontmatter、链接、index/log 约定。</summary>
    public const string Schema = """
        ## LLM Wiki 维护规范

        你正在维护工作区的 `wiki/` 知识库。规则：
        1. 目录：`sources/`（来源摘要，一源一页）、`entities/`（实体）、`concepts/`（概念）、`queries/`（问答沉淀）；根目录保留 `index.md`、`log.md`、`overview.md`。
        2. 每个页面以 YAML frontmatter 开头，必须包含：`title`、`type`（source|entity|concept|overview|query）、`tags`、`sources`（相对工作区根的来源路径）、`created`、`updated`。
        3. 页面之间用**相对 markdown 链接**互链（如 `../entities/张三.md`）。
        4. 写入/删除页面**必须**调用 `wiki.savePage` / `wiki.deletePage`，由服务负责 index.md、log.md 与向量索引；不要手写 index.md 或 log.md。
        5. 新增内容前先 `wiki.readIndex` 了解已有页面，避免重复建页；发现相关内容应优先并入既有页面。
        6. 回答用户前先 `wiki.search`；wiki 无相关内容时再考虑回落 raw（`includeRaw=true`）。
        7. 定期可用 `wiki.lint` 检查孤儿页、死链、未收录、来源缺失。
        """;
}
```

- [ ] **Step 2: 构建验证** — `dotnet build LuBan.AIAgent/LuBan.AIAgent.csproj`，0 error。

- [ ] **Step 3: 提交**

```
git add LuBan.AIAgent/Wiki/WikiDefaults.cs
git commit -m "feat: 新增 wiki 默认 schema 文案"
```

---

### Task 14: `WikiToolGroup` + `WikiToolPlugin`

**Files:**
- Create: `LuBan.AIAgent/Tools/Wiki/WikiToolGroup.cs`
- Create: `LuBan.AIAgent/Tools/Wiki/WikiToolPlugin.cs`
- Modify: `LuBan.XTestProject/WikiUnitTest.cs`

- [ ] **Step 1: 写 `WikiToolGroup`**

```csharp
using System.ComponentModel;
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Wiki;

namespace LuBan.AIAgent.Tools.Wiki;

/// <summary>wiki 工具实现。</summary>
public class WikiToolGroup
{
    private readonly IWikiService _wiki;
    private readonly WikiToolOptions _options;
    private readonly IToolConfirmationService _confirmation;

    /// <summary>构造。</summary>
    public WikiToolGroup(IWikiService wiki, WikiToolOptions options, IToolConfirmationService confirmation)
    {
        _wiki = wiki;
        _options = options;
        _confirmation = confirmation;
    }

    /// <summary>读取 index.md。</summary>
    [Description("读取 wiki 的 index.md，了解已有页面清单。")]
    public async Task<ToolResult<string>> ReadIndexAsync(CancellationToken cancellationToken = default)
    {
        var index = await _wiki.ReadIndexAsync(cancellationToken);
        var text = index.Render();
        return ToolResult.Ok(text);
    }

    /// <summary>读取页面。</summary>
    [Description("读取 wiki 页面正文。参数 relativePath 为相对 wiki 根的路径，如 entities/张三.md。")]
    public async Task<ToolResult<string>> ReadPageAsync(
        [Description("相对 wiki 根的页面路径")] string relativePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await _wiki.ReadPageAsync(relativePath, cancellationToken);
            return ToolResult.Ok(page.Body);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail<string>(ex.Message);
        }
    }

    /// <summary>写入/更新页面。</summary>
    [Description("写入或更新一个 wiki 页面。服务会自动维护 index.md、log.md 与向量索引。")]
    public async Task<ToolResult<string>> SavePageAsync(
        [Description("相对 wiki 根的页面路径，如 entities/张三.md")] string relativePath,
        [Description("页面标题")] string title,
        [Description("页面类型：source|entity|concept|overview|query")] string type,
        [Description("Markdown 正文")] string body,
        [Description("来源路径数组（相对工作区根），可空")] string[]? sources = null,
        [Description("标签数组，可空")] string[]? tags = null,
        [Description("供 index.md 的摘要，可空")] string? summary = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["relativePath"] = relativePath };
        var outcome = await _confirmation.EvaluateAsync("wiki.savePage", relativePath, args);
        if (outcome == EnumConfirmationOutcome.Planned) return ToolResult.Plan<string>();
        if (outcome == EnumConfirmationOutcome.Denied) return ToolResult.Denied<string>();

        try
        {
            await _wiki.SavePageAsync(new WikiPage
            {
                RelativePath = relativePath,
                Title = title,
                Type = type,
                Body = body,
                Sources = sources?.ToList() ?? new List<string>(),
                Tags = tags?.ToList() ?? new List<string>(),
                IndexSummary = summary
            }, cancellationToken);
            return ToolResult.Ok($"已保存 {relativePath}");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail<string>(ex.Message);
        }
    }

    /// <summary>删除页面。</summary>
    [Description("删除一个 wiki 页面，并同步移除 index 条目与向量索引。")]
    public async Task<ToolResult<string>> DeletePageAsync(
        [Description("相对 wiki 根的页面路径")] string relativePath,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?> { ["relativePath"] = relativePath };
        var outcome = await _confirmation.EvaluateAsync("wiki.deletePage", relativePath, args);
        if (outcome == EnumConfirmationOutcome.Planned) return ToolResult.Plan<string>();
        if (outcome == EnumConfirmationOutcome.Denied) return ToolResult.Denied<string>();

        try
        {
            await _wiki.DeletePageAsync(relativePath, cancellationToken);
            return ToolResult.Ok($"已删除 {relativePath}");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail<string>(ex.Message);
        }
    }

    /// <summary>搜索。</summary>
    [Description("在 wiki 中做向量搜索；includeRaw=true 时同时回落到 raw 工作区文件。")]
    public async Task<ToolResult<string>> SearchAsync(
        [Description("查询内容")] string query,
        [Description("返回条数，省略用默认值")] int? topK = null,
        [Description("是否同时检索 raw 工作区文件")] bool? includeRaw = null,
        CancellationToken cancellationToken = default)
    {
        var results = await _wiki.SearchAsync(query, topK ?? _options.TopK, includeRaw ?? _options.IncludeRawDefault, cancellationToken);
        if (results.Count == 0) return ToolResult.Fail<string>("未找到相关内容。");

        var sb = new System.Text.StringBuilder();
        foreach (var r in results)
            sb.Append("### ").Append(r.FilePath).Append(" (L").Append(r.StartLine).Append('-').Append(r.EndLine).Append(")\n")
              .Append(r.Content).Append("\n\n");

        var text = sb.ToString();
        if (text.Length > _options.MaxResultChars) text = text[.._options.MaxResultChars];
        return ToolResult.Ok(text);
    }

    /// <summary>lint。</summary>
    [Description("检查 wiki 的孤儿页、死链、未收录、来源缺失、来源过期。")]
    public async Task<ToolResult<string>> LintAsync(CancellationToken cancellationToken = default)
    {
        var report = await _wiki.LintAsync(cancellationToken);
        if (report.Findings.Count == 0) return ToolResult.Ok($"共 {report.PageCount} 页，未发现问题。");

        var sb = new System.Text.StringBuilder();
        sb.Append("共 ").Append(report.PageCount).Append(" 页，发现 ").Append(report.Findings.Count).Append(" 项：\n");
        foreach (var f in report.Findings)
            sb.Append("- [").Append(f.Kind).Append("] ").Append(f.Path).Append("：").Append(f.Detail).Append('\n');
        return ToolResult.Ok(sb.ToString());
    }
}
```

- [ ] **Step 2: 写 `WikiToolPlugin`**

```csharp
using LuBan.AIAgent.Abstractions;
using LuBan.AIAgent.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LuBan.AIAgent.Tools.Wiki;

/// <summary>wiki 工具插件（opt-in，工具组名 "wiki"）。</summary>
public class WikiToolPlugin : ILuBanToolPlugin
{
    private readonly IOptions<LuBanAgentOptions> _options;

    /// <summary>构造。</summary>
    public WikiToolPlugin(IOptions<LuBanAgentOptions> options) => _options = options;

    /// <inheritdoc />
    public string GroupName => "wiki";

    /// <inheritdoc />
    public string? Description => "LLM Wiki 知识库：读取/写入/删除 wiki 页面并维护索引";

    /// <inheritdoc />
    public bool IsOptIn => true;

    /// <inheritdoc />
    public IReadOnlyList<AIFunction> GetTools(IServiceProvider serviceProvider, ToolGroupOptions? toolsOptions = null)
    {
        var wiki = serviceProvider.GetService<IWikiService>();
        if (wiki == null) return Array.Empty<AIFunction>();

        var confirmation = serviceProvider.GetRequiredService<IToolConfirmationService>();
        var options = toolsOptions?.Wiki ?? _options.Value.Tools.Wiki;
        var group = new WikiToolGroup(wiki, options, confirmation);

        return new List<AIFunction>
        {
            AIFunctionFactoryHelper.Create(group, nameof(WikiToolGroup.ReadIndexAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(WikiToolGroup.ReadPageAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(WikiToolGroup.SavePageAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(WikiToolGroup.DeletePageAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(WikiToolGroup.SearchAsync)),
            AIFunctionFactoryHelper.Create(group, nameof(WikiToolGroup.LintAsync))
        };
    }

    /// <inheritdoc />
    public bool IsEnabled(LuBanAgentOptions options) => options.Tools.Wiki.Enabled;
}
```

- [ ] **Step 3: 写失败测试（opt-in + 工具数量）** — 追加：

```csharp
    [TestMethod]
    public void WikiToolPlugin_IsOptIn_AndDisabledByDefault()
    {
        var plugin = new WikiToolPlugin(Options.Create(new LuBanAgentOptions()));
        Assert.AreEqual("wiki", plugin.GroupName);
        Assert.IsTrue(plugin.IsOptIn);
        Assert.IsFalse(plugin.IsEnabled(new LuBanAgentOptions()));
    }
```

- [ ] **Step 4: 跑测试确认通过** — `--filter "FullyQualifiedName~WikiUnitTest"`，全绿。

- [ ] **Step 5: 提交**

```
git add LuBan.AIAgent/Tools/Wiki/ LuBan.XTestProject/WikiUnitTest.cs
git commit -m "feat: 新增 wiki 工具组与插件（opt-in）"
```

---

### Task 15: 注册插件 + README + 最终构建

**Files:**
- Modify: `LuBan.AIAgent/LuBanAgentExtensions.cs`
- Modify: `LuBan.AIAgent/README.md` / `README.en.md`

- [ ] **Step 1: 注册插件** — 在 `LuBanAgentExtensions.cs` 既有的插件注册处（`AddLuBanAgent`，约 L46-54 的插件注册模式）依样加入：

```csharp
        services.AddSingleton<ILuBanToolPlugin, Tools.Wiki.WikiToolPlugin>();
```

- [ ] **Step 2: 构建 + 全量测试**

```powershell
dotnet build LuBan.AIAgent/LuBan.AIAgent.csproj
dotnet test LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WikiUnitTest"
```

预期：0 error，测试全绿。

- [ ] **Step 3: 更新 README（中英成对）** — `README.md` 与 `README.en.md` 同步补充：
  - wiki 工具组（opt-in，6 个 `wiki.*` 工具）；
  - `wiki/` 目录布局与 frontmatter 约定；
  - 源格式支持表（txt/log/ini/cfg/toml/properties、md、json/jsonc/jsonl/ndjson、csv/tsv、xml/xaml/svg、html/htm 降级纯文本、xlsx 正式/xls 尽力）；
  - 新增依赖 MiniExcel。

- [ ] **Step 4: 提交**

```
git add LuBan.AIAgent/LuBanAgentExtensions.cs LuBan.AIAgent/README.md LuBan.AIAgent/README.en.md
git commit -m "feat: 注册 wiki 工具插件并更新 README"
```

---

## 阶段 B：Agent 集成（`luban-agent`）

所有命令默认在 `D:\WorkBench\Walle\luban\luban-agent` 下执行。

### Task 16: `WorkspaceWikiContext` + DI 注册

**Files:**
- Create: `LubanAgentCore/Agents/WorkspaceWikiContext.cs`
- Modify: `LubanAgentCore/Hosting/AgentHostBuilder.cs`

- [ ] **Step 1: 写上下文实现**

```csharp
using LuBan.AIAgent.Wiki;

namespace LubanAgentCore.Agents;

/// <summary>基于 <see cref="WorkspaceManager.Current"/> 的 wiki 上下文。</summary>
public sealed class WorkspaceWikiContext : IWikiContext
{
    /// <inheritdoc />
    public string? WorkspaceRoot => WorkspaceManager.Current?.RootPath;
}
```

- [ ] **Step 2: 在 `AgentHostBuilder` 的 `embedder != null` 块内注册**（约 L114-123，`IRetrievalService` 之后）：

```csharp
            services.AddSingleton<IWikiContext, WorkspaceWikiContext>();
            services.AddSingleton<IWikiService>(sp => new WikiService(
                sp.GetRequiredService<IRetrievalService>(),
                sp.GetRequiredService<IWikiContext>(),
                sp.GetRequiredService<IOptions<LuBanAgentOptions>>().Value.Tools.Wiki));
```

顶部补 `using LuBan.AIAgent.Wiki;` 与 `using LubanAgentCore.Agents;`（按现有 using 顺序）。

- [ ] **Step 3: 构建验证** — `dotnet build LubanAgent.slnx`（agent 侧引用的是 NuGet `LuBan.AIAgent`；先完成阶段 A 的打包发布并把 `LubanAgentCore.csproj` 的版本提升到新版本，否则编译会找不到 `IWikiService`）：

```powershell
# 在 luban-framework 下打包（构建即生成 nupkg）
dotnet build LuBan.AIAgent/LuBan.AIAgent.csproj -c Release
# 在 luban-agent 下把 LubanAgentCore.csproj 中 LuBan.AIAgent 版本号改为本次新版本后：
dotnet build LubanAgent.slnx
```

- [ ] **Step 4: 提交**

```
git add LubanAgentCore/Agents/WorkspaceWikiContext.cs LubanAgentCore/Hosting/AgentHostBuilder.cs LubanAgentCore/LubanAgentCore.csproj
git commit -m "feat: 注册 wiki 服务与工作区上下文"
```

---

### Task 17: `RagAgentProfile` wiki 集成

**Files:**
- Modify: `LubanAgentCore/Agents/RagAgentProfile.cs`

- [ ] **Step 1: 启用 wiki 组 + 关闭 raw 注入 + 注入 schema** — 改默认值：

```csharp
    private WikiConfig _wiki = new();

    public RagAgentProfile(WorkspaceInfo workspace)
    {
        _workspace = workspace;
        _systemPrompt = "你是一个知识库问答专家，维护并基于 wiki 知识库回答问题。优先使用 wiki 工具检索与沉淀知识；wiki 无相关内容时再回落 raw 来源。";
        _toolGroups = new[] { "wiki", "retrieval", "filesystem", "context" };
        LoadRagConfig();
        _systemPrompt = string.IsNullOrWhiteSpace(_systemPrompt) ? _systemPrompt : $"{_systemPrompt}\n\n{WikiDefaults.Schema}";
        if (_wiki.SchemaPath != null)
        {
            var schemaFile = Path.Combine(_workspace.RootPath, _wiki.SchemaPath);
            if (File.Exists(schemaFile))
                _systemPrompt = $"{_systemPrompt}\n\n{File.ReadAllText(schemaFile)}";
        }
    }
```

- [ ] **Step 2: `RetrievalMode` 改 null（取消每轮 raw 注入）+ `BuildToolOptions` 写 Wiki**

```csharp
    /// <summary>
    /// 检索模式：null 表示不做每轮 raw 注入，改由 LLM 主动调用 wiki 工具。
    /// </summary>
    public override string? RetrievalMode => null;

    /// <inheritdoc/>
    protected override ToolGroupOptions BuildToolOptions() => new()
    {
        Browser = { Headless = false },
        Script = { DefaultTimeout = 30000 },
        Wiki = { Enabled = true, TopK = _wiki.TopK, IncludeRawDefault = _wiki.IncludeRaw }
    };
```

- [ ] **Step 3: 解析 `rag-config.json.wiki` 节点** — 在 `LoadRagConfig()` 内、`agentProfile` 解析之后加：

```csharp
            if (doc.RootElement.TryGetProperty("wiki", out var wiki))
            {
                if (wiki.TryGetProperty("enabled", out var en) && en.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    _wiki.Enabled = en.GetBoolean();
                if (wiki.TryGetProperty("schemaPath", out var sp2))
                    _wiki.SchemaPath = sp2.GetString();
                if (wiki.TryGetProperty("topK", out var tk) && tk.TryGetInt32(out var topK))
                    _wiki.TopK = topK;
                if (wiki.TryGetProperty("includeRaw", out var ir) && ir.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    _wiki.IncludeRaw = ir.GetBoolean();
            }
```

并加内部类型（文件末尾，namespace 内）：

```csharp
/// <summary>rag-config.json 的 wiki 节点。</summary>
internal sealed class WikiConfig
{
    /// <summary>是否启用 wiki 工具组。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>相对工作区根的 schema 路径。</summary>
    public string? SchemaPath { get; set; } = "wiki/SCHEMA.md";
    /// <summary>默认搜索返回条数。</summary>
    public int TopK { get; set; } = 8;
    /// <summary>搜索是否默认回落 raw。</summary>
    public bool IncludeRaw { get; set; }
}
```

`BuildToolOptions` 里 `Wiki.Enabled = _wiki.Enabled`（而非写死 true），以尊重配置。

- [ ] **Step 4: 构建验证**

```powershell
dotnet build LubanAgent.slnx
```

- [ ] **Step 5: 提交**

```
git add LubanAgentCore/Agents/RagAgentProfile.cs
git commit -m "feat: RAG profile 启用 wiki 工具组并关闭每轮 raw 注入"
```

---

### Task 18: `/wiki` CLI 命令

**Files:**
- Create: `LubanAgentCli/Commands/WikiCommand.cs`
- Modify: `LubanAgentCli/App/ViewModels/CommandViewModel.cs`
- Modify: `LubanAgentCore/Commands/CommandCatalog.cs`

- [ ] **Step 1: 写 `WikiCommand`**（结构对齐 `RagCommand`；`-ingest` 通过 `ConversationViewModel.ProcessInputAsync` 注入附件+指令）

```csharp
using LuBan.AIAgent.Wiki;
using LubanAgentCli.App.ViewModels;

namespace LubanAgentCli.Commands;

/// <summary>LLM Wiki 命令：-status/-ingest/-lint/-rebuild/-search。</summary>
public class WikiCommand : CommandBase
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IWikiService _wiki;
    private readonly ConversationViewModel? _conversation;

    /// <summary>构造。</summary>
    public WikiCommand(
        ConfigManager configManager,
        IConfiguration configuration,
        IWorkspaceManager workspaceManager,
        IWikiService wiki,
        ConversationViewModel? conversation,
        ITuiOutputWriter writer,
        ITuiUiService ui)
        : base(configManager, configuration, writer, ui)
    {
        _workspaceManager = workspaceManager;
        _wiki = wiki;
        _conversation = conversation;
    }

    /// <inheritdoc />
    public override string Name => "wiki";

    /// <inheritdoc />
    public override string Description => "LLM Wiki 管理（-status/-ingest/-lint/-rebuild/-search）";

    /// <inheritdoc />
    public override Task ExecuteAsync() { ShowHelp(); return Task.CompletedTask; }

    /// <inheritdoc />
    public override async Task<bool> ExecuteAsync(string[] args)
    {
        if (args.Length == 0) { ShowHelp(); return true; }
        switch (args[0].ToLowerInvariant())
        {
            case "-status": await StatusAsync(); break;
            case "-ingest": await IngestAsync(args); break;
            case "-lint": await LintAsync(); break;
            case "-rebuild": await RebuildAsync(); break;
            case "-search": await SearchAsync(args); break;
            case "-help": case "-h": ShowHelp(); break;
            default: Writer.WriteError($"未知子命令: {args[0]}"); ShowHelp(); break;
        }
        return true;
    }

    private void ShowHelp()
    {
        Writer.WriteLine();
        Writer.WriteLine("LLM Wiki 命令用法：");
        Writer.WriteLine("  /wiki -status              - 显示 wiki 统计");
        Writer.WriteLine("  /wiki -ingest <路径|glob>  - 让 LLM 读取源文件并写入 wiki");
        Writer.WriteLine("  /wiki -lint                - 检查孤儿页/死链/未收录/来源缺失");
        Writer.WriteLine("  /wiki -rebuild             - 重建 wiki 向量索引（逐页，排除 log.md）");
        Writer.WriteLine("  /wiki -search <查询>       - 在 wiki 中搜索");
    }

    private async Task<bool> EnsureRagWorkspaceAsync()
    {
        var workspace = _workspaceManager.CurrentWorkspace;
        if (workspace == null) { Writer.WriteError("请先使用 /work -switch 切换工作区"); return false; }
        if (workspace.Type != "Rag") { Writer.WriteError($"当前工作区 {workspace.Name} 不是 RAG 知识库"); return false; }
        return true;
    }

    private async Task StatusAsync()
    {
        if (!await EnsureRagWorkspaceAsync()) return;
        var stats = await _wiki.GetStatsAsync();
        Writer.WriteSuccess("wiki 状态");
        Writer.WriteInfo($"  页面数: {stats.PageCount}");
        Writer.WriteInfo($"  最近重建: {stats.LastRebuildSummary ?? "无"}");
    }

    private async Task IngestAsync(string[] args)
    {
        if (!await EnsureRagWorkspaceAsync()) return;
        if (args.Length < 2) { Writer.WriteError("用法: /wiki -ingest <路径|glob>"); return; }
        if (_conversation == null) { Writer.WriteError("Agent 尚未初始化，请先输入内容启动 Agent"); return; }

        var target = string.Join(' ', args[1..]);
        var workspace = _workspaceManager.CurrentWorkspace!;
        var attachments = ResolveFiles(workspace.RootPath, target);
        if (attachments.Count == 0) { Writer.WriteError($"未找到匹配文件: {target}"); return; }

        var instruction = $"请读取以下源文件，按 wiki schema 将其沉淀为 wiki 页面（先读 index，必要时并页），并为相关实体/概念建页互链：{target}";
        Writer.WriteInfo($"开始 ingest：{attachments.Count} 个文件");
        await _conversation.ProcessInputAsync(instruction, attachments);
    }

    private static List<string> ResolveFiles(string rootPath, string target)
    {
        var full = Path.IsPathRooted(target) ? target : Path.Combine(rootPath, target);
        if (File.Exists(full)) return new List<string> { full };
        var dir = Path.GetDirectoryName(full);
        var pattern = Path.GetFileName(full);
        if (dir != null && Directory.Exists(dir) && pattern.Contains('*'))
            return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories).ToList();
        return Directory.Exists(full)
            ? Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).ToList()
            : new List<string>();
    }

    private async Task LintAsync()
    {
        if (!await EnsureRagWorkspaceAsync()) return;
        var report = await _wiki.LintAsync();
        Writer.WriteSuccess($"lint 完成：{report.PageCount} 页，{report.Findings.Count} 项发现");
        foreach (var f in report.Findings.Take(50))
            Writer.WriteInfo($"  [{f.Kind}] {f.Path}：{f.Detail}");
    }

    private async Task RebuildAsync()
    {
        if (!await EnsureRagWorkspaceAsync()) return;
        Writer.WriteInfo("开始重建 wiki 索引（排除 log.md）...");
        var report = await _wiki.RebuildIndexAsync();
        Writer.WriteSuccess($"重建完成：扫描 {report.ScannedFiles}，删除 {report.DeletedFiles}，切块 {report.TotalChunks}");
        foreach (var err in report.Errors.Take(5)) Writer.WriteWarning($"  {err}");
    }

    private async Task SearchAsync(string[] args)
    {
        if (!await EnsureRagWorkspaceAsync()) return;
        if (args.Length < 2) { Writer.WriteError("用法: /wiki -search <查询>"); return; }
        var results = await _wiki.SearchAsync(string.Join(' ', args[1..]));
        if (results.Count == 0) { Writer.WriteWarning("未找到相关内容"); return; }
        foreach (var r in results)
        {
            Writer.WriteInfo($"文件: {r.FilePath} (L{r.StartLine}-{r.EndLine})");
            Writer.WriteLine(r.Content);
            Writer.WriteLine("---");
        }
    }
}
```

- [ ] **Step 2: 路由 + 解析** — 在 `CommandViewModel.TryExecute` 的 `/rag` 行下加：

```csharp
            case "/wiki":     case "/wk":        ExecuteManagementCommand<WikiCommand>(parts); return true;
```

在 `ResolveCommand<TCommand>` 的 switch 中加：

```csharp
            nameof(WikiCommand) => (TCommand)(object)new WikiCommand(configManager, configuration,
                _services.GetRequiredService<IWorkspaceManager>(),
                _services.GetRequiredService<IWikiService>(),
                _conversationVm, _writer, _ui),
```

并补 `using LuBan.AIAgent.Wiki;`（`IWikiService`）。

- [ ] **Step 3: 注册到命令清单** — 在 `CommandCatalog.cs` 依既有 `/rag` 条目风格加 `/wiki`（Name=`/wiki`，Aliases=`/wk`，Description、Category 与 `SlashCommandClient.Cli`）。

- [ ] **Step 4: `-s` 复数别名冲突** — `ExpandSubCommandAliases` 会把 `-s` 展开为 `-switch`（`/wiki -s` 会变 `-switch`）。**不覆写该逻辑**，`/wiki` 帮助里明确写全 `-search`；若需 `-s` 简写，在 `WikiCommand.ExecuteAsync` 里额外接受 `-switch`（映射到 search）或改全局展开表（风险高，首版不做）。

- [ ] **Step 5: 构建验证** — `dotnet build LubanAgent.slnx`。

- [ ] **Step 6: 提交**

```
git add LubanAgentCli/Commands/WikiCommand.cs LubanAgentCli/App/ViewModels/CommandViewModel.cs LubanAgentCore/Commands/CommandCatalog.cs
git commit -m "feat: 新增 /wiki 命令（status/ingest/lint/rebuild/search）"
```

---

### Task 19: 桌面端 wiki 入口

**Files:**
- Modify: `LubanAgentCodex/Views/RagManageWindow.axaml` + `.axaml.cs`

- [ ] **Step 1: 加"Wiki"按钮** — 在 `RagManageWindow.axaml` 既有按钮区（参照"重建索引"按钮）加：

```xml
<Button x:Name="WikiRebuildButton" Content="重建 Wiki 索引" Click="OnWikiRebuildClick" />
<Button x:Name="WikiLintButton" Content="Wiki Lint" Click="OnWikiLintClick" />
```

- [ ] **Step 2: 事件处理** — 在 `.axaml.cs` 中解析 `IWikiService`（参照 L96/L135 现有 `IRetrievalService` 的解析方式），实现：

```csharp
    private async void OnWikiRebuildClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var report = await _wikiService.RebuildIndexAsync();
            await ShowInfoAsync($"重建完成：扫描 {report.ScannedFiles}，删除 {report.DeletedFiles}，切块 {report.TotalChunks}");
        }
        catch (Exception ex) { await ShowErrorAsync($"重建失败: {ex.Message}"); }
    }

    private async void OnWikiLintClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var report = await _wikiService.LintAsync();
            var text = report.Findings.Count == 0
                ? $"共 {report.PageCount} 页，未发现问题。"
                : $"共 {report.PageCount} 页，{report.Findings.Count} 项：\n" +
                  string.Join("\n", report.Findings.Take(50).Select(f => $"[{f.Kind}] {f.Path}：{f.Detail}"));
            await ShowInfoAsync(text);
        }
        catch (Exception ex) { await ShowErrorAsync($"lint 失败: {ex.Message}"); }
    }
```

`ShowInfoAsync` / `ShowErrorAsync` 用窗口内既有的对话框辅助方法名（以现有 `.axaml.cs` 为准替换）。

- [ ] **Step 3: 构建验证** — `dotnet build LubanAgent.slnx`。

- [ ] **Step 4: 提交**

```
git add LubanAgentCodex/Views/RagManageWindow.axaml LubanAgentCodex/Views/RagManageWindow.axaml.cs
git commit -m "feat: 桌面端新增 wiki 重建与 lint 入口"
```

---

### Task 20: 冒烟文档 + 收尾验证

**Files:**
- Modify: `LubanAgentCli/docs/TUI-SMOKE-TEST.md`

- [ ] **Step 1: 增补冒烟步骤** — 在文档中加"LLM Wiki"小节：

```markdown
## LLM Wiki

1. `/rag -new <一个含 md/csv/xlsx 的目录>` → `/work -switch <名称>`
2. `/wiki -status` → 应显示页面数 0、最近重建"无"（不报错）
3. `/wiki -ingest <相对路径>` → LLM 应调用 `wiki.savePage` 建页；确认 `/wiki -status` 页面数 > 0
4. 直接提问 → LLM 应先 `wiki.search`，wiki 无结果时可回落 raw
5. `/wiki -lint` → 输出孤儿/死链等发现，无异常
6. `/wiki -rebuild` → 扫描数 == `wiki/**/*.md`（排除 log.md）数量
7. 切换到一个 Normal 工作区 → 提问时**不应**出现 `wiki.*` 工具（opt-in 生效）
```

- [ ] **Step 2: 全量验证**

```powershell
# framework
dotnet build LuBan.AIAgent/LuBan.AIAgent.csproj
dotnet test LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WikiUnitTest"
# agent
dotnet build LubanAgent.slnx
```

预期：全绿。既有失败项（`TestDependencyInjection`、`WebApplication1` NU1301）不在本计划 DoD 内。

- [ ] **Step 3: 中英 README 成对复查** — 确认阶段 A 的 README 改动中英一致，无遗漏。

- [ ] **Step 4: 提交**

```
git add LubanAgentCli/docs/TUI-SMOKE-TEST.md
git commit -m "docs: 补充 LLM Wiki 冒烟测试步骤"
```

---

## DoD（完成定义）

1. `WikiUnitTest` 全绿（opt-in 门控、frontmatter 往返、slug、提取器、`WikiService` 读写/搜索前缀、插件 opt-in）。
2. `dotnet build LubanAgent.slnx` 与 `dotnet build LuBan.AIAgent/LuBan.AIAgent.csproj` 均 0 error。
3. `/rag` 命令行为零回归（既有功能与输出不变）。
4. 普通（Normal）工作区不出现 `wiki.*` 工具；Rag 工作区出现 6 个 wiki 工具、`RetrievalMode == null`。
5. `/wiki -rebuild` 扫描页面数 == `wiki/**/*.md` 文件数（排除 `log.md`）。
6. `wiki.savePage` / `wiki.deletePage` 触发 `IToolConfirmationService`；Plan 模式返回 `ToolResult.Plan`，拒绝返回 `ToolResult.Denied`。
7. framework 组件 README 中英文成对更新；`LuBan.AIAgent` 版本号已递增并由 agent 引用新版本。

## 风险与回滚

- **接口变更**：`IRetrievalService` 新增成员属破坏性变更；framework 与 agent 必须同版本发布（agent `LubanAgentCore.csproj` 提升到新版本号）。
- **NuGet 发布**：agent 不通过 ProjectReference 依赖 framework，阶段 B 前必须先 `dotnet build -c Release` 生成 nupkg 并让 agent 可用新版本号。
- **`.xls` 不支持**：MiniExcel 不读 BIFF `.xls`，`-ingest` 时该文件会告警跳过（spec Q16 已接受）。
- **回滚**：每任务独立提交，可 `git revert` 单任务；wiki 为新增目录，删除 `wiki/` 即恢复"纯 raw RAG"（`RetrievalMode` 改回 `"auto"`、`ToolGroups` 去掉 `"wiki"`）。