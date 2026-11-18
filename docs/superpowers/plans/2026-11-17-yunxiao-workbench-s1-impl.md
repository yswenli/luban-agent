# 实施计划 · 云效 DevOps 工作台 Web 插件 S1（纵切：连接 + 项目列表）

| 项 | 内容 |
|----|------|
| **上游** | `docs/实施设计-云效DevOps工作台Web插件-S1.md`（深度审查后 v2） |
| **分支** | `feature/yunxiao-workbench-s1` |
| **测试栈** | MSTest（`LubanAgent.Tests`）；前端无头断言脚本 |
| **纪律** | 强制红绿 TDD：先写失败测试 → 看红 → 最少实现 → 看绿 → 提交；两段式评审（规格符合度 + 代码质量） |

> 任务粒度按 superpowers writing-plans 规范：每个任务 2~5 分钟可完成，文件路径精确、改法无歧义、有明确验证命令。

---

## 批次 A · 地基（T1~T3）

### 任务 1：YunxiaoSettingsService 移入 Core（纯移动，签名不变）

- **文件路径**：
  - 移动 `LubanAgentCodex/Security/YunxiaoSettingsService.cs` → `LubanAgentCore/Connectors/AliyunDevOps/YunxiaoSettingsService.cs`
  - 修改引用方 3 处：`LubanAgentCodex/App.axaml.cs`、`LubanAgentCodex/ViewModels/Settings/YunxiaoTabViewModel.cs`、`LubanAgentCodex/Views/Settings/SettingsWindow.Editors.Global.cs`
- **要做的**：
  1. 移动文件，把文件头注释的「命名空间：LubanAgentCodex.Security」改为「LubanAgentCore.Connectors.AliyunDevOps」；`namespace LubanAgentCodex.Security;` → `namespace LubanAgentCore.Connectors.AliyunDevOps;`。
  2. 类体 **一字不改**（`GetOrganizationId` / `HasPat` / `SaveAsync(string? organizationId, string? pat = null, ...)` / `ApplyPersistedOrganizationId` 全部保持原签名）。
  3. 三个引用方：删掉 `using LubanAgentCodex.Security;`（若只为该类），补 `using LubanAgentCore.Connectors.AliyunDevOps;`；`App.axaml.cs` 里 `services.AddSingleton<Security.YunxiaoSettingsService>()` 改为 `services.AddSingleton<LubanAgentCore.Connectors.AliyunDevOps.YunxiaoSettingsService>()`（或加 using 后短名）。
  4. 该文件原引用的 `ICredentialStore`、`YunxiaoApiOptions`、`AliyunCredentialKeyAliases` 都在 Core，移动后无需新依赖。
- **验证**：`dotnet build LubanAgent.slnx --nologo -v q` 全绿；`dotnet test LubanAgent.Tests --nologo -v q` 全绿（回归）。

### 任务 2：C28——WebHost 注册可写 CredentialStore（红绿见任务 3 的钉死用例）

- **文件路径**：`LubanAgentWebHost/Program.cs`（`AgentHostBuilder.Build(...)` 的 `services => { ... }` 回调内，紧挨现有 `services.AddScoped<HostOrchestrationProgressSink>()` 一带）
- **要做的**：插入三行（照抄桌面端 `LubanAgentCodex/App.axaml.cs` 的替换段，去掉 CodexYunxiaoCredentialProvider 那行）：
  ```csharp
  // C28：可写凭据存储覆盖框架默认只读实现（ConfigurationCredentialStore.SetAsync 抛异常）；
  // 云效设置服务供 YunxiaoController 使用。
  services.AddSingleton<LubanAgentCore.Security.CredentialStore>();
  services.ReplaceSingleton<ICredentialStore>(d => d.GetRequiredService<LubanAgentCore.Security.CredentialStore>());
  services.AddSingleton<LubanAgentCore.Connectors.AliyunDevOps.YunxiaoSettingsService>();
  ```
  using 说明：`ICredentialStore` 在框架 `LuBan.AIAgent.Plugins.Security`；`ReplaceSingleton` 扩展在 **`Microsoft.Extensions.DependencyInjection.Extensions`**（WebHost 若无全局 using 需显式补这一行）。
- **验证**：编译过；任务 3 的「SetAsync 真落盘」用例红→绿（该用例在任务 3 才写，故本任务先编译验证，红绿闭环在任务 3 合并验收）。

### 任务 3：设置服务单测（红绿 TDD）

- **文件路径**：`LubanAgent.Tests/YunxiaoSettingsServiceUnitTest.cs`（新建）
- **要做的**：MSTest（`[TestClass]` + `[TestMethod]`，风格照 `AliyunCredentialKeyAliasesUnitTest.cs`）。被测类 = `YunxiaoSettingsService` + **手写内存假 `ICredentialStore`**（Dictionary 实现四个成员 `GetAsync/SetAsync/Contains/Keys`，约 20 行，放测试文件内）+ 真实 `YunxiaoApiOptions`。**禁止用真实 `CredentialStore`**：其 `Config` 经 `UserContextRegistry.GetOrCreate` 走 `ConfigManager.GetDefaultConfigPath(userId)` 会写真实 LocalApplicationData，且 `UserContextRegistry(DiContainer root)` 的 `Build` 写死默认路径、无法注入临时目录（已核实 `LubanAgentCore/Configuration/UserContextRegistry.cs:87-93`）。用例集：
  1. `SaveAsync_PatNull_KeepsExisting`：先存 pat="old"，再 `SaveAsync(orgId, pat: null)`，断言 PAT 仍为 "old"。
  2. `SaveAsync_PatEmpty_Clears`：`SaveAsync(orgId, pat: "")` 后 `HasPat()` 为 false。
  3. `SaveAsync_PatValue_Overwrites`：存 "a" 再存 "b"，读回为 "b"。
  4. `SaveAsync_WritesCanonicalKeys`：存入后假存储里 `connector:yunxiao:token`、`connector:yunxiao:organizationid` 两键命中（`YunxiaoSettingsService` 直写规范键，别名归一是 `CredentialStore` 层的事，假存储按字面键断言）。
  5. `HasPat_NeverLeaksPlaintext`：`HasPat()` 只返回 bool（断言方法签名与返回类型即可）。
  6. `SaveAsync_SetAsyncDoesNotThrow`：`SaveAsync` 全程不抛 `NotSupportedException`（假存储不抛即过；真正的 C28 钉死在下面 DI 形状用例）。
  7. `SaveAsync_Idempotent`：同值重复保存两次不抛、值不变。
  8. `WebHostCredentialStoreRegistration_ResolvesWritableImplementation`：**钉死 C28 的 DI 形状**——照 `Program.cs` 将写入的注册代码搭一个最小 `ServiceCollection`（`AddSingleton<CredentialStore>()` + `ReplaceSingleton<ICredentialStore>(...)`），断言 `GetRequiredService<ICredentialStore>() is LubanAgentCore.Security.CredentialStore`（可写），而非框架默认 `ConfigurationCredentialStore`。**不调 SetAsync、不落盘**。
- **验证**：`dotnet test LubanAgent.Tests --filter YunxiaoSettingsServiceUnitTest --nologo` 先红（若任务 1/2 已完成则直接绿——此时按「补测试钉行为」处理，逐用例核对断言确实会失败：临时改一处断言看红再改回）→ 全绿。

---

## 批次 B · Projex 客户端（T4）

### 任务 4：YunxiaoProject 模型 + ProjexClient.SearchProjectsAsync（红绿 TDD）

- **文件路径**：
  - `LubanAgentCore/Connectors/AliyunDevOps/YunxiaoModels.cs`（追加 `YunxiaoProject`）
  - `LubanAgentCore/Connectors/AliyunDevOps/ProjexClient.cs`（新建）
  - `LubanAgentCore/Connectors/AliyunDevOps/AliyunDevOpsConnectorServiceCollectionExtensions.cs`（注册）
  - `LubanAgent.Tests/ProjexClientUnitTest.cs`（新建）
- **要做的**：
  1. `YunxiaoProject`：与 `YunxiaoRepository` 同风格（`[JsonPropertyName]`），字段 `Id`、`Name`、`Identifier`（标识）、`UpdateTime`、`Description`——~~字段名以实测为准，T11 校正~~ **已校正（2026-11-18）**：真机字段为 `customCode`（标识）/ `gmtCreate`/`gmtModified`（epoch 毫秒）/ `status`/`creator` 内嵌对象，`YunxiaoProject` 已按此重写，页面投影 `YunxiaoProjectItemDto` 保持 identifier/updateTime 口径。
  2. `ProjexClient`：构造签名照抄 `CodeupClient`（`YunxiaoApiOptions` + `IYunxiaoCredentialProvider` + `PluginPermissionGuard?` + `string? connectorName`）；`HttpClient` 初始化、token 头注入、超时/重试从 `YunxiaoApiOptions` 读——**复制 CodeupClient 的私有辅助方法**（发请求、解 `YunxiaoApiResponse<T>`、错误映射），不新造抽象。
  3. `SearchProjectsAsync(string organizationId, int page, int perPage, string? keyword, CancellationToken)` → `YunxiaoPagedResult<YunxiaoProject>?`：POST `https://{Endpoint}/oapi/v1/projex/organizations/{organizationId}/projects:search`，body 含 `page`/`perPage`/可选 `conditions`（~~字段名 T11 校正~~ **已校正**：名称检索即 conditions 的 conditionGroups JSON 串，无 keyword 字段）；`perPage` 上限 **200** 截断（实测界值 [1,200]）。
  4. DI 注册照抄 CodeupClient 三参形态（含 `PluginPermissionGuard.Default` 兜底）。
  5. 单测（**照仓库先例 `YunxiaoResponseParserUnitTest`：测纯函数，不引 `HttpMessageHandler` 桩**）：请求装配抽纯函数 `BuildSearchRequest(organizationId, page, perPage, keyword)` 返回 URL + body 对象——测 URL 形态（`projects:search`）、perPage=200 截到 100、keyword 透传/缺省；解析侧复用 `YunxiaoResponseParser.JsonOpts` 反序列化真机形态 JSON 夹具——测 `YunxiaoPagedResult<YunxiaoProject>` 的 Items/Total 映射与错误形态（`ParseBusinessError` 同口径）。token 头属 `HttpClient` 装配面，以代码走查 + T11 实机探活覆盖。
- **验证**：`dotnet test LubanAgent.Tests --filter ProjexClientUnitTest --nologo` 红→绿；全量 build 绿。

---

## 批次 C · 服务编排与 REST（T5~T6）

### 任务 5：WebYunxiaoService 时序编排（红绿 TDD）

- **文件路径**：`LubanAgentWebHost/Services/WebYunxiaoService.cs`（新建）、`LubanAgent.Tests/WebYunxiaoServiceUnitTest.cs`（新建）
- **要做的**：**两层**——`YunxiaoWorkbenchService`（实例类，构造注入 `YunxiaoSettingsService`/`ProjexClient`/`IYunxiaoCredentialProvider`，承载全部逻辑，可单测）+ `WebYunxiaoService`（`public static class` 薄壳，照抄 `WebPluginService` 惯例：每方法 `WebApi.RunGuardedAsync` + `TryReadPayload` + `OkJson`/`FailJson`，经 `WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId)` 解析实例类后转调——已核实 `WebPluginService.cs:58` 同款取 DI）。四个方法（逻辑都在实例类）：
  1. `GetSettingsAsync()`：`{ organizationId, hasPat }`（不回明文）。
  2. `TestConnectionAsync(string payload)`：payload `{ organizationId, pat? }`；pat 省略用已存 PAT（经 `IYunxiaoCredentialProvider.GetPatAsync`）；**探活 = `ProjexClient.SearchProjectsAsync(orgId, page: 1, perPage: 1, keyword: null)` 一发**（顺带 projectCount）；不写任何配置。回 `{ ok, projectCount, message? }`。
  3. `SaveSettingsAsync(string payload)`：payload `{ organizationAddress?, organizationId, pat? }`；address 非空时**从地址解析 orgId**（`https://devops.aliyun.com/orgs/{id}` 取末段，解析不出 400）；先探活后保存，探活失败 `FailJson(409, "连接校验未通过，未保存")`；保存调 `YunxiaoSettingsService.SaveAsync(orgId, pat)`（pat 缺省传 null=保持原值）。
  4. `ListProjectsAsync()`：`WebApi.QueryInt("page")`/`QueryInt("perPage")`/`Query("keyword")`（默认 page=1、perPage=20、上限 **200**）；透传 `ProjexClient.SearchProjectsAsync`，回 `{ items, total, page, perPage }`。
- **单测**（直接 new `YunxiaoWorkbenchService`，注入假 `ProjexClient`/假 `YunxiaoSettingsService`——两者都是普通类，测试里写最小假实现（方法 `virtual` 或提取接口 `IProjexClient` 二选一，**倾向提取接口**，生产注册不变）；`IYunxiaoCredentialProvider` 是现成接口直接假。壳层 `WebYunxiaoService` 不单测（静态 Runtime 起不来，壳只有转发）：
  1. 探活失败 → 409 且 `SaveAsync` 一次都没被调。
  2. 探活成功 → `SaveAsync(orgId, pat)` 收到正确参数。
  3. payload 解析失败 → 400。
  4. pat 缺省时探活用已存 PAT。
  5. ListProjects 的 page/perPage 默认与上限截断。
  6. 响应壳 `success` 字段与错误码正确。
- **验证**：`dotnet test LubanAgent.Tests --filter WebYunxiaoServiceUnitTest --nologo` 红→绿。

### 任务 6：YunxiaoController + 保留名

- **文件路径**：`LubanAgentWebHost/Controllers/YunxiaoController.cs`（新建）、`LubanAgentWebHost/Hosting/WebPluginPaths.cs`（改 `HostControllerNames`）
- **要做的**：
  1. 控制器四动作照设计文档 §4.4（薄透传，`[HttpGet]`/`[HttpPost]`，POST 参 `string payload`）。
  2. `HostControllerNames` 集合补 `"yunxiao"`（现 14 个 → 15 个）。
- **验证**：build 绿；单测：`IsHostControllerDirectoryName("yunxiao")` 断言 true——加进**已有**的 `LubanAgent.Tests/WebPluginHostUnitTest.cs`（它就是 `WebPluginPaths` 的测试，已核实），不新建文件。

---

## 批次 D · 前端页面（T7~T10）

### 任务 7：api.js + app.js（路由与门禁）

- **文件路径**：`LubanAgent.Plugin.YunXiao/web/assets/api.js`、`app.js`（新建）
- **要做的**：
  - `api.js`：`get(path, params)` / `post(path, body)`，双层包裹 `JSON.stringify({ payload: JSON.stringify(body) })`、`Content-Type: application/json; charset=utf-8`、`credentials: 'same-origin'`，解 `ApiEnvelope`（`success/code/message/data`）——照抄宿主 `web/src/lib/api.ts` 的语义改写成无构建 JS。
  - `app.js`：hash 路由 `#/connect` / `#/projects`；启动时 `GET /yunxiao/settings`，`hasPat=false` 强制进 `#/connect`（进 `#/projects` 弹回）；屏容器 show/hide 切换。
- **验证**：断言脚本任务 10 覆盖；手验：直接开 `index.html` 看两屏切换与门禁。

### 任务 8：屏 1 connect.js

- **文件路径**：`LubanAgent.Plugin.YunXiao/web/assets/connect.js`、`web/index.html`（表单骨架改造）、`web/assets/base.css`（补令牌）
- **要做的**：
  - 表单：组织访问地址（输入即解析 orgId 回填，可手改）+ 组织 ID（只读回填）+ PAT（`type="password"`，不回显不落 DOM）。
  - 「测试连接」按钮 → `POST /yunxiao/testconnection`；「保存」按钮**测试通过前 disabled**（D7 前端半边），点击 → `POST /yunxiao/savesettings`；成功提示后跳 `#/projects`。
  - 错误态：401/409/400 的 `message` 直接显示（服务端已脱敏）。
- **验证**：断言脚本（任务 10）门禁用例。

### 任务 9：屏 2 projects.js

- **文件路径**：`LubanAgent.Plugin.YunXiao/web/assets/projects.js`、`web/index.html`
- **要做的**：
  - 左栏：搜索框（`keyword` 传 ListProjects）+ 项目列表（`单行省略 + title 悬停`、栏内滚动）；右栏两态：未选 → 组织统计（项目总数=total）；选中 → 项目详情卡（name/identifier/updateTime）。
  - **不画**其他功能入口（D9）；栅格 `min-width: 0`、无横向滚动。
- **验证**：断言脚本长文本/滚动用例。

### 任务 10：前端断言脚本 + 全量回归

- **文件路径**：`LubanAgent.Plugin.YunXiao/tests/assert-s1.js`（新建；无头 Edge 注入探针手法，不进 web/ 包）
- **要做的**：断言清单（设计文档 §6.2 六条）：画板 826×694 无溢出、保存按钮门禁、长项目名单行省略、列表栏内滚动、hasPat=false 弹回 connect、无横向滚动。探针回传 `PROBE::` title JSON 判 PASS/FAIL。
- **验证**：`node LubanAgent.Plugin.YunXiao/tests/assert-s1.js`（或对应驱动命令）全 PASS；`dotnet test LubanAgent.Tests --nologo` 全绿。

---

## 批次 E · 实机校正（T11）

### 任务 11：真实云效探活校正 + 端到端手验

- **文件路径**：`ProjexClient.cs` / `YunxiaoModels.cs`（按实测修 URL/字段）
- **要做的**：用真实 PAT 发一次 `projects:search`（`curl` 或临时单测），校正：URL 形态、请求体字段名、响应字段名（items 映射）、分页上限。**未实机验证前不许宣称完成**；失败按设计 §3.3 报「能力缺口」。**✅ 已完成（2026-11-18 真机取证）**：URL 形态正确；检索走 conditions（非 keyword）；响应为裸数组 + X-Total/X-Page/X-Per-Page 头；分页界值 [1,200]；标识字段 customCode、时间 gmtCreate/gmtModified（epoch 毫秒）。校正提交：`fix: 按真机取证校正 Projex 请求装配与响应解析（T11）`。
- **验证**：真实探活回执贴进本计划附录；端到端手验：屏 1 测试连接→保存→屏 2 列表出真实项目。

---

## 评审卡点（两段式）

每批完成后：
1. **规格符合度**：对照设计文档 §3/§4 逐条核对（契约字段名、时序、错误码）。
2. **代码质量**：DRY（复用 CodeupClient 辅助方法而非复制粘贴失控）、YAGNI（不做二期才用的写接口）、脱敏无明文。

Critical（阻断）：探活时序可被绕过、PAT 明文落日志/DOM、C28 未替换导致保存抛异常、契约字段名与设计不符。
