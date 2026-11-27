import { createSignal, Show } from 'solid-js';

import { Icon } from '../../components/Icons';
import { fileToBase64, pluginsApi } from '../../lib/plugins';
import type { PluginChangeResult } from '../../lib/types';
import { showToast } from '../../stores/app';
import { t } from '../../i18n';

/** 单个插件包上限，与服务端校验一致；超限直接在前端拦掉，不浪费一次上传。 */
const MAX_PACKAGE_BYTES = 32 * 1024 * 1024;

const packageTree = () => t('plugins.install.tree');

const manifestSample = () => t('plugins.install.manifest');

/**
 * 安装插件：选 zip → Base64 → `/plugin/install`。
 *
 * 说明区必须随界面出现：插件包没有独立格式文档，用户只能照着这里打包。
 */
export function InstallPanel(props: {
  busy: boolean;
  onBusy: (value: boolean) => void;
  onInstalled: (result: PluginChangeResult) => void;
}) {
  const [open, setOpen] = createSignal(false);
  const [file, setFile] = createSignal<File | null>(null);
  const [error, setError] = createSignal<string | null>(null);
  let input!: HTMLInputElement;

  const pickFile = (selected: File | null) => {
    setError(null);
    if (!selected) {
      setFile(null);
      return;
    }

    if (!/\.zip$/i.test(selected.name)) {
      setFile(null);
      setError(t('plugins.zipRequired'));
      return;
    }

    if (selected.size > MAX_PACKAGE_BYTES) {
      setFile(null);
      setError(t('plugins.install.tooLarge', { max: MAX_PACKAGE_BYTES / 1024 / 1024 }));
      return;
    }

    setFile(selected);
  };

  const reset = () => {
    setFile(null);
    setError(null);
    if (input) input.value = '';
  };

  const install = async () => {
    const selected = file();
    if (!selected || props.busy) return;

    props.onBusy(true);
    setError(null);
    try {
      const contentBase64 = await fileToBase64(selected);
      const result = await pluginsApi.install({ fileName: selected.name, contentBase64 });
      props.onInstalled(result);
      reset();
      setOpen(false);
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      props.onBusy(false);
    }
  };

  const copySample = () => {
    void navigator.clipboard
      ?.writeText(manifestSample())
      .then(() => showToast(t('plugins.install.sampleCopied'), 'success'))
      .catch(() => showToast(t('plugins.install.copyFailed'), 'error'));
  };

  return (
    <div class="plugin-install">
      <div class="plugin-install-head">
        <button class="btn ghost" onClick={() => setOpen(!open())}>
          <Icon name={open() ? 'chevron-down' : 'plus'} size={15} />
          {t('plugins.install.title')}
        </button>
        <span class="faint">{t('plugins.install.hint')}</span>
      </div>

      <Show when={open()}>
        <div class="plugin-install-body">
          <div class="plugin-install-pick">
            <input
              ref={input}
              type="file"
              accept=".zip,application/zip"
              style={{ display: 'none' }}
              onChange={(event) => pickFile(event.currentTarget.files?.[0] ?? null)}
            />
            <button class="btn" disabled={props.busy} onClick={() => input.click()}>
              <Icon name="folder" size={15} />
              {t('plugins.install.pick')}
            </button>
            <span class="faint">{file() ? t('plugins.install.selectedFile', { name: file()!.name, size: formatSize(file()!.size) }) : t('plugins.install.notPicked')}</span>
            <div class="spacer" />
            <Show when={file()}>
              <button class="btn ghost" disabled={props.busy} onClick={reset}>
                {t('plugins.install.clear')}
              </button>
            </Show>
            <button class="btn primary" disabled={!file() || props.busy} onClick={() => void install()}>
              {props.busy ? t('plugins.install.installing') : t('plugins.install.uploadAndInstall')}
            </button>
          </div>

          <Show when={error()}>
            <div class="notice error">
              <Icon name="alert" size={16} />
              <span>{error()}</span>
            </div>
          </Show>

          <div class="plugin-doc">
            <div class="plugin-doc-section">
              <div class="section-label">{t('plugins.install.structure')}</div>
              <pre class="plugin-doc-pre">{packageTree()}</pre>
              <ul class="plugin-doc-list">
                <li>{t('plugins.install.requiredFields')}</li>
                <li>{t('plugins.install.dll')}</li>
                <li>{t('plugins.install.webPageRule')}</li>
                <li>{t('plugins.install.packageRule')}</li>
                <li>{t('plugins.install.autoEnable')}</li>
              </ul>
            </div>

            <div class="plugin-doc-section">
              <div class="plugin-doc-section-head">
                <div class="section-label">{t('plugins.install.minExample')}</div>
                <button class="btn ghost" onClick={copySample}>
                  {t('plugins.install.copy')}
                </button>
              </div>
              <pre class="plugin-doc-pre mono">{manifestSample()}</pre>
            </div>

            <div class="notice warn">
              <Icon name="alert" size={16} />
              <span>
                {t('plugins.install.securityNotice')}
              </span>
            </div>
          </div>
        </div>
      </Show>
    </div>
  );
}

function formatSize(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)}MB`;
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(0)}KB`;
  return `${bytes}B`;
}