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
  if (settings) return { name: 'settings', tab: settings[1] ?? 'provider' };

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

export function openSettings(tab = 'provider'): void {
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

function closeOverlay(): void {
  const view = lastView();
  if (view.name === 'chat') navigate(`#/s/${encodeURIComponent(view.sessionId)}`);
  else if (view.name === 'wiki') navigate('#/wiki');
  else if (view.name === 'plugin') navigate(`#/plugin/${encodeURIComponent(view.pluginId)}/${encodeURIComponent(view.entryId)}`);
  else navigate('#/');
}