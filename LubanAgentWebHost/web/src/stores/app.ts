import { createSignal } from 'solid-js';

import { get, post } from '../lib/api';
import {
  persistExpandedProjects,
  persistSelectedProject,
  readExpandedProjects,
  readSelectedProject,
} from '../lib/sidebar';
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

/**
 * 「未归属」虚拟项目：旧会话没有 `workspaceId`，不能凭空归到某个真实项目，
 * 在左栏单列一个节点供查看与删除。它不是工作区，不参与排序，也不能新建会话。
 */
export const UNASSIGNED_PROJECT = '__unassigned__';

/** 左栏选中的项目；`UNASSIGNED_PROJECT` 表示「未归属」节点。 */
const [selectedProjectId, setSelectedProjectId] = createSignal<string | null>(readSelectedProject());

/** 左栏已展开的项目集合（可同时展开多个）。 */
const [expandedProjects, setExpandedProjects] = createSignal<string[]>(readExpandedProjects());

/** 「查看全部」触发一次右栏滚到会话区顶部。 */
const [revealSessionsToken, setRevealSessionsToken] = createSignal(0);

/** 顶栏点搜索时递增，左栏据此把焦点放回搜索框。 */
const [searchFocusToken, setSearchFocusToken] = createSignal(0);

/**
 * 已消费的搜索聚焦请求水位。
 *
 * 会话页与首页都会挂载左栏，若各自都在 effect 里响应 token，切页后每次挂载都会抢焦点。
 * 由首页左栏通过 {@link consumeSearchFocus} 取走一次；同一 token 只生效一次。
 */
let handledSearchFocusToken = 0;

/** 左栏搜索关键词：同时匹配项目名与会话标题。 */
const [projectQuery, setProjectQuery] = createSignal('');

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
  selectedProjectId,
  expandedProjects,
  revealSessionsToken,
  projectQuery,
  setProjectQuery,
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
    setSelection(data.selection ?? null);
    selectValidProject();
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
    // 会话归属变了（删除项目、首次加载）后重新校验左栏选中项
    selectValidProject();
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

/** 重新拉取工作区列表并回写 bootstrap（新建/删除项目后需要）。 */
export async function refreshWorkspaces(): Promise<void> {
  try {
    const list = await get<Workspace[]>('/workspace/list');
    setBootstrap((current) => (current ? { ...current, workspaces: list ?? [] } : current));
    selectValidProject();
  } catch (error) {
    showToast((error as Error).message, 'error');
  }
}

/**
 * 左栏选中项目。
 *
 * 只改前端选中态，不改变展开集合——展开/收起由 `toggleProjectExpanded`
 * 单独维护，这样「收起但保持选中」也能成立。
 */
export function selectProject(workspaceId: string | null): void {
  if (selectedProjectId() === workspaceId) return;
  setSelectedProjectId(workspaceId);
  persistSelectedProject(workspaceId);
}

/** 展开某个项目的会话子树。 */
export function expandProject(workspaceId: string): void {
  if (expandedProjects().includes(workspaceId)) return;
  const next = [...expandedProjects(), workspaceId];
  setExpandedProjects(next);
  persistExpandedProjects(next);
}

/** 切换项目的展开态（项目行点击 / chevron 共用）。 */
export function toggleProjectExpanded(workspaceId: string): void {
  const current = expandedProjects();
  const next = current.includes(workspaceId)
    ? current.filter((id) => id !== workspaceId)
    : [...current, workspaceId];
  setExpandedProjects(next);
  persistExpandedProjects(next);
}

/** 「查看全部」：选中并展开该项目，同时请右栏滚到会话区顶部。 */
export function revealProjectSessions(workspaceId: string): void {
  selectProject(workspaceId);
  expandProject(workspaceId);
  setRevealSessionsToken((value) => value + 1);
}

/** 顶栏搜索入口：回到首页并把焦点交给搜索框。 */
export function focusSessionSearch(): void {
  setSearchFocusToken((value) => value + 1);
}

/**
 * 首页左栏取走一次搜索聚焦请求。
 *
 * 返回 true 表示这是尚未处理过的新请求；调用方随后展开侧栏并聚焦输入框。
 */
export function consumeSearchFocus(): boolean {
  const token = searchFocusToken();
  if (token === 0 || token === handledSearchFocusToken) return false;
  handledSearchFocusToken = token;
  return true;
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
      // 新项目直接选中并展开，和旧行为一致（旧行为还会把后端指针切过去）
      selectProject(workspaceId);
      expandProject(workspaceId);
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
 * 删的正好是选中项时回落第一个剩余项目（没有项目则落到「未归属」，再没有就清空选中）；
 * 同时把它从展开集合里摘掉，避免下次新建同名项目时莫名处于展开态。
 */
export async function deleteWorkspace(workspaceId: string): Promise<boolean> {
  try {
    await post('/workspace/delete', { workspaceId });
  } catch (error) {
    showToast((error as Error).message, 'error');
    return false;
  }

  if (expandedProjects().includes(workspaceId)) {
    const next = expandedProjects().filter((id) => id !== workspaceId);
    setExpandedProjects(next);
    persistExpandedProjects(next);
  }
  if (selectedProjectId() === workspaceId) {
    setSelectedProjectId(null);
    persistSelectedProject(null);
  }

  await refreshWorkspaces();
  await refreshSessions();
  // 会话列表变了，未归属回落的判断要用最新数据再算一次
  selectValidProject();
  showToast(t('app.toast.projectDeleted'), 'success');
  return true;
}

export async function createSession(title?: string): Promise<string | null> {
  const workspaceId = activeProjectId();
  if (!workspaceId) {
    showToast(t('sidebar.selectProjectFirst'), 'info');
    return null;
  }
  return createSessionInWorkspace(workspaceId, title);
}

/**
 * 在指定工作区新建会话。
 *
 * 知识库会话归属知识库工作区，但首页不展示知识库项目；传显式 `workspaceId`
 * 可保证会话落进目标项目，不会误落到首页当前选中的项目。
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

/* ── 项目 → 会话二级菜单 ─────────────────────────────────────────────── */

/** 当前选中项目；选中「未归属」或未选中时返回 null（不能作为新会话的归属）。 */
export function activeProjectId(): string | null {
  const id = selectedProjectId();
  return id && id !== UNASSIGNED_PROJECT ? id : null;
}

/** 当前选中的项目对象；未选中或选中「未归属」时返回 null。 */
export function selectedProject(): Workspace | null {
  const id = activeProjectId();
  if (!id) return null;
  return (bootstrap()?.workspaces ?? []).find((item) => item.workspaceId === id) ?? null;
}

/** 会话排序键：优先更新时间。 */
function sessionOrder(session: SessionSummary): string {
  return session.updatedAt ?? session.createdAt;
}

/**
 * 某项目下的会话，按最近更新倒序。
 *
 * `null` 表示未归属：只取没有 `workspaceId` 的会话。知识库会话始终排除。
 */
export function sessionsOfProject(workspaceId: string | null): SessionSummary[] {
  const wiki = wikiWorkspaceIds();
  return sessions()
    .filter((session) => {
      if (session.workspaceId && wiki.has(session.workspaceId)) return false;
      return workspaceId ? session.workspaceId === workspaceId : !session.workspaceId;
    })
    .sort((left, right) => sessionOrder(right).localeCompare(sessionOrder(left)));
}

/** 没有归属任何项目的会话（左栏「未归属」节点）。 */
export function unassignedSessions(): SessionSummary[] {
  return sessionsOfProject(null);
}

/** 会话的展示标题（未命名时用占位文案，搜索与列表同源）。 */
export function sessionTitle(session: { title?: string | null }): string {
  return session.title?.trim() || t('topbar.unnamedSession');
}

/** 项目名（未命名时回落到根路径，和侧栏展示一致）。 */
export function projectName(workspace: Workspace): string {
  return workspace.name?.trim() || workspace.rootPath;
}

/** 左栏一个项目节点：命中搜索时只带命中的会话。 */
export interface VisibleProject {
  project: Workspace;
  /** 该项目在左栏展示的会话；搜索时已按标题过滤。 */
  sessions: SessionSummary[];
  /** 项目名本身命中：项目与其全部会话都保留。 */
  matchedByName: boolean;
}

/**
 * 左栏项目树（已被搜索过滤）。
 *
 * 无关键词时返回全部项目与各自会话；有关键词时：
 * 项目名命中 → 项目与其全部会话都保留；仅会话标题命中 → 只保留命中的会话。
 * 两者都没命中的项目整行隐藏，避免左栏出现一串空项目。
 */
export function visibleProjects(): VisibleProject[] {
  const keyword = projectQuery().trim().toLowerCase();
  const result: VisibleProject[] = [];

  for (const project of projectWorkspaces()) {
    const all = sessionsOfProject(project.workspaceId);
    if (!keyword) {
      result.push({ project, sessions: all, matchedByName: false });
      continue;
    }

    const matchedByName = projectName(project).toLowerCase().includes(keyword);
    if (matchedByName) {
      result.push({ project, sessions: all, matchedByName });
      continue;
    }

    const hits = all.filter((session) => sessionTitle(session).toLowerCase().includes(keyword));
    if (hits.length > 0) result.push({ project, sessions: hits, matchedByName: false });
  }

  return result;
}

/** 左栏「未归属」节点：搜索时按节点名或会话标题过滤。 */
export interface VisibleUnassigned {
  visible: boolean;
  sessions: SessionSummary[];
}

export function visibleUnassigned(): VisibleUnassigned {
  const all = unassignedSessions();
  const keyword = projectQuery().trim().toLowerCase();
  if (!keyword) return { visible: all.length > 0, sessions: all };
  if (t('sidebar.unassigned').toLowerCase().includes(keyword)) return { visible: true, sessions: all };

  const hits = all.filter((session) => sessionTitle(session).toLowerCase().includes(keyword));
  return { visible: hits.length > 0, sessions: hits };
}

/**
 * 选中项失效时回落：先取第一个真实项目，再取「未归属」，都没有就清空。
 *
 * 首次进入（localStorage 为空）、项目被删、只剩未归属会话都会走到这里。
 */
function selectValidProject(): void {
  const current = selectedProjectId();
  const projects = projectWorkspaces();
  if (current === UNASSIGNED_PROJECT) {
    if (unassignedSessions().length > 0) return;
  } else if (current && projects.some((project) => project.workspaceId === current)) {
    return;
  }

  const fallback = projects[0]?.workspaceId ?? (unassignedSessions().length > 0 ? UNASSIGNED_PROJECT : null);
  if (fallback === current) return;
  setSelectedProjectId(fallback);
  persistSelectedProject(fallback);
}
