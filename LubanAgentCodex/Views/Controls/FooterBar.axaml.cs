/****************************************************************************
*Copyright @ yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*Author：yswenli
*命名空间：LubanAgentCodex.Views.Controls
*文件名： FooterBar
*版本号： V1.0.0.0
*唯一标识：页脚状态栏控件
*当前的用户域：WALLE
*创建人：yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/8/20
*描述：页脚状态栏控件，显示权限模式、工作目录、Git分支、Token用量
*
*****************************************************************************/
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LuBan.AIAgent.Abstractions;

namespace LubanAgentCodex.Views.Controls;

/// <summary>
/// 页脚状态栏控件
/// </summary>
public partial class FooterBar : UserControl
{
    private TextBlock? _permissionModeText;
    private TextBlock? _workingDirectoryText;
    private TextBlock? _gitBranchText;
    private TextBlock? _tokenUsageText;
    
    public FooterBar()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _permissionModeText = this.FindControl<TextBlock>("PermissionModeText");
        _workingDirectoryText = this.FindControl<TextBlock>("WorkingDirectoryText");
        _gitBranchText = this.FindControl<TextBlock>("GitBranchText");
        _tokenUsageText = this.FindControl<TextBlock>("TokenUsageText");
    }
    
    /// <summary>
    /// 更新权限模式显示
    /// </summary>
    public void UpdatePermissionMode(ToolPermissionMode mode)
    {
        if (_permissionModeText == null) return;
        
        _permissionModeText.Text = mode switch
        {
            ToolPermissionMode.Default => "[default]",
            ToolPermissionMode.Plan => "[plan]",
            ToolPermissionMode.AcceptEdits => "[accept-edits]",
            ToolPermissionMode.BypassPermissions => "[bypass]",
            _ => "[unknown]"
        };
        
        _permissionModeText.Foreground = mode switch
        {
            ToolPermissionMode.Default => Brush.Parse("#FFFFFF"),
            ToolPermissionMode.Plan => Brush.Parse("#AFA9EC"),
            ToolPermissionMode.AcceptEdits => Brush.Parse("#85B7EB"),
            ToolPermissionMode.BypassPermissions => Brush.Parse("#F09595"),
            _ => Brush.Parse("#FFFFFF")
        };
    }
    
    /// <summary>
    /// 更新工作目录显示
    /// </summary>
    public void UpdateWorkingDirectory(string path)
    {
        if (_workingDirectoryText == null) return;
        
        // 简化路径，只显示最后两段
        var parts = path.Split(Path.DirectorySeparatorChar);
        var display = parts.Length > 2 
            ? $"{parts[^2]}{Path.DirectorySeparatorChar}{parts[^1]}"
            : path;
        
        _workingDirectoryText.Text = display;
    }
    
    /// <summary>
    /// 更新 Git 分支显示
    /// </summary>
    public void UpdateGitBranch(string branch)
    {
        if (_gitBranchText == null) return;
        _gitBranchText.Text = $"git:{branch}";
    }
    
    /// <summary>
    /// 更新 Token 用量显示
    /// </summary>
    public void UpdateTokenUsage(long tokens)
    {
        if (_tokenUsageText == null) return;
        
        var display = tokens > 1000 
            ? $"{tokens / 1000.0:F1}k tok"
            : $"{tokens} tok";
        
        _tokenUsageText.Text = display;
    }
}
