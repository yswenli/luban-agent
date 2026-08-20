# LuBan Agent Codex

<div align="center">

**Cross-platform AI Coding Agent Desktop Client based on Avalonia UI**

*Empower LLMs with thinking, planning, tool calling, and autonomous execution capabilities*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12.1.1-blue.svg)](https://avaloniaui.net/)

[English](README.en.md) | [中文](README.md)

</div>

---

## 🌟 Why Choose LuBan Agent Codex?

Imagine: you just type "help me check what directories are under D drive" in the GUI, and AI automatically calls the file system tool to list all directories. Say "help me open Baidu and search for LuBan Framework", and AI will automatically launch the browser and complete the search.

This is not science fiction, this is **LuBan Agent Codex**.

### Have you encountered these problems?

- 😫 Want LLM to call tools to complete tasks, but the implementation details of MCP / Function Calling are headache?
- 😤 Skill management, tool registration, and session persistence each need to be implemented separately, with high maintenance costs?
- 😖 Difficult to switch model Providers—switching from Provider A to Provider B requires rewriting a lot of code?
- 😩 Lack of graphical interface—command line tools are not intuitive enough to manage complex tasks?

**LuBan Agent Codex provides you with a complete AI Agent desktop experience**, from Agent runtime, multi-model routing, skill system, tool system, session storage to graphical interface—ready to use out of the box.

---

## ✨ Core Features

### 🎨 Modern Graphical Interface
- **Classic three-column layout**: Left sidebar + Main content area + Bottom input area
- **Dark theme**: Professional dark color scheme, no fatigue during long coding sessions
- **Streaming output**: Real-time display of AI thinking process and tool call status
- **Smart scrolling**: Auto-follow output, support manual scrolling to view history

### 🤖 Multi-Model Routing
- **20+ AI Provider support**: OpenAI, Azure, DeepSeek, Kimi, GLM, Qwen, Doubao, Claude, Gemini, Ollama, MiniMax, Ark, Bailian, Hunyuan, MiMo, ERNIE, xAI Grok, Qianfan, Tencent TI, Huawei Pangu, AWS Bedrock, OpenRouter, and custom OpenAI-compatible APIs
- **Unified `provider:model` format**: One-click model switching, no code changes required
- **Dynamic routing**: LuBanChatClient automatically distributes to the corresponding Provider based on prefix

### 🛠️ 7 Built-in Tool Groups
| Tool Group | Capabilities |
|------------|--------------|
| 🌐 **Browser Tools** | Navigate, click, type, screenshot, get content (based on Playwright) |
| 📁 **File System Tools** | Read, write, list directories, with safe path restrictions |
| 🔧 **Script Execution Tools** | Execute Shell, Lua, Python scripts |
| 🗄️ **Database Tools** | ADO.NET direct SQL execution (MySQL/PostgreSQL/SQL Server/SQLite) |
| 🔴 **Redis Tools** | Execute Redis commands via redis-cli |
| 🌍 **Web Tools** | Send HTTP requests to get web content |
| 🔍 **Semantic Retrieval Tools** | Index local code/documents, search relevant fragments by semantics |

### 🎯 Skill System
Built-in nine core skills, plug and play:
- **Brainstorming**: Explore requirements and design before implementing features
- **Code Review**: Review code, find issues, provide improvement suggestions
- **Documentation Generation**: Generate code comments, README, API documentation
- **Code Refactoring**: Refactor code to improve code quality
- **Test Generation**: Automatically generate unit tests
- **Code Explanation**: Explain complex code logic
- **Debug Assistant**: Assist in debugging issues
- **Git Commit**: Generate standardized Git commit messages
- **Skill Discovery**: Automatically discover and recommend suitable skills

**File-based Skills**: Support defining custom Skills through SKILL.md files, compatible with OpenCode format, automatically loaded when placed in project-level or user-level directories.

### 🛡️ Security & Rule Engine
- **Path access rules**: Restrict file system access scope, prevent unauthorized operations
- **Dangerous operation confirmation**: Automatically require user confirmation before writing, deleting, executing scripts
- **Custom rules**: Support wildcard matching, flexibly control tool behavior

### 💾 Session Persistence
- Conversation history automatically saved to SQLite database
- Support long conversation compression (SummarizingChatReducer), context never lost
- Session statistics, Token count at a glance

### 🔌 MCP Protocol Support
- Built-in file system MCP client
- Support external MCP server hot loading
- Standard JSON-RPC protocol, seamless integration with ecosystem

### 🧩 Multi-Agent Task Orchestration
- **Complex task auto-decomposition**: Main Agent automatically decomposes complex tasks into DAG task graphs
- **Serial/parallel mixed orchestration**: Based on topological layering, same-layer nodes execute in parallel, cross-layer nodes execute serially
- **SubAgent scheduling**: Each DAG node executed by independent SubAgent, supporting tool group isolation
- **Context passing**: Nodes reference predecessor output through `{dep:xxx}` placeholders
- **Error handling**: Critical node failure skips successors, non-critical node failure continues execution

### 📂 Workspace & Knowledge Base
- **Workspace isolation**: Each workspace has independent root directory, session history, and configuration directory
- **Workspace configuration directory**: Automatically creates `.luban-agent/` under each workspace root, can place custom `skills`, `rules`, `mcps` configurations
- **Temporary file management**: Scripts, screenshots, intermediate files generated at runtime uniformly stored in `.luban-agent/temp/`, supports automatic cleanup of expired files
- **RAG Knowledge Base**: Special workspace type, supports file indexing and semantic retrieval, automatic retrieval-augmented Q&A
- **Vector storage isolation**: Index data from different workspaces completely isolated, no cross-reading
- **Path authorization management**: Workspace authorization integrated with PathGuard, only authorized workspace root directories accessible

---

## 🚀 Quick Start

### 1. Install Playwright Browser (Required before using browser tools)

```powershell
# Install browser version matching Microsoft.Playwright 1.61.0
npx playwright@1.61.0 install chromium
```

> **Note**: Browser version must match Microsoft.Playwright package version, current project uses 1.61.0.

### 2. Clone Source and Run

```bash
# Clone repository
git clone https://github.com/yswenli/luban-framework.git
cd luban-framework/luban-agent

# Run program
dotnet run --project LubanAgentCodex/LubanAgentCodex.csproj
```

### 3. Select Workspace

After startup, workspace picker will pop up:

```
┌─────────────────────────────────────────┐
│  Select Workspace                        │
├─────────────────────────────────────────┤
│  📁 MyProject                           │
│     D:\Projects\MyProject               │
│     Last active: 2026-08-20 10:30       │
│                                         │
│  📁 Docs                                │
│     D:\Documents\Docs                   │
│     Last active: 2026-08-19 15:20       │
│                                         │
│  [📂 Open Folder...]                    │
└─────────────────────────────────────────┘
```

Select existing workspace or click "Open Folder" to create new workspace.

### 4. Configure Your First AI Provider

Enter `/provider -add` in input box, follow prompts:

```
> /provider -add

Select Provider type:
  1. OpenAI
  2. Azure OpenAI
  3. DeepSeek
  4. Kimi (Moonshot)
  ...

Please select (1-23): 4
Please enter Kimi API Key: ********

✓ Provider 'Kimi' added and saved
```

### 5. Select Model and Start Conversation

```
> /model -switch

Configured Providers:
  1. Kimi

Please select Provider number: 1
Kimi supported models:
  1. k3
  2. k3-256k
  3. kimi-for-coding
  ...

Please select model number: 1
✓ Model selected: kimi:k3
```

### 6. Start Your First Agent Conversation

Directly enter question in input box, press Enter to send:

```
You: Help me check what directories are under D drive

🤖 Thinking...
⚙️ Calling tool: ListDirectoryAsync
   Parameters: path = D:\
✓ Tool execution complete

🤖 D drive has the following directories:
1. Program Files
2. Users  
3. Windows
...
```

---

## 📖 Command Overview

LuBan Agent Codex provides a concise and powerful command system, all commands start with `/`:

| Command | Shortcut | Description |
|---------|----------|-------------|
| `/help` | — | Display help information |
| `/clear` | — | Clear session history |
| `/mode [name]` | — | View or switch permission mode (default/plan/accept-edits/bypass) |
| `/provider` | `/p` | Manage AI Provider |
| `/model` | `/m` | Manage models |
| `/skill` | `/sk` | View and execute Skills |
| `/rule` | `/r` | View and manage rules |
| `/mcp` | `/mp` | View MCP clients |
| `/session` | `/se` | Manage conversation sessions |
| `/stats` | `/st` | Session and Token statistics |
| `/work` | `/w` | Workspace management |
| `/rag` | `/rg` | Knowledge base management |

### Sub-command Shortcuts

| Shortcut | Full Command | Applicable Scenarios |
|----------|--------------|---------------------|
| `-l` | `-list` | All management commands |
| `-a` | `-add` | All management commands |
| `-u` | `-update` | Provider/Model/Skill/Rule/MCP |
| `-d` | `-delete` | Provider/Model/Skill/Rule/MCP/Work/Rag |
| `-s` | `-switch` | All management commands |
| `-n` | `-new` | Session/Work/Rag |
| `-c` | `-clear` | Session |

**Examples**:
- `/p -l` = `/provider -list`
- `/st -d 7` = `/stats -days 7`
- `/work -n D:\MyProject` = Create workspace

---

## 🎨 Use Cases

### Scenario 1: File Management Assistant

```
You: Help me list all .cs files under src directory and count code lines

⚙️ Calling tool: ListDirectoryAsync
   Parameters: path = src
⚙️ Calling tool: ReadFileAsync
   Parameters: path = src\Program.cs
...

🤖 src directory has 15 .cs files, total 3,456 lines of code:
- Program.cs: 112 lines
- Services\ConsoleAppService.cs: 344 lines
...
```

### Scenario 2: Web Automation

```
You: Help me open Baidu and search for "LuBan Framework"

⚙️ Calling tool: NavigateAsync
   Parameters: url = https://www.baidu.com
⚙️ Calling tool: TypeTextAsync
   Parameters: selector = #kw, text = LuBan Framework
⚙️ Calling tool: ClickAsync
   Parameters: selector = #su

🤖 Search completed, current page title: LuBan Framework_Baidu Search
```

### Scenario 3: Code Review

```
> /skill code-review

Please enter content: public void Test() { var x = 1/0; }

📋 Code Analysis:
Found potential issues:
1. ⚠️ Line 1 has division by zero error
2. 💡 Recommend adding exception handling mechanism

Improvement suggestions:
...
```

### Scenario 4: Database Query

```
You: Help me query the latest 10 records in user table

⚙️ Calling tool: ExecuteQueryAsync
   Parameters: sql = SELECT * FROM users ORDER BY created_at DESC LIMIT 10
✓ Tool execution complete

🤖 Query results as follows:
...
```

### Scenario 5: Complex Task Auto-Orchestration

AI will automatically identify complex tasks and enable DAG orchestration:

```
You: Research LuBan-Framework and Luban-Agent two projects, compare their pros and cons, generate a comparison report

💭 Thinking process:
This is a complex task, needs to be decomposed into multiple subtasks for parallel execution...

🔄 Starting to plan task graph...
✓ Planning complete: Generated 4-node task graph

▶ Starting node execution: research-luban
  [SubAgent] Calling web_search to search LuBan framework features...
✓ Node complete: research-luban

▶ Starting node execution: research-langchain
  [SubAgent] Calling web_search to search LangChain framework features...
✓ Node complete: research-langchain
── Layer 1 execution complete (parallel) ──

▶ Starting node execution: compare
  [SubAgent] Performing comparative analysis based on predecessor results...
✓ Node complete: compare

🎯 Orchestration complete

🤖 Comparison report generated:

## LuBan vs LangChain Comparison

### Architecture Design
- **LuBan**: Based on .NET ecosystem, deeply integrated with Microsoft.Extensions.AI...
- **LangChain**: Python ecosystem, active community...

### Tool Integration
...

### Recommended Scenarios
...
```

### Scenario 6: Workspace Management

```
# Create workspace
> /work -new D:\MyProject
✓ Workspace created: MyProject - D:\MyProject

# List all workspaces
> /work -list
┌──────────┬──────┬──────────────┬──────┬────────────┬──────┐
│ Name     │ Type │ Root Dir     │ Sess │ Last Active│ Auth │
├──────────┼──────┼──────────────┼──────┼────────────┼──────┤
│ * MyProject │ Normal │ D:\MyProject │ 3    │ 2026-08-20 │ ✓    │
│   Docs      │ RAG  │ D:\Docs      │ 0    │ -          │ ✗    │
└──────────┴──────┴──────────────┴──────┴────────────┴──────┘

# Switch workspace
> /work -switch MyProject
✓ Switched to workspace: MyProject
  Root directory: D:\MyProject
```

### Scenario 7: RAG Knowledge Base Q&A

```
# 1. Create RAG knowledge base
> /rag -new D:\KnowledgeBase My Knowledge Base
✓ RAG knowledge base created: My Knowledge Base - D:\KnowledgeBase

# 2. Switch to RAG workspace
> /work -switch My Knowledge Base
✓ Switched to workspace: My Knowledge Base

# 3. Authorize and index files
> /rag -index *.md
Starting to index workspace: My Knowledge Base
✓ Indexing complete
  Scanned files: 25
  New files: 25
  Total chunks: 142

# 4. Retrieval test
> /rag -search how to configure workspace
Found 3 related results:
File: D:\KnowledgeBase\setup.md
Content: Workspace configuration requires...

# 5. Direct Q&A (automatic retrieval augmentation)
You: How to create a workspace?
🤖 According to knowledge base documents, creating workspace uses /work -new command...
```

---

## 🏗️ Project Structure

```
LubanAgentCodex/
├── App.axaml(.cs)              # Application entry point
├── Program.cs                  # Main entry point
├── Styles/
│   └── Colors.axaml            # Theme color resources
├── Services/
│   ├── AgentHostService.cs     # Agent host service
│   ├── FooterDataProvider.cs   # Footer data provider
│   └── StreamEvent.cs          # Stream event types
├── ViewModels/
│   ├── MainWindowViewModel.cs  # Main window ViewModel
│   └── Messages/               # Message data models
│       ├── AssistantMessageItem.cs
│       ├── UserMessageItem.cs
│       ├── ToolCallItem.cs
│       ├── ToolConfirmItem.cs
│       └── ThinkingMessageItem.cs
├── Views/
│   ├── MainWindow.axaml(.cs)   # Main window
│   ├── WorkspacePickerWindow   # Workspace picker
│   ├── RenameDialog            # Rename dialog
│   ├── SkillManageWindow       # Skill management window
│   ├── RuleManageWindow        # Rule management window
│   ├── MCPManageWindow         # MCP service management window
│   ├── ProviderManageWindow    # Provider management window
│   ├── WorkManageWindow        # Workspace management window
│   ├── RagManageWindow         # RAG management window
│   └── Controls/               # Custom controls
│       ├── Sidebar             # Left sidebar
│       ├── TitleBar            # Top title bar
│       ├── MessageStream       # Message stream
│       ├── InputBox            # Input box
│       ├── FooterBar           # Footer status bar
│       ├── UserMessageView     # User message
│       ├── AssistantMessageView# AI message
│       ├── ThinkingMessageView # Thinking process
│       ├── ToolCallCard        # Tool call card
│       ├── ConfirmCard         # Confirmation card
│       └── SystemMessageView   # System message
```

---

## ⚙️ Configuration

### Application Configuration (appsettings.json)

```json
{
  "LuBanAgent": {
    "DefaultModel": "openai:gpt-4o",
    "SystemPrompt": "You are an intelligent assistant.",
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
        "Keywords": [ "and", "simultaneously", "then", "also", "additionally", "moreover", "analyze and", "search and" ]
      }
    }
  }
}
```

### User Configuration (%LocalAppData%\LuBan\AIAgent\config.json)

User configuration (Provider, custom Skills, rules, etc.) automatically saved locally, automatically loaded on restart.

---

## 🎯 Advanced Features

### Dangerous Operation Confirmation

For dangerous operations (writing files, executing scripts, deleting data, etc.), the system will request user confirmation before execution:

```
You: Help me write a file to C:\temp\test.txt

⚠️ Confirmation Required
Tool: WriteFileAsync
Parameters:
  path: C:\temp\test.txt
  content: File content...

[✓ Allow] [ Allow All This Round] [✗ Deny]
```

**Operations requiring confirmation**:
- 📝 **File System**: Write files, delete files, create/delete directories
- 🔧 **Script Execution**: Execute Shell, Lua, Python scripts
- 🗄️ **Database**: INSERT, UPDATE, DELETE operations
- 🔴 **Redis**: SET, DELETE, FLUSHDB operations

### Session Management

```
# Create new session
> /session -new Project Discussion

# List all sessions
> /session -list

# Switch session
> /session -switch Project Discussion

# Clear all sessions (requires confirmation)
> /session -clear

# View statistics
> /stats -days 7
```

**Automatic session saving**:
- Conversation history automatically saved to SQLite database
- Database location: `%LocalAppData%\LuBan\AIAgent\ai_sessions.db`
- User messages and AI replies automatically saved
- Token count automatically tracked

### Custom Skills

LuBan Agent supports file-based Skills, create `SKILL.md` files in project-level or user-level directories, compatible with OpenCode format:

```
Storage locations (by priority):
  Project-level: <workspace>/.luban-agent/skills/<skill-id>/SKILL.md
  User-level: %LocalAppData%/LuBan/AIAgent/skills/<skill-id>/SKILL.md
```

**SKILL.md Format**:

```markdown
---
name: my-translator
description: "Translate text to English"
category: custom
---

# Translation Assistant

Please translate user-provided content to English.

## Requirements
- Maintain original tone and style
- Use idiomatic English expressions
- For technical terms, keep original and annotate in parentheses
```

### Custom Rules

```
# Add path protection rule
> /rule -add

Please enter rule ID: protect-system
Please enter rule name: Protect system directory
Please enter ActionTypePattern (default *): file-write
Please enter TargetPattern (default *): C:\Windows\*
Please enter Action (allow/deny): deny
Please enter priority (default 100): 100
✓ Custom rule 'Protect system directory' (protect-system) added
```

### MCP External Servers

```
# Add external MCP server
> /mcp -add

Please enter server name: github
Please enter description: GitHub integration
Please enter startup command (e.g., npx): npx
Please enter command arguments (space-separated, optional): -y @modelcontextprotocol/server-github
✓ External MCP server 'github' added

# Connect to MCP server
> /mcp -connect github
Connecting to github...
✓ Connection successful

# View available tools
> /mcp -tools github
github available tools:
  - create_issue: Create GitHub Issue
  - search_repositories: Search repositories
  ...
```

---

## 🔧 Tech Stack

| Component | Description |
|-----------|-------------|
| **Avalonia 12.1.1** | Cross-platform UI framework (.NET 10.0, dark theme) |
| **Microsoft.Agents.AI.Foundry** | Agent runtime framework |
| **Microsoft.Extensions.AI** | Unified chat client abstraction |
| **Microsoft.Playwright** | Browser automation engine |
| **LuBan.DI** | Dependency injection integration |
| **LuBan.Common** | Base interface and utility definitions |
| **Microsoft.ML.OnnxRuntime** | ONNX model inference (semantic retrieval) |
| **SQLite** | Session and vector data storage |
| **CommunityToolkit.Mvvm** | MVVM architecture support |

---

## 💡 Tips

- 🖥️ **Graphical Interface**: Launch enters Avalonia desktop application, enter plain text to directly converse with Agent
- ⌨️ **Shortcuts**: `Enter` send message, `Ctrl+Enter` newline, `Shift+Tab` switch permission mode
- 🛡️ **Four Permission Modes**: `Shift+Tab` cycle through Default / Plan / AcceptEdits / BypassPermissions, footer displays in real-time
- 🎨 **Collapse/Expand**: Thinking process and tool calls collapsed by default, click to expand and view details
- 📜 **Smart Scrolling**: Streaming output auto-follows; manual scroll up breaks follow
- 🌐 **Multi-Provider Support**: Supports 20+ AI Providers, unified `provider:model` format
- 🛠️ **7 Built-in Tool Groups**: Cover browser automation, file operations, script execution, database, Redis, web requests, semantic retrieval
- ⚠️ **Dangerous Operation Confirmation**: Automatically requires user confirmation for write, delete, execute and other dangerous operations
- 🔒 **Path Authorization**: After workspace authorization, AI Agent can access root directory and its subdirectories
- 🧩 **Multi-Agent Orchestration**: AI automatically identifies complex tasks, decomposes into DAG and executes with SubAgent in serial/parallel mix
- 📂 **Workspace Isolation**: Each workspace has independent session history and configuration directory

---

## 🤝 Related Projects

- **[LuBan.Framework](https://github.com/yswenli/luban-framework)** - LuBan framework core
- **[LuBan.DI](https://www.nuget.org/packages/LuBan.DI/)** - Dependency injection container
- **[LuBan.AIFlow](https://www.nuget.org/packages/LuBan.AIFlow/)** - AI workflow engine
- **[LuBan.Web.Core](https://www.nuget.org/packages/LuBan.Web.Core/)** - Web core components
- **[LuBan.Agent.CLI](../LubanAgentCli/README.md)** - Command line version

---

## 📄 License

MIT License

---

## 👤 Author

**yswenli**
- 📧 Email: yswenli@outlook.com
- 🐙 GitHub: [@yswenli](https://github.com/yswenli)

---

<div align="center">

**⭐ If this project is helpful to you, please give it a Star! ⭐**

Made with ❤️ by yswenli

</div>
