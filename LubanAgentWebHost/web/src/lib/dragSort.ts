/**
 * 列表指针拖放排序。
 *
 * 只负责「按下 → 位移超过阈值 → 计算插入槽位 → 松手提交」这一段交互；
 * 顺序写入与视觉状态由调用方处理：调用方在 `onDrop` 里改数据，在 `onSlot` 里画插入线。
 */

/** 位移超过该值才算拖动，避免点选（打开会话、筛选项目）被当成拖拽。 */
const DRAG_THRESHOLD = 4;

/** 指针进入容器边缘这一范围内时自动滚动，让长列表可以拖到可视区外。 */
const EDGE_SCROLL_RANGE = 28;
const EDGE_SCROLL_STEP = 12;

export interface DragSortOptions {
  /** 触发拖动的 pointerdown 事件。 */
  event: PointerEvent;
  /** 被拖动的项：在它上面捕获指针，指针移出窗口也能收到 up。 */
  item: HTMLElement;
  /** 可滚动容器：项滚动后插入位置随之变化。 */
  container: HTMLElement | null | undefined;
  /** 项的 CSS 选择器，按 DOM 顺序即列表顺序。 */
  itemSelector: string;
  /** 排列方向：横向标签条用 x，纵向项目列表用 y。 */
  axis: 'x' | 'y';
  /** 被拖动项在列表中的下标。 */
  index: number;
  /** 进入拖动（给项加拖动态样式）。 */
  onStart?: () => void;
  /** 插入槽位：原列表下标 0..n（n 为末尾）；null 表示仍在原位或已取消。 */
  onSlot: (slot: number | null) => void;
  /** 提交：`toIndex` 是移除被拖项后的目标下标。 */
  onDrop: (toIndex: number) => void;
  /** 拖动结束（提交、取消、没有真正拖起来都会调用）。 */
  onEnd: () => void;
}

export function beginDragSort(options: DragSortOptions): void {
  const { event, item, container, itemSelector, axis, index } = options;
  if (!container || event.button !== 0) return;

  const startX = event.clientX;
  const startY = event.clientY;
  let pointerX = startX;
  let pointerY = startY;
  let active = false;
  let slot: number | null = null;
  let frame = 0;
  let finished = false;

  const pointerAt = (): number => (axis === 'x' ? pointerX : pointerY);

  const items = (): HTMLElement[] =>
    Array.from(container.querySelectorAll<HTMLElement>(itemSelector));

  const slotAt = (): number => {
    const list = items();
    for (let position = 0; position < list.length; position++) {
      const rect = list[position].getBoundingClientRect();
      const middle = axis === 'x' ? rect.left + rect.width / 2 : rect.top + rect.height / 2;
      if (pointerAt() < middle) return position;
    }
    return list.length;
  };

  const updateSlot = (): void => {
    const found = slotAt();
    // 落在自己原来的前/后边界内等于没动：既不画插入线，也不提交
    const next = found === index || found === index + 1 ? null : found;
    if (next === slot) return;
    slot = next;
    options.onSlot(next);
  };

  // 指针停在容器边缘时持续滚动；滚完布局变了要按新位置重算槽位
  const edgeScroll = (): void => {
    if (finished) return;
    const rect = container.getBoundingClientRect();
    const near =
      axis === 'x'
        ? pointerX < rect.left + EDGE_SCROLL_RANGE
          ? -1
          : pointerX > rect.right - EDGE_SCROLL_RANGE
            ? 1
            : 0
        : pointerY < rect.top + EDGE_SCROLL_RANGE
          ? -1
          : pointerY > rect.bottom - EDGE_SCROLL_RANGE
            ? 1
            : 0;
    if (near) {
      if (axis === 'x') container.scrollLeft += near * EDGE_SCROLL_STEP;
      else container.scrollTop += near * EDGE_SCROLL_STEP;
      updateSlot();
    }
    frame = window.requestAnimationFrame(edgeScroll);
  };

  const onMove = (moveEvent: PointerEvent): void => {
    if (finished) return;
    pointerX = moveEvent.clientX;
    pointerY = moveEvent.clientY;
    if (!active) {
      if (
        Math.abs(pointerX - startX) < DRAG_THRESHOLD &&
        Math.abs(pointerY - startY) < DRAG_THRESHOLD
      ) {
        return;
      }
      active = true;
      // 指针捕获必须等到「确实在拖」之后再建立：按下即捕获会把 pointerup 重定向到捕获元素，
      // 浏览器据此把 click 派发给容器，容器内按钮自己的 onClick 收不到，普通点击就等于失效。
      try {
        item.setPointerCapture(event.pointerId);
      } catch {
        /* 合成事件或指针已失效：退化为普通 window 监听 */
      }
      document.body.classList.add('is-sorting');
      options.onStart?.();
      frame = window.requestAnimationFrame(edgeScroll);
    }
    // 拖动中不允许选中文本
    moveEvent.preventDefault();
    updateSlot();
  };

  function releaseCapture(): void {
    try {
      if (item.hasPointerCapture(event.pointerId)) item.releasePointerCapture(event.pointerId);
    } catch {
      /* 指针已失效：无需处理 */
    }
  }

  function end(commit: boolean): void {
    if (finished) return;
    finished = true;
    if (frame) window.cancelAnimationFrame(frame);
    window.removeEventListener('pointermove', onMove);
    window.removeEventListener('pointerup', onUp);
    window.removeEventListener('pointercancel', onCancel);
    window.removeEventListener('keydown', onKey);
    releaseCapture();
    document.body.classList.remove('is-sorting');

    if (active) {
      // 指针捕获后浏览器仍会补发 click，拖完必须吞掉，避免顺带打开会话/筛选项目
      swallowNextClick();
      if (commit && slot !== null) options.onDrop(slot > index ? slot - 1 : slot);
    }
    options.onEnd();
  }

  const onUp = (): void => end(true);
  const onCancel = (): void => end(false);
  const onKey = (keyEvent: KeyboardEvent): void => {
    if (keyEvent.key !== 'Escape') return;
    keyEvent.preventDefault();
    keyEvent.stopPropagation();
    end(false);
  };

  window.addEventListener('pointermove', onMove);
  window.addEventListener('pointerup', onUp);
  window.addEventListener('pointercancel', onCancel);
  window.addEventListener('keydown', onKey);
}

/** 丢弃拖动结束后补发的那一次 click。 */
function swallowNextClick(): void {
  const swallow = (event: MouseEvent): void => {
    event.stopPropagation();
    event.preventDefault();
  };
  window.addEventListener('click', swallow, true);
  window.setTimeout(() => window.removeEventListener('click', swallow, true), 0);
}