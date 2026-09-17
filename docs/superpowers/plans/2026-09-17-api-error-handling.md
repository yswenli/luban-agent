# LLM/API 错误分类与自动重试 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `LuBan.AIAgent` 内建立统一的 LLM/API 错误分类层（结构化优先 + 文案兜底）、瞬时错误自动退避重试、以及面向宿主的 `AgentApiException` 友好文案，CLI 与 Codex 按类别终止本轮并提示。

**Architecture:** 新增 `Errors/` 目录承载分类模型、分类器、容错重试中间件；`ResilientChatClient` 以幂等包装方式接入 `LuBanAgentFactory`（主对话/子代理）、`LlmTaskPlanner`（规划）与历史压缩路径；最终失败统一抛 `AgentApiException`，宿主只渲染 `FriendlyMessage`；重试进度通过 `LuBanAgentOptions.OnApiRetry` 回调投递给宿主。

**Tech Stack:** .NET 10 / `Microsoft.Extensions.AI` 10.10.0 / `System.ClientModel` 1.14.0（传递引用）/ MSTest 4.4.0 / Terminal.Gui CLI / Avalonia Codex

---

## 背景

普通对话的 API 错误目前在 CLI/Codex 都只显示 `ex.Message` 原文（`ConversationViewModel.cs:561-566`、`MainWindowViewModel.cs:153-160`）；唯一的友好映射 `CommandBase.GetFriendlyApiErrorMessage` 只在 `/init` 生效；无自动重试；`AutoOrchestrationMiddleware.TryPlanAsync` 静默吞掉规划期 API 错误；`SessionChatHistoryProvider` 的摘要压缩失败会直接打断主对话。

**开发期生效方式**：`LubanAgentCore.csproj:11` 目前是 `ProjectReference` → `LuBan.AIAgent`，framework 改动立即生效；正式发布仍需版本递增 + `dotnet pack` + 回退 `PackageReference`（见 Task 8，需用户许可）。

## 已定稿决策（访谈结论，勿改）

| 决策点 | 结论 |
|---|---|
| 实现层次 | framework `LuBan.AIAgent` |
| 识别机制 | 结构化优先（`ClientResultException.Status` + error.code）→ 文案关键字兜底 → 未识别 |
| 瞬时错误重试 | 要，ChatClient 中间件内重试；3 次尝试（含首次），1s/2s + 抖动，遵循 `Retry-After`（统一上限 30s）；每次 API 调用独立重试，不设轮次累计上限 |
| SDK 内置重试 | **禁用**（Core 两处 `OpenAIClientOptions.RetryPolicy = new ClientRetryPolicy(0)`），由中间件统一重试，避免 3×4 叠加与不可见重试 |
| 流式重试边界 | 仅**首个 token 之前**重试；已吐字后中断不重试 |
| 终止语义 | 只终止本轮，保留可输入；不熔断、不自动切模型、不弹模态 |
| 401/402/403 | 分别文案 + 终止，不重试 |
| 404 | 终止 + 排查清单（模型名/端点/下线），不重试 |
| 400 / 能力不支持 | 终止（合并进各自类别文案） |
| 上下文超长 | 终止 + 针对性提示（/clear、新会话、精简输入）；不自动压缩 |
| 429 限流 vs 配额 | 限流→重试 3 次后终止；配额耗尽→终止不重试，文案内解析并带上服务端重置时间 |
| 5xx / 网络 / 超时 | 重试 3 次后终止；用户 Esc 取消不重试（与超时区分） |
| 内容审核 | 终止 + 说明是风控拦截、建议改措辞 |
| 未识别错误 | 统一兜底文案「调用失败（未识别错误），详情见日志」，原文只进日志 |
| 展示 | 只显示友好文案（原始异常进日志）；重试进度进聊天区 |
| 传递方式 | 自定义异常 `AgentApiException`（流式与非流式一致）；重试进度走 `LuBanAgentOptions` 回调 |
| 配置 | `LuBanAgentOptions.ApiRetry` + appsettings 绑定 |
| 适用范围 | 全链路：主对话、`/init`、编排 planner/节点、历史压缩 |
| 旧映射 | 删除 `CommandBase.GetFriendlyApiErrorMessage`，统一走分类器 |
| 失败轮落库 | 保持现状（不落库） |
| 验证交付 | framework 单测 + TUI 冒烟用例 + README 中英文对同步 |

## 非目标（本期不做）

1. 不做熔断器、不自动切换备用模型/Provider、不弹模态确认框。
2. 不改变失败轮不落库的现状。
3. `ErrorEvent`（来自流内 `ErrorContent`，`AgentHostService.cs:215-219`）保持原文展示——该通道没有结构化状态码，无法可靠分类。
4. 编排节点的失败仍以 `NodeResult.Error` 文本合成兜底回复（`DagScheduler`），不改为异常。
5. 不内置厂商错误码映射表（结构化 + 关键字兜底足够）。
6. 失败轮不提供重发按钮/命令（自动重试已覆盖瞬时错误）。

## 宿主渲染约定（关键）

- 宿主**只在 `ex is AgentApiException` 时**使用 `apiEx.Error.FriendlyMessage`；
  其他异常（配置类 `InvalidOperationException` 等）保持 `ex.Message` 原文。
- 原因：分类器的「未识别 → 统一兜底文案」只应由 framework 在包装 API 调用异常时使用，
  不能让宿主对配置错误调用 `Classify()`，否则「未选择模型」这类可操作提示会被吞成「未识别错误」。

## 文件结构

**framework（`D:\WorkBench\Walle\luban\luban-framework\LuBan.AIAgent`）**

| 动作 | 文件 | 职责 |
|---|---|---|
| Create | `Errors/AgentApiErrorCategory.cs` | 错误类别枚举 |
| Create | `Errors/AgentApiErrorInfo.cs` | 分类结果 record |
| Create | `Errors/AgentRetryNotice.cs` | 重试通知 record |
| Create | `Errors/AgentApiException.cs` | 携带分类结果的异常 |
| Create | `Errors/ApiErrorClassifier.cs` | 分类矩阵、友好文案、退避计算、Retry-After 提取 |
| Create | `Errors/ResilientChatClient.cs` | 重试中间件（流式/非流式） |
| Create | `Errors/ChatClientResilience.cs` | 幂等包装入口 |
| Modify | `GlobalUsings.cs` | `global using LuBan.AIAgent.Errors;` |
| Modify | `Configuration/LuBanAgentOptions.cs` | `ApiRetry` + `OnApiRetry` + `ApiRetryOptions` |
| Modify | `LuBanAgentFactory.cs` | ctor 包装默认客户端；`BuildFunctionClient` 包装路由客户端 |
| Modify | `Orchestration/Planner/LlmTaskPlanner.cs` | ctor 包装规划客户端 |
| Modify | `Orchestration/AutoOrchestrationMiddleware.cs` | 规划期 API 错误不再静默降级 |
| Modify | `Sessions/SessionChatHistoryProvider.cs` | 压缩失败降级为跳过压缩 |
| Modify | `README.md` / `README.en.md` | 错误处理与重试章节 |
| Create | `LuBan.XTestProject/AgentApiErrorUnitTest.cs` | 分类矩阵 + 重试行为单测 |

**agent（`D:\WorkBench\Walle\luban\luban-agent`）**

| 动作 | 文件 | 职责 |
|---|---|---|
| Modify | `LubanAgentCli/GlobalUsings.cs` | 增加两个 global using |
| Modify | `LubanAgentCli/App/ViewModels/ConversationViewModel.cs` | 主对话渲染 `AgentApiException` |
| Modify | `LubanAgentCli/App/Views/RootView.cs` | 设置 `OnApiRetry` 进度提示 |
| Modify | `LubanAgentCli/Commands/CommandBase.cs` | 删除旧映射方法 |
| Modify | `LubanAgentCli/Commands/InitCommand.cs` | `/init` 改用分类结果 |
| Modify | `LubanAgentCli/App/ViewModels/CommandViewModel.cs` | 命令异常友好化 |
| Modify | `LubanAgentCli/appsettings.json` | `ApiRetry` 示例 |
| Modify | `LuBanAgentCodex/ViewModels/MainWindowViewModel.cs` | 渲染 + `OnApiRetry` |
| Modify | `LubanAgentCore/Configuration/ConfigManager.cs` | 禁用 SDK 内置重试（默认模型客户端） |
| Modify | `LubanAgentCore/Hosting/AgentHostBuilder.cs` | 禁用 SDK 内置重试（多 Provider 路由客户端） |
| Modify | `LubanAgentCodex/appsettings.json` | `ApiRetry` 示例 |
| Modify | `LubanAgentCli/docs/TUI-SMOKE-TEST.md` | 错误处理冒烟用例 |

---

## Task 1: 错误模型与配置项

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Errors/AgentApiErrorCategory.cs`
- Create: `luban-framework/LuBan.AIAgent/Errors/AgentApiErrorInfo.cs`
- Create: `luban-framework/LuBan.AIAgent/Errors/AgentRetryNotice.cs`
- Create: `luban-framework/LuBan.AIAgent/Errors/AgentApiException.cs`
- Modify: `luban-framework/LuBan.AIAgent/Configuration/LuBanAgentOptions.cs`
- Modify: `luban-framework/LuBan.AIAgent/GlobalUsings.cs`

- [ ] **Step 1: 新建类别枚举**

`Errors/AgentApiErrorCategory.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.AIAgent.Errors
*文件名： AgentApiErrorCategory
*唯一标识：LLM/API 错误类别
*创建时间：2026/9/17
*描述：LLM/API 错误类别
*
*****************************************************************************/
namespace LuBan.AIAgent.Errors;

/// <summary>
/// LLM/API 错误类别。
/// </summary>
public enum AgentApiErrorCategory
{
    /// <summary>未识别错误。</summary>
    Unknown = 0,
    /// <summary>宿主主动取消（Esc）。</summary>
    Canceled,
    /// <summary>认证失败（401，Key 无效/过期）。</summary>
    Authentication,
    /// <summary>余额不足（402）。</summary>
    PaymentRequired,
    /// <summary>权限不足（403）。</summary>
    PermissionDenied,
    /// <summary>模型不存在或已下线（404/410）。</summary>
    ModelNotFound,
    /// <summary>请求参数或格式错误（400 等）。</summary>
    BadRequest,
    /// <summary>上下文超出模型长度上限。</summary>
    ContextLengthExceeded,
    /// <summary>模型不支持请求的能力（工具调用/多模态等）。</summary>
    UnsupportedCapability,
    /// <summary>触发速率限制（429 rate-limit）。</summary>
    RateLimited,
    /// <summary>配额用尽（429 insufficient_quota 等）。</summary>
    QuotaExhausted,
    /// <summary>服务端错误（5xx）。</summary>
    ServerError,
    /// <summary>网络错误（DNS/连接/TLS）。</summary>
    NetworkError,
    /// <summary>请求超时（非用户取消）。</summary>
    Timeout,
    /// <summary>流式响应中途中断（已输出部分内容）。</summary>
    StreamInterrupted,
    /// <summary>内容被风控拦截。</summary>
    ContentFiltered
}
```

- [ ] **Step 2: 新建分类结果与重试通知**

`Errors/AgentApiErrorInfo.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.AIAgent.Errors
*文件名： AgentApiErrorInfo
*唯一标识：LLM/API 错误分类结果
*创建时间：2026/9/17
*描述：LLM/API 错误分类结果
*
*****************************************************************************/
namespace LuBan.AIAgent.Errors;

/// <summary>
/// LLM/API 错误的分类结果。
/// </summary>
/// <param name="Category">错误类别。</param>
/// <param name="StatusCode">HTTP 状态码（无结构化信息时为 null）。</param>
/// <param name="ErrorCode">服务端错误码（如 insufficient_quota；无则为 null）。</param>
/// <param name="FriendlyMessage">面向用户的友好文案（唯一展示内容，原始异常只进日志）。</param>
/// <param name="IsRetryable">是否属于可自动重试的瞬时错误。</param>
public sealed record AgentApiErrorInfo(
    AgentApiErrorCategory Category,
    int? StatusCode,
    string? ErrorCode,
    string FriendlyMessage,
    bool IsRetryable);
```

`Errors/AgentRetryNotice.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.AIAgent.Errors
*文件名： AgentRetryNotice
*唯一标识：自动重试通知
*创建时间：2026/9/17
*描述：自动重试通知（由 LuBanAgentOptions.OnApiRetry 投递给宿主）
*
*****************************************************************************/
namespace LuBan.AIAgent.Errors;

/// <summary>
/// 自动重试通知。回调在调用线程执行，宿主需自行编组到 UI 线程。
/// </summary>
/// <param name="Category">触发重试的错误类别。</param>
/// <param name="Attempt">即将进行的重试序号（1 表示第一次重试）。</param>
/// <param name="MaxAttempts">最大尝试次数（含首次调用）。</param>
/// <param name="Delay">本次重试前的等待时长。</param>
public sealed record AgentRetryNotice(
    AgentApiErrorCategory Category,
    int Attempt,
    int MaxAttempts,
    TimeSpan Delay);
```

- [ ] **Step 3: 新建异常类型**

`Errors/AgentApiException.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.AIAgent.Errors
*文件名： AgentApiException
*唯一标识：LLM/API 调用失败异常
*创建时间：2026/9/17
*描述：携带分类结果与友好文案的 LLM/API 调用失败异常
*
*****************************************************************************/
namespace LuBan.AIAgent.Errors;

/// <summary>
///  LLM/API 调用失败异常。宿主直接渲染 <see cref="AgentApiErrorInfo.FriendlyMessage"/> 即可，
/// 原始异常保存在 <see cref="Exception.InnerException"/> 供日志排查。
/// </summary>
public class AgentApiException : Exception
{
    /// <summary>
    /// 分类结果。
    /// </summary>
    public AgentApiErrorInfo Error { get; }

    /// <summary>
    /// 创建异常。
    /// </summary>
    /// <param name="error">分类结果。</param>
    /// <param name="innerException">原始异常（仅用于日志）。</param>
    public AgentApiException(AgentApiErrorInfo error, Exception? innerException = null)
        : base(error.FriendlyMessage, innerException)
    {
        Error = error;
    }
}
```

- [ ] **Step 4: 全局 using 注册命名空间**

修改 `GlobalUsings.cs`，在 `global using LuBan.AIAgent.Configuration;` 下一行插入：

```csharp
global using LuBan.AIAgent.Errors;
```

- [ ] **Step 5: 扩展 Options**

`Configuration/LuBanAgentOptions.cs`：在 `Confirmation` 属性之后（`:91` 后）插入两个属性：

```csharp
    /// <summary>
    /// API 重试配置（限流/5xx/网络错误/超时的自动退避重试）
    /// </summary>
    public ApiRetryOptions ApiRetry { get; set; } = new();

    /// <summary>
    /// 重试通知回调（不可配置绑定，由宿主在启动时设置；回调在调用线程执行，宿主需自行编组到 UI 线程）。
    /// </summary>
    public Action<AgentRetryNotice>? OnApiRetry { get; set; }
```

文件末尾追加配置类：

```csharp
/// <summary>
/// API 重试配置。
/// </summary>
public class ApiRetryOptions
{
    /// <summary>
    /// 是否启用自动重试（关闭时等效于最大尝试次数 1）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 最大尝试次数（含首次调用），默认 3
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// 首次重试的基础延迟（毫秒），默认 1000
    /// </summary>
    public int BaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// 指数退避倍数，默认 2（1s、2s…）
    /// </summary>
    public double BackoffFactor { get; set; } = 2;

    /// <summary>
    /// 单次重试等待上限（毫秒），默认 30000；对 Retry-After 与退避计算结果统一收口，
    /// 避免服务端返回超大 Retry-After 或畸形配置导致等待溢出/异常
    /// </summary>
    public int MaxDelayMs { get; set; } = 30000;
}
```

- [ ] **Step 6: 构建验证**

Run:

```powershell
dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj -c Debug
```

Expected: 0 警告 0 错误。（`System.ClientModel` 为传递引用，本步尚未使用；若后续 Task 2 报缺少 `ClientResultException`，再在 `LuBan.AIAgent.csproj` 显式添加 `<PackageReference Include="System.ClientModel" Version="1.14.0" />`。）

- [ ] **Step 7: 提交（需用户明确许可后执行）**

```powershell
cd luban-framework
git add LuBan.AIAgent/Errors LuBan.AIAgent/Configuration/LuBanAgentOptions.cs LuBan.AIAgent/GlobalUsings.cs
git commit -m "新增 LLM/API 错误分类模型与重试配置项"
```

---

## Task 2: 错误分类器（TDD）

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Errors/ApiErrorClassifier.cs`
- Create: `luban-framework/LuBan.XTestProject/AgentApiErrorUnitTest.cs`

- [ ] **Step 1: 写失败测试（分类矩阵 + 退避计算）**

`LuBan.XTestProject/AgentApiErrorUnitTest.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.XTestProject
*文件名： AgentApiErrorUnitTest
*唯一标识：LLM/API 错误分类与重试单测
*创建时间：2026/9/17
*描述：ApiErrorClassifier 分类矩阵与退避计算单元测试（纯单元，不访问网络）
*
*****************************************************************************/
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Errors;

using Microsoft.Extensions.AI;

using System.ClientModel;
using System.Net;

namespace LuBan.XTestProject;

[TestClass]
public class AgentApiErrorUnitTest
{
    [TestMethod]
    public void ClassifyHttpStatus_401_IsAuthentication_NotRetryable()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(401, "Incorrect API key provided: sk-xxx");

        Assert.AreEqual(AgentApiErrorCategory.Authentication, info.Category);
        Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void ClassifyHttpStatus_402_IsPaymentRequired_NotRetryable()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(402, "insufficient balance");

        Assert.AreEqual(AgentApiErrorCategory.PaymentRequired, info.Category);
        Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void ClassifyHttpStatus_403_IsPermissionDenied_NotRetryable()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(403, "you do not have access to model");

        Assert.AreEqual(AgentApiErrorCategory.PermissionDenied, info.Category);
        Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void ClassifyHttpStatus_404_IsModelNotFound_NotRetryable()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(404, "The model `gpt-x` does not exist");

        Assert.AreEqual(AgentApiErrorCategory.ModelNotFound, info.Category);
        Assert.IsFalse(info.IsRetryable);
        StringAssert.Contains(info.FriendlyMessage, "模型");
    }

    [TestMethod]
    public void ClassifyHttpStatus_400_ContextLength_IsContextLengthExceeded()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(400,
            "This model's maximum context length is 8192 tokens. error code: context_length_exceeded");

        Assert.AreEqual(AgentApiErrorCategory.ContextLengthExceeded, info.Category);
        Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void ClassifyHttpStatus_400_ContentFilter_IsContentFiltered()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(400,
            "The response was filtered due to content_filter");

        Assert.AreEqual(AgentApiErrorCategory.ContentFiltered, info.Category);
        Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void ClassifyHttpStatus_400_Unsupported_IsUnsupportedCapability()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(400,
            "tools is not supported by this model");

        Assert.AreEqual(AgentApiErrorCategory.UnsupportedCapability, info.Category);
        Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void ClassifyHttpStatus_429_RateLimit_IsRetryable()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(429, "rate_limit_exceeded: too many requests");

        Assert.AreEqual(AgentApiErrorCategory.RateLimited, info.Category);
        Assert.IsTrue(info.IsRetryable);
    }

    [TestMethod]
    public void ClassifyHttpStatus_429_InsufficientQuota_IsQuotaExhausted_WithResetTime()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(429,
            "HTTP 429 (insufficient_quota: insufficient_quota)\n\nYour token-plan 1-week quota has been exhausted. The quota will reset at 09-20 09:46:00 UTC.");

        Assert.AreEqual(AgentApiErrorCategory.QuotaExhausted, info.Category);
        Assert.IsFalse(info.IsRetryable);
        StringAssert.Contains(info.FriendlyMessage, "09-20 09:46:00 UTC");
    }

    [TestMethod]
    public void ClassifyHttpStatus_503_IsServerError_Retryable()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(503, "Service Unavailable");

        Assert.AreEqual(AgentApiErrorCategory.ServerError, info.Category);
        Assert.IsTrue(info.IsRetryable);
    }

    [TestMethod]
    public void Classify_HttpRequestException_IsNetworkError_Retryable()
    {
        var ex = new HttpRequestException("connection refused");

        var info = ApiErrorClassifier.Classify(ex);

        Assert.AreEqual(AgentApiErrorCategory.NetworkError, info.Category);
        Assert.IsTrue(info.IsRetryable);
    }

    [TestMethod]
    public void Classify_HttpRequestException_WithStatusCode_RoutesToStatusClassification()
    {
        var ex = new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized);

        var info = ApiErrorClassifier.Classify(ex);

        Assert.AreEqual(AgentApiErrorCategory.Authentication, info.Category);
        Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void Classify_ClientResultException_NoResponse_UnwrapsToNetworkError()
    {
        // SDK 传输层把 HttpRequestException 包成 ClientResultException（未收到响应时 Status == 0）
        var ex = new ClientResultException("connection refused", null!, new HttpRequestException("connection refused"));

        var info = ApiErrorClassifier.Classify(ex);

        Assert.AreEqual(AgentApiErrorCategory.NetworkError, info.Category);
        Assert.IsTrue(info.IsRetryable);
    }

    [TestMethod]
    public void Classify_ClientResultException_NoResponseNoInner_IsNetworkError()
    {
        var ex = new ClientResultException("no response", null!, null!);

        var info = ApiErrorClassifier.Classify(ex);

        Assert.AreEqual(AgentApiErrorCategory.NetworkError, info.Category);
        Assert.IsTrue(info.IsRetryable);
    }

    [TestMethod]
    public void Classify_AggregateException_UnwrapsInner()
    {
        // SDK 重试耗尽：多次尝试都抛异常时抛 AggregateException
        var ex = new AggregateException("Retry failed after 4 tries.",
            new HttpRequestException("connection refused"), new TaskCanceledException());

        var info = ApiErrorClassifier.Classify(ex);

        Assert.AreEqual(AgentApiErrorCategory.NetworkError, info.Category);
        Assert.IsTrue(info.IsRetryable);
    }

    [TestMethod]
    public void Classify_AgentApiException_PassesThrough()
    {
        var original = new AgentApiErrorInfo(AgentApiErrorCategory.QuotaExhausted, 429, "insufficient_quota", "配额已用尽", false);
        var ex = new AgentApiException(original, new InvalidOperationException("x"));

        var info = ApiErrorClassifier.Classify(ex);

        Assert.AreSame(original, info);
    }

    [TestMethod]
    public void ClassifyHttpStatus_429_BillingWithoutQuotaPhrase_IsRateLimited_Retryable()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(429,
            "Rate limit reached. Visit https://platform.openai.com/account/billing to check your limit and billing details.");

        Assert.AreEqual(AgentApiErrorCategory.RateLimited, info.Category);
        Assert.IsTrue(info.IsRetryable);
    }

    [TestMethod]
    public void ClassifyHttpStatus_403_ModelAccessDenied_IsPermissionDenied()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(403,
            "Your API key does not have permission to use this model.");

        Assert.AreEqual(AgentApiErrorCategory.PermissionDenied, info.Category);
        Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void ClassifyHttpStatus_422_ValidationError_IsBadRequest()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(422,
            "body.messages: field required");

        Assert.AreEqual(AgentApiErrorCategory.BadRequest, info.Category);
        Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void ClassifyHttpStatus_429_QuotaWithFourDigitYear_ParsesResetTime()
    {
        var info = ApiErrorClassifier.ClassifyHttpStatus(429,
            "quota has been exhausted. The quota will reset at 2026-09-20 09:46:00 UTC.");

        Assert.AreEqual(AgentApiErrorCategory.QuotaExhausted, info.Category);
        StringAssert.Contains(info.FriendlyMessage, "2026-09-20 09:46:00 UTC");
    }

    [TestMethod]
    public void ComputeRetryDelay_CapsRetryAfter()
    {
        var options = new ApiRetryOptions { MaxDelayMs = 30000 };

        // 服务端可能返回超大 Retry-After（如 30 天），必须被 MaxDelayMs 收口
        var delay = ApiErrorClassifier.ComputeRetryDelay(1, options, TimeSpan.FromDays(30));

        Assert.AreEqual(30d, delay.TotalSeconds, 0.001);
    }

    [TestMethod]
    public void ComputeRetryDelay_MalformedConfig_DoesNotThrow()
    {
        var options = new ApiRetryOptions { BaseDelayMs = int.MaxValue, BackoffFactor = 1000, MaxDelayMs = 30000 };

        var delay = ApiErrorClassifier.ComputeRetryDelay(5, options);

        Assert.IsTrue(delay > TimeSpan.Zero && delay <= TimeSpan.FromSeconds(30), $"delay={delay}");
    }

    [TestMethod]
    public void Classify_OperationCanceled_ByHost_IsCanceled_NotRetryable()
    {
        var info = ApiErrorClassifier.Classify(new OperationCanceledException(), canceledByHost: true);

        Assert.AreEqual(AgentApiErrorCategory.Canceled, info.Category);
        Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void Classify_OperationCanceled_NotByHost_IsTimeout_Retryable()
    {
        var info = ApiErrorClassifier.Classify(new OperationCanceledException(), canceledByHost: false);

        Assert.AreEqual(AgentApiErrorCategory.Timeout, info.Category);
        Assert.IsTrue(info.IsRetryable);
    }

    [TestMethod]
    public void Classify_Unknown_ReturnsUnifiedFallback()
    {
        var info = ApiErrorClassifier.Classify(new InvalidOperationException("something odd"));

        Assert.AreEqual(AgentApiErrorCategory.Unknown, info.Category);
        Assert.IsFalse(info.IsRetryable);
        StringAssert.Contains(info.FriendlyMessage, "未识别错误");
    }

    [TestMethod]
    public void ComputeRetryDelay_ExponentialWithJitter()
    {
        var options = new ApiRetryOptions { BaseDelayMs = 1000, BackoffFactor = 2 };

        var first = ApiErrorClassifier.ComputeRetryDelay(1, options);
        var second = ApiErrorClassifier.ComputeRetryDelay(2, options);

        Assert.IsTrue(first.TotalMilliseconds >= 800 && first.TotalMilliseconds <= 1200, $"first={first.TotalMilliseconds}");
        Assert.IsTrue(second.TotalMilliseconds >= 1600 && second.TotalMilliseconds <= 2400, $"second={second.TotalMilliseconds}");
    }

    [TestMethod]
    public void ComputeRetryDelay_RetryAfter_Wins()
    {
        var options = new ApiRetryOptions { BaseDelayMs = 1000, BackoffFactor = 2 };

        var delay = ApiErrorClassifier.ComputeRetryDelay(3, options, TimeSpan.FromSeconds(5));

        Assert.AreEqual(5d, delay.TotalSeconds, 0.001);
    }

    [TestMethod]
    public void GetCategoryLabel_ReturnsChineseLabel()
    {
        Assert.AreEqual("限流", ApiErrorClassifier.GetCategoryLabel(AgentApiErrorCategory.RateLimited));
        Assert.AreEqual("未识别错误", ApiErrorClassifier.GetCategoryLabel(AgentApiErrorCategory.Unknown));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentApiError"
```

Expected: 编译失败（`ApiErrorClassifier` 不存在）。

- [ ] **Step 3: 实现分类器**

`Errors/ApiErrorClassifier.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.AIAgent.Errors
*文件名： ApiErrorClassifier
*唯一标识：LLM/API 错误分类器
*创建时间：2026/9/17
*描述：结构化优先（HTTP 状态码 + error.code）+ 文案关键字兜底的错误分类器
*
*****************************************************************************/
using System.ClientModel;
using System.Globalization;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace LuBan.AIAgent.Errors;

/// <summary>
/// LLM/API 错误分类器。
/// 识别顺序：结构化信息（<see cref="ClientResultException.Status"/> + 响应文案中的 error.code）优先，
/// 关键字兜底；未识别的错误统一返回 <see cref="AgentApiErrorCategory.Unknown"/> 与统一兜底文案。
/// </summary>
public static class ApiErrorClassifier
{
    private const string UnknownMessage = "调用失败（未识别错误），详情见日志。";

    private const string AuthenticationMessage =
        "API 认证失败：API Key 无效或已过期。请检查该 Provider 的 Key 配置后重试。";

    private const string PaymentRequiredMessage =
        "账户余额不足：请充值后再试。";

    private const string PermissionDeniedMessage =
        "API 权限不足：当前 Key 无权访问该模型。请更换 Key 或切换模型。";

    private const string ModelNotFoundMessage =
        "模型不存在或已下线：请检查模型名与 Provider 端点配置（可用 /model 查看），或切换到其他模型。";

    private const string BadRequestMessage =
        "请求被模型拒绝（参数或格式错误）：请精简输入后重试，或切换到其他模型。";

    private const string ContextLengthExceededMessage =
        "上下文超出模型长度上限：请用 /clear 清空对话、新建会话，或精简当前输入后重试。";

    private const string UnsupportedCapabilityMessage =
        "当前模型不支持该能力（如工具调用/多模态）：请切换到支持该能力的模型。";

    private const string RateLimitedMessage =
        "请求过于频繁（触发速率限制）：请稍后再试。";

    private const string ServerErrorMessage =
        "模型服务端错误：服务暂时不可用，请稍后再试。";

    private const string NetworkErrorMessage =
        "网络连接失败：无法访问模型服务，请检查网络或 Provider 端点配置后重试。";

    private const string TimeoutMessage =
        "请求超时：模型未在超时时间内响应，请稍后再试。";

    private const string StreamInterruptedMessage =
        "响应中断：连接在输出过程中断开，以上内容可能不完整。请重新提问。";

    private const string ContentFilteredMessage =
        "内容被风控拦截（非系统故障）：请调整措辞后重新提问。";

    private const string CanceledMessage = "任务已取消。";

    private static readonly Regex QuotaResetRegex = new(
        @"reset at\s*(?<time>(?:[0-9]{4}-)?[0-9]{2}-[0-9]{2}\s+[0-9]{2}:[0-9]{2}:[0-9]{2}\s*UTC)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 分类异常。
    /// </summary>
    /// <param name="ex">原始异常。</param>
    /// <param name="canceledByHost">是否为宿主主动取消（Esc）；false 时 OperationCanceledException 视为请求超时。</param>
    /// <returns>分类结果。</returns>
    public static AgentApiErrorInfo Classify(Exception ex, bool canceledByHost = false)
        => Classify(ex, canceledByHost, depth: 0);

    private static AgentApiErrorInfo Classify(Exception ex, bool canceledByHost, int depth)
    {
        ArgumentNullException.ThrowIfNull(ex);

        // 已分类异常直通（幂等）：中间件包装后再分类（编排/压缩路径）不丢类别
        if (ex is AgentApiException agentApiException)
            return agentApiException.Error;

        // SDK 重试耗尽时会抛 AggregateException("Retry failed after N tries.")：取内层首个异常继续识别
        if (depth < 3 && ex is AggregateException aggregateException && aggregateException.InnerExceptions.Count > 0)
            return Classify(aggregateException.InnerExceptions[0], canceledByHost, depth + 1);

        if (ex is OperationCanceledException)
        {
            return canceledByHost
                ? new(AgentApiErrorCategory.Canceled, null, null, CanceledMessage, false)
                : new(AgentApiErrorCategory.Timeout, null, null, TimeoutMessage, true);
        }

        if (ex is TimeoutException)
            return new(AgentApiErrorCategory.Timeout, null, null, TimeoutMessage, true);

        if (ex is ClientResultException clientException)
        {
            // Status <= 0：未收到响应（传输层失败，如 DNS/连接/TLS 被 SDK 包装成 ClientResultException），
            // 先解内层异常；无内层时按网络错误处理（可重试）
            if (clientException.Status <= 0)
            {
                var inner = clientException.InnerException;
                if (depth < 3 && inner is not null && !ReferenceEquals(inner, clientException))
                    return Classify(inner, canceledByHost, depth + 1);
                return new(AgentApiErrorCategory.NetworkError, null, null, NetworkErrorMessage, true);
            }

            return ClassifyHttpStatus(clientException.Status, clientException.Message);
        }

        if (ex is HttpRequestException httpRequestException)
        {
            // 带 4xx/5xx 状态码的 HttpRequestException 按状态码分类，避免把 401 当网络错误重试
            if (httpRequestException.StatusCode is { } statusCode && (int)statusCode >= 400)
                return ClassifyHttpStatus((int)statusCode, httpRequestException.Message);
            return new(AgentApiErrorCategory.NetworkError, null, null, NetworkErrorMessage, true);
        }

        // IOException 覆盖 socket/流被重置等传输层 IO 故障；本地文件 IO 异常也会落此处（有意取舍：宁可多试一次）
        if (ex is SocketException or IOException)
            return new(AgentApiErrorCategory.NetworkError, null, null, NetworkErrorMessage, true);

        return new(AgentApiErrorCategory.Unknown, null, null, UnknownMessage, false);
    }

    /// <summary>
    /// 按 HTTP 状态码与错误文案分类（结构化入口，供单测直接覆盖矩阵）。
    /// </summary>
    /// <param name="status">HTTP 状态码。</param>
    /// <param name="message">服务端错误文案（含错误码字样时优先按码识别）。</param>
    /// <returns>分类结果。</returns>
    public static AgentApiErrorInfo ClassifyHttpStatus(int status, string? message)
    {
        var text = message ?? string.Empty;

        switch (status)
        {
            case 400:
                if (ContainsAny(text, "context_length_exceeded", "maximum context length", "too many tokens", "context length"))
                    return ContextLengthExceeded(status);
                if (ContainsAny(text, "content_filter", "content_policy_violation", "content policy", "flagged"))
                    return ContentFiltered(status);
                if (ContainsAny(text, "not support", "unsupported", "does not support"))
                    return UnsupportedCapability(status);
                return new(AgentApiErrorCategory.BadRequest, status, null, BadRequestMessage, false);

            case 401:
                return new(AgentApiErrorCategory.Authentication, status, "invalid_api_key", AuthenticationMessage, false);

            case 402:
                return new(AgentApiErrorCategory.PaymentRequired, status, null, PaymentRequiredMessage, false);

            case 403:
                if (ContainsAny(text, "insufficient_quota", "quota has been exhausted", "quota exhausted",
                        "exceeded your current quota", "billing"))
                    return QuotaExhausted(status, text);
                if (ContainsAny(text, "invalid_api_key", "incorrect api key"))
                    return new(AgentApiErrorCategory.Authentication, status, "invalid_api_key", AuthenticationMessage, false);
                return new(AgentApiErrorCategory.PermissionDenied, status, null, PermissionDeniedMessage, false);

            case 404:
            case 410:
                return new(AgentApiErrorCategory.ModelNotFound, status, "model_not_found", ModelNotFoundMessage, false);

            case 408:
                return new(AgentApiErrorCategory.Timeout, status, null, TimeoutMessage, true);

            case 413:
                return ContextLengthExceeded(status);

            case 422:
                // 422 既用于「能力不支持」，也被网关/FastAPI 代理用于参数校验失败：先看关键字，默认按参数错误
                return ContainsAny(text, "not support", "unsupported", "does not support")
                    ? UnsupportedCapability(status)
                    : new(AgentApiErrorCategory.BadRequest, status, null, BadRequestMessage, false);

            case 429:
                // 注意：不要用 "billing" 判定配额——OpenAI 限流文案常带 billing 链接，会误判为配额耗尽（不可重试）
                return ContainsAny(text, "insufficient_quota", "quota has been exhausted", "quota exhausted",
                        "exceeded your current quota")
                    ? QuotaExhausted(status, text)
                    : new(AgentApiErrorCategory.RateLimited, status, "rate_limit_exceeded", RateLimitedMessage, true);

            case 500:
            case 502:
            case 503:
            case 504:
                return new(AgentApiErrorCategory.ServerError, status, null, ServerErrorMessage, true);

            default:
                if (status >= 500)
                    return new(AgentApiErrorCategory.ServerError, status, null, ServerErrorMessage, true);
                if (status >= 400)
                    return new(AgentApiErrorCategory.BadRequest, status, null, BadRequestMessage, false);
                return new(AgentApiErrorCategory.Unknown, status, null, UnknownMessage, false);
        }
    }

    /// <summary>
    /// 构造「响应中断」分类结果（流式已输出内容后失败，不可重试）。
    /// </summary>
    /// <param name="status">底层错误状态码（可空）。</param>
    /// <param name="errorCode">底层错误码（可空）。</param>
    /// <returns>分类结果。</returns>
    public static AgentApiErrorInfo CreateStreamInterrupted(int? status = null, string? errorCode = null)
        => new(AgentApiErrorCategory.StreamInterrupted, status, errorCode, StreamInterruptedMessage, false);

    /// <summary>
    /// 计算第 N 次重试前的等待时长：优先遵循 Retry-After，否则指数退避 + ±20% 抖动；
    /// 两者都受 <see cref="ApiRetryOptions.MaxDelayMs"/> 上限收口。
    /// </summary>
    /// <param name="attempt">即将进行的重试序号（1 表示第一次重试）。</param>
    /// <param name="options">重试配置。</param>
    /// <param name="retryAfter">服务端 Retry-After（可空）。</param>
    /// <returns>等待时长。</returns>
    public static TimeSpan ComputeRetryDelay(int attempt, ApiRetryOptions options, TimeSpan? retryAfter = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var cap = TimeSpan.FromMilliseconds(Math.Max(1, options.MaxDelayMs));

        if (retryAfter is { } after && after > TimeSpan.Zero)
            return after > cap ? cap : after;

        var baseMs = Math.Max(1, options.BaseDelayMs);
        var factor = options.BackoffFactor <= 0 ? 1 : options.BackoffFactor;
        var ms = baseMs * Math.Pow(factor, Math.Max(0, attempt - 1));

        // 畸形配置（超大 BaseDelayMs × BackoffFactor）会让 Math.Pow 溢出为 Infinity，兜底回退基础延迟
        if (!double.IsFinite(ms) || ms <= 0)
            ms = Math.Min(baseMs, cap.TotalMilliseconds);

        // ±20% 抖动：避免多会话同时重试形成请求尖峰
        var jitter = 1 + (Random.Shared.NextDouble() * 0.4 - 0.2);
        var result = ms * jitter;
        if (!double.IsFinite(result) || result <= 0)
            return TimeSpan.FromMilliseconds(Math.Min(baseMs, cap.TotalMilliseconds));

        var delay = TimeSpan.FromMilliseconds(result);
        return delay > cap ? cap : delay;
    }

    /// <summary>
    /// 尝试从响应头提取 Retry-After。
    /// </summary>
    /// <param name="ex">原始异常。</param>
    /// <param name="delay">解析出的等待时长。</param>
    /// <returns>是否解析成功。</returns>
    public static bool TryGetRetryAfter(Exception ex, out TimeSpan delay)
    {
        delay = default;
        if (ex is not ClientResultException clientException)
            return false;

        var headers = clientException.GetRawResponse()?.Headers;
        if (headers is null || !headers.TryGetValue("Retry-After", out var value) || string.IsNullOrWhiteSpace(value))
            return false;

        if (int.TryParse(value, out var seconds) && seconds > 0)
        {
            delay = TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when))
        {
            delay = when - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero;
        }

        return false;
    }

    /// <summary>
    /// 获取类别的中文短标签（用于重试进度提示）。
    /// </summary>
    /// <param name="category">错误类别。</param>
    /// <returns>中文短标签。</returns>
    public static string GetCategoryLabel(AgentApiErrorCategory category) => category switch
    {
        AgentApiErrorCategory.Authentication => "认证失败",
        AgentApiErrorCategory.PaymentRequired => "余额不足",
        AgentApiErrorCategory.PermissionDenied => "权限不足",
        AgentApiErrorCategory.ModelNotFound => "模型不存在",
        AgentApiErrorCategory.BadRequest => "请求错误",
        AgentApiErrorCategory.ContextLengthExceeded => "上下文超长",
        AgentApiErrorCategory.UnsupportedCapability => "能力不支持",
        AgentApiErrorCategory.RateLimited => "限流",
        AgentApiErrorCategory.QuotaExhausted => "配额耗尽",
        AgentApiErrorCategory.ServerError => "服务端错误",
        AgentApiErrorCategory.NetworkError => "网络错误",
        AgentApiErrorCategory.Timeout => "请求超时",
        AgentApiErrorCategory.StreamInterrupted => "响应中断",
        AgentApiErrorCategory.ContentFiltered => "内容拦截",
        AgentApiErrorCategory.Canceled => "已取消",
        _ => "未识别错误"
    };

    private static AgentApiErrorInfo QuotaExhausted(int status, string message)
    {
        var match = QuotaResetRegex.Match(message);
        var tail = match.Success
            ? $"配额将于 {match.Groups["time"].Value.Trim()} 重置。"
            : "请等待配额刷新。";
        var text = $"API 配额已用尽：当前套餐额度已耗尽，{tail}可切换 Provider/模型或升级套餐后重试。";
        return new(AgentApiErrorCategory.QuotaExhausted, status, "insufficient_quota", text, false);
    }

    private static AgentApiErrorInfo ContextLengthExceeded(int? status)
        => new(AgentApiErrorCategory.ContextLengthExceeded, status, "context_length_exceeded", ContextLengthExceededMessage, false);

    private static AgentApiErrorInfo UnsupportedCapability(int? status)
        => new(AgentApiErrorCategory.UnsupportedCapability, status, null, UnsupportedCapabilityMessage, false);

    private static AgentApiErrorInfo ContentFiltered(int? status)
        => new(AgentApiErrorCategory.ContentFiltered, status, "content_filter", ContentFilteredMessage, false);

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: 运行测试确认通过**

Run:

```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentApiError"
```

Expected: 28 个测试全部通过。（该过滤器只跑本类，不触碰需要外部服务的集成测试。）

- [ ] **Step 5: 提交（需用户明确许可后执行）**

```powershell
cd luban-framework
git add LuBan.AIAgent/Errors/ApiErrorClassifier.cs LuBan.XTestProject/AgentApiErrorUnitTest.cs
git commit -m "新增 LLM/API 错误分类器与单元测试"
```

---

## Task 3: 容错重试客户端（TDD）

**Files:**
- Create: `luban-framework/LuBan.AIAgent/Errors/ResilientChatClient.cs`
- Create: `luban-framework/LuBan.AIAgent/Errors/ChatClientResilience.cs`
- Modify: `luban-framework/LuBan.XTestProject/AgentApiErrorUnitTest.cs`（追加 Fake 客户端与重试测试）

- [ ] **Step 1: 追加失败测试**

在 `AgentApiErrorUnitTest.cs` 文件末尾（`}` 之前的最后一个测试之后）追加以下测试方法，并新增文件级私有 Fake 类（放在 `AgentApiErrorUnitTest` 类之后）：

```csharp
    [TestMethod]
    public async Task Resilient_RetriesTransient_ThenSucceeds()
    {
        var inner = new FlakyChatClient(new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable), failTimes: 2);
        var options = new LuBanAgentOptions
        {
            ApiRetry = new ApiRetryOptions { MaxAttempts = 3, BaseDelayMs = 1, BackoffFactor = 1 }
        };
        var notices = new List<AgentRetryNotice>();
        options.OnApiRetry = notices.Add;

        var client = new ResilientChatClient(inner, options);
        var response = await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") });

        Assert.AreEqual(3, inner.Calls);
        Assert.AreEqual(2, notices.Count);
        Assert.AreEqual(1, notices[0].Attempt);
        Assert.AreEqual(3, notices[0].MaxAttempts);
        Assert.AreEqual("ok", response.Text);
    }

    [TestMethod]
    public async Task Resilient_Exhausted_ThrowsAgentApiException()
    {
        var inner = new FlakyChatClient(new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable), failTimes: 99);
        var options = new LuBanAgentOptions
        {
            ApiRetry = new ApiRetryOptions { MaxAttempts = 3, BaseDelayMs = 1, BackoffFactor = 1 }
        };

        var client = new ResilientChatClient(inner, options);

        var thrown = await Assert.ThrowsExactlyAsync<AgentApiException>(() =>
            client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") }));

        Assert.AreEqual(AgentApiErrorCategory.ServerError, thrown.Error.Category);
        Assert.AreEqual(3, inner.Calls);
    }

    [TestMethod]
    public async Task Resilient_NonRetryable_ThrowsImmediately()
    {
        var inner = new FlakyChatClient(new InvalidOperationException("bad config"), failTimes: 99);
        var options = new LuBanAgentOptions
        {
            ApiRetry = new ApiRetryOptions { MaxAttempts = 3, BaseDelayMs = 1, BackoffFactor = 1 }
        };

        var client = new ResilientChatClient(inner, options);

        var thrown = await Assert.ThrowsExactlyAsync<AgentApiException>(() =>
            client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") }));

        Assert.AreEqual(AgentApiErrorCategory.Unknown, thrown.Error.Category);
        Assert.AreEqual(1, inner.Calls);
    }

    [TestMethod]
    public async Task Resilient_Disabled_DoesNotRetry()
    {
        var inner = new FlakyChatClient(new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable), failTimes: 99);
        var options = new LuBanAgentOptions
        {
            ApiRetry = new ApiRetryOptions { Enabled = false, MaxAttempts = 3, BaseDelayMs = 1, BackoffFactor = 1 }
        };

        var client = new ResilientChatClient(inner, options);

        await Assert.ThrowsExactlyAsync<AgentApiException>(() =>
            client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") }));

        Assert.AreEqual(1, inner.Calls);
    }

    [TestMethod]
    public async Task Resilient_Streaming_RetriesBeforeFirstToken()
    {
        var inner = new ScriptedStreamClient();
        inner.Enqueue(text: null, fail: new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable));
        inner.Enqueue(text: "hello", fail: null);
        var options = new LuBanAgentOptions
        {
            ApiRetry = new ApiRetryOptions { MaxAttempts = 3, BaseDelayMs = 1, BackoffFactor = 1 }
        };

        var client = new ResilientChatClient(inner, options);
        var collected = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") }))
        {
            collected.Add(update.Text ?? string.Empty);
        }

        Assert.AreEqual(2, inner.Calls);
        Assert.AreEqual("hello", string.Concat(collected));
    }

    [TestMethod]
    public async Task Resilient_Streaming_AfterFirstToken_ThrowsStreamInterrupted()
    {
        var inner = new ScriptedStreamClient();
        inner.Enqueue(text: "part", fail: new HttpRequestException("connection reset"));
        var options = new LuBanAgentOptions
        {
            ApiRetry = new ApiRetryOptions { MaxAttempts = 3, BaseDelayMs = 1, BackoffFactor = 1 }
        };

        var client = new ResilientChatClient(inner, options);
        var collected = new List<string>();
        var thrown = await Assert.ThrowsExactlyAsync<AgentApiException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") }))
            {
                collected.Add(update.Text ?? string.Empty);
            }
        });

        Assert.AreEqual(AgentApiErrorCategory.StreamInterrupted, thrown.Error.Category);
        Assert.AreEqual(1, inner.Calls);
        Assert.AreEqual("part", string.Concat(collected));
    }

    [TestMethod]
    public async Task Resilient_Streaming_HostCanceled_DoesNotRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var inner = new ScriptedStreamClient();
        inner.Enqueue(text: null, fail: new OperationCanceledException(cts.Token));
        var options = new LuBanAgentOptions
        {
            ApiRetry = new ApiRetryOptions { MaxAttempts = 3, BaseDelayMs = 1, BackoffFactor = 1 }
        };

        var client = new ResilientChatClient(inner, options);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") }, cancellationToken: cts.Token))
            {
            }
        });

        Assert.AreEqual(1, inner.Calls);
    }

    [TestMethod]
    public void ChatClientResilience_Wrap_IsIdempotent()
    {
        var raw = new FlakyChatClient(new InvalidOperationException("x"), failTimes: 0);
        var options = new LuBanAgentOptions();

        var once = ChatClientResilience.Wrap(raw, options);
        var twice = ChatClientResilience.Wrap(once, options);

        Assert.AreSame(once, twice);
    }
```

Fake 客户端（放在 `AgentApiErrorUnitTest` 类之外、同一文件内）：

```csharp
/// <summary>
/// 前 N 次调用失败、之后返回固定响应的聊天客户端（非流式测试用）。
/// </summary>
internal sealed class FlakyChatClient : IChatClient
{
    private readonly Exception _exception;
    private readonly int _failTimes;

    public int Calls { get; private set; }

    public FlakyChatClient(Exception exception, int failTimes)
    {
        _exception = exception;
        _failTimes = failTimes;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Calls <= _failTimes)
            return Task.FromException<ChatResponse>(_exception);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>
/// 按脚本逐次返回「文本 + 是否失败」的流式聊天客户端（流式测试用）。
/// </summary>
internal sealed class ScriptedStreamClient : IChatClient
{
    private readonly Queue<(string? Text, Exception? Fail)> _steps = new();

    public int Calls { get; private set; }

    public void Enqueue(string? text, Exception? fail) => _steps.Enqueue((text, fail));

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls++;
        var step = _steps.Count > 0 ? _steps.Dequeue() : (Text: (string?)null, Fail: (Exception?)null);

        await Task.Yield();

        if (step.Text is not null)
            yield return new ChatResponseUpdate(ChatRole.Assistant, step.Text);

        if (step.Fail is not null)
            throw step.Fail;
    }

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose()
    {
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentApiError"
```

Expected: 编译失败（`ResilientChatClient` / `ChatClientResilience` 不存在）。

- [ ] **Step 3: 实现容错客户端**

`Errors/ResilientChatClient.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.AIAgent.Errors
*文件名： ResilientChatClient
*唯一标识：LLM 调用容错重试中间件
*创建时间：2026/9/17
*描述：对瞬时错误按配置自动退避重试，最终失败抛出携带分类结果的 AgentApiException
*
*****************************************************************************/
namespace LuBan.AIAgent.Errors;

/// <summary>
/// LLM 调用容错中间件：对瞬时错误（限流/5xx/网络/超时）按配置自动退避重试，
/// 最终失败时抛出 <see cref="AgentApiException"/>。
/// 流式调用仅在首个 token 产出前重试；已产出内容后中断不再重试，抛「响应中断」。
/// 必须包裹在 <c>FunctionInvokingChatClient</c> 之内，避免工具轮次整体回滚造成重复副作用。
/// </summary>
public class ResilientChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly LuBanAgentOptions _options;

    /// <summary>
    /// 创建容错客户端。
    /// </summary>
    /// <param name="inner">被包装的聊天客户端。</param>
    /// <param name="options">Agent 配置（读取 <see cref="LuBanAgentOptions.ApiRetry"/> 与 <see cref="LuBanAgentOptions.OnApiRetry"/>）。</param>
    public ResilientChatClient(IChatClient inner, LuBanAgentOptions options)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private int MaxAttempts => _options.ApiRetry.Enabled ? Math.Max(1, _options.ApiRetry.MaxAttempts) : 1;

    /// <inheritdoc/>
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        var maxAttempts = MaxAttempts;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _inner.GetResponseAsync(list, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var info = ApiErrorClassifier.Classify(ex, cancellationToken.IsCancellationRequested);
                if (info.Category == AgentApiErrorCategory.Canceled)
                    throw;

                if (attempt >= maxAttempts || !info.IsRetryable)
                {
                    Logger.Error("LLM 调用失败", ex, info.Category.ToString());
                    throw new AgentApiException(info, ex);
                }

                var delay = GetDelay(attempt, ex);
                Notify(info, attempt, maxAttempts, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        var maxAttempts = MaxAttempts;

        for (var attempt = 1; ; attempt++)
        {
            var yieldedAny = false;
            var completed = false;
            Exception? failure = null;

            await using (var enumerator = _inner
                .GetStreamingResponseAsync(list, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken))
            {
                while (true)
                {
                    ChatResponseUpdate? current = null;
                    try
                    {
                        if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                            current = enumerator.Current;
                        else
                            completed = true;
                    }
                    catch (ArgumentOutOfRangeException) when (!cancellationToken.IsCancellationRequested)
                    {
                        continue;
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }

                    if (failure is not null || completed)
                        break;

                    yieldedAny = true;
                    yield return current!;
                }
            }

            if (completed || failure is null)
                yield break;

            var info = ApiErrorClassifier.Classify(failure, cancellationToken.IsCancellationRequested);
            if (info.Category == AgentApiErrorCategory.Canceled)
                throw failure;

            if (yieldedAny)
            {
                Logger.Error("LLM 流式中断", failure, info.Category.ToString());
                throw new AgentApiException(ApiErrorClassifier.CreateStreamInterrupted(info.StatusCode, info.ErrorCode), failure);
            }

            if (attempt >= maxAttempts || !info.IsRetryable)
            {
                Logger.Error("LLM 调用失败", failure, info.Category.ToString());
                throw new AgentApiException(info, failure);
            }

            var delay = GetDelay(attempt, failure);
            Notify(info, attempt, maxAttempts, delay);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? key = null)
        => _inner.GetService(serviceType, key);

    /// <inheritdoc/>
    public void Dispose()
    {
        _inner.Dispose();
        GC.SuppressFinalize(this);
    }

    private TimeSpan GetDelay(int attempt, Exception ex)
        => ApiErrorClassifier.TryGetRetryAfter(ex, out var retryAfter)
            ? ApiErrorClassifier.ComputeRetryDelay(attempt, _options.ApiRetry, retryAfter)
            : ApiErrorClassifier.ComputeRetryDelay(attempt, _options.ApiRetry);

    private void Notify(AgentApiErrorInfo info, int attempt, int maxAttempts, TimeSpan delay)
    {
        Logger.Warn($"LLM 调用失败（{info.Category}），{delay.TotalSeconds:F0}s 后重试（第 {attempt} 次重试，最多 {maxAttempts - 1} 次）");
        try
        {
            _options.OnApiRetry?.Invoke(new AgentRetryNotice(info.Category, attempt, maxAttempts, delay));
        }
        catch (Exception ex)
        {
            Logger.Warn($"重试通知回调异常: {ex.Message}");
        }
    }
}
```

`Errors/ChatClientResilience.cs`：

```csharp
/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LuBan.AIAgent.Errors
*文件名： ChatClientResilience
*唯一标识：容错客户端包装入口
*创建时间：2026/9/17
*描述：幂等包装入口，避免重复包裹导致重试次数相乘
*
*****************************************************************************/
namespace LuBan.AIAgent.Errors;

/// <summary>
/// 容错客户端包装入口。
/// </summary>
public static class ChatClientResilience
{
    /// <summary>
    /// 为聊天客户端套上容错重试（已是 <see cref="ResilientChatClient"/> 时原样返回，保证幂等）。
    /// </summary>
    /// <param name="inner">被包装的聊天客户端。</param>
    /// <param name="options">Agent 配置。</param>
    /// <returns>容错聊天客户端。</returns>
    public static IChatClient Wrap(IChatClient inner, LuBanAgentOptions options)
        => inner is ResilientChatClient ? inner : new ResilientChatClient(inner, options);
}
```

- [ ] **Step 4: 运行测试确认通过**

Run:

```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentApiError"
```

Expected: 39 个测试全部通过（28 + 8 + 3 个审查补充用例）。

- [ ] **Step 5: 提交（需用户明确许可后执行）**

```powershell
cd luban-framework
git add LuBan.AIAgent/Errors/ResilientChatClient.cs LuBan.AIAgent/Errors/ChatClientResilience.cs LuBan.XTestProject/AgentApiErrorUnitTest.cs
git commit -m "新增 LLM 调用容错重试中间件与单元测试"
```

---

### Task 3 审查修复记录（已落地）

- `ResilientChatClient` 流式循环：`try` 仅包 `MoveNextAsync`（与 `SanitizingChatClient` 对齐），`ArgumentOutOfRangeException continue` 增加单次尝试内上限 `MaxConsecutiveEnumeratorOutOfRange = 3`，超出按失败处理（防无产出热自旋）；成功 `MoveNextAsync` 时计数器清零（需补一行，见 Task 4 实施提示）。
- 配置读取：每次调用快照 `_options.ApiRetry ?? new ApiRetryOptions()`，删除了 `MaxAttempts` 属性；`OnApiRetry` 仍实时读取（支持宿主晚注册）。
- 回调异常日志改为带异常对象（保留栈）。
- `ChatClientResilience.Wrap` 文档注明只允许直接包装原始客户端（防装饰器嵌套乘算）。
- 有意保留：流式取消路径 `throw failure;`（不引 `ExceptionDispatchInfo`，避免不可达告警风险）；`yieldedAny` 对任意 update 计数（含工具调用 delta，重试会重放工具副作用）。
- 新增 3 个用例：首 token 前重试耗尽 → 原始类别；回调抛异常不中断重试；退避等待期取消（`Assert.ThrowsAsync<OperationCanceledException>`，`Task.Delay` 抛派生 `TaskCanceledException`）。

---

## Task 4: framework 接入（工厂 / 规划器 / 编排 / 压缩）

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/LuBanAgentFactory.cs`
- Modify: `luban-framework/LuBan.AIAgent/Orchestration/Planner/LlmTaskPlanner.cs`
- Modify: `luban-framework/LuBan.AIAgent/Orchestration/AutoOrchestrationMiddleware.cs`
- Modify: `luban-framework/LuBan.AIAgent/Sessions/SessionChatHistoryProvider.cs`

- [ ] **Step 1: 工厂默认客户端包装**

`LuBanAgentFactory.cs` 构造函数 `:52` 原代码：

```csharp
        _chatClient = chatClient;
```

改为：

```csharp
        // 默认客户端统一套容错重试：覆盖主对话、摘要压缩（历史压缩也使用该实例）
        _chatClient = ChatClientResilience.Wrap(chatClient, options.Value);
```

- [ ] **Step 2: 工厂路由客户端包装**

`LuBanAgentFactory.cs` `BuildFunctionClient` `:181` 原代码：

```csharp
        IChatClient sanitizedClient = new SanitizingChatClient(ResolveChatClient(modelName));
```

改为（容错在内、清洗在外：重试的是真实 provider 调用，只清洗一次）：

```csharp
        IChatClient sanitizedClient = new SanitizingChatClient(
            ChatClientResilience.Wrap(ResolveChatClient(modelName), opts));
```

> `BuildHistoryProvider`（`:226-232`）继续传 `_chatClient`，已在 Step 1 中包装，无需再改。

- [ ] **Step 3: 规划器客户端包装**

`Orchestration/Planner/LlmTaskPlanner.cs` 构造函数 `:44` 原代码：

```csharp
        _chatClient = ResolvePlannerClient(chatClient, providerRouter, options.Value.Orchestration?.PlannerModel);
```

改为：

```csharp
        _chatClient = ChatClientResilience.Wrap(
            ResolvePlannerClient(chatClient, providerRouter, options.Value.Orchestration?.PlannerModel),
            options.Value);
```

- [ ] **Step 4: 规划期 API 错误不再静默降级**

`Orchestration/AutoOrchestrationMiddleware.cs` `TryPlanAsync` 的 catch 块 `:70-74` 原代码：

```csharp
        catch (Exception ex)
        {
            Logger.Debug($"[OrchDiag] skip: planner threw {ex.GetType().Name}: {ex.Message}");
            return null;
        }
```

改为：

```csharp
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 可识别的 API 故障（认证/配额/限流/网络/服务端等）不再静默降级为常规对话，向上透出分类结果；
            // 仅「规划结果格式/校验失败」这类非 API 错误继续降级
            var info = ApiErrorClassifier.Classify(ex, cancellationToken.IsCancellationRequested);
            if (info.Category is not AgentApiErrorCategory.Unknown and not AgentApiErrorCategory.Canceled)
            {
                Logger.Error("编排规划失败（API 错误）", ex, info.Category.ToString());
                throw new AgentApiException(info, ex);
            }

            Logger.Debug($"[OrchDiag] skip: planner threw {ex.GetType().Name}: {ex.Message}");
            return null;
        }
```

- [ ] **Step 5: 摘要压缩失败降级为跳过压缩**

`Sessions/SessionChatHistoryProvider.cs` `:93-118` 的整块原代码：

```csharp
        if (history.Count > _targetCount + _threshold)
        {
#pragma warning disable MEAI001
            var reducer = new SummarizingChatReducer(_chatClient, _targetCount, _threshold);
#pragma warning restore MEAI001
            var reduced = (await reducer.ReduceAsync(history, cancellationToken)).ToList();

            if (reduced.Count > 0 && reduced.Count < history.Count)
            {
                var keptCount = Math.Min(reduced.Count - 1, history.Count);
                var keptTail = messages.Skip(messages.Count - keptCount).ToList();
                var compactedIds = messages.Take(messages.Count - keptCount).Select(m => m.Id)
                    .Concat(summaries.Select(s => s.Id))
                    .ToList();

                // 先写摘要、后归档：反序执行时若归档成功而写摘要失败，历史会被永久裁掉且无摘要兜底。
                var summaryText = reduced[0].Text ?? "";
                await _sessionManager.AddMessageAsync(sessionId, "summary", summaryText, EstimateTokens(summaryText));
                await _sessionManager.MarkMessagesCompactedAsync(sessionId, compactedIds);

                latestSummary = new SessionMessage { Id = long.MaxValue, Role = "summary", Content = summaryText };
                history = keptTail
                    .Select(m => new ChatMessage(MapRole(m.Role), m.Content))
                    .ToList();
            }
        }
```

改为：

```csharp
        if (history.Count > _targetCount + _threshold)
        {
            try
            {
#pragma warning disable MEAI001
                var reducer = new SummarizingChatReducer(_chatClient, _targetCount, _threshold);
#pragma warning restore MEAI001
                var reduced = (await reducer.ReduceAsync(history, cancellationToken)).ToList();

                if (reduced.Count > 0 && reduced.Count < history.Count)
                {
                    var keptCount = Math.Min(reduced.Count - 1, history.Count);
                    var keptTail = messages.Skip(messages.Count - keptCount).ToList();
                    var compactedIds = messages.Take(messages.Count - keptCount).Select(m => m.Id)
                        .Concat(summaries.Select(s => s.Id))
                        .ToList();

                    // 先写摘要、后归档：反序执行时若归档成功而写摘要失败，历史会被永久裁掉且无摘要兜底。
                    var summaryText = reduced[0].Text ?? "";
                    await _sessionManager.AddMessageAsync(sessionId, "summary", summaryText, EstimateTokens(summaryText));
                    await _sessionManager.MarkMessagesCompactedAsync(sessionId, compactedIds);

                    latestSummary = new SessionMessage { Id = long.MaxValue, Role = "summary", Content = summaryText };
                    history = keptTail
                        .Select(m => new ChatMessage(MapRole(m.Role), m.Content))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                var info = ApiErrorClassifier.Classify(ex, cancellationToken.IsCancellationRequested);
                if (info.Category == AgentApiErrorCategory.Canceled)
                    throw;

                // 压缩失败不打断主对话：跳过压缩，用未压缩历史继续
                Logger.Warn($"历史摘要压缩失败（{info.Category}），已跳过压缩继续对话", ex);
            }
        }
```

- [ ] **Step 6: 构建 + 跑分类/重试单测**

Run:

```powershell
dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj -c Debug
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentApiError"
```

Expected: 构建 0 警告 0 错误；39 个测试通过（Task 2/3 的分类与重试用例）。

- [ ] **Step 7: 提交（需用户明确许可后执行）**

```powershell
cd luban-framework
git add LuBan.AIAgent/LuBanAgentFactory.cs LuBan.AIAgent/Orchestration/Planner/LlmTaskPlanner.cs LuBan.AIAgent/Orchestration/AutoOrchestrationMiddleware.cs LuBan.AIAgent/Sessions/SessionChatHistoryProvider.cs
git commit -m "接入错误分类与自动重试：工厂/规划器/编排/压缩"
```

---

## Task 4b: Core 禁用 SDK 内置重试（用户批准的追加项）

**背景（源码核实）**：`System.ClientModel` 1.14 的默认 pipeline 在 `RetryPolicy == null` 时使用 `ClientRetryPolicy.Default`（`DefaultMaxRetries = 3`，0.8s 起指数退避，尊重 Retry-After），即 **OpenAI SDK 已内置 4 次尝试**；同时传输层把 `HttpRequestException` 包成 `ClientResultException`（`Status == 0`），重试耗尽抛 `AggregateException`。若不处理，中间件的 3 次尝试会与 SDK 的 4 次叠加（最坏 12 次请求/调用），且 SDK 重试对用户不可见。

**结论**：禁用 SDK 内置重试，由 `ResilientChatClient` 统一重试。

**Files:**
- Modify: `luban-agent/LubanAgentCore/Configuration/ConfigManager.cs`
- Modify: `luban-agent/LubanAgentCore/Hosting/AgentHostBuilder.cs`

- [ ] **Step 1: 默认模型客户端禁用 SDK 重试**

`LubanAgentCore/Configuration/ConfigManager.cs` `:538-541` 原代码：

```csharp
        var clientOptions = new OpenAI.OpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
```

改为：

```csharp
        var clientOptions = new OpenAI.OpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            // 禁用 SDK 内置重试（默认 ClientRetryPolicy.Default 会再重试 3 次），
            // 由 LuBan.AIAgent 的 ResilientChatClient 统一重试，避免次数相乘与不可见重试
            RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0)
        };
```

- [ ] **Step 2: 多 Provider 路由客户端禁用 SDK 重试**

`LubanAgentCore/Hosting/AgentHostBuilder.cs` `:72-79` 原代码：

```csharp
                    var clientOptions = new OpenAI.OpenAIClientOptions();
                    if (p.NetworkTimeoutSeconds.HasValue)
                        clientOptions.NetworkTimeout = TimeSpan.FromSeconds(p.NetworkTimeoutSeconds.Value);
                    if (!string.IsNullOrEmpty(p.BaseUrl))
                        clientOptions.Endpoint = new Uri(p.BaseUrl);
```

改为：

```csharp
                    var clientOptions = new OpenAI.OpenAIClientOptions
                    {
                        // 禁用 SDK 内置重试，由 ResilientChatClient 统一重试（与 ConfigManager.CreateChatClient 一致）
                        RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0)
                    };
                    if (p.NetworkTimeoutSeconds.HasValue)
                        clientOptions.NetworkTimeout = TimeSpan.FromSeconds(p.NetworkTimeoutSeconds.Value);
                    if (!string.IsNullOrEmpty(p.BaseUrl))
                        clientOptions.Endpoint = new Uri(p.BaseUrl);
```

- [ ] **Step 3: 统一包装 DI 注册的 IChatClient（覆盖 /compact）**

`LuBanAgentCore/Hosting/AgentHostBuilder.cs` `:58-62` 原代码：

```csharp
        services.AddScoped<Microsoft.Extensions.AI.IChatClient>(sp =>
        {
            var cm = sp.GetRequiredService<ConfigManager>();
            return cm.CreateChatClient();
        });
```

改为（DI 消费者 `LlmTaskPlanner`、`CompactContextToolPlugin`/`SummarizingChatReducer` 也获得重试与分类；工厂内再 `Wrap` 时幂等，不会叠加）：

```csharp
        services.AddScoped<Microsoft.Extensions.AI.IChatClient>(sp =>
        {
            var cm = sp.GetRequiredService<ConfigManager>();
            var opts = sp.GetRequiredService<IOptions<LuBanAgentOptions>>().Value;
            return LuBan.AIAgent.Errors.ChatClientResilience.Wrap(cm.CreateChatClient(), opts);
        });
```

> 背景：`Tools/Context/CompactContextToolPlugin.cs:148` 从 DI 直接取 `IChatClient` 喂给 `SummarizingChatReducer`，绕过工厂包装；不包这一层，`/compact` 的 API 错误既无重试也无友好文案。

- [ ] **Step 4: 构建验证**

Run:

```powershell
dotnet build luban-agent/LubanAgentCore/LubanAgentCore.csproj -c Debug
```

Expected: 0 警告 0 错误。

- [ ] **Step 5: 提交（需用户明确许可后执行）**

```powershell
cd luban-agent
git add LubanAgentCore/Configuration/ConfigManager.cs LubanAgentCore/Hosting/AgentHostBuilder.cs
git commit -m "禁用 OpenAI SDK 内置重试并在 DI 层统一包装容错客户端"
```

---

## Task 5: CLI 接入

**Files:**
- Modify: `luban-agent/LubanAgentCli/GlobalUsings.cs`
- Modify: `luban-agent/LubanAgentCli/App/ViewModels/ConversationViewModel.cs`
- Modify: `luban-agent/LubanAgentCli/App/Views/RootView.cs`
- Modify: `luban-agent/LubanAgentCli/Commands/CommandBase.cs`
- Modify: `luban-agent/LubanAgentCli/Commands/InitCommand.cs`
- Modify: `luban-agent/LubanAgentCli/App/ViewModels/CommandViewModel.cs`
- Modify: `luban-agent/LubanAgentCli/appsettings.json`

- [ ] **Step 1: 全局 using**

`LubanAgentCli/GlobalUsings.cs`：在 `global using LuBan.AIAgent.Configuration;` 下一行插入 `global using LuBan.AIAgent.Errors;`；在 `global using Microsoft.Extensions.DependencyInjection;` 下一行插入 `global using Microsoft.Extensions.Options;`。

- [ ] **Step 2: 主对话渲染友好文案**

`LubanAgentCli/App/ViewModels/ConversationViewModel.cs` `ProcessInputAsync` 的 `:561-566` 原代码：

```csharp
        catch (Exception ex)
        {
            Logger.Error("Agent 对话异常", ex);
            _dispatcher.Invoke(() =>
                _doc.AppendBlock(new SystemBlock($"错误: {ex.Message}", foreground: BlockColors.Failure)));
        }
```

改为（在 `catch (OperationCanceledException)` 之后、通用 catch 之前插入新分支）：

```csharp
        catch (AgentApiException apiEx)
        {
            // 框架已完成错误分类：只渲染友好文案，原始异常进日志
            Logger.Error("Agent 对话异常", apiEx, apiEx.Error.Category.ToString());
            _dispatcher.Invoke(() =>
                _doc.AppendBlock(new SystemBlock(apiEx.Error.FriendlyMessage, foreground: BlockColors.Failure)));
        }
        catch (Exception ex)
        {
            Logger.Error("Agent 对话异常", ex);
            _dispatcher.Invoke(() =>
                _doc.AppendBlock(new SystemBlock($"错误: {ex.Message}", foreground: BlockColors.Failure)));
        }
```

- [ ] **Step 3: 重试进度提示**

`LubanAgentCli/App/Views/RootView.cs` 构造函数中 `_doc = new ConversationDocument();`（`:76`）之后插入：

```csharp
        // 自动重试进度提示：框架回调运行在调用线程，需编组到 UI 线程
        services.GetRequiredService<IOptions<LuBanAgentOptions>>().Value.OnApiRetry = notice =>
            _dispatcher.Invoke(() => _doc.AppendBlock(new SystemBlock(
                $"⟳ {ApiErrorClassifier.GetCategoryLabel(notice.Category)}：{notice.Delay.TotalSeconds:F0}s 后重试（第 {notice.Attempt} 次，最多 {notice.MaxAttempts - 1} 次）",
                foreground: BlockColors.Accent)));
```

- [ ] **Step 4: 删除旧映射方法**

`LubanAgentCli/Commands/CommandBase.cs`：删除 `GetFriendlyApiErrorMessage` 整个方法及其 XML 注释（从 `/// <summary>`「获取友好的 API 错误信息」到方法结束的 `}`，即当前 `:92-150`）。

- [ ] **Step 4b: 非 TUI 容器包装 IChatClient（保持“单一重试权威”闭环）**

`LubanAgentCli/Commands/CommandBase.cs` `BuildServiceProvider()` `:87` 原代码：

```csharp
        services.AddSingleton<IChatClient>(sp => ConfigManager.CreateChatClient());
```

改为：

```csharp
        services.AddSingleton<IChatClient>(sp => ChatClientResilience.Wrap(
            ConfigManager.CreateChatClient(),
            sp.GetRequiredService<IOptions<LuBanAgentOptions>>().Value));
```

> 背景：`AgentHostBuilder` 已包装，但非 TUI 容器（`/skill` 等命令路径）仍注册裸客户端；SDK 重试被禁用后，该路径将完全无重试与分类。`LubanAgentCli/GlobalUsings.cs` 已有 `global using LuBan.AIAgent.Configuration;` 与 `LuBan.AIAgent.Errors`（Task 5 Step 1 添加），`Microsoft.Extensions.Options` 亦在 Step 1 添加。

- [ ] **Step 5: /init 使用分类结果**

`LubanAgentCli/Commands/InitCommand.cs` `RunInitAsync` 的 catch `:174-178` 原代码：

```csharp
        catch (Exception ex)
        {
            Logger.Error("InitCommand 操作异常", ex);
            Writer.WriteError(GetFriendlyApiErrorMessage(ex));
        }
```

改为：

```csharp
        catch (Exception ex)
        {
            Logger.Error("InitCommand 操作异常", ex);
            // 框架已包装 LLM/API 失败为 AgentApiException；其余（配置类）异常保留原文
            var message = ex is AgentApiException apiEx ? apiEx.Error.FriendlyMessage : ex.Message;
            Writer.WriteError(message);
        }
```

- [ ] **Step 6: 管理命令异常友好化**

`LubanAgentCli/App/ViewModels/CommandViewModel.cs` `:208-212` 原代码：

```csharp
            catch (Exception ex)
            {
                Logger.Error("管理命令执行异常", ex);
                _writer.WriteError($"命令执行异常: {ex.Message}");
            }
```

改为：

```csharp
            catch (Exception ex)
            {
                Logger.Error("管理命令执行异常", ex);
                var message = ex is AgentApiException apiEx ? apiEx.Error.FriendlyMessage : ex.Message;
                _writer.WriteError($"命令执行异常: {message}");
            }
```

- [ ] **Step 7: appsettings 示例配置**

`LubanAgentCli/appsettings.json`：在 `LuBanAgent` 节点内 `"MaxToolLoopIterations": 30,` 之后插入：

```jsonc
    // 瞬时错误（限流/5xx/网络/超时）自动重试：最大尝试次数 3（含首次），1s/2s 指数退避，单次等待上限 30s
    "ApiRetry": {
      "Enabled": true,
      "MaxAttempts": 3,
      "BaseDelayMs": 1000,
      "BackoffFactor": 2,
      "MaxDelayMs": 30000
    },
```

- [ ] **Step 8: 构建验证**

Run:

```powershell
dotnet build luban-agent/LubanAgentCli/LubanAgentCli.csproj -c Debug
```

Expected: 0 警告 0 错误。

- [ ] **Step 9: 提交（需用户明确许可后执行）**

```powershell
cd luban-agent
git add LubanAgentCli
git commit -m "CLI 接入统一错误分类与重试进度提示"
```

---

## Task 6: Codex 接入

**Files:**
- Modify: `luban-agent/LubanAgentCodex/ViewModels/MainWindowViewModel.cs`
- Modify: `luban-agent/LubanAgentCodex/appsettings.json`

- [ ] **Step 1: 补充 using**

`LubanAgentCodex/ViewModels/MainWindowViewModel.cs` 顶部 `using LubanAgentCore.Utils;` 之后插入：

```csharp
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Errors;
using Microsoft.Extensions.Options;
```

- [ ] **Step 2: 设置重试进度回调**

构造函数 `:82-85` 原代码：

```csharp
    public MainWindowViewModel(IServiceProvider services)
    {
        _agentHost = new AgentHostService(services);
    }
```

改为：

```csharp
    public MainWindowViewModel(IServiceProvider services)
    {
        _agentHost = new AgentHostService(services);

        // 自动重试进度提示：框架回调运行在调用线程，编组到 UI 线程追加系统消息
        services.GetRequiredService<IOptions<LuBanAgentOptions>>().Value.OnApiRetry = notice =>
            Dispatcher.UIThread.Post(() => Messages.Add(new SystemMessageItem
            {
                Content = $"⟳ {ApiErrorClassifier.GetCategoryLabel(notice.Category)}：{notice.Delay.TotalSeconds:F0}s 后重试（第 {notice.Attempt} 次，最多 {notice.MaxAttempts - 1} 次）"
            }));
    }
```

- [ ] **Step 3: SendAsync 渲染友好文案**

`SendAsync` `:153-160` 原代码：

```csharp
        catch (Exception ex)
        {
            Messages.Add(new SystemMessageItem
            {
                Content = $"错误: {ex.Message}",
                IsError = true
            });
        }
```

改为：

```csharp
        catch (AgentApiException apiEx)
        {
            // 框架已完成错误分类：只渲染友好文案，原始异常进日志
            Messages.Add(new SystemMessageItem
            {
                Content = apiEx.Error.FriendlyMessage,
                IsError = true
            });
        }
        catch (Exception ex)
        {
            Messages.Add(new SystemMessageItem
            {
                Content = $"错误: {ex.Message}",
                IsError = true
            });
        }
```

- [ ] **Step 4: 初始化与 /init 渲染统一**

`SendAsync` 初始化 catch `:112-120` 原代码：

```csharp
            catch (Exception ex)
            {
                Messages.Add(new SystemMessageItem
                {
                    Content = $"初始化失败: {ex.Message}",
                    IsError = true
                });
                return;
            }
```

改为：

```csharp
            catch (Exception ex)
            {
                var message = ex is AgentApiException apiEx ? apiEx.Error.FriendlyMessage : ex.Message;
                Messages.Add(new SystemMessageItem
                {
                    Content = $"初始化失败: {message}",
                    IsError = true
                });
                return;
            }
```

`RunInitAgentsMdAsync` catch `:261-264` 原代码：

```csharp
        catch (Exception ex)
        {
            Messages.Add(new SystemMessageItem { Content = $"错误: {ex.Message}", IsError = true });
        }
```

改为：

```csharp
        catch (Exception ex)
        {
            var message = ex is AgentApiException apiEx ? apiEx.Error.FriendlyMessage : ex.Message;
            Messages.Add(new SystemMessageItem { Content = $"错误: {message}", IsError = true });
        }
```

> `case ErrorEvent e:`（`:789-791`）保持原文渲染，属本期非目标（该通道无结构化状态码）。

- [ ] **Step 5: appsettings 示例配置**

`LubanAgentCodex/appsettings.json`：与 Task 5 Step 7 相同的 `ApiRetry` 节点插入到 `LuBanAgent` 节点内。

- [ ] **Step 6: 构建验证**

Run:

```powershell
dotnet build luban-agent/LubanAgentCodex/LubanAgentCodex.csproj -c Debug
```

Expected: 无 `error CS`（若进程占用 `LubanAgentCore.dll` 会出现 MSB3021/MSB3027 文件锁错误，先关闭正在运行的 Codex/VS 再构建）。

- [ ] **Step 7: 提交（需用户明确许可后执行）**

```powershell
cd luban-agent
git add LubanAgentCodex
git commit -m "Codex 接入统一错误分类与重试进度提示"
```

---

## Task 7: 文档与冒烟用例

**Files:**
- Modify: `luban-framework/LuBan.AIAgent/README.md`
- Modify: `luban-framework/LuBan.AIAgent/README.en.md`
- Modify: `luban-agent/LubanAgentCli/docs/TUI-SMOKE-TEST.md`

- [ ] **Step 1: 中文 README 增补章节**

`README.md`：在 `### 9. 多 Agent 任务编排` 小节结束、`## 支持的 AI Provider` 之前插入：

````markdown
### 10. 错误处理与自动重试

框架对所有 LLM/API 调用做统一错误分类（结构化优先：HTTP 状态码 + `error.code`），最终失败抛出 `AgentApiException`，宿主渲染 `Error.FriendlyMessage` 即可：

| 类别 | 典型错误 | 是否自动重试 |
|---|---|---|
| 认证/权限/余额 | 401 / 402 / 403 | 否，终止并提示 |
| 模型不存在 | 404 / 410 | 否，终止并给出排查清单 |
| 参数错误 / 上下文超长 / 能力不支持 | 400 / 413 / 422 | 否，终止并提示 |
| 内容风控 | `content_filter` | 否，终止并建议改措辞 |
| 限流 | 429 `rate_limit_exceeded` | 是（默认 3 次尝试，1s/2s 指数退避 + 抖动，遵循 `Retry-After`，单次等待上限 30s） |
| 服务端错误 / 网络错误 / 超时 | 5xx / DNS/TLS / 超时 | 是（同上） |
| 配额耗尽 | 429 `insufficient_quota` | 否，终止并在文案中带上服务端给出的重置时间 |
| 流式中断 | 已输出内容后连接断开 | 否，保留已输出并提示响应中断 |

配置（`appsettings.json` 的 `LuBanAgent` 节）：

```json
"ApiRetry": {
  "Enabled": true,
  "MaxAttempts": 3,
  "BaseDelayMs": 1000,
  "BackoffFactor": 2,
  "MaxDelayMs": 30000
}
```

重试进度回调（由宿主在启动时设置，回调在调用线程执行）：

```csharp
options.Value.OnApiRetry = notice =>
    Console.WriteLine($"{notice.Category} 将在 {notice.Delay.TotalSeconds:F0}s 后重试（第 {notice.Attempt} 次）");
```

说明：

- 流式调用仅在**首个 token 产出前**重试；已产出内容后中断不会重试，避免内容重复。
- 容错中间件包裹在 `FunctionInvokingChatClient` 之内，每个工具轮次的 API 调用独立重试。
- OpenAI SDK 的内置重试（`ClientRetryPolicy.Default`，默认再重试 3 次）已在宿主侧禁用，重试次数与进度提示以本配置为准。
- 编排规划期的可识别 API 故障不再静默降级为常规对话，而是按分类结果透出。
- 会话摘要压缩失败会降级为「跳过压缩」继续对话，不再打断主对话。
````

- [ ] **Step 2: 英文 README 同步**

`README.en.md`：在 `### 9. Multi-Agent Task Orchestration` 小节结束、`## Supported AI Providers` 之前插入：

````markdown
### 10. Error Handling and Automatic Retry

All LLM/API calls are classified by the framework (structured first: HTTP status + `error.code`). On final failure an `AgentApiException` is thrown; hosts only need to render `Error.FriendlyMessage`:

| Category | Typical error | Auto retry |
|---|---|---|
| Auth / permission / balance | 401 / 402 / 403 | No — abort and show guidance |
| Model not found | 404 / 410 | No — abort with a checklist |
| Bad request / context length / unsupported capability | 400 / 413 / 422 | No — abort with guidance |
| Content moderation | `content_filter` | No — abort and suggest rewording |
| Rate limit | 429 `rate_limit_exceeded` | Yes (3 attempts by default, 1s/2s exponential backoff with jitter, honors `Retry-After`, single-wait cap 30s) |
| Server / network / timeout | 5xx / DNS/TLS / timeout | Yes (same as above) |
| Quota exhausted | 429 `insufficient_quota` | No — abort; the server-provided reset time is included in the message |
| Stream interruption | Connection drops after content was emitted | No — keep partial output and report the interruption |

Configuration (`LuBanAgent` section in `appsettings.json`):

```json
"ApiRetry": {
  "Enabled": true,
  "MaxAttempts": 3,
  "BaseDelayMs": 1000,
  "BackoffFactor": 2,
  "MaxDelayMs": 30000
}
```

Retry progress callback (set by the host at startup; invoked on the calling thread):

```csharp
options.Value.OnApiRetry = notice =>
    Console.WriteLine($"{notice.Category} retrying in {notice.Delay.TotalSeconds:F0}s (attempt {notice.Attempt})");
```

Notes:

- Streaming calls are retried only **before the first token**; once content has been emitted a failure is not retried to avoid duplicated output.
- The resilience middleware sits inside `FunctionInvokingChatClient`, so each tool round retries its own API call independently.
- The OpenAI SDK's built-in retries (`ClientRetryPolicy.Default`, 3 extra attempts) are disabled on the host side so the configured retry budget and progress notifications are authoritative.
- Recognizable API failures during orchestration planning are no longer silently downgraded to a normal chat; they surface with their classification.
- Session summarization failures degrade to "skip compaction" instead of breaking the main conversation.
````

- [ ] **Step 3: 冒烟用例**

`LubanAgentCli/docs/TUI-SMOKE-TEST.md`：在 `## 预期结果` 之前插入：

```markdown
## Provider/模型错误处理

- [ ] 23. 把当前 Provider 的 API Key 改成无效值后发送消息 — 显示「API 认证失败：API Key 无效或已过期…」，本轮终止，仍可继续输入；日志含原始异常
- [ ] 24. 把模型名改成不存在的值后发送消息 — 显示「模型不存在或已下线…」排查清单，不重试
- [ ] 25. 使用配额耗尽的 Key — 显示「API 配额已用尽…配额将于 xx 重置」（含服务端重置时间），不出现重试提示
- [ ] 26. 断网后发送消息 — 聊天区先出现「⟳ 网络错误：Ns 后重试（第 1 次，最多 2 次）」，最多两次，随后显示「网络连接失败…」
- [ ] 27. 断网重试等待期间按 Esc — 立即显示「任务已取消」，不再继续重试
```

- [ ] **Step 4: 提交（需用户明确许可后执行）**

```powershell
cd luban-framework
git add LuBan.AIAgent/README.md LuBan.AIAgent/README.en.md
git commit -m "README 增补错误处理与自动重试说明"

cd ../luban-agent
git add LubanAgentCli/docs/TUI-SMOKE-TEST.md
git commit -m "补充错误处理冒烟用例"
```

---

## Task 8: 全量验证与发布

- [ ] **Step 1: 全量构建**

```powershell
dotnet build luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj -c Debug
dotnet build luban-agent/LubanAgent.slnx -c Debug
```

Expected: framework 0/0；agent 0/0（先关闭运行中的 Codex/VS，避免 DLL 文件锁导致的 MSB3021/MSB3027）。

- [ ] **Step 2: 单测子集回归**

```powershell
dotnet test luban-framework/LuBan.XTestProject/LuBan.UnitTestProject.csproj --filter "FullyQualifiedName~AgentApiError"
```

Expected: 39 通过。可选回归 `--filter "FullyQualifiedName~AIAgentUnitTest"`（已知 `TestDependencyInjection` 为预先存在失败，仓库环境问题）。

- [ ] **Step 3: 手动冒烟**

按 `LubanAgentCli/docs/TUI-SMOKE-TEST.md` 用例 23–27 执行（需要真实 Provider，配额/网络类用例按可操作性选做）。

- [ ] **Step 4: 发版与回退（需用户明确许可后执行）**

1. 递增 `luban-framework/LuBan.AIAgent/LuBan.AIAgent.csproj` `<Version>`（当前 `2026.9.16.1` → 如 `2026.9.17.1`）。
2. `dotnet pack` 并发布 NuGet。
3. 把 `luban-agent/LubanAgentCore/LubanAgentCore.csproj:11` 的 `ProjectReference` 改回 `PackageReference Include="LuBan.AIAgent" Version="<新版本>"`。
4. 重新 `dotnet build luban-agent/LubanAgent.slnx -c Debug` 验证。
5. 两个子仓库分别 git 提交（中文短句，**须用户许可**）。

---

## 回滚方式

- 代码回滚：删除 `LuBan.AIAgent/Errors/` 目录，还原 `GlobalUsings.cs`、`LuBanAgentOptions.cs`、`LuBanAgentFactory.cs`、`LlmTaskPlanner.cs`、`AutoOrchestrationMiddleware.cs`、`SessionChatHistoryProvider.cs` 与 4 个宿主文件；删除 `AgentApiErrorUnitTest.cs`。
- 配置回滚：移除两个 `appsettings.json` 中的 `ApiRetry` 节点（缺省值与原行为一致，删掉即回到无重试）。
- 行为差异：`ResilientChatClient` 包装是唯一的运行时开关入口（`LuBanAgentOptions.ApiRetry.Enabled=false` 可立即关闭重试，无需回滚代码）。

## 已知限制

1. `Retry-After` 解析依赖 `ClientResultException.GetRawResponse()`，该路径无单测覆盖（`PipelineResponse` 为抽象类，构造成本高），靠用例 26 手动验证。
2. `ErrorEvent`（流内 `ErrorContent`）保持原文，可能是英文/厂商原文。
3. 工具轮次内每次 API 调用独立重试，最坏情况（10 轮工具循环）单轮对话最多 40 次请求；按定稿决策不做轮次累计上限。
4. 失败轮不落库（保持现状），重载历史后该轮消失。
5. **工具暴露编排路径**（`Orchestration:ExposeAsTool=true`，两个宿主默认 false）：`Tools/Orchestration/OrchestrationToolGroup.cs` 的 catch-all 会把 `AgentApiException` 折叠成 `ToolResult.Fail`（文案仍是 `FriendlyMessage`，模型可见、宿主不感知）；且 `FunctionInvokingChatClient` 会把工具内异常转成 `FunctionResultContent.Exception`，rethrow 也无法抵达宿主，故不做修改。
6. **反思/重规划路径**（`Orchestrator.cs` 的 reflection 调用）失败仍折成文本反思结果，不抛 `AgentApiException`。
7. **`/skill` 非 TUI 容器**（`CommandBase.BuildServiceProvider`）未接 `OnApiRetry`：重试与分类文案正常，但无重试进度提示（新建容器内 `LuBanAgentOptions.OnApiRetry` 为 null）。
8. **摘要压缩半成功**：`AddMessageAsync` 成功而 `MarkMessagesCompactedAsync` 失败时会重复写摘要（现在不再打断主对话）；概率低，未加会话级闩锁。
9. `RootView.Dispose` 未清空 `OnApiRetry`（lambda 捕获 view，根容器 options 会多持有已释放视图的引用；回调投递到已关闭 dispatcher 时被静默丢弃）。