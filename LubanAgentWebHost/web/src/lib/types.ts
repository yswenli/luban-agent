/** 后端响应壳（`ApiEnvelope`）。 */
export interface ApiEnvelope<T> {
  success: boolean;
  code: number;
  message?: string | null;
  data?: T | null;
}

export interface Workspace {
  workspaceId: string;
  name: string;
  rootPath: string;
  type: string;
  userId?: string | null;
  configPath?: string | null;
  lastActiveAt?: string | null;
  isAuthorized: boolean;
  /** 工作区根目录下是否存在 `.git`（Git 仓库）；后端未返回时按无处理。 */
  hasGit?: boolean;
}

export interface ProviderSummary {
  id: string;
  displayName: string;
  models: string[];
}

export interface PermissionMode {
  key: string;
  displayName: string;
}

export interface AttachmentPolicy {
  extensions: string[];
  maxCount: number;
  undecodableExtensions: string[];
  /** 按图片处理的扩展名（与后端判定同源，MIME 缺失时也能正确分流）。 */
  imageExtensions: string[];
  maxTextBytes: number;
  maxImageBytes: number;
  maxImageEdge: number;
  /** 图片上传硬上限：超过直接拒绝，不进入解码流程。 */
  maxImageUploadBytes: number;
  /** 图片像素总数上限：超过直接拒绝，浏览器端据此避免整幅解码。 */
  maxImagePixels: number;
}

export interface Bootstrap {
  userId: string;
  version?: string | null;
  workspaces: Workspace[];
  currentWorkspaceId?: string | null;
  providers: ProviderSummary[];
  selection?: { providerId?: string | null; modelId?: string | null } | null;
  permissionModes: PermissionMode[];
  /** 当前（全局）模型可选的思考档位；空串表示跟随模型，空数组表示不显示控件。 */
  reasoningEfforts: string[];
  /** 当前（全局）模型可选的上下文预算档位（token）；空数组表示不显示控件。 */
  contextTiers: number[];
  /** 当前（全局）模型上下文窗口（token）。 */
  contextWindow?: number | null;
  attachments: AttachmentPolicy;
  confirmation: { timeoutSeconds: number };
}

/** 模型能力档案（服务端 `model/profile`）。 */
export interface ModelProfile {
  providerId: string;
  modelId: string;
  /** 可发档位；`null` 表示未知，空数组表示明确不支持。 */
  reasoningEfforts?: string[] | null;
  /** 上下文窗口（token）；`null` 表示未知。 */
  contextWindow?: number | null;
  /** 按窗口派生的上下文预算档位。 */
  contextTiers: number[];
  /** 是否已有用户登记（决定「恢复内置默认」是否可用）。 */
  hasOverride: boolean;
}

/** 会话生效的生成参数（服务端 `agent/sessionSettings`）。 */
export interface SessionGenerationSettings {
  /** 生效的思考档位（枚举名）；`null` 表示跟随模型。 */
  reasoningEffort?: string | null;
  /** 生效的上下文触发阈值（token，已按窗口留白收敛）；`null` 表示未知或未设。 */
  contextTier?: number | null;
  /** 用户选中的上下文预算档位（token，未过窗口留白）；用于菜单勾选，`null` 表示未知。 */
  contextBudget?: number | null;
  /** 该会话模型可选的思考档位；空串表示跟随模型，空数组表示不显示控件。 */
  reasoningEfforts: string[];
  /** 该会话模型可选的上下文预算档位（token）；空数组表示不显示控件。 */
  contextTiers: number[];
  /** 该会话模型上下文窗口（token）。 */
  contextWindow?: number | null;
}

export interface SessionSummary {
  sessionId: string;
  title?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  messageCount: number;
  totalTokens: number;
  workspaceId?: string | null;
  provider?: string | null;
  modelName?: string | null;
}

export interface SessionMessage {
  id: number;
  sessionId: string;
  role: string;
  content: string;
  thinking?: string | null;
  /** 序列化的 `AttachmentRecord[]`（PascalCase）。 */
  attachments?: string | null;
  /** 序列化的 `[{ name, callId, arguments }]`，`arguments` 是 JSON 字符串。 */
  toolCalls?: string | null;
  /** 序列化的 `[{ kind, text, name, callId, arguments }]`（camelCase），按发生顺序排列。 */
  segments?: string | null;
  tokens?: number | null;
  /** 本轮助手回复耗时（毫秒）。 */
  durationMs?: number | null;
  createdAt: string;
}

export interface WebCommand {
  name: string;
  aliases: string[];
  description: string;
  category: string;
  usage: string;
  action: string;
}

export interface AcceptedAttachment {
  path: string;
  fileName: string;
  mediaType: string;
  size: number;
  /** 相对工作区根的路径，供 `/file/read` 预览；落在工作区外时为空串。 */
  webPath: string;
}

export interface RejectedAttachment {
  fileName: string;
  reason: string;
}

export interface AttachmentUploadResult {
  accepted: AcceptedAttachment[];
  rejected: RejectedAttachment[];
}

/** 历史消息里 `Attachments` 字段的元素（框架 `AttachmentRecord`，PascalCase）。 */
export interface StoredAttachment {
  FileName?: string;
  MediaType?: string;
  FileSize?: number;
  SourcePath?: string;
  Kind?: string;
  /** 宿主补上的相对工作区根路径；缺省表示不可在页内预览。 */
  WebPath?: string;
  TextPayload?: string | null;
  ProcessedPath?: string | null;
  IsLargeText?: boolean;
}

/** 历史消息里 `ToolCalls` 字段的元素。 */
export interface StoredToolCall {
  name: string;
  callId: string;
  arguments: string;
}

/** 会话消息里的一段有序内容（`SessionMessage.segments` 的元素）。 */
export interface StoredSegment {
  kind?: string;
  text?: string | null;
  name?: string | null;
  callId?: string | null;
  arguments?: string | null;
  /** 段落耗时（毫秒）：思考段为区间时长，tool 段为一次调用时长；旧数据为空。 */
  durationMs?: number | null;
}

/** 待发送附件（本地队列项）。 */
export interface PendingAttachment {
  id: string;
  fileName: string;
  mediaType: string;
  size: number;
  /** 服务端落盘后的绝对路径；上传完成前为空。 */
  path?: string;
  /** 服务端给出的相对工作区根路径；上传完成前为空。 */
  webPath?: string;
  status: 'uploading' | 'ready' | 'error';
  error?: string;
  previewUrl?: string;
}

/* ── 设置页（文件型条目与供应商 / 模型） ─────────────────────────────── */

/** 技能列表项：`id` 是目录名（稳定键），`name` 是 frontmatter 里的显示名。 */
export interface SkillSummary {
  id: string;
  name: string;
  category: string;
}

/** 输入 `@` 时可选的技能（当前会话可用技能，只含启用项）。 */
export interface AvailableSkill {
  /** 技能标识：选中后插入输入框的文本。 */
  id: string;
  name: string;
  description: string;
  category: string;
  isBuiltin: boolean;
}

export interface SkillFile {
  /** 目录名；新建时为占位空串。 */
  id: string;
  name: string;
  description: string;
  category: string;
  triggers: string;
  body: string;
}

export interface RuleFile {
  id: string;
  name: string;
  description: string;
  actionTypePattern: string;
  targetPattern: string;
  action: string;
  priority: number;
  enabled: boolean;
  content: string;
}

export interface McpFile {
  name: string;
  description: string;
  transport: string;
  target: string;
  args: string[];
  enabled: boolean;
}

export interface ProviderListItem {
  name: string;
  displayName: string;
  baseUrl?: string | null;
  apiKeyMask: string;
  networkTimeoutSeconds?: number | null;
  customModels: string[];
  isSelected: boolean;
}

/** 内置供应商预设（后端 `/provider/presets`）。 */
export interface ProviderPreset {
  name: string;
  displayName: string;
  baseUrl: string;
  /** 基址是否仍需用户填写（自定义端点项，以及无预设地址的项）。 */
  editableBaseUrl: boolean;
  /** 是否为「自定义」项；true 时标识与基址均可编辑。 */
  isCustom: boolean;
  /** 添加时的风险提示（如 Claude 需经代理），无则为 null。 */
  warning?: string | null;
}

export interface ModelItem {
  modelId: string;
  isCustom: boolean;
  isSelected: boolean;
}

export interface ProviderModels {
  providerId: string;
  displayName: string;
  models: ModelItem[];
}

/* ── 工作区 AGENTS.md 初始化 ─────────────────────────────────────────── */

export interface AgentsMdInfo {
  workspaceId: string;
  workspaceName: string;
  rootPath: string;
  fileName: string;
  targetPath: string;
  exists: boolean;
  existingLength: number;
}

export interface AgentsMdResult {
  targetPath: string;
  markdown: string;
  verificationPrompt: string;
  sessionId?: string | null;
}

/* ── 全局用量 ─────────────────────────────────────────────────────────── */

export interface GlobalSessionStats {
  totalSessions: number;
  totalMessages: number;
  totalTokens: number;
  days: number;
  averageDailyTokens: number;
}

/* ── LLM Wiki 知识库 ─────────────────────────────────────────────────── */

export interface WikiStatus {
  pageCount: number;
  lastRebuildSummary?: string | null;
  indexedFiles: number;
  indexedChunks: number;
}

export interface WikiFileInfo {
  filePath: string;
  language?: string | null;
  chunkCount: number;
  indexedTime: string;
}

export interface RetrievalHit {
  chunkId: number;
  filePath: string;
  startLine: number;
  endLine: number;
  chunkType: string;
  symbolName?: string | null;
  language: string;
  content: string;
  score: number;
}

export interface IndexReport {
  scannedFiles: number;
  newFiles: number;
  updatedFiles: number;
  skippedFiles: number;
  deletedFiles: number;
  totalChunks: number;
  embeddedChunks: number;
  reusedChunks: number;
  errors: string[];
  warnings: string[];
}

export interface LintFinding {
  kind: string;
  path: string;
  detail: string;
}

export interface WikiLintReport {
  findings: LintFinding[];
  pageCount: number;
}

/* ── 插件（Web 插件根，与桌面端隔离） ────────────────────────────────── */

export interface PluginSkillInfo {
  id: string;
  name: string;
  description?: string | null;
}

export interface PluginMcpInfo {
  name: string;
  transport?: string | null;
}

/** 插件的页面入口；`url` 指向当前镜像，插件未启用或镜像失败时为空串。 */
export interface PluginPageInfo {
  entryId: string;
  label: string;
  icon?: string | null;
  webPage: string;
  url: string;
}

export type PluginState = 'installed' | 'enabled' | 'disabled' | 'error';

export interface PluginItem {
  id: string;
  name: string;
  version: string;
  author?: string | null;
  description?: string | null;
  state: PluginState;
  isBuiltin: boolean;
  requiresRestart: boolean;
  lastError?: string | null;
  skills: PluginSkillInfo[];
  mcpServers: PluginMcpInfo[];
  pages: PluginPageInfo[];
  /** 页面镜像失败原因；成功或无需镜像时为 null。 */
  pageError?: string | null;
}

export interface PluginList {
  rootPath: string;
  items: PluginItem[];
}

export interface PluginChangeResult {
  id: string;
  state: string;
  warnings: string[];
  plugin?: PluginItem | null;
}