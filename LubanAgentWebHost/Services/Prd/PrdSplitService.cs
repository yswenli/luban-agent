/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.Prd
*文件名： PrdSplitService
*描述：PRD 拆分预览：同步来源 + 一次性非流式模型调用 + 输出归一化
*
*****************************************************************************/

namespace LubanAgentWebHost.Services.Prd;

/// <summary>
/// PRD 拆分预览服务。
/// </summary>
/// <remarks>
/// 采用宿主一次性非流式调用（与提示词改写同口径）：不装配工具、不带会话历史，
/// 只把同步结果与补充文档交给模型，要求其直接返回 JSON 计划。
/// 模型选择与客户端创建在请求作用域内解析，避免跨用户复用。
/// </remarks>
public sealed class PrdSplitService
{
    /// <summary>拆分调用整体超时。</summary>
    private static readonly TimeSpan SplitTimeout = TimeSpan.FromSeconds(300);

    private readonly PrdSyncService _sync;
    private readonly IServiceProvider _services;

    /// <summary>
    /// 创建拆分预览服务。
    /// </summary>
    /// <param name="sync">同步服务。</param>
    /// <param name="services">作用域容器（按需解析模型选择与供应商路由：两者都可能因未配置而不注册）。</param>
    public PrdSplitService(PrdSyncService sync, IServiceProvider services)
    {
        _sync = sync;
        _services = services;
    }

    /// <summary>
    /// 生成拆分预览。
    /// </summary>
    /// <param name="request">拆分请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>拆分计划；模型未配置 / 超时 / 输出不可解析时返回失败信封。</returns>
    public async Task<YunxiaoWorkbenchResult<PrdSplitPreviewResult>> PreviewAsync(
        PrdSplitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            return YunxiaoWorkbenchResult<PrdSplitPreviewResult>.Failure(400, "项目 ID 不能为空");

        var documents = (request.Documents ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d.Content))
            .ToList();

        var sync = await _sync.SyncAsync(new PrdSyncRequest(request.ProjectId, request.SprintId), cancellationToken)
            .ConfigureAwait(false);
        if (!sync.Ok)
            return YunxiaoWorkbenchResult<PrdSplitPreviewResult>.Failure(sync.Code, sync.Message ?? "同步失败");

        var source = sync.Data!;
        if (source.Requirements.Count == 0 && documents.Count == 0)
        {
            var reason = string.IsNullOrWhiteSpace(source.Message)
                ? "没有可拆分的内容：云效同步为空，请指定迭代或上传本地 PRD 后重试"
                : $"没有可拆分的内容：{source.Message}";
            return YunxiaoWorkbenchResult<PrdSplitPreviewResult>.Failure(400, reason);
        }

        var selection = _services.GetService(typeof(IAgentModelSelection)) as IAgentModelSelection;
        if (selection is null)
            return YunxiaoWorkbenchResult<PrdSplitPreviewResult>.Failure(503, "模型能力未就绪：请先在设置里完成模型配置");

        var model = await selection
            .ResolveQualifiedModelNameAsync(null, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(model))
            return YunxiaoWorkbenchResult<PrdSplitPreviewResult>.Failure(503, "尚未选择模型：请先在设置里选定模型");

        var router = _services.GetService(typeof(IProviderRouter)) as IProviderRouter;
        if (router is null)
            return YunxiaoWorkbenchResult<PrdSplitPreviewResult>.Failure(503, "未配置模型供应商：请先在设置里填写模型供应商与 API Key");

        IChatClient client;
        try
        {
            client = router.CreateChatClient(model);
        }
        catch (InvalidOperationException)
        {
            return YunxiaoWorkbenchResult<PrdSplitPreviewResult>.Failure(503, "当前模型不可用：请检查供应商配置后重试");
        }

        var prompt = PrdSplitPlanner.BuildUserPrompt(source, documents);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SplitTimeout);

        ChatResponse? response;
        try
        {
            response = await client
                .GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, PrdSplitPlanner.SystemInstruction),
                        new ChatMessage(ChatRole.User, prompt)
                    ],
                    new ChatOptions
                    {
                        Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
                        // 拆分结果可能包含数十条需求与上百条任务，供应商默认输出上限（常见 1024~2048）会被截断
                        MaxOutputTokens = 32000
                    },
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return YunxiaoWorkbenchResult<PrdSplitPreviewResult>.Failure(504, "拆分超时，请稍后重试");
        }
        catch (Exception ex)
        {
            return YunxiaoWorkbenchResult<PrdSplitPreviewResult>.Failure(502, $"模型调用失败：{ex.Message}");
        }

        var (plan, error) = PrdSplitPlanner.Parse(response?.Text);
        if (plan is null)
            return YunxiaoWorkbenchResult<PrdSplitPreviewResult>.Failure(502, $"模型输出无法解析：{error}");

        if (!string.IsNullOrWhiteSpace(request.IterationName) && plan.Iteration is not null)
            plan.Iteration.Name = request.IterationName.Trim();

        var warnings = new List<string>();
        warnings.AddRange(source.Warnings);
        warnings.AddRange(plan.Warnings);

        return YunxiaoWorkbenchResult<PrdSplitPreviewResult>.Success(new PrdSplitPreviewResult
        {
            Plan = plan,
            SyncStatus = source.Status,
            RequirementCount = source.Requirements.Count,
            DocumentNames = documents.Select(d => d.Name).ToList(),
            Warnings = warnings
        });
    }
}
