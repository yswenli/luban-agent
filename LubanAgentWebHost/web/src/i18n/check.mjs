#!/usr/bin/env node
/**
 * 语言包一致性校验。
 *
 * `zh-CN` 是键的唯一真源；本脚本在不启动 TypeScript 的前提下比对 14 个语言包，
 * 避免「编译能过但运行时缺键/漏占位符」。CI 与本地都用 `npm run i18n:check`。
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const localeDir = path.join(here, 'locales');
const SOURCE = 'zh-CN';

/** 与 `index.ts` 的 `LOCALES` 保持一致。 */
const LOCALES = [
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
];

/** 抽出一个语言包对象字面量并求值（键均带引号，值均为字符串）。 */
function load(file) {
  const full = path.join(localeDir, `${file}.ts`);
  if (!fs.existsSync(full)) throw new Error(`缺少语言包 ${file}.ts`);
  const src = fs.readFileSync(full, 'utf8');
  const open = src.indexOf('{', src.indexOf('= {'));
  const close = src.lastIndexOf('}');
  if (open < 0 || close < open) throw new Error(`${file}.ts 找不到对象字面量`);
  // eslint-disable-next-line no-new-func
  return Function(`"use strict"; return (${src.slice(open, close + 1)});`)();
}

/** 键出现次数：用于发现重复键（对象求值会静默去重）。 */
function keyCount(file) {
  const src = fs.readFileSync(path.join(localeDir, `${file}.ts`), 'utf8');
  return (src.match(/^\s{2}'[^']+':/gm) ?? []).length;
}

const placeholders = (text) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

const errors = [];
const base = load(SOURCE);
const baseKeys = Object.keys(base);

if (baseKeys.length !== keyCount(SOURCE)) {
  errors.push(`zh-CN.ts 有重复键：${baseKeys.length} 个唯一键 / ${keyCount(SOURCE)} 条定义`);
}

// 值可以是纯空格（如 `format.duration.partsSeparator`），只拦真正的空串
for (const key of baseKeys) {
  if (!base[key].length) errors.push(`zh-CN.ts 键 ${key} 的文案为空`);
}

for (const locale of LOCALES) {
  const dict = load(locale);
  const keys = Object.keys(dict);

  if (keys.length !== keyCount(locale)) {
    errors.push(`${locale}.ts 有重复键：${keys.length} 个唯一键 / ${keyCount(locale)} 条定义`);
  }
  if (keys.length !== baseKeys.length) {
    errors.push(`${locale}.ts 键数量 ${keys.length}，应为 ${baseKeys.length}`);
  }

  const missing = baseKeys.filter((key) => !(key in dict));
  const extra = keys.filter((key) => !(key in base));
  if (missing.length) errors.push(`${locale}.ts 缺 ${missing.length} 个键：${missing.slice(0, 5).join(', ')}…`);
  if (extra.length) errors.push(`${locale}.ts 多出 ${extra.length} 个键：${extra.slice(0, 5).join(', ')}…`);

  if (keys.join('\n') !== baseKeys.join('\n')) {
    const firstDiff = baseKeys.findIndex((key, i) => keys[i] !== key);
    errors.push(`${locale}.ts 键顺序与 zh-CN.ts 不一致（第 ${firstDiff + 1} 个键起）`);
  }

  for (const key of baseKeys) {
    const value = dict[key];
    if (typeof value !== 'string') continue;
    if (!value.length) errors.push(`${locale}.ts 键 ${key} 的文案为空`);
    const want = placeholders(base[key]);
    const got = placeholders(value);
    if (want.join() !== got.join()) {
      errors.push(`${locale}.ts 键 ${key} 占位符不一致：期望 {${want.join('} {')}}，实际 {${got.join('} {')}}`);
    }
  }
}

if (errors.length) {
  console.error(`i18n 校验失败（${errors.length} 项）：`);
  for (const line of errors) console.error(`  - ${line}`);
  process.exit(1);
}

console.log(`i18n 校验通过：${LOCALES.length} 个语言包，各 ${baseKeys.length} 条文案。`);