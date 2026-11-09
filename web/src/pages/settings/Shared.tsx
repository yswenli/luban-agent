import type { JSX } from 'solid-js';
import { For, Show } from 'solid-js';

import { Icon } from '../../components/Icons';

/** 设置页三栏骨架：左列表 + 右表单。 */
export function SettingsPane(props: {
  title: string;
  hint?: string;
  items: string[];
  activeKey: string | null;
  emptyText: string;
  createLabel?: string;
  busy?: boolean;
  onSelect: (key: string) => void;
  onCreate?: () => void;
  children: JSX.Element;
}) {
  return (
    <div class="settings-pane">
      <div class="settings-list">
        <div class="settings-list-head">
          <span class="section-label">{props.title}</span>
          <span class="spacer" />
          <Show when={props.createLabel}>
            <button class="btn ghost" onClick={() => props.onCreate?.()} disabled={props.busy}>
              <Icon name="plus" size={14} />
              {props.createLabel}
            </button>
          </Show>
        </div>
        <Show
          when={props.items.length > 0}
          fallback={<div class="settings-list-empty">{props.emptyText}</div>}
        >
          <For each={props.items}>
            {(key) => (
              <button
                class="settings-item"
                classList={{ active: key === props.activeKey }}
                onClick={() => props.onSelect(key)}
              >
                {key}
              </button>
            )}
          </For>
        </Show>
      </div>
      <div class="settings-form">{props.children}</div>
    </div>
  );
}

/** 表单项外壳。 */
export function Field(props: { label: string; hint?: string; wide?: boolean; children: JSX.Element }) {
  return (
    <label class="field" classList={{ wide: props.wide }}>
      <span class="field-label">{props.label}</span>
      {props.children}
      <Show when={props.hint}>
        <span class="field-hint">{props.hint}</span>
      </Show>
    </label>
  );
}

/** 单行文本 / 密码输入。 */
export function TextField(props: {
  label: string;
  value: string;
  hint?: string;
  placeholder?: string;
  type?: 'text' | 'password' | 'number';
  wide?: boolean;
  onInput: (value: string) => void;
}) {
  return (
    <Field label={props.label} hint={props.hint} wide={props.wide}>
      <input
        class="text-input"
        type={props.type ?? 'text'}
        value={props.value}
        placeholder={props.placeholder}
        onInput={(event) => props.onInput(event.currentTarget.value)}
      />
    </Field>
  );
}

/** 多行文本。 */
export function AreaField(props: {
  label: string;
  value: string;
  hint?: string;
  placeholder?: string;
  rows?: number;
  wide?: boolean;
  mono?: boolean;
  onInput: (value: string) => void;
}) {
  return (
    <Field label={props.label} hint={props.hint} wide={props.wide}>
      <textarea
        class="text-area"
        classList={{ mono: props.mono }}
        rows={props.rows ?? 4}
        value={props.value}
        placeholder={props.placeholder}
        onInput={(event) => props.onInput(event.currentTarget.value)}
      />
    </Field>
  );
}

/** 复选。 */
export function CheckField(props: { label: string; checked: boolean; hint?: string; onChange: (value: boolean) => void }) {
  return (
    <label class="check-field">
      <input type="checkbox" checked={props.checked} onChange={(event) => props.onChange(event.currentTarget.checked)} />
      <span>{props.label}</span>
      <Show when={props.hint}>
        <span class="field-hint">{props.hint}</span>
      </Show>
    </label>
  );
}

/** 下拉选择。 */
export function SelectField(props: {
  label: string;
  value: string;
  options: { value: string; label: string }[];
  hint?: string;
  wide?: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <Field label={props.label} hint={props.hint} wide={props.wide}>
      <select class="text-input" value={props.value} onChange={(event) => props.onChange(event.currentTarget.value)}>
        <For each={props.options}>{(item) => <option value={item.value}>{item.label}</option>}</For>
      </select>
    </Field>
  );
}

/** 表单底部动作条。 */
export function FormActions(props: { children: JSX.Element }) {
  return <div class="settings-actions">{props.children}</div>;
}