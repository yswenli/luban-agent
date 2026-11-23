/*
  云效 DevOps 工作台 · 屏 5 迭代任务看板（F1）。

  约束：
  1. 列名 / 列序 / 列数全部来自后端实例反查结果，页面不硬编码任何状态；
  2. 任务卡标题两行截断、悬停显示完整标题；编号与日期单行省略；
  3. 列多时看板横向滚动，列内任务纵向滚动；
  4. 请求 GET /yunxiao/board?projectId=&sprintId=，错误走全局提示条。
*/
(function (global) {
  'use strict';

  const api = global.YunxiaoApi;
  const app = global.YunxiaoApp;

  function $(id) {
    return document.getElementById(id);
  }

  const els = {
    project: $('board-project'),
    sprint: $('board-sprint'),
    range: $('board-sprint-range'),
    dueField: $('board-due-field'),
    wrap: $('board-wrap'),
    empty: $('board-empty'),
    cols: $('board-cols'),
    summary: $('board-summary-text'),
    reload: $('btn-board-reload')
  };

  const state = { projectId: '', sprintId: '', loading: false };

  function esc(text) {
    return String(text == null ? '' : text)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  /** 从 hash 读项目上下文（#/board?projectId=&identifier=&name=）。 */
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

  function showEmpty(text) {
    els.empty.textContent = text;
    els.empty.hidden = false;
    els.cols.hidden = true;
  }

  function renderSprints(sprints, currentId) {
    els.sprint.innerHTML = sprints.map(function (item) {
      return '<option value="' + esc(item.id) + '"'
        + (item.id === currentId ? ' selected' : '') + '>'
        + esc(item.name) + '</option>';
    }).join('');
    els.sprint.disabled = sprints.length === 0;
  }

  function renderSummary(board) {
    const parts = ['共 ' + board.totalTasks + ' 条任务'];
    parts.push(board.columns.length + ' 个状态列');
    if (board.doneCount > 0) parts.push('已完成 ' + board.doneCount);
    if (board.overdueCount > 0) parts.push('逾期 ' + board.overdueCount);
    els.summary.textContent = parts.join(' · ');

    els.range.textContent = board.sprintStartDate || board.sprintEndDate
      ? '迭代周期 ' + (board.sprintStartDate || '—') + ' ~ ' + (board.sprintEndDate || '—')
      : '迭代周期未设置';

    if (board.dueDateResolved) {
      els.dueField.hidden = false;
      els.dueField.textContent = '预计完成字段：' + (board.dueDateFieldName || '已解析');
      els.dueField.classList.remove('is-warn');
    } else {
      els.dueField.hidden = false;
      els.dueField.textContent = '未取到「预计完成时间」字段，列内按编号排序';
      els.dueField.classList.add('is-warn');
    }
  }

  function renderCard(task) {
    const due = task.dueDate
      ? '<span class="board-card-due' + (task.overdue ? ' is-overdue' : '') + '">'
        + esc(task.dueDate) + (task.overdue ? ' 逾期' : '') + '</span>'
      : '<span class="board-card-due is-empty">预计完成未设</span>';

    const href = '#/task?projectId=' + encodeURIComponent(state.projectId)
      + '&workitemId=' + encodeURIComponent(task.id)
      + '&sprintId=' + encodeURIComponent(state.sprintId);

    return '<a class="board-card" href="' + esc(href) + '" title="' + esc(task.title) + '">'
      + '<div class="board-card-title">' + esc(task.title) + '</div>'
      + '<div class="board-card-meta">'
      + '<span class="board-card-serial">' + esc(task.serialNumber || '—') + '</span>'
      + due
      + '</div>'
      + '<div class="board-card-assignee">' + esc(task.assignee || '未指派') + '</div>'
      + '</a>';
  }

  function renderColumns(columns) {
    els.cols.innerHTML = columns.map(function (column) {
      return '<section class="board-col">'
        + '<header class="board-col-head">'
        + '<span class="board-col-name" title="' + esc(column.statusName) + '">' + esc(column.statusName) + '</span>'
        + '<span class="board-col-count">' + column.tasks.length + '</span>'
        + '</header>'
        + '<div class="board-col-body">'
        + (column.tasks.length ? column.tasks.map(renderCard).join('') : '<div class="board-col-empty">暂无任务</div>')
        + '</div>'
        + '</section>';
    }).join('');
    els.cols.hidden = false;
    els.empty.hidden = true;
  }

  function render(board) {
    els.project.textContent = board.projectName || board.projectId || '—';
    renderSprints(board.sprints || [], board.sprintId);
    renderSummary(board);

    if (!board.sprintId) {
      showEmpty('该项目还没有迭代，先在云效创建迭代后再查看');
      return;
    }
    if (!board.columns.length) {
      showEmpty('该迭代没有任务，或任务状态暂不可见');
      return;
    }
    renderColumns(board.columns);

    const warnings = (board.warnings || []).filter(Boolean);
    if (warnings.length && app && app.showToast) {
      app.showToast(warnings[0], 'err');
    }
  }

  async function load(sprintId) {
    if (!state.projectId) {
      showEmpty('未指定项目，请从项目列表进入');
      return;
    }
    if (state.loading) return;

    state.loading = true;
    els.empty.textContent = '加载中…';
    els.empty.hidden = false;
    els.cols.hidden = true;

    try {
      const board = await api.get('/yunxiao/board', {
        projectId: state.projectId,
        sprintId: sprintId || state.sprintId || ''
      });
      state.sprintId = board.sprintId || '';
      render(board);
    } catch (e) {
      showEmpty('读取看板失败：' + (e && e.message ? e.message : '未知错误'));
    } finally {
      state.loading = false;
    }
  }

  function init() {
    els.sprint.addEventListener('change', function () {
      state.sprintId = els.sprint.value;
      load(state.sprintId);
    });
    if (els.reload) {
      els.reload.addEventListener('click', function () {
        load(state.sprintId);
      });
    }
  }

  function enter() {
    const query = readQuery();
    state.projectId = query.projectId || '';
    state.sprintId = query.sprintId || '';
    els.project.textContent = query.name || state.projectId || '—';
    els.summary.textContent = '—';
    els.range.textContent = '—';
    els.dueField.hidden = true;
    load(state.sprintId);
  }

  global.YunxiaoBoard = { init: init, enter: enter };
})(window);
