import { createSignal, For, Show } from 'solid-js';

import { ConfirmActionDialog } from '../../components/Dialog';
import { Icon } from '../../components/Icons';
import { pluginStateLabel } from '../../lib/plugins';
import type { PluginItem } from '../../lib/types';
import { showToast } from '../../stores/app';
import { t } from '../../i18n';

/**
 * 插件详情：基本信息、贡献摘要、启停 / 升级 / 卸载。
 *
 * 内置插件只读：不可升级也不可卸载（框架会拒绝，前端提前禁用按钮省一次往返）。
 */
export function PluginDetail(props: {
  plugin: PluginItem;
  warnings: string[];
  busy: boolean;
  onEnable: () => void;
  onDisable: () => void;
  onUninstall: () => void;
  onUpgrade: (file: File) => void;
}) {
  const [confirm, setConfirm] = createSignal<'disable' | 'uninstall' | null>(null);
  let upgradeInput!: HTMLInputElement;

  const enabled = () => props.plugin.state === 'enabled';

  const pickUpgrade = (file: File | undefined) => {
    if (!file) return;
    if (!/\.zip$/i.test(file.name)) {
      showToast(t('plugins.zipRequired'), 'error');
      return;
    }
    props.onUpgrade(file);
    if (upgradeInput) upgradeInput.value = '';
  };

  return (
    <div class="plugin-detail">
      <div class="plugin-detail-head">
        <div>
          <div class="dialog-title">{props.plugin.name || props.plugin.id}</div>
          <div class="dialog-sub mono">
            {props.plugin.id} · v{props.plugin.version}
          </div>
        </div>
        <div class="spacer" />
        <span class={`plugin-state state-${props.plugin.state}`}>{pluginStateLabel(props.plugin.state)}</span>
        <Show when={props.plugin.isBuiltin}>
          <span class="tag">{t('plugins.builtin')}</span>
        </Show>
      </div>

      <Show when={props.plugin.description}>
        <p class="plugin-desc">{props.plugin.description}</p>
      </Show>

      <div class="plugin-actions">
        <Show
          when={enabled()}
          fallback={
            <button class="btn primary" disabled={props.busy} onClick={() => props.onEnable()}>
              <Icon name="check" size={15} />
              {t('plugins.enable')}
            </button>
          }
        >
          <button class="btn" disabled={props.busy} onClick={() => setConfirm('disable')}>
            <Icon name="stop" size={15} />
            {t('plugins.disable')}
          </button>
        </Show>

        <input
          ref={upgradeInput}
          type="file"
          accept=".zip,application/zip"
          style={{ display: 'none' }}
          onChange={(event) => pickUpgrade(event.currentTarget.files?.[0])}
        />
        <button
          class="btn"
          disabled={props.busy || props.plugin.isBuiltin}
          title={props.plugin.isBuiltin ? t('plugins.upgradeUnsupported') : t('plugins.upgradeHint')}
          onClick={() => upgradeInput.click()}
        >
          <Icon name="arrow-up" size={15} />
          {t('plugins.upgrade')}
        </button>
        <button
          class="btn danger"
          disabled={props.busy || props.plugin.isBuiltin}
          title={props.plugin.isBuiltin ? t('plugins.uninstallUnsupported') : t('plugins.uninstallHint')}
          onClick={() => setConfirm('uninstall')}
        >
          <Icon name="trash" size={15} />
          {t('plugins.uninstall')}
        </button>
      </div>

      <Show when={props.plugin.requiresRestart}>
        <div class="notice warn">
          <Icon name="alert" size={16} />
          <span>{t('plugins.restartNotice')}</span>
        </div>
      </Show>

      <Show when={props.plugin.lastError}>
        <div class="notice error">
          <Icon name="alert" size={16} />
          <span>{t('plugins.lastError', { message: props.plugin.lastError ?? '' })}</span>
        </div>
      </Show>

      <Show when={props.plugin.pageError}>
        <div class="notice warn">
          <Icon name="alert" size={16} />
          <span>{t('plugins.pageError', { message: props.plugin.pageError ?? '' })}</span>
        </div>
      </Show>

      <For each={props.warnings}>
        {(warning) => (
          <div class="notice warn">
            <Icon name="alert" size={16} />
            <span>{warning}</span>
          </div>
        )}
      </For>

      <div class="plugin-meta">
        <div class="plugin-meta-row">
          <span class="plugin-meta-label">{t('plugins.meta.id')}</span>
          <span class="mono">{props.plugin.id}</span>
        </div>
        <div class="plugin-meta-row">
          <span class="plugin-meta-label">{t('plugins.meta.version')}</span>
          <span class="mono">{props.plugin.version}</span>
        </div>
        <div class="plugin-meta-row">
          <span class="plugin-meta-label">{t('plugins.meta.author')}</span>
          <span>{props.plugin.author || '—'}</span>
        </div>
        <div class="plugin-meta-row">
          <span class="plugin-meta-label">{t('plugins.meta.state')}</span>
          <span>{pluginStateLabel(props.plugin.state)}</span>
        </div>
      </div>

      <div class="plugin-section">
        <div class="section-label">{t('plugins.skillsTitle', { count: props.plugin.skills.length })}</div>
        <Show
          when={props.plugin.skills.length > 0}
          fallback={<span class="faint">{t('plugins.noSkills')}</span>}
        >
          <For each={props.plugin.skills}>
            {(skill) => (
              <div class="plugin-row">
                <span class="plugin-row-name">{skill.name || skill.id}</span>
                <Show when={skill.description}>
                  <span class="faint">{skill.description}</span>
                </Show>
              </div>
            )}
          </For>
        </Show>
      </div>

      <div class="plugin-section">
        <div class="section-label">{t('plugins.mcpTitle', { count: props.plugin.mcpServers.length })}</div>
        <Show
          when={props.plugin.mcpServers.length > 0}
          fallback={<span class="faint">{t('plugins.noMcp')}</span>}
        >
          <For each={props.plugin.mcpServers}>
            {(server) => (
              <div class="plugin-row">
                <span class="plugin-row-name">{server.name}</span>
                <span class="tag">{server.transport || 'inproc'}</span>
              </div>
            )}
          </For>
        </Show>
      </div>

      <div class="plugin-section">
        <div class="section-label">{t('plugins.pagesTitle', { count: props.plugin.pages.length })}</div>
        <Show
          when={props.plugin.pages.length > 0}
          fallback={<span class="faint">{t('plugins.noPages')}</span>}
        >
          <For each={props.plugin.pages}>
            {(page) => (
              <div class="plugin-row">
                <span class="plugin-row-name">{page.label || page.entryId}</span>
                <span class="mono faint">{page.webPage}</span>
                <Show
                  when={page.url}
                  fallback={<span class="tag">{t('plugins.availableAfterEnable')}</span>}
                >
                  <a class="plugin-page-link" href={page.url} target="_blank" rel="noreferrer">
                    {t('plugins.open')}
                  </a>
                </Show>
              </div>
            )}
          </For>
        </Show>
      </div>

      <p class="plugin-note">
        {t('plugins.note')}
      </p>

      <Show when={confirm() === 'disable'}>
        <ConfirmActionDialog
          title={t('plugins.disableTitle')}
          message={t('plugins.disableMessage', { name: props.plugin.name || props.plugin.id })}
          confirmText={t('plugins.disable')}
          onClose={() => setConfirm(null)}
          onConfirm={() => {
            setConfirm(null);
            props.onDisable();
          }}
        />
      </Show>

      <Show when={confirm() === 'uninstall'}>
        <ConfirmActionDialog
          title={t('plugins.uninstallTitle')}
          message={t('plugins.uninstallMessage', { name: props.plugin.name || props.plugin.id })}
          confirmText={t('plugins.uninstall')}
          onClose={() => setConfirm(null)}
          onConfirm={() => {
            setConfirm(null);
            props.onUninstall();
          }}
        />
      </Show>
    </div>
  );
}