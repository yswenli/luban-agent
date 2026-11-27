import { createEffect, createMemo, createSignal, For, onCleanup, onMount, Show } from 'solid-js';
import type { JSX } from 'solid-js';

import { Icon } from '../components/Icons';
import { NewProjectWizard } from '../components/NewProjectWizard';
import { ProjectSidebar } from '../components/ProjectSidebar';
import { createSessionActions } from '../components/SessionActions';
import { SideResizer } from '../components/SideResizer';
import type { DayBucket } from '../lib/format';
import { dayBucket, formatAbsoluteTime, formatSessionTime, localDayKey, truncate } from '../lib/format';
import { openSession } from '../lib/router';
import { sidebarCollapsed, useNarrowViewport } from '../lib/sidebar';
import { workspaceColor, workspaceInitial } from '../lib/workspace';
import {
  UNASSIGNED_PROJECT,
  activeProjectId,
  createSession,
  createWorkspace,
  projectName,
  refreshSessions,
  revealSessionsToken,
  selectedProject,
  selectedProjectId,
  sessionTitle,
  sessionsOfProject,
  sessionsError,
  sessionsLoading,
} from '../stores/app';
import { t } from '../i18n';

/** 会话的展示时间：优先更新时间。 */
function sessionTime(session: { updatedAt?: string | null; createdAt: string }): string {
  return session.updatedAt ?? session.createdAt;
}

/**
 * 首页：左栏「项目 → 会话」树 + 右栏选中项目的详情与会话。
 *
 * `main` 有值时用它替换右栏内容（插件页面借同一套侧栏渲染），侧栏入口与项目树保持一致。
 */
export function SessionListPage(props: { main?: JSX.Element }) {
  const [creating, setCreating] = createSignal(false);
  const [newProject, setNewProject] = createSignal(false);
  const sessionActions = createSessionActions();

  let scrollEl: HTMLDivElement | undefined;

  // 时间标签含「秒前」，需要定时推进；分组用的日期键只在跨日时推进，避免每 10 秒白重排一次列表。
  const [now, setNow] = createSignal(Date.now());
  const [todayKey, setTodayKey] = createSignal(localDayKey(new Date().toISOString()));

  const narrow = useNarrowViewport();
  // 窄屏左栏变横排 chip（见 styles.css 的 760px 断点），那时栏宽由 CSS 接管，拖拽条要隐掉
  const draggable = () => !narrow() && !sidebarCollapsed();

  const project = () => selectedProject();
  const unassigned = () => selectedProjectId() === UNASSIGNED_PROJECT;
  const items = () => sessionsOfProject(activeProjectId());

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

  // 左栏「查看全部」：右栏滚到会话区顶部
  createEffect(() => {
    if (revealSessionsToken() === 0) return;
    scrollEl?.scrollTo({ top: 0 });
  });

  const groups = createMemo(() => {
    void todayKey(); // 跨过午夜要重新分组，否则「今天」标题下会挂着标签写着「昨天」的行
    const list = items();
    return [
      { key: 'today' as const, title: t('sessionList.today'), items: list.filter((s) => dayBucket(sessionTime(s)) === 'today') },
      { key: 'earlier' as const, title: t('sessionList.earlier'), items: list.filter((s) => dayBucket(sessionTime(s)) === 'earlier') },
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

  return (
    <div class="home-body">
      <ProjectSidebar searchFocus />
      <Show when={draggable()}>
        <SideResizer label={t('sessionList.resizeLabel')} />
      </Show>

      <Show when={!props.main} fallback={props.main}>
        <div class="home-main">
          <Show
            when={project() || unassigned()}
            fallback={
              <div class="home-empty">
                <div class="empty">
                  <Icon name="new-project" size={30} />
                  <h2>{t('sessionList.noProjectTitle')}</h2>
                  <p>{t('sessionList.noProjectBody')}</p>
                  <button class="btn primary" onClick={() => setNewProject(true)}>
                    <Icon name="new-project" size={16} />
                    {t('sidebar.newProject')}
                  </button>
                </div>
              </div>
            }
          >
            <div class="project-head">
              <Show
                when={project()}
                fallback={
                  <span class="ws-badge muted">
                    <Icon name="message" size={13} />
                  </span>
                }
              >
                {(current) => (
                  <span class="ws-badge" style={{ background: workspaceColor(current().workspaceId) }}>
                    {workspaceInitial(current().name)}
                  </span>
                )}
              </Show>
              <div class="project-head-main">
                <div class="project-head-title">
                  <span class="project-head-name">
                    {project() ? projectName(project()!) : t('sidebar.unassigned')}
                  </span>
                  <Show when={project()?.hasGit}>
                    <span class="side-git" title={t('sidebar.gitRepo')} role="img" aria-label={t('sidebar.gitRepo')}>
                      <Icon name="git" size={14} />
                    </span>
                  </Show>
                </div>
                <div class="project-head-sub" title={project()?.rootPath}>
                  {project() ? project()!.rootPath : t('sessionList.unassignedHint')}
                </div>
              </div>
              <span class="tag project-count">{t('sessionList.projectSessions', { count: items().length })}</span>
              <button class="icon-btn" title={t('sessionList.refresh')} onClick={() => void refreshSessions()}>
                <Icon name="refresh" size={16} />
              </button>
              <Show when={project()}>
                <button class="btn primary" disabled={creating()} onClick={() => void onNewSession()}>
                  <Icon name="plus" size={15} />
                  {t('topbar.newSession')}
                </button>
              </Show>
            </div>

            <div class="home-scroll" ref={(el) => (scrollEl = el)}>
              <Show when={sessionsError()}>
                <div class="notice error" style={{ 'margin-bottom': '12px' }}>
                  <Icon name="alert" size={16} />
                  <span>{sessionsError()}</span>
                </div>
              </Show>

              <Show when={!sessionsLoading()} fallback={<div class="loading-line">{t('sessionList.loading')}</div>}>
                <Show
                  when={items().length > 0}
                  fallback={
                    <div class="empty">
                      <Icon name="message" size={30} />
                      <Show
                        when={project()}
                        fallback={<p>{t('sessionList.unassignedHint')}</p>}
                      >
                        <h2>{t('sessionList.emptyProjectTitle')}</h2>
                        <p>{t('sessionList.emptyProjectBody')}</p>
                        <button class="btn primary" disabled={creating()} onClick={() => void onNewSession()}>
                          <Icon name="plus" size={16} />
                          {t('topbar.newSession')}
                        </button>
                      </Show>
                    </div>
                  }
                >
                  <For each={groups()}>
                    {(group) => (
                      <div class="group">
                        <div class="group-head">
                          <span class="group-title">{group.title}</span>
                        </div>
                        <For each={group.items}>
                          {(session) =>
                            sessionRow(
                              session,
                              group.key,
                              now,
                              (event, item) =>
                                sessionActions.openMenu(event, item, { deleteOnly: unassigned() }),
                            )
                          }
                        </For>
                      </div>
                    )}
                  </For>
                </Show>
              </Show>
            </div>
          </Show>
        </div>
      </Show>

      <Show when={newProject()}>
        <NewProjectWizard onClose={() => setNewProject(false)} onCreate={(input) => createWorkspace(input)} />
      </Show>

      {sessionActions.render()}
    </div>
  );
}

function sessionRow(
  session: { sessionId: string; title?: string | null; createdAt: string; updatedAt?: string | null },
  bucket: DayBucket,
  now: () => number,
  onMenu: (event: MouseEvent, target: { sessionId: string; title?: string | null }) => void,
) {
  const time = sessionTime(session);
  const label = sessionTitle(session);
  return (
    <div
      class="session-row"
      role="button"
      tabIndex={0}
      onClick={() => openSession(session.sessionId)}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget) return;
        if (event.key === 'Enter' || event.key === ' ') openSession(session.sessionId);
      }}
    >
      <span class="session-title">{truncate(label, 60)}</span>
      <span class="session-meta">
        <span class="session-time" title={formatAbsoluteTime(time)}>
          {formatSessionTime(time, bucket, now())}
        </span>
      </span>
      <span class="session-actions">
        <button class="icon-btn" title={t('common.more')} onClick={(event) => onMenu(event, session)}>
          <Icon name="more" size={16} />
        </button>
      </span>
    </div>
  );
}
