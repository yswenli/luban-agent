/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.Prd
*文件名： PrdSubmitService
*描述：PRD 拆分提交：迭代 / 需求 / 任务 / 关系 / 测试用例 / 测试计划逐项落库并记回执
*
*****************************************************************************/

using System.Security.Cryptography;
using System.Text;
using LubanAgentCore.Connectors.AliyunDevOps;

namespace LubanAgentWebHost.Services.Prd;

/// <summary>
/// PRD 拆分提交服务。
/// </summary>
/// <remarks>
/// <para>
/// 提交是<b>逐项独立</b>的：任一项失败只记该条回执，不中断后续项；
/// 已成功项写入幂等账本，重跑时按「类型 + 键 + 内容指纹」复用，只重做失败项。
/// </para>
/// <para>
/// 测试用例依赖云效用例库（取第一个可用库）；测试计划接口字段未真机验证，
/// 失败时仅记回执并提示原因，不影响已创建的需求 / 任务 / 用例。
/// </para>
/// </remarks>
public sealed class PrdSubmitService
{
    private const int MaxTotalItems = 300;
    private const int LedgerReceiptLimit = 500;

    private readonly IProjexClient _projex;
    private readonly IYunxiaoSettingsService _settings;
    private readonly PrdReceiptStore _store;

    /// <summary>
    /// 创建提交服务。
    /// </summary>
    /// <param name="projex">云效研发项目域客户端。</param>
    /// <param name="settings">云效设置（取组织 ID）。</param>
    /// <param name="store">幂等回执账本。</param>
    public PrdSubmitService(IProjexClient projex, IYunxiaoSettingsService settings, PrdReceiptStore store)
    {
        _projex = projex;
        _settings = settings;
        _store = store;
    }

    /// <summary>
    /// 提交拆分计划。
    /// </summary>
    /// <param name="request">提交请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>逐项回执（部分失败仍返回成功信封，失败明细见 <see cref="PrdSubmitResult.Receipts"/>）。</returns>
    public async Task<YunxiaoWorkbenchResult<PrdSubmitResult>> SubmitAsync(
        PrdSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            return YunxiaoWorkbenchResult<PrdSubmitResult>.Failure(400, "项目 ID 不能为空");

        var plan = request.Plan;
        if (plan is null || plan.Requirements.Count == 0)
            return YunxiaoWorkbenchResult<PrdSubmitResult>.Failure(400, "拆分计划为空或没有需求");

        var total = plan.Requirements.Count + plan.Tasks.Count + plan.Testcases.Count;
        if (total > MaxTotalItems)
            return YunxiaoWorkbenchResult<PrdSubmitResult>.Failure(400, $"拆分项过多（{total} 项），单次提交上限 {MaxTotalItems} 项");

        var orgId = _settings.GetOrganizationId();
        if (string.IsNullOrWhiteSpace(orgId))
            return YunxiaoWorkbenchResult<PrdSubmitResult>.Failure(400, "未配置组织 ID");

        var projectId = request.ProjectId.Trim();
        var result = new PrdSubmitResult();

        string? assignedTo;
        if (!string.IsNullOrWhiteSpace(request.AssignedTo))
        {
            assignedTo = request.AssignedTo.Trim();
        }
        else
        {
            try
            {
                var user = await _projex.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
                assignedTo = user?.Id;
            }
            catch (Exception ex)
            {
                return YunxiaoWorkbenchResult<PrdSubmitResult>.Failure(502, $"读取云效当前用户失败：{ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(assignedTo))
            return YunxiaoWorkbenchResult<PrdSubmitResult>.Failure(400, "无法确定负责人：云效创建工作项要求 assignedTo，请先配置云效连接");

        var ledger = await _store.LoadAsync(projectId, cancellationToken).ConfigureAwait(false);
        var receipts = new List<PrdSubmitReceipt>();

        var sprintId = !string.IsNullOrWhiteSpace(request.SprintId)
            ? request.SprintId.Trim()
            : ledger.SprintId;

        if (string.IsNullOrWhiteSpace(sprintId) && plan.Iteration is { Name: not null } iteration && !string.IsNullOrWhiteSpace(iteration.Name))
        {
            var iterationKey = MakeKey("iteration", iteration.Name!, iteration.StartDate);
            if (TryReuse(ledger, iterationKey, out var reusedSprintId))
            {
                sprintId = reusedSprintId;
                receipts.Add(ReusedReceipt("iteration", iterationKey, iteration.Name!, reusedSprintId!));
            }
            else
            {
                try
                {
                    sprintId = await _projex.CreateSprintAsync(
                        orgId,
                        projectId,
                        iteration.Name!,
                        [assignedTo!],
                        NullIfBlank(iteration.StartDate),
                        NullIfBlank(iteration.EndDate),
                        NullIfBlank(iteration.Description),
                        null,
                        null,
                        cancellationToken).ConfigureAwait(false);

                    receipts.Add(sprintId is null
                        ? FailedReceipt("iteration", iterationKey, iteration.Name!, "创建迭代失败（响应缺 id）")
                        : OkReceipt("iteration", iterationKey, iteration.Name!, sprintId));
                }
                catch (Exception ex)
                {
                    receipts.Add(FailedReceipt("iteration", iterationKey, iteration.Name!, ex.Message));
                }
            }
        }

        result.SprintId = sprintId;

        var requirementTypeId = await FindTypeIdAsync(orgId, projectId, "Req", cancellationToken).ConfigureAwait(false);
        var taskTypeId = await FindTypeIdAsync(orgId, projectId, "Task", cancellationToken).ConfigureAwait(false);

        if (requirementTypeId is null)
            return YunxiaoWorkbenchResult<PrdSubmitResult>.Failure(502, "项目下没有可用的需求类型（workitemTypes category=Req）");
        if (plan.Tasks.Count > 0 && taskTypeId is null)
            return YunxiaoWorkbenchResult<PrdSubmitResult>.Failure(502, "项目下没有可用的任务类型（workitemTypes category=Task）");

        // ── 需求 ───────────────────────────────────────────────────────────
        var requirementIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requirementSources = new Dictionary<string, (string? Type, string? Paragraph)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Requirements)
        {
            var title = item.Title ?? "(无标题)";
            var key = MakeKey("requirement", item.Key ?? title, title);
            requirementSources[item.Key ?? title] = (item.Source, item.SourceParagraph);

            if (TryReuse(ledger, key, out var reusedId))
            {
                requirementIds[item.Key ?? title] = reusedId!;
                receipts.Add(ReusedReceipt("requirement", key, title, reusedId!, item.Source, item.SourceParagraph));
                continue;
            }

            try
            {
                var id = await _projex.CreateWorkitemAsync(
                    orgId,
                    new YunxiaoCreateWorkitemRequest
                    {
                        ProjectId = projectId,
                        Subject = title,
                        WorkitemTypeId = requirementTypeId,
                        AssignedTo = assignedTo!,
                        SprintId = sprintId,
                        Description = BuildDescription(item.Description, item.Acceptance),
                        FormatType = "MARKDOWN"
                    },
                    cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(id))
                {
                    receipts.Add(FailedReceipt("requirement", key, title, "创建工作项失败（响应缺 id）", item.Source, item.SourceParagraph));
                    continue;
                }

                requirementIds[item.Key ?? title] = id!;
                receipts.Add(OkReceipt("requirement", key, title, id, item.Source, item.SourceParagraph));
            }
            catch (Exception ex)
            {
                receipts.Add(FailedReceipt("requirement", key, title, ex.Message, item.Source, item.SourceParagraph));
            }
        }

        // ── 任务（挂父需求 + 建 SUB 关系）────────────────────────────────────
        foreach (var item in plan.Tasks)
        {
            var title = item.Title ?? "(无标题)";
            var requirementKey = item.RequirementKey ?? string.Empty;
            var (taskSource, taskParagraph) = InheritSource(requirementSources, requirementKey, null, item.SourceParagraph);

            if (!requirementIds.TryGetValue(requirementKey, out var parentId))
            {
                receipts.Add(FailedReceipt("task", MakeKey("task", item.Key ?? title, title), title,
                    $"关联需求（{requirementKey}）未创建，任务跳过", taskSource, taskParagraph));
                continue;
            }

            var key = MakeKey("task", item.Key ?? title, title);
            string? taskId;

            if (TryReuse(ledger, key, out var reusedId))
            {
                taskId = reusedId;
                receipts.Add(ReusedReceipt("task", key, title, reusedId!, taskSource, taskParagraph));
            }
            else
            {
                try
                {
                    taskId = await _projex.CreateWorkitemAsync(
                        orgId,
                        new YunxiaoCreateWorkitemRequest
                        {
                            ProjectId = projectId,
                            Subject = title,
                            WorkitemTypeId = taskTypeId!,
                            AssignedTo = assignedTo!,
                            SprintId = sprintId,
                            ParentId = parentId,
                            Description = BuildDescription(item.Description, null),
                            FormatType = "MARKDOWN"
                        },
                        cancellationToken).ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        receipts.Add(FailedReceipt("task", key, title, "创建任务失败（响应缺 id）", taskSource, taskParagraph));
                        continue;
                    }

                    receipts.Add(OkReceipt("task", key, title, taskId, taskSource, taskParagraph));
                }
                catch (Exception ex)
                {
                    receipts.Add(FailedReceipt("task", key, title, ex.Message, taskSource, taskParagraph));
                    continue;
                }
            }

            // 云效在 CreateWorkitemAsync 时传 ParentId 即自动建立父子关联，无需额外调关系接口
        }

        // ── 测试用例 ────────────────────────────────────────────────────────
        var testcaseIds = new List<string>();
        if (plan.Testcases.Count > 0)
        {
            string? testRepoId = null;
            string? directoryId = null;
            string? repoError = null;
            try
            {
                var repos = await _projex.ListTestReposAsync(orgId, 1, 100, null, cancellationToken).ConfigureAwait(false);
                testRepoId = repos.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.TestRepoIdentifier))?.TestRepoIdentifier;
                if (string.IsNullOrWhiteSpace(testRepoId))
                    repoError = "云效没有可用的测试用例库";
                else
                {
                    // 创建用例必填 directoryId，取根目录（parentId=null，通常名为「全部用例」）
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

            foreach (var item in plan.Testcases)
            {
                var title = item.Title ?? "(无标题)";
                var key = MakeKey("testcase", item.Key ?? title, title);
                var caseSource = InheritSource(requirementSources, item.RequirementKey, null, item.SourceParagraph);

                if (TryReuse(ledger, key, out var reusedId))
                {
                    testcaseIds.Add(reusedId!);
                    receipts.Add(ReusedReceipt("testcase", key, title, reusedId!, caseSource.Type, caseSource.Paragraph));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(testRepoId) || string.IsNullOrWhiteSpace(directoryId))
                {
                    receipts.Add(FailedReceipt("testcase", key, title, repoError ?? "缺少测试用例库", caseSource.Type, caseSource.Paragraph));
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
                            AssignedTo = assignedTo,
                            PreCondition = NullIfBlank(item.PreCondition)
                        },
                        cancellationToken).ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        receipts.Add(FailedReceipt("testcase", key, title, "创建用例失败（响应缺 id）", caseSource.Type, caseSource.Paragraph));
                        continue;
                    }

                    testcaseIds.Add(id!);
                    receipts.Add(OkReceipt("testcase", key, title, id, caseSource.Type, caseSource.Paragraph));
                }
                catch (Exception ex)
                {
                    receipts.Add(FailedReceipt("testcase", key, title, ex.Message, caseSource.Type, caseSource.Paragraph));
                }
            }
        }

        // ── 测试计划 ────────────────────────────────────────────────────────
        if (plan.TestPlan is { Name: not null } testPlan && !string.IsNullOrWhiteSpace(testPlan.Name))
        {
            var name = testPlan.Name!;
            var key = MakeKey("testplan", name, testPlan.Environment);

            if (TryReuse(ledger, key, out var reusedId))
            {
                receipts.Add(ReusedReceipt("testplan", key, name, reusedId!));
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
                            StartDate = NullIfBlank(plan.Iteration?.StartDate),
                            EndDate = NullIfBlank(plan.Iteration?.EndDate),
                            Managers = [assignedTo!],
                            CaseIds = testcaseIds.Count > 0 ? testcaseIds : null
                        },
                        cancellationToken).ConfigureAwait(false);

                    receipts.Add(id is null
                        ? FailedReceipt("testplan", key, name, "创建测试计划失败（响应缺标识）")
                        : OkReceipt("testplan", key, name, id));
                }
                catch (Exception ex)
                {
                    receipts.Add(FailedReceipt("testplan", key, name, ex.Message));
                }
            }
        }

        result.Receipts = receipts;
        result.OkCount = receipts.Count(r => r.Ok && r.Kind != "relation");
        result.FailedCount = receipts.Count(r => !r.Ok && r.Kind != "relation");

        await PersistLedgerAsync(ledger, projectId, sprintId, receipts, result).ConfigureAwait(false);
        return YunxiaoWorkbenchResult<PrdSubmitResult>.Success(result);
    }

    /// <summary>
    /// 合并回执到账本并落盘；落盘失败只记告警。
    /// </summary>
    private async Task PersistLedgerAsync(
        PrdReceiptLedger ledger,
        string projectId,
        string? sprintId,
        List<PrdSubmitReceipt> receipts,
        PrdSubmitResult result)
    {
        try
        {
            var merged = ledger.Receipts
                .Concat(receipts)
                .GroupBy(r => r.Key, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(r => r.At).First())
                .OrderByDescending(r => r.At)
                .Take(LedgerReceiptLimit)
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

    /// <summary>
    /// 依次尝试取默认 / 启用的工作项类型（纯函数供单测锁口径）。
    /// </summary>
    /// <param name="types">工作项类型列表。</param>
    /// <returns>类型 ID；无可用类型返回 <c>null</c>。</returns>
    internal static string? PickTypeId(IReadOnlyList<YunxiaoWorkitemType>? types)
    {
        if (types is null || types.Count == 0)
            return null;

        var enabled = types
            .Where(t => !string.IsNullOrWhiteSpace(t.Id) && t.Enable != false)
            .ToList();

        if (enabled.Count == 0)
            return null;

        return enabled.FirstOrDefault(t => t.DefaultType == true)?.Id
               ?? enabled.FirstOrDefault(t => t.SystemDefault == true)?.Id
               ?? enabled[0].Id;
    }

    /// <summary>
    /// 生成幂等键：类型 + 键 + 内容指纹（内容变化不复用旧对象）。
    /// </summary>
    /// <param name="kind">类型。</param>
    /// <param name="key">拆分键。</param>
    /// <param name="fingerprint">内容指纹来源。</param>
    /// <returns>幂等键。</returns>
    internal static string MakeKey(string kind, string key, string? fingerprint)
    {
        var hash = ShortHash(fingerprint ?? string.Empty);
        return $"{kind}:{key.Trim()}:{hash}";
    }

    /// <summary>
    /// 短哈希（SHA256 前 8 位十六进制，纯函数供单测锁口径）。
    /// </summary>
    /// <param name="value">输入。</param>
    /// <returns>8 位小写十六进制。</returns>
    internal static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    private async Task<string?> FindTypeIdAsync(
        string organizationId,
        string projectId,
        string category,
        CancellationToken cancellationToken)
    {
        try
        {
            var types = await _projex.ListWorkitemTypesAsync(organizationId, projectId, category, cancellationToken)
                .ConfigureAwait(false);
            return PickTypeId(types);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryReuse(PrdReceiptLedger ledger, string key, out string? remoteId)
    {
        var receipt = ledger.Receipts.FirstOrDefault(r => r.Key == key && r.Ok && !string.IsNullOrWhiteSpace(r.RemoteId));
        remoteId = receipt?.RemoteId;
        return remoteId is not null;
    }

    private static PrdSubmitReceipt OkReceipt(
        string kind,
        string key,
        string title,
        string? remoteId,
        string? sourceType = null,
        string? sourceParagraph = null) => new()
    {
        Kind = kind,
        Key = key,
        Title = title,
        Ok = true,
        RemoteId = remoteId,
        SourceType = sourceType,
        SourceParagraph = sourceParagraph
    };

    private static PrdSubmitReceipt ReusedReceipt(
        string kind,
        string key,
        string title,
        string remoteId,
        string? sourceType = null,
        string? sourceParagraph = null) => new()
    {
        Kind = kind,
        Key = key,
        Title = title,
        Ok = true,
        RemoteId = remoteId,
        Reused = true,
        SourceType = sourceType,
        SourceParagraph = sourceParagraph
    };

    private static PrdSubmitReceipt FailedReceipt(
        string kind,
        string key,
        string title,
        string message,
        string? sourceType = null,
        string? sourceParagraph = null) => new()
    {
        Kind = kind,
        Key = key,
        Title = title,
        Ok = false,
        Message = message,
        SourceType = sourceType,
        SourceParagraph = sourceParagraph
    };

    /// <summary>
    /// 拼接工作项描述（描述 + 验收标准）。
    /// </summary>
    private static string? BuildDescription(string? description, string? acceptance)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(description))
            parts.Add(description.Trim());
        if (!string.IsNullOrWhiteSpace(acceptance))
            parts.Add($"**验收标准**\n\n{acceptance.Trim()}");

        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>按需求键继承来源（任务 / 用例未单列时取父需求的来源类型与原文段落）。</summary>
    private static (string? Type, string? Paragraph) InheritSource(
        IReadOnlyDictionary<string, (string? Type, string? Paragraph)> parents,
        string? requirementKey,
        string? ownType,
        string? ownParagraph)
    {
        parents.TryGetValue(requirementKey ?? string.Empty, out var parent);
        return (
            string.IsNullOrWhiteSpace(ownType) ? parent.Type : ownType,
            string.IsNullOrWhiteSpace(ownParagraph) ? parent.Paragraph : ownParagraph);
    }
}
