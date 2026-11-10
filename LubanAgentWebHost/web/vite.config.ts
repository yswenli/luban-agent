import { defineConfig } from 'vite';
import solid from 'vite-plugin-solid';

// 开发模式下把后端接口前缀转发到 WebHost 实际端口；静态资源仍由 Vite 提供。
// 端口由 WebHost 的 --port / LubanAgentWeb:Port 决定，这里用环境变量覆盖，默认 8080。
const hostPort = process.env.VITE_HOST_PORT ?? '8080';

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
  '/a2a',
  '/llmwiki',
];

const proxy = Object.fromEntries(
  apiPrefixes.map((prefix) => [
    prefix,
    { target: `http://127.0.0.1:${hostPort}`, changeOrigin: true },
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