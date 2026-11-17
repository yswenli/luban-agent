import { createSignal, Show } from 'solid-js';

import { Icon } from '../../components/Icons';
import { fileToBase64, pluginsApi } from '../../lib/plugins';
import type { PluginChangeResult } from '../../lib/types';
import { showToast } from '../../stores/app';

/** 单个插件包上限，与服务端校验一致；超限直接在前端拦掉，不浪费一次上传。 */
const MAX_PACKAGE_BYTES = 32 * 1024 * 1024;

const PACKAGE_TREE = `my-plugin.zip
└─ com.example.myplugin/          # 顶层目录名随意，建议与 id 一致
   ├─ plugin.json                 # 必需
   ├─ lib/                        # 入口程序集及其依赖
   │  └─ Com.Example.MyPlugin.dll
   ├─ web/                        # 可选：自带页面（webPage 必须在此之下）
   │  ├─ index.html
   │  └─ app.js
   └─ assets/                     # 可选：图标等资源`;

const MANIFEST_SAMPLE = `{
  "id": "com.example.myplugin",
  "name": "示例插件",
  "version": "1.0.0",
  "entryAssembly": "Com.Example.MyPlugin.dll",
  "uiContributions": [
    { "id": "main", "label": "示例", "icon": "plug", "webPage": "web/index.html" }
  ]
}`;

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
      setError('请选择 .zip 格式的插件包');
      return;
    }

    if (selected.size > MAX_PACKAGE_BYTES) {
      setFile(null);
      setError(`插件包超过 ${MAX_PACKAGE_BYTES / 1024 / 1024}MB 上限`);
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
      ?.writeText(MANIFEST_SAMPLE)
      .then(() => showToast('已复制 plugin.json 示例', 'success'))
      .catch(() => showToast('复制失败，请手动选中示例文本', 'error'));
  };

  return (
    <div class="plugin-install">
      <div class="plugin-install-head">
        <button class="btn ghost" onClick={() => setOpen(!open())}>
          <Icon name={open() ? 'chevron-down' : 'plus'} size={15} />
          安装插件
        </button>
        <span class="faint">上传浏览器选中的 zip 包；安装成功后自动启用。</span>
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
              选择 zip 包
            </button>
            <span class="faint">{file() ? `${file()!.name}（${formatSize(file()!.size)}）` : '尚未选择文件'}</span>
            <div class="spacer" />
            <Show when={file()}>
              <button class="btn ghost" disabled={props.busy} onClick={reset}>
                清除
              </button>
            </Show>
            <button class="btn primary" disabled={!file() || props.busy} onClick={() => void install()}>
              {props.busy ? '安装中…' : '上传并安装'}
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
              <div class="section-label">包结构</div>
              <pre class="plugin-doc-pre">{PACKAGE_TREE}</pre>
              <ul class="plugin-doc-list">
                <li>
                  <code>plugin.json</code> 必须有 <code>id</code>、<code>name</code>、<code>version</code>、
                  <code>entryAssembly</code>；<code>uiContributions</code>、<code>skills</code>、<code>mcpServers</code>{' '}
                  可选。
                </li>
                <li>
                  必须有 DLL，且 <code>entryAssembly</code> 里的入口类型实现 <code>IPlugin</code>；插件只共享宿主已加载的{' '}
                  <code>LuBan.*</code> / <code>Microsoft.*</code> / <code>System.*</code>，不要自带这些程序集。
                </li>
                <li>
                  <code>webPage</code> 必须指向本包 <code>web/</code> 下的 <code>.html</code> 文件；页面里的相对路径可用，
                  白名单外扩展名（如 <code>.woff2</code>、<code>.json</code>）可能无法加载。
                </li>
                <li>
                  清单放压缩包根目录，或只保留一个顶层目录；单包不超过 32MB，解压后不超过 128MB。
                </li>
                <li>安装后自动启用；同 id 已存在时请改用「升级」。</li>
              </ul>
            </div>

            <div class="plugin-doc-section">
              <div class="plugin-doc-section-head">
                <div class="section-label">plugin.json 最小示例</div>
                <button class="btn ghost" onClick={copySample}>
                  复制
                </button>
              </div>
              <pre class="plugin-doc-pre mono">{MANIFEST_SAMPLE}</pre>
            </div>

            <div class="notice warn">
              <Icon name="alert" size={16} />
              <span>
                插件页面与宿主同源运行，脚本可访问浏览器存储与当前页面 DOM。只安装可信来源的插件。
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