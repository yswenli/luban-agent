# 图片/媒体类型与体积格式化去重 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 CLI/Codex 与框架内重复的「扩展名→MIME / 是否图片判定 / SkiaSharp 图片原语 / 文件体积格式化」统一下沉到 `LuBan.Common`，`DefaultAttachmentProcessor` 复用，行为除已确认项外保持不变。

**Architecture:** 在 `LuBan.Common` 新增 `IO.MediaTypeUtil`（媒体类型单一来源）与 `IO.FileUtil.FormatFileSize`，并在既有 `ImageUtil` 增补 SkiaSharp 原语；`LuBan.AIAgent.DefaultAttachmentProcessor` 改为消费这些公共设施（保留全部策略逻辑）；CLI/Codex 侧删除私有重复实现改为委托。设计依据见 `docs/superpowers/specs/2026-10-13-agent-image-mime-dedup-design.md`（commit `54d75fb`）。

**Tech Stack:** .NET 10（`net10.0`）、SkiaSharp 4.153.1、MSTest（`LuBan.UnitTestProject.csproj`）、Terminal.Gui（CLI）、Avalonia 12（Codex）。

**仓库与提交边界：** 本计划跨两个独立 git 仓库——`luban-framework/`（Task 1-4、8）与 `luban-agent/`（Task 5-7）。**每个 Task 的 commit 在对应仓库内执行**；不存在能同时覆盖两个仓库的单一 worktree。framework 侧改动经 `TEMP-LOCAL-VERIFY` 的 `ProjectReference`（`LubanAgentCore.csproj`）即时反映到 agent 构建。

---

## 文件结构（改动清单）

**新建**
- `luban-framework/LuBan.Common/IO/MediaTypeUtil.cs` — 媒体类型/图片判定单一来源。
- `luban-framework/LuBan.XTestProject/MediaTypeUtilUnitTest.cs` — 其单测。
- `luban-framework/LuBan.XTestProject/FileUtilFormatFileSizeUnitTest.cs` — `FormatFileSize` 单测。
- `luban-framework/LuBan.XTestProject/ImageUtilPrimitivesUnitTest.cs` — `ImageUtil` 新原语单测。

**修改**
- `luban-framework/LuBan.Common/IO/FileUtil.cs` — 新增 `FormatFileSize(long)`。
- `luban-framework/LuBan.Common/ImageUtil.cs` — 新增 `ScaleToFit` / `Resize` / `Encode` / `SaveThumbnail`。
- `luban-framework/LuBan.AIAgent/Attachments/DefaultAttachmentProcessor.cs` — 改用 `MediaTypeUtil` + `ImageUtil`。
- `luban-framework/LuBan.Common/LuBan.Common.csproj`、`luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj` — 版本号。
- `luban-framework/LuBan.Common/README.md`、`README.en.md` — 文档。
- `luban-agent/LubanAgentCli/App/ViewModels/ConversationViewModel.cs` — 删除私有 MIME/图片判定。
- `luban-agent/LubanAgentCli/App/Models/Blocks/UserMessageBlock.cs` — 体积格式化委托。
- `luban-agent/LubanAgentCodex/Converters/FileSizeConverter.cs` — 体积格式化委托。

---

## Task 1: 新建 `MediaTypeUtil`

**Files:**
- Create: `luban-framework/LuBan.Common/IO/MediaTypeUtil.cs`
- Test: `luban-framework/LuBan.XTestProject/MediaTypeUtilUnitTest.cs`

- [ ] **Step 1: 写失败测试**

创建 `luban-framework/LuBan.XTestProject/MediaTypeUtilUnitTest.cs`：

```csharp
using LuBan.Common.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LuBan.XTestProject;

[TestClass]
public class MediaTypeUtilUnitTest
{
    [TestMethod]
    public void ExtensionToMediaType_DoesNotContainPdf()
    {
        Assert.IsFalse(MediaTypeUtil.ExtensionToMediaType.ContainsKey(".pdf"));
    }

    [TestMethod]
    public void ExtensionToMediaType_Has42Entries()
    {
        Assert.AreEqual(42, MediaTypeUtil.ExtensionToMediaType.Count);
    }

    [TestMethod]
    public void IsImage_RecognizesImageExtensions_CaseInsensitive()
    {
        Assert.IsTrue(MediaTypeUtil.IsImage("a.png"));
        Assert.IsTrue(MediaTypeUtil.IsImage("a.JPEG"));
        Assert.IsTrue(MediaTypeUtil.IsImage("a.tiff"));
        Assert.IsTrue(MediaTypeUtil.IsImage("a.heic"));
        Assert.IsTrue(MediaTypeUtil.IsImage("a.heif"));
    }

    [TestMethod]
    public void IsImage_ExcludesSvgPdfAndUnknown()
    {
        Assert.IsFalse(MediaTypeUtil.IsImage("a.svg"));
        Assert.IsFalse(MediaTypeUtil.IsImage("a.pdf"));
        Assert.IsFalse(MediaTypeUtil.IsImage("a.exe"));
    }

    [TestMethod]
    public void IsHeicLike_OnlyHeicAndHeif()
    {
        Assert.IsTrue(MediaTypeUtil.IsHeicLike("a.heic"));
        Assert.IsTrue(MediaTypeUtil.IsHeicLike("a.HEIF"));
        Assert.IsFalse(MediaTypeUtil.IsHeicLike("a.png"));
    }

    [TestMethod]
    public void ResolveMediaType_MapsKnownExtensions()
    {
        Assert.AreEqual("image/png", MediaTypeUtil.ResolveMediaType("x.png"));
        Assert.AreEqual("image/jpeg", MediaTypeUtil.ResolveMediaType("x.jpg"));
        Assert.AreEqual("image/svg+xml", MediaTypeUtil.ResolveMediaType("x.svg"));
        Assert.AreEqual("application/json", MediaTypeUtil.ResolveMediaType("x.json"));
    }

    [TestMethod]
    public void ResolveMediaType_DefaultsToTextPlain()
    {
        Assert.AreEqual("text/plain", MediaTypeUtil.ResolveMediaType("x.unknownext"));
    }

    [TestMethod]
    public void ResolveMediaType_HonorsDefaultParameter()
    {
        Assert.AreEqual("application/octet-stream", MediaTypeUtil.ResolveMediaType("x.unknownext", "application/octet-stream"));
        Assert.AreEqual("application/octet-stream", MediaTypeUtil.ResolveMediaType("x.pdf", "application/octet-stream"));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~MediaTypeUtilUnitTest"
```
Expected: 编译失败（`MediaTypeUtil` 未定义 / `CS0103`）。

- [ ] **Step 3: 实现 `MediaTypeUtil`**

创建 `luban-framework/LuBan.Common/IO/MediaTypeUtil.cs`（`using` 依赖 `LuBan.Common` 的全局 using，无需额外 using）：

```csharp
namespace LuBan.Common.IO;

/// <summary>
/// 附件/图片媒体类型单一来源：扩展名→MIME 映射、是否图片判定、HEIC 类判定。
/// </summary>
public static class MediaTypeUtil
{
    /// <summary>
    /// 扩展名→媒体类型（含点、小写、大小写不敏感）。
    /// 同时充当"受支持附件扩展名集合"，**不含 `.pdf`**（PDF 非受支持附件类型）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ExtensionToMediaType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif",
            [".bmp"] = "image/bmp",
            [".tiff"] = "image/tiff",
            [".tif"] = "image/tiff",
            [".heic"] = "image/heic",
            [".heif"] = "image/heif",
            [".svg"] = "image/svg+xml",
            [".txt"] = "text/plain",
            [".md"] = "text/markdown",
            [".json"] = "application/json",
            [".xml"] = "application/xml",
            [".yaml"] = "text/yaml",
            [".yml"] = "text/yaml",
            [".toml"] = "text/plain",
            [".ini"] = "text/plain",
            [".cfg"] = "text/plain",
            [".conf"] = "text/plain",
            [".log"] = "text/plain",
            [".sql"] = "text/plain",
            [".cs"] = "text/plain",
            [".py"] = "text/plain",
            [".js"] = "text/plain",
            [".ts"] = "text/plain",
            [".java"] = "text/plain",
            [".go"] = "text/plain",
            [".rs"] = "text/plain",
            [".rb"] = "text/plain",
            [".php"] = "text/plain",
            [".swift"] = "text/plain",
            [".kt"] = "text/plain",
            [".c"] = "text/plain",
            [".cpp"] = "text/plain",
            [".h"] = "text/plain",
            [".hpp"] = "text/plain",
            [".css"] = "text/plain",
            [".html"] = "text/plain",
            [".htm"] = "text/plain",
            [".sh"] = "text/plain"
        };

    /// <summary>图片类扩展名（`.svg` 不在此列，仍按文本处理）。</summary>
    public static readonly IReadOnlySet<string> ImageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tiff", ".tif", ".heic", ".heif"
        };

    /// <summary>无法解码、需提示转换的扩展名。</summary>
    public static readonly IReadOnlySet<string> HeicLikeExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".heic", ".heif" };

    /// <summary>按扩展名判断是否属于图片集。</summary>
    public static bool IsImage(string pathOrFileName)
        => ImageExtensions.Contains(Path.GetExtension(pathOrFileName));

    /// <summary>按扩展名判断是否属于 HEIC 类。</summary>
    public static bool IsHeicLike(string pathOrFileName)
        => HeicLikeExtensions.Contains(Path.GetExtension(pathOrFileName));

    /// <summary>解析扩展名对应的媒体类型；未命中返回 <paramref name="defaultMediaType"/>。</summary>
    public static string ResolveMediaType(string pathOrFileName, string defaultMediaType = "text/plain")
        => ExtensionToMediaType.TryGetValue(Path.GetExtension(pathOrFileName), out var mt) ? mt : defaultMediaType;
}
```

- [ ] **Step 4: 运行测试确认通过**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~MediaTypeUtilUnitTest"
```
Expected: PASS（8 个测试全绿）。

- [ ] **Step 5: 提交**

```bash
git -C luban-framework add LuBan.Common/IO/MediaTypeUtil.cs LuBan.XTestProject/MediaTypeUtilUnitTest.cs
git -C luban-framework commit -m "新增 LuBan.Common.IO.MediaTypeUtil 统一媒体类型来源"
```

---

## Task 2: `FileUtil.FormatFileSize(long)`

**Files:**
- Modify: `luban-framework/LuBan.Common/IO/FileUtil.cs`（在 `Exists` 方法后插入）
- Test: `luban-framework/LuBan.XTestProject/FileUtilFormatFileSizeUnitTest.cs`

- [ ] **Step 1: 写失败测试**

创建 `luban-framework/LuBan.XTestProject/FileUtilFormatFileSizeUnitTest.cs`：

```csharp
using LuBan.Common.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LuBan.XTestProject;

[TestClass]
public class FileUtilFormatFileSizeUnitTest
{
    [TestMethod]
    public void FormatFileSize_Negative_ReturnsZeroB()
    {
        Assert.AreEqual("0B", FileUtil.FormatFileSize(-1));
    }

    [TestMethod]
    public void FormatFileSize_Bytes()
    {
        Assert.AreEqual("0B", FileUtil.FormatFileSize(0));
        Assert.AreEqual("1023B", FileUtil.FormatFileSize(1023));
    }

    [TestMethod]
    public void FormatFileSize_Kilobytes()
    {
        Assert.AreEqual("1.0KB", FileUtil.FormatFileSize(1024));
        Assert.AreEqual("1.5KB", FileUtil.FormatFileSize(1536));
    }

    [TestMethod]
    public void FormatFileSize_Megabytes()
    {
        Assert.AreEqual("1.0MB", FileUtil.FormatFileSize(1024 * 1024));
        Assert.AreEqual("2.0MB", FileUtil.FormatFileSize(2 * 1024 * 1024));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~FileUtilFormatFileSizeUnitTest"
```
Expected: 编译失败（`FileUtil.FormatFileSize` 不存在 / `CS0117`）。

- [ ] **Step 3: 实现**

在 `luban-framework/LuBan.Common/IO/FileUtil.cs` 的 `Exists` 方法（原 26-34 行）之后插入：

```csharp
    /// <summary>
    /// 把字节数格式化为人类可读文本（不带空格）：B / KB / MB。
    /// 负值归一为 <c>"0B"</c>；各档保留一位小数（B 档为整数）。
    /// </summary>
    /// <param name="bytes">字节数。</param>
    public static string FormatFileSize(long bytes)
    {
        if (bytes < 0)
            return "0B";
        if (bytes < 1024)
            return $"{bytes}B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1}KB";
        return $"{bytes / 1024.0 / 1024.0:F1}MB";
    }
```

- [ ] **Step 4: 运行测试确认通过**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~FileUtilFormatFileSizeUnitTest"
```
Expected: PASS（4 个测试全绿）。

- [ ] **Step 5: 提交**

```bash
git -C luban-framework add LuBan.Common/IO/FileUtil.cs LuBan.XTestProject/FileUtilFormatFileSizeUnitTest.cs
git -C luban-framework commit -m "FileUtil 新增 FormatFileSize 体积格式化"
```

---

## Task 3: `ImageUtil` 增补 SkiaSharp 原语

**Files:**
- Modify: `luban-framework/LuBan.Common/ImageUtil.cs`（在 `DrawImage` 之后、`#region Private Methods` 之前插入）
- Test: `luban-framework/LuBan.XTestProject/ImageUtilPrimitivesUnitTest.cs`

- [ ] **Step 1: 写失败测试**

创建 `luban-framework/LuBan.XTestProject/ImageUtilPrimitivesUnitTest.cs`：

```csharp
using LuBan.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace LuBan.XTestProject;

[TestClass]
public class ImageUtilPrimitivesUnitTest
{
    [TestMethod]
    public void ScaleToFit_ShrinksWithinBox_Truncating()
    {
        Assert.AreEqual((100, 50), ImageUtil.ScaleToFit(200, 100, 100, 100));
    }

    [TestMethod]
    public void ScaleToFit_NoUpscale_WhenSmallerThanBox()
    {
        Assert.AreEqual((10, 20), ImageUtil.ScaleToFit(10, 20, 100, 100));
    }

    [TestMethod]
    public void Resize_ProducesExactDimensions()
    {
        using var src = new SKBitmap(200, 100);
        using var resized = ImageUtil.Resize(src, 50, 25);
        Assert.AreEqual(50, resized.Width);
        Assert.AreEqual(25, resized.Height);
    }

    [TestMethod]
    public void Encode_ReturnsNonEmptyBytes()
    {
        using var bmp = new SKBitmap(8, 8);
        var bytes = ImageUtil.Encode(bmp, SKEncodedImageFormat.Png, 100);
        Assert.IsTrue(bytes.Length > 0);
    }

    [TestMethod]
    public void SaveThumbnail_WritesJpegFile()
    {
        using var bmp = new SKBitmap(200, 100);
        var path = ImageUtil.SaveThumbnail(bmp);
        try
        {
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ImageUtilPrimitivesUnitTest"
```
Expected: 编译失败（`ScaleToFit` / `Resize` / `Encode` / `SaveThumbnail` 未定义 / `CS0117`）。

- [ ] **Step 3: 实现**

在 `luban-framework/LuBan.Common/ImageUtil.cs` 的 `DrawImage` 方法（原 160-165 行）之后插入：

```csharp
    /// <summary>
    /// 计算等比内缩到指定框内的尺寸；不放大（框大于原图时返回原尺寸）。取整采用截断。
    /// </summary>
    /// <param name="width">源宽。</param>
    /// <param name="height">源高。</param>
    /// <param name="maxWidth">最大宽。</param>
    /// <param name="maxHeight">最大高。</param>
    public static (int Width, int Height) ScaleToFit(int width, int height, int maxWidth, int maxHeight)
    {
        var ratio = Math.Min((double)maxWidth / width, (double)maxHeight / height);
        if (ratio > 1.0) ratio = 1.0;
        return (Math.Max(1, (int)(width * ratio)), Math.Max(1, (int)(height * ratio)));
    }

    /// <summary>
    /// 缩放位图到指定尺寸（Linear 采样）。
    /// </summary>
    /// <param name="image">源位图。</param>
    /// <param name="width">目标宽。</param>
    /// <param name="height">目标高。</param>
    public static SKBitmap Resize(SKBitmap image, int width, int height)
        => image.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

    /// <summary>
    /// 把位图编码为字节数组。
    /// </summary>
    /// <param name="image">源位图。</param>
    /// <param name="format">编码格式。</param>
    /// <param name="quality">质量（0-100）。</param>
    public static byte[] Encode(SKBitmap image, SKEncodedImageFormat format, int quality)
    {
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    /// <summary>
    /// 生成缩略图并落盘到临时目录，返回文件路径。等比缩放到 <paramref name="maxSize"/> 内（不放大），JPEG 质量 80。
    /// 取整采用四舍五入（与附件处理器缩略图路径一致）。
    /// </summary>
    /// <param name="image">源位图。</param>
    /// <param name="maxSize">最大边长，默认 64。</param>
    public static string SaveThumbnail(SKBitmap image, int maxSize = 64)
    {
        var dir = LuBan.Common.IO.TempDirectory.GetTempDir();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".jpg");

        var ratio = Math.Min((double)maxSize / image.Width, (double)maxSize / image.Height);
        if (ratio > 1.0) ratio = 1.0;
        var thumbW = Math.Max(1, (int)Math.Round(image.Width * ratio));
        var thumbH = Math.Max(1, (int)Math.Round(image.Height * ratio));
        using var thumb = Resize(image, thumbW, thumbH);
        using var fs = File.Create(path);
        thumb.Encode(fs, SKEncodedImageFormat.Jpeg, 80);
        return path;
    }
```

- [ ] **Step 4: 运行测试确认通过**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ImageUtilPrimitivesUnitTest"
```
Expected: PASS（5 个测试全绿）。

- [ ] **Step 5: 提交**

```bash
git -C luban-framework add LuBan.Common/ImageUtil.cs LuBan.XTestProject/ImageUtilPrimitivesUnitTest.cs
git -C luban-framework commit -m "ImageUtil 增补 ScaleToFit/Resize/Encode/SaveThumbnail 原语"
```

---

## Task 4: `DefaultAttachmentProcessor` 复用公共设施

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/Attachments/DefaultAttachmentProcessor.cs`

**说明：** 本任务是纯重构，回归由既有 `AttachmentProcessorUnitTest` 兜底。策略逻辑（`MaxWidth/MaxHeight=2000`、`MaxImageBytes=5MB`、`EncodeUnderLimit`、`IsPng`、HEIC 守卫、`generateThumbnail` 开关、默认 `text/plain`）全部保留，仅替换工具来源。

- [ ] **Step 1: 运行既有测试确认基线全绿**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AttachmentProcessorUnitTest"
```
Expected: PASS（全部用例绿，作为重构基线）。

- [ ] **Step 2: 替换 using 段**

把文件顶部（原 1-3 行）：
```csharp
using Microsoft.Extensions.AI;
using SkiaSharp;
using System.Text;
```
改为：
```csharp
using LuBan.Common;
using LuBan.Common.IO;
using Microsoft.Extensions.AI;
using SkiaSharp;
using System.Text;
```

- [ ] **Step 3: 删除私有媒体类型成员**

删除 `DefaultAttachmentProcessor.cs` 原 12-68 行的私有 `ExtensionToMediaType` 字典、`ImageExtensions` 集合、`HeicLikeExtensions` 集合（含其 XML 注释）。

- [ ] **Step 4: 改写 `SupportedExtensions` / `IsSupported` / `ResolveMediaType`**

把原 70-83 行：
```csharp
    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions { get; } = ExtensionToMediaType.Keys.ToList();

    /// <inheritdoc />
    public bool IsSupported(string filePath)
        => !string.IsNullOrWhiteSpace(filePath)
           && ExtensionToMediaType.ContainsKey(Path.GetExtension(filePath));

    /// <inheritdoc />
    public string ResolveMediaType(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ExtensionToMediaType.TryGetValue(ext, out var mt) ? mt : "text/plain";
    }
```
改为：
```csharp
    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions { get; } = MediaTypeUtil.ExtensionToMediaType.Keys.ToList();

    /// <inheritdoc />
    public bool IsSupported(string filePath)
        => !string.IsNullOrWhiteSpace(filePath)
           && MediaTypeUtil.ExtensionToMediaType.ContainsKey(Path.GetExtension(filePath));

    /// <inheritdoc />
    public string ResolveMediaType(string filePath) => MediaTypeUtil.ResolveMediaType(filePath);
```

- [ ] **Step 5: 改写 `ProcessAsync` 中的图片/HEIC 判定**

把原 127 行：
```csharp
        var isText = !ImageExtensions.Contains(ext) || ext.Equals(".svg", StringComparison.OrdinalIgnoreCase);
```
改为：
```csharp
        var isText = !MediaTypeUtil.IsImage(ext) || ext.Equals(".svg", StringComparison.OrdinalIgnoreCase);
```

把原 131 行：
```csharp
        if (HeicLikeExtensions.Contains(ext))
```
改为：
```csharp
        if (MediaTypeUtil.IsHeicLike(ext))
```

- [ ] **Step 6: 用 `ImageUtil.ScaleToFit` 替换手写缩放尺寸计算**

把 `ProcessImageAsync` 原 151-159 行：
```csharp
        if (width > MaxWidth || height > MaxHeight)
        {
            var ratio = Math.Min((double)MaxWidth / width, (double)MaxHeight / height);
            var targetW = Math.Max(1, (int)(width * ratio));
            var targetH = Math.Max(1, (int)(height * ratio));
            using var resized = Resize(source, targetW, targetH);
            bytes = EncodeUnderLimit(resized);
            outMedia = IsPng(bytes) ? "image/png" : "image/jpeg";
        }
```
改为：
```csharp
        if (width > MaxWidth || height > MaxHeight)
        {
            var (targetW, targetH) = ImageUtil.ScaleToFit(width, height, MaxWidth, MaxHeight);
            using var resized = ImageUtil.Resize(source, targetW, targetH);
            bytes = EncodeUnderLimit(resized);
            outMedia = IsPng(bytes) ? "image/png" : "image/jpeg";
        }
```

- [ ] **Step 7: 删除私有 SkiaSharp 原语并改调 `ImageUtil`**

删除 `ResizeSampling` 字段与私有 `Resize`、`Encode`、`SaveThumbnail` 方法（原 184-187、210-214、219-234 行）。

把 `ProcessImageAsync` 原 173 行：
```csharp
        string? thumb = generateThumbnail ? SaveThumbnail(source) : null;
```
改为：
```csharp
        string? thumb = generateThumbnail ? ImageUtil.SaveThumbnail(source) : null;
```

把 `EncodeUnderLimit`（原 189-208 行）中的 `Encode(...)` 与 `Resize(...)` 调用改为 `ImageUtil.Encode(...)` 与 `ImageUtil.Resize(...)`，整体变为：
```csharp
    private static byte[] EncodeUnderLimit(SKBitmap image)
    {
        // 先试 PNG，再按质量档降级 JPEG
        var png = ImageUtil.Encode(image, SKEncodedImageFormat.Png, 100);
        if (png.LongLength <= MaxImageBytes)
            return png;

        foreach (var quality in new[] { 85, 70, 55, 40 })
        {
            var jpeg = ImageUtil.Encode(image, SKEncodedImageFormat.Jpeg, quality);
            if (jpeg.LongLength <= MaxImageBytes)
                return jpeg;
        }

        // 仍超限：等比缩小再试
        var workW = Math.Max(1, (int)(image.Width * 0.75));
        var workH = Math.Max(1, (int)(image.Height * 0.75));
        using var work = ImageUtil.Resize(image, workW, workH);
        return EncodeUnderLimit(work);
    }
```

- [ ] **Step 8: 构建并运行回归测试**

Run:
```powershell
dotnet build luban-framework/LuBan.FrameWork.sln
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AttachmentProcessorUnitTest"
```
Expected: 构建成功；测试全绿（含 `IsSupported_RecognizesImageAndTextExtensions` 的 `IsFalse(IsSupported("a.pdf"))`、`ResolveMediaType_MapsKnownExtensions`、`ProcessAsync_SmallNonPngImage_KeepsOriginalMediaType`、`ProcessAsync_Heic_PromptsConversion`）。

- [ ] **Step 9: 提交**

```bash
git -C luban-framework add LuBan.AIAgent/Attachments/DefaultAttachmentProcessor.cs
git -C luban-framework commit -m "DefaultAttachmentProcessor 复用 MediaTypeUtil 与 ImageUtil"
```

---

## Task 5: CLI `ConversationViewModel` 迁移

**Files:**
- Modify: `luban-agent/LubanAgentCli/App/ViewModels/ConversationViewModel.cs`

- [ ] **Step 1: 添加 using**

在文件顶部（`using LuBan.AIAgent.Orchestration;` 之后，原 19 行后）加入：
```csharp
using LuBan.Common.IO;
```

- [ ] **Step 2: 删除私有图片集合与判定，改调 `MediaTypeUtil`**

删除原 478-482 行：
```csharp
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    private static bool IsImagePath(string path)
        => ImageExtensions.Contains(Path.GetExtension(path));
```

把 `HasUnsupportedImage`（原 484-485 行）：
```csharp
    private bool HasUnsupportedImage(string modelId, IReadOnlyList<AttachmentDraft> attachments)
        => attachments.Any(a => IsImagePath(a.Path)) && !_services.GetModelCapabilityService().SupportsVision(modelId);
```
改为：
```csharp
    private bool HasUnsupportedImage(string modelId, IReadOnlyList<AttachmentDraft> attachments)
        => attachments.Any(a => MediaTypeUtil.IsImage(a.Path)) && !_services.GetModelCapabilityService().SupportsVision(modelId);
```

把 `BuildDisplayInfos` 内原 505-507 行：
```csharp
            var isImage = IsImagePath(a.Path);
            list.Add(new AttachmentDisplayInfo(
                a.FileName, GuessMediaType(a.Path), size, null, isImage, null, a.Path));
```
改为：
```csharp
            var isImage = MediaTypeUtil.IsImage(a.Path);
            list.Add(new AttachmentDisplayInfo(
                a.FileName, MediaTypeUtil.ResolveMediaType(a.Path, "application/octet-stream"), size, null, isImage, null, a.Path));
```

- [ ] **Step 3: 删除私有 `GuessMediaType`**

删除原 512-524 行整段 `GuessMediaType` switch 方法（含其前置空行）。

- [ ] **Step 4: 构建确认无残留引用**

Run:
```powershell
dotnet build luban-agent/luban-agent.slnx
```
Expected: 构建成功（无 `IsImagePath` / `GuessMediaType` / `ImageExtensions` 未定义错误）。

- [ ] **Step 5: 提交**

```bash
git -C luban-agent add LubanAgentCli/App/ViewModels/ConversationViewModel.cs
git -C luban-agent commit -m "refactor: CLI 附件 MIME/图片判定改用 MediaTypeUtil"
```

---

## Task 6: CLI `UserMessageBlock` 体积格式化委托

**Files:**
- Modify: `luban-agent/LubanAgentCli/App/Models/Blocks/UserMessageBlock.cs`

- [ ] **Step 1: 添加 using**

在文件顶部 `using LubanAgentCore.Models;` 之前（原 19 行前）加入：
```csharp
using LuBan.Common.IO;
```

- [ ] **Step 2: 替换内联体积格式化**

把 `Render` 中（原 83-85 行）：
```csharp
            var size = a.FileSize < 1024 ? $"{a.FileSize}B"
                : a.FileSize < 1024 * 1024 ? $"{a.FileSize / 1024.0:F1}KB"
                : $"{a.FileSize / 1024.0 / 1024.0:F1}MB";
```
改为：
```csharp
            var size = FileUtil.FormatFileSize(a.FileSize);
```

- [ ] **Step 3: 构建确认**

Run:
```powershell
dotnet build luban-agent/luban-agent.slnx
```
Expected: 构建成功。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentCli/App/Models/Blocks/UserMessageBlock.cs
git -C luban-agent commit -m "refactor: CLI 附件体积格式化改用 FileUtil.FormatFileSize"
```

---

## Task 7: Codex `FileSizeConverter` 体积格式化委托

**Files:**
- Modify: `luban-agent/LubanAgentCodex/Converters/FileSizeConverter.cs`

- [ ] **Step 1: 添加 using**

在文件顶部 `using Avalonia.Data.Converters;` 之前（原 21 行前）加入：
```csharp
using LuBan.Common.IO;
```

- [ ] **Step 2: 改写 `Convert`**

把原 31-39 行：
```csharp
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long size || size < 0)
            return string.Empty;

        if (size < 1024)
            return $"{size} B";
        if (size < 1024 * 1024)
            return $"{size / 1024.0:F1} KB";
        return $"{size / 1024.0 / 1024.0:F1} MB";
    }
```
改为：
```csharp
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 非 long 返回空串（保留原契约）；负值由 FormatFileSize 归一为 "0B"。
        if (value is not long size)
            return string.Empty;

        return FileUtil.FormatFileSize(size);
    }
```

- [ ] **Step 3: 构建确认**

Run:
```powershell
dotnet build luban-agent/luban-agent.slnx
```
Expected: 构建成功。

- [ ] **Step 4: 提交**

```bash
git -C luban-agent add LubanAgentCodex/Converters/FileSizeConverter.cs
git -C luban-agent commit -m "refactor: Codex 附件体积格式化改用 FileUtil.FormatFileSize"
```

---

## Task 8: 文档与版本号

**Files:**
- Modify: `luban-framework/LuBan.Common/README.md`
- Modify: `luban-framework/LuBan.Common/README.en.md`
- Modify: `luban-framework/LuBan.Common/LuBan.Common.csproj`
- Modify: `luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj`

> `LuBan.AIAgent` 公开 API 未变（`IAttachmentProcessor` 契约不变），故其 README 不改；仅递增版本号。

- [ ] **Step 1: 更新 `LuBan.Common/README.md` 文件 IO 段**

把第 128 行：
```markdown
`IO.FileUtil`、`IO.PathUtil`、`IO.FileTypeUtil`、`IO.TempFile`、`IO.PersistenceFile`
```
改为：
```markdown
`IO.FileUtil`、`IO.PathUtil`、`IO.FileTypeUtil`、`IO.MediaTypeUtil`、`IO.TempFile`、`IO.PersistenceFile`
```

并在第 130 行说明后追加一行：
```markdown
> `FileUtil.FormatFileSize` 把字节数格式化为 `123B` / `1.5KB` / `2.0MB`；`MediaTypeUtil` 提供扩展名→MIME、是否图片的统一判定。
```

- [ ] **Step 2: 同步 `LuBan.Common/README.en.md`**

把第 128 行：
```markdown
`IO.FileUtil`, `IO.PathUtil`, `IO.FileTypeUtil`, `IO.TempFile`, `IO.PersistenceFile`
```
改为：
```markdown
`IO.FileUtil`, `IO.PathUtil`, `IO.FileTypeUtil`, `IO.MediaTypeUtil`, `IO.TempFile`, `IO.PersistenceFile`
```

并在第 130 行说明后追加一行：
```markdown
> `FileUtil.FormatFileSize` formats byte counts as `123B` / `1.5KB` / `2.0MB`; `MediaTypeUtil` provides unified extension-to-MIME and image detection.
```

- [ ] **Step 3: 递增版本号**

把 `luban-framework/LuBan.Common/LuBan.Common.csproj` 第 11 行：
```xml
    <Version>2026.10.10.1</Version>
```
改为：
```xml
    <Version>2026.10.13.1</Version>
```

把 `luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj` 第 12 行 `<Version>` 改为：
```xml
    <Version>2026.10.13.1</Version>
```

> ⚠️ 该文件工作树中存在**既有未提交改动**（`<Version>` 由 `2026.10.9.2` 改为 `2026.10.9.1`，非本次产生）。提交前先与用户确认处理方式（保留本任务设定为 `2026.10.13.1`，或先还原再改）。

- [ ] **Step 4: 构建并打包**

Run:
```powershell
dotnet build luban-framework/LuBan.FrameWork.sln
```
Expected: 构建成功，`LuBan.Common` 与 `LuBan.AIAgent` 生成 `2026.10.13.1` 的 nupkg。

- [ ] **Step 5: 提交**

```bash
git -C luban-framework add LuBan.Common/README.md LuBan.Common/README.en.md LuBan.Common/LuBan.Common.csproj LuBan.AIAgent/LuBan.AIAgent.csproj
git -C luban-framework commit -m "文档与版本：MediaTypeUtil/FormatFileSize 说明，递增 Common/AIAgent 版本"
```

---

## 最终验证

- [ ] **回归**

Run:
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AttachmentProcessorUnitTest"
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~MediaTypeUtilUnitTest"
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~FileUtilFormatFileSizeUnitTest"
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~ImageUtilPrimitivesUnitTest"
dotnet build luban-framework/LuBan.FrameWork.sln
dotnet build luban-agent/luban-agent.slnx
```
Expected: 全部 PASS / 构建成功。

- [ ] **手动冒烟（luban-agent 无自动化测试）**
  - Codex：图片附件发送、缩略图生成、点击大图查看、附件体积显示（去空格后正确，如 `123B`）。
  - CLI：图片附件发送、vision 不支持时的 `HasUnsupportedImage` 提示、附件体积显示。

---

## Self-Review（对照 spec）

- **Spec §3.1 `MediaTypeUtil`** → Task 1（含 42 项、不含 `.pdf`、`IsImage`/`IsHeicLike`/`ResolveMediaType` 默认值参数）。✅
- **Spec §3.2 `FormatFileSize`** → Task 2（含负值 `0B`、不带空格、各档）。✅
- **Spec §3.3 `ImageUtil` 原语** → Task 3（`ScaleToFit` 截断、`SaveThumbnail` 四舍五入、`Resize`/`Encode`）。✅
- **Spec §3.4 消费者改动表** → Task 4（处理器）、Task 5（CLI VM）、Task 6（CLI block）、Task 7（Codex）。✅
- **Spec §3.5 行为变更** → Task 5（CLI 图片集扩为 10 项）、Task 7（Codex 去空格 + 非 `long` 守卫）。✅
- **Spec §5 测试** → Task 1-3 新增单测 + Task 4 回归 + 最终验证。✅
- **Spec §6 DI 结论（不开工）** → 无对应任务（正确，spec 明确不产生 DI 改动）。✅
- **Spec §7 发版与文档** → Task 8（版本递增 + 中英文 README 同步）。✅
- **占位符扫描**：无 TODO/TBD/"类似 Task N"；所有代码步骤含完整代码与命令。✅
- **类型/命名一致性**：`MediaTypeUtil.ExtensionToMediaType` / `ImageExtensions` / `HeicLikeExtensions` / `IsImage` / `IsHeicLike` / `ResolveMediaType`、`FileUtil.FormatFileSize`、`ImageUtil.ScaleToFit` / `Resize` / `Encode` / `SaveThumbnail` 在定义处与各消费任务中拼写一致。✅
