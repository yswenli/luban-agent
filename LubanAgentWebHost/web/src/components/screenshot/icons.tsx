import type { JSX } from 'solid-js';

/**
 * 截图标注工具栏图标（16px 视口，`currentColor` 描边）。
 *
 * 每个图标都是**工厂函数**：JSX 常量在求值时就生成了唯一的 DOM 节点，
 * 同一图标出现两次会把节点搬到最后一处，前面的实例变成空 `<svg>`。
 */
const wrap = (inner: JSX.Element): JSX.Element => (
  <svg
    viewBox="0 0 16 16"
    width="16"
    height="16"
    fill="none"
    stroke="currentColor"
    stroke-width="1.4"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    {inner}
  </svg>
);

export const iconRect = () => wrap(<rect x="2.5" y="3.5" width="11" height="9" rx="1" />);

export const iconEllipse = () => wrap(<ellipse cx="8" cy="8" rx="5.5" ry="4" />);

export const iconArrow = () =>
  wrap(
    <>
      <path d="M3 13 13 3" />
      <path d="M8.5 3H13v4.5" />
    </>,
  );

/** 楔形粗尾：粗箭杆 + 更宽三角头 */
export const iconArrowWedge = () =>
  wrap(
    <>
      <path d="M2.6 13.4 9.9 6.1" stroke-width="2.6" />
      <path d="M13.5 2.5 9.1 3.9 12.1 6.9z" fill="currentColor" stroke="none" />
    </>,
  );

/** 双向箭头：两端各一支箭头 */
export const iconArrowDouble = () =>
  wrap(
    <>
      <path d="M3.5 12.5 12.5 3.5" />
      <path d="M8.5 3.5h4v4" />
      <path d="M7.5 12.5h-4v-4" />
    </>,
  );

export const iconBrush = () =>
  wrap(
    <>
      <path d="M3 13.2 4.2 9.8l6.6-6.6a1.7 1.7 0 0 1 2.4 2.4l-6.6 6.6z" />
      <path d="M9.4 4.6l2.4 2.4" />
    </>,
  );

export const iconMosaic = () =>
  wrap(
    <>
      <rect x="2.5" y="2.5" width="4.5" height="4.5" />
      <rect x="9" y="3" width="4.5" height="4.5" />
      <rect x="3" y="9" width="4.5" height="4.5" />
      <rect x="8.5" y="8.5" width="5" height="5" />
    </>,
  );

export const iconText = () =>
  wrap(
    <>
      <path d="M3 3.5h10" />
      <path d="M8 3.5v9" />
      <path d="M5.5 12.5h5" />
    </>,
  );

export const iconUndo = () =>
  wrap(
    <>
      <path d="M3.2 6.6h6a3.6 3.6 0 0 1 0 7.2H6" />
      <path d="M5.8 3.7 2.9 6.6l2.9 2.9" />
    </>,
  );

export const iconDownload = () =>
  wrap(
    <>
      <path d="M8 2.5v8" />
      <path d="M4.6 7.4 8 10.8l3.4-3.4" />
      <path d="M2.8 13.4h10.4" />
    </>,
  );

export const iconClose = () =>
  wrap(
    <>
      <path d="M4.2 4.2l7.6 7.6" />
      <path d="M11.8 4.2l-7.6 7.6" />
    </>,
  );

export const iconCheck = () => wrap(<path d="M3 8.6 6.4 12 13 4.6" />);