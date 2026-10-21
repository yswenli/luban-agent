# 编译检查（newproject-build）

> 「新建项目」插件原子技能 · 对应 **Step 5** · **M4-P4 已落地**（口径见 PRD §4.3 Step 5，v0.22）。

## 何时使用

- 主 skill `newproject-create` 的第 5 步；
- 或**仅重新编译**（改代码后复验）。

## 执行（由你**亲自执行**，编译方式按工程类型判断）

**本技能要求项目助手在本会话内自己动手**，逐端编译并汇总：

1. 先看清每端仓库里有什么（`.sln` / `.csproj` / `package.json` / `pubspec.yaml` / `pom.xml` …），
   再自行决定编译命令——**编译方式由你按工程类型判断**，插件不预设命令。
2. 依赖未装就先装（`dotnet restore` / `npm install` / `pnpm install`），再编译；**每个端最多重试 1 次**。
3. 逐端得出「通过 / 失败」与一句话结论，写进**结论文件**；回复正文只回一行 `DONE` 或 `FAILED: <失败端与原因>`。

常见形态（供参考，非硬性规定）：

| 端 | 典型命令 |
|----|----------|
| 后端（.NET） | `dotnet build <解决方案>.sln` |
| 管理端（Vite / Vue） | `pnpm install` → `pnpm run build` |
| 移动端 | 按工程类型（如 Flutter 的 `flutter build`） |

结论文件路径与 schema 由插件固定，**原样使用、不要另存别处**：

- 路径：`{本地目录}\.luban-agent\plugin-newproject\results\step5-<时间戳>.json`
- schema：

```json
{
  "step": 5,
  "ok": false,
  "checks": [
    { "name": "Service", "ok": true,  "detail": "编译通过",           "command": "dotnet build xxx.sln" },
    { "name": "Admin",   "ok": false, "detail": "2 个错误：…",        "command": "pnpm run build" }
  ],
  "message": "<一句话总结>"
}
```

- `checks[].name` **必须用端标识**：`Service` / `Admin` / `Mobile`（不要写中文名、不要写仓库名）。
- `checks` **必须逐端齐全**：漏写某个端会被判为「该端未给结论」＝**本步失败**。

> **不要调用** `newproject-*` 技能，也不要调用「新建项目」插件的任何工具或服务：本步的提示词正是该插件在**同一会话**里下发的，
> 回头调用插件服务会形成「同会话回合串行」的自等死锁。

## 硬边界

- **只编译、不修复**：即使报错也**不要**去改代码 / 配置 / 项目文件，把错误如实报出来，用户修完后再重跑本步。
- 编译产物（`bin` / `obj` / `node_modules` / `dist`）留在原处，**不要删除、不要清理**。
- 命令首词必须在白名单内：`dotnet` / `git` / `node` / `npm` / `npx` / `pnpm` / `yarn` / `python` / `pip`；
  一次一条命令，可用 `&&` 串联（**每段首词同样受白名单约束**），不要用管道与重定向。

## 结果

- 逐端编译结论一目了然；**失败阻断 Step 6**（不自动继续），但**保留已生成代码**，不做回滚。

## 失败处理

- 编译错误：给出**出错文件 / 行号 / 错误码**摘要（最多 3 条），并提示可让 Agent 就地修复后重试本步。
- 缺 SDK / 还原失败：提示目标框架与 SDK 要求（本项目统一 `net10.0`）。
- 严禁用「跳过编译」之类话术糊弄：编译失败就是失败，如实上报。