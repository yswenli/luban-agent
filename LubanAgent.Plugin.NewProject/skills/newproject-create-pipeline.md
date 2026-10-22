# 创建流水线（newproject-create-pipeline）

> 「新建项目」插件原子技能 · 对应 **Step 7** · **v0.30 版**（2026-10-22 真机取证后重写）。

## 何时使用

- 主 skill `newproject-create` 的第 7 步；
- 或**仅补建某端流水线**（例如 Mobile 后补）；
- 或给已有流水线**一键补齐部署阶段**（仅后端，见末节）。

## 前置条件

- Step 6（推送）对该端已成功——界面用 `StepGate.CanCreatePipelines` 拦，门窗口径只认档案事实。
- 插件配置 `newproject.settings.json` 的 `pipelineDefaults[]` 已含该端默认值（端口 / 容器名 / 服务连接 / OSS 目录 / 主机组等）。

> **不再依赖参考流水线**：历史口径（`pipelineReferences` + `flow_clone_pipeline`）已作废。
> 云效控制台里现存流水线**全部是 tb 形态**，其 `flow` 原文不可提交（报 `1209300 校验失败`），
> 故改为「**内嵌新版 YAML 模板 + 占位符渲染**」再创建。

## 执行（云效相关步骤：委托 MCP 工具，PRD §4.10.7 / §4.11.3 注⑤）

对所选**每个端**（最多 3 条）：

1. **先检索同名流水线**：`mcp_yunxiao_flow_list_pipelines`（`pipelineName=` 服务端过滤可用），命中则走更新而非重复创建（幂等）。
2. **渲染模板**：按端取内嵌新版 YAML（`PipelineTemplateLibrary`），用 `pipelineDefaults[end]` + 项目档案展开占位符
   （名称 / 仓库地址 / 分支 / 服务连接 / 变量 / 缓存 / OSS 目录 / `runsOn`）。
3. **创建**：`mcp_yunxiao_flow_create_pipeline`（`name` + `content` + `organizationId`）；
   已存在则 `mcp_yunxiao_flow_update_pipeline`（`pipelineId` + `name` + `content`）。
4. 需要时 `mcp_yunxiao_flow_list_pipelines` / 详情接口复核；回写档案（流水线 ID 与 `RemoteArtifactKind.Pipeline`）。

界面提供 **YAML 预览**，可点「**编辑 YAML…**」在独立窗口里**手工微调**选中端的提交原文（v0.31）：
按端、会话内保留，可「重置为模板」；提交前只做最小校验（非空 / 无 Tab / 有顶层 `stages:` / `name:` 与模板名一致），
真正语法错误由云效原样报回（`1209300`）。创建与补齐**都以微调后的原文为准**；Agent 侧同样先渲染再提交。

## 结果

- 所选端**各 1 条、最多 3 条**流水线；名称含项目缩写，便于识别。

## 重要约束（真机口径）

- **变量不能带 `type`**：顶层 `variables:` 只写 `key` / `value`，可选 `secret: true`（私密变量）。
  写了 `type:` 会被拒（`Not supported variable type`）。
- **`runsOn` 默认不写**（＝云效默认北京公共集群）。OpenAPI 校验对**私有集群一律报「runsOn 不存在」**，
  故私有集群值只能人工在 `pipelineDefaults[].buildCluster` 回填（整串照写，如 `private/<集群ID>`）。
- **缓存模式**随之推导：`private*` ⇒ `cacheMode: local`，否则 `remote`。
- 后台模板只跑 `docker build`（`Command` 步骤 + `HARBOR_*` 变量），需构建机可用 docker daemon。
- 前端模板：`NodeBuild`（`versionType: custom` + `customNodeVersion: lts/gallium`）→ `OSSDelete`（`prefix`）→ `OSSUpload`
  （`sourceFilePath` / `targetFilePath`），必须带 `region`。
- **部署阶段默认禁用**（PRD §6 #14）：首次创建的 YAML **只含构建阶段**，部署阶段以注释块附在末尾；
  同时产出「**待人工绑定清单**」（服务连接 / 构建集群 / Harbor 凭据 / 主机组 uuid）。
- 建完流水线**不自动运行**（PRD §4.11.3 注⑥，`triggerEvents: []`）；需排障时 Agent 可即席调用 `mcp_yunxiao_flow_run_pipeline`。
- 中止 / 清理时**远端流水线不自动删除**，只列孤儿清单。

## 一键补齐部署阶段（仅后端）

- 前端两端**无独立部署阶段**（构建产物上传 OSS 即发布），只对 Service 有效。
- 前置：`pipelineDefaults[Service].deployMachineGroup` 必须是**主机组 uuid**
  （tb 的数字 id 如 `226948` 会被拒：`该机器组uuid[226948]不存在`）。未绑定时**直接给可读失败、不发请求**。
- 补齐走 `mcp_yunxiao_flow_update_pipeline`，部署阶段用官方组件 `VMDockerDeploy`
  （`with: { machineGroup, executeUser: root, run }`）。
- **注意**：更新会**一并覆盖流水线名称**，因此必须回传现有名称（界面用卡内勾选确认后再提交）。
- **与手工微调叠加**：若该端有微调文本，则以微调文本为基础、在末尾**追加**部署阶段片段（缩进在 `stages:` 之下）；
  若微调文本里已自己写了 `VMDockerDeploy`（自备部署阶段）或已有 `deploy` 阶段（换了组件也算），则不再重复追加。

## 失败处理

- 未通过 Step 6：阻断并提示「先完成第 6 步（推送）」。
- 渲染后残留占位符 / YAML 非法：停在创建前，附上失败原因，不创建半成品流水线。
- 微调文本缺 `stages:` / 用了 Tab / 改了 `name:`：提交前拦下（不发请求），提示「恢复模板」或改配置 `pipelineNameFormat`。
- 云效返回业务错误（`errorCode` 为字符串）：原样透出 `errorMessage`（如 `1209300 yaml校验失败…`），便于定位步骤参数。