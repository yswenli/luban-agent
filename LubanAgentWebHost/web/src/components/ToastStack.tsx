import { For } from 'solid-js';

import { toasts } from '../stores/app';

/** 右下角提示；只读 store，不接收 props。 */
export function ToastStack() {
  return (
    <div class="toast-stack" aria-live="polite">
      <For each={toasts()}>{(toast) => <div class={`toast ${toast.kind}`}>{toast.text}</div>}</For>
    </div>
  );
}