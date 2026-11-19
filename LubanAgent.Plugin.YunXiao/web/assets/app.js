/*
  云效 DevOps 工作台 · 应用壳（S1 + S2 屏 3）：
  1. hash 路由：#/connect（屏 1 连接设置）/ #/projects（屏 2 项目列表）/ #/init（屏 3 项目初始化）；
  2. 门禁：GET /yunxiao/settings 后 hasPat=false 强制 #/connect（手动进 #/projects / #/init 弹回），
     hasPat=true 默认 #/projects；
  3. 三屏容器 show/hide 切换（hidden 属性）；
  4. 全局提示条：显示 message，自动消失。

  经典脚本按序加载：api.js → app.js → connect.js → projects.js → init.js。
*/
(function (global) {
  'use strict';

  const api = global.YunxiaoApi;

  /** 门禁与共享状态（PAT 明文绝不进入本状态）。 */
  const state = { organizationId: '', hasPat: false };

  let current = null;
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
    const hash = global.location.hash;
    if (hash === '#/init') return 'init';
    if (hash === '#/projects') return 'projects';
    if (hash === '#/connect') return 'connect';
    return state.hasPat ? 'projects' : 'connect';
  }

  /** 三屏 show/hide + 面包屑 / 动作栏切换。 */
  function show(target) {
    if (current === target) return;
    current = target;

    $('crumb-current').textContent = target === 'projects'
      ? '项目列表'
      : target === 'init' ? '项目初始化' : '连接设置';
    $('screen-connect').hidden = target !== 'connect';
    $('screen-projects').hidden = target !== 'projects';
    $('screen-init').hidden = target !== 'init';
    $('actions-connect').hidden = target !== 'connect';
    $('actions-projects').hidden = target !== 'projects';
    $('actions-init').hidden = target !== 'init';
    $('action-hint').textContent = target === 'projects'
      ? '选中项目查看详情，「重新加载」刷新列表。'
      : target === 'init'
        ? '先预检再执行；流水线复制在右列，代码库任务完成后解锁。'
        : '测试连接通过后才能保存；令牌只写入宿主凭据存储。';

    if (target === 'projects') global.YunxiaoProjects.enter();
    if (target === 'init') global.YunxiaoInit.enter();
  }

  /** 路由解析（含门禁弹回）。 */
  function applyRoute() {
    let target = targetFromHash();
    if ((target === 'projects' || target === 'init') && !state.hasPat) {
      target = 'connect';
      if (global.location.hash === '#/projects' || global.location.hash === '#/init') {
        showToast('请先完成连接设置', 'err');
      }
    }
    const desired = target === 'projects'
      ? '#/projects'
      : target === 'init' ? '#/init' : '#/connect';
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
