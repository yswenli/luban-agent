import type { JSX } from 'solid-js';

/**
 * 语言菜单里的国旗/地区标识。
 *
 * 全部内置 SVG：Windows 不提供国旗 emoji（会退化成 `US` 这类字母），
 * 不能依赖 `🇺🇸`。图形做简化处理，只求一眼可辨，不追求官方比例与纹章细节。
 * `zh-TW` 按约定用「繁」字徽标，不用旗帜。
 */

function star(cx: number, cy: number, radius: number): string {
  const points: string[] = [];
  for (let index = 0; index < 10; index += 1) {
    const angle = -Math.PI / 2 + (index * Math.PI) / 5;
    const r = index % 2 === 0 ? radius : radius * 0.4;
    points.push(`${(cx + r * Math.cos(angle)).toFixed(2)},${(cy + r * Math.sin(angle)).toFixed(2)}`);
  }
  return points.join(' ');
}

function gb(): JSX.Element {
  return (
    <>
      <rect width="24" height="16" fill="#012169" />
      <path d="M0 0 24 16M24 0 0 16" stroke="#fff" stroke-width="3.4" />
      <path d="M0 0 24 16M24 0 0 16" stroke="#C8102E" stroke-width="1.5" />
      <path d="M12 0v16M0 8h24" stroke="#fff" stroke-width="5.4" />
      <path d="M12 0v16M0 8h24" stroke="#C8102E" stroke-width="3" />
    </>
  );
}

function az(): JSX.Element {
  return (
    <>
      <rect width="24" height="5.34" fill="#0092BC" />
      <rect y="5.34" width="24" height="5.33" fill="#E4002B" />
      <rect y="10.67" width="24" height="5.33" fill="#00AF66" />
      <circle cx="10.2" cy="8" r="3.1" fill="#fff" />
      <circle cx="11.5" cy="8" r="2.6" fill="#E4002B" />
      <polygon points={star(15.2, 8, 1.9)} fill="#fff" />
    </>
  );
}

function es(): JSX.Element {
  return (
    <>
      <rect width="24" height="16" fill="#AA151B" />
      <rect y="4" width="24" height="8" fill="#F1BF00" />
    </>
  );
}

function id(): JSX.Element {
  return (
    <>
      <rect width="24" height="8" fill="#E11C24" />
      <rect y="8" width="24" height="8" fill="#fff" />
    </>
  );
}

function it(): JSX.Element {
  return (
    <>
      <rect width="8" height="16" fill="#009246" />
      <rect x="8" width="8" height="16" fill="#fff" />
      <rect x="16" width="8" height="16" fill="#CE2B37" />
    </>
  );
}

function ja(): JSX.Element {
  return (
    <>
      <rect width="24" height="16" fill="#fff" />
      <circle cx="12" cy="8" r="4.4" fill="#BC002D" />
    </>
  );
}

function ko(): JSX.Element {
  return (
    <>
      <rect width="24" height="16" fill="#fff" />
      <path d="M7.8 8a4.2 4.2 0 0 1 8.4 0Z" fill="#CD2E3A" />
      <path d="M7.8 8a4.2 4.2 0 0 0 8.4 0Z" fill="#0047A0" />
      <path d="M5 3.2h2.6M5 4.6h2.6M16.4 11.4H19M16.4 12.8H19" stroke="#1b1b1b" stroke-width="0.7" />
      <path d="M18 3.2h1M18 4.6h1M5 11.4h1M5 12.8h1" stroke="#1b1b1b" stroke-width="0.7" />
    </>
  );
}

function pt(): JSX.Element {
  return (
    <>
      <rect width="24" height="16" fill="#DA291C" />
      <rect width="9.6" height="16" fill="#046A38" />
      <circle cx="9.6" cy="8" r="3.4" fill="#FFE900" />
      <circle cx="9.6" cy="8" r="2" fill="#DA291C" />
      <rect x="8.7" y="6.4" width="1.8" height="3.2" fill="#fff" />
    </>
  );
}

function pl(): JSX.Element {
  return (
    <>
      <rect width="24" height="8" fill="#fff" />
      <rect y="8" width="24" height="8" fill="#DC143C" />
    </>
  );
}

function ru(): JSX.Element {
  return (
    <>
      <rect width="24" height="5.34" fill="#fff" />
      <rect y="5.34" width="24" height="5.33" fill="#0039A6" />
      <rect y="10.67" width="24" height="5.33" fill="#D52B1E" />
    </>
  );
}

function th(): JSX.Element {
  return (
    <>
      <rect width="24" height="16" fill="#A51931" />
      <rect y="2.5" width="24" height="11" fill="#F4F5F8" />
      <rect y="5" width="24" height="6" fill="#2D2A4A" />
    </>
  );
}

function tr(): JSX.Element {
  return (
    <>
      <rect width="24" height="16" fill="#E30A17" />
      <circle cx="9.4" cy="8" r="3.5" fill="#fff" />
      <circle cx="10.6" cy="8" r="2.8" fill="#E30A17" />
      <polygon points={star(14.6, 8, 2)} fill="#fff" />
    </>
  );
}

function cn(): JSX.Element {
  return (
    <>
      <rect width="24" height="16" fill="#DE2910" />
      <polygon points={star(4.6, 4.4, 3)} fill="#FFDE00" />
      <polygon points={star(9.4, 1.6, 1)} fill="#FFDE00" />
      <polygon points={star(10.6, 3.9, 1)} fill="#FFDE00" />
      <polygon points={star(10.2, 6.4, 1)} fill="#FFDE00" />
      <polygon points={star(8.6, 8.2, 1)} fill="#FFDE00" />
    </>
  );
}

const SHAPES: Record<string, () => JSX.Element> = {
  gb,
  az,
  es,
  id,
  it,
  ja,
  ko,
  pt,
  pl,
  ru,
  th,
  tr,
  cn,
};

/** 语言菜单左侧标识；`tw` 用文字徽标，其余为 SVG 国旗。 */
export function Flag(props: { code: string; size?: number }) {
  const width = () => props.size ?? 20;
  const height = () => Math.round((width() * 2) / 3);

  if (props.code === 'tw') {
    return (
      <span
        class="flag flag-badge"
        style={{ width: `${width()}px`, height: `${height()}px`, 'font-size': `${Math.round(width() * 0.52)}px` }}
        aria-hidden="true"
      >
        繁
      </span>
    );
  }

  const shape = () => (SHAPES[props.code] ?? SHAPES.gb)();
  return (
    <svg class="flag" width={width()} height={height()} viewBox="0 0 24 16" aria-hidden="true">
      {shape()}
    </svg>
  );
}