import { createEffect, createMemo, createSignal, For, onMount, Show } from 'solid-js';

import { Icon } from '../../components/Icons';
import { t } from '../../i18n';
import { deleteGitCredential, listGitCredentials, saveGitCredential, type GitCredential } from '../../lib/workspaceGit';
import { showToast } from '../../stores/app';
import { currentUser } from '../../stores/auth';

import { FormActions, SettingsPane, TextField } from './Shared';

/** 同一域名（协议 + 主机 + 端口）下的全部凭据；普通用户只有一条，管理员可能有多条。 */
interface DomainGroup {
  key: string;
  host: string;
  port: number;
  credentials: GitCredential[];
}

function groupKey(host: string, port: number): string {
  return `https://${host}:${port}`;
}

/** 展示域名：默认 HTTPS 端口不显示，非默认端口拼在域名后。 */
function displayHost(host: string, port: number): string {
  return port === 443 ? host : `${host}:${port}`;
}

/** 按 `用户 + 协议 + 域名 + 端口` 去重；管理员视图把同一域名下不同用户的记录归到一组。 */
function groupCredentials(list: GitCredential[]): DomainGroup[] {
  const map = new Map<string, DomainGroup>();
  for (const credential of list) {
    const key = groupKey(credential.host, credential.port);
    const group = map.get(key);
    if (group) {
      group.credentials.push(credential);
    } else {
      map.set(key, {
        key,
        host: credential.host,
        port: credential.port,
        credentials: [credential],
      });
    }
  }

  return [...map.values()].sort((left, right) => left.host.localeCompare(right.host));
}

/**
 * Git 凭据页：按域名保存 HTTPS 仓库的用户名与访问令牌。
 *
 * 普通用户只看到自己的凭据；管理员看到全部用户并按域名分组，便于清理离职/过期令牌。
 * 密码只写不读：列表与详情都不回显 `secret`，重置只能删除后重新保存。
 */
export function GitCredentialsTab() {
  const [credentials, setCredentials] = createSignal<GitCredential[]>([]);
  const [activeKey, setActiveKey] = createSignal<string | null>(null);
  const [host, setHost] = createSignal('');
  const [userName, setUserName] = createSignal('');
  const [secret, setSecret] = createSignal('');
  const [busy, setBusy] = createSignal(false);

  const admin = () => currentUser()?.isAdmin === true;
  const groups = createMemo(() => groupCredentials(credentials()));
  const active = createMemo(() => groups().find((group) => group.key === activeKey()) ?? null);

  const reload = async (prefer?: string): Promise<void> => {
    try {
      const list = await listGitCredentials();
      setCredentials(list);
      const nextGroups = groupCredentials(list);
      const wanted = prefer ?? activeKey();
      if (wanted && nextGroups.some((group) => group.key === wanted)) {
        setActiveKey(wanted);
      } else {
        setActiveKey(nextGroups[0]?.key ?? null);
      }
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  onMount(() => void reload());

  // 删除最后一组后 activeKey 会指向不存在的组，这里补一次回落到第一组
  createEffect(() => {
    const list = groups();
    if (list.length > 0 && !list.some((group) => group.key === activeKey())) {
      setActiveKey(list[0].key);
    }
  });

  const entries = () =>
    groups().map((group) => ({
      key: group.key,
      label: displayHost(group.host, group.port),
      sub: admin()
        ? t('gitCred.ownerCount', { count: group.credentials.length })
        : 'HTTPS',
    }));

  /** 保存一条凭据：同一用户同一域名会被覆盖。 */
  const save = async (): Promise<void> => {
    const hostValue = host().trim();
    if (!hostValue) {
      showToast(t('gitCred.invalidHost'), 'error');
      return;
    }

    if (!userName().trim() || !secret()) {
      showToast(t('gitCred.required'), 'error');
      return;
    }

    setBusy(true);
    try {
      const saved = await saveGitCredential({
        host: hostValue,
        userName: userName().trim(),
        secret: secret(),
      });
      await reload(groupKey(saved.host, saved.port));
      setSecret('');
      showToast(t('gitCred.saved'), 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  /** 删除单条凭据：只按主键删，管理员视图下不会牵连同一域名的其他用户。 */
  const removeCredential = async (credential: GitCredential): Promise<void> => {
    const label = `${credential.gitUserName}@${displayHost(credential.host, credential.port)}`;
    if (!window.confirm(t('gitCred.deleteOneConfirm', { name: label }))) {
      return;
    }

    setBusy(true);
    try {
      await deleteGitCredential({ id: credential.id });
      await reload();
      showToast(t('gitCred.deleted'), 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  /**
   * 左列表的删除按钮：普通用户每个域名只有一条，等价于删除该条。
   *
   * 管理员不提供域名级删除（同一域名下可能有多个用户的令牌），
   * 只能逐条删，避免一次点击清掉别人的凭据。
   */
  const removeGroup = async (key: string): Promise<void> => {
    const credential = groups().find((item) => item.key === key)?.credentials[0];
    if (credential) {
      await removeCredential(credential);
    }
  };

  const updateTime = (value?: string | null): string =>
    value ? value.replace('T', ' ').slice(0, 16) : '—';

  return (
    <SettingsPane
      title={t('gitCred.title')}
      entries={entries()}
      activeKey={activeKey()}
      emptyText={t('gitCred.empty')}
      busy={busy()}
      onSelect={(key) => setActiveKey(key)}
      onDelete={admin() ? undefined : (key) => void removeGroup(key)}
    >
      <Show when={active()}>
        {(group) => (
          <>
            <div class="settings-form-head">
              <span class="field-label">{displayHost(group().host, group().port)}</span>
              <span class="field-hint">{t('gitCred.savedHint')}</span>
            </div>
            <div class="cred-list">
              <For each={group().credentials}>
                {(credential) => (
                  <div class="cred-row">
                    <Icon name="user" size={15} />
                    <div class="cred-main">
                      <span class="cred-name">{credential.gitUserName}</span>
                      <span class="cred-meta">
                        <Show when={admin()}>{credential.owner} · </Show>
                        {t('gitCred.updatedAt')} {updateTime(credential.updatedAt)}
                      </span>
                    </div>
                    <button
                      class="icon-btn"
                      title={t('common.deleteNamed', { name: displayHost(group().host, group().port) })}
                      disabled={busy()}
                      onClick={() => void removeCredential(credential)}
                    >
                      <Icon name="trash" size={14} />
                    </button>
                  </div>
                )}
              </For>
            </div>
          </>
        )}
      </Show>

      <div class="settings-form-head" style={{ 'margin-top': '18px' }}>
        <span class="field-label">{t('gitCred.add')}</span>
      </div>
      <div class="field-grid">
        <TextField
          label={t('gitCred.host')}
          value={host()}
          placeholder="git.example.com:8443"
          hint={t('gitCred.hostHint')}
          wide
          autocomplete="off"
          onInput={(value) => setHost(value)}
        />
        <TextField
          label={t('gitCred.user')}
          value={userName()}
          autocomplete="off"
          onInput={(value) => setUserName(value)}
        />
        <TextField
          label={t('gitCred.secret')}
          value={secret()}
          type="password"
          autocomplete="new-password"
          onInput={(value) => setSecret(value)}
        />
      </div>
      <FormActions>
        <button class="btn primary" disabled={busy()} onClick={() => void save()}>
          {busy() ? t('common.saving') : t('common.save')}
        </button>
      </FormActions>

      <div class="notice" style={{ 'margin-top': '12px' }}>
        <Icon name="shield" size={16} />
        <span>
          {t('gitCred.plaintextNote')}
          <Show when={admin()}> {t('gitCred.adminNote')}</Show>
        </span>
      </div>
      <div class="field-hint" style={{ 'margin-top': '8px' }}>
        {t('gitCred.dedupNote')}
      </div>
    </SettingsPane>
  );
}