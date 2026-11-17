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
        ? `${okText}（${tips.length} 条提示，见详情）`
        : `${okText}：${tips[0]}`;
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
      await applyResult(await pluginsApi.upgrade({ id, fileName: file.name, contentBase64 }), '升级完成');
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
          <span class="dialog-title">插件</span>
          <span class="dialog-sub mono" title={rootPath()}>
            {rootPath() || '加载中…'}
          </span>
          <div class="spacer" />
          <button class="icon-btn" title="刷新插件列表" disabled={loading()} onClick={() => void refreshPlugins()}>
            <Icon name="refresh" size={16} />
          </button>
          <button class="icon-btn" title="关闭插件管理（Esc）" onClick={() => props.onClose()}>
            <Icon name="close" size={17} />
          </button>
        </div>

        <div class="settings-layout">
          <div class="settings-nav">
            <div class="settings-list-head">
              <span class="section-label">已安装（{plugins().length}）</span>
            </div>
            <Show
              when={plugins().length > 0}
              fallback={
                <div class="side-hint faint">
                  还没有插件。Web 端安装的插件与桌面端分开存放，点右侧「安装插件」上传 zip 包。
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
              onInstalled={(result) => void applyResult(result, '插件已安装并启用')}
            />

            <Show
              when={active()}
              fallback={<div class="settings-list-empty">选择左侧插件查看详情，或先安装一个。</div>}
            >
              {(plugin) => (
                <PluginDetail
                  plugin={plugin()}
                  warnings={warningsOf(plugin().id)}
                  busy={busy()}
                  onEnable={() => void run(() => pluginsApi.enable(plugin().id), '已启用')}
                  onDisable={() => void run(() => pluginsApi.disable(plugin().id), '已停用')}
                  onUninstall={() => void run(() => pluginsApi.uninstall(plugin().id), '已卸载')}
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