#!/usr/bin/env node
/*
  云效 Web 插件 S1 前端断言驱动（任务 10，批次 E）。
  设计稿验证惯例：无头 Edge 注入探针 + document.title 回传 'PROBE::' + JSON。

  用法：
    node LubanAgent.Plugin.YunXiao/tests/assert-s1.js
    （可选环境变量 EDGE_PATH 覆盖 msedge.exe 路径；KEEP_TMP=1 保留临时目录）

  流程：
    1. 复制 web/index.html 到临时目录，改写 assets/ 相对引用为绝对 file:// URL，
       在 </body> 前注入探针 <script>（探针同步装 fetch mock，DOM ready 后跑断言，
       最后 document.title = 'PROBE::' + JSON.stringify({results:[...]})）；
    2. 优先走 CDP（--remote-debugging-port + 原生 WebSocket）读 title 通道，
       并用 Emulation.setDeviceMetricsOverride 把视口精确钉在 826×694
      （--headless=new 下 --window-size 不可靠：实测视口会回落 800×600）；
       CDP 不可用时回退 spec 命令 --headless=new --dump-dom（视口靠
       --window-size 尽力而为，Edge 157 MSIX launcher 还曾丢弃 stdout）；
    3. 逐条打印 PASS/FAIL；任一失败 → 退出码 1。

  断言清单（对应设计 §6.2）：
    1  画板 .frame 826×694（±1）
    2a 屏 1（connect）#content 无纵向溢出、无横向滚动
    2b 屏 2（projects）#content 无纵向溢出、无横向滚动
    3  保存按钮 #btn-save 初始 disabled
    4  PAT：#org-pat type=password 且 localStorage/sessionStorage 无写入
    5  长项目名单行省略（mock fetch 渲染超长名 → computedStyle 检查）
    6  门禁跳转：hasPat=false 时 #/projects 被弹回 #/connect
    7  禁用资源：script[src]/link[href] 无 .mjs/.json/.map/.wasm/.woff(2) 后缀
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

  /* ---- fetch mock：在 bootstrap（DOMContentLoaded）之前同步安装 ---- */
  var LONG_NAME = '超长项目名用于单行省略断言-模块交付流水线自动化'.repeat(8) + '-END';
  window.__PROBE_LONG_NAME__ = LONG_NAME;
  window.fetch = function (url, opts) {
    var u = String(url);
    var envelope;
    if (u.indexOf('/yunxiao/settings') !== -1) {
      /* 夹具一律合成组织值（禁用值零夹具红线） */
      envelope = { success: true, code: 'OK', message: '', data: { organizationId: 'DEMOORG', hasPat: false } };
    } else if (u.indexOf('/yunxiao/projects') !== -1) {
      envelope = { success: true, code: 'OK', message: '', data: {
        total: 3,
        items: [
          { id: 'p1', name: LONG_NAME, identifier: 'extremely-long-project-identifier-0123456789', updateTime: '2026-11-17T08:00:00Z' },
          { id: 'p2', name: '常规项目', identifier: 'demo-app', updateTime: '2026-11-01' },
          { id: 'p3', name: '另一个项目', identifier: 'demo-web', updateTime: '2026-10-15' }
        ]
      }};
    } else {
      envelope = { success: true, code: 'OK', message: '', data: {} };
    }
    return Promise.resolve({
      ok: true,
      status: 200,
      text: function () { return Promise.resolve(JSON.stringify(envelope)); }
    });
  };

  async function run() {
    /* 就绪：bootstrap 完成（组织 ID 回填）且 applyRoute 已把 hash 定在 #/connect */
    var ready = await poll(function () {
      var el = document.getElementById('org-id');
      return el && el.value === 'DEMOORG' && window.location.hash === '#/connect';
    }, 8000, 50);
    if (!ready) record('0', '探针就绪（bootstrap 完成）', false, 'hash=' + window.location.hash);

    /* 1. 画板 826×694 */
    var frame = document.querySelector('.frame');
    var rect = frame ? frame.getBoundingClientRect() : null;
    record('1', '画板 .frame 826×694（±1）',
      !!rect && Math.abs(rect.width - 826) <= 1 && Math.abs(rect.height - 694) <= 1,
      rect
        ? ('w=' + rect.width.toFixed(1) + ' h=' + rect.height.toFixed(1) + ' inner=' + window.innerWidth + 'x' + window.innerHeight)
        : 'no .frame');

    /* 2a. 屏 1 content 无纵向溢出、无横向滚动 */
    var content = document.getElementById('content');
    function overflowDetail() {
      return 'scrollH=' + content.scrollHeight + ' clientH=' + content.clientHeight +
        ' scrollW=' + content.scrollWidth + ' clientW=' + content.clientWidth;
    }
    var connectVisible = !document.getElementById('screen-connect').hidden;
    record('2a', '屏1 connect：content 无纵向溢出/无横向滚动',
      connectVisible && content.scrollHeight <= content.clientHeight + 1 &&
      content.scrollWidth <= content.clientWidth + 1,
      overflowDetail());

    /* 3. 保存按钮门禁：初始 disabled */
    var save = document.getElementById('btn-save');
    record('3', '保存按钮 #btn-save 初始 disabled', !!save && save.disabled === true,
      'disabled=' + (save ? save.disabled : 'missing'));

    /* 4. PAT 安全：type=password + 存储未写入 */
    var pat = document.getElementById('org-pat');
    var storOk = true, storDetail = '';
    try {
      var keys = [];
      for (var i = 0; i < window.localStorage.length; i++) keys.push(window.localStorage.key(i));
      for (var j = 0; j < window.sessionStorage.length; j++) keys.push('session:' + window.sessionStorage.key(j));
      var bad = keys.filter(function (k) { return /pat|token|secret|credential/i.test(k); });
      storOk = keys.length === 0 && bad.length === 0;
      storDetail = 'storageKeys=[' + keys.join(',') + ']';
    } catch (e) {
      storDetail = 'storage-inaccessible:' + e.name + '（未写入即无法读取，视为通过）';
    }
    record('4', 'PAT type=password 且存储无令牌痕迹',
      !!pat && pat.type === 'password' && storOk,
      'type=' + (pat ? pat.type : 'missing') + '; ' + storDetail);

    /* 7. 禁用资源后缀 */
    var nodes = document.querySelectorAll('script[src],link[href]');
    var badRes = [];
    Array.prototype.forEach.call(nodes, function (n) {
      var u = n.getAttribute('src') || n.getAttribute('href') || '';
      if (/\.(mjs|json|map|wasm|woff2?)(\?|#|$)/i.test(u)) badRes.push(u);
    });
    record('7', '禁用资源（mjs/json/map/wasm/woff）', badRes.length === 0,
      badRes.length ? ('bad: ' + badRes.join(', ')) : ('checked=' + nodes.length));

    /* 6. 门禁跳转：hasPat=false → #/projects 弹回 #/connect */
    window.location.hash = '#/projects';
    var bounced = await poll(function () { return window.location.hash === '#/connect'; }, 2000, 30);
    var sc = document.getElementById('screen-connect');
    var sp = document.getElementById('screen-projects');
    record('6', '门禁跳转：#/projects 弹回 #/connect',
      bounced && !sc.hidden && sp.hidden,
      'hash=' + window.location.hash + ' connectHidden=' + sc.hidden + ' projectsHidden=' + sp.hidden);

    /* 5. 长项目名单行省略：解锁门禁 → 进 projects 渲染超长名 */
    window.YunxiaoApp.setHasPat(true);
    window.YunxiaoApp.navigate('#/projects');
    var rendered = await poll(function () {
      return !!document.querySelector('#proj-list .proj-item-name');
    }, 8000, 50);
    var nameEl = document.querySelector('#proj-list .proj-item-name');
    var cs = nameEl ? window.getComputedStyle(nameEl) : null;
    var trunc = nameEl ? (nameEl.scrollWidth > nameEl.clientWidth) : false;
    record('5', '长项目名单行省略（ellipsis+nowrap）',
      rendered && !!cs && cs.textOverflow === 'ellipsis' && cs.whiteSpace === 'nowrap',
      cs
        ? ('textOverflow=' + cs.textOverflow + ' whiteSpace=' + cs.whiteSpace +
           ' truncated=' + trunc + ' titleFull=' + (nameEl.getAttribute('title') === LONG_NAME))
        : 'no .proj-item-name');

    /* 2b. 屏 2 content 无纵向溢出、无横向滚动（列表已渲染） */
    record('2b', '屏2 projects：content 无纵向溢出/无横向滚动',
      content.scrollHeight <= content.clientHeight + 1 &&
      content.scrollWidth <= content.clientWidth + 1,
      overflowDetail());

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
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'yunxiao-s1-assert-'));
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
  const udd = path.join(os.tmpdir(), 'yunxiao-s1-dump-udd-' + Date.now());
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
  const udd = path.join(os.tmpdir(), 'yunxiao-s1-cdp-udd-' + Date.now() + '-' + process.pid);
  const port = 9200 + Math.floor(Math.random() * 700);
  const fileUrl = pathToFileURL(htmlFile).href;
  console.log('[channel] 回退 CDP: headless + --remote-debugging-port=' + port);

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
      const deadline = Date.now() + 30000;
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
  console.log('---- S1 前端断言结果 ----');
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
  console.error('断言驱动失败（不视为通过）。可按 tests/assert-s1.js 头部清单人工核对。');
  process.exit(2);
});
