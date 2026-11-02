# 设计：DI 统一到 LuBan.DI 的 DiContainer（2026-10-29）

状态：实施中（2026-10-30 按实施结果回填：`ServiceProviderUtil` 改为删除、`GetService` 未构建返回 null、新增 `Rebuild`/`ResetServices`；待全量验收）
范围：`luban-framework`（`LuBan.DI`、`LuBan.AIAgent`）+ `luban-agent`（`LubanAgentCore`、`LubanAgentCli`、`LubanAgentCodex`、`LubanAgentA2A`、`LubanAgent.Tests`）

---

## 1. 意图

产品与框架代码目前直接使用 `Microsoft.Extensions.DependencyInjection`（下称 MS.DI）的类型与调用：
`new ServiceCollection()`、`IServiceCollection`、`IServiceProvider`、`ServiceDescriptor.Xxx`、`GetRequiredService<T>()`、`CreateScope()`。

目标：把这些「容器操作」全部收敛到 `LuBan.DI`，由新类型 `DiContainer` 承担；`LuBan.DI` 缺的能力先补在 `LuBan.DI`，再在产品/框架代码里引用。

为什么是现在（用户口径）：「项目里直接使用 MS.DI 相关操作的地方」都要换，先把容器抽象收到框架里，再谈别的。

## 2. 目标 / 非目标

### 目标

1. `LuBan.DI` 新增 `DiContainer`：既能新建（空），也能包装已有 `IServiceCollection` 或已有 `IServiceProvider`。
2. `DiContainer` **同时实现 `IServiceCollection` 与 `IServiceProvider`**（另实现 `IServiceScopeFactory`、`IDisposable`、`IAsyncDisposable`），使只认这两个接口的第三方扩展方法可直接接收它。
3. `DiContainer.Instance`：全局唯一容器实例。
4. `DiContainer.CreateScope()`：返回可 `using` 的子容器，子容器自带 `GetService` / `GetRequiredService`。
5. 上述 6 个项目的 DI 操作全部改写为 `DiContainer`；`LuBan.AIAgent` 公开契约里的 `IServiceProvider` 一并换成 `DiContainer`。
6. 因契约变更而编不过的工程（`LuBan.Web.Core`、`WebApplication1`、`LuBan.XTestProject`、`smoke/plugin-system`、`LubanAgent.Plugin.NewProject`）做**编译必需的**改动，不做风格改造。

### 非目标

- 不把显式注册改成特性自动注册（`AutoInjectAllCustomerServices`/`InjectionAttribute` 保持现状，不做「扫描注册」替换 `AgentHostBuilder`）。
- 不改 `Microsoft.Extensions.Options` / `Configuration` / `Logging` 的用法（`IOptions<T>`、`IConfiguration` 不属于本次范围）。
- 不改 `LuBan.Web.Core`、`WebApplication1`、`LuBan.XTestProject` 既有 DI 风格，只在编译必需处改。
- 不改生命周期语义、注册顺序语义、`ValidateScopes`/`ValidateOnBuild` 配置（默认值保持暴力校验）。
- 不重构业务逻辑，不做顺手清理。
- **不修 `LuBan.Web.Core` 的双 provider 现状**（`StartupUtil` 在 ASP.NET `builder.Build()` 前自建 provider，与 `app.Services` 并存）。本次只做编译修复；该风险记录在案（§8），`WebApplication1` 不纳入运行验收。

### 反目标（即使编译通过也算失败）

- 出现两套并行容器（两个 `DiContainer` 各自持有一份 provider，或 `DiContainer.Instance` 与 ASP.NET `app.Services` 被当成两套全局解析入口），导致单例双份、状态分裂。
- 会话 scope 语义变化：会话级服务串用户/串会话，或 `ValidateScopes` 在启动/首轮对话时报错。
- 手动冒烟 `LubanAgentCli` / `LubanAgentCodex` 任一失败。

## 3. 约束

- 全部目标框架 `net10.0`。
- **`LuBan.DI` 新增对 `Microsoft.Extensions.DependencyInjection`（实现层，非仅 Abstractions）的直接包引用**，版本 `10.0.14`（与 `LuBan.Common`/`LuBan.Logging` 等框架项目一致），不再依赖 `LuBan.Common` 的传递引入。注意：agent 侧已按 AGENTS.md 的 F2 口径不再显式声明 `Microsoft.Extensions.*` 包，因此该直接引用不会制造 NU1605 降级；发包后需在 agent 侧还原验证一次。
- 框架包需发包并递增版本（`LuBan.DI`、`LuBan.AIAgent` 当前 csproj 为 `2026.10.29.1`；agent 侧现引用 `LuBan.AIAgent 2026.10.29.2`）。
- agent 侧新增 `LuBan.DI` 包引用（目前只有 `LuBan.Common` / `LuBan.Logging` / `LuBan.Orm` / `LuBan.AIAgent`）。
- `smoke/plugin-system/**` 有意保留 `ProjectReference`（不在 `LubanAgent.slnx` 内），本次仅做编译修复。
- 两个仓库当前都有未提交改动：本次改造直接在现有工作区叠加，不建分支、不提交、不动 WIP；验证阶段 agent 侧临时用 `ProjectReference`。

### 3.1 实施顺序（必须分批，否则中途无法编译验证）

1. **`LuBan.DI` 单点先行**：新增 `DiContainer`（含非泛型重载、scope 适配、私有 `_ownsProvider` 所有权标志、`virtual Dispose`）+ 单测 + 中英文 README；`ServiceProviderUtil` 整体删除、调用点改走 `DiContainer.Instance`（见 §4.7）。此步框架 sln 应能全绿。
2. **`LuBan.AIAgent` 契约与实现**：按 §5 改契约与内部注册/解析，再修**框架内消费方**（`LuBan.Web.Core`、`WebApplication1`、`LuBan.XTestProject`）；框架 sln 必须重新全绿。
3. **agent 侧切引用**：5 个项目临时改用 `ProjectReference` 指向本地框架项目（`TEMP-LOCAL-VERIFY` 形态）。
4. **agent 逐项目改**：`LubanAgentCore` → `LubanAgentA2A` → `LubanAgentCli` → `LubanAgentCodex` → `LubanAgent.Tests`；每步构建一次，避免一次暴露几百个错误。
5. **编译修复**：`LubanAgent.Plugin.NewProject`、`smoke/plugin-system`。
6. **验证**：两个测试项目 + Cli/Codex 手动冒烟。
7. **收尾**：用户人工发包 → agent 侧切回 `PackageReference` 并提版本 → 再跑一遍构建与冒烟。

## 4. `DiContainer` API 设计

命名空间 `LuBan.DI`，**一个对象一个文件**：`DiContainer.cs`（容器本体）、`DiContainerMode.cs`（内部枚举）、`DiContainerScope.cs`（`IServiceScope` 适配器）、`DiContainerExtensions.cs`（`AsDiContainer` 三重载）。

类型要求：

- **非 `sealed`**：`AgentHostServices` 需继承。
- `GetService(Type)` / `GetRequiredService(Type)` / `CreateScope()` / `Dispose()` / `DisposeAsync()` 均声明为 `virtual`（`AgentHostServices` 要 override 释放顺序：先释放 user scope 再释放 root；其余留出余地）。
- 生命周期所有权由**私有字段 `_ownsProvider`** 控制（不对外暴露）：包装外部 provider（`new DiContainer(provider)`）时 `false`，`Dispose` **不触碰**被包装对象；由自己 `Build()` / `Rebuild()` 出来的 `true`，`Dispose` 释放 provider 与本实例创建的全部 scope。

### 4.1 构造

```csharp
public DiContainer();                          // 空集合，未构建
public DiContainer(IServiceCollection services); // 包装已有集合，未构建；注册与 Build 都合法
public DiContainer(IServiceProvider provider);   // 已构建视图：只读解析，注册操作抛 InvalidOperationException
```

语义约定：

| 构造 | 可注册 | 可解析 | 可 Build |
|------|--------|--------|----------|
| `new DiContainer()` | 是 | 否（抛「未 Build」） | 是 |
| `new DiContainer(services)` | 是（写入同一集合） | 否 | 是 |
| `new DiContainer(provider)` | 否 | 是 | 否（抛异常） |

**「构造 vs 视图」的 Build 差异（易混，特此说明）**：`new DiContainer(services)` 表示宿主**接管**这个集合，因此允许 `Build`（宿主根实例）；`services.AsDiContainer()` 是**借用视图**，不允许 `Build`。二者区别在「谁拥有」。产品代码统一用前者（`AgentHostBuilder` 等），适配层内部才用后者。

`new DiContainer(provider)` 用于包装既有提供器：ASP.NET 的 `app.Services`、`HttpContext.RequestServices`、`IServiceScope.ServiceProvider`。**本视图不得持有/释放被包装的 provider**。

**未构建时的解析语义**：`GetRequiredService` / `GetRequiredService<T>` / `GetServices<T>` / `Provider` 在未 `Build` 时抛 `InvalidOperationException("容器尚未构建，请先调用 Build()。")`；`GetService` / `GetService<T>` 返回 `null`（`GetService` 契约本身允许返回 null，且与删除前的 `ServiceProviderUtil.GetService` 一致）。即：**只有 `GetRequiredService` 家族新增了抛错路径，`GetService` 行为不变**。

### 4.2 全局实例

```csharp
public static DiContainer Instance { get; }          // 全局唯一，首次访问创建空容器
public DiContainer UseAsGlobal();                    // 把当前实例设为全局（返回 this，可链式）
public bool IsGlobal { get; }
```

- 全局容器是可选设施：未调用 `UseAsGlobal()` 时 `Instance` 是空容器（`GetRequiredService` 抛「未构建」，`GetService` 返回 `null`）。
- **`Instance` 的存在意义就是承接原 `ServiceProviderUtil` 的职责**：它是全进程唯一的静态容器入口；`LuBan.Orm` / `LuBan.Logging` / `LuBan.Service` / `LuBan.ApprovalFlow` / `LuBan.Web.Core` / `WebApplication1` 的旧静态解析调用点已直接改写为 `DiContainer.Instance.GetService/GetRequiredService`。不是「为将来预留」的死设施。
- **agent 侧不自动 `UseAsGlobal()`**：现状 agent 从不设置全局容器，经全局解析的路径本就不存在；自动指向 root 会让 `ValidateScopes=true` 下的 scoped 解析新增一条抛错路径。需要时由调用方显式启用（A2A 内层 WebApplication 不覆盖全局）。
- 原 `ServiceProviderUtil` 已整体删除，不存在转发层（见 §4.7）。

### 4.3 注册

覆盖现有调用点需要的全部形态（产品代码中出现过的形态必须一一对应）：

```csharp
// 单例
DiContainer AddSingleton<TService>() where TService : class;
DiContainer AddSingleton<TService, TImpl>() where TService : class where TImpl : class, TService;
DiContainer AddSingleton<TService>(TService instance) where TService : class;
DiContainer AddSingleton<TService>(Func<DiContainer, TService> factory) where TService : class;
DiContainer AddSingleton<TService, TImpl>(Func<DiContainer, TImpl> factory) where TImpl : class, TService;
// 作用域（同上五种）
DiContainer AddScoped<...>(...);
// 瞬态（同上五种）
DiContainer AddTransient<...>(...);

// 非泛型（运行时类型注册，外部插件加载必需）
DiContainer AddSingleton(Type serviceType, Type implementationType);
DiContainer AddSingleton(Type serviceType, object instance);
DiContainer AddSingleton(Type serviceType, Func<DiContainer, object> factory);
DiContainer AddScoped(Type serviceType, Type implementationType);
DiContainer AddScoped(Type serviceType, Func<DiContainer, object> factory);
DiContainer AddTransient(Type serviceType, Type implementationType);
DiContainer AddTransient(Type serviceType, Func<DiContainer, object> factory);

// 覆盖/清理
DiContainer TryAddSingleton<TService>();                                  // 单元测试/产品里都有单泛型形态
DiContainer TryAddSingleton<TService, TImpl>();
DiContainer TryAddScoped<TService>();
DiContainer TryAddScoped<TService, TImpl>();
DiContainer TryAddTransient<TService>();
DiContainer TryAddTransient<TService, TImpl>();
DiContainer ReplaceSingleton<TService>(TService instance);               // A2A / Web.Core 用实例替换
DiContainer ReplaceSingleton<TService, TImpl>();
DiContainer ReplaceSingleton<TService>(Func<DiContainer, TService> factory);
DiContainer ReplaceScoped<TService>(TService instance);
DiContainer ReplaceScoped<TService, TImpl>();
DiContainer ReplaceScoped<TService>(Func<DiContainer, TService> factory);
DiContainer RemoveAll<TService>();
```

- **`ReplaceXxx` 语义必须等同 `services.Replace(...)`**：移除该服务类型的**全部**既有注册后追加一条（`ServiceCollectionDescriptorExtensions.Replace` 的行为），不是「移除最后一条」。现有 10 处 `services.Replace(ServiceDescriptor.Xxx)` 依赖此语义（`AIAgentExtension:59`、`StartupRunner:58`、`App.axaml.cs:91/97/104/106`、`LubanAgentA2AExtensions:53/54/56/70`）。
- **单泛型 `TryAdd*<TService>()` 是硬需求**：`TryAddScoped<IAgentUserResolver>`、`TryAddScoped<IAgentWorkspaceResolver>`、`TryAddSingleton<A2ARuntimeState>` 等直接使用单泛型重载。

- **非泛型重载是硬需求**：`LuBan.AIAgent/LuBanAgentExtensions.cs:262` 用 `services.AddScoped(typeof(ILuBanToolPlugin), type)` 按运行时类型注册外部插件，泛型 API 覆盖不到。
- 工厂参数类型是 `DiContainer`（不是 `IServiceProvider`）：`sp => new Foo(sp.GetRequiredService<Bar>())` → `di => new Foo(di.GetRequiredService<Bar>())`。
- 注册方法返回 `DiContainer`，支持链式。
- 兜底：`DiContainer` 实现 `IServiceCollection`，所以 MS/第三方扩展（`AddLogging`、`AddOptions`、`AddHttpClient` 等）仍可直接作用于它；但**产品代码不得再出现 `ServiceDescriptor.`**，凡本清单覆盖的形态一律用上面的实例方法。

### 4.4 解析

```csharp
object? GetService(Type serviceType);
T? GetService<T>();
object GetRequiredService(Type serviceType);
T GetRequiredService<T>() where T : notnull;
IEnumerable<T> GetServices<T>();
IServiceProvider Provider { get; }   // 供极少数第三方互操作，产品代码禁止使用
```

### 4.5 构建与作用域

```csharp
DiContainer Build(bool validateScopes = true, bool validateOnBuild = true);
DiContainer Rebuild(bool validateScopes = false, bool validateOnBuild = false, TimeSpan? deferOldDispose = null);   // 每次重建并释放旧 provider
DiContainer ResetServices(IServiceCollection services);   // 接管外部集合（如 ASP.NET builder.Services），不动已构建的 provider
bool IsBuilt { get; }
DiContainer CreateScope();                       // 子容器；Dispose/DisposeAsync 释放 scope
bool IsScope { get; }
DiContainer? Parent { get; }

// 证据：IServiceScopeFactory 的接口方法必须显式实现（返回类型不同，不能与上面的 CreateScope() 共存为同一成员）
IServiceScope IServiceScopeFactory.CreateScope();   // 内部适配返回 IServiceScope 包装
```

- `Build(...)` 在原实例上完成构建并返回 `this`（`ServiceProviderOptions` 语义等价：默认 `ValidateScopes = true`、`ValidateOnBuild = true`，与 `AgentHostBuilder` 现状一致）。
- **不提供 `Build(IServiceProvider)` 重载**（原设计列过，实测全仓库零调用点，已取消）：需要挂接已有 provider 时用 `new DiContainer(provider)` / `provider.AsDiContainer()`，即视图而非构建。
- **`Rebuild(...)` 是「每次重建」语义**（默认不校验、构建后释放旧 provider）：`LuBan.Web.Core` 的 `StartupUtil` 用它承接原 `BuildProvider()` 的「每次重建、不校验」行为，不能用幂等的 `Build()` 替代。
- **注册与构建互斥**：已构建的根容器上再调用任何注册方法（含 `TryAdd*` / `Replace*` / `RemoveAll`）抛 `InvalidOperationException`；需重新注册时先 `ResetServices(新集合)` 再 `Rebuild()`。
- **Build 归属规则（防双 provider）**：`Build` 只允许在「宿主自己 new 出来并持有」的那个根实例上调用，且幂等——重复 `Build()` 直接返回 `this`，不重建。`AsDiContainer()` 产生的两类视图**禁止** `Build`（抛 `InvalidOperationException`）：provider 视图只能解析、集合视图只能注册。这样「两套并行容器」这个反目标在实现层面就被堵死。
- `CreateScope()` 必须先 `Build`（或本身是构建好的视图），否则抛异常；每次返回新实例，`Dispose` 幂等。
- **`CreateScope()` 语义必须与 MS.DI 一致：永远从根派生，不继承调用方 scope 的身份状态。** `LubanAgentCore/Configuration/UserScopedServiceExtensions.cs` 的 `CreateUserScope(userId)` 正是依赖这一点做身份回填，改动此语义会导致会话串用户。
- 作用域容器只解析、不可注册；其解析走 scope 的 provider；其私有 `_ownsProvider = true`（scope 自身要释放），但**不得释放根**。

### 4.6 与 `IServiceCollection` / `IServiceProvider` 的共存规则（关键决策）

`DiContainer` 实现 `IServiceCollection`（成员全部转发内部集合）与 `IServiceProvider`（转发内部/作用域 provider），因此：

- `services.AddLuBanAgentDefaults(configuration)`、`services.AddLogging(...)`、`services.AddLubanAgentA2A(configuration)` 这类**只认 `IServiceCollection` 的扩展方法调用点文本不变**（`diContainer` 直接作集合传入）。
- 同一处若 `DiContainer` 自己有同名实例方法（如 `AddSingleton`），**实例方法优先**，不会走 MS.DI 扩展方法。
- 在「已构建视图」（`new DiContainer(provider)`）上调用 `IServiceCollection.Add` 抛 `InvalidOperationException`。
- **`AsDiContainer` 必须提供三重载**，否则 `DiContainer`（同时是集合与提供器）传入时会产生二义性：`AsDiContainer(this DiContainer)` 返回自身、`AsDiContainer(this IServiceCollection)`、`AsDiContainer(this IServiceProvider)`。C# 精确类型匹配优先于接口转换，重载能正确解析。
- 视图（外部 provider / 外部集合包装）**不可 `Build`**，见 §4.5 归属规则。

### 4.7 `ServiceProviderUtil` 的处理（2026-10-30 变更：整体删除，不再折叠）

用户口径：包发布后对外只应存在一个全局入口，折叠成的转发层会让外部读者同时看到 `ServiceProviderUtil` 与 `DiContainer.Instance` 两个入口。因此原「保留 API + 内部转发」方案作废，改为**删除**。

删除与迁移的逐项映射（`LuBan.DI/ServiceProviderUtil.cs` 已移除）：

- `GetService` / `GetService<T>` / `GetRequiredService` / `GetRequiredService<T>` → `DiContainer.Instance` 上同名成员。
- `this Type` 扩展形态（`type.GetRequiredService()`，`LuBan.ApprovalFlow/Core/Handlers/BaseNodeHandler.cs` 1 处）→ `DiContainer.Instance.GetRequiredService(targetType)`。
- `services.InitServiceProvider()`（`LuBan.Web.Core/Utils/StartupUtil.cs`）→ `DiContainer.Instance.ResetServices(services)`。
- `services.BuildProvider()`（`StartupUtil.cs`）→ `DiContainer.Instance.Rebuild()`，保留原「每次重建、不校验」语义（**不是**幂等的 `Build()`）。
- `AutoInjectAllCustomerServices(IServiceCollection)` 的包装方法删除；实现本就在 `ServiceDescriptorUtil`，改为 `this IServiceCollection` 扩展，调用点文本不变。
- `Services` 属性、`GetExceptionScope()`、`GetRequiredServiceForOnce<T>()` 实测零调用点，直接删除，不迁成新的死 API。

影响面：`LuBan.Orm`、`LuBan.Logging`、`LuBan.Service`、`LuBan.ApprovalFlow`、`LuBan.Web.Core`、`WebApplication1` 的旧静态调用点全部改写；`luban-agent` 侧原本零引用。

**破坏性变更**：仓库外直接调用 `ServiceProviderUtil` 的代码编译失败，必须在发版说明（`LuBan.DI.csproj` 的 `PackageReleaseNotes`）写明迁移方式。

## 5. 契约变更（`LuBan.AIAgent`）

统一规则：**凡是「容器句柄」的类型，`IServiceProvider` → `DiContainer`**；凡是「配置/选项/日志」的类型不动；**第三方/官方（MS.DI、ASP.NET、A2A SDK）只认 `IServiceCollection` 的扩展方法签名不动**，我们自己的 `Add*` 一律改成 `this DiContainer`（见第 13 条）。

| # | 位置 | 现状 | 之后 |
|---|------|------|------|
| 1 | `ILuBanToolPlugin.GetTools` | `IServiceProvider sp` | `DiContainer sp` |
| 2 | `ToolPluginRegistry.GetAllFunctions` | `IServiceProvider sp` | `DiContainer` |
| 3 | `IPluginHost.Services` / `AgentPluginHost.Services` | `IServiceProvider` | `DiContainer` |
| 4 | `IAgentHost.Services` / `AgentHost.Services` | `IServiceProvider` | `DiContainer` |
| 5 | `IPluginSessionContributor.Contribute` | `IServiceProvider sessionServices` | `DiContainer` |
| 6 | `IAgentSessionExtension.ApplyAsync` | `IServiceProvider sessionServices` | `DiContainer` |
| 7 | `IAgentUserResolver.ResolveUserId` / `DefaultSeams` | `IServiceProvider` | `DiContainer` |
| 8 | `ISkill.ServiceProvider`、`IRule.Services` | `IServiceProvider?` | `DiContainer?` |
| 9 | `IConnector.Services` / `CreateClient(Func<IServiceProvider,...>)` | `IServiceProvider` | `DiContainer` |
| 10 | `ConnectorInstanceHost(IServiceProvider root)` | `IServiceProvider` | `DiContainer` |
| 11 | `SessionRuntimeManager(IServiceProvider root)`、`SessionRuntime.Services`、`SessionRuntime` 内部 `IServiceScope` | `IServiceProvider` / `IServiceScope` | `DiContainer` / `DiContainer`（scope） |
| 12 | `LuBanAgentFactory`、`LlmTaskPlanner`、`PluginManager`、`OrchestrationToolGroup`、`ContextInjectBuilder`、`PerUserWorkspaceResolver`、`SessionChatHistoryProvider` 的构造参数/字段 | `IServiceProvider` | `DiContainer` |
| 13 | `AddLuBanAgent` / `AddLuBanAgentDefaults` / `AddLuBanAgentHost` / `AddLuBanAgentOpenAI` / `AddLubanA2A` / `AddConnector<T>` / `AddBuiltinConnectors` / `AddAliyunDevOpsConnector` | `this IServiceCollection` | `this DiContainer` |

第 13 条说明：改成 `this DiContainer` 后，agent 侧 `services.AddLuBanAgentDefaults(configuration)` 这类调用**文本不变**（`services` 已是 `DiContainer`），符合「这些不变」；只有外部拿着 `IServiceCollection` 的调用方（`LuBan.Web.Core`、`WebApplication1`、`LuBan.XTestProject`、`smoke`）需要加一次 `.AsDiContainer()`，属于已接受的编译修复。

仍然**只认 `IServiceCollection`** 的只有第三方/官方扩展：`AddLogging(...)`、ASP.NET `builder.Services`、`AddA2AAgent(...)`。这些调用点允许保留 `IServiceCollection` 与对应 `using`。

需要新增的适配扩展（`LuBan.DI`）：

```csharp
public static DiContainer AsDiContainer(this DiContainer container);        // 返回自身（必需，避免二义性）
public static DiContainer AsDiContainer(this IServiceProvider provider);    // 已构建视图（私有 _ownsProvider = false）
public static DiContainer AsDiContainer(this IServiceCollection services);  // 未构建视图（不可 Build）
```

## 6. 改写规则（逐类映射）

| 现状 | 之后 |
|------|------|
| `var services = new ServiceCollection();` | `var di = new DiContainer();` |
| `services.AddSingleton<Foo>();` | `di.AddSingleton<Foo>();` |
| `services.AddSingleton<I, T>();` | `di.AddSingleton<I, T>();` |
| `services.AddSingleton<IModel>(modelManager)`（实例） | `di.AddSingleton<IModel>(modelManager)` |
| `services.AddSingleton<IFoo>(sp => new Foo(sp.GetRequiredService<Bar>()))` | `di.AddSingleton<IFoo>(d => new Foo(d.GetRequiredService<Bar>()))` |
| 工厂 lambda 体内把 `sp` 当 `IServiceProvider` 用（`sp.GetService(typeof(X))`、传给要求 `IServiceProvider` 的构造/方法） | 保留但仍可用（`DiContainer` 实现 `IServiceProvider`）；若目标签名已改成 `DiContainer` 则直接传 |
| `services.AddScoped(sp => sp.GetRequiredService<X>().Y)` | `di.AddScoped(d => d.GetRequiredService<X>().Y)` |
| `services.AddScoped(typeof(ILuBanToolPlugin), type)` | `di.AddScoped(typeof(ILuBanToolPlugin), type)`（非泛型重载） |
| `services.TryAddSingleton<IPluginUiDispatcher, ImmediatePluginUiDispatcher>()` | `di.TryAddSingleton<...>()` |
| `services.Replace(ServiceDescriptor.Singleton<IFoo>(sp => ...))` | `di.ReplaceSingleton<IFoo>(d => ...)` |
| `services.RemoveAll<T>()` | `di.RemoveAll<T>()` |
| `services.BuildServiceProvider(new ServiceProviderOptions{ ValidateScopes = true, ValidateOnBuild = true })` | `di.Build()` |
| `services.BuildServiceProvider()` | `di.Build(validateScopes: false, validateOnBuild: false)`（仅测试/冒烟等原本就不校验的场景） |
| `root.CreateScope()` + `scope.ServiceProvider` | `var scope = root.CreateScope();` + `scope.GetRequiredService<T>()` |
| `GetRequiredService<IServiceScopeFactory>().CreateScope()` | `di.CreateScope()`（`DiContainer` 自身实现 `IServiceScopeFactory`，仅作互操作兜底，产品代码不再调用） |
| `IServiceScope` 字段/参数（`SessionRuntime`、`AgentHostServices`、`KnowledgeTaskService` 等） | `DiContainer`（scope 视图） |
| `IServiceProvider services` 参数 / 字段 / 属性 | `DiContainer services` |
| `IServiceScopeFactory` 构造参数（`KnowledgeTaskService`、`InternalSubAgentHandlerAdapter`、`A2ARuntimeLifecycle`、`A2AServerHost`） | `DiContainer`，内部 `CreateScope()` |
| 测试替身 `class XxxProvider : IServiceProvider`（`AttachmentBareImageMentionUnitTest`、`McpConnectorManagementUnitTest`） | `new DiContainer(fakeProvider)` 包装后传入；**不再新增 `IServiceProvider` 实现类** |
| `httpContext.RequestServices.GetRequiredService<T>()` | `httpContext.RequestServices.AsDiContainer().GetRequiredService<T>()` |
| `builder.Services`（ASP.NET） | 保持 `IServiceCollection`；需要解析时 `builder.Services.AsDiContainer()`，或 `app.Services.AsDiContainer()` |
| `global using Microsoft.Extensions.DependencyInjection;` / `.Extensions;` | 删除；改 `global using LuBan.DI;` |
| `AgentHostServices : IServiceProvider, IDisposable, IAsyncDisposable`（root + 常驻 user scope 门面） | `AgentHostServices : DiContainer`，构造 `(DiContainer root, DiContainer userScope)`，`Root` / `UserServices` 改 `DiContainer` |

## 7. 逐项目清单（工作量估算）

调用点统计（实测正则：`GetRequiredService|GetService<|GetService(|GetServices<|BuildServiceProvider|new ServiceCollection|Add(Singleton|Scoped|Transient)|TryAdd|Replace(|ServiceDescriptor.|IServiceScopeFactory|CreateScope`）：

| 项目 | 文件数 | 调用点 | 重点文件 |
|------|--------|--------|----------|
| `LuBan.DI`（框架） | 新增 3 + 改 1 | — | 新增 `DiContainer`（含 scope 适配）；删除 `ServiceProviderUtil`；README 中英文 |
| `LuBan.AIAgent`（框架） | 73 | 289 | `LuBanAgentExtensions`(89)、`LuBanAgentHostExtensions`(21)、`LuBanAgentDefaultsExtensions`(17)、`LuBanAgentFactory`(11)、`OrchestrationToolGroup`(11)、`ConnectorServiceCollectionExtensions`(10)、`AgentHost`(9) |
| `LubanAgentCore` | 19 | 178 | `AgentHostBuilder`(106)、`AliyunDevOpsConnectorServiceCollectionExtensions`(16)、`AgentPluginHost`(9)、`UserScopedServiceExtensions`(6) |
| `LubanAgentCli` | 15 | 83 | `CommandViewModel`(30)、`StartupWorkspaceResolver`(8)、`CommandBase`(7)、`StartupRunner`(7) |
| `LubanAgentCodex` | 19 | 87 | `MainWindowViewModel`(24)、`App.axaml.cs`(21)、`MainWindow.axaml.cs`(14) |
| `LubanAgentA2A` | 5 | 45 | `LubanAgentA2AExtensions`(21)、`A2AServerHost`(6)、`InternalSubAgentHandlerAdapter`(3) |
| `LubanAgent.Tests` | 8 | 16 | `SettingsViewModelUnitTest`(8)、`PluginHostUnitTest`(2)、两个测试替身(2) |

编译修复（不做风格改造，但**确定要改**）：

- `LuBan.Web.Core`：`AIAgentExtension`（3 处 `Add*` 调用）、`WebAgentUserResolver`。
- `WebApplication1`：`AgentController`（`RequestServices` 解析）。
- `LuBan.XTestProject`：~20 个 AIAgent 相关测试（`AgentHostRegistrationUnitTest`、`AgentHostResetUnitTest`、`ScopedPluginChainUnitTest`、`PluginManagerLifecycleUnitTest`、`SessionRuntimeManagerUnitTest` 等）。
- `smoke/plugin-system`：`TestPluginHost`（`IPluginHost.Services` 类型）、`Program`（`new ServiceCollection()`）。
- `LubanAgent.Plugin.NewProject`：6 文件 / 18 处，非「如有」——`HostAssistantBridge.cs:186` 还按 `typeof(IServiceScopeFactory)` 动态解析，靠 MS.DI 内置注册兜底（`DiContainer` 包装 MS provider 时这些内置注册天然存在，无需额外处理）。

## 8. 风险与对策

| 风险 | 对策 |
|------|------|
| 视图误 `Build` 造成两份 provider（正好命中反目标） | §4.5 归属规则：Build 幂等 + 仅宿主根实例可 Build，视图 Build 抛异常；单测断言 |
| `DiContainer` 实现 `IServiceCollection` 后 `AddSingleton` 调用产生歧义/意外走扩展方法 | 单元测试锁定「实例方法优先」；对 `AddSingleton<TService>(TService instance)` 与 MS 扩展同名场景断言注册结果 |
| `AsDiContainer` 三重载解析错（`DiContainer` 同时是集合与提供器） | `AsDiContainer(this DiContainer)` 返回自身的重载必须有；单测覆盖三种入参 |
| 双全局容器 | `ServiceProviderUtil` 已删除，全进程只剩 `DiContainer._current` 一处全局状态；单测断言 `ResetServices` + `Rebuild` 后 `Instance` 指向同一容器 |
| 会话 scope 串号 | `CreateScope` 单元测试 + `ValidateScopes=true` 冒烟；Cli/Codex 冒烟路径覆盖多会话与切换工作区 |
| `CreateScope()` 返回类型与 `IServiceScopeFactory` 冲突 | 显式接口实现 + 内部 `IServiceScope` 适配器（§4.5） |
| `AgentHostServices` 继承 `DiContainer` 后 Dispose 语义（先 scope 后 root） | `Dispose`/`DisposeAsync` 声明为 `virtual` 并 `override`；测试断言释放顺序 |
| 包装外部 provider（`HttpContext.RequestServices`）被误 Dispose | 包装视图私有 `_ownsProvider = false`，`Dispose` 不触碰 |
| 删除 `ServiceProviderUtil` 属破坏性 API 变更（外部代码编译失败） | `PackageReleaseNotes` 写明迁移方式（改为 `DiContainer.Instance`）；保持一致的是 `GetService` 返回 null、`GetRequiredService` 抛异常的分工 |
| 框架发包/版本联动出错 | 先发 `LuBan.DI`，再发 `LuBan.AIAgent`，最后 agent 侧统一提版本；两仓库分别构建验证 |
| 用户 WIP 被覆盖 | 直接在现工作区叠加改动，不 stash/不提交；改动范围按文件精确控制，任务完成后再由用户统一整理 |
| `LuBan.Web.Core` 的 `StartupUtil` 自建 provider 与 ASP.NET provider 并存（既有问题，本次不修） | 明确不纳入本次验收；风险记录，后续单独处理 |
| 外部插件二进制兼容：`ILuBanToolPlugin.GetTools` 签名变更后，仓库外用旧版框架编译的插件会在加载时失配 | **已确认不保兼容**：插件随框架重编译重发；发布说明里注明旧插件需重编译 |

## 9. 验收

1. `dotnet build luban-framework/LuBan.FrameWork.sln` 通过。
2. `dotnet build luban-agent/LubanAgent.slnx` 通过。
3. `dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj` 通过（集成测试失败先排查环境）。
4. `dotnet test luban-agent/LubanAgent.Tests/LubanAgent.Tests.csproj` 通过。
5. `smoke/plugin-system` 能单独构建（不在 slnx 内）。
6. **手动冒烟 `LubanAgentCli`**：启动 → 自动建/进工作区 → 首轮对话（含工具调用）→ 切换会话 → `/skill`、`/mcp`、`/model` 等命令 → 退出无异常。
7. **手动冒烟 `LubanAgentCodex`**：启动 → 主窗口会话 → 设置窗口（技能/规则/MCP/知识库/云效页签）→ 插件管理 → 切换工作区 → 退出无异常。
8. 静态验收：改造主体（`LuBan.AIAgent` + `LubanAgentCore` + `LubanAgentCli` + `LubanAgentCodex` + `LubanAgentA2A` + `LubanAgent.Tests`，共 6 个）的**容器操作**清零——源码中不再出现 `new ServiceCollection()`、`BuildServiceProvider`、`ServiceDescriptor.`，也不再出现作为参数/字段的 `IServiceProvider`、`IServiceScope`、`IServiceScopeFactory`。`LuBan.DI` 作为本次新增的容器实现，不在此列。
   允许保留：
   - `IServiceCollection` 仅用于 MS/第三方扩展的接收者边界（`AddLogging`、ASP.NET `builder.Services`、`AddA2AAgent`），以及这些边界所需的 `using Microsoft.Extensions.DependencyInjection;`；
   - **适配层除外**：`DiContainer.cs` / `DiContainerScope.cs` / `AsDiContainer` 扩展中必然出现 `IServiceProvider`、`IServiceScope`、`IServiceScopeFactory`（`ServiceProviderUtil.cs` 已删除，不再属于例外）。
   `LuBan.AIAgent` 公开 API 中 `IServiceProvider` 仅出现在 `AsDiContainer()` 适配与第三方互操作处。
9. `LuBan.XTestProject` 新增 `DiContainer` 单测（`LuBan.UnitTestProject.csproj`），至少覆盖：实例方法优先于扩展方法、三重载 `AsDiContainer`、Build 幂等与视图禁 Build、`CreateScope` 从根派生、`_ownsProvider` 释放语义、未构建时 `GetService` 返回 null 而 `GetRequiredService` 抛异常、`ResetServices` + `Rebuild` 后 `Instance` 刷新、`IServiceScopeFactory.CreateScope()` 适配、`Replace*` / `TryAdd*` 语义。
10. `LuBan.DI` 的 `README.md` / `README.en.md` 同步更新（AGENTS.md 明文约定），全局入口只描述 `DiContainer`（含 `Instance`），不再出现 `ServiceProviderUtil`。

## 10. 我的判断（可推翻，请在评审时确认）

1. 公开 `Add*` 扩展方法（`LuBan.AIAgent` 的 8 个 + `LubanAgentCore` 的 `AddAliyunDevOpsConnector` + `LubanAgentA2A` 的 `AddLubanAgentA2A`）签名改为 `this DiContainer`；agent 侧调用点文本不变，外部 `IServiceCollection` 调用方加 `.AsDiContainer()`。
2. `ServiceProviderUtil` 整体删除（2026-10-30 用户决定），静态入口只剩 `DiContainer.Instance`；这是破坏性变更，需在发版说明中写迁移方式。
3. `DiContainer` 非 `sealed`，`AgentHostServices` 继承它。
4. `DiContainer` 默认 `Build()` 开启 `ValidateScopes` + `ValidateOnBuild`；仅在原本未校验的测试场景显式关闭。
5. `DiContainer.Instance` 默认空容器，agent 侧不自动 `UseAsGlobal()`（避免新增 `ValidateScopes` 抛错路径）。
6. `LuBan.DI` **已确定新增** `Microsoft.Extensions.DependencyInjection`（实现层）直接包引用，避免依赖 `LuBan.Common` 的传递引入（用户已确认）。
7. 验收口径按 §9.8：容器操作清零（适配层除外）；`IServiceCollection` 仅作为 MS/第三方扩展的接收者边界保留。
8. 外部插件兼容不做（已确认），旧插件需随新框架重编译。
9. **`DiContainer.Instance` 不是死代码，它的定位是替代 `ServiceProviderUtil`**（用户明确）：删除 `ServiceProviderUtil` 后由它承接全部框架内部静态解析调用点；agent 侧仍不自动 `UseAsGlobal()`。

## 11. 已确认的决策（用户口径）

1. **外部插件二进制兼容不保**：`ILuBanToolPlugin` 等契约直接改类型，旧插件随框架重编译重发。
2. **发布与验证链路**：开发验证阶段 agent 侧先临时用 `ProjectReference` 指向框架项目（沿用仓库历史 `TEMP-LOCAL-VERIFY` 做法）；验证通过后由用户**人工发包**，agent 侧再切回 `PackageReference` 并提升版本号。版本号沿用日期制递增。
3. **未提交改动**：两个仓库现有 WIP 先不动，本次改造在其之上进行；提交/整理等任务完成后再处理。