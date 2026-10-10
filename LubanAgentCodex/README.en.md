# LuBan Agent Codex

<div align="center">

**Cross-platform AI Coding Agent Desktop Client based on Avalonia UI**

*Empower LLMs with thinking, planning, tool calling, and autonomous execution capabilities*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12.1.2-blue.svg)](https://avaloniaui.net/)

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

### 🛠️ 10 Built-in Tool Groups
| Tool Group | Capabilities |
|------------|--------------|
| 🌐 **Browser Tools** | Navigate, click, type, screenshot, get content (based on Playwright) |
| 📁 **File System Tools** | Read, write, list, search, copy, move, delete; allowed directly inside the workspace, paths outside require confirmation |
| 🔧 **Script Execution Tools** | Execute Shell, Lua, Python scripts |
| 🌍 **Web Tools** | Send HTTP requests to get web content |
| 🔍 **Semantic Retrieval Tools** | Index local code/documents, search relevant fragments by semantics |
| 🗜️ **Context Compaction Tools** | Compact the current session's conversation history to free token budget (LLM-invocable) |
| 🧠 **Local Memory Tools** | Store, query, and manage long-term memory |
| 🔌 **MCP External Tools** | Connect external MCP servers and merge their tools into the Agent toolset |
| 🧩 **Orchestration Tools** | Decompose composite tasks into a DAG and dispatch SubAgents in parallel/serial |
| 📚 **Wiki Knowledge Base Tools** | Read/write/delete wiki pages, vector search, health check (opt-in) |

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

### 📎 Attachment Support (Images + Text Files)
- **Multiple input paths**: the 📎 button, dropping files onto the window, and `Ctrl+V` for clipboard files or screenshots
- **Thumbnail preview**: the attachment bar and message area render thumbnails, file names, and sizes; click to view full size (Esc / click to close)
- **Size tiers**: up to 5 per message; text ≤50KB is inlined, >50KB injects only a path hint for the model to read on demand
- **Model-aware**: with a non-vision model the 📎 button stays enabled but images are blocked with a prompt; automatically disabled at the 5-item cap
- **Async processing**: attachments are preprocessed on a background thread so large images don't freeze the UI; failed attachments are dropped individually with a notice

### 🛡️ Security & Rule Engine
- **Path access rules**: Paths inside the workspace are allowed directly; paths outside the workspace are allowed after confirmation
- **Dangerous operation confirmation**: Require user confirmation before out-of-workspace access, writing, deleting, or executing scripts, with this-time/turn/session grant tiers
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
- **Memory context injection**: The orchestration entry builds workspace long-term memory and rule context once, then injects it into every SubAgent's system prompt
- **Error handling**: Critical node failure skips successors, non-critical node failure continues execution
- **Heuristic pre-filter**: task planning is only entered when the input length falls within `[MinLength, MaxLength]` and a composite keyword matches, saving LLM calls and preventing ordinary long questions from being misclassified as composite tasks

### 📂 Workspace & Knowledge Base
- **Workspace isolation**: Each workspace has independent root directory, session history, and configuration directory
- **Stable workspace ID**: The workspace ID is derived from the normalized root path, so CLI and Codex yield the same ID for the same directory and thus share long-term memory across hosts
- **Workspace configuration directory**: Automatically creates `.luban-agent/` under each workspace root, can place custom `skills`, `rules`, `mcps` configurations
- **Temporary file management**: Scripts, screenshots, intermediate files generated at runtime uniformly stored in `.luban-agent/temp/`, supports automatic cleanup of expired files
- **LLM Wiki Knowledge Base**: Special workspace type, supports file indexing and semantic retrieval, automatic retrieval-augmented Q&A
- **Vector storage isolation**: Index data from different workspaces completely isolated, no cross-reading
- **Path authorization management**: Paths inside the workspace are allowed directly; paths outside the workspace are allowed after user confirmation, with grants remembered per path root (this-time/this-turn/this-session tiers)

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
git clone https://github.com/yswenli/luban-agent.git
cd luban-agent

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
| `/llmwiki` | `/lw`, `/rag`, `/rg`, `/wiki`, `/wk` | LLM Wiki knowledge base management |

### Sub-command Shortcuts

| Shortcut | Full Command | Applicable Scenarios |
|----------|--------------|---------------------|
| `-l` | `-list` | All management commands |
| `-a` | `-add` | All management commands |
| `-u` | `-update` | Provider/Model/Skill/Rule/MCP |
| `-d` | `-delete` | Provider/Model/Skill/Rule/MCP/Work/LLM Wiki |
| `-s` | `-switch` | All management commands (`-search` in `/llmwiki`) |
| `-n` | `-new` | Session/Work/LLM Wiki |
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

### Scenario 4: Context Compaction

```
You: Help me compact the current conversation context

⚙️ Calling tool: CompactContextAsync
✓ Tool execution complete

🤖 Context compacted: archived 12 messages, kept 8 active messages
```

### Scenario 5: Complex Task Auto-Orchestration

When the model judges the input to be a composite task it first calls `plan_task` to build a task graph, then calls `run_orchestration` to execute the DAG:

```
You: Research LuBan-Framework and Luban-Agent two projects, compare their pros and cons, generate a comparison report

💭 Thinking process:
This is a complex task, needs to be decomposed into multiple subtasks for parallel execution...

🔄 Starting to plan task graph...
✓ Planning complete: Generated 4-node task graph

▶ Starting node execution: research-luban
  [SubAgent] Calling FetchUrlAsync to fetch LuBan framework docs...
✓ Node complete: research-luban

▶ Starting node execution: research-langchain
  [SubAgent] Calling FetchUrlAsync to fetch LangChain framework docs...
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
│   Docs      │ LLM Wiki  │ D:\Docs      │ 0    │ -          │ ✗    │
└──────────┴──────┴──────────────┴──────┴────────────┴──────┘

# Switch workspace
> /work -switch MyProject
✓ Switched to workspace: MyProject
  Root directory: D:\MyProject
```

### Scenario 7: LLM Wiki Knowledge Base Q&A

```
# 1. Create LLM Wiki knowledge base
> /llmwiki -new D:\KnowledgeBase My Knowledge Base
✓ LLM Wiki knowledge base created: My Knowledge Base - D:\KnowledgeBase
# The name can be omitted: /llmwiki -new D:\KnowledgeBase → defaults to llmwiki

# 2. Switch to LLM Wiki workspace
> /work -switch My Knowledge Base
✓ Switched to workspace: My Knowledge Base

# 3. Authorize and index files
> /llmwiki -index *.md
Starting to index workspace: My Knowledge Base
✓ Indexing complete
  Scanned files: 25
  New files: 25
  Total chunks: 142

# 4. Retrieval test
> /llmwiki -search how to configure workspace
Found 3 related results:
File: D:\KnowledgeBase\setup.md
Content: Workspace configuration requires...

# 5. Direct Q&A (automatic retrieval augmentation)
You: How to create a workspace?
🤖 According to knowledge base documents, creating workspace uses /work -new command...
```

### Scenario 8: Safely Reading Files Outside the Workspace (Three-tier Authorization)

By default only paths inside the workspace are allowed without confirmation. When the Agent needs to reference material outside the workspace, it pops up a **modal confirmation dialog**, and you decide how wide to open the scope:

```
You: Take a look at D:\Shared\api-spec.md and implement the matching client

┌─ Tool Confirmation ──────────────────────────────────┐
│ ⚠ Confirm whether to run this tool                    │
│ ReadFileAsync                                        │
│ { "path": "D:\\Shared\\api-spec.md" }                │
│   [✗ Deny] [Allow All This Turn] [Allow All This Session] [✓ Allow] │
└──────────────────────────────────────────────────────┘

(choose "Allow All This Session")

Message stream: Allowed Read File
✓ Granted this-session access to D:\Shared
📄 Read api-spec.md (216 lines)
🤖 Sure — generating the client per the interface contract...
```

| Tier | Scope | Memory |
|------|-------|--------|
| `✓ Allow` | this call only | not remembered |
| `Allow all this turn` | same-type tools within this turn | expires after each turn |
| `Allow all this session` | same-type tools for the whole session | remembered per path root (file → parent dir, dir → itself) |

> If the grant root lands on a drive root (e.g. `D:\`) it is not remembered, preventing one grant from opening an entire disk. The main window is non-interactive until you choose; Esc or ending the session equals "Deny".

### Scenario 9: Plan Mode — Read First, Produce a Plan, Change Nothing

In Plan mode the Agent only **reads** context to produce a plan; every write is recorded as a planned item and never executed:

```
> /mode plan
✓ Permission mode switched: plan

You: Refactor the src/Logging module

🔍 [Plan] Reading context...
   ReadFileAsync      src\Logging\Logger.cs    ✓ read-only, allowed
   ListDirectoryAsync src\Logging\             ✓ read-only, allowed
   WriteFileAsync     src\Logging\Logger.cs    ⏸ recorded as planned (Plan mode does not execute)

🤖 Plan:
   1. Extract an ILogger interface
   2. Switch file writes to async batched flush
   3. Add unit tests
   Switch back to default mode to execute after you confirm.
```

### Scenario 10: Deletes Always Ask — Authorization Is Never "Permanent"

Delete-class tools (`DeleteFileAsync` / `DeleteDirectoryAsync` / `DeletePageAsync`) intentionally **never remember** authorization; even if you pick "allow all this turn/session", the next delete still pops up a confirmation:

```
You: Clean up the temp directory

┌─ Tool Confirmation ──────────────────────────────────┐
│ ⚠ Confirm whether to run this tool                    │
│ DeleteDirectoryAsync                                 │
│ { "path": ".luban-agent\\temp" }                     │
│   [✗ Deny] [Allow All This Turn] [Allow All This Session] [✓ Allow] │
└──────────────────────────────────────────────────────┘

(choose "Allow All This Turn")

✓ Deleted .luban-agent\temp
ℹ Delete operations do not remember authorization; the next delete will confirm again
```

### Scenario 11: Four Permission Modes, One-key Cycling

Switch among the four modes with `/mode` or `Shift+Tab`:

```
> /mode
Current permission mode: default
Available modes: default / plan / accept-edits / bypass (use Shift+Tab to cycle)

> /mode accept-edits
✓ Permission mode switched: accept-edits
```

| Mode | Behavior |
|------|----------|
| `default` | In-workspace/authorized scope is confirmation-free; out-of-workspace paths, deletes, script execution, etc. require confirmation |
| `plan` | Read-only tools within authorized scope are allowed to produce a plan; everything else is only recorded as planned items |
| `accept-edits` | Edit-class operations within authorized scope (with a path, non-delete) are confirmation-free; scripts and deletes still confirm |
| `bypass` | Skip all tool confirmations (requires a second confirmation when switching; use with caution) |

### Scenario 12: Plug in External MCP Tools to Extend Capabilities On Demand

Beyond built-in tools, you can bring external MCP server tools into the Agent toolset via the **MCP management window** or the `/mcp` command:

```
> /mcp -list
Status     Type   Name         Description
[connected] builtin filesystem  Built-in filesystem MCP client

> /mcp -add                       # interactive form for name / command / args
✓ External MCP server 'github' added
  Use /mcp connect github to connect

> /mcp -connect github
Connecting github...
✓ Connected

> /mcp -tools github
  - list_repos: list repositories
  - create_issue: create an issue
  ...

You: Create a bug issue in the luban-agent repo
🤖 [MCP tool call] github.create_issue ...
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
│       └── ThinkingMessageItem.cs
├── Views/
│   ├── MainWindow.axaml(.cs)   # Main window
│   ├── RenameDialog            # Rename dialog
│   ├── SkillManageWindow       # Skill management window
│   ├── RuleManageWindow        # Rule management window
│   ├── MCPManageWindow         # MCP service management window
│   ├── SettingsWindow          # Unified settings center (skills/rules/MCP/providers/models)
│   ├── WorkManageWindow        # Workspace management window
│   ├── LlmWikiManageWindow         # LLM Wiki management window
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
    "Orchestration": {
      "Enabled": true,
      "PlannerReasoningEffort": "None",
      "MaxParallelism": 5,
      "MaxNodes": 20,
      "DefaultNodeTimeoutSeconds": 300
    }
  }
}
```

### User Configuration (%LocalAppData%\LuBan\AIAgent\config.json)

User configuration (Provider, custom Skills, rules, etc.) automatically saved locally, automatically loaded on restart.

---

## 🎯 Advanced Features

### Dangerous Operation Confirmation

For sensitive operations (out-of-workspace path access, writing files, executing scripts, deleting data, etc.), the system shows a **modal confirmation dialog** before execution; in-workspace paths are allowed directly:

```
You: Help me write a file to C:\temp\test.txt

┌─ Tool Confirmation ──────────────────────────────────┐
│ ⚠ Confirm whether to run this tool                    │
│ WriteFileAsync                                        │
│ {                                                     │
│   "path": "C:\\temp\\test.txt",                        │
│   "content": "File content..."                         │
│ }                                                     │
│   [✗ Deny] [Allow All This Turn] [Allow All This Session] [✓ Allow] │
└───────────────────────────────────────────────────────┘

Message stream record: Allowed 写入文件
```

The dialog is an application-modal window: the main window is not interactive until you choose; Esc or aborting the session is treated as "Deny".

**Grant tiers**: `✓ Allow` allows this call only; `Allow All This Turn` is remembered for this turn (reset each turn); `Allow All This Session` is remembered for this session (process lifetime). Grants are remembered per path root (parent directory for files, the directory itself for directories); when the grant root resolves to a drive root (e.g. `D:\`), it is not remembered.

**Operations requiring confirmation**:
- 📝 **File System**: read/write/copy/move for out-of-workspace paths, and all delete operations (deletion is never remembered and always confirms)
- 🔧 **Script Execution**: Execute Shell, Lua, Python scripts

Read-only and normal edit operations inside the workspace need no confirmation; in Plan mode only in-workspace read-only tools are allowed directly.

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
- ⌨️ **Shortcuts**: `Enter` send message, `Ctrl+Enter` newline, `Ctrl+V` paste attachments (files/screenshots), `Shift+Tab` switch permission mode
- 🛡️ **Four Permission Modes**: `Shift+Tab` cycle through Default / Plan / AcceptEdits / BypassPermissions, footer displays in real-time
- 🎨 **Collapse/Expand**: Thinking process and tool calls collapsed by default, click to expand and view details
- 📜 **Smart Scrolling**: Streaming output auto-follows; manual scroll up breaks follow
- 🌐 **Multi-Provider Support**: Supports 20+ AI Providers, unified `provider:model` format
- 🛠️ **10 Built-in Tool Groups**: Cover browser automation, file operations, script execution, web requests, semantic retrieval, context compaction, local memory, MCP external tools, multi-agent orchestration, and the LLM Wiki knowledge base (opt-in)
- ⚠️ **Dangerous Operation Confirmation**: Requires user confirmation for out-of-workspace path access and sensitive operations (write, delete, execute), with this-time/turn/session grant tiers
- 🔒 **Path Authorization**: In-workspace paths need no confirmation; out-of-workspace paths are allowed after confirmation, with grants remembered per path root
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
