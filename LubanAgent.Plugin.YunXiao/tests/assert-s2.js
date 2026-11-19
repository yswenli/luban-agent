#!/usr/bin/env node
/*
  云效 Web 插件 S2 屏 3 前端断言驱动（任务 12，批次 F）。
  设计稿验证惯例：无头 Edge 注入探针 + document.title 回传 'PROBE::' + JSON。

  用法：
    node LubanAgent.Plugin.YunXiao/tests/assert-s2.js
    （可选环境变量 EDGE_PATH 覆盖 msedge.exe 路径；KEEP_TMP=1 保留临时目录）

  流程（照 tests/assert-s1.js）：
    1. 复制 web/index.html 到临时目录，改写 assets/ 相对引用为绝对 file:// URL，
       在 </body> 前注入探针 <script>（探针同步装 fetch mock，DOM ready 后跑断言，
       最后 document.title = 'PROBE::' + JSON.stringify({results:[...]})）；
    2. 优先走 CDP（--remote-debugging-port + 原生 WebSocket）读 title 通道，
       并用 Emulation.setDeviceMetricsOverride 把视口精确钉在 826×694
      （--headless=new 下 --window-size 不可靠，别改回）；
       CDP 不可用时回退 spec 命令 --headless=new --dump-dom；
    3. 逐条打印 PASS/FAIL；任一失败 → 退出码 1。

  fetch mock（camelCase，对齐 YunxiaoContracts / init.js 口径）：
    GET  /yunxiao/settings        → { organizationId:'DEMOORG', hasPat:true }
    GET  /yunxiao/projects       → 两条演示项目（屏 2 入口跳板）
    GET  /yunxiao/manifests      → 单模板（超长库名验省略，description 含 HTML 字符验 escapeHtml）
                                   + groups/workspaces/sessionReady
    POST /yunxiao/preflight      → plan 三段 + gitCommands（脱敏 <REPO_URL>）+ conflicts + sessionReady
                                   + reusedLibs/warnings（MOCK.reuseAll=true 时：全复用零 git 步骤）
    POST /yunxiao/initexec       → 'task-1'
    GET  /yunxiao/inittask       → 重跑前 {status:1, libs:[{status:5=failed}]}（点出重跑）；
                                   initretry 之后 → {status:2=done, libs:[{status:4=done}]}（终态即停轮询）
    POST /yunxiao/initretry      → 'task-1'
    POST /yunxiao/pipelinepreview → mobile/admin 两段互异 diff + content + exists（mobile 缺失/admin 已存在）
    POST /yunxiao/pipelinecommit → 逐端 {end,ok,pipelineId|message}（admin 补全语义「已存在跳过」）
  状态数字映射以 init.js 为准（normTaskStatus: 0=pending/1=running/2=done/3=failed；
  normLibStatus: 0=pending/1=cloning/2=renaming/3=pushing/4=done/5=failed），与服务端
  InitTaskStatus / LibStatus 枚举同序。

  断言清单：
    1   画板 .frame 826×694（±1）（CDP 钉视口后断言）
    2a  屏 3 初始态 #content 无纵向溢出/无横向滚动
    2b  屏 3 终态（预检+任务+diff+提交全渲染）#content 无纵向溢出/无横向滚动
    3   入口点亮：#/projects 项目详情「项目初始化」→ hash #/init 且屏 3 可见
    4   模板=1 锁定态：.msel.locked（无多选 toggle）、无「已选 n」计数
    5   代号 → 三段派生重算（代号前缀替换）；改写行再改代号不被覆盖（dirty）
    6   预检面板：命令序列默认展开可见、文本不含 pt-
    7   执行门禁：预检后 enabled；sessionReady=false（重加载+重预检）→ 禁用并明示
    8   F6 依赖门禁：任务未 done 置灰 → 终态解锁 + inittask 轮询停止（调用数冻结）
    9   diff 切换：mobile/admin 两段 diff 文本互换
    10  禁用资源：script/link 无 .mjs/.json/.map/.wasm/.woff 后缀
    11  长文本省略：超长模板名 → .msel-h .v computedStyle ellipsis+nowrap
    12  失败库「重跑」链路：POST initretry 后推进终态
    13  提交链路：create 成功 + 未勾覆盖的 update 行卡内确认（update+confirm:true）
    14  模板描述渲染（S2b）：name+description，escapeHtml 不注入标签（含 HTML 字符夹具）
    15  复用徽标（S2b）：ReusedLibs 命中 → 「复用」+「将复用已有库」
    16  全复用无 git 序列提示（S2b）：「无需 git 操作（全部复用）」
    17  全复用会话门禁放宽（S2b）：sessionReady=false 不阻断执行
    18  F6 Exists 标注（S2b）：「已存在（提交时跳过）/缺失（将新建）」
*/
'use strict';

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const http = require('node:http');
const { spawn, spawnSync } = require('node:child_process');
const { pathToFileURL } = require('node:url');

const EDGE = process.env.EDGE_PATH ||
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const VIEWPORT = { width: 826, height: 694 };

/* ---------------- 探针（注入页内执行） ---------------- */
/* 注意：探针体内不用模板串/反引号，仅字符串拼接，方便嵌入。 */
const PROBE_JS = String.raw`
(function () {
  'use strict';
  var results = [];
  function record(id, name, pass, detail) {
    results.push({ id: id, name: name, pass: !!pass, detail: detail === undefined ? '' : String(detail) });
  }
  function finish() {
    results.sort(function (a, b) {
      var na = parseInt(a.id, 10); var nb = parseInt(b.id, 10);
      if (isNaN(na) || isNaN(nb)) return a.id < b.id ? -1 : 1;
      if (na !== nb) return na - nb;
      return a.id < b.id ? -1 : 1;
    });
    try {
      document.title = 'PROBE::' + JSON.stringify({ results: results });
    } catch (e) {
      document.title = 'PROBE::' + JSON.stringify({
        results: [{ id: 'probe', name: 'probe-internal', pass: false, detail: String(e) }]
      });
    }
    window.__PROBE_DONE__ = true;
  }
  function poll(check, timeoutMs, intervalMs) {
    var start = Date.now();
    return new Promise(function (resolve) {
      (function tick() {
        var v = false;
        try { v = !!check(); } catch (e) { v = false; }
        if (v) return resolve(true);
        if (Date.now() - start >= timeoutMs) return resolve(false);
        setTimeout(tick, intervalMs || 50);
      })();
    });
  }
  function sleep(ms) {
    return new Promise(function (r) { setTimeout(r, ms); });
  }
  function setInput(input, value) {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }
  function clickEl(node) {
    node.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window }));
  }
  function overflowDetail(content) {
    return 'scrollH=' + content.scrollHeight + ' clientH=' + content.clientHeight +
      ' scrollW=' + content.scrollWidth + ' clientW=' + content.clientWidth;
  }

  /* ---- fetch mock：在 bootstrap（DOMContentLoaded）之前同步安装 ---- */
  var LONG_TPL = 'TPLX-' + 'VeryLongTemplateCode'.repeat(4) + '-Mixed';
  window.__PROBE_LONG_TPL__ = LONG_TPL;
  /* 含 HTML 字符的模板描述（escapeHtml 夹具：不得被当标签注入） */
  var LONG_DESC = '合成演示模板库<b>bold</b>';
  window.__PROBE_LONG_DESC__ = LONG_DESC;

  var MOCK = {
    sessionReady: true,
    retryArmed: false,
    reuseAll: false,
    calls: { settings: 0, projects: 0, manifests: 0, preflight: 0, initexec: 0, inittask: 0, initretry: 0, pipelinepreview: 0, pipelinecommit: 0 },
    posts: []
  };
  window.__MOCK__ = MOCK;

  var PREFLIGHT_DATA = {
    plan: {
      repoName: 'NEWX-Mixed',
      dirs: [
        { from: 'TPLX-Admin', to: 'NEWX-Admin' },
        { from: 'TPLX-Mobile', to: 'NEWX-Mobile' },
        { from: 'TPLX-Service', to: 'NEWX-Service' }
      ],
      pipelines: [
        { from: 'ACME_DEV_Deploy_TPLX-Mobile', to: 'ACME_DEV_Deploy_NEWX-Mobile' }
      ]
    },
    gitCommands: [
      { label: '克隆母版', command: 'git clone --branch master <REPO_URL> /w/demo/NEWX-Mixed' },
      { label: '改名', command: 'git -C "/w/demo/NEWX-Mixed" mv TPLX-Admin NEWX-Admin' }
    ],
    conflicts: [],
    sessionReady: true,
    reusedLibs: [],
    warnings: []
  };

  var TASK_RUNNING = {
    id: 'task-1', codename: 'NEWX', status: 1,
    libs: [{ repo: 'NEWX-Mixed', repoId: '999', status: 5, detail: '模拟克隆失败（探针）' }],
    finishedAt: null
  };
  var TASK_DONE = {
    id: 'task-1', codename: 'NEWX', status: 2,
    libs: [{ repo: 'NEWX-Mixed', repoId: '999', status: 4, detail: '' }],
    finishedAt: '2026-11-18T00:00:00Z'
  };

  function envelope(data) {
    return { success: true, code: 'OK', message: '', data: data };
  }

  window.fetch = function (url, opts) {
    var u = String(url);
    var body = null;
    try {
      var raw = opts && opts.body ? String(opts.body) : '';
      body = JSON.parse(JSON.parse(raw).payload);
    } catch (e) {
      body = null;
    }
    var data = {};
    if (u.indexOf('/yunxiao/settings') !== -1) {
      MOCK.calls.settings++;
      data = { organizationId: 'DEMOORG', hasPat: true };
    } else if (u.indexOf('/yunxiao/projects') !== -1) {
      MOCK.calls.projects++;
      data = {
        total: 2,
        items: [
          { id: 'p1', name: '演示项目甲', identifier: 'demo-app', updateTime: '2026-11-01' },
          { id: 'p2', name: '演示项目乙', identifier: 'demo-web', updateTime: '2026-10-15' }
        ]
      };
    } else if (u.indexOf('/yunxiao/manifests') !== -1) {
      MOCK.calls.manifests++;
      data = {
        repos: [{ name: LONG_TPL, description: LONG_DESC, templateProjectId: 321 }],
        groups: [{ id: '11', name: 'DEMOGRP' }],
        workspaces: [{ id: 'w1', rootPath: '/w/demo' }],
        sessionReady: MOCK.sessionReady
      };
    } else if (u.indexOf('/yunxiao/preflight') !== -1) {
      MOCK.calls.preflight++;
      MOCK.posts.push({ path: '/yunxiao/preflight', body: body });
      var pre = JSON.parse(JSON.stringify(PREFLIGHT_DATA));
      pre.sessionReady = MOCK.sessionReady;
      if (MOCK.reuseAll) {
        /* 全复用：目标库命中 ReusedLibs → 零 git 步骤 + 结构待校验提示 */
        pre.reusedLibs = [pre.plan.repoName];
        pre.gitCommands = [];
        pre.warnings = ['结构待校验：本地未克隆 ' + pre.plan.repoName];
      }
      data = pre;
    } else if (u.indexOf('/yunxiao/initexec') !== -1) {
      MOCK.calls.initexec++;
      MOCK.posts.push({ path: '/yunxiao/initexec', body: body });
      data = 'task-1';
    } else if (u.indexOf('/yunxiao/inittask') !== -1) {
      MOCK.calls.inittask++;
      data = MOCK.retryArmed ? TASK_DONE : TASK_RUNNING;
    } else if (u.indexOf('/yunxiao/initretry') !== -1) {
      MOCK.calls.initretry++;
      MOCK.posts.push({ path: '/yunxiao/initretry', body: body });
      MOCK.retryArmed = true;
      data = 'task-1';
    } else if (u.indexOf('/yunxiao/pipelinepreview') !== -1) {
      MOCK.calls.pipelinepreview++;
      MOCK.posts.push({ path: '/yunxiao/pipelinepreview', body: body });
      /* diff 的 + 行须能重建顶层 stages:（init.js contentOf 重建口径）；
         exists：mobile 缺失（将新建）/ admin 已存在（提交时跳过） */
      data = {
        items: [
          { end: 'mobile', name: 'ACME_DEV_Deploy_NEWX-Mobile', diff: '-x\n+stages:\n+  - mobile', content: 'stages:\n  - mobile', warning: null, exists: false },
          { end: 'admin', name: 'ACME_DEV_Deploy_NEWX-Admin', diff: '-y\n+stages:\n+  - admin', content: 'stages:\n  - admin', exists: true }
        ]
      };
    } else if (u.indexOf('/yunxiao/pipelinecommit') !== -1) {
      MOCK.calls.pipelinecommit++;
      MOCK.posts.push({ path: '/yunxiao/pipelinecommit', body: body });
      /* admin 走补全语义：服务端「已存在跳过」回执 */
      data = {
        results: [
          { end: 'mobile', ok: true, pipelineId: '701' },
          { end: 'admin', ok: true, message: '已存在跳过' }
        ]
      };
    } else {
      data = {};
    }
    return Promise.resolve({
      ok: true,
      status: 200,
      text: function () { return Promise.resolve(JSON.stringify(envelope(data))); }
    });
  };

  async function run() {
    /* 就绪：bootstrap 完成（hasPat=true → 默认 #/projects）且项目列表已渲染 */
    var ready = await poll(function () {
      return window.YunxiaoInit && window.YunxiaoApp
        && window.location.hash === '#/projects'
        && !document.getElementById('screen-projects').hidden
        && !!document.querySelector('#proj-list .proj-item');
    }, 8000, 50);
    if (!ready) record('0', '探针就绪（bootstrap + 项目列表）', false, 'hash=' + window.location.hash);

    /* 1. 画板 826×694 */
    var frame = document.querySelector('.frame');
    var rect = frame ? frame.getBoundingClientRect() : null;
    record('1', '画板 .frame 826×694（±1）',
      !!rect && Math.abs(rect.width - 826) <= 1 && Math.abs(rect.height - 694) <= 1,
      rect
        ? ('w=' + rect.width.toFixed(1) + ' h=' + rect.height.toFixed(1) + ' inner=' + window.innerWidth + 'x' + window.innerHeight)
        : 'no .frame');

    /* 3. 入口点亮：项目详情「项目初始化」→ #/init */
    var item = document.querySelector('#proj-list .proj-item');
    if (item) clickEl(item);
    var entryReady = await poll(function () {
      var e = document.querySelector('#proj-main .entry[data-entry="init"]');
      return e && !e.disabled;
    }, 3000, 50);
    var entry = document.querySelector('#proj-main .entry[data-entry="init"]');
    if (entry) clickEl(entry);
    var entered = await poll(function () {
      return window.location.hash === '#/init' && !document.getElementById('screen-init').hidden;
    }, 3000, 50);
    record('3', '入口点亮：项目详情「项目初始化」→ #/init 屏 3 可见',
      entryReady && entered && document.getElementById('screen-projects').hidden,
      'hash=' + window.location.hash + ' entryReady=' + entryReady + ' entered=' + entered);

    /* manifests 就绪（单母版锁定态渲染） */
    var manifestsReady = await poll(function () {
      return !!document.querySelector('#tpl-sel .msel.locked');
    }, 8000, 50);

    var content = document.getElementById('content');

    /* 2a. 屏 3 初始态无溢出 */
    record('2a', '屏3 init 初始态：content 无纵向溢出/无横向滚动',
      content.scrollHeight <= content.clientHeight + 1 &&
      content.scrollWidth <= content.clientWidth + 1,
      overflowDetail(content));

    /* 4. 母版=1 锁定态 */
    var chip = document.getElementById('tpl-count');
    var locked = document.querySelector('#tpl-sel .msel.locked');
    var toggle = document.querySelector('#tpl-sel [data-act="toggle"]');
    var selText = (document.getElementById('tpl-sel').textContent || '') + '|' + (chip.textContent || '');
    record('4', '母版=1 锁定态（锁定样式 + 无「已选 n」计数）',
      manifestsReady && !!locked && !toggle && selText.indexOf('已选') === -1
        && (chip.textContent || '').indexOf('锁定') !== -1,
      'chip=' + chip.textContent + ' locked=' + !!locked + ' toggle=' + !!toggle);

    /* 11. 长文本省略：锁定 .v 承载超长母版库名 */
    var vEl = document.querySelector('#tpl-sel .msel-h .v');
    var cs = vEl ? window.getComputedStyle(vEl) : null;
    var trunc = vEl ? (vEl.scrollWidth > vEl.clientWidth) : false;
    record('11', '长文本省略（超长模板名 ellipsis+nowrap）',
      !!cs && cs.textOverflow === 'ellipsis' && cs.whiteSpace === 'nowrap' && trunc
        && vEl.getAttribute('title') === window.__PROBE_LONG_TPL__,
      cs
        ? ('textOverflow=' + cs.textOverflow + ' whiteSpace=' + cs.whiteSpace +
           ' truncated=' + trunc + ' titleFull=' + (vEl.getAttribute('title') === window.__PROBE_LONG_TPL__))
        : 'no .msel-h .v');

    /* 14. 模板描述渲染（S2b）：name+description，escapeHtml 不注入标签 */
    var hintEl = document.querySelector('#tpl-sel .msel-h .mhint');
    var rawTagInjected = !!document.querySelector('#tpl-sel .msel-h b');
    record('14', '模板描述渲染（name+description，escapeHtml 不注入标签）',
      !!hintEl && hintEl.textContent === window.__PROBE_LONG_DESC__
        && hintEl.getAttribute('title') === window.__PROBE_LONG_DESC__
        && !rawTagInjected
        && (document.getElementById('tpl-sel').innerHTML.indexOf('&lt;b&gt;') !== -1),
      'hint=' + (hintEl ? hintEl.textContent : 'none') + ' rawTag=' + rawTagInjected +
      ' escaped=' + (document.getElementById('tpl-sel').innerHTML.indexOf('&lt;b&gt;') !== -1));

    /* 5. 代号 → 三段派生 + dirty 语义（S2b：库名=代号前缀替换，源名=所选模板名整体） */
    var code = document.getElementById('init-code');
    function deriveVal(kind, key) {
      var sel = key
        ? '#derive-list input.dr-to[data-kind="' + kind + '"][data-key="' + key + '"]'
        : '#derive-list input.dr-to[data-kind="' + kind + '"]';
      var n = document.querySelector(sel);
      return n ? n.value : '';
    }
    /* 派生期望：LONG_TPL 代号段 TPLX → NEWX，后缀整体保留（NEWX- + VeryLong…-Mixed） */
    var LONG_SUFFIX = window.__PROBE_LONG_TPL__.slice(5);
    setInput(code, 'NEWX');
    var derived = await poll(function () {
      return deriveVal('repo') === 'NEWX-' + LONG_SUFFIX;
    }, 2000, 30);
    var initialOk = derived
      && deriveVal('dir', 'admin') === 'NEWX-Admin'
      && deriveVal('dir', 'mobile') === 'NEWX-Mobile'
      && deriveVal('dir', 'backend') === 'NEWX-Service';
    /* 改写 mobile 目录行 → 再改代号：改写值不被覆盖（dirty），非 dirty 行重算 */
    var mobileDir = document.querySelector('#derive-list input.dr-to[data-kind="dir"][data-key="mobile"]');
    if (mobileDir) setInput(mobileDir, 'CUSTOMX-Mobile');
    setInput(code, 'NEWX2');
    var dirtyKept = deriveVal('dir', 'mobile') === 'CUSTOMX-Mobile';
    var recomputed = deriveVal('repo') === 'NEWX2-' + LONG_SUFFIX && deriveVal('dir', 'admin') === 'NEWX2-Admin';
    setInput(code, 'NEWX');
    var restored = deriveVal('repo') === 'NEWX-' + LONG_SUFFIX && deriveVal('dir', 'mobile') === 'CUSTOMX-Mobile';
    record('5', '代号→三段派生重算（前缀替换） + 改写值不被覆盖（dirty）',
      initialOk && dirtyKept && recomputed && restored,
      'initial=' + initialOk + ' dirtyKept=' + dirtyKept + ' recomputed=' + recomputed + ' restored=' + restored);

    /* 8a（先采初始态）：F6 依赖门禁 —— 任务未 done 置灰 */
    var pipeLock = document.getElementById('pipe-lock');
    var lockInitial = !pipeLock.hidden
      && document.getElementById('btn-preview').disabled === true
      && document.getElementById('btn-commit').disabled === true
      && document.getElementById('pipe-ends').classList.contains('pipe-off');

    /* 6. 预检面板：命令序列默认展开、不含 pt- */
    clickEl(document.getElementById('btn-preflight'));
    var preOk = await poll(function () {
      var p = document.getElementById('preflight-panel');
      return p && !p.hidden && (document.getElementById('git-cmds').textContent || '').length > 0;
    }, 4000, 50);
    var cmds = document.getElementById('git-cmds');
    var cmdText = cmds.textContent || '';
    var cmdsVisible = cmds.offsetParent !== null && cmds.offsetHeight > 0;
    record('6', '预检面板：命令序列默认展开可见、不含 pt-',
      preOk && cmdsVisible && cmdText.indexOf('git clone') !== -1 && cmdText.indexOf('pt-') === -1,
      'visible=' + cmdsVisible + ' hasClone=' + (cmdText.indexOf('git clone') !== -1) +
      ' hasPt=' + (cmdText.indexOf('pt-') !== -1) + ' sample=' + cmdText.slice(0, 46));

    /* 7. 执行门禁：预检后 enabled；sessionReady=false（重加载+重预检）→ 禁用并明示 */
    var btnExec = document.getElementById('btn-exec');
    var gate7a = btnExec.disabled === false;
    window.__MOCK__.sessionReady = false;
    clickEl(document.getElementById('btn-reload-manifests'));
    await poll(function () { return window.__MOCK__.calls.manifests >= 2; }, 3000, 50);
    clickEl(document.getElementById('btn-preflight'));
    var gate7b = await poll(function () {
      return document.getElementById('btn-exec').disabled === true
        && (document.getElementById('exec-gate').textContent || '').indexOf('会话') !== -1;
    }, 4000, 50);
    record('7', '执行门禁：sessionReady=false 时禁用并明示原因',
      gate7a && gate7b,
      'enabledAfterPreflight=' + gate7a + ' disabledWhenNoSession=' + gate7b +
      ' gate=' + (document.getElementById('exec-gate').textContent || ''));

    /* 恢复会话可用并重新预检（供后续执行） */
    window.__MOCK__.sessionReady = true;
    clickEl(document.getElementById('btn-reload-manifests'));
    await poll(function () { return window.__MOCK__.calls.manifests >= 3; }, 3000, 50);
    clickEl(document.getElementById('btn-preflight'));
    var gate7c = await poll(function () {
      return document.getElementById('btn-exec').disabled === false;
    }, 4000, 50);

    /* 12. 执行 → running 快照点出失败库「重跑」→ initretry → 终态 */
    clickEl(document.getElementById('btn-exec'));
    var execPosted = await poll(function () {
      return window.__MOCK__.calls.initexec >= 1;
    }, 3000, 50);
    var retryReady = await poll(function () {
      return !!document.querySelector('#task-libs [data-act="retry"]');
    }, 5000, 50);
    var retryBtn = document.querySelector('#task-libs [data-act="retry"]');
    if (retryBtn) clickEl(retryBtn);
    var retryPosted = await poll(function () {
      return window.__MOCK__.calls.initretry >= 1;
    }, 3000, 50);
    var retryBody = null;
    window.__MOCK__.posts.forEach(function (p) {
      if (p.path === '/yunxiao/initretry') retryBody = p.body;
    });
    record('12', '失败库「重跑」链路（POST initretry → 推进终态）',
      execPosted && retryReady && retryPosted
        && !!retryBody && retryBody.taskId === 'task-1'
        && String(retryBody.repoNames) === 'NEWX-Mixed',
      'exec=' + execPosted + ' retryBtn=' + retryReady + ' retryPosted=' + retryPosted +
      ' body=' + JSON.stringify(retryBody));

    /* 8b. 终态：done 解锁 + 轮询停止 */
    var doneOk = await poll(function () {
      var st = document.getElementById('task-status');
      return st && st.textContent === '已完成';
    }, 5000, 50);
    var libDone = (document.querySelector('#task-libs .taskrow .st') || {}).textContent === '已完成';
    var lockFinal = document.getElementById('pipe-lock').hidden === true
      && document.getElementById('btn-preview').disabled === false
      && document.getElementById('btn-commit').disabled === false
      && !document.getElementById('pipe-ends').classList.contains('pipe-off');
    var cnt1 = window.__MOCK__.calls.inittask;
    await sleep(2600);
    var cnt2 = window.__MOCK__.calls.inittask;
    record('8', 'F6 依赖门禁：未 done 置灰 → done 解锁 + inittask 轮询停止',
      lockInitial && doneOk && libDone && lockFinal && cnt2 === cnt1,
      'initialLocked=' + lockInitial + ' taskDone=' + doneOk + ' libDone=' + libDone +
      ' unlocked=' + lockFinal + ' inittaskCalls ' + cnt1 + '=>' + cnt2);

    /* 9. diff 切换：mobile/admin 文本互换 */
    var diffReady = await poll(function () {
      var p = document.getElementById('diff-panel');
      return p && !p.hidden && (document.getElementById('diff-pre').textContent || '').length > 0;
    }, 5000, 50);
    var pre9 = document.getElementById('diff-pre');
    var tabs = document.querySelectorAll('#diff-tabs .tab');
    var DIFF_MOBILE = '-x\n+stages:\n+  - mobile';
    var DIFF_ADMIN = '-y\n+stages:\n+  - admin';
    var mobileFirst = pre9.textContent === DIFF_MOBILE;
    var tabAdmin = document.querySelector('#diff-tabs .tab[data-end="admin"]');
    if (tabAdmin) clickEl(tabAdmin);
    var adminText = pre9.textContent === DIFF_ADMIN;
    var tabMobile = document.querySelector('#diff-tabs .tab[data-end="mobile"]');
    if (tabMobile) clickEl(tabMobile);
    var backText = pre9.textContent === DIFF_MOBILE;
    record('9', 'diff 端切换（mobile/admin 两段 diff 互换）',
      diffReady && tabs.length === 2 && mobileFirst && adminText && backText,
      'tabs=' + tabs.length + ' mobile=' + mobileFirst + ' admin=' + adminText + ' back=' + backText);

    /* 18. F6 Exists 标注（S2b）：已存在（提交时跳过）/ 缺失（将新建） */
    var existsMobile = document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="exists"]');
    var existsAdmin = document.querySelector('#pipe-ends .endrow[data-end="admin"] [data-f="exists"]');
    record('18', 'F6 Exists 标注（已存在（提交时跳过）/缺失（将新建））',
      !!existsMobile && existsMobile.textContent === '缺失（将新建）'
        && !!existsAdmin && existsAdmin.textContent === '已存在（提交时跳过）',
      'mobile=' + (existsMobile ? existsMobile.textContent : 'none') +
      ' admin=' + (existsAdmin ? existsAdmin.textContent : 'none'));

    /* 13. 提交链路：create 成功 + 覆盖卡内确认（update + confirm:true） */
    clickEl(document.getElementById('btn-commit'));
    var commitOk = await poll(function () {
      var row = document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="result"]');
      return row && !row.hidden && (row.textContent || '').indexOf('成功') !== -1;
    }, 5000, 50);
    var commitPosts = window.__MOCK__.posts.filter(function (p) { return p.path === '/yunxiao/pipelinecommit'; });
    var createOk = commitPosts.length === 1 && commitPosts[0].body && commitPosts[0].body.items
      && commitPosts[0].body.items.length === 2
      && commitPosts[0].body.items[0].mode === 'create'
      && commitPosts[0].body.items[0].confirm === undefined;
    clickEl(document.getElementById('btn-commit'));
    var confirmShown = await poll(function () {
      var c = document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="confirm"]');
      return c && !c.hidden;
    }, 3000, 50);
    var confirmYes = document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="confirm-yes"]');
    if (confirmYes) clickEl(confirmYes);
    var updateOk = await poll(function () {
      var list = window.__MOCK__.posts.filter(function (p) { return p.path === '/yunxiao/pipelinecommit'; });
      if (list.length < 2) return false;
      var items = (list[list.length - 1].body && list[list.length - 1].body.items) || [];
      var upd = items.filter(function (i) { return i.end === 'mobile' && i.mode === 'update'; });
      return !!(upd.length === 1
        && upd[0].confirm === true && String(upd[0].pipelineId) === '701');
    }, 5000, 50);
    record('13', '提交链路：create 成功 + 覆盖卡内确认（update+confirm:true）',
      commitOk && createOk && confirmShown && updateOk,
      'create=' + commitOk + ' createBody=' + createOk + ' confirmBox=' + confirmShown + ' update=' + updateOk);

    /* 15/16/17. 复用语义（S2b）：徽标 + 全复用无 git 提示 + 会话门禁放宽 */
    window.__MOCK__.reuseAll = true;
    window.__MOCK__.sessionReady = false;
    clickEl(document.getElementById('btn-reload-manifests'));
    await poll(function () { return window.__MOCK__.calls.manifests >= 4; }, 3000, 50);
    clickEl(document.getElementById('btn-preflight'));
    var reuseReady = await poll(function () {
      var b = document.getElementById('plan-lib-badge');
      return b && b.textContent === '复用';
    }, 4000, 50);
    var planText = (document.getElementById('plan-repo') || {}).textContent || '';
    record('15', '复用徽标（ReusedLibs 命中 → 「复用」+「将复用已有库」）',
      reuseReady && planText.indexOf('将复用已有库') !== -1,
      'badge=' + ((document.getElementById('plan-lib-badge') || {}).textContent || 'none') +
      ' hint=' + (planText.indexOf('将复用已有库') !== -1));

    var gitText = document.getElementById('git-cmds').textContent || '';
    record('16', '全复用无 git 序列提示（无需 git 操作（全部复用））',
      gitText.indexOf('无需 git 操作（全部复用）') !== -1 && gitText.indexOf('git clone') === -1,
      'sample=' + gitText.slice(0, 60));

    var gate17 = await poll(function () {
      return document.getElementById('btn-exec').disabled === false;
    }, 3000, 50);
    record('17', '全复用会话门禁放宽（sessionReady=false 不阻断执行）',
      gate17, 'gate=' + (document.getElementById('exec-gate').textContent || ''));

    window.__MOCK__.reuseAll = false;
    window.__MOCK__.sessionReady = true;

    /* 2b. 终态无溢出（预检+任务+diff+提交全渲染） */
    record('2b', '屏3 init 终态：content 无纵向溢出/无横向滚动',
      content.scrollHeight <= content.clientHeight + 1 &&
      content.scrollWidth <= content.clientWidth + 1,
      overflowDetail(content));

    /* 10. 禁用资源后缀 */
    var nodes = document.querySelectorAll('script[src],link[href]');
    var badRes = [];
    Array.prototype.forEach.call(nodes, function (n) {
      var u = n.getAttribute('src') || n.getAttribute('href') || '';
      if (/\.(mjs|json|map|wasm|woff2?)(\?|#|$)/i.test(u)) badRes.push(u);
    });
    record('10', '禁用资源（mjs/json/map/wasm/woff）', badRes.length === 0,
      badRes.length ? ('bad: ' + badRes.join(', ')) : ('checked=' + nodes.length));

    if (!gate7c) record('7b', '执行门禁恢复（sessionReady=true 重预检）', false, 'exec 仍禁用');
    finish();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function () {
      run().catch(function (e) {
        record('probe', 'probe-crash', false, String(e && e.stack || e));
        finish();
      });
    });
  } else {
    run().catch(function (e) {
      record('probe', 'probe-crash', false, String(e && e.stack || e));
      finish();
    });
  }
})();
`;

/* ---------------- 临时 HTML 生成 ---------------- */

function buildProbeHtml() {
  const webDir = path.join(__dirname, '..', 'web');
  let html = fs.readFileSync(path.join(webDir, 'index.html'), 'utf8');
  const assetsUrl = pathToFileURL(path.join(webDir, 'assets')).href;
  html = html.replace(/(src|href)="assets\//g, '$1="' + assetsUrl + '/');
  if (html.indexOf('</body>') === -1) throw new Error('index.html 缺少 </body>');
  html = html.replace('</body>', '<script>\n' + PROBE_JS + '\n</script>\n</body>');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'yunxiao-s2-assert-'));
  const file = path.join(dir, 'probe.html');
  fs.writeFileSync(file, html, 'utf8');
  return file;
}

/* ---------------- PROBE::JSON 提取 ---------------- */

function decodeEntities(s) {
  return s
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&amp;/g, '&');
}

function parseProbePayload(text) {
  // title 通道：原始 JSON（CDP）或 HTML 转义后的 JSON（--dump-dom）
  if (text.indexOf('PROBE::') !== 0) throw new Error('title 无 PROBE:: 前缀');
  return JSON.parse(text.slice('PROBE::'.length));
}

function extractFromDom(dump) {
  const m = dump.match(/<title>PROBE::([\s\S]*?)<\/title>/);
  if (!m) return null;
  return parseProbePayload('PROBE::' + decodeEntities(m[1]));
}

/* ---------------- 进程清理（按 --user-data-dir 命令行匹配） ---------------- */

function killEdgeByUserDataDir(udd) {
  try {
    spawnSync('powershell', [
      '-NoProfile', '-NonInteractive', '-Command',
      "Get-CimInstance Win32_Process -Filter \"Name='msedge.exe'\" | " +
        "Where-Object { $_.CommandLine -like '*" + udd + "*' } | " +
        "ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"
    ], { timeout: 20000 });
  } catch (e) { /* 清理失败不阻塞报告 */ }
}

/* ---------------- 通道 B（回退）：--dump-dom（spec 命令） ---------------- */

function tryDumpDom(htmlFile) {
  const udd = path.join(os.tmpdir(), 'yunxiao-s2-dump-udd-' + Date.now());
  const fileUrl = pathToFileURL(htmlFile).href;
  console.log('[channel] 尝试 --dump-dom: ' + fileUrl);
  let out = '';
  try {
    const res = spawnSync(EDGE, [
      '--headless=new', '--disable-gpu',
      '--user-data-dir=' + udd,
      '--window-size=' + VIEWPORT.width + ',' + VIEWPORT.height,
      '--virtual-time-budget=5000',
      '--dump-dom', fileUrl
    ], { encoding: 'utf8', timeout: 30000, maxBuffer: 32 * 1024 * 1024 });
    out = (res.stdout || '') + '';
  } catch (e) {
    console.log('[channel] --dump-dom 调用异常: ' + e.message);
  }
  killEdgeByUserDataDir(udd);
  if (!out.trim()) {
    console.log('[channel] --dump-dom 输出为空（Edge 157 MSIX launcher 丢弃 stdout 的已知问题），本通道放弃');
    return null;
  }
  return extractFromDom(out);
}

/* ---------------- 通道 A（首选）：CDP（remote-debugging-port + 原生 WebSocket） ---------------- */

function httpGetJson(url, timeoutMs) {
  return new Promise(function (resolve, reject) {
    const req = http.get(url, { timeout: timeoutMs || 3000 }, function (res) {
      let body = '';
      res.on('data', function (c) { body += c; });
      res.on('end', function () {
        try { resolve(JSON.parse(body)); } catch (e) { reject(e); }
      });
    });
    req.on('error', reject);
    req.on('timeout', function () { req.destroy(new Error('timeout')); });
  });
}

function sleep(ms) {
  return new Promise(function (r) { setTimeout(r, ms); });
}

class CdpClient {
  constructor(ws) {
    this.ws = ws;
    this.seq = 0;
    this.pending = new Map();
    ws.addEventListener('message', (ev) => {
      const msg = JSON.parse(String(ev.data));
      if (msg.id && this.pending.has(msg.id)) {
        const p = this.pending.get(msg.id);
        this.pending.delete(msg.id);
        if (msg.error) p.reject(new Error(msg.error.message));
        else p.resolve(msg.result);
      }
    });
  }
  static connect(url) {
    return new Promise(function (resolve, reject) {
      const ws = new WebSocket(url);
      const timer = setTimeout(function () { reject(new Error('WebSocket 连接超时')); }, 8000);
      ws.addEventListener('open', function () {
        clearTimeout(timer);
        resolve(new CdpClient(ws));
      });
      ws.addEventListener('error', function () {
        clearTimeout(timer);
        reject(new Error('WebSocket 连接失败'));
      });
    });
  }
  send(method, params) {
    const id = ++this.seq;
    const self = this;
    return new Promise(function (resolve, reject) {
      self.pending.set(id, { resolve: resolve, reject: reject });
      self.ws.send(JSON.stringify({ id: id, method: method, params: params || {} }));
      setTimeout(function () {
        if (self.pending.has(id)) {
          self.pending.delete(id);
          reject(new Error('CDP 超时: ' + method));
        }
      }, 20000);
    });
  }
  close() {
    try { this.ws.close(); } catch (e) { /* ignore */ }
  }
}

async function runCdp(htmlFile) {
  const udd = path.join(os.tmpdir(), 'yunxiao-s2-cdp-udd-' + Date.now() + '-' + process.pid);
  const port = 9200 + Math.floor(Math.random() * 700);
  const fileUrl = pathToFileURL(htmlFile).href;
  console.log('[channel] CDP 通道: headless + --remote-debugging-port=' + port);

  const child = spawn(EDGE, [
    '--headless=new', '--disable-gpu',
    '--user-data-dir=' + udd,
    '--remote-debugging-port=' + port,
    'about:blank'
  ], { detached: true, stdio: 'ignore' });
  child.unref();

  try {
    // 等 DevTools 端点就绪（launcher 会立即退出，真实实例稍后监听端口）
    let version = null;
    for (let i = 0; i < 60; i++) {
      try {
        version = await httpGetJson('http://127.0.0.1:' + port + '/json/version', 2000);
        break;
      } catch (e) {
        await sleep(250);
      }
    }
    if (!version) throw new Error('DevTools 端点未就绪');
    console.log('[channel] ' + version.Browser + ' / ' + String(version['User-Agent'] || '').slice(0, 60));

    const list = await httpGetJson('http://127.0.0.1:' + port + '/json/list', 3000);
    const page = list.filter(function (t) { return t.type === 'page'; })[0];
    if (!page || !page.webSocketDebuggerUrl) throw new Error('无 page 调试目标');

    const cdp = await CdpClient.connect(page.webSocketDebuggerUrl);
    try {
      await cdp.send('Page.enable');
      await cdp.send('Emulation.setDeviceMetricsOverride', {
        width: VIEWPORT.width, height: VIEWPORT.height, deviceScaleFactor: 1, mobile: false
      });
      await cdp.send('Page.navigate', { url: fileUrl });

      // 轮询 title，等探针写入 PROBE::JSON
      const deadline = Date.now() + 45000;
      let title = '';
      while (Date.now() < deadline) {
        const r = await cdp.send('Runtime.evaluate', {
          expression: 'document.title', returnByValue: true
        });
        title = (r && r.result && r.result.value) || '';
        if (title.indexOf('PROBE::') === 0) return parseProbePayload(title);
        await sleep(200);
      }
      throw new Error('等待 PROBE:: title 超时（最后 title=' + title + '）');
    } finally {
      cdp.close();
    }
  } finally {
    killEdgeByUserDataDir(udd);
  }
}

/* ---------------- 报告 ---------------- */

function report(payload) {
  const results = (payload && payload.results) || [];
  let failed = 0;
  console.log('');
  console.log('---- S2 前端断言结果 ----');
  for (const r of results) {
    const status = r.pass ? 'PASS' : 'FAIL';
    if (!r.pass) failed++;
    console.log('[' + status + '] ' + r.id + ' ' + r.name + ' — ' + (r.detail || ''));
  }
  console.log('-------------------------');
  console.log('合计: ' + (results.length - failed) + '/' + results.length + ' PASS' +
    (failed ? ('，' + failed + ' FAIL') : ''));
  return failed;
}

async function main() {
  if (!fs.existsSync(EDGE)) {
    console.error('未找到 msedge.exe: ' + EDGE);
    process.exit(2);
  }
  const htmlFile = buildProbeHtml();
  console.log('[probe] 临时页面: ' + htmlFile);

  let payload = null;
  let channel = '';

  // 通道 A（首选）：CDP —— Emulation.setDeviceMetricsOverride 精确钉住 826×694 视口
  try {
    payload = await runCdp(htmlFile);
    if (payload) channel = 'CDP(remote-debugging-port)';
  } catch (e) {
    console.log('[channel] CDP 通道失败: ' + e.message + ' → 回退 --dump-dom');
  }

  // 通道 B（回退）：spec 的 --dump-dom（视口靠 --window-size 尽力而为）
  if (!payload) {
    payload = tryDumpDom(htmlFile);
    if (payload) channel = '--dump-dom';
  }

  if (!payload) {
    console.error('[channel] 两个通道均未取得 PROBE:: 输出');
    process.exit(2);
  }

  console.log('[channel] 捕获成功: ' + channel);
  const failed = report(payload);

  if (!process.env.KEEP_TMP) {
    try {
      fs.rmSync(path.dirname(htmlFile), { recursive: true, force: true });
    } catch (e) { /* ignore */ }
  } else {
    console.log('[probe] KEEP_TMP=1，临时目录保留: ' + path.dirname(htmlFile));
  }
  process.exit(failed ? 1 : 0);
}

main().catch(function (e) {
  console.error('[fatal] ' + (e && e.stack || e));
  console.error('断言驱动失败（不视为通过）。可按 tests/assert-s2.js 头部清单人工核对。');
  process.exit(2);
});
