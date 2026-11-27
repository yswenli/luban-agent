import { createSignal, onMount, Show } from 'solid-js';

import { settingsApi } from '../../lib/settings';
import { showToast } from '../../stores/app';
import { FormActions, SettingsPane, TextField } from './Shared';

/**
 * 工作区根目录页（仅管理员）：配置允许创建项目的本机目录白名单。
 *
 * 配置是机器级的，所有用户共用同一份列表：新建项目（含 git 克隆）只能落在这些根目录下。
 * 只约束「在哪里新建」——从列表移除某个根目录后，已经登记的工作区仍可切换与使用。
 */
export function RootsTab() {
  const [roots, setRoots] = createSignal<string[]>([]);
  const [draft, setDraft] = createSignal('');
  const [busy, setBusy] = createSignal(false);

  const reload = async (): Promise<void> => {
    try {
      setRoots(await settingsApi.listWorkspaceRoots());
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  onMount(() => void reload());

  /** 保存整份列表；成功返回 true，失败保留原列表并提示原因。 */
  const save = async (next: string[]): Promise<boolean> => {
    setBusy(true);
    try {
      setRoots(await settingsApi.saveWorkspaceRoots(next));
      showToast('已保存工作区根目录', 'success');
      return true;
    } catch (error) {
      showToast((error as Error).message, 'error');
      await reload();
      return false;
    } finally {
      setBusy(false);
    }
  };

  const add = async (): Promise<void> => {
    const value = draft().trim();
    if (!value) return;
    if (roots().some((root) => root.toLowerCase() === value.toLowerCase())) {
      showToast('该目录已在列表中', 'error');
      return;
    }

    if (await save([...roots(), value])) {
      setDraft('');
    }
  };

  const remove = async (root: string): Promise<void> => {
    if (roots().length <= 1) {
      showToast('至少保留一个工作区根目录', 'error');
      return;
    }

    if (!window.confirm(`移除根目录 ${root}？\n已登记在该目录下的工作区仍可继续使用，但不能再在其中新建项目。`)) {
      return;
    }

    await save(roots().filter((item) => item !== root));
  };

  const entries = () => roots().map((root) => ({ key: root, label: root, sub: '允许在该目录下新建项目' }));

  return (
    <SettingsPane
      title="根目录"
      entries={entries()}
      activeKey={null}
      emptyText="还没有配置根目录；服务端会回落到 ~/.luban-agent。"
      busy={busy()}
      onSelect={() => undefined}
      onDelete={(key) => void remove(key)}
    >
      <div class="settings-form-head">
        <span class="field-label">新增根目录</span>
      </div>
      <div class="field-grid">
        <TextField
          label="绝对路径"
          value={draft()}
          placeholder="D:\WorkBench\projects"
          wide
          hint="必须填写本机绝对路径；已存在的文件不能作为根目录"
          onInput={(value) => setDraft(value)}
        />
      </div>
      <FormActions>
        <button class="btn primary" onClick={() => void add()} disabled={busy() || draft().trim().length === 0}>
          添加
        </button>
      </FormActions>

      <Show when={roots().length > 0}>
        <div class="notice" style={{ 'margin-top': '12px' }}>
          <span>
            机器级配置，所有用户共用：新建项目（指定目录与 git 克隆）都只能落在上述根目录内。
            从列表移除不会影响已登记的工作区。
          </span>
        </div>
      </Show>
    </SettingsPane>
  );
}