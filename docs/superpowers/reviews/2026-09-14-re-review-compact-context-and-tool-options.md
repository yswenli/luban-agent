# 代码审查报告（第二轮）：CompactContext 工具与工具参数化

**审查日期**: 2026-09-14（第二轮） | **上一轮**: `docs/superpowers/reviews/2026-09-14-tool-call-display-code-review.md`
**审查对象**: framework `794ba09` + 未提交改动；agent `f23029d`
**方式**: 静态审查（逐文件读源码 + 交叉核对调用链），未编译（沙箱 NETSDK1060 阻断）

---

## 0. 本轮范围与大势

自上一轮以来代码已推进很多：

| 仓库 | 状态 |
|------|------|
| luban-framework | `794ba09`「完善上下文压缩工具插件及接口参数化支持」+ 5 个未提交文件 |
| luban-agent | `f23029d`「新增ToolDisplayNames映射表，统一工具中文动作名」（含 spec/plan/上一轮报告入库），工作区干净 |

**上一轮 P0 已修复** ✓：`ConversationViewModel.cs:747` 现在按 4 参构造 `ToolCallBlock(name, callId, _doc, _dispatcher)`，`e0f04f7` 的编译断裂已消除。

**已完成项（核对通过）**：framework 侧 `GetTools(sp, toolsOptions)` 参数化 / 9 组注册 / `Tools/Context` 新插件 / csproj 去 DB 包 + Description / GlobalUsings 清 7 行 / README（含 context）/ Browser 插件 Scoped→Singleton；agent 侧两份 `appsettings.json` 删 `Tools` 节、CLI csproj 注释、`SettingsWindow` CS8602、`AgentProfile.BuildToolOptions()` + 两个 Profile 硬编码、`ToolDisplayNames` 加 `CompactContextAsync`、CLI/Codex README。

---

## 1. 结论速览

| ID | 级别 | 位置 | 问题 |
|----|------|------|------|
| P0-1 | **运行时异常** | `Tools/Context/CompactContextToolPlugin.cs:61` | `GetRequiredService<ISessionManager>()` 让未注册会话的容器创建 Agent 即抛异常 |
| P1-1 | 高（语义） | `ToolConfirmationService.cs:185,241` | 有副作用的压缩工具被列入只读名单 → Plan 模式真实执行写库 |
| P1-2 | 高（安全） | `CompactContextToolPlugin.cs:101,107` | `targetCount` 无边界校验，可一键归档全部历史 |
| P1-3 | 高（潜伏） | `ScriptToolPlugin.cs:63` 等 5 处 | `toolsOptions?.X ?? _options` 永不回落 → 传对象即整组替换，配置被静默丢弃 |
| P1-4 | 高（潜伏回归） | 删除 `appsettings` Tools 节 | 未传 options 的路径退回 `Shell="pwsh"`，Windows 上大概率执行失败 |
| P2-1 | 中 | `CompactContextToolPlugin.cs:146-154` | 工具结果回灌新旧摘要全文，抵消压缩收益 |
| P2-2 | 中 | `CompactContextToolPlugin.cs:130` | 无 `CancellationToken`，摘要调用无法被 ESC 取消 |
| P2-3 | 中 | `CompactContextToolPlugin.cs:103` | 依赖全局 `CurrentSession` → 编排子 Agent 会压缩别的会话 |
| P2-4 | 中 | `CompactContextToolPlugin.cs:140-144` | 先归档后写摘要，非原子 → 失败即不可逆丢上下文 |
| P2-5 | 中 | 同上 + `SessionChatHistoryProvider.cs:113-137` | 压缩逻辑第 2 份拷贝，`MapRole` 第 3 份 |
| P2-6 | 中 | `LuBan.AIAgent.csproj:12` | 破坏性接口变更未升版本（仍 `2026.9.11.1`） |
| P2-7 | 中 | 未提交 5 文件 | 测试工程编译修复（972c862 曾编坏）但有顶格缩进；`SetOptions` 时序/并发缺口；文件末尾缺换行 |
| P2-8 | 中 | agent 侧 | 上一轮 P1/P2（Codex 错误顺序、CLI 单槽工具块等）**仍未修** |
| P3 | 低 | 多处 | 根 README 仍列数据库/Redis 工具；注释空格残留；`GetAllFunctions` 变成死代码 |

---

## 2. P0-1：新增硬依赖导致创建 Agent 抛异常

```csharp
// Tools/Context/CompactContextToolPlugin.cs:59-66
public IReadOnlyList<AIFunction> GetTools(IServiceProvider sp, ToolGroupOptions? toolsOptions = null)
{
    var sessionManager = sp.GetRequiredService<ISessionManager>();   // ← 必需解析
```

`AddLuBanAgent` 系列**从不注册** `ISessionManager`（接口在框架定义、实现由宿主提供，见 `ISessionManager` 头注释与 `LuBanAgentExtensions.cs:39-140`）。而 `ToolPluginRegistry.GetPlugins(null)` 返回**全部**已启用插件，`BuildTools` 会调用每个插件的 `GetTools`，因此任何"注册了 AddLuBanAgent 但没注册 ISessionManager"的容器，在 `CreateAsync` 时都会抛 `InvalidOperationException: No service for type 'ISessionManager'`。

**已确认命中的在树路径**：

| 路径 | 说明 |
|------|------|
| `LubanAgentCli/Commands/CommandBase.cs:82-90` | 独立容器只注册 `IConfiguration/ConfigManager/IChatClient` → 无 ISessionManager |
| `LubanAgentCli/Commands/SkillCommand.cs:448-453` | 用上面这个容器 `CreateAsync(modelName: ...)` → **必抛** |
| 外部 NuGet 消费者 / `LuBan.XTestProject`（若调用 CreateAsync） | 同上 |

> `AIAgentUnitTest.TestDependencyInjection` 只解析工厂与注册表、不建 Agent，故测试不受影响；`AgentHostBuilder.cs:102` 注册了 `ISessionManager`，CLI/Codex 主流程不受影响。

**修复**（照抄同仓 `RetrievalToolPlugin.cs:52-58` 的既有写法）：

```csharp
var sessionManager = sp.GetService<ISessionManager>();
if (sessionManager is null) return Array.Empty<AIFunction>();   // 宿主无会话能力则不出该工具
```

---

## 3. P1：新功能与参数化机制

### P1-1 压缩工具绕过权限模式（Plan 模式会真实执行）

`ToolConfirmationService.cs:185` 把 `CompactContextAsync` 加进 `DefaultReadOnlyTools`；而 Plan 分支（`:241-246`）对 ReadOnlyTools 直接 `Allowed`：

```csharp
case ToolPermissionMode.Plan:
    if (ReadOnlyTools.Contains(toolName)) return Allowed;   // ← 压缩在这里真实执行
    _context.OnPlannedAction?.Invoke(toolName, arguments);
    return Planned;
```

但压缩**不是只读**：它 `MarkMessagesCompactedAsync`（归档消息）+ `AddMessageAsync("summary")`（写库），从模型上下文角度是**破坏性且不可逆**的。同时该工具自身也没调用 `IToolConfirmationService.EvaluateAsync`（spec §3.3 曾要求注入），等于**完全绕过权限门控**。

**建议**：从 `DefaultReadOnlyTools` 移除；Plan 模式走 `OnPlannedAction` 记录而不执行。若坚持"压缩不该打断用户"，应新增独立的"免确认工具"名单（`AutoConfirmTools` 已有此语义）并与 Plan 模式互斥，而不是借用"只读"语义。顺带：`DefaultReadOnlyTools` 的 XML 注释只写了 Plan 模式用途，而 Default 路径也依赖它，注释应补全。

### P1-2 `targetCount` 无边界校验

```csharp
var target = targetCount ?? _options.Session.CompactTargetMessages;   // 未做任何 clamp
```

LLM 可传 `0` / 负数 / 极大值（参数描述只说"压缩后保留的消息数"）。`target=0` 时 `keptCount = reduced.Count - 1` 可能为 0 → `compactedIds` 覆盖全部消息 → **一次调用即可清空会话上下文**（数据仍在库中但不再进入模型），且无确认。建议 `Math.Clamp(target, 1, messages.Count)`，或强制下限（如 ≥ `CompactThreshold`）。

### P1-3 `toolsOptions?.X ?? _options` 是不成立的回落

`ToolGroupOptions` 的 6 个组属性全部 `= new()`（`ToolGroupOptions.cs:34-59`），**永不为 null**，所以：

```csharp
var opts = toolsOptions?.Script ?? _options;   // toolsOptions 非 null 时永远走前半
```

而 `AgentProfile.CreateAgentAsync` 现在**总是**传 `BuildToolOptions()`（两个 Profile 都返回带 Browser/Script 覆盖的实例）→ 未被显式设置的组（Web / Retrieval / LocalMemory / FileSystem 之外的其它成员）一律被"框架默认值"整体替换，宿主通过 `LuBanAgent:Tools:*`（appsettings / config.json / 用户级配置）提供的值被静默丢弃。

今天表现不严重（宿主已删 `Tools` 节；且 `RetrievalToolOptions`/`LocalMemoryOptions` 的框架默认值恰好等于原 appsettings 值），但 `LocalMemoryOptions.DatabasePath`（"留空则使用默认用户数据目录"）这类用户级配置一旦设置就会被忽略——**记忆库换库 → 用户看到"记忆丢了"**。Agent 侧 `LubanAgentCore/Hosting/AgentHostBuilder.cs` 的检索装配同样读配置，长期看两边会不一致。

**建议**（任一）：
1. 组属性改可空：`public BrowserToolOptions? Browser { get; set; }` → `??` 回落成立（注意两个 Profile 的 `Browser = { Headless = false }` 写法需改为 `Browser = new BrowserToolOptions { Headless = false }`）；
2. 提供"基于当前生效配置克隆 + 只覆盖显式项"的入口，例如 `ToolGroupOptions.From(options.Value.Tools)` 后由 Profile 修改；
3. 至少把语义写进接口注释：**"传非 null 即整组替换，未设置成员取框架默认值；null 才使用宿主配置"**（现注释"null 使用构造时默认值"易误读）。

### P1-4 删除 `appsettings` Tools 节后的真实回归：`pwsh`

```csharp
// ScriptToolOptions.cs
public string Shell { get; set; } = "pwsh";          // 框架默认
public int DefaultTimeout { get; set; } = 120000;    // 框架默认
```

原 `appsettings.json` 为 `Script: { Shell: "cmd", DefaultTimeout: 30000 }`，该节已随 `f23029d` 删除。于是：

- 主 Agent：`NormalAgentProfile` / `RagAgentProfile` 显式 `Script = { Shell = "cmd", DefaultTimeout = 30000 }` ✓
- **子 Agent**：`LuBanAgentFactory.CreateSubAgentAsync` 由 `Orchestration/SubAgentFactory.cs:90-94` 调用且**不传** toolsOptions → 取 `_options`（已删配置 → 框架默认）→ `Shell = "pwsh"` ✗
- **SkillCommand**：`CreateAsync(modelName: ...)` 不传 → `pwsh` ✗
- 外部消费者（NuGet）同 `pwsh` ✗

`ScriptToolGroup.RunShellAsync` 直接以 `_options.Shell` 作为可执行文件启动进程（`ScriptToolPlugin.cs:132-142`），未装 PowerShell 7 的机器会命中"Shell 工具不可用: pwsh"分支；超时也从 30s 变 120s。

**建议**：让框架默认值贴合平台（Windows `cmd` / Unix `bash`，或用 `OperatingSystem.IsWindows()` 初始化），并让 `CreateSubAgentAsync`/`SkillCommand` 也传 Profile 选项，避免主/子 Agent 两套行为。

### P2 级（新功能内部）

| ID | 位置 | 问题与建议 |
|----|------|-----------|
| P2-1 | `:146-154` | 结果 JSON 里回灌 `oldSummary.Content` + `newSummary` 全文（各可达数千字符），而工具目的正是省 token；且新摘要下一轮会被 provider 自动注入，属重复。建议只返回长度/计数（`oldSummaryChars`/`newSummaryChars`）。另 `compactedCount = compactedIds.Count` 把被归档的旧摘要也算进去，报数偏大。 |
| P2-2 | `:130` | `reducer.ReduceAsync(history, CancellationToken.None)` 硬编码；方法签名未声明 `CancellationToken`，`AIFunctionFactory` 也就不会注入 → 一次摘要 LLM 调用无法被 ESC 取消。建议加 `CancellationToken ct = default` 并透传。 |
| P2-3 | `:103` | 用全局 `ISessionManager.CurrentSession`：编排子 Agent 并行运行时该指针指向主会话 → **子 Agent 调用会压缩别人的会话**。建议从工具调用上下文取 session（或子 Agent 场景不注册该工具组）。 |
| P2-4 | `:140-144` | 先 `MarkMessagesCompactedAsync` 再 `AddMessageAsync`，中间失败则"消息已归档但没有摘要"= 不可逆净损失。建议先写摘要、后标记，或用事务。同缺陷在 `SessionChatHistoryProvider.cs:127-130` 已存在。 |
| P2-5 | `:116-144` | 阈值判断 / 摘要替换 / 归档计算与 `SessionChatHistoryProvider.ProvideChatHistoryAsync:113-137` 逐行重复；`MapRole` 已是第 3 份（provider、插件、`LuBanAgent.cs`）；`EstimateTokens` 被内联成 `Length/4`。建议抽 `SessionCompactor.TryCompactAsync(...)` 单一实现。 |
| P2-6 | `LuBan.AIAgent.csproj:12` | `GetTools` 加参数、`CreateAsync` 加参数都是 breaking change，版本仍是 `2026.9.11.1`。若该版本已发布则本次无法覆盖推送；建议升到 `2026.9.14.x` 并在 README 标注破坏性变更与迁移方式。 |

---

## 4. 未提交改动（5 文件）

| 文件 | 评价 |
|------|------|
| `LuBan.XTestProject/AIAgentUnitTest.cs` | **必要修复**：`972c862` 删掉了 `DatabaseToolOptions`/`RedisToolOptions` 类，但该测试仍引用它们 → 当时测试工程编不过，本次改动正是修复。**但新行 `Script = new ScriptToolOptions { Enabled = false }` 顶格无缩进（第 100 行）**，提交前请整理。 |
| `Tools/Browser/BrowserToolPlugin.cs` + `Infrastructure/PlaywrightSession.cs` | `SetOptions(toolsOptions?.Browser ?? _options)` 实现运行期注入，方向正确；但 (a) `_options` 由 `readonly` 改为可变且**无锁**，`GetPageAsync` 侧读取无同步；(b) 注释要求"需在首次 GetPageAsync 之前调用"，而 `GetTools` 每次建 Agent 都会调用——同一 Scoped 会话内已初始化时，`SetOptions` 静默不生效（只有后续重建页面才生效），建议 `_initialized` 时记录警告或改为构造注入。 |
| `Tools/Context/CompactContextToolPlugin.cs` | 把角色映射从三元表达式改为 `MapRole`（更正确）✓；但**文件末尾缺换行**，且 `MapRole` 即 P2-5 的第 3 份拷贝。 |

---

## 5. 上一轮问题复检（仍未修复）

| 级别 | 位置 | 状态 |
|------|------|------|
| P1 | `LubanAgentCodex/ViewModels/MainWindowViewModel.cs:703-704` | ✗ 仍旧 `tool.State` 先于 `tool.ErrorMessage` → 失败卡片恒显示「工具执行失败」，真实错误丢失 |
| P1 | `LubanAgentCli/App/ViewModels/ConversationViewModel.cs:88,749,773,845` | ✗ 仍旧单槽 `_currentToolBlock`，不按 `CallId` 匹配 → 同轮多工具错配 + 首个块动画 Timer 永久运行 |
| P2 | `LubanAgentCodex/Views/Controls/ToolCallCard.axaml.cs:90,127,138,148,164` | ✗ 仍无 attach 恢复计时器；`PropertyChanged` 重复订阅；参数文本不复位；耗时基准取创建时刻 |
| P2 | 两宿主 | ✗ 失败文案不一致，CLI 额外重复一行 `❌ 工具执行失败: …` |
| P2 | `LubanAgentCore/Utils/ToolDisplayNames.cs:41-44` | ✗ 短名全局映射歧义（`SaveAsync/SearchAsync/ListAsync/DeleteAsync`），MCP 同名会被错译 |

---

## 6. P3 与残留

- `luban-agent/README.md:61-62` 与 `README.en.md:61-62` 仍列「数据库工具 / Redis 工具」两行（`LubanAgentCli`、`LubanAgentCodex` 的 README 已清）→ 根 README 漏改。
- `Luban.AIAgent/Abstractions/ToolConfirmationService.cs:235` 注释「避免脚本 类工具绕过权限模式」多一个空格（删词残留）。
- `ToolPluginRegistry.GetAllFunctions`（`:79-82`）全仓已无调用方 → 死代码；它还把 `toolsOptions` 固定传 `null`，若保留建议同步参数化或标记 `[Obsolete]`。
- `bin/Debug/.../appsettings.json` 仍是带 `Tools`/`Database`/`Redis` 的旧副本（构建产物，重建即消失，仅提示运行旧二进制会读旧配置）。

---

## 7. 建议处理顺序

1. **立刻**：P0-1（`GetService` + 空列表回落）——它是唯一会抛异常的项，且命中现有 `SkillCommand`。
2. **同批**：P1-1（Plan 模式语义）、P1-2（clamp）、P1-4（`pwsh` 默认值 / 子 Agent 传参）、P2-4（归档顺序）。
3. **接着**：P1-3 的语义澄清（至少补注释；有条件则改组属性可空）、P2-1/P2-2/P2-3、P2-5 抽公共压缩器、P2-6 升版本。
4. **收尾**：整理未提交 3 项并提交；再修上一轮遗留的 Codex/CLI 展示层 P1、P2。

---

*本报告基于静态审查，未经编译验证（沙箱 NuGet 环境故障 NETSDK1060）。P0-1、P1-4、P2-6 等结论均由类型定义、DI 注册与调用点直接推导。*

---

# 附录 A：复核（2026-09-14 17:25，第三轮）

## A.1 代码状态：与本报告撰写时完全一致

| 仓库 | 复核结果 |
|------|----------|
| luban-framework | HEAD 仍为 `794ba09`；工作区仍是同样 5 个 `M` 文件（`PlaywrightSession.cs`、`SessionChatHistoryProvider.cs`、`BrowserToolPlugin.cs`、`CompactContextToolPlugin.cs`、`AIAgentUnitTest.cs`） |
| luban-agent | HEAD 仍为 `f23029d`；工作区仅多出本报告（未跟踪） |

**结论：正文第 1—6 节全部成立，无一条需要撤销。** 以下为逐条复核证据、以及本轮新增的发现。

## A.2 关键结论二次核实（附证据链）

**P0-1（会抛异常）——已核实，证据链闭合**

```
CompactContextToolPlugin.cs:61   sp.GetRequiredService<ISessionManager>()
LuBanAgentExtensions.cs:54       注册 CompactContextToolPlugin（无条件）
LuBanAgentExtensions.cs:39-140   AddLuBanAgent 两个重载均不注册 ISessionManager
                                 （全框架 grep "AddSingleton<ISessionManager" → 无匹配）
ToolPluginRegistry.cs:63-71      GetPlugins(null) → 返回全部已启用插件（IsEnabled 恒 true）
LuBanAgentFactory.cs:157         BuildTools → 每个插件 GetTools → 命中上面第 61 行
CommandBase.cs:82-90              独立容器只注册 IConfiguration/ConfigManager/IChatClient
SkillCommand.cs:448-453          用该容器调 CreateAsync(modelName:...) → 必抛
```
用户视角：在 CLI 里执行 `/skill <名称>`（带交互表单那条路径）会在建 Agent 时 `InvalidOperationException: No service for type 'ISessionManager'`。

**P1-1 / P1-2 ——已核实**：`ToolConfirmationService.cs:185` 含 `CompactContextAsync`，`:241-246` Plan 分支对其直接 `Allowed`；插件内无 `EvaluateAsync` 调用。`CompactContextToolPlugin.cs:107` 的 `target = targetCount ?? ...` 之后无任何 clamp。

**P1-3 ——已核实，且本轮补齐了"谁来消费配置"的完整对照（见 A.3）**：
```
RetrievalBootstrap.cs:40  configuration.GetSection("LuBanAgent:Tools:Retrieval")   ← 宿主装配读配置
MemoryRecallRule.cs:71,80 _options.Value.Tools.LocalMemory                        ← 规则读配置
RetrievalToolPlugin.cs:63 toolsOptions?.Retrieval ?? _options.Value.Tools.Retrieval ← 工具读传参
LocalMemoryToolPlugin.cs:57 toolsOptions?.LocalMemory ?? _options                 ← 工具读传参
```

**P1-4 ——已核实**：`ScriptToolOptions.cs:39,54` 默认 `pwsh` / `120000`；`SubAgentFactory.cs:90-94` 未传 toolsOptions；`SkillCommand.cs:453` 未传 toolsOptions；`ScriptToolPlugin.cs:132-142` 直接以 `_options.Shell` 作为可执行文件。

## A.3 【新增】参数化有效性矩阵（本次改造到底哪些组真的生效）

| 工具组 | `toolsOptions` 覆盖是否真正生效 | 依据 |
|--------|------------------------------|------|
| Script | ✅ 生效（Shell/DefaultTimeout/PythonPath/LuaPath） | `ScriptToolPlugin.cs:132,138,142` 读 `_options` |
| Web | ✅ 生效（MaxCharacters） | `WebToolPlugin.cs:113-115` 读 `_options.MaxCharacters` |
| Browser | ✅ 生效（有瑕疵：无锁 + 可能已初始化） | `PlaywrightSession.SetOptions` → `_options` 驱动启动参数 |
| Retrieval | ⚠️ **仅 `MaxResultChars` 生效**；`ModelId`/`DefaultTopK`/`MaxFileSizeKB` 工具侧根本不消费 | `RetrievalToolPlugin.cs:172` 是组内唯一 `_options` 用法；`SearchCodeAsync` 的 `topK = 5` 是字面量默认值 |
| **LocalMemory** | ❌ **空转**：`GetTools` 把 `memoryOptions` 传进 `LocalMemoryToolGroup`，但该类 4 个方法**从不读 `_options`**（`SearchAsync` 的 `topK = 5` 同为字面量）→ `Tools:LocalMemory:DefaultTopK` 从未生效 | `LocalMemoryToolPlugin.cs:79-215` |
| FileSystem | ➖ 仅签名兼容（`AllowedRoots` 由 `PathGuard` 消费，不经此处） | 与 spec 一致 |

**这条矩阵把 P1-3 的影响面收窄并说清了本质**：不是"所有配置都被丢弃"，而是**同一份配置出现了两条读取路径**——宿主/规则走 `IConfiguration`，工具走 `toolsOptions`；且 `LocalMemory` 组的工具侧根本没用上。用户若在 `config.json` / 用户配置里设 `Tools:LocalMemory:DefaultTopK` 或 `Tools:LocalMemory:RecallTopK`，会出现"召回是 5 条、工具搜索仍是 5 条默认/或反之"的行为不一致，且完全无提示。

**建议**：`LocalMemoryToolGroup` 的 `SearchAsync` 默认值改为 `int? topK = null` → `topK ?? _options.DefaultTopK`（让参数化真正生效）；同时给 `ToolGroupOptions` 组属性补可空或明确注释（正文 P1-3）。

## A.4 【新增】上一轮遗漏的未提交改动：`SessionChatHistoryProvider.cs`

上一轮我只 diff 了 4 个文件，漏看了这个（12 行）。改动实质是把角色映射从
`.Select(m => new ChatMessage(m.Role == "user" ? ChatRole.User : ChatRole.Assistant, m.Content))`
换成 `MapRole(m.Role)`，并新增 `MapRole`（新增 `system` 分支）。两处调用点（`:110`、`:134`）同步替换。

- **正确性**：`MapRole` 覆盖 `user/assistant/system`，比原先（任何非 user 都当 assistant）更正确 ✓；`CompactContextToolPlugin` 里同一改动 ✓。
- **风险（低，当前为惰性）**：全仓 `AddMessageAsync` 的写入角色只有 `user/assistant/summary`（`SessionChatHistoryProvider:130,168,182`、`LuBanAgent.cs:193,196`、插件 `:144`），**没有任何地方写入 `system`**，所以 `ChatRole.System` 分支目前不会命中。一旦将来有写入方写入 `system` 消息，它会被夹在历史中段作为 `ChatRole.System` 发给模型——部分 OpenAI 兼容/其它厂商端点对"非首条 system"会报错或忽略。建议要么在 `MapRole` 处加注释说明该约束，要么把中段 system 折成 `ChatRole.User` 并加 `[system]` 前缀。

## A.5 【新增】复核结论一句话

`CompactContext` 的**功能正确性**（阈值判断、摘要替换、归档计算、仅写 user/assistant/summary、`IsCompacted` 默认 0 让新摘要可见）与 `SessionChatHistoryProvider` 完全一致，未发现逻辑错误；问题集中在**权限语义（P1-1）、入参安全（P1-2）、配置读取路径分裂（P1-3/A.3）、未传参路径的默认值回归（P1-4）、以及新增硬依赖导致抛异常（P0-1）**这五处。修完这五项后，本功能即可视为可用。
