import { createSignal, Show } from 'solid-js';

import { LocaleMenu } from '../../components/LocaleMenu';
import { t } from '../../i18n';
import { showToast } from '../../stores/app';
import { changePassword, currentUser, updateDisplayName, userLabel } from '../../stores/auth';

import { Field, FormActions, TextField } from './Shared';

/**
 * 账号页：本人资料与修改密码。
 *
 * 显示名与密码共用底部「保存」：显示名有改动就更新；密码字段填了才校验并改密，
 * 两处都没动则直接提示，不发请求。
 * 改密成功后服务端会换发当前浏览器的登录 Cookie，其他设备上的旧令牌立即失效，
 * 因此这里提示用户「其他设备需重新登录」而不是「请重新登录」（当前页仍可用）。
 */
export function AccountTab() {
  const [displayName, setDisplayName] = createSignal(currentUser() ? userLabel(currentUser()!) : '');
  const [oldPassword, setOldPassword] = createSignal('');
  const [newPassword, setNewPassword] = createSignal('');
  const [confirmPassword, setConfirmPassword] = createSignal('');
  const [busy, setBusy] = createSignal(false);

  const submit = async (): Promise<void> => {
    const user = currentUser();
    if (!user) return;

    const name = displayName().trim();
    const nameChanged = name !== userLabel(user);
    const wantsPasswordChange = Boolean(oldPassword() || newPassword() || confirmPassword());

    if (!nameChanged && !wantsPasswordChange) {
      showToast(t('account.noChanges'));
      return;
    }
    if (wantsPasswordChange) {
      if (!oldPassword()) {
        showToast(t('account.errorCurrentRequired'), 'error');
        return;
      }
      if (newPassword().length < 8) {
        showToast(t('account.errorNewShort'), 'error');
        return;
      }
      if (newPassword() !== confirmPassword()) {
        showToast(t('account.errorMismatch'), 'error');
        return;
      }
    }

    setBusy(true);

    // 先存显示名：它不涉及凭据，失败时不必回滚；密码成功后会递增令牌版本并换发 Cookie
    let error: string | null = null;
    if (nameChanged) {
      error = await updateDisplayName(name);
      if (!error) {
        const updated = currentUser();
        setDisplayName(updated ? userLabel(updated) : name);
      }
    }

    if (!error && wantsPasswordChange) {
      error = await changePassword(oldPassword(), newPassword());
      if (!error) {
        setOldPassword('');
        setNewPassword('');
        setConfirmPassword('');
      }
    }

    setBusy(false);
    if (error) {
      showToast(error, 'error');
      return;
    }

    if (nameChanged && wantsPasswordChange) {
      showToast(t('account.updatedBoth'), 'success');
    } else if (nameChanged) {
      showToast(t('account.updatedName'), 'success');
    } else {
      showToast(t('account.updatedPassword'), 'success');
    }
  };

  return (
    <div class="settings-form">
      <div class="settings-form-head">
        <span class="field-label">{t('account.title')}</span>
      </div>
      <Show when={currentUser()}>
        {(user) => (
          <div class="field-grid">
            <TextField label={t('account.userName')} value={user().userName} readOnly onInput={() => undefined} />
            <TextField
              label={t('account.displayName')}
              value={displayName()}
              placeholder={user().userName}
              hint={t('account.displayNameHint')}
              autocomplete="off"
              onInput={(value) => setDisplayName(value)}
            />
            <TextField
              label={t('account.userId')}
              value={user().userId}
              readOnly
              hint={t('account.userIdHint')}
              onInput={() => undefined}
            />
            <Field label={t('account.role')}>
              <input
                class="text-input"
                type="text"
                value={user().isAdmin ? t('account.roleAdmin') : t('account.roleUser')}
                readOnly
              />
            </Field>
            <div class="field">
              <span class="field-label">{t('account.language')}</span>
              <LocaleMenu variant="text" />
              <span class="field-hint">{t('account.languageHint')}</span>
            </div>
          </div>
        )}
      </Show>

      <div class="settings-form-head" style={{ 'margin-top': '20px' }}>
        <span class="field-label">{t('account.changePassword')}</span>
      </div>
      <div class="field-grid">
        <TextField
          label={t('account.currentPassword')}
          type="password"
          wide
          value={oldPassword()}
          hint={t('account.currentPasswordHint')}
          onInput={(value) => setOldPassword(value)}
        />
        <TextField
          label={t('account.newPassword')}
          type="password"
          value={newPassword()}
          hint={t('account.newPasswordHint')}
          onInput={(value) => setNewPassword(value)}
        />
        <TextField
          label={t('account.confirmPassword')}
          type="password"
          value={confirmPassword()}
          onInput={(value) => setConfirmPassword(value)}
        />
      </div>
      <FormActions>
        <button class="btn primary" onClick={() => void submit()} disabled={busy()}>
          {t('common.save')}
        </button>
      </FormActions>
    </div>
  );
}