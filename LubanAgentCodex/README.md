# LuBan Agent Codex

<div align="center">

**基于 Avalonia UI 的跨平台 AI 编码代理桌面客户端**

*让大模型具备思考、规划、调用工具和自主执行的能力*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12.1.1-blue.svg)](https://avaloniaui.net/)

[English](README.en.md) | 中文

</div>

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
- **经典三栏式布局**：左侧边栏 + 主内容区 + 底部输入区
- **暗色主题**：专业的暗色配色方案，长时间编码不疲劳
- **流式输出**：实时显示 AI 思考过程和工具调用状态
- **智能滚动**：自动跟随输出，支持手动滚动查看历史

### 🤖 多模型路由
- **20+ 种 AI Provider 支持**：OpenAI、Azure、DeepSeek、Kimi、GLM、通义千问、豆包、Claude、Gemini、Ollama、MiniMax、字节方舟、阿里百炼、腾讯混元、小米 MiMo、百度文心一言(ERNIE)、xAI Grok、百度智能云千帆、腾讯云 TI 平台、华为云盘古、AWS Bedrock、OpenRouter，以及自定义 OpenAI 兼容 API
- **统一 `provider:model` 格式**：一键切换模型，无需修改代码
- **动态路由**：LuBanChatClient 根据前缀自动分发到对应 Provider

### 🛠️ 7 大内置工具组
| 工具组 | 能力 |
|--------|------|
| 🌐 **浏览器工具** | 导航、点击、输入、截图、获取内容（基于 Playwright） |
| 📁 **文件系统工具** | 读取、写入、列出目录，支持安全路径限制 |
| 🔧 **脚本执行工具** | 执行 Shell、Lua、Python 脚本 |
| 🗄️ **数据库工具** | ADO.NET 直连执行 SQL（MySQL/PostgreSQL/SQL Server/SQLite） |
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

### 📂 工作区与知识库
- **工作区隔离**：每个工作区拥有独立的根目录、会话历史和配置目录
- **工作区配置目录**：每个工作区根目录下自动创建 `.luban-agent/`，可放置自定义 `skills`、`rules`、`mcps` 配置
- **临时文件管理**：运行时生成的脚本、截图、中间文件统一存放于 `.luban-agent/temp/`，支持自动清理过期文件
- **RAG 知识库**：特殊工作区类型，支持文件索引与语义检索，自动检索增强问答
- **向量存储隔离**：不同工作区的索引数据完全隔离，互不串读
- **路径授权管理**：工作区授权与 PathGuard 集成，仅授权的工作区根目录可访问

---

## 🚀 快速开始

### 1. 安装 Playwright 浏览器（使用浏览器工具前必须安装）

```powershell
# 安装与 Microsoft.Playwright 1.61.0 匹配的浏览器版本
npx playwright@1.61.0 install chromium
```

> **注意**：浏览器版本必须与 Microsoft.Playwright 包版本匹配，当前项目使用的是 1.61.0。

### 2. 克隆源码运行

```bash
# 克隆仓库
git clone https://github.com/yswenli/luban-framework.git
cd luban-framework/luban-agent

# 运行程序
dotnet run --project LubanAgentCodex/LubanAgentCodex.csproj
```

### 3. 选择工作区

启动后会弹出工作区选择器：

```
┌─────────────────────────────────────────┐
│  选择工作区                              │
├─────────────────────────────────────────┤
│  📁 MyProject                           │
│     D:\Projects\MyProject               │
│     最后活跃: 2026-08-20 10:30          │
│                                         │
│  📁 Docs                                │
│     D:\Documents\Docs                   │
│     最后活跃: 2026-08-19 15:20          │
│                                         │
│  [📂 打开文件夹...]                      │
└─────────────────────────────────────────┘
```

选择已有工作区或点击"打开文件夹"创建新工作区。

### 4. 配置你的第一个 AI Provider

在输入框中输入 `/provider -add`，按提示操作：

```
> /provider -add

选择 Provider 类型:
  1. OpenAI
  2. Azure OpenAI
  3. DeepSeek
  4. Kimi (Moonshot)
  ...

请选择 (1-23): 4
请输入 Kimi API Key: ********

✓ Provider 'Kimi' 已添加并保存
```

### 5. 选择模型并开始对话

```
> /model -switch

已配置的 Provider:
  1. Kimi

请选择 Provider 编号: 1
Kimi 支持的模型:
  1. k3
  2. k3-256k
  3. kimi-for-coding
  ...

请选择模型编号: 1
✓ 已选择模型: kimi:k3
```

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
| `/help` | — | 显示帮助信息 |
| `/clear` | — | 清空会话历史 |
| `/mode [name]` | — | 查看或切换权限模式（default/plan/accept-edits/bypass） |
| `/provider` | `/p` | 管理 AI Provider |
| `/model` | `/m` | 管理模型 |
| `/skill` | `/sk` | 查看和执行 Skill |
| `/rule` | `/r` | 查看和管理规则 |
| `/mcp` | `/mp` | 查看 MCP 客户端 |
| `/session` | `/se` | 管理对话会话 |
| `/stats` | `/st` | 会话与 Token 统计 |
| `/work` | `/w` | 工作区管理 |
| `/rag` | `/rg` | 知识库管理 |

### 子命令简写

| 简写 | 完整命令 | 适用场景 |
|------|---------|---------|
| `-l` | `-list` | 所有管理命令 |
| `-a` | `-add` | 所有管理命令 |
| `-u` | `-update` | Provider/Model/Skill/Rule/MCP |
| `-d` | `-delete` | Provider/Model/Skill/Rule/MCP/Work/Rag |
| `-s` | `-switch` | 所有管理命令 |
| `-n` | `-new` | Session/Work/Rag |
| `-c` | `-clear` | Session |

**示例**：
- `/p -l` = `/provider -list`
- `/st -d 7` = `/stats -days 7`
- `/work -n D:\MyProject` = 创建工作区

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

### 场景三：代码审查

```
> /skill code-review

请输入内容: public void Test() { var x = 1/0; }

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

⚙️ 调用工具: ExecuteQueryAsync
   参数: sql = SELECT * FROM users ORDER BY created_at DESC LIMIT 10
✓ 工具执行完成

🤖 查询结果如下：
...
```

### 场景五：复合任务自动编排

AI 会自动识别复合任务并启用 DAG 编排：

```
你: 调研 LuBan-Framework 和 Luban-Agent 两个项目，对比它们的优缺点，生成一份对比报告

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

```
# 创建工作区
> /work -new D:\MyProject
✓ 已创建工作区: MyProject - D:\MyProject

# 列出所有工作区
> /work -list
┌──────────┬──────┬──────────────┬──────┬────────────┬──────┐
│ 名称     │ 类型 │ 根目录       │ 会话 │ 最后活跃   │ 授权 │
├──────────┼──────┼──────────────┼──────┼────────────┼──────┤
│ * MyProject │ 普通 │ D:\MyProject │ 3    │ 2026-08-20 │ ✓    │
│   Docs      │ RAG  │ D:\Docs      │ 0    │ -          │ ✗    │
└──────────┴──────┴──────────────┴──────┴────────────┴──────┘

# 切换工作区
> /work -switch MyProject
✓ 已切换到工作区: MyProject
  根目录: D:\MyProject
```

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
你: 如何创建工作区？
🤖 根据知识库文档，创建工作区使用 /work -new 命令...
```

---

## 🏗️ 项目结构

```
LubanAgentCodex/
├── App.axaml(.cs)              # 应用程序入口
├── Program.cs                  # 主入口点
├── Styles/
│   └── Colors.axaml            # 主题配色资源
├── Services/
│   ├── AgentHostService.cs     # Agent 宿主服务
│   ├── FooterDataProvider.cs   # 页脚数据提供者
│   └── StreamEvent.cs          # 流式事件类型
├── ViewModels/
│   ├── MainWindowViewModel.cs  # 主窗口 ViewModel
│   └── Messages/               # 消息数据模型
│       ├── AssistantMessageItem.cs
│       ├── UserMessageItem.cs
│       ├── ToolCallItem.cs
│       ├── ToolConfirmItem.cs
│       └── ThinkingMessageItem.cs
├── Views/
│   ├── MainWindow.axaml(.cs)   # 主窗口
│   ├── RenameDialog            # 重命名对话框
│   ├── SkillManageWindow       # 技能管理窗口
│   ├── RuleManageWindow        # 规则管理窗口
│   ├── MCPManageWindow         # MCP 服务管理窗口
│   ├── ProviderManageWindow    # Provider 管理窗口
│   ├── WorkManageWindow        # 工作区管理窗口
│   ├── RagManageWindow         # RAG 管理窗口
│   └── Controls/               # 自定义控件
│       ├── Sidebar             # 左侧边栏
│       ├── TitleBar            # 顶部标题栏
│       ├── MessageStream       # 消息流
│       ├── InputBox            # 输入框
│       ├── FooterBar           # 页脚状态栏
│       ├── UserMessageView     # 用户消息
│       ├── AssistantMessageView# AI 消息
│       ├── ThinkingMessageView # 思考过程
│       ├── ToolCallCard        # 工具调用卡片
│       ├── ConfirmCard         # 确认卡片
│       └── SystemMessageView   # 系统消息
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

⚠️ 需要确认
工具: WriteFileAsync
参数:
  path: C:\temp\test.txt
  content: 文件内容...

[✓ 允许] [ 本轮全允许] [✗ 拒绝]
```

**需要确认的操作包括**：
- 📝 **文件系统**: 写入文件、删除文件、创建/删除目录
- 🔧 **脚本执行**: 执行 Shell、Lua、Python 脚本
- 🗄️ **数据库**: INSERT、UPDATE、DELETE 操作
- 🔴 **Redis**: SET、DELETE、FLUSHDB 操作

### 会话管理

```
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
- 对话历史自动保存到 SQLite 数据库
- 数据库位置：`%LocalAppData%\LuBan\AIAgent\ai_sessions.db`
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

```
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

```
# 添加外部 MCP 服务器
> /mcp -add

请输入服务器名称: github
请输入描述: GitHub 集成
请输入启动命令 (如 npx): npx
请输入命令参数 (空格分隔，可选): -y @modelcontextprotocol/server-github
✓ 外部 MCP 服务器 'github' 已添加

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
| **Avalonia 12.1.1** | 跨平台 UI 框架（.NET 10.0，暗色主题） |
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
- ⌨️ **快捷键**：`Enter` 发送消息、`Ctrl+Enter` 换行、`Shift+Tab` 切换权限模式
- 🛡️ **四模式权限**：`Shift+Tab` 循环切换 Default / Plan / AcceptEdits / BypassPermissions，页脚实时显示
- 🎨 **折叠/展开**：思考过程和工具调用默认折叠，点击展开查看详情
- 📜 **智能滚动**：流式输出自动贴底；手动上滚断开跟随
- 🌐 **多 Provider 支持**：支持 20+ 种 AI Provider，统一 `provider:model` 格式
- 🛠️ **7 大内置工具组**覆盖浏览器自动化、文件操作、脚本执行、数据库、Redis、Web 请求、语义检索
- ⚠️ **危险操作确认**：对写入、删除、执行等危险操作自动要求用户确认
- 🔒 **路径授权**：工作区授权后，AI Agent 可访问根目录及其子目录
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

<div align="center">

**⭐ 如果这个项目对你有帮助，请给它一个 Star！⭐**

Made with ❤️ by yswenli

</div>
