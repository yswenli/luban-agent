# 编译检查（newproject-build）

> 「新建项目」插件原子技能 · 对应 **Step 5** · **M4-P4 已落地**（口径见 PRD §4.3 Step 5，v0.22；**管理端已知构建姿势与边界放宽见 v0.28**）。

## 何时使用

- 主 skill `newproject-create` 的第 5 步；
- 或**仅重新编译**（改代码后复验）。

## 执行（由你**亲自执行**，编译方式按工程类型判断）

**本技能要求项目助手在本会话内自己动手**，逐端编译并汇总：

1. 先看清每端仓库里有什么（`.sln` / `.csproj` / `package.json` / `pubspec.yaml` / `pom.xml` …），
   再自行决定编译命令——**编译方式由你按工程类型判断**，插件不预设命令。
2. 依赖未装就先装（`dotnet restore` / `npm install` / `cnpm install` / `pnpm install`），再编译；**每个端最多重试 1 次**。
3. 逐端得出「通过 / 失败」与一句话结论，写进**结论文件**；回复正文只回一行 `DONE` 或 `FAILED: <失败端与原因>`。

常见形态（供参考，非硬性规定）：

| 端 | 典型命令 |
|----|----------|
| 后端（.NET） | `dotnet build <解决方案>.sln` |
| 管理端（Vite / Vue） | `fnm exec --using=16 -- npm.cmd install` → `fnm exec --using=16 -- npm.cmd run build:dev` |
| 移动端 | 按工程类型（如 Flutter 的 `flutter build`） |

### 本组织已知构建姿势（与你的默认判断冲突时以此为准）

**管理端（Admin，Vite + Vue）**：本仓库用 **npm 系**构建（**不要用 pnpm**），Node 版本必须是 **16**：

1. **分支**：本仓库的工作分支是 `develop`（若当前不在 `develop` 且工作树干净：`git checkout develop`）。
2. **Node 16**：用 `fnm` 把命令包一层 —— `fnm exec --using=16 -- npm.cmd <子命令>`
   （Windows 下 `fnm` 直接 spawn `npm` 会报 `program not found`，**必须写 `npm.cmd`**；
   不要用 `fnm env | Out-String | Invoke-Expression` 这类管道 / 求值写法，会被守卫拒绝）。
3. **装依赖**：`fnm exec --using=16 -- npm.cmd install --registry=https://registry.npmmirror.com`
   （内网慢可换 `cnpm` 同源镜像；`node_modules` 若原本是 pnpm 装的，`npm install` 会补装成 npm 布局，属正常）。
4. **构建**：`fnm exec --using=16 -- npm.cmd run build:dev` —— **不是 `npm run build`**，本仓库的构建脚本叫 `build:dev`。
5. **若报 `Failed to resolve entry for package "X"`**：说明该依赖自身的构建脚本被跳过、包入口没生成
   （`@vue-office/*` 系列常见）——用 `fnm exec --using=16 -- npm.cmd rebuild X` 补跑后重试一次。

> **不要**用 `pnpm install && pnpm run build` 这套默认姿势打管理端：pnpm v10 默认跳过依赖的构建脚本，
> `@vue-office/*` 的包入口（`lib/index.js`，由包自身 `postinstall` 生成）会缺失，`vite build` 直接报入口解析失败。

> **兜底**：若机器上确实没有 `fnm` / Node 16 / `cnpm`，**不要**自行安装全局工具链、也不要改仓库配置——
> 把缺失项如实写进该端结论的 `detail`，本步按失败上报，由用户补好环境后重跑。

结论文件路径与 schema 由插件固定，**原样使用、不要另存别处**：

- 路径：`{本地目录}\.luban-agent\plugin-newproject\results\step5-<时间戳>.json`
- schema：

```json
{
  "step": 5,
  "ok": false,
  "checks": [
    { "name": "Service", "ok": true,  "detail": "编译通过",           "command": "dotnet build xxx.sln" },
    { "name": "Admin",   "ok": false, "detail": "2 个错误：…",        "command": "fnm exec --using=16 -- npm.cmd run build:dev" }
  ],
  "message": "<一句话总结>"
}
```

- `checks[].name` **必须用端标识**：`Service` / `Admin` / `Mobile`（不要写中文名、不要写仓库名）。
- `checks` **必须逐端齐全**：漏写某个端会被判为「该端未给结论」＝**本步失败**。

> **不要调用** `newproject-*` 技能，也不要调用「新建项目」插件的任何工具或服务：本步的提示词正是该插件在**同一会话**里下发的，
> 回头调用插件服务会形成「同会话回合串行」的自等死锁。

## 硬边界

- **只编译、不改业务**（v0.28 放宽）：**允许修依赖树与环境**——装依赖、用 `fnm` 切 Node 版本、
  重跑依赖自身的构建脚本（`npm rebuild X`）、必要时重建 `node_modules`；
  但**禁止改业务源码与工程配置**（业务代码、`package.json`、`vite.config`、`.csproj` / `.sln` 等一律不改；
  锁文件不要手工编辑，包管理器装依赖时自动更新属正常副作用）。源码级编译错误如实报出，用户修完后再重跑本步。
- 编译产物（`bin` / `obj` / `node_modules` / `dist`）留在原处，**不要删除、不要清理**。
- 命令首词必须在白名单内：`dotnet` / `git` / `node` / `npm` / `npx` / `pnpm` / `yarn` / `fnm` / `cnpm` / `python` / `pip`；
  一次一条命令，可用 `&&` 串联（**每段首词同样受白名单约束**），不要用管道与重定向。

## 结果

- 逐端编译结论一目了然；**失败阻断 Step 6**（不自动继续），但**保留已生成代码**，不做回滚。

## 失败处理

- 编译错误：给出**出错文件 / 行号 / 错误码**摘要（最多 3 条），并提示可让 Agent 就地修复后重试本步。
- 缺 SDK / 还原失败：提示目标框架与 SDK 要求（本项目统一 `net10.0`）。
- 严禁用「跳过编译」之类话术糊弄：编译失败就是失败，如实上报。