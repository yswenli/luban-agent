import { Match, onMount, Show, Switch } from 'solid-js';

import { ToastStack } from './components/ToastStack';
import { Icon } from './components/Icons';
import { useRoute } from './lib/router';
import { ChatPage } from './pages/Chat';
import { SessionListPage } from './pages/SessionList';
import { SettingsPage } from './pages/Settings';
import { bootError, booting, loadBootstrap } from './stores/app';

export function App() {
  const route = useRoute();

  onMount(() => {
    void loadBootstrap();
  });

  return (
    <>
      <Show when={!booting()} fallback={<BootScreen />}>
        <Show when={bootError()} fallback={
          <Switch>
            <Match when={route().name === 'sessions'}>
              <SessionListPage />
            </Match>
            <Match when={route().name === 'chat'}>
              <ChatPage sessionId={(route() as { name: 'chat'; sessionId: string }).sessionId} />
            </Match>
            <Match when={route().name === 'settings'}>
              <SettingsPage tab={(route() as { name: 'settings'; tab: string }).tab} />
            </Match>
          </Switch>
        }>
          <ErrorScreen message={bootError() ?? '启动失败'} />
        </Show>
      </Show>
      <ToastStack />
    </>
  );
}

function BootScreen() {
  return (
    <div class="boot">
      <div class="boot-card">
        <div class="spinner" />
        <h1>正在连接 LuBan Agent</h1>
        <p>读取工作区、供应商与模型配置…</p>
      </div>
    </div>
  );
}

function ErrorScreen(props: { message: string }) {
  return (
    <div class="boot">
      <div class="boot-card">
        <Icon name="alert" size={28} />
        <h1>无法连接后端</h1>
        <p>{props.message}</p>
        <button class="btn primary" onClick={() => void loadBootstrap()}>
          <Icon name="refresh" size={16} />
          重试
        </button>
      </div>
    </div>
  );
}