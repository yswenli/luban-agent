/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.Plugins
*文件名： CodexPluginHost
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/15
*描述：LubanCodex 的插件宿主实现（单例）：承接插件贡献并落到 UI / 工作区 / 会话注册表
*
*****************************************************************************/
using System.Text;
using LuBan.AIAgent.Configuration;
using LuBan.AIAgent.Plugins;
using LuBan.AIAgent.Plugins.Lifecycle;
using LuBan.AIAgent.Plugins.Security;
using LuBan.AIAgent.Skills;
using LubanAgentCore.Context;
using LubanAgentCore.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace LubanAgentCodex.Plugins;

/// <summary>
/// LubanCodex 插件宿主实现（<b>单例</b>）。
/// </summary>
/// <remarks>
/// <para>
/// 框架 <c>PluginManager</c> 为单例，经根容器延迟解析 <c>IPluginHost</c>，故本类型必须以单例注册；
/// 其中需要触达的 scoped 服务统一经 <see cref="PluginHostContext"/> 的会话 scope 解析。
/// </para>
/// <para>
/// 贡献落地遵循 PRD §3.11 / §3.12：
/// <list type="bullet">
/// <item>侧栏条目 / 面板 / 命令 → 记账于 <see cref="PluginHostRegistry"/>，由主窗口渲染与挂载；</item>
/// <item>Skill → 写工作区文件 <c>.luban-agent/skills/&lt;id&gt;/SKILL.md</c> → <c>SkillRegistry.LoadFromWorkspace</c> → 重建 Agent；</item>
/// <item>MCP → 注册进程内客户端到<b>各活动会话</b>的 <c>MCPRegistry</c>，并在每次会话构建时由
/// <see cref="Contribute"/> 回放（MCP 注册表为会话 scope，每会话一份）。</item>
/// </list>
/// </para>
/// </remarks>
public sealed class CodexPluginHost : IPluginHost, IPluginSessionContributor
{
    private readonly PluginHostRegistry _registry;
    private readonly PluginHostContext _context;
    private readonly IConfiguration _configuration;
    private readonly IOptions<LuBanAgentOptions> _agentOptions;

    /// <summary>
    /// 创建插件宿主。
    /// </summary>
    /// <param name="registry">贡献记账表。</param>
    /// <param name="context">作用域访问桥。</param>
    /// <param name="configuration">宿主配置（供插件读取配置项）。</param>
    /// <param name="agentOptions">Agent 选项（取 Skill 文件大小上限等）。</param>
    public CodexPluginHost(
        PluginHostRegistry registry,
        PluginHostContext context,
        IConfiguration configuration,
        IOptions<LuBanAgentOptions> agentOptions)
    {
        _registry = registry;
        _context = context;
        _configuration = configuration;
        _agentOptions = agentOptions;
    }

    /// <summary>
    /// 侧栏贡献发生变化（宿主 UI 需重取 <see cref="GetSidebarEntries"/> 刷新）。
    /// </summary>
    public event EventHandler? SidebarEntriesChanged;

    /// <summary>
    /// 面板即将被注销：宿主须在 <b>UI 线程同步</b>把面板控件从内容区解绑（参数：entryId、面板实例）。
    /// </summary>
    /// <remarks>
    /// 面板实例由插件 ALC 定义，UI 树上的残留引用会钉住程序集导致卸载降级为 <c>RequiresRestart</c>
    /// （PRD §3.12 第 2 步「宿主须先解绑内容区再调用 UnregisterPanel」）。
    /// </remarks>
    public event Action<string, IPluginPanel>? PanelDetaching;

    #region IPluginHost

    /// <inheritdoc />
    /// <remarks>
    /// 返回<b>根容器</b>：插件可解析身份无关的进程级单例（配置、Provider 工厂等）；
    /// 按用户/会话隔离的 scoped 服务在 <c>ValidateScopes=true</c> 下不能从根容器取，
    /// 插件应改为经 <c>SessionRuntime.Services</c> 获取（PRD §3.9 DI 硬约束）。
    /// </remarks>
    public IServiceProvider Services => _context.Root;

    /// <inheritdoc />
    /// <remarks>框架经 <c>PluginHostScope</c> 按插件包装后回填真实标识，共享实现自身不持有插件身份。</remarks>
    public string PluginId => string.Empty;

    /// <inheritdoc />
    /// <remarks>
    /// 同理，共享实现自身不持有插件权限；真实权限由 <c>PluginHostScope</c> 回填，
    /// 因此这里返回空权限（默认拒绝）以免误放行。
    /// </remarks>
    public PluginPermissions Permissions => new();

    /// <inheritdoc />
    public void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Logger.Info(message);
    }

    /// <inheritdoc />
    public void LogError(string message, Exception? exception = null)
    {
        if (string.IsNullOrWhiteSpace(message)) message = "插件发生未知错误。";
        Logger.Warn(message, exception);
    }

    /// <inheritdoc />
    public string? GetConfig(string key)
        => string.IsNullOrWhiteSpace(key) ? null : _configuration[key];

    /// <inheritdoc />
    public void RegisterSidebarEntry(PluginSidebarEntry entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Id))
        {
            LogError("侧栏条目缺少 id，已忽略。");
            return;
        }

        if (_registry.AddOrUpdateSidebarEntry(entry))
            Log($"侧栏条目 {entry.Id} 覆盖了既有同 id 条目。");

        NotifySidebarEntriesChanged();
    }

    /// <inheritdoc />
    public void UnregisterSidebarEntry(string entryId)
    {
        if (!_registry.RemoveSidebarEntry(entryId)) return;
        NotifySidebarEntriesChanged();
    }

    /// <inheritdoc />
    public void RegisterPanel(string entryId, Func<IPluginPanel> factory)
    {
        if (string.IsNullOrWhiteSpace(entryId) || factory is null)
        {
            LogError("面板注册缺少 entryId 或工厂，已忽略。");
            return;
        }

        if (_registry.RegisterPanel(entryId, factory))
            Log($"面板 {entryId} 覆盖了既有同 id 面板。");
    }

    /// <inheritdoc />
    public void UnregisterPanel(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId)) return;

        var panel = _registry.PeekPanel(entryId);

        // 必须同步：本方法由框架卸载流程第 2 步（后台线程）调用，PC 返回后框架即进入 ALC 卸载，
        // 若此时 UI 树仍持有插件程序集定义的面板控件，弱引用验证必然失败。
        RunOnUiThreadSync(() =>
        {
            if (panel is null) return;

            try
            {
                PanelDetaching?.Invoke(entryId, panel);
            }
            catch (Exception ex)
            {
                LogError($"面板 {entryId} 从内容区解绑失败", ex);
            }

            try
            {
                panel.OnUnloading();
            }
            catch (Exception ex)
            {
                LogError($"面板 {entryId} 的 OnUnloading 失败", ex);
            }
        });

        _registry.RemovePanel(entryId);
    }

    /// <inheritdoc />
    public void RegisterCommand(PluginCommandDescriptor command)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.Name))
        {
            LogError("命令描述缺少 name，已忽略。");
            return;
        }

        _registry.RegisterCommand(command);
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginSidebarEntry> GetSidebarEntries() => _registry.GetSidebarEntries();

    /// <inheritdoc />
    public IPluginPanel? GetPanel(string entryId) => _registry.GetPanel(entryId);

    /// <inheritdoc />
    public void PostToUi(Action action) => PostToUiCore(action);

    #endregion

    #region 技能贡献

    /// <inheritdoc />
    public void RegisterSkill(SkillDefinition definition)
    {
        if (definition is null || string.IsNullOrWhiteSpace(definition.Id))
        {
            LogError("插件技能缺少 id，已忽略。");
            return;
        }

        var sourceDirectory = ResolveSourceDirectory(definition);

        // 先记账再落盘：运行期技能只存在于插件代码中，记账后才能在会话构建 / 工作区切换时补齐（PRD §3.11）
        _registry.TrackSkill(definition, sourceDirectory);

        var workspaceRoot = _context.CurrentWorkspaceRoot;
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            Log($"插件技能 {definition.Id} 暂无活动工作区，将在会话构建时补齐。");
            return;
        }

        if (!TryWriteSkillFile(workspaceRoot, definition, definition, sourceDirectory, out var error))
        {
            LogError($"插件技能 {definition.Id} 落盘失败：{error}");
            return;
        }

        _registry.TrackSkillRoot(definition.Id, workspaceRoot);
        Log($"技能 {definition.Id} 已写入 {Path.Combine(workspaceRoot, ".luban-agent", "skills", definition.Id)}");

        ReloadSkillsFor(workspaceRoot);
    }

    /// <inheritdoc />
    public void UnregisterSkill(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId)) return;

        var roots = _registry.GetSkillRoots(skillId);
        if (roots.Count == 0)
        {
            // 本次进程内未写过（如上次运行写入后再重启）：退化到当前工作区清理
            var current = _context.CurrentWorkspaceRoot;
            roots = string.IsNullOrWhiteSpace(current) ? Array.Empty<string>() : new[] { current };
        }

        foreach (var root in roots)
        {
            try
            {
                var skillDir = Path.Combine(root, ".luban-agent", "skills", skillId);
                if (Directory.Exists(skillDir))
                    Directory.Delete(skillDir, recursive: true);

                ReloadSkillsFor(root);
            }
            catch (Exception ex)
            {
                LogError($"删除插件技能 {skillId} 失败（{root}）", ex);
            }
        }

        _registry.RemoveSkill(skillId);
    }

    #endregion

    #region MCP 贡献

    /// <inheritdoc />
    public void RegisterMcpServer(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            LogError("插件 MCP 缺少名称，已忽略。");
            return;
        }

        var runtimes = _context.AllRuntimes();
        if (runtimes.Count == 0)
        {
            // 尚无活动会话：由 Contribute 在会话构建时回放，无需在此处理
            Log($"MCP {name} 暂无活动会话，将在会话构建时回放。");
            return;
        }

        foreach (var runtime in runtimes)
            TryRegisterMcpInSession(runtime.Services, name);

        ReloadAgents(_ => true);
    }

    /// <inheritdoc />
    public void UnregisterMcpServer(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        foreach (var runtime in _context.AllRuntimes())
        {
            try
            {
                runtime.Services.GetService<MCPRegistry>()?.UnregisterPluginClient(name);
            }
            catch (Exception ex)
            {
                LogError($"注销插件 MCP {name} 失败（会话 {runtime.SessionId}）", ex);
            }
        }

        ReloadAgents(_ => true);
    }

    #endregion

    #region IPluginSessionContributor

    /// <inheritdoc />
    /// <remarks>
    /// 会话级回放（PRD §3.11）：进程内 MCP 客户端无法用文件描述，且 <c>MCPRegistry</c> 为会话 scope，
    /// 故每次构建 Agent 前都要按当前<b>启用中</b>插件的清单重新注入；同时补齐本会话工作区缺失的
    /// 插件技能文件（工作区切换后自愈）。技能有两条来源：清单声明的 <c>plugin.json</c>，
    /// 以及运行期贡献（<c>RegisterSkill</c> / <c>ContributeSkills</c>，由宿主记账），两者都只补缺失、
    /// 不覆盖已存在文件。全过程幂等，失败不影响会话构建。
    /// </remarks>
    public void Contribute(IServiceProvider sessionServices)
    {
        if (sessionServices is null) return;

        var manager = _context.Root.GetService<PluginManager>();
        if (manager is null) return;

        var registry = sessionServices.GetService<MCPRegistry>();
        var workspaceRoot = sessionServices.GetService<SessionContextState>()?.WorkspaceRoot;

        foreach (var plugin in manager.GetPlugins())
        {
            if (plugin.State != PluginState.Enabled)
                continue;

            if (registry is not null)
            {
                foreach (var declaration in plugin.Manifest?.EnabledMcpServers ?? Enumerable.Empty<PluginMcpServerDeclaration>())
                {
                    if (registry.Contains(declaration.Name)) continue;

                    var client = PluginMcpCatalog.Create(
                        declaration.Name,
                        _context.Root,
                        plugin.Id,
                        plugin.Manifest?.Permissions);
                    if (client is null) continue;

                    registry.RegisterPluginClient(client);
                    Log($"会话回放插件 MCP：{declaration.Name}（插件 {plugin.Id}）");
                }
            }

            if (!string.IsNullOrWhiteSpace(workspaceRoot))
                EnsureManifestSkills(workspaceRoot!, plugin);
        }

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            EnsureRuntimeSkills(workspaceRoot!);
    }

    #endregion

    #region 宿主 UI 用扩展

    /// <summary>
    /// 取当前全部插件命令（本期为声明性契约，供管理器界面展示）。
    /// </summary>
    public IReadOnlyList<PluginCommandDescriptor> GetCommands() => _registry.GetCommands();

    #endregion

    #region 内部实现

    private static void RunOnUiThreadSync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Invoke(action);
    }

    private static void PostToUiCore(Action action)
    {
        if (action is null) return;

        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private void NotifySidebarEntriesChanged()
        => PostToUiCore(() => SidebarEntriesChanged?.Invoke(this, EventArgs.Empty));

    /// <summary>把一项插件 MCP 注册进指定会话的注册表（幂等）。</summary>
    /// <remarks>
    /// 插件身份取自当前权限环境（<see cref="PluginHostScope.RegisterMcpServer"/> 在委派前已进入），
    /// 使 MCP 客户端出口调用按插件声明判定越界（PRD §3.7）；无环境时按「未声明权限」处理（默认拒绝）。
    /// </remarks>
    private void TryRegisterMcpInSession(IServiceProvider sessionServices, string name)
    {
        try
        {
            var registry = sessionServices.GetService<MCPRegistry>();
            if (registry is null || registry.Contains(name)) return;

            var scope = PluginScopeContext.Current;
            var client = PluginMcpCatalog.Create(
                name,
                _context.Root,
                scope?.PluginId ?? string.Empty,
                scope?.Permissions);
            if (client is null) return;

            registry.RegisterPluginClient(client);
            _ = ConnectSafeAsync(client, name);
        }
        catch (Exception ex)
        {
            LogError($"注册插件 MCP {name} 失败", ex);
        }
    }

    private async Task ConnectSafeAsync(IMCPClient client, string name)
    {
        try
        {
            await client.ConnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError($"连接插件 MCP {name} 失败", ex);
        }
    }

    /// <summary>重建满足条件的会话 Agent；运行中的会话改为标记延迟重载，避免打断当前回合。</summary>
    private void ReloadAgents(Func<SessionRuntime, bool> predicate)
    {
        foreach (var runtime in _context.AllRuntimes())
        {
            if (!predicate(runtime)) continue;

            if (runtime.IsRunning)
                runtime.RequestReload();
            else
                _context.ResetAgent(runtime.SessionId);
        }
    }

    /// <summary>把指定工作区下所有会话的技能注册表重载，并重建其 Agent。</summary>
    private void ReloadSkillsFor(string workspaceRoot)
    {
        foreach (var runtime in _context.AllRuntimes())
        {
            if (!string.Equals(_context.WorkspaceRootOf(runtime), workspaceRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                _context.ResolveInSession<SkillRegistry>(runtime)?.LoadFromWorkspace(workspaceRoot);
            }
            catch (Exception ex)
            {
                LogError($"重载工作区技能失败（{workspaceRoot}）", ex);
            }

            if (runtime.IsRunning)
                runtime.RequestReload();
            else
                _context.ResetAgent(runtime.SessionId);
        }
    }

    /// <summary>补齐某插件清单声明的技能文件（仅当文件缺失时写入，避免每次构建都重写）。</summary>
    private void EnsureManifestSkills(string workspaceRoot, PluginDescriptor plugin)
    {
        foreach (var declaration in plugin.Manifest?.Skills ?? new List<SkillDefinition>())
        {
            if (string.IsNullOrWhiteSpace(declaration.Id)) continue;

            var skillFile = Path.Combine(workspaceRoot, ".luban-agent", "skills", declaration.Id, "SKILL.md");
            if (File.Exists(skillFile)) continue;

            if (!TryWriteSkillFile(workspaceRoot, declaration, declaration, plugin.Directory, out var error))
                LogError($"回放插件技能 {declaration.Id} 失败：{error}");
            else
            {
                _registry.TrackSkillRoot(declaration.Id, workspaceRoot);
                Log($"会话回放插件技能：{declaration.Id} → {Path.Combine(workspaceRoot, ".luban-agent", "skills", declaration.Id)}");
            }
        }
    }

    /// <summary>补齐本进程内插件运行期贡献的技能文件（仅当文件缺失时写入）。</summary>
    /// <remarks>
    /// 运行期技能（<c>host.RegisterSkill</c> / <c>IPlugin.ContributeSkills()</c>）不在任何清单里，
    /// 只存在于插件代码中；启用时若没有活动工作区，或其后切换了工作区，都必须由宿主按记账重新补齐。
    /// </remarks>
    private void EnsureRuntimeSkills(string workspaceRoot)
    {
        foreach (var record in _registry.GetRuntimeSkills())
        {
            var definition = record.Definition;
            if (string.IsNullOrWhiteSpace(definition.Id)) continue;

            var skillFile = Path.Combine(workspaceRoot, ".luban-agent", "skills", definition.Id, "SKILL.md");
            if (File.Exists(skillFile)) continue;

            if (!TryWriteSkillFile(workspaceRoot, definition, definition, record.SourceDirectory, out var error))
                LogError($"回放运行期插件技能 {definition.Id} 失败：{error}");
            else
            {
                _registry.TrackSkillRoot(definition.Id, workspaceRoot);
                Log($"会话回放运行期插件技能：{definition.Id} → {Path.Combine(workspaceRoot, ".luban-agent", "skills", definition.Id)}");
            }
        }
    }

    /// <summary>按技能定义反查来源插件目录（插件包内 <c>entry</c> 文件所在目录）。</summary>
    /// <remarks>
    /// <para>
    /// 技能有两条来源通道，解析必须都覆盖（PRD §4.10）：
    /// <list type="number">
    /// <item><b>清单声明</b>的技能 → 直接取声明它的插件目录（精确、权威）；</item>
    /// <item><b>运行期贡献</b>的技能（<c>host.RegisterSkill</c> / <c>ContributeSkills</c>）不在任何清单里，
    /// 退化到「在所有插件目录中按 <c>entry</c> 相对路径唯一匹配」；命中多个则拒绝猜测并告警。</item>
    /// </list>
    /// </para>
    /// </remarks>
    private string? ResolveSourceDirectory(SkillDefinition definition)
    {
        var manager = _context.Root.GetService<PluginManager>();
        if (manager is null) return null;

        var plugins = manager.GetPlugins();

        // ① 清单声明：唯一权威来源
        foreach (var plugin in plugins)
        {
            if (plugin.Manifest?.Skills?.Any(
                    s => string.Equals(s.Id, definition.Id, StringComparison.OrdinalIgnoreCase)) == true)
                return plugin.Directory;
        }

        // ② 运行期技能：按 entry 相对路径唯一匹配插件目录
        var entry = definition.Entry;
        if (string.IsNullOrWhiteSpace(entry)) return null;

        string? matched = null;
        foreach (var plugin in plugins)
        {
            if (!PluginEntryExists(plugin.Directory, entry)) continue;

            if (matched is not null)
            {
                LogError($"插件技能 {definition.Id} 的入口 {entry} 在多个插件目录中命中，已放弃猜测（仅写元信息）。");
                return null;
            }

            matched = plugin.Directory;
        }

        return matched;
    }

    /// <summary>
    /// 写入一项插件技能文件（格式与 <c>SkillManageToolGroup.WriteSkillAsync</c> 保持一致）。
    /// </summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <param name="definition">技能定义（提供 id / name / description / entry）。</param>
    /// <param name="declaration">清单声明（提供缺省值）。</param>
    /// <param name="pluginDirectory">插件目录（解析相对 entry 用，可为 <c>null</c>）。</param>
    /// <param name="error">失败原因。</param>
    private bool TryWriteSkillFile(
        string workspaceRoot,
        SkillDefinition definition,
        SkillDefinition declaration,
        string? pluginDirectory,
        out string error)
    {
        error = string.Empty;

        if (!SkillIdPattern.IsMatch(definition.Id))
        {
            error = $"Skill Id 不合法（需匹配 {SkillIdPattern}）";
            return false;
        }

        if (IsBuiltinSkill(definition.Id))
        {
            error = "与内置 Skill 同名，不可覆盖";
            return false;
        }

        var name = FirstNonEmpty(definition.Name, declaration.Name, definition.Id);
        var description = FirstNonEmpty(definition.Description, declaration.Description, string.Empty);
        var entry = definition.Entry ?? declaration.Entry;

        var content = TryReadPluginEntry(pluginDirectory, entry);
        if (content is null && !string.IsNullOrWhiteSpace(entry))
            LogError($"插件技能 {definition.Id} 的入口文件不可读：{entry}（已退化为仅写元信息）");

        var text = new StringBuilder()
            .AppendLine("---")
            .AppendLine($"name: {SanitizeFrontmatterValue(name)}")
            .AppendLine($"description: {SanitizeFrontmatterValue(description)}")
            .AppendLine("---")
            .AppendLine()
            .AppendLine((content ?? string.Empty).Trim())
            .ToString();

        var limit = _agentOptions.Value.Tools.SkillManage.MaxSkillFileBytes;
        if (Encoding.UTF8.GetByteCount(text) > limit)
        {
            error = $"内容超过上限 {limit / 1024}KB";
            return false;
        }

        try
        {
            var skillDir = Path.Combine(workspaceRoot, ".luban-agent", "skills", definition.Id);
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), text);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>读取插件包内的技能入口文件；路径越界或文件不存在时返回 <c>null</c>。</summary>
    private static string? TryReadPluginEntry(string? pluginDirectory, string? entry)
    {
        var path = ResolvePluginEntryPath(pluginDirectory, entry);
        if (path is null) return null;

        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>插件包内是否存在该入口文件（沿用同一套防穿越规则）。</summary>
    private static bool PluginEntryExists(string? pluginDirectory, string? entry)
    {
        var path = ResolvePluginEntryPath(pluginDirectory, entry);
        if (path is null) return false;

        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>解析插件包内入口文件的绝对路径；路径越界时返回 <c>null</c>（防目录穿越）。</summary>
    private static string? ResolvePluginEntryPath(string? pluginDirectory, string? entry)
    {
        if (string.IsNullOrWhiteSpace(pluginDirectory) || string.IsNullOrWhiteSpace(entry))
            return null;

        try
        {
            var root = Path.GetFullPath(pluginDirectory);
            var path = Path.GetFullPath(Path.Combine(root, entry));

            // 防目录穿越：入口必须落在插件目录内
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private bool IsBuiltinSkill(string skillId)
        => _context.Root.GetService<BuiltinSkillCatalog>()?.Get(skillId) is not null;

    /// <summary>技能 id 合法性正则，与框架 <c>SkillManageToolGroup</c> 保持一致。</summary>
    private static readonly System.Text.RegularExpressions.Regex SkillIdPattern =
        new(@"^[a-z0-9][a-z0-9_-]{0,63}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string SanitizeFrontmatterValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // 与 SkillManageToolGroup 一致：移除换行与冒号，避免破坏 YAML 结构
        return value.Replace("\r", " ").Replace("\n", " ").Replace(":", " -");
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value!;
        }

        return string.Empty;
    }

    #endregion
}