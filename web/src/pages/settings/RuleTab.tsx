import { createEffect, createSignal, Show } from 'solid-js';

import { resetAgent, settingsApi } from '../../lib/settings';
import type { RuleFile } from '../../lib/types';
import { showToast } from '../../stores/app';
import { AreaField, CheckField, FormActions, SelectField, SettingsPane, TextField } from './Shared';

const empty = (): RuleFile => ({
  id: '',
  name: '',
  description: '',
  actionTypePattern: '*',
  targetPattern: '*',
  action: 'allow',
  priority: 100,
  enabled: true,
  content: '',
});

/** 规则页：文件型条目（`rules/<id>.json`）。 */
export function RuleTab(props: { scope: string }) {
  const [items, setItems] = createSignal<string[]>([]);
  const [active, setActive] = createSignal<string | null>(null);
  const [form, setForm] = createSignal<RuleFile>(empty());
  const [busy, setBusy] = createSignal(false);

  const open = async (key: string) => {
    try {
      const data = await settingsApi.readRule(props.scope, key);
      setActive(key);
      setForm({ ...empty(), ...data });
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  const reload = async (keep?: string | null) => {
    try {
      const list = await settingsApi.listRules(props.scope);
      setItems(list);
      const next = keep ?? active();
      if (next && list.includes(next)) {
        await open(next);
      } else if (list.length > 0) {
        await open(list[0]);
      } else {
        setActive(null);
        setForm(empty());
      }
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  createEffect(() => {
    props.scope;
    setActive(null);
    setForm(empty());
    void reload(null);
  });

  const patch = <K extends keyof RuleFile>(key: K, value: RuleFile[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  const save = async () => {
    if (!form().id.trim()) {
      showToast('规则 Id 不能为空', 'error');
      return;
    }

    setBusy(true);
    try {
      const result = await settingsApi.saveRule({ scope: props.scope, originalId: active(), ...form() });
      await resetAgent();
      showToast('规则已保存', 'success');
      await reload(result?.key ?? form().id.trim());
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    const key = active();
    if (!key) return;
    if (!window.confirm(`删除规则「${key}」？该操作不可撤销。`)) return;

    setBusy(true);
    try {
      await settingsApi.deleteRule(props.scope, key);
      await resetAgent();
      showToast('规则已删除', 'success');
      setActive(null);
      setForm(empty());
      await reload(null);
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <SettingsPane
      title="规则"
      items={items()}
      activeKey={active()}
      emptyText="该作用域下还没有文件型规则。"
      createLabel="新建"
      busy={busy()}
      onSelect={(key) => void open(key)}
      onCreate={() => {
        setActive(null);
        setForm(empty());
      }}
    >
      <div class="field-grid">
        <TextField label="Id" value={form().id} hint="同时作为文件名" onInput={(v) => patch('id', v)} />
        <TextField label="名称" value={form().name} onInput={(v) => patch('name', v)} />
        <TextField label="描述" value={form().description} wide onInput={(v) => patch('description', v)} />
        <TextField label="动作类型通配" value={form().actionTypePattern} onInput={(v) => patch('actionTypePattern', v)} />
        <TextField label="目标通配" value={form().targetPattern} onInput={(v) => patch('targetPattern', v)} />
        <SelectField
          label="动作"
          value={form().action}
          options={[
            { value: 'allow', label: 'allow（放行）' },
            { value: 'deny', label: 'deny（拒绝）' },
          ]}
          onChange={(v) => patch('action', v)}
        />
        <TextField
          label="优先级"
          type="number"
          value={String(form().priority)}
          hint="数字越大优先级越高"
          onInput={(v) => patch('priority', Number.parseInt(v, 10) || 0)}
        />
        <CheckField label="启用" checked={form().enabled} onChange={(v) => patch('enabled', v)} />
        <AreaField
          label="引导正文"
          value={form().content}
          rows={10}
          wide
          mono
          placeholder="命中规则后追加给模型的引导文本；可留空。"
          onInput={(v) => patch('content', v)}
        />
      </div>
      <FormActions>
        <button class="btn primary" onClick={() => void save()} disabled={busy()}>
          保存
        </button>
        <Show when={active()}>
          <button class="btn danger" onClick={() => void remove()} disabled={busy()}>
            删除
          </button>
        </Show>
      </FormActions>
    </SettingsPane>
  );
}