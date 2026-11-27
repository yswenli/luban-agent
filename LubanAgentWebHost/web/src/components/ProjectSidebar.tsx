import { createMemo, createSignal, For, onMount, Show } from 'solid-js';

import { ConfirmActionDialog, Modal, TextPromptDialog } from './Dialog';
import { Icon } from './Icons';
import { AboutDialog, HelpDialog } from './InfoDialogs';
import { PopMenu, menuAt } from './PopMenu';
import { beginDragSort } from '../lib/dragSort';
import { truncate } from '../lib/format';
import { openPlugin, openPlugins, openSettings, openWiki } from '../lib/router';
import { sidebarCollapsed, sideWidth, toggleSidebarCollapsed, useNarrowViewport } from '../lib/sidebar';
import type { Workspace } from '../lib/types';
import { workspaceColor, workspaceInitial } from '../lib/workspace';
import {
  clearProjectFilter,
  createWorkspace,
  deleteWorkspace,
  filterWorkspaceId,
  moveProject,
  projectWorkspaces,
  renameWorkspace,
  selectProject,
  sessions,
  type CreateWorkspaceInput,
  type CreateWorkspaceResult,
} from '../stores/app';
import { dropChat } from '../stores/chats';
import { ensurePluginsLoaded, pluginPageEntries } from '../stores/plugins';
import { t } from '../i18n';

type ProjectAction =
  | { kind: 'rename'; workspaceId: string; name: string }
  | { kind: 'delete'; workspaceId: string; name: string };

/** 项目行的悬浮说明：收起态没有项目名，得把名字补进 title。 */
function projectTitle(workspace: Workspace, collapsed: boolean, selected: boolean): string {
  const suffix = selected ? ` ${t('sidebar.filterAgain')}` : '';
  if (collapsed) return `${workspace.name || workspace.rootPath}${suffix}`;
  return `${workspace.rootPath}${suffix}`;
}

/**
 * 左侧项目栏：首页、知识库页与会话页共用同一份内容与宽度状态。
 *
 * 点项目做什么由 `onSelect` 决定——首页是筛选会话列表，会话页是跳到该项目最近的会话。
 * 不传 `onSelect` 时按首页语义（筛选 + 切换后端当前工作区）。
 * `selectedWorkspaceId` 用于会话语义下的高亮：首页不传，改用筛选值。
 */
export function ProjectSidebar(props: {
  onSelect?: (workspaceId: string) => void | Promise<void>;
  selectedWorkspaceId?: string | null;
}) {
  const [projectMenu, setProjectMenu] = createSignal<{ x: number; y: number; workspaceId: string } | null>(null);
  const [projectDialog, setProjectDialog] = createSignal<ProjectAction | null>(null);
  const [newProject, setNewProject] = createSignal(false);
  const [info, setInfo] = createSignal<'help' | 'about' | null>(null);
  const [draggingProject, setDraggingProject] = createSignal<string | null>(null);
  const [dropSlot, setDropSlot] = createSignal<number | null>(null);
  let listEl: HTMLDivElement | undefined;

  const narrow = useNarrowViewport();
  // 窄屏强制展开：栏位变成横排 chip，收起态在窄屏没有意义
  const collapsed = () => sidebarCollapsed() && !narrow();

  const workspaces = () => projectWorkspaces();
  const selectedId = () => (props.selectedWorkspaceId !== undefined ? props.selectedWorkspaceId : filterWorkspaceId());

  onMount(() => {
    void ensurePluginsLoaded();
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

  function select(workspaceId: string) {
    if (props.onSelect) void props.onSelect(workspaceId);
    else void selectProject(workspaceId);
  }

  /**
   * 项目拖放排序：宽屏竖排按上下算，窄屏横排 chip 按左右算。
   *
   * 收起态只剩图标、没有项目名可对齐，不开放排序；「更多」按钮自己响应点击。
   */
  function onProjectPointerDown(event: PointerEvent, workspaceId: string, index: number): void {
    if (collapsed()) return;
    if ((event.target as HTMLElement).closest('.session-actions')) return;
    beginDragSort({
      event,
      item: event.currentTarget as HTMLElement,
      container: listEl,
      itemSelector: '.side-item',
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
            <Show when={!props.onSelect && filterWorkspaceId()}>
              <button
                class="icon-btn side-clear-filter"
                title={t('sidebar.showAllProjects')}
                onClick={() => clearProjectFilter()}
              >
                <Icon name="close" size={15} />
              </button>
            </Show>
            <button class="icon-btn side-new-project" title={t('sidebar.newProject')} onClick={() => setNewProject(true)}>
              <Icon name="new-project" size={17} />
            </button>
          </div>
        </div>

        <div class="side-list" ref={(el) => (listEl = el)} data-axis={narrow() ? 'x' : 'y'}>
          <For each={workspaces()}>
            {(workspace, index) => (
              <div
                class="side-item"
                classList={{
                  selected: workspace.workspaceId === selectedId(),
                  dragging: draggingProject() === workspace.workspaceId,
                  'drop-before': dropSlot() === index(),
                  'drop-after':
                    dropSlot() === workspaces().length && index() === workspaces().length - 1,
                }}
                role="button"
                tabIndex={0}
                title={projectTitle(workspace, collapsed(), workspace.workspaceId === selectedId())}
                onPointerDown={(event) => onProjectPointerDown(event, workspace.workspaceId, index())}
                onClick={() => select(workspace.workspaceId)}
                onKeyDown={(event) => {
                  // 行内「更多」按钮的按键不触发行点击
                  if (event.target !== event.currentTarget) return;
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    select(workspace.workspaceId);
                  }
                }}
              >
                <span class="ws-badge" style={{ background: workspaceColor(workspace.workspaceId) }}>
                  {workspaceInitial(workspace.name)}
                </span>
                <Show when={!collapsed()}>
                  <span class="side-name">{workspace.name || workspace.rootPath}</span>
                  <Show when={workspace.hasGit}>
                    <span class="side-git" title={t('sidebar.gitRepo')} role="img" aria-label={t('sidebar.gitRepo')}>
                      <Icon name="git" size={14} />
                    </span>
                  </Show>
                  <span class="session-actions">
                    <button
                      class="icon-btn"
                      title={t('common.more')}
                      aria-label={t('sidebar.projectActions')}
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
            <span class="side-hint faint">{t('sidebar.noProjects')}</span>
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
              const count = sessions().filter((session) => session.workspaceId === action().workspaceId).length;
              const cascade = count > 0 ? t('sidebar.deleteSessionsNote', { count }) : '';
              return t('sidebar.deleteConfirm', { name: truncate(action().name || t('topbar.unnamedProject'), 40), cascade });
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
          onCreate={(input) => createWorkspace(input)}
        />
      </Show>

      <Show when={info() === 'help'}>
        <HelpDialog onClose={() => setInfo(null)} />
      </Show>

      <Show when={info() === 'about'}>
        <AboutDialog onClose={() => setInfo(null)} />
      </Show>
    </>
  );
}

/**
 * 新建项目弹窗：可选本机目录或 git 仓库。
 *
 * git 模式由宿主克隆到 `~/.luban-agent/repositories/{项目名}`；同名目录已存在时不静默覆盖，
 * 先返回冲突提示，由用户确认是否复用（复用即在该目录上直接工作）。
 */
function NewProjectDialog(props: {
  onClose: () => void;
  onCreate: (input: CreateWorkspaceInput) => Promise<CreateWorkspaceResult>;
}) {
  const [source, setSource] = createSignal<'directory' | 'git'>('directory');
  const [rootPath, setRootPath] = createSignal('');
  const [gitUrl, setGitUrl] = createSignal('');
  const [name, setName] = createSignal('');
  const [saving, setSaving] = createSignal(false);
  const [error, setError] = createSignal<string | null>(null);
  /** 409 冲突：`reuse` 目标目录已存在（git 克隆），`share` 目录已被其他用户登记。 */
  const [conflict, setConflict] = createSignal<{ message: string; kind: 'reuse' | 'share' } | null>(null);
  let rootInput!: HTMLInputElement;
  let gitInput!: HTMLInputElement;

  onMount(() => {
    window.setTimeout(() => rootInput?.focus(), 30);
  });

  const canSubmit = () =>
    !saving() && (source() === 'directory' ? rootPath().trim().length > 0 : gitUrl().trim().length > 0);

  const changeSource = (next: 'directory' | 'git') => {
    if (saving() || source() === next) return;
    setSource(next);
    setError(null);
    setConflict(null);
    window.setTimeout(() => (next === 'git' ? gitInput : rootInput)?.focus(), 30);
  };

  const submit = async (reuseExisting = false, shareExisting = false) => {
    if (!canSubmit()) return;
    setSaving(true);
    setError(null);
    try {
      const result = await props.onCreate({
        source: source(),
        rootPath: rootPath().trim(),
        gitUrl: gitUrl().trim(),
        name: name().trim(),
        reuseExisting,
        shareExisting,
      });
      if (result.ok) {
        props.onClose();
      } else if (result.code === 409) {
        // 目录冲突单独展示：给用户「复用/共用」与「改名字/换地址」两条出路
        setConflict({
          message: result.error ?? t('sidebar.targetExists'),
          kind: result.reason === 'in-use' ? 'share' : 'reuse',
        });
      } else {
        setError(result.error ?? t('sidebar.createFailed'));
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      title={t('sidebar.newProject')}
      subtitle={t('sidebar.newProjectHint')}
      onClose={props.onClose}
      footer={
        <>
          <button class="btn ghost" onClick={props.onClose}>
            {t('common.cancel')}
          </button>
          <button class="btn primary" disabled={!canSubmit()} onClick={() => void submit()}>
            {saving() ? t('common.creating') : t('common.create')}
          </button>
        </>
      }
    >
      <div class="form-stack">
        <div class="field">
          <div class="section-label">{t('sidebar.source')}</div>
          <select
            class="text-input"
            value={source()}
            disabled={saving()}
            onChange={(event) => changeSource(event.currentTarget.value === 'git' ? 'git' : 'directory')}
          >
            <option value="directory">{t('sidebar.sourceDirectory')}</option>
            <option value="git">{t('sidebar.sourceGit')}</option>
          </select>
        </div>

        <div class="field">
          <div class="section-label">{t('common.nameOptional')}</div>
          <input
            class="text-input"
            placeholder={source() === 'git' ? t('sidebar.nameFromRepo') : t('sidebar.nameFromDir')}
            value={name()}
            onInput={(event) => setName(event.currentTarget.value)}
          />
        </div>

        <Show
          when={source() === 'git'}
          fallback={
            <div class="field">
              <div class="section-label">{t('common.rootPath')}</div>
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
              <div class="field-hint">
                {t('sidebar.rootHint')}
              </div>
            </div>
          }
        >
          <div class="field">
            <div class="section-label">{t('sidebar.repoUrl')}</div>
            <input
              ref={gitInput}
              class="text-input mono"
              placeholder="https://git.example.com/group/repo.git"
              value={gitUrl()}
              onInput={(event) => setGitUrl(event.currentTarget.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') void submit();
              }}
            />
            <div class="field-hint">
              {t('sidebar.cloneHint')}
            </div>
          </div>
        </Show>

        <Show when={conflict()}>
          {(value) => (
            <>
              <div class="auth-note">
                <Icon name="alert" size={15} />
                <span>{value().message}</span>
              </div>
              <button
                class="btn ghost"
                disabled={saving()}
                onClick={() =>
                  void (value().kind === 'share'
                    ? submit(source() === 'git', true)
                    : submit(true))
                }
              >
                {value().kind === 'share' ? t('sidebar.reuseWorkspace') : t('sidebar.reuseDirectory')}
              </button>
            </>
          )}
        </Show>

        <Show when={error()}>
          <div class="auth-error">
            <Icon name="alert" size={15} />
            <span>{error()}</span>
          </div>
        </Show>
      </div>
    </Modal>
  );
}