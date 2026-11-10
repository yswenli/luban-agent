import type { JSX } from 'solid-js';

/**
 * 轻量线性图标（16px 视口，`currentColor` 描边），避免引入图标库。
 *
 * 每个图标是**工厂函数**而非 JSX 常量：JSX 常量在求值时就生成了唯一的 DOM 节点，
 * 同一图标在页面出现两次会把节点搬到最后一处，前面的实例变成空 `<svg>`。
 */
const paths: Record<string, () => JSX.Element> = {
  grid: () => (
    <>
      <rect x="3" y="3" width="7" height="7" rx="1.5" />
      <rect x="14" y="3" width="7" height="7" rx="1.5" />
      <rect x="3" y="14" width="7" height="7" rx="1.5" />
      <rect x="14" y="14" width="7" height="7" rx="1.5" />
    </>
  ),
  search: () => (
    <>
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 4.5 4.5" />
    </>
  ),
  plus: () => <path d="M12 5v14M5 12h14" />,
  'arrow-left': () => <path d="M19 12H5m0 0 6-6m-6 6 6 6" />,
  'arrow-up': () => <path d="M12 19V5m0 0-6 6m6-6 6 6" />,
  settings: () => (
    <>
      <circle cx="12" cy="12" r="3.2" />
      <path d="M19.4 15a1.7 1.7 0 0 0 .34 1.87l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06A1.7 1.7 0 0 0 15 19.4a1.7 1.7 0 0 0-1 1.55V21a2 2 0 1 1-4 0v-.09A1.7 1.7 0 0 0 8.9 19.4a1.7 1.7 0 0 0-1.87.34l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.7 1.7 0 0 0 4.6 15a1.7 1.7 0 0 0-1.55-1H3a2 2 0 1 1 0-4h.09A1.7 1.7 0 0 0 4.6 9a1.7 1.7 0 0 0-.34-1.87l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.7 1.7 0 0 0 9 4.6a1.7 1.7 0 0 0 1-1.55V3a2 2 0 1 1 4 0v.09A1.7 1.7 0 0 0 15 4.6a1.7 1.7 0 0 0 1.87-.34l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.7 1.7 0 0 0 19.4 9c.13.37.4.68.76.87H21a2 2 0 1 1 0 4h-.09a1.7 1.7 0 0 0-1.51 1.13Z" />
    </>
  ),
  help: () => (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M9.5 9.3a2.6 2.6 0 0 1 5 .8c0 1.7-2.5 2.1-2.5 3.9" />
      <path d="M12 17.5h.01" />
    </>
  ),
  info: () => (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 11v5.5" />
      <path d="M12 7.6h.01" />
    </>
  ),
  close: () => <path d="M18 6 6 18M6 6l12 12" />,
  copy: () => (
    <>
      <rect x="9" y="9" width="11" height="11" rx="2" />
      <path d="M5 15V5a2 2 0 0 1 2-2h10" />
    </>
  ),
  file: () => (
    <>
      <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8Z" />
      <path d="M14 3v5h5" />
    </>
  ),
  image: () => (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <circle cx="8.5" cy="9.5" r="1.5" />
      <path d="m21 15-4.5-4.5L6 20" />
    </>
  ),
  stop: () => <rect x="6.5" y="6.5" width="11" height="11" rx="2" />,
  'chevron-down': () => <path d="m6 9 6 6 6-6" />,
  'chevron-right': () => <path d="m9 6 6 6-6 6" />,
  trash: () => (
    <>
      <path d="M4 7h16" />
      <path d="M9 7V5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2" />
      <path d="M6 7v12a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V7" />
      <path d="M10 11v5M14 11v5" />
    </>
  ),
  pencil: () => <path d="M4 20h4L20 8a2.8 2.8 0 0 0-4-4L4 16v4Z" />,
  more: () => (
    <>
      <circle cx="5" cy="12" r="1.4" />
      <circle cx="12" cy="12" r="1.4" />
      <circle cx="19" cy="12" r="1.4" />
    </>
  ),
  refresh: () => (
    <>
      <path d="M20 11a8 8 0 1 0-2.3 6.4" />
      <path d="M20 4v7h-7" />
    </>
  ),
  alert: () => (
    <>
      <path d="M10.3 3.6 1.9 18a2 2 0 0 0 1.7 3h16.8a2 2 0 0 0 1.7-3L13.7 3.6a2 2 0 0 0-3.4 0Z" />
      <path d="M12 9v4.5M12 17h.01" />
    </>
  ),
  check: () => <path d="m5 12.5 4.5 4.5L19 7" />,
  terminal: () => (
    <>
      <path d="m5 8 4.5 4L5 16" />
      <path d="M12.5 16H19" />
    </>
  ),
  folder: () => <path d="M3 7a2 2 0 0 1 2-2h4l2 2.5h8a2 2 0 0 1 2 2V18a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z" />,
  message: () => (
    <>
      <path d="M21 12a8.5 8.5 0 0 1-8.5 8.5H8l-4 2 1.1-3.6A8.5 8.5 0 1 1 21 12Z" />
    </>
  ),
  spark: () => <path d="M12 3v4m0 10v4M3 12h4m10 0h4M5.6 5.6l2.8 2.8m7.2 7.2 2.8 2.8m0-12.8-2.8 2.8m-7.2 7.2-2.8 2.8" />,
  shield: () => (
    <>
      <path d="M12 3 5 6v6c0 4.2 2.9 7.6 7 9 4.1-1.4 7-4.8 7-9V6Z" />
      <path d="m9.5 12 1.8 1.8 3.2-3.4" />
    </>
  ),
  brain: () => (
    <>
      <path d="M9.5 4.5A3 3 0 0 0 6.6 8a3 3 0 0 0-1.6 5.4A3 3 0 0 0 7.5 19a3 3 0 0 0 5-2.2V6.7a2.2 2.2 0 0 0-3-2.2Z" />
      <path d="M14.5 4.5A3 3 0 0 1 17.4 8a3 3 0 0 1 1.6 5.4 3 3 0 0 1-2.5 5.6 3 3 0 0 1-5-2.2" />
    </>
  ),
  'new-project': () => (
    <>
      <path d="M3 7a2 2 0 0 1 2-2h4l2 2.5h8a2 2 0 0 1 2 2V18a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z" />
      <path d="M12 11v5m-2.5-2.5h5" />
    </>
  ),
};

export type IconName = keyof typeof paths | string;

export function Icon(props: { name: IconName; size?: number; class?: string }) {
  const size = () => props.size ?? 18;
  return (
    <svg
      width={size()}
      height={size()}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.7"
      stroke-linecap="round"
      stroke-linejoin="round"
      class={props.class}
      aria-hidden="true"
    >
      {(paths[props.name] ?? paths.file)()}
    </svg>
  );
}