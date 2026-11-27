import { createSignal, For, onMount, Show } from 'solid-js';

import { ConfirmActionDialog, Modal } from '../../components/Dialog';
import { Icon } from '../../components/Icons';
import { pruneSessionView } from '../../lib/router';
import { settingsApi } from '../../lib/settings';
import type { GlobalSessionStats, WorkspaceCacheItem, WorkspaceCacheScan } from '../../lib/types';
import { bootstrap, refreshSessions, sessions, showToast } from '../../stores/app';
import { dropChat } from '../../stores/chats';
import { SelectField } from './Shared';
import { formatBytes, formatTokenCompact } from '../../lib/format';
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

/** 清理目标：全部工作区或单个工作区。 */
type CacheTarget = { kind: 'all' } | { kind: 'one'; item: WorkspaceCacheItem };

/**
 * 数据与用量页：全局会话统计 + 批量清理 + 工作区缓存回收。
 *
 * 统计按会话创建时间限定天数；清理支持「全部工作区」与单独某个工作区，
 * 宿主侧遇到运行中的会话会返回 409，页面直接透出提示。
 *
 * 工作区缓存只统计 `{工作区}/.luban-agent/temp`；默认不删被历史消息引用的上传附件。
 */
export function DataTab() {
  const [days, setDays] = createSignal('0');
  const [stats, setStats] = createSignal<GlobalSessionStats | null>(null);
  const [busy, setBusy] = createSignal<string | null>(null);
  /** 清理目标：`all` 或工作区标识。 */
  const [target, setTarget] = createSignal('all');
  const [pending, setPending] = createSignal(false);

  const [keepDays, setKeepDays] = createSignal('7');
  const [cache, setCache] = createSignal<WorkspaceCacheScan | null>(null);
  const [cacheBusy, setCacheBusy] = createSignal<string | null>(null);
  const [cacheTarget, setCacheTarget] = createSignal<CacheTarget | null>(null);
  const [optTemp, setOptTemp] = createSignal(true);
  const [optOrphan, setOptOrphan] = createSignal(true);
  const [optReferenced, setOptReferenced] = createSignal(false);

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

  const loadCache = async () => {
    setCacheBusy('scan');
    try {
      setCache(await settingsApi.workspaceCacheScan(Number(keepDays())));
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setCacheBusy(null);
    }
  };

  onMount(() => {
    void load();
    void loadCache();
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

  /** 打开清理对话框；不传 item 表示全部工作区。 */
  const openCacheDialog = (item?: WorkspaceCacheItem) => {
    setCacheTarget(item ? { kind: 'one', item } : { kind: 'all' });
    setOptTemp(true);
    setOptOrphan(true);
    setOptReferenced(false);
  };

  const cacheItems = () => {
    const current = cacheTarget();
    if (!current) return [];
    return current.kind === 'all' ? (cache()?.items ?? []) : [current.item];
  };

  /** 估算释放量：按当前勾选项对目标工作区求和（服务端会按保留期与实时状态复核）。 */
  const cacheEstimate = () => {
    const items = cacheItems();
    // 默认两项（临时文件 + 未引用附件）就是服务端可回收口径，直接用精确值；
    // 其余组合只能给出上限，弹窗里会提示服务端复核
    if (optTemp() && optOrphan() && !optReferenced()) {
      return items.reduce(
        (total, item) => ({
          bytes: total.bytes + item.reclaimableBytes,
          files: total.files + item.reclaimableFiles,
        }),
        { bytes: 0, files: 0 },
      );
    }

    return items.reduce(
      (total, item) => {
        const orphan = Math.max(0, item.uploadBytes - item.referencedBytes);
        const orphanFiles = Math.max(0, item.uploadFiles - item.referencedFiles);
        if (optTemp()) {
          total.bytes += item.tempBytes;
          total.files += item.tempFiles;
        }
        if (optOrphan()) {
          total.bytes += orphan;
          total.files += orphanFiles;
        }
        if (optReferenced()) {
          total.bytes += item.referencedBytes;
          total.files += item.referencedFiles;
        }
        return total;
      },
      { bytes: 0, files: 0 },
    );
  };

  /** 单个勾选项在目标工作区内的占用。 */
  const cachePartBytes = (part: 'temp' | 'orphan' | 'referenced') =>
    cacheItems().reduce((sum, item) => {
      if (part === 'temp') return sum + item.tempBytes;
      if (part === 'referenced') return sum + item.referencedBytes;
      return sum + Math.max(0, item.uploadBytes - item.referencedBytes);
    }, 0);

  const runCacheClean = async () => {
    const current = cacheTarget();
    if (!current || cacheBusy()) return;
    setCacheBusy('clean');
    try {
      const result = await settingsApi.workspaceCacheClean({
        allWorkspaces: current.kind === 'all',
        workspaceId: current.kind === 'one' ? current.item.workspaceId : null,
        keepDays: Number(keepDays()),
        includeTempFiles: optTemp(),
        includeOrphanUploads: optOrphan(),
        includeReferencedUploads: optReferenced(),
      });
      setCacheTarget(null);
      await loadCache();
      if (result.failedFiles > 0) {
        showToast(
          t('cache.cleanedPartial', {
            size: formatBytes(result.freedBytes),
            files: result.failedFiles,
          }),
          'error',
        );
      } else {
        showToast(t('cache.cleaned', { size: formatBytes(result.freedBytes) }), 'success');
      }
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setCacheBusy(null);
    }
  };

  const cacheDialogTitle = () => {
    const current = cacheTarget();
    if (!current) return '';
    return current.kind === 'all'
      ? t('cache.cleanTitleAll')
      : t('cache.cleanTitle', { name: current.item.name || current.item.rootPath });
  };

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

      <div class="settings-form-head" style={{ 'margin-top': '26px' }}>
        <span class="field-label">{t('cache.title')}</span>
        <div class="spacer" />
        <select
          class="text-input"
          style={{ width: '132px' }}
          value={keepDays()}
          onChange={(event) => {
            setKeepDays(event.currentTarget.value);
            void loadCache();
          }}
        >
          <option value="1">{t('cache.keep1')}</option>
          <option value="7">{t('cache.keep7')}</option>
          <option value="30">{t('cache.keep30')}</option>
          <option value="0">{t('cache.keepAll')}</option>
        </select>
        <button
          class="icon-btn"
          title={t('common.refresh')}
          disabled={cacheBusy() === 'clean'}
          onClick={() => void loadCache()}
        >
          <Icon name="refresh" size={16} />
        </button>
      </div>

      <p class="field-hint">{t('cache.hint')}</p>

      <Show when={cache()} fallback={<p class="field-hint">{t('common.loading')}</p>}>
        <Show
          when={cache()!.items.length > 0}
          fallback={<p class="field-hint">{t('cache.empty')}</p>}
        >
        <div class="cache-table">
          <div class="cache-row cache-head">
            <span>{t('cache.colWorkspace')}</span>
            <span>{t('cache.colTemp')}</span>
            <span>{t('cache.colOrphan')}</span>
            <span>{t('cache.colReferenced')}</span>
            <span>{t('cache.colReclaimable')}</span>
            <span />
          </div>
          <For each={cache()!.items}>
            {(item) => (
              <div class="cache-row">
                <span class="cache-name" title={item.rootPath || item.tempPath}>
                  {item.name || item.rootPath || item.workspaceId}
                </span>
                <span>{formatBytes(item.tempBytes)}</span>
                <span>{formatBytes(Math.max(0, item.uploadBytes - item.referencedBytes))}</span>
                <span>{formatBytes(item.referencedBytes)}</span>
                <span class="cache-reclaim">{formatBytes(item.reclaimableBytes)}</span>
                <span class="cache-action">
                  <button
                    class="btn ghost"
                    disabled={!!cacheBusy() || (item.tempBytes <= 0 && item.uploadBytes <= 0)}
                    onClick={() => openCacheDialog(item)}
                  >
                    {t('cache.clean')}
                  </button>
                </span>
              </div>
            )}
          </For>
        </div>

        <div class="settings-actions cache-footer">
          <span class="field-hint">
            {t('cache.total', {
              size: formatBytes(cache()?.totalBytes ?? 0),
              reclaimable: formatBytes(cache()?.reclaimableBytes ?? 0),
            })}
          </span>
          <div class="spacer" />
          <button
            class="btn danger"
            disabled={!!cacheBusy() || (cache()?.totalBytes ?? 0) <= 0}
            onClick={() => openCacheDialog()}
          >
            {cacheBusy() === 'clean' ? t('cache.cleaning') : t('cache.cleanAll')}
          </button>
        </div>
        </Show>
      </Show>

      <Show when={cacheTarget()}>
        <Modal
          title={cacheDialogTitle()}
          busy={cacheBusy() === 'clean'}
          onClose={() => setCacheTarget(null)}
          footer={
            <>
              <button class="btn ghost" disabled={cacheBusy() === 'clean'} onClick={() => setCacheTarget(null)}>
                {t('common.cancel')}
              </button>
              <button
                class="btn danger"
                disabled={
                  cacheBusy() === 'clean' || (!optTemp() && !optOrphan() && !optReferenced())
                }
                onClick={() => void runCacheClean()}
              >
                {cacheBusy() === 'clean' ? t('cache.cleaning') : t('cache.clean')}
              </button>
            </>
          }
        >
          <label class="cache-option">
            <input
              type="checkbox"
              checked={optTemp()}
              onChange={(event) => setOptTemp(event.currentTarget.checked)}
            />
            <span class="cache-option-label">{t('cache.optTemp')}</span>
            <span class="cache-option-size">{formatBytes(cachePartBytes('temp'))}</span>
          </label>
          <label class="cache-option">
            <input
              type="checkbox"
              checked={optOrphan()}
              onChange={(event) => setOptOrphan(event.currentTarget.checked)}
            />
            <span class="cache-option-label">{t('cache.optOrphan')}</span>
            <span class="cache-option-size">{formatBytes(cachePartBytes('orphan'))}</span>
          </label>
          <label class="cache-option">
            <input
              type="checkbox"
              checked={optReferenced()}
              onChange={(event) => setOptReferenced(event.currentTarget.checked)}
            />
            <span class="cache-option-label">{t('cache.optReferenced')}</span>
            <span class="cache-option-size">{formatBytes(cachePartBytes('referenced'))}</span>
          </label>
          <Show when={optReferenced()}>
            <p class="cache-warn">{t('cache.optReferencedWarn')}</p>
          </Show>
          <p class="field-hint">
            {t('cache.estimate', {
              size: formatBytes(cacheEstimate().bytes),
              files: cacheEstimate().files,
            })}
          </p>
        </Modal>
      </Show>
    </div>
  );
}