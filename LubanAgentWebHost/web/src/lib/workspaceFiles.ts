import { get } from './api';

/** 工作区目录列举结果里的一项：路径统一用 `/` 分隔、相对工作区根。 */
export interface WorkspaceEntry {
  name: string;
  path: string;
  type: 'dir' | 'file';
  size: number | null;
  /** 最近修改时间（UTC，ISO 字符串）。 */
  modified: string;
}

/** `/file/list` 的响应：一次只回一层子项，`truncated` 表示超过单目录上限被截断。 */
export interface WorkspaceListing {
  workspaceId: string;
  path: string;
  entries: WorkspaceEntry[];
  truncated: boolean;
}

/**
 * 列举工作区内某个目录的直接子项。
 *
 * `showIgnored` 为假时后端会跳过 `.git`、`node_modules`、`bin`、`obj` 等目录；
 * `workspaceId` 必传，后端按它定位项目根目录。
 */
export function listWorkspaceDir(
  workspaceId: string | null | undefined,
  path: string,
  showIgnored: boolean,
): Promise<WorkspaceListing> {
  return get<WorkspaceListing>('/file/list', {
    workspaceId: workspaceId ?? undefined,
    path: path || undefined,
    showIgnored: showIgnored ? 1 : undefined,
  });
}

/** 取扩展名（小写，含点）；无扩展名返回空串。 */
export function fileExtension(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(dot).toLowerCase() : '';
}

/** 文件图标里的短标签：md → MD、dockerfile → DOC、无扩展名 → ·。 */
export function fileGlyph(name: string): string {
  const extension = fileExtension(name);
  if (!extension) return '·';
  return extension.slice(1).toUpperCase().slice(0, 3);
}

/**
 * 文件图标的配色分组。
 *
 * 只用于视觉区分，不影响任何行为：配色按「文档 / 代码 / 配置 / 图片 / 音视频」分五类。
 */
export function fileKindClass(name: string): string {
  const extension = fileExtension(name);
  if (['.md', '.markdown', '.txt', '.rst', '.adoc'].includes(extension)) return 'doc';
  if (['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp', '.ico', '.svg'].includes(extension)) return 'image';
  if (['.mp4', '.m4v', '.webm', '.ogv', '.mov', '.mp3', '.wav', '.ogg', '.oga', '.opus', '.m4a', '.aac', '.flac'].includes(extension)) {
    return 'media';
  }
  if (
    ['.json', '.jsonc', '.xml', '.yaml', '.yml', '.toml', '.ini', '.conf', '.cfg', '.properties', '.env'].includes(extension)
  ) {
    return 'config';
  }
  if (
    [
      '.cs', '.csproj', '.sln', '.slnx', '.props', '.targets', '.cshtml', '.razor', '.vb', '.fs', '.fsproj',
      '.ts', '.tsx', '.js', '.jsx', '.mjs', '.cjs', '.css', '.scss', '.less', '.html', '.htm',
      '.vue', '.svelte', '.axaml', '.xaml', '.ps1', '.psm1', '.sh', '.bash', '.bat', '.cmd', '.sql',
      '.py', '.java', '.kt', '.kts', '.go', '.rs', '.c', '.h', '.cpp', '.hpp', '.cc',
      '.m', '.swift', '.dart', '.rb', '.php', '.lua', '.r', '.pl', '.scala', '.gradle',
    ].includes(extension)
  ) {
    return 'code';
  }
  return 'other';
}