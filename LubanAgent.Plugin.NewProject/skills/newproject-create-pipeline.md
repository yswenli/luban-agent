# 创建流水线（newproject-create-pipeline）

> 「新建项目」插件原子技能 · 对应 **Step 7** · **助手驱动口径**：
> 「**一次只读准备下发 + 用户确认后同会话执行续跑**」。
> 组织专有值（Harbor 域名、服务连接 id、bucket、端口、模板仓名、pipelineId、主机组 uuid …）
> **不在本正文复述**——值总表读工作区 `.luban-agent/plugin-newproject/docs/org-reference.md`，
> 三端 YAML 模板读 `docs/pipeline-*.yml.md`，编排约定（哪端有独立部署阶段 / 留空即需人工绑定 …）
> 读 `docs/pipeline-conventions.md`。

## 何时使用

- 主 skill `newproject-create` 的第 7 步；
- 或**仅补建某端流水线**（例如 Mobile 后补）；
- 或对已有流水线做「**覆盖更新**」纠偏 / 补部署段（仅后端需要，见末节）。

## 前置条件

- Step 6（推送）对该端已成功。界面用 `StepGate.CanCreatePipelines` 拦，**判据已改读 Step 6 结论文件**：
  最近一份 `results/step6-*.json` 的逐端推送结果全通过且覆盖 `spec.Ends` 全集才放行；
  `ProjectSpec.StepStates` 已降级为「仅界面进度显示」，**不再作闸门依据**。
- 云效组织 ID 与 PAT 已在 codex「设置 → 云效连接」配好；PAT 由连接器注入 MCP，
  **不进工具入参、不进提示词、不落结论文件**。
- 各端默认值（端口 / 容器名 / 服务连接 / OSS 目录 …）与部署目标候选取值读
  `docs/org-reference.md`（**本正文不复述任何具体值**）。
- **后端必须有部署目标**：准备阶段先**只读**拉取云效主机组列表并选定；列表读不到 / 没选到目标
  ⇒ **阻断创建、不发任何请求**。

> **不再依赖参考流水线**：历史口径（参考流水线 + `flow_clone_pipeline`）已作废。
> 云效控制台里现存流水线**全部是 tb 形态**、其 `flow` 原文不可提交（报 `1209300 校验失败`），
> 故改为「**助手按 `docs/pipeline-*.yml.md` 渲染新版 YAML → 提交**」。

## 术语：一次只读准备下发 + 同会话执行续跑

本步在宿主 API 上表现为「先下发一次**只读准备** → 用户确认（选部署目标 / 微调 YAML）→
再下发一次**执行续跑**」。这是**同一个步骤**的两个阶段（框架没有「轮内提问」工具，答复后由插件
再发一次提示词），**不是两个各自独立的步骤**；执行续跑只做增量，**不重跑准备阶段**。

## 阶段一 · 准备（只读，不建任何流水线）

1. **拉部署目标候选（仅后端 Service）**：`mcp_yunxiao_flow_list_host_groups` 拉主机组列表，
   每条记 **数字 id + uuid + 名称 + 机器数**（需要时 `mcp_yunxiao_flow_get_host_group`
   （**路径键必须是数字 id**）摊出机器明细核对落点）。**前端两端没有独立部署阶段，跳过本步**。
   列表拉取失败 / 无可用主机组 / 未选到目标 ⇒ **阻断创建、不发任何请求**
   （**不降级成手填 uuid**——uuid 填错云效只回「该机器组 uuid[x] 不存在」，白跑一次且污染远端）。
2. **逐端渲染新版 YAML**：按端取 `docs/pipeline-*.yml.md` 的模板，用项目档案 + `docs/org-reference.md`
   的值展开（名称 / 仓库地址 / 分支（取 `pushBranch`）/ 服务连接 / 变量 / 缓存 / OSS 目录 / 部署段 …）；
   占位符替换语义见 `docs/pipeline-conventions.md`。
3. **产出结论文件**：`results/step7-*.json`，`status = "done"`；`artifacts[]` 的 `kind=pipeline`
   **每端一条**，`extra` 为 `PipelineArtifactExtra`
   （`yamlIncludesDeployStage` / `pendingBindings` / `recommendedDeployTargetHostGroupId`（数字 id）/
   `recommendedDeployTargetUuid`）；**`inputs[]` 带部署目标选择**（有独立部署阶段的端必选，
   选项**同时带数字 id 与 uuid**，见下）。

> 准备阶段**不得**创建任何流水线——结论文件里若出现新建的流水线即为不通过。

面板：展示 YAML（**可手工微调**，会话内按端保留、可「重置为模板」）、部署目标下拉（只读回显机器明细）、
待人工绑定清单。⚠️ **面板不再做 YAML 即时校验**——`name:` 一致性 / Tab / 顶层 `stages:` 等一律
**交给云效报回**（失败即 `status="failed"`，文案取云效原文）。微调后的文本随用户答复进执行续跑。

## 阶段二 · 执行续跑（用户确认后，同一会话）

提示词带上用户确认后的选择（`answers`）+ 准备阶段结论文件**绝对路径** + 微调后的 YAML。助手动作：

1. **先检索同名流水线**：`mcp_yunxiao_flow_list_pipelines`（`pipelineName=` 服务端过滤可用）；
   **命中同名即对该条 `mcp_yunxiao_flow_update_pipeline` 覆盖更新本次渲染的 YAML**（**全端**；
   不再「命中即复用」——旧流水线可能缺部署段或残留上一版模板）；未命中才创建（幂等）。
2. **创建**：`mcp_yunxiao_flow_create_pipeline`（逐端 1 条、最多 3 条；`name` + `content`；
   编排约定见 `docs/pipeline-conventions.md`）；**建完不自动运行**。
3. **覆盖更新**：已存在则 `mcp_yunxiao_flow_update_pipeline`（`pipelineId` + `name` + `content`）；
   注意更新会**一并覆盖流水线名称**，故必须回传现有名称。
4. **部署段随创建写入**：有独立部署阶段的端（**仅后端**），`deploy` 段随构建段一起写进提交 YAML
   （`machineGroup` 用本次选定的**主机组 uuid**）；**缺目标即 `failed`、不发请求**。
   同时产出「**待人工绑定清单**」（服务连接 / 构建集群 / Harbor 凭据 / 主机组 uuid 等，
   依据 `docs/pipeline-conventions.md` 的「留空即需人工绑定」约定）。
5. **回写结论文件**：`results/step7-*.json`，`artifacts[kind=pipeline]` 逐端如实记录
   流水线 id / webUrl / 是否含部署段 / 所选主机组（uuid + 名）；`checks[]` **逐端一条**；
   **缺端即整步 `failed`**（流水线在远端，**无本地可独立核对的证据**，如实记为残余风险）。

## 结论文件契约

`{workspace}\.luban-agent\plugin-newproject\results\step7-<ts>.json`：`status` 为
**`done` / `needsInput` / `failed`**（Step 7 是**阻断性**步骤）。`artifacts[]` 判别属性是 `kind`
（本步一律 `kind=pipeline`；`extra` 为具名强类型 `PipelineArtifactExtra`），
`inputs[]` 见下；不得用自由字典充当 `extra`。

## 交互项 `inputs[]`（部署目标选择）

```jsonc
{
  "id": "deploy-target",
  "kind": "select",
  "prompt": "选择部署目标（必须选，创建时写入 deploy 段）",
  "ends": ["Service"],
  "options": [
    { "value": "<数字 hostGroupId>",   // 拉机器明细用（flow_get_host_group 只吃数字 id）
      "uuid": "<主机组 uuid>",          // YAML machineGroup 写的是它
      "label": "<名称>（<机器数> 台）" }
  ],
  "details": { "defaultValue": "<默认数字 hostGroupId>", "required": true }
}
```

- **必须同时带数字 id 与 uuid**：`flow_get_host_group` 只吃数字 id（传 uuid 会 400），
  而 YAML `machineGroup` 写的是 uuid。
- 空组（机器数为 0）可选，但要标「当前 0 台机器：部署会找不到落点」告警。
- 聊天路径（无面板）：助手把主机组候选（名称 / 机器数 / 数字 id / uuid）**直接列在对话回复里**，
  用户回一句即可，助手在**同一会话继续**创建；**不依赖 `defaultValue` 兜底、也不降级为手填 uuid**。
- 用户既不选也不回复 → 停在本步 `needsInput`（可重试），**不发创建请求**。

## 结果

- 所选端**各 1 条、最多 3 条**流水线；名称含项目缩写，便于识别。
- 后端流水线**含活动部署段**，前端两端不含（上传 OSS 即发布）。

## 重要约束（真机口径）

- **变量不能带 `type`**：顶层 `variables:` 只写 `key` / `value`，可选 `secret: true`。写了 `type:` 会被拒。
- **`runsOn` 默认不写**（＝云效默认北京公共集群）。OpenAPI 校验对**私有集群一律报「runsOn 不存在」**，
  故私有集群值只能人工回填（整串照写，取值形态见 `docs/org-reference.md`）。
- **缓存模式**随之推导：`private*` ⇒ `cacheMode: local`，否则 `remote`。
- 后端模板只跑 `docker build`，需构建机可用 docker daemon。
- 前端模板：Node 构建 → OSS 删除 → OSS 上传，**必须带 `region`**（字段细节见 `docs/pipeline-*.yml.md`）。
- **部署段随创建写入**：有独立部署阶段的端（**仅后端**），创建时就把 `deploy` 段写进提交 YAML，
  **不再有注释块**；同时产出「待人工绑定清单」。
- **写进 YAML 的是主机组 uuid，不是数字 id**：tb 形态的数字 id 会被拒（`该机器组 uuid[<id>] 不存在`）。
- 建完流水线**不自动运行**（`triggerEvents: []`）；需排障时助手可即席调用 `flow_run_pipeline`
  （**该工具在插件策略里默认拒绝**，只在排障时按用户显式要求放行）。
- 中止 / 清理时**远端流水线不自动删除**，只列孤儿清单。

## 覆盖更新（创建命中同名也自动走覆盖更新 · 全端）

- **两条路径**：①**创建入口自动**——普通创建（Step 7「创建」）按名命中同名流水线时，对该条覆盖更新
  本次渲染的 YAML（**全端**，`Created = false`）；②**手动入口**——Step 7「**覆盖更新**」按钮对**已有**
  流水线做 `flow_update_pipeline` **整体覆盖**，把当前口径的完整 YAML（含部署段）推上去（纠偏 / 补部署段）。
- **副作用（①）**：普通重跑也会覆盖远端（含控制台上的人工改动）——即取代历史「命中即复用」口径。
- 用途（②）：补更早建的「只含构建段 + 末尾注释块」的旧流水线，或用户换了部署目标 / 改了配置后纠偏。
- 部署阶段用官方组件（`with: { machineGroup, executeUser, run }`）。
- **破坏性**：更新会一并覆盖流水线名称，界面用**卡内勾选确认**后再提交，不弹模态框。
- **纯覆盖**：微调文本原样提交（无「追加 / 去重」逻辑）；换目标时丢弃旧微调。

## 失败处理

- 未通过 Step 6 闸门：阻断并提示「先完成第 6 步（推送）」。
- 主机组列表读不到 / 无可用主机组 / 未选到目标：**阻断创建、不发请求**，给出可读原因
  （指引修凭据 / 权限后「刷新主机组」）。
- 渲染后残留占位符 / YAML 非法：停在创建前，附上失败原因，**不创建半成品流水线**。
- 面板微调的 YAML 不合法：面板**不校验**（交给云效报回），`status="failed"` + 云效原文。
- 缺部署目标：`failed`，**不发请求**。
- 云效返回业务错误（`errorCode` 为字符串）：原样透出 `errorMessage`
  （如 `1209300 yaml 校验失败…`），便于定位步骤参数。
- 云效 401 / 403：**不重试**（令牌失效引导重配 PAT；无权限硬闯无用）。
- 云效 429 / 限流：按 `docs/pipeline-conventions.md` 的退避约定重试，仍失败即 `failed`。

**幂等总原则**：所有「建」动作**先查后建**；产物即时写进结论文件（失败重跑只补缺失）；**不做静默覆盖**。