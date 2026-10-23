/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： NewProjectKnowledgeAssetsUnitTest
*创建时间：2026/10/22
*描述：随包分发的知识资产（docs/ 与 skills/）的「存在性与结构」单测（设计 §6.5 / §12-2）：
*      ① 三端流水线 YAML 模板齐备且为云效可收的 stages: 顶层形态；
*      ② docs/README.md 索引覆盖全部 docs/*.md（不漏文件）；
*      ③ skills/*.md 与 plugin.json 的「组织专有值禁用扫描」——唯一副本原则的可执行检查。
*      为什么扫 skills 不扫 docs：docs/ 本就是组织专有值的唯一副本（合法含 TJCES / Harbor 等）。
*
*****************************************************************************/

namespace LubanAgent.Tests;

[TestClass]
public class NewProjectKnowledgeAssetsUnitTest
{
    /// <summary>技能正文不得复述的组织专有值 / 已删类型名（§6.5）。</summary>
    private static readonly string[] ForbiddenInSkills =
    [
        "harbor.tjingcai.com",
        "r3x6es4esgbsn6ur",
        "sm7mho5x9ex945rp",
        "tejingcai",
        "TJCES",
        "codeup_clone",
        "codeup_push",
        "PipelineRenderer",
        "PipelineTemplateLibrary",
        "PipelineYamlOverride",
        "PushRetryPolicy"
    ];

    private static readonly string[] PipelineTemplates =
    [
        "pipeline-service.yml.md",
        "pipeline-admin.yml.md",
        "pipeline-mobile.yml.md"
    ];

    /// <summary>从测试程序集目录向上找到仓库根（含 LubanAgent.slnx 的目录）。</summary>
    private static string PluginDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "LubanAgent.Plugin.NewProject");
            if (File.Exists(Path.Combine(dir.FullName, "LubanAgent.slnx")) && Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new AssertFailedException("找不到插件目录（未能在上级目录中定位含 LubanAgent.slnx 的仓库根）。");
    }

    private static string DocsDirectory() => Path.Combine(PluginDirectory(), "docs");

    private static string SkillsDirectory() => Path.Combine(PluginDirectory(), "skills");

    // ── docs/：三端模板结构 ────────────────────────────────────────────────

    [TestMethod]
    public void Docs_三端流水线模板齐备且为stages顶层形态()
    {
        foreach (var template in PipelineTemplates)
        {
            var path = Path.Combine(DocsDirectory(), template);
            Assert.IsTrue(File.Exists(path), $"缺少流水线模板：{template}");

            var content = File.ReadAllText(path);
            StringAssert.Contains(content, "stages:", $"{template} 必须给云效可收的 stages: 顶层形态。");
        }
    }

    [TestMethod]
    public void Docs_关键知识正文齐备()
    {
        foreach (var name in new[]
                 {
                     "README.md", "org-reference.md", "step3-repositories.md",
                     "step6-push.md", "pipeline-conventions.md"
                 })
        {
            Assert.IsTrue(File.Exists(Path.Combine(DocsDirectory(), name)), $"缺少知识正文：{name}");
        }
    }

    [TestMethod]
    public void Docs_README索引覆盖全部docs文件()
    {
        var docsDir = DocsDirectory();
        var readme = File.ReadAllText(Path.Combine(docsDir, "README.md"));

        var files = Directory.GetFiles(docsDir, "*.md")
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, "README.md", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.IsTrue(files.Count >= 7, $"docs 正文至少 7 份（实为 {files.Count}）。");

        foreach (var file in files)
        {
            StringAssert.Contains(readme, $"./{file}", $"docs/README.md 索引未覆盖 {file}。");
        }
    }

    // ── skills/：唯一副本原则的可执行检查 ──────────────────────────────────

    [TestMethod]
    public void Skills_禁用值扫描_零命中()
    {
        foreach (var path in Directory.GetFiles(SkillsDirectory(), "*.md"))
        {
            var content = File.ReadAllText(path);
            foreach (var forbidden in ForbiddenInSkills)
            {
                Assert.IsFalse(
                    content.Contains(forbidden, StringComparison.Ordinal),
                    $"{Path.GetFileName(path)} 出现组织专有值 / 已删类型名「{forbidden}」——应只写「读 docs 的哪一节」。");
            }
        }
    }

    [TestMethod]
    public void Skills_六篇技能正文齐备且非空()
    {
        var files = Directory.GetFiles(SkillsDirectory(), "*.md").ToList();

        Assert.AreEqual(6, files.Count, "技能正文应为 6 篇（create / clone-template / setup-db / build / push-repo / create-pipeline）。");

        foreach (var path in files)
        {
            Assert.IsTrue(new FileInfo(path).Length > 0, $"{Path.GetFileName(path)} 为空。");
        }
    }

    [TestMethod]
    public void PluginJson_描述不含禁用值()
    {
        var manifest = Path.Combine(PluginDirectory(), "plugin.json");
        Assert.IsTrue(File.Exists(manifest), "缺少 plugin.json。");

        var content = File.ReadAllText(manifest);
        foreach (var forbidden in ForbiddenInSkills)
        {
            Assert.IsFalse(
                content.Contains(forbidden, StringComparison.Ordinal),
                $"plugin.json 出现「{forbidden}」。");
        }
    }
}