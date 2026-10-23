# 组织专有值总表（org-reference）

> **这是"集中改这里、不必重发插件"的唯一入口。**
> 端口 / Harbor / 服务连接 / OSS 桶 / 主机组 uuid / 模板仓名 / 流水线 id 等组织专有值，**只在本文件与 `pipeline-*.yml.md` 里各存一份**；
> 技能正文（`skills/*.md`）不复述这些值。
>
> 「取值来源」列里的插件配置项名（如 `templates[].repository`）是**历史出处**，用于追溯；当前运行期这些值**以本文件为准**。

---

## 1. 模板仓（Step 3）

| 项 | 值 | 取值来源 | 生效位置 |
|----|----|----------|----------|
| 模板库定位约定 | **必须先全量拉取模板库列表**（`templateType=1`，`page=1`，`perPage=100`），再在客户端按名匹配；**没有 `search` 入参，不能指望服务端按名过滤** | 设计文档 §5.2 第 1 条 | Step 3 准备阶段定位 `templateProjectId` |
| 后端模板仓名 | `TJCES-Service` | `templates[end=Service].repository` | 建库时作 `templateProjectId` |
| 管理端模板仓名 | `TJCES-Admin` | `templates[end=Admin].repository` | 同上 |
| 移动端模板仓名 | `TJCES-Mobile` | `templates[end=Mobile].repository` | 同上（**仅当 Step 1 勾选了移动端才参与**） |
| 模板仓 ID 登记（可选） | **留空** ⇒ 按名匹配；登记了整数 ID 则以 ID 精确匹配（**必须命中模板库，否则阻断**） | `templates[].projectId` | 与上面同名，登记优先 |
| 模板 pin 登记（可选） | **留空** ⇒ 运行期解析（见第 3 节）；登记了值则**必须真实存在**，否则阻断 | `templates[].branch` | 建库后的 `git clone -b <pin>` |

> 模板仓的**全路径**（如 `tjc/TJCES-Service`）与**默认分支名**不在此登记：**运行期从模板库条目 / 模板仓分支列表解析**，不要臆造。

---

## 2. 项目命名（Step 1 / Step 3 / Step 6）

| 项 | 值 | 取值来源 | 生效位置 |
|----|----|----------|----------|
| 代码组名格式 | `{CODE}`（全大写缩写，如 `csxmy` → `CSXMY`） | `groupNameFormat` | Step 3 建代码组的 `name` / `path` |
| 代码组父级组 ID | **留空** ⇒ 建在组织根下 | `groupParentId` | Step 3 建代码组的 `parentId` |
| 仓库名格式 | `{Code}-{end}`（**首字母大写**，如 `csxmy` → `Csxmy-Service` / `Csxmy-Admin` / `Csxmy-Mobile`）⚠️ **是 `{Code}` 不是 `{code}`** | `repositoryNameFormat` | 云效仓库 `name` / `path`；本地仓库目录名（`<工作区>\<仓库名>`） |
| 端后缀 `{end}` | `Service` / `Admin` / `Mobile`（枚举名原文，首字母大写） | 代码 `EndSuffix(end) => end.ToString()` | 拼进仓库名 |
| 令牌语义 | `{code}` 原文 / `{Code}` 首字母大写 / `{CODE}` 全大写 / `{name}` 项目中文名 | 代码 `Capitalize` / `ToUpperInvariant` | 所有格式串展开 |
| 推送分支 | `develop` | `pushBranch`（留空 ⇒ 退回该端仓库默认分支） | 建库时的默认分支、Step 6 的 `checkout`/`push` 目标、流水线 `{branch}` |
| 默认可见性 | `internal`（组织内公开）；另一档 `private`（私有） | `defaultVisibility` | Step 3 建库 `visibility` |

> 组名与库名**刻意区分**：组名 `CSXMY`（全大写），库名 `Csxmy-Service`（首字母大写）。

---

## 3. pin 择优（Step 3）

| 项 | 值 | 取值来源 | 生效位置 |
|----|----|----------|----------|
| 择优顺序 | `develop` → `master` → `main` → **模板默认分支**（`list_branches` 里 `defaultBranch=true` 的那条） | 代码 `PreferredPins` | 决定克隆用哪个分支 |
| 登记值校验 | 登记了 `templates[].branch` 时**必须真实存在**（**区分大小写**），否则**阻断**、不静默回退 | 设计文档 §11 | Step 3 准备阶段即判失败 |
| 全无可用 | 既无 `develop` / `master` / `main`，也无默认分支标记 ⇒ **阻断**，不猜测 | 代码 `ResolvePinAsync` | 同上 |
| 结论回显 | 记「取到了哪个、依据是什么」（如 `按开发约定取模板 develop`） | 代码 `Source` 文案 | `artifacts[].extra.resolvedBranchSource` |

---

## 4. 占位符规则（Step 3 克隆后）

| 项 | 值 | 取值来源 | 生效位置 |
|----|----|----------|----------|
| 替换表（字面量、**区分大小写**） | `TJCES` → `{CODE}`；`Tjces` → `{Code}`；`tjces` → `{code}` | `placeholderRules[]` | 已克隆仓库的**文件内容 + 文件/目录名** |
| 替换顺序 | 按 **`From` 长度降序**（长键优先，避免短键先吃掉长键）；命中即字面量替换，**不做正则** | `placeholderRules[]` | 助手按本节执行 |
| 作用对象 | 内容替换 → **文件改名** → **目录改名**（目录按路径深度**倒序**，自底向上避免路径失效） | 助手按本节执行 | 同上 |
| 跳过目录 | `.git`、`.vs`、`.idea`、`.vscode`、`node_modules`、`bin`、`obj`、`packages`、`TestResults`、`dist`、`build` | 助手按本节执行 | 不进入这些目录 |
| 只改文本 | 按扩展名白名单（`.cs/.csproj/.sln/.slnx/.props/.targets/.config/.resx/.manifest/.json/.xml/.yml/.yaml/.toml/.ini/.env/.editorconfig/.md/.txt/.sql/.sh/.ps1/.bat/.cmd/.dockerfile/.html/.htm/.css/.scss/.less/.js/.mjs/.cjs/.ts/.tsx/.jsx/.vue/.razor/.cshtml/.xaml/.axaml/.java/.kt/.kts/.gradle/.properties/.pro/.plist/.pbxproj/.xcconfig/.swift/.dart/.go/.py/.rb/.php`）+ 无扩展名白名单（`Dockerfile`、`.gitignore`、`.gitattributes`、`.npmrc`、`.dockerignore`、`Makefile`、`LICENSE`、`README`） | 助手按本节执行 | 其余文件一律不改写 |
| 单文件上限 | 4 MB（超过即跳过） | 助手按本节执行 | 同上 |
| 编码边界 | **UTF-16 / UTF-32（BOM）跳过**；前 8 KB 出现 `NUL` 的二进制跳过；**UTF-8 BOM 原样保留** | 助手按本节执行 | 宁可漏改也不破坏文件 |
| 改名冲突 | 改名目标已存在 ⇒ **放弃该次改名**并告警（不覆盖） | 助手按本节执行 | 同上 |
| 幂等要求 | 替换前统计命中数、替换后**复查「残余命中 = 0」**；结论文件 `checks[]` 必须含 `placeholder-residual` 且 `ok=true` | 设计文档 §6.2 | Step 3 执行续跑的通过判定 |

---

## 5. 后端流水线（Service，Step 7）

| 项 | 值 | 取值来源 | 生效位置 |
|----|----|----------|----------|
| 应用端口 | `7000` | `pipelineDefaults[Service].appPort` | 变量 `APP_PORT`；`docker run -p 7000:7000` |
| 镜像名 | `{code}-service`（如 `csxmy-service`） | `imageName` | 变量 `DOCKER_IMAGE_NAME` |
| 容器名 | `{code}-service` | `containerName` | 变量 `DOCKER_CONTAINER_NAME` |
| 镜像 tag | `latest` | `imageTag` | 变量 `DOCKER_IMAGE_TAG` |
| dockerRepo（Harbor 项目/命名空间） | `{branch}` ⇒ 展开为 `develop` | `dockerRepo`（默认 `{branch}`） | 变量 `DOCKER_REPO_NAME` |
| Harbor 域名 | `harbor.tjingcai.com` | `harborHost` | 变量 `HARBOR_HOST`；`docker login/push/pull` 前缀 |
| Harbor 凭据 | 出厂为空：`HARBOR_USER` / `HARBOR_PASSWORD`（后者 `secret: true`） | 模板出厂值 | **必须人工在控制台「变量和缓存」补齐**（见待绑定清单） |
| codeup 服务连接 | `r3x6es4esgbsn6ur`（组织内 codeup 服务连接，受理编号 496035） | `codeupServiceConnection` | `sources.repo.certificate.serviceConnection` |
| 构建集群 `buildCluster` | **留空** ⇒ 不写 `runsOn`（云效默认北京公共集群，`cacheMode: remote`）。企服版取证私有集群值（注释记载 `buildNodeGroup=PM-SielV3Iovlo4Au4J`）**故意留空**，需人工在控制台确认形态后回填（形如 `private/<集群ID>`，回填后 `cacheMode` 取 `local`） | `buildCluster` | `jobs.<id>.runsOn`（可选行） |
| 部署主机组（默认） | `tIPKOJBbwwOVKVia`（主机组 id `226948`，名称「移动云DEV主机组」，1 台机器） | `deployMachineGroup`（**必须是主机组 uuid**） | `deploy` 段 `VMDockerDeploy.machineGroup` |
| 部署目标下拉 | 创建**之前**在 Step 7「部署目标」区从主机组列表选；会话未选且配置留空 ⇒ **创建被阻断** | 设计文档 §5.4 | 同上一行 |

---

## 6. 前端流水线（Admin / Mobile，Step 7）

| 项 | 值 | 取值来源 | 生效位置 |
|----|----|----------|----------|
| OSS 服务连接 | `sm7mho5x9ex945rp`（受理编号 520828） | `ossServiceConnection` | `OSSDelete` / `OSSUpload` 的 `serviceConnection` |
| OSS bucket | `tejingcai` | `ossBucket` | 同上 |
| OSS region | `cn-shanghai` | `ossRegion` | 同上（**必填**） |
| 管理端 OSS 目录（前缀） | `DEV/{CODE}-Admin/`（如 `DEV/CSXMY-Admin/`） | `ossPrefix` | `OSSDelete.prefix` / `OSSUpload.targetFilePath` |
| 移动端 OSS 目录（前缀） | `DEV/{CODE}-Mobile/`（如 `DEV/CSXMY-Mobile/`） | `ossPrefix` | 同上 |
| 管理端上传源目录 | `dist` | `uploadPath` | `OSSUpload.sourceFilePath` |
| 移动端上传源目录 | `dist/dev/h5` | `uploadPath` | 同上 |
| Node 运行时 | `versionType: custom` + `customNodeVersion: lts/gallium`（= Node 16，对齐企服版 `CI_RUNTIME_VERSION`） | 模板出厂值 | `NodeBuild` 的 `with` |
| 构建集群 | **留空** ⇒ 不写 `runsOn`（云效默认北京公共集群）；企服版前端用公共集群 `K8S-4` | `buildCluster` | 与后端同口径 |
| codeup 服务连接 | `r3x6es4esgbsn6ur`（与后端同一条） | `codeupServiceConnection` | `sources.repo.certificate.serviceConnection` |
| 独立部署阶段 | **无**（构建产物上传 OSS 即发布） | 设计文档 §5.4 / 代码 `DeployStageYaml` | `yamlIncludesDeployStage = false` |

---

## 7. 构建姿势

| 端 | 姿势 | 生效位置 |
|----|------|----------|
| 后端（Service） | `dotnet build`（**Step 5 本地编译检查**用；CI 侧是 `docker build -f WebHost/Dockerfile`） | Step 5 |
| 管理端（Admin） | Node 16 + npm 系客户端：**`fnm exec --using=16 -- npm.cmd …`**；构建命令 `npm run build:dev` | Step 5 与控制台作业 |
| 移动端（Mobile） | 同管理端（`fnm exec --using=16 -- npm.cmd …` + `npm run build:dev`） | 同上 |
| CI 侧 npm 源 | `npm config set registry https://registry.npmmirror.com`（写死在模板 `NodeBuild.with.run` 里） | `pipeline-admin.yml.md` / `pipeline-mobile.yml.md` |

---

## 8. 提交身份（Step 6）

| 项 | 值 | 取值来源 | 生效位置 |
|----|----|----------|----------|
| 提交者姓名 | 优先本机 `git config user.name`；缺失时兜底 `LuBan Agent` | `commitAuthorName` | `git -c user.name=... commit` |
| 提交者邮箱 | 优先本机 `git config user.email`；缺失时兜底 `luban-agent@local` | `commitAuthorEmail` | `git -c user.email=... commit` |
| 兜底设置方式 | **在本仓库级**设置（不碰用户全局配置） | 设计文档 §5.3 | Step 6 |
| 提交信息格式 | `chore: 初始化 {项目名}（{端标签}）——由「新建项目」向导按模板 {模板仓名} 生成`<br>端标签取中文：`后端` / `管理端` / `移动端` | 代码 `EndLabel` + 提交信息模板 | Step 6 commit |

---

## 9. 流水线溯源标注（Step 7）

| 端 | 企服版参考流水线 `pipelineId` | 企服版名称 | 说明 |
|----|------------------------------|------------|------|
| Service | `3481236` | `CME_DEV_Deploy_TJCES-Service(7000)` | 仅作「这条模板翻译自哪条企服版流水线」的**可追溯标注** |
| Admin | `3489337` | `CME_DEV_Deploy_TJCES-Admin` | 同上 |
| Mobile | `3489317` | `CME_DEV_Deploy_TJCES-Mobile` | 同上 |

> 旧组 `DEV_Deploy_TJCES-*` **不作模板**。
> ⚠️ 三端 YAML 是**翻译稿**（按企服版 tb 形态语义翻成新版 YAML），**不是企服版 `flow` 原文照抄**——照抄必报 `1209300`。

---

## 10. 运行时形态约定（写 YAML 时别踩的坑）

| 约定 | 说明 |
|------|------|
| 变量**不带** `type` | 写了报 `Not supported variable type`（400）；私密值用 `secret: true` |
| `Command` / `NodeBuild` 必填 `with.run` | 缺了直接失败 |
| `OSSUpload` 的键名 | `sourceFilePath` / `targetFilePath`（**不是** `localPath` / `prefix`） |
| `OSSDelete` 的键名 | `prefix`；两者都必填 `region` |
| `runsOn` 三形态 | 默认环境标量 `public/cn-beijing`、容器 `{group, container}`、VM `{group, labels, vm}`；**私有集群任何写法都被 OpenAPI 拒绝**（报「runsOn {} 不存在」） ⇒ 模板默认**不写** `runsOn` |
| `VMDockerDeploy.machineGroup` | **必须是主机组 uuid**；tb 形态数字 id（如 `226948`）会被拒（报「该机器组uuid[226948]不存在」） |
| `flow_get_host_group` 入参 | 只吃**数字 hostGroupId**（传 uuid 会 400） ⇒ 交互项必须**同时**带数字 id 与 uuid |
| YAML 缩进 | 一律**空格**，不能出现 Tab；三端 YAML 必须含顶层 `stages:` |
