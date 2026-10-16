# 按模板建库并克隆（newproject-clone-template）

> 「新建项目」插件原子技能 · 对应 **Step 3** · **M4-P3**（能力层已实现：`INewProjectService.CreateRepositoriesAsync`；
> 聊天入口在本 skill 内改为编排下面的 `mcp_yunxiao_*` 工具，二者语义一致、零分叉）

## 何时使用

- 主 skill `newproject-create` 的第 3 步；
- 或单独补建 / 补克隆**某一个端**（例如只补 `-Mobile`）。

## 前置条件

- 已有项目档案（`ProjectSpec`），且第 2 步「初始化」已完成（工作区与会话就绪、云效凭据可取用）。
- 三个模板仓 `TJCES-Service` / `TJCES-Admin` / `TJCES-Mobile` 在组织内可见。
- 已 pin 的模板分支 / 标签可用（PRD §6 #5：不跟随模板升级，**找不到即阻断**并提示 pin 值失效）。
  **pin 为空同样阻断**：先到插件配置 `templates[].branch` 登记真实值，不要猜默认分支。
- 引导用户到 codex「设置 → 云效连接」填好组织 ID 与 PAT（中心版 `organizationId` 必填）。

## 执行（全部经云效 MCP 工具，PRD §4.10.7）

对 Step 1 所选的**每个端**依次执行：

1. `mcp_yunxiao_codeup_search_repos`：按名称定位该端的 `TJCES-*` 模板仓，取其 ID
   （若插件配置 `templates[].projectId` 已登记，直接用该 ID，无需检索）；**找不到就停**，不要挑同名近似仓。
2. 目标仓库**同名先查**（`mcp_yunxiao_codeup_search_repos`）：**已存在即复用**（重跑幂等，不重复建库）；
   不存在才 `mcp_yunxiao_codeup_create_repo`：
   - `templateProject.templateProjectId` = 模板仓 ID；
   - `templateProject.syncAllBranches = true`（否则新仓可能没有分支）；
   - `visibility = internal`（**组织内公开**，PRD §4.5 默认值；用户确认区可选 `private`）；
   - `name` / `path` 默认取 `{缩写}-Service|-Admin|-Mobile`（可选 `namespaceId` 或建在组织根下）。
3. 取单仓详情（`httpUrlToRepo`）：创建接口**不返回 HTTPS 克隆地址**，必须再查一次。
4. `mcp_yunxiao_codeup_clone`：按其 HTTPS 地址克隆到 `<本地目录>/<仓库名>`，分支为模板 pin 的分支。
5. 克隆后做**占位符替换**（内容 + 文件/目录名；PRD §4.3 Step 3）：规则取自插件配置 `placeholderRules`，
   默认 `TJCES→{CODE}` / `Tjces→{Code}` / `tjces→{code}`，区分大小写、按 `From` 长度降序替换。

**目标目录已存在且非空**：给「复用已克隆 / 清空后重克隆 / 中止」三选一（PRD §4.3 Step 3），
不要静默覆盖。聊天入口拿不到用户回答时**中止本步**并给出引导文案。

## 结果

- 档案字段：`Repositories[].ExternalId` / `WebUrl` / `CloneUrl` / `LocalPath` / `Cloned = true`
- 远端产物清单追加：各端仓库（`RemoteArtifactKind.Repository`），带云效控制台链接。
- 每条产物**即时持久化**：中途失败时已成功端的账目不会丢，重跑只补缺失。

## 失败处理

| 现象 | 处理 |
|------|------|
| 401 / 令牌失效 | 引导用户到 codex 云效配置重录 PAT |
| 403 非组织成员 | 提示无组织 / 资源权限，不要重试硬闯 |
| 429 | 退避重试（次数有限），仍失败则中断并提示 |
| 模板分支（pin）不存在或为空 | 阻断并提示 pin 值缺失 / 失效（**不自动改用默认分支**） |
| 模板仓定位不到 | 阻断，提示确认模板仓在组织内可见或改登记 `templates[].projectId` |
| 仓库名已存在 | **复用**（幂等）；重名语义由本插件保证各端仓库名互不相同（建库计划校验） |
| 目标目录非空 | 三选一（复用已克隆 / 清空后重克隆 / 中止本步） |

中止 / 清理时**远端仓库不自动删除**，只保留孤儿清单（PRD §4.7）。