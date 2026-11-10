import { get, post } from './api';
import type {
  A2AClient,
  A2AClientListItem,
  A2AServer,
  McpFile,
  ModelItem,
  ProviderListItem,
  ProviderModels,
  RuleFile,
  SkillFile,
} from './types';

/**
 * 设置页接口封装。
 *
 * 作用域一律用字符串：`global` 表示全局目录（`~/.luban-agent`），
 * 其余值当作工作区标识（服务端换算成该工作区根目录）。
 */
export type ScopeValue = string;

interface ListResponse<T> {
  items: T[];
}

export const settingsApi = {
  /* ── 技能 ── */
  listSkills: async (scope: ScopeValue): Promise<string[]> =>
    (await get<ListResponse<string>>('/skill/list', { scope }))?.items ?? [],
  readSkill: (scope: ScopeValue, key: string): Promise<SkillFile> =>
    get<SkillFile>('/skill/read', { scope, key }),
  saveSkill: (body: Record<string, unknown>): Promise<{ key: string }> =>
    post<{ key: string }>('/skill/save', body),
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
  saveProvider: (body: Record<string, unknown>): Promise<ProviderListItem> =>
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