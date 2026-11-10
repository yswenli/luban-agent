import type { JSX } from 'solid-js';
import { onCleanup, onMount } from 'solid-js';

/**
 * 浮层菜单。
 *
 * 定位由调用方通过 `style` 给定（通常是触发按钮的 `getBoundingClientRect()`），
 * 组件只负责「点外部关闭 + Esc 关闭」，避免每个页面各写一份。
 */
export function PopMenu(props: {
  style: JSX.CSSProperties;
  onClose: () => void;
  children: JSX.Element;
}) {
  let host!: HTMLDivElement;

  onMount(() => {
    const onPointerDown = (event: MouseEvent) => {
      if (!host?.contains(event.target as Node)) props.onClose();
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') props.onClose();
    };
    // 捕捉阶段注册：菜单挂载后第一次点击才会命中，不会误关刚打开自己
    window.addEventListener('mousedown', onPointerDown, true);
    window.addEventListener('keydown', onKey);
    onCleanup(() => {
      window.removeEventListener('mousedown', onPointerDown, true);
      window.removeEventListener('keydown', onKey);
    });
  });

  return (
    <div ref={host} class="menu" style={props.style} onMouseDown={(event) => event.stopPropagation()}>
      {props.children}
    </div>
  );
}

/** 把浮层约束在视口内（右侧 / 底部溢出时向左上收）。 */
export function clampPosition(x: number, y: number, width: number, height: number): { x: number; y: number } {
  const margin = 8;
  const maxX = window.innerWidth - width - margin;
  const maxY = window.innerHeight - height - margin;
  return {
    x: Math.max(margin, Math.min(x, Math.max(margin, maxX))),
    y: Math.max(margin, Math.min(y, Math.max(margin, maxY))),
  };
}

/** 按锚点矩形算菜单左上角坐标。`align=end` 时菜单右边缘对齐锚点右边缘。 */
export function menuAt(
  rect: DOMRect,
  options: { width: number; height: number; align?: 'start' | 'end'; gap?: number },
): { x: number; y: number } {
  const gap = options.gap ?? 6;
  const x = options.align === 'end' ? rect.right - options.width : rect.left;
  return clampPosition(x, rect.bottom + gap, options.width, options.height);
}