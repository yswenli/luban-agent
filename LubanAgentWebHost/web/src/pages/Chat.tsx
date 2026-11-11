import { createEffect, createMemo, createSignal, For, Index, onCleanup, onMount, Show } from 'solid-js';

import { ConfirmActionDialog, TextPromptDialog } from '../components/Dialog';
import { Icon } from '../components/Icons';
import { Markdown } from '../components/Markdown';
import { PopMenu, menuAt } from '../components/PopMenu';
import { post } from '../lib/api';
import { formatBytes, formatTime, isImageMediaType, truncate } from '../lib/format';
import { openSettings, openSessions } from '../lib/router';
import type { StoredAttachment, WebCommand } from '../lib/types';
import { workspaceColor, workspaceInitial } from '../lib/workspace';
import type { ChatItem, ToolCard } from '../stores/chat';
import { dropChat, getChat } from '../stores/chats';
import {
  bootstrap,
  commands,
  currentModelLabel,
  cyclePermissionMode,
  deleteSession,
  loadCommands,
  permissionModeKey,
  permissionModeLabel,
  reasoningEffort,
  refreshProviderModels,
  refreshSessions,
  renameSession,
  selectModel,
  selection,
  sessions,
  setReasoningEffort,
  showToast,
  workspaceName,
} from '../stores/app';

export function ChatPage(props: { sessionId: string }) {
  const chat = getChat(props.sessionId);

  const [input, setInput] = createSignal('');
  const [plusMenu, setPlusMenu] = createSignal<{ x: number; y: number } | null>(null);
  const [effortMenu, setEffortMenu] = createSignal<{ x: number; y: number } | null>(null);
  const [modelMenu, setModelMenu] = createSignal<{ x: number; y: number } | null>(null);
  const [modelsLoading, setModelsLoading] = createSignal(false);
  const [moreMenu, setMoreMenu] = createSignal<{ x: number; y: number } | null>(null);
  const [dragging, setDragging] = createSignal(false);
  const [focused, setFocused] = createSignal(false);
  const [commandIndex, setCommandIndex] = createSignal(0);
  const [helpOpen, setHelpOpen] = createSignal(false);
  const [dialog, setDialog] = createSignal<'rename' | 'delete' | null>(null);

  let scrollEl!: HTMLDivElement;
  let textareaEl!: HTMLTextAreaElement;
  let fileInputEl!: HTMLInputElement;

  const session = createMemo(() => sessions().find((item) => item.sessionId === props.sessionId) ?? null);
  const title = () => session()?.title || '未命名会话';
  const totalTokens = () => session()?.totalTokens ?? 0;

  const commandQuery = createMemo(() => {
    const value = input();
    if (!value.startsWith('/') || /\s/.test(value)) return null;
    return value.slice(1).toLowerCase();
  });

  const matchedCommands = createMemo<WebCommand[]>(() => {
    const query = commandQuery();
    if (query === null) return [];
    return commands().filter((command) => {
      if (command.name.slice(1).toLowerCase().startsWith(query)) return true;
      return command.aliases.some((alias) => alias.slice(1).toLowerCase().startsWith(query));
    });
  });

  onMount(() => {
    void loadCommands();
    void (async () => {
      // 整表覆盖必须先在快照重建之前完成
      await chat.loadHistory();
      await chat.resume();
    })();
    if (sessions().length === 0) void refreshSessions();
    // 不在这里 abort：会话实例常驻，切换页面时流式回合继续在后台跑
  });

  // 流式期间每条帧都会替换 items 数组，跟随到最新
  createEffect(() => {
    chat.items();
    if (!scrollEl) return;
    queueMicrotask(() => {
      scrollEl.scrollTop = scrollEl.scrollHeight;
    });
  });

  createEffect(() => {
    const list = matchedCommands();
    if (commandIndex() >= list.length) setCommandIndex(0);
  });

  function resizeTextarea() {
    if (!textareaEl) return;
    textareaEl.style.height = 'auto';
    textareaEl.style.height = `${Math.min(textareaEl.scrollHeight, 220)}px`;
  }

  async function submit() {
    if (chat.streaming()) {
      await chat.stop();
      return;
    }

    const text = input();
    if (!text.trim()) return;

    if (commandQuery() !== null) {
      const list = matchedCommands();
      const command = list[commandIndex()] ?? list[0];
      if (command) {
        runCommand(command);
        return;
      }
    }

    setInput('');
    queueMicrotask(resizeTextarea);
    await chat.send(text, permissionModeKey(), reasoningEffort());
  }

  function runCommand(command: WebCommand) {
    setInput('');
    queueMicrotask(resizeTextarea);

    const action = command.action;
    if (action.startsWith('openSettings:')) {
      openSettings(action.slice('openSettings:'.length));
      return;
    }

    switch (action) {
      case 'action:clearMessages':
        void clearMessages();
        return;
      case 'action:cyclePermissionMode':
        cyclePermissionMode();
        showToast(`权限模式：${permissionModeLabel()}`, 'info');
        return;
      case 'action:help':
        setHelpOpen(true);
        return;
      case 'action:showSessionStats':
        showToast(
          `累计 ${totalTokens()} Token · ${session()?.messageCount ?? 0} 条消息`,
          'info',
        );
        return;
      case 'action:openSessionList':
      case 'action:focusWorkspaces':
        openSessions();
        return;
      case 'action:openLlmWiki':
        showToast('本版暂不支持 LLM Wiki 管理', 'info');
        return;
      default:
        showToast(`暂不支持命令 ${command.name}`, 'info');
    }
  }

  async function clearMessages() {
    try {
      await post('/session/clearMessages', { sessionId: props.sessionId });
      await chat.loadHistory(true);
      await refreshSessions();
      showToast('已清空消息', 'success');
    } catch (error) {
      showToast((error as Error).message, 'error');
    }
  }

  async function addFiles(files: File[]) {
    await chat.addFiles(files);
  }

  /** 展开模型下拉：缓存为空时按供应商补齐一次列表（有 API Key 才会真正请求远端）。 */
  function openModelMenu(event: MouseEvent) {
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    setModelMenu(menuAt(rect, { width: 300, height: 320, gap: 6, align: 'start' }));

    const pending = (bootstrap()?.providers ?? []).filter((provider) => provider.models.length === 0);
    if (pending.length === 0) return;

    setModelsLoading(true);
    void Promise.all(pending.map((provider) => refreshProviderModels(provider.id))).finally(() =>
      setModelsLoading(false),
    );
  }

  function onDrop(event: DragEvent) {
    event.preventDefault();
    setDragging(false);
    const files = Array.from(event.dataTransfer?.files ?? []);
    void addFiles(files);
  }

  function onPaste(event: ClipboardEvent) {
    const files = Array.from(event.clipboardData?.files ?? []);
    if (files.length === 0) return;
    event.preventDefault();
    void addFiles(files);
  }

  const plusMenuHeight = 104;
  const effortOptions = ['', 'None', 'Low', 'Medium', 'High'] as const;

  return (
    <>
      <div class="chat-head">
        <span
          class="ws-badge sm"
          style={{ background: workspaceColor(session()?.workspaceId) }}
          title={workspaceName(session()?.workspaceId) || '未知项目'}
        >
          {workspaceInitial(workspaceName(session()?.workspaceId))}
        </span>
        <span class="chat-title" title={`${workspaceName(session()?.workspaceId)} / ${title()}`}>
          <Show when={workspaceName(session()?.workspaceId)}>
            <span class="faint">{workspaceName(session()?.workspaceId)} / </span>
          </Show>
          {title()}
        </span>
        <div class="spacer" />
        <Show when={totalTokens() > 0}>
          <span class="faint mono" title="累计 Token">
            {totalTokens()} tok
          </span>
        </Show>
        <button
          class="icon-btn"
          title="更多"
          onClick={(event) => {
            const rect = event.currentTarget.getBoundingClientRect();
            const position = menuAt(rect, { width: 172, height: 124, align: 'end' });
            setMoreMenu(position);
          }}
        >
          <Icon name="more" size={18} />
        </button>
      </div>

        <div class="chat-scroll" ref={(element) => (scrollEl = element)}>
          <div class="thread">
            <Show when={!chat.loadingHistory()} fallback={<div class="loading-line">加载消息…</div>}>
              <Show when={chat.historyError()}>
                <div class="notice error">
                  <Icon name="alert" size={16} />
                  <span>{chat.historyError()}</span>
                </div>
              </Show>

              <Show when={chat.items().length === 0 && !chat.historyError()}>
                <div class="empty">
                  <Icon name="spark" size={28} />
                  <h2>开始对话</h2>
                  <p>输入问题，或点左下角「+」加入图片与文件。</p>
                </div>
              </Show>

              <Index each={chat.items()}>{(item) => <ChatRow item={item} />}</Index>

              <Show when={chat.plans().length > 0}>
                <div class="plan-strip">
                  <span class="section-label">计划</span>
                  <For each={chat.plans()}>
                    {(entry, index) => (
                      <div class="plan-item">
                        <span class="idx">{index() + 1}.</span>
                        <span class="mono">{entry.toolName}</span>
                      </div>
                    )}
                  </For>
                </div>
              </Show>

              <Show when={chat.progress().length > 0}>
                <div class="plan-strip">
                  <span class="section-label">子代理进度</span>
                  <For each={chat.progress()}>{(text) => <div class="plan-item">{text}</div>}</For>
                </div>
              </Show>

              <Show when={chat.streaming()}>
                <div class="typing">
                  <i />
                  <i />
                  <i />
                </div>
              </Show>
            </Show>
          </div>
        </div>

        <div class="composer-wrap">
          <div
            class="composer"
            classList={{ focused: focused(), dragging: dragging() }}
            onDragOver={(event) => {
              event.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={onDrop}
          >
            <Show when={chat.pending().length > 0}>
              <div class="composer-pending">
                <For each={chat.pending()}>
                  {(item) => (
                    <div class="pending-item" classList={{ uploading: item.status === 'uploading', error: item.status === 'error' }}>
                      <Show
                        when={isImageMediaType(item.mediaType) && item.previewUrl}
                        fallback={
                          <span class="fileicon">
                            <Icon name={isImageMediaType(item.mediaType) ? 'image' : 'file'} size={16} />
                          </span>
                        }
                      >
                        <img class="thumb" src={item.previewUrl} alt={item.fileName} />
                      </Show>
                      <span class="meta">
                        <span class="name" title={item.fileName}>
                          {item.fileName}
                        </span>
                        <span class="size">
                          {item.status === 'uploading' ? '上传中…' : item.status === 'error' ? item.error : formatBytes(item.size)}
                        </span>
                      </span>
                      <button class="remove" title="移除" onClick={() => chat.removePending(item.id)}>
                        <Icon name="close" size={13} />
                      </button>
                    </div>
                  )}
                </For>
              </div>
            </Show>

            <textarea
              ref={(element) => (textareaEl = element)}
              value={input()}
              rows={1}
              placeholder="给 Luban Agent 发送消息（Enter 发送，Shift+Enter 换行）"
              onInput={(event) => {
                setInput(event.currentTarget.value);
                setCommandIndex(0);
                resizeTextarea();
              }}
              onFocus={() => setFocused(true)}
              onBlur={() => setFocused(false)}
              onPaste={onPaste}
              onKeyDown={(event) => {
                if (event.isComposing) return;

                const list = matchedCommands();
                if (list.length > 0) {
                  if (event.key === 'ArrowDown') {
                    event.preventDefault();
                    setCommandIndex((index) => (index + 1) % list.length);
                    return;
                  }
                  if (event.key === 'ArrowUp') {
                    event.preventDefault();
                    setCommandIndex((index) => (index - 1 + list.length) % list.length);
                    return;
                  }
                  if (event.key === 'Escape') {
                    event.preventDefault();
                    setInput('');
                    return;
                  }
                }

                if (event.key === 'Enter' && !event.shiftKey) {
                  event.preventDefault();
                  void submit();
                }
              }}
            />

            <div class="composer-bar">
              <button
                class="pill-btn"
                title="添加图片和文件 / 命令"
                onClick={(event) => {
                  const rect = event.currentTarget.getBoundingClientRect();
                  setPlusMenu(menuAt(rect, { width: 214, height: plusMenuHeight, gap: 6, align: 'start' }));
                }}
              >
                <Icon name="plus" size={17} />
              </button>

              <button
                class="pill-btn value"
                title="切换模型（全局生效）"
                onClick={openModelMenu}
              >
                <Icon name="spark" size={14} />
                <span class="model-name">{currentModelLabel()}</span>
                <Icon name="chevron-down" size={13} />
              </button>

              <button
                class="pill-btn value"
                title="切换权限模式"
                onClick={() => {
                  cyclePermissionMode();
                }}
              >
                <Icon name="shield" size={14} />
                {permissionModeLabel()}
              </button>

              <button
                class="pill-btn value"
                title="推理强度"
                onClick={(event) => {
                  const rect = event.currentTarget.getBoundingClientRect();
                  setEffortMenu(menuAt(rect, { width: 170, height: 190, gap: 6, align: 'start' }));
                }}
              >
                <Icon name="brain" size={14} />
                {reasoningEffort() || '跟随模型'}
              </button>

              <div class="spacer" />

              <button
                class="send-btn"
                classList={{ stop: chat.streaming() }}
                title={chat.streaming() ? '停止本轮' : '发送'}
                disabled={!chat.streaming() && (!input().trim() || chat.pending().some((item) => item.status === 'uploading'))}
                onClick={() => void submit()}
              >
                <Icon name={chat.streaming() ? 'stop' : 'arrow-up'} size={17} />
              </button>
            </div>

            <Show when={matchedCommands().length > 0}>
              <div class="command-menu">
                <For each={matchedCommands()}>
                  {(command, index) => (
                    <button
                      class="command-row"
                      classList={{ active: index() === commandIndex() }}
                      onMouseEnter={() => setCommandIndex(index())}
                      onClick={() => runCommand(command)}
                    >
                      <span class="command-name">{command.name}</span>
                      <span class="command-desc">{command.description}</span>
                      <span class="faint mono">{command.usage}</span>
                    </button>
                  )}
                </For>
              </div>
            </Show>
          </div>

          <div class="hint">
            附件最多 {bootstrap()?.attachments.maxCount ?? 5} 个 · 支持拖放与粘贴 · Enter 发送 / Shift+Enter 换行
          </div>
        </div>

      <input
        ref={fileInputEl}
        type="file"
        multiple
        hidden
        onChange={(event) => {
          const files = Array.from(event.currentTarget.files ?? []);
          event.currentTarget.value = '';
          void addFiles(files);
        }}
      />

      <Show when={plusMenu()}>
        {(menu) => (
          <PopMenu style={{ left: `${menu().x}px`, top: `${menu().y}px`, 'min-width': '214px' }} onClose={() => setPlusMenu(null)}>
            <button
              class="menu-item"
              onClick={() => {
                setPlusMenu(null);
                fileInputEl?.click();
              }}
            >
              <Icon name="image" size={16} />
              图片和文件
              <div class="spacer" />
              <span class="sub">Ctrl+U</span>
            </button>
            <button
              class="menu-item"
              onClick={() => {
                setPlusMenu(null);
                setInput('/');
                queueMicrotask(() => {
                  textareaEl?.focus();
                  resizeTextarea();
                });
              }}
            >
              <Icon name="terminal" size={16} />
              命令
              <div class="spacer" />
              <span class="sub">/</span>
            </button>
          </PopMenu>
        )}
      </Show>

      <Show when={effortMenu()}>
        {(menu) => (
          <PopMenu style={{ left: `${menu().x}px`, top: `${menu().y}px`, 'min-width': '170px' }} onClose={() => setEffortMenu(null)}>
            <For each={effortOptions}>
              {(option) => (
                <button
                  class="menu-item"
                  classList={{ active: reasoningEffort() === option }}
                  onClick={() => {
                    setEffortMenu(null);
                    setReasoningEffort(option);
                  }}
                >
                  {option || '跟随模型'}
                  <div class="spacer" />
                  <Show when={reasoningEffort() === option}>
                    <Icon name="check" size={15} />
                  </Show>
                </button>
              )}
            </For>
          </PopMenu>
        )}
      </Show>

      <Show when={modelMenu()}>
        {(menu) => (
          <PopMenu style={{ left: `${menu().x}px`, top: `${menu().y}px`, width: '300px', 'max-height': '320px', overflow: 'auto' }} onClose={() => setModelMenu(null)}>
            <For each={bootstrap()?.providers ?? []}>
              {(provider) => (
                <>
                  <div class="menu-section">{provider.displayName}</div>
                  <For each={provider.models}>
                    {(model) => (
                      <button
                        class="menu-item"
                        classList={{
                          active: selection()?.providerId === provider.id && selection()?.modelId === model,
                        }}
                        onClick={() => {
                          setModelMenu(null);
                          void selectModel(provider.id, model);
                        }}
                      >
                        <span class="mono">{model}</span>
                        <div class="spacer" />
                        <Show when={selection()?.providerId === provider.id && selection()?.modelId === model}>
                          <Icon name="check" size={15} />
                        </Show>
                      </button>
                    )}
                  </For>
                </>
              )}
            </For>
            <Show when={(bootstrap()?.providers ?? []).length === 0}>
              <div class="menu-item faint">还没有供应商</div>
            </Show>
            <Show when={modelsLoading()}>
              <div class="menu-item faint">正在拉取模型列表…</div>
            </Show>
          </PopMenu>
        )}
      </Show>

      <Show when={moreMenu()}>
        {(menu) => (
          <PopMenu style={{ left: `${menu().x}px`, top: `${menu().y}px`, 'min-width': '172px' }} onClose={() => setMoreMenu(null)}>
            <button
              class="menu-item"
              onClick={() => {
                setMoreMenu(null);
                setDialog('rename');
              }}
            >
              <Icon name="pencil" size={15} />
              重命名
            </button>
            <button
              class="menu-item"
              onClick={() => {
                setMoreMenu(null);
                void clearMessages();
              }}
            >
              <Icon name="refresh" size={15} />
              清空消息
            </button>
            <button
              class="menu-item"
              onClick={() => {
                setMoreMenu(null);
                setDialog('delete');
              }}
            >
              <Icon name="trash" size={15} />
              删除会话
            </button>
          </PopMenu>
        )}
      </Show>

      <Show when={chat.confirmation()}>
        {(request) => (
          <ToolConfirmDialog
            request={request()}
            onResolve={(result) => void chat.resolveConfirm(result)}
            onTimeout={() => chat.confirmTimeout()}
          />
        )}
      </Show>

      <Show when={helpOpen()}>
        <HelpDialog onClose={() => setHelpOpen(false)} />
      </Show>

      <Show when={dialog() === 'rename'}>
        <TextPromptDialog
          title="重命名会话"
          label="标题"
          initialValue={title()}
          onClose={() => setDialog(null)}
          onConfirm={(value) => {
            setDialog(null);
            void renameSession(props.sessionId, value);
          }}
        />
      </Show>

      <Show when={dialog() === 'delete'}>
        <ConfirmActionDialog
          title="删除会话"
          message={`确定删除「${truncate(title(), 40)}」？该会话的消息将一并删除。`}
          onClose={() => setDialog(null)}
          onConfirm={() => {
            setDialog(null);
            void deleteSession(props.sessionId).then((ok) => {
              if (!ok) return;
              dropChat(props.sessionId);
              openSessions();
            });
          }}
        />
      </Show>
    </>
  );
}

function ChatRow(props: { item: () => ChatItem }) {
  const notice = () => {
    const value = props.item();
    return value.kind === 'notice' ? value : null;
  };
  const user = () => {
    const value = props.item();
    return value.kind === 'user' ? value : null;
  };
  const assistant = () => {
    const value = props.item();
    return value.kind === 'assistant' ? value : null;
  };

  return (
    <>
      <Show when={notice()}>
        {(item) => (
          <div class={`notice ${item().tone}`}>
            <Icon name={item().tone === 'error' || item().tone === 'warn' ? 'alert' : 'help'} size={15} />
            <span>{item().text}</span>
          </div>
        )}
      </Show>

      <Show when={user()}>
        {(item) => (
          <div class="msg msg-user">
            <Show when={item().attachments.length > 0}>
              <div class="attachments">
                <Index each={item().attachments}>
                  {(attachment) => <AttachmentChip attachment={attachment()} />}
                </Index>
              </div>
            </Show>
            <div class="bubble">{item().text}</div>
            <div class="msg-meta" style={{ 'justify-content': 'flex-end' }}>
              {formatTime(item().time)}
            </div>
          </div>
        )}
      </Show>

      <Show when={assistant()}>
        {(item) => (
          <div class="msg msg-assistant">
            <Show when={item().thinking.trim().length > 0}>
              <details class="thinking" classList={{ 'is-live': item().streaming && !item().text }}>
                <summary>
                  <Icon name="brain" size={14} />
                  思考过程
                </summary>
                <div class="thinking-body">{item().thinking}</div>
              </details>
            </Show>

            <Show when={item().tools.length > 0}>
              <Index each={item().tools}>{(tool) => <ToolCardView tool={tool} />}</Index>
            </Show>

            <Show when={item().text}>
              <div class="assistant-text">
                <Markdown text={item().text} />
              </div>
            </Show>

            <div class="msg-meta">
              <Show when={item().history}>
                <span class="status history">历史记录</span>
              </Show>
              <span>{formatTime(item().time)}</span>
            </div>
          </div>
        )}
      </Show>
    </>
  );
}

function AttachmentChip(props: { attachment: StoredAttachment }) {
  const name = () => props.attachment.FileName || '附件';
  const size = () => formatBytes(props.attachment.FileSize ?? 0);
  return (
    <div class="attachment-chip" title={props.attachment.SourcePath || name()}>
      <Show when={isImageMediaType(props.attachment.MediaType)} fallback={<Icon name="file" size={15} />}>
        <Icon name="image" size={15} />
      </Show>
      <span class="name">{name()}</span>
      <span class="faint">{size()}</span>
    </div>
  );
}

function ToolCardView(props: { tool: () => ToolCard }) {
  const [open, setOpen] = createSignal(false);

  const label = createMemo(() => {
    switch (props.tool().status) {
      case 'running':
        return '执行中';
      case 'done':
        return '完成';
      case 'failed':
        return '失败';
      case 'denied':
        return '已拒绝';
      default:
        return '历史记录';
    }
  });

  const summary = createMemo(() => {
    const text = props.tool().argumentsText.replace(/\s+/g, ' ').trim();
    return truncate(text, 90);
  });

  return (
    <div class="tool-card">
      <button class="tool-head" onClick={() => setOpen((value) => !value)}>
        <Icon name={open() ? 'chevron-down' : 'chevron-right'} size={14} />
        <span class="tool-name">{props.tool().name}</span>
        <span class="tool-summary">{summary()}</span>
        <span class={`status ${props.tool().status}`}>{label()}</span>
      </button>
      <Show when={open()}>
        <div class="tool-body">
          <div>
            <div class="tool-label">参数</div>
            <pre class="tool-pre">{props.tool().argumentsText || '（无参数）'}</pre>
          </div>
          <Show when={props.tool().resultSummary}>
            <div>
              <div class="tool-label">结果摘要</div>
              <pre class="tool-pre">{props.tool().resultSummary}</pre>
            </div>
          </Show>
          <Show when={props.tool().error}>
            <div>
              <div class="tool-label">错误</div>
              <pre class="tool-pre">{props.tool().error}</pre>
            </div>
          </Show>
          <Show when={props.tool().status === 'history'}>
            <div class="faint">历史回放只保留调用参数，不包含工具执行结果。</div>
          </Show>
        </div>
      </Show>
    </div>
  );
}

function ToolConfirmDialog(props: {
  request: { confirmationId: string; toolName: string; arguments: unknown; timeoutSeconds: number };
  onResolve: (result: 'allow' | 'deny' | 'allowAll' | 'allowSession') => void;
  onTimeout: () => void;
}) {
  const [remaining, setRemaining] = createSignal(Math.max(1, props.request.timeoutSeconds));

  onMount(() => {
    const timer = window.setInterval(() => {
      const next = remaining() - 1;
      if (next <= 0) {
        window.clearInterval(timer);
        props.onTimeout();
      } else {
        setRemaining(next);
      }
    }, 1000);
    onCleanup(() => window.clearInterval(timer));
  });

  const argsText = createMemo(() => {
    const value = props.request.arguments;
    if (value === null || value === undefined) return '（无参数）';
    if (typeof value === 'string') return value;
    try {
      return JSON.stringify(value, null, 2);
    } catch {
      return String(value);
    }
  });

  return (
    <div class="overlay">
      <div class="dialog" role="dialog" aria-modal="true">
        <div class="dialog-head">
          <div>
            <div class="dialog-title">允许执行 {props.request.toolName}？</div>
            <div class="dialog-sub">工具调用需要你确认</div>
          </div>
        </div>
        <div class="dialog-body">
          <div class="tool-label">参数</div>
          <pre class="tool-pre">{argsText()}</pre>
        </div>
        <div class="dialog-foot">
          <span class="countdown">{remaining()} 秒后按拒绝处理</span>
          <button class="btn ghost" onClick={() => props.onResolve('deny')}>
            拒绝
          </button>
          <button class="btn" onClick={() => props.onResolve('allowSession')}>
            本会话全允许
          </button>
          <button class="btn" onClick={() => props.onResolve('allowAll')}>
            本轮全允许
          </button>
          <button class="btn primary" onClick={() => props.onResolve('allow')}>
            允许
          </button>
        </div>
      </div>
    </div>
  );
}

function HelpDialog(props: { onClose: () => void }) {
  return (
    <div
      class="overlay"
      onClick={(event) => {
        if (event.target === event.currentTarget) props.onClose();
      }}
    >
      <div class="dialog" role="dialog" aria-modal="true">
        <div class="dialog-head">
          <div class="dialog-title">浏览器端可用命令</div>
          <div class="spacer" />
          <button class="icon-btn" onClick={props.onClose} title="关闭">
            <Icon name="close" size={17} />
          </button>
        </div>
        <div class="dialog-body">
          <For each={commands()}>
            {(command) => (
              <div class="command-row" style={{ cursor: 'default' }}>
                <span class="command-name">{command.name}</span>
                <span class="command-desc">{command.description}</span>
              </div>
            )}
          </For>
        </div>
        <div class="dialog-foot">
          <button class="btn primary" onClick={props.onClose}>
            知道了
          </button>
        </div>
      </div>
    </div>
  );
}