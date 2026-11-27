import { createSignal, Show } from 'solid-js';

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
      showToast('没有需要保存的修改');
      return;
    }
    if (wantsPasswordChange) {
      if (!oldPassword()) {
        showToast('请填写当前密码', 'error');
        return;
      }
      if (newPassword().length < 8) {
        showToast('新密码至少 8 位', 'error');
        return;
      }
      if (newPassword() !== confirmPassword()) {
        showToast('两次输入的新密码不一致', 'error');
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
      showToast('显示名与密码已更新，其他设备上的登录已失效', 'success');
    } else if (nameChanged) {
      showToast('显示名已更新', 'success');
    } else {
      showToast('密码已更新，其他设备上的登录已失效', 'success');
    }
  };

  return (
    <div class="settings-form">
      <div class="settings-form-head">
        <span class="field-label">我的账号</span>
      </div>
      <Show when={currentUser()}>
        {(user) => (
          <div class="field-grid">
            <TextField label="登录名" value={user().userName} readOnly onInput={() => undefined} />
            <TextField
              label="显示名"
              value={displayName()}
              placeholder={user().userName}
              hint="留空即使用登录名"
              autocomplete="off"
              onInput={(value) => setDisplayName(value)}
            />
            <TextField label="用户标识" value={user().userId} readOnly hint="同时作为工作区归属值，注册后不可变" onInput={() => undefined} />
            <Field label="角色">
              <input class="text-input" type="text" value={user().isAdmin ? '管理员' : '普通用户'} readOnly />
            </Field>
          </div>
        )}
      </Show>

      <div class="settings-form-head" style={{ 'margin-top': '20px' }}>
        <span class="field-label">修改密码</span>
      </div>
      <div class="field-grid">
        <TextField
          label="当前密码"
          type="password"
          wide
          value={oldPassword()}
          hint="仅在需要改密时填写"
          onInput={(value) => setOldPassword(value)}
        />
        <TextField
          label="新密码"
          type="password"
          value={newPassword()}
          hint="至少 8 位"
          onInput={(value) => setNewPassword(value)}
        />
        <TextField
          label="确认新密码"
          type="password"
          value={confirmPassword()}
          onInput={(value) => setConfirmPassword(value)}
        />
      </div>
      <FormActions>
        <button class="btn primary" onClick={() => void submit()} disabled={busy()}>
          保存
        </button>
      </FormActions>
    </div>
  );
}