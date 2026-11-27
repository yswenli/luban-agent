import { createSignal } from 'solid-js';

import { get, post } from '../lib/api';
import type { Bootstrap, SessionGenerationSettings, SessionSummary, WebCommand, Workspace } from '../lib/types';
import { forgetSession, pruneSessions } from './tabs';
import { t } from '../i18n';

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

/** 每轮下发参数：权限模式默认 default，思考强度由会话生效值决定。 */
const [permissionModeKey, setPermissionModeKey] = createSignal('default');

/**
 * 各会话生效的思考档位与上下文预算档位（键为会话标识）。
 *
 * 值来自服务端 `agent/sessionSettings`，含该会话模型的可选档位与窗口；
 * 不在表里表示尚未拉取，调用方按「跟随模型」处理。
 */
const [generationSettings, setGenerationSettings] = createSignal<Record<string, SessionGenerationSettings>>({});

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
  generationSettings,
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

/** 未登录时不需要读 bootstrap：启动页收尾由这里负责。 */
export function markBootSkipped(): void {
  setBooting(false);
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

/**
 * 切换后端当前工作区。
 *
 * 成功不提示：首页点项目即触发，属常规导航动作；失败才提示。
 */
export async function switchWorkspace(workspaceId: string): Promise<void> {
  try {
    await post('/workspace/switch', { workspaceId });
    setCurrentWorkspaceId(workspaceId);
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
 * 新建工作区入参。
 *
 * 位置统一为「管理员白名单根目录 + 相对子目录」；`git` 由宿主克隆到该位置，
 * `subPath` 留空时服务端取项目名/仓库名作为子目录。
 */
export interface CreateWorkspaceInput {
  source: 'directory' | 'git';
  rootPath?: string;
  /** 相对根目录的子目录（可多级，使用 `/` 分隔）。 */
  subPath?: string;
  gitUrl?: string;
  name?: string;
  /** 本次填写的 HTTPS 凭据；缺省回落当前用户已保存的同域名凭据。 */
  gitCredential?: { userName?: string; secret?: string } | null;
  /** 创建成功后保存本次凭据（按用户 + 域名覆盖）。 */
  saveCredential?: boolean;
  reuseExisting?: boolean;
  /** 目录已被其他用户登记时，确认共用（服务端先返回 409 提示）。 */
  shareExisting?: boolean;
}

/** 新建工作区结果：失败时带回文案与状态码（409 表示目录已存在或已被他人使用，可确认）。 */
export interface CreateWorkspaceResult {
  ok: boolean;
  code?: number;
  error?: string;
  /** 冲突类型：`exists` 目标目录已存在（复用）；`in-use` 已被其他用户使用（共用）。 */
  reason?: string;
}

/**
 * 新建工作区并切换过去。
 *
 * 后端要求绝对路径且类型限定 Normal/LlmWiki；网页只创建普通项目。
 * 失败不弹 toast：新建弹窗需要把错误（尤其是 409 目录冲突）显示在表单里。
 */
export async function createWorkspace(input: CreateWorkspaceInput): Promise<CreateWorkspaceResult> {
  try {
    const workspace = await post<{ workspaceId?: string } | null>('/workspace/create', {
      name: input.name?.trim() || null,
      source: input.source,
      rootPath: input.rootPath?.trim() || null,
      subPath: input.subPath?.trim() || null,
      gitUrl: input.source === 'git' ? input.gitUrl?.trim() : null,
      gitCredential: input.source === 'git' ? input.gitCredential ?? null : null,
      saveCredential: input.source === 'git' && input.saveCredential === true,
      reuseExisting: input.source === 'git' && input.reuseExisting === true,
      shareExisting: input.shareExisting === true,
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
    showToast(t('app.toast.projectCreated'), 'success');
    return { ok: true };
  } catch (error) {
    const failure = error as { code?: number; message?: string; data?: { reason?: string } };
    return { ok: false, code: failure.code, error: failure.message, reason: failure.data?.reason };
  }
}

/**
 * 重命名工作区：只改显示名，不移动磁盘目录。
 *
 * @param successText 成功提示文案（项目与知识库共用一个接口，提示语按入口区分）。
 */
export async function renameWorkspace(
  workspaceId: string,
  name: string,
  successText = t('app.toast.projectRenamed'),
): Promise<boolean> {
  const trimmed = name.trim();
  if (!trimmed) return false;

  try {
    await post('/workspace/rename', { workspaceId, name: trimmed });
    await refreshWorkspaces();
    showToast(successText, 'success');
    return true;
  } catch (error) {
    showToast((error as Error).message, 'error');
    return false;
  }
}

/**
 * 删除项目（工作区）：物理清理会话、索引与 .luban-agent 配置目录，磁盘根目录及用户文件保持不动。
 *
 * 删的正好是当前工作区时自动切到剩余第一个，避免之后新建的会话落到「未绑定项目」；
 * 若原本正筛选着这个项目，选中态跟随切换后的当前工作区，不留「无选中」的空档。
 */
export async function deleteWorkspace(workspaceId: string): Promise<boolean> {
  const wasFiltered = filterWorkspaceId() === workspaceId;
  try {
    await post('/workspace/delete', { workspaceId });
  } catch (error) {
    showToast((error as Error).message, 'error');
    return false;
  }

  if (wasFiltered) setFilterWorkspaceId(null);

  await refreshWorkspaces();
  if (currentWorkspaceId() === workspaceId) {
    const next = projectWorkspaces()[0] ?? null;
    if (next) {
      await switchWorkspace(next.workspaceId);
    } else {
      setCurrentWorkspaceId(null);
    }
  }
  // 选中态始终跟随当前工作区：左栏「选中」既表示筛选，也表示新建会话归属哪个项目
  if (wasFiltered) setFilterWorkspaceId(currentWorkspaceId());

  await refreshSessions();
  showToast(t('app.toast.projectDeleted'), 'success');
  return true;
}

export async function createSession(title?: string): Promise<string | null> {
  return createSessionInWorkspace(currentWorkspaceId(), title);
}

/**
 * 在指定工作区新建会话，不改变后端「当前工作区」。
 *
 * 知识库会话归属知识库工作区，但首页不展示知识库项目；若复用 `createSession`
 * 先切工作区，之后首页「新建会话」会莫名落进知识库。
 */
export async function createSessionInWorkspace(workspaceId: string | null, title?: string): Promise<string | null> {
  try {
    const session = await post<{ sessionId?: string } | null>('/session/create', {
      title: title ?? null,
      workspaceId,
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
    showToast(t('app.toast.renamed'), 'success');
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
    showToast(t('app.toast.sessionDeleted'), 'success');
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
    showToast(t('app.toast.defaultModelSet'), 'success');
  } catch (error) {
    showToast((error as Error).message, 'error');
  }
}

/**
 * 切换单个会话使用的模型。
 *
 * 只落该会话（`ai_session.Provider/ModelName`），不改全局默认；服务端下一轮按会话解析模型。
 */
export async function selectSessionModel(sessionId: string, providerId: string, modelId: string): Promise<void> {
  try {
    await post<{ providerId?: string; modelId?: string }>('/session/selectModel', {
      sessionId,
      providerId,
      modelId,
    });
    setSessions((list) =>
      list.map((item) => (item.sessionId === sessionId ? { ...item, provider: providerId, modelName: modelId } : item)),
    );
    // 换模型后可选档位与窗口都可能变，重新拉一次生效值；调用方据此判断是否回落
    await loadSessionGeneration(sessionId);
    showToast(t('app.toast.sessionModelSwitched'), 'success');
  } catch (error) {
    showToast((error as Error).message, 'error');
  }
}

/**
 * 取某会话已拉取的生成参数；尚未拉取时为 `null`（调用方按「跟随模型」处理）。
 */
export function sessionGeneration(sessionId: string): SessionGenerationSettings | null {
  return generationSettings()[sessionId] ?? null;
}

/**
 * 拉取会话生效的思考档位与上下文预算档位。
 *
 * 读失败不改动既有值：设置不是对话的前提，报错只影响控件展示。
 */
export async function loadSessionGeneration(sessionId: string): Promise<void> {
  try {
    const data = await get<SessionGenerationSettings>('/agent/sessionSettings', { sessionId });
    setGenerationSettings((map) => ({ ...map, [sessionId]: data }));
  } catch {
    /* 拉不到就保持「跟随模型」，不打断对话 */
  }
}

/**
 * 写入会话生效的思考档位与上下文预算档位，成功后就地刷新生效值。
 *
 * 两个字段按整体替换提交：`''` / `null` 表示清除覆盖、回到跟随模型。
 */
export async function saveSessionGeneration(
  sessionId: string,
  reasoningEffort: string,
  contextTier: number | null,
): Promise<void> {
  try {
    await post('/agent/saveSessionSettings', {
      sessionId,
      reasoningEffort: reasoningEffort || null,
      contextTier,
    });
    await loadSessionGeneration(sessionId);
  } catch (error) {
    showToast((error as Error).message, 'error');
  }
}

/**
 * 刷新全局供应商清单与当前模型选择（供应商保存 / 删除后调用）。
 *
 * 不走 `loadBootstrap()`：它会置 `booting=true`，整页闪回启动页并卸载设置弹层；
 * 这里只回写 bootstrap 里的 providers / selection，顶栏模型名与模型下拉随之更新。
 */
export async function refreshProviders(): Promise<void> {
  try {
    const data = await get<Bootstrap>('/system/bootstrap');
    setBootstrap((current) =>
      current ? { ...current, providers: data.providers ?? [], selection: data.selection ?? null } : current,
    );
    setSelection(data.selection ?? null);
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
    showToast(t('app.toast.modelsFailed', { message: (error as Error).message }), 'error');
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
  return modes.find((mode) => mode.key === permissionModeKey())?.displayName ?? t('permission.defaultMode');
}

/** 会话当前使用的模型：会话单独选过就用会话的，否则跟随全局默认。 */
export function sessionModel(sessionId: string): { providerId?: string | null; modelId?: string | null } | null {
  const session = sessions().find((item) => item.sessionId === sessionId);
  if (session?.provider && session.modelName) {
    return { providerId: session.provider, modelId: session.modelName };
  }
  return selection();
}

/** 会话模型的展示名（供应商显示名 / 模型名）。 */
export function sessionModelLabel(sessionId: string): string {
  const selected = sessionModel(sessionId);
  if (!selected?.providerId || !selected.modelId) return t('app.modelUnselected');
  const provider = bootstrap()?.providers.find((item) => item.id === selected.providerId);
  return `${provider?.displayName ?? selected.providerId} / ${selected.modelId}`;
}

export function workspaceName(workspaceId: string | null | undefined): string {
  if (!workspaceId) return '';
  return bootstrap()?.workspaces.find((item) => item.workspaceId === workspaceId)?.name ?? '';
}

/** 知识库工作区类型（`Workspace.Type`），只在知识库页展示，不进首页项目列表。 */
export const LLM_WIKI_TYPE = 'LlmWiki';

const PROJECT_ORDER_KEY = 'luban.web.projectOrder';

function readProjectOrder(): string[] {
  try {
    const raw = window.localStorage.getItem(PROJECT_ORDER_KEY);
    if (!raw) return [];
    const value = JSON.parse(raw) as unknown;
    return Array.isArray(value)
      ? value.filter((item): item is string => typeof item === 'string')
      : [];
  } catch {
    /* 隐私模式读不到：按后端顺序展示 */
    return [];
  }
}

/**
 * 用户手工拖出来的项目顺序（workspaceId 列表）。
 *
 * 只记录拖过的项目；没登记的新项目按后端顺序（最后活跃时间倒序）排在末尾，
 * 所以新建、删除项目都不会把已有项目挤乱。
 */
const [projectOrder, setProjectOrder] = createSignal<string[]>(readProjectOrder());

/** 首页「项目」列表：排除知识库工作区，并叠加手工拖动顺序。 */
export function projectWorkspaces(): Workspace[] {
  const list = (bootstrap()?.workspaces ?? []).filter((item) => item.type !== LLM_WIKI_TYPE);
  const order = projectOrder();
  if (order.length === 0) return list;

  const rank = new Map(order.map((workspaceId, position) => [workspaceId, position]));
  // 未登记项目统一排在已登记之后；同秩时 sort 稳定，保留后端顺序
  return [...list].sort(
    (left, right) =>
      (rank.get(left.workspaceId) ?? Number.MAX_SAFE_INTEGER) -
      (rank.get(right.workspaceId) ?? Number.MAX_SAFE_INTEGER),
  );
}

/** 拖动项目排序：把项目移到 `toIndex`（移除后的下标），并把当前可见顺序整体写入本地。 */
export function moveProject(workspaceId: string, toIndex: number): void {
  const ids = projectWorkspaces().map((item) => item.workspaceId);
  const from = ids.indexOf(workspaceId);
  if (from < 0) return;

  const next = ids.filter((id) => id !== workspaceId);
  const target = Math.max(0, Math.min(toIndex, next.length));
  if (target === from) return;
  next.splice(target, 0, workspaceId);
  setProjectOrder(next);

  try {
    window.localStorage.setItem(PROJECT_ORDER_KEY, JSON.stringify(next));
  } catch {
    /* 隐私模式或配额满：只影响刷新后恢复 */
  }
}

/** 知识库工作区标识集合：首页会话列表据此隐藏知识库内的会话。 */
export function wikiWorkspaceIds(): Set<string> {
  return new Set(
    (bootstrap()?.workspaces ?? [])
      .filter((item) => item.type === LLM_WIKI_TYPE)
      .map((item) => item.workspaceId),
  );
}