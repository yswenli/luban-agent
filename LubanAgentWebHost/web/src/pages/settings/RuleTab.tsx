import { createEffect, createSignal, Show } from 'solid-js';

import { resetAgent, settingsApi } from '../../lib/settings';
import type { RuleFile } from '../../lib/types';
import { showToast } from '../../stores/app';
import { AreaField, CheckField, FormActions, SelectField, SettingsPane, TextField } from './Shared';
import { t } from '../../i18n';

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
  /* 作用域切换或快速点选时，只让最后一次请求的结果落地。 */
  let loadSeq = 0;

  const open = async (key: string) => {
    const seq = ++loadSeq;
    try {
      const data = await settingsApi.readRule(props.scope, key);
      if (seq !== loadSeq) return;
      setActive(key);
      setForm({ ...empty(), ...data });
    } catch (error) {
      if (seq !== loadSeq) return;
      showToast((error as Error).message, 'error');
    }
  };

  const reload = async (keep?: string | null) => {
    const seq = ++loadSeq;
    try {
      const list = await settingsApi.listRules(props.scope);
      if (seq !== loadSeq) return;
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
      if (seq !== loadSeq) return;
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
      showToast(t('rule.idRequired'), 'error');
      return;
    }

    setBusy(true);
    try {
      const result = await settingsApi.saveRule({ scope: props.scope, originalId: active(), ...form() });
      await resetAgent();
      showToast(t('rule.saved'), 'success');
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
    if (!window.confirm(t('rule.deleteConfirm', { name: key }))) return;

    setBusy(true);
    try {
      await settingsApi.deleteRule(props.scope, key);
      await resetAgent();
      showToast(t('rule.deleted'), 'success');
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
      title={t('rule.title')}
      items={items()}
      activeKey={active()}
      emptyText={t('rule.empty')}
      createLabel={t('common.new')}
      busy={busy()}
      onSelect={(key) => void open(key)}
      onCreate={() => {
        setActive(null);
        setForm(empty());
      }}
    >
      <div class="field-grid">
        <TextField label="Id" value={form().id} hint={t('mcp.nameHint')} onInput={(v) => patch('id', v)} />
        <TextField label={t('common.name')} value={form().name} onInput={(v) => patch('name', v)} />
        <TextField label={t('common.description')} value={form().description} wide onInput={(v) => patch('description', v)} />
        <TextField label={t('rule.actionTypePattern')} value={form().actionTypePattern} onInput={(v) => patch('actionTypePattern', v)} />
        <TextField label={t('rule.targetPattern')} value={form().targetPattern} onInput={(v) => patch('targetPattern', v)} />
        <SelectField
          label={t('rule.action')}
          value={form().action}
          options={[
            { value: 'allow', label: t('rule.actionAllow') },
            { value: 'deny', label: t('rule.actionDeny') },
          ]}
          onChange={(v) => patch('action', v)}
        />
        <TextField
          label={t('rule.priority')}
          type="number"
          value={String(form().priority)}
          hint={t('rule.priorityHint')}
          onInput={(v) => patch('priority', Number.parseInt(v, 10) || 0)}
        />
        <CheckField label={t('common.enabled')} checked={form().enabled} onChange={(v) => patch('enabled', v)} />
        <AreaField
          label={t('rule.content')}
          value={form().content}
          rows={8}
          wide
          mono
          placeholder={t('rule.contentPlaceholder')}
          onInput={(v) => patch('content', v)}
        />
      </div>
      <FormActions>
        <button class="btn primary" onClick={() => void save()} disabled={busy()}>
          {t('common.save')}
        </button>
        <Show when={active()}>
          <button class="btn danger" onClick={() => void remove()} disabled={busy()}>
            {t('common.delete')}
          </button>
        </Show>
      </FormActions>
    </SettingsPane>
  );
}