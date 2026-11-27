import { createSignal, onMount, Show } from 'solid-js';

import { formatServerDateTime } from '../../lib/format';
import { showToast } from '../../stores/app';
import { listUsers, resetPassword, userLabel, currentUser, type AuthUser } from '../../stores/auth';
import { FormActions, SettingsPane, TextField } from './Shared';

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

  const reset = async (): Promise<void> => {
    const user = active();
    if (!user) return;
    if (self(user)) {
      showToast('不能重置自己的密码，请到「账号」页修改', 'error');
      return;
    }
    if (!window.confirm(`重置「${userLabel(user)}」的密码？该用户当前的登录会立即失效。`)) return;

    setBusy(true);
    try {
      const password = await resetPassword(user.userId);
      setNewPassword(password);
      showToast('已生成新密码，请立即转交该用户', 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const copy = async (text: string): Promise<void> => {
    try {
      await navigator.clipboard.writeText(text);
      showToast('已复制', 'success');
    } catch {
      showToast('复制失败，请手动选中复制', 'error');
    }
  };

  const entries = () =>
    users().map((user) => ({
      key: user.userId,
      label: userLabel(user),
      sub: `${user.userName}${user.isAdmin ? ' · 管理员' : ''}${user.isBuiltIn ? ' · 内置' : ''}`,
    }));

  return (
    <SettingsPane
      title="用户"
      entries={entries()}
      activeKey={activeId()}
      emptyText="还没有用户。"
      busy={busy()}
      onSelect={(key) => {
        setActiveId(key);
        setNewPassword('');
      }}
    >
      <Show when={active()} fallback={<div class="settings-list-empty">选择左侧用户查看详情。</div>}>
        {(user) => (
          <>
            <div class="field-grid">
              <TextField label="登录名" value={user().userName} readOnly onInput={() => undefined} />
              <TextField
                label="角色"
                value={user().isAdmin ? '管理员' : '普通用户'}
                readOnly
                onInput={() => undefined}
              />
              <TextField
                label="用户标识"
                value={user().userId}
                readOnly
                hint="工作区与会话都归属到这个标识，注册后不可变"
                onInput={() => undefined}
              />
              <TextField
                label="最后登录"
                value={formatServerDateTime(user().lastLoginAt) || '未登录过'}
                readOnly
                onInput={() => undefined}
              />
              <Show when={newPassword()}>
                <TextField
                  label="新密码"
                  value={newPassword()}
                  readOnly
                  wide
                  hint="只显示这一次，离开本页后无法再取回"
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
                    ? '不能重置自己的密码，请到「账号」页修改'
                    : user().isBuiltIn
                      ? '内置管理员只能由本人修改密码'
                      : undefined
                }
              >
                重置密码
              </button>
              <Show when={newPassword()}>
                <button class="btn" onClick={() => void copy(newPassword())}>
                  复制新密码
                </button>
              </Show>
            </FormActions>
            <Show when={user().isBuiltIn}>
              <div class="field-hint" style={{ 'margin-top': '10px' }}>
                内置管理员是最上层账号，密码只能由本人在「账号」页修改，管理员之间也不能互相重置。
              </div>
            </Show>
          </>
        )}
      </Show>
    </SettingsPane>
  );
}