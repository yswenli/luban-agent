import { get, post } from './api';

/** 白名单根目录下的一个子目录。 */
export interface WorkspaceBrowseEntry {
  name: string;
  /** 相对根目录的路径，使用 `/` 分隔。 */
  path: string;
  /** 是否已经是 git 仓库。 */
  hasGit: boolean;
}

/** 根目录内的只读浏览结果。 */
export interface WorkspaceBrowse {
  root: string;
  /** 当前目录相对根目录的路径；根目录本身为空串。 */
  path: string;
  /** 上一级相对路径；已在根目录时为 null。 */
  parent: string | null;
  entries: WorkspaceBrowseEntry[];
}

/** HTTPS 凭据（不含密码）。 */
export interface GitCredentialInput {
  userName?: string | null;
  secret?: string | null;
}

/** 仓库地址预检结果。 */
export interface GitVerifyResult {
  ok: boolean;
  kind: string;
  host: string;
  port: number;
  /** git-auth / git-not-found / git-network / git-timeout / git-unknown / git-ssh-unsupported */
  reason?: string | null;
  message?: string | null;
}

/** 某个仓库地址对应的凭据状态。 */
export interface GitCredentialStatus {
  kind: string;
  host: string;
  port: number;
  saved: boolean;
  userName?: string | null;
}

/** 设置页的凭据列表项。 */
export interface GitCredential {
  id: number;
  userId: string;
  /** 归属用户展示名（管理员视图用）。 */
  owner: string;
  scheme: string;
  host: string;
  port: number;
  gitUserName: string;
  updatedAt?: string | null;
}

/** 在白名单根目录内浏览子目录。 */
export function browseWorkspace(root: string, path: string): Promise<WorkspaceBrowse> {
  return get<WorkspaceBrowse>('/workspace/browse', { root, path });
}

/** 预检仓库地址（地址 / 网络 / 凭据）。始终 200，用 `ok` 表达成败。 */
export function verifyGitRepository(gitUrl: string, credential?: GitCredentialInput): Promise<GitVerifyResult> {
  return post<GitVerifyResult>('/workspace/verifyGit', {
    gitUrl,
    gitCredential: credential?.userName || credential?.secret ? credential : null,
  });
}

/** 查询某个仓库地址对应的已保存凭据（只回用户名）。 */
export function getGitCredentialStatus(gitUrl: string): Promise<GitCredentialStatus> {
  return get<GitCredentialStatus>('/workspace/gitCredential', { gitUrl });
}

/** 列出凭据：普通用户仅自己，管理员为全部。 */
export async function listGitCredentials(): Promise<GitCredential[]> {
  return (await get<GitCredential[]>('/workspace/gitCredentials')) ?? [];
}

/** 保存一条 HTTPS 凭据（域名可带端口）。 */
export function saveGitCredential(body: {
  host: string;
  userName: string;
  secret: string;
}): Promise<GitCredential> {
  return post<GitCredential>('/workspace/saveGitCredential', body);
}

/** 删除凭据：按主键，或按仓库地址删当前用户该域名的。 */
export function deleteGitCredential(body: { id?: number; gitUrl?: string }): Promise<{ deleted: boolean }> {
  return post<{ deleted: boolean }>('/workspace/deleteGitCredential', body);
}