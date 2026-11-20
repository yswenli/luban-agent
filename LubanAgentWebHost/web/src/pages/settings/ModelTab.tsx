import { createEffect, createSignal, For, Show } from 'solid-js';

import { Modal } from '../../components/Dialog';
import { resetAgent, settingsApi } from '../../lib/settings';
import type { ModelItem, ModelProfile, ProviderListItem } from '../../lib/types';
import { selectModel, selection, showToast } from '../../stores/app';
import { FormActions, SettingsPane, TextField } from './Shared';

/** 标准链路能发出的思考档位，顺序即弹窗里的勾选顺序。 */
const SENDABLE_EFFORTS = ['None', 'Low', 'Medium', 'High', 'ExtraHigh'] as const;

/** 档位显示名；与对话页保持一致。 */
const EFFORT_LABELS: Record<string, string> = {
  None: '不思考',
  Low: '低',
  Medium: '中',
  High: '高',
  ExtraHigh: '极高',
};

/**
 * 模型页：按供应商维护自定义模型，并把某个模型设为「新会话默认模型」。
 *
 * 单个会话实际使用哪个模型在对话页切换；这里只决定没单独选过模型的会话跟随哪个默认。
 * 列表只读本机清单；「刷新」会向远端拉取（有 API Key 时才真正发请求）。
 */
export function ModelTab() {
  const [providers, setProviders] = createSignal<ProviderListItem[]>([]);
  const [active, setActive] = createSignal<string | null>(null);
  const [models, setModels] = createSignal<ModelItem[]>([]);
  const [newModel, setNewModel] = createSignal('');
  const [busy, setBusy] = createSignal(false);

  /** 能力档案弹窗：目标模型 + 服务端回读结果 + 编辑中的档位与窗口。 */
  const [profileTarget, setProfileTarget] = createSignal<ModelItem | null>(null);
  const [profile, setProfile] = createSignal<ModelProfile | null>(null);
  const [profileMode, setProfileMode] = createSignal<'unknown' | 'none' | 'some'>('unknown');
  const [profileEfforts, setProfileEfforts] = createSignal<string[]>([]);
  const [profileWindow, setProfileWindow] = createSignal('');
  const [profileBusy, setProfileBusy] = createSignal(false);

  const providerLabel = (name: string) => providers().find((item) => item.name === name)?.displayName ?? name;

  // 切供应商与首次加载会并发发请求：只认最后一次，且迟到的响应不得写回已被切走的供应商。
  let loadSeq = 0;

  const loadModels = async (providerId: string) => {
    const seq = ++loadSeq;
    const stale = () => seq !== loadSeq || providerId !== active();

    const group = (await settingsApi.listModels(providerId)).find((item) => item.providerId === providerId);
    if (stale()) return;
    if (group && group.models.length > 0) {
      setModels(group.models);
      return;
    }

    // 本机清单只有自定义模型；内置/远端模型来自进程内缓存，缓存未建立时先拉一次。
    const refreshed = await settingsApi.refreshModels(providerId);
    if (stale()) return;
    const custom = new Set(providers().find((item) => item.name === providerId)?.customModels ?? []);
    const current = selection();
    setModels(
      (refreshed?.models ?? []).map((modelId) => ({
        modelId,
        isCustom: custom.has(modelId),
        isSelected: current?.providerId === providerId && current?.modelId === modelId,
      })),
    );
  };

  const reload = async (keep?: string | null) => {
    try {
      const list = await settingsApi.listProviders();
      setProviders(list);
      // 已经选中的供应商优先：首次加载期间的异步回填不能把用户刚点的供应商覆盖掉。
      const preferred = active() ?? keep ?? null;
      const next =
        preferred && list.some((item) => item.name === preferred) ? preferred : (list[0]?.name ?? null);
      setActive(next);
      if (next) {
        await loadModels(next);
      } else {
        setModels([]);
      }
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  createEffect(() => {
    void reload(null);
  });

  const select = async (providerId: string) => {
    setActive(providerId);
    try {
      await loadModels(providerId);
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  const refresh = async () => {
    const providerId = active();
    if (!providerId) return;

    setBusy(true);
    try {
      await settingsApi.refreshModels(providerId);
      await loadModels(providerId);
      showToast('模型列表已刷新', 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const setCurrent = async (modelId: string) => {
    const providerId = active();
    if (!providerId) return;

    await selectModel(providerId, modelId);
    try {
      await loadModels(providerId);
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  const add = async () => {
    const providerId = active();
    const modelId = newModel().trim();
    if (!providerId || !modelId) return;

    setBusy(true);
    try {
      await settingsApi.addModel(providerId, modelId);
      setNewModel('');
      await loadModels(providerId);
      showToast('已添加自定义模型', 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const rename = async (model: ModelItem) => {
    const providerId = active();
    if (!providerId) return;

    const next = window.prompt(`把模型「${model.modelId}」重命名为：`, model.modelId);
    if (!next || next.trim() === model.modelId) return;

    setBusy(true);
    try {
      await settingsApi.renameModel(providerId, model.modelId, next.trim());
      await loadModels(providerId);
      showToast('模型已重命名', 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const remove = async (model: ModelItem) => {
    const providerId = active();
    if (!providerId) return;
    if (!window.confirm(`删除自定义模型「${model.modelId}」？`)) return;

    setBusy(true);
    try {
      await settingsApi.deleteModel(providerId, model.modelId);
      await loadModels(providerId);
      showToast('模型已删除', 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  /** 回读结果同步到编辑态：`null`=未知、空数组=明确不支持、非空=可选档位。 */
  const applyProfile = (info: ModelProfile) => {
    setProfile(info);
    const efforts = info.reasoningEfforts;
    setProfileMode(efforts == null ? 'unknown' : efforts.length === 0 ? 'none' : 'some');
    setProfileEfforts([...(efforts ?? [])]);
    setProfileWindow(info.contextWindow != null ? String(info.contextWindow) : '');
  };

  const openProfile = async (model: ModelItem) => {
    const providerId = active();
    if (!providerId) return;

    setProfileTarget(model);
    setProfile(null);
    setProfileMode('unknown');
    setProfileEfforts([]);
    setProfileWindow('');
    setProfileBusy(true);
    try {
      const info = await settingsApi.getModelProfile(providerId, model.modelId);
      applyProfile(info);
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setProfileBusy(false);
    }
  };

  const toggleEffort = (name: string) => {
    setProfileEfforts((list) => (list.includes(name) ? list.filter((item) => item !== name) : [...list, name]));
  };

  /** 按三态把勾选结果翻译成请求字段：未知=null、明确不支持=[]、指定档位=勾选集合。 */
  const effortsForSave = (): string[] | null => {
    switch (profileMode()) {
      case 'unknown':
        return null;
      case 'none':
        return [];
      default:
        return profileEfforts();
    }
  };

  const saveProfile = async (reset: boolean) => {
    const providerId = active();
    const target = profileTarget();
    if (!providerId || !target) return;

    if (!reset && profileMode() === 'some' && profileEfforts().length === 0) {
      showToast('已选「指定档位」但一项都没勾；要表示不支持请选「不支持思考」', 'error');
      return;
    }

    const windowText = profileWindow().trim();
    const window = windowText === '' ? null : Number(windowText);
    if (!reset) {
      if (window !== null && (!Number.isInteger(window) || window <= 0)) {
        showToast('上下文窗口必须是正整数（留空表示未知）', 'error');
        return;
      }
    }

    setProfileBusy(true);
    try {
      const info = await settingsApi.saveModelProfile({
        providerId,
        modelId: target.modelId,
        reasoningEfforts: reset ? undefined : effortsForSave(),
        contextWindow: reset ? undefined : window,
        reset,
      });
      applyProfile(info);
      // 配置已落盘；重建 Agent 让下一次对话立刻按新档位下发
      await resetAgent();
      showToast(reset ? '已恢复内置默认' : '能力档案已保存', 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setProfileBusy(false);
    }
  };

  return (
    <SettingsPane
      title="模型"
      items={providers().map((item) => item.name)}
      activeKey={active()}
      emptyText="还没有配置任何供应商，先在「供应商」页签添加。"
      busy={busy()}
      onSelect={(key) => void select(key)}
    >
      <Show
        when={active()}
        fallback={<div class="settings-list-empty">请选择一个供应商。</div>}
      >
        <div class="settings-form-head">
          <span class="field-label">{providerLabel(active()!)}</span>
          <span class="spacer" />
          <button class="btn ghost" onClick={() => void refresh()} disabled={busy()}>
            刷新
          </button>
        </div>

        <div class="field-hint">
          这里选的是「新会话默认模型」；已单独选过模型的会话不受影响，单个会话的模型在对话页底部切换。
        </div>

        <Show when={models().length > 0} fallback={<div class="settings-list-empty">该供应商暂无模型。</div>}>
          <div class="model-list">
            <For each={models()}>
              {(model) => (
                <div class="model-row" classList={{ current: model.isSelected }}>
                  <span class="model-row-name">{model.modelId}</span>
                  <Show when={model.isCustom}>
                    <span class="tag">自定义</span>
                  </Show>
                  <span class="spacer" />
                  <button class="btn ghost" onClick={() => void openProfile(model)} disabled={busy() || profileBusy()}>
                    能力
                  </button>
                  <Show
                    when={!model.isSelected}
                    fallback={<span class="tag current">新会话默认</span>}
                  >
                    <button class="btn ghost" onClick={() => void setCurrent(model.modelId)} disabled={busy()}>
                      设为新会话默认
                    </button>
                  </Show>
                  <Show when={model.isCustom}>
                    <button class="btn ghost" onClick={() => void rename(model)} disabled={busy()}>
                      重命名
                    </button>
                    <button class="btn ghost" onClick={() => void remove(model)} disabled={busy()}>
                      删除
                    </button>
                  </Show>
                </div>
              )}
            </For>
          </div>
        </Show>

        <div class="field-grid" style={{ 'margin-top': '16px' }}>
          <TextField
            label="新增自定义模型"
            value={newModel()}
            wide
            placeholder="模型标识，如 qwen3.8-max"
            onInput={setNewModel}
          />
        </div>
        <FormActions>
          <button class="btn primary" onClick={() => void add()} disabled={busy() || !newModel().trim()}>
            添加
          </button>
        </FormActions>
      </Show>

      <Show when={profileTarget()}>
        {(target) => (
          <Modal
            title="模型能力"
            subtitle={`${providerLabel(active() ?? '')} / ${target().modelId}`}
            onClose={() => setProfileTarget(null)}
            footer={
              <>
                <button
                  class="btn ghost"
                  disabled={profileBusy() || !profile()?.hasOverride}
                  onClick={() => void saveProfile(true)}
                >
                  恢复内置默认
                </button>
                <span class="spacer" />
                <button class="btn ghost" onClick={() => setProfileTarget(null)}>
                  关闭
                </button>
                <button class="btn primary" disabled={profileBusy() || !profile()} onClick={() => void saveProfile(false)}>
                  {profileBusy() ? '保存中…' : '保存'}
                </button>
              </>
            }
          >
            <div class="form-stack">
              <div class="field">
                <div class="section-label">思考档位</div>
                <div class="field-hint">
                  三态互斥：「未知」不登记，对话页只给「跟随模型」；「不支持思考」明确不显示档位控件；
                  「指定档位」按勾选下发。
                </div>
                <div class="field-grid">
                  <label class="check-field">
                    <input
                      type="radio"
                      name="profile-mode"
                      checked={profileMode() === 'unknown'}
                      onChange={() => setProfileMode('unknown')}
                    />
                    <span>未知（不登记）</span>
                  </label>
                  <label class="check-field">
                    <input
                      type="radio"
                      name="profile-mode"
                      checked={profileMode() === 'none'}
                      onChange={() => setProfileMode('none')}
                    />
                    <span>不支持思考</span>
                  </label>
                  <label class="check-field">
                    <input
                      type="radio"
                      name="profile-mode"
                      checked={profileMode() === 'some'}
                      onChange={() => setProfileMode('some')}
                    />
                    <span>指定档位</span>
                  </label>
                </div>

                <Show when={profileMode() === 'some'}>
                  <div class="field-grid">
                    <For each={SENDABLE_EFFORTS}>
                      {(name) => (
                        <label class="check-field">
                          <input
                            type="checkbox"
                            checked={profileEfforts().includes(name)}
                            onChange={() => toggleEffort(name)}
                          />
                          <span>{EFFORT_LABELS[name] ?? name}</span>
                          <span class="faint mono">{name}</span>
                        </label>
                      )}
                    </For>
                  </div>
                </Show>
              </div>
              <div class="field">
                <div class="section-label">上下文窗口（token）</div>
                <input
                  class="text-input mono"
                  type="number"
                  min="1"
                  placeholder="留空表示未知"
                  value={profileWindow()}
                  onInput={(event) => setProfileWindow(event.currentTarget.value)}
                />
                <div class="field-hint">
                  {profile()?.contextTiers?.length
                    ? `按窗口派生的上下文预算档位：${profile()!.contextTiers.join(' / ')}`
                    : '窗口未知时对话页不显示上下文控件。'}
                </div>
              </div>
            </div>
          </Modal>
        )}
      </Show>
    </SettingsPane>
  );
}