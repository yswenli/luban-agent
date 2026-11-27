import { createEffect, createMemo, createSignal, Match, onCleanup, onMount, Show, Switch } from 'solid-js';

import { Icon } from './components/Icons';
import { FilePreviewDialog } from './components/FilePreviewDialog';
import { AboutDialog, ConnectionHelpDialog } from './components/InfoDialogs';
import { LoginPage } from './components/LoginPage';
import { ToastStack } from './components/ToastStack';
import { TopBar } from './components/TopBar';
import { get } from './lib/api';
import { closePlugins, closeSettings, lastView, useRoute } from './lib/router';
import { ChatPage } from './pages/Chat';
import { LlmWikiPage } from './pages/LlmWiki';
import { PluginPage } from './pages/plugins/PluginPage';
import { PluginsOverlay } from './pages/plugins/PluginsOverlay';
import { SessionListPage } from './pages/SessionList';
import { SettingsOverlay } from './pages/Settings';
import { bootError, booting, loadBootstrap, markBootSkipped } from './stores/app';
import { authError, authStatus, currentUser, loadAuthStatus } from './stores/auth';
import { authBlocked } from './stores/session';
import { rememberSession } from './stores/tabs';

/** 会话心跳间隔：远小于令牌有效期（默认 15 分钟），页面开着就不会因为闲置而过期。 */
const SESSION_HEARTBEAT_MS = 60_000;

export function App() {
  const route = useRoute();
  /** 登录状态是否已问过宿主；问过之前不渲染登录页，避免有令牌时闪一下登录框。 */
  const [authReady, setAuthReady] = createSignal(false);

  createEffect(() => {
    const current = route();
    if (current.name !== 'chat') return;
    rememberSession(current.sessionId);
  });

  /** 问一次登录状态；已登录才继续读 bootstrap。 */
  async function start(): Promise<void> {
    setAuthReady(false);
    const status = await loadAuthStatus();
    setAuthReady(true);
    if (status?.user) void loadBootstrap();
    else markBootSkipped();
  }

  onMount(() => {
    void start();

    // 心跳只做一件事：让门禁把续期后的令牌滑回 Cookie（真正的过期由 401 转成登录页）
    const timer = window.setInterval(() => {
      if (!currentUser() || bootError() || authBlocked()) return;
      void get('/system/session').catch(() => undefined);
    }, SESSION_HEARTBEAT_MS);
    onCleanup(() => window.clearInterval(timer));
  });

  // 设置与插件管理都是浮层：主区渲染打开前的视图
  const view = createMemo(() =>
    route().name === 'settings' || route().name === 'plugins' ? lastView() : route(),
  );
  const activeSessionId = createMemo(() => {
    const current = view();
    return current.name === 'chat' ? current.sessionId : null;
  });
  const showWiki = createMemo(() => view().name === 'wiki');
  const settingsTab = createMemo(() => {
    const current = route();
    return current.name === 'settings' ? current.tab : null;
  });
  const pluginsOpen = createMemo(() => route().name === 'plugins');
  const pluginRoute = createMemo(() => {
    const current = view();
    return current.name === 'plugin' ? current : null;
  });

  return (
    <>
      <Show when={authReady()} fallback={<BootScreen text="正在校验登录状态…" />}>
        <Show
          when={authStatus()}
          fallback={<AuthUnavailable message={authError() ?? '无法连接后端'} onRetry={() => void start()} />}
        >
          <Show when={currentUser()} fallback={<LoginPage notice={authBlocked()} />}>
            {/* 令牌在会话中途失效：留在登录页重新登录，不把用户赶去终端取入口地址 */}
            <Show when={authBlocked()} fallback={<>
              <Show when={!booting()} fallback={<BootScreen />}>
                <Show
                  when={bootError()}
                  fallback={
                    <div class="app">
                      <TopBar activeSessionId={activeSessionId()} />

                      <div class="shell">
                        <Switch fallback={<SessionListPage />}>
                          <Match when={showWiki()}>
                            <LlmWikiPage />
                          </Match>
                          <Match when={pluginRoute()}>
                            {(target) => (
                              <SessionListPage
                                main={<PluginPage pluginId={target().pluginId} entryId={target().entryId} />}
                              />
                            )}
                          </Match>
                          <Match when={activeSessionId()} keyed>
                            {(sessionId) => <ChatPage sessionId={sessionId} />}
                          </Match>
                        </Switch>
                      </div>

                      <Show when={settingsTab()}>{(tab) => <SettingsOverlay tab={tab()} onClose={closeSettings} />}</Show>
                      <Show when={pluginsOpen()}>
                        <PluginsOverlay onClose={closePlugins} />
                      </Show>
                      <FilePreviewDialog />
                    </div>
                  }
                >
                  <ErrorScreen message={bootError() ?? '启动失败'} />
                </Show>
              </Show>
            </>}>
              {(message) => <LoginPage notice={message()} />}
            </Show>
          </Show>
        </Show>
      </Show>
      <ToastStack />
    </>
  );
}

function BootScreen(props: { text?: string }) {
  return (
    <div class="boot">
      <div class="boot-card">
        <div class="spinner" />
        <h1>正在连接 LuBan Agent</h1>
        <p>{props.text ?? '读取工作区、供应商与模型配置…'}</p>
      </div>
    </div>
  );
}

/** 登录状态都取不到：宿主没起来或网络不通，重试只重问状态，不触发 bootstrap。 */
function AuthUnavailable(props: { message: string; onRetry: () => void }) {
  return (
    <div class="boot">
      <div class="boot-card">
        <Icon name="alert" size={28} />
        <h1>无法连接后端</h1>
        <p>{props.message}</p>
        <div class="boot-actions">
          <button class="btn primary" onClick={props.onRetry}>
            <Icon name="refresh" size={16} />
            重试
          </button>
        </div>
      </div>
    </div>
  );
}

function ErrorScreen(props: { message: string }) {
  // 连不上宿主时读不到 /system/readme，帮助内容只能是本地排查步骤，不能在弹窗里渲染 README
  const [dialog, setDialog] = createSignal<'help' | 'about' | null>(null);

  return (
    <div class="boot">
      <div class="boot-card">
        <Icon name="alert" size={28} />
        <h1>无法连接后端</h1>
        <p>{props.message}</p>
        <div class="boot-actions">
          <button class="btn primary" onClick={() => void loadBootstrap()}>
            <Icon name="refresh" size={16} />
            重试
          </button>
          <button class="btn ghost" onClick={() => setDialog('help')}>
            <Icon name="help" size={16} />
            帮助
          </button>
          <button class="btn ghost" onClick={() => setDialog('about')}>
            <Icon name="info" size={16} />
            关于
          </button>
        </div>
      </div>

      <Show when={dialog() === 'help'}>
        <ConnectionHelpDialog onClose={() => setDialog(null)} />
      </Show>
      <Show when={dialog() === 'about'}>
        <AboutDialog onClose={() => setDialog(null)} />
      </Show>
    </div>
  );
}