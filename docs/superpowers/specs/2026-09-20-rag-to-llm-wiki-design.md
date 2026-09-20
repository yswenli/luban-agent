# RAG 改造为 LLM Wiki 设计

**日期**: 2026-09-20 | **状态**: 设计完成，待审批

---

## 1. 背景与目标

现有 RAG 知识库是「向量索引 + 每次查询召回」：`IRetrievalService` 索引工作区文件到 SQLite 向量库，查询时召回片段交给 LLM 生成答案。知识不会被沉淀——每个问题都要从原始文档重新发现、拼装。

Karpathy 提出的 **LLM Wiki** 模式不同：LLM **增量构建并维护一个持久化的 wiki**（结构化、互链的 markdown 文件集），作为原始源与用户之间的中间层。新源到来时，LLM 读取、提取要点、整合进既有 wiki（更新实体页、修订主题综述、标注矛盾、强化或挑战综合结论）。知识被编译一次并持续保持最新，而非每次查询重新推导。

目标：在**保留现有向量检索能力**的前提下，把 Rag 工作区升级为「wiki 为主的持久知识库」，实现 `ingest / query / lint` 完整闭环，并在 CLI 与桌面端同时可用。

---

## 2. 决策记录

| # | 问题 | 选择 | 说明 |
|---|------|------|------|
| Q1 | 改造定位 | **叠加：wiki 为主，检索作召回** | 保留 SQLite 向量索引作为召回引擎，新增 wiki 层由 LLM 维护 |
| Q2 | 维护驱动方式 | **命令 + LLM 工具结合** | 命令负责工程化入口（批量 ingest、lint、rebuild）；对话中 LLM 自主调用 wiki 工具做增量维护 |
| Q3 | 三层结构落位 | **复用 Rag 工作区，新增 `wiki/` 子目录** | 工作区根即 raw 源，`wiki/` 存 LLM 生成物；schema 走 Profile 内置 + `wiki/SCHEMA.md` 覆盖 |
| Q4 | 首版验收范围 | **完整闭环 ingest + query + lint** | 对齐 Karpathy 三大操作 |
| Q5 | 使用端与规模 | **CLI + 桌面端都要；面向大库** | 核心逻辑必须放框架层以两端复用；导航以向量检索为主，`index.md` 仅作目录 |
| Q6 | 召回对象 | **索引 wiki 页为主** | query 检索 wiki 编译页，`includeRaw` 时回 raw 溯源 |
| Q7 | 知识来源 | **本地文件为主 + 剪藏/粘贴** | 网页先用现有 Browser/抓取工具落成 markdown 再入库 |
| Q8 | 架构方案 | **方案 C：最小 WikiService + schema 提示词承载创作** | 代码管确定性；LLM 内容创作由内置 schema（`RagAgentProfile` 提示词，`wiki/SCHEMA.md` 覆盖）承载 |
| Q9 | xlsx 依赖 | **引入 MiniExcel** | 与框架 `LuBan.Common` 既有选择一致（已从 NPOI 迁移） |
| Q10 | “json1” 含义 | **.jsonl（JSON Lines）** | 同时兼容 `.ndjson` |
| Q11 | `RetrievalMode` | **改为 `null`，工具按需检索** | 取消每轮 auto 注入 raw，改由 LLM 主动 `wiki.search` |
| Q12 | wiki 写入权限 | **走 `IToolConfirmationService`** | 与 `RetrievalToolGroup` 一致，受 Plan/权限模式控制 |
| Q13 | 文档与注释 | **覆盖式要求准确完整** | 文件头注释块 + 中文 XML 注释；中英文 README 成对更新 |

---

## 3. 架构设计

### 3.1 三层落位与职责边界

```
<root>/                         # Rag 工作区根 = raw 源（LLM 只读）
  <原始文件…>
  wiki/                         # wiki 层（LLM 独占写入）
    SCHEMA.md                   # 可选：覆盖内置 schema
    index.md / log.md / overview.md
    sources/ entities/ concepts/ queries/
```

- **raw 层**：工作区根下原始文件，LLM 只读，是事实来源。
- **wiki 层**：`wiki/` 下 markdown，LLM 通过 `wiki.savePage` 维护。
- **schema 层**：默认内置于 `RagAgentProfile` 系统提示词（始终生效），存在 `<root>/wiki/SCHEMA.md` 时读取并替换默认段。**不走 AGENTS.md**（`RagAgentProfile` 未覆写 `LoadWorkspaceAgentsMd`，默认 false）。

**职责边界（方案 C 核心）**

| 归属 | 内容 |
|------|------|
| 代码管（确定性） | 路径与文件布局、`index.md`/`log.md` 的格式与追加、链接/引用存在性校验、lint 确定性命中、页面变更后的增量重索引 |
| LLM 管（创造性） | 读源取要点、写摘要/实体页/概念页、建立交叉引用、标记矛盾与过时、生成综合结论 |

**分层与新增物**

| 层 | 新增/改动 | 职责 |
|---|---|---|
| 框架 `LuBan.AIAgent` | `IWikiService`/`WikiService`、`WikiToolPlugin`（工具组 `wiki`）、`IWikiContext` 抽象、`ISourceExtractor` + 提取器族、Wiki 默认 schema 文案 | 确定性逻辑与提取层 |
| Agent | `IWikiContext` 实现、`RagAgentProfile` 扩 `wiki` 工具组、`rag-config.json` 增 `wiki` 节点、`AgentHostBuilder` 注册 | 工作区感知与注册 |
| CLI / 桌面 | `/wiki` 命令（CLI）、命令入口（桌面）、薄封装 `IWikiService` | 两端复用同一服务 |

### 3.2 框架新增：接口与模型

**`IWikiContext`（框架定义，agent 实现，对照 `SqliteVectorStore` 的静态访问器模式）**

```csharp
/// <summary>Wiki 上下文：向框架暴露当前工作区根路径，避免框架依赖 agent 的 WorkspaceManager。</summary>
public interface IWikiContext
{
    /// <summary>当前工作区根路径；无工作区上下文时为 null。</summary>
    string? WorkspaceRoot { get; }
}
```

**`IWikiService`（框架定义 + 默认实现 `WikiService`，依赖 `IRetrievalService` 与 `IWikiContext`）**

| 类别 | 方法 | 行为 |
|---|---|---|
| 读 | `ReadIndexAsync()` | 解析 `index.md` → 结构化 `WikiIndex` |
| 读 | `ReadPageAsync(string relativePath)` | 读 frontmatter + 正文 |
| 写 | `SavePageAsync(WikiPage page)` | 落盘 + `index.md` 条目 upsert + `log.md` 追加 + 单页 `IndexContentAsync` **增量重索引** |
| 写 | `DeletePageAsync(string relativePath)` | 删文件 + 移除 index 条目 + log + 索引软删除 |
| 查 | `SearchAsync(string query, int topK, bool includeRaw = false)` | 默认 `SearchAsync(pathPrefix=<wikiRoot>)`；`includeRaw` 追加无前缀检索做溯源 |
| 编排 | `RebuildIndexAsync()` | `IndexDirectoryAsync(<wikiRoot>, "*.md")`（自动软删除消失页） |
| 编排 | `LintAsync()` | 确定性检查，返回 `WikiLintReport` |
| 统计 | `GetStatsAsync()` | 页数 / 索引切块数 |

**模型**

- `WikiPage`：`RelativePath / Title / Type / Tags / Sources / Body / IndexSummary / Created / Updated`。
- `WikiIndex` / `WikiIndexEntry`：解析自 `index.md` 的结构化目录。
- `WikiLintReport { Findings, PageCount, IndexedChunks }`。
- `LintFinding { Kind, Path, Detail }`，`Kind ∈ { Orphan, DeadLink, Unindexed, MissingSource, StaleCandidate }`。
- `ExtractedSource`：见 3.3。

**`WikiToolPlugin`（工具组 `wiki`，注册于 `LuBanAgentExtensions`）**

工具：`wiki.readIndex`、`wiki.readPage`、`wiki.savePage`、`wiki.search`、`wiki.lint`。

关键约束：**LLM 只经 `savePage` 间接维护 `index.md`/`log.md`**，不直接编辑这两个文件，避免格式漂移。`savePage`/`deletePage` 走 `IToolConfirmationService`，返回 `ToolResult.Plan/Denied` 语义与 `RetrievalToolGroup` 一致。

### 3.3 框架新增：源提取层

ingest 前先把源规范化为文本，避免 LLM 直接读二进制。

```csharp
/// <summary>源提取器：把各种格式的源文件规范化为可读文本。</summary>
public interface ISourceExtractor
{
    /// <summary>本提取器支持的扩展名集合（含点，大小写不敏感）。</summary>
    IReadOnlyCollection<string> Extensions { get; }

    /// <summary>提取文件为规范化文本。</summary>
    Task<ExtractedSource> ExtractAsync(string path, CancellationToken ct = default);
}

/// <summary>提取结果。</summary>
public sealed class ExtractedSource
{
    public string Text { get; init; }                                   // 规范化 markdown/纯文本
    public string Language { get; init; }                               // 供切块/索引
    public IReadOnlyDictionary<string, string> Metadata { get; init; }  // sheet 名、列数、行数等
}
```

| 提取器 | 扩展名 | 处理 |
|---|---|---|
| `TextExtractor` | `.txt .log .ini .cfg .toml .properties` + 兜底 | 直通；UTF-8 严格解码，失败则告警跳过 |
| `MarkdownExtractor` | `.md .markdown` | 直通 |
| `JsonExtractor` | `.json .jsonc .jsonl .ndjson` | 缩进 JSON / JSONL 逐行；超大对象截断并附结构摘要 |
| `DelimitedTextExtractor` | `.csv .tsv` | 转 markdown 表（首行作表头），超宽/超长截断并标注 |
| `XmlExtractor` | `.xml .xaml .svg` | 缩进文本 |
| `HtmlExtractor` | `.html .htm` | 去 `script/style/注释`，正文转 markdown |
| `ExcelExtractor` | `.xlsx .xls` | MiniExcel 读；**每 sheet → 一个 markdown 表**，sheet 名作小节标题 |

`SourceExtractorRegistry` 按扩展名路由，未命中回落 `TextExtractor`。

**与索引的关系（避免改动扩大）**：wiki 页本身是 markdown，天然进入向量库；各源经 ingest 变成 wiki 页后即可检索，**无需改现有索引管线**。仅在 `ChunkerFactory` 补 `.txt/.csv/.tsv/.jsonl/.ndjson` 语言映射（让 raw 层可选索引也正确），`.xlsx/.xls` 保持被 `LooksBinary` 排除不变。

### 3.4 Agent 层改动

- 新增 `IWikiContext` 实现（读 `WorkspaceManager.Current?.RootPath`），在 `AgentHostBuilder` 注册；同时注册框架 `IWikiService`（对照 `AgentHostBuilder.cs:117` 的 `IVectorStore`/`IRetrievalService` 注册）。
- `RagAgentProfile`：
  - `ToolGroups` 增 `"wiki"` → `{ "wiki", "retrieval", "filesystem", "context" }`。
  - `RetrievalMode` 由 `"auto"` 改为 `null`（取消每轮 raw 注入，改由 LLM 主动 `wiki.search`）。
  - `SystemPrompt` 内置 Wiki 默认 schema；存在 `<root>/wiki/SCHEMA.md` 时读取并替换默认段。
  - `rag-config.json` 的 `agentProfile` 节点解析保持兼容，新增可选 `wiki` 节点。

### 3.5 CLI 与桌面端命令

- `/wiki -status`：stats + index 概览（纯代码）。
- `/wiki -ingest <path|glob>`：源路径作附件 + 指令注入 `AgentHostService.RunStreamingAsync`（`AgentHostService.cs:121`），由 LLM 调 `wiki` 工具落页。
- `/wiki -lint`：先出代码报告，可选「交 LLM 复核」（同样注入会话）。
- `/wiki -rebuild`：纯代码重建 wiki 向量索引。
- `/wiki -search <q>`：纯代码检索验证。
- 桌面端 `RagManageWindow` 复用同一 `IWikiService`，入口与展示适配 Avalonia。

---

## 4. 数据与文件规范

### 4.1 页面格式

```markdown
---
title: 标题
type: source | entity | concept | overview | query
tags: [tag1, tag2]
sources: [sources/xxx.md, raw相对路径]   # 本页依据
created: 2026-09-20
updated: 2026-09-20
---

正文…交叉引用用相对链接 [标题](entities/foo.md)，Obsidian 可直接打开。
```

- frontmatter 字段刻意最小（YAGNI）：`title/type/tags/sources/created/updated`。
- 链接统一用**相对 markdown 链接**（而非 `[[wikilink]]`）：Obsidian 与 CLI/grep 都好用，且代码可确定性地做死链校验。
- slug 规则：标题转 slug，保留中文，过滤 Windows 非法字符（`\/:*?"<>|`），冲突加 `-2/-3` 后缀。

### 4.2 index.md（内容目录，按分类分组）

```markdown
## sources
- [某文章摘要](sources/xxx.md) — 一句话摘要 ｜ sources:1 ｜ updated:2026-09-20
## entities
- [某人](entities/foo.md) — …
```

### 4.3 log.md（追加式流水，前缀固定）

```markdown
## [2026-09-20] ingest | 某文章
- 新建 sources/xxx.md；更新 entities/foo.md、concepts/bar.md
## [2026-09-20] query | 对比 A 与 B
- 归档 queries/compare-a-b.md
```

前缀 `## [` 固定，便于 `grep "^## \[" log.md | tail -5`。

### 4.4 归属与校验

页面正文与摘要由 LLM 产出；`WikiService` 负责落盘、frontmatter 规整、`index.md` 条目 upsert、`log.md` 追加。lint 的确定性检查：孤儿页（无入链）、死链（目标不存在）、未收录 index、`sources` 指向的 raw 文件缺失；`StaleCandidate`（页面 `updated` 早于其来源文件 mtime）作候选，**矛盾/过时判定交 LLM**。

### 4.5 索引范围

wiki 下所有 `*.md` 入向量库（为主），**排除 `log.md`**（纯流水噪声）；`index.md` 保留可检索。

---

## 5. 工作流与 schema

### 5.1 ingest（逐源为主，支持批量）

1. 源进入：放入工作区，或粘贴/剪藏为 markdown，或 `/wiki -ingest <path|glob>`。
2. 代码：`ISourceExtractor` 提取规范文本 → `ReadIndexAsync` 给出现有目录 → 组装 ingest 指令。
3. LLM：读源，产出/更新 `sources/<slug>.md`（摘要+要点+链接）及受影响的 `entities/*`、`concepts/*`，必要时 `overview.md`。
4. 代码：每次 `savePage` 落盘 + index upsert + log 追加 + 单页重索引；一个源可触碰 10-15 页，逐页调用。
5. 收尾：`log.md` 记 `## [date] ingest | <源标题>`。

### 5.2 query

1. `wiki.search`（默认 wiki 页，`includeRaw` 可溯源）→ 命中页 → `wiki.readPage`。
2. LLM 综合作答并引用页面相对路径。
3. 可选归档：值得留存的答案经 `savePage(type=query, queries/<slug>.md)` 回写，index 加条目、log 记 `query`——探索也复利。

### 5.3 lint

1. `WikiService.LintAsync` 出客观项（`Orphan/DeadLink/Unindexed/MissingSource/StaleCandidate`）。
2. `/wiki -lint` 展示；可选「交 LLM 复核」：矛盾、过时主张、缺页概念、建议新源/新问题。
3. LLM 修正后经 `savePage` 回写。

### 5.4 schema 内容大纲（内置默认 + `wiki/SCHEMA.md` 覆盖）

- wiki 目的与三层结构（raw 只读 / wiki 由 LLM 写 / schema 是约定）
- 目录分类与 slug 命名规则；frontmatter 字段与 `type` 枚举
- 链接规范：必须用相对 markdown 链接且目标存在于 wiki 内
- 三工作流步骤；偏好「一次只 ingest 一个源、先与用户讨论要点」
- 红线：不改 raw；不手改 `index.md`/`log.md`（一律走 `savePage`）；新数据与旧主张冲突须在页内显式标注

---

## 6. 配置与兼容

### 6.1 配置

- `rag-config.json` 增可选 `wiki` 节点：`{ enabled:true, schemaPath:"wiki/SCHEMA.md", indexRaw:false, topK:8, includeRaw:false }`；缺省即上述默认。
- `LuBanAgentOptions.Tools` 增 `Wiki` 选项（`Enabled/TopK/IncludeRawDefault/MaxResultChars`）。
- `LuBanAgentExtensions.AddLuBanAgent` 注册 `WikiToolPlugin` 与 `IWikiService`；agent 侧 `AgentHostBuilder` 注册 `IWikiContext` 实现。

### 6.2 兼容

- 现有 `/rag`（`new/index/search/list/delete`）与 `retrieval` 工具组**全部保留**，`/rag -index` 仍可索引 raw 供溯源。
- 旧 Rag 工作区**零迁移**：无 `wiki/` 时由 `WikiService` 惰性创建，首次 ingest 生成 `index.md`/`log.md`/页面。
- 普通工作区不受影响（不注入 wiki 工具组与 schema）。

### 6.3 打包与依赖

- `LuBan.AIAgent.csproj` 新增 `PackageReference Include="MiniExcel"`。
- `LuBan.AIAgent.csproj` 的 `<Version>` 按日期制递增，重新打包供 agent 消费。
- 注意：`luban-agent/LubanAgentCore.csproj` 当前为**临时 ProjectReference** 引用 `LuBan.AIAgent`，人工发布后须改回 NuGet。

---

## 7. 错误处理

- 源提取失败（编码错误/损坏 xlsx）→ 跳过并记录，返回可读错误，不中断同批其它源。
- slug 冲突 → 加 `-2/-3` 后缀；frontmatter 缺失 → 代码补默认值并记 warning。
- 链接校验失败 → 只进 lint 报告，**不阻止保存**（避免阻塞创作）。
- 重索引失败 → 页面仍落盘，`log.md` 记 warning，`/wiki -rebuild` 可修复。
- 提取上限：xlsx 单 sheet 行数、csv 行列数、json 大小/深度均设上限，超限截断并在文本中标注。
- 并发：`WikiService` 写入用 `SemaphoreSlim` 串行化，避免 `index.md`/`log.md` 竞争（检索侧已有 `AsyncReaderWriterLock`）。
- 权限：`wiki.savePage`/`wiki.deletePage` 走现有 `IToolConfirmationService`，兼容 Plan/确认模式。

---

## 8. 测试与验收

### 8.1 自动化测试（`LuBan.XTestProject`，MSTest，纯逻辑、不依赖外部服务）

新增 `WikiUnitTest`：

- `SourceExtractorRegistry`：各格式提取（txt/md/json/jsonl/jsonc/csv/tsv/xml/html/xlsx 各一），含截断与降级告警。
- slug 生成与冲突（含中文、非法字符、去重后缀）。
- frontmatter 解析/序列化；`index.md` 解析与条目 upsert；`log.md` 追加与前缀可 `grep`。
- `LintAsync` 五类 `Kind` 各自命中。
- `WikiService` 经 **fake `IRetrievalService`** 验证：`SearchAsync` 默认 `pathPrefix=<wikiRoot>`、`includeRaw` 追加、`SavePageAsync` 触发单页重索引、`DeletePageAsync` 触发软删除。不引 sqlite/ONNX 依赖。

### 8.2 手动冒烟（agent 无自动化测试）

补进 `LubanAgentCli/docs/TUI-SMOKE-TEST.md`：Rag 工作区放各格式源 → `/wiki -ingest` → 查 `wiki/` 结构 → `/wiki -search` → `/agi` 问答带引用 → `/wiki -lint` → `/wiki -rebuild`；桌面端同流程。

### 8.3 验收标准（DoD）

1. 各格式源（`.md/.txt/.csv/.tsv/.json/.jsonl/.jsonc/.xml/.html/.xlsx`）`/wiki -ingest` 后生成对应 `sources/` 页 + index 条目 + log 条目，格式合规范。
2. `/wiki -search` 默认命中 wiki 页而非 raw。
3. `/agi` 基于 wiki 作答并给页面引用；`RetrievalMode=null` 下不再自动注入 raw。
4. `/wiki -lint` 准确定位死链/孤儿/未收录/缺源/过时候选。
5. `/wiki -rebuild` 后向量库页数与 wiki 页数一致（排除 `log.md`）。
6. 旧 `/rag` 命令与既有 Rag 工作区零回归。
7. 中英文 README 成对更新、注释完整、`<Version>` 递增、两仓库 `dotnet build` 通过。
8. 既有测试（含 13 个脚本工具测试）保持全绿。

> `TestDependencyInjection`（`IChatClient` 无法解析）属预存在失败，不在 DoD 内。

---

## 9. 范围边界（首版不含，YAGNI）

- `WikiPageChunker` 页面级切块优化（按标题切块并标注 `chunkType=wiki`）。
- BM25/混合检索与 LLM 重排。
- 图片本地化与多模态（`raw/assets/`）。
- `qmd`/MCP 搜索集成。
- 多用户/人类审批工作流。
- Avalonia 端专属 wiki 可视化面板（关系图等）。

---

## 10. 文档与交付规范

- 新文件遵循仓库既有「文件头注释块」（Copyright / CLR 版本 / 机器名称 / 命名空间 / 文件名 / 版本号 / 唯一标识 / 创建人 / 邮箱 / 创建时间 / 描述 / 修改标记）+ 中文 XML 文档注释；公共接口、模型、公共方法全覆盖 `<summary>/<param>/<returns>`。
- `LuBan.AIAgent/README.md` 与 `README.en.md` **成对**更新：新增 `wiki` 工具组、`/wiki` 命令、`Wiki/` 目录树、源格式支持表、MiniExcel 依赖说明。
- `LubanAgentCli/docs/TUI-SMOKE-TEST.md` 增补 `/wiki` 冒烟步骤。
- 提交信息用中文；`luban-agent` 带 conventional 前缀（`docs:`/`feat:`/`refactor:`），`luban-framework` 用中文短句。