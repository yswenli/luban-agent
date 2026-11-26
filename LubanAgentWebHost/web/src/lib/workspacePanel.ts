import { createSignal } from 'solid-js';

/**
 * 右侧「工作区」面板的宽度、开合与树状态。
 *
 * 宽度与开合是模块级信号：进出会话页会卸载页面组件，状态留在模块里才不会回到默认值。
 * 展开的目录与选中的文件按会话记在内存里（刷新页面即恢复默认），不落 localStorage：
 * 工作区内容本身随时会变，持久化一份旧路径只会让人点到「文件不存在」。
 */

const WIDTH_STORAGE_KEY = 'luban.web.workspaceWidth';
const OPEN_STORAGE_KEY = 'luban.web.workspaceOpen';
const IGNORED_STORAGE_KEY = 'luban.web.workspaceIgnored';

/** 面板宽度区间：再窄放不下文件树与预览两列。 */
export const WORKSPACE_MIN_WIDTH = 280;
export const WORKSPACE_DEFAULT_WIDTH = 420;

/** 面板最宽占对话区容器的 65%，再往左拖不再生效，给对话区留出可读宽度。 */
export const WORKSPACE_MAX_RATIO = 0.65;

/** 视口宽度信号：窗口缩放时同步；DOM 未渲染时作兜底分母，并驱动 aria 重算。 */
const [viewportWidth, setViewportWidth] = createSignal(window.innerWidth);

/** 分栏容器宽度 = 面板的父元素 `.chat-body` 的实际宽度。 */
function splitContainerWidth(fallback: number): number {
  const host = document.querySelector('.chat-body');
  return host instanceof HTMLElement && host.clientWidth > 0 ? host.clientWidth : fallback;
}

/** 当前容器下工作区面板可拖到的最大宽度（不低于最小宽度）。 */
export function maxWorkspaceWidth(): number {
  // 无条件读一次信号：窗口变化后 aria-valuemax 才会重算（容器宽度本身不是响应式的）
  const viewport = viewportWidth();
  return Math.max(WORKSPACE_MIN_WIDTH, Math.round(splitContainerWidth(viewport) * WORKSPACE_MAX_RATIO));
}

export function clampWorkspaceWidth(value: number): number {
  if (!Number.isFinite(value)) return WORKSPACE_DEFAULT_WIDTH;
  return Math.min(maxWorkspaceWidth(), Math.max(WORKSPACE_MIN_WIDTH, Math.round(value)));
}

function readStoredWidth(): number {
  try {
    const raw = window.localStorage.getItem(WIDTH_STORAGE_KEY);
    return raw === null ? WORKSPACE_DEFAULT_WIDTH : clampWorkspaceWidth(Number(raw));
  } catch {
    /* 隐私模式读不到 localStorage：退回默认值即可 */
    return WORKSPACE_DEFAULT_WIDTH;
  }
}

function readStoredFlag(key: string): boolean {
  try {
    return window.localStorage.getItem(key) === '1';
  } catch {
    return false;
  }
}

function writeStoredFlag(key: string, value: boolean): void {
  try {
    window.localStorage.setItem(key, value ? '1' : '0');
  } catch {
    /* 只影响刷新后恢复 */
  }
}

/** 用户拖到的宽度：窗口变窄只压缩显示值，放宽后恢复到这一档。 */
let preferredWidth = readStoredWidth();

const [workspaceWidth, setWorkspaceWidthSignal] = createSignal(clampWorkspaceWidth(preferredWidth));
const [workspaceDragging, setWorkspaceDraggingSignal] = createSignal(false);
const [workspaceOpen, setWorkspaceOpenSignal] = createSignal(readStoredFlag(OPEN_STORAGE_KEY));
const [workspaceShowIgnored, setWorkspaceShowIgnoredSignal] = createSignal(readStoredFlag(IGNORED_STORAGE_KEY));

function syncWidthToViewport(): void {
  setViewportWidth(window.innerWidth);
  // 拖拽中不改写信号：pointermove 按「起始宽度 + 位移」重算，这里夹一次会让下一次移动跳变
  if (workspaceDragging()) return;
  const clamped = clampWorkspaceWidth(preferredWidth);
  if (clamped !== workspaceWidth()) setWorkspaceWidthSignal(clamped);
}

window.addEventListener('resize', syncWidthToViewport);
// 模块加载时 DOM 还没渲染，上面的初值按视口粗算；首帧后按分栏容器的真实宽度再夹一次
requestAnimationFrame(syncWidthToViewport);
// Vite HMR 重新执行本模块时摘掉旧监听，否则同一 resize 会叠加多个回调
import.meta.hot?.dispose(() => window.removeEventListener('resize', syncWidthToViewport));

export { workspaceDragging, workspaceOpen, workspaceShowIgnored, workspaceWidth };

/** 拖动 / 键盘调整只改信号，松手才落盘（见 SideResizer），避免每帧写 localStorage。 */
export function setWorkspaceWidth(value: number): void {
  if (!Number.isFinite(value)) return;
  preferredWidth = Math.max(WORKSPACE_MIN_WIDTH, Math.round(value));
  setWorkspaceWidthSignal(clampWorkspaceWidth(preferredWidth));
}

export function setWorkspaceDragging(value: boolean): void {
  setWorkspaceDraggingSignal(value);
}

export function persistWorkspaceWidth(): void {
  try {
    window.localStorage.setItem(WIDTH_STORAGE_KEY, String(workspaceWidth()));
  } catch {
    /* 隐私模式或配额满：只影响刷新后恢复，不影响本次拖拽 */
  }
}

/** 双击或按 Home：宽度回到默认值。 */
export function resetWorkspaceWidth(): void {
  preferredWidth = WORKSPACE_DEFAULT_WIDTH;
  setWorkspaceWidthSignal(clampWorkspaceWidth(preferredWidth));
  persistWorkspaceWidth();
}

export function toggleWorkspacePanel(): void {
  const next = !workspaceOpen();
  setWorkspaceOpenSignal(next);
  writeStoredFlag(OPEN_STORAGE_KEY, next);
}

export function closeWorkspacePanel(): void {
  setWorkspaceOpenSignal(false);
  writeStoredFlag(OPEN_STORAGE_KEY, false);
}

export function setWorkspaceShowIgnored(value: boolean): void {
  setWorkspaceShowIgnoredSignal(value);
  writeStoredFlag(IGNORED_STORAGE_KEY, value);
}

/* ── 按会话记忆的树状态 ─────────────────────────────────────────────── */

const EMPTY_PATHS: ReadonlySet<string> = new Set();

const expandedBySession = new Map<string, Set<string>>();
const selectedBySession = new Map<string, string>();

/** 展开集合或选中文件变化时自增，供组件建立响应式依赖。 */
const [treeStateVersion, bumpTreeState] = createSignal(0);

export { treeStateVersion };

export function expandedDirsOf(sessionId: string): ReadonlySet<string> {
  // 读一次版本信号：调用点（Show / For / effect）由此建立依赖，展开状态变化才会重渲染
  treeStateVersion();
  return expandedBySession.get(sessionId) ?? EMPTY_PATHS;
}

export function setDirExpanded(sessionId: string, path: string, expanded: boolean): void {
  let set = expandedBySession.get(sessionId);
  if (!set) {
    if (!expanded) return;
    set = new Set();
    expandedBySession.set(sessionId, set);
  }

  if (expanded) set.add(path);
  else set.delete(path);
  bumpTreeState((value) => value + 1);
}

export function selectedFileOf(sessionId: string): string | null {
  // 同 expandedDirsOf：读版本信号，保证预览区与选中高亮跟随点击更新
  treeStateVersion();
  return selectedBySession.get(sessionId) ?? null;
}

export function setSelectedFile(sessionId: string, path: string | null): void {
  if (path) selectedBySession.set(sessionId, path);
  else selectedBySession.delete(sessionId);
  bumpTreeState((value) => value + 1);
}

/** 关闭会话标签时释放它的树状态，避免长期打开大量会话时 Map 一直增长。 */
export function forgetSessionWorkspaceState(sessionId: string): void {
  expandedBySession.delete(sessionId);
  selectedBySession.delete(sessionId);
}