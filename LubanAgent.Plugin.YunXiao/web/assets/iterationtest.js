(function (global) {
  'use strict';

  /**
   * 迭代测试（F9）：按来源生成用例 → 勾选用例范围 → 用例与测试计划落库。
   * 与 PRD 拆分共用 #/prd 屏，通过顶部 Tab 切换。
   */

  var els = {};
  var projectId = '';
  var projectName = '';
  var cases = [];

  var ESC = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

  function $(id) {
    return document.getElementById(id);
  }

  function escapeHtml(value) {
    return String(value == null ? '' : value).replace(/[&<>"']/g, function (ch) { return ESC[ch]; });
  }

  function showResult(el, message, ok) {
    if (!el) return;
    el.hidden = false;
    el.className = 'result ' + (ok ? 'result-ok' : 'result-err');
    el.textContent = message;
  }

  function hideResult(el) {
    if (!el) return;
    el.hidden = true;
    el.textContent = '';
  }

  /** 读 hash 中的项目上下文。 */
  function readQuery() {
    var hash = global.location.hash || '';
    var index = hash.indexOf('?');
    var query = {};
    if (index < 0) return query;
    hash.slice(index + 1).split('&').forEach(function (pair) {
      if (!pair) return;
      var kv = pair.split('=');
      query[decodeURIComponent(kv[0])] = decodeURIComponent(kv[1] || '');
    });
    return query;
  }

  /** 已勾选的来源。 */
  function pickedSources() {
    var sources = [];
    if (els.srcNeeds.checked) sources.push('needs');
    if (els.srcPrd.checked) sources.push('prd');
    if (els.srcTasks.checked) sources.push('tasks');
    if (els.srcManual.checked) sources.push('manual');
    return sources;
  }

  /** 刷新「手工输入」块的显隐。 */
  function syncManualUi() {
    els.manualBlock.hidden = !els.srcManual.checked;
  }

  /** 渲染用例清单（默认全选，可取消勾选=缩小用例范围）。 */
  function renderCases(result) {
    cases = result.cases || [];
    els.caseTag.textContent = cases.length ? String(cases.length) + ' 条' : '空';
    els.empty.hidden = cases.length > 0;
    els.casePanel.hidden = cases.length === 0;
    els.planPanel.hidden = cases.length === 0;
    els.caseCount.textContent = String(cases.length);

    els.caseTree.innerHTML = cases.map(function (item, index) {
      var sources = (item.sources || []).join('、');
      var paragraph = item.sourceParagraph
        ? '<span class="prd-meta">来源：' + escapeHtml(item.sourceParagraph) + '</span>'
        : '';
      var rest = item.steps ? ' · ' + escapeHtml(item.steps) : '';
      return '<label class="prd-node prd-sub it-case">'
        + '<input type="checkbox" class="it-case-check" data-index="' + index + '" checked />'
        + '<b>' + escapeHtml(item.title || '') + '</b>'
        + (sources ? '<span class="prd-badge prd-badge-task">' + escapeHtml(sources) + '</span>' : '')
        + paragraph
        + (rest ? '<span class="prd-meta">' + rest + '</span>' : '')
        + '</label>';
    }).join('');

    updatePicked();
    els.caseTree.querySelectorAll('.it-case-check').forEach(function (box) {
      box.addEventListener('change', updatePicked);
    });

    var warnings = result.warnings || [];
    showResult(
      els.result,
      warnings.length
        ? '已生成 ' + cases.length + ' 条用例；' + warnings.join('；')
        : '已生成 ' + cases.length + ' 条用例',
      true);

    if (!els.planName.value) {
      els.planName.value = (projectName || '迭代') + ' 测试计划';
    }
    els.planPanel.hidden = false;
  }

  function updatePicked() {
    var picked = els.caseTree.querySelectorAll('.it-case-check:checked').length;
    els.casePicked.textContent = String(picked);
  }

  /** 渲染可用状态候选（V6-3：取自项目现有工作项）。 */
  function renderStatuses(info) {
    var statuses = (info && info.statuses) || [];
    if (!statuses.length) {
      els.statusPanel.hidden = !!info && !!info.message;
      return;
    }

    var rows = statuses.map(function (item) {
      return '<span class="kv-k">' + escapeHtml(item.category || '') + '</span>'
        + '<span class="kv-v">' + escapeHtml(item.name || '') + '</span>';
    }).join('');
    els.statusList.innerHTML = rows;
    els.statusPanel.hidden = false;
  }

  async function generate() {
    if (!projectId) {
      showResult(els.result, '缺少项目上下文，请从项目列表选择项目后进入', false);
      return;
    }

    var sources = pickedSources();
    if (!sources.length) {
      showResult(els.result, '请至少勾选一个用例来源', false);
      return;
    }
    if (sources.length === 1 && sources[0] === 'manual' && !els.manual.value.trim()) {
      showResult(els.result, '已勾选「手工输入」，请先填写正文', false);
      return;
    }

    els.generate.disabled = true;
    showResult(els.result, '正在生成用例（单次模型调用，请稍候）…', true);

    try {
      var result = await global.YunxiaoApi.post('/yunxiao/iterationtestpreview', {
        projectId: projectId,
        sprintId: (els.sprint.value || '').trim() || null,
        sources: sources,
        manualContent: els.manual.value,
        requirementKeys: null
      });

      if (!result || !(result.cases || []).length) {
        els.cases = [];
        els.caseTag.textContent = '空';
        els.empty.hidden = false;
        els.casePanel.hidden = true;
        els.planPanel.hidden = true;
        els.receiptPanel.hidden = true;
        showResult(els.result, '来源内容不足以生成用例：请补充手工输入或更换来源', false);
        return;
      }

      renderCases(result);
    } catch (e) {
      showResult(els.result, e && e.message ? e.message : '用例生成失败', false);
    } finally {
      els.generate.disabled = false;
    }
  }

  async function save() {
    if (!projectId) {
      showResult(els.result, '缺少项目上下文，请从项目列表选择项目后进入', false);
      return;
    }

    var picked = [];
    els.caseTree.querySelectorAll('.it-case-check:checked').forEach(function (box) {
      var index = Number(box.getAttribute('data-index'));
      if (cases[index]) picked.push(cases[index]);
    });

    if (!picked.length && !(els.planName.value || '').trim()) {
      showResult(els.result, '请至少勾选一条用例，或填写测试计划名称', false);
      return;
    }

    els.save.disabled = true;
    showResult(els.result, '正在逐项保存到云效…', true);

    try {
      var result = await global.YunxiaoApi.post('/yunxiao/iterationtestsave', {
        projectId: projectId,
        sprintId: (els.sprint.value || '').trim() || null,
        cases: picked,
        testPlan: {
          name: (els.planName.value || '').trim() || null,
          environment: (els.planEnv.value || '').trim() || null,
          executor: (els.planExec.value || '').trim() || null,
          startDate: els.planStart.value || null,
          endDate: els.planEnd.value || null
        }
      });

      renderReceipts(result);
    } catch (e) {
      showResult(els.result, e && e.message ? e.message : '保存失败', false);
    } finally {
      els.save.disabled = false;
    }
  }

  function renderReceipts(data) {
    els.receiptPanel.hidden = false;
    els.okCount.textContent = String(data.okCount || 0);
    els.failCount.textContent = String(data.failedCount || 0);
    els.planOut.textContent = data.testPlanId || '—';

    var receipts = data.receipts || [];
    els.receipts.innerHTML = receipts.map(function (item) {
      var state = item.ok ? 'ok' : 'fail';
      var suffix = item.reused ? ' · 已复用' : '';
      var message = !item.ok && item.message ? '：' + item.message : '';
      var paragraph = item.sourceParagraph ? '<span class="prd-receipt-src">「' + escapeHtml(item.sourceParagraph) + '」</span>' : '';
      return '<div class="prd-receipt ' + state + '">'
        + '<span class="prd-receipt-kind">' + escapeHtml(item.kind || '') + '</span>'
        + '<span class="prd-receipt-title">' + escapeHtml(item.title || '') + suffix + message + '</span>'
        + paragraph
        + '</div>';
    }).join('');

    var warnings = data.warnings || [];
    showResult(
      els.result,
      '保存完成：成功 ' + (data.okCount || 0) + ' 项，失败 ' + (data.failedCount || 0) + ' 项'
        + (warnings.length ? '；' + warnings.join('；') : ''),
      (data.failedCount || 0) === 0);
  }

  /** Tab 切换。 */
  function switchTab(name) {
    var split = name !== 'test';
    $('prd-tab-split').hidden = !split;
    $('prd-tab-test').hidden = split;
    $('tab-prd-split').className = 'prd-tab' + (split ? ' is-active' : '');
    $('tab-prd-test').className = 'prd-tab' + (split ? '' : ' is-active');

    // 迭代测试的项目上下文只在 enter() 里读取；首次切入时补一次，避免空 projectId 直接打到后端
    if (!split && !projectId) {
      enter();
    }
  }

  function reset() {
    cases = [];
    els.caseTag.textContent = '未生成';
    els.caseTree.innerHTML = '';
    els.caseCount.textContent = '0';
    els.casePicked.textContent = '0';
    els.empty.hidden = false;
    els.casePanel.hidden = true;
    els.planPanel.hidden = true;
    els.receiptPanel.hidden = true;
    els.statusPanel.hidden = true;
    els.statusList.innerHTML = '';
    els.receipts.innerHTML = '';
    els.okCount.textContent = '—';
    els.failCount.textContent = '—';
    els.planOut.textContent = '—';
    hideResult(els.result);
  }

  function init() {
    els = {
      projectTag: $('it-project-tag'),
      srcNeeds: $('it-src-needs'),
      srcPrd: $('it-src-prd'),
      srcTasks: $('it-src-tasks'),
      srcManual: $('it-src-manual'),
      sprint: $('it-sprint'),
      manualBlock: $('it-manual-blk'),
      manual: $('it-manual'),
      generate: $('btn-it-generate'),
      result: $('it-result'),
      statusPanel: $('it-status-panel'),
      statusList: $('it-status-list'),
      empty: $('it-empty'),
      casePanel: $('it-case-panel'),
      caseTree: $('it-case-tree'),
      caseTag: $('it-case-tag'),
      caseCount: $('it-case-count'),
      casePicked: $('it-case-picked'),
      planPanel: $('it-plan-panel'),
      planName: $('it-plan-name'),
      planEnv: $('it-plan-env'),
      planExec: $('it-plan-exec'),
      planStart: $('it-plan-start'),
      planEnd: $('it-plan-end'),
      save: $('btn-it-save'),
      receiptPanel: $('it-receipt-panel'),
      okCount: $('it-ok-count'),
      failCount: $('it-fail-count'),
      planOut: $('it-plan-out'),
      receipts: $('it-receipts')
    };

    if (!els.generate) return;

    document.querySelectorAll('[data-prd-tab]').forEach(function (tab) {
      tab.addEventListener('click', function () { switchTab(tab.getAttribute('data-prd-tab')); });
    });

    els.srcManual.addEventListener('change', syncManualUi);
    els.generate.addEventListener('click', generate);
    els.save.addEventListener('click', save);
    syncManualUi();
  }

  /** 进入屏：刷新项目上下文与状态候选。 */
  async function enter() {
    var query = readQuery();
    projectId = query.projectId || '';
    projectName = query.name || query.identifier || '';
    els.projectTag.textContent = projectName || projectId || '未指定项目';
    els.projectTag.title = projectId;

    reset();

    if (!projectId) {
      showResult(els.result, '缺少项目上下文，请先从项目列表选择项目并点击「PRD 拆分」', false);
      return;
    }

    try {
      var info = await global.YunxiaoApi.get('/yunxiao/prdsource', { projectId: projectId });
      if (info && info.projectName) {
        projectName = info.projectName;
        els.projectTag.textContent = info.projectName;
      }
      renderStatuses(info);
    } catch (e) {
      // 来源信息只是辅助展示，失败不阻断生成
    }
  }

  global.YunxiaoIterationTest = { init: init, enter: enter, switchTab: switchTab };
})(window);
