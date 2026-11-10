# LuBan Agent Codex

**Cross-platform AI Coding Agent Desktop Client based on Avalonia UI**

*Empower LLMs with thinking, planning, tool calling, and autonomous execution capabilities*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](../LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12.1.6-blue.svg)](https://avaloniaui.net/)

[English](README.en.md) | [中文](README.md)

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
- **Two-column work layout**: left sidebar (workspaces / sessions / knowledge bases / plugin entries) + right chat area (message stream + input box); the bottom status bar shows permission mode, working directory, Git branch, run state, and token usage
- **Dark theme**: Professional dark color scheme, no fatigue during long coding sessions
- **Streaming output**: Real-time display of AI thinking process and tool call status
- **Smart scrolling**: Auto-follow output, support manual scrolling to view history

### 🤖 Multi-Model Routing
- **20+ AI Provider support**: OpenAI, Azure, DeepSeek, Kimi, GLM, Qwen, Doubao, Claude, Gemini, Ollama, MiniMax, Ark, Bailian, Hunyuan, MiMo, ERNIE, xAI Grok, Qianfan, Tencent TI, Huawei Pangu, AWS Bedrock, OpenRouter, and custom OpenAI-compatible APIs
- **Unified `provider:model` format**: One-click model switching, no code changes required
- **Dynamic routing**: LuBanChatClient automatically distributes to the corresponding Provider based on prefix

### 🛠️ 12 Built-in Tool Groups
| Tool Group | Capabilities |
|------------|--------------|
| 🌐 **Browser Tools** | Navigate, click, type, screenshot, get content (based on Playwright) |
| 📁 **File System Tools** | Read, write, edit (single/multi/replace-all), list, search, copy, move, delete; allowed directly inside the workspace, paths outside require confirmation |
| 🌿 **Git Tools** | Status, diff, log, branch, stage, commit, merge, rebase, push — 26 fine-grained tools in total |
| 🔧 **Script Execution Tools** | Execute Shell, Lua, Python scripts |
| 🌍 **Web Tools** | Send HTTP requests to get web content |
| 🔍 **Semantic Retrieval Tools** | Index local code/documents, search relevant fragments by semantics |
| 🗜️ **Context Compaction Tools** | Compact the current session's conversation history to free token budget (LLM-invocable) |
| 🧠 **Local Memory Tools** | Store, query, and manage long-term memory |
| 🔌 **MCP External Tools** | Connect external MCP servers and merge their tools into the Agent toolset |
| 🧩 **Orchestration Tools** | Decompose composite tasks into a DAG and dispatch SubAgents in parallel/serial |
| 📚 **Wiki Knowledge Base Tools** | Read/write/delete wiki pages, vector search, health check (opt-in) |
| 📜 **Log Analysis Tools** | Aggregate overview, regex search, and line slicing for large log files with constant memory (enabled by default) |

> **Orchestration note**: Git tools are not included in the orchestration sub-agent default tool groups; specify `toolGroups` explicitly on a DAG node to let a sub-agent run Git operations.

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
- **Workspace configuration directory**: Automatically creates `.luban-agent/` under each workspace root, can place custom `skills`, `rules`, `mcps` configurations, plus connector enablement files `connectors/<name>.json`
- **Temporary file management**: Scripts, screenshots, intermediate files generated at runtime uniformly stored in `.luban-agent/temp/`, supports automatic cleanup of expired files
- **LLM Wiki Knowledge Base**: Special workspace type, supports file indexing and semantic retrieval, automatic retrieval-augmented Q&A
- **Vector storage isolation**: Index data from different workspaces completely isolated, no cross-reading
- **Path authorization management**: Paths inside the workspace are allowed directly; paths outside the workspace are allowed after user confirmation, with grants remembered per path root (this-time/this-turn/this-session tiers)

---

## 🚀 Quick Start

### 1. Prepare the Playwright Browser (Required before using browser tools)

On first use of the browser tools, the app auto-installs the Chromium driver matching the version declared by the `Microsoft.Playwright` package into `~/.luban-agent/playwright-driver/{version}`; no manual step is needed on success.

```powershell
# Manual fallback only when auto-install is blocked by the network:
# use the version printed by the app (PlaywrightDriverResolver.GetInstallBrowsersHint)
npx playwright@<version> install chromium
```

> **Note**: The browser version must match the `Microsoft.Playwright` package version; do not pin a fixed version by hand — the version declared in the csproj is authoritative.

### 2. Clone Source and Run

```bash
# Clone repository
git clone https://github.com/yswenli/luban-agent.git
cd luban-agent

# Run program
dotnet run --project LubanAgentCodex/LubanAgentCodex.csproj
```

### 3. Enter a Workspace

Startup shows a splash screen, then automatically restores the **most recently active normal workspace** (LLM Wiki knowledge bases are excluded from restore). If no workspace has ever been created, it creates a normal workspace from the current process working directory, authorizes it, and makes it current.

```
Splash: Loading app configuration... → Initializing core services... → Preparing knowledge base and workspace...
→ Building main UI... → Main window
```

After entering, switch workspaces and sessions from the sidebar; use `/work` when you need to create, switch, delete, or authorize workspaces.

### 4. Configure Your First AI Provider

Enter `/provider` to open the "Providers" tab of the settings center (written to the global `config.json`, not per workspace):

1. Click "＋ New" at the bottom left to create a provider draft;
2. Pick a preset in "Type (built-in preset, optional)" (15 built-in vendors + Custom, 16 entries); selecting one auto-fills `Name` and `BaseUrl`;
3. Fill in `ApiKey` (required), optionally adjust `BaseUrl` and add or remove custom models;
4. Click "Save"; click "Apply Configuration" to take effect immediately, otherwise it applies on the next workspace switch or restart.

The sidebar "⚙ Settings" button and `/provider` open the same settings center.

### 5. Select a Model and Start the Conversation

```
> /model -list
Available models:
  kimi:k3 (current)
  kimi:k3-256k
  ...

> /model -switch kimi:k3
Model switched: kimi:k3
```

`/model` only supports `-list` (default) and `-switch <provider:model>`; add, edit, and delete providers/models on the "Models" tab of the settings center. The model drop-down on the right of the input toolbar also switches the current model directly.

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

## 📖 Command Overview

LuBan Agent Codex provides a concise and powerful command system, all commands start with `/`:

| Command | Shortcut | Description |
|---------|----------|-------------|
| `/help` | — | Show commands and shortcuts (content comes from the command catalog) |
| `/clear` | — | Clear the current message stream |
| `/mode [name]` | — | View or switch permission mode (default/plan/accept-edits/bypass) |
| `/exit` | `/quit` | Exit the application |
| `/provider` | `/p` | Open the "Providers" tab of the settings center |
| `/model` | `/m` | List models, or switch with `-switch <provider:model>` |
| `/skill` | `/sk` | Open the "Skills" tab (view / create / edit / delete skills) |
| `/rule` | `/r` | Open the "Rules" tab of the settings center |
| `/mcp` | `/mp` | Open the "MCP Services" tab of the settings center |
| `/session` | `/se` | List / create / clear sessions |
| `/stats` | `/st` | Show statistics (message count, permission mode, current session) |
| `/work` | `/w` | Open the "Workspace Management" window |
| `/llmwiki` | `/lw`, `/rag`, `/rg`, `/wiki`, `/wk` | Open the "LLM Wiki Knowledge Base Management" window |
| `/a2a` | — | A2A collaboration management (`-list` / `-server` / `-client` / `-start` / `-stop`) |

> `/agi` and `/init` are CLI-only and unavailable in Codex.

### Sub-command Shortcuts

GUI and CLI share the same alias expansion (`SlashCommandAliases.Expand`), but in Codex only `/model`, `/session`, and `/a2a` actually parse sub-commands; the other management commands ignore arguments and open their UI directly.

| Shortcut | Expands to | Notes |
|----------|------------|-------|
| `-l` | `-list` | List |
| `-a` | `-add` | Add |
| `-u` | `-update` | Update |
| `-d` | `-delete` | Delete |
| `-s` | `-switch` | Switch; expands to `-server` for `/a2a` and `-search` for `/llmwiki` |
| `-n` | `-new` | New |
| `-c` | `-clear` | Clear; expands to `-client` for `/a2a` |
| `-i` | `-index` | Expands only for `/llmwiki` |
| `-t` | `-tools` | Tool list (no Codex command parses this sub-command) |

**Examples**:
- `/m -l` = `/model -list`
- `/m -s kimi:k3` = `/model -switch kimi:k3`
- `/se -n Project Discussion` = `/session -new Project Discussion`
- `/a2a -s` = `/a2a -server` (not `-switch`)

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

### Scenario 3: Skill-driven Code Review

`/skill` only opens the "Skills" tab of the settings center (view / create / edit / delete `SKILL.md` files); it does not accept code to review. The review is done by the Agent: the model first calls `ListSkillsAsync` to find a skill, then `UseSkillAsync` to load its instruction template.

```
You: Use the code review skill on this code: public void Test() { var x = 1/0; }

⚙️ Calling tool: ListSkillsAsync
   Parameters: keyword = code
⚙️ Calling tool: UseSkillAsync
   Parameters: id = code-review
✓ Tool execution complete

🤖 📋 Code analysis:
1. ⚠️ Line 1 risks division by zero
2. 💡 Recommend adding exception handling
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

Enter `/work` to open the "Workspace Management" window. Each row shows the type icon (📁 Normal / 📚 LLM Wiki), name, root directory, `✓ current` marker, and authorization state; the toolbar provides:

| Button | Action |
|--------|--------|
| `+ New Workspace` | Pick a directory and create a normal workspace |
| `Switch` | Make the selected workspace current |
| `Delete` | Delete the selected workspace (cascades to its sessions and indexes, with confirmation) |
| `Authorize` | Grant path authorization for the selected workspace (switches to it first when unauthorized) |

```
> /work
(opens the "Workspace Management" window)
```

After switching, the sidebar session list and the footer working directory refresh together.

### Scenario 7: LLM Wiki Knowledge Base Q&A

Enter `/llmwiki` to open the "LLM Wiki Knowledge Base Management" window. Each row shows 📚 name, path, index status (e.g. "Indexed 25 files · 142 chunks"), and the `✓ current` marker; the toolbar provides:

| Button | Action |
|--------|--------|
| `+ Create Knowledge Base` | Create an LLM Wiki workspace with a name and directory |
| `Index` | Build the index for the selected knowledge base; first asks for an "index file pattern" (leave empty to index everything) |
| `Search` | Asks for a "search query", then replaces the view with "Search results · N" and a `← Back to list` button |
| `Delete` | Delete the selected knowledge base (cascades to sessions and indexes, with confirmation) |
| `Rebuild Wiki Index` | Rebuild the Wiki page vector index for the selected knowledge base |
| `Wiki Lint` | Check Wiki page health and report findings |

```
> /llmwiki
(opens the "LLM Wiki Knowledge Base Management" window)
```

Switch to that knowledge base workspace (via the workspace window or the sidebar), then ask in the input box (retrieval-augmented automatically):

```
You: How do I create a workspace?
🤖 According to the knowledge base docs, use /work to open Workspace Management and click "+ New Workspace"...
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
✓ Permission mode switched: Plan

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

Switch among the four modes with `/mode` or `Shift+Tab` (while the Agent is running, `Shift+Tab` is refused with a notice, because the tool-confirmation semantics of the in-flight turn are already fixed):

```
> /mode
Current permission mode: Default
Available modes: default / plan / accept-edits / bypass

> /mode accept-edits
✓ Permission mode switched: AcceptEdits
```

> Command feedback is localized in the app UI; the mode values you type remain `default` / `plan` / `accept-edits` / `bypass`.

| Mode | Behavior |
|------|----------|
| `default` | In-workspace/authorized scope is confirmation-free; out-of-workspace paths, deletes, script execution, etc. require confirmation |
| `plan` | Read-only tools within authorized scope are allowed to produce a plan; everything else is only recorded as planned items |
| `accept-edits` | Edit-class operations within authorized scope (with a path, non-delete) are confirmation-free; scripts and deletes still confirm |
| `bypass` | Skip all tool confirmations (requires a second confirmation when switching; use with caution) |

### Scenario 12: Plug in External MCP Tools to Extend Capabilities On Demand

Beyond built-in tools, you can bring external MCP server tools into the Agent toolset via the "MCP Services" tab of the settings center (jump straight there with `/mcp`):

```
> /mcp
(opens Settings → Workspace Configuration → MCP Services)
```

1. Click "＋ New" and fill the form: `Name` (required, used as the file name), `Description`, `Transport` (stdio / http / sse), `Command` (for http/sse you can put the URL here), `Args` (one per line), `Enabled`;
2. Click "Save", then "Apply Configuration"; saving triggers `Reload()` on the management service, so no restart is needed;
3. Ask in the chat and the Agent merges that server's tools (named `mcp_{client}_{tool}`) into its toolset:

```
You: Create a bug issue in the luban-agent repo
⚙️ Calling tool: mcp_github_create_issue
   Parameters: repo = luban-agent, title = ...
✓ Tool execution complete
```

> The "Scope" selector at the top switches between `★ Global` and a specific workspace, deciding whether the config lands in user-level or workspace-level `.luban-agent/mcps/`.

---

## 🏗️ Project Structure

```
LubanAgentCodex/
├── App.axaml(.cs)              # Application entry (DI wiring + plugin UI dispatcher replacement)
├── Program.cs                  # Main entry point
├── GlobalUsings.cs             # Global usings
├── Assets/                     # Static assets (icons)
├── Converters/                 # Value converters (file size, thumbnail path)
├── Styles/
│   └── Colors.axaml            # Theme color resources
├── Plugins/
│   └── AvaloniaPluginUiDispatcher.cs  # Avalonia implementation of IPluginUiDispatcher (UI-thread marshalling)
├── Security/
│   ├── CodexYunxiaoCredentialProvider.cs  # Yunxiao credential provider (via host credential store)
│   └── YunxiaoSettingsService.cs          # Yunxiao organization ID / PAT access
├── Services/
│   └── FooterDataProvider.cs   # Footer data provider
├── ViewModels/
│   ├── MainWindowViewModel.cs  # Main window ViewModel
│   ├── SessionViewModel.cs     # Session ViewModel (consumes framework AgentStreamEvent)
│   ├── Settings/               # Settings-tab ViewModels (no Avalonia dependency, unit-testable)
│   └── Messages/               # Message data models
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
    ├── MainWindow.axaml(.cs)   # Main window
    ├── SettingsWindow.axaml(.cs)  # Unified settings center (window shell: state wiring + editor dispatch)
    ├── Settings/               # Settings UI partials (navigation / actions / editors)
    │   ├── SettingsTabKind.cs
    │   ├── SettingsWindow.Navigation.cs
    │   ├── SettingsWindow.Actions.cs
    │   ├── SettingsWindow.Editors.Workspace.cs
    │   └── SettingsWindow.Editors.Global.cs
    ├── PluginManageWindow       # Plugin management window
    ├── SkillManageWindow        # Skill management window
    ├── RuleManageWindow         # Rule management window
    ├── MCPManageWindow          # MCP service management window
    ├── WorkManageWindow         # Workspace management window
    ├── LlmWikiManageWindow      # LLM Wiki management window
    ├── RenameDialog / NewWorkspaceDialog / NodeDetailWindow
    │   / ImageViewerWindow / SplashWindow / InitAgentsMdDialog
    └── Controls/               # Custom controls
        ├── Sidebar             # Left sidebar
        ├── MessageStream       # Message stream
        ├── InputBox            # Input box
        ├── FooterBar           # Footer status bar
        ├── BusyOverlay         # Busy overlay
        ├── UserMessageView     # User message
        ├── AssistantMessageView # AI message
        ├── ThinkingMessageView # Thinking process
        ├── ToolCallCard        # Tool call card
        ├── OrchestrationNodeCard # Orchestration node card
        └── SystemMessageView   # System message
```

> Note: the Agent host (`IAgentHost` / `AgentHost`) and stream event types (`AgentStreamEvent`) come from the framework
> `LuBan.AIAgent`; this project only consumes them in the UI. Business logic for skills / rules / MCP / sessions /
> workspaces / plugins / connectors lives in `LubanAgentCore.Services.Management`.

---

## ⚙️ Configuration

### Application Configuration (appsettings.json)

```json
{
  "LuBanAgent": {
    "DefaultModel": "openai:gpt-4o",
    "SystemPrompt": "You are an intelligent assistant that helps users complete various tasks.",
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

### User Configuration (%LocalAppData%\LuBan\AIAgent\config.json)

User configuration (Provider, custom Skills, rules, connector declarations, etc.) automatically saved locally, automatically loaded on restart.

> Connectors (e.g. the Aliyun DevOps `yunxiao` connector) declare availability in the `connectors` section of `config.json`; a plugin manifest's `mcpServers: ["yunxiao"]` is resolved by the host from the connector's MCP surface, and the PAT flows only through the credential store (key `connector:yunxiao:token`, with legacy-key `yunxiao_pat` compatibility) — it is never written in plain text into `.luban-agent/mcps/*.json`.

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

Message stream record: Allowed Write File
```

The dialog is an application-modal window: the main window is not interactive until you choose; Esc or aborting the session is treated as "Deny".

**Grant tiers**: `✓ Allow` allows this call only; `Allow All This Turn` is remembered for this turn (reset each turn); `Allow All This Session` is remembered for this session (process lifetime). Grants are remembered per path root (parent directory for files, the directory itself for directories); when the grant root resolves to a drive root (e.g. `D:\`), it is not remembered.

**Operations requiring confirmation**:
- 📝 **File System**: read/write/copy/move for out-of-workspace paths, and all delete operations (deletion is never remembered and always confirms)
- 🔧 **Script Execution**: Execute Shell, Lua, Python scripts

Read-only and normal edit operations inside the workspace need no confirmation; in Plan mode only in-workspace read-only tools are allowed directly.

### Session Management

```
# Create and switch to a new session (title optional, defaults to "New Session")
> /session -new Project Discussion

# List all sessions (the current one carries a "(current)" marker)
> /session -list

# Clear the current session (also clears the message stream)
> /session -clear

# View statistics
> /stats
```

`/session` only supports `-list`, `-new <title>`, and `-clear`; switch sessions by clicking one in the sidebar.
`/stats` takes no arguments and prints the message count, permission mode, and current session; token usage is shown in the footer status bar.

**Automatic session saving**:
- Conversation history automatically saved to SQLite database
- Database location: `luban-ai-agent.db` under the program base directory (`Data Source=./luban-ai-agent.db` in `appsettings.json`; the legacy `ai_sessions.db` is renamed automatically on first start)
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

Enter `/rule` to open the "Rules" tab of the settings center, click "＋ New", then fill the form: `Id` (required, used as the file name), `Name`, `Description`, `ActionTypePattern`, `TargetPattern` (both support wildcards), `Action` (allow / deny), `Priority` (higher number wins), `Enabled`, and `Content` (optional guidance text read by `IContentRule`). Click "Save" to apply.

```
> /rule
(opens Settings → Workspace Configuration → Rules)

Example: Id=protect-system, Name=Protect system directories,
         ActionTypePattern=file-write, TargetPattern=C:\Windows\*, Action=deny, Priority=100
```

### MCP External Servers

```
# Open the MCP services settings tab
> /mcp

# Form fields
Name        required, used as the file name (e.g. github)
Description optional
Transport   stdio / http / sse
Command     startup command for stdio (e.g. npx); for http/sse you can put the URL here
Args        one argument per line, e.g. -y, @modelcontextprotocol/server-github
Enabled     whether the server participates in loading
```

After "Save", click "Apply Configuration" to hot-reload (`McpManagementService` calls `Reload()`); exposed tools are named `mcp_{client}_{tool}`, e.g. `mcp_github_create_issue`.

---

## 🔧 Tech Stack

| Component | Description |
|-----------|-------------|
| **Avalonia 12.1.6** | Cross-platform UI framework (.NET 10.0, dark theme) |
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
- ⌨️ **Shortcuts**: `Enter` send message, `Ctrl+Enter` / `Shift+Enter` newline, `Ctrl+V` paste attachments (files/screenshots), `Shift+Tab` switch permission mode, `Esc` cancel the current task
- 🛡️ **Four Permission Modes**: `Shift+Tab` cycle through Default / Plan / AcceptEdits / BypassPermissions, footer displays in real-time
- 🎨 **Collapse/Expand**: Thinking process and tool calls collapsed by default, click to expand and view details
- 📜 **Smart Scrolling**: Streaming output auto-follows; manual scroll up breaks follow
- 🌐 **Multi-Provider Support**: Supports 20+ AI Providers, unified `provider:model` format
- 🛠️ **12 Built-in Tool Groups**: Cover browser automation, file operations (including editing), Git version control, script execution, web requests, semantic retrieval, context compaction, local memory, MCP external tools, multi-agent orchestration, the LLM Wiki knowledge base (opt-in), and large log analysis
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

**⭐ If this project is helpful to you, please give it a Star! ⭐**

Made with ❤️ by yswenli
