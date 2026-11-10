import type { ApiEnvelope } from './types';

export class ApiError extends Error {
  constructor(
    public readonly code: number,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

interface RuntimeConfig {
  userId?: string;
  port?: number;
}

/** 读取 WebHost 启动时写入的 `runtime-config.js`；开发模式下缺失即视为空配置。 */
export function runtimeConfig(): RuntimeConfig {
  const value = (window as unknown as { __LUBAN__?: RuntimeConfig }).__LUBAN__;
  return value ?? {};
}

/**
 * 请求头。
 *
 * 访问令牌不落前端：宿主把地址栏 `?t=` 换成 HttpOnly Cookie，浏览器之后的同源请求自动携带，
 * JS 读不到也无需读。`Authorization` 只给 CLI / Codex / 插件用。
 */
function authHeaders(): Record<string, string> {
  return { Accept: 'application/json' };
}

function buildQuery(params?: Record<string, string | number | boolean | null | undefined>): string {
  if (!params) return '';
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue;
    search.set(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}

async function parseEnvelope<T>(response: Response): Promise<T> {
  const text = await response.text();
  if (!text) {
    if (response.ok) return undefined as T;
    throw new ApiError(response.status, `请求失败（HTTP ${response.status}）`);
  }

  let envelope: ApiEnvelope<T>;
  try {
    envelope = JSON.parse(text) as ApiEnvelope<T>;
  } catch {
    throw new ApiError(response.status, text.slice(0, 200));
  }

  if (!envelope.success) {
    throw new ApiError(envelope.code || response.status, envelope.message || '请求失败');
  }

  return envelope.data as T;
}

export async function get<T>(
  path: string,
  params?: Record<string, string | number | boolean | null | undefined>,
): Promise<T> {
  const response = await fetch(`${path}${buildQuery(params)}`, {
    method: 'GET',
    headers: authHeaders(),
    // 访问令牌放在 HttpOnly Cookie 里，必须带上同源凭据
    credentials: 'same-origin',
  });
  return parseEnvelope<T>(response);
}

export async function post<T>(path: string, body: unknown = {}): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      ...authHeaders(),
      // SAEA.MVC 只在 Content-Type 精确含这个子串时才绑定 JSON 到动作参数
      'Content-Type': 'application/json; charset=utf-8',
    },
    body: JSON.stringify({ payload: JSON.stringify(body) }),
    credentials: 'same-origin',
  });
  return parseEnvelope<T>(response);
}

/** SSE 需要直接拿到 Response 与自定义请求头，故单独暴露请求构造。 */
export function sseRequest(path: string, body: unknown, signal?: AbortSignal): Promise<Response> {
  return fetch(path, {
    method: 'POST',
    headers: {
      ...authHeaders(),
      'Content-Type': 'application/json; charset=utf-8',
    },
    body: JSON.stringify({ payload: JSON.stringify(body) }),
    signal,
    credentials: 'same-origin',
  });
}

/** 把 HTTP 失败响应解析成可读错误（SSE 请求失败时用）。 */
export async function toApiError(response: Response): Promise<ApiError> {
  const text = await response.text().catch(() => '');
  if (text) {
    try {
      const envelope = JSON.parse(text) as ApiEnvelope<unknown>;
      if (envelope && envelope.success === false) {
        return new ApiError(envelope.code || response.status, envelope.message || '请求失败');
      }
    } catch {
      /* 非 JSON：走兜底 */
    }
  }
  return new ApiError(response.status, `请求失败（HTTP ${response.status}）`);
}