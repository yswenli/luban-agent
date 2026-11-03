# 新建项目插件 · 知识正文（docs 索引）

> 本目录是「新建项目」插件的**知识正文**，随插件包分发，**在 Step 2 首次复制，并在 Step 3/4/5/6/7 每次下发前覆盖式重铺到项目工作区**：

```
{项目工作区}\.luban-agent\plugin-newproject\docs\
```

助手在执行 Step 3 / Step 6 / Step 7 时，请**直接读这个绝对路径下的文件**（提示词会把实际路径注入）。
`docs/` 只被复制（覆盖式），**不会删除**用户在该目录里手工补充的文件。

> ℹ️ `docs/` 是**覆盖式自愈**：每步下发前由插件把包内 `docs/` 重新覆盖到工作区（同名文件以插件包为准，用户手补文件保留）。
> 升级插件后，已建项目在**下一次相关步骤**即拿到最新知识——**不需要**重跑 Step 2。

## 唯一副本原则

组织专有值（Harbor 域名、服务连接 id、bucket、主机组 uuid、模板仓名、流水线 id 等）**只在 `docs/` 里存在一份**。

- 要改端口 / Harbor / 服务连接 / 主机组 / OSS 桶 / 构建姿势 —— **只改 `org-reference.md` 与对应的 `pipeline-*.yml.md`，不必重发插件**。
- 技能正文（`skills/*.md`）不复述这些值，只写「读 docs 的哪一节」。

## 索引（何时读哪一份）

| 文件 | 讲什么 | 何时读 |
|------|--------|--------|
| [`org-reference.md`](./org-reference.md) | 组织专有值总表：模板仓名与仓内模块、命名格式、pin 择优、占位符规则、三端流水线默认值、构建姿势、提交身份；每条注明**取值来源 + 生效位置** | **任何步骤开跑前**先读一遍；需要确认真实值（端口 / Harbor / 服务连接 / bucket / 主机组 uuid / 模板仓名）时必读。**这是唯一改值入口** |
| [`step3-repositories.md`](./step3-repositories.md) | Step 3：**单仓形态**（建库 / 克隆各一次，端=仓内模块目录）、模板库定位、pin 择优、建代码组 / 建库、建库后补查 `httpUrlToRepo`、轮询等 pin 分支、克隆（框架 git 工具组 + `workingDirectory` 口径）、占位符替换与模块目录归位、本地复核项 | 执行 **Step 3**（准备与执行续跑）时读；补克隆 / 补模块目录核对时也读 |
| [`step6-push.md`](./step6-push.md) | Step 6：**单仓单次推送**（三端同仓同分支）、推送分支口径（`pushBranch`，本组织 `develop`）、提交身份兜底、git 工具组调用姿势（`GitAddAsync(paths=["."])` / commit / push）、**报错→处置对照表**、凭据失败固定提示、限流退避（10/20/30s，最多 3 次） | 执行 **Step 6**（推送改动到自有仓库）时读；排查推送失败时必读 |
| [`pipeline-service.yml.md`](./pipeline-service.yml.md) | 后端（Service）新版流水线 YAML 模板：`Command` 登录 Harbor + `docker build/push`（构建目录 = 模块目录），**含独立 `deploy` 段**（`VMDockerDeploy`） | 执行 **Step 7** 准备阶段、渲染后端 YAML 时读 |
| [`pipeline-admin.yml.md`](./pipeline-admin.yml.md) | 管理端（Admin）新版流水线 YAML 模板：`NodeBuild`（Node 18/20 + pnpm）+ `OSSDelete`/`OSSUpload`（**无**独立部署阶段） | 执行 **Step 7** 准备阶段、渲染管理端 YAML 时读 |
| [`pipeline-mobile.yml.md`](./pipeline-mobile.yml.md) | 移动端（Mobile）新版流水线 YAML 模板：`NodeBuild`（Node 16 + npm）+ `OSSDelete`/`OSSUpload`（**无**独立部署阶段） | 执行 **Step 7** 准备阶段、渲染移动端 YAML 时读 |
| [`pipeline-conventions.md`](./pipeline-conventions.md) | 流水线名称格式、变量与缓存口径、**待人工绑定清单**、覆盖更新口径、`pipelineId` 溯源标注、**哪端有独立部署阶段** | 执行 **Step 7**（准备与执行续跑）时**必读**：`yamlIncludesDeployStage` 与 `pendingBindings` 的判定依据都在这里 |

## 与「技能」的区别（别搞混）

- `skills/*.md`：**编排入口**（流程、闸门、交互约定、结论文件契约），会被注册成技能，助手按技能调度执行。
- `docs/*.md`：**参考资料**（值、模板、命令、判定口径），**不注册成技能**，只作为正文被读。

技能正文里出现组织专有值时以 `docs/` 为准；两者冲突时**以 `docs/` 为准**并记入 `checks[].detail`。

## 结论文件里的自证要求（与 docs 的对应关系）

| 结论文件字段 | 依据的 docs 章节 |
|--------------|------------------|
| `artifacts[kind=repository].extra.templateRepository` / `templateProjectId` / `resolvedBranchSource` | `org-reference.md`（模板仓 + pin 择优）、`step3-repositories.md` |
| `checks[]` 的 `placeholder-residual`（`ok=true`） | `org-reference.md`（占位符规则）+ `step3-repositories.md`（替换语义与幂等） |
| `artifacts[kind=pipeline].extra.yamlIncludesDeployStage` | `pipeline-conventions.md`（只有后端有独立部署阶段） |
| `artifacts[kind=pipeline].extra.pendingBindings` | `pipeline-conventions.md`（待人工绑定清单） |
| `artifacts[kind=pipeline].extra.recommendedDeployTargetHostGroupId` / `recommendedDeployTargetUuid` | `pipeline-conventions.md`（部署目标要**同时**带数字 id 与 uuid） |
| Step 6 的 `checks[]` / 失败 `message` | `step6-push.md`（分支口径 + 报错对照表 + 凭据固定提示） |
