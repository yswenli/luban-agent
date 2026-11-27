import { createSignal, onMount, Show } from 'solid-js';

import { formatServerDateTime } from '../../lib/format';
import { showToast } from '../../stores/app';
import { createInvite, deleteInvite, listInvites, revokeInvite, type InviteCode } from '../../stores/auth';
import { FormActions, SelectField, SettingsPane, TextField } from './Shared';

/** 可选有效期：与服务端 1–30 天的限制对齐，只暴露常用档位。 */
const VALID_DAYS = [1, 3, 7, 14, 30];

/**
 * 邀请码页（仅管理员）：生成、查看与作废注册邀请码。
 *
 * 邀请码一次有效（用于注册即失效），过期时间按天计算；作废只对未使用的码有意义，
 * 已使用的码保留在列表里用于追溯是谁注册的。
 */
export function InviteTab() {
  const [items, setItems] = createSignal<InviteCode[]>([]);
  const [activeCode, setActiveCode] = createSignal<string | null>(null);
  const [validDays, setValidDays] = createSignal('7');
  const [busy, setBusy] = createSignal(false);

  const active = () => items().find((item) => item.code === activeCode()) ?? null;

  const statusText = (invite: InviteCode): string =>
    invite.usedBy ? '已使用' : invite.expired ? '已过期' : '未使用';

  /** 使用者 + 使用时间；时间缺失时只显示标识，避免拖出空的分隔符。 */
  const usedText = (invite: InviteCode): string => {
    if (!invite.usedBy) return '—';
    const at = formatServerDateTime(invite.usedAt);
    return at ? `${invite.usedBy} · ${at}` : invite.usedBy;
  };

  const entries = () =>
    items().map((invite) => ({
      key: invite.code,
      label: invite.code,
      sub: invite.usedBy
        ? `${statusText(invite)} · ${invite.usedBy}`
        : invite.expired
          ? statusText(invite)
          : `${statusText(invite)} · ${formatServerDateTime(invite.expiresAt)} 过期`,
    }));

  const reload = async (keep?: string | null): Promise<void> => {
    try {
      const list = await listInvites();
      setItems(list);
      const next = keep ?? activeCode();
      const selected =
        list.find((item) => item.code === next) ??
        list.find((item) => !item.usedBy && !item.expired) ??
        list[0] ??
        null;
      setActiveCode(selected?.code ?? null);
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  onMount(() => void reload());

  const create = async (): Promise<void> => {
    setBusy(true);
    try {
      const invite = await createInvite(Number(validDays()));
      showToast(`已生成邀请码 ${invite.code}`, 'success');
      await reload(invite.code);
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const revoke = async (): Promise<void> => {
    const code = activeCode();
    if (!code) return;
    if (!window.confirm(`作废邀请码 ${code}？作废后不能再用于注册。`)) return;

    setBusy(true);
    try {
      await revokeInvite(code);
      showToast('邀请码已作废', 'success');
      await reload(null);
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  /**
   * 删除邀请码：与「作废」不同，已使用/已过期的码也能删。
   * 删除后列表不再展示，服务端保留软删除标记；已使用的记录会失去追溯入口，确认框里明确提示。
   */
  const remove = async (code: string): Promise<void> => {
    const invite = items().find((item) => item.code === code);
    if (!invite) return;

    const usedNote = invite.usedBy
      ? `该邀请码已被 ${invite.usedBy} 使用，删除后将无法追溯注册来源。`
      : '删除后不可恢复。';
    if (!window.confirm(`删除邀请码 ${invite.code}？${usedNote}`)) return;

    setBusy(true);
    try {
      await deleteInvite(invite.code);
      showToast('邀请码已删除', 'success');
      await reload(null);
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

  return (
    <SettingsPane
      title="邀请码"
      entries={entries()}
      activeKey={activeCode()}
      emptyText="还没有邀请码。点「新建」生成一个发给需要注册的人。"
      createLabel="新建"
      busy={busy()}
      onSelect={(key) => setActiveCode(key)}
      onCreate={() => setActiveCode(null)}
      onDelete={(key) => void remove(key)}
    >
      <Show
        when={active()}
        fallback={
          <>
            <div class="settings-form-head">
              <span class="field-label">生成邀请码</span>
            </div>
            <div class="field-grid">
              <SelectField
                label="有效天数"
                value={validDays()}
                options={VALID_DAYS.map((days) => ({ value: String(days), label: `${days} 天` }))}
                hint="过期后未使用的码自动失效"
                onChange={(value) => setValidDays(value)}
              />
            </div>
            <FormActions>
              <button class="btn primary" onClick={() => void create()} disabled={busy()}>
                生成邀请码
              </button>
            </FormActions>
          </>
        }
      >
        {(invite) => (
          <>
            <div class="field-grid">
              <TextField
                label="邀请码"
                value={invite().code}
                readOnly
                wide
                hint="一次性使用：注册成功后立即失效"
                onInput={() => undefined}
              />
              <TextField label="状态" value={statusText(invite())} readOnly onInput={() => undefined} />
              <TextField
                label="过期时间"
                value={formatServerDateTime(invite().expiresAt)}
                readOnly
                onInput={() => undefined}
              />
              <TextField
                label="创建者"
                value={invite().createdBy || '—'}
                readOnly
                onInput={() => undefined}
              />
              <TextField
                label="使用者"
                value={usedText(invite())}
                readOnly
                onInput={() => undefined}
              />
            </div>
            <FormActions>
              <button class="btn" onClick={() => void copy(invite().code)}>
                复制邀请码
              </button>
              <Show when={!invite().usedBy && !invite().expired}>
                <button class="btn danger" onClick={() => void revoke()} disabled={busy()}>
                  作废
                </button>
              </Show>
            </FormActions>
          </>
        )}
      </Show>
    </SettingsPane>
  );
}