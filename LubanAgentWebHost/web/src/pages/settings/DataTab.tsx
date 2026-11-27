import { createSignal, onMount, Show } from 'solid-js';

import { ConfirmActionDialog } from '../../components/Dialog';
import { Icon } from '../../components/Icons';
import { pruneSessionView } from '../../lib/router';
import { settingsApi } from '../../lib/settings';
import type { GlobalSessionStats } from '../../lib/types';
import { bootstrap, refreshSessions, sessions, showToast } from '../../stores/app';
import { dropChat } from '../../stores/chats';
import { SelectField } from './Shared';
import { formatTokenCompact } from '../../lib/format';
import { t } from '../../i18n';

/** 一张用量统计卡。 */
function StatCard(props: { label: string; value: string; hint?: string }) {
  return (
    <div class="stat-card">
      <span class="stat-value">{props.value}</span>
      <span class="stat-label">{props.label}</span>
      <Show when={props.hint}>
        <span class="stat-hint">{props.hint}</span>
      </Show>
    </div>
  );
}

/**
 * 数据与用量页：全局会话统计 + 批量清理。
 *
 * 统计按会话创建时间限定天数；清理支持「全部工作区」与单独某个工作区，
 * 宿主侧遇到运行中的会话会返回 409，页面直接透出提示。
 */
export function DataTab() {
  const [days, setDays] = createSignal('0');
  const [stats, setStats] = createSignal<GlobalSessionStats | null>(null);
  const [busy, setBusy] = createSignal<string | null>(null);
  /** 清理目标：`all` 或工作区标识。 */
  const [target, setTarget] = createSignal('all');
  const [pending, setPending] = createSignal(false);

  const workspaces = () => bootstrap()?.workspaces ?? [];

  const targetWorkspace = () =>
    target() === 'all' ? null : workspaces().find((item) => item.workspaceId === target()) ?? null;

  const load = async () => {
    try {
      const span = Number(days());
      setStats(await settingsApi.globalStats(span > 0 ? span : null));
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  onMount(() => {
    void load();
  });

  const clear = async () => {
    if (busy()) return;
    if (target() !== 'all' && !targetWorkspace()) {
      showToast(t('data.workspaceGone'), 'error');
      return;
    }
    setBusy('clear');
    try {
      const before = sessions().map((item) => item.sessionId);
      // 「全部工作区」由宿主一次清空（含知识库工作区与未绑定工作区的会话），不再逐区下发；
      // 指定工作区则只清该区，宿主按 workspaceId 过滤
      await settingsApi.clearAllSessions(
        target() === 'all' ? { allWorkspaces: true } : { workspaceId: target() },
      );
      await refreshSessions();
      // 会话实例常驻内存：销毁已删会话，避免回退或残留页面继续展示清空前的内容
      const remaining = new Set(sessions().map((item) => item.sessionId));
      for (const sessionId of before) {
        if (!remaining.has(sessionId)) dropChat(sessionId);
      }
      // 被清掉的会话若是当前打开项，退回首页；设置浮层打开时只修底层视图
      pruneSessionView(remaining);
      await load();
      showToast(t('data.cleared'), 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(null);
    }
  };

  const confirmMessage = () =>
    target() === 'all'
      ? t('data.clearConfirmAll')
      : t('data.clearConfirmWorkspace', {
          name: targetWorkspace()?.name || targetWorkspace()?.rootPath || target(),
        });

  return (
    <div class="settings-form">
      <div class="settings-form-head">
        <span class="field-label">{t('data.overview')}</span>
        <div class="spacer" />
        <select
          class="text-input"
          style={{ width: '132px' }}
          value={days()}
          onChange={(event) => {
            setDays(event.currentTarget.value);
            void load();
          }}
        >
          <option value="0">{t('data.allTime')}</option>
          <option value="7">{t('data.last7')}</option>
          <option value="30">{t('data.last30')}</option>
        </select>
        <button class="icon-btn" title={t('common.refresh')} onClick={() => void load()}>
          <Icon name="refresh" size={16} />
        </button>
      </div>

      <div class="stat-grid">
        <StatCard label={t('data.sessions')} value={String(stats()?.totalSessions ?? 0)} />
        <StatCard label={t('data.messages')} value={String(stats()?.totalMessages ?? 0)} />
        <StatCard
          label={t('data.totalTokens')}
          value={formatTokenCompact(stats()?.totalTokens ?? 0)}
          hint={stats()?.days ? t('data.daysCovered', { days: stats()!.days }) : undefined}
        />
        <StatCard label={t('data.avgDailyTokens')} value={formatTokenCompact(Math.round(stats()?.averageDailyTokens ?? 0))} />
      </div>

      <div class="settings-form-head" style={{ 'margin-top': '26px' }}>
        <span class="field-label">{t('data.cleanup')}</span>
      </div>

      <div class="field-grid">
        <SelectField
          label={t('data.scope')}
          value={target()}
          options={[
            { value: 'all', label: t('data.allWorkspaces') },
            ...workspaces().map((item) => ({ value: item.workspaceId, label: item.name || item.rootPath })),
          ]}
          hint={t('data.scopeHint')}
          onChange={setTarget}
        />
      </div>

      <div class="settings-actions">
        <button
          class="btn danger"
          disabled={!!busy() || (target() !== 'all' && !targetWorkspace())}
          onClick={() => setPending(true)}
        >
          {busy() === 'clear' ? t('data.clearing') : t('data.clearButton')}
        </button>
      </div>

      <Show when={pending()}>
        <ConfirmActionDialog
          title={t('data.clearButton')}
          message={confirmMessage()}
          onClose={() => setPending(false)}
          onConfirm={() => {
            setPending(false);
            void clear();
          }}
        />
      </Show>
    </div>
  );
}