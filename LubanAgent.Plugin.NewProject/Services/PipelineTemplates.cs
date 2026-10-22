/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： PipelineTemplates
*创建时间：2026/10/22
*描述：Step 7 流水线模板库与渲染器（PRD §4.3 Step 7，v0.30 修订）
*****************************************************************************/

using System.Text;
using System.Text.RegularExpressions;
using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Configuration;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 三端流水线模板库：<b>内嵌新版 YAML</b>（云效 OpenAPI 只收新版 YAML，见 PRD §4.11.3 注⑤）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么内嵌 C# 常量而不是包内 yaml 文件</b>：模板随程序集分发，不依赖铺包与 ALC 路径解析，
/// 且可直接单测（真机验收前先由单测锁住「三端结构 + 关键节点」）。
/// </para>
/// <para>
/// <b>模板来源</b>：按企服版 TJCES 三端参考流水线（<c>pipelineReferences</c>）的 <b>tb 形态语义</b>翻译为新版 YAML。
/// 已核的官方语法：<c>sources</c> / <c>stages.&lt;id&gt;.jobs.&lt;id&gt;.{name,runsOn,steps|component}</c> /
/// <c>steps.&lt;id&gt;.{step,name,with}</c> / 顶层 <c>variables:</c>（<b>只写 <c>key</c> / <c>value</c>，可选 <c>secret</c></b>）/
/// 顶层 <c>cache:</c>（<c>cacheDirs</c> + <c>cacheMode</c>）；步骤名取自官方「步骤 steps 清单」
/// （<c>Command</c> / <c>NodeBuild</c> / <c>OSSUpload</c> / <c>OSSDelete</c>），组件名取自「组件 component 清单」
/// （<c>VMDockerDeploy</c> —— 主机 Docker 部署，对应企服版 <c>docker-deploy</c>）。
/// </para>
/// <para>
/// <b>真机校正（2026-10-22，8 轮写探针 + 官方组件文档）</b>：
/// ① 变量<b>不能</b>带 <c>type</c>（写了报 <c>Not supported variable type</c>），私密值用 <c>secret: true</c>；
/// ② <c>Command</c> / <c>NodeBuild</c> 必填 <c>with.run</c>；
/// ③ <c>NodeBuild</c> 用 <c>versionType</c> + <c>customNodeVersion</c>（企服版 Node 16 ⇒ <c>lts/gallium</c>）；
/// ④ <c>OSSUpload</c> 的源 / 目标目录键是 <c>sourceFilePath</c> / <c>targetFilePath</c>（<b>不是</b> <c>localPath</c> / <c>prefix</c>），
/// <c>OSSDelete</c> 的目录键是 <c>prefix</c>，两者都必填 <c>region</c>；
/// ⑤ <c>runsOn</c> 三形态：默认环境标量 <c>public/cn-beijing</c>、容器 <c>{group, container}</c>、VM <c>{group, labels, vm}</c>
/// —— <b>私有集群任何写法都被 OpenAPI 校验拒绝</b>（报「runsOn {} 不存在」），故模板默认<b>不写</b> <c>runsOn</c>
/// （＝云效默认北京公共集群），私有集群值交由人工在 <c>pipelineDefaults[].buildCluster</c> 回填；
/// ⑥ <c>VMDockerDeploy.machineGroup</c> 必须是<b>主机组 uuid</b>（tb 数字 id 被拒）。
/// </para>
/// </remarks>
public static class PipelineTemplateLibrary
{
    /// <summary>
    /// 构建阶段模板（新版 YAML 片段：<c>sources</c> + <c>variables</c> + <c>cache</c> + <c>stages</c>）。
    /// </summary>
    /// <param name="end">端。</param>
    /// <returns>含占位符的新版 YAML。</returns>
    public static string BuildStageYaml(ProjectEnd end) => end switch
    {
        ProjectEnd.Service => ServiceYaml,
        ProjectEnd.Admin => AdminYaml,
        ProjectEnd.Mobile => MobileYaml,
        _ => throw new ArgumentOutOfRangeException(nameof(end), end, "未知的端，无法给出流水线模板。")
    };

    /// <summary>
    /// 部署阶段模板（<b>不</b>写进首次创建的 YAML，见 PRD §6 #14「部署阶段默认禁用」）。
    /// </summary>
    /// <param name="end">端。</param>
    /// <returns>部署阶段 YAML 片段；该端<b>没有</b>独立部署阶段时为 <c>null</c>。</returns>
    /// <remarks>
    /// 仅后端有独立部署阶段（企服版参考流水线取证：管理端 / 移动端的 OSS 上传即发布，无部署阶段）。
    /// </remarks>
    public static string? DeployStageYaml(ProjectEnd end) => end == ProjectEnd.Service ? ServiceDeployYaml : null;

    /// <summary>
    /// 部署阶段的注释块（附在首次创建 YAML 的末尾，供用户对照人工在控制台补齐）。
    /// </summary>
    /// <param name="end">端。</param>
    /// <returns>以 <c>#</c> 开头的注释块；该端没有部署阶段时说明原因。</returns>
    public static string DeployStageComment(ProjectEnd end)
    {
        var body = DeployStageYaml(end);
        var note = new StringBuilder();
        note.AppendLine("# ⚠ 本流水线**只含构建阶段**，部署阶段按 PRD §6 #14「默认禁用」未写入。");
        note.AppendLine("#    确认下列资源已绑定后，用界面上的「一键补齐部署阶段」追加部署阶段（或直接在此处解除注释后提交）：");

        if (body is null)
        {
            note.AppendLine("#    · 该端没有独立部署阶段：构建产物上传 OSS 即发布（企服版参考流水线亦如此）。");
            return note.ToString();
        }

        foreach (var line in body.Split('\n'))
            // 前缀「# 」之外再补 2 空格：片段是「顶格写的独立 stage」，解除注释后必须挂在顶层 stages: 之下
            // （stage id 缩进 2 空格），否则会变成顶层 deploy: 键、云效 YAML 校验必失败。
            note.Append("#   ").AppendLine(line.TrimEnd('\r'));

        return note.ToString();
    }

    /// <summary>
    /// 该端「待人工绑定清单」（PRD §6 #14 第 5 项：服务连接 / 主机组 / 构建集群 / 制品仓库 / 环境变量组）。
    /// </summary>
    /// <remarks>
    /// 条目按 <b>2026-10-22 真机取证</b>校正：私有构建集群与主机组<b>不能</b>由 OpenAPI 校验通过
    /// （分别报「runsOn {} 不存在」「该机器组uuid[x]不存在」），必须由人工在控制台确认后回填配置，故一律列在此处。
    /// </remarks>
    /// <param name="end">端。</param>
    /// <returns>含占位符的清单条目。</returns>
    public static IReadOnlyList<string> PendingBindings(ProjectEnd end) => end switch
    {
        ProjectEnd.Service => new[]
        {
            "服务连接（代码源 codeup）：{codeupSc} —— 确认该项目仓库在授权范围内",
            "构建阶段 runsOn：{cluster} —— 留空＝云效默认北京公共集群（只跑 docker build，需构建机可用 docker daemon）；"
                + "企服版用的是私有集群，请在 pipelineDefaults[Service].buildCluster 填 private/<集群ID>"
                + "（OpenAPI 对私有集群一律报「runsOn 不存在」，以控制台实际值为准）",
            "Harbor 凭据：变量 HARBOR_USER / HARBOR_PASSWORD 出厂为空 —— 请在控制台流水线「变量和缓存」补齐（HARBOR_PASSWORD 已标 secret: true）",
            "部署主机组：deployMachineGroup={machineGroup} —— **必须是主机组 uuid**"
                + "（真机核：企服版 tb 的数字 id 226948 会被拒，报「该机器组uuid[226948]不存在」）；"
                + "绑定完成后用界面「一键补齐部署阶段」并入部署阶段（组件 VMDockerDeploy）",
            "代码源事件触发：triggerEvents 为空 —— 需要 push 自动构建时请在控制台开启"
        },
        ProjectEnd.Admin => new[]
        {
            "服务连接（OSS）：{ossSc} —— 确认授权范围含 region {ossRegion} / bucket {ossBucket}",
            "OSS 目录：上传源 sourceFilePath='{uploadPath}' → 远端 targetFilePath='{ossPrefix}'（OSSDelete 以同一前缀做目录清理）",
            "构建集群：模板不写 runsOn ⇒ 云效默认公共北京集群（企服版用公共集群 K8S-4）；如需私有集群请在 pipelineDefaults[].buildCluster 填 private/<集群ID>",
            "Node 运行时：versionType=custom + customNodeVersion=lts/gallium（对齐企服版 CI_RUNTIME_VERSION，即 Node 16）",
            "代码源事件触发：triggerEvents 为空 —— 需要 push 自动构建时请在控制台开启"
        },
        ProjectEnd.Mobile => new[]
        {
            "服务连接（OSS）：{ossSc} —— 确认授权范围含 region {ossRegion} / bucket {ossBucket}",
            "OSS 目录：上传源 sourceFilePath='{uploadPath}' → 远端 targetFilePath='{ossPrefix}'（OSSDelete 以同一前缀做目录清理）",
            "构建集群：模板不写 runsOn ⇒ 云效默认公共北京集群（企服版用公共集群 K8S-4）；如需私有集群请在 pipelineDefaults[].buildCluster 填 private/<集群ID>",
            "Node 运行时：versionType=custom + customNodeVersion=lts/gallium（对齐企服版 CI_RUNTIME_VERSION，即 Node 16）",
            "代码源事件触发：triggerEvents 为空 —— 需要 push 自动构建时请在控制台开启"
        },
        _ => Array.Empty<string>()
    };

    // ── 后端：构建 + 推送 Harbor（对应企服版「Command 设 hosts → DockerLogin → Command docker build/tag/push」）──
    private const string ServiceYaml = """
        name: {pipelineName}

        sources:
          repo:
            type: codeup
            name: {repository}
            endpoint: {endpoint}
            branch: {branch}
            # 建完不自动运行（PRD §4.11.3 注⑥）；需要 push 自动构建时请在控制台开启事件触发。
            triggerEvents: []
            certificate:
              type: serviceConnection
              serviceConnection: {codeupSc}

        # 「变量和缓存」段（新版 YAML 口径）：
        #   · 变量只写 key / value（可选 secret）——真机核：type 字段不被支持，写了直接 400：
        #     「Not supported variable type」。
        #   · 缓存写 cacheDirs + cacheMode（remote 走云效托管缓存；私有集群可用 local）。
        variables:
          - key: DOCKER_IMAGE_NAME
            value: '{image}'
          - key: DOCKER_IMAGE_TAG
            value: '{imageTag}'
          - key: DOCKER_IMAGE_BASE_VERSION
            value: 'v1.0'
          - key: DOCKER_IMAGE_LOCAL_TAG
            value: 'local-latest'
          - key: DOCKER_REPO_NAME
            value: '{dockerRepo}'
          - key: DOCKER_CONTAINER_NAME
            value: '{container}'
          - key: APP_PORT
            value: '{port}'
          - key: APP_ENTRY
            value: 'WebHost.dll'
          - key: SOLUTION_PATH
            value: './WebHost/WebHost.csproj'
          - key: HARBOR_HOST
            value: '{harborHost}'
          - key: HARBOR_USER
            value: ''
          - key: HARBOR_PASSWORD
            value: ''
            secret: true

        cache:
          cacheDirs:
            - /root/.m2
            - /root/.gradle/caches
            - /root/.npm
            - /root/.yarn
            - /go/pkg/mod
            - /root/.cache
          cacheMode: {cacheMode}

        stages:
          build:
            name: 构建
            jobs:
              build:
                name: 构建并推送镜像
        {runsOn}
                steps:
                  login_harbor:
                    step: Command
                    name: 登录 Harbor
                    with:
                      run: |
                        set -e
                        echo "$HARBOR_PASSWORD" | docker login -u "$HARBOR_USER" --password-stdin "$HARBOR_HOST"
                  build_image:
                    step: Command
                    name: 构建并推送镜像
                    with:
                      run: |
                        set -e
                        cd "$PROJECT_DIR"
                        docker build -f WebHost/Dockerfile \
                          -t "$HARBOR_HOST/$DOCKER_REPO_NAME/$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG" .
                        docker push "$HARBOR_HOST/$DOCKER_REPO_NAME/$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"
        """;

    // ── 后端部署阶段（默认不写入，人工确认后由「一键补齐部署阶段」追加）──
    // 组件口径（Y1 裁决 + 官方组件文档「主机 Docker 部署 VMDockerDeploy」，与企服版 docker-deploy 语义一一对应）：
    //   machineGroup 必填，且**必须是主机组 uuid**（真机核：tb 的数字 id 226948 报「该机器组uuid[226948]不存在」）；
    //   executeUser 默认 root；run 为部署脚本；变量沿用流水线级 variables。
    private const string ServiceDeployYaml = """
        deploy:
          name: 部署
          jobs:
            deploy:
              name: 部署到主机组
              component: VMDockerDeploy
              with:
                machineGroup: {machineGroup}
                executeUser: root
                run: |
                  set -e
                  echo "$HARBOR_PASSWORD" | docker login -u "$HARBOR_USER" --password-stdin "$HARBOR_HOST"
                  docker pull "$HARBOR_HOST/$DOCKER_REPO_NAME/$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"
                  docker rm -f "$DOCKER_CONTAINER_NAME" || true
                  docker run -d --name "$DOCKER_CONTAINER_NAME" -p "$APP_PORT:$APP_PORT" \
                    -e ASPNETCORE_ENVIRONMENT=dev \
                    "$HARBOR_HOST/$DOCKER_REPO_NAME/$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"
        """;

    // ── 管理端：Node 构建 + OSS 上传（对应企服版 node_build + OSS delete/upload）──
    // 真机校正（2026-10-22，依据官方组件文档「NodeBuild / OSSDelete / OSSUpload」）：
    //   · NodeBuild：写 versionType + customNodeVersion（对齐企服版 VERSION_TYPE=CUSTOM / CI_RUNTIME_VERSION=lts/gallium，
    //     即 Node 16 运行时）；run 必填。
    //   · OSSDelete：serviceConnection / region / bucket 必填，待删目录键是 **prefix**。
    //   · OSSUpload：serviceConnection / region / bucket / sourceFilePath / targetFilePath 必填
    //     —— 注意键名不是 localPath / prefix（历史写法，真机被静默忽略）。
    //   · 不写 runsOn ⇒ 走云效默认公共构建集群（企服版 K8S-4 同口径）；如需私有集群改 pipelineDefaults[].buildCluster。
    private const string AdminYaml = """
        name: {pipelineName}

        sources:
          repo:
            type: codeup
            name: {repository}
            endpoint: {endpoint}
            branch: {branch}
            # 建完不自动运行（PRD §4.11.3 注⑥）；需要 push 自动构建时请在控制台开启事件触发。
            triggerEvents: []
            certificate:
              type: serviceConnection
              serviceConnection: {codeupSc}

        cache:
          cacheDirs:
            - /root/.npm
            - /root/.cache
          cacheMode: {cacheMode}

        stages:
          build:
            name: 阶段1
            jobs:
              build:
                name: node 编译并上传 OSS
                steps:
                  node_build:
                    step: NodeBuild
                    name: Node.js 构建
                    with:
                      versionType: custom
                      customNodeVersion: lts/gallium
                      run: |
                        set -e
                        npm config set registry https://registry.npmmirror.com
                        npm install
                        npm run build:dev
                  oss_delete:
                    step: OSSDelete
                    name: 清理 OSS 远端目录
                    with:
                      serviceConnection: {ossSc}
                      region: {ossRegion}
                      bucket: '{ossBucket}'
                      prefix: '{ossPrefix}'
                  oss_upload:
                    step: OSSUpload
                    name: 上传构建产物到 OSS
                    with:
                      serviceConnection: {ossSc}
                      region: {ossRegion}
                      bucket: '{ossBucket}'
                      sourceFilePath: '{uploadPath}'
                      targetFilePath: '{ossPrefix}'
        """;

    // ── 移动端：与管理端同构，仅 OSS 前缀与产物目录不同 ──
    // 口径同管理端（NodeBuild versionType/customNodeVersion + OSSDelete prefix + OSSUpload sourceFilePath/targetFilePath）。
    private const string MobileYaml = """
        name: {pipelineName}

        sources:
          repo:
            type: codeup
            name: {repository}
            endpoint: {endpoint}
            branch: {branch}
            # 建完不自动运行（PRD §4.11.3 注⑥）；需要 push 自动构建时请在控制台开启事件触发。
            triggerEvents: []
            certificate:
              type: serviceConnection
              serviceConnection: {codeupSc}

        cache:
          cacheDirs:
            - /root/.npm
            - /root/.cache
          cacheMode: {cacheMode}

        stages:
          build:
            name: 阶段1
            jobs:
              build:
                name: node 编译并上传 OSS
                steps:
                  node_build:
                    step: NodeBuild
                    name: Node.js 构建
                    with:
                      versionType: custom
                      customNodeVersion: lts/gallium
                      run: |
                        set -e
                        npm config set registry https://registry.npmmirror.com
                        npm install
                        npm run build:dev
                  oss_delete:
                    step: OSSDelete
                    name: 清理 OSS 远端目录
                    with:
                      serviceConnection: {ossSc}
                      region: {ossRegion}
                      bucket: '{ossBucket}'
                      prefix: '{ossPrefix}'
                  oss_upload:
                    step: OSSUpload
                    name: 上传构建产物到 OSS
                    with:
                      serviceConnection: {ossSc}
                      region: {ossRegion}
                      bucket: '{ossBucket}'
                      sourceFilePath: '{uploadPath}'
                      targetFilePath: '{ossPrefix}'
        """;
}

/// <summary>
/// 流水线渲染结果。
/// </summary>
public sealed class PipelineRenderResult
{
    /// <summary>流水线名称（<c>pipelineNameFormat</c> 展开）。</summary>
    public string PipelineName { get; set; } = string.Empty;

    /// <summary>可直接提交云效的<b>新版 YAML</b>（顶层含 <c>name</c> / <c>sources</c> / <c>stages</c>）。</summary>
    public string Yaml { get; set; } = string.Empty;

    /// <summary>待人工绑定清单（已展开占位符）。</summary>
    public IReadOnlyList<string> PendingBindings { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 展开后的<b>部署阶段片段</b>（该端没有独立部署阶段时为 <c>null</c>）。
    /// </summary>
    /// <remarks>
    /// 首次创建时它只以注释块形式附在 <see cref="Yaml"/> 末尾；用户手工微调后走「一键补齐部署阶段」时，
    /// 由 <see cref="PipelineYamlOverride.Compose"/> 把它追加到微调文本之后。
    /// </remarks>
    public string? DeployStageYaml { get; set; }
}

/// <summary>
/// 流水线 YAML 渲染器：把「项目档案 + 按端仓库 + 插件配置」展开成可提交的新版 YAML。
/// </summary>
/// <remarks>
/// 纯函数、无 IO，便于单测直接锁住三端输出（PRD §4.3 Step 7）。
/// </remarks>
public static class PipelineRenderer
{
    /// <summary>
    /// 渲染某端流水线。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="repository">该端目标仓库（Step 3 产物：名称 / 克隆地址 / 默认分支）。</param>
    /// <param name="settings">插件配置（名称格式 / 推送分支 / 按端默认值）。</param>
    /// <param name="includeDeployStage">是否包含部署阶段（默认 <c>false</c>，见 PRD §6 #14）。</param>
    /// <returns>渲染结果（名称 / YAML / 待绑定清单）。</returns>
    public static PipelineRenderResult Render(
        ProjectSpec spec,
        EndRepository repository,
        NewProjectSettings settings,
        bool includeDeployStage = false)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);

        var defaults = settings.FindPipelineDefault(repository.End) ?? new PipelineDefault { End = repository.End };
        var tokens = BuildTokens(spec, repository, settings, defaults);

        var name = Expand(settings.PipelineNameFormat, tokens);
        if (string.IsNullOrWhiteSpace(name))
            name = $"{spec.Code.ToUpperInvariant()}_DEV_Deploy_{repository.RepositoryName}";

        tokens["{pipelineName}"] = name;

        var yaml = new StringBuilder(Expand(PipelineTemplateLibrary.BuildStageYaml(repository.End), tokens).TrimEnd());

        // 部署阶段片段（展开后）一并带出：首次创建只把注释块附在末尾；
        // 补齐时若用户手工微调过 YAML，由能力层用 Compose 把它追加到微调文本之后。
        // 注意缩进：片段必须挂在顶层 `stages:` 之下（stage id 缩进 2 空格），
        // 否则拼出来的是顶层 `deploy:` 键（v0.30 的片段未缩进，属未被真机覆盖的潜在结构错误）。
        var deployFragment = PipelineTemplateLibrary.DeployStageYaml(repository.End);
        if (deployFragment is not null)
            deployFragment = Indent(Expand(deployFragment, tokens).TrimEnd(), 2);

        if (includeDeployStage)
        {
            if (deployFragment is not null)
                yaml.AppendLine().AppendLine().Append(deployFragment);
        }
        else
        {
            yaml.AppendLine().AppendLine().Append(Expand(PipelineTemplateLibrary.DeployStageComment(repository.End), tokens).TrimEnd());
        }

        var pending = PipelineTemplateLibrary.PendingBindings(repository.End)
            .Select(item => Expand(item, tokens))
            .ToArray();

        return new PipelineRenderResult
        {
            PipelineName = name,
            Yaml = yaml.ToString(),
            PendingBindings = pending,
            DeployStageYaml = deployFragment
        };
    }

    /// <summary>
    /// 组装占位符表：先项目级令牌（<c>{code}</c> 等），再按端默认值展开为具体值。
    /// </summary>
    /// <param name="spec">项目档案。</param>
    /// <param name="repository">该端仓库。</param>
    /// <param name="settings">插件配置。</param>
    /// <param name="defaults">该端默认值。</param>
    /// <returns>占位符 → 值。</returns>
    private static Dictionary<string, string> BuildTokens(
        ProjectSpec spec,
        EndRepository repository,
        NewProjectSettings settings,
        PipelineDefault defaults)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{code}"] = spec.Code ?? string.Empty,
            ["{Code}"] = Capitalize(spec.Code),
            ["{CODE}"] = (spec.Code ?? string.Empty).ToUpperInvariant(),
            ["{name}"] = spec.Name ?? string.Empty,
            ["{end}"] = NewProjectSettings.EndSuffix(repository.End),
            ["{repository}"] = repository.RepositoryName,
            ["{endpoint}"] = repository.CloneUrl
        };

        // 与 Step 6 推送分支同口径：配置优先，其次仓库默认分支，最后 main（PRD §6 #17）。
        tokens["{branch}"] = !string.IsNullOrWhiteSpace(settings.PushBranch)
            ? settings.PushBranch
            : string.IsNullOrWhiteSpace(repository.DefaultBranch) ? "main" : repository.DefaultBranch;

        // 按端默认值里的字段本身可含项目级令牌（如 containerName = "{code}-service"），故先展开一轮。
        tokens["{port}"] = defaults.AppPort;
        tokens["{container}"] = Expand(defaults.ContainerName, tokens);
        tokens["{image}"] = Expand(defaults.ImageName, tokens);
        tokens["{imageTag}"] = string.IsNullOrWhiteSpace(defaults.ImageTag) ? "latest" : defaults.ImageTag;
        tokens["{dockerRepo}"] = Expand(
            string.IsNullOrWhiteSpace(defaults.DockerRepo) ? "{branch}" : defaults.DockerRepo, tokens);
        tokens["{harborHost}"] = string.IsNullOrWhiteSpace(defaults.HarborHost) ? "harbor.tjingcai.com" : defaults.HarborHost;

        // ── runsOn（X3 裁决）───────────────────────────────────────────────
        // 默认不写 runsOn ⇒ 云效默认北京公共集群（真机 200）。原因：OpenAPI 对<b>私有</b>集群一律报
        // 「runsOn {} 不存在」，故私有集群值只能由人工在 pipelineDefaults[].buildCluster 回填；
        // 回填值<b>整串照写</b>（形如 private/<集群ID> 或 public/cn-beijing），模板不再硬拼 private/ 前缀。
        // 缩进为 jobs.<id> 同级（8 空格），空值时整行留空（YAML 允许空行）。
        var buildCluster = (defaults.BuildCluster ?? string.Empty).Trim();
        tokens["{cluster}"] = string.IsNullOrWhiteSpace(buildCluster) ? "（未配置：走云效默认公共北京集群）" : buildCluster;
        tokens["{runsOn}"] = string.IsNullOrWhiteSpace(buildCluster) ? string.Empty : $"        runsOn: {buildCluster}";

        tokens["{codeupSc}"] = defaults.CodeupServiceConnection;
        tokens["{ossSc}"] = defaults.OssServiceConnection;
        tokens["{ossBucket}"] = defaults.OssBucket;
        tokens["{ossPrefix}"] = Expand(defaults.OssPrefix, tokens);
        tokens["{uploadPath}"] = defaults.UploadPath;

        // ── 部署资源（Y1 裁决）─────────────────────────────────────────────
        // VMDockerDeploy.machineGroup 必须是<b>主机组 uuid</b>；未配置时给可读占位（而不是空值），
        // 让 YAML 预览 / 注释块里就能看出「这里还缺一个 uuid」；真正补齐前能力层还会再拦一次（见 NewProjectService）。
        tokens["{machineGroup}"] = string.IsNullOrWhiteSpace(defaults.DeployMachineGroup)
            ? "'<待绑定：主机组 uuid>'"
            : $"'{defaults.DeployMachineGroup.Trim()}'";

        // OSS 区域（真机核：企服版 region=cn-shanghai）；OSS 步骤必填该字段。
        tokens["{ossRegion}"] = string.IsNullOrWhiteSpace(defaults.OssRegion) ? "cn-shanghai" : defaults.OssRegion.Trim();

        // 缓存模式口径：私有构建集群支持 local（企服版私有集群 runnerCacheMode=local），公共集群用 remote。
        tokens["{cacheMode}"] = buildCluster.StartsWith("private", StringComparison.OrdinalIgnoreCase) ? "local" : "remote";

        return tokens;
    }

    /// <summary>
    /// 按占位符表展开文本（<b>长键优先</b>，避免短键先替换吃掉长键）。
    /// </summary>
    /// <param name="text">含占位符的文本。</param>
    /// <param name="tokens">占位符表。</param>
    /// <returns>展开后的文本。</returns>
    internal static string Expand(string? text, IReadOnlyDictionary<string, string> tokens)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = text;
        foreach (var pair in tokens.OrderByDescending(item => item.Key.Length))
        {
            if (pair.Key.Length == 0)
                continue;

            result = result.Replace(pair.Key, pair.Value ?? string.Empty, StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>首字母大写（<c>{Code}</c> 令牌口径）。</summary>
    /// <param name="code">缩写。</param>
    /// <returns>首字母大写后的缩写。</returns>
    private static string Capitalize(string? code)
        => string.IsNullOrEmpty(code) ? string.Empty : char.ToUpperInvariant(code[0]) + code[1..];

    /// <summary>
    /// 把整段文本统一缩进若干空格（空行不补空格）。
    /// </summary>
    /// <param name="text">原文。</param>
    /// <param name="spaces">缩进空格数。</param>
    /// <returns>缩进后的文本。</returns>
    /// <remarks>
    /// 用途：部署阶段片段在模板常量里是「顶格写的独立片段」，拼进完整 YAML 时必须挂在顶层 <c>stages:</c> 之下
    /// （stage id 缩进 2 空格）——否则会变成顶层 <c>deploy:</c> 键，云效 YAML 校验必失败。
    /// </remarks>
    private static string Indent(string text, int spaces)
    {
        if (string.IsNullOrEmpty(text) || spaces <= 0)
            return text;

        var pad = new string(' ', spaces);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return string.Join("\n", lines.Select(line => line.Length == 0 ? line : pad + line));
    }
}

/// <summary>
/// Step 7 手工微调（PRD §4.3 Step 7 v0.31）：把用户在编辑器里改过的 YAML 与模板渲染结果合并，并做提交前的最小校验。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不上 YAML 解析库</b>：插件不引入额外依赖，真正的语法校验交给云效——非法 YAML 由服务端返回
/// <c>1209300</c>，文案原样透出。这里只挡两类「必然白跑一次」的低级错误：改坏顶层结构（缺 <c>stages:</c>）
/// 与改掉流水线名（同名幂等检索与档案名称会和远端对不上）。
/// </para>
/// <para>
/// <b>补齐语义</b>：手工微调只作用在构建段；「一键补齐部署阶段」会在微调文本末尾追加部署阶段片段，
/// 若微调文本里已经自己写了 <c>VMDockerDeploy</c>（用户自备部署阶段）或已存在 <c>deploy</c> 阶段（换了组件也算），
/// 则不再重复追加——同 id 阶段追加两次会拼出重复键、云效校验必失败。
/// </para>
/// </remarks>
public static class PipelineYamlOverride
{
    /// <summary>顶层 <c>name:</c> 行（新版 YAML 的首行）。</summary>
    private static readonly Regex NamePattern = new(@"^name:[ \t]*(?<value>.+?)[ \t]*$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>顶层 <c>stages:</c> 行（允许行尾注释）。</summary>
    private static readonly Regex StagesPattern = new(@"^stages:[ \t]*(#.*)?$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// 已存在的 <c>deploy</c> 阶段行（stage id 缩进 1–4 空格；步骤 id 缩进更深，不会误判）。
    /// </summary>
    /// <remarks>
    /// 用于「补齐不重复追加」：用户若自己写了 <c>deploy</c> 阶段（哪怕换了组件），再追加一个同 id 阶段
    /// 会拼出重复键、云效校验必失败。
    /// </remarks>
    private static readonly Regex DeployStagePattern = new(@"^[ \t]{1,4}deploy:[ \t]*$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// 合并「模板渲染结果」与「用户手工微调」：无微调即原样返回模板结果；有微调时以微调文本为准。
    /// </summary>
    /// <param name="renderedYaml">模板渲染出的完整 YAML（首次创建口径）。</param>
    /// <param name="overrideYaml">用户手工微调的原文；<c>null</c>／空白表示未微调。</param>
    /// <param name="deployStageYaml">展开后的部署阶段片段；该端无部署阶段或本次不补齐时为 <c>null</c>。</param>
    /// <returns>最终要提交给云效的 YAML。</returns>
    /// <remarks>
    /// 不做语法修复：微调文本原样提交（只统一换行符、去掉末尾空白），保证「界面看到的与提交的一致」。
    /// <paramref name="deployStageYaml"/> 由 <c>PipelineRenderer</c> 给出（已按 stage 缩进 2 空格）。
    /// </remarks>
    public static string Compose(string renderedYaml, string? overrideYaml, string? deployStageYaml)
    {
        if (string.IsNullOrWhiteSpace(overrideYaml))
            return renderedYaml;

        var text = Normalize(overrideYaml);
        if (string.IsNullOrWhiteSpace(deployStageYaml)
            || text.Contains("VMDockerDeploy", StringComparison.OrdinalIgnoreCase)
            || DeployStagePattern.IsMatch(text))
        {
            return text.TrimEnd();
        }

        return text.TrimEnd() + "\n\n" + Normalize(deployStageYaml).TrimEnd();
    }

    /// <summary>
    /// 提交前的最小校验：非空 / 不含 Tab / 有顶层 <c>stages:</c> / <c>name:</c> 与模板渲染名一致。
    /// </summary>
    /// <param name="yaml">待提交 YAML。</param>
    /// <param name="expectedName">模板渲染出的流水线名（同名幂等检索依赖它）。</param>
    /// <returns>不合规时的可读文案；合规返回 <c>null</c>。</returns>
    public static string? Validate(string? yaml, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return "YAML 不能为空：请点「恢复模板」重置后再微调。";

        if (yaml.Contains('\t'))
            return "YAML 里不能出现 Tab：云效用空格缩进，请把 Tab 换成空格后重试。";

        if (!StagesPattern.IsMatch(yaml))
            return "YAML 缺少顶层 stages:（云效只收顶层 stages: 的新版 YAML）：请点「恢复模板」，只改内容、不改结构。";

        var name = ReadName(yaml);
        if (string.IsNullOrWhiteSpace(name))
            return $"YAML 缺少顶层 name:（应保持为 {expectedName}）：请点「恢复模板」后重试。";

        if (!string.Equals(name, expectedName, StringComparison.Ordinal))
        {
            return $"流水线名不能改：YAML 里的 name 是「{name}」，期望「{expectedName}」。"
                   + "改名会让同名幂等检索与档案里的名称对不上；确需改名请改插件配置 pipelineNameFormat 后重跑本步。";
        }

        return null;
    }

    /// <summary>
    /// 读取顶层 <c>name:</c> 的值（去掉成对引号）。
    /// </summary>
    /// <param name="yaml">YAML 原文。</param>
    /// <returns>流水线名；未找到返回 <c>null</c>。</returns>
    public static string? ReadName(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return null;

        var match = NamePattern.Match(yaml);
        if (!match.Success)
            return null;

        var value = match.Groups["value"].Value.Trim();
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            value = value[1..^1].Trim();

        return value.Length == 0 ? null : value;
    }

    /// <summary>统一换行符为 <c>\n</c>（提交与落档案同形，跨平台比较也不飘）。</summary>
    /// <param name="text">原文。</param>
    /// <returns>换行统一后的文本。</returns>
    private static string Normalize(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}