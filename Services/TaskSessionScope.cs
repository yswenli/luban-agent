/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net8.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgent.Services
*文件名： TaskSessionScope
*版本号： V1.0.0.0
*唯一标识：新建
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/7/31
*描述：任务会话作用域，负责任务根目录的确认与 PathGuard 的临时授权
*
*****************************************************************************/
namespace LubanAgent.Services;

/// <summary>
/// 任务会话作用域，负责任务根目录的确认与 PathGuard 的临时授权。
/// 在 /agi、/browse 等命令开始前检查当前工作目录是否已授权，
/// 若未授权则提示用户确认，并将该路径临时加入 PathGuard 的 AllowedRoots。
/// </summary>
public sealed class TaskSessionScope : IDisposable
{
    private readonly LuBanAgentOptions _options;
    private readonly List<string> _originalRoots;
    private readonly bool _modified;
    private bool _disposed;

    /// <summary>
    /// 本次任务确认的工作根目录（已规范化，以分隔符结尾）。空列表表示无限制。
    /// </summary>
    public IReadOnlyList<string> ConfirmedRoots { get; }

    /// <summary>
    /// 用户是否选择跳过根目录限制（全盘访问）。
    /// </summary>
    public bool IsUnrestricted { get; }

    private TaskSessionScope(LuBanAgentOptions options, bool modify, string? addRoot = null)
    {
        _options = options;
        _originalRoots = new List<string>(_options.Tools.FileSystem.AllowedRoots ?? new());

        if (!modify || addRoot == null)
        {
            // 无需修改：当前目录已授权或配置为不限制
            _modified = false;
            ConfirmedRoots = _originalRoots;
            IsUnrestricted = _originalRoots.Count == 0;
        }
        else
        {
            // 将新授权目录追加到现有列表（保留原有授权目录）
            _modified = true;
            var newRoots = new List<string>(_originalRoots) { addRoot };
            _options.Tools.FileSystem.AllowedRoots = newRoots;
            ConfirmedRoots = newRoots;
            IsUnrestricted = false;
        }
    }

    /// <summary>
    /// 交互式创建任务会话作用域。
    /// 自动检查当前工作目录是否已授权，已授权则直接通过；
    /// 未授权则提示用户确认是否授权访问当前目录。
    /// </summary>
    /// <param name="pathGuard">路径守卫实例，用于检查当前目录是否已授权。</param>
    /// <param name="options">LuBan Agent 配置选项。</param>
    /// <param name="commandName">命令名称（用于提示语）。</param>
    /// <returns>已确认的任务会话作用域。用户取消时返回 null。</returns>
    public static TaskSessionScope? CreateInteractive(
        PathGuard pathGuard,
        LuBanAgentOptions options,
        string commandName)
    {
        var cwd = Directory.GetCurrentDirectory();

        // 当前工作目录已授权（或配置为不限制），无需提示
        if (pathGuard.IsAllowed(cwd))
        {
            AnsiConsole.MarkupLine($"[grey]当前工作目录: {Markup.Escape(cwd)}[/]");
            return new TaskSessionScope(options, modify: false);
        }

        // 当前工作目录未授权，提示用户确认
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[cyan]═══ 任务根目录确认 ═══[/]");
        AnsiConsole.MarkupLine($"[grey]命令: {Markup.Escape(commandName)}[/]");
        AnsiConsole.MarkupLine("[yellow]⚠️  检测到当前工作目录未在授权列表中:[/]");
        AnsiConsole.MarkupLine($"[yellow]  {Markup.Escape(cwd)}[/]");
        AnsiConsole.WriteLine();

        var confirm = AnsiConsole.Confirm("[yellow]是否授权访问此目录？[/]", defaultValue: true);
        if (!confirm)
        {
            AnsiConsole.MarkupLine("[red]✗ 已取消任务（当前目录未授权）[/]");
            return null;
        }

        // 规范化当前目录路径
        string normalizedCwd;
        try
        {
            normalizedCwd = Path.GetFullPath(cwd);
            if (!normalizedCwd.EndsWith(Path.DirectorySeparatorChar))
                normalizedCwd += Path.DirectorySeparatorChar;
        }
        catch
        {
            AnsiConsole.MarkupLine("[red]✗ 无法解析当前目录路径，任务已取消[/]");
            return null;
        }

        AnsiConsole.MarkupLine($"[green]✓ 已授权当前工作目录: {Markup.Escape(normalizedCwd)}[/]");
        return new TaskSessionScope(options, modify: true, addRoot: normalizedCwd);
    }

    /// <summary>
    /// 恢复 PathGuard 的原始 AllowedRoots 配置。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_modified)
        {
            _options.Tools.FileSystem.AllowedRoots = _originalRoots;
        }
    }
}
