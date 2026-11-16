import { createSignal } from 'solid-js';

import type { FilePreviewTarget } from '../lib/filePreview';

const [filePreview, setFilePreview] = createSignal<FilePreviewTarget | null>(null);

export { filePreview };

/**
 * 在页内弹层里打开工作区文件。
 *
 * 会话里的相对链接与弹层内 Markdown 的嵌套链接都走这里，逐层点进去不离开当前页。
 */
export function openFilePreview(path: string, workspaceId?: string): void {
  const value = path.trim();
  if (!value) return;
  setFilePreview({ path: value, workspaceId });
}

export function closeFilePreview(): void {
  setFilePreview(null);
}