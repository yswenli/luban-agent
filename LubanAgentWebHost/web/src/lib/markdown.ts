import DOMPurify from 'dompurify';
import { marked } from 'marked';
import { t } from '../i18n';

marked.setOptions({
  gfm: true,
  breaks: true,
});

/**
 * Markdown → 安全 HTML。
 *
 * 全仓库内容都来自本机 Agent 输出，仍统一过 DOMPurify：模型可能回显任意网页片段，
 * 页面又持有本机 API 令牌，不能给内联脚本任何执行机会。
 */
export function renderMarkdown(text: string): string {
  const html = marked.parse(text ?? '', { async: false }) as string;
  return DOMPurify.sanitize(html, {
    USE_PROFILES: { html: true },
    ADD_ATTR: ['target', 'rel'],
    FORBID_TAGS: ['style', 'form', 'input', 'button'],
    FORBID_ATTR: ['style', 'onerror', 'onload'],
  });
}

/**
 * 本宿主的文件接口路径：模型经「取文件链接」工具拿到的是带 Host 的绝对地址，
 * 这类链接同样要在弹层里打开，否则会整页跳到原文接口、丢掉当前会话。
 */
const FILE_READ_PATH = /\/file\/read\/?$/i;

/** 从 `/file/read?...` 链接里取出预览目标；不是该接口返回 null。 */
function readFileReadTarget(value: string): { path: string; workspaceId?: string } | null {
  let url: URL;
  try {
    url = new URL(value, window.location.href);
  } catch {
    return null;
  }

  if (!FILE_READ_PATH.test(url.pathname)) return null;

  const path = url.searchParams.get('path')?.trim();
  if (!path) return null;

  return { path, workspaceId: url.searchParams.get('workspaceId') ?? undefined };
}

/**
 * 把工作区相对链接改写成宿主文件接口，点击后在页内弹层里预览原文件。
 *
 * 会话里的 `[overview.md](overview.md)` 若原样留在 DOM，浏览器按页面地址解析会落到站点静态根
 * （页面路由全在 hash 里，静态根没有这些文件）→ 404。这里只接管工作区相对路径：
 * 外部链接、页内锚点、站点绝对路径都不动，避免破坏已有的跳转语义。
 * `<img src>` 同样改写，否则 Markdown 里引用的工作区图片也会 404。
 *
 * 链接额外带 `data-workspace-file`：`Markdown` 组件据此把普通左键点击收进弹层；
 * `href` 仍是接口地址，Ctrl+点击 / 中键 / 脚本失效时按原方式打开。
 * 文本与图片以外的格式由宿主回「不支持预览」。
 */
export function rewriteWorkspaceFileLinks(host: HTMLElement, workspaceId: string): void {
  const rewrite = (element: Element, attribute: 'href' | 'src') => {
    const value = element.getAttribute(attribute)?.trim() ?? '';
    if (!value || value.startsWith('#')) return;

    // 已经是宿主文件接口的地址（模型用「取文件链接」工具拿到的那种）：
    // 只补弹层标记，不改写地址，避免把 Host 也一起改掉
    if (element instanceof HTMLAnchorElement) {
      const direct = readFileReadTarget(value);
      if (direct) {
        markAnchor(element, direct.path, direct.workspaceId);
        return;
      }
    }

    if (value.startsWith('/')) return;
    if (/^[a-z][a-z0-9+.-]*:/i.test(value)) return;

    const query = new URLSearchParams({ path: value });
    if (workspaceId) query.set('workspaceId', workspaceId);
    element.setAttribute(attribute, `/file/read?${query.toString()}`);

    if (element instanceof HTMLAnchorElement) {
      markAnchor(element, value, workspaceId);
    }
  };

  host.querySelectorAll('a[href]').forEach((anchor) => rewrite(anchor, 'href'));
  host.querySelectorAll('img[src]').forEach((image) => rewrite(image, 'src'));
}

/** 给链接打上页内预览标记：`href` 保持原值，作为 Ctrl+点击与新标签页的回退入口。 */
function markAnchor(anchor: HTMLAnchorElement, path: string, workspaceId?: string): void {
  anchor.setAttribute('target', '_blank');
  anchor.setAttribute('rel', 'noopener noreferrer');
  anchor.setAttribute('title', t('markdown.previewInDialog'));
  anchor.setAttribute('data-workspace-file', path);
  if (workspaceId) {
    anchor.setAttribute('data-workspace-id', workspaceId);
  }
}