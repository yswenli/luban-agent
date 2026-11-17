import { For, Show, createSignal, onCleanup, onMount } from 'solid-js';
import type { JSX } from 'solid-js';
import type { ArrowStyle, Rect, ToolType } from './annotation';
import { MIN_SIZE, createNumberField } from './number';
import {
  iconArrow,
  iconArrowDouble,
  iconArrowWedge,
  iconBrush,
  iconCheck,
  iconClose,
  iconDownload,
  iconEllipse,
  iconMosaic,
  iconRect,
  iconText,
  iconUndo,
} from './icons';

export interface ToolbarProps {
  tool: ToolType | null;
  /** 主体尺寸：图形线宽、文字字号或马赛克块大小 */
  width: number;
  /** 文字工具的字号下拉档位 */
  fontSizes: number[];
  /** 描边宽度，CSS 像素；0.1 起，手输 */
  strokeWidth: number;
  /** 描边颜色：文字外描边 / 图形线条外圈 */
  strokeColor: string;
  /** 是否绘制描边 */
  strokeEnabled: boolean;
  /** 矩形/椭圆是否开启填充 */
  fillEnabled: boolean;
  /** 矩形/椭圆的填充色 */
  fillColor: string;
  /** 主体颜色：文字颜色 / 图形线条色 */
  color: string;
  /** 箭头样式；仅箭头工具使用 */
  arrowStyle: ArrowStyle;
  /** 选区位置：工具栏优先贴在选区右下方，放不下就翻到上方 */
  anchor: Rect;
  canUndo: boolean;
  onToolChange: (tool: ToolType | null) => void;
  onWidthChange: (value: number) => void;
  onStrokeWidthChange: (value: number) => void;
  onStrokeColorChange: (value: string) => void;
  onStrokeEnabledChange: (value: boolean) => void;
  onFillEnabledChange: (value: boolean) => void;
  onFillColorChange: (value: string) => void;
  onColorChange: (value: string) => void;
  onArrowStyleChange: (value: ArrowStyle) => void;
  onUndo: () => void;
  onDownload: () => void;
  onCancel: () => void;
  onConfirm: () => void;
}

const toolItems: { key: ToolType; title: string; icon: () => JSX.Element }[] = [
  { key: 'rect', title: '方框', icon: iconRect },
  { key: 'ellipse', title: '椭圆', icon: iconEllipse },
  { key: 'arrow', title: '箭头', icon: iconArrow },
  { key: 'brush', title: '画笔', icon: iconBrush },
  { key: 'mosaic', title: '马赛克', icon: iconMosaic },
  { key: 'text', title: '文字', icon: iconText },
];

const arrowStyleItems: { key: ArrowStyle; title: string; icon: () => JSX.Element }[] = [
  { key: 'thin', title: '细尾箭头', icon: iconArrow },
  { key: 'wedge', title: '楔形粗尾箭头', icon: iconArrowWedge },
  { key: 'double', title: '双向箭头', icon: iconArrowDouble },
];

export function Toolbar(props: ToolbarProps) {
  let rootEl: HTMLDivElement | undefined;
  const [box, setBox] = createSignal({ w: 0, h: 0 });

  const sizeLabel = () => {
    if (props.tool === 'text') return '字号';
    if (props.tool === 'mosaic') return '块大小';
    return '大小';
  };

  const showFill = () => props.tool === 'rect' || props.tool === 'ellipse';

  const sizeField = createNumberField(
    () => props.width,
    (value) => props.onWidthChange(value),
  );
  const strokeField = createNumberField(
    () => props.strokeWidth,
    (value) => props.onStrokeWidthChange(value),
  );

  /** 选区右下角优先；下方放不下翻到上方，再整体夹进视口 */
  const posStyle = () => {
    const { x, y, w, h } = props.anchor;
    const gap = 8;
    const vw = window.innerWidth;
    const vh = window.innerHeight;
    const tw = box().w || 310;
    const th = box().h || 38;
    let left = x + w - tw;
    let top = y + h + gap;
    if (top + th > vh - gap) top = y - gap - th;
    if (top < gap) top = y + h + gap;
    left = Math.min(Math.max(gap, left), Math.max(gap, vw - tw - gap));
    top = Math.min(Math.max(gap, top), Math.max(gap, vh - th - gap));
    return { left: `${Math.round(left)}px`, top: `${Math.round(top)}px` };
  };

  function measure(): void {
    if (!rootEl) return;
    setBox({ w: rootEl.offsetWidth, h: rootEl.offsetHeight });
  }

  let observer: ResizeObserver | null = null;

  onMount(() => {
    measure();
    window.addEventListener('resize', measure);
    // 二级工具栏会随选项增减变宽变窄，跟着重算位置，避免贴边溢出
    if (typeof ResizeObserver !== 'undefined' && rootEl) {
      observer = new ResizeObserver(measure);
      observer.observe(rootEl);
    }
  });

  onCleanup(() => {
    window.removeEventListener('resize', measure);
    observer?.disconnect();
    observer = null;
  });

  return (
    <div
      ref={rootEl}
      class="vws-tb"
      style={posStyle()}
      onPointerDown={(event) => {
        event.stopPropagation();
        event.preventDefault();
      }}
      onClick={(event) => event.stopPropagation()}
    >
      <div class="vws-tb__bar">
        <For each={toolItems}>
          {(item) => (
            <button
              type="button"
              class="vws-tb__btn"
              classList={{ 'is-active': props.tool === item.key }}
              title={item.title}
              onClick={() => props.onToolChange(props.tool === item.key ? null : item.key)}
            >
              {item.icon()}
            </button>
          )}
        </For>
        <span class="vws-tb__sep" />
        <button
          type="button"
          class="vws-tb__btn"
          title="撤销 (Ctrl+Z)"
          disabled={!props.canUndo}
          onClick={() => props.onUndo()}
        >
          {iconUndo()}
        </button>
        <button type="button" class="vws-tb__btn" title="下载" onClick={() => props.onDownload()}>
          {iconDownload()}
        </button>
        <button type="button" class="vws-tb__btn" title="取消 (Esc)" onClick={() => props.onCancel()}>
          {iconClose()}
        </button>
        <button type="button" class="vws-tb__btn is-primary" title="完成 (Enter)" onClick={() => props.onConfirm()}>
          {iconCheck()}
        </button>
      </div>

      <Show when={props.tool}>
        <div class="vws-tb__options">
          <Show when={props.tool === 'arrow'}>
            <span class="vws-tb__label">样式</span>
            <For each={arrowStyleItems}>
              {(item) => (
                <button
                  type="button"
                  class="vws-tb__btn vws-tb__btn--style"
                  classList={{ 'is-active': props.arrowStyle === item.key }}
                  title={item.title}
                  aria-label={item.title}
                  aria-pressed={props.arrowStyle === item.key}
                  onClick={() => props.onArrowStyleChange(item.key)}
                >
                  {item.icon()}
                </button>
              )}
            </For>
            <span class="vws-tb__sep" />
          </Show>

          <span class="vws-tb__label">{sizeLabel()}</span>
          <Show
            when={props.tool === 'text'}
            fallback={
              <>
                <input
                  class="vws-tb__num"
                  type="number"
                  min={MIN_SIZE}
                  step="0.1"
                  inputmode="decimal"
                  value={sizeField.draft()}
                  title={`${sizeLabel()}，可直接输入`}
                  onPointerDown={(event) => event.stopPropagation()}
                  onInput={sizeField.onInput}
                  onChange={sizeField.onCommit}
                  onBlur={sizeField.onCommit}
                />
                <span class="vws-tb__unit">px</span>
              </>
            }
          >
            <select
              class="vws-tb__select"
              value={String(props.width)}
              title={`字号 ${props.width}px`}
              onPointerDown={(event) => event.stopPropagation()}
              onChange={(event) => props.onWidthChange(Number(event.currentTarget.value))}
            >
              <For each={props.fontSizes}>{(size) => <option value={String(size)}>{size}</option>}</For>
            </select>
          </Show>

          <Show when={props.tool !== 'mosaic'}>
            <span class="vws-tb__sep" />
            <span class="vws-tb__label">颜色</span>
            <input
              class="vws-tb__pick"
              type="color"
              value={props.color}
              title={`颜色 ${props.color}`}
              onPointerDown={(event) => event.stopPropagation()}
              onInput={(event) => props.onColorChange(event.currentTarget.value)}
            />

            <span class="vws-tb__sep" />
            <span class="vws-tb__label">描边</span>
            <button
              type="button"
              class="vws-tb__switch"
              classList={{ 'is-on': props.strokeEnabled }}
              role="switch"
              aria-checked={props.strokeEnabled}
              title={props.strokeEnabled ? '关闭描边' : '开启描边'}
              onClick={() => props.onStrokeEnabledChange(!props.strokeEnabled)}
            >
              <span class="vws-tb__knob" />
            </button>
            <Show when={props.strokeEnabled}>
              <input
                class="vws-tb__num"
                type="number"
                min={MIN_SIZE}
                step="0.1"
                inputmode="decimal"
                value={strokeField.draft()}
                title="描边宽度，0.1px 起，可直接输入"
                onPointerDown={(event) => event.stopPropagation()}
                onInput={strokeField.onInput}
                onChange={strokeField.onCommit}
                onBlur={strokeField.onCommit}
              />
              <span class="vws-tb__unit">px</span>
              <input
                class="vws-tb__pick"
                type="color"
                value={props.strokeColor}
                title={`描边色 ${props.strokeColor}`}
                onPointerDown={(event) => event.stopPropagation()}
                onInput={(event) => props.onStrokeColorChange(event.currentTarget.value)}
              />
            </Show>
          </Show>

          <Show when={showFill()}>
            <span class="vws-tb__sep" />
            <span class="vws-tb__label">填充</span>
            <button
              type="button"
              class="vws-tb__switch"
              classList={{ 'is-on': props.fillEnabled }}
              role="switch"
              aria-checked={props.fillEnabled}
              title={props.fillEnabled ? '关闭填充' : '开启填充'}
              onClick={() => props.onFillEnabledChange(!props.fillEnabled)}
            >
              <span class="vws-tb__knob" />
            </button>
            <Show when={props.fillEnabled}>
              <input
                class="vws-tb__pick"
                type="color"
                value={props.fillColor}
                title={`填充色 ${props.fillColor}`}
                onPointerDown={(event) => event.stopPropagation()}
                onInput={(event) => props.onFillColorChange(event.currentTarget.value)}
              />
            </Show>
          </Show>
        </div>
      </Show>
    </div>
  );
}