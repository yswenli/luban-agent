import { createSignal, onCleanup } from 'solid-js';

export type Route =
  | { name: 'sessions' }
  | { name: 'chat'; sessionId: string }
  | { name: 'settings'; tab: string }
  | { name: 'wiki' }
  | { name: 'plugins' }
  | { name: 'plugin'; pluginId: string; entryId: string };

/**
 * SAEA.MVC 只按 `/{controller}/{action}` 派发、没有 SPA 回退，
 * 所以路由必须全部放在 hash 里（`#/`、`#/s/<id>`、`#/settings/<tab>`、`#/wiki`、
 * `#/plugins`、`#/plugin/<pluginId>/<entryId>`）。
 */
export function parseHash(hash: string): Route {
  const value = hash.replace(/^#/, '');
  if (!value || value === '/') return { name: 'sessions' };

  const chat = value.match(/^\/s\/([^/?#]+)/);
  if (chat) return { name: 'chat', sessionId: decodeURIComponent(chat[1]) };

  const settings = value.match(/^\/settings(?:\/([^/?#]+))?/);
  // 省略页签时落到导航第一项（账号），与侧栏「设置」入口的预期一致
  if (settings) return { name: 'settings', tab: settings[1] ?? 'account' };

  if (/^\/wiki(?:[/?#]|$)/.test(value)) return { name: 'wiki' };

  if (/^\/plugins(?:[/?#]|$)/.test(value)) return { name: 'plugins' };

  const plugin = value.match(/^\/plugin\/([^/?#]+)\/([^/?#]+)/);
  if (plugin) {
    return {
      name: 'plugin',
      pluginId: decodeURIComponent(plugin[1]),
      entryId: decodeURIComponent(plugin[2]),
    };
  }

  return { name: 'sessions' };
}

export function useRoute() {
  const [route, setRoute] = createSignal<Route>(parseHash(window.location.hash));
  const onChange = () => {
    const next = parseHash(window.location.hash);
    setRoute(next);
    // 设置与插件管理都是浮层：记住打开前的视图，关闭时回到原处而不是固定回首页
    if (next.name !== 'settings' && next.name !== 'plugins') setLastView(next);
  };
  window.addEventListener('hashchange', onChange);
  onCleanup(() => window.removeEventListener('hashchange', onChange));
  return route;
}

export function navigate(path: string): void {
  if (window.location.hash === path) return;
  window.location.hash = path;
}

export function openSession(sessionId: string): void {
  navigate(`#/s/${encodeURIComponent(sessionId)}`);
}

export function openSessions(): void {
  navigate('#/');
}

export function openSettings(tab = 'account'): void {
  navigate(`#/settings/${tab}`);
}

export function openWiki(): void {
  navigate('#/wiki');
}

export function openPlugins(): void {
  navigate('#/plugins');
}

export function openPlugin(pluginId: string, entryId: string): void {
  navigate(`#/plugin/${encodeURIComponent(pluginId)}/${encodeURIComponent(entryId)}`);
}

const isOverlay = (route: Route) => route.name === 'settings' || route.name === 'plugins';

const initialView = parseHash(window.location.hash);
const [lastView, setLastView] = createSignal<Route>(isOverlay(initialView) ? { name: 'sessions' } : initialView);

/** 设置 / 插件管理浮层下方要渲染的视图。 */
export { lastView };

/** 设置浮层关闭后回到打开前的视图（深链接直接打开时回首页）。 */
export function closeSettings(): void {
  closeOverlay();
}

/** 插件管理浮层关闭后回到打开前的视图。 */
export function closePlugins(): void {
  closeOverlay();
}

/**
 * 会话被批量删除（如设置页清空会话数据）后修正视图。
 *
 * 当前路由或「打开浮层前的视图」指向已删会话时回首页：否则会停留在已清空的对话页，
 * 消息来自内存池、发送又会被服务端按「会话不存在」拒绝。浮层本身打开时不改 hash，只修底层视图。
 */
export function pruneSessionView(existing: Iterable<string>): void {
  const keep = new Set(existing);
  const view = lastView();
  if (view.name === 'chat' && !keep.has(view.sessionId)) setLastView({ name: 'sessions' });

  const current = parseHash(window.location.hash);
  if (current.name === 'chat' && !keep.has(current.sessionId)) navigate('#/');
}

function closeOverlay(): void {
  const view = lastView();
  if (view.name === 'chat') navigate(`#/s/${encodeURIComponent(view.sessionId)}`);
  else if (view.name === 'wiki') navigate('#/wiki');
  else if (view.name === 'plugin') navigate(`#/plugin/${encodeURIComponent(view.pluginId)}/${encodeURIComponent(view.entryId)}`);
  else navigate('#/');
}