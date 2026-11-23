'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const taskPath = path.resolve(__dirname, '../LubanAgent.Plugin.YunXiao/web/assets/task.js');
const taskSource = fs.readFileSync(taskPath, 'utf8');
const elementIds = [
  'task-serial', 'task-status', 'task-title', 'task-assignee', 'task-due',
  'task-description', 'task-acceptance', 'task-parent', 'task-sprint-name',
  'task-code-context', 'task-warnings', 'task-comments', 'task-comment-count',
  'task-comment-error', 'task-comment-input', 'btn-task-comment', 'task-workspace',
  'task-seed-summary', 'task-session-title', 'task-session-status', 'task-messages',
  'task-followup-input', 'btn-task-followup', 'btn-task-start', 'btn-task-restart',
  'btn-task-cancel', 'task-back-board'
];

function makeDetail() {
  return {
    workitemId: 'work-1',
    serialNumber: 'TASK-1',
    title: '运行时测试任务',
    statusName: '待处理',
    description: '任务描述',
    acceptance: '验收标准',
    canStart: true,
    seedPrompt: '请处理这个任务',
    seedCommentCount: 0,
    comments: [],
    warnings: [],
    codeContext: {
      workspaceRoot: 'D:/repo',
      repoName: 'repo',
      branch: 'main',
      ends: []
    }
  };
}

function encodeSse(events) {
  return events.map(function (item) {
    return 'event: ' + item.event + '\n'
      + 'data: ' + JSON.stringify(item.data || {}) + '\n\n';
  }).join('');
}

function sseResponse(events) {
  const bytes = new TextEncoder().encode(encodeSse(events));
  let sent = false;
  return {
    ok: true,
    status: 200,
    body: {
      getReader: function () {
        return {
          read: async function () {
            if (!sent) {
              sent = true;
              return { value: bytes, done: false };
            }
            return { value: undefined, done: true };
          }
        };
      }
    }
  };
}

function failingStreamResponse(message) {
  return {
    ok: true,
    status: 200,
    body: {
      getReader: function () {
        return {
          read: async function () {
            throw new Error(message);
          }
        };
      }
    }
  };
}

function gatedSseResponse(events) {
  const bytes = new TextEncoder().encode(encodeSse(events));
  let markStarted;
  let openGate;
  const started = new Promise(function (resolve) { markStarted = resolve; });
  const gate = new Promise(function (resolve) { openGate = resolve; });
  let sent = false;
  return {
    started: started,
    release: function () { openGate(); },
    response: {
      ok: true,
      status: 200,
      body: {
        getReader: function () {
          return {
            read: async function () {
              if (!sent) {
                sent = true;
                markStarted();
                await gate;
                return { value: bytes, done: false };
              }
              return { value: undefined, done: true };
            }
          };
        }
      }
    }
  };
}

function createHarness(options) {
  options = options || {};
  const elements = {};
  elementIds.forEach(function (id) {
    elements[id] = {
      id: id,
      value: '',
      textContent: '',
      innerHTML: '',
      hidden: false,
      disabled: false,
      href: '',
      scrollTop: 0,
      scrollHeight: 0,
      listeners: {},
      addEventListener: function (event, handler) {
        this.listeners[event] = handler;
      }
    };
  });

  const calls = { get: [], post: [], fetch: [] };
  const errors = [];
  const toasts = [];
  const detail = options.detail || makeDetail();
  const binding = Object.prototype.hasOwnProperty.call(options, 'binding')
    ? options.binding
    : null;
  const latestMessages = options.latestMessages || [];
  const getHandlers = Object.assign({
    '/yunxiao/taskdetail': function () { return detail; },
    '/yunxiao/tasksession': function () { return binding; },
    '/session/latestmessages': function () { return latestMessages; },
    '/workspace/byrootpath': function () { return { workspaceId: 'workspace-1' }; }
  }, options.get || {});
  const postHandlers = Object.assign({
    '/yunxiao/tasksessionsave': function (payload) { return Object.assign({}, payload); },
    '/yunxiao/taskcomment': function () { return {}; },
    '/agent/active': function () { return { active: false }; },
    '/agent/cancel': function () { return {}; },
    '/workspace/create': function () { return { workspaceId: 'workspace-1' }; },
    '/session/create': function () { return { sessionId: 'session-1' }; }
  }, options.post || {});

  const api = {
    get: async function (route, query) {
      calls.get.push({ path: route, query: query });
      const handler = getHandlers[route];
      if (!handler) throw new Error('未 mock GET ' + route);
      return handler(query, calls.get.filter(function (call) { return call.path === route; }).length);
    },
    post: async function (route, payload) {
      calls.post.push({ path: route, body: payload });
      const handler = postHandlers[route];
      if (!handler) throw new Error('未 mock POST ' + route);
      return handler(payload, calls.post.filter(function (call) { return call.path === route; }).length);
    }
  };

  const window = {
    YunxiaoApi: api,
    YunxiaoApp: {
      showError: function (message) { errors.push(String(message || '')); },
      showToast: function (message, kind) { toasts.push({ message: String(message || ''), kind: kind }); }
    },
    location: {
      hash: '#/task?projectId=project-1&workitemId=work-1&sprintId=sprint-1&name=demo'
    },
    fetch: async function (route, request) {
      const envelope = JSON.parse(request.body);
      const call = {
        path: route,
        request: request,
        payload: JSON.parse(envelope.payload)
      };
      calls.fetch.push(call);
      if (!options.fetch) throw new Error('未 mock fetch ' + route);
      return options.fetch(route, call.payload, calls.fetch.length);
    }
  };
  window.window = window;

  const context = {
    window: window,
    document: {
      getElementById: function (id) { return elements[id] || null; }
    },
    TextDecoder: TextDecoder,
    Uint8Array: Uint8Array,
    encodeURIComponent: encodeURIComponent,
    decodeURIComponent: decodeURIComponent,
    console: console
  };
  vm.runInNewContext(taskSource, context, { filename: taskPath });

  return {
    task: window.YunxiaoTask,
    elements: elements,
    calls: calls,
    errors: errors,
    toasts: toasts
  };
}

async function enter(harness) {
  harness.task.init();
  await harness.task.enter();
}

function saves(harness) {
  return harness.calls.post.filter(function (call) {
    return call.path === '/yunxiao/tasksessionsave';
  });
}

function posts(harness, route) {
  return harness.calls.post.filter(function (call) { return call.path === route; });
}

test('取消后的 error 与 done 最终保存 cancelled=4', async function () {
  const stream = gatedSseResponse([
    { event: 'error', data: { message: '本轮对话已取消' } },
    { event: 'done', data: {} }
  ]);
  const harness = createHarness({
    fetch: function (route) {
      assert.equal(route, '/agent/chat');
      return stream.response;
    }
  });
  await enter(harness);

  const starting = harness.elements['btn-task-start'].listeners.click();
  await stream.started;
  await harness.elements['btn-task-cancel'].listeners.click();
  stream.release();
  await starting;

  assert.equal(posts(harness, '/agent/cancel').length, 1);
  assert.equal(posts(harness, '/agent/cancel')[0].body.sessionId, 'session-1');
  const saved = saves(harness);
  assert.equal(saved[0].body.status, 2);
  assert.equal(saved[saved.length - 1].body.status, 4);
});

test('chat 读取异常后 active=true，attach done 最终保存 completed=3', async function () {
  const harness = createHarness({
    post: {
      '/agent/active': function () { return { active: true, runId: 'run-1' }; }
    },
    fetch: function (route) {
      if (route === '/agent/chat') return failingStreamResponse('读取中断');
      if (route === '/agent/attach') {
        return sseResponse([
          { event: 'run.snapshot', data: { runId: 'run-1', text: '续接成功' } },
          { event: 'done', data: {} }
        ]);
      }
      throw new Error('意外 fetch ' + route);
    }
  });
  await enter(harness);
  await harness.elements['btn-task-start'].listeners.click();

  assert.deepEqual(harness.calls.fetch.map(function (call) { return call.path; }), [
    '/agent/chat', '/agent/attach'
  ]);
  assert.equal(posts(harness, '/agent/active').length, 1);
  const saved = saves(harness);
  assert.equal(saved[saved.length - 1].body.status, 3);
  assert.equal(saved.some(function (call) { return call.body.status === 5; }), false);
});

test('无 done 的 EOF 且 active=false 时恢复消息并保存 created=1', async function () {
  const harness = createHarness({
    latestMessages: [{ role: 'assistant', content: '无法确认前的最后消息' }],
    fetch: function (route) {
      assert.equal(route, '/agent/chat');
      return sseResponse([{ event: 'text.delta', data: { delta: '未完成输出' } }]);
    }
  });
  await enter(harness);
  await harness.elements['btn-task-start'].listeners.click();

  assert.equal(posts(harness, '/agent/active').length, 1);
  const saved = saves(harness);
  assert.equal(saved[saved.length - 1].body.status, 1);
  assert.equal(saved.some(function (call) { return call.body.status === 5; }), false);
  assert.equal(harness.calls.get.filter(function (call) {
    return call.path === '/session/latestmessages';
  }).length, 1);
  assert.match(harness.elements['task-messages'].innerHTML, /无法确认前的最后消息/);
});

test('enter 发现旧 Running 且 active=false 时校准为 created=1', async function () {
  const harness = createHarness({
    binding: {
      linkId: 'link-1',
      workItemId: 'work-1',
      workspaceId: 'workspace-1',
      sessionId: 'session-old',
      status: 2,
      result: null
    },
    latestMessages: [{ role: 'assistant', content: '进入页面前的历史消息' }]
  });
  await enter(harness);

  assert.equal(posts(harness, '/agent/active').length, 1);
  const saved = saves(harness);
  assert.equal(saved.length, 1);
  assert.equal(saved[0].body.sessionId, 'session-old');
  assert.equal(saved[0].body.status, 1);
  assert.match(harness.elements['task-messages'].innerHTML, /进入页面前的历史消息/);
});

test('评论 POST 成功但详情刷新失败时明确提示已发布', async function () {
  let detailReads = 0;
  const harness = createHarness({
    get: {
      '/yunxiao/taskdetail': function () {
        detailReads += 1;
        if (detailReads > 1) throw new Error('详情接口不可用');
        return makeDetail();
      }
    }
  });
  await enter(harness);
  harness.elements['task-comment-input'].value = '一条新评论';
  await harness.elements['btn-task-comment'].listeners.click();

  assert.equal(posts(harness, '/yunxiao/taskcomment').length, 1);
  assert.equal(harness.elements['task-comment-input'].value, '');
  const notices = harness.errors.concat(harness.toasts.map(function (toast) { return toast.message; }));
  assert.equal(notices.some(function (message) {
    return message.includes('评论已发布') && message.includes('详情刷新失败');
  }), true, '实际提示：' + JSON.stringify(notices));
  assert.equal(harness.errors.some(function (message) {
    return message.includes('发布评论失败');
  }), false);
});

test('重新开始的前置工作区 API 失败不写旧绑定 Failed 且恢复旧消息', async function () {
  const harness = createHarness({
    binding: {
      linkId: 'link-old',
      workItemId: 'work-1',
      workspaceId: 'workspace-old',
      sessionId: 'session-old',
      status: 3,
      result: '上一轮结果'
    },
    latestMessages: [{ role: 'assistant', content: '旧会话消息仍在' }],
    get: {
      '/workspace/byrootpath': function () { throw new Error('工作区读取失败'); }
    }
  });
  await enter(harness);
  await harness.elements['btn-task-restart'].listeners.click();

  assert.equal(saves(harness).length, 0);
  assert.equal(harness.calls.get.filter(function (call) {
    return call.path === '/session/latestmessages';
  }).length, 2);
  assert.match(harness.elements['task-messages'].innerHTML, /旧会话消息仍在/);
});
