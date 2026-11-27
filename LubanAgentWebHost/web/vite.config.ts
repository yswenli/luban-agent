import { defineConfig } from 'vite';
import solid from 'vite-plugin-solid';

// 开发模式下把后端接口前缀转发到 WebHost 实际端口；静态资源仍由 Vite 提供。
// 端口由 WebHost 的 --port / LubanAgentWeb:Port 决定，这里用环境变量覆盖，默认与宿主默认端口一致。
const hostPort = process.env.VITE_HOST_PORT ?? '18080';

// 宿主默认以 HTTPS 提供服务（自签证书），代理必须按同一协议连上游，否则握手直接失败；
// 用 --http 起宿主时把 VITE_HOST_PROTOCOL 设为 http。
// 自签证书不被 Node 信任，https 上游必须关掉 secure（仅本次开发代理生效，与浏览器侧的证书信任无关）。
const hostProtocol = process.env.VITE_HOST_PROTOCOL ?? 'https';

// 开发模式下浏览器直连 Vite、拿不到宿主换发的 Cookie，故把「宿主令牌」填进 VITE_HOST_TOKEN，
// 由代理在转发时补 Authorization 头；生产模式不读这个变量。
// 注意：浏览器入口里的 ?t= 是会话令牌，只能换 Cookie，不能当 Bearer 用；
// 宿主令牌取自 ~/.luban-agent/webhost.json 的 token，或启动宿主时用 --host-token 指定的值。
const hostToken = process.env.VITE_HOST_TOKEN;
const hostAuthHeaders = hostToken ? { Authorization: `Bearer ${hostToken}` } : undefined;

const apiPrefixes = [
  '/agent',
  '/session',
  '/workspace',
  '/system',
  '/attachment',
  '/provider',
  '/skill',
  '/rule',
  '/mcp',
  '/model',
  '/llmwiki',
  '/plugin',
  '/plugin-web',
  '/file',
];

const proxy = Object.fromEntries(
  apiPrefixes.map((prefix) => [
    prefix,
    {
      target: `${hostProtocol}://127.0.0.1:${hostPort}`,
      changeOrigin: true,
      secure: false,
      headers: hostAuthHeaders,
    },
  ]),
);

export default defineConfig({
  // 产物由 SAEA 以站点根目录托管，但 index.html 里仍用相对路径更稳（支持子目录部署）
  base: './',
  plugins: [solid()],
  build: {
    // 产物直接落到宿主工程的静态根目录，dotnet build 时再由 csproj 拷到输出目录
    outDir: '../wwwroot',
    emptyOutDir: true,
    target: 'es2022',
    // 不产出 .json / .webmanifest：SAEA 的静态 MIME 表没有对应映射
    assetsInlineLimit: 4096,
  },
  server: {
    port: 5173,
    strictPort: false,
    proxy,
  },
});