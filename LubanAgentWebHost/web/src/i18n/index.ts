import { createSignal } from 'solid-js';

import az from './locales/az';
import en from './locales/en';
import es from './locales/es';
import id from './locales/id';
import it from './locales/it';
import ja from './locales/ja';
import ko from './locales/ko';
import pl from './locales/pl';
import pt from './locales/pt';
import ru from './locales/ru';
import th from './locales/th';
import tr from './locales/tr';
import zhCN, { type MessageKey } from './locales/zh-CN';
import zhTW from './locales/zh-TW';

/**
 * 界面语言。
 *
 * `zh-CN` 是键与类型的唯一真源：其它语言包缺键会编译失败。新增语言时还要同步
 * `LubanAgentCore/Protocol/SupportedLanguages.cs`（服务端白名单）与本文件 `LOCALES`。
 */
export const LOCALES = [
  'en',
  'az',
  'es',
  'id',
  'it',
  'ja',
  'ko',
  'pt',
  'pl',
  'ru',
  'th',
  'tr',
  'zh-CN',
  'zh-TW',
] as const;

export type Locale = (typeof LOCALES)[number];
export type { MessageKey };

/** 语言菜单展示用的元数据。`flag` 为 `flags.tsx` 里的图案标识。 */
export const LOCALE_META: Record<Locale, { label: string; flag: string }> = {
  en: { label: 'English', flag: 'gb' },
  az: { label: 'Azərbaycan', flag: 'az' },
  es: { label: 'Español', flag: 'es' },
  id: { label: 'Bahasa Indonesia', flag: 'id' },
  it: { label: 'Italiano', flag: 'it' },
  ja: { label: '日本語', flag: 'ja' },
  ko: { label: '한국어', flag: 'ko' },
  pt: { label: 'Português', flag: 'pt' },
  pl: { label: 'Polski', flag: 'pl' },
  ru: { label: 'Русский', flag: 'ru' },
  th: { label: 'ภาษาไทย', flag: 'th' },
  tr: { label: 'Türkçe', flag: 'tr' },
  'zh-CN': { label: '简体中文', flag: 'cn' },
  'zh-TW': { label: '繁體中文', flag: 'tw' },
};

/** 本地缓存键。首次渲染与匿名选择只认它，账号语言在登录后覆盖。 */
export const LOCALE_STORAGE_KEY = 'luban.web.locale';

/** 词表的最小形状：语义键 + 复数键（`基名.one` 这类）。 */
type Messages = Record<string, string>;

/**
 * 已完成翻译的语言包。
 *
 * 全部静态引入：体积可接受，换来切换零延迟与首屏不闪。`t()` 的兜底链
 * （当前 → 英文 → 简体中文 → 键名）保证个别漏译不会渲染出空白。
 */
const DICTIONARIES: Record<Locale, Messages> = {
  en,
  az,
  es,
  id,
  it,
  ja,
  ko,
  pt,
  pl,
  ru,
  th,
  tr,
  'zh-CN': zhCN,
  'zh-TW': zhTW,
};

/** 语言代码归一化：大小写不敏感，`zh-hant` / `zh-HK` 归到 `zh-TW`。 */
export function normalizeLocale(value: string | null | undefined): Locale | null {
  const raw = (value ?? '').trim().replace('_', '-');
  if (!raw) return null;
  if (/^zh/i.test(raw)) {
    return /hant|tw|hk|mo/i.test(raw) ? 'zh-TW' : 'zh-CN';
  }
  const base = raw.split('-')[0].toLowerCase();
  return (LOCALES as readonly string[]).includes(base) ? (base as Locale) : null;
}

/** 浏览器首选语言里第一个受支持的；一个都命中不了返回 null。 */
function matchBrowserLocale(): Locale | null {
  if (typeof navigator === 'undefined') return null;
  const list = navigator.languages?.length ? navigator.languages : [navigator.language];
  for (const item of list) {
    const hit = normalizeLocale(item);
    if (hit) return hit;
  }
  return null;
}

function readCachedLocale(): Locale | null {
  try {
    return normalizeLocale(window.localStorage.getItem(LOCALE_STORAGE_KEY));
  } catch {
    return null;
  }
}

function writeCachedLocale(value: Locale): void {
  try {
    window.localStorage.setItem(LOCALE_STORAGE_KEY, value);
  } catch {
    // 隐私模式下写不进去：本次会话仍按内存里的语言渲染
  }
}

function syncDocumentLang(value: Locale): void {
  if (typeof document !== 'undefined') {
    document.documentElement.lang = value;
  }
}

const initialLocale = readCachedLocale() ?? matchBrowserLocale() ?? 'zh-CN';
const [locale, setLocaleSignal] = createSignal<Locale>(initialLocale);

syncDocumentLang(initialLocale);

/** 当前界面语言（响应式）。 */
export { locale };

/** 浏览器 Intl 使用的语言标签；与界面语言一致。 */
export function localeTag(): string {
  return locale();
}

/** 把 `{name}` 占位符替换为参数值。 */
function interpolate(text: string, params?: Record<string, string | number>): string {
  if (!params) return text;
  let result = text;
  for (const [name, value] of Object.entries(params)) {
    result = result.split(`{${name}}`).join(String(value));
  }
  return result;
}

/**
 * 取文案。
 *
 * 缺失时按「当前语言 → 英文 → 简体中文 → 键名」回退，避免个别漏译直接显示空白。
 */
export function t(key: MessageKey, params?: Record<string, string | number>): string {
  const text = DICTIONARIES[locale()]?.[key] ?? en[key] ?? zhCN[key] ?? key;
  return interpolate(text, params);
}

/**
 * 切换到指定语言：只影响本机（界面 + 缓存 + `<html lang>`）。
 *
 * 账号持久化由调用方按登录态决定，避免 i18n 模块反向依赖 auth store。
 */
export function applyLocale(next: Locale): void {
  setLocaleSignal(next);
  writeCachedLocale(next);
  syncDocumentLang(next);
}

/**
 * 用账号里的语言覆盖本地：账号未设置（老用户）时保留浏览器检测结果，不写回账号。
 */
export function applyAccountLocale(language: string | null | undefined): void {
  const next = normalizeLocale(language);
  if (next) applyLocale(next);
}

/**
 * 首屏防闪：在登录状态返回前，用缓存的语言先渲染。
 *
 * 账号语言在 `/auth/status` 返回后由 `applyAccountLocale` 覆盖。
 */
export function preferredLocale(): Locale {
  return locale();
}