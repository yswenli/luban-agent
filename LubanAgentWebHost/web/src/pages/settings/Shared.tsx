import type { JSX } from 'solid-js';
import { For, Show } from 'solid-js';

import { Icon } from '../../components/Icons';
import { t } from '../../i18n';

/** 设置页左列表的条目：`label` 为主文本，`sub` 为次要小字，`group` 相同的相邻条目会归到同一分组标题下。 */
export interface SettingsEntry {
  key: string;
  label: string;
  sub?: string;
  group?: string;
}

/** 设置页三栏骨架：左列表 + 右表单。 */
export function SettingsPane(props: {
  title: string;
  hint?: string;
  items?: string[];
  /** 分组条目；提供时优先于 `items`。 */
  entries?: SettingsEntry[];
  activeKey: string | null;
  emptyText: string;
  createLabel?: string;
  /** 列表上方的额外动作（如导入按钮），与「新建」并排显示在列表外，列表宽度不会挤压按钮。 */
  headActions?: JSX.Element;
  busy?: boolean;
  onSelect: (key: string) => void;
  onCreate?: () => void;
  /** 条目右侧的删除动作；提供时列表项渲染删除图标。 */
  onDelete?: (key: string) => void;
  children: JSX.Element;
}) {
  const count = () => props.entries?.length ?? props.items?.length ?? 0;

  return (
    <div class="settings-pane">
      <div class="settings-list-col">
        <Show when={props.createLabel || props.headActions}>
          <div class="settings-list-toolbar">
            <Show when={props.createLabel}>
              <button class="btn ghost" onClick={() => props.onCreate?.()} disabled={props.busy}>
                <Icon name="plus" size={14} />
                {props.createLabel}
              </button>
            </Show>
            {props.headActions}
          </div>
        </Show>
        <div class="settings-list">
          <div class="settings-list-head">
            <span class="section-label">{props.title}</span>
          </div>
          <Show when={count() > 0} fallback={<div class="settings-list-empty">{props.emptyText}</div>}>
            <Show
              when={props.entries}
              fallback={
                <For each={props.items ?? []}>
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
              }
            >
              <For each={props.entries!}>
                {(entry, index) => {
                  const previous = () => (index() > 0 ? props.entries![index() - 1] : undefined);
                  const item = () => (
                    <button
                      class="settings-item"
                      classList={{ active: entry.key === props.activeKey }}
                      onClick={() => props.onSelect(entry.key)}
                    >
                      <span class="settings-item-name">{entry.label}</span>
                      <Show when={entry.sub}>
                        <span class="settings-item-sub">{entry.sub}</span>
                      </Show>
                    </button>
                  );
                  return (
                    <>
                      <Show when={entry.group && entry.group !== previous()?.group}>
                        <div class="settings-group">{entry.group}</div>
                      </Show>
                      <Show when={props.onDelete} fallback={item()}>
                        <div class="settings-item-row" classList={{ active: entry.key === props.activeKey }}>
                          {item()}
                          <button
                            class="settings-item-delete"
                            title={t('common.deleteNamed', { name: entry.label })}
                            disabled={props.busy}
                            onClick={() => props.onDelete?.(entry.key)}
                          >
                            <Icon name="trash" size={14} />
                          </button>
                        </div>
                      </Show>
                    </>
                  );
                }}
              </For>
            </Show>
          </Show>
        </div>
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

/** 单行文本 / 密码输入；`readOnly` 为 true 时只读但可选中复制。 */
export function TextField(props: {
  label: string;
  value: string;
  hint?: string;
  placeholder?: string;
  type?: 'text' | 'password' | 'number';
  wide?: boolean;
  readOnly?: boolean;
  /** 浏览器自动填充提示；显示名一类非凭据字段传 `off`，避免密码管理器把口令填进来。 */
  autocomplete?: string;
  onInput: (value: string) => void;
}) {
  return (
    <Field label={props.label} hint={props.hint} wide={props.wide}>
      <input
        class="text-input"
        type={props.type ?? 'text'}
        value={props.value}
        placeholder={props.placeholder}
        autocomplete={props.autocomplete}
        readOnly={props.readOnly}
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