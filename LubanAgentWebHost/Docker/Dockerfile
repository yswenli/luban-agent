# LubanAgentWebHost 运行时镜像（Linux 容器）
#
# 构建上下文是 dotnet publish 的产物目录，不是仓库根目录：
#   dotnet publish LubanAgentWebHost/LubanAgentWebHost.csproj -c Release -r linux-x64 \
#       --self-contained false -o LubanAgentWebHost/bin/docker-publish/linux-x64
#   docker build -f LubanAgentWebHost/Docker/Dockerfile \
#       -t luban-agent-webhost:2.0.1 LubanAgentWebHost/bin/docker-publish/linux-x64
# 直接执行同目录的 build-image.ps1 可一步完成（含版本号推导与 .dockerignore 落位）。
#
# 基础镜像用 aspnet 而非 runtime：传递依赖 LuBan.Orm 与 UAParser.Core 声明了
# Microsoft.AspNetCore.App 框架引用，产物 runtimeconfig.json 因此把它列为必需框架；
# 换成 dotnet/runtime:10.0 会在启动前报「You must install or update .NET」。
# 这与 SAEA.MVC 无关——SAEA.MVC 自身只需要 Microsoft.NETCore.App。
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# git：工作区克隆与 Git 工具；libgomp1：ONNX Runtime 依赖；curl：HEALTHCHECK；
# tzdata：容器内本地时间格式化（TZ 生效的前提）；
# python3：脚本工具的 RunPython（PythonPath 默认 "python"，需在 .env 里指定为 python3）
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        git \
        libgomp1 \
        curl \
        ca-certificates \
        tzdata \
        python3 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# 单层写入并直接改属主：先 COPY 再 RUN chown 会把整个产物再复制一层，
# 镜像会凭空多出数百 MB。
COPY --chown=app:app . ./

# LUBAN_AGENT_HOME 决定数据库、工作区、JWT 密钥、插件等运行期文件的位置，统一放挂载卷。
# HOME 决定框架级 ~/.luban-agent（skills/rules/mcps）与 git 凭据的位置，跟随 LUBAN_AGENT_HOME
# 一起指向 /data 才能随卷持久化；两者都由 .env 覆盖（见同目录 .env.example）。
# 嵌入模型包不进镜像：首次启动按 LuBanAgent__Tools__Retrieval__DownloadUrl 下载到 /data/models。
ENV LUBAN_AGENT_HOME=/data \
    DOTNET_RUNNING_IN_CONTAINER=true \
    TZ=Asia/Shanghai

# 卷目录必须由运行用户持有，否则建库与写 self-describing 文件会失败
RUN mkdir -p /data && chown app:app /data

USER app

EXPOSE 18080
VOLUME ["/data"]

# 默认按 --http 启动（TLS 交给外层反向代理）；若改用容器内自签 HTTPS，
# 需同时改 CMD 与这里的协议，并保证 /data/webhost 下的证书可复用。
HEALTHCHECK --interval=30s --timeout=5s --start-period=90s --retries=3 \
    CMD curl -fsS "http://127.0.0.1:18080/system/health" || exit 1

ENTRYPOINT ["dotnet", "LubanAgentWebHost.dll"]
CMD ["--http", "--port", "18080"]