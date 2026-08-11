# LuBan Agent CLI

<div align="center">

**基于 Microsoft Agent Framework 的 AI Agent 命令行工具**

*让大模型具备思考、规划、调用工具和自主执行的能力*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Terminal.Gui](https://img.shields.io/badge/Terminal.Gui-2.4.17-blue.svg)](https://gui-cs.github.io/Terminal.Gui/)

[English](README.en.md) | 中文

</div>

---

## 🌟 为什么选择 LuBan Agent？

想象一下：你只需说一句"帮我查一下D盘下面有哪些目录"，AI 就能自动调用文件系统工具，列出所有目录。再说一句"帮我打开百度并搜索 LuBan Framework"，AI 就会自动启动浏览器，完成搜索。

这不是科幻，这就是 **LuBan Agent**。

### 你是否遇到过这些问题？

- 😫 想让 LLM 调用工具完成任务，但 MCP / Function Calling 的实现细节令人头疼？
- 😤 Skill 管理、工具注册、会话持久化各自需要单独实现，维护成本高？
- 😖 模型 Provider 切换困难——从 Provider A 换到 Provider B 需要重写大量代码？
- 😩 缺少中间件机制——日志、策略控制、权限拦截难以扩展？

**LuBan Agent 为你提供完整的 AI Agent 基础设施**，从 Agent 运行时、多模型路由、技能系统、工具系统、会话存储到中间件管道——开箱即用。

---

## ✨ 核心特性

### 🤖 多模型路由
- **20+ 种 AI Provider 支持**：OpenAI、Azure、DeepSeek、Kimi、GLM、通义千问、豆包、Claude、Gemini、Ollama、MiniMax、字节方舟、阿里百炼、腾讯混元、小米 MiMo、百度文心一言(ERNIE)、xAI Grok、百度智能云千帆、腾讯云 TI 平台、华为云盘古、AWS Bedrock、OpenRouter，以及自定义 OpenAI 兼容 API
- **多地址支持**：部分 Provider 提供多个 API 地址（如 Kimi 有国内通用、海外直连、编程专属三个地址），添加时可灵活选择
- **统一 `provider:model` 格式**：一键切换模型，无需修改代码
- **动态路由**：LuBanChatClient 根据前缀自动分发到对应 Provider

### 🛠️ 7 大内置工具组
| 工具组 | 能力 |
|--------|------|
| 🌐 **浏览器工具** | 导航、点击、输入、截图、获取内容（基于 Playwright） |
| 📁 **文件系统工具** | 读取、写入、列出目录，支持安全路径限制 |
| 🔧 **脚本执行工具** | 执行 Shell、Lua、Python 脚本 |
| 🗄️ **数据库工具** | ADO.NET 直连执行 SQL（MySQL/PostgreSQL/SQL Server/SQLite），支持动态连接字符串 |
| 🔴 **Redis 工具** | 通过 redis-cli 执行 Redis 命令 |
| 🌍 **Web 工具** | 发送 HTTP 请求获取网页内容 |
| 🔍 **语义检索工具** | 索引本地代码/文档，按语义搜索相关片段 |

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

**对话内激活 Skill**：在 `/agi` 或 `/browse` 对话中输入 `/skill -switch`，选择 Skill 后仅对下一条输入生效，执行后自动取消。

**文件化 Skill**：支持通过 SKILL.md 文件定义自定义 Skill，兼容 OpenCode 格式，放置于项目级或用户级目录即可自动加载。

### 🛡️ 安全与规则引擎
- **路径访问规则**：限制文件系统访问范围，防止越权操作
- **危险操作确认**：写入、删除、执行脚本前自动要求用户确认
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
- **错误处理**：关键节点失败跳过后继，非关键节点失败继续执行
- **无感集成**：在 `/agi` 对话中自动触发，无需手动切换命令
- **编排扩展**：工作区 `.luban-agent/plans/*.json` 定义任务模板、`.luban-agent/roles/*.json` 定义自定义 SubAgent 角色；`Orchestration:PlannerModel` 与节点 `ModelName` 支持 `provider:model` 多模型路由
- **启发式预过滤**：短输入且无复合关键词时跳过任务拆解，节省 LLM 调用

### 📂 工作区与知识库
- **工作区隔离**：每个工作区拥有独立的根目录、会话历史和配置目录
- **工作区配置目录**：每个工作区根目录下自动创建 `.luban-agent/`，可放置自定义 `skills`、`rules`、`mcps` 配置
- **临时文件管理**：运行时生成的脚本、截图、中间文件统一存放于 `.luban-agent/temp/`，支持自动清理过期文件
- **RAG 知识库**：特殊工作区类型，支持文件索引与语义检索，自动检索增强问答
- **向量存储隔离**：不同工作区的索引数据完全隔离，互不串读
- **路径授权管理**：工作区授权与 PathGuard 集成，仅授权的工作区根目录可访问
- **默认配置生成**：RAG 工作区创建时自动生成 `rag-config.json` 默认配置

---

## 🚀 快速开始

### 1. 安装 Playwright 浏览器（使用浏览器工具前必须安装）

```powershell
# 安装与 Microsoft.Playwright 1.61.0 匹配的浏览器版本
npx playwright@1.61.0 install chromium
```

> **注意**：浏览器版本必须与 Microsoft.Playwright 包版本匹配，当前项目使用的是 1.61.0。

### 方式一：克隆源码运行

```bash
# 克隆仓库
git clone https://github.com/yswenli/luban-framework.git
cd luban-framework/luban-agent

# 运行程序
dotnet run
```

### 方式二：作为 dotnet tool 安装（推荐）

安装为 .NET 全局工具后，可从任意目录直接调用 `luban-agent-cli` 命令，无需克隆源码。

#### 从 NuGet 安装（发布后）

```bash
dotnet tool install -g LuBan.Agent.CLI
```

#### 从本地源码安装

```bash
# 1. 克隆仓库
git clone https://github.com/yswenli/luban-framework.git
cd luban-framework/luban-agent

# 2. 打包
dotnet pack -c Release -o ./artifacts

# 3. 全局安装
dotnet tool install -g LuBan.Agent.CLI --add-source ./artifacts
```

#### 安装后使用

```bash
# 在任意目录启动全屏 TUI 交互界面（需要可交互终端，不支持输入/输出重定向）
luban-agent-cli
```

> **TUI 已完成迁移**：界面层已从 Console/Spectre 混合渲染全面迁移到 Terminal.Gui v2 全屏 TUI（参照 Claude Code 风格）。
> 三区域布局（会话区/页脚/输入区）、Agent 流式对话循环、四模式权限确认、内联命令面板、页脚元数据和 Agent View 多会话均已就位。
> 首次输入时自动初始化 Agent 并进入流式对话；`Esc` 取消当前任务，`Shift+Tab` 循环切换权限模式，`Tab` 切换对话/任务视图。

#### 配置文件说明

- **应用配置**（`appsettings.json`）：始终优先从工具安装目录加载；当前工作目录下的 `appsettings.json` 可作为覆盖项
- **用户配置**（Provider、Skill、规则等）：自动保存在 `%LocalAppData%\LuBan\AIAgent\config.json`
- **会话数据**：保存在 `%LocalAppData%\LuBan\AIAgent\ai_sessions.db`

#### 更新与卸载

```bash
# 更新（从 NuGet）
dotnet tool update -g LuBan.Agent.CLI

# 更新（从本地源码）
dotnet pack -c Release -o ./artifacts
dotnet tool update -g LuBan.Agent.CLI --add-source ./artifacts

# 卸载
dotnet tool uninstall -g LuBan.Agent.CLI
```

#### 验证安装

```bash
# 查看已安装的工具
dotnet tool list -g

# 应看到类似输出：
# Package Id         Version   Commands
# -----------------------------------------
# LuBan.Agent.CLI    1.0.0     luban-agent-cli
```

### 3. 配置你的第一个 AI Provider

```
> /provider -add
选择 Provider 类型:
  1. OpenAI
  2. Azure OpenAI
  3. DeepSeek
  4. Kimi (Moonshot)
  5. 智谱 GLM
  6. 通义千问
  7. 豆包
  8. Claude
  9. Google Gemini
  10. Ollama (本地)
  11. MiniMax
  12. 字节方舟 (火山引擎)
  13. 阿里百炼
  14. 腾讯混元
  15. 小米 MiMo
  16. 百度文心一言 (ERNIE)
  17. xAI Grok
  18. 百度智能云千帆
  19. 腾讯云 TI 平台
  20. 华为云盘古
  21. AWS Bedrock
  22. OpenRouter
  23. 自定义 OpenAI 兼容 API
请选择 (1-23): 4
请输入 Kimi API Key: ********

Kimi API 地址选择:
  1. 国内通用 (https://api.moonshot.cn/v1) - 推荐
  2. 海外直连 (https://api.moonshot.ai/v1)
  3. 编程专属 (https://api.kimi.com/coding/v1)
请选择 (1-3): 1
✓ Provider 'Kimi' 已添加并保存
  支持的模型: k3, k3-256k, kimi-for-coding, kimi-for-coding-highspeed
```

> **安全提示**：API Key 输入时会隐藏显示（密码输入模式）

### 4. 选择模型并开始对话

```
> /model -switch
已配置的 Provider:
  1. OpenAI

请选择 Provider 编号: 1
OpenAI 支持的模型:
  1. gpt-4.1
  2. gpt-4.1-mini
  3. gpt-4.1-nano
  4. gpt-4o
  ...

请选择模型编号: 4
✓ 已选择模型: openai:gpt-4o
```

### 5. 开始你的第一次 Agent 对话

```
> /agi

你: 帮我查一下D盘下面有哪些目录

⠋ 正在调用工具: ListDirectoryAsync
⠙ 正在思考...
⠹ 正在生成回答...

[调用工具]: list_directory
  参数 path: D:\
[工具结果]: Program Files, Users, Windows, ...

🤖 D盘下有以下目录：
1. Program Files
2. Users  
3. Windows
...
```

> **实时状态显示**：在执行 AI 对话时，会显示动态旋转动画和实时状态：
> - 正在思考...
> - 正在调用工具: {工具名}
> - 工具执行完成，正在生成回答...
> - 生成回答完成

---

## 📖 命令一览

LuBan Agent 提供了简洁而强大的命令系统：

### 直接命令行执行

TUI 重构完成后，所有命令均在 TUI 全屏界面内以 `/` 前缀交互式调用。直接命令行参数执行（如 `luban-agent-cli /se -l`）将作为后续增强功能计划。

### 命令输入方式（TUI 全屏界面）

启动后进入 Terminal.Gui 全屏 alt-screen 界面，布局自上而下为会话区（可滚动）、页脚（模式/目录/git/token）、输入区（贴底）：

- **Enter** — 提交当前输入（普通文本发送给 Agent，`/` 开头路由到命令面板）
- **Ctrl+Q** — 强制退出 TUI
- **Ctrl+L** — 重绘屏幕
- **Esc** — 取消当前运行的 Agent 任务
- **Shift+Tab** — 循环切换权限模式（Default → Plan → AcceptEdits → BypassPermissions）
- **Tab** — 切换对话视图 / Agent 任务视图
- **鼠标** — 点击 Block 折叠/展开；滚轮滚动；Shift 拖选终端原生复制

### 命令列表

| 命令 | 简写 | 说明 |
|------|------|------|
| `/help` | — | 显示帮助信息（已接入 TUI） |
| `/clear` | — | 清空会话历史（已接入 TUI） |
| `/mode [name]` | — | 查看或切换权限模式（default/plan/accept-edits/bypass）（已接入 TUI） |
| `/exit` / `/quit` | — | 退出程序 |
| `/provider` | `/p` | 管理 AI Provider（后端已就绪，TUI 内联界面计划中） |
| `/model` | `/m` | 管理模型（后端已就绪，TUI 内联界面计划中） |
| `/skill` | `/sk` | 查看和执行 Skill |
| `/rule` | `/r` | 查看和管理规则 |
| `/mcp` | `/mp` | 查看 MCP 客户端 |
| `/session` | `/se` | 管理对话会话 |
| `/agi` | `/a` | 通用 Agent 对话（已自动启用，直接输入即可） |
| `/browse` | `/b` | 针对网站操作特异化 Agent |
| `/stats` | `/st` | 会话与 Token 统计 |
| `/work` | `/w` | 工作区管理 |
| `/rag` | `/rg` | 知识库管理 |

### 子命令简写

| 简写 | 完整命令 | 适用场景 |
|------|---------|---------|
| `-l` | `-list` | 所有管理命令 |
| `-a` | `-add` | 所有管理命令（注：`/work` 中 `-add` 为 `-authorize` 别名） |
| `-u` | `-update` | Provider/Model/Skill/Rule/MCP |
| `-d` | `-delete` | Provider/Model/Skill/Rule/MCP/Work/Rag |
| `-d` | `-days` | Stats（统计天数） |
| `-s` | `-switch` | 所有管理命令 |
| `-n` | `-new` | Session/Work/Rag |
| `-c` | `-clear` | Session |
| `-c` | `-connect` | MCP |
| `-t` | `-tools` | MCP |
| `-i` | `-index` | Rag（索引文件） |
| `-info` | `-info` | Work（工作区信息） |
| `-add` | `-authorize` | Work（授权工作区） |

**示例**：
- `/p -l` = `/provider -list`
- `/st -d 7` = `/stats -days 7`
- `/st --all` = 跨所有工作区统计
- `/mp -c filesystem` = `/mcp -connect filesystem`
- `/work -n D:\MyProject` = 创建工作区
- `/rag -i *.md` = 索引当前 RAG 工作区的 Markdown 文件

---

## 🎨 使用场景

### 场景一：文件管理助手

```
你: 帮我列出 src 目录下所有 .cs 文件并统计代码行数

[调用工具]: list_directory
  参数 path: src
[调用工具]: read_file
  参数 path: src\Program.cs
...

🤖 src 目录下共有 15 个 .cs 文件，总计 3,456 行代码：
- Program.cs: 112 行
- Services\ConsoleAppService.cs: 344 行
...
```

### 场景二：网页自动化

```
你: 帮我打开百度并搜索 "LuBan Framework"

[调用工具]: NavigateAsync
  参数 url: https://www.baidu.com
[调用工具]: TypeTextAsync
  参数 selector: #kw, text: LuBan Framework
[调用工具]: ClickAsync
  参数 selector: #su

🤖 已完成搜索，当前页面标题: LuBan Framework_百度搜索
```

### 场景三：代码审查

```
> /skill code-review

请输入内容: public void Test() { var x = 1/0; }

执行 Skill: 代码审查

📋 代码分析：
发现潜在问题：
1. ⚠️ 第 1 行存在除以零的错误
2. 💡 建议添加异常处理机制

改进建议：
...
```

### 场景四：数据库查询

```
你: 帮我查询用户表中最近的10条记录

[调用工具]: run_sql
  参数 sql: SELECT * FROM users ORDER BY created_at DESC LIMIT 10
[工具结果]: ...

🤖 查询结果如下：
...
```

### 场景五：复合任务自动编排

在 `/agi` 对话中，AI 会自动识别复合任务并启用 DAG 编排：

```
> /agi

👶 调研 LuBan-Framework 和 Luban-Agent 两个项目，对比它们的优缺点，生成一份对比报告

💭 思考过程:
这是一个复合任务，需要拆解为多个子任务并行执行...

🔄 开始规划任务图谱...
✓ 规划完成: 已生成 4 个节点的任务图谱

▶ 开始执行节点: research-luban
  [SubAgent] 调用 web_search 搜索 LuBan 框架特性...
✓ 节点完成: research-luban

▶ 开始执行节点: research-langchain
  [SubAgent] 调用 web_search 搜索 LangChain 框架特性...
✓ 节点完成: research-langchain
── 第 1 层执行完成（并行） ──

▶ 开始执行节点: compare
  [SubAgent] 基于前驱结果进行对比分析...
✓ 节点完成: compare
── 第 2 层执行完成 ──

▶ 开始执行节点: report
  [SubAgent] 生成对比报告...
✓ 节点完成: report
── 第 3 层执行完成 ──

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

**编排说明**：

- 输入复合任务后，AI 自动判断是否需要拆解
- 每个节点由独立 SubAgent 执行，支持工具组隔离
- 同层节点并行执行（如同时调研两个框架），跨层节点串行执行
- 节点间通过 `{dep:xxx}` 占位符传递上下文
- 流式输出执行进度，实时显示节点状态
- 无需手动切换命令，在 `/agi` 对话中自然触发

### 场景六：工作区管理

```
# 创建工作区
> /work -new D:\MyProject
✓ 已创建工作区: MyProject - D:\MyProject

# 列出所有工作区
> /work -list
┌──────────┬──────┬──────────────┬──────┬────────────┬──────┐
│ 名称     │ 类型 │ 根目录       │ 会话 │ 最后活跃   │ 授权 │
├──────────┼──────┼──────────────┼──────┼────────────┼──────┤
│ * MyProject │ 普通 │ D:\MyProject │ 3    │ 2026-07-31 │ ✓    │
│   Docs      │ RAG  │ D:\Docs      │ 0    │ -          │ ✗    │
└──────────┴──────┴──────────────┴──────┴────────────┴──────┘

# 切换工作区
> /work -switch MyProject
✓ 已切换到工作区: MyProject
  根目录: D:\MyProject
  输入 /agi 开始工作

# 授权工作区访问
> /work -authorize
═══ 工作区授权确认 ═══
工作区: MyProject
根目录: D:\MyProject
⚠️  AI Agent 将被授权访问此目录及其子目录
是否授权？(y/N): y
✓ 已授权工作区
```

**工作区说明**：

- 启动时自动以当前目录创建或恢复工作区
- 每个工作区拥有独立的会话历史与配置目录（`.luban-agent/`）
- 切换工作区时自动恢复该工作区的最近会话
- 工作区授权后，AI Agent 可访问根目录及其子目录

### 场景七：RAG 知识库问答

```
# 1. 创建 RAG 知识库
> /rag -new D:\KnowledgeBase 我的知识库
✓ 已创建 RAG 知识库: 我的知识库 - D:\KnowledgeBase

# 2. 切换到 RAG 工作区
> /work -switch 我的知识库
✓ 已切换到工作区: 我的知识库

# 3. 授权并索引文件
> /rag -index *.md
开始索引工作区: 我的知识库
✓ 索引完成
  扫描文件: 25
  新增文件: 25
  总切块数: 142

# 4. 检索测试
> /rag -search 如何配置工作区
找到 3 条相关结果：
文件: D:\KnowledgeBase\setup.md
内容: 工作区配置需要...

# 5. 直接问答（自动检索增强）
> /agi
模式: 知识库问答（自动检索增强）

👶 如何创建工作区？
🤖 根据知识库文档，创建工作区使用 /work -new 命令...
```

**RAG 知识库说明**：

- RAG 工作区是特殊工作区，专注于文件管理与知识问答
- 默认支持 `.txt` 和 `.md` 文件索引
- 索引后通过 `/agi` 对话时自动检索相关文档并注入上下文
- 不同 RAG 工作区的向量数据完全隔离
- 工作区根目录下自动生成 `.luban-agent/rag-config.json` 配置文件

---

## 🏗️ 项目结构

```
LubanAgent/
├── App/                    # TUI 应用层（Terminal.Gui 启动 + DI + 主题）
│   ├── TerminalGuiApp.cs       # Application.Create().Init() + Run 启动引导
│   ├── TuiTheme.cs             # 24-bit TrueColor 配色方案
│   ├── IUiDispatcher.cs        # UI 线程调度抽象
│   └── TerminalGuiDispatcher.cs
├── Views/                  # TUI 视图层（纯渲染 · 无业务逻辑）
│   ├── RootView.cs             # 顶层容器（Runnable）+ 全局快捷键 + 视图协调
│   ├── ConversationView.cs     # 会话区自绘（Block 文档模型驱动）
│   ├── InputBarView.cs         # 输入区（原生 TextView 封装）
│   └── FooterView.cs           # 页脚自绘（模式/目录/git/token/tasks）
├── ViewModels/             # MVVM ViewModel 层
│   ├── ConversationViewModel.cs  # Agent 生命周期 + 流式对话循环 + 权限确认
│   ├── CommandViewModel.cs       # / 命令路由与执行
│   └── AgentViewViewModel.cs     # 多会话任务视图
├── Models/                 # 纯数据模型（无 UI 依赖 · 可单测）
│   ├── ConversationDocument.cs   # 会话文档模型
│   ├── AgentTask.cs / TaskRegistry.cs  # 任务模型与注册表
│   ├── ChoiceOption.cs / ConfirmResult.cs / PlannedAction.cs  # 选择组件类型
│   └── Blocks/                  # Block 类层级
│       ├── Block.cs             # 抽象基类（Layout/Render/HitTest）
│       ├── RenderLine.cs        # RenderLine + TextSegment
│       ├── BlockColors.cs       # 24-bit TrueColor 配色常量
│       ├── UserMessageBlock / AssistantMessageBlock
│       ├── ThinkingBlock / ToolCallBlock / ToolResultBlock
│       ├── InlineChoiceBlock / SystemBlock
│       └── ChoiceBlocks.cs      # 工厂方法
├── Infrastructure/        # 基础设施
│   ├── FlushThrottle.cs         # 流式刷新节流器（16ms 窗口）
│   ├── DatabaseInitializer.cs
│   └── SqliteLocalMemoryStore.cs
├── Services/              # 核心服务
│   ├── FooterDataProvider.cs    # 页脚元数据（git 分支/token 用量）
│   ├── ConsoleAppService.cs     # 命令分发
│   ├── SessionManager.cs        # 会话持久化
│   └── WorkspaceManager.cs      # 工作区管理与授权
├── Commands/              # 命令实现
├── Configuration/         # 配置管理
├── Profiles/              # Agent 配置
├── Repositories/          # 数据访问层
├── Retrieval/             # 语义检索
├── Entities/              # 数据实体
├── Model/                 # AI 模型文件
└── Program.cs             # 程序入口（TUI bootstrap）
```

---

## ⚙️ 配置说明

### 应用配置 (appsettings.json)

```json
{
  "LuBanAgent": {
    "DefaultModel": "openai:gpt-4o",
    "SystemPrompt": "你是一个智能助手。",
    "MaxToolLoopIterations": 10,
    "Session": {
      "CompactTargetMessages": 50,
      "CompactThreshold": 10
    },
    "Tools": {
      "Browser": {
        "Enabled": true,
        "Headless": false,
        "Timeout": 30000
      },
      "FileSystem": {
        "Enabled": true,
        "AllowedRoots": ["C:\\Work"]
      },
      "Script": {
        "Enabled": true,
        "Shell": "cmd",
        "DefaultTimeout": 30000
      },
      "Database": {
        "Enabled": true,
        "ConnectionString": "Server=..."
      },
      "Redis": {
        "Enabled": true
      },
      "Web": {
        "Enabled": true
      },
      "Retrieval": {
        "Enabled": true,
        "ModelId": "bge-small-zh-v1.5",
        "AutoDownload": true,
        "MaxFileSizeKB": 5120,
        "DefaultTopK": 5
      }
    },
    "Orchestration": {
      "Enabled": true,
      "PlannerType": "Composite",
      "AutoDetect": true,
      "ExposeAsTool": false,
      "MaxParallelism": 3,
      "MaxNodes": 20,
      "DefaultNodeTimeoutSeconds": 120,
      "HeuristicFilter": {
        "Enabled": true,
        "MaxLength": 20,
        "Keywords": [ "和", "同时", "然后", "并且", "另外", "还有", "分析并", "搜索并" ]
      }
    }
  }
}
```

### 用户配置 (%LocalAppData%\LuBan\AIAgent\config.json)

用户配置（Provider、自定义 Skill、规则等）自动保存在本地，重启后自动加载。

---

## 🎯 高级功能

### 危险操作确认

对于危险操作（写入文件、执行脚本、删除数据等），系统会在执行前请求用户确认：

```
你: 帮我写一个文件到 C:\temp\test.txt

⚠️  危险操作请求: WriteFileAsync
参数:
  path: C:\temp\test.txt
  content: 文件内容...

是否执行此操作？(y/N): y
✓ 已确认执行

[调用工具]: WriteFileAsync
[工具结果]: 已写入文件 C:\temp\test.txt

🤖 已成功写入文件...
```

**需要确认的操作包括**：
- 📝 **文件系统**: 写入文件、删除文件、创建/删除目录
- 🔧 **脚本执行**: 执行 Shell、Lua、Python 脚本
- 🗄️ **数据库**: INSERT、UPDATE、DELETE 操作
- 🔴 **Redis**: SET、DELETE、FLUSHDB 操作

### 会话管理

```bash
# 创建新会话
> /session -new 项目讨论

# 列出所有会话
> /session -list

# 切换会话
> /session -switch 项目讨论

# 清除所有会话（需确认）
> /session -clear

# 查看统计
> /stats -days 7
```

**会话自动保存**：
- 在 `/agi` 对话中，对话历史自动保存到 SQLite 数据库
- 数据库位置：`%LocalAppData%\LuBan\AIAgent\ai_sessions.db`
- 用户消息和 AI 回复都会自动保存
- Token 数量自动统计

### 自定义 Skill

LuBan Agent 支持两种自定义 Skill 方式：

#### 方式一：文件化 Skill（推荐）

在项目级或用户级目录创建 `SKILL.md` 文件，兼容 OpenCode 格式：

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

**使用方式**：

```bash
# 在 /agi 对话中激活 Skill
> /skill -switch
可用 Skills:
#    类别         名称                 描述                                     来源
1    custom       翻译助手             将文本翻译成英文                          文件

请选择编号 (1-1), 或 0 取消: 1
✓ 已激活 Skill: 翻译助手
💡 Skill 仅对下一条输入生效，执行后自动取消

# 下一条输入自动携带 Skill 指令
👶 你好，世界
🤖 Hello, World
```

**优先级**：项目级 > 用户级 > 内置 > config.json

#### 方式二：命令行添加（兼容旧版）

```bash
# 添加自定义 Skill
> /skill -add

请输入 Skill ID: my-translator
请输入 Skill 名称: 翻译助手
请输入 Skill 描述: 将文本翻译成英文
请输入分类: custom
请输入提示词模板（多行输入，单独一行 '.' 结束）:
请将以下内容翻译成英文：
{input}
.
请输入示例（可选，逗号分隔）: 你好,世界
✓ 自定义 Skill '翻译助手' (my-translator) 已添加
```

### 自定义规则

```bash
# 添加路径保护规则
> /rule -add

请输入规则 ID: protect-system
请输入规则名称: 保护系统目录
请输入 ActionTypePattern (默认 *): file-write
请输入 TargetPattern (默认 *): C:\Windows\*
请输入 Action (allow/deny): deny
请输入优先级 (默认 100): 100
✓ 自定义规则 '保护系统目录' (protect-system) 已添加
```

### MCP 外部服务器

```bash
# 添加外部 MCP 服务器
> /mcp -add

请输入服务器名称: github
请输入描述: GitHub 集成
请输入启动命令 (如 npx): npx
请输入命令参数 (空格分隔，可选): -y @modelcontextprotocol/server-github
✓ 外部 MCP 服务器 'github' 已添加
使用 /mcp connect github 连接

# 连接 MCP 服务器
> /mcp -connect github
正在连接 github...
✓ 连接成功

# 查看可用工具
> /mcp -tools github
github 可用的工具：
  - create_issue: 创建 GitHub Issue
  - search_repositories: 搜索仓库
  ...
```

---

## 🔧 技术栈

| 组件 | 说明 |
|------|------|
| **Terminal.Gui 2.4.17** | 全屏 TUI 框架（net10.0，24-bit TrueColor，alt-screen） |
| **Microsoft.Agents.AI.Foundry** | Agent 运行时框架 |
| **Microsoft.Extensions.AI** | 统一聊天客户端抽象 |
| **Microsoft.Playwright** | 浏览器自动化引擎 |
| **LuBan.DI** | 依赖注入集成 |
| **LuBan.Common** | 基础接口与工具定义 |
| **Microsoft.ML.OnnxRuntime** | ONNX 模型推理（语义检索） |
| **SQLite** | 会话与向量数据存储 |
| **MVVM + Block Document Model** | 自绘渲染架构（Block → RenderLine → ConversationView） |

---

## 💡 小贴士

- 🖥️ **全屏 TUI**：启动即进入 Terminal.Gui 全屏界面，输入普通文本直接与 Agent 对话
- ⌨️ **快捷键**：`Esc` 取消任务、`Shift+Tab` 切换权限模式、`Tab` 切换任务视图、`Ctrl+Q` 退出、`Ctrl+L` 重绘
- 🛡️ **四模式权限**：`Shift+Tab` 循环切换 Default / Plan / AcceptEdits / BypassPermissions，页脚实时显示
- 🎨 **折叠/展开**：思考过程和工具调用默认折叠，点击 `▸` 展开查看详情
- 📜 **滚动跟随**：流式输出自动贴底；手动上滚断开跟随，底部显示"↓ 行提示"
- 🌐 **多地址支持**：部分 Provider（如 Kimi、MiniMax）提供多个 API 地址，添加时可选择
- 🛠️ **7 大内置工具组**覆盖浏览器自动化、文件操作、脚本执行、数据库、Redis、Web 请求、语义检索
- ⚠️ **ToolConfirmationService** 对写入、删除、执行等危险操作自动要求用户确认（InlineChoiceBlock 内联确认）
- 🔒 **FileSystemToolOptions.AllowedRoots** 限制文件访问范围，防止 Agent 越权操作
- 💬 模型路由使用 `provider:model` 格式，支持 20+ 种 AI Provider
- 🧩 **多 Agent 编排**：`/agi` 对话中 AI 自动识别复合任务，拆解为 DAG 并由 SubAgent 串行/并行混合执行
- 📂 **工作区隔离**：`/work` 命令管理工作区，每个工作区有独立的会话历史与配置目录

---

## 🤝 相关项目

- **[LuBan.Framework](https://github.com/yswenli/luban-framework)** - LuBan 框架核心
- **[LuBan.DI](https://www.nuget.org/packages/LuBan.DI/)** - 依赖注入容器
- **[LuBan.AIFlow](https://www.nuget.org/packages/LuBan.AIFlow/)** - AI 工作流引擎
- **[LuBan.Web.Core](https://www.nuget.org/packages/LuBan.Web.Core/)** - Web 核心组件

---

## 📄 许可证

MIT License

---

## 👤 作者

**yswenli**
- 📧 Email: yswenli@outlook.com
- 🐙 GitHub: [@yswenli](https://github.com/yswenli)

---

<div align="center">

**⭐ 如果这个项目对你有帮助，请给它一个 Star！⭐**

Made with ❤️ by yswenli

</div>
