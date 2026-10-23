# 管理端（Admin）流水线 YAML 模板（pipeline-admin.yml.md）

> 用途：**Step 7 准备阶段**渲染管理端流水线。组织专有值以 [`org-reference.md`](./org-reference.md) 为准，通用口径见 [`pipeline-conventions.md`](./pipeline-conventions.md)。
> 这是**翻译稿**（按企服版 tb 形态语义翻成新版 YAML），**不是企服版 `flow` 原文照抄**——照抄必报 `1209300`。
> 溯源：企服版参考流水线 `pipelineId = 3489337`（`CME_DEV_Deploy_TJCES-Admin`）。
> 口径：`NodeBuild` 编译 + `OSSDelete` 清目录 + `OSSUpload` 上传 ⇒ **构建产物上传 OSS 即发布，无独立部署阶段**（`yamlIncludesDeployStage = false`）。

## 占位符表（渲染时逐项填值）

| 占位符 | 取值 | 说明 |
|--------|------|------|
| `{pipelineName}` | `{CODE}_DEV_Deploy_{repository}` 展开值，例 `CSXMY_DEV_Deploy_Csxmy-Admin` | 顶层 `name:`，**提交后不允许改** |
| `{repository}` | 该端仓库名，例 `Csxmy-Admin` | `sources.repo.name` |
| `{endpoint}` | 该端仓库 HTTPS 克隆地址（Step 3 补查得到的 `httpUrlToRepo`） | `sources.repo.endpoint`；**不带凭据** |
| `{branch}` | 推送分支 `develop`（= `pushBranch`） | `sources.repo.branch` |
| `{codeupSc}` | `r3x6es4esgbsn6ur` | codeup 服务连接 id |
| `{cacheMode}` | 构建集群留空 ⇒ `remote`；`private/*` ⇒ `local` | `cache.cacheMode` |
| `{ossSc}` | `sm7mho5x9ex945rp` | OSS 服务连接 id |
| `{ossRegion}` | `cn-shanghai` | OSS 区域（**必填**） |
| `{ossBucket}` | `tejingcai` | OSS 桶 |
| `{ossPrefix}` | `DEV/{CODE}-Admin/` 展开值，例 `DEV/CSXMY-Admin/` | `OSSDelete.prefix` / `OSSUpload.targetFilePath` |
| `{uploadPath}` | `dist` | `OSSUpload.sourceFilePath` |

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
    - /root/.npm
    - /root/.cache
  cacheMode: {cacheMode}

stages:
  build:
    name: 阶段1
    jobs:
      build:
        name: node 编译并上传 OSS
        # 不写 runsOn ⇒ 云效默认公共北京构建集群（企服版用公共集群 K8S-4）。
        steps:
          node_build:
            step: NodeBuild
            name: Node.js 构建
            with:
              versionType: custom
              customNodeVersion: lts/gallium   # = Node 16，对齐企服版 CI_RUNTIME_VERSION
              run: |
                set -e
                npm config set registry https://registry.npmmirror.com
                npm install
                npm run build:dev
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

## 渲染后示例（项目缩写 `csxmy`）

```yaml
name: CSXMY_DEV_Deploy_Csxmy-Admin

sources:
  repo:
    type: codeup
    name: Csxmy-Admin
    endpoint: https://codeup.aliyun.com/<组织路径>/CSXMY/Csxmy-Admin.git
    branch: develop
    triggerEvents: []
    certificate:
      type: serviceConnection
      serviceConnection: r3x6es4esgbsn6ur

cache:
  cacheDirs:
    - /root/.npm
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
              customNodeVersion: lts/gallium
              run: |
                set -e
                npm config set registry https://registry.npmmirror.com
                npm install
                npm run build:dev
          oss_delete:
            step: OSSDelete
            name: 清理 OSS 远端目录
            with:
              serviceConnection: sm7mho5x9ex945rp
              region: cn-shanghai
              bucket: 'tejingcai'
              prefix: 'DEV/CSXMY-Admin/'
          oss_upload:
            step: OSSUpload
            name: 上传构建产物到 OSS
            with:
              serviceConnection: sm7mho5x9ex945rp
              region: cn-shanghai
              bucket: 'tejingcai'
              sourceFilePath: 'dist'
              targetFilePath: 'DEV/CSXMY-Admin/'
```

> 管理端**没有独立部署阶段** ⇒ `artifacts[kind=pipeline].extra.yamlIncludesDeployStage = false`，
> `inputs[]` **不带**「部署目标选择」项。待人工绑定清单见 [`pipeline-conventions.md`](./pipeline-conventions.md)。
