import { createSignal, For, Show } from 'solid-js';

import { truncate } from '../lib/format';
import { openSession, openSessions } from '../lib/router';
import { workspaceColor, workspaceInitial } from '../lib/workspace';
import {
  bootstrap,
  createSession,
  focusSessionSearch,
  sessions,
  workspaceName,
} from '../stores/app';
import { dropChat, getChat } from '../stores/chats';
import { forgetSession, openSessionIds } from '../stores/tabs';

import { Icon } from './Icons';

/**
 * 全局顶栏：会话入口 + 已打开会话标签。
 *
 * 标签与路由解耦——切到首页/设置时标签仍在，点回去继续原会话；
 * 进行中的回合在标签上显示活动点（SSE 由全局会话池持有，不随页面卸载中断）。
 */
export function TopBar(props: { activeSessionId: string | null }) {
  const [creating, setCreating] = createSignal(false);

  async function createTab(): Promise<void> {
    if (creating()) return;
    setCreating(true);
    try {
      const sessionId = await createSession();
      if (sessionId) openSession(sessionId);
    } finally {
      setCreating(false);
    }
  }

  function closeTab(sessionId: string): void {
    const list = openSessionIds();
    const index = list.indexOf(sessionId);
    const wasActive = props.activeSessionId === sessionId;
    forgetSession(sessionId);
    // 关标签即视为结束该会话的本次浏览：销毁实例并中止在途流
    dropChat(sessionId);
    if (!wasActive) return;

    // 关掉当前标签：优先落到相邻标签，没有就回首页
    const rest = list.filter((id) => id !== sessionId);
    const next = rest[Math.min(index, rest.length - 1)];
    if (next) openSession(next);
    else openSessions();
  }

  return (
    <div class="app-topbar">
      <button class="icon-btn" title="会话列表" onClick={() => openSessions()}>
        <Icon name="grid" size={17} />
      </button>
      <button
        class="icon-btn"
        title="搜索会话"
        onClick={() => {
          openSessions();
          focusSessionSearch();
        }}
      >
        <Icon name="search" size={17} />
      </button>

      <div class="session-tabs">
        <For each={openSessionIds()}>
          {(sessionId) => {
            const chat = getChat(sessionId);
            const session = () => sessions().find((item) => item.sessionId === sessionId) ?? null;
            const workspaceId = () => session()?.workspaceId ?? null;
            const project = () => workspaceName(workspaceId()) || '未命名项目';
            const title = () => session()?.title || '未命名会话';

            return (
              <div
                class="session-tab"
                classList={{ active: props.activeSessionId === sessionId }}
                title={`${project()} / ${title()}`}
              >
                <button class="session-tab-main" onClick={() => openSession(sessionId)}>
                  <span class="ws-badge sm" style={{ background: workspaceColor(workspaceId()) }}>
                    {workspaceInitial(project())}
                  </span>
                  <span class="session-tab-title">{truncate(title(), 18)}</span>
                  <Show when={chat.streaming()}>
                    <span class="tab-activity" title="本轮进行中" />
                  </Show>
                  <Show when={chat.confirmation()}>
                    <span class="tab-attention" title="等待工具确认" />
                  </Show>
                </button>
                <button
                  class="session-tab-close"
                  title="关闭标签"
                  onClick={(event) => {
                    event.stopPropagation();
                    closeTab(sessionId);
                  }}
                >
                  <Icon name="close" size={12} />
                </button>
              </div>
            );
          }}
        </For>

        <button class="icon-btn" title="新建会话" disabled={creating()} onClick={() => void createTab()}>
          <Icon name="plus" size={17} />
        </button>
      </div>

      <div class="spacer" />

      <Show when={bootstrap()?.version}>
        <span class="faint mono topbar-version" title={bootstrap()!.version ?? ''}>
          v{bootstrap()!.version!.split('+')[0]}
        </span>
      </Show>
    </div>
  );
}