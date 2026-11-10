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
          const payload = (data ?? {}) as Record<string, unknown>;
          switch (event) {
            case 'session':
              if (typeof payload.runId === 'string') setRunId(payload.runId);
              break;

            case 'think.delta':
              updateAssistant(assistantId, (item) => {
                item.thinking += String(payload.delta ?? '');
              });
              break;

            case 'text.delta':
              updateAssistant(assistantId, (item) => {
                item.text += String(payload.delta ?? '');
              });
              break;

            case 'tool.started':
              updateAssistant(assistantId, (item) => {
                item.tools.push({
                  callId: String(payload.callId ?? nextId()),
                  name: String(payload.name ?? 'tool'),
                  argumentsText: pretty(payload.arguments),
                  status: 'running',
                });
              });
              break;

            case 'tool.completed':
              updateTool(assistantId, String(payload.callId ?? ''), (tool) => {
                tool.status = 'done';
                tool.resultSummary = String(payload.resultSummary ?? '');
              });
              break;

            case 'tool.failed':
              updateTool(assistantId, String(payload.callId ?? ''), (tool) => {
                tool.status = 'failed';
                tool.error = String(payload.error ?? '工具执行失败');
              });
              break;

            case 'tool.denied':
              setItems((list) => [
                ...list,
                {
                  kind: 'notice',
                  id: nextId(),
                  text: `已拒绝工具调用：${String(payload.toolName ?? '')}`,
                  tone: 'warn',
                },
              ]);
              break;

            case 'tool.confirm':
              setConfirmation({
                confirmationId: String(payload.confirmationId ?? ''),
                toolName: String(payload.toolName ?? '工具调用'),
                arguments: payload.arguments,
                timeoutSeconds: Number(payload.timeoutSeconds ?? 60),
              });
              break;

            case 'attachment.added':
              setItems((list) => [
                ...list,
                {
                  kind: 'notice',
                  id: nextId(),
                  text: `已加入附件：${String(payload.fileName ?? '')}（${formatBytes(Number(payload.fileSize ?? 0))}）`,
                  tone: 'info',
                },
              ]);
              break;

            case 'attachment.skipped':
              setItems((list) => [
                ...list,
                {
                  kind: 'notice',
                  id: nextId(),
                  text: `附件已跳过：${String(payload.source ?? '')} — ${String(payload.reason ?? '')}`,
                  tone: 'warn',
                },
              ]);
              break;

            case 'plan.item':
              setPlans((list) => [...list, { toolName: String(payload.toolName ?? ''), arguments: payload.arguments }]);
              break;

            case 'orchestration.progress': {
              const text = String(payload.message ?? payload.text ?? '');
              if (text) setProgress((list) => [...list, text]);
              break;
            }

            case 'error':
              setItems((list) => [
                ...list,
                { kind: 'notice', id: nextId(), text: String(payload.message ?? '对话失败'), tone: 'error' },
              ]);
              break;

            case 'done':
              done = true;
              break;

            default:
              break;
          }
        },
        onError: (error) => {
          setItems((list) => [
            ...list,
            { kind: 'notice', id: nextId(), text: (error as Error).message ?? '对话连接失败', tone: 'error' },
          ]);
        },
        onClose: () => {
          updateAssistant(assistantId, (item) => {
            item.streaming = false;
          });
          setStreaming(false);
          setRunId(null);
          setConfirmation(null);
          controller = null;
          if (done) {
            void refreshSessions();
          }
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
    send,
    stop,
    resolveConfirm,
    confirmTimeout,
    addFiles,
    removePending,
    dispose,
  };
}