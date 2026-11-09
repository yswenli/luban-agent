/*
 * 占位文件：真实内容由 LubanAgentWebHost 启动时按当前用户与令牌重写同名文件。
 * 保留此占位是为了让构建期能解析 HTML 中的 <script src="/runtime-config.js">，
 * 避免 Vite 把它当成需要打包的入口；运行时永远以宿主写入的内容为准。
 */
window.__LUBAN__ = window.__LUBAN__ || {};