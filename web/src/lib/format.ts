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

export function formatTime(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
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