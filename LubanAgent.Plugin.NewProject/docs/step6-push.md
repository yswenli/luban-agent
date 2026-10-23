# Step 6 知识正文：推送改动到自有仓库（step6-push）

> 配套技能：`skills/newproject-push-repo.md`。组织专有值以 [`org-reference.md`](./org-reference.md) 为准。
> Step 6 是**一次下发**（无往返），逐端串行执行。**闸门**：Step 5 编译未过不能推送（判定源是最近一份 Step 5 结论文件，逐端全 `Ok` 且覆盖全部端才放行）。

## 0. 前置

- Step 3 已完成：各端仓库已创建并克隆到本地，档案 `cloned = true`。
- Step 5 编译检查已通过（结论文件逐端 `Ok`）。
- **git 凭据由用户在外部环境配置**：git credential helper / 系统凭据管理器 / 已配置的 remote。插件与技能**不提供凭据通路**，助手**不得**在 URL 里拼 `oauth2:<PAT>@` 之类的凭据。

---

## 1. 逐端执行顺序

对每个端**依次**执行 1.1 → 1.5（不要并行推送，避免触发云效限流）。

> **助手侧口径（框架 git 工具组）**：本节命令是"口径"，助手实际调
> `GitStatusAsync` / `GitCheckoutAsync` / `GitAddAsync` / `GitCommitAsync` / `GitPushAsync` / `GitLogAsync`，
> 且**每个调用都要带 `workingDirectory=<该端仓库目录>`**（不传时框架在项目工作区根执行，会报「不是 git 仓库」）。
> 映射：`git add -A` ⇒ `GitAddAsync(paths=["."])`（**不能传 `-A`**，工具会拼成 `git add -- -A` 当 pathspec 而失败）；
> `git checkout -b <branch>` ⇒ `GitCheckoutAsync(target=<branch>, createBranch=true)`；`git push -u origin <branch>` ⇒
> `GitPushAsync(remote="origin", branch=<branch>, setUpstream=true)`。

### 1.1 看工作树

```
git status --porcelain
```

- 有改动 ⇒ 继续；
- **无改动** ⇒ 跳过提交（**不产生空提交**），但仍可推送（幂等，远端可能已有该提交）；在 `checks[].detail` 里**如实写明**「无改动，跳过提交」。

### 1.2 建 / 切分支

目标分支口径：

| 情形 | 目标分支 |
|------|----------|
| 插件配置 `pushBranch` 有值（**本组织为 `develop`**） | 用它 |
| `pushBranch` 留空 | 退回该端仓库的**默认分支** |
| 仓库仍为空（无任何分支） | 以 `main` 为默认分支创建并推送 |

操作：

```
git rev-parse --verify --quiet refs/heads/<branch>   # 命中 ⇒ 已有该分支
git checkout <branch>                                # 命中则直接切过去
git checkout -b <branch>                             # 未命中则自当前 pin（= 档案 templateBranch）新建
```

> 分支口径与 Step 3 的 pin 择优一致（各端仓库实际都落在 `develop`）⇒ 不会出现「在 develop 编译、推 main」。

### 1.3 提交身份（兜底口径）

```
git config --get user.name
git config --get user.email
```

| 情形 | 取值 |
|------|------|
| 本机 `git config user.name` 有值 | 用它 |
| 缺失 | 兜底 `LuBan Agent` |
| 本机 `git config user.email` 有值 | 用它 |
| 缺失 | 兜底 `luban-agent@local` |

兜底时**在本仓库级**设置（`git config --local` 或直接用 `-c` 随命令传入），**不要**改用户的全局配置。

### 1.4 暂存 + 提交

```
git add -A
git diff --cached --quiet     # 退出码 0 ⇒ 无改动，跳过提交
git -c user.name="<name>" -c user.email="<email>" commit -m "<提交信息>"
```

提交信息格式（中文，与 luban-agent 既有约定一致）：

```
chore: 初始化 {项目名}（{端标签}）——由「新建项目」向导按模板 {模板仓名} 生成
```

- `{端标签}` 取中文：`后端` / `管理端` / `移动端`（**已自带"端"字，不要再拼一个"端"**）。
- 示例：`chore: 初始化 测试项目一（管理端）——由「新建项目」向导按模板 TJCES-Admin 生成`

### 1.5 推送

```
git push -u origin <branch>      # 不强制推送
```

- **不 force、不 `--force-with-lease`**；非快进（远端已有提交）⇒ 停下让用户确认，**不自动重试**。
- 成功后记 `artifacts[]`：各端分支名 + 提交 id（`commitId`）。

---

## 2. 凭据失败处置（固定文案）

出现下列 git 原文报错时：

```
fatal: Authentication failed for 'https://codeup.aliyun.com/...'
fatal: could not read Username for 'https://codeup.aliyun.com': terminal prompts disabled
remote: Invalid username or password.
```

⇒ 结论文件 `status="failed"`，`message` = **git 原文摘要（脱敏，不含凭据）** + 下面这句**固定提示**：

```
请先在外部环境配好 codeup HTTPS 凭据（git credential helper / 系统凭据管理器），再重跑本步
```

**不要**把失败归因成"PAT 权限不足"就完事；也**不要**自行去要 PAT、更不要把它写进 remote URL。

---

## 3. 报错 → 处置对照表

| git / 云效现象 | 处置 | 是否自动重试 | 结论文件 |
|----------------|------|--------------|----------|
| 云效限流：`remote: 请求过于频繁，请稍后重试!` / HTTP 429 | **退避重试**（见第 4 节） | ✅ 线性 10/20/30s，最多 3 次 | 重试过程记 `checks[]`，用尽仍失败则 `failed` |
| `Authentication failed` / `could not read Username` / `Invalid username or password` | 第 2 节固定提示 | ❌ | `failed` + 原文摘要 + 固定提示 |
| 401 / 令牌失效 | 引导到 codex「设置 → 云效连接」重配 PAT | ❌ | `failed` |
| 403 / 权限不足 | 检查仓库可见性、PAT 所属用户权限、目标分支是否被保护 | ❌ | `failed` |
| 非快进（`[rejected] ... (non-fast-forward)` / `fetch first`） | **不 force**；停下让用户确认后再决定 | ❌ | `failed`（带原文） |
| `failed to push some refs`（原因不明，非限流） | 文案给「若持续失败再检查 PAT 写权限 / 分支保护」 | ❌ | `failed` |
| 仓库目录不是 git 工作区（无 `.git`） | 阻断，提示**重跑 Step 3** 完成克隆 | ❌ | `failed` |
| 切分支失败 / 工作树脏得异常 | 阻断，回显 git 报错摘要，先人工确认本地工作区状态 | ❌ | `failed` |
| 无改动可提交 | 跳过提交、**继续推送** | — | `done` + `checks[]` 注明「无改动」 |
| 推送目标分支已存在 | 直接切过去（`git checkout <branch>`）后推送 | — | 正常 |
| 网络抖动（非限流） | **不自动重试** | ❌ | `failed`（由用户判断是否重推） |

> **判定要点**：只有**云效服务端限流**才值得重试。真机证据：同一条凭据、同一个 `develop` 分支，隔 11 秒重推即成功，而紧接 3 秒后推送又被限流
> ⇒ **限流不是权限问题**，不要据此判"PAT 无写权限 / 分支被保护"。

---

## 4. 限流退避口径（严格照做）

- **退避序列（线性）**：第 1 次重试前等 **10s**，第 2 次 **20s**，第 3 次 **30s**。
- **最多 3 次重试**（不含首次尝试），单端最坏多等 60s。
- ⚠️ **是线性间隔，不是指数间隔**。
- 每次等待期间，在操作记录 / `checks[].detail` 里写明：`<仓库名> · <分支> —— 云效服务端限流（<原文摘要>）：Ns 后重试（第 k/3 次）`。
- 3 次用尽仍失败 ⇒ `failed`，文案：

```
推送失败：<仓库名> · <分支> —— <git 原文摘要>
（云效服务端限流：本轮已自动退避重试 3 次仍未成功，建议等约 1 分钟后重跑本步；已推送端的产物不受影响）
```

- **成功后如实记录**：若本次是限流退避后成功的，`checks[].detail` 写「云效限流后，第 N 次重试成功」。

---

## 5. 完成判定（逐端完备性）

- `artifacts[]` 里 **每端一条** 记录（分支名 + 提交 id）；
- `checks[]` **每端一条**，`name` 取端名；
- **任一端失败 ⇒ 整步 `failed`**，但已成功端的账目**照常写进档案**（重跑只补缺失）；
- `status="done"` 才允许推进 Step 7（`CanCreatePipelines` 读 Step 6 结论文件的逐端推送结果）。

> 只推一次不动就重推：重跑本步即可（先查后建 > 幂等），已推送的端不会重复提交（`git diff --cached --quiet` 为空则跳过）。
