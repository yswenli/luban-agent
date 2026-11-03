# Step 3 知识正文：按模板建库并克隆（step3-repositories）

> 配套技能：`skills/newproject-clone-template.md`。组织专有值一律以 [`org-reference.md`](./org-reference.md) 为准。
> Step 3 的形状是「**一次只读准备下发 + 用户确认后同会话执行续跑**」（术语上不叫"两轮"，实现上答复后助手会在同一会话再收到一次提示词）。

## 0. 两条铁律

1. **先查后建**：所有"建"动作（建组 / 建库）都必须先查；命中即**复用**，不重复建、不报重名错。
2. **产物即时落账**：仓库建好 / 克隆完成就把事实写进结论文件 `artifacts[]`；中途失败时已成功的账目**不能丢**（重跑只补缺失）。

> **单仓形态**：一个项目只建**一个**仓库（模板仓 `TJCES-Mixed`，目标仓 `{Code}-Mixed`）。三端是**仓内模块子目录**，
> 不是三个仓库：建库、克隆、推送各**只做一次**；逐端只体现在「模块目录核对」与结论文件的 `modulePath`。详见
> [`org-reference.md`](./org-reference.md) 的「模板仓与仓内模块」表。

---

## 1. 准备阶段 · 只读（不创建任何云效资源）

工具说明：以下 `mcp_yunxiao_codeup_*` 都是**只读**调用。

### 1.1 定位模板仓（必须全量拉取模板库）

```
mcp_yunxiao_codeup_list_template_repos(templateType=1, page=1, perPage=100)
```

- ⚠️ **该接口没有 `search` 入参**，不能指望服务端按名过滤 ⇒ **全量拉取后在客户端按名匹配**。
- 匹配口径：`name` **忽略大小写**精确相等（例：`TJCES-Mixed`）。
- 若模板仓登记了整数 ID，则以 **ID 精确匹配**为准（`artifacts[].extra.templateProjectId`）。
- 判定：
  - 模板库列表**为空** ⇒ **阻断**，`status="failed"`，message 写「云效自定义模板库为空（templateType=1）：请先在云效把模板仓登记为模板」。
  - 未命中名称 ⇒ **阻断**，message 里**列出模板库现有条目**（`name` / `path` / `projectId`），让用户能一眼看出该改哪个值
    （常见原因：模板仓 `TJCES-Mixed` 尚未登记为模板库条目 `templateType=1`，需组织侧先登记）。
- 模板仓名见 [`org-reference.md`](./org-reference.md) 的「模板仓与仓内模块」表；`templatePath` 与「模板默认分支名」**运行期从模板库条目 / 分支列表解析**，不要臆造。
- **模板仓只有一个**：不按端分别定位，也不因 Step 1 的端勾选而改变。端勾选只影响克隆后保留哪些**模块目录**（见 2.7）。

### 1.2 解析 pin 分支

```
mcp_yunxiao_codeup_list_branches(repositoryId=<templateProjectId>, page=1, perPage=100)
```

按 [`org-reference.md`](./org-reference.md)「pin 择优」表给出的顺序择优：

1. 若登记了 pin（`templates[].branch`）：**必须精确存在**（区分大小写）⇒ 命中即用（`resolvedBranchSource="配置登记"`）；**不存在即阻断**，列出模板仓现有分支，**不静默回退**。
2. 未登记：依次找 `develop` → `master` → `main`（精确、区分大小写；文案如 `按开发约定取模板 develop`）。
3. 都没有：取 `defaultBranch=true` 的那条（文案 `取模板默认分支 <name>`）。
4. 一条都没有 ⇒ **阻断**，message 列出模板仓现有分支。

### 1.3 查代码组 / 查仓库 / 查仓库目录

```
mcp_yunxiao_codeup_list_groups(search=<代码组名>)      # 代码组名格式见 org-reference.md「项目命名」表
mcp_yunxiao_codeup_search_repos(search=<仓库名>)        # 单仓：只查一次
mcp_yunxiao_codeup_get_repo(repositoryId=<命中项 id>)   # 仅当 search 命中时：取 httpUrlToRepo 与 defaultBranch
```

- 仓库命中时**必须**再调 `get_repo` 取 `httpUrlToRepo`（搜索/列表返回不保证带克隆地址）。
- **仓库只有一个**：按 `{Code}-Mixed` 查一次即可，不要按端查三遍、也不要因为三端同名而报重名错。
- 检查**仓库目录**（= `<工作区>\<仓库名>`，各端同值）是否**已存在且非空**（决定执行续跑时是否要走"三选一"，同一目录只问一次）。

### 1.4 准备阶段产出

写结论文件 `results/step3-prepare-<ts>.json`（**注意文件名带 `prepare`**）：
`"phase": "prepare"`，`"status": "ready"`（事实查清、可执行）或 `"blocked"`（条件不满足、不可执行，必须写明原因），
把事实写进 `artifacts[]`：

> **不要写 `status="done"`**：`done` 是执行轮（建组 / 建库 / 克隆完成）专用。准备阶段写 `done` 会被判定为「本步已完成」，
> 助手在执行轮就只剩一句 `DONE`、零写动作——这是真机翻车过的方式。

- `kind=group` **一条**：`name` / `externalId`（组 id）/ `webUrl`；`extra.exists` / `extra.parentId` / `extra.organizationId`
  （**必须与提示词注入的组织 ID 同值**；空值会被判为准备失败）。
- `kind=repository` **每端一条**（面板按端渲染模板 / pin 读数，并逐端核对模块目录）：
  `end` / `name`（**各端同值**，如 `Csxmy-Mixed`）/ `webUrl` / `cloneUrl` / `localPath`（**各端同值，均为仓根**）/
  `modulePath`（**逐端不同**，如 `Csxmy-Service`）/ `extra.templateRepository` / `extra.templateProjectId` /
  `extra.templatePath` / `extra.templateBranches` / `extra.resolvedBranchSource` / `extra.repositoryExists` /
  `extra.localPathOccupied` / `extra.visibility` / `extra.error`。
  > 仓库级的读数（模板仓 / pin / 是否已存在 / 可见性 / 本地目录）在三条里**写同样的值**，差异只有 `end` 与 `modulePath`。
- `checks[]` 逐项记录（每端一条，`name` 取端名）。
- ⚠️ **逐端完备性**：`kind=repository` 必须**每端一条**，**缺端即整步 failed**。

面板据此渲染确认界面：用户可改**目标仓库名（各端共用，一份输入）**与**可见性**；模块目录由插件按
[`org-reference.md`](./org-reference.md)「项目命名」表的格式派生，界面只读展示。

---

## 2. 执行续跑 · 建组建库并克隆

提示词会带：用户确认后的建库计划 + 准备阶段结论文件绝对路径 + 用户答复（`answers`）。
助手**只做增量**，不重跑准备阶段。

> **单仓一次闭环（硬口径）**：建组 → 建库 / 复用 → 补查克隆地址 → 等 pin → 克隆 → 替换占位符 → 按端核对模块目录，
> 这一串**只走一遍**，不是「逐端各走一遍」。逐端只做两件事：确认 `modulePath` 目录存在、写该端的 `artifacts[]` 条目。
> 仓根未就绪 / 占位符残余 ≠ 0 / `placeholderSources` 为空时**不得写执行结论收尾**：继续调用工具做完；
> 用 `status=failed` 的结论文件代替执行＝本步判失败。
> 只有 docs 失败对照表里不可恢复的失败（401/403/pin 120s 未就绪/凭据缺失）才能让本步失败。
> 起步时先看档案里仓库的「已克隆 / 未克隆」：**已克隆则跳过 2.1~2.6**，只补 2.7 的模块目录核对与 2.8 落账。

### 2.1 建代码组（先查后建）

代码组不存在 ⇒

```
mcp_yunxiao_codeup_create_group(name=<组名格式展开值>, path=<同 name>, parentId=<留空＝组织根>)
```

- `name` / `path` 均为 [`org-reference.md`](./org-reference.md)「项目命名」表里的代码组名格式展开值（例 `CSXMY`）；**勿含 `/`**。
- 已存在 ⇒ **复用**，`checks[]` 注明「复用」，不重复建。
- 可见性沿用 `internal`（组织内公开）。

### 2.2 建仓库（先查后建，单仓只建一个）

仓库不存在 ⇒

```
mcp_yunxiao_codeup_create_repo(
    path=<仓库名格式展开值>,             # 例 Csxmy-Mixed（单仓，各端同值）
    name=<同上>,
    namespaceId=<代码组 id>,
    visibility=<internal | private>,
    templateProject={ templateProjectId: <模板库命中项 id>, templateType: 1, syncAllBranches: true }
)
```

- **已存在同名仓库 ⇒ 复用**（幂等），`checks[]` 注明「复用」，**不要**覆盖创建。
- **只建一次**：不要按端重复创建（重名会被拒，且会留下不一致的默认分支 / 可见性）。
- 建库响应**不保证**带 `httpUrlToRepo`。

### 2.3 建库后必须再查一次克隆地址 ⚠️

新建仓库**必须再调一次**：

```
mcp_yunxiao_codeup_get_repo(repositoryId=<建库响应里的 id>)
```

取其 `httpUrlToRepo` 与 `defaultBranch`。

- `httpUrlToRepo` 为空 ⇒ **本步失败**（不猜地址、不拼地址）。
- 仅"**复用已存在仓库**"时才能直接用准备阶段拿到的地址。
- 记录：`externalId`（仓库 id）、`cloneUrl`、`defaultBranch`（空则退回推送分支 `develop`）。

### 2.4 轮询等 pin 分支就绪

`syncAllBranches` 是**服务端异步**（实测建库后 10–17s 才补齐模板分支）。建库后立刻 `git clone -b` **必然失败**，且会留下坏 `.git` 残骸。

```
循环：每 3 秒调一次 mcp_yunxiao_codeup_list_branches(repositoryId=<新仓库 id>)
      判断 pin 分支名是否出现（精确、区分大小写）
      上限 120 秒
```

- 超时 ⇒ **阻断**（`status="failed"`，message 写「模板分支未就绪（等满 120s）」）⇒ **不盲目 clone**。
- 复用已存在仓库时，若 pin 分支已在列表中，可直接进入克隆。

### 2.5 克隆（单仓只克隆一次）

```
git clone -b <pin> <httpUrlToRepo> "<仓库目录>"      # 仓库目录 = <工作区>\<仓库名>，已含仓库名，勿再拼一次
```

> **助手侧口径**：用框架 git 工具组，克隆＝`GitCloneAsync(url=<httpUrlToRepo>, branch=<pin>, directory=<仓库目录>)`；
> 克隆之后的所有 git 调用（status / branch / checkout / add / commit / push / log）**必须显式带
> `workingDirectory=<仓库目录>`**（= 仓根，不是模块目录）——不传时框架在项目工作区根执行，会报「不是 git 仓库」或操作到别的目录。

- 目标目录是 [`org-reference.md`](./org-reference.md)「项目命名」表里的**仓库名**格式展开值（`{Code}-Mixed`），与云端仓库名同源。
- **不做浅克隆**（保持模板默认行为）；**不内嵌凭据**（凭据由用户在外部环境配置，见 [`step6-push.md`](./step6-push.md)）。
- 克隆失败 ⇒ 记 git **原文摘要**（脱敏）后写 `status="failed"`；**不自行删目录**（清理由用户在面板三选一决定，见 2.6）。
- 若 git 留下 `.git/HEAD = ref: refs/heads/.invalid` 之类的残骸，属**已知现象**：如实报告，不要当作"已克隆"。
- 克隆成功后仓根下应有模板自带的模块目录（`TJCES-Service` / `TJCES-Admin` / `TJCES-Mobile`，稍后被占位符改名）；
  缺目录 ⇒ 这次克隆不完整，**按失败处理**，不要凭猜测补建目录。

### 2.6 目标目录非空 ⇒ 三选一（`needsInput`）

准备阶段已发现、或 clone 时报「目录非空」⇒ 写 `status="needsInput"`，`inputs[]` 带一条：

```jsonc
{
  "id": "clone-conflict",
  "kind": "choice",
  "ends": ["<端>"],
  "prompt": "目标目录非空，如何处理？",
  "options": [
    { "value": "reuse",   "label": "复用已克隆（健康校验通过才算就绪）" },
    { "value": "reclone", "label": "清空目录后重新克隆" },
    { "value": "abort",   "label": "中止本步" }
  ],
  "details": { "path": "<绝对路径>", "entryCount": <N> }
}
```

| 答复 | 处置 |
|------|------|
| `reuse` | 先做**健康校验**（工作树可用 + `.git/refs/heads/<pin>` 或 `packed-refs` 命中、`.git/HEAD` 指向有效 ref），通过才算就绪；不通过就回到三选一 |
| `reclone` | **由助手删除目录**再克隆。⚠️ Windows 下含 `.git` 的目录有**只读 pack 文件**，裸删会 `UnauthorizedAccessException` ⇒ 用 `RunPythonAsync` 跑 `shutil.rmtree(path, onerror=<清只读位后重试>)` |
| `abort` | 本步结束为 `failed`（文案取用户选择），不推进 |

- 聊天路径（无面板）：把三选一**直接列在对话回复里**，用户回一句即可，助手同会话继续。
- 用户既不选也不回复 ⇒ 停在 `needsInput`（可重试），**不克隆、不删目录**。

### 2.7 占位符替换与模块目录归位

对**已克隆的仓库**（单仓只有一份）执行"内容 + 文件/目录名"批量替换。规则表见 [`org-reference.md`](./org-reference.md) 的「占位符规则」表（`TJCES`→`{CODE}` / `Tjces`→`{Code}` / `tjces`→`{code}`，字面量、区分大小写、按 `From` 长度降序）。

执行姿势（助手侧脚本，`RunPythonAsync`）：

1. **跳过** `.git`、`.vs`、`.idea`、`.vscode`、`node_modules`、`bin`、`obj`、`packages`、`TestResults`、`dist`、`build`；只改文本扩展名白名单内的文件，外加 **`.env` 族**（文件名以 `.env` 开头：`.env`、`.env.development`、`.env.production`、`.env.test`…，**不要只按扩展名判**，`.env.development` 的扩展名是 `.development`）与无扩展名白名单（`Dockerfile`/`.gitignore`/`.gitattributes`/`.npmrc`/`.dockerignore`/`Makefile`/`LICENSE`/`README`）；单文件 > 4 MB 跳过；UTF-16 跳过；含 `NUL` 的二进制跳过；**UTF-8 BOM 原样保留**。
2. **替换前统计命中数**（按 `From` 分别统计）。
3. 替换顺序：**文件内容** → **文件改名** → **目录改名**（目录按路径深度倒序）。
4. **替换后复查「残余命中 = 0」**（三个 `From` 全部为 0；**复查范围与替换范围必须完全一致**——同一套跳过目录 + 同一套文本白名单，否则要么漏报、要么把有意跳过的文件误判成残余）。
5. 改名目标已存在 ⇒ 放弃该次改名并记录（不覆盖）。
6. **模块目录归位**：改名后仓根下应是 `{Code}-Service` / `{Code}-Admin`（+ 勾选了移动端时的 `{Code}-Mobile`）。
   - 逐个核对计划里每端的 `modulePath` 目录**存在且非空**；缺失 ⇒ 本步 `failed`（不要凭猜测新建空目录充数）。
   - **Step 1 未勾选移动端** ⇒ 占位符替换后**删除** `{Code}-Mobile` 模块目录（模板仓是单仓，不删就会把移动端代码带进新项目）。

结论文件 `checks[]` 必须含一条：

```jsonc
{ "name": "placeholder-residual", "ok": true, "detail": "残余命中 0（替换前命中 N 处，改写 M 个文件，重命名 K 项）", "command": "<脚本命令摘要>" }
```

> 插件侧还会**独立再扫一遍残余**（不采信自报）⇒ 残余不为 0 会被判失败。
> `extra.placeholderSources` **为空即判「占位符替换未完成」＝本步未完成**（不是「无法复核」），
> 不得写 `cloned=true`。

### 2.8 本地复核与落账

助手完成前，在**仓根**（`workingDirectory=<仓库目录>`）用 `git status`（工作树）与 `git rev-parse --verify refs/heads/<pin>` 复核，
再**逐端**核对 `<仓库目录>\<modulePath>` 存在，然后把结果写进 `artifacts[]`（每端一条 `kind=repository`）：

- `localPath` = **仓根**（各端同值）、`modulePath` = 该端模块目录（逐端不同）；
- `extra.cloned = true`（**前提**：占位符已替换且 `placeholderSources` 非空，否则不算就绪）；
- `extra.defaultBranch`；
- `extra.commitId`（若本次有本地提交/模板自带 commit 可读则填，读不到留空并如实说明）。

档案侧回填（由插件做）：`Repositories[]` 的 `ExternalId` / `WebUrl` / `CloneUrl` / `LocalPath` / `ModulePath` / `TemplateBranch` / `Cloned`，
以及远端产物清单（代码组 + **一条**仓库产物）。

**Step 3 执行续跑成功后，插件会自动作废 Step 5 编译结论与 Step 7 流水线结论**（仓库改名/重建后旧 YAML 写死的地址与分支会误导）；你只需如实落账，无需手动清。

---

## 3. 失败对照表

| 现象 | 处置 | 结论文件 |
|------|------|----------|
| 云效 401 / 令牌失效 | **不重试**，提示重配 PAT | `failed` + 原文 |
| 云效 403 | **不重试**（无权限，硬闯无用） | `failed` |
| 云效 429 / 限流 | 退避重试（线性 10/20/30s，最多 3 次），仍失败即失败 | 重试过程记 `checks[]` |
| 模板库为空 / 未命中模板仓名 | 阻断，列出模板库现有条目 | `failed` |
| 登记 pin 不存在 / 模板仓无可用分支 | 阻断，列出模板仓现有分支，**不猜默认分支** | `failed` |
| 建库后 pin 分支 120s 未出现 | 阻断，不盲目 clone | `failed` |
| 建库响应缺仓库 id | 阻断（检查代码组写权限 / 可见性 / 仓库名是否被占用） | `failed` |
| `get_repo` 未返回 `httpUrlToRepo` | 阻断本步（不猜地址） | `failed` |
| git 凭据缺失 / 认证失败 | 提示外部配置凭据（固定文案见 [`step6-push.md`](./step6-push.md)） | `failed` |
| 克隆残骸（`.git/HEAD = ref: refs/heads/.invalid`） | **不自行删**；写 `needsInput` 让用户选清理方案 | `needsInput` / `failed` |
| 目标目录非空 | 三选一（同一目录只问一次） | `needsInput` |
| 仓库名已存在 | **复用**（幂等） | `done` + `checks[]` 注明「复用」 |
| 占位符残余 ≠ 0 | 视为未完成 | `failed` |
| `placeholderSources` 为空 | 视为**占位符替换未完成**（不得 `cloned=true`） | 继续做，不写结论 |
| 仓根复核不通过（目录空 / 无 `.git` / refs 缺） | 视为未克隆 | `failed` |
| 模块目录缺失（`{Code}-Service` / `{Code}-Admin` / 勾选时的 `{Code}-Mobile`） | 视为克隆或替换未完成，**不新建空目录充数** | `failed` |

> 单仓下失败基本是整仓级：失败 ⇒ **整步 failed**，但**已落账的档案与远端产物照常保留**（重跑只补缺失）。
> **远端仓库一律不自动删除**（中止 / 清理时只留孤儿清单）。
