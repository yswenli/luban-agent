import { createEffect, createSignal, onCleanup, Show } from 'solid-js';

import { ApiError } from '../lib/api';
import { filePreviewName, fileReadUrl, loadFilePreview, type FilePreviewContent } from '../lib/filePreview';
import { closeFilePreview, filePreview } from '../stores/filePreview';
import { Modal } from './Dialog';
import { Markdown } from './Markdown';

/**
 * 会话内文件链接的页内预览弹层。
 *
 * 内容只从宿主文件接口取：文本原样展示、Markdown 排版、图片可点击放大、
 * HTML 放进无脚本无同源权限的沙箱 iframe，避免工作区里被注入的页面读到本页凭据。
 */
export function FilePreviewDialog() {
  const [content, setContent] = createSignal<FilePreviewContent | null>(null);
  const [error, setError] = createSignal<string | null>(null);
  const [loading, setLoading] = createSignal(false);
  const [zoomed, setZoomed] = createSignal(false);

  let objectUrl: string | null = null;
  let sequence = 0;

  const releaseObjectUrl = () => {
    if (!objectUrl) return;
    URL.revokeObjectURL(objectUrl);
    objectUrl = null;
  };

  const kind = () => content()?.kind ?? null;
  const text = () => {
    const value = content();
    return value && (value.kind === 'markdown' || value.kind === 'text') ? value.text : '';
  };
  const url = () => {
    const value = content();
    return value && 'url' in value ? value.url : '';
  };

  createEffect(() => {
    const target = filePreview();
    sequence += 1;
    const requestId = sequence;
    releaseObjectUrl();
    setContent(null);
    setError(null);
    setZoomed(false);
    if (!target) {
      setLoading(false);
      return;
    }

    setLoading(true);
    void loadFilePreview(target)
      .then((value) => {
        // 连点多个链接时只认最后一次请求，先到的图片 blob 立即释放
        if (requestId !== sequence) {
          if ('url' in value) URL.revokeObjectURL(value.url);
          return;
        }
        if ('url' in value) objectUrl = value.url;
        setContent(value);
      })
      .catch((cause: unknown) => {
        if (requestId !== sequence) return;
        setError(cause instanceof ApiError ? cause.message : '预览失败，请稍后重试。');
      })
      .finally(() => {
        if (requestId === sequence) setLoading(false);
      });
  });

  onCleanup(releaseObjectUrl);

  return (
    <Show when={filePreview()}>
      {(target) => (
        <Modal
          title={filePreviewName(target().path)}
          subtitle={target().path}
          wide
          onClose={closeFilePreview}
          footer={
            <a class="btn ghost" href={fileReadUrl(target())} target="_blank" rel="noopener noreferrer">
              新标签页打开
            </a>
          }
        >
          <Show when={loading()}>
            <div class="file-preview-state">正在读取…</div>
          </Show>

          <Show when={error()}>{(message) => <div class="file-preview-state is-error">{message()}</div>}</Show>

          <Show when={kind() === 'markdown'}>
            <div class="file-preview-md">
              <Markdown text={text()} workspaceId={target().workspaceId} />
            </div>
          </Show>

          <Show when={kind() === 'text'}>
            <pre class="file-preview-text">{text()}</pre>
          </Show>

          <Show when={kind() === 'image'}>
            <img
              class="file-preview-image"
              classList={{ 'is-original': zoomed() }}
              src={url()}
              alt={filePreviewName(target().path)}
              title={zoomed() ? '点击恢复适应窗口' : '点击按原尺寸查看'}
              onClick={() => setZoomed(!zoomed())}
            />
          </Show>

          <Show when={kind() === 'video'}>
            <video
              class="file-preview-media"
              src={url()}
              controls
              autoplay={false}
              onError={() => {
                setContent(null);
                setError('浏览器无法播放这个视频（容器或编码不受支持）；请在本地打开，或点下方「新标签页打开」。');
              }}
            />
          </Show>

          <Show when={kind() === 'audio'}>
            <audio
              class="file-preview-audio"
              src={url()}
              controls
              onError={() => {
                setContent(null);
                setError('浏览器无法播放这个音频；请在本地打开，或点下方「新标签页打开」。');
              }}
            />
          </Show>

          <Show when={kind() === 'html'}>
            <iframe
              class="file-preview-html"
              sandbox=""
              src={url()}
              title={filePreviewName(target().path)}
            />
          </Show>
        </Modal>
      )}
    </Show>
  );
}