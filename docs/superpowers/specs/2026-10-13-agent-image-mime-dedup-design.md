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
  - `LuBan.Common.IO.FileTypeUtil._contentTypeDic` 是另一套庞大的 legacy HTTP 表（≈500 项，默认 `application/octet-stream`，且缺 `.webp/.heic/.heif`）。
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
- **不改变"受支持附件类型"集合**：`.pdf` 仍不受支持（见 §3.1）。CLI 唯一可达的附件入口 `InputBarView.AddAttachment`（`InputBarView.cs:370`，仅由已过滤的 `ParseFileReferences` 与剪贴板调用）本就无法附加 `.pdf`，故删除 CLI 的 `.pdf` 映射不改变可达行为。
- 不引入 `LuBan.DI`（见第 6 节结论）。
- 不改变 `AttachmentService` / `IAttachmentProcessor` 的公开契约。

## 3. 组件设计

### 3.1 `LuBan.Common.IO.MediaTypeUtil`（新建，统一媒体类型来源）

新建静态类，承载"附件/图片媒体类型"的单一真实来源，命名空间 `LuBan.Common.IO`。

- `IReadOnlyDictionary<string,string> ExtensionToMediaType`：**逐项迁移**自 `DefaultAttachmentProcessor.ExtensionToMediaType`（`DefaultAttachmentProcessor.cs:12-56`，图片 + 文本 + 常见代码扩展名，**共 42 项**），大小写不敏感。**不新增 `.pdf`**：此表同时充当"受支持扩展名集合"（下条），加入 `.pdf` 会把 PDF 纳入附件受理范围、破坏既有契约 `IsSupported("a.pdf")==false`（`AttachmentProcessorUnitTest.cs:30`），并让 PDF 被 `ProcessTextAsync` 当 UTF-8 文本读入（`DefaultAttachmentProcessor.cs:128-129,236+`）。
- **受支持集 == 此表**：`DefaultAttachmentProcessor.SupportedExtensions` 与 `IsSupported` 继续由 `MediaTypeUtil.ExtensionToMediaType` 的键集合派生（`DefaultAttachmentProcessor.cs:71,74-76`），语义与现状完全一致；Codex 文件选择器过滤器（`MainWindow.axaml.cs:461`）随之保持不含 `.pdf`。
- `IReadOnlySet<string> ImageExtensions`：迁移自 `DefaultAttachmentProcessor.ImageExtensions`（`.png .jpg .jpeg .webp .gif .bmp .tiff .tif .heic .heif`，共 10 项）。**`.svg` 仍按文本处理，不入图片集**，与现有语义一致。
- `IReadOnlySet<string> HeicLikeExtensions`：`.heic .heif`。
- `bool IsImage(string pathOrFileName)`：按扩展名判断是否属于 `ImageExtensions`。
- `bool IsHeicLike(string pathOrFileName)`：按扩展名判断是否属于 `HeicLikeExtensions`。
- `string ResolveMediaType(string pathOrFileName, string defaultMediaType = "text/plain")`：查 `ExtensionToMediaType`，未命中返回 `defaultMediaType`。

**默认值语义**：不同消费者各自的默认返回通过参数传入，避免行为漂移：
- `DefaultAttachmentProcessor` 调 `ResolveMediaType(path)`（默认 `text/plain`）。
- CLI 调 `ResolveMediaType(path, "application/octet-stream")`。

**为何新建类而非复用 `FileTypeUtil`**：`FileTypeUtil._contentTypeDic` 是 legacy 通用 HTTP 内容类型表（≈500 项，默认 `application/octet-stream`），与"附件支持的扩展名→媒体类型"这一策略投影不是一回事；将附件策略混入其中会污染 legacy 表并制造双重默认值。`FileTypeUtil` 本次不改动。

### 3.2 `LuBan.Common.IO.FileUtil.FormatFileSize(long)`（新增）

- 规范输出（不带空格）：
  - `bytes < 0` → `"0B"`
  - `bytes < 1024` → `"{bytes}B"`
  - `bytes < 1024*1024` → `"{bytes/1024.0:F1}KB"`
  - 否则 → `"{bytes/1024.0/1024.0:F1}MB"`
- 消费者：
  - Codex `Converters/FileSizeConverter.Convert` 改为委托 `FileUtil.FormatFileSize`。**Codex 输出由 `123 B` 变为 `123B`（去除空格），属既定决策。**
    - **负值/非 `long` 语义变更**：现实现 `value is not long || size < 0` 时返回**空串**（`FileSizeConverter.cs:32-33`），委托后负值返回 `"0B"`。需保留 `value is not long` 守卫（非 `long` 仍返回空串），负值改为 `"0B"` 属可接受变更（实际不会出现负体积）。
  - CLI `App/Models/Blocks/UserMessageBlock.Render` 改为委托 `FileUtil.FormatFileSize`（输出与现状一致：`<1KB`→`{n}B`、`<1MB`→`{n/1024:F1}KB`、否则 `{n/1024/1024:F1}MB`，见 `UserMessageBlock.cs:83-85`）。

### 3.3 `LuBan.Common.ImageUtil` 增补 SkiaSharp 原语

在现有 `ImageUtil` 中新增可复用的 SkiaSharp 原语（保留现有 `MakeThumbnail` / `MakeWaterMark` / `MakeGray` / `DrawImage` 不变）：

- `(int Width, int Height) ScaleToFit(int width, int height, int maxWidth, int maxHeight)`：等比内缩到框内且不放大小图（`ratio>1` 时钳为 1.0）；取整**采用截断** `Math.Max(1, (int)(size * ratio))`，与处理器主缩放路径一致（`DefaultAttachmentProcessor.cs:151-155`）。
- `SKBitmap Resize(SKBitmap image, int width, int height)`：使用 `SKFilterMode.Linear + SKMipmapMode.Linear`（与处理器现状一致，`DefaultAttachmentProcessor.cs:184-187`）。
- `byte[] Encode(SKBitmap image, SKEncodedImageFormat format, int quality)`：编码为字节数组。
- `string SaveThumbnail(SKBitmap image, int maxSize = 64)`：等比缩放到 `maxSize` 内（不放大小图，取整**保留 `Math.Round`** 语义，与现状一致，`DefaultAttachmentProcessor.cs:226-229`），JPEG 质量 80，落盘到 `LuBan.Common.IO.TempDirectory.GetTempDir()`，返回文件路径。
  - 注：`ScaleToFit`（截断）与 `SaveThumbnail`（四舍五入）取整方式现状不同，须分别保留以做到零像素级回归。

`DefaultAttachmentProcessor` 随后：
- 删除私有 `Resize` / `Encode` / `SaveThumbnail`，改调 `ImageUtil` 对应方法。
- 保留策略逻辑于处理器内：`MaxWidth/MaxHeight=2000`、`MaxImageBytes=5MB`、`EncodeUnderLimit`（PNG→逐档 JPEG 降级→0.75 缩图递归）、`IsPng` 探测、HEIC 守卫、`generateThumbnail` 开关、默认 `text/plain`。
- 保留的行为契约（由单测兜底）：见第 5 节。

### 3.4 消费者改动汇总

| 消费者 | 现状 | 改为 |
|---|---|---|
| CLI `ConversationViewModel.ImageExtensions` / `IsImagePath` | 私有 6 项集合与判定（`ConversationViewModel.cs:478-482`） | 删除，用 `MediaTypeUtil.IsImage`（**扩为 10 项**，见 §3.5） |
| CLI `ConversationViewModel.GuessMediaType` | 私有 switch（含 `.pdf`，`ConversationViewModel.cs:512-524`） | 删除，用 `MediaTypeUtil.ResolveMediaType(path, "application/octet-stream")`（**移除 `.pdf` 分支**，不可达，见 §2/§3.5） |
| CLI `UserMessageBlock` 体积格式化 | 内联 | `FileUtil.FormatFileSize` |
| Codex `FileSizeConverter` | 内联 | `FileUtil.FormatFileSize`（输出去空格） |
| 框架 `DefaultAttachmentProcessor` 受支持判定/MIME/图片判定 | 私有字典/集合 | `MediaTypeUtil`（**受支持集 = 该表键集，不含 `.pdf`，语义不变**） |
| 框架 `DefaultAttachmentProcessor` SkiaSharp 原语 | 私有方法 | `ImageUtil` 原语（保留 `EncodeUnderLimit`/`IsPng`/HEIC 守卫等策略） |

> 注：`LuBan.AIAgent.Sessions.SessionChatHistoryProvider` 亦通过 `DefaultAttachmentProcessor.ResolveMediaType` 解析历史图片 MIME（`SessionChatHistoryProvider.cs:41,343`），随处理器实现统一切换到 `MediaTypeUtil`，无需单独改动。

### 3.5 已确认的行为变更（相对现状）

1. **Codex 附件体积文案**：`123 B` → `123B`（去空格）。
2. **CLI 图片判定集合扩大**：由 `{png,jpg,jpeg,gif,bmp,webp}`（6 项，`ConversationViewModel.cs:478-479`）扩为 `MediaTypeUtil.ImageExtensions`（10 项，含 `tiff/tif/heic/heif`）；影响 `ConversationViewModel.HasUnsupportedImage`（`ConversationViewModel.cs:484-485`）的 vision 门禁与 `BuildDisplayInfos` 的图片图标/像素尺寸展示。方向为语义统一，属已确认决策。
3. **CLI `GuessMediaType` 的 `.pdf` 映射移除**：`.pdf` 非受支持附件类型，且附件唯一入口 `InputBarView.AddAttachment` 只接收已过 `IsSupported` 的路径，故该分支不可达；如未来需要，另立 CLI 常量，**不得**并入共享表（见 §3.1）。
4. 其余保持与现状一致：各消费者默认 MIME 默认值、`FormatFileSize` 各档输出、处理器受支持集与 SkiaSharp 策略。

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
  - `MediaTypeUtil`：`IsImage`（命中/未命中/大小写；`tiff/tif/heic/heif` 命中；`.svg`、`.pdf` 未命中）、`ResolveMediaType`（命中、默认值参数、CLI 语义的 octet-stream 默认）、`ExtensionToMediaType` **不含 `.pdf`**。
  - `FileUtil.FormatFileSize`：负值→`0B`、`1023B`、`1024B`→`1.0KB`、MB 档位。

### 命令
```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AttachmentProcessor"
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~MediaTypeUtil|FullyQualifiedName~FormatFileSize"
dotnet build luban-framework/LuBan.FrameWork.sln
dotnet build luban-agent/luban-agent.slnx
```
> 回归红线：`AttachmentProcessorUnitTest.IsSupported_RecognizesImageAndTextExtensions` 必须保持全绿（含 `IsFalse(IsSupported("a.pdf"))`）。

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
| 共享表误纳 `.pdf`，击穿 `IsSupported`/文件选择器/文本读取 | 表严格迁移原 42 项、不含 `.pdf`；`AttachmentProcessorUnitTest.IsSupported_RecognizesImageAndTextExtensions` 作为红线 |
| CLI 图片判定由 6 项扩到 10 项（含 tiff/tif/heic/heif） | 已确认决策；`HasUnsupportedImage` 的 vision 门禁随之覆盖新扩展名（见 §3.5），冒烟确认提示符合预期 |
| Codex 体积显示文案变化（去空格） | 已确认属既定决策；冒烟覆盖 |
| 重构 `DefaultAttachmentProcessor` 图片核心引入回归 | 既有 `AttachmentProcessorUnitTest` 全绿作为兜底；保留策略逻辑、仅替换原语；`ScaleToFit` 截断 / `SaveThumbnail` 四舍五入的取整差异须分别保留 |