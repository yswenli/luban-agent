import { createMemo, createSignal } from 'solid-js';

import { pluginsApi } from '../lib/plugins';
import type { PluginItem } from '../lib/types';
import { showToast } from './app';

const [plugins, setPlugins] = createSignal<PluginItem[]>([]);
const [rootPath, setRootPath] = createSignal('');
const [loading, setLoading] = createSignal(false);
const [loaded, setLoaded] = createSignal(false);

export { plugins, rootPath, loading };

/** 是否已完成过一次列表加载；插件页面据此区分「还没拉到列表」与「确实没有该插件」。 */
export const pluginsLoaded = loaded;

/**
 * 重新拉取插件列表。
 *
 * 侧栏的插件页面入口从这份列表派生，安装 / 启停 / 卸载后必须刷新；
 * 失败只提示不抛，避免打断当前页面。
 */
export async function refreshPlugins(): Promise<void> {
  setLoading(true);
  try {
    const data = await pluginsApi.list();
    setPlugins(data?.items ?? []);
    setRootPath(data?.rootPath ?? '');
    setLoaded(true);
  } catch (error) {
    showToast((error as Error).message, 'error');
  } finally {
    setLoading(false);
  }
}

/** 首次运行或宿主重启后加载一次；已加载过则不重复请求。 */
export async function ensurePluginsLoaded(): Promise<void> {
  if (loaded()) return;
  await refreshPlugins();
}

/**
 * 侧栏的插件页面入口：只取「已启用且页面可用」，按插件与条目顺序排列。
 *
 * 每个条目携带插件标识与条目标识，点击即打开 `#/plugin/{pluginId}/{entryId}`。
 */
export const pluginPageEntries = createMemo(() =>
  plugins()
    .filter((plugin) => plugin.state === 'enabled')
    .flatMap((plugin) =>
      plugin.pages
        .filter((page) => !!page.url)
        .map((page) => ({
          pluginId: plugin.id,
          entryId: page.entryId,
          label: page.label || plugin.name || page.entryId,
          icon: page.icon ?? null,
        })),
    ),
);

/** 按插件标识取当前列表里的那一项（详情页与插件页共用）。 */
export function findPlugin(pluginId: string): PluginItem | null {
  return plugins().find((plugin) => plugin.id === pluginId) ?? null;
}