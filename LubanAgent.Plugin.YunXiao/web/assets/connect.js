/*
  云效 DevOps 工作台 · 屏 1 连接设置（S1）。

  约束：
  1. PAT 输入 type=password：不回显、不写任何 data-属性、不落 localStorage / sessionStorage；
  2. 「保存」按钮 D7 门禁：测试连接通过前 disabled，表单任一输入变化即失效重测；
  3. 测试 / 保存时 pat 为空串不传该字段（服务端：省略=保持已存令牌）；
  4. 已存配置回填：organizationId + 地址（https://devops.aliyun.com/orgs/{id}），
     hasPat=true 时 PAT 框留空并提示「已保存令牌，留空表示不修改」。
*/
(function (global) {
  'use strict';

  const api = global.YunxiaoApi;
  const app = global.YunxiaoApp;

  let els = null;
  let hasStoredPat = false;
  let testPassed = false;

  /** 组织访问地址末段解析为组织 ID（去掉 scheme / query / 尾斜杠）。 */
  function parseOrgIdFromAddress(address) {
    let value = (address || '').trim();
    if (!value) return '';
    value = value.split('#')[0].split('?')[0].replace(/\/+$/, '');
    value = value.replace(/^[a-zA-Z][a-zA-Z0-9+.-]*:\/\//, '');
    const index = value.lastIndexOf('/');
    return index === -1 ? '' : value.substring(index + 1);
  }

  function setResult(kind, message) {
    els.result.hidden = false;
    els.result.className = 'result ' + (kind === 'ok' ? 'result-ok' : 'result-err');
    els.result.textContent = message;
  }

  function clearResult() {
    els.result.hidden = true;
    els.result.textContent = '';
    els.result.className = 'result';
  }

  /** D7 门禁：测试未通过时「保存」保持 disabled。 */
  function resetGate() {
    testPassed = false;
    els.save.disabled = true;
  }

  function updatePatHelp() {
    els.patHelp.textContent = hasStoredPat ? '已保存令牌，留空表示不修改' : '';
  }

  function onAddressInput() {
    const orgId = parseOrgIdFromAddress(els.address.value);
    if (orgId) els.orgId.value = orgId;
  }

  /** 测试连接：只读探活，pat 空不传。 */
  async function testConnection() {
    const organizationId = els.orgId.value.trim();
    if (!organizationId) {
      resetGate();
      setResult('err', '请先填写组织 ID');
      return;
    }
    const body = { organizationId: organizationId };
    const pat = els.pat.value;
    if (pat) body.pat = pat;

    els.test.disabled = true;
    try {
      const data = await api.post('/yunxiao/testconnection', body);
      const count = data && typeof data.projectCount === 'number' ? data.projectCount : 0;
      testPassed = true;
      els.save.disabled = false;
      setResult('ok', '校验通过 · 组织内 ' + count + ' 个项目');
    } catch (e) {
      resetGate();
      setResult('err', e && e.message ? e.message : '测试连接失败');
    } finally {
      els.test.disabled = false;
    }
  }

  /** 保存：organizationAddress + organizationId + pat 一起传，pat 空不传。 */
  async function saveSettings() {
    const organizationId = els.orgId.value.trim();
    if (!organizationId) {
      resetGate();
      setResult('err', '请先填写组织 ID');
      return;
    }
    const body = {
      organizationAddress: els.address.value.trim(),
      organizationId: organizationId
    };
    const pat = els.pat.value;
    if (pat) body.pat = pat;

    els.save.disabled = true;
    try {
      await api.post('/yunxiao/savesettings', body);
      els.pat.value = '';
      hasStoredPat = true;
      updatePatHelp();
      resetGate();
      app.setHasPat(true);
      app.showToast('连接设置已保存', 'ok');
      app.navigate('#/projects');
    } catch (e) {
      app.showError(e && e.message ? e.message : '保存连接设置失败');
      els.save.disabled = !testPassed;
    }
  }

  /** 入口：应用壳在读完 GET settings 后调用（settings = { organizationId, hasPat }）。 */
  function init(settings) {
    els = {
      address: document.getElementById('org-address'),
      orgId: document.getElementById('org-id'),
      pat: document.getElementById('org-pat'),
      patHelp: document.getElementById('pat-help'),
      result: document.getElementById('connect-result'),
      test: document.getElementById('btn-test'),
      save: document.getElementById('btn-save')
    };

    hasStoredPat = !!(settings && settings.hasPat);
    const organizationId = (settings && settings.organizationId) || '';
    if (organizationId) {
      els.orgId.value = organizationId;
      els.address.value = 'https://devops.aliyun.com/orgs/' + organizationId;
    }
    updatePatHelp();

    els.address.addEventListener('input', function () {
      onAddressInput();
      resetGate();
      clearResult();
    });
    els.orgId.addEventListener('input', function () {
      resetGate();
      clearResult();
    });
    els.pat.addEventListener('input', function () {
      resetGate();
      clearResult();
    });
    els.test.addEventListener('click', testConnection);
    els.save.addEventListener('click', saveSettings);
  }

  global.YunxiaoConnect = { init: init };
})(window);
