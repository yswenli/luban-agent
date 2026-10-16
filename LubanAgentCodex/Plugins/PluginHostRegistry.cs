/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.Plugins
*文件名： PluginHostRegistry
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：插件贡献记账表（UI 无关，可单测）：侧栏条目、面板工厂与实例、命令、技能
*
*****************************************************************************/
using LuBan.AIAgent.Plugins;

namespace LubanAgentCodex.Plugins;

/// <summary>
/// 插件贡献记账表：保存宿主侧真实持有的插件贡献（侧栏条目 / 面板工厂与实例 / 命令 / 技能）。
/// </summary>
/// <remarks>
/// <para>
/// 刻意<b>不引用任何 UI 类型</b>，便于单测；UI 挂载由 <see cref="CodexPluginHost"/> 通知主窗口完成。
/// </para>
/// <para>
/// 键统一使用 <c>entryId</c>（PRD §3.2：侧栏条目 id 同时是面板注册键）。框架
/// <c>PluginHostScope</c> 在 <c>UnregisterPanel</c> 时只传 <c>entryId</c>，故跨插件同名条目按
/// 「后注册者胜出」处理并记警告——插件应保证 id 全局唯一。
/// </para>
/// </remarks>
public sealed class PluginHostRegistry
{
    private readonly object _gate = new();

    private readonly List<PluginSidebarEntry> _sidebarEntries = new();
    private readonly Dictionary<string, Func<IPluginPanel>> _panelFactories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IPluginPanel> _panelInstances = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginCommandDescriptor> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _skillRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginSkillRecord> _runtimeSkills = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 登记（或覆盖）一个侧栏条目。
    /// </summary>
    /// <param name="entry">侧栏条目（<c>PluginId</c> 已由框架回填；可能为 null）。</param>
    /// <returns>是否覆盖了已存在的同 id 条目。</returns>
    public bool AddOrUpdateSidebarEntry(PluginSidebarEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            var index = _sidebarEntries.FindIndex(
                e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                _sidebarEntries[index] = entry;
                return true;
            }

            _sidebarEntries.Add(entry);
            return false;
        }
    }

    /// <summary>
    /// 移除一个侧栏条目。
    /// </summary>
    /// <param name="entryId">条目 id。</param>
    /// <returns>是否实际移除。</returns>
    public bool RemoveSidebarEntry(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return false;

        lock (_gate)
        {
            var index = _sidebarEntries.FindIndex(
                e => string.Equals(e.Id, entryId, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                return false;

            _sidebarEntries.RemoveAt(index);
            return true;
        }
    }

    /// <summary>
    /// 取当前全部侧栏条目（快照，按注册顺序；只保留启用中插件的贡献）。
    /// </summary>
    public IReadOnlyList<PluginSidebarEntry> GetSidebarEntries()
    {
        lock (_gate)
        {
            return _sidebarEntries.ToList();
        }
    }

    /// <summary>
    /// 注册面板工厂。
    /// </summary>
    /// <param name="entryId">条目 / 面板标识。</param>
    /// <param name="factory">面板工厂（延迟实例化）。</param>
    /// <returns>是否覆盖了已存在的同 id 工厂。</returns>
    public bool RegisterPanel(string entryId, Func<IPluginPanel> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
        {
            var overwritten = _panelFactories.ContainsKey(entryId);
            _panelFactories[entryId] = factory;

            // 工厂被替换：丢弃旧实例引用，避免旧实例钉住插件 ALC
            _panelInstances.Remove(entryId);
            return overwritten;
        }
    }

    /// <summary>
    /// 取（必要时创建并缓存）面板实例。
    /// </summary>
    /// <param name="entryId">条目 / 面板标识。</param>
    /// <returns>面板实例；未注册工厂返回 <c>null</c>。</returns>
    public IPluginPanel? GetPanel(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return null;

        Func<IPluginPanel>? factory;
        lock (_gate)
        {
            if (_panelInstances.TryGetValue(entryId, out var cached))
                return cached;

            if (!_panelFactories.TryGetValue(entryId, out factory))
                return null;
        }

        var panel = factory();
        lock (_gate)
        {
            // 并发创建时以先入者为准
            if (_panelInstances.TryGetValue(entryId, out var existing))
                return existing;

            _panelInstances[entryId] = panel;
            return panel;
        }
    }

    /// <summary>
    /// 取已实例化的面板（不触发创建）。
    /// </summary>
    /// <param name="entryId">条目 / 面板标识。</param>
    /// <returns>已实例化的面板；未实例化返回 <c>null</c>。</returns>
    public IPluginPanel? PeekPanel(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return null;

        lock (_gate)
        {
            return _panelInstances.GetValueOrDefault(entryId);
        }
    }

    /// <summary>
    /// 移除面板工厂与实例引用（不调用 <c>OnUnloading</c>，由调用方在 UI 线程完成）。
    /// </summary>
    /// <param name="entryId">条目 / 面板标识。</param>
    /// <returns>是否实际移除。</returns>
    public bool RemovePanel(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return false;

        lock (_gate)
        {
            _panelInstances.Remove(entryId);
            return _panelFactories.Remove(entryId);
        }
    }

    /// <summary>
    /// 登记一个命令（本期为声明性契约，不参与调度）。
    /// </summary>
    /// <param name="command">命令描述。</param>
    /// <returns>是否覆盖了已存在的同名命令。</returns>
    public bool RegisterCommand(PluginCommandDescriptor command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Name))
            return false;

        lock (_gate)
        {
            var overwritten = _commands.ContainsKey(command.Name);
            _commands[command.Name] = command;
            return overwritten;
        }
    }

    /// <summary>
    /// 取当前全部命令（快照）。
    /// </summary>
    public IReadOnlyList<PluginCommandDescriptor> GetCommands()
    {
        lock (_gate)
        {
            return _commands.Values.ToList();
        }
    }

    /// <summary>
    /// 清空全部记账（用于宿主关闭）。
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _sidebarEntries.Clear();
            _panelFactories.Clear();
            _panelInstances.Clear();
            _commands.Clear();
            _skillRoots.Clear();
            _runtimeSkills.Clear();
        }
    }

    /// <summary>
    /// 记录一项插件运行期贡献的技能（<c>RegisterSkill</c> / <c>ContributeSkills</c>）。
    /// </summary>
    /// <remarks>
    /// 清单声明的技能可由 <c>plugin.json</c> 随时重建，而运行期贡献的技能只存在于插件代码中，
    /// 宿主必须记账才能在会话构建（含工作区切换）时补齐（PRD §3.11「回放」）。
    /// 定义按值快照，避免插件复用 / 改写同一实例导致回放内容漂移。
    /// </remarks>
    /// <param name="definition">技能定义。</param>
    /// <param name="sourceDirectory">来源插件目录（用于解析相对 <c>entry</c>；可为 <c>null</c>）。</param>
    public void TrackSkill(SkillDefinition definition, string? sourceDirectory)
    {
        if (definition is null || string.IsNullOrWhiteSpace(definition.Id))
            return;

        lock (_gate)
        {
            _runtimeSkills[definition.Id] = new PluginSkillRecord(CloneDefinition(definition), sourceDirectory);
        }
    }

    /// <summary>
    /// 取全部已记账的运行期技能（快照；供会话构建时补齐）。
    /// </summary>
    public IReadOnlyList<PluginSkillRecord> GetRuntimeSkills()
    {
        lock (_gate)
        {
            return _runtimeSkills.Values.ToList();
        }
    }

    private static SkillDefinition CloneDefinition(SkillDefinition definition) => new()
    {
        Id = definition.Id,
        Name = definition.Name,
        Description = definition.Description,
        Entry = definition.Entry,
        Category = definition.Category,
        TriggerKeywords = definition.TriggerKeywords is null ? null : new List<string>(definition.TriggerKeywords),
        Permissions = definition.Permissions
    };

    /// <summary>
    /// 记录插件技能写入过的工作区根（停用时据此精确删除，避免误删同名非插件技能）。
    /// </summary>
    /// <param name="skillId">技能标识。</param>
    /// <param name="workspaceRoot">工作区根路径。</param>
    public void TrackSkillRoot(string skillId, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(skillId) || string.IsNullOrWhiteSpace(workspaceRoot))
            return;

        lock (_gate)
        {
            if (!_skillRoots.TryGetValue(skillId, out var roots))
            {
                roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _skillRoots[skillId] = roots;
            }

            roots.Add(workspaceRoot);
        }
    }

    /// <summary>
    /// 取某技能写入过的工作区根集合。
    /// </summary>
    /// <param name="skillId">技能标识。</param>
    public IReadOnlyCollection<string> GetSkillRoots(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            return Array.Empty<string>();

        lock (_gate)
        {
            return _skillRoots.TryGetValue(skillId, out var roots) ? roots.ToArray() : Array.Empty<string>();
        }
    }

    /// <summary>
    /// 移除某技能的记账。
    /// </summary>
    /// <param name="skillId">技能标识。</param>
    /// <returns>是否实际移除。</returns>
    public bool RemoveSkill(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            return false;

        lock (_gate)
        {
            _runtimeSkills.Remove(skillId);
            return _skillRoots.Remove(skillId);
        }
    }

    /// <summary>
    /// 取全部已记账的插件技能 id（快照；供工作区切换时重放）。
    /// </summary>
    public IReadOnlyList<string> GetSkillIds()
    {
        lock (_gate)
        {
            return _skillRoots.Keys.ToList();
        }
    }
}

/// <summary>
/// 运行期贡献技能的一条记账（技能定义快照 + 来源插件目录）。
/// </summary>
/// <param name="Definition">技能定义快照。</param>
/// <param name="SourceDirectory">来源插件目录（用于解析相对 <c>entry</c>；解析不出时为 <c>null</c>）。</param>
public sealed record PluginSkillRecord(SkillDefinition Definition, string? SourceDirectory);