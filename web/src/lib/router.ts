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
  const onChange = () => setRoute(parseHash(window.location.hash));
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