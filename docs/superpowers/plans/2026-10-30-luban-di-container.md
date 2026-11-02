# DI 统一到 LuBan.DI.DiContainer 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: 用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans` 逐任务执行。任务用 `- [ ]` 复选框跟踪。

**Goal：** 新增 `LuBan.DI.DiContainer`，并把 `luban-framework` / `luban-agent` 两仓库里**直接使用 MS.DI 容器操作**的代码全部换成 `DiContainer`；`LuBan.AIAgent` 契约里的 `IServiceProvider` / `IServiceScope` / `IServiceScopeFactory` 一律换成 `DiContainer`。

**Architecture：** `DiContainer` 同时实现 `IServiceCollection`（注册期转发内部集合）、`IServiceProvider`（解析期转发内部 provider）、`IServiceScopeFactory`（互操作兜底）、`IDisposable` / `IAsyncDisposable`。`CreateScope()` 返回**同类型子容器**（可 `using`、自带 `GetRequiredService`）。`ServiceProviderUtil` 保留全部公开 API，内部折叠到 `DiContainer.Instance`，保证全进程只有一份全局 provider。框架与产品代码的容器句柄一律是 `DiContainer`。

**Tech Stack：** .NET 10 / C# 13 / MSTest 4.5 / Microsoft.Extensions.DependencyInjection 10.0.14

**设计文档：** `luban-agent/docs/superpowers/specs/2026-10-29-luban-di-container-design.md`

**本计划位置：** `luban-agent/docs/superpowers/plans/2026-10-30-luban-di-container.md`

---

## 0. 执行前必读

1. **本计划不执行 git 提交、不建分支、不 stash。** 两个仓库都已有用户 WIP，改动直接叠加在现工作区，任务完成由用户统一整理。每个任务末尾是「检查点」，不是 `git commit`。若执行者认为必须提交，先问用户。
2. **根目录 `luban/` 不是 git 仓库。** 任何 git 命令都要 `git -C D:\WorkBench\Walle\luban\luban-framework ...` 或 `git -C D:\WorkBench\Walle\luban\luban-agent ...`。
3. **agent 侧现在已经是 `ProjectReference` 形态**（`LubanAgentCore` / `LubanAgentA2A` / `LubanAgent.Plugin.NewProject` 直连 `..\..\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj`，csproj 里有 `TEMP-LOCAL-VERIFY` 注释）。所以**不需要新增跨仓库引用**就能编译，改完框架源码对 agent 立即生效。
4. **对设计稿的三处修订（以本计划为准，不按设计稿）**：
   - 设计稿 §4.1 写「未构建时 `GetService` 抛 `InvalidOperationException`」→ **改为 `GetService` 返回 `null`，只有 `GetRequiredService` 抛 `InvalidOperationException("容器尚未构建…")`**。理由：`ServiceProviderUtil.GetService<T>()` 现状返回 `default`，`LuBan.Orm` / `LuBan.Logging` / `LuBan.Service` 等 15+ 处依赖该语义；改抛异常属于与本次无关的破窗。`IServiceProvider.GetService` 的 .NET 契约本身也允许返回 null。
   - 设计稿 §4.7 写 `ServiceProviderUtil.BuildProvider()` → `Container.Build()` → **改为 `Container.Rebuild()`**。`Build()` 是幂等的（防双 provider），而 `BuildProvider` 原语义是「每次重建并释放旧 provider」，两者不能混用。
   - 设计稿 §4.5 的 `Build(IServiceProvider provider)` → **删除，不实现**。ASP.NET 场景统一用 `app.Services.AsDiContainer()`（已构建视图），提供「挂接外部 provider 的 Build」只会多一条双 provider 的入口。
5. **三个术语（全文一致）**：
   - **宿主根**：宿主自己 `new DiContainer()` 得到的实例，可注册、可 `Build()`、`Build` 幂等。
   - **视图**：`services.AsDiContainer()`（只注册、不可 Build）或 `provider.AsDiContainer()` / `new DiContainer(provider)`（只解析、不可 Build、不持有被包装对象）。
   - **scope**：`DiContainer.CreateScope()` 返回的子容器，只解析、`Dispose` 时释放 scope，**永远从根派生**。
6. **命令统一用 PowerShell（Windows）**，构建加 `-v m` 降噪。测试项目路径注意：框架侧挂进 sln 的是 `LuBan.XTestProject/LuBan.UnitTestProject.csproj`，不是同目录的 `LuBan.XTestProject.csproj`。
7. **任务顺序不可乱**：`LuBan.DI` → `LuBan.AIAgent` 契约 → 框架消费方 → agent 逐项目。中间任何一步都必须能 `dotnet build` 通过再进下一步，否则错误量会一次爆到几百条。
8. **静态扫描命令（多任务复用）**：

```powershell
# 容器操作（目标：6 个改造项目清零）
$pat = 'new ServiceCollection|BuildServiceProvider|ServiceDescriptor\.'
$roots = @(
  'D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent',
  'D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore',
  'D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli',
  'D:\WorkBench\Walle\luban\luban-agent\LubanAgentCodex',
  'D:\WorkBench\Walle\luban\luban-agent\LubanAgentA2A',
  'D:\WorkBench\Walle\luban\luban-agent\LubanAgent.Tests'
)
Get-ChildItem -Recurse -Filter *.cs $roots | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern $pat | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
```

```powershell
# 容器句柄类型（目标：只剩适配层与 IServiceCollection 边界）
$tpat = '(?<![A-Za-z])(IServiceProvider|IServiceScopeFactory|IServiceScope)(?![A-Za-z])'
Get-ChildItem -Recurse -Filter *.cs $roots | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern $tpat | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
```

9. **每完成一个任务，把该任务的 `- [ ]` 改成 `- [x]`，并在「验证记录」行写实际命令与结果**（通过/失败原因）。这是后续任务判断基线是否可信的唯一依据。
10. **遇到「本计划未覆盖的调用形态」时的处置**：先在 `LuBan.DI/DiContainer.cs` 补齐对应重载 + 单测（保持实例方法优先、返回 `this`），再改调用点；**不允许**在业务项目里退回 `ServiceProviderUtil` 或裸 MS.DI 绕过。

---

## 1. 任务总览

| # | 任务 | 主要文件 | 完成判据 |
|---|------|----------|----------|
| 1 | `LuBan.DI` 前置：包引用 | `LuBan.DI/LuBan.DI.csproj` | 项目单独编译通过 |
| 2 | `DiContainer` 注册 + 解析 | `LuBan.DI/DiContainer.cs` | 编译通过 |
| 3 | `DiContainer` 构建 / 作用域 / 显式接口适配 | `DiContainer.cs`（合并）、`DiContainerMode.cs`、`DiContainerScope.cs` | 编译通过 |
| 4 | `AsDiContainer` 三重载 + `ServiceProviderUtil` 折叠 | `DiContainerExtensions.cs`、`ServiceProviderUtil.cs` | 编译通过 |
| 5 | `LuBan.DI` 单元测试 | `LuBan.XTestProject/DiContainerUnitTest.cs` | 新测试全绿 |
| 6 | `LuBan.DI` README 中英同步 | `LuBan.DI/README.md`、`README.en.md` | 文档含 DiContainer 章节 |
| 7 | `LuBan.AIAgent` 契约换型 | 12 类接口 + 19 个 `GetTools` 实现 | 框架项目编译（消费方暂红） |
| 8 | `LuBan.AIAgent` 注册/解析改写 + `Add*` 签名 | `LuBanAgent*.cs`、`Hosting/*`、`Connectors/*` | 框架项目编译 |
| 9 | 框架内消费方编译修复 | `LuBan.Web.Core`、`WebApplication1`、`LuBan.XTestProject` | `LuBan.FrameWork.sln` 全绿 |
| 10 | 框架侧验收 + 静态扫描 | — | 构建 + `LuBan.UnitTestProject` |
| 11 | agent 侧引用确认 | 3 个 csproj | 已是 `ProjectReference`，无改动 |
| 12 | `LubanAgentCore` 改造 | `AgentHostBuilder`、`AgentHostServices`、`UserScopedServiceExtensions` 等 19 文件 | `LubanAgentCore` 编译 |
| 13 | `LubanAgentA2A` 改造 | 5 文件 | 编译 |
| 14 | `LubanAgentCli` 改造 | 15 文件 | 编译 |
| 15 | `LubanAgentCodex` 改造 | 19 文件 | 编译 |
| 16 | `LubanAgent.Tests` 改造 | 8 文件 | 测试全绿 |
| 17 | `LubanAgent.Plugin.NewProject` 编译修复 | 6 文件 | 编译 |
| 18 | `smoke/plugin-system` 编译修复 | 4 工程 | 单独构建通过 |
| 19 | 全量验收（构建 / 测试 / 静态扫描） | — | 全绿 + 扫描清零 |
| 20 | 手动冒烟（Cli / Codex） | — | 见任务内清单 |
| 21 | 发包与切回 `PackageReference`（用户人工） | 各 csproj | 包引用形态 + 版本号提升 |

工作量参考（实测调用点，正则见设计稿 §7）：`LuBan.AIAgent` 73 文件 / 289 处、`LubanAgentCore` 19 / 178、`LubanAgentCli` 15 / 83、`LubanAgentCodex` 19 / 87、`LubanAgentA2A` 5 / 45、`LubanAgent.Tests` 8 / 16。

---

## Task 1：`LuBan.DI` 前置——补实现层包引用

**Files：**
- Modify: `luban-framework/LuBan.DI/LuBan.DI.csproj`

**为什么：** 现状 `LuBan.DI` 只有 `Microsoft.Extensions.DependencyInjection.Abstractions`，`BuildServiceProvider()` 是靠 `LuBan.Common` 传递带进来的实现层。`DiContainer` 要直接 `new ServiceCollection().BuildServiceProvider(...)`，必须显式声明实现层包，否则一旦 `LuBan.Common` 调整传递引用就断编译。

- [ ] **Step 1：改 csproj**

在 `LuBan.DI.csproj` 的 `ItemGroup` 里，把

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.14" />
</ItemGroup>
```

改成

```xml
<ItemGroup>
  <!-- Abstractions 由实现层包传递提供，此处显式保留便于读者看清边界；版本与实现层一致 -->
  <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.14" />
</ItemGroup>
```

> 不要动 `<Version>2026.10.29.1</Version>`，版本号统一在 Task 21 提升。

- [ ] **Step 2：单独构建**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.DI\LuBan.DI.csproj -v m
```

期望：`Build succeeded`，且 `bin\Debug\net10.0` 下同时出现 `LuBan.DI.dll` 与 `Microsoft.Extensions.DependencyInjection.dll`。

- [ ] **Step 3：检查点**

```powershell
Select-String -Path D:\WorkBench\Walle\luban\luban-framework\LuBan.DI\LuBan.DI.csproj -Pattern 'Microsoft.Extensions.DependencyInjection'
```

期望：只有一条实现层 `PackageReference`（`Abstractions` 那行已删）。

**验证记录：** ⬜ 未执行

---

## Task 2：`DiContainer` 第一部分——模式、注册、解析

**Files：**
- Create: `luban-framework/LuBan.DI/DiContainer.cs`

**设计要点（写代码前先读）：**
- **非 `sealed`**，`Dispose()` / `DisposeAsync()` / `GetService(Type)` / `GetRequiredService(Type)` / `CreateScope()` 都是 `virtual`（`AgentHostServices` 要继承并 `override` Dispose）。
- 四种**模式**：`Root`（宿主根，可注册可 Build）、`ProviderView`（只解析）、`CollectionView`（只注册、不可 Build）、`Scope`（只解析、拥有 scope）。
- `IServiceCollection` 成员**全部显式实现**（`void IServiceCollection.Add(...)` 等），避免污染 `DiContainer` 的公开方法表，也避免和 MS 的扩展方法/我们的实例方法打架。
- 公开注册方法一律返回 `DiContainer`（链式），内部统一 `lock (_sync)`。
- 工厂参数类型是 `DiContainer`，实现时用 `sp.AsDiContainer()` 把 MS 传进来的 provider 包成视图再回调（**不能强转 `(DiContainer)sp`**，MS 传的是内部 `ServiceProvider`，强转会 `InvalidCastException`）。

- [ ] **Step 1：建 `DiContainer.cs`（注册 + 解析）**

```csharp
namespace LuBan.DI;

/// <summary>容器模式。</summary>
internal enum DiContainerMode
{
    /// <summary>宿主根：可注册、可构建。</summary>
    Root,
    /// <summary>外部 provider 包装视图：只解析。</summary>
    ProviderView,
    /// <summary>外部集合包装视图：只注册、不可构建。</summary>
    CollectionView,
    /// <summary>由 CreateScope 派生的作用域容器：只解析。</summary>
    Scope
}

/// <summary>
/// LuBan 统一 DI 容器。同时实现 <see cref="IServiceCollection"/>（注册期）、
/// <see cref="IServiceProvider"/>（解析期）、<see cref="IServiceScopeFactory"/>（互操作兜底）。
/// </summary>
public partial class DiContainer : IServiceCollection, IServiceProvider, IServiceScopeFactory, IDisposable, IAsyncDisposable
{
    private static DiContainer _current = new();

    /// <summary>
    /// 全局容器入口。定位：承接原 <see cref="ServiceProviderUtil"/> 的全部静态入口。
    /// 默认为空容器（解析失败、注册可用）；调用 <see cref="UseAsGlobal"/> 后指向宿主容器。
    /// </summary>
    public static DiContainer Instance => Volatile.Read(ref _current);

    private readonly object _sync = new();
    private readonly DiContainerMode _mode;
    private readonly bool _canBuild;
    private readonly List<DiContainer> _children = new();

    private IServiceCollection _services;
    private IServiceProvider? _provider;
    private IServiceScope? _scope;
    private DiContainer? _root;
    private bool _ownsProvider;
    private int _disposed;

    /// <summary>创建空容器（宿主根）。</summary>
    public DiContainer() : this(new ServiceCollection(), DiContainerMode.Root) { }

    /// <summary>包装已有集合（宿主根：接管该集合，可注册、可构建）。</summary>
    public DiContainer(IServiceCollection services)
        : this(services ?? throw new ArgumentNullException(nameof(services)), DiContainerMode.Root) { }

    /// <summary>包装已有 provider（已构建视图：只解析，不持有、不释放被包装对象）。</summary>
    public DiContainer(IServiceProvider provider)
        : this(new ServiceCollection(), DiContainerMode.ProviderView)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _ownsProvider = false;
    }

    internal DiContainer(IServiceCollection services, DiContainerMode mode)
    {
        _services = services;
        _mode = mode;
        _canBuild = mode == DiContainerMode.Root;
    }

    /// <summary>作用域容器（由 CreateScope 创建）。</summary>
    private DiContainer(IServiceScope scope, DiContainer root) : this(new ServiceCollection(), DiContainerMode.Scope)
    {
        _scope = scope;
        _provider = scope.ServiceProvider;
        _ownsProvider = true;
        _root = root;
    }

    /// <summary>当前实例是否即全局容器。</summary>
    public bool IsGlobal => ReferenceEquals(Instance, this);

    /// <summary>是否已构建（或已是视图）。</summary>
    public bool IsBuilt => _provider != null;

    /// <summary>是否为作用域容器。</summary>
    public bool IsScope => _mode == DiContainerMode.Scope;

    /// <summary>作用域容器的根容器；非作用域返回 null。</summary>
    public DiContainer? Parent => _root;

    /// <summary>内部 provider（极少数据三方互操作使用；产品代码禁止直接使用）。</summary>
    public IServiceProvider Provider => RequireProvider();

    /// <summary>
    /// 把当前实例设为全局容器。必须在本实例<b>全部注册完成之后</b>调用：
    /// 切换后，原先注册到旧全局实例上的服务不再可见（与折叠前 `ServiceProviderUtil.Services = services` 等价）。
    /// </summary>
    public DiContainer UseAsGlobal()
    {
        Interlocked.Exchange(ref _current, this);
        return this;
    }

    // ---------- 解析 ----------

    /// <inheritdoc />
    public virtual object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ThrowIfDisposed();
        return _provider?.GetService(serviceType);   // 未构建：返回 null（GetService 契约允许）
    }

    /// <summary>解析服务；未注册或未构建返回 null。</summary>
    public T? GetService<T>() => (T?)GetService(typeof(T));

    /// <inheritdoc />
    public virtual object GetRequiredService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return RequireProvider().GetRequiredService(serviceType);
    }

    /// <summary>解析服务；未注册或未构建抛异常。</summary>
    public T GetRequiredService<T>() where T : notnull => (T)GetRequiredService(typeof(T));

    /// <summary>解析全部注册。</summary>
    public IEnumerable<T> GetServices<T>() => RequireProvider().GetServices<T>();

    /// <summary>未构建 / 已释放的失败语义集中在这一点上。</summary>
    private IServiceProvider RequireProvider()
    {
        ThrowIfDisposed();
        return _provider ?? throw new InvalidOperationException("容器尚未构建，请先调用 Build()。");
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(DiContainer));
    }

    // ---------- 注册 ----------

    /// <summary>单例。</summary>
    public DiContainer AddSingleton<TService>() where TService : class
        => Add(ServiceDescriptor.Singleton<TService>());

    /// <summary>单例（服务 + 实现）。</summary>
    public DiContainer AddSingleton<TService, TImpl>() where TService : class where TImpl : class, TService
        => Add(ServiceDescriptor.Singleton<TService, TImpl>());

    /// <summary>单例（实例）。</summary>
    public DiContainer AddSingleton<TService>(TService instance) where TService : class
        => Add(ServiceDescriptor.Singleton(instance));

    /// <summary>单例（工厂，工厂参数是 DiContainer）。</summary>
    public DiContainer AddSingleton<TService>(Func<DiContainer, TService> factory) where TService : class
        => Add(ServiceDescriptor.Singleton<TService>(sp => factory(sp.AsDiContainer())));

    /// <summary>单例（服务 + 实现工厂）。</summary>
    public DiContainer AddSingleton<TService, TImpl>(Func<DiContainer, TImpl> factory)
        where TService : class where TImpl : class, TService
        => Add(ServiceDescriptor.Singleton<TService>(sp => factory(sp.AsDiContainer())));

    /// <summary>作用域。</summary>
    public DiContainer AddScoped<TService>() where TService : class
        => Add(ServiceDescriptor.Scoped<TService>());

    /// <summary>作用域（服务 + 实现）。</summary>
    public DiContainer AddScoped<TService, TImpl>() where TService : class where TImpl : class, TService
        => Add(ServiceDescriptor.Scoped<TService, TImpl>());

    /// <summary>作用域（实例，等价于单例实例）。</summary>
    public DiContainer AddScoped<TService>(TService instance) where TService : class
        => Add(ServiceDescriptor.Scoped(_ => instance));

    /// <summary>作用域（工厂）。</summary>
    public DiContainer AddScoped<TService>(Func<DiContainer, TService> factory) where TService : class
        => Add(ServiceDescriptor.Scoped<TService>(sp => factory(sp.AsDiContainer())));

    /// <summary>作用域（服务 + 实现工厂）。</summary>
    public DiContainer AddScoped<TService, TImpl>(Func<DiContainer, TImpl> factory)
        where TService : class where TImpl : class, TService
        => Add(ServiceDescriptor.Scoped<TService>(sp => factory(sp.AsDiContainer())));

    /// <summary>瞬态。</summary>
    public DiContainer AddTransient<TService>() where TService : class
        => Add(ServiceDescriptor.Transient<TService>());

    /// <summary>瞬态（服务 + 实现）。</summary>
    public DiContainer AddTransient<TService, TImpl>() where TService : class where TImpl : class, TService
        => Add(ServiceDescriptor.Transient<TService, TImpl>());

    /// <summary>瞬态（实例，等价于单例实例）。</summary>
    public DiContainer AddTransient<TService>(TService instance) where TService : class
        => Add(ServiceDescriptor.Transient(_ => instance));

    /// <summary>瞬态（工厂）。</summary>
    public DiContainer AddTransient<TService>(Func<DiContainer, TService> factory) where TService : class
        => Add(ServiceDescriptor.Transient<TService>(sp => factory(sp.AsDiContainer())));

    /// <summary>瞬态（服务 + 实现工厂）。</summary>
    public DiContainer AddTransient<TService, TImpl>(Func<DiContainer, TImpl> factory)
        where TService : class where TImpl : class, TService
        => Add(ServiceDescriptor.Transient<TService>(sp => factory(sp.AsDiContainer())));

    // ---------- 注册（非泛型，运行时类型 / 外部插件必需） ----------

    /// <summary>单例（服务自身）。</summary>
    public DiContainer AddSingleton([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type serviceType)
        => Add(ServiceDescriptor.Singleton(serviceType));

    /// <summary>单例（服务 + 实现）。</summary>
    public DiContainer AddSingleton(Type serviceType, Type implementationType)
        => Add(ServiceDescriptor.Singleton(serviceType, implementationType));

    /// <summary>单例（实例）。</summary>
    public DiContainer AddSingleton(Type serviceType, object instance)
        => Add(ServiceDescriptor.Singleton(serviceType, instance));

    /// <summary>单例（工厂）。</summary>
    public DiContainer AddSingleton(Type serviceType, Func<DiContainer, object> factory)
        => Add(ServiceDescriptor.Singleton(serviceType, sp => factory(sp.AsDiContainer())));

    /// <summary>作用域（服务自身）。</summary>
    public DiContainer AddScoped([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type serviceType)
        => Add(ServiceDescriptor.Scoped(serviceType));

    /// <summary>作用域（服务 + 实现）。</summary>
    public DiContainer AddScoped(Type serviceType, Type implementationType)
        => Add(ServiceDescriptor.Scoped(serviceType, implementationType));

    /// <summary>作用域（工厂）。</summary>
    public DiContainer AddScoped(Type serviceType, Func<DiContainer, object> factory)
        => Add(ServiceDescriptor.Scoped(serviceType, sp => factory(sp.AsDiContainer())));

    /// <summary>瞬态（服务自身）。</summary>
    public DiContainer AddTransient([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type serviceType)
        => Add(ServiceDescriptor.Transient(serviceType));

    /// <summary>瞬态（服务 + 实现）。</summary>
    public DiContainer AddTransient(Type serviceType, Type implementationType)
        => Add(ServiceDescriptor.Transient(serviceType, implementationType));

    /// <summary>瞬态（工厂）。</summary>
    public DiContainer AddTransient(Type serviceType, Func<DiContainer, object> factory)
        => Add(ServiceDescriptor.Transient(serviceType, sp => factory(sp.AsDiContainer())));

    // ---------- 覆盖 / 清理 ----------

    /// <summary>不存在时才注册单例。</summary>
    public DiContainer TryAddSingleton<TService>() where TService : class
        => TryAdd(ServiceDescriptor.Singleton<TService>());

    /// <summary>不存在时才注册单例（服务 + 实现）。</summary>
    public DiContainer TryAddSingleton<TService, TImpl>() where TService : class where TImpl : class, TService
        => TryAdd(ServiceDescriptor.Singleton<TService, TImpl>());

    /// <summary>不存在时才注册单例（工厂）。</summary>
    public DiContainer TryAddSingleton<TService>(Func<DiContainer, TService> factory) where TService : class
        => TryAdd(ServiceDescriptor.Singleton<TService>(sp => factory(sp.AsDiContainer())));

    /// <summary>不存在时才注册作用域。</summary>
    public DiContainer TryAddScoped<TService>() where TService : class
        => TryAdd(ServiceDescriptor.Scoped<TService>());

    /// <summary>不存在时才注册作用域（服务 + 实现）。</summary>
    public DiContainer TryAddScoped<TService, TImpl>() where TService : class where TImpl : class, TService
        => TryAdd(ServiceDescriptor.Scoped<TService, TImpl>());

    /// <summary>不存在时才注册瞬态。</summary>
    public DiContainer TryAddTransient<TService>() where TService : class
        => TryAdd(ServiceDescriptor.Transient<TService>());

    /// <summary>不存在时才注册瞬态（服务 + 实现）。</summary>
    public DiContainer TryAddTransient<TService, TImpl>() where TService : class where TImpl : class, TService
        => TryAdd(ServiceDescriptor.Transient<TService, TImpl>());

    /// <summary>不存在时才注册瞬态（工厂）。</summary>
    public DiContainer TryAddTransient<TService>(Func<DiContainer, TService> factory) where TService : class
        => TryAdd(ServiceDescriptor.Transient<TService>(sp => factory(sp.AsDiContainer())));

    /// <summary>替换单例（移除该类型的全部注册后追加一条，语义同 MS 的 Replace）。</summary>
    public DiContainer ReplaceSingleton<TService>(TService instance) where TService : class
        => RemoveAll<TService>().Add(ServiceDescriptor.Singleton(instance));

    /// <summary>替换单例（服务 + 实现）。</summary>
    public DiContainer ReplaceSingleton<TService, TImpl>() where TService : class where TImpl : class, TService
        => RemoveAll<TService>().Add(ServiceDescriptor.Singleton<TService, TImpl>());

    /// <summary>替换单例（工厂）。</summary>
    public DiContainer ReplaceSingleton<TService>(Func<DiContainer, TService> factory) where TService : class
        => RemoveAll<TService>().Add(ServiceDescriptor.Singleton<TService>(sp => factory(sp.AsDiContainer())));

    /// <summary>替换作用域（实例）。</summary>
    public DiContainer ReplaceScoped<TService>(TService instance) where TService : class
        => RemoveAll<TService>().Add(ServiceDescriptor.Scoped(_ => instance));

    /// <summary>替换作用域（服务 + 实现）。</summary>
    public DiContainer ReplaceScoped<TService, TImpl>() where TService : class where TImpl : class, TService
        => RemoveAll<TService>().Add(ServiceDescriptor.Scoped<TService, TImpl>());

    /// <summary>替换作用域（工厂）。</summary>
    public DiContainer ReplaceScoped<TService>(Func<DiContainer, TService> factory) where TService : class
        => RemoveAll<TService>().Add(ServiceDescriptor.Scoped<TService>(sp => factory(sp.AsDiContainer())));

    /// <summary>移除指定服务类型的全部注册。</summary>
    public DiContainer RemoveAll<TService>() => RemoveAll(typeof(TService));

    /// <summary>移除指定服务类型的全部注册。</summary>
    public DiContainer RemoveAll(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        lock (_sync)
        {
            ServiceCollectionMutationGuard();
            for (var i = _services.Count - 1; i >= 0; i--)
            {
                if (_services[i].ServiceType == serviceType) _services.RemoveAt(i);
            }
            return this;
        }
    }

    // ---------- 内部 ----------

    private DiContainer Add(ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_sync)
        {
            ServiceCollectionMutationGuard();
            _services.Add(descriptor);
            return this;
        }
    }

    private DiContainer TryAdd(ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_sync)
        {
            ServiceCollectionMutationGuard();
            for (var i = 0; i < _services.Count; i++)
                if (_services[i].ServiceType == descriptor.ServiceType) return this;
            _services.Add(descriptor);
            return this;
        }
    }

    /// <summary>注册合法性：ProviderView / Scope 不可注册，已构建的根不可再注册。</summary>
    private void ServiceCollectionMutationGuard()
    {
        if (_mode is DiContainerMode.ProviderView or DiContainerMode.Scope)
            throw new InvalidOperationException("当前 DiContainer 视图不可注册服务。");
        if (_provider != null)
            throw new InvalidOperationException("容器已构建，不可再注册服务；请先 ResetServices 或新建容器。");
    }
}
```

- [ ] **Step 2：编译（此时会因为缺 `AsDiContainer` 报错，先忍）**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.DI\LuBan.DI.csproj -v m 2>&1 | Select-String -Pattern 'error' | Select-Object -First 10
```

期望：只剩「`AsDiContainer` 不存在」这类错误（其余成员在 Task 3/4 补齐）；不应出现「未实现接口成员」「重复定义」。

- [ ] **Step 3：检查点**

人工过一遍，确认下列形态全部存在（后续任务依赖）：`AddSingleton<T>()`、`AddSingleton<T,TImpl>()`、`AddSingleton<T>(instance)`、`AddSingleton<T>(Func<DiContainer,T>)`、`AddSingleton(Type,Type)`、`AddScoped(Type,Type)`、`AddTransient(Type,Type)`、`TryAddScoped<T>()`、`TryAddSingleton<T>()`、`ReplaceSingleton<T>(instance)`、`RemoveAll<T>()`。

**验证记录：** ⬜ 未执行

---

## Task 3：`DiContainer` 第二部分——构建、作用域、释放、显式接口适配

**Files：**
- Create: `luban-framework/LuBan.DI/DiContainer.Scope.cs`
- Create: `luban-framework/LuBan.DI/DiContainer.Adapter.cs`

> **2026-10-30 变更（用户口径：一个对象一个文件）**：`Class.Part.cs` 拆分不符合本仓库文件规范（`LuBan.DI` 之外无同类命名）。实际落地为 **`DiContainer.cs` 单文件承载容器全部成员**，另拆 `DiContainerMode.cs`（内部枚举）与 `DiContainerScope.cs`（`IServiceScope` 适配器）；`DiContainer.Scope.cs` / `DiContainer.Adapter.cs` **已删除**。下方 Step 1/2 的代码块仅作历史记录，内容已原样并入 `DiContainer.cs`。

**设计要点：**
- `Build()` **幂等**：重复调用直接 `return this`，不重建 —— 这是「防双 provider」的第一道闸。
- `Rebuild()` 是 `ServiceProviderUtil.BuildProvider()` 的专用语义：**每次重建并释放旧 provider**，可选延迟释放（原 `GetRequiredServiceForOnce` 延迟 5 秒）。
- `Build` / `Rebuild` / `ResetServices` 只允许 `Root` 模式，视图与 scope 一律抛 `InvalidOperationException`。
- `CreateScope()` **永远从根派生**（`_root ?? this`），不继承调用方 scope 的身份状态；这与 MS.DI 一致，`CreateUserScope` 的身份回填依赖它。
- `IServiceScopeFactory.CreateScope()` 必须**显式实现**：返回类型 `IServiceScope` 与公开 `DiContainer CreateScope()` 不同，不能共存为同一成员。
- 释放顺序：先子容器（scope），再自身 scope，最后（仅 `_ownsProvider`）provider。包装视图 `_ownsProvider = false`，**绝不释放别人**。
- `Dispose()` / `DisposeAsync()` 必须 `virtual`，`AgentHostServices` 要 `override`。

- [ ] **Step 1：建 `DiContainer.Scope.cs`**

```csharp
namespace LuBan.DI;

public partial class DiContainer
{
    /// <summary>
    /// 构建容器（幂等：已构建直接返回本实例，不重建）。
    /// </summary>
    /// <param name="validateScopes">校验 scoped 服务不得从根解析（默认开启，与 AgentHostBuilder 现状一致）。</param>
    /// <param name="validateOnBuild">构建时校验全部注册可被实例化（默认开启）。</param>
    public DiContainer Build(bool validateScopes = true, bool validateOnBuild = true)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_mode != DiContainerMode.Root)
                throw new InvalidOperationException("只有宿主根容器可以构建；视图与作用域容器不可构建。");

            if (_provider != null) return this;   // 幂等

            _provider = _services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = validateScopes,
                ValidateOnBuild = validateOnBuild
            });
            _ownsProvider = true;
            return this;
        }
    }

    /// <summary>
    /// 重建容器：每次重建并释放旧 provider（供 <see cref="ServiceProviderUtil"/> 折叠使用）。
    /// </summary>
    /// <param name="validateScopes">校验 scoped 服务不得从根解析。</param>
    /// <param name="validateOnBuild">构建时校验全部注册。</param>
    /// <param name="deferOldDispose">旧 provider 的延迟释放时长；null 表示立即释放。</param>
    public DiContainer Rebuild(bool validateScopes = false, bool validateOnBuild = false, TimeSpan? deferOldDispose = null)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_mode != DiContainerMode.Root)
                throw new InvalidOperationException("只有宿主根容器可以构建；视图与作用域容器不可构建。");

            var old = _provider;
            _provider = _services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = validateScopes,
                ValidateOnBuild = validateOnBuild
            });
            _ownsProvider = true;

            if (old != null && !ReferenceEquals(old, _provider))
                DisposeProvider(old, deferOldDispose);

            return this;
        }
    }

    /// <summary>
    /// 替换内部服务集合（供 <see cref="ServiceProviderUtil.Services"/> setter 使用）；不触碰已构建的 provider。
    /// </summary>
    public DiContainer ResetServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_mode != DiContainerMode.Root)
                throw new InvalidOperationException("只有宿主根容器可以替换服务集合。");
            _services = services;
            return this;
        }
    }

    /// <summary>
    /// 从根派生一个作用域容器。语义与 MS.DI 一致：<b>永远从根派生</b>，不继承调用方作用域的身份状态。
    /// </summary>
    public virtual DiContainer CreateScope()
    {
        ThrowIfDisposed();
        var root = _root ?? this;
        // 未构建时 RequireProvider 抛「容器尚未构建」
        var scope = root.RequireProvider().GetRequiredService<IServiceScopeFactory>().CreateScope();
        var child = new DiContainer(scope, root);
        root.TrackScope(child);
        return child;
    }

    /// <summary><see cref="IServiceScopeFactory"/> 显式实现（互操作兜底，产品代码请直接用 <see cref="CreateScope()"/>）。</summary>
    IServiceScope IServiceScopeFactory.CreateScope() => new DiContainerScope(this.CreateScope());

    private void TrackScope(DiContainer child)
    {
        lock (_sync) _children.Add(child);
    }

    private void DetachScope(DiContainer child)
    {
        lock (_sync) _children.Remove(child);
    }

    private static void DisposeProvider(IServiceProvider provider, TimeSpan? deferOldDispose)
    {
        if (deferOldDispose is { } delay && delay > TimeSpan.Zero)
        {
            // 延迟销毁旧 provider，避免正在使用的服务被意外销毁（沿用 ServiceProviderUtil 原逻辑）
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay).ConfigureAwait(false);
                if (provider is IAsyncDisposable ad) await ad.DisposeAsync().ConfigureAwait(false);
                else (provider as IDisposable)?.Dispose();
            });
            return;
        }

        if (provider is IDisposable disposable) disposable.Dispose();
    }

    /// <inheritdoc />
    public virtual void Dispose() => DisposeCoreAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync() => DisposeCoreAsync();

    private async ValueTask DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        DiContainer[] children;
        lock (_sync)
        {
            children = _children.ToArray();
            _children.Clear();
        }
        foreach (var child in children) await child.DisposeAsync().ConfigureAwait(false);

        if (_scope != null)
        {
            // scope：只释放自己的 scope，不释放根
            _scope.Dispose();
            _scope = null;
            _root?.DetachScope(this);
        }
        else if (_ownsProvider && _provider != null)
        {
            if (_provider is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else
                (_provider as IDisposable)?.Dispose();
        }

        _provider = null;
        GC.SuppressFinalize(this);
    }

    /// <summary><see cref="IServiceScopeFactory.CreateScope()"/> 的适配器（仅供第三方按接口消费）。</summary>
    private sealed class DiContainerScope(DiContainer container) : IServiceScope
    {
        public IServiceProvider ServiceProvider => container;

        public void Dispose() => container.Dispose();
    }
}
```

- [ ] **Step 2：建 `DiContainer.Adapter.cs`（`IServiceCollection` 显式实现）**

```csharp
namespace LuBan.DI;

public partial class DiContainer : IServiceCollection
{
    /// <summary>当前模式下是否允许注册。</summary>
    private bool CanRegister =>
        _mode is DiContainerMode.Root or DiContainerMode.CollectionView && _provider == null;

    int ICollection<ServiceDescriptor>.Count => _services.Count;

    bool ICollection<ServiceDescriptor>.IsReadOnly => !CanRegister;

    ServiceDescriptor IList<ServiceDescriptor>.this[int index]
    {
        get => _services[index];
        set
        {
            lock (_sync)
            {
                ServiceCollectionMutationGuard();
                _services[index] = value;
            }
        }
    }

    void ICollection<ServiceDescriptor>.Add(ServiceDescriptor item) => Add(item);

    void ICollection<ServiceDescriptor>.Clear()
    {
        lock (_sync)
        {
            ServiceCollectionMutationGuard();
            _services.Clear();
        }
    }

    bool ICollection<ServiceDescriptor>.Contains(ServiceDescriptor item) => _services.Contains(item);

    void ICollection<ServiceDescriptor>.CopyTo(ServiceDescriptor[] array, int arrayIndex)
        => _services.CopyTo(array, arrayIndex);

    IEnumerator<ServiceDescriptor> IEnumerable<ServiceDescriptor>.GetEnumerator()
        => _services.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        => _services.GetEnumerator();

    int IList<ServiceDescriptor>.IndexOf(ServiceDescriptor item) => _services.IndexOf(item);

    void IList<ServiceDescriptor>.Insert(int index, ServiceDescriptor item)
    {
        lock (_sync)
        {
            ServiceCollectionMutationGuard();
            _services.Insert(index, item);
        }
    }

    void IList<ServiceDescriptor>.RemoveAt(int index)
    {
        lock (_sync)
        {
            ServiceCollectionMutationGuard();
            _services.RemoveAt(index);
        }
    }

    bool ICollection<ServiceDescriptor>.Remove(ServiceDescriptor item)
    {
        lock (_sync)
        {
            ServiceCollectionMutationGuard();
            return _services.Remove(item);
        }
    }
}
```

> 如果编译器报「未实现 `IReadOnlyList<ServiceDescriptor>.this[int]`」之类，说明本 SDK 的 `IServiceCollection` 还继承了只读接口，按提示补一条同义显式成员（`ServiceDescriptor IReadOnlyList<ServiceDescriptor>.this[int index] => _services[index];`）即可，不改设计。

- [ ] **Step 3：编译**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.DI\LuBan.DI.csproj -v m
```

期望：`Build succeeded`（`AsDiContainer` 在 Task 4 补，若此时仍报缺失，继续 Task 4 后再编译）。

**验证记录：** ⬜ 未执行

---

## Task 4：`AsDiContainer` 三重载 + `ServiceProviderUtil` 删除

> **2026-10-30 变更（用户决定，方案 A）**：Task 4 原定为「折叠」——保留 `ServiceProviderUtil` 全部公开 API、内部转发 `DiContainer.Instance`。该方案**作废**，改为**整体删除** `LuBan.DI/ServiceProviderUtil.cs`，19 处调用点直接改写为 `DiContainer.Instance.*`。理由：包发布后外部读者会同时看到 `ServiceProviderUtil` 与 `DiContainer.Instance` 两个全局入口。
>
> 因此 Step 2 的「改写 `ServiceProviderUtil.cs`」代码块**仅作历史记录**，实际执行的是删除 + 调用点迁移（映射表见设计稿 §4.7）。Step 4 的扫描目标文件同步变为「全仓库不得再出现 `ServiceProviderUtil`」。

**Files：**
- Create: `luban-framework/LuBan.DI/DiContainerExtensions.cs`
- Delete: `luban-framework/LuBan.DI/ServiceProviderUtil.cs`
- Modify: 19 个调用点文件（`StartupUtil`、`BaseNodeHandler`、`OnlineUserStoreProvider`、`ServiceDescriptorUtil`、`LuBanOrm`、`SqlUtil`、`BaseService`、`BaseService<T>`、`ServiceCacheExtention`、`ExtraFileController`、`LuBanOrmMiddleware`、`WebApp`、`AuthController`、`DatabaseService`、`LuBanLoggingServiceExtensions`、`FlowEventBusPublisher`、`FlowEventListenerManager`、`LoggerCollector` 等）

**设计要点：**
- `AsDiContainer` **必须三重载**：`DiContainer`（返回自身）、`IServiceCollection`（集合视图，`CollectionView`）、`IServiceProvider`（provider 视图）。否则 `DiContainer` 同时是集合与 provider，调用会产生二义性。
- `ServiceProviderUtil` 原有静态成员中，有调用点的 4 类（`GetService` / `GetRequiredService`、`this Type` 扩展、`InitServiceProvider` / `BuildProvider`）逐一映射到 `DiContainer.Instance` 同名成员或 `ResetServices` / `Rebuild`；零调用点的 `Services` / `GetExceptionScope` / `GetRequiredServiceForOnce` 直接删除，不迁成死 API。
- `GetService` 未构建返回 `null`（`DiContainer` 现状语义）；`GetRequiredService` 未构建抛异常。
- `BuildProvider()` 映射为 `Rebuild()`（每次重建、不校验），不是幂等的 `Build()`。
- `AutoInjectAllCustomerServices` 改为 `this IServiceCollection` 扩展，调用点文本不变。

- [x] **Step 1：建 `DiContainerExtensions.cs`**

```csharp
namespace LuBan.DI;

/// <summary>容器适配扩展。</summary>
public static class DiContainerExtensions
{
    /// <summary>已是容器，直接返回自身（必需：否则同时满足集合/provider 重载会产生二义性）。</summary>
    public static DiContainer AsDiContainer(this DiContainer container)
        => container ?? throw new ArgumentNullException(nameof(container));

    /// <summary>把外部 provider 包成已构建视图（只解析；不持有、不释放被包装对象）。</summary>
    public static DiContainer AsDiContainer(this IServiceProvider provider)
        => new(provider ?? throw new ArgumentNullException(nameof(provider)));

    /// <summary>把外部集合包成注册视图（只注册，不可 Build）。</summary>
    public static DiContainer AsDiContainer(this IServiceCollection services)
        => new(services ?? throw new ArgumentNullException(nameof(services)), DiContainerMode.CollectionView);
}
```

- [x] **Step 2（已作废，改为删除）：`ServiceProviderUtil.cs` 整体删除 + 19 处调用点迁移**

> 实际执行：`rm LuBan.DI/ServiceProviderUtil.cs`，调用点按设计稿 §4.7 映射表改为 `DiContainer.Instance.GetService/GetRequiredService`、`ResetServices(services)`、`Rebuild()`。**没有**保留转发层。
> 下方代码块是原「折叠」方案草稿，仅作历史记录，**未落地**。

```csharp
namespace LuBan.DI;

/// <summary>
/// DI 工具类。已折叠到 <see cref="DiContainer.Instance"/>：全进程只有一份全局 provider。
/// 公开 API 保持不变，供历史调用点继续工作。
/// </summary>
public static class ServiceProviderUtil
{
    private static DiContainer Container => DiContainer.Instance;

    /// <summary>服务集合（即全局容器的集合本体）。</summary>
    public static IServiceCollection Services
    {
        set => Container.ResetServices(value ?? throw new ArgumentNullException(nameof(value)));
        get => Container;
    }

    /// <summary>设置服务提供容器。</summary>
    public static void InitServiceProvider(this IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>注入瞬态周期的服务。</summary>
    public static void AddTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>()
        where TService : class
        => Container.AddTransient<TService>();

    /// <summary>注入请求生存周期的服务。</summary>
    public static void AddScoped<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>()
        where TService : class
        => Container.AddScoped<TService>();

    /// <summary>注入单例周期的服务。</summary>
    public static void AddSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>()
        where TService : class
        => Container.AddSingleton<TService>();

    /// <summary>完成 DI 注入容器（重建并释放旧 provider）。</summary>
    public static void BuildProvider(this IServiceCollection services)
    {
        ConsoleUtil.WriteLineWithCount("完成DI注入容器", color: ConsoleColor.Green);
        Container.Rebuild();
    }

    /// <summary>完成 DI 注入容器，一般放在最后一步。</summary>
    public static void BuildProvider()
    {
        ConsoleUtil.WriteLineWithCount("创建DI容器", color: ConsoleColor.Green);
        Container.Rebuild();
    }

    /// <summary>查找已注入的类型（未构建返回 null）。</summary>
    public static object? GetService([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] this Type type)
        => Container.GetService(type);

    /// <summary>查找已注入的类型（未构建返回 default）。</summary>
    public static TService? GetService<TService>() => Container.GetService<TService>();

    /// <summary>获取服务；未构建抛异常。</summary>
    public static TService GetRequiredService<TService>() where TService : notnull
        => Container.GetRequiredService<TService>();

    /// <summary>获取服务；未构建抛异常。</summary>
    public static object GetRequiredService([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] this Type type)
        => Container.GetRequiredService(type);

    /// <summary>获取作用域共享的异常信息。</summary>
    public static ExceptionScope GetExceptionScope() => GetRequiredService<ExceptionScope>();

    /// <summary>一次性获取某个服务（注册 → 重建 → 延迟释放旧 provider → 解析）。</summary>
    public static T GetRequiredServiceForOnce<T>(Action<IServiceCollection> addServiceProvider) where T : notnull
    {
        addServiceProvider?.Invoke(Container);
        Container.Rebuild(deferOldDispose: TimeSpan.FromSeconds(5));
        return Container.GetRequiredService<T>();
    }

    /// <summary>全局自动注入带 InjectionAttribute 的服务。</summary>
    public static IServiceCollection AutoInjectAllCustomerServices(this IServiceCollection services)
    {
        ConsoleUtil.WriteLineWithCount(
            "正在自动注入全局中符合框架的InjectionAttribute的IScoped，ISingleton，ITransient的对象到DI容器",
            color: ConsoleColor.Green);
        return ServiceDescriptorUtil.AutoInjectAllCustomerServices(services);
    }
}
```

> 若 `ConsoleUtil` / `ExceptionScope` / `ServiceDescriptorUtil` 的命名空间不在文件作用域内（它们本来就在 `LuBan.DI` / `LuBan.Common`，且 `GlobalUsings.cs` 已 `global using`），无需额外 using。

- [x] **Step 3：编译 + 全框架构建**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.DI\LuBan.DI.csproj -v m
```

期望：`Build succeeded`，0 warning（`IsTrimmable=true` 下若出现 IL2xxx 裁剪告警，补 `[DynamicallyAccessedMembers]` 而不是关警告）。

- [x] **Step 4：检查点（关键：不能有两套全局状态）**

```powershell
Get-ChildItem -Recurse -Filter *.cs D:\WorkBench\Walle\luban\luban-framework | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern 'ServiceProviderUtil|BuildProvider\(|InitServiceProvider'
```

期望：**无输出**（文件已删除，全仓库无残留引用）。

**验证记录：** ✅ 已完成（2026-10-30）
- `ServiceProviderUtil.cs` 已删除；19 个源文件迁移到 `DiContainer.Instance`。
- `dotnet build LuBan.FrameWork.sln` → `已成功生成`，0 警告 0 错误。
- 全仓库（framework + agent，源码与文档）扫描 `ServiceProviderUtil` / `BuildProvider(` / `InitServiceProvider` → 0 条。
- `DiContainerUnitTest` 中 `ServiceProviderUtil_SharesProviderWithInstance` 已替换为 `ResetServices_ThenRebuild_RefreshesGlobalProvider`，10 个用例全绿。

---

## Task 5：`LuBan.DI` 单元测试

**Files：**
- Create: `luban-framework/LuBan.XTestProject/DiContainerUnitTest.cs`

**为什么放这里：** 框架测试项目 `LuBan.UnitTestProject.csproj` 已 `ProjectReference` 到 `LuBan.AIAgent`（进而到 `LuBan.DI`），并且是 sln 里挂的测试项目。

**测试风格：** MSTest，`[TestClass]` / `[TestMethod]`，断言用 `Assert.ThrowsExactly<T>`（框架测试里既有写法）。

- [ ] **Step 1：写测试（9 个用例，覆盖设计稿 §9.9 的全部要求）**

```csharp
using LuBan.DI;
using Microsoft.Extensions.DependencyInjection;

namespace LuBan.XTestProject;

[TestClass]
public class DiContainerUnitTest
{
    private DiContainer? _previousGlobal;

    [TestInitialize]
    public void Setup() => _previousGlobal = DiContainer.Instance;

    [TestCleanup]
    public void Cleanup() => _previousGlobal?.UseAsGlobal();

    private sealed class Foo { }
    private sealed class Bar(Foo foo) { public Foo Foo { get; } = foo; }

    [TestMethod]
    public void InstanceMethod_WinsOverExtensionMethod()
    {
        var di = new DiContainer();
        var bar = new Bar(new Foo());
        di.AddSingleton<Bar>(bar);              // 实例重载（instance）
        di.Build();

        Assert.AreSame(bar, di.GetRequiredService<Bar>());
    }

    [TestMethod]
    public void FactoryReceivesDiContainer()
    {
        var di = new DiContainer();
        DiContainer? seen = null;
        di.AddSingleton<Foo>();
        di.AddSingleton<Bar>(d => { seen = d; return new Bar(d.GetRequiredService<Foo>()); });
        di.Build();

        var bar = di.GetRequiredService<Bar>();
        Assert.IsNotNull(seen);
        Assert.IsInstanceOfType<DiContainer>(seen);
        Assert.AreSame(bar.Foo, di.GetRequiredService<Foo>());
    }

    [TestMethod]
    public void AsDiContainer_ThreeOverloads_Resolve()
    {
        var di = new DiContainer();
        di.AddSingleton<Foo>();
        di.Build();

        // 1) DiContainer → 自身
        Assert.AreSame(di, di.AsDiContainer());

        // 2) IServiceProvider → 已构建视图（可解析）
        var providerView = ((IServiceProvider)di).AsDiContainer();
        Assert.IsNotNull(providerView.GetRequiredService<Foo>());

        // 3) IServiceCollection → 注册视图（可注册、不可 Build）
        var collectionView = new ServiceCollection().AsDiContainer();
        collectionView.AddSingleton<Foo>();
        Assert.IsFalse(collectionView.IsBuilt);
    }

    [TestMethod]
    public void Build_IsIdempotent_AndViewCannotBuild()
    {
        var di = new DiContainer();
        di.AddSingleton<Foo>();
        Assert.AreSame(di, di.Build());
        Assert.AreSame(di, di.Build());                       // 幂等，未重建

        var view = ((IServiceProvider)di).AsDiContainer();
        Assert.ThrowsExactly<InvalidOperationException>(() => view.Build());
        Assert.ThrowsExactly<InvalidOperationException>(() => new ServiceCollection().AsDiContainer().Build());
    }

    [TestMethod]
    public void CreateScope_DerivesFromRoot()
    {
        var di = new DiContainer();
        di.AddScoped<Foo>();
        di.Build();

        var scope1 = di.CreateScope();
        var scope2 = scope1.CreateScope();      // 从 scope 派生的仍然来自根

        Assert.AreSame(di, scope1.Parent);
        Assert.AreSame(di, scope2.Parent);
        Assert.AreNotSame(scope1.GetRequiredService<Foo>(), scope2.GetRequiredService<Foo>());
        Assert.AreSame(scope1.GetRequiredService<Foo>(), scope1.GetRequiredService<Foo>());

        scope1.Dispose();
        scope2.Dispose();
    }

    [TestMethod]
    public void OwnsProvider_DisposeSemantics()
    {
        var built = new DiContainer();
        built.AddSingleton<Foo>().Build();
        built.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => built.GetRequiredService<Foo>());

        var inner = new ServiceCollection().AddSingleton<Foo>().BuildServiceProvider();
        var view = ((IServiceProvider)inner).AsDiContainer();
        view.Dispose();                                        // 不得释放被包装对象
        Assert.IsNotNull(inner.GetService<Foo>());
        inner.Dispose();
    }

    [TestMethod]
    public void GetServiceBeforeBuild_ReturnsNull_RequiredThrows()
    {
        var di = new DiContainer();
        di.AddSingleton<Foo>();

        Assert.IsNull(di.GetService<Foo>());
        Assert.ThrowsExactly<InvalidOperationException>(() => di.GetRequiredService<Foo>());
    }

    [TestMethod]
    public void ServiceProviderUtil_SharesProviderWithInstance()
    {
        var di = new DiContainer();
        di.AddSingleton<Foo>().UseAsGlobal();

        Assert.AreSame(di, DiContainer.Instance);
        di.Build();
        Assert.AreSame(ServiceProviderUtil.GetRequiredService<Foo>(), di.GetRequiredService<Foo>());
    }

    [TestMethod]
    public void ScopeFactoryAdapter_ReturnsWorkingScope()
    {
        var di = new DiContainer();
        di.AddScoped<Foo>();
        di.Build();

        var factory = (IServiceScopeFactory)di;
        using var scope = factory.CreateScope();
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<Foo>());
    }

    [TestMethod]
    public void ReplaceAndTryAdd_Semantics()
    {
        var di = new DiContainer();
        di.AddSingleton<Foo>();
        di.AddSingleton<Foo>();          // 两条注册
        var replacement = new Foo();
        di.ReplaceSingleton<Foo>(replacement);
        di.TryAddSingleton<Bar>();       // 不存在才加
        di.TryAddSingleton<Bar>();       // 第二次忽略
        di.Build();

        Assert.AreSame(replacement, di.GetRequiredService<Foo>());
        Assert.AreEqual(1, di.GetServices<Bar>().Count());
    }
}
```

- [ ] **Step 2：跑测试**

```powershell
dotnet test D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~DiContainerUnitTest" -v m
```

期望：10 个用例全绿。失败先看语义是否与实现一致（尤其 `CreateScope_DerivesFromRoot` 的 `Parent`）。

- [ ] **Step 3：检查点**

```powershell
dotnet test D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~DiContainerUnitTest" --logger "console;verbosity=normal"
```

期望输出里能看到 `Passed!  - Failed: 0, Passed: 10`。

**验证记录：** ⬜ 未执行

---

## Task 6：`LuBan.DI` README 中英同步

**Files：**
- Modify: `luban-framework/LuBan.DI/README.md`
- Modify: `luban-framework/LuBan.DI/README.en.md`

**背景：** AGENTS.md 明文要求每个 framework 组件的中英文 README 成对同步。

- [ ] **Step 1：两份都加「容器」章节**，中文版：

```markdown
## DiContainer（统一容器）

`LuBan.DI.DiContainer` 是框架统一的 DI 容器，同时实现 `IServiceCollection`（注册）、
`IServiceProvider`（解析）、`IServiceScopeFactory`（互操作）与 `IDisposable`。

```csharp
var di = new DiContainer();
di.AddSingleton<IFoo, Foo>();
di.AddScoped<IBar>(d => new Bar(d.GetRequiredService<IFoo>()));
di.Build();                                  // 默认 ValidateScopes + ValidateOnBuild，幂等

using var scope = di.CreateScope();          // 子容器，永远从根派生
var bar = scope.GetRequiredService<IBar>();
```

- 宿主自己 `new` 出来的根容器才允许 `Build()`；`services.AsDiContainer()` / `provider.AsDiContainer()` 是借用视图，不可 `Build()`。
- 包装外部 provider 的视图不持有、不释放被包装对象。
- `DiContainer.Instance` 是全局容器入口；`ServiceProviderUtil` 已折叠为它的转发层，全进程只有一份全局 provider。
- 未构建时 `GetService` 返回 `null`，`GetRequiredService` 抛 `InvalidOperationException`。
```

英文版对应翻译（`## DiContainer (unified container)`，同样代码块 + 同样四条要点）。

- [ ] **Step 2：检查点**

```powershell
Select-String -Path D:\WorkBench\Walle\luban\luban-framework\LuBan.DI\README.md,D:\WorkBench\Walle\luban\luban-framework\LuBan.DI\README.en.md -Pattern 'DiContainer'
```

期望：两份都有命中，且要点条数一致（中文 4 条 / 英文 4 条）。

**验证记录：** ⬜ 未执行

---

## Task 7：`LuBan.AIAgent` 契约换型（接口 + 直接实现）

**Files（按实测命中，全部文件都在 `luban-framework/LuBan.AIAgent/` 下）：**

| # | 文件:行 | 现状 | 改成 |
|---|---------|------|------|
| 1 | `Abstractions/ILuBanToolPlugin.cs:51` | `GetTools(IServiceProvider sp, ...)` | `DiContainer sp` |
| 2 | `Abstractions/ToolPluginRegistry.cs:79` | `GetAllFunctions(IServiceProvider sp, ...)` | `DiContainer sp` |
| 3 | `Plugins/Contracts/IPluginHost.cs:35` | `IServiceProvider Services { get; }` | `DiContainer Services` |
| 4 | `Plugins/Lifecycle/PluginHostScope.cs:82` | `public IServiceProvider Services => _inner.Services;` | `public DiContainer Services` |
| 5 | `Plugins/Lifecycle/PluginManager.cs:40,62` | 字段/构造 `IServiceProvider services` | `DiContainer services` |
| 6 | `Hosting/IAgentHost.cs:31` / `Hosting/AgentHost.cs:36,48` | `IServiceProvider Services` | `DiContainer Services` |
| 7 | `Hosting/IAgentSessionExtension.cs:24` | `ApplyAsync(IServiceProvider sessionServices, ...)` | `DiContainer sessionServices` |
| 8 | `Hosting/IAgentUserResolver.cs:22` / `Hosting/DefaultSeams.cs:45` | `ResolveUserId(IServiceProvider)` | `ResolveUserId(DiContainer)` |
| 9 | `Hosting/PerUserWorkspaceResolver.cs:42` | ctor 参数 `IServiceProvider services` | `DiContainer services` |
| 10 | `Hosting/SessionRuntime.cs:34,49,83` | `IServiceScope _scope` / `IServiceProvider Services => _scope.ServiceProvider` / ctor | `DiContainer _scope` / `DiContainer Services => _scope` / ctor `DiContainer scope` |
| 11 | `Hosting/SessionRuntimeManager.cs:36,46` | 字段/构造 `IServiceProvider root` | `DiContainer root` |
| 12 | `Rules/ContextInjectBuilder.cs:27,34` | `IServiceProvider? _services` | `DiContainer? _services` |
| 13 | `Rules/IRule.cs:106` | `IServiceProvider? Services { get; set; }` | `DiContainer? Services` |
| 14 | `Skills/ISkill.cs:91` | `IServiceProvider? ServiceProvider { get; set; }` | `DiContainer? ServiceProvider` |
| 15 | `Sessions/SessionChatHistoryProvider.cs:56` | ctor `IServiceProvider? services = null` | `DiContainer? services = null` |
| 16 | `Connectors/IConnector.cs:76,90` | `Func<IServiceProvider, PluginPermissionScope?, IMCPClient>` / `required IServiceProvider Services` | `Func<DiContainer, ...>` / `required DiContainer Services` |
| 17 | `Connectors/ConnectorFactoryRegistry.cs:140,150,159` | `IServiceProvider _root` / ctor / `Root` | `DiContainer` |
| 18 | `LuBanAgentFactory.cs:35,49` | 字段/构造 `IServiceProvider` | `DiContainer` |
| 19 | `Orchestration/Planner/LlmTaskPlanner.cs:26,38` | 字段/构造 `IServiceProvider` | `DiContainer` |
| 20 | `Tools/Orchestration/OrchestrationToolGroup.cs:33,39` | 字段/构造 `IServiceProvider` | `DiContainer` |
| 21 | `LuBanAgentDefaultsExtensions.cs:47` / `LuBanAgentExtensions.cs:230` | `Func<IServiceProvider, IChatClient> chatClientFactory` | `Func<DiContainer, IChatClient>` |

**同步规则（同一任务内必须一起改，否则编译不过）：**
- `_scope.ServiceProvider.GetRequiredService<T>()` → `_scope.GetRequiredService<T>()`；`_scope.ServiceProvider.GetService(t)` → `_scope.GetService(t)`。
- `Services => _scope.ServiceProvider` → `Services => _scope`。
- XML 注释里的 `<see cref="IServiceProvider"/>` 必须一起改，否则 `CS1574`。
- **不要**改第三方/官方只认 `IServiceCollection` 的调用（`AddLogging`、`AddOptions`、`AddHttpClient`、`AddAgent`/`AddA2AAgent`）。

- [ ] **Step 1：先扫出契约面，确认没有漏项**

```powershell
$b='D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent'
Get-ChildItem -Recurse -Filter *.cs $b | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern '(?<![A-Za-z])(IServiceProvider|IServiceScopeFactory|IServiceScope)(?![A-Za-z])' |
  ForEach-Object { "$($_.Path.Replace("$b\",'')):$($_.LineNumber): $($_.Line.Trim())" }
```

期望基数：**33 行**（上表覆盖的契约面 + 19 个 `GetTools` 实现 + 2 个 chatClientFactory + 3 行注释）。扫描结果里若出现上表没有的**签名行**，按同一规则（容器句柄 → `DiContainer`）补进表里再改。

- [ ] **Step 2：改接口与实现（按上表逐条）**

19 个 `GetTools` 实现文件（全部在 `Tools/` 下，签名第一参数一律改 `DiContainer sp`）：

```
Browser/BrowserToolPlugin.cs                ConnectorManage/ConnectorManageToolPlugin.cs
Context/CompactContextToolPlugin.cs         FileSystem/FileSystemToolPlugin.cs
Git/GitToolPlugin.cs                        LocalMemory/LocalMemoryToolPlugin.cs
Log/LogToolPlugin.cs                        McpManage/McpManageToolPlugin.cs
Orchestration/OrchestrationToolPlugin.cs    Retrieval/RetrievalToolPlugin.cs
Script/ScriptToolPlugin.cs                  Skill/SkillToolPlugin.cs
SkillManage/SkillManageToolPlugin.cs        Web/WebToolPlugin.cs
WiKi/WikiToolPlugin.cs                      MCP/MCPToolPlugin.cs
```

（另有 `Abstractions/ILuBanToolPlugin.cs` 接口本身与 `Abstractions/ToolPluginRegistry.cs` 调用侧。）

- [ ] **Step 3：改直接实现（10-21 行）**

重点注意三处语义容易写错：

1. `SessionRuntime`：`_scope` 由 `IServiceScope` 变 `DiContainer` 后，`DisposeAsync` 里原来 `_scope.Dispose()` 保持不变（`DiContainer` 有同步 `Dispose`），**不要**改成 `DisposeAsync().AsTask()` 之外的东西。
2. `SessionRuntimeManager`：`_root.CreateScope()` 现在返回 `DiContainer`，`new SessionRuntime(scope, ...)` 的类型自然对齐；**不要**再写 `scope.ServiceProvider`。
3. `ConnectorFactoryRegistry.ConnectorInstanceHost`：`Services = _root`（`DiContainer` 直接赋给 `IConnector.Services`），`Root` 属性类型跟着改。

- [ ] **Step 4：编译（框架项目整体）**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj -v m 2>&1 | Select-String -Pattern ' error ' | Select-Object -First 30
```

期望：只剩「注册/工厂 lambda 里类型不匹配」这一类错误（Task 8 处理）。若出现 `CS1503 cannot convert from 'Microsoft.Extensions.DependencyInjection.ServiceProvider' to 'LuBan.DI.DiContainer'`，说明有地方把 MS provider 直接往契约里塞，按 §0-10 补 `AsDiContainer()`。

- [ ] **Step 5：检查点**

```powershell
Select-String -Path D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Abstractions\ILuBanToolPlugin.cs,D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Hosting\IAgentHost.cs,D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\Plugins\Contracts\IPluginHost.cs -Pattern 'DiContainer'
```

期望：三个文件都有 `DiContainer`，且都不再出现 `IServiceProvider`。

**验证记录：** ⬜ 未执行

---

## Task 8：`LuBan.AIAgent` 注册/解析改写 + `Add*` 签名换型

**Files（改写重点，按命中数排序）：**

```
LuBanAgentExtensions.cs(82)            LuBanAgentHostExtensions.cs(18)
LuBanAgentDefaultsExtensions.cs(10)    LuBanAgentFactory.cs(9)
Tools/Orchestration/OrchestrationToolGroup.cs(9)   Hosting/AgentHost.cs(8)
Tools/Context/CompactContextToolPlugin.cs(6)       Connectors/ConnectorServiceCollectionExtensions.cs(5)
LuBanAgentOpenAIExtensions.cs(5)       Tools/McpManage/McpManageToolPlugin.cs(4)
Hosting/SessionRuntimeManager.cs(4)    A2A/A2AExtensions.cs(4)
Tools/ConnectorManage/ConnectorManageToolPlugin.cs(4)  ...
```

**机械改写对照（全项目通用）：**

| 现状 | 之后 |
|------|------|
| `services.AddSingleton<Foo>();` | `di.AddSingleton<Foo>();`（实例方法，返回 `DiContainer`） |
| `services.AddScoped<IFoo>(sp => new Foo(sp.GetRequiredService<X>()))` | `services.AddScoped<IFoo>(d => new Foo(d.GetRequiredService<X>()))` |
| `services.AddScoped(typeof(ILuBanToolPlugin), type)` | `di.AddScoped(typeof(ILuBanToolPlugin), type)`（`LuBanAgentExtensions.cs:262`） |
| `services.TryAddSingleton<IAgentUserResolver, DefaultAgentUserResolver>()` | `di.TryAddSingleton<...>()` |
| `services.Replace(ServiceDescriptor.Singleton<IFoo>(sp => ...))` | `di.ReplaceSingleton<IFoo>(d => ...)` |
| `sp.GetRequiredService<IServiceScopeFactory>()`（工厂里创建 scope） | 直接用传进来的 `d`（契约已是 `DiContainer`，`d.CreateScope()`） |
| `services.BuildServiceProvider(...)` | **本任务不应出现**（AIAgent 现状 0 处） |
| `services.AddOptions()` / `AddLogging()` / `AddHttpClient()` / `AddA2AAgent()` | **保持不变**（MS/官方扩展只认 `IServiceCollection`） |

- [ ] **Step 1：改 9 处 `Add*` 扩展签名**

| 文件:行 | 方法 |
|---------|------|
| `LuBanAgentDefaultsExtensions.cs:44,62` | `AddLuBanAgentDefaults`（两个重载） |
| `LuBanAgentExtensions.cs:39,227` | `AddLuBanAgent`（两个重载） |
| `LuBanAgentHostExtensions.cs:53` | `AddLuBanAgentHost` |
| `LuBanAgentOpenAIExtensions.cs:52` | `AddLuBanAgentOpenAI` |
| `A2A/A2AExtensions.cs:36` | `AddLubanA2A` |
| `Connectors/ConnectorServiceCollectionExtensions.cs:41,68` | `AddConnector<TConnector>` / `AddBuiltinConnectors` |

改法（以 `AddLuBanAgentHost` 为例）：

```csharp
public static DiContainer AddLuBanAgentHost(this DiContainer services)
```

同时把 `ArgumentNullException.ThrowIfNull(services)` 保留，`return services;` 保留。

- [ ] **Step 2：在 `AddLuBanAgentDefaults` 里注册 `DiContainer` 自身**

为什么必须先做这一步：契约换型后 `SessionRuntimeManager`、`LuBanAgentFactory`、`PerUserWorkspaceResolver` 等**从 DI 里构造**的服务，构造参数是 `DiContainer`。如果不注册，`ValidateOnBuild = true` 会直接抛「Unable to resolve service for type 'LuBan.DI.DiContainer'」。

在 `AddLuBanAgentDefaults` 的注册段最前面加：

```csharp
// 让所有经 DI 构造的服务都能拿到「当前解析上下文」的容器视图。
// 必须是 Transient：单例服务拿根视图、Scoped 服务拿所在 scope 视图（等价于 MS 注入 IServiceProvider 的语义）。
services.TryAddTransient<DiContainer>(sp => sp.AsDiContainer());
```

> 不要用 `AddSingleton(di)`：那会让 Scoped 服务拿到根视图，`ValidateScopes = true` 下解析 Scoped 依赖直接抛错，且会串用户。

- [ ] **Step 3：按对照表改完全部注册/解析调用点**

建议顺序（每改完一个文件编译一次，避免一次堆几百个错）：
`LuBanAgentDefaultsExtensions` → `LuBanAgentExtensions` → `LuBanAgentHostExtensions` → `LuBanAgentOpenAIExtensions` → `LuBanAgentFactory` → `Hosting/*` → `Plugins/*` → `Tools/*` → `Connectors/*` → `A2A/A2AExtensions`。

- [ ] **Step 4：编译**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj -v m 2>&1 | Select-String -Pattern ' error ' | Select-Object -First 30
```

期望：`Build succeeded`。常见残留错误与处置：

| 错误 | 原因 | 处置 |
|------|------|------|
| `CS1929 'IServiceProvider' does not contain a definition for 'GetRequiredService'` | 某处仍是裸 `IServiceProvider` | 加 `using LuBan.DI;` 并换类型 |
| `CS1503 cannot convert 'Microsoft.Extensions.DependencyInjection.ServiceProvider' to 'DiContainer'` | 把 MS provider 塞给契约 | 该处先 `.AsDiContainer()` |
| `CS0121 ambiguous`（`AddSingleton`） | 实参本身同时匹配多个重载 | 显式指定泛型：`di.AddSingleton<Foo>(foo)` |
| `CS1662 lambda 返回类型不匹配` | 工厂 lambda 用了 `IServiceProvider` 方法 | 改成 `d.GetXxx()` |

- [ ] **Step 5：检查点**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\LuBan.AIAgent.csproj -v m
Select-String -Path D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent\*.cs -Pattern 'this IServiceCollection'
```

期望：构建成功；`*.cs` 根目录下 `this IServiceCollection` 为 **0 条**（子目录里也应为 0，只有第三方扩展调用点）。

**验证记录：** ⬜ 未执行

---

## Task 9：框架内消费方编译修复（`LuBan.Web.Core` / `WebApplication1` / `LuBan.XTestProject`）

**原则：只做编译必需改动，不做风格改造。** 这三处的 `IServiceCollection` 局部变量**保持原样**，只在调用我们 `Add*` 的地方插入 `.AsDiContainer()`。

- [ ] **Step 1：`LuBan.Web.Core`（3 处调用 + 1 处接口实现）**

`LuBan.Web.Core/AIAgent/AIAgentExtension.cs`：

```csharp
// 现状（第 50/53/54 行）
services.AddLuBanAgentDefaults(effectiveConfig);
services.TryAddScoped<IAgentUserResolver, WebAgentUserResolver>();
services.AddLuBanAgentHost();
services.AddLuBanAgentOpenAI(effectiveConfig);

// 之后：Add* 走容器，MS 的 TryAddScoped 仍走集合
var di = services.AsDiContainer();
di.AddLuBanAgentDefaults(effectiveConfig);
services.TryAddScoped<IAgentUserResolver, WebAgentUserResolver>();
di.AddLuBanAgentHost();
di.AddLuBanAgentOpenAI(effectiveConfig);
```

第 59 行的 `services.Replace(ServiceDescriptor.Singleton<IConfiguration>(effectiveConfig));` **保持不动**（MS 扩展，语义等价）。

`LuBan.Web.Core/AIAgent/WebAgentUserResolver.cs:38`：

```csharp
public string ResolveUserId(DiContainer services)
```

（方法体若只调 `services.GetRequiredService<T>()`，无需改；需要 `using LuBan.DI;`，若项目已有 `global using LuBan.DI;` 则不加。）

- [ ] **Step 2：`WebApplication1`**

`WebApplication1/Controllers/AgentController.cs` 里 `HttpContext.RequestServices.GetService<T>()` 改为：

```csharp
HttpContext.RequestServices.AsDiContainer().GetService<T>()
```

（`IServiceProvider` 没有泛型 `GetService<T>()` 扩展在作用域内时也可直接保留 MS 的 `using`；统一改成 `AsDiContainer()` 更符合本次口径。）

- [ ] **Step 3：`LuBan.XTestProject`（固定清单，按行改）**

| 文件:行 | 改法 |
|---------|------|
| `AgentHostRegistrationUnitTest.cs:24,25` | `services.AsDiContainer().AddLuBanAgentDefaults(config)` / `...AddLuBanAgentHost()` |
| `AgentHostRegistrationUnitTest.cs:92` | `scope.ServiceProvider.GetRequiredService<...>()` 保持（scope 来自 MS `CreateScope()`，没换）；若该处 `scope` 改自 `di.CreateScope()` 则直接 `scope.GetRequiredService<...>()` |
| `AgentHostResetUnitTest.cs:176,180` | 同 24/25 |
| `AIAgentUnitTest.cs:138` | `services.AsDiContainer().AddLuBanAgent(config)` |
| `ChatClientBaseUrlWarningUnitTest.cs:30` | `services.AsDiContainer().AddLuBanAgentOpenAI(configuration)` |
| `ConnectorManageToolGroupUnitTest.cs:75` | `services.AsDiContainer().AddConnector<StubConnector>(...)` |
| `ConnectorRegistryUnitTest.cs:116,118,139,280,281,317,381` | 同上 `AddConnector` / `AddBuiltinConnectors` |
| `DefaultsHostUnitTest.cs:31` | `services.AsDiContainer().AddLuBanAgentDefaults(config, _ => new MockChatClient("test"))` |
| `LogDiUnitTest.cs:20,40,53` | `services.AsDiContainer().AddLuBanAgent(configuration)` |
| `PerUserWorkspaceResolverUnitTest.cs:86` | `public string ResolveUserId(DiContainer services) => userId;` |
| `PluginManagerLifecycleUnitTest.cs:618` | `public DiContainer Services { get; set; } = null!;` |
| `ScopedPluginChainUnitTest.cs:28` / `SessionScopeRegistryUnitTest.cs:32` | `services.AsDiContainer().AddLuBanAgent(config, ...)` |
| `WikiUnitTest.cs:45` | `GetTools(DiContainer sp, ToolGroupOptions? toolsOptions = null)` |
| `EditToolUnitTest.cs:235` / `McpResourceToolUnitTest.cs:170` / `RequiredArgumentGuardUnitTest.cs:438` | `StubServiceProvider : IServiceProvider` **保留**（这是被测工具内部用的 provider 替身，不属容器句柄） |

- [ ] **Step 4：构建整个框架 sln**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.FrameWork.sln -v m 2>&1 | Select-String -Pattern ' error ' | Select-Object -First 30
```

期望：`Build succeeded`。若有 `smoke`/未进 sln 的项目报错，留给 Task 18。

- [ ] **Step 5：检查点**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.FrameWork.sln -v m
```

期望输出含 `0 Error(s)`。

**验证记录：** ⬜ 未执行

---

## Task 10：框架侧验收 + 静态扫描

- [x] **Step 1：框架测试（`DiContainer` 单测 + 既有 AIAgent 测试）**

```powershell
dotnet test D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\LuBan.UnitTestProject.csproj -v m
```

期望：新增 `DiContainerUnitTest` 全绿。既有集成测试（Redis / 微信 / 轻流 / SignalR / RagFlow / Office）失败**先判定为环境问题**，记录失败用例名到「验证记录」，不算本任务阻塞。

- [x] **Step 2：静态扫描（6 个改造项目里的框架部分）**

```powershell
$roots = @('D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent')
Get-ChildItem -Recurse -Filter *.cs $roots | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern 'new ServiceCollection|BuildServiceProvider|ServiceDescriptor\.' |
  ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
```

期望：**0 条**（`CompactContextToolPlugin.cs:55` 是注释，若命中就改注释措辞）。

- [x] **Step 3：检查点**

框架侧三件事都成立才算过：sln 构建 0 error、`DiContainerUnitTest` 全绿、扫描 0 条。

**验证记录：** ✅ 已完成（2026-10-30）
- `dotnet build LuBan.FrameWork.sln` → `已成功生成`，0 警告 0 错误。
- `DiContainerUnitTest` 10 个用例全绿（`--filter FullyQualifiedName~DiContainerUnitTest`）。
- 静态扫描 A 组（`new ServiceCollection` / `BuildServiceProvider` / `ServiceDescriptor.`）在 `LuBan.AIAgent` → **0 条**。
- 另有 2 处注释命中 `LuBan.AIAgent` 的 `IServiceProvider` / `IServiceScopeFactory` 措辞，按本 Step 口径改注释（`LuBanAgentExtensions.cs` 注册 `DiContainer` 的 Transient 说明、`Tools/Context/CompactContextToolPlugin.cs` 用会话作用域 ChatClient 的说明），语义未变。
- 附：框架全量测试 1153 例中 68 例失败，均为环境依赖或**改动前既有**失败（以 HEAD worktree 基线对照，见 Task 19 验证记录）。

---

## Task 11：agent 侧引用确认（预期零改动）

**Files：** 无。

`LubanAgentCore` / `LubanAgentA2A` / `LubanAgent.Plugin.NewProject` 已经是 `ProjectReference` 指向本地框架；`LubanAgentCli` / `LubanAgentCodex` / `LubanAgent.Tests` 通过 `ProjectReference` 间接拿到框架。**本任务只做确认，不改文件。**

- [x] **Step 1：确认引用形态**

```powershell
Get-ChildItem D:\WorkBench\Walle\luban\luban-agent -Recurse -Filter *.csproj |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\smoke\\' } |
  Select-String -Pattern 'luban-framework\\\\LuBan' | ForEach-Object { "$($_.Path): $($_.Line.Trim())" }
```

期望：`LubanAgentCore` / `LubanAgentA2A` / `LubanAgent.Plugin.NewProject` 三个 csproj 命中 `ProjectReference`（`LubanAgentCore` 4 条、另外两个各 1 条）。

- [x] **Step 2：确认没有显式 MS.DI 包（避免 NU1605 降级）**

```powershell
Get-ChildItem D:\WorkBench\Walle\luban\luban-agent -Recurse -Filter *.csproj |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern 'PackageReference Include="Microsoft.Extensions.DependencyInjection'
```

期望：0 条（若命中且版本 < 10.0.14，删掉它，交给框架传递；这不是本次新增的问题，但会阻断构建）。

- [x] **Step 3：检查点**

两条扫描都符合期望即可，无文件改动。

**验证记录：** ✅ 已完成（2026-10-30），无文件改动
- `ProjectReference` 命中：`LubanAgentCore` 4 条（`LuBan.AIAgent` / `LuBan.Common` / `LuBan.Logging` / `LuBan.Orm`）、`LubanAgentA2A` 1 条、`LubanAgent.Plugin.NewProject` 1 条 —— 与期望一致。
- 显式 `Microsoft.Extensions.DependencyInjection` 包 → **0 条**。

---

## Task 12：`LubanAgentCore` 改造（19 文件 / 178 处）

**核心策略：** 绝大多数文件只需要「类型换名」——`new ServiceCollection()` → `new DiContainer()`、`IServiceProvider` → `DiContainer`、`IServiceScope` → `DiContainer`、`scope.ServiceProvider` → `scope`。**不要重写文件结构**。

- [ ] **Step 1：`AgentHostBuilder`（最大注册点，106 处）**

三处结构性改动 + 一个改名：

```csharp
// 1) 方法改名（BuildServiceProvider 这个字面量本身也要消失，见 §0 的静态验收）
public static AgentHostServices Build(
    IConfiguration configuration,
    OnnxEmbeddingGenerator? embedder = null,
    ModelManager? modelManager = null,
    Action<DiContainer>? configureServices = null)      // 原来是 Action<IServiceCollection>

// 2) 第 59 行
var services = new DiContainer();                      // 原来是 new ServiceCollection()

// 3) 第 190 行：默认值与原 ServiceProviderOptions 完全一致
var root = services.Build();                           // ValidateScopes=true / ValidateOnBuild=true

// 4) 第 203/205 行保持不变（CreateUserScope 已改为返回 DiContainer）
var userScope = root.CreateUserScope(LocalUserDefaults.DefaultUserId);
return new AgentHostServices(root, userScope);

// 5) 第 211 行
public static async Task InitializeWorkspaceAsync(DiContainer sp, List<string> notices)
```

> 变量名继续叫 `services`（类型 `DiContainer`），这样方法体内 100 多处 `services.AddXxx(...)` **文本不用改**，实例方法会优先于 MS 扩展方法。

- [ ] **Step 2：`AgentHostServices` 改为继承 `DiContainer`（整文件替换类体）**

```csharp
namespace LubanAgentCore.Hosting;

/// <summary>
/// 应用级服务提供者：持有根容器与进程常驻用户作用域，对外解析委托到用户作用域。
/// </summary>
public sealed class AgentHostServices : DiContainer
{
    private readonly DiContainer _root;
    private readonly DiContainer _userScope;
    private int _disposed;

    /// <summary>创建应用级服务提供者。</summary>
    /// <param name="root">根容器。</param>
    /// <param name="userScope">进程常驻用户作用域。</param>
    public AgentHostServices(DiContainer root, DiContainer userScope)
        : base((IServiceProvider)userScope)   // 解析全部转发到用户作用域（必须显式转接口，否则与集合重载二义）
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(userScope);
        _root = root;
        _userScope = userScope;
    }

    /// <summary>根容器（身份无关单例所在）。</summary>
    public DiContainer Root => _root;

    /// <summary>用户作用域容器。</summary>
    public DiContainer UserServices => _userScope;

    /// <inheritdoc />
    public override void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        // 先释放用户作用域，再释放根（与原实现顺序一致；重复释放由各自幂等保证）
        await _userScope.DisposeAsync().ConfigureAwait(false);
        await _root.DisposeAsync().ConfigureAwait(false);
    }
}
```

> 注意：**不要**再调用 `base.DisposeAsync()`（基类会把 `_provider` 当成自己的 provider，虽然 `OwnsProvider = false` 不会误释放，但语义上这里由 `_root` 统一释放更清晰）。

- [ ] **Step 3：`Configuration/UserScopedServiceExtensions.cs` 整文件替换**

```csharp
public static class UserScopedServiceExtensions
{
    /// <summary>解析当前用户标识；缺失时回退为本地默认用户。</summary>
    public static string ResolveUserKey(this DiContainer services)
        => services.GetRequiredService<IUserContext>().ResolveUserKey();

    /// <summary>解析用户标识；缺失时回退为本地默认用户。</summary>
    public static string ResolveUserKey(this IUserContext userContext)
        => userContext.UserId is { Length: > 0 } id ? id : LocalUserDefaults.DefaultUserId;

    /// <summary>
    /// 创建根派生作用域并回填指定用户身份。
    /// <para>MS.DI 的 CreateScope() 永远从根容器派生，不继承调用方作用域的身份状态；
    /// 未回填时按用户隔离的服务会回退到默认用户桶，故根派生 scope 必须显式回填。</para>
    /// </summary>
    public static DiContainer CreateUserScope(this DiContainer services, string? userId)
    {
        ArgumentNullException.ThrowIfNull(services);
        var scope = services.CreateScope();
        scope.GetRequiredService<UserContextState>().UserId =
            string.IsNullOrWhiteSpace(userId) ? LocalUserDefaults.DefaultUserId : userId.Trim();
        return scope;
    }

    /// <summary>解析当前用户的 <see cref="ProviderService"/>。</summary>
    public static ProviderService GetProviderService(this DiContainer services)
        => services.GetRequiredService<ProviderService>();

    /// <summary>解析当前用户的 <see cref="ModelCapabilityService"/>。</summary>
    public static ModelCapabilityService GetModelCapabilityService(this DiContainer services)
        => services.GetRequiredService<ModelCapabilityService>();

    /// <summary>解析当前用户的 <see cref="ModelCapabilityCache"/>。</summary>
    public static ModelCapabilityCache GetModelCapabilityCache(this DiContainer services)
        => services.GetRequiredService<ModelCapabilityCache>();
}
```

> **删掉 `CreateUserScope(this IServiceScopeFactory, ...)` 这个重载**。理由：`LubanAgentCore` 在静态验收范围内，不允许残留 `IServiceScopeFactory`；唯一外部调用点 `LubanAgent.Plugin.NewProject/Services/HostAssistantBridge.cs:186` 在 Task 17 一并改成 `DiContainer`。

- [ ] **Step 4：其余 16 个文件按「类型换名」清单处理**

| 文件:行 | 改动 |
|---------|------|
| `Configuration/UserContextRegistry.cs:56,61` | `IServiceProvider _root` → `DiContainer` |
| `Hosting/CoreSeams.cs:57,70,85,103,113,118` | `IServiceProvider` → `DiContainer`（`CoreAgentProfileResolver` / 两个 `ApplyAsync` / `ResolveUserId` / `VisionModelCapabilityGate`） |
| `Hosting/CoreSessionRuntimeInitializer.cs:32` | ctor 参数 → `DiContainer` |
| `Plugins/AgentPluginHost.cs:89,386,469` | `Services` 属性、`Contribute`、`TryRegisterMcpInSession` → `DiContainer` |
| `Plugins/IPluginSessionContributor.cs:40` | `Contribute(DiContainer sessionServices)` |
| `Plugins/PluginBootstrap.cs:32` | `InitializeAsync(DiContainer services, ...)` |
| `Plugins/PluginHostContext.cs:36,46,54` | 字段/构造/`Root` → `DiContainer` |
| `Plugins/PluginMcpCatalog.cs:48` | 参数 → `DiContainer` |
| `Services/AttachmentService.cs:75,82` | 字段/构造 → `DiContainer` |
| `Services/KnowledgeTaskService.cs:84,99` | `_scopeFactory` 类型 `IServiceScopeFactory` → `DiContainer`（`CreateUserScope` 调用不变） |
| `Connectors/AliyunDevOps/AliyunDevOpsConnectorServiceCollectionExtensions.cs:51,110` | `this IServiceCollection` → `this DiContainer`；`CreateConnector(IServiceProvider)` → `DiContainer` |
| 其余 `scope.ServiceProvider.GetRequiredService<T>()` | `scope.GetRequiredService<T>()` |

- [ ] **Step 5：编译**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore\LubanAgentCore.csproj -v m 2>&1 | Select-String -Pattern ' error ' | Select-Object -First 30
```

期望：`Build succeeded`。高频错误处置：

| 错误 | 处置 |
|------|------|
| `CS1503 cannot convert from 'System.IServiceProvider' to 'LuBan.DI.DiContainer'` | 调用方参数类型没换，或 MS provider 未加 `.AsDiContainer()` |
| `CS1061 'DiContainer' does not contain 'ServiceProvider'` | 把 `scope.ServiceProvider` 改成 `scope` |
| `CS0121 ambiguous between AddSingleton(IServiceCollection,...)` | 显式写泛型并确认实参类型 |
| `CS1503 ... 'ServiceCollection' to 'DiContainer'` | 该处 `new ServiceCollection()` 还没换成 `new DiContainer()` |

- [ ] **Step 6：检查点**

```powershell
Get-ChildItem -Recurse -Filter *.cs D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern 'new ServiceCollection|BuildServiceProvider|ServiceDescriptor\.|(?<![A-Za-z])(IServiceProvider|IServiceScopeFactory|IServiceScope)(?![A-Za-z])'
```

期望：**0 条**（`AgentHostServices.cs` 里的 `(IServiceProvider)userScope` 强制转换是唯一例外，允许保留；如想彻底清零，可改成 `userScope.Provider`——语义等同，二义也消失）。

**验证记录：** ⬜ 未执行

---

## Task 13：`LubanAgentA2A` 改造（5 文件 / 45 处）

- [ ] **Step 1：`LubanAgentA2AExtensions.cs`**

```csharp
public static DiContainer AddLubanAgentA2A(      // 返回类型与 this 参数都换
    this DiContainer services, IConfiguration configuration)
{
    ...
    services.AddLubanA2A(configuration);          // 框架侧已是 DiContainer 版
    services.TryAddSingleton<A2ARuntimeState>();

    services.ReplaceSingleton<IA2AAgentRegistry, InMemoryA2AAgentRegistry>();
    services.ReplaceSingleton<IA2ATransport>(d =>
        new InProcessA2ATransport(d.GetRequiredService<IA2AAgentRegistry>()));
    services.ReplaceSingleton<IA2AServerHost>(d =>
        new A2AServerHost(
            d.GetRequiredService<A2ARuntimeState>(),
            d.GetRequiredService<IA2AAgentRegistry>(),
            d,                                          // 原 d.GetRequiredService<IServiceScopeFactory>()：契约已改 DiContainer
            d.GetService<ILoggerFactory>(),
            d.GetService<ILogger<A2AServerHost>>()));

    services.TryAddSingleton<A2ARuntimeLifecycle>();
    services.TryAddSingleton<IA2ALifecycle>(d => d.GetRequiredService<A2ARuntimeLifecycle>());
    services.ReplaceScoped<ISubAgentExecutor>(d => { ...原有逻辑，sp 改 d... });
    return services;
}
```

- [ ] **Step 2：三个 `IServiceScopeFactory` 字段/构造改 `DiContainer`**

| 文件:行 | 改动 |
|---------|------|
| `InProcess/InternalSubAgentHandlerAdapter.cs:32,41` | `_scopeFactory` → `DiContainer` |
| `Runtime/A2ARuntimeLifecycle.cs:35,59` | 同上 |
| `Server/A2AServerHost.cs:34,52` | `_outerScopeFactory` → `DiContainer` |

- [ ] **Step 3：`scope.ServiceProvider` → `scope`**

```csharp
// InternalSubAgentHandlerAdapter.cs:117-118
using var scope = _scopeFactory.CreateScope();
var services = scope;                       // 原 scope.ServiceProvider
```

其余 `using var scope = _scopeFactory.CreateScope(); ... scope.ServiceProvider.GetX()` 同样处理。

> `A2AServerHost` 内部 `WebApplication.CreateSlimBuilder()` / `builder.Services.AddA2AAgent<...>(...)` / `app.MapA2A(...)` **全部保持原样**——这是第三方 SDK 边界，只认 `IServiceCollection`。

- [ ] **Step 4：编译**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgentA2A\LubanAgentA2A.csproj -v m 2>&1 | Select-String -Pattern ' error ' | Select-Object -First 20
```

期望：`Build succeeded`。

- [ ] **Step 5：检查点**

```powershell
Get-ChildItem -Recurse -Filter *.cs D:\WorkBench\Walle\luban\luban-agent\LubanAgentA2A |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern 'new ServiceCollection|BuildServiceProvider|ServiceDescriptor\.|(?<![A-Za-z])(IServiceScopeFactory|IServiceScope)(?![A-Za-z])'
```

期望：0 条（`IServiceCollection` 允许出现在 `builder.Services` 相关注释/接口边界）。

**验证记录：** ⬜ 未执行

---

## Task 14：`LubanAgentCli` 改造（15 文件 / 83 处）

**先做 3 处改名（静态验收要求 `BuildServiceProvider` 字面量清零）：**

| 现状 | 之后 | 影响调用点 |
|------|------|-----------|
| `AgentHostBuilder.BuildServiceProvider(...)` | `AgentHostBuilder.Build(...)`（Task 12 已改） | `StartupRunner.cs:43`、`App.axaml.cs:84`（Codex，Task 15） |
| `StartupRunner.BuildServiceProvider(...)` → `StartupRunner.BuildHost(...)` | 改名 | `App/StartupDialog.cs:119` |
| `CommandBase.BuildServiceProvider()` → `CommandBase.BuildContainer()` | 改名 + 返回 `DiContainer` | `Commands/SkillCommand.cs:396` |

- [ ] **Step 1：`App/StartupRunner.cs`**

```csharp
public static AgentHostServices BuildHost(                  // 原 BuildServiceProvider
    IConfiguration configuration, OnnxEmbeddingGenerator? embedder, ModelManager? modelManager)
{
    TuiDiag.Enabled = configuration.GetValue<bool>("LuBanAgent:DebugMode");
    return AgentHostBuilder.Build(                          // 原 BuildServiceProvider
        configuration, embedder, modelManager,
        services =>                                          // 形参现在是 DiContainer
        {
            services.AddLubanAgentA2A(configuration);
            services.AddSingleton<TitleService>();
            services.AddSingleton<TuiOrchestrationProgressSink>();
            services.ReplaceSingleton<IOrchestrationProgressSink>(
                d => d.GetRequiredService<TuiOrchestrationProgressSink>());   // 原 services.Replace(ServiceDescriptor...)
        });
}

public static Task InitializeWorkspaceAsync(DiContainer sp, List<string> notices)
    => AgentHostBuilder.InitializeWorkspaceAsync(sp, notices);
```

- [ ] **Step 2：`App/StartupDialog.cs:119`**

```csharp
Services = StartupRunner.BuildHost(config, embedder, modelManager);
```

- [ ] **Step 3：`Commands/CommandBase.cs:82-92`**

```csharp
protected DiContainer BuildContainer()                     // 原 BuildServiceProvider()
{
    var services = new DiContainer();                      // 原 new ServiceCollection()
    services.AddSingleton<IConfiguration>(Configuration);
    services.AddSingleton(ConfigManager);
    services.AddSingleton<IChatClient>(d => ChatClientResilience.Wrap(
        new RequestDumpChatClient(ConfigManager.CreateChatClient()),
        d.GetRequiredService<IOptions<LuBanAgentOptions>>().Value));
    services.AddLuBanAgent(Configuration);
    // 原 BuildServiceProvider() 不校验，这里保持不校验
    return services.Build(validateScopes: false, validateOnBuild: false);
}
```

- [ ] **Step 4：`Commands/SkillCommand.cs:396-406`**

```csharp
using var serviceProvider = BuildContainer();
...
var context = new SkillContext
{
    Agent = agent,
    ServiceProvider = serviceProvider,      // SkillContext.ServiceProvider 已是 DiContainer
    ...
};
```

- [ ] **Step 5：其余 ViewModel / View / Command 按类型换名**

| 文件:行 | 改动 |
|---------|------|
| `App/StartupWorkspaceResolver.cs:34,93,134,185` | `IServiceProvider services` → `DiContainer` |
| `App/StartupWorkspaceResolver.cs:206,208` | `scope.ServiceProvider.GetRequiredService<...>()` → `scope.GetRequiredService<...>()`；`scope.ServiceProvider.GetService<DefaultContextState>()` → `scope.GetService<DefaultContextState>()` |
| `App/ViewModels/CommandViewModel.cs:30,53` | `_services` / ctor → `DiContainer` |
| `App/ViewModels/ConversationViewModel.cs:31,168` | 同上 |
| `App/Views/InputBarView.cs:239,255` | `_services` / `SetServiceProvider(DiContainer services)` |
| `App/Views/RootView.cs:87,124` | 字段/ctor → `DiContainer` |
| `Commands/ModelCommand.cs:44` / `Commands/ProviderCommand.cs:56` | ctor 参数 → `DiContainer` |

> View 层方法名 `SetServiceProvider` 可以保留（不是 `BuildServiceProvider`，扫描不命中）；若要统一改名为 `SetServices` 属风格改动，**不做**。

- [ ] **Step 6：编译**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\LubanAgentCli.csproj -v m 2>&1 | Select-String -Pattern ' error ' | Select-Object -First 30
```

- [ ] **Step 7：检查点**

```powershell
Get-ChildItem -Recurse -Filter *.cs D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern 'new ServiceCollection|BuildServiceProvider|ServiceDescriptor\.|(?<![A-Za-z])(IServiceProvider|IServiceScopeFactory|IServiceScope)(?![A-Za-z])'
```

期望：0 条（`SetServiceProvider` 含 `ServiceProvider` 子串，但正则带 `(?<![A-Za-z])` 前缀且 `Set` 紧邻，不会命中；确认输出为空即可）。

**验证记录：** ⬜ 未执行

---

## Task 15：`LubanAgentCodex` 改造（19 文件 / 87 处）

- [ ] **Step 1：`App.axaml.cs`**

```csharp
// 第 84 行
_services = AgentHostBuilder.Build(configuration, embedder, modelManager, services =>
{
    // 第 91/97/104/106 行：Replace(ServiceDescriptor...) → 实例替换方法
    services.ReplaceSingleton<IOrchestrationProgressSink>(
        d => d.GetRequiredService<AvaloniaOrchestrationProgressSink>());
    services.ReplaceSingleton<IPluginUiDispatcher, AvaloniaPluginUiDispatcher>();
    services.ReplaceSingleton<ICredentialStore>(
        d => new ...（保持原有工厂逻辑，sp 改 d）...);
    services.ReplaceSingleton<IYunxiaoCredentialProvider>(
        d => new ...);
});
```

> 第 147 行注释里提到「注入的 IServiceProvider」属于说明文字，改成 `DiContainer` 让扫描干净。

- [ ] **Step 2：ViewModel / View 按类型换名**

| 文件:行 | 改动 |
|---------|------|
| `Services/FooterDataProvider.cs:25,31` | `IServiceProvider` → `DiContainer` |
| `ViewModels/MainWindowViewModel.cs:211,277` | `public DiContainer Services => _agentHost.Services;` / ctor → `DiContainer` |
| `ViewModels/Settings/McpTabViewModel.cs:21` 等 5 个页签 VM | `(DiContainer? services)` |
| `Views/LlmWikiManageWindow.axaml.cs:25,28,50` | `_services` / `_scope` → `DiContainer` |
| `Views/LlmWikiManageWindow.axaml.cs:55,56` | `_scope.GetService<...>()`（原 `_scope.ServiceProvider.GetService`） |
| `Views/MainWindow.axaml.cs:38,80` | `_services` / `SetServiceProvider(DiContainer)` |
| `Views/MainWindow.axaml.cs:836` | `retrievalScope.GetService<IRetrievalService>()` |
| `Views/MCPManageWindow.axaml.cs:25,40` / `RuleManageWindow.cs:25,40` / `SkillManageWindow.cs:25,40` / `WorkManageWindow.cs:36` / `PluginManageWindow.cs:42` | 重载链上的 `IServiceProvider` → `DiContainer` |
| `Views/SettingsWindow.axaml.cs:40,81` | 同上 |
| `Views/Controls/Sidebar.axaml.cs:31,154,254` | 同上（`EnsurePluginHost(DiContainer services)`） |
| `Views/Controls/InputBox.axaml.cs:91` | `SetServiceProvider(DiContainer services)` |

> Avalonia 的 `DataContext`/XAML 绑定不涉及这些类型，不需要改 `.axaml`。

- [ ] **Step 3：编译**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgentCodex\LubanAgentCodex.csproj -v m 2>&1 | Select-String -Pattern ' error ' | Select-Object -First 30
```

- [ ] **Step 4：检查点**

同 Task 14 Step 7，目录换 `LubanAgentCodex`，期望 0 条。

**验证记录：** ⬜ 未执行

---

## Task 16：`LubanAgent.Tests` 改造（8 文件 / 16 处）

- [ ] **Step 1：`PluginHostUnitTest.cs:30,38`**

```csharp
private DiContainer _root = null!;                     // 原 ServiceProvider

_root = new DiContainer().Build(validateScopes: false, validateOnBuild: false);
```

- [ ] **Step 2：`SettingsViewModelUnitTest.cs:324-339`**

```csharp
private static DiContainer Provider(...)               // 原 IServiceProvider
{
    ...
    return new DiContainer()                           // 原 new ServiceCollection()
        .AddSingleton(...)                             // 原有注册按实例方法/集合扩展原样保留
        .Build(validateScopes: false, validateOnBuild: false);
}
```

> `DiContainer` 的 `Add*` 返回 `DiContainer`，链式写法天然成立；若原链式里用了 MS 专有扩展（如 `AddLogging`、`Configure<T>`），这些是 `IServiceCollection` 扩展，接收 `DiContainer` 后**返回 `IServiceCollection`**，链式会断。处理：拆成语句：

```csharp
var di = new DiContainer();
di.AddLogging(...);
di.AddSingleton(...);
return di.Build(validateScopes: false, validateOnBuild: false);
```

- [ ] **Step 3：三个测试替身改为「包装」写法（不新增 `IServiceProvider` 实现类）**

```csharp
// AttachmentBareImageMentionUnitTest.cs:226 / AttachmentServiceLimitUnitTest.cs:49
private sealed class ThrowingServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => throw new InvalidOperationException("boom");
}

// 使用处（原来是 new ThrowingServiceProvider() 传给框架）
var di = new DiContainer(new ThrowingServiceProvider());
```

```csharp
// McpConnectorManagementUnitTest.cs:220 EmptyServiceProvider 同理
var di = new DiContainer(new EmptyServiceProvider());
```

> 保留替身类本身（它只是 `IServiceProvider` 的桩，不是容器句柄）；外面包一层 `DiContainer` 即可。

- [ ] **Step 4：跑测试**

```powershell
dotnet test D:\WorkBench\Walle\luban\luban-agent\LubanAgent.Tests\LubanAgent.Tests.csproj -v m
```

期望：全绿。

- [ ] **Step 5：检查点**

```powershell
Get-ChildItem -Recurse -Filter *.cs D:\WorkBench\Walle\luban\luban-agent\LubanAgent.Tests |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern 'new ServiceCollection|BuildServiceProvider|ServiceDescriptor\.'
```

期望：0 条（三处 `: IServiceProvider` 桩类允许保留，用上面的类型正则确认时它们会命中——**这类命中属于允许项**，在验证记录里注明即可）。

**验证记录：** ⬜ 未执行

---

## Task 17：`LubanAgent.Plugin.NewProject` 编译修复（6 文件 / 18 处）

**原则：只修到能编译，不重写插件逻辑。** 该工程不在静态验收的 6 个项目里。

- [ ] **Step 1：`Services/HostAssistantBridge.cs:186-188`**

```csharp
// 原来：按接口动态解析 scope 工厂
var factory = _resolve(typeof(IServiceScopeFactory)) as IServiceScopeFactory;
using var scope = factory?.CreateUserScope(LocalUserDefaults.DefaultUserId);
var bootstrap = scope?.ServiceProvider.GetService(typeof(IAgentHost)) as IAgentHost;

// 之后：容器句柄已统一为 DiContainer
var factory = _resolve(typeof(DiContainer)) as DiContainer;
using var scope = factory?.CreateUserScope(LocalUserDefaults.DefaultUserId);
var bootstrap = scope?.GetService(typeof(IAgentHost)) as IAgentHost;
```

> `CreateUserScope(this DiContainer, ...)` 是 `LubanAgentCore.Configuration` 的扩展，文件已有相应 using 即可；若原来专门 `using Microsoft.Extensions.DependencyInjection;` 仅为 `IServiceScopeFactory`，保留也无害（插件工程允许）。

- [ ] **Step 2：其余 4 处的常规处置**

| 位置 | 处置 |
|------|------|
| `Services/NewProjectService.cs:2383,2405,2446,2740`（`_host.Services.GetService(...)`） | **无需改动**——`IPluginHost.Services` 变成 `DiContainer` 后，`GetService(Type)` 实例方法仍存在 |
| `Services/HostAssistantBridge.cs:202`（`runtime.Services.GetService(...)`） | **无需改动**——`SessionRuntime.Services` 变成 `DiContainer`，`GetService(Type)` 仍在 |
| 任何实现 `ILuBanToolPlugin` 的插件工具类（若编译报 `GetTools` 签名不匹配） | 第一参数改 `DiContainer sp`，方法体里 `sp.GetRequiredService<T>()` 通常不用改 |
| 任何实现 `IAgentUserResolver` / `IAgentSessionExtension` / `IPluginSessionContributor` 的类 | 参数 `IServiceProvider` → `DiContainer` |

- [ ] **Step 3：编译**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgent.Plugin.NewProject\LubanAgent.Plugin.NewProject.csproj -v m 2>&1 | Select-String -Pattern ' error ' | Select-Object -First 20
```

期望：`Build succeeded`。

**验证记录：** ⬜ 未执行

---

## Task 18：`smoke/plugin-system` 编译修复

**背景：** 这 4 个工程有意保留 `ProjectReference`（不在 `LubanAgent.slnx` 内），是「改框架源码即时生效」的验证台。

- [ ] **Step 1：`framework-harness/Program.cs:41-43`**

```csharp
using var provider = new DiContainer()
    .AddSingleton<IPluginHost>(testHost)
    .Build(validateScopes: false, validateOnBuild: false);
```

（`PluginManager` 的构造参数已是 `DiContainer`，下游 `new PluginManager(..., provider, ...)` 无需改。）

- [ ] **Step 2：`framework-harness/TestPluginHost.cs:24`**

```csharp
public DiContainer Services { get; set; } = null!;
```

- [ ] **Step 3：编译 4 个工程**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\smoke\plugin-system\framework-harness\Harness.csproj -v m
dotnet build D:\WorkBench\Walle\luban\luban-agent\smoke\plugin-system\framework-plugins\HelloPlugin\HelloPlugin.csproj -v m
dotnet build D:\WorkBench\Walle\luban\luban-agent\smoke\plugin-system\framework-plugins\HelloPluginTimer\HelloPluginTimer.csproj -v m
dotnet build D:\WorkBench\Walle\luban\luban-agent\smoke\plugin-system\codex-plugin\CodexSmokePlugin.csproj -v m
```

期望：4 个都 `Build succeeded`。

- [ ] **Step 4（可选，推荐）：跑一次 smoke**

```powershell
dotnet run --project D:\WorkBench\Walle\luban\luban-agent\smoke\plugin-system\framework-harness\Harness.csproj -- unload
```

期望：进程正常退出（退出码 0）；`/framework-harness/Program.cs` 里自带的断言不报失败。若因缺少插件产物目录失败，属环境问题，记录即可。

**验证记录：** ⬜ 未执行

---

## Task 19：全量验收（构建 / 测试 / 静态扫描）

- [x] **Step 1：两仓库构建**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.FrameWork.sln -v m 2>&1 | Select-String -Pattern 'error|Warning\(s\)|Error\(s\)' | Select-Object -Last 5
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgent.slnx -v m 2>&1 | Select-String -Pattern 'error|Warning\(s\)|Error\(s\)' | Select-Object -Last 5
```

期望：两边都 `0 Error(s)`。**新增警告必须清零**（尤其 `IsTrimmable` 触发的 IL2xxx 与 `CS0618`）。

> **已完成（2026-10-30）**：框架 sln ✅ `已成功生成`（0 警告 0 错误）；agent slnx ✅ `8 个警告 0 个错误`——8 条全部是既有的 `NU1902`/`NU1903`（`System.Security.Cryptography.Pkcs` 8.0.1 已知漏洞，Cli/Codex 各 2 条 + Avalonia `AVLN3001` 等），与本次 DI 改造无关。
> 首次尝试时曾被文件占用阻断（`Microsoft Visual Studio (1484)` + 运行中的 `LubanAgentCodex (27716)` 锁 `LubanAgentCodex/bin/**/*.dll`，`MSB3027`/`MSB3021`）；关闭 Codex 后重跑通过。

- [x] **Step 2：两个测试项目**

```powershell
dotnet test D:\WorkBench\Walle\luban\luban-framework\LuBan.XTestProject\LuBan.UnitTestProject.csproj -v m
dotnet test D:\WorkBench\Walle\luban\luban-agent\LubanAgent.Tests\LubanAgent.Tests.csproj -v m
```

期望：`DiContainerUnitTest` 全绿 + `LubanAgent.Tests` 全绿。框架侧集成测试失败按环境问题记录（用例名 + 缺哪个外部服务）。

- [x] **Step 3：静态扫描（验收口径 §9.8）**

```powershell
$roots = @(
  'D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent',
  'D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore',
  'D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli',
  'D:\WorkBench\Walle\luban\luban-agent\LubanAgentCodex',
  'D:\WorkBench\Walle\luban\luban-agent\LubanAgentA2A',
  'D:\WorkBench\Walle\luban\luban-agent\LubanAgent.Tests'
)
Get-ChildItem -Recurse -Filter *.cs $roots | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern 'new ServiceCollection|BuildServiceProvider|ServiceDescriptor\.' |
  ForEach-Object { "A|$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }

Get-ChildItem -Recurse -Filter *.cs $roots | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern '(?<![A-Za-z])(IServiceProvider|IServiceScopeFactory|IServiceScope)(?![A-Za-z])' |
  ForEach-Object { "B|$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
```

期望：
- A 组：**0 条**。
- B 组：只剩**允许项**，逐条记录到验收结论里，允许项仅限：
  1. `LubanAgent.Tests` 的三个测试替身桩（`ThrowingServiceProvider` ×2、`EmptyServiceProvider`）——它们是被包装进 `DiContainer` 的桩，不是容器句柄；
  2. `LubanAgentCore/Hosting/AgentHostServices.cs` 的 `base((IServiceProvider)userScope)` 强制转换（若按 Task 12 注改用 `userScope.Provider` 则本条也不存在）；
  3. `IServiceCollection` 出现在 `AddLogging` / `builder.Services` / `AddA2AAgent` 边界（这一组不被 B 正则匹配，仅作说明）。

- [x] **Step 4：确认「零遗留」**

```powershell
Get-ChildItem -Recurse -Filter *.cs D:\WorkBench\Walle\luban\luban-framework\LuBan.DI |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  Select-String -Pattern 'static IServiceCollection|static IServiceProvider'
```

期望：0 条（`ServiceProviderUtil` 里不再有静态字段，全局状态只有 `DiContainer._current` 一个）。

- [x] **Step 5：检查点**

四条全过才算 Task 19 完成；把 A 组/B 组原始输出与测试汇总行贴进「验证记录」。

**验证记录：** ✅ 已完成（2026-10-30）

**Step 3 静态扫描**（6 个根目录，`\\obj\\`/`\\bin\\` 已排除）
- A 组 `new ServiceCollection|BuildServiceProvider|ServiceDescriptor\.`：**0 条**。
- B 组 `IServiceProvider|IServiceScopeFactory|IServiceScope` 裸类型：**4 条，全部是允许项**
  1. `luban-agent/LubanAgentCore/Hosting/AgentHostServices.cs:32`：`base((IServiceProvider)userScope)` 强转（Step 3 允许项 2）
  2. `luban-agent/LubanAgent.Tests/AttachmentBareImageMentionUnitTest.cs:227`：`ThrowingServiceProvider` 测试桩（允许项 1）
  3. `luban-agent/LubanAgent.Tests/AttachmentServiceLimitUnitTest.cs:50`：同上
  4. `luban-agent/LubanAgent.Tests/McpConnectorManagementUnitTest.cs:221`：`EmptyServiceProvider` 测试桩（允许项 1）

**Step 4 零遗留**
- `LuBan.DI/**/*.cs` 中 `static IServiceCollection|static IServiceProvider` 仅 1 条：`ServiceDescriptorUtil.cs:178` 的扩展方法签名 `AutoInjectAllCustomerServices(this IServiceCollection services)`，**不是静态字段**；`static IServiceProvider` 0 条。全局状态只剩 `DiContainer._current`。

**Step 1 构建**
- 框架：`dotnet build LuBan.FrameWork.sln` → `已成功生成`，0 警告 0 错误。
- agent：`dotnet build LubanAgent.slnx --no-restore` → `8 个警告 0 个错误`（3.99s）。8 条警告全部是既有的 `NU1902`/`NU1903`（`System.Security.Cryptography.Pkcs` 8.0.1 已知漏洞，`LubanAgentCli` / `LubanAgentCodex`）与 Avalonia `AVLN3001` 等，与本次改造无关，未新增。
- 首次尝试时曾被文件占用阻断（`Microsoft Visual Studio (1484)` + 运行中的 `LubanAgentCodex (27716)` 锁 `LubanAgentCodex/bin/**/*.dll`，`MSB3027`/`MSB3021`）；关闭 Codex 后重跑通过。

**Step 2 测试**
- 框架 `LuBan.UnitTestProject`：1153 例，失败 68 / 通过 1085。**用 HEAD 基线 worktree 对照：基线 1141 例、失败 68，失败集合与当前完全一致（`Compare-Object` 双向无差异）** → 68 例均为改动前既有失败或环境依赖，本次改造零回归。分类：Redis 连接失败 23（`RedisUnitTest.Initialize`）、轻流 Qingflow 11、数据库仓储 `BaseRepositoryUnitTest` 9、`EventBusUnitTest` 5（静态订阅计数互相污染）、企业微信 3（未配置开发者应用）、其余零散（CodeGenerator / RegionSeed / RagFlow / SignalR / Sms / Config / Currency / Model / DynamicReport / VideoTool / `AIAgentUnitTest.TestDependencyInjection` / `DefaultsHostUnitTest.Defaults_FactoryCreateAsync_DoesNotThrow`）。
- `DiContainerUnitTest` 10 例全绿。
- `LubanAgent.Tests`：**373 例全部通过（失败 0）**。构建/测试走 `-p:BaseOutputPath=luban-agent\.verify-bin\ -p:PluginPackageDir=%TEMP%\luban-plugin-stage\` 绕开 Codex 输出目录占用。

**环境绕行说明（供复现）：** agent 侧输出目录被占用时，用 `-p:BaseOutputPath=<仓库内路径> -p:PluginPackageDir=<临时目录>` 构建；`BaseOutputPath` **不能**指到仓库外——`NewProjectKnowledgeAssetsUnitTest` 靠从程序集目录向上找 `LubanAgent.slnx` 定位插件目录，设到 `%TEMP%` 会让 8 个用例报「找不到插件目录」（已验证，属测试基础设施限制，不是代码问题）。

**验证记录（2026-10-30 追加：按仓库文件规范重整 `LuBan.DI` 后复验）**

用户反馈「`DiContainer.Scope.cs` / `DiContainer.Adapter.cs` 的命名与内容不符合规范，应一个对象一个文件；注释格式参考 `IScoped.cs`」，因此做了纯结构调整，**无行为变更**：

- `DiContainer.Scope.cs` + `DiContainer.Adapter.cs` **删除**，全部成员（构建 / 重建 / 作用域 / 释放 / `IServiceScopeFactory` 与 `IServiceCollection` 显式实现 / 内部注册原语）并入 `DiContainer.cs`；`partial` 修饰一并去掉（框架内除 `LuBan.CloudStorage/FileHandler.cs` 与 `WebApplication1` 演示外无 `partial` 惯例，`LuBan.DI` 内为新引入）。
- 新增 `DiContainerMode.cs`（`internal enum`，原 `DiContainer.cs` 内嵌枚举）与 `DiContainerScope.cs`（`internal sealed class DiContainerScope : IServiceScope`，原 `DiContainer.Adapter.cs` 内嵌类）。
- 4 个文件补齐与 `IScoped.cs` 同格式的 ASCII 文件头（Copyright / CLR版本 / 机器名称 / 公司名称 / 命名空间 / 文件名 / 版本号 / 唯一标识 / 当前的用户域 / 创建人 / 电子邮箱 / 创建时间 / 描述 + 修改标记），`/// <summary>` 全部改为多行式（`LuBan.DI` 内单行 `/// <summary>...</summary>` 现为 0 条），索引器补充 `<value>` 标签。

复验结果：

- `dotnet build LuBan.FrameWork.sln` → `已成功生成`，0 警告 0 错误。
- `dotnet build LubanAgent.slnx` → 12 个警告 0 个错误；逐条核对，全部是 `System.Security.Cryptography.Pkcs` 8.0.1 的 `NU1902`/`NU1903`（`LubanAgentCore` / `LubanAgentA2A` / `LubanAgentCli` / `LubanAgentCodex`），**无任何指向 `LuBan.DI` 的警告**。
- `DiContainerUnitTest` 10/10 通过；框架全量 `1153 例 / 失败 68 / 通过 1085`，与上面 HEAD 基线对照的 68 例失败集合一致 → 结构重整零回归。

---

## Task 20：手动冒烟（必须真人执行，agent 只记录结果）

**环境准备：**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\LubanAgentCli.csproj -v m
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgentCodex\LubanAgentCodex.csproj -v m
```

### 20.1 `LubanAgentCli`

```powershell
dotnet run --project D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\LubanAgentCli.csproj
```

- [ ] **S1 启动**：进 TUI，无异常堆栈、无「容器尚未构建」提示
- [ ] **S2 工作区**：自动创建或进入工作区，状态栏显示工作区名
- [ ] **S3 首轮对话（含工具调用）**：发一条会触发工具的消息（如「列出当前目录文件」），能看到工具调用卡片 + 最终回复
- [ ] **S4 切会话**：新建第二个会话 → 回切第一个 → 历史与上下文都在，两个会话互不串
- [ ] **S5 `/skill`**：列表能出来；执行一个 skill 不报错
- [ ] **S6 `/mcp`**：列表能出来；增/删一条 MCP 配置后重启仍在
- [ ] **S7 `/model`**：切换模型成功，后续对话走新模型
- [ ] **S8 退出**：`/exit` 或 Ctrl+Q，进程退出码 0，无 `ObjectDisposedException` / `InvalidOperationException: 容器尚未构建`

### 20.2 `LubanAgentCodex`

```powershell
dotnet run --project D:\WorkBench\Walle\luban\luban-agent\LubanAgentCodex\LubanAgentCodex.csproj
```

- [ ] **C1 主窗口**：启动无异常，会话列表能加载
- [ ] **C2 会话**：新建会话 → 发消息 → 有流式回复
- [ ] **C3 设置窗口各页签**：技能 / 规则 / MCP / 知识库 / 云效 五个页签都能打开且能读到数据（**重点看云效页签**，它走 `ICredentialStore` / `IYunxiaoCredentialProvider` 的 `ReplaceSingleton` 替换）
- [ ] **C4 插件管理窗口**：列表能出来，能启用/停用
- [ ] **C5 切换工作区**：切换后会话列表刷新，后续对话写入新工作区
- [ ] **C6 退出**：关主窗口，进程退出码 0，无释放异常

**判定：** 任何一项失败 = 本次改造不通过，回到对应 Task 排查（优先怀疑 scope 语义与 `Replace` 语义）。

**验证记录：** ⬜ 未执行（等待真人冒烟）

---

## Task 21：发包与切回 `PackageReference`（**用户人工执行**）

**前置：** Task 19 / 20 全绿。

- [ ] **Step 1：提升框架包版本（用户人工）**

| 项目 | 现值 | 目标值 |
|------|------|--------|
| `LuBan.DI/LuBan.DI.csproj` | `2026.10.29.1` | `2026.10.30.1` |
| `LuBan.AIAgent/LuBan.AIAgent.csproj` | `2026.10.29.1` | `2026.10.30.1` |
| `LuBan.Web.Core/LuBan.Web.Core.csproj` | 按其现值 | 同日递增一位 |

> **破坏性变更提示（2026-10-30 追加）**：本次 `LuBan.DI` 删除了 `ServiceProviderUtil`。上调版本时**必须**同时把 `LuBan.DI.csproj` 的 `<PackageReleaseNotes>` 从现有的一行描述改为迁移说明，否则仓库外用户升级后只会看到编译失败：
> `ServiceProviderUtil 已删除，改用 DiContainer.Instance 上的同名成员；BuildProvider()/BuildProvider(services) → DiContainer.Instance.Rebuild()；InitServiceProvider(services) → DiContainer.Instance.ResetServices(services)；AutoInjectAllCustomerServices 改为 IServiceCollection 扩展（调用点不变）。未构建时 GetService 返回 null、GetRequiredService 抛异常。`

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.FrameWork.sln -v m
# nupkg 落在各项目 bin\Debug\net10.0\ 下，由用户推到私有源 nexus3
```

- [ ] **Step 2：agent 侧切回 `PackageReference`（用户人工）**

`LubanAgentCore.csproj`：

```xml
<ItemGroup>
  <PackageReference Include="LuBan.AIAgent" Version="2026.10.30.1" />
  <PackageReference Include="LuBan.Common" Version="2026.10.27.1" />
  <PackageReference Include="LuBan.Logging" Version="2026.10.27.1" />
  <PackageReference Include="LuBan.Orm" Version="2026.10.27.1" />
  <!-- 本次新增：产品代码直接用 DiContainer，显式声明便于版本对齐 -->
  <PackageReference Include="LuBan.DI" Version="2026.10.30.1" />
</ItemGroup>
```

`LubanAgentA2A.csproj` / `LubanAgent.Plugin.NewProject.csproj`：把 `TEMP-LOCAL-VERIFY` 的 `ProjectReference` 段换回

```xml
<PackageReference Include="LuBan.AIAgent" Version="2026.10.30.1" />
```

**注意：** 切回后 `luBan-agent` 不再编译框架源码，之后任何框架改动都要重新发包。

- [ ] **Step 3：还原后复验**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgent.slnx -v m
dotnet test D:\WorkBench\Walle\luban\luban-agent\LubanAgent.Tests\LubanAgent.Tests.csproj -v m
```

期望：`0 Error(s)`、测试全绿；**特别关注 `NU1605`**（包降级）。若出现，按 AGENTS.md 的 F2 口径删掉 agent 侧显式声明的低版本 `Microsoft.Extensions.*`，交给 `LuBan.*` 传递。

- [ ] **Step 4：再冒烟一次**

重复 Task 20 的 S1-S8 / C1-C6（包引用形态与 ProjectReference 形态在运行时行为应完全一致；不一致说明包版本内容不对）。

**验证记录：** ⬜ 未执行（用户人工）

---

## 附录 A：常见错误 → 处置速查

| 现象 | 根因 | 处置 |
|------|------|------|
| `Unable to resolve service for type 'LuBan.DI.DiContainer'` | 没注册容器自身 | Task 8 Step 2 的 `TryAddTransient<DiContainer>(sp => sp.AsDiContainer())` 缺失 |
| `Cannot resolve scoped service ... from root provider` | 把 `DiContainer` 注册成 Singleton，Scoped 服务拿到根视图 | 必须 `TryAddTransient` |
| `InvalidCastException` 在工厂 lambda 里 | 写了 `(DiContainer)sp` | 改 `sp.AsDiContainer()`（或直接用我们的工厂重载） |
| `容器尚未构建` 在启动时抛 | 某处 `DiContainer` 没 `Build()` 就被解析 | 检查 `AsDiContainer()` 的集合视图是否被当根用；集合视图不可 Build |
| 单例出现两份（同一注册构造两次） | 同时 `Build()` 了宿主根 + 又对同一集合 `BuildServiceProvider()` | 全项目只允许 `di.Build()`；`BuildServiceProvider()` 已从 6 个项目清零 |
| 会话串用户 | `CreateScope()` 不是从根派生 | 确认 `DiContainer.CreateScope()` 用 `_root ?? this`；`AgentHostServices` 的基类是 `userScope` 视图 |
| `CS0121 ambiguous`（`AddSingleton`） | 实参同时匹配 instance 与 factory 重载 | 显式泛型 + 显式 lambda 变量类型 |
| `CS1061 'DiContainer' does not contain 'ServiceProvider'` | `scope.ServiceProvider` 残留 | 改 `scope` |
| `CS1929 'IServiceProvider' does not contain 'GetRequiredService'` | 缺少 `using LuBan.DI;`（`DiContainer` 是实例方法，不依赖 MS 扩展） | 加 using 并换类型 |
| `NU1605` 包降级 | agent 侧显式声明了低版本 `Microsoft.Extensions.*` | 删显式声明（AGENTS.md F2 口径） |
| 插件加载后 `MissingMethodException`（`GetTools`） | 旧插件二进制未重编译 | 按用户决策：插件随框架重编译重发，不保兼容 |

## 附录 B：本次明确不做的事

- 不把显式注册改成 `InjectionAttribute` 自动扫描注册。
- 不动 `IOptions<T>` / `IConfiguration` / `ILoggingBuilder` 的用法。
- 不修 `LuBan.Web.Core/Utils/StartupUtil.cs` 自建 provider 与 ASP.NET `app.Services` 并存的既有问题（`WebApplication1` 不纳入运行验收）。
- 不做 `SetServiceProvider` 之类方法改名、不做无关格式化、不做顺手清理。
- 不为外部插件保留二进制兼容。
- 不在本计划内执行任何 git 提交。

## 附录 C：回滚方式

改动全部叠加在工作区、无提交，所以回滚 = 用户 `git -C <repo> checkout -- .`（会连带丢弃用户原有 WIP，**执行前必须由用户确认**）。

更安全的局部回滚顺序（反向）：

1. 还原 agent 侧 6 个项目的改动（`git -C luban-agent checkout -- LubanAgentCore LubanAgentCli LubanAgentCodex LubanAgentA2A LubanAgent.Tests LubanAgent.Plugin.NewProject smoke`）；
2. 还原框架消费方（`LuBan.Web.Core` / `WebApplication1` / `LuBan.XTestProject`）；
3. 还原 `LuBan.AIAgent`；
4. 删除 `LuBan.DI/DiContainer*.cs`、`DiContainerExtensions.cs`，还原 `ServiceProviderUtil.cs` 与 csproj。

## 附录 D：任务完成度自检

全部任务完成后逐条确认：

- [ ] `LuBan.DI`：`DiContainer` + 适配扩展 + `ServiceProviderUtil` 折叠 + 10 个单测 + 中英文 README
- [ ] `LuBan.AIAgent`：契约 21 项换型 + 9 个 `Add*` 签名 + 容器自身注册 + 注册/解析改写
- [ ] 框架 sln 0 error、`LuBan.UnitTestProject` 全绿
- [ ] agent 6 项目改造完成、`LubanAgent.slnx` 0 error、`LubanAgent.Tests` 全绿
- [ ] 2 个编译修复工程（`Plugin.NewProject`、`smoke`）能编译
- [ ] 静态扫描 A 组 0 条、B 组只剩允许项
- [ ] Cli 冒烟 S1-S8、Codex 冒烟 C1-C6 全部通过
- [ ] （用户）发包 + 切回 `PackageReference` 后复验通过
