/*
  云效 DevOps 工作台 · 应用壳（S1 + S2 屏 3）：
  1. hash 路由：#/connect（屏 1 连接设置）/ #/projects（屏 2 项目列表）/ #/init（屏 3 项目初始化）
     / #/prd（屏 4 PRD 拆分）；
  2. 门禁：GET /yunxiao/settings 后 hasPat=false 强制 #/connect（手动进 #/projects / #/init / #/prd 弹回），
     hasPat=true 默认 #/projects；
  3. 四屏容器 show/hide 切换（hidden 属性）；
  4. 全局提示条：显示 message，自动消失。

  经典脚本按序加载：api.js → app.js → connect.js → projects.js → init.js → prd.js。
*/
(function (global) {
  'use strict';

  const api = global.YunxiaoApi;

  /** 门禁与共享状态（PAT 明文绝不进入本状态）。 */
  const state = { organizationId: '', hasPat: false };

  let current = null;
  let lastHash = '';
  let toastTimer = 0;

  function $(id) {
    return document.getElementById(id);
  }

  /** 全局提示条：kind = 'ok' | 'err'，3.5 秒后自动消失。 */
  function showToast(message, kind) {
    const el = $('toast');
    if (!el) return;
    const variant = kind === 'ok' ? 'toast-ok' : 'toast-err';
    el.textContent = message;
    el.className = 'toast ' + variant + ' show';
    if (toastTimer) global.clearTimeout(toastTimer);
    toastTimer = global.setTimeout(function () {
      el.className = 'toast ' + variant;
    }, 3500);
  }

  function showError(message) {
    showToast(message || '请求失败', 'err');
  }

  function targetFromHash() {
    const hash = global.location.hash || '';
    const path = hash.split('?')[0];
    if (path === '#/prd') return 'prd';
    if (path === '#/init') return 'init';
    if (path === '#/projects') return 'projects';
    if (path === '#/connect') return 'connect';
    return state.hasPat ? 'projects' : 'connect';
  }

  /** 四屏 show/hide + 面包屑 / 动作栏切换。 */
  function show(target) {
    // 同屏但 hash 变化（切换项目上下文的 #/init、#/prd）也要重进，保证上下文刷新
    const hash = global.location.hash || '';
    if (current === target && lastHash === hash) return;
    current = target;
    lastHash = hash;

    $('crumb-current').textContent = target === 'projects'
      ? '项目列表'
      : target === 'init'
        ? '项目初始化'
        : target === 'prd' ? 'PRD 拆分' : '连接设置';
    $('screen-connect').hidden = target !== 'connect';
    $('screen-projects').hidden = target !== 'projects';
    $('screen-init').hidden = target !== 'init';
    $('screen-prd').hidden = target !== 'prd';
    $('actions-connect').hidden = target !== 'connect';
    $('actions-projects').hidden = target !== 'projects';
    $('actions-init').hidden = target !== 'init';
    $('actions-prd').hidden = target !== 'prd';
    $('action-hint').textContent = target === 'projects'
      ? '选中项目查看详情，「重新加载」刷新列表。'
      : target === 'init'
        ? '先预检再执行；流水线复制在右列，代码库任务完成后解锁。'
        : target === 'prd'
          ? '同步云效需求或补充本地 PRD，生成预览确认后再提交落库。'
          : '测试连接通过后才能保存；令牌只写入宿主凭据存储。';

    if (target === 'projects') global.YunxiaoProjects.enter();
    if (target === 'init') global.YunxiaoInit.enter();
    if (target === 'prd') global.YunxiaoPrd.enter();
  }

  /** 路由解析（含门禁弹回）。 */
  function applyRoute() {
    let target = targetFromHash();
    const hash = global.location.hash || '';
    const path = hash.split('?')[0];
    if ((target === 'projects' || target === 'init' || target === 'prd') && !state.hasPat) {
      target = 'connect';
      if (path === '#/projects' || path === '#/init' || path === '#/prd') {
        showToast('请先完成连接设置', 'err');
      }
    }
    // #/init 与 #/prd 允许携带项目上下文（?projectId=&identifier=&name=），命中时保留原 hash
    const desired = target === 'projects'
      ? '#/projects'
      : target === 'init'
        ? (path === '#/init' ? hash : '#/init')
        : target === 'prd'
          ? (path === '#/prd' ? hash : '#/prd')
          : '#/connect';
    if (global.location.hash !== desired) {
      global.location.hash = desired;
    }
    show(target);
  }

  function navigate(hash) {
    global.location.hash = hash;
  }

  function setHasPat(value) {
    state.hasPat = !!value;
  }

  async function bootstrap() {
    global.addEventListener('hashchange', applyRoute);

    let settings = null;
    try {
      settings = await api.get('/yunxiao/settings');
    } catch (e) {
      showError(e && e.message ? e.message : '读取连接设置失败');
    }
    state.organizationId = (settings && settings.organizationId) || '';
    state.hasPat = !!(settings && settings.hasPat);

    global.YunxiaoConnect.init(state);
    global.YunxiaoProjects.init();
    global.YunxiaoInit.init();
    global.YunxiaoPrd.init();

    applyRoute();
  }

  global.YunxiaoApp = {
    state: state,
    showToast: showToast,
    showError: showError,
    navigate: navigate,
    setHasPat: setHasPat
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', bootstrap);
  } else {
    bootstrap();
  }
})(window);
