import { createEffect, createSignal, Show } from 'solid-js';

import { resetAgent, settingsApi } from '../../lib/settings';
import type { SkillFile, SkillSummary } from '../../lib/types';
import { showToast } from '../../stores/app';
import { AreaField, FormActions, SettingsPane, TextField } from './Shared';
import type { SettingsEntry } from './Shared';

const empty = (): SkillFile => ({ id: '', name: '', description: '', category: '', triggers: '', body: '' });

/** 未填分类的技能归到这一组，并排在最后。 */
const UNCATEGORIZED = '未分类';

/** 按分类分组；组内保持列表接口返回的顺序（按显示名），组间按分类名，未分类垫底。 */
function groupByCategory(items: SkillSummary[]): SettingsEntry[] {
  const groups = new Map<string, SkillSummary[]>();
  for (const item of items) {
    const key = item.category.trim() || UNCATEGORIZED;
    const list = groups.get(key);
    if (list) list.push(item);
    else groups.set(key, [item]);
  }

  return [...groups.keys()]
    .sort((a, b) => (a === UNCATEGORIZED ? 1 : b === UNCATEGORIZED ? -1 : a.localeCompare(b)))
    .flatMap((group) =>
      groups.get(group)!.map((item) => ({
        key: item.id,
        label: item.name,
        sub: item.id === item.name ? undefined : item.id,
        group,
      })),
    );
}

/** 读文件为 Base64（不含 DataURL 前缀）。 */
function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const text = String(reader.result ?? '');
      const index = text.indexOf(',');
      resolve(index >= 0 ? text.slice(index + 1) : text);
    };
    reader.onerror = () => reject(reader.error ?? new Error('读取文件失败'));
    reader.readAsDataURL(file);
  });
}

/**
 * 技能页：文件型条目（`SKILL.md`）。
 *
 * 与桌面端一致，不列内置与 config 自定义技能，也不提供启停；保存后按需重建 Agent。
 * 左侧按分类分组，主文本是 frontmatter 的 name，次要小字是目录名。
 */
export function SkillTab(props: { scope: string }) {
  const [items, setItems] = createSignal<SkillSummary[]>([]);
  const [active, setActive] = createSignal<string | null>(null);
  const [form, setForm] = createSignal<SkillFile>(empty());
  const [busy, setBusy] = createSignal(false);
  let fileInput: HTMLInputElement | undefined;

  const reload = async (keep?: string | null) => {
    try {
      const list = await settingsApi.listSkills(props.scope);
      setItems(list);
      const next = keep ?? active();
      if (next && list.some((item) => item.id === next)) {
        await open(next);
      } else if (list.length > 0) {
        await open(list[0].id);
      } else {
        setActive(null);
        setForm(empty());
      }
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  const open = async (key: string) => {
    try {
      const data = await settingsApi.readSkill(props.scope, key);
      setActive(key);
      setForm({ ...empty(), ...data, id: key });
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  };

  createEffect(() => {
    props.scope;
    setActive(null);
    setForm(empty());
    void reload(null);
  });

  const patch = (key: keyof SkillFile, value: string) => setForm((current) => ({ ...current, [key]: value }));

  const save = async () => {
    if (!form().name.trim()) {
      showToast('技能的 name 不能为空', 'error');
      return;
    }
    if (!form().description.trim()) {
      // 与桌面端一致：description 供模型选择技能时匹配，不能为空
      showToast('技能的 description 不能为空', 'error');
      return;
    }

    setBusy(true);
    try {
      const current = form();
      const result = await settingsApi.saveSkill({
        scope: props.scope,
        originalId: active(),
        name: current.name.trim(),
        description: current.description,
        category: current.category,
        triggers: current.triggers,
        body: current.body,
      });
      await resetAgent();
      showToast('技能已保存', 'success');
      await reload(result?.key ?? active());
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    const key = active();
    if (!key) return;
    const label = form().name || key;
    if (!window.confirm(`删除技能「${label}」（目录 ${key}）？该操作不可撤销。`)) return;

    setBusy(true);
    try {
      await settingsApi.deleteSkill(props.scope, key);
      await resetAgent();
      showToast('技能已删除', 'success');
      setActive(null);
      setForm(empty());
      await reload(null);
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const importZip = async (file: File) => {
    setBusy(true);
    try {
      const result = await settingsApi.importSkills({
        scope: props.scope,
        fileName: file.name,
        contentBase64: await fileToBase64(file),
      });
      await resetAgent();
      const imported = result?.imported?.length ?? 0;
      const skipped = result?.skipped?.length ?? 0;
      const ignored = result?.ignored?.length ?? 0;
      const parts = [`已导入 ${imported} 个技能`];
      if (skipped > 0) parts.push(`跳过 ${skipped} 个条目`);
      if (ignored > 0) parts.push(`${ignored} 个非 SKILL.md 文件未导入（仅支持单文件技能）`);
      showToast(parts.join('，'), imported > 0 ? 'success' : 'error');
      await reload(result?.imported?.[0] ?? null);
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <SettingsPane
      title="技能"
      entries={groupByCategory(items())}
      activeKey={active()}
      emptyText="该作用域下还没有文件型技能。"
      createLabel="新建"
      headActions={
        <>
          <button class="btn ghost" onClick={() => fileInput?.click()} disabled={busy()}>
            导入 ZIP
          </button>
          <input
            ref={(el) => (fileInput = el)}
            type="file"
            accept=".zip,application/zip"
            style={{ display: 'none' }}
            onChange={(event) => {
              const file = event.currentTarget.files?.[0];
              event.currentTarget.value = '';
              if (file) void importZip(file);
            }}
          />
        </>
      }
      busy={busy()}
      onSelect={(key) => void open(key)}
      onCreate={() => {
        setActive(null);
        setForm(empty());
      }}
    >
      <div class="field-grid">
        <TextField
          label="名称"
          value={form().name}
          hint={active() ? `目录名 ${active()}（保存不移动目录）` : '新建时目录名按名称生成'}
          onInput={(v) => patch('name', v)}
        />
        <TextField label="分类" value={form().category} hint="左侧按此分组" onInput={(v) => patch('category', v)} />
        <TextField label="描述" value={form().description} wide hint="必填，模型据此判断是否选用该技能" onInput={(v) => patch('description', v)} />
        <TextField
          label="触发词"
          value={form().triggers}
          hint="逗号分隔"
          wide
          onInput={(v) => patch('triggers', v)}
        />
        <AreaField
          label="正文"
          value={form().body}
          rows={10}
          wide
          mono
          placeholder="Markdown 正文；写清适用场景与步骤。"
          onInput={(v) => patch('body', v)}
        />
      </div>
      <FormActions>
        <button class="btn primary" onClick={() => void save()} disabled={busy()}>
          保存
        </button>
        <Show when={active()}>
          <button class="btn danger" onClick={() => void remove()} disabled={busy()}>
            删除
          </button>
        </Show>
      </FormActions>
    </SettingsPane>
  );
}