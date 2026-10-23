/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectStepLocalVerifierUnitTest
*创建时间：2026/10/22
*描述：Step 3/6/7 插件侧本地独立复核器（设计 §6.1.4 第 4 环）单测——
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

    /// <summary>造一个「像 git 仓」的目录：.git/HEAD + 一个本地分支 + 一份源码。</summary>
    private string NewRepo(string pin = "develop", string sourceContent = "class App { }", bool withGit = true)
    {
        var repo = Path.Combine(NewRoot(), "jnms-Service");
        Directory.CreateDirectory(Path.Combine(repo, "src"));
        File.WriteAllText(Path.Combine(repo, "src", "App.cs"), sourceContent);

        if (withGit)
        {
            var gitDir = Path.Combine(repo, ".git");
            Directory.CreateDirectory(Path.Combine(gitDir, "refs", "heads"));
            File.WriteAllText(Path.Combine(gitDir, "HEAD"), $"ref: refs/heads/{pin}\n");
            File.WriteAllText(Path.Combine(gitDir, "refs", "heads", pin), Sha40);
        }

        return repo;
    }

    private static StepReport RepoReport(string localPath, string pin, params string[] tokens) => new()
    {
        Step = 3,
        Ok = true,
        Artifacts =
        {
            new StepReportArtifact
            {
                Kind = "repository",
                End = ProjectEnd.Service,
                Name = "jnms-Service",
                LocalPath = localPath,
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
    public void VerifyRepositories_未给占位符令牌_报问题()
    {
        var repo = NewRepo();
        var report = RepoReport(repo, "develop");

        var issues = StepLocalVerifier.VerifyRepositories(report, [ProjectEnd.Service]);

        Assert.AreEqual(1, issues.Count);
        StringAssert.Contains(issues[0], "placeholderSources 为空");
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

    // ── Step 6 ────────────────────────────────────────────────────────────

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