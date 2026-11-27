import { createEffect, createMemo, createSignal, Match, Switch } from 'solid-js';

import { Icon } from '../../components/Icons';
import { openPlugins, openSessions } from '../../lib/router';
import { applyThemeTo } from '../../lib/theme';
import type { PluginPageInfo } from '../../lib/types';
import { showToast } from '../../stores/app';
import { ensurePluginsLoaded, findPlugin, pluginsLoaded } from '../../stores/plugins';
import { t } from '../../i18n';

type PageState = { kind: 'loading' } | { kind: 'ok' } | { kind: 'error'; message: string };

/**
 * 插件页面宿主：同源 iframe 占满主内容区。
 *
 * 插件页面与宿主同源（不加 sandbox），页面内可直接调用现有 REST / SSE —— 会话 Cookie 由浏览器自动携带；
 * `/system` 特权动作只认宿主固定 Bearer，插件页面拿不到，所以装插件不等于能关停服务。
 */
export function PluginPage(props: { pluginId: string; entryId: string }) {
  const [state, setState] = createSignal<PageState>({ kind: 'loading' });
  const [reloadToken, setReloadToken] = createSignal(0);

  createEffect(() => {
    void ensurePluginsLoaded();
  });

  const plugin = createMemo(() => findPlugin(props.pluginId));

  const page = createMemo<PluginPageInfo | null>(() => {
    const current = plugin();
    if (!current) return null;
    // 不回落第一个页面：侧栏入口与 hash 都按 entryId 定位，回落会让标题 / 地址与内容对不上
    return current.pages.find((item) => item.entryId === props.entryId) ?? null;
  });

  // 停用 / 卸载后列表刷新：当前页不再可用时回首页并说明原因
  createEffect(() => {
    if (!pluginsLoaded()) return;
    const current = plugin();
    if (!current) {
      showToast(t('plugins.page.uninstalled'), 'error');
      openSessions();
      return;
    }

    if (current.state !== 'enabled') {
      showToast(t('plugins.page.disabled'), 'error');
      openSessions();
      return;
    }

    if (!page()?.url) {
      showToast(t('plugins.page.missing'), 'error');
      openSessions();
    }
  });

  // 先探一次页面资源：404 / 401 时 iframe 会白屏，外层给出可读提示
  createEffect(() => {
    // 订阅重载令牌：点「重新加载」时重新探测并重建 iframe
    reloadToken();
    const url = page()?.url;
    if (!url) return;

    let cancelled = false;
    setState({ kind: 'loading' });
    void fetch(url, { credentials: 'same-origin', cache: 'no-store' })
      .then((response) => {
        if (cancelled) return;
        if (response.ok) {
          setState({ kind: 'ok' });
          return;
        }

        const message =
          response.status === 401
            ? t('plugins.page.sessionExpired')
            : t('plugins.page.httpFailed', { status: response.status });
        setState({ kind: 'error', message });
      })
      .catch(() => {
        if (cancelled) return;
        setState({ kind: 'error', message: t('plugins.page.loadFailed') });
      });

    return () => {
      cancelled = true;
    };
  });

  const label = () => page()?.label || plugin()?.name || props.entryId;

  /** iframe 的重建键：url 与重载令牌任一变化都换新对象，`keyed` Show 会重建 iframe。 */
  const frame = createMemo(() => {
    const url = page()?.url;
    return url ? { url, token: reloadToken() } : null;
  });

  return (
    <div class="home-main plugin-main">
      <div class="plugin-page-head">
        <div>
          <div class="dialog-title">{label()}</div>
          <div class="dialog-sub mono">
            {plugin()?.id ?? props.pluginId} · {props.entryId}
          </div>
        </div>
        <div class="spacer" />
        <button class="btn ghost" onClick={() => setReloadToken(reloadToken() + 1)}>
          <Icon name="refresh" size={15} />
          {t('plugins.page.reload')}
        </button>
        <button class="btn ghost" onClick={() => openPlugins()}>
          <Icon name="plug" size={15} />
          {t('plugins.page.manage')}
        </button>
      </div>

      <div class="plugin-frame-wrap">
        <Switch>
          <Match when={state().kind === 'error'}>
            <div class="notice error">
              <Icon name="alert" size={16} />
              <span>{(state() as { message: string }).message}</span>
            </div>
          </Match>
          <Match when={state().kind === 'loading'}>
            <div class="loading-line">{t('plugins.page.loading')}</div>
          </Match>
          <Match when={state().kind === 'ok' ? frame() : null} keyed>
            {(target) => {
              let frameEl: HTMLIFrameElement | undefined;
              // 主题切换只重写 iframe 文档上的属性，不重建元素；点「重新加载」时 keyed 换新元素，由 onLoad 重新注入
              createEffect(() => applyThemeTo(frameEl?.contentDocument));
              return (
                <iframe
                  ref={(el) => (frameEl = el)}
                  class="plugin-frame"
                  src={target.url}
                  title={label()}
                  onLoad={() => applyThemeTo(frameEl?.contentDocument)}
                />
              );
            }}
          </Match>
        </Switch>
      </div>
    </div>
  );
}