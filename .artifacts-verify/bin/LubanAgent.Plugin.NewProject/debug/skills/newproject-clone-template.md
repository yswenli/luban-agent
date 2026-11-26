# 按模板建库并克隆（newproject-clone-template）

> 「新建项目」插件原子技能 · 对应 **Step 3** · **助手驱动口径**：
> 「**一次只读准备下发 + 用户确认后同会话执行续跑**」。
> 组织专有值（模板仓名 / 代码组名格式 / pin 择优 / 占位符规则 …）**不在本正文复述**——
> 编排约定读工作区 `.luban-agent/plugin-newproject/docs/step3-repositories.md`，
> 值总表读 `docs/org-reference.md`。

## 何时使用

- 主 skill `newproject-create` 的第 3 步；
- 或单独补建 / 补克隆本项目的**单仓**（例如只补克隆、或只补模块目录核对）。

## 前置条件

- 已有项目档案（`ProjectSpec`），且第 2 步「初始化」已完成（工作区 / 会话就绪、`docs/` 快照已复制、
  云效凭据可取用）。
- **单仓形态**：一个项目只建**一个**仓库（模板仓名见 `docs/org-reference.md` 的「模板仓与仓内模块」表；
  目标仓 `{Code}-Mixed`）；三端是其仓内
  **模块子目录**（`{Code}-Service` / `{Code}-Admin` / `{Code}-Mobile`）。建库、克隆、推送各只做一次。
- 云效组织 ID 与 PAT 已在 codex「设置 → 云效连接」配好；PAT 由连接器注入 MCP，
  **不进工具入参、不进提示词、不落结论文件**。组织 ID 相反：插件会把它写进本轮提示词，
  调用 `mcp_yunxiao_*` 时**显式传同一个值**（不要传空串——空串会绕过客户端回落配置的兜底）。
- **git 凭据**由用户在**外部环境**自行配好（git credential helper / 系统凭据管理器 / 预置 remote）；
  助手不提供凭据通路，也不在 URL 里拼 `oauth2:<PAT>@`。
- 模板仓必须登记为**模板库**（`templateType = 1`）条目——普通仓库搜索**不保证**命中模板。
- 模板定位与 pin 择优、占位符令牌语义的**具体约定与取值**：读 `docs/step3-repositories.md` 与
  `docs/org-reference.md`。

## 术语：一次只读准备下发 + 同会话执行续跑

本步在宿主 API 上表现为「先下发一次**只读准备** → 用户确认 → 再下发一次**执行续跑**」。
这是**同一个步骤**的两个阶段（框架没有「轮内提问」工具，答复后由插件再发一次提示词），
**不是两个各自独立的步骤**；执行续跑只做增量，**不重跑准备阶段**。

## 阶段一 · 准备（只读，不建任何云效资源）

助手动作（全部只读）：

1. `mcp_yunxiao_codeup_list_template_repos`（`templateType=1`、`perPage=100`）**全量拉取模板库条目**，
   再**在客户端按模板仓名**匹配出 `templateProjectId`（**单仓只有一条**）
   （该接口**没有 `search` 入参**，不能指望服务端按名过滤；名称约定见 `docs/step3-repositories.md`）。
2. `mcp_yunxiao_codeup_list_branches`（`repositoryId = templateProjectId`）取模板仓分支，
   按 `docs/org-reference.md` 的 **pin 择优顺序**解析出 pin（**只解析一次**）；解析不出**即阻塞，不猜默认分支**。
3. `mcp_yunxiao_codeup_list_groups`（`search = 代码组名`）查目标代码组是否已存在（决定「新建 / 复用」）。
4. `mcp_yunxiao_codeup_search_repos`（`search = 仓库名`）查目标仓库是否已存在（**只查一次**，不按端重复查）；
   **若已存在，再调 `mcp_yunxiao_codeup_get_repo`（`repositoryId = 命中项 id`）取 `httpUrlToRepo`**。
5. 检查**仓库目录**（`<工作区>\<仓库名>`，**已含仓库名**，各端同值）是否已存在且非空。

产出结论文件 `results/step3-*.json`：把上面的事实写进 **`artifacts[]`**
（`kind=group` 一条 + `kind=repository` 每端一条），`checks[]` 逐项记录。面板据此渲染**确认界面**，
用户可改**目标仓库名（各端共用一份输入）/ 可见性**；模块目录按格式派生、界面只读。

`artifacts[]` 要点（**判别属性是 `kind`，`extra` 是具名强类型**）：

- `kind=group`：`name` / `externalId`（组 id）/ `webUrl`；`extra` 为 `GroupArtifactExtra`
  （`exists` / `parentId` / `organizationId`）。
- `kind=repository`：`end` / `name`（**各端同值**）/ `webUrl` / `cloneUrl` / `localPath`（**各端同值，均为仓根**）/
  `modulePath`（**逐端不同**）/ `templateBranch`；
  `extra` 为 `RepositoryArtifactExtra`（`templateRepository` / `templateProjectId` / `templatePath` /
  `templateBranches` / `resolvedBranchSource` / `repositoryExists` / `localPathOccupied` / `visibility` /
  `error`）。
- **逐端完备性**：`kind=repository` **每端一条**（`Service` / `Admin` / `Mobile`），缺端即整步 `failed`；
  三条里除 `end` / `modulePath` 外**写同样的值**（同仓、同模板、同 pin）。

> 准备阶段**不得**出现任何「新建」类产物——结论文件里若出现新建的组 / 仓库即为不通过。

## 阶段二 · 执行续跑（用户确认后，同一会话）

提示词带上用户确认后的建库计划 + 准备阶段结论文件**绝对路径** + 用户答复（`answers`）。助手动作：

1. 代码组不存在 → `mcp_yunxiao_codeup_create_group`（**先查后建由助手守**：确认无同名组才建）。
2. 仓库不存在 → `mcp_yunxiao_codeup_create_repo`（`templateProjectId` / `templateType=1` /
   `syncAllBranches=true` / `namespaceId` / `path`；**单仓只建一次**，不要按端重复建）。
3. **新建仓库后必须再调一次 `mcp_yunxiao_codeup_get_repo`（`repositoryId` = 建库响应里的 id）取
   `httpUrlToRepo`**——建库响应不保证带该字段；仅「复用已存在仓库」时才能直接用准备阶段拿到的地址。
4. 轮询 `mcp_yunxiao_codeup_list_branches` 等 pin 分支就绪（上限 120s / 间隔 3s，**超时即 `failed`**，
   不盲目 clone）。
5. `GitCloneAsync(url=<httpUrlToRepo>, branch=<pin>, directory=<仓库目录>)`（框架 git 工具组；
   仓库目录 = `<工作区>\<仓库名>`，**已含仓库名，勿再拼一次**，**单仓只克隆一次**；克隆地址取自
   `httpUrlToRepo`，**不带凭据**）。
6. 克隆失败 → 记录 **git 原文摘要**（**不内嵌凭据**），**不自行删目录**，写 `status="failed"`
   （残余目录清理见下「冲突三选一」）。
7. 目标目录非空（准备阶段已发现或 clone 时报错）→ 写 `status="needsInput"`，`inputs[]` 带**克隆冲突
   三选一**（见下；同一仓库目录只问一次）。
8. **占位符替换**：用 `RunPythonAsync` 现场脚本完成「内容 + 文件 / 目录名」批量替换
   （跳过 `.git` / `bin` / `obj` / `node_modules`；按 `From` 长度降序；区分大小写；**幂等**）；
   替换前统计命中、替换后复查「**残余命中 = 0**」，`checks[]` 必须含 `placeholder-residual` 且
   `ok=true`。规则与令牌语义读 `docs/org-reference.md` 与 `docs/step3-repositories.md`；
   把**实际命中的来源令牌**逐字写进 `extra.placeholderSources`（**至少一项**，插件按它独立扫残余）。
   **随后归位模块目录**：逐个核对每端 `modulePath` 目录存在且非空；Step 1 未勾选移动端 ⇒ **删除**
   `{Code}-Mobile` 模块目录（模板仓是单仓，不删会把移动端代码带进新项目）。
9. **复核**（`GitStatusAsync` / `GitBranchListAsync` 都要带 `workingDirectory=<仓库根>`，不传会在项目
   工作区根执行）确认工作树与 `refs/heads/<pin>` 可用、并逐端核对模块目录存在后，把 `cloned=true`、各端
   `modulePath` 与产物账目写进 `artifacts[kind=repository].extra`（`cloned` / `defaultBranch` / `commitId`；
   `localPath` 写仓根）。

执行续跑结论文件同样落 `results/step3-*.json`：`status = "done"` / `"needsInput"` / `"failed"`，
`checks[]` **逐端一条**（`name` 取端标识 `Service` / `Admin` / `Mobile`，失败时 `detail` 给原文摘要），
`artifacts[kind=repository]` 逐端如实记录 `modulePath` 与 `extra`（`cloned` / `commitId`）；**失败 → 整步 `failed`**，
但已落账的档案与产物**照常保留**，便于重跑只补缺失。

> **单仓一次闭环（硬口径）**：上面 1→9 **只走一遍**（不是按端各走一遍：建库 / 克隆 / 推送在同一仓库上重复做
> 只会撞重名与限流）。逐端只做两件事：核对 `modulePath` 目录、写该端 `artifacts[]`。占位符残余 ≠ 0 或
> `extra.placeholderSources` 为空时**不得写结论文件收尾**，必须继续调用工具做完；
> 用 `status=failed` 的结论文件代替执行＝本步判失败。档案标「已克隆」时跳过 1~7，只补 8~9。

## 交互项 `inputs[]`（克隆冲突三选一）

目标目录非空时写 `status="needsInput"`，`inputs[]`：

```jsonc
{
  "id": "clone-conflict",
  "kind": "choice",
  "prompt": "目标目录非空，如何处理？",
  "ends": ["Service", "Admin"],
  "options": [
    { "value": "reuse",   "label": "复用已克隆（健康校验通过才算就绪）" },
    { "value": "reclone", "label": "清空目录后重新克隆" },
    { "value": "abort",   "label": "中止本步" }
  ],
  "details": { "path": "<仓库目录>", "entryCount": 42 }
}
```

- `reuse` → **先做健康校验**（工作树 + `.git/refs/heads/<pin>`）通过才算就绪；校验不通过回到三选一，
  不把坏目录盖章成「已克隆」。
- `reclone` → **由助手删除目录**：用 `RunPythonAsync` 现场脚本
  `shutil.rmtree(path, onerror=清只读后重试)`——克隆出来的仓库里 git 的 pack 文件是**只读**的，
  框架 `DeleteDirectoryAsync` 是裸 `Directory.Delete`、**不处理 Windows 只读位**，直接删大概率失败；
  删干净后再进入执行续跑。
- `abort` → 本步结束为 `failed`（文案取用户选择），不推进；远端仓库保留（登记为孤儿产物）。
- **用户不回复** → 停在本步 `needsInput`（可重试），**不发创建 / 克隆请求**。
- 聊天路径（无面板）：三选一**直接在对话里回复**，助手在同一会话继续。

## 结果（落账）

- 档案字段：`Repositories[].ExternalId` / `WebUrl` / `CloneUrl` / `LocalPath`（**仓根，各端同值**）/
  `ModulePath`（**逐端模块目录**）/ `TemplateBranch`（pin 解析结论）/ `Cloned`（语义：**本地确已就绪**）。
- 远端产物清单追加：**代码组**（`kind=group`，`end = null`）、**本项目单仓仓库**（`kind=repository`，**只一条**）。
- 每条产物**即时持久化**：中途失败时已成功的账目不会丢，重跑只补缺失。
- 插件回读 `artifacts[]` 后做**本地复核**（仓根目录存在且非空 / `<localPath>/.git` 有效 /
  `refs/heads/<pin>` 存在 / 逐端模块目录存在 / 占位符残余 = 0，逐项独立再扫一遍）；通过后作废最近一份
  Step 5 编译结论与 Step 7 流水线结论（仓库改名 / 重建后旧结论是误导）。

## 失败处理

| 现象 | 处理 |
|------|------|
| 云效 401 / 令牌失效 | **不重试**，引导用户到 codex 云效配置重录 PAT |
| 云效 403 非组织成员 | **不重试**（无权限，硬闯无用），`failed` |
| 云效 429 / 限流 | 按 `docs/step3-repositories.md` 的退避约定重试，仍失败即 `failed` |
| pin 登记值不存在 / 模板仓无可用分支 | **阻断**，不猜默认分支，`failed` |
| 建库后 pin 分支 120s 未出现 | **阻断**，不盲目 clone，`failed` |
| git 凭据缺失 / 认证失败 | 提示「请先在外部环境配好凭据」+ git 原文摘要，`failed` |
| 克隆失败残骸（`.git/HEAD = ref: refs/heads/.invalid`） | **不自行删**；写 `needsInput` 或 `failed`，由用户选清理 |
| 目标目录非空 | 三选一（`reuse` / `reclone` / `abort`）；选 `reclone` 后**由助手删除**（注意只读位） |
| 复用目录健康校验不通过 | 视为**未就绪**，回到「三选一」让用户决定，不当作已克隆 |
| 仓库名已存在 | **复用**（幂等），`checks[]` 注明「复用」 |
| 模板仓定位不到 | 阻断：确认模板仓在组织内可见且已登记为模板库条目 |
| 占位符残余 ≠ 0 | 本步 `failed`（本地复核也会独立再扫一遍） |

**幂等总原则**：所有「建」动作**先查后建**；所有产物即时写进结论文件；不做静默覆盖。
中止 / 清理时**远端仓库不自动删除**，只保留孤儿清单。