# AOT 兼容性改造设计（修订版）

**日期**: 2026-09-11
**状态**: 审阅中
**范围**: luban-agent（LubanAgentCli / LubanAgentCore）为主；luban-framework 本轮零改动，仅规定后续触发条件

## 1. 目标

为未来 Native AOT 单文件发布提前清理 **agent 自身源码中**可控的反射/动态代码障碍。本轮不追求三个项目实际发布为 AOT，**不改动任何 `.csproj` 的 `PublishAot`/`PublishSingleFile` 配置**，非 AOT 构建行为保持完全不变。

## 2. 关键技术结论（修订旧方案的依据）

### 2.1 NuGet 包内代码不受消费端编译常量影响

agent 通过 **NuGet 包**（`LuBan.* 2026.9.10.2`）引用 framework，无 ProjectReference。消费者 `dotnet publish -p:DefineConstants=PUBLISH_AOT` 时，包内 DLL 是已编译产物，其中的 `#if !PUBLISH_AOT` **永远不会被触发**。

因此：

- ~~framework 源文件用 `#if !PUBLISH_AOT` 排除 Emit 代码~~ → 对 agent 的 AOT 发布**完全无效**，废弃。
- framework 侧正确手段是 AOT 分析特性（`[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`/`[DynamicallyAccessedMembers]`），随包编译进元数据，供消费端分析器读取。
- `#if !PUBLISH_AOT` 条件编译**只对 agent 仓库内直接参与编译的源码有效**。

### 2.2 Emit 在 Native AOT 下的真实行为

- `Reflection.Emit`（`DefineDynamicAssembly`/`DynamicMethod`/`ILGenerator`）在 Native AOT 下可编译通过，仅运行时抛 `PlatformNotSupportedException`；分析器以 **IL3050**（RequiresDynamicCode）告警。
- 从入口不可达的代码，AOT 编译器做可达性分析后不会纳入原生镜像。
- 已核实 LuBan.DI 的 Emit 代理链对 CLI 不可达：全仓库无 `: AspectDispatchProxy` 子类、无 `InjectionAttribute.Proxy` 设置、CLI 不调用 `AutoInjectAllCustomerServices`（在 `AgentHostBuilder` 手动注册）。
- 该链现有标注已完整：`AspectDispatchProxy.Create`、`ServiceDescriptorUtil.AddDispatchProxy/Register/RegisterService/AutoInjectAllCustomerServices` 均有 RUC；`ProxyBuilder`/`ProxyAssembly`/`AspectDispatchProxyGenerator` 有 `[UnconditionalSuppressMessage]` 配对说明。
- `FastILUtil`（方法级 RUC）/`DynamicUtil`（类级 RUC）同理；其调用方 `ReflectionUtil` 等是否需要补注，取决于告警实测，不预先改动。

**结论：framework 本轮零改动、零重新打包。**

### 2.3 反射分类与处置原则

| 类别 | AOT 手段 | 本轮处置 |
|------|----------|----------|
| 反射读取（`GetProperty`/`Invoke`/`Activator.CreateInstance`） | DAM 注解或源生成器 | agent 侧能换成直接调用/源生成的直接换 |
| 反射生成（Reflection.Emit） | `[RequiresDynamicCode]`（IL3050），无法静态救活 | 不可达代码靠裁剪丢弃；可达且非核心的用 `#if` 隔离 |

## 3. 范围外（明确搁置）

| 障碍 | 原因 |
|------|------|
| SqlSugarCore 5.1.4.220 | AOT 兼容性需实际试编译/运行实测后再定对策 |
| Avalonia 12.x（LubanAgentCodex） | 等官方 Native AOT 支持 |
| WebApplication1 | 依赖 LuBan.Service 的 Job 程序集扫描，复杂度高，先不动 |
| framework 预加 DAM 注解 | 无告警驱动的预标注容易错标漏标；改为试编译出现 IL2xxx/IL3050 后定点补 |
| 三项目 `.csproj` 发布配置 | 本轮不启用 AOT 发布，保持原样 |

## 4. Agent 层改动（luban-agent 仓库）

### 4.1 BertTokenizer 反射 → 直接调用静态工厂

**文件**: `LubanAgentCore/Retrieval/OnnxEmbeddingGenerator.cs`（`EnsureLoaded`，54–63 行）

现状用 `typeof(Tokenizer).Assembly.GetType("Microsoft.ML.Tokenizers.BertTokenizer")` + `GetMethod("Create").Invoke` 反射创建。
已核实（Microsoft.ML.Tokenizers 2.0.0 元数据）：`BertTokenizer` 是 **public sealed 类，无公开构造函数**，工厂为 `public static BertTokenizer Create(string vocabFile, BertOptions options)`。

改为：

```csharp
_tokenizer = BertTokenizer.Create(vocabTxtPath, options);
```

删除类型探测与 `NotSupportedException` 反射分支（`vocabTxtPath`/`options` 逻辑不变）。

### 4.2 System.Text.Json 源生成上下文

**新增**: `LubanAgentCore/Configuration/ConfigJsonContext.cs`

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LubanAgentCore.Configuration;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(ProviderConfig))]
[JsonSerializable(typeof(CustomSkillConfig))]
[JsonSerializable(typeof(CustomRuleConfig))]
[JsonSerializable(typeof(McpServerConfig))]
internal partial class ConfigJsonContext : JsonSerializerContext
{
}
```

`Encoder` 不能通过 `[JsonSourceGenerationOptions]` 设置（该特性无此参数，CS0246/编译错误，已实测）。中文不转义需要单独的 options 拷贝（实测拷贝后 `TypeInfoResolver` 仍指向源生成上下文，AOT 有效），放在 `ConfigManager` 同文件或上下文文件的静态 holder：

```csharp
internal static class ConfigJsonOptions
{
    private static JsonSerializerOptions? _pretty;
    public static JsonSerializerOptions Pretty => _pretty ??= new JsonSerializerOptions(ConfigJsonContext.Default.Options)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
```

说明：

- `CustomSkillConfig`/`CustomRuleConfig`/`McpServerConfig` 来自 `LuBan.AIAgent` NuGet 包，类型均为 public 普通 POCO（已核实属性列表，无字典/多态/自定义转换器），源生成可覆盖。
- 选项与 framework `SerializeUtil.ToJson(defaultVal:true, nullValue:false)` 的实际输出对齐：`WhenWritingNull`（不输出 null 属性）+ 缩进 + 非 ASCII 不转义（`ConfigJsonOptions.Pretty`），保证 config.json 磁盘格式不变。已用最小 net10 工程实测：null 字段省略、中文原样输出、往返反序列化正常。
- 这些配置类型无 `DateTime` 成员，不需要 framework 的 `DateTimeJsonConverter`。
- 注意：不能在 `ConfigJsonContext` 自身加访问 `Default.Options` 的静态字段（静态构造期 `Default` 尚未初始化，会 NRE，已实测），holder 必须是独立类型并惰性初始化。

**改动点**:

1. `ConfigManager.cs`
   - 111 行：`JsonSerializer.Deserialize<AppConfig>(json)` → `JsonSerializer.Deserialize(json, typeof(AppConfig), ConfigJsonOptions.Pretty)`。读写统一用 Pretty（`WhenWritingNull` 对读取无影响；实测拷贝 options 后 resolver 仍是源生成上下文）。
   - 144 行 `Save()`：`_config.ToJson(hasIndentation: true)` → `JsonSerializer.Serialize(_config, typeof(AppConfig), ConfigJsonOptions.Pretty)`。
2. `WorkspaceManager.cs` 684 行（写内置规则参考 JSON）：`config.ToJson(hasIndentation: true)` → `JsonSerializer.Serialize(config, typeof(CustomRuleConfig), ConfigJsonOptions.Pretty)`。此处虽不影响 AOT 正确性，但同为 RUC 调用点且类型已在上下文内，一并消除，保证 Core 程序集无残留 JSON 反射告警。

### 4.3 FastInputBootstrapper 条件编译隔离

该类纯反射 Terminal.Gui 私有字段（`_input`/`_inputTask`/`Coordinator`）并 `Activator.CreateInstance` 内部类型 `NetInput`，属第三方内部实现细节，无法做 DAM/源生成，且仅用于降低输入延迟（非核心功能）。

**文件与调用点闭包**（共 2 文件 4 处）：

| 文件 | 位置 | 处理 |
|------|------|------|
| `LubanAgentCli/Infrastructure/FastInputBootstrapper.cs` | 整个文件 | 文件级 `#if !PUBLISH_AOT ... #endif` |
| `LubanAgentCli/App/TerminalGuiApp.cs` | 34 行字段 `private FastInputBootstrapper? _fastInput;` | `#if !PUBLISH_AOT` 包裹声明 |
| `LubanAgentCli/App/TerminalGuiApp.cs` | 59–60 行 `Dispose` 中调用 | `#if !PUBLISH_AOT` 包裹 |
| `LubanAgentCli/App/TerminalGuiApp.cs` | 124–126 行 `Run` 中创建与 `TryEnable`/日志 | `#if !PUBLISH_AOT` 包裹；AOT 分支无操作（不补日志，避免噪音），`fastInputOk` 变量一并收进条件块 |

`PUBLISH_AOT` 当前无任何地方定义，正常构建行为零变化。

## 5. Framework 后续策略（本轮不实施）

未来真正执行 CLI 的 AOT 试编译（`dotnet publish -p:PublishAot=true ...`）后：

1. 收集告警，仅处理**从 CLI 入口可达**调用链上的 IL2xxx/IL3050；
2. framework 侧定点补 `[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`/`[DAM]`，agent 侧对应调用点要么注解传播、要么换实现；
3. 不可达死代码（DI Emit 代理等）不加处理，依赖裁剪；
4. framework 任何改动需升 `<Version>`（日期制）、重新打包发 NuGet，agent 升级包版本后才生效；两仓库分别提交。

禁止再用"`#if PUBLISH_AOT` 排除 NuGet 包内文件"的思路。

## 6. 验证

1. `dotnet build luban-agent/luban-agent.slnx` 通过（未定义宏，全部代码照常编译）。
2. 手动冒烟：CLI 启动 TUI、配置加载/保存（确认 config.json 格式与改造前一致：null 字段不输出、缩进、中文不转义）、嵌入模型加载（BertTokenizer 路径实际跑通）。参考 `LubanAgentCli/docs/TUI-SMOKE-TEST.md`。
3. framework 本轮不动，无需构建/打包；`dotnet build luban-framework/LuBan.FrameWork.sln` 不受影响（可选回归）。
4. 本轮**不执行** AOT 试编译（SqlSugar 等未清，预期大量告警）；试编译属第 5 节后续阶段。

## 7. 提交

- 仅改动 luban-agent 一个仓库（framework 本轮零改动）。
- 建议信息：`refactor: AOT 兼容性预备 — BertTokenizer 去反射、配置 JSON 源生成、FastInput 条件隔离`
- 不主动提交，待用户确认后执行。
