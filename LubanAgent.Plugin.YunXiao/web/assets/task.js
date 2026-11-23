/*
  云效 DevOps 工作台 · F2 任务详情与薄会话面板。
  只呈现任务上下文、评论和宿主会话回显，不复制宿主的完整聊天能力。
*/
(function (global) {
  'use strict';

  const api = global.YunxiaoApi;
  const app = global.YunxiaoApp;
  const SessionStatus = {
    notStarted: 0, created: 1, running: 2, completed: 3, cancelled: 4, failed: 5
  };

  function $(id) {
    return document.getElementById(id);
  }

  const els = {
    serial: $('task-serial'),
    status: $('task-status'),
    title: $('task-title'),
    assignee: $('task-assignee'),
    due: $('task-due'),
    description: $('task-description'),
    acceptance: $('task-acceptance'),
    parent: $('task-parent'),
    sprint: $('task-sprint-name'),
    code: $('task-code-context'),
    warnings: $('task-warnings'),
    comments: $('task-comments'),
    commentCount: $('task-comment-count'),
    commentError: $('task-comment-error'),
    commentInput: $('task-comment-input'),
    commentButton: $('btn-task-comment'),
    workspace: $('task-workspace'),
    seedSummary: $('task-seed-summary'),
    sessionTitle: $('task-session-title'),
    sessionStatus: $('task-session-status'),
    messages: $('task-messages'),
    followupInput: $('task-followup-input'),
    followupButton: $('btn-task-followup'),
    startButton: $('btn-task-start'),
    restartButton: $('btn-task-restart'),
    cancelButton: $('btn-task-cancel'),
    back: $('task-back-board')
  };

  const state = {
    projectId: '',
    workitemId: '',
    sprintId: '',
    projectName: '',
    detail: null,
    binding: null,
    sessionTitle: '',
    messages: [],
    live: null,
    running: false,
    starting: false,
    commenting: false,
    runId: '',
    streamDone: false,
    streamFailed: false,
    cancelRequested: false
  };

  function esc(value) {
    return String(value == null ? '' : value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function readQuery() {
    const hash = global.location.hash || '';
    const index = hash.indexOf('?');
    const query = {};
    if (index < 0) return query;
    hash.slice(index + 1).split('&').forEach(function (pair) {
      if (!pair) return;
      const eq = pair.indexOf('=');
      const key = eq < 0 ? pair : pair.slice(0, eq);
      const value = eq < 0 ? '' : pair.slice(eq + 1);
      query[decodeURIComponent(key)] = decodeURIComponent(value.replace(/\+/g, ' '));
    });
    return query;
  }

  function showError(error, fallback) {
    if (app && app.showError) app.showError(error && error.message ? error.message : fallback);
  }

  function formatTime(value) {
    if (!value) return '时间未知';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString('zh-CN', { hour12: false });
  }

  function makeSessionTitle() {
    const detail = state.detail;
    if (!detail) return '';
    return '任务 ' + (detail.serialNumber || detail.workitemId) + ' · ' + detail.title;
  }

  function buildSeedDigest() {
    const detail = state.detail;
    const code = detail.codeContext || {};
    return [
      detail.title,
      '描述 ' + (detail.description || '').length + ' 字',
      '评论 ' + detail.seedCommentCount + ' 条',
      (code.repoName || '未登记代码库') + '@' + (code.branch || '未登记分支')
    ].join(' · ');
  }

  function renderCodeContext(code) {
    if (!code) {
      els.code.innerHTML = '<div class="task-empty">尚未登记工程上下文</div>';
      return;
    }
    const ends = (code.ends || []).map(function (end) {
      return '<div class="task-code-end"><strong>' + esc(end.end) + '</strong>'
        + '<span>' + esc(end.dir || '目录未登记') + '</span>'
        + '<small>' + esc(end.pipelineName || '流水线未登记') + '</small></div>';
    }).join('');
    els.code.innerHTML = '<div class="task-code-repo">' + esc(code.repoName || '代码库未登记')
      + '<span>' + esc(code.branch || '分支未登记') + '</span></div>'
      + (ends || '<div class="task-empty">未登记各端目录</div>');
  }

  function renderComments(detail) {
    els.commentCount.textContent = detail.commentError ? '—' : String(detail.commentCount || 0);
    els.commentError.hidden = !detail.commentError;
    els.commentError.textContent = detail.commentError || '';
    const comments = detail.comments || [];
    els.comments.innerHTML = comments.length ? comments.map(function (comment) {
      return '<article class="task-comment' + (comment.isLatest ? ' is-latest' : '') + '">'
        + '<header><strong>' + esc(comment.author || '未知用户') + '</strong>'
        + (comment.isLatest ? '<span class="task-latest">最新</span>' : '')
        + '<time>' + esc(formatTime(comment.createdAt)) + '</time></header>'
        + '<div class="task-comment-content">' + esc(comment.content) + '</div></article>';
    }).join('') : '<div class="task-empty">' + (detail.commentError ? '评论暂不可用' : '暂无评论') + '</div>';
  }

  function renderDetail() {
    const detail = state.detail;
    if (!detail) return;
    els.serial.textContent = detail.serialNumber || detail.workitemId;
    els.status.textContent = detail.statusName || detail.statusNameEn || '状态未知';
    els.title.textContent = detail.title;
    els.assignee.textContent = detail.assignee || '未指派';
    els.due.textContent = detail.dueDate || '预计完成未设';
    els.description.textContent = detail.description || '（未填写）';
    els.acceptance.textContent = detail.acceptance || '（未填写）';
    els.parent.textContent = detail.parentRequirement
      ? detail.parentRequirement.displayName
      : '无父需求';
    els.sprint.textContent = detail.sprintName || detail.sprintId || '未登记';
    renderCodeContext(detail.codeContext);
    renderComments(detail);

    const warnings = (detail.warnings || []).filter(Boolean);
    els.warnings.hidden = warnings.length === 0;
    els.warnings.textContent = warnings.join('；');

    const code = detail.codeContext || {};
    els.workspace.textContent = code.workspaceRoot || '未登记工作区目录，无法开始任务';
    els.seedSummary.textContent = '标题、描述（' + (detail.description || '').length + ' 字）、验收标准 · '
      + (detail.commentError ? '评论暂不可用' : '最近 ' + detail.seedCommentCount + ' 条评论')
      + ' · ' + (code.repoName || '代码库未登记') + ' / ' + (code.branch || '分支未登记');
    if (!state.sessionTitle) state.sessionTitle = makeSessionTitle();
    renderSession();
  }

  function statusText() {
    if (state.starting) return '正在创建';
    if (state.running) return '进行中';
    if (!state.binding) return '未开始';
    const names = {
      [SessionStatus.created]: '会话已创建', [SessionStatus.running]: '进行中',
      [SessionStatus.completed]: '已完成', [SessionStatus.cancelled]: '已取消',
      [SessionStatus.failed]: '失败', [SessionStatus.notStarted]: '未开始'
    };
    return names[state.binding.status] || '会话已创建';
  }

  function renderActions() {
    const detail = state.detail;
    const code = detail && detail.codeContext;
    const canStart = !!(detail && detail.canStart && code && code.workspaceRoot);
    const busy = state.running || state.starting;
    els.startButton.hidden = !!state.binding;
    els.restartButton.hidden = !state.binding;
    els.startButton.disabled = !canStart || busy;
    els.restartButton.disabled = !canStart || busy;
    els.cancelButton.disabled = !state.running;
    els.followupButton.disabled = !state.binding || busy || !els.followupInput.value.trim();
    els.followupInput.disabled = !state.binding || busy;
    els.commentButton.disabled = state.commenting || !els.commentInput.value.trim();
  }

  function renderMessage(message) {
    const role = message.role === 'user' ? '我' : message.role === 'system' ? '系统' : 'Agent';
    return '<article class="task-message is-' + esc(message.role || 'assistant') + '">'
      + '<div class="task-message-role">' + role + '</div>'
      + (message.thinking ? '<details class="task-thinking"><summary>思考过程</summary><div>'
        + esc(message.thinking) + '</div></details>' : '')
      + '<div class="task-message-content">' + esc(message.content || '') + '</div></article>';
  }

  function renderLive() {
    if (!state.live) return '';
    const live = state.live;
    const tools = (live.tools || []).map(function (tool) {
      const detail = tool.error || tool.resultSummary || tool.status || '';
      return '<div class="task-tool is-' + esc(tool.status || 'running') + '">'
        + '<strong>' + esc(tool.name || '工具调用') + '</strong><span>' + esc(detail) + '</span></div>';
    }).join('');
    return (live.user ? renderMessage({ role: 'user', content: live.user }) : '')
      + '<article class="task-message is-assistant is-live">'
      + '<div class="task-message-role">Agent' + (state.running ? ' · 正在响应' : '') + '</div>'
      + (live.thinking ? '<details class="task-thinking"><summary>思考过程</summary><div>'
        + esc(live.thinking) + '</div></details>' : '')
      + '<div class="task-message-content">' + esc(live.text || '') + '</div>'
      + tools
      + (live.notice ? '<div class="task-live-notice">' + esc(live.notice) + '</div>' : '')
      + (live.error ? '<div class="task-live-error">' + esc(live.error) + '</div>' : '')
      + '</article>';
  }

  function renderMessages() {
    const history = state.messages.map(renderMessage).join('');
    const live = renderLive();
    els.messages.innerHTML = history + live
      || '<div class="task-empty">开始任务后，这里只展示会话标题与最近消息。</div>';
    els.messages.scrollTop = els.messages.scrollHeight;
  }

  function renderSession() {
    els.sessionTitle.textContent = state.binding
      ? (state.sessionTitle || makeSessionTitle())
      : '尚未创建会话';
    els.sessionStatus.textContent = statusText();
    renderMessages();
    renderActions();
  }

  async function loadDetail() {
    const detail = await api.get('/yunxiao/taskdetail', {
      projectId: state.projectId,
      workitemId: state.workitemId
    });
    state.detail = detail;
    renderDetail();
  }

  async function restoreMessages() {
    if (!state.binding) {
      state.messages = [];
      renderMessages();
      return;
    }
    const messages = await api.get('/session/latestmessages', {
      sessionId: state.binding.sessionId,
      count: 20
    });
    state.messages = messages || [];
    state.live = null;
    renderMessages();
  }

  async function saveBinding(status, result) {
    if (!state.binding) return;
    const payload = Object.assign({}, state.binding, {
      workItemId: state.workitemId,
      status: status,
      result: result || state.binding.result || null
    });
    try {
      state.binding = await api.post('/yunxiao/tasksessionsave', payload);
    } catch (error) {
      showError(error, '保存任务会话绑定失败');
    }
  }

  function findTool(callId) {
    if (!state.live) return null;
    return (state.live.tools || []).find(function (tool) { return tool.callId === callId; });
  }

  function handleSse(event, data) {
    if (!state.live) state.live = { user: '', text: '', thinking: '', tools: [] };
    if (event === 'run.snapshot') {
      state.runId = data.runId || state.runId;
      state.live.text = data.text || '';
      state.live.thinking = data.thinking || '';
      state.live.tools = data.tools || [];
      state.live.notice = data.pendingConfirmation ? '有工具调用等待确认，请在宿主会话中处理。' : '';
    } else if (event === 'text.delta') {
      state.live.text += data.delta || '';
    } else if (event === 'think.delta') {
      state.live.thinking += data.delta || '';
    } else if (event === 'tool.started') {
      state.live.tools.push({ callId: data.callId, name: data.name, status: 'running' });
    } else if (event === 'tool.completed' || event === 'tool.failed') {
      const tool = findTool(data.callId);
      if (tool) {
        tool.status = event === 'tool.completed' ? 'done' : 'failed';
        tool.resultSummary = data.resultSummary;
        tool.error = data.error;
      }
    } else if (event === 'tool.confirm') {
      state.live.notice = '有工具调用等待确认，请在宿主会话中处理。';
    } else if (event === 'orchestration.progress' && data.message) {
      state.live.notice = data.message;
    } else if (event === 'session.title') {
      state.sessionTitle = data.title || state.sessionTitle;
      els.sessionTitle.textContent = state.sessionTitle;
    } else if (event === 'error') {
      state.streamFailed = true;
      state.live.error = data.message || '会话失败';
    } else if (event === 'done') {
      state.streamDone = true;
    }
    renderMessages();
  }

  async function postSse(path, body) {
    const response = await global.fetch(path, {
      method: 'POST',
      headers: {
        Accept: 'text/event-stream',
        'Content-Type': 'application/json; charset=utf-8'
      },
      body: JSON.stringify({ payload: JSON.stringify(body) }),
      credentials: 'same-origin'
    });
    if (response.status === 204) return false;
    if (!response.ok) {
      const text = await response.text();
      let message = text;
      try {
        const envelope = JSON.parse(text);
        message = envelope.message || message;
      } catch (e) { /* 非 JSON 错误直接展示原文 */ }
      throw new Error(message || ('请求失败（HTTP ' + response.status + '）'));
    }
    if (!response.body) throw new Error('浏览器不支持流式响应');

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    function dispatch(raw) {
      const lines = raw.split(/\r?\n/);
      let event = 'message';
      const data = [];
      lines.forEach(function (line) {
        if (line.indexOf('event:') === 0) event = line.slice(6).trim();
        if (line.indexOf('data:') === 0) data.push(line.slice(5).trimStart());
      });
      if (!data.length) return;
      let payload = {};
      try { payload = JSON.parse(data.join('\n')); } catch (e) { return; }
      handleSse(event, payload);
    }

    while (true) {
      const part = await reader.read();
      buffer += decoder.decode(part.value || new Uint8Array(), { stream: !part.done });
      let boundary = /\r?\n\r?\n/.exec(buffer);
      while (boundary) {
        dispatch(buffer.slice(0, boundary.index));
        buffer = buffer.slice(boundary.index + boundary[0].length);
        boundary = /\r?\n\r?\n/.exec(buffer);
      }
      if (part.done) break;
    }
    if (buffer.trim()) dispatch(buffer);
    return true;
  }

  async function runConversation(prompt, attach) {
    if (!state.binding || state.running) return;
    state.running = true;
    state.streamDone = false;
    state.streamFailed = false;
    state.cancelRequested = false;
    state.live = { user: attach ? '' : prompt, text: '', thinking: '', tools: [] };
    renderSession();
    let recoveryAttempted = false;
    try {
      while (true) {
        const path = attach ? '/agent/attach' : '/agent/chat';
        const body = attach
          ? { sessionId: state.binding.sessionId, runId: state.runId || null }
          : {
              prompt: prompt,
              sessionId: state.binding.sessionId,
              enableTools: true,
              permissionMode: 'plan'
            };
        let transportError = null;
        try {
          const streamed = await postSse(path, body);
          if (!streamed) state.live = null;
        } catch (error) {
          transportError = error;
          if (state.live) state.live.error = error && error.message ? error.message : '会话连接失败';
        }

        if (state.cancelRequested && (state.streamFailed || state.streamDone)) {
          await saveBinding(SessionStatus.cancelled, state.live && state.live.error);
          if (state.streamDone) {
            try { await restoreMessages(); } catch (error) { showError(error, '恢复会话消息失败'); }
          }
          break;
        }
        if (state.streamFailed) {
          await saveBinding(SessionStatus.failed, state.live && state.live.error);
          break;
        }
        if (state.streamDone) {
          await saveBinding(SessionStatus.completed);
          try { await restoreMessages(); } catch (error) { showError(error, '恢复会话消息失败'); }
          break;
        }

        if (!recoveryAttempted) {
          recoveryAttempted = true;
          let active = null;
          try {
            active = await api.post('/agent/active', { sessionId: state.binding.sessionId });
          } catch (error) {
            if (!transportError) transportError = error;
          }
          if (active && active.active) {
            attach = true;
            state.runId = active.runId || '';
            state.streamDone = false;
            state.streamFailed = false;
            state.live = { user: '', text: '', thinking: '', tools: [] };
            renderSession();
            continue;
          }
        }

        try { await restoreMessages(); } catch (error) { showError(error, '恢复会话消息失败'); }
        if (state.binding && state.binding.status === SessionStatus.running) {
          await saveBinding(SessionStatus.created);
        }
        if (transportError) showError(transportError, '会话连接失败');
        break;
      }
    } finally {
      state.running = false;
      state.runId = '';
      renderSession();
    }
  }

  async function resumeActive() {
    if (!state.binding) return;
    try {
      const active = await api.post('/agent/active', { sessionId: state.binding.sessionId });
      if (active && active.active) {
        state.runId = active.runId || '';
        await runConversation('', true);
      } else if (state.binding.status === SessionStatus.running) {
        await saveBinding(SessionStatus.created);
        renderSession();
      }
    } catch (error) {
      showError(error, '读取会话运行状态失败');
    }
  }

  async function startTask() {
    const detail = state.detail;
    const code = detail && detail.codeContext;
    if (!detail || !detail.canStart || !code || !code.workspaceRoot || state.running || state.starting) return;
    const previousBinding = state.binding;
    const previousMessages = state.messages;
    let bindingEstablished = false;
    state.starting = true;
    state.messages = [];
    state.live = null;
    state.sessionTitle = makeSessionTitle();
    renderActions();
    try {
      let workspace = await api.get('/workspace/byrootpath', { rootPath: code.workspaceRoot });
      if (!workspace) {
        workspace = await api.post('/workspace/create', {
          rootPath: code.workspaceRoot,
          name: code.repoName || detail.title,
          type: 'Normal'
        });
      }
      const session = await api.post('/session/create', {
        title: state.sessionTitle,
        workspaceId: workspace.workspaceId
      });
      state.binding = {
        linkId: '',
        workItemId: state.workitemId,
        workspaceId: workspace.workspaceId,
        sessionId: session.sessionId,
        seedDigest: buildSeedDigest(),
        seedCommentCount: detail.seedCommentCount,
        seedAt: new Date().toISOString(),
        status: SessionStatus.running,
        result: null
      };
      bindingEstablished = true;
      state.binding = await api.post('/yunxiao/tasksessionsave', state.binding);
      state.starting = false;
      renderSession();
      await runConversation(detail.seedPrompt, false);
    } catch (error) {
      showError(error, '开始任务失败');
      if (bindingEstablished) {
        await saveBinding(SessionStatus.failed, error && error.message);
      } else {
        state.binding = previousBinding;
        try {
          await restoreMessages();
        } catch (restoreError) {
          state.messages = previousMessages;
          state.live = null;
          showError(restoreError, '恢复会话消息失败');
        }
      }
    } finally {
      state.starting = false;
      renderSession();
    }
  }

  async function createComment() {
    const content = els.commentInput.value.trim();
    if (!content || state.commenting) return;
    state.commenting = true;
    renderActions();
    let published = false;
    try {
      await api.post('/yunxiao/taskcomment', {
        projectId: state.projectId,
        workitemId: state.workitemId,
        content: content
      });
      published = true;
      els.commentInput.value = '';
      await loadDetail();
      if (app && app.showToast) app.showToast('评论已发布', 'ok');
    } catch (error) {
      if (published) showError(null, '评论已发布，但详情刷新失败');
      else showError(error, '发布评论失败');
    } finally {
      state.commenting = false;
      renderActions();
    }
  }

  async function cancel() {
    if (!state.binding || !state.running) return;
    state.cancelRequested = true;
    els.cancelButton.disabled = true;
    try {
      await api.post('/agent/cancel', {
        sessionId: state.binding.sessionId,
        runId: state.runId || null
      });
      if (app && app.showToast) app.showToast('已请求取消当前回合', 'ok');
    } catch (error) {
      state.cancelRequested = false;
      showError(error, '取消会话失败');
      renderActions();
    }
  }

  function init() {
    els.commentInput.addEventListener('input', renderActions);
    els.followupInput.addEventListener('input', renderActions);
    els.commentButton.addEventListener('click', createComment);
    els.startButton.addEventListener('click', startTask);
    els.restartButton.addEventListener('click', startTask);
    els.cancelButton.addEventListener('click', cancel);
    els.followupButton.addEventListener('click', function () {
      const prompt = els.followupInput.value.trim();
      if (!prompt || state.running) return;
      els.followupInput.value = '';
      runConversation(prompt, false);
    });
  }

  async function enter() {
    const query = readQuery();
    state.projectId = query.projectId || '';
    state.workitemId = query.workitemId || '';
    state.sprintId = query.sprintId || '';
    state.projectName = query.name || '';
    state.detail = null;
    state.binding = null;
    state.messages = [];
    state.live = null;
    state.running = false;
    state.starting = false;
    state.sessionTitle = '';
    els.title.textContent = '加载中…';
    els.messages.innerHTML = '';
    els.back.href = '#/board?projectId=' + encodeURIComponent(state.projectId)
      + '&sprintId=' + encodeURIComponent(state.sprintId)
      + '&name=' + encodeURIComponent(state.projectName);

    try {
      await loadDetail();
    } catch (error) {
      els.title.textContent = '任务详情读取失败';
      showError(error, '读取任务详情失败');
      renderActions();
      return;
    }
    try {
      state.binding = await api.get('/yunxiao/tasksession', { workitemId: state.workitemId });
      renderSession();
      if (state.binding) {
        await restoreMessages();
        await resumeActive();
      }
    } catch (error) {
      showError(error, '恢复任务会话失败');
    }
  }

  global.YunxiaoTask = { init: init, enter: enter };
})(window);
