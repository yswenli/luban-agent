import { sseRequest, toApiError } from './api';

export interface SseFrame {
  event: string;
  data: unknown;
}

export interface SseHandlers {
  onFrame: (frame: SseFrame) => void;
  onError?: (error: unknown) => void;
  onClose?: () => void;
}

/**
 * 用 `fetch` + `ReadableStream` 解析 SAEA 的 SSE 响应。
 *
 * 帧格式固定为 `event: {name}\ndata: {json}\n\n`；`: ping` 注释行直接丢弃。
 * 不用 `EventSource`：需要 POST、自定义 Authorization 头与中止控制。
 */
export async function streamPost(
  path: string,
  body: unknown,
  handlers: SseHandlers,
  signal?: AbortSignal,
): Promise<void> {
  let response: Response;
  try {
    response = await sseRequest(path, body, signal);
  } catch (error) {
    if (isAbort(error)) {
      handlers.onClose?.();
      return;
    }
    handlers.onError?.(error);
    return;
  }

  const contentType = response.headers.get('content-type') ?? '';
  if (!response.ok || !contentType.includes('text/event-stream') || !response.body) {
    handlers.onError?.(await toApiError(response));
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder('utf-8');
  let buffer = '';

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      buffer = buffer.replace(/\r\n/g, '\n');

      let boundary = buffer.indexOf('\n\n');
      while (boundary >= 0) {
        const block = buffer.slice(0, boundary);
        buffer = buffer.slice(boundary + 2);
        dispatch(block, handlers);
        boundary = buffer.indexOf('\n\n');
      }
    }

    // 收尾：服务端正常关闭时可能没有空行
    const tail = buffer.trim();
    if (tail) {
      dispatch(tail, handlers);
    }
    handlers.onClose?.();
  } catch (error) {
    if (isAbort(error)) {
      handlers.onClose?.();
      return;
    }
    handlers.onError?.(error);
  } finally {
    try {
      await reader.cancel();
    } catch {
      /* 连接已断 */
    }
  }
}

function dispatch(block: string, handlers: SseHandlers): void {
  let event = 'message';
  const dataLines: string[] = [];

  for (const rawLine of block.split('\n')) {
    const line = rawLine.trimEnd();
    if (!line || line.startsWith(':')) continue;

    if (line.startsWith('event:')) {
      event = line.slice(6).trim();
    } else if (line.startsWith('data:')) {
      dataLines.push(line.slice(5).trimStart());
    }
  }

  if (dataLines.length === 0) return;

  const text = dataLines.join('\n');
  let data: unknown = text;
  try {
    data = JSON.parse(text);
  } catch {
    /* 非 JSON 载荷按原文保留 */
  }

  handlers.onFrame({ event, data });
}

function isAbort(error: unknown): boolean {
  return (
    (error instanceof DOMException && error.name === 'AbortError') ||
    (error instanceof Error && error.name === 'AbortError')
  );
}