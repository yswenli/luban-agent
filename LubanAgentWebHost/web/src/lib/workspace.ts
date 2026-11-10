/** 工作区头像：取名称首个字母/数字，跳过符号与空白。 */
export function workspaceInitial(name: string | null | undefined): string {
  const text = (name ?? '').trim();
  if (!text) return '·';
  const match = /[\p{L}\p{N}]/u.exec(text);
  return (match?.[0] ?? text[0]).toUpperCase();
}

/** 由工作区标识推出稳定色相：同一工作区每次渲染颜色都一样。 */
export function workspaceHue(workspaceId: string | null | undefined): number {
  const key = workspaceId ?? '';
  let hash = 0;
  for (let index = 0; index < key.length; index += 1) {
    hash = (hash * 31 + key.charCodeAt(index)) % 360;
  }
  return hash;
}

/** 工作区头像的底色（白色文字始终可读）。 */
export function workspaceColor(workspaceId: string | null | undefined): string {
  return `hsl(${workspaceHue(workspaceId)} 48% 44%)`;
}