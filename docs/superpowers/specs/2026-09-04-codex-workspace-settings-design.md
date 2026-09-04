# LubanAgentCodex 工作区「设置」中心与侧边栏改版设计

- 日期：2026-09-04
- 状态：待评审（Agent 模式，设计先行）
- 关联仓库：`luban-agent`（子仓库）/ `luban-framework`（NuGet 引用）

## 1. 目标与范围

把工作区级别的「技能 / 规则 / MCP」配置，从一个**读全局 registry 的展示型三窗**（SkillManageWindow / RuleManageWindow / MCPManageWindow）重构为一个**直接读写工作区 `.luban-agent` 目录的配置中心（设置窗）**，并同步改版侧边栏：

1. 移除工作区行上的「⋯」菜单（原含 重命名 / 技能 / 规则 / MCP / 删除）。
2. 工作区行新增「−」删除按钮，点击**带确认弹窗**后删除。
3. 侧边栏底部（版本号 `v1.0.0` 之后）新增「⚙ 设置」按钮，点击弹出设置窗。
4. 设置窗可直接编辑某个工作区的 `skills` / `rules` / `mcps`，内容落盘到该工作区目录下的 `.luban-agent/`。
5. 用户选择左侧工作区时，agent 已按工作区加载这些配置（现有行为，本设计复用，不重新发明）。

**不在本设计范围**：命令面板本身的交互重写、RAG 知识库索引管线的重构（仅复用既有的 `DeleteWorkspaceAsync` 统一删除）、设置窗的「导出/导入」整份工作区配置（`.luban-agent` 打包，按 D8 不做）。

**全局（用户级）配置的处理范围**：「★ 全局」项（D10）UI 写入用户主目录 `~/.luban-agent` 属本设计范围；为使全局 rules/mcps 真正生效，框架层用户级加载（`GlobalLubanAgentPath` + `SkillLoader`/`RuleEngine`/`MCPRegistry` 扫描用户级根）一并纳入（R5 已消解，见第 8 节），不再外置。

## 2. 现状分析（基于真实代码）

### 2.1 侧边栏（`LubanAgentCodex/Views/Controls/Sidebar.axaml(.cs)`）

- 工作区行 Grid 列定义为 `Auto,*,Auto,Auto`：图标｜名称｜`➕` 新建会话｜`⋯` 菜单按钮（`wsMenuBtn`）。
- `wsMenuBtn` 当前 `IsVisible = false`，hover 时显示（`PointerEntered/Exited`）；`Flyout` 含：**重命名工作区 / ⚡技能管理 / 📏规则管理 / 🔌MCP服务 / 🗑️删除工作区** 五项。
- 删除逻辑（`deleteItem.Click`，行 308–330）：
  ```csharp
  var repo = _services!.GetRequiredService<WorkspaceRepository>();
  await repo.DeleteAsync(w => w.WorkspaceId == ws.WorkspaceId);   // ⚠ 物理删除
  LoadWorkspaces();
  ```
  **缺陷**：`DeleteAsync` 来自 `BaseRepository : SimpleClient<TEntity>`（SqlSugar），是**物理删除**；且**不清理由 `rag_file` / `rag_chunk` 表承载的向量索引**，会留下孤儿数据。RAG 知识库的 `DeleteRagAsync` 同样用 `WorkspaceRepository.DeleteAsync`（行 676），同样未清索引。
- 三个「管理」菜单项分别 `new SkillManageWindow / RuleManageWindow / MCPManageWindow` 并 `Show()`——这些是**只读全局 registry 的展示窗**，并不碰 `.luban-agent`。
- 底部信息区（`Sidebar.axaml` Grid.Row=4）含 `LubanAgentCodex` 标题与 `v1.0.0` 版本号（行 66–73），其后无设置入口。

### 2.2 命令面板（`LubanAgentCodex/ViewModels/MainWindowViewModel.cs` 约 342/358/374）

`/skill` `/rule` `/mcp` 等命令仍打开上述三个只读管理窗。本设计将其重定向到设置窗的对应标签。

### 2.3 配置加载（框架层，NuGet 包 `LuBan.AIAgent`）

agent 切换工作区重建时，`AgentHostService.InitializeAsync` 调用 `LoadFromWorkspace(RootPath)`，框架按工作区从 `.luban-agent` 重载：

| 类型 | 目录 | 文件 | 加载器 | 配置类 |
|------|------|------|--------|--------|
| skills | `<ws>/.luban-agent/skills` | `<name>/SKILL.md` | `SkillRegistry` / `SkillLoader` | —（Markdown + YAML frontmatter） |
| rules | `<ws>/.luban-agent/rules` | `*.json` | `RuleEngine` | `CustomRuleConfig` |
| mcps | `<ws>/.luban-agent/mcps` | `*.json` | `MCPRegistry` | `McpServerConfig` |

加载器均 `if (Directory.Exists(...))` 后遍历 `*.json`/`SKILL.md`，解析失败仅记日志不阻断。rules/mcps 配置需 `Enabled == true` 才生效。

**用户级（全局）加载（已实现，2026-09-04）**：三类加载器现均采用**双源扫描**——先加载用户级根 `~/.luban-agent/{skills,rules,mcps}`（`GlobalLubanAgentPath`），再加载工作区 `<ws>/.luban-agent/...`；合并时以 `Id`（skills/rules）或 `Name`（mcps）为键、`OrdinalIgnoreCase` 去重，后加载者覆盖先加载者，即**工作区级覆盖全局级**（详细规则见 4.7）。

## 3. 决策记录

| # | 议题 | 结论 |
|---|------|------|
| D1 | 设置窗配置哪个工作区 | **窗内左栏放工作区列表**，可切任意工作区编辑，不局限于当前选中（已与用户确认） |
| D2 | 重命名如何处理 | **双击工作区名行内改、回车确认、Esc 取消**，保留在主侧边栏，不占按钮位 |
| D3 | 旧三窗如何共处 | **废弃删除**，由设置窗统一接管；命令面板 `/skill` `/rule` `/mcp` 改指设置窗对应标签 |
| D4 | 右侧布局 | **方案 A · 三栏 IDE 风**（左工作区｜中条目｜右编辑器 + 顶部 Tab），参考 WorkBuddy / Trae（已与用户确认） |
| D5 | skills 编辑形态 | **frontmatter 结构化表单（name/description）+ Markdown 正文编辑器**（见 4.4），而非纯文本或纯表单 |
| D6 | 删除语义 | 走统一的 `WorkspaceManager.DeleteWorkspaceAsync`（已实现，逻辑删除 + 级联清理），修正现有物理删除 + 孤儿索引缺陷 |
| D7 | 「应用配置」热加载按钮 | **可选**：「★ 全局」与「当前正在对话的工作区」两个作用域均提供按钮触发 agent 重建热加载；非当前工作区仅落盘，不提供此按钮 |
| D8 | 设置窗是否需要「导出/导入」 | **不需要**：不提供整份工作区配置（`.luban-agent` 打包）的导出 / 导入入口，不预留此功能 |
| D9 | RAG 知识库是否纳入设置窗「工作区列表」 | **不纳入**：左栏仅列 `Type != "Rag"` 的普通工作区（见 4.2）；RAG 知识库的配置不在本设置窗管理范围内，其增删 / 索引管理走既有 RAG 知识库入口 |
| D10 | 是否需要「全局」配置入口 | **需要**：左栏工作区列表**上方**固定一项「★ 全局」，选中后编辑用户级 `.luban-agent`（skills/rules/mcps）；与工作区级并列，均支持「应用配置」热加载（见 4.3 / 4.7） |

## 4. 方案设计

### 4.1 侧边栏改动

**移除「⋯」菜单**
- 删除 `wsMenuBtn` 及其 `Flyout`（`Sidebar.axaml.cs` 行 203–213、233–337、341–342、346、350–351）。
- 工作区行 Grid 列改为 `Auto,*,Auto,Auto,Auto`：图标｜名称｜`➕` 新建会话｜`−` 删除｜（设置入口在底部，不放行内）。

**新增「−」删除按钮**
- 在 `newSessionBtn` 之后插入 `deleteBtn`（内容 `−`，红色 `TextTertiary`/hover 变 `Error`）。
- 点击逻辑：
  ```csharp
  var ok = await Dialogs.ShowConfirmAsync(owner, "确认删除",
      $"确定要删除工作区 \"{ws.Name}\" 吗？",
      "删除后将逻辑删除该工作区、其会话及关联的 RAG 向量索引，可经回收逻辑恢复。",
      "确定删除", danger: true);
  if (ok == true)
      await _workspaceManager.DeleteWorkspaceAsync(ws.WorkspaceId); // D6
  LoadWorkspaces();
  ```
- RAG 知识库行的 `🗑️` 删除同样改调 `DeleteWorkspaceAsync`（路由统一，复用同一级联清理）。

**双击重命名（D2）**
- `wsName` 改为 `PointerPressed` 双击时，将该行名称替换为 `TextBox`（初值 `ws.Name`），`KeyDown`：Enter → 调 `WorkspaceRepository.UpdateAsync` 改名并 `LoadWorkspaces()`；Esc → 取消还原。
- 行级 `PointerPressed` 切换工作区的逻辑保留，但需排除来自 `TextBox`/按钮的源（复用现有 `if (e.Source is Button) return` 模式，并加 `is TextBox` 判断）。

**新增「⚙ 设置」按钮（D3/D4）**
- `Sidebar.axaml` Grid.Row=4 内、版本号 `TextBlock` 之后插入：
  ```xml
  <Button Name="SettingsBtn" Content="⚙ 设置"
          Classes="sidebarFooterBtn" Margin="0,8,0,0" />
  ```
- `Sidebar.axaml.cs` 中 `FindControl<Button>("SettingsBtn")`，`Click` →
  ```csharp
  var win = new SettingsWindow(_services!, _workspaceManager.CurrentWorkspace);
  await win.ShowDialog(owner);   // 模态，关闭后 LoadWorkspaces() 刷新（如有重命名）
  ```
- 事件可经新增 `public event EventHandler? SettingsRequested;` 上抛到 `MainWindow`，由 MainWindow 持有 `SettingsWindow` 实例（与现有 `RagInitRequested` 同一模式）。

### 4.2 设置窗布局（方案 A）

新增 `LubanAgentCodex/Views/SettingsWindow.axaml(.cs)`，Avalonia `Window`，约 900×620，深色沿用 `Colors.axaml`：

```
┌──────────────────────────────────────────────────────────────┐
│  设置 · 工作区配置                                 [×]         │
├───────────────┬──────────────────────────────────────────────┤
│ 配置作用域     │  [ 技能 ] [ 规则 ] [ MCP ]   ← 顶部 Tab        │
│ ───────────── │ ───────────────────────────────────────────  │
│ ★ 全局         │  ┌─────────────┐  ┌──────────────────────┐  │
│ ───────────── │  │ 条目列表      │  │ 编辑器                │  │
│ ▸ 当前工作区   │  │ • code-review │  │ (依类型见 4.4)        │  │
│ ▸ 后端API      │  │ • doc-gen     │  │                      │  │
│ ▸ 运维脚本     │  │ + 新建        │  │ [保存] [删除此条目]   │  │
│ + 新建工作区   │  └─────────────┘  └──────────────────────┘  │
└───────────────┴──────────────────────────────────────────────┘
```
（左栏顶部固定一项「★ 全局」，与工作区列表之间以分隔线隔开；选中「全局」即编辑用户级 `.luban-agent`，见 D10。）

- **左栏**：顶部固定一项「★ 全局」（用户级 `.luban-agent`，见 D10）；其下为 `WorkspaceManager.GetUserWorkspacesAsync()` 过滤 `Type != "Rag"` 的工作区列表，点击切换右侧正在编辑的作用域（高亮当前）。`SelectedScope`（全局 / 某工作区）变化时重新读取对应 `.luban-agent` 目录。与侧边栏主列表共享同一工作区数据源。
- **顶部 Tab**：技能 / 规则 / MCP，切换右侧列表与编辑器内容。
- **中栏条目列表**：按当前 Tab 列出该工作区对应目录条目；`+ 新建` 创建空条目并进入编辑。
- **右栏编辑器**：依类型渲染（见 4.4）。底部 `[保存]` `[删除此条目]`。
- **「应用配置」按钮（可选，D7）**：「★ 全局」与「当前正在对话的工作区」两个作用域均触发 `AgentHostService` 重建以热加载（重建时按「先全局 → 后工作区」顺序合并、去重，见 4.7），无需切换工作区；非当前工作区仅落盘、提示「下次切换到该工作区时生效」。

### 4.3 数据契约（读写落地点）

所有写入均在 `<SelectedWorkspace.RootPath>/.luban-agent/` 下：

| 类型 | 路径 | 单条目 |
|------|------|--------|
| skills | `skills/<name>/SKILL.md` | 目录 = 技能名 |
| rules | `rules/<name>.json` | 文件名 = 规则名 |
| mcps | `mcps/<name>.json` | 文件名 = 服务名 |

目录不存在时首次保存自动 `Directory.CreateDirectory`。

**全局作用域（D10）的路径**：选中左栏「★ 全局」时，上述各类型写入的是**用户级** `.luban-agent`，根目录为**用户主目录**下的 `.luban-agent`（用户已确认，见 2026-09-04 补充）：

```
// 用户已确认：用户级 .luban-agent 位于用户主目录
GlobalRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".luban-agent")
// 即：%USERPROFILE%/.luban-agent  （本机 = C:\Users\yswen\.luban-agent）
//     {skills,rules,mcps} 三个子目录
```

- 该路径由框架统一提供：`LuBan.AIAgent.GlobalLubanAgentPath`（新增，`Root`/`SkillsDir`/`RulesDir`/`McpsDir`），与框架 Playwright 驱动缓存（`~/.luban-agent/playwright-driver`）**同一根目录约定**，Codex 设置窗与框架加载器共用同一来源，避免路径漂移（原设计假设的 `LocalApplicationData/LuBanFramework/AIAgent/.luban-agent` 已废弃，见 R5）。
- 设置窗读取/写入全局项时，直接对 `GlobalLubanAgentPath.SkillsDir` / `RulesDir` / `McpsDir` 下的文件操作，与项目级逻辑完全复用（仅根目录不同）。

### 4.4 编辑器形态（D5）

**技能（SKILL.md = YAML frontmatter + Markdown 正文）**
- 右栏上半：结构化表单 `name`（文本框，必填）、`description`（文本框，必填）、`category`（文本框，默认 `custom`）、`triggers`（逗号分隔关键词，可选）；下半：Markdown 多行编辑器（`TextBox`/AvaloniaEdit）。
- 保存时拼装 `---\nname: ...\ndescription: ...\ncategory: ...\ntriggers: ...\n---\n<正文>` 写回 `SKILL.md`。
- 注：`SkillMdParser` 仅识别 `name` / `description` / `category` / `triggers` 四个 frontmatter 键，其余键会被忽略；`triggers` 按逗号拆分映射到 `TriggerKeywords`。
- 新建：`skills/<name>/` 目录 + 默认 `SKILL.md` 模板。
- 删除：`Directory.Delete(skills/<name>, recursive: true)`。

**规则（CustomRuleConfig，JSON）**
- 结构化表单字段（以框架 `CustomRuleConfig` 为准，共 9 字段）：
  `Id`（文本，主键）、`Name`（文本）、`Description`（文本）、`ActionTypePattern`（动作类型匹配模式，默认 `*`）、`TargetPattern`（目标匹配模式，默认 `*`）、`Action`（命中动作，默认 `deny`）、`Priority`（优先级，默认 `100`）、`Enabled`（开关）、`Content`（可选，供 `IContentRule` 读取的引导文本，如 base-behavior）。
- 提供「查看 JSON」折叠，高级用户可直接编辑原始 JSON。
- 保存：`JsonConvert.SerializeObject(config, Formatting.Indented)` → `rules/<id>.json`。
- 新建/删除：对应 `.json` 文件增删。

**MCP（McpServerConfig，JSON）**
- 结构化表单字段（以框架 `McpServerConfig` 为准，共 6 字段）：`Name`（文本，主键）、`Description`（文本）、`Enabled`（开关）、`Transport`（`stdio`/`http`/`sse` 下拉，默认 `stdio`）、`Command`（文本）、`Args`（字符串列表）。
- 注：`McpServerConfig` **没有**独立的 `Url` / `Env` 字段；http/sse 的 baseUrl 复用 `Args.FirstOrDefault() ?? Command`（见 `HttpMCPClient.SendRequestAsync`），表单在 Transport 切到 http/sse 时应提示「URL 填在 `Command` 或 `Args[0]`」。
- 同样提供「查看 JSON」原始视图。
- 保存：`mcps/<name>.json`；新建/删除对应文件。
- 注：`MCPRegistry` 按 `Transport` 决定 `HttpMCPClient`/`StdioMCPClient`，表单需保证枚举合法。

**统一校验**：保存前 `JToken.Parse` / `JsonConvert.DeserializeObject` 校验 JSON 合法性（规则/MCP）与 frontmatter 可解析性（技能）；失败则在编辑器下方红字提示，不落盘。

### 4.5 删除工作区统一入口（D6）

`WorkspaceManager.DeleteWorkspaceAsync(string)` **已实现**（`LubanAgentCore/Services/WorkspaceManager.cs` 约 445 行），语义即本设计所需：

```csharp
public async Task<bool> DeleteWorkspaceAsync(string workspaceId)
{
    if (string.IsNullOrWhiteSpace(workspaceId)) return false;
    var ragFileRepo = new RagFileRepository();
    var ragChunkRepo = new RagChunkRepository();
    await _sessionRepo.SoftDeleteByWorkspaceAsync(workspaceId);   // 会话软删
    await ragFileRepo.DeleteByWorkspaceAsync(workspaceId);        // RAG 文件物理删
    await ragChunkRepo.DeleteByWorkspaceAsync(workspaceId);       // RAG 切块物理删
    await _repo.LogicDeleteAsync(w => w.WorkspaceId == workspaceId); // 工作区逻辑删
    // 若删除的是当前工作区：移除 PathGuard 根目录、清空当前引用与会话
    ...
}
```

其 XML 注释明确点名了历史上「Sidebar 走物理删除、管理窗口走逻辑删除、Sidebar 遗漏清理 rag_file/rag_chunk 残留孤儿索引」的缺陷——即本设计要收口的那个 bug，已在该方法层面修复。

**本设计只需接线，不新增 Core 代码**：
- 侧边栏工作区「−」删除：把 `repo.DeleteAsync(w => w.WorkspaceId == ws.WorkspaceId)` 改为
  `await _workspaceManager.DeleteWorkspaceAsync(ws.WorkspaceId);`
- `DeleteRagAsync`（RAG 知识库 `🗑️`）：同样改调 `DeleteWorkspaceAsync(model.WorkspaceId)`，删除前仍走 `Dialogs.ShowConfirmAsync` 确认；其额外的「逐个软删会话」逻辑已被该方法内部的 `SoftDeleteByWorkspaceAsync` 覆盖，可删除冗余代码。
- `RagFileRepository.DeleteByWorkspaceAsync` / `RagChunkRepository.DeleteByWorkspaceAsync` **已确认存在**（`RagRepository.cs:52/72`），CLI 的 `RagCommand`/`WorkCommand` 亦在用，无需新增。

### 4.6 废弃旧三窗 + 命令面板重定向（D3）

- `SkillManageWindow` / `RuleManageWindow` / `MCPManageWindow`：从 `Sidebar.axaml.cs` 与 `MainWindowViewModel.cs` 的引用全部移除；文件保留但不再被引用（或在本 PR 内删除，需确认是否别处仍用）。
- `MainWindowViewModel.cs` 命令面板 `/skill` `/rule` `/mcp`：
  ```csharp
  // 改为：
  var win = new SettingsWindow(_services, currentWs);
  win.PreselectTab(Kind.Skill/Rule/Mcp);
  await win.ShowDialog(owner);
  ```
- 确保 `SettingsWindow` 暴露 `PreselectTab(TabKind)` 方法。

### 4.7 生效时机与加载顺序

**加载顺序与去重规则（框架层，已实现）**

agent 每次 `LoadFromWorkspace`（切换工作区 / 重建 / 「应用配置」热加载）时，按「**先全局 → 后工作区**」的顺序合并三类配置，合并时以条目标识为键做**大小写不敏感去重**，后加载者覆盖先加载者，故**同名 / 同 Id 条目以工作区级覆盖全局级**：

| 类型 | 先加载（全局） | 后加载（工作区） | 去重键 |
|------|----------------|------------------|--------|
| skills | `~/.luban-agent/skills` | `<ws>/.luban-agent/skills` | 技能 `Id` |
| rules | `~/.luban-agent/rules` | `<ws>/.luban-agent/rules` | 规则 `Id` |
| mcps | `~/.luban-agent/mcps` | `<ws>/.luban-agent/mcps` | 服务 `Name` |

> skills 另有旧 `LocalAppData/LuBanFramework/AIAgent/skills` 兼容读取源，优先级最低，实际加载顺序为：遗留用户级 → 规范用户级（`~/.luban-agent/skills`）→ 项目级（后者覆盖前者）。

**生效时机**

- **「应用配置」热加载**：「★ 全局」与「当前正在对话的工作区」两个作用域**均**提供「应用配置」按钮；点击即触发 `AgentHostService` 重建，按上述顺序立即重新加载全局 + 工作区配置，无需切换工作区。
- **非当前工作区**：仅落盘，提示「下次切换到该工作区时生效」（agent 仅在切换重建时加载该工作区）。
- **未点「应用配置」**：当前工作区与全局配置均在下次切换 / 重建时按上述顺序生效。

## 5. 错误处理

- 工作区 `.luban-agent` 目录缺失 → 首次保存自动创建；列表为空时显示占位提示「暂无条目，点击 + 新建」。
- JSON / frontmatter 解析失败 → 编辑器内红字提示，拒绝保存。
- 文件 IO 异常（权限/占用）→ `Dialogs.ShowErrorAsync` 提示，不崩溃。
- 删除确认弹窗取消 → 不执行任何操作。
- RAG 索引清理异常 → 记录日志并继续逻辑删除工作区，不让索引清理失败阻塞删除。

## 6. 测试策略

- **单元**：`WorkspaceManager.DeleteWorkspaceAsync` 级联正确性（会话软删、RAG 索引清理、工作区逻辑删），用内存库验证无孤儿数据。
- **集成**：通过 `SettingsWindow` 在某工作区新建一个 skill → 断言 `skills/<name>/SKILL.md` 存在 → 切换该工作区 → agent 能加载该技能（或断言 `SkillRegistry` 含该条目）。
- **手动**：
  1. 侧边栏无「⋯」菜单；hover 也无。
  2. 点「−」弹确认框，取消无变化；确认后工作区从列表消失、其会话与 RAG 索引被清理。
  3. 双击工作区名可重命名，回车生效、Esc 取消。
  4. 点「⚙ 设置」弹出设置窗；左栏切工作区、Tab 切类型、编辑并保存后文件落盘。
  5. 命令面板 `/skill` 打开设置窗并定位技能标签。

## 7. 文件改动清单

新增：
- `LubanAgentCodex/Views/SettingsWindow.axaml` / `.axaml.cs`
- `luban-framework/LuBan.AIAgent/GlobalLubanAgentPath.cs`（用户级 `.luban-agent` 根目录解析，框架根命名空间）

修改：
- `LubanAgentCodex/Views/Controls/Sidebar.axaml`（加设置按钮）
- `LubanAgentCodex/Views/Controls/Sidebar.axaml.cs`（移除「⋯」菜单与 flyout；加「−」删除；双击重命名；设置按钮事件）
- `LubanAgentCodex/ViewModels/MainWindowViewModel.cs`（命令面板重定向）
- （可选删除）`SkillManageWindow` / `RuleManageWindow` / `MCPManageWindow`
- `luban-framework/LuBan.AIAgent/Skills/SkillLoader.cs`（`LoadAll` 用户级改为 `~/.luban-agent/skills`，保留旧 `LocalAppData` 路径兼容读取）
- `luban-framework/LuBan.AIAgent/Rules/RuleEngine.cs`（`LoadFromWorkspace` 先扫描 `~/.luban-agent/rules`）
- `luban-framework/LuBan.AIAgent/MCP/MCPRegistry.cs`（`LoadFromWorkspace` 先扫描 `~/.luban-agent/mcps`）

## 8. 风险与待办

- **R1（已消解）**：`DeleteWorkspaceAsync` 已实现且语义正确（逻辑删除 + 级联清理 + PathGuard 移除），本设计仅为侧边栏接线，无需新增 Core 代码。
- **R2（已核验）**：`RagFileRepository`/`RagChunkRepository` 的 `DeleteByWorkspaceAsync` 已存在，`WorkspaceManager.DeleteWorkspaceAsync` 与 CLI `RagCommand`/`WorkCommand` 均在调用。
- **R3（已消解，2026-09-04）**：`CustomRuleConfig` / `McpServerConfig` / 技能 frontmatter 的精确字段已按框架源码对齐（见 4.4）：规则 9 字段（`Id`/`Name`/`Description`/`ActionTypePattern`/`TargetPattern`/`Action`/`Priority`/`Enabled`/`Content`）、MCP 6 字段（`Name`/`Description`/`Enabled`/`Transport`/`Command`/`Args`，**无 `Url`/`Env`**，http/sse 的 baseUrl 复用 `Args[0] ?? Command`）、技能 frontmatter 四键（`name`/`description`/`category`/`triggers`）。
- **R4**：若「应用配置」热加载实现，`AgentHostService` 重建需保证不中断当前对话上下文（参考既有 `aa98adf` 自愈式重建）。
- **R5（已消解，2026-09-04 实现）**：用户级 `.luban-agent` 加载问题已通过框架层改动解决，用户已确认全局根目录为用户主目录 `~/.luban-agent`（本机 `C:\Users\yswen\.luban-agent`）。具体改动（参考 `lubanagentcli` 的全局配置处理：`SkillLoader` 双源扫描 + `ConfigManager.GetDefaultConfigPath` 用户级根约定）：
  - 新增 `LuBan.AIAgent.GlobalLubanAgentPath`（框架根命名空间）：暴露 `Root`/`SkillsDir`/`RulesDir`/`McpsDir`，统一解析 `~/.luban-agent/{skills,rules,mcps}`。
  - `SkillLoader.LoadAll`：用户级 skills 改为扫描 `GlobalLubanAgentPath.SkillsDir`（`~/.luban-agent/skills`）；保留旧 `LocalApplicationData/LuBanFramework/AIAgent/skills` 作为**兼容读取源**（更低优先级），加载顺序：遗留用户级 → 规范用户级 → 项目级（后者覆盖前者）。
  - `RuleEngine.LoadFromWorkspace`：在扫描 `<ws>/.luban-agent/rules` **之前**，先扫描 `GlobalLubanAgentPath.RulesDir`（用户级低优先级，项目级覆盖）。
  - `MCPRegistry.LoadFromWorkspace`：同上，先扫描 `GlobalLubanAgentPath.McpsDir`。
  - 效果：「★ 全局」写入的 skills/rules/mcps 经「应用配置」热加载或下次 agent 重建 / 工作区切换时生效；加载顺序为「先全局 → 后工作区」、以 Id/Name 大小写不敏感去重、工作区覆盖全局（详见 4.7）。
  - 注：旧 `LocalApplicationData/LuBanFramework/AIAgent/skills` 仅作兼容读取，新全局 skills 一律写入 `~/.luban-agent/skills`；既有位于旧路径的 skills 仍可被读取，建议后续迁移至新路径（不影响功能）。

## 9. 开放问题

（本节两项已于 D8 / D9 确认，无未决问题。）

- 设置窗「导出 / 导入」整份工作区配置：按 **D8** 明确不做，已列入第 1 节范围外，不预留入口。
- RAG 知识库纳入设置窗「工作区列表」：按 **D9** 明确不纳入，左栏仅普通工作区（见 4.2）。
