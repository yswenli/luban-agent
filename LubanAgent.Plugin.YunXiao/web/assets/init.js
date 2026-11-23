/*
  云效 DevOps 工作台 · 屏 3 项目初始化（S2 批次 E：F5 代码库区 + F6 流水线区）。

  交互骨架：
  1. 进屏 GET /yunxiao/manifests：模板库下拉（自定义模板真值，name+description 展示，长文本省略+title；
     总数=1 锁定单项；>1 多选，每勾一个=复制一个库）+ 代码组 / 工作区 / 分支（默认 master）；
  2. 新项目代号变更即重算三段派生预览（纯函数 deriveNaming / renameByCode，与后端 NamingDeriver 同口径：
     代号前缀替换 TPLX-Mixed→NEWX-Mixed、TPLX-Service→NEWX-Service；子目录行仅综合库（-Mixed）显示），
     派生行 / F6 端行输入均可单独改写，改后以改写值为准（dirty 标记，代号再变不覆盖手改值）；
     母版选择 / 代号 / 端口变更后防抖自动调 GET /yunxiao/pipelinederive（S2e §14）刷新「命名派生预览」
     与 F6 流水线名（dirty > synced > 预检 > 派生 优先级，手改值不覆盖；反查真名落地后默认名无前导下划线）；
     代码组自动预选（代号同名 → 模板代号同名 → 不挂组；用户改过后不覆盖，纯函数 matchGroupChoice）；
  3. 预检 POST /yunxiao/preflight（零副作用）→ 只读清单 + 每库徽标（新建/复用，复用提示「将复用已有库」）
     + git 命令序列默认展开（只渲染后端脱敏文本，不再拼接任何凭据；全复用时显示「无需 git 操作（全部复用）」）
     + 「确认执行」；门禁：未预检 / 预检失败 / 参数已变 / 需新建库时 sessionReady=false / 有冲突 → 禁用并明示原因；
  4. 执行 POST /yunxiao/initexec 秒回 taskId → 2s 轮询 GET /yunxiao/inittask 渲染逐库状态
     （失败行「重跑」POST /yunxiao/initretry 后继续轮询）；任务终态停轮询；
  5. F6 右列依赖门禁：FR-2 任务未达 done 前置灰「先完成代码库复制」；完成后自动
     POST /yunxiao/pipelinepreview（可「预览差异」重取）→ 逐端 diff 切换预览（块内横向滚动），
     每端标注「已存在（提交时跳过）/缺失（将新建）」（preview.exists）；
  6. 提交 POST /yunxiao/pipelinecommit：create 默认补全语义（skipIfExisting，服务端命中「已存在跳过」）；
     「覆盖已有流水线」勾选 → update+confirm:true；未勾的 update 行（已有流水线 id）提交前卡内确认；
     逐端结果行内展示，一端失败不影响他端。

  请求/响应字段口径以服务模型为准（YunxiaoInitService.cs / YunxiaoContracts.cs，camelCase）：
  PreflightRequest{templateRepoName,newCode,workspaceId,branch,templatePipelineNames,
  groupNewName?,ends:[{end,on,name,dir,port?}]}；ExecRequest{libs:[{templateRepoName,
  newRepoName?}],newCode,workspaceId,branch,groupNewName?}；PipelinePreviewRequest{newRepoId,
  newRepoName,templateCode,newCode,ends:[{end,enabled,buildDir?,pipelineName?,port?,
  templatePipelineName?}]}；PipelineCommitRequest{items:[{end,mode,name,content,confirm?,
  pipelineId?,skipIfExisting?}]}。

  云效/用户数据渲染一律 escapeHtml；无 ES module；经典脚本在 projects.js 之后加载。
*/
(function (global) {
  'use strict';

  const api = global.YunxiaoApi;
  const app = global.YunxiaoApp;

  /* ---------- 命名派生纯函数（与 LubanAgentWebHost.Services.Init.NamingDeriver 同口径，S2b §11.1） ---------- */

  const TEMPLATE_REPO_SUFFIX = '-Mixed';
  const PIPELINE_MARKER = '_DEV_Deploy_';
  /** 标记核心段（空前缀形态用「DEV_Deploy_」，不留前导下划线）。 */
  const PIPELINE_CORE = 'DEV_Deploy_';
  const CODE_PATTERN = /^[A-Za-z][A-Za-z0-9]*$/;

  /** 模板名 → 模板代号（首个 '-' 之前的段："TPLX-Mixed"/"TPLX-Service" → "TPLX"；无 '-' 或空白返回 null）。 */
  function parseTemplateCode(repoName) {
    const name = String(repoName || '');
    if (!name) return null;
    const index = name.indexOf('-');
    if (index < 0) return null;
    const code = name.slice(0, index);
    return code || null;
  }

  /**
   * 代号前缀替换改名（"TPLX-Mixed" → "NEWX-Mixed"、"TPLX-Service" → "NEWX-Service"）；
   * 名称不以 {templateCode}- 开头抛错（与 NamingDeriver.RenameByCode 同口径）。
   */
  function renameByCode(name, templateCode, newCode) {
    const text = String(name || '');
    const tCode = String(templateCode || '');
    const nCode = String(newCode || '');
    if (!CODE_PATTERN.test(tCode)) throw new Error('母版代号不合法');
    if (!CODE_PATTERN.test(nCode)) throw new Error('新项目代号必须为字母开头的字母数字组合');
    const prefix = tCode + '-';
    if (!text || text.indexOf(prefix) !== 0 || text.length === prefix.length) {
      throw new Error('名称不以 ' + prefix + ' 开头，无法按代号前缀替换');
    }
    return nCode + text.slice(tCode.length);
  }

  /** 母版流水线名 → 分组前缀（「DEV_Deploy_」核心段之前、去尾下划线；不含该段返回 null）。
      容忍三种形态：ACME_DEV_… → 'ACME'；_DEV_… → ''；DEV_… → ''。 */
  function parsePipelinePrefix(name) {
    const text = stripPortSuffix(String(name || ''));
    if (!text) return null;
    const index = text.indexOf(PIPELINE_CORE);
    return index < 0 ? null : text.slice(0, index).replace(/_+$/, '');
  }

  /** 剥离流水线名尾部 (端口) 后缀（S2c §12.3：容忍端口差异匹配）。 */
  function stripPortSuffix(name) {
    const text = String(name || '');
    return text.replace(/\([^()]*\)\s*$/, '');
  }

  /** 从流水线名解析尾部 (数字) 端口（S2c §12.3 反向回填）；解析不出返回 null。 */
  function parsePortSuffix(name) {
    const m = /\((\d+)\)\s*$/.exec(String(name || ''));
    return m ? parseInt(m[1], 10) : null;
  }

  /**
   * 三段派生（默认值，可逐段改写）：仓库名走代号前缀替换（综合库 {newCode}-Mixed）；
   * 子目录 -Admin/-Mobile/-Service 仅 includeDirs（综合库）时派生；
   * 流水线 只替换代号段（prefix + _DEV_Deploy_ + newCode + 端后缀）。与 NamingDeriver.Derive 一致。
   */
  function deriveNaming(templateCode, newCode, templatePipelineNames, includeDirs) {
    const tCode = String(templateCode || '');
    const nCode = String(newCode || '');
    if (!CODE_PATTERN.test(tCode)) throw new Error('母版代号不合法');
    if (!CODE_PATTERN.test(nCode)) throw new Error('新项目代号必须为字母开头的字母数字组合');

    const dirs = includeDirs === false ? [] : [
      { from: tCode + '-Admin', to: nCode + '-Admin' },
      { from: tCode + '-Mobile', to: nCode + '-Mobile' },
      { from: tCode + '-Service', to: nCode + '-Service' }
    ];

    const pipelines = [];
    (templatePipelineNames || []).forEach(function (name) {
      const text = String(name || '');
      const prefix = parsePipelinePrefix(text);
      if (prefix === null) return;
      const coreIndex = text.indexOf(PIPELINE_CORE);
      const remainder = text.slice(coreIndex + PIPELINE_CORE.length);
      const dashIndex = remainder.indexOf('-');
      const endSuffix = dashIndex >= 0 ? remainder.slice(dashIndex) : '';
      const head = prefix ? prefix + PIPELINE_MARKER : PIPELINE_CORE;
      pipelines.push({ from: text, to: head + nCode + endSuffix });
    });

    return { repoName: renameByCode(tCode + TEMPLATE_REPO_SUFFIX, tCode, nCode), dirs: dirs, pipelines: pipelines };
  }

  /* ---------- 状态映射（wire 可能是字符串枚举或数字枚举，双向兼容） ---------- */

  const TASK_STATUS_TEXT = { pending: '待开始', running: '进行中', done: '已完成', failed: '失败' };
  const LIB_STATUS_TEXT = {
    pending: '待开始', cloning: '克隆中', renaming: '改名中',
    pushing: '推送中', done: '已完成', failed: '失败'
  };

  function normTaskStatus(value) {
    const key = typeof value === 'number'
      ? ['pending', 'running', 'done', 'failed'][value]
      : String(value === undefined || value === null ? '' : value).toLowerCase();
    return key === 'running' || key === 'done' || key === 'failed' ? key : 'pending';
  }

  function normLibStatus(value) {
    const key = typeof value === 'number'
      ? ['pending', 'cloning', 'renaming', 'pushing', 'done', 'failed'][value]
      : String(value === undefined || value === null ? '' : value).toLowerCase();
    return key === 'cloning' || key === 'renaming' || key === 'pushing' || key === 'done' || key === 'failed'
      ? key : 'pending';
  }

  function statusClass(key) {
    if (key === 'done') return 'st st-ok';
    if (key === 'failed') return 'st st-err';
    if (key === 'running' || key === 'cloning' || key === 'renaming' || key === 'pushing') return 'st st-run';
    return 'st';
  }

  /* ---------- 三端固定配置 ---------- */

  const ENDS = [
    { end: 'mobile', role: '移动端', suffix: 'Mobile' },
    { end: 'admin', role: '管理后台', suffix: 'Admin' },
    { end: 'backend', role: '后端', suffix: 'Service' }
  ];

  function makeEnd(spec) {
    return {
      end: spec.end,
      role: spec.role,
      suffix: spec.suffix,
      enabled: true,
      dir: '',
      dirDirty: false,
      name: '',
      nameDirty: false,
      nameFromPlan: false,   // S2d §13.3：预检回填的服务端权威名（绑定代号组合，参数一变失效）
      derivedFrom: '',       // S2e §14：派生接口反查到的母版真实流水线名（From，展示用）
      tplName: '',
      port: 8080,
      overwrite: false,
      confirmPending: false,
      synced: false,
      result: null
    };
  }

  /* ---------- 共享状态 ---------- */

  const state = {
    reposLoaded: false,
    reposLoading: false,
    repos: [],
    groups: [],
    workspaces: [],
    sessionReady: false,
    selectedRepos: [],
    repoNewNames: {},
    repoDirty: {},
    popOpen: false,
    newCode: '',
    templateCode: '',
    groupChoice: '',        // '' = 不挂组；'__new__' = 新建；其余 = 既有组 name
    groupAuto: '',          // S2e §14 修复二：当前自动预选值（= groupChoice 时可被重匹配）
    groupDirty: false,      // S2e §14 修复二：用户手动改过组选择（改后不再自动覆盖）
    groupNewName: '',
    workspaceId: '',
    wsCreating: false,      // 下拉里选中「新建工作区」后为 true：展开手输区，workspaceId 置空
    branch: 'master',
    ends: ENDS.map(makeEnd),
    preflight: null,
    preflightStale: false,
    preflightError: '',
    taskId: '',
    task: null,
    pollTimer: 0,
    autoPreviewed: false,
    preview: {},            // end -> { name, diff, warning, content }
    activeDiffEnd: '',
    pipelineIds: {},        // end -> 已提交流水线 id（覆盖用）
    forceUpdate: {},        // end -> true（卡内确认后按覆盖提交）
    // S2d §13.1：工作区可搜索组合框 + 迷你目录浏览器（数据仅内存渲染不落盘）
    wsComboOpen: false,
    wsQuery: '',
    wsBrowseOpen: false,
    wsBrowsePath: '',
    wsBrowseDirs: [],
    wsBrowseLoading: false,
    wsBrowseError: '',
    planNamesKey: '',       // S2d §13.3：预检回填名绑定的 (templateCode,newCode) 组合
    // 项目上下文（#/init?projectId=&identifier=&name=）：初始化完成后登记回写，列表据此标注「已初始化」
    project: { id: '', identifier: '', name: '' },
    initSaved: false,       // 本次初始化完成登记是否已落盘（防重复提交）
    restored: false,        // 已初始化项目：本次进入已反显完成登记
    restoreKey: ''          // 反显归属键（projectId/identifier），换项目时重置
  };

  let els = null;
  let endRefs = {};

  function escapeHtml(value) {
    return String(value === undefined || value === null ? '' : value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function el(id) {
    return document.getElementById(id);
  }

  /* ---------- 派生值计算（非 dirty 才覆盖） ---------- */

  /** 模板是否综合库形态（-Mixed 结尾；才有三子目录改名段）。 */
  function isMixedTemplate(name) {
    const text = String(name || '');
    return text.slice(-TEMPLATE_REPO_SUFFIX.length) === TEMPLATE_REPO_SUFFIX;
  }

  /** 代码库派生名（代号前缀替换：TPLX-Mixed→NEWX-Mixed、TPLX-Service→NEWX-Service）。 */
  function derivedRepoNameOf(name) {
    const text = String(name || '');
    const code = parseTemplateCode(text);
    if (!code || !state.newCode) return '';
    if (!CODE_PATTERN.test(code) || !CODE_PATTERN.test(state.newCode)) return '';
    return renameByCode(text, code, state.newCode);
  }

  function repoTo(repoName) {
    return state.repoDirty[repoName] ? (state.repoNewNames[repoName] || '') : derivedRepoNameOf(repoName);
  }

  function derivedDir(endRec) {
    return state.newCode ? state.newCode + '-' + endRec.suffix : '';
  }

  /** 端流水线名本地占位派生源：所选母版流水线名，缺省用默认模板名（核心段形态，无前导下划线）。
      S2e §14：刻意不含派生接口反查名（derivedFrom）——占位保持「以预检为准」语义（断言 28）。 */
  function localTemplateSourceOf(endRec) {
    const custom = String(endRec.tplName || '').trim();
    if (custom) return custom;
    return state.templateCode ? PIPELINE_CORE + state.templateCode + '-' + endRec.suffix : '';
  }

  /** 端流水线名派生源（展示用）：手选母版名 > 派生接口反查真名 > 默认标记形态。 */
  function templateNameOf(endRec) {
    const custom = String(endRec.tplName || '').trim();
    if (custom) return custom;
    if (endRec.derivedFrom) return endRec.derivedFrom;
    return state.templateCode ? PIPELINE_CORE + state.templateCode + '-' + endRec.suffix : '';
  }

  function derivedName(endRec) {
    const source = localTemplateSourceOf(endRec);
    if (!source || !state.newCode) return '';
    const prefix = parsePipelinePrefix(source);
    if (prefix === null) return '';
    const coreIndex = source.indexOf(PIPELINE_CORE);
    const remainder = source.slice(coreIndex + PIPELINE_CORE.length);
    const dashIndex = remainder.indexOf('-');
    // 尾部 (端口) 剥离后代号替换；后端端按端口盒值重加 (端口) 后缀（S2c §12.3，与 NamingDeriver 同口径）
    let endSuffix = dashIndex >= 0 ? remainder.slice(dashIndex) : '';
    endSuffix = stripPortSuffix(endSuffix);
    if (endRec.end === 'backend' && endRec.port !== '' && endRec.port !== undefined && endRec.port !== null) {
      endSuffix = endSuffix + '(' + endRec.port + ')';
    }
    const head = prefix ? prefix + PIPELINE_MARKER : PIPELINE_CORE;
    return head + state.newCode + endSuffix;
  }

  function applyDerivation() {
    const first = state.selectedRepos[0] || '';
    state.templateCode = parseTemplateCode(first) || '';
    // S2d §13.3：预检回填的权威名绑定 (templateCode,newCode) 组合，参数一变即失效回本地占位派生
    const key = state.templateCode + '|' + state.newCode;
    if (state.planNamesKey !== key) {
      state.planNamesKey = key;
      state.ends.forEach(function (e) { e.nameFromPlan = false; });
    }
    state.ends.forEach(function (e) {
      if (!e.dirDirty) e.dir = derivedDir(e);
      // synced（S2c §12.3 反向回填「已同步」态）与 nameFromPlan（S2d §13.3 预检权威名）
      // 以现实/预检为准，不被本地派生覆盖
      if (!e.nameDirty && !e.synced && !e.nameFromPlan) e.name = derivedName(e);
    });
    // S2e §14 修复二：manifests 加载 / 母版选择 / 代号变更后自动预选代码组
    autoSelectGroup();
  }

  /* ---------- S2e §14 修复二：代码组自动预选（代号同名优先，纯函数可测） ---------- */

  /**
   * 代码组自动预选匹配（纯函数）：与新项目代号同名的组（大小写不敏感精确名）→ 命中；
   * 无 → 与模板代号同名的组；再无 → 不挂组（''）。
   */
  function matchGroupChoice(groups, newCode, templateCode) {
    const list = groups || [];
    function find(code) {
      const c = String(code === undefined || code === null ? '' : code).trim();
      if (!c) return null;
      for (let i = 0; i < list.length; i++) {
        const name = String((list[i] && list[i].name) || '');
        if (name.toLowerCase() === c.toLowerCase()) return name;
      }
      return null;
    }
    return find(newCode) || find(templateCode) || '';
  }

  /** 自动预选代码组：用户改过且当前选择非自动值时不覆盖；否则重匹配（可被代号变更重新触发）。 */
  function autoSelectGroup() {
    if (state.groupDirty && state.groupChoice !== state.groupAuto) return;
    const match = matchGroupChoice(state.groups, state.newCode, state.templateCode);
    state.groupAuto = match;
    if (state.groupChoice !== match) {
      state.groupChoice = match;
      if (els && els.group) renderGroupSelect();
    }
  }

  /* ---------- S2e §14 修复一：流水线派生接口防抖自动刷新 ---------- */

  let deriveTimer = 0;
  const DERIVE_DEBOUNCE_MS = 300;

  /** 母版选择 / 代号 / 端口变更后防抖刷新派生（防抖窗口内多发并一）。 */
  function scheduleDeriveRefresh() {
    if (deriveTimer) global.clearTimeout(deriveTimer);
    deriveTimer = global.setTimeout(function () {
      deriveTimer = 0;
      doDerive();
    }, DERIVE_DEBOUNCE_MS);
  }

  function buildDeriveQuery() {
    return {
      templateRepoName: state.selectedRepos[0] || '',
      newCode: state.newCode,
      ends: JSON.stringify(state.ends.map(function (e) {
        const item = { end: e.end, on: !!e.enabled };
        if (e.end === 'backend') {
          const port = parseInt(e.port, 10);
          if (!isNaN(port)) item.port = port;
        }
        return item;
      }))
    };
  }

  /** 调派生接口刷新「命名派生预览」与 F6 流水线名（失败静默——本地占位派生仍在，不打断输入）。 */
  async function doDerive() {
    if (!state.selectedRepos.length || !CODE_PATTERN.test(state.newCode) || !state.templateCode) return;
    try {
      const data = await api.get('/yunxiao/pipelinederive', buildDeriveQuery());
      applyDeriveResult(data);
    } catch (e) {
      /* 派生刷新失败静默 */
    }
  }

  /**
   * 派生结果落地（S2d 优先级 dirty > synced > 预检 > 派生）：
   * 仅最低优先级（非 dirty / 非 synced / 非 nameFromPlan）的流水线名被派生值覆盖；
   * 反查真名（From）落地后默认名不再带前导下划线（To 由服务端 DerivePipelineName 保证）。
   */
  function applyDeriveResult(data) {
    const items = (data && data.items) || [];
    if (!items.length) return;
    let changed = false;
    items.forEach(function (item) {
      const rec = findEnd(item.end);
      if (!rec) return;
      if (item.from) rec.derivedFrom = String(item.from);
      const to = String(item.to || '');
      if (to && !rec.nameDirty && !rec.synced && !rec.nameFromPlan && rec.name !== to) {
        rec.name = to;
        changed = true;
      }
    });
    renderDerive();
    syncEnds();
  }

  function markStale() {
    if (state.preflight) {
      state.preflightStale = true;
      updateExecGate();
    }
  }

  /* ---------- 清单加载 ---------- */

  async function loadManifests() {
    if (state.reposLoading) return;
    state.reposLoading = true;
    els.tplCount.textContent = '母版库 加载中…';
    try {
      const data = await api.get('/yunxiao/manifests');
      state.repos = (data && data.repos) || [];
      state.groups = (data && data.groups) || [];
      state.workspaces = (data && data.workspaces) || [];
      state.sessionReady = !!(data && data.sessionReady);
      state.reposLoaded = true;

      // 默认勾选第一个母版库（多选时；总数=1 由渲染层锁定单项）
      if (!state.selectedRepos.length && state.repos.length) {
        state.selectedRepos = [state.repos[0].name];
      }
      if (!state.workspaceId && state.workspaces.length) {
        state.workspaceId = state.workspaces[0].id;
      }
      applyDerivation();
      renderAll();
    } catch (e) {
      state.reposLoaded = true;
      app.showError(e && e.message ? e.message : '读取初始化清单失败');
      renderAll();
    } finally {
      state.reposLoading = false;
    }
  }

  /* ---------- F5：母版库下拉 ---------- */

  function renderTplSel() {
    const total = state.repos.length;
    els.tplCount.textContent = total === 1
      ? '母版库 1 个 · 锁定'
      : '母版库 ' + total + ' 个 · 已选 ' + state.selectedRepos.length;

    if (!total) {
      els.tplSel.innerHTML = '<p class="field-help">'
        + (state.reposLoaded ? '无可用模板库。' : '加载中…')
        + '</p>';
      return;
    }

    if (total === 1) {
      const repo = state.repos[0];
      const name = repo.name || '';
      const hint = repo.description || '';
      els.tplSel.innerHTML = '<div class="msel locked">'
        + '<div class="msel-h"><span class="v" title="' + escapeHtml(name) + '">'
        + escapeHtml(name) + '</span>'
        + (hint
          ? '<span class="mhint" title="' + escapeHtml(hint) + '">' + escapeHtml(hint) + '</span>'
          : '')
        + '</div></div>';
      return;
    }

    const sel = state.selectedRepos;
    const summary = sel.length
      ? sel.map(function (n) { return n; }).join('、')
      : '未选择母版库';
    let html = '<div class="msel">'
      + '<button class="msel-h" type="button" data-act="toggle">'
      + '<span class="v" title="' + escapeHtml(summary) + '">' + escapeHtml(summary) + '</span>'
      + '<span class="n">已选 ' + sel.length + ' / ' + total + '</span></button>';
    if (state.popOpen) {
      html += '<div class="msel-pop">';
      state.repos.forEach(function (repo) {
        const name = repo.name || '';
        const hint = repo.description || '';
        const on = sel.indexOf(name) >= 0;
        html += '<div class="mopt' + (on ? ' on' : '') + '" data-repo="' + escapeHtml(name) + '">'
          + '<span class="mck"></span>'
          + '<span class="mopt-main">'
          + '<span class="mname" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>'
          + '<span class="mhint" title="' + escapeHtml(hint) + '">' + escapeHtml(hint || '—') + '</span>'
          + '</span></div>';
      });
      html += '</div>';
    }
    html += '</div>';
    els.tplSel.innerHTML = html;
  }

  /* ---------- F5：命名派生预览（三段，逐行可改写） ---------- */

  function renderDerive() {
    const newCode = state.newCode;
    const templateCode = state.templateCode;
    els.deriveTag.textContent = newCode
      ? '代号 ' + newCode + (templateCode ? ' 替换 ' + templateCode : '')
      : '未填代号';

    if (!newCode) {
      els.deriveList.innerHTML = '<p class="field-help">填入新项目代号后显示三段派生对照，每行可单独改写。</p>';
      return;
    }

    let html = '';
    // 代码库：每个勾选的母版库一行
    html += '<div class="dr-seg">代码库</div>';
    state.selectedRepos.forEach(function (name) {
      html += '<div class="dr">'
        + '<span class="dr-k">库</span>'
        + '<span class="dr-from" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>'
        + '<i class="dr-ar">→</i>'
        + '<input class="field-input dr-to" data-kind="repo" data-key="' + escapeHtml(name) + '" '
        + 'value="' + escapeHtml(repoTo(name)) + '" spellcheck="false" />'
        + '</div>';
    });
    if (!state.selectedRepos.length) {
      html += '<p class="field-help">请先勾选母版代码库。</p>';
    }

    // 子目录：仅综合库（模板名 -Mixed 结尾）派生三行（Admin / Mobile / Service）
    html += '<div class="dr-seg">子目录</div>';
    if (!templateCode) {
      html += '<p class="field-help">模板名须含代号段（首个 - 之前为模板代号）。</p>';
    } else if (isMixedTemplate(state.selectedRepos[0] || '')) {
      state.ends.forEach(function (e) {
        html += '<div class="dr">'
          + '<span class="dr-k">目录</span>'
          + '<span class="dr-from" title="' + escapeHtml(templateCode + '-' + e.suffix) + '">'
          + escapeHtml(templateCode + '-' + e.suffix) + '</span>'
          + '<i class="dr-ar">→</i>'
          + '<input class="field-input dr-to" data-kind="dir" data-key="' + escapeHtml(e.end) + '" '
          + 'value="' + escapeHtml(e.dir) + '" spellcheck="false" />'
          + '</div>';
      });
    } else {
      html += '<p class="field-help">单端模板无子目录改名段。</p>';
    }

    // 流水线：三行（来源=所选母版流水线名 / 默认模板名）
    html += '<div class="dr-seg">流水线</div>';
    state.ends.forEach(function (e) {
      const source = templateNameOf(e) || '—';
      html += '<div class="dr">'
        + '<span class="dr-k">流水线</span>'
        + '<span class="dr-from" title="' + escapeHtml(source) + '">' + escapeHtml(source) + '</span>'
        + '<i class="dr-ar">→</i>'
        + '<input class="field-input dr-to" data-kind="name" data-key="' + escapeHtml(e.end) + '" '
        + 'value="' + escapeHtml(e.name) + '" spellcheck="false" />'
        + '</div>';
    });

    els.deriveList.innerHTML = html;
  }

  /* ---------- F5：共用设置 ---------- */

  function renderGroupSelect() {
    let html = '<option value="">不挂组</option>';
    state.groups.forEach(function (g) {
      const name = g.name || '';
      html += '<option value="' + escapeHtml(name) + '"' + (state.groupChoice === name ? ' selected' : '') + '>'
        + escapeHtml(name) + '</option>';
    });
    html += '<option value="__new__"' + (state.groupChoice === '__new__' ? ' selected' : '') + '>新建代码组</option>';
    els.group.innerHTML = html;
    els.group.value = state.groupChoice;
    els.groupNew.hidden = state.groupChoice !== '__new__';
  }

  /* ---------- F5：工作区可搜索组合框（S2d §13.1） ---------- */

  /** 工作区显示名：rootPath 缺省时退 id（列表项与触发器共用）。 */
  function wsLabel(w) {
    return String((w && w.rootPath) || (w && w.id) || '');
  }

  /** 搜索过滤（rootPath / 名称子串，大小写不敏感）；名称取 rootPath 尾段。 */
  function wsMatches(w, q) {
    if (!q) return true;
    const p = wsLabel(w).toLowerCase();
    const segs = p.split(/[\\/]/).filter(Boolean);
    const name = segs.length ? segs[segs.length - 1] : '';
    return p.indexOf(q) >= 0 || name.indexOf(q) >= 0;
  }

  function renderWorkspaceSelect() {
    // 值承载（hidden #init-workspace）绑定 workspaceId —— 原下拉值语义
    const current = state.workspaces.filter(function (w) {
      return String(w.id === undefined || w.id === null ? '' : w.id) === String(state.workspaceId);
    })[0];
    const text = state.wsCreating
      ? '新建工作区'
      : (current ? wsLabel(current) : (state.workspaces.length ? '请选择工作区' : '无可用工作区'));
    if (els.wsTriggerText) els.wsTriggerText.textContent = text;
    if (els.wsTrigger) els.wsTrigger.title = text;
    if (els.workspace) els.workspace.value = state.workspaceId;
    // 手输区跟随新建态：默认收起，下拉选「新建工作区」或浏览目录选此目录后才展开
    if (els.wsNew) els.wsNew.hidden = !state.wsCreating;
    renderWsComboList();
  }

  /** 展开/收起手输新建区；展开时把焦点交给路径输入框。 */
  function setWsNewMode(on) {
    state.wsCreating = !!on;
    if (els.wsNew) els.wsNew.hidden = !state.wsCreating;
    // 重新进入新建态时清掉上一轮的结果提示，避免「已新建工作区」误导
    if (state.wsCreating && els.wsNewResult) {
      els.wsNewResult.hidden = true;
      els.wsNewResult.textContent = '';
    }
    if (state.wsCreating && els.wsNewPath) els.wsNewPath.focus();
  }

  /** 进入新建态：先解除已选工作区，避免用户忘点「新建并使用」时预检沿用旧值。 */
  function enterWsNewMode() {
    state.workspaceId = '';
    if (els.workspace) {
      els.workspace.value = '';
      els.workspace.dispatchEvent(new Event('change'));
    }
    setWsNewMode(true);
    renderWorkspaceSelect();
  }

  function renderWsComboList() {
    const newOpt = '<div class="combo-opt combo-opt-new" data-wsnew="1">＋ 新建工作区</div>';
    if (!els.wsList) return;
    const q = String(state.wsQuery || '').trim().toLowerCase();
    if (!state.workspaces.length) {
      els.wsList.innerHTML = '<p class="combo-empty">无可用工作区</p>' + newOpt;
      return;
    }
    const rows = state.workspaces.filter(function (w) { return wsMatches(w, q); });
    if (!rows.length) {
      els.wsList.innerHTML = '<p class="combo-empty">无匹配</p>' + newOpt;
      return;
    }
    els.wsList.innerHTML = rows.map(function (w) {
      const id = w.id === undefined || w.id === null ? '' : String(w.id);
      const label = wsLabel(w);
      return '<div class="combo-opt' + (String(state.workspaceId) === id ? ' on' : '') + '" '
        + 'data-wsid="' + escapeHtml(id) + '" title="' + escapeHtml(label) + '">'
        + escapeHtml(label) + '</div>';
    }).join('') + newOpt;
  }

  function setWsComboOpen(open) {
    state.wsComboOpen = !!open;
    if (els.wsPop) els.wsPop.hidden = !state.wsComboOpen;
    if (state.wsComboOpen) {
      renderWsComboList();
      renderWsBrowser();
      if (els.wsSearch) els.wsSearch.focus();
    } else {
      // 收起弹层一并收起目录浏览器（数据仅内存，收起即弃）
      state.wsBrowseOpen = false;
    }
  }

  /* ---- 迷你目录浏览器（GET /workspace/dirs，只读单层，数据仅内存渲染） ---- */

  /** 面包屑段（可点回退）：计算机 → 盘符/根 → 逐级子段。 */
  function dirCrumbs(p) {
    const crumbs = [{ label: '计算机', path: '' }];
    const text = String(p || '');
    if (!text) return crumbs;
    const drive = /^([A-Za-z]:)[\\/]?/.exec(text);
    if (drive) {
      crumbs.push({ label: drive[1] + '\\', path: drive[1] + '\\' });
      let acc = drive[1] + '\\';
      text.slice(drive[0].length).split(/[\\/]/).filter(Boolean).forEach(function (seg) {
        acc = (acc.charAt(acc.length - 1) === '\\' ? acc : acc + '\\') + seg;
        crumbs.push({ label: seg, path: acc });
      });
      return crumbs;
    }
    let acc = '';
    text.split(/[\\/]/).filter(Boolean).forEach(function (seg) {
      acc += '/' + seg;
      crumbs.push({ label: seg, path: acc });
    });
    return crumbs;
  }

  function renderWsBrowser() {
    if (!els.wsDirBrowser) return;
    els.wsDirBrowser.hidden = !state.wsBrowseOpen;
    if (!state.wsBrowseOpen) return;

    const crumbs = dirCrumbs(state.wsBrowsePath);
    els.wsDirCrumb.innerHTML = crumbs.map(function (c, i) {
      const last = i === crumbs.length - 1;
      return (i > 0 ? '<span class="crumb-slash">/</span>' : '')
        + '<span class="' + (last ? 'crumb-cur' : 'crumb-seg') + '" data-path="' + escapeHtml(c.path) + '">'
        + escapeHtml(c.label) + '</span>';
    }).join('');

    if (state.wsBrowseLoading) {
      els.wsDirList.innerHTML = '<p class="combo-empty">加载中…</p>';
    } else if (!state.wsBrowseDirs.length) {
      els.wsDirList.innerHTML = '<p class="combo-empty">无子目录</p>';
    } else {
      // 仅子目录行（端点只列目录；路径文本一律 escapeHtml）
      els.wsDirList.innerHTML = state.wsBrowseDirs.map(function (d) {
        return '<div class="dir-row" data-path="' + escapeHtml(d.path || '') + '" '
          + 'title="' + escapeHtml(d.path || '') + '">' + escapeHtml(d.name || d.path || '') + '</div>';
      }).join('');
    }

    if (els.wsDirChoose) els.wsDirChoose.disabled = !state.wsBrowsePath;
    if (els.wsDirError) {
      els.wsDirError.hidden = !state.wsBrowseError;
      els.wsDirError.textContent = state.wsBrowseError || '';
    }
  }

  async function loadWsDirs(path) {
    state.wsBrowseLoading = true;
    state.wsBrowseError = '';
    renderWsBrowser();
    try {
      const data = await api.get('/workspace/dirs', path ? { path: path } : undefined);
      state.wsBrowseDirs = (data && data.dirs) || [];
      state.wsBrowsePath = path || '';
    } catch (e) {
      state.wsBrowseDirs = [];
      state.wsBrowseError = e && e.message ? e.message : '读取目录失败';
    } finally {
      state.wsBrowseLoading = false;
      renderWsBrowser();
    }
  }

  function openWsBrowser() {
    state.wsBrowseOpen = true;
    renderWsBrowser();
    loadWsDirs(state.wsBrowsePath || '');
  }

  /** 当前生效的代码组名（不挂组 = 空串）。 */
  function activeGroupName() {
    if (state.groupChoice === '__new__') return state.groupNewName.trim();
    return state.groupChoice;
  }

  /* ---------- F5：预检 ---------- */

  function validatePreflight() {
    if (!state.selectedRepos.length) return '请先勾选母版代码库';
    if (!CODE_PATTERN.test(state.newCode)) return '请填写新项目代号（字母开头的字母数字）';
    if (!parseTemplateCode(state.selectedRepos[0])) return '模板名须含代号段（首个 - 之前为模板代号）';
    if (!state.workspaceId) return '请选择工作区目录';
    if (!state.branch.trim()) return '请填写初始化分支';
    return '';
  }

  function buildPreflightBody() {
    const templatePipelineNames = [];
    state.ends.forEach(function (e) {
      const tpl = String(e.tplName || '').trim();
      if (tpl) templatePipelineNames.push(tpl);
    });
    const body = {
      templateRepoName: state.selectedRepos[0],
      newCode: state.newCode,
      workspaceId: state.workspaceId,
      branch: state.branch.trim(),
      templatePipelineNames: templatePipelineNames,
      ends: state.ends.map(function (e) {
        const item = { end: e.end, on: !!e.enabled, name: e.name, dir: e.dir };
        if (e.end === 'backend') {
          const port = parseInt(e.port, 10);
          if (!isNaN(port)) item.port = port;
        }
        return item;
      })
    };
    const groupName = activeGroupName();
    if (groupName) body.groupNewName = groupName;
    return body;
  }

  /**
   * S2d §13.3 预检回填（反向回填扩展到预检阶段）：plan.pipelines 为服务端权威名
   * （带真实前缀，预检回执即正确名），仅在字段未被用户改写（非 dirty）且未被
   * 预览同步（非 synced）时回填 F6 流水线名——dirty 用户改写优先。
   */
  function backfillNamesFromPlan(plan) {
    const pairs = (plan && plan.pipelines) || [];
    state.ends.forEach(function (e) {
      if (e.nameDirty || e.synced) return;
      for (let i = 0; i < pairs.length; i++) {
        const to = String((pairs[i] && pairs[i].to) || '');
        const tail = stripPortSuffix(to);
        if (to && tail.slice(-(e.suffix.length + 1)) === '-' + e.suffix) {
          e.name = to;
          e.nameFromPlan = true;
          return;
        }
      }
    });
  }

  async function doPreflight() {
    const invalid = validatePreflight();
    els.preflightError.hidden = !invalid;
    els.preflightError.className = 'result result-err';
    els.preflightError.textContent = invalid;
    if (invalid) return;

    els.btnPreflight.disabled = true;
    try {
      const data = await api.post('/yunxiao/preflight', buildPreflightBody());
      state.preflight = data || {};
      state.preflightStale = false;
      // S2d §13.3：预检成功后用 plan.pipelines 回填 F6 流水线名（仅非 dirty）
      backfillNamesFromPlan(state.preflight.plan);
      renderEnds();
      renderPreflight();
    } catch (e) {
      state.preflight = null;
      renderPreflight();
      els.preflightError.hidden = false;
      els.preflightError.className = 'result result-err';
      els.preflightError.textContent = e && e.message ? e.message : '预检失败';
    } finally {
      els.btnPreflight.disabled = false;
    }
  }

  function pairListHtml(pairs) {
    if (!pairs || !pairs.length) return '—';
    return pairs.map(function (p) {
      const text = (p.from || '') + ' → ' + (p.to || '');
      return '<span class="kv-v" style="display:block;white-space:normal" title="' + escapeHtml(text) + '">'
        + escapeHtml(text) + '</span>';
    }).join('');
  }

  function renderPreflight() {
    const pre = state.preflight;
    els.preflightPanel.hidden = !pre;
    if (!pre) {
      updateExecGate();
      return;
    }

    const plan = pre.plan || {};
    const repoName = plan.repoName || '';
    // 每库徽标（S2b §11.2）：ReusedLibs 命中 → 「复用」+ 提示「将复用已有库」；否则「新建」
    const reused = repoName && (pre.reusedLibs || []).indexOf(repoName) >= 0;
    els.planRepo.innerHTML = (repoName ? escapeHtml(repoName) : '—')
      + (repoName
        ? ' <span class="badge' + (reused ? ' badge-ok' : '') + '" id="plan-lib-badge">'
          + (reused ? '复用' : '新建') + '</span>'
          + (reused ? ' <span class="kv-hint">将复用已有库</span>' : '')
        : '');
    els.planRepo.title = repoName;
    // S2e §14 修复四：复用库无改名动作 → 不显示「将改名目录」对照，改显「结构校验」（子目录存在性/待校验）；
    // 新建库保持改名对照渲染（按库类型分流）
    if (reused) {
      if (els.planDirsK) els.planDirsK.textContent = '结构校验';
      const checks = (pre.structureChecks && pre.structureChecks.length)
        ? pre.structureChecks
        : (pre.warnings || []);
      els.planDirs.innerHTML = checks.length
        ? checks.map(function (c) {
            return '<span class="kv-v" style="display:block;white-space:normal" title="' + escapeHtml(c) + '">'
              + escapeHtml(c) + '</span>';
          }).join('')
        : '—';
    } else {
      if (els.planDirsK) els.planDirsK.textContent = '将改名目录';
      els.planDirs.innerHTML = pairListHtml(plan.dirs);
    }
    els.planPipes.innerHTML = pairListHtml(plan.pipelines);
    els.planBranch.textContent = state.branch.trim() || '—';
    const conflicts = pre.conflicts || [];
    els.planConflicts.textContent = conflicts.length ? conflicts.join('；') : '无';
    els.planConflicts.title = conflicts.join('\n');

    // git 命令序列：只展示后端脱敏文本，默认展开；零 git 步骤时按成因明示
    // （全部复用 / 本地已有源码——S2e §14 修复三根即检出场景）
    const steps = pre.gitCommands || [];
    let cmdText = steps.length
      ? steps.map(function (s) { return (s.label || '') + '：' + (s.command || ''); }).join('\n')
      : (reused ? '无需 git 操作（全部复用）' : '无需 git 操作（本地已有源码）');
    const warnings = pre.warnings || [];
    if (warnings.length) {
      cmdText += '\n' + warnings.map(function (w) { return '提示：' + w; }).join('\n');
    }
    els.gitCmds.textContent = cmdText;

    renderPipeTag(plan.repoName || '');
    updateExecGate();
    // S2c §12.3.5：预检后重算 F6 门禁——全复用（无新建库）时不等 FR-2 任务即可解锁
    applyPipeLock();
  }

  /** 是否有 git 工作量（S2c §12.2）：预检 git 步骤非空 = 有库要 clone/checkout，才要求会话。 */
  function needsGitWork(pre) {
    return !!pre && (pre.gitCommands || []).length > 0;
  }

  /** 确认执行门禁：未预检 / 参数已变 / 有 git 工作量时 session 不可用 / 有冲突 / 任务进行中 → 禁用并明示原因。 */
  function updateExecGate() {
    const pre = state.preflight;
    let reason = '';
    if (!pre) {
      reason = '未预检或预检失败，无法执行';
    } else if (state.preflightStale) {
      reason = '参数已变更，请重新预检';
    } else if (needsGitWork(pre) && (!pre.sessionReady || !state.sessionReady)) {
      // S2c §12.2 门禁口径（收紧 S2b）：只要有一个库要 clone/checkout（git 步骤非空）就要求会话；
      // 仅当全部库已有本地源码检出（零 git 步骤）才免会话
      reason = '工作区会话通路不可用';
    } else if (pre.conflicts && pre.conflicts.length) {
      reason = '存在冲突：' + pre.conflicts.join('；');
    }
    if (!reason && state.taskId) {
      const st = state.task ? normTaskStatus(state.task.status) : 'running';
      if (st !== 'done' && st !== 'failed') reason = '初始化任务进行中';
    }
    els.btnExec.disabled = !!reason;
    els.execGate.textContent = reason || '预检通过，可以确认执行。';
  }

  /* ---------- F5：执行 + 逐库状态 ---------- */

  function buildExecBody() {
    const body = {
      libs: state.selectedRepos.map(function (name) {
        return { templateRepoName: name, newRepoName: repoTo(name) };
      }),
      newCode: state.newCode,
      workspaceId: state.workspaceId,
      branch: state.branch.trim()
    };
    const groupName = activeGroupName();
    if (groupName) body.groupNewName = groupName;
    return body;
  }

  async function doExec() {
    if (els.btnExec.disabled) return;
    els.btnExec.disabled = true;
    try {
      const taskId = await api.post('/yunxiao/initexec', buildExecBody());
      state.taskId = String(taskId || '');
      state.task = null;
      state.autoPreviewed = false;
      state.initSaved = false;
      if (els.initDone) els.initDone.hidden = true;
      els.taskPanel.hidden = false;
      renderTask();
      app.showToast('初始化任务已发起', 'ok');
      startPolling();
      refreshTask();
    } catch (e) {
      app.showError(e && e.message ? e.message : '发起初始化失败');
    } finally {
      updateExecGate();
    }
  }

  function startPolling() {
    if (state.pollTimer) return;
    state.pollTimer = global.setInterval(function () { refreshTask(); }, 2000);
  }

  function stopPolling() {
    if (state.pollTimer) {
      global.clearInterval(state.pollTimer);
      state.pollTimer = 0;
    }
  }

  async function refreshTask() {
    if (!state.taskId) return;
    try {
      const data = await api.get('/yunxiao/inittask', { taskId: state.taskId });
      state.task = data || null;
      renderTask();
      const st = normTaskStatus(data && data.status);
      if (st === 'done' || st === 'failed') {
        stopPolling();
        if (st === 'done') onTaskDone();
      }
    } catch (e) {
      // 轮询单次失败不打断（网络抖动）；终态前继续轮询
    }
  }

  function onTaskDone() {
    applyPipeLock();
    app.showToast('代码库复制完成，可预览并提交流水线', 'ok');
    if (!state.autoPreviewed) {
      state.autoPreviewed = true;
      doPreview();
    }
  }

  async function retryLib(repo) {
    try {
      await api.post('/yunxiao/initretry', { taskId: state.taskId, repoNames: [repo] });
      app.showToast('已发起重跑：' + repo, 'ok');
      startPolling();
      refreshTask();
    } catch (e) {
      app.showError(e && e.message ? e.message : '重跑失败');
    }
  }

  function renderTask() {
    if (!state.taskId && !state.task) {
      els.taskPanel.hidden = true;
      return;
    }
    els.taskPanel.hidden = false;
    const task = state.task;
    els.taskId.textContent = state.taskId || '—';
    els.taskId.title = state.taskId || '';
    const tKey = normTaskStatus(task && task.status);
    els.taskStatus.textContent = task ? (TASK_STATUS_TEXT[tKey] || tKey) : '进行中';

    const libs = (task && task.libs) || [];
    if (!libs.length) {
      els.taskLibs.innerHTML = '<p class="field-help">等待逐库推进…</p>';
      return;
    }
    els.taskLibs.innerHTML = libs.map(function (lib) {
      const repo = lib.repo || '';
      const key = normLibStatus(lib.status);
      const detail = lib.detail || '';
      return '<div class="taskrow" data-repo="' + escapeHtml(repo) + '">'
        + '<span class="tname" title="' + escapeHtml(repo) + '">' + escapeHtml(repo) + '</span>'
        + (detail && key === 'failed'
          ? '<span class="tdetail" title="' + escapeHtml(detail) + '">' + escapeHtml(detail) + '</span>'
          : '')
        + '<span class="' + statusClass(key) + '">' + escapeHtml(LIB_STATUS_TEXT[key] || key) + '</span>'
        + (key === 'failed'
          ? '<button class="btn" type="button" data-act="retry">重跑</button>'
          : '')
        + '</div>';
    }).join('');
  }

  /* ---------- F6：三端行 / 依赖门禁 / diff / 提交 ---------- */

  function renderPipeTag(repoName) {
    els.pipeRepoTag.textContent = repoName ? '新库标签：' + repoName : '新库标签：未预检';
    els.pipeRepoTag.title = repoName || '';
  }

  function isPipeUnlocked() {
    if (state.task && normTaskStatus(state.task.status) === 'done') return true;
    // 已初始化项目再次进入：登记反显即视为就绪，允许重新预览/提交
    if (state.restored) return true;
    // S2c §12.3.5：全复用（预检无新建库）时 F6 不等 FR-2 任务完成，即可预览/同步
    return isAllReuse();
  }

  /** 是否全复用（预检无新建库）：目标库命中 ReusedLibs（S2c §12.3.5 F6 门禁放宽判据）。 */
  function isAllReuse() {
    const pre = state.preflight;
    if (!pre || !pre.plan || !pre.plan.repoName) return false;
    return (pre.reusedLibs || []).indexOf(pre.plan.repoName) >= 0;
  }

  /* ---------- 初始化完成登记（项目列表「已初始化」判定） ---------- */

  /** 解析 #/init?projectId=&identifier=&name= 的项目上下文；无参数时返回空值。 */
  function readProjectContext() {
    const context = { id: '', identifier: '', name: '' };
    const hash = String(global.location.hash || '');
    const mark = hash.indexOf('?');
    if (mark < 0) return context;
    hash.slice(mark + 1).split('&').forEach(function (pair) {
      if (!pair) return;
      const eq = pair.indexOf('=');
      const key = eq < 0 ? pair : pair.slice(0, eq);
      let value = '';
      try {
        value = eq < 0 ? '' : decodeURIComponent(pair.slice(eq + 1).replace(/\+/g, ' '));
      } catch (e) {
        value = '';
      }
      if (key === 'projectId') context.id = value;
      else if (key === 'identifier') context.identifier = value;
      else if (key === 'name') context.name = value;
    });
    return context;
  }

  /** 代码库侧是否已绑定：任务 done，或全复用（目标库本就存在，无需复制）。 */
  function codeSideReady() {
    if (state.task && normTaskStatus(state.task.status) === 'done') return true;
    return isAllReuse();
  }

  /** 完成条件：代码库侧就绪 + 每个启用端「已存在」或「提交成功」。 */
  function isInitComplete() {
    if (!codeSideReady() || !state.newCode) return false;
    const enabled = state.ends.filter(function (e) { return e.enabled; });
    if (!enabled.length) return false;
    return enabled.every(function (e) {
      const pv = state.preview[e.end];
      return !!(pv && pv.exists) || !!(e.result && e.result.ok);
    });
  }

  function showInitDone(restored) {
    if (!els.initDone) return;
    els.initDone.hidden = false;
    els.initDone.textContent = restored
      ? '该项目已完成初始化，已反显上次配置；如需调整，重新预检后预览差异并提交即可。'
      : '初始化完成：代码库已绑定、三端流水线已就绪，可返回项目列表查看「已初始化」。';
  }

  /**
   * 已初始化项目再次进入：把完成登记反显到表单
   * （母版库 / 新代号 / 代码组 / 分支 / 三端 enable·母版流水线·流水线名·端口）。
   */
  function applyInitRecord(record) {
    if (!record) return false;
    if (record.templateCode) state.templateCode = record.templateCode;
    if (record.codename) state.newCode = record.codename;
    if (record.groupName) {
      state.groupChoice = record.groupName;
      state.groupDirty = true;
    }
    if (record.branch) state.branch = record.branch;
    if (record.workspaceId) state.workspaceId = record.workspaceId;
    if (Array.isArray(record.selectedRepos) && record.selectedRepos.length) {
      state.selectedRepos = record.selectedRepos.slice();
      state.repoNewNames = {};
      state.repoDirty = {};
    }
    if (Array.isArray(record.ends) && record.ends.length) {
      state.ends = state.ends.map(function (endRec) {
        const hit = record.ends.find(function (r) { return r.end === endRec.end; });
        if (!hit) return endRec;
        return Object.assign({}, endRec, {
          enabled: hit.enabled !== false,
          name: hit.name || '',
          nameDirty: !!hit.name,
          tplName: hit.templateName || '',
          derivedFrom: hit.templateName || '',
          dir: hit.dir || '',
          dirDirty: !!hit.dir,
          port: hit.port === null || hit.port === undefined ? endRec.port : hit.port
        });
      });
    }
    state.restored = true;
    state.initSaved = true;   // 已登记过：不再自动重复登记
    return true;
  }

  /** 拉取项目完成登记并反显（失败不阻断页面）。 */
  async function loadInitRecord(context) {
    try {
      const record = await api.post('/yunxiao/projectinitget', {
        projectId: context.id || '',
        identifier: context.identifier || ''
      });
      if (!record) return;
      if (applyInitRecord(record)) {
        renderAll();
        applyPipeLock();
        showInitDone(true);
      }
    } catch (err) {
      if (global.console) global.console.warn('读取初始化登记失败', err);
    }
  }

  /** 条件满足时落盘一次完成登记（失败不阻断主流程，云效侧已就绪）。 */
  async function maybeMarkInitComplete() {
    if (state.initSaved || !isInitComplete()) return;
    const project = state.project || { id: '', identifier: '', name: '' };
    const identifier = project.identifier || state.newCode;
    if (!project.id && !identifier) return;

    state.initSaved = true;
    try {
      await api.post('/yunxiao/projectinit', {
        projectId: project.id || '',
        identifier: identifier || '',
        codename: state.newCode,
        workspaceId: state.workspaceId || '',
        repoName: primaryRepoName() || '',
        templateCode: state.templateCode || '',
        groupName: state.groupChoice && state.groupChoice !== '__new__' ? state.groupChoice : (state.groupNewName || ''),
        branch: state.branch || '',
        selectedRepos: (state.selectedRepos || []).slice(),
        ends: state.ends.map(function (e) {
          const pv = state.preview[e.end] || {};
          const info = pv.info || null;
          return {
            end: e.end,
            enabled: !!e.enabled,
            dir: e.dir || '',
            name: (info && info.actualName) || pv.name || e.name || '',
            templateName: e.tplName || e.derivedFrom || '',
            port: e.port === '' || e.port === undefined || e.port === null ? null : Number(e.port),
            pipelineId: state.pipelineIds[e.end] || (info && info.id) || ''
          };
        })
      });
      showInitDone();
    } catch (err) {
      state.initSaved = false;
      if (global.console) global.console.warn('初始化完成登记失败', err);
    }
  }

  function applyPipeLock() {
    const unlocked = isPipeUnlocked();
    els.pipeLock.hidden = unlocked;
    els.pipeEnds.classList.toggle('pipe-off', !unlocked);
    els.btnPreview.disabled = !unlocked;
    els.btnCommit.disabled = !unlocked;
    if (!unlocked) renderPipeTag(state.preflight && state.preflight.plan ? state.preflight.plan.repoName : '');
  }

  /** S2d §13.3：本地默认派生（预检前占位）标注「以预检为准」，不再当提交真值。 */
  function showNameHint(e) {
    return !!(e.name && !e.nameDirty && !e.synced && !e.nameFromPlan);
  }

  function renderEnds() {
    els.pipeEnds.innerHTML = state.ends.map(function (e) {
      const pv = state.preview[e.end];
      const existsBadge = pv
        ? (pv.exists
          ? '<span class="badge badge-ok" data-f="exists">已存在（提交时跳过）</span>'
          : '<span class="badge" data-f="exists">缺失（将新建）</span>')
        : '';
      // S2c §12.3：已存在端反向回填后标记「已同步」态
      const syncedBadge = e.synced
        ? '<span class="badge badge-ok" data-f="synced">已同步</span>'
        : '';
      return '<div class="endrow' + (e.enabled ? '' : ' off') + '" data-end="' + escapeHtml(e.end) + '">'
        + '<div class="er-h">'
        + '<input type="checkbox" data-f="on"' + (e.enabled ? ' checked' : '') + ' />'
        + '<span class="er-role">' + escapeHtml(e.role) + '</span>'
        + existsBadge
        + syncedBadge
        + '</div>'
        + '<div class="frow"><label>母版流水线</label>'
        + '<input class="field-input" data-f="tpl" value="' + escapeHtml(templateNameOf(e)) + '" spellcheck="false" '
        + 'placeholder="选填 · 反查兜底" /></div>'
        + '<div class="frow"><label>流水线名</label>'
        + '<input class="field-input" data-f="name" value="' + escapeHtml(e.name) + '" spellcheck="false" /></div>'
        // S2d §13.3：本地占位派生标注「以预检为准」（预检回填/同步/手改后不再显示）
        + '<p class="er-hint" data-f="namehint"' + (showNameHint(e) ? '' : ' hidden') + '>'
        + '本地派生为占位 · 以预检为准</p>'
        + (e.end === 'backend'
          ? '<div class="pbox"><div class="ph"><span>后端端口 · 写入 dev 环境变量</span></div>'
            + '<div class="frow"><label>监听端口</label>'
            + '<input class="field-input" data-f="port" type="number" min="1" max="65535" '
            + 'value="' + escapeHtml(e.port) + '" /></div></div>'
          : '')
        + '<label class="er-ow"><input type="checkbox" data-f="ow"' + (e.overwrite ? ' checked' : '') + ' />'
        + '覆盖已有流水线</label>'
        + '<div class="er-confirm" data-f="confirm"' + (e.confirmPending ? '' : ' hidden') + '>'
        + '<span style="flex:1 1 auto;min-width:0">该端已有流水线，提交将走覆盖（update）。</span>'
        + '<button class="btn" type="button" data-f="confirm-yes">确认覆盖</button>'
        + '<button class="btn" type="button" data-f="confirm-no">取消</button>'
        + '</div>'
        + '<div class="result er-result'
        + (e.result ? (e.result.ok ? ' result-ok' : ' result-err') : '')
        + '" data-f="result"' + (e.result ? '' : ' hidden') + '>'
        + (e.result ? escapeHtml(e.result.message) : '') + '</div>'
        + '</div>';
    }).join('');

    endRefs = {};
    Array.prototype.forEach.call(els.pipeEnds.querySelectorAll('.endrow'), function (row) {
      endRefs[row.getAttribute('data-end')] = {
        row: row,
        on: row.querySelector('[data-f="on"]'),
        dir: row.querySelector('[data-f="dir"]'),
        name: row.querySelector('[data-f="name"]'),
        namehint: row.querySelector('[data-f="namehint"]'),
        tpl: row.querySelector('[data-f="tpl"]'),
        port: row.querySelector('[data-f="port"]'),
        ow: row.querySelector('[data-f="ow"]'),
        confirm: row.querySelector('[data-f="confirm"]'),
        result: row.querySelector('[data-f="result"]')
      };
    });
    syncEnds();
  }

  function syncEnds() {
    state.ends.forEach(function (e) {
      const refs = endRefs[e.end];
      if (!refs) return;
      refs.row.classList.toggle('off', !e.enabled);
      refs.on.checked = !!e.enabled;
      if (refs.dir && refs.dir.value !== e.dir) refs.dir.value = e.dir;
      if (refs.name.value !== e.name) refs.name.value = e.name;
      if (refs.namehint) refs.namehint.hidden = !showNameHint(e);
      if (refs.tpl.value !== templateNameOf(e)) refs.tpl.value = templateNameOf(e);
      if (refs.port && String(refs.port.value) !== String(e.port)) refs.port.value = e.port;
      refs.ow.checked = !!e.overwrite;
      if (refs.confirm) refs.confirm.hidden = !e.confirmPending;
      if (refs.result) {
        refs.result.hidden = !e.result;
        refs.result.className = 'result er-result ' + (e.result && e.result.ok ? 'result-ok' : 'result-err');
        refs.result.textContent = e.result ? e.result.message : '';
      }
    });
    applyPipeLock();
  }

  function findEnd(endName) {
    for (let i = 0; i < state.ends.length; i++) {
      if (state.ends[i].end === endName) return state.ends[i];
    }
    return null;
  }

  /** 主新库名（预览/提交用）：第一个母版库的改写后新库名。 */
  function primaryRepoName() {
    const first = state.selectedRepos[0];
    return first ? repoTo(first) : '';
  }

  // 从任务回执里查新库 id（LibState.repoId）；未回填返回空串
  function lookupRepoId(repoName) {
    const libs = (state.task && state.task.libs) || [];
    for (let i = 0; i < libs.length; i++) {
      if (libs[i] && libs[i].repo === repoName && libs[i].repoId) return String(libs[i].repoId);
    }
    return '';
  }

  /** 页面锁定：预览/提交进行中禁止所有操作 */
  function lockPage() {
    document.querySelector('.content')?.classList.add('blocking');
  }
  function unlockPage() {
    document.querySelector('.content')?.classList.remove('blocking');
  }

  async function doPreview() {
    if (els.btnPreview.disabled) return;
    const repoName = primaryRepoName();
    if (!repoName || !CODE_PATTERN.test(state.newCode)) {
      app.showError('请先完成代码库区配置（母版库与新项目代号）');
      return;
    }
    els.btnPreview.disabled = true;
    lockPage();
    try {
      const data = await api.post('/yunxiao/pipelinepreview', {
        // 优先取任务回执的新库 id（LibState.RepoId）；全复用无任务时留空 → 后端按 repoName 回查真实 id（S2c §12.3.5）
        newRepoId: lookupRepoId(repoName) || '',
        newRepoName: repoName,
        templateCode: state.templateCode,
        newCode: state.newCode,
        ends: state.ends.map(function (e) {
          const item = { end: e.end, enabled: !!e.enabled, buildDir: e.dir, pipelineName: e.name };
          if (e.end === 'backend') {
            const port = parseInt(e.port, 10);
            if (!isNaN(port)) item.port = port;
          }
          const tpl = String(e.tplName || '').trim();
          if (tpl) item.templatePipelineName = tpl;
          return item;
        })
      });
      state.preview = {};
      ((data && data.items) || []).forEach(function (item) {
        state.preview[item.end] = {
          name: item.name || '',
          diff: item.diff || '',
          warning: item.warning || '',
          // S2b §11.2：派生名已存在（提交默认跳过）/ 缺失（将新建）
          exists: !!item.exists,
          // 契约带回替换后全文（S2 修复）：提交优先用 content，缺失才由 diff 重建
          content: item.content,
          // S2c §12.3：已存在端真实流水线信息（回显 + 反向回填）
          info: item.pipeline || null
        };
        // 反向回填：已存在端把 ActualName/解析端口回填表单，表单以现实为准（S2c §12.3）
        if (item.exists && item.pipeline) {
          const rec = findEnd(item.end);
          if (rec) {
            if (item.pipeline.actualName) {
              rec.name = String(item.pipeline.actualName);
              rec.nameDirty = false;
            }
            if (item.pipeline.port !== undefined && item.pipeline.port !== null) {
              rec.port = item.pipeline.port;
            }
            rec.synced = true;   // 已同步态（dirty 重置为已同步）
          }
        }
      });
      const ends = Object.keys(state.preview);
      if (ends.indexOf(state.activeDiffEnd) < 0) state.activeDiffEnd = ends[0] || '';
      renderEnds();
      renderDiff();
      maybeMarkInitComplete();
    } catch (e) {
      app.showError(e && e.message ? e.message : '流水线差异预览失败');
    } finally {
      unlockPage();
      els.btnPreview.disabled = !isPipeUnlocked();
    }
  }

  function renderDiff() {
    const ends = state.ends.filter(function (e) { return state.preview[e.end]; });
    els.diffPanel.hidden = !ends.length;
    if (!ends.length) return;
    els.diffTabs.innerHTML = ends.map(function (e) {
      return '<button type="button" class="tab' + (e.end === state.activeDiffEnd ? ' on' : '') + '" '
        + 'data-end="' + escapeHtml(e.end) + '">' + escapeHtml(e.role) + '</button>';
    }).join('');
    const pv = state.preview[state.activeDiffEnd] || state.preview[ends[0].end];
    els.diffPre.textContent = pv.diff || '（无差异）';
    els.diffWarning.textContent = pv.warning || '';
    els.diffWarning.hidden = !pv.warning;
  }

  /** 逐端结果写回行内。 */
  function setEndResult(endName, ok, message) {
    const e = findEnd(endName);
    if (e) {
      e.result = { ok: ok, message: message };
      e.confirmPending = false;
    }
    syncEnds();
  }

  /* 提交内容来源：预览响应带 content（替换后全文）时优先用；
     缺失时才由 diff 的 + 行尽力重建（回退）。 */
  function reconstructFromDiff(diff) {
    return String(diff || '').split('\n')
      .filter(function (line) { return line.charAt(0) === '+'; })
      .map(function (line) { return line.slice(1); })
      .join('\n');
  }

  function contentOf(preview) {
    return preview.content !== undefined && preview.content !== null
      ? String(preview.content)
      : reconstructFromDiff(preview.diff);
  }

  async function doCommit() {
    if (els.btnCommit.disabled) return;
    lockPage();
    const items = [];
    const submitted = [];
    let localFail = 0;

    state.ends.forEach(function (e) {
      if (!e.enabled) return;
      const pv = state.preview[e.end];
      if (!pv) {
        setEndResult(e.end, false, '请先预览差异');
        localFail++;
        return;
      }
      const content = contentOf(pv);
      if (!pv.exists && !content) {
        setEndResult(e.end, false, '预览内容为空（未反查到母版流水线或 diff 重建失败），已跳过该端');
        localFail++;
        return;
      }
      const name = pv.name || e.name;
      const pid = state.pipelineIds[e.end];
      const force = state.forceUpdate[e.end];

      if (e.overwrite || force) {
        if (!pid) {
          setEndResult(e.end, false, '覆盖需要既有流水线 id，请先以新建提交后再覆盖');
          localFail++;
          return;
        }
        items.push({ end: e.end, mode: 'update', name: name, content: content, pipelineId: pid, confirm: true });
        submitted.push(e.end);
        return;
      }
      if (pid && !e.overwrite) {
        // update 行未勾「覆盖」：提交前卡内确认
        e.confirmPending = true;
        syncEnds();
        return;
      }
      // 默认补全语义（S2b §11.2）：create 带 skipIfExisting，服务端 TOCTOU 复查命中即「已存在跳过」
      items.push({ end: e.end, mode: 'create', name: name, content: content, skipIfExisting: true });
      submitted.push(e.end);
    });

    if (!items.length) {
      if (localFail) app.showError('无可提交的端，请检查各端说明');
      return;
    }

    els.btnCommit.disabled = true;
    try {
      const data = await api.post('/yunxiao/pipelinecommit', { items: items });
      const results = (data && (data.results || data.items)) || [];
      const byEnd = {};
      results.forEach(function (r) { byEnd[r.end] = r; });
      submitted.forEach(function (endName) {
        const r = byEnd[endName];
        if (!r) {
          setEndResult(endName, false, '未返回该端结果');
          return;
        }
        if (r.ok) {
          if (r.pipelineId) state.pipelineIds[endName] = String(r.pipelineId);
          delete state.forceUpdate[endName];
          const msg = r.message || '';
          if (msg.indexOf('已存在') !== -1) {
            // S2c §12.3.4：已存在端零操作 → 显示「已同步」+ Id/ActualName 可追溯
            const rec = findEnd(endName);
            if (rec) rec.synced = true;
            const trace = [];
            if (r.pipelineId) trace.push(String(r.pipelineId));
            if (msg.indexOf('（') !== -1) trace.push(msg.slice(msg.indexOf('（') + 1, msg.lastIndexOf('）')));
            setEndResult(endName, true, '已同步' + (trace.length ? ' · ' + trace.join(' · ') : ''));
          } else {
            // 成功 + id
            setEndResult(endName, true, msg || ('成功' + (r.pipelineId ? ' · ' + r.pipelineId : '')));
          }
        } else {
          setEndResult(endName, false, r.message || '提交失败');
        }
      });
      maybeMarkInitComplete();
    } catch (e) {
      // 整包失败（如 409 覆盖未确认）：message 原样逐端展示
      const message = e && e.message ? e.message : '提交失败';
      submitted.forEach(function (endName) {
        setEndResult(endName, false, message);
      });
    } finally {
      unlockPage();
      els.btnCommit.disabled = !isPipeUnlocked();
    }
  }

  /* ---------- 入口与事件 ---------- */

  /** 把 state 中的代号 / 分支回写到输入框（反显与外部状态变更共用，值相同不写避免打断输入）。 */
  function syncCommonInputs() {
    if (els.code && els.code.value !== state.newCode) els.code.value = state.newCode;
    if (els.branch && els.branch.value !== state.branch) els.branch.value = state.branch;
  }

  function renderAll() {
    syncCommonInputs();
    renderTplSel();
    renderGroupSelect();
    renderWorkspaceSelect();
    applyDerivation();
    renderDerive();
    syncEnds();
    renderPreflight();
    renderTask();
    renderDiff();
    applyPipeLock();
    updateExecGate();
  }

  /** 首屏进入：拉清单；已初始化项目反显登记；进行中的任务恢复轮询。 */
  function enter() {
    const context = readProjectContext();
    const key = (context.id || '') + '|' + (context.identifier || '');
    if (key !== state.restoreKey) {
      state.restoreKey = key;
      state.restored = false;
      state.initSaved = false;
      if (els && els.initDone) els.initDone.hidden = true;
    }
    state.project = context;
    if (!state.reposLoaded && !state.reposLoading) loadManifests();
    renderAll();
    if ((context.id || context.identifier) && !state.restored) {
      loadInitRecord(context);
    }
    if (state.taskId && state.task) {
      const st = normTaskStatus(state.task.status);
      if (st !== 'done' && st !== 'failed') startPolling();
    }
    applyPipeLock();
  }

  function init() {
    els = {
      tplCount: el('tpl-count'),
      tplSel: el('tpl-sel'),
      code: el('init-code'),
      deriveTag: el('derive-tag'),
      deriveList: el('derive-list'),
      group: el('init-group'),
      groupNew: el('init-group-new'),
      workspace: el('init-workspace'),
      wsCombo: el('ws-combo'),
      wsTrigger: el('ws-trigger'),
      wsTriggerText: el('ws-trigger-text'),
      wsPop: el('ws-pop'),
      wsSearch: el('ws-search'),
      wsList: el('ws-list'),
      wsBrowseBtn: el('ws-browse-btn'),
      wsDirBrowser: el('ws-dir-browser'),
      wsDirCrumb: el('ws-dir-crumb'),
      wsDirList: el('ws-dir-list'),
      wsDirChoose: el('ws-dir-choose'),
      wsDirClose: el('ws-dir-close'),
      wsDirError: el('ws-dir-error'),
      wsNewPath: el('ws-new-path'),
      wsNew: el('ws-new'),
      btnWsCreate: el('btn-ws-create'),
      wsNewResult: el('ws-new-result'),
      branch: el('init-branch'),
      btnPreflight: el('btn-preflight'),
      preflightError: el('preflight-error'),
      preflightPanel: el('preflight-panel'),
      planRepo: el('plan-repo'),
      planDirsK: el('plan-dirs-k'),
      planDirs: el('plan-dirs'),
      planPipes: el('plan-pipes'),
      planBranch: el('plan-branch'),
      planConflicts: el('plan-conflicts'),
      gitCmds: el('git-cmds'),
      btnExec: el('btn-exec'),
      execGate: el('exec-gate'),
      initDone: el('init-done'),
      taskPanel: el('task-panel'),
      taskId: el('task-id'),
      taskStatus: el('task-status'),
      taskLibs: el('task-libs'),
      pipeRepoTag: el('pipe-repo-tag'),
      pipeLock: el('pipe-lock'),
      pipeEnds: el('pipe-ends'),
      btnPreview: el('btn-preview'),
      btnCommit: el('btn-commit'),
      diffPanel: el('diff-panel'),
      diffTabs: el('diff-tabs'),
      diffPre: el('diff-pre'),
      diffWarning: el('diff-warning'),
      btnReload: el('btn-reload-manifests')
    };

    renderEnds();

    els.code.addEventListener('input', function () {
      state.newCode = els.code.value.trim();
      applyDerivation();
      renderDerive();
      syncEnds();
      markStale();
      scheduleDeriveRefresh();   // S2e §14 修复一：代号变更 → 防抖刷新派生
    });

    els.group.addEventListener('change', function () {
      state.groupChoice = els.group.value;
      state.groupDirty = true;   // S2e §14 修复二：用户手动改过 → 不再自动覆盖
      els.groupNew.hidden = state.groupChoice !== '__new__';
      markStale();
    });
    els.groupNew.addEventListener('input', function () {
      state.groupNewName = els.groupNew.value;
      markStale();
    });
    els.workspace.addEventListener('change', function () {
      state.workspaceId = els.workspace.value;
      markStale();
    });

    // S2d §13.1：工作区可搜索组合框（触发器开合 / 搜索过滤 / 选中绑定 workspaceId）
    if (els.wsTrigger) {
      els.wsTrigger.addEventListener('click', function (e) {
        e.stopPropagation();
        setWsComboOpen(!state.wsComboOpen);
      });
    }
    if (els.wsPop) {
      els.wsPop.addEventListener('click', function (e) {
        e.stopPropagation();   // 防 document 点击收起逻辑误关弹层
        const target = e.target;
        const opt = target && target.closest ? target.closest('.combo-opt') : null;
        if (opt) {
          // 「＋ 新建工作区」是动作项，不是数据项：清空已选并展开手输区
          if (opt.getAttribute('data-wsnew') === '1') {
            enterWsNewMode();
            setWsComboOpen(false);
            return;
          }
          // 选中回填原下拉值语义：绑定 workspaceId（hidden 承载 + change 通知）
          const id = opt.getAttribute('data-wsid') || '';
          setWsNewMode(false);
          state.workspaceId = id;
          if (els.workspace) {
            els.workspace.value = id;
            els.workspace.dispatchEvent(new Event('change'));
          }
          renderWorkspaceSelect();
          setWsComboOpen(false);
          return;
        }
        if (target === els.wsBrowseBtn) {
          openWsBrowser();
          return;
        }
        const crumb = target && target.closest ? target.closest('.crumb-seg') : null;
        if (crumb) {
          loadWsDirs(crumb.getAttribute('data-path') || '');
          return;
        }
        const row = target && target.closest ? target.closest('.dir-row') : null;
        if (row) {
          loadWsDirs(row.getAttribute('data-path') || '');
          return;
        }
        if (target === els.wsDirChoose) {
          // 「选此目录」等同进入新建态：先展开手输区再回填，否则回填结果不可见
          if (state.wsBrowsePath) {
            if (!state.wsCreating) enterWsNewMode();
            if (els.wsNewPath) {
              els.wsNewPath.value = state.wsBrowsePath;
              syncWsCreateBtn();
              els.wsNewPath.focus();
            }
            setWsComboOpen(false);
          }
          return;
        }
        if (target === els.wsDirClose) {
          state.wsBrowseOpen = false;
          renderWsBrowser();
        }
      });
    }
    if (els.wsSearch) {
      els.wsSearch.addEventListener('input', function () {
        state.wsQuery = els.wsSearch.value;
        renderWsComboList();
      });
    }
    document.addEventListener('click', function () {
      if (state.wsComboOpen) setWsComboOpen(false);
    });

    // 工作区手输新建（S2c §12.1.1）：空路径禁用按钮，成功后重拉 manifests 并选中新工作区
    function syncWsCreateBtn() {
      if (!els.wsNewPath || !els.btnWsCreate) return;
      els.btnWsCreate.disabled = !els.wsNewPath.value.trim();
    }
    if (els.wsNewPath) {
      els.wsNewPath.addEventListener('input', syncWsCreateBtn);
      syncWsCreateBtn();
    }
    if (els.btnWsCreate) {
      els.btnWsCreate.addEventListener('click', async function () {
        const path = els.wsNewPath.value.trim();
        if (!path) return;
        els.btnWsCreate.disabled = true;
        if (els.wsNewResult) {
          els.wsNewResult.hidden = true;
          els.wsNewResult.textContent = '';
        }
        try {
          // api.js post 已带双层 payload + Bearer（与 /yunxiao 同壳）；字段按 WorkspaceCreateRequest.rootPath。
          // S2d §13.2：create 为 ensure 幂等语义（规范化命中既有工作区即复用），响应带 reused 标记
          const data = await api.post('/workspace/create', { rootPath: path, type: 'Normal' });
          const reused = !!(data && data.reused);
          await loadManifests();
          // 成功一律「选中该工作区 + 重拉列表」：按 ensure 响应的 workspaceId 绑定（原下拉值语义）
          const wid = data && data.workspaceId !== undefined && data.workspaceId !== null
            ? String(data.workspaceId)
            : '';
          if (wid) {
            state.workspaceId = wid;
            setWsNewMode(false);
            renderWorkspaceSelect();
            markStale();
          }
          els.wsNewPath.value = '';
          syncWsCreateBtn();
          const tip = reused ? '已复用现有工作区' : '已新建工作区';
          if (els.wsNewResult) {
            els.wsNewResult.hidden = false;
            els.wsNewResult.className = 'result er-result result-ok';
            els.wsNewResult.textContent = tip;
          }
          app.showToast(tip, 'ok');
        } catch (e) {
          // 失败（400/500 等）显示宿主 message
          if (els.wsNewResult) {
            els.wsNewResult.hidden = false;
            els.wsNewResult.className = 'result er-result result-err';
            els.wsNewResult.textContent = e && e.message ? e.message : '新建工作区失败';
          }
          syncWsCreateBtn();
        }
      });
    }

    els.branch.addEventListener('input', function () {
      state.branch = els.branch.value;
      markStale();
    });

    els.btnPreflight.addEventListener('click', doPreflight);
    els.btnExec.addEventListener('click', doExec);
    els.btnPreview.addEventListener('click', doPreview);
    els.btnCommit.addEventListener('click', doCommit);
    els.btnReload.addEventListener('click', function () {
      state.reposLoaded = false;
      loadManifests();
    });

    // 母版库下拉（勾选切换 / 弹层开合）；stopPropagation 防 document 关闭逻辑
    // 在 innerHTML 重渲染后误判 target 脱离容器而把弹层关掉
    els.tplSel.addEventListener('click', function (e) {
      e.stopPropagation();
      const target = e.target;
      const toggle = target && target.closest ? target.closest('[data-act="toggle"]') : null;
      if (toggle) {
        state.popOpen = !state.popOpen;
        renderTplSel();
        return;
      }
      const opt = target && target.closest ? target.closest('.mopt') : null;
      if (opt) {
        const name = opt.getAttribute('data-repo') || '';
        const index = state.selectedRepos.indexOf(name);
        if (index >= 0) state.selectedRepos.splice(index, 1);
        else state.selectedRepos.push(name);
        applyDerivation();
        renderTplSel();
        renderDerive();
        syncEnds();
        markStale();
        scheduleDeriveRefresh();   // S2e §14 修复一：母版选择变更 → 防抖刷新派生
      }
    });
    document.addEventListener('click', function (e) {
      if (!state.popOpen) return;
      const target = e.target;
      if (target && els.tplSel.contains(target)) return;
      state.popOpen = false;
      renderTplSel();
    });

    // 派生预览逐行改写（改后以改写值为准）
    els.deriveList.addEventListener('input', function (e) {
      const target = e.target;
      const kind = target && target.getAttribute ? target.getAttribute('data-kind') : '';
      const key = target && target.getAttribute ? target.getAttribute('data-key') : '';
      if (!kind || !key) return;
      const value = target.value;
      if (kind === 'repo') {
        state.repoNewNames[key] = value;
        state.repoDirty[key] = true;
      } else {
        const rec = findEnd(key);
        if (!rec) return;
        if (kind === 'dir') {
          rec.dir = value;
          rec.dirDirty = true;
        } else {
          rec.name = value;
          rec.nameDirty = true;
        }
        syncEnds();
      }
      markStale();
    });

    // F6 端行输入 / 勾选 / 卡内确认
    els.pipeEnds.addEventListener('input', function (e) {
      const target = e.target;
      const field = target && target.getAttribute ? target.getAttribute('data-f') : '';
      const row = target && target.closest ? target.closest('.endrow') : null;
      if (!field || !row) return;
      const rec = findEnd(row.getAttribute('data-end'));
      if (!rec) return;
      if (field === 'dir') {
        rec.dir = target.value;
        rec.dirDirty = true;
        renderDerive();
      } else if (field === 'name') {
        rec.name = target.value;
        rec.nameDirty = true;
        renderDerive();
      } else if (field === 'tpl') {
        rec.tplName = target.value;
        if (!rec.nameDirty) {
          rec.name = derivedName(rec);
          const refs = endRefs[rec.end];
          if (refs) refs.name.value = rec.name;
        }
        renderDerive();
      } else if (field === 'port') {
        rec.port = target.value;
        // 端口盒变更 → 后端流水线名重派生（S2c §12.3：端口盒值参与派生）
        if (rec.end === 'backend' && !rec.nameDirty) {
          rec.name = derivedName(rec);
          const refs = endRefs[rec.end];
          if (refs && refs.name) refs.name.value = rec.name;
          renderDerive();
        }
        scheduleDeriveRefresh();   // S2e §14 修复一：端口变更 → 防抖刷新派生
      }
      markStale();
    });

    els.pipeEnds.addEventListener('change', function (e) {
      const target = e.target;
      const field = target && target.getAttribute ? target.getAttribute('data-f') : '';
      const row = target && target.closest ? target.closest('.endrow') : null;
      if (!field || !row) return;
      const rec = findEnd(row.getAttribute('data-end'));
      if (!rec) return;
      if (field === 'on') {
        rec.enabled = !!target.checked;
        syncEnds();
        markStale();
      } else if (field === 'ow') {
        rec.overwrite = !!target.checked;
        if (rec.overwrite) {
          rec.confirmPending = false;
          delete state.forceUpdate[rec.end];
        }
        syncEnds();
      }
    });

    els.pipeEnds.addEventListener('click', function (e) {
      const target = e.target;
      const row = target && target.closest ? target.closest('.endrow') : null;
      if (!row) return;
      const rec = findEnd(row.getAttribute('data-end'));
      if (!rec) return;
      const field = target && target.getAttribute ? target.getAttribute('data-f') : '';
      if (field === 'confirm-yes') {
        rec.confirmPending = false;
        state.forceUpdate[rec.end] = true;
        syncEnds();
        doCommit();
      } else if (field === 'confirm-no') {
        rec.confirmPending = false;
        syncEnds();
      }
    });

    // 逐库状态「重跑」
    els.taskLibs.addEventListener('click', function (e) {
      const target = e.target;
      const btn = target && target.closest ? target.closest('[data-act="retry"]') : null;
      if (!btn) return;
      const row = btn.closest('.taskrow');
      if (row) retryLib(row.getAttribute('data-repo') || '');
    });

    // diff 端切换
    els.diffTabs.addEventListener('click', function (e) {
      const target = e.target;
      const tab = target && target.closest ? target.closest('.tab') : null;
      if (!tab) return;
      state.activeDiffEnd = tab.getAttribute('data-end') || '';
      renderDiff();
    });
  }

  global.YunxiaoInit = {
    init: init,
    enter: enter,
    // 纯函数导出（断言 / 复用）
    deriveNaming: deriveNaming,
    parseTemplateCode: parseTemplateCode,
    parsePipelinePrefix: parsePipelinePrefix,
    renameByCode: renameByCode,
    matchGroupChoice: matchGroupChoice   // S2e §14 修复二：代码组自动预选纯函数
  };
})(window);
