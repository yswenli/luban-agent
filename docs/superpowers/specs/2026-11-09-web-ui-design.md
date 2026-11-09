# LubanAgent 浏览器端（Web UI）设计

- 日期：2026-11-09
- 范围：`luban-agent` / `LubanAgentWebHost`
- 状态：待确认

## 1. 意图

在 `LubanAgentWebHost` 已有的 HTTP/SSE 接口之上，加一套浏览器界面，让用户不必开终端或桌面端就能使用 LubanAgent。

界面形态与信息结构对齐三张参考图（`docs/1.png` 会话列表、`docs/2.png` 设置页、`docs/3.png` 对话页、`docs/4.png` 输入区 `+` 菜单）。

## 2. 目标

1. 打开页面第一屏是**会话列表**：顶部按工作区筛选，列表按「今天 / 更早」分组，每行标注所属工作区；点开即可多轮对话。
2. 对话页支持流式输出、工具调用过程展示、工具确认弹窗、附件（拖放 + 粘贴）与斜杠命令。
3. 设置页覆盖桌面端（`LubanAgentCodex`）的配置能力：技能、规则、MCP、A2A、供应商、模型，并保留「★ 全局 / 当前工作区」作用域切换。
4. 现有功能不退化：`LubanAgentCli`、`LubanAgentCodex`、`LubanAgent.Tests` 的构建与测试全部照旧通过。

## 3. 非目标

- 不做插件管理（`LubanAgent.Plugin.NewProject` 相关入口不出现）。
- 不做连接器管理（云效 `Connectors/AliyunDevOps` 相关入口不出现）。
- 不做 `+` 菜单里的「上下文 @」与「Shell 命令 !」。
- 不做多用户 / 远程访问：仍只允许本机回环（`WebApi.IsLoopback`，`LubanAgentWebHost/Hosting/WebApi.cs:210-211`）。
- 不改 `LubanAgentCli` / `LubanAgentCodex` 的界面与行为。
- 不改框架 `LuBan.AIAgent` 的公开契约（斜杠命令元数据直接复用，不新增枚举值）。

## 4. 反目标

- 页面在第一屏就要求用户先选工作区、再选模型，层层下拉才看到会话。
- 为了省事默认放行工具调用（确认框是硬需求）。
- 为了页面好看而新增一套与桌面端语义不同的配置存储（必须落同一批文件和同一份 `config.json`）。

## 5. 现状事实（均为代码核对结论）

| 事实 | 依据 |
|---|---|
| **`Root` 现在传的是绝对路径，SAEA 26.10.8.1 下静态文件必然 404**：SAEA 在构造静态文件索引时执行 `_root.Replace("/","").Replace("\\","")` 再 `Path.Combine(appDir, 结果)`，绝对路径被拼成 `C:Users...wwwroot` 之类的不存在路径。实测：绝对路径 3 种写法全部 404，`Root="wwwroot"`（相对、单段）全部 200；相对名按 `AppContext.BaseDirectory` 解析，与进程 CWD 无关 | `LubanAgentWebHost/Program.cs:79`（建目录）、`:85`（`Root = root`）；`SAEA.Http` 的 `HttpUtility` 构造与 `MapPath`（ilspy 反编译）；可复跑探针 |
| SAEA.MVC 默认 `Root="wwwroot"`、`DefaultPage="index.html"`、`IsStaticsCached=True`、`Timeout=180`、`BufferSize=65536` | 框架配置类型默认值（反射核对） |
| `IsStaticsCached=True` 时静态文件**内容在首次请求后常驻进程内存**：改盘上同名文件必须重启进程才生效；启动后新增的文件可即时访问（走 fallback 路径拼接） | 探针实测：改 `cachetest.html` 后仍返回旧内容，重启后返回新内容 |
| 静态响应不带 charset（html 为 `text/html`，js 为 `application/x-javascript`、css 为 `text/css`）；MIME 表 188 项，`woff2`/`ttf`/`json`/`map`/`webmanifest`/`mjs` **无映射**，落到 `unknown/unknown` | `SAEA.Http.HttpMIME`（反编译）+ 探针实测响应头 |
| 路由只认 `/{controller}/{action}`，前端深链必须用 hash | `LubanAgentCore/Protocol/AgentApiRoutes.cs:12-15` |
| 接口鉴权：回环来源校验 + 可选 `Authorization: Bearer <token>` | `Hosting/WebApi.cs:50-79`、`:181-201` |
| 对话接口共 5 个：`/agent/send`、`/agent/chat`(SSE)、`/agent/confirm`、`/agent/cancel`、`/agent/reset` | `Controllers/AgentController.cs:43/149/253/291/315` |
| SSE 线格式 `event: {name}\ndata: {json}\n\n`，心跳为 `: ping` | `Protocol/Events/AgentSseFrame.cs:58-60`、`:27-29` |
| SSE 事件 14 种：session / think.delta / text.delta / tool.started / tool.completed / tool.failed / tool.confirm / attachment.added / attachment.skipped / error / done / plan.item / tool.denied / orchestration.progress | `Protocol/Events/AgentSseEventNames.cs` |
| 工具确认回传 `allow` / `deny` / `allowAll` / `allowSession` | `Controllers/AgentController.cs:484-505` |
| 会话接口 19 个、工作区接口 10 个、LLM Wiki 13 个、A2A 1 个、系统 1 个 | `Protocol/AgentApiRoutes.cs:37-188` |
| 请求体统一为单字段 `payload`，响应统一为 `{success, code, message, data}` | `Protocol/AgentApiJson.cs`、`Protocol/ApiEnvelope.cs` |
| 附件只接受本地绝对路径或 URL，不接受上传字节 | `Protocol/Dtos/AgentChatRequest.cs:24` |
| 每轮附件上限 5（产品侧），超出部分被静默截断并回一条「已跳过」提示 | `Services/AttachmentService.cs:42`、`:415-416`、`:441-447` |
| 附件类型白名单 **42** 种扩展名，**不含 PDF**；`ImageExtensions` 11 项（含 `.heic`/`.heif`），`.svg` 不按图片处理 | `LuBan.Common/IO/MediaTypeUtil.cs:41-93` |
| 图片不按大小拒绝：>2000px 或 >5MB 会缩放/重编码，解码失败才抛 `NotSupportedException`（提示转 PNG/JPEG）；文本单文件 >10MB 才拒绝 | `LuBan.AIAgent/Attachments/DefaultAttachmentProcessor.cs:49-52`、`:107`、`:115-133`、`:179-180` |
| **消息里没有 `tool` 角色**：工具调用与思考内容都挂在 assistant 消息上。`ToolCalls` 是 JSON **字符串** `[{name, callId, arguments}]`（camelCase，`arguments` 本身又是 JSON 字符串）；`Attachments` 是 `AttachmentRecord[]`（PascalCase：`FileName`/`MediaType`/`FileSize`/`SourcePath`/`Kind`/`TextPayload`/`ProcessedPath`/`IsLargeText`）。`/session/messages` 原样返回这两个字符串，前端需二次解析 | `LuBan.AIAgent/Sessions/SessionChatHistoryProvider.cs:220`、`:233`、`:376-390`；`Attachments/AttachmentRecord.cs:34-70`；`Sessions/ISessionManager.cs:200-246` |
| 工具确认：SSE `tool.confirm` 载荷 `{confirmationId, toolName, arguments, timeoutSeconds}`；回传 `/agent/confirm` 收 `{sessionId, confirmationId, result}`，`result` ∈ `allow`/`deny`/`allowAll`/`allowSession`；超时（默认 60 秒，`LuBanAgent:Confirmation:TimeoutSeconds`，当前 appsettings 未配置）按 **拒绝** 处理，之后再提交返回 404 | `LuBan.AIAgent/Protocol/AgentEventProtocol.cs`（`ToFrame`）；`Hosting/AgentStreamEvent.cs:129-136`；`Hosting/DefaultToolConfirmationBroker.cs:59-61`、`:77-104`；`Configuration/LuBanAgentOptions.cs:160`；`Controllers/AgentController.cs:253-289`、`:484-505` |
| 权限模式 4 值，稳定键名 `default`/`plan`/`accept-edits`/`bypass`，循环顺序 Default→Plan→AcceptEdits→Bypass→Default；非法文本回落 Default | `Services/PermissionModeRules.cs:30-38`、`:44-51`、`:58-64`；`Controllers/AgentController.cs:479-482` |
| 推理强度走 `Enum.TryParse<ReasoningEffort>`（None/Low/Medium/High），空或非法回落 `null`（沿用模型默认） | `Controllers/AgentController.cs:471-474` |
| **POST 请求体必须带 `Content-Type: application/json; charset=utf-8`**：SAEA.MVC 只在 `ContentType` 含该串时才把 JSON 解成 `Dictionary<string,string>` 绑定到动作参数，`application/json` 裸跑会被静默忽略、`payload` 为空 | `SAEA.MVC` 的 `Controller` 调用前参数绑定（反编译）；探针实测：缺 charset → `payloadLength=0` |
| 静态路由：`/` 返回 `DefaultPage`（`index.html`）；未知路径返回 **404 且无 SPA 回退** | 探针实测（`GET /` 200、`GET /nope` 404） |
| SAEA 的 multipart 解析随体积超线性变慢：1MB≈4s、2MB≈13s、3MB≈27s、4MB≈48s、5MB≈77s；同样的 10MB 文件走 JSON（base64 后 13.98MB 请求体）只要 ≈2s | 探针实测（同一进程、同一 `Root`，`curl --max-time`） |
| 斜杠命令清单在框架，**16 条**，带 `SlashCommandClient` 标记（`Cli`=1、`Codex`=2、`Both`=3）；其中 `/agi`、`/init` 仅 Cli | `LuBan.AIAgent/Hosting/Commands/CommandCatalog.cs:84-109`、`SlashCommandDefinition.cs:30-57` |
| 桌面端命令分发：管理类命令打开设置页对应页签 | `LubanAgentCodex/ViewModels/MainWindowViewModel.cs`（`ShowProviderManager` → `OpenSettings(SettingsTabKind.Provider)`） |
| 设置页 6 类配置都有现成产品服务 | `Services/Management/I{Skill,Rule,Mcp,ProviderModel}ManagementService.cs`、`A2A/Configuration/A2AWorkspaceConfigStore.cs` |
| 会话表已存 `WorkspaceId`，但框架 `SessionInfo` 不含该字段，`/session/list` 会丢 | `Entities/DbSession.cs`（`WorkspaceId`）、`Repositories/SessionRepository.cs:81`（返回 `List<DbSession>`）、`LuBan.AIAgent/Sessions/ISessionManager.cs:159` |
| 桌面端设置页的技能 / 规则 / MCP 三个页签**只列文件型条目**（`ListFileIds(scopeRoot)`），不是「内置 + 配置 + 文件」合并视图 | `LubanAgentCodex/ViewModels/Settings/SettingsWindowViewModel.cs:96-101` |
| 文件型条目不支持启停：`SetEnabledAsync` 对文件来源直接抛 `NotSupportedException` | `Services/Management/SkillManagementService.cs`、`RuleManagementService.cs`、`McpManagementService.cs` 的 `SetEnabledAsync` |
| MCP 桌面页只有列表 / 读取 / 保存 / 删除 / 建默认，没有连接测试与工具列举 | `LubanAgentCodex/ViewModels/Settings/McpTabViewModel.cs:30-109` |
| A2A 配置**只作用于工作区**，全局作用域下条目为空且不允许新建 | `ViewModels/Settings/A2aTabViewModel.cs:25`、`:62-66` |
| `ProviderConfig.ApiKey` 明文存 `config.json`，与集成 PAT 同基线；`DpapiCredentialProtector` 只有注册、全仓库无调用点 | `Configuration/ProviderConfig.cs:31`、`Configuration/ConfigManager.cs:355-373`、`Configuration/AppConfig.cs`（Integrations 注释）、`Hosting/AgentHostBuilder.cs:167` |
| 模型选择是**全局**的，`AgentChatRequest` 没有 provider / model 字段，`SetSelectionAsync` 明确不重建运行时 | `Protocol/Dtos/AgentChatRequest.cs`、`Services/Management/IProviderModelManagementService.cs` |
| `AgentHost.Reset` 在回合运行中只打延迟重载标记，由回合收尾时释放 | `LuBan.AIAgent` 的 `AgentHost.Reset` |

## 6. 已确定的决策

| 决策 | 内容 |
|---|---|
| 前端技术栈 | SolidJS + TypeScript + Vite |
| 托管方式 | 构建产物落到 `LubanAgentWebHost/wwwroot`，由 SAEA.MVC 静态托管，同进程同端口，不另起前端服务 |
| 路由 | hash 路由：`#/`、`#/s/<sessionId>`、`#/settings/<tab>` |
| 项目 = 工作区 | 会话列表的分组单位就是工作区 |
| 第一屏 | 会话列表 |
| 附件交互 | 拖放 + 粘贴，最多 5 个，类型按后端白名单 |
| 工具确认 | 网页内弹窗，允许 / 拒绝 / 本轮全允许 / 本会话全允许 |
| `+` 菜单 | 只做「图片和文件」与「命令」两项 |
| 设置项来源 | 对齐 `LubanAgentCodex` 的设置页（7 项中剔除「云效连接」→ 6 项） |
| 设置作用域 | 保留「★ 全局 / 当前工作区」两级切换 |
| 插件 / 连接器 | 暂不支持 |

### 6.1 由本文作者决定、待确认的项

1. **供应商 API Key 允许在网页录入**：读接口一律返回掩码（前 4 + 后 4），写接口接受明文，编辑时留空表示「不修改」。落盘基线与桌面端完全一致——`ApiKey` 明文存 `config.json`，本设计**不引入新加密层**，也不改动既有存储格式。
2. **构建产物提交进仓库**：`wwwroot` 内容随代码提交，`dotnet build` 不依赖 Node；前端开发者用 `npm run dev` 本地开发。
3. **新增会话摘要接口**而不是改框架 `SessionInfo`：避免动框架公开契约。
4. **配置写入后自动 `/agent/reset`**：`AgentHost.Reset` 自带「回合运行中延迟重载」语义，前端不额外拦。模型切换不触发重建（与桌面端一致，改的是全局选择）。
5. **分期交付**（顺序而非砍范围）：P1 会话列表 + 对话 + 附件 + 命令 + 确认框；P2 设置页 6 页签；P3 打磨（空态、快捷键、帮助）。
6. **技能 / 规则 / MCP 三个页签只列文件型条目**，与桌面端设置页一致；内置技能与 `config.json` 自定义项在网页不出现，也不提供启停开关。
7. **A2A 页签不提供全局作用域**：桌面端 A2A 只作用于工作区，网页沿用；全局下显示「A2A 配置仅作用于工作区」。
8. **不提供 MCP 连接测试与工具列举**：桌面端设置页没有这两项能力，网页不额外扩面。

### 6.2 落地前置修复（P1 第一件事）

`Program.cs:79` 用 `Path.Combine(AppContext.BaseDirectory, "wwwroot")` 算出绝对路径，`:85` 直接把它赋给 `Root`；SAEA 26.10.8.1 下必然 404（`HttpUtility` 会去掉所有分隔符再拼路径）。改为：

```csharp
var root = Path.Combine(AppContext.BaseDirectory, "wwwroot"); // 只用于建目录
Directory.CreateDirectory(root);
var mvcConfig = new SAEAMvcApplicationConfig { Port = settings.Port, Root = "wwwroot", /* ... */ };
```

即 **`Root` 只传相对单段目录名**，目录创建保留绝对路径写法；SAEA 自己按 exe 目录解析 `wwwroot`。这一步不做，后面所有页面都打不开。

## 7. 页面设计

### 7.1 全局骨架

- 顶部：标签栏（当前会话标题）+ 全局搜索入口（本版只做会话搜索，不做多标签页）。
- 底部左：「设置」；底部右：「帮助」（打开内置说明面板，内容是快捷键与命令清单）。
- 配色取向：浅色、中性灰阶、单一强调色（选中态与焦点环），卡片圆角与 1px 描边；不引入 UI 组件库。
- 字体：系统 UI 字体栈；正文 14px；等宽字体只用于代码块、路径、命令。

### 7.2 会话列表（`#/`）

- 「项目」区：横向工作区 chips，当前工作区高亮；点击 chip = 切换当前工作区（`/workspace/switch`）并**用它作为列表筛选条件**，不是把列表拆成多个分组。
- 搜索框：客户端过滤，匹配会话标题与工作区名（不新增后端接口）。
- 分组：`今天` / `更早`（按 `UpdatedAt ?? CreatedAt` 落在本机当天与否），与参考图一致。
- 会话行：标题 + 右侧灰色工作区名；hover 出现「重命名 / 删除」。
- 分组标题右侧：`新建会话`（`/session/create`，带当前工作区）→ 跳 `#/s/<id>`。
- 空态：无会话时显示引导文案与「新建会话」主按钮。

### 7.3 对话页（`#/s/<sessionId>`）

- 顶栏：`工作区名 / 会话标题`，右侧 Token 累计与「…」菜单（重命名 / 清空消息 / 删除会话）。
- 消息区：
  - 用户消息：浅色底块 + 右上角时间；附件 chip 取自消息 `Attachments` JSON（PascalCase 字段），只显示文件名 / 大小 / 类型，不回读文件内容（`SourcePath` 可能已被清理）。
  - 助手消息：Markdown 渲染（标题、列表、表格、行内代码、代码块带复制按钮）。
  - 思考内容：默认折叠的「思考过程」区块，流式追加；历史回放读同一条 assistant 消息的 `Thinking`。
  - 工具卡片：工具名 + 参数摘要 + 状态（执行中 / 完成 / 失败 / 已拒绝）；点击展开完整参数与结果摘要。
  - **历史回放的边界**：落库只有 assistant 消息上的 `ToolCalls`（`name`/`callId`/`arguments`，`arguments` 是 JSON 字符串），**没有工具结果**，也没有 `tool` 角色消息。回放时工具卡片只重建「调用了什么、参数是什么」，状态标为「历史记录」，不得伪造完成结果；本轮实时回合才有 `tool.completed` 的 `resultSummary`。
  - 计划项与子代理进度：独立窄条，位于当前回合末尾。
  - 附件：本轮已加入的附件以 chip 显示，`attachment.skipped` 帧在消息区插入提示行。
- 输入区（对齐 `docs/3.png`、`docs/4.png`）：
  - 多行自适应高度；`Enter` 发送，`Shift+Enter` 换行。
  - `+` 菜单：`图片和文件`（Ctrl+U）、`命令`（`/`）。没有「上下文」「Shell 命令」。
  - 输入 `/` 弹出命令列表（键盘上下选择、回车执行），数据来自 `/system/commands`。
  - 粘贴图片、拖放文件 → 进入待发送队列；超过 5 个或类型不在白名单（42 项）时即时提示并且不入队。
  - 底部一行：`+`、`供应商显示名 / 模型名`（下拉切换，写全局选择）、`权限模式`（显示 `默认 (Default)` / `计划 (Plan)` / `接受编辑 (AcceptEdits)` / `跳过权限 (Bypass)`，点击按上面顺序循环）、`推理强度`（None / Low / Medium / High，默认「跟随模型」）、发送按钮（回合进行中变为「停止」）。
  - 权限模式与推理强度是**每轮下发**参数：切到 Plan 后必须随下一轮 `/agent/chat` 的 `permissionMode` 发出，否则服务端一律按 Default 处理（`AgentChatRequest.PermissionMode` 注释已明确）。
- 停止：`POST /agent/cancel`，带 `sessionId` 与首帧 `session` 回传的 `runId`；不带 `runId` 会取消该会话当前回合。
- 工具确认弹窗：数据来自 `tool.confirm` 帧（`confirmationId` / `toolName` / `arguments` / `timeoutSeconds`）。标题为工具名，正文为参数 JSON（可折叠），按钮「拒绝」（deny）、「允许」（allow）、「本轮全允许」（allowAll）、「本会话全允许」（allowSession）；按 `timeoutSeconds` 显示倒计时，超时按拒绝处理并给出提示，此时再点按钮会收到 404「确认请求不存在、已过期或会话不匹配」。

### 7.4 设置页（`#/settings/<tab>`）

- 左导航：`← 返回应用`、页签搜索框、6 个页签（技能、规则、MCP 服务、A2A 协作、供应商、模型）。
- 作用域切换：技能 / 规则 / MCP 三个页签显示 `★ 全局` 与 `当前工作区`；A2A 只有工作区（切到全局时列表为空并给出「A2A 配置仅作用于工作区」提示、禁用新建）；供应商 / 模型固定全局。
- 技能 / 规则 / MCP 的列表是**文件型条目**（目录名或文件名），与桌面端一致：没有启停开关，没有内置项。MCP 页不提供连接测试与工具列举。
- 每个页签统一为三栏结构：条目列表 + 表单 + 动作栏（保存 / 删除 / 新建）。
- 字段严格对齐桌面端表单定义（`LubanAgentCodex/ViewModels/Settings/SettingsContracts.cs`）：
  - 技能：名称、描述、分类、触发词、正文。
  - 规则：Id、名称、描述、动作类型通配、目标通配、allow/deny、优先级、启用、引导正文。
  - MCP：名称、描述、传输方式、命令或 URL、参数（一行一个）、启用。
  - 供应商：标识（小写）、API Key、BaseUrl、显示名、网络超时（秒）、自定义模型。
  - 模型：按供应商列出，支持新增 / 重命名 / 删除 / 设为当前。
  - A2A 服务端：监听地址、端口、端点前缀、是否要求 API Key、API Key 列表、对外名称、描述、版本。
  - A2A 客户端：标识、远端地址、API Key、是否注册为本地子代理、远端 Agent 名、是否声明 AllowAll。
- 保存成功：toast 提示，并按 6.1-4 处理 Agent 重建。

## 8. 接口设计

### 8.1 请求约定与运行时配置

- **请求体两条硬规矩**（缺一即失败，且失败是静默的）：
  1. 一律经 `AgentApiJson.WrapPayload` 包装成 `{"payload":"<内层 JSON 字符串>"}`，与现有客户端（`LubanAgentCore/Client/AgentServerClient.cs:230`）一致；直接发业务 JSON 对象会被 `Dictionary<string,string>` 反解失败。
  2. `Content-Type` 必须是 `application/json; charset=utf-8`。SAEA.MVC 只在 `ContentType` 含 `application/json; charset=UTF-8` 子串时才绑定 JSON 到动作参数，浏览器默认发的 `application/json` 会让 `payload` 静默为空。前端 `api/client.ts` 统一写死该头，不逐处传。
- 响应壳统一 `{success, code, message, data}`（`ApiEnvelope`，camelCase）；`success=false` 时按 `code` 分流 400/403/404/503。
- `Program.cs` 启动时在**输出目录的 `wwwroot`**（即静态根）下生成 `runtime-config.js`：

```js
window.__LUBAN__ = { token: "<--token 值，未启用时为空>", userId: "<--user>", port: 1234 };
```

- 页面在 `index.html` 中以 `<script src="/runtime-config.js"></script>` 引入；文件缺失时前端回落为「无令牌」并在设置页提示（便于 `vite dev`）。
- 前端 `fetch` 包装统一补 `Authorization: Bearer <token>`（令牌为空则不补）。
- SSE 用 `fetch` + `ReadableStream` 解析，不用 `EventSource`（需要自定义头与 POST）；帧格式 `event: {name}\ndata: {json}\n\n`，`: ping` 注释行直接丢弃。
- **前端产物更新必须重启进程**：`IsStaticsCached=True` 时静态文件内容在首次请求后常驻内存，改盘上同名文件不生效（含 `index.html`）。开发调试要么重启，要么把 `IsStaticsCached` 设为 false。

### 8.2 新增接口

统一沿用 `payload` 单字段请求体与 `ApiEnvelope` 响应壳。

**系统**

| 路由 | 方法 | 说明 |
|---|---|---|
| `/system/bootstrap` | GET | 首屏一次拿全：用户、工作区列表与当前工作区、供应商与可选模型、当前模型选择、权限模式清单、推理强度清单、附件策略、确认超时、版本号 |
| `/system/commands` | GET | 斜杠命令元数据（名称、别名、描述、分类、用法、前端动作标识） |

`bootstrap` 字段口径：

- `workspaces`：`{workspaceId, name, rootPath, type, lastActiveAt, isAuthorized}`（对齐 `WorkspaceController.ToDto`）；`currentWorkspaceId` 来自 `IWorkspaceManager.CurrentWorkspace`。
- `providers`：`{id, displayName, models[]}`，**不含 `ApiKey`**（无论如何都不下发）；`selection`：`{providerId, modelId}`（`IProviderModelManagementService.GetSelection`）。
- `permissionModes`：`[{key, displayName}]`，key 取 `PermissionModeRules.ToKey`，文案取 `ToDisplayName`。
- `reasoningEfforts`：`["None","Low","Medium","High"]`（`Enum.GetNames<ReasoningEffort>`），前端另加「跟随模型」= 不下发。
- `attachments`：`extensions`（42 项，带前导点，如 `.png`，来自 `AttachmentService.SupportedExtensions`）、`maxCount`（5）、`undecodableExtensions`（`.heic` / `.heif`）、`maxTextBytes`（10485760）、`maxImageBytes`（5242880）、`maxImageEdge`（2000）。
- `confirmation`：`{timeoutSeconds}`，取自 `IToolConfirmationBroker.TimeoutSeconds`。

**附件**

| 路由 | 方法 | 说明 |
|---|---|---|
| `/attachment/upload` | POST | 包装后的 `{sessionId?, files:[{fileName, contentBase64}]}`；返回 `{ accepted: [{path,fileName,mediaType,size}], rejected: [{fileName,reason}] }` |

为什么不用 multipart：SAEA 的 multipart 解析随体积超线性变慢（探针实测 1MB≈4s、3MB≈27s、5MB≈77s），浏览器上传一张截图就要等半分钟；同一文件走 JSON 只用 2s。因此上传走标准的 `payload` 包装 + base64，代价是请求体约为文件体积的 1.37 倍（base64 4/3 × JSON 转义），单文件 10MB 时请求体 ≈14MB（探针实测 2.1s，成功）。

规则：
- 落盘目录 = 会话所属工作区（传了 `sessionId` 时先经 `ISessionWorkspaceQuery.GetSessionWorkspaceIdAsync` 取 `DbSession.WorkspaceId`）的 `.luban-agent/temp/uploads/<yyyyMMdd>/`；未传 `sessionId` 时用当前工作区。该目录已在 `.gitignore` 的 `/.luban-agent` 下，不会进提交。
- 文件名去掉路径分隔符与非法字符，加随机前缀防重名；只写上述固定目录，不接受调用方指定路径。
- 扩展名不在 42 项白名单、HEIC/HEIF、文本类 >10MB 的一律进 `rejected` 并附中文原因，不影响同批其他文件；图片不按大小拒绝（>5MB 或 >2000px 由发送时的 `DefaultAttachmentProcessor` 缩放）。
- 单次请求文件数上限 5（与 `MaxAttachmentsPerMessage` 一致），超出部分直接 `rejected`，不做静默截断。
- 上传只回传**绝对路径**；发送时把它塞进 `AgentChatRequest.Attachments`。

**实现约束（对本节所有新增动作生效）**：每个动作必须是 `Task<ActionResult> Xxx(string? payload) => WebApi.RunAsync(async () => { if (await WebApi.GuardAsync() is { } denied) return denied; ... })`——SAEA 未挂 `m_OnException`，动作内抛异常会直接掐断连接（`Hosting/WebApi.cs` 类注释）。

**会话**

| 路由 | 方法 | 说明 |
|---|---|---|
| `/session/listDetailed` | GET | 会话摘要列表：`sessionId`、`title`、`createdAt`、`updatedAt`、`messageCount`、`totalTokens`、`workspaceId`、`provider`、`modelName` |

实现取 `SessionRepository.GetUserSessionsAsync`（返回 `List<DbSession>`，已含 `WorkspaceId`），不动框架 `SessionInfo`。

**设置**

列表类接口都只返回文件型条目（与桌面端设置页一致），作用域参数统一为 `scope`：`global` 或 `workspaceId`；服务端把 `workspaceId` 解析成根目录，`global` 传 `null` 给管理服务。

| 路由 | 方法 | 说明 |
|---|---|---|
| `/skill/list`、`/skill/read`、`/skill/save`、`/skill/delete` | GET/POST | 技能（文件型；无启停） |
| `/rule/list`、`/rule/read`、`/rule/save`、`/rule/delete` | GET/POST | 规则（文件型；无启停） |
| `/mcp/list`、`/mcp/read`、`/mcp/save`、`/mcp/delete` | GET/POST | MCP（文件型；无启停、无连接测试） |
| `/provider/list`、`/provider/save`、`/provider/delete` | GET/POST | 供应商（API Key 掩码） |
| `/model/list`、`/model/add`、`/model/rename`、`/model/delete` | GET/POST | 模型（刷新与设为当前复用 `/provider/models`、`/provider/select`，不重复定义） |
| `/a2a/getServer`、`/a2a/saveServer`、`/a2a/listClients`、`/a2a/getClient`、`/a2a/saveClient`、`/a2a/deleteClient` | GET/POST | A2A（`scope` 只接受 `workspaceId`；`/a2a/status` 已存在） |

**命令映射**

`/system/commands` 在框架 `CommandCatalog.All`（16 条）基础上，过滤掉 `SlashCommandClient.Cli` 专属（`/agi`、`/init`）与网页无意义的 `/exit`，剩 **13 条**，并为每条补一个前端动作标识：

| 命令 | 网页动作 |
|---|---|
| `/help` | 打开帮助面板 |
| `/clear` | `POST /session/clearMessages` |
| `/mode` | 切换本轮权限模式（本地状态，随下一轮下发给 `/agent/chat`） |
| `/stats` | 打开会话统计浮层（`GET /session/stats`） |
| `/session` | 跳会话列表 |
| `/provider`、`/model`、`/skill`、`/rule`、`/mcp`、`/work`、`/a2a` | 跳设置页对应页签；`/work` 跳会话列表并聚焦工作区 chips |
| `/llmwiki` | 跳 LLM Wiki 浮层入口（本版只列出已有知识库与状态，不做完整管理页） |

动作标识由后端下发（形如 `openSettings:provider`、`action:clearMessages`），前端只认识少量动词，避免再次出现「命令名 → 行为」的第二份真相。

### 8.3 复用的既有接口

- 对话：`/agent/chat`、`/agent/confirm`、`/agent/cancel`、`/agent/reset`
- 会话：`/session/list`、`/session/get`、`/session/messages`、`/session/create`、`/session/rename`、`/session/delete`、`/session/clearMessages`、`/session/switch`、`/session/stats`
- 工作区：`/workspace/list`、`/workspace/current`、`/workspace/switch`、`/workspace/create`
- 其余：`/llmwiki/*`（本版只在命令面板保留入口，不做专门页面）、`/a2a/status`、`/system/health`

## 9. 前端工程

```
LubanAgentWebHost/
  web/
    package.json           # solid-js / @solidjs/router / marked / dompurify；dev: vite / vite-plugin-solid / typescript
    vite.config.ts         # base: './', build.outDir: '../wwwroot', emptyOutDir: true
    tsconfig.json
    index.html             # 引入 /runtime-config.js
    src/
      main.tsx  app.tsx  routes.tsx
      api/client.ts        # fetch 包装：令牌、payload 包装、信封解包、错误动画
      api/sse.ts           # POST + ReadableStream 帧解析
      api/types.ts
      stores/bootstrap.ts  sessions.ts  chat.ts  settings.ts
      pages/SessionList.tsx  Chat.tsx  Settings.tsx
      components/AppShell.tsx  SessionRow.tsx  Composer.tsx  AttachmentChips.tsx
                 CommandPalette.tsx  MessageBubble.tsx  ToolCard.tsx
                 ConfirmDialog.tsx  SettingsNav.tsx  Field.tsx  Toast.tsx
      styles/tokens.css  app.css
  wwwroot/                 # 构建产物，随代码提交
```

- 依赖只保留必要项：不引入 UI 组件库、不引入状态管理库（Solid 自带 store）。
- Markdown 渲染用 `marked` + `dompurify`（助手输出可能含 HTML）。
- `vite.config.ts` 关键项：`base: './'`、`build.outDir: '../wwwroot'`、`build.emptyOutDir: true`、`build.target: 'es2022'`。
  - `base` 用相对路径，配合 hash 路由（文档 URL 的 path 始终是 `/`，相对路径不会失效）。
  - **不要产出 `.woff2` / `.json` / `.webmanifest` / `.mjs`**：SAEA 的 MIME 表没有这些扩展名，会回 `unknown/unknown`（探针实测）。因此不引入自定义 Web 字体（§7.1 已定系统字体栈），运行期配置继续用 `runtime-config.js` 而不是 JSON 文件，也不做 PWA manifest。
  - 产物只有 `.js` / `.css` / 图片：`.js` 被标成 `application/x-javascript`、`.css` 为 `text/css`，浏览器按脚本 / 样式表加载均正常（模块脚本接受该历史 JS MIME）。
- `LubanAgentWebHost.csproj` 增加 `wwwroot` 的输出拷贝（本项目是 `Microsoft.NET.Sdk` 控制台工程，`wwwroot` 默认不参与发布）：
  ```xml
  <Content Include="wwwroot\**" CopyToOutputDirectory="PreserveNewest" />
  ```
  否则运行时静态根（`AppContext.BaseDirectory/wwwroot`）里没有产物。
- `vite dev` 代理：`server.proxy` 把 `/agent`、`/session`、`/workspace`、`/system`、`/attachment`、`/skill`、`/rule`、`/mcp`、`/provider`、`/model`、`/a2a`、`/llmwiki` 转发到 WebHost 实际端口（端口由 `--port` / `LubanAgentWeb:Port` 决定，代理目标用环境变量 `VITE_HOST_PORT`，默认 8080）。代理只匹配这些前缀，静态资源仍由 Vite 提供。
- `.gitignore`：增加 `web/node_modules/`、`web/dist/`、`wwwroot/runtime-config.js`；`wwwroot` 其余产物不忽略。
- README 中英各补一节「浏览器端」，写明 `npm install && npm run build`、产物随代码提交、开发模式代理与 `runtime-config.js` 缺失时的行为。

## 10. 安全与隐私

- 只监听本机回环，`GuardAsync` 的来源校验保持不动。
- 令牌写入 `wwwroot/runtime-config.js`，该文件随进程启动生成、不提交（加入 `.gitignore`）。
- API Key 读接口一律掩码；日志与错误文案不得回显密钥（沿用 `WebApi.SafeErrorMessage`）。
- **存储基线不变**：`ProviderConfig.ApiKey` 与集成 PAT 仍是 `config.json` 明文（与桌面端同基线）；本设计只保证网页不回显、不写日志，不引入新加密层，也不迁移既有数据。
- 上传走 base64 JSON：文件字节会经浏览器内存与服务端内存各过一遍（与桌面端选择本地文件的差别仅此一处）；落盘只写工作区固定子目录（`.luban-agent/temp/uploads/<yyyyMMdd>/`，已随 `/.luban-agent` 忽略），拒绝 `..`、绝对路径文件名、白名单外扩展名。
- `bootstrap` / 供应商列表接口**从不**下发 `ApiKey` 原值；只有掩码（前 4 + 后 4）与是否已配置。
- 上传暂存文件不自动清理（本版不做定时清理，避免扩面）；同一工作区多次上传会累积在 `.luban-agent/temp/uploads/` 下。
- 助手输出的 Markdown 经 `dompurify` 清洗后再插入 DOM。
- 不提供任意命令执行入口（`+` 菜单不出现 Shell 命令）。

## 11. 风险

| 风险 | 影响 | 处置 |
|---|---|---|
| 上传走 base64 JSON：请求体约为文件的 1.37 倍，服务端同时持有 base64 字符串与解码后字节 | 10MB 文本文件峰值约 24MB（探针实测请求体 13.98MB、耗时 2.1s）；5×10MB 极端情况约 120MB | 单文件按 10MB 上限拒绝；单次请求最多 5 个；前端预检大小并逐个上传而不是一次打包多个 |
| 静态文件内容首请求后常驻内存 | 重新构建前端后页面仍是旧版，容易被误判为「构建没生效」 | 交付说明写明「换产物必须重启进程」；开发期可把 `IsStaticsCached` 设为 false |
| SAEA MIME 表缺 `woff2`/`json`/`webmanifest`/`mjs` | 资源回 `unknown/unknown`，字体可能加载失败 | 不用自定义字体与 PWA manifest；产物只保留 js/css/图片 |
| 请求体漏带 `; charset=utf-8` | `payload` 静默为空，表现为 400「请求体不是合法的 JSON」或无参数错误 | `api/client.ts` 统一写死头；首屏 `/system/bootstrap` 做一次自检并在失败时提示 |
| 落库没有工具结果，只有工具名与参数 | 历史回放无法还原工具输出，容易被误设计成「有结果」 | 回放卡片标为「历史记录」，只展示名称与参数；实时回合才展示 `resultSummary` |
| `runtime-config.js` 明文存放令牌 | 同机其他进程可读 | 仅回环 + 文件不提交；文档中标注 |
| 配置写后缓存 Agent 未刷新 | 改完设置行为不变 | 写后 `/agent/reset`；`AgentHost.Reset` 对运行中的回合只打延迟重载标记，不会打断当前回合 |
| 网页录入 API Key 使其经浏览器内存过一遍 | 与桌面端同基线，无新增落盘风险 | 仅回环 + 掩码回显 + 不写日志；不做「加密存储」承诺 |
| SSE 无断点续传 | 断线丢增量 | 断线后重载 `/session/messages` 并提示 |
| 助手输出含 HTML | XSS | `dompurify` 清洗，禁用脚本与内联事件属性 |
| 前端产物提交 | diff 噪声 | 单独提交构建产物，PR 说明 |
| 设置页 6 页签工作量大 | 交付周期长 | 按 6.1-5 分期，P1 先可用 |

## 12. 验收标准

1. `dotnet build LubanAgent.slnx` 0 错误；`dotnet test LubanAgent.Tests` 全绿；`npm run build` 成功。
2. 启动 WebHost 后访问 `http://127.0.0.1:<port>/` **直接返回页面**（`Root="wwwroot"` 生效，不再是 404），首屏为会话列表，能看到今日与更早分组，并标注所属工作区；刷新任意 `#/...` 地址仍是同一页面。
3. 新建会话 → 发一句话 → 正文流式出现；工具调用展示开始/完成；触发写操作时弹出确认框，四个按钮行为正确；不操作时按倒计时超时并自动按拒绝处理。
4. 拖入 6 个文件时第 6 个被拒并提示；粘贴截图可入队；上传 10MB 文本文件成功（请求体 ≈14MB、耗时可接受）；PDF 与 HEIC 被拒且有明确文案。
5. 输入 `/` 出命令列表（13 条，无 `/agi`、`/init`、`/exit`）；`/clear` 清空当前会话；`/model` 跳设置页模型页签。
6. 设置页 6 个页签可读可写；技能 / 规则 / MCP 在全局与当前工作区之间切换后列表内容随之变化，A2A 在全局下为空并给出提示；供应商列表的 API Key 显示为掩码，留空保存不改变原值。
7. 页面未出现插件、连接器、上下文 @、Shell 命令入口。
8. `--token` 启动时，页面自身 API 调用全部带令牌且成功；未带令牌的第三方请求仍被 401 拒绝。
9. 重新执行 `npm run build` 并**重启进程**后页面内容更新；不重启时保持旧内容（与静态缓存行为一致，属预期）。

## 13. 留给构建者

- 组件拆分粒度与具体样式数值（在 `tokens.css` 内集中定义后自行取值）。
- 错误文案的具体措辞（需中文、可操作、不暴露本机路径）。
- 命令面板的分组顺序与图标。
- 表单校验的触发时机（失焦 / 提交时）。
- 帮助面板的内容组织。