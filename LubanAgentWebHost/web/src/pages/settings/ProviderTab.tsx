import { createSignal, onMount, Show } from 'solid-js';

import { resetAgent, settingsApi } from '../../lib/settings';
import type { ProviderListItem, ProviderPreset } from '../../lib/types';
import { refreshProviders, showToast } from '../../stores/app';
import { AreaField, FormActions, SelectField, SettingsPane, TextField } from './Shared';
import { t } from '../../i18n';

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

/** 比较基址时忽略首尾空白、大小写与末尾斜杠，避免与预设默认值误判不一致。 */
const normalizeUrl = (url: string | null | undefined) => (url ?? '').trim().replace(/\/+$/, '').toLowerCase();

/**
 * 供应商页：直连本机 `config.json`，与桌面端设置页同一存储。
 *
 * 密钥只写不读：列表接口返回掩码，表单留空表示沿用已有密钥。
 * 顶部下拉列出后端预设（框架内置供应商 + 自定义）：选中内置项时标识与基址按预设固定，
 * 只有「自定义」或预设标明需自填端点的项（Azure / Ollama，以及无预设地址的 Claude / Gemini）才允许编辑基址。
 */
export function ProviderTab() {
  const [items, setItems] = createSignal<ProviderListItem[]>([]);
  const [presets, setPresets] = createSignal<ProviderPreset[]>([]);
  const [presetKey, setPresetKey] = createSignal('custom');
  const [active, setActive] = createSignal<string | null>(null);
  const [form, setForm] = createSignal<ProviderForm>(empty());
  const [busy, setBusy] = createSignal(false);

  const masked = () => items().find((item) => item.name === active())?.apiKeyMask ?? '';

  const customKey = () => presets().find((preset) => preset.isCustom)?.name ?? 'custom';

  const currentPreset = () => presets().find((preset) => preset.name === presetKey());

  /** 选中「自定义」（含预设尚未加载时）即可自由填写标识与基址。 */
  const isCustom = () => currentPreset()?.isCustom ?? true;

  const nameEditable = () => isCustom();

  const baseUrlEditable = () => isCustom() || (currentPreset()?.editableBaseUrl ?? true);

  /**
   * 标识与基址都对得上某个内置预设时视为内置项；基址已改成别的地址则按自定义处理，保证端点仍可编辑。
   * 预设本身没有默认地址的项（Claude / Gemini，端点天然由用户提供）只按标识匹配，避免其保存后回落成「自定义」。
   */
  const matchPreset = (name: string, baseUrl: string | null | undefined): string => {
    const key = name.trim().toLowerCase();
    const current = normalizeUrl(baseUrl);
    const hit = presets().find(
      (preset) =>
        !preset.isCustom &&
        preset.name.toLowerCase() === key &&
        (preset.baseUrl === '' || current === '' || current === normalizeUrl(preset.baseUrl)),
    );
    return hit?.name ?? customKey();
  };

  const open = (item: ProviderListItem) => {
    setActive(item.name);
    const preset = matchPreset(item.name, item.baseUrl);
    setPresetKey(preset);
    // 内置项的基址留空时按预设值回填，让表单直接展示实际使用的地址
    const presetBaseUrl = presets().find((entry) => entry.name === preset)?.baseUrl ?? '';
    const storedBaseUrl = (item.baseUrl ?? '').trim();
    setForm({
      name: item.name,
      apiKey: '',
      baseUrl: storedBaseUrl || (preset === customKey() ? '' : presetBaseUrl),
      // 列表的 displayName 在未设置时会回退成标识，这里反推回空串，避免改名后把旧标识固化成显示名
      displayName: item.displayName === item.name ? '' : item.displayName,
      timeout: item.networkTimeoutSeconds ? String(item.networkTimeoutSeconds) : '',
      customModels: item.customModels ?? [],
    });
  };

  const loadPresets = async () => {
    if (presets().length > 0) return;
    try {
      setPresets(await settingsApi.listProviderPresets());
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  const reload = async (keep?: string | null) => {
    try {
      await loadPresets();
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
        setPresetKey(customKey());
        setForm(empty());
      }
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  onMount(() => {
    void reload(null);
  });

  const patch = <K extends keyof ProviderForm>(key: K, value: ProviderForm[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  /** 选中内置预设时按预设预填标识与基址；选「自定义」只解除只读，保留当前输入。 */
  const selectPreset = (key: string) => {
    const preset = presets().find((entry) => entry.name === key);
    if (!preset) return;
    setPresetKey(preset.name);
    if (preset.isCustom) return;
    setForm((current) => ({ ...current, name: preset.name, baseUrl: preset.baseUrl }));
  };

  const baseUrlHint = () => {
    if (isCustom()) return undefined;
    const preset = currentPreset();
    if (!preset) return undefined;
    const head = preset.editableBaseUrl ? t('provider.endpointEditable') : t('provider.endpointFixed');
    const warning = (preset.warning ?? '').trim().replace(/\s+/g, ' ');
    return warning ? `${head}；${warning}` : head;
  };

  const save = async () => {
    const name = form().name.trim();
    if (!name) {
      showToast(t('provider.required'), 'error');
      return;
    }

    // 标识改动即重命名：沿用原供应商的密钥，不算新建
    const current = active();
    const renaming = current !== null && current !== name;
    if (renaming && items().some((item) => item.name === name)) {
      showToast(t('provider.exists', { name }), 'error');
      return;
    }
    if (current === null && items().some((item) => item.name === name)) {
      showToast(t('provider.existsEdit', { name }), 'error');
      return;
    }

    if (current === null && !form().apiKey.trim()) {
      showToast(t('provider.apiKeyRequired'), 'error');
      return;
    }

    setBusy(true);
    try {
      const saved = await settingsApi.saveProvider({
        name,
        originalName: renaming ? current : null,
        apiKey: form().apiKey.trim() || null,
        baseUrl: form().baseUrl.trim() || null,
        displayName: form().displayName.trim() || null,
        networkTimeoutSeconds: Number.parseInt(form().timeout, 10) || null,
        customModels: form().customModels,
      });
      await resetAgent();
      await refreshProviders();
      showToast(t('provider.saved'), 'success');
      await reload(saved?.name ?? name);
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    const name = active();
    if (!name) return;
    if (!window.confirm(t('provider.deleteConfirm', { name }))) return;

    setBusy(true);
    try {
      await settingsApi.deleteProvider(name);
      await resetAgent();
      await refreshProviders();
      showToast(t('provider.deleted'), 'success');
      setActive(null);
      setPresetKey(customKey());
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
      title={t('provider.title')}
      items={items().map((item) => item.name)}
      activeKey={active()}
      emptyText={t('provider.empty')}
      createLabel={t('common.new')}
      busy={busy()}
      onSelect={(key) => {
        const item = items().find((entry) => entry.name === key);
        if (item) open(item);
      }}
      onCreate={() => {
        setActive(null);
        setPresetKey(customKey());
        setForm(empty());
      }}
    >
      <div class="field-grid">
        {/* 预设异步加载：加载完成后再渲染下拉，避免选项后到时选中态与表单不一致；加载失败则退化为纯手填 */}
        <Show when={presets().length > 0}>
          <SelectField
            label={t('provider.preset')}
            wide
            value={presetKey()}
            options={presets().map((preset) => ({ value: preset.name, label: preset.displayName }))}
            hint={t('provider.presetHint')}
            onChange={selectPreset}
          />
        </Show>
        <TextField
          label={t('provider.id')}
          value={form().name}
          readOnly={!nameEditable()}
          hint={nameEditable() ? t('provider.idHintCustom') : t('provider.idHintBuiltin')}
          onInput={(v) => patch('name', v)}
        />
        <TextField label={t('account.displayName')} value={form().displayName} onInput={(v) => patch('displayName', v)} />
        <TextField
          label="API Key"
          type="password"
          value={form().apiKey}
          wide
          hint={active() ? t('provider.apiKeyCurrent', { mask: masked() }) : t('provider.apiKeyNew')}
          onInput={(v) => patch('apiKey', v)}
        />
        <TextField
          label="BaseUrl"
          value={form().baseUrl}
          wide
          readOnly={!baseUrlEditable()}
          placeholder={t('provider.baseUrlPlaceholder')}
          hint={baseUrlHint()}
          onInput={(v) => patch('baseUrl', v)}
        />
        <TextField
          label={t('provider.timeout')}
          type="number"
          value={form().timeout}
          hint={t('provider.timeoutHint')}
          onInput={(v) => patch('timeout', v)}
        />
        <AreaField
          label={t('provider.customModels')}
          value={form().customModels.join('\n')}
          rows={4}
          wide
          mono
          hint={t('provider.customModelsHint')}
          onInput={(v) => patch('customModels', v.split(/\r?\n/).map((line) => line.trim()).filter(Boolean))}
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