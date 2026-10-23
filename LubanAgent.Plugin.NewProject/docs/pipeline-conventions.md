# 流水线通用约定（pipeline-conventions）

> **Step 7 必读**：`yamlIncludesDeployStage` 与 `pendingBindings` 的判定依据都在本文件。
> 组织专有值以 [`org-reference.md`](./org-reference.md) 为准；三端 YAML 模板见 [`pipeline-service.yml.md`](./pipeline-service.yml.md) / [`pipeline-admin.yml.md`](./pipeline-admin.yml.md) / [`pipeline-mobile.yml.md`](./pipeline-mobile.yml.md)。

## 1. 名称格式

| 项 | 格式 | 展开示例（`CODE=CSXMY`，`repository=Csxmy-Service`） |
|----|------|------------------------------------------------------|
| 流水线名 | `{CODE}_DEV_Deploy_{repository}` | `CSXMY_DEV_Deploy_Csxmy-Service` |
| 兜底（格式为空时） | `{CODE}_DEV_Deploy_{repository}` | 同上 |

- 顶层 `name:` 就是流水线名，**创建后不允许改**：同名幂等检索与档案里记录的名称都依赖它。
- 确需改名 ⇒ 改本文件的格式串后**重跑 Step 7**（不要只改某一条 YAML 的 `name:`）。
- 对齐组织既有命名习惯（如企服版 `CME_DEV_Deploy_TJCES-Service(7000)`）。

## 2. 变量与缓存

| 口径 | 说明 |
|------|------|
| 变量写法 | 顶层 `variables:` 是**列表**，每项只写 `key` / `value`，私密值加 `secret: true` |
| ⚠️ 不能写 `type` | 写了直接 400：`Not supported variable type` |
| 后端变量集 | `DOCKER_IMAGE_NAME` / `DOCKER_IMAGE_TAG` / `DOCKER_IMAGE_BASE_VERSION` / `DOCKER_IMAGE_LOCAL_TAG` / `DOCKER_REPO_NAME` / `DOCKER_CONTAINER_NAME` / `APP_PORT` / `APP_ENTRY` / `SOLUTION_PATH` / `HARBOR_HOST` / `HARBOR_USER` / `HARBOR_PASSWORD` |
| 管理端 / 移动端 | **不写**顶层 `variables:`（模板里没有） |
| 缓存写法 | 顶层 `cache:` = `cacheDirs`（列表）+ `cacheMode` |
| `cacheMode` 取值 | 构建集群留空（公共集群）⇒ `remote`；`buildCluster` 以 `private` 开头 ⇒ `local` |
| 缓存目录 | 后端：`/root/.m2`、`/root/.gradle/caches`、`/root/.npm`、`/root/.yarn`、`/go/pkg/mod`、`/root/.cache`；前端：`/root/.npm`、`/root/.cache` |

## 3. 哪端有独立部署阶段（`yamlIncludesDeployStage` 的判定依据）

| 端 | 有独立部署阶段？ | 阶段内容 | `yamlIncludesDeployStage` | 是否必选部署目标 |
|----|------------------|----------|---------------------------|------------------|
| **Service（后端）** | ✅ 有 | `stages.deploy` · `VMDockerDeploy`（主机 Docker 部署，对应企服版 `docker-deploy`） | `true` | **必须选**（缺目标 ⇒ 不提交、写 `failed`；v0.33 由助手按提示词硬边界判定，插件无守卫） |
| Admin（管理端） | ❌ 无 | 构建产物上传 OSS 即发布 | `false` | 不需要 |
| Mobile（移动端） | ❌ 无 | 同上 | `false` | 不需要 |

- 有部署阶段的端：`deploy` 段**随创建一起写入** YAML（不是"先建残缺流水线、再事后补齐"）。
- `VMDockerDeploy.machineGroup` **必须是主机组 uuid**（tb 形态数字 id `226948` 会被拒，报「该机器组uuid[226948]不存在」）。
- 部署目标在**创建之前**由用户在 Step 7「部署目标」区从主机组列表里选；`inputs[]` 的选项**必须同时带数字 `hostGroupId` 与 `uuid`**：
  - 数字 `hostGroupId` ⇒ 调 `mcp_yunxiao_flow_get_host_group(hostGroupId=...)` 拉机器明细（**传 uuid 会 400**）；
  - `uuid` ⇒ 写进 YAML 的 `machineGroup`。
- 聊天路径（无面板）：把主机组候选（名称 / 机器数 / 数字 id / uuid）**直接列在对话回复里**，用户回一句即可，助手同会话继续创建。
- 结论文件字段：`extra.recommendedDeployTargetHostGroupId`（数字 id）、`extra.recommendedDeployTargetUuid`（uuid）。

## 4. 待人工绑定清单（`pendingBindings` 的依据：留空即需人工绑定）

> 这些值**不能**由 OpenAPI 校验通过（私有构建集群报「runsOn {} 不存在」；主机组 uuid 之外的写法报「该机器组uuid[x]不存在」），
> 必须由人工在云效控制台确认后回填/开启。助手把它们**逐条写进 `artifacts[kind=pipeline].extra.pendingBindings`**，面板照显。

### 4.1 后端（Service）

| # | 待绑定项 | 当前值 / 状态 | 要做的事 |
|---|----------|---------------|----------|
| 1 | 服务连接（代码源 codeup） | `r3x6es4esgbsn6ur` | 确认**该项目仓库在授权范围内** |
| 2 | 构建阶段 `runsOn` | **留空**（＝云效默认北京公共集群，只跑 `docker build`，需构建机有可用 docker daemon） | 如需私有集群：在控制台确认实际形态（形如 `private/<集群ID>`）后回填 `runsOn`；企服版取证注释记载 `buildNodeGroup=PM-SielV3Iovlo4Au4J`，**以控制台实际值为准** |
| 3 | Harbor 凭据 | 变量 `HARBOR_USER` / `HARBOR_PASSWORD` **出厂为空**（后者已标 `secret: true`） | 在控制台流水线「变量和缓存」补齐 |
| 4 | 部署主机组 | `machineGroup='tIPKOJBbwwOVKVia'`（主机组 id `226948`，名称「移动云DEV主机组」，1 台机器） | 已写进 `deploy` 段；如需换目标 ⇒ 在 Step 7「部署目标」区重选后用**覆盖更新**重推 YAML |
| 5 | 代码源事件触发 | `triggerEvents: []`（**建完不自动运行**） | 需要 push 自动构建时在控制台开启 |

### 4.2 管理端（Admin）

| # | 待绑定项 | 当前值 / 状态 | 要做的事 |
|---|----------|---------------|----------|
| 1 | 服务连接（OSS） | `sm7mho5x9ex945rp` | 确认授权范围含 region `cn-shanghai` / bucket `tejingcai` |
| 2 | OSS 目录 | 上传源 `sourceFilePath='dist'` → 远端 `targetFilePath='DEV/{CODE}-Admin/'`（`OSSDelete` 以同一前缀做目录清理） | 确认前缀符合预期 |
| 3 | 构建集群 | 模板**不写** `runsOn` ⇒ 云效默认公共北京集群（企服版用公共集群 `K8S-4`） | 如需私有集群：确认形态后回填（形如 `private/<集群ID>`） |
| 4 | Node 运行时 | `versionType=custom` + `customNodeVersion=lts/gallium`（= Node 16，对齐企服版 `CI_RUNTIME_VERSION`） | 确认模板仓在 Node 16 下可构建 |
| 5 | 代码源事件触发 | `triggerEvents: []` | 需要 push 自动构建时在控制台开启 |

### 4.3 移动端（Mobile）

| # | 待绑定项 | 当前值 / 状态 | 要做的事 |
|---|----------|---------------|----------|
| 1 | 服务连接（OSS） | `sm7mho5x9ex945rp` | 确认授权范围含 region `cn-shanghai` / bucket `tejingcai` |
| 2 | OSS 目录 | 上传源 `sourceFilePath='dist/dev/h5'` → 远端 `targetFilePath='DEV/{CODE}-Mobile/'` | 确认前缀符合预期 |
| 3 | 构建集群 | 模板**不写** `runsOn` ⇒ 云效默认公共北京集群 | 如需私有集群：确认形态后回填 |
| 4 | Node 运行时 | `versionType=custom` + `customNodeVersion=lts/gallium`（= Node 16） | 确认模板仓在 Node 16 下可构建 |
| 5 | 代码源事件触发 | `triggerEvents: []` | 需要 push 自动构建时在控制台开启 |

## 5. 覆盖更新口径

| 场景 | 动作 |
|------|------|
| 首次创建 | `mcp_yunxiao_flow_create_pipeline`，**逐端 1 条，最多 3 条**；**建完不自动运行** |
| 同名已存在 / 重跑 | **按档案里的 `PipelineId` 直改** ⇒ `mcp_yunxiao_flow_update_pipeline`（覆盖更新），**不要**重复 `create` |
| 改了部署目标 | 必须用「覆盖更新」把新 YAML 推上去（YAML 里写死了 uuid；旧 YAML 的 uuid 已失效） |
| 面板手工微调 | 微调文本随 `answers` 进执行续跑；**面板不做即时校验**（`name:` 一致性 / Tab / 顶层 `stages:` 一律**交给云效报回**，失败即 `status="failed"`，文案取云效原文，如 `1209300`） |
| 缺部署目标（后端） | **不提交、直接失败**（`status="failed"`）；v0.33 由助手按提示词硬边界判定——**不得**把 `'<未选择：主机组 uuid>'` 之类占位发到云效 |
| 远端产物 | **一律不自动删除**（重跑/中止都只留孤儿清单） |

- 结论文件 `artifacts[kind=pipeline]` 的字段：`end` / `name`（→ 流水线名）/ `yaml` / `externalId`（→ `PipelineId`）/ `webUrl` / `extra.yamlIncludesDeployStage` / `extra.pendingBindings`。
- ⚠️ **逐端完备性**：`kind=pipeline` 必须覆盖「需创建的端」全集（勾选了几端就几条），**缺端即整步 `failed`**。
- ⚠️ Step 7 **没有可独立核对的本地证据**（流水线在远端）⇒ 通过判定 = 助手自报 + Step 6 结论文件闸门；这是**已如实记录的残余风险**。缓解手段：每个 MCP 调用的返回摘要写进 `checks[].detail`。

## 6. `pipelineId` 溯源标注

三端 YAML 是**翻译稿**，下列企服版参考流水线 id 仅作**溯源标注**（不参与渲染、不写进 YAML）：

| 端 | 企服版 `pipelineId` | 企服版名称 |
|----|---------------------|------------|
| Service | `3481236` | `CME_DEV_Deploy_TJCES-Service(7000)` |
| Admin | `3489337` | `CME_DEV_Deploy_TJCES-Admin` |
| Mobile | `3489317` | `CME_DEV_Deploy_TJCES-Mobile` |

> 旧组 `DEV_Deploy_TJCES-*` **不作模板**。
> 溯源标注的用途：出问题时可回企服版流水线对照语义；**不要**把企服版 tb 形态 `flow` 原文直接提交给云效（必报 `1209300`）。
