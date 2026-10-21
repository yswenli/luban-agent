# 推送改动到自有仓库（newproject-push-repo）

> 「新建项目」插件原子技能 · 对应 **Step 6** · **M4-P3**（能力层已实现：`INewProjectService.PushAsync`；
> 向导与聊天入口共用同一实现，云效逻辑零分叉）

## 何时使用

- 主 skill `newproject-create` 的第 6 步；
- 或**重新推送 / 补推某端**（例如 Mobile 上次漏推）。

## 前置条件

- Step 3 已完成（各端仓库已创建并克隆到本地，且档案里对应端的 `cloned = true`）。
- Step 5 编译检查已通过（或用户明确要求跳过编译直接推送）。
- 云效 PAT 可取用（鉴权形如 `oauth2:<PAT>@`）。

## 目标分支口径（PRD §6 #17）

| 情形 | 目标分支 |
|------|----------|
| 插件配置 `pushBranch` 有值（**v0.28 起默认 `develop`**） | 用它 |
| `pushBranch` 留空 | 退回该端仓库的**默认分支** |
| 仓库仍为空（无任何分支） | 以 `main` 为默认分支创建并推送 |

> **分支策略（v0.28 口径）**：Step 3 克隆的 pin 按 `develop → master → main` 择优（`NewProjectService.PreferredPins`），
> 本组织各端仓库实际都落在 `develop`；故 Step 6 推送分支同步为 `develop`，避免「在 develop 编译、推 main」。
> 该值同时用作云效建仓时的默认分支（`DefaultBranch`），Step 3 与 Step 6 两处口径一致。

## 执行（云效相关步骤：委托 MCP 工具，PRD §4.10.7）

对所选**每个端**依次执行：

1. **切到目标分支**：按上表解析出 `branch`——`git rev-parse --verify --quiet refs/heads/<branch>` 命中则
   `git checkout <branch>`，否则自模板 pin 分支 `git checkout -b <branch>`（档案 `templateBranch` 即 pin）。
2. `git add -A`，再用 `git diff --cached --quiet` 判空；**无改动就跳过提交**（不产生空提交），继续推送。
3. 有改动才提交，提交信息用中文、风格与 luban-agent 既有约定一致；
   提交身份**优先读本机 `git config user.name/user.email`**，缺失时用插件配置 `commitAuthorName/Email` 兜底。
4. `mcp_yunxiao_codeup_push`：把本地改动推送到该端专属仓库的**目标分支**（**不强制推送**）。
5. 回写档案：远端产物清单追加 `RemoteArtifactKind.Branch`（带端名 + 分支名 + 链接）。

> **本地 git 动作（1~3）不走 MCP**：由能力层经宿主的进程执行器拉起 `git`，且**收敛到唯一出口**
> （`RunGitAsync`）统一过门禁——按 git **子命令名**判定权限组（`git:commit` 覆盖 `add`/`commit`、
> `git:inspect` 覆盖 `rev-parse`/`status`/`diff`/`branch`/`config`/`checkout`）、`RequireProcess("git")`
> 校验进程白名单；判定前会跳过 `-c` / `-C` / `--git-dir` 等**前置全局选项**，防止换个写法绕过门禁（PRD §3.7、§6 #25）。

## 结果

- 各端专属仓库的目标分支（默认 `main`）上出现本项目初始提交，本地 `branch` 与 `origin/<branch>` 一致。
- 操作记录区可看到每端的「整理分支 → 提交改动 → 推送到自有仓库」动作与 commit 摘要。

## 失败处理

| 现象 | 处理 |
|------|------|
| 非快进（远端已有提交） | **不自动 force**；停下让用户确认后再决定 |
| 401 / 令牌失效 | 引导重录 PAT |
| 网络 / 429 | 退避重试，仍失败则中断，已推送的端保留结果 |
| 认证失败但仓库存在 | 提示检查仓库可见性与 PAT 所属用户权限，以及分支是否被保护 |
| 本地目录不是 git 工作区 | 阻断并提示重跑 Step 3 完成克隆（此时档案 `cloned` 不应为真） |
| 切分支失败 | 阻断：回显 git 报错摘要，先人工确认本地工作区状态 |
| 无改动可提交 | 跳过提交、**继续推送**（幂等，远端可能已有该提交） |
| 权限被门禁拦截 | 说明插件 `plugin.json` 缺少对应 git 能力组，需补齐后重启 codex |