# LuBan Agent CLI

**AI Agent Command-Line Tool Based on Microsoft Agent Framework**

*Empowering Large Language Models with Thinking, Planning, Tool-Calling, and Autonomous Execution*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](../LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Terminal.Gui](https://img.shields.io/badge/Terminal.Gui-2.6.0--develop.61-blue.svg)](https://gui-cs.github.io/Terminal.Gui/)

English | [中文](README.md)

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

### 🛠️ 12 Built-in Tool Groups
| Tool Group | Capabilities |
|------------|--------------|
| 🌐 **Browser Tools** | Navigate, click, type, screenshot, get content (powered by Playwright) |
| 📁 **FileSystem Tools** | Read, write, edit (single/multi/replace-all), list, search, copy, move, delete; allowed directly inside the workspace, paths outside require confirmation |
| 🌿 **Git Tools** | Status, diff, log, branch, stage, commit, merge, rebase, push, worktree isolation — 30 fine-grained tools in total |
| 🔧 **Script Execution Tools** | Execute Shell, Lua, Python scripts |
| 🌍 **Web Tools** | Send HTTP requests to fetch web content |
| 🔍 **Semantic Retrieval Tools** | Index local code/documents and search by semantic similarity |
| 🗜️ **Context Compaction Tools** | Compact the current session's conversation history to free token budget (LLM-invocable) |
| 🧠 **Local Memory Tools** | Store, query, and manage long-term memory |
| 🔌 **MCP External Tools** | Connect external MCP servers and merge their tools into the Agent toolset |
| 🧩 **Orchestration Tools** | Decompose composite tasks into a DAG and dispatch SubAgents to run in parallel/serial |
| 📚 **Wiki Knowledge Base Tools** | Read/write/delete wiki pages, vector search, health check (opt-in) |
| 📜 **Log Analysis Tools** | Aggregate overview, regex search, and line slicing for large log files with constant memory (enabled by default) |

> **Orchestration note**: Git tools are not included in the orchestration sub-agent default tool groups (`Orchestration.DefaultToolGroups=["filesystem", "log"]`); specify `toolGroups` explicitly on a DAG node to let a sub-agent run Git operations.

### 🎯 Skill System
Ten core built-in skills, plug and play:
- **Brainstorming**: Explore requirements and design before implementing features
- **Code Review**: Review code, identify issues, and provide improvement suggestions
- **Documentation**: Generate code comments, README files, API documentation
- **Code Refactoring**: Refactor code to improve quality
- **Test Generation**: Automatically generate unit tests
- **Code Explanation**: Explain complex code logic
- **Debug Assistant**: Assist with debugging issues
- **Git Commit**: Generate standardized Git commit messages
- **Skill Discovery**: Automatically discover and recommend suitable skills
- **AGENTS.md Generation**: Generate the workspace AGENTS.md spec document (the `/init` command)

**Skill Toggle**: `/skill -switch` enables or disables a built-in/custom Skill, and the state is persisted; disabled Skills no longer take part in conversations. File-based Skills cannot be toggled this way — edit the `.luban-agent/skills` directory instead. `/skill <id>` runs a single Skill directly.

**File-based Skills**: Support defining custom Skills via SKILL.md files, compatible with OpenCode format. Place them in project-level or user-level directories for automatic loading.

### 📎 Attachment Support (Images + Text Files)
- **Multiple input paths**: `@path` references, the `Ctrl+O` file picker, and terminal drag-and-drop
- **Size tiers**: up to 5 per message; text ≤50KB is inlined, >50KB injects only a path hint for the model to read on demand
- **Image handling**: resized locally before sending; requires a vision model, otherwise blocked with a prompt
- **Lightweight persistence**: sessions store only paths and metadata; history replays from disk or shows placeholders

### 🛡️ Security & Rule Engine
- **Path Access Rules**: Paths inside the workspace are allowed directly; paths outside the workspace are allowed after confirmation
- **Dangerous Operation Confirmation**: Requires user confirmation before out-of-workspace access, write, delete, or execute operations, with this-time/turn/session grant tiers
- **Custom Rules**: Support wildcard matching for flexible tool behavior control

### 💾 Session Persistence
- Conversation history automatically saved to SQLite database
- Long conversation compaction (the `compact_context` tool: summarizes then archives older messages, with an optional retention count)
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
- **Memory Context Injection**: The orchestration entry builds workspace long-term memory and rule context once, then injects it into every SubAgent's system prompt
- **Error Handling**: Critical node failure skips successors, non-critical failure continues execution
- **Seamless Integration**: Automatically triggered in `/agi` conversation, no manual command switching needed
- **Orchestration Extensions**: workspace `.luban-agent/plans/*.json` for task templates and `.luban-agent/roles/*.json` for custom SubAgent roles; `Orchestration:PlannerModel` and node `ModelName` support `provider:model` multi-model routing
- **Heuristic Pre-Filter**: task planning is only entered when the input length falls within `[MinLength, MaxLength]` and a composite keyword matches, saving LLM calls and preventing ordinary long questions from being misclassified as composite tasks

### 📂 Workspace & Knowledge Base
- **Workspace Isolation**: Each workspace has its own root directory, session history, and configuration directory
- **Stable Workspace ID**: The workspace ID is derived from the normalized root path, so CLI and Codex yield the same ID for the same directory and thus share long-term memory across hosts
- **Workspace Config Directory**: Automatically creates `.luban-agent/` under workspace root for custom `skills`, `rules`, `mcps` configurations, plus connector enablement files `connectors/<name>.json`
- **Temporary File Management**: Scripts, screenshots, and intermediate files generated at runtime are stored under `.luban-agent/temp/`, with automatic cleanup of expired files
- **LLM Wiki Knowledge Base**: Special workspace type supporting file indexing and semantic retrieval with auto-retrieval-augmented Q&A
- **Vector Store Isolation**: Index data from different workspaces is completely isolated, no cross-workspace data leaks
- **Path Authorization Management**: Paths inside the workspace are allowed directly; paths outside the workspace are allowed after user confirmation, with grants remembered per path root (this-time/this-turn/this-session tiers)
- **Default Config Generation**: LLM Wiki workspaces automatically generate default `llmwiki-config.json` upon creation

---

## 🚀 Quick Start

### 1. Browser Driver (Prepared Automatically on First Browser Tool Use)

The browser driver (~100MB) is not shipped with the package. On the first browser tool call it is installed automatically to `~/.luban-agent/playwright-driver/{version}`, using the version declared by the `Microsoft.Playwright` package — no manual step needed.

> **Note**: Manual installation is normally unnecessary. If automatic installation fails (e.g. restricted network), the app prints a fallback command matching the current runtime version (of the form `npx playwright@<version> install chromium`); run it as prompted. Do not pin another version, or it will not match the bundled driver.

### Option 1: Clone and Run from Source

```bash
# Clone the repository
git clone https://github.com/yswenli/luban-agent.git
cd luban-agent

# Run the program (there is no csproj at the repo root, so name the CLI project)
dotnet run --project LubanAgentCli/LubanAgentCli.csproj
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
git clone https://github.com/yswenli/luban-agent.git
cd luban-agent

# 2. Pack
dotnet pack LubanAgentCli/LubanAgentCli.csproj -c Release -o ./artifacts

# 3. Install globally
dotnet tool install -g LuBan.Agent.CLI --add-source ./artifacts
```

#### Usage After Installation

```bash
# Launch full-screen TUI interface from any directory (requires an interactive terminal; no redirects)
luban-agent-cli
```

> **TUI Migration Complete**: The interface has been fully migrated from Console/Spectre hybrid to Terminal.Gui v2 full-screen TUI (Claude Code style).
> Three-region layout (conversation / footer / input), Agent streaming conversation loop, four-mode permission system, inline command panel, footer metadata, and Agent View multi-session are all in place.
> Agent auto-initializes on first input; `Esc` cancels running task, `Shift+Tab` cycles permission modes, `Tab` toggles conversation / task views.

#### Configuration Files

- **Application config** (`appsettings.json`): Always loaded first from the tool installation directory; an `appsettings.json` in the current working directory can override it
- **User config** (Providers, Skills, rules, etc.): Automatically saved to `%LocalAppData%\LuBan\AIAgent\users\<userId>\config.json`, where `<userId>` comes from the `LUBAN_USER_ID` environment variable (default `yswenli`); the legacy single-user config `%LocalAppData%\LuBan\AIAgent\config.json` is migrated automatically on first access
- **Session data**: Saved to `luban-ai-agent.db` under the program base directory (`Data Source=./luban-ai-agent.db` in `appsettings.json` is resolved against the base directory); the legacy `ai_sessions.db` is renamed to it on first start

#### Update & Uninstall

```bash
# Update (from NuGet)
dotnet tool update -g LuBan.Agent.CLI

# Update (from local source)
dotnet pack LubanAgentCli/LubanAgentCli.csproj -c Release -o ./artifacts
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
  OpenAI
  Azure OpenAI
  DeepSeek
  Kimi (Moonshot)
  Zhipu GLM
  Qwen
  Doubao
  Claude
  Google Gemini
  Ollama (Local)
  MiniMax
  Volcengine Ark
  Alibaba Bailian
  Tencent Hunyuan
  Xiaomi MiMo
  Custom OpenAI-Compatible API

(Select Kimi, then fill in the API Key in the form)

Kimi API endpoint selection:
  Domestic (https://api.moonshot.cn/v1)
  Overseas (https://api.moonshot.ai/v1)
  Coding-specific (https://api.kimi.com/coding/v1)
✓ Provider 'Kimi' added and saved
  Supported models: k3, k3-256k, kimi-for-coding, kimi-for-coding-highspeed
```

> **Security Note**: API Key input is hidden (password input mode)

### 4. Select Model and Start Chatting

```
> /model -switch
Select Provider:
  OpenAI

OpenAI available models (Enter to confirm; the first is used if none is selected):
  gpt-4.1
  gpt-4.1-mini
  gpt-4.1-nano
  gpt-4o
  ...

✓ LLM confirmed: OpenAI - gpt-4o
```

### 5. Start Your First Agent Conversation

```
> /agi

You: Show me what directories are on drive D

⠋ Calling tool: ListDirectoryAsync
⠙ Thinking...
⠹ Generating response...

[Calling tool]: ListDirectoryAsync
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

## 🌐 Host Service and Access Token (WebHost)

This program does not execute the agent itself: conversations, file access and tool calls all happen in the local host process `LubanAgentWebHost`, and this program is only its client.

- **Auto launch**: when no host is available locally, the client starts a `LubanAgentWebHost` child process, passing only `--plugins-dir` and `--debug`; port, token and timeouts all come from the host's own configuration (`LubanAgentWeb:Port`, default 18080). If an instance is already running, the client reads the descriptor file, probes `/system/health` and adopts it instead of starting a second one. The host executable is resolved in this order: `AgentClientOptions.ServerPath` → environment variable `LUBAN_AGENT_WEBHOST` → the client output directory → walking up for `LubanAgentWebHost/bin/**`; if none is found the client reports "LubanAgentWebHost not found".
- **The host stays resident**: a launched host keeps running after this program exits; stop it with `LubanAgentWebHost --stop`. Only one instance can own a port — the one started later fails on the port conflict.
- **Access token**: the host listens on all interfaces, so every start generates a one-off token and records it in `%LOCALAPPDATA%\luban-agent\webhost.json`; the client reads it from there and sends `Authorization: Bearer` automatically — no manual input needed.
- **Browser UI**: the host also serves a web UI at the entry address printed in the terminal (`http://<LAN IP>:<port>/?t=<token>`). The token is exchanged for an HttpOnly cookie and the URL is 302-redirected to the clean address; it expires when the host restarts.
- **Getting a 401 or failing to connect**:
  - run `LubanAgentWebHost token` on the host machine to print the current entry address again;
  - after a host restart the token is gone: old links, bookmarks and browser sessions must be replaced;
  - port already in use: start with `--port <port>`;
  - to start over: `LubanAgentWebHost --stop`, then launch this program again.
- **Security boundary**: while the token is valid, anyone with the entry address can read/write workspaces and run commands; there is no TLS by default, so do not forward the address. See "WebHost access token and security" in the root README for the full picture.

---

## 📖 Command Reference

LuBan Agent provides a simple yet powerful command system:

### Direct Command-Line Execution

After the TUI migration, all commands are invoked interactively within the TUI full-screen interface using the `/` prefix. Direct command-line argument execution (e.g., `luban-agent-cli /se -l`) is planned as a future enhancement.

### Command Input (TUI Full-Screen Interface)

On launch, enters a Terminal.Gui full-screen alt-screen interface with a top-to-bottom layout: conversation area (scrollable), footer (mode/directory/git/token), and input area (bottom):

- **Enter** — Submit input (plain text → Agent; `/` prefix → command panel)
- **Ctrl+Q** — Quit the TUI (confirmation dialog; warns that a running task will be interrupted)
- **Ctrl+L** — Redraw screen
- **Ctrl+O** — Open the file picker to add attachments (multi-select)
- **Esc** — Cancel the running Agent task; when idle it opens the same quit confirmation as Ctrl+Q
- **Shift+Tab** — Cycle permission modes (Default → Plan → AcceptEdits → BypassPermissions)
- **Tab** — Toggle conversation / Agent task view
- **Mouse** — Click blocks to collapse/expand; scroll wheel; Shift+drag for native terminal selection

### Attachment Input

- **`@path` reference** — Write `@D:\work\a.cs` or `@./src/a.cs` in the input; resolved into attachments on submit. If any reference cannot be resolved, nothing is sent and a prompt is shown
- **`Ctrl+O`** — Open the file picker (multi-select) to add attachments
- **Terminal drag-and-drop** — Drop files onto the terminal window to add them to the pending attachments
- **Submit** — Attachments-only messages (no text) can also be sent with Enter

Limits: up to **5** attachments per message; images require a vision-capable model; text **≤50KB** is inlined, **>50KB** injects only a path hint for the model to read on demand. A message may mix images and text files.

### Command List

| Command | Shorthand | Description |
|---------|-----------|-------------|
| `/help` | — | Show help |
| `/clear` | — | Clear conversation history |
| `/mode [name]` | — | View or switch permission mode (default/plan/accept-edits/bypass) |
| `/exit` / `/quit` | — | Exit program |
| `/provider` | `/p` | Manage AI Providers |
| `/model` | `/m` | Manage models |
| `/skill` | `/sk` | View and run Skills |
| `/rule` | `/r` | View and manage rules |
| `/mcp` | `/mp` | View MCP clients |
| `/session` | `/se` | Manage chat sessions |
| `/agi` | `/a` | General Agent conversation (auto-enabled, just type) |
| `/browse` | `/b` | Website-specific Agent operations |
| `/stats` | `/st` | Session & Token statistics |
| `/work` | `/w` | Workspace management |
| `/llmwiki` | `/lw`, `/rag`, `/rg`, `/wiki`, `/wk` | LLM Wiki knowledge base management |
| `/a2a` | — | A2A collaboration management (status/server/client/start-stop) |
| `/init <description>` | — | Initialize the workspace AGENTS.md |

### Subcommand Shorthands

| Shorthand | Full Command | Applies To |
|-----------|--------------|------------|
| `-l` | `-list` | All management commands |
| `-a` | `-add` | All management commands (note: in `/work`, `-add` is an alias of `-authorize`) |
| `-u` | `-update` | Provider/Model/Skill/Rule/MCP |
| `-d` | `-delete` | Provider/Model/Skill/Rule/MCP/Work/LLM Wiki |
| `-s` | `-switch` | All management commands (note: in `/llmwiki` `-s` is `-search`, in `/a2a` `-s` is `-server`) |
| `-n` | `-new` | Session/Work/LLM Wiki |
| `-c` | `-clear` | Session (note: in `/a2a` `-c` is `-client`) |
| `-t` | `-tools` | MCP |
| `-i` | `-index` | LLM Wiki (index files) |
| `-info` | `-info` | Work (workspace info) |
| `-add` | `-authorize` | Work (authorize workspace) |

**Examples**:
- `/p -l` = `/provider -list`
- `/st -days 7` = `/stats -days 7` (statistics only accept `-days`/`days`; `-d` is expanded to `-delete`)
- `/st --all` = statistics across all workspaces
- `/mp -connect filesystem` = `/mcp -connect filesystem` (`-c` is expanded to `-clear` and cannot stand for `-connect`)
- `/work -n D:\MyProject` = create a workspace
- `/llmwiki -i *.md` = index the Markdown files of the current LLM Wiki workspace

---

## 🎨 Use Cases

### Use Case 1: File Management Assistant

```
You: List all .cs files in the src directory and count lines of code

[Calling tool]: ListDirectoryAsync
  Parameter path: src
[Calling tool]: ReadFileAsync
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

Executing Skill: Code Review
Review code, identify issues, and suggest improvements

Enter content: public void Test() { var x = 1/0; }

📋 Code Analysis:
Potential issues found:
1. ⚠️ Division by zero error on line 1
2. 💡 Suggest adding exception handling

Improvement suggestions:
...
```

### Use Case 4: Context Compaction

```
You: Help me compact the current conversation context

[Calling tool]: CompactContextAsync
[Tool result]: { "compactCount": 12, "remainingCount": 8 }

🤖 Context compacted: archived 12 messages, kept 8 active messages
```

### Use Case 5: Composite Task Automatic Orchestration

In `/agi` conversation, when the model judges the input to be a composite task it first calls `plan_task` to build a task graph, then calls `run_orchestration` to execute the DAG:

```
> /agi

👶 Research LuBan and LangChain frameworks, compare their pros and cons, and generate a comparison report

💭 Thinking:
This is a composite task that needs to be decomposed into parallel subtasks...

🔄 Planning task graph...
✓ Planning complete: Generated 4-node task graph

▶ Starting node: research-luban
  [SubAgent] Calling FetchUrlAsync to fetch LuBan framework docs...
✓ Node completed: research-luban

▶ Starting node: research-langchain
  [SubAgent] Calling FetchUrlAsync to fetch LangChain framework docs...
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
┌─────────────┬──────────┬──────────────┬──────┬─────────┬────────────┐
│ Name        │ Type     │ Root         │ Auth │ Sess    │ Last Active│
├─────────────┼──────────┼──────────────┼──────┼─────────┼────────────┤
│ * MyProject │ Normal   │ D:\MyProject │ ✓    │ 3       │ 2026-07-31 │
│   Docs      │ LLM Wiki │ D:\Docs      │ ✗    │ 0       │ -          │
└─────────────┴──────────┴──────────────┴──────┴─────────┴────────────┘

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

- On startup the most recently active workspace is entered (descending `LastActiveAt`, skipping workspaces whose root no longer exists or that are of type LLM Wiki); when no usable workspace exists, a creation form is shown (path/name/AGENTS.md description), and cancelling it enters the main UI without a workspace
- Entering the main UI automatically loads that workspace's most recent session (descending `UpdateTime`; a default session is created if none) and replays its history
- Each workspace has its own session history and configuration directory (`.luban-agent/`)
- Switching workspaces automatically restores the most recent session for that workspace
- After authorization, AI Agent can access the root directory and its subdirectories without confirmation; out-of-workspace paths trigger a separate confirmation on access (with this-time/turn/session grant options)

### Use Case 7: LLM Wiki Knowledge Base Q&A

```
# 1. Create LLM Wiki knowledge base
> /llmwiki -new D:\KnowledgeBase "My Knowledge Base"
✓ Created LLM Wiki knowledge base: My Knowledge Base - D:\KnowledgeBase

# 2. Switch to LLM Wiki workspace
> /work -switch "My Knowledge Base"
✓ Switched to workspace: My Knowledge Base

# 3. Index files (indexing runs synchronously inside the host process with a progress window; --wait behaves the same)
> /llmwiki -index *.md
Indexing workspace: My Knowledge Base
File pattern: *.md
✓ Indexing complete
  Scanned files: 25
  New files: 25
  Updated files: 0
  Skipped files: 0
  Total chunks: 142

# 4. Search test
> /llmwiki -search "how to configure workspace"
Found 3 relevant results:
File: D:\KnowledgeBase\setup.md
Content: Workspace configuration requires...

# 5. Direct Q&A (auto-retrieval augmented)
> /agi
Mode: Knowledge Base Q&A (auto-retrieval augmented)

👶 How to create a workspace?
🤖 According to the knowledge base documentation, use the /work -new command...
```

**LLM Wiki Knowledge Base Notes**:

- LLM Wiki workspace is a special workspace type focused on file management and knowledge Q&A
- The name can be omitted when creating; it defaults to `llmwiki` (multiple knowledge bases sharing the name are distinguished by path)
- Indexes Markdown, plain text (`.md`/`.txt`/`.csv`/`.log`, etc.) and common source files (`.cs`/`.ts`/`.py`/`.go`/`.java`/`.yaml`/`.json`/`.html`, etc.) by default; extensions outside the whitelist such as `.docx`/`.pdf` are skipped; `.git`/`bin`/`obj`/`node_modules` and similar directories are excluded automatically
- After indexing, `/agi` conversations automatically retrieve relevant documents and inject context
- Vector data from different LLM Wiki workspaces is completely isolated
- A default `llmwiki-config.json` configuration file is automatically generated under the workspace root directory

### Use Case 8: Safely Reading Files Outside the Workspace (Three-tier Authorization)

By default only paths inside the workspace are allowed without confirmation. When the Agent needs to reference material outside the workspace, it asks for your authorization first, and you decide how wide to open the scope:

```
> /work -switch MyProject
✓ Switched to workspace: MyProject
  Root: D:\MyProject

You: Take a look at D:\Shared\api-spec.md and implement the matching client

⚠ ReadFileAsync
  path=D:\Shared\api-spec.md
  [Y] Allow   [N] Deny   [A] Allow All This Turn   [S] Allow All This Session
> s

✓ Granted this-session access to D:\Shared
📄 Read api-spec.md (216 lines)
🤖 Sure — generating the client per the interface contract...
```

| Tier | Scope | Memory |
|------|-------|--------|
| `Y` Allow | this call only | not remembered |
| `A` Allow all this turn | same-type tools within this turn | expires after each turn |
| `S` Allow all this session | same-type tools for the whole session | remembered per path root (file → parent dir, dir → itself) |

> If the grant root lands on a drive root (e.g. `D:\`) it is not remembered, preventing one grant from opening an entire disk.

### Use Case 9: Plan Mode — Read First, Produce a Plan, Change Nothing

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

### Use Case 10: Deletes Always Ask — Authorization Is Never "Permanent"

Delete-class tools (`DeleteFileAsync` / `DeleteDirectoryAsync` / `DeletePageAsync`) intentionally **never remember** authorization; even if you pick "allow all this turn/session", the next delete still confirms:

```
You: Clean up the temp directory

⚠ DeleteDirectoryAsync
  path=.luban-agent\temp
  [Y] Allow   [N] Deny   [A] Allow All This Turn   [S] Allow All This Session
> a

✓ Deleted .luban-agent\temp
ℹ Delete operations do not remember authorization; the next delete will confirm again
```

### Use Case 11: Four Permission Modes, One-key Cycling

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

### Use Case 12: Plug in External MCP Tools to Extend Capabilities On Demand

Beyond built-in tools, you can bring external MCP server tools into the Agent toolset:

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
LubanAgentCli/
├── App/                    # TUI application layer (Terminal.Gui bootstrap + theme + dialogs)
│   ├── TerminalGuiApp.cs       # Application creation/init and run bootstrap
│   ├── StartupRunner.cs        # Startup flow (wizard → workspace resolution → main UI)
│   ├── StartupDialog.cs        # Startup wizard dialog
│   ├── StartupWorkspaceResolver.cs # Resolve and enter the latest workspace (form if none)
│   ├── TuiTheme.cs             # 24-bit TrueColor theme
│   ├── ITuiUiService.cs / TuiUiService.cs  # Modal interaction abstraction and implementation
│   ├── TuiOutputWriter.cs      # Writes command output into the conversation document
│   ├── IUiDispatcher.cs / TerminalGuiDispatcher.cs  # UI thread dispatch
│   ├── Services/               # TUI-side helper services
│   │   └── TitleService.cs         # Session title generation
│   ├── Dialogs/                # Popup components (unified style)
│   │   ├── BaseDialog.cs           # Base dialog (no border + gray bg + white text + title)
│   │   ├── ConfirmDialog.cs        # Confirm dialog (Yes/No)
│   │   ├── ChooseDialog.cs         # Choose dialog (ListView + OK/Cancel)
│   │   ├── FormDialog.cs           # Form dialog (dynamic fields + validation)
│   │   ├── TableDialog.cs          # Table dialog (TableView + Select/Close)
│   │   └── ProgressDialog.cs       # Progress dialog (no buttons + status update)
│   ├── Views/                  # TUI view layer (pure rendering · no business logic)
│   │   ├── RootView.cs             # Top-level container (Runnable) + global key bindings + view coordination
│   │   ├── ConversationView.cs     # Conversation area (self-drawn, Block document model)
│   │   ├── InputBarView.cs         # Input area (wraps native TextView)
│   │   ├── FooterView.cs           # Footer (mode/directory/git/token/tasks)
│   │   └── NodeDetailView.cs       # Orchestration node detail popup
│   ├── ViewModels/             # MVVM ViewModel layer
│   │   ├── ConversationViewModel.cs  # Agent lifecycle + streaming loop + permission confirmation
│   │   ├── CommandViewModel.cs       # / command routing and execution
│   │   └── AgentViewViewModel.cs     # Multi-session task view
│   └── Models/                 # Pure data models (no UI deps · unit-testable)
│       ├── ConversationDocument.cs   # Conversation document model
│       ├── AgentTask.cs / TaskRegistry.cs  # Task model and registry
│       ├── AttachmentDraft.cs / ChoiceOption.cs  # Attachment drafts and choice items
│       └── Blocks/                  # Block class hierarchy
│           ├── Block.cs             # Abstract base (Layout/Render/HitTest)
│           ├── RenderLine.cs        # RenderLine + TextSegment
│           ├── BlockColors.cs / TextMeasure.cs
│           ├── UserMessageBlock / AssistantMessageBlock
│           ├── ThinkingBlock / ToolCallBlock / SystemBlock
│           ├── InlineChoiceBlock / ActionSpinnerBlock / OrchestrationNodeBlock
│           ├── ChoiceBlocks.cs      # Inline confirmation block factory
│           └── MarkdownLightRenderer.cs  # Lightweight Markdown rendering
├── Commands/               # Slash command implementations
│   ├── CommandBase.cs / ICommand.cs
│   ├── ProviderCommand.cs / ModelCommand.cs / SkillCommand.cs / RuleCommand.cs
│   ├── MCPCommand.cs / SessionCommand.cs / StatsCommand.cs / WorkCommand.cs
│   └── LlmWikiCommand.cs / A2ACommand.cs / InitCommand.cs
├── Infrastructure/         # TUI infrastructure
│   ├── FastInputBootstrapper.cs  # Fast input bootstrap
│   └── TuiDiag.cs                # TUI diagnostics switch
├── Services/               # Session-scoped UI services
│   └── SpinnerService.cs       # Loading indicator
├── GlobalUsings.cs
└── Program.cs              # Entry point (TUI bootstrap)
```

> Session persistence, workspace management, database initialization, and the streaming refresh throttle live in `LubanAgentCore` (e.g. `Infrastructure/DatabaseInitializer.cs`, `Services/WorkspaceManager.cs`, `Utils/FlushThrottle.cs`), not in this project.

### Dialog Architecture

All dialogs inherit from `BaseDialog`, ensuring unified style and behavior:

| Component | Purpose | Buttons |
|-----------|---------|---------|
| `ConfirmDialog` | Confirm operations (Yes/No) | Yes/No (default configurable) |
| `ChooseDialog` | Select one from list | OK/Cancel |
| `FormDialog` | Dynamic form input | OK/Cancel (with validation) |
| `TableDialog` | Table data display & selection | Select/Close |
| `ProgressDialog` | Long-running task progress | None (closable configurable) |

**Unified Style**: No border, gray background (#2D2D2D), white text, bold left-aligned title (accent color), two-line gap between title and content.

---

## ⚙️ Configuration

### Application Configuration (appsettings.json)

```json
{
  "LuBanAgent": {
    "DefaultModel": "deepseek:v4flash",
    "SystemPrompt": "You are an intelligent assistant that can help users with various tasks.",
    "MaxToolLoopIterations": 500,
    "Session": {
      "CompactTargetMessages": 50,
      "CompactThreshold": 10
    },
    "Orchestration": {
      "Enabled": true,
      "PlannerReasoningEffort": "None",
      "MaxParallelism": 5,
      "MaxNodes": 20,
      "DefaultNodeTimeoutSeconds": 300,
      "DefaultToolGroups": [ "filesystem", "log" ]
    }
  }
}
```

The above is an excerpt; see `LubanAgentCli/appsettings.json` for the complete set of options.

### User Configuration (%LocalAppData%\LuBan\AIAgent\users\<userId>\config.json)

User configurations (Providers, custom Skills, rules, connector declarations, etc.) are automatically saved locally and loaded on restart. `<userId>` comes from the `LUBAN_USER_ID` environment variable (default `yswenli`); the legacy single-user config `%LocalAppData%\LuBan\AIAgent\config.json` is migrated into the user directory on first access.

> Connectors (e.g. the Aliyun DevOps `yunxiao` connector) declare availability in the `connectors` section of `config.json`, and whether one is enabled in a workspace is decided by `.luban-agent/connectors/<name>.json`; credential keys are generalized to `connector:<instanceName>:token` (with legacy-key `yunxiao_pat` compatibility).

---

## 🎯 Advanced Features

### Dangerous Operation Confirmation

For sensitive operations (out-of-workspace path access, writing files, executing scripts, deleting data, etc.), the system shows an inline confirmation before execution; in-workspace paths are allowed directly:

```
You: Write a file to C:\temp\test.txt

⚠ WriteFileAsync
  path=C:\temp\test.txt, content=File content...
  [Y] Allow   [N] Deny   [A] Allow All This Turn   [S] Allow All This Session

> y
✓ Confirmed

[Calling tool]: WriteFileAsync
[Tool result]: File written to C:\temp\test.txt

🤖 File written successfully...
```

**Grant tiers**: `Y` allows this call only; `A` is remembered for this turn (reset each turn); `S` is remembered for this session (process lifetime).

**Operations requiring confirmation**:
- 📝 **FileSystem**: read/write/copy/move for out-of-workspace paths, and all delete operations (deletion is never remembered and always confirms)
- 🔧 **Script**: Execute Shell, Lua, Python scripts

Read-only and normal edit operations inside the workspace need no confirmation; in Plan mode only in-workspace read-only tools are allowed directly.

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
- Database location: `luban-ai-agent.db` under the program base directory (the legacy `ai_sessions.db` is renamed automatically on first start)
- Both user messages and AI responses are automatically saved
- Token counts are automatically tracked

### Custom Skills

LuBan Agent supports two ways to create custom Skills:

#### Method 1: File-based Skills (Recommended)

Create `SKILL.md` files in project-level or user-level directories, compatible with OpenCode format:

```
Storage locations (the former is loaded first, the latter overrides it):
  User-level: ~/.luban-agent/skills/<skill-id>/SKILL.md   # on Windows: %USERPROFILE%\.luban-agent\skills\...
  Project-level: <workspace>/.luban-agent/skills/<skill-id>/SKILL.md
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
# Enable/disable a Skill (built-in and custom Skills can be toggled; state is persisted)
> /skill -switch
Select a Skill to enable/disable:
  Code Review (code-review) [built-in] [enabled]
  Translation Assistant (my-translator) [file] [enabled]

✓ Skill 'code-review' disabled

# Run a Skill directly (by Id)
> /skill my-translator
Executing Skill: Translation Assistant
```

**Priority**: Built-in (hard-coded) > project-level files > user-level files > config.json (for duplicate Ids the highest priority wins).

#### Method 2: Command Line (Legacy)

```bash
# Add custom Skill
> /skill -add
(Form: Skill ID *, Skill name *, Skill description, Category (default custom), Prompt template (multiline), Examples (optional, comma-separated))
✓ Custom Skill 'Translation Assistant' (my-translator) added
```

### Custom Rules

```bash
# Add path protection rule
> /rule -add
(Form: Rule ID *, Rule name *, ActionTypePattern (default *), TargetPattern (default *), Action (allow/deny, default deny), Priority (default 100))
✓ Custom rule 'Protect System Directories' (protect-system) added
```

### External MCP Servers

```bash
# Add external MCP server
> /mcp -add
(Form: Server name *, Description, Launch command (e.g., npx) *, Command arguments (one per line, spaces allowed))
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
| **Terminal.Gui 2.6.0-develop.61** | Full-screen TUI framework (net10.0, 24-bit TrueColor, alt-screen) |
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
- ⌨️ **Keyboard Shortcuts**: `Esc` cancel task (confirms exit when idle), `Shift+Tab` cycle permission modes, `Tab` toggle task view, `Ctrl+Q` quit with confirmation, `Ctrl+L` redraw, `Ctrl+O` add attachments
- 🛡️ **Four Permission Modes**: `Shift+Tab` cycles Default / Plan / AcceptEdits / BypassPermissions; footer shows current mode
- 🎨 **Collapse/Expand**: Thinking and tool calls default to collapsed; click `▸` to expand
- 📜 **Scroll Follow**: Streaming output auto-follows; manual scroll-up disconnects, shows "↓ N lines of new content" prompt
- 💬 Model routing uses `provider:model` format, supporting 20+ AI providers; add new providers via `/provider -add`
- 🌐 **Multiple Endpoints**: Some providers (like Kimi, MiniMax) offer multiple API endpoints, selectable during setup
- 🛠️ **12 Built-in Tool Groups** cover browser automation, file operations (including editing), Git version control, script execution, web requests, semantic retrieval, context compaction, local memory, MCP external tools, multi-agent orchestration, the LLM Wiki knowledge base (opt-in), and large log analysis
- ⚠️ **ToolConfirmationService** requires confirmation for out-of-workspace path access and sensitive operations (write, delete, execute), with this-time/turn/session grant tiers (inline InlineChoiceBlock, Y/N/A/S)
- 🔒 **FileSystemToolOptions.AllowedRoots** together with workspace roots forms the trusted scope; operations inside it need no confirmation
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

**⭐ If this project helps you, please give it a Star! ⭐**

Made with ❤️ by yswenli
