import { createEffect, createMemo, createSignal, For, Index, Match, on, onCleanup, onMount, Show, Switch } from 'solid-js';

import { ComposerEditor, type ComposerEditorHandle, type MentionToken } from '../components/ComposerEditor';
import { ConfirmActionDialog, Modal, TextPromptDialog } from '../components/Dialog';
import { Icon } from '../components/Icons';
import { Markdown } from '../components/Markdown';
import { PopMenu, menuAt } from '../components/PopMenu';
import { ProjectSidebar } from '../components/ProjectSidebar';
import { SideResizer } from '../components/SideResizer';
import { ScreenshotOverlay } from '../components/screenshot/ScreenshotOverlay';
import { isCaptureSupported } from '../components/screenshot/capture';
import { post } from '../lib/api';
import {
  formatBytes,
  formatCompactDuration,
  formatDuration,
  formatMessageTime,
  formatTokenCompact,
  isImageMediaType,
  truncate,
} from '../lib/format';
import { openSession, openSettings, openSessions, openWiki } from '../lib/router';
import { settingsApi } from '../lib/settings';
import { sidebarCollapsed, useNarrowViewport } from '../lib/sidebar';
import type { AvailableSkill, StoredAttachment, WebCommand } from '../lib/types';
import { workspaceColor, workspaceInitial } from '../lib/workspace';
import type { ChatItem, PendingQuestionRequest, ToolCard, UserQuestionAnswer } from '../stores/chat';
import { dropChat, getChat } from '../stores/chats';
import { openFilePreview } from '../stores/filePreview';
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

/**
 * 会破坏数据、执行前必须二次确认的命令动作。
 * `/clear` 经 messageRepo 物理删除当前会话全部消息（含软删标记），不可恢复。
 */
const DANGEROUS_COMMAND_ACTIONS = new Set(['action:clearMessages']);

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
  /** `@` token 的 DOM 定位（不透明句柄），选中技能后据此原地替换成胶囊。 */
  const [mentionToken, setMentionToken] = createSignal<MentionToken | null>(null);
  const [helpOpen, setHelpOpen] = createSignal(false);
  const [dialog, setDialog] = createSignal<'rename' | 'delete' | null>(null);
  /** 等待二次确认的危险命令；非空即弹确认框，确认后才真正执行。 */
  const [pendingCommand, setPendingCommand] = createSignal<WebCommand | null>(null);
  const [screenshotOpen, setScreenshotOpen] = createSignal(false);
  /** 改写请求在途：期间锁住输入与发送，避免改写到一半又被编辑 */
  const [optimizing, setOptimizing] = createSignal(false);
  /** 输入区待发送缩略图的页内放大：值为本地 blob 地址与文件名。 */
  const [thumbZoom, setThumbZoom] = createSignal<{ url: string; name: string } | null>(null);

  let scrollEl!: HTMLDivElement;
  let threadEl!: HTMLDivElement;
  let composerApi: ComposerEditorHandle | undefined;
  let fileInputEl!: HTMLInputElement;

  /** 距底部容差：滚轮惯性与小数像素下，这个距离内仍按「贴底」处理。 */
  const FOLLOW_THRESHOLD = 32;
  /**
   * 跟随状态：滚动位置进入底部容差即上锁，用户上滚离开底部容差才解锁。
   * 用普通变量而非 signal——它只被事件与渲染读取，进响应式图会让滚动反过来触发 effect。
   */
  let pinned = true;
  /** 跟随写入的 rAF 句柄：同一帧内多次 items 变更只写一次 scrollTop。 */
  let followRaf = 0;
  /** 上一帧跟随的投递时刻：rAF 不执行时用来判定「这帧已经等太久」。 */
  let followAt = 0;
  /** 上一次 scroll 的 scrollTop：用来识别滚动方向（滚动条拖动、键盘没有 wheel 事件）。 */
  let lastScrollTop = 0;
  /** 滚动停止后的校验定时器（`scrollend` 的跨平台等价做法）。 */
  let scrollSettleTimer = 0;
  /**
   * 用户上滚手势的截止时刻：手势进行中（哪怕还在容差带内）先不跟随。
   * 否则触控板每帧十几像素的上滚会被逐帧拉回底部，位移永远攒不到容差之外。
   */
  let followHoldUntil = 0;

  /** 用户手势保持跟随暂停的时长（毫秒）；滚动停止校验的 160ms 之内必然到期。 */
  const FOLLOW_HOLD_MS = 200;

  const [canScroll, setCanScroll] = createSignal(false);
  const [atBottom, setAtBottom] = createSignal(true);
  const [atTop, setAtTop] = createSignal(true);
  const [hasNew, setHasNew] = createSignal(false);

  const session = createMemo(() => sessions().find((item) => item.sessionId === props.sessionId) ?? null);
  const title = () => session()?.title || '未命名会话';
  const totalTokens = () => session()?.totalTokens ?? 0;

  /** 本会话生效的生成参数；尚未拉取时为 null，按「跟随模型」处理。 */
  const generation = createMemo(() => sessionGeneration(props.sessionId));

  /**
   * 圆环分母：模型上下文窗口优先；窗口未知时取上下文预算档位的最大值兜底
   * （档位本身也由窗口派生，两者都没有时不画圆环，只保留数字）。
   */
  const tokenLimit = createMemo(() => {
    const windowSize = generation()?.contextWindow;
    if (windowSize && windowSize > 0) return windowSize;
    const tiers = generation()?.contextTiers ?? bootstrap()?.contextTiers ?? [];
    return tiers.length > 0 ? Math.max(...tiers) : 0;
  });
  /**
   * 当前窗口内的已用量：会话累计 Token 对窗口取余（跨过一个整窗口即回卷）。
   * 累计不足一个窗口时余数就是累计值，按实际用量展示；
   * 余数为 0 且累计不为 0 说明刚好用满整数个窗口，按满窗口显示，避免误判成「没用」。
   * 后端只给累计值，拿不到「最近一次请求的输入 Token」，以整窗口回卷近似当前上下文占用。
   */
  const tokenUsedInWindow = createMemo(() => {
    const limit = tokenLimit();
    const total = totalTokens();
    if (limit <= 0) return total;
    const rest = total % limit;
    return rest === 0 && total > 0 ? limit : rest;
  });
  /** 圆环比例 = 当前窗口内已用 ÷ 窗口；窗口未知时为 0（不画环，只留数字）。 */
  const tokenRatio = createMemo(() => {
    const limit = tokenLimit();
    return limit > 0 ? Math.min(1, Math.max(0, tokenUsedInWindow() / limit)) : 0;
  });
  /** 悬停提示：窗口内用量 / 窗口与百分比，并附会话累计值；窗口来自预算档位时标注为估算。 */
  const tokenUsageTitle = () => {
    const total = totalTokens().toLocaleString('zh-CN');
    const limit = tokenLimit();
    if (limit <= 0) return `当前会话累计已消费 ${total} token`;
    const percent = (tokenRatio() * 100).toFixed(1);
    const fromWindow = (generation()?.contextWindow ?? 0) > 0;
    const used = tokenUsedInWindow().toLocaleString('zh-CN');
    const window = limit.toLocaleString('zh-CN');
    return `当前窗口已用 ${used} / ${window} token（${percent}%${fromWindow ? '' : '，窗口按上下文预算档位估算'}）；会话累计 ${total} token`;
  };

  /** 当前思考档位（枚举名）；空串即跟随模型。 */
  const currentEffort = () => generation()?.reasoningEffort ?? '';
  /** 思考档位选项：会话模型档案优先，未拉到时退回首屏数据。 */
  const effortOptions = () => generation()?.reasoningEfforts ?? bootstrap()?.reasoningEfforts ?? [];

  /** 上下文预算档位选项：会话模型优先，未拉到时退回首屏数据；空表示不显示控件。 */
  const tierOptions = () => generation()?.contextTiers ?? bootstrap()?.contextTiers ?? [];
  /** 菜单里展示的模型窗口：会话参数已拉到时以它为准（null 即未知），未拉到时退回首屏数据；0 表示未知。 */
  const tierWindow = () => {
    const current = generation();
    return current ? (current.contextWindow ?? 0) : (bootstrap()?.contextWindow ?? 0);
  };
  /**
   * 选中的档位：服务端 `contextBudget` 是用户挑的原始档位；
   * 它缺失时按后端默认（末档，即窗口本身）勾选——`contextTier` 是留白后的触发点，不能直接比对。
   */
  const currentTier = () => {
    const selected = generation()?.contextBudget;
    if (selected != null) return selected;
    const tiers = tierOptions();
    return tiers.length > 0 ? Math.max(...tiers) : null;
  };

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
   * `@` 技能候选匹配：id / 名称 / 描述 / 分类任一包含输入即命中，
   * id 或名称以输入开头的排前面（同档保持原有顺序）。
   * 与桌面端 / CLI 的 `SkillMentionMatcher` 同口径，改这里须三端同步。
   */
  function matchSkills(skills: AvailableSkill[], token: string): AvailableSkill[] {
    const query = token.trim().toLowerCase();
    const ranked: Array<{ skill: AvailableSkill; rank: number }> = [];
    for (const skill of skills) {
      const id = skill.id.toLowerCase();
      const name = skill.name.toLowerCase();
      const hit =
        id.includes(query) ||
        name.includes(query) ||
        (skill.description ?? '').toLowerCase().includes(query) ||
        (skill.category ?? '').toLowerCase().includes(query);
      if (!hit) continue;
      ranked.push({ skill, rank: id.startsWith(query) || name.startsWith(query) ? 0 : 1 });
    }
    // 同档保持入参顺序（现代引擎的 sort 为稳定排序）
    ranked.sort((left, right) => left.rank - right.rank);
    return ranked.slice(0, 10).map((entry) => entry.skill);
  }

  /**
   * `@` 技能候选：只在单行、且光标前是「行首或空白 + @token」时命中。
   * 含路径分隔符的 token（`@D:\a.cs`、`@./src`）不当作技能，留给附件路径引用。
   */
  const matchedSkills = createMemo<AvailableSkill[]>(() => {
    if (!mentionOpen()) return [];
    return matchSkills(mentionSkills(), mentionQuery());
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
    lastScrollTop = element.scrollTop;
    const onScroll = () => {
      const top = element.scrollTop;
      const distance = element.scrollHeight - top - element.clientHeight;
      // 内容增长与跟随写入只会让 scrollTop 变大；scroll 事件又是异步派发的，迟到时读到的
      // 已经是写入后的位置与增长后的高度。所以只有「位置确实变小且已离开底部容差」
      // 才说明用户拖/点了滚动条或滚了滚轮——这里正是流式追加期间曾经误判停跟的地方。
      const movedUp = top < lastScrollTop - 1;
      lastScrollTop = top;
      // 位置变小只可能来自用户操作（拖/点滚动条、滚轮、键盘）或内容在上方收缩；
      // 两种情况都先暂停跟随，让位移落地，否则容差带内会被逐帧拉回底部
      if (movedUp) followHoldUntil = performance.now() + FOLLOW_HOLD_MS;

      if (distance <= FOLLOW_THRESHOLD) {
        // 触底（含容差）即锁住：之后内容继续追加也保持跟随
        pinned = true;
        setHasNew(false);
      } else if (movedUp) {
        // 位置真正离开底部：解锁跟随，由用户自己控制阅读位置
        pinned = false;
      }
      syncScrollState();
    };
    const onWheel = (event: WheelEvent) => {
      // 用户正在操作滚动：先撤销尚未落地的跟随写入，否则同一帧内会被拉回底部，
      // 上滚的位移也就读不到了
      if (followRaf) {
        cancelAnimationFrame(followRaf);
        followRaf = 0;
      }
      if (event.deltaY > 0 && isNearBottom()) {
        pinned = true;
        setHasNew(false);
        return;
      }
      // 上滚不在这里解锁：等这次滚动的 scroll 事件读到真实位移，
      // 只有滚动条真正离开底部容差才停跟，一像素抖动不会打断跟随；
      // 但手势期间必须暂停跟随，否则小步上滚会被逐帧拉回底部
      if (event.deltaY < 0) followHoldUntil = performance.now() + FOLLOW_HOLD_MS;
    };
    // 滚动停止后再校验一次：覆盖平滑跳转落点、滚动条拖动、惯性滚动等没有 wheel 事件的场景
    const onScrollSettled = () => {
      window.clearTimeout(scrollSettleTimer);
      scrollSettleTimer = window.setTimeout(() => {
        // 手势已停止：解除暂停，容差内的停手位置就是底部，补一次贴底
        followHoldUntil = 0;
        relockIfNearBottom();
        if (pinned) follow();
        else syncScrollState();
      }, 160);
    };
    // 视口变化会改变可视高度，按钮状态需要重算
    const onResize = () => {
      relockIfNearBottom();
      if (pinned) follow();
      else syncScrollState();
    };

    element.addEventListener('scroll', onScroll, { passive: true });
    element.addEventListener('scroll', onScrollSettled, { passive: true });
    element.addEventListener('wheel', onWheel, { passive: true });
    window.addEventListener('resize', onResize);

    // 后台标签页不执行 rAF 回调：回到前台时补一次贴底，避免停在中途
    const onVisibilityChange = () => {
      if (document.visibilityState !== 'visible') return;
      relockIfNearBottom();
      if (pinned) stickToBottom();
      else syncScrollState();
    };
    document.addEventListener('visibilitychange', onVisibilityChange);

    // 内容高度变化（流式追加、`<details>` 展开、图片加载）：贴底时继续跟随，否则只刷新按钮状态
    let observer: ResizeObserver | null = null;
    if (typeof ResizeObserver !== 'undefined' && threadEl) {
      observer = new ResizeObserver(() => {
        // 高度突变可能凭空拉开距离但没有 scroll 事件：仍在容差内就自己纠回来
        relockIfNearBottom();
        if (pinned) follow();
        else syncScrollState();
      });
      observer.observe(threadEl);
    }

    queueMicrotask(syncScrollState);

    onCleanup(() => {
      element.removeEventListener('scroll', onScroll);
      element.removeEventListener('scroll', onScrollSettled);
      element.removeEventListener('wheel', onWheel);
      window.removeEventListener('resize', onResize);
      document.removeEventListener('visibilitychange', onVisibilityChange);
      observer?.disconnect();
      window.clearTimeout(scrollSettleTimer);
      if (followRaf) cancelAnimationFrame(followRaf);
    });
  });

  // 流式期间每条帧都会替换 items 数组：上锁时跟随到最新，解锁时只提示有新内容
  createEffect(() => {
    chat.items();
    if (!scrollEl) return;
    if (!pinned) {
      // 掉锁不一定伴随 scroll 事件（滚动锚定重选、节点替换）：仍在容差内就恢复跟随
      relockIfNearBottom();
      if (!pinned) {
        setHasNew(true);
        return;
      }
    }
    follow();
  });

  // 会话切换：重置跟随状态；历史就位后由 onMount / 上面这个 effect 兜底贴底
  createEffect(() => {
    void props.sessionId;
    pinned = true;
    followHoldUntil = 0;
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

  /**
   * 消费点回锁：掉锁不总是伴随 scroll 事件（滚动锚定重选、节点替换、高度突变），
   * 只要位置又落回容差内，就恢复跟随。
   */
  function relockIfNearBottom(): void {
    if (pinned || !scrollEl) return;
    if (!isNearBottom()) return;
    pinned = true;
    setHasNew(false);
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
    if (!pinned || !scrollEl) return;
    // 用户上滚手势进行中：先让位移落地，容差带内的小步上滚不能逐帧拉回
    if (performance.now() < followHoldUntil) return;
    // 后台标签页里 rAF 不执行：直接写滚动位置，否则内容一直涨而视图停在中途
    if (document.visibilityState === 'hidden') {
      scrollEl.scrollTop = scrollEl.scrollHeight;
      return;
    }
    if (followRaf) {
      // 窗口被遮挡/最小化但 visibilityState 仍是 visible 时，rAF 可能长时间不回调；
      // 一直等下去会让 followRaf 永不清零、之后所有跟随都被这里挡掉（永久停更）。
      // 超过 200ms 还没落地就直接同步补一次，并把这一帧作废。
      if (performance.now() - followAt < 200) return;
      cancelAnimationFrame(followRaf);
      followRaf = 0;
      scrollEl.scrollTop = scrollEl.scrollHeight;
      syncScrollState();
      return;
    }
    followAt = performance.now();
    followRaf = requestAnimationFrame(() => {
      followRaf = 0;
      if (!pinned || !scrollEl) return;
      // 同一帧里用户刚开始上滚：这一帧让给用户
      if (performance.now() < followHoldUntil) return;
      scrollEl.scrollTop = scrollEl.scrollHeight;
      syncScrollState();
    });
  }

  /** 强制贴底：发送消息、历史加载完成、会话切换时使用。 */
  function stickToBottom(): void {
    pinned = true;
    followHoldUntil = 0;
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
    setMentionToken(null);
    setMentionIndex(0);
  }

  /** 根据光标位置重算 `@` token；不满足条件即关闭候选。 */
  function updateMention(): void {
    const token = composerApi?.tokenAtCaret() ?? null;
    if (!token) {
      closeMention();
      return;
    }

    setMentionToken(token);
    setMentionQuery(token.query);
    setMentionIndex(0);
    setMentionOpen(true);
    void ensureSkills();
  }

  /** 把选中的技能按 token 位置原地替换成胶囊（序列化仍是 `@id `）。 */
  function applyMention(skill: AvailableSkill): void {
    const token = mentionToken();
    closeMention();
    if (!token) return;
    composerApi?.applyToken(token, { id: skill.id, label: skill.name || skill.id });
  }

  async function switchSessionModel(providerId: string, modelId: string): Promise<void> {
    const before = currentEffort();
    await selectSessionModel(props.sessionId, providerId, modelId);
    const after = currentEffort();
    // 换模型可能把原有档位判为不支持；服务端已回落为「跟随模型」，这里明确告知而不是静默
    if (before && before !== after) {
      // 落库值对新模型无效：顺手清掉思考强度，档位保持不变（窗口会由后端按新模型收敛）
      await saveSessionGeneration(props.sessionId, '', currentTier());
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
    // 附件没传完就发不出去：和发送按钮同一判定，先拦住，不能清空输入或贴底
    if (chat.pending().some((item) => item.status === 'uploading')) {
      showToast('附件还在上传，请稍候', 'info');
      return;
    }

    composerApi?.clear();
    closeMention();
    // 自己发的消息必须立刻可见：无论直接发出还是排队，都重新上锁并贴底
    stickToBottom();
    await chat.send(text, permissionModeKey(), currentEffort());
  }

  /**
   * 命令入口：会破坏数据的动作先弹确认，其余直接执行。
   * 命令菜单点击与手输 `/clear` 回车都走这里，拦一处即可全覆盖。
   */
  function runCommand(command: WebCommand) {
    if (DANGEROUS_COMMAND_ACTIONS.has(command.action)) {
      // 先不清输入框：取消后用户还能看见自己敲的命令
      setPendingCommand(command);
      return;
    }
    executeCommand(command);
  }

  /** 真正执行命令；命令文本只在这里丢弃。 */
  function executeCommand(command: WebCommand) {
    composerApi?.clear();
    closeMention();

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

  /** 危险命令的确认文案：条数与「是否正在生成」都会影响后果，必须写清。 */
  function commandWarning(command: WebCommand): string {
    if (command.action === 'action:clearMessages') {
      const count = session()?.messageCount ?? 0;
      const head =
        count > 0
          ? `当前会话的 ${count} 条消息将被永久删除，无法恢复。`
          : '当前会话的消息将被永久删除，无法恢复。';
      return chat.streaming() ? `${head}正在生成的这一轮会先被中断。` : head;
    }
    return '该命令执行后无法撤销，确定继续？';
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
    setModelMenu(menuAt(rect, { width: 300, height: 420, gap: 6, align: 'start' }));

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
        // 改写结果用纯文本整段回填：能解析到的 `@id` 会重新变成胶囊
        composerApi?.setText(result.text);
        closeMention();
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

  const narrow = useNarrowViewport();
  // 窄屏左栏变横排 chip（见 styles.css 的 760px 断点），那时栏宽由 CSS 接管，拖拽条要隐掉
  const draggable = () => !narrow() && !sidebarCollapsed();

  /**
   * 项目栏点项目：跳到该项目最近更新过的会话。
   *
   * 只刷新列表、不切换后端「当前工作区」——切了会让之后新建的会话落到别处；
   * 该项目一个会话都没有时留在此页，让用户自己决定新建。
   */
  const openProjectLatestSession = async (workspaceId: string) => {
    if (workspaceId === session()?.workspaceId) return;
    await refreshSessions();
    const latest = sessions()
      .filter((item) => item.workspaceId === workspaceId)
      .sort((a, b) => (b.updatedAt ?? b.createdAt).localeCompare(a.updatedAt ?? a.createdAt))[0];
    if (latest) openSession(latest.sessionId);
    else showToast('该项目还没有会话', 'info');
  };

  return (
    <>
      <div class="chat-body">
        <ProjectSidebar selectedWorkspaceId={session()?.workspaceId ?? null} onSelect={openProjectLatestSession} />
        <Show when={draggable()}>
          <SideResizer label="调整项目栏宽度" />
        </Show>

        <div class="chat-main">
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
          <span class="token-usage" title={tokenUsageTitle()}>
            <Show when={tokenLimit() > 0}>
              <span class="token-ring" aria-hidden="true">
                <span class="token-ring-value" style={{ '--p': tokenRatio() }} />
              </span>
            </Show>
            <span class="token-usage-text">{formatTokenCompact(tokenUsedInWindow())}</span>
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

      <div class="chat-scroll-wrap">
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
              <Index each={chat.items()}>{(item) => <ChatRow
                item={item}
                workspaceId={session()?.workspaceId ?? undefined}
                onRetry={(id) => chat.retryQueued(id)}
                onCancel={(id) => chat.removeQueued(id)}
              />}</Index>

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
                <div class="typing" role="status" aria-label="生成中" />
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
                        <img
                          class="thumb"
                          src={item.previewUrl}
                          alt={item.fileName}
                          title="点击查看大图"
                          onClick={() => setThumbZoom({ url: item.previewUrl!, name: item.fileName })}
                        />
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

            <ComposerEditor
              api={(handle) => (composerApi = handle)}
              placeholder="给 Luban Agent 发送消息（Enter 发送，Shift+Enter 换行）"
              disabled={optimizing()}
              resolveSkill={(id) => {
                const skill = mentionSkills().find((item) => item.id === id);
                return skill ? { id: skill.id, label: skill.name || skill.id } : null;
              }}
              onInput={(text) => {
                setInput(text);
                setCommandIndex(0);
              }}
              onCaret={() => updateMention()}
              onFocus={() => setFocused(true)}
              onBlur={() => {
                setFocused(false);
                closeMention();
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
                    composerApi?.clear();
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
                composerApi?.setText('/', false);
                closeMention();
                composerApi?.focus();
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
                    // 档位按整体替换提交：带上当前档位，避免切换思考强度时把它清掉
                    void saveSessionGeneration(props.sessionId, option, currentTier());
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
          <PopMenu
            style={{
              left: `${menu().x}px`,
              top: `${menu().y}px`,
              width: '300px',
              'max-height': '420px',
              display: 'flex',
              'flex-direction': 'column',
              overflow: 'hidden',
            }}
            onClose={() => setModelMenu(null)}
          >
            <div class="menu-scroll">
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
            </div>
            <Show when={tierOptions().length > 0}>
              <div class="menu-ctx">
                <div class="menu-ctx-head">
                  上下文预算
                  <Show when={tierWindow() > 0}>
                    <span class="win">
                      窗口 <b>{formatTokenCompact(tierWindow())}</b>
                    </span>
                  </Show>
                </div>
                <For each={tierOptions()}>
                  {(tier) => {
                    const isDefault = tier === Math.max(...tierOptions());
                    return (
                      <button
                        class="menu-item"
                        classList={{ active: currentTier() === tier }}
                        title="档位越小越早触发压缩：省 token，但更早丢弃历史"
                        onClick={() => void saveSessionGeneration(props.sessionId, currentEffort(), tier)}
                      >
                        <span class="tier">{formatTokenCompact(tier)}</span>
                        <span class="hint-role">{isDefault ? '默认' : '提前压缩'}</span>
                        <div class="spacer" />
                        <Show when={currentTier() === tier}>
                          <Icon name="check" size={15} />
                        </Show>
                      </button>
                    );
                  }}
                </For>
              </div>
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

      <Show when={chat.pendingQuestion()}>
        {(request) => (
          <QuestionDialog
            request={request()}
            submitting={chat.questionSubmitting()}
            onSubmit={(answers) => void chat.resolveQuestion(answers)}
            onCancel={() => void chat.cancelQuestion()}
            onTimeout={() => chat.questionTimeout()}
          />
        )}
      </Show>

      <Show when={helpOpen()}>
        <HelpDialog onClose={() => setHelpOpen(false)} />
      </Show>

      <Show when={thumbZoom()}>
        {(target) => (
          <Modal title={target().name} onClose={() => setThumbZoom(null)} wide>
            <img class="zoomed-image" src={target().url} alt={target().name} />
          </Modal>
        )}
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

      <Show when={pendingCommand()}>
        {(command) => (
          <ConfirmActionDialog
            title={`执行 ${command().name}？`}
            message={commandWarning(command())}
            confirmText={command().action === 'action:clearMessages' ? '清空消息' : '继续执行'}
            onClose={() => setPendingCommand(null)}
            onConfirm={() => {
              const target = command();
              setPendingCommand(null);
              executeCommand(target);
            }}
          />
        )}
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

/** 助手气泡的渲染组：思考 / 正文各自一块，相邻工具调用合成一组。 */
interface AssistantGroup {
  kind: 'thinking' | 'text' | 'tools';
  text: string;
  tools: ToolCard[];
  /** 思考段的耗时（毫秒）；进行中或历史缺数据时为空。 */
  durationMs?: number;
}

function ChatRow(props: {
  item: () => ChatItem;
  workspaceId?: string;
  /** 重试一条发送失败的消息。 */
  onRetry: (id: string) => void;
  /** 取消一条排队中 / 发送失败的消息。 */
  onCancel: (id: string) => void;
}) {
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

  /** 相邻连续的工具调用合成一组，其余段落各自成块：保持发生顺序。 */
  const assistantGroups = createMemo<AssistantGroup[]>(() => {
    const groups: AssistantGroup[] = [];
    for (const block of assistant()?.blocks ?? []) {
      if (block.kind === 'tool') {
        const last = groups[groups.length - 1];
        if (last && last.kind === 'tools') last.tools.push(block.tool);
        else groups.push({ kind: 'tools', text: '', tools: [block.tool] });
        continue;
      }
      groups.push({ kind: block.kind, text: block.text, tools: [], durationMs: block.kind === 'thinking' ? block.durationMs : undefined });
    }
    return groups;
  });

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
                  {(attachment) => (
                    <AttachmentChip attachment={attachment()} workspaceId={props.workspaceId} />
                  )}
                </Index>
              </div>
            </Show>
            <div class="bubble">{item().text}</div>
            <Show when={item().state}>
              {(state) => (
                <div class={`msg-state ${state()}`}>
                  <Switch fallback={<span>排队中 · 本轮结束后自动发送</span>}>
                    <Match when={state() === 'sending'}>
                      <span>发送中…</span>
                    </Match>
                    <Match when={state() === 'failed'}>
                      <span>未发出</span>
                    </Match>
                  </Switch>
                  <Show when={state() === 'failed'}>
                    <button class="msg-state-btn" onClick={() => props.onRetry(item().id)}>
                      重试
                    </button>
                  </Show>
                  <Show when={state() !== 'sending'}>
                    <button class="msg-state-btn" onClick={() => props.onCancel(item().id)}>
                      取消
                    </button>
                  </Show>
                </div>
              )}
            </Show>
            <div class="msg-meta" style={{ 'justify-content': 'flex-end' }}>
              {formatMessageTime(item().time)}
            </div>
          </div>
        )}
      </Show>

      <Show when={assistant()}>
        {(item) => (
          <div class="msg msg-assistant">
            <Index each={assistantGroups()}>
              {(group, index) => (
                <Switch>
                  <Match when={group().kind === 'thinking'}>
                    <ThinkingBlockView
                      text={group().text}
                      durationMs={group().durationMs}
                      live={item().streaming && index === assistantGroups().length - 1}
                    />
                  </Match>

                  <Match when={group().kind === 'text'}>
                    <div class="assistant-text">
                      <Markdown text={group().text} workspaceId={props.workspaceId} />
                    </div>
                  </Match>

                  <Match when={group().kind === 'tools'}>
                    <ToolGroupView tools={() => group().tools} />
                  </Match>
                </Switch>
              )}
            </Index>

            <div class="msg-meta">
              <Show when={item().history}>
                <span class="status history">历史记录</span>
              </Show>
              <span>{formatMessageTime(item().time)}</span>
              <Show when={formatDuration(item().durationMs)}>
                <span class="metric" title="本轮回复耗时">{formatDuration(item().durationMs)}</span>
              </Show>
              <Show when={item().tokens}>
                <span class="metric" title="本轮 token 用量">{item().tokens!.toLocaleString('zh-CN')} tok</span>
              </Show>
            </div>
          </div>
        )}
      </Show>
    </>
  );
}

function AttachmentChip(props: { attachment: StoredAttachment; workspaceId?: string }) {
  const name = () => props.attachment.FileName || '附件';
  const size = () => formatBytes(props.attachment.FileSize ?? 0);

  // 只有图片且宿主给出了工作区内相对路径才可点：非图片走 /file/read 会得到「不支持预览」提示页
  const previewPath = () => {
    const value = props.attachment.WebPath?.trim();
    if (!value || !isImageMediaType(props.attachment.MediaType)) return null;
    return value;
  };

  const content = () => (
    <>
      <Show when={isImageMediaType(props.attachment.MediaType)} fallback={<Icon name="file" size={15} />}>
        <Icon name="image" size={15} />
      </Show>
      <span class="name">{name()}</span>
      <span class="faint">{size()}</span>
    </>
  );

  return (
    <Show
      when={previewPath()}
      fallback={
        <div class="attachment-chip" title={props.attachment.SourcePath || name()}>
          {content()}
        </div>
      }
    >
      {(path) => (
        <button
          type="button"
          class="attachment-chip clickable"
          title={props.attachment.SourcePath ? `查看 ${name()}（${props.attachment.SourcePath}）` : `查看 ${name()}`}
          onClick={() => openFilePreview(path(), props.workspaceId)}
        >
          {content()}
        </button>
      )}
    </Show>
  );
}

/**
 * 思考过程折叠块：默认收起，展开/收起走 grid 行高过渡，与工具调用同款。
 */
function ThinkingBlockView(props: { text: string; durationMs?: number; live: boolean }) {
  const [open, setOpen] = createSignal(false);

  return (
    <div class="thinking" classList={{ open: open(), 'is-live': props.live }}>
      <button
        type="button"
        class="fold-head"
        aria-expanded={open()}
        onClick={() => setOpen((value) => !value)}
      >
        <Icon name="lightbulb" size={14} />
        思考过程
        <Show when={formatCompactDuration(props.durationMs)}>
          <span class="summary-time" title="思考耗时">
            {formatCompactDuration(props.durationMs)}
          </span>
        </Show>
      </button>
      <div class="group-collapse">
        <div class="group-collapse-inner">
          <div class="thinking-body">{props.text}</div>
        </div>
      </div>
    </div>
  );
}

/**
 * 工具调用整组折叠：与「思考过程」同款样式，默认收起。
 * 状态只用标题栏右侧的脉动点表示（`is-live`），不额外加文字徽标——与思考过程一致。
 */
function ToolGroupView(props: { tools: () => ToolCard[] }) {
  const [open, setOpen] = createSignal(false);
  const running = createMemo(() => props.tools().some((tool) => tool.status === 'running'));

  /**
   * 整组耗时：各次调用耗时之和；运行中的调用还没有耗时，不计入。
   * 单次耗时是墙钟时间（工具开始到返回），含等待人工确认的时间——确认发生在工具方法内部，
   * 因此需要确认的工具会比实际执行时间明显更长；组耗时与实时帧按 `at` 做差的口径一致。
   */
  const durationMs = createMemo(() => props.tools().reduce((sum, tool) => sum + (tool.durationMs ?? 0), 0));

  return (
    <div class="tool-group" classList={{ open: open(), 'is-live': running() }}>
      <button
        type="button"
        class="fold-head"
        aria-expanded={open()}
        onClick={() => setOpen((value) => !value)}
      >
        <Icon name="terminal" size={14} />
        工具调用
        <span class="faint">{props.tools().length} 个</span>
        <Show when={formatCompactDuration(durationMs())}>
          <span class="summary-time" title="工具调用耗时">
            {formatCompactDuration(durationMs())}
          </span>
        </Show>
      </button>
      <div class="group-collapse">
        <div class="group-collapse-inner">
          <div class="tool-group-body">
            <Index each={props.tools()}>{(tool) => <ToolCardView tool={tool} />}</Index>
          </div>
        </div>
      </div>
    </div>
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
  request: {
    confirmationId: string;
    toolName: string;
    arguments: unknown;
    timeoutSeconds: number;
    /** 确认来源（编排子代理节点）；主代理发起时为空。 */
    origin?: { nodeId?: string | null; role?: string | null; description?: string | null } | null;
  };
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

  // 来源标注：编排子代理发起时显示节点/角色/描述，主代理发起时为空（不占行）
  const originText = createMemo(() => {
    const origin = props.request.origin;
    if (!origin) return null;
    const node = origin.nodeId?.trim();
    const role = origin.role?.trim() ?? '';
    const desc = origin.description?.trim() ?? '';
    // 三字段全空与框架一致：视为无来源，不占行
    if (!node && !role && !desc) return null;
    const roleText = role ? `（角色 ${role}）` : '';
    const descText = desc ? `：${desc}` : '';
    return `来源：编排节点 ${node || '未命名'}${roleText}${descText}`;
  });

  return (
    <div class="overlay">
      <div class="dialog" role="dialog" aria-modal="true">
        <div class="dialog-head">
          <div>
            <div class="dialog-title">允许执行 {props.request.toolName}？</div>
            <div class="dialog-sub">工具调用需要你确认</div>
            <Show when={originText()}>
              {(text) => <div class="dialog-sub">{text()}</div>}
            </Show>
          </div>
        </div>
        <div class="dialog-body">
          <div class="tool-label">参数</div>
          <pre class="tool-pre">{argsText()}</pre>
        </div>
        <div class="dialog-foot">
          {/* 只在最后一分钟提示：长时间等待时秒数只会占位刷屏，临期才需要提醒 */}
          <span class="countdown">
            <Show when={remaining() <= 60}>{remaining()} 秒后按拒绝处理</Show>
          </span>
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

function QuestionDialog(props: {
  request: PendingQuestionRequest;
  /** 答案提交中：保持挂载，按钮禁用，避免失败后已填内容与倒计时被重置。 */
  submitting: boolean;
  onSubmit: (answers: UserQuestionAnswer[]) => void;
  onCancel: () => void;
  onTimeout: () => void;
}) {
  const questions = props.request.questions;
  const total = questions.length;
  // 题号导航用的静态下标：一次提问的题目固定，不随作答变化
  const spots = questions.map((_, index) => index);
  // 分步向导：一次只呈现一题，各题答案按题号留存，来回切题不丢已填内容
  const [step, setStep] = createSignal(0);
  // 以服务端/首帧给出的截止时刻为准：失败放回后继续按原截止时间倒数，不从满值重来
  const [remaining, setRemaining] = createSignal(
    Math.max(1, Math.round((props.request.expiresAt - Date.now()) / 1000)),
  );
  const [selected, setSelected] = createSignal<string[][]>(questions.map(() => []));
  const [other, setOther] = createSignal<string[]>(questions.map(() => ''));
  // 「其它」自由输入默认折叠成一行加号按钮，点过才展开，不占常显行高
  const [otherOpen, setOtherOpen] = createSignal<boolean[]>(questions.map(() => false));

  let bodyEl!: HTMLDivElement;

  onMount(() => {
    const timer = window.setInterval(() => {
      const next = remaining() - 1;
      if (next <= 0) {
        // 提交在途：结果以服务端返回为准，定时器继续留在 1 秒，失败后仍能按截止时间收尾
        if (props.submitting) {
          setRemaining(1);
          return;
        }
        window.clearInterval(timer);
        props.onTimeout();
      } else {
        setRemaining(next);
      }
    }, 1000);
    onCleanup(() => window.clearInterval(timer));
  });

  // 来源标注：编排子代理发起时显示节点/角色/描述，主代理发起时为空（不占行）
  const originText = createMemo(() => {
    const origin = props.request.origin;
    if (!origin) return null;
    const node = origin.nodeId?.trim();
    const role = origin.role?.trim() ?? '';
    const desc = origin.description?.trim() ?? '';
    if (!node && !role && !desc) return null;
    const roleText = role ? `（角色 ${role}）` : '';
    const descText = desc ? `：${desc}` : '';
    return `来源：编排节点 ${node || '未命名'}${roleText}${descText}`;
  });

  const current = createMemo(() => questions[step()]);
  const isLast = createMemo(() => step() === total - 1);
  const headerText = createMemo(() => current().header?.trim() ?? '');
  const isMulti = createMemo(() => current().multiSelect === true);

  /** 单选替换该项、多选切换该项。 */
  function choose(index: number, label: string): void {
    const multi = questions[index].multiSelect === true;
    const picked = selected()[index];
    // 重复点选已选中的单选项目前没有变化，直接返回，免得点一下就被推到下一题
    if (!multi && picked.length === 1 && picked[0] === label) return;
    setSelected((list) =>
      list.map((labels, i) => {
        if (i !== index) return labels;
        if (multi) {
          return labels.includes(label) ? labels.filter((item) => item !== label) : [...labels, label];
        }
        return [label];
      }),
    );
    // 单选视为答完本题：自动进下一题；多选可能要勾几项，最后一题要留在这里复核，都交给按钮
    if (!multi && index === step() && !isLast()) setStep(index + 1);
  }

  function changeOther(index: number, value: string): void {
    setOther((list) => list.map((text, i) => (i === index ? value : text)));
  }

  function openOther(index: number): void {
    setOtherOpen((list) => list.map((open, i) => (i === index ? true : open)));
  }

  /** 收起「其它」同时清空：折叠态不能留一段还在参与提交校验的隐藏文本。 */
  function clearOther(index: number): void {
    setOther((list) => list.map((text, i) => (i === index ? '' : text)));
    setOtherOpen((list) => list.map((open, i) => (i === index ? false : open)));
  }

  /** 本题是否已作答：选项与自由输入任一即可。 */
  function answered(index: number): boolean {
    return selected()[index].length > 0 || other()[index].trim().length > 0;
  }

  /** 未作答的题号（0 基）：既用来禁用提交，也用来告诉用户到底缺哪题。 */
  const missing = createMemo(() => spots.filter((index) => !answered(index)));

  const missingText = createMemo(() => {
    const list = missing();
    if (list.length === 0) return '';
    if (total === 1) return '本题未作答';
    return `第 ${list.map((index) => index + 1).join('、')} 题未作答`;
  });

  /** 每题至少有一个选项或一段自由输入；自由输入与选项可并存，自由输入走独立字段回传。 */
  const answers = createMemo<UserQuestionAnswer[] | null>(() => {
    if (missing().length > 0) return null;
    const picked = selected();
    const free = other();
    return questions.map((question, i) => ({
      question: question.question,
      labels: [...picked[i]],
      freeText: free[i].trim() || null,
    }));
  });

  function goPrev(): void {
    if (step() > 0) setStep(step() - 1);
  }

  function goNext(): void {
    if (!isLast() && answered(step())) setStep(step() + 1);
  }

  function submit(): void {
    const result = answers();
    if (result && !props.submitting) props.onSubmit(result);
  }

  // 换题后把正文滚回顶部：上一题滚到一半的位置会盖住新题的选项
  createEffect(on(step, () => {
    if (bodyEl) bodyEl.scrollTop = 0;
  }));

  return (
    <div class="overlay">
      <div class="dialog" role="dialog" aria-modal="true">
        <div class="dialog-head">
          <div>
            <div class="dialog-title">{total > 1 ? `Agent 有 ${total} 个问题` : 'Agent 想请你确认'}</div>
            <div class="dialog-sub">回答后 Agent 会继续执行</div>
            <Show when={originText()}>{(text) => <div class="dialog-sub">{text()}</div>}</Show>
          </div>
        </div>
        <div class="dialog-body q-body" ref={(element) => (bodyEl = element)}>
          <Show when={total > 1 || isMulti()}>
            <div class="q-steps">
              <Show when={total > 1}>
                <For each={spots}>
                  {(index) => (
                    <button
                      type="button"
                      class={`q-dot${answered(index) ? ' done' : ''}${step() === index ? ' now' : ''}`}
                      aria-label={`第 ${index + 1} 题`}
                      title={`第 ${index + 1} 题${answered(index) ? '（已作答）' : '（未作答）'}`}
                      onClick={() => setStep(index)}
                    />
                  )}
                </For>
                <span class="q-counter">第 {step() + 1} / {total} 题</span>
              </Show>
              <Show when={isMulti()}>
                <span class="q-flag">可多选</span>
              </Show>
            </div>
          </Show>
          <div class="q-main">
            <Show when={headerText()}>
              <div class="q-tag">{headerText()}</div>
            </Show>
            <div class="q-text">{current().question}</div>
          </div>
          <div class={isMulti() ? 'q-opts is-multi' : 'q-opts'} role={isMulti() ? 'group' : 'radiogroup'}>
            <For each={current().options}>
              {(option) => {
                const active = () => selected()[step()].includes(option.label);
                return (
                  <button
                    type="button"
                    class={active() ? 'q-opt on' : 'q-opt'}
                    role={isMulti() ? 'checkbox' : 'radio'}
                    aria-checked={active()}
                    onClick={() => choose(step(), option.label)}
                  >
                    <span class="q-mark" />
                    <span class="q-opt-body">
                      <span class="q-opt-label">{option.label}</span>
                      <Show when={option.description}>
                        <span class="q-opt-desc">{option.description}</span>
                      </Show>
                    </span>
                  </button>
                );
              }}
            </For>
          </div>
          <Show
            when={otherOpen()[step()]}
            fallback={
              <button type="button" class="q-other-toggle" onClick={() => openOther(step())}>
                <Icon name="plus" size={13} /> 其它答案（自定义）
              </button>
            }
          >
            <div class="q-other">
              <input
                class="text-input"
                placeholder="输入自定义答案"
                value={other()[step()]}
                onInput={(event) => changeOther(step(), event.currentTarget.value)}
                onKeyDown={(event) => {
                  if (event.key !== 'Enter') return;
                  event.preventDefault();
                  if (isLast()) submit();
                  else goNext();
                }}
              />
              <button type="button" class="btn" onClick={() => clearOther(step())} title="清除自定义答案并收起">
                清除
              </button>
            </div>
          </Show>
        </div>
        <div class="dialog-foot">
          {/* 只在最后一分钟提示：长时间等待时秒数只会占位刷屏，临期才需要提醒 */}
          <span class="countdown">
            <Show when={remaining() <= 60}>{remaining()} 秒后自动跳过</Show>
          </span>
          <Show when={missing().length > 0}>
            <button type="button" class="btn q-jump" disabled={props.submitting} onClick={() => setStep(missing()[0])}>
              {missingText()}
            </button>
          </Show>
          <button class="btn ghost" disabled={props.submitting} onClick={() => props.onCancel()}>
            跳过作答
          </button>
          <Show when={step() > 0}>
            <button class="btn" onClick={goPrev}>
              上一步
            </button>
          </Show>
          <Show
            when={!isLast()}
            fallback={
              <button class="btn primary" disabled={props.submitting || missing().length > 0} onClick={submit}>
                {props.submitting ? '提交中…' : '提交'}
              </button>
            }
          >
            <button class="btn primary" disabled={!answered(step())} onClick={goNext}>
              下一题
            </button>
          </Show>
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