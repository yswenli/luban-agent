import { createChat } from './chat';

export type ChatStore = ReturnType<typeof createChat>;

/**
 * 全局会话池。
 *
 * 页面只是会话状态的渲染层：离开对话页不销毁实例、不中止 SSE，
 * 流式回合在后台继续，顶栏标签据此显示活动指示。
 */
const pool = new Map<string, ChatStore>();

/** 按会话取实例；首次访问时创建。 */
export function getChat(sessionId: string): ChatStore {
  let chat = pool.get(sessionId);
  if (!chat) {
    chat = createChat(sessionId);
    pool.set(sessionId, chat);
  }
  return chat;
}

/** 删除会话或主动清理时销毁实例（会中止在途流、回收预览 URL）。 */
export function dropChat(sessionId: string): void {
  const chat = pool.get(sessionId);
  if (!chat) return;
  pool.delete(sessionId);
  chat.dispose();
}