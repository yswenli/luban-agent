import { createEffect, createMemo, createSignal, For, onCleanup, onMount, Show } from 'solid-js';
import type { JSX } from 'solid-js';

import { ConfirmActionDialog, TextPromptDialog } from '../components/Dialog';
import { Icon } from '../components/Icons';
import { PopMenu, menuAt } from '../components/PopMenu';
import { ProjectSidebar } from '../components/ProjectSidebar';
import { SideResizer } from '../components/SideResizer';
import type { DayBucket } from '../lib/format';
import { dayBucket, formatAbsoluteTime, formatSessionTime, localDayKey, truncate } from '../lib/format';
import { openSession } from '../lib/router';
import { sidebarCollapsed, useNarrowViewport } from '../lib/sidebar';
import { workspaceColor, workspaceInitial } from '../lib/workspace';
import {
  createSession,
  deleteSession,
  filterWorkspaceId,
  refreshSessions,
  renameSession,
  searchFocusToken,
  sessions,
  sessionsError,
  sessionsLoading,
  wikiWorkspaceIds,
  workspaceName,
} from '../stores/app';
import { dropChat } from '../stores/chats';
import { t } from '../i18n';

type RowAction =
  | { kind: 'rename'; sessionId: string; title: string }
  | { kind: 'delete'; sessionId: string; title: string };

/** 会话的展示时间：优先更新时间。 */
function sessionTime(session: { updatedAt?: string | null; createdAt: string }): string {
  return session.updatedAt ?? session.createdAt;
}

/**
 * 首页：左栏项目 + 右栏会话。
 *
 * `main` 有值时用它替换右栏内容（插件页面借同一套侧栏渲染），侧栏入口与项目列表保持一致。
 */
export function SessionListPage(props: { main?: JSX.Element }) {
  const [search, setSearch] = createSignal('');
  const [rowMenu, setRowMenu] = createSignal<{ x: number; y: number; sessionId: string } | null>(null);
  const [dialog, setDialog] = createSignal<RowAction | null>(null);
  const [creating, setCreating] = createSignal(false);

  let searchEl!: HTMLInputElement;

  // 时间标签含「秒前」，需要定时推进；分组用的日期键只在跨日时推进，避免每 10 秒白重排一次列表。
  const [now, setNow] = createSignal(Date.now());
  const [todayKey, setTodayKey] = createSignal(localDayKey(new Date().toISOString()));

  const narrow = useNarrowViewport();
  // 窄屏左栏变横排 chip（见 styles.css 的 760px 断点），那时栏宽由 CSS 接管，拖拽条要隐掉
  const draggable = () => !narrow() && !sidebarCollapsed();

  onMount(() => {
    void refreshSessions();
    const timer = window.setInterval(() => {
      const value = Date.now();
      setNow(value);
      const key = localDayKey(new Date(value).toISOString());
      if (key !== todayKey()) setTodayKey(key);
    }, 10_000);
    onCleanup(() => window.clearInterval(timer));
  });

  // 顶栏点搜索：回到首页后把焦点交给搜索框（首次加载 token 为 0，不抢焦点）
  createEffect(() => {
    const token = searchFocusToken();
    if (token === 0) return;
    queueMicrotask(() => searchEl?.focus());
  });

  const filtered = createMemo(() => {
    const project = filterWorkspaceId();
    const keyword = search().trim().toLowerCase();
    const wiki = wikiWorkspaceIds();

    return sessions().filter((session) => {
      // 知识库工作区只在知识库页出现，其会话不进首页列表
      if (session.workspaceId && wiki.has(session.workspaceId)) return false;
      if (project && session.workspaceId !== project) return false;
      if (!keyword) return true;
      const title = (session.title ?? '').toLowerCase();
      const ws = workspaceName(session.workspaceId).toLowerCase();
      return title.includes(keyword) || ws.includes(keyword);
    });
  });

  const groups = createMemo(() => {
    void todayKey(); // 跨过午夜要重新分组，否则「今天」标题下会挂着标签写着「昨天」的行
    const items = filtered();
    return [
      { key: 'today' as const, title: t('sessionList.today'), items: items.filter((s) => dayBucket(sessionTime(s)) === 'today') },
      { key: 'earlier' as const, title: t('sessionList.earlier'), items: items.filter((s) => dayBucket(sessionTime(s)) === 'earlier') },
    ].filter((group) => group.items.length > 0);
  });

  async function onNewSession() {
    if (creating()) return;
    setCreating(true);
    try {
      const sessionId = await createSession();
      if (sessionId) openSession(sessionId);
    } finally {
      setCreating(false);
    }
  }

  function openRowMenu(event: MouseEvent, sessionId: string) {
    event.stopPropagation();
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const position = menuAt(rect, { width: 168, height: 92, align: 'end' });
    setRowMenu({ ...position, sessionId });
  }

  const activeSession = createMemo(() => {
    const menu = rowMenu();
    if (!menu) return null;
    return sessions().find((session) => session.sessionId === menu.sessionId) ?? null;
  });

  return (
    <div class="home-body">
      <ProjectSidebar />
      <Show when={draggable()}>
        <SideResizer label={t('sessionList.resizeLabel')} />
      </Show>

      <Show when={!props.main} fallback={props.main}>
        <div class="home-main">
          <div class="searchrow home-search">
          <Icon name="search" size={16} />
          <input
            ref={searchEl}
            type="search"
            placeholder={t('topbar.search')}
            value={search()}
            onInput={(event) => setSearch(event.currentTarget.value)}
          />
          <button class="icon-btn search-action" title={t('sessionList.refresh')} onClick={() => void refreshSessions()}>
            <Icon name="refresh" size={16} />
          </button>
        </div>

        <div class="home-scroll">
          <Show when={sessionsError()}>
            <div class="notice error" style={{ 'margin-bottom': '12px' }}>
              <Icon name="alert" size={16} />
              <span>{sessionsError()}</span>
            </div>
          </Show>

          <Show when={!sessionsLoading()} fallback={<div class="loading-line">{t('sessionList.loading')}</div>}>
            <Show
              when={filtered().length > 0}
              fallback={
                <div class="empty">
                  <Icon name="message" size={30} />
                  <Show
                    when={search().trim()}
                    fallback={
                      <Show
                        when={filterWorkspaceId()}
                        fallback={
                          <>
                            <h2>{t('sessionList.emptyTitle')}</h2>
                            <p>{t('sessionList.emptyBody')}</p>
                          </>
                        }
                      >
                        <h2>{t('sessionList.emptyProjectTitle')}</h2>
                        <p>{t('sessionList.emptyProjectBody')}</p>
                      </Show>
                    }
                  >
                    <h2>{t('sessionList.noMatchTitle')}</h2>
                    <p>{t('sessionList.noMatchBody')}</p>
                  </Show>
                  <button class="btn primary" disabled={creating()} onClick={() => void onNewSession()}>
                    <Icon name="plus" size={16} />
                    {t('topbar.newSession')}
                  </button>
                </div>
              }
            >
              <For each={groups()}>
                {(group, groupIndex) => (
                  <div class="group">
                    <div class="group-head">
                      <span class="group-title">{group.title}</span>
                      <Show when={groupIndex() === 0}>
                        <button class="group-action" disabled={creating()} onClick={() => void onNewSession()}>
                          <Icon name="plus" size={15} />
                          {t('topbar.newSession')}
                        </button>
                      </Show>
                    </div>
                    <For each={group.items}>
                      {(session) =>
                        sessionRow(
                          session.sessionId,
                          session.title,
                          session.workspaceId,
                          sessionTime(session),
                          group.key,
                          now,
                          openRowMenu,
                        )
                      }
                    </For>
                  </div>
                )}
              </For>
            </Show>
          </Show>
        </div>
      </div>
      </Show>

      <Show when={rowMenu()}>
        {(menu) => (
          <PopMenu style={{ left: `${menu().x}px`, top: `${menu().y}px`, 'min-width': '168px' }} onClose={() => setRowMenu(null)}>
            <button
              class="menu-item"
              onClick={() => {
                const session = activeSession();
                setRowMenu(null);
                if (session) setDialog({ kind: 'rename', sessionId: session.sessionId, title: session.title ?? '' });
              }}
            >
              <Icon name="pencil" size={15} />
              {t('common.rename')}
            </button>
            <button
              class="menu-item"
              onClick={() => {
                const session = activeSession();
                setRowMenu(null);
                if (session) setDialog({ kind: 'delete', sessionId: session.sessionId, title: session.title ?? '' });
              }}
            >
              <Icon name="trash" size={15} />
              {t('common.delete')}
            </button>
          </PopMenu>
        )}
      </Show>

      <Show when={dialog()?.kind === 'rename' ? (dialog() as Extract<RowAction, { kind: 'rename' }>) : null}>
        {(action) => (
          <TextPromptDialog
            title={t('sessionList.renameTitle')}
            label={t('sessionList.renameLabel')}
            initialValue={action().title}
            confirmText={t('common.save')}
            onClose={() => setDialog(null)}
            onConfirm={(value) => {
              const target = action().sessionId;
              setDialog(null);
              void renameSession(target, value);
            }}
          />
        )}
      </Show>

      <Show when={dialog()?.kind === 'delete' ? (dialog() as Extract<RowAction, { kind: 'delete' }>) : null}>
        {(action) => (
          <ConfirmActionDialog
            title={t('sessionList.deleteTitle')}
            message={t('sessionList.deleteMessage', { name: truncate(action().title || t('topbar.unnamedSession'), 40) })}
            onClose={() => setDialog(null)}
            onConfirm={() => {
              const target = action().sessionId;
              setDialog(null);
              void deleteSession(target).then((ok) => {
                if (ok) dropChat(target);
              });
            }}
          />
        )}
      </Show>
    </div>
  );
}

function sessionRow(
  sessionId: string,
  title: string | null | undefined,
  workspaceId: string | null | undefined,
  time: string | null | undefined,
  bucket: DayBucket,
  now: () => number,
  onMenu: (event: MouseEvent, sessionId: string) => void,
) {
  const name = workspaceName(workspaceId);
  const timeLabel = () => formatSessionTime(time, bucket, now());
  return (
    <div class="session-row" role="button" tabIndex={0} onClick={() => openSession(sessionId)} onKeyDown={(event) => {
      if (event.key === 'Enter' || event.key === ' ') openSession(sessionId);
    }}>
      <span
        class="ws-badge sm"
        classList={{ muted: !workspaceId }}
        style={workspaceId ? { background: workspaceColor(workspaceId) } : {}}
        title={name || t('sessionList.unknownProject')}
      >
        {workspaceInitial(name)}
      </span>
      <span class="session-title">{truncate(title || t('topbar.unnamedSession'), 60)}</span>
      <span class="session-meta">
        <span class="session-time" title={formatAbsoluteTime(time)}>{timeLabel()}</span>
        <span class="session-ws" title={name}>{name}</span>
      </span>
      <span class="session-actions">
        <button
          class="icon-btn"
          title={t('common.more')}
          onClick={(event) => onMenu(event, sessionId)}
        >
          <Icon name="more" size={16} />
        </button>
      </span>
    </div>
  );
}