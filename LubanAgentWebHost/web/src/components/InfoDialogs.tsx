import { createSignal, onMount, Show } from 'solid-js';

import { get } from '../lib/api';
import { bootstrap } from '../stores/app';
import { Modal } from './Dialog';
import { Icon } from './Icons';
import { Markdown } from './Markdown';
import { t } from '../i18n';

const REPO_URL = 'https://github.com/yswenli/luban-agent';
const AUTHOR_URL = 'https://github.com/yswenli';
const AUTHOR_EMAIL = 'yswenli@outlook.com';

/**
 * README 里的链接在弹窗内点击会改掉 hash 路由（SPA 的地址全在 hash 里），
 * 所以统一拦截：外部链接新窗口打开，仓库内相对路径转到 GitHub，页内锚点不跳。
 */
function onReadmeClick(event: MouseEvent) {
  const anchor = (event.target as HTMLElement | null)?.closest('a');
  if (!anchor) return;

  event.preventDefault();
  const href = anchor.getAttribute('href') ?? '';
  if (/^(https?:|mailto:)/i.test(href)) {
    window.open(href, '_blank', 'noopener,noreferrer');
  } else if (!href.startsWith('#') && href) {
    window.open(`${REPO_URL}/blob/main/${href.replace(/^\.\//, '')}`, '_blank', 'noopener,noreferrer');
  }
}

/**
 * 帮助弹窗：直接渲染仓库 README。
 *
 * 正文由宿主从程序集旁的 `README.md` 读回（`/system/readme`），前端不维护第二份帮助文档；
 * 读取失败只提示原因，不影响其它操作。
 */
export function HelpDialog(props: { onClose: () => void }) {
  const [markdown, setMarkdown] = createSignal<string | null>(null);
  const [error, setError] = createSignal<string | null>(null);

  onMount(() => {
    void get<{ markdown: string }>('/system/readme')
      .then((data) => setMarkdown(data.markdown))
      .catch((err: unknown) => setError(err instanceof Error ? err.message : t('info.readmeFailed')));
  });

  return (
    <Modal
      title={t('app.help')}
      subtitle={t('info.readmeTitle')}
      wide
      onClose={props.onClose}
      footer={
        <button class="btn primary" onClick={props.onClose}>
          {t('common.close')}
        </button>
      }
    >
      <Show when={error()}>
        <div class="notice error">
          <Icon name="alert" size={16} />
          <span>{error()}</span>
        </div>
      </Show>
      <Show
        when={markdown()}
        fallback={
          <Show when={!error()}>
            <div class="loading-line">{t('info.readmeLoading')}</div>
          </Show>
        }
      >
        {(text) => (
          <div class="md-readme" onClick={onReadmeClick}>
            <Markdown text={text()} />
          </div>
        )}
      </Show>
    </Modal>
  );
}

/** 关于弹窗：仓库、作者与版本（版本来自首屏引导，连不上宿主时不显示）。 */
export function AboutDialog(props: { onClose: () => void }) {
  const version = () => bootstrap()?.version?.split('+')[0];

  return (
    <Modal
      title={t('app.about')}
      subtitle="LuBan Agent"
      onClose={props.onClose}
      footer={
        <button class="btn primary" onClick={props.onClose}>
          {t('info.ok')}
        </button>
      }
    >
      <figure class="about-qr">
        <img src="/system/entryQr" alt={t('info.qrTitle')} width="180" height="180" />
        <figcaption>{t('info.qrHint')}</figcaption>
      </figure>
      <p class="about-intro">{t('info.description')}</p>
      <dl class="about-grid">
        <dt>{t('info.version')}</dt>
        <dd>{version() ? `v${version()}` : t('info.versionUnknown')}</dd>
        <dt>{t('info.repo')}</dt>
        <dd>
          <a href={REPO_URL} target="_blank" rel="noreferrer">
            {REPO_URL.replace('https://', '')}
          </a>
        </dd>
        <dt>{t('info.author')}</dt>
        <dd>
          yswenli ·{' '}
          <a href={AUTHOR_URL} target="_blank" rel="noreferrer">
            @yswenli
          </a>{' '}
          · <a href={`mailto:${AUTHOR_EMAIL}`}>{AUTHOR_EMAIL}</a>
        </dd>
        <dt>{t('info.license')}</dt>
        <dd>MIT License</dd>
      </dl>
    </Modal>
  );
}

/**
 * 连不上宿主时的帮助：此时读不到 README，只能给出本地可执行的排查步骤。
 */
export function ConnectionHelpDialog(props: { onClose: () => void }) {
  return (
    <Modal
      title={t('app.help')}
      subtitle={t('info.helpTitle')}
      onClose={props.onClose}
      footer={
        <button class="btn primary" onClick={props.onClose}>
          {t('info.ok')}
        </button>
      }
    >
      <ul class="about-steps">
        <li>
          {t('info.help.tokenPrefix')} <code>?t=</code>
          {t('info.help.tokenBody')}
        </li>
        <li>
          {t('info.help.runInTerminal')} <code>LubanAgentWebHost --token</code>
          {t('info.help.rerun')}
        </li>
        <li>
          {t('info.help.restartPrefix')} <code>LubanAgentWebHost</code>
          {t('info.help.restartSuffix')}
        </li>
        <li>{t('info.help.forgotPassword')}</li>
        <li>
          {t('info.help.stopPrefix')} <code>LubanAgentWebHost --stop</code>
          {t('info.help.portPrefix')} <code>--port</code>
          {t('info.help.portSuffix')}
        </li>
        <li>{t('info.help.security')}</li>
      </ul>
    </Modal>
  );
}