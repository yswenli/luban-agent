import {
  SIDE_DEFAULT_WIDTH,
  SIDE_MIN_WIDTH,
  maxSideWidth,
  persistSideWidth,
  resetSideWidth,
  setSideDragging,
  setSideWidth,
  sideDragging,
  sideWidth,
} from '../lib/sidebar';

/** 方向键一次挪动的距离：比鼠标拖拽粗糙，但可预期。 */
const KEYBOARD_STEP = 16;

/**
 * 左右分栏之间的拖拽接缝。
 *
 * 用 pointer capture 而不是 document 级监听：指针移出浏览器窗口时仍能收到 up，
 * 不会把页面留在「拖拽中」状态。宽度只写模块信号，松手才落 localStorage。
 */
export function SideResizer(props: { label?: string }) {
  let startX = 0;
  let startWidth = 0;
  let dragging = false;

  const onPointerDown = (event: PointerEvent) => {
    if (event.button !== 0) return;
    event.preventDefault();
    const target = event.currentTarget as HTMLElement;
    target.setPointerCapture(event.pointerId);
    dragging = true;
    startX = event.clientX;
    startWidth = sideWidth();
    setSideDragging(true);
  };

  const onPointerMove = (event: PointerEvent) => {
    if (!dragging) return;
    setSideWidth(startWidth + (event.clientX - startX));
  };

  const endDrag = (event: PointerEvent) => {
    if (!dragging) return;
    dragging = false;
    const target = event.currentTarget as HTMLElement;
    if (target.hasPointerCapture(event.pointerId)) target.releasePointerCapture(event.pointerId);
    setSideDragging(false);
    persistSideWidth();
  };

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      setSideWidth(sideWidth() - KEYBOARD_STEP);
      persistSideWidth();
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      setSideWidth(sideWidth() + KEYBOARD_STEP);
      persistSideWidth();
    } else if (event.key === 'Home') {
      event.preventDefault();
      resetSideWidth();
    }
  };

  return (
    <div
      class="side-resizer"
      classList={{ dragging: sideDragging() }}
      role="separator"
      aria-orientation="vertical"
      aria-label={props.label ?? '调整侧栏宽度'}
      aria-valuemin={SIDE_MIN_WIDTH}
      aria-valuemax={maxSideWidth()}
      aria-valuenow={sideWidth()}
      tabIndex={0}
      title={`拖动调整宽度，双击恢复默认（${SIDE_DEFAULT_WIDTH}px）`}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onDblClick={() => resetSideWidth()}
      onKeyDown={onKeyDown}
    />
  );
}