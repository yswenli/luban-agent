/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebHostDiscoveryUnitTest
*描述：宿主自述文件的读写、过期判定与清理
*
*****************************************************************************/

using LubanAgentCore.Client;

namespace LubanAgent.Tests;

[TestClass]
public class WebHostDiscoveryUnitTest
{
    private string _directory = null!;
    private string? _originalPath;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"luban-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _originalPath = WebHostDiscovery.FilePathOverride;
        WebHostDiscovery.FilePathOverride = Path.Combine(_directory, "webhost.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        WebHostDiscovery.FilePathOverride = _originalPath;
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言结果
        }
    }

    [TestMethod]
    public void 写入后可读回端口令牌与进程号()
    {
        WebHostDiscovery.Write(new WebHostDescriptor
        {
            BaseAddress = "http://127.0.0.1:18080",
            Token = "tok-1",
            ProcessId = Environment.ProcessId,
            ServerPath = "LubanAgentWebHost.exe",
            StartedAt = DateTimeOffset.Now
        });

        var descriptor = WebHostDiscovery.TryRead();

        Assert.IsNotNull(descriptor);
        Assert.AreEqual("http://127.0.0.1:18080", descriptor.BaseAddress);
        Assert.AreEqual("tok-1", descriptor.Token);
        Assert.AreEqual(Environment.ProcessId, descriptor.ProcessId);
    }

    [TestMethod]
    public void 进程已退出时视为过期并清理()
    {
        WebHostDiscovery.Write(new WebHostDescriptor
        {
            BaseAddress = "http://127.0.0.1:18080",
            ProcessId = int.MaxValue - 1
        });

        Assert.IsNull(WebHostDiscovery.TryRead());
        Assert.IsFalse(File.Exists(WebHostDiscovery.FilePath), "过期文件应被清理，避免客户端反复连到幽灵地址");
    }

    [TestMethod]
    public void 内容损坏时返回空并清理()
    {
        File.WriteAllText(WebHostDiscovery.FilePath, "{ not-json");

        Assert.IsNull(WebHostDiscovery.TryRead());
        Assert.IsFalse(File.Exists(WebHostDiscovery.FilePath));
    }

    [TestMethod]
    public void 删除时不动其他实例的自述()
    {
        WebHostDiscovery.Write(new WebHostDescriptor
        {
            BaseAddress = "http://127.0.0.1:18080",
            ProcessId = Environment.ProcessId
        });

        WebHostDiscovery.Delete(Environment.ProcessId + 1);
        Assert.IsTrue(File.Exists(WebHostDiscovery.FilePath), "进程号不匹配时不应删除别人的自述文件");

        WebHostDiscovery.Delete(Environment.ProcessId);
        Assert.IsFalse(File.Exists(WebHostDiscovery.FilePath));
    }
}