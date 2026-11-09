import DOMPurify from 'dompurify';
import { marked } from 'marked';

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