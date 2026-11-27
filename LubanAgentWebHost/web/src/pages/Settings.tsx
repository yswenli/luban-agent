import { createEffect, createMemo, createSignal, For, Match, on, onCleanup, onMount, Show, Switch } from 'solid-js';

import { hasOpenModal } from '../components/Dialog';
import { Icon, type IconName } from '../components/Icons';
import { t, type MessageKey } from '../i18n';
import { bootstrap, projectWorkspaces } from '../stores/app';
import { currentUser } from '../stores/auth';
import { AccountTab } from './settings/AccountTab';
import { DataTab } from './settings/DataTab';
import { GitCredentialsTab } from './settings/GitCredentialsTab';
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
  /** 展示名走词表：模块级常量不能存已翻译的字符串，否则切换语言后列表不刷新。 */
  labelKey: MessageKey;
  icon: IconName;
  /** 是否提供「★ 全局 + 工作区」作用域下拉；供应商与模型固定全局。 */
  scoped: boolean;
  /** 只作用于工作区（授权、配置目录、AGENTS.md），没有全局作用域。 */
  requiresWorkspace?: boolean;
  /** 仅管理员可见（用户与邀请码管理）；非管理员连入口都不显示。 */
  adminOnly?: boolean;
}

// 导航顺序即用户使用频率顺序：账号在最前（登录后第一件事通常是认人/改密），
// 管理员紧接着看邀请码，供应商与模型随后，文件型配置（技能/规则/MCP）再后，
// 用户治理与数据清理收尾。
const tabs: TabDef[] = [
  { id: 'account', labelKey: 'settings.tab.account', icon: 'user', scoped: false },
  { id: 'git-credentials', labelKey: 'settings.tab.gitCredentials', icon: 'git', scoped: false },
  { id: 'invites', labelKey: 'settings.tab.invites', icon: 'key', scoped: false, adminOnly: true },
  { id: 'roots', labelKey: 'settings.tab.roots', icon: 'folder', scoped: false, adminOnly: true },
  { id: 'provider', labelKey: 'settings.tab.provider', icon: 'folder', scoped: false },
  { id: 'model', labelKey: 'settings.tab.model', icon: 'brain', scoped: false },
  { id: 'skill', labelKey: 'settings.tab.skill', icon: 'spark', scoped: true },
  { id: 'rule', labelKey: 'settings.tab.rule', icon: 'shield', scoped: true },
  { id: 'mcp', labelKey: 'settings.tab.mcp', icon: 'terminal', scoped: true },
  { id: 'workspace', labelKey: 'settings.tab.workspace', icon: 'file', scoped: false, requiresWorkspace: true },
  { id: 'notify', labelKey: 'settings.tab.notify', icon: 'bell', scoped: false },
  { id: 'users', labelKey: 'settings.tab.users', icon: 'shield', scoped: false, adminOnly: true },
  { id: 'data', labelKey: 'settings.tab.data', icon: 'grid', scoped: false },
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
    return allowedTabs().filter((tab) => {
      const label = t(tab.labelKey).toLowerCase();
      return label.includes(keyword) || tab.id.includes(keyword);
    });
  });

  /** 用户下的项目工作区（不含知识库），顺序沿用首页拖动结果。 */
  const projects = () => projectWorkspaces();

  /** 该页签是否提供「★ 全局」作用域。 */
  const hasGlobal = () => activeTab().scoped;

  // 每个页签回到自己的默认作用域：带全局的页签默认 ★ 全局，仅工作区的页签默认第一个项目
  createEffect(
    on(
      () => activeTab().id,
      () => setSelection(hasGlobal() ? 'global' : projects()[0]?.workspaceId ?? ''),
    ),
  );

  const showScope = () => activeTab().scoped || !!activeTab().requiresWorkspace;

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
    return hasGlobal() ? [{ value: 'global', label: t('settings.globalOption') }, ...list] : list;
  };

  /** 需要工作区却一个都没有：给出提示，不渲染表单。无作用域的全局页签不受此限。 */
  const needWorkspace = () => showScope() && scopeValue() === '';

  const subtitle = () => {
    if (showScope()) {
      if (scopeValue() === 'global') return t('settings.scopeGlobal');
      return scopeValue()
        ? t('settings.scopeWorkspace', { name: workspaceLabel(scopeValue()) })
        : t('settings.scopeNone');
    }
    if (activeTab().id === 'notify') return t('settings.subtitleNotify');
    if (activeTab().id === 'account') return t('settings.subtitleAccount');
    if (activeTab().id === 'git-credentials') return t('settings.subtitleGitCredentials');
    if (activeTab().id === 'users') return t('settings.subtitleUsers');
    if (activeTab().id === 'invites') return t('settings.subtitleInvites');
    if (activeTab().id === 'roots') return t('settings.subtitleRoots');
    if (activeTab().id === 'data') return t('settings.subtitleData');
    return t('settings.scopeGlobal');
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
          <span class="dialog-title">{t('settings.title')}</span>
          <div class="spacer" />
          <Show when={showScope()}>
            <label class="scope-select">
              <span>{t('settings.scope')}</span>
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
          <button class="icon-btn" title={t('settings.close')} onClick={() => props.onClose()}>
            <Icon name="close" size={17} />
          </button>
        </div>

        <div class="settings-layout">
          <div class="settings-nav">
            <div class="searchrow" style={{ margin: '4px 0 8px' }}>
              <Icon name="search" size={15} />
              <input
                type="search"
                placeholder={t('settings.search')}
                value={search()}
                onInput={(event) => setSearch(event.currentTarget.value)}
                style={{ height: '34px', 'padding-left': '36px' }}
              />
            </div>
            <For each={visibleTabs()}>
              {(tab) => (
                <a class="nav-item" classList={{ active: tab.id === activeTab().id }} href={`#/settings/${tab.id}`}>
                  <Icon name={tab.icon} size={16} />
                  {t(tab.labelKey)}
                </a>
              )}
            </For>
          </div>

          <div class="settings-body">
            <div class="settings-head">
              <div>
                <div class="dialog-title">{t(activeTab().labelKey)}</div>
                <div class="dialog-sub">{subtitle()}</div>
              </div>
            </div>

            <Show
              when={!needWorkspace()}
              fallback={
                <div class="notice warn">
                  <Icon name="alert" size={16} />
                  <span>{t('settings.noWorkspace')}</span>
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
                <Match when={activeTab().id === 'git-credentials'}>
                  <GitCredentialsTab />
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
                <span>{t('settings.noProviders')}</span>
              </div>
            </Show>
          </div>
        </div>
      </div>
    </div>
  );
}