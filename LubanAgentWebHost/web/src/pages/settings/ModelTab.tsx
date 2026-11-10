import { createEffect, createSignal, For, Show } from 'solid-js';

import { settingsApi } from '../../lib/settings';
import type { ModelItem, ProviderListItem } from '../../lib/types';
import { selectModel, selection, showToast } from '../../stores/app';
import { FormActions, SettingsPane, TextField } from './Shared';

/**
 * 模型页：按供应商维护自定义模型，并把某个模型设为全局当前模型。
 *
 * 列表只读本机清单；「刷新」会向远端拉取（有 API Key 时才真正发请求）。
 */
export function ModelTab() {
  const [providers, setProviders] = createSignal<ProviderListItem[]>([]);
  const [active, setActive] = createSignal<string | null>(null);
  const [models, setModels] = createSignal<ModelItem[]>([]);
  const [newModel, setNewModel] = createSignal('');
  const [busy, setBusy] = createSignal(false);

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
                  <Show
                    when={!model.isSelected}
                    fallback={<span class="tag current">当前</span>}
                  >
                    <button class="btn ghost" onClick={() => void setCurrent(model.modelId)} disabled={busy()}>
                      设为当前
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
    </SettingsPane>
  );
}