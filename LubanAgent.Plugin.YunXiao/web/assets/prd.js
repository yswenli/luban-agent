/*
  云效 DevOps 工作台 · 屏 4 PRD 拆分（S3：同步 → 拆分预览 → 落库）。

  约束：
  1. 项目上下文从 hash 查询串读取（projectId / identifier / name），缺 projectId 时提示返回项目列表；
  2. 来源：云效需求（默认，按迭代过滤）+ 本地上传 / 粘贴正文；工作区 / 企微云盘保持灰态；
  3. 拆分由宿主一次性调用模型完成，前端只负责收集来源、展示计划树与回执；
  4. 云效数据与模型输出均属外部输入：渲染一律 escapeHtml，禁止裸 innerHTML 拼接。
*/
(function (global) {
  'use strict';

  const api = global.YunxiaoApi;
  const app = global.YunxiaoApp;

  let els = null;
  let projectId = '';
  let plan = null;

  function escapeHtml(value) {
    return String(value === undefined || value === null ? '' : value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function $(id) {
    return document.getElementById(id);
  }

  /** 解析 hash 查询串（#/prd?projectId=…&name=…）。 */
  function readQuery() {
    const hash = global.location.hash || '';
    const index = hash.indexOf('?');
    const result = {};
    if (index < 0) return result;
    hash.slice(index + 1).split('&').forEach(function (pair) {
      if (!pair) return;
      const pos = pair.indexOf('=');
      if (pos < 0) return;
      const key = decodeURIComponent(pair.slice(0, pos));
      const value = decodeURIComponent(pair.slice(pos + 1).replace(/\+/g, ' '));
      result[key] = value;
    });
    return result;
  }

  function statusText(status) {
    switch (status) {
      case 'ready': return '同步完成';
      case 'partial': return '部分完成（有告警）';
      case 'empty': return '无可同步需求';
      case 'failed': return '同步失败';
      default: return status || '—';
    }
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

  /** 来源切换：本地块只在「本地上传」时展开。 */
  function syncSourceUi() {
    const isLocal = els.source.value === 'local';
    els.localBlock.hidden = !isLocal;
  }

  /** 读取本地文件到文本框（只接受文本类文件）。 */
  function readFile() {
    const file = els.file.files && els.file.files[0];
    if (!file) return;
    const reader = new global.FileReader();
    reader.onload = function () {
      els.text.value = String(reader.result || '');
      els.text.dataset.name = file.name;
    };
    reader.onerror = function () {
      app.showError('读取文件失败');
    };
    reader.readAsText(file, 'utf-8');
  }

  /** 收集补充文档：文本框有内容才带。 */
  function collectDocuments() {
    const content = (els.text.value || '').trim();
    if (!content) return [];
    return [{ name: els.text.dataset.name || '本地 PRD', content: content }];
  }

  function sprintId() {
    const value = (els.sprint.value || '').trim();
    return value || null;
  }

  /** 渲染同步摘要。 */
  function renderSync(data) {
    els.syncPanel.hidden = false;
    els.status.textContent = statusText(data.status);
    els.reqCount.textContent = String((data.requirements || []).length);
    els.attCount.textContent = String((data.attachments || []).length);
    const warnings = data.warnings || [];
    els.warnings.textContent = warnings.length ? warnings.join('；') : (data.message || '—');
    els.warnings.title = els.warnings.textContent;

    if (data.status === 'failed' || data.status === 'empty') {
      showResult(els.syncResult, data.message || '云效暂无可同步需求，可切到「本地上传」继续', false);
    } else {
      showResult(els.syncResult, '已同步 ' + (data.requirements || []).length + ' 条需求', true);
    }
  }

  /** 计划树：迭代 → 需求（任务 / 用例）→ 测试计划。 */
  function renderPlan(result) {
    plan = result.plan || null;
    if (!plan) {
      els.planPanel.hidden = true;
      els.empty.hidden = false;
      return;
    }

    const requirements = plan.requirements || [];
    const tasks = plan.tasks || [];
    const testcases = plan.testcases || [];
    const iteration = plan.iteration || {};

    const parts = [];
    parts.push('<div class="prd-node prd-iter">'
      + '<span class="prd-badge">迭代</span>'
      + '<b>' + escapeHtml(iteration.name || '（未命名迭代）') + '</b>'
      + '<span class="prd-meta">' + escapeHtml(iteration.startDate || '—') + ' ~ ' + escapeHtml(iteration.endDate || '—') + '</span>'
      + '</div>');

    requirements.forEach(function (req) {
      parts.push('<div class="prd-node prd-req">'
        + '<span class="prd-badge">需求 ' + escapeHtml(req.key || '') + '</span>'
        + '<b>' + escapeHtml(req.title || '') + '</b>'
        + '<span class="prd-meta">' + escapeHtml(req.priority || '') + '</span>'
        + '</div>');

      tasks.filter(function (t) { return t.requirementKey === req.key; }).forEach(function (task) {
        parts.push('<div class="prd-node prd-sub">'
          + '<span class="prd-badge prd-badge-task">任务 ' + escapeHtml(task.key || '') + '</span>'
          + escapeHtml(task.title || '')
          + '</div>');
      });

      testcases.filter(function (c) { return c.requirementKey === req.key; }).forEach(function (testcase) {
        parts.push('<div class="prd-node prd-sub">'
          + '<span class="prd-badge prd-badge-case">用例 ' + escapeHtml(testcase.key || '') + '</span>'
          + escapeHtml(testcase.title || '')
          + '</div>');
      });
    });

    if (plan.testPlan && plan.testPlan.name) {
      parts.push('<div class="prd-node prd-plan">'
        + '<span class="prd-badge prd-badge-plan">测试计划</span>'
        + escapeHtml(plan.testPlan.name)
        + '</div>');
    }

    els.planTree.innerHTML = parts.join('');
    els.planPanel.hidden = false;
    els.empty.hidden = true;
    els.planTag.textContent = requirements.length + ' 需求 / ' + tasks.length + ' 任务 / ' + testcases.length + ' 用例';

    const warnings = result.warnings || [];
    if (warnings.length) {
      showResult(els.syncResult, warnings.join('；'), false);
    }
  }

  /** 执行同步。 */
  async function doSync() {
    if (!projectId) {
      app.showError('缺少项目上下文，请从项目列表进入');
      return;
    }
    els.sync.disabled = true;
    hideResult(els.syncResult);
    try {
      const data = await api.post('/yunxiao/prdsync', { projectId: projectId, sprintId: sprintId() });
      renderSync(data || {});
    } catch (e) {
      showResult(els.syncResult, (e && e.message) || '同步失败', false);
    } finally {
      els.sync.disabled = false;
    }
  }

  /** 生成拆分预览（同步 + 补充文档 → 模型）。 */
  async function doPreview() {
    if (!projectId) {
      app.showError('缺少项目上下文，请从项目列表进入');
      return;
    }
    els.preview.disabled = true;
    hideResult(els.syncResult);
    els.planTag.textContent = '生成中…';
    try {
      const data = await api.post('/yunxiao/prdsplitpreview', {
        projectId: projectId,
        sprintId: sprintId(),
        documents: collectDocuments()
      });
      renderPlan(data || {});
      app.showToast('拆分预览已生成', 'ok');
    } catch (e) {
      els.planTag.textContent = '生成失败';
      showResult(els.syncResult, (e && e.message) || '拆分预览失败', false);
    } finally {
      els.preview.disabled = false;
    }
  }

  /** 提交拆分计划（逐项落库，幂等重跑）。 */
  async function doSubmit() {
    if (!plan) {
      app.showError('请先生成拆分预览');
      return;
    }
    els.submit.disabled = true;
    hideResult(els.syncResult);
    try {
      const data = await api.post('/yunxiao/prdsplitsubmit', {
        projectId: projectId,
        sprintId: sprintId(),
        plan: plan
      });
      renderReceipts(data || {});
      if ((data && data.failedCount) > 0) {
        app.showToast('部分条目失败，修正后重试即可（已成功项会跳过）', 'err');
      } else {
        app.showToast('已提交到云效', 'ok');
      }
    } catch (e) {
      showResult(els.syncResult, (e && e.message) || '提交失败', false);
    } finally {
      els.submit.disabled = false;
    }
  }

  /** 回执表：成功 / 失败分色，失败带原因。 */
  function renderReceipts(data) {
    els.receiptPanel.hidden = false;
    els.okCount.textContent = String(data.okCount || 0);
    els.failCount.textContent = String(data.failedCount || 0);
    els.sprintOut.textContent = data.sprintId || '—';

    const receipts = data.receipts || [];
    els.receipts.innerHTML = receipts.map(function (item) {
      const state = item.ok ? 'ok' : 'fail';
      const suffix = item.reused ? ' · 已复用' : '';
      const message = !item.ok && item.message ? '：' + item.message : '';
      return '<div class="prd-receipt ' + state + '">'
        + '<span class="prd-receipt-kind">' + escapeHtml(item.kind || '') + '</span>'
        + '<span class="prd-receipt-title">' + escapeHtml(item.title || '') + suffix + message + '</span>'
        + '</div>';
    }).join('');

    const warnings = data.warnings || [];
    if (warnings.length) {
      showResult(els.syncResult, warnings.join('；'), false);
    }
  }

  /** 清空预览与回执。 */
  function reset() {
    plan = null;
    els.planPanel.hidden = true;
    els.receiptPanel.hidden = true;
    els.planTree.innerHTML = '';
    els.receipts.innerHTML = '';
    els.planTag.textContent = '未生成';
    els.empty.hidden = false;
    hideResult(els.syncResult);
  }

  function init() {
    els = {
      projectTag: $('prd-project-tag'),
      source: $('prd-source'),
      sprint: $('prd-sprint'),
      localBlock: $('prd-local-blk'),
      file: $('prd-file'),
      text: $('prd-text'),
      sync: $('btn-prd-sync'),
      preview: $('btn-prd-preview'),
      submit: $('btn-prd-submit'),
      reset: $('btn-prd-reset'),
      syncResult: $('prd-sync-result'),
      syncPanel: $('prd-sync-panel'),
      status: $('prd-status'),
      reqCount: $('prd-req-count'),
      attCount: $('prd-att-count'),
      warnings: $('prd-warnings'),
      empty: $('prd-empty'),
      planPanel: $('prd-plan-panel'),
      planTree: $('prd-plan-tree'),
      planTag: $('prd-plan-tag'),
      receiptPanel: $('prd-receipt-panel'),
      okCount: $('prd-ok-count'),
      failCount: $('prd-fail-count'),
      sprintOut: $('prd-sprint-out'),
      receipts: $('prd-receipts')
    };

    els.source.addEventListener('change', syncSourceUi);
    els.file.addEventListener('change', readFile);
    els.sync.addEventListener('click', doSync);
    els.preview.addEventListener('click', doPreview);
    els.submit.addEventListener('click', doSubmit);
    els.reset.addEventListener('click', reset);
  }

  /** 进入屏：读项目上下文并展示来源信息。 */
  async function enter() {
    const query = readQuery();
    projectId = query.projectId || '';
    const label = query.name || query.identifier || projectId || '未指定项目';
    els.projectTag.textContent = label;
    els.projectTag.title = projectId;
    els.projectTag.className = 'chip' + (projectId ? '' : ' chip-warn');
    reset();
    syncSourceUi();

    if (!projectId) {
      showResult(els.syncResult, '缺少项目上下文，请先从项目列表选择项目并点击「PRD 拆分」', false);
      return;
    }

    try {
      const info = await api.get('/yunxiao/prdsource', { projectId: projectId });
      if (info && info.projectName) {
        els.projectTag.textContent = info.projectName;
        els.projectTag.title = info.projectId || projectId;
      }
      if (info && info.hasTestRepo === false) {
        showResult(els.syncResult, '未发现可用的测试用例库：测试用例与测试计划会失败，其余资产可正常落库', false);
      }
    } catch (e) {
      // 来源信息只是辅助展示，失败不阻断拆分
    }
  }

  global.YunxiaoPrd = { init: init, enter: enter };
})(window);
