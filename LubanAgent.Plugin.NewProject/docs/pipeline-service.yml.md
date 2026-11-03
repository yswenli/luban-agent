# 后端（Service）流水线 YAML 模板（pipeline-service.yml.md）

> 用途：**Step 7 准备阶段**渲染后端流水线。组织专有值以 [`org-reference.md`](./org-reference.md) 为准，通用口径见 [`pipeline-conventions.md`](./pipeline-conventions.md)。
> 这是**翻译稿**（按企服版 tb 形态语义翻成新版 YAML），**不是企服版 `flow` 原文照抄**——照抄必报 `1209300`。
> 溯源：企服版参考流水线的 id 与名称见 [`org-reference.md`](./org-reference.md) 的「流水线溯源标注（Step 7）」表（本文件不写具体值）。

## 占位符表（渲染时逐项填值）

| 占位符 | 取值 | 说明 |
|--------|------|------|
| `{pipelineName}` | `{CODE}_DEV_Deploy_{module}` 展开值，例 `CSXMY_DEV_Deploy_Csxmy-Service` | 顶层 `name:`，**提交后不允许改**（同名幂等检索与档案名称都依赖它） |
| `{repository}` | **本项目单仓仓库名**，例 `Csxmy-Mixed`（三端流水线同值） | `sources.repo.name` |
| `{module}` | 该端仓内模块目录，例 `Csxmy-Service` | 构建步骤的工作目录；同时是**流水线名后缀**（三端同仓，用仓库名会撞名） |
| `{endpoint}` | 单仓 HTTPS 克隆地址（Step 3 补查得到的 `httpUrlToRepo`；三端同值） | `sources.repo.endpoint`；**不带凭据** |
| `{branch}` | 推送分支 `develop`（= `pushBranch`） | `sources.repo.branch` |
| `{codeupSc}` | 见 [`org-reference.md`](./org-reference.md)「后端流水线（Service）」表里的 codeup 服务连接 | codeup 服务连接 id |
| `{image}` | `{code}-service`，例 `csxmy-service` | 变量 `DOCKER_IMAGE_NAME` |
| `{container}` | `{code}-service` | 变量 `DOCKER_CONTAINER_NAME` |
| `{imageTag}` | `latest` | 变量 `DOCKER_IMAGE_TAG` |
| `{dockerRepo}` | `{branch}` 展开值 ⇒ `develop` | 变量 `DOCKER_REPO_NAME` |
| `{port}` | `7000` | 变量 `APP_PORT` |
| `{harborHost}` | 见 [`org-reference.md`](./org-reference.md)「后端流水线（Service）」表里的 Harbor 域名 | 变量 `HARBOR_HOST` |
| `{cacheMode}` | 构建集群留空 ⇒ `remote`；`private/*` ⇒ `local` | `cache.cacheMode` |
| `{runsOn}` | 构建集群留空 ⇒ **整行不写**（云效默认北京公共集群）；非空 ⇒ 写 `        runsOn: <cluster>`（**8 空格缩进**，与 `steps:` 同级） | 可选行 |
| `{machineGroup}` | 部署主机组 **uuid**（默认值见 [`org-reference.md`](./org-reference.md)「后端流水线（Service）」表里的部署主机组；本步由用户在「部署目标」区选定） | `deploy` 段 `VMDockerDeploy.machineGroup`，**必须带单引号** |

> **部署目标兜底口径**：准备阶段就要把 `{machineGroup}` 填成**具体 uuid**（候选里的默认值 / 唯一合理项），
> 并写进结论 `extra.recommendedDeployTargetUuid`——YAML 必须是**可直接提交**的正文，插件侧已无渲染器与守卫。
> 执行阶段用户改选了目标就按新 uuid 覆盖改写；**仍没有可用 uuid 时不许提交**（不得写 `'<未选择：主机组 uuid>'` 之类占位）：
> 写 `status="failed"` 并说明缺部署目标（提示词硬边界；部署目标取值见 pipeline-conventions.md）。

> **单仓构建上下文**：`sources.repo` 指向的是**单仓**（`{Code}-Mixed`），后端的 `Dockerfile` 与解决方案都在
> 仓内模块目录 `{module}` 下；构建步骤必须先 `cd "$PROJECT_DIR/{module}"`，再用 `-f Dockerfile .` 作上下文。
> `SOLUTION_PATH`（`./WebHost/WebHost.csproj`）等 Dockerfile 内的相对路径**均相对该模块目录**，不要改成相对仓根。

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

# 「变量和缓存」段：变量只写 key / value（可选 secret）——真机核：type 字段不被支持，
# 写了直接 400「Not supported variable type」。私密值用 secret: true。
variables:
  - key: DOCKER_IMAGE_NAME
    value: '{image}'
  - key: DOCKER_IMAGE_TAG
    value: '{imageTag}'
  - key: DOCKER_IMAGE_BASE_VERSION
    value: 'v1.0'
  - key: DOCKER_IMAGE_LOCAL_TAG
    value: 'local-latest'
  - key: DOCKER_REPO_NAME
    value: '{dockerRepo}'
  - key: DOCKER_CONTAINER_NAME
    value: '{container}'
  - key: APP_PORT
    value: '{port}'
  - key: APP_ENTRY
    value: 'WebHost.dll'
  - key: SOLUTION_PATH
    value: './WebHost/WebHost.csproj'
  - key: HARBOR_HOST
    value: '{harborHost}'
  - key: HARBOR_USER
    value: ''            # 出厂为空 —— 需人工在控制台「变量和缓存」补齐
  - key: HARBOR_PASSWORD
    value: ''            # 出厂为空 —— 需人工补齐
    secret: true

cache:
  cacheDirs:
    - /root/.m2
    - /root/.gradle/caches
    - /root/.npm
    - /root/.yarn
    - /go/pkg/mod
    - /root/.cache
  cacheMode: {cacheMode}

stages:
  build:
    name: 构建
    jobs:
      build:
        name: 构建并推送镜像
        # {runsOn} 在此处展开：构建集群留空时整行不写（走云效默认北京公共集群）；
        # 非空时写成 `        runsOn: private/<集群ID>`（8 空格缩进，与下面的 steps: 同级）。
        # 真机核：OpenAPI 对私有集群一律报「runsOn {} 不存在」，故此处默认留空，由人工确认后回填。
        steps:
          login_harbor:
            step: Command
            name: 登录 Harbor
            with:
              run: |
                set -e
                echo "$HARBOR_PASSWORD" | docker login -u "$HARBOR_USER" --password-stdin "$HARBOR_HOST"
          build_image:
            step: Command
            name: 构建并推送镜像
            with:
              run: |
                set -e
                cd "$PROJECT_DIR/{module}"
                docker build -f Dockerfile \
                  -t "$HARBOR_HOST/$DOCKER_REPO_NAME/$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG" .
                docker push "$HARBOR_HOST/$DOCKER_REPO_NAME/$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"

  # ── 以下 deploy 段是「顶格写的独立片段」缩进 2 空格后的结果 ──
  # ⚠️ 必须挂在顶层 stages: 之下（stage id 缩进 2 空格）；顶格写会变成顶层 `deploy:` 键，云效 YAML 校验必失败。
  # ⚠️ machineGroup 必须是主机组 **uuid**（tb 形态数字 id 会被拒，报「该机器组uuid[...]不存在」）。
  deploy:
    name: 部署
    jobs:
      deploy:
        name: 部署到主机组
        component: VMDockerDeploy
        with:
          machineGroup: '{machineGroup}'
          executeUser: root
          run: |
            set -e
            echo "$HARBOR_PASSWORD" | docker login -u "$HARBOR_USER" --password-stdin "$HARBOR_HOST"
            docker pull "$HARBOR_HOST/$DOCKER_REPO_NAME/$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"
            docker rm -f "$DOCKER_CONTAINER_NAME" || true
            docker run -d --name "$DOCKER_CONTAINER_NAME" -p "$APP_PORT:$APP_PORT" \
              -e ASPNETCORE_ENVIRONMENT=dev \
              "$HARBOR_HOST/$DOCKER_REPO_NAME/$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"
```

## 渲染后示例（项目缩写 `csxmy`，部署目标已选）

```yaml
name: CSXMY_DEV_Deploy_Csxmy-Service

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

variables:
  - key: DOCKER_IMAGE_NAME
    value: 'csxmy-service'
  - key: DOCKER_IMAGE_TAG
    value: 'latest'
  - key: DOCKER_REPO_NAME
    value: 'develop'
  - key: DOCKER_CONTAINER_NAME
    value: 'csxmy-service'
  - key: APP_PORT
    value: '7000'
  - key: HARBOR_HOST
    value: '{harborHost}'
  # …（其余 key 同上模板）

cache:
  cacheDirs:
    - /root/.m2
    - /root/.gradle/caches
    - /root/.npm
    - /root/.yarn
    - /go/pkg/mod
    - /root/.cache
  cacheMode: remote

stages:
  build:
    name: 构建
    jobs:
      build:
        name: 构建并推送镜像
        steps: {}          # 实际内容同上方模板
  deploy:
    name: 部署
    jobs:
      deploy:
        name: 部署到主机组
        component: VMDockerDeploy
        with:
          machineGroup: '{machineGroup}'
          executeUser: root
          run: |           # 实际内容同上方模板
            set -e
```

> 后端是**唯一**有独立 `deploy` 阶段（`VMDockerDeploy`）的端 ⇒ 结论文件里 `yamlIncludesDeployStage = true`；
> 部署目标**必选**，`inputs[]` 的选项**同时带数字 `hostGroupId` 与 `uuid`**（数字 id 用于 `flow_get_host_group` 拉机器明细，uuid 用于 YAML）。
> 待人工绑定清单见 [`pipeline-conventions.md`](./pipeline-conventions.md)。
