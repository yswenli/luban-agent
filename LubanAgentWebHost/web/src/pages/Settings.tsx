import { createMemo, createSignal, For, Match, Show, Switch } from 'solid-js';

import { Icon, type IconName } from '../components/Icons';
import { openSessions } from '../lib/router';
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

export function SettingsPage(props: { tab: string }) {
  const [search, setSearch] = createSignal('');
  const [scope, setScope] = createSignal<'global' | 'workspace'>('global');

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
      ? 'A2A 配置仅作用于工作区，请先在顶栏选择一个工作区。'
      : '尚未选择工作区，请先在会话列表顶部选择一个工作区。';

  return (
    <div class="app">
      <div class="shell">
        <div class="topbar">
          <button class="icon-btn" title="返回应用" onClick={() => openSessions()}>
            <Icon name="arrow-left" size={18} />
          </button>
          <span class="topbar-title">设置</span>
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
        </div>

        <div class="settings-layout">
          <div class="settings-nav">
            <button class="nav-item" onClick={() => openSessions()}>
              <Icon name="arrow-left" size={16} />
              返回应用
            </button>
            <div class="searchrow" style={{ margin: '10px 0 6px' }}>
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