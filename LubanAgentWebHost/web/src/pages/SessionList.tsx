import { createEffect, createMemo, createSignal, For, onCleanup, onMount, Show } from 'solid-js';
import type { JSX } from 'solid-js';

import { ConfirmActionDialog, Modal, TextPromptDialog } from '../components/Dialog';
import { Icon } from '../components/Icons';
import { AboutDialog, HelpDialog } from '../components/InfoDialogs';
import { PopMenu, menuAt } from '../components/PopMenu';
import type { DayBucket } from '../lib/format';
import { dayBucket, formatAbsoluteTime, formatSessionTime, localDayKey, truncate } from '../lib/format';
import { openPlugin, openPlugins, openSession, openSettings, openWiki } from '../lib/router';
import type { Workspace } from '../lib/types';
import { workspaceColor, workspaceInitial } from '../lib/workspace';
import {
  bootstrap,
  clearProjectFilter,
  createSession,
  createWorkspace,
  deleteSession,
  deleteWorkspace,
  filterWorkspaceId,
  projectWorkspaces,
  refreshSessions,
  renameSession,
  renameWorkspace,
  searchFocusToken,
  selectProject,
  sessions,
  sessionsError,
  sessionsLoading,
  wikiWorkspaceIds,
  workspaceName,
} from '../stores/app';
import { dropChat } from '../stores/chats';
import { ensurePluginsLoaded, pluginPageEntries } from '../stores/plugins';

type RowAction =
  | { kind: 'rename'; sessionId: string; title: string }
  | { kind: 'delete'; sessionId: string; title: string };

type ProjectAction =
  | { kind: 'rename'; workspaceId: string; name: string }
  | { kind: 'delete'; workspaceId: string; name: string };

/** 侧栏收起状态跨页面保持：进出会话页会卸载本组件，信号留在模块级才不会回到展开态。 */
const SIDEBAR_STORAGE_KEY = 'luban.web.sidebarCollapsed';

function readSidebarCollapsed(): boolean {
  try {
    return window.localStorage.getItem(SIDEBAR_STORAGE_KEY) === '1';
  } catch {
    return false;
  }
}

const [sidebarCollapsed, setSidebarCollapsed] = createSignal(readSidebarCollapsed());

function persistSidebarCollapsed(collapsed: boolean): void {
  try {
    window.localStorage.setItem(SIDEBAR_STORAGE_KEY, collapsed ? '1' : '0');
  } catch {
    /* 隐私模式或配额满：只影响刷新后恢复，不影响本次操作 */
  }
}

/** 窄屏侧栏是横排 chip（见 styles.css 的 max-width: 760px 断点），横向不占正文宽度，收起只会藏掉标签。 */
const NARROW_SIDEBAR_QUERY = '(max-width: 760px)';

function useNarrowViewport(): () => boolean {
  const [narrow, setNarrow] = createSignal(window.matchMedia(NARROW_SIDEBAR_QUERY).matches);
  onMount(() => {
    const query = window.matchMedia(NARROW_SIDEBAR_QUERY);
    const onChange = (event: MediaQueryListEvent) => setNarrow(event.matches);
    setNarrow(query.matches);
    query.addEventListener('change', onChange);
    onCleanup(() => query.removeEventListener('change', onChange));
  });
  return narrow;
}

/** 项目行的悬浮说明：收起态没有项目名，得把名字补进 title。 */
function projectTitle(workspace: Workspace, collapsed: boolean, filtered: boolean): string {
  const suffix = filtered ? ' · 再点一次取消筛选' : '';
  if (collapsed) return `${workspace.name || workspace.rootPath}${suffix}`;
  return `${workspace.rootPath}${suffix}`;
}

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
  const [projectMenu, setProjectMenu] = createSignal<{ x: number; y: number; workspaceId: string } | null>(null);
  const [projectDialog, setProjectDialog] = createSignal<ProjectAction | null>(null);
  const [creating, setCreating] = createSignal(false);
  const [newProject, setNewProject] = createSignal(false);
  const [info, setInfo] = createSignal<'help' | 'about' | null>(null);

  let searchEl!: HTMLInputElement;

  // 时间标签含「秒前」，需要定时推进；分组用的日期键只在跨日时推进，避免每 10 秒白重排一次列表。
  const [now, setNow] = createSignal(Date.now());
  const [todayKey, setTodayKey] = createSignal(localDayKey(new Date().toISOString()));

  onMount(() => {
    void refreshSessions();
    void ensurePluginsLoaded();
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

  const workspaces = () => projectWorkspaces();

  // 窄屏强制展开：栏位变成横排 chip，收起态在窄屏没有意义
  const narrow = useNarrowViewport();
  const collapsed = () => sidebarCollapsed() && !narrow();

  function toggleSidebar() {
    const next = !sidebarCollapsed();
    setSidebarCollapsed(next);
    persistSidebarCollapsed(next);
  }

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
      { key: 'today' as const, title: '今天', items: items.filter((s) => dayBucket(sessionTime(s)) === 'today') },
      { key: 'earlier' as const, title: '更早', items: items.filter((s) => dayBucket(sessionTime(s)) === 'earlier') },
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

  function openProjectMenu(event: MouseEvent, workspaceId: string) {
    event.stopPropagation();
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const position = menuAt(rect, { width: 168, height: 92, align: 'end' });
    setProjectMenu({ ...position, workspaceId });
  }

  const activeProject = createMemo(() => {
    const menu = projectMenu();
    if (!menu) return null;
    return workspaces().find((workspace) => workspace.workspaceId === menu.workspaceId) ?? null;
  });

  return (
    <div class="home-body">
      <aside class="home-side" classList={{ collapsed: collapsed() }}>
        <div class="side-head">
          <Show when={!narrow()}>
            <button
              class="icon-btn side-toggle"
              title={collapsed() ? '展开侧栏' : '收起侧栏'}
              aria-label={collapsed() ? '展开侧栏' : '收起侧栏'}
              aria-expanded={!collapsed()}
              onClick={toggleSidebar}
            >
              <Icon name="panel-left" size={17} />
            </button>
          </Show>
          <Show when={!collapsed()}>
            <span class="section-label">项目</span>
          </Show>
          <div class="side-actions">
            <Show when={filterWorkspaceId()}>
              <button
                class="icon-btn side-clear-filter"
                title="显示全部项目"
                onClick={() => clearProjectFilter()}
              >
                <Icon name="close" size={15} />
              </button>
            </Show>
            <button class="icon-btn side-new-project" title="新建项目" onClick={() => setNewProject(true)}>
              <Icon name="new-project" size={17} />
            </button>
          </div>
        </div>

        <div class="side-list">
          <For each={workspaces()}>
            {(workspace) => (
              <div
                class="side-item"
                classList={{ selected: workspace.workspaceId === filterWorkspaceId() }}
                role="button"
                tabIndex={0}
                title={projectTitle(workspace, collapsed(), workspace.workspaceId === filterWorkspaceId())}
                onClick={() => void selectProject(workspace.workspaceId)}
                onKeyDown={(event) => {
                  // 行内「更多」按钮的按键不触发行点击
                  if (event.target !== event.currentTarget) return;
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    void selectProject(workspace.workspaceId);
                  }
                }}
              >
                <span class="ws-badge" style={{ background: workspaceColor(workspace.workspaceId) }}>
                  {workspaceInitial(workspace.name)}
                </span>
                <Show when={!collapsed()}>
                  <span class="side-name">{workspace.name || workspace.rootPath}</span>
                  <span class="session-actions">
                    <button
                      class="icon-btn"
                      title="更多"
                      aria-label="项目操作"
                      onClick={(event) => openProjectMenu(event, workspace.workspaceId)}
                    >
                      <Icon name="more" size={16} />
                    </button>
                  </span>
                </Show>
              </div>
            )}
          </For>
          <Show when={workspaces().length === 0 && !collapsed()}>
            <span class="side-hint faint">还没有项目，点右上角新建。</span>
          </Show>
        </div>

        <div class="side-links">
          <button class="side-link" title="知识库" onClick={() => openWiki()}>
            <Icon name="file" size={16} />
            <Show when={!collapsed()}>知识库</Show>
          </button>
          <button class="side-link" title="插件" onClick={() => openPlugins()}>
            <Icon name="plug" size={16} />
            <Show when={!collapsed()}>插件</Show>
          </button>
          <button class="side-link" title="设置" onClick={() => openSettings()}>
            <Icon name="settings" size={16} />
            <Show when={!collapsed()}>设置</Show>
          </button>
          <button class="side-link" title="帮助" onClick={() => setInfo('help')}>
            <Icon name="help" size={16} />
            <Show when={!collapsed()}>帮助</Show>
          </button>
          <button class="side-link" title="关于" onClick={() => setInfo('about')}>
            <Icon name="info" size={16} />
            <Show when={!collapsed()}>关于</Show>
          </button>
          {/* 插件自带页面的入口：只列已启用且镜像可用的条目，放在这一组底部 */}
          <Show when={pluginPageEntries().length > 0}>
            <div class="side-plugin-links">
              <For each={pluginPageEntries()}>
                {(entry) => (
                  <button
                    class="side-link"
                    title={entry.label}
                    onClick={() => openPlugin(entry.pluginId, entry.entryId)}
                  >
                    <Show
                      when={entry.iconUrl}
                      fallback={<Icon name={entry.icon || 'plug'} size={16} />}
                    >
                      {(url) => (
                        <img class="side-plugin-logo" src={url()} width={16} height={16} alt="" />
                      )}
                    </Show>
                    <Show when={!collapsed()}>
                      <span class="side-plugin-label">
                        {entry.label}
                        <Icon name="plug" size={10} class="side-plugin-badge" />
                      </span>
                    </Show>
                  </button>
                )}
              </For>
            </div>
          </Show>
        </div>
      </aside>

      <Show when={!props.main} fallback={props.main}>
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

      <Show when={projectMenu()}>
        {(menu) => (
          <PopMenu style={{ left: `${menu().x}px`, top: `${menu().y}px`, 'min-width': '168px' }} onClose={() => setProjectMenu(null)}>
            <button
              class="menu-item"
              onClick={() => {
                const project = activeProject();
                setProjectMenu(null);
                if (project) {
                  setProjectDialog({ kind: 'rename', workspaceId: project.workspaceId, name: project.name ?? '' });
                }
              }}
            >
              <Icon name="pencil" size={15} />
              重命名
            </button>
            <button
              class="menu-item"
              onClick={() => {
                const project = activeProject();
                setProjectMenu(null);
                if (project) {
                  setProjectDialog({ kind: 'delete', workspaceId: project.workspaceId, name: project.name ?? '' });
                }
              }}
            >
              <Icon name="trash" size={15} />
              删除
            </button>
          </PopMenu>
        )}
      </Show>

      <Show when={projectDialog()?.kind === 'rename' ? (projectDialog() as Extract<ProjectAction, { kind: 'rename' }>) : null}>
        {(action) => (
          <TextPromptDialog
            title="重命名项目"
            subtitle="只改显示名，磁盘目录不变"
            label="名称"
            initialValue={action().name}
            confirmText="保存"
            onClose={() => setProjectDialog(null)}
            onConfirm={(value) => {
              const target = action().workspaceId;
              setProjectDialog(null);
              void renameWorkspace(target, value);
            }}
          />
        )}
      </Show>

      <Show when={projectDialog()?.kind === 'delete' ? (projectDialog() as Extract<ProjectAction, { kind: 'delete' }>) : null}>
        {(action) => (
          <ConfirmActionDialog
            title="删除项目"
            message={(() => {
              const count = sessions().filter((session) => session.workspaceId === action().workspaceId).length;
              const cascade = count > 0 ? `该项目下的 ${count} 个会话会一并删除，` : '';
              return `确定删除项目「${truncate(action().name || '未命名项目', 40)}」？${cascade}.luban-agent 配置目录会一并清理，磁盘目录其他文件不受影响。`;
            })()}
            onClose={() => setProjectDialog(null)}
            onConfirm={() => {
              const target = action().workspaceId;
              const removed = sessions()
                .filter((session) => session.workspaceId === target)
                .map((session) => session.sessionId);
              setProjectDialog(null);
              void deleteWorkspace(target).then((ok) => {
                if (ok) removed.forEach((sessionId) => dropChat(sessionId));
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

      <Show when={info() === 'help'}>
        <HelpDialog onClose={() => setInfo(null)} />
      </Show>

      <Show when={info() === 'about'}>
        <AboutDialog onClose={() => setInfo(null)} />
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
        title={name || '未知项目'}
      >
        {workspaceInitial(name)}
      </span>
      <span class="session-title">{truncate(title || '未命名会话', 60)}</span>
      <span class="session-meta">
        <span class="session-time" title={formatAbsoluteTime(time)}>{timeLabel()}</span>
        <span class="session-ws" title={name}>{name}</span>
      </span>
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