# Web 端插件管理设计（LubanAgentWebHost）

日期：2026-11-13
范围：`luban-agent/LubanAgentWebHost`（后端 + 前端）、`luban-agent/LubanAgentCore`（新增与一处客户端启动器改动）
修订：v2 —— 按深度审查结论修正静态根、缓存、MIME、限额与隔离边界，并新增 `M15/M16`

## 1. 目标

让 Web 端（浏览器）具备一套自己的插件体系，与桌面端 `LubanAgentCodex` 的目录与面板隔离：

1. 左侧栏「设置」上方新增「插件」入口，打开插件管理页。
2. 插件管理页支持：列出插件、上传本地 zip 安装、启用、停用、升级、卸载，并清楚说明 zip 包的内容与结构。
3. 插件自带 HTML 页面：启用后在左侧栏底部（知识库/设置那一组）出现插件自己的入口，点开占主内容区；没有页面的插件不产生任何入口。
4. 插件只对 Web 会话生效：贡献技能与 MCP；不实现桌面端的 `IPluginPanel` / Avalonia 面板。

## 2. 关键结论（先行）

1. **方案可行**：运行时安装的页面文件能被 SAEA 静态通道命中（`MapPath` 未在启动快照里时按「根 + 路径」拼物理路径，命中判定是 `File.Exists`），因此无需改框架、无需重启即可访问新装页面。
2. **必须绕开 4 个硬限制**：`InstallAsync` 写死 `<BaseDirectory>/plugins`；`plugin.json` 没有页面字段且未知成员会被丢弃；静态文件字节按物理路径缓存（默认 `IsStaticsCached=true`）；MVC 静态兜底把 URL 倒数第二段当控制器名。
3. **有一处必须改客户端启动器**：`ServerProcessLauncher.BuildArguments` 现在只下发客户端的 `--plugins-dir`（`LubanAgentCore/Client/ServerProcessLauncher.cs:169-189`），不改就做不到「Web 插件目录与 Codex 隔离」。方案 B（已确认）：**保留 `--plugins-dir` 语义**，新增 `--web-plugins-dir` 专用参数；宿主两个根都扫描，但 Web 管理页只呈现 Web 根下的插件（见 §7 M16）。
4. **隔离是「目录 + 状态文件」，技能文件不隔离**：插件贡献的**技能**由 `AgentPluginHost.RegisterSkill` 写成工作区文件 `.luban-agent/skills/<id>/SKILL.md`（`LubanAgentCore/Plugins/AgentPluginHost.cs:229-243`），同一工作区被 Codex 打开时仍会看到；**MCP** 只注册进当前进程的会话级 `MCPRegistry`（同文件 `:282-320`、`:464-489`），不落盘、不泄漏给桌面端。文案与验收必须按此写，不能声称「Codex 完全看不到」。
5. **单包体积受限但可用**：Base64 上传后需小于 SAEA 的 64MB 请求体上限（`SAEA.Http/Net/Pipeline/HttpRequestSplitter.cs:68-82`），故单包 32MB 是安全上界。

## 3. 现状（可核依据）

- 左侧栏底部目前只有 知识库 / 设置 / 帮助 / 关于：`web/src/pages/SessionList.tsx:194-211`。
- 前端路由全在 hash 里，只有 sessions / chat / settings / wiki 四种：`web/src/lib/router.ts:3-7`、`:13-26`；主区渲染在 `web/src/App.tsx:63-77`，浮层挂载在 `:76`。
- 用户初始化时会扫描并恢复插件启用状态（不是进程启动瞬间）：`Hosting/AgentWebRuntime.cs:150` → `LubanAgentCore/Plugins/PluginBootstrap.cs:37-85`。
- 插件根目录来自 `LubanAgentWeb:PluginsDir` / `--plugins-dir`，为空时取宿主程序目录下的 `plugins`：`Hosting/WebHostSettings.cs:41-47`、`:69`、`:87-89`。
- 客户端启动 WebHost 时**会**下发 `--plugins-dir`（指向客户端插件目录）：`LubanAgentCore/Client/ServerProcessLauncher.cs:160-189`，单测在 `LubanAgent.Tests/AgentProtocolUnitTest.cs:238-258`。
- 插件管理服务已存在但无 Web 出口：`LubanAgentCore/Services/Management/PluginManagementService.cs:62-98`（转发框架 `PluginManager`）。
- 框架生命周期入口：`ScanAsync`（`PluginManager.cs:90-167`，同 id 已登记则跳过）、`EnableAsync`（`:216`）、`DisableAsync`（`:264-287`，卸载验证失败时置 `Disabled` + `RequiresRestart`）、`UninstallAsync`（`:295-332`，删目录 + 删状态）、`UpgradeAsync`（`:345-470`，先校验新清单、再卸载旧版、`RequiresRestart` 时中止、成功后回到 `Installed`）、`InstallAsync`（目标目录硬编码 `<BaseDirectory>/plugins`，`:492`）、`Changed` 事件（`:82`）。
- 安装包结构约定（框架）：`plugin.json` + `lib/`（入口程序集及依赖）+ `assets/`：`LuBan.AIAgent/Plugins/Loading/PluginLocation.cs:36-54`；入口程序集解析 `lib/<name>` 或根目录同名文件（`ManifestValidator.cs:125-130`）。
- 清单模型：`PluginManifest.cs:60-135`；`uiContributions` 元素只有 `id/label/icon/panelType`（`PluginSidebarEntry.cs`），**没有 `webPage`**，`JsonSerializer` 会静默丢弃未知成员。
- 清单校验：`id/name/version/entryAssembly` 必填（`ManifestValidator.cs:88-96`）、id 正则（`:94-96`）、版本语义化（`:98-100`）、`targetApp` 仅在配置了 `LuBanAgent:Plugins:TargetApp` 时校验（`:106-113`）、缺 `panelType` 与缺 `label` 只是警告（`:145-149`）。两端 appsettings 均未配 `TargetApp`。
- 插件状态文件默认 `%LocalAppData%/LuBan/AIAgent/plugins/plugin-states.json`，可由 `LuBanAgent:Plugins:StateFile` 覆盖（`PluginStateRepository.cs:48-62`、`LuBanAgentExtensions.cs:194-195`）；`PluginLoader` 为单例（`LuBanAgentExtensions.cs:184`），Web 侧可直接解析做安装前校验。
- 全站门禁只认三种凭据：`?t=`、`luban_auth` Cookie、`Authorization: Bearer`（`Hosting/GatedHttpContext.cs:59-107`、`WebHostAccess.cs:219-259`）；静态资源同样被拦。Cookie 为 `Path=/; HttpOnly; SameSite=Lax`（`WebHostAccess.cs:336-337`），同源 iframe 自动携带。
- 特权接口只认固定 Bearer：`Services/WebSystemService.cs:175-186`。
- 已有 JSON + Base64 上传 zip 的先例（技能导入）：`Services/WebSkillService.cs:109-157`；技能导入限额 `MaxImportEntries=500`、解压总量 8MB（`SkillManagementService.cs:42-45`、`:334-380`）。
- 静态根：`Program.cs:101-114` 只允许相对单段目录名 `wwwroot`（`:110`）；`HttpUtility` 构造时以 `PathHelper.GetCurrentPath()` 为父、做一次目录快照（`SAEA.Http/HttpUtility.cs:60-76`），`MapPath` 对未命中快照的 URL 回退为「父目录 + 根 + 路径」（`:84-105`）；命中判定是 `File.Exists`（`SAEA.Http/Common/StaticResourcesCache.cs:68-71`）。`PathHelper.GetCurrentPath()` 返回 `Path.GetDirectoryName(AppContext.BaseDirectory)`，因 `BaseDirectory` 带尾部分隔符，结果就是输出目录本身（`SAEA.Common/IO/PathHelper.cs:46-64`）。
- 静态文件有两层缓存：配置 `IsStaticsCached` 默认 `true`（`SAEA.MVC/SAEAMvcApplicationConfig.cs:58-61`，本项目未覆盖），`FileResult/HttpFileResult` 按**物理路径**缓存字节（`SAEA.Http/Model/HttpFileResult.cs:86-95`）；`MapPath` 另按 URL 永久缓存映射（`HttpUtility.cs:84-105`）。响应头层面 `CacheCalcString` 默认 `-1,-1`（不缓存，`SAEA.Http/HttpSession.cs:47`）。
- MVC 静态兜底：URL 段数 ≥3 时把 `arr[len-2] + "Controller"` 当控制器名，未命中才走静态（`SAEA.MVC/HttpContext.cs:250-274`）。
- MIME 表覆盖面小：`.js → application/x-javascript`（属合法 JS MIME，`type="module"` 可用）；`.mjs/.json/.map/.wasm/.woff/.woff2/.ttf/.webp` 无映射，返回 `unknown`（`SAEA.Http/Base/HttpMIME.cs`）。
- 前端构建：`outDir=../wwwroot`、`emptyOutDir=true`（`web/vite.config.ts:41-48`）；`apiPrefixes` 12 项（`:15-28`）。`wwwroot` 由 csproj `Content ... PreserveNewest` 拷到输出目录、不清理目标（`LubanAgentWebHost.csproj:32-46`）。
- 插件程序集共享集：默认 `LuBan.* / Microsoft.* / System.*` 等（`Configuration/PluginAssemblySharing.cs:47-73`），`LubanAgent.*` 不在其中。

无上游设计依据：`docs/superpowers/specs/2026-11-09-web-ui-design.md` 未涉及插件管理。

## 4. 非目标 / 反目标

- 不实现桌面端 `IPluginPanel`（Avalonia 控件）在 Web 中的渲染；`uiContributions[].panelType` 在 Web 侧忽略。
- **不支持「纯 HTML 插件」**：`entryAssembly` 必填且入口类型须实现 `IPlugin`（`ManifestValidator.cs:88-96`、`Contracts/IPlugin.cs:32-59`）。自带页面只是同一个 .NET 插件的附加能力，安装说明必须写明。
- 不做插件市场、远程仓库、在线更新检查；只能上传本地 zip。
- 不做插件依赖自动安装（`dependencies` 缺失只报错）。
- 不修改 `luban-framework` 任何文件；不发包。
- 不追求「Codex 连工作区技能也看不到」（做不到，见结论 4），只保证插件目录与状态文件隔离。

## 5. 硬约束

1. **框架不能改**：`InstallAsync` 目标目录写死，Web 侧自己完成「解压 → 校验 → 复制到 Web 插件根 → `ScanAsync`」；启用/停用/升级/卸载复用框架。
2. **`plugin.json` 没有页面字段**，Web 侧必须从插件目录回读原始 `plugin.json` 取 `uiContributions[].webPage`（`PluginManifest` 已丢弃该成员）。
3. **插件页面与宿主同源**（B1）：走 `luban_auth` Cookie 过门禁；iframe 内同源 `<script src>`/`<link>` 自动可用，不能改成「沙箱 + Bearer」（子资源无法带 Authorization，会全 401）。
4. **静态根不可配**，运行时服务的是输出目录 `AppContext.BaseDirectory/wwwroot`；`GetCurrentPath()` 就是该输出目录本身（不是其上一级）。镜像必须写输出目录那一份，源码树 `LubanAgentWebHost/wwwroot` 不写插件内容。
5. **静态字节缓存按物理路径命中**：同一路径换内容后仍返回旧字节（进程内），所以镜像路径**必须带随安装变化的戳**（`M9`），否则升级后页面还是旧版。删除/重建目录也不清缓存。
6. **控制器名冲突**：`/plugin-web/{id}/{stamp}/web/` 看起来安全（倒数第二段是 `web`），但插件 `web/` 下若再建一层名为宿主控制器名的目录（`plugin/session/agent/attachment/mcp/model/llmwiki/a2a/agentsmd/system/provider/skill/rule/workspace`），该目录下的文件会先被当路由（`SAEA.MVC/HttpContext.cs:250-274`）。需在安装说明与校验中约束（`M15`）。
7. **MIME 只保证保守白名单**：白名单之外的 `.mjs/.json/.map/.wasm/.woff/.woff2/.ttf/.webp` 均返回 `unknown/unknown`，浏览器会拒绝加载（`M12`）。
8. **插件程序集只能依赖 `LuBan.AIAgent` 契约**：`LubanAgent.*` 不在 ALC 共享集（`PluginAssemblySharing.cs:47-73`），引用 `LubanAgentCore` 会在运行期加载失败，且只体现为 `lastError`。
9. **插件宿主身份固定默认用户**：`PluginHostContext.UserId` 硬编码 `LocalUserDefaults.DefaultUserId`（`LubanAgentCore/Plugins/PluginHostContext.cs:57`）。用 `--user` 启动 WebHost 时，插件技能会落到默认用户工作区。
10. **Web 插件必须有自己的根**（`M16`）：`--plugins-dir` 仍指向客户端（Codex）插件目录，Web 管理的插件根由 `--web-plugins-dir` 给出（缺省 `%LocalAppData%/LuBan/AIAgent/webhost/plugins`）。Web 管理页只允许操作 Web 根下的插件，桌面端插件在页面上只读/不可见。

## 6. 已确认的决策（用户）

| # | 决策 |
|---|------|
| D1 | 插件包格式不变：`plugin.json` + 入口程序集（及 `lib/`、`assets/`、技能等） |
| D2 | 插件装在 WebHost 自己的插件目录，由 Web 页面管理 |
| D3 | 只对 Web 会话生效（技能 / MCP），不碰 Codex 的目录与面板 |
| D4 | 插件可以自带 HTML 页面，且需要与宿主打通 |
| D5 | 有页面的插件在启用后于左侧栏底部出现入口，点开占主内容区；没页面则不显示任何入口 |
| D6 | 安装方式：用户选本地 zip；安装界面必须写清 zip 内的内容与结构 |
| D7 | 页面与宿主打通采用 B1：iframe 同源、不沙箱、走 Cookie，插件 JS 直接调现有 API |

## 7. 我定的默认（可推翻）

| # | 默认 | 理由 |
|---|------|------|
| M1 | Web 插件根：`%LocalAppData%/LuBan/AIAgent/webhost/plugins`；优先级 `LubanAgentWeb:WebPluginsDir` / `--web-plugins-dir` > 默认值。`PluginsDir` / `--plugins-dir` 仍是**桌面端插件根**（两个根都扫描，Web 管理页只列 Web 根） | 与 Codex 隔离；不写进程目录（部署目录可能只读、重建即丢）；保留既有参数含义不破坏 Codex |
| M2 | 插件状态文件：`%LocalAppData%/LuBan/AIAgent/webhost/plugin-states.json`，在 `Program` 里以「配置未显式给出时才写入」的方式覆盖 `LuBanAgent:Plugins:StateFile` | 否则与桌面端共用一份状态文件 |
| M3 | 安装成功后**自动启用**；启用失败按框架状态机落 `Disabled` 或 `Error`，回显 `lastError` 并提供「重试启用」 | 框架只有四态（`Contracts/PluginState.cs:32-53`），不能自造「已安装但启用失败」 |
| M4 | 已存在同 id 时拒绝安装，提示改用「升级」 | 避免误删已有插件目录 |
| M5 | 升级：上传同 id 新 zip → 解压校验 → 框架 `UpgradeAsync`；升级前启用的会自动重新启用 | 框架已实现热替换与卸载验证 |
| M6 | 限额：单包 32MB、解压后总量 128MB、条目 500（对齐技能导入）、单文件 32MB；Base64 后约 43MB < SAEA 64MB 请求体上限 | 条目数对齐 `SkillManagementService` 拦 zip 炸弹；体积放宽是因为插件含 DLL |
| M7 | 页面声明字段 `uiContributions[].webPage`，必须位于 `web/` 之下（如 `web/index.html`） | 复用侧栏条目语义；让资源 URL 足够段数，避开短路径被当 `/{controller}/{action}` |
| M8 | 前端路由 `#/plugin/{pluginId}/{entryId}`；iframe 地址 `/plugin-web/{pluginId}/{stamp}/web/...` | 前端路由走 hash；`stamp` 见 M9 |
| M9 | 页面镜像到**输出目录** `wwwroot/plugin-web/{pluginId}/{stamp}/web/`，`stamp` 每次重建镜像时重新生成（8 位随机 hex）；启用时构建、停用/卸载时整棵删除、宿主启动先清空 `plugin-web` 再按已启用插件重建 | 规避 `IsStaticsCached` 的按路径字节缓存；启动重建可自愈 `clean`/重新发布/只读部署 |
| M10 | zip 允许两种布局：根目录直接含 `plugin.json`，或仅有一个顶层目录且其中有 `plugin.json` | 用户自己压缩时两种都常见，界面以「顶层单目录」为标准写法 |
| M11 | 插件管理页做成与设置同级的浮层 | 与「知识库 / 设置」现有交互一致 |
| M12 | 页面资源只保证 `.html/.htm/.css/.js/.png/.jpg/.jpeg/.gif/.svg/.ico/.txt`；`.js` 可用 `type="module"`（MIME 为合法 JS 类型），但不得使用 `.mjs/.json/.map/.wasm/.woff/.woff2/.ttf/.webp`；需要字体请转 base64 内嵌 CSS，需要数据请写在 `.js` 里 | 白名单外的 MIME 实测返回 `unknown` |
| M13 | 不提供 `/plugin/page/...` 接口；页面元信息由 `/plugin/list` 一次带全 | SAEA 只按 `/{controller}/{action}` 派发，三段路由注册不上 |
| M14 | 路由常量放 WebHost 内部常量类，不改 `LubanAgentCore/Protocol/AgentApiRoutes.cs` | 这些接口不需要客户端 SDK 访问 |
| M15 | 交付页面镜像时校验：`web/` 下任何子目录名不得与宿主控制器同名（清单见硬约束 6），命中即拒绝安装并提示改名 | 否则该目录下的资源会被当路由 |
| M16 | （已确认，方案 B）新增 `--web-plugins-dir` / `LubanAgentWeb:WebPluginsDir` 作为 Web 插件根；`--plugins-dir` 语义不变，仍指客户端（Codex）插件目录。启动器：**保留原有 `--plugins-dir` 下发**，另在 `AgentClientOptions.WebPluginsDirectory` 有值时追加 `--web-plugins-dir`（缺省由宿主回落 `%LocalAppData%/LuBan/AIAgent/webhost/plugins`）；`AgentProtocolUnitTest` 原断言不变，只新增断言。宿主两根都扫描（Codex 插件在 Web 会话仍生效），Web 管理页只列/只管 Web 根 | 满足 D2/D3 的目录隔离，同时不改变 `--plugins-dir` 的既有含义；Codex 自身的插件扫描在本进程内独立运行（`LubanAgentCodex/App.axaml.cs:185`），不受影响 |
| M17 | （已确认，方案 B+D）升级：`requiresRestart` 为真时前置拒绝，不进框架；调用前把旧插件目录快照到系统临时目录，升级返回 `false` 或抛异常时按快照补回缺失文件（已存在的文件不覆盖）。卸载：框架 `UninstallAsync` 后若目录仍存在（已加载程序集被占用），回带提示「重启宿主后自动清理」。宿主运行时初始化时清理 Web 插件根下「无 `plugin.json` 且未登记」的孤儿目录；安装遇到同类残留目录时提示残留并给路径，不再笼统说「已存在，请改用升级」 | 框架升级/卸载是「先删目录」且不校验删除是否完整（`PluginManager.cs:413-416`、`UninstallAsync`），Windows 下已加载 DLL 被映射删除会中途失败，留下半删/空壳目录，导致同 id 永久装不回来 |

## 8. 功能规格

### 8.1 左侧栏「插件」入口

- 位置：`知识库` 与 `设置` 之间（即「设置上方」）。
- 点击打开插件管理浮层，hash 记 `#/plugins`；关闭回到打开前的视图（复用设置页 `lastView` 机制，`router.ts:33-34`）。
- 图标沿用现有 `Icon` 组件，新增 `plug` 图形（`web/src/components/Icons.tsx` 的图标表）。

### 8.2 插件管理页

三栏骨架复用设置页的 `SettingsLayout`（`web/src/pages/settings/Shared.tsx`）：

- 左列：插件列表项，主文本 `name`，次要小字 `version · 状态`。
- 右列（选中项）：
  - 基本信息：id、名称、版本、作者、描述、状态（已安装/已启用/已停用/错误 + `lastError`）。
  - 贡献摘要：技能数、MCP 数、页面入口（`webPage` 列表）。
  - 操作：启用 / 停用（二次确认，提示会注销技能与 MCP）/ 升级（选 zip）/ 卸载（二次确认，提示注销技能与 MCP、删除插件目录；程序集被占用时目录留到重启后自动清理）。内置插件（`isBuiltin`）禁用升级与卸载。
- 右列顶部固定「安装插件」区：
  - 文件选择（`.zip`）→ 上传安装，展示进度与结果。
  - **zip 结构说明（必须有）**：标准布局、`plugin.json` 必填字段、`lib/`、`web/`、`assets/`、`skills` 的作用；写明「必须有 DLL 且入口类型实现 `IPlugin`」「`webPage` 指向的 HTML 必须随包提供」「顶层单目录或根目录直放均可」「单包不超过 32MB」「安装后自动启用」。
  - 可复制的 `plugin.json` 最小示例（含 `uiContributions[].webPage`）。
- 列表为空时给出空态文案与安装引导。
- 页脚一行小字写明边界：「插件目录与状态文件与桌面端隔离；插件贡献的技能会写成当前工作区文件，桌面端打开同一工作区也会看到；MCP 只在当前 Web 进程内生效」。

### 8.3 安装包结构与校验

标准结构（界面上按此展示）：

```
my-plugin.zip
└─ com.example.myplugin/          # 顶层目录名随意，建议与 id 一致
   ├─ plugin.json                 # 必需
   ├─ lib/                        # 入口程序集及其依赖
   │  └─ Com.Example.MyPlugin.dll
   ├─ web/                        # 可选：自带页面（webPage 必须在此之下）
   │  ├─ index.html
   │  └─ app.js
   └─ assets/                     # 可选：图标等资源
```

`plugin.json` 最小示例：

```json
{
  "id": "com.example.myplugin",
  "name": "示例插件",
  "version": "1.0.0",
  "entryAssembly": "Com.Example.MyPlugin.dll",
  "uiContributions": [
    { "id": "main", "label": "示例", "icon": "plug", "webPage": "web/index.html" }
  ]
}
```

校验顺序与拒绝理由（全部回 400，文案面向用户、不带本机路径）：

1. Base64 非法 / 不是 zip / 条目数 > 500 / 解压总量 > 128MB / 单文件 > 32MB。体积按**解压后实际写入字节**累计（zip 头里的声明长度只做预筛），避免声明值造假绕过限额。
2. 解压后定位不到 `plugin.json`（两种布局都不满足）。
3. `PluginLoader.ReadManifest` 为 null → 提示清单解析失败；`PluginLoader.Validate` 不通过 → 透传 `ToSummary()`（缺必填、id 格式、版本格式、入口程序集不存在等）。
4. `id` 已在**桌面端根**登记或桌面端根下已存在同名目录 → 提示「已在桌面端（Codex）插件目录中，Web 端不能重复安装同一标识」（该插件在 Web 端既不能装也不能升级，必须先报这一条，不能回「请改用升级」）；`id` 已在 Web 根登记或 Web 根下已存在同名目录（含清单完整） → 提示改用升级；同名目录存在但**缺 `plugin.json`** → 判定为上次卸载残留，提示「重启宿主自动清理」并给出目录路径。
5. 每个 `webPage` 非空条目：必须以 `web/` 开头、`Path.GetFullPath` 后仍在插件目录内（拒绝 `..`、绝对路径、盘符、符号链接）、文件存在、扩展名 `.html/.htm`；不满足即拒绝安装（不静默忽略）。
6. `web/` 下子目录名命中控制器名（`M15`）→ 拒绝安装。
7. `web/` 内出现白名单外扩展名 → 仅警告（提示可能无法在 Web 端加载），不阻断安装。

解压先落系统临时目录下独立子目录，校验通过才复制到插件根；任何一步失败清理临时目录，不留半个插件。复制完成后 `ScanAsync([webRoot])` 登记，再 `EnableAsync`。**启用失败不算安装失败**：接口仍回成功，但 `warnings` 带一条「安装成功但启用失败：<原因>」，详情页可重试启用。

### 8.4 插件自带页面

- 声明：`uiContributions[].webPage`（M7）。空/缺省表示该条目在 Web 端无页面。
- 生效条件：插件 `Enabled` 且 `webPage` 校验通过。入口按启用插件与条目顺序追加到左侧栏底部那一组；图标用清单 `icon` 映射到现有图标集，未知取值回退通用图标。
- 打开：`#/plugin/{pluginId}/{entryId}`，主内容区渲染 `PluginPage`，内部 `<iframe src="{pages[].url}">` 占满主区、无 `sandbox` 属性（B1）。`entryId` 必须**精确命中**：匹配不到时直接回首页并提示「页面入口不存在或不可用」，不回落第一个页面（否则地址栏、侧栏文案与实际内容会不一致）。
- 资源：镜像路径带 `stamp`（M9），页面内相对路径天然可用；`/plugin/list` 返回的 `url` 永远指向当前镜像。URL 各段逐段转义（文件名里的空格、`#`、`?` 原样拼进地址会被截断）。
- 停用/卸载时删除该插件整个镜像目录；若当前正打开该插件页面，前端跳回首页并提示原因。
- 加载失败（404 / 401 / 白屏）时 iframe 外层给可读提示，不静默留白；401 说明浏览器会话已过期，提示重新取入口地址。
- 镜像写入失败（部署目录只读等）**不**让安装/启用失败：按「已安装、已启用，但 Web 页面不可用」处理，详情页回显原因，技能与 MCP 仍生效。

### 8.5 与宿主打通（B1）

- 插件页面与宿主同源，直接 `fetch` 现有 REST / SSE（Cookie 自动携带）；能力范围就是现有 API 全集。
- 硬边界：`/system` 特权动作只认固定 Bearer（`WebSystemService.cs:175-186`），插件页面拿 Cookie 调不动，**装插件不等于能关停服务**。
- 固定宿主令牌只存在服务端配置与自述文件里，不进 Cookie、不进前端。
- 安装说明里明示风险：插件页面与宿主同源，脚本可访问浏览器存储与页面 DOM；只安装可信来源的插件。

## 9. 接口设计

统一走 `WebApi.RunGuardedAsync` 与 `ApiEnvelope`；路由常量放 WebHost 内部常量类 `Hosting/WebPluginRoutes.cs`（M14）。

| 方法 | 路由 | 入参 | 出参 |
|------|------|------|------|
| GET | `/plugin/list` | — | `rootPath` + `items[]` |
| POST | `/plugin/install` | `{ fileName, contentBase64 }` | `{ id, state, warnings[] }`（自动启用失败时 `warnings` 带原因，安装仍算成功） |
| POST | `/plugin/upgrade` | `{ id, fileName, contentBase64 }` | `{ id, state, warnings[] }` |
| POST | `/plugin/enable` | `{ id }` | 操作后的 `state` |
| POST | `/plugin/disable` | `{ id }` | 操作后的 `state` |
| POST | `/plugin/uninstall` | `{ id }` | `{ id, state, warnings[] }`（目录未删净时带重启提示） |

不提供单个插件的页面元信息接口（M13）。

`PluginItemDto`：`id, name, version, author, description, state, isBuiltin, requiresRestart, lastError, skills[], mcpServers[], pages[]`；`pages[]` 元素 `{ entryId, label, icon, webPage, url }`，`url = /plugin-web/{id}/{stamp}/web/...`。状态用字符串（`installed/enabled/disabled/error`）。

升级流程（M17）：

1. `requiresRestart` 为真 → 直接 400「旧版本程序集尚未释放，请重启宿主后再升级」，不进框架。
2. 否则把旧目录快照到系统临时目录，再调 `UpgradeAsync`；返回 `false` 或抛异常时按快照补回缺失文件。
3. 返回 `false` 且仍 `requiresRestart` → 回重启提示；否则若有补回文件，回「旧版本文件仍被占用，已恢复插件目录」；再否则回通用失败文案。

卸载：框架 `UninstallAsync` 返回 `true` 后仍尝试删一次目录；目录还在就回「插件已停用并注销；程序集文件仍被宿主占用，重启宿主后会自动清理残留目录」。宿主运行时初始化（`AgentWebRuntime` 首次请求）在 `RebuildMirrorsAsync` 里清理 Web 插件根下无 `plugin.json` 且未登记的孤儿目录。

## 10. 后端改动点

新增（`LubanAgentWebHost`）：

- `Services/WebPluginService.cs`：列表 / 安装 / 升级 / 启停 / 卸载；zip 解压与校验；页面镜像同步（含 `stamp` 管理）。
- `Controllers/PluginController.cs`：只做路由与透传（对齐 `AttachmentController`）。
- `Hosting/WebPluginPaths.cs`：插件根、状态文件、镜像根（`AppContext.BaseDirectory/wwwroot/plugin-web`）的解析与路径内校验（纯函数，便于单测）。
- `Hosting/PluginPageManifest.cs`：读原始 `plugin.json` 取 `uiContributions[].webPage`，并做路径归一化与越界校验。

改动：

- `Hosting/WebHostSettings.cs`：新增 `WebPluginsDir`（`LubanAgentWeb:WebPluginsDir` / `--web-plugins-dir`）；`PluginsDir` 语义不变。
- `Program.cs`：`BuildConfiguration` 之后，若 `LuBanAgent:Plugins:StateFile` 为空则写入 M2 路径（配置已显式给出时不覆盖）。
- `Hosting/AgentWebRuntime.cs`：`PluginBootstrap.InitializeAsync` 传入「Web 插件根 + 桌面端插件根」两个根（`:150`）；随后清空并重建插件页面镜像（首次用户初始化时执行，单机单用户，重复执行幂等）。
- `Hosting/WebPluginRoutes.cs`：登记上表路由（WebHost 内部，不进 `LubanAgentCore/Protocol/AgentApiRoutes.cs`）。
- `LubanAgentCore/Plugins/PluginBootstrap.cs`：新增多根重载（现有单根签名保持）。
- `LubanAgentCore/Client/AgentClientOptions.cs` + `ServerProcessLauncher.cs`：新增 `WebPluginsDirectory`，在原有 `--plugins-dir` 之外追加下发 `--web-plugins-dir`；原断言不变，新增断言。

不改：`luban-framework` 任何文件；`LubanAgentCodex` 任何文件。

## 11. 前端改动点

- `web/src/lib/router.ts`：新增 `{ name: 'plugins' }` 与 `{ name: 'plugin'; pluginId; entryId }`；`openPlugins()`、`openPlugin()`。
- `web/src/App.tsx`：主区渲染分支加插件页；`#/plugins` 与设置同样按浮层处理。
- `web/src/pages/SessionList.tsx`：底部新增「插件」按钮（知识库与设置之间）；底部再按插件页面条目追加动态入口。
- `web/src/pages/plugins/`：`PluginsOverlay.tsx`、`PluginList.tsx`、`PluginDetail.tsx`、`InstallPanel.tsx`（含 zip 结构与示例说明）、`PluginPage.tsx`（iframe 宿主）。
- `web/src/stores/plugins.ts`：插件列表 store，启动加载一次，安装/启停/卸载后刷新；侧栏入口从这里派生。
- `web/src/lib/plugins.ts`：接口封装（沿用 `post` 的 `{payload}` 包装，`api.ts:87-98`）。
- `web/src/styles.css`、`web/src/components/Icons.tsx`：样式与 `plug` 图标。
- `web/vite.config.ts`：`apiPrefixes` 增 `/plugin` 与 `/plugin-web`（开发模式 iframe 也要能加载镜像页面）。

`LubanAgentWebHost/wwwroot/` 是仓库跟踪的构建产物：改完前端需 `npx tsc --noEmit` + `npx vite build` 并保留产物。插件镜像只写输出目录，**不**写源码树，也不进 git；`emptyOutDir` 只清源码树产物，不影响输出目录镜像。

## 12. 权限与安全

- 全部接口经 `RunGuardedAsync`（默认校验令牌 + 等用户初始化）。
- 路径安全：所有插件路径先 `Path.GetFullPath` 再校验前缀；zip 解压拒绝 `..`、绝对路径、盘符、符号链接条目（防 zip-slip）；`webPage` 单独再校验一次。
- 上传体积服务端二次校验，不信任前端。
- 回传错误不含本机绝对路径（沿用 `WebApi.SafeErrorMessage`）。`/plugin/list` 会回传插件根目录（本机绝对路径），仅限本机单用户场景，且该接口本身要过门禁。
- 同源无沙箱的风险在安装说明与详情页明示。

## 13. 风险与对策

1. **升级后页面仍是旧内容** → 镜像路径带 `stamp`（M9）；不用同路径覆盖。
2. **输出目录被重建或只读** → 启动重建自愈；写入失败降级为「页面不可用」，不影响技能/MCP。
3. **程序集共享集不含 `LubanAgent.*`** → 安装说明写明「插件只引用 `LuBan.AIAgent`」，失败以 `lastError` 呈现。
4. **升级时旧程序集未释放** → 框架会卸载校验并中止，但它的「先删旧目录再拷新包」在文件被占用时会删到一半（Windows 文件映射）；Web 侧改为「前置拒绝 + 目录快照回补」（M17）。
5. **卸载后目录残留**（已加载 DLL 被占用，框架删除失败只记日志）→ 卸载接口回带重启提示，宿主启动时按「无 `plugin.json` 且未登记」清理（M17）。
6. **`--user` 下插件作用域错位**（硬约束 9）→ 不修，页面与文档写明「Web 插件只对默认用户生效」。
7. **技能落工作区文件、跨客户端可见**（结论 4）→ 不修，文案如实说明（技能共享、MCP 不共享）；如后续要求强隔离，需改 Core 的技能写入路径，属独立议题。
8. **页面子目录与控制器同名** → 安装期拒绝（M15）+ 说明。
9. **白名单外资源 200 但 MIME=unknown** → 安装期警告 + 说明；`web/` 之外的资源不进镜像。

## 14. 验收清单

1. 左侧栏在「设置」上方出现「插件」；点击打开浮层，关闭回到原视图。
2. 上传符合结构的 zip → 安装成功并自动启用；列表状态为已启用。
3. 上传缺 `plugin.json`、id 非法、缺 `entryAssembly`、条目/体积超限的包 → 分别得到可读拒绝原因，不留半个插件目录。
4. 同 id 再装 → 提示改用升级；升级后版本号变化、原启用状态保持，**插件页面内容是新版**（验证 `stamp` 生效）。对已启用插件直接升级 → 若旧程序集未释放则 400「请重启宿主后再升级」，且插件目录文件数不变（不半删）。
5. 停用后：插件页面从左侧栏消失、镜像目录被删除、技能与 MCP 不再生效；再启用恢复。
6. 卸载后：列表移除、状态记录清除、镜像删除；若程序集仍被占用，回带「重启后自动清理」提示，重启宿主后残留目录被清理、同 id 可重新安装。内置插件不可卸载/升级。
7. 有 `webPage` 的插件启用后出现侧栏入口，点开在主内容区显示页面；页面内 `fetch('/system/bootstrap')` 可用（Cookie 鉴权）；`type="module"` 的 `.js` 能正常加载。
8. 插件页面调 `/system` 特权动作被拒（401）。
9. 停用/卸载时正打开该插件页面 → 自动回首页并提示。
10. Web 插件目录与 `plugin-states.json` 与桌面端互不相同；启动 WebHost 时仍收到 `--plugins-dir`，并新增 `--web-plugins-dir` 指向 Web 插件根（M16）。
11. `dotnet build LubanAgent.slnx` 通过；`dotnet test LubanAgent.Tests` 通过（新增 `WebPluginPaths`、`PluginPageManifest`、zip 校验、升级失败回补、`ServerProcessLauncher` 断言）；前端 `npx tsc --noEmit`、`npx vite build` 退出码 0。

## 15. 审查取证记录（本次）

| 结论 | 取证方式 |
|------|----------|
| `PathHelper.GetCurrentPath()` 返回输出目录本身（非上一级） | 读 `SAEA.Common/IO/PathHelper.cs:46-64` + 输出目录实测（`bin/Debug/net10.0/`） |
| 运行时新增的镜像文件能被静态通道命中 | 构造 `HttpUtility("wwwroot")` 后新建文件再 `MapPath`，`File.Exists` 为真 |
| `.js` 为 `application/x-javascript`，`.json/.mjs/.woff2/.wasm/.map` 为 `unknown` | 直接调用 `HttpMIME.GetType` 实测 |
| 静态字节按路径缓存、默认开启 | `SAEAMvcApplicationConfig.cs:58-61` + `HttpFileResult.cs:86-95` |
| 3 段以上 URL 会被当控制器路由 | `SAEA.MVC/HttpContext.cs:200-275` |
| 默认请求体上限 64MB、不支持 chunked | `HttpRequestSplitter.cs:68-82` |
| 启动器当前把客户端插件目录下发给 WebHost | `ServerProcessLauncher.cs:160-189` + 单测断言 |
| `plugin.json` 未知成员（`webPage`）被丢弃 | `PluginManifest.cs:82-83` + `PluginSidebarEntry.cs` 字段集合 |
| 插件技能落工作区文件、MCP 只进会话注册表 | `AgentPluginHost.cs:229-243`（技能写入）、`:282-320` / `:464-489`（MCP 注册进会话 `MCPRegistry`） |

## 16. 留给实现者

- 具体 CSS 尺寸、列表与详情在窄屏下的折叠方式。
- 错误文案措辞、加载态与进度条形态（大包 Base64 上传建议显示百分比）。
- `PluginPage` iframe 的 `title`、`referrerpolicy`、刷新按钮等细节。
- 是否在详情页展示 `dependencies` 缺失的具体原因（框架已给汇总文本，直接透传）。