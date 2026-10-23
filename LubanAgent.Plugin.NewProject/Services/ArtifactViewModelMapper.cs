/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： ArtifactViewModelMapper
*创建时间：2026/10/22
*描述：结论文件 artifacts[] → 面板视图模型的逐字段映射（设计 §6.1.1，C11′）
*****************************************************************************/

using System.Globalization;
using LubanAgent.Plugin.NewProject.Abstractions;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 把结论文件的 <c>artifacts[]</c> 映射为面板视图模型（<see cref="RepositoryPrecheck"/> / <see cref="PipelinePreview"/>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不新增专用段</b>：C11′ 定案——<c>artifacts[]</c> 既是产物账目（落进档案），也是面板视图模型的
/// <b>唯一来源</b>；面板不读 <c>extra</c> 的任意键，只读 <see cref="GroupArtifactExtra"/> /
/// <see cref="RepositoryArtifactExtra"/> / <see cref="PipelineArtifactExtra"/> 的具名属性。
/// </para>
/// <para>
/// <b>缺端 / 缺字段一律明确报错</b>，不静默留空：映射不进的地方写进 <see cref="RepositoryPrecheck.Errors"/>
/// 或 <see cref="PipelinePreviewMapResult.Errors"/>，由调用方记进操作记录并阻断推进。
/// </para>
/// </remarks>
public static class ArtifactViewModelMapper
{
    /// <summary>
    /// 由 Step 3 准备阶段结论映射出预检读数（面板确认界面直接消费）。
    /// </summary>
    /// <param name="report">已解析的准备阶段结论。</param>
    /// <param name="takenAt">结论文件修改时间（界面提示用）。</param>
    /// <param name="ends">本次要求的端（用于核对逐端完备性）。</param>
    /// <returns>预检读数（映射缺口写进 <see cref="RepositoryPrecheck.Errors"/>）。</returns>
    public static RepositoryPrecheck ToRepositoryPrecheck(
        StepReport report,
        DateTimeOffset takenAt,
        IReadOnlyList<ProjectEnd> ends)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(ends);

        var result = new RepositoryPrecheck { TakenAt = takenAt };

        var group = report.Artifacts.FirstOrDefault(one => IsKind(one.Kind, "group"));
        if (group is null)
        {
            result.Errors.Add("结论缺少 kind=group 产物，无法渲染建组读数。");
        }
        else
        {
            result.GroupName = group.Name ?? string.Empty;
            result.GroupWebUrl = Nullable(group.WebUrl);
            result.GroupId = ParseLong(group.ExternalId);

            if (group.Extra is GroupArtifactExtra gx)
            {
                result.GroupExists = gx.Exists;
                result.GroupParentId = gx.ParentId;
                result.OrganizationId = Nullable(gx.OrganizationId);
            }
            else
            {
                result.Errors.Add("kind=group 产物的 extra 不是 GroupArtifactExtra（结论文件 schema 不符）。");
            }
        }

        foreach (var end in ends)
        {
            var row = new EndPrecheck { End = end };
            result.Ends.Add(row);

            var artifact = report.Artifacts.FirstOrDefault(one => IsKind(one.Kind, "repository") && one.End == end);
            if (artifact is null)
            {
                row.Error = "结论缺少该端 kind=repository 产物。";
                result.Errors.Add($"{EndName(end)}：结论缺少该端仓库产物。");
                continue;
            }

            row.RepositoryName = artifact.Name ?? string.Empty;
            row.RepositoryWebUrl = Nullable(artifact.WebUrl);
            row.LocalPath = artifact.LocalPath ?? string.Empty;
            row.ResolvedBranch = Nullable(artifact.TemplateBranch);

            if (string.IsNullOrWhiteSpace(row.RepositoryName))
                result.Errors.Add($"{EndName(end)}：仓库产物缺少名称。");

            if (artifact.Extra is RepositoryArtifactExtra ex)
            {
                row.TemplateRepository = ex.TemplateRepository ?? string.Empty;
                row.TemplateProjectId = ex.TemplateProjectId;
                row.TemplatePath = Nullable(ex.TemplatePath);
                row.TemplateBranches = ex.TemplateBranches ?? new List<string>();
                row.ResolvedBranchSource = ex.ResolvedBranchSource ?? string.Empty;
                row.RepositoryExists = ex.RepositoryExists;
                row.LocalPathOccupied = ex.LocalPathOccupied;
                row.Visibility = string.IsNullOrWhiteSpace(ex.Visibility) ? "internal" : ex.Visibility!;
                row.Error = Nullable(ex.Error);
            }
            else
            {
                row.Error = "仓库产物的 extra 不是 RepositoryArtifactExtra（结论文件 schema 不符）。";
                result.Errors.Add($"{EndName(end)}：{row.Error}");
            }

            if (string.IsNullOrWhiteSpace(row.LocalPath) && string.IsNullOrEmpty(row.Error))
                result.Errors.Add($"{EndName(end)}：仓库产物缺少 localPath（无法确定落地目录）。");
        }

        // checks[] → Errors / Warnings（ok=false → 阻断；ok=true 且 detail 以「提示」开头 → 非阻断）
        foreach (var check in report.Checks)
        {
            var detail = (check.Detail ?? string.Empty).Trim();
            if (!check.Ok)
            {
                result.Errors.Add(string.IsNullOrWhiteSpace(detail)
                    ? $"{check.Name}：未通过。"
                    : $"{check.Name}：{detail}");
            }
            else if (detail.StartsWith("提示", StringComparison.Ordinal))
            {
                result.Warnings.Add($"{check.Name}：{detail}");
            }
        }

        return result;
    }

    /// <summary>
    /// 由 Step 7 准备阶段结论映射出流水线预览（面板 YAML 复核界面直接消费）。
    /// </summary>
    /// <param name="report">已解析的准备阶段结论。</param>
    /// <returns>预览列表 + 映射缺口。</returns>
    public static PipelinePreviewMapResult ToPipelinePreviews(StepReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var result = new PipelinePreviewMapResult();

        foreach (var artifact in report.Artifacts.Where(one => IsKind(one.Kind, "pipeline")))
        {
            if (artifact.End is null)
            {
                result.Errors.Add("存在未标端归属的 kind=pipeline 产物（end 为空）。");
                continue;
            }

            var preview = new PipelinePreview
            {
                End = artifact.End.Value,
                PipelineName = artifact.Name ?? string.Empty,
                Yaml = artifact.Yaml ?? string.Empty,
                PipelineId = Nullable(artifact.ExternalId) ?? string.Empty,
                WebUrl = Nullable(artifact.WebUrl) ?? string.Empty
            };

            if (artifact.Extra is PipelineArtifactExtra ex)
            {
                preview.PendingBindings = ex.PendingBindings ?? new List<string>();
                preview.YamlIncludesDeployStage = ex.YamlIncludesDeployStage;
                preview.RecommendedDeployTargetUuid = Nullable(ex.RecommendedDeployTargetUuid) ?? string.Empty;
            }
            else
            {
                result.Errors.Add($"{EndName(artifact.End.Value)}：流水线产物的 extra 不是 PipelineArtifactExtra"
                                  + "（结论文件 schema 不符）。");
            }

            if (string.IsNullOrWhiteSpace(preview.PipelineName))
                result.Errors.Add($"{EndName(artifact.End.Value)}：流水线产物缺少名称。");

            if (string.IsNullOrWhiteSpace(preview.Yaml))
                result.Errors.Add($"{EndName(artifact.End.Value)}：流水线产物缺少 YAML 正文。");

            result.Previews.Add(preview);
        }

        if (result.Previews.Count == 0)
            result.Errors.Add("结论里没有任何 kind=pipeline 产物，无法渲染流水线预览。");

        return result;
    }

    private static bool IsKind(string? actual, string expected)
        => string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string? Nullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static long? ParseLong(string? value)
        => long.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string EndName(ProjectEnd end) => end switch
    {
        ProjectEnd.Service => "后端",
        ProjectEnd.Admin => "管理端",
        ProjectEnd.Mobile => "移动端",
        _ => end.ToString()
    };
}

/// <summary>
/// <see cref="ArtifactViewModelMapper.ToPipelinePreviews"/> 的返回：预览列表 + 映射缺口。
/// </summary>
public sealed class PipelinePreviewMapResult
{
    /// <summary>预览列表（每端一条）。</summary>
    public List<PipelinePreview> Previews { get; } = new();

    /// <summary>映射缺口（缺端 / 缺字段 / schema 不符）；非空即应阻断推进。</summary>
    public List<string> Errors { get; } = new();
}