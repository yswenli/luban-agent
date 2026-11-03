/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectStepLocalVerifierUnitTest
*创建时间：2026/10/22
*描述：Step 3/6/7 插件侧本地独立复核器单测——
*      用临时目录造出「像 git 仓」的目录结构，锁死：
*      Step 3：目录 / .git/HEAD / refs/heads/<pin> / 占位符残余 = 0；
*      Step 6：refs/remotes/origin/<branch> 就位且提交 id 与自报一致；
*      Step 7：无本地证据，只如实给出残余风险说明（不作失败依据）。
*
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Services;

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectStepLocalVerifierUnitTest
{
    private const string Sha40 = "0123456789abcdef0123456789abcdef01234567";

    private readonly List<string> _tempRoots = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _tempRoots)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // 临时目录，清理失败不影响断言。
            }
        }

        _tempRoots.Clear();
    }

    private string NewRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "luban-np-verify", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempRoots.Add(dir);
        return dir;
    }

    /// <summary>单仓名（各端共用同一个仓根）。</summary>
    private const string RepoName = "jnms-Mixed";

    /// <summary>后端在仓内的模块目录名。</summary>
    private const string ServiceModule = "jnms-Service";

    /// <summary>
    /// 造一个「像 git 仓」的目录：.git/HEAD + 一个本地分支 + 一份源码。
    /// 单仓形态：仓根下再放一个后端模块子目录（各端模块目录必须真实存在）。
    /// </summary>
    private string NewRepo(
        string pin = "develop",
        string sourceContent = "class App { }",
        bool withGit = true,
        bool withModule = true)
    {
        var repo = Path.Combine(NewRoot(), RepoName);
        Directory.CreateDirectory(repo);

        if (withModule)
        {
            var module = Path.Combine(repo, ServiceModule);
            Directory.CreateDirectory(Path.Combine(module, "src"));
            File.WriteAllText(Path.Combine(module, "src", "App.cs"), sourceContent);
        }
        else
        {
            // 没模块目录时也要让仓根非空，避免把「目录空」混进要断言的问题里。
            File.WriteAllText(Path.Combine(repo, "README.md"), "# repo");
        }

        if (withGit)
        {
            var gitDir = Path.Combine(repo, ".git");
            Directory.CreateDirectory(Path.Combine(gitDir, "refs", "heads"));
            File.WriteAllText(Path.Combine(gitDir, "HEAD"), $"ref: refs/heads/{pin}\n");
            File.WriteAllText(Path.Combine(gitDir, "refs", "heads", pin), Sha40);
        }

        return repo;
    }

    private static StepReport RepoReport(
        string localPath,
        string pin,
        params string[] tokens) => new()
    {
        Step = 3,
        Ok = true,
        Artifacts =
        {
            new StepReportArtifact
            {
                Kind = "repository",
                End = ProjectEnd.Service,
                Name = RepoName,
                LocalPath = localPath,
                ModulePath = ServiceModule,
                TemplateBranch = pin,
                Extra = new RepositoryArtifactExtra { Cloned = true, PlaceholderSources = tokens.ToList() }
            }
        }
    };

    /// <summary>同仓两端（后端 + 管理端）的结论：localPath 同值，模块目录不同。</summary>
    private static StepReport TwoEndsRepoReport(string localPath, string pin, params string[] tokens) => new()
    {
        Step = 3,
        Ok = true,
        Artifacts =
        {
            new StepReportArtifact
            {
                Kind = "repository",
                End = ProjectEnd.Service,
                Name = RepoName,
                LocalPath = localPath,
                ModulePath = ServiceModule,
                TemplateBranch = pin,
                Extra = new RepositoryArtifactExtra { Cloned = true, PlaceholderSources = tokens.ToList() }
            },
            new StepReportArtifact
            {
                Kind = "repository",
                End = ProjectEnd.Admin,
                Name = RepoName,
                LocalPath = localPath,
                ModulePath = "jnms-Admin",
                TemplateBranch = pin,
                Extra = new RepositoryArtifactExtra { Cloned = true, PlaceholderSources = tokens.ToList() }
            }
        }
    };

    private static StepReport PushReport(string localPath, string branch, string? commitId, params string[] tokens) => new()
    {
        Step = 6,
        Ok = true,
        Artifacts =
        {
            new StepReportArtifact
            {
                Kind = "repository",
                End = ProjectEnd.Service,
                Name = "jnms-Service",
                LocalPath = localPath,
                TemplateBranch = branch,
                Extra = new RepositoryArtifactExtra
                {
                    Cloned = true,
                    CommitId = commitId,
                    PlaceholderSources = tokens.ToList()
                }
            }
        }
    };

    // ── Step 3 ────────────────────────────────────────────────────────────

    [TestMethod]
    public void VerifyRepositories_目录与pin与占位符齐备_通过()
    {
        var repo = NewRepo();
        var report = RepoReport(repo, "develop", "TJCES", "tjces");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        CollectionAssert.AreEqual(Array.Empty<string>(), issues.ToArray());
    }

    [TestMethod]
    public void VerifyRepositories_本地目录不存在_报问题()
    {
        var missing = Path.Combine(NewRoot(), "不存在");
        var report = RepoReport(missing, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "本地目录不存在");
    }

    [TestMethod]
    public void VerifyRepositories_未给localPath_报问题()
    {
        var report = RepoReport(string.Empty, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "未给出 localPath");
    }

    [TestMethod]
    public void VerifyRepositories_缺git目录_报问题()
    {
        var repo = NewRepo(withGit: false);
        var report = RepoReport(repo, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "未发现 .git 目录");
    }

    [TestMethod]
    public void VerifyRepositories_pin分支未就位_报问题()
    {
        var repo = NewRepo(pin: "develop");
        // 结论自报 pin = release（本地只有 develop）
        var report = RepoReport(repo, "release", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "refs/heads/release");
    }

    [TestMethod]
    public void VerifyRepositories_未给pin分支_报问题()
    {
        var repo = NewRepo();
        var report = RepoReport(repo, string.Empty, "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "未给出 pin 分支");
    }

    [TestMethod]
    public void VerifyRepositories_占位符残余命中_报问题()
    {
        // 源码里还留着 TJCES（替换没生效）
        var repo = NewRepo(sourceContent: "namespace TJCES.Service; class App { }");
        var report = RepoReport(repo, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "占位符残余未清零");
    }

    [TestMethod]
    public void VerifyRepositories_点号开头的env族文件残余_报问题()
    {
        // 真机踩坑点：.env.development 的扩展名是 .development，只按扩展名匹配会漏改/漏扫
        var repo = NewRepo();
        File.WriteAllText(Path.Combine(repo, ".env.development"),
            "VITE_API_URL = 'https://dev-tjces.example.test/service'");
        var report = RepoReport(repo, "develop", "TJCES", "Tjces", "tjces");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "占位符残余未清零");
    }

    [TestMethod]
    public void VerifyRepositories_白名单外文件与跳过目录_不参与残余()
    {
        // 助手按文档不会改这些文件，复核器必须同口径，否则会把「按规则跳过」误判成残余
        var repo = NewRepo();
        File.WriteAllText(Path.Combine(repo, "logo.png"), "TJCES");
        Directory.CreateDirectory(Path.Combine(repo, "dist"));
        File.WriteAllText(Path.Combine(repo, "dist", "app.js"), "TJCES");
        Directory.CreateDirectory(Path.Combine(repo, "node_modules"));
        File.WriteAllText(Path.Combine(repo, "node_modules", "pkg.js"), "TJCES");
        var report = RepoReport(repo, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        CollectionAssert.AreEqual(Array.Empty<string>(), issues.ToArray());
    }

    [TestMethod]
    public void VerifyRepositories_UTF16文件_不参与残余()
    {
        // 文档约定 UTF-16 跳过（宁可不改也不破坏编码），复核器同样不参与
        var repo = NewRepo();
        File.WriteAllText(Path.Combine(repo, ".env.local"), "VITE_API_URL=dev-TJCES", System.Text.Encoding.Unicode);
        var report = RepoReport(repo, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        CollectionAssert.AreEqual(Array.Empty<string>(), issues.ToArray());
    }

    [TestMethod]
    public void VerifyRepositories_无扩展名白名单文件残余_报问题()
    {
        // TextFileNames 里的名字（.gitignore 这类点号开头）不靠扩展名也要参与
        var repo = NewRepo();
        File.WriteAllText(Path.Combine(repo, ".gitignore"), "TJCES/bin/");
        var report = RepoReport(repo, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "占位符残余未清零");
    }

    [TestMethod]
    public void VerifyRepositories_未给占位符令牌_报问题()
    {
        var repo = NewRepo();
        var report = RepoReport(repo, "develop");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "placeholderSources 为空");
        // 方案 C：空令牌＝该端没做占位符替换（不是「复核器能力不足」），cloned=true 也不成立
        StringAssert.Contains(issues[0], "占位符替换未完成");
        StringAssert.Contains(issues[0], "`cloned=true` 不成立");
    }

    [TestMethod]
    public void VerifyRepositories_空目录_报空目录问题()
    {
        var repo = Path.Combine(NewRoot(), "empty");
        Directory.CreateDirectory(repo);
        var report = RepoReport(repo, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        StringAssert.Contains(string.Join("；", issues), "本地目录为空");
    }

    [TestMethod]
    public void VerifyRepositories_模块目录缺失_报问题()
    {
        // 单仓下「端」就是仓内模块目录：仓根对但模块目录没了 ⇒ 该端未完成
        var repo = NewRepo(withModule: false);
        var report = RepoReport(repo, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "仓内模块目录不存在");
        StringAssert.Contains(issues[0], ServiceModule);
    }

    [TestMethod]
    public void VerifyRepositories_同仓两端_仓级只复核一次且逐端核对模块目录()
    {
        var repo = NewRepo();
        Directory.CreateDirectory(Path.Combine(repo, "jnms-Admin"));
        var report = TwoEndsRepoReport(repo, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service, ProjectEnd.Admin]);

        CollectionAssert.AreEqual(Array.Empty<string>(), issues.ToArray());
    }

    [TestMethod]
    public void VerifyRepositories_同仓两端占位符残余_只报一条仓级问题()
    {
        // 仓级问题（占位符残余）不能按端重复报三遍
        var repo = NewRepo(sourceContent: "namespace TJCES.Service; class App { }");
        Directory.CreateDirectory(Path.Combine(repo, "jnms-Admin"));
        var report = TwoEndsRepoReport(repo, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service, ProjectEnd.Admin]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "占位符残余未清零");
        StringAssert.Contains(issues[0], "（同仓）");
    }

    [TestMethod]
    public void VerifyRepositories_未勾选端模块目录仍存在_报问题()
    {
        // 移动端未勾选：克隆后应删掉 jnms-Mobile，留着就是没删
        var repo = NewRepo();
        Directory.CreateDirectory(Path.Combine(repo, "jnms-Mobile"));
        var report = RepoReport(repo, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(
            report,
            [ProjectEnd.Service],
            [(ProjectEnd.Mobile, "jnms-Mobile")]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "未勾选该端");
        StringAssert.Contains(issues[0], "jnms-Mobile");
    }

    [TestMethod]
    public void VerifyRepositories_未勾选端模块目录已删除_通过()
    {
        var repo = NewRepo();
        var report = RepoReport(repo, "develop", "TJCES");

        var issues = StepLocalVerifier.VerifyRepositories(
            report,
            [ProjectEnd.Service],
            [(ProjectEnd.Mobile, "jnms-Mobile")]);

        CollectionAssert.AreEqual(Array.Empty<string>(), issues.ToArray());
    }

    // ── Step 6 ────────────────────────────────────────────────────────────

    /// <summary>同仓两端（后端 + 管理端）的推送结论：可分别给分支，用于核对分支一致性。</summary>
    private static StepReport PushTwoEndsReport(
        string localPath, string serviceBranch, string adminBranch, string? commitId)
    {
        static StepReportArtifact Repository(ProjectEnd end, string branch, string? commit) => new()
        {
            Kind = "repository",
            End = end,
            Name = RepoName,
            LocalPath = string.Empty,
            TemplateBranch = branch,
            Extra = new RepositoryArtifactExtra { Cloned = true, CommitId = commit, PlaceholderSources = ["TJCES"] }
        };

        var service = Repository(ProjectEnd.Service, serviceBranch, commitId);
        service.LocalPath = localPath;
        var admin = Repository(ProjectEnd.Admin, adminBranch, commitId);
        admin.LocalPath = localPath;

        return new StepReport { Step = 6, Ok = true, Artifacts = { service, admin } };
    }

    private static void WriteRemoteTrackingRef(string repo, string branch, string sha)
    {
        var gitDir = Path.Combine(repo, ".git");
        Directory.CreateDirectory(Path.Combine(gitDir, "refs", "remotes", "origin"));
        File.WriteAllText(Path.Combine(gitDir, "refs", "remotes", "origin", branch), sha);
    }

    [TestMethod]
    public void VerifyPush_远端跟踪分支就位且提交id一致_通过()
    {
        var repo = NewRepo();
        WriteRemoteTrackingRef(repo, "develop", Sha40);
        var report = PushReport(repo, "develop", Sha40, "TJCES");

        var issues = StepLocalVerifier.VerifyPush(report, [ProjectEnd.Service]);

        CollectionAssert.AreEqual(Array.Empty<string>(), issues.ToArray());
    }

    [TestMethod]
    public void VerifyPush_提交id与自报不一致_报问题()
    {
        var repo = NewRepo();
        WriteRemoteTrackingRef(repo, "develop", Sha40);
        var report = PushReport(repo, "develop", new string('a', 40), "TJCES");

        var issues = StepLocalVerifier.VerifyPush(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "与结论自报");
    }

    [TestMethod]
    public void VerifyPush_无远端跟踪分支_报问题()
    {
        var repo = NewRepo();
        var report = PushReport(repo, "develop", Sha40, "TJCES");

        var issues = StepLocalVerifier.VerifyPush(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "refs/remotes/origin/develop");
    }

    [TestMethod]
    public void VerifyPush_未给提交id_报问题()
    {
        var repo = NewRepo();
        WriteRemoteTrackingRef(repo, "develop", Sha40);
        var report = PushReport(repo, "develop", null, "TJCES");

        var issues = StepLocalVerifier.VerifyPush(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "未给出提交 id");
    }

    [TestMethod]
    public void VerifyPush_同仓两端分支不一致_报问题()
    {
        // 单仓必须推同一分支；两端自报不同分支说明结论本身有问题，不能只核首个
        var repo = NewRepo();
        WriteRemoteTrackingRef(repo, "develop", Sha40);
        var report = PushTwoEndsReport(repo, "develop", "main", Sha40);

        var issues = StepLocalVerifier.VerifyPush(report, [ProjectEnd.Service, ProjectEnd.Admin]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "推送分支不一致");
    }

    // ── Step 7 ────────────────────────────────────────────────────────────

    [TestMethod]
    public void PipelineResidualNotes_有流水线产物_给出残余风险说明()
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
                    Name = "jnms-Service-Deploy",
                    Yaml = "stages:\n  - stage: build",
                    Extra = new PipelineArtifactExtra()
                }
            }
        };

        var notes = StepLocalVerifier.PipelineResidualNotes(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, notes.Count);
        StringAssert.Contains(notes[0], "没有可独立核对的本地证据");
    }

    [TestMethod]
    public void PipelineResidualNotes_无产物_空清单()
    {
        var report = new StepReport { Step = 7, Ok = true };

        Assert.AreEqual(0, StepLocalVerifier.PipelineResidualNotes(report, [ProjectEnd.Service]).Count);
    }
}