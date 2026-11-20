/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentWebHost.Services.Prd
*文件名： PrdSplitPlanner
*描述：PRD 拆分的提示词装配与模型输出解析 / 归一化（纯函数，供单测锁口径）
*
*****************************************************************************/

using System.Text;
using System.Text.Json;

namespace LubanAgentWebHost.Services.Prd;

/// <summary>
/// PRD 拆分规划器：把同步结果与补充文档装配成提示词，并解析模型的 JSON 输出。
/// </summary>
/// <remarks>
/// 本类不发起模型调用（调用在 <see cref="PrdSplitService"/>），
/// 全为纯函数以便对「提示词口径」和「输出容错」单独单测。
/// </remarks>
public static class PrdSplitPlanner
{
    /// <summary>用户提示词总字符上限（超出部分截断，避免超出上下文）。</summary>
    public const int MaxPromptChars = 24_000;

    /// <summary>需求上限。</summary>
    public const int MaxRequirements = 50;

    /// <summary>任务上限。</summary>
    public const int MaxTasks = 200;

    /// <summary>测试用例上限。</summary>
    public const int MaxTestcases = 200;

    private static readonly JsonSerializerOptions ParseOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// 系统指令：约束模型只输出 JSON 且不臆造事实。
    /// </summary>
    public const string SystemInstruction = """
        你是研发需求拆分助手。根据给定的需求清单与补充文档，产出一份可直接落库到云效的结构化计划。

        只输出一个 JSON 对象，不要任何解释、不要 Markdown 代码块、不要前后缀。JSON 结构如下：
        {
          "iteration": { "name": "迭代名称", "startDate": "yyyy-MM-dd", "endDate": "yyyy-MM-dd", "description": "迭代目标" },
          "requirements": [ { "key": "R1", "title": "需求标题", "description": "需求描述", "acceptance": "验收标准", "priority": "P0|P1|P2", "source": "sync|supplement|local", "sourceTitle": "来源标题" } ],
          "tasks": [ { "key": "T1", "requirementKey": "R1", "title": "任务标题", "description": "任务说明", "priority": "P1", "dueDate": "yyyy-MM-dd" } ],
          "testcases": [ { "key": "C1", "requirementKey": "R1", "title": "用例标题", "preCondition": "前置条件", "steps": "1. 步骤\n2. 期望结果" } ],
          "testplan": { "name": "测试计划名称", "environment": "测试环境", "executor": "" }
        }

        必须遵守：
        1. 至少产出 1 条需求；每条任务必须通过 requirementKey 挂到某条需求。
        2. 每条需求拆 1–5 个可执行任务；有明显验证价值的补测试用例，用例数不超过任务数。
        3. 标题不超过 200 字；不要编造需求与文档中没有的事实，信息不足时在描述中写明待确认。
        4. 未提供迭代日期时 startDate / endDate 用空字符串；未提供优先级时用 P2。
        5. key 在同一数组内唯一，格式 R1/R2、T1/T2、C1/C2。
        """;

    /// <summary>
    /// 装配用户提示词。
    /// </summary>
    /// <param name="sync">云效同步结果。</param>
    /// <param name="documents">补充文档（可为 <c>null</c>）。</param>
    /// <returns>提示词正文（总长不超过 <see cref="MaxPromptChars"/>）。</returns>
    public static string BuildUserPrompt(PrdSyncResult sync, IReadOnlyList<PrdSupplementDocument>? documents)
    {
        ArgumentNullException.ThrowIfNull(sync);

        var builder = new StringBuilder();
        builder.AppendLine($"项目：{sync.ProjectName}（{sync.ProjectId}）");
        builder.AppendLine($"同步状态：{sync.Status}，需求条数：{sync.Requirements.Count}，附件份数：{sync.Attachments.Count}");
        builder.AppendLine();
        builder.AppendLine("【云效需求清单】");

        for (var i = 0; i < sync.Requirements.Count; i++)
        {
            if (i >= MaxRequirements)
                break;

            var item = sync.Requirements[i];
            builder.AppendLine($"{i + 1}. {item.Title}" + (string.IsNullOrWhiteSpace(item.SerialNumber) ? "" : $"（{item.SerialNumber}）"));
            if (!string.IsNullOrWhiteSpace(item.Description))
                builder.AppendLine($"   描述：{Truncate(item.Description!, 1200)}");
            if (!string.IsNullOrWhiteSpace(item.Acceptance))
                builder.AppendLine($"   验收：{Truncate(item.Acceptance!, 600)}");
        }

        // 需求条数不进提示词会让模型按全量拆分，输出必然超出单次调用的 token 上限而被截断
        if (sync.Requirements.Count > MaxRequirements)
            builder.AppendLine($"（需求共 {sync.Requirements.Count} 条，这里只列出前 {MaxRequirements} 条，请只拆分列出的需求）");

        if (sync.Attachments.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("【需求附件（仅文件名，正文未解析）】");
            foreach (var file in sync.Attachments.Take(50))
                builder.AppendLine($"- {file.WorkitemTitle} / {file.FileName}");
        }

        var documentBudget = Math.Max(MaxPromptChars - builder.Length, 2000);
        if (documents is { Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine("【补充文档】");
            var used = 0;
            foreach (var document in documents)
            {
                if (used >= documentBudget)
                    break;

                var content = document.Content ?? string.Empty;
                var remaining = documentBudget - used;
                var slice = Truncate(content, Math.Min(remaining, 8000));
                builder.AppendLine($"--- {document.Name} ---");
                builder.AppendLine(slice);
                used += slice.Length;
            }
        }

        if (builder.Length > MaxPromptChars)
            builder.Length = MaxPromptChars;

        return builder.ToString();
    }

    /// <summary>
    /// 解析模型输出为拆分计划并归一化。
    /// </summary>
    /// <param name="text">模型返回文本（允许 ```json 围栏与前后说明）。</param>
    /// <returns>成功返回计划；失败返回 <c>null</c> 与原因。</returns>
    public static (PrdSplitPlan? Plan, string? Error) Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, "模型输出为空");

        var json = ExtractJsonObject(text!);
        if (json is null)
            return (null, "模型输出不含 JSON 对象");

        PrdSplitPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<PrdSplitPlan>(json, ParseOpts);
        }
        catch (JsonException ex)
        {
            return (null, $"JSON 解析失败：{ex.Message}");
        }

        if (plan is null)
            return (null, "JSON 解析结果为空");

        Normalize(plan);

        if (plan.Requirements.Count == 0)
            return (null, "未解析出有效需求（requirements 为空或标题缺失）");

        return (plan, null);
    }

    /// <summary>
    /// 从自由文本中截取第一个完整 JSON 对象（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="text">原始文本。</param>
    /// <returns>JSON 对象文本；找不到返回 <c>null</c>。</returns>
    internal static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
            return null;

        var end = text.LastIndexOf('}');
        if (end <= start)
            return null;

        return text[start..(end + 1)];
    }

    /// <summary>
    /// 归一化：补键、去空项、校验关联、截断超量（纯函数，供单测锁口径）。
    /// </summary>
    /// <param name="plan">待归一化的计划（原地修改）。</param>
    internal static void Normalize(PrdSplitPlan plan)
    {
        plan.Requirements ??= [];
        plan.Tasks ??= [];
        plan.Testcases ??= [];
        plan.Warnings ??= [];

        NormalizeRequirements(plan);
        var requirementKeys = plan.Requirements.Select(r => r.Key!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        NormalizeTasks(plan, requirementKeys);
        NormalizeTestcases(plan, requirementKeys);
        NormalizeTestPlan(plan);
    }

    private static void NormalizeRequirements(PrdSplitPlan plan)
    {
        var normalized = new List<PrdRequirementPlan>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in plan.Requirements)
        {
            if (item is null)
                continue;

            item.Title = item.Title?.Trim();
            if (string.IsNullOrWhiteSpace(item.Title))
                continue;

            item.Key = MakeUniqueKey(item.Key, $"R{normalized.Count + 1}", used);
            item.Description = item.Description?.Trim();
            item.Acceptance = item.Acceptance?.Trim();
            item.Priority = NormalizePriority(item.Priority);
            normalized.Add(item);
        }

        if (normalized.Count > MaxRequirements)
        {
            plan.Warnings.Add($"需求超过 {MaxRequirements} 条，已截断");
            normalized.RemoveRange(MaxRequirements, normalized.Count - MaxRequirements);
        }

        plan.Requirements = normalized;
    }

    private static void NormalizeTasks(PrdSplitPlan plan, HashSet<string> requirementKeys)
    {
        var normalized = new List<PrdTaskPlan>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in plan.Tasks)
        {
            if (item is null)
                continue;

            item.Title = item.Title?.Trim();
            if (string.IsNullOrWhiteSpace(item.Title))
                continue;

            var requirementKey = item.RequirementKey?.Trim();
            if (string.IsNullOrWhiteSpace(requirementKey) || !requirementKeys.Contains(requirementKey))
            {
                plan.Warnings.Add($"任务「{item.Title}」未关联到有效需求，已跳过");
                continue;
            }

            item.Key = MakeUniqueKey(item.Key, $"T{normalized.Count + 1}", used);
            item.RequirementKey = requirementKey;
            item.Description = item.Description?.Trim();
            item.Priority = NormalizePriority(item.Priority);
            item.DueDate = item.DueDate?.Trim();
            normalized.Add(item);
        }

        if (normalized.Count > MaxTasks)
        {
            plan.Warnings.Add($"任务超过 {MaxTasks} 条，已截断");
            normalized.RemoveRange(MaxTasks, normalized.Count - MaxTasks);
        }

        plan.Tasks = normalized;
    }

    private static void NormalizeTestcases(PrdSplitPlan plan, HashSet<string> requirementKeys)
    {
        var normalized = new List<PrdTestcasePlan>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in plan.Testcases)
        {
            if (item is null)
                continue;

            item.Title = item.Title?.Trim();
            if (string.IsNullOrWhiteSpace(item.Title))
                continue;

            item.Key = MakeUniqueKey(item.Key, $"C{normalized.Count + 1}", used);

            var requirementKey = item.RequirementKey?.Trim();
            if (!string.IsNullOrWhiteSpace(requirementKey) && !requirementKeys.Contains(requirementKey))
            {
                plan.Warnings.Add($"用例「{item.Title}」关联的需求不存在，已清空关联");
                requirementKey = null;
            }

            item.RequirementKey = requirementKey;
            item.PreCondition = item.PreCondition?.Trim();
            item.Steps = item.Steps?.Trim();
            normalized.Add(item);
        }

        if (normalized.Count > MaxTestcases)
        {
            plan.Warnings.Add($"测试用例超过 {MaxTestcases} 条，已截断");
            normalized.RemoveRange(MaxTestcases, normalized.Count - MaxTestcases);
        }

        plan.Testcases = normalized;
    }

    private static void NormalizeTestPlan(PrdSplitPlan plan)
    {
        if (plan.TestPlan is null)
            return;

        plan.TestPlan.Name = plan.TestPlan.Name?.Trim();
        plan.TestPlan.Environment = plan.TestPlan.Environment?.Trim();
        plan.TestPlan.Executor = plan.TestPlan.Executor?.Trim();

        if (string.IsNullOrWhiteSpace(plan.TestPlan.Name))
        {
            if (plan.Testcases.Count == 0)
            {
                plan.TestPlan = null;
                return;
            }

            var iterationName = plan.Iteration?.Name?.Trim();
            plan.TestPlan.Name = string.IsNullOrWhiteSpace(iterationName)
                ? "本轮测试计划"
                : $"{iterationName} 测试计划";
        }

        if (plan.Iteration is not null)
        {
            plan.Iteration.Name = plan.Iteration.Name?.Trim();
            plan.Iteration.StartDate = plan.Iteration.StartDate?.Trim();
            plan.Iteration.EndDate = plan.Iteration.EndDate?.Trim();
            plan.Iteration.Description = plan.Iteration.Description?.Trim();
        }
    }

    private static string MakeUniqueKey(string? key, string fallback, HashSet<string> used)
    {
        var candidate = string.IsNullOrWhiteSpace(key) ? fallback : key.Trim();

        if (used.Add(candidate))
            return candidate;

        var suffix = 2;
        var unique = $"{candidate}-{suffix}";
        while (!used.Add(unique))
        {
            suffix++;
            unique = $"{candidate}-{suffix}";
        }

        return unique;
    }

    private static string? NormalizePriority(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
            return "P2";

        var value = priority.Trim().ToUpperInvariant();
        return value is "P0" or "P1" or "P2" or "P3" ? value : "P2";
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text;

        return text[..max] + "…";
    }
}
