import { createSignal, untrack } from 'solid-js';

/**
 * 顶栏「已打开会话」标签。
 *
 * 标签独立于路由：切到首页后会话仍留在顶栏，点回去可直接继续；
 * 只保存会话标识，标题与项目名每次从 `sessions()` 里现取，避免存两份数据。
 */
const STORAGE_KEY = 'luban.web.openSessions';
const MAX_TABS = 12;

function readStorage(): string[] {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const value = JSON.parse(raw) as unknown;
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
  } catch {
    return [];
  }
}

const [openSessionIds, setOpenSessionIds] = createSignal<string[]>(readStorage());

function persist(list: string[]): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(list));
  } catch {
    /* 隐私模式或配额满：只影响刷新后恢复，不影响本次会话 */
  }
}

function commit(list: string[]): void {
  setOpenSessionIds(list);
  persist(list);
}

export { openSessionIds };

/** 以下操作会读写标签集合，必须 `untrack`：在副作用里调用时不能反向订阅该信号，否则会自我触发。 */
function current(): string[] {
  return untrack(openSessionIds);
}

/** 打开一个会话时把它记到顶栏（重复打开只保留一个标签，超出上限丢弃最旧的）。 */
export function rememberSession(sessionId: string): void {
  if (!sessionId) return;
  const list = current();
  if (list.includes(sessionId)) return;
  commit([...list, sessionId].slice(-MAX_TABS));
}

/** 关闭标签或删除会话时移除。 */
export function forgetSession(sessionId: string): void {
  const list = current();
  if (!list.includes(sessionId)) return;
  commit(list.filter((id) => id !== sessionId));
}

/** 会话列表刷新后剔除已不存在的标签，避免顶栏留下指向已删除会话的入口。 */
export function pruneSessions(existing: Iterable<string>): void {
  const keep = new Set(existing);
  const list = current();
  const next = list.filter((id) => keep.has(id));
  if (next.length !== list.length) commit(next);
}