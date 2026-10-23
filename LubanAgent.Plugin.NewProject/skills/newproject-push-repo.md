# 推送改动到自有仓库（newproject-push-repo）

> 「新建项目」插件原子技能 · 对应 **Step 6** · **助手驱动口径**：本步**一次下发**即可完成（无需「准备 / 续跑」两阶段）。
> 组织专有值（目标分支名、提交身份兜底值、提交信息格式、退避间隔 …）**不在本正文复述**——
> 编排约定读工作区 `.luban-agent/plugin-newproject/docs/step6-push.md`，值总表读 `docs/org-reference.md`。

## 何时使用

- 主 skill `newproject-create` 的第 6 步；
- 或**重新推送 / 补推某端**（例如 Mobile 上次漏推）。

## 前置条件

- Step 3 已完成（各端仓库已创建并克隆到本地，且档案里对应端的 `cloned = true`）。
- Step 5 编译检查已通过。界面用 `StepGate.CanPush` 拦，**判据已改读 Step 5 结论文件**：
  取最近一份 `results/step5-*.json`（`mtime` 最新且晚于本步最近一次起始时刻），
  **逐端 `BuildResults` 全 `Ok` 且覆盖 `spec.Ends` 全集**才放行；`ProjectSpec.StepStates`
  已降级为「仅界面进度显示」，**不再作闸门依据**。
- 用户明确要求跳过编译直接推送时，按用户指令例外处理。
- **git 凭据**由用户在**外部环境**自行配好（git credential helper / 系统凭据管理器 / 预置 remote）；
  助手不提供凭据通路，也**不在 URL 里拼 `oauth2:<PAT>@`**。

## 执行方式

本步全部动作走**框架 git 工具组**（`Git*Async`），**不调** `mcp_yunxiao_codeup_*`：

- git 工具在**工作区内**由框架直接放行、不经插件策略（不需要动白名单）；
- 云效侧本次**没有**推送类工具，推送一律经 git 工具组；
- **每个调用都要带 `workingDirectory=<该端仓库目录>`**——不传时框架在项目工作区根执行，会报「不是 git 仓库」。

> **纪律（机制拦不住，只能靠约定）**：不得使用 `--force`、`reset --hard`、`clean -fd`。
> 工作区内的破坏性 git 动作同样免确认、免回调（框架 `GitResetAsync` / `GitCleanAsync` 与 shell 里的
> `git` 都拦不住），故只能用约定约束；操作记录把工具调用**原样**呈现给用户复核。

对所选**每个端**依次执行：

1. **看工作树**：`GitStatusAsync(workingDirectory=<仓库目录>)`。**无改动就跳过提交**（如实报告，不产生空提交），仍继续推送。
2. **建 / 切目标分支**：目标分支名按 `docs/step6-push.md` 解析（配置 `pushBranch` 有值用配置值，
   留空退回该端仓库的**默认分支**，仓库为空则以 `docs/org-reference.md` 登记的默认分支创建并推送）；
   分支已存在用 `GitCheckoutAsync(target=<branch>, workingDirectory=<仓库目录>)` 切过去，
   否则 `GitCheckoutAsync(target=<branch>, createBranch=true, workingDirectory=<仓库目录>)` 新建。
3. **提交身份兜底**：先读本机 `git config user.name` / `user.email`；缺失时按 `docs/org-reference.md`
   的兜底值在**本仓库级**设置（不改全局配置）。
4. **提交**：`GitAddAsync(paths=["."], workingDirectory=<仓库目录>)`（**不能传 `-A`**：工具会拼成
   `git add -- -A` 当 pathspec 而失败）→ `GitCommitAsync(message=<消息>, workingDirectory=<仓库目录>)`；
   提交信息格式按 `docs/step6-push.md`（中文，风格与 luban-agent 既有约定一致）。
5. **推送**：`GitPushAsync(remote="origin", branch=<branch>, setUpstream=true, workingDirectory=<仓库目录>)`；
   **凭据由用户外部配置**，URL 里不带凭据；**不强制推送**。
6. **回写结论文件**：`results/step6-*.json`，`status = "done"`；`artifacts[]` 里 `kind=repository`
   **每端一条**，`extra` 记该端推送目标分支与提交 id（`defaultBranch` / `commitId`）；
   `checks[]` **逐端一条**（`name` 取端标识 `Service` / `Admin` / `Mobile`，失败时 `detail` 给原文摘要）。
   **任一端失败 → 整步 `failed`**，但已成功落账的条目**照常写进档案**，便于重跑只补缺失。

## 结论文件契约

`{workspace}\.luban-agent\plugin-newproject\results\step6-<ts>.json`：`status` 为 **`done` / `needsInput` / `failed`**
（Step 6 是**阻断性**步骤，`status == "done"` 且 `ok == true` 才算通过）。
`artifacts[]` 的判别属性是 `kind`（本步一律 `kind=repository`），`extra` 为具名强类型
`RepositoryArtifactExtra`（字段集合固定，助手只填值、不能变形）；**不得**用
`Dictionary<string, object?>` 之类的自由字典。

## 结果

- 各端专属仓库的目标分支上出现本项目初始提交，本地分支与 `origin/<branch>` 一致。
- 操作记录区可看到每端的「看工作树 → 建 / 切分支 → 提交改动 → 推送到自有仓库」动作与 commit 摘要。
- 插件回读 `artifacts[]` 后做**本地复核**：`<localPath>/.git/refs/remotes/origin/<pushBranch>` 存在，
  且 commit id 与自报一致（远端不可独立核对的部分如实记为残余风险）。

## 失败处理

| 现象 | 处理 |
|------|------|
| **云效服务端限流**（`请求过于频繁，请稍后重试!` / HTTP 429） | **退避重试**：按 `docs/step6-push.md` 的退避约定——**线性 10s / 20s / 30s、最多 3 次**（**不是指数间隔**），重试过程记入 `checks[]`；三次用尽仍失败才中断，已推送的端保留结果。**限流不是权限问题**——不要据此判 PAT / 分支保护 |
| 非快进（远端已有提交） | **不自动 force、不自动重试**；停下让用户确认后再决定 |
| 401 / 令牌失效 | **不重试**，引导用户到 codex 云效配置重录 PAT |
| 403 非组织成员 / 无写权限 | **不重试**（硬闯无用） |
| **认证失败 / 凭据缺失**（`Authentication failed` / `could not read Username` …） | `status="failed"`，`message` 带 git 原文摘要 + 固定提示「**请先在外部环境配好 codeup HTTPS 凭据（git credential helper / 系统凭据管理器）**」 |
| 网络抖动（非限流报错） | **不自动重试**；文案给「若持续失败，请检查 PAT 是否具备该仓库写权限、目标分支是否被保护」，由用户判断是否重推 |
| 仓库目录不是 git 工作区 | 阻断并提示重跑 Step 3 完成克隆（此时档案 `cloned` 不应为真） |
| 切分支失败 | 阻断：回显 git 报错摘要，先人工确认本地工作区状态 |
| 无改动可提交 | 跳过提交、**继续推送**（幂等，远端可能已有该提交） |
| 权限被门禁拦截 | 说明插件 `plugin.json` 缺少对应 git 能力组，需补齐后重启 codex |

**幂等总原则**：先看工作树再提交（无改动跳过）；产物即时写进结论文件（失败重跑只补缺失）；
**不做静默覆盖、不强制推送**。中止 / 清理时**远端仓库不自动删除**，只保留孤儿清单。