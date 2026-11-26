import { Show } from 'solid-js';

import { fileDownloadUrl, filePreviewName, type FilePreviewContent } from '../lib/filePreview';
import { showToast } from '../stores/app';

import { Icon } from './Icons';

/** 有「渲染态 / 原文」两种呈现的文本类型；纯文本本来即源码，不需要切换。 */
export function hasSourceView(kind: FilePreviewContent['kind'] | null): boolean {
  return kind === 'markdown' || kind === 'html';
}

/** 可复制的正文：Markdown、纯文本与 HTML 原文；图片、音视频没有正文。 */
export function previewPlainText(content: FilePreviewContent | null): string {
  return content && (content.kind === 'markdown' || content.kind === 'text' || content.kind === 'html')
    ? content.text
    : '';
}

/**
 * 写入剪贴板。
 *
 * 宿主常以 `http://<内网地址>` 访问，属于非安全上下文，`navigator.clipboard` 在多数浏览器里直接是
 * `undefined`；因此必须有 `execCommand('copy')` 兜底，否则按钮点了毫无反应。
 */
async function writeClipboard(value: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(value);
      return true;
    }
  } catch {
    // 权限被拒或非安全上下文：继续走下方兜底
  }

  try {
    const area = document.createElement('textarea');
    area.value = value;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.top = '-1000px';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    const copied = document.execCommand('copy');
    area.remove();
    return copied;
  } catch {
    return false;
  }
}

/**
 * 文件预览的通用操作区：查看源码 / 复制 / 下载。
 *
 * 工作区面板与会话文件弹层共用，避免两处行为（尤其复制与下载地址）分叉。
 */
export function FilePreviewActions(props: {
  path: string;
  workspaceId?: string;
  content: FilePreviewContent | null;
  showingSource: boolean;
  onToggleSource: () => void;
}) {
  const plainText = () => previewPlainText(props.content);

  const copy = async () => {
    const text = plainText();
    if (!(await writeClipboard(text || props.path))) {
      showToast('复制失败：当前页面不允许写入剪贴板', 'error');
      return;
    }
    showToast(text ? '已复制文件内容' : '已复制文件路径', 'success');
  };

  return (
    <div class="file-preview-actions">
      <Show when={hasSourceView(props.content?.kind ?? null)}>
        <button
          class="icon-btn"
          classList={{ active: props.showingSource }}
          title={props.showingSource ? '返回预览' : '查看源码'}
          aria-pressed={props.showingSource}
          onClick={() => props.onToggleSource()}
        >
          <Icon name="code" size={16} />
        </button>
      </Show>

      <button class="icon-btn" title={plainText() ? '复制文件内容' : '复制文件路径'} onClick={() => void copy()}>
        <Icon name="copy" size={16} />
      </button>

      <a
        class="icon-btn"
        href={fileDownloadUrl({ path: props.path, workspaceId: props.workspaceId })}
        download={filePreviewName(props.path)}
        title="下载"
      >
        <Icon name="download" size={16} />
      </a>
    </div>
  );
}