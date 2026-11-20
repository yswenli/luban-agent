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
                                   + groups/workspaces/sessionReady（MOCK.wsCreatedPath 后追加新建工作区）
    POST /yunxiao/preflight      → plan 三段 + gitCommands（脱敏 <REPO_URL>）+ conflicts + sessionReady
                                   + reusedLibs/warnings（MOCK.reuseAll=true：全复用零 git 步骤；
                                   MOCK.reuseCheckout=true：全复用但缺源码 → 本地检出步骤非空）
    POST /yunxiao/initexec       → 'task-1'
    GET  /yunxiao/inittask       → 重跑前 {status:1, libs:[{status:5=failed}]}（点出重跑）；
                                   initretry 之后 → {status:2=done, libs:[{status:4=done}]}（终态即停轮询）
    POST /yunxiao/initretry      → 'task-1'
    POST /yunxiao/pipelinepreview → mobile/admin 两段互异 diff + content + exists（mobile 缺失/admin 已存在）；
                                   withBackendInfo=true 时追加 backend（exists + pipeline{Id,ActualName,Port}）；
                                   emptyPrefixMatch=true 时回显请求中的空前缀名 + 后缀段命中真实名（§13.3 仿真）
    POST /yunxiao/pipelinecommit → 逐端 {end,ok,pipelineId|message}（admin 补全语义「已存在·已同步信息」含 Id）；
                                   emptyPrefixMatch=true 时按后缀段命中仿真回「已存在·已同步信息（ACME 真名）」
    GET  /yunxiao/pipelinederive → 逐端 {end,from,to}（S2e §14 修复一）：MOCK.deriveRealName=true 时
                                   反查 ACME 真名（from=ACME_DEV_Deploy_TPLX-*，to 带 ACME_ 前缀）；
                                   false 时 from:null、to=DEV_Deploy_{代号}-{端}（无前导下划线兜底）
    POST /workspace/create       → ensure 幂等（S2d §13.2）：rootPath 命中既有 /w/demo → reused:true 复用 w1；
                                   否则新建（reused:false，MOCK.wsCreatedPath 后追加）；MOCK.wsCreateFail=true 时 400 语义失败信封
    GET  /workspace/dirs         → 迷你目录浏览器单层子目录（S2d §13.1）：DIRS 表按 path 查，空 path 返回根层
  状态数字映射以 init.js 为准（normTaskStatus: 0=pending/1=running/2=done/3=failed；
  normLibStatus: 0=pending/1=cloning/2=renaming/3=pushing/4=done/5=failed），与服务端
  InitTaskStatus / LibStatus 枚举同序。夹具一律合成名/合成端口（TPLX/NEWX/ACME、7001/9000）。

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
    17  全复用会话门禁（S2c §12.2 口径，收紧 S2b）：零 git 步骤免会话不阻断；
        有检出步骤（缺本地源码）时 sessionReady=false 必须阻断（不得放宽）
    18  F6 Exists 标注（S2b）：「已存在（提交时跳过）/缺失（将新建）」
    19  工作区手输新建入口（S2c §12.1.1）：输入框+按钮渲染、空路径禁用、trim 提交
        POST /workspace/create → 重拉 manifests 选中新工作区；失败显示宿主 message
    20  派生后端名含 (端口)（S2c §12.3）：端口盒变更 → 后端流水线名重派生
    21  已存在端反向回填（S2c §12.3）：端口盒←解析端口（值变化）、流水线名←ActualName、「已同步」徽标
    22  同步回执含 Id（S2c §12.3.4）：已存在端「已同步 · Id · ActualName」可追溯
    23  全复用 F6 不锁（S2c §12.3.5）：全复用不等 FR-2 任务即可预览/同步；非全复用未 done 仍锁
    24  工作区搜索过滤（S2d §13.1）：组合框搜索框按 rootPath/名称子串过滤（大小写不敏感）、空态「无匹配」
    25  目录浏览器（S2d §13.1）：入口渲染 + mock /workspace/dirs 子目录列表 + 逐层进入 + 面包屑回退
        + 「选此目录」回填路径输入框（仅子目录、无文件、数据仅内存）
    26  ensure 语义提示区分（S2d §13.2）：既有工作区命中 →「已复用现有工作区」+ 选中；
        新路径 →「已新建工作区」
    27  空前缀匹配兜底（S2d §13.3）：空前缀提交名（_DEV_Deploy_*）经后缀段命中回执
        「已存在·已同步信息（ACME 真名）」，行内可追溯
    28  预检回填含真实前缀（S2d §13.3）：plan.pipelines（ACME_ 真前缀）回填 F6 流水线名；
        dirty 用户改写不被覆盖；占位派生 hint「以预检为准」随回填收起
    29  派生预览无前导下划线（S2e §14 修复一）：反查真名 mock → to 带 ACME_ 前缀；
        反查不到兜底 → to=DEV_Deploy_*（两态 to 均不得以 '_' 开头）
    30  代码组自动预选（S2e §14 修复二）：代号同名组（大小写不敏感）优先 → 无则模板代号同名组
    31  代码组用户改过后不覆盖（S2e §14 修复二）：手动改组选择后代号再变仍保持用户值
    32  根目录即检出 → 预检零 git 命令（S2e §14 修复三仿真）：命令序列区显示「无需 git 操作」、无 clone
    33  复用库预检不显示「将改名目录」（S2e §14 修复四）：复用库改显「结构校验」行（子目录存在性/待校验）
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
    reuseCheckout: false,      // S2c §12.2：全复用但缺源码 → 预检 git 步骤非空（本地检出）
    withBackendInfo: false,    // S2c §12.3：preview 追加 backend（exists + pipeline 回显）
    emptyPrefixMatch: false,   // S2d §13.3：preview/commit 后缀段命中仿真（空前缀名 ↔ ACME 真名）
    deriveRealName: true,      // S2e §14：派生接口反查真名（false = 反查不到 → 无前导下划线兜底）
    localSourceOnly: false,    // S2e §14：工作区根即检出 → 预检零 git 步骤（非复用）
    wsCreatedPath: '',         // S2c §12.1.1：/workspace/create 成功后 manifests 追加该工作区
    wsCreateFail: false,       // S2c §12.1.1：/workspace/create 返回 400 语义失败信封
    calls: { settings: 0, projects: 0, manifests: 0, preflight: 0, initexec: 0, inittask: 0, initretry: 0, pipelinepreview: 0, pipelinecommit: 0, pipelinederive: 0, wsdirs: 0 },
    posts: []
  };
  window.__MOCK__ = MOCK;

  /* S2d §13.1：目录浏览器单层子目录表（合成路径；仅子目录、无文件） */
  var DIRS = {
    '': [{ name: 'w', path: '/w' }],
    '/w': [
      { name: 'demo', path: '/w/demo' },
      { name: 'new-ws', path: '/w/new-ws' }
    ],
    '/w/demo': [{ name: 'alpha', path: '/w/demo/alpha' }]
  };

  var PREFLIGHT_DATA = {
    plan: {
      repoName: 'NEWX-Mixed',
      dirs: [
        { from: 'TPLX-Admin', to: 'NEWX-Admin' },
        { from: 'TPLX-Mobile', to: 'NEWX-Mobile' },
        { from: 'TPLX-Service', to: 'NEWX-Service' }
      ],
      /* S2d §13.3：预检回执即正确名（服务端反查真实前缀派生）——三端齐全供 F6 回填 */
      pipelines: [
        { from: 'ACME_DEV_Deploy_TPLX-Mobile', to: 'ACME_DEV_Deploy_NEWX-Mobile' },
        { from: 'ACME_DEV_Deploy_TPLX-Admin', to: 'ACME_DEV_Deploy_NEWX-Admin' },
        { from: 'ACME_DEV_Deploy_TPLX-Service', to: 'ACME_DEV_Deploy_NEWX-Service' }
      ]
    },
    gitCommands: [
      { label: '克隆母版', command: 'git clone --branch master <REPO_URL> /w/demo/NEWX-Mixed' },
      { label: '改名', command: 'git -C "/w/demo/NEWX-Mixed" mv TPLX-Admin NEWX-Admin' }
    ],
    conflicts: [],
    sessionReady: true,
    reusedLibs: [],
    warnings: [],
    structureChecks: []
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
      var wsList = [{ id: 'w1', rootPath: '/w/demo' }];
      /* S2c §12.1.1：工作区新建成功后，清单追加新工作区（前端按 rootPath 回选） */
      if (MOCK.wsCreatedPath) wsList.push({ id: 'w-new', rootPath: MOCK.wsCreatedPath });
      data = {
        repos: [{ name: LONG_TPL, description: LONG_DESC, templateProjectId: 321 }],
        /* S2e §14 修复二夹具：TPLX = 模板代号同名组（兜底），NEWX = 新代号同名组（优先） */
        groups: [{ id: '11', name: 'DEMOGRP' }, { id: '12', name: 'TPLX' }, { id: '13', name: 'NEWX' }],
        workspaces: wsList,
        sessionReady: MOCK.sessionReady
      };
    } else if (u.indexOf('/yunxiao/pipelinederive') !== -1) {
      MOCK.calls.pipelinederive++;
      /* S2e §14 修复一：GET 查询串 newCode + ends(JSON)；deriveRealName=false 仿真反查不到
         → from:null、to=DEV_Deploy_*（无前导下划线兜底）；backend 带 (端口) 后缀 */
      var dvCode = '';
      var mDvCode = /[?&]newCode=([^&]*)/.exec(u);
      if (mDvCode) dvCode = decodeURIComponent(mDvCode[1]);
      var dvEnds = [];
      var mDvEnds = /[?&]ends=([^&]*)/.exec(u);
      if (mDvEnds) {
        try { dvEnds = JSON.parse(decodeURIComponent(mDvEnds[1])); } catch (e2) { dvEnds = []; }
      }
      data = { items: (dvEnds || []).filter(function (x) { return x && x.on; }).map(function (x) {
        var suf = x.end === 'mobile' ? 'Mobile' : (x.end === 'admin' ? 'Admin' : 'Service');
        var from = null;
        var to = '';
        if (MOCK.deriveRealName) {
          from = 'ACME_DEV_Deploy_TPLX-' + suf;
          to = 'ACME_DEV_Deploy_' + dvCode + '-' + suf;
        } else {
          to = 'DEV_Deploy_' + dvCode + '-' + suf;
        }
        if (x.end === 'backend' && x.port) to = to + '(' + x.port + ')';
        return { end: x.end, from: from, to: to };
      }) };
    } else if (u.indexOf('/yunxiao/preflight') !== -1) {
      MOCK.calls.preflight++;
      MOCK.posts.push({ path: '/yunxiao/preflight', body: body });
      var pre = JSON.parse(JSON.stringify(PREFLIGHT_DATA));
      pre.sessionReady = MOCK.sessionReady;
      if (MOCK.reuseAll) {
        /* 全复用：目标库命中 ReusedLibs；默认零 git 步骤 + 结构待校验提示
           （S2e §14 修复四：复用库出结构校验字段，前端不显示「将改名目录」对照） */
        pre.reusedLibs = [pre.plan.repoName];
        pre.gitCommands = [];
        pre.warnings = ['结构待校验：本地未克隆 ' + pre.plan.repoName];
        pre.structureChecks = ['本地未克隆 ' + pre.plan.repoName + '：子目录待校验'];
        if (MOCK.reuseCheckout) {
          /* S2c §12.2：全复用但本地缺源码 → 本地检出步骤非空（PlanLocalCheckout 同源形态，无凭据） */
          pre.gitCommands = [
            { label: '检出库 ' + pre.plan.repoName, command: 'git clone --branch master <REPO_URL> /w/demo/' + pre.plan.repoName },
            { label: '跟踪远端 develop', command: 'git -C "/w/demo/' + pre.plan.repoName + '" checkout -b develop origin/develop' },
            { label: '本地新建 develop（上一步失败时）', command: 'git -C "/w/demo/' + pre.plan.repoName + '" checkout -b develop' }
          ];
        }
      }
      if (MOCK.localSourceOnly) {
        /* S2e §14 修复三仿真：工作区根即检出（本地已有源码）→ 零 git 步骤、非复用（新建徽标） */
        pre.gitCommands = [];
        pre.warnings = [];
        pre.reusedLibs = [];
        pre.structureChecks = [];
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
      if (MOCK.emptyPrefixMatch) {
        /* S2d §13.3 仿真：回显请求中的空前缀名（服务端 end.PipelineName 口径），
           后缀段命中真实名 → exists + PipelineInfo 回显（模拟服务端匹配兜底） */
        var beName = '';
        ((body && body.ends) || []).forEach(function (x) {
          if (x.end === 'backend') beName = String(x.pipelineName || '');
        });
        data = { items: [
          { end: 'backend', name: beName, diff: '-z\n+stages:\n+  - backend', content: 'stages:\n  - backend',
            warning: null, exists: true,
            pipeline: { id: '9001', actualName: 'ACME_DEV_Deploy_NEWX-Service(9000)', port: 9000 } }
        ] };
      } else {
        /* diff 的 + 行须能重建顶层 stages:（init.js contentOf 重建口径）；
           exists：mobile 缺失（将新建）/ admin 已存在（提交时跳过）；
           withBackendInfo=true 时追加 backend 端（已存在 + pipeline 回显/反向回填） */
        var items = [
          { end: 'mobile', name: 'ACME_DEV_Deploy_NEWX-Mobile', diff: '-x\n+stages:\n+  - mobile', content: 'stages:\n  - mobile', warning: null, exists: false },
          { end: 'admin', name: 'ACME_DEV_Deploy_NEWX-Admin', diff: '-y\n+stages:\n+  - admin', content: 'stages:\n  - admin', exists: true }
        ];
        if (MOCK.withBackendInfo) {
          /* S2c §12.3：已存在端回显 PipelineInfo{Id,ActualName,Port}（反向回填端口盒 9000；夹具合成值） */
          items.push({
            end: 'backend', name: 'ACME_DEV_Deploy_NEWX-Service', diff: '-z\n+stages:\n+  - backend',
            content: 'stages:\n  - backend', exists: true,
            pipeline: { id: '9001', actualName: 'ACME_DEV_Deploy_NEWX-Service(9000)', port: 9000 }
          });
        }
        data = { items: items };
      }
    } else if (u.indexOf('/yunxiao/pipelinecommit') !== -1) {
      MOCK.calls.pipelinecommit++;
      MOCK.posts.push({ path: '/yunxiao/pipelinecommit', body: body });
      if (MOCK.emptyPrefixMatch) {
        /* S2d §13.3 仿真：后缀段命中兜底——空前缀 create 名命中 ACME 真名
           → 「已存在·已同步信息」（含 Id/ActualName，与服务端 TOCTOU 回执同形） */
        data = { results: ((body && body.items) || []).map(function (it) {
          return {
            end: it.end, ok: true, pipelineId: '9001',
            message: '已存在·已同步信息（ACME_DEV_Deploy_NEWX-Service(9000)）'
          };
        }) };
      } else {
        /* admin 走补全语义：服务端「已存在·已同步信息」回执（含 Id/ActualName）；backend 同 */
        var results = [
          { end: 'mobile', ok: true, pipelineId: '701' },
          { end: 'admin', ok: true, pipelineId: '801', message: '已存在·已同步信息（ACME_DEV_Deploy_NEWX-Admin）' }
        ];
        if (MOCK.withBackendInfo) {
          results.push({ end: 'backend', ok: true, pipelineId: '9001', message: '已存在·已同步信息（ACME_DEV_Deploy_NEWX-Service(9000)）' });
        }
        data = { results: results };
      }
    } else if (u.indexOf('/workspace/dirs') !== -1) {
      MOCK.calls.wsdirs++;
      /* S2d §13.1：单层子目录（DIRS 表按 path 查；空/缺省 = 根层） */
      var dp = '';
      var qi = u.indexOf('path=');
      if (qi >= 0) dp = decodeURIComponent(u.slice(qi + 5).split('&')[0]);
      data = { dirs: (DIRS[dp] || []).slice() };
    } else if (u.indexOf('/workspace/create') !== -1) {
      MOCK.calls.wscreate = (MOCK.calls.wscreate || 0) + 1;
      MOCK.posts.push({ path: '/workspace/create', body: body });
      if (MOCK.wsCreateFail) {
        /* S2c §12.1.1：宿主 400 语义（ApiEnvelope success:false → ApiError，前端显示 message） */
        return Promise.resolve({
          ok: true,
          status: 200,
          text: function () {
            return Promise.resolve(JSON.stringify({
              success: false, code: 400, message: 'rootPath 必须是绝对路径', data: null
            }));
          }
        });
      }
      var rp = String((body && body.rootPath) || '').trim();
      /* S2d §13.2 ensure 幂等：rootPath 规范化命中既有工作区（/w/demo = w1）→ reused:true 复用 */
      if (rp.toLowerCase() === '/w/demo') {
        data = { reused: true, workspaceId: 'w1', name: 'demo', rootPath: '/w/demo', type: 'Normal' };
      } else {
        MOCK.wsCreatedPath = rp;
        data = { reused: false, workspaceId: 'w-new', id: 'w-new', name: '新工作区', rootPath: rp, type: 'Normal' };
      }
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

    /* 19. 工作区手输新建入口（S2c §12.1.1）：渲染 + 空禁用 + trim 提交 + 成功选中 + 失败报错 */
    var wsPath = document.getElementById('ws-new-path');
    var wsBtn = document.getElementById('btn-ws-create');
    var wsResult = document.getElementById('ws-new-result');
    var wsEntryOk = !!wsPath && !!wsBtn && !!wsResult && wsBtn.disabled === true;
    setInput(wsPath, '  /w/new-ws  ');
    var wsEnabled = wsBtn.disabled === false;
    clickEl(wsBtn);
    var wsPosted = await poll(function () {
      return (window.__MOCK__.calls.wscreate || 0) >= 1;
    }, 3000, 50);
    var wsSelected = await poll(function () {
      var sel = document.getElementById('init-workspace');
      return sel && sel.value === 'w-new';
    }, 3000, 50);
    var wsBody = null;
    window.__MOCK__.posts.forEach(function (p) {
      if (p.path === '/workspace/create') wsBody = p.body;
    });
    var wsTrimOk = !!wsBody && wsBody.rootPath === '/w/new-ws';
    var wsShown = !!wsResult && !wsResult.hidden && (wsResult.textContent || '').indexOf('新建') !== -1;
    var wsCleared = wsPath.value === '' && wsBtn.disabled === true;
    window.__MOCK__.wsCreateFail = true;
    setInput(wsPath, 'bad-rel-path');
    clickEl(wsBtn);
    var wsErrShown = await poll(function () {
      return wsResult && !wsResult.hidden && (wsResult.textContent || '').indexOf('绝对路径') !== -1;
    }, 3000, 50);
    window.__MOCK__.wsCreateFail = false;
    record('19', '工作区新建入口（渲染/空禁用/trim 提交/成功选中/失败报错）',
      wsEntryOk && wsEnabled && wsPosted && wsSelected && wsTrimOk && wsShown && wsCleared && wsErrShown,
      'entry=' + wsEntryOk + ' enabled=' + wsEnabled + ' posted=' + wsPosted +
      ' selected=' + wsSelected + ' trim=' + wsTrimOk + ' shown=' + wsShown +
      ' cleared=' + wsCleared + ' err=' + wsErrShown);

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

    /* 20. 派生后端名含 (端口)（S2c §12.3）：端口盒变更 → 后端流水线名重派生（合成端口 8080→7001） */
    var backendRow20 = document.querySelector('#pipe-ends .endrow[data-end="backend"]');
    var backendPort20 = backendRow20 ? backendRow20.querySelector('[data-f="port"]') : null;
    var backendName20 = backendRow20 ? backendRow20.querySelector('[data-f="name"]') : null;
    var nameDefaultPort = backendName20 ? backendName20.value : '';
    if (backendPort20) setInput(backendPort20, '7001');
    var nameNewPort = backendName20 ? backendName20.value : '';
    record('20', '派生后端名含 (端口)（端口盒变更 → 重派生）',
      !!backendPort20 && !!backendName20
        && nameDefaultPort.indexOf('(8080)') !== -1
        && nameNewPort.indexOf('(7001)') !== -1
        && nameNewPort.indexOf('NEWX-Service') !== -1,
      'default=' + nameDefaultPort + ' after=' + nameNewPort);

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
    var mBase7a = window.__MOCK__.calls.manifests;
    clickEl(document.getElementById('btn-reload-manifests'));
    await poll(function () { return window.__MOCK__.calls.manifests > mBase7a; }, 3000, 50);
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
    var mBase7b = window.__MOCK__.calls.manifests;
    clickEl(document.getElementById('btn-reload-manifests'));
    await poll(function () { return window.__MOCK__.calls.manifests > mBase7b; }, 3000, 50);
    clickEl(document.getElementById('btn-preflight'));
    var gate7c = await poll(function () {
      return document.getElementById('btn-exec').disabled === false;
    }, 4000, 50);

    /* 23. 全复用 F6 不锁（S2c §12.3.5）：全复用不等 FR-2 任务完成即可预览/同步；非全复用未 done 仍锁 */
    window.__MOCK__.reuseAll = true;
    clickEl(document.getElementById('btn-preflight'));
    var f6Unlocked = await poll(function () {
      return document.getElementById('btn-preview').disabled === false
        && document.getElementById('btn-commit').disabled === false
        && document.getElementById('pipe-lock').hidden === true;
    }, 4000, 50);
    window.__MOCK__.reuseAll = false;
    clickEl(document.getElementById('btn-preflight'));
    var f6Relocked = await poll(function () {
      return document.getElementById('btn-preview').disabled === true
        && document.getElementById('pipe-lock').hidden === false;
    }, 4000, 50);
    record('23', '全复用 F6 不锁（不等 FR-2 任务；非全复用未 done 仍锁）',
      f6Unlocked && f6Relocked,
      'unlocked=' + f6Unlocked + ' relocked=' + f6Relocked);

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

    /* 22. 同步回执含 Id（S2c §12.3.4）：已存在端「已同步 · Id · ActualName」可追溯 */
    var adminResult22 = document.querySelector('#pipe-ends .endrow[data-end="admin"] [data-f="result"]');
    var adminText22 = adminResult22 ? (adminResult22.textContent || '') : '';
    record('22', '同步回执含 Id（已同步 · Id · ActualName 可追溯）',
      !!adminResult22 && !adminResult22.hidden
        && adminText22.indexOf('已同步') !== -1
        && adminText22.indexOf('801') !== -1
        && adminText22.indexOf('ACME_DEV_Deploy_NEWX-Admin') !== -1,
      'result=' + adminText22);

    /* 21. 已存在端反向回填（S2c §12.3）：端口盒←解析端口（值变化 7001→9000）、流水线名←ActualName、已同步徽标 */
    var backendRow21 = document.querySelector('#pipe-ends .endrow[data-end="backend"]');
    var portBefore21 = backendRow21 ? (backendRow21.querySelector('[data-f="port"]') || {}).value : '';
    window.__MOCK__.withBackendInfo = true;
    clickEl(document.getElementById('btn-preview'));
    var fillOk = await poll(function () {
      var row = document.querySelector('#pipe-ends .endrow[data-end="backend"]');
      if (!row) return false;
      var port = row.querySelector('[data-f="port"]');
      var name = row.querySelector('[data-f="name"]');
      var synced = row.querySelector('[data-f="synced"]');
      return port && String(port.value) === '9000'
        && name && name.value === 'ACME_DEV_Deploy_NEWX-Service(9000)'
        && !!synced && (synced.textContent || '') === '已同步';
    }, 5000, 50);
    var existsBadge21 = document.querySelector('#pipe-ends .endrow[data-end="backend"] [data-f="exists"]');
    record('21', '已存在端反向回填（端口盒值变化←(9000)、流水线名←ActualName、已同步徽标）',
      fillOk && String(portBefore21) === '7001'
        && !!existsBadge21 && existsBadge21.textContent === '已存在（提交时跳过）',
      'portWas=' + portBefore21 + ' fill=' + fillOk +
      ' exists=' + (existsBadge21 ? existsBadge21.textContent : 'none'));

    /* 15/16/17. 复用语义（S2b/S2c）：徽标 + 全复用无 git 提示 + 会话门禁（S2c §12.2 收紧口径） */
    window.__MOCK__.reuseAll = true;
    window.__MOCK__.sessionReady = false;
    var mBase15 = window.__MOCK__.calls.manifests;
    clickEl(document.getElementById('btn-reload-manifests'));
    await poll(function () { return window.__MOCK__.calls.manifests > mBase15; }, 3000, 50);
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

    /* 17a. 全复用 + 零 git 步骤（全部已有本地源码）→ 免会话，不阻断 */
    var gate17a = await poll(function () {
      return document.getElementById('btn-exec').disabled === false;
    }, 3000, 50);
    /* 17b. 全复用 + 有检出步骤（缺本地源码）→ 仍要求会话，sessionReady=false 必须阻断（S2c §12.2，不得放宽） */
    window.__MOCK__.reuseCheckout = true;
    clickEl(document.getElementById('btn-preflight'));
    var gate17b = await poll(function () {
      return document.getElementById('btn-exec').disabled === true
        && (document.getElementById('exec-gate').textContent || '').indexOf('会话') !== -1;
    }, 4000, 50);
    record('17', '全复用会话门禁（零 git 步骤免会话 / 有检出步骤要求会话）',
      gate17a && gate17b,
      'zeroGit=' + gate17a + ' withCheckoutBlocked=' + gate17b +
      ' gate=' + (document.getElementById('exec-gate').textContent || ''));

    window.__MOCK__.reuseCheckout = false;
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

    /* ── S2d §13 增补判据（24-28） ──────────────────────────────────────── */

    /* 24. 工作区搜索过滤（S2d §13.1）：rootPath/名称子串、大小写不敏感、空态「无匹配」 */
    var wsTrigger24 = document.getElementById('ws-trigger');
    if (wsTrigger24) clickEl(wsTrigger24);
    var popOpen24 = await poll(function () {
      var p = document.getElementById('ws-pop');
      return p && !p.hidden;
    }, 3000, 50);
    var wsSearch24 = document.getElementById('ws-search');
    var wsList24 = document.getElementById('ws-list');
    var filterNew = false, filterCase = false, emptyOk = false;
    if (wsSearch24 && wsList24) {
      setInput(wsSearch24, 'new');
      filterNew = await poll(function () {
        return wsList24.querySelectorAll('.combo-opt').length === 1
          && (wsList24.textContent || '').indexOf('/w/new-ws') !== -1;
      }, 2000, 30);
      setInput(wsSearch24, 'DEMO');
      filterCase = await poll(function () {
        return wsList24.querySelectorAll('.combo-opt').length === 1
          && (wsList24.textContent || '').indexOf('/w/demo') !== -1
          && (wsList24.textContent || '').indexOf('new-ws') === -1;
      }, 2000, 30);
      setInput(wsSearch24, 'zzz-nomatch');
      emptyOk = await poll(function () {
        return wsList24.querySelectorAll('.combo-opt').length === 0
          && (wsList24.textContent || '').indexOf('无匹配') !== -1;
      }, 2000, 30);
      setInput(wsSearch24, '');
    }
    record('24', '工作区搜索过滤（子串/大小写不敏感/空态「无匹配」）',
      popOpen24 && filterNew && filterCase && emptyOk,
      'pop=' + popOpen24 + ' new=' + filterNew + ' case=' + filterCase + ' empty=' + emptyOk);

    /* 25. 目录浏览器（S2d §13.1）：入口 + mock 子目录列表 + 逐层进入 + 面包屑回退 + 选此目录回填 */
    var browseBtn25 = document.getElementById('ws-browse-btn');
    if (browseBtn25) clickEl(browseBtn25);
    var dirList25 = document.getElementById('ws-dir-list');
    var browseOpen25 = await poll(function () {
      var b = document.getElementById('ws-dir-browser');
      return b && !b.hidden && b.querySelectorAll('.dir-row').length === 1
        && (b.textContent || '').indexOf('w') !== -1;
    }, 3000, 50);
    var rowW = dirList25 ? dirList25.querySelector('.dir-row[data-path="/w"]') : null;
    if (rowW) clickEl(rowW);
    var level2 = await poll(function () {
      return dirList25.querySelectorAll('.dir-row').length === 2
        && (dirList25.textContent || '').indexOf('demo') !== -1
        && (dirList25.textContent || '').indexOf('new-ws') !== -1;
    }, 3000, 50);
    var crumbHome = document.querySelector('#ws-dir-crumb .crumb-seg[data-path=""]');
    if (crumbHome) clickEl(crumbHome);
    var backRoot = await poll(function () {
      return dirList25.querySelectorAll('.dir-row').length === 1
        && (dirList25.textContent || '').indexOf('new-ws') === -1;
    }, 3000, 50);
    var rowW2 = dirList25 ? dirList25.querySelector('.dir-row[data-path="/w"]') : null;
    if (rowW2) clickEl(rowW2);
    await poll(function () {
      return !!dirList25.querySelector('.dir-row[data-path="/w/demo"]');
    }, 3000, 50);
    var rowDemo = dirList25 ? dirList25.querySelector('.dir-row[data-path="/w/demo"]') : null;
    if (rowDemo) clickEl(rowDemo);
    var deepOk = await poll(function () {
      return (dirList25.textContent || '').indexOf('alpha') !== -1;
    }, 3000, 50);
    var chooseBtn = document.getElementById('ws-dir-choose');
    if (chooseBtn) clickEl(chooseBtn);
    var filled25 = String((document.getElementById('ws-new-path') || {}).value || '') === '/w/demo';
    record('25', '目录浏览器（入口/子目录列表/逐层进入/面包屑回退/选此目录回填）',
      browseOpen25 && level2 && backRoot && deepOk && filled25,
      'open=' + browseOpen25 + ' level2=' + level2 + ' back=' + backRoot +
      ' deep=' + deepOk + ' filled=' + filled25);

    /* 26. ensure 语义提示区分（S2d §13.2）：既有命中「已复用现有工作区」/ 新建「已新建工作区」 */
    var wsPath26 = document.getElementById('ws-new-path');
    var wsBtn26 = document.getElementById('btn-ws-create');
    var wsResult26 = document.getElementById('ws-new-result');
    setInput(wsPath26, '/w/demo');
    clickEl(wsBtn26);
    var reuseTip = await poll(function () {
      return wsResult26 && !wsResult26.hidden
        && (wsResult26.textContent || '').indexOf('已复用现有工作区') !== -1;
    }, 3000, 50);
    var reuseSel = await poll(function () {
      return (document.getElementById('init-workspace') || {}).value === 'w1';
    }, 3000, 50);
    setInput(wsPath26, '/w/another-new');
    clickEl(wsBtn26);
    var newTip = await poll(function () {
      return wsResult26 && !wsResult26.hidden
        && (wsResult26.textContent || '').indexOf('已新建工作区') !== -1;
    }, 3000, 50);
    var newSel = await poll(function () {
      return (document.getElementById('init-workspace') || {}).value === 'w-new';
    }, 3000, 50);
    record('26', 'ensure 语义提示区分（复用「已复用现有工作区」/ 新建「已新建工作区」）',
      reuseTip && reuseSel && newTip && newSel,
      'reuseTip=' + reuseTip + ' reuseSel=' + reuseSel + ' newTip=' + newTip + ' newSel=' + newSel);

    /* 27. 空前缀匹配兜底（S2d §13.3）：空前缀提交名 → 后缀段命中回执「已存在·已同步信息（ACME 真名）」 */
    window.__MOCK__.emptyPrefixMatch = true;
    var backendName27 = document.querySelector('#pipe-ends .endrow[data-end="backend"] [data-f="name"]');
    if (backendName27) setInput(backendName27, '_DEV_Deploy_NEWX-Service(9000)');
    var nameBefore27 = backendName27 ? String(backendName27.value || '') : '';
    clickEl(document.getElementById('btn-preview'));
    var prePost27 = await poll(function () {
      var list = window.__MOCK__.posts.filter(function (p) { return p.path === '/yunxiao/pipelinepreview'; });
      if (!list.length) return false;
      var ends = (list[list.length - 1].body && list[list.length - 1].body.ends) || [];
      return ends.some(function (x) {
        var n = String(x.pipelineName || '');
        return x.end === 'backend' && n.indexOf('_DEV_Deploy_') === 0 && n.indexOf('ACME') === -1;
      });
    }, 4000, 50);
    /* 等 doPreview 续体收口（state.preview 写入 + 名称反向回填）再提交，防异步竞态 */
    var previewDone27 = await poll(function () {
      var n = document.querySelector('#pipe-ends .endrow[data-end="backend"] [data-f="name"]');
      return n && n.value === 'ACME_DEV_Deploy_NEWX-Service(9000)';
    }, 4000, 50);
    clickEl(document.getElementById('btn-commit'));
    var commitPost27 = await poll(function () {
      var list = window.__MOCK__.posts.filter(function (p) { return p.path === '/yunxiao/pipelinecommit'; });
      if (!list.length) return false;
      var items = (list[list.length - 1].body && list[list.length - 1].body.items) || [];
      return items.some(function (i) {
        var n = String(i.name || '');
        return i.end === 'backend' && i.mode === 'create'
          && n.indexOf('_DEV_Deploy_') === 0 && n.indexOf('ACME') === -1;
      });
    }, 4000, 50);
    var receipt27 = await poll(function () {
      var row = document.querySelector('#pipe-ends .endrow[data-end="backend"] [data-f="result"]');
      if (!row || row.hidden) return false;
      var t = row.textContent || '';
      return t.indexOf('已同步') !== -1 && t.indexOf('9001') !== -1
        && t.indexOf('ACME_DEV_Deploy_NEWX-Service(9000)') !== -1;
    }, 4000, 50);
    window.__MOCK__.emptyPrefixMatch = false;
    record('27', '空前缀匹配兜底（提交名空前缀 → 回执「已存在·已同步信息」含 ACME 真名）',
      nameBefore27.indexOf('_DEV_Deploy_') === 0 && nameBefore27.indexOf('ACME') === -1
        && prePost27 && previewDone27 && commitPost27 && receipt27,
      'name=' + nameBefore27 + ' previewPost=' + prePost27 + ' previewDone=' + previewDone27 +
      ' commitPost=' + commitPost27 + ' receipt=' + receipt27);

    /* 28. 预检回填含真实前缀（S2d §13.3）：plan.pipelines 回填 F6；dirty 改写不覆盖；占位 hint 收起 */
    var code28 = document.getElementById('init-code');
    setInput(code28, 'NEWX2');
    setInput(code28, 'NEWX');
    var mobileName28 = document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="name"]');
    var hint28 = document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="namehint"]');
    // 占位派生名须无前导下划线（S2e：'DEV_Deploy_' 核心形态；'_DEV_…' 起则 indexOf 为 1 不命中）
    var placeholder28 = mobileName28 && String(mobileName28.value).indexOf('DEV_Deploy_') === 0
      && String(mobileName28.value).indexOf('ACME') === -1;
    var hintShown28 = hint28 && !hint28.hidden && (hint28.textContent || '').indexOf('以预检为准') !== -1;
    var backendName28 = document.querySelector('#pipe-ends .endrow[data-end="backend"] [data-f="name"]');
    if (backendName28) setInput(backendName28, 'CUSTOMX-Service');   // dirty 用户改写优先
    clickEl(document.getElementById('btn-preflight'));
    var fill28 = await poll(function () {
      var n = document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="name"]');
      return n && n.value === 'ACME_DEV_Deploy_NEWX-Mobile';
    }, 4000, 50);
    var hintHidden28 = !!document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="namehint"]').hidden;
    var dirtyKept28 = String((document.querySelector('#pipe-ends .endrow[data-end="backend"] [data-f="name"]') || {}).value || '') === 'CUSTOMX-Service';
    record('28', '预检回填流水线名含真实前缀（plan.pipelines 回填；dirty 不覆盖；占位 hint 收起）',
      placeholder28 && hintShown28 && fill28 && hintHidden28 && dirtyKept28,
      'placeholder=' + placeholder28 + ' hintShown=' + hintShown28 + ' fill=' + fill28 +
      ' hintHidden=' + hintHidden28 + ' dirtyKept=' + dirtyKept28);

    /* ── S2e §14 增补判据（29-33，实测三问题修复） ──────────────────────── */

    /* 29. 派生预览无前导下划线（修复一）：反查真名 → to 带 ACME_ 前缀；反查不到 → to=DEV_Deploy_* 兜底 */
    window.__MOCK__.deriveRealName = true;
    setInput(code28, 'NEWX3');
    var derive29a = await poll(function () {
      var n = document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="name"]');
      return n && n.value === 'ACME_DEV_Deploy_NEWX3-Mobile';
    }, 5000, 50);
    var val29a = String((document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="name"]') || {}).value || '');
    window.__MOCK__.deriveRealName = false;
    setInput(code28, 'NEWX4');
    var derive29b = await poll(function () {
      var n = document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="name"]');
      return n && n.value === 'DEV_Deploy_NEWX4-Mobile';
    }, 5000, 50);
    var val29b = String((document.querySelector('#pipe-ends .endrow[data-end="mobile"] [data-f="name"]') || {}).value || '');
    var row29 = document.querySelector('#derive-list input.dr-to[data-kind="name"][data-key="mobile"]');
    var rowVal29 = row29 ? String(row29.value || '') : '';
    record('29', '派生预览无前导下划线（反查真名带 ACME 前缀 / 反查不到兜底 DEV_Deploy_）',
      derive29a && val29a.indexOf('ACME_DEV_Deploy_') === 0 && val29a.charAt(0) !== '_'
        && derive29b && val29b === 'DEV_Deploy_NEWX4-Mobile' && val29b.charAt(0) !== '_'
        && rowVal29 === 'DEV_Deploy_NEWX4-Mobile',
      'real=' + val29a + ' fallback=' + val29b + ' deriveRow=' + rowVal29);

    /* 30. 代码组自动预选（修复二）：代号同名组优先 → 模板代号同名组兜底 */
    setInput(code28, 'NEWX');
    var group30a = await poll(function () {
      return document.getElementById('init-group').value === 'NEWX';
    }, 3000, 50);
    setInput(code28, 'NEWX9');
    var group30b = await poll(function () {
      return document.getElementById('init-group').value === 'TPLX';
    }, 3000, 50);
    record('30', '代码组自动预选（代号同名优先 → 模板代号同名兜底）',
      group30a && group30b,
      'newCodeMatch=' + group30a + ' templateFallback=' + group30b);

    /* 31. 代码组用户改过后不覆盖（修复二）：手动改组选择后代号再变保持用户值 */
    var groupSel31 = document.getElementById('init-group');
    groupSel31.value = 'DEMOGRP';
    groupSel31.dispatchEvent(new Event('change', { bubbles: true }));
    setInput(code28, 'NEWX8');
    await sleep(500);   // 过派生防抖窗口，确认无自动覆盖
    var group31 = String((document.getElementById('init-group') || {}).value || '');
    record('31', '代码组用户改过后不自动覆盖（代号再变保持用户值）',
      group31 === 'DEMOGRP',
      'value=' + group31);

    /* 32. 根目录即检出 → 预检零 git 命令（修复三仿真） */
    window.__MOCK__.localSourceOnly = true;
    clickEl(document.getElementById('btn-preflight'));
    var zeroGit32 = await poll(function () {
      var t = document.getElementById('git-cmds').textContent || '';
      return t.indexOf('无需 git 操作') !== -1 && t.indexOf('git clone') === -1;
    }, 4000, 50);
    var gitText32 = String((document.getElementById('git-cmds') || {}).textContent || '');
    window.__MOCK__.localSourceOnly = false;
    record('32', '根目录即检出 → 预检零 git 命令（「无需 git 操作」提示、无 clone）',
      zeroGit32 && gitText32.indexOf('pt-') === -1,
      'text=' + gitText32.slice(0, 50));

    /* 33. 复用库预检不显示「将改名目录」（修复四）→ 改显结构校验行 */
    window.__MOCK__.reuseAll = true;
    clickEl(document.getElementById('btn-preflight'));
    var reuseReady33 = await poll(function () {
      var k = document.getElementById('plan-dirs-k');
      return k && k.textContent === '结构校验';
    }, 4000, 50);
    var label33 = String((document.getElementById('plan-dirs-k') || {}).textContent || '');
    var dirsText33 = String((document.getElementById('plan-dirs') || {}).textContent || '');
    window.__MOCK__.reuseAll = false;
    record('33', '复用库预检不显示「将改名目录」（改显结构校验行）',
      reuseReady33 && label33 === '结构校验'
        && dirsText33.indexOf('→') === -1
        && (dirsText33.indexOf('待校验') !== -1 || dirsText33.indexOf('存在') !== -1),
      'label=' + label33 + ' dirs=' + dirsText33.slice(0, 50));

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
