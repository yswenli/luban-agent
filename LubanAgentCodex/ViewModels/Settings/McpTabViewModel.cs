/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.ViewModels.Settings
*文件名： McpTabViewModel
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗「MCP 服务」页业务（列表、读取、保存、新建、删除）
*
*****************************************************************************/

namespace LubanAgentCodex.ViewModels.Settings;

/// <summary>
/// MCP 服务页 ViewModel：只做校验与转发，所有提示文案与原设置窗保持一致。
/// </summary>
/// <param name="services">宿主服务提供者（为 null 时视为容器不可用，操作返回失败文案）。</param>
public sealed class McpTabViewModel(DiContainer? services)
{
    private IMcpManagementService? Mcps => services?.GetService<IMcpManagementService>();

    /// <summary>容器是否可用。</summary>
    public bool Available => Mcps != null;

    /// <summary>列出当前作用域下的 MCP 服务名。</summary>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public IReadOnlyList<string> ListFileIds(string? scopeRoot)
        => Mcps?.ListFileIds(scopeRoot).ToList() ?? [];

    /// <summary>条目是否已存在。</summary>
    /// <param name="name">服务名。</param>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public bool Exists(string name, string? scopeRoot)
        => ListFileIds(scopeRoot).Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>读取 MCP 配置；不存在返回 null。</summary>
    /// <param name="key">服务名。</param>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public McpServerConfig? Read(string key, string? scopeRoot)
        => Mcps?.ReadFile(key, scopeRoot);

    /// <summary>
    /// 保存 MCP 服务（含 Name 必填校验）。
    /// </summary>
    /// <param name="oldKey">原条目键；新建时为 null。</param>
    /// <param name="form">表单内容。</param>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public SettingsOutcome Save(string? oldKey, McpForm form, string? scopeRoot)
    {
        var name = form.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return SettingsOutcome.Failure("MCP 服务的 Name 不能为空。");

        var cfg = new McpServerConfig
        {
            Name = name,
            Description = form.Description.Trim(),
            Transport = string.IsNullOrWhiteSpace(form.Transport) ? "stdio" : form.Transport,
            Command = form.Command.Trim(),
            Args = McpArgsParser.Parse(form.ArgsText),
            Enabled = form.Enabled,
        };

        try
        {
            (Mcps ?? throw new InvalidOperationException("MCP 管理服务不可用"))
                .SaveFile(oldKey, cfg, scopeRoot);
        }
        catch (Exception ex)
        {
            return SettingsOutcome.Failure($"保存失败：{ex.Message}");
        }

        return SettingsOutcome.Success(key: name);
    }

    /// <summary>
    /// 新建一条默认 MCP 服务（自动避让重名）。
    /// </summary>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public SettingsOutcome CreateDefault(string? scopeRoot)
    {
        const string baseName = "new-mcp";
        var name = baseName;
        var i = 1;
        while (Exists(name, scopeRoot)) name = $"{baseName}-{i++}";

        try
        {
            (Mcps ?? throw new InvalidOperationException("MCP 管理服务不可用"))
                .SaveFile(null, new McpServerConfig { Name = name }, scopeRoot);
        }
        catch (Exception ex)
        {
            return SettingsOutcome.Failure($"新建失败：{ex.Message}");
        }

        return SettingsOutcome.Success("已新建条目，请填写内容后点击「保存」。", name);
    }

    /// <summary>
    /// 删除 MCP 服务（文件来源）。
    /// </summary>
    /// <param name="key">服务名。</param>
    /// <param name="scopeRoot">作用区根目录（null=全局）。</param>
    public async Task<SettingsOutcome> DeleteAsync(string key, string? scopeRoot)
    {
        try
        {
            await (Mcps ?? throw new InvalidOperationException("MCP 管理服务不可用"))
                .DeleteAsync(key, ManagedOrigin.File, scopeRoot);
        }
        catch (Exception ex)
        {
            return SettingsOutcome.Failure($"删除失败：{ex.Message}");
        }

        return SettingsOutcome.Success("已删除该条目。");
    }
}