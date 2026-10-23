/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： YunxiaoToolDefinitionsUnitTest
*描述：验证云效 MCP 工具定义（13 个，PRD §4.11.3）的名称、数量与 schema 形状
*      v0.3 助手驱动：删 codeup_clone / codeup_push（git 一律走 git 工具组），
*      补 codeup_list_template_repos / codeup_get_repo / codeup_list_groups 三个只读工具
*      迁自框架 LuBan.XTestProject（W3 去厂商化：厂商用例随实现迁入产品测试项目）
*****************************************************************************/

using System.Text.Json;
using LubanAgentCore.Connectors.AliyunDevOps.Mcp;

namespace LubanAgent.Tests;

[TestClass]
public class YunxiaoToolDefinitionsUnitTest
{
    private static readonly string[] ExpectedNames =
    [
        "codeup_search_repos",
        "codeup_list_template_repos",
        "codeup_get_repo",
        "codeup_create_repo",
        "codeup_list_groups",
        "codeup_list_branches",
        "codeup_create_group",
        "flow_list_pipelines",
        "flow_list_host_groups",
        "flow_get_host_group",
        "flow_create_pipeline",
        "flow_update_pipeline",
        "flow_run_pipeline"
    ];

    [TestMethod]
    public void GetAllTools_ReturnsExpectedBaseNames()
    {
        var tools = YunxiaoToolDefinitions.GetAllTools().ToList();

        Assert.AreEqual(13, tools.Count, "工具总数应为 13（v0.3 助手驱动：删 codeup_clone / codeup_push，补 3 个只读工具）");
        CollectionAssert.AreEquivalent(ExpectedNames, tools.Select(t => t.Name).ToList());
    }

    [TestMethod]
    public void GetAllTools_不含克隆与推送工具_git操作一律走git工具组()
    {
        var names = YunxiaoToolDefinitions.GetAllTools().Select(t => t.Name).ToList();

        Assert.IsFalse(names.Contains("codeup_clone"), "codeup_clone 已删除（git 操作走 git 工具组）。");
        Assert.IsFalse(names.Contains("codeup_push"), "codeup_push 已删除（git 操作走 git 工具组）。");
    }

    [TestMethod]
    public void GetAllTools_NamesAreUnique_AndCarryNoMcpPrefix()
    {
        var names = YunxiaoToolDefinitions.GetAllTools().Select(t => t.Name).ToList();

        Assert.AreEqual(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "工具名不得重复");
        // 前缀由 MCPToolPlugin 统一添加，定义处再带前缀会出现 mcp_yunxiao_mcp_yunxiao_*
        Assert.IsFalse(names.Any(n => n.StartsWith("mcp_", StringComparison.OrdinalIgnoreCase)), "定义处不得自带 mcp_ 前缀");
    }

    [TestMethod]
    public void GetAllTools_EachHasDescriptionAndParseableSchema()
    {
        foreach (var tool in YunxiaoToolDefinitions.GetAllTools())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} 缺少描述");

            using var doc = JsonDocument.Parse(tool.InputSchema!);
            Assert.AreEqual("object", doc.RootElement.GetProperty("type").GetString(), $"{tool.Name} schema 根类型应为 object");
            Assert.AreEqual(JsonValueKind.Object, doc.RootElement.GetProperty("properties").ValueKind, $"{tool.Name} schema 缺少 properties");
        }
    }

    [TestMethod]
    public void CodeupListBranches_Schema_RequiresRepositoryId()
    {
        var tool = YunxiaoToolDefinitions.GetAllTools().Single(t => t.Name == "codeup_list_branches");

        using var doc = JsonDocument.Parse(tool.InputSchema!);
        var required = doc.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        CollectionAssert.Contains(required, "repositoryId");

        var properties = doc.RootElement.GetProperty("properties");
        foreach (var name in new[] { "repositoryId", "page", "perPage", "sort", "search", "organizationId" })
        {
            Assert.IsTrue(properties.TryGetProperty(name, out _), $"codeup_list_branches 缺少参数 {name}");
        }
    }

    /// <summary>工具定义口径（v0.30）：`flow_create_pipeline` 取代 `flow_clone_pipeline`
    /// （云效无「复制流水线」接口，且只收顶层 `stages:` 的新版 YAML）。</summary>
    [TestMethod]
    public void FlowCreatePipeline_Schema_RequiresNameAndContent()
    {
        var tool = YunxiaoToolDefinitions.GetAllTools().Single(t => t.Name == "flow_create_pipeline");

        using var doc = JsonDocument.Parse(tool.InputSchema!);
        var required = doc.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        CollectionAssert.AreEquivalent(new[] { "name", "content" }, required);

        var properties = doc.RootElement.GetProperty("properties");
        foreach (var name in new[] { "name", "content", "organizationId" })
        {
            Assert.IsTrue(properties.TryGetProperty(name, out _), $"flow_create_pipeline 缺少参数 {name}");
        }

        Assert.IsFalse(properties.TryGetProperty("sourcePipelineId", out _), "新版口径不再有 sourcePipelineId（不再克隆）");
    }

    [TestMethod]
    public void CodeupCreateGroup_Schema_RequiresNameAndPath()
    {
        var tool = YunxiaoToolDefinitions.GetAllTools().Single(t => t.Name == "codeup_create_group");

        using var doc = JsonDocument.Parse(tool.InputSchema!);
        var required = doc.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        CollectionAssert.Contains(required, "name");
        CollectionAssert.Contains(required, "path");

        var properties = doc.RootElement.GetProperty("properties");
        foreach (var name in new[] { "name", "path", "parentId", "visibility", "description", "organizationId" })
        {
            Assert.IsTrue(properties.TryGetProperty(name, out _), $"codeup_create_group 缺少参数 {name}");
        }
    }

    // ── v0.3 新增的三个只读工具 ─────────────────────────────────────────────

    /// <summary>模板库查询（v0.3）：定位模板仓以取 <c>templateProjectId</c>；按注释「无 search、需全量拉取」。</summary>
    [TestMethod]
    public void CodeupListTemplateRepos_Schema_支持模板类型与分页()
    {
        var tool = YunxiaoToolDefinitions.GetAllTools().Single(t => t.Name == "codeup_list_template_repos");

        using var doc = JsonDocument.Parse(tool.InputSchema!);
        var properties = doc.RootElement.GetProperty("properties");
        foreach (var name in new[] { "templateType", "page", "perPage", "organizationId" })
        {
            Assert.IsTrue(properties.TryGetProperty(name, out _), $"codeup_list_template_repos 缺少参数 {name}");
        }

        Assert.IsFalse(properties.TryGetProperty("search", out _), "本接口没有 search 入参（不支持按名过滤，需客户端全量匹配）");
        StringAssert.Contains(tool.Description, "codeup_search_repos");
    }

    /// <summary>仓库详情（v0.3）：唯一返回 <c>httpUrlToRepo</c> 的入口，克隆地址取自这里。</summary>
    [TestMethod]
    public void CodeupGetRepo_Schema_RequiresRepositoryId()
    {
        var tool = YunxiaoToolDefinitions.GetAllTools().Single(t => t.Name == "codeup_get_repo");

        using var doc = JsonDocument.Parse(tool.InputSchema!);
        var required = doc.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        CollectionAssert.AreEquivalent(new[] { "repositoryId" }, required);

        var properties = doc.RootElement.GetProperty("properties");
        foreach (var name in new[] { "repositoryId", "organizationId" })
        {
            Assert.IsTrue(properties.TryGetProperty(name, out _), $"codeup_get_repo 缺少参数 {name}");
        }

        StringAssert.Contains(tool.Description, "httpUrlToRepo");
    }

    /// <summary>代码组列表（v0.3）：建库前「先查后建」判断目标组是否已存在。</summary>
    [TestMethod]
    public void CodeupListGroups_Schema_支持搜索与父级过滤()
    {
        var tool = YunxiaoToolDefinitions.GetAllTools().Single(t => t.Name == "codeup_list_groups");

        using var doc = JsonDocument.Parse(tool.InputSchema!);
        var properties = doc.RootElement.GetProperty("properties");
        foreach (var name in new[] { "search", "parentId", "page", "perPage", "orderBy", "sort", "organizationId" })
        {
            Assert.IsTrue(properties.TryGetProperty(name, out _), $"codeup_list_groups 缺少参数 {name}");
        }
    }
}