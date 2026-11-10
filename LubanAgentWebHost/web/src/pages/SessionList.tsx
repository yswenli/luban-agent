import { createEffect, createMemo, createSignal, For, onMount, Show } from 'solid-js';

import { ConfirmActionDialog, Modal, TextPromptDialog } from '../components/Dialog';
import { Icon } from '../components/Icons';
import { PopMenu, menuAt } from '../components/PopMenu';
import { dayBucket, formatRelative, truncate } from '../lib/format';
import { openSession, openSettings } from '../lib/router';
import { workspaceColor, workspaceInitial } from '../lib/workspace';
import {
  bootstrap,
  clearProjectFilter,
  createSession,
  createWorkspace,
  deleteSession,
  filterWorkspaceId,
  refreshSessions,
  renameSession,
  searchFocusToken,
  selectProject,
  sessions,
  sessionsError,
  sessionsLoading,
  showToast,
  workspaceName,
} from '../stores/app';
import { dropChat } from '../stores/chats';

type RowAction =
  | { kind: 'rename'; sessionId: string; title: string }
  | { kind: 'delete'; sessionId: string; title: string };

/** 会话的展示时间：优先更新时间。 */
function sessionTime(session: { updatedAt?: string | null; createdAt: string }): string {
  return session.updatedAt ?? session.createdAt;
}

export function SessionListPage() {
  const [search, setSearch] = createSignal('');
  const [rowMenu, setRowMenu] = createSignal<{ x: number; y: number; sessionId: string } | null>(null);
  const [dialog, setDialog] = createSignal<RowAction | null>(null);
  const [creating, setCreating] = createSignal(false);
  const [newProject, setNewProject] = createSignal(false);

  let searchEl!: HTMLInputElement;

  onMount(() => {
    void refreshSessions();
  });

  // 顶栏点搜索：回到首页后把焦点交给搜索框（首次加载 token 为 0，不抢焦点）
  createEffect(() => {
    const token = searchFocusToken();
    if (token === 0) return;
    queueMicrotask(() => searchEl?.focus());
  });

  const workspaces = () => bootstrap()?.workspaces ?? [];

  const filtered = createMemo(() => {
    const project = filterWorkspaceId();
    const keyword = search().trim().toLowerCase();

    return sessions().filter((session) => {
      if (project && session.workspaceId !== project) return false;
      if (!keyword) return true;
      const title = (session.title ?? '').toLowerCase();
      const ws = workspaceName(session.workspaceId).toLowerCase();
      return title.includes(keyword) || ws.includes(keyword);
    });
  });

  const groups = createMemo(() => {
    const items = filtered();
    return [
      { key: 'today', title: '今天', items: items.filter((s) => dayBucket(sessionTime(s)) === 'today') },
      { key: 'yesterday', title: '昨天', items: items.filter((s) => dayBucket(sessionTime(s)) === 'yesterday') },
      { key: 'earlier', title: '更早', items: items.filter((s) => dayBucket(sessionTime(s)) === 'earlier') },
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
      <aside class="home-side">
        <div class="side-head">
          <span class="section-label">项目</span>
          <div class="side-actions">
            <Show when={filterWorkspaceId()}>
              <button class="icon-btn" title="显示全部项目" onClick={() => clearProjectFilter()}>
                <Icon name="close" size={15} />
              </button>
            </Show>
            <button class="icon-btn" title="新建项目" onClick={() => setNewProject(true)}>
              <Icon name="new-project" size={17} />
            </button>
          </div>
        </div>

        <div class="side-list">
          <For each={workspaces()}>
            {(workspace) => (
              <button
                class="side-item"
                classList={{ selected: workspace.workspaceId === filterWorkspaceId() }}
                title={
                  workspace.workspaceId === filterWorkspaceId()
                    ? `${workspace.rootPath} · 再点一次取消筛选`
                    : workspace.rootPath
                }
                onClick={() => void selectProject(workspace.workspaceId)}
              >
                <span class="ws-badge" style={{ background: workspaceColor(workspace.workspaceId) }}>
                  {workspaceInitial(workspace.name)}
                </span>
                <span class="side-name">{workspace.name || workspace.rootPath}</span>
              </button>
            )}
          </For>
          <Show when={workspaces().length === 0}>
            <span class="side-hint faint">还没有项目，点右上角新建。</span>
          </Show>
        </div>

        <div class="side-links">
          <button class="side-link" onClick={() => openSettings('skill')}>
            <Icon name="settings" size={16} />
            设置
          </button>
          <button class="side-link" onClick={() => showToast('输入 /help 查看可用命令', 'info')}>
            <Icon name="help" size={16} />
            帮助
          </button>
        </div>
      </aside>

      <div class="home-main">
        <div class="searchrow home-search">
          <Icon name="search" size={16} />
          <input
            ref={searchEl}
            type="search"
            placeholder="搜索会话"
            value={search()}
            onInput={(event) => setSearch(event.currentTarget.value)}
          />
          <button class="icon-btn search-action" title="刷新会话" onClick={() => void refreshSessions()}>
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

          <Show when={!sessionsLoading()} fallback={<div class="loading-line">加载会话…</div>}>
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
                            <h2>还没有会话</h2>
                            <p>新建一个会话开始对话；会话会绑定到当前工作区。</p>
                          </>
                        }
                      >
                        <h2>该项目还没有会话</h2>
                        <p>新建一个会话；它会绑定到当前项目。</p>
                      </Show>
                    }
                  >
                    <h2>没有匹配的会话</h2>
                    <p>换个关键词，或清空搜索查看全部会话。</p>
                  </Show>
                  <button class="btn primary" disabled={creating()} onClick={() => void onNewSession()}>
                    <Icon name="plus" size={16} />
                    新建会话
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
                          新建会话
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
              void deleteSession(target).then((ok) => {
                if (ok) dropChat(target);
              });
            }}
          />
        )}
      </Show>

      <Show when={newProject()}>
        <NewProjectDialog
          onClose={() => setNewProject(false)}
          onCreate={(rootPath, name) => createWorkspace(rootPath, name)}
        />
      </Show>
    </div>
  );
}

/** 新建项目弹窗：名称可选，根路径必须是绝对路径。 */
function NewProjectDialog(props: {
  onClose: () => void;
  onCreate: (rootPath: string, name: string) => Promise<boolean>;
}) {
  const [rootPath, setRootPath] = createSignal('');
  const [name, setName] = createSignal('');
  const [saving, setSaving] = createSignal(false);
  let rootInput!: HTMLInputElement;

  onMount(() => {
    window.setTimeout(() => rootInput?.focus(), 30);
  });

  const submit = async () => {
    if (saving() || !rootPath().trim()) return;
    setSaving(true);
    try {
      if (await props.onCreate(rootPath().trim(), name().trim())) props.onClose();
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      title="新建项目"
      subtitle="项目对应本机上的一个工作区目录"
      onClose={props.onClose}
      footer={
        <>
          <button class="btn ghost" onClick={props.onClose}>
            取消
          </button>
          <button class="btn primary" disabled={saving() || !rootPath().trim()} onClick={() => void submit()}>
            {saving() ? '创建中…' : '创建'}
          </button>
        </>
      }
    >
      <div class="form-stack">
        <div class="field">
          <div class="section-label">名称（可选）</div>
          <input
            class="text-input"
            placeholder="默认取目录名"
            value={name()}
            onInput={(event) => setName(event.currentTarget.value)}
          />
        </div>
        <div class="field">
          <div class="section-label">根路径（绝对路径）</div>
          <input
            ref={rootInput}
            class="text-input mono"
            placeholder="D:\WorkBench\Walle\my-project"
            value={rootPath()}
            onInput={(event) => setRootPath(event.currentTarget.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') void submit();
            }}
          />
        </div>
      </div>
    </Modal>
  );
}

function sessionRow(
  sessionId: string,
  title: string | null | undefined,
  workspaceId: string | null | undefined,
  time: string | null | undefined,
  onMenu: (event: MouseEvent, sessionId: string) => void,
) {
  const name = workspaceName(workspaceId);
  return (
    <div class="session-row" role="button" tabIndex={0} onClick={() => openSession(sessionId)} onKeyDown={(event) => {
      if (event.key === 'Enter' || event.key === ' ') openSession(sessionId);
    }}>
      <span
        class="ws-badge sm"
        classList={{ muted: !workspaceId }}
        style={workspaceId ? { background: workspaceColor(workspaceId) } : {}}
        title={name || '未知项目'}
      >
        {workspaceInitial(name)}
      </span>
      <span class="session-title">{truncate(title || '未命名会话', 60)}</span>
      <span class="session-ws" title={formatRelative(time)}>{name}</span>
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