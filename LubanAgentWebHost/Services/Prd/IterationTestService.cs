/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.Prd
*文件名： IterationTestService
*描述：迭代测试（F9）：按来源生成用例、重复合并、用例与测试计划落库
*
*****************************************************************************/

using System.Text;
using System.Text.Json;
using LubanAgentCore.Connectors.AliyunDevOps;

namespace LubanAgentWebHost.Services.Prd;

/// <summary>
/// 迭代测试服务（F9）。
/// </summary>
/// <remarks>
/// <para>来源多选：<c>needs</c>（需求标题 + 描述 + 验收）、<c>prd</c>（需求正文）、<c>tasks</c>（任务标题）、<c>manual</c>（手工输入）。</para>
/// <para>生成后按标题归一化去重，重复用例合并来源并计数，供界面提示。</para>
/// <para>保存复用 PRD 拆分同一套幂等账本，重复执行只补失败项。</para>
/// </remarks>
public sealed class IterationTestService
{
    private static readonly TimeSpan GenerateTimeout = TimeSpan.FromSeconds(300);

    private const int MaxCases = 200;
    private const int MaxPromptChars = 40_000;

    private static readonly JsonSerializerOptions ParseOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly PrdSyncService _sync;
    private readonly IProjexClient _projex;
    private readonly IYunxiaoSettingsService _settings;
    private readonly PrdReceiptStore _store;
    private readonly IServiceProvider _services;

    /// <summary>
    /// 创建迭代测试服务。
    /// </summary>
    /// <param name="sync">同步服务（取需求 / 任务来源）。</param>
    /// <param name="projex">云效研发项目域客户端。</param>
    /// <param name="settings">云效设置（取组织 ID）。</param>
    /// <param name="store">幂等回执账本。</param>
    /// <param name="services">作用域容器（解析模型选择与供应商路由）。</param>
    public IterationTestService(
        PrdSyncService sync,
        IProjexClient projex,
        IYunxiaoSettingsService settings,
        PrdReceiptStore store,
        IServiceProvider services)
    {
        _sync = sync;
        _projex = projex;
        _settings = settings;
        _store = store;
        _services = services;
    }

    /// <summary>
    /// 按来源生成用例（一次性非流式模型调用）。
    /// </summary>
    /// <param name="request">生成请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>去重合并后的用例；来源为空 / 模型未配置时返回失败信封。</returns>
    public async Task<YunxiaoWorkbenchResult<IterationTestPreviewResult>> GenerateAsync(
        IterationTestRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(400, "项目 ID 不能为空");

        var sources = NormalizeSources(request.Sources);
        if (sources.Count == 0)
            return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(400, "请至少选择一个用例来源");

        if (sources.SequenceEqual(["manual"]) && string.IsNullOrWhiteSpace(request.ManualContent))
            return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(400, "已选「手工输入」但正文为空");

        PrdSyncResult? sync = null;
        if (sources.Any(s => s is "needs" or "prd" or "tasks"))
        {
            var synced = await _sync
                .SyncAsync(new PrdSyncRequest(request.ProjectId.Trim(), request.SprintId), cancellationToken)
                .ConfigureAwait(false);
            if (!synced.Ok)
                return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(synced.Code, synced.Message ?? "读取云效需求失败");

            sync = synced.Data!;

            if (request.RequirementKeys is { Count: > 0 })
            {
                var keys = request.RequirementKeys
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                sync.Requirements = sync.Requirements
                    .Where(r => r.SerialNumber is not null && keys.Contains(r.SerialNumber))
                    .ToList();
            }
        }

        var prompt = BuildPrompt(sources, sync, request.ManualContent);
        if (string.IsNullOrWhiteSpace(prompt))
            return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(400, "所选来源没有可用的内容");

        var selection = _services.GetService(typeof(IAgentModelSelection)) as IAgentModelSelection;
        if (selection is null)
            return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(503, "模型能力未就绪：请先在设置里完成模型配置");

        var model = await selection.ResolveQualifiedModelNameAsync(null, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(model))
            return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(503, "尚未选择模型：请先在设置里选定模型");

        var router = _services.GetService(typeof(IProviderRouter)) as IProviderRouter;
        if (router is null)
            return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(503, "未配置模型供应商：请先在设置里填写模型供应商与 API Key");

        IChatClient client;
        try
        {
            client = router.CreateChatClient(model);
        }
        catch (InvalidOperationException)
        {
            return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(503, "当前模型不可用：请检查供应商配置后重试");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GenerateTimeout);

        ChatResponse? response;
        try
        {
            response = await client
                .GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, SystemInstruction),
                        new ChatMessage(ChatRole.User, prompt)
                    ],
                    new ChatOptions
                    {
                        Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
                        MaxOutputTokens = 32000
                    },
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(504, "用例生成超时，请稍后重试");
        }
        catch (Exception ex)
        {
            return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(502, $"模型调用失败：{ex.Message}");
        }

        var (cases, error) = ParseCases(response?.Text);
        if (cases is null)
            return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Failure(502, $"模型输出无法解析：{error}");

        var (merged, mergedCount) = MergeDuplicates(cases);
        var warnings = new List<string>();
        if (sync is not null)
            warnings.AddRange(sync.Warnings);
        if (mergedCount > 0)
            warnings.Add($"已合并 {mergedCount} 条重复用例（保留全部来源）");

        return YunxiaoWorkbenchResult<IterationTestPreviewResult>.Success(new IterationTestPreviewResult
        {
            Cases = merged,
            MergedCount = mergedCount,
            SourceRequirementCount = sync?.Requirements.Count ?? 0,
            Warnings = warnings
        });
    }

    /// <summary>
    /// 保存用例与测试计划（逐项落库，幂等重跑）。
    /// </summary>
    /// <param name="request">保存请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>逐项回执；部分失败仍走成功信封。</returns>
    public async Task<YunxiaoWorkbenchResult<IterationTestSaveResult>> SaveAsync(
        IterationTestSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            return YunxiaoWorkbenchResult<IterationTestSaveResult>.Failure(400, "项目 ID 不能为空");

        var cases = (request.Cases ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Title))
            .ToList();
        if (cases.Count == 0 && string.IsNullOrWhiteSpace(request.TestPlan?.Name))
            return YunxiaoWorkbenchResult<IterationTestSaveResult>.Failure(400, "没有可保存的用例或测试计划");

        var orgId = _settings.GetOrganizationId();
        if (string.IsNullOrWhiteSpace(orgId))
            return YunxiaoWorkbenchResult<IterationTestSaveResult>.Failure(400, "未配置组织 ID");

        var projectId = request.ProjectId.Trim();
        var result = new IterationTestSaveResult();
        var ledger = await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false);
        var receipts = new List<PrdSubmitReceipt>();

        string? sprintId = string.IsNullOrWhiteSpace(request.SprintId) ? ledger.SprintId : request.SprintId.Trim();

        var executor = request.TestPlan?.Executor;
        if (string.IsNullOrWhiteSpace(executor))
        {
            try
            {
                executor = (await _projex.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false))?.Id;
            }
            catch (Exception ex)
            {
                receipts.Add(new PrdSubmitReceipt
                {
                    Kind = "testcase",
                    Key = "-",
                    Title = "读取当前用户",
                    Ok = false,
                    Message = ex.Message
                });
            }
        }

        // ── 用例 ────────────────────────────────────────────────────────────
        var caseIds = new List<string>();
        if (cases.Count > 0)
        {
            string? testRepoId = null;
            string? directoryId = null;
            string? repoError = null;
            try
            {
                var repos = await _projex.ListTestReposAsync(orgId, 1, 100, null, cancellationToken).ConfigureAwait(false);
                testRepoId = repos.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.TestRepoIdentifier))?.TestRepoIdentifier;
                if (string.IsNullOrWhiteSpace(testRepoId))
                {
                    repoError = "云效没有可用的测试用例库";
                }
                else
                {
                    var dirs = await _projex.ListTestRepoDirectoriesAsync(orgId, testRepoId, 1, 100, cancellationToken).ConfigureAwait(false);
                    directoryId = dirs.FirstOrDefault(d => string.IsNullOrWhiteSpace(d.ParentId))?.Id;
                    if (string.IsNullOrWhiteSpace(directoryId))
                        repoError = "测试用例库没有可用的根目录";
                }
            }
            catch (Exception ex)
            {
                repoError = $"读取测试用例库/目录失败：{ex.Message}";
            }

            foreach (var item in cases)
            {
                var title = item.Title!.Trim();
                var key = PrdSubmitService.MakeKey("testcase", item.Key ?? title, title);

                if (TryReuse(ledger, key, out var reusedId))
                {
                    caseIds.Add(reusedId!);
                    receipts.Add(new PrdSubmitReceipt
                    {
                        Kind = "testcase",
                        Key = key,
                        Title = title,
                        Ok = true,
                        RemoteId = reusedId,
                        Reused = true,
                        SourceType = "iteration",
                        SourceParagraph = item.SourceParagraph
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(testRepoId) || string.IsNullOrWhiteSpace(directoryId))
                {
                    receipts.Add(FailedReceipt("testcase", key, title, repoError ?? "缺少测试用例库", item.SourceParagraph));
                    continue;
                }

                try
                {
                    var id = await _projex.CreateTestcaseAsync(
                        orgId,
                        new YunxiaoCreateTestcaseRequest
                        {
                            TestRepoId = testRepoId!,
                            DirectoryId = directoryId!,
                            Subject = title,
                            AssignedTo = NullIfBlank(executor),
                            PreCondition = NullIfBlank(item.PreCondition)
                        },
                        cancellationToken).ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        receipts.Add(FailedReceipt("testcase", key, title, "创建用例失败（响应缺 id）", item.SourceParagraph));
                        continue;
                    }

                    caseIds.Add(id!);
                    receipts.Add(new PrdSubmitReceipt
                    {
                        Kind = "testcase",
                        Key = key,
                        Title = title,
                        Ok = true,
                        RemoteId = id,
                        SourceType = "iteration",
                        SourceParagraph = item.SourceParagraph
                    });
                }
                catch (Exception ex)
                {
                    receipts.Add(FailedReceipt("testcase", key, title, ex.Message, item.SourceParagraph));
                }
            }
        }

        // ── 测试计划 ────────────────────────────────────────────────────────
        if (request.TestPlan is { Name: not null } plan && !string.IsNullOrWhiteSpace(plan.Name))
        {
            var name = plan.Name.Trim();
            var key = PrdSubmitService.MakeKey("testplan", name, plan.Environment);

            if (TryReuse(ledger, key, out var reusedId))
            {
                result.TestPlanId = reusedId;
                receipts.Add(new PrdSubmitReceipt
                {
                    Kind = "testplan",
                    Key = key,
                    Title = name,
                    Ok = true,
                    RemoteId = reusedId,
                    Reused = true
                });
            }
            else
            {
                try
                {
                    var id = await _projex.CreateTestPlanAsync(
                        orgId,
                        new YunxiaoCreateTestPlanRequest
                        {
                            Name = name,
                            ProjectId = projectId,
                            SprintId = sprintId,
                            StartDate = NullIfBlank(plan.StartDate),
                            EndDate = NullIfBlank(plan.EndDate),
                            Managers = string.IsNullOrWhiteSpace(executor) ? null : [executor!],
                            CaseIds = caseIds.Count > 0 ? caseIds : null
                        },
                        cancellationToken).ConfigureAwait(false);

                    result.TestPlanId = id;
                    receipts.Add(id is null
                        ? FailedReceipt("testplan", key, name, "创建测试计划失败（响应缺标识）", null)
                        : new PrdSubmitReceipt
                        {
                            Kind = "testplan",
                            Key = key,
                            Title = name,
                            Ok = true,
                            RemoteId = id
                        });
                }
                catch (Exception ex)
                {
                    receipts.Add(FailedReceipt("testplan", key, name, ex.Message, null));
                }
            }
        }

        result.Receipts = receipts;
        result.OkCount = receipts.Count(r => r.Ok);
        result.FailedCount = receipts.Count(r => !r.Ok);

        await PersistLedgerAsync(ledger, projectId, sprintId, receipts, result).ConfigureAwait(false);
        return YunxiaoWorkbenchResult<IterationTestSaveResult>.Success(result);
    }

    /// <summary>系统指令：只输出 JSON 用例数组。</summary>
    public const string SystemInstruction = """
        你是测试用例设计助手。根据给定的来源内容，产出可直接落库到云效测试用例库的用例清单。

        只输出一个 JSON 对象，不要任何解释、不要 Markdown 代码块、不要前后缀。JSON 结构如下：
        {
          "testcases": [
            { "key": "C1", "title": "用例标题（不超过 200 字）", "preCondition": "前置条件", "steps": "1. 步骤\n2. 预期结果", "requirementKey": "来源需求编号，没有则留空", "sourceParagraph": "来源原文片段（≤100字）" }
          ]
        }

        必须遵守：
        1. 只依据给定来源设计用例，不要编造来源中没有的功能。
        2. 每条用例聚焦一个可验证点；标题要能区分不同用例。
        3. sourceParagraph 必须逐字摘录来源原文，不得改写。
        4. key 在同一数组内唯一，格式 C1/C2。
        5. 来源内容不足以设计用例时输出 {"testcases": []}。
        """;

    /// <summary>
    /// 装配生成提示词。
    /// </summary>
    internal static string BuildPrompt(IReadOnlyList<string> sources, PrdSyncResult? sync, string? manualContent)
    {
        var builder = new StringBuilder();

        if (sources.Contains("needs") && sync is not null)
        {
            builder.AppendLine("【来源：云效需求（标题 + 描述 + 验收）】");
            foreach (var item in sync.Requirements)
            {
                builder.AppendLine($"- {item.SerialNumber} {item.Title}");
                if (!string.IsNullOrWhiteSpace(item.Description))
                    builder.AppendLine($"  描述：{Truncate(item.Description!, 1000)}");
                if (!string.IsNullOrWhiteSpace(item.Acceptance))
                    builder.AppendLine($"  验收：{Truncate(item.Acceptance!, 500)}");
                if (builder.Length >= MaxPromptChars)
                    break;
            }
            builder.AppendLine();
        }

        if (sources.Contains("prd") && sync is not null)
        {
            builder.AppendLine("【来源：PRD 正文片段】");
            foreach (var item in sync.Requirements)
            {
                if (string.IsNullOrWhiteSpace(item.Description))
                    continue;
                builder.AppendLine($"- {item.Title}：{Truncate(item.Description!, 800)}");
                if (builder.Length >= MaxPromptChars)
                    break;
            }
            builder.AppendLine();
        }

        if (sources.Contains("tasks") && sync is not null)
        {
            builder.AppendLine("【来源：迭代任务】");
            foreach (var item in sync.Requirements.Take(200))
                builder.AppendLine($"- {item.Title}");
            builder.AppendLine();
        }

        if (sources.Contains("manual") && !string.IsNullOrWhiteSpace(manualContent))
        {
            builder.AppendLine("【来源：手工输入】");
            builder.AppendLine(Truncate(manualContent.Trim(), MaxPromptChars / 2));
            builder.AppendLine();
        }

        var text = builder.ToString().Trim();
        return text.Length == 0 ? string.Empty : Truncate(text, MaxPromptChars);
    }

    /// <summary>
    /// 解析模型输出（容忍 Markdown 围栏与裸数组）。
    /// </summary>
    internal static (List<IterationTestCaseItem>? Cases, string? Error) ParseCases(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, "模型输出为空");

        var body = text.Trim();
        var start = body.IndexOf('{');
        var end = body.LastIndexOf('}');
        if (start >= 0 && end > start)
            body = body[start..(end + 1)];

        try
        {
            var wrapper = JsonSerializer.Deserialize<CaseWrapper>(body, ParseOptions);
            var cases = wrapper?.Testcases;
            if (cases is null)
                return (null, "缺少 testcases 字段");

            var cleaned = cases
                .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Title))
                .Select(c =>
                {
                    c!.Title = c.Title!.Trim();
                    c.PreCondition = c.PreCondition?.Trim();
                    c.Steps = c.Steps?.Trim();
                    c.SourceParagraph = string.IsNullOrWhiteSpace(c.SourceParagraph)
                        ? null
                        : Truncate(c.SourceParagraph!.Trim(), 200);
                    return c;
                })
                .ToList();

            if (cleaned.Count > MaxCases)
                cleaned.RemoveRange(MaxCases, cleaned.Count - MaxCases);

            return (cleaned, null);
        }
        catch (JsonException ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// 按标题归一化去重，重复用例合并来源列表。
    /// </summary>
    /// <param name="cases">原始用例。</param>
    /// <returns>合并后的用例与被合并的条数。</returns>
    internal static (List<IterationTestCaseItem> Cases, int MergedCount) MergeDuplicates(IEnumerable<IterationTestCaseItem> cases)
    {
        var merged = new List<IterationTestCaseItem>();
        var index = new Dictionary<string, IterationTestCaseItem>(StringComparer.Ordinal);

        foreach (var item in cases)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Title))
                continue;

            var normalized = NormalizeTitle(item.Title);
            if (index.TryGetValue(normalized, out var existing))
            {
                foreach (var source in item.Sources)
                {
                    if (!existing.Sources.Contains(source))
                        existing.Sources.Add(source);
                }
                continue;
            }

            if (item.Sources.Count == 0 && !string.IsNullOrWhiteSpace(item.RequirementKey))
                item.Sources.Add(item.RequirementKey!);

            index[normalized] = item;
            merged.Add(item);
        }

        var mergedCount = cases.Count(c => c is not null && !string.IsNullOrWhiteSpace(c.Title)) - merged.Count;
        return (merged, Math.Max(mergedCount, 0));
    }

    /// <summary>
    /// 标题归一化：去空白与常见标点、统一小写，用于重复判定。
    /// </summary>
    internal static string NormalizeTitle(string title)
    {
        var builder = new StringBuilder(title.Length);
        foreach (var ch in title)
        {
            if (char.IsWhiteSpace(ch))
                continue;
            if (char.IsPunctuation(ch) || char.IsSymbol(ch))
                continue;
            builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }

    private static List<string> NormalizeSources(IEnumerable<string>? sources)
    {
        if (sources is null)
            return [];

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "needs", "prd", "tasks", "manual" };
        return sources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(allowed.Contains)
            .Distinct()
            .ToList();
    }

    private async Task PersistLedgerAsync(
        PrdReceiptLedger ledger,
        string projectId,
        string? sprintId,
        List<PrdSubmitReceipt> receipts,
        IterationTestSaveResult result)
    {
        try
        {
            var merged = ledger.Receipts
                .Concat(receipts)
                .GroupBy(r => r.Key, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(r => r.At).First())
                .OrderByDescending(r => r.At)
                .Take(500)
                .ToList();

            ledger.ProjectId = projectId;
            ledger.SprintId = sprintId;
            ledger.Receipts = merged;

            await _store.SaveAsync(ledger).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"回执账本落盘失败：{ex.Message}");
        }
    }

    private static bool TryReuse(PrdReceiptLedger ledger, string key, out string? remoteId)
    {
        var receipt = ledger.Receipts.FirstOrDefault(r => r.Key == key && r.Ok && !string.IsNullOrWhiteSpace(r.RemoteId));
        remoteId = receipt?.RemoteId;
        return remoteId is not null;
    }

    private static PrdSubmitReceipt FailedReceipt(string kind, string key, string title, string message, string? sourceParagraph) => new()
    {
        Kind = kind,
        Key = key,
        Title = title,
        Ok = false,
        Message = message,
        SourceType = "iteration",
        SourceParagraph = sourceParagraph
    };

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private sealed class CaseWrapper
    {
        public List<IterationTestCaseItem?>? Testcases { get; set; }
    }
}
