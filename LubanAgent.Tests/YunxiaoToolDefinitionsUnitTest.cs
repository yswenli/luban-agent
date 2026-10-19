/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： YunxiaoToolDefinitionsUnitTest
*描述：验证云效 MCP 工具定义（10 个，PRD §4.11.3）的名称、数量与 schema 形状
*      迁自框架 LuBan.XTestProject（W3 去厂商化：厂商用例随实现迁入产品测试项目）
*****************************************************************************/

using System.Text.Json;
using LubanAgent.Connectors.AliyunDevOps.Mcp;

namespace LubanAgent.Tests;

[TestClass]
public class YunxiaoToolDefinitionsUnitTest
{
    private static readonly string[] ExpectedNames =
    [
        "codeup_search_repos",
        "codeup_clone",
        "codeup_create_repo",
        "codeup_push",
        "codeup_list_branches",
        "codeup_create_group",
        "flow_list_pipelines",
        "flow_clone_pipeline",
        "flow_update_pipeline",
        "flow_run_pipeline"
    ];

    [TestMethod]
    public void GetAllTools_ReturnsTenTools_WithExpectedBaseNames()
    {
        var tools = YunxiaoToolDefinitions.GetAllTools().ToList();

        Assert.AreEqual(10, tools.Count, "工具总数应为 10（PRD §4.11.3）");
        CollectionAssert.AreEquivalent(ExpectedNames, tools.Select(t => t.Name).ToList());
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
}