import { createSignal, onMount, Show } from 'solid-js';

import { formatServerDateTime, parseServerUtc } from '../../lib/format';
import { showToast } from '../../stores/app';
import { listUsers, resetPassword, unlockUser, userLabel, currentUser, type AuthUser } from '../../stores/auth';
import { FormActions, SettingsPane, TextField } from './Shared';
import { t } from '../../i18n';

/**
 * 用户页（仅管理员）：列出账号，为忘记密码的用户生成一次性新密码。
 *
 * 重置会让目标用户的旧密码与全部已登录会话立即失效；新密码只在本次响应里出现，
 * 刷新后无法再取回，所以页面上保留复制按钮并明确提示。
 */
export function UsersTab() {
  const [users, setUsers] = createSignal<AuthUser[]>([]);
  const [activeId, setActiveId] = createSignal<string | null>(null);
  const [newPassword, setNewPassword] = createSignal('');
  const [busy, setBusy] = createSignal(false);

  const active = () => users().find((item) => item.userId === activeId()) ?? null;

  const reload = async (keep?: string | null) => {
    try {
      const list = await listUsers();
      setUsers(list);
      const next = keep ?? activeId();
      const selected = list.find((item) => item.userId === next) ?? list[0] ?? null;
      setActiveId(selected?.userId ?? null);
      setNewPassword('');
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  onMount(() => void reload());

  /* 自我重置会让当前会话立即失效、新密码只返回一次；本人改密走「账号」页。 */
  const self = (user: AuthUser): boolean => user.userId === currentUser()?.userId;

  /* 服务端 UTC 时间不带时区后缀，直接 new Date 会按本地时间解读，必须走 parseServerUtc。 */
  const locked = (user: AuthUser): boolean => {
    const until = parseServerUtc(user.lockedUntil)?.getTime() ?? 0;
    return until > Date.now();
  };

  const lockState = (user: AuthUser): string => {
    if (locked(user))
      return t('users.lockedUntil', { time: formatServerDateTime(user.lockedUntil) });
    const failed = user.failedLoginCount ?? 0;
    return failed > 0 ? t('users.canLoginFailed', { count: failed }) : t('users.normal');
  };

  const unlock = async (): Promise<void> => {
    const user = active();
    if (!user) return;
    setBusy(true);
    try {
      const cleared = await unlockUser(user.userId);
      showToast(cleared ? t('users.unlocked') : t('users.noLock'), 'success');
      await reload(user.userId);
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const reset = async (): Promise<void> => {
    const user = active();
    if (!user) return;
    if (self(user)) {
      showToast(t('users.resetSelf'), 'error');
      return;
    }
    if (!window.confirm(t('users.resetConfirm', { name: userLabel(user) }))) return;

    setBusy(true);
    try {
      const password = await resetPassword(user.userId);
      setNewPassword(password);
      showToast(t('users.resetOk'), 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const copy = async (text: string): Promise<void> => {
    try {
      await navigator.clipboard.writeText(text);
      showToast(t('common.copied'), 'success');
    } catch {
      showToast(t('common.copyFailed'), 'error');
    }
  };

  const entries = () =>
    users().map((user) => ({
      key: user.userId,
      label: userLabel(user),
      sub: `${user.userName}${user.isAdmin ? t('users.adminSuffix') : ''}${user.isBuiltIn ? t('users.builtinSuffix') : ''}`,
    }));

  return (
    <SettingsPane
      title={t('users.title')}
      entries={entries()}
      activeKey={activeId()}
      emptyText={t('users.empty')}
      busy={busy()}
      onSelect={(key) => {
        setActiveId(key);
        setNewPassword('');
      }}
    >
      <Show when={active()} fallback={<div class="settings-list-empty">{t('users.selectEmpty')}</div>}>
        {(user) => (
          <>
            <div class="field-grid">
              <TextField label={t('account.userName')} value={user().userName} readOnly onInput={() => undefined} />
              <TextField
                label={t('account.role')}
                value={user().isAdmin ? t('account.roleAdmin') : t('account.roleUser')}
                readOnly
                onInput={() => undefined}
              />
              <TextField
                label={t('account.userId')}
                value={user().userId}
                readOnly
                hint={t('users.userIdHint')}
                onInput={() => undefined}
              />
              <TextField
                label={t('users.lastLogin')}
                value={formatServerDateTime(user().lastLoginAt) || t('users.neverLoggedIn')}
                readOnly
                onInput={() => undefined}
              />
              <TextField
                label={t('users.loginState')}
                value={lockState(user())}
                readOnly
                hint={t('users.lockHint')}
                onInput={() => undefined}
              />
              <Show when={newPassword()}>
                <TextField
                  label={t('account.newPassword')}
                  value={newPassword()}
                  readOnly
                  wide
                  hint={t('users.newPasswordHint')}
                  onInput={() => undefined}
                />
              </Show>
            </div>
            <FormActions>
              <button
                class="btn primary"
                onClick={() => void reset()}
                disabled={busy() || user().isBuiltIn || self(user())}
                title={
                  self(user())
                    ? t('users.resetSelf')
                    : user().isBuiltIn
                      ? t('users.resetBuiltIn')
                      : undefined
                }
              >
                {t('users.resetPassword')}
              </button>
              <button
                class="btn"
                onClick={() => void unlock()}
                disabled={busy() || (!locked(user()) && !(user().failedLoginCount ?? 0))}
                title={locked(user()) ? t('users.unlockTitle') : t('users.unlockCountTitle')}
              >
                {t('users.unlock')}
              </button>
              <Show when={newPassword()}>
                <button class="btn" onClick={() => void copy(newPassword())}>
                  {t('users.copyPassword')}
                </button>
              </Show>
            </FormActions>
            <Show when={user().isBuiltIn}>
              <div class="field-hint" style={{ 'margin-top': '10px' }}>
                {t('users.builtInNote')}
              </div>
            </Show>
          </>
        )}
      </Show>
    </SettingsPane>
  );
}