import { createMemo, createSignal, For, onMount, Show } from 'solid-js';

import { ConfirmActionDialog, TextPromptDialog } from '../components/Dialog';
import { Icon } from '../components/Icons';
import { PopMenu, menuAt } from '../components/PopMenu';
import { formatRelative, isToday, truncate } from '../lib/format';
import { openSession, openSettings } from '../lib/router';
import {
  bootstrap,
  createSession,
  currentWorkspaceId,
  deleteSession,
  refreshSessions,
  renameSession,
  sessions,
  sessionsError,
  sessionsLoading,
  showToast,
  switchWorkspace,
  workspaceName,
} from '../stores/app';

type RowAction =
  | { kind: 'rename'; sessionId: string; title: string }
  | { kind: 'delete'; sessionId: string; title: string };

export function SessionListPage() {
  const [search, setSearch] = createSignal('');
  const [rowMenu, setRowMenu] = createSignal<{ x: number; y: number; sessionId: string } | null>(null);
  const [dialog, setDialog] = createSignal<RowAction | null>(null);
  const [creating, setCreating] = createSignal(false);

  onMount(() => {
    void refreshSessions();
  });

  const workspaces = () => bootstrap()?.workspaces ?? [];

  const filtered = createMemo(() => {
    const keyword = search().trim().toLowerCase();
    const workspaceId = currentWorkspaceId();
    return sessions().filter((session) => {
      if (workspaceId && session.workspaceId !== workspaceId) return false;
      if (!keyword) return true;
      const title = (session.title ?? '').toLowerCase();
      const ws = workspaceName(session.workspaceId).toLowerCase();
      return title.includes(keyword) || ws.includes(keyword);
    });
  });

  const today = createMemo(() => filtered().filter((s) => isToday(s.updatedAt ?? s.createdAt)));
  const earlier = createMemo(() => filtered().filter((s) => !isToday(s.updatedAt ?? s.createdAt)));

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
    <div class="app">
      <div class="shell">
        <div class="topbar">
          <Icon name="grid" size={17} />
          <span class="topbar-title">LuBan Agent</span>
          <div class="spacer" />
          <button class="icon-btn" title="刷新" onClick={() => void refreshSessions()}>
            <Icon name="refresh" size={17} />
          </button>
          <button class="icon-btn" title="设置" onClick={() => openSettings('skill')}>
            <Icon name="settings" size={17} />
          </button>
        </div>

        <div class="scroll">
          <div class="pad">
            <div class="projects">
              <div class="section-label">项目</div>
              <div class="chips">
                <For each={workspaces()}>
                  {(workspace) => (
                    <button
                      class="chip"
                      classList={{ selected: workspace.workspaceId === currentWorkspaceId() }}
                      onClick={() => {
                        if (workspace.workspaceId === currentWorkspaceId()) return;
                        void switchWorkspace(workspace.workspaceId);
                      }}
                      title={workspace.rootPath}
                    >
                      <span class="dot" />
                      {workspace.name}
                    </button>
                  )}
                </For>
                <Show when={workspaces().length === 0}>
                  <span class="faint">还没有工作区，可在设置页添加。</span>
                </Show>
              </div>
            </div>

            <div class="searchrow">
              <Icon name="search" size={16} />
              <input
                type="search"
                placeholder="搜索会话"
                value={search()}
                onInput={(event) => setSearch(event.currentTarget.value)}
              />
            </div>

            <Show when={sessionsError()}>
              <div class="notice error" style={{ 'margin-top': '12px' }}>
                <Icon name="alert" size={16} />
                <span>{sessionsError()}</span>
              </div>
            </Show>

            <Show when={!sessionsLoading()} fallback={<div class="loading-line">加载会话…</div>}>
              <Show
                when={filtered().length > 0}
                fallback={
                  <div class="empty">
                    <Icon name="message" size={30} />
                    <h2>还没有会话</h2>
                    <p>新建一个会话开始对话；会话会绑定到当前工作区。</p>
                    <button class="btn primary" disabled={creating()} onClick={() => void onNewSession()}>
                      <Icon name="plus" size={16} />
                      新建会话
                    </button>
                  </div>
                }
              >
                <Show when={today().length > 0}>
                  <div class="group">
                    <div class="group-head">
                      <span class="group-title">今天</span>
                      <button class="group-action" disabled={creating()} onClick={() => void onNewSession()}>
                        <Icon name="plus" size={15} />
                        新建会话
                      </button>
                    </div>
                    <For each={today()}>{(session) => sessionRow(session.sessionId, session.title, session.workspaceId, session.updatedAt ?? session.createdAt, openRowMenu)}</For>
                  </div>
                </Show>

                <Show when={earlier().length > 0}>
                  <div class="group">
                    <div class="group-head">
                      <span class="group-title">更早</span>
                      <Show when={today().length === 0}>
                        <button class="group-action" disabled={creating()} onClick={() => void onNewSession()}>
                          <Icon name="plus" size={15} />
                          新建会话
                        </button>
                      </Show>
                    </div>
                    <For each={earlier()}>
                      {(session) => sessionRow(session.sessionId, session.title, session.workspaceId, session.updatedAt ?? session.createdAt, openRowMenu)}
                    </For>
                  </div>
                </Show>
              </Show>
            </Show>
          </div>
        </div>

        <div class="pagefoot">
          <button class="foot-link" onClick={() => openSettings('skill')}>
            <Icon name="settings" size={16} />
            设置
          </button>
          <button class="foot-link" onClick={() => showToast('输入 /help 查看可用命令', 'info')}>
            <Icon name="help" size={16} />
            帮助
          </button>
          <div class="spacer" />
          <Show when={bootstrap()?.version}>
            <span class="faint mono" title={bootstrap()!.version ?? ''}>
              v{bootstrap()!.version!.split('+')[0]}
            </span>
          </Show>
        </div>
      </div>

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
              重命名
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
              删除
            </button>
          </PopMenu>
        )}
      </Show>

      <Show when={dialog()?.kind === 'rename' ? (dialog() as Extract<RowAction, { kind: 'rename' }>) : null}>
        {(action) => (
          <TextPromptDialog
            title="重命名会话"
            label="标题"
            initialValue={action().title}
            confirmText="保存"
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
            title="删除会话"
            message={`确定删除「${truncate(action().title || '未命名会话', 40)}」？该会话的消息将一并删除。`}
            onClose={() => setDialog(null)}
            onConfirm={() => {
              const target = action().sessionId;
              setDialog(null);
              void deleteSession(target);
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
  onMenu: (event: MouseEvent, sessionId: string) => void,
) {
  return (
    <div class="session-row" role="button" tabIndex={0} onClick={() => openSession(sessionId)} onKeyDown={(event) => {
      if (event.key === 'Enter' || event.key === ' ') openSession(sessionId);
    }}>
      <span class="session-avatar">
        <Icon name="message" size={15} />
      </span>
      <span class="session-title">{truncate(title || '未命名会话', 60)}</span>
      <span class="session-ws">{workspaceName(workspaceId) || formatRelative(time)}</span>
      <span class="session-actions">
        <button
          class="icon-btn"
          title="更多"
          onClick={(event) => onMenu(event, sessionId)}
        >
          <Icon name="more" size={16} />
        </button>
      </span>
    </div>
  );
}