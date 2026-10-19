# 外部 MCP（配置驱动）—— 能力、示例与冒烟

本文覆盖实施计划 **W4 / T4.1–T4.4**：框架 `LuBan.AIAgent` 的「配置驱动外部 MCP」能力，
即不写代码、只放一个 JSON 就能接入第三方 MCP 服务（含带凭据的连接）。

与本文互补的两份文档：

| 文档 | 覆盖 |
|------|------|
| `CONNECTOR-SMOKE-TEST.md` | 框架**连接器**体系（`connectors/<实例名>.json`，进程内 MCP，S1–S7） |
| 本文 | 框架**外部 MCP**体系（`mcps/<名>.json`，HTTP/SSE/stdio 三种传输，S8） |

---

## 1. 配置位置与优先级

| 层级 | 路径 | 说明 |
|------|------|------|
| 全局（跨工作区） | `%LOCALAPPDATA%\LuBan\AIAgent\users\<user>\config.json` 的 `McpServers` | 与模型/提供方同级 |
| 工作区 | `<工作区>\.luban-agent\mcps\*.json` | **每个文件一个 MCP**，文件名任意；`enabled:false` 即跳过 |

字段（`McpServerConfig`，读取时大小写不敏感，camelCase / PascalCase 均可）：

| 字段 | 类型 | 适用传输 | 说明 |
|------|------|----------|------|
| `name` | string | 全部 | MCP 名；工具名前缀 `mcp_<name>_*`，**必须唯一** |
| `description` | string | 全部 | 描述，仅展示 |
| `transport` | string | 全部 | `stdio`（默认）/ `sse`（旧 HTTP+SSE）/ `http`（**Streamable HTTP**） |
| `command` / `args` | string / string[] | `stdio` | 启动命令与参数 |
| `url` | string | `http` / `sse` | 端点。`http` 直连该 URL；`sse` 用于旧式 `…/sse` + `…/message` 双端点 |
| `headers` | object | `http` / `sse` | 请求头；**值支持凭据引用** |
| `environment` | object | `stdio` | 子进程环境变量；**值支持凭据引用** |
| `enabled` | bool | 全部 | 默认 `true` |

> `transport:"http"` 是本期**语义修正**：修复前它与 `sse` 同义（URL 一律拼 `+"/message"`），
> 现在按 MCP 规范走 **Streamable HTTP（直连 URL，不拼路径）**。仓库内本次修正前无任何 `transport:"http"` 配置，故无兼容影响。

## 2. 凭据引用（令牌绝不落盘）

语法：**内联替换**，可出现在值的任意位置，键读到空白符为止。

```
$credential:<凭据键>
```

- 解析时机：建立连接时（`IMcpConfigValueResolver` → `ICredentialStore`），**不缓存明文**、不写日志。
- 典型用法：`"Authorization": "Bearer $credential:connector:yunxiao:token"`、
  `"YUNXIAO_ACCESS_TOKEN": "$credential:connector:yunxiao:token"`。
- 写入门禁（`McpConfigSecurityValidator`）：疑似令牌的**请求头**（名含 `authorization`/`token`/`secret`/`api-key`/`password`/`cookie`）
  与**环境变量**（名含 `token`/`secret`/`password`/`pat`/`api_key`/`credential`），其值**必须**是引用；
  URL 禁止内嵌 `https://user:password@host` 形式。违规会被 `McpManage` 写入工具**直接拒绝**。
- 键的写法：连接器凭据统一为 `connector:<实例名>:<字段>`（默认实例令牌即 `connector:yunxiao:token`，
  与历史键 `yunxiao_pat` 等价，旧键仍可读）。

## 3. 示例（可直接复制到工作区）

源文件在 `docs/examples/mcps/`；复制到 `<工作区>\.luban-agent\mcps\` 后把 `enabled` 改成 `true` 即生效。

### 3.1 云效官方托管 MCP（Streamable HTTP）

`docs/examples/mcps/yunxiao-remote.json`：

```json
{
  "name": "yunxiao-remote",
  "description": "阿里云云效官方托管 MCP（Streamable HTTP；PAT 走凭据引用，无明文）",
  "transport": "http",
  "url": "https://openapi-rdc.aliyuncs.com/ai/mcp",
  "headers": { "Authorization": "Bearer $credential:connector:yunxiao:token" },
  "enabled": false
}
```

> 官方亦接受 `X-Yunxiao-Token: $credential:connector:yunxiao:token`（不带 `Bearer` 前缀）；
> 二选一即可，本文示例用 `Authorization`。

### 3.2 云效官方 MCP（stdio，本机 Node 路线）

`docs/examples/mcps/yunxiao-remote-stdio.json`：

```json
{
  "name": "yunxiao-remote-stdio",
  "transport": "stdio",
  "command": "npx",
  "args": [ "-y", "alibabacloud-devops-mcp-server" ],
  "environment": { "YUNXIAO_ACCESS_TOKEN": "$credential:connector:yunxiao:token" },
  "enabled": false
}
```

> stdio 路线需要本机有 Node/npx，首次运行会联网拉包；本期**以 HTTP 路线为主**，stdio 作为备选。

### 3.3 裁剪工具集（可选，T4.4）

官方 MCP 支持 `toolsets` 查询参数裁剪暴露的工具面。**框架不加专用字段**，按官方约定二选一：

- URL 上带查询串：`"url": "https://openapi-rdc.aliyuncs.com/ai/mcp?toolsets=codeup,flow"`
- 请求头上带：`"headers": { "X-Devops-Toolsets": "codeup,flow", "Authorization": "Bearer $credential:connector:yunxiao:token" }`

具体可用的 toolsets 名称以阿里云官方文档为准（本期不作为框架契约）。

## 4. 能力边界：外部 MCP 路线 vs 连接器路线

两条路线**并存互补**，不是替代关系：

| 能力 | 官方云效 MCP | 产品连接器（`CodeupClient` / `FlowClient`） |
|------|--------------|---------------------------------------------|
| 仓库检索 / 分支 / 流水线读写 | ✅ | ✅ |
| **创建仓库**（`create_repository`） | ❌ 无 | ✅ |
| **代码组 / namespace 管理** | ❌ 无 | ✅ |
| **本地 git clone / push** | ❌ 无（只操作远端 API） | ✅（经 `ProcessRunner`） |
| 架构定位 | 零代码接入第三方能力 | 需宿主显式注册的进程内实现 |

**结论**：新建项目向导 Step 3（建库 + 克隆 + 推送）仍走**连接器**（`CodeupClient`），
不受官方 MCP 能力缺口影响；外部 MCP 用于补充查询类/流水线类工具与快速验证第三方服务。

## 5. 冒烟 S8 · 外部 MCP 配置驱动（T4.2）

**前置**：已在 Codex「设置 → 云效连接」保存过 PAT（即 `connector:yunxiao:token` 可解析）；有网络。

**步骤**

1. 把 `docs/examples/mcps/yunxiao-remote.json` 复制到 `<工作区>\.luban-agent\mcps\`，并把 `enabled` 改为 `true`。
2. 启动宿主，重开会话（或触发一次 MCP 重载）。

**预期证据**

1. 工具列表出现 `mcp_yunxiao-remote_*` 前缀的工具（由官方 MCP `tools/list` 返回）。
2. 打开该 JSON：**无**明文 PAT，只有 `$credential:connector:yunxiao:token`。
3. `logs\error.txt` 无 `加载工作区 MCP 失败` 与鉴权错误。

**失败判据**

- 工具不出现且日志报鉴权失败 → 检查凭据键是否能解析（`connector:yunxiao:token` / 旧键 `yunxiao_pat`）。
- 若把 `Authorization` 写成明文令牌 → 用对话让 agent 写入时应被**拒绝**（见 §2 写入门禁）；
  手工落盘虽不会被拒（读取不做校验），但**不要**这么做。

**stdio 版**（3.2）：重复上述步骤，把文件名换成 `yunxiao-remote-stdio.json`，预期同上，
差别是官方 MCP 作为 `npx` 子进程运行（任务管理器可观察到 node 进程）。

**边界验证**

- 明文令牌写入 `mcps/*.json` 被 `McpManage` 写入工具拒绝（错误文案含「必须使用凭据引用」）。
- `transport:"http"` 直连 URL（日志中无 `+"/message"` 拼出的地址）。

---

## 附：与连接器 smoke 的关系

`CONNECTOR-SMOKE-TEST.md` 的 S1–S7 验证**连接器**装配/凭据/暴露面；本文 S8 验证**外部 MCP**。
两者共用同一套凭据键（`connector:<实例名>:token`）与同一份 `.luban-agent/` 工作区目录。