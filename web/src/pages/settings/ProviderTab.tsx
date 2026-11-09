import { createEffect, createSignal, Show } from 'solid-js';

import { resetAgent, settingsApi } from '../../lib/settings';
import type { ProviderListItem } from '../../lib/types';
import { showToast } from '../../stores/app';
import { AreaField, FormActions, SettingsPane, TextField } from './Shared';

interface ProviderForm {
  name: string;
  apiKey: string;
  baseUrl: string;
  displayName: string;
  timeout: string;
  customModels: string[];
}

const empty = (): ProviderForm => ({
  name: '',
  apiKey: '',
  baseUrl: '',
  displayName: '',
  timeout: '',
  customModels: [],
});

/**
 * 供应商页：直连本机 `config.json`，与桌面端设置页同一存储。
 *
 * 密钥只写不读：列表接口返回掩码，表单留空表示沿用已有密钥。
 */
export function ProviderTab() {
  const [items, setItems] = createSignal<ProviderListItem[]>([]);
  const [active, setActive] = createSignal<string | null>(null);
  const [form, setForm] = createSignal<ProviderForm>(empty());
  const [busy, setBusy] = createSignal(false);

  const masked = () => items().find((item) => item.name === active())?.apiKeyMask ?? '';

  const open = (item: ProviderListItem) => {
    setActive(item.name);
    setForm({
      name: item.name,
      apiKey: '',
      baseUrl: item.baseUrl ?? '',
      displayName: item.displayName,
      timeout: item.networkTimeoutSeconds ? String(item.networkTimeoutSeconds) : '',
      customModels: item.customModels ?? [],
    });
  };

  const reload = async (keep?: string | null) => {
    try {
      const list = await settingsApi.listProviders();
      setItems(list);
      const next = keep ?? active();
      const matched = next ? list.find((item) => item.name === next) : undefined;
      if (matched) {
        open(matched);
      } else if (list.length > 0) {
        open(list[0]);
      } else {
        setActive(null);
        setForm(empty());
      }
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  createEffect(() => {
    void reload(null);
  });

  const patch = <K extends keyof ProviderForm>(key: K, value: ProviderForm[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  const save = async () => {
    if (!form().name.trim()) {
      showToast('供应商标识不能为空', 'error');
      return;
    }

    if (!active() && !form().apiKey.trim()) {
      showToast('新建供应商时 API Key 不能为空', 'error');
      return;
    }

    setBusy(true);
    try {
      const saved = await settingsApi.saveProvider({
        name: form().name.trim(),
        apiKey: form().apiKey.trim() || null,
        baseUrl: form().baseUrl.trim() || null,
        displayName: form().displayName.trim() || null,
        networkTimeoutSeconds: Number.parseInt(form().timeout, 10) || null,
        customModels: form().customModels,
      });
      await resetAgent();
      showToast('供应商已保存', 'success');
      await reload(saved?.name ?? form().name.trim());
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    const name = active();
    if (!name) return;
    if (!window.confirm(`删除供应商「${name}」？如果当前模型属于它，模型选择会一并清空。`)) return;

    setBusy(true);
    try {
      await settingsApi.deleteProvider(name);
      await resetAgent();
      showToast('供应商已删除', 'success');
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
      title="供应商"
      items={items().map((item) => item.name)}
      activeKey={active()}
      emptyText="还没有配置任何供应商。"
      createLabel="新建"
      busy={busy()}
      onSelect={(key) => {
        const item = items().find((entry) => entry.name === key);
        if (item) open(item);
      }}
      onCreate={() => {
        setActive(null);
        setForm(empty());
      }}
    >
      <div class="field-grid">
        <TextField
          label="标识"
          value={form().name}
          hint="小写；作为唯一键"
          onInput={(v) => patch('name', v)}
        />
        <TextField label="显示名" value={form().displayName} onInput={(v) => patch('displayName', v)} />
        <TextField
          label="API Key"
          type="password"
          value={form().apiKey}
          wide
          hint={active() ? `当前：${masked()}；留空表示不修改` : '新建时必填'}
          onInput={(v) => patch('apiKey', v)}
        />
        <TextField
          label="BaseUrl"
          value={form().baseUrl}
          wide
          placeholder="留空使用默认地址"
          onInput={(v) => patch('baseUrl', v)}
        />
        <TextField
          label="网络超时（秒）"
          type="number"
          value={form().timeout}
          hint="留空使用默认"
          onInput={(v) => patch('timeout', v)}
        />
        <AreaField
          label="自定义模型"
          value={form().customModels.join('\n')}
          rows={6}
          wide
          mono
          hint="一行一个；保存时按差值增删"
          onInput={(v) => patch('customModels', v.split(/\r?\n/).map((line) => line.trim()).filter(Boolean))}
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