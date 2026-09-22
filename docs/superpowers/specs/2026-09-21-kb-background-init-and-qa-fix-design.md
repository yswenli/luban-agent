# 知识库初始化后台化 + 问答正确性修复 + Excel 最小必修 设计

**日期**: 2026-09-21 | **状态**: 设计完成，待审批
**关联**: `2026-09-20-rag-to-llm-wiki-design.md`（wiki 层已实现并提交）；本设计修正其落地后暴露的运行时缺陷

---

## 1. 背景与目标

实测基线（本机）：Rag 工作区 `rag_file` 1217 个文件 / 12920 个切块；本地 ONNX（bge-small-zh-v1.5，`EmbedBatchSize=32`）连续嵌入约 20 分钟。

暴露三个问题：

1. **初始化阻塞**：Codex「初始化知识库」全程全屏遮罩（创建 → 授权 → 切工作区 → 预扫 → 嵌入），CLI `/rag -index`、`/wiki -ingest`、`/wiki -rebuild` 全程 `closable:false` 进度窗。用户在此期间无法使用产品。
2. **问答不准确**：该轮 `tool_calls` 仅 `ReadIndex`，12920 个切块从未参与召回；知识库「有内容但答不出」。
3. **Excel 提取不可用**：`.xls` 注册即必失败；表头推导对大报表静默产出空表；未注册二进制回落文本提取器产生乱码污染。

**目标**：初始化的必需部分同步完成即放行用户操作，其余转后台并在对话显示区给出「开始 / 完成（或失败）」两条消息；同时修掉问答链路的致命根因；Excel 提取做到「不误报成功、不污染上下文」。

---

## 2. 决策记录

| # | 问题 | 选择 | 说明 |
|---|------|------|------|
| Q1 | 后台化覆盖入口 | **两端所有长耗时入口** | Codex 初始化按钮；CLI `/rag -index`、`/wiki -ingest`（提取段）、`/wiki -rebuild`。启动向导的模型准备为必需项，保持同步 |
| Q2 | 后台任务的工作区归属 | **显式传 `workspaceId`** | 框架 API 显式参数，agent 侧按 id 解析根路径；后台执行结果不受用户切换工作区影响 |
| Q3 | 锁与进度（方案 A1） | **改 framework** | `IndexDirectoryAsync` 不再全程持全局写锁；新增 `IProgress<IndexProgress>`；顺带修搜索正确性缺陷 |
| Q4 | 兜底召回 | **auto 注入 + 工具调用优先级** | RAG 工作区每轮自动注入 topK 检索参考；提示词规定「已调用检索工具时以工具结果为准」 |
| Q5 | Excel 修复范围 | **最小必修** | 移除 `.xls`、表头按列序号、`\|`与换行转义、二进制兜底拒绝 |
| Q6 | 显示区反馈形态 | **两条消息** | 开始时一条「已在后台…」；结束时一条「完成（扫描/切块数）或失败（原因）」 |
| Q7 | 兼容策略 | **可选参数 + 显式破坏面声明** | `IRetrievalService`/`IWikiService` 新增参数全部可选；`IVectorStore` 为实现者破坏性变更（当前仅 agent 实现），README 标注 |

---

## 3. 根因 → 修法映射

| 编号 | 根因（证据） | 修法 | 层 |
|---|---|---|---|
| R1 | 索引根 = 工作区根（`MainWindow.axaml.cs:682`、`RagCommand.cs:221`），但 wiki 搜索默认 `pathPrefix=<root>\wiki` + `includeRaw=false`（`WikiService.cs:157-164`），而 `wiki/` 为空 → 首检必空 | ① `RagAgentProfile`/`rag-config.json` 默认 `wiki.includeRaw=true` ② `WikiService` 真正消费 `WikiToolOptions.TopK/IncludeRawDefault` ③ 提示词首选 `retrieval` 组工具（能搜全工作区） | agent + 框架 |
| R2 | `RagAgentProfile.RetrievalMode => null`（`:75`）→ 框架 `LuBanAgent.cs:331` 不注入，全靠模型自觉 | `RetrievalMode => "auto"`；提示词加「必须先调用检索工具 / 无据明说不知道 / 工具结果优先」 | agent |
| R3 | `SqliteVectorStore.LoadVectorsAsync` 截断前**无排序**（`:138`），候选 = `topK*20`（`RetrievalService.cs:33,238`）→ 12920 块只有任意 ~160 参与比较，结果不可复现 | `RetrievalService.SearchAsync` 取**全量候选**；store 侧按 `Id` 稳定排序保证确定性；chunk > 200k 时按 Id 有序抽样并告警日志 | 框架 + agent |
| R4 | `WikiToolGroup.cs:133` 空结果返回 `ToolResult.Fail` | 改 `ToolResult.Ok`，附「可 includeRaw=true 检索原始资料」提示 | 框架 |
| R5 | 空 `index.md` 渲染近空（`Wiki/WikiService.cs:86`、`Wiki/Models.cs:151-169`）→ 模型判定「知识库为空」 | 空索引渲染引导语（明确 wiki 为空、应改用检索工具） | 框架 |
| R6 | 默认 `rag-config.json` 无 `wiki` 节点；`retrieval` 节点为死配置（`WorkspaceManager.cs:564-585`） | 模板补 `wiki` 节点；`RagAgentProfile` 消费 `retrieval` 节点 | agent |
| R7 | `GetChunksAsync` 缺 `!f.IsDelete`（`SqliteVectorStore.cs:149`）；`GetStatsAsync` 计数缺文件级过滤（`:165`） | 补齐软删过滤 | agent |
| E1 | `.xls` 注册但 MiniExcel 不支持 BIFF，`GetSheetNames` 直接按 zip 打开必抛（`ExcelExtractor.cs:30,36`） | 移除 `.xls` 注册 | 框架 |
| E2 | `useHeaderRow:true` + `rows.FirstOrDefault()?.Keys` 推表头（`:39-45`），首行是标题/空表头/重复表头 → 空表或丢列且不报错 | 改 `useHeaderRow:false` 按列序号输出，首行作显示表头，空/重复表头回退 `列A/列B/列C` | 框架 |
| E3 | 单元格含 `\|`、换行破坏 markdown 表格（`:43,48`） | 表头与单元格转义：`\|`→`\\\|`，换行→`<br>` | 框架 |
| E4 | 未注册扩展（`.docx/.pdf/.png/.zip/.dll/.xlsm`）回落 `TextExtractor`（`SourceExtractorRegistry.cs:50`）读二进制乱码 | 兜底前做二进制判定（复用 `ChunkerFactory` 的 `LooksBinary` 能力或扩展名黑名单），二进制抛可读异常由上层告警跳过 | 框架 |

---

## 4. 框架设计（`luban-framework/LuBan.AIAgent`）

### 4.1 显式 `workspaceId`（Q2）

`workspaceId` 作为**可选参数**（默认 `null`，null 时行为不变）贯穿检索链路：

```csharp
// IRetrievalService / IRetrievalService 实现
Task<IndexReport> IndexDirectoryAsync(string path, string? glob = null, bool force = false,
    CancellationToken cancellationToken = default, string? workspaceId = null);
Task<IndexReport> IndexFileAsync(string path, bool force = false,
    CancellationToken cancellationToken = default, string? workspaceId = null);
Task RemoveAsync(string sourceName, CancellationToken cancellationToken = default, string? workspaceId = null);
Task<IndexReport> IndexContentAsync(string content, string language, string sourceName,
    CancellationToken cancellationToken = default, string? workspaceId = null);
Task<IReadOnlyList<RetrievalResult>> SearchAsync(string query, int topK = 5, string? pathPrefix = null,
    string? language = null, CancellationToken cancellationToken = default, string? workspaceId = null);
Task<IndexStats> GetStatsAsync(string? workspaceId = null);

// IVectorStore：所有 8 个方法追加 string? workspaceId = null
```

- `RetrievalService` 只做透传，不解释该值语义（对框架不透明字符串）。
- `IWikiContext` 增加默认接口成员，保持既有实现可编译：

```csharp
public interface IWikiContext
{
    string? WorkspaceRoot { get; }
    /// <summary>指定工作区根；null 时等价于 <see cref="WorkspaceRoot"/>。</summary>
    string? WorkspaceRootFor(string? workspaceId) => WorkspaceRoot;
}
```

- `IWikiService` 的读/写/编排方法追加可选 `string? workspaceId = null`，内部通过 `IWikiContext.WorkspaceRootFor` 解析根。

### 4.2 索引并发与进度（Q3）

`RetrievalService.IndexDirectoryAsync`：

- **锁粒度**：现状 `using var _ = await _rwLock.WriteLockAsync(...)` 覆盖整个方法（`:58`），导致后台索引期间 `SearchAsync` 的读锁等待 20 分钟。改为：
  - 枚举文件、读文件、切块在锁外；
  - 每文件完成提取/切块后，仅在 `_store` 写入阶段短暂持锁；
  - 删除失联文件的 `SoftDeleteFileAsync` 阶段同理；
  - `SqliteVectorStore` 已有 `_writeGate` 串行化落库，锁只用于防止「搜索读到半更新状态」。
- **嵌入并发**：新增 `SemaphoreSlim _embedGate(1,1)` 包住 embedder 调用，避免后台索引与前台 auto 注入并发使用同一 ONNX generator。
- **进度**：新增重载

```csharp
Task<IndexReport> IndexDirectoryAsync(string path, string? glob, bool force,
    IProgress<IndexProgress>? progress, CancellationToken cancellationToken = default,
    string? workspaceId = null);

/// <param name="Stage">Scanning / Embedding / Deleting / Done</param>
public sealed record IndexProgress(IndexStage Stage, int Done, int Total, string? CurrentFile);
```

  旧签名保留（委托到新重载，`progress: null`）。
- `IndexReport` 不变，继续含 `ScannedFiles/NewFiles/UpdatedFiles/SkippedFiles/DeletedFiles/TotalChunks/EmbeddedChunks/ReusedChunks/Errors`。

### 4.3 搜索正确性（R3/R4/R5/R7）

- `SearchAsync`：`_store.LoadVectorsAsync(pathPrefix, language, maxResults)` 的 `maxResults` 语义改为「返回上限」，框架传 `int.MaxValue`（全量），由 store 保证**按 `Id` 升序**返回；store 内部对 chunk 数 > 200k 的场景做有序抽样 + 告警日志（防内存爆炸）。
- agent `SqliteVectorStore.LoadVectorsAsync`：加 `OrderBy(Id)` 再 `Take`；`GetChunksAsync` 补 `!f.IsDelete`；`GetStatsAsync` 文件/切块计数排除 `IsDelete`。
- `WikiToolGroup.SearchAsync` 空结果 → `ToolResult.Ok`，内容含「wiki 未命中，可 includeRaw=true 检索原始资料」。
- `WikiService` 空 `index.md` 渲染引导语；`WikiToolOptions.TopK/IncludeRawDefault` 真正生效（当前 `_options` 未使用）。

### 4.4 Excel 与提取兜底（Q5）

`Wiki/Extractors/ExcelExtractor.cs`：

- `Extensions` 移除 `.xls`（保留 `.xlsx`；`.xlsm` 仍不注册，走兜底拒绝而非乱码）。
- 改用 `MiniExcel.Query(path, useHeaderRow: false)`：首行作为显示表头，列名取 `列A/列B/…`（Excel 列号），正文从第 2 行起；表头为空或重复时不丢列。
- markdown 表格输出前转义：`|`→`\|`、`\r\n|\n|\r`→`<br>`；表头同理。
- 边生成边判断 `MaxChars`，超限立即 break 并置 `Truncated`（不再先拼全量再截断）。

`Wiki/Extractors/SourceExtractorRegistry.cs`：

- `Resolve` 兜底分支前，对未注册扩展先做二进制判定（复用 `ChunkerFactory` 的二进制探测逻辑，若为 `private` 则提取为 `internal static`）；判定为二进制时抛出带扩展名的可读异常，由 ingest 上层捕获记为告警并跳过该文件。

---

## 5. Agent 设计（`luban-agent`）

### 5.1 后台知识库任务服务（新）

`LubanAgentCore/Services/KnowledgeTaskService.cs`：

```csharp
public enum KnowledgeTaskKind { Index, WikiIngest, WikiRebuild }
public enum KnowledgeTaskState { Running, Completed, Failed }

public sealed record KnowledgeTaskInfo(
    string Id, KnowledgeTaskKind Kind, string WorkspaceId, string Label,
    KnowledgeTaskState State, int Done, int Total, string? Detail,
    DateTime StartedAt, DateTime? EndedAt);

public sealed class KnowledgeTaskService
{
    public event Action<KnowledgeTaskInfo>? Updated;   // 开始/进度/结束统一回调
    public IReadOnlyList<KnowledgeTaskInfo> Snapshot { get; }
    public KnowledgeTaskInfo StartIndex(string workspaceId, string root, string? glob = null);
    public KnowledgeTaskInfo StartWikiIngest(string workspaceId, string root);
    public KnowledgeTaskInfo StartWikiRebuild(string workspaceId, string root);
    public bool Cancel(string taskId);
}
```

- **串行**：内部 `SemaphoreSlim(1,1)` + `Channel` 队列，同一时刻只跑一个知识库任务，避免并发写库。
- **进度节流**：框架 `IProgress` 按文件回调，服务侧合并为 ≥500ms 一次，且内容不变则不发通知（避免 UI 重绘风暴）。
- **失败/取消**：捕获异常写入 `Detail`，状态 `Failed`；`Cancel` 走 `CancellationTokenSource`。
- **参数显式**：所有调用向框架传 `workspaceId`，不依赖 `WorkspaceManager.Current`。

### 5.2 作用域解析（Q2）

- `SqliteVectorStore`：`CurrentWorkspaceId` 改为「方法参数 `workspaceId` 优先，否则 `WorkspaceManager.Current?.WorkspaceId ?? ""`」。
- `WorkspaceWikiContext.WorkspaceRootFor(workspaceId)`：按 id 查工作区（`WorkspaceManager` 现有按 id 查询能力，若无则新增 `GetWorkspaceAsync(string id)`）→ 返回其 `RootPath`；查不到回退 `Current?.RootPath`。
- 结论：后台任务运行期间用户切库，写入/读取仍落在目标任务所属工作区。

### 5.3 两端 UI 接入

**Codex**（`Views/MainWindow.axaml.cs` `OnRagInitRequested`）：

1. 同步完成：创建工作区 → 授权 → 设为当前 → 立即关闭遮罩、恢复用户原工作区；
2. 显示区插系统消息：「知识库「X」已创建，正在后台建立索引…」；
3. `KnowledgeTaskService.StartIndex(...)`；
4. `Updated` 事件：终态时插第二条消息（完成：扫描 N / 新增 M / 切块 K；失败：原因 + 可用 `/rag -index` 重试）。

**CLI**：

- `RagCommand.IndexFiles`、`WikiCommand.IngestAsync` 的提取阶段、`WikiCommand.RebuildAsync`：改为调用 `KnowledgeTaskService`，不再使用 `closable:false` 阻塞进度窗；命令立即返回并输出「已转入后台：<任务标签>」；
- 完成/失败经 `Updated` 事件写入显示区（复用既有系统消息渲染），进度以同一条消息节流更新；
- 保留 `--wait`（或等价开关）供脚本/冒烟场景同步等待。

### 5.4 Profile 与配置（R1/R2/R6）

- `RagAgentProfile`：`RetrievalMode => "auto"`；`WikiOptions.IncludeRawDefault = true`；系统提示词补充硬约束段：
  - 「回答知识库问题前**必须先调用检索工具**（`retrieval.searchCode` 搜原始资料 / `wiki.search` 搜 wiki 页）」；
  - 「若本轮已有检索工具结果，以工具结果为准；自动注入的参考仅作兜底」；
  - 「检索无命中且无自动注入内容时，明确说明『知识库中未找到依据』，不得凭记忆作答」。
- `WorkspaceManager` 默认 `rag-config.json` 模板补 `wiki` 节点（`includeRaw: true`）；`RagAgentProfile` 读取 `retrieval` 节点使既有死配置生效。

---

## 6. 错误处理与可观测

- 后台任务失败不弹模态框，只写显示区消息 + `Logger`；任务可重入（同工作区重复触发时幂等交给增量索引的哈希比对）。
- 索引报告中的 `Errors` 列表非空时，完成消息附带「N 个文件跳过」。
- 框架侧保持既有 `Logger.Error` 风格，不新增日志框架依赖。

---

## 7. 测试与验证

**框架单测**（`LuBan.XTestProject/WikiUnitTest.cs` 扩展）：

1. `ExcelExtractor` 对 `.xls` 不再注册（`SourceExtractorRegistry.Resolve("a.xls")` 走兜底拒绝）；
2. 表头为空/重复时列数不丢（构造最小 xlsx 或用内存流）；
3. `|`/换行转义正确；
4. 超 `MaxChars` 时 `Truncated=true` 且长度受限；
5. 二进制文件（如 `.png` 二进制头）兜底被拒；
6. `SearchAsync` 传 `maxResults` 时 `LoadVectorsAsync` 返回按 Id 有序（agent 侧无单测，用 `SqliteVectorStore` 手工验证）。

**构建**：`dotnet build LuBan.AIAgent/LuBan.AIAgent.csproj -v q --nologo`（framework）、`dotnet build LubanAgent.slnx -v q --nologo`（agent）0 警告。

**手动冒烟**（追加到 `LubanAgentCli/docs/TUI-SMOKE-TEST.md`）：

1. CLI `/rag -index` 立即返回、可继续对话；完成后显示区出现完成消息；
2. 后台索引进行中切换到另一工作区，完成后核对 chunk 落在目标任务工作区（DB 校验 `ai_rag_file.WorkspaceId`）；
3. 后台索引期间发起一次问答，`tool_calls` 含检索工具且检索未被长时间阻塞；
4. Codex 初始化按钮：遮罩立即消失、显示区两条消息、原工作区恢复；
5. 空 `wiki/` 工作区提问：模型回答带「未找到依据」或使用 `retrieval` 工具，而非凭记忆作答。

---

## 8. 风险与限制

| 风险 | 缓解 |
|---|---|
| 后台索引与前台问答同时嵌入，CPU/ONNX 争用 | `_embedGate` 串行 + 可选后台限流（每批间让出） |
| 全量向量加载在超大库（>200k chunk）内存压力 | 阈值为界降级为按 Id 有序抽样 + 告警日志 |
| `IVectorStore` 签名破坏性变更 | 仅 agent 实现；README 中英成对标注迁移说明 |
| 增量索引与 wiki 页面写入并发落库 | 任务服务串行化 + store `_writeGate` |
| 后台任务在进程退出时被中断 | 任务状态写内存即可（不承诺跨进程恢复）；DB 增量语义保证下次重跑可续 |

---

## 9. 非目标

- 不实现「移除 RAG、完全改 llm-wiki」（已评估为方向合理但不对症，本轮不动）。
- 不改 Embedder/模型、不改 chunk 策略与 `supportedExtensions` 白名单、不清理存量 12920 切块垃圾。
- 不修复 `ai_workspace` 重复行与 100MB 历史 DB。
- 不做 Excel 的 CSV RFC4180、GBK 回退、隐藏 sheet、`.xlsm` 支持（归入后续「健壮性」批次）。

---

## 10. 交付顺序

1. **框架**：4.1 → 4.2 → 4.3 → 4.4 + 单测；用户执行发版（`<Version>2026.9.21.x</Version>` → nexus）。
2. **Agent**：切 NuGet 包版本 → 5.1/5.2 → 5.3 → 5.4 → 构建。
3. **验证**：按第 7 节冒烟。