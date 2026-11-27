import { createEffect, createMemo, createSignal, For, Match, on, onCleanup, onMount, Show, Switch } from 'solid-js';

import { hasOpenModal } from '../components/Dialog';
import { Icon, type IconName } from '../components/Icons';
import { bootstrap, projectWorkspaces } from '../stores/app';
import { currentUser } from '../stores/auth';
import { A2aTab } from './settings/A2aTab';
import { AccountTab } from './settings/AccountTab';
import { DataTab } from './settings/DataTab';
import { InviteTab } from './settings/InviteTab';
import { McpTab } from './settings/McpTab';
import { ModelTab } from './settings/ModelTab';
import { NotifyTab } from './settings/NotifyTab';
import { ProviderTab } from './settings/ProviderTab';
import { RootsTab } from './settings/RootsTab';
import { RuleTab } from './settings/RuleTab';
import { SkillTab } from './settings/SkillTab';
import { UsersTab } from './settings/UsersTab';
import { WorkspaceTab } from './settings/WorkspaceTab';

interface TabDef {
  id: string;
  label: string;
  icon: IconName;
  /** 是否提供「★ 全局 + 工作区」作用域下拉；供应商与模型固定全局。 */
  scoped: boolean;
  /** 只支持工作区作用域（A2A）。 */
  workspaceOnly?: boolean;
  /** 只作用于工作区（授权、配置目录、AGENTS.md），没有全局作用域。 */
  requiresWorkspace?: boolean;
  /** 仅管理员可见（用户与邀请码管理）；非管理员连入口都不显示。 */
  adminOnly?: boolean;
}

// 导航顺序即用户使用频率顺序：账号在最前（登录后第一件事通常是认人/改密），
// 管理员紧接着看邀请码，供应商与模型随后，文件型配置（技能/规则/MCP）再后，
// A2A 协作垫底，用户治理与数据清理收尾。
const tabs: TabDef[] = [
  { id: 'account', label: '账号', icon: 'user', scoped: false },
  { id: 'invites', label: '邀请码', icon: 'key', scoped: false, adminOnly: true },
  { id: 'roots', label: '工作区根目录', icon: 'folder', scoped: false, adminOnly: true },
  { id: 'provider', label: '供应商', icon: 'folder', scoped: false },
  { id: 'model', label: '模型', icon: 'brain', scoped: false },
  { id: 'skill', label: '技能', icon: 'spark', scoped: true },
  { id: 'rule', label: '规则', icon: 'shield', scoped: true },
  { id: 'mcp', label: 'MCP 服务', icon: 'terminal', scoped: true },
  { id: 'a2a', label: 'A2A 协作', icon: 'message', scoped: true, workspaceOnly: true },
  { id: 'workspace', label: '工作区', icon: 'file', scoped: false, requiresWorkspace: true },
  { id: 'notify', label: '通知', icon: 'bell', scoped: false },
  { id: 'users', label: '用户', icon: 'shield', scoped: false, adminOnly: true },
  { id: 'data', label: '数据与用量', icon: 'grid', scoped: false },
];

/**
 * 设置浮层。
 *
 * 覆盖在当前视图之上、按 `#/settings/<tab>` 深链切换页签；关闭即退回打开前的视图，
 * 因此不再有独立的设置整页与「返回应用」。
 */
export function SettingsOverlay(props: { tab: string; onClose: () => void }) {
  const [search, setSearch] = createSignal('');
  /** 用户在下拉里选的作用域：`global` 或工作区标识。 */
  const [selection, setSelection] = createSignal('global');

  onMount(() => {
    const onKey = (event: KeyboardEvent) => {
      // 弹窗自己处理 Esc：避免确认框和设置浮层一起关掉
      if (event.key === 'Escape' && !hasOpenModal()) props.onClose();
    };
    window.addEventListener('keydown', onKey);
    onCleanup(() => window.removeEventListener('keydown', onKey));
  });

  /** 当前身份能看到的页签：用户与邀请码管理只对管理员开放。 */
  const allowedTabs = createMemo(() => {
    const admin = currentUser()?.isAdmin ?? false;
    return admin ? tabs : tabs.filter((tab) => !tab.adminOnly);
  });

  const activeTab = createMemo(() => allowedTabs().find((tab) => tab.id === props.tab) ?? allowedTabs()[0]);

  const visibleTabs = createMemo(() => {
    const keyword = search().trim().toLowerCase();
    if (!keyword) return allowedTabs();
    return allowedTabs().filter((tab) => tab.label.toLowerCase().includes(keyword) || tab.id.includes(keyword));
  });

  /** 用户下的项目工作区（不含知识库），顺序沿用首页拖动结果。 */
  const projects = () => projectWorkspaces();

  /** 带全局作用域的页签（A2A 即使 `scoped` 也只支持工作区）。 */
  const hasGlobal = () => activeTab().scoped && !activeTab().workspaceOnly;

  // 每个页签回到自己的默认作用域：带全局的页签默认 ★ 全局，仅工作区的页签默认第一个项目
  createEffect(
    on(
      () => activeTab().id,
      () => setSelection(hasGlobal() ? 'global' : projects()[0]?.workspaceId ?? ''),
    ),
  );

  const showScope = () => activeTab().scoped || !!activeTab().workspaceOnly || !!activeTab().requiresWorkspace;

  const workspaceLabel = (workspaceId: string): string => {
    const workspace = projects().find((item) => item.workspaceId === workspaceId);
    return workspace?.name || workspace?.rootPath || workspaceId;
  };

  /** 后端作用域值：`global` 或工作区标识；工作区列表为空时返回空串。 */
  const scopeValue = (): string => {
    const chosen = selection();
    const exists = projects().some((item) => item.workspaceId === chosen);
    if (exists) return chosen;
    return hasGlobal() ? 'global' : projects()[0]?.workspaceId ?? '';
  };

  const scopeOptions = () => {
    const list = projects().map((item) => ({ value: item.workspaceId, label: item.name || item.rootPath }));
    return hasGlobal() ? [{ value: 'global', label: '★ 全局' }, ...list] : list;
  };

  /** 需要工作区却一个都没有：给出提示，不渲染表单。无作用域的全局页签不受此限。 */
  const needWorkspace = () => showScope() && scopeValue() === '';

  const subtitle = () => {
    if (showScope()) {
      if (scopeValue() === 'global') return '作用域：★ 全局';
      return scopeValue() ? `作用域：${workspaceLabel(scopeValue())}` : '作用域：未选择工作区';
    }
    if (activeTab().id === 'notify') return '保存位置：本机浏览器（不随账号同步）';
    if (activeTab().id === 'account') return '密码只保存哈希，改密后其他设备上的登录立即失效';
    if (activeTab().id === 'users') return '仅管理员可见：重置后旧密码与该用户的登录状态立即失效';
    if (activeTab().id === 'invites') return '仅管理员可见：邀请码一次性使用，注册成功后即失效';
    if (activeTab().id === 'roots') return '仅管理员可见：机器级白名单，所有用户新建项目都受此限制';
    if (activeTab().id === 'data') return '统计范围：全部会话';
    return '作用域：★ 全局';
  };

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
            <label class="scope-select">
              <span>作用域</span>
              <select
                class="text-input"
                value={scopeValue()}
                disabled={scopeOptions().length <= 1}
                onChange={(event) => setSelection(event.currentTarget.value)}
              >
                <For each={scopeOptions()}>{(item) => <option value={item.value}>{item.label}</option>}</For>
              </select>
            </label>
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
                <div class="dialog-sub">{subtitle()}</div>
              </div>
            </div>

            <Show
              when={!needWorkspace()}
              fallback={
                <div class="notice warn">
                  <Icon name="alert" size={16} />
                  <span>还没有可用的工作区：请先在首页「项目」里新建或导入一个工作区。</span>
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
                <Match when={activeTab().id === 'workspace'}>
                  <WorkspaceTab workspaceId={scopeValue()} />
                </Match>
                <Match when={activeTab().id === 'data'}>
                  <DataTab />
                </Match>
                <Match when={activeTab().id === 'notify'}>
                  <NotifyTab />
                </Match>
                <Match when={activeTab().id === 'account'}>
                  <AccountTab />
                </Match>
                <Match when={activeTab().id === 'users'}>
                  <UsersTab />
                </Match>
                <Match when={activeTab().id === 'invites'}>
                  <InviteTab />
                </Match>
                <Match when={activeTab().id === 'roots'}>
                  <RootsTab />
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