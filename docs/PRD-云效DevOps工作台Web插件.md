# PRD：云效 DevOps 工作台 Web 插件（界面设计稿配套需求）

- **版本**：v1.0（草案 · 待评审）
- **日期**：2026-11-17
- **作者**：luban 团队
- **状态**：设计定稿（干净版；历次演进见文末「附录 D 变更记录」）

> **本文为设计阶段产物，不含插件实现代码。** 交付两件设计产物：本 PRD 与十屏高保真界面设计稿 `docs/界面设计稿-宿主尺寸预览.html`（826×694，当前有效界面设计稿）。插件工程骨架 `LubanAgent.Plugin.YunXiao`（`plugin.json` id `com.tjces.yunxiao`、`web/index.html` 骨架页、`YunXiaoPlugin` 入口程序集）已就位，六项业务界面与取数通道按本 PRD 落地；本 PRD 不改宿主代码、不改框架、不发包。文中「本仓现状」断言来自只读代码勘察，定位以「文件 + 符号/章节标题」为主锚，不含编造的文件行号；「平台侧能力」断言来自 §4.13 联网核对的官方文档并逐条附 URL。**均为文档核对，未实机调用任何云效 / 企微接口，未在宿主实测官方 MCP。**

- **阅读说明**：本文面向产品、PM 与非技术评审者，**正文不含代码**；接口、清单、数据模型一律用表格与中文说明表达，唯一的代码围栏是 §4.6.1 与 §4.6.2 的 Unicode 方框界面草图。**§3.7 代码挂载点索引表**与 **§4.12 能力缺口清单**属开发实现附录，非技术评审可直接跳过。内部章节引用（§X）用于文内互指，不代表外部设计依据。术语约定：PAT＝个人访问令牌；Frame＝界面设计稿中的一屏；FR＝功能需求；V＝验收断言；R＝风险；O＝开放问题。

---

## 0. 关键结论（先行）

1. **范围仅为设计产物**：本次只交付「界面设计稿 + 本 PRD」，不落地插件业务代码、不改宿主与框架；里程碑 M2/M3 仅为规划（§5）。宿主 Web 插件机制本身已完整落地（`Controllers/PluginController.cs`、`Services/WebPluginService.cs`、`Hosting/WebPluginPaths.cs`、`Hosting/PluginPageManifest.cs`、`Hosting/WebPluginRoutes.cs`、`web/src/pages/plugins/PluginPage.tsx`、`web/src/stores/plugins.ts`）；插件工程骨架 `LubanAgent.Plugin.YunXiao`（`com.tjces.yunxiao`）已就位（清单、入口程序集与骨架页面），**六项业务界面与云效取数通道均待按本 PRD 接线**。
2. **双节奏信息架构 + 明确的页面流转主干**：7 项功能分两类节奏——FR-1~FR-4 是「一次性配置」（连接 / Codeup / Flow / DevOps 项目），FR-5~FR-7 是「周期性作业」（每个迭代重复：读 PRD → 拆分 → 看板开发）。两类节奏的导航、状态与回滚策略分开设计（§2.2、§4.1）。**页面层级（§2.9）**：首次打开先做云效连接门禁 → 插件首页是项目列表（不是看板，**只读自云效、插件内不提供新建项目**）→ 点击项目进迭代任务看板 → 看板页看项目详情进入项目迭代需求页；首次点击且尚未初始化的项目先进入项目初始化（复制母版新建或同步既有），完成后自动进入看板。看板是「项目工作台」，不是插件首页。
3. **能力缺口不悬空，且须区分「本仓未接线」与「平台零能力」**：本仓 `LubanAgentCore/Connectors/AliyunDevOps/` 现状仅 13 个 MCP 工具（Codeup 7 + Flow 6），项目 / 迭代 / 工作项 / 状态枚举 / 测试 / 看板 / 企微云盘**在本仓均未接线**。但联网核对确认：**云效平台侧这些能力齐备**，且阿里云已发布官方托管 MCP Server（§4.13；其公开工具清单未覆盖创建项目本体，见 §4.13.2）。全文用 §4.12 三张表把「已具备（表 A）/ SDK-only 待包装（表 B）/ 本仓待接线（表 C）」逐行列出，每个 FR 的「依赖能力」与每条风险都回指到具体行，不允许「默认能做」的隐含假设。一期范围与禁用占位态不因平台能力齐备而自动放开（决策项见 O6）。
4. **5 处诉求必须重定义**：FR-1「填云效地址 + accessToken」因 Endpoint 固定（官方中心版接入点）、WebHost 无凭据写入 API 而重定义；FR-3「复制现有流水线」因云效 OpenAPI 无复制接口而重定义为「以母版流水线为来源新建/覆盖」（官方推荐做法，§4.13.4 第 1 条）；FR-4「创建或设置项目」收敛为「设置（选择已有）」——插件内不提供新建项目，创建动作留在云效控制台（§4.13.3 C3 行）；FR-5 的 PRD 来源为「默认云效同步 + 本地补充」，企微云盘因同源 iframe 读不到浏览器本机 FS、企微微盘需独立应用鉴权且微文档不可下载而一期禁用（§4.13.3 C21 行）；FR-7「用 Agent 调 brainstorming 完成开发」因 brainstorming 是设计门禁技能（方案未明确批准前不得写代码）而重定义为「**看任务详情（含评论）→ 在工作区开始任务**」：插件只负责把任务上下文交给工作区会话，方案先行的约束交由技能自身门禁在会话内兑现，不再由插件编排成两阶段面板。详见 §4.3 各 FR「设计取舍与差异」与 §6。
5. **看板状态列动态生成**：迭代任务看板的列名、列数、列序必须由云效工作项状态枚举动态生成，禁止硬编码「未开始/开发中/已提交/已完成」四列（§4.3 FR-7、§4.8 V7-3、§6 R5）；「预计完成时间」排序需处理空值与时区（§6 R8）。
6. **视觉一致性与尺寸基准**：界面设计稿 9 个 Frame 统一 **826×694**（宿主 iframe 实际可用区域，实测推导见 §2.6），贴合宿主浅色令牌（`LubanAgentWebHost/web/src/styles.css` 的 `:root`），正文 14px / 行高 1.6 / 按钮 34px / 圆角 8·12·18px；纯手写 CSS、无 UI 框架。宿主已有顶栏与 258px 左侧栏，插件 iframe 内不再自绘重复的侧栏与顶栏；每屏顶部用面包屑标明当前层级，底部固定动作栏只放动作按钮（说明性文字不进界面）。宿主无可复用的看板列类与独立数据表格类，看板列、表格、抽屉均自绘（§2.6、§4.6）。
7. **联网核对结论（§4.13）**：`openapi-rdc.aliyuncs.com` 是云效官方**中心版**统一接入点，PAT + `x-yunxiao-token` 对应 `oapi/v1` 新接口通道，中心版路径必须带 organizationId；云效官方已提供项目/迭代/工作项/状态工作流/测试用例/测试计划的 OpenAPI，以及覆盖 7 个 toolsets、150+ 工具的托管 MCP（`https://openapi-rdc.aliyuncs.com/ai/mcp`）；`GetPipeline` 返回 `pipelineConfig.flow`（YAML），官方确无「复制流水线」接口而推荐「复制 YAML」；企微微盘有 `wedrive/file_list`、`file_info`、`file_download` 三接口但微文档不可下载。故 §4.12 表 C 的 C1–C20 **不是「平台需新增」，而是「本仓需接线」**。以上均为官方文档核对，未实机调用验证。

---

## 1. 背景与目标

### 1.1 现状（逐条可核）

| # | 现状断言 | 可核依据（文件 + 符号/章节） |
|---|----------|------------------------------|
| 1 | Web 插件机制已完整落地：安装 / 启用 / 停用 / 升级 / 卸载、页面清单回读、镜像与路由 | `Controllers/PluginController.cs`、`Services/WebPluginService.cs`、`Hosting/WebPluginPaths.cs`、`Hosting/PluginPageManifest.cs`、`Hosting/WebPluginRoutes.cs` |
| 2 | 插件页由同源无 sandbox iframe 嵌入，hash 路由，Cookie 自动携带，插件 JS 可直连宿主 REST/SSE | `web/src/pages/plugins/PluginPage.tsx`、`web/src/stores/plugins.ts`；`2026-11-13-web-plugin-management-design.md` §5 硬约束 3、§8 功能规格 |
| 3 | 插件体系设计已定案，含硬约束、决策、模块、功能规格、验收 | `docs/superpowers/specs/2026-11-13-web-plugin-management-design.md` v2（16 节）：§5 硬约束、§6 D1–D7、§7 M1–M17、§8 功能规格、§14 验收 |
| 4 | **本插件的工程骨架已就位**：`plugin.json`（id `com.tjces.yunxiao`、`webPage: web/index.html`）、最小入口程序集、骨架页面（六项功能卡片均为「待接线」占位）；已入 `LubanAgent.slnx` | `LubanAgent.Plugin.YunXiao/`（`plugin.json`、`YunXiaoPlugin.cs`、`web/index.html`、`web/assets/base.css`、`LubanAgent.Plugin.YunXiao.csproj`） |
| 5 | 桌面端「新建项目」插件是可参考的插件形态（任务上下文装配、步骤提示、工具白名单） | `LubanAgent.Plugin.NewProject/`（`Services/HostAssistantBridge.cs`、`Services/StepPrompts.cs`、`Services/AssistantToolPolicy.cs`、`skills/newproject-*.md`、`docs/pipeline-*.yml.md`、`docs/org-reference.md`、`newproject.settings.json`） |
| 6 | 云效连接器仅 13 个 MCP 工具（Codeup 7 + Flow 6），Agent 侧加前缀 `mcp_yunxiao_` | `LubanAgentCore/Connectors/AliyunDevOps/Mcp/YunxiaoToolDefinitions.cs` 定义工具；`Mcp/YunxiaoMCPClient.cs` 负责暴露 |
| 7 | SDK 有「读流水线详情」「更新代码仓库」方法但未包装为 MCP 工具 | `LubanAgentCore/Connectors/AliyunDevOps/FlowClient.cs` 的 `GetPipelineAsync`；`CodeupClient.cs` 的 `UpdateRepositoryAsync` |
| 8 | 配置节、凭据键、Endpoint、鉴权头已固定，且 Endpoint 与官方中心版接入点一致 | 配置节 `LuBanAgent:Connectors:Yunxiao`；凭据键 `connector:yunxiao:token` / `connector:yunxiao:organizationid`（`AliyunCredentialKeyAliases.cs`）；Endpoint `openapi-rdc.aliyuncs.com`（`YunxiaoApiOptions.cs` 的 `Endpoint`）；鉴权头 `x-yunxiao-token`。官方「服务接入点」文档确认该域名为**中心版统一接入点**，且 organizationId 仅中心版必填（§4.13） |
| 9 | 云效 OpenAPI 无「复制流水线」接口，但 `GetPipeline` 可读 YAML；git 类工具已从连接器删除 | 历史 `flow_clone_pipeline`、`codeup_clone`、`codeup_push` 已删（`PRD-插件体系与新建项目插件.md` 修订说明）；官方 `GetPipeline` 返回 `pipelineConfig.flow`，官方产品文宣明确「复制 YAML + 创建/更新 OpenAPI」为推荐做法（§4.13） |
| 10 | 项目 / 迭代 / 需求 / 任务 / 缺陷 / 状态枚举 / 测试用例 / 测试计划 / 看板 / 企微云盘读取**在本仓连接器均未接线**；但云效平台侧与企微平台侧能力齐备 | 本仓侧：`LubanAgentCore/Connectors/AliyunDevOps/` 内无对应工具与客户端方法。平台侧：云效官方 OpenAPI 与官方托管 MCP Server、企微微盘 OpenAPI，逐条文档见 §4.13 |
| 11 | WebHost 无云效设置界面、无凭据写入 API、**未注册可写凭据存储**（框架默认实现只读）；桌面端设置仅 orgId + PAT、无地址输入框 | `LubanAgentCodex/Security/YunxiaoSettingsService.cs`；WebHost `Controllers/` 与 `Services/` 内无对应入口；`luban-framework/LuBan.AIAgent/LuBanAgentExtensions.cs` 的 `TryAddSingleton<ICredentialStore>`、`Plugins/Security/ConfigurationCredentialStore.cs` 的 `SetAsync` 抛 `NotSupportedException`；可写实现仅 `LubanAgentCodex/App.axaml.cs` |
| 12 | 上传 / 文件读取 / 工作区元数据 / Agent 会话与 SSE 通路已有 | `Controllers/AttachmentController.cs` 的 `Upload`（JSON + Base64，落盘 `.luban-agent/temp/uploads/<yyyyMMdd>/`）、`Controllers/FileController.cs` 的 `Read`、`Services/WebFileService.cs`、`Controllers/WorkspaceController.cs`、`Controllers/AgentController.cs`（Send / Chat / Active / Attach / Confirm / Cancel）、`Services/WebAgentService.cs` |
| 13 | 头脑风暴技能是「设计门禁」技能 | `luban-framework/LuBan.AIAgent/Skills/BuiltIn/BrainstormingSkill.cs`，`Id = "brainstorming"`，`PromptTemplate` 含「硬门禁」段 |
| 14 | git 动作只有 Agent 工具组，无 REST 入口 | `luban-framework/LuBan.AIAgent/Tools/Git/GitToolPlugin`（clone / commit / push / status / diff）；WebHost 侧无 git 控制器 |
| 15 | 视觉令牌可直接复用 | `LubanAgentWebHost/web/src/styles.css` 的 `:root` |
| 16 | 旧「新建项目」插件的默认推送分支口径（**本插件不沿用**） | `LubanAgent.Plugin.NewProject/newproject.settings.json` 的 `pushBranch = "develop"` 与 `{Code}-Mixed` 库名格式；本插件按母版默认分支 `master`，命名派生沿用 `{代号}-Mixed` 约定 |

> **上游依据与核对边界**：`docs/superpowers/specs/2026-11-09-web-ui-design.md` 未涉及插件管理；本 PRD 的**功能规格、界面草图与分期决策**为按用户 7 项诉求与上述现状推导的设计，除已定案的 Web 插件管理设计外，**无产品设计类上游文档可引**。云效 / 企微平台侧能力已于 2026-11-16 联网核对官方文档，逐条 URL 与结论见 §4.13。核对方式仅为阅读官方文档，**未实机调用任何云效或企微接口**，也未运行构建或测试。

### 1.2 痛点

- **初始化链路割裂**：新项目要在云效控制台、Codeup、Flow、DevOps 项目之间来回切换，靠人工记忆母版库名、母版流水线名与命名规则、组织 ID 与目标分支，易错且不可复核——尤其「改个代号要同步改库名、三个子目录名、三条流水线名」这类一致性工作，人工极易漏改。
- **PRD 到研发资产靠手工搬运**：PRD 文档里的需求、任务、测试用例、测试计划要人工逐条录入云效，迭代与工作项状态口径不统一，拆分结果不可追溯。
- **迭代进度不可视、状态口径不一致**：团队需要一个按任务状态分类、按预计完成时间排序的看板，但云效工作项状态随项目工作流配置变化，任何写死四列的界面都会与实际状态错位。
- **「让 Agent 干活」缺上下文、也缺门禁**：任务真正有用的信息一半在详情、一半在评论里（分支要求、字段高亮、踩过的坑），而 Agent 从零开始问一遍等于把评论重打一次；直接让 Agent 动手又跳过方案评审——brainstorming 技能本身禁止「未获明确批准就写代码」，插件不该再重复实现一遍这套判定，而应把任务上下文组装好交给这个技能。

### 1.3 目标（可验证）

| 编号 | 目标 | 可验证判据 |
|------|------|------------|
| G1 | 以单个 Web 插件承载 7 项功能，且不改宿主代码 | 插件通过 `plugin.json` 的 `uiContributions[].webPage` 挂载；§3.7 代码挂载点索引表除插件自身外全部标注「不改，仅索引」 |
| G2 | 一次性配置向导可复核、可留存、可重跑 | FR-1~FR-4 每步有「只读预检 + 用户确认 + 执行回读」，配置写入档案；§4.8 V1~V4 覆盖 |
| G3 | PRD 可解析并拆分为云效研发资产 | FR-5/FR-6 产出迭代、需求、任务、测试用例、测试计划的拆分明细与落库回执；§4.8 V5/V6 覆盖 |
| G4 | 看板按云效实际状态动态分栏、按预计完成时间排序；任务详情可看评论，并能把「标题 + 详情 + 评论」直接带入工作区开始任务 | V7-3 列名/列数/列序与 `workitem_list_statuses` 返回一致；V7-4 任务详情含评论列表；V7-5 首轮输入完整包含标题、详情与评论；V7-6 方案未确认前零实现动作（由技能门禁保证） |
| G5 | 能力缺口显式化，不出现「默认能做」 | §4.12 三表逐行标注落点；每条缺口在 FR 或 §6 有落点；§6 十六条风险（R1 ~ R16）均给现状依据与处置 |

### 1.4 非目标

- 不落地插件业务代码、不改宿主 WebHost、不改 `LubanAgentCore`、不改 `luban-framework`、不发包、不运行构建与测试（本次仅设计产物）。
- 不改宿主左侧栏、不新增宿主 hash 路由；面包屑与内部跳转由插件 iframe 内部自绘。
- 不做「纯 HTML 插件」：`entryAssembly` 必填且入口类实现 `IPlugin`。
- 不做企微云盘一期接线（P2 开放，界面禁用占位）。
- 不做流水线「官方复制」、不做 git 克隆/推送 REST 工具（git 由框架 git 工具组经工作区会话执行，本插件只编排）。
- 不做状态列硬编码、不做双向实时同步、不做断点续跑、不做远端产物自动删除。
- 不覆盖云效 OpenAPI Endpoint、不引入私有化地址输入框（列 §6 R4 开放问题）。
- 不托管 PAT 生命周期（生成、轮换由云效控制台负责），插件只做校验与安全保存。

---

## 2. 通用设计原则

### 2.1 契约优先

插件只依赖 `LuBan.AIAgent` 契约程序集，宿主只依赖契约、不依赖插件内部类型；前后端交互统一走宿主既有 REST/SSE 契约，插件 JS 不自带后端。所有云效能力以「MCP 工具 / 宿主接口」为边界，插件不直连云效 SDK（与 `LubanAgent.Plugin.YunXiao.csproj` 打包约束一致）。

### 2.2 一次性配置 vs 周期性作业双节奏

| 维度 | 配置向导（FR-1~FR-4） | 周期性作业（FR-5~FR-7） |
|------|------------------------|--------------------------|
| 触发频率 | 每项目一次，配好即留存 | 每迭代重复 |
| 数据落点 | 连接档案、代码库档案、流水线档案、项目档案 | PRD 文档、拆分计划、迭代、工作项、看板、Agent 作业 |
| 导航位置 | 首次打开的连接门禁 + 项目列表上的初始化入口 | 首页右栏的「PRD 工作台」入口、迭代任务看板上的「PRD 工作台」「项目详情」「打开任务」入口 |
| 交互范式 | 只读预检 → 用户确认 → 执行 → 回读结论 | 选迭代 → 浏览/拆分 → 看板作业 → 任务详情 → 在工作区开始任务 |
| 回滚口径 | 远端一律不自动删，只列清单；本地自动回收 | 拆分结果不自动删，重跑按幂等键复用 |
| 状态机 | 附录 A（配置向导） | 附录 B / C（任务会话 / 看板任务） |

### 2.3 同源 iframe 与宿主打通

插件页经同源无 sandbox iframe 嵌入（`web/src/pages/plugins/PluginPage.tsx`），`luban_auth` Cookie 自动携带（`2026-11-13-web-plugin-management-design.md` §5 硬约束 3），因此插件 JS 可直接 `fetch` 宿主 REST 与 SSE；不得改用「沙箱 + Bearer」，否则子资源无法带 Authorization 会全 401。面包屑在 iframe 内自绘，不改宿主侧栏与路由。

### 2.4 最小权限与凭据隔离

PAT 只在服务端保存与取用，绝不进入页面 DOM、日志、SSE 回显与操作记录；`/system` 特权动作只认固定 Bearer、插件页面调不到（`Services/WebSystemService.cs`）；凭据写入必须走新增的**非特权**写入端点（§4.12 表 C 行 C22），并同时补齐可写凭据存储注册（C28）与校验入口（C29），不得复用特权通道。**「非特权」不等于「无权限」**：同源 Cookie 对全部非特权控制器有效，插件前端只允许调用 §3.4 H10 白名单内的端点（现状与处置见 R14）。插件宿主身份固定默认用户（`PluginHostContext` 的 `UserId`），本 PRD 不假设能切换为当前登录用户。

### 2.5 能力缺口显式化

任何 FR 若依赖尚未落地的能力，必须在「依赖能力」项回指 §4.12 具体行，并在 §6 给风险处置；界面上对**本仓未接线**的入口做禁用占位而非隐藏或伪可用（FR-5 企微云盘）；唯一的例外是 FR-4 的项目创建——它既未接线、又不是本插件的必经步骤，因此**整体不做入口**，只在列表底部写明「新建项目请在云效控制台完成」，避免用户对着一个永远点不动的按钮猜。注意区分「本仓未接线」与「平台零能力」：后者经 §4.13 核对后基本不成立，禁用理由应写「未接线 / 待确认权限边界」，不得写成「平台不支持」。

### 2.6 视觉一致性与尺寸基准

统一采用宿主 `:root` 令牌（`LubanAgentWebHost/web/src/styles.css`）：背景 `#f3f4f6`、卡片 `#fff`、次级面 `#f7f8fa` / `#eef0f4`、边框 `#e5e7eb` / `#d5d9e0`、主文字 `#17191d`、次文字 `#5f6670` / `#9aa1ab`、强调 `#3d5afe`（hover `#2f4bea`、soft `#eef1ff`）、危险 `#d92d20`、警告 `#b54708`、成功 `#067647`；圆角 8 / 12 / 18px；正文 14px、行高 1.6、按钮高 34px、紧凑标签 28px；系统 UI 字体栈；阴影 `0 1px 2px rgba(16,24,40,.05)` 与 `0 1px 3px rgba(16,24,40,.06), 0 10px 30px rgba(16,24,40,.07)`；无 UI 框架。宿主无可复用 kanban 列类与独立数据表格类，看板列、表格、抽屉均自绘。

**宿主尺寸基准（实测）**：插件页面经 `PluginPage.tsx` 以同源 iframe 嵌入宿主，不是独立浏览器窗口。以下取值以真实 `web/src/styles.css` 复刻宿主 DOM（`.app` / `.app-topbar` / `.shell` / `.home-side` / `.plugin-main` / `.plugin-page-head` / `.plugin-frame` / `.app-footer`），在无头浏览器 1440×900 视口读取 `plugin-frame` 的 `getBoundingClientRect()` 得到，与逐项相减结果一致：

| 项 | 取值 | 依据 |
|----|------|------|
| 宿主外壳 / 顶栏 | `max-width: 1120px`，居中 | `.app-topbar`、`.shell` |
| 宿主左侧栏 | `flex: 0 0 258px` | `.home-side` |
| 主内容区左右内边距 | 合计 34px | `.home-main` / `.plugin-main` `padding: 0 22px 0 12px` |
| 插件 iframe | `width: 100%`，1px 边框，12px 圆角 | `.plugin-frame` |
| **可用宽度** | **1120 − 2 − 258 − 34 = 826px** | 上述四项相减 |
| **可用高度** | **≈ 694px（1440×900 窗口）** | 900 − 32 应用内边距 − 46 顶栏（`.app-topbar` `height: 46px`，全局 `box-sizing: border-box` 已含 1px 边框） − 5 外壳上边距（`.shell` `margin-top: 5px`） − 30 页脚（`.app-footer` `padding: 10px 0 2px` + 11.5px×1.6 文本行） − 2 外壳边框 − 75 插件页头（`.plugin-page-head` `padding: 16px 0 12px` + 标题块 47px） − 16 页头下留白（`.plugin-frame-wrap` `padding-bottom: 16px`） = 694 |
| 窗口变宽的影响 | 仅增加两侧灰底留白，iframe 宽度不变 | 外壳恒为 1120px 居中 |

> **尺寸口径**：826×694 是 iframe **外框**（含 1px 边框），文档内实际视口为 **824×692**；设计稿按 `box-sizing: border-box` 绘制，故外框取值与视觉一致。屏内再扣 44px 面包屑、46px 动作栏与 2px 边框，内容区为 **602px**（十屏按此绘制，长内容在内容区内滚动而非撑破画板）。**紧凑插件页头已拍板向宿主提需求（O11）**：单行页头（标题 + 重新加载 / 插件管理图标按钮）32px + 下留白 8px，替代现状 75 + 16 = 91px，内容区由 694px 增至**约 745px**（折叠隐藏页头则约 785px）。该改动属宿主侧；落地前本稿一律按 694px 基准绘制、内容区自适应滚动，宿主落地后自动获得余量、不返工。

配套十屏高保真预览稿见 `docs/界面设计稿-宿主尺寸预览.html`：iframe 内 44px 面包屑标明层级（触发条件与去向由页内提示与空态文案承载），底部 46px 动作栏**只放按钮**，不放说明性提示文字；内容区 10–12px 内边距。

### 2.7 任务详情与工作区会话（单入口，不做两阶段面板）

任务下钻页只做两件事：**把任务读清楚**、**把任务带进工作区**。

- **读清楚**：任务标题、任务描述、验收标准、所属需求 / 迭代 / 代码库与分支，以及**评论列表**（取自云效工作项评论，按时间倒序，最新一条置顶标记）。评论可继续发表，发表经云效官方接口写回，不在插件本地另存一份。
- **带进去**：主操作「在工作区开始任务」把「任务标题 + 任务详情 + 全部评论」拼成首轮输入，在**当前项目的本机工作目录**（即宿主工作区，FR-2 已设定）下创建会话，指定 brainstorming 技能、以方案模式启动，并把流式输出回显在页面内。会话同时出现在宿主左侧会话列表，用户可回到宿主里继续同一段对话。
- **门禁归属**：`BrainstormingSkill` 的硬门禁（方案未获明确批准前不写代码、不建骨架、不调实现类技能）仍然生效，但**由技能在会话内兑现**：技能产出方案 → 用户在对话里明确确认 → 技能才进入实现。插件不提供「批准方案」按钮，也不画第二阶段面板，避免把技能的判定复制成一套易失真的 UI 状态。
- **只留绑定，不留状态机**：插件记录「任务 ↔ 会话 ↔ 工作区」绑定与首轮输入摘要（数据模型 `TaskSession`），用于回到任务时继续同一段会话、以及在任务上显示「已开始」；不维护 brainstorm / approved / implement 这类第二套状态。

**取舍说明**：曾有草案把技能门禁翻译成界面上「阶段一 / 阶段二 + 批准方案」两段，已取消。原因是这套 UI 会把「方案是否已获批准」变成插件自己维护的一份状态，与技能内部的真实判定并行存在，两边一旦不一致（用户在宿主会话里直接确认、或技能要求补充澄清）界面就会说谎；而用户真正需要的是「任务信息别丢、进去就能干、别在我点头前改代码」，这三件事单入口都能满足。

### 2.8 UI 贡献模型与导航边界

插件以 `plugin.json` 的 `uiContributions[].webPage`（`web/index.html`）贡献页面；WebHost 回读原始 json（框架 `PluginManifest` 丢弃未知字段）后镜像静态资源；hash 路由 `#/plugin/{pluginId}/{entryId}` 无 SPA 回退，插件内部用自身 hash 片段管理屏与层级，不触碰宿主路由表。面包屑由 iframe 内自绘，宿主左侧栏与路由表保持不变。

### 2.9 信息架构与页面流转

**主干流程**（8 张业务屏 + 2 张非业务屏；屏与屏之间可直接跳转，不平铺为可随意点开的 Tab）：

> **屏与 Frame 的对应**：HTML 预览稿共 10 屏（屏 0–9），Frame 共 9 个——F0 拆为屏 0（流程总览）与屏 9（设计令牌与硬约束）、F3 复用屏 2（首页）与屏 5（项目详情 · 迭代需求）、F5 与 F6 合并为屏 3（项目初始化），F1 / F2 / F4 / F8 / F9 各对应一屏；非业务屏即屏 0 与屏 9。F7（创建新项目）不设。

| 步骤 | 触发条件 | 到达页面 | 完成后去向 |
|------|----------|----------|------------|
| 1 | 插件首次打开，判断「是否已保存有效云效连接」 | 否 → **云效连接**（全屏阻断的初始化门禁） | 保存成功后自动进入项目列表 |
| 1' | 同上，判断为「是」 | **项目列表**（插件首页）· 左栏项目列表 + 右栏默认项目统计 | — |
| 2 | 左栏点某个项目 | 右栏切换为该项目的**四个入口**：项目详情 · 迭代需求 / 迭代任务 / 迭代测试 / PRD 工作台 | 由四个入口分别下钻 |
| 2' | 项目尚未初始化（未绑定代码库与流水线） | 点任一入口先进入**项目初始化** | 选择「复制母版新建」或「同步既有」：新建时**选一个母版代码库（`TJCES-Mixed` 这类）→ 填新项目代号（`RGMS`）→ 系统派生并预览新库名 / 三个子目录名 / 三条流水线名**，确认后克隆母版、本地改目录名、推送到新库；随后复制三条**只绑 dev** 的流水线（名字同样按代号派生、后端只填监听端口）；完成后进入**所选入口**（未指定时进看板） |
| 2a | 右栏点「项目详情 · 迭代需求」 | **项目详情 · 迭代需求**（二级） | 切换当前迭代后，看板与测试数据随之刷新 |
| 2b | 右栏点「迭代任务」 | **迭代任务看板**（二级工作台） | 继续下钻任务详情、项目详情、PRD 工作台 |
| 2c | 右栏点「迭代测试」 | **迭代测试 · 用例与计划**（二级） | 生成测试用例、设置测试计划 |
| 2d | 右栏点「PRD 工作台」 | **PRD 工作台**（辅助作业页） | **默认同步当前云效项目的 PRD 与需求描述**，也可上传本地文档作补充；拆分为迭代、需求、任务、测试用例、测试计划，写入当前项目（与 3' 同一页，入口不同） |
| 3 | 看板页点任务卡 | **任务详情（含评论）** | 读描述、验收标准与评论；一键把标题、详情与评论带入工作区开始任务 |
| 3' | 看板页点「PRD 工作台」 | **PRD 工作台** | 与首页右栏入口同一页；拆分结果回写到当前项目与当前迭代 |

**首页左右分栏的硬约束**：

| 区域 | 宽度 | 内容 | 行为 |
|------|------|------|------|
| 左栏 · 项目列表 | 固定 246px | 标题与项目计数、搜索框、项目条目（名称、迭代与任务摘要、初始化状态）、底部说明 | **条目多时只在左栏内部上下滚动**，不撑高整页、不挤压右栏；搜索为输入即筛选；**不提供新建项目入口** |
| 右栏 · 内容区 | 自适应（约 524px） | 两种互斥面板：项目统计（未选项目时的默认）/ 选中项目的详情与四入口（含 PRD 工作台） | 面板切换不改变左栏选中态；右栏内容超高时自身滚动，不牵引左栏 |

**页面层级**：入口门禁＝云效连接；一级＝项目列表（插件首页，左列表右内容）；二级＝项目初始化（条件分支）、项目详情 · 迭代需求、迭代任务看板、迭代测试 · 用例与计划；三级＝任务详情与工作区；**PRD 工作台为辅助作业页**，首页右栏与看板两处入口均可达。另有两张非业务屏：信息架构与页面流转总览、设计令牌与宿主硬约束。

**判断规则**：「已初始化」以「是否已绑定代码库与流水线」为准，须在左栏每条项目上显式标注，让用户点下去之前就能预期结果；未初始化项目点任一入口都先走初始化，不允许出现「点了没反应」。项目列表只读自云效组织，**插件内不提供创建项目**：需要新建时在云效控制台完成，回插件刷新列表即可（§4.13.3、§4.13.4 第 6 条）。

**返回路径唯一**：任务详情回迭代任务看板；PRD 工作台回到来处（首页右栏进入则回首页，看板进入则回看板，界面底部提供两个返回动作）；看板、项目详情、迭代测试回插件首页；首页可回云效连接设置。每屏顶部面包屑标明层级，底部动作栏给返回与下一步（只放按钮）。

**屏序 ↔ F 编号映射**（保留 §4.6 / §4.12 / §4.8 的 F 编号承重结构）：

| 屏序 | 页面 | 对应 F 编号 | 页面定位 |
|------|------|-------------|----------|
| 0 | 信息架构与页面流转总览 | F0（说明部分） | 非业务屏 · 说明层级、判断与返回路径 |
| 1 | 云效连接 | F4 | 入口门禁 · 首次打开 |
| 2 | 项目列表（左右分栏） | F3（插件首页） | 插件首页 · 左栏列表 + 右栏统计 / 项目详情 |
| 3 | 项目初始化：代码库 + 流水线 | F5 + F6（合并） | 二级条件分支 · 新建 / 同步双模式 |
| 4 | 迭代任务看板 | F1 | 二级工作台 · 三条下钻路径起点 |
| 5 | 项目详情 · 迭代需求 | F3 的三栏部分 | 二级 · 首页右栏或看板进入 |
| 6 | 任务详情与工作区 | F2 | 三级 · 看板点任务卡进入 |
| 7 | PRD 工作台 | F8 | 辅助作业页 · 首页右栏或看板进入 |
| 8 | 迭代测试 · 用例与计划 | F9 | 二级 · 首页右栏「迭代测试」入口 |
| 9 | 设计令牌与宿主硬约束 | F0（规范部分） | 非业务屏 |

> 合并说明：F5 与 F6 合并为一屏「项目初始化」，因为「复制/同步代码库 + 流水线」是一个连续动作，826px 宽度下拆两屏会割裂因果；F7（创建新项目面板）不设——插件内不提供新建项目入口，项目列表只读自云效，故 Frame 共 9 个（F0–F6、F8、F9）。

---

## 3. 落地设计（不改宿主）

### 3.1 宿主与插件挂载点

| 环节 | 落点 | 说明 |
|------|------|------|
| 页面贡献 | `plugin.json` → `uiContributions[].webPage` | 指向插件包内 `web/index.html`，由 WebHost 回读原始 json 取得（骨架已就位） |
| 清单回读 | `Hosting/PluginPageManifest.cs` | 绕开框架 `PluginManifest` 丢弃未知字段的限制 |
| 资源镜像 | `Hosting/WebPluginPaths.cs`、`Services/WebPluginService.cs` | 镜像到输出目录 `wwwroot/plugin-web/{id}/{stamp}/web/...` |
| 路由 | `Hosting/WebPluginRoutes.cs` | hash 路由命中插件页，无 SPA 回退 |
| 前端嵌入 | `web/src/pages/plugins/PluginPage.tsx`、`web/src/stores/plugins.ts` | 同源无 sandbox iframe |
| 管理入口 | `Controllers/PluginController.cs` | 安装 / 启用 / 停用 / 升级 / 卸载 |

设计定案依据：`2026-11-13-web-plugin-management-design.md` §5 硬约束、§6 D1–D7、§7 M1–M17、§8 功能规格、§14 验收。

### 3.2 plugin.json 与 webPage

插件清单关键字段用表格表达（不写代码块）：

| 字段 | 取值 / 约束 | 说明 |
|------|-------------|------|
| `id` | 全局唯一，符合框架 id 正则（骨架取 `com.tjces.yunxiao`） | 决定镜像目录 `plugin-web/{id}/` |
| `name` / `version` | 必填，版本语义化 | 升级时触发新 `stamp` |
| `entryAssembly` | **必填**，入口类实现 `IPlugin`（骨架为 `YunXiaoPlugin : PluginBase`） | 不支持纯 HTML 插件 |
| `targetApp` | 取 `LubanAgentWebHost`（两端 `appsettings` 未配置目标应用校验项） | 不触发目标应用校验 |
| `uiContributions[].id` / `label` / `icon` | 侧栏入口元信息 | `panelType` 在 Web 侧忽略 |
| `uiContributions[].webPage` | 必须位于 `web/` 下、扩展名 `.html` / `.htm`、防目录穿越 | 框架 `PluginManifest` 不识别，WebHost 回读原始 json 取得 |

### 3.3 web/ 目录与镜像路径（stamp）

| 项 | 约定 |
|----|------|
| 源 | 插件包 `web/` 子目录，入口 `index.html` |
| 镜像目标 | 输出目录 `AppContext.BaseDirectory/wwwroot/plugin-web/{id}/{stamp}/web/...`（源码树 `LubanAgentWebHost/wwwroot` 不写插件内容） |
| `stamp` 必要性 | 静态文件字节按**物理路径**缓存（`HttpFileResult` 按物理路径缓存字节、`HttpUtility.MapPath` 按 URL 永久缓存映射），同路径换内容仍返回旧字节，故镜像路径必须带随安装变化的戳；删除/重建目录也不清缓存 |
| 缓存依据 | `2026-11-13-web-plugin-management-design.md` §5 硬约束 4、5 |
| 资源约束 | 仅白名单扩展名可用；字体必须 base64 内嵌进 CSS，运行期数据写进 `.js`（见 §3.4）；单包条目数上限 `MaxEntries = 500`（`Services/WebPluginService.cs`），超出即拒绝安装 |
| 升级语义 | 升级生成新 `stamp` 目录，旧目录不再参与路由；`RequiresRestart = true` 时须**重启宿主**才生效（`Services/WebPluginService.cs` 的 `RequiresRestart` 分支），界面与文档不得宣称「热更新即生效」 |
| 卸载与清理 | 删除走 `TryDeleteDirectory` 尽力而为：失败时只留下**半删目录**、操作本身不阻断；重装同一 `id` 前须确认旧目录已清 |

### 3.4 硬约束清单

| # | 硬约束 | 来源（文件 + 符号/章节） | 对本插件的落地要求 |
|---|--------|--------------------------|--------------------|
| H1 | `entryAssembly` 必填且入口类须实现 `IPlugin`，不支持纯 HTML 插件 | `2026-11-13-web-plugin-management-design.md` §5 硬约束；`ManifestValidator.cs`、`Contracts/IPlugin.cs` | 插件须带一个最小 .NET 入口程序集（骨架 `YunXiaoPlugin` 已满足），页面只是附加能力 |
| H2 | 插件程序集只能依赖 `LuBan.AIAgent` 契约；引用 `LubanAgentCore` 运行期加载失败（只体现为 `lastError`） | §5 硬约束 8；`Configuration/PluginAssemblySharing.cs` | 插件不直接引用云效 SDK，全部经 MCP / 宿主接口（`LubanAgent.Plugin.YunXiao.csproj` 打包约束已写明） |
| H3 | `webPage` 必须在 `web/` 下、`.html` / `.htm`、防目录穿越 | §5 硬约束 2、6；`Hosting/PluginPageManifest.cs` | 入口固定 `web/index.html`，不引用 `web/` 之外路径 |
| H4 | MIME 仅白名单：`.html` `.htm` `.css` `.js` `.png` `.jpg` `.jpeg` `.gif` `.svg` `.ico` `.txt`；`.mjs` `.json` `.map` `.wasm` `.woff` `.woff2` `.ttf` `.webp` 返回 unknown | §5 硬约束 7；`SAEA.Http/Base/HttpMIME.cs` | 字体 base64 内嵌 CSS；配置/字典数据写进 `.js`；不引入 `.json` / `.mjs` / `.map` / `.wasm` / 独立字体文件 |
| H5 | `web/` 子目录名不得与宿主 controller 同名：`plugin` `session` `agent` `attachment` `mcp` `model` `llmwiki` `a2a` `agentsmd` `system` `provider` `skill` `rule` `workspace`（代码现存 14 项） | §5 硬约束 6；`SAEA.MVC/HttpContext.cs` 静态兜底按倒数第二段判控制器；`Hosting/WebPluginPaths.cs` 的 `HostControllerNames` | 插件目录命名避开这 14 个保留名，例如用 `assets/` `views/` `vendor/`。**已知缺口**：`HostControllerNames` 未含 `file`，而 `Controllers/FileController.cs` 真实存在，安装期同名拦截对 `file` 不生效；落地前须把 `file` 补入该列表（本次仅登记，不改宿主，见 R7） |
| H6 | 静态字节按物理路径缓存，镜像路径带 `stamp` | §5 硬约束 5；`Hosting/WebPluginPaths.cs` | 升级生成新 `stamp`，不复用旧物理路径 |
| H7 | POST 请求体统一 `{"payload":"<JSON字符串>"}`；响应统一 `ApiEnvelope{success, code, message, data}` | `Services/WebPluginService.cs` 等宿主接口的既有约定 | 插件所有 POST 按此封装，前端统一解包与错误提示 |
| H8 | hash 路由无 SPA 回退 | §5 硬约束；`web/src/lib/router.ts` | 屏与层级用 iframe 内 hash 片段，刷新后由入口 `index.html` 自行还原视图 |
| H9 | `/system` 特权动作只认固定 Bearer、插件页面调不到；插件宿主身份固定默认用户 | §5 硬约束 9；`Services/WebSystemService.cs`、`PluginHostContext.cs` 的 `UserId` | 凭据写入必须另开非特权 API（并补可写凭据存储，见 H10 与表 C 行 C28）；不假设当前登录用户身份 |
| H10 | **「非特权」不等于「无权限」**：`Hosting/WebApi.cs` 的 `HasValidCredential` 同时接受固定 Bearer 与浏览器会话 Cookie，同源 iframe 里的插件 JS 因此可调**全部非特权控制器**，包括 `McpController.Save`（注册 stdio 命令）、`ProviderController.Save`（改模型与密钥）、`WorkspaceController.Delete` | `Hosting/WebApi.cs`、`Controllers/McpController.cs`、`Controllers/ProviderController.cs`、`Controllers/WorkspaceController.cs`；`appsettings.json` 的 `Plugins:Security`；插件权限契约 `Plugins/Contracts/PluginPermissions.cs` 的**按插件 id 作用域只在 Agent 侧生效**（`LubanAgentCore/Plugins/AgentPluginHost.cs` 的 MCP 注册），WebHost 控制器不按其判定 | 插件前端**只调白名单端点**：插件页面清单、Agent / Session、Attachment / File 只读、Workspace 查询；**不得调用** MCP 保存 / 删除、Provider 保存 / 删除、Workspace 删除 / 改名、System；宿主侧尚未按插件 id 限定可调控制器，落地前须补 `PluginPermissions` 接线或等价约束（见 R14） |

### 3.5 前后端契约

| 契约项 | 约定（中文说明） | 落地注意 |
|--------|------------------|----------|
| 请求体 | 所有 POST 统一封装为「payload 字段 + JSON 字符串」的单层对象 | 插件 `fetch` 时把业务参数序列化为字符串放进 payload，不直接发裸对象 |
| 响应体 | 统一信封：成功标志 `success`、业务码 `code`、提示 `message`、数据 `data` | 前端统一解包；`success=false` 时按 `code` 映射文案，不把 `message` 直接回显 PAT |
| 鉴权 | 同源 Cookie `luban_auth` 自动携带 | 不手动加 Bearer；不依赖 `/system` 特权通道 |
| 流式 | Agent 执行进度走 SSE（`Services/WebAgentService.cs`） | 任务会话的回显走 SSE，逐条追加到会话面板；刷新后用 `Messages` 恢复 |
| 文档基准 | iframe 的 `src` 为 `/plugin-web/{id}/{stamp}/web/index.html`，宿主**不注入 `<base>`**（`web/src/pages/plugins/PluginPage.tsx`） | 插件内的相对路径按镜像目录解析；调宿主 REST **必须写绝对路径**（如 `/agent/chat`、`/session/create`），不能用 `../` 或相对段落，否则会打到镜像目录下 |
| 加载与失败 | 宿主侧已有「加载插件页面…」与错误文案 + 重新加载（`PluginPage.tsx` 的 loading / error 分支） | 插件内只处理数据空态与禁用占位，不重复绘制宿主级加载页或整页错误页 |
| 分页 / 列表 | 列表类接口按宿主既有分页口径 | 看板任务列表一次性取当前迭代全量，前端按状态分栏与排序 |
| 错误码 | 凭据失效、限流、权限不足、字段校验失败分类映射 | 见 §4.7；PAT 相关失败统一「个人令牌无效」类文案，不回显原文 |

### 3.6 接口清单（含凭据写入与校验缺口）

| 接口用途 | 宿主现成入口（文件 + 符号） | 状态 | 支撑 FR |
|----------|------------------------------|------|---------|
| 上传本地文件（JSON + Base64） | `Controllers/AttachmentController.cs` 的 `Upload` | 已具备 | FR-5 |
| 读取文件内容 | `Controllers/FileController.cs` 的 `Read`、`Services/WebFileService.cs` | 已具备 | FR-5 |
| 工作区元数据（按根目录查 / 建 / 切） | `Controllers/WorkspaceController.cs`（`ByRootPath` / `Create` / `List` / `Switch`） | 已具备 | FR-2 / FR-5 |
| 工作区文件 / 目录列举 | **无**：`FileController` 只有按相对路径读单文件的 `Read`，`WorkspaceController` 只管工作区元数据，**没有任何列举目录或文件清单的入口** | **需新增**（§4.12 表 C 行 C31） | FR-5「宿主工作区选文件」 |
| git 执行（clone / 改名后 commit / push） | **无 REST 入口**：git 是 Agent 的工具组（`luban-framework/LuBan.AIAgent/Tools/Git/GitToolPlugin`，含 clone / commit / push / status / diff），插件 JS 调不动 | **经工作区会话由 Agent 执行（一期定案）**；备选 C30 后续可选（§4.12 表 C，O10） | FR-2 |
| Agent 会话与执行（Send / Chat / Active / Attach / Confirm / Cancel） | `Controllers/AgentController.cs`、`Services/WebAgentService.cs`（含 SSE） | 已具备 | FR-7 |
| 工作区与会话（按工作目录查/建工作区、按工作区建会话、读取历史消息） | `Controllers/WorkspaceController.cs`（`ByRootPath` / `Create`）、`Controllers/SessionController.cs`（`Create` / `Messages`）、`Services/WebWorkspaceService.cs`、`Services/WebSessionService.cs` | 已具备 | FR-7 |
| 插件安装 / 启用 / 页面清单 / 镜像 | `Controllers/PluginController.cs`、`Services/WebPluginService.cs`、`Hosting/PluginPageManifest.cs` | 已具备 | 全局 |
| 云效 13 个 MCP 工具调用 | `Mcp/YunxiaoToolDefinitions.cs`、`Mcp/YunxiaoMCPClient.cs`（Agent 侧 `mcp_yunxiao_*`） | 已具备（部分） | FR-2 / FR-3 |
| 读流水线详情 YAML | `FlowClient.cs` 的 `GetPipelineAsync`；官方 `GetPipeline` 返回 `pipelineConfig.flow`（§4.13.4 第 1 条） | **SDK-only，待包装为 MCP**（§4.12 表 B 行 B1）；平台侧已有官方 API | FR-3 |
| 更新代码仓库 | `CodeupClient.cs` 的 `UpdateRepositoryAsync` | **SDK-only，待包装为 MCP**（§4.12 表 B 行 B2） | FR-2 |
| 云效凭据校验 | **无** | **需新增**（§4.12 表 C 行 C29；探活逻辑可复用 `ConnectorManagementService.TestAsync` 的底层调用，但该服务按**已配置**连接器测试，入口须以临时选项只读探活、不落配置；宿主已在 `AgentHostBuilder` 注册 `IConnectorManagementService`） | FR-1 |
| 云效凭据写入 | **无**，且 WebHost 未注册可写 `ICredentialStore` | **需新增**（§4.12 表 C 行 C22 写入端点 + C28 可写存储；平台无替代） | FR-1 |
| 云效官方托管 MCP（项目 / 迭代 / 工作项 / 状态 / 测试 / 流水线 / 代码） | 框架通用外部 MCP：`Configuration/McpServerConfig.cs` 的 `Transport=http` / `Url` / `Headers`；`MCP/CredentialValueResolver.cs` 的内联 `$credential:` | **本仓未接线，平台侧已具备**（`https://openapi-rdc.aliyuncs.com/ai/mcp`，§4.13.2） | FR-2 / FR-3 / FR-4 / FR-6 / FR-7 |
| DevOps 项目 / 迭代 / 工作项 / 测试 / 看板 读写入口 | 官方 OpenAPI，逐条路径见 §4.13.3 | **本仓未接线**（§4.12 表 C 行 C1–C20、C23–C27），非「需平台新增」 | FR-4 / FR-5 / FR-6 / FR-7 |
| 企微云盘读取 | 官方 `wedrive/file_list`、`file_info`、`file_download`（§4.13.3 C21 行） | **需新增企微连接器 + 独立应用鉴权（P2 / 开放）**（§4.12 表 C 行 C21） | FR-5 |

**C22 / C28 / C29 最小契约（设计建议，宿主侧尚未存在）**

| 项 | 约定 |
|----|------|
| 职责拆分 | C28 解决「能不能写」（依赖注入换掉只读实现）、C22 解决「从哪里写」（非特权端点）、C29 解决「怎么验」（只读探活）——三者缺一则 FR-1 不成立 |
| 方法 / 路径 | 建议 `POST /connector/credential`（写入）与 `POST /connector/test`（校验）；面向前端、走非特权通道，**不得**落在 `/system`。路径为建议，落地时以宿主评审定稿为准 |
| 载荷 | 按 §3.5 单层 `payload` 包装：`{ organizationAddress, organizationId, pat }`；校验只读不落盘；写入响应只回 `{ savedAt }`，不回显 `pat` |
| 幂等 | 同一 `organizationId` 重复保存即覆盖同一凭据键，不新增记录；失败不写任何键 |
| 生效链路 | 写入 → 凭据存储落全局 `config.json` 的 `Integrations` → 凭据提供者按调用读取（`ConfigurationYunxiaoCredentialProvider` 明确不缓存，无需清缓存） → 回读确认后才置「已保存」；不得要求用户重启宿主才生效 |
| 复用载体 | 校验复用 `ConnectorManagementService.TestAsync` 的探活调用，但须以临时选项（orgId / PAT 直传、不读不写配置）执行；写入复用 `IConnectorConfigWriter` 与凭据键别名表，不自造第二套存储 |

### 3.7 代码挂载点索引表（开发实现附录，可跳过）

> 本表仅索引实现可能触及的真实位置；除插件自身工程外**均不改，仅索引**。

| 挂载点 | 真实文件 | 改动说明 |
|--------|----------|----------|
| 插件清单与页面贡献 | `LubanAgent.Plugin.YunXiao/plugin.json`、`web/index.html`、`YunXiaoPlugin.cs` | **插件自身工程**：骨架已就位，六项业务界面按本 PRD 在 `web/` 下实现 |
| 页面清单回读 | `LubanAgentWebHost/Hosting/PluginPageManifest.cs` | 不改，仅索引：已能回读 `webPage`，无需改动 |
| 资源镜像与 stamp | `LubanAgentWebHost/Hosting/WebPluginPaths.cs`、`Services/WebPluginService.cs` | 不改，仅索引：升级生成新戳的既有机制直接复用 |
| 插件路由 | `LubanAgentWebHost/Hosting/WebPluginRoutes.cs` | 不改，仅索引 |
| iframe 嵌入 | `LubanAgentWebHost/web/src/pages/plugins/PluginPage.tsx`、`web/src/stores/plugins.ts` | 不改，仅索引 |
| 凭据写入 API | `LubanAgentWebHost/Controllers/`（拟新增非特权入口） | 不改，仅索引：FR-1 前置缺口，见 §4.12 表 C 行 C22、C29 与 §3.6 契约 |
| 可写凭据存储注册 | `LubanAgentWebHost/Program.cs`、`LubanAgentCore/Hosting/AgentHostBuilder.cs`（框架 `TryAddSingleton<ICredentialStore>` 落只读实现）；可写参考 `LubanAgentCore/Security/CredentialStore.cs`、`LubanAgentCodex/App.axaml.cs` | 不改，仅索引：WebHost 现为只读实现，保存会抛 `NotSupportedException`，见 §4.12 表 C 行 C28 |
| 云效 MCP 工具面 | `LubanAgentCore/Connectors/AliyunDevOps/Mcp/YunxiaoToolDefinitions.cs`、`Mcp/YunxiaoMCPClient.cs` | 不改，仅索引：M2 阶段补齐工具，见 §4.12 表 B / C |
| 云效 SDK 客户端 | `LubanAgentCore/Connectors/AliyunDevOps/FlowClient.cs`、`CodeupClient.cs` | 不改，仅索引 |
| 文件 / 工作区通路 | `LubanAgentWebHost/Controllers/AttachmentController.cs`、`FileController.cs`、`WorkspaceController.cs`、`Services/WebFileService.cs` | 不改，仅索引 |
| Agent 能力 | `LubanAgentWebHost/Controllers/AgentController.cs`、`Services/WebAgentService.cs` | 不改，仅索引 |
| 头脑风暴技能 | `luban-framework/LuBan.AIAgent/Skills/BuiltIn/BrainstormingSkill.cs` | 不改，仅索引：任务会话指定该技能，其「硬门禁」在会话内生效 |
| 任务上下文装配与工具白名单参考 | `LubanAgent.Plugin.NewProject/Services/HostAssistantBridge.cs`、`StepPrompts.cs`、`AssistantToolPolicy.cs` | 不改，仅索引：作为「任务上下文 → 首轮输入」与工具白名单的参考实现 |
| 视觉令牌 | `LubanAgentWebHost/web/src/styles.css` 的 `:root` | 不改，仅索引 |

### 3.8 连接器设计

| 组成 | 真实文件 / 符号 | 本设计口径 |
|------|-----------------|------------|
| 连接器注册 | `AliyunDevOpsConnectorServiceCollectionExtensions.cs` 的 `AddAliyunDevOpsConnector`；配置节 `LuBanAgent:Connectors:Yunxiao` | 沿用例，新增工具不改注册方式 |
| 鉴权与凭据 | `IYunxiaoCredentialProvider.cs`、`PatCredential.cs`、`AliyunCredentialKeyAliases.cs`（键 `connector:yunxiao:token` / `connector:yunxiao:organizationid`） | PAT + 组织 ID 由服务端保存；界面只收不回显 |
| 接入点 | `YunxiaoApiOptions.cs` 的 `Endpoint`（`openapi-rdc.aliyuncs.com`），鉴权头 `x-yunxiao-token` | **固定，不覆盖**；「组织访问地址」仅用于解析组织 ID（§4.3 FR-1） |
| 客户端 | `CodeupClient.cs`、`FlowClient.cs`、`YunxiaoConnector.cs` | 新增工具复用既有 HTTP 层与响应解析（`YunxiaoResponseParser.cs` / `YunxiaoModels.cs`） |
| MCP 工具面 | `Mcp/YunxiaoToolDefinitions.cs`、`Mcp/YunxiaoMCPClient.cs`（前缀 `mcp_yunxiao_`） | 表 B 包装 2 个；表 C 共 **31 行**：云效工具 24 个（C1、C2、C4–C20、C23–C27，含可选 C27）、宿主侧 5 项（C22 写入端点 / C28 可写凭据存储注册 / C29 校验入口 / C30 git 执行端点（后续可选）/ C31 工作区文件列举）、平台开放项 2 项（C3 创建项目后续 / C21 企微 P2）；命名与入参见 §4.11、§4.12 |
| 错误映射 | `YunxiaoErrorMapper.cs` | 401/403/429/500 分类映射到 §4.7 文案 |

---

## 4. 功能需求

### 4.1 概述

**功能地图**：7 项功能按双节奏归位——「一次性配置」段含 F4 云效连接（FR-1）、F5 Codeup（FR-2）、F6 Flow（FR-3）、F3 的 DevOps 项目浏览与绑定（FR-4，只做「选择既有」）；「周期性作业」段含 F8 上部取得 PRD（FR-5，默认云效同步）、F8 中下拆分（FR-6）、F1 + F2 + F3 看板与 Agent（FR-7）；F0 规范页承载全局视觉与约束。**页面不是按功能平铺，而是按用户流转串起来**：首次打开走 F4 门禁，首页是 F3 改造的项目列表，点击项目进 F1 看板，首次点击则先走 F5 + F6 合并的项目初始化；完整层级与 F 编号 ↔ 屏序映射见 §2.9。

**双节奏表**：见 §2.2（此处不重复，互指）。

**功能 ↔ Frame ↔ 章节映射表（两份产物互相印证）**：

| 功能编号 | 对应 Frame | PRD 章节落点 |
|----------|------------|--------------|
| 功能 1 · 设置云效 MCP | F4 云效连接 | FR-1 / §3.6 / §3.8 / §4.9 |
| 功能 2 · 设置云效 Codeup 代码库 | F5 Codeup 代码库 | FR-2 |
| 功能 3 · 设置云效 Flow 流水线 | F6 Flow 流水线 | FR-3 |
| 功能 4 · 设置云效 DevOps 项目 | F3 项目列表（插件首页，右栏两面板） | FR-4 / §2.9 |
| 功能 5 · 取得 PRD（默认云效同步，本地文档补充） | F8 PRD 工作台上部 | FR-5 |
| 功能 6 · 拆分 PRD 到迭代/需求/任务/测试用例/测试计划（写入当前项目） | F8 PRD 工作台中下 + F9 迭代测试（用例与计划）+ F3 | FR-6 / §4.6.2 |
| 功能 7 · DevOps 看板与任务详情驱动的工作区会话 | F1 项目工作台 + F2 任务详情与工作区 + F3 浏览 | FR-7 / §2.7 / §4.6.1 / §4.10 |
| 全局 · 视觉与约束 | F0 规范页 | §2.6 / §3.4 / §4.6 |

**核对规则**（两份产物逐帧互校）：

| 规则 | 内容 |
|------|------|
| 规则 1 | 每个 Frame 元信息必须含「功能编号 + PRD 章节」二元组，取值来自上表 |
| 规则 2 | §4.6 Frame 清单表行数 = 9（F0–F6、F8、F9），与画布 Frame 数一致 |
| 规则 3 | §4.12 每行缺口（表 B、表 C 全部行）必须在某个 FR 的「依赖能力」或 §6 风险中有落点 |
| 规则 4 | §4.8 每条验收 V 必须回溯到一个 FR 编号（V 的前缀即所属 FR） |
| 规则 5 | 5 处重定义必须在对应 FR 的「设计取舍与差异」与 §6 风险同时出现 |

### 4.2 用户故事

| 角色 | 故事 | 覆盖 FR |
|------|------|---------|
| 项目初始化者 | 我希望在一个向导里把云效连接、代码库、流水线、DevOps 项目一次配好并复核，避免在多套控制台间切换和靠记忆填组织 ID / 分支 | FR-1~FR-4 |
| 项目初始化者 | 我希望初始化前看到「只读预检」清单（命中哪个母版库、新代号派生出哪三个名字、落在哪个目录、用哪个分支），确认后才真正创建 | FR-2 / FR-3 / FR-4 |
| 迭代负责人 | 我希望 PRD 默认就从云效同步下来、不用先下载成文件，产品另外给的文档也能一起并入，一键拆成迭代、需求、任务、测试用例、测试计划，并能核对拆分明细与落库结果 | FR-5 / FR-6 |
| 迭代负责人 | 我希望看板按云效实际状态分栏、按预计完成时间排序，不因写死四列而错位 | FR-7 |
| 开发者 | 我希望在任务详情里一次看全描述、验收标准与评论，不用回云效翻评论找上下文 | FR-7 |
| 开发者 | 我希望点一下就把「标题 + 详情 + 评论」带进工作区开始干活，会话能在宿主里接着聊 | FR-7 |
| 开发者 | 我希望方案没被我点头之前，Agent 不会偷偷写代码或建骨架 | FR-7 |

### 4.3 功能需求明细（FR-1 ~ FR-7）

> 每项固定模板：目标 / 前置条件 / 界面 / 字段表 / 交互流程 / 状态机 / 依赖能力 / 异常与回滚 / 对应验收 / 设计取舍与差异。「依赖能力」中的行号指 §4.12 三张表的内部行编号（A/B/C），不是文件行号。

---

#### FR-1 · 设置云效连接

| 项 | 内容 |
|----|------|
| **目标** | 配置组织访问地址、组织 ID、PAT，校验连通并安全保存，作为后续所有云效能力的前置 |
| **前置条件** | 已部署 Web 插件并启用；宿主侧已补齐三项（§4.12 表 C 行 C22 写入端点、C28 可写 `ICredentialStore` 注册、C29 校验入口）——**现有 WebHost 一项都不具备，直接调保存会抛 `NotSupportedException`**；用户持有云效 PAT 与组织 ID |
| **界面** | F4 云效连接；区域：连接表单区 + 校验结果区 + 保存状态区 |

字段表：

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| 组织访问地址 | 字符串 | 用户输入 | 是 | **仅用于解析 organizationId**，不覆盖 OpenAPI Endpoint；私有化 Endpoint 列 §6 R4 开放问题 |
| 组织 ID | 字符串 | 地址解析或用户手填 | 是 | 对应凭据键 `connector:yunxiao:organizationid` |
| PAT | 字符串（密码态） | 用户输入 | 是 | 对应凭据键 `connector:yunxiao:token`；只收不回显、不写 DOM |
| 校验状态 | 枚举 | 系统回读 | 否 | 未校验 / 校验中 / 通过 / 失败（含分类原因） |
| 保存时间 | 日期时间 | 系统 | 否 | 上次成功保存时间 |

交互流程（编号步骤）：

1. 用户在 F4 填入组织访问地址；界面从地址中解析出组织 ID 并回填（可手动修正）。
2. 用户填入 PAT（密码态输入，不显示明文）。
3. 点「校验」→ 调用非特权凭据校验入口（C29；探活逻辑复用 `ConnectorManagementService.TestAsync`，但以临时选项只读执行、不落配置），用固定 Endpoint 与 `x-yunxiao-token` 头做一次只读探活。
4. 校验通过 → 界面显示组织名称/可见域摘要；失败 → 按 §4.7 分类提示，不回显 PAT 原文。
5. 点「保存」→ 经非特权写入端点（C22）落库到既有凭据键；**落库实现依赖 C28 的可写 `ICredentialStore`**。凭据提供者 `ConfigurationYunxiaoCredentialProvider` 明确「不缓存」，每次调用都重新读取（`AliyunDevOpsConnectorServiceCollectionExtensions.cs`），因此写入后无需清缓存，但必须回读确认，界面仅显示「已保存」与保存时间。
6. 保存成功后，配置向导其余步骤（FR-2~FR-4）解锁。

状态机：未配置 → 校验中 → 已校验 → 已保存（对应附录 A 的「连接」子态）。

依赖能力：§4.12 表 C 行 C22（非特权凭据写入端点）、C28（WebHost 可写凭据存储注册）、C29（非特权凭据校验入口），三者为 FR-1 前置，缺一不可；表 A 通路（`AliyunCredentialKeyAliases.cs`、`YunxiaoApiOptions.cs`、`YunxiaoSettingsService.cs` 现状）；现状依据见 §6 R4、R6、R14 与 §4.9。

异常与回滚：见 §4.7「凭据与网络类」；保存失败不写任何凭据，界面保持未配置态。

对应验收：V1-1、V1-2、V1-3、V1-4。

**设计取舍与差异**：原始诉求是「填写云效地址 + accessToken」。因 Endpoint 固定为 `openapi-rdc.aliyuncs.com`（`YunxiaoApiOptions.cs` 的 `Endpoint`）、且 WebHost 侧没有任何云效设置界面或凭据写入 API（桌面端 `YunxiaoSettingsService.cs` 也只有 orgId + PAT、无地址输入框），重定义为「配置组织访问地址（仅用于解析 organizationId，不覆盖 OpenAPI Endpoint）+ 组织 ID + PAT，校验并保存（依赖新增非特权写入端点）」；私有化 Endpoint 是否可配列 §6 R4 开放问题。**另需说明**：WebHost 现挂的是框架只读 `ICredentialStore`（`SetAsync` 抛 `NotSupportedException`），可写实现只在桌面端，故「保存」不是加一个接口就可用——须同时补 C28 与 C29（§4.12）。

---

#### FR-2 · 复制母版代码库（整库复制 + 代号派生命名）

| 项 | 内容 |
|----|------|
| **目标** | **从母版代码库复制出新项目的库**：模板 = 已建好的母版库（如 `TJCES-Mixed`，根目录并列 `TJCES-Admin` / `TJCES-Mobile` / `TJCES-Service`），复制后按**新项目代号**把库名、三个子目录名、三条流水线名一并改写（`TJCES-*` → `RGMS-*`）；统一设置可见性、代码组、宿主工作目录与分支（母版默认 `master`），克隆后**在本地批量重命名子目录并推送**，改完的名字交给 FR-3 作流水线与构建目录 |
| **前置条件** | FR-1 已保存；组织内已有母版代码库（至少 1 个，且结构固定、子目录可直接改名）；工作区会话通路可用（git 执行通路，见下） |
| **界面** | F5 项目初始化·代码库区（与 F6 同屏，左列）；区域：**母版代码库下拉**（可多选，一项复制出一个库）+ **新项目代号输入** + **命名派生预览**（库名 / 子目录 / 流水线名三段替换关系）+ 共用设置区（工作区目录 / 分支）+ 复制与改名结果 |

字段表：

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| 母版库总数 | 数字 | `codeup_search_repos` / `codeup_list_template_repos` 按命名规则筛出 `*-Mixed` 母版库 | 是 | 决定选择模式：**等于 1 → 下拉锁定为单项、不可展开**；大于 1 → 下拉内可多选，一项复制出一个库。**两个列表接口都分页（`perPage` 上限 100、`page` 上限 150，响应头带 `x-total`），须翻页或按总数取全量**，不能只看首页就判「只有一个母版库」 |
| 母版代码库 | 引用 / 列表 | `codeup_search_repos`（按 `-Mixed` 命名或组织约定的母版清单） | 是 | **模板 = 已建好的母版库**，不是框架模板；下拉展示库名与其子项目数（如 `TJCES-Mixed`：企服版 · 3 子项目）。单母版锁定，多母版可勾选，**每勾一个复制出一个新库** |
| 母版代号 | 字符串 | 系统解析（母版库名前缀） | 是 | 从母版库名解析出代号（`TJCES-Mixed` → `TJCES`），用于定位所有需要替换的名字；解析不到时要求用户手填 |
| 新项目代号 | 字符串 | 用户输入 | 是 | **本屏唯一的命名输入**，如 `RGMS`。它同时决定：新库名 `{代号}-Mixed`、三个子目录 `{代号}-Admin` / `{代号}-Mobile` / `{代号}-Service`、三条流水线名 `{分组前缀}_DEV_Deploy_{代号}-{端}`；只填代号，不逐项填三个名字，避免用户自己保证不一致 |
| 命名派生预览 | 只读列表 | 系统生成 | 是 | 三段「母版名 → 新名」对照（库 / 子目录 / 流水线），执行前必须可见；任何一项也可单独改写，改完后续步骤按新值走 |
| 子目录改名方式 | 枚举 | 系统固定 | 是 | **本地重命名**：官方无重命名目录接口，克隆后在宿主工作区把 `TJCES-*` 目录改名并提交（§4.13.4 第 11 条）；界面须明示这一步，不能让用户以为改个名字就自动生效 |
| 复制方式 | 枚举 | 系统选择 | 是 | **主路径：本地克隆母版 → 改目录名 → 推送新库**（能真正改名）；**降级：母版设为自定义模板库 → `CreateRepository` 带 `templateProject` + `syncAllBranches`**（服务端复制、目录名不变，需再走一次本地改名）。**降级路径有前置门槛**：母版库须由库管理员设为自定义模板库且体积 < 100 MB，否则该路径不可用、只能走克隆路径（§4.13.4 第 8、11 条）。官方无 Fork 接口 |
| 可见性 | 枚举 | 用户选择 | 是 | 组织内公开 / 私有，**多库共用一次选择** |
| 归属代码组 | 引用 | `codeup_list_groups` / `codeup_create_group` | 是 | 多库共用；先查后建、同名复用不重复建 |
| 本机工作目录 | 字符串 | 用户选择（宿主工作区） | 是 | 语义为**宿主工作区目录**，非浏览器本机 FS（§6 R9）；**一个项目共用一个目录**，多库落在其下 |
| 目标分支 | 字符串 | 默认 `master` | 是 | 母版库默认分支即 `master`（真实库如此），副本同起；开发分支由 Agent 在工作区切出，初始化阶段不切 |
| 复制结果 | 列表 | 系统回读 | 否 | 逐库状态：未开始 / 复制中 / 改名中 / 已推送 / 失败；**失败可只重跑该库** |

交互流程（编号步骤）：

1. 进入项目初始化（新建模式），只读列出母版代码库与代码组，不创建任何资源。
2. **选母版**：母版库为 1 个时下拉锁定（不展开、不出现「已选 n 个」）；多个时可勾选，一项复制出一个库。
3. **填新项目代号**（如 `RGMS`）→ 界面立刻给出**命名派生预览**：`TJCES-Mixed → RGMS-Mixed`、`TJCES-* → RGMS-*`、`…_Deploy_TJCES-* → …_Deploy_RGMS-*`；三段都可单独改写。
4. 填共用项：归属代码组、宿主工作区目录；分支默认 `master`（母版默认分支）。
5. 点「预检」→ 展示只读清单：**新库名是否已在组织内存在**（存在则阻断并提示换代号）、将改名的 N 个子目录、将复制的三条流水线名、目标分支；**不创建任何云效资源**。
6. 用户确认后逐库执行（**git 动作经工作区会话执行，接口层没有直接的 git 入口**）：查/建代码组 → **克隆母版到工作区 → 在本地把 `{母版代号}-*` 目录批量改名为 `{新代号}-*` → commit → 建新库（`codeup_create_repo`）→ 推送 master**（降级路径为 `CreateRepository` 带 `templateProject`，见字段表「复制方式」）。
7. 回读结论：用 `codeup_get_repo` + `codeup_list_branches` 确认新库与 `master` 存在、目录名已改；界面显示「已复制出新库」；**单库失败只重跑该库，不回滚已成功的库**。
7a. 派生出的子目录清单即为 FR-3 的**构建目录候选**（`RGMS-Admin` / `RGMS-Mobile` / `RGMS-Service`）；不依赖扫描——名字是这一屏算出来的，扫描只作兜底校验（实际目录与预期不符时提示）。
8. 需要改仓库属性时，经表 B 行 B2 的 `codeup_update_repository` 更新；不做自动删除。

**执行通路（可行性硬点，已定案）**：**git 类动作（clone / 改名后 commit / push / status / diff）没有任何 REST 入口**——git 是 Agent 的工具组（`luban-framework/LuBan.AIAgent/Tools/Git/GitToolPlugin`，框架层），WebHost 侧没有等价控制器，**插件 JS 调不动 git**。一期**只走「工作区会话」这一条**（O10）：初始化动作拼成一条工作区会话指令（`SessionController.Create` + `/agent/chat`），Agent 调 git 工具组完成克隆 / 改名 / 提交 / 推送；工具调用走 `AgentController.Confirm` 的人工确认通道，界面进度条对齐 SSE 事件流，逐库回执从会话事件里解析。「宿主 git 执行端点（C30）」为后续可选、一期不做。落地要求：改名后 commit 的说明文案固定可核、失败只重跑该库、执行前把「将执行的 git 命令序列」在界面上明示（不静默跑破坏性操作）。

状态机：未配置 → 预检 → 待确认 → 执行中（逐库） → 全部就绪 / 部分失败（对应附录 A 的「Codeup」子态）。

依赖能力：§4.12 表 A 行 A1（`codeup_search_repos`，**主路径用它按命名规则检索母版库**）、A2（`codeup_list_template_repos`，**仅当母版库已被设为自定义模板库时可用**，且模板库须 < 100 MB，见 §4.13.4 第 8 条；超限时只能走克隆路径）、A3（`codeup_get_repo`）、A4（`codeup_create_repo`）、A5（`codeup_list_branches`）、A6（`codeup_create_group`）、A7（`codeup_list_groups`）；表 B 行 B2（`codeup_update_repository`）；宿主通路 `WorkspaceController.cs`（仅工作区元数据）；**git 执行通路：经工作区会话 + `AgentController`（Send/Chat/Confirm）驱动框架 git 工具组**（`Tools/Git/GitToolPlugin`）；C30 同步端点一期不做。

异常与回滚：见 §4.7「目录/文件类」与「云效资源类」；克隆失败清理本次新建的本地目录，远端仓库不自动删、只列清单；重跑按同名幂等复用；**会话通路未接线（无法发起工作区会话）时，「开始复制」按钮禁用并明示原因，不得进入执行中态**；**多库部分失败时不回滚已成功的库**，只对失败库重跑。

对应验收：V2-1、V2-2、V2-3、V2-4、V2-5、V2-6、V2-7。

**设计取舍与差异**：与原始诉求一致；补充四点落地口径——①「本机工作目录」指宿主工作区目录（同源 iframe 读不到浏览器本机 FS，§6 R9）；②初始化分支固定为母版默认的 `master`，不沿用旧「新建项目」插件 `newproject.settings.json` 的 `pushBranch = "develop"`（那条口径只适用于旧插件）；③**模板选择模式由母版总数决定**（1 个锁定、多个多选），不把单选 / 多选做成用户的额外开关；④模板对应的是「一套工程结构」而非「一个端」，库名与库数量都不按端划分，一个库可含多端项目，端与库的对应关系交给 FR-3 每条流水线各自选择；模板即**已建好的母版库**（`TJCES-Mixed`），动作是**整库复制 + 代号重命名**而不是「选框架模板建空库」，命名上只让用户填一个代号，其余三个名字由系统派生并预览。命名派生沿用组织既有 `{Code}-Mixed` 约定（`newproject.settings.json` 的 `defaultVisibility` / 库名格式可佐证）。

---

#### FR-3 · 设置云效 Flow 流水线（三端各一条，只绑 dev 环境）

| 项 | 内容 |
|----|------|
| **目标** | 把母版库的三条流水线**复制成新项目名下的三条**：流水线名由新代号派生（`CME_DEV_Deploy_{母版代号}-*` → `CME_DEV_Deploy_RGMS-*`，母版流水线真值见 `LubanAgent.Plugin.NewProject/docs/org-reference.md`），构建目录同步派生（`RGMS-Admin` / `RGMS-Mobile` / `RGMS-Service`）；**只绑 dev 环境**，后端只填监听端口并写入 dev 环境变量；某端不需要时取消勾选跳过 |
| **前置条件** | FR-2 已就绪（拿到新库 id / 名称 / 派生出的三个子目录）；FR-1 已保存；已包装表 B 行 B1 |
| **界面** | F6 项目初始化·流水线区（与 F5 同屏，右列）；区域：卡片标题处的**新库标签**（`RGMS-Mixed`）+ 三端流水线行（勾选 + **派生的构建目录** + **派生的流水线名**，两者均可改）+ 后端端口盒（只有监听端口一项）+ 差异预览条。卡片内一句说明「复制流水线」的真实机制是「读母版 YAML → 改名新建」 |

字段表：

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| 端 | 枚举 | 系统固定 | 是 | **移动端 / 管理后台 / 后端**，三行固定存在，不动态增减 |
| 该端启用 | 布尔 | 用户勾选 | 是 | 取消勾选 = 该端跳过，不读 YAML、不提交 |
| 部署环境 | 枚举 | 系统固定 `dev` | 是 | **固定 `dev`**。云效流水线对象没有内建的「开发 / 测试 / 生产」三环境属性，多环境差异由**流水线变量（如 `DEPLOY_ENV=dev`）/ 独立流水线 / AppStack 应用环境**实现（§4.13.4 第 9 条）；初始化只把各端流水线的部署环境固定为 dev，测试与生产沿用既有编排，插件不新建环境、不改测试与生产 |
| 母版流水线 | 引用 | `flow_list_pipelines` 按 `{分组前缀}_DEV_Deploy_{母版代号}-{端}` 反查 | 启用时必填 | 母版库配套的三条流水线，仅作 YAML 来源；**`type != PIPELINEASCODE` 不可选**。反查不到时允许手选，但须提示「该流水线不属于母版，构建目录可能对不上」 |
| 新流水线名称 | 字符串 | 系统派生（可改） | 是 | `{分组前缀}_DEV_Deploy_{新代号}-{端}`，如 `CME_DEV_Deploy_RGMS-Mobile`；分组前缀与 `_DEV_Deploy_` 段取自母版流水线名，只替换代号；这是母版复制后必须改的第二类名字 |
| 关联代码库 | 引用 | FR-2 库列表 | 是 | **项目级**：默认带出 FR-2 所建 / 同步的唯一库（多库时默认第一库），显示在卡片标题处；三条流水线默认共用，多库项目可在此切换 |
| 构建目录 | 字符串 | FR-2 派生结果（可改） | 是（单工程库可空） | 该端流水线的构建根目录，如 `RGMS-Admin` / `RGMS-Mobile` / `RGMS-Service`——**由代号派生，与 FR-2 实际改名的目录同名**；这是「一库多端」下唯一能区分三端的落点。主值与 FR-2 的改名结果校验一致，不一致时阻断 |
| 后端监听端口 | 数字 | 用户输入 | 后端启用时必填 | **后端唯一的可填参数**，写入 dev 环境变量并参与 YAML 替换；服务名称、镜像名、健康检查、副本数等按流水线现有规则取自模板，不出现输入项 |
| 参考 YAML | 文本 | `flow_get_pipeline` 读取 | 启用时必填 | 逐条读取，每条流水线各一份 |
| 替换占位符 | 键值表 | 用户确认 | 是 | 至少含：代码库 id / 名称 / 构建目录；后端另含端口变量 |
| diff 预览 | 文本 | 系统生成 | 否 | **逐条流水线分别预览**，可切换查看 |
| 提交方式 | 枚举 | 用户选择 | 是 | 新建（`flow_create_pipeline`）/ 覆盖（`flow_update_pipeline`） |

交互流程（编号步骤）：

1. 进入流水线区，标题处显示 FR-2 复制出的新库（如 `RGMS-Mixed`）；三行固定为移动端、管理后台、后端，每行默认勾选；环境固定 `dev`，界面上不做环境选择器。
2. 每行带出**派生好的构建目录与流水线名**（`RGMS-Mobile` / `CME_DEV_Deploy_RGMS-Mobile` …），并反查母版对应那条流水线；名字可直接改，母版流水线也可换（列表只列 YAML 化流水线，UI 编排的置灰并标注不可选）。
3. 后端行展开端口盒：**只有「监听端口」一个输入**（默认 8080）；预检校验端口冲突，冲突即阻断并指出冲突项；其余参数不在界面出现，按模板现有规则处理。
4. 逐条读 YAML（`flow_get_pipeline`，表 B 行 B1）→ 按该端「代码库 + 构建目录」做占位符替换（repo / workDir / branch / 端口变量）→ 生成各自的 diff，**只替换部署环境相关变量并固定为 dev**，测试与生产段的变量不动。
5. 差异预览条汇总「复制 1 个库 + 3 条流水线，替换 4 类名字并写入 1 项端口」（repo / dir / 流水线名 / branch + port），可逐条切换查看；**未确认差异不发任何提交请求**。
6. 用户确认后逐条提交：新建走 `flow_create_pipeline`、覆盖走 `flow_update_pipeline`（覆盖须卡内确认）。
7. 回读每条结果与「新流水线名已生效、构建目录指向 `{新代号}-{端}`、部署环境固定为 dev」结论，写入项目档案；某端取消勾选则该端跳过，不影响其他端。

状态机：未配置 → 已选参考 → 已读 YAML → 已预览 → 待提交 → 已创建 / 已覆盖（逐端独立；对应附录 A 的「Flow」子态）。

依赖能力：§4.12 表 A 行 A8（`flow_list_pipelines`）、A11（`flow_create_pipeline`）、A12（`flow_update_pipeline`）；表 B 行 B1（`flow_get_pipeline`，FR-3 必需）；现状依据 `FlowClient.cs` 的 `GetPipelineAsync`。**后端端口不新增接口**：端口以 dev 环境变量的形式并入 YAML 替换，随 `flow_create_pipeline` / `flow_update_pipeline` 一并提交。

异常与回滚：见 §4.7「云效资源类」；覆盖为破坏性操作，须卡内勾选确认；提交前 diff 未确认不发请求；**三端各自独立提交，某一端失败不回滚其他端**，仅重跑失败端；远端流水线不自动删。

对应验收：V3-1、V3-2、V3-3、V3-4、V3-5、V3-6、V3-7、V3-8。

**设计取舍与差异**：原始诉求是「复制现有流水线，修改为新代码库的流水线」。因云效 OpenAPI **无「复制流水线」接口**（历史 `flow_clone_pipeline` 已删，见 `PRD-插件体系与新建项目插件.md` 修订说明），实现上重定义为「以母版流水线为来源新建/覆盖」。机制链路：选母版流水线 → `flow_get_pipeline` 读 YAML → 占位符替换（代码库 id / 名称 / 构建目录）→ 预览 diff → `flow_create_pipeline` / `flow_update_pipeline`。**措辞**：界面上保留「复制流水线」这一用户语言（用户原话即「从母版复制出新的流水线并改名」），同时在卡片内一句点明它其实是「读母版 YAML → 改名新建」，说法贴合直觉、机制不骗人。其余口径：①**流水线按端固定三条**（移动端 / 管理后台 / 后端），三端默认共用同一个库、各自指向不同构建目录；②**只绑 dev**——不做环境选择器，不配置测试与生产；③**后端参数只有监听端口 1 项**，服务名称、镜像名、健康检查、副本数一律按流水线现有规则取自模板（参数越少，YAML 替换面越小）；④**代码库与端解耦**——现实中一个库常同时装多端项目，每条流水线显式选择指向的库（默认唯一库），「一库多端」靠**库内构建目录（workDir）**区分，库上移为项目级；⑤流水线不是「选一条参考」，而是「复制母版那三条并改名」——名字由新代号派生，与母版一一对应，构建目录取 FR-2 派生并改名后的确定值，两边必须一致。

---

#### FR-4 · 设置云效 DevOps 项目

| 项 | 内容 |
|----|------|
| **目标** | 设置（选择已有）云效 DevOps 研发项目，作为迭代与工作项的容器；**不在插件内创建项目** |
| **前置条件** | FR-1 已保存；已补齐表 C 行 C1–C4 |
| **界面** | F3 插件首页（左栏项目列表 + 右栏两面板）；区域：左栏搜索与可滚动项目列表（含初始化状态）；右栏项目统计（未选项目时的默认）/ 选中项目的四入口（项目详情 · 迭代需求、迭代任务、迭代测试、PRD 工作台） |

字段表：

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| 当前项目 | 引用 | `projex_list_projects` / `projex_get_project` | 是 | **只能选已有项目**；列表只读自云效组织 |
| 项目简介 | 字符串 | 用户输入 | 否 | 展示用，对应 `description` |
| 关联代码库 | 引用 | FR-2 回带 | 否 | 便于从项目跳到代码库 |
| 设置结果 | 枚举 | 系统回读 | 否 | 未设置 / 已选择 / 失败 |

交互流程（编号步骤）：

1. 进入插件首页，左栏列出组织内 DevOps 项目（表 C 行 C1），搜索输入即筛选；项目条目多时只在左栏内部滚动；未选项目时右栏默认展示项目统计（项目总数、初始化进度、任务分布、最近访问）。
2. 点左栏某个项目 → 右栏切换为该项目的标题、状态、四个计数与**四个入口**：项目详情 · 迭代需求（进二级项目页）、迭代任务（进看板）、迭代测试（进用例与计划）、PRD 工作台（默认同步云效 PRD，也可上传本地文档，拆分为五类研发资产写入当前项目）；未初始化的项目点任一入口先进入项目初始化（§2.9）。
3. **列表里没有需要的项目时，界面不给新建入口**：底部说明提示「新建项目请在云效控制台完成」，用户建完回插件刷新列表即可（V4-4）。官方 `CreateProject` 能力与门槛见 §4.13.3，留作后续版本的可选项（O8）。
4. 选中项目即设为当前项目并缓存于档案；需要改简介时走 `projex_update_project`（覆盖须确认）。
5. 设置成功后，F3 的项目详情与迭代需求、FR-6 拆分、FR-7 看板的「当前项目」默认值随之更新；新项目首次点击仍先走项目初始化。

状态机：未设置 → 浏览中 → 已选择（对应附录 A 的「DevOps 项目」子态）。

依赖能力：§4.12 表 C 行 C1（projex_list_projects）、C2（projex_get_project）、C4（projex_update_project）；上述为宿主侧逻辑工具名，**本仓当前未接线**。云效官方 **OpenAPI** 已有 `projects:search` / `CreateProject` / `UpdateProjectField`（§4.13.3）；官方**托管 MCP** 的 `project-management` 公开工具清单覆盖项目搜索与详情，但**未列出创建项目工具**（§4.13.2）。一期只接 C1 / C2 / C4，**C3 创建项目不接线**。

异常与回滚：见 §4.7「云效资源类」；选择失败不写档案；重跑幂等，不自动删项目。

对应验收：V4-1、V4-2、V4-3、V4-4、V4-5、V4-6。

**创建项目的官方门槛**：官方 `CreateProject` 要求「项目协作 / 项目 / 读写」权限，必填 `name`、`customCode`（4–6 位大写字母、组织内唯一）、`scope`（`public` / `private`）与 `templateId`，而 `templateId` 必须先调用 `GetProjectTemplateList` 获取；响应仅返回新项目 `id`。帮助中心另有旧版路径 `POST /organization/{organizationId}/projects/createProject`，参数名为 `templateIdentifier`，与新版 `templateId` 不同，接线时须按所用版本核对。创建项目是组织级、不可自动回滚的写操作，且前置依赖多于「绑定既有项目」，这是一期先落地「锁定既有项目」的实质理由，而非平台不支持（§4.13.3、§4.13.4 第 6 条）。

**设计取舍与差异**：原始诉求含「创建 / 选择」两条分支；一期只落地「锁定既有项目」——列出 / 查看项目并绑定为当前项目，配置存入档案；**界面完全不出现新建项目入口与其表单**。原因：插件内创建一个项目后仍要回云效配模板、配权限、拉迭代，多一步反而绕；把创建留在云效控制台，插件只负责「选一个项目继续往下做」（§2.5），将「创建或设置」收敛为「设置」单一分支。

---

#### FR-5 · 取得 PRD（默认云效同步，本地文件作补充）

| 项 | 内容 |
|----|------|
| **目标** | 为 FR-6 准备本次要拆分的 PRD 内容：**默认从当前云效项目同步需求类工作项的标题、描述、验收标准与工作项附件**；产品另行提供的文档经「本地上传 / 宿主工作区」并入，作为**补充来源并列参与拆分**，不覆盖同步结果 |
| **前置条件** | FR-4 已选定当前项目（云效同步必需）；宿主文件通路可用（本地上传必需）；已补齐表 C 行 C10、C25、C26 |
| **界面** | F8 PRD 工作台上部；区域：来源四选一（云效同步 · 默认 / 本地上传 / 宿主工作区按相对路径读取 / 企微云盘禁用）+ 同步结果摘要与「重新同步」 |

字段表：

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| PRD 来源 | 枚举 | 用户选择 | 是 | **云效同步（默认）** / 上传本地文件（`<input type="file">` + Upload 接口） / 宿主工作区按相对路径读取（目录列举未接线，见 C31） / 企微云盘（禁用占位） |
| 同步范围 | 枚举 | 系统 / 用户 | 否 | 当前项目的**需求类工作项**（标题、描述、验收标准）与工作项附件；可按当前迭代或全项目 |
| 同步批次 | 对象 | 系统 | 否 | 同步时间、成功条数、失败条目；多次同步以最新批次为准，历史只留摘要 |
| 补充文档 | 列表 | 文件读取 | 否 | 本地上传或工作区选入的文档，显式标注「补充来源」，不覆盖同步结果 |
| 文件名 | 字符串 | 系统读取 | 否 | 展示用；云效同步侧取工作项标题与附件名 |
| 文件内容 | 文本 | 文件读取 | 是 | 解析后的纯文本，供拆分；富文本描述按纯文本降级并保留换行 |
| 冲突项 | 列表 | 系统 | 否 | 同一需求在云效与本地文档中描述不一致时列出，交用户逐条确认以哪一侧为准 |
| 解析状态 | 枚举 | 系统 | 否 | 未就绪 / 同步中 / 解析中 / 已就绪 / 部分就绪 / 失败 |
| 文件校验 | 枚举 | 系统 | 否 | 类型 / 大小 / 编码校验结果 |

交互流程（编号步骤）：

1. 进入 F8 时**先自动同步**（无需用户点任何按钮）：按当前项目调 `workitem_list_requirements`（表 C 行 C10）取需求类工作项，再逐条 `workitem_get`（C25）读描述与自定义字段（验收标准），并按 `workitem_list_attachments` + `workitem_get_file`（C26）拉取附件文本。
2. 同步完成后只显示一行摘要——「已同步需求工作项 N 条、附件 M 份，最近同步 hh:mm」——不在页面上整段铺开富文本；失败条目折算成「部分就绪」并给出原因摘要与重试入口。
3. 用户可切换来源：「本地上传」用标准 `<input type="file">`（用户手势选择，File API 读出后经 `AttachmentController.Upload` 的 JSON + Base64 通道落盘到 `.luban-agent/temp/uploads/<日期>/`）；「宿主工作区」经 `FileController.Read` / `WebFileService.cs` **按已知相对路径读取**——**宿主目前没有目录列举接口**（C31），接线前不得把这一来源画成「可浏览工作区文件树」，只允许手填相对路径或从最近用过的路径里选（V5-7）。两者都标记为**补充来源**，与同步结果并列，不覆盖。
4. 「企微云盘」入口显示为**禁用占位**，标注「暂未接线」，不产生任何请求（V5-3）。
5. 上传/选择后做类型、大小、编码校验；解析为纯文本。
6. 同一需求在云效与本地文档中描述不一致时列入「冲突项」，逐条选择以哪一侧为准；**未选择时默认以云效为准**，避免本地旧文档覆盖线上口径。
7. 点「确认本次 PRD」→ 生成 `PrdDocument`（`source` = 云效同步 + 补充来源），进入 FR-6 拆分。
8. 云效侧读取失败（无权限、项目内暂无需求、Projex 未接线）时**不阻断**：页面显示「云效暂无可同步的需求，可上传本地 PRD 继续」，本地来源照常可用；反之没有本地文件时同步结果也可独立支撑拆分。

状态机：未就绪 → 同步中 / 上传选择中 → 解析中 → 已就绪 / 部分就绪（属周期性作业入口，不进附录 A 的配置向导状态机）。

依赖能力：§4.12 表 A 通路行 A14（`AttachmentController.Upload`）、A15（`FileController.Read`）、A16（`WorkspaceController.cs`）、A16b（`WebFileService.cs`）；表 C 行 C10（`workitem_list_requirements`）、C25（`workitem_get`）、C26（`workitem_list_attachments`）；表 C 行 C21（`WecomDriveConnector`，P2 / 开放，本期禁用）。C10 / C25 / C26 在云效平台侧分别对应 `ListWorkitems` / `GetWorkItemInfo` / `ListWorkitemAttachments` + `GetWorkitemFile`（§4.13.4 第 12 条），**本仓均未接线**。

异常与回滚：见 §4.7「目录/文件类」「PRD 同步类」与「能力缺口类」；解析失败不生成 `PrdDocument`；同步失败只降级为本地来源，不在云效侧留任何写入；禁用入口不发请求。

对应验收：V5-1、V5-2、V5-3、V5-4、V5-5、V5-6、V5-7。

**设计取舍与差异**：原始「读取本机或企微云盘里的 PRD 文件」的口径是「先有文件才能拆分」，现改为**云效同步为默认来源**：PRD 本来就躺在云效项目的需求描述、验收标准与附件里，让用户先下载成文件再上传是重复劳动；本地上传保留为**补充**，用于产品另外给的文档。仍受两条既有约束：同源 iframe 下插件 JS **读不到浏览器本机 FS**（§2.3、§6 R9），故本机文件必须经宿主上传接口（`<input type="file">` 用户手势选择不受此限）；**企微微盘需独立的企微应用 secret 与「微盘」权限且微文档不可下载**（§4.13.3 C21 行；§6 R3），故列 P2 并做禁用占位——注意平台侧 `wedrive/*` 接口是存在的，一期禁用不是因为「平台没有接口」；云效侧**项目知识库文档没有独立读取接口**，因此云效同步的边界是「工作项 + 工作项附件」，见 R13。

---

#### FR-6 · 拆分 PRD 到云效研发资产

| 项 | 内容 |
|----|------|
| **目标** | 把一份 PRD 拆为迭代、需求、任务、测试用例、测试计划，并落到**当前云效项目**；目标项目固定取自首页选中项（或看板当前项目），本页不再选项目 |
| **前置条件** | FR-4 已设置当前项目；FR-5 已就绪（云效同步结果或补充文档至少一侧可用）；已补齐表 C 行 C5–C20 |
| **界面** | F8 PRD 工作台（上部来源、中下拆分树与落库目标、底部拆分明细）+ F9 迭代测试（用例生成与测试计划）+ F3 浏览；区域：页头固定显示目标项目 + 拆分树 + 五类落库目标表 + 拆分明细；测试屏左列用例生成来源与生成结果、右列测试计划设置 |

字段表：

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| 目标项目 | 引用 | `DevOpsProject` 当前项目 | 是 | **固定为当前云效项目**，页头只读展示，不可在本页切换；要换项目回首页左栏重选 |
| 目标迭代 | 引用 / 新建 | `projex_list_sprints` / `projex_create_sprint` | 是 | 选已有迭代，或填新迭代名称与起止日期待建；**新建时 `sprintId` 先为空**，与其余四类同批创建后再回填，提交前以「待创建」标记，避免「迭代既是产出又是必填引用」的循环 |
| 需求清单 | 列表 | 拆分生成 | 是 | 每条含标题、描述、优先级 |
| 任务清单 | 列表 | 拆分生成 | 是 | 每条含标题、parentId（挂需求）、预计完成时间、负责人、优先级 |
| 测试用例清单 | 列表 | 拆分生成 | 否 | 每条关联需求/任务 |
| 测试计划 | 引用 | 拆分生成 | 否 | 关联本迭代测试用例 |
| 用例生成来源 | 枚举 | 用户选择 | 否 | 来自需求（默认）/ 来自 PRD / 来自任务 / 手工补充；可多选 |
| 计划名称 | 字符串 | 用户输入 | 否 | 默认「迭代 N 回归测试计划」 |
| 测试环境 / 执行人 / 计划周期 | 枚举 / 引用 / 日期区间 | 用户选择 | 否 | 环境取自项目环境配置；周期默认覆盖迭代剩余时间 |
| 用例范围 | 枚举 | 用户选择 | 否 | 全部用例 / 仅 P0 / 仅冒烟；与「已选用例」条数联动 |
| 拆分明细 | 列表 | 系统 | 否 | 逐条展示「来源段落 → 目标资产 → 落库回执」 |
| 落库状态 | 枚举 | 系统回读 | 否 | 未拆分 / 拆分中 / 已预览 / 已提交 / 部分失败 |

交互流程（编号步骤）：

1. 页头确认目标项目（固定为当前云效项目，只读），再在 F8 中下选择/新建目标迭代（表 C 行 C5–C7）。
2. 点「生成拆分方案」→ 系统按 PRD 章节产出**迭代、需求、任务、测试用例、测试计划五类预览清单**（不落库），并逐条标出来源是「云效同步」还是「本地上传」。
3. 用户逐条核对、增删、调整 parentId 与预计完成时间；状态相关字段先经 `workitem_list_statuses` / `workitem_list_workflow` 取可用枚举（表 C 行 C8、C9）。
4. 点「提交到云效」→ 依次创建迭代（C7，复用已有则跳过）、需求（C10/C11）、任务（C12/C13）、关系（C16）、测试用例（C19）、测试计划（C20），必要时更新任务（C14）；**所有写入都以目标项目为根，不跨项目**。
5. 逐条记录落库回执，生成 `PrdSplitPlan` 与拆分明细（含来源段落与来源类型），支持「部分失败」时只重跑失败项（幂等键）。
6. 提交成功后可在 F3 浏览页查看项目 → 迭代 → 需求列表。
7. 测试用例与测试计划也可不经 PRD 拆分：首页右栏点「迭代测试」直接进入 F9，按当前迭代的需求与验收标准生成用例（来源可选、覆盖范围可见、重复用例自动合并并保留来源），再在右列设置计划名称、关联迭代、环境、执行人、周期与用例范围后保存；落库仍走 C19 / C20。
8. 本仓未接线阶段，F9 的生成与保存只做本地演示：写入本地草稿并在界面标注「本仓未接线」，不发送任何云效写请求（V6-5、V6-6）。

状态机：未拆分 → 已预览 → 提交中 → 已提交 / 部分失败（对应附录 C 的「由拆分新建」入口）。

依赖能力：§4.12 表 C 行 C5（projex_list_sprints）、C6（projex_get_sprint）、C7（projex_create_sprint）、C8（workitem_list_statuses）、C9（workitem_list_workflow）、C10（workitem_list_requirements）、C11（workitem_create_requirement）、C12（workitem_list_tasks）、C13（workitem_create_task）、C14（workitem_update_task）、C16（workitem_create_relation）、C19（testcase_create）、C20（testplan_create）；以上均为宿主侧逻辑工具名，**本仓当前未接线**，平台侧官方 API / 托管 MCP 对应见 §4.13.3，M2 的工作是接线与实机验证而非自研。

异常与回滚：见 §4.7「云效资源类」「能力缺口类」；拆分结果不自动删、不自动回滚远端，只列失败清单供重跑；空值/时区的预计完成时间处理见 §6 R8。

对应验收：V6-1、V6-2、V6-3、V6-4、V6-5、V6-6、V6-7、V6-8。

**设计取舍与差异**：与原始诉求一致；补充「目标项目固定为当前云效项目」「先预览后提交」两段式与「拆分明细 + 落库回执」可核口径，避免一次性盲写云效，也避免拆分结果误落到别的项目。

---

#### FR-7 · DevOps 看板与任务详情（含评论）驱动的工作区会话

| 项 | 内容 |
|----|------|
| **目标** | 查看项目列表、迭代列表、选择当前迭代、查看迭代需求列表；**更重要**：按任务状态分类查看迭代任务看板（按预计完成时间排序、状态以云效实际状态为准）；点任务卡进入任务详情，读到描述、验收标准与**评论**，并一键把「标题 + 详情 + 评论」带入当前项目工作区开始任务 |
| **前置条件** | FR-4 已设置当前项目；FR-2 已设定本机工作目录（即工作区根目录）；FR-6 已产出任务；已补齐表 C 行 C8、C9、C12、C14、C17、C18、C23、C24；Agent 能力（表 A 通路 A17 / A17b / A18–A21）与工作区 / 会话接口（§3.6 能力表）可用 |
| **界面** | F1 迭代任务看板（项目工作台，从首页右栏「迭代任务」进入，非插件首页）+ F2 任务详情与工作区 + F3 浏览；区域：顶部面包屑、当前项目与迭代选择、项目详情入口 + 动态状态列看板；F2 为独立下钻页（不是抽屉），左列任务详情与评论、右列「在工作区开始任务」与工作区会话回显 |

字段表：

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| 当前项目 | 引用 | FR-4 档案 | 是 | 顶部可切换 |
| 当前迭代 | 引用 | `projex_list_sprints` / `projex_get_sprint` | 是 | 选择后加载该迭代数据 |
| 迭代需求列表 | 列表 | `workitem_list_requirements` | 否 | F3 浏览页展示 |
| 看板状态列 | 列表 | `workitem_list_statuses` / `workitem_list_workflow` **动态生成** | 是 | 列名/列数/列序完全取自枚举，**不得硬编码四列** |
| 任务卡片 | 列表 | `board_list_workitems_by_sprint` / `workitem_list_tasks` | 是 | 含编号、标题、状态、负责人、预计完成时间、优先级 |
| 排序口径 | 枚举 | 前端 | 是 | 按预计完成时间升序，空值置末、时区统一（§6 R8） |
| 任务详情 | 对象 | `workitem_list_tasks` / `workitem_list_bugs` 回读 | 否 | F2 展示：标题、描述、验收标准、负责人、预计完成时间 |
| 任务评论 | 列表 | `workitem_list_comments`（官方 `ListWorkitemComments`） | 否 | 按时间倒序，含作者、时间、正文；最新一条标记置顶；富文本按纯文本降级展示并保留换行 |
| 会话绑定 | 对象 | `TaskSession`（插件本地） | 否 | 任务 ↔ 会话 ↔ 工作区；含首轮输入摘要、带入评论条数、创建时间、最近活跃时间 |
| 会话状态 | 枚举 | 系统 | 否 | 未开始 / 会话已创建 / 进行中 / 已完成 / 已取消 / 失败（只反映会话本身，不模拟技能内部阶段） |

交互流程（编号步骤）：

1. 从插件首页「项目列表」点击某个项目进入 F1（§2.9）；若该项目首次点击且尚未初始化，先进入「项目初始化」完成代码库与流水线，完成后自动到达 F1。在 F1 顶部选择当前迭代，或点「项目详情」进入 F3 浏览项目 → 迭代 → 需求列表。
2. 系统经 `workitem_list_statuses` / `workitem_list_workflow` 取该项目的**状态枚举**，据此动态生成看板列（列名、列数、列序与返回一致）；加载 `board_list_workitems_by_sprint` 取该迭代任务。
3. 任务按状态落入对应列，列内按预计完成时间升序排列（空值置末、时区统一）；卡片展示编号、标题、负责人、预计完成时间、优先级。
4. 点任一任务卡 → 进入 F2 任务详情：上方标题与状态、左列任务详情（描述、验收标准、所属需求 / 迭代 / 代码库与分支）与**评论列表**（经 `workitem_list_comments` 读取，倒序、最新置顶），底部可直接补充一条评论（经 `workitem_create_comment` 写回云效，成功后刷新列表）。
5. 右列展示「在工作区开始任务」卡片，明确列出**将带入的内容**（任务标题、任务描述、全部评论、代码库与分支）、**目标工作区**（FR-2 设定的本机工作目录）与**调用技能**（brainstorming）。内容随评论变化实时更新，带入条数可见。
6. 用户点「**在工作区开始任务**」→ 插件按工作目录查/建工作区（`WorkspaceController.ByRootPath` / `Create`），在该工作区下创建会话（`SessionController.Create`，标题形如「任务 10228 · 流水线 YAML 替换逻辑」），把任务上下文拼成首轮输入后经 `/agent/chat` 发起并流式回显；会话与任务的绑定写入 `TaskSession`。
7. 首轮输入要求以 brainstorming 技能开场（先用 `UseSkill` 加载该技能再作答），以方案模式启动（`PermissionMode = plan`）；技能产出方案后由用户在会话里明确确认，确认前不写代码、不建骨架——这一步由技能自身门禁保证，插件不额外维护「批准」状态。
8. 会话出现在宿主左侧会话列表，用户可回到宿主继续同一段对话；再次进入该任务时页面直接显示已有会话与最近消息（`SessionController.Messages`），并可「继续会话」或「重新开始」。
9. 会话完成或取消后展示结果摘要；`workitem_update_task` 回写任务状态/备注（是否回写以用户确认为准，不自动改云效状态）。

状态机：看板任务见附录 C；任务会话见附录 B。

依赖能力：§4.12 表 C 行 C1（projex_list_projects）、C5（projex_list_sprints）、C6（projex_get_sprint）、C8（workitem_list_statuses）、C9（workitem_list_workflow）、C10（workitem_list_requirements）、C12（workitem_list_tasks）、C14（workitem_update_task）、C15（workitem_list_bugs）、C17（board_list_fields）、C18（board_list_workitems_by_sprint）、C23（workitem_list_comments，只读）、C24（workitem_create_comment，读写）；表 A 通路行 A17（`AgentController`）、A17b（`WebAgentService.cs` SSE）、A18（`BrainstormingSkill`）、A19（`HostAssistantBridge.RunAsync`）、A20（`StepPrompts`）、A21（`AssistantToolPolicy`）；工作区与会话接口见 §3.6 表（`WorkspaceController`、`SessionController`）。

异常与回滚：见 §4.7「能力缺口类」「任务会话类」「评论类」「状态枚举类」；状态枚举取不到时看板显示空态而非回退四列；评论读取失败时任务详情仍可看，评论区显示失败原因与重试且**不阻断开始任务**（带入内容改为「评论暂不可用」）；会话创建失败保留任务页与已拼好的首轮输入，可重试；用户在会话中取消后 `TaskSession` 置为已取消，不产生任何实现产物。

对应验收：V7-1、V7-2、V7-3、V7-4、V7-5、V7-6、V7-7、V7-8、V7-9、V7-10。

**设计取舍与差异**：原始「可将未开始/开发中的任务用 WebHost 的 Agent 能力调 brainstorming 技能完成开发任务」。`BrainstormingSkill.cs` 的「硬门禁」明确「设计方案被用户明确批准前不得调用任何实现类 Skill、不得写代码、不得创建项目骨架、不得执行任何实现动作」，所以 brainstorming 是**设计门禁技能**而非实现技能，不能当作「一键完成开发」的按钮。落地方式是把用户视角收敛成一步：**看任务详情（含评论）→ 在工作区开始任务**，由插件负责把任务上下文组装进会话、由技能负责「方案先于实现」的判定；不由插件另立「阶段一 / 阶段二 / 批准方案」的界面状态。「未开始/开发中」仍作为是否显示主操作的判定依据；看板状态列**必须由状态枚举动态生成，不得硬编码四列**。

---

### 4.4 外部系统集成

| 外部系统 / 子服务 | 现有能力（可核） | 本 PRD 使用点 | 缺口登记 |
|-------------------|------------------|---------------|----------|
| 云效 Codeup | 7 个 MCP 工具（`YunxiaoToolDefinitions.cs`） | FR-2 列母版库 / 复制建库 / 分支 | `codeup_update_repository`（表 B 行 B2） |
| 云效 Flow | 6 个 MCP 工具 + `FlowClient.GetPipelineAsync`（SDK-only） | FR-3 母版流水线 / 新建 / 覆盖 | `flow_get_pipeline`（表 B 行 B1）；无「复制」接口（R1） |
| 云效 Projex / DevOps 项目 | 官方 OpenAPI 已具备（含 `CreateProject`）；官方托管 MCP 覆盖项目搜索 / 详情 / 迭代 / 工作项 / 状态 / 测试，但**未列出创建项目工具**；**本仓未接线** | FR-4、FR-7 项目列表 | 表 C 行 C1–C4 / §4.13.2、§4.13.3 |
| 云效迭代 / sprint | 官方 `ListSprints` / `CreateSprint` 已具备；本仓未接线 | FR-6、FR-7 | 表 C 行 C5–C7 / §4.13.3 |
| 云效工作项（需求/任务/缺陷/状态/工作流/关系） | 官方 `CreateWorkitem(V2)` / `ListWorkitems` / `GetWorkitemWorkflow` 已具备；本仓未接线 | FR-6、FR-7 | 表 C 行 C8–C16 / §4.13.3 |
| 云效测试用例 / 测试计划 | 官方 `CreateTestCase` / `CreateTestPlan` 已具备；本仓未接线 | FR-6 | 表 C 行 C19、C20 / §4.13.3 |
| 云效看板字段 / 迭代工作项 | **无独立看板 API**；由工作流状态 + `ListWorkitems` 条件过滤组合，无需 `board_*` | FR-7 动态分栏 | 表 C 行 C17、C18 / §4.13.3 |
| 企微云盘 | 官方 `wedrive/*` 三接口已具备；需独立企微应用鉴权，微文档不可下载 | FR-5（一期禁用占位） | 表 C 行 C21（P2 / 开放）/ §4.13.3 |
| 本机文件 / 宿主工作区 | `AttachmentController.Upload`、`FileController.Read`、`WebFileService.cs`、`WorkspaceController.cs` | FR-2、FR-5 | 无（已具备，表 A 通路） |
| WebHost Agent 能力 | `AgentController.cs`、`WebAgentService.cs` SSE、`BrainstormingSkill`、`WorkspaceController`、`SessionController` | FR-2 git 执行通路、FR-7 工作区会话 | 凭据写入与校验另计（表 C 行 C22 / C28 / C29） |

### 4.5 数据模型

> 每个实体给出字段表（字段名 / 类型 / 来源 / 必填 / 说明）。「类型」为业务数据类型（字符串、整数、日期时间、枚举、引用、列表、布尔），非代码类型；「引用」指向另一实体或云效返回的标识。

**YunxiaoConnection（云效连接档案）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| organizationAddress | 字符串 | 用户输入 | 是 | 组织访问地址，仅用于解析 organizationId |
| organizationId | 字符串 | 解析或手填 | 是 | 对应 `connector:yunxiao:organizationid` |
| tokenRef | 引用 | 凭据存储 | 是 | 指向 `connector:yunxiao:token`，不存明文 |
| endpoint | 字符串 | 系统固定 | 是 | `openapi-rdc.aliyuncs.com`，不可覆盖 |
| validatedAt | 日期时间 | 系统 | 否 | 最近校验通过时间 |
| savedAt | 日期时间 | 系统 | 否 | 最近保存时间 |
| status | 枚举 | 系统 | 是 | 未配置/已校验/已保存/失效 |

**CodeupRepoSetup（代码库档案，支持多库）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| templateMode | 枚举 | 系统判定 | 是 | **single（母版库 1 个，锁定）/ multi（多个母版可选，一项复制一个库）** |
| templateRepoIds | 列表 | `codeup_search_repos` 筛母版库 | 是 | 选中的母版库；single 模式长度为 1 |
| templateCode | 字符串 | 系统从母版库名解析 | 是 | 母版代号（`TJCES-Mixed` → `TJCES`），所有名字替换的查找键 |
| projectCode | 字符串 | 用户输入 | 是 | 新项目代号（如 `RGMS`），**唯一命名输入**，派生库名 / 子目录 / 流水线名 |
| copyMode | 枚举 | 系统选择 | 是 | **clone-push（主）/ template-project（降级）**；官方无 Fork 接口，见 §4.13.4 第 11 条 |
| repos | 列表 | 系统派生 | 是 | 每项含：母版库 id 与名、**派生出的 repoName**、repoId、cloneUrl、subProjectRenames、readyStatus；**一母版一库** |
| groupId | 引用 | `codeup_list_groups` / `codeup_create_group` | 是 | 归属代码组，多库共用 |
| visibility | 枚举 | 用户选择 | 是 | 组织内公开/私有，多库共用 |
| workspacePath | 字符串 | 用户选择 | 是 | 宿主工作区目录（非浏览器本机 FS），一个项目一个目录 |
| targetBranch | 字符串 | 默认 `master` | 是 | 母版默认分支即 master，副本同起；开发分支由 Agent 后续切出 |
| syncAllBranches | 布尔 | 用户选择 | 否 | 仅 `template-project` 降级路径使用：是否导入母版全部分支（官方 `templateProject.syncAllBranches`，默认否，只导默认分支） |
| subProjectRenames | 列表 | 系统派生 + 本地执行 | 是 | 子目录改名清单：`{from: 'TJCES-Admin', to: 'RGMS-Admin'}` × 3；**由宿主克隆后本地 `mv` 并提交**（官方无重命名目录接口） |
| subProjects | 列表 | 系统派生（扫描兜底校验） | 否 | 改名后的实际目录（`RGMS-Admin` / `RGMS-Mobile` / `RGMS-Service`），供 FR-3 构建目录使用；与预期不符时提示 |

**FlowPipelineSetup（流水线档案，三端各一条）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| role | 枚举 | 系统固定 | 是 | **mobile（移动端）/ admin（管理后台）/ backend（后端）**，三端固定 |
| enabled | 布尔 | 用户勾选 | 是 | false 表示该端跳过，不读 YAML、不提交 |
| env | 枚举 | 系统固定 | 是 | **固定 `dev`**；环境不是流水线内建属性，差异由流水线变量 / AppStack 应用环境实现，初始化只固定 dev，测试与生产沿用既有编排 |
| linkedRepoName | 引用 | FR-2 派生出的新库 | 是 | 该端流水线指向的新库（如 `RGMS-Mixed`）；三端共用同一个新库 |
| workDir | 字符串 | FR-2 派生的 subProjectRenames | 是（单工程库可空） | 该端流水线在库内的构建根目录，如 `RGMS-Admin`；**「一库多端」下区分三端的唯一落点**，必须与 FR-2 改名后的实际目录一致，随 YAML 的 workDir / 构建命令一起替换 |
| backendPort | 数字 | 用户输入 | 后端启用时必填 | **后端唯一可填参数**，写入 dev 环境变量；其余参数按模板现有规则处理，不入档案 |
| templatePipelineName | 字符串 | `flow_list_pipelines` 反查 | 启用时必填 | 母版那条流水线名，如 `CME_DEV_Deploy_{母版代号}-Mobile`（真值见 `LubanAgent.Plugin.NewProject/docs/org-reference.md`）；用于定位 YAML 来源与解析分组前缀 |
| pipelineName | 字符串 | 系统派生（可改） | 是 | 新流水线名 `{分组前缀}_DEV_Deploy_{新代号}-{端}`，如 `CME_DEV_Deploy_RGMS-Mobile`；母版复制后必须改的第二类名字 |
| referencePipelineId | 引用 | `flow_list_pipelines` | 启用时必填 | 母版流水线 id（仅作 YAML 模板，须为 YAML 化） |
| referenceYaml | 文本 | `flow_get_pipeline` | 是 | 参考 YAML 详情 |
| targetPipelineName | 字符串 | 同上 pipelineName | 是 | 新建/覆盖目标名，默认取派生值；用户改过则以此为准 |
| placeholders | 列表 | 用户确认 | 是 | 代码库 id / 名称 / 构建目录 |
| renderedYaml | 文本 | 系统生成 | 是 | 占位符替换后 YAML |
| diffPreview | 文本 | 系统生成 | 否 | 参考 vs 替换后差异 |
| submitMode | 枚举 | 用户选择 | 是 | 新建/覆盖 |
| pipelineId | 引用 | 提交返回 | 否 | 目标流水线 id |

**DevOpsProject（项目档案）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| projectId | 引用 | 云效返回 | 是 | 当前项目 id |
| projectName | 字符串 | 用户输入/返回 | 是 | 项目名称 |
| projectKey | 字符串 | 用户输入/返回 | 是 | 组织内唯一标识 |
| description | 字符串 | 用户输入 | 否 | 简介 |
| linkedRepoIds | 列表 | FR-2 回带 | 否 | 关联代码库（多库，**不按端标注**；库与端的对应关系记在流水线侧） |
| linkedPipelineIds | 列表 | FR-3 回带 | 否 | 关联流水线（三端各一条，每条含指向的代码库） |
| isCurrent | 布尔 | 系统 | 是 | 是否设为当前项目 |

**PrdDocument（PRD 文档）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| documentId | 引用 | 系统生成 | 是 | 文档 id |
| source | 枚举 | 用户选择 | 是 | **云效同步（默认）** / 上传本地 / 工作区 / 企微云盘（禁用） |
| syncScope | 枚举 | 系统 | 否 | 云效同步范围：当前迭代 / 全项目；记录纳入的需求工作项与附件清单 |
| syncedAt | 日期时间 | 系统 | 否 | 最近一次同步完成时间，用于「最近同步 hh:mm」与过期提示 |
| supplements | 列表 | 文件读取 | 否 | 补充来源文档（本地上传 / 工作区），带文件名、编码与解析状态；不覆盖同步结果 |
| conflicts | 列表 | 系统 | 否 | 云效与补充文档描述不一致的条目，含两侧摘要与用户选择 |
| fileName | 字符串 | 文件读取 | 否 | 主来源名称；云效同步时为项目名 + 需求条数摘要 |
| content | 文本 | 文件读取 / 同步 | 是 | 归一化后的纯文本，供拆分 |
| encoding | 字符串 | 系统校验 | 是 | 编码校验结果 |
| parsedAt | 日期时间 | 系统 | 否 | 解析时间 |
| status | 枚举 | 系统 | 是 | 未就绪 / 同步中 / 解析中 / 已就绪 / 部分就绪 / 失败 |

**PrdSplitPlan（拆分计划）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| planId | 引用 | 系统生成 | 是 | 计划 id |
| documentId | 引用 | `PrdDocument` | 是 | 来源 PRD（云效同步 + 补充文档归一化后的那一条） |
| projectId | 引用 | `DevOpsProject` | 是 | 目标项目，**固定等于当前项目**，不可在拆分页改写 |
| sprintId | 引用 | `Iteration` | 提交后必填 | 目标迭代；已有迭代直接引用，**新建迭代在提交回读后回填**，提交前为空并标「待创建」 |
| outputs | 枚举集合 | 系统 | 是 | 五类产出：迭代 / 需求 / 任务 / 测试用例 / 测试计划，逐类计数回显 |
| items | 列表 | 系统生成 | 是 | 逐条「来源段落（含云效同步 / 本地上传标记）→ 目标资产 → 落库回执」 |
| status | 枚举 | 系统回读 | 是 | 未拆分/已预览/已提交/部分失败 |
| idempotencyKey | 字符串 | 系统生成 | 是 | 重跑幂等键 |

**Iteration（迭代）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| sprintId | 引用 | 云效返回 | 是 | 迭代 id |
| projectId | 引用 | `DevOpsProject` | 是 | 所属项目 |
| name | 字符串 | 用户输入/返回 | 是 | 迭代名称 |
| startDate | 日期 | 云效/用户 | 否 | 开始日期 |
| endDate | 日期 | 云效/用户 | 否 | 结束日期 |
| isCurrent | 布尔 | 系统 | 是 | 是否当前迭代 |

**WorkItem（需求 / 任务 / 缺陷统一）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| workItemId | 引用 | 云效返回 | 是 | 工作项 id |
| type | 枚举 | 系统 | 是 | 需求/任务/缺陷 |
| title | 字符串 | 拆分/用户 | 是 | 标题 |
| description | 文本 | 拆分/用户 | 否 | 描述 |
| statusId | 字符串 | **状态枚举**（`workitem_list_statuses` / `workitem_list_workflow`） | 是 | **必须来自枚举，不得写死** |
| statusName | 字符串 | 状态枚举 | 是 | 看板列名来源 |
| dueDate | 日期时间 | 拆分/用户 | 否 | **预计完成时间**，看板排序键；空值置末、时区统一（R8） |
| assignee | 字符串 | 拆分/用户 | 否 | 负责人 |
| priority | 枚举 | 拆分/用户 | 否 | 优先级 |
| parentId | 引用 | 拆分/用户 | 否 | 挂接的需求/父工作项 |
| sprintId | 引用 | `Iteration` | 是 | 所属迭代 |

**TestCase（测试用例）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| caseId | 引用 | 云效返回 | 是 | 用例 id |
| title | 字符串 | 拆分生成 | 是 | 用例标题 |
| relatedWorkItemId | 引用 | `WorkItem` | 否 | 关联需求/任务 |
| steps | 文本 | 拆分生成 | 否 | 步骤与预期 |
| planId | 引用 | `TestPlan` | 否 | 所属测试计划 |

**TestPlan（测试计划）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| planId | 引用 | 云效返回 | 是 | 计划 id |
| name | 字符串 | 拆分生成 | 是 | 计划名称 |
| sprintId | 引用 | `Iteration` | 是 | 所属迭代 |
| caseIds | 列表 | 拆分生成 | 否 | 关联测试用例 |

**BoardColumn（看板列，状态枚举动态生成）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| statusId | 字符串 | `workitem_list_statuses` / `workitem_list_workflow` | 是 | 与枚举一致 |
| name | 字符串 | 状态枚举 | 是 | **列名**取自枚举 |
| order | 整数 | 状态枚举 / `board_list_fields` | 是 | **列序**取自枚举 |
| workflowId | 引用 | `workitem_list_workflow` | 否 | 工作流标识 |
| isStartColumn | 布尔 | 系统推导 | 是 | 是否「未开始」类（可触发 Agent） |
| isActiveColumn | 布尔 | 系统推导 | 是 | 是否「开发中」类（可触发 Agent） |

> 说明：`BoardColumn` 不预置任何固定列；列名、列数、列序完全由状态枚举生成（V7-3）。「未开始/开发中」的可触发判定由枚举元数据推导，不靠字面量硬编码。

**TaskSession（任务与会话的绑定）**

| 字段名 | 类型 | 来源 | 必填 | 说明 |
|--------|------|------|------|------|
| linkId | 引用 | 系统生成 | 是 | 绑定 id |
| workItemId | 引用 | `WorkItem` | 是 | 目标任务；同一任务只保留一条有效绑定 |
| workspaceId | 引用 | `WorkspaceController.ByRootPath` / `Create` | 是 | 目标工作区（FR-2 的本机工作目录） |
| sessionId | 引用 | `SessionController.Create` | 是 | 工作区下的会话 |
| seedDigest | 文本 | 系统 | 是 | 首轮输入摘要（标题、描述长度、评论条数、代码库与分支），用于判断是否需要重新带入 |
| seedCommentCount | 数字 | `workitem_list_comments` | 否 | 首轮带入的评论条数 |
| seedAt | 日期时间 | 系统 | 是 | 首轮输入发送时间 |
| lastActiveAt | 日期时间 | 系统 | 否 | 最近活跃时间，用于列表排序与「继续会话」 |
| status | 枚举 | 系统 | 是 | 未开始 / 会话已创建 / 进行中 / 已完成 / 已取消 / 失败 |
| result | 文本 | 会话 | 否 | 结果摘要与产物清单（用户在会话里确认后回填） |

> 说明：本模型**不记录技能的方案 / 批准中间态**。方案是否已获批准以会话内对话为准，插件若另存一份「已批准」会与真实情况脱钩（§2.7 取舍说明）。

### 4.6 界面设计

**Frame 清单表（9 行，与画布 Frame 数一致）**

| Frame | 名称 | 屏序 / 页面定位 | 功能编号 | PRD 章节 | 主要区域 |
|-------|------|-------------------|----------|----------|----------|
| F0 | 信息架构与规范页 | 0 / 9 · 非业务屏 | 全局 | §2.6 / §2.9 / §3.4 / §4.6 | 流程总览、层级表、视觉令牌、硬约束摘要、Frame 索引 |
| F1 | 迭代任务看板（项目工作台） | 4 · 二级 | 功能 7 | FR-7 / §4.6.1 | 面包屑、当前项目与迭代、动态状态列看板（任务卡带「打开任务」）、三条下钻入口、迭代概览 |
| F2 | 任务详情与工作区 | 6 · 三级 | 功能 7 | FR-7 / §2.7 / §4.10 | 任务标题与状态、任务描述与验收标准、评论列表与发表、在工作区开始任务卡片、工作区会话回显 |
| F3 | 项目列表与项目·迭代·需求 | 2 / 5 · 一级首页 + 二级 | 功能 4 / 5 / 6 / 7 | FR-4 / FR-6 / FR-7 / §2.9 | 首页左栏：搜索、可滚动项目列表与初始化状态（**无新建入口**）；右栏两面板：项目统计（默认）/ 选中项目的四入口（项目详情 · 迭代需求、迭代任务、迭代测试、PRD 工作台）；屏 5 复用其项目详情与迭代需求三栏 |
| F4 | 云效连接 | 1 · 入口门禁 | 功能 1 | FR-1 / §3.6 / §3.8 / §4.9 | 首次打开阻断提示、地址 / 组织 ID / PAT 表单、校验与保存状态 |
| F5 | 项目初始化 · 复制代码库 | 3 · 二级（与 F6 合并） | 功能 2 | FR-2 | **母版库下拉**（= 已建好的母版代码库，单母版锁定 / 多母版勾选）**+ 新项目代号 + 命名派生预览**（库名 / 子目录 / 流水线名三段替换）、工作区目录、分支（默认 master）、复制与改名结果 |
| F6 | 项目初始化 · 复制流水线 | 3 · 二级（与 F5 合并） | 功能 3 | FR-3 | 新库标签（`RGMS-Mixed`）+ 三端流水线行（勾选 / **派生的构建目录** / **派生的流水线名**）、**后端端口盒（只 1 项监听端口）**、差异预览条（复制 1 库 + 3 流水线、替换 4 类名字 + 写入 1 项端口） |
| F8 | PRD 工作台 | 7 · 辅助作业页（首页右栏或看板进入） | 功能 5 / 6 | FR-5 / FR-6 | 上部：来源四选一（云效同步为默认 / 本地上传 / 宿主工作区 / 企微云盘禁用）+ 同步摘要与重新同步；中下：拆分树（迭代 → 需求 → 任务 → 测试用例、测试计划）与五类落库目标表；底部：拆分明细（来源段落 → 目标资产 → 落库回执） |
| F9 | 迭代测试 · 用例与计划 | 8 · 二级 | 功能 6 | FR-6 / §2.9 | 左列：生成来源、覆盖范围、生成结果用例列表；右列：计划名称 / 关联迭代 / 环境 / 执行人 / 周期 / 用例范围 / 保存与计划摘要 |

> 全部 Frame 统一 **826×694**（宿主 iframe 实际可用区域，推导见 §2.6），贴合宿主浅色令牌与控件尺度；不自绘宿主侧栏/顶栏，每屏用面包屑标明层级（触发条件与去向由页内提示承载），底部动作栏只放按钮；每个 Frame 元信息含「功能编号 + PRD 章节」（核对规则 1、2）。**屏序是 HTML 预览稿的绘制顺序，与 F 编号不是一一对应**：F5 + F6 合并为屏 3「项目初始化」、F0 拆为屏 0「流程总览」与屏 9「设计令牌与硬约束」；F9 对应屏 8；F7（创建新项目）不设，Frame 共 9 个；映射见 §2.9。

**视觉令牌表**

| 令牌 | 取值 | 用途 |
|------|------|------|
| 背景 | `#f3f4f6` | 页面底色 |
| 卡片面 | `#ffffff` | 卡片、面板、表单容器 |
| 次级面 | `#f7f8fa` / `#eef0f4` | 看板列底、分组标题、只读区 |
| 边框 | `#e5e7eb` | 卡片与分隔线 |
| 主文字 / 次文字 | `#17191d` / `#5f6670` | 标题 / 说明 |
| 强调 | `#3d5afe`（soft `#eef1ff`） | 主按钮、选中态、链接 |
| 危险 / 警告 / 成功 | `#d92d20` / `#b54708` / `#067647` | 失败、禁用占位提示、成功回执 |
| 圆角 | 8 / 12 / 18px | 控件 / 卡片 / 面板 |
| 正文 | 14px，系统 UI 字体栈 | 全局 |
| UI 框架 | 无（纯手写 CSS） | 看板列、数据表格、左右分栏面板均自绘（宿主无可复用类） |

**状态空态表**

| 界面区域 | 空态条件 | 展示文案 | 可用操作 |
|----------|----------|----------|----------|
| 全局 · 数据加载中 | 区块首个请求未返回 | 区块内行内提示「加载中…」或骨架占位，**不整页遮挡、不清空上一稳定态**；宿主级「加载插件页面…」由宿主 `PluginPage.tsx` 承担，插件内不重复绘制 | 无（等待，超阈值按对应失败态处理） |
| 插件首次打开 | 未保存有效云效连接 | 「首次打开插件，需要先完成云效连接配置」，全屏阻断，项目列表与看板不可进入 | 填写并保存连接，随后进入项目列表 |
| F4 连接 | 未配置 | 「尚未配置云效连接，请先填写组织信息与 PAT」 | 去填写 |
| 项目列表（插件首页） | 组织下无可见项目，或项目搜索能力未接线 | 左栏空态「当前组织下暂无可见项目，或项目搜索能力尚未接线；新建项目请在云效控制台完成」，右栏仍显示项目统计的空值 | 前往云效控制台创建后刷新 / 重试 |
| 首页右栏 | 尚未选中任何项目 | 默认显示项目统计（项目总数、初始化进度、任务分布、最近访问），并提示「从左侧选择项目查看四个入口」 | 选择左侧项目 |
| 首页右栏 · 已选项目 | 项目尚未初始化 | 状态标「待初始化」，四个入口上方提示「尚未绑定代码库与流水线，点入口会先进入初始化」 | 进入项目初始化（复制母版新建或同步既有） |
| 项目初始化 | 参考流水线为可视化编排类型 | 「可视化编排不支持以 YAML 复制」并禁用下一步 | 返回另选 YAML 化参考流水线 |
| F1 看板 | 未设置当前迭代 | 「请先选择一个迭代」 | 打开项目详情选择 |
| F1 看板 | 状态枚举取不到 | 「状态列表获取失败，暂无法生成看板列」（**不回退四列**） | 重试 / 查看错误原因 |
| F1 看板 | 迭代无任务 | 「当前迭代暂无任务」 | 去 F8 拆分 PRD |
| F8 上部 | 云效同步未取到内容（项目内暂无需求，或无 Projex 权限，或本仓未接线） | 「云效暂无可同步的需求，可上传本地 PRD 继续」；不阻断本地上传与拆分 | 上传本地文件 / 重试同步 |
| F8 上部 | 同步中超过等待阈值 | 「正在同步云效 PRD（已取到 N 条），可先上传本地文档」 | 等待 / 重新同步 / 先上传 |
| F8 上部 | 未导入任何 PRD（同步为空且无本地文档） | 「云效同步暂无内容，请上传本地文件或从工作区选择 PRD」 | 上传 / 选文件 / 重新同步 |
| F8 上部 | 云效与本地文档描述冲突 | 「2 处描述不一致，请选择以云效为准或以本地文档为准」 | 逐条选择（默认云效） |
| F8 企微云盘入口 | 未接线 | 「暂未接线」（禁用占位） | 无（不产生请求，V5-3） |
| F9 用例生成 | 当前迭代无需求或验收标准 | 「当前迭代暂无可生成用例的需求」 | 去 PRD 工作台拆分 / 切「手工补充」 |
| F9 测试计划保存 | 本仓未接线测试模块 | 「云效支持，本插件未接线，保存为本地草稿」 | 无（不产生请求，V6-6） |
| F5 母版代码库 | 组织内只有 1 个母版库 | 下拉锁定为该库、不可展开，不显示「已选 n 个」 | 直接填代号进下一步（V2-5） |
| F5 母版代码库 | 组织内无母版库 | 「组织内暂无母版代码库。请先在云效 Codeup 建一个母版库（结构固定、子目录可直接改名）」 | 前往云效创建后刷新 |
| F5 项目代号 | 派生的新库名已存在 | 「`RGMS-Mixed` 已存在，请换一个代号」并阻断预检 | 换代号 |
| F6 流水线 | 某端母版流水线非 YAML 化或反查不到 | 该行置灰标「该端不可用（原因）；可取消勾选跳过」；反查不到时允许手选并提示「该流水线不属于母版」 | 取消勾选该端 / 手选一条 YAML 化流水线 |
| F6 流水线 | 后端端口与本项目其他流水线冲突 | 「端口 8080 与已有流水线冲突」并阻断提交 | 改端口或取消后端 |
| F2 评论 | 任务无评论 | 「该任务还没有评论，描述与验收标准会作为首轮输入」 | 补充评论（可选） |
| F2 评论 | 评论读取失败 | 「评论读取失败（原因摘要），不影响开始任务」 | 重试读取 |
| F2 会话 | 尚未开始任务 | 「尚未在工作区开始任务」 | 在工作区开始任务 |
| F2 会话 | 已有会话且进行中 | 「会话进行中，可继续或在宿主里接着聊」 | 继续会话 / 重新开始 / 在云效中打开任务 |
| F2 非可触发状态 | 任务非「未开始/开发中」 | 「仅未开始/开发中的任务可在工作区开始」 | 无（主按钮禁用，评论仍可读） |

#### 4.6.1 项目工作台界面草图（F1 看板 + F2 任务详情）

**草图定位：迭代任务看板（F1，项目工作台）与任务详情（F2，在工作区开始任务）**

> 下图用 Unicode 方框线示意两屏结构：F1 是看板，任务卡上只有「打开任务」一个入口；F2 是独立下钻页，左列任务详情与评论，右列「在工作区开始任务」与工作区会话回显。**插件首页是项目列表，F1 是从首页右栏「迭代任务」入口到达的项目工作台**（§2.9）。**草图仅为示意，不代表最终视觉稿**；按宿主真实尺寸绘制的高保真版见 `docs/界面设计稿-宿主尺寸预览.html` 的屏 4 与屏 6。

```
┌───────────────────────────────────────────────────────────────────────────────────────────────┐
│云效 DevOps 工作台 · 迭代任务看板                                        已连                  │
├───────────────────────────────────────────────────────────────────────────────────────────────┤
│项目[同仁医院互联网服务平台 ▾]   迭代[迭代 24 · 11/10 - 11/28 ▾]   项目详情  PRD 工作台  刷    │
│列名、列序与列数取自云效状态枚举；下列四列仅为示例，禁止硬编码四                               │
├───────────────────────┼───────────────────────┼───────────────────────┼───────────────────────┤
│未开始 · 3             │开发中 · 3             │已提交 · 3             │已完成 ·               │
│#10231 字段映射        │#10228 YAML 替换       │#10221 模板拉取        │#10212 连接校          │
│张工 · 11/18           │陈工 · 11/17 逾期      │李工 · 11/14           │王工 · 11/11 完        │
│[打开任务]             │[打开任务]             │[查看进展]             │[已归档                │
│#10234 列渲染          │#10230 PRD 解析        │#10224 分支创建        │#10215 凭据封          │
│李工 · 11/20           │刘工 · 11/19           │陈工 · 11/15           │刘工 · 11/12 完        │
│[打开任务]             │[打开任务]             │[查看进展]             │[已归档                │
└───────────────────────┴───────────────────────┴───────────────────────┴───────────────────────┘
│列内按预计完成时间升序，空值置末、时区统一；排序字段名须动态解析，不得写                       │
├───────────────────────────────────────────────────────────────────────────────────────────────┤
│迭代 24 概览：任务 14 · 状态列 4（示例） · 已完成 3 · 逾期 1 · 关联需求 4   进度 21%           │
├───────────────────────────────────────────────────────────────────────────────────────────────┤
│                                              [返回项目列表] [项目详情] [打开选中任务          │
└───────────────────────────────────────────────────────────────────────────────────────────────┘
```

```
┌─────────────────────────────────────────────────────┬───────────────────────────────────────┐
│云效 DevOps 工作台 · 任务详情与工作区                                 已连接                 │
├─────────────────────────────────────────────────────┼───────────────────────────────────────┤
│流水线 YAML 替换逻辑 · 编号 10228                    │开发中 · 逾期 2 天                     │
├─────────────────────────────────────────────────────┼───────────────────────────────────────┤
│任务详情                                             │在工作区开始任务                       │
│所属需求：复制母版流水线并按新代号改名               │把任务标题、任务描述与 3 条评论一起作为│
│负责人：陈工 · 预计完成 2026-11-17                   │首轮输入，交给当前项目工作区的头脑风暴 │
│所属迭代：迭代 24 · 11/10 - 11/28                    │技能，先产出方案。                     │
│代码库：HOSP-Mixed · master（子目录 HOSP-*）         │                                       │
│描述：读取参考流水线的 pipelineConfig.flow，         │目标工作区：本机工作目录               │
│替换 sources 中的代码库与分支占位符，生成            │调用技能：头脑风暴 brainstorming       │
│差异预览后再调用创建或更新流水线接口。               │带入内容：标题、描述、评论 3 条、      │
│验收标准：差异预览须人工确认后才提交；失败           │代码库与分支                           │
│时只重跑失败步骤，保留已成功产物。                   │                                       │
│                                                     │[ 在工作区开始任务 ]                   │
│评论 · 3 条                                          │会话建在宿主左侧会话列表，可回到宿主   │
│陈工 11-15 14:20 · 最新                              │里接着聊。                             │
│参考流水线是 YAML 化的，可以直接读 flow；            │                                       │
│目标分支建议固定 master，不用每次确认。              │工作区会话 · 已创建                    │
│李工 11-15 10:05                                     │① 首轮输入已发送                       │
│差异预览请把仓库地址与分支高亮，评审时               │   任务 10228 · 标题、描述与 3 条评论  │
│一眼能看出改动点。                                   │② 头脑风暴技能正在产出方案             │
│王工 11-14 17:40                                     │   流式回显中 · 可取消，可回到宿主继续 │
│可视化编排那条流水线没有 YAML，选参考                │                                       │
│流水线时要直接提示不可选。                           │方案经你确认后才会动代码，这一步由     │
│                                                     │头脑风暴技能自己把关；插件只记录       │
│[ 补充一条评论，发表后写入云效 ] [发表]              │任务与会话的绑定。                     │
├─────────────────────────────────────────────────────┼───────────────────────────────────────┤
│[返回看板] [在云效中打开] [在工作区开始任务]                                                 │
└─────────────────────────────────────────────────────┴───────────────────────────────────────┘
```

**草图阅读要点：动态分栏，以及任务上下文怎么进工作区**

> 左栏四列仅为示意占位，真实列名、列数、列序由工作流状态枚举（官方 `GetWorkitemWorkflow` 的 `statuses[]`，宿主侧逻辑名 `workitem_list_statuses` / `workitem_list_workflow`）生成，禁止硬编码（V7-3、R5、§4.13.3）；列内按「预计完成时间」字段 identifier（须经 `ListWorkItemAllFields` 动态解析，不得写死 `dueDate`）升序、空值置末（R8、§4.13.4 第 3 条）。F2 右侧卡片把「将带入的内容」逐项写在界面上——标题、描述、全部评论、代码库与分支，避免用户点下去才发现 Agent 少了一半上下文；评论随任务实时读取，条数变化会反映在卡片上。开始任务后页面显示的是**会话回显**（首轮已发送 + 技能流式产出），而不是插件自造的「阶段一 / 阶段二」进度条：方案是否可进入实现，由头脑风暴技能在会话内判定并等待用户明确确认（V7-6、R2）。企微云盘等**本仓未接线**入口一律禁用占位，不产生请求（V5-3、R3）。**草图仅为示意，不代表最终视觉稿。**

#### 4.6.2 首页左右分栏草图（F3）

**草图定位：插件首页（左栏项目列表 + 右栏内容区）**

> 左栏宽度固定、内容超高时**只在栏内滚动**，整页不出现纵向滚动条；右栏按状态切换**两种面板**——未选项目时是项目统计，选中项目时是该项目的标题、四个计数与四个入口（含 PRD 工作台）。左栏只有搜索框，**没有新建项目入口**。**草图仅为示意**，高保真版见 `docs/界面设计稿-宿主尺寸预览.html` 屏 2（默认态与选中态均已实现，可直接点击切换）。

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ 云效 DevOps 工作台 · 首页项目列表                                                      │
├──────────────────────────┼─────────────────────────────────────────────────────────────┤
│ DevOps 项目 · 14 个      │ 项目统计 · 组织 tjcEs · 北京同仁医院                        │
│ [搜索框]                 │ 14 项目总数 · 12 已初始化 · 2 待初始化                      │
│                          │ 初始化进度 12 / 14 · 86%   ████████████░░                   │
│ ▸ 同仁医院互联网服务平台 │ 迭代任务分布：未开始 18 · 开发中 15                         │
│   迭代 24 进行中 · 11-16 │ 待测试 9 · 已完成 27（统计口径示意）                        │
│   [已初始化] [12 迭代]   │                                                             │
│   输液记录数据中心       │ 最近访问                                                    │
│   迭代 12 进行中 · 11-15 │ 同仁医院互联网服务平台        11-16                         │
│   [已初始化] [6 迭代]    │ 检验报告互认平台              11-16                         │
│   门诊排班改造           │                                                             │
│   未绑定代码库 · 11-14   │ 从左侧选择项目后，右栏切换为四个入口：                      │
│   未绑定代码库 · 11-14   │ 项目详情 · 迭代需求 / 迭代任务 / 迭代测试 / PRD 工作台      │
│   未绑定代码库 · 11-14   │ （未初始化项目点入口先进入项目初始化，PRD 默认同步云效）    │
│ （条目多时左栏内滚动）   │                                                             │
│ 共 14 个项目 · 最近访问  │                                                             │
└──────────────────────────┴─────────────────────────────────────────────────────────────┘
```

**右栏两态的判定**：

| 右栏状态 | 触发 | 主要内容 | 主操作 |
|----------|------|----------|--------|
| 项目统计（默认） | 进入首页、或点「项目统计」 | 项目总数 / 已初始化 / 待初始化 / 可见迭代四个计数、初始化进度、迭代任务分布、最近访问 | 从左侧选项目 |
| 选中项目 | 点左栏任一项目 | 项目名称与状态、迭代 / 需求 / 任务 / 测试用例四个计数、四个入口卡片（PRD 工作台标注「默认云效同步」）、当前迭代摘要 | 进入项目详情 / 迭代任务 / 迭代测试 / PRD 工作台 |

**四个入口的去向**：项目详情 · 迭代需求 → 屏 5（二级，切换当前迭代）；迭代任务 → 屏 4（看板，继续下钻任务详情与 PRD 工作台）；迭代测试 → 屏 8（用例生成与测试计划）；PRD 工作台 → 屏 7（默认同步云效 PRD，可上传本地文档补充，拆分为五类资产写入当前项目）。未初始化项目点任一入口先进屏 3 完成初始化，再回所选入口（§2.9）。

#### 4.6.3 数据密度与长文本规范

> 看板、列表、树、表格在真实数据下必然遇到「条目多、文本长」。本节是十屏共用的处理规则；界面设计稿已按此实现，并在样例里放入超长文本与超量条目（屏 4「未开始」列 5 条任务、屏 2 超长项目名、屏 7 超长需求名、屏 8 超长用例名、屏 6 超长评论）。

| 场景 | 规则 | 全文如何可达 |
|------|------|--------------|
| 任务标题 / 需求名 / 用例名 | 最多 **2 行截断**（超出省略号），不让长文撑高卡片 | 悬停（title 提示）与详情页 |
| 任务评论 | 最多 **2 行截断** | 悬停与评论区 |
| 列表主标题（项目名、迭代名、计划名） | **单行省略**，行高固定便于扫读 | 悬停；详情页只读展示全文 |
| 次要说明（条目副文本） | 单行省略 | 悬停 |
| 表格说明列（写入位置等） | 允许换行，**不截断** | — |
| 表格内路径 / 流水线名 / 库名 | 等宽字体 + 单行省略 | 悬停 + 可复制 |
| 看板列 | **列内滚动**（列高封顶），列头（列名 + 计数）**吸顶固定**，滚动时不消失 | — |
| 项目列表 / 迭代列表 / 用例列表 / 拆分树 | **区内滚动**，外层不撑高、整页不出滚动条 | — |
| 横向 | **禁止横向滚动**；栅格子项必须留 `min-width: 0` 类约束，防止 nowrap 长文本撑破列宽——否则邻列被挤窄、文本连锁换行，实测会让整屏高出 30~50px | — |
| 计数一致性 | 列头计数与概览数字同源（如「未开始 5 个任务 / 共 14 个任务」），数据变化一并刷新 | — |

密度档位：看板 / 表格 / 列表用**紧凑档**（行距收紧），表单与说明用**默认档**；同一屏内不混用多种密度。空态与加载态仍走 §4.6 状态空态表，不在本节重复。

---

### 4.7 异常处理与回滚

| 类别 | 触发场景 | 界面文案口径 | 回滚 / 处置 |
|------|----------|--------------|-------------|
| 凭据与网络类 | PAT 失效、401/403、限流 429、500、超时 | 统一「个人令牌无效」或「云效服务暂不可用，请稍后重试」；**不回显 PAT 原文** | 保存失败不写凭据；限流按退避重试（沿用既有口径）；界面保持上一稳定态 |
| 目录/文件类 | 工作目录不可用、克隆失败、PRD 解析失败、文件超限 | 「目录不可用 / 文件解析失败（原因摘要，已脱敏）」 | 克隆失败清理本次新建的本地目录；解析失败不生成 `PrdDocument` |
| PRD 同步类 | 云效侧无需求、无 Projex 权限、读取超时、附件下载失败、富文本缺表格图片、云效与本地文档描述冲突 | 「云效暂无可同步的需求，可上传本地 PRD 继续」「已取到 N 条，M 条读取失败（原因摘要）」「2 处描述不一致，请选择以哪侧为准」 | 同步失败**降级不阻断**：本地来源照常可用，页面只标「部分就绪」；不在云效侧产生任何写入；冲突项默认以云效为准并留痕于拆分明细；失败的附件列入重试清单 |
| 云效资源类 | 建组/建库/建流水线/建项目/建工作项失败、覆盖冲突 | 「创建失败 + 分类原因 + 已产出清单」 | **远端一律不自动删、只列清单**；本地自动回收；重跑按同名/幂等键复用；覆盖须卡内确认 |
| 多库初始化类 | 多个母版中某几个库复制或改名失败、部分库已就绪 | 「N 个库中 M 个失败（逐库原因），已成功的库不回滚」 | 仅重跑失败库；成功的库保持就绪；预检清单与实际创建数量不一致时阻断提交 |
| 三端流水线类 | 某端无 YAML 化流水线可用、未选构建目录或目录在库中不存在、后端端口冲突、某端提交失败 | 「该端不可用（原因）；可取消勾选跳过」「该目录在新库 `RGMS-Mixed` 的 master 分支下不存在，请重新选择」「端口 8080 与已有流水线冲突」 | 三端独立提交，某端失败不回滚其他端；目录缺失与端口冲突在预检阶段阻断、不提交；端口不做自动改写；**只改 dev 环境变量，测试与生产环境保持原样** |
| 构建目录类 | 库只有一个工程、派生的目录与端对不上、用户手填了不存在的路径 | 「未扫描到子项目，构建目录留空则按库根构建」「请确认目录名与库结构一致」 | 派生的三个目录与 FR-2 变更结果强校验；手填路径在预检时按本地目录存在性校验；不因目录为空而拒绝建流水线（单工程库是合法情况） |
| 命名冲突类 | 新代号派生的库名在组织内已存在、流水线名已存在、子目录改名前发现本地已有同名目录 | 「`RGMS-Mixed` 已存在，请换一个代号」「流水线 `CME_DEV_Deploy_RGMS-Mobile` 已存在，将改为覆盖或改名」「本地已存在 `RGMS-Admin`，改名会覆盖」 | 库名冲突在预检阶段阻断、不发请求；流水线名冲突由用户选择改名或覆盖（覆盖须确认）；本地同名目录冲突时先备份再改名，不静默覆盖 |
| 能力缺口类 | 表 C 工具未就绪、企微云盘未接线、状态枚举取不到 | 「该能力暂未接线」（禁用占位）或「状态列表获取失败」 | 不发请求；看板显示空态**不回退四列**；缺口登记 §4.12 / §6 |
| 评论类 | 评论读取失败、评论写入失败、评论内容含富文本或附件 | 「评论读取失败（原因摘要），不影响开始任务」「评论未能写入云效，请稍后重试」 | 读取失败不阻断任务详情与开始任务（带入内容标注「评论暂不可用」）；写入失败保留输入内容供重试，不本地另存；富文本按纯文本降级展示并保留换行 |
| 任务会话类 | 工作区不可用、会话创建失败、SSE 中断、任务已不在「未开始/开发中」 | 「工作区不可用」「会话创建失败，可重试」「仅未开始/开发中的任务可在工作区开始」 | 会话创建失败保留已拼好的首轮输入；SSE 中断可经 `Active` / `Attach` 续接；任务状态变化只禁用主按钮，不影响已建会话 |
| 状态枚举类 | 状态枚举返回为空或与看板不一致 | 「状态列表获取失败，暂无法生成看板列」 | 不硬编码、不缓存旧列误导；重试后按最新枚举重建 |

> 回滚总口径：本地自动回收、远端不自动删除只列清单、重跑幂等复用——与既有「新建项目」插件口径一致（`PRD-插件体系与新建项目插件.md` §4.7 相关说明）。

### 4.8 验收标准

> 每条 V 可验证断言，且回溯到一个 FR（核对规则 4）。共 50 条。

| 编号 | 所属 FR | 可验证断言 |
|------|---------|------------|
| V1-1 | FR-1 | 填写组织访问地址后，界面解析出的 organizationId 与所填地址一致；Endpoint 始终为 `openapi-rdc.aliyuncs.com`，不出现可覆盖 Endpoint 的输入框 |
| V1-2 | FR-1 | **PAT 不以明文出现在页面 DOM、日志、SSE 回显与操作记录中**；输入框为密码态、保存后不回填 |
| V1-3 | FR-1 | 校验失败按分类提示且不回显 PAT 原文；保存失败不写入任何凭据 |
| V1-4 | FR-1 | 保存成功后 FR-2~FR-4 步骤解锁，且连接档案含 savedAt |
| V2-1 | FR-2 | 点「预检」只展示母版库、派生的三个名字、落地目录与目标分支，不产生任何云效创建请求 |
| V2-2 | FR-2 | 确认后克隆母版到宿主工作区 → 本地把 `{母版代号}-*` 目录改名为 `{新代号}-*` → 建新库并推送 `master`；`codeup_get_repo` 回读库存在、`codeup_list_branches` 回读含 `master`，且改名后的三个目录均在 |
| V2-3 | FR-2 | 克隆失败时清理本次新建的本地目录，远端仓库不自动删、只列清单 |
| V2-4 | FR-2 | 目标分支默认 `master`（母版默认分支），初始化阶段不切分支；开发分支由 Agent 在工作区切出 |
| V2-5 | FR-2 | **母版库为 1 个时下拉锁定为单项、不可展开、不出现「已选 N 个」；大于 1 时可勾选，收起态显示摘要与「n / 总数」**；每勾一个母版复制出一个新库，预检清单数量与实际复制数量一致 |
| V2-6 | FR-2 | **只填一个「新项目代号」即可派生三类名字**：库名 `{代号}-Mixed`、三个子目录 `{代号}-Admin` / `{代号}-Mobile` / `{代号}-Service`、三条流水线名 `…_Deploy_{代号}-*`；执行前必须展示「母版名 → 新名」派生预览，三段均可单独改写；新库名在组织内已存在时预检阻断并提示换代号；**子目录改名须显式标注为「克隆后本地改名并提交」**，不得让用户以为改个名字自动生效 |
| V2-7 | FR-2 | 克隆 / 本地改名 / 提交 / 推送经**工作区会话由 Agent 执行**（`/agent/chat` + `Confirm`），进度对齐 SSE 事件流并给出逐库回执（改名目录清单 + 推送分支）；会话通路未接线时「开始复制」禁用并明示原因，不得进入执行中态；执行前界面上明示将执行的 git 命令序列 |
| V3-1 | FR-3 | 界面可用「复制流水线」这一用户语言，但**必须同时点明机制**（读母版 YAML → 按新名新建），不得暗示存在一键复制接口；选母版后经 `flow_get_pipeline`（官方 `GetPipeline`）读到 YAML；**母版流水线 `type != PIPELINEASCODE` 时显示「可视化编排不支持以 YAML 复制」并禁用下一步**（R12） |
| V3-2 | FR-3 | 占位符替换至少覆盖代码库 id / 名称 / 构建目录，提交前展示 diff 预览且未确认不发请求 |
| V3-3 | FR-3 | 「新建」走 `flow_create_pipeline`、「覆盖」走 `flow_update_pipeline`；覆盖须卡内确认 |
| V3-4 | FR-3 | 提交后回读结果与「已绑定新库 `{代号}-Mixed` 与新构建目录」结论一致 |
| V3-5 | FR-3 | 流水线固定三行（移动端 / 管理后台 / 后端），每行**带出派生的构建目录与流水线名**并反查到母版那条流水线（库为项目级、三端共用新库）；**取消勾选某端即跳过该端，不读 YAML、不提交，且不影响其他端**；三端结果逐条回读，且**只固定 dev 环境**，测试与生产环境不被改写 |
| V3-6 | FR-3 | 后端启用时参数区**只包含「监听端口」一个输入**并参与 dev 环境变量替换；界面不出现服务名称 / 健康检查 / 副本数等输入项；预检校验端口冲突并阻断；移动端与管理后台不出现该输入 |
| V3-7 | FR-3 | 三端流水线各自绑定**库内构建目录**（如 `RGMS-Mobile` / `RGMS-Admin` / `RGMS-Service`），**取值必须与 FR-2 改名后的实际目录一致**，不一致时阻断；**单工程库可留空、按库根构建**，不阻断流程；手填目录在预检时按本地目录存在性校验，不存在则阻断并提示；提交时 workDir 参与 YAML 替换 |
| V3-8 | FR-3 | **三条流水线名由新代号派生**（`CME_DEV_Deploy_{母版代号}-*` → `CME_DEV_Deploy_RGMS-*`，母版流水线真值见 `LubanAgent.Plugin.NewProject/docs/org-reference.md`），分组前缀与 `_DEV_Deploy_` 段保持母版原样、只替换代号；名字逐条可改，改后提交名以界面值为准；母版流水线反查不到时允许手选，但须提示「该流水线不属于母版，构建目录可能对不上」 |
| V4-1 | FR-4 | 可列出组织内项目并查看单个项目，选择已有项目绑定为当前项目 |
| V4-2 | FR-4 | 设置/更新项目前展示字段预检，确认后才提交；失败不写档案 |
| V4-3 | FR-4 | 设置当前项目后，F3 浏览与 FR-6/FR-7 的默认值同步更新 |
| V4-4 | FR-4 | **插件内不出现任何「新建项目」入口与创建表单**；列表底部说明「新建项目请在云效控制台完成」，界面不因此产生任何创建请求；文案须写成完整、自包含的一句话，不使用易误解为「平台不支持」的短标签 |
| V4-5 | FR-4 | 首页为左右分栏：左栏固定宽度、**只含搜索框与项目列表**（无新建按钮），项目条目多时**只在左栏内部上下滚动**（不撑高整页、不挤压右栏）；右栏未选项目时显示项目统计；选中项目后显示「项目详情 · 迭代需求 / 迭代任务 / 迭代测试 / **PRD 工作台**」**四个入口**，分别到达屏 5、屏 4、屏 8、屏 7；**PRD 工作台入口上须标注「默认云效同步」**；未初始化项目点任一入口先进入项目初始化 |
| V4-6 | FR-4 | 长项目名在列表与面板标题中**单行省略**（悬停看全文）、项目数超出可视区只在左栏内滚动；选中长名项目后右栏不出现滚动、其余入口与计数照常显示（§4.6.3） |
| V5-1 | FR-5 | 进入 PRD 工作台即**自动从云效同步当前项目的 PRD 输入**（需求工作项经 `workitem_list_requirements`，描述与验收标准经 `workitem_get`，附件经 `workitem_list_attachments` / `workitem_get_file`），无需用户先准备文件；「上传本地文件」经 `AttachmentController.Upload`（JSON + Base64）、「宿主工作区」经 `FileController.Read` / `WebFileService.cs` 按已知相对路径读取 |
| V5-2 | FR-5 | 导入后做类型/大小/编码校验并展示文件名与内容预览，失败不生成 `PrdDocument`；云效同步部分失败时状态为「部分就绪」并给出失败条目与原因摘要 |
| V5-3 | FR-5 | **企微云盘入口在未接线时为禁用占位且不产生任何请求** |
| V5-4 | FR-5 | 「本机目录」语义为宿主工作区；**本地上传用标准文件选择控件（用户手势）**，但界面不得出现「按路径读写浏览器本机磁盘」的控件（同源 iframe 读不到浏览器本机 FS，R9） |
| V5-5 | FR-5 | **云效同步是默认来源且不需用户手动触发**：页面显示同步摘要（需求条数、附件份数、最近同步时间）与「重新同步」；云效侧无需求或无权限时**降级为可上传本地 PRD，不阻断拆分**，且不产生任何云效写请求 |
| V5-6 | FR-5 | 本地上传 / 工作区文档以**补充来源**身份与同步结果并列参与拆分，界面明确标注来源类型、不覆盖同步结果；同一需求两侧描述不一致时列入冲突项，未选择时以云效为准 |
| V5-7 | FR-5 | 「宿主工作区」来源在目录列举接口（C31）接线前**只支持按已知相对路径读取**，界面不得出现「浏览工作区文件树」的选择器；本地上传不受此限 |
| V6-1 | FR-6 | 先生成预览清单（不落库），用户可逐条核对/增删/调整 parentId 与预计完成时间 |
| V6-2 | FR-6 | 提交产出**迭代、需求、任务、测试用例、测试计划**五类对象，并附逐条「来源段落 → 目标资产 → 落库回执」明细，来源段落须标明来自云效同步还是本地上传 |
| V6-3 | FR-6 | 状态相关字段取自 `workitem_list_statuses` / `workitem_list_workflow`，不写死 |
| V6-4 | FR-6 | 「部分失败」时只重跑失败项（幂等键），远端结果不自动删 |
| V6-5 | FR-6 | 「迭代测试」屏可按来源（需求 / PRD / 任务 / 手工）生成测试用例，生成结果逐条含编号、标题、优先级与来源需求，重复用例自动合并且保留来源；当前迭代无需求时显示空态而非空列表 |
| V6-6 | FR-6 | 测试计划字段完整（名称、关联迭代、环境、执行人、周期、用例范围）且与「已选用例」条数一致；本仓未接线阶段保存只落本地草稿并显式标注未接线，不发送云效写请求 |
| V6-7 | FR-6 | **拆分目标项目固定为当前云效项目**：页头只读展示项目名、页面内不出现项目选择控件；五类产出均写入该项目（迭代在项目内新建或复用），跨项目写入一律阻断 |
| V6-8 | FR-6 | 拆分树长需求名**单行省略**并保留悬停全文，拆分明细的长来源段落允许换行不截断，均不撑破卡片、不出横向滚动（§4.6.3） |
| V7-1 | FR-7 | 可查看项目列表、项目迭代列表、选择当前迭代、查看迭代需求列表 |
| V7-2 | FR-7 | 看板任务按状态分类展示，列内按预计完成时间升序、空值置末、时区统一 |
| V7-3 | FR-7 | **看板列名 / 列数 / 列序与 `workitem_list_statuses` 返回一致，且不得硬编码四列**；枚举取不到时显示空态而非回退四列 |
| V7-4 | FR-7 | 任务详情展示标题、描述、验收标准与**评论列表**（作者、时间、正文、最新置顶），评论经 `workitem_list_comments` 读取；补充评论经 `workitem_create_comment` 写回云效后列表刷新 |
| V7-5 | FR-7 | 「在工作区开始任务」发出的**首轮输入完整包含**任务标题、任务描述与评论正文；目标工作区等于 FR-2 设定的本机工作目录；会话标题可定位到该任务 |
| V7-6 | FR-7 | 首轮输入要求先加载 brainstorming 技能且以方案模式启动；**用户在该会话中明确确认方案之前，Agent 不写文件、不建骨架、不调实现类技能**（由技能硬门禁保证，「继续」「看着办」不构成批准） |
| V7-7 | FR-7 | 会话经 `/agent/chat` 流式回显、可取消；同一任务再次进入时显示已有会话与最近消息，可「继续会话」或「重新开始」；`TaskSession` 只记绑定与首轮摘要，不另存「已批准」状态 |
| V7-8 | FR-7 | 每个 Frame 元信息含「功能编号 + PRD 章节」，§4.6 清单 9 行与画布一致；每个 V 可回溯到一个 FR |
| V7-9 | FR-7 | 界面上**不出现**「阶段一 / 阶段二 / 批准方案」这类插件自造的阶段控件；方案是否可进入实现只在会话内呈现与确认；仅「未开始/开发中」任务显示「在工作区开始任务」主按钮，其余状态按钮禁用但评论仍可读 |
| V7-10 | FR-7 | 看板列条目超出列高时**列内滚动**、列头列名与计数吸顶固定；任务标题最多 2 行截断且悬停可看全文，长负责人 / 编号行单行省略（§4.6.3） |

### 4.9 凭据（PAT）规范

| 项 | 规范 |
|----|------|
| 存储 | 服务端经新增非特权写入端点（表 C 行 C22，落库实现依赖 C28）写入既有凭据键 `connector:yunxiao:token` / `connector:yunxiao:organizationid`（`AliyunCredentialKeyAliases.cs`），插件不自行持有明文 |
| 落盘形态 | **明文**：C28 拟注册的产品可写实现 `LubanAgentCore/Security/CredentialStore.cs` 把值写进全局 `config.json` 的 `Integrations` 节；`Configuration/AppConfig.cs` 明确「不引入额外加密层」（与 Provider API Key 同基线）。本条不是设计选择，是现状事实，必须写进评审材料 |
| 威胁模型与缓解 | 威胁 = 本机同账户用户可直接读取 `config.json` 拿到 PAT；现有缓解只到「不进 DOM / 日志 / SSE / 操作记录」与「依赖操作系统账户隔离」这一层，**不等于加密**；是否为本机落盘引入加密列入 O4 |
| 传输 | 同源 Cookie 过门禁；`x-yunxiao-token` 由服务端连接器注入，插件 JS 不接触 |
| 界面 | 密码态输入、只收不回显、保存后不回填、不写页面 DOM |
| 日志 | 操作记录、SSE 回显、错误提示、会话消息一律不出现明文 PAT；失败文案统一「个人令牌无效」 |
| 权限 | 不复用 `/system` 特权通道（只认固定 Bearer）；写入走非特权端点（C22）；**但同源 Cookie 对全部非特权控制器有效**，插件前端须按 §3.4 H10 的白名单收敛调用面（R14） |
| 身份 | 插件宿主身份固定默认用户（`PluginHostContext` 的 `UserId`），不假设当前登录用户 |
| 生命周期 | 不托管生成与轮换；用户在云效控制台维护，插件只做校验与安全保存 |
| Endpoint | 固定 `openapi-rdc.aliyuncs.com`（`YunxiaoApiOptions.cs` 的 `Endpoint`），组织访问地址仅用于解析组织 ID |

### 4.10 任务上下文装配与工作区会话设计

| 项 | 设计 |
|----|------|
| 首轮输入 | 由三部分拼成：**任务标题**、**任务描述 + 验收标准**（含所属需求的一句上下文）、**全部评论**（按时间正序，带作者与时间，便于 Agent 理解讨论过程）；末尾附项目 / 迭代 / **复制出的新库**（如 `RGMS-Mixed`）、**三个改名后的子项目目录**与目标分支——让 Agent 知道某段代码在 `RGMS-Mixed/RGMS-Service` 而不是库根，也知道流水线名是 `CME_DEV_Deploy_RGMS-Service`。只读任务与评论，不做摘要压缩，避免丢细节；评论超过 30 条时保留最近 30 条并在末尾注明「更早评论已省略 N 条」 |
| 技能指定 | 首轮输入明确要求「先加载 brainstorming 技能再作答」，让 Agent 走 `UseSkill` 取该技能指令；技能自身的硬门禁负责「方案先于实现」，插件不再实现第二套判定（§2.7） |
| 权限模式 | 以方案模式启动（`PermissionMode = plan`），把「先给方案、后动手」在会话参数上也表达一次；用户可在宿主里自行切换 |
| 会话落点 | 先按工作目录查工作区（`WorkspaceController.ByRootPath`，不存在则 `Create`），再在该工作区下建会话（`SessionController.Create`，`Title` 形如「任务 10228 · 流水线 YAML 替换逻辑」）；同一任务只保留一条有效 `TaskSession` |
| 流式回显 | 经 `/agent/chat` 的 SSE 通道逐条追加；`Active` 探活、`Attach` 续接、`Cancel` 取消；页面刷新后经 `SessionController.Messages` 恢复最近的对话 |
| 工具白名单 | 参考 `AssistantToolPolicy`：只读工具与工作区内读写、框架 git 工具组、白名单云效 MCP 工具；删除类与越界写入一律拒。白名单不用于「门禁」，而是防误操作 |
| 可回宿主 | 会话建在宿主已注册的工作区下，天然出现在宿主左侧会话列表；插件只展示「会话标题 + 最近消息」，不在 iframe 里重造一个完整聊天客户端 |
| 留痕 | `TaskSession` 记录任务、工作区、会话、首轮摘要（含带入评论条数）与最近活跃时间；用于「再次进入该任务时继续」与看板卡上的「已开始」标记 |
| 触发范围 | 仅 `BoardColumn` 判定为「未开始 / 开发中」类的任务显示主操作（由状态枚举元数据推导，不靠字面量）；其余状态下评论仍可读、可发表 |

### 4.11 云效连接器与 MCP 设计

| 项 | 设计 |
|----|------|
| 现状工具面 | 13 个 MCP 工具：Codeup 7 + Flow 6（`Mcp/YunxiaoToolDefinitions.cs`），Agent 侧暴露加前缀 `mcp_yunxiao_`（`Mcp/YunxiaoMCPClient.cs`），逐行见 §4.12 表 A |
| SDK-only 包装 | `flow_get_pipeline` ← `FlowClient.GetPipelineAsync`（FR-3 必需）、`codeup_update_repository` ← `CodeupClient.UpdateRepositoryAsync`，见 §4.12 表 B |
| 需新增工具 | 项目 / 迭代 / 工作项 / 测试 / 看板 / 企微云盘 / 凭据写入，见 §4.12 表 C 共 31 行（M2 阶段）：云效工具 24 个（含可选 C27）、宿主侧 5 项（C22 / C28 / C29 / C30（后续可选）/ C31）、平台开放项 2 项（C3 / C21） |
| 命名规范 | 沿用 `域_动作_对象` 的下划线小写风格，与现有 `codeup_*` / `flow_*` 一致；Agent 暴露统一加 `mcp_yunxiao_` 前缀 |
| 配置与凭据 | 配置节 `LuBanAgent:Connectors:Yunxiao`；凭据键 `connector:yunxiao:token` / `connector:yunxiao:organizationid`；Endpoint 固定、鉴权头 `x-yunxiao-token` |
| 错误映射 | 复用 `YunxiaoErrorMapper.cs`，401/403/429/500 分类映射到 §4.7 文案 |
| 依赖边界 | 插件程序集只依赖 `LuBan.AIAgent` 契约（§3.4 H2），全部云效能力经 MCP / 宿主接口，不直连 SDK |
| 核对要求 | 表 C 的工具名是**宿主侧逻辑工具名**（需求锚点），不是云效官方 API 名；官方名称、REST 路径与官方托管 MCP 的对应关系已在 §4.13 联网核对。实现时优先映射官方托管 MCP / OpenAPI，无需自研同名 REST 客户端；**例外：C3 创建项目在官方 MCP 公开工具清单中缺失，须按 `CreateProject` OpenAPI 单独接线**（§4.13.2）；M2 准入条件为「实机验证托管 MCP 连通性与 PAT 权限边界」 |

### 4.12 能力缺口清单：已具备 vs 需新增（开发实现附录，可跳过）

> 本节是全文承重墙。三张表把每项能力的来源与落点钉死：**表 A 已具备**、**表 B SDK-only 待包装**、**表 C 本仓待接线**。表内行号（A/B/C 前缀）供 §4.3「依赖能力」与 §6 风险回指，均指本表内部行，不是文件行号。核对规则 3：表 B、表 C 每一行都在某个 FR 或 §6 有落点。
>
> **口径**：表 C 的「需新增」指**本仓侧尚未接线**，**不等于云效平台没有该能力**。C1–C20 与 C23 / C24 对应的能力在云效官方 OpenAPI 与官方托管 MCP Server 中均已存在（§4.13）；表 C 的「建议工具名」保留为**宿主侧逻辑工具名**，作为 FR 依赖与验收的需求锚点，实现时映射到官方工具即可，不必自研同名 REST 客户端。一期范围与禁用占位态不因此自动放开（O6）。

**表 A · 已具备（13 个 MCP 工具）**

| 行 | 工具 base 名 | Agent 暴露名 | 域 | 支撑功能 |
|----|--------------|--------------|----|----------|
| A1 | `codeup_search_repos` | `mcp_yunxiao_codeup_search_repos` | Codeup | FR-2 检索仓库 |
| A2 | `codeup_list_template_repos` | `mcp_yunxiao_codeup_list_template_repos` | Codeup | FR-2 列出可作母版的模板库（主路径按 `-Mixed` 命名筛） |
| A3 | `codeup_get_repo` | `mcp_yunxiao_codeup_get_repo` | Codeup | FR-2 取克隆地址 |
| A4 | `codeup_create_repo` | `mcp_yunxiao_codeup_create_repo` | Codeup | FR-2 建库 |
| A5 | `codeup_list_branches` | `mcp_yunxiao_codeup_list_branches` | Codeup | FR-2 确认 master |
| A6 | `codeup_create_group` | `mcp_yunxiao_codeup_create_group` | Codeup | FR-2 建代码组 |
| A7 | `codeup_list_groups` | `mcp_yunxiao_codeup_list_groups` | Codeup | FR-2 列代码组 |
| A8 | `flow_list_pipelines` | `mcp_yunxiao_flow_list_pipelines` | Flow | FR-3 反查母版流水线 |
| A9 | `flow_list_host_groups` | `mcp_yunxiao_flow_list_host_groups` | Flow | FR-3 主机组（只读辅助） |
| A10 | `flow_get_host_group` | `mcp_yunxiao_flow_get_host_group` | Flow | FR-3 主机组明细（只读辅助） |
| A11 | `flow_create_pipeline` | `mcp_yunxiao_flow_create_pipeline` | Flow | FR-3 新建 |
| A12 | `flow_update_pipeline` | `mcp_yunxiao_flow_update_pipeline` | Flow | FR-3 覆盖 |
| A13 | `flow_run_pipeline` | `mcp_yunxiao_flow_run_pipeline` | Flow | 本期不纳入编排（沿用既有助手策略口径） |

**表 A · 宿主已具备通路（补充）**

| 行 | 通路符号 | 真实文件 | 支撑功能 |
|----|----------|----------|----------|
| A14 | `AttachmentController.Upload`（JSON + Base64） | `Controllers/AttachmentController.cs`；落盘到工作区固定子目录 `.luban-agent/temp/uploads/<yyyyMMdd>/` | FR-5 上传本地文件（`<input type="file">` + File API 读出后上传，**不违反**「读不到浏览器本机 FS」——那条约束指按任意路径读写磁盘） |
| A15 | `FileController.Read` | `Controllers/FileController.cs` | FR-5 读文件内容 |
| A16 | `WorkspaceController` | `Controllers/WorkspaceController.cs` | FR-2 / FR-5 工作区元数据（**不含文件列举**，见 C31） |
| A16b | `WebFileService` | `Services/WebFileService.cs` | FR-5 文件服务 |
| A17 | `AgentController`（Send/Chat/Active/Attach/Confirm/Cancel） | `Controllers/AgentController.cs` | FR-2 git 执行通路 / FR-7 Agent 会话 |
| A17b | `WebAgentService` SSE | `Services/WebAgentService.cs` | FR-7 会话流式回显 |
| A18 | `BrainstormingSkill`（Id = `brainstorming`，含「硬门禁」） | `luban-framework/LuBan.AIAgent/Skills/BuiltIn/BrainstormingSkill.cs` | FR-7 首轮指定技能 |
| A19 | `HostAssistantBridge.RunAsync`（`IAgentHost.RunStreamingAsync` + 白名单） | `LubanAgent.Plugin.NewProject/Services/HostAssistantBridge.cs` | FR-7 会话驱动与工具白名单参考 |
| A20 | `StepPrompts` 的 7 个 Build*Prompt | `LubanAgent.Plugin.NewProject/Services/StepPrompts.cs` | FR-7 提示词参考 |
| A21 | `AssistantToolPolicy` | `LubanAgent.Plugin.NewProject/Services/AssistantToolPolicy.cs` | FR-7 工具白名单参考 |

**表 B · SDK-only 待包装为 MCP 工具**

| 行 | 建议工具名 | 现有 SDK 方法 | 域 | 支撑功能 | 优先级 |
|----|------------|---------------|----|----------|--------|
| B1 | `flow_get_pipeline` | `FlowClient.GetPipelineAsync`（读流水线详情 YAML） | Flow | FR-3 参考 YAML 读取（**FR-3 必需**），落点 R1 | 高 |
| B2 | `codeup_update_repository` | `CodeupClient.UpdateRepositoryAsync` | Codeup | FR-2 改仓库属性 | 中 |

**表 C · 本仓待接线（平台侧已有，官方对应见 §4.13）**

| 行 | 建议工具名 | 关键入参 | 云效 OpenAPI 域 | 支撑功能 | 优先级 |
|----|------------|----------|-----------------|----------|--------|
| C1 | `projex_list_projects` | organizationId | 研发项目 Projex | FR-4 / FR-7 项目列表 | 高 |
| C2 | `projex_get_project` | organizationId, projectId | 研发项目 Projex | FR-4 项目详情 | 高 |
| C3 | `projex_create_project` | organizationId, name, customCode, scope, templateId（模板列表由 `GetProjectTemplateList` 提供） | 研发项目 Projex | **FR-4 不接线**（插件不提供创建入口，仅备后续版本，见 O8） | 后续 |
| C4 | `projex_update_project` | organizationId, projectId | 研发项目 Projex | FR-4 设置/改名 | 中 |
| C5 | `projex_list_sprints` | organizationId, projectId | 迭代 Sprint | FR-6 / FR-7 迭代列表 | 高 |
| C6 | `projex_get_sprint` | organizationId, sprintId | 迭代 Sprint | FR-7 当前迭代 | 高 |
| C7 | `projex_create_sprint` | organizationId, projectId, name | 迭代 Sprint | FR-6 新建迭代 | 高 |
| C8 | `workitem_list_statuses` | organizationId, projectId, workItemType | 工作项状态枚举 | FR-6 / FR-7 **动态分栏前提**（落点 R5、V7-3） | 高 |
| C9 | `workitem_list_workflow` | organizationId, projectId | 工作项工作流 | FR-6 / FR-7 列序与可触发判定（落点 R5） | 高 |
| C10 | `workitem_list_requirements` | organizationId, sprintId | 工作项·需求 | FR-5 / FR-6 / FR-7 需求列表 | 高 |
| C11 | `workitem_create_requirement` | organizationId, sprintId, title | 工作项·需求 | FR-6 创建需求 | 高 |
| C12 | `workitem_list_tasks` | organizationId, sprintId | 工作项·任务 | FR-6 / FR-7 任务列表 | 高 |
| C13 | `workitem_create_task` | organizationId, sprintId, parentId, dueDate | 工作项·任务 | FR-6 创建任务 | 高 |
| C14 | `workitem_update_task` | organizationId, workItemId | 工作项·任务 | FR-6 / FR-7 更新状态与字段（落点 R8） | 高 |
| C15 | `workitem_list_bugs` | organizationId, sprintId | 工作项·缺陷 | FR-7 缺陷浏览 | 中 |
| C16 | `workitem_create_relation` | organizationId, sourceId, targetId | 工作项关系 | FR-6 需求-任务-用例关联 | 中 |
| C17 | `board_list_fields` | organizationId, projectId | **无独立看板 API**；列序取自官方 `GetWorkitemWorkflow` 的 `statuses[]` | FR-7 列序元数据（落点 R5、§4.13.3） | 中 |
| C18 | `board_list_workitems_by_sprint` | organizationId, sprintId, statusId | **无独立看板 API**；由官方 `ListWorkitems` 的 `conditions` / `groupCondition` / `orderBy` 组合 | FR-7 迭代任务看板（落点 R5、R8、§4.13.3） | 高 |
| C19 | `testcase_create` | organizationId, projectId, title | 测试用例 | FR-6 测试用例 | 中 |
| C20 | `testplan_create` | organizationId, sprintId, caseIds | 测试计划 | FR-6 测试计划 | 中 |
| C21 | `WecomDriveConnector`（建议 `wedrive_auth` / `wedrive_list_files` / `wedrive_read_file`） | 云盘鉴权与文件标识 | 企微云盘 | FR-5 企微云盘读取（一期禁用占位，落点 R3） | P2 / 开放 |
| C22 | WebHost 非特权凭据写入端点 | organizationAddress, organizationId, pat | WebHost 本地接口 | FR-1 保存（落点 R6、V1-2、§3.6 契约）；落库依赖 C28 | 高 |
| C23 | `workitem_list_comments` | organizationId, workItemId | 工作项评论·读取 | FR-7 任务详情评论（落点 V7-4） | 高 |
| C24 | `workitem_create_comment` | organizationId, workItemId, content, formatType, parentId | 工作项评论·写入 | FR-7 补充评论（落点 §4.7 评论类） | 中 |
| C25 | `workitem_get` | organizationId, workItemId | 工作项·详情（官方 `GetWorkItemInfo`） | **FR-5 云效同步**：读需求描述（富文本）与验收标准等自定义字段（落点 V5-1） | 高 |
| C26 | `workitem_list_attachments` + `workitem_get_file` | organizationId, workItemId / fileId | 工作项附件（官方 `ListWorkitemAttachments`、`GetWorkitemFile`） | **FR-5 云效同步**：把 PRD 附件拉成本地文本参与拆分（落点 V5-1、R13） | 高 |
| C27 | `codeup_list_files` | organizationId, repositoryId, ref, type=DIRECT | 代码库文件树（官方 `ListFiles` / `ListRepositoryTree`） | FR-3 构建目录候选（**可选、M5**）：一期用克隆后的本地目录代替，接线后可不依赖克隆预览目录（落点 §4.13.4 第 10 条） | 低 |
| C28 | WebHost 可写 `ICredentialStore` 注册 | 存储实现 + 落盘位置（全局 `config.json` 的 `Integrations`） | WebHost / Core 依赖注入 | **FR-1 保存的落库前提**（落点 §4.9、R6）：框架 `TryAddSingleton<ICredentialStore>` 落只读实现，`ConfigurationCredentialStore.SetAsync` 抛 `NotSupportedException`；可写实现仅桌面端 `App.axaml.cs` 替换 | 高 |
| C29 | 非特权凭据校验入口 | organizationId / pat（只读探活） | WebHost 本地接口（探活逻辑可复用 `IConnectorManagementService.TestAsync` 的底层调用；该服务按已配置连接器测试，故入口内需以临时选项只读探活、不落任何配置） | **FR-1 第 3 步「校验」**（落点 §3.6、V1-3）：原表无对应入口 | 高 |
| C30 | 宿主 git 执行端点（`git_execute`） | workspaceId、动作（clone / commit / push / status / diff）、参数 | WebHost 本地接口 | **后续可选（一期不做）**：一期 git 动作经工作区会话由 Agent 执行（O10 / R15）；本行仅留档，M2 不接 | 低 |
| C31 | 工作区文件 / 目录列举 | workspaceId、相对路径 | WebHost 本地接口（**平台无替代**） | FR-5「宿主工作区选文件」：接线前该来源只支持按已知相对路径读取（V5-7），落点 M5 | 低 |

> **核对状态**：表 C 的**云效工具名**是宿主侧逻辑工具名，不是云效官方 API 名，也不代表真实入参；其背后的平台能力已于 2026-11-16 逐条核对官方文档，官方名称与 REST 路径见 §4.13。C1–C20、C23–C27 应优先由官方托管 MCP / OpenAPI 承载；C21（企微微盘）平台侧有接口但需独立企微应用鉴权。**C22 / C28 / C29（写入端点、可写凭据存储注册、校验入口）与 C30 / C31（git 执行端点、工作区文件列举）确为本仓需新增、无平台替代**，其中 C28 是既有依赖注入缺口而非新接口，C29 可直接复用 `ConnectorManagementService.TestAsync`，C30 的 git 能力框架层已有（`GitToolPlugin`）但没有面向插件的 REST 通路（R15 / O10），C31 用于补齐「浏览工作区文件」的目录列举（R16）。核对仅为阅读官方文档，**未实机调用、未运行构建或测试**；M2 准入条件为「实机验证托管 MCP 连通性 + PAT 权限边界」。

### 4.13 云效平台能力联网核对（2026-11-16）

> 本节记录联网核对的结论与出处，用于把 §4.12 表 C 从「按功能推导」升级为「有官方依据」。**核对方式：阅读阿里云帮助中心 / OpenAPI 门户 / 官方 GitHub README / 企业微信开发者中心公开文档，未实机调用任何接口，未在宿主环境实测官方 MCP 连通性。**

#### 4.13.1 接入通道（与 §3.8 连接器设计对齐）

| 项 | 官方结论 | 出处 |
|----|----------|------|
| 中心版接入点 | `openapi-rdc.aliyuncs.com` 为**统一服务接入点**；Region 版用组织专属实例域名 | 云效「服务接入点（domain）」`https://help.aliyun.com/zh/yunxiao/developer-reference/service-access-point-domain` |
| 双认证体系 | 老版 `devops/2021-06-25` 走 `devops.cn-hangzhou.aliyuncs.com` + 阿里云 AK 签名；新版 `oapi/v1` 走 `openapi-rdc.aliyuncs.com` + `x-yunxiao-token` PAT，无需签名 | OpenAPI 产品目录 `https://next.api.aliyun.com/product/devops`；CLI 中心 `https://api.alibabacloud.com/api-tools/cli/devops/2021-06-25`；第三方实践交叉印证 `https://www.clicksun.cn/bbs.asp?id=40714` |
| organizationId | **仅中心版**路径必填（如 `/oapi/v1/projex/organizations/{organizationId}/projects`）；Region 版路径不带 | 各 API 文档「适用版本 / 中心版、Region版」段落 |
| 仓库既有配置 | `YunxiaoApiOptions.cs` 的 `Endpoint`、`x-yunxiao-token` 头、`connector:yunxiao:token` / `connector:yunxiao:organizationid` 凭据键 → 与官方中心版 PAT 通道**完全吻合**，FR-1「组织访问地址仅用于解析 organizationId、不覆盖 Endpoint」的结论成立 | `LubanAgentCore/Connectors/AliyunDevOps/YunxiaoApiOptions.cs`、`AliyunCredentialKeyAliases.cs`；`LubanAgentCodex/Security/YunxiaoSettingsService.cs` |

#### 4.13.2 官方托管 MCP Server（本次核对最重要的发现）

阿里云官方仓库 `alibabacloud-devops-mcp-server` 提供 **150+ 原子能力**，分 7 个 toolsets：`organization-management`、`code-management`、`project-management`、`pipeline-management`、`packages-management`、`application-delivery`、`test-management`。中心版**官方托管端点**为 `https://openapi-rdc.aliyuncs.com/ai/mcp`，Streamable HTTP（stateless），认证支持 `Authorization: Bearer <TOKEN>` 或 `X-Yunxiao-Token: <TOKEN>`；可用 `?toolsets=project-management,test-management` 或 `X-Devops-Toolsets` 头裁剪工具集以控制上下文。出处：`https://github.com/aliyun/alibabacloud-devops-mcp-server`（README「Official Hosted MCP Service」「Toolsets」「Prerequisites」段）。

**关键边界**：README 的 `Project Management Tools` 清单中，项目本体工具只有 `get_project`（项目详情）与 `search_projects`（项目搜索）；其余 `create_sprint` / `create_version` / `create_work_item` 创建的是**项目内对象**，不是项目本身。**公开工具清单未列出 `create_project` / `CreateProject`**。因此「接入官方托管 MCP」可解决项目搜索、迭代、工作项、状态、测试等接线，**但不能直接解决 FR-4 的创建项目**；C3 仍需按官方 OpenAPI 单独接线，或在 M2 以运行时 `tools/list` 做最终核验（R10、O6、O8）。

**宿主侧可直接接入，无需新增厂商连接器代码**：

| 宿主能力 | 可核符号 | 结论 |
|----------|----------|------|
| Streamable HTTP 外部 MCP | `luban-framework/LuBan.AIAgent/Configuration/McpServerConfig.cs` 的 `Transport = "http"`、`Url`、`Headers` | 支持 |
| Header 内联凭据引用 | `luban-framework/LuBan.AIAgent/MCP/CredentialValueResolver.cs` 的 `ResolveAsync`，README 明确示例 `Authorization: Bearer $credential:connector:my:token` | 支持，且凭据键 `connector:yunxiao:token` 已存在 |
| 框架/产品边界 | `AGENTS.md`「框架 / 产品边界」：框架只保留连接器抽象与通用外部 MCP（`url`/`headers`/`environment` + `$credential:`），不含厂商实现 | 官方 MCP 属通用外部 MCP 配置，合规 |

据此，一条可行的配置形态为：`{ "name": "yunxiao-official", "transport": "http", "url": "https://openapi-rdc.aliyuncs.com/ai/mcp", "headers": { "Authorization": "Bearer $credential:connector:yunxiao:token" }, "enabled": true }`。**该形态仅为依据两侧文档推导的候选配置，未在宿主实机验证**，且官方 README 要求 PAT 授予组织管理/项目协作/代码管理/流水线/制品/应用交付/测试管理**全部 API 的读写权限**，与 §2.4「最小权限」原则冲突（R11 / O6）。

#### 4.13.3 表 C 行 ↔ 官方能力映射（中心版路径省略 `https://openapi-rdc.aliyuncs.com` 前缀）

| 表 C 行 | 官方 API / 能力 | 路径或名称 | 出处 |
|---------|-----------------|------------|------|
| C1 / C2 | 项目搜索、项目详情 | `POST /oapi/v1/projex/organizations/{organizationId}/projects:search`；`GetProjectInfo` | `https://help.aliyun.com/knowledge_detail/460498.html`；CLI 项目搜索文档（第三方实现，交叉印证）`https://github.com/AndersonBY/alibabacloud-devops-cli/blob/850ff84f26309880634a47d362708eb0781787f0/docs/usage/23-org.md` |
| C3 / C4 | 创建/更新项目 | 中心版 `POST /oapi/v1/projex/organizations/{organizationId}/projects`、Region 版 `POST /oapi/v1/projex/projects`；必填 `name`、`customCode`（4–6 位大写字母、组织内唯一）、`scope`（`public`/`private`）、`templateId`（先调 `GetProjectTemplateList`）；权限「项目协作 / 项目 / 读写」；响应仅返回新项目 `id`。旧版另有 `POST /organization/{organizationId}/projects/createProject`（参数 `templateIdentifier`）。`UpdateProjectField` | `https://help.aliyun.com/en/yunxiao/developer-reference/projex/`；`https://help.aliyun.com/practice_detail/460490`；`https://www.alibabacloud.com/help/en/yunxiao/developer-reference/createproject` |
| C5–C7 | 迭代列表/详情/创建 | `GET /oapi/v1/projex/organizations/{organizationId}/projects/{id}/sprints`（状态 `TODO/DOING/ARCHIVED`）；`CreateSprint` | `https://www.alibabacloud.com/help/zh/yunxiao/developer-reference/listsprints`；`https://helpcdn.aliyun.com/document_detail/460492.html` |
| C8 / C9 | **状态枚举与列序真源** | `GetWorkitemWorkflow`：`GET /oapi/v1/projex/.../workitemTypes/{id}/workflows`，返回 `statuses[]`（`id` / `displayName` / `name` / `nameEn`）与 `defaultStatusId`；老版另有 `ListWorkItemWorkFlowStatus` | `https://www.alibabacloud.com/help/zh/yunxiao/developer-reference/getworkitemworkflow-get-a-list-of-the-status-of-the-work-item` |
| C10–C14、C16 | 工作项增删查改、父子/关联 | `POST /oapi/v1/projex/workitems`（`CreateWorkitem`，含 `parentId`、`sprint`、`workitemTypeId`）；`CreateWorkitemV2`（`category=Req/Bug/Task/Topic`、`sprintIdentifier`、`parentIdentifier`）；`ListWorkitems`（`conditions` 按状态过滤、`orderBy` 排序）；`UpdateWorkItem`；`GetWorkitemRelations` | `https://www.alibabacloud.com/help/doc-detail/2870395.html`；`https://help.aliyun.com/zh/yunxiao/developer-reference/api-devops-2021-06-25-createworkitemv2`；`https://help.aliyun.com/zh/document_detail/392298.html` |
| C17 / C18 | 看板列与迭代任务 | **无独立「看板」API**；由 `GetWorkitemWorkflow` 取状态列 + `ListWorkitems`（`conditions`/`groupCondition`/`orderBy`）按迭代与状态分组即可，无需 `board_*` 新工具 | 同上两处；`https://api.aliyun.com/document/devops/2021-06-25/UpdateWorkItem` 的 API 目录 |
| C19 / C20 | 测试用例、测试计划 | `CreateTestCase`（`POST /organization/{organizationId}/testhub/testcase`）；`CreateTestPlan`（中心版 `POST /oapi/v1/testhub/organizations/{organizationId}/testPlans`）；另有 `ListTestPlan`、`AddTestPlanTestcases`、`GetTestResultList` 等 | `https://helpcdn.aliyun.com/document_detail/2391129.html`；`https://www.alibabacloud.com/help/zh/yunxiao/developer-reference/createtestplan-create-a-test-plan`；`https://help.aliyun.com/zh/document_detail/2982224.html` |
| C21 | 企微微盘 | `POST /cgi-bin/wedrive/file_list`、`file_info`、`file_download`（普通文件返回 `download_url` + cookie，有效期 2 小时）；**仅支持普通文件，微文档/表格/收集表/幻灯片不可下载**；需企业微信自建应用 secret 且具备「微盘」权限 | `https://developer.work.weixin.qq.com/document/path/97886`、`https://developer.work.weixin.qq.com/document/path/97881`；文件列表 `https://work.weixin.qq.com/api/doc/90001/90144/93657` |
| C22 / C28 / C29 | WebHost 非特权凭据写入端点、可写凭据存储注册、校验入口 | **平台无替代**，确为本仓需新增（FR-1 前置，R6）：C22 为端点、C28 为依赖注入缺口（默认实现只读）、C29 复用 `ConnectorManagementService.TestAsync` 的探活调用（须以临时选项只读执行） | 本仓 `Controllers/` 与 `Services/` 无对应入口；`LuBanAgentExtensions.cs` 的 `TryAddSingleton<ICredentialStore>` |
| C23 | 工作项评论列表 | `GET /oapi/v1/projex/organizations/{organizationId}/workitems/{id}/comments`（Region 版无 organizations 段）；返回 `content`、`contentFormat`（`RICHTEXT` / `MARKDOWN`）、`gmtCreate`、`gmtModified`、`id`、`parentId`、`top`、`topTime`、`user.id` / `user.name`；权限「项目协作 / 工作项评论 / 只读」 | `https://help.aliyun.com/zh/yunxiao/developer-reference/work-item-comments/`、`https://www.alibabacloud.com/help/zh/yunxiao/developer-reference/listworkitemcomments` |
| C24 | 创建工作项评论 | `POST /oapi/v1/projex/organizations/{organizationId}/workitems/{id}/comments`；body `content`（必填）、`formatType`（`RICHTEXT` / `MARKDOWN`，默认 `RICHTEXT`）、`parentId`、`operatorId`（个人 token 时无效）；权限「项目协作 / 工作项评论 / 读写」 | `https://help.aliyun.com/practice_detail/461180`、`https://www.alibabacloud.com/help/tc/yunxiao/developer-reference/createworkitemcomment` |
| C25 | 工作项详情（PRD 正文来源） | `GetWorkItemInfo`：返回需求标题、描述（`RICHTEXT` 富文本）、自定义字段（含验收标准）与所属迭代；与 `ListWorkitems` 配合按项目 / 迭代筛选。具体请求路径未逐字核对，实现时以官方文档为准 | `https://api.aliyun.com/product/devops`（工作项分组：`ListWorkitems`、`GetWorkItemInfo`）、`https://troubleshooting.api.aliyun.com/document/devops/2021-06-25/ListWorkitems` |
| C26 | 工作项附件（PRD 文件来源） | `ListWorkitemAttachments`（附件列表，返回文件 id / 名称 / 大小）与 `GetWorkitemFile`（按文件 id 取文件）。**附件才是能拿到完整 PRD 正文的通道**；两者路径同样以官方文档为准 | `https://api.aliyun.com/product/devops`（工作项分组：附件列表、文件信息两条接口） |

#### 4.13.4 平台能力要点（核对结论）

1. **FR-3「读母版流水线 YAML」有官方依据**：`GetPipeline`（中心版 `GET /oapi/v1/flow/organizations/{organizationId}/pipelines/{pipelineId}`）返回 `pipelineConfig.flow`（YAML 字符串）、`pipelineConfig.sources[].data.repo` / `.branch`、`type`。`type = PIPELINEASCODE` 表示 YAML 化流水线，`null` 表示 UI 编排 —— **UI 编排流水线无 YAML 可读，FR-3 必须限定母版流水线为 YAML 化**（R12）。官方确无复制接口，产品文宣明确推荐「复制 YAML + `CreatePipeline` / `UpdatePipeline`（后者仅支持 YAML 化流水线）」，与 FR-3 的重定义方向一致。出处：`https://help.aliyun.com/zh/yunxiao/developer-reference/getpipeline-get-pipeline-details`、`https://developer.aliyun.com/article/1332100`。
2. **「模板建库 / 建分支」有官方直接支持**：`ListTemplateRepositories`（`GET /oapi/v1/codeup/organizations/{orgId}/repositories/templates`，`templateType` 1 自定义 / 2 系统预置）、`CreateRepository` 的 `templateProject`（`templateProjectId` / `templateType` / `syncAllBranches`）、`CreateBranch`（`branch` + `ref`）。本组织的实际形态是「**母版库整库复制**」而不是「选框架模板建空库」——`ListTemplateRepositories` 仍可用于列出可作模板的库，但真正的复制动作与子目录改名都没有官方接口，须走本地 clone + 改名 + 推送（第 11 条）；分支以母版默认的 `master` 为准。出处：`https://www.alibabacloud.com/help/doc-detail/2846558.htm`、`https://help.aliyun.com/zh/yunxiao/developer-reference/codeup/`、`https://help.aliyun.com/document_detail/2846571.html`。
3. **「预计完成时间」不是固定系统字段**：云效把「截止日期 / 期望日期 / 预计工时」等作为**项目可配置自定义属性**，字段 identifier 随项目配置而变，需经 `ListWorkItemAllFields` 动态取。R8 / O2 的「字段名不明」判断成立且应升级为硬约束：排序前必须按字段列表解析 identifier，不得写死 `dueDate`。
4. **F1 动态列的真源是 `GetWorkitemWorkflow` 的 `statuses[]`**，不是自造的 `workitem_list_statuses`；后者作为宿主侧逻辑工具名保留即可。V7-3 / V6-3 的断言不变。
5. **企微云盘不是「平台零能力」**：平台侧三接口齐备，一期禁用的真实理由是「需独立的企微应用鉴权体系 + 微文档不可下载 + 需要文件选择交互」，而非接口不存在。R3 / O3 据此定义。
6. **「创建新项目」与平台能力无关，插件干脆不提供入口**：官方 `CreateProject` 明确存在且可调用（中心版 / Region 版路径、必填字段、权限见 §4.13.3 C3/C4 行），但官方托管 MCP 的 `project-management` 公开工具清单**没有** `create_project`，接入 MCP 不能替代 C3；创建项目还要求 `GetProjectTemplateList` 前置、项目编号查重与项目协作读写权限，属组织级不可自动回滚的写操作。禁用占位仍要用户理解一个不存在的操作，不如把创建留在云效控制台，插件只负责「选一个项目继续往下做」。R10、O8 据此定义。
7. **任务评论有官方读写接口**：`ListWorkitemComments`（只读）与 `CreateWorkitemComment`（读写）在中心版 / Region 版均适用，路径同为 `/oapi/v1/projex/…/workitems/{id}/comments`，返回体含作者、时间、`parentId`、置顶标记与内容格式。因此「任务详情含评论」与「补充评论写回云效」都能由官方接口承载，不需要本地另存一套评论；**内容格式为 `RICHTEXT` 时是富文本**，界面前端按纯文本降级展示并保留换行，回复引用关系用 `parentId` 表达。出处：`https://help.aliyun.com/zh/yunxiao/developer-reference/work-item-comments/`、`https://help.aliyun.com/practice_detail/461180`。
8. **Codeup 仓库模板的官方形态：按工程结构划分，不按端**：官方「仓库模板」文档给出的**系统模板**为 Spring、NodeJs Express、Android、iOS、Go Micro、Ruby on Rails、.NET Core、React 等框架级模板；组织可把自有库**设为模板库**（仅库管理员可开启；模板库须 < 100 MB；对该库有读权限的成员才能用），新建时在「自定义模板」页签选择，**默认只导入默认分支，可勾选导入所有分支**；`CreateRepository` 的 `templateProject` 对象含 `templateProjectId` / `templateType`（1 自定义、2 系统预置）/ `syncAllBranches`（第 2 条已引 API 文档）。**模板对应的是「一套工程结构」，与「端」无关**——一个代码库可以同时容纳后端、管理后台、移动端等多个项目。另有两处接口细节：`ListTemplateRepositories` **分页**（page / perPage，perPage 上限 100，系统预置模板的 `path` 返回 null），因此「模板总数」必须翻页取全量或按 `x-total` 判定，不能只看首页；`CreateRepository` 的 `visibility` 取值为 `private` / `internal`（组织内公开），响应回带 `defaultBranch`（模板库示例为 master）。出处：`https://www.alibabacloud.com/help/zh/yunxiao/user-guide/warehouse-template`、`https://help.aliyun.com/en/yunxiao/user-guide/warehouse-template`、`https://www.alibabacloud.com/help/doc-detail/2846145.htm`、`https://www.alibabacloud.com/help/doc-detail/2846558.htm`。
9. **云效 Flow 的「环境」官方口径**：流水线对象本身**没有**「开发 / 测试 / 生产」内建属性。官方实现多环境差异的路径有三条：①为不同阶段**各建一条流水线**，并在各流水线设置不同自定义变量（如 `DEPLOY_ENV=dev` / `test` / `prod`、`CONFIG_PATH=/config/dev`）；②在流水线 YAML 的 Pipeline 级 `variables` / `variableGroups` 定义变量（支持 String / Number / Boolean / Object，运行时输入优先）；③用 **AppStack 应用级环境**（应用模板预置测试 / 预发 / 生产三条流水线与多套环境，环境关联变量组）。因此「流水线有三个环境」不是官方模型；插件「只配 dev」的准确含义是**只把各端流水线的部署环境变量与部署目标固定为 dev**，测试与生产沿用参考流水线既有编排，插件不新建环境、不改测试与生产。出处：`https://intl.aliyun.com/help/zh/doc-detail/153688.htm`、`https://www.alibabacloud.com/help/zh/doc-detail/2253213.html`、`https://developer.aliyun.com/article/1682025`。
10. **「一库多端」在云效里没有独立对象，只能靠构建目录区分**：Codeup 的模型是「组织 → 代码组 → 代码库 → 文件」，**没有「子项目 / 子应用」这一层**。组织真实库 `TJCES-Mixed` 的形态就是根目录并列 `TJCES-Admin`、`TJCES-Mobile`、`TJCES-Service` 三个子目录（母版默认分支按 `master` 处理，副本同起），因此「三端各一条流水线」在云效侧的落点只能是**同库不同构建目录**，不存在「三端三个库」的必然性。取目录清单有两条路：①官方 `ListFiles`（Region 版 `GET /oapi/v1/codeup/repositories/{repositoryId}/files/tree`，`type` 默认 `DIRECT`，可传 `ref` 指定分支）与 2021-06-25 版 `ListRepositoryTree`（`GET /repository/{repositoryId}/files/tree`，`type` 可取 `DIRECT` / `RECURSIVE` / `FLATTEN`），可直接读远端一级目录；②**本仓当前 13 个工具（Codeup 7 + Flow 6）里没有任何读文件树的能力**，`codeup_get_repo` 只回克隆地址与默认分支，所以构建目录候选一期取自**克隆后本地扫描**（零新接口、离线可用），官方接口留作 M5 优化。出处：`https://www.alibabacloud.com/help/zh/yunxiao/developer-reference/listfiles`、`https://help.aliyun.com/zh/yunxiao/developer-reference/api-devops-2021-06-25-listrepositorytree`。
11. **「复制代码库」与「改目录名」在官方 API 里都不存在**：云效 Codeup 的仓库域 OpenAPI（`api.aliyun.com/product/devops` 的仓库分组）只有 `CreateRepository`（创建或者导入代码库）、`DeleteRepository`、`UpdateRepository`、`TransferRepository`、`TriggerRepositoryMirrorSync` 等，**没有 Fork / 复制 / 派生仓库接口**；文件域也没有「重命名目录」接口（只有读文件树 `ListFiles` / `ListRepositoryTree`）。因此「从 `TJCES-Mixed` 复制出 `RGMS-Mixed` 并把 `TJCES-*` 改成 `RGMS-*`」只能落地为两条路径：
   - **路径 A（一期主路径）**：宿主 clone 母版到工作区 → 在本地把 `TJCES-Admin` / `TJCES-Mobile` / `TJCES-Service` 批量改名为 `RGMS-*`（同时按需替换文本里的旧代号）→ `CreateRepository` 建空库或经 `codeup_create_repo` 建库 → 把改动后的代码推送到新库的 `master`。优点是**名字真正改掉**，缺点是首次推送是全量、时间与母版体积成正比。
   - **路径 B（降级 / 快速路径）**：把母版库设为**自定义模板库**（库管理员开启、库 < 100 MB、有读权限者可用），`CreateRepository` 带 `templateProject{templateProjectId, templateType=1, syncAllBranches}`，由服务端复制代码。优点是服务端复制快，缺点是**子目录名仍是母版的**，仍要补一次本地改名与推送。
   两条路径的**代码库名**都在 `CreateRepository` 时直接给新名（`repoName`）、**流水线名**都在 `flow_create_pipeline` 时给新名（`name`），这两项无阻碍；只有「子目录名」是硬约束，必须在界面与档案里显式写成「克隆后本地改名」，不能让用户以为改个字段就自动生效。出处：`https://api.aliyun.com/product/devops`（仓库 / 文件分组接口清单）、`https://www.alibabacloud.com/help/zh/yunxiao/user-guide/warehouse-template`（自定义模板库的门槛与导入分支行为）。
12. **「云效同步 PRD」能落到哪些接口：工作项 + 工作项附件可以，项目知识库文档不行**：PRD 在云效里通常以三种形态存在——①需求类工作项的**描述（富文本）与验收标准等自定义字段**；②挂在工作项上的**附件**（Word / PDF / Markdown）；③项目知识库里的**文档页**。前两种有官方接口：工作项域（`https://api.aliyun.com/product/devops` 的工作项分组）提供 `ListWorkitems`（按项目 / 迭代 / 状态组合筛选，官方标注 20 次/秒限流）、`GetWorkItemInfo`（取描述与字段）、`ListWorkitemAttachments`（附件列表）、`GetWorkitemFile`（取文件）、`GetWorkitemCommentList`（评论），可以组装出「同步本项目的 PRD 输入」这条链路；`ListWorkitems` 与工作流状态接口的官方页分别为 `https://troubleshooting.api.aliyun.com/document/devops/2021-06-25/ListWorkitems`、`https://error-center.aliyun.com/document/devops/2021-06-25/ListWorkItemWorkFlowStatus`。第三种**没有对应的项目文档读取接口**：官方 OpenAPI 里的「知识库」是**通义灵码（lingma）知识库**（`GET /oapi/v1/lingma/organizations/{orgId}/knowledgeBases` 与 `/knowledgeBases/{id}/files`，见 `https://help.aliyun.com/document_detail/2840397.html`），面向问答检索，不是 Projex 项目文档，不能当 PRD 正文来源。因此 FR-5 的「云效同步」边界明确为**工作项 + 工作项附件**；若团队把 PRD 写在知识库文档里，一期走本地上传，是否另接文档接口列为 R13 / O9。

---

## 5. 里程碑概览（仅规划，不在本文档实现）

| 里程碑 | 目标 | 主要产出 | 依赖 | 状态 |
|--------|------|----------|------|------|
| M1 · 设计产物 | 界面设计稿 + 本 PRD | 十屏高保真预览稿 `docs/界面设计稿-宿主尺寸预览.html`（826×694，**当前有效界面设计稿**）、F0–F6 / F8 / F9 共 9 个 Frame 的 Ardot 画布稿（旧版 1600×1000，待画布编辑连接恢复后按同一规格重排）、本 PRD v1.0、§4.12 能力缺口清单 | 已完成的只读代码勘察、Web 插件管理设计定案、官方文档核对、宿主运行实例与样式文件核对、用户明确的页面逻辑 | **本次交付** |
| M2 · 接线与实机验证 | 把表 B / 表 C 落地为可用能力 | `flow_get_pipeline` / `codeup_update_repository` 包装；工作项读取三件套 `workitem_get` / `workitem_list_attachments` / `workitem_get_file`（C25 / C26）接线，打通「云效同步 PRD」；**优先配置官方托管 MCP 并裁剪 toolsets**（§4.13.2），把表 C 的宿主侧逻辑工具名映射到官方工具；**非特权凭据写入端点 + 可写凭据存储注册 + 校验入口（C22 / C28 / C29）**；实机验证托管端点连通性、PAT 权限边界（R11）、插件页可调端点白名单（R14 / H10）、YAML 化门禁（R12）与「预计完成时间」字段 identifier（R8） | M1 评审通过；O6 就「一期是否接入官方 MCP」拍板；§4.13 官方文档核对（已完成） | 未开始 |
| M3 · 插件实现 | 实现 Web 插件并端到端验收 | `LubanAgent.Plugin.YunXiao` 的 `web/` 页面按 §4.6 实现六项业务界面 + 任务上下文装配与会话驱动 + 配置向导与看板；§4.8 全部 50 条 V 断言通过 | M2 完成；宿主 Web 插件机制（已具备）；插件骨架（已就位） | 未开始 |

> 本次仅 M1；M2 / M3 为规划，不代表已排期或已实现。

---

## 6. 风险与开放问题（R1 ~ R16）

| 编号 | 现状依据（文件 + 符号/章节） | 影响 | 处置 | 回指 FR / Frame |
|------|------------------------------|------|----------|-----------------|
| R1 | 云效 OpenAPI 无「复制流水线」接口（官方 CLI 目录无 Copy/Clone Pipeline；`https://next.api.aliyun.com/product/devops`）；历史 `flow_clone_pipeline` 已删；官方 `GetPipeline` 返回 `pipelineConfig.flow` YAML，产品文宣明确推荐「复制 YAML + 创建/更新 OpenAPI」（§4.13.4 第 1 条） | 「复制现有流水线」在 API 层不成立，但「读 YAML → 改 → 新建/覆盖」有官方依据 | 重定义为「以母版流水线为来源新建/覆盖」：读 YAML → 占位符替换 → diff 预览 → 创建/更新；先包装表 B 行 B1；母版流水线须限定为 YAML 化（R12） | FR-3 / F6 |
| R2 | `BrainstormingSkill.cs` 的「硬门禁」：方案被明确批准前不得调用实现类 Skill、不得写代码、不得创建骨架；「继续」「看着办」不算批准 | 若把该技能当成「一键完成开发」的按钮，会被门禁阻断；若插件再自造一套「已批准」状态，又会与技能内真实判定脱钩 | 不复制技能判定：插件只做「任务标题 + 详情 + 评论」的上下文装配与工作区会话创建，方案确认在会话内完成；`TaskSession` 只记绑定不记批准态（§2.7、§4.10） | FR-7 / F1、F2 |
| R3 | **本仓**无企微连接器（`LubanAgentCore/Connectors/AliyunDevOps/` 内无对应工具）；但企微平台侧有 `wedrive/file_list`、`file_info`、`file_download` 三接口，需自建应用 secret + 「微盘」权限，且仅普通文件可下载、微文档/表格/收集表/幻灯片不可下载（§4.13.3 C21 行）；表 C 行 C21 | 一期若接线需引入独立的企微鉴权体系与文件选择交互，且 PRD 常以微文档形式存在、可能根本读不到正文 | 默认来源走云效同步（工作项 + 附件），本地上传 / 宿主工作区作补充；企微云盘列 P2、界面禁用占位且不产生请求；P2 立项时先确认 PRD 的文件类型分布 | FR-5 / F8 上部 |
| R4 | Endpoint 固定 `openapi-rdc.aliyuncs.com`（`YunxiaoApiOptions.cs` 的 `Endpoint`）；桌面端 `YunxiaoSettingsService.cs` 只有 orgId + PAT、无地址输入框 | 「填云效地址」会被误解为可覆盖 Endpoint | 组织访问地址仅用于解析 organizationId；私有化 Endpoint 是否可配列**开放问题**，本期不支持 | FR-1 / F4 |
| R5 | 状态枚举 / 工作流**本仓未接线**（表 C 行 C8、C9、C17、C18）；官方真源为 `GetWorkitemWorkflow` 的 `statuses[]`（`id` / `displayName` / `name` / `nameEn`）+ `defaultStatusId`，且云效无独立看板 API、看板由状态列 + `ListWorkitems` 条件过滤组合（§4.13.3） | 硬编码四列会与云效实际状态错位 | 看板列名/列数/列序由工作流状态枚举动态生成（宿主侧逻辑名 `workitem_list_statuses` / `workitem_list_workflow`）；取不到显示空态而非回退四列 | FR-7 / F1 |
| R6 | `/system` 特权动作只认固定 Bearer（`Services/WebSystemService.cs`）；WebHost 无凭据写入 API，且**未注册可写 `ICredentialStore`**（框架 `TryAddSingleton<ICredentialStore>` 落 `ConfigurationCredentialStore`，`SetAsync` 抛 `NotSupportedException`；可写实现只在 `LubanAgentCodex/App.axaml.cs`）；插件宿主身份固定默认用户（`PluginHostContext.cs` 的 `UserId`） | FR-1 保存凭据无合法入口；即使补了端点，写库仍会失败；不能假设当前登录用户 | 新增**非特权**凭据写入端点（C22）+ 可写凭据存储注册（C28）+ 校验入口（C29），不复用特权通道；PAT 不进 DOM/日志；身份按默认用户设计；落盘为明文，威胁模型见 §4.9 与 O4 | FR-1 / F4 |
| R7 | MIME 保守白名单（§3.4 H4，`SAEA.Http/Base/HttpMIME.cs`）；`web/` 子目录名不得与 controller 同名（§3.4 H5），保留名在代码里现存 **14 项**且未含 `file`（`Hosting/WebPluginPaths.cs` 的 `HostControllerNames`），而 `Controllers/FileController.cs` 真实存在 | 引入 `.json` / `.mjs` / `.map` / `.wasm` / 独立字体或撞名目录会加载失败/被当路由；`file` 同名目录当前不被拦截 | 字体 base64 内嵌 CSS、数据写进 `.js`；目录命名避开 14 个保留名；落地前把 `file` 补入 `HostControllerNames` 并覆盖安装期校验 | 全局 / F0 |
| R8 | 「预计完成时间」不是云效固定系统字段：「截止日期 / 期望日期 / 预计工时」属项目可配置自定义属性，字段 identifier 随项目配置而变（§4.13.4 第 3 条）；排序还需处理空值与时区 | 写死 `dueDate` 会取不到值，导致看板排序错位 | **排序前必须经 `ListWorkItemAllFields` 动态解析字段 identifier，不得写死字段名**；空值置末、时区统一（表 C 行 C14、C18 落点，O2） | FR-6 / FR-7 / F1、F8 |
| R9 | 同源 iframe 下插件 JS 读不到浏览器本机 FS（§2.3）；宿主工作区由 `WorkspaceController.cs` / `WebFileService.cs` 提供 | 「本机目录」被误解为浏览器可访问的用户磁盘 | 明确「本机工作目录」= 宿主工作区目录；文件走 `AttachmentController.Upload` / `FileController.Read`；界面不出现按路径读写本机磁盘的控件（`<input type="file">` 用户手势不受限） | FR-2 / FR-5 / F5、F8 |
| R10 | **本仓**云效连接器仅 Codeup 7 + Flow 6 共 13 个工具，无项目协作工具（`LubanAgentCore/Connectors/AliyunDevOps/` 的 `GetAllTools()`）；云效官方 **OpenAPI** 已有 `projects:search` / `CreateProject`，官方**托管 MCP** 的 `project-management` 公开工具清单未列出 `create_project`（§4.13.2、§4.13.4 第 6 条）；表 C 行 C1–C4 | FR-4 的「创建项目」分支在本仓未接线；若画成可用会误导，若误以为「接入官方 MCP 即可创建」会漏接 C3 | **界面不提供创建入口**（V4-4），只保存「绑定既有项目」配置，列表底部提示「新建项目请在云效控制台完成」；C3 留作后续可选能力（O8） | FR-4 |
| R11 | 云效官方托管 MCP 的 Prerequisites 要求 PAT 对组织管理 / 项目协作 / 代码管理 / 流水线 / 制品 / 应用交付 / 测试管理**全部 API 授予读写权限**（`https://github.com/aliyun/alibabacloud-devops-mcp-server`）；与 §2.4「最小权限与凭据隔离」冲突。托管端点 `https://openapi-rdc.aliyuncs.com/ai/mcp` 亦未在宿主环境实测 | 单个 PAT 权限面过大；若端点不可用或工具集裁剪失效，会引入超量工具上下文与越权写入面 | M2 准入前实机验证连通性；用 `?toolsets=` / `X-Devops-Toolsets` 只开必要 toolsets；评估按环境拆分 PAT 或改用只读 token；仍无法收敛最小权限时列为 O6 决策项 | 全局 / F0、F4 |
| R12 | 官方 `GetPipeline` 返回体 `type = PIPELINEASCODE` 才表示 YAML 化流水线，`null` 表示 UI 编排；`UpdatePipeline` 明确「只支持更新 YAML 化流水线」（§4.13.4 第 1 条） | 若母版流水线为 UI 编排，`pipelineConfig.flow` 无可用 YAML，FR-3「读 YAML → 替换 → 新建/覆盖」链路不成立 | F6 选母版流水线时回读 `type` 并做门禁：非 YAML 化流水线显示「该流水线为可视化编排，不支持以 YAML 复制」并禁用下一步（V3-1） | FR-3 / F6 |
| R13 | **PRD 的第三种存在形态读不到**：项目知识库文档页在官方 OpenAPI 里没有对应的项目文档读取接口；`lingma/knowledgeBases` 属通义灵码问答知识库，不是 Projex 项目文档（§4.13.4 第 12 条）。可读的只有需求类工作项（`ListWorkitems` / `GetWorkItemInfo`）与其附件（`ListWorkitemAttachments` / `GetWorkitemFile`）；且需求描述为 `RICHTEXT` 富文本，转纯文本会丢表格与图片 | 若团队的 PRD 主要是知识库文档或富文本长文，云效同步拿到的可能是残缺正文，拆分会漏需求；用户仍要回到「本地上传」 | FR-5 已把「云效同步」边界写成「工作项 + 工作项附件」，并在同步摘要里显示条数与附件份数，让用户一眼看出拿到多少；解析后标出「富文本降级」的位置，必要时提示补传本地文档（V5-1、V5-6）；是否另接知识库 / 文档接口列为 O9 | FR-5 / F8 上部 |
| R14 | 同源 iframe 的 Cookie 调用面 = **全部非特权控制器**：`Hosting/WebApi.cs` 的 `HasValidCredential` 同时接受固定 Bearer 与浏览器会话 Cookie；`Controllers/McpController.cs` 的 `Save` / `Delete`（MCP 配置，含 stdio 命令）、`Controllers/ProviderController.cs` 的 `Save` / `Delete`（模型与密钥）、`Controllers/WorkspaceController.cs` 的 `Delete` / `Rename` 均只要求该凭据；`appsettings.json` 的 `Plugins:Security` 只配了 `NetworkWhitelist` / `CredentialCatalog` / `Connectors`，而 `PluginPermissions`（`Plugins/Contracts/PluginPermissions.cs`）的按插件 id 作用域只在 Agent 侧生效（`LubanAgentCore/Plugins/AgentPluginHost.cs` 的 MCP 注册），WebHost 控制器不按其判定 | 插件页面里任意 JS（含被注入的第三方脚本代码）都能改 MCP、改 Provider 密钥、删工作区；「只给非特权 API」并不构成最小权限，与 §2.4 的隔离原则相悖 | 前端按 §3.4 H10 白名单只调必要端点；宿主侧接上 `PluginPermissions`（按插件 id 限定可调控制器）或在门禁层按路径前缀拒绝；落地前把「插件页可调端点清单」写进 M1 / M2 验收 | 全局 / F0 |
| R15 | **git 没有 REST 入口**：git 是 Agent 工具组 `luban-framework/LuBan.AIAgent/Tools/Git/GitToolPlugin`（clone / commit / push / status / diff），WebHost 侧无对应控制器，插件 JS 无法自行克隆 / 改名 / 推送（§3.6、FR-2 执行通路） | FR-2 的「克隆母版 → 本地改名 → 推送 master」若按同步 API 设计会完全落空；改为会话式执行又有工具确认、进度解析与失败重试的新形态 | 一期定案（O10）：经**工作区会话**由 Agent 执行（`/agent/chat` + `Confirm`），C30 同步端点不做；界面进度对齐 SSE 事件流，执行前明示 git 命令序列，通路未接线时按钮禁用（V2-7） | FR-2 / F5 |
| R16 | **工作区文件 / 目录列举接口不存在**：`FileController` 只有按相对路径读单文件的 `Read`，`WorkspaceController` 只管工作区元数据（§3.6） | 「宿主工作区选文件」若画成目录浏览器会是空壳；用户误以为能浏览磁盘 | 一期把该来源收敛为「按已知相对路径读取」，界面不出现文件树（V5-7）；真要浏览目录须接线 C31（M5）；本地上传走 `<input type="file">` 不受影响 | FR-5 / F8 上部 |

### 开放问题汇总

| 编号 | 开放问题 | 关联风险 | 状态 / 决策时点 |
|------|----------|----------|--------------|
| O1 | 是否支持私有化 / Region 版 Endpoint（覆盖固定 Endpoint） | R4 | 待定 · M2 准入前 |
| O2 | 「预计完成时间」在目标项目里的自定义字段 identifier、格式与空值语义（须经 `ListWorkItemAllFields` 动态解析） | R8 | 待定 · M2 接线时按项目实测 |
| O3 | 企微云盘鉴权形态（自建应用 secret / 微盘权限）、PRD 文件类型分布，以及微文档不可下载的替代读法 | R3 | 待定 · P2 立项时 |
| O4 | 非特权凭据写入端点（C22）的落库位置与权限边界；可写 `ICredentialStore` 由谁注册（C28）；**PAT 落盘为明文**（§4.9），是否引入本机加密（如 DPAPI / 受保护存储）及其与 Provider API Key 基线的一致性 | R6、R14 | 待定 · M2 设计评审 |
| O5 | 状态枚举「未开始 / 开发中」可触发类的推导规则（按枚举元数据还是项目配置） | R5 | 待定 · M2 / FR-7 细化 |
| O6 | **一期是否改为接入云效官方托管 MCP**，从而放开 F1/F8 部分禁用占位与项目搜索 / 迭代 / 工作项 / 状态 / 测试能力；代价是 PAT 全读写权限面（R11）与工具集裁剪。**该选项与「新建项目」无关**——插件不提供创建入口，且官方 MCP 也未列出创建项目工具（§4.13.2） | R11、R10、R3 | **待产品拍板；未拍板前维持现有一期范围与全部禁用占位态** |
| O7 | 官方 MCP toolsets 的最小必要集合，以及是否用只读 PAT / 多 PAT 分权收敛权限面 | R11 | 待定 · M2 准入前 |
| O8 | **后续版本是否在插件内提供创建项目**（接线官方 `CreateProject`，并一并提供 `GetProjectTemplateList` 模板选择、项目编号查重、可见性与字段预检确认）。一期不做；若后续要做，最小成本做法是列表底部保留一个「前往云效控制台创建」的外链（不产生插件写请求） | R10、R11 | **已定：一期不做，界面无入口**（后续可选） |
| O9 | **云效侧 PRD 的读取边界**：一期只同步「需求类工作项 + 工作项附件」；若团队 PRD 主要写在项目知识库文档里，是否需要另接文档接口，或统一约定「PRD 以工作项附件形式挂到需求上」。前者依赖官方 API 是否补齐，后者是流程约定、零开发 | R13 | 待定 · M2 接线前与产品 / 项目管理员确认 PRD 的实际存放习惯 |
| O10 | FR-2 的 git 执行通路 | R15、R14 | **已定（2026-11-17）：会话式**——git 动作经工作区会话由 Agent 调 git 工具组执行（`/agent/chat` + `Confirm` 人工确认，进度与回执从 SSE 事件流解析）；C30 同步端点降为后续可选、一期不做 |
| O11 | 紧凑插件页头 | §2.6 | **已定（2026-11-17）：向宿主提需求**——单行页头 32px + 留白 8px 替代现状 91px，内容区 694px → 约 745px（折叠隐藏约 785px）；属宿主侧改动，插件仍按 694px 绘制、不返工 |

---

## 7. 附录：状态机（中文文字描述）

> 三段状态机用中文文字 + 状态转换表描述，不引入代码围栏。A 为一次性配置向导，B 为任务会话，C 为看板任务。

### A · 配置向导状态机（FR-1 ~ FR-4）

状态集合：未开始、连接未配置、连接已校验、连接已保存、Codeup 预检、Codeup 待确认、Codeup 执行中、Codeup 已就绪、Flow 已选参考、Flow 已读 YAML、Flow 已预览、Flow 待提交、Flow 已创建/已覆盖、项目浏览中、项目待确认、项目已设置、失败。

| 当前状态 | 事件 | 下一状态 | 说明 |
|----------|------|----------|------|
| 未开始 | 进入配置向导 | 连接未配置 | 从 F4 起步 |
| 连接未配置 | 校验通过 | 连接已校验 | 只读探活 |
| 连接已校验 | 保存成功 | 连接已保存 | 落库到凭据键 |
| 连接已保存 | 进入 F5 | Codeup 预检 | 只读清单，不创建资源 |
| Codeup 预检 | 勾选「我已确认」 | Codeup 待确认 | — |
| Codeup 待确认 | 执行 | Codeup 执行中 | 经工作区会话克隆母版 / 本地改目录名 / 建新库 / 推送 master |
| Codeup 执行中 | 回读新库与 master、三个改名目录 | Codeup 已就绪 | 解锁 F6 |
| Codeup 执行中 | 克隆失败 | 失败 | 清理本地目录、远端列清单 |
| Codeup 已就绪 | 进入 F6 反查母版流水线 | Flow 已选参考 | 经表 B 行 B1 读 YAML |
| Flow 已选参考 | 读到 YAML | Flow 已读 YAML | — |
| Flow 已读 YAML | 生成 diff | Flow 已预览 | 占位符替换 |
| Flow 已预览 | 选新建/覆盖并确认 | Flow 待提交 | 覆盖须卡内确认 |
| Flow 待提交 | 提交成功 | Flow 已创建/已覆盖 | 解锁 F3 项目浏览 |
| Flow 已创建/已覆盖 | 回到 F3 | 项目浏览中 | 列项目并绑定当前项目 |
| 项目浏览中 | 选择已有项目并确认 | 项目待确认 | — |
| 项目待确认 | 设置成功 | 项目已设置 | 设为当前项目 |
| 任一失败态 | 重试 / 重跑 | 对应前置状态 | 幂等复用，远端不自动删 |

### B · 任务会话状态机（FR-7）

状态集合：未开始、会话已创建、进行中、已完成、已取消、失败。**只描述会话自身的生命周期，不模拟技能内部的方案 / 实现阶段。**

| 当前状态 | 事件 | 下一状态 | 说明 |
|----------|------|----------|------|
| 未开始 | 点「在工作区开始任务」（仅未开始/开发中） | 会话已创建 | 先按工作目录查/建工作区，再建会话；写入 `TaskSession` |
| 未开始 | 工作区不可用 / 会话创建失败 | 未开始 | 保留已拼好的首轮输入，可重试 |
| 会话已创建 | 首轮输入发送成功 | 进行中 | 首轮含标题、描述、验收标准与全部评论；要求先加载 brainstorming 技能 |
| 进行中 | SSE 中断 / 页面关闭 | 进行中 | 经 `Active` / `Attach` 续接，或回宿主继续同一会话 |
| 进行中 | 用户在会话中取消 | 已取消 | 已产生的读取动作不回滚，不产生实现产物 |
| 进行中 | 会话结束且有产物 / 结论 | 已完成 | 回填结果摘要；是否回写云效状态由用户确认 |
| 进行中 | 会话异常终止 | 失败 | 可「重新开始」，新建会话并保留旧绑定历史 |
| 已完成 / 已取消 | 用户「继续会话」 | 进行中 | 复用同一 `sessionId`，不重复带入评论；如需刷新上下文用「重新开始」 |
| 任意态 | 任务状态离开「未开始/开发中」 | 不变 | 只禁用主按钮，已有会话仍可查看与继续 |

> 硬约束：「方案未获明确批准前不写代码」由 brainstorming 技能在会话内保证，插件不另存该状态（V7-6、V7-9、R2）；首轮输入必须完整包含任务标题、描述与评论（V7-5）。

### C · 看板任务状态机（FR-7）

状态集合：由拆分新建、云效枚举状态（列名/列数/列序动态生成）、Agent 处理中、已完成、已归档。

| 当前状态 | 事件 | 下一状态 | 说明 |
|----------|------|----------|------|
| （无） | FR-6 拆分提交 | 由拆分新建 | 落入状态枚举对应的初始列 |
| 由拆分新建 | 用户/云效改状态 | 云效枚举状态 | 列归属随状态枚举变化，不写死 |
| 云效枚举状态（未开始/开发中类） | 在工作区开始任务 | Agent 处理中 | 进入附录 B |
| Agent 处理中 | 会话完成 | 云效枚举状态 或 已完成 | 是否回写云效状态以用户确认为准 |
| 云效枚举状态 | 迭代结束 / 归档 | 已归档 | 看板按当前迭代过滤 |
| 任意态 | 状态枚举取不到 | 空态（非四列回退） | 见 §4.6 状态空态表、V7-3、R5 |

> 看板列本身不是固定状态：`BoardColumn` 由 `workitem_list_statuses` / `workitem_list_workflow` 动态生成，任务只在「当前列集合」内迁移（R5、V7-3）。

---

## 附录 D · 变更记录（精简）

| 版本 | 一句话 |
|------|--------|
| v0.1 | 首版草案：7 项诉求 → FR-1~FR-7，能力缺口三表（表 A/B/C）立项 |
| v0.2 | 联网核对云效 / 企微平台能力，新增 §4.13（官方 URL 与真实 REST 路径）；「平台零能力」更正为「本仓未接线」 |
| v0.2.1 | 核实官方 `CreateProject` 可用但托管 MCP 未列创建工具；创建项目改「后续可选」（O8） |
| v0.2.2 | 按宿主运行实例把画板基准从 1600×1000 改为 iframe 尺寸（当时推导 826×735）；产出十屏 HTML 预览稿 |
| v0.2.3 | 信息架构重排：连接门禁 → 项目列表首页 → 看板 → 任务详情，新增 §2.9 页面流转 |
| v0.2.4 | 任务详情单入口（含评论 → 工作区会话），取消插件侧「两阶段批准」UI，`TaskSession` 只记绑定 |
| v0.2.5 | 首页左右分栏（左列表 / 右统计 + 三入口），新增迭代测试屏，Frame 增至 F0–F9 |
| v0.2.6 | 项目初始化细化：模板多选、三端流水线、后端参数 |
| v0.2.7 | 移除新建项目入口（F7 删除）；流水线只绑 dev；后端参数收缩为监听端口 |
| v0.2.8 | 代码库与端解耦（一库多端），模板按工程结构不按端；按官网修正 Flow「环境」口径 |
| v0.2.9 | 按真实库 `TJCES-Mixed` 校准「一库多端」：新增构建目录 `workDir`，库为项目级 |
| v0.3.0 | 代码库改为「母版库整库复制 + 代号派生命名」（`TJCES-Mixed` → `RGMS-Mixed`），分支口径改 `master` |
| v0.3.1 | PRD 来源改「云效同步默认 + 本地补充」，拆分产物五类写入当前项目；首页右栏扩为四入口 |
| v0.3.2 | 两文档一致性复核：计数 / 分支 / 表格归类 / 循环依赖 / 排序等九类订正 |
| v0.3.3 | 审查整改：凭据三件套（C22/C28/C29）、保留名录缺口（`file`）、H10/R14 插件调用面、PAT 明文落盘披露 |
| v0.3.4 | 与实现代码对表：**尺寸实测 826×694**（735 作废）、git 执行通路硬点、工作区文件列举缺口（C30/C31） |
| v0.4.0 | 拍板 O10 会话式 git 执行、O11 紧凑页头；新增 §4.6.3 数据密度与长文本规范，验收增至 50 条 |
| v0.4.1 | 移除流程位置条与动作栏提示文字两类说明性内容（十屏全清），内容区回升 602px |
| **v1.0** | **干净定稿**：删除累积修订说明与过程性措辞，正文直接陈述当前设计；现状断言更新为「插件骨架已就位」；文末保留本变更表 |

