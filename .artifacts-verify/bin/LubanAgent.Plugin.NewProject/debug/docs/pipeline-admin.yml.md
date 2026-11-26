# 管理端（Admin）流水线 YAML 模板（pipeline-admin.yml.md）

> 用途：**Step 7 准备阶段**渲染管理端流水线。组织专有值以 [`org-reference.md`](./org-reference.md) 为准，通用口径见 [`pipeline-conventions.md`](./pipeline-conventions.md)。
> 这是**翻译稿**（按企服版 tb 形态语义翻成新版 YAML），**不是企服版 `flow` 原文照抄**——照抄必报 `1209300`。
> 溯源：企服版参考流水线的 id 与名称见 [`org-reference.md`](./org-reference.md) 的「流水线溯源标注（Step 7）」表（本文件不写具体值）。
> 口径：`NodeBuild` 编译 + `OSSDelete` 清目录 + `OSSUpload` 上传 ⇒ **构建产物上传 OSS 即发布，无独立部署阶段**（`yamlIncludesDeployStage = false`）。

## 占位符表（渲染时逐项填值）

| 占位符 | 取值 | 说明 |
|--------|------|------|
| `{pipelineName}` | `{CODE}_DEV_Deploy_{module}` 展开值，例 `CSXMY_DEV_Deploy_Csxmy-Admin` | 顶层 `name:`，**提交后不允许改** |
| `{repository}` | **本项目单仓仓库名**，例 `Csxmy-Mixed`（三端同值） | `sources.repo.name` |
| `{module}` | 该端仓内模块目录，例 `Csxmy-Admin` | 构建步骤的工作目录；同时是**流水线名后缀** |
| `{endpoint}` | 单仓 HTTPS 克隆地址（Step 3 补查得到的 `httpUrlToRepo`；三端同值） | `sources.repo.endpoint`；**不带凭据** |
| `{branch}` | 推送分支 `develop`（= `pushBranch`） | `sources.repo.branch` |
| `{codeupSc}` | 见 [`org-reference.md`](./org-reference.md)「后端流水线（Service）」表里的 codeup 服务连接 | codeup 服务连接 id |
| `{cacheMode}` | 构建集群留空 ⇒ `remote`；`private/*` ⇒ `local` | `cache.cacheMode` |
| `{ossSc}` | 见 [`org-reference.md`](./org-reference.md)「前端流水线（Admin / Mobile）」表里的 OSS 服务连接 | OSS 服务连接 id |
| `{ossRegion}` | `cn-shanghai` | OSS 区域（**必填**） |
| `{ossBucket}` | 见 [`org-reference.md`](./org-reference.md)「前端流水线（Admin / Mobile）」表里的 OSS bucket | OSS 桶 |
| `{ossPrefix}` | `DEV/{CODE}-Admin/` 展开值，例 `DEV/CSXMY-Admin/` | `OSSDelete.prefix` / `OSSUpload.targetFilePath` |
| `{uploadPath}` | `{module}/dist`（相对**仓根**），例 `Csxmy-Admin/dist` | `OSSUpload.sourceFilePath` |

> 管理端**无**变量段（模板不写顶层 `variables:`）；**无** `runsOn`（构建集群留空 ⇒ 云效默认北京公共集群）。
> 需要私有构建集群时在控制台确认形态后回填（形如 `private/<集群ID>`，回填后 `cacheMode` 取 `local`）。

## 模板正文

```yaml
name: {pipelineName}

sources:
  repo:
    type: codeup
    name: {repository}
    endpoint: {endpoint}
    branch: {branch}
    # 建完不自动运行；需要 push 自动构建时请在云效控制台开启事件触发。
    triggerEvents: []
    certificate:
      type: serviceConnection
      serviceConnection: {codeupSc}

cache:
  cacheDirs:
    - /root/.local/share/pnpm
    - /root/.cache
  cacheMode: {cacheMode}

stages:
  build:
    name: 阶段1
    jobs:
      build:
        name: node 编译并上传 OSS
        # 不写 runsOn ⇒ 云效默认公共北京构建集群。
        steps:
          node_build:
            step: NodeBuild
            name: Node.js 构建
            with:
              versionType: custom
              customNodeVersion: lts/iron      # = Node 20 LTS，对齐管理端 README「Node 18/20 LTS」
              run: |
                set -e
                cd "$PROJECT_DIR/{module}"
                command -v pnpm >/dev/null 2>&1 || npm install -g pnpm
                pnpm install --frozen-lockfile || pnpm install
                pnpm run build:dev
          oss_delete:
            step: OSSDelete
            name: 清理 OSS 远端目录
            with:
              serviceConnection: {ossSc}
              region: {ossRegion}
              bucket: '{ossBucket}'
              prefix: '{ossPrefix}'
          oss_upload:
            step: OSSUpload
            name: 上传构建产物到 OSS
            with:
              serviceConnection: {ossSc}
              region: {ossRegion}
              bucket: '{ossBucket}'
              sourceFilePath: '{uploadPath}'
              targetFilePath: '{ossPrefix}'
```

> ⚠️ 键名别写错：`OSSUpload` 用 `sourceFilePath` / `targetFilePath`（**不是** `localPath` / `prefix`）；`OSSDelete` 用 `prefix`；两者都必填 `region`。
> ⚠️ `NodeBuild` 必填 `with.run`；Node 运行时用 `versionType` + `customNodeVersion`（**不要**写 `nodeVersion` 之类的旧键）。

> **单仓 + pnpm**：`sources.repo` 指向单仓，管理端代码在仓内模块目录 `{module}` 下，构建先 `cd "$PROJECT_DIR/{module}"`。
> 管理端以 `pnpm-lock.yaml` 为准（**不要**改用 npm：仓里残留的 `package-lock.json` 是历史遗留，混用会装出不一致的依赖树）；
> `pnpm-lock.yaml` 与 `package.json` 可能不完全同步，锁文件过期时退回普通 `pnpm install`，不要手工改锁文件。
> 上传源 `{module}/dist` **相对仓根**（`OSSUpload` 的工作目录是仓根，不是模块目录）。

## 渲染后示例（项目缩写 `csxmy`）

```yaml
name: CSXMY_DEV_Deploy_Csxmy-Admin

sources:
  repo:
    type: codeup
    name: Csxmy-Mixed
    endpoint: https://codeup.aliyun.com/<组织路径>/CSXMY/Csxmy-Mixed.git
    branch: develop
    triggerEvents: []
    certificate:
      type: serviceConnection
      serviceConnection: {codeupSc}

cache:
  cacheDirs:
    - /root/.local/share/pnpm
    - /root/.cache
  cacheMode: remote

stages:
  build:
    name: 阶段1
    jobs:
      build:
        name: node 编译并上传 OSS
        steps:
          node_build:
            step: NodeBuild
            name: Node.js 构建
            with:
              versionType: custom
              customNodeVersion: lts/iron
              run: |
                set -e
                cd "$PROJECT_DIR/Csxmy-Admin"
                command -v pnpm >/dev/null 2>&1 || npm install -g pnpm
                pnpm install --frozen-lockfile || pnpm install
                pnpm run build:dev
          oss_delete:
            step: OSSDelete
            name: 清理 OSS 远端目录
            with:
              serviceConnection: {ossSc}
              region: cn-shanghai
              bucket: '{ossBucket}'
              prefix: 'DEV/CSXMY-Admin/'
          oss_upload:
            step: OSSUpload
            name: 上传构建产物到 OSS
            with:
              serviceConnection: {ossSc}
              region: cn-shanghai
              bucket: '{ossBucket}'
              sourceFilePath: 'Csxmy-Admin/dist'
              targetFilePath: 'DEV/CSXMY-Admin/'
```

> 管理端**没有独立部署阶段** ⇒ `artifacts[kind=pipeline].extra.yamlIncludesDeployStage = false`，
> `inputs[]` **不带**「部署目标选择」项。待人工绑定清单见 [`pipeline-conventions.md`](./pipeline-conventions.md)。
