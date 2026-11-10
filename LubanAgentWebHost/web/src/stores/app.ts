import { createSignal } from 'solid-js';

import { get, post } from '../lib/api';
import type { Bootstrap, SessionSummary, WebCommand, Workspace } from '../lib/types';
import { forgetSession, pruneSessions } from './tabs';

export interface ToastItem {
  id: number;
  kind: 'info' | 'success' | 'error';
  text: string;
}

const [bootstrap, setBootstrap] = createSignal<Bootstrap | null>(null);
const [booting, setBooting] = createSignal(true);
const [bootError, setBootError] = createSignal<string | null>(null);

const [sessions, setSessions] = createSignal<SessionSummary[]>([]);
const [sessionsLoading, setSessionsLoading] = createSignal(false);
const [sessionsError, setSessionsError] = createSignal<string | null>(null);

const [commands, setCommands] = createSignal<WebCommand[]>([]);

const [currentWorkspaceId, setCurrentWorkspaceId] = createSignal<string | null>(null);

/**
 * 首页左栏选中的项目（仅前端筛选）。
 *
 * 与后端「当前工作区」分开保存：筛选只影响右侧列表，切换当前工作区另由
 * `selectProject` 负责，两者在同一动作里更新以保持新建会话归属一致。
 */
const [filterWorkspaceId, setFilterWorkspaceId] = createSignal<string | null>(null);

/** 顶栏点搜索时递增，首页据此把焦点放回搜索框。 */
const [searchFocusToken, setSearchFocusToken] = createSignal(0);

const [selection, setSelection] = createSignal<{ providerId?: string | null; modelId?: string | null } | null>(null);

/** 每轮下发参数：权限模式默认 default，推理强度空串表示「跟随模型」。 */
const [permissionModeKey, setPermissionModeKey] = createSignal('default');
const [reasoningEffort, setReasoningEffort] = createSignal('');

const [toasts, setToasts] = createSignal<ToastItem[]>([]);
let toastSeed = 0;

export {
  bootstrap,
  booting,
  bootError,
  sessions,
  sessionsLoading,
  sessionsError,
  commands,
  currentWorkspaceId,
  filterWorkspaceId,
  searchFocusToken,
  selection,
  permissionModeKey,
  setPermissionModeKey,
  reasoningEffort,
  setReasoningEffort,
  toasts,
};

export function showToast(text: string, kind: ToastItem['kind'] = 'info'): void {
  const id = ++toastSeed;
  // 只留最近 3 条：设置页连续保存时否则会堆成一面墙，盖住右侧操作区。
  setToasts((list) => [...list, { id, kind, text }].slice(-3));
  window.setTimeout(() => {
    setToasts((list) => list.filter((item) => item.id !== id));
  }, kind === 'error' ? 5200 : 3000);
}

export async function loadBootstrap(): Promise<void> {
  setBooting(true);
  setBootError(null);
  try {
    const data = await get<Bootstrap>('/system/bootstrap');
    setBootstrap(data);
    setCurrentWorkspaceId(data.currentWorkspaceId ?? null);
    setSelection(data.selection ?? null);
    if (data.permissionModes.length > 0 && !data.permissionModes.some((mode) => mode.key === permissionModeKey())) {
      setPermissionModeKey(data.permissionModes[0].key);
    }
  } catch (error) {
    setBootError((error as Error).message);
  } finally {
    setBooting(false);
  }
}

export async function refreshSessions(): Promise<void> {
  setSessionsLoading(true);
  setSessionsError(null);
  try {
    const list = (await get<SessionSummary[]>('/session/listDetailed')) ?? [];
    setSessions(list);
    // 已删除的会话不应继续留在顶栏标签里
    pruneSessions(list.map((session) => session.sessionId));
  } catch (error) {
    setSessionsError((error as Error).message);
  } finally {
    setSessionsLoading(false);
  }
}

export async function loadCommands(): Promise<void> {
  if (commands().length > 0) return;
  try {
    const list = await get<WebCommand[]>('/system/commands');
    setCommands(list ?? []);
  } catch {
    /* 命令清单拿不到不影响对话 */
  }
}

/** 重新拉取工作区列表并回写 bootstrap（新建项目后需要）。 */
export async function refreshWorkspaces(): Promise<void> {
  try {
    const list = await get<Workspace[]>('/workspace/list');
    setBootstrap((current) => (current ? { ...current, workspaces: list ?? [] } : current));
  } catch (error) {
    showToast((error as Error).message, 'error');
  }
}

export async function switchWorkspace(workspaceId: string): Promise<void> {
  try {
    await post('/workspace/switch', { workspaceId });
    setCurrentWorkspaceId(workspaceId);
    showToast('已切换工作区', 'success');
  } catch (error) {
    showToast((error as Error).message, 'error');
  }
}

/**
 * 首页左栏点项目：筛选右侧列表并把该项目设为当前工作区。
 *
 * 再点一次已选中的项目则取消筛选（不改回后端工作区，避免来回切换）。
 */
export async function selectProject(workspaceId: string): Promise<void> {
  if (filterWorkspaceId() === workspaceId) {
    setFilterWorkspaceId(null);
    return;
  }

  setFilterWorkspaceId(workspaceId);
  if (workspaceId !== currentWorkspaceId()) {
    await switchWorkspace(workspaceId);
  }
}

/** 清除项目筛选，恢复展示全部会话。 */
export function clearProjectFilter(): void {
  setFilterWorkspaceId(null);
}

/** 顶栏搜索入口：回到首页并把焦点交给搜索框。 */
export function focusSessionSearch(): void {
  setSearchFocusToken((value) => value + 1);
}

/**
 * 新建工作区并切换过去。
 *
 * 后端要求绝对路径且类型限定 Normal/LlmWiki；网页只创建普通项目。
 */
export async function createWorkspace(rootPath: string, name?: string): Promise<boolean> {
  try {
    const workspace = await post<{ workspaceId?: string } | null>('/workspace/create', {
      name: name?.trim() || null,
      rootPath: rootPath.trim(),
      type: 'Normal',
    });
    await refreshWorkspaces();
    const workspaceId = workspace?.workspaceId;
    if (workspaceId) {
      await post('/workspace/switch', { workspaceId });
      setCurrentWorkspaceId(workspaceId);
      // 新项目还没有会话；清掉旧筛选，避免右侧空白让人误以为列表坏了
      setFilterWorkspaceId(null);
    }
    showToast('已创建项目', 'success');
    return true;
  } catch (error) {
    showToast((error as Error).message, 'error');
    return false;
  }
}

export async function createSession(title?: string): Promise<string | null> {
  try {
    const session = await post<{ sessionId?: string } | null>('/session/create', {
      title: title ?? null,
      workspaceId: currentWorkspaceId(),
    });
    const sessionId = session?.sessionId ?? null;
    await refreshSessions();
    return sessionId;
  } catch (error) {
    showToast((error as Error).message, 'error');
    return null;
  }
}

export async function renameSession(sessionId: string, title: string): Promise<boolean> {
  try {
    await post('/session/rename', { sessionId, title });
    await refreshSessions();
    showToast('已重命名', 'success');
    return true;
  } catch (error) {
    showToast((error as Error).message, 'error');
    return false;
  }
}

export async function deleteSession(sessionId: string): Promise<boolean> {
  try {
    await post('/session/delete', { sessionId });
    forgetSession(sessionId);
    await refreshSessions();
    showToast('已删除会话', 'success');
    return true;
  } catch (error) {
    showToast((error as Error).message, 'error');
    return false;
  }
}

export async function selectModel(providerId: string, modelId: string): Promise<void> {
  try {
    const data = await post<{ providerId?: string; modelId?: string }>('/provider/select', {
      providerId,
      modelId,
    });
    setSelection(data ?? { providerId, modelId });
    showToast('已切换模型（全局生效）', 'success');
  } catch (error) {
    showToast((error as Error).message, 'error');
  }
}

/**
 * 补齐某供应商的模型列表。
 *
 * 首屏 bootstrap 只读该用户的模型缓存，全新环境里缓存为空，下拉会没有任何模型；
 * 因此在用户展开模型下拉时按供应商按需刷新一次（有 API Key 才会真正发请求）。
 */
export async function refreshProviderModels(providerId: string): Promise<void> {
  try {
    const data = await get<{ providerId: string; models: string[] }>(
      `/provider/models?providerId=${encodeURIComponent(providerId)}`,
    );
    const models = data?.models ?? [];
    setBootstrap((current) => {
      if (!current) return current;
      return {
        ...current,
        providers: current.providers.map((provider) =>
          provider.id === providerId ? { ...provider, models } : provider,
        ),
      };
    });
  } catch (error) {
    showToast(`模型列表拉取失败：${(error as Error).message}`, 'error');
  }
}

/** 按 Default → Plan → AcceptEdits → Bypass 循环（与 `PermissionModeRules.Next` 同序）。 */
export function cyclePermissionMode(): void {
  const modes = bootstrap()?.permissionModes ?? [];
  if (modes.length === 0) return;
  const index = modes.findIndex((mode) => mode.key === permissionModeKey());
  const next = modes[(index + 1 + modes.length) % modes.length];
  setPermissionModeKey(next.key);
}

export function permissionModeLabel(): string {
  const modes = bootstrap()?.permissionModes ?? [];
  return modes.find((mode) => mode.key === permissionModeKey())?.displayName ?? '默认 (Default)';
}

export function currentModelLabel(): string {
  const selected = selection();
  if (!selected?.providerId || !selected.modelId) return '未选择模型';
  const provider = bootstrap()?.providers.find((item) => item.id === selected.providerId);
  return `${provider?.displayName ?? selected.providerId} / ${selected.modelId}`;
}

export function workspaceName(workspaceId: string | null | undefined): string {
  if (!workspaceId) return '';
  return bootstrap()?.workspaces.find((item) => item.workspaceId === workspaceId)?.name ?? '';
}