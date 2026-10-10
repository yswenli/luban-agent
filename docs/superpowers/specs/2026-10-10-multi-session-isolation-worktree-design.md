# 多会话隔离与 Git Worktree 隔离设计（per-user / per-workspace / per-session 作用域重构）

**日期**: 2026-10-10 | **状态**: 设计完成，待审批

---

## 1. 背景与目标

用户诉求：同一用户（如张三）可在**不同工作区、不同会话**里**同时**运行不同任务——例如工作区 A 有 10 个会话，其中会话 1 在主树实现功能甲，会话 2 在 **git worktree** 里并行实现功能乙。无论 `LubanAgentCli` 还是 `LubanAgentCodex`，界面都应能随时切换查看/执行任意工作区的任意会话，且切换**不影响**后台会话继续运行。

现状根因：`luban-agent` 的「当前工作区 / 当前目录 / 当前会话 / 确认上下文 / 工作区级注册表」大量使用**进程级全局/静态状态**，且两端只在启动时构建**一个 root DI 容器**、对话流程从不 `CreateScope`，导致 `AddScoped` 实际等价于单例。因此当前**无法**在同一进程内安全地并发运行多个会话，也无法让不同工作区会话共存。

关键约束：工具实现与 `RuleEngine`/`SkillRegistry`/`MCPRegistry`/`ToolConfirmationContext`/`TempDirectory`/`PathGuard` 等基础设施位于框架仓库 `LuBan.AIAgent`；agent 仅通过 NuGet 包（`LuBan.*` 2026.x.x）依赖框架，改框架源码**必须重新打包发布**才对 agent 生效（见 `AGENTS.md`）。

目标：重构为 **per-user / per-workspace / per-session** 三层作用域，彻底去全局化；在此基础上交付可选的 **git worktree 会话隔离**；并让两端界面支持自由切换会话/工作区与后台并发运行。

---

## 2. 决策记录

| # | 问题 | 选择 | 说明 |
|---|------|------|------|
| Q1 | 隔离触发方式 | **自然语言 / skill 指令 + LLM 调工具** | 用户说「用 worktree 做」或技能提示词指示，由 LLM 调 `EnterWorktree` 工具进入隔离；无自动检测 |
| Q2 | 隔离作用域 | **显式指令才结束** | 同意后会话停留在 worktree，直到用户显式下达「完成/合并」 |
| Q3 | 合并回主分支 | **worktree 内提交 + merge 回原分支** | worktree 用临时分支，agent 在 worktree 内提交；「完成」时主树 `git merge` 该分支后删除分支与 worktree；**冲突则停止交人工** |
| Q4 | worktree 位置 | **仓库内 `.worktrees/<name>`** | `.gitignore` 已忽略 `.worktrees/` |
| Q5 | 总体架构 | **方案 C：重构为 per-user/per-workspace/per-session 作用域** | 引入 scoped 上下文替换所有 static/单例可变状态（否决「会话级绑定最小改」与「硬阻止切换」） |
| Q6 | 交付宿主 | **CLI + Codex 两端都要** | 但 P0/P1 先打通 `LubanAgentCore` + Codex；**CLI 后台并发纳入 P2** |
| Q7 | 空闲卸载 | **可配置，默认 30 分钟无活动** | `SessionRuntimeManager` 对闲置运行时 `Dispose` 释放 scope |
| Q8 | 文件级乐观并发检测 | **移除** | 不引入读取基线/写入前 mtime/哈希校验；worktree 仅由显式指令触发 |
| Q9 | 分期落地 | **选项 A：P0 为跨仓最小并发底座** | P0 含对框架的**最小**改动（`ToolConfirmationContext` 改 scoped）并发布新包；P1 完成框架 per-workspace；P2 收尾 |

---

## 3. 目标架构：分层作用域

### 3.1 三层作用域模型（嵌套 scope）

```
宿主进程
└─ 每窗口 DI 容器 (Codex 每窗口一个；CLI 单容器)          ← 隔离单元
   ├─ L1 per-user   (root 级, keyed by userId)
   │    ProviderHelper 缓存 / ModelCapabilityRegistry / ConfigManager(选中模型)
   └─ L2 per-workspace (IServiceScope, workspaceId→scope 缓存)
        RuleEngine / SkillRegistry / MCPRegistry / ToolPluginRegistry
        PathGuard / wiki+vector / 工作区根解析
        └─ L3 per-session (IServiceScope, 每活动会话一个)
             SessionManager 当前会话 / 有效根(worktree 或工作区)
             AllowedRoots / TempDirectory 解析 / ToolConfirmationContext
             SessionRuntime / worktree 会话状态
```

### 3.2 上下文契约（新增，放框架 `Abstractions` 供跨仓共用，agent 实现）

- `IUserContext`（root/keyed）：`UserId`。
- `IWorkspaceContext`（workspace-scoped）：`WorkspaceInfo Current`、`WorkspaceRoot`、按工作区已加载的规则/技能/MCP/检索。
- `ISessionContext`（session-scoped）：
  - `SessionInfo Current`
  - `string EffectiveRoot`（有活动 worktree 即 worktree 路径，否则工作区根）
  - `WorktreeSession? ActiveWorktree`
  - `IReadOnlyList<string> AllowedRoots`
  - `string ResolveTempDirectory()`
  - `bool IsWithinEffectiveRoot(string path)`
- 门面 `IAgentContext`：注入 session-scoped `ISessionContext` + 其父 `IWorkspaceContext`，替换今天所有 `WorkspaceManager.Current` 静态读点。

### 3.3 去全局化硬规则

1. **删除 `Directory.SetCurrentDirectory`**；所有路径绝对化；脚本/终端工具的 `workingDirectory` 显式传 `ISessionContext.EffectiveRoot`。
2. 停止改写单例 `IOptions<LuBanAgentOptions>`；新增 scoped `WorkspaceSessionOptions`，承载 `WorkspaceRoot` + `AllowedRoots`。
3. `TempDirectory.Resolver` 静态全局 → 上下文方法 `ResolveTempDirectory()`（按会话、AsyncLocal 按轮设，兼容子代理）。
4. 框架注册表 `RuleEngine`/`SkillRegistry`/`MCPRegistry`/`ToolPluginRegistry`/`PathGuard` 从 singleton 改 scoped；`ToolConfirmationContext`/`IToolConfirmationService` 改 session-scoped（保留 AsyncLocal 子代理作用域，但基座按会话隔离）。
5. 非 DI 静态读者（`SessionManager`、`SqliteVectorStore`、`WorkspaceWikiContext`）改为构造函数注入 `IAgentContext`，不再读静态。

### 3.4 static → scoped 映射（逐项）

| 现状 | 位置 | 目标 |
|------|------|------|
| `WorkspaceManager._current` / `Current` | `WorkspaceManager.cs:165,203` | `IWorkspaceContext.Current`（移除 static） |
| `_knownRoots` 静态缓存 | `WorkspaceManager.cs:189,196,667` | `IWorkspaceContext`（不再跨用户泄漏） |
| `IsWithinWorkspace`(static) | `WorkspaceManager.cs:217`；回调 `ToolConfirmationContextExtensions.cs:56` | `ISessionContext.IsWithinEffectiveRoot()` |
| `TempDirectory.Resolver` 静态 | 框架 `LuBan.Common\IO\TempDirectory.cs:29`；写 `WorkspaceManager.cs:174` | `ResolveTempDirectory()`（scoped/AsyncLocal） |
| `Directory.Set/GetCurrentDirectory` | 写 `WorkspaceManager.cs:360,396`；读 `AgentHostBuilder.cs:32,155`、`App.axaml.cs:137`、`FooterDataProvider.cs:93,97`、`StartupWorkspaceResolver.cs:126` 等 | 删除改写；读取点改显式根 |
| `IOptions.WorkspaceRoot` 写 | `WorkspaceManager.cs:404`；读 `WorkspaceWikiContext.cs:25,32`、A2A 桥接等 | `WorkspaceSessionOptions`（scoped） |
| `IOptions...AllowedRoots` 改写 | `WorkspaceManager.cs:622-626,645-647` | `ISessionContext.AllowedRoots` |
| `SessionManager._currentSession` | `SessionManager.cs:32,50` | session-scoped `ISessionContext.Current`；按会话显式传 `sessionId` |
| 框架注册表 singleton + `LoadFromWorkspace` | `LuBanAgentExtensions.cs:63,69,73,86,94` | scoped per-workspace |
| `ToolConfirmationContext` singleton + AsyncLocal | `LuBanAgentExtensions.cs:103-104` | session-scoped |
| `ProviderHelper` / `ModelCapabilityRegistry` static | `ProviderHelper.cs:27-53` / `ModelCapabilityRegistry.cs:26-27` | per-user keyed |

---

## 4. 会话运行时并发模型

### 4.1 `SessionRuntime`（L3 实例，每会话一个）

- 持有：L3 session scope（链在 L2 workspace scope → L1 user root 之上）、`LuBanAgent` 实例、**回合队列 + 运行循环**、事件流（进度/历史）、`ISessionContext`、session-scoped `ToolConfirmationContext`。
- 状态：`Idle` / `Running`；**UI 焦点不影响运行**——切走后台继续。
- 历史按现有 `SessionChatHistoryProvider` 落库，切回可恢复。

### 4.2 `SessionRuntimeManager`（每窗口容器内）

- `GetOrCreateAsync(userId, workspaceId, sessionId)`：命中复用，否则按嵌套 scope 创建。
- `Activate(sessionId)`：仅切换「当前展示的运行时」绑定，**不停止其它运行时**。
- 空闲卸载：闲置可配置（默认 30 分钟）后 `Dispose` 释放 scope；窗口/应用关闭时全部释放。
- 并发粒度：**每会话内串行（回合入队），跨会话并发**。

### 4.3 并发下必须处理的共享资源

- SQLite：`LuBanOrm` 全局 `SqlSugarScope`（`LuBanOrm.cs:37`）+ 连接串无参数（`LubanAgentCli\appsettings.json:219`、`LubanAgentCodex\appsettings.json:215`）→ 加 `WAL` + `busy_timeout`，应用层写重试。
- `PathGuard` 允许列表、`TempDirectory`、确认上下文、进度 sink：**全部 per-session**（见 §3）。
- 日志 `Logger`：只读型，可接受全局。

---

## 5. Git Worktree 隔离生命周期

### 5.1 新增工具（框架 `GitToolGroup`，均经 `IToolConfirmationService`）

- `EnterWorktreeAsync(reason)` → 调 `IWorktreeCoordinator.BeginAsync`
- `FinishWorktreeAsync()` → 调 `IWorktreeCoordinator.EndAsync`
- `WorktreeStatusAsync()` → 返回 `ISessionContext.ActiveWorktree`

### 5.2 进入 `BeginAsync`（宿主 `WorktreeSessionManager`）

1. 前置：`git rev-parse --is-inside-work-tree` 校验（非仓库→错误，agent 告知不可用）；主树脏→**警告并要求显式确认**（worktree 基于已提交 HEAD，未提交改动留在主树）。
2. 命名：目录 `.worktrees/<yyyyMMdd-HHmmss>`，分支 `luban-worktree/<yyyyMMdd-HHmmss>`。
3. `git worktree add -b <branch> <path> HEAD`（主工作树仍留在原分支，不切分支）。
4. 登记 `WorktreeSession { SessionId, WorkspaceId, WorktreePath, Branch, BaseBranch, BaseCommit, CreatedAt }` → 落库表 `ai_worktree_session` + 内存 registry。
5. `ISessionContext.EffectiveRoot = worktreePath`，`AllowedRoots` 加入该路径；此后所有文件/终端操作按 `EffectiveRoot` 解析。

### 5.3 隔离作用域

会话停留在 worktree 直到用户显式下达「完成/合并」；期间在 worktree 内自由读写。

### 5.4 结束 `EndAsync`（用户说「完成」）

1. worktree 内 `git add -A && git commit`（agent 在 worktree 内提交）；无改动则跳过。
2. 主工作树执行 `git merge <branch>`。**冲突 → 停止，保留 worktree+分支，交人工**，不自动解冲突。
3. 成功 → `git worktree remove <path>` + `git branch -D <branch>` + 删除 DB 记录 + `TempDirectory.Cleanup`；`EffectiveRoot` 还原为工作区根，`AllowedRoots` 还原。
4. **基线分支漂移检测**：若主树当前分支 ≠ `BaseBranch` → 警告并停止，交人工。

### 5.5 拒绝 / 放弃 / 切换

- 拒绝同意：不创建，留在主树继续。
- 中途「放弃」：`git worktree remove --force` + `git branch -D`，丢弃改动，还原 `EffectiveRoot`。
- **切会话**：按目标会话的 `ISessionContext` 重派生——registry 中有活动 worktree → `EffectiveRoot` = 该 worktree 路径并恢复；无 → 工作区根。worktree 目录与分支留在磁盘**不销毁**。
- **切工作区**：走各自 workspace scope；worktree 归属会话（在 workspace 内）。
- 同仓库多会话各持 worktree 由 git 多工作树天然支持，**无「只能一个」限制**。

---

## 6. 宿主适配（Codex / CLI）

### 6.1 Codex

- Shell（`MainWindow`）挂「会话工作区」；侧边栏已是「工作区 → 会话」两层列表（`Sidebar.axaml.cs:345-356`），选中会话即把详情视图（对话/工具卡/进度/输入）绑定到该会话运行时；列表项显示**运行中徽标**。
- 后台会话继续推进；未聚焦会话的确认弹窗 → 归属该会话视图 + 列表徽标；该回合阻塞等待（超时→Deny）。
- `AgentHostService` 纳入 DI（修 `SettingsWindow.axaml.cs:1673` 的 `GetRequiredService<AgentHostService>()` 解析异常）；会话切换的 scope 创建/释放做竞态保护。

### 6.2 CLI（P2）

- 新增会话切换视图（页签/列表）；后台会话在消息泵上继续推进，切回可见进度；沿用 `_inputQueue` 每会话回合入队模式。

---

## 7. 并发安全与已识别阻断项

当前「同进程两会话并发」不安全的根因是**进程级单例可变状态**（加锁无法修复），必须改作用域：

| 阻断项 | 位置 | 后果 | 属仓库 | 归期 |
|--------|------|------|--------|------|
| `ToolConfirmationContext` singleton + 每轮 `ConfigureForTurn`/`Reset` | `LuBanAgentExtensions.cs:103-104`；重配 `AgentHostService.cs:241,340`、`ToolConfirmationContextExtensions.cs:54-61` | turn B 覆盖 A 的回调/CancellationToken/Mode；A 结束 `Reset()` 清空 B → B 工具被误 Denied；会话级「全部允许」互泄 | framework | **P0** |
| `SessionManager._currentSession` singleton | `SessionManager.cs:32,50`；`SessionChatHistoryProvider.cs:74,157` | 两会话共享 current → 串会话、丢持久化 | agent | P0 |
| `AgentHostService` / `LuBanAgent._session` 一实例一会话 | `AgentHostService.cs:26,228`；框架 `LuBanAgent.cs:42` | 同实例两轮并发历史穿插 | framework/agent | P0 |
| `WorkspaceManager.Current` static | `WorkspaceManager.cs:165,203` | 进程单当前工作区 → 不同工作区并发硬阻断 | agent | P0（去静态） |
| 工作区级注册表 singleton（`RuleEngine`/`SkillRegistry`/`MCPRegistry`/`PathGuard`） | `LuBanAgentExtensions.cs:63,69,73,86,94` | 同工作区无害；不同工作区最后加载者覆盖 | framework | **P1** |
| 共享 `SqlSugarScope` + SQLite 无 WAL | `LuBanOrm.cs:37`；连接串 | 并发写可能 "database is locked" | 配置 | P0 |

结论：**同一工作区**两会话并发的致命项是 `ToolConfirmationContext`（framework 单例）→ P0 必须含框架最小改动；**不同工作区**并发还需 P1 的框架 per-workspace 作用域。

---

## 8. 错误处理与边界

- 非 git 仓库 / `git` 不在 PATH → `EnterWorktree` 返回错误，agent 告知不可用。
- 主树脏 → 警告 + 显式确认；目录/分支名冲突 → 自动加后缀重生成；目标路径已存在 → 报错；`.worktrees/` 未忽略 → 警告（当前已忽略）。
- Finish 冲突 / 基线分支漂移 → 停止交人工；`worktree remove` 因占用失败 → 报告并保留，给重试指引。
- 用户拒绝 → 不创建；中途放弃 → `--force` 移除丢弃。
- 空闲卸载/崩溃带活动 worktree → **不销毁**；再次激活该会话时从 registry 恢复。
- **进程崩溃遗留对账**：启动时扫描 `.worktrees/` + `ai_worktree_session`，孤儿（无对应活动会话）→ 提示「恢复或清理」；git 存在但 DB 无记录的 worktree → 提示 prune。
- 用户手工删除 worktree 目录 → 使用时检测缺失，报错并提示恢复/放弃。
- SQLite 并发锁 → WAL + busy_timeout + 写重试。

---

## 9. 分期交付（P0 / P1 / P2）

### P0 — 跨仓最小并发底座（交付：同工作区并发 + worktree + Codex 自由切换）

**framework（最小、加法为主，需发布新包）**
1. `ToolConfirmationContext` / `IToolConfirmationService`：`singleton → scoped`（`LuBanAgentExtensions.cs:103-104`），连带调整依赖它的插件生命周期（**风险点**：若插件为 singleton 注入它，需改 scoped 或注入访问器——落地前先核实）。
2. 新增抽象（加法）：`IUserContext` / `IWorkspaceContext` / `ISessionContext` / `IWorktreeCoordinator`。
3. `GitToolGroup` 增 `GitWorktreeAdd/Remove/List`。
4. 发布新版本包。

**agent**
1. 实现上下文三契约；**每会话一个 `IServiceScope`**（嵌套 L1→L2→L3）。
2. `SessionRuntime`（回合队列 + 运行循环 + 事件流）+ `SessionRuntimeManager`（GetOrCreate/Activate/空闲卸载）。
3. `AgentHostService` / `LuBanAgent` 改 per-session；`SessionManager` 去 `_currentSession` 静态、按会话传 `sessionId`。
4. `WorkspaceManager.Current` static → `ISessionContext`；`WorktreeSessionManager` 实现 `IWorktreeCoordinator`；`ai_worktree_session` 表 + 启动对账。
5. Codex 多会话 UI；SQLite 加 `WAL` + `busy_timeout`；修 `AgentHostService` DI。

**边界**：同工作区多会话**并发**安全；跨工作区可自由切换查看，但**同时运行**留到 P1（P0 跨工作区并发回合退化为串行/临时锁保护）。

### P1 — framework per-workspace 作用域（交付：跨工作区并发，需再发布 framework 包）
- framework：`RuleEngine`/`SkillRegistry`/`MCPRegistry`/`ToolPluginRegistry`/`PathGuard` singleton→scoped；`TempDirectory.Resolver` 静态→per-context。
- agent：`WorkspaceManager` 彻底去静态、workspace scope 管理、移除「每轮重载」临时规避。

### P2 — CLI 后台并发 + 收尾
- CLI：会话切换视图 + 后台消息泵，支持后台会话运行。
- per-user keyed 缓存（`ProviderHelper`/`ModelCapabilityRegistry`）；清理剩余静态；空闲卸载策略调优。

**依赖顺序**：P0 → P1 → P2；P0、P1 各触发一次 framework 重打包发布（`AGENTS.md` 流程）。

---

## 10. 测试策略

- framework（MSTest，`LuBan.XTestProject/LuBan.UnitTestProject.csproj`）：git worktree 工具单测（mock `ProcessRunner`）；scoped `ToolConfirmationContext` 两会话隔离单测；scoped 注册表按工作区隔离单测。
- agent：**无自动化测试**，验证 = 构建 + 手动冒烟（`LubanAgentCli/docs/TUI-SMOKE-TEST.md` 范式）+ Codex 多会话并发冒烟。
- 集成：临时目录建真实 git 仓库跑 worktree 增/合并/删往返；两会话并发确认态互不串扰。

---

## 11. 验收要点

- 构建：`dotnet build luban-framework/LuBan.FrameWork.sln` 与 `dotnet build luban-agent/LubanAgent.slnx` 通过（全量 sln 受 `WebApplication1` 死私源阻断，用具体项目构建规避）。
- 单测：worktree 工具、确认上下文/注册表作用域隔离。
- 手动冒烟（Codex）：同工作区两会话并发；会话 2 进 worktree 实现功能乙、会话 1 主树实现功能甲互不干扰；切走会话 2 其后台继续；「完成」合并回主分支；构造 merge 冲突验证「停止交人工」；崩溃后启动对账提示恢复/清理。
- 手动冒烟（CLI，P2）：多会话切换 + 后台并发。

---

## 12. 风险与边界

- **跨仓发布依赖**：P0/P1 各需发布 `LuBan.*` 新包；框架改动在 agent 侧不会自动生效。
- **`ToolConfirmationContext` 改 scoped 的连带影响**：依赖其生命周期的框架插件若为 singleton，需同步调整；落地前先核实插件注册与依赖注入路径。
- **P0 跨工作区并发退化**：P0 期间跨工作区同时运行会互相覆盖工作区级注册表，故退化为串行/临时锁；完整跨工作区并发在 P1。
- **SQLite 桌面场景隔离**：无 `HttpContext` 时 `SqlSugarScope` 的 AsyncLocal 隔离对两个并发 `async` 流是否生效需实测；必要时对会话写加应用层串行化。
- **CLI 后台并发**成本较高（消息泵/视图重构），已单列 P2。
- **`.worktrees/` 与 `TempDirectory` 清理**需与会话生命周期、崩溃对账三方对齐，避免误删未合并成果。