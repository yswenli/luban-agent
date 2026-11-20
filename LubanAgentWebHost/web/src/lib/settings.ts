import { get, post } from './api';
import type {
  A2AClient,
  A2AClientListItem,
  A2AServer,
  AgentsMdInfo,
  AgentsMdResult,
  GlobalSessionStats,
  McpFile,
  ModelItem,
  ModelProfile,
  ProviderListItem,
  ProviderModels,
  ProviderPreset,
  RuleFile,
  SkillFile,
  SkillSummary,
  Workspace,
} from './types';

/**
 * 设置页接口封装。
 *
 * 作用域一律用字符串：`global` 表示全局目录（`~/.luban-agent`），
 * 其余值当作工作区标识（服务端换算成该工作区根目录）。
 */
export type ScopeValue = string;

/** 保存供应商的请求体；`originalName` 非空表示改名，服务端按重命名处理并迁移密钥与选中模型。 */
export interface ProviderSaveRequest {
  name: string;
  originalName?: string | null;
  apiKey?: string | null;
  baseUrl?: string | null;
  displayName?: string | null;
  networkTimeoutSeconds?: number | null;
  customModels?: string[];
}

interface ListResponse<T> {
  items: T[];
}

export const settingsApi = {
  /* ── 技能 ── */
  listSkills: async (scope: ScopeValue): Promise<SkillSummary[]> =>
    (await get<ListResponse<SkillSummary>>('/skill/list', { scope }))?.items ?? [],
  readSkill: (scope: ScopeValue, key: string): Promise<SkillFile> =>
    get<SkillFile>('/skill/read', { scope, key }),
  saveSkill: (body: Record<string, unknown>): Promise<{ key: string }> =>
    post<{ key: string }>('/skill/save', body),
  importSkills: (body: {
    scope: ScopeValue;
    fileName: string;
    contentBase64: string;
  }): Promise<{ imported: string[]; skipped: string[]; ignored: string[] }> =>
    post<{ imported: string[]; skipped: string[]; ignored: string[] }>('/skill/import', body),
  deleteSkill: (scope: ScopeValue, key: string): Promise<void> =>
    post<void>('/skill/delete', { scope, key }),

  /* ── 规则 ── */
  listRules: async (scope: ScopeValue): Promise<string[]> =>
    (await get<ListResponse<string>>('/rule/list', { scope }))?.items ?? [],
  readRule: (scope: ScopeValue, key: string): Promise<RuleFile> =>
    get<RuleFile>('/rule/read', { scope, key }),
  saveRule: (body: Record<string, unknown>): Promise<{ key: string }> =>
    post<{ key: string }>('/rule/save', body),
  deleteRule: (scope: ScopeValue, key: string): Promise<void> =>
    post<void>('/rule/delete', { scope, key }),

  /* ── MCP ── */
  listMcps: async (scope: ScopeValue): Promise<string[]> =>
    (await get<ListResponse<string>>('/mcp/list', { scope }))?.items ?? [],
  readMcp: (scope: ScopeValue, key: string): Promise<McpFile> =>
    get<McpFile>('/mcp/read', { scope, key }),
  saveMcp: (body: Record<string, unknown>): Promise<{ key: string }> =>
    post<{ key: string }>('/mcp/save', body),
  deleteMcp: (scope: ScopeValue, key: string): Promise<void> =>
    post<void>('/mcp/delete', { scope, key }),

  /* ── 供应商 ── */
  listProviders: async (): Promise<ProviderListItem[]> =>
    (await get<ListResponse<ProviderListItem>>('/provider/list'))?.items ?? [],
  listProviderPresets: async (): Promise<ProviderPreset[]> =>
    (await get<ListResponse<ProviderPreset>>('/provider/presets'))?.items ?? [],
  saveProvider: (body: ProviderSaveRequest): Promise<ProviderListItem> =>
    post<ProviderListItem>('/provider/save', body),
  deleteProvider: (name: string): Promise<void> => post<void>('/provider/delete', { name }),

  /* ── 模型 ── */
  listModels: async (providerId?: string): Promise<ProviderModels[]> =>
    (await get<{ providers: ProviderModels[] }>('/model/list', { providerId }))?.providers ?? [],
  refreshModels: (providerId: string): Promise<{ providerId: string; models: string[] }> =>
    get<{ providerId: string; models: string[] }>('/provider/models', { providerId }),
  addModel: (providerId: string, modelId: string): Promise<ModelItem> =>
    post<ModelItem>('/model/add', { providerId, modelId }),
  renameModel: (providerId: string, oldModelId: string, newModelId: string): Promise<ModelItem> =>
    post<ModelItem>('/model/rename', { providerId, oldModelId, newModelId }),
  deleteModel: (providerId: string, modelId: string): Promise<void> =>
    post<void>('/model/delete', { providerId, modelId }),
  /** 读取模型能力档案（档位与上下文窗口）。 */
  getModelProfile: (providerId: string, modelId: string): Promise<ModelProfile> =>
    get<ModelProfile>('/model/profile', { providerId, modelId }),
  /** 写入模型能力档案；`reset` 为 true 时删除用户登记、恢复内置默认。 */
  saveModelProfile: (body: {
    providerId: string;
    modelId: string;
    reasoningEfforts?: string[] | null;
    contextWindow?: number | null;
    reset?: boolean;
  }): Promise<ModelProfile> => post<ModelProfile>('/model/saveProfile', body),
  selectModel: (providerId: string, modelId: string): Promise<void> =>
    post<void>('/provider/select', { providerId, modelId }),

  /* ── A2A ── */
  getA2AServer: (scope: ScopeValue): Promise<A2AServer> => get<A2AServer>('/a2a/getServer', { scope }),
  saveA2AServer: (body: Record<string, unknown>): Promise<{ key: string }> =>
    post<{ key: string }>('/a2a/saveServer', body),
  listA2AClients: async (scope: ScopeValue): Promise<A2AClientListItem[]> =>
    (await get<ListResponse<A2AClientListItem>>('/a2a/listClients', { scope }))?.items ?? [],
  getA2AClient: (scope: ScopeValue, key: string): Promise<A2AClient> =>
    get<A2AClient>('/a2a/getClient', { scope, key }),
  saveA2AClient: (body: Record<string, unknown>): Promise<{ key: string }> =>
    post<{ key: string }>('/a2a/saveClient', body),
  deleteA2A: (scope: ScopeValue, key: string): Promise<void> =>
    post<void>('/a2a/deleteClient', { scope, key }),

  /* ── 工作区 ── */
  listWorkspaces: async (): Promise<Workspace[]> => (await get<Workspace[]>('/workspace/list')) ?? [],
  authorizeWorkspace: (workspaceId: string): Promise<Workspace> =>
    post<Workspace>('/workspace/authorize', { workspaceId }),
  ensureWorkspaceConfig: (workspaceId: string): Promise<Workspace> =>
    post<Workspace>('/workspace/ensureConfig', { workspaceId }),

  /* ── AGENTS.md ── */
  agentsMdInfo: (workspaceId: string): Promise<AgentsMdInfo> =>
    post<AgentsMdInfo>('/agentsmd/info', { workspaceId }),
  agentsMdGenerate: (body: Record<string, unknown>): Promise<AgentsMdResult> =>
    post<AgentsMdResult>('/agentsmd/generate', body),
  agentsMdSave: (body: Record<string, unknown>): Promise<AgentsMdResult> =>
    post<AgentsMdResult>('/agentsmd/save', body),

  /* ── 数据与用量 ── */
  globalStats: (days?: number | null): Promise<GlobalSessionStats> =>
    get<GlobalSessionStats>('/session/globalStats', { days }),
  clearAllSessions: (workspaceId?: string | null): Promise<void> =>
    post<void>('/session/clearAll', { workspaceId: workspaceId ?? null }),
};

/**
 * 配置落盘后让宿主重建 Agent，使新配置对下一次对话生效。
 *
 * 失败不阻断保存动作：配置已经写盘，重建只是让当前进程立刻感知。
 */
export async function resetAgent(): Promise<void> {
  try {
    await post('/agent/reset', {});
  } catch {
    /* 配置已保存；重建失败在下一次发送时自然重试 */
  }
}