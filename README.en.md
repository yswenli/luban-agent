# LuBan Agent CLI

<div align="center">

**AI Agent Command-Line Tool Based on Microsoft Agent Framework**

*Empowering Large Language Models with Thinking, Planning, Tool-Calling, and Autonomous Execution*

[![NuGet](https://img.shields.io/nuget/v/LuBan.AIAgent.svg)](https://www.nuget.org/packages/LuBan.AIAgent/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

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
- **11 AI Provider Support**: OpenAI, Azure, DeepSeek, Kimi, GLM, Qwen, Doubao, Claude, Gemini, Ollama, plus custom OpenAI-compatible API
- **Unified `provider:model` Format**: Switch models with one command, no code changes needed
- **Dynamic Routing**: LuBanChatClient automatically dispatches to the corresponding provider based on prefix

### 🛠️ 7 Built-in Tool Groups
| Tool Group | Capabilities |
|------------|--------------|
| 🌐 **Browser Tools** | Navigate, click, type, screenshot, get content (powered by Playwright) |
| 📁 **FileSystem Tools** | Read, write, list directories with secure path restrictions |
| 🔧 **Script Execution Tools** | Execute Shell, Lua, Python scripts |
| 🗄️ **Database Tools** | Execute SQL statements via sqlcmd |
| 🔴 **Redis Tools** | Execute Redis commands via redis-cli |
| 🌍 **Web Tools** | Send HTTP requests to fetch web content |
| 🔍 **Semantic Retrieval Tools** | Index local code/documents and search by semantic similarity |

### 🎯 Skill System
Three core built-in skills, plug and play:
- **Brainstorming**: Explore requirements and design before implementing features
- **Code Review**: Review code, identify issues, and provide improvement suggestions
- **Documentation**: Generate code comments, README files, API documentation

Supports custom Skills to easily extend your unique capabilities.

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

---

## 🚀 Quick Start

### 1. Install Playwright Browser (Required Before Using Browser Tools)

```powershell
# Install browser version matching Microsoft.Playwright 1.61.0
npx playwright@1.61.0 install chromium
```

> **Note**: Browser version must match the Microsoft.Playwright package version. This project currently uses 1.61.0.

### 2. Clone and Run

```bash
# Clone the repository
git clone https://github.com/yswenli/luban-framework.git
cd luban-framework/luban-agent

# Run the program
dotnet run
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
  11. Custom OpenAI-Compatible API
Select (1-11): 1
Enter OpenAI API Key: ********
✓ Provider 'OpenAI' added and saved
  Supported models: gpt-4.1, gpt-4.1-mini, gpt-4.1-nano, gpt-4o...
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

### Command Input Methods

- **Tab Auto-completion** - Type partial command and press Tab to auto-complete
- **Up/Down Arrows** - Browse command history
- **Esc Key** - Clear current input
- **Command Prefix** - All commands start with `/`
- **Number Shortcuts** - Support numbers 1-10 for quick command selection

### Command List

| Command | Shorthand | Number | Description |
|---------|-----------|--------|-------------|
| `/provider` | `/p` | `1` | Manage AI Providers (-list/-add/-update/-delete/-switch) |
| `/model` | `/m` | `2` | Manage Models (-list/-add/-update/-delete/-switch) |
| `/skill` | `/sk` | `3` | View and Execute Skills (-list/-add/-update/-delete/-switch) |
| `/rule` | `/r` | `4` | View and Manage Rules (-list/-add/-update/-delete/-switch) |
| `/mcp` | `/mp` | `5` | View MCP Clients (-list/-add/-update/-delete/-switch/-connect/-tools) |
| `/session` | `/se` | `6` | Manage Chat Sessions (-list/-new/-clear/-switch) |
| `/agi` | `/a` | `7` | General Agent Conversation |
| `/browse` | `/b` | `8` | Website-specific Agent Operations |
| `/stats` | `/st` | `9` | Session & Token Statistics (-days N) |
| `/exit` | - | `10` | Exit Program |

### Sub-command Shorthand

| Shorthand | Full Command | Applicable Commands |
|-----------|--------------|---------------------|
| `-l` | `-list` | All management commands |
| `-a` | `-add` | All management commands |
| `-u` | `-update` | Provider/Model/Skill/Rule/MCP |
| `-d` | `-delete` | Provider/Model/Skill/Rule/MCP |
| `-d` | `-days` | Stats (statistics days) |
| `-s` | `-switch` | All management commands |
| `-n` | `-new` | Session |
| `-c` | `-clear` | Session |
| `-c` | `-connect` | MCP |
| `-t` | `-tools` | MCP |

**Examples**:
- `/p -l` = `/provider -list`
- `/st -d 7` = `/stats -days 7`
- `/mp -c filesystem` = `/mcp -connect filesystem`

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

---

## 🏗️ Project Structure

```
LubanAgent/
├── Commands/              # Command implementations
│   ├── ProviderCommand.cs     # Provider management
│   ├── ModelCommand.cs        # Model management
│   ├── SkillCommand.cs        # Skill management
│   ├── RuleCommand.cs         # Rule management
│   ├── MCPCommand.cs          # MCP client management
│   ├── SessionCommand.cs      # Session management
│   ├── AgiCommand.cs          # General Agent conversation
│   ├── BrowseCommand.cs       # Browser Agent
│   └── StatsCommand.cs        # Statistics
├── Services/              # Core services
│   ├── ConsoleAppService.cs   # Command dispatch & interaction
│   └── SessionManager.cs      # Session persistence
├── Repositories/          # Data access layer
│   ├── SessionRepository.cs   # Session storage
│   └── RagRepository.cs       # RAG data storage
├── Retrieval/             # Semantic retrieval
│   ├── ModelManager.cs        # Embedding model management
│   ├── OnnxEmbeddingGenerator.cs  # ONNX embedding generator
│   └── SqliteVectorStore.cs   # SQLite vector store
├── Infrastructure/        # Infrastructure
│   └── DatabaseInitializer.cs # Database initialization
├── Entities/              # Data entities
├── Model/                 # AI model files
└── Program.cs             # Application entry point
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
| **Microsoft.Agents.AI.Foundry** | Agent runtime framework |
| **Microsoft.Extensions.AI** | Unified chat client abstraction |
| **Microsoft.Playwright** | Browser automation engine |
| **LuBan.DI** | Dependency injection integration |
| **LuBan.Common** | Base interfaces & tool definitions |
| **Microsoft.ML.OnnxRuntime** | ONNX model inference (semantic retrieval) |
| **SQLite** | Session & vector data storage |

---

## 💡 Tips

- 💬 Model routing uses `provider:model` format. Add new Providers via `/provider -add`
- 🛠️ **7 Built-in Tool Groups** cover browser automation, file operations, script execution, database, Redis, web requests, and semantic retrieval
- ⚠️ **ToolConfirmationService** automatically requests user confirmation for dangerous operations (write, delete, execute)
- 🔒 **FileSystemToolOptions.AllowedRoots** restricts file access scope to prevent unauthorized Agent operations
- 💾 **Session History Auto-Persistence** with long conversation compression (SummarizingChatReducer) ensures context is never lost
- 🎨 **Custom Skill/Rule/MCP Persistence** - configurations saved to local files and auto-loaded on restart
- 🛡️ **Rule Interception** automatically checks before tool execution, supporting deny/allow/modify
- 🔌 **MCP Tool Integration** - external MCP server tools automatically exposed to Agent
- 📦 Hot-load external tool plugin assemblies via `ExternalPlugins` configuration
- 🔗 Integrate with RagFlow / Dify / Coze and other AI platforms via [LuBan.AIFlow](https://www.nuget.org/packages/LuBan.AIFlow/)

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
