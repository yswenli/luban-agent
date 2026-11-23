import { createEffect, createMemo, createSignal, For, Index, onCleanup, onMount, Show } from 'solid-js';

import { ConfirmActionDialog, TextPromptDialog } from '../components/Dialog';
import { Icon } from '../components/Icons';
import { Markdown } from '../components/Markdown';
import { PopMenu, menuAt } from '../components/PopMenu';
import { ScreenshotOverlay } from '../components/screenshot/ScreenshotOverlay';
import { isCaptureSupported } from '../components/screenshot/capture';
import { post } from '../lib/api';
import { formatBytes, formatTime, isImageMediaType, truncate } from '../lib/format';
import { openSettings, openSessions, openWiki } from '../lib/router';
import { settingsApi } from '../lib/settings';
import type { AvailableSkill, StoredAttachment, WebCommand } from '../lib/types';
import { workspaceColor, workspaceInitial } from '../lib/workspace';
import type { ChatItem, ToolCard } from '../stores/chat';
import { dropChat, getChat } from '../stores/chats';
import {
  bootstrap,
  commands,
  cyclePermissionMode,
  deleteSession,
  loadCommands,
  permissionModeKey,
  permissionModeLabel,
  loadSessionGeneration,
  refreshProviderModels,
  refreshSessions,
  renameSession,
  saveSessionGeneration,
  selectSessionModel,
  sessionGeneration,
  sessionModel,
  sessionModelLabel,
  sessions,
  showToast,
  wikiWorkspaceIds,
  workspaceName,
} from '../stores/app';

/** 思考档位显示名；未知枚举名原样显示，避免新档位被吞掉。 */
function effortLabel(value: string): string {
  switch (value) {
    case '':
      return '跟随模型';
    case 'None':
      return '不思考';
    case 'Low':
      return '低';
    case 'Medium':
      return '中';
    case 'High':
      return '高';
    case 'ExtraHigh':
      return '极高';
    default:
      return value;
  }
}

export function ChatPage(props: { sessionId: string }) {
  const chat = getChat(props.sessionId);

  const [input, setInput] = createSignal('');
  const [plusMenu, setPlusMenu] = createSignal<{ x: number; y: number } | null>(null);
  const [effortMenu, setEffortMenu] = createSignal<{ x: number; y: number } | null>(null);
  const [modelMenu, setModelMenu] = createSignal<{ x: number; y: number } | null>(null);
  const [modelsLoading, setModelsLoading] = createSignal(false);
  const [moreMenu, setMoreMenu] = createSignal<{ x: number; y: number } | null>(null);
  const [dragging, setDragging] = createSignal(false);
  const [focused, setFocused] = createSignal(false);
  const [commandIndex, setCommandIndex] = createSignal(0);
  /** `@` 技能候选：候选集（当前会话可用技能）与是否正在展示。 */
  const [mentionSkills, setMentionSkills] = createSignal<AvailableSkill[]>([]);
  const [mentionOpen, setMentionOpen] = createSignal(false);
  const [mentionQuery, setMentionQuery] = createSignal('');
  const [mentionIndex, setMentionIndex] = createSignal(0);
  /** `@` token 在输入框中的区间 `[start, end)`，用于选中后原地替换。 */
  const [mentionRange, setMentionRange] = createSignal<{ start: number; end: number } | null>(null);
  const [helpOpen, setHelpOpen] = createSignal(false);
  const [dialog, setDialog] = createSignal<'rename' | 'delete' | null>(null);
  const [screenshotOpen, setScreenshotOpen] = createSignal(false);
  /** 改写请求在途：期间锁住输入与发送，避免改写到一半又被编辑 */
  const [optimizing, setOptimizing] = createSignal(false);

  let scrollEl!: HTMLDivElement;
  let threadEl!: HTMLDivElement;
  let textareaEl!: HTMLTextAreaElement;
  let fileInputEl!: HTMLInputElement;

  /** 距底部容差：滚轮惯性与小数像素下，这个距离内仍按「贴底」处理。 */
  const FOLLOW_THRESHOLD = 32;
  /**
   * 跟随状态：用户主动上滚即解锁，回到底部或主动发送时重新上锁。
   * 用普通变量而非 signal——它只被事件与渲染读取，进响应式图会让滚动反过来触发 effect。
   */
  let pinned = true;
  /** 用户主动上滚的锁存：即使还在容差带内也不重新上锁，避免触控板小步上滚被反复拉回。 */
  let userHeld = false;
  /** 跟随写入的 rAF 句柄：同一帧内多次 items 变更只写一次 scrollTop。 */
  let followRaf = 0;

  const [canScroll, setCanScroll] = createSignal(false);
  const [atBottom, setAtBottom] = createSignal(true);
  const [atTop, setAtTop] = createSignal(true);
  const [hasNew, setHasNew] = createSignal(false);

  const session = createMemo(() => sessions().find((item) => item.sessionId === props.sessionId) ?? null);
  const title = () => session()?.title || '未命名会话';
  const totalTokens = () => session()?.totalTokens ?? 0;

  /** 本会话生效的生成参数；尚未拉取时为 null，按「跟随模型」处理。 */
  const generation = createMemo(() => sessionGeneration(props.sessionId));
  /** 当前思考档位（枚举名）；空串即跟随模型。 */
  const currentEffort = () => generation()?.reasoningEffort ?? '';
  /** 思考档位选项：会话模型档案优先，未拉到时退回首屏数据。 */
  const effortOptions = () => generation()?.reasoningEfforts ?? bootstrap()?.reasoningEfforts ?? [];

  const commandQuery = createMemo(() => {
    const value = input();
    if (!value.startsWith('/') || /\s/.test(value)) return null;
    return value.slice(1).toLowerCase();
  });

  const matchedCommands = createMemo<WebCommand[]>(() => {
    const query = commandQuery();
    if (query === null) return [];
    return commands().filter((command) => {
      if (command.name.slice(1).toLowerCase().startsWith(query)) return true;
      return command.aliases.some((alias) => alias.slice(1).toLowerCase().startsWith(query));
    });
  });

  /**
   * `@` 技能候选：只在单行、且光标前是「行首或空白 + @token」时命中。
   * 含路径分隔符的 token（`@D:\a.cs`、`@./src`）不当作技能，留给附件路径引用。
   */
  const matchedSkills = createMemo<AvailableSkill[]>(() => {
    if (!mentionOpen()) return [];
    const query = mentionQuery().toLowerCase();
    return mentionSkills()
      .filter(
        (skill) =>
          skill.id.toLowerCase().startsWith(query) || skill.name.toLowerCase().startsWith(query),
      )
      .slice(0, 10);
  });

  let skillsLoading = false;
  let skillsLoaded = false;
  let skillsFailed = false;
  /** 技能请求代次：会话切换时自增，用于丢弃上一个会话的迟到响应。 */
  let skillsEpoch = 0;

  // 会话切换：可用技能按会话绑定的工作区解析，需重新拉取（否则会展示上一个工作区的技能）
  createEffect(() => {
    void props.sessionId;
    skillsEpoch++;
    skillsLoading = false;
    skillsLoaded = false;
    skillsFailed = false;
    setMentionSkills([]);
    closeMention();
  });

  onMount(() => {
    void loadCommands();
    void (async () => {
      // 整表覆盖必须先在快照重建之前完成
      await chat.loadHistory();
      await chat.resume();
      // 历史就位后一次性贴底（瞬时，不做动画）
      stickToBottom();
    })();
    if (sessions().length === 0) void refreshSessions();
    // 不在这里 abort：会话实例常驻，切换页面时流式回合继续在后台跑

    const element = scrollEl;
    const onScroll = () => {
      const distance = element.scrollHeight - element.scrollTop - element.clientHeight;
      // 真正到底（含跳转落点）才重新上锁；容差带内被 userHeld 锁住时不回锁
      if (distance <= 1) {
        userHeld = false;
        pinned = true;
        setHasNew(false);
      } else if (distance > FOLLOW_THRESHOLD) {
        pinned = false;
      }
      syncScrollState();
    };
    const onWheel = (event: WheelEvent) => {
      // 上滚第一时间解锁：等 scroll 事件可能已被下一帧的跟随写回
      if (event.deltaY < 0) {
        userHeld = true;
        pinned = false;
        return;
      }
      if (event.deltaY > 0 && isNearBottom()) {
        userHeld = false;
        pinned = true;
        setHasNew(false);
      }
    };
    // 视口变化会改变可视高度，按钮状态需要重算
    const onResize = () => {
      if (pinned) follow();
      else syncScrollState();
    };

    element.addEventListener('scroll', onScroll, { passive: true });
    element.addEventListener('wheel', onWheel, { passive: true });
    window.addEventListener('resize', onResize);

    // `<details>` 展开、图片加载等会改变内容高度：贴底时继续跟随，否则只刷新按钮状态
    let observer: ResizeObserver | null = null;
    if (typeof ResizeObserver !== 'undefined' && threadEl) {
      observer = new ResizeObserver(() => {
        if (pinned) follow();
        else syncScrollState();
      });
      observer.observe(threadEl);
    }

    queueMicrotask(syncScrollState);

    onCleanup(() => {
      element.removeEventListener('scroll', onScroll);
      element.removeEventListener('wheel', onWheel);
      window.removeEventListener('resize', onResize);
      observer?.disconnect();
      if (followRaf) cancelAnimationFrame(followRaf);
    });
  });

  // 流式期间每条帧都会替换 items 数组：上锁时跟随到最新，解锁时只提示有新内容
  createEffect(() => {
    chat.items();
    if (!scrollEl) return;
    if (!pinned) {
      setHasNew(true);
      return;
    }
    follow();
  });

  // 会话切换：重置跟随状态；历史就位后由 onMount / 上面这个 effect 兜底贴底
  createEffect(() => {
    void props.sessionId;
    pinned = true;
    userHeld = false;
    setHasNew(false);
  });

  // 会话切换 / 首次进入时拉取该会话生效的档位；读不到只影响控件展示
  createEffect(() => {
    void loadSessionGeneration(props.sessionId);
  });

  createEffect(() => {
    const list = matchedCommands();
    if (commandIndex() >= list.length) setCommandIndex(0);
  });

  createEffect(() => {
    const list = matchedSkills();
    if (mentionIndex() >= list.length) setMentionIndex(0);
  });

  /* ── 滚动跟随与悬浮跳转 ───────────────────────────────────────────── */

  /** 距底部容差内即视为贴底；内容不足一屏时天然算贴底。 */
  function isNearBottom(): boolean {
    if (!scrollEl) return true;
    return scrollEl.scrollHeight - scrollEl.scrollTop - scrollEl.clientHeight <= FOLLOW_THRESHOLD;
  }

  /** 把真实滚动位置回写为界面状态（是否可滚、按钮方向）。 */
  function syncScrollState(): void {
    if (!scrollEl) return;
    const scrollable = scrollEl.scrollHeight > scrollEl.clientHeight + 1;
    setCanScroll(scrollable);
    setAtTop(scrollEl.scrollTop <= 1);
    setAtBottom(!scrollable || isNearBottom());
    if (isNearBottom()) setHasNew(false);
  }

  /** 跟随到最新：仅在上锁状态生效，rAF 合并，回调内复检避免抢用户滚动。 */
  function follow(): void {
    if (!pinned || !scrollEl || followRaf) return;
    followRaf = requestAnimationFrame(() => {
      followRaf = 0;
      if (!pinned || !scrollEl) return;
      scrollEl.scrollTop = scrollEl.scrollHeight;
      syncScrollState();
    });
  }

  /** 强制贴底：发送消息、历史加载完成、会话切换时使用。 */
  function stickToBottom(): void {
    pinned = true;
    userHeld = false;
    setHasNew(false);
    if (!scrollEl) return;
    if (followRaf) {
      cancelAnimationFrame(followRaf);
      followRaf = 0;
    }
    scrollEl.scrollTop = scrollEl.scrollHeight;
    syncScrollState();
  }

  /** 悬浮按钮跳转：单按钮按当前位置决定方向，只有这里用平滑滚动。 */
  function jumpTo(direction: 'top' | 'bottom'): void {
    if (!scrollEl) return;
    setHasNew(false);
    // 目标位置在动画开始时确定；方向/贴底状态由随后的 scroll 事件回写（到精确底部时自动重新上锁）
    scrollEl.scrollTo({
      top: direction === 'bottom' ? scrollEl.scrollHeight : 0,
      behavior: 'smooth',
    });
  }

  function resizeTextarea() {
    if (!textareaEl) return;
    textareaEl.style.height = 'auto';
    textareaEl.style.height = `${Math.min(textareaEl.scrollHeight, 220)}px`;
  }

  /* ── `@` 技能候选 ─────────────────────────────────────────────────── */

  /** 拉取当前会话可用技能（每次会话切换后一次；失败只提示一次，不打断输入）。 */
  async function ensureSkills(): Promise<void> {
    if (skillsLoading || skillsLoaded || skillsFailed) return;
    skillsLoading = true;
    const epoch = skillsEpoch;
    try {
      const items = await settingsApi.listAvailableSkills(props.sessionId);
      // 会话已切换：丢弃上一个会话的迟到响应，避免弹层里混入别的工作区的技能
      if (epoch !== skillsEpoch) return;
      setMentionSkills(items);
      skillsLoaded = true;
    } catch (error) {
      if (epoch !== skillsEpoch) return;
      skillsFailed = true;
      showToast(error instanceof Error ? error.message : '技能列表加载失败', 'error');
    } finally {
      if (epoch === skillsEpoch) skillsLoading = false;
    }
  }

  /** 关闭 `@` 候选（不清空输入，也不影响斜杠菜单）。 */
  function closeMention(): void {
    setMentionOpen(false);
    setMentionQuery('');
    setMentionRange(null);
    setMentionIndex(0);
  }

  /** 根据光标位置重算 `@` token；不满足条件即关闭候选。 */
  function updateMention(): void {
    const element = textareaEl;
    if (!element) {
      closeMention();
      return;
    }

    const value = element.value ?? '';
    const caret = element.selectionStart ?? 0;
    if (value.includes('\n')) {
      closeMention();
      return;
    }

    // 行首或空白后的 @token（除空白/路径特征外的字符都算 token，含中文技能名）
    const match = /(^|\s)@([^\s@\\/:~]*)$/.exec(value.slice(0, caret));
    if (!match) {
      closeMention();
      return;
    }

    const token = match[2];

    setMentionRange({ start: caret - token.length - 1, end: caret });
    setMentionQuery(token);
    setMentionIndex(0);
    setMentionOpen(true);
    void ensureSkills();
  }

  /** 把选中的技能按 token 区间原地替换为 `@id `，并把光标放到末尾。 */
  function applyMention(skill: AvailableSkill): void {
    const range = mentionRange();
    if (!range) {
      closeMention();
      return;
    }

    const value = textareaEl?.value ?? input();
    const insert = `@${skill.id} `;
    const next = value.slice(0, range.start) + insert + value.slice(range.end);
    closeMention();
    setInput(next);

    queueMicrotask(() => {
      if (!textareaEl) return;
      const caret = range.start + insert.length;
      textareaEl.focus();
      textareaEl.setSelectionRange(caret, caret);
      resizeTextarea();
    });
  }

  async function switchSessionModel(providerId: string, modelId: string): Promise<void> {
    const before = currentEffort();
    await selectSessionModel(props.sessionId, providerId, modelId);
    const after = currentEffort();
    // 换模型可能把原有档位判为不支持；服务端已回落为「跟随模型」，这里明确告知而不是静默
    if (before && before !== after) {
      // 落库值对新模型无效：顺手清掉，避免换回老模型时又静默复活
      await saveSessionGeneration(props.sessionId, '', null);
      showToast('当前模型不支持该思考档位，已改为跟随模型', 'info');
    }
  }

  async function submit() {
    if (optimizing()) return;

    // 斜杠命令是本地动作；本轮运行期间不执行，避免清空/切换类命令打断正在写库的轮次
    if (commandQuery() !== null) {
      const list = matchedCommands();
      const command = list[commandIndex()] ?? list[0];
      if (command) {
        if (chat.streaming()) {
          showToast('本轮进行中，命令暂不可用', 'info');
          return;
        }
        runCommand(command);
        return;
      }
    }

    const text = input();
    if (!text.trim()) {
      // 空内容时同一个按钮就是「取消」
      if (chat.streaming()) await chat.stop();
      return;
    }

    setInput('');
    closeMention();
    queueMicrotask(resizeTextarea);
    // 立即发出的消息必须可见：重新上锁并贴底；排队消息不打断当前阅读位置
    if (!chat.streaming()) stickToBottom();
    await chat.send(text, permissionModeKey(), currentEffort());
  }

  function runCommand(command: WebCommand) {
    setInput('');
    closeMention();
    queueMicrotask(resizeTextarea);

    const action = command.action;
    if (action.startsWith('openSettings:')) {
      openSettings(action.slice('openSettings:'.length));
      return;
    }

    switch (action) {
      case 'action:clearMessages':
        void clearMessages();
        return;
      case 'action:cyclePermissionMode':
        cyclePermissionMode();
        showToast(`权限模式：${permissionModeLabel()}`, 'info');
        return;
      case 'action:help':
        setHelpOpen(true);
        return;
      case 'action:showSessionStats':
        showToast(
          `累计 ${totalTokens()} Token · ${session()?.messageCount ?? 0} 条消息`,
          'info',
        );
        return;
      case 'action:openSessionList':
      case 'action:focusWorkspaces':
        openSessions();
        return;
      case 'action:openLlmWiki':
        openWiki();
        return;
      default:
        showToast(`暂不支持命令 ${command.name}`, 'info');
    }
  }

  async function clearMessages() {
    try {
      await post('/session/clearMessages', { sessionId: props.sessionId });
      await chat.loadHistory(true);
      await refreshSessions();
      showToast('已清空消息', 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  }

  async function addFiles(files: File[]) {
    await chat.addFiles(files);
  }

  /** 展开模型下拉：缓存为空时按供应商补齐一次列表（有 API Key 才会真正请求远端）。 */
  function openModelMenu(event: MouseEvent) {
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    setModelMenu(menuAt(rect, { width: 300, height: 320, gap: 6, align: 'start' }));

    const pending = (bootstrap()?.providers ?? []).filter((provider) => provider.models.length === 0);
    if (pending.length === 0) return;

    setModelsLoading(true);
    void Promise.all(pending.map((provider) => refreshProviderModels(provider.id))).finally(() =>
      setModelsLoading(false),
    );
  }

  function onDrop(event: DragEvent) {
    event.preventDefault();
    setDragging(false);
    const files = Array.from(event.dataTransfer?.files ?? []);
    void addFiles(files);
  }

  function onPaste(event: ClipboardEvent) {
    const files = Array.from(event.clipboardData?.files ?? []);
    if (files.length === 0) return;
    event.preventDefault();
    void addFiles(files);
  }

  /**
   * 把输入框里的内容改写成需求表述。
   * 只替换文字，不动已附的文件；结果直接覆盖、不提供撤销。
   */
  async function optimizeInput() {
    const text = input();
    if (!text.trim() || optimizing()) return;
    setOptimizing(true);
    try {
      const result = await post<{ text: string }>('/agent/optimize', {
        prompt: text,
        sessionId: props.sessionId,
      });
      if (result.text) {
        setInput(result.text);
        closeMention();
        queueMicrotask(resizeTextarea);
      }
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setOptimizing(false);
    }
  }

  const plusMenuHeight = 140;
  /** 抓屏依赖安全上下文：内网 http 下浏览器不提供 getDisplayMedia，菜单项直接置灰 */
  const captureSupported = isCaptureSupported();

  /** 后退：知识库会话回知识库页，其余回会话列表。 */
  const goBack = () => {
    const workspaceId = session()?.workspaceId;
    if (workspaceId && wikiWorkspaceIds().has(workspaceId)) openWiki();
    else openSessions();
  };

  return (
    <>
      <div class="chat-head">
        <button class="icon-btn" title="返回列表" onClick={goBack}>
          <Icon name="arrow-left" size={18} />
        </button>
        <span
          class="ws-badge sm"
          style={{ background: workspaceColor(session()?.workspaceId) }}
          title={workspaceName(session()?.workspaceId) || '未知项目'}
        >
          {workspaceInitial(workspaceName(session()?.workspaceId))}
        </span>
        <span class="chat-title" title={`${workspaceName(session()?.workspaceId)} / ${title()}`}>
          <Show when={workspaceName(session()?.workspaceId)}>
            <span class="faint">{workspaceName(session()?.workspaceId)} / </span>
          </Show>
          {title()}
        </span>
        <div class="spacer" />
        <Show when={totalTokens() > 0}>
          <span class="faint mono" title="当前会话 Token 用量">
            当前会话已消费 {totalTokens()} token
          </span>
        </Show>
        <button
          class="icon-btn"
          title="更多"
          onClick={(event) => {
            const rect = event.currentTarget.getBoundingClientRect();
            const position = menuAt(rect, { width: 172, height: 124, align: 'end' });
            setMoreMenu(position);
          }}
        >
          <Icon name="more" size={18} />
        </button>
      </div>

      <div class="chat-body">
        <div class="chat-scroll" ref={(element) => (scrollEl = element)}>
          <div class="thread" ref={(element) => (threadEl = element)}>
            <Show when={!chat.loadingHistory()} fallback={<div class="loading-line">加载消息…</div>}>
              <Show when={chat.historyError()}>
                <div class="notice error">
                  <Icon name="alert" size={16} />
                  <span>{chat.historyError()}</span>
                </div>
              </Show>

              <Show when={chat.items().length === 0 && !chat.historyError()}>
                <div class="empty">
                  <Icon name="spark" size={28} />
                  <h2>开始对话</h2>
                  <p>输入问题，或点左下角「+」加入图片与文件。</p>
                </div>
              </Show>

              {/* 会话没绑工作区时传 undefined（不改写相对链接）：让宿主回落到「当前工作区」会读到另一个项目的同名文件 */}
              <Index each={chat.items()}>{(item) => <ChatRow item={item} workspaceId={session()?.workspaceId ?? undefined} />}</Index>

              <Show when={chat.plans().length > 0}>
                <div class="plan-strip">
                  <span class="section-label">计划</span>
                  <For each={chat.plans()}>
                    {(entry, index) => (
                      <div class="plan-item">
                        <span class="idx">{index() + 1}.</span>
                        <span class="mono">{entry.toolName}</span>
                      </div>
                    )}
                  </For>
                </div>
              </Show>

              <Show when={chat.progress().length > 0}>
                <div class="plan-strip">
                  <span class="section-label">子代理进度</span>
                  <For each={chat.progress()}>{(text) => <div class="plan-item">{text}</div>}</For>
                </div>
              </Show>

              <Show when={chat.streaming()}>
                <div class="typing">
                  <i />
                  <i />
                  <i />
                </div>
              </Show>
            </Show>
          </div>
        </div>

        <Show when={canScroll() && (!atBottom() || !atTop())}>
          <button
            class="chat-fab"
            title={atBottom() ? '回到顶部' : '回到底部'}
            onClick={() => jumpTo(atBottom() ? 'top' : 'bottom')}
          >
            <Icon name={atBottom() ? 'arrow-up' : 'arrow-down'} size={18} />
            <Show when={hasNew() && !atBottom()}>
              <span class="chat-fab-dot" />
            </Show>
          </button>
        </Show>
      </div>

      <div class="composer-wrap">
          <div
            class="composer"
            classList={{ focused: focused(), dragging: dragging() }}
            onDragOver={(event) => {
              event.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={onDrop}
          >
            <Show when={chat.queue().length > 0}>
              <div class="composer-queue">
                <div class="queue-head">
                  <Icon name="message" size={13} />
                  <span>排队中 {chat.queue().length}</span>
                  <span class="queue-hint">本轮结束后自动发送</span>
                </div>
                <For each={chat.queue()}>
                  {(item) => (
                    <div class="queue-item">
                      <span class="queue-text" title={item.text}>
                        {item.text}
                      </span>
                      <Show when={item.attachments.length > 0}>
                        <span class="queue-files" title={`${item.attachments.length} 个附件`}>
                          <Icon name="file" size={12} />
                          {item.attachments.length}
                        </span>
                      </Show>
                      <button class="queue-remove" title="取消排队" onClick={() => chat.removeQueued(item.id)}>
                        <Icon name="close" size={12} />
                      </button>
                    </div>
                  )}
                </For>
              </div>
            </Show>

            <Show when={chat.pending().length > 0}>
              <div class="composer-pending">
                <For each={chat.pending()}>
                  {(item) => (
                    <div class="pending-item" classList={{ uploading: item.status === 'uploading', error: item.status === 'error' }}>
                      <Show
                        when={isImageMediaType(item.mediaType) && item.previewUrl}
                        fallback={
                          <span class="fileicon">
                            <Icon name={isImageMediaType(item.mediaType) ? 'image' : 'file'} size={16} />
                          </span>
                        }
                      >
                        <img class="thumb" src={item.previewUrl} alt={item.fileName} />
                      </Show>
                      <span class="meta">
                        <span class="name" title={item.fileName}>
                          {item.fileName}
                        </span>
                        <span class="size">
                          {item.status === 'uploading' ? '上传中…' : item.status === 'error' ? item.error : formatBytes(item.size)}
                        </span>
                      </span>
                      <button class="remove" title="移除" onClick={() => chat.removePending(item.id)}>
                        <Icon name="close" size={13} />
                      </button>
                    </div>
                  )}
                </For>
              </div>
            </Show>

            <Show when={optimizing()}>
              <div class="composer-busy">
                <span class="busy-dot" />
                处理中…
              </div>
            </Show>

            <textarea
              ref={(element) => (textareaEl = element)}
              value={input()}
              rows={1}
              disabled={optimizing()}
              placeholder="给 Luban Agent 发送消息（Enter 发送，Shift+Enter 换行）"
              onInput={(event) => {
                setInput(event.currentTarget.value);
                setCommandIndex(0);
                resizeTextarea();
                updateMention();
              }}
              onFocus={() => setFocused(true)}
              onBlur={() => {
                setFocused(false);
                closeMention();
              }}
              onClick={() => updateMention()}
              onKeyUp={(event) => {
                // 光标移动可能离开 / 进入 `@token`；上下键由候选弹层消费，这里不碰
                if (event.key === 'ArrowLeft' || event.key === 'ArrowRight' || event.key === 'Home' || event.key === 'End') {
                  updateMention();
                }
              }}
              onPaste={onPaste}
              onKeyDown={(event) => {
                if (event.isComposing) return;

                // `@` 技能候选优先于斜杠菜单：两者不会同时展示（`@` 不满足斜杠的整串前缀条件）
                const skills = matchedSkills();
                if (mentionOpen()) {
                  if (skills.length > 0) {
                    if (event.key === 'ArrowDown') {
                      event.preventDefault();
                      setMentionIndex((index) => (index + 1) % skills.length);
                      return;
                    }
                    if (event.key === 'ArrowUp') {
                      event.preventDefault();
                      setMentionIndex((index) => (index - 1 + skills.length) % skills.length);
                      return;
                    }
                    if (event.key === 'Enter' || event.key === 'Tab') {
                      event.preventDefault();
                      applyMention(skills[mentionIndex()] ?? skills[0]);
                      return;
                    }
                  }
                  if (event.key === 'Escape') {
                    event.preventDefault();
                    closeMention();
                    return;
                  }
                }

                const list = matchedCommands();
                if (list.length > 0) {
                  if (event.key === 'ArrowDown') {
                    event.preventDefault();
                    setCommandIndex((index) => (index + 1) % list.length);
                    return;
                  }
                  if (event.key === 'ArrowUp') {
                    event.preventDefault();
                    setCommandIndex((index) => (index - 1 + list.length) % list.length);
                    return;
                  }
                  if (event.key === 'Escape') {
                    event.preventDefault();
                    setInput('');
                    return;
                  }
                }

                if (event.key === 'Enter' && !event.shiftKey) {
                  event.preventDefault();
                  void submit();
                }
              }}
            />

            <div class="composer-bar">
              <button
                class="pill-btn"
                title="添加图片和文件 / 命令"
                disabled={optimizing()}
                onClick={(event) => {
                  const rect = event.currentTarget.getBoundingClientRect();
                  setPlusMenu(menuAt(rect, { width: 214, height: plusMenuHeight, gap: 6, align: 'start' }));
                }}
              >
                <Icon name="plus" size={17} />
              </button>

              <button
                class="pill-btn value"
                title="切换本会话模型（不影响其他会话）"
                disabled={optimizing()}
                onClick={openModelMenu}
              >
                <Icon name="brain" size={14} />
                <span class="model-name">{sessionModelLabel(props.sessionId)}</span>
                <Icon name="chevron-down" size={13} />
              </button>

              <button
                class="pill-btn value"
                title="切换权限模式"
                disabled={optimizing()}
                onClick={() => {
                  cyclePermissionMode();
                }}
              >
                <Icon name="shield" size={14} />
                {permissionModeLabel()}
              </button>

              <Show when={effortOptions().length > 1}>
                <button
                  class="pill-btn value"
                  title="思考强度（本会话）"
                  disabled={optimizing()}
                  onClick={(event) => {
                    const rect = event.currentTarget.getBoundingClientRect();
                    setEffortMenu(menuAt(rect, { width: 170, height: 190, gap: 6, align: 'start' }));
                  }}
                >
                  <Icon name="lightbulb" size={14} />
                  {effortLabel(currentEffort())}
                </button>
              </Show>

              <div class="spacer" />

              <button
                class="optimize-btn"
                title="把输入内容改写成更清晰的需求表述"
                disabled={!input().trim() || optimizing() || chat.streaming()}
                onClick={() => void optimizeInput()}
              >
                <Icon name="wand" size={16} />
              </button>

              <button
                class="send-btn"
                classList={{ stop: chat.streaming() && !input().trim() }}
                title={chat.streaming() ? (input().trim() ? '加入发送队列' : '停止本轮') : '发送'}
                disabled={
                  optimizing() ||
                  (!input().trim() && !chat.streaming()) ||
                  (!!input().trim() && chat.pending().some((item) => item.status === 'uploading'))
                }
                onClick={() => void submit()}
              >
                <Icon name={chat.streaming() && !input().trim() ? 'stop' : 'arrow-up'} size={17} />
              </button>
            </div>

            <Show when={mentionOpen() && matchedSkills().length > 0}>
              <div class="command-menu">
                <For each={matchedSkills()}>
                  {(skill, index) => (
                    <button
                      class="command-row"
                      classList={{ active: index() === mentionIndex() }}
                      onMouseDown={(event) => event.preventDefault()}
                      onMouseEnter={() => setMentionIndex(index())}
                      onClick={() => applyMention(skill)}
                    >
                      <span class="command-name">@{skill.id}</span>
                      <span class="command-desc">{skill.description || skill.name}</span>
                      <span class="command-tag">{skill.isBuiltin ? '内置' : skill.category}</span>
                    </button>
                  )}
                </For>
              </div>
            </Show>

            <Show when={matchedCommands().length > 0}>
              <div class="command-menu">
                <For each={matchedCommands()}>
                  {(command, index) => (
                    <button
                      class="command-row"
                      classList={{ active: index() === commandIndex() }}
                      onMouseEnter={() => setCommandIndex(index())}
                      onClick={() => runCommand(command)}
                    >
                      <span class="command-name">{command.name}</span>
                      <span class="command-desc">{command.description}</span>
                      <span class="faint mono">{command.usage}</span>
                    </button>
                  )}
                </For>
              </div>
            </Show>
          </div>

          <div class="hint">
            附件最多 {bootstrap()?.attachments.maxCount ?? 5} 个 · 支持拖放与粘贴 · Enter 发送 / Shift+Enter 换行
          </div>
        </div>

      <input
        ref={fileInputEl}
        type="file"
        multiple
        hidden
        onChange={(event) => {
          const files = Array.from(event.currentTarget.files ?? []);
          event.currentTarget.value = '';
          void addFiles(files);
        }}
      />

      <Show when={plusMenu()}>
        {(menu) => (
          <PopMenu style={{ left: `${menu().x}px`, top: `${menu().y}px`, 'min-width': '214px' }} onClose={() => setPlusMenu(null)}>
            <button
              class="menu-item"
              disabled={!captureSupported}
              title={
                captureSupported
                  ? '框选屏幕区域并标注，确认后作为附件加入'
                  : '当前访问地址不是安全上下文，浏览器不允许抓屏；请改用 https 或 localhost 打开'
              }
              onClick={() => {
                setPlusMenu(null);
                setScreenshotOpen(true);
              }}
            >
              <Icon name="camera" size={16} />
              截图
              <div class="spacer" />
              <Show when={!captureSupported}>
                <span class="sub">需 https</span>
              </Show>
            </button>
            <button
              class="menu-item"
              onClick={() => {
                setPlusMenu(null);
                fileInputEl?.click();
              }}
            >
              <Icon name="image" size={16} />
              图片和文件
              <div class="spacer" />
              <span class="sub">Ctrl+U</span>
            </button>
            <button
              class="menu-item"
              onClick={() => {
                setPlusMenu(null);
                setInput('/');
                closeMention();
                queueMicrotask(() => {
                  textareaEl?.focus();
                  resizeTextarea();
                });
              }}
            >
              <Icon name="terminal" size={16} />
              命令
              <div class="spacer" />
              <span class="sub">/</span>
            </button>
          </PopMenu>
        )}
      </Show>

      <Show when={effortMenu()}>
        {(menu) => (
          <PopMenu style={{ left: `${menu().x}px`, top: `${menu().y}px`, 'min-width': '170px' }} onClose={() => setEffortMenu(null)}>
            <For each={effortOptions()}>
              {(option) => (
                <button
                  class="menu-item"
                  classList={{ active: currentEffort() === option }}
                  onClick={() => {
                    setEffortMenu(null);
                    // 上下文档位暂无控件：显式清除覆盖，避免把当前默认值钉成会话覆盖
                    void saveSessionGeneration(props.sessionId, option, null);
                  }}
                >
                  {effortLabel(option)}
                  <div class="spacer" />
                  <Show when={currentEffort() === option}>
                    <Icon name="check" size={15} />
                  </Show>
                </button>
              )}
            </For>
          </PopMenu>
        )}
      </Show>

      <Show when={modelMenu()}>
        {(menu) => (
          <PopMenu style={{ left: `${menu().x}px`, top: `${menu().y}px`, width: '300px', 'max-height': '320px', overflow: 'auto' }} onClose={() => setModelMenu(null)}>
            <For each={bootstrap()?.providers ?? []}>
              {(provider) => (
                <>
                  <div class="menu-section">{provider.displayName}</div>
                  <For each={provider.models}>
                    {(model) => (
                      <button
                        class="menu-item"
                        classList={{
                          active:
                            sessionModel(props.sessionId)?.providerId === provider.id &&
                            sessionModel(props.sessionId)?.modelId === model,
                        }}
                        onClick={() => {
                          setModelMenu(null);
                          void switchSessionModel(provider.id, model);
                        }}
                      >
                        <span class="mono">{model}</span>
                        <div class="spacer" />
                        <Show
                          when={
                            sessionModel(props.sessionId)?.providerId === provider.id &&
                            sessionModel(props.sessionId)?.modelId === model
                          }
                        >
                          <Icon name="check" size={15} />
                        </Show>
                      </button>
                    )}
                  </For>
                </>
              )}
            </For>
            <Show when={(bootstrap()?.providers ?? []).length === 0}>
              <div class="menu-item faint">还没有供应商</div>
            </Show>
            <Show when={modelsLoading()}>
              <div class="menu-item faint">正在拉取模型列表…</div>
            </Show>
          </PopMenu>
        )}
      </Show>

      <Show when={moreMenu()}>
        {(menu) => (
          <PopMenu style={{ left: `${menu().x}px`, top: `${menu().y}px`, 'min-width': '172px' }} onClose={() => setMoreMenu(null)}>
            <button
              class="menu-item"
              onClick={() => {
                setMoreMenu(null);
                setDialog('rename');
              }}
            >
              <Icon name="pencil" size={15} />
              重命名
            </button>
            <button
              class="menu-item"
              onClick={() => {
                setMoreMenu(null);
                void clearMessages();
              }}
            >
              <Icon name="refresh" size={15} />
              清空消息
            </button>
            <button
              class="menu-item"
              onClick={() => {
                setMoreMenu(null);
                setDialog('delete');
              }}
            >
              <Icon name="trash" size={15} />
              删除会话
            </button>
          </PopMenu>
        )}
      </Show>

      <Show when={chat.confirmation()}>
        {(request) => (
          <ToolConfirmDialog
            request={request()}
            onResolve={(result) => void chat.resolveConfirm(result)}
            onTimeout={() => chat.confirmTimeout()}
          />
        )}
      </Show>

      <Show when={helpOpen()}>
        <HelpDialog onClose={() => setHelpOpen(false)} />
      </Show>

      <Show when={dialog() === 'rename'}>
        <TextPromptDialog
          title="重命名会话"
          label="标题"
          initialValue={title()}
          onClose={() => setDialog(null)}
          onConfirm={(value) => {
            setDialog(null);
            void renameSession(props.sessionId, value);
          }}
        />
      </Show>

      <Show when={dialog() === 'delete'}>
        <ConfirmActionDialog
          title="删除会话"
          message={`确定删除「${truncate(title(), 40)}」？该会话的消息将一并删除。`}
          onClose={() => setDialog(null)}
          onConfirm={() => {
            setDialog(null);
            void deleteSession(props.sessionId).then((ok) => {
              if (!ok) return;
              dropChat(props.sessionId);
              openSessions();
            });
          }}
        />
      </Show>

      <ScreenshotOverlay
        open={screenshotOpen()}
        onCapture={(file) => void addFiles([file])}
        onClose={() => setScreenshotOpen(false)}
        onError={(message) => showToast(message, 'error')}
      />
    </>
  );
}

function ChatRow(props: { item: () => ChatItem; workspaceId?: string }) {
  const notice = () => {
    const value = props.item();
    return value.kind === 'notice' ? value : null;
  };
  const user = () => {
    const value = props.item();
    return value.kind === 'user' ? value : null;
  };
  const assistant = () => {
    const value = props.item();
    return value.kind === 'assistant' ? value : null;
  };

  return (
    <>
      <Show when={notice()}>
        {(item) => (
          <div class={`notice ${item().tone}`}>
            <span class="notice-icon">
              <Icon name={item().tone === 'error' || item().tone === 'warn' ? 'alert' : 'help'} size={15} />
            </span>
            <span>{item().text}</span>
          </div>
        )}
      </Show>

      <Show when={user()}>
        {(item) => (
          <div class="msg msg-user">
            <Show when={item().attachments.length > 0}>
              <div class="attachments">
                <Index each={item().attachments}>
                  {(attachment) => <AttachmentChip attachment={attachment()} />}
                </Index>
              </div>
            </Show>
            <div class="bubble">{item().text}</div>
            <div class="msg-meta" style={{ 'justify-content': 'flex-end' }}>
              {formatTime(item().time)}
            </div>
          </div>
        )}
      </Show>

      <Show when={assistant()}>
        {(item) => (
          <div class="msg msg-assistant">
            <Show when={item().thinking.trim().length > 0}>
              <details class="thinking" classList={{ 'is-live': item().streaming && !item().text }}>
                <summary>
                  <Icon name="lightbulb" size={14} />
                  思考过程
                </summary>
                <div class="thinking-body">{item().thinking}</div>
              </details>
            </Show>

            <Show when={item().tools.length > 0}>
              <ToolGroupView tools={() => item().tools} />
            </Show>

            <Show when={item().text}>
              <div class="assistant-text">
                <Markdown text={item().text} workspaceId={props.workspaceId} />
              </div>
            </Show>

            <div class="msg-meta">
              <Show when={item().history}>
                <span class="status history">历史记录</span>
              </Show>
              <span>{formatTime(item().time)}</span>
            </div>
          </div>
        )}
      </Show>
    </>
  );
}

function AttachmentChip(props: { attachment: StoredAttachment }) {
  const name = () => props.attachment.FileName || '附件';
  const size = () => formatBytes(props.attachment.FileSize ?? 0);
  return (
    <div class="attachment-chip" title={props.attachment.SourcePath || name()}>
      <Show when={isImageMediaType(props.attachment.MediaType)} fallback={<Icon name="file" size={15} />}>
        <Icon name="image" size={15} />
      </Show>
      <span class="name">{name()}</span>
      <span class="faint">{size()}</span>
    </div>
  );
}

/**
 * 工具调用整组折叠：与「思考过程」同款样式，默认收起，摘要只留数量与整体状态。
 * 展开后再逐条看工具卡片，每张卡片仍可单独展开参数与结果。
 */
function ToolGroupView(props: { tools: () => ToolCard[] }) {
  const running = createMemo(() => props.tools().some((tool) => tool.status === 'running'));

  const tone = createMemo(() => {
    const tools = props.tools();
    if (tools.some((tool) => tool.status === 'running')) return { className: 'running', label: '执行中' };
    if (tools.some((tool) => tool.status === 'failed')) return { className: 'failed', label: '有失败' };
    if (tools.some((tool) => tool.status === 'denied')) return { className: 'denied', label: '已拒绝' };
    if (tools.some((tool) => tool.status === 'done')) return { className: 'done', label: '完成' };
    return { className: 'history', label: '历史记录' };
  });

  return (
    <details class="tool-group" classList={{ 'is-live': running() }}>
      <summary>
        <Icon name="terminal" size={14} />
        工具调用
        <span class="faint">{props.tools().length} 个</span>
        <span class={`status ${tone().className}`}>{tone().label}</span>
      </summary>
      <div class="tool-group-body">
        <Index each={props.tools()}>{(tool) => <ToolCardView tool={tool} />}</Index>
      </div>
    </details>
  );
}

function ToolCardView(props: { tool: () => ToolCard }) {
  const [open, setOpen] = createSignal(false);

  const label = createMemo(() => {
    switch (props.tool().status) {
      case 'running':
        return '执行中';
      case 'done':
        return '完成';
      case 'failed':
        return '失败';
      case 'denied':
        return '已拒绝';
      default:
        return '历史记录';
    }
  });

  const summary = createMemo(() => {
    const text = props.tool().argumentsText.replace(/\s+/g, ' ').trim();
    return truncate(text, 90);
  });

  return (
    <div class="tool-card">
      <button class="tool-head" onClick={() => setOpen((value) => !value)}>
        <Icon name={open() ? 'chevron-down' : 'chevron-right'} size={14} />
        <span class="tool-name">{props.tool().name}</span>
        <span class="tool-summary">{summary()}</span>
        <span class={`status ${props.tool().status}`}>{label()}</span>
      </button>
      <Show when={open()}>
        <div class="tool-body">
          <div>
            <div class="tool-label">参数</div>
            <pre class="tool-pre">{props.tool().argumentsText || '（无参数）'}</pre>
          </div>
          <Show when={props.tool().resultSummary}>
            <div>
              <div class="tool-label">结果摘要</div>
              <pre class="tool-pre">{props.tool().resultSummary}</pre>
            </div>
          </Show>
          <Show when={props.tool().error}>
            <div>
              <div class="tool-label">错误</div>
              <pre class="tool-pre">{props.tool().error}</pre>
            </div>
          </Show>
          <Show when={props.tool().status === 'history'}>
            <div class="faint">历史回放只保留调用参数，不包含工具执行结果。</div>
          </Show>
        </div>
      </Show>
    </div>
  );
}

function ToolConfirmDialog(props: {
  request: { confirmationId: string; toolName: string; arguments: unknown; timeoutSeconds: number };
  onResolve: (result: 'allow' | 'deny' | 'allowAll' | 'allowSession') => void;
  onTimeout: () => void;
}) {
  const [remaining, setRemaining] = createSignal(Math.max(1, props.request.timeoutSeconds));

  onMount(() => {
    const timer = window.setInterval(() => {
      const next = remaining() - 1;
      if (next <= 0) {
        window.clearInterval(timer);
        props.onTimeout();
      } else {
        setRemaining(next);
      }
    }, 1000);
    onCleanup(() => window.clearInterval(timer));
  });

  const argsText = createMemo(() => {
    const value = props.request.arguments;
    if (value === null || value === undefined) return '（无参数）';
    if (typeof value === 'string') return value;
    try {
      return JSON.stringify(value, null, 2);
    } catch {
      return String(value);
    }
  });

  return (
    <div class="overlay">
      <div class="dialog" role="dialog" aria-modal="true">
        <div class="dialog-head">
          <div>
            <div class="dialog-title">允许执行 {props.request.toolName}？</div>
            <div class="dialog-sub">工具调用需要你确认</div>
          </div>
        </div>
        <div class="dialog-body">
          <div class="tool-label">参数</div>
          <pre class="tool-pre">{argsText()}</pre>
        </div>
        <div class="dialog-foot">
          <span class="countdown">{remaining()} 秒后按拒绝处理</span>
          <button class="btn ghost" onClick={() => props.onResolve('deny')}>
            拒绝
          </button>
          <button class="btn" onClick={() => props.onResolve('allowSession')}>
            本会话全允许
          </button>
          <button class="btn" onClick={() => props.onResolve('allowAll')}>
            本轮全允许
          </button>
          <button class="btn primary" onClick={() => props.onResolve('allow')}>
            允许
          </button>
        </div>
      </div>
    </div>
  );
}

function HelpDialog(props: { onClose: () => void }) {
  return (
    <div
      class="overlay"
      onClick={(event) => {
        if (event.target === event.currentTarget) props.onClose();
      }}
    >
      <div class="dialog" role="dialog" aria-modal="true">
        <div class="dialog-head">
          <div class="dialog-title">浏览器端可用命令</div>
          <div class="spacer" />
          <button class="icon-btn" onClick={props.onClose} title="关闭">
            <Icon name="close" size={17} />
          </button>
        </div>
        <div class="dialog-body">
          <For each={commands()}>
            {(command) => (
              <div class="command-row" style={{ cursor: 'default' }}>
                <span class="command-name">{command.name}</span>
                <span class="command-desc">{command.description}</span>
              </div>
            )}
          </For>
        </div>
        <div class="dialog-foot">
          <button class="btn primary" onClick={props.onClose}>
            知道了
          </button>
        </div>
      </div>
    </div>
  );
}