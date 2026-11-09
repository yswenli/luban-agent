import { createEffect, createSignal, Show } from 'solid-js';

import { resetAgent, settingsApi } from '../../lib/settings';
import type { McpFile } from '../../lib/types';
import { showToast } from '../../stores/app';
import { AreaField, CheckField, FormActions, SelectField, SettingsPane, TextField } from './Shared';

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

  const open = async (key: string) => {
    try {
      const data = await settingsApi.readMcp(props.scope, key);
      setActive(key);
      setForm({ ...empty(), ...data, args: data.args ?? [] });
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  const reload = async (keep?: string | null) => {
    try {
      const list = await settingsApi.listMcps(props.scope);
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

  const patch = <K extends keyof McpFile>(key: K, value: McpFile[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  const save = async () => {
    if (!form().name.trim()) {
      showToast('服务名不能为空', 'error');
      return;
    }

    setBusy(true);
    try {
      const result = await settingsApi.saveMcp({ scope: props.scope, originalId: active(), ...form() });
      await resetAgent();
      showToast('MCP 配置已保存', 'success');
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
    if (!window.confirm(`删除 MCP 配置「${key}」？该操作不可撤销。`)) return;

    setBusy(true);
    try {
      await settingsApi.deleteMcp(props.scope, key);
      await resetAgent();
      showToast('MCP 配置已删除', 'success');
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
      title="MCP 服务"
      items={items()}
      activeKey={active()}
      emptyText="该作用域下还没有文件型 MCP 配置。"
      createLabel="新建"
      busy={busy()}
      onSelect={(key) => void open(key)}
      onCreate={() => {
        setActive(null);
        setForm(empty());
      }}
    >
      <div class="field-grid">
        <TextField label="名称" value={form().name} hint="同时作为文件名" onInput={(v) => patch('name', v)} />
        <TextField label="描述" value={form().description} onInput={(v) => patch('description', v)} />
        <SelectField
          label="传输方式"
          value={form().transport}
          options={[
            { value: 'stdio', label: 'stdio（本地进程）' },
            { value: 'http', label: 'http（Streamable HTTP）' },
            { value: 'sse', label: 'sse（旧版 HTTP+SSE）' },
          ]}
          onChange={(v) => patch('transport', v)}
        />
        <TextField
          label={form().transport === 'stdio' ? '启动命令' : '服务地址'}
          value={form().target}
          wide
          placeholder={form().transport === 'stdio' ? '如 npx -y @modelcontextprotocol/server-filesystem' : '如 http://127.0.0.1:3000/mcp'}
          onInput={(v) => patch('target', v)}
        />
        <Show when={form().transport === 'stdio'}>
          <AreaField
            label="启动参数"
            value={form().args.join('\n')}
            rows={5}
            wide
            mono
            hint="一行一个参数"
            onInput={(v) => patch('args', v.split(/\r?\n/).map((line) => line.trim()).filter(Boolean))}
          />
        </Show>
        <CheckField label="启用" checked={form().enabled} onChange={(v) => patch('enabled', v)} />
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