# 插件体系冒烟资产（M1 ~ M3）

本目录是 **PRD-插件体系与新建项目插件.md（v0.18）** 中 M1/M2/M3 的**可复现验证入口**：
framework 侧带自动断言的 headless harness + codex 宿主侧的手工回归清单与插件包。

> 临时冒烟工程已沉淀到此处（原先放在系统临时目录，验证完即弃），后续改插件体系**必须先跑本目录**。

## 目录结构

| 路径 | 说明 |
|------|------|
| `build.ps1` | 一键：构建插件包 → 跑 harness 三场景 → 构建 codex 插件并铺到运行目录 |
| `framework-harness/` | headless 断言宿主：`Program.cs`（断言脚本）、`TestPluginHost.cs`（最小 `IPluginHost` 实现，只记账） |
| `framework-plugins/HelloPlugin/` | 主冒烟插件（`com.demo.hello`）：三条 Skill 通道 + 面板 + 卸载后残留 UI 回调 |
| `framework-plugins/HelloPluginTimer/` | 二分定位变体：额外持有「每秒 PostToUi」定时器 |
| `codex-plugin/` | codex 宿主冒烟插件（`com.demo.codex`）：侧栏 / 内容区面板 / 技能落盘 / MCP 回放 / `PostToUi` |
| `artifacts/` | 生成物（git 忽略）：两个 framework 插件包 + codex 插件包 |

冒烟工程**不加入 `LubanAgent.slnx`**（避免日常构建变慢、避免插件包污染宿主输出），只由 `build.ps1` 单独构建。

## 1. 一键构建与运行

```powershell
# 全流程（含 codex 插件铺包）
pwsh -File luban-agent\smoke\plugin-system\build.ps1

# 只跑 framework 侧
pwsh -File luban-agent\smoke\plugin-system\build.ps1 -SkipCodex
```

`build.ps1` 退出码 0 = framework 三场景全绿；非 0 会打印失败场景。
脚本用 `ProjectReference` 直接引 `luban-framework/LuBan.AIAgent`（现行 `TEMP-LOCAL-VERIFY` 策略），**改框架源码即时生效，无需发 nupkg**。

## 2. framework-harness 三场景（自动断言）

| 场景 | 命令 | 断言要点 |
|------|------|----------|
| 正向 | `dotnet run --project framework-harness -- unload <artifacts/plugins>` | 启用 → 停用后 `RequiresRestart=false`：ALC/实例/程序集/类型弱引用全部死亡（§3.12 第 7 步真回收）；侧栏/面板工厂/Skill/MCP 全部精确注销；无残留 UI 回调 |
| 反向 | `... --token <artifacts/plugins>` | 宿主故意保留 UI 委托 → 卸载被**检出失败**并降级为 `Disabled + RequiresRestart=true`（不静默通过）；`PostToUi` 失效令牌拦下卸载后追补的回调，副作用未发生 |
| 二分定位 | `... --unload <artifacts/plugins-timer>` | 定时器变体走正向模式仍能真回收 → 排除「周期性 UI 投递钉住 ALC」 |

三场景均为「启用 → 停用 → 二次启用 → 二次停用」，覆盖可重复装载。
`com.demo.hello` 的 `permissions` 故意写成数组简写的**裸维度** `["filesystem:workspace","network"]`，用于覆盖 §3.7「裸维度 ≡ 通配、声明期放行 + 记警告、运行期以宿主白名单为上限」这条路径。

## 3. codex 宿主手工回归（M2 + M3 + 问题 C）

前置：`pwsh -File ...\build.ps1` 铺包成功，然后**启动/重启** `LubanAgentCodex`（`dotnet run --project luban-agent\LubanAgentCodex` 或直接跑 exe，首次扫描约 16~18s）。

| # | 步骤 | 预期证据 |
|---|------|----------|
| 1 | 插件管理器启用 `com.demo.codex` | `logs/info.txt` 出现「初始化 → 3 条技能写入 → MCP 回放登记 → 已启用」；`%LocalAppData%\LuBan\AIAgent\plugins\plugin-states.json` 中 `state: 1` 且无 `lastError` |
| 2 | 点击侧栏「冒烟面板」挂载内容区 | 面板标题正常，`UI 回调计数` 每秒 +1（验证 `PostToUi` 跨线程投递） |
| 3 | 检查技能落盘 | `<ws>\.luban-agent\skills\` 下：`codex-hello-skill`（清单 + `entry` 正文）、`codex-runtime-skill`（**运行期贡献** + `entry` 正文，逐字等于插件包内 `skills\codex-runtime-skill.md`）、`codex-noentry-skill`（仅元信息，预期带警告） |
| 4 | 本次会话构建 | `logs/info.txt` 出现「会话回放插件 MCP：yunxiao（插件 com.demo.codex）」 |
| 5 | **问题 C 回归（运行期技能回放）** | 插件保持启用 → 切到另一个工作区（或新开工作区）→ 新工作区 `.luban-agent\skills\codex-runtime-skill\SKILL.md` **被补齐**且 info 出现「会话回放运行期插件技能：codex-runtime-skill」；若启用时无活动工作区，info 出现「暂无活动工作区，将在会话构建时补齐」而非报错 |
| 6 | **固定回归项：挂载面板后停用** | 面板先从内容区解绑（UI 无残留、无异常），info 出现「定时器释放 → 已停用」，且**不得**出现「程序集卸载验证未通过」降级；停止后 `codex-runtime-skill` 等在**所有写过的工作区**被删除（第 5 步补齐的工作区也要清掉） |
| 7 | M3 凭据（D1：明文沿用宿主配置基线） | 设置页录入云效组织 ID + PAT → `%LocalAppData%\LuBan\AIAgent\users\<用户>\config.json` 的 `Integrations` 出现 `yunxiao_organization_id` / `yunxiao_pat` |
| 8 | 警告核对 | `logs/warn.txt` 为**当日文件**，其 `LastWriteTime` 早于本次运行 ⇒ 本次零警告（无入口文件不可读、无越界、无卸载降级） |

已知限制：codex 侧无自动化（app 无测试工程），只能按上表手工回归；`logs/warn.txt` 仅在产生 warn 级条目时才写入/更新时间戳，故「时间戳未变」即「本次零警告」。

## 4. 与需求文档的对应

| 资产 | PRD 章节 |
|------|----------|
| harness 七步卸载 / 降级 / 弱引用断言 | §3.12、§3.12.5 |
| `PostToUi` 失效令牌 | §6 #22 |
| 三条 Skill 通道 + 工作区落盘 + 会话回放 | §3.11、§4.10 |
| 权限声明（裸维度 ≡ 通配、白名单为上限） | §3.7 |
| codex 侧栏 / 内容区 / 插件管理器 | §3.4、§3.9 |
| M1 ~ M3 验收 | §5 |