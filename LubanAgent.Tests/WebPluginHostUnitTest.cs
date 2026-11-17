/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Tests
*文件名： WebPluginHostUnitTest
*描述：Web 宿主插件管理侧纯函数单测：路径归一与越界校验、页面声明回读与校验、zip 解包守卫、插件根定位
*      说明：安装 / 升级 / 启停需要 PluginManager 与用户作用域（sealed 具体类），覆盖依赖手动冒烟。
*
*****************************************************************************/

namespace LubanAgent.Tests;

using LubanAgentWebHost.Hosting;
using LubanAgentWebHost.Services;

[TestClass]
public class WebPluginHostUnitTest
{
    // ---------------- 路径与白名单 ----------------

    [TestMethod]
    public void 相对路径归一化_统一分隔符并拒绝越界()
    {
        Assert.AreEqual("web/index.html", WebPluginPaths.NormalizeRelative("web\\index.html"));
        Assert.AreEqual("web/a/b.js", WebPluginPaths.NormalizeRelative(" web//a//b.js "));
        Assert.IsNull(WebPluginPaths.NormalizeRelative("../evil.html"));
        Assert.IsNull(WebPluginPaths.NormalizeRelative("web/../../evil.html"));
        Assert.IsNull(WebPluginPaths.NormalizeRelative("/abs/index.html"));
        Assert.IsNull(WebPluginPaths.NormalizeRelative("C:/x/index.html"));
        Assert.IsNull(WebPluginPaths.NormalizeRelative("web/./a.html"));
        Assert.IsNull(WebPluginPaths.NormalizeRelative("   "));
    }

    [TestMethod]
    public void 路径包含判断_根目录本身算在内部_前缀相近的兄弟目录不算()
    {
        var root = Path.Combine(Path.GetTempPath(), "luban-plugin-root-" + Guid.NewGuid().ToString("N"));

        Assert.IsTrue(WebPluginPaths.IsUnder(root, root));
        Assert.IsTrue(WebPluginPaths.IsUnder(Path.Combine(root, "web", "index.html"), root));
        Assert.IsFalse(WebPluginPaths.IsUnder(Path.Combine(root, "..", "other"), root));
        Assert.IsFalse(WebPluginPaths.IsUnder(root + "-sibling", root));
        Assert.IsFalse(WebPluginPaths.IsUnder("", root));
        Assert.IsFalse(WebPluginPaths.IsUnder(root, ""));
    }

    [TestMethod]
    public void 页面路径_必须在web目录下且为html()
    {
        Assert.IsTrue(WebPluginPaths.IsUnderWebFolder("web/index.html"));
        Assert.IsFalse(WebPluginPaths.IsUnderWebFolder("web"));
        Assert.IsFalse(WebPluginPaths.IsUnderWebFolder("webx/index.html"));
        Assert.IsFalse(WebPluginPaths.IsUnderWebFolder(null));

        Assert.IsTrue(WebPluginPaths.IsHtmlPage("web/a.HTML"));
        Assert.IsTrue(WebPluginPaths.IsHtmlPage("web/a.htm"));
        Assert.IsFalse(WebPluginPaths.IsHtmlPage("web/a.js"));
        Assert.IsFalse(WebPluginPaths.IsHtmlPage(null));
    }

    [TestMethod]
    public void 资源白名单_拒绝字体脚本模块等静态通道无法识别MIME的扩展名()
    {
        Assert.IsTrue(WebPluginPaths.IsMirrorAsset("web/index.html"));
        Assert.IsTrue(WebPluginPaths.IsMirrorAsset("web/app.css"));
        Assert.IsTrue(WebPluginPaths.IsMirrorAsset("web/app.js"));
        Assert.IsTrue(WebPluginPaths.IsMirrorAsset("web/logo.svg"));

        Assert.IsFalse(WebPluginPaths.IsMirrorAsset("web/font.woff2"));
        Assert.IsFalse(WebPluginPaths.IsMirrorAsset("web/data.json"));
        Assert.IsFalse(WebPluginPaths.IsMirrorAsset("web/app.mjs"));
    }

    [TestMethod]
    public void 控制器同名目录_命中宿主控制器名()
    {
        Assert.IsTrue(WebPluginPaths.IsHostControllerDirectoryName("plugin"));
        Assert.IsTrue(WebPluginPaths.IsHostControllerDirectoryName("Skill"));
        Assert.IsTrue(WebPluginPaths.IsHostControllerDirectoryName(" llmwiki "));
        Assert.IsFalse(WebPluginPaths.IsHostControllerDirectoryName("assets"));
        Assert.IsFalse(WebPluginPaths.IsHostControllerDirectoryName(""));
    }

    [TestMethod]
    public void 镜像戳_八位十六进制且每次不同()
    {
        var first = WebPluginPaths.CreateStamp();
        var second = WebPluginPaths.CreateStamp();

        Assert.AreEqual(8, first.Length);
        Assert.IsTrue(first.All(ch => char.IsAsciiHexDigitLower(ch)), first);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void 页面URL与镜像目录_都挂在静态根的plugin_web下()
    {
        Assert.AreEqual(
            "/plugin-web/com.example/abcd1234/web/index.html",
            WebPluginPaths.PageUrl("com.example", "abcd1234", "web/index.html"));

        // 包内文件名允许空格与 #，逐段转义后才能当 iframe 地址
        Assert.AreEqual(
            "/plugin-web/com.example/abcd1234/web/a%23b.html",
            WebPluginPaths.PageUrl("com.example", "abcd1234", "web/a#b.html"));
        Assert.AreEqual(
            "/plugin-web/com.example/abcd1234/web/a%20b.html",
            WebPluginPaths.PageUrl("com.example", "abcd1234", "web/a b.html"));

        Assert.IsTrue(WebPluginPaths.IsUnder(
            WebPluginPaths.MirrorDirectory("com.example", "abcd1234"),
            WebPluginPaths.MirrorRoot));
    }

    [TestMethod]
    public void 插件根默认值_与桌面端分开且显式配置优先()
    {
        Assert.AreNotEqual(WebPluginPaths.DefaultWebPluginsRoot, WebPluginPaths.DefaultDesktopPluginsRoot);
        StringAssert.Contains(WebPluginPaths.DefaultStateFilePath, "plugin-states.json");

        var configured = Path.Combine(Path.GetTempPath(), $"luban-web-plugins-{Guid.NewGuid():N}");
        var fallback = Path.Combine(Path.GetTempPath(), $"luban-web-plugins-fallback-{Guid.NewGuid():N}");

        Assert.AreEqual(Path.GetFullPath(configured), WebPluginPaths.ResolveWebPluginsRoot(configured));
        Assert.AreEqual(Path.GetFullPath(fallback), WebPluginPaths.ResolveWebPluginsRoot("  ", fallback));
        Assert.AreEqual(Path.GetFullPath(fallback), WebPluginPaths.ResolveDesktopPluginsRoot(fallback));
    }

    // ---------------- 页面声明（plugin.json 回读） ----------------

    [TestMethod]
    public void 页面声明回读_取到webPage并归一化路径()
    {
        var directory = CreatePluginDirectory(ManifestWithPage("web/index.html"), "web/index.html");
        try
        {
            var entries = PluginPageManifest.Read(directory);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("main", entries[0].EntryId);
            Assert.AreEqual("示例", entries[0].Label);
            Assert.AreEqual("plug", entries[0].Icon);
            Assert.AreEqual("web/index.html", entries[0].WebPage);
            Assert.AreEqual(0, PluginPageManifest.Validate(directory).Count);
            Assert.AreEqual(1, PluginPageManifest.ReadValid(directory).Count);
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [TestMethod]
    public void 页面声明校验_web之外的页面被拒()
    {
        var directory = CreatePluginDirectory(ManifestWithPage("index.html"), "index.html");
        try
        {
            var errors = PluginPageManifest.Validate(directory);

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "web/");
            Assert.AreEqual(0, PluginPageManifest.ReadValid(directory).Count);
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [TestMethod]
    public void 页面声明校验_目录穿越被拒()
    {
        var directory = CreatePluginDirectory(ManifestWithPage("../evil.html"));
        try
        {
            var errors = PluginPageManifest.Validate(directory);

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "非法");
            Assert.AreEqual(0, PluginPageManifest.Read(directory).Count);
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [TestMethod]
    public void 页面声明校验_非html与缺失文件都被拒()
    {
        var directory = CreatePluginDirectory(
            ManifestWithPage("web/app.js", "web/missing.html"),
            "web/app.js");
        try
        {
            var errors = PluginPageManifest.Validate(directory);

            Assert.AreEqual(2, errors.Count);
            Assert.IsTrue(errors.Any(message => message.Contains(".html", StringComparison.Ordinal)));
            Assert.IsTrue(errors.Any(message => message.Contains("不存在", StringComparison.Ordinal)));
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [TestMethod]
    public void 页面声明可用列表_过滤掉缺失文件的条目()
    {
        var directory = CreatePluginDirectory(
            ManifestWithPage("web/index.html", "web/missing.html"),
            "web/index.html");
        try
        {
            var valid = PluginPageManifest.ReadValid(directory);

            Assert.AreEqual(1, valid.Count);
            Assert.AreEqual("web/index.html", valid[0].WebPage);
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [TestMethod]
    public void 清单没有页面声明或JSON损坏_不抛异常且返回空()
    {
        var withoutPages = CreatePluginDirectory("{ \"id\": \"com.example\", \"name\": \"示例\" }");
        var broken = CreatePluginDirectory("{ not-json ");
        try
        {
            Assert.AreEqual(0, PluginPageManifest.Read(withoutPages).Count);
            Assert.AreEqual(0, PluginPageManifest.Validate(withoutPages).Count);

            Assert.AreEqual(0, PluginPageManifest.Read(broken).Count);
            Assert.AreEqual(0, PluginPageManifest.Validate(broken).Count);
        }
        finally
        {
            DeleteQuietly(withoutPages);
            DeleteQuietly(broken);
        }
    }

    // ---------------- zip 解包 ----------------

    [TestMethod]
    public void 解包_保留目录层级与文件内容()
    {
        var target = CreateEmptyDirectory();
        try
        {
            var bytes = CreateZip(
                ("plugin.json", "{ \"id\": \"com.example\" }"),
                ("web/index.html", "<html>ok</html>"));

            WebPluginService.ExtractZip(bytes, target);

            Assert.AreEqual(
                "{ \"id\": \"com.example\" }",
                File.ReadAllText(Path.Combine(target, "plugin.json")));
            Assert.AreEqual(
                "<html>ok</html>",
                File.ReadAllText(Path.Combine(target, "web", "index.html")));
        }
        finally
        {
            DeleteQuietly(target);
        }
    }

    [TestMethod]
    public void 解包_拒绝目录穿越条目且不落盘()
    {
        var target = CreateEmptyDirectory();
        var outside = Path.Combine(Path.GetDirectoryName(target)!, "evil.txt");
        try
        {
            var bytes = CreateZip(("../evil.txt", "boom"));

            var error = Assert.ThrowsExactly<InvalidOperationException>(
                () => WebPluginService.ExtractZip(bytes, target));

            StringAssert.Contains(error.Message, "非法路径");
            Assert.IsFalse(File.Exists(outside));
        }
        finally
        {
            DeleteQuietly(target);
            DeleteQuietly(outside);
        }
    }

    [TestMethod]
    public void 解包_条目数超限被拒()
    {
        var target = CreateEmptyDirectory();
        try
        {
            var bytes = CreateZip(
                Enumerable.Range(0, 501)
                    .Select(index => ($"file-{index}.txt", "x"))
                    .ToArray());

            var error = Assert.ThrowsExactly<InvalidOperationException>(
                () => WebPluginService.ExtractZip(bytes, target));

            StringAssert.Contains(error.Message, "条目数");
        }
        finally
        {
            DeleteQuietly(target);
        }
    }

    [TestMethod]
    public void 插件根定位_支持根目录直放与单一顶层目录()
    {
        var direct = CreatePluginDirectory(ManifestWithPage("web/index.html"), "web/index.html");
        var nested = CreateEmptyDirectory();
        var nestedTop = Path.Combine(nested, "com.example.myplugin");
        Directory.CreateDirectory(nestedTop);
        File.WriteAllText(Path.Combine(nestedTop, "plugin.json"), ManifestWithPage("web/index.html"));
        var multiple = CreateEmptyDirectory();
        Directory.CreateDirectory(Path.Combine(multiple, "a"));
        Directory.CreateDirectory(Path.Combine(multiple, "b"));

        try
        {
            Assert.AreEqual(Path.GetFullPath(direct), Path.GetFullPath(WebPluginService.LocatePluginRoot(direct)!));
            Assert.AreEqual(Path.GetFullPath(nestedTop), Path.GetFullPath(WebPluginService.LocatePluginRoot(nested)!));
            Assert.IsNull(WebPluginService.LocatePluginRoot(multiple));
        }
        finally
        {
            DeleteQuietly(direct);
            DeleteQuietly(nested);
            DeleteQuietly(multiple);
        }
    }

    // ---------------- 升级失败回补 ----------------

    [TestMethod]
    public void 升级失败回补_只补缺失文件且不覆盖已存在内容()
    {
        var snapshot = CreateEmptyDirectory();
        var target = CreateEmptyDirectory();
        Directory.CreateDirectory(Path.Combine(snapshot, "lib"));
        Directory.CreateDirectory(Path.Combine(target, "lib"));
        File.WriteAllText(Path.Combine(snapshot, "plugin.json"), "{ \"id\": \"com.example.demo\" }");
        File.WriteAllText(Path.Combine(snapshot, "lib", "a.dll"), "snap-a");
        File.WriteAllText(Path.Combine(snapshot, "lib", "b.dll"), "snap-b");
        // 半删后的目标：清单丢了，a.dll 还在（被占用删不掉、内容完好）
        File.WriteAllText(Path.Combine(target, "lib", "a.dll"), "snap-a");

        try
        {
            var restored = WebPluginService.RestoreMissingFiles(snapshot, target);

            Assert.AreEqual(2, restored);
            Assert.IsTrue(File.Exists(Path.Combine(target, "plugin.json")));
            Assert.AreEqual("snap-b", File.ReadAllText(Path.Combine(target, "lib", "b.dll")));
            Assert.AreEqual("snap-a", File.ReadAllText(Path.Combine(target, "lib", "a.dll")));
        }
        finally
        {
            DeleteQuietly(snapshot);
            DeleteQuietly(target);
        }
    }

    [TestMethod]
    public void 升级失败回补_快照或目标缺失时不做任何事()
    {
        var target = CreateEmptyDirectory();

        try
        {
            Assert.AreEqual(0, WebPluginService.RestoreMissingFiles(null, target));
            Assert.AreEqual(
                0,
                WebPluginService.RestoreMissingFiles(
                    Path.Combine(Path.GetTempPath(), "luban-plugin-missing-" + Guid.NewGuid().ToString("N")),
                    target));
            Assert.AreEqual(0, WebPluginService.RestoreMissingFiles(target, null));
            Assert.AreEqual(0, Directory.GetFileSystemEntries(target).Length);
        }
        finally
        {
            DeleteQuietly(target);
        }
    }

    // ---------------- 测试数据 ----------------

    private static string ManifestWithPage(params string[] webPages)
    {
        var contributions = webPages.Select(
            page => $"{{ \"id\": \"{(webPages.Length == 1 ? "main" : Guid.NewGuid().ToString("N"))}\", \"label\": \"示例\", \"icon\": \"plug\", \"webPage\": \"{page}\" }}");

        return $$"""
            {
              "id": "com.example.myplugin",
              "name": "示例插件",
              "version": "1.0.0",
              "entryAssembly": "Com.Example.MyPlugin.dll",
              "uiContributions": [ {{string.Join(", ", contributions)}} ]
            }
            """;
    }

    private static string CreatePluginDirectory(string manifest, params string[] files)
    {
        var directory = CreateEmptyDirectory();
        File.WriteAllText(Path.Combine(directory, WebPluginPaths.ManifestFileName), manifest);

        foreach (var file in files)
        {
            var path = Path.Combine(directory, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<html></html>");
        }

        return directory;
    }

    private static string CreateEmptyDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "luban-plugin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static byte[] CreateZip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}