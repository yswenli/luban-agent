# 实施设计 · 云效 DevOps 工作台 Web 插件（S1 纵切）

| 项 | 内容 |
|----|------|
| **文档类型** | 实施设计文档（superpowers 头脑风暴产出，经用户逐块确认） |
| **上游文档** | `docs/PRD-云效DevOps工作台Web插件.md`（v1.0）、`docs/界面设计稿-宿主尺寸预览.html`（826×694 十屏） |
| **日期** | 2026-11-17 |
| **状态** | 待用户签字确认；确认后进「隔离工作区 + 实施计划」 |
| **实现范围** | 切片 S1：云效连接设置 + 项目列表（端到端纵切） |

---

## 0. 决策记录（本次头脑风暴逐块确认）

| 编号 | 决策点 | 结论 | 备注 |
|------|--------|------|------|
| D1 | 实现范围切法 | **纵切先行**：S1 连接 + 项目列表端到端，再按 S2~S4 扩展 | 排除「六界面全量」「先 M2 接线层」 |
| D2 | 宿主 REST 形态 | **专用控制器群**（yunxiao 前缀），薄路由 + 业务 Service，不走通用工具转发端点 | 排除「统一 invoke 端点」「读写分流混合式」 |
| D3 | Projex 接线程度 | **读写全接线**（总体能力口径）；S1 只落地 `projex_list_projects`，写接口随 S3 落 | 排除「只读接线」「本期全部禁用占位」 |
| D4 | 测试策略 | **服务端 TDD（MSTest）+ 前端无头断言脚本**，不给插件 web/ 加构建链 | 排除「仅服务端 TDD」「引入前端测试框架」 |
| D5 | S1 边界 | **连接屏 + 项目列表屏**一起交付，含本仓第一个 Projex 调用 | 排除「仅云效连接屏」 |
| D6 | 凭据写入（C22）形态 | **云效专用 settings 端点**（只接组织地址 + PAT 两键） | 排除「通用凭据写入端点」 |
| D7 | 校验与保存时序 | **先校验后保存**：测试连接通过前保存按钮禁用；服务端保存前强制再探活一次 | — |
| D8 | 屏 2 项目列表数据源 | **Projex 研发项目**（`projects:search`），代码库/流水线归项目详情内再查 | 排除「项目 + 代码库双层」 |
| D9 | S1 未实现功能入口 | **本期不画入口**：S1 页面只呈现连接屏与项目列表屏 | 与十屏设计稿全貌的差异见 §8 差异表 |

---

## 1. 范围与不做

### 1.1 S1 交付

1. **屏 1 云效连接**（设计稿屏 1）：组织地址 + PAT 表单、测试连接、保存；凭据落宿主 `CredentialStore`。
2. **屏 2 项目列表**（设计稿屏 2）：左栏 Projex 研发项目列表（搜索 + 滚动），右栏未选项目时组织统计 / 选中后项目详情卡。
3. **宿主 REST 面**：`YunxiaoController`（settings 读写、连接校验、项目列表）。
4. **ProjexClient**：本仓首个 Projex 客户端（`LubanAgentCore`），只含项目列表/详情只读方法，形态对齐 `CodeupClient`。
5. **测试**：MSTest 服务端用例（红绿 TDD）+ 前端无头断言脚本。

### 1.2 S1 明确不做

| 不做项 | 归属 |
|--------|------|
| 项目初始化（母版复制、流水线复制、git 会话执行） | S2 |
| PRD 同步与拆分落库（Projex 工作项/迭代/用例/计划写接口） | S3 |
| 迭代看板、任务详情、任务工作区、迭代测试屏 | S4 |
| 紧凑插件页头（宿主侧改造，O11） | 宿主待办 |
| 通用凭据写入端点 | 不做（D6） |

---

## 2. 架构总览

```
┌──────────────────────────────────────────────────────────┐
│ LubanAgent.Plugin.YunXiao/web/（纯静态，无构建链）          │
│   index.html + assets/{base.css, api.js, app.js,          │
│                        connect.js, projects.js}           │
│   hash 路由：#/connect（屏1）  #/projects（屏2）            │
└───────────────┬──────────────────────────────────────────┘
                │ fetch 同源绝对路径，Cookie 自动携带
                │ POST 体：{ "payload": "<JSON 字符串>" }
                ▼
┌──────────────────────────────────────────────────────────┐
│ LubanAgentWebHost                                          │
│   YunxiaoController（薄路由：参数透传，业务在 Service）     │
│   WebYunxiaoService（static，照抄 WebPluginService 惯例：  │
│     RunGuardedAsync + 校验→保存时序、列表透传）            │
└───────────────┬──────────────────────────────────────────┘
                │ DI（沿用 AliyunDevOpsConnectorServiceCollectionExtensions；
                │    C28：Program.cs 内 ReplaceSingleton ICredentialStore
                │    → 可写 CredentialStore，见 §4.5）
                ▼
┌──────────────────────────────────────────────────────────┐
│ LubanAgentCore/Connectors/AliyunDevOps                     │
│   YunxiaoSettingsService（由 Codex 下沉共享，见 §4.1）      │
│   ProjexClient（新增，HttpClient 单例，对齐 CodeupClient）  │
│   CodeupClient / FlowClient / IYunxiaoCredentialProvider   │
│   LubanAgentCore.Security.CredentialStore（可写，键名见    │
│     §4.2；框架默认 ConfigurationCredentialStore 只读、     │
│     SetAsync 抛异常，必须被替换）                          │
└───────────────┬──────────────────────────────────────────┘
                │ x-yunxiao-token + organizationId
                ▼
        openapi-rdc.aliyuncs.com（云效中心版 OpenAPI）
```

**约束回放（插件页面必须遵守，来自插件管理设计与 PRD）：**
- 只调宿主 REST 绝对路径，不直连云效、不接触令牌明文；
- 同源 iframe，Cookie 自动携带（`credentials: 'same-origin'`）；
- POST 必须 `Content-Type: application/json; charset=utf-8`（SAEA.MVC 只在该子串精确匹配时才绑定 JSON 到动作参数）；
- 静态资源只用 html/css/js/图片白名单（mjs/json/map/wasm/woff 不可用）；hash 路由、无 SPA 回退。

---

## 3. 接口契约（S1 全集）

### 3.1 端点表

| 方法 | 路径（SAEA 惯例路由 `{controller}/{action}`） | 入参 | 出参 | 说明 |
|------|-----------------------------------------------|------|------|------|
| GET | `/yunxiao/settings` | — | `{ organizationId, hasPat }` | 回显配置；**PAT 只回布尔，绝不回明文**；组织地址由前端按 `https://devops.aliyun.com/orgs/{organizationId}` 回显（地址本身不落库） |
| POST | `/yunxiao/testconnection` | `payload: { organizationId, pat? }` | `{ ok, projectCount, message? }` | 只读探活，不落盘；`pat` 省略时用已存 PAT（设置页改地址不动令牌的场景） |
| POST | `/yunxiao/savesettings` | `payload: { organizationAddress?, organizationId, pat? }` | `{ saved: true }` | **服务端先探活，探活失败拒绝保存**；`organizationAddress` 仅用于解析/校正 organizationId 不落库；`pat` 空/缺省 = 保持原值（对齐 `YunxiaoSettingsService.SaveAsync` 语义） |
| GET | `/yunxiao/projects` | 查询串 `page`、`perPage`、`keyword?` | `{ items[], total, page, perPage }` | Projex 研发项目列表 |

> 路径形态说明：SAEA.MVC 为惯例路由（例：`PluginController.List()` → `/plugin/list`），URL 取控制器名去 `Controller` 后缀 + 动作名小写。原设计稿里的 `/yunxiao/settings/test` 归一为 `/yunxiao/testconnection`，其余语义不变。

### 3.2 通信壳（照抄宿主既有约定）

请求（前端 `post()`）：

```js
// 与宿主 web/src/lib/api.ts 的约定完全一致：
// SAEA.MVC 只在 Content-Type 精确含该子串时才绑定 JSON 到动作参数
fetch(path, {
  method: 'POST',
  headers: { 'Accept': 'application/json',
             'Content-Type': 'application/json; charset=utf-8' },
  body: JSON.stringify({ payload: JSON.stringify(body) }), // 双层包裹
  credentials: 'same-origin',                              // HttpOnly Cookie 鉴权
});
```

响应（`ApiEnvelope`）：

```json
{ "success": true,  "code": 200, "message": null,  "data": { } }
{ "success": false, "code": 400, "message": "组织地址无效", "data": null }
```

### 3.3 错误码约定

| 场景 | HTTP | message（已脱敏，不得含令牌/绝对路径） |
|------|------|----------------------------------------|
| payload 解析失败 | 400 | 请求格式错误 |
| 组织 ID/PAT 格式非法 | 400 | 组织 ID 无效 / 个人访问令牌格式非法 |
| 云效探活 401/403 | 401 | 令牌无效或权限不足，请检查 PAT |
| 云效限流 429 | 429 | 云效接口限流，请稍后重试 |
| 云效不可达/5xx | 502 | 云效服务暂不可用 |
| 保存前探活失败 | 409 | 连接校验未通过，未保存 |

---

## 4. 服务端设计

### 4.1 YunxiaoSettingsService 下沉 Core（消除两份逻辑）

现状：`YunxiaoSettingsService` 在 `LubanAgentCodex/Security/`（桌面端），WebHost 不能引用 Codex。**决策：把该类移入 `LubanAgentCore/Connectors/AliyunDevOps/`，Codex 改 using 后继续用，WebHost 直接注入**——避免两份设置逻辑漂移（排除「WebHost 再写一份」方案）。

移动后公开面不变（现有签名，WebHost 复用）：

```csharp
namespace LubanAgentCore.Connectors.AliyunDevOps;   // 自 LubanAgentCodex.Security 移入

/// <summary>云效连接配置读写门面：组织 ID + PAT（Codex / WebHost 共用，公开面保持现状不变）。</summary>
public sealed class YunxiaoSettingsService
{
    public YunxiaoSettingsService(ICredentialStore store, YunxiaoApiOptions options) { ... }

    public string GetOrganizationId();   // 优先 YunxiaoApiOptions.OrganizationId，其次凭据存储
    public bool HasPat();                // 只判已配与否，不回明文

    // 语义（桌面端设置页既有约定，WebHost 必须原样保留）：
    //   organizationId 空串 = 清空；pat = null 保持原值不变；pat 空串 = 清除。
    public Task SaveAsync(string? organizationId, string? pat = null, CancellationToken ct = default);

    public void ApplyPersistedOrganizationId();  // 启动回填 options（写入后运行时即时生效，无需重启）
}
```

移动影响面：桌面端共 4 处引用（`App.axaml.cs` 注册、`YunxiaoTabViewModel.cs`、`SettingsWindow.Editors.Global.cs`、自身），只改命名空间引用，行为零变化。**注意 `SaveAsync` 的组织参数是 organizationId（不是地址）**——组织访问地址只是输入侧解析出 ID 用，与 PRD FR-1 字段表口径一致。

### 4.2 凭据键名（沿用 AliyunCredentialKeyAliases 约定）

| 项 | 键 |
|----|----|
| PAT | `connector:yunxiao:token`（`DefaultTokenKey`） |
| 组织 ID | `connector:yunxiao:organizationid`（`DefaultOrganizationIdKey`；既有别名解析，**禁止新造键名**） |

### 4.3 ProjexClient（新增，对齐 CodeupClient 形态）

```csharp
namespace LubanAgentCore.Connectors.AliyunDevOps;

/// <summary>
/// Projex（研发项目/工作项域）OpenAPI 客户端。与 CodeupClient/FlowClient 同构：
/// HttpClient 单例、x-yunxiao-token 头、YunxiaoApiOptions.Endpoint 作 Host、
/// 构造签名对齐 CodeupClient（含 PluginPermissionGuard）。
/// </summary>
public class ProjexClient : IDisposable
{
    private readonly HttpClient _httpClient;

    // 构造签名照抄 CodeupClient 现状（含权限守卫与连接器实例名）：
    public ProjexClient(
        YunxiaoApiOptions options,
        IYunxiaoCredentialProvider credentialProvider,
        PluginPermissionGuard? permissionGuard = null,
        string? connectorName = null) { ... }

    /// <summary>项目搜索（S1 唯一所需方法）。POST /oapi/v1/projex/organizations/{organizationId}/projects:search</summary>
    public async Task<YunxiaoPagedResult<YunxiaoProject>?> SearchProjectsAsync(
        string organizationId, int page, int perPage, string? keyword,
        CancellationToken cancellationToken = default) { ... }

    /// <summary>项目详情（S1 预留，屏 2 详情卡用）。GetProjectInfo</summary>
    public async Task<YunxiaoProject?> GetProjectAsync(
        string organizationId, string projectId,
        CancellationToken cancellationToken = default) { ... }
}
```

要点：
- 入参映射以官方文档为准（PRD §4.13.3 行 C1/C2：`POST /oapi/v1/projex/organizations/{organizationId}/projects:search`、`GetProjectInfo`），**接口未实机验证**，接线后第一步跑真实探活，URL/参数按实测校正；
- 分页沿用客户端通用封装 `YunxiaoPagedResult<T>`（JSON 字段 `result/total/page/perPage`，定义在 `YunxiaoModels.cs`），`perPage` 初值 100、越界截断——projects:search 的真实分页上限**待实测校正**（Codeup 列表接口的 100/150 口径不可直接套用）；
- 错误映射：401/403 → 凭据异常、429 → 限流、5xx/网络 → 服务不可用（同 §3.3）；
- 新增模型 `YunxiaoProject`（id / name / 标识 / 更新时间等，字段按实测补）加进 `YunxiaoModels.cs`，与 `YunxiaoRepository` 同风格 `JsonPropertyName`。

DI 注册追加（`AliyunDevOpsConnectorServiceCollectionExtensions`，照抄 CodeupClient 注册形态）：

```csharp
services.TryAddSingleton(sp => new ProjexClient(
    sp.GetRequiredService<YunxiaoApiOptions>(),
    sp.GetRequiredService<IYunxiaoCredentialProvider>(),
    sp.GetService<PluginPermissionGuard>() ?? PluginPermissionGuard.Default));
```

### 4.4 YunxiaoController + WebYunxiaoService

```csharp
namespace LubanAgentWebHost.Controllers;

/// <summary>云效工作台 REST（薄路由，照抄 PluginController 形态：只做参数透传，业务在 WebYunxiaoService）。</summary>
public sealed class YunxiaoController : Controller
{
    [HttpGet]
    public Task<ActionResult> Settings() => WebYunxiaoService.GetSettingsAsync();

    [HttpPost]
    public Task<ActionResult> TestConnection(string payload) => WebYunxiaoService.TestConnectionAsync(payload);

    [HttpPost]
    public Task<ActionResult> SaveSettings(string payload) => WebYunxiaoService.SaveSettingsAsync(payload);

    [HttpGet]
    public Task<ActionResult> Projects() => WebYunxiaoService.ListProjectsAsync();
}
```

服务分两层（**审查修正**：`WebPluginService` 依赖静态 `WebApi.Runtime.Current`，壳层无法单测，故拆）：
- **`YunxiaoWorkbenchService`（实例类，可测）**：构造注入 `YunxiaoSettingsService`、`ProjexClient`、`IYunxiaoCredentialProvider`，承载全部时序与校验逻辑——单元测试只测它；
- **`WebYunxiaoService`（`public static class`，薄壳）**：照抄 `WebPluginService` 惯例——每方法 `WebApi.RunGuardedAsync` + `TryReadPayload` + `OkJson`/`FailJson`；GET 参数用 `WebApi.Query` / `WebApi.QueryInt` 取；经 `WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId)` 解析 `YunxiaoWorkbenchService` 后转调（与 `WebPluginService` 取 DI 的方式一致）。

核心时序（D7，都在 `YunxiaoWorkbenchService` 内）：

```csharp
// SaveSettingsAsync 伪码：
// 1) WebApi.RunGuardedAsync 内：TryReadPayload<SaveSettingsRequest>(payload, out req)
// 2) 格式校验（organizationId 非空、pat 若给则非空串）→ 失败 FailJson(400, ...)
// 3) 先探活（与 TestConnectionAsync 同一只读调用，pat 省略则用已存 PAT）
//      → 失败 FailJson(409, "连接校验未通过，未保存")
// 4) 调 YunxiaoSettingsService.SaveAsync(organizationId, pat) 落盘
//      （pat=null 保持原值——与桌面端设置页留空语义一致，禁止把 null 当清除）
// 5) WebApi.OkJson(new { saved = true })；日志与响应全程不含 PAT 明文
```

探活实现口径（修正 PRD C29 的「复用 TestAsync」表述）：`IConnectorManagementService.TestAsync` 测的是**已配置**连接器，不接受临时凭据，**不能直接复用**；实际探活是「以用户本次输入的 organizationId + PAT 建临时只读调用（建议 `ProjexClient.SearchProjectsAsync(page=1, perPage=1)` 一发探活，顺带拿到 projectCount），不写任何配置」。

**新增保留名**：把 `"yunxiao"` 加进 `WebPluginPaths.HostControllerNames`（现有 14 个保留名），防止插件 `web/` 子目录名与新控制器同名遮蔽路由。

### 4.5 C28：WebHost 可写凭据存储注册（保存链路的硬前提，原任务清单遗漏，本轮补上）

现状（已核实）：框架 `LuBanAgentExtensions.cs` 以 `TryAddSingleton<ICredentialStore>` 落**只读** `ConfigurationCredentialStore`，其 `SetAsync` 直接抛 `NotSupportedException`；可写实现 `LubanAgentCore/Security/CredentialStore.cs`（落全局 `config.json` 的 `Integrations`，含键别名归一与环境变量兜底）目前**只在桌面端 `App.axaml.cs` 替换注册**。WebHost 的 `Program.cs` **没有做这个替换**——不补上，`YunxiaoSettingsService.SaveAsync` 一写就抛。

修法（照抄桌面端 `App.axaml.cs` 的替换段，放进 `Program.cs` 的 `AgentHostBuilder.Build(...)` 服务回调里）：

```csharp
// 云效凭据：可写 CredentialStore 覆盖框架默认只读实现（C28）；
// 设置服务注册为单例，供 YunxiaoController 的 WebYunxiaoService 解析。
services.AddSingleton<CredentialStore>();
services.ReplaceSingleton<ICredentialStore>(d => d.GetRequiredService<CredentialStore>());
services.AddSingleton<YunxiaoSettingsService>();
```

注意：桌面端还把 `IYunxiaoCredentialProvider` 换成了 `CodexYunxiaoCredentialProvider`；WebHost **不需要**——框架默认的 `ConfigurationYunxiaoCredentialProvider` 注释明确「优先读 ICredentialStore、每次调用重新读取（不缓存）」，替换存储后即读到新值，写入后无需清缓存（与 PRD FR-1 第 5 步口径一致）。

---

## 5. 页面层设计（插件 web/）

### 5.1 文件结构

```
LubanAgent.Plugin.YunXiao/web/
├── index.html          壳：面包屑 + 内容区 + 动作栏（826×694 内自适应）
└── assets/
    ├── base.css        沿用骨架样式，补齐设计稿令牌（--accent:#3d5afe 等）
    ├── api.js          fetch 封装：双层 payload 包裹 + ApiEnvelope 解包（照抄宿主约定）
    ├── app.js          hash 路由（#/connect | #/projects）+ 首次进入门禁
    ├── connect.js      屏 1 表单逻辑
    └── projects.js     屏 2 列表 + 详情面板逻辑
```

### 5.2 路由与门禁

| 条件 | 行为 |
|------|------|
| 未配置凭据（`GET /yunxiao/settings` 返回 `hasPat=false`） | 强制 `#/connect`，进入 `#/projects` 自动弹回 |
| 已配置凭据 | 默认 `#/projects` |
| 屏 1 测试连接未通过 | 保存按钮 `disabled`（D7 前端半边） |

### 5.3 长文本与数据量（对齐设计稿 §4.6.3 规范）

- 项目名**单行省略** + 悬停 `title` 全文；左栏项目多时**栏内滚动**，不出整页滚动条；
- 右栏详情卡字段值单行省略；栅格子项留 `min-width: 0`，**禁止横向滚动**；
- 列表计数与右栏统计同源刷新。

### 5.4 S1 差异表（相对十屏设计稿）

| 差异点 | S1 形态 | 销账时机 |
|--------|---------|----------|
| 右栏「项目详情 / 迭代任务 / 迭代测试 / PRD 工作台」四入口 | **不画**（D9），右栏只到项目详情卡 | S2~S4 逐个点亮 |
| 屏 1「初始化状态」四步卡 | 不画（初始化属 S2） | S2 |
| 屏 3~屏 9 | 不实现 | S2~S4 |

---

## 6. 测试设计

### 6.1 服务端（MSTest，`LubanAgent.Tests/`，强制红绿 TDD）

| 测试文件（拟） | 被测对象 | 关键用例 |
|----------------|----------|----------|
| `YunxiaoSettingsServiceUnitTest.cs` | 设置服务 | `SaveAsync` 三分支语义（pat=null 保持 / 空串清除 / 有值覆盖）；落盘键为 `connector:yunxiao:token` 与 `connector:yunxiao:organizationid`；`HasPat` 不泄露明文；重复保存幂等；**SetAsync 真落盘不抛异常（钉死 C28 替换生效）** |
| `YunxiaoControllerUnitTest.cs` | REST 面 | payload 解析失败→400；成功壳 `ApiEnvelope{success:true}`；保存前探活失败→409 且未落盘 |
| `ProjexClientUnitTest.cs` | ProjexClient | token 头注入；perPage>100 截断；429/5xx 错误映射；`projects:search` 反序列化；keyword 透传 |
| `WebYunxiaoServiceUnitTest.cs` | 业务编排 | 校验→保存时序；项目列表分页透传与 keyword 过滤；脱敏错误文案 |

**测试构造口径（审查修正，三条硬约束）**：
1. `YunxiaoSettingsService` 行为测试用**手写内存假 `ICredentialStore`**——禁止用真实 `CredentialStore`：其 `Config` 经 `UserContextRegistry.GetOrCreate` 内部走 `ConfigManager.GetDefaultConfigPath(userId)`，**会写真实 LocalApplicationData 配置**，且 `UserContextRegistry.Build` 写死默认路径、单测无法注入临时路径，会产生测试污染；
2. C28「SetAsync 真落盘」用例改为 **DI 形状断言**：容器构建后 `GetRequiredService<ICredentialStore>()` 的运行时类型必须是 `LubanAgentCore.Security.CredentialStore`（可写），而不是框架默认的 `ConfigurationCredentialStore`（只读、SetAsync 抛 `NotSupportedException`）——同样不落盘；
3. HTTP 层不单测——仓库先例是 `YunxiaoResponseParserUnitTest` 测**解析纯函数**（真机取证 JSON 夹具 + `YunxiaoResponseParser.JsonOpts` 共享口径）。ProjexClient 照此模式：请求装配（URL、分页截断、keyword）抽纯函数、解析复用 `YunxiaoResponseParser`，不引入 `HttpMessageHandler` 测试桩。

### 6.2 前端断言脚本（无头 Edge 探针，不进 web/ 包）

`LubanAgent.Plugin.YunXiao/tests/assert-s1.js`（驱动方式沿用文档验证用过的「注入脚本 + title 探针回传」手法）：

| 断言 | 判据 |
|------|------|
| 画板不溢出 | 屏 1/屏 2 `content.scrollHeight <= clientHeight`，无横向滚动 |
| 保存按钮门禁 | 测试连接未通过时保存按钮 `disabled` |
| 长项目名 | 超长名单行省略（`scrollWidth > clientWidth` 且不换行撑高） |
| 列表滚动 | 项目超量时左栏栏内可滚，右栏不受牵引 |
| 门禁跳转 | `hasPat=false` 时进入 `#/projects` 被弹回 `#/connect` |

### 6.3 TDD 纪律（superpowers 强制）

每个任务：先写失败测试 → 确认红 → 最少实现 → 确认绿 → 提交；**测试前写的实现代码一律删除**。

---

## 7. 任务拆分预览（细粒度计划在下一步产出）

| 序 | 任务 | 主要文件 | 预估 |
|----|------|----------|------|
| T1 | `YunxiaoSettingsService` 移入 Core（签名不变），Codex 4 处引用改命名空间 | `LubanAgentCore/Connectors/AliyunDevOps/YunxiaoSettingsService.cs`、`LubanAgentCodex/`（App.axaml.cs / YunxiaoTabViewModel / SettingsWindow.Editors.Global） | 小 |
| T2 | **C28：WebHost 注册可写 CredentialStore**（照抄 App.axaml.cs 替换段） | `LubanAgentWebHost/Program.cs` | 小 |
| T3 | 设置服务红绿 TDD（SaveAsync 三分支语义、幂等、脱敏、键名归一） | `LubanAgent.Tests/YunxiaoSettingsServiceUnitTest.cs` | 小 |
| T4 | `ProjexClient.SearchProjectsAsync` 红绿 TDD + `YunxiaoProject` 模型 | `ProjexClient.cs`、`YunxiaoModels.cs`、`ProjexClientUnitTest.cs` | 中 |
| T5 | `WebYunxiaoService` 时序编排红绿 TDD（探活前置、409 拒存、列表透传） | `WebYunxiaoService.cs`、`WebYunxiaoServiceUnitTest.cs` | 中 |
| T6 | `YunxiaoController` + 保留名补 `yunxiao` | `YunxiaoController.cs`、`WebPluginPaths.cs` | 小 |
| T7 | `api.js` + `app.js` 路由与门禁 | `web/assets/` | 小 |
| T8 | 屏 1 `connect.js`（表单/门禁/错误态） | `web/assets/connect.js` | 小 |
| T9 | 屏 2 `projects.js`（列表/详情/长文本） | `web/assets/projects.js` | 中 |
| T10 | 前端断言脚本 + 全量回归 | `tests/assert-s1.js` | 小 |
| T11 | 真实云效探活校正 ProjexClient 参数 + 端到端手验 | — | 中 |

---

## 8. 风险与开放项

| 编号 | 事项 | 处置 |
|------|------|------|
| R-S1-1 | Projex `projects:search` **未实机验证**（URL/入参按官方文档写） | T10 以真实探活校正；失败按 §3.3 报「能力缺口」，不假装成功 |
| R-S1-2 | `YunxiaoSettingsService` 移动触碰 Codex 编译面（4 处引用） | T1 先移 + 全量构建 + 回归 Codex 现有测试 |
| R-S1-6 | **WebHost 默认只读凭据存储**：不替换则保存必抛 `NotSupportedException`（C28 漏做会让服务单测假绿、真机必炸） | T2 照抄桌面端替换段；T3 加一条「SetAsync 真落盘不抛异常」用例钉死 |
| R-S1-3 | SAEA.MVC 对 `{controller}/{action}` 的大小写与绑定细节 | T5 以 `PluginController` 既有运行为准；前端路径全小写 |
| R-S1-4 | 非特权凭据写入是新攻击面 | settings 端点只接两键；键名白名单硬编码；日志/响应全程脱敏（409/400 文案见 §3.3） |
| R-S1-5 | 「本期不画入口」与十屏全貌的差异 | 已建 §5.4 差异表，S2~S4 逐项销账 |

---

## 9. dev / prod 建议

| 项 | dev | prod |
|----|-----|------|
| PAT 来源 | 云效测试组织的只读 PAT，用完即吊销 | 生产 PAT 最小权限（项目只读 + 必要写域），专人保管 |
| 日志 | 可开 Debug 级，但 **PAT 仍脱敏**（只记键名与长度） | Information 级；严禁任何凭据明文进日志 |
| 探活超时 | 5s（快速失败） | 10s + 429 退避重试 1 次 |
| 部署 | 构建插件包→铺到 Web 插件根→插件管理页「启用」（骨架 README 流程） | 同左；升级需 `RequiresRestart` 时走宿主重启窗口 |

**常见坑：**
1. POST 忘记双层包裹或 Content-Type 带错子串 → SAEA 不绑定参数，表现为「请求格式错误」；
2. 插件页面用相对路径 fetch → iframe 镜像目录下 404，必须绝对路径 `/yunxiao/...`；
3. `perPage` 不截断 → 云效 400；前端默认 20、上限 100；
4. 保存按钮只做前端禁用而不做服务端探活 → 可被直接调 API 绕过（服务端必须二次探活）。

---

## 10. 下一步（superpowers 流程）

1. **用户签字确认本文档**（改哪条直接说）；
2. 开隔离工作区（git worktree + 分支），跑通构建与测试基线；
3. 产出 T1~T10 的 2~5 分钟粒度实施计划（精确文件路径、完整代码、验证步骤）；
4. 子代理驱动 TDD 实现 + 两段式评审。
