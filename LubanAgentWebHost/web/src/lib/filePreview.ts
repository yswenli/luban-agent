import { ApiError, toApiError } from './api';

/** 弹层里正在预览的文件：`path` 是会话链接里的原值，`workspaceId` 决定解析根目录。 */
export interface FilePreviewTarget {
  path: string;
  workspaceId?: string;
}

/** 弹层可展示的内容：排版好的 Markdown、源码文本、图片、音视频流或独立来源的 HTML。 */
export type FilePreviewContent =
  | { kind: 'markdown'; text: string }
  | { kind: 'text'; text: string }
  | { kind: 'image'; url: string }
  | { kind: 'video'; url: string }
  | { kind: 'audio'; url: string }
  /** `text` 保留 HTML 原文：预览用沙箱 iframe，切「查看源码」时直接展示，不再二次请求。 */
  | { kind: 'html'; url: string; text: string };

/** 组装宿主文件接口地址：直接打开仍可用（新标签页 / Ctrl+点击走的都是它）。 */
export function fileReadUrl(target: FilePreviewTarget): string {
  const query = new URLSearchParams({ path: target.path });
  if (target.workspaceId) query.set('workspaceId', target.workspaceId);
  return `/file/read?${query.toString()}`;
}

/** 组装下载地址：不受预览白名单与体积上限限制，任意工作区文件都可取回。 */
export function fileDownloadUrl(target: FilePreviewTarget): string {
  const query = new URLSearchParams({ path: target.path });
  if (target.workspaceId) query.set('workspaceId', target.workspaceId);
  return `/file/download?${query.toString()}`;
}

/** 弹层标题用的文件名（链接可能带百分号编码）。 */
export function filePreviewName(path: string): string {
  const bare = path.split(/[?#]/)[0] ?? '';
  const segment = bare.split(/[\\/]/).filter(Boolean).pop() ?? path;
  try {
    return decodeURIComponent(segment);
  } catch {
    return segment;
  }
}

/** 视频扩展名：与后端 `WebFileKinds` 的白名单保持一致。 */
const VIDEO_EXTENSIONS = new Set(['.mp4', '.m4v', '.webm', '.ogv', '.mov']);

/** 音频扩展名：与后端 `WebFileKinds` 的白名单保持一致。 */
const AUDIO_EXTENSIONS = new Set(['.mp3', '.wav', '.ogg', '.oga', '.opus', '.m4a', '.aac', '.flac']);

/** 扩展名对应的音视频类型；不是音视频时返回 null。 */
export function mediaKindOf(path: string): 'video' | 'audio' | null {
  const extension = extensionOf(path);
  if (VIDEO_EXTENSIONS.has(extension)) return 'video';
  if (AUDIO_EXTENSIONS.has(extension)) return 'audio';
  return null;
}

/**
 * 取文件内容到弹层。
 *
 * 类型按宿主回的 `Content-Type` 决定，不自己猜扩展名：宿主已经按白名单挡掉了二进制与超限文件，
 * 这里只负责把 `text/plain` 的正文再按扩展名分成「Markdown 渲染」与「原始文本」。
 * HTML 走 blob URL 交给沙箱 iframe，脚本与同源权限都不放行。
 * 音视频不给 blob：直接把接口地址交给 `<video>`/`<audio>`，浏览器自己流式取，避免整个文件进内存。
 */
export async function loadFilePreview(target: FilePreviewTarget): Promise<FilePreviewContent> {
  const media = mediaKindOf(target.path);
  if (media) return { kind: media, url: fileReadUrl(target) };

  const response = await fetch(fileReadUrl(target), {
    headers: { Accept: '*/*' },
    // 门禁认 HttpOnly Cookie，必须带同源凭据
    credentials: 'same-origin',
  });

  if (!response.ok) {
    // 401 交给统一的整页「令牌已过期」提示，其余按状态码给一句能落地的说明
    if (response.status === 401) throw await toApiError(response);
    throw new ApiError(response.status, previewErrorMessage(response.status));
  }

  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.startsWith('image/')) {
    return { kind: 'image', url: URL.createObjectURL(await response.blob()) };
  }

  const text = await response.text();
  if (!contentType.startsWith('text/plain')) {
    // 未知类型不猜：按源码文本展示比直接塞进 DOM 安全
    return { kind: 'text', text };
  }

  const extension = extensionOf(target.path);
  if (extension === '.md' || extension === '.markdown') return { kind: 'markdown', text };
  if (extension === '.html' || extension === '.htm') {
    return {
      kind: 'html',
      url: URL.createObjectURL(new Blob([text], { type: 'text/html; charset=utf-8' })),
      text,
    };
  }

  return { kind: 'text', text };
}

/** 链接原值的扩展名（小写，忽略查询串与锚点）。 */
function extensionOf(path: string): string {
  const bare = path.split(/[?#]/)[0] ?? '';
  const name = bare.split(/[\\/]/).filter(Boolean).pop() ?? '';
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(dot).toLowerCase() : '';
}

/** 宿主文件接口的失败状态码 → 中文说明。 */
function previewErrorMessage(status: number): string {
  switch (status) {
    case 404:
      return '工作区里找不到这个文件，可能已被移动或删除。';
    case 413:
      return '文件超过预览上限（文本 2 MB、图片 10 MB），请在本地打开。';
    case 415:
      return '这个格式不支持页内预览，请在本地打开。';
    case 500:
      return '读取失败：文件可能被其他程序占用，或权限不足。';
    default:
      return `预览失败（HTTP ${status}）。`;
  }
}