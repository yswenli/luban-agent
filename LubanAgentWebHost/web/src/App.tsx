import { createEffect, createMemo, onMount, Show } from 'solid-js';

import { Icon } from './components/Icons';
import { ToastStack } from './components/ToastStack';
import { TopBar } from './components/TopBar';
import { closeSettings, lastView, useRoute } from './lib/router';
import { ChatPage } from './pages/Chat';
import { SessionListPage } from './pages/SessionList';
import { SettingsOverlay } from './pages/Settings';
import { bootError, booting, loadBootstrap } from './stores/app';
import { rememberSession } from './stores/tabs';

export function App() {
  const route = useRoute();

  createEffect(() => {
    const current = route();
    if (current.name !== 'chat') return;
    rememberSession(current.sessionId);
  });

  onMount(() => {
    void loadBootstrap();
  });

  // 设置是浮层：主区渲染打开设置前的视图
  const view = createMemo(() => (route().name === 'settings' ? lastView() : route()));
  const activeSessionId = createMemo(() => {
    const current = view();
    return current.name === 'chat' ? current.sessionId : null;
  });
  const settingsTab = createMemo(() => {
    const current = route();
    return current.name === 'settings' ? current.tab : null;
  });

  return (
    <>
      <Show when={!booting()} fallback={<BootScreen />}>
        <Show
          when={bootError()}
          fallback={
            <div class="app">
              <TopBar activeSessionId={activeSessionId()} />

              <div class="shell">
                <Show when={activeSessionId()} keyed fallback={<SessionListPage />}>
                  {(sessionId) => <ChatPage sessionId={sessionId} />}
                </Show>
              </div>

              <Show when={settingsTab()}>{(tab) => <SettingsOverlay tab={tab()} onClose={closeSettings} />}</Show>
            </div>
          }
        >
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