namespace LubanAgentCli.App.Services;

/// <summary>
/// 窗口标题服务。根据工作区、模型、会话状态动态生成标题。
/// </summary>
internal sealed class TitleService
{
    private string _workspaceName = "";
    private string _gitBranch = "";
    private string _modelName = "";
    private string _sessionTitle = "";

    /// <summary>标题变更事件。</summary>
    public event Action<string>? TitleChanged;

    /// <summary>设置工作区名称。</summary>
    public void SetWorkspace(string name)
    {
        _workspaceName = name ?? "";
        UpdateTitle();
    }

    /// <summary>设置 Git 分支。</summary>
    public void SetGitBranch(string branch)
    {
        _gitBranch = branch ?? "";
        UpdateTitle();
    }

    /// <summary>设置模型名称。</summary>
    public void SetModel(string model)
    {
        _modelName = model ?? "";
        UpdateTitle();
    }

    /// <summary>设置会话标题。</summary>
    public void SetSessionTitle(string title)
    {
        _sessionTitle = title ?? "";
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(_sessionTitle))
        {
            parts.Add(_sessionTitle);
        }

        if (!string.IsNullOrEmpty(_workspaceName))
        {
            parts.Add(_workspaceName);
        }

        if (!string.IsNullOrEmpty(_gitBranch) && _gitBranch != "—")
        {
            parts.Add($"({_gitBranch})");
        }

        if (!string.IsNullOrEmpty(_modelName))
        {
            parts.Add($"[{_modelName}]");
        }

        var title = parts.Count > 0
            ? string.Join(" ", parts) + " — LubanAgent"
            : "LubanAgent";

        TitleChanged?.Invoke(title);
    }
}