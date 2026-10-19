# 按模板建库并克隆（newproject-clone-template）

> 「新建项目」插件原子技能 · 对应 **Step 3** · **M4-P3**（能力层已实现：`INewProjectService.CreateRepositoriesAsync`；
> 向导与聊天入口共用同一实现，云效逻辑零分叉）

## 何时使用

- 主 skill `newproject-create` 的第 3 步；
- 或单独补建 / 补克隆**某一个端**（例如只补 `-Mobile`）。

## 前置条件

- 已有项目档案（`ProjectSpec`），且第 2 步「初始化」已完成（工作区与会话就绪、云效凭据可取用）。
- 三个模板仓 `TJCES-Service` / `TJCES-Admin` / `TJCES-Mobile` 在组织内可见，且已登记在插件配置 `templates[]`
  （能拿到模板仓 ID 时用 `templates[].projectId` 精确指定最稳）。
- **模板 pin 语义（PRD §6 #5）**：`templates[].branch` 是**可选覆盖**——
  登记了值就**必须真实存在**，否则预检直接阻断；**留空＝运行期解析**，在模板仓现有分支中按
  `develop → master → main → 模板默认分支` 取第一个存在的，并把「取到哪个、依据是什么」写入档案
  `repositories[].templateBranch`、回显在预检里。**不要**把某个分支名当成普适默认值硬猜。
- 引导用户到 codex「设置 → 云效连接」填好组织 ID 与 PAT（中心版 `organizationId` 必填）。

## 执行（首选：能力层一次到底）

调用 `INewProjectService.CreateRepositoriesAsync`——与向导**同一实现**，内部顺序固定，逐端执行：

1. **只读预检**：把即将使用的参数摊开核对——目标代码组（「已存在将复用（含 ID）」/「不存在将新建」）、
   逐端模板库命中项（`templateProjectId` / 全路径）、模板仓现有分支与 pin 解析结论、仓库名、落地目录。
   **预检不创建任何云效资源**；不通过就停，不要带着错误参数往下走。
2. **建项目代码组（先查后建）**：按 `groupNameFormat`（默认 `{CODE}`，如 `csxmy` → `CSXMY`）在组织内
   **先查后建**——同名即复用（不重复建、不报重名错）；父级取 `groupParentId`（**留空＝组织根**），
   可见性沿用 `defaultVisibility`（默认 `internal`＝组织内公开）。
3. **建库**：`templateProject{ templateProjectId, templateType = 1, syncAllBranches = true }`、
   `namespaceId` ＝代码组 ID、`path` ＝ `repositoryNameFormat` 展开值（默认 `{code}-{end}`，如 `csxmy-Service`）。
   **同名仓库已存在即复用**（重跑幂等，不重复建库）。
4. **补查 HTTPS 地址**：创建接口**不返回** `httpUrlToRepo`，必须再查一次单仓详情。
5. **等 pin 分支就绪再克隆**：`syncAllBranches` 是**服务端异步**（实测建库后 10–17s 才补齐模板分支），
   须轮询分支列表等 pin 分支出现（上限 120s / 间隔 3s）；**超时即失败**，不要盲目 clone。
6. **克隆**：`git clone -b <pin> <HTTPS 地址>` 到 `<本地目录>/<仓库名>`。
   **克隆失败必须清理本次新建的目录**——失败会留下 `.git/HEAD = ref: refs/heads/.invalid` 的空工作树残骸，
   不清理的话重跑会被误判为「已克隆」而假成功；失败文案要带 git 原始报错摘要（URL 内嵌凭据已脱敏）。
7. **复用前健康校验**：目标目录已存在且非空时，给用户「**复用已克隆 / 清空后重克隆 / 中止**」三选一，
   不静默覆盖；选「复用」**必须校验**工作树与 `.git/refs/heads/<pin>` 均可用才算通过。
   档案 `cloned` **只在「真克隆成功」或「经健康校验的复用」时置真**。
8. **占位符替换**：内容 + 文件/目录名（PRD §4.3 Step 3）；规则取自插件配置 `placeholderRules`，
   默认 `TJCES→{CODE}` / `Tjces→{Code}` / `tjces→{code}`，区分大小写、按 `From` 长度降序替换。

## 聊天路径的 MCP 直编（仅用于补做单点动作）

- **查分支 / 解析 pin**：`mcp_yunxiao_codeup_list_branches`（`repositoryId` 必填，返回裸数组，总数在响应头 `x-total`）。
- **定位模板仓**：优先用配置里已登记的 `templates[].projectId`；`mcp_yunxiao_codeup_search_repos` 只搜**普通仓库**，
  **不保证返回模板库条目**（模板库查询暂无对应 MCP 工具，需要时经能力层）。
- **建代码组**：`mcp_yunxiao_codeup_create_group`（`name` / `path` 必填，`parentId` 留空＝组织根）——
  该工具**直接创建、不做重名查重**，只有确认无同名组时才用；需要「先查后建」一律走能力层。
- **建库 / 克隆 / 占位符**：同上面第 3~8 步。**建库与克隆建议仍走能力层**（含等分支就绪、残骸清理、
  健康校验、git 报错摘要），MCP 单点工具不附带这些保护。
- 拿不到用户对「三选一」的回答时，**中止本步**并给出引导文案。

## 结果

- 档案字段：`Repositories[].ExternalId` / `WebUrl` / `CloneUrl` / `LocalPath` / `TemplateBranch`（pin 解析结论）/
  `Cloned`（语义：**本地确已就绪**）。
- 远端产物清单追加：**代码组**（`RemoteArtifactKind.Group`）、**各端仓库**（`RemoteArtifactKind.Repository`），带云效控制台链接。
- 每条产物**即时持久化**：中途失败时已成功端的账目不会丢，重跑只补缺失。

## 失败处理

| 现象 | 处理 |
|------|------|
| 401 / 令牌失效 | 引导用户到 codex 云效配置重录 PAT |
| 403 非组织成员 | 提示无组织 / 资源权限，不要重试硬闯 |
| 429 | 退避重试（次数有限），仍失败则中断并提示 |
| pin 登记值不存在 | 阻断并提示 pin 值失效（**不自动改用默认分支**、不猜测） |
| 模板仓无可用分支 | 阻断：说明已按 `develop → master → main → 模板默认分支` 全试过，请确认模板仓内容 |
| 新建仓库后 pin 分支迟迟不出现 | 等满 120s 即失败并提示「模板分支未就绪」，**不盲目 clone** |
| 克隆失败 | 清理本次新建目录（不留 `.git/HEAD = ref: refs/heads/.invalid` 残骸）+ 回显 git 报错摘要 |
| 复用目录健康校验不通过 | 视为**未就绪**，回到「三选一」让用户决定，不当作已克隆 |
| 模板仓定位不到 | 阻断：优先改登记 `templates[].projectId`，再确认模板仓在组织内可见 |
| 仓库名已存在 | **复用**（幂等）；各端仓库名互不相同由建库计划校验保证 |
| 目标目录非空 | 三选一（复用已克隆 / 清空后重克隆 / 中止本步） |

中止 / 清理时**远端仓库不自动删除**，只保留孤儿清单（PRD §4.7）。