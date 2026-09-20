/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCli.App
*文件名： StartupWorkspaceResolver
*版本号： V1.0.0.0
*唯一标识：启动阶段工作区解析器
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/18
*描述：启动阶段解析默认工作区：取最近活跃非 RAG 工作区并恢复其最近会话；
*无可用工作区时弹出创建表单（路径/名称/AGENTS.md 描述），随后授权、设为当前并可选生成 AGENTS.md。
*调用位置：StartupDialog 关闭之后、RootView 创建之前（CLI 侧主导，向导不再处理工作区）
*
*****************************************************************************/
namespace LubanAgentCli.App;

/// <summary>
/// 启动阶段工作区解析器。
/// </summary>
internal static class StartupWorkspaceResolver
{
    /// <summary>
    /// 解析并进入工作区。无可用工作区时弹出创建表单；用户取消则返回 false（进入无工作区主界面）。
    /// </summary>
    /// <param name="services">根级 DI 容器。</param>
    /// <param name="ui">TUI 模态交互服务。</param>
    /// <param name="notices">启动提示集合（追加，供主界面横幅展示）。</param>
    /// <returns>是否已成功进入某个工作区。</returns>
    public static async Task<bool> ResolveAsync(IServiceProvider services, ITuiUiService ui, List<string> notices)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(notices);

        var workspaceManager = services.GetRequiredService<IWorkspaceManager>();

        var target = await FindLatestUsableAsync(workspaceManager);

        if (target is null)
        {
            target = await PromptCreateAsync(services, ui);
            if (target is null)
            {
                notices.Add("未选择工作区。请先在磁盘上创建目录，再用 /w -n <路径> 创建工作区后开始对话。");
                return false;
            }
        }
        else
        {
            notices.Add($"当前工作区: {target.Name} ({target.RootPath})");
        }

        // 授权（沿用启动向导设置的授权弹窗）
        var authorized = await workspaceManager.EnsureAuthorizedAsync(target);
        notices.Add(authorized ? "工作区已授权" : "工作区未授权，文件操作将受限");

        // 设置为当前工作区（内部恢复最近活跃会话，无则新建“新会话”）
        await workspaceManager.SetCurrentAsync(target.WorkspaceId);

        return true;
    }

    /// <summary>
    /// 取最近活跃、类型非 Rag 且根目录仍存在的工作区；仓储已按 LastActiveAt 倒序返回。
    /// </summary>
    private static async Task<WorkspaceInfo?> FindLatestUsableAsync(IWorkspaceManager workspaceManager)
    {
        var workspaces = await workspaceManager.GetUserWorkspacesAsync();
        foreach (var ws in workspaces)
        {
            if (string.Equals(ws.Type, "Rag", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Directory.Exists(ws.RootPath)) continue;
            return ws;
        }
        return null;
    }

    /// <summary>
    /// 循环弹出创建表单，直到成功创建或用户取消。
    /// </summary>
    private static async Task<WorkspaceInfo?> PromptCreateAsync(IServiceProvider services, ITuiUiService ui)
    {
        var workspaceManager = services.GetRequiredService<IWorkspaceManager>();
        var cwd = Directory.GetCurrentDirectory();

        while (true)
        {
            var values = ui.ShowForm("创建工作区", new[]
            {
                new FormField("路径", InitialValue: cwd),
                new FormField("名称（可空，默认取目录名）", Required: false),
                new FormField("AGENTS.md 创建描述（可空）", Required: false, Multiline: true)
            });

            if (values is null) return null; // 用户取消

            var path = values[0].Trim();
            if (!Directory.Exists(path))
            {
                ui.Notify("创建工作区", $"目录不存在: {path}");
                continue;
            }

            var name = values[1].Trim();
            var description = values[2].Trim();

            WorkspaceInfo created;
            try
            {
                created = await workspaceManager.CreateWorkspaceAsync(
                    path, string.IsNullOrWhiteSpace(name) ? null : name, type: "Normal");
            }
            catch (InvalidOperationException ex)
            {
                ui.Notify("创建工作区", ex.Message);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                await TryGenerateAgentsMdAsync(services, ui, created, description);
            }

            return created;
        }
    }

    /// <summary>
    /// 尝试生成 AGENTS.md：未选模型或已存在则跳过，失败不阻断（仅提示）。
    /// </summary>
    private static async Task TryGenerateAgentsMdAsync(
        IServiceProvider services, ITuiUiService ui, WorkspaceInfo workspace, string description)
    {
        var initializer = services.GetRequiredService<IAgentsMdInitializer>();
        var configManager = services.GetRequiredService<ConfigManager>();

        if (!configManager.HasSelectedModel)
        {
            ui.Notify("AGENTS.md", "未选择模型，已跳过 AGENTS.md 生成。可稍后用 /init 生成。");
            return;
        }

        if (initializer.ReadExisting(workspace) is not null)
        {
            ui.Notify("AGENTS.md", "工作区已存在 AGENTS.md，已保留原文件，跳过生成。");
            return;
        }

        var agentFactory = services.GetRequiredService<ILuBanAgentFactory>();
        var model = configManager.SelectedModel!;

        try
        {
            ui.RunWithProgress("生成 AGENTS.md", "正在生成 AGENTS.md...", async report =>
            {
                var prompt = initializer.BuildPrompt(new AgentsMdOptions
                {
                    AgentName = workspace.Name,
                    Description = description,
                    OutputMode = "full"
                });

                var agent = await agentFactory.CreateAsync(modelName: model);
                var response = await agent.RunAsync(prompt);
                var markdown = initializer.Normalize(response.Text ?? string.Empty);
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    throw new InvalidOperationException("生成结果为空");
                }

                await initializer.WriteAsync(workspace, markdown);
                report($"AGENTS.md 已生成（{markdown.Length} 字符）");
            });
        }
        catch (Exception ex)
        {
            Logger.Error("启动阶段生成 AGENTS.md 失败", ex);
            ui.Notify("AGENTS.md", $"生成失败，已跳过: {ex.Message}");
        }
    }
}