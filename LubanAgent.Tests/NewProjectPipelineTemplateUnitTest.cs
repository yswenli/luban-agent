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

        // 默认不含部署阶段：注释里出现（被注释掉），但不存在未注释的 deploy: 阶段
        StringAssert.Contains(result.Yaml, "# deploy:");
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