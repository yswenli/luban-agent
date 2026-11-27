import { localeTag, t } from '../i18n';

/** 本机日期键，用于「今天 / 更早」分组。 */
export function localDayKey(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

export function isToday(value: string | null | undefined): boolean {
  return localDayKey(value) === localDayKey(new Date().toISOString());
}

/** 会话列表分组：今天 / 更早。 */
export type DayBucket = 'today' | 'earlier';

export function dayBucket(value: string | null | undefined): DayBucket {
  const key = localDayKey(value);
  if (!key) return 'earlier';
  return key === localDayKey(new Date().toISOString()) ? 'today' : 'earlier';
}

/** 整日差，按本机日历日计算，避免「23 小时」与「昨天」互相矛盾。 */
function calendarDaysBetween(from: Date, to: Date): number {
  const a = Date.UTC(from.getFullYear(), from.getMonth(), from.getDate());
  const b = Date.UTC(to.getFullYear(), to.getMonth(), to.getDate());
  return Math.max(0, Math.round((b - a) / 86_400_000));
}

/** 整月差，日不足当月则少算一个月。 */
function calendarMonthsBetween(from: Date, to: Date): number {
  let months = (to.getFullYear() - from.getFullYear()) * 12 + (to.getMonth() - from.getMonth());
  if (to.getDate() < from.getDate()) months -= 1;
  return Math.max(0, months);
}

/** 相对时间格式化器；`numeric: 'auto'` 让「昨天」这类词替掉「1 天前」。 */
function relativeFormat(): Intl.RelativeTimeFormat {
  return new Intl.RelativeTimeFormat(localeTag(), { numeric: 'auto' });
}

/**
 * 会话行的时间标签。
 *
 * 今天组按时长递进（秒 / 分 / 小时），更早组按日历阶梯递进（昨天 → 天 → 周 → 月 → 年）。
 * 交给 `Intl.RelativeTimeFormat` 生成译文，不再手写「N 天前」这类拼接。
 * `now` 由调用方传入，便于列表定时刷新同一批标签。
 */
export function formatSessionTime(
  value: string | null | undefined,
  bucket: DayBucket,
  now: number = Date.now(),
): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';

  const rtf = relativeFormat();

  if (bucket === 'today') {
    const diff = Math.max(0, now - date.getTime());
    if (diff < 60_000) return rtf.format(-Math.max(1, Math.floor(diff / 1000)), 'second');
    if (diff < 3_600_000) return rtf.format(-Math.floor(diff / 60_000), 'minute');
    return rtf.format(-Math.floor(diff / 3_600_000), 'hour');
  }

  const current = new Date(now);
  const days = calendarDaysBetween(date, current);
  if (days <= 1) return rtf.format(-1, 'day');
  if (days < 7) return rtf.format(-days, 'day');
  if (days < 30) return rtf.format(-Math.floor(days / 7), 'week');

  const months = calendarMonthsBetween(date, current);
  if (months < 12) return rtf.format(-Math.max(1, months), 'month');
  return rtf.format(-Math.floor(months / 12), 'year');
}

/** 会话行 time 悬浮提示用的绝对时间。 */
export function formatAbsoluteTime(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return `${date.toLocaleDateString(localeTag())} ${formatTime(value)}`;
}

export function formatTime(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleTimeString(localeTag(), { hour: '2-digit', minute: '2-digit' });
}

/** 本机日历日差（今天 = 0，昨天 = 1）；跨月跨年也按日历日算，不受时区偏移影响。 */
function calendarDayOffset(date: Date): number {
  const now = new Date();
  const target = Date.UTC(date.getFullYear(), date.getMonth(), date.getDate());
  const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  return Math.round((today - target) / 86_400_000);
}

/** 本机日期键 `YYYY-MM-DD`（消息时间只用日期部分，不带时区信息）。 */
function localDateText(date: Date): string {
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/**
 * 对话消息时间：今天只显示时分，昨天 / 前天加对应前缀，更早显示 `YYYY-MM-DD HH:mm`。
 * 日期按本机日历日比较，避免「23 小时前」与「昨天」互相矛盾。
 */
export function formatMessageTime(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const time = formatTime(value);
  const offset = calendarDayOffset(date);
  if (offset === 0) return time;
  if (offset === 1) return t('format.yesterdayAt', { time });
  if (offset === 2) return t('format.dayBeforeYesterdayAt', { time });
  return `${localDateText(date)} ${time}`;
}

/** 单位数字格式化（`4 min` / `4 分`），由 Intl 负责译文与复数。 */
function unitValue(value: number, unit: Intl.NumberFormatOptions['unit']): string {
  return new Intl.NumberFormat(localeTag(), {
    style: 'unit',
    unit,
    unitDisplay: 'narrow',
  }).format(value);
}

/** 天数（邀请码有效期下拉）：长单位形式，由 Intl 处理单复数与语序（`1 day` / `7 days`）。 */
export function formatDayCount(days: number): string {
  return new Intl.NumberFormat(localeTag(), {
    style: 'unit',
    unit: 'day',
    unitDisplay: 'long',
  }).format(days);
}

/**
 * 把毫秒耗时格式化为紧凑文案：`<1 秒`、`43 秒`、`4 分 43 秒`、`1 小时 2 分`。
 * 缺失或非正数返回空串，便于调用方直接条件渲染。
 */
export function formatDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) return '';
  if (!Number.isFinite(ms) || ms <= 0) return '';
  const totalSeconds = Math.round(ms / 1000);
  if (totalSeconds < 1) return t('format.duration.lessThanSecond');
  if (totalSeconds < 60) return unitValue(totalSeconds, 'second');
  const totalMinutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (totalMinutes < 60) {
    const parts = [unitValue(totalMinutes, 'minute')];
    if (seconds > 0) parts.push(unitValue(seconds, 'second'));
    return parts.join(t('format.duration.partsSeparator'));
  }
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  const parts = [unitValue(hours, 'hour')];
  if (minutes > 0) parts.push(unitValue(minutes, 'minute'));
  return parts.join(t('format.duration.partsSeparator'));
}

/**
 * 紧凑单位耗时：`50s`、`20m 30s`、`1h 45m 30s`、`1d 18h 30m 10s`。
 * 缺失或非正数返回空串，便于调用方条件渲染；不足 1 秒按 1 秒计。
 */
export function formatCompactDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) return '';
  if (!Number.isFinite(ms) || ms <= 0) return '';
  const totalSeconds = Math.max(1, Math.floor(ms / 1000));
  const days = Math.floor(totalSeconds / 86_400);
  const hours = Math.floor((totalSeconds % 86_400) / 3_600);
  const minutes = Math.floor((totalSeconds % 3_600) / 60);
  const seconds = totalSeconds % 60;
  const parts: string[] = [];
  if (days > 0) parts.push(`${days}d`);
  if (hours > 0) parts.push(`${hours}h`);
  if (minutes > 0) parts.push(`${minutes}m`);
  if (seconds > 0 || parts.length === 0) parts.push(`${seconds}s`);
  return parts.join(' ');
}

export function formatRelative(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';

  const diff = Date.now() - date.getTime();
  const minute = 60_000;
  const hour = 60 * minute;
  const day = 24 * hour;

  const rtf = relativeFormat();
  if (diff < minute) return t('format.justNow');
  if (diff < hour) return rtf.format(-Math.floor(diff / minute), 'minute');
  if (diff < day) return rtf.format(-Math.floor(diff / hour), 'hour');
  if (diff < 7 * day) return rtf.format(-Math.floor(diff / day), 'day');
  return date.toLocaleDateString(localeTag(), { month: '2-digit', day: '2-digit' });
}

/** 文件大小；小数位交给 Intl，千分位与小数点跟着语言走。 */
export function formatBytes(bytes: number | null | undefined): string {
  if (!bytes || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  const text = new Intl.NumberFormat(localeTag(), {
    maximumFractionDigits: unit === 0 || value >= 10 ? 0 : 1,
  }).format(value);
  return `${text} ${units[unit]}`;
}

/**
 * 紧凑 token 数：按语言选用万 / K / M 等量级（250571 → 25.1万 / 251K）。
 */
export function formatTokenCompact(tokens: number): string {
  if (!Number.isFinite(tokens) || tokens <= 0) return '0';
  return new Intl.NumberFormat(localeTag(), {
    notation: 'compact',
    maximumFractionDigits: 1,
  }).format(tokens);
}

/** 普通整数分组显示，随当前界面语言变化。 */
export function formatNumber(value: number): string {
  return new Intl.NumberFormat(localeTag()).format(value);
}

/** 文件扩展名（小写，含点）；无扩展名返回空串。 */
export function extensionOf(fileName: string): string {
  const index = fileName.lastIndexOf('.');
  return index <= 0 ? '' : fileName.slice(index).toLowerCase();
}

/** 附件是否按图片展示预览。 */
export function isImageMediaType(mediaType: string | null | undefined): boolean {
  return !!mediaType && mediaType.startsWith('image/');
}

/** 截断长文本，保留尾部省略号。 */
export function truncate(text: string, max: number): string {
  const value = text.trim();
  return value.length <= max ? value : `${value.slice(0, max - 1)}…`;
}

/**
 * 解析服务端的 UTC 时间戳。
 *
 * 数据库取回的 UTC 时间会丢掉 Kind，序列化后不带时区后缀，直接 `new Date(value)` 会按本地时间
 * 解读（东八区就差 8 小时）。无后缀的值在此补 `Z`，带后缀的原样解析。
 */
export function parseServerUtc(value: string | null | undefined): Date | null {
  if (!value) return null;
  const normalized = /(?:[zZ]|[+-]\d{2}:?\d{2})$/.test(value) ? value : `${value}Z`;
  const date = new Date(normalized);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** UTC 时间戳 → 本机日期时间；无效值返回空串。 */
export function formatServerDateTime(value: string | null | undefined): string {
  const date = parseServerUtc(value);
  return date ? date.toLocaleString(localeTag()) : '';
}