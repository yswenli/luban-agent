import type { JSX } from 'solid-js';
import { createSignal, onCleanup, onMount, Show } from 'solid-js';

import { Icon } from './Icons';

/** 通用弹窗外壳：遮罩点击 / Esc 关闭。 */
export function Modal(props: {
  title: string;
  subtitle?: string;
  children: JSX.Element;
  footer?: JSX.Element;
  onClose: () => void;
}) {
  onMount(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') props.onClose();
    };
    window.addEventListener('keydown', onKey);
    onCleanup(() => window.removeEventListener('keydown', onKey));
  });

  return (
    <div
      class="overlay"
      onClick={(event) => {
        if (event.target === event.currentTarget) props.onClose();
      }}
    >
      <div class="dialog" role="dialog" aria-modal="true">
        <div class="dialog-head">
          <div>
            <div class="dialog-title">{props.title}</div>
            <Show when={props.subtitle}>
              <div class="dialog-sub">{props.subtitle}</div>
            </Show>
          </div>
          <div class="spacer" />
          <button class="icon-btn" onClick={props.onClose} aria-label="关闭">
            <Icon name="close" />
          </button>
        </div>
        <div class="dialog-body">{props.children}</div>
        <Show when={props.footer}>
          <div class="dialog-foot">{props.footer}</div>
        </Show>
      </div>
    </div>
  );
}

/** 单行文本输入弹窗（重命名会话等）。 */
export function TextPromptDialog(props: {
  title: string;
  subtitle?: string;
  label?: string;
  initialValue: string;
  confirmText?: string;
  onConfirm: (value: string) => void;
  onClose: () => void;
}) {
  const [value, setValue] = createSignal(props.initialValue);
  let input!: HTMLInputElement;

  onMount(() => {
    window.setTimeout(() => {
      input?.focus();
      input?.select();
    }, 30);
  });

  const submit = () => {
    const text = value().trim();
    if (!text) return;
    props.onConfirm(text);
  };

  return (
    <Modal
      title={props.title}
      subtitle={props.subtitle}
      onClose={props.onClose}
      footer={
        <>
          <button class="btn ghost" onClick={props.onClose}>
            取消
          </button>
          <button class="btn primary" disabled={!value().trim()} onClick={submit}>
            {props.confirmText ?? '保存'}
          </button>
        </>
      }
    >
      <Show when={props.label}>
        <div class="section-label" style={{ 'margin-bottom': '6px' }}>
          {props.label}
        </div>
      </Show>
      <input
        ref={input}
        class="text-input"
        value={value()}
        onInput={(event) => setValue(event.currentTarget.value)}
        onKeyDown={(event) => {
          if (event.key === 'Enter') submit();
        }}
      />
    </Modal>
  );
}

/** 危险操作确认弹窗。 */
export function ConfirmActionDialog(props: {
  title: string;
  message: string;
  confirmText?: string;
  onConfirm: () => void;
  onClose: () => void;
}) {
  return (
    <Modal
      title={props.title}
      onClose={props.onClose}
      footer={
        <>
          <button class="btn ghost" onClick={props.onClose}>
            取消
          </button>
          <button class="btn danger" onClick={props.onConfirm}>
            {props.confirmText ?? '确认删除'}
          </button>
        </>
      }
    >
      <p style={{ margin: '0 0 6px' }}>{props.message}</p>
    </Modal>
  );
}