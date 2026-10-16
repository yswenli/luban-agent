# LubanAgent.Plugin.NewProject（新建项目插件）

> PRD：`docs/PRD-插件体系与新建项目插件.md`（v0.18）· 里程碑 **M4**
> 插件标识：`com.tjces.newproject` · 显示名：新建项目

以 TJCES 三端模板仓创建新项目：**七步向导**（左侧栏面板）+ **聊天式入口**（`newproject-*` 技能），
两条入口共用同一个 `INewProjectService`，云效逻辑零分叉。

## 目录结构

```
LubanAgent.Plugin.NewProject/
├─ LubanAgent.Plugin.NewProject.csproj   # 含「铺包」目标（见下）
├─ plugin.json                           # 插件清单：侧栏条目 / 6 个技能 / yunxiao MCP / 权限
├─ newproject.settings.json              # 模板 pin、参考流水线、命名与推送分支约定
├─ NewProjectPlugin.cs                   # 插件入口（PluginBase）
├─ Abstractions/
│  ├─ ProjectSpec.cs                     # 项目档案与相关模型（PRD §4.5）
│  └─ INewProjectService.cs              # 共享能力层契约（PRD §4.10.2）
├─ Configuration/NewProjectSettings.cs   # 包内配置读取（模板对照 / 参考流水线）
├─ Serialization/PluginJsonContext.cs    # 源生成 JSON 上下文（插件持久化的唯一入口，见下）
├─ Services/
│  ├─ NewProjectService.cs               # 共享能力层实现（P2~P5 分批补齐）
│  └─ NewProjectStateStore.cs            # 档案持久化（残留检测 / 孤儿清单）
├─ Ui/WizardPanel.cs                     # 向导面板（UserControl + IPluginPanel，纯代码 UI）
└─ skills/                               # 6 个技能正文（随插件包分发，落到工作区）
```

## 打包硬约束（重要，改动前先读）

插件包 `lib/` **只放插件自身程序集**（`LubanAgent.Plugin.NewProject.dll` + 同名 `deps.json`），
**不得**把宿主程序集（`LubanAgentCore.dll` 等）复制进去。

原因：框架 `PluginLoadContext` 只强制共享 `LuBan.*` / `Avalonia` / `Microsoft.*` / `System.*`；
宿主程序集若出现在 `lib/`，会被加载进插件 ALC，产生**第二份 `Type` 副本**，
与宿主传入的服务实例类型不一致（`IPlugin` / `IWorkspaceManager` 等转换会失败）。

依据（已实测）：`AssemblyDependencyResolver.ResolveAssemblyToPath` 对「文件缺失」的依赖返回 `null`
→ `PluginLoadContext.Load` 返回 `null` → 运行时回落**默认 ALC** → 与宿主共用同一份类型。

因此本工程：

- 以 `ProjectReference` 引用 `LubanAgentCore`（**编译期**用宿主/框架 API）；
- `StagePluginPackage` 目标只复制 `plugin.json` / `newproject.settings.json` / 自身 dll + deps.json / `skills/`，
  **不整目录复制**输出目录。

### 序列化硬约束（同样重要）

插件内的 JSON 读写**只能**经 `Serialization/PluginJsonContext`（`System.Text.Json` **源生成**），
**不得**使用反射式 `JsonSerializer.Deserialize<T>` / `Serialize<T>`（`T` 为插件自身类型），
也不得自建 `JsonSerializerOptions`。

原因（已实测，5 组对照）：反射路径会在**默认 ALC** 里按「插件类型」为键缓存反射元数据
（`JsonTypeInfo` / 成员访问委托），把插件 ALC **永久钉住** → 停用/卸载时第 ⑥ 步 GC 弱引用验证必然失败
（降级 `RequiresRestart`），M1 的「卸载即真回收」保证被破坏。实测结论：

| 做法 | 插件 ALC 能否回收 |
|------|------------------|
| 仅加载程序集 | ✅ |
| 反射式 `JsonSerializer` 解析**插件类型**（解析成功） | ❌ 钉住（其 `JsonSerializerOptions` 实例本身已被 GC，说明问题在默认 ALC 的类型缓存） |
| **源生成 `JsonSerializerContext`**（本插件采此方案） | ✅ |
| 纯 DOM（`JsonDocument` / `Utf8JsonWriter`） | ✅ |

新增需要持久化的模型时，**必须**在 `PluginJsonContext` 上登记 `[JsonSerializable]`。
配置里的「端」写字符串枚举（`"Service"`），由该上下文的 `UseStringEnumConverter` 承载。

## 构建与铺包

```powershell
dotnet build luban-agent/LubanAgent.slnx          # 整解决方案（含本插件）
dotnet build luban-agent/LubanAgent.Plugin.NewProject/LubanAgent.Plugin.NewProject.csproj
```

构建后插件包自动铺到 codex 运行目录：

```
luban-agent/LubanAgentCodex/bin/<Configuration>/net10.0/plugins/com.tjces.newproject/
├─ plugin.json
├─ newproject.settings.json
├─ lib/LubanAgent.Plugin.NewProject.dll
├─ lib/LubanAgent.Plugin.NewProject.deps.json
└─ skills/newproject-*.md
```

> 首次运行的 codex 可能**尚未启用**本插件：默认状态为禁用，需在「插件管理」窗口启用。
> 启用状态记录在 `%LocalAppData%/LuBan/AIAgent/plugins/plugin-states.json`。

## 验证（luban-agent 无自动化测试，手工冒烟）

1. 构建解决方案 → 确认上面的插件包目录已生成；
2. 启动 codex → 「插件管理」启用「新建项目」；
3. 左侧栏出现「新建项目」入口，点击后面板挂载成功（左侧步骤条 + 右侧操作记录区）；
4. 停用插件 → 侧栏入口与面板消失，`.luban-agent/skills/newproject-*` 无残留。

## 阶段进度

| 批次 | 内容 | 状态 |
|------|------|------|
| **P1** | 工程骨架、清单、档案模型、共享能力层契约与桩、向导面板骨架、铺包通路、源生成 JSON 通路 | **已完成**（构建 0 错误；框架侧生命周期探针全绿，含「停用后 ALC 真回收」） |
| P2 | Step 1 表单 + Step 2 初始化项目环境（工作区 / 会话 / 技能 / 云效 / 项目助手） | 待办 |
| P3 | Step 3 按模板建库并克隆（框架 `YunxiaoConnector` 直连）+ Step 6 推送 | 待办 |
| P4 | Step 4 配置与建库 + Step 5 编译检查 | 待办 |
| P5 | Step 7 创建流水线 + 中止并清理（工作区注销 / 技能与 MCP 文件回收） | 待办 |
| P6 | 6 个技能正文细化 + 聊天入口与向导联调 | 待办 |