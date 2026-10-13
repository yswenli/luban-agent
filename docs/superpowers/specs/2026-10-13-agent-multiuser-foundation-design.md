# 多用户地基设计（LuBan.AIAgent 服务端化前置 / CLI·Codex 单用户固定 yswenli）

- 日期：2026-10-13
- 状态：待实现
- 范围：方案 A（只打地基，不新建服务器宿主）
- 涉及仓库：`luban-agent`（LubanAgentCore、LubanAgentCli、LubanAgentCodex）、`luban-framework`（LuBan.AIAgent 契约）

## 1. 背景与动机

`LuBan.AIAgent` 将来要放到服务器上运行，必须支持多用户；`LubanAgentCli` / `LubanAgentCodex` 是单用户桌面/终端产品，默认用户固定为 `yswenli`。当前代码的身份与隔离能力远不足以支撑服务端多用户：

- 身份抽象存在但未接线：`IUserContext` 唯一实现在 `LubanAgentCore/Context/UserContext.cs`，由 `AgentHostBuilder.cs:104-105` 用 `Environment.UserName` 注册为 **Singleton（进程级）**；唯一消费方是 `UserScopedServiceExtensions.ResolveUserKey`，仅驱动 keyed 的 `ProviderService` / `ModelCapabilityService` 缓存。
- 会话 / 工作区归属与身份脱节：`SessionManager.CreateSessionAsync` 按传入值落库，调用点硬编码 `"default"`（`LubanAgentCli/Commands/SessionCommand.cs:188`、`LubanAgentCodex/Views/MainWindow.axaml.cs:565`、`.../Views/Controls/Sidebar.axaml.cs:617`、`LubanAgentCore/Services/WorkspaceManager.cs:351`、`LubanAgentCore/Services/SessionManager.cs:267`）或传 `null`/`""`（`MainWindowViewModel.cs:1289`/`1267`）；`WorkspaceManager.CreateWorkspaceAsync` **从不写 `UserId`** → 工作区 `UserId` 恒为 `null`；`GetUserWorkspacesAsync(null)` 退化为全量。
- Provider 配置为进程级单例：`ConfigManager` 读机器级 `%LocalAppData%\LuBan\AIAgent\config.json`，无用户分区。
- 本地记忆为单文件 `%LocalAppData%\LuBan\AIAgent\localmemory.db`，含跨用户可见的"全局行"（`WorkspaceId IS NULL`）。
- `LuBan.AIAgent` 是纯类库，无任何可执行 / Web 宿主；全仓唯一"服务端"是 `LubanAgentA2A` 的 loopback 宿主，不接管外层容器。**服务端宿主目前不存在**（本轮不新建）。

## 2. 目标与非目标

### 目标
1. 让 `LuBan.AIAgent` / `LubanAgentCore` 具备"请求级用户身份"的抽象与接线，使宿主可注入用户。
2. 会话、工作区、Provider 配置 / 模型列表、本地记忆按用户隔离。
3. CLI / Codex 固定单用户 `yswenli`，改造后行为与现状等价（存量数据无缝迁移）。
4. 用户级可变缓存跨会话 / 跨请求复用（模型列表不重复拉取）。

### 非目标（本轮明确不做）
- 不新建 ASP.NET Core 服务器宿主项目（地基就绪后由后续立项承载）。
- 不引入认证 / 登录 / JWT / `ClaimsPrincipal`。
- 不引入 AsyncLocal / `IUserContextAccessor` 隐式环境身份（身份一律走 DI scope）。
- 不按用户拆分日志目录、附件临时目录。
- 不按用户拆分工作区文件系统（多用户指向同一路径的隔离不在本轮范围）。
- 不改动 A2A 现有 `X-Api-Key` 机制。

## 3. 关键约束与事实

- **Scoped 不跨 scope 共享**：子 scope 不继承父 scope 的 Scoped 实例。因此"按用户隔离 + 跨会话复用"**不能**依赖 scope 树，必须由 Singleton 注册表按 `userId` 分桶承载；scope 只承担"身份"与"请求态"。
- **契约归属**：`IUserContext`、`ISessionManager`、`ILocalMemoryStore`、`ILocalMemoryService`、`IWorkspaceContextProvider` 定义在 `luban-framework`（`LuBan.AIAgent`）；**`IWorkspaceManager` 定义在 agent 本地**（`LubanAgentCore/Services/WorkspaceManager.cs:76`），其契约改动不触发 framework 发版。
- **`ValidateScopes=true` + `ValidateOnBuild=true`**（`AgentHostBuilder.cs:169-174`）：Singleton 消费 Scoped 会在构建期抛异常，这是本次连锁改造的强制约束。
- **会话 scope 现有范式**：`SessionRuntimeManager.CreateRuntime` 从根容器 `CreateScope()` 并回填 `SessionContextState`（`SessionRuntimeManager.cs:76-80`）；本次身份注入复用同一范式。
- **ORM 租户能力未使用**：`BaseRepository<T>(tenantId)` 具备多租户能力，但 agent 全部使用默认租户。本轮不启用 ORM 租户，隔离靠 `UserId` 列 + 手工过滤（与现状方向一致，避免牵连所有仓储）。
- **`yswenli` 仅为署名**：全仓 `yswenli` 出现在版权头注释；本机 `Environment.UserName` = `yswen`。默认用户 `yswenli` 是本次新引入的常量语义。

## 4. 设计决策摘要

| 编号 | 决策 | 结论 |
|------|------|------|
| D1 | 交付范围 | 方案 A：只打地基，不新建服务器宿主 |
| D2 | 隔离范围 | 会话 + 工作区 + Provider 配置/模型列表 + 本地记忆 |
| D3 | 身份注入方式 | Scoped `IUserContext`，回填范式（不引入 AsyncLocal） |
| D4 | 用户级共享态承载 | Singleton 注册表 `UserContextRegistry` + `GetOrAdd` 分桶 |
| D5 | 本地记忆形态 | 单文件 + `UserId` 列 + 单例 store + Scoped 服务传参（甲） |
| D6 | 默认用户 | 常量 `DefaultLocalUserId = "yswenli"`，不加配置项 |
| D7 | Provider 配置隔离 | 每用户一个 `config.json` + `ConfigManager` 注册表化 |
| D8 | 存量数据 | 一次性、幂等迁移到 `yswenli` |
| D9 | 顶层 DI 拓扑 | 方案 A：用户 scope + 注册表（用户级可变共享态放注册表内"每用户单例"，DI 注册为 Scoped 工厂返回该单例） |
| D10 | 框架契约改动 | 破坏性变更 + 发新包（不加兼容重载） |

## 5. 身份模型

- 契约 `LuBan.AIAgent.Abstractions.IUserContext.UserId`（`string?`）不变。
- 新增 `UserContextState`（Scoped，可写）：`string? UserId { get; set; }`。
- `UserContext` 改为从 `UserContextState` 派生：`UserId => state.UserId`。
- 新增常量 `DefaultLocalUserId = "yswenli"`（置于 `LubanAgentCore`，CLI/Codex 引用）。
- scope 创建后由创建方回填 `UserContextState.UserId`：
  - CLI / Codex：应用启动时建"用户 scope"，填 `DefaultLocalUserId`。
  - 服务器（未来）：中间件认证后建 scope 并填认证用户，核心无需改动。
  - 会话 scope：由 `SessionRuntimeManager.GetOrCreate(...)` 建 scope 后回填。

## 6. DI 拓扑（方案 A 落地）

### 根容器（真正的进程级单例）
仓储（`SessionRepository`、`SessionMessageRepository`、`WorkspaceRepository`、`LlmWiki*Repository`、`WorktreeSessionRepository`）、`SessionRuntimeManager`、`UserContextRegistry`、`GitProcess`、嵌入器（`SerializingEmbeddingGenerator`）、`KnowledgeTaskService`、`ILocalMemoryStore`（单文件）、`ILoggerFactory`、`IConfiguration`、无状态目录（`BuiltinSkillCatalog`、`BuiltinRuleCatalog`、`ICommandCatalog`）。

### 用户级共享态（注册表内"每用户单例"；DI 注册为 **Scoped 工厂 → 返回注册表内该用户单例**）
`ISessionManager`、`IWorkspaceManager`、`ConfigManager`、`IAppConfigReader`、`ProviderService`、`ModelCapabilityService`、`IProviderRouter`（连同底层 `ProviderCache`、`ModelCapabilityCache`）。

> 这些服务持有**用户级可变状态**（`SessionManager._currentSession`、`WorkspaceManager._current` / `_knownRoots`、模型列表缓存、Provider 客户端缓存、配置文件路径）。若注册为"真 Scoped"，会话 scope（从根派生）会各自重建一份 → `CurrentWorkspace` / `CurrentSession` / 已知根缓存丢失，`SqliteVectorStore`（`SqliteVectorStore.cs:48` 回退 `CurrentWorkspace`）与 `WorkspaceWikiContext.GetKnownRootPath` 退化；`IProviderRouter`（`LuBanChatClient`，持客户端字典）也会被每个会话 scope 重建（消费者 `LuBanAgentFactory:50`、`LlmTaskPlanner:40` 均在会话 scope）。故必须由注册表承担"跨 scope 共享"，DI 只是 Scoped 门面。

### 用户 scope（CLI/Codex 启动建一个并作为 `Services`；服务器每请求一个）
`UserContextState`、`IUserContext`、`IChatClient`、`AgentHostService`、`IWorkspaceContextProvider`、`WorktreeReconciler`、`AttachmentService`、`ILocalMemoryService`、`MemoryRecallRule`。

### 会话 scope（`SessionRuntimeManager` 从根派生）
沿用现有 `SessionContextState` 回填，额外回填 `UserContextState.UserId`；会话级服务（`LuBanAgentFactory`、工具插件、`DagScheduler` 等）不变。会话 scope 解析"用户级共享态"时经 Scoped 工厂命中**同一用户单例**（不重建）。

### 随之调整生命周期的连锁（`ValidateScopes=true` 强制）
必须从 Singleton 降为 Scoped（否则构建期即抛 captive dependency）：`AgentHostService`（`:130`）、`AttachmentService`（`:131`，消费 `IWorkspaceManager`）、`IProviderRouter`（`:70`）、`IWorkspaceContextProvider`（`:142`）、`WorktreeReconciler`（`:126`，消费 `IWorkspaceManager`）、`MemoryRecallRule`（框架 `LuBanAgentExtensions.cs:69`）、`ILocalMemoryService`（框架 `:113`）。同时移除 `AgentHostBuilder` 中 `ConfigManager` / `ProviderService` / `ModelCapabilityService` 的进程级单例与 keyed 注册，改由注册表统一提供。

### 根派生 scope 的身份回填（关键，易漏）
- **MS.DI 事实**：`IServiceScopeFactory.CreateScope()` **永远从根容器派生**，不继承当前 scope 的 `SessionContextState` / `UserContextState`（现有 `SessionRuntimeManager` 手动回填 `SessionContextState` 即为此）。
- 因此每个"根派生 scope"必须在创建后立即回填 `UserContextState.UserId`（必要时同时回填 `SessionContextState`），否则注册表 Scoped 门面会拿到空用户键、命中错误分桶。
- 新增统一辅助（如扩展方法 `CreateUserScope(userId)`：建 scope + 回填身份），替换下列裸 `CreateScope()`：
  - `SessionRuntimeManager.cs:76`（会话 scope，补回填 UserId）
  - `KnowledgeTaskService.cs:124/152`（后台索引/重建；需把触发方的 `userId` 传入并回填，否则 `IWorkspaceManager.GetKnownRootPath` 命中错桶）
  - `StartupWorkspaceResolver.cs:205`（已回填 `SessionContextState`，补 `UserContextState`）
  - `MainWindow.axaml.cs:692`、`LlmWikiManageWindow.axaml.cs:63`（检索 scope）
  - CLI：`Services` 本身即"用户 scope"，`RootView` 直接持有它；**移除** `TerminalGuiApp.cs:181` 的嵌套 `uiScope`（原为 root→scoped 而设，用户 scope 下不再需要）
  - 框架 `CompactContextToolPlugin.cs:151`：本类为 Scoped，推荐改为**直接注入 `IChatClient`**，不建派生 scope
  - A2A 宿主（`A2AServerHost` / `InternalSubAgentHandlerAdapter` / `A2ARuntimeLifecycle`）派生 scope 同受影响，本轮按 `DefaultLocalUserId` 回填
- **兜底**：注册表 Scoped 门面在 `IUserContext.UserId` 为空时回退 `DefaultLocalUserId`（单用户宿主安全网，保证 CLI/Codex 行为等价）；服务器宿主必须在每个请求派生 scope 显式设置用户（后续可在服务器宿主改为强校验）。

## 7. 用户级共享态注册表

新增 Singleton `UserContextRegistry`：

- 内部 `ConcurrentDictionary<string, UserBucket>`，`GetOrAdd(userId)` 惰性建桶；构造时注入根 `IServiceProvider`（用于从根解析仓储 / 目录等单例依赖）。
- `UserBucket`（每用户一份）持有该用户专属的**单例**：`ConfigManager`、`ProviderCache`、`ModelCapabilityCache`、`ProviderService`、`ModelCapabilityService`、`IProviderRouter`、`ISessionManager`、`IWorkspaceManager`。
- 创建 bucket 时完成接线（一律在注册表内 new 出来、不经 Scoped，避免 captive dependency）：
  - `ConfigManager` 用该用户 config 路径（`GetDefaultConfigPath(userId)`）加载，并**直接注入该用户 `ProviderService`**（取代 `AgentHostBuilder.cs:180` 的 `configManager.AttachServices(sp)`；`ConfigManager.GetAllModels` 现经 `_services.GetProviderService()` 解析，改造后直接用注入的 `ProviderService`）。
  - `ModelCapabilityCache.Initialize(cm.ModelCapabilities, cm.PersistModelCapability)`（取代现在 `AgentHostBuilder.cs:183-184` 的全局初始化）。
  - `IProviderRouter` 用该用户 `ConfigManager.Providers` 构建（取代现在 `AgentHostBuilder.cs:70-93` 的进程级单例）。
  - `SessionManager` / `WorkspaceManager` 由注册表 new 出来，**`userId` 构造期直接传入**（不注入 Scoped `IUserContext`）；`WorkspaceManager` 的其余依赖（`WorkspaceRepository`、`SessionRepository`、`ISessionManager`、`BuiltinSkillCatalog`、`BuiltinRuleCatalog`）从根解析。
- 替换现有 keyed 注册（`AgentHostBuilder.cs:111-118`）与 `IUserContext` Singleton 注册（`:105`）。
- Scoped 门面（`ISessionManager`、`IWorkspaceManager`、`ConfigManager`、`IAppConfigReader`、`ProviderService`、`ModelCapabilityService`、`IProviderRouter`）用**工厂注册**，按当前 `IUserContext.UserId` 从注册表取该用户单例：
  - `sp.GetRequiredService<IUserContext>().UserId` → `registry.GetBucket(userId).Xxx`
  - 工厂注册避免 captive dependency（注册生命期为 Scoped，满足 `ValidateScopes`）。
- 效果：**跨会话 / 跨请求复用靠注册表，scope 只承担身份与请求态。**

## 8. 会话 / 工作区归属

- `SessionManager` 改为在注册表中按 `userId` 构造（构造期持有该用户 `userId`，不注入 Scoped `IUserContext`）；`CreateSessionAsync` **从该 `userId` 落库**，移除 `userId` 参数。
- 删除 5 处硬编码 `"default"`：`SessionCommand.cs:188`、`MainWindow.axaml.cs:565`、`Sidebar.axaml.cs:617`、`WorkspaceManager.cs:351`、`SessionManager.cs:267`。
- `WorkspaceManager.CreateWorkspaceAsync` 写入 `UserId = 该用户 userId`（同样来自注册表构造，不注入 Scoped `IUserContext`）。
- **Manager 层**（`ISessionManager` / `IWorkspaceManager`）的 `GetUserSessionsAsync()` / `GetUserWorkspacesAsync()` 改为**无参、默认当前用户**；废弃 `null = 全量` 语义。**仓储层**（`SessionRepository.GetUserSessionsAsync(string userId)`、`WorkspaceRepository.GetUserWorkspacesAsync(string userId)`）保留显式 `userId` 参数，由 Manager 传入当前用户。更新调用点：`MainWindowViewModel.cs:1267`（去 `""`）、`WorkCommand.cs:126`、`StartupWorkspaceResolver.cs:124`、`App.axaml.cs:129`、`AgentHostBuilder.InitializeWorkspaceAsync`（`:202`，现用 `workspaceRepo.GetAllAsync()` 取全部用户工作区，改为经 `IWorkspaceManager.GetUserWorkspacesAsync()` 取当前用户）。
- `SessionRuntimeManager.GetOrCreate(sessionId, userId, workspaceId, workspaceRoot)` 增加 `userId` 入参，建 scope 后回填 `UserContextState`；`AgentHostService`（Scoped）传入当前 `IUserContext.UserId`。
- `WorktreeReconciler`（改 Scoped）按当前用户过滤工作区。

## 9. 本地记忆 UserId（甲方案）

- `LocalMemory` 表新增 `UserId TEXT NOT NULL`。
- 去重键 `(WorkspaceId, ContentHash)` → `(UserId, WorkspaceId, ContentHash)`。
- `ILocalMemoryStore` 各方法增加 `userId` 参数：`UpsertAsync`、`DeleteAsync`、`ListAsync`、`LoadAllAsync`、`LoadByIdsAsync`、`DeleteExpiredAsync`。
- "全局行"变为**每用户全局行**（`WorkspaceId IS NULL AND UserId = @userId`）。
- `ILocalMemoryService`（框架，改 Scoped）注入 `IUserContext`，调用 store 时传当前 `userId`。
- `LocalMemoryService` 的倒排索引字段 `_inverted`（`LocalMemoryService.cs:31`）是实例级可变状态；服务由 Singleton 改 Scoped 后，索引天然按用户隔离（CLI/Codex 的用户 scope 常驻进程，索引仅构建一次）。
- `MemoryRecallRule`（改 Scoped）随 `ILocalMemoryService` 生命周期调整。

## 10. 配置路径与存量迁移

- 配置路径改为 `%LocalAppData%\LuBan\AIAgent\users\{userId}\config.json`；`ConfigManager.GetDefaultConfigPath(userId)`。
- 一次性、幂等迁移（启动期执行）：
  1. 旧机器级 `config.json` 存在且目标用户文件不存在 → 移动到 `users\yswenli\config.json`。
  2. DB 中 `ai_session` / `ai_workspace` 的 `UserId` 为 `null` 或 `"default"` → 回填为 `yswenli`。
  3. `LocalMemory` 旧行 `UserId` 为空 → 回填为 `yswenli`。
- 迁移幂等：目标已存在或旧文件已不存在时跳过；可重复执行不产生副作用。

## 11. CLI / Codex 接线

- **CLI**：`StartupDialog` 建应用级用户 scope（`root.CreateScope()`），填 `UserContextState.UserId = DefaultLocalUserId`，`Services` 指向该 scope（替代现有 root 指针）；退出时随根容器异步释放。
- **Codex**：`App.axaml.cs` 同法建用户 scope，`_services` 指向它。
- **`CommandBase.BuildServiceProvider`**（`CommandBase.cs:82-92`）：现独立建容器，注册 Singleton `ConfigManager` 并直接 `ConfigManager.CreateChatClient()`，且无 `IUserContext`（一旦走 keyed / 注册表服务即抛）。改造后需改走 `AgentHostBuilder` 的注册表 + 用户 scope（补建 scope、回填 `DefaultLocalUserId`），不再自行 `new ConfigManager`。
- 全量清理硬编码 `"default"` / `null` / `""`。

## 12. 测试与验证

- 框架单测（MSTest，`LuBan.UnitTestProject.csproj`）：
  - 记忆按 `UserId` 过滤、每用户全局行互不可见。
  - `ConfigManager.GetDefaultConfigPath(userId)` 路径正确。
  - `UserContextRegistry` 同 userId 返回同实例、不同 userId 隔离。
  - 根派生 scope 未回填身份时注册表门面回退 `DefaultLocalUserId`；回填后命中对应用户桶。
- agent 无自动化测试 → 构建（`dotnet build LubanAgent.slnx`，0 警告 0 错误）+ TUI 冒烟（`LubanAgentCli/docs/TUI-SMOKE-TEST.md`）+ Codex 手动冒烟。
- **跨仓发布**：本次框架契约变更项为 `ISessionManager`（`LuBan.AIAgent/Sessions`）与 `ILocalMemoryStore` / `ILocalMemoryService`（`LocalMemory`，含生命周期 Singleton→Scoped）。签名 / 生命周期变更为**破坏性改动** → framework 升版本（`LuBan.AIAgent` 现 `2026.10.13.1`）、重新打包发布，agent 更新 NuGet 引用后才生效。**`IWorkspaceManager` 定义在 agent，不涉及 framework 发版。**

## 13. 风险与开放问题

1. **跨仓 + 发布链**（已决 D10：破坏性变更 + 发新包）：需按顺序 framework 发版 → agent 升级引用，存在两仓联动窗口。
2. **Singleton→Scoped 连锁面大**：`AgentHostService`、`AttachmentService`、`IProviderRouter`、`IWorkspaceContextProvider`、`WorktreeReconciler`、`MemoryRecallRule`、`ILocalMemoryService` 均需调整；`ConfigManager` / `ProviderService` / `ModelCapabilityService` 的进程级注册改由注册表统一提供；`ValidateOnBuild` 兜底暴露遗漏。
3. **CLI 用户 scope 生命周期**：必须活到进程退出。改为"用户 scope 直接作为 `RootView` 容器"后，需理顺与 `TerminalGuiApp` 的释放顺序（用户 scope 随根容器释放，勿在 UI 退出时提前 `Dispose`），避免 Scoped 服务不可解析。
4. **工作区文件系统共享**：不同用户指向同一路径的隔离不在本轮范围。
5. **服务器未来项**：`SessionRuntimeManager` 目前仅按 `sessionId` 分桶；服务器多用户时需确认键含 `userId`（本次已加 `userId` 入参，为将来留口）。
6. **迁移时机**：迁移在启动期执行，若 DB 处于并发多进程访问，需确认迁移语句的幂等与并发安全（SQLite 单写者特性下建议在容器构建后、UI 启动前串行执行）。

## 14. 验收标准

- [ ] `IUserContext` 为 Scoped，可由宿主按 scope 注入；CLI/Codex 单用户 `yswenli`。
- [ ] 同一 `userId` 下，用户 scope 与各会话 scope 解析到**同一** `ISessionManager` / `IWorkspaceManager` / `ConfigManager` 实例（当前会话 / 当前工作区 / 模型缓存跨会话保持）。
- [ ] 会话、工作区创建与查询均按当前用户；无硬编码 `"default"` / `null` / `""`。
- [ ] Provider 配置按用户分文件；模型列表 / 能力缓存按用户分桶且跨会话复用。
- [ ] 本地记忆按 `UserId` 隔离，全局行按用户可见。
- [ ] 存量数据迁移后 CLI/Codex 行为与改造前等价。
- [ ] framework 与 agent 均构建通过；框架相关单测通过；TUI/Codex 冒烟通过。
