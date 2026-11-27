import { createEffect, createSignal, Show } from 'solid-js';

import { resetAgent, settingsApi } from '../../lib/settings';
import type { McpFile } from '../../lib/types';
import { showToast } from '../../stores/app';
import { AreaField, CheckField, FormActions, SelectField, SettingsPane, TextField } from './Shared';
import { t } from '../../i18n';

const empty = (): McpFile => ({
  name: '',
  description: '',
  transport: 'stdio',
  target: '',
  args: [],
  enabled: true,
});

/** MCP 页：文件型条目（`mcps/<name>.json`）；不提供连接测试与工具列举。 */
export function McpTab(props: { scope: string }) {
  const [items, setItems] = createSignal<string[]>([]);
  const [active, setActive] = createSignal<string | null>(null);
  const [form, setForm] = createSignal<McpFile>(empty());
  const [busy, setBusy] = createSignal(false);
  /* 作用域切换或快速点选时，只让最后一次请求的结果落地。 */
  let loadSeq = 0;

  const open = async (key: string) => {
    const seq = ++loadSeq;
    try {
      const data = await settingsApi.readMcp(props.scope, key);
      if (seq !== loadSeq) return;
      setActive(key);
      setForm({ ...empty(), ...data, args: data.args ?? [] });
    } catch (error) {
      if (seq !== loadSeq) return;
      showToast((error as Error).message, 'error');
    }
  };

  const reload = async (keep?: string | null) => {
    const seq = ++loadSeq;
    try {
      const list = await settingsApi.listMcps(props.scope);
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

  const patch = <K extends keyof McpFile>(key: K, value: McpFile[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  const save = async () => {
    if (!form().name.trim()) {
      showToast(t('mcp.nameRequired'), 'error');
      return;
    }

    setBusy(true);
    try {
      const result = await settingsApi.saveMcp({ scope: props.scope, originalId: active(), ...form() });
      await resetAgent();
      showToast(t('mcp.saved'), 'success');
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
    if (!window.confirm(t('mcp.deleteConfirm', { name: key }))) return;

    setBusy(true);
    try {
      await settingsApi.deleteMcp(props.scope, key);
      await resetAgent();
      showToast(t('mcp.deleted'), 'success');
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
      title={t('mcp.title')}
      items={items()}
      activeKey={active()}
      emptyText={t('mcp.empty')}
      createLabel={t('common.new')}
      busy={busy()}
      onSelect={(key) => void open(key)}
      onCreate={() => {
        setActive(null);
        setForm(empty());
      }}
    >
      <div class="field-grid">
        <TextField label={t('common.name')} value={form().name} hint={t('mcp.nameHint')} onInput={(v) => patch('name', v)} />
        <TextField label={t('common.description')} value={form().description} onInput={(v) => patch('description', v)} />
        <SelectField
          label={t('mcp.transport')}
          value={form().transport}
          options={[
            { value: 'stdio', label: t('mcp.transportStdio') },
            { value: 'http', label: t('mcp.transportHttp') },
            { value: 'sse', label: t('mcp.transportSse') },
          ]}
          onChange={(v) => patch('transport', v)}
        />
        <TextField
          label={form().transport === 'stdio' ? t('mcp.startCommand') : t('mcp.serverUrl')}
          value={form().target}
          wide
          placeholder={form().transport === 'stdio' ? t('mcp.startPlaceholder') : t('mcp.urlPlaceholder')}
          onInput={(v) => patch('target', v)}
        />
        <Show when={form().transport === 'stdio'}>
          <AreaField
            label={t('mcp.args')}
            value={form().args.join('\n')}
            rows={4}
            wide
            mono
            hint={t('mcp.argsHint')}
            onInput={(v) => patch('args', v.split(/\r?\n/).map((line) => line.trim()).filter(Boolean))}
          />
        </Show>
        <CheckField label={t('common.enabled')} checked={form().enabled} onChange={(v) => patch('enabled', v)} />
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