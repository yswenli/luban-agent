# 实施计划 · 云效 DevOps 工作台 Web 插件 · S2（FR-2+FR-3 一体）

> 依据：`docs/实施设计-云效DevOps工作台Web插件-S2.md`（已深度审查修正 6 处，2026-11-18）。
> 分支：`feature/yunxiao-workbench-s2`。测试栈 MSTest（`LubanAgent.Tests`，当前 602 绿为基线）。
> 纪律：每任务红绿 TDD（先写失败测试→看红→最少实现→看绿→提交由主代理统一做）；**子代理不 git commit**；两段式评审（规格符合度→代码质量）卡在每批末；禁用值红线（真值只在 org-reference.md）；PAT 明文零落盘零日志。

## 批次 A · 纯函数地基（T1~T3）

### 任务 1：NamingDeriver 红绿 TDD
- 新建 `LubanAgentWebHost/Services/Init/NamingDeriver.cs`（static，纯函数）+ `LubanAgent.Tests/NamingDeriverUnitTest.cs`。
- 方法（签名照设计 §4.1）：
  - `ParseTemplateCode(string templateRepoName)`：`"-Mixed"` 后缀剥离；无后缀/空 → null。
  - `ParsePipelinePrefix(string templatePipelineName)`：首个 `_DEV_Deploy_` 之前的部分；无该段 → null。
  - `Derive(string templateCode, string newCode, IReadOnlyList<string> templatePipelineNames)` → `NamingPlan`（record：`RepoName`、`Dirs`（3 条 `{code}-Admin/Mobile/Service`）、`Pipelines`（每端 `{prefix}_DEV_Deploy_{code}-{end}`，prefix 取自对应母版流水线名））；端枚举固定 mobile/admin/backend 对应 `-Mobile`/`-Admin`/`-Service` 后缀。
  - 传入新代号做合法性校验（非空、`^[A-Za-z][A-Za-z0-9]*$`，不合法抛 `ArgumentException`）。
- 用例（≥8）：代号解析正/反；前缀解析正/反（合成名 `ACME_DEV_Deploy_DEMO-Mobile`）；三段派生；改写场景（调用方以改写后名字为准——Derive 只出默认值）；新代号非法；空列表。
- 验证：`dotnet test LubanAgent.Tests --filter "FullyQualifiedName~NamingDeriver"` 绿。

### 任务 2：GitCommandPlanner 红绿 TDD
- 新建 `LubanAgentWebHost/Services/Init/GitCommandPlanner.cs` + `LubanAgent.Tests/GitCommandPlannerUnitTest.cs`。
- `PlanCloneRenamePush(workspaceRoot, templateRepoUrl, newRepoUrl, templateCode, newCode, dirs, branch)` → `IReadOnlyList<GitStep>`（record：`Label`、`Command`）。
- 步骤序列：`clone --branch {branch} {templateRepoUrl} {workspaceRoot}/{newCode}-Mixed` → 每目录 `git mv {tpl}-{end} {new}-{end}`（在 clone 目录内）→ `git commit -m "chore: 重命名子目录 {tpl}-* → {new}-*（项目初始化派生）"`（固定文案）→ `git remote set-url origin {newRepoUrl}` → `git push origin {branch}`。
- **脱敏断言（硬性）**：输出 Command 里不出现 `pt-`、`@` 前缀凭据形态；URL 以调用方给的裸 URL 传入（凭据不进 URL，执行侧注入）；单测断言序列中无 `pt-` 子串。
- 用例（≥6）：顺序正确；三目录 mv 齐全；commit 文案固定；remote set-url 在 push 前；脱敏断言；dirs 为空时跳过 mv/commit。
- 验证：filter `~GitCommandPlanner` 绿。

### 任务 3：ICodeupClient / IFlowClient 接口提取 + DI 兼容
- 新建 `LubanAgentCore/Connectors/AliyunDevOps/ICodeupClient.cs`、`IFlowClient.cs`：公开面逐方法照抄现有 `CodeupClient`/`FlowClient`（8+4 个方法签名一字不差），Client 类实现之。
- DI：`AliyunDevOpsConnectorServiceCollectionExtensions` 加 `TryAddSingleton<ICodeupClient>(sp => sp.GetRequiredService<CodeupClient>())` 与 Flow 同款（照 `IProjexClient` 桥接先例）。
- 既有消费方零改动（仍按具体类注册）；全量回归绿（602+新增）。
- 验证：`dotnet build LubanAgent.slnx` 0 错 + 全量测试绿。

## 批次 B · 会话网关与任务态（T4~T6）

### 任务 4：ISessionGateway 契约 + 最小回路实测（R-S2-1）
- 新建 `LubanAgentWebHost/Services/Init/ISessionGateway.cs`（签名照设计 §4.3 修正版：CreateInitSessionAsync(workspaceId)/SendAsync/ConfirmAsync(confirmationId,result)/WaitReceiptAsync）+ `SessionGateway.cs`。
- 实现口径：`WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId)` 解析 `IAgentHost`/会话服务（照 `WebAgentService` 壳同款取 DI，in-process）；`SendAsync` 内部走 **Chat（SSE，EnableTools=true）** 消费事件流到终态（非流式 SendAsync 不装配工具——已核实禁用）；`tool.confirm` 帧 → `ConfirmAsync`（`AgentConfirmRequest{SessionId,ConfirmationId,Result}`）；回执 = SSE 终态 + `Messages` 轮询兜底。
- **最小回路手动实测**（先于编码收口）：起 WebHost → 建会话 → Chat 发一条 `echo` 级指令（装配工具）→ 观察 tool.confirm/回执帧形态 → 按实测钉 `SessionReceipt` 解析；口径不符→报告「能力缺口」停下，不硬上。
- 单测：网关解析/映射逻辑纯函数化（SSE 帧→SessionReceipt 解析器抽 internal static），mock 事件序列测 allow/deny 分支。
- 验证：filter `~SessionGateway` 绿 + 手测记录。

### 任务 5：InitTaskStore 红绿 TDD
- 新建 `LubanAgentWebHost/Services/Init/InitTaskStore.cs`：JSON 文件 `%LocalAppData%/LuBan/AIAgent/webhost/yunxiao-init-tasks.json`（原子写：tmp+replace；路径可注入以便测试用临时目录——**测试必须注入临时路径，禁写真实 LocalApplicationData**，同 S1 纪律）。
- 模型 `InitTask{Id,Codename,Status,Libs[{Repo,Status,Detail}],WorkspaceId,SessionId,CreatedAt,FinishedAt}`；状态：`pending/cloning/renaming/pushing/done/failed`。
- API：`UpsertAsync`、`GetAsync(id)`、`ListByCodenameAsync`、`TryBeginAsync(codename)`（同代号互斥：进行中返回 false）。
- 用例（≥6）：原子写读回；重启模拟（新实例读旧文件）；部分重跑留痕；互斥；损坏 JSON 容错（当空）；路径注入。
- 验证：filter `~InitTaskStore` 绿。

### 任务 6：YunxiaoInitService 预检/清单 红绿 TDD（Part 1）
- 新建 `LubanAgentWebHost/Services/Init/YunxiaoInitService.cs`（实例类，构造注入 `ICodeupClient`/`IFlowClient`/`IProjexClient`/`ISessionGateway`/`InitTaskStore`/`IYunxiaoSettingsService`）。
- 方法 Part1：`GetManifestsAsync`（母版库 `*-Mixed` 翻页聚合：perPage=100 自行 clamp、按 x-total 翻满；代码组列表；workspaces 列表（经 `IWorkspaceManager` 或注入的 workspace 服务口——实测定）；sessionReady 探测）、`PreflightAsync`（零副作用：NamingDeriver 派生 + GitCommandPlanner 序列 + 新库名查重 `GetRepositoryAsync` + 端口占用预检（`IPGlobalProperties`））。
- 用例（≥7）：翻页聚合（3 页 mock 合成 29 条）；母版筛选 `*-Mixed`；preflight 返回三段预览与命令序列；冲突列出（库名已存在/端口占用）；sessionReady=false 时 exec 拒绝；参数校验 400 语义。
- 验证：filter `~YunxiaoInitService` 绿。

## 批次 C · 执行编排与 FR-3（T7~T8）

### 任务 7：YunxiaoInitService 执行/任务/重跑 红绿 TDD（Part 2）
- 方法：`ExecAsync`（返回 taskId；后台 `Task.Run` 推进：TryBegin 互斥 → 二次查重 → 查/建代码组 → `CreateRepositoryAsync` 建库 → gateway 逐库发指令（预批清单给 gateway 做 confirm 放行）→ 更新 Libs 状态 → 收口）；`GetTaskAsync`；`RetryAsync`（只重跑失败库，幂等复用）。
- 状态机迁移逐库独立；某库 fail 不影响其他库；执行中断（异常）落 `failed` + detail（脱敏）。
- 用例（≥8）：exec 秒回 taskId；互斥拒 409；二次查重中止该库；逐库推进 mock gateway；重跑幂等；detail 无 PAT；跨进程重启续查（TaskStore 注入临时路径）。
- 验证：filter `~YunxiaoInitService` 绿。

### 任务 8：YamlSubstitutor + FR-3 服务方法 红绿 TDD
- 新建 `LubanAgentWebHost/Services/Init/YamlSubstitutor.cs` + 单测：`Substitute(yaml, vars)`（替换 repo id/名称、workDir、branch、后端端口变量；**测试/生产段不动**——按 YAML 段注释/变量名白名单精确替换，不做全文盲替）；`Diff(original, modified)`（unified diff 文本，自实现或逐行对比输出简单 diff 格式）。
- `YunxiaoInitService` 增 `PreviewPipelinesAsync`（`ListPipelinesAsync` 反查母版三条：名称含 `_DEV_Deploy_{tplCode}-{end}` 且 `type==PIPELINEASCODE`；逐条 `GetPipelineAsync` 取 YAML → Substitute → diff）与 `CommitPipelinesAsync`（逐端独立：create 走 `CreatePipelineAsync(name, content)`、update 走 `UpdatePipelineAsync`，覆盖须 confirm=true 否则 409；回读结果）。
- 用例（≥8）：替换面精确（多目标替换）；生产段变量不动；diff 生成；反查命名匹配；UI 编排型（非 PIPELINEASCODE）不可选；逐端独立失败；覆盖 409；content 带顶层 `stages:` 校验（防 tb 形态误喂，仓库既有真机注释口径）。
- 验证：filter `~YamlSubstitutor`/`~YunxiaoInitService` 绿。

## 批次 D · REST 面（T9）

### 任务 9：YunxiaoController 追加 7 动作 + 契约 DTO
- `LubanAgentWebHost/Models/YunxiaoContracts.cs` 追加 S2 DTO（manifests/preflight/exec/task/retry/pipelines 请求响应 record，命名照 S1 `Yunxiao*Dto` 风格）。
- `LubanAgentWebHost/Services/WebYunxiaoService.cs` 追加 7 个 static 壳方法（RunGuardedAsync + TryReadPayload + Query 转调 `YunxiaoInitService`，照 S1 形态）。
- `LubanAgentWebHost/Controllers/YunxiaoController.cs` 追加动作：`Manifests`(GET)、`Preflight`(POST)、`InitExec`(POST)、`InitTask`(GET)、`InitRetry`(POST)、`PipelinePreview`(POST)、`PipelineCommit`(POST)。路由名避开 `Task`/`Get` 等保留感词——用 `InitTask`/`InitRetry` 显式前缀。
- 错误映射：400 参数、409 互斥/覆盖未确认/查重、503 会话不可用（照设计 §3 错误表）。
- 验证：build 0 错；服务单测回归绿；curl 级手验留到批次 F。

## 批次 E · 前端（T10~T11）

### 任务 10：屏 3 F5 左列 + 路由/入口
- `web/assets/init.js` + `index.html` 增 `#screen-init` + `app.js` 路由 `#/init`；屏 2 详情卡点亮「项目初始化」入口（其余仍灰态，差异表销账）。
- F5：母版下拉（manifests；总数=1 锁定单项禁展开）、新代号输入（变更即重算预览）、三段命名派生预览（各段可改）、共用设置（代码组下拉/新建、工作区选择器 rootPath 展示绑 workspaceId、分支默认 master）、预检按钮 → 预检面板（只读清单 + **git 命令序列默认展开** + 确认执行按钮）、逐库状态列表（2s 轮询 InitTask）+ 失败库「重跑」。
- 门禁：未预检/预检失败 → 执行禁用；sessionReady=false → 执行禁用并明示。
- 通信壳沿用 `api.js`（双层 payload）。

### 任务 11：屏 3 F6 右列（流水线区）
- 三端行（勾选+构建目录+流水线名，均预填可改）、后端端口盒（唯一输入，默认 8080）、`/yunxiao/pipelines/preview` 逐条 diff 切换预览、提交按钮（覆盖模式卡内勾选 confirm）。
- 依赖 FR-2 结果（新库 id 自任务态取）；FR-2 未完成时右列灰态提示。
- 长文本/溢出硬规则沿用 S1（单行省略+title、区内滚动、无横向滚动）。

## 批次 F · 断言与实机（T12~T13）

### 任务 12：assert-s2.js + 全量回归
- 新建 `LubanAgent.Plugin.YunXiao/tests/assert-s2.js`（CDP 通道首选——`Emulation.setDeviceMetricsOverride` 钉 826×694，S1 教训）。
- 判据：屏 3 两列无溢出/无横向滚动；母版=1 锁定态；派生预览改写触发重算；命令序列默认展开且**不含 `pt-`**；未预检执行 disabled；长名省略；diff 切换存在；屏 2 入口可点进 `#/init`；禁用扩展名零引用。
- 全量回归：`dotnet build` 0 错 + `dotnet test` 全绿 + `assert-s1.js` 8/8 + `assert-s2.js` 全 PASS。

### 任务 13：实机验证（D7：只读实机 + 授权单库试验）
- 只读实机：manifests/preflight 对真实组织跑通（母版清单、查重、命令序列核对）。
- **授权单库试验**（需用户指定试验代号/确认）：建库 + clone/改名/推送 + 一条流水线 create 全链路一次；回执核对；清理需用户确认后执行（远端不自动删）。
- 结果回写设计文档销账表。

## 评审卡点（每批末）

1. **规格符合度**：对照设计文档逐条（端点契约/字段名/状态机/脱敏红线）。
2. **代码质量**：命名、重复、测试有效性、边界。
- Critical 阻断项：PAT 明文出现于任何文件/日志/命令序列；预检非零副作用；执行期 confirm 绕过预批清单；禁用值混入 docs；测试写真实 LocalApplicationData。

## 风险对照

R-S2-1 → T4 先实测；R-S2-2 → 全程占位符 + 禁用值扫描；R-S2-3 → T12 专项；R-S2-4 → T5/T7 互斥与二次查重；R-S2-5 → T2 脱敏断言 + T7 detail 脱敏断言。
