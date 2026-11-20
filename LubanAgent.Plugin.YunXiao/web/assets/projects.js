/*
  云效 DevOps 工作台 · 屏 2 项目列表（S1）。

  约束：
  1. 左栏搜索（回车 / 按钮）→ GET /yunxiao/projects?page=1&perPage=20&keyword=；
     项目多时左栏内部滚动（overflow-y:auto），不出整页滚动条；
  2. 列表项 name / identifier / updateTime：单行省略 + title 悬停全文；
  3. 右栏两态：未选中 → 组织统计卡（total + organizationId）；选中 → 项目详情卡
     （description 超长 3 行截断 + title）；
  4. 首屏进入自动拉一次列表；「重新加载」刷新；详情卡入口：「项目初始化」→ #/init、
     「PRD 拆分」→ #/prd（均携带项目上下文），「看板」保持灰态「待接线」；
  5. 云效数据属外部输入：渲染一律 escapeHtml，禁止裸 innerHTML 拼接。
*/
(function (global) {
  'use strict';

  const api = global.YunxiaoApi;
  const app = global.YunxiaoApp;

  const PER_PAGE = 20;

  let els = null;
  let items = [];
  let total = 0;
  let selectedId = null;
  let loaded = false;

  function escapeHtml(value) {
    return String(value === undefined || value === null ? '' : value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  /** 更新时间展示：ISO 串取日期段，其余原样（全文进 title）。 */
  function formatTime(value) {
    if (!value) return '—';
    const text = String(value);
    const matched = text.match(/^(\d{4}-\d{2}-\d{2})/);
    return matched ? matched[1] : text;
  }

  function idOf(project) {
    return project.id === undefined || project.id === null ? '' : String(project.id);
  }

  function findSelected() {
    for (let i = 0; i < items.length; i++) {
      if (idOf(items[i]) === selectedId) return items[i];
    }
    return null;
  }

  function renderList() {
    if (!items.length) {
      els.list.innerHTML = '<div class="proj-empty">无匹配项目</div>';
      return;
    }
    els.list.innerHTML = items.map(function (project) {
      const id = idOf(project);
      const name = project.name || '（未命名项目）';
      const identifier = project.identifier || '—';
      const time = formatTime(project.updateTime);
      const rawTime = project.updateTime || '';
      const initBadge = project.initialized
        ? '<span class="proj-item-init ok" title="代码库与三端流水线已绑定">已初始化</span>'
        : '<span class="proj-item-init" title="尚未完成项目初始化">待初始化</span>';
      return '<button type="button" class="proj-item' + (id === selectedId ? ' on' : '') + '" data-id="' + escapeHtml(id) + '">'
        + '<span class="proj-item-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>'
        + '<span class="proj-item-meta">'
        + '<span class="proj-item-id" title="' + escapeHtml(identifier) + '">' + escapeHtml(identifier) + '</span>'
        + initBadge
        + '<span class="proj-item-time" title="' + escapeHtml(rawTime) + '">' + escapeHtml(time) + '</span>'
        + '</span>'
        + '</button>';
    }).join('');
  }

  /** 右栏：未选中 → 组织统计卡。 */
  function statsHtml() {
    return '<h2 class="detail-title">组织统计</h2>'
      + '<div class="statrow">'
      + '<div class="stat"><b>' + total + '</b><span>项目总数</span></div>'
      + '</div>'
      + '<div class="kv">'
      + '<span class="kv-k">组织 ID</span>'
      + '<span class="kv-v" title="' + escapeHtml(app.state.organizationId) + '">' + escapeHtml(app.state.organizationId || '—') + '</span>'
      + '</div>'
      + '<p class="detail-help">在左侧选择项目后，这里显示项目详情。</p>';
  }

  /** 右栏：选中 → 项目详情卡（description 3 行截断 + title + 功能入口）。 */
  function detailHtml(project) {
    const name = project.name || '（未命名项目）';
    const identifier = project.identifier || '—';
    const description = project.description || '';
    const rawTime = project.updateTime || '';
    return '<h2 class="detail-title">项目详情</h2>'
      + '<div class="kv">'
      + '<span class="kv-k">项目名称</span><span class="kv-v" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>'
      + '<span class="kv-k">项目标识</span><span class="kv-v" title="' + escapeHtml(identifier) + '">' + escapeHtml(identifier) + '</span>'
      + '<span class="kv-k">项目描述</span><span class="kv-v clamp3" title="' + escapeHtml(description) + '">' + escapeHtml(description || '—') + '</span>'
      + '<span class="kv-k">更新时间</span><span class="kv-v" title="' + escapeHtml(rawTime) + '">' + escapeHtml(rawTime || '—') + '</span>'
      + '</div>'
      + '<div class="detail-entries">'
      + '<button type="button" class="entry" data-entry="init">'
      + '<span class="entry-main"><span class="entry-title">项目初始化</span>'
      + '<span class="entry-sub">' + (project.initialized ? '已完成初始化，可进入查看' : '复制母版库与流水线，生成新项目骨架') + '</span></span>'
      + '<span class="entry-arrow">›</span></button>'
      + '<button type="button" class="entry" data-entry="prd">'
      + '<span class="entry-main"><span class="entry-title">PRD 拆分</span>'
      + '<span class="entry-sub">同步需求并拆成迭代 / 任务 / 用例落库</span></span>'
      + '<span class="entry-arrow">›</span></button>'
      + '<button type="button" class="entry" disabled>'
      + '<span class="entry-main"><span class="entry-title">看板</span>'
      + '<span class="entry-sub">待接线</span></span>'
      + '<span class="entry-arrow">›</span></button>'
      + '</div>';
  }

  function renderRight() {
    const project = selectedId !== null ? findSelected() : null;
    if (!project) {
      selectedId = null;
      els.main.innerHTML = statsHtml();
      return;
    }
    els.main.innerHTML = detailHtml(project);
  }

  function render() {
    renderList();
    renderRight();
  }

  /** 拉取列表：page=1、perPage=20，keyword 透传。 */
  async function reload() {
    const keyword = els.keyword.value.trim();
    els.list.innerHTML = '<div class="proj-empty">加载中…</div>';
    try {
      const data = await api.get('/yunxiao/projects', { page: 1, perPage: PER_PAGE, keyword: keyword });
      items = (data && data.items) || [];
      total = (data && data.total) || 0;
      loaded = true;
      if (selectedId !== null && !findSelected()) selectedId = null;
      render();
    } catch (e) {
      app.showError(e && e.message ? e.message : '加载项目列表失败');
      els.list.innerHTML = '<div class="proj-empty">加载失败，请重试</div>';
    }
  }

  function init() {
    els = {
      keyword: document.getElementById('proj-keyword'),
      search: document.getElementById('btn-search'),
      list: document.getElementById('proj-list'),
      main: document.getElementById('proj-main'),
      reload: document.getElementById('btn-reload')
    };

    els.search.addEventListener('click', reload);
    els.keyword.addEventListener('keydown', function (e) {
      if (e.key === 'Enter') {
        e.preventDefault();
        reload();
      }
    });
    els.reload.addEventListener('click', reload);
    els.main.addEventListener('click', function (e) {
      const target = e.target;
      const entry = target && target.closest ? target.closest('.entry[data-entry]') : null;
      if (!entry) return;
      const project = findSelected();
      if (!project) {
        app.showError('请先在左侧选择项目');
        return;
      }
      // 携带项目上下文进目标页：#/init 登记回写「已初始化」，#/prd 用作同步与落库的项目标识
      const query = ['projectId=' + encodeURIComponent(idOf(project))];
      if (project.identifier) query.push('identifier=' + encodeURIComponent(project.identifier));
      if (project.name) query.push('name=' + encodeURIComponent(project.name));
      const hash = entry.getAttribute('data-entry') === 'prd' ? '#/prd?' : '#/init?';
      global.location.hash = hash + query.join('&');
    });
    els.list.addEventListener('click', function (e) {
      const target = e.target;
      const item = target && target.closest ? target.closest('.proj-item') : null;
      if (!item) return;
      selectedId = item.getAttribute('data-id') || '';
      render();
    });
  }

  /** 首屏进入自动拉一次；之后靠「重新加载」/ 搜索刷新。 */
  function enter() {
    if (!loaded) reload();
  }

  global.YunxiaoProjects = { init: init, enter: enter, reload: reload };
})(window);
