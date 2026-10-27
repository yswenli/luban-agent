/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*命名空间：LubanAgentCodex.ViewModels.Settings
*文件名： A2aTabViewModel
*版本号： V1.0.0.0
*唯一标识：新建
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/27
*描述：设置窗「A2A 协作」页业务（服务端/客户端条目、读取、新建、保存、删除）
*
*****************************************************************************/

namespace LubanAgentCodex.ViewModels.Settings;

/// <summary>
/// A2A 协作页 ViewModel：工作区文件（<c>.luban-agent/a2a</c>）语义由 <see cref="A2AWorkspaceConfigStore"/> 承载。
/// </summary>
public sealed class A2aTabViewModel
{
    /// <summary>
    /// 列出当前工作区的 A2A 条目（服务端 + 各客户端）。
    /// </summary>
    /// <param name="workspaceRoot">工作区根路径；null 表示全局作用域（无条目）。</param>
    public IReadOnlyList<SettingsListItem> BuildList(string? workspaceRoot)
    {
        var list = new List<SettingsListItem>();
        if (string.IsNullOrWhiteSpace(workspaceRoot)) return list;

        var root = workspaceRoot;
        if (A2AWorkspaceConfigStore.ServerExists(root))
            list.Add(new SettingsListItem { Key = "server", Display = "服务端   server.json" });

        foreach (var (fileName, cfg) in A2AWorkspaceConfigStore.LoadClients(root))
        {
            var name = string.IsNullOrWhiteSpace(cfg.Name)
                ? Path.GetFileNameWithoutExtension(fileName)
                : cfg.Name!;
            list.Add(new SettingsListItem { Key = fileName, Display = $"客户端   {name}" });
        }

        return list;
    }

    /// <summary>读取服务端配置；文件不存在返回 null。</summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    public A2AWorkspaceFile? ReadServer(string workspaceRoot)
        => A2AWorkspaceConfigStore.LoadServer(workspaceRoot);

    /// <summary>读取客户端配置；文件不存在返回 null。</summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <param name="fileName">客户端文件名。</param>
    public A2AWorkspaceFile? ReadClient(string workspaceRoot, string fileName)
        => A2AWorkspaceConfigStore.LoadClients(workspaceRoot)
            .FirstOrDefault(x => string.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            .Config;

    /// <summary>
    /// 新建默认条目：无服务端时先建 server.json，否则建一个新的客户端文件。
    /// </summary>
    /// <param name="workspaceRoot">工作区根路径；null 表示全局作用域（不允许）。</param>
    public SettingsOutcome CreateDefault(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return SettingsOutcome.Failure("A2A 配置仅作用于工作区，请先在顶栏选择一个工作区。");

        var root = workspaceRoot;
        if (!A2AWorkspaceConfigStore.ServerExists(root))
        {
            A2AWorkspaceConfigStore.SaveServer(root, new A2AWorkspaceFile
            {
                Host = "127.0.0.1",
                Port = 0,
                Path = "/a2a",
                RequireApiKey = true,
                ApiKeys = new List<string>(),
                AllowedWebhookDomains = new List<string>(),
            });
            return SettingsOutcome.Success("已新建服务端配置 server.json，请填写后点击「保存」。", "server");
        }

        var i = 1;
        var fileName = A2AWorkspaceConfigStore.BuildClientFileName("remote");
        while (File.Exists(Path.Combine(A2AWorkspaceConfigStore.ResolveDir(root), fileName)))
            fileName = A2AWorkspaceConfigStore.BuildClientFileName($"remote{i++}");

        A2AWorkspaceConfigStore.SaveClient(root, fileName, new A2AWorkspaceFile
        {
            Mode = "client",
            Name = Path.GetFileNameWithoutExtension(fileName),
        });

        return SettingsOutcome.Success("已新建客户端配置，请填写 Name / Endpoint 后点击「保存」。", fileName);
    }

    /// <summary>
    /// 保存服务端配置。
    /// </summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <param name="form">表单内容。</param>
    public SettingsOutcome SaveServer(string workspaceRoot, A2aServerForm form)
    {
        var apiKeys = form.ApiKeysText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        _ = int.TryParse(form.PortText.Trim(), out var port);

        var cfg = A2AWorkspaceConfigStore.LoadServer(workspaceRoot) ?? new A2AWorkspaceFile();
        cfg.Mode = "server";
        cfg.Host = string.IsNullOrWhiteSpace(form.Host) ? "127.0.0.1" : form.Host.Trim();
        cfg.Port = port;
        cfg.Path = string.IsNullOrWhiteSpace(form.Path) ? "/a2a" : form.Path.Trim();
        cfg.RequireApiKey = form.RequireApiKey;
        cfg.ApiKeys = apiKeys;
        cfg.AgentName = form.AgentName.Trim();
        cfg.AgentDescription = form.AgentDescription.Trim();
        cfg.AgentVersion = string.IsNullOrWhiteSpace(form.AgentVersion) ? "1.0.0" : form.AgentVersion.Trim();

        A2AWorkspaceConfigStore.SaveServer(workspaceRoot, cfg);
        return SettingsOutcome.Success(key: "server");
    }

    /// <summary>
    /// 保存客户端配置（改名时同步删除旧文件）。
    /// </summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <param name="oldKey">原客户端文件名；新建时为 null/空。</param>
    /// <param name="form">表单内容。</param>
    public SettingsOutcome SaveClient(string workspaceRoot, string? oldKey, A2aClientForm form)
    {
        var name = form.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return SettingsOutcome.Failure("客户端的 Name 不能为空。");

        var endpoint = form.Endpoint.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
            return SettingsOutcome.Failure("客户端的 Endpoint 不能为空。");

        var targetFile = A2AWorkspaceConfigStore.BuildClientFileName(name);
        var dir = A2AWorkspaceConfigStore.ResolveDir(workspaceRoot);
        if (!string.IsNullOrEmpty(oldKey) && !string.Equals(oldKey, targetFile, StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(dir, targetFile)))
        {
            return SettingsOutcome.Failure($"已存在同名客户端文件 {targetFile}，请更换 Name。");
        }

        var client = A2AWorkspaceConfigStore.LoadClients(workspaceRoot)
            .FirstOrDefault(x => string.Equals(x.FileName, oldKey, StringComparison.OrdinalIgnoreCase)).Config
            ?? new A2AWorkspaceFile();

        client.Mode = "client";
        client.Name = name;
        client.Endpoint = endpoint;
        client.ApiKey = string.IsNullOrWhiteSpace(form.ApiKey) ? null : form.ApiKey.Trim();
        client.RegisterAsSubAgent = form.RegisterAsSubAgent;
        client.RemoteAgentName = string.IsNullOrWhiteSpace(form.RemoteAgentName) ? null : form.RemoteAgentName.Trim();
        client.AllowAll = form.AllowAll;

        A2AWorkspaceConfigStore.SaveClient(workspaceRoot, targetFile, client);
        if (!string.IsNullOrEmpty(oldKey) && !string.Equals(oldKey, targetFile, StringComparison.OrdinalIgnoreCase))
            A2AWorkspaceConfigStore.DeleteFile(workspaceRoot, oldKey);

        return SettingsOutcome.Success(key: targetFile);
    }

    /// <summary>
    /// 删除 A2A 条目（服务端固定文件名 <c>server.json</c>）。
    /// </summary>
    /// <param name="workspaceRoot">工作区根路径。</param>
    /// <param name="key">条目键（<c>server</c> 或客户端文件名）。</param>
    public SettingsOutcome Delete(string workspaceRoot, string key)
    {
        try
        {
            A2AWorkspaceConfigStore.DeleteFile(workspaceRoot, key == "server" ? "server.json" : key);
        }
        catch (Exception ex)
        {
            return SettingsOutcome.Failure($"删除失败：{ex.Message}");
        }

        return SettingsOutcome.Success("已删除该 A2A 配置。");
    }
}