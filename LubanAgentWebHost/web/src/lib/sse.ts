import { sseRequest, toApiError } from './api';
import { t } from '../i18n';

export interface SseFrame {
  event: string;
  data: unknown;
  /** 轮次内帧序号（服务端 `id:` 行）；心跳与未分配序号的帧没有该值。 */
  id?: number;
}

export interface SseHandlers {
  onFrame: (frame: SseFrame) => void;
  /** 服务端以 204 空体表示「没有可续的轮次」；不设该回调时按错误处理。 */
  onEmpty?: () => void;
  onError?: (error: unknown) => void;
  onClose?: () => void;
}

/**
 * 长连接判死期限。
 *
 * 服务端每 15s 发一条 `: ping` 心跳，45s 相当于连丢三拍；首帧期限短一些：
 * 写出响应头后服务端会立刻补发 `session` 或 `run.snapshot`。
 */
export interface SseLimits {
  /** 连续这么久没有收到任何字节（含心跳注释）即按断线报错；缺省或 ≤0 关闭。 */
  idleMs?: number;
  /** 首帧期限；缺省沿用 `idleMs`。 */
  firstFrameMs?: number;
}

/** 收到首帧前的订阅期限（毫秒）；页面在后台时不计时。 */
export const DEFAULT_FIRST_FRAME_MS = 30_000;

/** 空闲判死期限（毫秒）；服务端心跳 15s，留三拍余量。 */
export const DEFAULT_IDLE_MS = 45_000;

/** 判死时对外的错误文案：区分「连接被静默掐断」与主动取消。 */
const silentDropMessage = () => t('sse.silentDrop');

/**
 * 客户端判死（长时间无数据 / 首帧未到）时抛出的错误。
 *
 * 调用方需要区分「首帧都没收到」与「流到一半断掉」：前者服务端可能还在初始化、
 * 轮次尚未登记，此时探活会得到「没有活跃轮次」的假阴性。
 */
export class SseTimeoutError extends Error {
  /** true = 连首帧都没收到。 */
  readonly beforeFirstFrame: boolean;

  constructor(beforeFirstFrame: boolean) {
    super(silentDropMessage());
    this.name = 'SseTimeoutError';
    this.beforeFirstFrame = beforeFirstFrame;
  }
}

/**
 * 用 `fetch` + `ReadableStream` 解析 SAEA 的 SSE 响应。
 *
 * 帧格式固定为 `id: {seq}\nevent: {name}\ndata: {json}\n\n`（无序号时省略 `id:` 行）；
 * `: ping` 注释行直接丢弃，但仍算「收到数据」，会重置空闲计时。
 * 不用 `EventSource`：需要 POST 请求体、流式中止与逐帧回调；访问凭据由宿主换发的 Cookie 自动携带。
 */
export async function streamPost(
  path: string,
  body: unknown,
  handlers: SseHandlers,
  signal?: AbortSignal,
  limits?: SseLimits,
): Promise<void> {
  // 内部控制器把「用户取消」与「看门狗判死」统一成一次 abort，再按原因分派回调
  const local = new AbortController();
  let timedOut = false;
  const onExternalAbort = () => local.abort();

  if (signal) {
    if (signal.aborted) {
      handlers.onClose?.();
      return;
    }
    signal.addEventListener('abort', onExternalAbort, { once: true });
  }

  const idleMs = limits?.idleMs ?? 0;
  const firstFrameMs = limits?.firstFrameMs ?? idleMs;
  let timer: ReturnType<typeof setTimeout> | undefined;

  const arm = (ms: number): void => {
    if (timer !== undefined) clearTimeout(timer);
    if (ms <= 0) {
      timer = undefined;
      return;
    }
    timer = setTimeout(() => {
      // 页面在后台时定时器会被节流，且此时本就不该按「长时间无数据」判死：重新计时
      if (typeof document !== 'undefined' && document.visibilityState === 'hidden') {
        arm(ms);
        return;
      }
      timedOut = true;
      local.abort();
    }, ms);
  };

  const disarm = (): void => {
    if (timer !== undefined) clearTimeout(timer);
    timer = undefined;
  };

  let response: Response;
  // 首帧期限从发出请求就开始计：连响应头都迟迟不回来同样按静默断开处理
  let received = false;
  arm(firstFrameMs);
  try {
    response = await sseRequest(path, body, local.signal);
  } catch (error) {
    disarm();
    signal?.removeEventListener('abort', onExternalAbort);
    if (isAbort(error)) {
      if (timedOut) handlers.onError?.(new SseTimeoutError(!received));
      else handlers.onClose?.();
      return;
    }
    handlers.onError?.(error);
    return;
  }

  // 204 空体是「没有可续的轮次」的正常应答，必须先于 Content-Type 判定拦下：
  // 否则会被当成非 SSE 响应，前端看到「请求失败（HTTP 204）」
  if (response.status === 204) {
    disarm();
    signal?.removeEventListener('abort', onExternalAbort);
    handlers.onEmpty?.();
    return;
  }

  const contentType = response.headers.get('content-type') ?? '';
  if (!response.ok || !contentType.includes('text/event-stream') || !response.body) {
    disarm();
    signal?.removeEventListener('abort', onExternalAbort);
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

      // 任意字节都算活着：注释心跳也走这条路径；首帧之后改为按空闲期限计时
      received = true;
      arm(idleMs);

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
      if (timedOut) {
        handlers.onError?.(new SseTimeoutError(!received));
      } else {
        handlers.onClose?.();
      }
      return;
    }
    handlers.onError?.(error);
  } finally {
    disarm();
    signal?.removeEventListener('abort', onExternalAbort);
    try {
      await reader.cancel();
    } catch {
      /* 连接已断 */
    }
  }
}

function dispatch(block: string, handlers: SseHandlers): void {
  let event = 'message';
  let id: number | undefined;
  const dataLines: string[] = [];

  for (const rawLine of block.split('\n')) {
    const line = rawLine.trimEnd();
    if (!line || line.startsWith(':')) continue;

    if (line.startsWith('event:')) {
      event = line.slice(6).trim();
    } else if (line.startsWith('data:')) {
      dataLines.push(line.slice(5).trimStart());
    } else if (line.startsWith('id:')) {
      const parsed = Number(line.slice(3).trim());
      if (Number.isInteger(parsed) && parsed > 0) id = parsed;
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

  handlers.onFrame({ event, data, id });
}

function isAbort(error: unknown): boolean {
  return (
    (error instanceof DOMException && error.name === 'AbortError') ||
    (error instanceof Error && error.name === 'AbortError')
  );
}