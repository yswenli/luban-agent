# 实施设计 · 云效 DevOps 工作台 Web 插件 · S2（项目初始化：复制代码库 + 复制流水线）

> 状态：**待签字**（头脑风暴五块已逐块确认，签字后进细粒度计划 → 子代理 TDD 实现）
> 上游依据：`docs/PRD-云效DevOps工作台Web插件.md` FR-2 / FR-3 / §4.12~4.13；`docs/界面设计稿-宿主尺寸预览.html` 屏 3（F5/F6）；S1 实施设计（`实施设计-云效DevOps工作台Web插件-S1.md`）已定口径与差异销账约定。
> 范围纪律：本文只覆盖 S2（FR-2+FR-3 一体）；PRD 同步/拆分（S3）、看板/任务工作区（S4）不动。

---

## 0. 决策记录（头脑风暴拍板，2026-11-18）

| # | 分叉 | 结论 |
|---|------|------|
| D1 | 切片边界 | **FR-2 + FR-3 一体**（屏 3 左右两列同交付，V2+V3 一次验收；FR-3 构建目录直接消费 FR-2 改名结果） |
| D2 | git 会话通路接线深度 | **明示命令 + 人工确认 + 最终回执**（不做逐条 SSE 进度动画） |
| D3 | git 自动化验证 | **仅单测**（指令装配/解析纯函数）+ 手动实机一次（不搭临时裸仓集成测试） |
| D4 | 执行异步形态 | **任务化 + 轮询回执**（`exec` 秒回 `taskId`，前端轮询任务态；同步阻塞会超时，弃） |
| D5 | 会话生命周期 | **每次初始化新建专用工作区会话，完成后留存**（命令与回执可追溯；不复用日常会话） |
| D6 | 任务状态持久化 | **落盘**（宿主重启可续查回执、失败可继续重跑；内存态弃） |
| D7 | 实机验证边界 | **只读实机 + 授权单库试验**（写操作仅在用户指定试验代号下跑一次，清理需用户确认） |
| D8 | 测试策略 | 服务端红绿 TDD（MSTest）+ 前端无头断言 `assert-s2.js`（CDP 通道首选，S1 教训） |

PRD 已定案、本文不重开的口径：git 动作只走工作区会话（接口层无 git 入口）；建库/建组/建流水线走 REST；命名只输代号、三段派生预览可单独改；模板模式由母版总数决定（1 锁定 / 多多选）；流水线=「读母版 YAML → 改名新建」、只绑 dev、后端唯一参数是监听端口；逐库/逐端独立失败重跑、不回滚成功项；远端资源不自动删。

---

## 1. 范围与验收

| 交付 | 内容 | 验收 |
|------|------|------|
| FR-2 复制母版代码库 | 母版清单（全量翻页）→ 代号派生预览 → 预检（冲突/命令序列明示）→ 逐库执行（建组建库 + 会话 git clone/改名/commit/push）→ 回执/重跑 | V2-1~V2-7 |
| FR-3 复制流水线 | 三端行（勾选/构建目录/流水线名）→ 反查母版流水线（仅 `PIPELINEASCODE`）→ 读 YAML → 占位符替换 + 逐条 diff → 确认提交（create/update）→ 回读 | V3-1~V3-8 |
| 屏 3 前端 | F5 左列 + F6 右列（826×694 内无溢出）、预检面板、逐库状态列表、diff 切换 | assert-s2.js |
| S1 差异销账 | 屏 2 详情卡点亮「项目初始化」入口（其余入口仍灰态） | 断言 + 手验 |

**明确不做**：C30 宿主 git 同步端点（PRD 定案一期不做）；PRD 同步/拆分（S3）；看板/任务工作区（S4）；测试/生产环境流水线；自动删除远端资源。

---

## 2. 架构总览

```
web/（屏 3 #/init，纯静态）
  │  fetch（双层 payload + ApiEnvelope，同 S1）
  ▼
YunxiaoController（薄路由追加 7 动作）
  │
WebYunxiaoService（static 壳：RunGuardedAsync + TryReadPayload，同 S1）
  │
YunxiaoInitService（实例类，可测；S2 新增核心编排）
  ├─ NamingDeriver（纯函数：代号解析 / 三段派生 / 分组前缀）
  ├─ GitCommandPlanner（纯函数：git 命令序列装配，展示与执行同源）
  ├─ YamlSubstitutor（纯函数：占位符替换 + diff 生成）
  ├─ ICodeupClient / IFlowClient（接口提取，照 IProjexClient 先例）
  ├─ ISessionGateway（会话通路收口：Create 指令 / Confirm / 查回执）
  ├─ InitTaskStore（任务态落盘，见 §4.4）
  └─ YunxiaoSettingsService（读组织/凭据，复用 S1）
```

依赖现状（已核）：`CodeupClient` 8 方法齐 FR-2 需求面（search/get/create/update repo、branches、template repos、list/create groups），`FlowClient` 4 方法齐 FR-3（list/get/create/update pipeline）——**连接器层零新增**，只做接口提取供 mock。

---

## 3. 接口契约（`/yunxiao` 前缀，沿用 S1：薄路由 + 双层 payload + `ApiEnvelope`）

| 端点 | 方法 | 入参（payload 内） | 出参 | 说明 |
|------|------|--------------------|------|------|
| `/yunxiao/manifests` | GET | — | `{ repos:[{name,description?}], groups:[{id,name}], workspaces:[{id,rootPath}], sessionReady }` | 母版库全量清单（`*-Mixed` 按 `x-total` 翻页聚合，perPage≤100、page≤150 自行截断——CodeupClient 不截断）+ 代码组 + **工作区清单**（屏3「工作区目录」=工作区选择器，内部绑 `workspaceId`，rootPath 仅展示；`WorkspaceController.List/ByRootPath/Create`）+ **会话通路可用性**（不可用则前端禁「开始执行」并明示原因，PRD 异常条款）。下拉「子项目数」按派生目录数（固定 3 端结构）展示，不为此新增树接口——执行期以实际目录兜底校验（PRD 7a） |
| `/yunxiao/preflight` | POST | `{ codename, repoIds[], groupId?, groupNewName?, workspaceId, branch, ends:{mobile:{on,name,dir},admin:{...},backend:{on,name,dir,port}} }` | `{ preview:{repos:[{from,to}],dirs:[{from,to}],pipelines:[{from,to}]}, gitCommands:[...]（脱敏）, conflicts:[...], pipelineDiffs:[{end,diff}] }` | **零副作用**；三段派生预览（每段可改，改后传回执行）；git 命令序列默认展开；冲突=新库名已存在/目录非空/端口占用 |
| `/yunxiao/init/exec` | POST | 预检确认后的完整清单（含改写后名字） | `{ taskId }` | 秒回；服务端建组→建库→会话 git→推送，逐库推进；**执行前二次查重**（TOCTOU）；同代号进行中任务互斥 |
| `/yunxiao/init/tasks/{id}` | GET | — | `{ id, codename, status, libs:[{repo,status,detail}], createdAt, finishedAt? }` | 前端 2s 轮询；库状态：`pending/cloning/renaming/pushing/done/failed`；`detail` 失败原因（脱敏） |
| `/yunxiao/init/tasks/{id}/retry` | POST | `{ repoIds[] }` | `{ taskId }` | 只重跑失败库；同名幂等复用 |
| `/yunxiao/pipelines/preview` | POST | `{ repoId, ends[...] }` | `{ diffs:[{end,name,diff}] }` | 逐条 YAML diff（读母版 YAML → 占位符替换），可切条查看 |
| `/yunxiao/pipelines/commit` | POST | `{ items:[{end,mode:"create"\|"update",name,content,...}] }` | `{ results:[{end,ok,pipelineId?,message?}] }` | 逐端独立提交；覆盖（update）须 `confirm:true`，缺失返回 409 |

错误表（沿用 S1 400/401/409/429/502 口径）追加：

| 场景 | code | message |
|------|------|---------|
| 会话通路未接线/不可用 | 503 | 工作区会话通路不可用，无法执行 git 动作 |
| 同代号任务进行中 | 409 | 该项目代号已有进行中的初始化任务 |
| 执行时新库名被抢先创建 | 409（该库 failed） | 新库名已存在（预检后被创建），该库中止 |
| 覆盖流水线未确认 | 409 | 覆盖为破坏性操作，需 confirm:true |

---

## 4. 服务端设计

### 4.1 NamingDeriver（纯函数，TDD 重灾区）

```csharp
// LubanAgentWebHost/Services/Init/NamingDeriver.cs
public static class NamingDeriver
{
    // "TJCES-Mixed" → "TJCES"；解析不出（无 -Mixed 后缀）返回 null（前端要求手填）
    public static string? ParseTemplateCode(string templateRepoName);

    // 三段派生（每段独立可改，改后以传回值为准）：
    //   repo:  {code}-Mixed
    //   dirs:  {code}-Admin / {code}-Mobile / {code}-Service
    //   pipe:  {prefix}_DEV_Deploy_{code}-{end}  （prefix 与 _DEV_Deploy_ 段取自母版流水线名，只替换代号段）
    public static NamingPlan Derive(string templateCode, string newCode, IReadOnlyList<string> templatePipelineNames);

    // 母版流水线名 → 分组前缀（合成示例："ACME_DEV_Deploy_DEMO-Mobile" → "ACME"；解析规则=首个 _DEV_Deploy_ 之前的部分；真值见 org-reference.md）
    public static string? ParsePipelinePrefix(string templatePipelineName);
}
```

### 4.2 GitCommandPlanner（纯函数；**展示与执行同一份序列**，防展示≠执行）

```csharp
public static class GitCommandPlanner
{
    // 输出有序步骤：clone 母版 → 批量 rename {tplCode}-* → {newCode}-* → commit（固定文案）→ remote set-url 新库 → push origin master
    // 固定 commit 文案（可核）："chore: 重命名子目录 {tplCode}-* → {newCode}-*（项目初始化派生）"
    // 命令中 URL 一律脱敏占位（<REPO_URL>），PAT 永不入序列；
    // 执行侧由 ISessionGateway 注入凭据（环境变量/askpass），明文零落盘零日志
    public static IReadOnlyList<GitStep> PlanCloneRenamePush(
        string workspaceDir, string templateRepoUrl, string newRepoUrl,
        string templateCode, string newCode, IReadOnlyList<string> dirs, string branch);
}
```

### 4.3 ISessionGateway（会话通路收口，R-S2-1 的隔离层）

```csharp
public interface ISessionGateway
{
    // 新建专用会话（留存）：SessionController.Create → SessionCreateRequest{WorkspaceId}
    Task<string> CreateInitSessionAsync(string workspaceId, CancellationToken ct);

    // 下发 git 指令序列：内部走 AgentController.Chat（SSE）消费事件流到终态——
    // 注意：非流式 SendAsync「不装配工具」，git 执行必须走 ChatAsync（EnableTools=true）；
    // D2「不做进度动画」= 不把中间事件透出给 UI，但通道本身仍须 SSE
    Task SendAsync(string sessionId, string instruction, CancellationToken ct);

    // 工具确认：AgentConfirmRequest{SessionId, ConfirmationId(取自 tool.confirm 帧), Result: allow|deny|allowAll|allowSession}
    // 执行期由网关按预检预批的命令清单自动放行（同批 allowAll），偏离清单即 deny 并失败该库；
    // 不用 PermissionMode=bypass 绕过确认（保留人工确认通道语义，PRD 既定）
    Task ConfirmAsync(string sessionId, string confirmationId, string result, CancellationToken ct);

    // 最终回执：SSE 终态 + Messages/LatestMessages 轮询兜底（SessionController.Messages）
    Task<SessionReceipt> WaitReceiptAsync(string sessionId, CancellationToken ct);
}
```

实现取 DI 口径：网关经 `WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId)` 解析 `IAgentHost` 等底层服务（与 `WebAgentService` 壳同款），**in-process 调用、不走 HTTP 自环**；任务跑在后台 `Task.Run`，不受 HTTP 超时约束。

接线第一步（R-S2-1 处置）：先用最小指令打通 `SessionController.Create → Chat（SSE，EnableTools）→ tool.confirm/Confirm → Messages 回执` 实测，口径不符按「能力缺口」报，不假装成功。

### 4.4 InitTaskStore（落盘持久化，D6）

- 形态：**JSON 状态文件**（照 `WebPluginPaths.DefaultStateFilePath` 先例，`%LocalAppData%/LuBan/AIAgent/webhost/plugin-states.json` 同目录）：`%LocalAppData%/LuBan/AIAgent/webhost/yunxiao-init-tasks.json`；单宿主进程、原子写（临时文件 + 替换）。
- 模型：`InitTask { Id, Codename, Status, Libs[], CreatedAt, FinishedAt, SessionId }`；重跑只改 `Libs` 子集状态，历史留痕。
- 备选（弃）：SqlSugar 建表——过重，任务态体量小、无跨进程查询需求。

### 4.5 接口提取与新增文件清单

| 文件 | 动作 |
|------|------|
| `LubanAgentCore/Connectors/AliyunDevOps/ICodeupClient.cs`、`IFlowClient.cs` | 新增接口（公开面照抄现有 Client），Client 实现之，DI 兼容注册照 `IProjexClient` 先例 |
| `LubanAgentWebHost/Services/Init/{NamingDeriver,GitCommandPlanner,YamlSubstitutor,InitTaskStore,ISessionGateway,SessionGateway,YunxiaoInitService}.cs` | 新增 |
| `LubanAgentWebHost/Controllers/YunxiaoController.cs` | 追加 7 动作（manifests/preflight/exec/tasks/retry/pipelines×2） |
| `LubanAgentWebHost/Models/YunxiaoContracts.cs` | 追加 S2 DTO |
| `LubanAgent.Plugin.YunXiao/web/assets/{init.js,init.css}`、`app.js`/`index.html` | 屏 3 与路由/入口 |

### 4.6 YamlSubstitutor（FR-3 占位符替换 + diff）

- 替换面（PRD 字段表）：代码库 id/名称、构建目录（workDir）、分支、**后端端口变量（固定 dev 环境变量）**；测试/生产段变量不动。
- 输出逐条 unified diff（预览）与替换后 content（提交用）；替换规则以母版流水线 YAML 实物为准，规则表进单测夹具（脱敏占位）。

---

## 5. 页面层（屏 3，826×694）

- 路由：`#/init`（自屏 2 详情卡「项目初始化」入口进入——**S1 差异表此处销账**；其余两入口仍灰态）。
- F5 左列：母版下拉（总数=1 锁定单项；>1 可多选）+ 新项目代号 + 命名派生预览（三段各自可改，改后重算）+ 共用设置（代码组下拉/新建、工作区选择器——展示 rootPath 绑 `workspaceId`、分支默认 `master`）+ 复制结果逐库状态列表 + 失败库「重跑」。
- F6 右列：新库标签 + 三端行（勾选 + 构建目录 + 流水线名，均可改）+ 后端端口盒（唯一输入，默认 8080）+ 逐条 diff 切换预览 + 「提交流水线」（覆盖需卡内确认勾选）。
- 预检面板：只读清单 + **git 命令序列默认展开（脱敏）** + 「确认执行」；未预检或预检失败 → 执行按钮禁用（D2 门禁）。
- 长文本/溢出硬规则沿用 S1：单行省略 + title、区内滚动、无横向滚动、两列在 826×694 内无溢出（R-S2-3 专项断言）。
- 执行中：2s 轮询任务态刷新逐库状态；宿主重启后进入页面自动恢复历史任务展示（落盘收益）。

---

## 6. 测试设计

### 6.1 服务端（MSTest，红绿 TDD）

| 测试文件（拟） | 关键用例 |
|----------------|----------|
| `NamingDeriverUnitTest.cs` | 代号解析（-Mixed 后缀/解析不出）；三段派生与单独改写；分组前缀解析；空/非法输入 |
| `GitCommandPlannerUnitTest.cs` | 命令序列顺序与固定 commit 文案；rename 覆盖三目录；**URL/PAT 脱敏断言（序列里不出现 pt- 前缀明文）**；分支参数 |
| `YamlSubstitutorUnitTest.cs` | 占位符替换面（repo/workDir/branch/端口）；测试生产段不动；diff 生成 |
| `YunxiaoInitServiceUnitTest.cs` | 预检零副作用；冲突检查；exec 二次查重；同代号互斥；逐库状态机迁移；重跑幂等；gateway mock 全链；manifests 翻页聚合（x-total 口径） |
| `YunxiaoPipelineFlowServiceUnitTest.cs`（并入 InitService 亦可） | preview 依赖 FR-2 结果；commit 逐端独立、覆盖 409 |
| `InitTaskStoreUnitTest.cs` | 原子写、重启读回、部分重跑留痕 |

mock 面：`ICodeupClient`/`IFlowClient`/`IProjexClient`/`ISessionGateway`/`IYunxiaoSettingsService`（均有接口）；真实 `CredentialStore` 禁用（S1 口径：防 LocalApplicationData 污染）。

### 6.2 前端（`tests/assert-s2.js`，CDP 通道首选——S1 教训钉死）

判据：屏 3 两列无溢出/无横向滚动；母版=1 锁定态；派生预览可改且改后触发重算；命令序列默认展开且不含 `pt-` 明文；未预检执行按钮 disabled；长名省略；diff 切换；入口点亮自屏 2。

### 6.3 手动实机（D3/D7）

1. 只读实机：manifests/预flight/查重对真实组织跑通；
2. **授权单库试验**：由用户指定试验代号（建议 `TEST*`）与试验分支，全链路跑一次（建组复用/建库/clone/改名/推送 + 一条流水线 create），回执核对；清理动作（删试验库/流水线）**由用户确认后执行或用户自行处理**，我方不自动删。

---

## 7. 任务预览（细粒度计划时拆 2~5 分钟粒度）

| 序 | 任务 | 主要文件 | 预估 |
|----|------|----------|------|
| T1 | `NamingDeriver` 红绿 TDD | Services/Init/ + 单测 | 中 |
| T2 | `GitCommandPlanner` 红绿 TDD（脱敏断言） | 同上 | 中 |
| T3 | `ICodeupClient`/`IFlowClient` 提取 + DI 兼容 | Core + 单测回归 | 小 |
| T4 | `ISessionGateway` + 会话通路最小回路实测（R-S2-1） | SessionGateway + 手验 | 中 |
| T5 | `InitTaskStore` 红绿 TDD | 同上 | 小 |
| T6 | `YunxiaoInitService`（manifests/preflight/exec/tasks/retry）红绿 TDD | 服务 + 单测 | 大 |
| T7 | `YamlSubstitutor` + FR-3 preview/commit 红绿 TDD | 服务 + 单测 | 中 |
| T8 | `YunxiaoController` 追加 7 动作 + 契约 DTO | Controller/Models | 小 |
| T9 | 屏 3 前端 F5 左列（清单/派生/预检/状态列表） | web/assets/init.js | 大 |
| T10 | 屏 3 前端 F6 右列（三端/diff/提交）+ 屏 2 入口点亮 | web/assets/ | 中 |
| T11 | `assert-s2.js` + 全量回归 | tests/ | 中 |
| T12 | 实机验证（只读 + 授权单库试验）+ 校正 | — | 中 |

---

## 8. 风险

| 编号 | 事项 | 处置 |
|------|------|------|
| R-S2-1 | 会话通路接口口径未实测（S1 未接过） | T4 最小回路先打通；不通按「能力缺口」报 |
| R-S2-2 | 组织专有真值红线 | 文档/夹具一律占位符（`{母版代号}`/`{新代号}`），真值唯一副本在 `LubanAgent.Plugin.NewProject/docs/org-reference.md`，禁用值扫描兜底 |
| R-S2-3 | 屏 3 两列信息密度 → 826×694 溢出 | assert-s2.js 专项 + 列内滚动设计 |
| R-S2-4 | 执行中途重启 / 重复执行 | 落盘任务态 + 二次查重 + 同代号互斥 |
| R-S2-5 | **PAT 泄露面**（git clone URL 可能带凭据） | 命令序列展示与日志全程脱敏（`<REPO_URL>`），执行侧凭据注入（askpass/env），明文零落盘（含 commit 文案与回执 detail） |

---

## 9. dev / prod 建议

- dev：真组织只读实机随时可跑；写操作只用试验代号；会话通路本地不通时屏 3 执行按钮禁用是**预期行为**。
- prod：工作区目录限宿主工作区白名单内（防任意路径 clone）；端口冲突预检保留；覆盖流水线二次确认保留；任务态文件权限随宿主用户。

## 10. 常见坑

1. 命令序列「展示 ≠ 执行」的 TOCTOU：预检与执行必须同源生成序列（`GitCommandPlanner` 单一入口）。
2. git 命令里任何 `<token>@host` 形式都会进界面与日志——**从源头就不生成**。
3. 母版清单翻页：`x-total` 口径同 Codeup（S1 实测过 Projex 是 `X-Total`，Codeup 侧小写 `x-total`，大小写不敏感解析），只看首页会误判「仅一个母版库」。
4. 断言脚本视口：CDP 通道首选（`Emulation.setDeviceMetricsOverride`），`--window-size` 在 new headless 下不可靠（S1 实测教训）。
5. `flow_create_pipeline` 的 content 必须是顶层 `stages:` 新版 YAML（仓库既有真机注释），喂 tb 形态 `flow` 必报「至少要包含一个stage」。
