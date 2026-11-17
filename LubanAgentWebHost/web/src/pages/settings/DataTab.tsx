import { createSignal, onMount, Show } from 'solid-js';

import { ConfirmActionDialog } from '../../components/Dialog';
import { Icon } from '../../components/Icons';
import { settingsApi } from '../../lib/settings';
import type { GlobalSessionStats } from '../../lib/types';
import { currentWorkspaceId, refreshSessions, showToast, workspaceName } from '../../stores/app';
import { SelectField } from './Shared';

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
 * 统计按会话创建时间限定天数；清理支持「全部会话」与「当前工作区」两种范围，
 * 宿主侧遇到运行中的会话会返回 409，页面直接透出提示。
 */
export function DataTab() {
  const [days, setDays] = createSignal('0');
  const [stats, setStats] = createSignal<GlobalSessionStats | null>(null);
  const [busy, setBusy] = createSignal<string | null>(null);
  const [target, setTarget] = createSignal<'all' | 'workspace'>('all');
  const [pending, setPending] = createSignal(false);

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
    setBusy('clear');
    try {
      if (target() === 'workspace') {
        const workspaceId = currentWorkspaceId();
        if (!workspaceId) {
          showToast('尚未选择工作区', 'error');
          return;
        }
        await settingsApi.clearAllSessions(workspaceId);
      } else {
        // 接口的 workspaceId 留空会退化成「当前工作区」，清空全部必须逐个工作区下发
        const list = await settingsApi.listWorkspaces();
        const failures: string[] = [];
        for (const workspace of list) {
          try {
            await settingsApi.clearAllSessions(workspace.workspaceId);
          } catch (error) {
            failures.push(workspace.name || workspace.rootPath);
          }
        }
        if (failures.length > 0) {
          await refreshSessions();
          await load();
          showToast(`有 ${failures.length} 个工作区未能清空（可能存在运行中的会话）：${failures.join('、')}`, 'error');
          return;
        }
      }
      await refreshSessions();
      await load();
      showToast('会话已清空', 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(null);
    }
  };

  const confirmMessage = () =>
    target() === 'workspace'
      ? `确定删除「${workspaceName(currentWorkspaceId()) || '当前工作区'}」下的全部会话？消息无法恢复。`
      : '确定删除全部工作区下的会话与消息？该操作不可恢复。';

  return (
    <div class="settings-form">
      <div class="settings-form-head">
        <span class="field-label">使用概览</span>
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
          <option value="0">全部时间</option>
          <option value="7">最近 7 天</option>
          <option value="30">最近 30 天</option>
        </select>
        <button class="icon-btn" title="刷新" onClick={() => void load()}>
          <Icon name="refresh" size={16} />
        </button>
      </div>

      <div class="stat-grid">
        <StatCard label="会话数" value={String(stats()?.totalSessions ?? 0)} />
        <StatCard label="消息数" value={String(stats()?.totalMessages ?? 0)} />
        <StatCard
          label="累计 Token"
          value={(stats()?.totalTokens ?? 0).toLocaleString()}
          hint={stats()?.days ? `统计覆盖 ${stats()!.days} 天` : undefined}
        />
        <StatCard label="日均 Token" value={Math.round(stats()?.averageDailyTokens ?? 0).toLocaleString()} />
      </div>

      <div class="settings-form-head" style={{ 'margin-top': '26px' }}>
        <span class="field-label">数据清理</span>
      </div>

      <div class="field-grid">
        <SelectField
          label="清理范围"
          value={target()}
          options={[
            { value: 'all', label: '全部工作区' },
            { value: 'workspace', label: '当前工作区' },
          ]}
          hint="清理前请先停止正在运行的会话"
          onChange={(value) => setTarget(value as 'all' | 'workspace')}
        />
      </div>

      <div class="settings-actions">
        <button
          class="btn danger"
          disabled={!!busy() || (target() === 'workspace' && !currentWorkspaceId())}
          onClick={() => setPending(true)}
        >
          {busy() === 'clear' ? '清理中…' : '清空会话数据'}
        </button>
      </div>

      <Show when={pending()}>
        <ConfirmActionDialog
          title="清空会话数据"
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