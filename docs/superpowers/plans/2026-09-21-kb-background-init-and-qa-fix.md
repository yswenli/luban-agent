# 知识库初始化后台化 + 问答正确性修复 + Excel 最小必修 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让知识库初始化的必需部分同步完成即放行用户操作、其余转后台并在显示区给两条消息；同时修掉「有内容但答不出」的致命根因；Excel 提取不再误报成功、不再污染上下文。

**Architecture:** 框架层显式化 `workspaceId` 参数、拆细索引锁粒度、把候选向量从「无序截断」改为「有序全量」，并修 Excel 提取与二进制兜底；agent 层新增串行的后台知识库任务服务，CLI/Codex 只做薄接入；RAG 恢复每轮 auto 注入并规定工具结果优先。

**Tech Stack:** .NET 10 / C#、Microsoft.Extensions.AI、MiniExcel 1.46.0、MSTest、SQLite 向量库（既有）。

**Spec:** `D:\WorkBench\Walle\luban\luban-agent\docs\superpowers\specs\2026-09-21-kb-background-init-and-qa-fix-design.md`

---

## File Structure

### 框架 `luban-framework/LuBan.AIAgent/`

| 文件 | 职责 |
|------|------|
| `Retrieval/IRetrievalService.cs` (改) | 全部方法追加可选 `string? workspaceId = null`；`IndexDirectoryAsync` 进度重载 |
| `Retrieval/RetrievalService.cs` (改) | 透传 `workspaceId`、拆锁、`_embedGate`、`IProgress`、全量候选、空结果语义不变 |
| `Retrieval/IVectorStore.cs` (改) | 8 个方法追加可选 `string? workspaceId = null`（破坏性，仅 agent 实现） |
| `Retrieval/IndexProgress.cs` (新) | `IndexStage` + `IndexProgress` |
| `Wiki/IWikiContext.cs` (改) | 加默认成员 `WorkspaceRootFor(string? workspaceId)` |
| `Wiki/IWikiService.cs` + `Wiki/WikiService.cs` (改) | 方法追加可选 `workspaceId`；空 `index.md` 引导语；`WikiToolOptions` 生效 |
| `Wiki/WikiToolOptions.cs` (查/改) | 确认 `TopK`/`IncludeRawDefault` 存在且被消费 |
| `Wiki/Models.cs` (改) | 空索引渲染引导语 |
| `Tools/Wiki/WikiToolGroup.cs` (改) | 空结果 `Fail` → `Ok` |
| `Wiki/Extractors/ExcelExtractor.cs` (改) | 移除 `.xls`、按列序号、转义、提前截断 |
| `Wiki/Extractors/SourceExtractorRegistry.cs` (改) | 兜底二进制拒绝 |
| `Retrieval/Chunkers/ChunkerFactory.cs` (改) | `LooksBinary` 提为 `internal static` |
| `LuBan.XTestProject/WikiUnitTest.cs` (改) | Excel/兜底/搜索确定性单测 |
| `LuBan.AIAgent.csproj` (改) | 版本递增 |
| `README.md` / `README.en.md` (改) | 中英成对：`IVectorStore` 破坏性变更说明 + 后台化/进度能力 |

### Agent `luban-agent/`

| 文件 | 职责 |
|------|------|
| `LubanAgentCore/LubanAgentCore.csproj` (改) | 切 `LuBan.AIAgent` 新版本 |
| `LubanAgentCore/Retrieval/SqliteVectorStore.cs` (改) | `workspaceId` 参数优先、`OrderBy(Id)`、软删过滤 |
| `LubanAgentCore/Agents/WorkspaceWikiContext.cs` (改) | 实现 `WorkspaceRootFor` |
| `LubanAgentCore/Services/KnowledgeTaskService.cs` (新) | 后台任务队列 + 状态通知 |
| `LubanAgentCore/Services/WorkspaceManager.cs` (改) | 按 id 查工作区（若无）、`rag-config.json` 模板补 `wiki` 节点 |
| `LubanAgentCore/Agents/RagAgentProfile.cs` (改) | `RetrievalMode="auto"`、`includeRaw=true`、提示词硬约束、消费 `retrieval` 节点 |
| `LubanAgentCore/Hosting/AgentHostBuilder.cs` (改) | 注册 `KnowledgeTaskService` |
| `LubanAgentCodex/Views/MainWindow.axaml.cs` (改) | 初始化按钮后台化 + 两条消息 |
| `LubanAgentCli/Commands/RagCommand.cs` (改) | `/rag -index` 转后台 + `--wait` |
| `LubanAgentCli/Commands/WikiCommand.cs` (改) | `-ingest`/`-rebuild` 转后台 + `--wait` |
| `LubanAgentCli/docs/TUI-SMOKE-TEST.md` (改) | 后台建库冒烟步骤 |

---

## 阶段 A：框架（`luban-framework`）

所有命令默认在 `D:\WorkBench\Walle\luban\luban-framework` 下执行；构建：`dotnet build LuBan.AIAgent/LuBan.AIAgent.csproj -v q --nologo`。

### Task 1: `workspaceId` 显式化 + 进度模型

**Files:**
- Modify: `LuBan.AIAgent/Retrieval/IRetrievalService.cs`
- Modify: `LuBan.AIAgent/Retrieval/IVectorStore.cs`
- Modify: `LuBan.AIAgent/Retrieval/RetrievalService.cs`
- Create: `LuBan.AIAgent/Retrieval/IndexProgress.cs`

- [x] **Step 1: 新建进度模型** — `Retrieval/IndexProgress.cs`

```csharp
namespace LuBan.AIAgent.Retrieval;

/// <summary>索引阶段。</summary>
public enum IndexStage { Scanning, Embedding, Deleting, Done }

/// <summary>索引进度快照。</summary>
public sealed record IndexProgress(IndexStage Stage, int Done, int Total, string? CurrentFile = null);
```

- [x] **Step 2: `IRetrievalService` 追加参数与重载**

```csharp
Task<IndexReport> IndexDirectoryAsync(string path, string? glob = null, bool force = false,
    IProgress<IndexProgress>? progress = null, CancellationToken cancellationToken = default, string? workspaceId = null);
Task<IndexReport> IndexFileAsync(string path, bool force = false,
    CancellationToken cancellationToken = default, string? workspaceId = null);
Task RemoveAsync(string sourceName, CancellationToken cancellationToken = default, string? workspaceId = null);
Task<IndexReport> IndexContentAsync(string content, string language, string sourceName,
    CancellationToken cancellationToken = default, string? workspaceId = null);
Task<IReadOnlyList<RetrievalResult>> SearchAsync(string query, int topK = 5, string? pathPrefix = null,
    string? language = null, CancellationToken cancellationToken = default, string? workspaceId = null);
Task<IndexStats> GetStatsAsync(string? workspaceId = null);
```

> `IndexDirectoryAsync` 为破坏性签名（第 4 个参数由 `CancellationToken` 变为 `IProgress`）：框架内实现与调用点需同步；调用点全部在框架内（`RetrievalToolPlugin`、测试），均按位置传参会编译失败并被迫修正，符合预期。

- [x] **Step 3: `IVectorStore` 8 个方法追加 `string? workspaceId = null`**（`GetFilesAsync/UpsertFileAsync/SoftDeleteFileAsync/GetFileChunksAsync/ReplaceFileChunksAsync/LoadVectorsAsync/GetChunksAsync/GetStatsAsync`）

- [x] **Step 4: `RetrievalService` 透传** — 每个方法把 `workspaceId` 原样传给 `_store.*`；`SearchAsync` 传 `maxResults: int.MaxValue`（见 Task 3 Step 2）。

- [x] **Step 5: 构建** — 期望 0 错误（框架内无 `IVectorStore` 实现，编译应通过；若测试桩存在则同步签名）

### Task 2: 锁粒度 + 嵌入串行 + 进度上报

**Files:**
- Modify: `LuBan.AIAgent/Retrieval/RetrievalService.cs`

- [x] **Step 1: 拆锁** — 删除 `IndexDirectoryAsync` 方法级 `WriteLockAsync`；改为在 `IndexSingleContentAsync` 之后对 `_store.ReplaceFileChunksAsync`（经由 `IndexSingleContentAsync` 内部）与 `SoftDeleteFileAsync` 调用处包短锁：

```csharp
foreach (var file in files)
{
    cancellationToken.ThrowIfCancellationRequested();
    // ... 读取/哈希/比较（锁外）
    IndexReport r;
    using (await _rwLock.WriteLockAsync(cancellationToken))
        r = await IndexSingleContentAsync(content, lang, fullPath, hash, cancellationToken, workspaceId);
    progress?.Report(new IndexProgress(IndexStage.Embedding, i, files.Count, file));
}
```

- [x] **Step 2: 嵌入门** — 新增字段 `private readonly SemaphoreSlim _embedGate = new(1, 1);`，在 `RetrievalService` 内所有 `_embedder.GenerateAsync(...)` 调用点（查询嵌入与文档嵌入）包住：

```csharp
float[][] vectors;
await _embedGate.WaitAsync(cancellationToken);
try { vectors = /* 原 _embedder.GenerateAsync 调用 */; }
finally { _embedGate.Release(); }
```

- [x] **Step 3: 进度阶段** — 枚举完成后 `Report(Scanning, 0, files.Count, null)`；循环内 `Report(Embedding, i+1, files.Count, file)`；删除循环内 `Report(Deleting, done, total, kv.Key)`；方法末尾 `Report(Done, files.Count, files.Count, null)`。

- [x] **Step 4: 构建 + 现有检索单测** — `dotnet test LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~Retrieval"`（失败先排查环境依赖，非本次引入）

### Task 3: 搜索正确性（R3/R7 框架侧）

**Files:**
- Modify: `LuBan.AIAgent/Retrieval/RetrievalService.cs`

- [x] **Step 1: 全量候选** — `SearchAsync` 中 `LoadVectorsAsync(pathPrefix, language, topK * SearchOversampleFactor)` 改为 `LoadVectorsAsync(pathPrefix, language, int.MaxValue, workspaceId)`；删除 `SearchOversampleFactor` 常量（`:33`）。

- [x] **Step 2: 有序保证** — 在 `SearchAsync` 结果打分前不再依赖 store 顺序；`scored` 的 `OrderByDescending(Score).ThenBy(ChunkId)` 保证同分确定性。

### Task 4: Wiki 上下文与 `IWikiService` 显式工作区（R1/R5）

**Files:**
- Modify: `LuBan.AIAgent/Wiki/IWikiContext.cs`
- Modify: `LuBan.AIAgent/Wiki/IWikiService.cs` + `Wiki/WikiService.cs`
- Modify: `LuBan.AIAgent/Wiki/Models.cs`
- Modify: `LuBan.AIAgent/Tools/Wiki/WikiToolGroup.cs`
- 检查: `LuBan.AIAgent/Configuration/WikiToolOptions.cs`

- [x] **Step 1: `IWikiContext` 默认成员**

```csharp
string? WorkspaceRootFor(string? workspaceId) => WorkspaceRoot;
```

- [x] **Step 2: `IWikiService` 方法追加可选 `string? workspaceId = null`**（`SearchAsync/ReadIndexAsync/ReadPageAsync/SavePageAsync/DeletePageAsync/RebuildIndexAsync/LintAsync/GetStatsAsync`），`WikiService` 内部根路径一律经 `_context.WorkspaceRootFor(workspaceId)`；所有 `IRetrievalService` 调用带上 `workspaceId`。

- [x] **Step 3: 空 `index.md` 引导语** — `Models.cs:151-169` 渲染分支：无条目时输出

```markdown
# Wiki Index

> 本 wiki 尚无页面。若需回答用户问题，请使用检索工具（`retrieval` 组）检索工作区原始资料，或先执行 ingest 建立 wiki 页。
```

- [x] **Step 4: `WikiToolGroup` 空结果改 `Ok`**（`:133`），内容含「wiki 未命中，可用 includeRaw=true 检索原始资料」。

- [x] **Step 5: `WikiToolOptions` 生效** — 确认 `TopK`/`IncludeRawDefault` 成员存在，`WikiService.SearchAsync` 在显式参数缺省时读取之；不存在则新增。

- [x] **Step 6: 构建**

### Task 5: Excel 最小必修 + 二进制兜底

**Files:**
- Modify: `LuBan.AIAgent/Wiki/Extractors/ExcelExtractor.cs`
- Modify: `LuBan.AIAgent/Wiki/Extractors/SourceExtractorRegistry.cs`
- Modify: `LuBan.AIAgent/Retrieval/Chunkers/ChunkerFactory.cs`
- Modify: `LuBan.XTestProject/WikiUnitTest.cs`

- [x] **Step 1: 写失败测试**（`WikiUnitTest.cs` 追加）

```csharp
[TestMethod]
public void Registry_RejectsXls()
{
    var ex = Assert.ThrowsExactly<NotSupportedException>(
        () => SourceExtractorRegistry.CreateDefault(Options.Create(new WikiToolOptions())).Resolve("a.xls"));
    StringAssert.Contains(ex.Message, ".xls");
}

[TestMethod]
public void Registry_RejectsBinaryFallback()
{
    var png = Path.Combine(Path.GetTempPath(), $"luban-bin-{Guid.NewGuid():N}.png");
    File.WriteAllBytes(png, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02 });
    try
    {
        Assert.ThrowsExactly<NotSupportedException>(
            () => SourceExtractorRegistry.CreateDefault(Options.Create(new WikiToolOptions())).Resolve(png));
    }
    finally { File.Delete(png); }
}

[TestMethod]
public void Excel_EscapesPipesAndNewlines()
{
    // 构造最小 xlsx（MiniExcel 无法写旧格式；用固定字节模板或 MiniExcel.SaveAs）
    // 断言结果中不含裸 "|" 列分隔破坏、换行已转 <br>
}
```

- [x] **Step 2: 运行测试确认失败** — `dotnet test LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~WikiUnitTest"`

- [x] **Step 3: `ExcelExtractor`** — 移除 `.xls`；`useHeaderRow:false` 首行作显示表头，正文从第 2 行；列名 `列A/列B/…`；表头与单元格转义（`|`→`\|`、换行→`<br>`）；`MaxChars` 边写边判并置 `Truncated`。

- [x] **Step 4: `SourceExtractorRegistry.Resolve`** — 未命中注册表时先判二进制（`ChunkerFactory.LooksBinary` 提为 `internal static`），二进制抛 `NotSupportedException($"不支持的二进制文件类型: {ext}")`。

- [x] **Step 5: 测试通过 + 构建**

### Task 6: 版本 + README

- [x] **Step 1:** `LuBan.AIAgent.csproj` `<Version>` 递增（如 `2026.9.21.2`）。
- [x] **Step 2:** `README.md`/`README.en.md` 成对补充：后台索引 + `IProgress`、`workspaceId` 参数、**`IVectorStore` 破坏性变更（实现者需同步签名）**、Excel 支持范围（仅 `.xlsx`）。
- [ ] **Step 3:** 告知用户发版（nexus `https://nexus.tjingcai.com/repository/nuget-hosted/index.json`），**发版由用户执行**。

---

## 阶段 B：Agent（`luban-agent`）

构建：`dotnet build LubanAgent.slnx -v q --nologo`。

### Task 7: 切包 + 存储层适配

**Files:** `LubanAgentCore/LubanAgentCore.csproj`、`Retrieval/SqliteVectorStore.cs`、`Agents/WorkspaceWikiContext.cs`

- [x] **Step 1:** 6 个 `LuBan.*` 包版本切到 Task 6 发布版本，`dotnet restore` 成功为准。
- [x] **Step 2:** `SqliteVectorStore`：`CurrentWorkspaceId` → 方法内 `var wsId = workspaceId ?? (WorkspaceManager.Current?.WorkspaceId ?? "");`；8 个方法签名加 `string? workspaceId = null`。
- [x] **Step 3:** `LoadVectorsAsync` 加 `.OrderBy(v => v.ChunkId)`（或 `Id`）再 `Take`；`GetChunksAsync` 过滤 `!f.IsDelete`；`GetStatsAsync` 计数排除 `IsDelete`；chunk > 200_000 时抽样 + `Logger.Warn`。
- [x] **Step 4:** `WorkspaceWikiContext` 实现 `WorkspaceRootFor(workspaceId)`。
- [x] **Step 5:** 构建 0 错误。

### Task 8: 后台任务服务

**Files:** `LubanAgentCore/Services/KnowledgeTaskService.cs`(新)、`Hosting/AgentHostBuilder.cs`、`Services/WorkspaceManager.cs`

- [x] **Step 1:** 新建 `KnowledgeTaskService`（接口/模型/实现见 spec 5.1）。
- [x] **Step 2:** `WorkspaceManager` 增按 id 查询（若无则加 `GetWorkspaceAsync(string id)`）。
- [x] **Step 3:** `AgentHostBuilder` 注册为 singleton。
- [x] **Step 4:** 构建 + 手写一次性验证：触发索引后立即返回，DB 中 `ai_rag_file.WorkspaceId` 落在目标任务。

### Task 9: Profile 与配置

**Files:** `Agents/RagAgentProfile.cs`、`Services/WorkspaceManager.cs`(模板)

- [x] **Step 1:** `RetrievalMode => "auto"`；`IncludeRawDefault = true`。
- [x] **Step 2:** 提示词补硬约束三段（必须先调检索工具 / 工具结果优先 / 无据明说不知道）。
- [x] **Step 3:** 消费 `retrieval` 节点（`rag-config.json`）使死配置生效；默认模板补 `wiki` 节点。
- [x] **Step 4:** 构建。

### Task 10: Codex 接入

**Files:** `LubanAgentCodex/Views/MainWindow.axaml.cs`

- [x] **Step 1:** `OnRagInitRequested`：创建/授权后立即关遮罩 + 恢复原工作区。
- [x] **Step 2:** 显示区插第一条消息「知识库「X」已创建，正在后台建立索引…」。
- [x] **Step 3:** 订阅 `KnowledgeTaskService.Updated`，终态插第二条消息（含报告/失败原因）。
- [x] **Step 4:** 构建。

### Task 11: CLI 接入

**Files:** `LubanAgentCli/Commands/RagCommand.cs`、`Commands/WikiCommand.cs`

- [x] **Step 1:** `/rag -index` 转后台，输出「已转入后台」；新增 `--wait` 走原同步路径。
- [x] **Step 2:** `/wiki -ingest` 提取段、`/wiki -rebuild` 同样转后台 + `--wait`。
- [x] **Step 3:** `Updated` 事件渲染到显示区（同一条消息节流更新，终态追加）。
- [x] **Step 4:** 构建。

### Task 12: 冒烟与提交

- [x] **Step 1:** `LubanAgentCli/docs/TUI-SMOKE-TEST.md` 追加 spec 第 7 节 5 条冒烟。
- [ ] **Step 2:** 两仓库各自提交（framework 中文短句；agent `feat:`/`fix:`）。**docs 目录（spec/plan）不提交，保留工作区改动。**

---

## 验收清单

- [x] framework 构建 0 错误、`WikiUnitTest` 全绿
- [x] agent 构建 0 错误
- [ ] 后台索引不阻塞问答；终态两条消息出现在显示区
- [ ] 后台执行期间切库，chunk 归属正确
- [ ] 空 `wiki/` 工作区提问不再凭记忆作答
- [x] `.xls` 被明确拒绝；大表不再静默空表；二进制不进上下文
