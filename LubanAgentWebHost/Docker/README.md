# LubanAgentWebHost Docker

本目录存放 WebHost 的容器化配置。镜像在**本机构建**：先把 .NET 产物发布到本地目录，再以该目录为上下文打镜像（Dockerfile 内不做还原与编译）。

| 文件 | 作用 |
|---|---|
| `Dockerfile` | 运行时镜像：`aspnet:10.0` + git/libgomp1/curl/tzdata/python3，非 root `app` 用户，`HEALTHCHECK` 探 `/system/health` |
| `.dockerignore` | 剔除 Playwright 驱动（252 MB）、嵌入模型包（52 MB）与调试符号，构建时由脚本复制到发布目录根部 |
| `build-image.ps1` | 一步完成 `dotnet publish` + `docker build`，镜像标签取 csproj `<Version>` |
| `docker-compose.yml` | 单容器编排：`.env` 注入、`/data` 数据卷、`/data/projects` 宿主机目录映射、端口 |
| `.env.example` | 运行期配置模板（容器内路径、模型下载地址、管理员密码、宿主端口与项目目录） |

## 环境要求

- .NET 10 SDK（发布用）
- Docker（Linux 容器模式）

## 构建

```powershell
# 在 LubanAgentWebHost/Docker 目录下
pwsh -File ./build-image.ps1
# 产物：luban-agent-webhost:<csproj 版本> 与 luban-agent-webhost:latest
```

脚本实际执行两步，等价命令：

```powershell
dotnet publish ..\LubanAgentWebHost.csproj -c Release -r linux-x64 --self-contained false `
    -o ..\bin\docker-publish\linux-x64 -p:DebugType=None -p:DebugSymbols=false
docker build -f Dockerfile -t luban-agent-webhost:2.0.1 ..\bin\docker-publish\linux-x64
```

ARM 宿主（如 Apple Silicon）加 `-Runtime linux-arm64`。

## 运行

先准备一份 `.env`（首次从模板复制，按需改端口、管理员密码、项目目录）：

```powershell
copy .env.example .env
```

```powershell
# 方式一：compose（推荐，自动建 /data 卷）
docker compose up -d
docker compose logs -f

# 方式二：docker run（--env-file 带入同一份 .env；端口需显式写）
docker run -d --name luban-agent-webhost -p 18080:18080 `
    --env-file .env `
    -v luban-agent-data:/data `
    -v ${PWD}/workspace:/data/projects `
    luban-agent-webhost:latest
```

启动后访问 `http://<宿主机IP>:18080/`。不设置 `LubanAgentWeb__AdminPassword` 时，内置管理员密码随机生成，只在首次启动日志里打印一次。

## 配置（`.env`）

`.env` 同时承担两件事：compose 用它做 `${VAR}` 插值，并通过 `env_file` 整份注入容器；`docker run` 用 `--env-file` 注入。**值一律不加引号**——compose 会剥引号而 `docker run --env-file` 不会，统一不加才能两边一致。

| 键 | 说明 |
|---|---|
| `LUBAN_WEBHOST_TAG` / `LUBAN_WEBHOST_PORT` | 镜像标签、宿主机端口（容器内固定 18080），仅 compose 插值用 |
| `LUBAN_WORKSPACE_HOST_DIR` | 宿主机项目目录，映射到容器内 `/data/projects` |
| `LUBAN_AGENT_HOME=/data` | 数据根：SQLite 库、模型、仓库、工作区、JWT 密钥、插件 |
| `HOME=/data` | 框架级 `~/.luban-agent`（skills/rules/mcps）与 git 凭据跟随它，必须也是 `/data` 才随卷持久化 |
| `LuBanLoggingOptions__Directory=/data/logs` | 滚动日志目录；镜像内默认是进程目录下的 `logs`（`/app` 对 `app` 用户只读） |
| `LuBanLoggingOptions__EnableConsole=true` | 镜像内默认 `false`；打开后 `docker logs` 才能看到框架日志 |
| `LuBanAgent__Tools__Retrieval__DownloadUrl` | 嵌入模型包地址，首启下载到 `/data/models`；留空则离线启动、检索功能自动禁用 |
| `LuBanAgent__Tools__Script__PythonPath=python3` | 脚本工具解释器；Debian 只有 `python3`，没有 `python` |
| `LubanAgentWeb__AdminPassword` | 内置管理员初始密码；留空随机生成 |
| `LubanAgentWeb__UseHttps=false` | 容器内走 HTTP，TLS 交给外层反向代理 |

## 数据、端口与挂载

- `/data`（`LUBAN_AGENT_HOME`）：SQLite 库、工作区、克隆的仓库、JWT 密钥、插件、嵌入模型与日志。容器重建只要保留该卷，数据就在。
- `/data/projects`：`LUBAN_WORKSPACE_HOST_DIR` 的挂载点。工作区根目录白名单默认就是 `/data`，所以在「设置 → 工作区」里填 `/data/projects/xxx` 就能直接新建，不用改白名单。Linux 宿主上该目录默认由守护进程以 root 创建，需先 `chown 1654`（容器内 `app` 用户的 uid）。
- 容器内固定监听 `18080`；宿主机端口用 `LUBAN_WEBHOST_PORT` 改。
- `.env` 缺失时 compose 直接报错——这是有意的：漏配 `LUBAN_AGENT_HOME` 会把数据写进容器层，容器一重建就全丢。`HOME=/data` 也依赖 `/data` 存在（镜像内已 `mkdir` 并 chown）；若挂载异常导致它不存在，`SpecialFolder.UserProfile` 会返回空串，框架级目录会退化成相对路径 `.luban-agent` 静默落到 `/app`。

## 嵌入模型

镜像**不带**模型包。首次启动时按 `LuBanAgent__Tools__Retrieval__DownloadUrl` 下载到 `/data/models/bge-small-zh-v1.5.zip`（实测 52.4 MB），解压到 `/data/models/bge-small-zh-v1.5/`；两个文件都留在卷里，容器重建不会重下。下载失败只记日志并禁用检索（LLM Wiki 知识库问答不可用），其余功能照常启动。

离线部署时把该键留空，并提前把 zip 或解压后的模型目录放进卷：

```powershell
# zip 会被优先采用（数据目录优先于程序目录）
docker cp bge-small-zh-v1.5.zip <容器名>:/data/models/
```

## HTTPS

镜像默认以 `--http` 启动，TLS 由外层反向代理（Nginx/Caddy/Traefik）终止。若坚持容器内自签 HTTPS：

```powershell
docker run ... luban-agent-webhost:latest --port 18080
```

去掉 `--http` 后宿主会启用自签证书并写 `/data/webhost`，浏览器首次访问需要信任该证书；compose 里还要把 `LubanAgentWeb__UseHttps` 改为 `true`（或删掉该行），并覆盖启动命令去掉 `--http`：

```yaml
    command: ["--port", "18080"]
```

## 已知限制（Linux 容器）

- **凭据加密不可用**：`DpapiCredentialProtector.IsAvailable` 只在 Windows 为 true，在设置页保存供应商/连接器密钥会报「当前平台不支持凭据加密」，请改用环境变量注入。
- **浏览器工具不可用**：镜像未带 Chromium 及 `libnss3` 等系统库；Playwright 驱动会被按需下载到 `$HOME/.luban-agent/playwright-driver`（`HOME=/data` 时即 `/data` 卷内），但没有浏览器可跑，需要该能力时自行建派生镜像安装依赖。
- **无 ssh 客户端**：镜像未装 `openssh-client`，`git@host:repo` 形式的远端无法克隆；HTTPS 远端正常。
- **git 身份需预置**：`GitToolPlugin` 不设置 `user.name/email`，容器内提交前先配置一次（`HOME=/data` 后写入 `/data/.gitconfig`，随卷保留）：
  ```powershell
  docker exec -it luban-agent-webhost git config --global user.name "Luban Agent"
  docker exec -it luban-agent-webhost git config --global user.email "agent@example.com"
  ```
- **Shell 工具走 bash/sh**：容器内没有 cmd/powershell，模型给出的 Windows 命令会失败；脚本工具用 `python3`。
- **路径语义**：工作区、附件、项目路径都是容器内路径，宿主机路径需先挂载。

## 运维

```powershell
# 离线重置密码（先停容器，再一次性运行；密码只打印一次）
docker compose stop
docker run --rm -v luban-agent-data:/data luban-agent-webhost:latest --reset-password yswenli

# 备份数据卷
docker run --rm -v luban-agent-data:/data -v ${PWD}:/backup alpine `
    tar czf /backup/luban-agent-data.tgz -C /data .
```

镜像约 476 MB（`docker images` 显示值，未压缩；实际构建上下文约 130 MiB，已剔除 Playwright 驱动 252.3 MiB 与模型包 52.4 MiB）。构建上下文与最终镜像都不含 `.playwright`、`EmbeddingModels/`、`*.pdb`。