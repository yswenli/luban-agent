# LuBan Agent Codex

**基于 Avalonia UI 的跨平台 AI 编码代理桌面客户端**

*让大模型具备思考、规划、调用工具和自主执行的能力*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](../LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12.1.6-blue.svg)](https://avaloniaui.net/)

[English](README.en.md) | 中文

---

## 🌟 为什么选择 LuBan Agent Codex？

想象一下：你只需在图形界面中输入"帮我查一下D盘下面有哪些目录"，AI 就能自动调用文件系统工具，列出所有目录。再说一句"帮我打开百度并搜索 LuBan Framework"，AI 就会自动启动浏览器，完成搜索。

这不是科幻，这就是 **LuBan Agent Codex**。

### 你是否遇到过这些问题？

- 😫 想让 LLM 调用工具完成任务，但 MCP / Function Calling 的实现细节令人头疼？
- 😤 Skill 管理、工具注册、会话持久化各自需要单独实现，维护成本高？
- 😖 模型 Provider 切换困难——从 Provider A 换到 Provider B 需要重写大量代码？
- 😩 缺少图形界面——命令行工具不够直观，难以管理复杂任务？

**LuBan Agent Codex 为你提供完整的 AI Agent 桌面体验**，从 Agent 运行时、多模型路由、技能系统、工具系统、会话存储到图形界面——开箱即用。

---

## ✨ 核心特性

### 🎨 现代化图形界面
- **双栏工作布局**：左侧边栏（工作区 / 会话 / 知识库 / 插件入口）+ 右侧对话区（消息流 + 输入框）；底部状态栏显示权限模式、工作目录、Git 分支、运行状态与 Token 用量
- **暗色主题**：专业的暗色配色方案，长时间编码不疲劳
- **流式输出**：实时显示 AI 思考过程和工具调用状态
- **智能滚动**：自动跟随输出，支持手动滚动查看历史

### 🤖 多模型路由
- **20+ 种 AI Provider 支持**：OpenAI、Azure、DeepSeek、Kimi、GLM、通义千问、豆包、Claude、Gemini、Ollama、MiniMax、字节方舟、阿里百炼、腾讯混元、小米 MiMo、百度文心一言(ERNIE)、xAI Grok、百度智能云千帆、腾讯云 TI 平台、华为云盘古、AWS Bedrock、OpenRouter，以及自定义 OpenAI 兼容 API
- **统一 `provider:model` 格式**：一键切换模型，无需修改代码
- **动态路由**：LuBanChatClient 根据前缀自动分发到对应 Provider

### 🛠️ 12 大内置工具组
| 工具组 | 能力 |
|--------|------|
| 🌐 **浏览器工具** | 导航、点击、输入、截图、获取内容（基于 Playwright） |
| 📁 **文件系统工具** | 读取、写入、编辑（单处/多处/全部替换）、列出目录、搜索、复制、移动、删除；工作区内直接放行，工作区外经确认 |
| 🌿 **Git 工具** | 状态、差异、日志、分支、暂存、提交、合并、变基、推送等 26 个细粒度工具 |
| 🔧 **脚本执行工具** | 执行 Shell、Lua、Python 脚本 |
| 🌍 **Web 工具** | 发送 HTTP 请求获取网页内容 |
| 🔍 **语义检索工具** | 索引本地代码/文档，按语义搜索相关片段 |
| 🗜️ **上下文压缩工具** | 压缩当前会话的对话历史，释放 token 预算（LLM 可主动调用） |
| 🧠 **本地记忆工具** | 长期记忆的存储、查询和管理 |
| 🔌 **MCP 外部工具** | 接入外部 MCP 服务器，将其工具并入 Agent 工具集 |
| 🧩 **编排工具** | 复合任务拆解为 DAG，调度 SubAgent 并行/串行执行 |
| 📚 **Wiki 知识库工具** | 读写/删除 wiki 页面、向量搜索、健康检查（opt-in，需显式启用） |
| 📜 **大日志分析工具** | 大日志文件聚合概览、正则检索、按行切片，恒定内存（默认启用） |

> **编排说明**：Git 工具默认不进入编排子代理的工具组，如需让子代理执行 Git 操作，请在 DAG 节点中显式指定 `toolGroups`。

### 🎯 Skill 系统
内置九大核心技能，即插即用：
- **头脑风暴**：实现功能前探索需求和设计
- **代码审查**：审查代码、发现问题、提供改进建议
- **文档生成**：生成代码注释、README、API 文档
- **代码重构**：重构代码，提升代码质量
- **测试生成**：自动生成单元测试
- **代码解释**：解释复杂代码逻辑
- **调试助手**：辅助调试问题
- **Git 提交**：生成规范的 Git 提交信息
- **技能发现**：自动发现和推荐合适的技能

**文件化 Skill**：支持通过 SKILL.md 文件定义自定义 Skill，兼容 OpenCode 格式，放置于项目级或用户级目录即可自动加载。

### 📎 附件支持（图片 + 文本文件）
- **多入口添加**：📎 按钮选择文件、拖拽文件到窗口、`Ctrl+V` 粘贴剪贴板文件或截屏位图
- **缩略图预览**：附件条与消息区渲染缩略图、文件名与大小，点击可查看大图（Esc / 点击关闭）
- **体积分级**：每消息最多 5 个；文本 ≤50KB 内联，>50KB 仅注入路径指引由模型按需读取
- **模型能力感知**：非 vision 模型下 📎 常亮，加入图片时拦截并提示；达 5 个上限自动禁用
- **异步处理**：附件在后台线程预处理，大图片不冻结界面；处理失败的附件单独丢弃并提示

### 🛡️ 安全与规则引擎
- **路径访问规则**：工作区内路径直接放行，工作区外路径经确认放行
- **危险操作确认**：工作区外访问、写入、删除、执行脚本前要求用户确认，支持本次/本轮/本会话三档授权
- **自定义规则**：支持通配符匹配，灵活控制工具行为

### 💾 会话持久化
- 对话历史自动保存到 SQLite 数据库
- 支持长对话压缩（SummarizingChatReducer），上下文永不丢失
- 会话统计、Token 计数一目了然

### 🔌 MCP 协议支持
- 内置文件系统 MCP 客户端
- 支持外部 MCP 服务器热加载
- 标准 JSON-RPC 协议，无缝对接生态

### 🧩 多 Agent 任务编排
- **复合任务自动拆解**：主 Agent 识别复杂任务后自动拆解为 DAG 任务图谱
- **串行/并行混合编排**：基于拓扑分层，同层节点并行执行，跨层串行执行
- **SubAgent 调度**：每个 DAG 节点由独立 SubAgent 执行，支持工具组隔离
- **上下文传递**：节点间通过 `{dep:xxx}` 占位符引用前驱输出
- **记忆上下文注入**：编排入口统一构建工作区长期记忆与规则上下文，随每个 SubAgent 的系统提示词一并下发
- **错误处理**：关键节点失败跳过后继，非关键节点失败继续执行
- **启发式预过滤**：仅当输入长度落在 `[MinLength, MaxLength]` 且命中复合关键词时才进入任务拆解，节省 LLM 调用，并避免普通长问题被误判为复合任务

### 📂 工作区与知识库
- **工作区隔离**：每个工作区拥有独立的根目录、会话历史和配置目录
- **工作区 ID 稳定**：工作区 ID 由归一化后的根路径派生，CLI 与 Codex 在同一目录得到同一 ID，进而跨宿主共享长期记忆
- **工作区配置目录**：每个工作区根目录下自动创建 `.luban-agent/`，可放置自定义 `skills`、`rules`、`mcps` 配置，以及连接器启用文件 `connectors/<name>.json`
- **临时文件管理**：运行时生成的脚本、截图、中间文件统一存放于 `.luban-agent/temp/`，支持自动清理过期文件
- **LLM Wiki 知识库**：特殊工作区类型，支持文件索引与语义检索，自动检索增强问答
- **向量存储隔离**：不同工作区的索引数据完全隔离，互不串读
- **路径授权管理**：工作区内路径直接放行；工作区外路径经用户确认后放行，授权按路径所在根记忆（本次/本轮/本会话三档）

---

## 🚀 快速开始

### 1. 准备 Playwright 浏览器（使用浏览器工具前必须完成）

首次调用浏览器工具时，应用按 `Microsoft.Playwright` 包声明版本自动把 Chromium 驱动安装到 `~/.luban-agent/playwright-driver/{version}`，成功时无需手工操作。

```powershell
# 仅在自动安装被网络阻断时手工兜底：版本号取应用提示（PlaywrightDriverResolver.GetInstallBrowsersHint）给出的值
npx playwright@<版本> install chromium
```

> **注意**：浏览器版本必须与 `Microsoft.Playwright` 包版本一致，不要手工钉住固定版本号；具体版本以 csproj 声明为准。

### 2. 克隆源码运行

```bash
# 克隆仓库
git clone https://github.com/yswenli/luban-agent.git
cd luban-agent

# 运行程序
dotnet run --project LubanAgentCodex/LubanAgentCodex.csproj
```

### 3. 进入工作区

启动先显示闪屏，随后自动恢复**最近活跃的普通工作区**（LLM Wiki 知识库不参与自动恢复）；若从未创建过工作区，则用当前进程工作目录自动创建一个普通工作区，并授权、设为当前工作区。

```
闪屏：正在加载应用配置… → 正在初始化核心服务… → 正在准备知识库与工作区…
→ 正在构建主界面… → 主窗口
```

进入后可在侧边栏切换工作区与会话；需要新建、切换、删除、授权时输入 `/work` 打开「工作区管理」窗口。

### 4. 配置你的第一个 AI Provider

输入 `/provider` 打开设置中心的「供应商」页（配置写入全局 `config.json`，不分工作区）：

1. 点击左下「＋ 新建」生成供应商草稿；
2. 在「类型（内置预设，可选）」中选择预设（内置 15 个厂商 + Custom，共 16 项），选中后自动预填 `Name` 与 `BaseUrl`；
3. 填写 `ApiKey`（必填），按需修改 `BaseUrl`、增删自定义模型；
4. 点击「保存」；如需立即生效再点「应用配置」，否则在下次切换工作区或重启后生效。

侧边栏「⚙ 设置」与 `/provider` 打开的是同一个设置中心。

### 5. 选择模型并开始对话

```
> /model -list
可用模型:
  kimi:k3 (当前)
  kimi:k3-256k
  ...

> /model -switch kimi:k3
已切换模型: kimi:k3
```

`/model` 只支持 `-list`（缺省）与 `-switch <provider:model>`；供应商与模型的增删改在设置中心「模型」页完成。输入框工具栏右侧的模型下拉框也可直接切换当前模型。

### 6. 开始你的第一次 Agent 对话

直接在输入框输入问题，按 Enter 发送：

```
你: 帮我查一下D盘下面有哪些目录

🤖 思考中...
⚙️ 调用工具: ListDirectoryAsync
   参数: path = D:\
✓ 工具执行完成

🤖 D盘下有以下目录：
1. Program Files
2. Users  
3. Windows
...
```

---

## 📖 命令一览

LuBan Agent Codex 提供了简洁而强大的命令系统，所有命令均以 `/` 开头：

| 命令 | 简写 | 说明 |
|------|------|------|
| `/help` | — | 显示命令与快捷键（内容取自命令清单） |
| `/clear` | — | 清空当前消息流 |
| `/mode [name]` | — | 查看或切换权限模式（default/plan/accept-edits/bypass） |
| `/exit` | `/quit` | 退出程序 |
| `/provider` | `/p` | 打开设置中心「供应商」页 |
| `/model` | `/m` | 列出模型，或用 `-switch <provider:model>` 切换 |
| `/skill` | `/sk` | 打开设置中心「技能」页（查看 / 新建 / 编辑 / 删除 Skill） |
| `/rule` | `/r` | 打开设置中心「规则」页 |
| `/mcp` | `/mp` | 打开设置中心「MCP 服务」页 |
| `/session` | `/se` | 会话列表 / 新建 / 清空 |
| `/stats` | `/st` | 查看统计（消息数量、权限模式、当前会话） |
| `/work` | `/w` | 打开「工作区管理」窗口 |
| `/llmwiki` | `/lw`, `/rag`, `/rg`, `/wiki`, `/wk` | 打开「LLM Wiki 知识库管理」窗口 |
| `/a2a` | — | A2A 协作管理（`-list` / `-server` / `-client` / `-start` / `-stop`） |

> `/agi`、`/init` 仅在 CLI 端提供，Codex 不可用。

### 子命令简写

GUI 与 CLI 共用同一套简写展开（`SlashCommandAliases.Expand`），但 Codex 端只有 `/model`、`/session`、`/a2a` 真正解析子命令，其余管理命令忽略参数、直接打开对应界面。

| 简写 | 展开 | 说明 |
|------|------|------|
| `-l` | `-list` | 列表 |
| `-a` | `-add` | 新增 |
| `-u` | `-update` | 更新 |
| `-d` | `-delete` | 删除 |
| `-s` | `-switch` | 切换；`/a2a` 中展开为 `-server`，`/llmwiki` 中展开为 `-search` |
| `-n` | `-new` | 新建 |
| `-c` | `-clear` | 清空；`/a2a` 中展开为 `-client` |
| `-i` | `-index` | 仅 `/llmwiki` 展开 |
| `-t` | `-tools` | 工具列表（Codex 端无命令解析该子命令） |

**示例**：
- `/m -l` = `/model -list`
- `/m -s kimi:k3` = `/model -switch kimi:k3`
- `/se -n 项目讨论` = `/session -new 项目讨论`
- `/a2a -s` = `/a2a -server`（不是 `-switch`）

---

## 🎨 使用场景

### 场景一：文件管理助手

```
你: 帮我列出 src 目录下所有 .cs 文件并统计代码行数

⚙️ 调用工具: ListDirectoryAsync
   参数: path = src
⚙️ 调用工具: ReadFileAsync
   参数: path = src\Program.cs
...

🤖 src 目录下共有 15 个 .cs 文件，总计 3,456 行代码：
- Program.cs: 112 行
- Services\ConsoleAppService.cs: 344 行
...
```

### 场景二：网页自动化

```
你: 帮我打开百度并搜索 "LuBan Framework"

⚙️ 调用工具: NavigateAsync
   参数: url = https://www.baidu.com
⚙️ 调用工具: TypeTextAsync
   参数: selector = #kw, text = LuBan Framework
⚙️ 调用工具: ClickAsync
   参数: selector = #su

🤖 已完成搜索，当前页面标题: LuBan Framework_百度搜索
```

### 场景三：技能驱动的代码审查

`/skill` 只负责打开设置中心的「技能」页（查看 / 新建 / 编辑 / 删除 `SKILL.md`），不接收待审查代码。审查由 Agent 自动完成：模型先用 `ListSkillsAsync` 查找技能，再用 `UseSkillAsync` 加载技能指令模板。

```
你: 用代码审查技能看看这段代码：public void Test() { var x = 1/0; }

⚙️ 调用工具: ListSkillsAsync
   参数: keyword = code
⚙️ 调用工具: UseSkillAsync
   参数: id = code-review
✓ 工具执行完成

🤖 📋 代码分析：
1. ⚠️ 第 1 行存在除以零的风险
2. 💡 建议补充异常处理
...
```

### 场景四：上下文压缩

```
你: 帮我压缩一下当前的对话上下文

⚙️ 调用工具: CompactContextAsync
✓ 工具执行完成

🤖 已压缩上下文：归档 12 条消息，保留 8 条活跃消息
```

### 场景五：复合任务自动编排

模型判断输入为复合任务时，会先调用 `plan_task` 生成任务图谱，再调用 `run_orchestration` 执行 DAG 编排：

```
你: 调研 LuBan-Framework 和 Luban-Agent 两个项目，对比它们的优缺点，生成一份对比报告

💭 思考过程:
这是一个复合任务，需要拆解为多个子任务并行执行...

🔄 开始规划任务图谱...
✓ 规划完成: 已生成 4 个节点的任务图谱

▶ 开始执行节点: research-luban
  [SubAgent] 调用 FetchUrlAsync 抓取 LuBan 框架文档...
✓ 节点完成: research-luban

▶ 开始执行节点: research-langchain
  [SubAgent] 调用 FetchUrlAsync 抓取 LangChain 框架文档...
✓ 节点完成: research-langchain
── 第 1 层执行完成（并行） ──

▶ 开始执行节点: compare
  [SubAgent] 基于前驱结果进行对比分析...
✓ 节点完成: compare

🎯 编排完成

🤖 对比报告已生成：

## LuBan vs LangChain 对比

### 架构设计
- **LuBan**: 基于 .NET 生态，深度集成 Microsoft.Extensions.AI...
- **LangChain**: Python 生态，社区活跃...

### 工具集成
...

### 推荐场景
...
```

### 场景六：工作区管理

输入 `/work` 打开「工作区管理」窗口。列表每行显示类型图标（📁 普通 / 📚 LLM Wiki）、名称、根目录、`✓ 当前` 标记与授权状态，工具栏提供：

| 按钮 | 作用 |
|------|------|
| `+ 新建工作区` | 选择目录并创建普通工作区 |
| `切换` | 把选中工作区设为当前工作区 |
| `删除` | 删除选中工作区（级联删除其会话与索引，需确认） |
| `授权` | 为选中工作区补齐路径授权（未授权时自动切换过去） |

```
> /work
（打开「工作区管理」窗口）
```

切换后侧边栏的会话列表与页脚的工作目录会同步刷新。

### 场景七：LLM Wiki 知识库问答

输入 `/llmwiki` 打开「LLM Wiki 知识库管理」窗口。列表行显示 📚 名称、路径、索引状态（如「已索引 25 文件 · 142 切块」）与 `✓ 当前` 标记，工具栏提供：

| 按钮 | 作用 |
|------|------|
| `+ 创建知识库` | 指定名称与目录创建 LLM Wiki 类型工作区 |
| `索引` | 为选中知识库建立索引，先弹出「索引文件匹配模式」（留空索引全部文件） |
| `搜索` | 弹出「搜索查询」输入框，结果替换为「搜索结果 · N 条」视图，可「← 返回列表」 |
| `删除` | 删除选中知识库（级联删除会话与索引，需确认） |
| `重建 Wiki 索引` | 重建选中知识库的 Wiki 页面向量索引 |
| `Wiki Lint` | 检查 Wiki 页面健康度并给出报告 |

```
> /llmwiki
（打开「LLM Wiki 知识库管理」窗口）
```

在「工作区管理」窗口或用侧边栏切到该知识库工作区后，直接在输入框提问即可（自动检索增强）：

```
你: 如何创建工作区？
🤖 根据知识库文档，创建工作区使用 /work 打开「工作区管理」窗口后点「+ 新建工作区」...
```

### 场景八：安全地读取工作区外的文件（三档授权）

默认只有工作区内的路径免确认放行。需要临时参考工作区之外的资料时，Agent 会弹出**模态确认框**，由你决定放开的范围：

```
你: 参考一下 D:\Shared\api-spec.md，帮我实现对应的客户端

┌─ 工具确认 ───────────────────────────────────────────┐
│ ⚠ 需要确认是否执行该工具                              │
│ ReadFileAsync                                         │
│ { "path": "D:\\Shared\\api-spec.md" }                 │
│   [✗ 拒绝] [本轮全部允许] [本会话全部允许] [✓ 允许]    │
└───────────────────────────────────────────────────────┘

（选择“本会话全部允许”）

消息流留痕：已允许 读取文件
✓ 已授权本会话访问 D:\Shared
📄 已读取 api-spec.md（216 行）
🤖 好的，我按接口约定生成客户端代码……
```

| 档位 | 授权范围 | 记忆方式 |
|------|----------|----------|
| `✓ 允许` | 仅本次调用 | 不记忆 |
| `本轮全部允许` | 本对话轮次内的同类工具 | 每轮重置后失效 |
| `本会话全部允许` | 整个会话进程内的同类工具 | 授权按路径所在根记忆（文件取父目录、目录取自身） |

> 授权根若落在盘符根（如 `D:\`）不会被记忆，避免一次授权放开整块磁盘。选择前主窗口不可交互，Esc 或终止会话等同“拒绝”。

### 场景九：Plan 模式——先读后写，只出计划不动手

在 Plan 模式下，Agent 只会**读取**上下文来产出计划，所有写操作都被记录为计划项、不会真正执行：

```
> /mode plan
✓ 权限模式已切换: 计划 (Plan)

你: 把 src/Logging 模块重构一下

🔍 [计划] 正在读取上下文...
   ReadFileAsync      src\Logging\Logger.cs    ✓ 只读，放行
   ListDirectoryAsync src\Logging\             ✓ 只读，放行
   WriteFileAsync     src\Logging\Logger.cs    ⏸ 已记录为计划项（Plan 模式不执行）

🤖 计划如下：
   1. 抽取 ILogger 接口
   2. 将文件写入改为异步批量 flush
   3. 补充单元测试
   确认后请切换到 default 模式再执行。
```

### 场景十：删除操作每次都问——授权无法“一劳永逸”

删除类工具（`DeleteFileAsync` / `DeleteDirectoryAsync` / `DeletePageAsync`）出于安全考虑**永远不记忆授权**，即使选“本轮/本会话全部允许”，下一次删除仍会弹出确认：

```
你: 清掉临时目录

┌─ 工具确认 ───────────────────────────────────────────┐
│ ⚠ 需要确认是否执行该工具                              │
│ DeleteDirectoryAsync                                  │
│ { "path": ".luban-agent\\temp" }                      │
│   [✗ 拒绝] [本轮全部允许] [本会话全部允许] [✓ 允许]    │
└───────────────────────────────────────────────────────┘

（选择“本轮全部允许”）

✓ 已删除 .luban-agent\temp
ℹ 删除类操作不记忆授权，下次删除仍会确认
```

### 场景十一：四种权限模式，一键循环

用 `/mode` 或 `Shift+Tab` 在四种模式间切换（Agent 运行中 `Shift+Tab` 会被拒绝并提示「Agent 运行中无法切换权限模式」，因为本轮已启动的工具确认语义不会改变）：

```
> /mode
当前权限模式: 默认 (Default)
可用模式: default / plan / accept-edits / bypass

> /mode accept-edits
✓ 权限模式已切换: 接受编辑 (AcceptEdits)
```

| 模式 | 行为 |
|------|------|
| `default` | 工作区/已授权范围内免确认；工作区外路径、删除类、脚本执行等经确认 |
| `plan` | 授权范围内的只读工具放行以产出计划；其余仅记录为计划项，不执行 |
| `accept-edits` | 授权范围内的编辑类操作（有路径、非删除）免确认；脚本与删除仍确认 |
| `bypass` | 跳过所有工具确认（切换时需二次确认，请谨慎使用） |

### 场景十二：接入外部 MCP 工具，能力随插随扩

除了内置工具，还可以通过设置中心的「MCP 服务」页（`/mcp` 直接定位）把外部 MCP 服务器的工具接进 Agent 工具集：

```
> /mcp
（打开设置中心 → 工作区配置 → MCP 服务）
```

1. 点击「＋ 新建」，填写表单：`Name`（必填，即文件名）、`Description`、`Transport`（stdio / http / sse）、`Command`（http/sse 时可将 URL 填在此处）、`Args`（一行一个）、`Enabled`；
2. 点击「保存」，再点「应用配置」；保存时管理服务会调用 `Reload()` 热加载，无需重启；
3. 在对话中直接提需求，Agent 会把该服务器暴露的工具（命名 `mcp_{client}_{tool}`）并入工具集：

```
你: 帮我在 luban-agent 仓库建一个 bug issue
⚙️ 调用工具: mcp_github_create_issue
   参数: repo = luban-agent, title = ...
✓ 工具执行完成
```

> 顶栏「作用域」可切换 `★ 全局` 或具体工作区，决定配置写入用户级还是工作区级 `.luban-agent/mcps/`。

---

## 🏗️ 项目结构

```
LubanAgentCodex/
├── App.axaml(.cs)              # 应用程序入口（DI 装配 + 插件 UI 分派器替换）
├── Program.cs                  # 主入口点
├── GlobalUsings.cs             # 全局 using
├── Assets/                     # 图标等静态资源
├── Converters/                 # 值转换器（文件大小、缩略图路径）
├── Styles/
│   └── Colors.axaml            # 主题配色资源
├── Plugins/
│   └── AvaloniaPluginUiDispatcher.cs  # IPluginUiDispatcher 的 Avalonia 实现（UI 线程封送）
├── Security/
│   ├── CodexYunxiaoCredentialProvider.cs  # 云效凭据提供者（走宿主凭据存储）
│   └── YunxiaoSettingsService.cs          # 云效组织 ID / PAT 读写
├── Services/
│   └── FooterDataProvider.cs   # 页脚数据提供者
├── ViewModels/
│   ├── MainWindowViewModel.cs  # 主窗口 ViewModel
│   ├── SessionViewModel.cs     # 会话 ViewModel（消费框架 AgentStreamEvent）
│   ├── Settings/               # 设置窗分页 ViewModel（无 Avalonia 依赖，可单测）
│   └── Messages/               # 消息数据模型
│       ├── MessageItemBase.cs
│       ├── UserMessageItem.cs
│       ├── AssistantMessageItem.cs
│       ├── ToolCallItem.cs
│       ├── ThinkingMessageItem.cs
│       ├── SystemMessageItem.cs
│       ├── OrchestrationNodeItem.cs
│       ├── NodeActivityDisplayItem.cs
│       └── BoundedMessageCollection.cs
└── Views/
    ├── MainWindow.axaml(.cs)   # 主窗口
    ├── SettingsWindow.axaml(.cs)  # 统一设置中心（窗口壳：状态装配 + 编辑器分发）
    ├── Settings/               # 设置窗 UI partial（导航 / 动作 / 编辑器）
    │   ├── SettingsTabKind.cs
    │   ├── SettingsWindow.Navigation.cs
    │   ├── SettingsWindow.Actions.cs
    │   ├── SettingsWindow.Editors.Workspace.cs
    │   └── SettingsWindow.Editors.Global.cs
    ├── PluginManageWindow       # 插件管理窗口
    ├── SkillManageWindow        # 技能管理窗口
    ├── RuleManageWindow         # 规则管理窗口
    ├── MCPManageWindow          # MCP 服务管理窗口
    ├── WorkManageWindow         # 工作区管理窗口
    ├── LlmWikiManageWindow      # LLM Wiki 管理窗口
    ├── RenameDialog / NewWorkspaceDialog / NodeDetailWindow
    │   / ImageViewerWindow / SplashWindow / InitAgentsMdDialog
    └── Controls/               # 自定义控件
        ├── Sidebar             # 左侧边栏
        ├── MessageStream       # 消息流
        ├── InputBox            # 输入框
        ├── FooterBar           # 页脚状态栏
        ├── BusyOverlay         # 忙碌遮罩
        ├── UserMessageView     # 用户消息
        ├── AssistantMessageView # AI 消息
        ├── ThinkingMessageView # 思考过程
        ├── ToolCallCard        # 工具调用卡片
        ├── OrchestrationNodeCard # 编排节点卡片
        └── SystemMessageView   # 系统消息
```

> 注：Agent 宿主（`IAgentHost` / `AgentHost`）与流式事件类型（`AgentStreamEvent`）由框架 `LuBan.AIAgent` 提供，
> 本工程只做 UI 消费；技能 / 规则 / MCP / 会话 / 工作区 / 插件 / 连接器的业务逻辑在 `LubanAgentCore.Services.Management`。

---

## ⚙️ 配置说明

### 应用配置 (appsettings.json)

```json
{
  "LuBanAgent": {
    "DefaultModel": "openai:gpt-4o",
    "SystemPrompt": "你是一个智能助手，可以帮助用户完成各种任务。",
    "MaxToolLoopIterations": 30,
    "Session": {
      "CompactTargetMessages": 50,
      "CompactThreshold": 10,
      "IdleUnloadMinutes": 30
    },
    "Orchestration": {
      "Enabled": true,
      "PlannerReasoningEffort": "None",
      "MaxNodes": 20,
      "DefaultNodeTimeoutSeconds": 300
    }
  }
}
```

### 用户配置 (%LocalAppData%\LuBan\AIAgent\config.json)

用户配置（Provider、自定义 Skill、规则、连接器声明等）自动保存在本地，重启后自动加载。

> 连接器（如云效 `yunxiao`）在 `config.json` 的 `connectors` 节声明可用性；插件清单里的 `mcpServers: ["yunxiao"]` 由宿主按连接器暴露面解析，PAT 只经凭据存储流转（键 `connector:yunxiao:token`，兼容旧键 `yunxiao_pat`），不会明文写入 `.luban-agent/mcps/*.json`。

---

## 🎯 高级功能

### 危险操作确认

对于敏感操作（工作区外路径访问、写入文件、执行脚本、删除数据等），系统会在执行前弹出**模态确认对话框**；工作区内路径直接放行：

```
你: 帮我写一个文件到 C:\temp\test.txt

┌─ 工具确认 ───────────────────────────────────────────┐
│ ⚠ 需要确认是否执行该工具                              │
│ WriteFileAsync                                        │
│ {                                                     │
│   "path": "C:\\temp\\test.txt",                        │
│   "content": "文件内容..."                             │
│ }                                                     │
│   [✗ 拒绝] [本轮全部允许] [本会话全部允许] [✓ 允许]    │
└───────────────────────────────────────────────────────┘

消息流留痕：已允许 写入文件
```

对话框为应用模态窗口：选择前主窗口不可交互，Esc 或终止会话等同「拒绝」。

**授权档位**：`✓ 允许` 仅放行本次；`本轮全部允许` 记入本轮（每轮重置）；`本会话全部允许` 记入本会话（进程存活期）。授权按路径所在根记忆（文件取父目录、目录取自身）；授权根落到盘符根（如 `D:\`）时不记忆。

**需要确认的操作包括**：
- 📝 **文件系统**: 工作区外路径的读取/写入/复制/移动，以及所有删除操作（删除类不记忆，每次必确认）
- 🔧 **脚本执行**: 执行 Shell、Lua、Python 脚本

工作区内的只读与常规编辑免确认；Plan 模式下仅工作区内的只读工具直接放行。

### 会话管理

```
# 创建并切换到新会话（标题可省略，默认「新会话」）
> /session -new 项目讨论

# 列出所有会话（当前会话带「(当前)」标记）
> /session -list

# 清空当前会话（消息流一并清空）
> /session -clear

# 查看统计
> /stats
```

`/session` 只支持 `-list`、`-new <标题>`、`-clear`；切换会话请在侧边栏点击会话。
`/stats` 不接受参数，输出消息数量、权限模式与当前会话；Token 用量显示在页脚状态栏。

**会话自动保存**：
- 对话历史自动保存到 SQLite 数据库
- 数据库位置：程序基目录下的 `luban-ai-agent.db`（`appsettings.json` 中 `Data Source=./luban-ai-agent.db`；旧版 `ai_sessions.db` 首次启动自动更名）
- 用户消息和 AI 回复都会自动保存
- Token 数量自动统计

### 自定义 Skill

LuBan Agent 支持文件化 Skill，在项目级或用户级目录创建 `SKILL.md` 文件，兼容 OpenCode 格式：

```
存储位置（按优先级）：
  项目级: <workspace>/.luban-agent/skills/<skill-id>/SKILL.md
  用户级: %LocalAppData%/LuBan/AIAgent/skills/<skill-id>/SKILL.md
```

**SKILL.md 格式**：

```markdown
---
name: my-translator
description: "将文本翻译成英文"
category: custom
---

# 翻译助手

请将用户提供的内容翻译成英文。

## 要求
- 保持原文的语气和风格
- 使用地道的英文表达
- 如有专业术语，保留原文并括号注释
```

### 自定义规则

输入 `/rule` 打开设置中心的「规则」页，点「＋ 新建」后填写表单：`Id`（必填，即文件名）、`Name`、`Description`、`ActionTypePattern`、`TargetPattern`（两者均支持通配符）、`Action`（allow / deny）、`Priority`（数字越大优先级越高）、`Enabled`、`Content`（可选，供 `IContentRule` 读取的引导文本），点「保存」生效。

```
> /rule
（打开设置中心 → 工作区配置 → 规则）

示例：Id=protect-system，Name=保护系统目录，
     ActionTypePattern=file-write，TargetPattern=C:\Windows\*，Action=deny，Priority=100
```

### MCP 外部服务器

```
# 打开 MCP 服务设置页
> /mcp

# 表单字段
Name        必填，即文件名（如 github）
Description 可选
Transport   stdio / http / sse
Command     stdio 填启动命令（如 npx）；http / sse 可将 URL 填在此处
Args        一行一个参数，如 -y、@modelcontextprotocol/server-github
Enabled     是否参与加载
```

「保存」后点「应用配置」即时热加载（`McpManagementService` 调用 `Reload()`）；接入后的工具以 `mcp_{client}_{tool}` 命名，如 `mcp_github_create_issue`。

---

## 🔧 技术栈

| 组件 | 说明 |
|------|------|
| **Avalonia 12.1.6** | 跨平台 UI 框架（.NET 10.0，暗色主题） |
| **Microsoft.Agents.AI.Foundry** | Agent 运行时框架 |
| **Microsoft.Extensions.AI** | 统一聊天客户端抽象 |
| **Microsoft.Playwright** | 浏览器自动化引擎 |
| **LuBan.DI** | 依赖注入集成 |
| **LuBan.Common** | 基础接口与工具定义 |
| **Microsoft.ML.OnnxRuntime** | ONNX 模型推理（语义检索） |
| **SQLite** | 会话与向量数据存储 |
| **CommunityToolkit.Mvvm** | MVVM 架构支持 |

---

## 💡 小贴士

- 🖥️ **图形界面**：启动后进入 Avalonia 桌面应用，输入普通文本直接与 Agent 对话
- ⌨️ **快捷键**：`Enter` 发送消息、`Ctrl+Enter` / `Shift+Enter` 换行、`Ctrl+V` 粘贴附件（文件/截图）、`Shift+Tab` 切换权限模式、`Esc` 取消当前任务
- 🛡️ **四模式权限**：`Shift+Tab` 循环切换 Default / Plan / AcceptEdits / BypassPermissions，页脚实时显示
- 🎨 **折叠/展开**：思考过程和工具调用默认折叠，点击展开查看详情
- 📜 **智能滚动**：流式输出自动贴底；手动上滚断开跟随
- 🌐 **多 Provider 支持**：支持 20+ 种 AI Provider，统一 `provider:model` 格式
- 🛠️ **12 大内置工具组**覆盖浏览器自动化、文件操作（含编辑）、Git 版本控制、脚本执行、Web 请求、语义检索、上下文压缩、本地记忆、MCP 外部工具、多 Agent 编排、LLM Wiki 知识库（opt-in）与大日志分析
- ⚠️ **危险操作确认**：对工作区外路径访问及写入、删除、执行等敏感操作要求用户确认，支持本次/本轮/本会话三档授权
- 🔒 **路径授权**：工作区内免确认；工作区外路径经确认后放行，授权按路径所在根记忆
- 🧩 **多 Agent 编排**：AI 自动识别复合任务，拆解为 DAG 并由 SubAgent 串行/并行混合执行
- 📂 **工作区隔离**：每个工作区有独立的会话历史与配置目录

---

## 🤝 相关项目

- **[LuBan.Framework](https://github.com/yswenli/luban-framework)** - LuBan 框架核心
- **[LuBan.DI](https://www.nuget.org/packages/LuBan.DI/)** - 依赖注入容器
- **[LuBan.AIFlow](https://www.nuget.org/packages/LuBan.AIFlow/)** - AI 工作流引擎
- **[LuBan.Web.Core](https://www.nuget.org/packages/LuBan.Web.Core/)** - Web 核心组件
- **[LuBan.Agent.CLI](../LubanAgentCli/README.md)** - 命令行版本

---

## 📄 许可证

MIT License

---

## 👤 作者

**yswenli**
- 📧 Email: yswenli@outlook.com
- 🐙 GitHub: [@yswenli](https://github.com/yswenli)

---

**⭐ 如果这个项目对你有帮助，请给它一个 Star！⭐**

Made with ❤️ by yswenli
