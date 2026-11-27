import { createEffect, createSignal, on, Show } from 'solid-js';

import { settingsApi } from '../../lib/settings';
import type { AgentsMdInfo, AgentsMdResult } from '../../lib/types';
import { bootstrap, refreshWorkspaces, showToast } from '../../stores/app';
import { AreaField, CheckField, FormActions, SelectField, TextField } from './Shared';

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
      showToast('已授权访问该目录', 'success');
    });

  const ensureConfig = () =>
    run('config', async () => {
      const id = workspaceId();
      if (!id) return;
      await settingsApi.ensureWorkspaceConfig(id);
      await refreshWorkspaces();
      showToast('配置目录已就绪', 'success');
    });

  const applyResult = (result: AgentsMdResult) => {
    setMarkdown(result.markdown);
    setVerification(result.verificationPrompt);
    setOverwrite(true);
    showToast('AGENTS.md 已写入工作区根目录', 'success');
    if (workspaceId()) void loadInfo(workspaceId());
  };

  const generate = () =>
    run('generate', async () => {
      const id = workspaceId();
      if (!id) return;
      const value = form();
      if (!value.agentName.trim() || !value.scene.trim()) {
        showToast('Agent 名称与业务场景为必填项', 'error');
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
      showToast('校验提示词已复制', 'success');
    } catch {
      showToast('浏览器拒绝访问剪贴板，请手动选中复制', 'error');
    }
  };

  return (
    <div class="settings-form">
      <div class="settings-form-head">
        <span class="field-label">工作区</span>
        <div class="spacer" />
        <Show when={info()}>
          {(data) => <span class="tag">{data().workspaceName}</span>}
        </Show>
      </div>

      <div class="field-grid">
        <TextField label="根目录" value={info()?.rootPath ?? current()?.rootPath ?? ''} readOnly wide onInput={() => {}} />
        <Show when={workspace()?.isAuthorized} fallback={<div class="notice warn"><span>目录尚未授权，Agent 无法读写该工作区。</span></div>}>
          <div class="notice ok">
            <span>目录已授权，Agent 可读写该工作区。</span>
          </div>
        </Show>
        <TextField
          label="配置目录"
          value={workspace()?.configPath ?? '（尚未生成）'}
          hint="技能、规则与 MCP 配置都存放在该目录下"
          readOnly
          onInput={() => {}}
        />
      </div>

      <FormActions>
        <button class="btn" disabled={!!busy() || !workspaceId() || !!workspace()?.isAuthorized} onClick={() => void authorize()}>
          {busy() === 'authorize' ? '授权中…' : '授权目录访问'}
        </button>
        <button class="btn ghost" disabled={!!busy() || !workspaceId()} onClick={() => void ensureConfig()}>
          {busy() === 'config' ? '生成中…' : '生成配置目录'}
        </button>
      </FormActions>

      <div class="settings-form-head" style={{ 'margin-top': '26px' }}>
        <span class="field-label">AGENTS.md 初始化</span>
        <div class="spacer" />
        <Show when={info()}>
          {(data) => (
            <span class="tag" classList={{ current: data().exists }}>
              {data().exists ? `已存在 · ${data().existingLength} 字符` : '尚未创建'}
            </span>
          )}
        </Show>
      </div>

      <div class="field-grid">
        <TextField
          label="Agent 名称"
          value={form().agentName}
          placeholder="例如：支付网关助手"
          onInput={(value) => setForm({ ...form(), agentName: value })}
        />
        <SelectField
          label="输出模式"
          value={form().outputMode}
          options={[
            { value: 'full', label: '完整版' },
            { value: 'lite', label: '精简版' },
          ]}
          onChange={(value) => setForm({ ...form(), outputMode: value })}
        />
        <AreaField
          label="业务场景"
          hint="必填：这个工作区要解决什么问题"
          rows={3}
          wide
          value={form().scene}
          onInput={(value) => setForm({ ...form(), scene: value })}
        />
        <AreaField
          label="能力清单"
          hint="选填：逗号或换行分隔"
          rows={3}
          value={form().capabilities}
          onInput={(value) => setForm({ ...form(), capabilities: value })}
        />
        <AreaField
          label="工具 / API"
          hint="选填：逗号或换行分隔"
          rows={3}
          value={form().tools}
          onInput={(value) => setForm({ ...form(), tools: value })}
        />
        <AreaField
          label="补充说明"
          hint="选填：额外约束、边界与禁止事项"
          rows={3}
          wide
          value={form().description}
          onInput={(value) => setForm({ ...form(), description: value })}
        />
      </div>

      <Show when={info()?.exists}>
        <div style={{ 'margin-top': '10px' }}>
          <CheckField
            label="覆盖已有的 AGENTS.md"
            checked={overwrite()}
            hint="不勾选时，已存在会直接拒绝生成与保存"
            onChange={setOverwrite}
          />
        </div>
      </Show>

      <Show when={info()}>
        {(data) => <div class="dialog-sub mono" style={{ 'margin-top': '10px' }}>{data().targetPath}</div>}
      </Show>

      <FormActions>
        <button class="btn primary" disabled={!!busy() || !workspaceId()} onClick={() => void generate()}>
          {busy() === 'generate' ? '生成中…' : '生成并写入'}
        </button>
        <button class="btn" disabled={!!busy() || !markdown().trim()} onClick={() => void save()}>
          {busy() === 'save' ? '保存中…' : '保存编辑内容'}
        </button>
        <Show when={verification()}>
          <button class="btn ghost" onClick={() => void copyVerification()}>
            复制校验提示词
          </button>
        </Show>
      </FormActions>

      <Show when={markdown()}>
        <AreaField
          label="生成结果（可编辑后保存）"
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