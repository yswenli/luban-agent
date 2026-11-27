import { get, post } from './api';
import type {
  IndexReport,
  RetrievalHit,
  WikiFileInfo,
  WikiLintReport,
  WikiStatus,
  Workspace,
} from './types';
import { t } from '../i18n';

/**
 * LLM Wiki 知识库接口封装。
 *
 * 知识库本质是 `type=LlmWiki` 的工作区，列表直接筛工作区；其余动作走 `/llmwiki/*`。
 * 索引与重建属长耗时同步调用，受宿主 `LubanAgentWeb:TimeoutSeconds` 约束。
 */
export const wikiApi = {
  create: (rootPath: string, name?: string): Promise<Workspace> =>
    post<Workspace>('/llmwiki/create', { rootPath, name: name?.trim() || null }),
  remove: (workspaceId: string): Promise<void> => post<void>('/llmwiki/delete', { workspaceId }),
  listFiles: async (workspaceId: string): Promise<WikiFileInfo[]> =>
    (await get<WikiFileInfo[]>('/llmwiki/listFiles', { workspaceId })) ?? [],
  index: (workspaceId: string, glob?: string): Promise<IndexReport> =>
    post<IndexReport>('/llmwiki/index', { workspaceId, glob: glob?.trim() || null }),
  search: (workspaceId: string, query: string, scope?: string): Promise<RetrievalHit[]> =>
    post<RetrievalHit[]>('/llmwiki/search', { workspaceId, query, scope: scope ?? null }),
  status: (workspaceId: string): Promise<WikiStatus> => get<WikiStatus>('/llmwiki/status', { workspaceId }),
  lint: (workspaceId: string): Promise<WikiLintReport> => post<WikiLintReport>('/llmwiki/lint', { workspaceId }),
  rebuild: (workspaceId: string): Promise<IndexReport> => post<IndexReport>('/llmwiki/rebuild', { workspaceId }),
  clear: (workspaceId: string): Promise<void> => post<void>('/llmwiki/clear', { workspaceId }),
};

/** 统计报告的一行摘要，索引与重建共用。 */
export function indexSummary(report: IndexReport | null | undefined): string {
  if (!report) return '';
  const parts = [
    t('wiki.summary.scanned', { count: report.scannedFiles }),
    t('wiki.summary.new', { count: report.newFiles }),
    t('wiki.summary.updated', { count: report.updatedFiles }),
    t('wiki.summary.skipped', { count: report.skippedFiles }),
    t('wiki.summary.deleted', { count: report.deletedFiles }),
    t('wiki.summary.chunks', { count: report.totalChunks }),
    t('wiki.summary.embedded', { count: report.embeddedChunks }),
  ];
  return parts.join(' · ');
}
