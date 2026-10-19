# Luban Agent — 连接器配置化 冒烟清单（S1–S7）

本清单用于手工验证「连接器抽象 + 宿主按配置装配」这一体系（framework `LuBan.AIAgent` 连接器族），
覆盖修复轮 **C1（配置加载保护）/ R1（实例名即 MCP 名）/ R2（按实例名取凭据）/ R3（补种按实例名判重）/
R4（未声明即告警跳过）/ R5（按名解析 + 初始化契约）/ O1 / O2**。

与 TUI 功能清单（`LubanAgentCli/docs/TUI-SMOKE-TEST.md`）互不重叠：本文只验证连接器的装配、凭据与暴露面。

> **外部 MCP（配置驱动）**不在本文范围：见 `docs/EXTERNAL-MCP.md`（`mcps/*.json`、HTTP/stdio、凭据引用、冒烟 S8）。

## 前置条件

| 项 | 位置 |
|----|------|
| 宿主 | 桌面 `dotnet run --project luban-agent\LubanAgentCodex`；终端 `dotnet run --project luban-agent\LubanAgentCli` |
| 日志 | 宿主运行目录 `logs\info.txt` / `logs\warn.txt` / `logs\error.txt`（按级别分文件） |
| 用户配置 | `%LOCALAPPDATA%\LuBan\AIAgent\users\<user>\config.json`（键为 **PascalCase**：`Connectors`） |
| 工作区启用 | `<工作区>\.luban-agent\connectors\<实例名>.json`，内容 `{"enabled": true}` |
| 凭据键 | `connector:<实例名>:token`；**默认实例**即历史键 `yunxiao_pat`（二者等价，旧键仍可读） |

关键语义（判定依据）：

- **可用性**由 `config.json:Connectors` 决定（声明了才算「可用」）；**启用**由工作区 `connectors/<名>.json` 决定。
- 只声明未启用 / 工作区启用但宿主未声明 → 均**不暴露** MCP 工具（后者额外记一条 warn）。
- 暴露面名 = 连接器**实例名**（默认实例沿用 `yunxiao`），工具名前缀 `mcp_<mcp名>_*`。
- 任何失败路径都**只告警不中断**（未知/不可用连接器 = 告警 + 跳过）。

冒烟前请先备份 `config.json` 与工作区 `.luban-agent/`（S1–S7 会反复改写两者）。

---

## S1 · 老配置启动迁移（C1 + R3）

**操作**：备份 `config.json` → 删除其中 `Connectors` 节点（**保留** `Integrations` 里的 `yunxiao_pat`）→ 启动宿主 → 正常退出。

**预期证据**：

1. `config.json` 新增声明：
   `"Connectors": [ { "Name": "yunxiao", "Provider": "aliyunDevOps", "Enabled": false, "Settings": {} } ]`
2. `Integrations` 新增 `"connector:yunxiao:token"`（值与 `yunxiao_pat` 相同），且 `yunxiao_pat` **仍保留**。
3. 迁移前原文被备份为同目录 `config.json.bak`（仅首次迁移生成，不覆盖已有 `.bak`）。
4. `logs\error.txt` 无 `ConfigManager` 相关错误。

**失败判据**：`yunxiao_pat` 或 `Providers[].ApiKey` 丢失；`.bak` 缺失；`Connectors` 未补种或补成 `Enabled: true`；
每次启动都重复迁移（`.bak` 被反复重写）。

## S2 · 配置损坏保护（C1 核心）

**操作**：把 `config.json` 改成**非法 JSON**（如删掉末尾一个 `}`）→ 启动宿主 → 退出 → 检查文件内容与大小。

**预期证据**：

1. 磁盘文件**未被覆写**（仍是你写坏的那份内容，大小/内容不变）。
2. **未**生成新的 `.bak`（本次未发生落盘迁移）。
3. `logs\error.txt` 出现 `ConfigManager.Load 加载配置失败（保留磁盘原文件，不落盘覆盖）`。
4. 宿主仍能启动（内存空模型，不崩溃）。

**失败判据**：文件被清成 `{}` 或被规范化重写（说明仍在无条件落盘）；宿主启动即崩。

## S3 · 默认实例行为不回退（R1/R2 基线）

**操作**：`config.json` 声明 `yunxiao`（`Provider: aliyunDevOps`）+ `Integrations` 只保留旧键 `yunxiao_pat`（**不**新增
`connector:yunxiao:token`）→ 工作区写 `.luban-agent/connectors/yunxiao.json` = `{"enabled": true}` → 启动宿主并使用云效能力
（如插件里执行建库/克隆/推送）。

**预期证据**：

1. PAT 取用命中旧键 `yunxiao_pat`（回退读取生效，无「未配置 PAT」报错）。
2. `/mcp` 列表出现 `yunxiao`；工具名前缀为 `mcp_yunxiao_*`（与修复前完全一致）。
3. 仓操作行为与修复前一致；`logs\error.txt` 无新增异常。

**失败判据**：提示未配置 PAT；工具名前缀变化；`yunxiao` MCP 未出现。

## S4 · 多实例隔离（R1/R2 关键回归）

**操作**：`Connectors` 追加 `{ "Name": "yunxiao2", "Provider": "aliyunDevOps", "Enabled": false, "Settings": {} }` →
`Integrations` 写 `"connector:yunxiao2:token": "<另一个 PAT>"` → 工作区同时写 `connectors/yunxiao.json` 与
`connectors/yunxiao2.json`（均 `enabled: true`）→ 启动。

**预期证据**：

1. `/mcp` 同时出现 `yunxiao` 与 `yunxiao2` 两个 MCP。
2. 工具名出现两套前缀 `mcp_yunxiao_*` 与 `mcp_yunxiao2_*`，互不覆盖。
3. 经 `yunxiao2` 发起的云效请求使用 `connector:yunxiao2:token` 的那个 PAT（两实例凭据互不串用）。
4. 退出后 `config.json` 未被意外改写；`logs\error.txt` 无异常。

**失败判据**：只有一个 MCP 名（多实例互相覆盖）；两实例共用同一 PAT；切换 `yunxiao2` 的启用状态影响 `yunxiao` 的工具集。

## S5 · 工作区启用但宿主未声明（R4）

**操作**：从 `config.json` **删除** `yunxiao2` 声明，但**保留**工作区 `.luban-agent/connectors/yunxiao2.json`（`enabled: true`）→ 启动宿主，重复触发一次插件清单解析（或重开会话）。

**预期证据**：

1. `logs\warn.txt` 出现：`工作区启用了宿主未声明的连接器 'yunxiao2'，按未安装处理（请在 config.json:Connectors 内声明）。`
2. 同一条告警**每个实例名只记一次**（重复解析不再刷屏）。
3. 该连接器 MCP 不注册（无 `mcp_yunxiao2_*` 工具）。
4. 宿主与其它连接器照常工作（不 fail-fast、不中断会话）。

**失败判据**：宿主崩溃或启动失败；告警未记录；未声明的连接器仍被注册并暴露工具。

## S6 · 按名解析与初始化契约（R5）

**操作**：声明两个连接器（`yunxiao`、`yunxiao2`）且都启用 → 让某插件清单声明 `mcpServers: ["yunxiao2"]`（临时改插件
`plugin.json`，或经工作区 `.luban-agent/mcps/*.json`）→ 启动并观察工具与日志。

**预期证据**：

1. 精确命中 `yunxiao2`（工具前缀 `mcp_yunxiao2_*`），不落回 `yunxiao`。
2. `logs\warn.txt` **无** `插件声明的 MCP 服务器 'yunxiao2' 未由任何已声明连接器暴露` 之类的告警。
3. 同一实例的 `InitializeAsync` 每进程只执行一次：重复解析/重开会话不会重复初始化（无重复初始化日志）。

**失败判据**：解析失败或命中错误连接器；每次解析都重新初始化（日志重复出现初始化动作）。

## S7 · 未启用不暴露（配置驱动门禁）

**操作**：把工作区 `.luban-agent/connectors/yunxiao.json` 改成 `{"enabled": false}`（或直接删除该文件）→ 启动宿主，
并让插件清单声明 `mcpServers: ["yunxiao"]`。

**预期证据**：

1. 工具列表**无** `mcp_yunxiao_*` 工具（未启用即不暴露）。
2. `logs\warn.txt` 出现 `插件声明的 MCP 服务器 'yunxiao' 未由任何已声明连接器暴露，已跳过（请先在 config.json:Connectors 声明该连接器；…）`。
3. 宿主其余功能（对话、技能、规则、其它 MCP）不受影响。

**失败判据**：未启用仍暴露连接器工具（越权暴露）。

---

## 状态展示对照（供设置页 / 插件管理器核对）

`ConnectorRegistry.GetStates()` 的四态应与上面场景一一对上：

| 场景 | 状态 |
|------|------|
| 提供方已注册（如 `aliyunDevOps`），`config.json` 未声明 | `Available` |
| 已声明、工作区未启用（S7） | `Declared` |
| 已声明、工作区已启用、实例化成功（S3/S4/S6） | `Enabled` |
| 已声明但提供方无工厂（未安装 / 拼写错） | `Unavailable` |
| 实例化或初始化抛异常 | `Error`（附 `Message`；失败不缓存，下次可重试） |

## 回滚

1. 停宿主 → 用 `config.json.bak` 覆盖 `config.json`（若已生成）。
2. 删除工作区 `.luban-agent/connectors/` 下本次新增的实例文件。
3. 旧键 `yunxiao_pat` **未被删除**，故仅删 `connector:<名>:token` 即可回到迁移前状态。