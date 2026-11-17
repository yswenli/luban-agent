import { createSignal } from 'solid-js';

/**
 * 浏览器会话被门禁拒绝的状态。
 *
 * 令牌不落前端：地址栏 `?t=` 由宿主换成 HttpOnly Cookie，页面只在收到 401 时知道会话结束。
 * 一旦置位就整页挡住——此时任何按钮都会接着 401，让用户继续操作只会误导。
 */
const [authBlocked, setAuthBlocked] = createSignal<string | null>(null);

export { authBlocked };

/** 门禁拒绝访问时记录原因；已置位则不再刷新文案，避免后续并发 401 反复改写提示。 */
export function blockForAuth(message: string): void {
  setAuthBlocked((current) => current ?? message);
}

/** 供「重试」用：清掉提示后由整页重新加载重新走一遍门禁。 */
export function clearAuthBlock(): void {
  setAuthBlocked(null);
}