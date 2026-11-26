import { createEffect, createSignal, For, Show } from 'solid-js';

import { beginDragSort } from '../lib/dragSort';
import { truncate } from '../lib/format';
import { openSession, openSessions } from '../lib/router';
import { setThemePreference, themePreference, type ThemePreference } from '../lib/theme';
import { workspaceColor, workspaceInitial } from '../lib/workspace';
import { createSession, focusSessionSearch, sessions, workspaceName } from '../stores/app';
import { dropChat, getChat } from '../stores/chats';
import { forgetSession, moveSession, openSessionIds } from '../stores/tabs';

import { Icon } from './Icons';
import { menuAt, PopMenu } from './PopMenu';

/** 主题菜单项：图标只用于区分选项，按钮本身始终是调色板。 */
const THEME_OPTIONS: { value: ThemePreference; label: string; icon: string }[] = [
  { value: 'follow', label: '跟随系统', icon: 'monitor' },
  { value: 'dark', label: '深色', icon: 'moon' },
  { value: 'light', label: '浅色', icon: 'sun' },
];

/**
 * 全局顶栏：会话入口 + 已打开会话标签。
 *
 * 标签与路由解耦——切到首页/设置时标签仍在，点回去继续原会话；
 * 进行中的回合在标签上显示活动点（SSE 由全局会话池持有，不随页面卸载中断）。
 */
export function TopBar(props: { activeSessionId: string | null }) {
  const [creating, setCreating] = createSignal(false);
  const [themeMenu, setThemeMenu] = createSignal<{ x: number; y: number } | null>(null);
  const [draggingTab, setDraggingTab] = createSignal<string | null>(null);
  const [dropSlot, setDropSlot] = createSignal<number | null>(null);
  let tabsEl: HTMLDivElement | undefined;

  // 标签多到溢出时，把当前标签滚进可视区（新开的会话在末尾，不滚就看不见）
  createEffect(() => {
    const id = props.activeSessionId;
    if (!id || !tabsEl) return;
    tabsEl.querySelector<HTMLElement>(`[data-session-id="${CSS.escape(id)}"]`)
      ?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  });

  // 鼠标滚轮只有纵向 delta，横向溢出容器不会自动滚动，这里把纵向滚动转成横向
  function onTabsWheel(event: WheelEvent): void {
    if (!tabsEl || event.deltaX || !event.deltaY) return;
    if (tabsEl.scrollWidth <= tabsEl.clientWidth) return;
    tabsEl.scrollLeft += event.deltaY;
    event.preventDefault();
  }

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
    // 关标签只是本地离开：销毁本地实例并中止这条本地流；服务端轮次由 hub 继续跑，重开时自动续上
    dropChat(sessionId);
    if (!wasActive) return;

    // 关掉当前标签：优先落到相邻标签，没有就回首页
    const rest = list.filter((id) => id !== sessionId);
    const next = rest[Math.min(index, rest.length - 1)];
    if (next) openSession(next);
    else openSessions();
  }

  /** 标签左右拖放排序：位移超过阈值才进入拖动，单击仍然是打开会话。 */
  function onTabPointerDown(event: PointerEvent, sessionId: string, index: number): void {
    // 关闭按钮自己响应点击，不参与排序
    if ((event.target as HTMLElement).closest('.session-tab-close')) return;
    beginDragSort({
      event,
      item: event.currentTarget as HTMLElement,
      container: tabsEl,
      itemSelector: '.session-tab',
      axis: 'x',
      index,
      onStart: () => setDraggingTab(sessionId),
      onSlot: setDropSlot,
      onDrop: (toIndex) => moveSession(sessionId, toIndex),
      onEnd: () => {
        setDraggingTab(null);
        setDropSlot(null);
      },
    });
  }

  return (
    <div class="app-topbar">
      <button class="icon-btn" title="首页" onClick={() => openSessions()}>
        <Icon name="home" size={17} />
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

      <div class="session-tabs" ref={(el) => (tabsEl = el)} onWheel={onTabsWheel}>
        <For each={openSessionIds()}>
          {(sessionId, index) => {
            const chat = getChat(sessionId);
            const session = () => sessions().find((item) => item.sessionId === sessionId) ?? null;
            const workspaceId = () => session()?.workspaceId ?? null;
            const project = () => workspaceName(workspaceId()) || '未命名项目';
            const title = () => session()?.title || '未命名会话';

            return (
              <div
                class="session-tab"
                classList={{
                  active: props.activeSessionId === sessionId,
                  dragging: draggingTab() === sessionId,
                  'drop-before': dropSlot() === index(),
                  'drop-after':
                    dropSlot() === openSessionIds().length && index() === openSessionIds().length - 1,
                }}
                data-session-id={sessionId}
                title={`${project()} / ${title()}`}
                onPointerDown={(event) => onTabPointerDown(event, sessionId, index())}
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
      </div>

      <button class="icon-btn" title="新建会话" disabled={creating()} onClick={() => void createTab()}>
        <Icon name="plus" size={17} />
      </button>

      <div class="spacer" />

      <button
        class="icon-btn"
        classList={{ active: themeMenu() !== null }}
        title="主题"
        aria-label="主题"
        aria-haspopup="menu"
        aria-expanded={themeMenu() !== null}
        onClick={(event) => {
          const rect = event.currentTarget.getBoundingClientRect();
          setThemeMenu(menuAt(rect, { width: 176, height: 126, align: 'end' }));
        }}
      >
        <Icon name="palette" size={17} />
      </button>

      <Show when={themeMenu()}>
        {(position) => (
          <PopMenu
            style={{ left: `${position().x}px`, top: `${position().y}px`, 'min-width': '176px' }}
            onClose={() => setThemeMenu(null)}
          >
            <For each={THEME_OPTIONS}>
              {(option) => (
                <button
                  class="menu-item"
                  classList={{ active: themePreference() === option.value }}
                  onClick={() => {
                    setThemePreference(option.value);
                    setThemeMenu(null);
                  }}
                >
                  <Icon name={option.icon} size={15} />
                  {option.label}
                  <Show when={themePreference() === option.value}>
                    <Icon name="check" size={14} class="menu-check" />
                  </Show>
                </button>
              )}
            </For>
          </PopMenu>
        )}
      </Show>
    </div>
  );
}