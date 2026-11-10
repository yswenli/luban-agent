import { createMemo, createSignal, For, Match, onCleanup, onMount, Show, Switch } from 'solid-js';

import { Icon, type IconName } from '../components/Icons';
import { bootstrap, currentWorkspaceId, workspaceName } from '../stores/app';
import { A2aTab } from './settings/A2aTab';
import { McpTab } from './settings/McpTab';
import { ModelTab } from './settings/ModelTab';
import { ProviderTab } from './settings/ProviderTab';
import { RuleTab } from './settings/RuleTab';
import { SkillTab } from './settings/SkillTab';

interface TabDef {
  id: string;
  label: string;
  icon: IconName;
  /** 是否显示「★ 全局 / 当前工作区」切换；供应商与模型固定全局。 */
  scoped: boolean;
  /** 只支持工作区作用域（A2A）。 */
  workspaceOnly?: boolean;
}

const tabs: TabDef[] = [
  { id: 'skill', label: '技能', icon: 'spark', scoped: true },
  { id: 'rule', label: '规则', icon: 'shield', scoped: true },
  { id: 'mcp', label: 'MCP 服务', icon: 'terminal', scoped: true },
  { id: 'a2a', label: 'A2A 协作', icon: 'message', scoped: true, workspaceOnly: true },
  { id: 'provider', label: '供应商', icon: 'folder', scoped: false },
  { id: 'model', label: '模型', icon: 'brain', scoped: false },
];

/**
 * 设置浮层。
 *
 * 覆盖在当前视图之上、按 `#/settings/<tab>` 深链切换页签；关闭即退回打开前的视图，
 * 因此不再有独立的设置整页与「返回应用」。
 */
export function SettingsOverlay(props: { tab: string; onClose: () => void }) {
  const [search, setSearch] = createSignal('');
  const [scope, setScope] = createSignal<'global' | 'workspace'>('global');

  onMount(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') props.onClose();
    };
    window.addEventListener('keydown', onKey);
    onCleanup(() => window.removeEventListener('keydown', onKey));
  });

  const activeTab = createMemo(() => tabs.find((tab) => tab.id === props.tab) ?? tabs[0]);

  const visibleTabs = createMemo(() => {
    const keyword = search().trim().toLowerCase();
    if (!keyword) return tabs;
    return tabs.filter((tab) => tab.label.toLowerCase().includes(keyword) || tab.id.includes(keyword));
  });

  const showScope = () => activeTab().scoped;

  /** 后端作用域值：`global` 或工作区标识。 */
  const scopeValue = () => (scope() === 'global' ? 'global' : currentWorkspaceId() ?? 'global');

  /** 需要工作区但当前没有：给出提示，不渲染表单。 */
  const needWorkspace = () =>
    (activeTab().workspaceOnly && scope() === 'global') ||
    (scope() === 'workspace' && !currentWorkspaceId());

  const scopeNotice = () =>
    activeTab().workspaceOnly
      ? 'A2A 配置仅作用于工作区，请先在首页左侧选择一个项目。'
      : '尚未选择项目，请先在首页左侧选择一个项目。';

  return (
    <div
      class="settings-overlay"
      onClick={(event) => {
        if (event.target === event.currentTarget) props.onClose();
      }}
    >
      <div class="settings-panel" role="dialog" aria-modal="true">
        <div class="settings-panel-head">
          <span class="dialog-title">设置</span>
          <div class="spacer" />
          <Show when={showScope()}>
            <div class="scope-switch">
              <Show when={!activeTab().workspaceOnly}>
                <button classList={{ active: scope() === 'global' }} onClick={() => setScope('global')}>
                  ★ 全局
                </button>
              </Show>
              <button classList={{ active: scope() === 'workspace' }} onClick={() => setScope('workspace')}>
                当前工作区
              </button>
            </div>
          </Show>
          <button class="icon-btn" title="关闭设置（Esc）" onClick={() => props.onClose()}>
            <Icon name="close" size={17} />
          </button>
        </div>

        <div class="settings-layout">
          <div class="settings-nav">
            <div class="searchrow" style={{ margin: '4px 0 8px' }}>
              <Icon name="search" size={15} />
              <input
                type="search"
                placeholder="搜索设置"
                value={search()}
                onInput={(event) => setSearch(event.currentTarget.value)}
                style={{ height: '34px', 'padding-left': '36px' }}
              />
            </div>
            <For each={visibleTabs()}>
              {(tab) => (
                <a class="nav-item" classList={{ active: tab.id === activeTab().id }} href={`#/settings/${tab.id}`}>
                  <Icon name={tab.icon} size={16} />
                  {tab.label}
                </a>
              )}
            </For>
          </div>

          <div class="settings-body">
            <div class="settings-head">
              <div>
                <div class="dialog-title">{activeTab().label}</div>
                <div class="dialog-sub">
                  {showScope()
                    ? scope() === 'global'
                      ? '作用域：★ 全局'
                      : `作用域：${workspaceName(currentWorkspaceId()) || '当前工作区'}`
                    : '作用域：固定全局'}
                </div>
              </div>
            </div>

            <Show
              when={!needWorkspace()}
              fallback={
                <div class="notice warn">
                  <Icon name="alert" size={16} />
                  <span>{scopeNotice()}</span>
                </div>
              }
            >
              <Switch>
                <Match when={activeTab().id === 'skill'}>
                  <SkillTab scope={scopeValue()} />
                </Match>
                <Match when={activeTab().id === 'rule'}>
                  <RuleTab scope={scopeValue()} />
                </Match>
                <Match when={activeTab().id === 'mcp'}>
                  <McpTab scope={scopeValue()} />
                </Match>
                <Match when={activeTab().id === 'a2a'}>
                  <A2aTab scope={scopeValue()} />
                </Match>
                <Match when={activeTab().id === 'provider'}>
                  <ProviderTab />
                </Match>
                <Match when={activeTab().id === 'model'}>
                  <ModelTab />
                </Match>
              </Switch>
            </Show>

            <Show when={activeTab().id === 'provider' && (bootstrap()?.providers.length ?? 0) === 0}>
              <div class="notice" style={{ 'margin-top': '12px' }}>
                <Icon name="alert" size={16} />
                <span>首屏尚未读到模型缓存；保存供应商后到「模型」页签点「刷新」即可拉取。</span>
              </div>
            </Show>
          </div>
        </div>
      </div>
    </div>
  );
}