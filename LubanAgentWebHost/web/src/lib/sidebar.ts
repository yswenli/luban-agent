import { createSignal, onCleanup, onMount } from 'solid-js';

/**
 * 左侧栏的宽度与收起态。
 *
 * 都是模块级信号：进出会话页会卸载页面组件，状态留在模块里才不会回到默认值。
 */

const WIDTH_STORAGE_KEY = 'luban.web.sideWidth';
const COLLAPSED_STORAGE_KEY = 'luban.web.sidebarCollapsed';
const EXPANDED_PROJECTS_STORAGE_KEY = 'luban.web.expandedProjects';
const SELECTED_PROJECT_STORAGE_KEY = 'luban.web.selectedProject';

/** 宽度区间：再窄放不下项目名与徽标；上限按分栏容器比例算，见 {@link SIDE_MAX_RATIO}。 */
export const SIDE_MIN_WIDTH = 160;
export const SIDE_DEFAULT_WIDTH = 200;

/** 侧栏最宽占分栏容器的 40%（左:右 = 4:6），再往右拖不再生效。 */
export const SIDE_MAX_RATIO = 0.4;

/** 视口宽度信号：窗口缩放时同步；DOM 未渲染时作兜底分母，并驱动 aria 重算。 */
const [viewportWidth, setViewportWidth] = createSignal(window.innerWidth);

/** 分栏容器宽度 = 拖拽条的直接父元素（比视口少了应用内边距与外壳边框）。 */
function splitContainerWidth(fallback: number): number {
  const host = document.querySelector('.side-resizer')?.parentElement;
  return host instanceof HTMLElement && host.clientWidth > 0 ? host.clientWidth : fallback;
}

/** 当前分栏下侧栏可拖到的最大宽度（不低于最小宽度）。 */
export function maxSideWidth(): number {
  // 无条件读一次信号：窗口变化后 aria-valuemax 才会重算（容器宽度本身不是响应式的）
  const viewport = viewportWidth();
  return Math.max(SIDE_MIN_WIDTH, Math.round(splitContainerWidth(viewport) * SIDE_MAX_RATIO));
}

export function clampSideWidth(value: number): number {
  if (!Number.isFinite(value)) return SIDE_DEFAULT_WIDTH;
  return Math.min(maxSideWidth(), Math.max(SIDE_MIN_WIDTH, Math.round(value)));
}

function readStoredWidth(): number {
  try {
    const raw = window.localStorage.getItem(WIDTH_STORAGE_KEY);
    return raw === null ? SIDE_DEFAULT_WIDTH : clampSideWidth(Number(raw));
  } catch {
    /* 隐私模式读不到 localStorage：退回默认值即可 */
    return SIDE_DEFAULT_WIDTH;
  }
}

/** 用户拖到的宽度：窗口变窄只压缩显示值，放宽后恢复到这一档。 */
let preferredWidth = readStoredWidth();

const [sideWidth, setSideWidthSignal] = createSignal(clampSideWidth(preferredWidth));
const [sideDragging, setSideDraggingSignal] = createSignal(false);
const [sidebarCollapsed, setSidebarCollapsed] = createSignal(readStoredCollapsed());

function syncWidthToViewport(): void {
  setViewportWidth(window.innerWidth);
  // 拖拽中不改写信号：pointermove 按「起始宽度 + 位移」重算，这里夹一次会让下一次移动跳变
  if (sideDragging()) return;
  const clamped = clampSideWidth(preferredWidth);
  if (clamped !== sideWidth()) setSideWidthSignal(clamped);
}

window.addEventListener('resize', syncWidthToViewport);
// 模块加载时 DOM 还没渲染，上面的初值按视口粗算；首帧后按分栏容器的真实宽度再夹一次
requestAnimationFrame(syncWidthToViewport);
// Vite HMR 重新执行本模块时摘掉旧监听，否则同一 resize 会叠加多个回调
import.meta.hot?.dispose(() => window.removeEventListener('resize', syncWidthToViewport));

export { sideDragging, sidebarCollapsed, sideWidth };

/**
 * 拖动 / 键盘调整只改信号，松手才落盘（见 SideResizer），避免每帧写 localStorage。
 * 记录的是用户意图宽度，显示值再按当前上限夹一次。
 */
export function setSideWidth(value: number): void {
  if (!Number.isFinite(value)) return;
  preferredWidth = Math.max(SIDE_MIN_WIDTH, Math.round(value));
  setSideWidthSignal(clampSideWidth(preferredWidth));
}

export function setSideDragging(value: boolean): void {
  setSideDraggingSignal(value);
}

export function persistSideWidth(): void {
  try {
    window.localStorage.setItem(WIDTH_STORAGE_KEY, String(sideWidth()));
  } catch {
    /* 隐私模式或配额满：只影响刷新后恢复，不影响本次拖拽 */
  }
}

/** 双击或按 Home：宽度回到默认值。 */
export function resetSideWidth(): void {
  preferredWidth = SIDE_DEFAULT_WIDTH;
  setSideWidthSignal(clampSideWidth(preferredWidth));
  persistSideWidth();
}

function readStoredCollapsed(): boolean {
  try {
    return window.localStorage.getItem(COLLAPSED_STORAGE_KEY) === '1';
  } catch {
    return false;
  }
}

export function toggleSidebarCollapsed(): void {
  const next = !sidebarCollapsed();
  setSidebarCollapsed(next);
  try {
    window.localStorage.setItem(COLLAPSED_STORAGE_KEY, next ? '1' : '0');
  } catch {
    /* 只影响刷新后恢复 */
  }
}

/**
 * 左栏项目二级菜单的展开集合与选中项目。
 *
 * 都是纯前端状态：多个标签页各记各的，与后端无关。
 * 解析失败（隐私模式、旧格式）一律按默认值处理，不让损坏的 localStorage 卡住首屏。
 */
export function readExpandedProjects(): string[] {
  try {
    const raw = window.localStorage.getItem(EXPANDED_PROJECTS_STORAGE_KEY);
    if (!raw) return [];
    const value = JSON.parse(raw) as unknown;
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
  } catch {
    /* 隐私模式读不到：按全部收起处理 */
    return [];
  }
}

export function persistExpandedProjects(projectIds: string[]): void {
  try {
    window.localStorage.setItem(EXPANDED_PROJECTS_STORAGE_KEY, JSON.stringify(projectIds));
  } catch {
    /* 只影响刷新后恢复 */
  }
}

export function readSelectedProject(): string | null {
  try {
    const raw = window.localStorage.getItem(SELECTED_PROJECT_STORAGE_KEY);
    return raw && raw.length > 0 ? raw : null;
  } catch {
    return null;
  }
}

export function persistSelectedProject(projectId: string | null): void {
  try {
    if (projectId) window.localStorage.setItem(SELECTED_PROJECT_STORAGE_KEY, projectId);
    else window.localStorage.removeItem(SELECTED_PROJECT_STORAGE_KEY);
  } catch {
    /* 只影响刷新后恢复 */
  }
}

/** 窄屏侧栏是横排 chip（见 styles.css 的 max-width: 760px 断点），横向不占正文宽度，收起只会藏掉标签。 */
const NARROW_SIDEBAR_QUERY = '(max-width: 760px)';

export function useNarrowViewport(): () => boolean {
  const [narrow, setNarrow] = createSignal(window.matchMedia(NARROW_SIDEBAR_QUERY).matches);
  onMount(() => {
    const query = window.matchMedia(NARROW_SIDEBAR_QUERY);
    const onChange = (event: MediaQueryListEvent) => setNarrow(event.matches);
    setNarrow(query.matches);
    query.addEventListener('change', onChange);
    onCleanup(() => query.removeEventListener('change', onChange));
  });
  return narrow;
}