import { t } from '../i18n';
/**
 * 浏览器端图片预处理：超过阈值（最长边 / 体积 / 像素数）时先缩放并重编码。
 *
 * 服务端仍会按自己的阈值再处理一次（框架附件处理器：2000px / 5MB），
 * 这里的目的只是别把几 MB 原图整份 Base64 上传，并把解码压力留在浏览器侧。
 */

/** 可在浏览器侧重编码的位图格式；GIF 保留原样以免丢动画。 */
const REENCODABLE_TYPES = new Set(['image/png', 'image/jpeg', 'image/webp', 'image/bmp']);

/** JPEG 质量档：从高到低尝试，直到体积达标。 */
const JPEG_QUALITIES = [0.85, 0.7, 0.55, 0.4];

/** 体积仍不达标时尺寸的收缩比例与最多收缩轮数。 */
const SHRINK_FACTOR = 0.75;
const SHRINK_ROUNDS = 5;

export interface PreparedImage {
  /** 实际用于上传的文件：未触发压缩时就是原文件。 */
  file: File;
  /** 是否发生了重编码。 */
  compressed: boolean;
  /** 非空表示这张图不该上传（像素数超限），调用方应提示并跳过。 */
  rejectReason?: string;
}

/**
 * 按需压缩图片。
 *
 * 判定与框架附件处理器对齐：最长边超过 `maxEdge` 或体积超过 `maxBytes` 才处理；
 * 未超限直接返回原文件（保持无损、透明与动图）。浏览器无法解码时也原样返回，
 * 由服务端给出明确拒绝原因。
 *
 * 像素数超过 `maxPixels` 时不解码、直接返回拒绝原因：整幅解码要占 `宽×高×4` 字节，
 * 一亿像素就是 400MB，标签页会先崩掉，服务端的像素上限根本来不及生效。
 */
export async function prepareImageForUpload(
  file: File,
  maxEdge: number,
  maxBytes: number,
  maxPixels: number,
): Promise<PreparedImage> {
  if (!REENCODABLE_TYPES.has(file.type) || maxEdge <= 0 || maxBytes <= 0) {
    return { file, compressed: false };
  }

  if (maxPixels > 0) {
    const size = await measure(file);
    if (size && size.width * size.height > maxPixels) {
      return {
        file,
        compressed: false,
        rejectReason: t('image.tooLarge', {
      width: size.width,
      height: size.height,
      max: Math.floor(maxPixels / 10_000),
    }),
      };
    }
  }

  let bitmap: ImageBitmap;
  try {
    // 显式按 EXIF 方向解码：重编码会丢掉方向标记，若某个浏览器默认不应用，
    // 竖拍照片压缩后会被永久转 90°
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  } catch {
    return { file, compressed: false };
  }

  // 一次压缩内复用同一块画布：重设宽高会释放上一块后备存储，不必每轮都留一块给 GC
  const canvas = document.createElement('canvas');

  try {
    const edge = Math.max(bitmap.width, bitmap.height);
    if (edge <= maxEdge && file.size <= maxBytes) {
      return { file, compressed: false };
    }

    const scale = Math.min(1, maxEdge / edge);
    let width = Math.max(1, Math.round(bitmap.width * scale));
    let height = Math.max(1, Math.round(bitmap.height * scale));

    for (let round = 0; round <= SHRINK_ROUNDS; round += 1) {
      // PNG 源优先无损：截图类文本更清晰，带透明的图也不会铺成白底
      if (file.type === 'image/png') {
        const png = await encode(canvas, bitmap, width, height, 'image/png', 1);
        if (png && png.size <= maxBytes) {
          return { file: renamed(png, file, 'png'), compressed: true };
        }
      }

      for (const quality of JPEG_QUALITIES) {
        const jpeg = await encode(canvas, bitmap, width, height, 'image/jpeg', quality);
        if (jpeg && jpeg.size <= maxBytes) {
          return { file: renamed(jpeg, file, 'jpg'), compressed: true };
        }
      }

      width = Math.max(1, Math.round(width * SHRINK_FACTOR));
      height = Math.max(1, Math.round(height * SHRINK_FACTOR));
    }

    // 压不下去就交回原图：服务端会按同一阈值再拦一次
    return { file, compressed: false };
  } finally {
    bitmap.close();
    // 释放最后一块后备存储
    canvas.width = 0;
    canvas.height = 0;
  }
}

/** 只读图片尺寸：`<img>` 的 load 事件不要求整幅解码，比 createImageBitmap 便宜得多。 */
function measure(file: File): Promise<{ width: number; height: number } | null> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file);
    const image = new Image();
    const settle = (value: { width: number; height: number } | null): void => {
      URL.revokeObjectURL(url);
      resolve(value);
    };
    image.onload = () =>
      settle(
        image.naturalWidth > 0 && image.naturalHeight > 0
          ? { width: image.naturalWidth, height: image.naturalHeight }
          : null,
      );
    image.onerror = () => settle(null);
    image.src = url;
  });
}

/**
 * 画到 canvas 后编码。
 *
 * 复用传入的画布（重设宽高即释放旧后备存储）；`toBlob` 在调用时已快照画布内容，
 * 因此并发压缩交错执行也不会串图。
 */
function encode(
  canvas: HTMLCanvasElement,
  bitmap: ImageBitmap,
  width: number,
  height: number,
  type: string,
  quality: number,
): Promise<Blob | null> {
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  if (!context) return Promise.resolve(null);
  if (type === 'image/jpeg') {
    // JPEG 无透明通道，先铺白底，避免透明区域变黑
    context.fillStyle = '#fff';
    context.fillRect(0, 0, width, height);
  }
  context.drawImage(bitmap, 0, 0, width, height);
  return new Promise((resolve) => canvas.toBlob(resolve, type, quality));
}

/** 换扩展名与媒体类型，保持原始文件名主体（服务端按扩展名判类型，必须与字节一致）。 */
function renamed(blob: Blob, source: File, extension: string): File {
  const stem = source.name.replace(/\.[^./\\]+$/, '') || 'image';
  return new File([blob], `${stem}.${extension}`, {
    type: blob.type,
    lastModified: source.lastModified,
  });
}