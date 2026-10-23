# 移动端（Mobile）流水线 YAML 模板（pipeline-mobile.yml.md）

> 用途：**Step 7 准备阶段**渲染移动端流水线。组织专有值以 [`org-reference.md`](./org-reference.md) 为准，通用口径见 [`pipeline-conventions.md`](./pipeline-conventions.md)。
> 这是**翻译稿**（按企服版 tb 形态语义翻成新版 YAML），**不是企服版 `flow` 原文照抄**——照抄必报 `1209300`。
> 溯源：企服版参考流水线 `pipelineId = 3489317`（`CME_DEV_Deploy_TJCES-Mobile`）。
> 与管理端**同构**，仅 OSS 前缀与产物目录不同。**移动端只在 Step 1 勾选了 Mobile 时才参与。**

## 占位符表（渲染时逐项填值）

| 占位符 | 取值 | 说明 |
|--------|------|------|
| `{pipelineName}` | `{CODE}_DEV_Deploy_{repository}` 展开值，例 `CSXMY_DEV_Deploy_Csxmy-Mobile` | 顶层 `name:`，**提交后不允许改** |
| `{repository}` | 该端仓库名，例 `Csxmy-Mobile` | `sources.repo.name` |
| `{endpoint}` | 该端仓库 HTTPS 克隆地址（Step 3 补查得到的 `httpUrlToRepo`） | `sources.repo.endpoint`；**不带凭据** |
| `{branch}` | 推送分支 `develop`（= `pushBranch`） | `sources.repo.branch` |
| `{codeupSc}` | `r3x6es4esgbsn6ur` | codeup 服务连接 id |
| `{cacheMode}` | 构建集群留空 ⇒ `remote`；`private/*` ⇒ `local` | `cache.cacheMode` |
| `{ossSc}` | `sm7mho5x9ex945rp` | OSS 服务连接 id |
| `{ossRegion}` | `cn-shanghai` | OSS 区域（**必填**） |
| `{ossBucket}` | `tejingcai` | OSS 桶 |
| `{ossPrefix}` | `DEV/{CODE}-Mobile/` 展开值，例 `DEV/CSXMY-Mobile/` | `OSSDelete.prefix` / `OSSUpload.targetFilePath` |
| `{uploadPath}` | `dist/dev/h5` | `OSSUpload.sourceFilePath`（**与管理端不同**） |

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
        # 不写 runsOn ⇒ 云效默认公共北京构建集群。
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

## 渲染后示例（项目缩写 `csxmy`）

```yaml
name: CSXMY_DEV_Deploy_Csxmy-Mobile

sources:
  repo:
    type: codeup
    name: Csxmy-Mobile
    endpoint: https://codeup.aliyun.com/<组织路径>/CSXMY/Csxmy-Mobile.git
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
              prefix: 'DEV/CSXMY-Mobile/'
          oss_upload:
            step: OSSUpload
            name: 上传构建产物到 OSS
            with:
              serviceConnection: sm7mho5x9ex945rp
              region: cn-shanghai
              bucket: 'tejingcai'
              sourceFilePath: 'dist/dev/h5'
              targetFilePath: 'DEV/CSXMY-Mobile/'
```

> 移动端**没有独立部署阶段** ⇒ `artifacts[kind=pipeline].extra.yamlIncludesDeployStage = false`，
> `inputs[]` **不带**「部署目标选择」项。待人工绑定清单见 [`pipeline-conventions.md`](./pipeline-conventions.md)。
