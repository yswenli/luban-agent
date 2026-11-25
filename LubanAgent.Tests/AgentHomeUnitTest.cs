/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： AgentHomeUnitTest
*描述：用户级数据根目录（~/.luban-agent）解析与各外部文件落点回归测试
*
*****************************************************************************/

using LubanAgentCore.Infrastructure;

namespace LubanAgent.Tests;

[TestClass]
public class AgentHomeUnitTest
{
    [TestMethod]
    public void Root_IsUserProfileLubanAgent()
    {
        Assert.IsTrue(Path.IsPathRooted(AgentHome.Root), AgentHome.Root);

        // 支持 LUBAN_AGENT_HOME 覆盖（测试/便携部署隔离），覆盖时按覆盖值断言
        var overridden = Environment.GetEnvironmentVariable(AgentHome.HomeEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(overridden))
        {
            Assert.AreEqual(".luban-agent", Path.GetFileName(AgentHome.Root));
        }
        else
        {
            Assert.AreEqual(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(overridden.Trim())),
                AgentHome.Root,
                $"{AgentHome.HomeEnvironmentVariable} 覆盖后根目录应等于覆盖值");
        }
    }

    [TestMethod]
    public void AllExternalPaths_AreUnderRoot()
    {
        var paths = new[]
        {
            AgentHome.ConfigFile,
            AgentHome.UsersDirectory,
            AgentHome.ModelsDirectory,
            AgentHome.DatabasePath,
            AgentHome.LocalMemoryDatabasePath,
            AgentHome.PluginsDirectory,
            AgentHome.PluginsStateFile,
            AgentHome.PluginsStateDirectory,
            AgentHome.WebHostDirectory,
            AgentHome.WebPluginsDirectory,
            AgentHome.WebPluginStateFile,
            AgentHome.PrdReceiptDirectory,
            AgentHome.WebHostInfoFile,
            AgentHome.WebHostCertificateFile,
        };

        foreach (var path in paths)
        {
            Assert.IsTrue(IsUnder(path, AgentHome.Root), $"{path} 应位于 {AgentHome.Root} 之下");
        }
    }

    [TestMethod]
    public void StorageDefaults_AreUnderRoot()
    {
        var defaults = new[]
        {
            LubanAgentWebHost.Hosting.WebPluginPaths.DefaultWebPluginsRoot,
            LubanAgentWebHost.Hosting.WebPluginPaths.DefaultStateFilePath,
            LubanAgentWebHost.Services.Init.InitTaskStore.DefaultFilePath(),
            LubanAgentWebHost.Services.Init.ProjectInitStore.DefaultFilePath(),
            LubanAgentWebHost.Services.TaskDetail.TaskSessionStore.DefaultFilePath(),
            LubanAgentWebHost.Services.Prd.PrdReceiptStore.DefaultRoot,
            LubanAgent.Plugin.NewProject.Services.NewProjectStateStore.DefaultStateDirectory,
            LubanAgentWebHost.Hosting.WebHostTls.DefaultCertificatePath,
        };

        foreach (var path in defaults)
        {
            Assert.IsTrue(IsUnder(path, AgentHome.Root), $"{path} 应位于 {AgentHome.Root} 之下");
        }
    }

    [TestMethod]
    public void TryResolveSqliteDataSource_RewritesRelativePath()
    {
        var rewritten = AgentHome.TryResolveSqliteDataSource(
            "Data Source=./luban-ai-agent.db;Cache=Shared", out var resolved);

        Assert.IsTrue(rewritten);
        StringAssert.Contains(resolved!, AgentHome.DatabasePath);
        StringAssert.Contains(resolved!, "Cache=Shared");
    }

    [TestMethod]
    public void TryResolveSqliteDataSource_KeepsAbsolutePath()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "custom.db");
        var rewritten = AgentHome.TryResolveSqliteDataSource(
            $"Data Source={absolute};Cache=Shared", out var resolved);

        Assert.IsFalse(rewritten);
        Assert.AreEqual($"Data Source={absolute};Cache=Shared", resolved);
    }

    [TestMethod]
    public void TryResolveSqliteDataSource_IgnoresMissingConnection()
    {
        Assert.IsFalse(AgentHome.TryResolveSqliteDataSource(null, out _));
        Assert.IsFalse(AgentHome.TryResolveSqliteDataSource(string.Empty, out _));
    }

    private static bool IsUnder(string path, string root)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}