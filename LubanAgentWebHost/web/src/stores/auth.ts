import { createSignal } from 'solid-js';

import { get, post } from '../lib/api';

/**
 * 当前浏览器会话的用户。
 *
 * 令牌本身在 HttpOnly Cookie 里，前端拿不到；这个形状只用于展示与角色判断。
 */
export interface AuthUser {
  userId: string;
  userName: string;
  displayName?: string | null;
  isAdmin: boolean;
  isBuiltIn: boolean;
  lastLoginAt?: string | null;
  failedLoginCount?: number;
  lockedUntil?: string | null;
}

/** 登录页公开状态（`/auth/status`）。 */
export interface AuthStatus {
  initialized: boolean;
  allowRegistration: boolean;
  user: AuthUser | null;
}

const [authStatus, setAuthStatus] = createSignal<AuthStatus | null>(null);
const [authLoading, setAuthLoading] = createSignal(true);
const [authError, setAuthError] = createSignal<string | null>(null);

export { authStatus, authLoading, authError };

/** 当前登录用户；未登录为 null。 */
export function currentUser(): AuthUser | null {
  return authStatus()?.user ?? null;
}

/** 显示名优先，缺省回落到登录名。 */
export function userLabel(user: AuthUser): string {
  return user.displayName?.trim() || user.userName;
}

/**
 * 读取登录状态。
 *
 * 公开接口不走 `blockForAuth`：首次访问本来就没有凭据，401 是正常结果。
 */
export async function loadAuthStatus(): Promise<AuthStatus | null> {
  setAuthLoading(true);
  setAuthError(null);
  try {
    const status = await get<AuthStatus>('/auth/status', undefined, { blockOnAuth: false });
    const value: AuthStatus = {
      initialized: status?.initialized ?? false,
      allowRegistration: status?.allowRegistration ?? true,
      user: status?.user ?? null,
    };
    setAuthStatus(value);
    return value;
  } catch (error) {
    setAuthError((error as Error).message);
    setAuthStatus(null);
    return null;
  } finally {
    setAuthLoading(false);
  }
}

/** 登录；成功返回 null，失败返回可展示的错误文案。 */
export async function login(userName: string, password: string): Promise<string | null> {
  try {
    await post('/auth/login', { userName, password }, { blockOnAuth: false });
    return null;
  } catch (error) {
    return (error as Error).message;
  }
}

/** 注册入参（邀请码必填）。 */
export interface RegisterInput {
  userName: string;
  password: string;
  displayName?: string;
  inviteCode: string;
}

/** 注册并登录；成功返回 null，失败返回可展示的错误文案。 */
export async function register(input: RegisterInput): Promise<string | null> {
  try {
    await post('/auth/register', input, { blockOnAuth: false });
    return null;
  } catch (error) {
    return (error as Error).message;
  }
}

/** 登出：清掉登录 Cookie 后整页重载，回到登录页。 */
export async function logout(): Promise<void> {
  try {
    await post('/auth/logout', {}, { blockOnAuth: false });
  } catch {
    // 令牌本来就无效时也照样回登录页，不能让登出卡住
  }
  window.location.reload();
}

/** 修改本人密码；成功返回 null，失败返回可展示的错误文案。 */
export async function changePassword(oldPassword: string, newPassword: string): Promise<string | null> {
  try {
    await post('/auth/changePassword', { oldPassword, newPassword });
    return null;
  } catch (error) {
    return (error as Error).message;
  }
}

/** 修改本人显示名（空白回退登录名）；成功返回 null，失败返回可展示的错误文案。 */
export async function updateDisplayName(displayName: string): Promise<string | null> {
  try {
    const user = await post<AuthUser>('/auth/updateProfile', { displayName });
    const current = authStatus();
    // 就地更新会话身份，顶栏头像与名称即时生效，不必整页重载
    if (current && user) setAuthStatus({ ...current, user });
    return null;
  } catch (error) {
    return (error as Error).message;
  }
}

/** 邀请码（`/auth/invites`）。 */
export interface InviteCode {
  code: string;
  createdBy?: string | null;
  expiresAt: string;
  usedBy?: string | null;
  usedAt?: string | null;
  expired: boolean;
}

/** 邀请码列表（仅管理员）。 */
export async function listInvites(): Promise<InviteCode[]> {
  return (await get<InviteCode[]>('/auth/invites')) ?? [];
}

/** 生成邀请码（仅管理员）；`validDays` 服务端限定 1–30，缺省 7。 */
export function createInvite(validDays: number): Promise<InviteCode> {
  return post<InviteCode>('/auth/createInvite', { validDays });
}

/** 作废未使用的邀请码（仅管理员）。 */
export function revokeInvite(code: string): Promise<void> {
  return post<void>('/auth/revokeInvite', { code });
}

/** 删除邀请码（仅管理员，任意状态；软删除后不再出现在列表里）。 */
export function deleteInvite(code: string): Promise<void> {
  return post<void>('/auth/deleteInvite', { code });
}

/** 用户列表（仅管理员）。 */
export async function listUsers(): Promise<AuthUser[]> {
  return (await get<AuthUser[]>('/auth/users')) ?? [];
}

/** 重置指定用户的密码（仅管理员）；返回一次性新密码。 */
export async function resetPassword(userId: string): Promise<string> {
  const result = await post<{ userId: string; password: string }>('/auth/resetPassword', { userId });
  return result?.password ?? '';
}

/** 解锁指定用户（仅管理员）：清零失败计数并清除锁定。 */
export async function unlockUser(userId: string): Promise<boolean> {
  const result = await post<{ unlocked: boolean }>('/auth/unlock', { userId });
  return result?.unlocked ?? false;
}