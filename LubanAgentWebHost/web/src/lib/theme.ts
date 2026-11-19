import { createEffect, createMemo, createRoot, createSignal } from 'solid-js';

/**
 * 主题偏好：跟随系统 / 深色 / 浅色。
 *
 * 存储键与三态字面量在 iframe 里无法 import 本模块，宿主 `web/index.html` 与云效插件页
 * `web/index.html` 各有一份首屏内联脚本副本；改键名或枚举时必须三处同步。
 */
export type ThemePreference = 'follow' | 'dark' | 'light';
export type ResolvedTheme = 'dark' | 'light';

const STORAGE_KEY = 'luban.web.theme';
const DARK_QUERY = '(prefers-color-scheme: dark)';

function isPreference(value: string | null): value is ThemePreference {
  return value === 'follow' || value === 'dark' || value === 'light';
}

function readPreference(): ThemePreference {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    // 存量用户没有这个键：按「跟随系统」处理，不写回存储
    return isPreference(raw) ? raw : 'follow';
  } catch {
    return 'follow';
  }
}

const media = typeof window.matchMedia === 'function' ? window.matchMedia(DARK_QUERY) : null;

const [themePreference, setPreference] = createSignal<ThemePreference>(readPreference());
const [systemPrefersDark, setSystemPrefersDark] = createSignal(media?.matches ?? false);

// 只注册一次；显式选了深色/浅色时 resolvedTheme 不读这个信号，系统改色不会覆盖用户选择
media?.addEventListener('change', (event) => setSystemPrefersDark(event.matches));

export const resolvedTheme = createMemo<ResolvedTheme>(() => {
  const preference = themePreference();
  if (preference === 'follow') return systemPrefersDark() ? 'dark' : 'light';
  return preference;
});

export { themePreference };

/**
 * 把当前主题写到指定文档：宿主传 `document`，插件页传 iframe 的 `contentDocument`。
 *
 * `colorScheme` 同步写内联样式，让原生控件、滚动条与表单默认样式跟着走；跨源或无文档时判空返回。
 */
export function applyThemeTo(doc: Document | null | undefined): void {
  if (!doc) return;
  const root = doc.documentElement;
  // iframe 文档在解析完成前 documentElement 可能为空，此时交给 onLoad / 下一次 effect 处理
  if (!root) return;
  const mode = resolvedTheme();
  root.dataset.theme = mode;
  root.style.colorScheme = mode;
}

export function setThemePreference(next: ThemePreference): void {
  setPreference(next);
  try {
    window.localStorage.setItem(STORAGE_KEY, next);
  } catch {
    /* 隐私模式或配额满：本次仍然生效，只是刷新后回到上次可用值 */
  }
}

// 首绘前的属性由 index.html 内联脚本先写好，这里兜住模块加载后的首次同步
applyThemeTo(document);

// 全局唯一订阅点：主题变化立刻写根元素，组件不需要各自挂 effect
createRoot(() => {
  createEffect(() => applyThemeTo(document));
});