/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectArtifactMappingUnitTest
*创建时间：2026/10/22
*描述：结论文件 artifacts[] → 面板视图模型的逐字段映射（设计 §6.1.1，C11′）单测：
*      Step 3 准备结论 → RepositoryPrecheck（建组读数 + 逐端行 + checks 归并）；
*      Step 7 准备结论 → PipelinePreview 列表；缺端 / 缺字段 / extra 类型不符一律明确报错。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectArtifactMappingUnitTest
{
    private static readonly List<ProjectEnd> BothEnds = [ProjectEnd.Service, ProjectEnd.Admin];
    private static readonly DateTimeOffset TakenAt = new(2026, 10, 22, 10, 0, 0, TimeSpan.FromHours(8));

    private static StepReport PrecheckReport() => new()
    {
        Step = 3,
        Status = StepReportStatus.Done,
        Ok = true,
        Artifacts =
        {
            new StepReportArtifact
            {
                Kind = "group",
                Name = "jnms",
                ExternalId = "100",
                WebUrl = "https://codeup.aliyun.com/g/100",
                Extra = new GroupArtifactExtra { Exists = true, ParentId = 7, OrganizationId = "org-1" }
            },
            new StepReportArtifact
            {
                Kind = "repository",
                End = ProjectEnd.Service,
                Name = "jnms-Service",
                WebUrl = "https://codeup.aliyun.com/x/jnms-Service",
                LocalPath = "D:\\ws\\jnms\\jnms-Service",
                TemplateBranch = "develop",
                Extra = new RepositoryArtifactExtra
                {
                    TemplateRepository = "TJCES-Service",
                    TemplateProjectId = 11,
                    TemplatePath = "tjc/TJCES/TJCES-Service",
                    TemplateBranches = new List<string> { "develop", "master" },
                    ResolvedBranchSource = "配置登记",
                    RepositoryExists = false,
                    LocalPathOccupied = true,
                    Visibility = "private",
                    PlaceholderSources = new List<string> { "TJCES" }
                }
            },
            new StepReportArtifact
            {
                Kind = "repository",
                End = ProjectEnd.Admin,
                Name = "jnms-Admin",
                LocalPath = "D:\\ws\\jnms\\jnms-Admin",
                TemplateBranch = "develop",
                Extra = new RepositoryArtifactExtra { TemplateRepository = "TJCES-Admin" }
            }
        },
        Checks =
        {
            new StepReportCheck { Name = "代码组", Ok = true, Detail = "提示：目标组已存在，将复用。" },
            new StepReportCheck { Name = "模板仓", Ok = false, Detail = "模板库未命中 TJCES-Mobile。" }
        }
    };

    // ── Step 3 准备结论 → 预检读数 ─────────────────────────────────────────

    [TestMethod]
    public void ToRepositoryPrecheck_完整结论_字段与逐端行映射到位()
    {
        var precheck = ArtifactViewModelMapper.ToRepositoryPrecheck(PrecheckReport(), TakenAt, BothEnds);

        Assert.AreEqual(TakenAt, precheck.TakenAt);
        Assert.AreEqual("jnms", precheck.GroupName);
        Assert.AreEqual(100, precheck.GroupId);
        Assert.IsTrue(precheck.GroupExists);
        Assert.AreEqual(7, precheck.GroupParentId);
        Assert.AreEqual("org-1", precheck.OrganizationId);

        Assert.AreEqual(2, precheck.Ends.Count);

        var service = precheck.Ends.Single(one => one.End == ProjectEnd.Service);
        Assert.AreEqual("jnms-Service", service.RepositoryName);
        Assert.AreEqual("D:\\ws\\jnms\\jnms-Service", service.LocalPath);
        Assert.AreEqual("develop", service.ResolvedBranch);
        Assert.AreEqual("TJCES-Service", service.TemplateRepository);
        Assert.AreEqual(11, service.TemplateProjectId);
        Assert.AreEqual("tjc/TJCES/TJCES-Service", service.TemplatePath);
        Assert.AreEqual("配置登记", service.ResolvedBranchSource);
        Assert.AreEqual("private", service.Visibility);
        Assert.IsTrue(service.LocalPathOccupied);
        Assert.IsTrue(service.IsReady);

        // ok=false 的 check 进 Errors（阻断）；detail 以「提示」开头的 ok=true 进 Warnings（非阻断）
        Assert.AreEqual(1, precheck.Errors.Count);
        StringAssert.Contains(precheck.Errors[0], "模板库未命中");
        Assert.AreEqual(1, precheck.Warnings.Count);
        StringAssert.Contains(precheck.Warnings[0], "将复用");
        Assert.IsFalse(precheck.IsReady, "有阻断级错误时整体不可执行。");
    }

    [TestMethod]
    public void ToRepositoryPrecheck_缺代码组产物_报错误()
    {
        var report = PrecheckReport();
        report.Artifacts.RemoveAll(one => one.Kind == "group");

        var precheck = ArtifactViewModelMapper.ToRepositoryPrecheck(report, TakenAt, BothEnds);

        StringAssert.Contains(string.Join("；", precheck.Errors), "缺少 kind=group 产物");
    }

    [TestMethod]
    public void ToRepositoryPrecheck_代码组extra类型不符_报错误()
    {
        var report = PrecheckReport();
        report.Artifacts.Single(one => one.Kind == "group").Extra = null;

        var precheck = ArtifactViewModelMapper.ToRepositoryPrecheck(report, TakenAt, BothEnds);

        StringAssert.Contains(string.Join("；", precheck.Errors), "不是 GroupArtifactExtra");
    }

    [TestMethod]
    public void ToRepositoryPrecheck_缺端产物_该端标错且整体报错()
    {
        var report = PrecheckReport();
        report.Artifacts.RemoveAll(one => one.Kind == "repository" && one.End == ProjectEnd.Admin);

        var precheck = ArtifactViewModelMapper.ToRepositoryPrecheck(report, TakenAt, BothEnds);

        var admin = precheck.Ends.Single(one => one.End == ProjectEnd.Admin);
        Assert.IsFalse(admin.IsReady);
        StringAssert.Contains(admin.Error!, "缺少该端 kind=repository 产物");
        StringAssert.Contains(string.Join("；", precheck.Errors), "管理端");
    }

    [TestMethod]
    public void ToRepositoryPrecheck_缺localPath_报错误()
    {
        var report = PrecheckReport();
        report.Artifacts.Single(one => one.Kind == "repository" && one.End == ProjectEnd.Admin).LocalPath = string.Empty;

        var precheck = ArtifactViewModelMapper.ToRepositoryPrecheck(report, TakenAt, BothEnds);

        StringAssert.Contains(string.Join("；", precheck.Errors), "缺少 localPath");
    }

    // ── Step 7 准备结论 → 流水线预览 ───────────────────────────────────────

    private static StepReport PipelineReport() => new()
    {
        Step = 7,
        Status = StepReportStatus.Done,
        Ok = true,
        Artifacts =
        {
            new StepReportArtifact
            {
                Kind = "pipeline",
                End = ProjectEnd.Service,
                Name = "jnms-Service-Deploy",
                ExternalId = "3488",
                WebUrl = "https://flow.aliyun.com/p/3488",
                Yaml = "stages:\n  - stage: build",
                Extra = new PipelineArtifactExtra
                {
                    YamlIncludesDeployStage = true,
                    PendingBindings = new List<string> { "服务连接：容器镜像服务" },
                    RecommendedDeployTargetHostGroupId = 9,
                    RecommendedDeployTargetUuid = "uuid-9"
                }
            },
            new StepReportArtifact
            {
                Kind = "pipeline",
                End = ProjectEnd.Admin,
                Name = "jnms-Admin-Deploy",
                Yaml = "stages:\n  - stage: build",
                Extra = new PipelineArtifactExtra { YamlIncludesDeployStage = false }
            }
        }
    };

    [TestMethod]
    public void ToPipelinePreviews_完整结论_每端一条且extra字段到位()
    {
        var result = ArtifactViewModelMapper.ToPipelinePreviews(PipelineReport());

        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual(2, result.Previews.Count);

        var service = result.Previews.Single(one => one.End == ProjectEnd.Service);
        Assert.AreEqual("jnms-Service-Deploy", service.PipelineName);
        StringAssert.Contains(service.Yaml, "stages:");
        Assert.AreEqual("3488", service.PipelineId);
        Assert.AreEqual("https://flow.aliyun.com/p/3488", service.WebUrl);
        Assert.IsTrue(service.YamlIncludesDeployStage);
        Assert.AreEqual("uuid-9", service.RecommendedDeployTargetUuid);
        Assert.AreEqual(1, service.PendingBindings.Count);
        Assert.IsTrue(service.Created);
        Assert.IsFalse(service.Edited);

        var admin = result.Previews.Single(one => one.End == ProjectEnd.Admin);
        Assert.IsFalse(admin.Created);
        Assert.IsFalse(admin.YamlIncludesDeployStage);
    }

    [TestMethod]
    public void ToPipelinePreviews_无产物_报错误()
    {
        var report = PipelineReport();
        report.Artifacts.Clear();

        var result = ArtifactViewModelMapper.ToPipelinePreviews(report);

        Assert.AreEqual(0, result.Previews.Count);
        StringAssert.Contains(string.Join("；", result.Errors), "没有任何 kind=pipeline 产物");
    }

    [TestMethod]
    public void ToPipelinePreviews_产物无端归属_报错误()
    {
        var report = PipelineReport();
        report.Artifacts.Add(new StepReportArtifact
        {
            Kind = "pipeline",
            End = null,
            Name = "未标端",
            Yaml = "stages:"
        });

        var result = ArtifactViewModelMapper.ToPipelinePreviews(report);

        StringAssert.Contains(string.Join("；", result.Errors), "end 为空");
    }

    [TestMethod]
    public void ToPipelinePreviews_缺名称或YAML_报错误()
    {
        var report = new StepReport
        {
            Step = 7,
            Ok = true,
            Artifacts =
            {
                new StepReportArtifact
                {
                    Kind = "pipeline",
                    End = ProjectEnd.Service,
                    Name = string.Empty,
                    Yaml = "   ",
                    Extra = new PipelineArtifactExtra()
                }
            }
        };

        var result = ArtifactViewModelMapper.ToPipelinePreviews(report);

        var joined = string.Join("；", result.Errors);
        StringAssert.Contains(joined, "缺少名称");
        StringAssert.Contains(joined, "缺少 YAML 正文");
    }

    [TestMethod]
    public void ToPipelinePreviews_extra类型不符_报错误()
    {
        var report = new StepReport
        {
            Step = 7,
            Ok = true,
            Artifacts =
            {
                new StepReportArtifact
                {
                    Kind = "pipeline",
                    End = ProjectEnd.Service,
                    Name = "jnms-Service",
                    Yaml = "stages:",
                    Extra = null
                }
            }
        };

        var result = ArtifactViewModelMapper.ToPipelinePreviews(report);

        StringAssert.Contains(string.Join("；", result.Errors), "不是 PipelineArtifactExtra");
    }
}