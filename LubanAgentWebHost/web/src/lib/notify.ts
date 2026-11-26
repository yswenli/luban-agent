import { createSignal } from 'solid-js';

/**
 * 页面内提示音：一轮跑完 / 需要你点一下 / 对话级错误。
 *
 * 声音用 Web Audio 现场合成，不引音频文件；三个开关只存本机 localStorage
 * （纯浏览器偏好，不参与服务端配置，不同设备各自一份）。
 */
export type NotifyKind = 'done' | 'question' | 'error';

const STORAGE_PREFIX = 'luban.web.notify.';

/** 同一类提示音的最小间隔：同一帧重复到达时不叠音。三类彼此独立，互不吞声。 */
const MIN_GAP_MS = 1200;

/** 试听时的同一类最小间隔：连点两下鼠标不会叠音，但连点三行不同音都能听到。 */
const PREVIEW_GAP_MS = 350;

function readFlag(kind: NotifyKind): boolean {
  try {
    // 没有这个键 = 从未设置过 = 默认开；只有显式关掉才写 '0'
    return window.localStorage.getItem(STORAGE_PREFIX + kind) !== '0';
  } catch {
    return true;
  }
}

const [doneEnabled, setDoneEnabled] = createSignal(readFlag('done'));
const [questionEnabled, setQuestionEnabled] = createSignal(readFlag('question'));
const [errorEnabled, setErrorEnabled] = createSignal(readFlag('error'));

const flags = { done: doneEnabled, question: questionEnabled, error: errorEnabled } as const;
const setters = { done: setDoneEnabled, question: setQuestionEnabled, error: setErrorEnabled } as const;

/** 某个提示音是否开启；设置页用它绑定复选框。 */
export function notifyEnabled(kind: NotifyKind): boolean {
  return flags[kind]();
}

export function setNotifyEnabled(kind: NotifyKind, enabled: boolean): void {
  setters[kind](enabled);
  try {
    window.localStorage.setItem(STORAGE_PREFIX + kind, enabled ? '1' : '0');
  } catch {
    /* 隐私模式或配额满：本次仍然生效，只是刷新后回到上次可用值 */
  }
}

/** 一个音：`at` 相对起点秒数；`overtones` 是叠加的泛音（倍数 + 相对音量）。 */
interface Note {
  at: number;
  freq: number;
  length: number;
  wave: OscillatorType;
  volume: number;
  overtones?: { ratio: number; volume: number }[];
}

/**
 * 三类声音靠「音区 + 波形 + 节奏」三重区分，避免只听出「响了」分不清是哪一类。
 *
 * - 完成：高音区（G5–D6）三角波三连上行，节奏最密，尾音收长 —— 借鉴系统提示的「玻璃三音」（如 iOS 三全音、Slack 完成音的思路）。
 * - 需要你操作：中高音区（A5）纯正弦同音两响，短-长 —— 借鉴 IM 的「ping-ping」提醒，最柔、不抢注意力。
 * - 出错：低音区（A3–F3）三角波两响下行大三度，拖尾最长 —— 借鉴桌面系统「basso」类错误音，压得住、不像成功。
 */
const CHIMES: Record<NotifyKind, Note[]> = {
  done: [
    { at: 0, freq: 783.99, length: 0.11, wave: 'triangle', volume: 0.12 }, // G5
    { at: 0.085, freq: 987.77, length: 0.11, wave: 'triangle', volume: 0.12 }, // B5
    { at: 0.17, freq: 1174.66, length: 0.28, wave: 'triangle', volume: 0.12 }, // D6
  ],
  question: [
    { at: 0, freq: 880, length: 0.1, wave: 'sine', volume: 0.14 }, // A5
    { at: 0.2, freq: 880, length: 0.24, wave: 'sine', volume: 0.14 }, // A5
  ],
  error: [
    // 低音靠三角波 + 高八度泛音补足：笔记本小喇叭也能听出来，又不至于轰
    { at: 0, freq: 220, length: 0.22, wave: 'triangle', volume: 0.11, overtones: [{ ratio: 2, volume: 0.3 }] }, // A3
    { at: 0.16, freq: 174.61, length: 0.4, wave: 'triangle', volume: 0.11, overtones: [{ ratio: 2, volume: 0.3 }] }, // F3
  ],
};

let context: AudioContext | null = null;
const lastPlayedAt: Record<NotifyKind, number> = { done: 0, question: 0, error: 0 };

function audioContext(): AudioContext | null {
  if (context) return context;
  try {
    context = new AudioContext();
    return context;
  } catch {
    // 浏览器不支持或已被策略禁用：静默降级成「没有提示音」，不影响对话
    return null;
  }
}

function schedule(ac: AudioContext, start: number, note: Note, ratio: number, volume: number): void {
  const at = start + note.at;
  const osc = ac.createOscillator();
  const gain = ac.createGain();
  osc.type = note.wave;
  osc.frequency.setValueAtTime(note.freq * ratio, at);
  // 指数包络：起音 12ms 防爆音，尾音收到接近 0 再停
  gain.gain.setValueAtTime(0.0001, at);
  gain.gain.exponentialRampToValueAtTime(volume, at + 0.012);
  gain.gain.exponentialRampToValueAtTime(0.0001, at + note.length);
  osc.connect(gain);
  gain.connect(ac.destination);
  osc.start(at);
  osc.stop(at + note.length + 0.02);
}

async function play(kind: NotifyKind, force: boolean): Promise<void> {
  if (!force && !notifyEnabled(kind)) return;
  const now = Date.now();
  if (now - lastPlayedAt[kind] < (force ? PREVIEW_GAP_MS : MIN_GAP_MS)) return;

  const ac = audioContext();
  if (!ac) return;
  // 首次交互前音频上下文处于 suspended：尽力恢复，失败就静默（不弹提示打扰用户）
  if (ac.state === 'suspended') {
    try {
      await ac.resume();
    } catch {
      return;
    }
  }
  if (ac.state !== 'running') return;

  lastPlayedAt[kind] = now;
  const start = ac.currentTime + 0.01;
  for (const note of CHIMES[kind]) {
    schedule(ac, start, note, 1, note.volume);
    for (const overtone of note.overtones ?? []) {
      schedule(ac, start, note, overtone.ratio, note.volume * overtone.volume);
    }
  }
}

/** 按开关播放一次提示音；调用方不需要知道开关状态。 */
export function notify(kind: NotifyKind): void {
  void play(kind, false);
}

/** 设置页「试听」：忽略开关，只保留同一类音的短暂去重，方便逐条对比。 */
export function previewNotify(kind: NotifyKind): void {
  void play(kind, true);
}