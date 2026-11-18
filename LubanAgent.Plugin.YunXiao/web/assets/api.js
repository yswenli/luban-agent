/*
  云效 DevOps 工作台 · 宿主 REST 通信壳（S1）。

  口径逐字对齐 LubanAgentWebHost/web/src/lib/api.ts：
  1. POST：body 双层包裹 JSON.stringify({ payload: JSON.stringify(body) })，
     Content-Type 必须精确含 "application/json; charset=utf-8"（SAEA.MVC 绑定要求）；
  2. GET：Accept 头 + query 拼接（空值/undefined/null 跳过）；
  3. 响应壳 { success, code, message, data }：success=false 抛 ApiError（带 code + message）。

  无构建链：经典脚本，不用 ES module / mjs / json。
*/
(function (global) {
  'use strict';

  /** 请求失败错误：带 code + message。 */
  class ApiError extends Error {
    constructor(code, message) {
      super(message);
      this.name = 'ApiError';
      this.code = code;
    }
  }

  /** query 拼接：空串 / undefined / null 不进 query。 */
  function buildQuery(params) {
    if (!params) return '';
    const search = new URLSearchParams();
    Object.keys(params).forEach(function (key) {
      const value = params[key];
      if (value === undefined || value === null || value === '') return;
      search.set(key, String(value));
    });
    const text = search.toString();
    return text ? '?' + text : '';
  }

  /** ApiEnvelope 解包：失败抛 ApiError。 */
  function parseEnvelope(response) {
    return response.text().then(function (text) {
      if (!text) {
        if (response.ok) return undefined;
        throw new ApiError(response.status, '请求失败（HTTP ' + response.status + '）');
      }
      let envelope;
      try {
        envelope = JSON.parse(text);
      } catch (e) {
        throw new ApiError(response.status, text.slice(0, 200));
      }
      if (!envelope || !envelope.success) {
        throw new ApiError(
          (envelope && envelope.code) || response.status,
          (envelope && envelope.message) || '请求失败'
        );
      }
      return envelope.data;
    });
  }

  /** GET：path + query，同源凭据（宿主会话 Cookie）。 */
  function get(path, params) {
    return global
      .fetch(path + buildQuery(params), {
        method: 'GET',
        headers: { Accept: 'application/json' },
        credentials: 'same-origin'
      })
      .then(parseEnvelope);
  }

  /** POST：双层 payload 包裹，同源凭据。 */
  function post(path, body) {
    return global
      .fetch(path, {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json; charset=utf-8'
        },
        body: JSON.stringify({ payload: JSON.stringify(body === undefined || body === null ? {} : body) }),
        credentials: 'same-origin'
      })
      .then(parseEnvelope);
  }

  global.YunxiaoApi = { ApiError: ApiError, get: get, post: post };
})(window);
