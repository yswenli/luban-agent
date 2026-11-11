import { createSignal } from 'solid-js';

import { get, post } from '../lib/api';
import { extensionOf, formatBytes } from '../lib/format';
import { streamPost } from '../lib/sse';
import type {
  AttachmentUploadResult,
  PendingAttachment,
  SessionMessage,
  StoredAttachment,
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
}

export type ChatItem =
  | { kind: 'user'; id: string; text: string; attachments: StoredAttachment[]; time: string }
  | {
      kind: 'assistant';
      id: string;
      text: string;
      thinking: string;
      tools: ToolCard[];
      streaming: boolean;
      history: boolean;
      time: string;
    }
  | { kind: 'notice'; id: string; text: string; tone: 'info' | 'warn' | 'error' | 'ok' };

export interface ToolConfirmRequest {
  confirmationId: string;
  toolName: string;
  arguments: unknown;
  timeoutSeconds: number;
}

export interface PlanEntry {
  toolName: string;
  arguments: unknown;
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
        text: message.content,
        thinking: message.thinking ?? '',
        tools: parseToolCalls(message.toolCalls),
        streaming: false,
        history: true,
        time,
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

/** 单个会话的对话状态；实例由 `stores/chats.ts` 的会话池长期持有，只有销毁会话时才中止在途流。 */
export function createChat(sessionId: string) {
  const [items, setItems] = createSignal<ChatItem[]>([]);
  const [loadingHistory, setLoadingHistory] = createSignal(true);
  const [historyLoaded, setHistoryLoaded] = createSignal(false);
  const [historyError, setHistoryError] = createSignal<string | null>(null);
  const [streaming, setStreaming] = createSignal(false);
  const [runId, setRunId] = createSignal<string | null>(null);
  const [confirmation, setConfirmation] = createSignal<ToolConfirmRequest | null>(null);
  const [pending, setPending] = createSignal<PendingAttachment[]>([]);
  const [plans, setPlans] = createSignal<PlanEntry[]>([]);
  const [progress, setProgress] = createSignal<string[]>([]);

  let controller: AbortController | null = null;
  /** 会话实例是否已销毁（关标签）：销毁后不再挂新的订阅。 */
  let disposed = false;
  /** 本轮已实时渲染的提示条数；快照回放时跳过这段前缀，避免同页重连重复显示。 */
  let renderedNotices = 0;

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
      setItems(toHistoryItems(messages ?? []));
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
        const copy = { ...item, tools: item.tools.map((tool) => ({ ...tool })) };
        mutate(copy);
        return copy;
      }),
    );
  }

  function updateTool(id: string, callId: string, mutate: (tool: ToolCard) => void): void {
    updateAssistant(id, (item) => {
      const tool = item.tools.find((entry) => entry.callId === callId);
      if (tool) mutate(tool);
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

      case 'think.delta':
        updateAssistant(assistantId, (item) => {
          item.thinking += String(payload.delta ?? '');
        });
        return false;

      case 'text.delta':
        updateAssistant(assistantId, (item) => {
          item.text += String(payload.delta ?? '');
        });
        return false;

      case 'tool.started':
        updateAssistant(assistantId, (item) => {
          item.tools.push({
            callId: String(payload.callId ?? nextId()),
            name: String(payload.name ?? 'tool'),
            argumentsText: pretty(payload.arguments),
            status: 'running',
          });
        });
        return false;

      case 'tool.completed':
        updateTool(assistantId, String(payload.callId ?? ''), (tool) => {
          tool.status = 'done';
          tool.resultSummary = String(payload.resultSummary ?? '');
        });
        return false;

      case 'tool.failed':
        updateTool(assistantId, String(payload.callId ?? ''), (tool) => {
          tool.status = 'failed';
          tool.error = String(payload.error ?? '工具执行失败');
        });
        return false;

      case 'tool.denied':
        pushFrameNotice(`已拒绝工具调用：${String(payload.toolName ?? '')}`, 'warn', noticeKey);
        return false;

      case 'tool.confirm':
        setConfirmation({
          confirmationId: String(payload.confirmationId ?? ''),
          toolName: String(payload.toolName ?? '工具调用'),
          arguments: payload.arguments,
          timeoutSeconds: Number(payload.timeoutSeconds ?? 60),
        });
        return false;

      case 'attachment.added':
        pushFrameNotice(`已加入附件：${String(payload.fileName ?? '')}（${formatBytes(Number(payload.fileSize ?? 0))}）`, 'info', noticeKey);
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
      ? (payload.tools as Record<string, unknown>[]).map((tool) => ({
          callId: String(tool.callId ?? nextId()),
          name: String(tool.name ?? 'tool'),
          argumentsText: pretty(tool.arguments),
          status: (tool.status as ToolStatus) ?? 'running',
          resultSummary: tool.resultSummary === undefined || tool.resultSummary === null
            ? undefined
            : String(tool.resultSummary),
          error: tool.error === undefined || tool.error === null ? undefined : String(tool.error),
        }))
      : [];

    const bubble: ChatItem = {
      kind: 'assistant',
      id: assistantId,
      text: String(payload.text ?? ''),
      thinking: String(payload.thinking ?? ''),
      tools,
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
      });
    }

    if (typeof payload.runId === 'string') setRunId(payload.runId);
  }

  /** 结束本轮本地流状态（停止 / 断线 / 正常收尾共用）。 */
  function finishStream(assistantId: string | null, done: boolean): void {
    if (assistantId) {
      updateAssistant(assistantId, (item) => {
        item.streaming = false;
      });
    }
    setStreaming(false);
    setRunId(null);
    setConfirmation(null);
    controller = null;
    if (done) void refreshSessions();
  }

  /**
   * 探活并接上服务端正在进行的轮次；本地流还活着时直接返回。
   *
   * 返回值：true = 本轮已有结论（已接上并收到 done、attach 回 204 后已补历史、
   * 或探活显示已无活跃轮次且历史已补齐）；false = 没能判定/接上（探活失败、被中止、
   * 内部 attach 重试耗尽），调用方可据此重试。
   */
  async function resume(assistantId?: string): Promise<boolean> {
    // 会话实例已销毁（关标签）：绝不再挂新的订阅，否则留下无人读取的流
    if (disposed) return false;

    // streaming 为真但 controller 已释放，说明上一轮流死在了半路（如 SSE 出错未复位），
    // 这种残留状态不能挡住续流，否则该会话永远接不上。
    // 反之本地流确实在跑：本轮已有订阅者，重试不该再插一手。
    if (streaming() && controller !== null) return true;

    let active = false;
    try {
      const state = await post<{ active?: boolean }>('/agent/active', { sessionId });
      active = state?.active === true;
    } catch {
      // 探活本身失败（网络未恢复）：状态未知，交给调用方决定是否重试
      return false;
    }

    if (!active) {
      // 轮次已结束（跑完或被停止）：正文已入库，补历史即视为本轮收尾完成
      if (!historyLoaded() || items().length === 0) await loadHistory(true);
      return true;
    }

    const targetId = assistantId ?? nextId();
    controller = new AbortController();
    setStreaming(true);
    setRunId(null);

    for (let attempt = 0; ; attempt += 1) {
      let done = false;
      // 探活通过、attach 时轮次已跑完（跑完与 attach 之间的竞态）
      let empty = false;
      let started = false;
      await streamPost(
        '/agent/attach',
        { sessionId },
        {
          onFrame: ({ event, data }) => {
            if (event === 'run.snapshot') {
              restoreFromSnapshot(targetId, (data ?? {}) as Record<string, unknown>);
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
                  text: '',
                  thinking: '',
                  tools: [],
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
        controller.signal,
      );

      if (empty) {
        // 轮次已结束：内容在库里，直接补历史；不能重试（重试只会一直 204）
        await loadHistory(true);
        finishStream(started ? targetId : null, true);
        return true;
      }
      if (done) {
        finishStream(targetId, true);
        return true;
      }
      if (controller === null || controller.signal.aborted) {
        finishStream(targetId, false);
        return false;
      }
      if (attempt >= 3) {
        finishStream(targetId, false);
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
   */
  async function recoverAfterDrop(assistantId: string, error: unknown): Promise<void> {
    // 先释放本地流状态：resume() 会被「streaming 且 controller 非空」直接短路
    finishStream(assistantId, false);

    for (let attempt = 0; attempt < 3; attempt += 1) {
      await new Promise((resolve) => setTimeout(resolve, 1000 * 2 ** attempt));
      // 等待期间关掉了标签：不再续流，也不提示
      if (disposed) return;
      // 探活/attach 任一步得到结论即结束：本轮还在跑就接着收帧，已跑完就补历史
      if (await resume(assistantId)) return;
    }

    pushNotice((error as Error | undefined)?.message ?? '对话连接失败', 'error');
    finishStream(assistantId, false);
  }

  async function send(text: string, permissionMode: string, effort: string): Promise<void> {
    const prompt = text.trim();
    if (!prompt) return;
    if (streaming()) {
      showToast('本轮还在进行中，请先停止', 'info');
      return;
    }

    const attachments = pending();
    if (attachments.some((item) => item.status === 'uploading')) {
      showToast('附件还在上传，请稍候', 'info');
      return;
    }
    const ready = attachments.filter((item) => item.status === 'ready' && item.path);

    const assistantId = nextId();
    // 新一轮开始：重置提示计数，快照回放按「已实时渲染条数」跳过前缀
    renderedNotices = 0;
    const now = new Date().toISOString();

    const userItem: ChatItem = {
      kind: 'user',
      id: nextId(),
      text: prompt,
      attachments: ready.map((item) => ({
        FileName: item.fileName,
        MediaType: item.mediaType,
        FileSize: item.size,
        SourcePath: item.path ?? '',
        Kind: item.mediaType.startsWith('image/') ? 'image' : 'file',
      })),
      time: now,
    };

    const assistantItem: ChatItem = {
      kind: 'assistant',
      id: assistantId,
      text: '',
      thinking: '',
      tools: [],
      streaming: true,
      history: false,
      time: new Date().toISOString(),
    };

    setItems((list) => [...list, userItem, assistantItem]);
    setPlans([]);
    setProgress([]);
    setPending([]);
    setStreaming(true);
    setRunId(null);
    controller = new AbortController();

    let done = false;

    await streamPost(
      '/agent/chat',
      {
        prompt,
        sessionId,
        attachments: ready.map((item) => item.path),
        enableTools: true,
        permissionMode,
        reasoningEffort: effort || null,
      },
      {
        onFrame: ({ event, data }) => {
          if (applyFrame(assistantId, event, data)) done = true;
        },
        onError: (error) => {
          // 用户主动停止时不续流，直接收尾
          if (controller?.signal.aborted) {
            finishStream(assistantId, false);
            return;
          }
          // 断线：按 1s / 2s / 4s 退避重试续流，最多 3 次；耗尽后才提示
          // 错误分支不会触发 onClose（sse.ts 在错误路径只调 onError），收尾由续流逻辑负责
          void recoverAfterDrop(assistantId, error);
        },
        onClose: () => {
          // 没收到 done 就断了：服务端轮次不受影响（断连只摘订阅者），
          // 但本页已收不到后续帧，明确告知用户而不是让气泡静默停住
          if (!done && !controller?.signal.aborted) {
            pushNotice('连接已中断，本轮仍在后台运行；重新进入该会话可继续接收', 'warn');
          }
          finishStream(assistantId, done);
        },
      },
      controller.signal,
    );
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
      if (policy && !file.type.startsWith('image/') && file.size > policy.maxTextBytes) {
        showToast(`文本类附件不能超过 ${formatBytes(policy.maxTextBytes)}`, 'error');
        continue;
      }
      accepted.push(file);
    }

    for (const file of accepted) {
      const id = nextId();
      const previewUrl = file.type.startsWith('image/') ? URL.createObjectURL(file) : undefined;
      const item: PendingAttachment = {
        id,
        fileName: file.name,
        mediaType: file.type || 'application/octet-stream',
        size: file.size,
        status: 'uploading',
        previewUrl,
      };
      setPending((list) => [...list, item]);

      try {
        const contentBase64 = await fileToBase64(file);
        const result = await post<AttachmentUploadResult>('/attachment/upload', {
          sessionId,
          files: [{ fileName: file.name, contentBase64 }],
        });

        const uploaded = result?.accepted?.[0];
        const rejected = result?.rejected?.[0];
        if (uploaded) {
          setPending((list) =>
            list.map((entry) =>
              entry.id === id ? { ...entry, status: 'ready', path: uploaded.path, size: uploaded.size } : entry,
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
  }

  function removePending(id: string): void {
    setPending((list) => {
      const target = list.find((item) => item.id === id);
      if (target?.previewUrl) URL.revokeObjectURL(target.previewUrl);
      return list.filter((item) => item.id !== id);
    });
  }

  function dispose(): void {
    disposed = true;
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
    pending,
    plans,
    progress,
    loadHistory,
    resume,
    send,
    stop,
    resolveConfirm,
    confirmTimeout,
    addFiles,
    removePending,
    dispose,
  };
}