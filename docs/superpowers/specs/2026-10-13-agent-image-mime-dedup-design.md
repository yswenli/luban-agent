# 图片/媒体类型与 DI 去重设计（Agent CLI/Codex → LuBan.Common）

- 日期：2026-10-13
- 状态：待实现
- 范围：方案 C（图片工具去重 A + SkiaSharp 能力统一 B + DI 审视）
- 涉及仓库：`luban-framework`（LuBan.Common、LuBan.AIAgent）、`luban-agent`（LubanAgentCli、LubanAgentCodex）

## 1. 背景与动机

`LubanAgentCli` 与 `LubanAgentCodex` 各自维护了一份图片分类 / MIME 映射 / 文件体积格式化逻辑，与框架侧实现重复且语义不一致：

- 扩展名→MIME / 是否图片：
  - CLI `ConversationViewModel` 私有 `ImageExtensions` / `IsImagePath` / `GuessMediaType`（默认 `application/octet-stream`）。
  - 框架 `LuBan.AIAgent.Attachments.DefaultAttachmentProcessor` 私有 `ExtensionToMediaType` / `ImageExtensions` / `HeicLikeExtensions`（`ResolveMediaType` 默认 `text/plain`）。
  - `LuBan.Common.IO.FileTypeUtil._contentTypeDic` 是另一套庞大的 legacy HTTP 表（600+ 项，默认 `application/octet-stream`，且缺 `.webp/.heic/.heif`）。
- SkiaSharp 图片处理：
  - 框架提交 `4d7355e` 已把附件图片处理从 `SixLabors.ImageSharp` 迁移到 `SkiaSharp`，`DefaultAttachmentProcessor` 自此自带一套私有 SkiaSharp 原语（`Resize` / `EncodeUnderLimit` / `Encode` / `SaveThumbnail`）。
  - `LuBan.Common.ImageUtil` 同样基于 SkiaSharp，但两者未复用。
- 体积格式化：Codex `FileSizeConverter`（`123 B`，带空格）与 CLI `UserMessageBlock`（`123B`，不带空格）各写一份。

本次将上述重复逻辑下沉 / 复用到 `LuBan.Common`，并顺带审视 DI（是否引入 `LuBan.DI`）。

## 2. 目标与非目标

### 目标
1. 统一"扩展名→MIME 映射 + 是否图片判定"到单一来源。
2. 统一 SkiaSharp 图片原语，`DefaultAttachmentProcessor` 复用 `LuBan.Common.ImageUtil`。
3. 统一文件体积格式化到 `LuBan.Common`。
4. 对"是否把 agent 的 DI 复用/迁移到 `LuBan.DI`"给出明确结论。

### 非目标（本次明确不做）
- 不改动 UI 专属渲染代码：`LubanAgentCodex` 的 `ImageViewerWindow`、`Converters/ThumbnailPathConverter`、`Views/Controls/InputBox.axaml.cs` 剪贴板逻辑、CLI `UserMessageBlock.Render` 等。`LuBan.Common` / `LuBan.DI` 不引用 Avalonia / Terminal.Gui，这些代码不可下沉。
- 不改动附件编排与上传策略：`LuBanAgentCore.Services.AttachmentService`、`FileRefPattern`、`MaxAttachmentsPerMessage`、vision 过滤等行为保持不变。
- 不引入 `LuBan.DI`（见第 6 节结论）。
- 不改变 `AttachmentService` / `IAttachmentProcessor` 的公开契约。

## 3. 组件设计

### 3.1 `LuBan.Common.IO.MediaTypeUtil`（新建，统一媒体类型来源）

新建静态类，承载"附件/图片媒体类型"的单一真实来源，命名空间 `LuBan.Common.IO`。

- `IReadOnlyDictionary<string,string> ExtensionToMediaType`：迁移自 `DefaultAttachmentProcessor.ExtensionToMediaType`（图片 + 文本 + 常见代码扩展名，约 45 项），并补 `.pdf → application/pdf`（CLI 展示需要）。大小写不敏感。
- `IReadOnlySet<string> ImageExtensions`：迁移自 `DefaultAttachmentProcessor.ImageExtensions`（`.png .jpg .jpeg .webp .gif .bmp .tiff .tif .heic .heif`）。**`.svg` 仍按文本处理，不入图片集**，与现有语义一致。
- `IReadOnlySet<string> HeicLikeExtensions`：`.heic .heif`。
- `bool IsImage(string pathOrFileName)`：按扩展名判断是否属于 `ImageExtensions`。
- `bool IsHeicLike(string pathOrFileName)`：按扩展名判断是否属于 `HeicLikeExtensions`。
- `string ResolveMediaType(string pathOrFileName, string defaultMediaType = "text/plain")`：查 `ExtensionToMediaType`，未命中返回 `defaultMediaType`。

**默认值语义**：不同消费者各自的默认返回通过参数传入，避免行为漂移：
- `DefaultAttachmentProcessor` 调 `ResolveMediaType(path)`（默认 `text/plain`）。
- CLI 调 `ResolveMediaType(path, "application/octet-stream")`。

**为何新建类而非复用 `FileTypeUtil`**：`FileTypeUtil._contentTypeDic` 是 legacy 通用 HTTP 内容类型表（600+ 项，默认 `application/octet-stream`），与"附件支持的扩展名→媒体类型"这一策略投影不是一回事；将附件策略混入其中会污染 legacy 表并制造双重默认值。`FileTypeUtil` 本次不改动。

### 3.2 `LuBan.Common.IO.FileUtil.FormatFileSize(long)`（新增）

- 规范输出（不带空格）：
  - `bytes < 0` → `"0B"`
  - `bytes < 1024` → `"{bytes}B"`
  - `bytes < 1024*1024` → `"{bytes/1024.0:F1}KB"`
  - 否则 → `"{bytes/1024.0/1024.0:F1}MB"`
- 消费者：
  - Codex `Converters/FileSizeConverter.Convert` 改为委托 `FileUtil.FormatFileSize`。**Codex 输出由 `123 B` 变为 `123B`（去除空格），属既定决策。**
  - CLI `App/Models/Blocks/UserMessageBlock.Render` 改为委托 `FileUtil.FormatFileSize`（输出与现状一致）。

### 3.3 `LuBan.Common.ImageUtil` 增补 SkiaSharp 原语

在现有 `ImageUtil` 中新增可复用的 SkiaSharp 原语（保留现有 `MakeThumbnail` / `MakeWaterMark` / `MakeGray` / `DrawImage` 不变）：

- `(int Width, int Height) ScaleToFit(int width, int height, int maxWidth, int maxHeight)`：等比内缩到框内且不放大小图；纯计算。
- `SKBitmap Resize(SKBitmap image, int width, int height)`：使用 `SKFilterMode.Linear + SKMipmapMode.Linear`（与处理器现状一致）。
- `byte[] Encode(SKBitmap image, SKEncodedImageFormat format, int quality)`：编码为字节数组。
- `string SaveThumbnail(SKBitmap image, int maxSize = 64)`：等比缩放到 `maxSize` 内（不放大小图），JPEG 质量 80 落盘到 `LuBan.Common.IO.TempDirectory.GetTempDir()`，返回文件路径。

`DefaultAttachmentProcessor` 随后：
- 删除私有 `Resize` / `Encode` / `SaveThumbnail`，改调 `ImageUtil` 对应方法。
- 保留策略逻辑于处理器内：`MaxWidth/MaxHeight=2000`、`MaxImageBytes=5MB`、`EncodeUnderLimit`（PNG→逐档 JPEG 降级→0.75 缩图递归）、`IsPng` 探测、HEIC 守卫、`generateThumbnail` 开关、默认 `text/plain`。
- 保留的行为契约（由单测兜底）：见第 5 节。

### 3.4 消费者改动汇总

| 消费者 | 现状 | 改为 |
|---|---|---|
| CLI `ConversationViewModel.ImageExtensions` / `IsImagePath` | 私有集合与判定 | 删除，用 `MediaTypeUtil.IsImage` |
| CLI `ConversationViewModel.GuessMediaType` | 私有 switch | 删除，用 `MediaTypeUtil.ResolveMediaType(path, "application/octet-stream")` |
| CLI `UserMessageBlock` 体积格式化 | 内联 | `FileUtil.FormatFileSize` |
| Codex `FileSizeConverter` | 内联 | `FileUtil.FormatFileSize`（输出去空格） |
| 框架 `DefaultAttachmentProcessor` MIME/图片判定 | 私有字典/集合 | `MediaTypeUtil` |
| 框架 `DefaultAttachmentProcessor` SkiaSharp 原语 | 私有方法 | `ImageUtil` 原语 |

## 4. 数据流（不变）

附件链路保持：`AttachmentService.ParseFileReferences` → `IAttachmentProcessor.ProcessAsync` → `ProcessedAttachment` → `AttachmentDisplayInfo`。本次仅替换实现内部工具来源，不改数据流、时序或对外接口。

## 5. 测试与验证

### 自动化
- framework 既有 `LuBan.XTestProject/AttachmentProcessorUnitTest.cs` 全绿作为回归兜底，重点用例：
  - `ResolveMediaType_MapsKnownExtensions`（含 `.svg→image/svg+xml`、未知→`text/plain`）。
  - `ProcessAsync_Image_ProducesDataContent_WithImageMediaType`。
  - `ProcessAsync_SmallNonPngImage_KeepsOriginalMediaType`（`.webp` 原格式保留）。
  - `ProcessAsync_Heic_PromptsConversion`。
- 新增单测（MSTest，`LuBan.XTestProject/LuBan.UnitTestProject.csproj`）：
  - `MediaTypeUtil`：`IsImage`（命中/未命中/大小写/`.svg` 不算图片）、`ResolveMediaType`（命中、默认值参数、CLI 语义的 octet-stream 默认）。
  - `FileUtil.FormatFileSize`：负值→`0B`、`1023B`、`1024B`→`1.0KB`、MB 档位。

### 命令
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AttachmentProcessor"
dotnet build luban-framework/LuBan.FrameWork.sln
dotnet build luban-agent/luban-agent.slnx
```

### 手动冒烟（luban-agent 无自动化测试）
- Codex：图片附件发送、缩略图生成、点击大图查看、附件体积显示（去空格后显示正确）。
- CLI：图片附件发送、`HasUnsupportedImage`（vision 不支持时提示）、附件体积显示。

## 6. DI 审视结论（不开工）

**结论：不在 agent 引入 `LuBan.DI`。**

理由：
1. agent 采用标准 `Microsoft.Extensions.DependencyInjection`，重度依赖 `scoped` + `keyed` + `ValidateScopes/ValidateOnBuild`（见 `AgentHostBuilder.BuildServiceProvider`），用以支撑近期的多会话/多工作区隔离。
2. `LuBan.DI.ServiceProviderUtil` 是**全局可变静态容器**（`static IServiceCollection _services` / `static IServiceProvider? _serviceProvider`），无法承载 scoped/keyed 语义，与 agent 的隔离模型直接冲突。
3. agent 无 `InjectionAttribute` / `IScoped` / AOP 代理（`AspectDispatchProxy`）使用场景，引入无收益。
4. 唯一重复点 `CommandBase.BuildServiceProvider()`（headless 命令用的极简容器：仅 `IChatClient` + `AddLuBanAgent`）与全量 `AgentHostBuilder` 是有意分层（极简 vs 全量宿主），不合并。

交付物：仅本设计记录结论，**不产生 DI 代码改动**。

## 7. 发版与文档

- framework 公开 API 变化：递增 `LuBan.Common` 与 `LuBan.AIAgent` 的 `<Version>`（日期制）。
- 有公开 API 变化的组件需同步中英文 README（`README.md` / `README.en.md`）。
- 当前 `LuBan.AIAgent.csproj` 工作树存在**未提交改动**（`<Version>` 由 `2026.10.9.2` 改为 `2026.10.9.1`），非本次产生，需用户确认处理方式后再纳入发版。
- agent 侧当前通过 `TEMP-LOCAL-VERIFY` 的 `ProjectReference` 引用 framework，改动可即时生效；最终是否还原为 `PackageReference` 属独立计划范畴，本设计不强制。

## 8. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 统一 MIME 后行为漂移（默认值） | 通过 `ResolveMediaType` 的默认值参数保留各消费者语义 |
| Codex 体积显示文案变化（去空格） | 已确认属既定决策；冒烟覆盖 |
| 重构 `DefaultAttachmentProcessor` 图片核心引入回归 | 既有 `AttachmentProcessorUnitTest` 全绿作为兜底；保留策略逻辑、仅替换原语 |
| 遗漏 `.pdf` 等 CLI 专有映射 | 统一映射表显式补 `.pdf`，并以 `MediaTypeUtil` 单测覆盖 |