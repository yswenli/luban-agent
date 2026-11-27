import { createSignal, Show } from 'solid-js';

import { locale, t } from '../i18n';

import { Icon } from './Icons';
import { LocaleMenu } from './LocaleMenu';
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
      setError(t('login.errorMissing'));
      return;
    }

    if (mode() === 'register') {
      if (password().length < 8) {
        setError(t('login.errorPasswordShort'));
        return;
      }

      if (!inviteCode().trim()) {
        setError(t('login.errorInviteRequired'));
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
            // 匿名阶段可能已经选过语言，注册时一并落库，免得登录后再选一次
            language: locale(),
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
        <div class="auth-locale">
          <LocaleMenu variant="text" />
        </div>

        <div class="auth-head">
          <span class="auth-logo">
            <Icon name="spark" size={20} />
          </span>
          <div>
            <h1>LuBan Agent</h1>
            <p>{mode() === 'login' ? t('login.subtitle') : t('login.registerSubtitle')}</p>
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
            <span>{t('login.uninitialized')}</span>
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
              {t('login.tabLogin')}
            </button>
            <button
              type="button"
              class="auth-tab"
              classList={{ active: mode() === 'register' }}
              onClick={() => switchMode('register')}
            >
              {t('login.tabRegister')}
            </button>
          </div>
        </Show>

        <div class="form-stack">
          <div class="field">
            <div class="section-label">{t('login.userName')}</div>
            <input
              class="text-input"
              autocomplete="username"
              placeholder={t('login.userNamePlaceholder')}
              value={userName()}
              onInput={(event) => setUserName(event.currentTarget.value)}
            />
          </div>

          <div class="field">
            <div class="section-label">{t('login.password')}</div>
            <input
              class="text-input"
              type="password"
              autocomplete={mode() === 'login' ? 'current-password' : 'new-password'}
              placeholder={
                mode() === 'login'
                  ? t('login.passwordPlaceholder')
                  : t('login.passwordNewPlaceholder')
              }
              value={password()}
              onInput={(event) => setPassword(event.currentTarget.value)}
            />
          </div>

          <Show when={mode() === 'register'}>
            <div class="field">
              <div class="section-label">{t('login.displayName')}</div>
              <input
                class="text-input"
                placeholder={t('login.displayNamePlaceholder')}
                autocomplete="off"
                value={displayName()}
                onInput={(event) => setDisplayName(event.currentTarget.value)}
              />
            </div>
            <div class="field">
              <div class="section-label">{t('login.inviteCode')}</div>
              <input
                class="text-input mono"
                placeholder={t('login.inviteCodePlaceholder')}
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
          {busy() ? t('login.submitting') : mode() === 'login' ? t('login.submit') : t('login.submitRegister')}
        </button>
      </form>
    </div>
  );
}