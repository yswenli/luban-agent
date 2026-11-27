import { createEffect } from 'solid-js';
import { t } from '../i18n';

/**
 * 输入区富文本编辑器：`@技能` 以「图标 + 名称」胶囊形式内嵌。
 *
 * 为什么不是 textarea：胶囊带图标与内边距，宽度大于同长度文本，
 * textarea 的光标与换行按纯文本宽度计算，一出现胶囊就整体错位；
 * 只有把胶囊做成真实 DOM 节点，光标、选区、点击才能跟着它走。
 *
 * 对外只有纯文本：`onInput` 上报序列化结果（胶囊还原成 `@id`），
 * 发送链路（submit / optimize）无需知道胶囊存在。
 */

/** 胶囊显示用的技能信息：`id` 参与序列化，`label` 只是显示名。 */
export interface ComposerSkill {
  id: string;
  label: string;
}

/**
 * 光标前的 `@token` 及其 DOM 定位。
 * 调用方把它当作不透明句柄：只用于回传给 `applyToken`，不要读 `node` 系列字段。
 */
export interface MentionToken {
  /** `@` 之后的输入片段（可能为空串，即刚敲下 `@`）。 */
  query: string;
  node: Text;
  nodeStart: number;
  nodeEnd: number;
}

export interface ComposerEditorHandle {
  /** 当前内容的纯文本（胶囊序列化为 `@id`）。 */
  text(): string;
  /** 整段替换内容；`hydrate` 为真时把能解析到的 `@id` 还原成胶囊。 */
  setText(text: string, hydrate?: boolean): void;
  clear(): void;
  focus(): void;
  /** 光标前的 `@token`；不满足条件返回 null。 */
  tokenAtCaret(): MentionToken | null;
  /** 把 token 原地替换成技能胶囊（token 必须是最近一次 `tokenAtCaret` 的返回值）。 */
  applyToken(token: MentionToken, skill: ComposerSkill): void;
}

export interface ComposerEditorProps {
  placeholder: string;
  disabled?: boolean;
  /** 内容变化（含程序化 setText / 胶囊增删）。 */
  onInput: (text: string) => void;
  /** 光标位置可能变化（输入、点击、方向键），用于重算候选。 */
  onCaret?: () => void;
  /** 把已完整键入的 `@id` 解析成胶囊；返回 null 表示保留纯文本。 */
  resolveSkill?: (id: string) => ComposerSkill | null;
  api?: (handle: ComposerEditorHandle) => void;
  onKeyDown?: (event: KeyboardEvent) => void;
  onFocus?: () => void;
  onBlur?: () => void;
  /** 仅处理带文件的粘贴（图片/文件附件）；纯文本粘贴由编辑器自己接管。 */
  onPaste?: (event: ClipboardEvent) => void;
}

/** 胶囊图标：分层形状，与 Icons.tsx 的线性图标同风格（24 视口、currentColor）。 */
const CHIP_ICON =
  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"' +
  ' stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
  '<path d="m12 3 8 4.5-8 4.5-8-4.5L12 3Z" />' +
  '<path d="m4 12 8 4.5 8-4.5" />' +
  '<path d="m4 16.5 8 4.5 8-4.5" />' +
  '</svg>';

/** 允许出现在 `@token` 里的字符：路径特征（`@D:\a.cs`、`@./src`）不算技能。 */
const TOKEN_BEFORE = /(^|\s)@([^\s@\\/:~]*)$/;
/** 行内扫描已键入的技能引用（粘贴 / 改写结果回填时用）。 */
const TOKEN_INLINE = /(^|\s)@([^\s@\\/:~<>]+)/g;

export function ComposerEditor(props: ComposerEditorProps) {
  let editorEl!: HTMLDivElement;

  function makeChip(skill: ComposerSkill): HTMLSpanElement {
    const chip = document.createElement('span');
    chip.className = 'mention-chip';
    chip.contentEditable = 'false';
    chip.dataset.chipId = skill.id;
    chip.title = t('composer.skillPill', { name: skill.id });

    const icon = document.createElement('span');
    icon.className = 'chip-icon';
    icon.innerHTML = CHIP_ICON;

    const name = document.createElement('span');
    name.className = 'chip-name';
    name.textContent = skill.label;

    const remove = document.createElement('button');
    remove.type = 'button';
    remove.className = 'chip-x';
    remove.tabIndex = -1;
    remove.title = t('common.remove');
    remove.textContent = '×';
    // 按下就阻止默认行为，避免点 × 时输入区失焦、候选弹层跟着关掉
    remove.addEventListener('mousedown', (event) => event.preventDefault());
    remove.addEventListener('click', (event) => {
      event.preventDefault();
      event.stopPropagation();
      removeChip(chip);
    });

    chip.appendChild(icon);
    chip.appendChild(name);
    chip.appendChild(remove);
    // 胶囊整体不可编辑：按下即可聚焦输入区，点一下把光标放到胶囊之后，随后退格整体删除
    chip.addEventListener('mousedown', (event) => event.preventDefault());
    chip.addEventListener('click', (event) => {
      event.preventDefault();
      editorEl.focus();
      placeCaretAfter(chip);
      notifyCaret();
    });
    return chip;
  }

  function isChip(node: Node | null): node is HTMLElement {
    return (
      !!node &&
      node.nodeType === Node.ELEMENT_NODE &&
      !!(node as HTMLElement).dataset?.chipId
    );
  }

  function placeCaretAfter(node: Node): void {
    const parent = node.parentNode;
    if (!parent) return;
    const index = Array.prototype.indexOf.call(parent.childNodes, node) as number;
    collapseCaretTo(parent, index + 1);
  }

  function collapseCaretTo(node: Node, offset: number): void {
    const max = node.nodeType === Node.TEXT_NODE ? (node.nodeValue ?? '').length : Number.MAX_SAFE_INTEGER;
    const range = document.createRange();
    range.setStart(node, Math.min(offset, max));
    range.collapse(true);
    const selection = window.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(range);
  }

  function removeChip(chip: HTMLElement): void {
    const parent = chip.parentNode;
    if (!parent) return;
    const next = chip.nextSibling;
    // 胶囊后自动补了一个空格：删除时一并吞掉，避免留下两个空格
    if (next && next.nodeType === Node.TEXT_NODE) {
      (next as Text).data = (next as Text).data.replace(/^[ \u00a0]+/, '');
    }
    const index = Array.prototype.indexOf.call(parent.childNodes, chip) as number;
    chip.remove();
    editorEl.focus();

    if (next && next.nodeType === Node.TEXT_NODE && next.isConnected) {
      collapseCaretTo(next, 0);
    } else {
      collapseCaretTo(parent, Math.max(0, Math.min(index, parent.childNodes.length)));
    }
    emitInput();
  }

  /** 序列化：文本取原文，`<br>` / 块级元素还原成换行，胶囊还原成 `@id`。 */
  function serialize(): string {
    const out: string[] = [];
    const walk = (node: Node): void => {
      for (const child of Array.from(node.childNodes)) {
        if (child.nodeType === Node.TEXT_NODE) {
          out.push((child.nodeValue ?? '').replace(/\u00a0/g, ' '));
          continue;
        }
        if (child.nodeName === 'BR') {
          out.push('\n');
          continue;
        }
        if (child.nodeType !== Node.ELEMENT_NODE) continue;
        const element = child as HTMLElement;
        if (element.dataset.chipId) {
          out.push(`@${element.dataset.chipId}`);
          continue;
        }
        // 粘贴/回车可能留下 div/p：当作换行，避免多行被粘成一行
        const block = element.nodeName === 'DIV' || element.nodeName === 'P';
        if (block) {
          const last = out[out.length - 1] ?? '';
          if (last && !last.endsWith('\n')) out.push('\n');
        }
        walk(element);
      }
    };
    walk(editorEl);
    return out.join('').replace(/\u200b/g, '');
  }

  function syncEmpty(): void {
    editorEl.classList.toggle('is-empty', serialize().trim() === '');
  }

  function emitInput(): void {
    syncEmpty();
    props.onInput(serialize());
    notifyCaret();
  }

  function notifyCaret(): void {
    props.onCaret?.();
  }

  function tokenAtCaret(): MentionToken | null {
    if (!editorEl) return null;
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return null;
    const range = selection.getRangeAt(0);
    if (!range.collapsed) return null;
    const node = range.startContainer;
    if (node.nodeType !== Node.TEXT_NODE || !editorEl.contains(node)) return null;
    const text = (node as Text).data;
    const before = text.slice(0, range.startOffset);
    const match = TOKEN_BEFORE.exec(before);
    if (!match) return null;
    const query = match[2];
    return {
      query,
      node: node as Text,
      nodeStart: range.startOffset - query.length - 1,
      nodeEnd: range.startOffset,
    };
  }

  function applyToken(token: MentionToken, skill: ComposerSkill): void {
    const { node } = token;
    if (!node.isConnected || !editorEl.contains(node)) return;
    const text = node.data;
    // 先定位再改文本：改完 node 的偏移就失效了
    const after = text.slice(token.nodeEnd);
    node.data = text.slice(0, token.nodeStart);

    const parent = node.parentNode;
    if (!parent) return;
    const chip = makeChip(skill);
    parent.insertBefore(chip, node.nextSibling);
    // 与手写 `@id ` 同口径：胶囊后补一个空格，吞掉原位置的重复空白
    const tail = document.createTextNode(` ${after.replace(/^[ \u00a0]+/, '')}`);
    parent.insertBefore(tail, chip.nextSibling);

    editorEl.focus();
    collapseCaretTo(tail, 1);
    emitInput();
  }

  /** 一行纯文本按 `@id` 拆开：能解析的技能落成胶囊，其余保持文本。 */
  function appendInline(line: string, parent: Node, hydrate: boolean): void {
    let last = 0;
    TOKEN_INLINE.lastIndex = 0;
    let match: RegExpExecArray | null;
    while ((match = TOKEN_INLINE.exec(line))) {
      const tokenStart = match.index + match[1].length;
      const skill = hydrate ? props.resolveSkill?.(match[2]) ?? null : null;
      if (!skill) continue;
      parent.appendChild(document.createTextNode(line.slice(last, tokenStart)));
      parent.appendChild(makeChip(skill));
      last = tokenStart + match[2].length + 1;
    }
    parent.appendChild(document.createTextNode(line.slice(last)));
  }

  function setText(text: string, hydrate = true): void {
    editorEl.replaceChildren();
    const lines = text.split('\n');
    lines.forEach((line, index) => {
      if (index > 0) editorEl.appendChild(document.createElement('br'));
      appendInline(line, editorEl, hydrate);
    });
    emitInput();
  }

  function clear(): void {
    setText('');
  }

  function focus(): void {
    editorEl.focus();
    const range = document.createRange();
    range.selectNodeContents(editorEl);
    range.collapse(false);
    const selection = window.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(range);
  }

  /** 光标紧邻胶囊（或胶囊后那个空格）时，退格整体删掉胶囊。 */
  function deleteChipBefore(): boolean {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0 || !selection.isCollapsed) return false;
    const range = selection.getRangeAt(0);
    const container = range.startContainer;
    const offset = range.startOffset;
    let prev: Node | null = null;
    if (container.nodeType === Node.TEXT_NODE) {
      if (offset === 0) prev = container.previousSibling;
      else if (offset === 1 && /^[ \u00a0]/.test((container as Text).data)) prev = container.previousSibling;
    } else {
      prev = container.childNodes[offset - 1] ?? null;
    }
    if (!isChip(prev)) return false;
    removeChip(prev);
    return true;
  }

  /** 空格处把已完整键入的 `@id` 落成胶囊；返回 true 表示已消费这次按键。 */
  function convertAtCaret(): boolean {
    const token = tokenAtCaret();
    if (!token || !token.query) return false;
    const skill = props.resolveSkill?.(token.query) ?? null;
    if (!skill) return false;
    applyToken(token, skill);
    return true;
  }

  /** Shift+Enter：插一个换行，并在后面留一个零宽占位（不占文本，序列化时剔除）。 */
  function insertLineBreak(): void {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return;
    const range = selection.getRangeAt(0);
    if (!range.collapsed) range.deleteContents();
    const br = document.createElement('br');
    const filler = document.createTextNode('\u200b');
    range.insertNode(filler);
    filler.parentNode?.insertBefore(br, filler);
    collapseCaretTo(filler, 0);
    emitInput();
  }

  /** 粘贴纯文本：按行插入 `<br>`，末尾留零宽占位放光标（不经 execCommand，换行不会被吞）。 */
  function insertPlainText(value: string): void {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return;
    const range = selection.getRangeAt(0);
    if (!range.collapsed) range.deleteContents();

    const fragment = document.createDocumentFragment();
    value.replace(/\r\n?/g, '\n').split('\n').forEach((line, index) => {
      if (index > 0) fragment.appendChild(document.createElement('br'));
      if (line) fragment.appendChild(document.createTextNode(line));
    });
    const filler = document.createTextNode('\u200b');
    fragment.appendChild(filler);
    range.insertNode(fragment);
    collapseCaretTo(filler, 0);
    emitInput();
  }

  function handleKeyDown(event: KeyboardEvent): void {
    if (event.isComposing) return;

    if (event.key === 'Enter' && event.shiftKey) {
      event.preventDefault();
      insertLineBreak();
      return;
    }

    if (event.key === ' ' || event.key === 'Spacebar') {
      if (convertAtCaret()) {
        event.preventDefault();
        return;
      }
    }

    if (event.key === 'Backspace' && deleteChipBefore()) {
      event.preventDefault();
      return;
    }

    // 候选导航与发送仍由页面决定
    props.onKeyDown?.(event);
  }

  function handleKeyUp(event: KeyboardEvent): void {
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight' || event.key === 'Home' || event.key === 'End') {
      notifyCaret();
    }
  }

  function handlePaste(event: ClipboardEvent): void {
    const files = Array.from(event.clipboardData?.files ?? []);
    if (files.length > 0) {
      props.onPaste?.(event);
      return;
    }
    // 只收纯文本：富文本粘贴会把外部样式塞进输入区
    event.preventDefault();
    const text = event.clipboardData?.getData('text/plain') ?? '';
    if (text) insertPlainText(text);
  }

  createEffect(() => {
    if (!editorEl) return;
    editorEl.contentEditable = props.disabled ? 'false' : 'true';
  });

  const handle: ComposerEditorHandle = {
    text: serialize,
    setText,
    clear,
    focus,
    tokenAtCaret,
    applyToken,
  };
  props.api?.(handle);

  return (
    <div
      ref={editorEl}
      class="composer-editor is-empty"
      contenteditable="true"
      spellcheck={false}
      data-placeholder={props.placeholder}
      onInput={() => emitInput()}
      onKeyDown={handleKeyDown}
      onKeyUp={handleKeyUp}
      onClick={() => notifyCaret()}
      onFocus={() => props.onFocus?.()}
      onBlur={() => props.onBlur?.()}
      onPaste={handlePaste}
    />
  );
}