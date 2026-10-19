# LuBan Agent

<div align="center">

**A Complete AI Agent Solution Based on Microsoft Agent Framework**

*One shared core runtime, two terminal experiences: command-line TUI + cross-platform desktop client*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Terminal.Gui](https://img.shields.io/badge/Terminal.Gui-2.4.17-blue.svg)](https://gui-cs.github.io/Terminal.Gui/)
[![Avalonia](https://img.shields.io/badge/Avalonia-12.1.2-blue.svg)](https://avaloniaui.net/)

English | [中文](README.md)

</div>

---

## 🌟 What is LuBan Agent?

Imagine this: you simply say "Show me what directories are on drive D", and the AI automatically invokes the filesystem tool to list all directories. Then you say "Open Baidu and search for LuBan Framework", and the AI launches a browser and completes the search.

This isn't science fiction—this is **LuBan Agent**.

This repository is the complete LuBan Agent solution, containing a shared core library and two client forms:

| Project | Form | Description |
|---------|------|-------------|
| **[LubanAgentCore](LubanAgentCore/)** | Core Library | Agent runtime, tool system, skill system, session storage, LLM Wiki retrieval, workspace management |
| **[LubanAgentCli](LubanAgentCli/)** | Command-line TUI | Full-screen terminal UI based on Terminal.Gui v2 (Claude Code style), installable as a dotnet tool |
| **[LubanAgentCodex](LubanAgentCodex/)** | Desktop Client | Cross-platform GUI based on Avalonia UI, three-pane layout + dark theme |

> 📖 For detailed usage of each project, see: [LubanAgentCli README](LubanAgentCli/README.en.md) | [LubanAgentCodex README](LubanAgentCodex/README.en.md)

### Are You Facing These Challenges?

- 😫 Want LLMs to call tools and complete tasks, but struggling with MCP / Function Calling implementation details?
- 😤 Managing Skills, tool registration, and session persistence separately, leading to high maintenance costs?
- 😖 Difficulty switching model providers—rewriting tons of code just to go from Provider A to Provider B?
- 😩 Command line not intuitive enough—want a GUI without rebuilding an entire Agent infrastructure?

**LuBan Agent provides complete AI Agent infrastructure**—all core capabilities live in LubanAgentCore, shared by both CLI and Codex. Implement once, run everywhere.

---

## ✨ Core Features

### 🤖 Multi-Model Routing
- **20+ AI Provider Support**: OpenAI, Azure, DeepSeek, Kimi, GLM, Qwen, Doubao, Claude, Gemini, Ollama, MiniMax, Volcengine Ark, Alibaba Bailian, Tencent Hunyuan, Xiaomi MiMo, Baidu ERNIE, xAI Grok, Baidu Qianfan, Tencent TI Platform, Huawei Pangu, AWS Bedrock, OpenRouter, plus custom OpenAI-compatible APIs
- **Unified `provider:model` Format**: Switch models with one command, no code changes needed
- **Dynamic Routing**: LuBanChatClient automatically dispatches to the corresponding provider based on prefix

### 🛠️ 12 Built-in Tool Groups
| Tool Group | Capabilities |
|------------|--------------|
| 🌐 **Browser Tools** | Navigate, click, type, screenshot, get content (powered by Playwright) |
| 📁 **FileSystem Tools** | Read, write, edit (single/multi/replace-all), list, search, copy, move, delete; allowed directly inside the workspace, paths outside require confirmation |
| 🌿 **Git Tools** | Status, diff, log, branch, stage, commit, merge, rebase, push — 26 fine-grained tools in total |
| 🔧 **Script Execution Tools** | Execute Shell, Lua, Python scripts |
| 🌍 **Web Tools** | Send HTTP requests to fetch web content |
| 🔍 **Semantic Retrieval Tools** | Index local code/documents and search by semantic similarity |
| 🗜️ **Context Compaction Tools** | Compact the current session's conversation history to free token budget (LLM-invocable) |
| 🧠 **Local Memory Tools** | Store, query, and manage long-term memory |
| 🔌 **MCP External Tools** | Connect external MCP servers and merge their tools into the Agent toolset |
| 🧩 **Orchestration Tools** | Decompose composite tasks into a DAG and dispatch SubAgents in parallel/serial |
| 📚 **Wiki Knowledge Base Tools** | Read/write/delete wiki pages, vector search, health check (opt-in) |
| 📜 **Log Analysis Tools** | Aggregate overview, regex search, and line slicing for large log files with constant memory (enabled by default) |

### 🎯 Skill System
- Nine core built-in skills (Brainstorming, Code Review, Documentation, Refactoring, Test Generation, Code Explanation, Debug Assistant, Git Commit, Skill Discovery), plug and play
- **File-based Skills**: Define custom Skills via SKILL.md files, compatible with OpenCode format, auto-loaded from project-level or user-level directories
- **Conversational Management**: Create, use, and manage Skills and MCP servers through natural language conversation without manual config editing

### 🗣️ Conversational Skill/MCP Management
Operate through natural language—zero learning curve:

| Feature | How to Use |
|---------|------------|
| List Skills | "List all available Skills" |
| Use Skill | "Use the code-review-v2 Skill" |
| Create Skill | "Create a Skill named my-helper, described as general-purpose assistant" |
| Create MCP | "Add an MCP server named my-api, command is node server.js" |

**Features**:
- ✅ Built-in Skills/MCPs cannot be overwritten or deleted, ensuring system stability
- ✅ Write/delete operations require user confirmation, preventing accidental operations
- ✅ Changes take effect automatically in the next conversation turn, no restart needed
- ✅ Skill ID validation to prevent injection attacks

**Storage Locations**:
- Skill: `<workspace>/.luban-agent/skills/<skill-id>/SKILL.md`
- MCP: `<workspace>/.luban-agent/mcps/<mcp-name>.json` (external MCP; in-process connector MCPs are resolved from the config.json declaration and never land here)
- Connector enablement: `<workspace>/.luban-agent/connectors/<name>.json`

### 📎 Attachment Support (Images + Text Files)
- **Two attachment kinds**: images (PNG/JPEG/GIF/WebP/BMP/TIFF/HEIC, requires a vision model) and text files (code, config, logs, etc.)
- **Multiple input paths**: CLI supports `@path` references, the `Ctrl+O` file picker, and terminal drag-and-drop; Codex supports the 📎 button, dropping onto the window, and `Ctrl+V` (clipboard files or screenshots)
- **Size tiers**: up to 5 attachments per message; text ≤50KB is inlined into the message, >50KB injects only a path hint so the model can read it on demand with file tools; **`.log` attachments (50KB–1GB) are streamed into an aggregated overview** (level distribution/time range/top error signatures), and the model then drills down on demand with the `log` tool
- **Image preprocessing**: locally resized (max edge 2000px) then sent as base64 `DataContent`; automatically blocked with a prompt when the current model lacks vision
- **Lightweight persistence**: sessions store only paths and metadata (no base64); history replays rebuild from disk or show placeholders. Codex renders thumbnails in the attachment bar and message area, click to view full size

### 🛡️ Security & Rule Engine
- **Path Access Rules**: Paths inside the workspace are allowed directly; paths outside the workspace are allowed after user confirmation
- **Dangerous Operation Confirmation**: Require user confirmation before out-of-workspace access, write, delete, or execute operations, with this-time/turn/session grant tiers
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
- **Memory Context Injection**: The orchestration entry builds workspace long-term memory and rule context once, then injects it into every SubAgent's system prompt
- **Orchestration Extensions**: workspace `.luban-agent/plans/*.json` for task templates and `.luban-agent/roles/*.json` for custom SubAgent roles

### 📂 Workspace & Knowledge Base
- **Workspace Isolation**: Each workspace has its own root directory, session history, and configuration directory (`.luban-agent/`)
- **Stable Workspace ID**: The workspace ID is derived from the normalized root path, so CLI and Codex yield the same ID for the same directory and thus share long-term memory across hosts
- **LLM Wiki Knowledge Base**: Special workspace type supporting file indexing and semantic retrieval with auto-retrieval-augmented Q&A
- **Vector Store Isolation**: Index data from different workspaces is completely isolated
- **Path Authorization Management**: Paths inside the workspace are allowed directly; paths outside the workspace are allowed after user confirmation, with grants remembered per path root (this-time/this-turn/this-session tiers)

---

## 🚀 Quick Start

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later

### 1. Clone the Repository

```bash
git clone https://github.com/yswenli/luban-agent.git
cd luban-agent
```

### 2. Install Playwright Browser (Required Before Using Browser Tools)

```powershell
# Install browser version matching Microsoft.Playwright 1.61.0
npx playwright@1.61.0 install chromium
```

> **Note**: Browser version must match the Microsoft.Playwright package version. This project currently uses 1.61.0.

### 3. Build the Solution

```bash
dotnet build luban-agent.slnx
```

### 4. Choose Your Client

#### Option 1: Command-line TUI (LubanAgentCli)

```bash
# Run directly
dotnet run --project LubanAgentCli/LubanAgentCli.csproj

# Or install as a dotnet global tool (recommended)
dotnet pack LubanAgentCli/LubanAgentCli.csproj -c Release -o ./artifacts
dotnet tool install -g LuBan.Agent.CLI --add-source ./artifacts

# Launch from any directory after installation
luban-agent-cli
```

On launch, it enters the Terminal.Gui full-screen interface. Type plain text to chat with the Agent; prefix with `/` to open the command panel.

#### Option 2: Desktop Client (LubanAgentCodex)

```bash
dotnet run --project LubanAgentCodex/LubanAgentCodex.csproj
```

Select a workspace on startup, then chat with the Agent in the graphical interface.

### 5. Configure an AI Provider and Start Chatting

Configure your first Provider in either client:

```
> /provider -add
Select Provider type:
  1. OpenAI
  2. Azure OpenAI
  3. DeepSeek
  4. Kimi (Moonshot)
  ...
Select: 4
Enter Kimi API Key: ********
✓ Provider 'Kimi' added and saved
```

Then select a model and start your first Agent conversation:

```
> /model -switch
✓ Selected model: kimi:k3

You: Show me what directories are on drive D

[Calling tool]: ListDirectoryAsync
  Parameter path: D:\
[Tool result]: Program Files, Users, Windows, ...

🤖 Drive D contains the following directories:
1. Program Files
2. Users
3. Windows
...
```

> **Security Note**: API Key input is hidden (password input mode); user configuration is automatically saved to `%LocalAppData%\LuBan\AIAgent\config.json`

---

## 📦 Cross-Platform Build & Publish

The project supports building into **single-file executables** that run directly on target machines without requiring .NET runtime installation.

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- Verify SDK version: `dotnet --version`

### Publish as Single-File Executable

#### Command-line TUI (LubanAgentCli)

```powershell
# Windows 10/11 x64
dotnet publish LubanAgentCli/LubanAgentCli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Windows ARM64
dotnet publish LubanAgentCli/LubanAgentCli.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true

# macOS Intel (x64)
dotnet publish LubanAgentCli/LubanAgentCli.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true

# macOS Apple Silicon (ARM64)
dotnet publish LubanAgentCli/LubanAgentCli.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true

# Linux x64
dotnet publish LubanAgentCli/LubanAgentCli.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# Linux ARM64
dotnet publish LubanAgentCli/LubanAgentCli.csproj -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true
```

#### Desktop Client (LubanAgentCodex)

```powershell
# Windows 10/11 x64
dotnet publish LubanAgentCodex/LubanAgentCodex.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Windows ARM64
dotnet publish LubanAgentCodex/LubanAgentCodex.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true

# macOS Intel (x64)
dotnet publish LubanAgentCodex/LubanAgentCodex.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true

# macOS Apple Silicon (ARM64)
dotnet publish LubanAgentCodex/LubanAgentCodex.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true

# Linux x64
dotnet publish LubanAgentCodex/LubanAgentCodex.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# Linux ARM64
dotnet publish LubanAgentCodex/LubanAgentCodex.csproj -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true
```

### Output Location

| Project | Output Path |
|---------|-------------|
| LubanAgentCli | `LubanAgentCli/bin/Release/net10.0/<rid>/publish/LubanAgentCli[.exe]` |
| LubanAgentCodex | `LubanAgentCodex/bin/Release/net10.0/<rid>/publish/LubanAgentCodex[.exe]` |

> Windows output is `.exe`; macOS/Linux output is an extensionless ELF/Mach-O executable.

### Notes

- **NativeAOT not supported**: The project relies heavily on reflection from Terminal.Gui / Avalonia; `PublishAot=true` will cause runtime exceptions
- **Cross-compilation limitations**: Native dependencies for macOS and Linux (e.g., `libSkiaSharp`) must be compiled on the target platform; it is recommended to run `dotnet publish` directly on the target platform
- **Playwright browser**: Before using browser tools, run `npx playwright@1.61.0 install chromium` on the target machine
- **Configuration files**: After publishing, `appsettings.json` is output with the artifacts and can be modified before running; user configuration is saved to `%LocalAppData%\LuBan\AIAgent\` (Windows) or `~/.config/LuBan/AIAgent/` (macOS/Linux)

---

## 🏗️ Solution Architecture

```
luban-agent/
├── luban-agent.slnx          # Solution file (XML format)
├── Directory.Build.props     # Global build properties
│
├── LubanAgentCore/           # Core library (net10.0)
│   ├── Agents/               # Agent profiles (Normal / LLM Wiki)
│   ├── Configuration/        # Configuration management
│   ├── EmbeddingModels/      # AI embedding model files (bge-small-zh-v1.5)
│   ├── Entities/             # Data entities
│   ├── Hosting/              # Hosting integration
│   ├── Infrastructure/       # Infrastructure
│   ├── Models/               # Data models
│   ├── Repositories/         # Data access layer
│   ├── Retrieval/            # Semantic retrieval
│   ├── Services/             # Core services (Agent host / session / workspace)
│   └── Utils/                # Utilities
│
├── LubanAgentCli/            # Command-line TUI (Terminal.Gui v2 · net10.0)
│   ├── App/                  # TUI application layer (bootstrap + DI + theming)
│   │   └── Dialogs/          # Popup components (BaseDialog base + 5 subclasses)
│   ├── Views/                # View layer (pure rendering)
│   ├── ViewModels/           # MVVM ViewModel layer
│   ├── Models/               # Block document model
│   ├── Commands/             # Command implementations
│   └── Program.cs            # Entry point
│
├── LubanAgentCodex/          # Desktop client (Avalonia UI · net10.0)
│   ├── Views/                # Avalonia views
│   ├── ViewModels/           # MVVM ViewModel layer
│   ├── Services/             # UI services
│   ├── Styles/               # Style themes
│   └── Program.cs            # Entry point
│
└── docs/                     # Design & planning documents
```

**Architecture Overview**:

```
┌─────────────────┐  ┌─────────────────┐
│ LubanAgentCli   │  │ LubanAgentCodex │
│ (Terminal.Gui)  │  │   (Avalonia)    │
└────────┬────────┘  └────────┬────────┘
         │                    │
         └────────┬───────────┘
                  ▼
         ┌─────────────────┐
         │ LubanAgentCore  │  Agent runtime / tools / skills / sessions / LLM Wiki / workspaces
         └────────┬────────┘
                  ▼
         ┌─────────────────┐
         │  LuBan.AIAgent  │  Multi-model routing / MCP / orchestration engine
         └────────┬────────┘
                  ▼
     OpenAI / Kimi / DeepSeek / Claude / ... (20+ Providers)
```

- **UI/Business Separation**: CLI and Codex handle only interaction and rendering; all Agent capabilities come from Core
- **Single Source for Embedding Models**: `bge-small-zh-v1.5.zip` is maintained only in Core and propagated to each client's output directory via ProjectReference content flow
- **Unified Configuration**: Both clients share user configuration and session database under `%LocalAppData%\LuBan\AIAgent\`

---

## ⚙️ Configuration

### Application Configuration (appsettings.json)

```json
{
  "LuBanAgent": {
    "DefaultModel": "openai:gpt-4o",
    "SystemPrompt": "You are an intelligent assistant.",
    "MaxToolLoopIterations": 10,
    "Tools": {
      "Browser":    { "Enabled": true, "Headless": false },
      "FileSystem": { "Enabled": true, "AllowedRoots": ["C:\\Work"] },
      "Script":     { "Enabled": true, "Shell": "cmd" },
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

### User Configuration (%LocalAppData%\LuBan\AIAgent\config.json)

User configurations (Providers, custom Skills, rules, connector declarations, etc.) are automatically saved locally, shared by both clients, and loaded on restart.

> Connectors (e.g. the Aliyun DevOps `yunxiao` connector) declare their **availability** in the `connectors` section of `config.json` (an undeclared connector counts as not installed); whether one is enabled in a given workspace is decided by `.luban-agent/connectors/<name>.json`. When an older install has no `connectors` section, a default declaration is seeded on first load.

> 📖 For complete configuration options, see each sub-project's README.

---

## 🔧 Technology Stack

| Component | Description |
|-----------|-------------|
| **.NET 10.0** | Target framework |
| **LuBan.AIAgent** | Agent runtime framework (multi-model routing / MCP / orchestration) |
| **Microsoft.Extensions.AI** | Unified chat client abstraction |
| **Terminal.Gui 2.4.17** | CLI full-screen TUI framework (24-bit TrueColor, alt-screen) |
| **Avalonia 12.1.1** | Codex cross-platform desktop UI framework |
| **CommunityToolkit.Mvvm** | MVVM framework |
| **Microsoft.Playwright** | Browser automation engine |
| **Microsoft.ML.OnnxRuntime** | ONNX model inference (semantic retrieval) |
| **SqlSugar / SQLite** | Session & vector data storage |

---

## 💡 Tips

- 🖥️ **Two Clients, One Core**: CLI suits terminal lovers and server environments; Codex suits GUI users—configuration and sessions are fully shared
- ⌨️ **CLI Shortcuts**: `Esc` cancel task, `Shift+Tab` cycle permission modes, `Tab` toggle task view, `Ctrl+Q` quit, `Ctrl+O` add attachments
- 🛡️ **Four Permission Modes**: Default / Plan / AcceptEdits / BypassPermissions — in-workspace operations need no confirmation, while out-of-workspace and sensitive operations require user confirmation (this-time/turn/session grant tiers)
- 💬 Model routing uses `provider:model` format, supporting 20+ AI providers
- 🧩 **Multi-Agent Orchestration**: AI auto-decomposes complex tasks into DAG with serial/parallel SubAgent execution, and SubAgents inherit workspace long-term memory
- 📂 **Workspace Isolation**: Each workspace has its own session history and config directory (`.luban-agent/`), with a host-stable workspace ID that shares long-term memory
- 🔍 **LLM Wiki Knowledge Base**: After indexing local documents, conversations are automatically augmented with retrieval

---

## 🤝 Related Projects

- **[LuBan.Framework](https://github.com/yswenli/luban-framework)** - LuBan Framework Core
- **[LuBan.AIAgent](https://www.nuget.org/packages/LuBan.AIAgent/)** - AI Agent Runtime
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
