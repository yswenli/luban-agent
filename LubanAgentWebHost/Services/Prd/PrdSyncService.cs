/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.Prd
*文件名： PrdSyncService
*描述：PRD 来源同步（云效需求 + 验收标准 + 附件元数据）与来源信息读取
*
*****************************************************************************/

using LubanAgentCore.Connectors.AliyunDevOps;

namespace LubanAgentWebHost.Services.Prd;

/// <summary>
/// PRD 来源同步。
/// </summary>
/// <remarks>
/// <para>
/// 同步口径：按项目 <b>+ 可选迭代</b> 拉取需求（category=Req），逐条补详情取描述与「验收标准」，
/// 并汇总附件元数据。附件二进制不在本服务解析——音视频 / Office 等格式的文本抽取代价高且无真机验证，
/// 页面只展示附件份数与文件名，拆分时由模型依据需求正文与补充文档完成。
/// </para>
/// <para>
/// 失败策略：整体请求失败返回 <c>Status=failed</c> 而不是失败信封，页面据此提示
/// 「可上传本地 PRD 继续」；单条详情 / 附件失败只记告警并把状态降为 <c>partial</c>，不中断同步。
/// </para>
/// </remarks>
public sealed class PrdSyncService
{
    private const int PageSize = 200;
    private const int MaxPages = 3;

    /// <summary>逐条补详情 / 附件的上限（避免上百次串行请求拖垮首次同步）。</summary>
    private const int DetailLimit = 50;

    private readonly IProjexClient _projex;
    private readonly IYunxiaoSettingsService _settings;

    /// <summary>
    /// 创建同步服务。
    /// </summary>
    /// <param name="projex">云效研发项目域客户端。</param>
    /// <param name="settings">云效设置（取组织 ID）。</param>
    public PrdSyncService(IProjexClient projex, IYunxiaoSettingsService settings)
    {
        _projex = projex;
        _settings = settings;
    }

    /// <summary>
    /// 读取 PRD 来源信息（项目名 + 可用用例库）。
    /// </summary>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>来源信息；云效读取失败时带 <c>Message</c>。</returns>
    public async Task<YunxiaoWorkbenchResult<PrdSourceInfo>> GetSourceAsync(
        string? projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return YunxiaoWorkbenchResult<PrdSourceInfo>.Failure(400, "项目 ID 不能为空");

        var orgId = _settings.GetOrganizationId();
        if (string.IsNullOrWhiteSpace(orgId))
            return YunxiaoWorkbenchResult<PrdSourceInfo>.Failure(400, "未配置组织 ID");

        var info = new PrdSourceInfo { ProjectId = projectId.Trim() };

        try
        {
            var project = await _projex.GetProjectAsync(orgId, info.ProjectId, cancellationToken).ConfigureAwait(false);
            info.ProjectName = string.IsNullOrWhiteSpace(project?.Name) ? info.ProjectId : project!.Name!;
        }
        catch (Exception ex)
        {
            info.ProjectName = info.ProjectId;
            info.Message = $"读取项目名称失败：{ex.Message}";
        }

        try
        {
            var repos = await _projex.ListTestReposAsync(orgId, 1, 100, null, cancellationToken).ConfigureAwait(false);
            info.TestRepos = repos
                .Where(r => !string.IsNullOrWhiteSpace(r.TestRepoIdentifier))
                .Select(r => new PrdTestRepoRef
                {
                    Id = r.TestRepoIdentifier!,
                    Name = string.IsNullOrWhiteSpace(r.Name) ? r.TestRepoIdentifier! : r.Name!
                })
                .ToList();
            info.HasTestRepo = info.TestRepos.Count > 0;
        }
        catch (Exception ex)
        {
            info.HasTestRepo = false;
            info.Message = string.IsNullOrWhiteSpace(info.Message)
                ? $"读取测试用例库失败：{ex.Message}"
                : $"{info.Message}；读取测试用例库失败：{ex.Message}";
        }

        try
        {
            info.Statuses = await ListStatusOptionsAsync(orgId, info.ProjectId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            info.Message = string.IsNullOrWhiteSpace(info.Message)
                ? $"读取状态候选失败：{ex.Message}"
                : $"{info.Message}；读取状态候选失败：{ex.Message}";
        }

        return YunxiaoWorkbenchResult<PrdSourceInfo>.Success(info);
    }

    /// <summary>
    /// 从项目现有工作项反查可用状态并去重。
    /// 云效未暴露独立状态枚举接口：<c>projects/{id}/workitemTypes/{typeId}/workflows</c> 实测返回
    /// <c>400 get statusFlowList failed</c>，故以实例数据反查替代；未被任何工作项使用的状态取不到。
    /// </summary>
    /// <param name="orgId">组织 ID。</param>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>状态候选（按分类区分，同分类同 ID 去重）。</returns>
    internal async Task<List<PrdStatusOption>> ListStatusOptionsAsync(
        string orgId,
        string projectId,
        CancellationToken cancellationToken)
    {
        var options = new List<PrdStatusOption>();

        foreach (var category in new[] { "Req", "Task" })
        {
            try
            {
                var page = await _projex.SearchWorkitemsAsync(
                    orgId,
                    new YunxiaoWorkitemSearchCriteria
                    {
                        ProjectId = projectId,
                        Category = category,
                        Page = 1,
                        PerPage = 100
                    },
                    cancellationToken).ConfigureAwait(false);

                foreach (var item in page?.Items ?? [])
                {
                    var status = item.Status;
                    if (string.IsNullOrWhiteSpace(status?.Id) || string.IsNullOrWhiteSpace(status.Name))
                        continue;
                    if (options.Any(o => o.Category == category && o.Id == status.Id))
                        continue;

                    options.Add(new PrdStatusOption
                    {
                        Id = status.Id,
                        Name = status.Name,
                        Category = category,
                        NameEn = status.NameEn
                    });
                }
            }
            catch
            {
                // 状态候选只是辅助展示，单个分类失败不影响来源信息
            }
        }

        return options;
    }

    /// <summary>
    /// 同步需求清单（需求标题 / 描述 / 验收标准 / 附件元数据）。
    /// </summary>
    /// <param name="request">同步请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步结果；整体失败也走成功信封（<c>Data.Status=failed</c>）。</returns>
    public async Task<YunxiaoWorkbenchResult<PrdSyncResult>> SyncAsync(
        PrdSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            return YunxiaoWorkbenchResult<PrdSyncResult>.Failure(400, "项目 ID 不能为空");

        var orgId = _settings.GetOrganizationId();
        if (string.IsNullOrWhiteSpace(orgId))
            return YunxiaoWorkbenchResult<PrdSyncResult>.Failure(400, "未配置组织 ID");

        var result = new PrdSyncResult
        {
            ProjectId = request.ProjectId.Trim(),
            ProjectName = request.ProjectId.Trim(),
            SyncedAt = DateTimeOffset.Now
        };

        try
        {
            var project = await _projex.GetProjectAsync(orgId, result.ProjectId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(project?.Name))
                result.ProjectName = project!.Name!;
        }
        catch (Exception ex)
        {
            // 项目名只用于展示，失败不降级同步状态（避免响亮但无害的告警把结果判成 partial）
            result.Message = $"读取项目名称失败：{ex.Message}";
        }

        var items = new List<YunxiaoWorkitem>();
        try
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                var search = await _projex.SearchWorkitemsAsync(
                    orgId,
                    new YunxiaoWorkitemSearchCriteria
                    {
                        ProjectId = result.ProjectId,
                        Category = "Req",
                        SprintId = string.IsNullOrWhiteSpace(request.SprintId) ? null : request.SprintId.Trim(),
                        Page = page,
                        PerPage = PageSize
                    },
                    cancellationToken).ConfigureAwait(false);

                var pageItems = search?.Items ?? [];
                items.AddRange(pageItems);

                if (pageItems.Count == 0)
                    break;

                var total = search?.Total ?? 0;
                if (total > 0 && items.Count >= total)
                    break;

                if (pageItems.Count < PageSize)
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Status = "failed";
            result.Message = $"云效需求同步失败：{ex.Message}";
            return YunxiaoWorkbenchResult<PrdSyncResult>.Success(result);
        }

        if (items.Count == 0)
        {
            result.Status = "empty";
            return YunxiaoWorkbenchResult<PrdSyncResult>.Success(result);
        }

        var detailCount = 0;
        foreach (var item in items)
        {
            var entry = new PrdRequirementItem
            {
                WorkitemId = item.Id ?? string.Empty,
                SerialNumber = item.SerialNumber,
                Title = string.IsNullOrWhiteSpace(item.Subject) ? "(无标题)" : item.Subject!,
                Description = item.Description
            };

            if (detailCount < DetailLimit && !string.IsNullOrWhiteSpace(item.Id))
            {
                detailCount++;
                await FillDetailAsync(orgId, entry, result, cancellationToken).ConfigureAwait(false);
            }

            result.Requirements.Add(entry);
        }

        result.Status = result.Warnings.Count > 0 ? "partial" : "ready";
        return YunxiaoWorkbenchResult<PrdSyncResult>.Success(result);
    }

    /// <summary>
    /// 补齐单条需求的详情与附件元数据；失败只记告警。
    /// </summary>
    private async Task FillDetailAsync(
        string organizationId,
        PrdRequirementItem entry,
        PrdSyncResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _projex.GetWorkitemAsync(organizationId, entry.WorkitemId, cancellationToken).ConfigureAwait(false);
            if (detail is not null)
            {
                if (!string.IsNullOrWhiteSpace(detail.Description))
                    entry.Description = detail.Description;
                entry.Acceptance = ExtractAcceptance(detail.CustomFieldValues);
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"{entry.Title}：读取详情失败（{ex.Message}）");
        }

        try
        {
            var attachments = await _projex.ListWorkitemAttachmentsAsync(organizationId, entry.WorkitemId, cancellationToken).ConfigureAwait(false);
            foreach (var file in attachments)
            {
                result.Attachments.Add(new PrdAttachmentItem
                {
                    WorkitemId = entry.WorkitemId,
                    WorkitemTitle = entry.Title,
                    FileId = file.FileId,
                    FileName = file.FileName,
                    Suffix = file.Suffix,
                    Size = file.Size,
                    Url = file.Url
                });
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"{entry.Title}：读取附件失败（{ex.Message}）");
        }
    }

    /// <summary>
    /// 从自定义字段中提取「验收标准」（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="fields">工作项自定义字段值。</param>
    /// <returns>验收标准文本；无该字段或为空时返回 <c>null</c>。</returns>
    internal static string? ExtractAcceptance(IReadOnlyList<YunxiaoCustomFieldValue>? fields)
    {
        if (fields is null || fields.Count == 0)
            return null;

        var field = fields.FirstOrDefault(f =>
            !string.IsNullOrWhiteSpace(f.FieldName) &&
            f.FieldName!.Contains("验收", StringComparison.Ordinal));

        if (field?.Values is null || field.Values.Count == 0)
            return null;

        var texts = field.Values
            .Select(v => string.IsNullOrWhiteSpace(v.DisplayValue) ? v.Identifier : v.DisplayValue)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .ToList();

        return texts.Count == 0 ? null : string.Join("\n", texts);
    }
}
