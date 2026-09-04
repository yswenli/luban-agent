# LuBan Agent

<div align="center">

**基于 Microsoft Agent Framework 的完整 AI Agent 解决方案**

*一套核心运行时，两种终端体验：命令行 TUI + 跨平台桌面客户端*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Terminal.Gui](https://img.shields.io/badge/Terminal.Gui-2.4.17-blue.svg)](https://gui-cs.github.io/Terminal.Gui/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12.1.1-blue.svg)](https://avaloniaui.net/)

[English](README.en.md) | 中文

</div>

---

## 🌟 什么是 LuBan Agent？

想象一下：你只需说一句"帮我查一下D盘下面有哪些目录"，AI 就能自动调用文件系统工具，列出所有目录。再说一句"帮我打开百度并搜索 LuBan Framework"，AI 就会自动启动浏览器，完成搜索。

这不是科幻，这就是 **LuBan Agent**。

本仓库是 LuBan Agent 的完整解决方案，包含共享核心库与两种客户端形态：

| 项目 | 形态 | 说明 |
|------|------|------|
| **[LubanAgentCore](LubanAgentCore/)** | 核心类库 | Agent 运行时、工具系统、技能系统、会话存储、RAG 检索、工作区管理 |
| **[LubanAgentCli](LubanAgentCli/)** | 命令行 TUI | 基于 Terminal.Gui v2 的全屏终端界面（Claude Code 风格），可安装为 dotnet tool |
| **[LubanAgentCodex](LubanAgentCodex/)** | 桌面客户端 | 基于 Avalonia UI 的跨平台图形界面，三栏式布局 + 暗色主题 |

> 📖 各项目的详细使用说明请查看：[LubanAgentCli README](LubanAgentCli/README.md) | [LubanAgentCodex README](LubanAgentCodex/README.md)

### 你是否遇到过这些问题？

- 😫 想让 LLM 调用工具完成任务，但 MCP / Function Calling 的实现细节令人头疼？
- 😤 Skill 管理、工具注册、会话持久化各自需要单独实现，维护成本高？
- 😖 模型 Provider 切换困难——从 Provider A 换到 Provider B 需要重写大量代码？
- 😩 命令行不够直观，想要图形界面，又不想重复造一套 Agent 基础设施？

**LuBan Agent 为你提供完整的 AI Agent 基础设施**——核心能力沉淀在 LubanAgentCore 中，CLI 与 Codex 共用同一套运行时，一次实现，处处可用。

---

## ✨ 核心特性

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
| 🗄️ **数据库工具** | ADO.NET 直连执行 SQL（MySQL/PostgreSQL/SQL Server/SQLite），支持动态连接字符串 |
| 🔴 **Redis 工具** | 通过 redis-cli 执行 Redis 命令 |
| 🌍 **Web 工具** | 发送 HTTP 请求获取网页内容 |
| 🔍 **语义检索工具** | 索引本地代码/文档，按语义搜索相关片段 |

### 🎯 Skill 系统
- 内置九大核心技能（头脑风暴、代码审查、文档生成、代码重构、测试生成、代码解释、调试助手、Git 提交、技能发现），即插即用
- **文件化 Skill**：通过 SKILL.md 文件定义自定义 Skill，兼容 OpenCode 格式，项目级/用户级目录自动加载

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
- **编排扩展**：工作区 `.luban-agent/plans/*.json` 定义任务模板、`.luban-agent/roles/*.json` 定义自定义 SubAgent 角色

### 📂 工作区与知识库
- **工作区隔离**：每个工作区拥有独立的根目录、会话历史和配置目录（`.luban-agent/`）
- **RAG 知识库**：特殊工作区类型，支持文件索引与语义检索，自动检索增强问答
- **向量存储隔离**：不同工作区的索引数据完全隔离，互不串读
- **路径授权管理**：工作区授权与 PathGuard 集成，仅授权的工作区根目录可访问

---

## 🚀 快速开始

### 环境要求

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) 或更高版本

### 1. 克隆仓库

```bash
git clone https://github.com/yswenli/luban-agent.git
cd luban-agent
```

### 2. 安装 Playwright 浏览器（使用浏览器工具前必须安装）

```powershell
# 安装与 Microsoft.Playwright 1.61.0 匹配的浏览器版本
npx playwright@1.61.0 install chromium
```

> **注意**：浏览器版本必须与 Microsoft.Playwright 包版本匹配，当前项目使用的是 1.61.0。

### 3. 构建解决方案

```bash
dotnet build luban-agent.slnx
```

### 4. 选择你的客户端运行

#### 方式一：命令行 TUI（LubanAgentCli）

```bash
# 直接运行
dotnet run --project LubanAgentCli/LubanAgentCli.csproj

# 或安装为 dotnet 全局工具（推荐）
dotnet pack LubanAgentCli/LubanAgentCli.csproj -c Release -o ./artifacts
dotnet tool install -g LuBan.Agent.CLI --add-source ./artifacts

# 安装后在任意目录启动
luban-agent-cli
```

启动后进入 Terminal.Gui 全屏界面，输入普通文本直接与 Agent 对话，`/` 开头进入命令面板。

#### 方式二：桌面客户端（LubanAgentCodex）

```bash
dotnet run --project LubanAgentCodex/LubanAgentCodex.csproj
```

启动后选择工作区，即可在图形界面中与 Agent 对话。

### 5. 配置 AI Provider 并开始对话

在任一客户端中配置你的第一个 Provider：

```
> /provider -add
选择 Provider 类型:
  1. OpenAI
  2. Azure OpenAI
  3. DeepSeek
  4. Kimi (Moonshot)
  ...
请选择: 4
请输入 Kimi API Key: ********
✓ Provider 'Kimi' 已添加并保存
```

然后选择模型，开始你的第一次 Agent 对话：

```
> /model -switch
✓ 已选择模型: kimi:k3

你: 帮我查一下D盘下面有哪些目录

[调用工具]: list_directory
  参数 path: D:\
[工具结果]: Program Files, Users, Windows, ...

🤖 D盘下有以下目录：
1. Program Files
2. Users
3. Windows
...
```

> **安全提示**：API Key 输入时会隐藏显示（密码输入模式）；用户配置自动保存在 `%LocalAppData%\LuBan\AIAgent\config.json`

---

## 🏗️ 解决方案架构

```
luban-agent/
├── luban-agent.slnx          # 解决方案文件（XML 格式）
├── Directory.Build.props     # 全局构建属性
│
├── LubanAgentCore/           # 核心类库（net10.0）
│   ├── Agents/               # Agent 配置（Normal / RAG Profile）
│   ├── Configuration/        # 配置管理
│   ├── EmbeddingModels/      # AI 嵌入模型文件（bge-small-zh-v1.5）
│   ├── Entities/             # 数据实体
│   ├── Hosting/              # 宿主集成
│   ├── Infrastructure/       # 基础设施
│   ├── Models/               # 数据模型
│   ├── Repositories/         # 数据访问层
│   ├── Retrieval/            # 语义检索
│   ├── Services/             # 核心服务（Agent 宿主 / 会话 / 工作区）
│   └── Utils/                # 工具类
│
├── LubanAgentCli/            # 命令行 TUI（Terminal.Gui v2 · net10.0）
│   ├── App/                  # TUI 应用层（启动引导 + DI + 主题）
│   ├── Views/                # 视图层（纯渲染）
│   ├── ViewModels/           # MVVM ViewModel 层
│   ├── Models/               # Block 文档模型
│   ├── Commands/             # 命令实现
│   └── Program.cs            # 程序入口
│
├── LubanAgentCodex/          # 桌面客户端（Avalonia UI · net10.0）
│   ├── Views/                # Avalonia 视图
│   ├── ViewModels/           # MVVM ViewModel 层
│   ├── Services/             # UI 服务
│   ├── Styles/               # 样式主题
│   └── Program.cs            # 程序入口
│
└── docs/                     # 设计与规划文档
```

**架构说明**：

```
┌─────────────────┐  ┌─────────────────┐
│ LubanAgentCli   │  │ LubanAgentCodex │
│ (Terminal.Gui)  │  │   (Avalonia)    │
└────────┬────────┘  └────────┬────────┘
         │                    │
         └────────┬───────────┘
                  ▼
         ┌─────────────────┐
         │ LubanAgentCore  │  Agent 运行时 / 工具 / 技能 / 会话 / RAG / 工作区
         └────────┬────────┘
                  ▼
         ┌─────────────────┐
         │  LuBan.AIAgent  │  多模型路由 / MCP / 编排引擎
         └────────┬────────┘
                  ▼
     OpenAI / Kimi / DeepSeek / Claude / ... (20+ Providers)
```

- **UI 与业务分离**：CLI 与 Codex 只负责交互与渲染，全部 Agent 能力由 Core 提供
- **嵌入模型单一来源**：`bge-small-zh-v1.5.zip` 仅在 Core 中维护，经 ProjectReference 内容传播带入各客户端输出目录
- **统一配置**：两个客户端共享 `%LocalAppData%\LuBan\AIAgent\` 下的用户配置与会话数据库

---

## ⚙️ 配置说明

### 应用配置 (appsettings.json)

```json
{
  "LuBanAgent": {
    "DefaultModel": "openai:gpt-4o",
    "SystemPrompt": "你是一个智能助手。",
    "MaxToolLoopIterations": 10,
    "Tools": {
      "Browser":    { "Enabled": true, "Headless": false },
      "FileSystem": { "Enabled": true, "AllowedRoots": ["C:\\Work"] },
      "Script":     { "Enabled": true, "Shell": "cmd" },
      "Database":   { "Enabled": true },
      "Redis":      { "Enabled": true },
      "Web":        { "Enabled": true },
      "Retrieval":  { "Enabled": true, "ModelId": "bge-small-zh-v1.5" }
    },
    "Orchestration": {
      "Enabled": true,
      "AutoDetect": true,
      "MaxParallelism": 3,
      "MaxNodes": 20
    }
  }
}
```

### 用户配置 (%LocalAppData%\LuBan\AIAgent\config.json)

用户配置（Provider、自定义 Skill、规则等）自动保存在本地，两个客户端共享，重启后自动加载。

> 📖 完整配置项说明请查看各子项目的 README。

---

## 🔧 技术栈

| 组件 | 说明 |
|------|------|
| **.NET 10.0** | 目标框架 |
| **LuBan.AIAgent** | Agent 运行时框架（多模型路由 / MCP / 编排） |
| **Microsoft.Extensions.AI** | 统一聊天客户端抽象 |
| **Terminal.Gui 2.4.17** | CLI 全屏 TUI 框架（24-bit TrueColor，alt-screen） |
| **Avalonia 12.1.1** | Codex 跨平台桌面 UI 框架 |
| **CommunityToolkit.Mvvm** | MVVM 框架 |
| **Microsoft.Playwright** | 浏览器自动化引擎 |
| **Microsoft.ML.OnnxRuntime** | ONNX 模型推理（语义检索） |
| **SqlSugar / SQLite** | 会话与向量数据存储 |

---

## 💡 小贴士

- 🖥️ **两种客户端，一套核心**：CLI 适合终端党与服务器环境，Codex 适合喜欢图形界面的用户，配置与会话完全共享
- ⌨️ **CLI 快捷键**：`Esc` 取消任务、`Shift+Tab` 切换权限模式、`Tab` 切换任务视图、`Ctrl+Q` 退出
- 🛡️ **四模式权限**：Default / Plan / AcceptEdits / BypassPermissions，危险操作自动要求确认
- 💬 模型路由使用 `provider:model` 格式，支持 20+ 种 AI Provider
- 🧩 **多 Agent 编排**：AI 自动识别复合任务，拆解为 DAG 并由 SubAgent 串行/并行混合执行
- 📂 **工作区隔离**：每个工作区有独立的会话历史与配置目录（`.luban-agent/`）
- 🔍 **RAG 知识库**：索引本地文档后，对话时自动检索增强问答

---

## 🤝 相关项目

- **[LuBan.Framework](https://github.com/yswenli/luban-framework)** - LuBan 框架核心
- **[LuBan.AIAgent](https://www.nuget.org/packages/LuBan.AIAgent/)** - AI Agent 运行时
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
