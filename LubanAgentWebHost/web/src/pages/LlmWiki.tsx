import { createSignal, For, onMount, Show } from 'solid-js';

import { ConfirmActionDialog, Modal, TextPromptDialog } from '../components/Dialog';
import { Icon } from '../components/Icons';
import { SideResizer } from '../components/SideResizer';
import { openSession } from '../lib/router';
import { sidebarCollapsed, sideWidth, useNarrowViewport } from '../lib/sidebar';
import { settingsApi } from '../lib/settings';
import type {
  IndexReport,
  LintFinding,
  RetrievalHit,
  WikiFileInfo,
  WikiLintReport,
  WikiStatus,
  Workspace,
} from '../lib/types';
import { indexSummary, wikiApi } from '../lib/wiki';
import { createSessionInWorkspace, refreshSessions, refreshWorkspaces, renameWorkspace, sessions, showToast } from '../stores/app';

/** 一张统计卡。 */
function Stat(props: { label: string; value: string }) {
  return (
    <div class="stat-card">
      <span class="stat-value">{props.value}</span>
      <span class="stat-label">{props.label}</span>
    </div>
  );
}

/**
 * LLM Wiki 知识库管理页。
 *
 * 知识库是 `type=LlmWiki` 的工作区：列表直接筛工作区，索引 / 检索 / 重建 / lint 走 `/llmwiki/*`。
 * 索引与重建是宿主侧同步长任务，按钮期间保持禁用并提示耗时。
 */
export function LlmWikiPage() {
  const [bases, setBases] = createSignal<Workspace[]>([]);
  const [selected, setSelected] = createSignal<string | null>(null);
  const [status, setStatus] = createSignal<WikiStatus | null>(null);
  const [files, setFiles] = createSignal<WikiFileInfo[]>([]);
  const [query, setQuery] = createSignal('');
  const [hits, setHits] = createSignal<RetrievalHit[]>([]);
  const [lint, setLint] = createSignal<WikiLintReport | null>(null);
  const [report, setReport] = createSignal('');
  const [busy, setBusy] = createSignal<string | null>(null);
  const [glob, setGlob] = createSignal('');

  const [creating, setCreating] = createSignal(false);
  const [newPath, setNewPath] = createSignal('');
  const [newName, setNewName] = createSignal('');
  const [saving, setSaving] = createSignal(false);
  const [chatBusy, setChatBusy] = createSignal(false);

  const [pendingDelete, setPendingDelete] = createSignal<Workspace | null>(null);
  const [pendingRename, setPendingRename] = createSignal<Workspace | null>(null);
  const [pendingClear, setPendingClear] = createSignal(false);

  const current = () => bases().find((item) => item.workspaceId === selected()) ?? null;

  const reload = async (keep?: string | null) => {
    try {
      const list = await settingsApi.listWorkspaces();
      const wiki = (list ?? []).filter((item) => item.type === 'LlmWiki');
      setBases(wiki);
      const next = keep ?? selected();
      if (next && wiki.some((item) => item.workspaceId === next)) {
        await select(next);
      } else if (wiki.length > 0) {
        await select(wiki[0].workspaceId);
      } else {
        setSelected(null);
        setStatus(null);
        setFiles([]);
        setHits([]);
        setLint(null);
        setReport('');
      }
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  const select = async (workspaceId: string) => {
    setSelected(workspaceId);
    setHits([]);
    setLint(null);
    setReport('');
    await loadStatus(workspaceId);
    await loadFiles(workspaceId);
  };

  const loadStatus = async (workspaceId: string) => {
    try {
      setStatus(await wikiApi.status(workspaceId));
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  const loadFiles = async (workspaceId: string) => {
    try {
      setFiles(await wikiApi.listFiles(workspaceId));
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  onMount(() => {
    void reload(null);
  });

  const run = async (key: string, action: () => Promise<void>) => {
    if (busy()) return;
    setBusy(key);
    try {
      await action();
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(null);
    }
  };

  const summarize = (result: IndexReport, prefix: string) => {
    setReport(`${prefix}：${indexSummary(result)}`);
    if (result.errors.length > 0) {
      showToast(`${prefix}完成，但有 ${result.errors.length} 项失败`, 'error');
    } else {
      showToast(`${prefix}完成`, 'success');
    }
  };

  const doIndex = () =>
    run('index', async () => {
      const id = selected();
      if (!id) return;
      summarize(await wikiApi.index(id, glob()), '索引');
      await loadStatus(id);
      await loadFiles(id);
    });

  const doRebuild = () =>
    run('rebuild', async () => {
      const id = selected();
      if (!id) return;
      summarize(await wikiApi.rebuild(id), '重建');
      await loadStatus(id);
    });

  const doLint = () =>
    run('lint', async () => {
      const id = selected();
      if (!id) return;
      const result = await wikiApi.lint(id);
      setLint(result);
      showToast(`lint 完成：${result.findings.length} 项发现`, result.findings.length > 0 ? 'error' : 'success');
      await loadStatus(id);
    });

  const doSearch = () =>
    run('search', async () => {
      const id = selected();
      const keyword = query().trim();
      if (!id || !keyword) return;
      setHits(await wikiApi.search(id, keyword));
    });

  const create = async () => {
    const rootPath = newPath().trim();
    if (!rootPath || saving()) return;
    setSaving(true);
    try {
      const workspace = await wikiApi.create(rootPath, newName());
      setCreating(false);
      setNewPath('');
      setNewName('');
      await refreshWorkspaces();
      await reload(workspace?.workspaceId ?? null);
      showToast('已创建知识库', 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setSaving(false);
    }
  };

  const remove = (workspace: Workspace) =>
    run('delete', async () => {
      await wikiApi.remove(workspace.workspaceId);
      await refreshWorkspaces();
      await reload(null);
      showToast('已删除知识库', 'success');
    });

  /** 只改显示名，磁盘目录与索引都不动（与项目重命名同一接口）。 */
  const rename = async (workspace: Workspace, name: string) => {
    if (await renameWorkspace(workspace.workspaceId, name, '已重命名知识库')) {
      await reload(workspace.workspaceId);
    }
  };

  /** 知识库新建时默认未授权：授权后 Agent 才能直接读写该目录。 */
  const authorize = (workspaceId: string) =>
    run('authorize', async () => {
      await settingsApi.authorizeWorkspace(workspaceId);
      await refreshWorkspaces();
      await reload(workspaceId);
      showToast('已授权访问该目录', 'success');
    });

  const clear = () =>
    run('clear', async () => {
      const id = selected();
      if (!id) return;
      await wikiApi.clear(id);
      await loadStatus(id);
      await loadFiles(id);
      showToast('已清空索引', 'success');
    });

  /** 只有已索引文件与切块都存在时才允许对话，避免打开一个零召回的知识库会话。 */
  const indexReady = () =>
    (status()?.indexedFiles ?? 0) > 0 && (status()?.indexedChunks ?? 0) > 0;

  /**
   * 打开知识库对话：每个知识库固定复用同一个会话。
   *
   * 直接按 workspaceId 建会话，不切换后端「当前工作区」——否则首页「新建会话」会落进知识库。
   */
  const startChat = async () => {
    const workspace = current();
    if (!workspace || chatBusy() || !indexReady()) return;

    setChatBusy(true);
    try {
      await refreshSessions();
      const existing = sessions()
        .filter((item) => item.workspaceId === workspace.workspaceId)
        .sort((a, b) => (b.updatedAt ?? b.createdAt).localeCompare(a.updatedAt ?? a.createdAt))[0];
      const sessionId = existing?.sessionId
        ?? await createSessionInWorkspace(workspace.workspaceId, '知识库对话');
      if (sessionId) openSession(sessionId);
    } finally {
      setChatBusy(false);
    }
  };

  const findingText = (finding: LintFinding) => `${finding.path} · ${finding.detail}`;

  const narrow = useNarrowViewport();
  // 窄屏左栏变横排 chip（见 styles.css 的 760px 断点），那时栏宽由 CSS 接管，拖拽条要隐掉
  const draggable = () => !narrow() && !sidebarCollapsed();

  return (
    <div class="home-body">
      <aside
        class="home-side"
        classList={{ collapsed: sidebarCollapsed() && !narrow() }}
        style={sidebarCollapsed() || narrow() ? undefined : { 'flex-basis': `${sideWidth()}px` }}
      >
        <div class="side-head">
          <span class="section-label">知识库</span>
          <div class="side-actions">
            <button class="icon-btn" title="新建知识库" onClick={() => setCreating(true)}>
              <Icon name="plus" size={17} />
            </button>
          </div>
        </div>

        <div class="side-list">
          <For each={bases()}>
            {(item) => (
              <div
                class="side-item"
                classList={{ selected: item.workspaceId === selected() }}
                role="button"
                tabIndex={0}
                title={item.rootPath}
                onClick={() => void select(item.workspaceId)}
                onKeyDown={(event) => {
                  if (event.target !== event.currentTarget) return;
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    void select(item.workspaceId);
                  }
                }}
              >
                <span class="ws-badge">📚</span>
                <span class="side-name">{item.name || item.rootPath}</span>
                <span class="session-actions">
                  <button
                    class="icon-btn"
                    title="重命名知识库"
                    onClick={(event) => {
                      event.stopPropagation();
                      setPendingRename(item);
                    }}
                  >
                    <Icon name="pencil" size={15} />
                  </button>
                  <button
                    class="icon-btn"
                    title="删除知识库"
                    onClick={(event) => {
                      event.stopPropagation();
                      setPendingDelete(item);
                    }}
                  >
                    <Icon name="trash" size={15} />
                  </button>
                </span>
              </div>
            )}
          </For>
          <Show when={bases().length === 0}>
            <span class="side-hint faint">还没有知识库，点右上角新建。</span>
          </Show>
        </div>
      </aside>
      <Show when={draggable()}>
        <SideResizer label="调整知识库栏宽度" />
      </Show>

      <div class="home-main">
        <Show
          when={current()}
          fallback={
            <div class="empty">
              <Icon name="brain" size={30} />
              <h2>还没有知识库</h2>
              <p>知识库把目录里的资料切块索引，供对话检索引用。</p>
              <button class="btn primary" onClick={() => setCreating(true)}>
                <Icon name="plus" size={16} />
                新建知识库
              </button>
            </div>
          }
        >
          {(workspace) => (
            <>
              <div class="home-scroll wiki-main">
                <div class="wiki-head">
                  <div>
                    <div class="dialog-title">{workspace().name || workspace().rootPath}</div>
                    <div class="dialog-sub mono">{workspace().rootPath}</div>
                  </div>
                  <div class="spacer" />
                  <Show when={!workspace().isAuthorized}>
                    <button class="btn ghost" disabled={!!busy()} onClick={() => void authorize(workspace().workspaceId)}>
                      {busy() === 'authorize' ? '授权中…' : '授权目录访问'}
                    </button>
                  </Show>
                  <button class="btn ghost" disabled={!!busy()} onClick={() => void reload(selected())}>
                    <Icon name="refresh" size={15} />
                    刷新
                  </button>
                </div>

                <div class="stat-grid">
                  <Stat label="wiki 页面" value={String(status()?.pageCount ?? 0)} />
                  <Stat label="已索引文件" value={String(status()?.indexedFiles ?? 0)} />
                  <Stat label="已索引切块" value={String(status()?.indexedChunks ?? 0)} />
                </div>
                <Show when={status()?.lastRebuildSummary}>
                  <div class="notice ok" style={{ 'margin-bottom': '12px' }}>
                    <Icon name="check" size={16} />
                    <span>最近重建：{status()!.lastRebuildSummary}</span>
                  </div>
                </Show>

                <div class="wiki-actions">
                  <input
                    class="text-input mono"
                    placeholder="索引文件匹配（可选，如 **/*.md）"
                    value={glob()}
                    onInput={(event) => setGlob(event.currentTarget.value)}
                  />
                  <button class="btn" disabled={!!busy()} onClick={() => void doIndex()}>
                    {busy() === 'index' ? '索引中…' : '索引'}
                  </button>
                  <button class="btn" disabled={!!busy()} onClick={() => void doRebuild()}>
                    {busy() === 'rebuild' ? '重建中…' : '重建'}
                  </button>
                  <button class="btn ghost" disabled={!!busy()} onClick={() => void doLint()}>
                    {busy() === 'lint' ? '校验中…' : 'Lint'}
                  </button>
                  <button class="btn danger" disabled={!!busy()} onClick={() => setPendingClear(true)}>
                    清空索引
                  </button>
                </div>

                <Show when={report()}>
                  <div class="notice" style={{ 'margin-bottom': '12px' }}>
                    <Icon name="info" size={16} />
                    <span>{report()}</span>
                  </div>
                </Show>

                <div class="searchrow" style={{ 'margin-bottom': '12px' }}>
                  <Icon name="search" size={16} />
                  <input
                    type="search"
                    placeholder="在知识库中检索"
                    value={query()}
                    onInput={(event) => setQuery(event.currentTarget.value)}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter') void doSearch();
                    }}
                  />
                  <button
                    class="icon-btn search-action"
                    title="检索"
                    disabled={!!busy() || !query().trim()}
                    onClick={() => void doSearch()}
                  >
                    <Icon name="search" size={16} />
                  </button>
                </div>

                <Show when={hits().length > 0}>
                  <div class="wiki-section">
                    <div class="section-label">检索命中（{hits().length}）</div>
                    <For each={hits()}>
                      {(hit) => (
                        <div class="wiki-row">
                          <div class="wiki-row-head">
                            <span class="mono wiki-path">
                              {hit.filePath}:{hit.startLine}-{hit.endLine}
                            </span>
                            <span class="tag">score {hit.score.toFixed(3)}</span>
                          </div>
                          <div class="wiki-row-body mono">{hit.content}</div>
                        </div>
                      )}
                    </For>
                  </div>
                </Show>

                <Show when={lint()}>
                  {(result) => (
                    <div class="wiki-section">
                      <div class="section-label">
                        Lint 报告 · 扫描 {result().pageCount} 个页面 · {result().findings.length} 项发现
                      </div>
                      <Show
                        when={result().findings.length > 0}
                        fallback={
                          <div class="notice ok">
                            <Icon name="check" size={16} />
                            <span>未发现问题</span>
                          </div>
                        }
                      >
                        <For each={result().findings}>
                          {(finding) => (
                            <div class="wiki-row">
                              <span class="tag">{finding.kind}</span>
                              <span class="mono">{findingText(finding)}</span>
                            </div>
                          )}
                        </For>
                      </Show>
                    </div>
                  )}
                </Show>

                <div class="wiki-section">
                  <div class="section-label">已索引文件（{files().length}）</div>
                  <Show
                    when={files().length > 0}
                    fallback={<span class="side-hint faint">还没有索引，点上方「索引」开始。</span>}
                  >
                    <For each={files()}>
                      {(file) => (
                        <div class="wiki-row">
                          <span class="mono wiki-path">{file.filePath}</span>
                          <span class="tag">{file.language ?? '未知语言'}</span>
                          <span class="faint">{file.chunkCount} 块</span>
                        </div>
                      )}
                    </For>
                  </Show>
                </div>
              </div>
              <div class="wiki-footer">
                <Show when={!indexReady()}>
                  <span class="stat-hint">先完成索引（需有已索引文件与切块）后才能对话</span>
                </Show>
                <button
                  class="btn primary"
                  disabled={!indexReady() || !!busy() || chatBusy()}
                  title={indexReady() ? '与知识库对话（自动检索增强）' : '先完成索引后才能对话'}
                  onClick={() => void startChat()}
                >
                  <Icon name="message" size={16} />
                  {chatBusy() ? '正在打开…' : '对话'}
                </button>
              </div>
            </>
          )}
        </Show>
      </div>

      <Show when={creating()}>
        <Modal
          title="新建知识库"
          subtitle="知识库对应本机上的一个目录，索引其中的资料"
          onClose={() => setCreating(false)}
          footer={
            <>
              <button class="btn ghost" onClick={() => setCreating(false)}>
                取消
              </button>
              <button class="btn primary" disabled={saving() || !newPath().trim()} onClick={() => void create()}>
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
                value={newName()}
                onInput={(event) => setNewName(event.currentTarget.value)}
              />
            </div>
            <div class="field">
              <div class="section-label">根路径（绝对路径）</div>
              <input
                class="text-input mono"
                placeholder="D:\WorkBench\Walle\my-wiki"
                value={newPath()}
                onInput={(event) => setNewPath(event.currentTarget.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') void create();
                }}
              />
            </div>
          </div>
        </Modal>
      </Show>

      <Show when={pendingRename()}>
        {(workspace) => (
          <TextPromptDialog
            title="重命名知识库"
            subtitle="只改显示名，磁盘目录与索引都不受影响"
            label="名称"
            initialValue={workspace().name || workspace().rootPath}
            confirmText="保存"
            onClose={() => setPendingRename(null)}
            onConfirm={(value) => {
              const target = workspace();
              setPendingRename(null);
              void rename(target, value);
            }}
          />
        )}
      </Show>

      <Show when={pendingDelete()}>
        {(workspace) => (
          <ConfirmActionDialog
            title="删除知识库"
            message={`确定删除「${workspace().name || workspace().rootPath}」？该知识库的会话、索引与 .luban-agent 配置会一并清理，磁盘目录其他文件不受影响。`}
            onClose={() => setPendingDelete(null)}
            onConfirm={() => {
              const target = workspace();
              setPendingDelete(null);
              void remove(target);
            }}
          />
        )}
      </Show>

      <Show when={pendingClear()}>
        <ConfirmActionDialog
          title="清空索引"
          message="确定清空该知识库的文件与切块索引？磁盘资料不受影响，之后可重新索引。"
          onClose={() => setPendingClear(false)}
          onConfirm={() => {
            setPendingClear(false);
            void clear();
          }}
        />
      </Show>
    </div>
  );
}