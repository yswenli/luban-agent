import { createSignal, Show } from 'solid-js';

import { ConfirmActionDialog, TextPromptDialog } from './Dialog';
import { Icon } from './Icons';
import { PopMenu, menuAt } from './PopMenu';
import { truncate } from '../lib/format';
import { deleteSession, renameSession } from '../stores/app';
import { dropChat } from '../stores/chats';
import { t } from '../i18n';

type RowAction =
  | { kind: 'rename'; sessionId: string; title: string }
  | { kind: 'delete'; sessionId: string; title: string };

/**
 * 会话行的「更多」菜单 + 重命名 / 删除对话框。
 *
 * 左栏二级菜单与右栏会话列表共用。浮层由调用方在页面级渲染（`render()`），
 * 不要塞进可点击的行里——否则菜单与遮罩的点击会冒泡成「打开会话」。
 */
export function createSessionActions() {
  const [menu, setMenu] = createSignal<{ x: number; y: number; sessionId: string; title: string } | null>(null);
  const [dialog, setDialog] = createSignal<RowAction | null>(null);

  function openMenu(event: MouseEvent, session: { sessionId: string; title?: string | null }): void {
    event.stopPropagation();
    event.preventDefault();
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const position = menuAt(rect, { width: 168, height: 92, align: 'end' });
    setMenu({
      ...position,
      sessionId: session.sessionId,
      // 保留原始标题（空标题为空串）：重命名框不该预填「未命名会话」这种占位文案，
      // 否则用户直接保存会把占位文案写成真标题。占位只在删除提示里补。
      title: session.title ?? '',
    });
  }

  function render() {
    return (
      <>
        <Show when={menu()}>
          {(item) => (
            <PopMenu
              style={{ left: `${item().x}px`, top: `${item().y}px`, 'min-width': '168px' }}
              onClose={() => setMenu(null)}
            >
              <button
                class="menu-item"
                onClick={() => {
                  const target = item();
                  setMenu(null);
                  setDialog({ kind: 'rename', sessionId: target.sessionId, title: target.title });
                }}
              >
                <Icon name="pencil" size={15} />
                {t('common.rename')}
              </button>
              <button
                class="menu-item"
                onClick={() => {
                  const target = item();
                  setMenu(null);
                  setDialog({ kind: 'delete', sessionId: target.sessionId, title: target.title });
                }}
              >
                <Icon name="trash" size={15} />
                {t('common.delete')}
              </button>
            </PopMenu>
          )}
        </Show>

        <Show when={dialog()?.kind === 'rename' ? (dialog() as Extract<RowAction, { kind: 'rename' }>) : null}>
          {(action) => (
            <TextPromptDialog
              title={t('sessionList.renameTitle')}
              label={t('sessionList.renameLabel')}
              initialValue={action().title}
              confirmText={t('common.save')}
              onClose={() => setDialog(null)}
              onConfirm={(value) => {
                const target = action().sessionId;
                setDialog(null);
                void renameSession(target, value);
              }}
            />
          )}
        </Show>

        <Show when={dialog()?.kind === 'delete' ? (dialog() as Extract<RowAction, { kind: 'delete' }>) : null}>
          {(action) => (
            <ConfirmActionDialog
              title={t('sessionList.deleteTitle')}
              message={t('sessionList.deleteMessage', {
                name: truncate(action().title || t('topbar.unnamedSession'), 40),
              })}
              onClose={() => setDialog(null)}
              onConfirm={() => {
                const target = action().sessionId;
                setDialog(null);
                void deleteSession(target).then((ok) => {
                  if (ok) dropChat(target);
                });
              }}
            />
          )}
        </Show>
      </>
    );
  }

  return { openMenu, render };
}
