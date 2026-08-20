# LubanAgentCodex

基于 Avalonia 的跨平台 AI 编码代理桌面客户端，参考 OpenAI Codex 桌面版设计。

## 功能特性

- **经典三栏式布局**：左侧边栏 + 主内容区 + 底部输入区
- **工作区管理**：支持多工作区切换、重命名、删除
- **会话管理**：按工作区分组的会话列表，支持会话切换和历史加载
- **流式对话**：支持 AI 流式输出、思考内容展示
- **工具调用**：工具调用卡片（可展开参数）、确认/拒绝流程
- **权限模式**：Default / Plan / AcceptEdits / BypassPermissions
- **RAG 知识库**：支持从指定目录初始化 RAG 知识库
- **技能/规则/MCP 管理**：工作区级别的技能、规则和 MCP 服务管理

## 技术栈

- **框架**：Avalonia 11.2.3
- **目标平台**：.NET 10.0
- **MVVM**：CommunityToolkit.Mvvm
- **AI 引擎**：LuBan.AIAgent
- **数据库**：SqlSugar + SQLite

## 项目结构

```
LubanAgentCodex/
├── App.axaml(.cs)              # 应用程序入口
├── Program.cs                  # 主入口点
├── Styles/
│   └── Colors.axaml            # 主题配色资源
├── Services/
│   ├── AgentHostService.cs     # Agent 宿主服务
│   └── StreamEvent.cs          # 流式事件类型
├── ViewModels/
│   ├── MainWindowViewModel.cs  # 主窗口 ViewModel
│   └── Messages/               # 消息数据模型
├── Views/
│   ├── MainWindow.axaml(.cs)   # 主窗口
│   ├── WorkspacePickerWindow   # 工作区选择器
│   ├── RenameDialog            # 重命名对话框
│   ├── SkillManageWindow       # 技能管理窗口
│   ├── RuleManageWindow        # 规则管理窗口
│   ├── MCPManageWindow         # MCP 服务管理窗口
│   └── Controls/               # 自定义控件
│       ├── Sidebar             # 左侧边栏
│       ├── TitleBar            # 顶部标题栏
│       ├── MessageStream       # 消息流
│       ├── InputBox            # 输入框
│       ├── UserMessageView     # 用户消息
│       ├── AssistantMessageView# AI 消息
│       ├── ToolCallCard        # 工具调用卡片
│       ├── ConfirmCard         # 确认卡片
│       └── SystemMessageView   # 系统消息
```

## 快捷键

| 快捷键 | 功能 |
|--------|------|
| Enter | 发送消息 |
| Ctrl+Enter | 换行 |
| Shift+Enter | 换行 |

## 运行

```bash
dotnet run --project luban-agent/LubanAgentCodex/LubanAgentCodex.csproj
```

## 构建

```bash
dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj
```

## 许可证

Copyright © 2026 yswenli. All Rights Reserved.
