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

/**
 * 会话行的时间标签。
 *
 * 今天组按时长递进（秒 / 分 / 小时），更早组按日历阶梯递进（昨天 → 天 → 周 → 月 → 年）。
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

  if (bucket === 'today') {
    const diff = Math.max(0, now - date.getTime());
    if (diff < 60_000) return `${Math.max(1, Math.floor(diff / 1000))}秒前`;
    if (diff < 3_600_000) return `${Math.floor(diff / 60_000)}分钟前`;
    return `${Math.floor(diff / 3_600_000)}小时前`;
  }

  const current = new Date(now);
  const days = calendarDaysBetween(date, current);
  if (days <= 1) return '昨天';
  if (days === 2) return '前天';
  if (days < 7) return `${days}天前`;
  if (days < 30) return `${Math.floor(days / 7)}周前`;

  const months = calendarMonthsBetween(date, current);
  if (months < 6) return `${Math.max(1, months)}个月前`;
  if (months < 12) return months === 6 ? '半年前' : `${months}个月前`;
  return `${Math.floor(months / 12)}年前`;
}

/** 会话行 time 悬浮提示用的绝对时间。 */
export function formatAbsoluteTime(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return `${date.toLocaleDateString('zh-CN')} ${formatTime(value)}`;
}

export function formatTime(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
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
 * 对话消息时间：今天只显示时分，昨天 / 前天加中文前缀，更早显示 `YYYY-MM-DD HH:mm`。
 * 日期按本机日历日比较，避免「23 小时前」与「昨天」互相矛盾。
 */
export function formatMessageTime(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const time = formatTime(value);
  const offset = calendarDayOffset(date);
  if (offset === 0) return time;
  if (offset === 1) return `昨天 ${time}`;
  if (offset === 2) return `前天 ${time}`;
  return `${localDateText(date)} ${time}`;
}

/**
 * 把毫秒耗时格式化为紧凑中文：`<1 秒`、`43 秒`、`4 分 43 秒`、`1 小时 2 分`。
 * 缺失或非正数返回空串，便于调用方直接条件渲染。
 */
export function formatDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) return '';
  if (!Number.isFinite(ms) || ms <= 0) return '';
  const totalSeconds = Math.round(ms / 1000);
  if (totalSeconds < 1) return '<1 秒';
  if (totalSeconds < 60) return `${totalSeconds} 秒`;
  const totalMinutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (totalMinutes < 60) return seconds > 0 ? `${totalMinutes} 分 ${seconds} 秒` : `${totalMinutes} 分`;
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  return minutes > 0 ? `${hours} 小时 ${minutes} 分` : `${hours} 小时`;
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

  if (diff < minute) return '刚刚';
  if (diff < hour) return `${Math.floor(diff / minute)} 分钟前`;
  if (diff < day) return `${Math.floor(diff / hour)} 小时前`;
  if (diff < 7 * day) return `${Math.floor(diff / day)} 天前`;
  return date.toLocaleDateString('zh-CN', { month: '2-digit', day: '2-digit' });
}

export function formatBytes(bytes: number | null | undefined): string {
  if (!bytes || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value >= 10 || unit === 0 ? Math.round(value) : value.toFixed(1)} ${units[unit]}`;
}

/**
 * 紧凑 token 数：满 1 万按万计并保留一位小数（250571 → 25.1万），
 * 小数位为 0 时省掉（70000 → 7万）；不足 1 万显示原值。
 */
export function formatTokenCompact(tokens: number): string {
  if (!Number.isFinite(tokens) || tokens <= 0) return '0';
  if (tokens < 10_000) return `${Math.round(tokens)}`;
  const value = (tokens / 10_000).toFixed(1);
  return `${value.endsWith('.0') ? value.slice(0, -2) : value}万`;
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