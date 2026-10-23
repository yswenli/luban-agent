# 编译检查（newproject-build）

> 「新建项目」插件原子技能 · 对应 **Step 5** · **助手驱动口径**（口径见 PRD §4.3 Step 5，v0.22；
> 本步判定口径**不变**）。组织专有值（含**本组织已知构建姿势**）不在本正文复述，
> 读工作区 `.luban-agent/plugin-newproject/docs/org-reference.md`（索引见 `docs/README.md`）。

## 何时使用

- 主 skill `newproject-create` 的第 5 步；
- 或**仅重新编译**（改代码后复验）。

## 执行（由你**亲自执行**，编译方式按工程类型判断）

**本技能要求项目助手在本会话内自己动手**，逐端编译并汇总：

1. 先看清每端仓库里有什么（`.sln` / `.csproj` / `package.json` / `pubspec.yaml` / `pom.xml` …），
   再自行决定编译命令——**编译方式由你按工程类型判断**，插件不预设命令。
2. 依赖未装就先装（`dotnet restore` / `npm install` / `cnpm install` / `pnpm install`），再编译；
   **每个端最多重试 2 次**。装依赖必须**独占一条命令并等它正常退出**：不要接管道截断输出、
   不要与其它命令并行——中途被杀会留下「目录在、文件不全」的半装包，比没装更坑。
3. 逐端得出「通过 / 失败」与一句话结论，写进**结论文件**；回复正文只回一行
   `DONE` 或 `FAILED: <失败端与原因>`。

常见形态（供参考，非硬性规定）：

| 端 | 典型命令 |
|----|----------|
| 后端（.NET） | `dotnet build <解决方案>.sln` |
| 管理端（Vite / Vue） | 按 `docs/org-reference.md` 的「构建姿势」节 |
| 移动端 | 按工程类型（如 Flutter 的 `flutter build`） |

### 本组织已知构建姿势

各端（尤其**管理端**）的分支口径、Node 版本、包管理器、包一层命令的写法、构建脚本名、
依赖入口缺失（`Failed to resolve entry for package "X"`）时的补跑姿势，以及兜底说明，
**统一读 `docs/org-reference.md` 的「构建姿势」节**——本正文不复述。
**与本组织既有做法冲突时以该文档为准**。

结论文件路径与 schema 由插件固定，**原样使用、不要另存别处**：

- 路径：`{项目工作区}\.luban-agent\plugin-newproject\results\step5-<时间戳>.json`
- schema：

```json
{
  "step": 5,
  "ok": false,
  "checks": [
    { "name": "Service", "ok": true,  "detail": "编译通过",           "command": "dotnet build xxx.sln" },
    { "name": "Admin",   "ok": false, "detail": "2 个错误：…",        "command": "<按 docs 构建姿势执行>" }
  ],
  "message": "<一句话总结>"
}
```

- `checks[].name` **必须用端标识**：`Service` / `Admin` / `Mobile`（不要写中文名、不要写仓库名）。
- `checks` **必须逐端齐全**：漏写某个端会被判为「该端未给结论」＝**本步失败**。
- 判定口径**不变**（四环齐）：文件存在 + JSON 可解析 + `ok: true` + 本轮事件流无真错误
  （守卫拒绝 / 用户取消**不算**错误）；`status` 是 Step 3 / 6 / 7 的阻断性字段，**本步可省略**。

> **不要调用** `newproject-*` 技能，也不要调用「新建项目」插件的任何工具或服务：本步的提示词正是该插件在**同一会话**里下发的，
> 回头调用插件服务会形成「同会话回合串行」的自等死锁。

## 硬边界

- **只编译、不改业务**（v0.28 放宽）：**允许修依赖树与环境**——装依赖、切换 Node 版本、
  重建 `node_modules`（含删掉半装的单个包目录后重装）；
  但**禁止改业务源码与工程配置**（业务代码、`package.json`、`vite.config`、`.csproj` / `.sln` 等一律不改；
  锁文件不要手工编辑，包管理器装依赖时自动更新属正常副作用）。源码级编译错误如实报出，用户修完后再重跑本步。

### 半装包（`Failed to resolve entry for package "X"`）的判定与修法

- **判定**：包目录存在，但 `package.json` 的 `module` / `main` / `exports` 指向的入口文件不存在
  （例：`node_modules/element-plus/es/index.mjs` 缺失）——这是安装被中途打断留下的半装包。
- **修法**：在本端仓库目录执行
  `node -e "require('fs').rmSync('node_modules/X',{recursive:true,force:true})"`，
  再重跑 `npm.cmd install`（只补这一个包，秒级）。
- **无效做法**：`npm rebuild X`（只重跑包自身生命周期脚本，预构建包不会补回缺失文件）；
  也不要因 `npm install` 几秒 exit 0 就认为修好——npm 只核对包目录是否存在，不校验文件完整性。
  多个包同时残缺时才考虑 `npm.cmd ci`（整树重建，可能超过 2 分钟）。
- 动过依赖后**必须重跑构建**，以最后一次构建输出定结论。
- 编译产物（`bin` / `obj` / `node_modules` / `dist`）留在原处，**不要删除、不要清理**。
- 命令首词必须在白名单内：`dotnet` / `git` / `node` / `npm` / `npx` / `pnpm` / `yarn` / `fnm` / `cnpm` /
  `python` / `py` / `python3` / `pip` / `pip3`；
  一次一条命令，可用 `&&` 串联（**每段首词同样受白名单约束**），不要用管道与重定向。

## 结果

- 逐端编译结论一目了然；**失败阻断 Step 6**（不自动继续），但**保留已生成代码**，不做回滚。

## 失败处理

- 编译错误：给出**出错文件 / 行号 / 错误码**摘要（最多 3 条），并提示可让 Agent 就地修复后重试本步。
- 缺 SDK / 还原失败：提示目标框架与 SDK 要求（本项目统一 `net10.0`）。
- 严禁用「跳过编译」之类话术糊弄：编译失败就是失败，如实上报。