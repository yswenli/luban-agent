import { createEffect, createSignal, Show } from 'solid-js';

import { resetAgent, settingsApi } from '../../lib/settings';
import type { SkillFile } from '../../lib/types';
import { showToast } from '../../stores/app';
import { AreaField, FormActions, SettingsPane, TextField } from './Shared';

const empty = (): SkillFile => ({ name: '', description: '', category: '', triggers: '', body: '' });

/**
 * 技能页：文件型条目（`SKILL.md`）。
 *
 * 与桌面端一致，不列内置与 config 自定义技能，也不提供启停；保存后按需重建 Agent。
 */
export function SkillTab(props: { scope: string }) {
  const [items, setItems] = createSignal<string[]>([]);
  const [active, setActive] = createSignal<string | null>(null);
  const [form, setForm] = createSignal<SkillFile>(empty());
  const [busy, setBusy] = createSignal(false);

  const reload = async (keep?: string | null) => {
    try {
      const list = await settingsApi.listSkills(props.scope);
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

  const open = async (key: string) => {
    try {
      const data = await settingsApi.readSkill(props.scope, key);
      setActive(key);
      setForm(data);
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

  const patch = (key: keyof SkillFile, value: string) => setForm((current) => ({ ...current, [key]: value }));

  const save = async () => {
    if (!form().name.trim()) {
      showToast('技能名不能为空', 'error');
      return;
    }

    setBusy(true);
    try {
      const result = await settingsApi.saveSkill({ scope: props.scope, originalId: active(), ...form() });
      await resetAgent();
      showToast('技能已保存', 'success');
      await reload(result?.key ?? form().name.trim());
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    const key = active();
    if (!key) return;
    if (!window.confirm(`删除技能「${key}」？该操作不可撤销。`)) return;

    setBusy(true);
    try {
      await settingsApi.deleteSkill(props.scope, key);
      await resetAgent();
      showToast('技能已删除', 'success');
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
      title="技能"
      items={items()}
      activeKey={active()}
      emptyText="该作用域下还没有文件型技能。"
      createLabel="新建"
      busy={busy()}
      onSelect={(key) => void open(key)}
      onCreate={() => {
        setActive(null);
        setForm(empty());
      }}
    >
      <div class="field-grid">
        <TextField label="名称" value={form().name} hint="同时作为目录名" onInput={(v) => patch('name', v)} />
        <TextField label="分类" value={form().category} onInput={(v) => patch('category', v)} />
        <TextField label="描述" value={form().description} wide onInput={(v) => patch('description', v)} />
        <TextField
          label="触发词"
          value={form().triggers}
          hint="逗号分隔"
          wide
          onInput={(v) => patch('triggers', v)}
        />
        <AreaField
          label="正文"
          value={form().body}
          rows={16}
          wide
          mono
          placeholder="Markdown 正文；写清适用场景与步骤。"
          onInput={(v) => patch('body', v)}
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