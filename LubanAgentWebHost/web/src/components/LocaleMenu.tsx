import { createSignal, For, Show } from 'solid-js';

import { applyLocale, LOCALE_META, LOCALES, locale, t, type Locale } from '../i18n';
import { Flag } from '../i18n/flags';
import { currentUser, setAccountLanguage } from '../stores/auth';
import { showToast } from '../stores/app';

import { Icon } from './Icons';
import { menuAt, PopMenu } from './PopMenu';

/**
 * 界面语言切换入口。
 *
 * 未登录时只改本机（缓存 + `<html lang>`）；已登录时再写账号，换设备登录后自动沿用。
 * 写库失败不回滚界面语言——本机已经切了，回滚只会让人以为点击没生效。
 */
export function LocaleMenu(props: { variant?: 'icon' | 'text' }) {
  const [menu, setMenu] = createSignal<{ x: number; y: number } | null>(null);
  const text = () => props.variant === 'text';

  async function choose(next: Locale): Promise<void> {
    setMenu(null);
    if (next === locale()) return;

    applyLocale(next);
    // 账号语言是跨设备同步的唯一真源；匿名访问没有可写的账号
    if (!currentUser()) return;
    const error = await setAccountLanguage(next);
    if (error) showToast(t('language.saveFailed'), 'error');
  }

  return (
    <>
      <button
        class={text() ? 'locale-btn' : 'icon-btn'}
        classList={{ active: menu() !== null }}
        title={t('language.button')}
        aria-label={t('language.button')}
        aria-haspopup="menu"
        aria-expanded={menu() !== null}
        onClick={(event) => {
          const rect = event.currentTarget.getBoundingClientRect();
          const height = Math.min(420, LOCALES.length * 34 + 40);
          setMenu(
            menuAt(rect, {
              width: 200,
              height,
              // 两种位置都贴右边缘展开，避免菜单顶出视口
              align: 'end',
            }),
          );
        }}
      >
        <Icon name="globe" size={text() ? 16 : 17} />
        <Show when={text()}>
          <span>{LOCALE_META[locale()].label}</span>
        </Show>
      </button>

      <Show when={menu()}>
        {(position) => (
          <PopMenu
            style={{
              left: `${position().x}px`,
              top: `${position().y}px`,
              'min-width': '200px',
              'max-height': '420px',
              'overflow-y': 'auto',
            }}
            onClose={() => setMenu(null)}
          >
            <div class="menu-section">{t('language.menuTitle')}</div>
            <For each={LOCALES}>
              {(item) => (
                <button
                  class="menu-item"
                  classList={{ active: locale() === item }}
                  lang={item}
                  onClick={() => void choose(item)}
                >
                  <Flag code={LOCALE_META[item].flag} size={18} />
                  <span>{LOCALE_META[item].label}</span>
                  <Show when={locale() === item}>
                    <Icon name="check" size={14} class="menu-check" />
                  </Show>
                </button>
              )}
            </For>
          </PopMenu>
        )}
      </Show>
    </>
  );
}