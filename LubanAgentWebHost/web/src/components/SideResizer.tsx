import {
  SIDE_MIN_WIDTH,
  maxSideWidth,
  persistSideWidth,
  resetSideWidth,
  setSideDragging,
  setSideWidth,
  sideDragging,
  sideWidth,
} from '../lib/sidebar';
import { t } from '../i18n';

/** 方向键一次挪动的距离：比鼠标拖拽粗糙，但可预期。 */
const KEYBOARD_STEP = 16;

/** 一条拖拽接缝要操作的宽度状态；两侧分栏各用自己的实现。 */
export interface ResizeTarget {
  width: () => number;
  setWidth: (value: number) => void;
  minWidth: number;
  maxWidth: () => number;
  dragging: () => boolean;
  setDragging: (value: boolean) => void;
  persist: () => void;
  reset: () => void;
}

/** 默认目标：左侧项目栏（`lib/sidebar.ts` 的模块信号）。 */
const sidebarTarget: ResizeTarget = {
  width: sideWidth,
  setWidth: setSideWidth,
  minWidth: SIDE_MIN_WIDTH,
  maxWidth: maxSideWidth,
  dragging: sideDragging,
  setDragging: setSideDragging,
  persist: persistSideWidth,
  reset: resetSideWidth,
};

/**
 * 左右分栏之间的拖拽接缝。
 *
 * 用 pointer capture 而不是 document 级监听：指针移出浏览器窗口时仍能收到 up，
 * 不会把页面留在「拖拽中」状态。宽度只写模块信号，松手才落 localStorage。
 * `invert` 用于右侧面板：接缝在面板左边，指针左移才是「变宽」。
 */
export function SideResizer(props: { label?: string; target?: ResizeTarget; invert?: boolean }) {
  let startX = 0;
  let startWidth = 0;
  let dragging = false;

  const target = () => props.target ?? sidebarTarget;
  const direction = () => (props.invert ? -1 : 1);

  const onPointerDown = (event: PointerEvent) => {
    if (event.button !== 0) return;
    event.preventDefault();
    const element = event.currentTarget as HTMLElement;
    element.setPointerCapture(event.pointerId);
    dragging = true;
    startX = event.clientX;
    startWidth = target().width();
    target().setDragging(true);
  };

  const onPointerMove = (event: PointerEvent) => {
    if (!dragging) return;
    target().setWidth(startWidth + (event.clientX - startX) * direction());
  };

  const endDrag = (event: PointerEvent) => {
    if (!dragging) return;
    dragging = false;
    const element = event.currentTarget as HTMLElement;
    if (element.hasPointerCapture(event.pointerId)) element.releasePointerCapture(event.pointerId);
    target().setDragging(false);
    target().persist();
  };

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      target().setWidth(target().width() - KEYBOARD_STEP * direction());
      target().persist();
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      target().setWidth(target().width() + KEYBOARD_STEP * direction());
      target().persist();
    } else if (event.key === 'Home') {
      event.preventDefault();
      target().reset();
    }
  };

  return (
    <div
      class="side-resizer"
      classList={{ dragging: target().dragging() }}
      role="separator"
      aria-orientation="vertical"
      aria-label={props.label ?? t('resizer.sidebarWidth')}
      aria-valuemin={target().minWidth}
      aria-valuemax={target().maxWidth()}
      aria-valuenow={target().width()}
      tabIndex={0}
      title={t('resizer.dragHint')}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onDblClick={() => target().reset()}
      onKeyDown={onKeyDown}
    />
  );
}
