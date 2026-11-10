import { createSignal, onCleanup } from 'solid-js';

export type Route =
  | { name: 'sessions' }
  | { name: 'chat'; sessionId: string }
  | { name: 'settings'; tab: string };

/**
 * SAEA.MVC 只按 `/{controller}/{action}` 派发、没有 SPA 回退，
 * 所以路由必须全部放在 hash 里（`#/`、`#/s/<id>`、`#/settings/<tab>`）。
 */
export function parseHash(hash: string): Route {
  const value = hash.replace(/^#/, '');
  if (!value || value === '/') return { name: 'sessions' };

  const chat = value.match(/^\/s\/([^/?#]+)/);
  if (chat) return { name: 'chat', sessionId: decodeURIComponent(chat[1]) };

  const settings = value.match(/^\/settings(?:\/([^/?#]+))?/);
  if (settings) return { name: 'settings', tab: settings[1] ?? 'skill' };

  return { name: 'sessions' };
}

export function useRoute() {
  const [route, setRoute] = createSignal<Route>(parseHash(window.location.hash));
  const onChange = () => {
    const next = parseHash(window.location.hash);
    setRoute(next);
    // 设置是浮层：记住打开前的视图，关闭时回到原处而不是固定回首页
    if (next.name !== 'settings') setLastView(next);
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

export function openSettings(tab = 'skill'): void {
  navigate(`#/settings/${tab}`);
}

const initialView = parseHash(window.location.hash);
const [lastView, setLastView] = createSignal<Route>(initialView.name === 'settings' ? { name: 'sessions' } : initialView);

/** 设置浮层下方要渲染的视图。 */
export { lastView };

/** 设置浮层关闭后回到打开前的视图（深链接直接打开时回首页）。 */
export function closeSettings(): void {
  const view = lastView();
  if (view.name === 'chat') navigate(`#/s/${encodeURIComponent(view.sessionId)}`);
  else navigate('#/');
}