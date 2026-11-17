import { createSignal, For, Show } from 'solid-js';

import { ConfirmActionDialog } from '../../components/Dialog';
import { Icon } from '../../components/Icons';
import { pluginStateLabel } from '../../lib/plugins';
import type { PluginItem } from '../../lib/types';
import { showToast } from '../../stores/app';

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
      showToast('请选择 .zip 格式的插件包', 'error');
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
          <span class="tag">内置</span>
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
              启用
            </button>
          }
        >
          <button class="btn" disabled={props.busy} onClick={() => setConfirm('disable')}>
            <Icon name="stop" size={15} />
            停用
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
          title={props.plugin.isBuiltin ? '内置插件不支持升级' : '选择新版本 zip 覆盖安装'}
          onClick={() => upgradeInput.click()}
        >
          <Icon name="arrow-up" size={15} />
          升级
        </button>
        <button
          class="btn danger"
          disabled={props.busy || props.plugin.isBuiltin}
          title={props.plugin.isBuiltin ? '内置插件不支持卸载' : '删除插件目录与状态记录'}
          onClick={() => setConfirm('uninstall')}
        >
          <Icon name="trash" size={15} />
          卸载
        </button>
      </div>

      <Show when={props.plugin.requiresRestart}>
        <div class="notice warn">
          <Icon name="alert" size={16} />
          <span>有程序集尚未释放，改动需重启宿主后生效。</span>
        </div>
      </Show>

      <Show when={props.plugin.lastError}>
        <div class="notice error">
          <Icon name="alert" size={16} />
          <span>上次启用失败：{props.plugin.lastError}</span>
        </div>
      </Show>

      <Show when={props.plugin.pageError}>
        <div class="notice warn">
          <Icon name="alert" size={16} />
          <span>页面镜像写入失败，技能与 MCP 不受影响：{props.plugin.pageError}</span>
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
          <span class="plugin-meta-label">标识</span>
          <span class="mono">{props.plugin.id}</span>
        </div>
        <div class="plugin-meta-row">
          <span class="plugin-meta-label">版本</span>
          <span class="mono">{props.plugin.version}</span>
        </div>
        <div class="plugin-meta-row">
          <span class="plugin-meta-label">作者</span>
          <span>{props.plugin.author || '—'}</span>
        </div>
        <div class="plugin-meta-row">
          <span class="plugin-meta-label">状态</span>
          <span>{pluginStateLabel(props.plugin.state)}</span>
        </div>
      </div>

      <div class="plugin-section">
        <div class="section-label">技能（{props.plugin.skills.length}）</div>
        <Show
          when={props.plugin.skills.length > 0}
          fallback={<span class="faint">该插件不提供技能。</span>}
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
        <div class="section-label">MCP 服务（{props.plugin.mcpServers.length}）</div>
        <Show
          when={props.plugin.mcpServers.length > 0}
          fallback={<span class="faint">该插件不提供 MCP 服务。</span>}
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
        <div class="section-label">页面入口（{props.plugin.pages.length}）</div>
        <Show
          when={props.plugin.pages.length > 0}
          fallback={<span class="faint">该插件没有自带页面，启用后不会出现在左侧栏。</span>}
        >
          <For each={props.plugin.pages}>
            {(page) => (
              <div class="plugin-row">
                <span class="plugin-row-name">{page.label || page.entryId}</span>
                <span class="mono faint">{page.webPage}</span>
                <Show
                  when={page.url}
                  fallback={<span class="tag">启用后可访问</span>}
                >
                  <a class="plugin-page-link" href={page.url} target="_blank" rel="noreferrer">
                    打开
                  </a>
                </Show>
              </div>
            )}
          </For>
        </Show>
      </div>

      <p class="plugin-note">
        插件目录与状态文件与桌面端隔离。插件贡献的技能会写成当前工作区文件，桌面端打开同一工作区也会看到；MCP
        只在当前 Web 进程内生效。
      </p>

      <Show when={confirm() === 'disable'}>
        <ConfirmActionDialog
          title="停用插件"
          message={`停用「${props.plugin.name || props.plugin.id}」会注销它注册的技能与 MCP，并移除左侧栏页面入口。`}
          confirmText="停用"
          onClose={() => setConfirm(null)}
          onConfirm={() => {
            setConfirm(null);
            props.onDisable();
          }}
        />
      </Show>

      <Show when={confirm() === 'uninstall'}>
        <ConfirmActionDialog
          title="卸载插件"
          message={`删除「${props.plugin.name || props.plugin.id}」的插件目录与状态记录，该操作不可撤销。若程序集仍被宿主占用，残留文件会在下次启动宿主时自动清理。`}
          confirmText="卸载"
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