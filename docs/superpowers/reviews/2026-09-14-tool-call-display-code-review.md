# 代码审查报告：工具调用展示与 DB/Redis 残留清理

**审查日期**: 2026-09-14 | **审查对象**: 工作区未提交改动 + 最近提交 `972c862` / `e0f04f7`
**审查方式**: 逐文件静态审查（读源码 + 交叉核对调用链），未编译（沙箱 NETSDK1060 阻断）

---

## 0. 审查范围

| 范围 | 内容 |
|------|------|
| luban-agent 工作区（未提交） | `ToolDisplayNames.cs`（新增）、`ToolCallBlock.cs`、`ConversationViewModel.cs`、`ToolCallCard.axaml.cs` |
| luban-agent 提交 `e0f04f7` | 工具调用序号防护、取消与 UI 体验 |
| luban-framework 提交 `972c862` | 移除 Database/Redis 工具、`FileSystemToolPlugin` 切片、`ToolConfirmationService` 只读放行 |
| 设计/计划一致性 | `docs/superpowers/specs|plans/2026-09-14-tool-config-refactor-and-context-compaction*` |

---

## 1. 结论速览

| ID | 级别 | 位置 | 问题 |
|----|------|------|------|
| P0-1 | **阻断** | `LubanAgentCli/App/ViewModels/ConversationViewModel.cs:749`(HEAD) | HEAD 提交不可编译：2 参数调用 4 参构造 |
| P1-1 | **高** | `LubanAgentCodex/ViewModels/MainWindowViewModel.cs:703-704` | 工具失败原因永远显示不出来 |
| P1-2 | **高** | `LubanAgentCli/App/ViewModels/ConversationViewModel.cs:747,767,845` | 单槽 `_currentToolBlock` 无法对应同轮多个工具调用 |
| P2-1 | 中 | 两宿主展示层 | 失败文案不一致，CLI 重复输出 |
| P2-2 | 中 | `ToolCallCard.axaml.cs:164` | 计时器 detach 后不再恢复 |
| P2-3 | 中 | `ToolCallCard.axaml.cs:79,90` | 事件重复订阅 / 参数文本不复位 |
| P2-4 | 中 | `ToolCallCard.axaml.cs:127,138,148` | 耗时基于"创建时刻"，迟到创建会显示巨大秒数 |
| P2-5 | 中 | framework 仓库 | DB/Redis 只删了代码，依赖与文档未清（spec §3.5/§3.7 未做） |
| P2-6 | 中 | `FileSystemToolPlugin.cs` + `FileSystemMCPClient.cs` | 切片逻辑复制两份，4 处细节问题 |
| P2-7 | 中 | `ToolDisplayNames.cs` | 缺 `CompactContextAsync`；短名全局映射有歧义 |
| P3 | 低 | 多处 | 注释/头部缩进残留；这些改动未进 spec/plan |

---

## 2. P0：HEAD 不可编译（必须先处理）

```
# HEAD (e0f04f7)
LubanAgentCli/App/ViewModels/ConversationViewModel.cs:749
    var toolBlock = new ToolCallBlock(functionCall.Name, functionCall.CallId);   // 2 参数

LubanAgentCli/App/Models/Blocks/ToolCallBlock.cs:57
    public ToolCallBlock(string toolName, string? callId, ConversationDocument doc, IUiDispatcher dispatcher)  // 唯一构造
```

全仓只有一个 `ToolCallBlock` 类型（`grep -rn "class ToolCallBlock"` 仅 1 处），无重载 → 该调用必然 `CS1729`。也就是说 **`e0f04f7` 是一个编译不过的提交**（当时编译被 NETSDK1060 阻断，未验证）。

**好消息**：工作区未提交的改动（第 747 行补上 `_doc, _dispatcher`）正是修复。因此：

1. 这份未提交改动**不能回退**，必须提交；
2. 提交前需要一次真实编译（在用户终端执行）。

---

## 3. P1：两处功能性缺陷

### P1-1 Codex 工具失败原因永远丢失

```csharp
// MainWindowViewModel.cs:697-708
tool.State = state;            // ① 先进 setter → 同步抛 PropertyChanged
tool.ErrorMessage = error;     // ② 后赋值
```

```csharp
// ToolCallCard.axaml.cs:90-94 —— 只订阅 State
item.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ToolCallItem.State)) UpdateState(); };
// ToolCallCard.axaml.cs:144-152 —— RenderFailed 读 ErrorMessage
_stateText.Text = $"{_boundItem.ErrorMessage ?? "工具执行失败"} · {elapsed.TotalSeconds:F1}s";
```

`State` 的 setter 同步触发 `UpdateState()` → `RenderFailed()`，此刻 `ErrorMessage` **仍是旧值**（首次为 `null`）→ 卡片恒显示兜底文案「工具执行失败」，真实错误信息此后无人再渲染。Codex 侧没有别处显示该错误（`ToolCallFailedEvent` 只更新卡片），所以**用户永远看不到失败原因**。

**修复（二选一，推荐前者）**：
```csharp
// MainWindowViewModel.UpdateToolCallState
tool.ErrorMessage = error;
tool.State = state;          // 先赋值 error，再触发状态变更
```
或卡片同时订阅 `ErrorMessage`。
同时该属性应遵守 MVVM：`ToolCallCard` 里手工赋值 TextBlock 属既有写法，本次不动。

### P1-2 CLI 单槽工具块无法对应同轮多个工具调用

```csharp
// ConversationViewModel.cs
747:  var toolBlock = new ToolCallBlock(functionCall.Name, functionCall.CallId, _doc, _dispatcher);
749:  _currentToolBlock = toolBlock;              // ← 单槽，后一个覆盖前一个
765:  if (_currentToolBlock is not null) { ... MarkComplete/MarkFailed ... }   // 不按 CallId 匹配
845:  if (_currentToolBlock is not null) MarkFailed("流中断");
```

`FunctionInvokingChatClient` 同一轮可产出多个 `FunctionCallContent`（FunctionCallContent 先全部到达，结果随后到达）。此时：

1. **错配**：`_currentToolBlock` 只保留最后一个（B）；A 的结果到达时把 B 标成"完成"；
2. **永久卡死**：`_currentToolBlock` 被清空后，A 的结果到达时无人可标 → A 一直到 `finally` 也没人处理 → `ToolCallBlock.StartAnimation` 的 100ms `Timer` **永久运行**，每次 `NotifyChanged()` 触发整块重排重绘（CPU 与 UI 抖动，且长期显示"正在 X…"）。

`ToolCallBlock.CallId` 已经存在（第 57 行）且构造时已赋值，只是从未用于匹配。Codex 侧用的是 `CallId` 反查（`MainWindowViewModel.UpdateToolCallState`），两端策略不一致。

**修复**：把单槽换成按 CallId 索引的集合 + 兜底：

```csharp
private readonly Dictionary<string, ToolCallBlock> _runningTools = new(StringComparer.Ordinal);

// FunctionCall 分支
_runningTools[functionCall.CallId ?? Guid.NewGuid().ToString()] = toolBlock;

// FunctionResult 分支
if (_runningTools.Remove(functionResult.CallId ?? "", out var blk)) { ...Mark...; }
else if (_runningTools.Count > 0) { /* 无 CallId 时的兜底：取最后一个未完成 */ }

// finally
foreach (var blk in _runningTools.Values) blk.MarkFailed("流中断");
_runningTools.Clear();
```
（须注意：字典只在 UI 线程 lambda 内读写，避免跨线程；`_currentRunSeq` 陈旧判断逻辑保持不变。）

---

## 4. P2：健壮性与一致性问题

### P2-1 失败文案两端不一致，CLI 重复输出

| 宿主 | 当前输出 |
|------|----------|
| CLI | `✗ {动作}失败 · N.Ns` **且**额外追加一行 `SystemBlock("❌ 工具执行失败: {msg}")` |
| Codex | `✗ {ErrorMessage ?? "工具执行失败"} · N.Ns`（丢掉动作名） |

建议统一为「`✗ {动作}失败 · N.Ns` + 错误详情单独一行/气泡」，不要两处都报同一件事。

### P2-2 计时器 detach 后不恢复

```csharp
// ToolCallCard.axaml.cs:164-168
protected override void OnDetachedFromVisualTree(...) { base...; StopTimer(); }
// 缺 OnAttachedToVisualTree → UpdateState()
```
当前 `MessageStream.axaml` 用的是**非虚拟化** `ItemsControl`（StackPanel），所以暂时只在整体清空时触发，不至于冻结。但一旦换成 `ListBox`/虚拟化或启用容器复用，运行中的卡片动画与秒数会**永久停止**。补一个 attach 钩子即可（零成本防御）。

### P2-3 事件订阅与参数文本未复位

- `ToolCallCard.axaml.cs:90-94`：每次 `BindItem` 都 `item.PropertyChanged += ...`，从不退订 → 容器复用时订阅叠加；
- `ToolCallCard.axaml.cs:79-86`：只在 `Arguments.Count > 0` 时写 `_argumentsText.Text`，复用时不复位 → 显示上一条工具的参数。

现状每次新建控件，风险低；修 P2-2 时一并处理更划算（`BindItem` 里先解绑旧的、`else` 分支清空文本）。

### P2-4 耗时基准是"创建时刻"

`_boundItem.Timestamp` 来自 `MessageItemBase`（`DateTime.Now`，创建即赋值）。卡片用 `DateTime.Now - Timestamp` 计时。若卡片在状态已变为 `Done` 之后才创建（重建消息列表/切换会话再回来），会显示 `now - 创建时刻` 的巨大秒数。建议 `ToolCallItem` 增加 `StartedAt/CompletedAt`，卡片用二者差值。

### P2-5 framework 侧 DB/Redis 清理不彻底（spec §3.5/§3.7 未落地）

`972c862` 只删了代码与 2 行 README，以下残留仍在：

| 文件 | 残留 |
|------|------|
| `LuBan.AIAgent/LuBan.AIAgent.csproj` | `Microsoft.Data.Sqlite.Core 10.0.12`、`MySqlConnector 2.6.2`、`Npgsql 10.0.3`、`Microsoft.Data.SqlClient 7.0.2` 四个包引用；`<Description>` 仍写"…脚本执行、数据库、Redis 等。" |
| `LuBan.AIAgent/GlobalUsings.cs` | 43/44/52/54/58 五行 DB global using |
| `LuBan.AIAgent/README.md` `.en.md` | 117/118 工具表、547/549 目录树、660 "7 大内置工具组…数据库、Redis" |
| `LubanAgentCli/README.md` | 51/52 数据库/Redis 工具行 |
| `LubanAgentCodex/README.md` `.en.md` | 56/57 工具行、483/487 `Database`/`Redis` 配置示例 |
| 目录 | `LuBan.AIAgent/Tools/Database`、`Tools/Redis` 空目录残留（未被 git 跟踪，但 IDE/默认 glob 可见） |
| `LubanAgentCli/LubanAgentCli.csproj:70-72` | 注释仍称"CLI 的 Database 工具仅支持 SqlServer/MySql/…"，现已**事实错误**（工具已不存在，排除项的意义只剩 SqlSugar 会话持久化） |

已核实：框架内除 `GlobalUsings.cs:44` 外**没有任何** `Microsoft.Data.Sqlite` 实际使用 → 这 4 个包引用当前是纯死引用（SqlSugar 会话持久化在宿主侧，不依赖框架这几个包）。删包可回收体积并避免误导。

### P2-6 ReadFileAsync 切片实现细节（`972c862`）

两处同一逻辑（`FileSystemToolPlugin.cs` 内置工具 ≈336-378 行、`FileSystemMCPClient.cs` ≈177-215 行）：

1. `readFileMaxChars = 256 * 1024` 是**字符数**，提示文案却写「约 256KB」；中文场景 256K 字符 ≈ 500KB+，文案失真；
2. 截断行数 `Math.Min(lineNo, 2000)`：按字符预算截断时，第 `lineNo` 行只写入了半行，实际完整行数是 `lineNo - 1` → 报数偏大 1；
3. `ReadLineAsync()` 归一化换行（原 `ReadToEnd` 原样返回）→ 返回内容不再与文件逐字节一致；对"读代码→改代码"无碍，但属行为变更，宜在注释里点明；
4. **同一逻辑复制两份**，后续调整（阈值/文案）极易漏改 → 建议抽 `FileTextReader.ReadCappedAsync(path, maxLines, maxChars)` 单一实现，两处调用。

（另：`sb.AppendLine(line.Substring(...))` 多一次分配，可忽略；`yield` + `catch` 的 CS1626 规避写法正确。）

### P2-7 `ToolDisplayNames` 的问题

1. **缺 spec §4.3 要求的映射**：`CompactContextAsync → 压缩上下文` 未添加。若因框架侧 `CompactContextToolPlugin` 尚未实现而有意滞后，请在 spec 中标注状态，避免"设计完成"与实际不符；
2. **短名全局映射有歧义**：`SaveAsync / SearchAsync / ListAsync / DeleteAsync` 实为 LocalMemory 专有，但映射表按"方法名"全局查找。MCP 或未来插件出现同名方法会被错译（例如 MCP 的 `DeleteAsync` → 显示"删除记忆"）。`GetAction(string toolName)` 拿不到组/插件信息 → 建议键改 `{group}.{method}` 形式（调用方补 group），或给 `GetAction` 增加可选 group 参数；
3. **MCP 工具名回落**：`mcp__github__create_issue` 无 `Async` 后缀 → 原样显示为「正在mcp__github__create_issue…」。Q11 决策为"保持现状"，但与"友好中文名"目标有落差，建议至少做一次分隔符切分/首字母大写。

---

## 5. P3：注释与流程

- `ToolConfirmationService.cs:235` 注释 "避免脚本 类工具绕过权限模式" —— 删词残留多一个空格；
- `ToolConfirmationService.cs:275` "只读工具（读取/概览/查询）" 中的"查询"对应已删除的 `ExecuteQueryAsync`，宜去掉；
- `ToolCallBlock.cs` 文件头 15-20 行行首少一个空格（编辑残留），与 2-14 行不齐；
- **流程**：本次工作区改动（`ToolDisplayNames` + 两宿主展示层 + `ConversationViewModel` 单槽改多槽）**既不在 spec 也不在 plan 中**（plan 只含"§4.3 加 `CompactContextAsync` 映射"一条），建议在 spec 增补"工具名友好化"小节，并同步 plan 勾选/新增任务。

---

## 6. 与设计/计划的一致性核对

| spec 章节 | 内容 | 状态 |
|-----------|------|------|
| §3.1 | `ILuBanToolPlugin.GetTools` 加 `toolsOptions` | ❌ 未做（签名仍 `GetTools(IServiceProvider)`） |
| §3.2 | `LuBanAgentFactory` 加 `toolsOptions` | ❌ 未做 |
| §3.3 | 新增 `Tools/Context/CompactContextToolPlugin.cs` | ❌ 未做（无 `Tools/Context` 目录） |
| §3.4 | `DefaultReadOnlyTools` 追加 `CompactContextAsync` | ❌ 未做（仅删了 `ExecuteQueryAsync`） |
| §3.5 | csproj 包/Description、GlobalUsings 清理 | ❌ 未做（见 P2-5） |
| §3.6 | Browser `AddScoped → AddSingleton` | ❓ 本次未核对 |
| §3.7 | framework README 更新 | ❌ 未做 |
| §4.1 | `AgentProfile.BuildToolOptions` | ❌ 未做 |
| §4.2 | CLI/Codex `appsettings.json` 删 `Tools` 节 | ❌ 未做（两份第 29 行仍有 `"Tools"`） |
| §4.3 | `ToolDisplayNames` 加映射 | ⚠️ 部分（新文件已建但缺 `CompactContextAsync`） |
| §4.4 | CLI csproj 注释修复 | ❌ 未做（注释已事实错误） |
| §4.5 | `SettingsWindow.axaml.cs:693` CS8602 | ❌ 未做（`provider.Name` 原样） |

> framework 仓库工作区干净、最后一次提交即 `972c862`，说明 spec 的 Part 1/Part 2 主体**尚未开工**。当前已落地的只有「工具名友好化展示」这一片。

---

## 7. 建议修复顺序

1. **立刻**：编译验证并提交工作区改动（消除 P0-1，避免 HEAD 长期不可编译）；提交信息里说明这是 `e0f04f7` 的构造参数补漏。
2. **同批**：P1-1（Codex 错误顺序）、P1-2（CLI 多工具 CallId 匹配）——两处都是用户可见缺陷，改动量小。
3. **随后**：P2-2/2-3/2-4 卡片健壮性合并处理；P2-6 抽公共切片实现。
4. **收尾**：P2-5 依赖与文档清理 + spec/plan 状态对齐，再决定 Part 1/Part 2 是否按 plan 逐步执行。

---

*本报告基于静态审查，未经编译验证（沙箱 NuGet 环境故障 NETSDK1060）。P0-1 结论由类型定义与调用点直接推导，不依赖编译。*
