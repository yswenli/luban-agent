import { createSignal, onMount, Show } from 'solid-js';

import { ConfirmActionDialog } from '../../components/Dialog';
import { Icon } from '../../components/Icons';
import { settingsApi } from '../../lib/settings';
import type { GlobalSessionStats } from '../../lib/types';
import { bootstrap, refreshSessions, showToast } from '../../stores/app';
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
      showToast('所选工作区已不存在，请重新选择清理范围', 'error');
      return;
    }
    setBusy('clear');
    try {
      if (target() !== 'all') {
        await settingsApi.clearAllSessions(target());
      } else {
        // 接口的 workspaceId 留空只清后端当前工作区，清空全部必须逐个工作区下发
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
    target() === 'all'
      ? '确定删除全部工作区下的会话与消息？该操作不可恢复。'
      : `确定删除工作区「${targetWorkspace()?.name || targetWorkspace()?.rootPath || target()}」下的全部会话与消息？该操作不可恢复。`;

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
            ...workspaces().map((item) => ({ value: item.workspaceId, label: item.name || item.rootPath })),
          ]}
          hint="清理前请先停止正在运行的会话；「全部工作区」也包含知识库"
          onChange={setTarget}
        />
      </div>

      <div class="settings-actions">
        <button
          class="btn danger"
          disabled={!!busy() || (target() !== 'all' && !targetWorkspace())}
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