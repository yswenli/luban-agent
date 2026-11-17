import { get, post } from './api';
import type { PluginChangeResult, PluginList } from './types';

/**
 * 插件接口封装。
 *
 * 安装走与技能导入同一套约定：浏览器把 zip 读成 Base64，`post` 再包成 `{ payload }`。
 * 插件包上限 32MB，Base64 后约 43MB，仍在 SAEA 的请求体上限内。
 */
export const pluginsApi = {
  list: (): Promise<PluginList> => get<PluginList>('/plugin/list'),
  install: (body: { fileName: string; contentBase64: string }): Promise<PluginChangeResult> =>
    post<PluginChangeResult>('/plugin/install', body),
  upgrade: (body: { id: string; fileName: string; contentBase64: string }): Promise<PluginChangeResult> =>
    post<PluginChangeResult>('/plugin/upgrade', body),
  enable: (id: string): Promise<PluginChangeResult> => post<PluginChangeResult>('/plugin/enable', { id }),
  disable: (id: string): Promise<PluginChangeResult> => post<PluginChangeResult>('/plugin/disable', { id }),
  uninstall: (id: string): Promise<PluginChangeResult> => post<PluginChangeResult>('/plugin/uninstall', { id }),
};

/** 读文件为 Base64（不含 DataURL 前缀）。 */
export function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const text = String(reader.result ?? '');
      const index = text.indexOf(',');
      resolve(index >= 0 ? text.slice(index + 1) : text);
    };
    reader.onerror = () => reject(reader.error ?? new Error('读取文件失败'));
    reader.readAsDataURL(file);
  });
}

/** 插件状态的中文说明。 */
export function pluginStateLabel(state: string): string {
  switch (state) {
    case 'enabled':
      return '已启用';
    case 'disabled':
      return '已停用';
    case 'error':
      return '错误';
    default:
      return '已安装';
  }
}