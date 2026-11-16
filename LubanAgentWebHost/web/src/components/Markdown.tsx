import { createEffect, onCleanup, onMount } from 'solid-js';

import { renderMarkdown, rewriteWorkspaceFileLinks } from '../lib/markdown';
import { openFilePreview } from '../stores/filePreview';

/**
 * Markdown 渲染 + 代码块复制按钮（按钮在渲染后注入，避免扩大 DOMPurify 白名单）。
 *
 * 传 `workspaceId` 表示内容来自工作区会话：其中的相对链接与图片会改写到宿主文件接口，
 * 普通左键点击在页内弹层里预览（Ctrl+点击等仍走浏览器默认的新标签页）；
 * 不传则保持原样（帮助弹窗的 README 链接另有 GitHub 跳转逻辑）。
 */
export function Markdown(props: { text: string; workspaceId?: string }) {
  let host!: HTMLDivElement;

  onMount(() => {
    const onClick = (event: MouseEvent) => {
      // 只接管普通左键：带修饰键或中键仍按 href 新标签页打开
      if (event.defaultPrevented || event.button !== 0) return;
      if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;

      const anchor = (event.target as Element | null)?.closest('a[data-workspace-file]');
      const path = anchor?.getAttribute('data-workspace-file');
      if (!path) return;

      event.preventDefault();
      openFilePreview(path, anchor?.getAttribute('data-workspace-id') ?? props.workspaceId);
    };

    host.addEventListener('click', onClick);
    onCleanup(() => host.removeEventListener('click', onClick));
  });

  createEffect(() => {
    const workspaceId = props.workspaceId;
    host.innerHTML = renderMarkdown(props.text);
    if (workspaceId) rewriteWorkspaceFileLinks(host, workspaceId);

    host.querySelectorAll('pre').forEach((pre) => {
      if (pre.querySelector('.code-copy')) return;

      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'code-copy';
      button.textContent = '复制';
      button.addEventListener('click', () => {
        const code = pre.querySelector('code')?.textContent ?? '';
        void navigator.clipboard?.writeText(code).then(
          () => {
            button.textContent = '已复制';
            window.setTimeout(() => {
              button.textContent = '复制';
            }, 1200);
          },
          () => {
            button.textContent = '复制失败';
            window.setTimeout(() => {
              button.textContent = '复制';
            }, 1200);
          },
        );
      });

      pre.appendChild(button);
    });
  });

  return <div class="md" ref={(element) => (host = element)} />;
}