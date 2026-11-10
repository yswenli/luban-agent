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
  maxTextBytes: number;
  maxImageBytes: number;
  maxImageEdge: number;
}

export interface Bootstrap {
  userId: string;
  version?: string | null;
  workspaces: Workspace[];
  currentWorkspaceId?: string | null;
  providers: ProviderSummary[];
  selection?: { providerId?: string | null; modelId?: string | null } | null;
  permissionModes: PermissionMode[];
  reasoningEfforts: string[];
  attachments: AttachmentPolicy;
  confirmation: { timeoutSeconds: number };
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
  tokens?: number | null;
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

/** 待发送附件（本地队列项）。 */
export interface PendingAttachment {
  id: string;
  fileName: string;
  mediaType: string;
  size: number;
  /** 服务端落盘后的绝对路径；上传完成前为空。 */
  path?: string;
  status: 'uploading' | 'ready' | 'error';
  error?: string;
  previewUrl?: string;
}

/* ── 设置页（文件型条目与供应商 / 模型） ─────────────────────────────── */

export interface SkillFile {
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

export interface A2AServer {
  host: string;
  port: number;
  path: string;
  requireApiKey: boolean;
  apiKeys: string[];
  agentName?: string | null;
  agentDescription?: string | null;
  agentVersion?: string | null;
}

export interface A2AClientListItem {
  key: string;
  name?: string | null;
  endpoint?: string | null;
}

export interface A2AClient {
  key: string;
  name?: string | null;
  endpoint?: string | null;
  apiKey?: string | null;
  registerAsSubAgent: boolean;
  remoteAgentName?: string | null;
  allowAll: boolean;
}