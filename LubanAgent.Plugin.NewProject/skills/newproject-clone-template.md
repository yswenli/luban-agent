# 按模板建库并克隆（newproject-clone-template）

> 「新建项目」插件原子技能 · 对应 **Step 3** · **M4-P1 骨架版**（执行脚本在 M4-P6 补齐）。

## 何时使用

- 主 skill `newproject-create` 的第 3 步；
- 或单独补建 / 补克隆**某一个端**（例如只补 `-Mobile`）。

## 前置条件

- 已有项目档案（`ProjectSpec`），且第 2 步「初始化」已完成（工作区与会话就绪、云效凭据可取用）。
- 三个模板仓 `TJCES-Service` / `TJCES-Admin` / `TJCES-Mobile` 在组织内可见。
- 已 pin 的模板分支 / 标签可用（PRD §6 #5：不跟随模板升级，**找不到即阻断**并提示 pin 值失效）。

## 执行（全部经云效 MCP 工具，PRD §4.10.7）

对 Step 1 所选的**每个端**依次执行：

1. `mcp_yunxiao_codeup_search_repos`：按名称定位该端的 `TJCES-*` 模板仓，取模板仓 ID。
2. `mcp_yunxiao_codeup_create_repo`：
   - `templateProject.templateProjectId` = 上一步的模板仓 ID；
   - `templateProject.syncAllBranches = true`（否则新仓可能没有分支）；
   - `visibility = internal`（**组织内公开**，PRD §4.5 默认值）；
   - `name` / `path` 默认取 `{缩写}-Service|-Admin|-Mobile`（可选 `namespaceId` 或建在组织根下）。
3. `mcp_yunxiao_codeup_search_repos`（单查）：创建接口**不返回 HTTPS 克隆地址**，需再查一次取 `httpUrlToRepo`。
4. `mcp_yunxiao_codeup_clone`：按其 HTTPS 地址克隆到 `<本地目录>/<仓库名>`，分支为模板 pin 的分支。

克隆完成后由插件后续步骤做**占位符替换**（项目名 / 缩写 / 端口 / 数据库名等），本 skill 不重复实现该逻辑。

## 结果

- 档案字段：`Repositories[].WebUrl` / `CloneUrl` / `LocalPath` / `Cloned = true`
- 远端产物清单追加：各端仓库（`RemoteArtifactKind.Repository`），带云效控制台链接。

## 失败处理

| 现象 | 处理 |
|------|------|
| 401 / 令牌失效 | 引导用户到 codex 云效配置重录 PAT |
| 403 非组织成员 | 提示无组织 / 资源权限，不要重试硬闯 |
| 429 | 退避重试（次数有限），仍失败则中断并提示 |
| 模板分支不存在 | 阻断并提示该端 pin 值失效（不自动改用默认分支） |
| 仓库名已存在 | 停下让用户改 `{缩写}` 或仓库名，**不覆盖**已有仓库 |

中止 / 清理时**远端仓库不自动删除**，只保留孤儿清单（PRD §4.7）。