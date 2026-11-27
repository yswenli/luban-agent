import { createSignal, Show } from 'solid-js';

import { Icon } from './Icons';
import { authStatus, login, register } from '../stores/auth';

/**
 * 登录 / 注册整页。
 *
 * 浏览器凭据是 HttpOnly Cookie，前端无法自检，只能先问 `/auth/status`；
 * 登录成功由宿主写 Cookie，随后整页重载重新走一遍状态查询与 bootstrap。
 */
export function LoginPage(props: { notice?: string | null }) {
  const [mode, setMode] = createSignal<'login' | 'register'>('login');
  const [userName, setUserName] = createSignal('');
  const [password, setPassword] = createSignal('');
  const [displayName, setDisplayName] = createSignal('');
  const [inviteCode, setInviteCode] = createSignal('');
  const [error, setError] = createSignal<string | null>(null);
  const [busy, setBusy] = createSignal(false);

  const allowRegistration = () => authStatus()?.allowRegistration ?? false;
  const initialized = () => authStatus()?.initialized ?? true;

  const switchMode = (next: 'login' | 'register') => {
    if (busy() || mode() === next) return;
    setMode(next);
    setError(null);
  };

  const submit = async (event: Event) => {
    event.preventDefault();
    if (busy()) return;

    const name = userName().trim();
    if (!name || !password()) {
      setError('请输入用户名和密码');
      return;
    }

    if (mode() === 'register') {
      if (password().length < 8) {
        setError('密码至少 8 位');
        return;
      }

      if (!inviteCode().trim()) {
        setError('请填写邀请码');
        return;
      }
    }

    setBusy(true);
    setError(null);
    const message =
      mode() === 'login'
        ? await login(name, password())
        : await register({
            userName: name,
            password: password(),
            displayName: displayName().trim() || undefined,
            inviteCode: inviteCode().trim(),
          });
    setBusy(false);

    if (message) {
      setError(message);
      return;
    }

    // 令牌已写进 HttpOnly Cookie，整页重载重新读状态与数据
    window.location.reload();
  };

  return (
    <div class="boot">
      <form class="auth-card" onSubmit={(event) => void submit(event)}>
        <div class="auth-head">
          <span class="auth-logo">
            <Icon name="spark" size={20} />
          </span>
          <div>
            <h1>LuBan Agent</h1>
            <p>{mode() === 'login' ? '登录后继续使用你的工作区与会话' : '凭邀请码创建账号'}</p>
          </div>
        </div>

        <Show when={props.notice}>
          <div class="auth-note">
            <Icon name="alert" size={15} />
            <span>{props.notice}</span>
          </div>
        </Show>

        <Show when={!initialized()}>
          <div class="auth-note">
            <Icon name="info" size={15} />
            <span>管理员账号尚未初始化，请查看宿主终端输出的初始密码。</span>
          </div>
        </Show>

        <Show when={allowRegistration()}>
          <div class="auth-tabs" role="tablist">
            <button
              type="button"
              class="auth-tab"
              classList={{ active: mode() === 'login' }}
              onClick={() => switchMode('login')}
            >
              登录
            </button>
            <button
              type="button"
              class="auth-tab"
              classList={{ active: mode() === 'register' }}
              onClick={() => switchMode('register')}
            >
              注册
            </button>
          </div>
        </Show>

        <div class="form-stack">
          <div class="field">
            <div class="section-label">用户名</div>
            <input
              class="text-input"
              autocomplete="username"
              placeholder="登录名"
              value={userName()}
              onInput={(event) => setUserName(event.currentTarget.value)}
            />
          </div>

          <div class="field">
            <div class="section-label">密码</div>
            <input
              class="text-input"
              type="password"
              autocomplete={mode() === 'login' ? 'current-password' : 'new-password'}
              placeholder={mode() === 'login' ? '密码' : '至少 8 位'}
              value={password()}
              onInput={(event) => setPassword(event.currentTarget.value)}
            />
          </div>

          <Show when={mode() === 'register'}>
            <div class="field">
              <div class="section-label">显示名（可选）</div>
              <input
                class="text-input"
                placeholder="默认与用户名相同"
                autocomplete="off"
                value={displayName()}
                onInput={(event) => setDisplayName(event.currentTarget.value)}
              />
            </div>
            <div class="field">
              <div class="section-label">邀请码</div>
              <input
                class="text-input mono"
                placeholder="由管理员生成"
                value={inviteCode()}
                onInput={(event) => setInviteCode(event.currentTarget.value.toUpperCase())}
              />
            </div>
          </Show>
        </div>

        <Show when={error()}>
          <div class="auth-error">
            <Icon name="alert" size={15} />
            <span>{error()}</span>
          </div>
        </Show>

        <button class="btn primary auth-submit" type="submit" disabled={busy()}>
          {busy() ? '请稍候…' : mode() === 'login' ? '登录' : '注册并登录'}
        </button>
      </form>
    </div>
  );
}