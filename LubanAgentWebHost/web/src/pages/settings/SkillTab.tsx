import { createEffect, createSignal, Show } from 'solid-js';

import { resetAgent, settingsApi } from '../../lib/settings';
import type { SkillFile, SkillSummary } from '../../lib/types';
import { showToast } from '../../stores/app';
import { AreaField, FormActions, SettingsPane, TextField } from './Shared';
import type { SettingsEntry } from './Shared';
import { t } from '../../i18n';

const empty = (): SkillFile => ({ id: '', name: '', description: '', category: '', triggers: '', body: '' });

/** 未填分类的技能归到这一组，并排在最后。 */
const UNCATEGORIZED = '\u0000uncategorized';

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
        group: group === UNCATEGORIZED ? t('skill.uncategorized') : group,
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
    reader.onerror = () => reject(reader.error ?? new Error(t('plugins.fileReadFailed')));
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
  /* 作用域切换或快速点选时，只让最后一次请求的结果落地。 */
  let loadSeq = 0;

  const reload = async (keep?: string | null) => {
    const seq = ++loadSeq;
    try {
      const list = await settingsApi.listSkills(props.scope);
      if (seq !== loadSeq) return;
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
      if (seq !== loadSeq) return;
      showToast((error as Error).message, 'error');
    }
  };

  const open = async (key: string) => {
    const seq = ++loadSeq;
    try {
      const data = await settingsApi.readSkill(props.scope, key);
      if (seq !== loadSeq) return;
      setActive(key);
      setForm({ ...empty(), ...data, id: key });
    } catch (error) {
      if (seq !== loadSeq) return;
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
      showToast(t('skill.nameRequired'), 'error');
      return;
    }
    if (!form().description.trim()) {
      // 与桌面端一致：description 供模型选择技能时匹配，不能为空
      showToast(t('skill.descriptionRequired'), 'error');
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
      showToast(t('skill.saved'), 'success');
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
    if (!window.confirm(t('skill.deleteConfirm', { name: label, key }))) return;

    setBusy(true);
    try {
      await settingsApi.deleteSkill(props.scope, key);
      await resetAgent();
      showToast(t('skill.deleted'), 'success');
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
      const parts = [t('skill.imported', { count: imported })];
      if (skipped > 0) parts.push(t('skill.importSkipped', { count: skipped }));
      if (ignored > 0) parts.push(t('skill.importIgnored', { count: ignored }));
      showToast(parts.join(t('common.listSeparator')), imported > 0 ? 'success' : 'error');
      await reload(result?.imported?.[0] ?? null);
    } catch (error) {
      showToast((error as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <SettingsPane
      title={t('skill.title')}
      entries={groupByCategory(items())}
      activeKey={active()}
      emptyText={t('skill.empty')}
      createLabel={t('common.new')}
      headActions={
        <>
          <button class="btn ghost" onClick={() => fileInput?.click()} disabled={busy()}>
            {t('skill.importZip')}
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
          label={t('common.name')}
          value={form().name}
          hint={active() ? t('skill.nameHintEdit', { key: active() ?? '' }) : t('skill.nameHintNew')}
          onInput={(v) => patch('name', v)}
        />
        <TextField label={t('skill.category')} value={form().category} hint={t('skill.categoryHint')} onInput={(v) => patch('category', v)} />
        <TextField label={t('common.description')} value={form().description} wide hint={t('skill.descriptionHint')} onInput={(v) => patch('description', v)} />
        <TextField
          label={t('skill.triggers')}
          value={form().triggers}
          hint={t('skill.triggersHint')}
          wide
          onInput={(v) => patch('triggers', v)}
        />
        <AreaField
          label={t('skill.body')}
          value={form().body}
          rows={10}
          wide
          mono
          placeholder={t('skill.bodyPlaceholder')}
          onInput={(v) => patch('body', v)}
        />
      </div>
      <FormActions>
        <button class="btn primary" onClick={() => void save()} disabled={busy()}>
          {t('common.save')}
        </button>
        <Show when={active()}>
          <button class="btn danger" onClick={() => void remove()} disabled={busy()}>
            {t('common.delete')}
          </button>
        </Show>
      </FormActions>
    </SettingsPane>
  );
}