import { createEffect, createMemo, createSignal, For, onCleanup, Show, untrack } from 'solid-js';

import { ApiError } from '../lib/api';
import { filePreviewName, fileReadUrl, loadFilePreview, type FilePreviewContent } from '../lib/filePreview';
import {
  closeWorkspacePanel,
  expandedDirsOf,
  selectedFileOf,
  setDirExpanded,
  setSelectedFile,
  setWorkspaceShowIgnored,
  treeStateVersion,
  workspaceShowIgnored,
} from '../lib/workspacePanel';
import { fileGlyph, fileKindClass, listWorkspaceDir, type WorkspaceEntry } from '../lib/workspaceFiles';
import { workspaceName } from '../stores/app';

import { Icon } from './Icons';
import { Markdown } from './Markdown';

/** 一层目录的加载状态；键是相对工作区根的路径（根为空串）。 */
interface DirState {
  loading: boolean;
  error: string | null;
  entries: WorkspaceEntry[];
  truncated: boolean;
}

/**
 * 右侧「工作区」面板：左列目录树 + 右侧文件预览。
 *
 * 目录按需展开（每次只列一层），文件内容复用会话里的 `/file/read` 预览能力：
 * Markdown 排版、源码纯文本、图片、HTML 沙箱 iframe，音视频直接交给浏览器流式播放。
 */
export function WorkspacePanel(props: { sessionId: string; workspaceId: string | null; width: number }) {
  const [dirs, setDirs] = createSignal<Record<string, DirState>>({});
  const [filter, setFilter] = createSignal('');
  const [content, setContent] = createSignal<FilePreviewContent | null>(null);
  const [contentError, setContentError] = createSignal<string | null>(null);
  const [contentLoading, setContentLoading] = createSignal(false);
  const [reloadToken, setReloadToken] = createSignal(0);

  let objectUrl: string | null = null;
  let previewSequence = 0;
  let directoryToken = 0;

  const selected = () => selectedFileOf(props.sessionId);
  // memo 去重：展开/收起目录也会 bump 版本信号，路径没变时不要重读文件
  const selectedPath = createMemo(selected);

  const releaseObjectUrl = () => {
    // 音视频的 url 是接口地址而不是 blob：只回收自己创建的 blob
    if (objectUrl?.startsWith('blob:')) URL.revokeObjectURL(objectUrl);
    objectUrl = null;
  };

  async function loadDir(path: string, token: number): Promise<void> {
    const workspaceId = props.workspaceId;
    if (!workspaceId) return;

    setDirs((previous) => ({
      ...previous,
      [path]: {
        loading: true,
        error: null,
        entries: previous[path]?.entries ?? [],
        truncated: previous[path]?.truncated ?? false,
      },
    }));

    try {
      const listing = await listWorkspaceDir(workspaceId, path, workspaceShowIgnored());
      if (token !== directoryToken) return;
      setDirs((previous) => ({
        ...previous,
        [path]: { loading: false, error: null, entries: listing.entries, truncated: listing.truncated },
      }));
    } catch (cause) {
      if (token !== directoryToken) return;
      setDirs((previous) => ({
        ...previous,
        [path]: { loading: false, error: (cause as Error).message, entries: [], truncated: false },
      }));
    }
  }

  // 会话 / 工作区 / 「显示忽略项」/ 手动刷新 变化时：清空树，重新拉根目录与已展开目录。
  // 展开集合要 untrack 读取：它随点击自增版本信号，若在此建立依赖，点一个节点就会把整棵树清空重拉
  createEffect(() => {
    const workspaceId = props.workspaceId;
    const sessionId = props.sessionId;
    workspaceShowIgnored();
    reloadToken();

    directoryToken += 1;
    const token = directoryToken;
    setDirs({});
    if (!workspaceId) return;

    void loadDir('', token);
    for (const path of untrack(() => expandedDirsOf(sessionId))) void loadDir(path, token);
  });

  // 选中文件或手动刷新时重取内容；先到的旧响应按序号丢弃，blob 立即回收
  createEffect(() => {
    const path = selectedPath();
    reloadToken();
    previewSequence += 1;
    const requestId = previewSequence;

    releaseObjectUrl();
    setContent(null);
    setContentError(null);
    if (!path) {
      setContentLoading(false);
      return;
    }

    setContentLoading(true);
    void loadFilePreview({ path, workspaceId: props.workspaceId ?? undefined })
      .then((value) => {
        if (requestId !== previewSequence) {
          if ('url' in value && value.url.startsWith('blob:')) URL.revokeObjectURL(value.url);
          return;
        }
        if ('url' in value) objectUrl = value.url;
        setContent(value);
      })
      .catch((cause: unknown) => {
        if (requestId !== previewSequence) return;
        setContentError(
          cause instanceof ApiError
            ? cause.message
            : '读取失败：文件可能已被移动、删除，或当前账号没有权限。',
        );
      })
      .finally(() => {
        if (requestId === previewSequence) setContentLoading(false);
      });
  });

  // 关闭面板时让在途预览响应作废：迟到的 blob 会走「旧响应」分支立即回收，不会留在内存里
  onCleanup(() => {
    previewSequence += 1;
    releaseObjectUrl();
  });

  const toggleDir = (path: string) => {
    const expanded = expandedDirsOf(props.sessionId).has(path);
    setDirExpanded(props.sessionId, path, !expanded);
    if (!expanded && !dirs()[path]) void loadDir(path, directoryToken);
  };

  const query = () => filter().trim().toLowerCase();

  /** 已加载节点里是否有名字命中筛选：只过滤已展开的内容，未加载的目录不参与。 */
  function hasLoadedMatch(path: string, keyword: string): boolean {
    const state = dirs()[path];
    if (!state) return false;
    return state.entries.some(
      (entry) =>
        entry.name.toLowerCase().includes(keyword) ||
        (entry.type === 'dir' && hasLoadedMatch(entry.path, keyword)),
    );
  }

  function matchesFilter(entry: WorkspaceEntry): boolean {
    const keyword = query();
    if (!keyword) return true;
    if (entry.name.toLowerCase().includes(keyword)) return true;
    return entry.type === 'dir' && hasLoadedMatch(entry.path, keyword);
  }

  /** 递归渲染一层目录；Solid 的响应式依赖由读到的信号自动建立。 */
  function TreeLevel(level: { parent: string; depth: number }) {
    treeStateVersion();
    const state = () => dirs()[level.parent];

    return (
      <Show when={state()} fallback={<div class="ws-tree-note">加载中…</div>}>
        {(dir) => (
          <>
            <Show when={dir().loading && dir().entries.length === 0}>
              <div class="ws-tree-note">加载中…</div>
            </Show>
            <Show when={dir().error}>
              {(message) => <div class="ws-tree-note is-error">{message()}</div>}
            </Show>
            <For each={dir().entries}>
              {(entry) => (
                <Show when={matchesFilter(entry)}>
                  <button
                    class="ws-node"
                    classList={{ 'is-dir': entry.type === 'dir', selected: entry.path === selected() }}
                    style={{ 'padding-left': `${6 + level.depth * 12}px` }}
                    title={entry.path}
                    onClick={() => {
                      if (entry.type === 'dir') toggleDir(entry.path);
                      else setSelectedFile(props.sessionId, entry.path);
                    }}
                  >
                    <Show
                      when={entry.type === 'dir'}
                      fallback={
                        <>
                          <span class={`ws-glyph ${fileKindClass(entry.name)}`}>{fileGlyph(entry.name)}</span>
                          <span class="ws-node-name">{entry.name}</span>
                        </>
                      }
                    >
                      <span class="ws-caret">
                        <Icon
                          name={expandedDirsOf(props.sessionId).has(entry.path) ? 'chevron-down' : 'chevron-right'}
                          size={12}
                        />
                      </span>
                      <Icon name="folder" size={14} class="ws-folder" />
                      <span class="ws-node-name">{entry.name}</span>
                    </Show>
                  </button>
                  <Show when={entry.type === 'dir' && expandedDirsOf(props.sessionId).has(entry.path)}>
                    <TreeLevel parent={entry.path} depth={level.depth + 1} />
                  </Show>
                </Show>
              )}
            </For>
            <Show when={dir().truncated}>
              <div class="ws-tree-note">条目过多，仅显示前一部分。</div>
            </Show>
          </>
        )}
      </Show>
    );
  }

  const previewText = () => {
    const value = content();
    return value && (value.kind === 'markdown' || value.kind === 'text') ? value.text : '';
  };

  /** 图片 / 音视频 / HTML 的来源地址；文本类为空串。 */
  const previewUrl = () => {
    const value = content();
    return value && 'url' in value ? value.url : '';
  };

  return (
    <aside
      class="workspace-panel"
      aria-label="工作区"
      style={{ '--ws-width': `${props.width}px` }}
    >
      <div class="ws-head">
        <div class="ws-title">
          <span class="ws-title-main">工作区</span>
          <span class="ws-title-sub" title={workspaceName(props.workspaceId)}>
            {workspaceName(props.workspaceId) || '当前会话'}
          </span>
        </div>
        <div class="ws-actions">
          <button
            class="icon-btn"
            classList={{ active: workspaceShowIgnored() }}
            title={workspaceShowIgnored() ? '隐藏 .git、node_modules 等目录' : '显示被忽略的目录'}
            aria-pressed={workspaceShowIgnored()}
            onClick={() => setWorkspaceShowIgnored(!workspaceShowIgnored())}
          >
            <Icon name="shield" size={16} />
          </button>
          <button class="icon-btn" title="刷新" onClick={() => setReloadToken((value) => value + 1)}>
            <Icon name="refresh" size={16} />
          </button>
          <button class="icon-btn" title="关闭工作区" onClick={closeWorkspacePanel}>
            <Icon name="close" size={16} />
          </button>
        </div>
      </div>

      <div class="ws-filter">
        <Icon name="search" size={14} />
        <input
          value={filter()}
          placeholder="筛选文件"
          aria-label="筛选文件"
          onInput={(event) => setFilter(event.currentTarget.value)}
        />
        <Show when={filter()}>
          <button class="ws-filter-clear" title="清空筛选" onClick={() => setFilter('')}>
            <Icon name="close" size={12} />
          </button>
        </Show>
      </div>

      <div class="ws-body">
        <div class="ws-tree" role="group" aria-label="工作区文件">
          <Show
            when={props.workspaceId}
            fallback={<div class="ws-tree-note">当前会话未绑定工作区。</div>}
          >
            <TreeLevel parent="" depth={0} />
          </Show>
        </div>

        <div class="ws-preview">
          <Show when={selected()} fallback={<div class="ws-empty">选择左侧文件查看内容</div>}>
            {(path) => (
              <>
                <div class="ws-preview-head">
                  <div class="ws-preview-titles">
                    <span class="ws-preview-name" title={path()}>{filePreviewName(path())}</span>
                    {/* 根目录文件的名字与路径相同，再显示一行会让标题区重复 */}
                    <Show when={path() !== filePreviewName(path())}>
                      <span class="ws-preview-path" title={path()}>{path()}</span>
                    </Show>
                  </div>
                  <a
                    class="ws-preview-link"
                    href={fileReadUrl({ path: path(), workspaceId: props.workspaceId ?? undefined })}
                    target="_blank"
                    rel="noopener noreferrer"
                  >
                    新标签页
                  </a>
                </div>

                <div class="ws-preview-body">
                  <Show when={contentLoading()}>
                    <div class="ws-preview-state">正在读取…</div>
                  </Show>

                  <Show when={contentError()}>
                    {(message) => <div class="ws-preview-state is-error">{message()}</div>}
                  </Show>

                  <Show when={content()?.kind === 'markdown'}>
                    <div class="file-preview-md">
                      <Markdown text={previewText()} workspaceId={props.workspaceId ?? undefined} />
                    </div>
                  </Show>

                  <Show when={content()?.kind === 'text'}>
                    <pre class="ws-preview-text">{previewText()}</pre>
                  </Show>

                  <Show when={content()?.kind === 'image'}>
                    <img
                      class="ws-preview-image"
                      src={previewUrl()}
                      alt={filePreviewName(path())}
                    />
                  </Show>

                  <Show when={content()?.kind === 'video'}>
                    <video
                      class="ws-preview-media"
                      src={previewUrl()}
                      controls
                      onError={() =>
                        setContentError('浏览器无法播放这个视频（容器或编码不受支持）；请点右上角「新标签页」在本地播放。')
                      }
                    />
                  </Show>

                  <Show when={content()?.kind === 'audio'}>
                    <audio
                      class="ws-preview-audio"
                      src={previewUrl()}
                      controls
                      onError={() => setContentError('浏览器无法播放这个音频；请点右上角「新标签页」在本地播放。')}
                    />
                  </Show>

                  <Show when={content()?.kind === 'html'}>
                    <iframe
                      class="ws-preview-html"
                      sandbox=""
                      src={previewUrl()}
                      title={filePreviewName(path())}
                    />
                  </Show>
                </div>
              </>
            )}
          </Show>
        </div>
      </div>
    </aside>
  );
}