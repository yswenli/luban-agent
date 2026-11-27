import { createEffect, createSignal, on, Show } from 'solid-js';

import { settingsApi } from '../../lib/settings';
import type { AgentsMdInfo, AgentsMdResult } from '../../lib/types';
import { bootstrap, refreshWorkspaces, showToast } from '../../stores/app';
import { AreaField, CheckField, FormActions, SelectField, TextField } from './Shared';
import { t } from '../../i18n';

interface AgentsMdForm {
  agentName: string;
  scene: string;
  capabilities: string;
  tools: string;
  outputMode: string;
  description: string;
}

const emptyForm = (name: string): AgentsMdForm => ({
  agentName: name,
  scene: '',
  capabilities: '',
  tools: '',
  outputMode: 'full',
  description: '',
});

/** 逗号 / 顿号 / 换行都算分隔符，避免用户为一个清单纠结格式。 */
const splitList = (value: string): string[] =>
  value
    .split(/[\n,，、]/)
    .map((item) => item.trim())
    .filter((item) => item.length > 0);

/**
 * 工作区页：作用于设置页顶部下拉选中的工作区——授权、配置目录、AGENTS.md 初始化。
 *
 * AGENTS.md 的生成与落盘都在宿主侧完成（`/agentsmd/generate`），页面只负责收集选项、
 * 展示生成结果并允许编辑后保存；规范化与写入规则与桌面端共用同一套实现。
 */
export function WorkspaceTab(props: { workspaceId: string }) {
  const workspaceId = () => props.workspaceId;

  const current = () => bootstrap()?.workspaces.find((item) => item.workspaceId === workspaceId()) ?? null;

  const [info, setInfo] = createSignal<AgentsMdInfo | null>(null);
  const [form, setForm] = createSignal<AgentsMdForm>(emptyForm(''));
  const [markdown, setMarkdown] = createSignal('');
  const [verification, setVerification] = createSignal('');
  const [overwrite, setOverwrite] = createSignal(false);
  const [busy, setBusy] = createSignal<string | null>(null);

  const workspace = () => current();

  const loadInfo = async (id: string) => {
    try {
      const data = await settingsApi.agentsMdInfo(id);
      setInfo(data);
      if (!form().agentName) setForm(emptyForm(data.workspaceName));
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  // 只在工作区切换时重置表单：授权 / 生成配置目录会刷新工作区列表，不能因此清掉用户已填的内容
  createEffect(
    on(workspaceId, (id) => {
      setInfo(null);
      setMarkdown('');
      setVerification('');
      setOverwrite(false);
      setForm(emptyForm(current()?.name ?? ''));
      if (id) void loadInfo(id);
    }),
  );

  const run = async (key: string, action: () => Promise<void>) => {
    if (busy()) return;
    setBusy(key);
    try {
      await action();
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(null);
    }
  };

  const authorize = () =>
    run('authorize', async () => {
      const id = workspaceId();
      if (!id) return;
      await settingsApi.authorizeWorkspace(id);
      await refreshWorkspaces();
      showToast(t('workspace.authorizedToast'), 'success');
    });

  const ensureConfig = () =>
    run('config', async () => {
      const id = workspaceId();
      if (!id) return;
      await settingsApi.ensureWorkspaceConfig(id);
      await refreshWorkspaces();
      showToast(t('workspace.configReady'), 'success');
    });

  const applyResult = (result: AgentsMdResult) => {
    setMarkdown(result.markdown);
    setVerification(result.verificationPrompt);
    setOverwrite(true);
    showToast(t('workspace.written'), 'success');
    if (workspaceId()) void loadInfo(workspaceId());
  };

  const generate = () =>
    run('generate', async () => {
      const id = workspaceId();
      if (!id) return;
      const value = form();
      if (!value.agentName.trim() || !value.scene.trim()) {
        showToast(t('workspace.required'), 'error');
        return;
      }
      const result = await settingsApi.agentsMdGenerate({
        workspaceId: id,
        agentName: value.agentName.trim(),
        scene: value.scene.trim(),
        capabilities: splitList(value.capabilities),
        tools: splitList(value.tools),
        outputMode: value.outputMode,
        description: value.description.trim() || null,
        overwrite: overwrite(),
      });
      applyResult(result);
    });

  const save = () =>
    run('save', async () => {
      const id = workspaceId();
      if (!id || !markdown().trim()) return;
      const result = await settingsApi.agentsMdSave({
        workspaceId: id,
        markdown: markdown(),
        overwrite: overwrite(),
      });
      applyResult(result);
    });

  const copyVerification = async () => {
    if (!verification()) return;
    try {
      await navigator.clipboard.writeText(verification());
      showToast(t('workspace.verificationCopied'), 'success');
    } catch {
      showToast(t('workspace.clipboardDenied'), 'error');
    }
  };

  return (
    <div class="settings-form">
      <div class="settings-form-head">
        <span class="field-label">{t('workspace.title')}</span>
        <div class="spacer" />
        <Show when={info()}>
          {(data) => <span class="tag">{data().workspaceName}</span>}
        </Show>
      </div>

      <div class="field-grid">
        <TextField label={t('workspace.rootPath')} value={info()?.rootPath ?? current()?.rootPath ?? ''} readOnly wide onInput={() => {}} />
        <Show when={workspace()?.isAuthorized} fallback={<div class="notice warn"><span>{t('workspace.unauthorized')}</span></div>}>
          <div class="notice ok">
            <span>{t('workspace.authorized')}</span>
          </div>
        </Show>
        <TextField
          label={t('workspace.configDir')}
          value={workspace()?.configPath ?? t('workspace.notGenerated')}
          hint={t('workspace.configDirHint')}
          readOnly
          onInput={() => {}}
        />
      </div>

      <FormActions>
        <button class="btn" disabled={!!busy() || !workspaceId() || !!workspace()?.isAuthorized} onClick={() => void authorize()}>
          {busy() === 'authorize' ? t('workspace.authorizing') : t('workspace.authorize')}
        </button>
        <button class="btn ghost" disabled={!!busy() || !workspaceId()} onClick={() => void ensureConfig()}>
          {busy() === 'config' ? t('workspace.generating') : t('workspace.generateConfig')}
        </button>
      </FormActions>

      <div class="settings-form-head" style={{ 'margin-top': '26px' }}>
        <span class="field-label">{t('workspace.agentsInit')}</span>
        <div class="spacer" />
        <Show when={info()}>
          {(data) => (
            <span class="tag" classList={{ current: data().exists }}>
              {data().exists ? t('workspace.existsChars', { count: data().existingLength }) : t('workspace.notCreated')}
            </span>
          )}
        </Show>
      </div>

      <div class="field-grid">
        <TextField
          label={t('workspace.agentName')}
          value={form().agentName}
          placeholder={t('workspace.agentNamePlaceholder')}
          onInput={(value) => setForm({ ...form(), agentName: value })}
        />
        <SelectField
          label={t('workspace.outputMode')}
          value={form().outputMode}
          options={[
            { value: 'full', label: t('workspace.full') },
            { value: 'lite', label: t('workspace.lite') },
          ]}
          onChange={(value) => setForm({ ...form(), outputMode: value })}
        />
        <AreaField
          label={t('workspace.scene')}
          hint={t('workspace.sceneHint')}
          rows={3}
          wide
          value={form().scene}
          onInput={(value) => setForm({ ...form(), scene: value })}
        />
        <AreaField
          label={t('workspace.capabilities')}
          hint={t('workspace.capabilitiesHint')}
          rows={3}
          value={form().capabilities}
          onInput={(value) => setForm({ ...form(), capabilities: value })}
        />
        <AreaField
          label={t('workspace.tools')}
          hint={t('workspace.toolsHint')}
          rows={3}
          value={form().tools}
          onInput={(value) => setForm({ ...form(), tools: value })}
        />
        <AreaField
          label={t('workspace.description')}
          hint={t('workspace.descriptionHint')}
          rows={3}
          wide
          value={form().description}
          onInput={(value) => setForm({ ...form(), description: value })}
        />
      </div>

      <Show when={info()?.exists}>
        <div style={{ 'margin-top': '10px' }}>
          <CheckField
            label={t('workspace.overwrite')}
            checked={overwrite()}
            hint={t('workspace.overwriteHint')}
            onChange={setOverwrite}
          />
        </div>
      </Show>

      <Show when={info()}>
        {(data) => <div class="dialog-sub mono" style={{ 'margin-top': '10px' }}>{data().targetPath}</div>}
      </Show>

      <FormActions>
        <button class="btn primary" disabled={!!busy() || !workspaceId()} onClick={() => void generate()}>
          {busy() === 'generate' ? t('workspace.generating') : t('workspace.generateAndWrite')}
        </button>
        <button class="btn" disabled={!!busy() || !markdown().trim()} onClick={() => void save()}>
          {busy() === 'save' ? t('common.saving') : t('workspace.saveEdits')}
        </button>
        <Show when={verification()}>
          <button class="btn ghost" onClick={() => void copyVerification()}>
            {t('workspace.copyVerification')}
          </button>
        </Show>
      </FormActions>

      <Show when={markdown()}>
        <AreaField
          label={t('workspace.result')}
          rows={18}
          wide
          mono
          value={markdown()}
          onInput={setMarkdown}
        />
      </Show>
    </div>
  );
}