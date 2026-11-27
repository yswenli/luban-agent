import { createEffect, createMemo, createSignal, For, onMount, Show } from 'solid-js';

import { ConfirmActionDialog, TextPromptDialog } from './Dialog';
import { Icon } from './Icons';
import { AboutDialog, HelpDialog } from './InfoDialogs';
import { NewProjectWizard } from './NewProjectWizard';
import { PopMenu, menuAt } from './PopMenu';
import { createSessionActions } from './SessionActions';
import { beginDragSort } from '../lib/dragSort';
import { formatAbsoluteTime, formatRelative, truncate } from '../lib/format';
import { openPlugin, openPlugins, openSession, openSessions, openSettings, openWiki } from '../lib/router';
import { sidebarCollapsed, sideWidth, toggleSidebarCollapsed, useNarrowViewport } from '../lib/sidebar';
import type { Workspace } from '../lib/types';
import { workspaceColor, workspaceInitial } from '../lib/workspace';
import {
  UNASSIGNED_PROJECT,
  consumeSearchFocus,
  createWorkspace,
  deleteWorkspace,
  expandedProjects,
  expandProject,
  moveProject,
  projectName,
  projectQuery,
  projectWorkspaces,
  renameWorkspace,
  revealProjectSessions,
  selectProject,
  selectedProjectId,
  sessionTitle,
  sessionsOfProject,
  setProjectQuery,
  toggleProjectExpanded,
  visibleProjects,
  visibleUnassigned,
} from '../stores/app';
import { dropChat } from '../stores/chats';
import { ensurePluginsLoaded, pluginPageEntries } from '../stores/plugins';
import { t } from '../i18n';

/** 二级菜单里每个项目最多直接列出多少条会话，其余收进「查看全部」。 */
const SESSION_PREVIEW_LIMIT = 8;

type ProjectAction =
  | { kind: 'rename'; workspaceId: string; name: string }
  | { kind: 'delete'; workspaceId: string; name: string };

/** 项目行的悬浮说明：收起态没有项目名，得把名字补进 title。 */
function projectTitle(workspace: Workspace, collapsed: boolean): string {
  return collapsed ? projectName(workspace) : workspace.rootPath;
}

/**
 * 左侧项目栏：首页与会话页共用同一份内容与宽度状态（知识库页有自己的左栏）。
 *
 * 项目行 = 选中 + 展开；再次点击已选中行 = 收起（保留选中）。行首 chevron 只做纯展开/收起。
 * 展开后列出该项目最近 8 条会话，点击会话直接打开。搜索框同时匹配项目名与会话标题。
 */
export function ProjectSidebar(props: { searchFocus?: boolean }) {
  const [projectMenu, setProjectMenu] = createSignal<{ x: number; y: number; workspaceId: string } | null>(null);
  const [projectDialog, setProjectDialog] = createSignal<ProjectAction | null>(null);
  const [newProject, setNewProject] = createSignal(false);
  const [info, setInfo] = createSignal<'help' | 'about' | null>(null);
  const [draggingProject, setDraggingProject] = createSignal<string | null>(null);
  const [dropSlot, setDropSlot] = createSignal<number | null>(null);
  const sessionActions = createSessionActions();
  let listEl: HTMLDivElement | undefined;
  let searchEl: HTMLInputElement | undefined;

  const narrow = useNarrowViewport();
  // 窄屏强制展开：栏位变成横排 chip，收起态在窄屏没有意义
  const collapsed = () => sidebarCollapsed() && !narrow();

  const workspaces = () => projectWorkspaces();
  const selectedId = () => selectedProjectId();
  const searching = () => projectQuery().trim().length > 0;
  const unassigned = createMemo(() => visibleUnassigned());

  /** 搜索期间所有命中项目临时展开，但不写进用户的展开集合；清空搜索即恢复原状。 */
  const isExpanded = (projectId: string): boolean =>
    !collapsed() && !narrow() && (searching() || expandedProjects().includes(projectId));

  onMount(() => {
    void ensurePluginsLoaded();
  });

  // 顶栏点搜索：回到首页后把焦点交给左栏搜索框（首次加载 token 为 0，不抢焦点）。
  // 只有首页左栏消费这个请求（`searchFocus`）：会话页左栏也挂载着，不该抢走输入焦点。
  // 收起态下搜索框不渲染，先把栏展开再聚焦，否则点了搜索没有任何反馈。
  createEffect(() => {
    if (!props.searchFocus || !consumeSearchFocus()) return;
    if (!narrow() && sidebarCollapsed()) toggleSidebarCollapsed();
    queueMicrotask(() => searchEl?.focus());
  });

  const activeProject = createMemo(() => {
    const menu = projectMenu();
    if (!menu) return null;
    return workspaces().find((workspace) => workspace.workspaceId === menu.workspaceId) ?? null;
  });

  function openProjectMenu(event: MouseEvent, workspaceId: string) {
    event.stopPropagation();
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const position = menuAt(rect, { width: 168, height: 92, align: 'end' });
    setProjectMenu({ ...position, workspaceId });
  }

  /** 项目行点击：选中 + 展开；再点已选中行则收起（保留选中）。 */
  function onProjectClick(projectId: string): void {
    const wasSelected = selectedId() === projectId;
    selectProject(projectId);
    // 收起态、窄屏横排 chip 与搜索态都没有二级菜单可开关，点击只负责切换右栏
    if (collapsed() || narrow() || searching()) return;
    if (wasSelected) toggleProjectExpanded(projectId);
    else expandProject(projectId);
  }

  function onSessionClick(projectId: string, sessionId: string): void {
    selectProject(projectId);
    openSession(sessionId);
  }

  /**
   * 项目拖放排序：宽屏竖排按上下算，窄屏横排 chip 按左右算。
   *
   * 收起态只剩图标、没有项目名可对齐，不开放排序；搜索态列出的是过滤后的子集，
   * 下标与完整顺序对不上，也禁用；「未归属」节点不参与排序，由 `itemSelector` 排除。
   * 会话行不绑定拖拽处理器。
   */
  function onProjectPointerDown(event: PointerEvent, workspaceId: string, index: number): void {
    if (collapsed() || searching()) return;
    if ((event.target as HTMLElement).closest('.session-actions, .side-chevron')) return;
    beginDragSort({
      event,
      item: event.currentTarget as HTMLElement,
      container: listEl,
      itemSelector: '.side-item:not(.unassigned)',
      axis: narrow() ? 'x' : 'y',
      index,
      onStart: () => setDraggingProject(workspaceId),
      onSlot: setDropSlot,
      onDrop: (toIndex) => moveProject(workspaceId, toIndex),
      onEnd: () => {
        setDraggingProject(null);
        setDropSlot(null);
      },
    });
  }

  return (
    <>
      <aside
        class="home-side"
        classList={{ collapsed: collapsed() }}
        style={narrow() || collapsed() ? undefined : { 'flex-basis': `${sideWidth()}px` }}
      >
        <div class="side-head">
          <Show when={!narrow()}>
            <button
              class="icon-btn side-toggle"
              title={collapsed() ? t('sidebar.expand') : t('sidebar.collapse')}
              aria-label={collapsed() ? t('sidebar.expand') : t('sidebar.collapse')}
              aria-expanded={!collapsed()}
              onClick={() => toggleSidebarCollapsed()}
            >
              <Icon name="panel-left" size={17} />
            </button>
          </Show>
          <Show when={!collapsed()}>
            <span class="section-label">{t('sidebar.projects')}</span>
          </Show>
          <div class="side-actions">
            <button class="icon-btn side-new-project" title={t('sidebar.newProject')} onClick={() => setNewProject(true)}>
              <Icon name="new-project" size={17} />
            </button>
          </div>
        </div>

        <Show when={!collapsed()}>
          <div class="side-search">
            <Icon name="search" size={15} />
            <input
              ref={searchEl}
              type="search"
              placeholder={t('sidebar.searchProjects')}
              aria-label={t('sidebar.searchProjects')}
              value={projectQuery()}
              onInput={(event) => setProjectQuery(event.currentTarget.value)}
              onKeyDown={(event) => {
                if (event.key === 'Escape') {
                  setProjectQuery('');
                  event.currentTarget.blur();
                }
              }}
            />
            <Show when={searching()}>
              <button class="icon-btn" title={t('sidebar.clearSearch')} onClick={() => setProjectQuery('')}>
                <Icon name="close" size={13} />
              </button>
            </Show>
          </div>
        </Show>

        <div class="side-list" ref={(el) => (listEl = el)} data-axis={narrow() ? 'x' : 'y'}>
          <For each={visibleProjects()}>
            {(entry, index) => {
              const projectId = entry.project.workspaceId;
              const expanded = () => isExpanded(projectId);
              const shown = () =>
                searching() ? entry.sessions : entry.sessions.slice(0, SESSION_PREVIEW_LIMIT);

              return (
                <>
                  <div
                    class="side-item"
                    classList={{
                      selected: projectId === selectedId(),
                      dragging: draggingProject() === projectId,
                      'drop-before': dropSlot() === index(),
                      'drop-after': dropSlot() === visibleProjects().length && index() === visibleProjects().length - 1,
                    }}
                    role="button"
                    tabIndex={0}
                    aria-expanded={expanded()}
                    title={projectTitle(entry.project, collapsed())}
                    onPointerDown={(event) => onProjectPointerDown(event, projectId, index())}
                    onClick={() => onProjectClick(projectId)}
                    onKeyDown={(event) => {
                      // 行内按钮 / chevron 的按键不触发行点击
                      if (event.target !== event.currentTarget) return;
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();
                        onProjectClick(projectId);
                      }
                    }}
                  >
                    <Show when={!collapsed()}>
                      <button
                        class="side-chevron"
                        aria-expanded={expanded()}
                        title={expanded() ? t('sidebar.collapseProject') : t('sidebar.expandProject')}
                        onClick={(event) => {
                          event.stopPropagation();
                          if (!searching()) toggleProjectExpanded(projectId);
                        }}
                      >
                        <Icon name={expanded() ? 'chevron-down' : 'chevron-right'} size={13} />
                      </button>
                    </Show>
                    <span class="ws-badge" style={{ background: workspaceColor(projectId) }}>
                      {workspaceInitial(entry.project.name)}
                    </span>
                    <Show when={!collapsed()}>
                      <span class="side-name">{projectName(entry.project)}</span>
                      <Show when={entry.project.hasGit}>
                        <span class="side-git" title={t('sidebar.gitRepo')} role="img" aria-label={t('sidebar.gitRepo')}>
                          <Icon name="git" size={14} />
                        </span>
                      </Show>
                      <span class="session-actions">
                        <button
                          class="icon-btn"
                          title={t('common.more')}
                          aria-label={t('sidebar.projectActions')}
                          onClick={(event) => openProjectMenu(event, projectId)}
                        >
                          <Icon name="more" size={16} />
                        </button>
                      </span>
                    </Show>
                  </div>

                  <Show when={expanded()}>
                    <div class="side-sessions">
                      <For each={shown()}>
                        {(session) => {
                          const time = session.updatedAt ?? session.createdAt;
                          const label = sessionTitle(session);
                          return (
                            <div
                              class="side-session"
                              role="button"
                              tabIndex={0}
                              title={label}
                              onClick={() => onSessionClick(projectId, session.sessionId)}
                              onKeyDown={(event) => {
                                if (event.target !== event.currentTarget) return;
                                if (event.key === 'Enter' || event.key === ' ') {
                                  event.preventDefault();
                                  onSessionClick(projectId, session.sessionId);
                                }
                              }}
                            >
                              <span class="side-session-title">{truncate(label, 60)}</span>
                              <span class="side-session-time" title={formatAbsoluteTime(time)}>
                                {formatRelative(time)}
                              </span>
                              <span class="session-actions">
                                <button
                                  class="icon-btn"
                                  title={t('common.more')}
                                  aria-label={t('common.more')}
                                  onClick={(event) => sessionActions.openMenu(event, session)}
                                >
                                  <Icon name="more" size={15} />
                                </button>
                              </span>
                            </div>
                          );
                        }}
                      </For>
                      <Show when={!searching() && entry.sessions.length > SESSION_PREVIEW_LIMIT}>
                        <button
                          class="side-session-more"
                          onClick={() => {
                            revealProjectSessions(projectId);
                            openSessions();
                          }}
                        >
                          {t('sidebar.viewAllSessions', { count: entry.sessions.length })}
                        </button>
                      </Show>
                    </div>
                  </Show>
                </>
              );
            }}
          </For>

          {/* 未归属：旧会话没有 workspaceId，单列一个虚拟节点；不可拖拽、不可重命名。 */}
          <Show when={unassigned().visible}>
            <div
              class="side-item unassigned"
              classList={{ selected: selectedId() === UNASSIGNED_PROJECT }}
              role="button"
              tabIndex={0}
              aria-expanded={isExpanded(UNASSIGNED_PROJECT)}
              title={t('sidebar.unassigned')}
              onClick={() => onProjectClick(UNASSIGNED_PROJECT)}
              onKeyDown={(event) => {
                if (event.target !== event.currentTarget) return;
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  onProjectClick(UNASSIGNED_PROJECT);
                }
              }}
            >
              <Show when={!collapsed()}>
                <button
                  class="side-chevron"
                  aria-expanded={isExpanded(UNASSIGNED_PROJECT)}
                  title={
                    isExpanded(UNASSIGNED_PROJECT) ? t('sidebar.collapseProject') : t('sidebar.expandProject')
                  }
                  onClick={(event) => {
                    event.stopPropagation();
                    if (!searching()) toggleProjectExpanded(UNASSIGNED_PROJECT);
                  }}
                >
                  <Icon name={isExpanded(UNASSIGNED_PROJECT) ? 'chevron-down' : 'chevron-right'} size={13} />
                </button>
              </Show>
              <span class="ws-badge muted">
                <Icon name="message" size={13} />
              </span>
              <Show when={!collapsed()}>
                <span class="side-name">{t('sidebar.unassigned')}</span>
              </Show>
            </div>

            <Show when={isExpanded(UNASSIGNED_PROJECT)}>
              <div class="side-sessions">
                <For each={searching() ? unassigned().sessions : unassigned().sessions.slice(0, SESSION_PREVIEW_LIMIT)}>
                  {(session) => {
                    const time = session.updatedAt ?? session.createdAt;
                    const label = sessionTitle(session);
                    return (
                      <div
                        class="side-session"
                        role="button"
                        tabIndex={0}
                        title={label}
                        onClick={() => onSessionClick(UNASSIGNED_PROJECT, session.sessionId)}
                        onKeyDown={(event) => {
                          if (event.target !== event.currentTarget) return;
                          if (event.key === 'Enter' || event.key === ' ') {
                            event.preventDefault();
                            onSessionClick(UNASSIGNED_PROJECT, session.sessionId);
                          }
                        }}
                      >
                        <span class="side-session-title">{truncate(label, 60)}</span>
                        <span class="side-session-time" title={formatAbsoluteTime(time)}>
                          {formatRelative(time)}
                        </span>
                        <span class="session-actions">
                          <button
                            class="icon-btn"
                            title={t('common.more')}
                            aria-label={t('common.more')}
                            onClick={(event) => sessionActions.openMenu(event, session, { deleteOnly: true })}
                          >
                            <Icon name="more" size={15} />
                          </button>
                        </span>
                      </div>
                    );
                  }}
                </For>
                <Show when={!searching() && unassigned().sessions.length > SESSION_PREVIEW_LIMIT}>
                  <button
                    class="side-session-more"
                    onClick={() => {
                      revealProjectSessions(UNASSIGNED_PROJECT);
                      openSessions();
                    }}
                  >
                    {t('sidebar.viewAllSessions', { count: unassigned().sessions.length })}
                  </button>
                </Show>
              </div>
            </Show>
          </Show>

          <Show when={!collapsed() && visibleProjects().length === 0 && !unassigned().visible}>
            <span class="side-hint faint">{searching() ? t('sidebar.noMatch') : t('sidebar.noProjects')}</span>
          </Show>
        </div>

        <div class="side-links">
          <button class="side-link" title={t('sidebar.wiki')} onClick={() => openWiki()}>
            <Icon name="file" size={16} />
            <Show when={!collapsed()}>{t('sidebar.wiki')}</Show>
          </button>
          <button class="side-link" title={t('sidebar.plugins')} onClick={() => openPlugins()}>
            <Icon name="plug" size={16} />
            <Show when={!collapsed()}>{t('sidebar.plugins')}</Show>
          </button>
          <button class="side-link" title={t('sidebar.settings')} onClick={() => openSettings()}>
            <Icon name="settings" size={16} />
            <Show when={!collapsed()}>{t('sidebar.settings')}</Show>
          </button>
          <button class="side-link" title={t('app.help')} onClick={() => setInfo('help')}>
            <Icon name="help" size={16} />
            <Show when={!collapsed()}>{t('app.help')}</Show>
          </button>
          <button class="side-link" title={t('app.about')} onClick={() => setInfo('about')}>
            <Icon name="info" size={16} />
            <Show when={!collapsed()}>{t('app.about')}</Show>
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
                    <Show when={entry.iconUrl} fallback={<Icon name={entry.icon || 'plug'} size={16} />}>
                      {(url) => <img class="side-plugin-logo" src={url()} width={16} height={16} alt="" />}
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

      <Show when={projectMenu()}>
        {(menu) => (
          <PopMenu
            style={{ left: `${menu().x}px`, top: `${menu().y}px`, 'min-width': '168px' }}
            onClose={() => setProjectMenu(null)}
          >
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
              {t('common.rename')}
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
              {t('common.delete')}
            </button>
          </PopMenu>
        )}
      </Show>

      <Show when={projectDialog()?.kind === 'rename' ? (projectDialog() as Extract<ProjectAction, { kind: 'rename' }>) : null}>
        {(action) => (
          <TextPromptDialog
            title={t('sidebar.renameProject')}
            subtitle={t('sidebar.renameHint')}
            label={t('common.name')}
            initialValue={action().name}
            confirmText={t('common.save')}
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
            title={t('sidebar.deleteProject')}
            message={(() => {
              const count = sessionsOfProject(action().workspaceId).length;
              const cascade = count > 0 ? t('sidebar.deleteSessionsNote', { count }) : '';
              return t('sidebar.deleteConfirm', { name: truncate(action().name || t('topbar.unnamedProject'), 40), cascade });
            })()}
            onClose={() => setProjectDialog(null)}
            onConfirm={() => {
              const target = action().workspaceId;
              const removed = sessionsOfProject(target).map((session) => session.sessionId);
              setProjectDialog(null);
              void deleteWorkspace(target).then((ok) => {
                if (ok) removed.forEach((sessionId) => dropChat(sessionId));
              });
            }}
          />
        )}
      </Show>

      <Show when={newProject()}>
        <NewProjectWizard onClose={() => setNewProject(false)} onCreate={(input) => createWorkspace(input)} />
      </Show>

      <Show when={info() === 'help'}>
        <HelpDialog onClose={() => setInfo(null)} />
      </Show>

      <Show when={info() === 'about'}>
        <AboutDialog onClose={() => setInfo(null)} />
      </Show>

      {sessionActions.render()}
    </>
  );
}
