# LuBan Agent CLI

<div align="center">

**基于 Microsoft Agent Framework 的 AI Agent 命令行工具**

*让大模型具备思考、规划、调用工具和自主执行的能力*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

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
- **11 种 AI Provider 支持**：OpenAI、Azure、DeepSeek、Kimi、GLM、通义千问、豆包、Claude、Gemini、Ollama，以及自定义 OpenAI 兼容 API
- **统一 `provider:model` 格式**：一键切换模型，无需修改代码
- **动态路由**：LuBanChatClient 根据前缀自动分发到对应 Provider

### 🛠️ 7 大内置工具组
| 工具组 | 能力 |
|--------|------|
| 🌐 **浏览器工具** | 导航、点击、输入、截图、获取内容（基于 Playwright） |
| 📁 **文件系统工具** | 读取、写入、列出目录，支持安全路径限制 |
| 🔧 **脚本执行工具** | 执行 Shell、Lua、Python 脚本 |
| 🗄️ **数据库工具** | 通过 sqlcmd 执行 SQL 语句 |
| 🔴 **Redis 工具** | 通过 redis-cli 执行 Redis 命令 |
| 🌍 **Web 工具** | 发送 HTTP 请求获取网页内容 |
| 🔍 **语义检索工具** | 索引本地代码/文档，按语义搜索相关片段 |

### 🎯 Skill 系统
内置三大核心技能，即插即用：
- **头脑风暴**：实现功能前探索需求和设计
- **代码审查**：审查代码、发现问题、提供改进建议
- **文档生成**：生成代码注释、README、API 文档

支持自定义 Skill，轻松扩展你的专属能力。

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
- **复合任务自动拆解**：主 Agent 将自然语言任务拆解为 DAG 任务图谱
- **串行/并行混合编排**：基于拓扑分层，同层节点并行执行，跨层串行执行
- **SubAgent 调度**：每个 DAG 节点由独立 SubAgent 执行，支持工具组隔离
- **上下文传递**：节点间通过 `{dep:xxx}` 占位符引用前驱输出
- **错误处理**：关键节点失败跳过后继，非关键节点失败继续执行
- **双模式调用**：`/orchestrate` 命令显式编排，或主 Agent 自动调用编排工具

### 📂 工作区与知识库
- **工作区隔离**：每个工作区拥有独立的根目录、会话历史和配置目录
- **工作区配置目录**：每个工作区根目录下自动创建 `.luban-agent/`，可放置自定义 `skills`、`rules`、`mcps` 配置
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

### 2. 克隆并运行

```bash
# 克隆仓库
git clone https://github.com/yswenli/luban-framework.git
cd luban-framework/luban-agent

# 运行程序
dotnet run
```

### 全局安装（可选）

如果想从任意目录直接调用 `luban-agent-cli` 命令，可将其安装为 .NET 全局工具：

```bash
# 打包
dotnet pack -c Release -o ./artifacts

# 全局安装
dotnet tool install -g LuBan.Agent.CLI --add-source ./artifacts
```

安装完成后，即可在任意目录通过 `luban-agent-cli` 命令启动。配置文件（`appsettings.json`）始终优先从程序所在目录加载，当前工作目录下的 `appsettings.json` 可作为覆盖项。

> **更新**：重新 `dotnet pack` 后执行 `dotnet tool update -g LuBan.Agent.CLI --add-source ./artifacts`
> **卸载**：`dotnet tool uninstall -g LuBan.Agent.CLI`

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
  11. 自定义 OpenAI 兼容 API
请选择 (1-11): 1
请输入 OpenAI API Key: ********
✓ Provider 'OpenAI' 已添加并保存
  支持的模型: gpt-4.1, gpt-4.1-mini, gpt-4.1-nano, gpt-4o...
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

除交互式菜单外，还支持通过命令行参数直接执行命令，执行完毕后自动退出，不进入交互菜单。**这在脚本化调用、从任意目录快速执行单次任务时非常实用。**

```bash
# 语法：luban-agent-cli /<命令> [子命令参数...]
# 首个参数为命令（以 / 开头），其余为子命令参数

# 从任意目录创建新会话
luban-agent-cli /se -n 新会话

# 从任意目录切换会话
luban-agent-cli /se -s 新会话

# 列出所有会话
luban-agent-cli /se -l

# 列出已配置的 Provider
luban-agent-cli /p -l
```

> **说明**：
> - 首个参数必须以 `/` 开头（如 `/se`、`/p`），否则进入交互式菜单
> - 子命令支持简写（`-l`=`-list`、`-s`=`-switch`、`-n`=`-new` 等），与交互模式完全一致
> - 数据库与会话配置始终保存在程序所在目录，不会污染当前工作目录

### 命令输入方式

- **Tab 自动完成** - 输入部分命令后按 Tab 自动补全
- **上/下箭头** - 浏览历史命令
- **Esc 键** - 清除当前输入
- **命令前缀** - 所有命令以 `/` 开头
- **数字快捷键** - 支持数字 1-13 快速选择命令

### 命令列表

| 命令 | 简写 | 数字键 | 说明 |
|------|------|--------|------|
| `/provider` | `/p` | `1` | 管理 AI Provider (-list/-add/-update/-delete/-switch) |
| `/model` | `/m` | `2` | 管理模型 (-list/-add/-update/-delete/-switch) |
| `/skill` | `/sk` | `3` | 查看和执行 Skill (-list/-add/-update/-delete/-switch) |
| `/rule` | `/r` | `4` | 查看和管理规则 (-list/-add/-update/-delete/-switch) |
| `/mcp` | `/mp` | `5` | 查看 MCP 客户端 (-list/-add/-update/-delete/-switch/-connect/-tools) |
| `/session` | `/se` | `6` | 管理对话会话 (-list/-new/-clear/-switch) |
| `/agi` | `/a` | `7` | 通用 Agent 对话 |
| `/browse` | `/b` | `8` | 针对网站操作特异化 Agent |
| `/stats` | `/st` | `9` | 会话与 Token 统计 (-days N, --all 跨工作区) |
| `/orchestrate` | `/o` | `10` | 复合任务编排（DAG 拆解 + SubAgent 调度） |
| `/work` | `/w` | `11` | 工作区管理 (-list/-new/-switch/-delete/-info/-authorize) |
| `/rag` | `/rg` | `12` | 知识库管理 (-new/-index/-search/-list/-delete) |
| `/exit` | - | `13` | 退出程序 |

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

### 场景五：复合任务编排

```
> /orchestrate

📝 调研 LuBan 框架并生成对比报告

🔄 开始规划任务图谱...
✓ 规划完成: 已生成 4 个节点的任务图谱
▶ 开始执行节点: research
✓ 节点完成: research
▶ 开始执行节点: analyze
✓ 节点完成: analyze
── 第 2 层执行完成 ──
▶ 开始执行节点: compare
✓ 节点完成: compare
▶ 开始执行节点: report
✓ 节点完成: report
── 第 3 层执行完成 ──
🎯 编排完成: completed
```

**编排说明**：

- 输入复合任务后，AI 自动拆解为 DAG 任务图谱
- 每个节点由独立 SubAgent 执行，支持工具组隔离
- 同层节点并行执行，跨层节点串行执行
- 节点间通过 `{dep:xxx}` 占位符传递上下文
- 流式输出执行进度，实时显示节点状态

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
├── Commands/              # 命令实现
│   ├── ProviderCommand.cs     # Provider 管理
│   ├── ModelCommand.cs        # 模型管理
│   ├── SkillCommand.cs        # Skill 管理
│   ├── RuleCommand.cs         # 规则管理
│   ├── MCPCommand.cs          # MCP 客户端管理
│   ├── SessionCommand.cs      # 会话管理
│   ├── AgiCommand.cs          # 通用 Agent 对话
│   ├── BrowseCommand.cs       # 浏览器 Agent
│   ├── StatsCommand.cs        # 统计信息
│   ├── OrchestrateCommand.cs  # 复合任务编排
│   ├── WorkCommand.cs         # 工作区管理
│   └── RagCommand.cs          # RAG 知识库管理
├── Services/              # 核心服务
│   ├── ConsoleAppService.cs   # 命令分发与交互
│   ├── SessionManager.cs      # 会话持久化
│   ├── WorkspaceManager.cs    # 工作区管理与授权
│   ├── AgentProfile.cs        # Agent 配置基类
│   ├── NormalAgentProfile.cs  # 普通工作区配置
│   └── RagAgentProfile.cs     # RAG 工作区配置
├── Repositories/          # 数据访问层
│   ├── SessionRepository.cs   # 会话存储
│   ├── WorkspaceRepository.cs # 工作区存储
│   └── RagRepository.cs       # RAG 数据存储
├── Retrieval/             # 语义检索
│   ├── ModelManager.cs        # 嵌入模型管理
│   ├── OnnxEmbeddingGenerator.cs  # ONNX 嵌入生成器
│   └── SqliteVectorStore.cs   # SQLite 向量存储（工作区隔离）
├── Infrastructure/        # 基础设施
│   └── DatabaseInitializer.cs # 数据库初始化
├── Entities/              # 数据实体
├── Model/                 # AI 模型文件
└── Program.cs             # 程序入口
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
| **Microsoft.Agents.AI.Foundry** | Agent 运行时框架 |
| **Microsoft.Extensions.AI** | 统一聊天客户端抽象 |
| **Microsoft.Playwright** | 浏览器自动化引擎 |
| **LuBan.DI** | 依赖注入集成 |
| **LuBan.Common** | 基础接口与工具定义 |
| **Microsoft.ML.OnnxRuntime** | ONNX 模型推理（语义检索） |
| **SQLite** | 会话与向量数据存储 |

---

## 💡 小贴士

- 💬 模型路由使用 `provider:model` 格式，新增 Provider 只需通过 `/provider -add` 添加
- 📌 **支持直接命令行执行**：`luban-agent-cli /se -s 新会话` 即可从任意目录执行单次命令并退出，无需进入交互菜单（详见[命令一览](#-命令一览)）
- 🛠️ **7 大内置工具组**覆盖浏览器自动化、文件操作、脚本执行、数据库、Redis、Web 请求、语义检索
- ⚠️ **ToolConfirmationService** 对写入、删除、执行等危险操作自动要求用户确认
- 🔒 **FileSystemToolOptions.AllowedRoots** 限制文件访问范围，防止 Agent 越权操作
- 💾 **会话历史自动持久化**，支持长对话压缩（SummarizingChatReducer），上下文永不丢失
- 🎨 **自定义 Skill/Rule/MCP 持久化**，配置保存到本地文件，重启后自动加载
- 🛡️ **规则拦截**在工具执行前自动检查，支持 deny/allow/modify
- 🔌 **MCP 工具集成**，外部 MCP 服务器工具自动暴露给 Agent
- 📦 通过 `ExternalPlugins` 配置可热加载外部工具插件程序集
- 🔗 结合 [LuBan.AIFlow](https://www.nuget.org/packages/LuBan.AIFlow/) 可对接 RagFlow / Dify / Coze 等 AI 平台
- 🧩 **多 Agent 编排**：`/orchestrate` 命令将复合任务拆解为 DAG，SubAgent 串行/并行混合执行，支持关键节点失败跳过、超时控制、上下文传递
- 📂 **工作区隔离**：`/work` 命令管理工作区，每个工作区有独立的会话历史与配置目录，切换工作区自动恢复最近会话
- 🔍 **RAG 知识库**：`/rag` 命令创建知识库工作区，索引 `.txt`/`.md` 文件后通过 `/agi` 自动检索增强问答，不同工作区向量数据完全隔离
- 📁 **工作区配置目录**：每个工作区根目录下自动创建 `.luban-agent/`，可放置自定义 `skills`、`rules`、`mcps` 配置，RAG 工作区还会生成默认 `rag-config.json`

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
