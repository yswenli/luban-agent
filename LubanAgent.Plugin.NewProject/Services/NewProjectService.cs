/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgent.Plugin.NewProject.Services
*文件名： NewProjectService
*创建时间：2026/10/16
*描述：共享能力层实现（PRD §4.10.2）。向导面板与聊天入口共用同一实例。
*****************************************************************************/

using LubanAgent.Plugin.NewProject.Abstractions;
using LubanAgent.Plugin.NewProject.Configuration;
using LuBan.AIAgent.Plugins;

namespace LubanAgent.Plugin.NewProject.Services;

/// <summary>
/// 「新建项目」共享能力层实现。
/// </summary>
/// <remarks>
/// <para>
/// <b>生命周期</b>：本实例由插件（<c>NewProjectPlugin</c>）持有，存活于插件 ALC 之内；
/// 向导面板直接持有它，聊天路径（P6）经同一实例暴露，保证「同一个实例、零逻辑分叉」。
/// </para>
/// <para>
/// <b>依赖解析（PRD §3.9 DI 硬约束）</b>：宿主容器里的工作区 / 会话 / 连接器服务
/// 一律<b>按需解析</b>，<b>不</b>长期持有（Scoped 服务不得被单例提升）。
/// </para>
/// <para>
/// <b>当前进度（M4-P1）</b>：档案模型、状态机、持久化、操作记录已就绪；
/// 六个能力方法按计划在 P2~P5 分批实现（此阶段返回明确的「未实现」结果，不抛异常）。
/// </para>
/// </remarks>
public sealed class NewProjectService : INewProjectService
{
    private readonly List<OperationRecord> _operations = new();
    private readonly NewProjectStateStore _store = new();
    private readonly object _gate = new();

    private IPluginHost? _host;
    private NewProjectSettings _settings = new();

    /// <inheritdoc />
    public ProjectSpec? Current { get; private set; }

    /// <inheritdoc />
    public WizardStep CurrentStep => Current?.CurrentStep ?? WizardStep.BasicInfo;

    /// <inheritdoc />
    public IReadOnlyList<OperationRecord> Operations
    {
        get
        {
            lock (_gate)
            {
                return _operations.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>
    /// 绑定宿主（插件初始化时调用）：载入包内配置与上一次未完成的档案。
    /// </summary>
    /// <param name="host">宿主能力接口。</param>
    internal void Attach(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        _settings = NewProjectSettings.LoadDefault();

        // 残留检测：上次未完成的项目档案仍可读取（供面板提示与孤儿清单展示，不做断点续跑）
        var restored = _store.Load();
        if (restored is not null && !restored.IsCompleted)
        {
            Current = restored;
            host.Log($"[newproject] 检测到未完成的项目档案：{restored.Code}（当前步骤 {(int)restored.CurrentStep}）");
        }
        else
        {
            Current = restored;
        }

        RaiseChanged();
    }

    /// <summary>
    /// 解除宿主绑定（插件关闭时调用）。
    /// </summary>
    internal void Detach()
    {
        _host = null;
        lock (_gate)
        {
            _operations.Clear();
        }

        RaiseChanged();
    }

    /// <inheritdoc />
    public ProjectSpec StartNewProject(
        string name,
        string code,
        string introduction,
        IEnumerable<ProjectEnd> ends,
        string localPath)
    {
        ArgumentNullException.ThrowIfNull(ends);

        var selected = ends.Distinct().ToList();

        var spec = new ProjectSpec
        {
            Name = name?.Trim() ?? string.Empty,
            Code = code?.Trim() ?? string.Empty,
            Introduction = introduction?.Trim() ?? string.Empty,
            Ends = selected,
            LocalPath = localPath?.Trim() ?? string.Empty,
            CurrentStep = WizardStep.BasicInfo
        };

        // 模板对照 → 各端目标仓库（仓库名默认 {缩写}-Service/-Admin/-Mobile，Step 3 可改）
        foreach (var end in selected)
        {
            var template = _settings.FindTemplate(end);
            spec.Repositories.Add(new EndRepository
            {
                End = end,
                TemplateRepository = template?.Repository ?? string.Empty,
                TemplateBranch = template?.Branch ?? string.Empty,
                RepositoryName = _settings.BuildRepositoryName(spec.Code, end),
                Visibility = _settings.DefaultVisibility,
                DefaultBranch = _settings.PushBranch,
                LocalPath = string.IsNullOrWhiteSpace(spec.LocalPath)
                    ? string.Empty
                    : Path.Combine(spec.LocalPath, _settings.BuildRepositoryName(spec.Code, end))
            });
        }

        // 参考流水线清单（Step 2 载入，Step 7 使用）
        foreach (var end in selected)
        {
            var reference = _settings.PipelineReferences.FirstOrDefault(item => item.End == end);
            if (!string.IsNullOrWhiteSpace(reference?.PipelineId))
                spec.PipelineReferenceIds.Add(reference!.PipelineId);
        }

        lock (_gate)
        {
            _operations.Clear();
        }

        Current = spec;
        _store.Save(spec);
        RaiseChanged();

        return spec;
    }

    /// <inheritdoc />
    public void Reset()
    {
        Current = null;
        lock (_gate)
        {
            _operations.Clear();
        }

        _store.Clear();
        RaiseChanged();
    }

    /// <inheritdoc />
    public Task<StepResult> InitializeAsync(CancellationToken cancellationToken = default)
        => NotYetAsync(WizardStep.Scaffold, "M4-P2");

    /// <inheritdoc />
    public Task<StepResult> CreateRepositoriesAsync(CancellationToken cancellationToken = default)
        => NotYetAsync(WizardStep.Repositories, "M4-P3");

    /// <inheritdoc />
    public Task<StepResult> SetupDatabaseAsync(CancellationToken cancellationToken = default)
        => NotYetAsync(WizardStep.Database, "M4-P4");

    /// <inheritdoc />
    public Task<StepResult> BuildAsync(CancellationToken cancellationToken = default)
        => NotYetAsync(WizardStep.Build, "M4-P4");

    /// <inheritdoc />
    public Task<StepResult> PushAsync(CancellationToken cancellationToken = default)
        => NotYetAsync(WizardStep.Push, "M4-P3");

    /// <inheritdoc />
    public Task<StepResult> CreatePipelinesAsync(CancellationToken cancellationToken = default)
        => NotYetAsync(WizardStep.Pipeline, "M4-P5");

    /// <inheritdoc />
    public Task AbortAndCleanupAsync(CancellationToken cancellationToken = default)
    {
        var spec = Current;
        if (spec is not null)
        {
            // 远端产物一律不自动删除：只保留孤儿清单（PRD §4.7）。
            var orphans = spec.RemoteArtifacts.Count;
            if (orphans > 0)
                _host?.Log($"[newproject] 「中止并清理」保留 {orphans} 条远端产物孤儿记录（需到云效控制台手动确认删除）。");
        }

        Reset();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 记一条操作记录（右侧时间线数据源，PRD §4.6）。
    /// </summary>
    /// <param name="step">步骤。</param>
    /// <param name="action">动作标题。</param>
    /// <param name="detail">参数 / 结果摘要（调用方负责脱敏）。</param>
    /// <param name="status">状态。</param>
    /// <param name="duration">耗时。</param>
    internal void Record(WizardStep step, string action, string detail, StepStatus status, TimeSpan duration)
    {
        lock (_gate)
        {
            _operations.Add(new OperationRecord
            {
                Step = step,
                Action = action,
                Detail = detail,
                Status = status,
                Duration = duration
            });
        }

        RaiseChanged();
    }

    /// <summary>
    /// 尚未实现的步骤：返回明确失败结果（不抛异常，面板可原样展示）。
    /// </summary>
    /// <param name="step">步骤。</param>
    /// <param name="batch">计划实现批次。</param>
    /// <returns>失败结果。</returns>
    private static Task<StepResult> NotYetAsync(WizardStep step, string batch)
        => Task.FromResult(StepResult.Fail(step, $"该步骤将在 {batch} 实现（M4 P1 骨架阶段）。"));

    /// <summary>
    /// 触发变更通知（面板据此刷新）。
    /// </summary>
    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Warn("[newproject] 面板刷新回调异常（已忽略）。", ex);
        }
    }
}