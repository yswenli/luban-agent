import { createMemo, createSignal, For, onCleanup, onMount, Show } from 'solid-js';

import { Icon } from '../../components/Icons';
import { fileToBase64, pluginStateLabel, pluginsApi } from '../../lib/plugins';
import type { PluginChangeResult, PluginItem } from '../../lib/types';
import { showToast } from '../../stores/app';
import {
  ensurePluginsLoaded,
  loading,
  plugins,
  refreshPlugins,
  rootPath,
} from '../../stores/plugins';
import { InstallPanel } from './InstallPanel';
import { PluginDetail } from './PluginDetail';
import { t } from '../../i18n';

/**
 * 插件管理浮层：左列插件列表，右列安装区 + 选中项详情。
 *
 * 骨架与设置页一致（`.settings-overlay` / `.settings-layout`），关闭后由 `router` 回到打开前的视图。
 */
export function PluginsOverlay(props: { onClose: () => void }) {
  const [selected, setSelected] = createSignal<string | null>(null);
  const [busy, setBusy] = createSignal(false);
  const [warnings, setWarnings] = createSignal<{ id: string; items: string[] }>({ id: '', items: [] });

  onMount(() => {
    void ensurePluginsLoaded();

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') props.onClose();
    };
    window.addEventListener('keydown', onKey);
    onCleanup(() => window.removeEventListener('keydown', onKey));
  });

  /** 选中项；未显式选中时回落列表第一项，列表刷新后不会出现「有数据但右侧空白」。 */
  const active = createMemo<PluginItem | null>(() => {
    const items = plugins();
    const id = selected();
    return (id ? items.find((item) => item.id === id) : undefined) ?? items[0] ?? null;
  });

  const warningsOf = (id: string) => (warnings().id === id ? warnings().items : []);

  /** 统一的「刷新列表 + 选中回填 + 提示」；安装 / 升级 / 启停 / 卸载共用。 */
  const applyResult = async (result: PluginChangeResult, okText: string) => {
    await refreshPlugins();
    setSelected(result.id || null);
    setWarnings({ id: result.id ?? '', items: result.warnings ?? [] });

    const tips = result.warnings ?? [];
    // 卸载后没有详情面板可看提示，直接把它带进 toast
    const message = tips.length === 0
      ? okText
      : result.plugin
        ? t('plugins.toast.withWarnings', { text: okText, count: tips.length })
        : t('plugins.toast.withFirstWarning', { text: okText, message: tips[0] });
    showToast(message, tips.length > 0 ? 'info' : 'success');
  };

  const run = async (task: () => Promise<PluginChangeResult>, okText: string) => {
    if (busy()) return;
    setBusy(true);
    try {
      await applyResult(await task(), okText);
    } catch (cause) {
      showToast((cause as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const upgrade = async (id: string, file: File) => {
    if (busy()) return;
    setBusy(true);
    try {
      const contentBase64 = await fileToBase64(file);
      await applyResult(
        await pluginsApi.upgrade({ id, fileName: file.name, contentBase64 }),
        t('plugins.toast.upgraded'),
      );
    } catch (cause) {
      showToast((cause as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div
      class="settings-overlay"
      onClick={(event) => {
        if (event.target === event.currentTarget) props.onClose();
      }}
    >
      <div class="settings-panel" role="dialog" aria-modal="true">
        <div class="settings-panel-head">
          <span class="dialog-title">{t('plugins.title')}</span>
          <span class="dialog-sub mono" title={rootPath()}>
            {rootPath() || t('common.loading')}
          </span>
          <div class="spacer" />
          <button class="icon-btn" title={t('plugins.refreshList')} disabled={loading()} onClick={() => void refreshPlugins()}>
            <Icon name="refresh" size={16} />
          </button>
          <button class="icon-btn" title={t('plugins.closeManager')} onClick={() => props.onClose()}>
            <Icon name="close" size={17} />
          </button>
        </div>

        <div class="settings-layout">
          <div class="settings-nav">
            <div class="settings-list-head">
              <span class="section-label">{t('plugins.installedCount', { count: plugins().length })}</span>
            </div>
            <Show
              when={plugins().length > 0}
              fallback={
                <div class="side-hint faint">
                  {t('plugins.emptyList')}
                </div>
              }
            >
              <For each={plugins()}>
                {(plugin) => (
                  <button
                    class="settings-item"
                    classList={{ active: active()?.id === plugin.id }}
                    onClick={() => setSelected(plugin.id)}
                  >
                    <span class="settings-item-name">{plugin.name || plugin.id}</span>
                    <span class="settings-item-sub">
                      v{plugin.version} · {pluginStateLabel(plugin.state)}
                    </span>
                  </button>
                )}
              </For>
            </Show>
          </div>

          <div class="settings-body">
            <InstallPanel
              busy={busy()}
              onBusy={setBusy}
              onInstalled={(result) => void applyResult(result, t('plugins.toast.installed'))}
            />

            <Show
              when={active()}
              fallback={<div class="settings-list-empty">{t('plugins.selectEmpty')}</div>}
            >
              {(plugin) => (
                <PluginDetail
                  plugin={plugin()}
                  warnings={warningsOf(plugin().id)}
                  busy={busy()}
                  onEnable={() => void run(() => pluginsApi.enable(plugin().id), t('plugins.toast.enabled'))}
                  onDisable={() => void run(() => pluginsApi.disable(plugin().id), t('plugins.toast.disabled'))}
                  onUninstall={() => void run(() => pluginsApi.uninstall(plugin().id), t('plugins.toast.uninstalled'))}
                  onUpgrade={(file) => void upgrade(plugin().id, file)}
                />
              )}
            </Show>
          </div>
        </div>
      </div>
    </div>
  );
}