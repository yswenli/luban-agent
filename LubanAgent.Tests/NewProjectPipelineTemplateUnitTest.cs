/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectPipelineTemplateUnitTest
*描述：Step 7 流水线模板库与渲染器单测（PRD §4.3 Step 7 v0.30）
*      口径：真机验收前先由单测锁住「三端结构 + 关键节点 + 变量与缓存 + 占位符全部展开」
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Configuration;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

/// <summary>
/// Step 7 三端流水线模板与渲染器（<see cref="PipelineTemplateLibrary"/> / <see cref="PipelineRenderer"/>）单测。
/// </summary>
/// <remarks>
/// <para>
/// 这些用例是<b>内嵌新版 YAML</b> 的回归护栏：模板是与企服版 tb <c>flow</c> 语义对齐后重写的，
/// 一旦有人「顺手」改错步骤名或漏展开占位符，此处先红，避免直接提交到云效（远端产物不自动删除）。
/// </para>
/// <para>
/// 断言口径已按 <b>2026-10-22 真机取证</b>（8 轮写探针 + 官方组件文档）校正：
/// 变量不带 <c>type</c>、<c>NodeBuild</c> 用 <c>versionType/customNodeVersion</c>、
/// <c>OSSUpload</c> 用 <c>sourceFilePath/targetFilePath</c>、<c>OSSDelete</c> 用 <c>prefix</c>、
/// <c>runsOn</c> 默认不写、部署组件为 <c>VMDockerDeploy</c> 且 <c>machineGroup</c> 需 uuid。
/// </para>
/// </remarks>
[TestClass]
public class NewProjectPipelineTemplateUnitTest
{
    private const string ProjectCode = "jnms";
    private const string ProjectName = "济南民生";
    private const string MachineGroupUuid = "6f2f4a1c-0d3b-4a6e-9c7d-2b5a8e1f4c30";

    // ── 模板库：三端结构 ───────────────────────────────────────────────────────

    [TestMethod]
    public void BuildStageYaml_Service_HasNewStyleYaml_WithVariablesAndCache()
    {
        var yaml = PipelineTemplateLibrary.BuildStageYaml(ProjectEnd.Service);

        StringAssert.StartsWith(yaml.TrimStart(), "name: {pipelineName}");
        StringAssert.Contains(yaml, "sources:");
        StringAssert.Contains(yaml, "type: codeup");
        StringAssert.Contains(yaml, "triggerEvents: []");
        StringAssert.Contains(yaml, "certificate:");
        StringAssert.Contains(yaml, "serviceConnection: {codeupSc}");

        // 变量与缓存（本轮 Step 7 的核心交付；旧「克隆参考流水线」口径不再成立）
        StringAssert.Contains(yaml, "variables:");
        StringAssert.Contains(yaml, "key: DOCKER_IMAGE_NAME");
        StringAssert.Contains(yaml, "key: APP_PORT");
        StringAssert.Contains(yaml, "key: DOCKER_CONTAINER_NAME");
        StringAssert.Contains(yaml, "cache:");
        StringAssert.Contains(yaml, "cacheDirs:");
        StringAssert.Contains(yaml, "/root/.npm");
        StringAssert.Contains(yaml, "cacheMode: {cacheMode}");

        // 官方步骤名 Command + runsOn 走独立令牌（默认整行为空 ⇒ 云效默认公共北京集群）
        StringAssert.Contains(yaml, "step: Command");
        StringAssert.Contains(yaml, "docker login");
        StringAssert.Contains(yaml, "{runsOn}");
        Assert.IsFalse(yaml.Contains("runsOn: private/"), "模板不应硬拼 private/ 前缀（值由配置整串给出）");
    }

    [TestMethod]
    public void BuildStageYamls_NeverEmitVariableType_AndMarkHarborPasswordSecret()
    {
        foreach (var end in new[] { ProjectEnd.Service, ProjectEnd.Admin, ProjectEnd.Mobile })
        {
            var yaml = PipelineTemplateLibrary.BuildStageYaml(end);

            // 真机核：顶层 variables 带 type 会被拒（Not supported variable type）
            Assert.IsFalse(yaml.Contains("type: String"), $"{end} 的 variables 不应带 type 字段");
            Assert.IsFalse(yaml.Contains("type: Plain"), $"{end} 的 variables 不应带 type 字段");
        }

        var service = PipelineTemplateLibrary.BuildStageYaml(ProjectEnd.Service);
        StringAssert.Contains(service, "secret: true");                 // HARBOR_PASSWORD 标私密
        StringAssert.Contains(service, "- key: HARBOR_PASSWORD");
    }

    [TestMethod]
    public void BuildStageYaml_Admin_UsesNodeBuildAndOssSteps_WithoutRunsOn()
    {
        var yaml = PipelineTemplateLibrary.BuildStageYaml(ProjectEnd.Admin);

        StringAssert.Contains(yaml, "step: NodeBuild");
        StringAssert.Contains(yaml, "step: OSSDelete");
        StringAssert.Contains(yaml, "step: OSSUpload");

        // NodeBuild 真机口径：versionType + customNodeVersion（企服版 Node 16 ⇒ lts/gallium）
        StringAssert.Contains(yaml, "versionType: custom");
        StringAssert.Contains(yaml, "customNodeVersion: lts/gallium");
        Assert.IsFalse(yaml.Contains("nodeVersion:"), "不再用裸 nodeVersion 写法（官方文档给出的是 versionType/customNodeVersion）");
        StringAssert.Contains(yaml, "npm run build:dev");

        // OSS 步骤真机键名：region 必填；上传用 sourceFilePath/targetFilePath（不是 localPath/prefix）
        StringAssert.Contains(yaml, "region: {ossRegion}");
        StringAssert.Contains(yaml, "sourceFilePath: '{uploadPath}'");
        StringAssert.Contains(yaml, "targetFilePath: '{ossPrefix}'");
        Assert.IsFalse(yaml.Contains("localPath:"), "OSSUpload 的目标键是 sourceFilePath，不是 localPath");

        // 前端走云效公共北京集群：不写 runsOn（企服版 buildNodeGroup=K8S-4 属公共集群）
        Assert.IsFalse(yaml.Contains("runsOn:"), "管理端不应出现 runsOn（公共集群）");

        // 前端无后端变量（镜像 / 端口 / 容器名），只有 npm 缓存
        Assert.IsFalse(yaml.Contains("DOCKER_IMAGE_NAME"), "管理端不应出现后端镜像变量");
        Assert.IsFalse(yaml.Contains("APP_PORT"), "管理端不应出现后端端口变量");
    }

    [TestMethod]
    public void BuildStageYaml_Mobile_SameShapeAsAdmin_WithOwnOssPrefixToken()
    {
        var mobile = PipelineTemplateLibrary.BuildStageYaml(ProjectEnd.Mobile);
        var admin = PipelineTemplateLibrary.BuildStageYaml(ProjectEnd.Admin);

        StringAssert.Contains(mobile, "step: NodeBuild");
        StringAssert.Contains(mobile, "step: OSSUpload");

        // 结构同构（除 OSS 前缀 / 上传目录的按端默认值外，模板本体一致）
        Assert.AreEqual(
            NormalizeForComparer(admin),
            NormalizeForComparer(mobile),
            "移动端与管理端模板应同构（差异只在按端默认值）");
    }

    [TestMethod]
    public void DeployStageYaml_OnlyServiceHasIndependentDeployStage_AndUsesVmDockerDeploy()
    {
        var service = PipelineTemplateLibrary.DeployStageYaml(ProjectEnd.Service)!;

        StringAssert.Contains(service, "component: VMDockerDeploy");
        StringAssert.Contains(service, "machineGroup: {machineGroup}");
        StringAssert.Contains(service, "executeUser: root");
        Assert.IsFalse(service.Contains("component: VMDeploy"), "VMDeploy 是「主机部署（制品）」；Docker 部署用 VMDockerDeploy");
        Assert.IsNull(PipelineTemplateLibrary.DeployStageYaml(ProjectEnd.Admin));
        Assert.IsNull(PipelineTemplateLibrary.DeployStageYaml(ProjectEnd.Mobile));
    }

    [TestMethod]
    public void DeployStageComment_FrontEnds_ExplainThatOssUploadIsPublish()
    {
        var admin = PipelineTemplateLibrary.DeployStageComment(ProjectEnd.Admin);

        // 注释块整体以 # 开头（可整块粘贴回 YAML）
        Assert.IsTrue(admin.Split('\n').Where(line => line.Trim().Length > 0).All(line => line.TrimStart().StartsWith('#')),
            "部署阶段注释块每一行都必须是注释");
        StringAssert.Contains(admin, "OSS");
    }

    [TestMethod]
    public void PendingBindings_AreNonEmpty_ForEveryEnd()
    {
        foreach (var end in new[] { ProjectEnd.Service, ProjectEnd.Admin, ProjectEnd.Mobile })
        {
            var pending = PipelineTemplateLibrary.PendingBindings(end);
            Assert.IsTrue(pending.Count > 0, $"{end} 的待人工绑定清单不应为空");
        }
    }

    [TestMethod]
    public void PendingBindings_Service_MentionsMachineGroupUuidAndRunsOn()
    {
        var pending = string.Join('\n', PipelineTemplateLibrary.PendingBindings(ProjectEnd.Service));

        StringAssert.Contains(pending, "uuid");                 // 主机组必须是 uuid（真机 226948 被拒）
        StringAssert.Contains(pending, "runsOn");               // 私有集群只能人工回填
        StringAssert.Contains(pending, "HARBOR_PASSWORD");
    }

    // ── 渲染器：占位符展开 ─────────────────────────────────────────────────────

    [TestMethod]
    public void Render_Service_ExpandsNameVariablesAndRunsOn()
    {
        var settings = CreateSettings();

        var result = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Service), settings);

        Assert.AreEqual("JNMS_DEV_Deploy_jnms-Service", result.PipelineName);
        StringAssert.Contains(result.Yaml, "name: JNMS_DEV_Deploy_jnms-Service");
        StringAssert.Contains(result.Yaml, "name: jnms-Service");                        // sources.<id>.name
        StringAssert.Contains(result.Yaml, "endpoint: https://codeup.example.com/jnms-Service.git");
        StringAssert.Contains(result.Yaml, "branch: develop");                           // pushBranch 优先
        StringAssert.Contains(result.Yaml, "serviceConnection: r3x6es4esgbsn6ur");      // 已展开，无占位符
        StringAssert.Contains(result.Yaml, "runsOn: private/PM-SielV3Iovlo4Au4J");       // buildCluster 整串照写
        StringAssert.Contains(result.Yaml, "value: 'jnms-service'");                     // 镜像 / 容器名
        StringAssert.Contains(result.Yaml, "value: '7000'");                             // APP_PORT
        StringAssert.Contains(result.Yaml, "value: 'develop'");                          // DOCKER_REPO_NAME = {branch}
        StringAssert.Contains(result.Yaml, "cacheMode: local");                          // 私有集群 → local
        Assert.IsFalse(result.Yaml.Contains("{"), "渲染后不应残留任何占位符");
    }

    [TestMethod]
    public void Render_WithoutBuildCluster_DropsRunsOn_AndFallsBackToRemoteCacheMode()
    {
        var settings = CreateSettings();
        settings.FindPipelineDefault(ProjectEnd.Service)!.BuildCluster = string.Empty;

        var result = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Service), settings);

        Assert.IsFalse(result.Yaml.Contains("runsOn:"), "构建集群留空时不应写 runsOn（走公共集群）");
        StringAssert.Contains(result.Yaml, "cacheMode: remote");                          // 公共集群用 remote
        Assert.IsFalse(result.Yaml.Contains("{"), "渲染后不应残留任何占位符");
    }

    [TestMethod]
    public void Render_AcceptsPublicClusterValue_Verbatim()
    {
        var settings = CreateSettings();
        settings.FindPipelineDefault(ProjectEnd.Service)!.BuildCluster = "public/cn-beijing";

        var result = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Service), settings);

        StringAssert.Contains(result.Yaml, "runsOn: public/cn-beijing");                  // 不再拼 private/ 前缀
        StringAssert.Contains(result.Yaml, "cacheMode: remote");                          // 非 private 前缀 → remote
    }

    [TestMethod]
    public void Render_Admin_ExpandsOssTokens()
    {
        var settings = CreateSettings();

        var result = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Admin), settings);

        StringAssert.Contains(result.Yaml, "serviceConnection: sm7mho5x9ex945rp");
        StringAssert.Contains(result.Yaml, "region: cn-shanghai");
        StringAssert.Contains(result.Yaml, "bucket: 'tejingcai'");
        StringAssert.Contains(result.Yaml, "targetFilePath: 'DEV/JNMS-Admin/'");
        StringAssert.Contains(result.Yaml, "prefix: 'DEV/JNMS-Admin/'");                  // OSSDelete 的目录键
        StringAssert.Contains(result.Yaml, "sourceFilePath: 'dist'");
        Assert.IsFalse(result.Yaml.Contains("{"), "渲染后不应残留任何占位符");
    }

    [TestMethod]
    public void Render_Default_IncludesDeployComment_AndKeepsDeployStageOut()
    {
        var settings = CreateSettings();

        var result = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Service), settings);

        // 默认不含部署阶段：注释里出现（被注释掉，且按 stages 之下的缩进展示），但不存在未注释的 deploy: 阶段
        StringAssert.Contains(result.Yaml, "#   deploy:");
        Assert.IsFalse(
            result.Yaml.Split('\n').Any(line => line.Trim() == "deploy:"),
            "默认口径下部署阶段不应是可执行阶段（只作注释附在末尾）");

        Assert.AreEqual(5, result.PendingBindings.Count, "后端待人工绑定清单条数应与模板一致");
        Assert.IsTrue(result.PendingBindings.All(item => !item.Contains('{')), "待绑定清单也应完全展开");
    }

    [TestMethod]
    public void Render_WithDeployStage_AppendsExecutableDeployStage_AndNoComment()
    {
        var settings = CreateSettings();

        var result = PipelineRenderer.Render(
            CreateSpec(), CreateRepository(ProjectEnd.Service), settings, includeDeployStage: true);

        Assert.IsTrue(
            result.Yaml.Split('\n').Any(line => line.Trim() == "deploy:"),
            "补齐口径下部署阶段应是可执行阶段");
        StringAssert.Contains(result.Yaml, "component: VMDockerDeploy");
        StringAssert.Contains(result.Yaml, $"machineGroup: '{MachineGroupUuid}'");
        Assert.IsFalse(result.Yaml.Contains("只含构建阶段"), "补齐口径下不应再附「只含构建阶段」的注释");

        // 结构护栏：部署阶段必须挂在顶层 stages: 之下（stage id 缩进 2 空格），
        // 顶格的 deploy: 是顶层键，云效 YAML 校验必失败（v0.30 潜在结构错误，v0.31 修正）。
        Assert.IsTrue(
            result.Yaml.Split('\n').Any(line => line == "  deploy:"),
            "部署阶段片段必须缩进 2 空格、挂在 stages: 之下");
        Assert.IsFalse(
            result.Yaml.Split('\n').Any(line => line == "deploy:"),
            "不得出现顶格 deploy:（那会变成顶层键）");
    }

    [TestMethod]
    public void Render_UnboundMachineGroup_EmitsReadablePlaceholder()
    {
        var settings = CreateSettings();
        settings.FindPipelineDefault(ProjectEnd.Service)!.DeployMachineGroup = string.Empty;

        var result = PipelineRenderer.Render(
            CreateSpec(), CreateRepository(ProjectEnd.Service), settings, includeDeployStage: true);

        // 未绑定时给可读占位（而不是 `machineGroup:` 空值这种必然被云效拒的写法）
        StringAssert.Contains(result.Yaml, "machineGroup: '<待绑定：主机组 uuid>'");
        Assert.IsFalse(result.Yaml.Contains("{"), "占位提示里也不应残留占位符语法");
    }

    [TestMethod]
    public void Render_FallsBackToBuiltInNameFormat_WhenPipelineNameFormatIsEmpty()
    {
        var settings = CreateSettings();
        settings.PipelineNameFormat = string.Empty;

        var result = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Admin), settings);

        Assert.AreEqual("JNMS_DEV_Deploy_jnms-Admin", result.PipelineName);
    }

    [TestMethod]
    public void Render_UsesRepositoryDefaultBranch_WhenPushBranchIsBlank()
    {
        var settings = CreateSettings();
        settings.PushBranch = string.Empty;

        var result = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Admin), settings);

        StringAssert.Contains(result.Yaml, "branch: master");   // 退回仓库默认分支
        StringAssert.Contains(result.Yaml, "name: JNMS_DEV_Deploy_jnms-Admin");
    }

    // ── 手工微调（v0.31：YAML 可编辑 + 补齐追加部署阶段）──────────────────────

    [TestMethod]
    public void Render_ExposesExpandedDeployFragment_ForServiceOnly()
    {
        var settings = CreateSettings();

        var service = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Service), settings);
        var admin = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Admin), settings);

        Assert.IsNotNull(service.DeployStageYaml, "后端应带出展开后的部署阶段片段（供补齐时追加到微调文本之后）");
        StringAssert.StartsWith(service.DeployStageYaml, "  deploy:", "部署阶段片段必须按 stages 之下的缩进（2 空格）");
        StringAssert.Contains(service.DeployStageYaml, $"machineGroup: '{MachineGroupUuid}'");
        Assert.IsFalse(service.DeployStageYaml!.Contains('{'), "部署阶段片段必须完全展开");
        Assert.IsNull(admin.DeployStageYaml, "前端没有独立部署阶段");
    }

    [TestMethod]
    public void Compose_WithoutOverride_KeepsRenderedYaml()
    {
        const string Rendered = "name: X\nstages:\n  build:\n";

        Assert.AreEqual(Rendered, PipelineYamlOverride.Compose(Rendered, null, "deploy:\n  component: VMDockerDeploy"));
        Assert.AreEqual(Rendered, PipelineYamlOverride.Compose(Rendered, "   \n\t", "deploy: x"));
    }

    [TestMethod]
    public void Compose_WithOverride_SubmitsEditedTextVerbatim_WithNormalizedLineEndings()
    {
        const string Edited = "name: X\r\nstages:\r\n  build:\r\n    jobs:\r\n      j:\r\n        steps: {}\r\n\r\n";

        var actual = PipelineYamlOverride.Compose("name: IGNORED\nstages:", Edited, deployStageYaml: null);

        Assert.AreEqual("name: X\nstages:\n  build:\n    jobs:\n      j:\n        steps: {}", actual,
            "微调文本必须原样提交（只统一换行符、去掉末尾空白）");
    }

    [TestMethod]
    public void Compose_AppendDeploy_AppendsFragmentAfterEditedText()
    {
        var actual = PipelineYamlOverride.Compose(
            "name: IGNORED\nstages:",
            "name: X\nstages:\n  build: {}",
            "  deploy:\n    component: VMDockerDeploy");

        StringAssert.StartsWith(actual, "name: X\nstages:");
        StringAssert.EndsWith(actual, "  deploy:\n    component: VMDockerDeploy");
        StringAssert.Contains(actual, "\n\n", "构建段与部署段之间应留一个空行");
    }

    [TestMethod]
    public void Compose_AppendDeploy_DoesNotDuplicate_WhenUserWroteOwnDeployStage()
    {
        // 用户自己写了 deploy 阶段但换了组件：同 id 再追加会拼出重复键，故按「已有 deploy 阶段」跳过
        const string Edited = "name: X\nstages:\n  build: {}\n  deploy:\n    component: KubectlDeploy";

        var actual = PipelineYamlOverride.Compose(Edited, Edited, "  deploy:\n    component: VMDockerDeploy");

        Assert.AreEqual(Edited, actual, "已有 deploy 阶段 ⇒ 不得再追加同 id 阶段");
    }

    [TestMethod]
    public void Compose_AppendDeploy_DoesNotDuplicate_WhenUserAlreadyWroteDeployComponent()
    {
        const string Edited = "name: X\nstages:\n  build: {}\n  deploy:\n    component: VMDockerDeploy";

        var actual = PipelineYamlOverride.Compose(Edited, Edited, "deploy:\n  component: VMDockerDeploy");

        Assert.AreEqual(Edited, actual, "用户自己写了 VMDockerDeploy ⇒ 视为自备部署阶段，不得重复追加");
    }

    [TestMethod]
    public void Compose_RealPreviewText_AppendsExecutableDeployStage()
    {
        var settings = CreateSettings();
        var rendered = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Service), settings);

        // 真实微调底稿＝预览原文：用户通常只改变量、**保留末尾的部署阶段注释块**。
        var edited = rendered.Yaml.Replace("value: '7000'", "value: '7021'");
        Assert.AreNotEqual(rendered.Yaml, edited, "样本必须真的含 APP_PORT 变量，否则本用例失去意义");

        // 锁住 D2 的根因：注释块里就有 VMDockerDeploy 字样（裸子串判据会因此误判为「用户自备部署阶段」）
        Assert.IsTrue(
            rendered.Yaml.Split('\n').Any(line => line.Contains("component: VMDockerDeploy") && line.TrimStart().StartsWith('#')),
            "预览原文的部署阶段注释块里应含 component: VMDockerDeploy（D2 的触发条件）");
        StringAssert.Contains(rendered.Yaml, "#   deploy:");

        var actual = PipelineYamlOverride.Compose(rendered.Yaml, edited, rendered.DeployStageYaml);

        Assert.IsTrue(
            actual.Split('\n').Any(line => line == "  deploy:"),
            "补齐时必须真的追加活动部署阶段（不得被注释块骗过）；失败即回归 D2");
        Assert.IsTrue(
            actual.Split('\n').Any(line => line.Trim() == "component: VMDockerDeploy"),
            "追加出的部署阶段应含活动组件行");
        StringAssert.Contains(actual, $"machineGroup: '{MachineGroupUuid}'");
        StringAssert.Contains(actual, "value: '7021'", "微调内容必须原样保留");
        Assert.IsFalse(
            actual.Split('\n').Any(line => line == "deploy:"),
            "不得出现顶格 deploy:（那会变成顶层键）");
        Assert.IsFalse(actual.Contains("只含构建阶段"), "追加部署段后不应再留「只含构建阶段」的注释块");
        Assert.IsNull(
            PipelineYamlOverride.Validate(actual, rendered.PipelineName),
            "补齐后的文本必须能过提交前校验");
    }

    [TestMethod]
    public void Compose_RealPreviewText_CreateMode_DoesNotAppendDeployStage()
    {
        var settings = CreateSettings();
        var rendered = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Service), settings);
        var edited = rendered.Yaml.Replace("value: '7000'", "value: '7021'");

        // 创建口径：NewProjectService 只在 includeDeployStage 时传部署片段，此处等价于传 null。
        var actual = PipelineYamlOverride.Compose(rendered.Yaml, edited, deployStageYaml: null);

        Assert.IsFalse(
            actual.Split('\n').Any(line => line.Trim() == "deploy:"),
            "首次创建口径不得出现活动部署阶段（PRD 2A）；失败即回归 D1");
        StringAssert.Contains(actual, "value: '7021'", "微调内容仍须原样提交");
        StringAssert.Contains(actual, "只含构建阶段", "创建口径应保留模板自带的注释块");
    }

    [TestMethod]
    public void Compose_AppendDeploy_IgnoresCommentedBlock_ButStillHonoursActiveDeployStage()
    {
        var settings = CreateSettings();
        var rendered = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Service), settings);

        // ① 只有注释块（默认底稿）⇒ 必须追加
        var appended = PipelineYamlOverride.Compose(rendered.Yaml, rendered.Yaml, rendered.DeployStageYaml);
        Assert.IsTrue(
            appended.Split('\n').Any(line => line == "  deploy:"),
            "只有注释块时不得判定为「用户自备部署阶段」");

        // ② 用户把注释块解注释、自己写了 deploy 阶段 ⇒ 守卫仍须生效（不得拼出重复键）
        var withOwnDeploy = rendered.Yaml.Replace("#   deploy:", "  deploy:");
        Assert.AreNotEqual(rendered.Yaml, withOwnDeploy, "样本必须真的含注释掉的 deploy 行");

        var actual = PipelineYamlOverride.Compose(rendered.Yaml, withOwnDeploy, rendered.DeployStageYaml);
        var expected = withOwnDeploy.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd();
        Assert.AreEqual(expected, actual, "已有活动 deploy 阶段 ⇒ 不得再追加同 id 阶段（Compose 只统一换行符）");
    }

    [TestMethod]
    public void Validate_AcceptsRenderedYaml_AndReadsName()
    {
        var rendered = PipelineRenderer.Render(CreateSpec(), CreateRepository(ProjectEnd.Service), CreateSettings());

        Assert.IsNull(PipelineYamlOverride.Validate(rendered.Yaml, rendered.PipelineName));
        Assert.AreEqual(rendered.PipelineName, PipelineYamlOverride.ReadName(rendered.Yaml));
    }

    [TestMethod]
    public void Validate_AcceptsQuotedName_AndTrailingCommentOnStages()
    {
        const string Yaml = "name: \"JNMS_DEV_Deploy_jnms-Service\"\nstages:   # 构建\n  build: {}";

        Assert.AreEqual("JNMS_DEV_Deploy_jnms-Service", PipelineYamlOverride.ReadName(Yaml));
        Assert.IsNull(PipelineYamlOverride.Validate(Yaml, "JNMS_DEV_Deploy_jnms-Service"));
    }

    [TestMethod]
    public void Validate_RejectsEmptyTabMissingStagesAndRenamedYaml()
    {
        Assert.IsNotNull(PipelineYamlOverride.Validate(null, "X"), "空 YAML 必须拦下");
        Assert.IsNotNull(PipelineYamlOverride.Validate("   ", "X"), "空白 YAML 必须拦下");

        var tab = PipelineYamlOverride.Validate("name: X\nstages:\n\tbuild: {}", "X");
        Assert.IsNotNull(tab, "Tab 缩进必须拦下（云效只接受空格）");
        StringAssert.Contains(tab, "Tab");

        var noStages = PipelineYamlOverride.Validate("name: X\nsources: {}", "X");
        Assert.IsNotNull(noStages, "缺顶层 stages: 必须拦下");
        StringAssert.Contains(noStages, "stages");

        var noName = PipelineYamlOverride.Validate("stages:\n  build: {}", "X");
        Assert.IsNotNull(noName, "缺顶层 name: 必须拦下");

        var renamed = PipelineYamlOverride.Validate("name: Y\nstages:\n  build: {}", "X");
        Assert.IsNotNull(renamed, "改掉流水线名必须拦下（同名幂等检索依赖它）");
        StringAssert.Contains(renamed, "流水线名不能改");
    }

    // ── 辅助 ──────────────────────────────────────────────────────────────────

    /// <summary>去掉按端差异行（OSS 前缀 / 上传目录）后用于结构比对。</summary>
    /// <param name="yaml">模板原文。</param>
    /// <returns>归一化文本。</returns>
    private static string NormalizeForComparer(string yaml)
        => string.Join('\n', yaml
            .Split('\n')
            .Where(line => !line.Contains("{ossSc}") && !line.Contains("{ossPrefix}") && !line.Contains("{uploadPath}")));

    private static ProjectSpec CreateSpec() => new()
    {
        Name = ProjectName,
        Code = ProjectCode,
        Ends = new List<ProjectEnd> { ProjectEnd.Service, ProjectEnd.Admin },
        Repositories = new List<EndRepository>
        {
            CreateRepository(ProjectEnd.Service),
            CreateRepository(ProjectEnd.Admin)
        }
    };

    private static EndRepository CreateRepository(ProjectEnd end) => new()
    {
        End = end,
        TemplateRepository = $"TJCES-{end}",
        RepositoryName = $"{ProjectCode}-{end}",
        CloneUrl = $"https://codeup.example.com/{ProjectCode}-{end}.git",
        WebUrl = $"https://codeup.example.com/{ProjectCode}-{end}",
        LocalPath = $@"D:\work\{ProjectCode}-{end}",
        DefaultBranch = "master",
        Cloned = true
    };

    private static NewProjectSettings CreateSettings() => new()
    {
        PushBranch = "develop",
        PipelineNameFormat = "{CODE}_DEV_Deploy_{repository}",
        PipelineDefaults = new List<PipelineDefault>
        {
            new()
            {
                End = ProjectEnd.Service,
                AppPort = "7000",
                ContainerName = "{code}-service",
                ImageName = "{code}-service",
                ImageTag = "latest",
                DockerRepo = "{branch}",
                HarborHost = "harbor.tjingcai.com",
                BuildCluster = "private/PM-SielV3Iovlo4Au4J",
                CodeupServiceConnection = "r3x6es4esgbsn6ur",
                DeployMachineGroup = MachineGroupUuid
            },
            new()
            {
                End = ProjectEnd.Admin,
                OssServiceConnection = "sm7mho5x9ex945rp",
                OssBucket = "tejingcai",
                OssRegion = "cn-shanghai",
                OssPrefix = "DEV/{CODE}-Admin/",
                UploadPath = "dist",
                CodeupServiceConnection = "r3x6es4esgbsn6ur"
            },
            new()
            {
                End = ProjectEnd.Mobile,
                OssServiceConnection = "sm7mho5x9ex945rp",
                OssBucket = "tejingcai",
                OssRegion = "cn-shanghai",
                OssPrefix = "DEV/{CODE}-Mobile/",
                UploadPath = "dist/dev/h5",
                CodeupServiceConnection = "r3x6es4esgbsn6ur"
            }
        }
    };
}