# AOT 兼容性预备改造 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 清除 luban-agent 自身源码中三处可控的反射障碍（BertTokenizer 反射创建、配置 JSON 反射序列化、Terminal.Gui 私有反射），为未来 Native AOT 发布铺路，且非 AOT 构建行为零变化。

**Architecture:** 三项互相独立的改动，均在 luban-agent 单一 git 仓库内：(1) 反射调用换成已核实的公开 API；(2) System.Text.Json 源生成上下文 + options holder 替换 RUC 扩展方法；(3) 纯反射的非核心组件用 `#if !PUBLISH_AOT` 条件隔离。framework（NuGet 包）本轮零改动。

**Tech Stack:** .NET 10 SDK（global.json 锁 10.0.100）、C#、System.Text.Json 源生成器、Microsoft.ML.Tokenizers 2.0.0、Terminal.Gui 2.6.0-develop.61。

**测试现实（重要）:** luban-agent 无自动化测试项目（见 AGENTS.md）。本计划的自动验证手段 = `dotnet build` 绿灯 + Task 2 的临时控制台往返验证（验证后删除，不入库）+ Task 3 的常量编译/产物字节检查；功能正确性最终以 `LubanAgentCli/docs/TUI-SMOKE-TEST.md` 手动冒烟为准。

**Git 约定:** 仓库根 `D:\WorkBench\Walle\luban` 不是 git 仓库，所有 git 命令必须在 `D:\WorkBench\Walle\luban\luban-agent` 内执行。PowerShell 下用 `git -C D:\WorkBench\Walle\luban\luban-agent ...`。

---

## File Structure

| 文件 | 动作 | 职责 |
|------|------|------|
| `luban-agent/LubanAgentCore/Retrieval/OnnxEmbeddingGenerator.cs` | 修改（54–63 行） | 删除 BertTokenizer 反射探测，改公开静态工厂 |
| `luban-agent/LubanAgentCore/Configuration/ConfigJsonContext.cs` | 新增 | JSON 源生成上下文（5 个配置类型）+ AOT 友好的 Pretty options holder |
| `luban-agent/LubanAgentCore/Configuration/ConfigManager.cs` | 修改（111、144 行） | 两处反射 JSON 调用改走源生成 options |
| `luban-agent/LubanAgentCore/Services/WorkspaceManager.cs` | 修改（684 行） | 内置规则参考 JSON 改走源生成 options |
| `luban-agent/LubanAgentCli/Infrastructure/FastInputBootstrapper.cs` | 修改 | 整个文件 `#if !PUBLISH_AOT` 包裹 |
| `luban-agent/LubanAgentCli/App/TerminalGuiApp.cs` | 修改（34、59–60、124–126 行） | 3 处 FastInput 引用同步条件包裹 |

---

## Task 1: BertTokenizer 反射调用改公开静态工厂

**Files:**
- Modify: `luban-agent/LubanAgentCore/Retrieval/OnnxEmbeddingGenerator.cs:45-64`

已核实事实（Microsoft.ML.Tokenizers 2.0.0 程序集元数据）：`Microsoft.ML.Tokenizers.BertTokenizer` 为 public sealed、无公开构造函数，工厂签名 `public static BertTokenizer Create(string vocabFile, BertOptions options)`，返回类型可赋给字段 `Tokenizer? _tokenizer`。

- [ ] **Step 1: 替换反射块**

将 `EnsureLoaded()` 中的这一段：

```csharp
                var bertTokenizerType = typeof(Tokenizer).Assembly.GetType("Microsoft.ML.Tokenizers.BertTokenizer");
                if (bertTokenizerType == null)
                    throw new NotSupportedException("Microsoft.ML.Tokenizers 版本不支持 BertTokenizer");

                var createMethod = bertTokenizerType.GetMethod("Create", new[] { typeof(string), typeof(BertOptions) });
                if (createMethod == null)
                    throw new NotSupportedException("BertTokenizer.Create(string, BertOptions) 方法不存在");

                _tokenizer = createMethod.Invoke(null, new object[] { vocabTxtPath, options }) as Tokenizer
                    ?? throw new InvalidOperationException("BertTokenizer.Create 返回 null");
```

替换为一行：

```csharp
                _tokenizer = BertTokenizer.Create(vocabTxtPath, options);
```

替换后该 lock 块应为：

```csharp
            if (_tokenizer == null)
            {
                var tokenizerJsonPath = Path.Combine(_modelDir, "tokenizer.json");
                if (!File.Exists(tokenizerJsonPath))
                    throw new FileNotFoundException($"tokenizer.json 不存在于 {tokenizerJsonPath}");

                var vocabTxtPath = EnsureVocabTxt(tokenizerJsonPath);
                var options = BuildBertOptions();

                _tokenizer = BertTokenizer.Create(vocabTxtPath, options);
            }
```

`BertTokenizer`/`BertOptions`/`Tokenizer` 均由 `GlobalUsings.cs` 的 `global using Microsoft.ML.Tokenizers;` 覆盖，无需新增 using。

- [ ] **Step 2: 构建验证**

Run:

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgent.slnx
```

Expected: `生成成功`，0 error。新代码不产生任何告警（BertTokenizer.Create 是公开静态方法，无裁剪/AOT 告警）。

- [ ] **Step 3: 提交**

```powershell
git -C D:\WorkBench\Walle\luban\luban-agent status --short
git -C D:\WorkBench\Walle\luban\luban-agent add LubanAgentCore/Retrieval/OnnxEmbeddingGenerator.cs
git -C D:\WorkBench\Walle\luban\luban-agent commit -m "refactor: BertTokenizer 反射创建改为公开静态工厂 Create"
```

`status --short` 应只显示本任务文件（若有其他无关改动，不要 add）。

---

## Task 2: 配置 JSON 源生成上下文

**Files:**
- Create: `luban-agent/LubanAgentCore/Configuration/ConfigJsonContext.cs`
- Modify: `luban-agent/LubanAgentCore/Configuration/ConfigManager.cs:111`
- Modify: `luban-agent/LubanAgentCore/Configuration/ConfigManager.cs:144`
- Modify: `luban-agent/LubanAgentCore/Services/WorkspaceManager.cs:684`

关键约束（均已实测，见 spec 4.2）：
- `[JsonSourceGenerationOptions]` 没有 `Encoder` 参数，不能在特性里设 `UnsafeRelaxedJsonEscaping`。
- 拷贝 `ConfigJsonContext.Default.Options` 得到的 options，其 `TypeInfoResolver` 仍指向源生成上下文，AOT 有效。
- holder 必须是独立类型且惰性初始化；在 context 类自身的静态字段里访问 `Default.Options` 会在静态构造期 NRE。

- [ ] **Step 1: 新建源生成上下文文件**

创建 `luban-agent/LubanAgentCore/Configuration/ConfigJsonContext.cs`，完整内容：

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LuBan.AIAgent.Configuration;

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

internal static class ConfigJsonOptions
{
    private static JsonSerializerOptions? _pretty;

    public static JsonSerializerOptions Pretty =>
        _pretty ??= new JsonSerializerOptions(ConfigJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
}
```

- [ ] **Step 2: 改造 ConfigManager.Load 的反序列化**

`ConfigManager.cs` 第 111 行：

```csharp
                var config = JsonSerializer.Deserialize<AppConfig>(json);
```

改为：

```csharp
                var config = JsonSerializer.Deserialize(json, typeof(AppConfig), ConfigJsonOptions.Pretty) as AppConfig;
```

（`WhenWritingNull` 对反序列化无影响；读写共用同一 options 保证 resolver 一致。）

- [ ] **Step 3: 改造 ConfigManager.Save 的序列化**

`ConfigManager.cs` 第 144 行：

```csharp
            var json = _config.ToJson(hasIndentation: true);
```

改为：

```csharp
            var json = JsonSerializer.Serialize(_config, typeof(AppConfig), ConfigJsonOptions.Pretty);
```

- [ ] **Step 4: 改造 WorkspaceManager 内置规则参考 JSON**

`WorkspaceManager.cs` 第 684 行：

```csharp
                var json = config.ToJson(hasIndentation: true);
```

改为：

```csharp
                var json = JsonSerializer.Serialize(config, typeof(CustomRuleConfig), ConfigJsonOptions.Pretty);
```

`CustomRuleConfig` 已由该文件 `using LuBan.AIAgent.Configuration;`（第 17 行）覆盖；`JsonSerializer` 由 `GlobalUsings.cs` 的 `global using System.Text.Json;` 覆盖。

- [ ] **Step 5: 构建并确认源生成器产出**

Run:

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore\LubanAgentCore.csproj -p:EmitCompilerGeneratedFiles=true
```

Expected: `生成成功`，0 error。

确认生成文件存在且覆盖 5 个类型：

```powershell
Get-ChildItem -Recurse D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore\obj\Debug\net10.0\generated -Filter "*ConfigJsonContext*.g.cs"
```

Expected: 至少列出一个 `.g.cs` 文件。再确认内容包含全部类型：

```powershell
Select-String -Path (Get-ChildItem -Recurse D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore\obj\Debug\net10.0\generated -Filter "*ConfigJsonContext*.g.cs").FullName -Pattern "AppConfig|ProviderConfig|CustomSkillConfig|CustomRuleConfig|McpServerConfig" | Measure-Object
```

Expected: 匹配行数明显大于 0（每个类型在生成代码中出现多次）。

- [ ] **Step 6: 临时控制台做真实往返验证（不入库）**

创建临时目录与工程：

```powershell
New-Item -ItemType Directory -Force -Path C:\Users\yswen\AppData\Local\Temp\opencode\jsonroundtrip | Out-Null
```

创建 `C:\Users\yswen\AppData\Local\Temp\opencode\jsonroundtrip\jsonroundtrip.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="D:\WorkBench\Walle\luban\luban-agent\LubanAgentCore\LubanAgentCore.csproj" />
  </ItemGroup>
</Project>
```

创建 `C:\Users\yswen\AppData\Local\Temp\opencode\jsonroundtrip\Program.cs`：

```csharp
using LubanAgentCore.Configuration;
using LuBan.AIAgent.Configuration;

var dir = Path.Combine(Path.GetTempPath(), "luban-aot-json-test");
Directory.CreateDirectory(dir);
var path = Path.Combine(dir, "config.json");
if (File.Exists(path)) File.Delete(path);

var cm = new ConfigManager(path);
cm.AddProvider("TestProvider", "secret", "https://example.com");
cm.AddCustomRule(new CustomRuleConfig { Id = "r1", Name = "中文规则", Action = "allow", Priority = 5 });

var text = File.ReadAllText(path);
Console.WriteLine(text);

if (text.Contains("\\u", StringComparison.Ordinal))
    throw new Exception("失败：中文被 \\u 转义");
if (!text.Contains("中文规则", StringComparison.Ordinal))
    throw new Exception("失败：中文未原样写出");
if (text.Contains("SelectedModel", StringComparison.Ordinal))
    throw new Exception("失败：null 属性不应输出");

var cm2 = new ConfigManager(path);
cm2.Load();
if (cm2.Providers.Count != 1 || cm2.Providers[0].ApiKey != "secret")
    throw new Exception("失败：Provider 往返反序列化错误");
if (cm2.CustomRules.Count != 1 || cm2.CustomRules[0].Name != "中文规则" || cm2.CustomRules[0].Priority != 5)
    throw new Exception("失败：规则往返反序列化错误");

Console.WriteLine("JSON-ROUNDTRIP-OK");
```

Run:

```powershell
dotnet run --project C:\Users\yswen\AppData\Local\Temp\opencode\jsonroundtrip\jsonroundtrip.csproj
```

Expected: 打印配置 JSON（中文原样、无 `SelectedModel` 字段、缩进），末行 `JSON-ROUNDTRIP-OK`，退出码 0。

清理临时工程：

```powershell
Remove-Item -Recurse -Force C:\Users\yswen\AppData\Local\Temp\opencode\jsonroundtrip
```

- [ ] **Step 7: 整解构建**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgent.slnx
```

Expected: `生成成功`，0 error。

- [ ] **Step 8: 提交**

```powershell
git -C D:\WorkBench\Walle\luban\luban-agent status --short
git -C D:\WorkBench\Walle\luban\luban-agent add LubanAgentCore/Configuration/ConfigJsonContext.cs LubanAgentCore/Configuration/ConfigManager.cs LubanAgentCore/Services/WorkspaceManager.cs
git -C D:\WorkBench\Walle\luban\luban-agent commit -m "refactor: 配置 JSON 改用 System.Text.Json 源生成上下文，消除反射序列化"
```

确认 `status --short` 中没有 `jsonroundtrip` 之类临时文件；该工程在 temp 目录、本就在仓库外。

---

## Task 3: FastInputBootstrapper 条件编译隔离

**Files:**
- Modify: `luban-agent/LubanAgentCli/Infrastructure/FastInputBootstrapper.cs`（整个文件）
- Modify: `luban-agent/LubanAgentCli/App/TerminalGuiApp.cs:34`
- Modify: `luban-agent/LubanAgentCli/App/TerminalGuiApp.cs:59-60`
- Modify: `luban-agent/LubanAgentCli/App/TerminalGuiApp.cs:124-126`

`PUBLISH_AOT` 当前全仓库无定义，正常构建行为零变化。

- [ ] **Step 1: 包裹 FastInputBootstrapper.cs**

在 `luban-agent/LubanAgentCli/Infrastructure/FastInputBootstrapper.cs` 的**第 1 行之前**插入一行，文件**最后一行之后**加结尾指令：

文件开头变为：

```csharp
#if !PUBLISH_AOT
using System.Reflection;
using System.Threading;
```

文件末尾（原第 248 行 `}` 之后）变为：

```csharp
}
#endif
```

中间所有内容保持不动。

- [ ] **Step 2: 包裹 TerminalGuiApp.cs 字段声明（34 行）**

将：

```csharp
    private FastInputBootstrapper? _fastInput;
```

改为：

```csharp
#if !PUBLISH_AOT
    private FastInputBootstrapper? _fastInput;
#endif
```

- [ ] **Step 3: 包裹 Dispose 中的调用（59–60 行）**

将：

```csharp
        _fastInput?.Dispose();
        _fastInput = null;
```

改为：

```csharp
#if !PUBLISH_AOT
        _fastInput?.Dispose();
        _fastInput = null;
#endif
```

- [ ] **Step 4: 包裹 Run 中的创建与日志（124–126 行）**

将：

```csharp
            _fastInput = new FastInputBootstrapper();
            var fastInputOk = _fastInput.TryEnable(application);
            Logger.Warn($"[TuiDiag] FastInput enabled={fastInputOk}");
```

改为：

```csharp
#if !PUBLISH_AOT
            _fastInput = new FastInputBootstrapper();
            var fastInputOk = _fastInput.TryEnable(application);
            Logger.Warn($"[TuiDiag] FastInput enabled={fastInputOk}");
#endif
```

`fastInputOk` 仅在块内使用，随整块一起隔离，无游离变量。`using LubanAgentCli.Infrastructure;`（第 19 行）保留——同命名空间下的 `TuiDiag` 仍在使用。

- [ ] **Step 5: 正常构建（宏未定义，功能保留）**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\LubanAgentCli.csproj
```

Expected: `生成成功`，0 error。

确认 FastInput 代码确实编入 DLL。程序集用户字符串（`ldstr`）以 UTF-16LE 存储，需按 Unicode 解码后检查该类独有的线程名字面量 `"FastInputPoller"`：

```powershell
$dll = "D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\bin\Debug\net10.0\LubanAgentCli.dll"
[Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($dll)).Contains("FastInputPoller")
```

Expected: 输出 `True`。

- [ ] **Step 6: 带 PUBLISH_AOT 常量构建（隔离生效）**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\LubanAgentCli.csproj -p:DefineConstants=PUBLISH_AOT
```

Expected: `生成成功`，0 error（证明条件闭包完整，无残留引用）。

确认 FastInput 代码未编入（同一路径 DLL 已被本次构建覆盖）：

```powershell
$dll = "D:\WorkBench\Walle\luban\luban-agent\LubanAgentCli\bin\Debug\net10.0\LubanAgentCli.dll"
[Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($dll)).Contains("FastInputPoller")
```

Expected: 输出 `False`。

- [ ] **Step 7: 恢复正常构建产物**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgent.slnx
```

Expected: `生成成功`。再跑一次 Step 5 的 Unicode 字节检查，Expected: 恢复输出 `True`。

- [ ] **Step 8: 提交**

```powershell
git -C D:\WorkBench\Walle\luban\luban-agent status --short
git -C D:\WorkBench\Walle\luban\luban-agent add LubanAgentCli/Infrastructure/FastInputBootstrapper.cs LubanAgentCli/App/TerminalGuiApp.cs
git -C D:\WorkBench\Walle\luban\luban-agent commit -m "refactor: FastInputBootstrapper 以 PUBLISH_AOT 条件编译隔离，供未来 AOT 发布裁剪"
```

---

## Task 4: 最终手动冒烟与交付确认

**Files:** 无代码改动。

- [ ] **Step 1: 全量构建**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-agent\LubanAgent.slnx
```

Expected: 0 error，告警数与改造前持平（无新增告警）。

- [ ] **Step 2: framework 回归（可选，本轮零改动）**

```powershell
dotnet build D:\WorkBench\Walle\luban\luban-framework\LuBan.FrameWork.sln
```

Expected: 成功。本轮未改 framework 任何文件，此步仅确认工作区无意外污染。

- [ ] **Step 3: TUI 手动冒烟**

按 `luban-agent/LubanAgentCli/docs/TUI-SMOKE-TEST.md` 至少执行：

1. 第 1、21、22 项：TUI 立即启动、无输入延迟、输入即时响应（验证 FastInput 在正常构建下行为不变）。
2. 在 TUI 中执行 `/provider` 添加一个 Provider 后退出，检查 `%LocalAppData%\LuBan\AIAgent\config.json`：中文不转义、缩进、无 null 字段；再次启动配置仍在（验证 Task 2 往返）。
3. 若本机已下载嵌入模型（`EmbeddingModels`），执行一次 `/rag` 索引或语义检索（验证 Task 1 BertTokenizer 路径）；模型不在本机则记录为"待有模型环境补验"，不阻塞交付。

- [ ] **Step 4: 提交结果与日志核对**

```powershell
git -C D:\WorkBench\Walle\luban\luban-agent log --oneline -3
git -C D:\WorkBench\Walle\luban\luban-agent status --short
```

Expected: 看到本计划的 3 条 refactor 提交；`status --short` 干净（或只剩与本任务无关的既有改动）。

向用户汇报：改动文件清单、构建结果、冒烟结果（含未验证项及原因）。**不要 push**，除非用户明确要求。
