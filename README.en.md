# LuBan Agent CLI

<div align="center">

**AI Agent Command-Line Tool Based on Microsoft Agent Framework**

*Empowering Large Language Models with Thinking, Planning, Tool-Calling, and Autonomous Execution*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Terminal.Gui](https://img.shields.io/badge/Terminal.Gui-2.4.17-blue.svg)](https://gui-cs.github.io/Terminal.Gui/)

English | [中文](README.md)

</div>

---

## 🌟 Why LuBan Agent?

Imagine this: You simply say "Show me what directories are on drive D", and the AI automatically invokes the filesystem tool to list all directories. Then you say "Open Baidu and search for LuBan Framework", and the AI launches a browser and completes the search.

This isn't science fiction—this is **LuBan Agent**.

### Are You Facing These Challenges?

- 😫 Want LLMs to call tools and complete tasks, but struggling with MCP / Function Calling implementation details?
- 😤 Managing Skills, tool registration, and session persistence separately, leading to high maintenance costs?
- 😖 Difficulty switching model providers—rewriting tons of code just to go from Provider A to Provider B?
- 😩 Lack of middleware mechanisms—logging, policy control, and permission interception are hard to extend?

**LuBan Agent provides complete AI Agent infrastructure**—from Agent runtime, multi-model routing, skill system, tool system, session storage to middleware pipelines—ready to use out of the box.

---

## ✨ Core Features

### 🤖 Multi-Model Routing
- **20+ AI Provider Support**: OpenAI, Azure, DeepSeek, Kimi, GLM, Qwen, Doubao, Claude, Gemini, Ollama, MiniMax, Volcengine Ark, Alibaba Bailian, Tencent Hunyuan, Xiaomi MiMo, Baidu ERNIE, xAI Grok, Baidu Qianfan, Tencent TI Platform, Huawei Pangu, AWS Bedrock, OpenRouter, plus custom OpenAI-compatible API
- **Multiple Endpoints**: Some providers offer multiple API endpoints (e.g., Kimi has domestic, overseas, and coding-specific addresses), selectable during setup
- **Unified `provider:model` Format**: Switch models with one command, no code changes needed
- **Dynamic Routing**: LuBanChatClient automatically dispatches to the corresponding provider based on prefix

### 🛠️ 7 Built-in Tool Groups
| Tool Group | Capabilities |
|------------|--------------|
| 🌐 **Browser Tools** | Navigate, click, type, screenshot, get content (powered by Playwright) |
| 📁 **FileSystem Tools** | Read, write, list directories with secure path restrictions |
| 🔧 **Script Execution Tools** | Execute Shell, Lua, Python scripts |
| 🗄️ **Database Tools** | Execute SQL via ADO.NET direct connections (MySQL/PostgreSQL/SQL Server/SQLite), with dynamic connection string support |
| 🔴 **Redis Tools** | Execute Redis commands via redis-cli |
| 🌍 **Web Tools** | Send HTTP requests to fetch web content |
| 🔍 **Semantic Retrieval Tools** | Index local code/documents and search by semantic similarity |

### 🎯 Skill System
Nine core built-in skills, plug and play:
- **Brainstorming**: Explore requirements and design before implementing features
- **Code Review**: Review code, identify issues, and provide improvement suggestions
- **Documentation**: Generate code comments, README files, API documentation
- **Code Refactoring**: Refactor code to improve quality
- **Test Generation**: Automatically generate unit tests
- **Code Explanation**: Explain complex code logic
- **Debug Assistant**: Assist with debugging issues
- **Git Commit**: Generate standardized Git commit messages
- **Skill Discovery**: Automatically discover and recommend suitable skills

**Activate Skill in Conversation**: Type `/skill -switch` in `/agi` or `/browse` conversation and select a Skill; it applies only to the next input and is automatically cleared afterwards.

**File-based Skills**: Support defining custom Skills via SKILL.md files, compatible with OpenCode format. Place them in project-level or user-level directories for automatic loading.

### 🛡️ Security & Rule Engine
- **Path Access Rules**: Restrict filesystem access scope, prevent unauthorized operations
- **Dangerous Operation Confirmation**: Automatically request user confirmation before write, delete, or execute operations
- **Custom Rules**: Support wildcard matching for flexible tool behavior control

### 💾 Session Persistence
- Conversation history automatically saved to SQLite database
- Long conversation compression (SummarizingChatReducer) ensures context is never lost
- Session statistics and Token counting at a glance

### 🔌 MCP Protocol Support
- Built-in filesystem MCP client
- Hot-loading support for external MCP servers
- Standard JSON-RPC protocol, seamlessly integrated with the ecosystem

### 🧩 Multi-Agent Task Orchestration
- **Composite Task Decomposition**: Main Agent automatically identifies complex tasks and decomposes them into DAG task graphs
- **Serial/Parallel Hybrid Orchestration**: Layer-based topological sort, parallel within same layer, serial across layers
- **SubAgent Scheduling**: Each DAG node executed by independent SubAgent with tool group isolation
- **Context Passing**: Nodes reference predecessor outputs via `{dep:xxx}` placeholders
- **Error Handling**: Critical node failure skips successors, non-critical failure continues execution
- **Seamless Integration**: Automatically triggered in `/agi` conversation, no manual command switching needed
- **Orchestration Extensions**: workspace `.luban-agent/plans/*.json` for task templates and `.luban-agent/roles/*.json` for custom SubAgent roles; `Orchestration:PlannerModel` and node `ModelName` support `provider:model` multi-model routing
- **Heuristic Pre-Filter**: short inputs without composite keywords skip task planning, saving LLM calls

### 📂 Workspace & Knowledge Base
- **Workspace Isolation**: Each workspace has its own root directory, session history, and configuration directory
- **Workspace Config Directory**: Automatically creates `.luban-agent/` under workspace root for custom `skills`, `rules`, `mcps` configurations
- **RAG Knowledge Base**: Special workspace type supporting file indexing and semantic retrieval with auto-retrieval-augmented Q&A
- **Vector Store Isolation**: Index data from different workspaces is completely isolated, no cross-workspace data leaks
- **Path Authorization Management**: Workspace authorization integrates with PathGuard; only authorized workspace root directories are accessible
- **Default Config Generation**: RAG workspaces automatically generate default `rag-config.json` upon creation

---

## 🚀 Quick Start

### 1. Install Playwright Browser (Required Before Using Browser Tools)

```powershell
# Install browser version matching Microsoft.Playwright 1.61.0
npx playwright@1.61.0 install chromium
```

> **Note**: Browser version must match the Microsoft.Playwright package version. This project currently uses 1.61.0.

### Option 1: Clone and Run from Source

```bash
# Clone the repository
git clone https://github.com/yswenli/luban-framework.git
cd luban-framework/luban-agent

# Run the program
dotnet run
```

### Option 2: Install as dotnet tool (Recommended)

Once installed as a .NET global tool, you can invoke `luban-agent-cli` from any directory without cloning the source code.

#### Install from NuGet (after publishing)

```bash
dotnet tool install -g LuBan.Agent.CLI
```

#### Install from Local Source

```bash
# 1. Clone the repository
git clone https://github.com/yswenli/luban-framework.git
cd luban-framework/luban-agent

# 2. Pack
dotnet pack -c Release -o ./artifacts

# 3. Install globally
dotnet tool install -g LuBan.Agent.CLI --add-source ./artifacts
```

#### Usage After Installation

```bash
# Launch full-screen TUI interface from any directory (requires an interactive terminal; no redirects)
luban-agent-cli
```

> **TUI Migration Complete**: The interface has been fully migrated from Console/Spectre hybrid to Terminal.Gui v2 full-screen TUI (Claude Code style).
> Three-region layout (conversation / footer / input), Agent streaming conversation loop, four-mode permission system,
> inline command panel, footer metadata, and Agent View multi-session are all in place.
> Agent auto-initializes on first input; `Esc` cancels running task, `Shift+Tab` cycles permission modes,
> `Tab` toggles conversation / task views.

#### Configuration Files

- **Application config** (`appsettings.json`): Always loaded first from the tool installation directory; an `appsettings.json` in the current working directory can override it
- **User config** (Providers, Skills, rules, etc.): Automatically saved to `%LocalAppData%\LuBan\AIAgent\config.json`
- **Session data**: Saved to `%LocalAppData%\LuBan\AIAgent\ai_sessions.db`

#### Update & Uninstall

```bash
# Update (from NuGet)
dotnet tool update -g LuBan.Agent.CLI

# Update (from local source)
dotnet pack -c Release -o ./artifacts
dotnet tool update -g LuBan.Agent.CLI --add-source ./artifacts

# Uninstall
dotnet tool uninstall -g LuBan.Agent.CLI
```

#### Verify Installation

```bash
# List installed tools
dotnet tool list -g

# You should see output similar to:
# Package Id         Version   Commands
# -----------------------------------------
# LuBan.Agent.CLI    1.0.0     luban-agent-cli
```

### 3. Configure Your First AI Provider

```
> /provider -add
Select Provider type:
  1. OpenAI
  2. Azure OpenAI
  3. DeepSeek
  4. Kimi (Moonshot)
  5. Zhipu GLM
  6. Qwen
  7. Doubao
  8. Claude
  9. Google Gemini
  10. Ollama (Local)
  11. MiniMax
  12. Volcengine Ark
  13. Alibaba Bailian
  14. Tencent Hunyuan
  15. Xiaomi MiMo
  16. Baidu ERNIE
  17. xAI Grok
  18. Baidu Qianfan
  19. Tencent TI Platform
  20. Huawei Pangu
  21. AWS Bedrock
  22. OpenRouter
  23. Custom OpenAI-Compatible API
Select (1-23): 4
Enter Kimi API Key: ********

Kimi API endpoint selection:
  1. Domestic (https://api.moonshot.cn/v1) - Recommended
  2. Overseas (https://api.moonshot.ai/v1)
  3. Coding-specific (https://api.kimi.com/coding/v1)
Select (1-3): 1
✓ Provider 'Kimi' added and saved
  Supported models: k3, k3-256k, kimi-for-coding, kimi-for-coding-highspeed
```

> **Security Note**: API Key input is hidden (password input mode)

### 4. Select Model and Start Chatting

```
> /model -switch
Configured Providers:
  1. OpenAI

Select Provider number: 1
OpenAI supported models:
  1. gpt-4.1
  2. gpt-4.1-mini
  3. gpt-4.1-nano
  4. gpt-4o
  ...

Select model number: 4
✓ Selected model: openai:gpt-4o
```

### 5. Start Your First Agent Conversation

```
> /agi

You: Show me what directories are on drive D

⠋ Calling tool: ListDirectoryAsync
⠙ Thinking...
⠹ Generating response...

[Calling tool]: list_directory
  Parameter path: D:\
[Tool result]: Program Files, Users, Windows, ...

🤖 Drive D contains the following directories:
1. Program Files
2. Users  
3. Windows
...
```

> **Real-time Status Display**: During AI conversations, dynamic spinner animations and real-time status are shown:
> - Thinking...
> - Calling tool: {tool name}
> - Tool execution completed, generating response...
> - Response generation completed

---

## 📖 Command Reference

LuBan Agent provides a simple yet powerful command system:

### Direct Command-Line Execution

After the TUI migration, all commands are invoked interactively within the TUI full-screen interface using the `/` prefix. Direct command-line argument execution (e.g., `luban-agent-cli /se -l`) is planned as a future enhancement.

### Command Input (TUI Full-Screen Interface)

On launch, enters a Terminal.Gui full-screen alt-screen interface with a top-to-bottom layout: conversation area (scrollable), footer (mode/directory/git/token), and input area (bottom):

- **Enter** — Submit input (plain text → Agent; `/` prefix → command panel)
- **Ctrl+Q** — Force quit TUI
- **Ctrl+L** — Redraw screen
- **Esc** — Cancel running Agent task
- **Shift+Tab** — Cycle permission modes (Default → Plan → AcceptEdits → BypassPermissions)
- **Tab** — Toggle conversation / Agent task view
- **Mouse** — Click blocks to collapse/expand; scroll wheel; Shift+drag for native terminal selection

### Command List

| Command | Shorthand | Description |
|---------|-----------|-------------|
| `/help` | — | Show help (TUI-ready) |
| `/clear` | — | Clear conversation history (TUI-ready) |
| `/mode [name]` | — | View or switch permission mode (default/plan/accept-edits/bypass) (TUI-ready) |
| `/exit` / `/quit` | — | Exit program |
| `/provider` | `/p` | Manage AI Providers (backend ready, inline UI planned) |
| `/model` | `/m` | Manage Models (backend ready, inline UI planned) |
| `/skill` | `/sk` | View and Execute Skills |
| `/rule` | `/r` | View and Manage Rules |
| `/mcp` | `/mp` | View MCP Clients |
| `/session` | `/se` | Manage Chat Sessions |
| `/agi` | `/a` | General Agent Conversation (auto-enabled, just type) |
| `/browse` | `/b` | Website-specific Agent Operations |
| `/stats` | `/st` | Session & Token Statistics |
| `/work` | `/w` | Workspace Management |
| `/rag` | `/rg` | Knowledge Base Management |
| `/rag` | `/rg` | Knowledge Base Management |

---

## 🎨 Use Cases

### Use Case 1: File Management Assistant

```
You: List all .cs files in the src directory and count lines of code

[Calling tool]: list_directory
  Parameter path: src
[Calling tool]: read_file
  Parameter path: src\Program.cs
...

🤖 The src directory contains 15 .cs files with a total of 3,456 lines of code:
- Program.cs: 112 lines
- Services\ConsoleAppService.cs: 344 lines
...
```

### Use Case 2: Web Automation

```
You: Open Baidu and search for "LuBan Framework"

[Calling tool]: NavigateAsync
  Parameter url: https://www.baidu.com
[Calling tool]: TypeTextAsync
  Parameter selector: #kw, text: LuBan Framework
[Calling tool]: ClickAsync
  Parameter selector: #su

🤖 Search completed. Current page title: LuBan Framework_百度搜索
```

### Use Case 3: Code Review

```
> /skill code-review

Enter content: public void Test() { var x = 1/0; }

Executing Skill: Code Review

📋 Code Analysis:
Potential issues found:
1. ⚠️ Division by zero error on line 1
2. 💡 Suggest adding exception handling

Improvement suggestions:
...
```

### Use Case 4: Database Query

```
You: Query the 10 most recent records from the users table

[Calling tool]: run_sql
  Parameter sql: SELECT * FROM users ORDER BY created_at DESC LIMIT 10
[Tool result]: ...

🤖 Query results:
...
```

### Use Case 5: Composite Task Auto-Orchestration

In `/agi` conversation, AI automatically identifies composite tasks and triggers DAG orchestration:

```
> /agi

👶 Research LuBan and LangChain frameworks, compare their pros and cons, and generate a comparison report

💭 Thinking:
This is a composite task that needs to be decomposed into parallel subtasks...

🔄 Planning task graph...
✓ Planning complete: Generated 4-node task graph

▶ Starting node: research-luban
  [SubAgent] Calling web_search for LuBan framework features...
✓ Node completed: research-luban

▶ Starting node: research-langchain
  [SubAgent] Calling web_search for LangChain framework features...
✓ Node completed: research-langchain
── Layer 1 completed (parallel) ──

▶ Starting node: compare
  [SubAgent] Comparing based on predecessor results...
✓ Node completed: compare
── Layer 2 completed ──

▶ Starting node: report
  [SubAgent] Generating comparison report...
✓ Node completed: report
── Layer 3 completed ──

🎯 Orchestration complete

🤖 Comparison report generated:

## LuBan vs LangChain Comparison

### Architecture
- **LuBan**: .NET ecosystem, deep integration with Microsoft.Extensions.AI...
- **LangChain**: Python ecosystem, active community...

### Tool Integration
...

### Recommended Scenarios
...
```

**Orchestration Notes**:

- AI automatically determines whether task decomposition is needed
- Each node executed by independent SubAgent with tool group isolation
- Same-layer nodes execute in parallel (e.g., researching two frameworks simultaneously), cross-layer nodes execute serially
- Context passed between nodes via `{dep:xxx}` placeholders
- Streaming progress output with real-time node status
- No manual command switching needed, naturally triggered in `/agi` conversation

### Use Case 6: Workspace Management

```
# Create workspace
> /work -new D:\MyProject
✓ Created workspace: MyProject - D:\MyProject

# List all workspaces
> /work -list
┌──────────┬──────┬──────────────┬──────┬────────────┬──────┐
│ Name     │ Type │ Root         │ Sess │ Last Active│ Auth │
├──────────┼──────┼──────────────┼──────┼────────────┼──────┤
│ * MyProject │ Normal │ D:\MyProject │ 3  │ 2026-07-31 │ ✓   │
│   Docs      │ RAG    │ D:\Docs      │ 0  │ -          │ ✗   │
└──────────┴──────┴──────────────┴──────┴────────────┴──────┘

# Switch workspace
> /work -switch MyProject
✓ Switched to workspace: MyProject
  Root: D:\MyProject
  Enter /agi to start working

# Authorize workspace access
> /work -authorize
═══ Workspace Authorization ═══
Workspace: MyProject
Root: D:\MyProject
⚠️  AI Agent will be authorized to access this directory and its subdirectories
Authorize? (y/N): y
✓ Workspace authorized
```

**Workspace Notes**:

- On startup, automatically creates or restores a workspace using the current directory
- Each workspace has its own session history and configuration directory (`.luban-agent/`)
- Switching workspaces automatically restores the most recent session for that workspace
- After authorization, AI Agent can access the root directory and its subdirectories

### Use Case 7: RAG Knowledge Base Q&A

```
# 1. Create RAG knowledge base
> /rag -new D:\KnowledgeBase "My Knowledge Base"
✓ Created RAG knowledge base: My Knowledge Base - D:\KnowledgeBase

# 2. Switch to RAG workspace
> /work -switch "My Knowledge Base"
✓ Switched to workspace: My Knowledge Base

# 3. Authorize and index files
> /rag -index *.md
Indexing workspace: My Knowledge Base
✓ Indexing complete
  Scanned files: 25
  New files: 25
  Total chunks: 142

# 4. Search test
> /rag -search "how to configure workspace"
Found 3 relevant results:
File: D:\KnowledgeBase\setup.md
Content: Workspace configuration requires...

# 5. Direct Q&A (auto-retrieval augmented)
> /agi
Mode: Knowledge Base Q&A (auto-retrieval augmented)

👶 How to create a workspace?
🤖 According to the knowledge base documentation, use the /work -new command...
```

**RAG Knowledge Base Notes**:

- RAG workspace is a special workspace type focused on file management and knowledge Q&A
- Supports indexing `.txt` and `.md` files by default
- After indexing, `/agi` conversations automatically retrieve relevant documents and inject context
- Vector data from different RAG workspaces is completely isolated
- A default `rag-config.json` configuration file is automatically generated under the workspace root directory

---

## 🏗️ Project Structure

```
LubanAgent/
├── App/                    # TUI application layer (Terminal.Gui bootstrap + DI + theming)
│   ├── TerminalGuiApp.cs       # Application.Create().Init() + Run bootstrap
│   ├── TuiTheme.cs             # 24-bit TrueColor theme
│   ├── IUiDispatcher.cs        # UI thread dispatch abstraction
│   └── TerminalGuiDispatcher.cs
├── Views/                  # TUI view layer (pure rendering · no business logic)
│   ├── RootView.cs             # Top-level container (Runnable) + global key bindings + view coordination
│   ├── ConversationView.cs     # Conversation area (self-drawn, Block document model)
│   ├── InputBarView.cs         # Input area (wraps native TextView)
│   └── FooterView.cs           # Footer (mode/directory/git/token/tasks)
├── ViewModels/             # MVVM ViewModel layer
│   ├── ConversationViewModel.cs  # Agent lifecycle + streaming loop + permission confirmation
│   ├── CommandViewModel.cs       # / command routing and execution
│   └── AgentViewViewModel.cs     # Multi-session task view
├── Models/                 # Pure data models (no UI deps · unit-testable)
│   ├── ConversationDocument.cs   # Conversation document model
│   ├── AgentTask.cs / TaskRegistry.cs  # Task model and registry
│   ├── ChoiceOption.cs / ConfirmResult.cs / PlannedAction.cs  # Choice component types
│   └── Blocks/                  # Block class hierarchy
│       ├── Block.cs             # Abstract base (Layout/Render/HitTest)
│       ├── RenderLine.cs        # RenderLine + TextSegment
│       ├── BlockColors.cs       # 24-bit TrueColor constants
│       ├── UserMessageBlock / AssistantMessageBlock
│       ├── ThinkingBlock / ToolCallBlock / ToolResultBlock
│       ├── InlineChoiceBlock / SystemBlock
│       └── ChoiceBlocks.cs      # Factory methods
├── Infrastructure/        # Infrastructure
│   ├── FlushThrottle.cs         # Streaming refresh throttle (16ms window)
│   ├── DatabaseInitializer.cs
│   └── SqliteLocalMemoryStore.cs
├── Services/              # Core services
│   ├── FooterDataProvider.cs    # Footer metadata (git branch/token usage)
│   ├── ConsoleAppService.cs     # Command dispatch
│   ├── SessionManager.cs        # Session persistence
│   └── WorkspaceManager.cs      # Workspace management & authorization
├── Commands/              # Command implementations
├── Configuration/         # Configuration management
├── Profiles/              # Agent profiles
├── Repositories/          # Data access layer
├── Retrieval/             # Semantic retrieval
├── Entities/              # Data entities
├── EmbeddingModels/       # AI embedding model files
└── Program.cs             # TUI bootstrap entry point
```

**Self-drawn rendering architecture**:
- `Models/Blocks/` — 7 Block subclasses with distinct colors; `Layout(width)` computes line count, `Render()` produces `List<RenderLine>` color segments
- `ConversationDocument` — manages Block collection, scroll offset, auto-scroll follow; `GetVisibleLines()` returns visible rows with clipping
- `ConversationView.OnDrawingContent()` — iterates `RenderLine` → `TextSegment`, calls `Driver.SetAttribute()` + `AddRune()` for per-glyph drawing
- `FlushThrottle` — 16ms window merges high-frequency streaming tokens into single redraw (~60fps)

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

### User Configuration (%LocalAppData%\LuBan\AIAgent\config.json)

User configurations (Providers, custom Skills, rules, etc.) are automatically saved locally and loaded on restart.

---

## 🎯 Advanced Features

### Dangerous Operation Confirmation

For dangerous operations (writing files, executing scripts, deleting data, etc.), the system will request user confirmation before execution:

```
You: Write a file to C:\temp\test.txt

⚠️  Dangerous Operation Request: WriteFileAsync
Parameters:
  path: C:\temp\test.txt
  content: File content...

Execute this operation? (y/N): y
✓ Confirmed

[Calling tool]: WriteFileAsync
[Tool result]: File written to C:\temp\test.txt

🤖 File written successfully...
```

**Operations requiring confirmation**:
- 📝 **FileSystem**: Write files, delete files, create/delete directories
- 🔧 **Script Execution**: Execute Shell, Lua, Python scripts
- 🗄️ **Database**: INSERT, UPDATE, DELETE operations
- 🔴 **Redis**: SET, DELETE, FLUSHDB operations

### Session Management

```bash
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

**Session Auto-save**:
- In `/agi` conversations, chat history is automatically saved to SQLite database
- Database location: `%LocalAppData%\LuBan\AIAgent\ai_sessions.db`
- Both user messages and AI responses are automatically saved
- Token counts are automatically tracked

### Custom Skills

LuBan Agent supports two ways to create custom Skills:

#### Method 1: File-based Skills (Recommended)

Create `SKILL.md` files in project-level or user-level directories, compatible with OpenCode format:

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

Please translate the user's content to English.

## Requirements
- Maintain the tone and style of the original text
- Use idiomatic English expressions
- For technical terms, keep the original and add annotations in parentheses
```

**Usage**:

```bash
# Activate Skill in /agi conversation
> /skill -switch
Available Skills:
#    Category     Name                 Description                            Source
1    custom       Translation Asst     Translate text to English              File

Select number (1-1), or 0 to cancel: 1
✓ Skill activated: Translation Assistant
💡 The Skill applies only to the next input and is cleared afterwards

# The next input carries the Skill instructions
👶 Hello, World
🤖 Hello, World
```

**Priority**: Project-level > User-level > Built-in > config.json

#### Method 2: Command Line (Legacy)

```bash
# Add custom Skill
> /skill -add

Enter Skill ID: my-translator
Enter Skill name: Translation Assistant
Enter Skill description: Translate text to English
Enter category: custom
Enter prompt template (multi-line input, single line '.' to finish):
Please translate the following to English:
{input}
.
Enter examples (optional, comma-separated): Hello,World
✓ Custom Skill 'Translation Assistant' (my-translator) added
```

### Custom Rules

```bash
# Add path protection rule
> /rule -add

Enter rule ID: protect-system
Enter rule name: Protect System Directories
Enter ActionTypePattern (default *): file-write
Enter TargetPattern (default *): C:\Windows\*
Enter Action (allow/deny): deny
Enter priority (default 100): 100
✓ Custom rule 'Protect System Directories' (protect-system) added
```

### External MCP Servers

```bash
# Add external MCP server
> /mcp -add

Enter server name: github
Enter description: GitHub Integration
Enter launch command (e.g., npx): npx
Enter command arguments (space-separated, optional): -y @modelcontextprotocol/server-github
✓ External MCP server 'github' added
Use /mcp connect github to connect

# Connect MCP server
> /mcp -connect github
Connecting github...
✓ Connected successfully

# View available tools
> /mcp -tools github
Available tools for github:
  - create_issue: Create GitHub Issue
  - search_repositories: Search repositories
  ...
```

---

## 🔧 Technology Stack

| Component | Description |
|-----------|-------------|
| **Terminal.Gui 2.4.17** | Full-screen TUI framework (net10.0, 24-bit TrueColor, alt-screen) |
| **Microsoft.Agents.AI.Foundry** | Agent runtime framework |
| **Microsoft.Extensions.AI** | Unified chat client abstraction |
| **Microsoft.Playwright** | Browser automation engine |
| **LuBan.DI** | Dependency injection integration |
| **LuBan.Common** | Base interfaces & tool definitions |
| **Microsoft.ML.OnnxRuntime** | ONNX model inference (semantic retrieval) |
| **SQLite** | Session & vector data storage |
| **MVVM + Block Document Model** | Self-drawn rendering (Block → RenderLine → ConversationView) |

---

## 💡 Tips

- 🖥️ **Full-Screen TUI**: Launch directly into Terminal.Gui full-screen; plain text input talks to Agent
- ⌨️ **Keyboard Shortcuts**: `Esc` cancel task, `Shift+Tab` cycle permission modes, `Tab` toggle task view, `Ctrl+Q` quit, `Ctrl+L` redraw
- 🛡️ **Four Permission Modes**: `Shift+Tab` cycles Default / Plan / AcceptEdits / BypassPermissions; footer shows current mode
- 🎨 **Collapse/Expand**: Thinking and tool calls default to collapsed; click `▸` to expand
- 📜 **Scroll Follow**: Streaming output auto-follows; manual scroll-up disconnects, shows "↓ N lines new" prompt
- 💬 Model routing uses `provider:model` format, supporting 20+ AI providers; add new providers via `/provider -add`
- 🌐 **Multiple Endpoints**: Some providers (like Kimi, MiniMax) offer multiple API endpoints, selectable during setup
- 🛠️ **7 Built-in Tool Groups** cover browser automation, file operations, script execution, database, Redis, web requests, and semantic retrieval
- ⚠️ **ToolConfirmationService** uses inline InlineChoiceBlock for tool confirmation (allow/deny/allow all this turn)
- 🔒 **FileSystemToolOptions.AllowedRoots** restricts file access scope to prevent unauthorized Agent operations
- 🧩 **Multi-Agent Orchestration**: AI auto-decomposes complex tasks into DAG with serial/parallel SubAgent execution
- 📂 **Workspace Isolation**: each workspace has individual session history and config directory

---

## 🤝 Related Projects

- **[LuBan.Framework](https://github.com/yswenli/luban-framework)** - LuBan Framework Core
- **[LuBan.DI](https://www.nuget.org/packages/LuBan.DI/)** - Dependency Injection Container
- **[LuBan.AIFlow](https://www.nuget.org/packages/LuBan.AIFlow/)** - AI Workflow Engine
- **[LuBan.Web.Core](https://www.nuget.org/packages/LuBan.Web.Core/)** - Web Core Components

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

**⭐ If this project helps you, please give it a Star! ⭐**

Made with ❤️ by yswenli

</div>
