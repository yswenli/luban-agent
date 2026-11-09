import { createEffect } from 'solid-js';

import { renderMarkdown } from '../lib/markdown';

/** Markdown 渲染 + 代码块复制按钮（按钮在渲染后注入，避免扩大 DOMPurify 白名单）。 */
export function Markdown(props: { text: string }) {
  let host!: HTMLDivElement;

  createEffect(() => {
    host.innerHTML = renderMarkdown(props.text);

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