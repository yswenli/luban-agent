import { createSignal, onMount, Show } from 'solid-js';

import { get } from '../lib/api';
import { bootstrap } from '../stores/app';
import { Modal } from './Dialog';
import { Icon } from './Icons';
import { Markdown } from './Markdown';

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
      .catch((err: unknown) => setError(err instanceof Error ? err.message : '读取 README 失败'));
  });

  return (
    <Modal
      title="帮助"
      subtitle="LuBan Agent README（随宿主构建同步）"
      wide
      onClose={props.onClose}
      footer={
        <button class="btn primary" onClick={props.onClose}>
          关闭
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
            <div class="loading-line">读取 README…</div>
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
      title="关于"
      subtitle="LuBan Agent"
      onClose={props.onClose}
      footer={
        <button class="btn primary" onClick={props.onClose}>
          知道了
        </button>
      }
    >
      <p class="about-intro">
        基于 Microsoft Agent Framework 的 AI Agent 解决方案：一套核心运行时，命令行 TUI、跨平台桌面客户端与本地 Agent 服务三端统一。
      </p>
      <dl class="about-grid">
        <dt>版本</dt>
        <dd>{version() ? `v${version()}` : '未知（未读到宿主版本）'}</dd>
        <dt>仓库</dt>
        <dd>
          <a href={REPO_URL} target="_blank" rel="noreferrer">
            {REPO_URL.replace('https://', '')}
          </a>
        </dd>
        <dt>作者</dt>
        <dd>
          yswenli ·{' '}
          <a href={AUTHOR_URL} target="_blank" rel="noreferrer">
            @yswenli
          </a>{' '}
          · <a href={`mailto:${AUTHOR_EMAIL}`}>{AUTHOR_EMAIL}</a>
        </dd>
        <dt>许可</dt>
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
      title="帮助"
      subtitle="连不上宿主时怎么办"
      onClose={props.onClose}
      footer={
        <button class="btn primary" onClick={props.onClose}>
          知道了
        </button>
      }
    >
      <ul class="about-steps">
        <li>
          宿主的浏览器入口带一次性令牌（<code>?t=</code> 参数）。令牌随本次运行有效，宿主重启后旧地址会失效。
        </li>
        <li>在宿主机终端执行 <code>LubanAgentWebHost token</code>，重新打印「浏览器入口」并整条复制打开。</li>
        <li>找不到终端或宿主已退出：重新启动 <code>LubanAgentWebHost</code>，再看启动日志里的浏览器入口。</li>
        <li>停止常驻实例：<code>LubanAgentWebHost --stop</code>；端口被占用时用 <code>--port</code> 换一个。</li>
        <li>带令牌的地址等同于完整操作权限，不要转发给他人。</li>
      </ul>
    </Modal>
  );
}