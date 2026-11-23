/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services
*文件名： WebYunxiaoService
*描述：云效工作台 REST 薄壳：守卫 + payload 解析 + ApiEnvelope 映射（业务全在 YunxiaoWorkbenchService / YunxiaoInitService）
*
*****************************************************************************/

using LubanAgentWebHost.Services.TaskDetail;

namespace LubanAgentWebHost.Services;

/// <summary>
/// 云效工作台 REST 薄壳（照抄 <see cref="WebPluginService"/> 惯例）。
/// </summary>
/// <remarks>
/// <b>壳层不写业务逻辑</b>：每方法 <see cref="WebApi.RunGuardedAsync"/> 兜底，
/// POST 走 <see cref="WebApi.TryReadPayload{T}"/>、GET 用 <see cref="WebApi.Query"/>/<see cref="WebApi.QueryInt"/>，
/// 经 <c>WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId)</c> 解析
/// <see cref="YunxiaoWorkbenchService"/> 后转调；
/// <see cref="YunxiaoWorkbenchResult{T}"/> → ApiEnvelope 的映射口径见该类型注释
/// （Ok → <c>WebApi.OkJson(Data)</c>，失败 → <c>WebApi.FailJson(Code, Message)</c>）。
/// </remarks>
public static class WebYunxiaoService
{
    /// <summary>读取连接设置（GET /yunxiao/settings）。</summary>
    /// <returns><c>{ organizationId, hasPat }</c>（绝不含 PAT 明文）。</returns>
    public static Task<ActionResult> GetSettingsAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoWorkbenchService>();

        var result = await service.GetSettingsAsync().ConfigureAwait(false);
        return ToEnvelope(result);
    });

    /// <summary>测试连接（POST /yunxiao/testconnection，只读探活）。</summary>
    /// <param name="payload">包装后的 <see cref="YunxiaoTestConnectionRequest"/> JSON。</param>
    /// <returns><c>{ ok, projectCount, message? }</c>。</returns>
    public static Task<ActionResult> TestConnectionAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<YunxiaoTestConnectionRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoWorkbenchService>();

        var result = await service
            .TestConnectionAsync(input.OrganizationId, input.Pat)
            .ConfigureAwait(false);
        return ToEnvelope(result);
    });

    /// <summary>保存连接设置（POST /yunxiao/savesettings，服务端先探活后落盘）。</summary>
    /// <param name="payload">包装后的 <see cref="YunxiaoSaveSettingsRequest"/> JSON。</param>
    /// <returns><c>{ saved: true }</c>。</returns>
    public static Task<ActionResult> SaveSettingsAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<YunxiaoSaveSettingsRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoWorkbenchService>();

        var result = await service
            .SaveSettingsAsync(input.OrganizationAddress, input.OrganizationId, input.Pat)
            .ConfigureAwait(false);
        return ToEnvelope(result);
    });

    /// <summary>项目列表（GET /yunxiao/projects）。</summary>
    /// <returns><c>{ items, total, page, perPage }</c>。</returns>
    public static Task<ActionResult> ListProjectsAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoWorkbenchService>();

        var result = await service
            .ListProjectsAsync(WebApi.QueryInt("page"), WebApi.QueryInt("perPage"), WebApi.Query("keyword"))
            .ConfigureAwait(false);
        return ToEnvelope(result);
    });

    // ═══════════════════════════════════════════════════════════════════════
    // S2（批次 D）：项目初始化 + FR-3 流水线（业务全在 YunxiaoInitService）
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>项目初始化清单（GET /yunxiao/manifests）。</summary>
    /// <returns><c>{ repos, groups, workspaces, sessionReady }</c>（服务直回结果对象，无 result 包装）。</returns>
    public static Task<ActionResult> ManifestsAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoInitService>();

        var result = await service.GetManifestsAsync(CancellationToken.None).ConfigureAwait(false);
        return WebApi.OkJson(result);
    });

    /// <summary>初始化预检（POST /yunxiao/preflight，<b>零副作用</b>）。</summary>
    /// <param name="payload">包装后的 <see cref="PreflightRequest"/> JSON。</param>
    /// <returns>预检结论（命名派生 / git 命令序列 / 冲突清单 / 会话可用性）。</returns>
    public static Task<ActionResult> PreflightAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<PreflightRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoInitService>();

        var result = await service.PreflightAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>
    /// 流水线命名派生（GET /yunxiao/pipelinederive，S2e §14 修复一，零副作用轻量反查+派生）。
    /// </summary>
    /// <remarks>
    /// 参数走查询串（风格照 <c>InitTask</c>）：<c>templateRepoName</c> / <c>newCode</c> 必填，
    /// <c>ends</c> 可选（<see cref="InitEndConfig"/> 数组 JSON，缺省 = 三端全开；backend 的 port 参与派生）。
    /// </remarks>
    /// <returns><c>{ items: [{ end, from, to }] }</c>（from 反查不到为 null；to 永不带前导下划线）。</returns>
    public static Task<ActionResult> PipelineDeriveAsync() => WebApi.RunGuardedAsync(async () =>
    {
        IReadOnlyList<InitEndConfig>? ends = null;
        var endsJson = WebApi.Query("ends");
        if (!string.IsNullOrWhiteSpace(endsJson))
        {
            if (!WebApi.TryReadPayload<List<InitEndConfig>>(endsJson, out var parsed) || parsed is null)
            {
                return WebApi.FailJson(400, "ends 不是合法的 JSON");
            }

            ends = parsed;
        }

        var input = new DeriveRequest(
            WebApi.Query("templateRepoName") ?? string.Empty,
            WebApi.Query("newCode") ?? string.Empty,
            ends);

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoInitService>();

        var result = await service.DerivePipelinesAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>执行初始化（POST /yunxiao/initexec，秒回 taskId，后台逐库推进）。</summary>
    /// <param name="payload">包装后的 <see cref="ExecRequest"/> JSON。</param>
    /// <returns><c>{ taskId }</c>（400 参数 / 409 同代号互斥 / 503 会话不可用）。</returns>
    public static Task<ActionResult> InitExecAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<ExecRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoInitService>();

        var result = await service.ExecAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>任务态查询（GET /yunxiao/inittask；前端 2s 轮询）。</summary>
    /// <param name="payload">GET 无请求体；本参数仅保持壳层签名统一，taskId 取查询串 <c>taskId</c>。</param>
    /// <returns><c>{ id, codename, status, libs, … }</c>；404 = 任务不存在。</returns>
    public static Task<ActionResult> InitTaskAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoInitService>();

        var result = await service
            .GetTaskAsync(WebApi.Query("taskId") ?? string.Empty, CancellationToken.None)
            .ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>重跑指定失败库（POST /yunxiao/initretry，幂等复用同任务）。</summary>
    /// <param name="payload">包装后的 <see cref="YunxiaoRetryRequest"/> JSON。</param>
    /// <returns><c>{ taskId }</c>（404 任务/库不存在；409 执行上下文失效）。</returns>
    public static Task<ActionResult> InitRetryAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<YunxiaoRetryRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoInitService>();

        var result = await service
            .RetryAsync(input.TaskId, input.RepoNames, CancellationToken.None)
            .ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>流水线预览（POST /yunxiao/pipelinepreview，逐端 YAML diff）。</summary>
    /// <param name="payload">包装后的 <see cref="PipelinePreviewRequest"/> JSON。</param>
    /// <returns><c>{ items: [{ end, name, diff, warning? }] }</c>。</returns>
    public static Task<ActionResult> PipelinePreviewAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<PipelinePreviewRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoInitService>();

        var result = await service.PreviewPipelinesAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>流水线提交（POST /yunxiao/pipelinecommit；update 覆盖须 <c>confirm:true</c>，缺失 → 409）。</summary>
    /// <param name="payload">包装后的 <see cref="PipelineCommitRequest"/> JSON。</param>
    /// <returns><c>{ results: [{ end, ok, pipelineId?, message? }] }</c>（逐端独立）。</returns>
    public static Task<ActionResult> PipelineCommitAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<PipelineCommitRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoInitService>();

        var result = await service.CommitPipelinesAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>
    /// S2 收口 · 项目初始化完成登记：代码库任务 done 且三端流水线就绪后落盘，
    /// 供 <c>/yunxiao/projects</c> 判定并标注「已初始化」。
    /// </summary>
    /// <param name="payload">POST 原始请求体（SAEA 字符串 payload，内层 JSON 为 <see cref="ProjectInitRequest"/>）。</param>
    /// <returns>成功 <c>{ ok:true, data:true }</c>；缺键 400。</returns>
    public static Task<ActionResult> ProjectInitAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<ProjectInitRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoInitService>();

        var result = await service.SaveProjectInitAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>
    /// S2 收口 · 反查项目初始化完成登记：已初始化项目再次进入初始化页时反显上次配置。
    /// </summary>
    /// <param name="payload">POST 原始请求体（内层 JSON 为 <see cref="ProjectInitGetRequest"/>）。</param>
    /// <returns><c>{ ok:true, data:{...}|null }</c>；缺键 400。</returns>
    public static Task<ActionResult> ProjectInitGetAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<ProjectInitGetRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<YunxiaoInitService>();

        var result = await service.GetProjectInitAsync(input.ProjectId, input.Identifier, CancellationToken.None)
            .ConfigureAwait(false);
        return ToResult(result);
    });

    // ═══════════════════════════════════════════════════════════════════════
    // S3：PRD 同步 + 拆分落库（业务在 Services/Prd 下）
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>PRD 来源信息（GET /yunxiao/prdsource；查询串 <c>projectId</c>）。</summary>
    /// <returns><c>{ projectId, projectName, testRepos, hasTestRepo, message? }</c>。</returns>
    public static Task<ActionResult> PrdSourceAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<PrdSyncService>();

        var result = await service
            .GetSourceAsync(WebApi.Query("projectId"), CancellationToken.None)
            .ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>PRD 同步（POST /yunxiao/prdsync）。</summary>
    /// <param name="payload">包装后的 <see cref="PrdSyncRequest"/> JSON。</param>
    /// <returns>同步结果（<c>status=failed</c> 仍走成功信封，便于页面回退到本地上传）。</returns>
    public static Task<ActionResult> PrdSyncAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<PrdSyncRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<PrdSyncService>();

        var result = await service.SyncAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>拆分预览（POST /yunxiao/prdsplitpreview；一次性调用模型）。</summary>
    /// <param name="payload">包装后的 <see cref="PrdSplitRequest"/> JSON。</param>
    /// <returns><c>{ plan, syncStatus, requirementCount, documentNames, warnings }</c>。</returns>
    public static Task<ActionResult> PrdSplitPreviewAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<PrdSplitRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<PrdSplitService>();

        var result = await service.PreviewAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>拆分提交（POST /yunxiao/prdsplitsubmit；逐项落库并用幂等账本跳过已成功项）。</summary>
    /// <param name="payload">包装后的 <see cref="PrdSubmitRequest"/> JSON。</param>
    /// <returns><c>{ sprintId, receipts, okCount, failedCount, warnings }</c>。</returns>
    public static Task<ActionResult> PrdSplitSubmitAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<PrdSubmitRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<PrdSubmitService>();

        var result = await service.SubmitAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    // ═══════════════════════════════════════════════════════════════════════
    // S3：迭代测试 F9（按来源生成用例 + 用例/测试计划落库）
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>用例生成（POST /yunxiao/iterationtestpreview；一次性调用模型）。</summary>
    /// <param name="payload">包装后的 <see cref="IterationTestRequest"/> JSON。</param>
    /// <returns><c>{ cases, mergedCount, sourceRequirementCount, warnings }</c>。</returns>
    public static Task<ActionResult> IterationTestPreviewAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<IterationTestRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<IterationTestService>();

        var result = await service.GenerateAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>用例与测试计划落库（POST /yunxiao/iterationtestsave；幂等重跑）。</summary>
    /// <param name="payload">包装后的 <see cref="IterationTestSaveRequest"/> JSON。</param>
    /// <returns><c>{ receipts, okCount, failedCount, testPlanId, warnings }</c>。</returns>
    public static Task<ActionResult> IterationTestSaveAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<IterationTestSaveRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<IterationTestService>();

        var result = await service.SaveAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    // ═══════════════════════════════════════════════════════════════════════
    // S4：迭代任务看板（F1，只读）
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>迭代任务看板（GET /yunxiao/board?projectId=&amp;sprintId=）。</summary>
    /// <returns><c>{ projectName, sprints, columns, totalTasks, doneCount, overdueCount, … }</c>。</returns>
    public static Task<ActionResult> BoardAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<BoardService>();

        var query = new BoardQuery
        {
            ProjectId = WebApi.Query("projectId"),
            SprintId = WebApi.Query("sprintId")
        };

        var result = await service.GetBoardAsync(query, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>任务详情（GET /yunxiao/taskdetail?projectId=&amp;workitemId=）。</summary>
    public static Task<ActionResult> TaskDetailAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<TaskDetailService>();
        var query = new TaskDetailQuery
        {
            ProjectId = WebApi.Query("projectId"),
            WorkitemId = WebApi.Query("workitemId")
        };
        var result = await service.GetDetailAsync(query, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>创建任务评论（POST /yunxiao/taskcomment）。</summary>
    public static Task<ActionResult> TaskCommentAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<TaskCommentRequest>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<TaskDetailService>();
        var result = await service.CreateCommentAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>读取任务会话绑定（GET /yunxiao/tasksession?workitemId=）。</summary>
    public static Task<ActionResult> TaskSessionAsync() => WebApi.RunGuardedAsync(async () =>
    {
        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<TaskSessionService>();
        var result = await service.GetAsync(WebApi.Query("workitemId"), CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>保存任务会话绑定（POST /yunxiao/tasksessionsave）。</summary>
    public static Task<ActionResult> TaskSessionSaveAsync(string payload) => WebApi.RunGuardedAsync(async () =>
    {
        if (!WebApi.TryReadPayload<TaskSession>(payload, out var input) || input is null)
        {
            return WebApi.FailJson(400, "请求体不是合法的 JSON");
        }

        await using var scope = WebApi.Runtime.CreateRequestScope(WebApi.Runtime.UserId);
        var service = scope.GetRequiredService<TaskSessionService>();
        var result = await service.SaveAsync(input, CancellationToken.None).ConfigureAwait(false);
        return ToResult(result);
    });

    /// <summary>
    /// S2 结果 → 响应壳（机械映射）：定码走 <see cref="MapError"/>，成功 <c>OkJson(Data)</c>、失败 <c>FailJson</c>。
    /// </summary>
    private static ActionResult ToResult<T>(YunxiaoWorkbenchResult<T> result)
    {
        var envelope = MapError(result);
        return envelope.Success
            ? WebApi.OkJson(result.Data)
            : WebApi.FailJson(envelope.Code, envelope.Message ?? "服务端处理失败");
    }

    /// <summary>
    /// 业务结果 → <see cref="ApiEnvelope"/> 错误码映射（纯函数，壳与单测共用唯一口径）：
    /// <see cref="YunxiaoWorkbenchResult{T}.Ok"/> → <c>ApiEnvelope.Ok(Data)</c>（200，数据透传）；
    /// 失败 → <c>ApiEnvelope.Fail(Code &gt; 0 ? Code : 400, Message)</c>（400/404/409/503 全走此径）。
    /// </summary>
    /// <typeparam name="T">成功载荷类型。</typeparam>
    /// <param name="result">业务结果。</param>
    /// <returns>响应壳。</returns>
    internal static ApiEnvelope MapError<T>(YunxiaoWorkbenchResult<T> result)
        => result.Ok
            ? ApiEnvelope.Ok(result.Data)
            : ApiEnvelope.Fail(result.Code > 0 ? result.Code : 400, result.Message ?? "服务端处理失败");

    /// <summary>
    /// 业务结果 → 响应壳（机械映射，见 <see cref="YunxiaoWorkbenchResult{T}"/> 注释）。
    /// </summary>
    private static ActionResult ToEnvelope<T>(YunxiaoWorkbenchResult<T> result)
        => result.Ok
            ? WebApi.OkJson(result.Data)
            : WebApi.FailJson(result.Code, result.Message ?? "服务端处理失败");
}
