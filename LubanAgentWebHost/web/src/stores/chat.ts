import { createSignal } from 'solid-js';

import { ApiError, get, post } from '../lib/api';
import { extensionOf, formatBytes } from '../lib/format';
import { prepareImageForUpload } from '../lib/image';
import { DEFAULT_FIRST_FRAME_MS, DEFAULT_IDLE_MS, SseTimeoutError, streamPost } from '../lib/sse';
import type {
  AttachmentPolicy,
  AttachmentUploadResult,
  PendingAttachment,
  SessionMessage,
  StoredAttachment,
  StoredSegment,
  StoredToolCall,
} from '../lib/types';

import { bootstrap, refreshSessions, showToast } from './app';

export type ToolStatus = 'running' | 'done' | 'failed' | 'denied' | 'history';

export interface ToolCard {
  callId: string;
  name: string;
  argumentsText: string;
  status: ToolStatus;
  resultSummary?: string;
  error?: string;
  /** 本次调用耗时（毫秒）；运行中或历史缺数据时为空。 */
  durationMs?: number;
  /** 服务端给出的调用开始时刻（epoch 毫秒）；仅本页实时收到 `tool.started` 时有值。 */
  startedAt?: number;
}

/** 助手气泡里的有序段落：思考、正文、工具调用按发生顺序排列。 */
export type AssistantBlock =
  | {
      kind: 'thinking';
      text: string;
      /** 思考区间耗时（毫秒）；进行中或历史缺数据时为空。 */
      durationMs?: number;
      /** 服务端给出的首/末帧时刻（epoch 毫秒），用于现场计时；历史回放只有 durationMs。 */
      startedAt?: number;
      endedAt?: number;
    }
  | { kind: 'text'; text: string }
  | { kind: 'tool'; tool: ToolCard };

/** 用户气泡的投递状态：缺省表示已发出，排队/发送中/失败时才带上标记。 */
export type UserItemState = 'queued' | 'sending' | 'failed';

export type ChatItem =
  | {
      kind: 'user';
      id: string;
      text: string;
      attachments: StoredAttachment[];
      time: string;
      state?: UserItemState;
    }
  | {
      kind: 'assistant';
      id: string;
      blocks: AssistantBlock[];
      streaming: boolean;
      history: boolean;
      time: string;
      /** 本轮真实 token 用量（后端采集，缺失时为空）。 */
      tokens?: number;
      /** 本轮回复耗时（毫秒，后端采集）。 */
      durationMs?: number;
    }
  | { kind: 'notice'; id: string; text: string; tone: 'info' | 'warn' | 'error' | 'ok' };

export interface ToolConfirmOrigin {
  /** 编排节点标识；主代理为空。 */
  nodeId?: string | null;
  /** 节点角色名。 */
  role?: string | null;
  /** 节点描述。 */
  description?: string | null;
}

export interface ToolConfirmRequest {
  confirmationId: string;
  toolName: string;
  arguments: unknown;
  timeoutSeconds: number;
  /** 确认来源（编排子代理节点）；主代理发起时为空。 */
  origin?: ToolConfirmOrigin | null;
}

/** 单个候选项（对齐框架 UserQuestionOption）。 */
export interface UserQuestionOption {
  label: string;
  description?: string | null;
}

/** 单个问题（对齐框架 UserQuestion）。 */
export interface UserQuestion {
  question: string;
  /** 短标题（≤12 字），用作标签页/角标。 */
  header?: string | null;
  multiSelect?: boolean;
  options: UserQuestionOption[];
}

/** 等待用户作答的问题请求。 */
export interface PendingQuestionRequest {
  questionId: string;
  questions: UserQuestion[];
  timeoutSeconds: number;
  /** 截止时刻（Unix 毫秒）：提交失败放回弹窗时按原截止时间继续倒数，不回满。 */
  expiresAt: number;
  /** 提问来源（编排子代理节点）；主代理发起时为空。 */
  origin?: ToolConfirmOrigin | null;
}

/** 回传给服务端的单题答案。 */
export interface UserQuestionAnswer {
  question: string;
  /** 选中的选项文案，必须属于该题选项。 */
  labels: string[];
  /** 「其它」自由输入；未填写为 null。 */
  freeText?: string | null;
}

export interface PlanEntry {
  toolName: string;
  arguments: unknown;
}

/** 排队中的待发消息：入队时快照文本、附件与当时的会话参数，之后切换模型不影响已排队内容。 */
export interface QueuedMessage {
  id: string;
  text: string;
  attachments: { path: string; fileName: string; mediaType: string; size: number; webPath?: string }[];
  permissionMode: string;
  effort: string;
}

let seed = 0;
const nextId = () => `local-${++seed}`;

/** 把历史消息里的 `ToolCalls`（JSON 字符串）还原成工具卡片。 */
function parseToolCalls(raw: string | null | undefined): ToolCard[] {
  if (!raw) return [];
  try {
    const list = JSON.parse(raw) as StoredToolCall[];
    return (list ?? []).map((item) => ({
      callId: item.callId ?? nextId(),
      name: item.name ?? 'unknown',
      argumentsText: pretty(item.arguments),
      // 落库不含工具结果，历史回放只能标注「历史记录」
      status: 'history' as const,
    }));
  } catch {
    return [];
  }
}

function parseAttachments(raw: string | null | undefined): StoredAttachment[] {
  if (!raw) return [];
  try {
    const list = JSON.parse(raw) as StoredAttachment[];
    return Array.isArray(list) ? list : [];
  } catch {
    return [];
  }
}

function pretty(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'string') {
    const trimmed = value.trim();
    if ((trimmed.startsWith('{') && trimmed.endsWith('}')) || (trimmed.startsWith('[') && trimmed.endsWith(']'))) {
      try {
        return JSON.stringify(JSON.parse(trimmed), null, 2);
      } catch {
        return value;
      }
    }
    return value;
  }
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

/** 追加文本段落：与末段同类型时并入，否则新开一段，保持时间线顺序。 */
function appendTextBlock(blocks: AssistantBlock[], kind: 'thinking' | 'text', delta: string): void {
  if (!delta) return;
  const last = blocks[blocks.length - 1];
  if (last && last.kind !== 'tool' && last.kind === kind) {
    last.text += delta;
    return;
  }
  blocks.push({ kind, text: delta });
}

/** 取帧时间戳（服务端 epoch 毫秒）；旧服务端不带该字段时退回本机时钟。 */
function frameAt(value: unknown): number {
  // 0 / 负数视为「未赋值」：不可由它反推区间，退回本机时钟更安全
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : Date.now();
}

/**
 * 是否存在「只拿到起点、无从落定」的思考段。
 *
 * 重连续传时快照只给 `startedAtMs`（服务端起点），`endedAt` 要等后续帧补齐；
 * 若快照之后就收尾，done 帧不带时刻，本地算不出耗时，只能回读历史取服务端落库值。
 */
function hasUnsettledThinking(blocksList: readonly ChatItem[]): boolean {
  return blocksList.some((item) => {
    if (item.kind !== 'assistant') return false;
    const last = item.blocks[item.blocks.length - 1];
    return (
      last?.kind === 'thinking' &&
      last.durationMs === undefined &&
      last.startedAt !== undefined &&
      last.endedAt === undefined
    );
  });
}

/** 落定末尾思考段的耗时：遇到正文 / 工具调用或本轮收尾时调用。 */
function closeThinking(blocks: AssistantBlock[], endAt?: number): void {
  const last = blocks[blocks.length - 1];
  if (!last || last.kind !== 'thinking' || last.durationMs !== undefined) return;
  // 重连后快照只给起点，末帧时刻由收口它的那一帧补齐
  const endedAt = last.endedAt ?? endAt;
  if (!last.startedAt || !endedAt) return;
  last.durationMs = Math.max(0, endedAt - last.startedAt);
}

/** 把历史 / 快照里落定的思考耗时并入当前段落（同类型相邻段落会被合并）。 */
function addThinkingDuration(blocks: AssistantBlock[], durationMs: number | null | undefined): void {
  if (!durationMs || durationMs <= 0) return;
  const last = blocks[blocks.length - 1];
  if (!last || last.kind !== 'thinking') return;
  last.durationMs = (last.durationMs ?? 0) + durationMs;
}

/** 历史消息里的有序段落（`Segments` JSON）；缺失或损坏时返回 null，由调用方退回旧口径。 */
function parseSegments(raw: string | null | undefined): AssistantBlock[] | null {
  if (!raw) return null;
  try {
    const list = JSON.parse(raw) as StoredSegment[];
    if (!Array.isArray(list) || list.length === 0) return null;
    const blocks: AssistantBlock[] = [];
    for (const segment of list) {
      if (segment.kind === 'tool') {
        blocks.push({
          kind: 'tool',
          tool: {
            callId: segment.callId ?? nextId(),
            name: segment.name ?? 'unknown',
            argumentsText: pretty(segment.arguments),
            // 落库不含工具结果，历史回放只能标注「历史记录」
            status: 'history',
            durationMs: segment.durationMs ?? undefined,
          },
        });
        continue;
      }
      const text = String(segment.text ?? '');
      if (text) {
        appendTextBlock(blocks, segment.kind === 'thinking' ? 'thinking' : 'text', text);
        if (segment.kind === 'thinking') addThinkingDuration(blocks, segment.durationMs);
      }
    }
    return blocks.length > 0 ? blocks : null;
  } catch {
    return null;
  }
}

/** 助手历史气泡的段落：优先用落库顺序，旧数据退回「思考 → 工具 → 正文」。 */
function historyBlocks(message: SessionMessage): AssistantBlock[] {
  const parsed = parseSegments(message.segments);
  if (parsed) return parsed;

  const blocks: AssistantBlock[] = [];
  const thinking = message.thinking ?? '';
  if (thinking.trim()) blocks.push({ kind: 'thinking', text: thinking });
  for (const tool of parseToolCalls(message.toolCalls)) blocks.push({ kind: 'tool', tool });
  if (message.content) blocks.push({ kind: 'text', text: message.content });
  return blocks;
}

/** 快照里的工具卡（`AgentRunToolSnapshot`）。 */
function snapshotTool(raw: Record<string, unknown>): ToolCard {
  return {
    callId: String(raw.callId ?? nextId()),
    name: String(raw.name ?? 'tool'),
    argumentsText: pretty(raw.arguments),
    status: (raw.status as ToolStatus) ?? 'running',
    resultSummary:
      raw.resultSummary === undefined || raw.resultSummary === null ? undefined : String(raw.resultSummary),
    error: raw.error === undefined || raw.error === null ? undefined : String(raw.error),
    durationMs: typeof raw.durationMs === 'number' ? raw.durationMs : undefined,
    startedAt: typeof raw.startedAtMs === 'number' ? raw.startedAtMs : undefined,
  };
}

/** 快照给出的有序段落（`AgentRunSegmentSnapshot[]`）；老服务端没有该字段时返回 null。 */
function snapshotBlocks(raw: unknown): AssistantBlock[] | null {
  if (!Array.isArray(raw) || raw.length === 0) return null;
  const blocks: AssistantBlock[] = [];
  for (const entry of raw as Record<string, unknown>[]) {
    const kind = String(entry.kind ?? '');
    if (kind === 'tool') {
      const tool = entry.tool as Record<string, unknown> | null | undefined;
      if (tool) blocks.push({ kind: 'tool', tool: snapshotTool(tool) });
      continue;
    }
    const text = String(entry.text ?? '');
    if (text) {
      const thinking = kind === 'thinking';
      appendTextBlock(blocks, thinking ? 'thinking' : 'text', text);
      if (thinking) {
        addThinkingDuration(blocks, typeof entry.durationMs === 'number' ? entry.durationMs : null);
        // 尚未落定的思考段：带上服务端起点，续传后与后续帧接上算完整耗时
        const block = blocks[blocks.length - 1];
        if (block?.kind === 'thinking' && block.startedAt === undefined && typeof entry.startedAtMs === 'number') {
          block.startedAt = entry.startedAtMs;
        }
      }
    }
  }
  return blocks.length > 0 ? blocks : null;
}

function toHistoryItems(messages: SessionMessage[]): ChatItem[] {
  return messages.map<ChatItem>((message) => {
    const time = message.createdAt;
    if (message.role === 'user') {
      return {
        kind: 'user',
        id: `msg-${message.id}`,
        text: message.content,
        attachments: parseAttachments(message.attachments),
        time,
      };
    }

    if (message.role === 'assistant') {
      return {
        kind: 'assistant',
        id: `msg-${message.id}`,
        blocks: historyBlocks(message),
        streaming: false,
        history: true,
        time,
        tokens: message.tokens ?? undefined,
        durationMs: message.durationMs ?? undefined,
      };
    }

    if (message.role === 'summary') {
      // 压缩摘要不入对话正文：只留一个压缩标记，摘要内容本身由服务端喂模型
      return {
        kind: 'notice',
        id: `msg-${message.id}`,
        text: '📦 上下文已压缩（历史摘要）',
        tone: 'info',
      };
    }

    return {
      kind: 'notice',
      id: `msg-${message.id}`,
      text: message.content,
      tone: 'info',
    };
  });
}

function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const text = String(reader.result ?? '');
      const index = text.indexOf(',');
      resolve(index >= 0 ? text.slice(index + 1) : text);
    };
    reader.onerror = () => reject(reader.error ?? new Error('读取文件失败'));
    reader.readAsDataURL(file);
  });
}

/** 按扩展名判图片：MIME 可能缺失（部分系统对 .bmp/.tif 不给 type），服务端也是按扩展名判的。 */
function isImageAttachment(policy: AttachmentPolicy | undefined, file: File): boolean {
  const extension = extensionOf(file.name);
  const known = policy?.imageExtensions;
  if (known && known.length > 0) {
    return known.some((item) => item.toLowerCase() === extension);
  }
  return file.type.startsWith('image/');
}

/** 单个会话的对话状态；实例由 `stores/chats.ts` 的会话池长期持有，只有销毁会话时才中止在途流。 */
export function createChat(sessionId: string) {
  const [items, setItems] = createSignal<ChatItem[]>([]);
  const [loadingHistory, setLoadingHistory] = createSignal(true);
  const [historyLoaded, setHistoryLoaded] = createSignal(false);
  const [historyError, setHistoryError] = createSignal<string | null>(null);
  const [streaming, setStreaming] = createSignal(false);
  const [runId, setRunId] = createSignal<string | null>(null);
  const [confirmation, setConfirmation] = createSignal<ToolConfirmRequest | null>(null);
  const [pendingQuestion, setPendingQuestion] = createSignal<PendingQuestionRequest | null>(null);
  /** 提问答案提交中：弹窗保持挂载（已填内容与倒计时不丢），按钮禁用防重复提交。 */
  const [questionSubmitting, setQuestionSubmitting] = createSignal(false);
  const [pending, setPending] = createSignal<PendingAttachment[]>([]);
  const [queue, setQueue] = createSignal<QueuedMessage[]>([]);
  const [plans, setPlans] = createSignal<PlanEntry[]>([]);
  const [progress, setProgress] = createSignal<string[]>([]);

  let controller: AbortController | null = null;
  /** 队列正在逐条发送：防止 finishStream 与本轮收尾重复起跑。 */
  let draining = false;
  /** 断线续流进行中：此时不能起下一条队列消息，否则会把后台仍在跑的轮次挤成 409。 */
  let recovering = false;
  /** 会话实例是否已销毁（关标签）：销毁后不再挂新的订阅。 */
  let disposed = false;
  /** 本轮已实时渲染的提示条数；快照回放时跳过这段前缀，避免同页重连重复显示。 */
  let renderedNotices = 0;
  /** 本轮已收到的最大帧序号（服务端 `id:` 行）；续流时作为 `fromSeq`，缺省表示内存为空需快照。 */
  let lastSeq: number | undefined;
  /** 当前流式气泡的 id：可见性 / 联网恢复时用它接回同一条气泡。 */
  let activeAssistantId: string | null = null;

  /**
   * 拉取历史消息。
   *
   * 默认只在首次进入时执行：会话实例常驻，切回对话页不能把进行中的流式内容覆盖掉；
   * 清空消息等需要重新拉取时传 `force = true`。
   */
  async function loadHistory(force = false): Promise<void> {
    if (!force && (historyLoaded() || streaming())) return;
    setLoadingHistory(true);
    setHistoryError(null);
    try {
      const messages = await get<SessionMessage[]>('/session/messages', { sessionId });
      const history = toHistoryItems(messages ?? []);
      // 还没落到服务端的前端气泡（排队中 / 发送失败）不能被历史覆盖掉，否则消息会凭空消失
      setItems((list) => {
        const unsent = list.filter(
          (item): item is Extract<ChatItem, { kind: 'user' }> =>
            item.kind === 'user' && (item.state === 'queued' || item.state === 'failed'),
        );
        return unsent.length > 0 ? [...history, ...unsent] : history;
      });
      setHistoryLoaded(true);
    } catch (error) {
      setHistoryError((error as Error).message);
    } finally {
      setLoadingHistory(false);
    }
  }

  function updateAssistant(id: string, mutate: (item: Extract<ChatItem, { kind: 'assistant' }>) => void): void {
    setItems((list) =>
      list.map((item) => {
        if (item.kind !== 'assistant' || item.id !== id) return item;
        const copy = {
          ...item,
          blocks: item.blocks.map((block) =>
            block.kind === 'tool' ? { kind: 'tool' as const, tool: { ...block.tool } } : { ...block },
          ),
        };
        mutate(copy);
        return copy;
      }),
    );
  }

  function updateTool(id: string, callId: string, mutate: (tool: ToolCard) => void): void {
    updateAssistant(id, (item) => {
      const block = item.blocks.find(
        (entry): entry is Extract<AssistantBlock, { kind: 'tool' }> =>
          entry.kind === 'tool' && entry.tool.callId === callId,
      );
      if (block) mutate(block.tool);
    });
  }

  /**
   * 追加一条提示；key 非空时按它去重（快照重放用，避免同页多次重连叠加同一条提示）。
   * 返回 true 表示本次真正追加了一条（被去重拦下时为 false）。
   */
  function pushNotice(text: string, tone: 'info' | 'warn' | 'error' | 'ok', key?: string): boolean {
    let appended = false;
    setItems((list) => {
      const id = key ?? nextId();
      if (key !== undefined && list.some((item) => item.kind === 'notice' && item.id === id)) {
        return list;
      }
      appended = true;
      return [...list, { kind: 'notice', id, text, tone }];
    });
    return appended;
  }

  /**
   * 渲染一条提示并按需累计实时条数。
   *
   * 实时帧（无稳定 key）每追加一条就记一笔，供快照回放跳过已渲染前缀；
   * 回放帧带 `${runId}#${序号}`，不计数，否则重连一次计数就翻倍。
   */
  function pushFrameNotice(
    text: string,
    tone: 'info' | 'warn' | 'error' | 'ok',
    noticeKey?: string,
  ): void {
    if (pushNotice(text, tone, noticeKey) && noticeKey === undefined) {
      renderedNotices += 1;
    }
  }

  /** 应用一帧到指定 assistant 气泡；返回 true 表示本轮已结束（done）。 */
  function applyFrame(assistantId: string, event: string, data: unknown, noticeKey?: string): boolean {
    const payload = (data ?? {}) as Record<string, unknown>;
    switch (event) {
      case 'session':
        if (typeof payload.runId === 'string') setRunId(payload.runId);
        return false;

      case 'think.delta': {
        const at = frameAt(payload.at);
        updateAssistant(assistantId, (item) => {
          appendTextBlock(item.blocks, 'thinking', String(payload.delta ?? ''));
          const block = item.blocks[item.blocks.length - 1];
          if (block && block.kind === 'thinking') {
            if (!block.startedAt) block.startedAt = at;
            block.endedAt = at;
          }
        });
        return false;
      }

      case 'text.delta': {
        const at = frameAt(payload.at);
        updateAssistant(assistantId, (item) => {
          // 正文出现即思考区间结束，与后端「连续同类型」口径一致
          closeThinking(item.blocks, at);
          appendTextBlock(item.blocks, 'text', String(payload.delta ?? ''));
        });
        return false;
      }

      case 'tool.started':
        updateAssistant(assistantId, (item) => {
          const startAt = frameAt(payload.at);
          closeThinking(item.blocks, startAt);
          const callId = String(payload.callId ?? nextId());
          // 同一调用重试：先摘掉旧卡片，保证时间线上只有最新一次
          const index = item.blocks.findIndex((block) => block.kind === 'tool' && block.tool.callId === callId);
          if (index >= 0) item.blocks.splice(index, 1);
          item.blocks.push({
            kind: 'tool',
            tool: {
              callId,
              name: String(payload.name ?? 'tool'),
              argumentsText: pretty(payload.arguments),
              status: 'running',
              startedAt: startAt,
            },
          });
        });
        return false;

      case 'tool.completed': {
        const at = frameAt(payload.at);
        updateTool(assistantId, String(payload.callId ?? ''), (tool) => {
          tool.status = 'done';
          tool.resultSummary = String(payload.resultSummary ?? '');
          if (tool.startedAt) tool.durationMs = Math.max(0, at - tool.startedAt);
        });
        return false;
      }

      case 'tool.failed': {
        const at = frameAt(payload.at);
        updateTool(assistantId, String(payload.callId ?? ''), (tool) => {
          tool.status = 'failed';
          tool.error = String(payload.error ?? '工具执行失败');
          if (tool.startedAt) tool.durationMs = Math.max(0, at - tool.startedAt);
        });
        return false;
      }

      case 'tool.denied':
        pushFrameNotice(`已拒绝工具调用：${String(payload.toolName ?? '')}`, 'warn', noticeKey);
        return false;

      case 'tool.confirm':
        setConfirmation({
          confirmationId: String(payload.confirmationId ?? ''),
          toolName: String(payload.toolName ?? '工具调用'),
          arguments: payload.arguments,
          timeoutSeconds: Number(payload.timeoutSeconds ?? 60),
          origin: (payload.origin as ToolConfirmOrigin | null | undefined) ?? null,
        });
        return false;

      case 'tool.question': {
        const raw = Array.isArray(payload.questions) ? (payload.questions as Record<string, unknown>[]) : [];
        const timeoutSeconds = Number(payload.timeoutSeconds ?? 60);
        setQuestionSubmitting(false);
        setPendingQuestion({
          questionId: String(payload.questionId ?? ''),
          questions: raw.map((item) => ({
            question: String(item.question ?? ''),
            header: (item.header as string | null | undefined) ?? null,
            multiSelect: item.multiSelect === true,
            options: Array.isArray(item.options)
              ? (item.options as Record<string, unknown>[]).map((option) => ({
                  label: String(option.label ?? ''),
                  description: (option.description as string | null | undefined) ?? null,
                }))
              : [],
          })),
          timeoutSeconds,
          // 帧里没有 expiresAt（提问时刻即现在），按超时换算出同一口径的截止时间
          expiresAt: Date.now() + timeoutSeconds * 1000,
          origin: (payload.origin as ToolConfirmOrigin | null | undefined) ?? null,
        });
        return false;
      }

      case 'attachment.added':
        // 附件加入成功不在对话里插提示：发送前后的附件列表已经展示文件与大小，逐条重复只会在正文下方刷屏
        return false;

      case 'attachment.skipped':
        pushFrameNotice(`附件已跳过：${String(payload.source ?? '')} — ${String(payload.reason ?? '')}`, 'warn', noticeKey);
        return false;

      case 'plan.item':
        setPlans((list) => [...list, { toolName: String(payload.toolName ?? ''), arguments: payload.arguments }]);
        return false;

      case 'orchestration.progress': {
        const text = String(payload.message ?? payload.text ?? '');
        if (text) setProgress((list) => [...list, text]);
        return false;
      }

      case 'api.retry': {
        // 自动重试是系统自愈过程：用可恢复的提示让用户知道「在重试」而不是卡死。
        // 不写具体等待秒数——提示经心跳节拍送出，到达时延已不可忽略
        const label = String(payload.label ?? payload.category ?? '请求失败');
        const attempt = Math.max(1, Number(payload.attempt ?? 1));
        const maxRetries = Math.max(1, Number(payload.maxAttempts ?? 2) - 1);
        pushFrameNotice(`⟳ ${label}：正在自动重试（第 ${attempt} 次，最多 ${maxRetries} 次）`, 'warn', noticeKey);
        return false;
      }

      case 'context.compacted': {
        // 服务端已给出面向用户的完整文案（压缩中 / 已压缩 + 归档条数与 token 估算）
        const message = String(payload.message ?? '📦 上下文已压缩');
        pushFrameNotice(message, 'info', noticeKey);
        return false;
      }

      case 'session.title':
        // 自动命名完成：重新拉一次会话列表，顶栏标签与首页列表一起更新
        void refreshSessions();
        return false;

      case 'error':
        pushFrameNotice(String(payload.message ?? '对话失败'), 'error', noticeKey);
        return false;

      case 'done':
        return true;

      default:
        return false;
    }
  }

  /** 用 run.snapshot 重建整条 assistant 气泡（仅页面刷新、内存为空时）。 */
  function restoreFromSnapshot(assistantId: string, payload: Record<string, unknown>): void {
    const tools = Array.isArray(payload.tools)
      ? (payload.tools as Record<string, unknown>[]).map(snapshotTool)
      : [];

    // 快照优先给有序段落；老服务端只给 text/thinking/tools，退回固定序
    const legacy: AssistantBlock[] = [];
    const thinking = String(payload.thinking ?? '');
    if (thinking) legacy.push({ kind: 'thinking', text: thinking });
    for (const tool of tools) legacy.push({ kind: 'tool', tool });
    const legacyText = String(payload.text ?? '');
    if (legacyText) legacy.push({ kind: 'text', text: legacyText });

    const bubble: ChatItem = {
      kind: 'assistant',
      id: assistantId,
      blocks: snapshotBlocks(payload.segments) ?? legacy,
      streaming: true,
      history: false,
      time: String(payload.startedAt ?? new Date().toISOString()),
    };

    // 同一页面内重连（上次流断在中途）时，尾部那条未落库的 assistant 气泡就是本轮的半成品：
    // 直接替换它，否则重试一次就多出一份重复正文。
    // 不能只看列表最后一项——工具被拒 / 附件被跳过 / 报错都会往尾部追加 notice，
    // 那些情况下半成品气泡已不在末尾，按末尾判断会变成「旧正文 + 完整正文」两份。
    setItems((list) => {
      let index = -1;
      for (let i = list.length - 1; i >= 0; i -= 1) {
        const item = list[i];
        if (item.kind === 'assistant' && !item.history) {
          index = i;
          break;
        }
      }
      if (index < 0) return [...list, bubble];

      const next = list.slice();
      next[index] = bubble;
      return next;
    });
    setPlans(Array.isArray(payload.plans) ? (payload.plans as PlanEntry[]) : []);
    setProgress(Array.isArray(payload.progress) ? (payload.progress as string[]) : []);

    // 快照携带本轮全部提示（服务端按发生顺序累积），文案由同一套 applyFrame 生成；
    // 按「轮次 + 序号」给稳定 id，同页多次重连不会把同一条提示叠多份。
    // 同页重连时断线前的提示已实时渲染过，跳过这段前缀只补新增部分；
    // 页面刷新后计数为 0，全部回放。
    const notices = (payload.notices as { event?: string; data?: string }[] | undefined) ?? [];
    const alreadyRendered = renderedNotices;
    const noticeKeyPrefix = `${String(payload.runId ?? '')}#`;
    for (const [noticeIndex, notice] of notices.entries()) {
      if (noticeIndex < alreadyRendered) continue;
      if (typeof notice.event !== 'string' || typeof notice.data !== 'string') continue;
      try {
        applyFrame(assistantId, notice.event, JSON.parse(notice.data), `${noticeKeyPrefix}${noticeIndex}`);
      } catch {
        /* 坏载荷忽略 */
      }
    }
    // 计数按快照里的总条数对齐（不回退：快照理论上不会比已渲染的更少）
    renderedNotices = Math.max(alreadyRendered, notices.length);

    const pendingConfirm = payload.pendingConfirmation as Record<string, unknown> | null | undefined;
    if (pendingConfirm) {
      const expiresAt = Date.parse(String(pendingConfirm.expiresAt ?? ''));
      const remaining = Number.isFinite(expiresAt)
        ? Math.max(1, Math.round((expiresAt - Date.now()) / 1000))
        : Number(pendingConfirm.timeoutSeconds ?? 60);
      setConfirmation({
        confirmationId: String(pendingConfirm.confirmationId ?? ''),
        toolName: String(pendingConfirm.toolName ?? '工具调用'),
        arguments: pendingConfirm.arguments,
        timeoutSeconds: remaining,
        origin: (pendingConfirm.origin as ToolConfirmOrigin | null | undefined) ?? null,
      });
    }

    const pendingQuestion = payload.pendingQuestion as Record<string, unknown> | null | undefined;
    if (pendingQuestion) {
      const parsedExpiresAt = Date.parse(String(pendingQuestion.expiresAt ?? ''));
      const timeoutSeconds = Number(pendingQuestion.timeoutSeconds ?? 60);
      const expiresAt = Number.isFinite(parsedExpiresAt)
        ? parsedExpiresAt
        : Date.now() + timeoutSeconds * 1000;
      const remaining = Math.max(1, Math.round((expiresAt - Date.now()) / 1000));
      let questions: UserQuestion[] = [];
      try {
        const parsed = JSON.parse(String(pendingQuestion.questionsJson ?? '[]')) as Record<string, unknown>[];
        questions = (Array.isArray(parsed) ? parsed : []).map((item) => ({
          question: String(item.question ?? ''),
          header: (item.header as string | null | undefined) ?? null,
          multiSelect: item.multiSelect === true,
          options: Array.isArray(item.options)
            ? (item.options as Record<string, unknown>[]).map((option) => ({
                label: String(option.label ?? ''),
                description: (option.description as string | null | undefined) ?? null,
              }))
            : [],
        }));
      } catch {
        /* 坏载荷先不弹窗，实时帧会补上 */
      }
      if (questions.length > 0) {
        setQuestionSubmitting(false);
        setPendingQuestion({
          questionId: String(pendingQuestion.questionId ?? ''),
          questions,
          timeoutSeconds: remaining,
          expiresAt,
          origin: (pendingQuestion.origin as ToolConfirmOrigin | null | undefined) ?? null,
        });
      }
    }

    if (typeof payload.runId === 'string') setRunId(payload.runId);
  }

  /** 结束本轮本地流状态（停止 / 断线 / 正常收尾共用）。 */
  function finishStream(assistantId: string | null, done: boolean, stream?: AbortController): void {
    // 这条流已被新流接替（切前台重连 / 续流重建）：它迟到的收尾不能再动气泡与全局状态，
    // 否则会把新流刚恢复的 streaming 标记、controller 一起清掉
    if (stream !== undefined && controller !== stream) return;

    if (assistantId) {
      updateAssistant(assistantId, (item) => {
        item.streaming = false;
        // 以思考收尾时也要落定耗时
        closeThinking(item.blocks);
      });
    }
    setStreaming(false);
    setRunId(null);
    setConfirmation(null);
    clearQuestion();
    controller = null;
    if (done) {
      // 序号按轮次从 1 开始重编：本轮已收尾，留着旧序号会让下一次 attach 按错误起点切片
      lastSeq = undefined;
      void refreshSessions();
    }
    // 队列里还有消息：本轮收尾（跑完 / 被取消 / 出错）后按入队顺序继续发下一条
    if (!disposed) void drainQueue();
  }

  /** 探活：true = 有活跃轮次，false = 没有，null = 请求失败（网络未恢复，状态未知）。 */
  async function probeActive(): Promise<boolean | null> {
    try {
      const state = await post<{ active?: boolean }>('/agent/active', { sessionId });
      return state?.active === true;
    } catch {
      return null;
    }
  }

  /**
   * 探活并接上服务端正在进行的轮次；本地流还活着时直接返回。
   *
   * 返回值：true = 本轮已有结论（已接上并收到 done、attach 回 204 后已补历史、
   * 或探活显示已无活跃轮次且历史已补齐）；false = 没能判定/接上（探活失败、被中止、
   * 内部 attach 重试耗尽），调用方可据此重试。
   *
   * @param waitForActive 探活显示「没有活跃轮次」时是否再宽限重探（2s × 3）。
   *   首帧超时后服务端可能仍在初始化、轮次尚未登记，此时立刻按「已结束」收尾会让这一轮
   *   消息看起来凭空消失（消息在轮次体内落库，补历史拿不到）；宽限期专门覆盖这段空窗。
   */
  async function resume(assistantId?: string, waitForActive = false): Promise<boolean> {
    // 会话实例已销毁（关标签）：绝不再挂新的订阅，否则留下无人读取的流
    if (disposed) return false;

    // streaming 为真但 controller 已释放，说明上一轮流死在了半路（如 SSE 出错未复位），
    // 这种残留状态不能挡住续流，否则该会话永远接不上。
    // 反之本地流确实在跑：本轮已有订阅者，重试不该再插一手。
    if (streaming() && controller !== null) return true;

    let active = await probeActive();
    if (active === false && waitForActive && !disposed) {
      for (let probe = 0; probe < 3 && active === false; probe += 1) {
        await new Promise((resolve) => setTimeout(resolve, 2000));
        if (disposed) return false;
        active = await probeActive();
      }
    }

    // 探活本身失败（网络未恢复）：状态未知，交给调用方决定是否重试
    if (active === null) return false;

    if (!active) {
      // 轮次已结束（跑完或被停止）：正文已入库，补历史即视为本轮收尾完成
      if (!historyLoaded() || items().length === 0) await loadHistory(true);
      return true;
    }

    const targetId = assistantId ?? nextId();
    activeAssistantId = targetId;
    controller = new AbortController();
    // 本次续流的控制器：期间可能被更新的流接替，回调与收尾都要核对这条流本身
    const streamController = controller;
    setStreaming(true);
    setRunId(null);
    // 断线收尾时本地已把气泡落定为「非流式」；增量续传不会重建气泡，这里单独恢复进行中标记
    updateAssistant(targetId, (item) => {
      item.streaming = true;
    });

    for (let attempt = 0; ; attempt += 1) {
      let done = false;
      // 探活通过、attach 时轮次已跑完（跑完与 attach 之间的竞态）
      let empty = false;
      let started = false;
      await streamPost(
        '/agent/attach',
        {
          sessionId,
          // 有本地序号就按增量续传；没有（页面刷新、内存为空）才让服务端下发快照
          ...(lastSeq !== undefined ? { fromSeq: lastSeq } : {}),
        },
        {
          onFrame: ({ event, data, id }) => {
            if (controller !== streamController) return;
            if (id !== undefined) lastSeq = id;
            if (event === 'run.snapshot') {
              const snapshot = (data ?? {}) as Record<string, unknown>;
              // 快照自带服务端已发布的序号：其后只补更大的帧，不能让 id 行覆盖它
              if (typeof snapshot.seq === 'number') lastSeq = snapshot.seq;
              restoreFromSnapshot(targetId, snapshot);
              started = true;
              return;
            }
            if (!started) {
              // 极端情况下快照帧之前先到实时帧：懒建一个空气泡，后续帧照常累积
              started = true;
              setItems((list) => [
                ...list,
                {
                  kind: 'assistant',
                  id: targetId,
                  blocks: [],
                  streaming: true,
                  history: false,
                  time: new Date().toISOString(),
                },
              ]);
            }
            if (applyFrame(targetId, event, data)) done = true;
          },
          onEmpty: () => {
            empty = true;
          },
          onError: () => {
            /* 由外层循环按 done / empty / aborted / 重试次数判定 */
          },
          onClose: () => {
            /* 同上 */
          },
        },
        streamController.signal,
        { idleMs: DEFAULT_IDLE_MS, firstFrameMs: DEFAULT_FIRST_FRAME_MS },
      );

      // 已被新流接替（切前台重连）：让位，避免两条流写同一条气泡
      if (controller !== streamController) return true;

      if (empty) {
        // 轮次已结束：内容在库里，直接补历史；不能重试（重试只会一直 204）
        await loadHistory(true);
        finishStream(started ? targetId : null, true, streamController);
        return true;
      }
      if (done) {
        // done 帧不带服务端时刻：重连续传留下的「只有起点」的思考段本地无从落定，
        // 此时本轮已入库，回读历史可直接拿到落库耗时（否则要等下次整页刷新才显示）
        if (hasUnsettledThinking(items())) await loadHistory(true);
        finishStream(targetId, true, streamController);
        return true;
      }
      if (streamController.signal.aborted) {
        finishStream(targetId, false, streamController);
        return false;
      }
      if (attempt >= 3) {
        finishStream(targetId, false, streamController);
        showToast('续流失败，请重新打开该会话', 'error');
        return false;
      }
      await new Promise((resolve) => setTimeout(resolve, 1000 * 2 ** attempt));
    }
  }

  /**
   * 流式响应中途出错后的自动续流。
   *
   * 断连只摘订阅者，服务端轮次通常仍在跑：按 1s / 2s / 4s 退避重新 attach，
   * 最多 3 次；任一成功即把后续帧接回同一条 assistant 气泡。
   * 3 次都失败才提示错误并交回原有收尾逻辑（不再重复提示一次）。
   *
   * @param awaitRunRegistration 这次断在「首帧之前」时置真：服务端可能仍在初始化、
   *   轮次尚未登记，探活会误报「没有活跃轮次」，需给 resume 留出宽限期再判定。
   */
  async function recoverAfterDrop(
    assistantId: string,
    error: unknown,
    awaitRunRegistration = false,
  ): Promise<void> {
    // 续流期间禁止队列抢跑：服务端旧轮次还在跑，起新消息只会 409
    recovering = true;
    try {
      // 先释放本地流状态：resume() 会被「streaming 且 controller 非空」直接短路
      finishStream(assistantId, false);

      for (let attempt = 0; attempt < 3; attempt += 1) {
        await new Promise((resolve) => setTimeout(resolve, 1000 * 2 ** attempt));
        // 等待期间关掉了标签：不再续流，也不提示
        if (disposed) return;
        // 探活/attach 任一步得到结论即结束：本轮还在跑就接着收帧，已跑完就补历史
        if (await resume(assistantId, awaitRunRegistration)) return;
      }

      pushNotice((error as Error | undefined)?.message ?? '对话连接失败', 'error');
      finishStream(assistantId, false);
    } finally {
      recovering = false;
      // 续流收尾后（接上或失败）再决定队列去向
      void drainQueue();
    }
  }

  /**
   * 发送一条消息；本轮还在跑时改为入队。
   *
   * 消息在调用的当下就作为用户气泡进入线程（排队中也立即可见、可取消），
   * 快照文本 / 已就绪附件 / 当时的权限模式与思考档位随队列条目保存，
   * 本轮收尾后由 drainQueue 按入队顺序自动发出。
   */
  async function send(text: string, permissionMode: string, effort: string): Promise<void> {
    const prompt = text.trim();
    if (!prompt) return;

    const attachments = pending();
    if (attachments.some((item) => item.status === 'uploading')) {
      showToast('附件还在上传，请稍候', 'info');
      return;
    }

    const message: QueuedMessage = {
      id: nextId(),
      text: prompt,
      attachments: attachments
        .filter((item) => item.status === 'ready' && item.path)
        .map((item) => ({
          path: item.path ?? '',
          fileName: item.fileName,
          mediaType: item.mediaType,
          size: item.size,
          webPath: item.webPath,
        })),
      permissionMode,
      effort,
    };

    const willQueue = streaming() || recovering || queue().length > 0;

    // 气泡先进线程：即使这一轮发不出去（排队 / 失败），消息也不会离开视野
    setItems((list) => [
      ...list,
      {
        kind: 'user',
        id: message.id,
        text: prompt,
        attachments: message.attachments.map((item) => ({
          FileName: item.fileName,
          MediaType: item.mediaType,
          FileSize: item.size,
          SourcePath: item.path,
          Kind: item.mediaType.startsWith('image/') ? 'image' : 'file',
          WebPath: item.webPath,
        })),
        time: new Date().toISOString(),
        state: willQueue ? 'queued' : 'sending',
      },
    ]);

    // 附件随消息一起走，输入区的待发送列表清空并释放预览 URL
    for (const item of attachments) {
      if (item.previewUrl) URL.revokeObjectURL(item.previewUrl);
    }
    setPending([]);

    // 续流期间旧轮次还在服务端跑：先入队，等续流接上或收尾后再发；
    // 此时直接发会起新流打断续流（resume 会被「本地流还活着」短路），还会撞 409
    if (willQueue) {
      setQueue((list) => [...list, message]);
      // 队列里有更早的失败消息时也要按序补发，不能插队
      if (!streaming() && !recovering) void drainQueue();
      return;
    }
    // 直接发送被服务端拒（上一轮还没真正收尾）时同样放回队列，避免消息丢失
    if (!(await runMessage(message))) {
      setQueue((list) => [message, ...list]);
    }
  }

  /** 依次发出队列中的消息：同一时刻只跑一条，本轮收尾后自动接上。 */
  async function drainQueue(): Promise<void> {
    if (draining || disposed || recovering) return;
    draining = true;
    try {
      while (!disposed && !streaming() && queue().length > 0) {
        const next = queue()[0];
        setQueue((list) => list.slice(1));
        const sent = await runMessage(next);
        if (!sent) {
          // 服务端上一轮还没真正收尾：放回队首，等下一次收尾再发，绝不把用户消息丢掉
          setQueue((list) => [next, ...list]);
          break;
        }
      }
    } finally {
      draining = false;
    }
  }

  /** 取消一条排队中 / 发送失败的消息（气泡上的「取消」）。 */
  function removeQueued(id: string): void {
    const item = items().find((entry) => entry.id === id);
    if (item?.kind === 'user' && item.state === 'sending') return;
    setQueue((list) => list.filter((entry) => entry.id !== id));
    setItems((list) => list.filter((entry) => entry.id !== id));
  }

  /** 重试一条发送失败的消息（气泡上的「重试」）。 */
  function retryQueued(id: string): void {
    if (!queue().some((entry) => entry.id === id)) return;
    setUserItemState(id, 'queued');
    void drainQueue();
  }

  /** 改写用户气泡的投递状态；`undefined` 表示已发出（回归普通气泡）。 */
  function setUserItemState(id: string, state: UserItemState | undefined): void {
    setItems((list) =>
      list.map((item) => (item.kind === 'user' && item.id === id ? { ...item, state } : item)),
    );
  }

  /** 409：服务端仍登记着上一轮（done 帧先到、轮次表后清）。 */
  function isRunConflict(error: unknown): boolean {
    return error instanceof ApiError && error.code === 409;
  }

  /** 真正起一轮：消费 `/agent/chat` 的 SSE。返回 false 表示服务端拒绝（409 重试耗尽）。 */
  async function runMessage(message: QueuedMessage): Promise<boolean> {
    const prompt = message.text.trim();
    if (!prompt) return true;

    const assistantId = nextId();
    // 新一轮开始：重置提示计数，快照回放按「已实时渲染条数」跳过前缀
    renderedNotices = 0;

    const assistantItem: ChatItem = {
      kind: 'assistant',
      id: assistantId,
      blocks: [],
      streaming: true,
      history: false,
      time: new Date().toISOString(),
    };

    // 用户气泡已在线程里（发送时就追加），这里只改状态；
    // 助手占位紧跟这条用户消息，后面再排队的消息气泡不会被垫到它前面
    setUserItemState(message.id, 'sending');
    setItems((list) => {
      const index = list.findIndex((item) => item.kind === 'user' && item.id === message.id);
      if (index < 0) return [...list, assistantItem];
      return [...list.slice(0, index + 1), assistantItem, ...list.slice(index + 1)];
    });
    setPlans([]);
    setProgress([]);
    setStreaming(true);
    setRunId(null);
    controller = new AbortController();
    activeAssistantId = assistantId;
    // 新轮次从 1 开始编号：上一轮留下的序号必须清掉，否则重连会按旧序号续传
    lastSeq = undefined;

    // 请求即将发出：气泡回归普通样式；此后断线由续流逻辑接管，不再算「未发出」
    setUserItemState(message.id, undefined);

    let done = false;
    // 本轮流的控制器：收尾可能把 controller 置空，回调里必须比对这条流自己的控制器
    const streamController = controller;

    for (let attempt = 0; ; attempt += 1) {
      let conflict = false;
      await streamPost(
        '/agent/chat',
        {
          prompt,
          sessionId,
          attachments: message.attachments.map((item) => item.path),
          enableTools: true,
          permissionMode: message.permissionMode,
          reasoningEffort: message.effort || null,
        },
        {
          onFrame: ({ event, data, id }) => {
            if (controller !== streamController) return;
            if (id !== undefined) lastSeq = id;
            if (applyFrame(assistantId, event, data)) done = true;
          },
          onError: (error) => {
            // 这条流已被新流接替（切前台重连）：旧流的错误不再驱动收尾或续流
            if (controller !== streamController) return;
            // 用户主动停止时不续流，直接收尾
            if (streamController.signal.aborted) {
              finishStream(assistantId, false, streamController);
              return;
            }
            // 409：上一轮刚收尾、登记表还没清。短退避后重发同一条，不把排队消息丢掉
            if (!done && isRunConflict(error)) {
              conflict = true;
              return;
            }
            // 断线：按 1s / 2s / 4s 退避重试续流，最多 3 次；耗尽后才提示
            // 错误分支不会触发 onClose（sse.ts 在错误路径只调 onError），收尾由续流逻辑负责。
            // 首帧超时另给宽限：服务端可能还在初始化，此刻探活会误报「没有活跃轮次」
            const beforeFirstFrame = error instanceof SseTimeoutError && error.beforeFirstFrame;
            void recoverAfterDrop(assistantId, error, beforeFirstFrame);
          },
          onClose: () => {
            // 这条流已被新流接替：收尾、提示都交给新流
            if (controller !== streamController) return;
            // 主动中止（切前台重连 / 关闭页面）不算断线，由发起方决定后续
            if (streamController.signal.aborted) {
              finishStream(assistantId, done, streamController);
              return;
            }
            if (!done) {
              // 没收到 done 就断了：服务端轮次不受影响（断连只摘订阅者）。
              // 代理空闲回收、服务端寿命兜底都会走到这里，自动接回而不是让气泡静默停住
              void recoverAfterDrop(assistantId, new Error('连接已中断，本轮仍在后台运行'));
              return;
            }
            finishStream(assistantId, true, streamController);
          },
        },
        streamController.signal,
        { idleMs: DEFAULT_IDLE_MS, firstFrameMs: DEFAULT_FIRST_FRAME_MS },
      );

      if (!conflict) break;
      if (attempt >= 3) {
        // 撤回本轮的空助手占位；用户气泡留在原位并标记失败，可重试 / 取消
        finishStream(assistantId, false, streamController);
        setItems((list) => list.filter((item) => item.id !== assistantId));
        setUserItemState(message.id, 'failed');
        pushNotice('上一轮尚未结束，这条消息未能发出', 'warn');
        return false;
      }
      await new Promise((resolve) => setTimeout(resolve, 300 * (attempt + 1)));
    }

    return true;
  }

  async function stop(): Promise<void> {
    try {
      await post('/agent/cancel', { sessionId, runId: runId() });
      showToast('已请求停止本轮', 'info');
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  }

  async function resolveConfirm(result: 'allow' | 'deny' | 'allowAll' | 'allowSession'): Promise<void> {
    const request = confirmation();
    if (!request) return;
    setConfirmation(null);
    try {
      await post('/agent/confirm', {
        sessionId,
        confirmationId: request.confirmationId,
        result,
      });
      if (result === 'deny') {
        showToast('已拒绝该工具调用', 'info');
      }
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  }

  function confirmTimeout(): void {
    setConfirmation(null);
    showToast('确认已超时，按拒绝处理', 'error');
  }

  /** 清空待作答提问并复位提交态。 */
  function clearQuestion(): void {
    setPendingQuestion(null);
    setQuestionSubmitting(false);
  }

  /** 回传问题答案；answers 必须覆盖本次提问的每一题（服务端按问题正文校验）。 */
  async function resolveQuestion(answers: UserQuestionAnswer[]): Promise<void> {
    const request = pendingQuestion();
    if (!request || questionSubmitting()) return;
    // 提交期间不卸载弹窗：失败时选项、自由输入与剩余时间都还在，可直接重试
    setQuestionSubmitting(true);
    try {
      await post('/agent/answer', {
        sessionId,
        questionId: request.questionId,
        answers,
      });
      clearQuestion();
    } catch (error) {
      // 404 = 服务端已无此提问（过期或已作答），重试没有意义；其余失败保留弹窗让用户重试
      if (error instanceof ApiError && error.code === 404) {
        clearQuestion();
      } else {
        setQuestionSubmitting(false);
        restoreQuestion(request);
      }
      showToast((error as Error).message, 'error');
    }
  }

  /** 用户取消作答：按「已取消」收尾，不算拒绝、不失败本轮。 */
  async function cancelQuestion(): Promise<void> {
    const request = pendingQuestion();
    if (!request || questionSubmitting()) return;
    setQuestionSubmitting(true);
    try {
      await post('/agent/answer', {
        sessionId,
        questionId: request.questionId,
        cancelled: true,
      });
      clearQuestion();
    } catch (error) {
      if (error instanceof ApiError && error.code === 404) {
        clearQuestion();
      } else {
        setQuestionSubmitting(false);
        restoreQuestion(request);
      }
      showToast((error as Error).message, 'error');
    }
  }

  /** 放回失败的提交；期间已收到新提问、或本轮已收尾时不再恢复。 */
  function restoreQuestion(request: PendingQuestionRequest): void {
    // 无论是否放回，提交态都要复位，否则下次进出弹窗仍是禁用状态
    setQuestionSubmitting(false);
    if (pendingQuestion()) return;
    // 本轮已收尾（done / 断线收尾已清空提问）：再放回只会留下一个答不上的弹窗
    if (!streaming()) return;
    setPendingQuestion(request);
  }

  /** 前端倒计时到点：按「已取消」回传，避免只清界面而服务端仍在等。 */
  function questionTimeout(): void {
    // 提交在途：以服务端返回为准，不再并发一条取消
    if (questionSubmitting()) return;
    const request = pendingQuestion();
    clearQuestion();
    showToast('提问已超时，已跳过作答', 'error');
    if (!request) return;
    void post('/agent/answer', {
      sessionId,
      questionId: request.questionId,
      cancelled: true,
    }).catch(() => {
      // 服务端自身也会超时收尾，这里失败无需打扰用户（界面已按超时清空）
    });
  }

  /** 把选中的文件加入待发送队列并逐个上传。 */
  async function addFiles(files: File[]): Promise<void> {
    if (files.length === 0) return;
    const policy = bootstrap()?.attachments;
    const current = pending();

    if (policy) {
      const remaining = policy.maxCount - current.length;
      if (remaining <= 0) {
        showToast(`每轮最多 ${policy.maxCount} 个附件`, 'error');
        return;
      }
      if (files.length > remaining) {
        showToast(`每轮最多 ${policy.maxCount} 个附件，多余的已忽略`, 'error');
        files = files.slice(0, remaining);
      }
    }

    const accepted: File[] = [];
    for (const file of files) {
      const extension = extensionOf(file.name);
      if (!extension) {
        showToast(`无法识别文件类型：${file.name}`, 'error');
        continue;
      }
      if (policy && !policy.extensions.some((item) => item.toLowerCase() === extension)) {
        showToast(`不支持的附件类型：${extension}`, 'error');
        continue;
      }
      if (policy && policy.undecodableExtensions.some((item) => item.toLowerCase() === extension)) {
        showToast(`${extension} 无法解码，请先转换为 PNG 或 JPEG`, 'error');
        continue;
      }
      if (policy && !isImageAttachment(policy, file) && file.size > policy.maxTextBytes) {
        showToast(`文本类附件不能超过 ${formatBytes(policy.maxTextBytes)}`, 'error');
        continue;
      }
      accepted.push(file);
    }

    let compressedCount = 0;
    let originalTotal = 0;
    let compressedTotal = 0;

    for (const file of accepted) {
      const id = nextId();
      const imageLike = isImageAttachment(policy, file);

      // 先建 pending 项再压缩：解码 + 多轮重编码要几秒，不能期间界面毫无反馈
      let previewUrl = imageLike ? URL.createObjectURL(file) : undefined;
      const item: PendingAttachment = {
        id,
        fileName: file.name,
        mediaType: file.type || 'application/octet-stream',
        size: file.size,
        status: 'uploading',
        previewUrl,
      };
      setPending((list) => [...list, item]);

      /** 预处理阶段失败/超限：撤掉刚建的 pending 项并提示。 */
      const reject = (reason: string): void => {
        setPending((list) => list.filter((entry) => entry.id !== id));
        if (previewUrl) URL.revokeObjectURL(previewUrl);
        showToast(`${file.name}：${reason}`, 'error');
      };

      let upload = file;
      if (policy && imageLike) {
        if (file.size > policy.maxImageUploadBytes) {
          reject(`图片不能超过 ${formatBytes(policy.maxImageUploadBytes)}`);
          continue;
        }

        // 图片先在本地缩放/重编码：别把几 MB 原图整份 Base64 上传，解码压力也留在浏览器侧
        try {
          const prepared = await prepareImageForUpload(
            file,
            policy.maxImageEdge,
            policy.maxImageBytes,
            policy.maxImagePixels,
          );
          if (prepared.rejectReason) {
            reject(prepared.rejectReason);
            continue;
          }
          upload = prepared.file;
          if (prepared.compressed) {
            compressedCount += 1;
            originalTotal += file.size;
            compressedTotal += upload.size;
          }
        } catch (error) {
          reject((error as Error).message);
          continue;
        }

        if (upload !== file) {
          // 换成压缩后的结果：预览与体积一起更新，旧的对象 URL 立即回收
          if (previewUrl) URL.revokeObjectURL(previewUrl);
          previewUrl = URL.createObjectURL(upload);
          setPending((list) =>
            list.map((entry) =>
              entry.id === id
                ? {
                    ...entry,
                    fileName: upload.name,
                    mediaType: upload.type || entry.mediaType,
                    size: upload.size,
                    previewUrl,
                  }
                : entry,
            ),
          );
        }
      }

      try {
        const contentBase64 = await fileToBase64(upload);
        const result = await post<AttachmentUploadResult>('/attachment/upload', {
          sessionId,
          files: [{ fileName: upload.name, contentBase64 }],
        });

        const uploaded = result?.accepted?.[0];
        const rejected = result?.rejected?.[0];
        if (uploaded) {
          setPending((list) =>
            list.map((entry) =>
              entry.id === id ? { ...entry, status: 'ready', path: uploaded.path, webPath: uploaded.webPath, size: uploaded.size } : entry,
            ),
          );
        } else {
          const reason = rejected?.reason ?? '上传失败';
          setPending((list) =>
            list.map((entry) => (entry.id === id ? { ...entry, status: 'error', error: reason } : entry)),
          );
          showToast(`${file.name}：${reason}`, 'error');
        }
      } catch (error) {
        const message = (error as Error).message;
        setPending((list) =>
          list.map((entry) => (entry.id === id ? { ...entry, status: 'error', error: message } : entry)),
        );
        showToast(`${file.name}：${message}`, 'error');
      }
    }

    if (compressedCount > 0) {
      showToast(
        `已压缩 ${compressedCount} 张图片：${formatBytes(originalTotal)} → ${formatBytes(compressedTotal)}`,
        'info',
      );
    }
  }

  function removePending(id: string): void {
    setPending((list) => {
      const target = list.find((item) => item.id === id);
      if (target?.previewUrl) URL.revokeObjectURL(target.previewUrl);
      return list.filter((item) => item.id !== id);
    });
  }

  /**
   * 页面回到前台 / 网络恢复时主动接回后台轮次。
   *
   * 后台期间连接可能已被 NAT 或系统休眠断开，而本地 `controller` 仍在，`resume()` 会被
   * 「本地流还活着」的短路挡掉；这里走断线恢复流程，先释放本地流再探活续流。
   */
  function reconnect(): void {
    if (disposed || recovering || !streaming() || !activeAssistantId) return;
    // 先掐掉本地流：被 NAT / 休眠半死的连接可能永远不会自己报错，
    // 不收掉就会出现「旧流 + 新 attach」两条流同时写同一气泡
    controller?.abort();
    void recoverAfterDrop(activeAssistantId, new Error('连接已中断，正在重连'));
  }

  const onVisibilityChange = (): void => {
    if (document.visibilityState === 'visible') reconnect();
  };

  document.addEventListener('visibilitychange', onVisibilityChange);
  window.addEventListener('online', reconnect);

  function dispose(): void {
    disposed = true;
    document.removeEventListener('visibilitychange', onVisibilityChange);
    window.removeEventListener('online', reconnect);
    controller?.abort();
    pending().forEach((item) => {
      if (item.previewUrl) URL.revokeObjectURL(item.previewUrl);
    });
  }

  return {
    sessionId,
    items,
    loadingHistory,
    historyError,
    streaming,
    runId,
    confirmation,
    pendingQuestion,
    questionSubmitting,
    pending,
    queue,
    plans,
    progress,
    loadHistory,
    resume,
    send,
    stop,
    resolveConfirm,
    confirmTimeout,
    resolveQuestion,
    cancelQuestion,
    questionTimeout,
    addFiles,
    removePending,
    removeQueued,
    retryQueued,
    dispose,
  };
}